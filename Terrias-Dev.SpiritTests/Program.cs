using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
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
var returnedBattleState = new SpiritCardBattleState
{
    TurnIndex = 4,
    ReadyOnTurn = new Dictionary<string, int> { ["intent-a"] = 7 },
    MaxHp = 35,
    CurrentHp = 19,
    CurrentDefend = 6,
    CurrentMagic = 2,
    PassiveState = new Dictionary<string, int> { ["passive-a"] = 3 },
    VisibleStatuses = new List<SpiritVisibleStatusSnapshot>
    {
        new() { Kind = "Buff", Id = "buff-a", Stacks = 2 }
    }
};
var spiritSummonRequest = new Terrias.Dll.Network.RpcSpiritSummonRequest(
    Captured("request-enemy", "request-spirit"),
    "owner-request",
    "request-token",
    2,
    returnedBattleState);
Assert(spiritSummonRequest.BattleState.TurnIndex == 4
       && spiritSummonRequest.BattleState.ReadyOnTurn["intent-a"] == 7
       && spiritSummonRequest.BattleState.MaxHp == 35
       && spiritSummonRequest.BattleState.CurrentHp == 19
       && spiritSummonRequest.BattleState.CurrentDefend == 6
       && spiritSummonRequest.BattleState.CurrentMagic == 2
       && spiritSummonRequest.BattleState.PassiveState["passive-a"] == 3
       && spiritSummonRequest.BattleState.VisibleStatuses.Single().Stacks == 2,
    "remote Spirit summon requests preserve the complete withdrawn battle state");

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
       && identity.SemanticOwnerPlayerId == "player-2"
       && identity.SemanticOwnerStatusId == "owner-2"
       && identity.ExecutionRoutePlayerId == "player-2"
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
Assert(CompanionAuthorityService.ProjectionProtocolVersion == 21
       && ProjectionRoleDeckService.CardModelVersion == "projection-role-deck-v3"
       && SpiritCollectionService.CurrentVersion == SpiritSystemContract.CollectionVersion
       && SpiritSystemContract.CollectionVersion == 9
       && SpiritSystemContract.InitialRosterGrantVersion == 1
       && SpiritSystemContract.InitialRosterProfileCount == 58
       && SpiritSystemContract.InitialRosterConfigurationKey == "GrantAllSpiritsOnFirstLoad"
       && SpiritSystemContract.GrowthRegistrySchemaVersion == 3
       && SpiritSystemContract.TrainingRegistrySchemaVersion == 2,
    "the current Spirit save, registry, and Partner protocol contract stays synchronized");

var normalizedRemotePayload = RemoteTargetEventApi.ComposePayload(
    "Terrias_Card_Spark",
    new Dictionary<string, string> { ["Name"] = "Spark", ["Id"] = "stale-id" },
    new Dictionary<string, string> { ["Value"] = "3" });
Assert(normalizedRemotePayload["Id"] == "Terrias_Card_Spark"
       && normalizedRemotePayload["Name"] == "Spark"
       && normalizedRemotePayload["Value"] == "3",
    "remote target payloads always retain their authoritative Terrias Id and merged card fields");
Assert(SpiritStatusBarText.FormatVerticalDigits(120) == "1\n2\n0",
    "vertical spirit status counters keep one upright digit per centered line");
Assert(ExplicitStatusEffectApi.ResolveShieldAmount(8, 1f) == 8
       && ExplicitStatusEffectApi.ResolveShieldAmount(8, 1.5f) == 12
       && ExplicitStatusEffectApi.ResolveShieldAmount(8, float.NaN) == 0,
    "explicit companion shield commits preserve native multipliers and reject invalid values");

var stableProfileKey = SpiritProfileBindingPolicy.ResolveStableProfileKey("network-player", "runtime-player");
var runtimeProfileKey = SpiritProfileBindingPolicy.ResolveStableProfileKey("", "runtime-player");
Assert(stableProfileKey == "network-player"
       && runtimeProfileKey == "runtime-player"
       && SpiritProfileBindingPolicy.ResolveStableProfileKey("", "") == "",
    "spirit persistence waits for a stable network or runtime player identity");
var legacyProfileKeys = SpiritProfileBindingPolicy.LegacyProfileKeys(@"C:\Save\SaveData.json");
Assert(legacyProfileKeys.Contains("SaveData")
       && legacyProfileKeys.Contains("local")
       && !SpiritProfileBindingPolicy.HasRecoverableContent(new SpiritCollectionDocument())
       && SpiritProfileBindingPolicy.HasRecoverableContent(new SpiritCollectionDocument
       {
           Instances = new List<SpiritInstance> { new() }
       })
       && SpiritProfileBindingPolicy.HasRecoverableContent(new SpiritCollectionDocument
       {
           InitialRosterGrantVersion = SpiritSystemContract.InitialRosterGrantVersion
       })
       && !SpiritProfileBindingPolicy.ShouldRecoverLegacy(true, new SpiritCollectionDocument
       {
           ProcessedCaptureTokens = new Dictionary<string, string> { ["capture"] = "uid" }
       }),
    "legacy profile recovery ignores empty fallbacks and never replaces an existing stable profile");

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var terriasModConfig = new Witch.Mod.ModConfig { DirectoryName = Path.Combine(repositoryRoot, "Terrias") };
Assert(Witch.Mod.ModConfigurationFile.TryRead(terriasModConfig.DirectoryName, out var ownConfiguration, out var configurationError)
       && string.IsNullOrWhiteSpace(configurationError)
       && ownConfiguration.ExtensionData.TryGetValue(SpiritSystemContract.InitialRosterConfigurationKey, out var initialRosterSetting)
       && initialRosterSetting is bool initialRosterEnabled
       && initialRosterEnabled,
    "the native mod configuration parser loads GrantAllSpiritsOnFirstLoad as an enabled JSON boolean");
TerriasTextCatalog.Load(terriasModConfig);
SpiritGrowthRegistry.Load(terriasModConfig);
SpiritTrainingRegistry.Load(terriasModConfig);
SpiritIntentRegistry.Load(terriasModConfig);
var registeredSpiritProfiles = SpiritGrowthRegistry.RegisteredProfiles();
Assert(registeredSpiritProfiles.Count == SpiritSystemContract.InitialRosterProfileCount
       && registeredSpiritProfiles.Select(profile => profile.ProfileId).Distinct(StringComparer.Ordinal).Count()
       == SpiritSystemContract.InitialRosterProfileCount,
    "the initial roster source exposes exactly fifty-eight distinct registered Spirit profiles");
