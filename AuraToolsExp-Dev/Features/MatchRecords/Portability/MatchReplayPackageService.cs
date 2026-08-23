using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Portability;

internal static class MatchReplayPackageService
{
    private const long MaximumEntryBytes = 512L * 1024L * 1024L;
    private const long MaximumPackageBytes = 2L * 1024L * 1024L * 1024L;
    private const int MaximumEntries = 12_000;

    internal static string Export(string recordId)
    {
        var document = MatchRecordStorage.Database.LoadV11(recordId, loadAttachmentPayloads: true)
                       ?? throw new InvalidOperationException("找不到经过验证的 Replay Document v11。");
        var record = MatchRecordStorage.Database.Get(recordId)
                     ?? throw new InvalidOperationException("找不到回放对应的对局摘要。");
        var analysis = MatchRecordStorage.Database.GetAnalysis(recordId)
                       ?? MatchAnalysisBuilder.BuildV11(record, document);
        var chunks = ReplayTimelineChunkerV11.Build(document.Events);
        var skeleton = Clone(document);
        skeleton.Events.Clear();
        skeleton.Checkpoints.Clear();
        foreach (var attachment in skeleton.Attachments) attachment.Payload = Array.Empty<byte>();
        var manifest = new ReplayPackageManifestV11
        {
            ExportedUtc = DateTime.UtcNow.ToString("O"),
            RecordId = recordId,
            DocumentSha256 = document.Header.DocumentSha256
        };
        var payloads = new Dictionary<string, (string Kind, byte[] Payload)>(StringComparer.Ordinal)
        {
            ["document.json.gz"] = ("Document", ReplayPayloadV11.Encode(skeleton)),
            ["analysis/summary.json.gz"] = ("Analysis", ReplayPayloadV11.Encode(analysis))
        };
        foreach (var chunk in chunks)
        {
            payloads["timeline/" + chunk.ChunkIndex.ToString("D6") + ".json.gz"] = ("Timeline", chunk.Payload);
        }
        for (var index = 0; index < document.Checkpoints.Count; index++)
        {
            payloads["checkpoints/" + index.ToString("D6") + ".json.gz"] =
                ("Checkpoint", ReplayPayloadV11.Encode(document.Checkpoints[index]));
        }
        foreach (var attachment in document.Attachments)
        {
            if (attachment.Payload.Length == 0)
            {
                throw new InvalidDataException("回放附件无法读取：" + attachment.Sha256);
            }
            payloads["attachments/" + attachment.Sha256.ToLowerInvariant() + NormalizeExtension(attachment.Extension)] =
                ("Attachment", attachment.Payload);
        }

        foreach (var pair in payloads.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            manifest.Entries.Add(new ReplayPackageEntryV11
            {
                Path = pair.Key,
                Kind = pair.Value.Kind,
                ByteLength = pair.Value.Payload.LongLength,
                LogicalByteLength = pair.Value.Payload.LongLength,
                Sha256 = ReplayCanonicalJsonV11.Sha256(pair.Value.Payload)
            });
        }

        var name = SafeName(record.LevelId) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".aurareplay";
        var output = UniquePath(Path.Combine(MatchRecordStorage.ExportsDirectory, name));
        using var transaction = AuraSharedFileStore.BeginWrite(
            AuraToolsIds.ModId,
            output,
            overwrite: false);
        using (var archive = new ZipArchive(
                   transaction.Stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true,
                   Encoding.UTF8))
        {
            WriteEntry(archive, "manifest.json", ReplayCanonicalJsonV11.SerializeUtf8(manifest));
            foreach (var pair in payloads.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                WriteEntry(archive, pair.Key, pair.Value.Payload);
            }
        }
        transaction.Stream.Flush();
        transaction.Stream.Position = 0;
        using (var verifyArchive = new ZipArchive(
                   transaction.Stream,
                   ZipArchiveMode.Read,
                   leaveOpen: true,
                   Encoding.UTF8))
        {
            ReadAndValidate(verifyArchive);
        }
        transaction.Commit();
        return output;
    }

