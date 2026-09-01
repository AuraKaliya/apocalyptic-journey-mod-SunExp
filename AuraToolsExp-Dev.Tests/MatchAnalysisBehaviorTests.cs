using System.IO.Compression;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchAnalysis()
    {
        var envelope = BuildReplayV17("analysis-v17");
        ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope);
        var record = PackageRecord(envelope);
        var report = MatchAnalysisBuilder.BuildV17(record, envelope.Document);
        Assert(report.TurnCount == 1
               && report.Turns.Single().ActionCount == 1
               && report.Turns.Single().Damage == 30
               && report.Cards.Single().CardId == "strike"
               && report.Cards.Single().AttributedDamage == 30,
            "v17 analysis reduces authoritative deltas and causal transactions without executing gameplay");

        var summary = MatchAnalysisBuilder.BuildSummary(new MatchRecord
        {
            RecordId = "summary-analysis",
            TurnCount = 2
        });
        Assert(summary.RecordId == "summary-analysis" && summary.Turns.Count == 2,
            "summary-only records retain an analysis surface without a retired replay stream");
    }

    public static void TestMatchReplayPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-PackageV17-" + Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        try
        {
            var sourceDatabase = new MatchRecordDatabase(Path.Combine(sourceRoot, "records.sqlite3"));
            sourceDatabase.Initialize();
            MatchRecordStorage.Configure(sourceDatabase, sourceRoot);
            var envelope = BuildReplayV17("package-v17");
            Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope).IsValid,
                "perspective-instruction package fixture seals all v17 roots");
            var record = PackageRecord(envelope);
            Assert(sourceDatabase.SaveV17(record, envelope, MatchAnalysisBuilder.BuildV17(record, envelope.Document)),
                "source database stores the perspective-instruction v17 package input");
            var package = MatchReplayPackageService.Export(record.RecordId);
            var preview = MatchReplayPackageService.Inspect(package);
            Assert(preview.ReplayProtocol == ReplayProtocolV17.DocumentVersion
                   && preview.Compatibility == "Compatible"
                   && preview.ContentSha256 == envelope.DeclaredDocumentRoot
                   && preview.PrivacySummary.Contains("玩家视角", StringComparison.Ordinal)
                   && preview.PrivacySummary.Contains("不可见对手数据", StringComparison.Ordinal),
                "v17 package inspection validates roots and states its fixed-perspective privacy boundary");
            using (var file = File.OpenRead(package))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                Assert(archive.GetEntry("manifest.json") != null
                       && archive.GetEntry("document.json.gz") != null
                       && archive.Entries.Any(item => item.FullName.StartsWith("timeline/truth/", StringComparison.Ordinal))
                       && archive.Entries.Any(item => item.FullName.StartsWith("timeline/presentation/", StringComparison.Ordinal))
                       && archive.Entries.Any(item => item.FullName.StartsWith("checkpoints/truth/", StringComparison.Ordinal))
                       && archive.GetEntry("assets/" + envelope.Document.Assets.Single().Sha256 + ".png") != null,
                    "v17 package has independent journal lanes, paired checkpoints, and declared embedded assets");
            }

            var targetDatabase = new MatchRecordDatabase(Path.Combine(targetRoot, "records.sqlite3"));
            targetDatabase.Initialize();
            MatchRecordStorage.Configure(targetDatabase, targetRoot);
            var imported = MatchReplayPackageService.Import(package);
            var loaded = targetDatabase.LoadV17(imported.RecordId, loadAssetPayloads: true);
            Assert(loaded != null
                   && loaded.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
                   && ReplayDocumentValidatorV17.Validate(loaded).IsValid
                   && loaded.Document.Assets.Single().Payload.SequenceEqual(envelope.Document.Assets.Single().Payload)
                   && File.Exists(targetDatabase.ResolveReplayAsset(envelope.Document.Assets.Single().Sha256)),
                "v17 import commits the exact canonical document without requiring source MODs");

            var duplicateRejected = false;
            try { MatchReplayPackageService.Import(package); }
            catch (InvalidDataException) { duplicateRejected = true; }
            Assert(duplicateRejected, "document roots reject duplicate v17 package imports");

            var damaged = Path.Combine(root, "damaged.aurareplay");
            var bytes = File.ReadAllBytes(package);
            File.WriteAllBytes(damaged, bytes.Take(bytes.Length / 2).ToArray());
            var damagedRejected = false;
            try { MatchReplayPackageService.Inspect(damaged); }
            catch (InvalidDataException) { damagedRejected = true; }
            Assert(damagedRejected, "truncated v17 packages are rejected before database writes");

            var executableEntry = Path.Combine(root, "executable-entry.aurareplay");
            using (var sourceFile = File.OpenRead(package))
            using (var sourceArchive = new ZipArchive(sourceFile, ZipArchiveMode.Read))
            using (var targetFile = File.Create(executableEntry))
            using (var targetArchive = new ZipArchive(targetFile, ZipArchiveMode.Create))
            {
                foreach (var sourceEntry in sourceArchive.Entries.Where(item => item.FullName != "manifest.json"))
                {
                    var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName);
                    using var input = sourceEntry.Open();
                    using var output = targetEntry.Open();
                    input.CopyTo(output);
                }
                var executable = new byte[] { 0x4d, 0x5a, 0x00, 0x00 };
                var executableZipEntry = targetArchive.CreateEntry("scripts/payload.dll");
                using (var output = executableZipEntry.Open()) output.Write(executable, 0, executable.Length);
                var manifestEntry = sourceArchive.GetEntry("manifest.json")!;
                ReplayPackageManifestV17 manifest;
                using (var input = manifestEntry.Open())
                using (var reader = new StreamReader(input))
                    manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayPackageManifestV17>(reader.ReadToEnd())!;
                manifest.Entries.Add(new ReplayPackageEntryV17
                {
                    Path = "scripts/payload.dll",
                    Kind = "Script",
                    ByteLength = executable.Length,
                    Sha256 = ReplayCanonicalJsonV17.Sha256(executable)
                });
                var targetManifest = targetArchive.CreateEntry("manifest.json");
                using var manifestOutput = targetManifest.Open();
                var manifestBytes = ReplayCanonicalJsonV17.SerializeUtf8(manifest);
                manifestOutput.Write(manifestBytes, 0, manifestBytes.Length);
            }
            var executableRejected = false;
            try { MatchReplayPackageService.Inspect(executableEntry); }
            catch (InvalidDataException) { executableRejected = true; }
            Assert(executableRejected,
                "v17 packages reject executable or script entries even when their bytes and manifest hash agree");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MatchRecord PackageRecord(ReplayDocumentEnvelopeV17 envelope) => new()
    {
        RecordId = envelope.Document.Header.RecordId,
        SessionId = envelope.Document.Header.BattleSessionId,
        LevelId = envelope.Document.Header.LevelId,
        Result = envelope.Document.Header.Result,
        StartedUtc = envelope.Document.Header.StartedUtc,
        EndedUtc = envelope.Document.Header.EndedUtc,
        ReplayProtocol = ReplayProtocolV17.DocumentVersion,
        ReplayState = MatchReplayStates.Ready,
        TurnCount = 1
    };
}
