using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using ReplayMedia = AuraToolsExp.Dll.Features.MatchRecords.Media;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.LegacyMigration;

internal sealed class ReplayLegacyMigrationReport
{
    public string MigrationId { get; set; } = "";
    public string ScannedUtc { get; set; } = "";
    public string DatabasePath { get; set; } = "";
    public List<ReplayLegacyMigrationItem> Records { get; set; } = new();
    public List<string> FilesToDelete { get; set; } = new();
    public List<string> FilesToQuarantine { get; set; } = new();
    public int ChunkRowsToDelete { get; set; }
    public long ChunkBytesToDelete { get; set; }
    public bool StatisticsWillBeDeleted { get; set; }
}

internal sealed class ReplayLegacyMigrationItem
{
    public string RecordId { get; set; } = "";
    public int ReplayProtocol { get; set; }
    public string Classification { get; set; } = "";
    public string Reason { get; set; } = "";
    public int ChunkCount { get; set; }
    public long ChunkBytes { get; set; }
    public int EventCount { get; set; }
    public int TurnCount { get; set; }
    public bool StatisticsPreserved { get; set; } = true;
    public List<string> MissingFacts { get; set; } = new();
    public List<string> MediaFiles { get; set; } = new();
    public List<ReplayLegacyMediaItem> Media { get; set; } = new();
}

internal sealed class ReplayLegacyMediaItem
{
    public string MediaId { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Format { get; set; } = "";
    public string Path { get; set; } = "";
    public string PlannedAction { get; set; } = "TranscodeOrValidateToMp4";
}

internal static class ReplayLegacyMigrationService
{
    private static string latestReportPath = "";

    internal static string LatestReportPath => latestReportPath;

