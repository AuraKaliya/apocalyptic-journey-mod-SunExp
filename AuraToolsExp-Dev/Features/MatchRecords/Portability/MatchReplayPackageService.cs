using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Portability;

internal static class MatchReplayPackageService
{
    private const long MaximumEntryBytes = 512L * 1024L * 1024L;
    private const long MaximumPackageBytes = 2L * 1024L * 1024L * 1024L;
    private const int MaximumChunks = 10000;

    internal static string Export(string recordId)
    {
        var record = MatchRecordStorage.Database.Get(recordId)
                     ?? throw new InvalidOperationException("找不到要导出的对局记录。");
        var chunks = MatchRecordStorage.Database.LoadChunks(recordId).OrderBy(item => item.ChunkIndex).ToList();
        if (chunks.Count > MaximumChunks)
        {
            throw new InvalidDataException("回放分块数量异常，无法导出。");
        }

        var analysis = MatchRecordStorage.Database.GetAnalysis(recordId)
                       ?? MatchAnalysisBuilder.Build(record, MatchReplayChunker.Decode(chunks));
        var recordPayload = MatchReplayPayload.Encode(record);
        var analysisPayload = MatchReplayPayload.Encode(analysis);
        var manifest = new MatchReplayPackageManifest
        {
            ExportedUtc = DateTime.UtcNow.ToString("O"),
            RecordId = record.RecordId,
            RecordSha256 = MatchReplayPayload.Sha256(recordPayload),
            AnalysisSha256 = MatchReplayPayload.Sha256(analysisPayload)
        };
        var packageChunks = new List<MatchReplayPackageChunk>();
        foreach (var chunk in chunks)
        {
            var entryName = "chunks/" + chunk.ChunkIndex.ToString("D6") + ".bin";
            manifest.ChunkSha256[entryName] = MatchReplayPayload.Sha256(chunk.Payload);
            packageChunks.Add(new MatchReplayPackageChunk
            {
                ChunkIndex = chunk.ChunkIndex,
                FirstSequence = chunk.FirstSequence,
                LastSequence = chunk.LastSequence,
                FirstTurnIndex = chunk.FirstTurnIndex,
                LastTurnIndex = chunk.LastTurnIndex,
                EntryName = entryName
            });
        }

        var name = SafeName(record.LevelId) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".aurareplay";
        var output = UniquePath(Path.Combine(MatchRecordStorage.ExportsDirectory, name));
        using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
        {
            WriteJson(archive, "manifest.json", manifest);
            WriteBytes(archive, "record.bin", recordPayload);
            WriteBytes(archive, "analysis.bin", analysisPayload);
            WriteJson(archive, "chunks.json", packageChunks);
            foreach (var chunk in chunks)
            {
                var entryName = packageChunks.First(item => item.ChunkIndex == chunk.ChunkIndex).EntryName;
                WriteBytes(archive, entryName, chunk.Payload);
            }
        }

        return output;
    }

    internal static MatchRecord Import(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException("回放包不存在。", packagePath);
        }

        using var file = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        if (archive.Entries.Count > MaximumChunks + 8
            || archive.Entries.Sum(entry => Math.Max(0L, entry.Length)) > MaximumPackageBytes)
        {
            throw new InvalidDataException("回放包包含过多数据。");
        }
        var manifest = ReadJson<MatchReplayPackageManifest>(archive, "manifest.json")
                       ?? throw new InvalidDataException("回放包缺少清单。");
        if (!string.Equals(manifest.Format, "AuraTools.MatchReplay", StringComparison.Ordinal)
            || manifest.PackageVersion != 1)
        {
            throw new InvalidDataException("不支持的回放包格式或版本。");
        }

        var recordPayload = ReadBytes(archive, "record.bin");
        Verify(recordPayload, manifest.RecordSha256, "record.bin");
        var record = MatchReplayPayload.Decode<MatchRecord>(recordPayload)
                     ?? throw new InvalidDataException("回放元数据无法读取。");
        var metadata = ReadJson<List<MatchReplayPackageChunk>>(archive, "chunks.json")
                       ?? throw new InvalidDataException("回放包缺少分块索引。");
        if (metadata.Count > MaximumChunks)
        {
            throw new InvalidDataException("回放分块数量异常。");
        }

        if (metadata.Select(item => item.ChunkIndex).Distinct().Count() != metadata.Count
            || metadata.Select(item => item.EntryName).Distinct(StringComparer.Ordinal).Count() != metadata.Count)
        {
            throw new InvalidDataException("回放分块索引包含重复项。");
        }

