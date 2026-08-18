using AuraToolsExp.Dll.Features.AdventureArchive;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestAdventureArchiveDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AuraTools-AdventureArchive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "MatchRecords.sqlite3");
        try
        {
            using (var connection = new WinSqliteConnection(path))
            {
                connection.Execute("CREATE TABLE battle_records(record_id TEXT PRIMARY KEY, adventure_id TEXT NOT NULL, started_utc TEXT NOT NULL);");
                using var insert = connection.Prepare("INSERT INTO battle_records(record_id, adventure_id, started_utc) VALUES(?, ?, ?);");
                insert.Bind(1, "battle-1");
                insert.Bind(2, "adventure-1");
                insert.Bind(3, "2026-08-18T00:05:00.0000000Z");
                insert.Execute();
            }

            var database = new AdventureArchiveDatabase(path);
            database.Initialize();
            database.Begin(new AdventureArchiveRecord
            {
                AdventureId = "adventure-1",
                StartedUtc = "2026-08-18T00:00:00.0000000Z",
                ModeId = "Normal",
                RoleId = "career-test",
                GameBuild = "1.0",
                ToolBuild = "2.0",
                ModFingerprint = "fingerprint",
                LatestStage = "start"
            });
            database.AppendEvent("adventure-1", new AdventureArchiveEvent
            {
                OccurredUtc = "2026-08-18T00:01:00.0000000Z",
                Kind = "reward",
                Title = "Reward",
                Detail = "card-test"
            });
            database.AppendSnapshot("adventure-1", new AdventureArchiveSnapshot
            {
                OccurredUtc = "2026-08-18T00:02:00.0000000Z",
                Reason = "reward",
                Stage = "map-2",
                RoleId = "career-test",
                CardsJson = "[\"card-test\"]",
                RelicsJson = "[]",
                StateJson = "{}"
            });
            database.Complete("adventure-1", "Win");

            var listed = database.List(20);
            var details = database.Load("adventure-1");
            Assert(listed.Count == 1
                   && listed[0].BattleCount == 1
                   && listed[0].EventCount == 1
                   && listed[0].SnapshotCount == 1
                   && details != null
                   && details.Record.Status == "complete"
                   && details.Record.Result == "Win"
                   && details.Events.Single().Detail == "card-test"
                   && details.Snapshots.Single().Stage == "map-2"
                   && details.BattleRecordIds.SequenceEqual(new[] { "battle-1" }),
                "adventure archive stores a low-frequency timeline and links battle records by AdventureId");

            Assert(database.Delete("adventure-1") && database.List(20).Count == 0,
                "deleting an adventure archive removes only archive-owned rows");
            using (var connection = new WinSqliteConnection(path))
            using (var query = connection.Prepare("SELECT COUNT(*) FROM battle_records WHERE adventure_id=?;"))
            {
                query.Bind(1, "adventure-1");
                Assert(query.Read() && query.Int64(0) == 1,
                    "adventure archive deletion preserves the independent battle record database");
            }
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }
}
