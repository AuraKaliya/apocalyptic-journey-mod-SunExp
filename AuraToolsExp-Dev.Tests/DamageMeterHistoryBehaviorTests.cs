using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Input;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraSkin.Shared.Models;
using Newtonsoft.Json;
internal static partial class AuraToolsTestSuite
{
    public static void TestDetailLimit()
    {
        var ledger = NewLedger();
        ledger.StartRound(1);
        for (var i = 1; i <= DamageMeterProtocol.MaxDetailsPerCombatant + 10; i++)
        {
            Apply(ledger, i, "p1", 1, 0, DamageTeam.Friendly, "detail_" + i);
        }
    
        var details = ledger.Combatants.Single().Details;
        Assert(details.Count <= DamageMeterProtocol.MaxDetailsPerCombatant, "detail cardinality bounded");
        Assert(details.ContainsKey("other"), "overflow details merge into other");
    }
    
    public static void TestAdventureHistory()
    {
        var history = new DamageHistoryStore();
        var first = NewLedger();
        first.StartRound(1);
        Apply(first, 1, "p1", 25, 5, DamageTeam.Friendly, "card");
        first.EndFight();
        Assert(history.Archive(first.CreateSnapshot(), "Win", "2026-06-25T00:00:00Z"),
            "completed fight archived");
        Assert(!history.Archive(first.CreateSnapshot(), "Win", "2026-06-25T00:00:01Z"),
            "fight session archived only once");
    
        for (var index = 2; index <= DamageMeterProtocol.MaxFightHistory + 3; index++)
        {
            var ledger = new DamageLedger();
            ledger.StartFight("session-" + index, true);
            ledger.StartRound(1);
            Apply(ledger, 1, "p1", index, 0, DamageTeam.Friendly, "card");
            ledger.EndFight();
            Assert(history.Archive(ledger.CreateSnapshot(), "Win", index.ToString()),
                "additional fight archived " + index);
        }
    
        Assert(history.Records.Count == DamageMeterProtocol.MaxFightHistory,
            "adventure history remains bounded");
        Assert(history.Records[0].SessionId == "session-4",
            "oldest history entries are trimmed first");
    
        var restored = new DamageHistoryStore();
        restored.ApplySnapshot(history.CreateSnapshot());
        Assert(restored.Records.Count == history.Records.Count
               && restored.Records[^1].Snapshot.Combatants.Single().TotalHpDamage
               == history.Records[^1].Snapshot.Combatants.Single().TotalHpDamage,
            "history snapshot round-trips with combat details");
    }
    
    public static void TestBestHitAndScientificFormat()
    {
        Assert(DamageMeterFormatters.FormatScientific(12345) == "1.234 E+04",
            "scientific formatter truncates mantissa and keeps exponent width");
        Assert(DamageMeterFormatters.TrimDisplayName("ABCDEFGHIJKLMN") == "ABCDEFGHIJKL",
            "display name keeps exactly twelve visible characters");
    
        var ledger = NewLedger();
        ledger.StartRound(1);
        Apply(ledger, 1, "p1", 25, 0, DamageTeam.Friendly, "small");
        Apply(ledger, 2, "p2", 200, 10, DamageTeam.Friendly, "big");
        Apply(ledger, 3, "p1", 150, 100, DamageTeam.Friendly, "bigger");
    
        var bestHit = ledger.BestHit();
        Assert(bestHit != null
               && bestHit.RecordName == DamageMeterRecordNames.BestHit
               && bestHit.Damage == 250
               && bestHit.SourceInstanceId == "p1",
            "best hit tracks the largest single event");
    
        ledger.EndFight();
        var history = new DamageHistoryStore();
        Assert(history.Archive(ledger.CreateSnapshot(), "Win", "2026-06-27T00:00:00Z"),
            "best-hit fight archived");
        Assert(history.Records[0].Snapshot.BestHit?.Damage == 250,
            "best hit survives adventure history snapshot");
    }
    