var baseGameLookup = EnemyCatalogApi.ConfiguredEnemyLookupCandidates("base-game", "10001");
Assert(baseGameLookup.Contains("enemy_10001", StringComparer.OrdinalIgnoreCase),
    "initial Spirit lookup expands normalized base-game ids to native enemy ids");
Assert(registeredSpiritProfiles.All(profile =>
    {
        var match = profile.Match ?? new SpiritSpeciesGrowthMatch();
        var candidates = EnemyCatalogApi.ConfiguredEnemyLookupCandidates(match.SourceModId, match.EnemyId);
        var expected = string.Equals(match.SourceModId, "base-game", StringComparison.OrdinalIgnoreCase)
            ? "enemy_" + match.EnemyId.Trim().Replace("enemy_", "")
            : TerriasContentIdCompatibility.CurrentMainPrefix + match.EnemyId.Trim();
        return candidates.Contains(expected, StringComparer.OrdinalIgnoreCase);
    }),
    "all fifty-eight initial Spirit profiles expose their authoritative native or owner-qualified enemy id candidate");
var initialAttemptLedger = new SpiritInitialRosterAttemptLedger();
Assert(initialAttemptLedger.MarkPending("profile", "catalog-not-ready")
       && initialAttemptLedger.PendingProfileKeys().SequenceEqual(new[] { "profile" })
       && initialAttemptLedger.TryBeginReadyAttempt("profile", 3)
       && !initialAttemptLedger.TryBeginReadyAttempt("profile", 3),
    "initial Spirit pending work begins exactly once for one native catalog generation");
initialAttemptLedger.MarkPending("profile", "late-registration");
Assert(initialAttemptLedger.TryBeginReadyAttempt("profile", 4),
    "a newer native catalog generation deterministically drains a pending initial Spirit obligation");
initialAttemptLedger.MarkCompleted("profile", "committed");
Assert(initialAttemptLedger.PendingProfileKeys().Count == 0
       && initialAttemptLedger.IsTerminal("profile")
       && !initialAttemptLedger.TryBeginReadyAttempt("profile", 5),
    "a committed initial Spirit grant leaves no retry path behind");
var pyroSpecies = SpiritGrowthRegistry.ResolveIdentity(new CapturedEnemySnapshot
{
    SourceModId = "base-game",
    EnemyId = "10004",
    VariantId = "10004"
});
var anemoSpecies = SpiritGrowthRegistry.ResolveIdentity(new CapturedEnemySnapshot
{
    SourceModId = "base-game",
    EnemyId = "10002",
    VariantId = "10002"
});
Assert(pyroSpecies.Profile.CaptureElement == "pyro"
       && anemoSpecies.Profile.CaptureElement == "anemo"
       && SpiritElementService.IconPath("pyro").EndsWith("元素-火", StringComparison.Ordinal)
       && SpiritElementService.IconPath("anemo").EndsWith("元素-风", StringComparison.Ordinal),
    "the growth registry owns capture defaults for all seven Spirit elements and presentation resolves their icons");
var elementalPresentation = CompanionIntentPresentationSnapshot.Resolve(
    new CompanionResolvedEffect { HandlerId = "damage.multi", Value = 12, RepeatCount = 2 },
    1,
    "electro");
Assert(elementalPresentation.DisplayText == "雷 · 12×2",
    "Spirit intent previews identify the per-segment element and hit count");
var commonIntentIds = SpiritTrainingRegistry.CommonIntentIds("Common.Basic")
    .Concat(SpiritTrainingRegistry.CommonIntentIds("Common.Tactical"))
    .Concat(SpiritTrainingRegistry.CommonIntentIds("Common.Advanced"))
    .Distinct(StringComparer.Ordinal)
    .ToArray();
var commonPassiveIds = SpiritTrainingRegistry.CommonPassiveIds("Common.Core")
    .Concat(SpiritTrainingRegistry.CommonPassiveIds("Common.Advanced"))
    .Distinct(StringComparer.Ordinal)
    .ToArray();
Assert(commonIntentIds.Length == 15
       && commonPassiveIds.Length == 12
       && commonIntentIds.All(id => SpiritIntentRegistry.Find(id) != null)
       && SpiritTrainingRegistry.RegistryHash != "00000000",
    "training registries load fifteen common intents, twelve common passives, and merge executable intent definitions");
var speciesPassives = SpiritTrainingRegistry.Passives("Species");
Assert(speciesPassives.Count == 51
       && speciesPassives.All(passive => SpiritPassiveMechanicRegistry.Validate(passive, out _))
       && speciesPassives.All(passive => passive.EffectKind != "type-resonance")
       && speciesPassives.Select(SpiritPassiveMechanicRegistry.Signature).Distinct(StringComparer.Ordinal).Count() == 51,
    "all fifty-one species passives use supported non-placeholder mechanics with independent signatures");
var external = new SpiritInstance
{
    SpiritUid = "external-spirit",
    SpeciesId = "external.species",
    ProfileId = "external.profile",
    Snapshot = Captured("external-enemy", "external-spirit"),
    Level = 1,
    Aptitude = 60
};
SpiritTrainingService.InitializeCaptured(external);
Assert(external.InherentAbilityPlanVersion == SpiritSystemContract.InherentAbilityPlanVersion
       && external.ResolvedInherentIntentIds.Count == 3
       && external.EquippedIntentIds.Count == 3
       && external.EquippedPassiveId == SpiritSystemContract.CompatibilityPassiveId
       && external.EquippedIntentIds.All(id => SpiritIntentRegistry.Find(id) != null),
    "external Spirits freeze an executable attack, defense, origin branch, and registered compatibility passive at level one");
var generatedPlans = Enumerable.Range(0, 128).Select(index =>
{
    var candidate = new SpiritInstance
    {
        SpiritUid = "plan-" + index,
        SpeciesId = "external.species",
        ProfileId = "external.profile",
        Snapshot = Captured("external-enemy", "plan-" + index),
        Level = 50,
        Aptitude = 60
    };
    SpiritTrainingService.InitializeCaptured(candidate);
    return candidate;
}).ToArray();
Assert(generatedPlans.All(candidate => candidate.UnlockPlan
           .Where(node => node.AbilityKind == "Intent" && node.Stage is 1 or 2 or 4)
           .Select(node => SpiritIntentRegistry.Find(node.AbilityId)?.Type ?? "")
           .Distinct(StringComparer.Ordinal)
           .Count() >= 2)
       && generatedPlans.Where(candidate => candidate.LearnedPassiveIds.Contains(
               "spirit.passive.common.advanced.swift-calculation", StringComparer.Ordinal))
           .All(candidate => candidate.LearnedIntentIds.Any(id => SpiritIntentRegistry.Find(id)?.SpeedScale > 0f)),
    "generated growth plans enforce common-intent type diversity and passive trigger reachability");