    internal static MatchRecord Import(string packagePath)
    {
        using var file = OpenPackage(packagePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var parsed = ReadAndValidate(archive);
        var document = parsed.Document;
        if (MatchRecordStorage.Database.ContainsContentHash(document.Header.DocumentSha256))
        {
            throw new InvalidDataException("相同内容的 v11 回放已经存在。");
        }
        if (MatchRecordStorage.Database.Get(document.Header.RecordId) != null)
        {
            document.Header.RecordId = Guid.NewGuid().ToString("N");
            document.Header.SessionId = document.Header.RecordId;
            ReplayDocumentFinalizerV11.FinalizeAndValidate(document);
        }
        var record = new MatchRecord
        {
            RecordId = document.Header.RecordId,
            AdventureId = document.Header.AdventureId,
            SessionId = document.Header.SessionId,
            LevelId = document.Header.LevelId,
            Result = document.Header.Result,
            StartedUtc = document.Header.StartedUtc,
            EndedUtc = document.Header.EndedUtc,
            Collection = MatchRecordCollections.Favorite,
            IsFavorite = true,
            Origin = MatchRecordOrigins.Imported,
            ReplayState = MatchReplayStates.Ready,
            ReplayProtocol = ReplayProtocolV11.DocumentVersion,
            GameBuild = document.Header.GameBuild,
            ToolBuild = document.Header.ToolBuild,
            ModFingerprint = document.Header.RuntimeFingerprint,
            RequiredCapabilities = (document.Header.RequiredCapabilities ?? new List<string>()).ToList(),
            ContentDependencies = document.Content.Dependencies.Select(item => item.OwnerModId).ToList(),
            ContentSha256 = document.Header.DocumentSha256,
            EventCount = document.Events.Count,
            TurnCount = Math.Max(1, document.Events.Count == 0 ? document.InitialState.TurnIndex : document.Events.Max(item => item.TurnIndex))
        };
        var analysis = parsed.Analysis ?? MatchAnalysisBuilder.BuildV11(record, document);
        analysis.RecordId = record.RecordId;
        if (!MatchRecordStorage.Database.SaveV11(record, document, analysis))
        {
            throw new IOException("v11 回放写入数据库失败。");
        }
        return record;
    }

    internal static MatchReplayImportPreview Inspect(string packagePath)
    {
        using var file = OpenPackage(packagePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var parsed = ReadAndValidate(archive);
        var document = parsed.Document;
        return new MatchReplayImportPreview
        {
            Path = packagePath,
            RecordId = document.Header.RecordId,
            LevelId = document.Header.LevelId,
            PackageBytes = file.Length,
            ReplayProtocol = ReplayProtocolV11.DocumentVersion,
            Compatibility = "Compatible",
            CompatibilityMessage = "Replay Document v11 已通过完整包验证。",
            Duplicate = MatchRecordStorage.Database.ContainsContentHash(document.Header.DocumentSha256),
            ContentSha256 = document.Header.DocumentSha256,
            ContentDependencies = document.Content.Dependencies.Select(item => item.OwnerModId).ToList(),
            PrivacySummary = "包内包含对局展示文本、图像/音频附件和动作时间线。"
        };
    }

    private static ParsedPackage ReadAndValidate(ZipArchive archive)
    {
        ValidateArchive(archive);
        var manifestPayload = ReadEntry(archive, "manifest.json", MaximumEntryBytes);
        var manifest = AuraSharedJson.Deserialize<ReplayPackageManifestV11>(Encoding.UTF8.GetString(manifestPayload))
                       ?? throw new InvalidDataException("回放包清单无法读取。");
        if (!string.Equals(manifest.Format, "AuraTools.MatchReplay", StringComparison.Ordinal)
            || manifest.PackageVersion != ReplayProtocolV11.PackageVersion
            || manifest.DocumentVersion != ReplayProtocolV11.DocumentVersion)
        {
            throw new InvalidDataException("运行时只接受 Replay Package v11；旧包必须进入迁移器。");
        }
        var manifestPaths = new HashSet<string>(manifest.Entries.Select(item => item.Path), StringComparer.Ordinal);
        if (manifestPaths.Count != manifest.Entries.Count
            || manifestPaths.Any(path => !SafeEntryPath(path)))
        {
            throw new InvalidDataException("v11 回放包清单包含重复或不安全路径。");
        }
        var actualPaths = new HashSet<string>(
            archive.Entries.Where(item => item.FullName != "manifest.json").Select(item => item.FullName),
            StringComparer.Ordinal);
        if (!actualPaths.SetEquals(manifestPaths))
        {
            throw new InvalidDataException("v11 回放包包含未声明或缺失的 entry。");
        }

        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (entry.ByteLength < 0 || entry.ByteLength > MaximumEntryBytes)
            {
                throw new InvalidDataException("v11 回放包 entry 大小超限：" + entry.Path);
            }
            var payload = ReadEntry(archive, entry.Path, MaximumEntryBytes);
            if (payload.LongLength != entry.ByteLength
                || !string.Equals(ReplayCanonicalJsonV11.Sha256(payload), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("v11 回放包 entry 校验失败：" + entry.Path);
            }
            payloads[entry.Path] = payload;
        }

        if (!payloads.TryGetValue("document.json.gz", out var documentPayload))
        {
            throw new InvalidDataException("v11 回放包缺少 document.json.gz。");
        }
        var document = ReplayPayloadV11.Decode<ReplayDocumentV11>(documentPayload);
        document.Events = manifest.Entries.Where(item => item.Kind == "Timeline")
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .SelectMany(item => ReplayPayloadV11.Decode<List<ReplayTimelineEventV11>>(payloads[item.Path]))
            .ToList();
        document.Checkpoints = manifest.Entries.Where(item => item.Kind == "Checkpoint")
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .Select(item => ReplayPayloadV11.Decode<ReplayCheckpointV11>(payloads[item.Path]))
            .ToList();
        var attachmentPayloads = manifest.Entries.Where(item => item.Kind == "Attachment")
            .ToDictionary(
                item => Path.GetFileNameWithoutExtension(item.Path),
                item => payloads[item.Path],
                StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in document.Attachments)
        {
            if (!attachmentPayloads.TryGetValue(attachment.Sha256, out var payload))
            {
                throw new InvalidDataException("v11 回放包缺少附件：" + attachment.Sha256);
            }
            attachment.Payload = payload;
        }
        if (!string.Equals(document.Header.DocumentSha256, manifest.DocumentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("v11 回放包文档哈希与清单不一致。");
        }
        var validation = ReplayDocumentValidatorV11.Validate(document);
        if (!validation.IsValid) throw new InvalidDataException("v11 回放包验证失败：" + validation.Message);
        MatchAnalysisReport? analysis = null;
        if (payloads.TryGetValue("analysis/summary.json.gz", out var analysisPayload))
        {
            analysis = ReplayPayloadV11.Decode<MatchAnalysisReport>(analysisPayload);
        }
        return new ParsedPackage(document, analysis);
    }

    private static FileStream OpenPackage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("回放包不存在。", path);
        }
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumPackageBytes)
        {
            throw new InvalidDataException("回放包大小超限。");
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count <= 0 || archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("回放包 entry 数量异常。");
        }
        long total = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!paths.Add(entry.FullName) || !SafeEntryPath(entry.FullName) || entry.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException("回放包包含不安全或重复 entry。");
            }
            total = checked(total + Math.Max(0, entry.Length));
            if (total > MaximumPackageBytes) throw new InvalidDataException("回放包解压后大小超限。");
        }
    }

    private static bool SafeEntryPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && !path.StartsWith("/", StringComparison.Ordinal)
               && !path.Contains("..")
               && !path.Contains('\\')
               && !path.Contains(':');
    }

    private static byte[] ReadEntry(ZipArchive archive, string name, long maximum)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("回放包缺少 " + name + "。");
        if (entry.Length < 0 || entry.Length > maximum) throw new InvalidDataException("回放包 entry 大小超限：" + name);
        using var stream = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > maximum) throw new InvalidDataException("回放包 entry 解压后大小超限：" + name);
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] payload)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(payload, 0, payload.Length);
    }

    private static ReplayDocumentV11 Clone(ReplayDocumentV11 document)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayDocumentV11>(
                   Encoding.UTF8.GetString(ReplayCanonicalJsonV11.SerializeUtf8(document)))
               ?? throw new InvalidDataException("v11 回放文档无法复制。");
    }

    private static string NormalizeExtension(string extension)
    {
        var value = (extension ?? "").Trim().ToLowerInvariant();
        return value is ".png" or ".jpg" or ".jpeg" or ".wav" or ".flac" ? value : ".bin";
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string((value ?? "Replay").Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Replay" : result;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        return Path.Combine(Path.GetDirectoryName(path) ?? ".",
            Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(path));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class ParsedPackage
    {
        internal ParsedPackage(ReplayDocumentV11 document, MatchAnalysisReport? analysis)
        {
            Document = document;
            Analysis = analysis;
        }
        internal ReplayDocumentV11 Document { get; }
        internal MatchAnalysisReport? Analysis { get; }
    }
}