    public static void TestOutOfRunHistoryBuilder()
    {
        var history = new DamageHistoryStore();
        var fightOne = NewLedger();
        fightOne.StartRound(1);
        Apply(fightOne, 1, "alpha", 100, 20, DamageTeam.Friendly, "a");
        Apply(fightOne, 2, "beta", 70, 0, DamageTeam.Friendly, "b");
        fightOne.EndFight();
        Assert(history.Archive(fightOne.CreateSnapshot(), "Win", "one"),
            "first source fight archived for out-of-run build");
    
        var fightTwo = new DamageLedger();
        fightTwo.StartFight("session-two", true);
        fightTwo.StartRound(1);
        Apply(fightTwo, 1, "alpha", 30, 0, DamageTeam.Friendly, "a2");
        fightTwo.EndFight();
        Assert(history.Archive(fightTwo.CreateSnapshot(), "Win", "two"),
            "second source fight archived for out-of-run build");
    
        var record = OutOfRunDamageHistoryBuilder.Build(
            history.Records,
            new OutOfRunDamageHistoryBuildRequest
            {
                AdventureId = "adventure",
                ModeId = "Normal",
                ModeDisplayName = "世界推演",
                Status = OutOfRunDamageHistoryStatus.Completed,
                TeamMembers = new[]
                {
                    new OutOfRunTeamMemberSnapshot
                    {
                        InstanceId = "alpha",
                        PlayerId = "player-alpha",
                        PlayerDisplayName = "PlayerAlphaLongName",
                        RoleId = "role-alpha",
                        RoleDisplayName = "AlphaLongNameForTrim",
                        DisplayName = "PlayerAlphaLongName",
                        AvatarPngBase64 = "avatar"
                    },
                    new OutOfRunTeamMemberSnapshot
                    {
                        InstanceId = "beta",
                        PlayerId = "player-beta",
                        PlayerDisplayName = "BetaPlayer",
                        RoleId = "role-beta",
                        RoleDisplayName = "Beta",
                        DisplayName = "BetaPlayer"
                    }
                }
            });
    
        Assert(record.TotalRounds == 2
               && record.TeamTotalDamage == 220
               && Math.Abs(record.TeamDps - 110d) < 0.001d,
            "out-of-run history aggregates total damage and rounds");
        Assert(record.BestHit?.Damage == 120 && record.Mvp.InstanceId == "alpha",
            "out-of-run history records best hit and highest-DPS MVP");
        Assert(record.TeamMembers.Count == 2
               && record.TeamMembers[0].TotalDamage == 150
               && record.TeamMembers[0].PlayerDisplayName == "PlayerAlphaLongName"
               && record.TeamMembers[0].RoleDisplayName == "AlphaLongNameForTrim"
               && record.TeamMembers[0].AvatarPngBase64 == "avatar",
            "out-of-run history preserves copied member identity and avatar data");
    
        var store = new OutOfRunDamageHistoryStore();
        Assert(store.Add(record) && !store.Add(record), "out-of-run history rejects duplicate adventure id");
        var restored = new OutOfRunDamageHistoryStore();
        restored.ApplyFile(store.CreateFile());
        Assert(restored.Records.Count == 1
               && restored.Records[0].Mvp.InstanceId == "alpha"
               && restored.Records[0].TeamMembers[0].PlayerDisplayName == "PlayerAlphaLongName"
               && restored.Records[0].TeamMembers[0].RoleDisplayName == "AlphaLongNameForTrim",
            "out-of-run history store file round-trips");
    
        var rosterOnly = OutOfRunDamageHistoryBuilder.Build(
            new DamageRunAggregateSnapshot
            {
                AdventureId = "fallback",
                TotalRounds = 1,
                Combatants = new List<CombatantDamageStat>
                {
                    new()
                    {
                        InstanceId = "alpha",
                        DisplayName = "Alpha",
                        Team = DamageTeam.Friendly,
                        TotalHpDamage = 50
                    },
                    new()
                    {
                        InstanceId = "e0",
                        DisplayName = "洛奈尔",
                        Team = DamageTeam.Friendly,
                        TotalHpDamage = 999
                    }
                }
            },
            new OutOfRunDamageHistoryBuildRequest
            {
                AdventureId = "fallback",
                TeamMembers = new[]
                {
                    new OutOfRunTeamMemberSnapshot
                    {
                        InstanceId = "alpha",
                        PlayerId = "player-alpha",
                        RoleId = "role-alpha",
                        RoleDisplayName = "Alpha"
                    }
                }
            });
        Assert(rosterOnly.TeamMembers.Count == 1
               && rosterOnly.TeamMembers[0].InstanceId == "alpha"
               && rosterOnly.TeamTotalDamage == 50
               && rosterOnly.Mvp.InstanceId == "alpha",
            "settlement history consumes only the captured real-player roster");
    
        var unresolved = OutOfRunDamageHistoryBuilder.Build(
            new DamageRunAggregateSnapshot
            {
                AdventureId = "unresolved",
                TotalRounds = 1,
                Combatants = new List<CombatantDamageStat>
                {
                    new() { InstanceId = "unknown", Team = DamageTeam.Friendly, TotalHpDamage = 1 }
                }
            },
            new OutOfRunDamageHistoryBuildRequest { AdventureId = "unresolved" });
        Assert(unresolved.TeamMembers.Count == 0 && unresolved.TeamTotalDamage == 0,
            "unknown and unrostered damage sources are excluded from settlement players");
    }
    
