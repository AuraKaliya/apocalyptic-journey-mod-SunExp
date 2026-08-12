using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestDamageHistoryDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-DPS-历史-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "DamageHistory.sqlite3");
        try
        {
            var database = new DamageHistoryDatabase(path);
            database.Initialize();
            const string adventureId = "adventure-unbounded";
            for (var sequence = 1; sequence <= 75; sequence++)
            {
                var record = CreateStoredFight(sequence);
                var stored = database.AppendFight(adventureId, record);
                Assert(stored?.Sequence == sequence, "SQLite assigns monotonic fight sequence " + sequence);
            }

            Assert(database.CountFights(adventureId) == 75,
                "SQLite fight history has no 40-record cap");
            Assert(database.AppendFight(adventureId, CreateStoredFight(75)) == null,
                "SQLite rejects a duplicate fight session within an adventure");

            var firstPage = database.LoadFightPage(adventureId);
            Assert(firstPage.Items.Count == 30
                   && firstPage.Items[0].Sequence == 75
                   && firstPage.Items[^1].Sequence == 46
                   && firstPage.HasMore
                   && firstPage.TotalCount == 75,
                "fight history loads the newest page with a stable keyset cursor");
            var secondPage = database.LoadFightPage(adventureId, firstPage.NextCursor);
            var thirdPage = database.LoadFightPage(adventureId, secondPage.NextCursor);
            Assert(secondPage.Items.Count == 30
                   && secondPage.Items[0].Sequence == 45
                   && thirdPage.Items.Count == 15
                   && thirdPage.Items[^1].Sequence == 1
                   && !thirdPage.HasMore,
                "fight history pages backward without gaps or duplicates");

            database.SaveRunState(adventureId, new DamageRunAggregateSnapshot
            {
                AdventureId = adventureId,
                StartedUtc = "2026-01-01T00:00:00Z",
                UpdatedUtc = "2026-01-02T00:00:00Z",
                EncounterCount = 75,
                TotalRounds = 75,
                ConfirmedEventCount = 75,
                Combatants = new List<CombatantDamageStat>
                {
                    new() { InstanceId = "alpha", DisplayName = "阿尔法", Team = DamageTeam.Friendly, TotalHpDamage = 2850 }
                }
            });
            var restoredRun = database.LoadRunState(adventureId);
            Assert(restoredRun?.ProtocolVersion == DamageMeterProtocol.Version
                   && restoredRun.EncounterCount == 75
                   && restoredRun.Combatants.Single().DisplayName == "阿尔法",
                "run aggregate resumes from a compressed SQLite payload with Unicode intact");

            Assert(database.DeleteFight(adventureId, 75)
                   && database.CountFights(adventureId) == 74,
                "one fight can be deleted independently");

            var avatar = Convert.ToBase64String(Enumerable.Range(0, 128).Select(value => (byte)value).ToArray());
            for (var sequence = 1; sequence <= 125; sequence++)
            {
                Assert(database.AppendAdventure(new OutOfRunDamageHistoryRecord
                {
                    AdventureId = "settlement-" + sequence,
                    ModeId = "Normal",
                    ModeDisplayName = "世界推演",
                    Status = OutOfRunDamageHistoryStatus.Completed,
                    EndedUtc = sequence <= 60
                        ? "2026-01-01T00:00:00Z"
                        : "2026-07-01T00:00:00Z",
                    TeamTotalDamage = sequence * 100,
                    TotalRounds = sequence,
                    TeamDps = 100,
                    TeamMembers = new List<OutOfRunTeamMemberSnapshot>
                    {
                        new()
                        {
                            InstanceId = "alpha",
                            PlayerId = "alpha",
                            PlayerDisplayName = "阿尔法",
                            AvatarPngBase64 = avatar
                        }
                    }
                }), "SQLite archives settlement " + sequence);
            }

            Assert(database.CountAdventures() == 125,
                "SQLite settlement history has no 100-record cap");
            var adventures = database.LoadAdventurePage();
            Assert(adventures.Items.Count == 30
                   && adventures.Items[0].Sequence == 125
                   && adventures.Items[0].TeamMembers[0].AvatarPngBase64 == avatar,
                "settlement pages hydrate deduplicated avatar payloads on demand");
            Assert(new FileInfo(path).Length < 2 * 1024 * 1024,
                "compressed records and deduplicated avatars keep a representative database compact");
            Assert(database.DeleteAdventure(125) && database.CountAdventures() == 124,
                "one settlement and its adventure details can be deleted");
            Assert(database.DeleteAdventuresBefore("2026-06-01T00:00:00Z") == 60
                   && database.CountAdventures() == 64,
                "time-based cleanup removes only settlements older than the cutoff");
            Assert(database.ClearAdventures() == 64 && database.CountAdventures() == 0,
                "settlement history can be cleared without a record cap");
            Assert(database.ClearFights(adventureId) == 74
                   && database.CountFights(adventureId) == 0,
                "the active adventure's remaining fight history can be cleared");

            Assert(!database.HasMeta("migration") , "migration marker starts absent");
            database.SetMeta("migration", "done");
            Assert(database.HasMeta("migration"), "migration marker persists in SQLite");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DamageFightRecord CreateStoredFight(int sequence)
    {
        return new DamageFightRecord
        {
            SessionId = "fight-" + sequence,
            Result = "Win",
            EndedUtc = "2026-08-12T00:00:00Z",
            Snapshot = new DamageMeterSnapshot
            {
                SessionId = "fight-" + sequence,
                CompletedRoundCount = 1,
                Combatants = new List<CombatantDamageStat>
                {
                    new()
                    {
                        InstanceId = "alpha",
                        DisplayName = "阿尔法",
                        Team = DamageTeam.Friendly,
                        TotalHpDamage = sequence,
                        TotalShieldDamage = sequence
                    }
                }
            }
        };
    }
}
