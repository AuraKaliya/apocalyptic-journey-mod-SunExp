using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    private const string V17CutoverId = "replay-pre17-to-v17-native-presentation-cutover";

    public static void TestMatchRecordDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-MatchRecordsV17-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestV17Storage(root);
            TestIncrementalCaptureRecovery(root);
            TestV17CutoverIdempotence(root);
            TestPreV17Cutover(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestV17Storage(string root)
    {
        var path = Path.Combine(root, "v17.db");
        var database = new MatchRecordDatabase(path);
        database.Initialize();
        var envelope = BuildReplayV17("database-v17");
        var validation = ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope);
        Assert(validation.IsValid, "database fixture is valid: " + validation.Message);
        var record = Summary(envelope);
        var analysis = MatchAnalysisBuilder.BuildV17(record, envelope.Document);
        Assert(database.SaveV17(record, envelope, analysis)
               && !database.SaveV17(Summary(envelope), envelope, analysis),
            "database atomically stores one validated v17 document and rejects duplicate ready ids");

        var loaded = database.LoadV17(record.RecordId, loadAssetPayloads: true);
        var manifestOnly = database.LoadV17(record.RecordId, loadAssetPayloads: false);
        Assert(loaded != null
               && loaded.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
               && ReplayDocumentValidatorV17.Validate(loaded).IsValid
               && loaded.Document.TruthEvents.Count == envelope.Document.TruthEvents.Count
               && loaded.Document.PresentationEvents.Count == envelope.Document.PresentationEvents.Count
               && loaded.Document.Assets.Single().Payload.SequenceEqual(envelope.Document.Assets.Single().Payload),
            "database restores both lanes, paired checkpoints, and content-addressed dynamic assets");
        Assert(manifestOnly != null
               && manifestOnly.Document.Assets.Single().Payload.Length == 0
               && ReplayDocumentValidatorV17.Validate(manifestOnly).IsValid
               && database.GetAnalysis(record.RecordId)?.RecordId == record.RecordId
               && database.ContainsContentHash(envelope.DeclaredDocumentRoot),
            "manifest-only validation and analysis lookup do not load asset bytes");

        var rejected = new MatchRecord
        {
            RecordId = "rejected-v17",
            SessionId = "rejected-v17",
            LevelId = "level-test",
            ReplayProtocol = ReplayProtocolV17.DocumentVersion
        };
        Assert(database.SaveSummaryV17(rejected, MatchAnalysisBuilder.BuildSummary(rejected), rejected: true)
               && database.Get(rejected.RecordId)?.ReplayState == MatchReplayStates.Rejected
               && database.LoadV17(rejected.RecordId) == null,
            "a rejected structured capture stores only its summary");

        var assetPath = database.ResolveReplayAsset(envelope.Document.Assets.Single().Sha256);
        Assert(File.Exists(assetPath)
               && database.Delete(record.RecordId)
               && !File.Exists(assetPath)
               && MatchRecordsDatabaseMigrator.IntegrityCheck(path) == "ok",
            "deleting the last v17 reference removes its asset and preserves SQLite integrity");
    }

    private static void TestIncrementalCaptureRecovery(string root)
    {
        var path = Path.Combine(root, "capture-recovery.db");
        var database = new MatchRecordDatabase(path);
        database.Initialize();
        var envelope = BuildReplayV17("capture-finalizing");
        var ordered = envelope.Document.TruthEvents.Concat(envelope.Document.PresentationEvents)
            .OrderBy(item => item.Sequence).ToList();
        var splitSequence = ordered[ordered.Count / 2].Sequence;
        var first = CaptureBatch(envelope.Document, 0, item => item.Sequence <= splitSequence);
        var second = CaptureBatch(envelope.Document, 1, item => item.Sequence > splitSequence);
        var record = Summary(envelope);
        record.ReplayState = MatchReplayStates.Recording;
        database.BeginCaptureV17(record, envelope.Document.Header, envelope.Document.InitialState, first);
        Assert(database.Get(record.RecordId)?.ReplayState == MatchReplayStates.Recording
               && database.AppendCaptureBatchV17(record.RecordId, second)
               && database.AppendCaptureBatchV17(record.RecordId, second),
            "recording starts durably and incremental batch retries are idempotent");

        var conflicting = ReplayCanonicalJsonV17.Clone(second);
        conflicting.PresentationEvents[0].EventType = ReplayEventTypesV17.AudioPresented;
        conflicting.BatchSha256 = "";
        var conflictRejected = false;
        try { database.AppendCaptureBatchV17(record.RecordId, conflicting); }
        catch (InvalidDataException) { conflictRejected = true; }
        Assert(conflictRejected, "an incremental batch index cannot be overwritten by different content");

        database.SaveFinalizingCaptureV17(record, envelope, Array.Empty<string>());
        Assert(database.Get(record.RecordId)?.ReplayState == MatchReplayStates.Finalizing,
            "the complete terminal draft is committed before background canonical finalization");

        var restarted = new MatchRecordDatabase(path);
        restarted.Initialize();
        Assert(restarted.RecoverFinalizingCapturesV17() == 1,
            "a process restart resumes a durably stored finalization draft");
        var recovered = restarted.LoadV17(record.RecordId, loadAssetPayloads: true);
        Assert(restarted.Get(record.RecordId)?.ReplayState == MatchReplayStates.Ready
               && recovered != null
               && ReplayDocumentValidatorV17.Validate(recovered).IsValid,
            "recovery transitions Finalizing to Ready only after canonical validation and atomic storage");
        using (var connection = new WinSqliteConnection(path))
            Assert(Scalar(connection, "SELECT COUNT(*) FROM replay_capture_sessions;") == 0
                   && Scalar(connection, "SELECT COUNT(*) FROM replay_capture_batches;") == 0,
                "successful recovery removes the retired recording/finalizing draft rows");

        var interruptedEnvelope = BuildReplayV17("capture-interrupted");
        var interruptedRecord = Summary(interruptedEnvelope);
        interruptedRecord.ReplayState = MatchReplayStates.Recording;
        var interruptedBatch = CaptureBatch(interruptedEnvelope.Document, 0, _ => true);
        restarted.BeginCaptureV17(
            interruptedRecord,
            interruptedEnvelope.Document.Header,
            interruptedEnvelope.Document.InitialState,
            interruptedBatch);
        var secondRestart = new MatchRecordDatabase(path);
        secondRestart.Initialize();
        Assert(secondRestart.RecoverFinalizingCapturesV17() == 0
               && secondRestart.Get(interruptedRecord.RecordId)?.ReplayState == MatchReplayStates.Incomplete
               && secondRestart.LoadV17(interruptedRecord.RecordId) == null,
            "a crash during Recording remains an auditable incomplete capture and never masquerades as Ready");
    }

    private static void TestV17CutoverIdempotence(string root)
    {
        var path = Path.Combine(root, "v17-cutover-idempotence.db");
        var database = new MatchRecordDatabase(path);
        database.Initialize();
        var envelope = BuildReplayV17("v17-survives-cutover-audit");
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope).IsValid
               && database.SaveV17(Summary(envelope), envelope),
            "cutover idempotence fixture stores a current v17 replay");
        var assetPath = database.ResolveReplayAsset(envelope.Document.Assets.Single().Sha256);
        using (var connection = new WinSqliteConnection(path))
            connection.Execute("DELETE FROM replay_migrations;");

        var reopened = new MatchRecordDatabase(path);
        reopened.Initialize();
        var loaded = reopened.LoadV17(envelope.Document.Header.RecordId, loadAssetPayloads: true);
        Assert(loaded != null
               && loaded.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
               && File.Exists(assetPath)
               && ReplayDocumentValidatorV17.Validate(loaded).IsValid,
            "rerunning the cutover audit never drops an already-valid v17 replay or shared asset");
    }

    private static void TestPreV17Cutover(string root)
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
            InsertLegacyRecord(connection, "retired-v11", 11, 999, "statistics-v11", "old-level");
            InsertLegacyRecord(connection, "retired-v13", 13, 888, "statistics-v13", "v13-level");
            InsertLegacyRecord(connection, "retired-v14", 14, 777, "statistics-v14", "v14-level");
            InsertLegacyRecord(connection, "retired-v15", 15, 666, "statistics-v15", "v15-level");
            InsertLegacyRecord(connection, "retired-v16", 16, 555, "statistics-v16", "v16-level");
            connection.Execute("CREATE TABLE replay_documents(record_id TEXT PRIMARY KEY, document_version INTEGER NOT NULL);");
            connection.Execute("INSERT INTO replay_documents(record_id, document_version) VALUES('retired-v13', 13);");
            connection.Execute("INSERT INTO replay_documents(record_id, document_version) VALUES('retired-v14', 14);");
            connection.Execute("INSERT INTO replay_documents(record_id, document_version) VALUES('retired-v15', 15);");
            connection.Execute("INSERT INTO replay_documents(record_id, document_version) VALUES('retired-v16', 16);");
            connection.Execute("CREATE TABLE replay_chunks(record_id TEXT, chunk_index INTEGER, payload BLOB);");
            connection.Execute("CREATE TABLE replay_pov_sidecars(record_id TEXT, player_id TEXT);");
            connection.Execute("CREATE TABLE replay_pov_asset_refs(record_id TEXT, player_id TEXT, asset_sha256 TEXT);");
            connection.Execute("CREATE TABLE replay_assets(asset_sha256 TEXT, file_path TEXT);");
            using var asset = connection.Prepare("INSERT INTO replay_assets(asset_sha256, file_path) VALUES(?, ?);");
            asset.Bind(1, new string('a', 64));
            asset.Bind(2, "Attachments/" + new string('a', 64) + ".bin");
            asset.Execute();
            connection.Execute("PRAGMA user_version=13;");
        }

        var migrated = new MatchRecordDatabase(path);
        migrated.Initialize();
        var v11 = migrated.Get("retired-v11");
        var v13 = migrated.Get("retired-v13");
        var v14 = migrated.Get("retired-v14");
        var v15 = migrated.Get("retired-v15");
        var v16 = migrated.Get("retired-v16");
        Assert(v11?.ReplayState == MatchReplayStates.SummaryOnly
               && v11.StatisticsJson == "statistics-v11"
               && v11.ContentSha256 == ""
               && v13?.ReplayState == MatchReplayStates.SummaryOnly
               && v13.StatisticsJson == "statistics-v13"
               && v13.ContentSha256 == ""
               && v14?.ReplayState == MatchReplayStates.SummaryOnly
               && v14.StatisticsJson == "statistics-v14"
               && v14.ContentSha256 == ""
               && v15?.ReplayState == MatchReplayStates.SummaryOnly
               && v15.StatisticsJson == "statistics-v15"
               && v15.ContentSha256 == ""
               && v16?.ReplayState == MatchReplayStates.SummaryOnly
               && v16.StatisticsJson == "statistics-v16"
               && v16.ContentSha256 == "",
            "v17 cutover retains pre-v17 summaries while retiring every old structured replay identity");
        using (var connection = new WinSqliteConnection(path))
        {
            Assert(!TableExists(connection, "replay_chunks")
                   && !TableExists(connection, "replay_pov_sidecars")
                   && !TableExists(connection, "replay_pov_asset_refs")
                   && TableExists(connection, "replay_truth_chunks")
                   && TableExists(connection, "replay_presentation_chunks")
                   && TableExists(connection, "replay_capture_sessions")
                   && TableExists(connection, "replay_capture_batches")
                   && MatchRecordsDatabaseMigrator.UserVersion(connection) == 14,
                "cutover leaves one v17 storage and durability surface with no POV/DOM tables");
            using var migration = connection.Prepare(
                "SELECT report_path, report_sha256, record_count, chunk_bytes FROM replay_migrations "
                + "WHERE migration_id=? AND state='Applied' LIMIT 1;");
            migration.Bind(1, V17CutoverId);
            Assert(migration.Read()
                   && File.Exists(Path.Combine(root, migration.Text(0).Replace('/', Path.DirectorySeparatorChar)))
                   && migration.Text(1).Length == 64
                   && migration.Int64(2) == 5
                   && migration.Int64(3) == 3885,
                "cutover records a verified audit report before deleting pre-v17 structures");
        }
        Assert(!File.Exists(retiredAsset), "cutover deletes assets referenced only by retired replay structures");

        File.WriteAllBytes(retiredAsset, new byte[] { 7, 8, 9 });
        using (var connection = new WinSqliteConnection(path))
        {
            using var update = connection.Prepare(
                "UPDATE replay_migrations SET state='PendingCleanup', applied_utc='' WHERE migration_id=?;");
            update.Bind(1, V17CutoverId);
            update.Execute();
        }
        var resumed = new MatchRecordDatabase(path);
        resumed.Initialize();
        using (var connection = new WinSqliteConnection(path))
        using (var migration = connection.Prepare(
                   "SELECT state FROM replay_migrations WHERE migration_id=?;"))
        {
            migration.Bind(1, V17CutoverId);
            Assert(!File.Exists(retiredAsset) && migration.Read() && migration.Text(0) == "Applied",
                "cutover resumes PendingCleanup before durably marking the migration Applied");
        }
    }

    private static ReplayCaptureBatchV17 CaptureBatch(
        ReplayDocumentV17 document,
        int index,
        Func<ReplayJournalEventV17, bool> predicate)
    {
        var truth = document.TruthEvents.Where(predicate).Select(ReplayCanonicalJsonV17.Clone).ToList();
        var presentation = document.PresentationEvents.Where(predicate).Select(ReplayCanonicalJsonV17.Clone).ToList();
        var all = truth.Concat(presentation).OrderBy(item => item.Sequence).ToList();
        var batch = new ReplayCaptureBatchV17
        {
            BatchIndex = index,
            FirstSequence = all.First().Sequence,
            LastSequence = all.Last().Sequence,
            TruthEvents = truth,
            PresentationEvents = presentation,
            Presentation = ReplayCanonicalJsonV17.Clone(document.Presentation),
            Assets = document.Assets.Select(ReplayCanonicalJsonV17.CloneAssetWithPayload).ToList()
        };
        var hashSource = ReplayCanonicalJsonV17.Clone(batch);
        hashSource.BatchSha256 = "";
        batch.BatchSha256 = ReplayCanonicalJsonV17.Sha256(hashSource);
        return batch;
    }

    private static void InsertLegacyRecord(
        WinSqliteConnection connection,
        string id,
        int protocol,
        long bytes,
        string statistics,
        string level)
    {
        using var insert = connection.Prepare(
            "INSERT INTO battle_records(record_id, adventure_id, session_id, level_id, result, started_utc, ended_utc, "
            + "collection_kind, replay_state, replay_protocol, game_build, tool_build, mod_fingerprint, event_count, turn_count, "
            + "compressed_bytes, statistics_payload, initial_payload, metadata_payload) VALUES(?, '', ?, ?, 'Win', '', '', "
            + "'Auto', 'Ready', ?, '', '', '', 8, 2, ?, ?, ?, ?);");
        insert.Bind(1, id);
        insert.Bind(2, id);
        insert.Bind(3, level);
        insert.Bind(4, protocol);
        insert.Bind(5, bytes);
        insert.Bind(6, MatchReplayPayload.Encode(statistics));
        insert.Bind(7, MatchReplayPayload.Encode(new MatchReplayInitialState { LevelId = level }));
        insert.Bind(8, MatchReplayPayload.Encode(new MatchRecordMetadata
        {
            BattleTitle = "Retired " + protocol,
            ContentSha256 = new string('b', 64),
            RequiredCapabilities = new List<string> { "old-capability" }
        }));
        insert.Execute();
    }

    private static long Scalar(WinSqliteConnection connection, string sql)
    {
        using var query = connection.Prepare(sql);
        return query.Read() ? query.Int64(0) : -1;
    }

    private static bool TableExists(WinSqliteConnection connection, string name)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, name);
        return query.Read();
    }

    private static MatchRecord Summary(ReplayDocumentEnvelopeV17 envelope) => new()
    {
        RecordId = envelope.Document.Header.RecordId,
        SessionId = envelope.Document.Header.BattleSessionId,
        AdventureId = envelope.Document.Header.AdventureId,
        LevelId = envelope.Document.Header.LevelId,
        BattleTitle = envelope.Document.Header.BattleTitle,
        Result = envelope.Document.Header.Result,
        StartedUtc = envelope.Document.Header.StartedUtc,
        EndedUtc = envelope.Document.Header.EndedUtc,
        ReplayProtocol = ReplayProtocolV17.DocumentVersion,
        ReplayState = MatchReplayStates.Ready,
        GameBuild = envelope.Document.Header.GameBuildProvenance,
        ToolBuild = envelope.Document.Header.RecorderBuild,
        TurnCount = 1
    };
}