var externalState = new CompanionBattleState(
    "external-status", external.ProfileId, "owner", -1, new CompanionStats(20, 3, 4, 3, 100), "player", entityKind: "SpiritAttachment");
externalState.ConfigureLoadout(external.EquippedIntentIds, external.EquippedPassiveId, external.LoadoutRevision, external.LoadoutHash);
Assert(CompanionIntentResolver.IntentsFor(externalState, CompanionIntentTendency.Attack).Count > 0
       && CompanionIntentResolver.IntentsFor(externalState, CompanionIntentTendency.Defense).Count > 0,
    "external compatibility loadouts close both attack and defense planning tendencies");
var emptyFallbackState = new CompanionBattleState(
    "empty-fallback", "external.profile", "owner", -1, new CompanionStats(20, 3, 4, 3, 100), "player", entityKind: "SpiritAttachment");
Assert(CompanionIntentResolver.IntentsFor(emptyFallbackState, CompanionIntentTendency.Attack).Count > 0
       && CompanionIntentResolver.IntentsFor(emptyFallbackState, CompanionIntentTendency.Defense).Count > 0,
    "an invalid empty loadout still receives the bounded emergency fallback instead of waiting forever");
Assert(SpiritVirtualGridPolicy.RequiredCellCount(260f, 166f, 8f, 3) == 12
       && SpiritVirtualGridPolicy.ContentHeight(1000, 3, 166f, 8f, 4f, 4f) == 58116f
       && SpiritVirtualGridPolicy.FirstVisibleRow(0f, 4f, 166f, 8f) == 0,
    "the warehouse virtual grid keeps a constant visible pool independent of collection size");
externalState.ApplyVisibleStatuses(new[]
{
    new SpiritVisibleStatusSnapshot { Kind = "Buff", Id = "buff_weak", Stacks = 2 },
    new SpiritVisibleStatusSnapshot { Kind = "Mechanic", Id = external.EquippedPassiveId, Value = 1, Maximum = 3 }
});
var visibleCopy = externalState.VisibleStatusSnapshot();
Assert(visibleCopy.Count == 2 && visibleCopy[0].Stacks == 2 && visibleCopy[1].Maximum == 3,
    "Spirit visible status snapshots retain bounded Buff and mechanic presentation state");
var equippedOnlyState = new CompanionBattleState(
    "spirit-equipped", "base-game.10040", "owner", 0, new CompanionStats(10, 3, 3, 2, 100), "player", entityKind: "SpiritAttachment");
equippedOnlyState.ConfigureLoadout(commonIntentIds.Take(3), commonPassiveIds[0], 1, "test");
var executableLoadout = CompanionIntentResolver.IntentsFor(equippedOnlyState, CompanionIntentTendency.Attack)
    .Concat(CompanionIntentResolver.IntentsFor(equippedOnlyState, CompanionIntentTendency.Defense))
    .Select(intent => intent.Id)
    .Distinct(StringComparer.Ordinal)
    .ToArray();
var emptyLoadoutState = new CompanionBattleState(
    "spirit-empty", "base-game.10040", "owner", 0, new CompanionStats(10, 3, 3, 2, 100), "player", entityKind: "SpiritAttachment");
var boundedEmptyFallback = CompanionIntentResolver.IntentsFor(emptyLoadoutState, CompanionIntentTendency.Attack)
    .Concat(CompanionIntentResolver.IntentsFor(emptyLoadoutState, CompanionIntentTendency.Defense))
    .Select(intent => intent.Id)
    .Distinct(StringComparer.Ordinal)
    .ToArray();
Assert(executableLoadout.Length == 3
       && executableLoadout.All(id => equippedOnlyState.EquippedIntentIds.Contains(id, StringComparer.Ordinal))
       && boundedEmptyFallback.Length == SpiritTrainingService.EmergencyFallbackIntentIds.Count
       && boundedEmptyFallback.All(id => SpiritTrainingService.EmergencyFallbackIntentIds.Contains(id, StringComparer.Ordinal)),
    "spirit battle selection uses the frozen loadout and only the bounded compatibility pair when that loadout is invalid");
var compactLoadout = new SpiritInstance
{
    SpiritUid = "compact-loadout",
    LearnedIntentIds = commonIntentIds.Take(3).ToList(),
    EquippedIntentIds = new List<string> { commonIntentIds[0] },
    LoadoutRevision = 1
};
Assert(SpiritTrainingService.EquipIntent(compactLoadout, 1, commonIntentIds[1])
       && SpiritTrainingService.EquipIntent(compactLoadout, 2, commonIntentIds[2])
       && compactLoadout.EquippedIntentIds.SequenceEqual(commonIntentIds.Take(3))
       && !SpiritTrainingService.EquipIntent(compactLoadout, 3, commonIntentIds[0]),
    "empty intent slots append contiguously without persisting blank gaps or exceeding the three-slot capacity");
var demonKingPhaseOne = SpiritGrowthRegistry.ResolveIdentity(new CapturedEnemySnapshot
{
    SourceModId = "BaseGame",
    EnemyId = "enemy_10048",
    VariantId = "enemy_10048"
});
var demonKingPhaseTwo = SpiritGrowthRegistry.ResolveIdentity(new CapturedEnemySnapshot
{
    SourceModId = "BaseGame",
    EnemyId = "enemy_10051",
    VariantId = "enemy_10051"
});
Assert(demonKingPhaseOne.ProfileId == "base-game.10048"
       && demonKingPhaseTwo.ProfileId == "base-game.10051"
       && demonKingPhaseOne.SpeciesId == "base-game.demon-king"
       && demonKingPhaseOne.SpeciesId == demonKingPhaseTwo.SpeciesId
       && SpiritGrowthRegistry.FormLabel(demonKingPhaseOne.Profile) == "第一形态"
       && demonKingPhaseOne.Profile.Tier == nameof(SpiritSpeciesTier.FinalBoss)
       && !demonKingPhaseOne.UsedFallback
       && SpiritGrowthRegistry.RegistryHash != "00000000",
    "schema-two registry resolves runtime aliases into fixed multi-form identities: "
    + demonKingPhaseOne.ProfileId + "/" + demonKingPhaseOne.SpeciesId + "/" + demonKingPhaseOne.UsedFallback
    + ", " + demonKingPhaseTwo.ProfileId + "/" + demonKingPhaseTwo.SpeciesId + "/" + demonKingPhaseTwo.UsedFallback
    + ", hash=" + SpiritGrowthRegistry.RegistryHash + ", diagnostic=" + SpiritGrowthRegistry.LastLoadDiagnostic);

