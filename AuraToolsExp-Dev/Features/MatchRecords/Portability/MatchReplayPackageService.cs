using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.Portability;

internal static class MatchReplayPackageService
{
    private const long MaximumEntryBytes = 512L * 1024L * 1024L;
    private const long MaximumPackageBytes = 2L * 1024L * 1024L * 1024L;
    private const int MaximumEntries = 12_000;

    internal static string Export(string recordId)
    {
        var envelope = MatchRecordStorage.Database.LoadV12(recordId, loadAssetPayloads: true)
                       ?? throw new InvalidOperationException("找不到经过验证的 Replay Document v12。");
        var record = MatchRecordStorage.Database.Get(recordId)
                     ?? throw new InvalidOperationException("找不到回放对应的对局摘要。");
        var document = envelope.Document;
        var analysis = MatchRecordStorage.Database.GetAnalysis(recordId)
                       ?? MatchAnalysisBuilder.BuildV12(record, document);
        ValidateAssetPayloads(document.Assets);

        var skeleton = ReplayCanonicalJsonV12.Clone(envelope);
        skeleton.Document.TruthEvents.Clear();
        skeleton.Document.PresentationEvents.Clear();
        skeleton.Document.TruthCheckpoints.Clear();
        skeleton.Document.PresentationCheckpoints.Clear();
        foreach (var asset in skeleton.Document.Assets) asset.Payload = Array.Empty<byte>();
        var payloads = new Dictionary<string, (string Kind, byte[] Payload)>(StringComparer.Ordinal)
        {
            ["document.json.gz"] = ("Document", ReplayPayloadV12.Encode(skeleton)),
            ["analysis/summary.json.gz"] = ("Analysis", ReplayPayloadV12.Encode(analysis))
        };
        AddChunks(payloads, "truth", ReplayJournalLanesV12.Truth, document.TruthEvents);
        AddChunks(payloads, "presentation", ReplayJournalLanesV12.Presentation, document.PresentationEvents);
        for (var index = 0; index < document.TruthCheckpoints.Count; index++)
            payloads["checkpoints/truth/" + index.ToString("D6") + ".json.gz"] =
                ("TruthCheckpoint", ReplayPayloadV12.Encode(document.TruthCheckpoints[index]));
        for (var index = 0; index < document.PresentationCheckpoints.Count; index++)
            payloads["checkpoints/presentation/" + index.ToString("D6") + ".json.gz"] =
                ("PresentationCheckpoint", ReplayPayloadV12.Encode(document.PresentationCheckpoints[index]));
        foreach (var asset in document.Assets)
            payloads["assets/" + asset.Sha256.ToLowerInvariant() + NormalizeExtension(asset.Extension)] =
                ("Asset", asset.Payload);

        var manifest = new ReplayPackageManifestV12
        {
            ExportedUtc = DateTime.UtcNow.ToString("O"),
            RecordId = recordId,
            DocumentRoot = envelope.DeclaredDocumentRoot,
            TruthRoot = document.Header.TruthRoot,
            PresentationRoot = document.Header.PresentationRoot,
            Entries = payloads.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new ReplayPackageEntryV12
                {
                    Path = item.Key,
                    Kind = item.Value.Kind,
                    ByteLength = item.Value.Payload.LongLength,
                    Sha256 = ReplayCanonicalJsonV12.Sha256(item.Value.Payload)
                })
                .ToList()
        };
        var name = SafeName(record.LevelId) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".aurareplay";
        var output = UniquePath(Path.Combine(MatchRecordStorage.ExportsDirectory, name));
        using var transaction = AuraSharedFileStore.BeginWrite(AuraToolsIds.ModId, output, overwrite: false);
        using (var archive = new ZipArchive(transaction.Stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WriteEntry(archive, "manifest.json", ReplayCanonicalJsonV12.SerializeUtf8(manifest));
            foreach (var pair in payloads.OrderBy(item => item.Key, StringComparer.Ordinal))
                WriteEntry(archive, pair.Key, pair.Value.Payload);
        }
        transaction.Stream.Flush();
        transaction.Stream.Position = 0;
        using (var verify = new ZipArchive(transaction.Stream, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8))
            ReadAndValidate(verify);
        transaction.Commit();
        return output;
    }