    public static void TestDeterministicAllocation()
    {
        var split = DamageAllocation.ProportionalSplit(11, new[] { 3, 2, 1 });
        Assert(split.SequenceEqual(new[] { 5, 4, 2 }), "weighted damage split uses largest remainders");
        Assert(split.Sum() == 11, "weighted damage split preserves total");
        var reduced = DamageAllocation.ProportionalSplit(2, new[] { 3, 2, 1 });
        Assert(reduced.SequenceEqual(new[] { 1, 1, 0 }), "small damage remains proportionally distributed");
        Assert(DamageAllocation.ProportionalSplit(10, null).Length == 0, "null weights are safe");
        Assert(DamageAllocation.ProportionalSplit(10, new[] { 0, -2 }).SequenceEqual(new[] { 0, 0 }),
            "non-positive weights receive no damage");
        Assert(DamageAllocation.ProportionalSplit(int.MaxValue, new[] { int.MaxValue, int.MaxValue }).Sum(value => (long)value)
               == int.MaxValue,
            "large values split without integer overflow");
    }
    
    public static void TestHotkeyNames()
    {
        Assert(DamageMeterHotkeyNames.TryNormalize(" f8 ", out var f8) && f8 == "F8",
            "function key normalized");
        Assert(DamageMeterHotkeyNames.TryNormalize("BackQuote", out var backquote) && backquote == "Backquote",
            "legacy BackQuote alias normalized");
        Assert(DamageMeterHotkeyNames.TryNormalize("Alpha7", out var digit) && digit == "Digit7",
            "legacy alpha digit normalized");
        Assert(DamageMeterHotkeyNames.TryNormalize("Keypad3", out var numpad) && numpad == "Numpad3",
            "legacy keypad digit normalized");
        Assert(DamageMeterHotkeyNames.TryNormalize("LeftControl", out var control) && control == "LeftCtrl",
            "legacy control alias normalized");
        Assert(!DamageMeterHotkeyNames.TryNormalize("DefinitelyNotAKey", out var fallback) && fallback == "F8",
            "invalid key reports deterministic fallback");
    }
    
    public static void TestInputFaultGate()
    {
        var gate = new DamageMeterInputFaultGate();
        var pollCount = 0;
        var errorCount = 0;
        Assert(!gate.TryPoll(() =>
        {
            pollCount++;
            throw new InvalidOperationException("input backend unavailable");
        }, _ => errorCount++), "input fault is contained");
        Assert(gate.IsFaulted && pollCount == 1 && errorCount == 1, "first input fault trips gate once");
        Assert(!gate.TryPoll(() =>
        {
            pollCount++;
            return true;
        }, _ => errorCount++), "faulted input is not polled every frame");
        Assert(pollCount == 1 && errorCount == 1, "faulted input cannot flood logs");
        gate.Reset();
        Assert(gate.TryPoll(() => true, _ => errorCount++), "configuration change resets input gate");
    }
}
