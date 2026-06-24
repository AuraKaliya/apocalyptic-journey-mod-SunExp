using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Input;

var assertions = 0;

TestRoundAndDpt();
TestShieldViewRecalculation();
TestSnapshotRecovery();
TestSequenceAndSessionGuards();
TestLongRunningTotals();
TestFilteringAndGrandTotal();
TestDetailLimit();
TestAdventureHistory();
TestDeterministicAllocation();
TestHotkeyNames();
TestInputFaultGate();
TestDamageMeterSettingsNormalization();

Console.WriteLine($"AuraToolsExp damage meter tests passed: {assertions} assertions.");
return;

void TestRoundAndDpt()
{
    var ledger = NewLedger();
    ledger.StartRound(1);
    Apply(ledger, 1, "p1", 70, 30, DamageTeam.Friendly, "card_a");
    Assert(ledger.CurrentRoundIndex == 1, "round one starts");
    var p1 = ledger.Combatants.Single();
    Assert(p1.DisplayCurrentRound(true) == 100, "round damage includes shield");

    ledger.StartRound(2);
    Assert(ledger.CompletedRoundCount == 1, "previous round closes once");
    Assert(p1.DisplayCurrentRound(true) == 0, "new round resets current counters");
    Assert(p1.Rounds.Count == 1 && p1.Rounds[0].HpDamage == 70, "round history preserved");

    Apply(ledger, 2, "p1", 50, 0, DamageTeam.Friendly, "card_b");
    Assert(Math.Abs(p1.AveragePerCompletedRound(true, ledger.AveragingRoundCount) - 75d) < 0.001,
        "live average DPT includes the active round");
    ledger.EndFight();
    Assert(ledger.CompletedRoundCount == 2, "fight end closes final active round");
    Assert(Math.Abs(p1.AveragePerCompletedRound(true, ledger.CompletedRoundCount) - 75d) < 0.001,
        "final DPT includes final round");
}

void TestShieldViewRecalculation()
{
    var ledger = NewLedger();
    ledger.StartRound(1);
    Apply(ledger, 1, "p1", 40, 60, DamageTeam.Friendly, "card");
    var stat = ledger.Combatants.Single();
    Assert(stat.DisplayTotal(true) == 100, "shield-inclusive view");
    Assert(stat.DisplayTotal(false) == 40, "shield-exclusive view recalculates from raw ledger");
}

void TestSnapshotRecovery()
{
    var source = NewLedger();
    source.StartRound(1);
    Apply(source, 1, "p1", 20, 5, DamageTeam.Friendly, "card");
    source.StartRound(2);
    Apply(source, 2, "p2", 30, 0, DamageTeam.Unknown, "buff");

    var restored = new DamageLedger();
    Assert(restored.ApplySnapshot(source.CreateSnapshot()), "snapshot accepted");
    Assert(restored.SessionId == source.SessionId
           && restored.ServerSequence == 2
           && restored.CurrentRoundIndex == 2,
        "snapshot restores protocol state");
    Assert(restored.Combatants.Count == 2, "snapshot restores all combatants");
    var stale = source.CreateSnapshot();
    Apply(source, 3, "p1", 1, 0, DamageTeam.Friendly, "card");
    Assert(!source.ApplySnapshot(stale) && source.ServerSequence == 3,
        "same-session stale snapshot cannot roll the ledger back");
}

void TestSequenceAndSessionGuards()
{
    var ledger = NewLedger();
    ledger.StartRound(1);
    var first = Event(ledger, 1, "p1", 10, 0, DamageTeam.Friendly, "card");
    Assert(ledger.Apply(first), "first event accepted");
    Assert(!ledger.Apply(first.Copy()), "duplicate server sequence rejected");

    var gap = Event(ledger, 3, "p1", 10, 0, DamageTeam.Friendly, "card");
    Assert(!ledger.Apply(gap), "sequence gap rejected for snapshot recovery");

    var wrongSession = Event(ledger, 2, "p1", 10, 0, DamageTeam.Friendly, "card");
    wrongSession.SessionId = "old-session";
    Assert(!ledger.Apply(wrongSession), "old session rejected");

    var zero = Event(ledger, 2, "p1", 0, 0, DamageTeam.Friendly, "card");
    Assert(!ledger.Apply(zero) && ledger.ServerSequence == 1, "zero damage does not consume sequence");

    var restored = new DamageLedger();
    Assert(restored.ApplySnapshot(new DamageMeterSnapshot
    {
        SessionId = "session",
        InFight = true,
        SharedEnabled = true,
        CurrentRoundIndex = 1,
        ServerSequence = 5000
    }), "high-sequence snapshot accepted");
    Assert(!restored.Apply(Event(restored, 1, "p1", 10, 0, DamageTeam.Friendly, "card")),
        "replayed old sequence rejected after snapshot");
}

