using System.IO.Compression;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchAnalysis()
    {
        var document = ReplayV10Document("analysis-v10");
        ReplayDocumentFinalizerV10.FinalizeAndValidate(document);
        var record = new MatchRecord
        {
            RecordId = document.Header.RecordId,
            ReplayProtocol = 10,
            TurnCount = 1
        };
        var report = MatchAnalysisBuilder.BuildV10(record, document);
        Assert(report.TurnCount == 1
               && report.Turns.Single().ActionCount == 1
               && report.Turns.Single().Damage == 7
               && report.Cards.Single().CardId == "card-a"
               && report.Cards.Single().AttributedDamage == 7,
            "v10 analysis consumes recorded semantics without executing gameplay");
    }

    public static void TestMatchReplayPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-PackageV10-" + Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        try
        {
            var sourceDatabase = new MatchRecordDatabase(Path.Combine(sourceRoot, "records.sqlite3"));
            sourceDatabase.Initialize();
            MatchRecordStorage.Configure(sourceDatabase, sourceRoot);
            var document = ReplayV10Document("package-v10");
            var payload = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            var hash = ReplayCanonicalJsonV10.Sha256(payload);
            document.Attachments.Add(new ReplayAttachmentV10
            {
                Sha256 = hash,
                MediaType = "image/png",
                Extension = ".png",
                Usage = "Card.Artwork",
                ByteLength = payload.Length,
                Width = 1,
                Height = 1,
                Required = true,
                Payload = payload
            });
            document.Content.Definitions.Single(item => item.Content.ContentKind == "Card")
                .Display.ArtworkAssetSha256 = hash;
            Assert(ReplayDocumentFinalizerV10.FinalizeAndValidate(document).IsValid,
                "self-contained attachment participates in the v10 document hash");
            var record = Summary(document);
            Assert(sourceDatabase.SaveV10(record, document, MatchAnalysisBuilder.BuildV10(record, document)),
                "source database stores the self-contained v10 package input");
            var package = MatchReplayPackageService.Export(record.RecordId);
            var preview = MatchReplayPackageService.Inspect(package);
            Assert(preview.ReplayProtocol == 10
                   && preview.Compatibility == "Compatible"
                   && preview.ContentSha256 == document.Header.DocumentSha256,
                "v10 package inspection validates the document, chunks, checkpoints, and attachment hashes");
            using (var file = File.OpenRead(package))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                Assert(archive.GetEntry("manifest.json") != null
                       && archive.GetEntry("document.json.gz") != null
                       && archive.Entries.Any(item => item.FullName.StartsWith("timeline/", StringComparison.Ordinal))
                       && archive.Entries.Any(item => item.FullName.StartsWith("checkpoints/", StringComparison.Ordinal))
                       && archive.GetEntry("attachments/" + hash + ".png") != null,
                    "v10 package layout contains only declared self-contained entries");
            }

            var targetDatabase = new MatchRecordDatabase(Path.Combine(targetRoot, "records.sqlite3"));
            targetDatabase.Initialize();
            MatchRecordStorage.Configure(targetDatabase, targetRoot);
            var imported = MatchReplayPackageService.Import(package);
            var loaded = targetDatabase.LoadV10(imported.RecordId, loadAttachmentPayloads: true);
            var importedAssetPath = targetDatabase.ResolveReplayAsset(hash);
            Assert(loaded != null
                   && loaded.Header.DocumentVersion == 10
                   && loaded.Attachments.Single().Payload.SequenceEqual(payload)
                   && ReplayDocumentValidatorV10.Validate(loaded).IsValid
                   && File.Exists(importedAssetPath),
                "v10 import commits a verified document and its content-addressed attachment");

            var duplicateRejected = false;
            try { MatchReplayPackageService.Import(package); }
            catch (InvalidDataException) { duplicateRejected = true; }
            Assert(duplicateRejected, "content hashes reject duplicate v10 package imports");
            Assert(targetDatabase.Delete(imported.RecordId) && !File.Exists(importedAssetPath),
                "content-addressed attachments are removed only after their final replay reference is deleted");

            var damaged = Path.Combine(root, "damaged.aurareplay");
            var bytes = File.ReadAllBytes(package);
            File.WriteAllBytes(damaged, bytes.Take(bytes.Length / 2).ToArray());
            var damagedRejected = false;
            try { MatchReplayPackageService.Inspect(damaged); }
            catch (InvalidDataException) { damagedRejected = true; }
            Assert(damagedRejected, "truncated v10 packages are rejected before database writes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
