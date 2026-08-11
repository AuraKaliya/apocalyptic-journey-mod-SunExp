using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Terrias.Dll.GameApi;
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
       && !SpiritProfileBindingPolicy.ShouldRecoverLegacy(true, new SpiritCollectionDocument
       {
           ProcessedCaptureTokens = new Dictionary<string, string> { ["capture"] = "uid" }
       }),
    "legacy profile recovery ignores empty fallbacks and never replaces an existing stable profile");

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
SpiritGrowthRegistry.Load(new Witch.Mod.ModConfig { DirectoryName = Path.Combine(repositoryRoot, "Terrias") });
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
Assert(migratedCollection.Version == 3
       && !string.IsNullOrWhiteSpace(migratedCollection.Instances[0].SpeciesId)
       && !string.IsNullOrWhiteSpace(migratedCollection.Instances[0].ProfileId)
       && migratedCollection.Instances[0].Level == 7
       && legacyStore.SaveCount == 0,
    "schema-two collections migrate in memory without rewriting a profile merely because it was opened");

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
       && party.PartySlots.Count(uid => !string.IsNullOrWhiteSpace(uid)) == 6,
    "duplicate species persist independently while the adventure party remains capped at six");
var capturedInstance = SpiritCollectionService.Find("uid-0")!;
var growthView = SpiritGrowthQueryService.Build(capturedInstance);
Assert(!string.IsNullOrWhiteSpace(capturedInstance.SpeciesId)
       && !string.IsNullOrWhiteSpace(capturedInstance.ProfileId)
       && growthView.RadarAxes.Count == 4
       && growthView.CurrentAptitudeCurve.Count == 50
       && growthView.StandardAptitudeCurve.Count == 50
       && growthView.TheoreticalAptitudeCurve.Count == 50
       && growthView.RadarAxes.All(axis => axis.Cap == 80),
    "growth query exposes stable identity, four-axis radar data, and all comparison curves");
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
       && !string.IsNullOrWhiteSpace(deployment.DeploymentToken),
    "battle start freezes the active individual into one deployment card payload");
Assert(SpiritBattleDeploymentService.CanSummon(deployment!, "owner", false, out _),
    "the frozen deployment card is initially legal");
SpiritBattleDeploymentService.MarkSummoned("owner");
Assert(!SpiritBattleDeploymentService.CanSummon(deployment!, "owner", false, out _),
    "one successful deployment blocks copied cards for the same owner");
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

sealed class MemorySpiritStore : ISpiritCollectionStore
{
    private SpiritCollectionDocument document;

    public MemorySpiritStore(SpiritCollectionDocument? initial = null)
    {
        document = initial ?? new SpiritCollectionDocument();
    }

    public bool FailNextSave { get; set; }

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