void TestLongRunningTotals()
{
    var ledger = NewLedger();
    ledger.StartRound(1);
    for (var sequence = 1; sequence <= 30; sequence++)
    {
        Apply(ledger, sequence, "p1", DamageMeterProtocol.MaxDamagePerEvent, 0, DamageTeam.Friendly, "card");
    }

    Assert(ledger.Combatants.Single().TotalHpDamage == 3_000_000_000L,
        "long fights cannot overflow aggregate damage");
}

void TestFilteringAndGrandTotal()
{
    var ledger = NewLedger();
    ledger.StartRound(1);
    Apply(ledger, 1, "friendly", 100, 0, DamageTeam.Friendly, "a");
    Apply(ledger, 2, "enemy", 80, 0, DamageTeam.Enemy, "b");
    Apply(ledger, 3, "unknown", 60, 0, DamageTeam.Unknown, "c");
    Assert(ledger.VisibleRows(false, true, true, 2).Count == 2, "row limit only affects presentation");
    Assert(ledger.DisplayGrandTotal(true, false, true) == 240, "grand total ignores row limit");
    Assert(ledger.DisplayGrandTotal(true, true, false) == 100, "friendly total excludes unknown when configured");
    Assert(ledger.DisplayGrandTotal(true, true, true) == 160, "friendly total can include unknown");
}

void TestDamageMeterSettingsNormalization()
{
    var settings = new DamageMeterSettings
    {
        FriendlyOnly = true,
        ShowPanelByDefault = false,
        IncludeUnknownTeam = true,
        CountShieldLoss = false,
        MaxRows = 12,
        ShowAverageDpt = false,
        ShowTeamShare = false
    };

    settings.Normalize();
    Assert(settings.ShowPanelByDefault, "DPS panel is always enabled by default");
    Assert(!settings.IncludeUnknownTeam, "friendly-only DPS excludes unknown-team damage");
    Assert(settings.CountShieldLoss, "shield damage display is always enabled");
    Assert(settings.MaxRows == 6, "DPS row count uses the fixed default");
    Assert(settings.ShowAverageDpt, "average DPT display is always enabled");
    Assert(settings.ShowTeamShare, "team damage share display is always enabled");

    settings.FriendlyOnly = false;
    settings.Normalize();
    Assert(settings.IncludeUnknownTeam, "unfiltered DPS includes unknown-team damage");
}

void TestDetailLimit()
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

void TestAdventureHistory()
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

void TestDeterministicAllocation()
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

void TestHotkeyNames()
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

void TestInputFaultGate()
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

DamageLedger NewLedger()
{
    var ledger = new DamageLedger();
    ledger.StartFight("session", true);
    return ledger;
}

void Apply(
    DamageLedger ledger,
    long sequence,
    string source,
    int hp,
    int shield,
    DamageTeam team,
    string detail)
{
    Assert(ledger.Apply(Event(ledger, sequence, source, hp, shield, team, detail)),
        "event " + sequence + " accepted");
}

DamageEvent Event(
    DamageLedger ledger,
    long sequence,
    string source,
    int hp,
    int shield,
    DamageTeam team,
    string detail)
{
    return new DamageEvent
    {
        SessionId = ledger.SessionId,
        ReporterPlayerId = "reporter",
        ReporterSequence = sequence,
        ServerSequence = sequence,
        RoundIndex = Math.Max(1, ledger.CurrentRoundIndex),
        SourceInstanceId = source,
        SourceDisplayName = source,
        SourceTeam = team,
        TargetInstanceId = "target",
        SourceDataId = detail,
        DetailLabel = detail,
        DamageType = "Normal",
        HpDamage = hp,
        ShieldDamage = shield,
        FinalDamage = hp + shield,
        AttributionConfidence = DamageAttributionConfidence.Exact
    };
}

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }

    assertions++;
}