Assert(SpiritGrowthService.ExperienceToNextLevel(1) == 20
       && SpiritGrowthService.TotalExperienceToLevel(50) == 4904,
    "spirit level curve matches the locked level-one and level-fifty totals");
var aptitude = SpiritGrowthService.RollAptitude("capture-token");
Assert(aptitude == SpiritGrowthService.RollAptitude("capture-token") && aptitude is >= 0 and <= 100,
    "spirit aptitude is deterministic and bounded");
var aptitudeSamples = new List<int>();
for (var index = 0; index < 1000; index++) aptitudeSamples.Add(SpiritGrowthService.RollAptitude("sample-" + index));
Assert(aptitudeSamples.Average() is > 56d and < 64d
       && aptitudeSamples.All(value => value is >= 0 and <= 100),
    "truncated aptitude samples remain centered without invalid tails");

var growthProfile = new SpiritSpeciesGrowthProfile
{
    BaseOrigins = new SpiritOriginVector { Magic = 10, Spirit = 10, Luck = 10, Perception = 10 },
    GrowthOrigins = new SpiritOriginVector { Magic = 20, Spirit = 20, Luck = 20, Perception = 20 }
};
var origins = SpiritGrowthService.OriginsAt(growthProfile, 50, 60);
Assert(origins.Magic == 31 && origins.Spirit == 31 && origins.Luck == 31 && origins.Perception == 31,
    "spirit origins apply the smooth aptitude multiplier at max level");
var growthStats = SpiritGrowthService.BattleStats(origins);
Assert(growthStats.MaxHp == 119 && growthStats.Attack == 40 && growthStats.Armor == 27,
    "spirit origins convert into the locked HP, attack, and armor formulas");
Assert(SpiritAscensionService.StarRankFor(0) == 0
       && SpiritAscensionService.StarRankFor(1) == 1
       && SpiritAscensionService.StarRankFor(2) == 2
       && SpiritAscensionService.StarRankFor(4) == 3
       && SpiritAscensionService.StarRankFor(8) == 4
       && SpiritAscensionService.StarRankFor(16) == 5
       && Enumerable.Range(0, 6).Select(SpiritAscensionService.PointBudgetForStar)
           .SequenceEqual(new[] { 0, 2, 6, 12, 20, 30 }),
    "guiyuan thresholds and cumulative origin budgets match the locked five-stage progression");
var normalizedAllocation = SpiritAscensionService.NormalizeAllocations(
    new SpiritOriginVector { Magic = 12, Perception = 10, Spirit = 10, Luck = 10 }, 4);
Assert(normalizedAllocation.Magic == 10
       && normalizedAllocation.Perception == 2
       && normalizedAllocation.Spirit == 0
       && normalizedAllocation.Luck == 0
       && normalizedAllocation.Total == 12,
    "guiyuan allocation normalization enforces ten points per axis and the cumulative star budget");
var fiveStarStats = SpiritAscensionService.ApplyStarBonus(new CompanionStats(119, 12, 40, 27, 84), 5);
Assert(fiveStarStats.MaxHp == 238
       && fiveStarStats.Attack == 80
       && fiveStarStats.Armor == 54
       && fiveStarStats.MaxMagic == 12
       && fiveStarStats.Speed == 84,
    "five stars double HP, attack, and armor without modifying magic energy or speed");
var fallbackProfile = SpiritGrowthRegistry.Resolve(new CapturedEnemySnapshot
{
    EnemyId = "extreme-species",
    VariantId = "extreme-species",
    BaseAttack = 10000,
    BaseHp = 1,
    BaseArmor = 0,
    Rarity = 1
});
Assert(fallbackProfile.BaseOrigins.Total == 28
       && new[] { fallbackProfile.BaseOrigins.Magic, fallbackProfile.BaseOrigins.Spirit, fallbackProfile.BaseOrigins.Luck, fallbackProfile.BaseOrigins.Perception }
           .All(value => value is >= 3 and <= 12),
    "fallback species budgets keep every origin within the ten-to-forty-five-percent envelope");
var fallbackIdentityA = SpiritGrowthRegistry.ResolveIdentity(Captured("identity-species", "identity-a"));
var fallbackIdentityB = SpiritGrowthRegistry.ResolveIdentity(Captured("identity-species", "identity-b"));
Assert(fallbackIdentityA.UsedFallback
       && fallbackIdentityA.SpeciesId == fallbackIdentityB.SpeciesId
       && fallbackIdentityA.ProfileId == fallbackIdentityB.ProfileId,
    "unregistered species receive deterministic stable identities independent of individual uid");

var existingRosterProfile = registeredSpiritProfiles[0];
var existingRosterSnapshot = RosterSnapshot(existingRosterProfile, "existing-roster-spirit");
existingRosterSnapshot.CaptureOrigin = "preexisting-capture";
var existingRosterInstance = new SpiritInstance
{
    SpiritUid = existingRosterSnapshot.SpiritUid,
    SpeciesId = existingRosterProfile.SpeciesId,
    ProfileId = existingRosterProfile.ProfileId,
    Snapshot = existingRosterSnapshot,
    Level = 4,
    Aptitude = 60
};
SpiritTrainingService.InitializeCaptured(existingRosterInstance);
var initialRosterStore = new MemorySpiritStore(new SpiritCollectionDocument
{
    Version = SpiritSystemContract.CollectionVersion,
    Instances = new List<SpiritInstance> { existingRosterInstance },
    DefaultPartySlots = new List<string> { existingRosterInstance.SpiritUid, "", "", "", "", "" },
    DefaultActiveSpiritUid = existingRosterInstance.SpiritUid
});
SpiritCollectionService.Configure(initialRosterStore);
var initialRosterSeeds = registeredSpiritProfiles.Select((profile, index) => new SpiritInitialRosterSeed
{
    ProfileId = profile.ProfileId,
    Snapshot = RosterSnapshot(profile, "initial-roster-" + index)
}).ToArray();
var initialRosterResult = SpiritCollectionService.GrantInitialRoster(initialRosterSeeds);
var grantedRoster = SpiritCollectionService.Snapshot();
Assert(initialRosterResult.Success
       && !initialRosterResult.AlreadyGranted
       && initialRosterResult.GrantedCount == SpiritSystemContract.InitialRosterProfileCount
       && grantedRoster.InitialRosterGrantVersion == SpiritSystemContract.InitialRosterGrantVersion
       && grantedRoster.Instances.Count == SpiritSystemContract.InitialRosterProfileCount + 1
       && grantedRoster.Instances.Count(item => item.ProfileId == existingRosterProfile.ProfileId) == 2
       && grantedRoster.Instances.Count(item => item.Snapshot.CaptureOrigin == SpiritSystemContract.InitialRosterCaptureOrigin)
       == SpiritSystemContract.InitialRosterProfileCount
       && grantedRoster.DefaultPartySlots[0] == existingRosterInstance.SpiritUid
       && grantedRoster.DefaultActiveSpiritUid == existingRosterInstance.SpiritUid
       && initialRosterStore.SaveCount == 1,
    "the enabled initial roster transaction always appends one of all fifty-eight profiles without deduping or changing the party");