    internal static MatchRecord Import(string packagePath)
    {
        using var file = OpenPackage(packagePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var parsed = ReadAndValidate(archive);
        var document = parsed.Envelope.Document;
        if (MatchRecordStorage.Database.ContainsContentHash(parsed.Envelope.DeclaredDocumentRoot))
            throw new InvalidDataException("相同内容的 v12 回放已经存在。");
        if (MatchRecordStorage.Database.Get(document.Header.RecordId) != null)
            throw new InvalidDataException("回放记录标识已存在；为保持权威根哈希，导入不会改写记录标识。");

        var record = new MatchRecord
        {
            RecordId = document.Header.RecordId,
            AdventureId = document.Header.AdventureId,
            SessionId = document.Header.BattleSessionId,
            LevelId = document.Header.LevelId,
            BattleTitle = document.Header.BattleTitle,
            Result = document.Header.Result,
            StartedUtc = document.Header.StartedUtc,
            EndedUtc = document.Header.EndedUtc,
            Collection = MatchRecordCollections.Favorite,
            IsFavorite = true,
            Origin = MatchRecordOrigins.Imported,
            ReplayState = MatchReplayStates.Ready,
            ReplayProtocol = ReplayProtocolV12.DocumentVersion,
            GameBuild = document.Header.GameBuildProvenance,
            ToolBuild = document.Header.RecorderBuild,
            RequiredCapabilities = document.Header.RequiredCapabilities.ToList(),
            OptionalCapabilities = document.Header.OptionalCapabilities.ToList(),
            ContentDependencies = ProvenanceOwners(document),
            ContentSha256 = parsed.Envelope.DeclaredDocumentRoot,
            EventCount = document.TruthEvents.Count + document.PresentationEvents.Count,
            TurnCount = Math.Max(1, document.TruthEvents.Select(item => item.RoundSequence).DefaultIfEmpty(0).Max())
        };
        var analysis = MatchAnalysisBuilder.BuildV12(record, document);
        analysis.RecordId = record.RecordId;
        if (!MatchRecordStorage.Database.SaveV12(record, parsed.Envelope, analysis))
            throw new IOException("v12 回放写入数据库失败。");
        return record;
    }

    internal static MatchReplayImportPreview Inspect(string packagePath)
    {
        using var file = OpenPackage(packagePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var parsed = ReadAndValidate(archive);
        var document = parsed.Envelope.Document;
        return new MatchReplayImportPreview
        {
            Path = packagePath,
            RecordId = document.Header.RecordId,
            LevelId = document.Header.LevelId,
            PackageBytes = file.Length,
            ReplayProtocol = ReplayProtocolV12.DocumentVersion,
            Compatibility = "Compatible",
            CompatibilityMessage = "Replay Document v12 已通过双通道、检查点、资源与根哈希验证。",
            Duplicate = MatchRecordStorage.Database.ContainsContentHash(parsed.Envelope.DeclaredDocumentRoot),
            ContentSha256 = parsed.Envelope.DeclaredDocumentRoot,
            ContentDependencies = ProvenanceOwners(document),
            PrivacySummary = "包内仅含公开权威状态、可移植表现与内嵌资源；不包含本机 POV sidecar。"
        };
    }

    private static ParsedPackage ReadAndValidate(ZipArchive archive)
    {
        ValidateArchive(archive);
        ReplayPackageManifestV12 manifest;
        try
        {
            manifest = ReplayCanonicalJsonV12.DeserializeStrict<ReplayPackageManifestV12>(
                Encoding.UTF8.GetString(ReadEntry(archive, "manifest.json", MaximumEntryBytes)));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("回放包清单无法读取。", ex);
        }
        if (manifest.Format != "AuraTools.MatchReplay"
            || manifest.PackageVersion != ReplayProtocolV12.PackageVersion
            || manifest.DocumentVersion != ReplayProtocolV12.DocumentVersion)
            throw new InvalidDataException("运行时只接受 Replay Package v12；旧包不再进入可播放集合。");
        if (manifest.Entries == null
            || manifest.Entries.Any(item => item == null || !ValidManifestEntry(item))
            || manifest.Entries.Count(item => item.Kind == "Document") != 1
            || manifest.Entries.Count(item => item.Kind == "Analysis") != 1)
            throw new InvalidDataException("v12 回放包清单 entry 类型或路径无效。");
        var declaredPaths = new HashSet<string>(manifest.Entries.Select(item => item.Path), StringComparer.Ordinal);
        if (declaredPaths.Count != manifest.Entries.Count || declaredPaths.Any(path => !SafeEntryPath(path)))
            throw new InvalidDataException("v12 回放包清单包含重复或不安全路径。");
        var actualPaths = new HashSet<string>(archive.Entries.Where(item => item.FullName != "manifest.json")
            .Select(item => item.FullName), StringComparer.Ordinal);
        if (!actualPaths.SetEquals(declaredPaths))
            throw new InvalidDataException("v12 回放包包含未声明或缺失的 entry。");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            if (entry.ByteLength < 0 || entry.ByteLength > MaximumEntryBytes)
                throw new InvalidDataException("v12 回放包 entry 大小超限：" + entry.Path);
            var payload = ReadEntry(archive, entry.Path, MaximumEntryBytes);
            if (payload.LongLength != entry.ByteLength
                || !string.Equals(ReplayCanonicalJsonV12.Sha256(payload), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v12 回放包 entry 校验失败：" + entry.Path);
            payloads[entry.Path] = payload;
        }
        if (!payloads.TryGetValue("document.json.gz", out var documentPayload))
            throw new InvalidDataException("v12 回放包缺少 document.json.gz。");
        var envelope = ReplayPayloadV12.Decode<ReplayDocumentEnvelopeV12>(documentPayload);
        envelope.Document.TruthEvents = DecodeChunks(manifest, payloads, "TruthChunk", ReplayJournalLanesV12.Truth);
        envelope.Document.PresentationEvents = DecodeChunks(manifest, payloads, "PresentationChunk", ReplayJournalLanesV12.Presentation);
        envelope.Document.TruthCheckpoints = manifest.Entries.Where(item => item.Kind == "TruthCheckpoint")
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .Select(item => ReplayPayloadV12.Decode<ReplayTruthCheckpointV12>(payloads[item.Path])).ToList();
        envelope.Document.PresentationCheckpoints = manifest.Entries.Where(item => item.Kind == "PresentationCheckpoint")
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .Select(item => ReplayPayloadV12.Decode<ReplayPresentationCheckpointV12>(payloads[item.Path])).ToList();
        var assetEntries = manifest.Entries.Where(item => item.Kind == "Asset").ToList();
        var expectedAssetPaths = envelope.Document.Assets
            .Select(item => "assets/" + item.Sha256.ToLowerInvariant() + NormalizeExtension(item.Extension))
            .ToHashSet(StringComparer.Ordinal);
        if (assetEntries.Count != envelope.Document.Assets.Count
            || !expectedAssetPaths.SetEquals(assetEntries.Select(item => item.Path)))
            throw new InvalidDataException("v12 回放包资源 entry 与文档清单不一致。");
        var assets = assetEntries
            .ToDictionary(item => AssetHashFromPath(item.Path), item => payloads[item.Path], StringComparer.OrdinalIgnoreCase);
        foreach (var asset in envelope.Document.Assets)
        {
            if (!assets.TryGetValue(asset.Sha256, out var payload))
                throw new InvalidDataException("v12 回放包缺少资源：" + asset.Sha256);
            asset.Payload = payload;
        }
        ValidateAssetPayloads(envelope.Document.Assets);
        if (manifest.RecordId != envelope.Document.Header.RecordId
            || !string.Equals(manifest.DocumentRoot, envelope.DeclaredDocumentRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.TruthRoot, envelope.Document.Header.TruthRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.PresentationRoot, envelope.Document.Header.PresentationRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("v12 回放包清单与文档根不一致。");
        var validation = ReplayDocumentValidatorV12.Validate(envelope);
        if (!validation.IsValid) throw new InvalidDataException("v12 回放包验证失败：" + validation.Message);
        MatchAnalysisReport? analysis = null;
        if (payloads.TryGetValue("analysis/summary.json.gz", out var analysisPayload))
        {
            analysis = ReplayPayloadV12.Decode<MatchAnalysisReport>(analysisPayload);
            if (!string.IsNullOrWhiteSpace(analysis.RecordId)
                && !string.Equals(analysis.RecordId, envelope.Document.Header.RecordId, StringComparison.Ordinal))
                throw new InvalidDataException("v12 回放分析摘要与文档标识不一致。");
        }
        return new ParsedPackage(envelope, analysis);
    }

    private static void AddChunks(
        IDictionary<string, (string Kind, byte[] Payload)> payloads,
        string directory,
        string lane,
        IReadOnlyList<ReplayJournalEventV12> events)
    {
        foreach (var chunk in ReplayJournalChunkerV12.Build(lane, events))
            payloads["timeline/" + directory + "/" + chunk.ChunkIndex.ToString("D6") + ".json.gz"] =
                (lane == ReplayJournalLanesV12.Truth ? "TruthChunk" : "PresentationChunk", ReplayPayloadV12.Encode(chunk));
    }

    private static List<ReplayJournalEventV12> DecodeChunks(
        ReplayPackageManifestV12 manifest,
        IReadOnlyDictionary<string, byte[]> payloads,
        string kind,
        string lane)
    {
        var chunks = manifest.Entries.Where(item => item.Kind == kind)
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .Select(item => ReplayPayloadV12.Decode<ReplayJournalChunkV12>(payloads[item.Path])).ToList();
        return ReplayJournalChunkerV12.Decode(lane, chunks).ToList();
    }

    private static void ValidateAssetPayloads(IEnumerable<ReplayAssetV12> assets)
    {
        foreach (var asset in assets)
        {
            var error = ReplayAssetContractV12.Validate(asset, requirePayload: true);
            if (error.Length > 0)
                throw new InvalidDataException("回放资源无效：" + asset.Sha256 + "，" + error);
        }
    }

    private static List<string> ProvenanceOwners(ReplayDocumentV12 document)
    {
        return document.Presentation.Entities.Select(item => item.Provenance.OwnerModId)
            .Concat(document.Presentation.Cards.Select(item => item.Provenance.OwnerModId))
            .Concat(document.Presentation.Buffs.Select(item => item.Provenance.OwnerModId))
            .Concat(document.Presentation.Intents.Select(item => item.Provenance.OwnerModId))
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList();
    }

    private static string AssetHashFromPath(string path)
    {
        var name = Path.GetFileName(path ?? "");
        if (name.Length < 64) throw new InvalidDataException("v12 回放资源路径无效：" + path);
        return name.Substring(0, 64);
    }

    private static FileStream OpenPackage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("回放包不存在。", path);
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumPackageBytes) throw new InvalidDataException("回放包大小超限。");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count <= 0 || archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("回放包 entry 数量异常。");
        long total = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!paths.Add(entry.FullName) || !SafeEntryPath(entry.FullName) || entry.Length > MaximumEntryBytes)
                throw new InvalidDataException("回放包包含不安全或重复 entry。");
            total = checked(total + Math.Max(0, entry.Length));
            if (total > MaximumPackageBytes) throw new InvalidDataException("回放包解压后大小超限。");
        }
    }

    private static bool SafeEntryPath(string path) => !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith("/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains('\\') && !path.Contains(':');

    private static bool ValidManifestEntry(ReplayPackageEntryV12 entry)
    {
        if (entry.ByteLength < 0
            || entry.ByteLength > MaximumEntryBytes
            || !IsSha256(entry.Sha256)
            || !SafeEntryPath(entry.Path)) return false;
        return entry.Kind switch
        {
            "Document" => entry.Path == "document.json.gz",
            "Analysis" => entry.Path == "analysis/summary.json.gz",
            "TruthChunk" => IndexedPayloadPath(entry.Path, "timeline/truth/"),
            "PresentationChunk" => IndexedPayloadPath(entry.Path, "timeline/presentation/"),
            "TruthCheckpoint" => IndexedPayloadPath(entry.Path, "checkpoints/truth/"),
            "PresentationCheckpoint" => IndexedPayloadPath(entry.Path, "checkpoints/presentation/"),
            "Asset" => AssetPayloadPath(entry.Path),
            _ => false
        };
    }

    private static bool IndexedPayloadPath(string path, string prefix)
    {
        if (path == null || !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".json.gz", StringComparison.Ordinal))
            return false;
        var value = path.Substring(prefix.Length, path.Length - prefix.Length - ".json.gz".Length);
        return value.Length == 6 && value.All(char.IsDigit);
    }

    private static bool AssetPayloadPath(string path)
    {
        if (path == null || !path.StartsWith("assets/", StringComparison.Ordinal)) return false;
        var name = path.Substring("assets/".Length);
        if (name.Length < 65 || !IsSha256(name.Substring(0, 64))) return false;
        return name.Substring(64) is ".png" or ".wav";
    }

    private static bool IsSha256(string value) => value != null
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9'
                                  || character is >= 'a' and <= 'f'
                                  || character is >= 'A' and <= 'F');

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

    private static string NormalizeExtension(string extension)
    {
        var value = (extension ?? "").Trim().ToLowerInvariant();
        return value is ".png" or ".wav" ? value : ".bin";
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string((value ?? "Replay").Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Replay" : result;
    }

    private static string UniquePath(string path) => !File.Exists(path) ? path : Path.Combine(
        Path.GetDirectoryName(path) ?? ".",
        Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(path));

    private sealed class ParsedPackage
    {
        internal ParsedPackage(ReplayDocumentEnvelopeV12 envelope, MatchAnalysisReport? analysis)
        {
            Envelope = envelope;
            Analysis = analysis;
        }
        internal ReplayDocumentEnvelopeV12 Envelope { get; }
        internal MatchAnalysisReport? Analysis { get; }
    }
}
