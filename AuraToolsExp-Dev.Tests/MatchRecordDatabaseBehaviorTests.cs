using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchRecordDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-MatchRecords-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "MatchRecords.sqlite3");
        try
        {
            var database = new MatchRecordDatabase(path);
            database.Initialize();
            for (var index = 1; index <= 25; index++)
            {
                var events = Enumerable.Range(1, 6).Select(sequence => new MatchReplayEvent
                {
                    Sequence = sequence,
                    TurnIndex = (sequence + 1) / 2,
                    ElapsedMilliseconds = sequence * 100,
                    Kind = MatchReplayEventKinds.ActionCommand,
                    TypeName = "Action." + sequence,
                    Payload = Enumerable.Repeat((byte)(index + sequence), 9000).ToArray()
                }).ToList();
                var chunks = MatchReplayChunker.Build(events, 32 * 1024);
                Assert(chunks.Count >= 2, "replay payload is split into bounded compressed chunks");
                Assert(database.Save(new MatchRecord
                {
                    RecordId = "record-" + index,
                    AdventureId = "adventure-1",
                    SessionId = "session-" + index,
                    LevelId = "level-" + index,
                    Result = "Win",
                    StartedUtc = "2026-08-12T00:00:00Z",
                    EndedUtc = "2026-08-12T00:01:00Z",
                    EventCount = events.Count,
                    TurnCount = 3,
                    GameBuild = "game",
                    ToolBuild = "tool",
                    ModFingerprint = "fingerprint",
                    StatisticsJson = "{\"total\":" + index + "}",
                    InitialState = new MatchReplayInitialState
                    {
                        LevelId = "level-" + index,
                        RoleQueue = new byte[] { 1, 2, 3 },
                        TemporaryRoles = new byte[] { 4, 5 },
                        RoleTableJson = "{}"
                    }
                }, chunks), "SQLite stores replay " + index + " atomically");
            }

            Assert(database.SetCollection("record-1", MatchRecordCollections.Favorite),
                "an automatic record can move into favorites");
            Assert(database.EnforceAutoLimit(5) == 19
                   && database.Count(MatchRecordCollections.Auto) == 5
                   && database.Count(MatchRecordCollections.Favorite) == 1,
                "automatic retention removes only excess automatic replays and never favorites");

            var favorite = database.Get("record-1");
            var chunksRestored = database.LoadChunks("record-1");
            var eventsRestored = MatchReplayChunker.Decode(chunksRestored);
            Assert(favorite?.Collection == MatchRecordCollections.Favorite
                   && favorite.InitialState.LevelId == "level-1"
                   && favorite.StatisticsJson.Contains("total")
                   && eventsRestored.Count == 6
                   && eventsRestored[^1].Sequence == 6,
                "record metadata, initial snapshot, statistics, and checked chunks load on demand");

            var analysis = new MatchAnalysisReport
            {
                RecordId = "record-1",
                GeneratedUtc = "2026-08-12T00:02:00Z",
                TurnCount = 3,
                TotalDamage = 321,
                Turns = new List<MatchAnalysisTurn>
                {
                    new() { TurnIndex = 1, Damage = 123, FirstEventSequence = 1, LastEventSequence = 2 }
                }
            };
            database.SaveAnalysis(analysis);
            Assert(database.GetAnalysis("record-1")?.Turns.Single().Damage == 123,
                "checked factual analysis payloads persist separately from replay chunks");

            var media = new MatchMediaAsset
            {
                MediaId = "media-1",
                RecordId = "record-1",
                Format = "AVI",
                FilePath = Path.Combine(root, "media.avi"),
                CreatedUtc = "2026-08-12T00:03:00Z",
                DurationMilliseconds = 12345,
                Width = 1280,
                Height = 720,
                FramesPerSecond = 30,
                FileBytes = 4567,
                Sha256 = "abc",
                TimelineJson = "[]"
            };
            database.SaveMedia(media);
            Assert(database.LoadMedia("record-1").Single().FramesPerSecond == 30d
                   && database.DeleteMedia("media-1")?.DurationMilliseconds == 12345
                   && database.LoadMedia("record-1").Count == 0,
                "video metadata remains compact in SQLite while media bytes stay outside the database");

            var damaged = chunksRestored.Select(item => new MatchReplayChunk
            {
                ChunkIndex = item.ChunkIndex,
                FirstSequence = item.FirstSequence,
                LastSequence = item.LastSequence,
                FirstTurnIndex = item.FirstTurnIndex,
                LastTurnIndex = item.LastTurnIndex,
                Sha256 = item.Sha256,
                Payload = (byte[])item.Payload.Clone()
            }).ToList();
            damaged[0].Payload[0] ^= 0xff;
            var checksumRejected = false;
            try
            {
                MatchReplayChunker.Decode(damaged);
            }
            catch (InvalidDataException)
            {
                checksumRejected = true;
            }

            Assert(checksumRejected, "corrupt replay chunks are rejected before playback");
            Assert(database.SetCollection("record-1", MatchRecordCollections.Auto)
                   && database.EnforceAutoLimit(5) == 1
                   && database.Count(MatchRecordCollections.Favorite) == 0
                   && database.Count(MatchRecordCollections.Auto) == 5,
                "moving a favorite back to automatic storage immediately reapplies retention");

            var page = database.LoadPage(MatchRecordCollections.Auto, pageSize: 3);
            Assert(page.Items.Count == 3 && page.HasMore && page.TotalCount == 5,
                "match records use keyset paging for lazy UI loading");
            Assert(database.Delete(page.Items[0].RecordId)
                   && database.Count(MatchRecordCollections.Auto) == 4,
                "one replay and all of its chunks can be deleted");
            Assert(database.Clear(MatchRecordCollections.Auto) == 4
                   && database.Count(MatchRecordCollections.Auto) == 0,
                "a replay collection can be cleared independently");
            Assert(new FileInfo(path).Length < 3 * 1024 * 1024,
                "representative replay metadata and compressed chunks remain compact in SQLite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
