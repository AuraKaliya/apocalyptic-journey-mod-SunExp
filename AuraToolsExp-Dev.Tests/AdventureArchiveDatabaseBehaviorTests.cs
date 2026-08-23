using AuraToolsExp.Dll.Features.AdventureArchive;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using Newtonsoft.Json.Linq;

internal static partial class AuraToolsTestSuite
{
    public static void TestAdventureArchiveDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AuraTools-AdventureArchive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "MatchRecords.sqlite3");
        try
        {
            CreateLegacyDatabase(path);
            var database = new AdventureArchiveDatabase(path);
            database.Initialize();

            var legacy = database.Load("legacy-adventure");
            var migratedCards = AdventureArchiveProjection.ReadEntries(
                legacy?.Snapshots.Single().CardsJson ?? "[]", "牌组");
            Assert(legacy != null
                   && legacy.Record.SchemaVersion == AdventureArchiveSchema.CurrentVersion
                   && legacy.Record.DataCompleteness == AdventureArchiveSchema.SummaryOnly
                   && legacy.Events.Single().DedupeKey == "legacy-1"
                   && legacy.Events.Single().PayloadJson.Contains("legacy")
                   && migratedCards.Count == 1
                   && migratedCards[0].Id == "legacy-card"
                   && migratedCards[0].Zone == "牌组",
                "adventure history migrates legacy summaries into the single v2 schema");

            database.Begin(new AdventureArchiveRecord
            {
                AdventureId = "adventure-1",
                StartedUtc = "2026-08-18T00:00:00.0000000Z",
                ModeId = "Normal",
                ModeName = "默认模式",
                RoleId = "career-test",
                RoleName = "测试角色",
                GameBuild = "1.0",
                ToolBuild = "2.0",
                ModFingerprint = "fingerprint",
                LatestStage = "start"
            });
            var richEvent = new AdventureArchiveEvent
            {
                OccurredUtc = "2026-08-18T00:01:00.0000000Z",
                Kind = "reward",
                Title = "获得奖励",
                Detail = "测试卡牌",
                PayloadJson = "{\"cardId\":\"card-test\"}",
                DedupeKey = "reward:1"
            };
            database.AppendEvent("adventure-1", richEvent);
            database.AppendEvent("adventure-1", new AdventureArchiveEvent
            {
                OccurredUtc = richEvent.OccurredUtc,
                Kind = richEvent.Kind,
                Title = richEvent.Title,
                Detail = richEvent.Detail,
                PayloadJson = richEvent.PayloadJson,
                DedupeKey = richEvent.DedupeKey
            });
            database.AppendSnapshot("adventure-1", Snapshot(
                "2026-08-18T00:02:00.0000000Z", 10,
                new AdventureArchiveContentEntry
                {
                    Id = "card-test", OwnerModId = "Witch", DisplayName = "测试卡牌", Zone = "当前卡组"
                }));
            database.Complete("adventure-1", "Win");

            var listed = database.List(20);
            var details = database.Load("adventure-1");
            var record = listed.Single(item => item.AdventureId == "adventure-1");
            Assert(listed.Count == 2
                   && record.BattleCount == 1
                   && record.EventCount == 1
                   && record.SnapshotCount == 1
                   && record.DataCompleteness == AdventureArchiveSchema.Rich
                   && record.RoleName == "测试角色"
                   && details != null
                   && details.Record.Status == "complete"
                   && details.Record.Result == "Win"
                   && details.Events.Single().Detail == "测试卡牌"
                   && details.Snapshots.Single().BlessingsJson == "[]"
                   && details.BattleRecordIds.SequenceEqual(new[] { "battle-1" }),
                "adventure history stores rich snapshots, deduplicated events and linked battle records");

            var before = Snapshot("2026-08-18T00:02:00.0000000Z", 10,
                new AdventureArchiveContentEntry { Id = "card-test", DisplayName = "测试卡牌", Zone = "当前卡组" });
            var after = Snapshot("2026-08-18T00:03:00.0000000Z", 15,
                new AdventureArchiveContentEntry { Id = "card-test", DisplayName = "测试卡牌", Zone = "当前卡组", Count = 2 });
            after.RelicsJson = AdventureArchiveProjection.SerializeEntries(new[]
            {
                new AdventureArchiveContentEntry { Id = "relic-test", DisplayName = "测试遗物", Zone = "已装备" }
            });
            var diff = AdventureArchiveProjection.Diff(before, after);
            Assert(diff.Cards.Single().Delta == 1
                   && diff.Relics.Single().Delta == 1
                   && diff.MoneyDelta == 5,
                "adventure history derives readable inventory and resource changes from committed snapshots");

            Assert(database.Delete("adventure-1") && database.List(20).Count == 1,
                "deleting an adventure history removes only history-owned rows");
            using (var connection = new WinSqliteConnection(path))
            using (var query = connection.Prepare("SELECT COUNT(*) FROM battle_records WHERE adventure_id=?;"))
            {
                query.Bind(1, "adventure-1");
                Assert(query.Read() && query.Int64(0) == 1,
                    "adventure history deletion preserves the independent battle record database");
            }
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static AdventureArchiveSnapshot Snapshot(
        string occurredUtc,
        int money,
        params AdventureArchiveContentEntry[] cards)
    {
        return new AdventureArchiveSnapshot
        {
            OccurredUtc = occurredUtc,
            Reason = "test",
            Stage = "map-2",
            RoleId = "career-test",
            CardsJson = AdventureArchiveProjection.SerializeEntries(cards),
            RelicsJson = "[]",
            BlessingsJson = "[]",
            StateJson = new JObject { ["money"] = money }.ToString()
        };
    }

    private static void CreateLegacyDatabase(string path)
    {
        using var connection = new WinSqliteConnection(path);
        connection.Execute("CREATE TABLE battle_records(record_id TEXT PRIMARY KEY, adventure_id TEXT NOT NULL, started_utc TEXT NOT NULL);");
        connection.Execute("CREATE TABLE adventure_archives(adventure_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, status TEXT NOT NULL, result TEXT NOT NULL, mode_id TEXT NOT NULL, role_id TEXT NOT NULL, game_build TEXT NOT NULL, tool_build TEXT NOT NULL, mod_fingerprint TEXT NOT NULL, latest_stage TEXT NOT NULL, event_count INTEGER NOT NULL, snapshot_count INTEGER NOT NULL);");
        connection.Execute("CREATE TABLE adventure_archive_events(adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, occurred_utc TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL, detail TEXT NOT NULL, payload_json TEXT NOT NULL, PRIMARY KEY(adventure_id, sequence));");
        connection.Execute("CREATE TABLE adventure_archive_snapshots(adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, occurred_utc TEXT NOT NULL, reason TEXT NOT NULL, stage TEXT NOT NULL, role_id TEXT NOT NULL, cards_json TEXT NOT NULL, relics_json TEXT NOT NULL, state_json TEXT NOT NULL, PRIMARY KEY(adventure_id, sequence));");
        connection.Execute("INSERT INTO adventure_archives VALUES('legacy-adventure','2026-08-17T00:00:00Z','','in-progress','','Normal','','1','1','legacy','start',1,1);");
        connection.Execute("INSERT INTO adventure_archive_events VALUES('legacy-adventure',1,'2026-08-17T00:01:00Z','adventure-start','冒险开始','Normal','{}');");
        connection.Execute("INSERT INTO adventure_archive_snapshots VALUES('legacy-adventure',1,'2026-08-17T00:02:00Z','start','start','','[\"legacy-card\"]','[]','{}');");
        using var insert = connection.Prepare("INSERT INTO battle_records(record_id, adventure_id, started_utc) VALUES(?, ?, ?);");
        insert.Bind(1, "battle-1");
        insert.Bind(2, "adventure-1");
        insert.Bind(3, "2026-08-18T00:05:00.0000000Z");
        insert.Execute();
    }
}