    internal static ReplayLegacyMigrationReport Scan()
    {
        var database = MatchRecordStorage.Database;
        var report = new ReplayLegacyMigrationReport
        {
            MigrationId = Guid.NewGuid().ToString("N"),
            ScannedUtc = DateTime.UtcNow.ToString("O"),
            DatabasePath = database.DatabasePath,
            StatisticsWillBeDeleted = false
        };
        var knownMedia = new HashSet<string>(
            database.LoadAllMediaPaths()
                .Select(ReplayMedia.MatchReplayMediaStore.ResolvePath)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var recordId in database.LoadLegacyReplayIds())
        {
            var record = database.Get(recordId);
            if (record == null) continue;
            var item = new ReplayLegacyMigrationItem
            {
                RecordId = record.RecordId,
                ReplayProtocol = record.ReplayProtocol,
                TurnCount = record.TurnCount
            };
            try
            {
                var chunks = database.LoadChunks(recordId).OrderBy(value => value.ChunkIndex).ToList();
                item.ChunkCount = chunks.Count;
                item.ChunkBytes = chunks.Sum(value => (long)(value.Payload?.Length ?? 0));
                report.ChunkRowsToDelete += chunks.Count;
                report.ChunkBytesToDelete += item.ChunkBytes;
                var events = MatchReplayChunker.Decode(chunks);
                item.EventCount = events.Count;
                Classify(record, events, item);
            }
            catch (Exception ex)
            {
                item.Classification = "Corrupt";
                item.Reason = "旧 chunks 无法完整验证：" + ex.Message;
                item.MissingFacts.Add("validated-event-stream");
            }
            foreach (var media in database.LoadMedia(recordId))
            {
                var path = ReplayMedia.MatchReplayMediaStore.ResolvePath(media.FilePath);
                item.MediaFiles.Add(path);
                item.Media.Add(new ReplayLegacyMediaItem
                {
                    MediaId = media.MediaId,
                    RecordId = recordId,
                    Format = media.Format,
                    Path = path
                });
                knownMedia.Add(Path.GetFullPath(path));
            }
            report.Records.Add(item);
        }

        ScanOrphans(report, knownMedia);
        var directory = Path.Combine(MatchRecordStorage.RootDirectory, "MigrationReports");
        Directory.CreateDirectory(directory);
        var pathValue = Path.Combine(directory, "replay-v10-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                                                   + "-" + report.MigrationId.Substring(0, 8) + ".json");
        var payload = ReplayCanonicalJsonV10.SerializeUtf8(report);
        File.WriteAllBytes(pathValue, payload);
        latestReportPath = pathValue;
        database.SaveMigrationScan(
            report.MigrationId,
            Relative(pathValue),
            ReplayCanonicalJsonV10.Sha256(payload),
            report.Records.Count,
            report.ChunkBytesToDelete);
        return report;
    }

    internal static ReplayLegacyMigrationReport ApplyLatest()
    {
        var path = ResolveLatestReport();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("尚未生成 v8/v9 迁移扫描报告。", path);
        }
        var payload = File.ReadAllBytes(path);
        var report = AuraSharedJson.Deserialize<ReplayLegacyMigrationReport>(Encoding.UTF8.GetString(payload))
                     ?? throw new InvalidDataException("迁移扫描报告无法读取。");
        if (!MatchRecordStorage.Database.ValidateMigrationScan(
                report.MigrationId,
                ReplayCanonicalJsonV10.Sha256(payload)))
        {
            throw new InvalidDataException("迁移扫描报告已被修改、已执行或不属于当前数据库。");
        }
        if (!string.Equals(
                Path.GetFullPath(report.DatabasePath),
                Path.GetFullPath(MatchRecordStorage.Database.DatabasePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("迁移报告对应的数据库与当前数据库不一致。");
        }
        if (report.StatisticsWillBeDeleted)
        {
            throw new InvalidDataException("迁移报告请求删除统计，当前安全迁移器拒绝执行。");
        }
        var currentIds = MatchRecordStorage.Database.LoadLegacyReplayIds().OrderBy(item => item, StringComparer.Ordinal).ToList();
        var reportIds = report.Records.Select(item => item.RecordId).OrderBy(item => item, StringComparer.Ordinal).ToList();
        if (!currentIds.SequenceEqual(reportIds, StringComparer.Ordinal))
        {
            throw new InvalidDataException("旧回放集合在扫描后发生变化，请重新扫描再确认清理。");
        }

        foreach (var media in report.Records.SelectMany(item => item.Media))
        {
            try
            {
                ReplayMedia.ReplayLegacyMediaTranscoder.TranscodeOrValidateAndImport(media.RecordId, media.Path);
                ReplayMedia.MatchReplayMediaStore.Delete(media.MediaId);
            }
            catch
            {
                Quarantine(media.Path, report.MigrationId);
                MatchRecordStorage.Database.DeleteMedia(media.MediaId);
            }
        }
        MatchRecordStorage.Database.ApplyLegacyReplayCleanup(reportIds, report.MigrationId);
        foreach (var file in report.FilesToDelete.Distinct(StringComparer.OrdinalIgnoreCase)) SafeDelete(file);
        foreach (var file in report.FilesToQuarantine.Distinct(StringComparer.OrdinalIgnoreCase)) Quarantine(file, report.MigrationId);
        CleanupEmptyOwnedDirectories();
        return report;
    }

    private static void Classify(
        MatchRecord record,
        IReadOnlyList<MatchReplayEvent> events,
        ReplayLegacyMigrationItem item)
    {
        var baseline = record.InitialState?.BaselineState;
        if (record.ReplayProtocol is not (8 or 9))
        {
            item.Classification = "SummaryOnly";
            item.Reason = "v7 及更早记录不执行命令回放。";
            item.MissingFacts.Add("authoritative-v10-events");
            return;
        }
        if (baseline == null)
        {
            item.Classification = "SummaryOnly";
            item.Reason = "缺少初始权威状态。";
            item.MissingFacts.Add("initial-logical-state");
            return;
        }
        if (!events.Any(value => value.ActionFrame != null))
        {
            item.Classification = "SummaryOnly";
            item.Reason = "缺少权威动作帧。";
            item.MissingFacts.Add("authoritative-action-frames");
            return;
        }

        // v8/v9 status snapshots only carry runtime instance ids. They do not bind every
        // actor to an owner-qualified content id and do not embed the referenced bytes.
        item.Classification = "RequiresOriginalArchive";
        item.Reason = "状态和动作可读，但敌人/角色内容标识及必需附件没有可验证原始字节。";
        item.MissingFacts.Add("owner-qualified-actor-content-id");
        item.MissingFacts.Add("content-addressed-presentation-assets");
    }

    private static void ScanOrphans(ReplayLegacyMigrationReport report, ISet<string> knownMedia)
    {
        foreach (var directory in new[] { MatchRecordStorage.MediaDirectory, MatchRecordStorage.TemporaryDirectory })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(path);
                if (knownMedia.Contains(full)) continue;
                var extension = Path.GetExtension(full).ToLowerInvariant();
                if (extension is ".avi" or ".wav" or ".spool" or ".jpg" or ".jpeg"
                    || full.EndsWith(".tmp.mp4", StringComparison.OrdinalIgnoreCase))
                {
                    report.FilesToDelete.Add(full);
                }
                else if (extension is ".mp4" or ".mov" or ".m4v" or ".webm")
                {
                    report.FilesToQuarantine.Add(full);
                }
            }
        }
        if (Directory.Exists(MatchRecordStorage.ImportsDirectory))
        {
            foreach (var path in Directory.GetFiles(MatchRecordStorage.ImportsDirectory, "*", SearchOption.AllDirectories))
            {
                report.FilesToQuarantine.Add(Path.GetFullPath(path));
            }
        }
        if (Directory.Exists(MatchRecordStorage.ExportsDirectory))
        {
            foreach (var path in Directory.GetFiles(MatchRecordStorage.ExportsDirectory, "*.aurareplay", SearchOption.TopDirectoryOnly))
            {
                if (IsLegacyReplayPackage(path)) report.FilesToQuarantine.Add(Path.GetFullPath(path));
            }
        }
        var exportJobs = Path.Combine(MatchRecordStorage.RootDirectory, "ExportJobs");
        if (Directory.Exists(exportJobs))
        {
            foreach (var path in Directory.GetFiles(exportJobs, "*", SearchOption.AllDirectories))
            {
                report.FilesToDelete.Add(Path.GetFullPath(path));
            }
        }
    }

