using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchRecordDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-MatchRecordsV11-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "MatchRecords.sqlite3");
        Directory.CreateDirectory(root);
        try
        {
            var database = new MatchRecordDatabase(path);
            database.Initialize();
            var document = ReplayV11Document("record-v11");
            Assert(ReplayDocumentFinalizerV11.FinalizeAndValidate(document).IsValid,
                "test Replay Document v11 finalizes before storage");
            var record = Summary(document);
            var analysis = MatchAnalysisBuilder.BuildV11(record, document);
            Assert(database.SaveV11(record, document, analysis),
                "SQLite stores the v11 summary, document, timeline, checkpoints, and analysis atomically");
            var loaded = database.LoadV11(record.RecordId);
            Assert(loaded != null
                   && loaded.Header.DocumentVersion == 11
                   && loaded.Header.DocumentSha256 == document.Header.DocumentSha256
                   && loaded.Events.Count == document.Events.Count
                   && ReplayDocumentValidatorV11.Validate(loaded).IsValid,
                "stored Replay Document v11 reloads through checksum-verified timeline chunks");
            Assert(database.Get(record.RecordId)?.ReplayProtocol == 11
                   && database.GetAnalysis(record.RecordId)?.TotalDamage == analysis.TotalDamage,
                "battle summaries and analysis remain independently queryable");

            var summaryOnly = Summary(ReplayV11Document("summary-only"));
            summaryOnly.ReplayState = MatchReplayStates.SummaryOnly;
            Assert(database.SaveSummaryV11(summaryOnly, new MatchAnalysisReport { RecordId = summaryOnly.RecordId }),
                "a failed recording can retain statistics without manufacturing a playable replay");
            Assert(database.LoadV11(summaryOnly.RecordId) == null
                   && database.Get(summaryOnly.RecordId)?.ReplayState == MatchReplayStates.SummaryOnly,
                "summary-only rows are never exposed as Replay Document v11");

            var rejectedDocument = ReplayV11Document("rejected-v11");
            rejectedDocument.Events[0].Audio.Add(new ReplayAudioCueV11
            {
                NativeResourceId = "Sounds/missing-effect",
                ResolutionPolicy = "embedded-required",
                Kind = "Effect",
                Bus = "Effect"
            });
            Assert(!ReplayDocumentFinalizerV11.FinalizeAndValidate(rejectedDocument).IsValid,
                "test capture draft reproduces a strict missing-PCM rejection");
            var rejectedRecord = Summary(rejectedDocument);
            rejectedRecord.CaptureDiagnostics.Add("required replay audio capture failed [get-data-returned-false]");
            Assert(database.SaveRejectedV11(rejectedRecord, rejectedDocument),
                "a rejected capture preserves its bounded v11 document and timeline as a non-playable diagnostic draft");
            using (var rejected = new WinSqliteConnection(path))
            using (var state = rejected.Prepare(
                       "SELECT document_state, (SELECT COUNT(*) FROM replay_timeline_chunks WHERE record_id=?) "
                       + "FROM replay_documents WHERE record_id=?;"))
            {
                state.Bind(1, rejectedRecord.RecordId);
                state.Bind(2, rejectedRecord.RecordId);
                Assert(state.Read()
                       && state.Text(0) == "Rejected"
                       && state.Int64(1) > 0
                       && database.LoadV11(rejectedRecord.RecordId) == null
                       && database.Get(rejectedRecord.RecordId)?.ReplayState == MatchReplayStates.SummaryOnly,
                    "rejected diagnostic drafts retain evidence but never enter the playable v11 runtime");
            }
            Assert(database.UpdateReplayState(summaryOnly.RecordId, MatchReplayStates.Incomplete),
                "test can reproduce the pre-fix incomplete-without-document state");
            var reopened = new MatchRecordDatabase(path);
            reopened.Initialize();
            Assert(reopened.Get(summaryOnly.RecordId)?.ReplayState == MatchReplayStates.SummaryOnly,
                "startup migration reclassifies v11 rows without a document as summary-only");

            var job = new MatchReplayExportJob
            {
                JobId = "job-v11",
                RecordId = record.RecordId,
                State = MatchReplayExportStates.Planned,
                CreatedUtc = "2026-08-20T00:00:00Z",
                StagingPath = Path.Combine(root, "job.partial.mp4"),
                TargetPath = Path.Combine(root, "job.mp4"),
                ProfileId = "profile",
                Width = 1280,
                Height = 720,
                FramesPerSecond = 30,
                FrameCount = 60
            };
            database.CreateExportJob(job);
            job.State = MatchReplayExportStates.Committing;
            job.OutputSha256 = new string('b', 64);
            Assert(database.UpdateExportJob(job), "export task state transitions use revision compare-and-swap");
            var asset = new MatchMediaAsset
            {
                MediaId = job.JobId,
                RecordId = job.RecordId,
                Format = "MP4",
                FilePath = "Media/record-v11/job.mp4",
                CreatedUtc = "2026-08-20T00:01:00Z",
                DurationMilliseconds = 2000,
                Width = 1280,
                Height = 720,
                FramesPerSecond = 30,
                FileBytes = 1234,
                Sha256 = job.OutputSha256,
                TimelineJson = "[]"
            };
            job.Message = "ready";
            Assert(database.CommitExportMedia(job, asset)
                   && database.LoadExportJob(job.JobId)?.State == MatchReplayExportStates.Ready
                   && database.LoadMedia(record.RecordId).Single().Format == "MP4",
                "media registration and Ready job transition commit in one SQLite transaction");

            var legacyEvents = new List<MatchReplayEvent>
            {
                new() { Sequence = 1, TurnIndex = 1, Kind = MatchReplayEventKinds.ActionFrame }
            };
            var legacyChunks = MatchReplayChunker.Build(legacyEvents, 32 * 1024);
            var legacy = new MatchRecord
            {
                RecordId = "legacy-v9",
                SessionId = "legacy-v9",
                LevelId = "legacy",
                ReplayProtocol = 9,
                ReplayState = MatchReplayStates.Ready,
                StatisticsJson = "{\"total\":42}",
                InitialState = new MatchReplayInitialState { LevelId = "legacy" }
            };
            Assert(database.Save(legacy, legacyChunks)
                   && database.LoadLegacyReplayIds().SequenceEqual(new[] { legacy.RecordId }),
                "the isolated migration boundary can inventory legacy rows without playing them");
            database.SaveMigrationScan("migration-test", "report.json", new string('c', 64), 1, legacyChunks.Sum(item => item.Payload.Length));
            database.ApplyLegacyReplayCleanup(new[] { legacy.RecordId }, "migration-test");
            Assert(database.LoadLegacyReplayIds().Count == 0
                   && database.LoadChunks(legacy.RecordId).Count == 0
                   && database.Get(legacy.RecordId)?.ReplayState == MatchReplayStates.SummaryOnly
                   && database.Get(legacy.RecordId)?.StatisticsJson.Contains("42", StringComparison.Ordinal) == true,
                "authorized legacy cleanup removes old chunks while preserving statistics as analysis-only v11 metadata");

            using (var connection = new WinSqliteConnection(path))
            using (var version = connection.Prepare("PRAGMA user_version;"))
            {
                Assert(version.Read() && version.Int64(0) == MatchRecordsDatabaseMigrator.CurrentVersion,
                    "the shared database records the v11 schema migration version");
            }
            Assert(database.Delete(record.RecordId)
                   && database.LoadV11(record.RecordId) == null
                   && database.LoadMedia(record.RecordId).Count == 0,
                "deleting a match removes its v11 document, media rows, chunks, and task rows");

            var cutoverPath = Path.Combine(root, "legacy-v10.sqlite3");
            using (var legacyConnection = new WinSqliteConnection(cutoverPath))
            {
                legacyConnection.Execute("CREATE TABLE replay_documents(record_id TEXT PRIMARY KEY, document_version INTEGER NOT NULL CHECK(document_version=10));");
                legacyConnection.Execute("INSERT INTO replay_documents(record_id, document_version) VALUES('old-v10', 10);");
            }
            var cutover = new MatchRecordDatabase(cutoverPath);
            cutover.Initialize();
            using (var migrated = new WinSqliteConnection(cutoverPath))
            using (var schema = migrated.Prepare("SELECT sql FROM sqlite_master WHERE type='table' AND name='replay_documents';"))
            using (var migration = migrated.Prepare("SELECT state, record_count FROM replay_migrations WHERE migration_id='replay-v10-to-v11-native-cutover';"))
            {
                Assert(schema.Read()
                       && schema.Text(0).Replace(" ", "").Contains("document_version=11", StringComparison.Ordinal)
                       && migration.Read()
                       && migration.Text(0) == "Applied"
                       && migration.Int64(1) == 1,
                    "startup performs the one-way v10 synthetic-document to v11 native schema cutover");
            }

            var emptyBootstrapPath = Path.Combine(root, "empty-bootstrap-v4.sqlite3");
            var emptyBootstrapSeed = new MatchRecordDatabase(emptyBootstrapPath);
            emptyBootstrapSeed.Initialize();
            var emptyBootstrapDocument = ReplayV11PreMaterializedDocument("empty-bootstrap-v11");
            var retainedBgmHash = emptyBootstrapDocument.Attachments.Single(value => value.Usage == "BattleBgm").Sha256;
            var removedEffectHash = emptyBootstrapDocument.Attachments.Single(value => value.Usage == "SetupEffect").Sha256;
            var emptyBootstrapRecord = Summary(emptyBootstrapDocument);
            Assert(emptyBootstrapSeed.SaveRejectedV11(emptyBootstrapRecord, emptyBootstrapDocument),
                "test database can seed the formerly Ready-compatible empty-baseline v11 shape");
            using (var seed = new WinSqliteConnection(emptyBootstrapPath))
            {
                using (var readyRecord = seed.Prepare(
                           "UPDATE battle_records SET replay_state='Ready' WHERE record_id=?;"))
                {
                    readyRecord.Bind(1, emptyBootstrapRecord.RecordId);
                    readyRecord.Execute();
                }
                using (var readyDocument = seed.Prepare(
                           "UPDATE replay_documents SET document_state='Ready' WHERE record_id=?;"))
                {
                    readyDocument.Bind(1, emptyBootstrapRecord.RecordId);
                    readyDocument.Execute();
                }
                seed.Execute(
                    "DELETE FROM replay_migrations WHERE migration_id='replay-v11-empty-bootstrap-to-materialized-baseline';");
                seed.Execute("PRAGMA user_version=4;");
            }

            var migratedEmptyBootstrap = new MatchRecordDatabase(emptyBootstrapPath);
            migratedEmptyBootstrap.Initialize();
            var migratedDocument = migratedEmptyBootstrap.LoadV11(emptyBootstrapRecord.RecordId);
            var migratedRecord = migratedEmptyBootstrap.Get(emptyBootstrapRecord.RecordId);
            using (var migratedConnection = new WinSqliteConnection(emptyBootstrapPath))
            using (var migration = migratedConnection.Prepare(
                       "SELECT state, record_count FROM replay_migrations "
                       + "WHERE migration_id='replay-v11-empty-bootstrap-to-materialized-baseline';"))
            using (var version = migratedConnection.Prepare("PRAGMA user_version;"))
            {
                Assert(migratedDocument != null
                       && ReplayDocumentValidatorV11.Validate(migratedDocument).IsValid
                       && ReplayPlayableBootstrapContractV11.ValidateState(migratedDocument.InitialState).Count == 0
                       && migratedDocument.Events.First().Audio.Single().Kind == "BattleBgm"
                       && migratedDocument.Events.Skip(1).First().EventType == ReplayEventTypesV11.ActionStarted
                       && migratedRecord?.ReplayState == MatchReplayStates.Ready
                       && migratedRecord.InitialState.BaselineState == null
                       && migratedRecord.CaptureDiagnostics.Any(value =>
                           value.Contains("replay-v11-empty-bootstrap-to-materialized-baseline", StringComparison.Ordinal))
                       && migration.Read()
                       && migration.Text(0) == "Applied"
                       && migration.Int64(1) == 1
                       && version.Read()
                       && version.Int64(0) == MatchRecordsDatabaseMigrator.CurrentVersion,
                    "v5 startup transactionally rebases retained empty-baseline Ready records and records the cutover");
            }
            Assert(File.Exists(Path.Combine(root, "Attachments", retainedBgmHash + ".wav"))
                   && !File.Exists(Path.Combine(root, "Attachments", removedEffectHash + ".wav")),
                "v5 migration retains referenced BGM and deletes the unpaired pre-materialization effect attachment");
            Assert(Directory.GetFiles(root, "empty-bootstrap-v4.sqlite3.backup-v4-*").Length == 1,
                "the materialized-baseline data migration preserves a pre-upgrade v4 database backup");

            var emptyTagPath = Path.Combine(root, "empty-tag-v5.sqlite3");
            var emptyTagSeed = new MatchRecordDatabase(emptyTagPath);
            emptyTagSeed.Initialize();
            var emptyTagDocument = ReplayV11Document("empty-tag-v11");
            emptyTagDocument.InitialState.Cards[0].Values.RemoveAll(value =>
                value.Key == ReplayCardPresentationContractV11.TagKey);
            ReplayDocumentFinalizerV11.FinalizeAndValidate(emptyTagDocument);
            var emptyTagRecord = Summary(emptyTagDocument);
            Assert(emptyTagSeed.SaveRejectedV11(emptyTagRecord, emptyTagDocument),
                "test database can seed the formerly Ready-compatible sparse card presentation shape");
            using (var seed = new WinSqliteConnection(emptyTagPath))
            {
                using (var readyRecord = seed.Prepare(
                           "UPDATE battle_records SET replay_state='Ready' WHERE record_id=?;"))
                {
                    readyRecord.Bind(1, emptyTagRecord.RecordId);
                    readyRecord.Execute();
                }
                using (var readyDocument = seed.Prepare(
                           "UPDATE replay_documents SET document_state='Ready' WHERE record_id=?;"))
                {
                    readyDocument.Bind(1, emptyTagRecord.RecordId);
                    readyDocument.Execute();
                }
                seed.Execute(
                    "DELETE FROM replay_migrations WHERE migration_id='replay-v11-card-presentation-empty-tag';");
                seed.Execute("PRAGMA user_version=5;");
            }

            var corruptEmptyTagPath = Path.Combine(root, "corrupt-empty-tag-v5.sqlite3");
            File.Copy(emptyTagPath, corruptEmptyTagPath);
            using (var corruptSeed = new WinSqliteConnection(corruptEmptyTagPath))
            {
                using var corruptHash = corruptSeed.Prepare(
                    "UPDATE replay_documents SET document_sha256='tampered' WHERE record_id=?;");
                corruptHash.Bind(1, emptyTagRecord.RecordId);
                corruptHash.Execute();
            }

            var migratedEmptyTag = new MatchRecordDatabase(emptyTagPath);
            migratedEmptyTag.Initialize();
            var repairedTagDocument = migratedEmptyTag.LoadV11(emptyTagRecord.RecordId);
            var repairedTagRecord = migratedEmptyTag.Get(emptyTagRecord.RecordId);
            using (var migratedConnection = new WinSqliteConnection(emptyTagPath))
            using (var migration = migratedConnection.Prepare(
                       "SELECT state, record_count FROM replay_migrations "
                       + "WHERE migration_id='replay-v11-card-presentation-empty-tag';"))
            using (var version = migratedConnection.Prepare("PRAGMA user_version;"))
            {
                Assert(repairedTagDocument != null
                       && ReplayDocumentValidatorV11.Validate(repairedTagDocument).IsValid
                       && repairedTagDocument.InitialState.Cards[0].Values.Any(value =>
                           value.Key == ReplayCardPresentationContractV11.TagKey && value.Value == "")
                       && repairedTagRecord?.ReplayState == MatchReplayStates.Ready
                       && repairedTagRecord.CaptureDiagnostics.Any(value =>
                           value.Contains("replay-v11-card-presentation-empty-tag", StringComparison.Ordinal))
                       && migration.Read()
                       && migration.Text(0) == "Applied"
                       && migration.Int64(1) == 1
                       && version.Read()
                       && version.Int64(0) == MatchRecordsDatabaseMigrator.CurrentVersion,
                    "v6 startup repairs sparse empty-Tag card snapshots and keeps retained Ready replays playable");
            }
            Assert(Directory.GetFiles(root, "empty-tag-v5.sqlite3.backup-v5-*").Length == 1,
                "the v6 card-presentation migration preserves a pre-upgrade v5 database backup");

            var corruptEmptyTag = new MatchRecordDatabase(corruptEmptyTagPath);
            corruptEmptyTag.Initialize();
            var corruptEmptyTagRecord = corruptEmptyTag.Get(emptyTagRecord.RecordId);
            using (var corruptConnection = new WinSqliteConnection(corruptEmptyTagPath))
            using (var corruptDocument = corruptConnection.Prepare(
                       "SELECT document_state FROM replay_documents WHERE record_id=?;"))
            {
                corruptDocument.Bind(1, emptyTagRecord.RecordId);
                Assert(corruptEmptyTagRecord?.ReplayState == MatchReplayStates.Corrupt
                       && corruptDocument.Read()
                       && corruptDocument.Text(0) == "Corrupt"
                       && corruptEmptyTagRecord.CaptureDiagnostics.Any(value =>
                           value.Contains("hash is invalid before migration", StringComparison.Ordinal)),
                    "v6 refuses to launder a tampered Ready document while repairing a missing Tag");
            }

            var legacyPcmPath = Path.Combine(root, "legacy-pcm-v6.sqlite3");
            var legacyPcmSeed = new MatchRecordDatabase(legacyPcmPath);
            legacyPcmSeed.Initialize();
            var legacyPcmDocument = ReplayV11Document("legacy-pcm-v11");
            var legacyWave = ReplayPcm16WaveContractV11.BuildPayload(
                new[] { new byte[8] },
                sampleFrames: 2,
                channels: 2,
                sampleRate: 48_000);
            legacyWave[34] = 0;
            legacyWave[35] = 0;
            var legacyWaveHash = ReplayCanonicalJsonV11.Sha256(legacyWave);
            Assert(ReplayPcm16WaveContractV11.TryRepairLegacyMissingBits(
                    legacyWave,
                    out var expectedRepairedWave,
                    out _,
                    out _),
                "the database migration fixture is a bounded missing-bits WAV");
            var repairedWaveHash = ReplayCanonicalJsonV11.Sha256(expectedRepairedWave);
            legacyPcmDocument.Attachments.Add(new ReplayAttachmentV11
            {
                Sha256 = legacyWaveHash,
                MediaType = "audio/wav",
                Extension = ".wav",
                Usage = "BattleBgm",
                ByteLength = legacyWave.Length,
                SampleRate = 48_000,
                Channels = 2,
                SampleFrames = 2,
                Required = true,
                Payload = legacyWave
            });
            legacyPcmDocument.Events[0].Audio.Add(new ReplayAudioCueV11
            {
                AssetSha256 = legacyWaveHash,
                NativeResourceId = "Sounds/test-bgm",
                ResolutionPolicy = "embedded-required",
                Kind = "BattleBgm",
                Bus = "Bgm",
                DurationSamples = 2
            });
            Assert(!ReplayDocumentFinalizerV11.FinalizeAndValidate(legacyPcmDocument).IsValid,
                "the old writer's zero-bit WAV is rejected by the final PCM contract");
            var legacyPcmRecord = Summary(legacyPcmDocument);
            Assert(legacyPcmSeed.SaveRejectedV11(legacyPcmRecord, legacyPcmDocument),
                "test database can seed the legacy malformed WAV document without bypassing production storage");
            using (var seed = new WinSqliteConnection(legacyPcmPath))
            {
                using (var readyRecord = seed.Prepare(
                           "UPDATE battle_records SET replay_state='Ready' WHERE record_id=?;"))
                {
                    readyRecord.Bind(1, legacyPcmRecord.RecordId);
                    readyRecord.Execute();
                }
                using (var readyDocument = seed.Prepare(
                           "UPDATE replay_documents SET document_state='Ready' WHERE record_id=?;"))
                {
                    readyDocument.Bind(1, legacyPcmRecord.RecordId);
                    readyDocument.Execute();
                }
                seed.Execute(
                    "DELETE FROM replay_migrations WHERE migration_id='replay-v11-pcm16-wave-header';");
                seed.Execute("PRAGMA user_version=6;");
            }

            var migratedPcm = new MatchRecordDatabase(legacyPcmPath);
            migratedPcm.Initialize();
            var repairedPcmDocument = migratedPcm.LoadV11(legacyPcmRecord.RecordId, loadAttachmentPayloads: true);
            var repairedPcmRecord = migratedPcm.Get(legacyPcmRecord.RecordId);
            var repairedPcmPath = Path.Combine(root, "Attachments", repairedWaveHash + ".wav");
            var archivedLegacyPcmPath = Path.Combine(
                root,
                "Quarantine",
                "Attachments",
                legacyWaveHash + ".wav.legacy-pcm-v6");
            using (var migratedConnection = new WinSqliteConnection(legacyPcmPath))
            using (var migration = migratedConnection.Prepare(
                       "SELECT state, record_count FROM replay_migrations "
                       + "WHERE migration_id='replay-v11-pcm16-wave-header';"))
            using (var reference = migratedConnection.Prepare(
                       "SELECT asset_sha256 FROM replay_asset_refs WHERE record_id=? AND usage='BattleBgm';"))
            {
                reference.Bind(1, legacyPcmRecord.RecordId);
                Assert(repairedPcmDocument != null
                       && ReplayDocumentValidatorV11.Validate(repairedPcmDocument).IsValid
                       && repairedPcmDocument.Events[0].Audio.Single().AssetSha256 == repairedWaveHash
                       && repairedPcmDocument.Attachments.Single().Sha256 == repairedWaveHash
                       && ReplayPcm16WaveContractV11.TryRead(
                           repairedPcmDocument.Attachments.Single().Payload,
                           out var repairedWaveInfo,
                           out _)
                       && repairedWaveInfo.BitsPerSample == 16
                       && repairedPcmRecord?.ReplayState == MatchReplayStates.Ready
                       && repairedPcmRecord.CaptureDiagnostics.Any(value =>
                           value.Contains("replay-v11-pcm16-wave-header", StringComparison.Ordinal))
                       && migration.Read()
                       && migration.Text(0) == "Applied"
                       && migration.Int64(1) == 1
                       && reference.Read()
                       && reference.Text(0) == repairedWaveHash
                       && File.Exists(repairedPcmPath)
                       && File.Exists(archivedLegacyPcmPath)
                       && !File.Exists(Path.Combine(root, "Attachments", legacyWaveHash + ".wav")),
                    "v7 atomically rewrites PCM content ids, replay hashes and refs while archiving the old WAV");
            }
            Assert(Directory.GetFiles(root, "legacy-pcm-v6.sqlite3.backup-v6-*").Length == 1,
                "the v7 PCM migration preserves a pre-upgrade v6 database backup");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MatchRecord Summary(ReplayDocumentV11 document)
    {
        return new MatchRecord
        {
            RecordId = document.Header.RecordId,
            SessionId = document.Header.SessionId,
            AdventureId = document.Header.AdventureId,
            LevelId = document.Header.LevelId,
            Result = document.Header.Result,
            StartedUtc = document.Header.StartedUtc,
            EndedUtc = document.Header.EndedUtc,
            Collection = MatchRecordCollections.Auto,
            ReplayProtocol = 11,
            ReplayState = MatchReplayStates.Ready,
            GameBuild = document.Header.GameBuild,
            ToolBuild = document.Header.ToolBuild,
            EventCount = document.Events.Count,
            TurnCount = document.Events.Max(item => item.TurnIndex),
            ContentSha256 = document.Header.DocumentSha256
        };
    }
}
