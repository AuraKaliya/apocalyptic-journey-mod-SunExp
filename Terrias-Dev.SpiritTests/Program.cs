using System;
using System.Collections.Generic;
using Terrias.Dll.Mechanics;

var assertions = 0;

Assert(SpiritCaptureRollService.ChanceBasisPoints(100, 100) == 1000,
    "full-health capture chance is 10 percent");
Assert(SpiritCaptureRollService.ChanceBasisPoints(50, 100) == 5000,
    "half-health capture chance is 50 percent");
Assert(SpiritCaptureRollService.ChanceBasisPoints(0, 100) == 9000,
    "zero-health capture chance is capped at 90 percent");
Assert(SpiritCaptureRollService.ChanceBasisPoints(-10, 0) == 9000,
    "capture chance clamps invalid HP inputs");
var firstRoll = SpiritCaptureRollService.RollBasisPoints("stable-seed");
Assert(firstRoll == SpiritCaptureRollService.RollBasisPoints("stable-seed")
       && firstRoll is >= 0 and < 10000,
    "capture roll is deterministic and bounded");
var succeeds = SpiritCaptureRollService.Succeeds(25, 100, "stable-seed", out var chance, out var roll);
Assert(chance == 7000 && succeeds == (roll < chance),
    "capture result compares the deterministic roll with the calculated chance");

var stats = new CompanionStats(0, 3, -1, -1);
Assert(stats.MaxHp == 1 && stats.MaxMagic == 3 && stats.Attack == 0 && stats.Armor == 0,
    "companion stats clamp constructor inputs");
Assert(!stats.TrySpendMagic(4) && stats.CurrentMagic == 3,
    "companion magic rejects unaffordable costs without mutation");
Assert(stats.TrySpendMagic(2) && stats.CurrentMagic == 1,
    "companion magic spends an affordable cost");
stats.RecoverMagic(9);
Assert(stats.CurrentMagic == 3, "companion magic recovery respects the maximum");

var state = new CompanionBattleState("spirit-1", "role-1", "owner-1", 2, stats, "player-1");
Assert(state.IsReady("intent-a"), "new companion intents are ready");
state.StartCooldown("intent-a", 2);
Assert(state.ReadyOnTurn("intent-a") == 3 && state.Cooldown("intent-a") == 3,
    "companion cooldown stores the authoritative ready turn");
state.AdvanceTurn();
Assert(state.TurnIndex == 1 && state.Cooldown("intent-a") == 2 && state.Revision == 1,
    "companion turn advancement updates cooldown and revision");
state.ApplyRemoteProgress(5, 7);
state.ApplyRemoteProgress(3, 2);
Assert(state.TurnIndex == 5 && state.Revision == 7,
    "remote companion progress is monotonic");

var plan = new CompanionIntentPlan
{
    PlanId = "plan-1",
    OrderedTargetIds = new List<string> { "enemy-a" },
    ResolvedEffects = new List<CompanionResolvedEffect>
    {
        new() { HandlerId = "damage.single", TargetIds = new List<string> { "enemy-a" }, Value = 9 }
    }
};
var snapshot = plan.Snapshot();
plan.OrderedTargetIds[0] = "enemy-b";
plan.ResolvedEffects[0].TargetIds[0] = "enemy-b";
Assert(snapshot.OrderedTargetIds[0] == "enemy-a"
       && snapshot.ResolvedEffects[0].TargetIds[0] == "enemy-a",
    "companion plans create deep immutable snapshots");

var legacy = new CompanionIntentDefinition
{
    Id = "legacy",
    HandlerId = "damage.single",
    HitCount = 2,
    FlatValue = 4
};
var legacyEffects = CompanionIntentEffects.Expand(legacy);
Assert(legacyEffects.Count == 1
       && legacyEffects[0].HandlerId == "damage.single"
       && legacyEffects[0].HitCount == 2,
    "legacy companion intents expand into one effect");
legacy.Effects.Add(new CompanionIntentEffectSpec { HandlerId = "buff.apply", BuffId = "buff-a", BuffStacks = 2 });
Assert(ReferenceEquals(CompanionIntentEffects.Expand(legacy), legacy.Effects),
    "schema-three companion intents preserve their composite effect list");

var identity = CompanionOwnershipService.Create("spirit-2", "player-2", "owner-2", "role-2", 3);
Assert(identity.StatusId == "spirit-2"
       && identity.OwnerPlayerId == "player-2"
       && identity.Faction == "Friendly"
       && identity.EntityKind == "Companion"
       && identity.SlotIndex == 3,
    "companion identity creation preserves owner and slot scope");
var epoch = CompanionAuthorityService.BattleEpoch;
CompanionAuthorityService.BeginBattleEpoch();
CompanionAuthorityService.InvalidateBattleEpoch();
Assert(CompanionAuthorityService.BattleEpoch >= epoch + 2,
    "companion lifecycle advances the authoritative battle epoch");
Assert(CompanionAuthorityService.ProjectionProtocolVersion > 0,
    "companion protocol exposes a positive compatibility version");

Console.WriteLine($"Terrias spirit runtime tests passed: {assertions} assertions.");

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    assertions++;
}