    private static string ResolveLatestReport()
    {
        if (!string.IsNullOrWhiteSpace(latestReportPath) && File.Exists(latestReportPath)) return latestReportPath;
        var directory = Path.Combine(MatchRecordStorage.RootDirectory, "MigrationReports");
        latestReportPath = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "replay-v10-*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(item => item, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? ""
            : "";
        return latestReportPath;
    }

    private static void SafeDelete(string path)
    {
        if (!TryResolveOwned(path, out var full) || !File.Exists(full)) return;
        File.Delete(full);
    }

    private static void Quarantine(string path, string migrationId)
    {
        if (!TryResolveOwned(path, out var full) || !File.Exists(full)) return;
        var directory = Path.Combine(MatchRecordStorage.RootDirectory, "Quarantine", "LegacyMedia", migrationId);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(full));
        if (File.Exists(target)) target = target + "." + Guid.NewGuid().ToString("N").Substring(0, 8);
        File.Move(full, target);
    }

    private static bool TryResolveOwned(string path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        var root = Path.GetFullPath(MatchRecordStorage.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Relative(string path)
    {
        var root = Path.GetFullPath(MatchRecordStorage.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/')
            : full;
    }

    private static void CleanupEmptyOwnedDirectories()
    {
        foreach (var root in new[]
                 {
                     MatchRecordStorage.TemporaryDirectory,
                     MatchRecordStorage.ImportsDirectory,
                     MatchRecordStorage.MediaDirectory,
                     Path.Combine(MatchRecordStorage.RootDirectory, "ExportJobs")
                 })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(item => item.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch
                {
                }
            }
        }
    }

    private static bool IsLegacyReplayPackage(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.GetEntry("manifest.json");
            if (entry == null) return true;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var manifest = AuraSharedJson.Deserialize<ReplayPackageManifestV10>(reader.ReadToEnd());
            return manifest?.PackageVersion != ReplayProtocolV10.PackageVersion;
        }
        catch
        {
            return true;
        }
    }
}
