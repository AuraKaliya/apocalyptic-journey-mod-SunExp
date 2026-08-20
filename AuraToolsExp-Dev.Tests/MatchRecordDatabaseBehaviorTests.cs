using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchRecordDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-MatchRecordsV10-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "MatchRecords.sqlite3");
        Directory.CreateDirectory(root);
        try
        {
            var database = new MatchRecordDatabase(path);
            database.Initialize();
            var document = ReplayV10Document("record-v10");
            Assert(ReplayDocumentFinalizerV10.FinalizeAndValidate(document).IsValid,
                "test Replay Document v10 finalizes before storage");
            var record = Summary(document);
            var analysis = MatchAnalysisBuilder.BuildV10(record, document);
            Assert(database.SaveV10(record, document, analysis),
                "SQLite stores the v10 summary, document, timeline, checkpoints, and analysis atomically");
            var loaded = database.LoadV10(record.RecordId);
            Assert(loaded != null
                   && loaded.Header.DocumentVersion == 10
                   && loaded.Header.DocumentSha256 == document.Header.DocumentSha256
                   && loaded.Events.Count == document.Events.Count
                   && ReplayDocumentValidatorV10.Validate(loaded).IsValid,
                "stored Replay Document v10 reloads through checksum-verified timeline chunks");
            Assert(database.Get(record.RecordId)?.ReplayProtocol == 10
                   && database.GetAnalysis(record.RecordId)?.TotalDamage == analysis.TotalDamage,
                "battle summaries and analysis remain independently queryable");

            var summaryOnly = Summary(ReplayV10Document("summary-only"));
            summaryOnly.ReplayState = MatchReplayStates.Incomplete;
            Assert(database.SaveSummaryV10(summaryOnly, new MatchAnalysisReport { RecordId = summaryOnly.RecordId }),
                "a failed recording can retain statistics without manufacturing a playable replay");
            Assert(database.LoadV10(summaryOnly.RecordId) == null
                   && database.Get(summaryOnly.RecordId)?.ReplayState == MatchReplayStates.Incomplete,
                "summary-only rows are never exposed as Replay Document v10");

            var job = new MatchReplayExportJob
            {
                JobId = "job-v10",
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
                FilePath = "Media/record-v10/job.mp4",
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
                   && database.Get(legacy.RecordId)?.ReplayState == MatchReplayStates.Incomplete
                   && database.Get(legacy.RecordId)?.StatisticsJson.Contains("42", StringComparison.Ordinal) == true,
                "authorized legacy cleanup removes old chunks while preserving statistics as analysis-only v10 metadata");

            using (var connection = new WinSqliteConnection(path))
            using (var version = connection.Prepare("PRAGMA user_version;"))
            {
                Assert(version.Read() && version.Int64(0) == MatchRecordsDatabaseMigrator.CurrentVersion,
                    "the shared database records the v10 schema migration version");
            }
            Assert(database.Delete(record.RecordId)
                   && database.LoadV10(record.RecordId) == null
                   && database.LoadMedia(record.RecordId).Count == 0,
                "deleting a match removes its v10 document, media rows, chunks, and task rows");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MatchRecord Summary(ReplayDocumentV10 document)
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
            ReplayProtocol = 10,
            ReplayState = MatchReplayStates.Ready,
            GameBuild = document.Header.GameBuild,
            ToolBuild = document.Header.ToolBuild,
            EventCount = document.Events.Count,
            TurnCount = document.Events.Max(item => item.TurnIndex),
            ContentSha256 = document.Header.DocumentSha256
        };
    }
}