var repeatedInitialRoster = SpiritCollectionService.GrantInitialRoster(initialRosterSeeds);
Assert(repeatedInitialRoster.Success
       && repeatedInitialRoster.AlreadyGranted
       && repeatedInitialRoster.GrantedCount == 0
       && SpiritCollectionService.Snapshot().Instances.Count == SpiritSystemContract.InitialRosterProfileCount + 1
       && initialRosterStore.SaveCount == 1,
    "the persisted initial roster version prevents another fifty-eight-instance batch on later loads");

var incompleteRosterStore = new MemorySpiritStore();
SpiritCollectionService.Configure(incompleteRosterStore);
var incompleteRosterResult = SpiritCollectionService.GrantInitialRoster(initialRosterSeeds.Take(
    SpiritSystemContract.InitialRosterProfileCount - 1).ToArray());
Assert(!incompleteRosterResult.Success
       && SpiritCollectionService.Snapshot().Instances.Count == 0
       && SpiritCollectionService.AppliedInitialRosterGrantVersion() == 0
       && incompleteRosterStore.SaveCount == 0,
    "a roster preflight count mismatch leaves the collection and completion marker untouched");

var blockedRosterStore = new MemorySpiritStore { CanGrantInitialRoster = false };
SpiritCollectionService.Configure(blockedRosterStore);
var blockedRosterResult = SpiritCollectionService.GrantInitialRoster(initialRosterSeeds);
Assert(!blockedRosterResult.Success
       && blockedRosterResult.Reason.Contains("blocked", StringComparison.Ordinal)
       && SpiritCollectionService.Snapshot().Instances.Count == 0
       && blockedRosterStore.SaveCount == 0,
    "an unreadable-profile guard blocks automatic roster writes instead of overwriting retained user data");

var failingRosterStore = new MemorySpiritStore();
SpiritCollectionService.Configure(failingRosterStore);
failingRosterStore.FailNextSave = true;
var initialRosterWriteFailed = false;
try { SpiritCollectionService.GrantInitialRoster(initialRosterSeeds); }
catch (IOException) { initialRosterWriteFailed = true; }
Assert(initialRosterWriteFailed
       && SpiritCollectionService.Snapshot().Instances.Count == 0
       && SpiritCollectionService.AppliedInitialRosterGrantVersion() == 0,
    "a failed initial roster save commits neither the fifty-eight instances nor the completion marker");

var legacyStore = new MemorySpiritStore(new SpiritCollectionDocument
{
    Version = 2,
    Instances = new List<SpiritInstance>
    {
        new()
        {
            SpiritUid = "legacy-uid",
            Snapshot = Captured("legacy-species", "legacy-uid"),
            Level = 7,
            Experience = 5,
            Aptitude = 60
        }
    }
});
SpiritCollectionService.Configure(legacyStore);
var migratedCollection = SpiritCollectionService.Snapshot();
Assert(migratedCollection.Version == SpiritCollectionService.CurrentVersion
       && !string.IsNullOrWhiteSpace(migratedCollection.Instances[0].SpeciesId)
       && !string.IsNullOrWhiteSpace(migratedCollection.Instances[0].ProfileId)
       && migratedCollection.Instances[0].Level == 7
       && migratedCollection.Instances[0].Speed is >= 80 and <= 120
       && migratedCollection.Instances[0].LoadoutRevision >= 1
       && migratedCollection.Instances[0].Presentation != null
       && SpiritElementService.TryParse(migratedCollection.Instances[0].ElementId, out _)
       && migratedCollection.Instances[0].ElementSource == SpiritElementService.LegacyMigrationSource
       && migratedCollection.Instances[0].ElementAssignmentRevision == SpiritElementService.AssignmentRevision
       && legacyStore.SaveCount == 1,
    "legacy collections persist deterministic training, element, and localized presentation state during the version migration");
SpiritCollectionService.Configure(legacyStore);
Assert(legacyStore.SaveCount == 1,
    "an already migrated spirit collection is not rewritten merely because it was opened");

var memoryStore = new MemorySpiritStore();
SpiritCollectionService.Configure(memoryStore);
var defaultSessionParty = new SpiritAdventureParty
{
    PartySlots = new List<string> { "uid-default", "", "", "", "", "" },
    ActiveSpiritUid = "uid-default"
};
var sessionStore = new MemorySpiritPartySessionStore();
SpiritAdventurePartySessionService.Configure(sessionStore);
var sessionParty = SpiritAdventurePartySessionService.EnterJourney("journey-a", "player-a", defaultSessionParty);
sessionParty.PartySlots[0] = "uid-captured";
sessionParty.ActiveSpiritUid = "uid-captured";
SpiritAdventurePartySessionService.SaveParty("journey-a", "player-a", sessionParty);
SpiritAdventurePartySessionService.Configure(sessionStore);
var resumedSessionParty = SpiritAdventurePartySessionService.EnterJourney("journey-a", "player-a", defaultSessionParty);
Assert(resumedSessionParty.PartySlots[0] == "uid-captured"
       && resumedSessionParty.ActiveSpiritUid == "uid-captured",
    "player-local adventure party resumes after a reconnect to the same journey");
resumedSessionParty.PartySlots[0] = "not-persisted";
Assert(SpiritAdventurePartySessionService.CurrentOrBegin("journey-a", "player-a", defaultSessionParty).PartySlots[0] == "uid-captured",
    "adventure party reads return defensive snapshots");
var nextJourneyParty = SpiritAdventurePartySessionService.EnterJourney("journey-b", "player-a", defaultSessionParty);
Assert(nextJourneyParty.PartySlots[0] == "uid-default" && nextJourneyParty.ActiveSpiritUid == "uid-default",
    "a new journey initializes from the player's default party");