        var chunks = new List<MatchReplayChunk>(metadata.Count);
        foreach (var item in metadata.OrderBy(value => value.ChunkIndex))
        {
            if (string.IsNullOrWhiteSpace(item.EntryName)
                || !item.EntryName.StartsWith("chunks/", StringComparison.Ordinal)
                || item.EntryName.Contains("..")
                || item.EntryName.Contains('\\')
                || !manifest.ChunkSha256.TryGetValue(item.EntryName, out var expected))
            {
                throw new InvalidDataException("回放分块索引不完整。");
            }

            var payload = ReadBytes(archive, item.EntryName);
            Verify(payload, expected, item.EntryName);
            chunks.Add(new MatchReplayChunk
            {
                ChunkIndex = item.ChunkIndex,
                FirstSequence = item.FirstSequence,
                LastSequence = item.LastSequence,
                FirstTurnIndex = item.FirstTurnIndex,
                LastTurnIndex = item.LastTurnIndex,
                Payload = payload,
                Sha256 = MatchReplayPayload.Sha256(payload)
            });
        }

        var decoded = MatchReplayChunker.Decode(chunks);
        if (decoded.Count != record.EventCount)
        {
            throw new InvalidDataException("回放事件数量与元数据不一致。");
        }

        MatchAnalysisReport? importedAnalysis = null;
        var analysisEntry = archive.GetEntry("analysis.bin");
        if (analysisEntry != null)
        {
            var analysisPayload = ReadBytes(archive, "analysis.bin");
            Verify(analysisPayload, manifest.AnalysisSha256, "analysis.bin");
            importedAnalysis = MatchReplayPayload.Decode<MatchAnalysisReport>(analysisPayload)
                               ?? throw new InvalidDataException("分析数据无法读取。");
        }

        if (MatchRecordStorage.Database.Get(record.RecordId) != null)
        {
            record.RecordId = Guid.NewGuid().ToString("N");
        }

        record.Sequence = 0;
        record.Collection = MatchRecordCollections.Favorite;
        record.ReplayState = MatchReplayStates.Ready;
        if (!MatchRecordStorage.Database.Save(record, chunks))
        {
            throw new IOException("回放记录写入数据库失败。");
        }

        if (importedAnalysis != null)
        {
            importedAnalysis.RecordId = record.RecordId;
            MatchRecordStorage.Database.SaveAnalysis(importedAnalysis);
        }
        else
        {
            MatchRecordStorage.Database.SaveAnalysis(MatchAnalysisBuilder.Build(record, decoded));
        }

        return record;
    }

    internal static int ImportInbox(out string message)
    {
        var files = Directory.GetFiles(MatchRecordStorage.ImportsDirectory, "*.aurareplay", SearchOption.TopDirectoryOnly);
        var imported = 0;
        var failures = new List<string>();
        foreach (var path in files)
        {
            try
            {
                Import(path);
                var completed = Path.Combine(MatchRecordStorage.ImportsDirectory, "Imported");
                Directory.CreateDirectory(completed);
                File.Move(path, UniquePath(Path.Combine(completed, Path.GetFileName(path))));
                imported++;
            }
            catch (Exception ex)
            {
                failures.Add(Path.GetFileName(path) + "：" + ex.Message);
            }
        }

        message = imported > 0 ? "已导入 " + imported + " 个回放包。" : "导入目录中没有可导入的回放包。";
        if (failures.Count > 0)
        {
            message += " 失败 " + failures.Count + " 个：" + string.Join("；", failures.Take(3));
        }

        return imported;
    }

    private static void WriteJson<T>(ZipArchive archive, string name, T value)
    {
        WriteBytes(archive, name, Encoding.UTF8.GetBytes(AuraSharedJson.SerializeCompact(value)));
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static T? ReadJson<T>(ZipArchive archive, string name)
    {
        return AuraSharedJson.Deserialize<T>(Encoding.UTF8.GetString(ReadBytes(archive, name)));
    }

    private static byte[] ReadBytes(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("回放包缺少 " + name + "。");
        if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
        {
            throw new InvalidDataException(name + " 超出允许大小。");
        }

        using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(int.MaxValue, entry.Length));
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaximumEntryBytes)
            {
                throw new InvalidDataException(name + " 解压后超出允许大小。");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void Verify(byte[] payload, string expected, string name)
    {
        if (string.IsNullOrWhiteSpace(expected)
            || !string.Equals(MatchReplayPayload.Sha256(payload), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(name + " 校验失败。");
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var normalized = new string((string.IsNullOrWhiteSpace(value) ? "match" : value)
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return normalized.Length > 64 ? normalized.Substring(0, 64) : normalized;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, name + "-" + index + extension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N") + extension);
    }
}
