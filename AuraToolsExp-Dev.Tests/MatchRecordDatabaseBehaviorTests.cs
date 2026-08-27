using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchRecordDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-MatchRecordsV12-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestV12Storage(root);
            TestV12CutoverIdempotence(root);
            TestPreV12Cutover(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestV12CutoverIdempotence(string root)
    {
        var path = Path.Combine(root, "v12-cutover-idempotence.db");
        var database = new MatchRecordDatabase(path);
        database.Initialize();
        var envelope = BuildReplayV12("v12-survives-cutover-audit");
        Assert(ReplayDocumentFinalizerV12.FinalizeAndValidate(envelope).IsValid
               && database.SaveV12(Summary(envelope), envelope),
            "v12 cutover idempotence fixture stores a current canonical replay");
        var assetPath = database.ResolveReplayAsset(envelope.Document.Assets.Single().Sha256);
        using (var connection = new WinSqliteConnection(path))
            connection.Execute("DELETE FROM replay_migrations;");

        var reopened = new MatchRecordDatabase(path);
        reopened.Initialize();
        var loaded = reopened.LoadV12(envelope.Document.Header.RecordId, loadAssetPayloads: true);
        Assert(loaded != null
               && loaded.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
               && File.Exists(assetPath)
               && ReplayDocumentValidatorV12.Validate(loaded).IsValid,
            "rerunning the cutover audit never drops an already-valid v12 document or its shared asset");
    }

    private static void TestV12Storage(string root)
    {
        var path = Path.Combine(root, "v12.db");
        var database = new MatchRecordDatabase(path);
        database.Initialize();
        var envelope = BuildReplayV12("database-v12");
        Assert(ReplayDocumentFinalizerV12.FinalizeAndValidate(envelope).IsValid,
            "database fixture is a valid v12 document");
        var record = Summary(envelope);
        var analysis = MatchAnalysisBuilder.BuildV12(record, envelope.Document);
        Assert(database.SaveV12(record, envelope, analysis),
            "database atomically stores a validated v12 canonical document");
        Assert(!database.SaveV12(Summary(envelope), envelope, analysis),
            "database rejects duplicate canonical record ids");

        var loaded = database.LoadV12(record.RecordId, loadAssetPayloads: true);
        var manifestOnly = database.LoadV12(record.RecordId, loadAssetPayloads: false);
        Assert(loaded != null
               && loaded.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
               && ReplayDocumentValidatorV12.Validate(loaded).IsValid
               && loaded.Document.TruthEvents.Count == envelope.Document.TruthEvents.Count
               && loaded.Document.PresentationEvents.Count == envelope.Document.PresentationEvents.Count
               && loaded.Document.Assets.Single().Payload.SequenceEqual(envelope.Document.Assets.Single().Payload),
            "database restores both journal lanes, paired checkpoints, and content-addressed assets");
        Assert(manifestOnly != null
               && manifestOnly.Document.Assets.Single().Payload.Length == 0
               && ReplayDocumentValidatorV12.Validate(manifestOnly).IsValid,
            "canonical validation depends on the asset manifest and remains valid without loading large payload bytes");
        Assert(database.GetAnalysis(record.RecordId)?.RecordId == record.RecordId
               && database.ContainsContentHash(envelope.DeclaredDocumentRoot),
            "analysis and canonical document root remain queryable beside the replay");

        var privateAssetBytes = ReplayTestPngBytes();
        var privateAssetHash = ReplayCanonicalJsonV12.Sha256(privateAssetBytes);
        var sidecar = new ReplayPovSidecarV12
        {
            ParentDocumentRoot = envelope.DeclaredDocumentRoot,
            PlayerId = "local-player",
            Events = new List<ReplayPovEventV12>
            {
                new()
                {
                    CanonicalSequence = envelope.Document.TruthEvents.First().Sequence,
                    TransactionId = envelope.Document.TruthEvents.First().TransactionId,
                    StepOrdinal = envelope.Document.TruthEvents.First().StepOrdinal,
                    Kind = ReplayPovEventKindsV12.UpsertPrivateCard,
                    Card = new ReplayPublicCardStateV12
                    {
                        CardInstanceId = "private-card",
                        DescriptorId = "private-card-desc",
                        OwnerPlayerId = "local-player",
                        Zone = "Hand"
                    }
                }
            },
            PrivateCards = new List<ReplayCardDescriptorV12>
            {
                new()
                {
                    DescriptorId = "private-card-desc",
                    Name = "Private Card",
                    ArtworkAssetSha256 = privateAssetHash
                }
            },
            Assets = new List<ReplayAssetV12>
            {
                new()
                {
                    Sha256 = privateAssetHash,
                    MediaType = "image/png",
                    Extension = ".png",
                    Usage = "Pov.Card.Artwork",
                    ByteLength = privateAssetBytes.Length,
                    Width = 1,
                    Height = 1,
                    Payload = privateAssetBytes
                }
            }
        };
        ReplayPovContractV12.Finalize(sidecar);
        database.SavePovV12(record.RecordId, sidecar);
        var loadedPov = database.LoadFirstPovV12(record.RecordId);
        var loadedPovWithAssets = database.LoadPovV12(record.RecordId, "local-player", loadAssetPayloads: true);
        Assert(loadedPov != null
               && loadedPov.ParentDocumentRoot == envelope.DeclaredDocumentRoot
               && loadedPov.Assets.Single().Payload.Length == 0
               && ReplayPovContractV12.Validate(loadedPov, requirePayloads: false) == ""
               && loadedPovWithAssets?.Assets.Single().Payload.SequenceEqual(privateAssetBytes) == true
               && ReplayPovContractV12.Validate(loadedPovWithAssets, requirePayloads: true) == "",
            "POV sidecar is independently hashed and points only to its parent document root");

        var rejected = new MatchRecord
        {
            RecordId = "rejected-v12",
            SessionId = "rejected-v12",
            LevelId = "level-test",
            ReplayProtocol = ReplayProtocolV12.DocumentVersion
        };
        Assert(database.SaveSummaryV12(rejected, MatchAnalysisBuilder.BuildSummary(rejected), rejected: true)
               && database.Get(rejected.RecordId)?.ReplayState == MatchReplayStates.Rejected
               && database.LoadV12(rejected.RecordId) == null,
            "failed structured capture stores only a rejected summary and never a partial document");

        var assetPath = database.ResolveReplayAsset(envelope.Document.Assets.Single().Sha256);
        var privateAssetPath = database.ResolveReplayAsset(privateAssetHash);
        Assert(File.Exists(assetPath)
               && File.Exists(privateAssetPath)
               && database.Delete(record.RecordId)
               && !File.Exists(assetPath)
               && !File.Exists(privateAssetPath),
            "deleting the final canonical and POV references removes their content-addressed files");
        Assert(MatchRecordsDatabaseMigrator.IntegrityCheck(path) == "ok",
            "v12 storage remains SQLite-integrity clean after atomic deletion");
    }

    private static void TestPreV12Cutover(string root)
    {
        var path = Path.Combine(root, "cutover.db");
        var attachmentDirectory = Path.Combine(root, "Attachments");
        Directory.CreateDirectory(attachmentDirectory);
        var retiredAsset = Path.Combine(attachmentDirectory, new string('a', 64) + ".bin");
        File.WriteAllBytes(retiredAsset, new byte[] { 7, 8, 9 });
        using (var connection = new WinSqliteConnection(path))
        {
            connection.Execute("CREATE TABLE battle_records(sequence INTEGER PRIMARY KEY AUTOINCREMENT, record_id TEXT UNIQUE NOT NULL, "
                               + "adventure_id TEXT NOT NULL, session_id TEXT NOT NULL, level_id TEXT NOT NULL, result TEXT NOT NULL, "
                               + "started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, collection_kind TEXT NOT NULL, replay_state TEXT NOT NULL, "
                               + "replay_protocol INTEGER NOT NULL, game_build TEXT NOT NULL, tool_build TEXT NOT NULL, mod_fingerprint TEXT NOT NULL, "
                               + "event_count INTEGER NOT NULL, turn_count INTEGER NOT NULL, compressed_bytes INTEGER NOT NULL, "
                               + "statistics_payload BLOB NOT NULL, initial_payload BLOB NOT NULL, metadata_payload BLOB NOT NULL);");
            var metadata = new MatchRecordMetadata
            {
                BattleTitle = "Retired battle",
                ContentSha256 = new string('b', 64),
                RequiredCapabilities = new List<string> { "old-capability" }
            };
            using (var insert = connection.Prepare(
                       "INSERT INTO battle_records(record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, "
                       + "collection_kind, replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, turn_count, "
                       + "compressed_bytes, statistics_payload, initial_payload, metadata_payload) VALUES(?, '', ?, 'old-level', 'Win', '', '', "
                       + "'Favorite', 'Ready', 11, '', '', '', 8, 2, 999, ?, ?, ?);"))
            {
                insert.Bind(1, "retired-v11");
                insert.Bind(2, "retired-v11");
                insert.Bind(3, MatchReplayPayload.Encode("statistics-retained"));
                insert.Bind(4, MatchReplayPayload.Encode(new MatchReplayInitialState { LevelId = "old-level" }));
                insert.Bind(5, MatchReplayPayload.Encode(metadata));
                insert.Execute();
            }
            connection.Execute("CREATE TABLE replay_chunks(record_id TEXT, chunk_index INTEGER, payload BLOB);");
            connection.Execute("CREATE TABLE replay_assets(asset_sha256 TEXT, file_path TEXT);");
            using var asset = connection.Prepare("INSERT INTO replay_assets(asset_sha256, file_path) VALUES(?, ?);");
            asset.Bind(1, new string('a', 64));
            asset.Bind(2, "Attachments/" + new string('a', 64) + ".bin");
            asset.Execute();
            connection.Execute("PRAGMA user_version=8;");
        }

        var migrated = new MatchRecordDatabase(path);
        migrated.Initialize();
        var retained = migrated.Get("retired-v11");
        Assert(retained != null
               && retained.ReplayProtocol == 11
               && retained.ReplayState == MatchReplayStates.SummaryOnly
               && retained.StatisticsJson == "statistics-retained"
               && retained.ContentSha256 == ""
               && retained.RequiredCapabilities.Count == 0
               && retained.CaptureDiagnostics.Any(item => item.Contains("retired", StringComparison.Ordinal)),
            "pre-v12 cutover retains summary and analysis inputs while removing playable identity and capabilities");
        using (var connection = new WinSqliteConnection(path))
        {
            Assert(!TableExists(connection, "replay_chunks")
                   && !TableExists(connection, "replay_timeline_chunks")
                   && TableExists(connection, "replay_truth_chunks")
                   && TableExists(connection, "replay_presentation_chunks")
                   && TableExists(connection, "replay_pov_asset_refs")
                   && MatchRecordsDatabaseMigrator.UserVersion(connection) == MatchRecordsDatabaseMigrator.CurrentVersion,
                "cutover deletes retired replay tables and leaves only the v12 storage surface");
            using var migration = connection.Prepare(
                "SELECT report_path, report_sha256, record_count, chunk_bytes FROM replay_migrations WHERE state='Applied' LIMIT 1;");
            Assert(migration.Read()
                   && File.Exists(Path.Combine(root, migration.Text(0).Replace('/', Path.DirectorySeparatorChar)))
                   && migration.Text(1).Length == 64
                   && migration.Int64(2) == 1
                   && migration.Int64(3) == 999,
                "cutover writes and records a verified audit report before retiring structured data");
        }
        Assert(!File.Exists(retiredAsset),
            "cutover deletes assets that were owned only by retired replay documents");
    }

    private static bool TableExists(WinSqliteConnection connection, string name)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, name);
        return query.Read();
    }

    private static MatchRecord Summary(ReplayDocumentEnvelopeV12 envelope) => new()
    {
        RecordId = envelope.Document.Header.RecordId,
        SessionId = envelope.Document.Header.BattleSessionId,
        AdventureId = envelope.Document.Header.AdventureId,
        LevelId = envelope.Document.Header.LevelId,
        BattleTitle = envelope.Document.Header.BattleTitle,
        Result = envelope.Document.Header.Result,
        StartedUtc = envelope.Document.Header.StartedUtc,
        EndedUtc = envelope.Document.Header.EndedUtc,
        ReplayProtocol = ReplayProtocolV12.DocumentVersion,
        ReplayState = MatchReplayStates.Ready,
        TurnCount = 1
    };
}