var otherPlayerParty = SpiritAdventurePartySessionService.EnterJourney("journey-b", "player-b", new SpiritAdventureParty());
Assert(otherPlayerParty.PartySlots.All(string.IsNullOrWhiteSpace),
    "a persisted adventure party never crosses the player-owner boundary");

var party = new SpiritAdventureParty();
for (var index = 0; index < 7; index++)
{
    var result = SpiritCollectionService.Capture(
        Captured("same-species", "uid-" + index),
        "capture-" + index,
        party,
        60);
    Assert(result.Success && result.Instance?.Level == 1 && result.Instance.Aptitude == 60,
        "captured spirit creates an independent level-one individual");
}
Assert(SpiritCollectionService.Snapshot().Instances.Count == 7
       && party.PartySlots.Count(uid => !string.IsNullOrWhiteSpace(uid)) == 6
       && SpiritCollectionService.Snapshot().Instances.Select(item => item.ElementId).Distinct(StringComparer.Ordinal).Count() == 1
       && SpiritCollectionService.Snapshot().Instances.All(item => item.ElementSource == SpiritElementService.CaptureDefaultSource),
    "captured duplicate species freeze the same configured default element while the adventure party remains capped at six");
var capturedDefaultElement = SpiritCollectionService.Find("uid-0")!.ElementId;
var individualOverrideElement = capturedDefaultElement == "electro" ? "pyro" : "electro";
Assert(SpiritCollectionService.SetElement("uid-1", individualOverrideElement)
       && SpiritCollectionService.Find("uid-1") is
       {
           ElementSource: SpiritElementService.ExplicitOverrideSource
       }
       && SpiritCollectionService.Find("uid-1")?.ElementId == individualOverrideElement
       && SpiritCollectionService.Find("uid-0")?.ElementId == capturedDefaultElement,
    "element belongs to the individual so same-species Spirits can diverge without rewriting their species default");
var capturedInstance = SpiritCollectionService.Find("uid-0")!;
var growthView = SpiritGrowthQueryService.Build(capturedInstance);
Assert(!string.IsNullOrWhiteSpace(capturedInstance.SpeciesId)
       && !string.IsNullOrWhiteSpace(capturedInstance.ProfileId)
       && growthView.ElementId == capturedInstance.ElementId
       && growthView.RadarAxes.Count == 4
       && growthView.CurrentAptitudeCurve.Count == 50
       && growthView.StandardAptitudeCurve.Count == 50
       && growthView.TheoreticalAptitudeCurve.Count == 50
       && growthView.RadarAxes.All(axis => axis.Cap == 80),
    "growth query exposes stable identity, four-axis radar data, and all comparison curves");
Assert(capturedInstance.Speed is >= 80 and <= 120
       && capturedInstance.TrainingPlanVersion == SpiritTrainingService.TrainingPlanVersion
       && capturedInstance.UnlockPlan.Count == 5
       && capturedInstance.UnlockPlan.Select(node => node.Stage).SequenceEqual(new[] { 1, 2, 3, 4, 5 })
       && capturedInstance.UnlockPlan[0].RequiredLevel is >= 6 and <= 10
       && capturedInstance.UnlockPlan[1].RequiredLevel is >= 14 and <= 18
       && capturedInstance.UnlockPlan[2].RequiredLevel is >= 23 and <= 28
       && capturedInstance.UnlockPlan[3].RequiredLevel is >= 32 and <= 38
       && capturedInstance.UnlockPlan[4].RequiredLevel is >= 42 and <= 47
       && capturedInstance.UnlockPlan.Select(node => node.AbilityId).Distinct(StringComparer.Ordinal).Count() == 5
       && capturedInstance.UnlockPlan.All(node => !node.Unlocked
                                                  && !capturedInstance.LearnedIntentIds.Contains(node.AbilityId, StringComparer.Ordinal)
                                                  && !capturedInstance.LearnedPassiveIds.Contains(node.AbilityId, StringComparer.Ordinal))
       && capturedInstance.LoadoutRevision >= 1
       && !string.IsNullOrWhiteSpace(capturedInstance.LoadoutHash),
    "capture freezes uniform speed and five hidden, deterministic, non-duplicated future unlock nodes into the individual record");
Assert(SpiritCollectionService.ToggleFavorite("uid-0")
       && SpiritCollectionService.ToggleLocked("uid-0")
       && SpiritCollectionService.Find("uid-0") is { Favorite: true, Locked: true },
    "favorite and lock flags persist through collection mutation");
var duplicateCapture = SpiritCollectionService.Capture(Captured("same-species", "different"), "capture-0", party, 99);
Assert(duplicateCapture.Success && duplicateCapture.DuplicateOperation
       && SpiritCollectionService.Snapshot().Instances.Count == 7,
    "capture operation tokens make persistence idempotent");
memoryStore.FailNextSave = true;
var failedDurableCapture = false;
try { SpiritCollectionService.Capture(Captured("same-species", "uid-failed"), "capture-failed", party, 60); }
catch (IOException) { failedDurableCapture = true; }
Assert(failedDurableCapture
       && SpiritCollectionService.Snapshot().Instances.Count == 7
       && party.PartySlots.All(uid => uid != "uid-failed"),
    "failed durable writes do not commit the captured individual or mutate the run party");
party.ActiveSpiritUid = "uid-0";
var experience = SpiritCollectionService.GrantBattleExperience(
    party.PartySlots,
    party.ActiveSpiritUid,
    20,
    "battle-1");
Assert(experience.Count == 6
       && experience.Single(result => result.Instance.SpiritUid == "uid-0").GainedExperience == 20
       && experience.Where(result => result.Instance.SpiritUid != "uid-0").All(result => result.GainedExperience == 5),
    "battle experience grants 100 percent to active and 25 percent to other carried spirits");
Assert(SpiritCollectionService.GrantBattleExperience(party.PartySlots, party.ActiveSpiritUid, 20, "battle-1").Count == 0,
    "battle experience tokens prevent duplicate settlement");
SpiritBattleDeploymentService.Begin(party, SpiritCollectionService.Snapshot(), 10, 20);
var deployment = SpiritBattleDeploymentService.DeploymentCardSnapshot();
Assert(deployment?.SpiritUid == "uid-0"
       && deployment.SpeciesId == capturedInstance.SpeciesId
       && deployment.ProfileId == capturedInstance.ProfileId
       && deployment.SpiritLevel == 2
       && deployment.SpiritAptitude == 60
       && deployment.SpiritSpeed == capturedInstance.Speed
       && deployment.LoadoutRevision == capturedInstance.LoadoutRevision
       && deployment.TrainingRegistryHash == SpiritTrainingRegistry.RegistryHash
       && deployment.LoadoutHash == SpiritTrainingService.LoadoutHash(deployment)
       && !string.IsNullOrWhiteSpace(deployment.DeploymentToken),
    "battle start freezes speed, loadout revision, and registry identity into one deployment card payload");
var initialBattleState = SpiritBattleDeploymentService.CreateInitialBattleState(deployment!);
var initialProfile = SpiritIntentRegistry.ResolveProfileIdentity(deployment!.ProfileId, deployment.ProfileKey).Profile;
var initialStats = CompanionStatsService.SpiritStats(deployment, initialProfile);
Assert(initialBattleState.CurrentMagic == initialStats.MaxMagic
       && initialBattleState.MaxHp == initialStats.MaxHp
       && initialBattleState.CurrentHp == initialStats.MaxHp,
    "battle initialization fills a Spirit's magic and health from its frozen deployment attributes");
var spentDeploymentStats = new CompanionStats(initialStats.MaxHp, initialStats.MaxMagic, initialStats.Attack, initialStats.Armor);
spentDeploymentStats.SetCurrentMagic(Math.Max(0, initialStats.MaxMagic - 1));
var withdrawnBattleState = SpiritCardBattleState.From(new CompanionBattleState(
    "withdrawn-spirit", deployment.ProfileId, "owner", -1, spentDeploymentStats, "player", entityKind: "SpiritAttachment"));
Assert(withdrawnBattleState.CurrentMagic == Math.Max(0, initialStats.MaxMagic - 1),
    "withdrawing and resummoning preserves remaining magic instead of refilling it per summon");
Assert(SpiritBattleDeploymentService.CanSummon(deployment!, "owner", false, out _),
    "the frozen deployment card is initially legal");
var forgedSpeed = SpiritModelCloner.CloneSnapshot(deployment!);
forgedSpeed.SpiritSpeed = forgedSpeed.SpiritSpeed == SpiritTrainingService.MaximumSpeed
    ? SpiritTrainingService.MinimumSpeed
    : forgedSpeed.SpiritSpeed + 1;
forgedSpeed.LoadoutHash = SpiritTrainingService.LoadoutHash(forgedSpeed);
Assert(!SpiritBattleDeploymentService.CanSummon(forgedSpeed, "owner", true, out _),
    "remote deployment rejects a client-rehashed speed that differs from the captured individual's deterministic roll");
var forgedAbility = SpiritModelCloner.CloneSnapshot(deployment!);
forgedAbility.EquippedIntentIds = new List<string> { commonIntentIds[0] };
forgedAbility.LoadoutHash = SpiritTrainingService.LoadoutHash(forgedAbility);
Assert(!SpiritBattleDeploymentService.CanSummon(forgedAbility, "owner", true, out _),
    "remote deployment reconstructs hidden progression and rejects an equipped ability that is not unlocked at this level");
SpiritBattleDeploymentService.MarkSummoned("owner");
Assert(SpiritBattleDeploymentService.CanSummon(deployment!, "owner", false, out _),
    "the battle deployment remains valid after withdrawal; the active store enforces one Spirit per owner");
SpiritBattleDeploymentService.Clear();

var guiyuanStore = new MemorySpiritStore(new SpiritCollectionDocument
{
    Instances = new List<SpiritInstance>
    {
        new()
        {
            SpiritUid = "guiyuan-target",
            SpeciesId = "species-shared",
            ProfileId = "profile-target",
            Snapshot = Captured("guiyuan-species", "guiyuan-target"),
            Aptitude = 60
        },
        new()
        {
            SpiritUid = "guiyuan-donor",
            SpeciesId = "species-shared",
            ProfileId = "profile-donor-form",
            Snapshot = Captured("guiyuan-species", "guiyuan-donor"),
            Aptitude = 60,
            GuiyuanValue = 3
        }
    }
});
SpiritCollectionService.Configure(guiyuanStore);
var protectedResult = SpiritCollectionService.Guiyuan(
    "guiyuan-target",
    new[] { "guiyuan-donor" },
    new[] { "guiyuan-donor" });
Assert(!protectedResult.Success
       && SpiritCollectionService.Snapshot().Instances.Count == 2,
    "guiyuan rejects party-protected donors without mutating the collection");
var guiyuanResult = SpiritCollectionService.Guiyuan(
    "guiyuan-target",
    new[] { "guiyuan-donor" },
    Array.Empty<string>());
Assert(guiyuanResult.Success
       && guiyuanResult.Preview.OfferedValue == 4
       && guiyuanResult.Target?.GuiyuanValue == 4
       && guiyuanResult.Target.GuiyuanAllocations.Total == 0
       && SpiritCollectionService.Find("guiyuan-donor") == null,
    "same-species forms can be consumed atomically and carry forward one plus their historical guiyuan value");
Assert(SpiritCollectionService.SetGuiyuanAllocations(
           "guiyuan-target",
           new SpiritOriginVector { Magic = 10, Perception = 2 })
       && !SpiritCollectionService.SetGuiyuanAllocations(
           "guiyuan-target",
           new SpiritOriginVector { Magic = 10, Perception = 3 }),
    "origin reallocation is free but cannot exceed the target's cumulative point budget");

var overflowTarget = new SpiritInstance
{
    SpiritUid = "overflow-target",
    SpeciesId = "species-overflow",
    ProfileId = "profile-overflow",
    Snapshot = Captured("overflow-species", "overflow-target"),
    Aptitude = 60,
    GuiyuanValue = 15
};
var overflowDonor = new SpiritInstance
{
    SpiritUid = "overflow-donor",
    SpeciesId = "species-overflow",
    ProfileId = "profile-overflow-form",
    Snapshot = Captured("overflow-species", "overflow-donor"),
    Aptitude = 60,
    GuiyuanValue = 3
};
var overflowPreview = SpiritAscensionService.Preview(overflowTarget, new[] { overflowDonor });
Assert(overflowPreview.OfferedValue == 4
       && overflowPreview.AppliedValue == 1
       && overflowPreview.OverflowValue == 3
       && overflowPreview.ResultStarRank == 5,
    "guiyuan preview explicitly separates effective value from overflow loss");
var failingGuiyuanStore = new MemorySpiritStore(new SpiritCollectionDocument
{
    Instances = new List<SpiritInstance> { overflowTarget, overflowDonor }
});
SpiritCollectionService.Configure(failingGuiyuanStore);
failingGuiyuanStore.FailNextSave = true;
var guiyuanWriteFailed = false;
try { SpiritCollectionService.Guiyuan("overflow-target", new[] { "overflow-donor" }, Array.Empty<string>()); }
catch (IOException) { guiyuanWriteFailed = true; }
Assert(guiyuanWriteFailed
       && SpiritCollectionService.Find("overflow-target")?.GuiyuanValue == 15
       && SpiritCollectionService.Find("overflow-donor") != null,
    "a failed guiyuan save preserves both the target value and every permanent donor");

var starDeploymentStore = new MemorySpiritStore(new SpiritCollectionDocument
{
    Instances = new List<SpiritInstance>
    {
        new()
        {
            SpiritUid = "star-deployment",
            SpeciesId = "species-star",
            ProfileId = "profile-star",
            Snapshot = Captured("star-species", "star-deployment"),
            Aptitude = 60,
            GuiyuanValue = 16,
            GuiyuanAllocations = new SpiritOriginVector { Magic = 10, Perception = 10, Spirit = 10 }
        }
    }
});
SpiritCollectionService.Configure(starDeploymentStore);
var starParty = new SpiritAdventureParty
{
    PartySlots = new List<string> { "star-deployment", "", "", "", "", "" },
    ActiveSpiritUid = "star-deployment"
};
SpiritBattleDeploymentService.Begin(starParty, SpiritCollectionService.Snapshot(), 11, 20);
var starDeployment = SpiritBattleDeploymentService.DeploymentCardSnapshot()!;
var starProfile = SpiritGrowthRegistry.Resolve(SpiritCollectionService.Find("star-deployment")!);
var unstarredDeploymentStats = SpiritGrowthService.BattleStats(
    starProfile,
    new SpiritOriginVector
    {
        Magic = starDeployment.OriginMagic,
        Perception = starDeployment.OriginPerception,
        Spirit = starDeployment.OriginSpirit,
        Luck = starDeployment.OriginLuck
    },
    SpiritIntentRegistry.ProfileForIdentity(starDeployment.ProfileId, starDeployment.ProfileKey),
    starDeployment.SpiritSpeed);
var frozenStarStats = CompanionStatsService.SpiritStats(
    starDeployment,
    SpiritIntentRegistry.ProfileForIdentity(starDeployment.ProfileId, starDeployment.ProfileKey));
Assert(starDeployment.SpiritStarRank == 5
       && starDeployment.SpiritGuiyuanValue == 16
       && SpiritElementService.TryParse(starDeployment.SpiritElementId, out _)
       && frozenStarStats.MaxHp == unstarredDeploymentStats.MaxHp * 2
       && frozenStarStats.Attack == unstarredDeploymentStats.Attack * 2
       && frozenStarStats.Armor == unstarredDeploymentStats.Armor * 2
       && frozenStarStats.MaxMagic == unstarredDeploymentStats.MaxMagic,
    "battle deployment freezes allocated origins and star rank while applying only the three permitted final-stat bonuses");
Assert(SpiritBattleDeploymentService.CanSummon(starDeployment, "star-owner", false, out _),
    "a consistent frozen guiyuan deployment snapshot passes authority validation");
var forgedAllocation = SpiritModelCloner.CloneSnapshot(starDeployment);
forgedAllocation.GuiyuanAllocationLuck++;
Assert(!SpiritBattleDeploymentService.CanSummon(forgedAllocation, "star-owner", true, out _),
    "remote deployment rejects guiyuan allocations that do not match the frozen effective origins");
var forgedElement = SpiritModelCloner.CloneSnapshot(starDeployment);
forgedElement.SpiritElementId = "void";
Assert(!SpiritBattleDeploymentService.CanSummon(forgedElement, "star-owner", true, out _),
    "remote deployment rejects elements outside the seven-type Spirit contract");
SpiritBattleDeploymentService.Clear();

Console.WriteLine($"Terrias spirit runtime tests passed: {assertions} assertions.");

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    assertions++;
}

CapturedEnemySnapshot Captured(string enemyId, string uid)
{
    return new CapturedEnemySnapshot
    {
        SpiritUid = uid,
        EnemyId = enemyId,
        VariantId = enemyId,
        DisplayName = enemyId,
        IdlePath = "idle",
        DictPath = "dict",
        Rarity = 1
    };
}

CapturedEnemySnapshot RosterSnapshot(SpiritSpeciesGrowthProfile profile, string uid)
{
    var match = profile.Match ?? new SpiritSpeciesGrowthMatch();
    var enemyId = match.EnemyId ?? "";
    var variantId = string.IsNullOrWhiteSpace(match.VariantId) || match.VariantId == "*"
        ? enemyId
        : match.VariantId;
    return new CapturedEnemySnapshot
    {
        SpiritUid = uid,
        SourceModId = match.SourceModId ?? "",
        EnemyId = enemyId,
        VariantId = variantId,
        DisplayName = profile.ProfileId,
        AnimationPath = "animation/" + profile.ProfileId,
        IdlePath = "idle/" + profile.ProfileId,
        DictPath = "dict/" + profile.ProfileId,
        CaptureOrigin = SpiritSystemContract.InitialRosterCaptureOrigin,
        CapturedAt = "2026-08-27T00:00:00.0000000Z",
        BaseHp = 40,
        BaseAttack = 8,
        BaseArmor = 3,
        Rarity = profile.Tier == nameof(SpiritSpeciesTier.Normal)
            ? 1
            : profile.Tier == nameof(SpiritSpeciesTier.Elite) ? 2 : 3
    };
}

sealed class MemorySpiritStore : ISpiritCollectionStore, ISpiritInitialRosterGrantGuard
{
    private SpiritCollectionDocument document;

    public MemorySpiritStore(SpiritCollectionDocument? initial = null)
    {
        document = initial ?? new SpiritCollectionDocument();
    }

    public bool FailNextSave { get; set; }

    public bool CanGrantInitialRoster { get; set; } = true;

    public string InitialRosterGrantBlockReason => CanGrantInitialRoster ? "" : "blocked by test store";

    public int SaveCount { get; private set; }

    public SpiritCollectionDocument Load() => document;

    public void Save(SpiritCollectionDocument value)
    {
        SaveCount++;
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new IOException("simulated durable write failure");
        }
        document = value;
    }
}

sealed class MemorySpiritPartySessionStore : ISpiritAdventurePartySessionStore
{
    private SpiritAdventurePartySessionDocument document = new();

    public SpiritAdventurePartySessionDocument Load() => document.Clone();

    public void Save(SpiritAdventurePartySessionDocument value)
    {
        document = value.Clone();
    }
}
