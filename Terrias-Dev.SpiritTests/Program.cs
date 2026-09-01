using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using AuraReplay.VisibleState.Shared;
using AuraReplay.Presentation.Shared;
using AuraShared.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

var assertions = 0;

if (args.Length is < 1 or > 2 || !File.Exists(args[0]) || args.Length == 2 && !File.Exists(args[1]))
    throw new ArgumentException("Expected the shipped spirit.artifact.registry.json path and optional live profile path.");
var artifactRegistryDocument = JsonConvert.DeserializeObject<SpiritArtifactRegistryDocument>(File.ReadAllText(args[0]))
    ?? throw new InvalidDataException("Artifact registry deserialization failed.");
typeof(SpiritArtifactRegistry).GetMethod("NormalizeAndValidate", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, new object[] { artifactRegistryDocument });
typeof(SpiritArtifactRegistry).GetMethod("SetDocument", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, new object[] { artifactRegistryDocument });
typeof(SpiritArtifactRegistry).GetField("ready", BindingFlags.NonPublic | BindingFlags.Static)!
    .SetValue(null, true);

var replayProviderType = typeof(SpiritBattleDeploymentService).Assembly.GetType(
    "Terrias.Dll.Hooks.TerriasSpiritReplayVisibleStateProvider",
    throwOnError: true)!;
replayProviderType.GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
var replayVisibleProvider = AuraReplayVisibleStateRuntime.Snapshot()
    .Single(provider => provider.OwnerModId == TerriasIds.ModId && provider.TypeId == "SpiritDeployment");
var projectionReplayVisibleProvider = AuraReplayVisibleStateRuntime.Snapshot()
    .Single(provider => provider.OwnerModId == TerriasIds.ModId && provider.TypeId == "ProjectionDeployment");
var replayEntityProvider = AuraReplayEntityPresentationRuntime.Snapshot()
    .Single(provider => provider.OwnerModId == TerriasIds.ModId);
var replayPresentationModule = AuraReplayPresentationRuntime.SnapshotModules()
    .Single(module => module.OwnerModId == TerriasIds.ModId
                      && module.TypeId == "SpiritBattlePresentation");
var projectionReplayPresentationModule = AuraReplayPresentationRuntime.SnapshotModules()
    .Single(module => module.OwnerModId == TerriasIds.ModId
                      && module.TypeId == "ProjectionBattlePresentation");
var starScoreReplayModule = AuraReplayPresentationRuntime.SnapshotModules()
    .Single(module => module.OwnerModId == TerriasIds.ModId
                      && module.TypeId == "StarScoreHudPresentation");
var wunaOrbitReplayModule = AuraReplayPresentationRuntime.SnapshotModules()
    .Single(module => module.OwnerModId == TerriasIds.ModId
                      && module.TypeId == "WunaOrbitFirePresentation");
var emptyReplayContext = new AuraReplayVisibleCaptureContext { RecordId = "spirit-replay-test" };
Assert(replayVisibleProvider.SchemaVersion == 1
       && replayEntityProvider.SchemaVersion == 1
       && replayPresentationModule.SchemaVersion == 1
       && replayPresentationModule.Portability == AuraReplayPresentationPortability.ProviderRequired
       && replayPresentationModule.RendererCapability == "owner-attached-spirit.v1"
       && projectionReplayVisibleProvider.SchemaVersion == 1
       && projectionReplayPresentationModule.SchemaVersion == 1
       && projectionReplayPresentationModule.Portability == AuraReplayPresentationPortability.Portable
       && string.IsNullOrWhiteSpace(projectionReplayPresentationModule.RendererCapability)
       && starScoreReplayModule.Portability == AuraReplayPresentationPortability.ProviderRequired
       && starScoreReplayModule.RendererCapability == "terrias-star-score-hud.v1"
       && wunaOrbitReplayModule.Portability == AuraReplayPresentationPortability.ProviderRequired
       && wunaOrbitReplayModule.RendererCapability == "terrias-wuna-orbit-fire.v1"
       && replayVisibleProvider.Capture(emptyReplayContext).Count == 0
       && projectionReplayVisibleProvider.Capture(emptyReplayContext).Count == 0
       && replayEntityProvider.Capture(emptyReplayContext).Count == 0,
    "companion replay initialization registers Spirit and Projection visible state plus owner-qualified presentation contracts without a Terrias dependency in AuraTools");

var crossModCaptured = new List<AuraReplayCapturedPresentationEvent>();
using (AuraReplayPresentationRuntime.BeginCapture("terrias-canonical-payload", crossModCaptured.Add))
{
    var spiritPublished = AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
    {
        EventId = "terrias-spirit-canonical",
        OwnerModId = TerriasIds.ModId,
        TypeId = "SpiritBattlePresentation",
        SchemaVersion = 1,
        Kind = AuraReplayPresentationKinds.VisibilityChanged,
        ActorEntityId = "ss-test",
        PayloadJson = AuraSharedJson.SerializeCompact(new
        {
            visible = true,
            Generation = 1,
            SlotIndex = 2,
            SpiritElementId = "fire"
        }),
        Persistent = true
    });
    var projectionPublished = AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
    {
        EventId = "terrias-projection-canonical",
        OwnerModId = TerriasIds.ModId,
        TypeId = "ProjectionBattlePresentation",
        SchemaVersion = 1,
        Kind = AuraReplayPresentationKinds.VisibilityChanged,
        ActorEntityId = "projection-test",
        PayloadJson = AuraSharedJson.SerializeCompact(new
        {
            visible = true,
            generation = "generation-1",
            SlotIndex = 1,
            RoleId = "role-test"
        }),
        Persistent = true
    });
    Assert(spiritPublished == AuraReplayPresentationPublishResult.Published
           && projectionPublished == AuraReplayPresentationPublishResult.Published,
        "Terrias Spirit and Projection multi-field replay payloads publish through the shared canonical boundary");
}
Assert(crossModCaptured.Count == 2
       && crossModCaptured[0].Event.PayloadJson
       == "{\"Generation\":1,\"SlotIndex\":2,\"SpiritElementId\":\"fire\",\"visible\":true}"
       && crossModCaptured[1].Event.PayloadJson
       == "{\"RoleId\":\"role-test\",\"SlotIndex\":1,\"generation\":\"generation-1\",\"visible\":true}",
    "captured Terrias presentation payloads are canonical before AuraToolsExp receives them");

if (args.Length == 2)
{
    var liveProfile = SpiritCollectionDocumentCodec.Deserialize(File.ReadAllText(args[1]));
    Assert(liveProfile.Instances.Count > 0
           && liveProfile.Instances.All(instance => instance.Identity != null
                                                    && instance.Source != null
                                                    && instance.Growth != null
                                                    && instance.Element != null
                                                    && instance.Ascension != null
                                                    && instance.Training != null
                                                    && instance.Equipment != null
                                                    && instance.Metadata != null),
        "the live player profile can be read through the one-way component migration boundary");
}

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
    SpiritDeploymentCodec.Seal(new SpiritDeploymentSnapshot
    {
        Identity = new SpiritDeploymentIdentity
        {
            SpiritUid = "request-spirit",
            SpeciesId = "request-species",
            ProfileId = "request-profile"
        },
        Source = Captured("request-enemy", "request-spirit")
    }),
    "owner-request",
    "request-token",
    2,
    returnedBattleState);
Assert(spiritSummonRequest.Deployment.SpiritUid == "request-spirit"
       && spiritSummonRequest.BattleState.TurnIndex == 4
       && spiritSummonRequest.BattleState.ReadyOnTurn["intent-a"] == 7
       && spiritSummonRequest.BattleState.MaxHp == 35
       && spiritSummonRequest.BattleState.CurrentHp == 19
       && spiritSummonRequest.BattleState.CurrentDefend == 6
       && spiritSummonRequest.BattleState.CurrentMagic == 2
       && spiritSummonRequest.BattleState.PassiveState["passive-a"] == 3
       && spiritSummonRequest.BattleState.VisibleStatuses.Single().Stacks == 2,
    "remote Spirit summon requests preserve the complete withdrawn battle state");
Assert(typeof(Terrias.Dll.Network.RpcSpiritSummonRequest).GetProperty("CapturedEnemy") == null
       && typeof(Terrias.Dll.Network.RpcSpiritSummonRequest).GetProperty("RegistryHash") == null
       && typeof(Terrias.Dll.Network.RpcSpiritSummonRequest).GetProperty("ReadyOnTurn") == null
       && typeof(Terrias.Dll.Network.SpiritCompanionSnapshot).GetProperty("Deployment") != null
       && typeof(Terrias.Dll.Network.SpiritCompanionSnapshot).GetProperty("ReturnedDeployment") != null
       && typeof(Terrias.Dll.Network.SpiritCompanionSnapshot).GetProperty("ReturnedTurnIndex") == null,
    "the network schema exposes one deployment payload and removes retired duplicate identity/hash fields");

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
Assert(CompanionAuthorityService.ProjectionProtocolVersion == 23
       && ProjectionRoleDeckService.CardModelVersion == "projection-role-deck-v3"
       && SpiritCollectionService.CurrentVersion == SpiritSystemContract.CollectionVersion
       && SpiritSystemContract.CollectionVersion == 12
       && SpiritSystemContract.DeploymentProtocolVersion == 1
       && SpiritSystemContract.ReadModelVersion == 1
       && SpiritSystemContract.ArtifactInventoryVersion == 2
       && SpiritSystemContract.ArtifactPresetCapacity == 20
       && SpiritSystemContract.ArtifactBattleProtocolVersion == 1
       && SpiritSystemContract.InitialRosterGrantVersion == 1
       && SpiritSystemContract.InitialRosterProfileCount == 58
       && SpiritSystemContract.InitialRosterConfigurationKey == "GrantAllSpiritsOnFirstLoad"
       && SpiritSystemContract.GrowthRegistrySchemaVersion == 3
       && SpiritSystemContract.TrainingRegistrySchemaVersion == 2,
    "the current Spirit save, registry, and Partner protocol contract stays synchronized");
Assert(SpiritDeploymentFeatureRegistry.FeatureIds().SequenceEqual(
        new[] { "core", "element", "ascension", "training", "artifact" }, StringComparer.Ordinal),
    "every current Spirit subsystem contributes to deployment through one ordered feature registry");

var artifactPool = SpiritArtifactRegistry.Pools().First();
var artifactInventory = new SpiritArtifactInventory
{
    SelectedPoolId = artifactPool.Id,
    TargetSetId = artifactPool.SetIds[0]
};
var artifactDraw = SpiritArtifactRoller.PrepareTenDraw(
    artifactInventory,
    artifactPool.Id,
    artifactPool.SetIds[0],
    new ZeroArtifactRandom(),
    "draw-token",
    "2026-08-28T00:00:00Z");
Assert(artifactDraw.Success && artifactDraw.Results.Count == 10 && artifactDraw.TruthCost == 160,
    "artifact draw creates one atomic 160-Truth ten-pull");
Assert(artifactDraw.Results.Count(item => item.Rarity >= 2) >= 1
       && artifactDraw.Results.All(item => item.Level == 1 && item.SubStatRolls.Count == 0),
    "artifact ten-pull applies its two-star guarantee and Lv.1 main-stat-only contract");
Assert(artifactDraw.Results.All(item => item.SetId == artifactPool.SetIds[0]),
    "zero target roll resolves into the selected artifact set");

artifactInventory.RarityPity = 29;
artifactInventory.TargetFate = 1;
var pityDraw = SpiritArtifactRoller.PrepareTenDraw(
    artifactInventory,
    artifactPool.Id,
    artifactPool.SetIds[0],
    new ZeroArtifactRandom(),
    "pity-token",
    "2026-08-28T00:00:00Z");
Assert(pityDraw.Results[0].Rarity == 3 && pityDraw.Results[0].SetId == artifactPool.SetIds[0]
       && pityDraw.ResultingTargetFate == 0,
    "thirtieth pull and armed target fate force a target-set three-star");

var artifactCollection = new SpiritCollectionDocument
{
    Instances = new List<SpiritInstance>
    {
        new() { SpiritUid = "spirit-a", ArtifactLoadout = new SpiritArtifactLoadout() },
        new() { SpiritUid = "spirit-b", ArtifactLoadout = new SpiritArtifactLoadout() }
    },
    ArtifactInventory = new SpiritArtifactInventory
    {
        Essence = 100,
        SelectedPoolId = artifactPool.Id,
        TargetSetId = artifactPool.SetIds[0],
        Artifacts = new List<SpiritArtifactInstance> { artifactDraw.Results[0].Clone() }
    }
};
var artifactUid = artifactCollection.ArtifactInventory.Artifacts[0].ArtifactUid;
var upgradedArtifact = SpiritArtifactInventoryService.Upgrade(
    artifactCollection, artifactUid, new ZeroArtifactRandom());
Assert(upgradedArtifact.Success
       && upgradedArtifact.Artifact?.Level == 2
       && upgradedArtifact.Artifact.SubStatRolls.Count == 1
       && artifactCollection.ArtifactInventory.Essence == 90,
    "artifact Lv.1 to Lv.2 consumes 10 essence and records one persistent roll");
Assert(SpiritArtifactInventoryService.Equip(artifactCollection, "spirit-a", artifactUid).Success
       && SpiritArtifactInventoryService.EquippedSpiritUid(artifactCollection, artifactUid) == "spirit-a",
    "artifact equips into the selected Spirit instance");
Assert(SpiritArtifactInventoryService.Equip(artifactCollection, "spirit-b", artifactUid).Success
       && SpiritArtifactInventoryService.EquippedSpiritUid(artifactCollection, artifactUid) == "spirit-b"
       && artifactCollection.Instances[0].ArtifactLoadout.ArtifactUids().Count == 0
       && artifactCollection.Instances[0].ArtifactLoadout.Revision == 2
       && artifactCollection.Instances[1].ArtifactLoadout.Revision == 1,
    "equipping an owned artifact atomically transfers it and advances every changed Spirit revision");
Assert(!SpiritArtifactInventoryService.Dismantle(artifactCollection, new[] { artifactUid }).Success,
    "equipped artifacts cannot be dismantled");
Assert(SpiritArtifactInventoryService.Unequip(
        artifactCollection, "spirit-b", artifactCollection.ArtifactInventory.Artifacts[0].SlotId).Success,
    "equipped artifact can be explicitly removed from its slot");
var artifactBattle = SpiritArtifactLoadoutResolver.Resolve(artifactCollection, artifactCollection.Instances[1]).Battle;
Assert(SpiritArtifactLoadoutResolver.ValidateBattleSnapshot(artifactBattle, out _),
    "empty artifact loadout still produces a registry-bound valid battle snapshot");
var dismantledArtifact = SpiritArtifactInventoryService.Dismantle(artifactCollection, new[] { artifactUid });
Assert(dismantledArtifact.Success && dismantledArtifact.EssenceDelta == 8
       && artifactCollection.ArtifactInventory.Essence == 98,
    "Lv.2 one-star dismantle returns base essence plus 70 percent of invested essence");

var presetArtifacts = CreatePresetArtifacts(artifactPool.SetIds[0], "preset-primary");
var alternateFlower = CreatePresetArtifacts(artifactPool.SetIds[1], "preset-alternate")
    .First(value => value.SlotId == SpiritArtifactSlots.Flower);
var presetCollection = new SpiritCollectionDocument
{
    Instances = new List<SpiritInstance>
    {
        new() { SpiritUid = "preset-spirit-a", ArtifactLoadout = new SpiritArtifactLoadout() },
        new() { SpiritUid = "preset-spirit-b", ArtifactLoadout = new SpiritArtifactLoadout() },
        new() { SpiritUid = "preset-spirit-c", ArtifactLoadout = new SpiritArtifactLoadout() }
    },
    ArtifactInventory = new SpiritArtifactInventory
    {
        SelectedPoolId = artifactPool.Id,
        TargetSetId = artifactPool.SetIds[0],
        Artifacts = presetArtifacts.Concat(new[] { alternateFlower }).Select(value => value.Clone()).ToList()
    }
};
var primaryDraft = new SpiritArtifactPreset { Name = "主预设" };
foreach (var artifact in presetArtifacts) primaryDraft.Set(artifact.SlotId, artifact.ArtifactUid);
var savedPrimary = SpiritArtifactPresetService.Save(presetCollection, primaryDraft);
Assert(savedPrimary.Success && savedPrimary.Preset != null
       && SpiritArtifactPresetService.ProtectedArtifactUids(presetCollection).Count == 5,
    "account artifact preset stores five exact instances and protects their union");
var protectedFlower = presetArtifacts.First(value => value.SlotId == SpiritArtifactSlots.Flower);
Assert(!SpiritArtifactInventoryService.Dismantle(presetCollection, new[] { protectedFlower.ArtifactUid }).Success,
    "preset-referenced artifacts cannot be dismantled even while unequipped");

var secondaryDraft = savedPrimary.Preset!.Clone();
secondaryDraft.PresetUid = "";
secondaryDraft.Name = "共享预设";
secondaryDraft.Set(SpiritArtifactSlots.Flower, alternateFlower.ArtifactUid);
var savedSecondary = SpiritArtifactPresetService.Save(presetCollection, secondaryDraft);
Assert(savedSecondary.Success
       && SpiritArtifactPresetService.ProtectedArtifactUids(presetCollection).Count == 6,
    "different account presets may share exact artifact instances without duplicate ownership data");

var primaryPreset = savedPrimary.Preset!;
var primaryBySlot = presetArtifacts.ToDictionary(value => value.SlotId, value => value.ArtifactUid, StringComparer.Ordinal);
Assert(SpiritArtifactInventoryService.Equip(
           presetCollection, "preset-spirit-a", primaryBySlot[SpiritArtifactSlots.Flower]).Success
       && SpiritArtifactInventoryService.Equip(
           presetCollection, "preset-spirit-a", primaryBySlot[SpiritArtifactSlots.Plume]).Success
       && SpiritArtifactInventoryService.Equip(
           presetCollection, "preset-spirit-b", primaryBySlot[SpiritArtifactSlots.Sands]).Success
       && SpiritArtifactInventoryService.Equip(
           presetCollection, "preset-spirit-b", primaryBySlot[SpiritArtifactSlots.Goblet]).Success,
    "preset transfer fixture distributes exact instances across multiple Spirits");
var beforePresetA = presetCollection.Instances[0].ArtifactLoadout.Revision;
var beforePresetB = presetCollection.Instances[1].ArtifactLoadout.Revision;
var appliedPreset = SpiritArtifactPresetService.Apply(
    presetCollection, "preset-spirit-c", primaryPreset.PresetUid);
Assert(appliedPreset.Success && appliedPreset.TransferredArtifactCount == 4
       && appliedPreset.AffectedSpiritUids.OrderBy(value => value, StringComparer.Ordinal)
           .SequenceEqual(new[] { "preset-spirit-a", "preset-spirit-b", "preset-spirit-c" })
       && presetCollection.Instances[0].ArtifactLoadout.ArtifactUids().Count == 0
       && presetCollection.Instances[1].ArtifactLoadout.ArtifactUids().Count == 0
       && SpiritArtifactSlots.All.All(slot => presetCollection.Instances[2].ArtifactLoadout.Get(slot) == primaryPreset.Get(slot))
       && presetCollection.Instances[0].ArtifactLoadout.Revision == beforePresetA + 1
       && presetCollection.Instances[1].ArtifactLoadout.Revision == beforePresetB + 1,
    "applying an account preset atomically strips prior owners and equips the current Spirit");
var repeatedPreset = SpiritArtifactPresetService.Apply(
    presetCollection, "preset-spirit-c", primaryPreset.PresetUid);
Assert(repeatedPreset.Success && repeatedPreset.AffectedSpiritUids.Count == 0
       && repeatedPreset.TransferredArtifactCount == 0,
    "reapplying the already active preset is an idempotent no-op");

var filteredPresetItems = SpiritArtifactInventoryQueryService.Filter(
    presetCollection,
    new SpiritArtifactInventoryFilter { CleanableOnly = true });
Assert(filteredPresetItems.Count == 0,
    "cleanable select-all query excludes equipped and every preset-referenced artifact");
Assert(SpiritArtifactPresetService.Delete(presetCollection, primaryPreset.PresetUid).Success
       && SpiritArtifactPresetService.IsProtected(presetCollection, primaryBySlot[SpiritArtifactSlots.Plume])
       && !SpiritArtifactPresetService.IsProtected(presetCollection, primaryBySlot[SpiritArtifactSlots.Flower]),
    "preset protection is reference-counted across the remaining account presets");
var unprotectedArtifact = CreatePresetArtifacts(artifactPool.SetIds[1], "unprotected")
    .First(value => value.SlotId == SpiritArtifactSlots.Plume);
presetCollection.ArtifactInventory.Artifacts.Add(unprotectedArtifact);
Assert(!SpiritArtifactInventoryService.Dismantle(
           presetCollection,
           new[] { unprotectedArtifact.ArtifactUid, primaryBySlot[SpiritArtifactSlots.Plume] }).Success
       && SpiritArtifactInventoryService.Find(presetCollection, unprotectedArtifact.ArtifactUid) != null,
    "batch dismantle remains all-or-nothing when any requested item has preset protection");
var queryArtifacts = CreatePresetArtifacts(artifactPool.SetIds[0], "query").Take(3).ToList();
queryArtifacts[0].Rarity = 1;
queryArtifacts[1].Rarity = 2;
queryArtifacts[2].Rarity = 3;
queryArtifacts[1].Locked = true;
var queryCollection = new SpiritCollectionDocument
{
    ArtifactInventory = new SpiritArtifactInventory { Artifacts = queryArtifacts }
};
var rarityFiltered = SpiritArtifactInventoryQueryService.Filter(
    queryCollection,
    new SpiritArtifactInventoryFilter { RarityMask = (1 << 1) | (1 << 3) });
Assert(rarityFiltered.Count == 2 && rarityFiltered.All(value => value.Rarity is 1 or 3),
    "artifact filter supports selecting multiple rarity bands without duplicating the warehouse query");
Assert(SpiritArtifactInventoryQueryService.SelectAllCleanable(queryCollection, queryArtifacts).Count == 2,
    "artifact select-all candidate count excludes locked items before UI selection");
var oversizedPresetInventory = new SpiritArtifactInventory
{
    Presets = Enumerable.Range(0, 21).Select(index => new SpiritArtifactPreset
    {
        PresetUid = index < 2 ? "duplicate-id" : "preset-" + index,
        Name = index < 2 ? "重复名称" : "预设 " + index,
        Order = index
    }).ToList()
};
SpiritArtifactPresetService.NormalizeInventory(oversizedPresetInventory);
Assert(oversizedPresetInventory.Presets.Count == 20
       && oversizedPresetInventory.Presets.Select(value => value.PresetUid).Distinct(StringComparer.Ordinal).Count() == 20
       && oversizedPresetInventory.Presets.Select(value => value.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 20
       && oversizedPresetInventory.Presets.Select(value => value.Order).SequenceEqual(Enumerable.Range(0, 20)),
    "account preset normalization enforces the 20-slot bound and deterministic unique identity/order");
var legacyArtifactStore = new MemorySpiritStore(new SpiritCollectionDocument
{
    Version = 10,
    ArtifactInventory = new SpiritArtifactInventory
    {
        Version = 1,
        SelectedPoolId = artifactPool.Id,
        TargetSetId = artifactPool.SetIds[0],
        Presets = null!
    }
});
SpiritCollectionService.Configure(legacyArtifactStore);
var migratedArtifactCollection = SpiritCollectionService.Snapshot();
Assert(legacyArtifactStore.SaveCount == 1
       && migratedArtifactCollection.Version == SpiritSystemContract.CollectionVersion
       && migratedArtifactCollection.ArtifactInventory.Version == 2
       && migratedArtifactCollection.ArtifactInventory.Presets.Count == 0,
    "the current collection schema performs one durable migration to the account preset inventory contract");

var artifactCombatState = new CompanionBattleState(
    "artifact-spirit", "role", "owner", -1, new CompanionStats(100, 10, 30, 20),
    entityKind: "SpiritAttachment");
artifactCombatState.ConfigureArtifactBattle(new SpiritArtifactBattleSnapshot
{
    ProtocolVersion = 1,
    ActiveEffects = new List<SpiritArtifactActiveEffectSnapshot>
    {
        new()
        {
            SetId = "gladiator",
            RequiredPieces = 2,
            EffectId = "gladiator.damage",
            HandlerId = "intent.attack.damage-percent",
            Amount = 8
        },
        new()
        {
            SetId = "gladiator",
            RequiredPieces = 4,
            EffectId = "gladiator.triumph",
            HandlerId = "intent.attack.triumph",
            Amount = 4,
            Maximum = 3
        }
    }
});
artifactCombatState.SetPassiveValue("artifact.gladiator.triumph.stacks", 3);
var artifactDamageEffects = new List<CompanionResolvedEffect>
{
    new() { HandlerId = "damage.single", Value = 125, RepeatCount = 1 }
};
var artifactCost = 2;
var artifactModifierKeys = new List<string>();
SpiritArtifactBattleRuntime.ApplyPlanModifiers(
    artifactCombatState,
    new CompanionIntentDefinition { Id = "attack", Type = "Attack", Cost = 2 },
    artifactDamageEffects,
    ref artifactCost,
    artifactModifierKeys);
Assert(artifactDamageEffects[0].PreArtifactValue == 125
       && artifactDamageEffects[0].ArtifactDamageBonusBasisPoints == 2000
       && artifactDamageEffects[0].Value == 150,
    "two- and four-piece damage bonuses add inside one independent artifact multiplier");

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
    SpiritUid = "existing-roster-spirit",
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

// Retain this migration contract while V11 player profiles remain supported; remove it with the V11 reader.
var legacyV11Root = new JObject
{
    ["Version"] = 11,
    ["Instances"] = new JArray
    {
        new JObject
        {
            ["SpiritUid"] = "legacy-v11-component",
            ["SpeciesId"] = "legacy-v11-species",
            ["ProfileId"] = "legacy-v11-profile",
            ["ElementId"] = "pyro",
            ["ElementSource"] = SpiritElementService.ExplicitOverrideSource,
            ["ElementAssignmentRevision"] = SpiritElementService.AssignmentRevision,
            ["Snapshot"] = JObject.FromObject(Captured("legacy-v11-enemy", "legacy-v11-component")),
            ["Presentation"] = JObject.FromObject(new SpiritLocalizedPresentation()),
            ["Level"] = 7,
            ["Experience"] = 5,
            ["Aptitude"] = 73,
            ["Speed"] = 88,
            ["GuiyuanValue"] = 2,
            ["GuiyuanAllocations"] = JObject.FromObject(new SpiritOriginVector { Magic = 2, Spirit = 4 }),
            ["TrainingPlanVersion"] = SpiritSystemContract.TrainingPlanVersion,
            ["InherentAbilityPlanVersion"] = SpiritSystemContract.InherentAbilityPlanVersion,
            ["ResolvedInherentIntentIds"] = new JArray(),
            ["ResolvedInherentPassiveId"] = "",
            ["LearnedIntentIds"] = new JArray(),
            ["EquippedIntentIds"] = new JArray(),
            ["LearnedPassiveIds"] = new JArray(),
            ["EquippedPassiveId"] = "",
            ["UnlockPlan"] = new JArray(),
            ["NewAbilityIds"] = new JArray(),
            ["LoadoutRevision"] = 1,
            ["LoadoutHash"] = "legacy-loadout",
            ["ArtifactLoadout"] = JObject.FromObject(new SpiritArtifactLoadout()),
            ["Favorite"] = true,
            ["Locked"] = false,
            ["CapturedAt"] = "2026-08-01T00:00:00Z"
        }
    },
    ["DefaultPartySlots"] = new JArray("legacy-v11-component", "", "", "", "", ""),
    ["DefaultActiveSpiritUid"] = "legacy-v11-component",
    ["ProcessedCaptureTokens"] = new JObject(),
    ["ProcessedBattleTokens"] = new JArray(),
    ["ArtifactInventory"] = JObject.FromObject(new SpiritArtifactInventory())
};
var decodedV11 = SpiritCollectionDocumentCodec.Deserialize(legacyV11Root.ToString(Formatting.None));
Assert(decodedV11.Version == 11
       && decodedV11.Instances.Count == 1
       && decodedV11.Instances[0].Identity.SpiritUid == "legacy-v11-component"
       && decodedV11.Instances[0].Growth.Level == 7
       && decodedV11.Instances[0].Growth.Aptitude == 73
       && decodedV11.Instances[0].Ascension.GuiyuanValue == 2
       && decodedV11.Instances[0].Metadata.Favorite,
    "V11 flat Spirit records migrate deterministically into typed components before normalization");
var invalidCurrentRoot = (JObject)legacyV11Root.DeepClone();
invalidCurrentRoot["Version"] = SpiritSystemContract.CollectionVersion;
var invalidCurrentRejected = false;
try { SpiritCollectionDocumentCodec.Deserialize(invalidCurrentRoot.ToString(Formatting.None)); }
catch (InvalidOperationException) { invalidCurrentRejected = true; }
Assert(invalidCurrentRejected,
    "the current collection schema rejects a flat instance instead of keeping a permanent dual reader");
var componentMigrationStore = new MemorySpiritStore(decodedV11);
SpiritCollectionService.Configure(componentMigrationStore);
var componentMigrated = SpiritCollectionService.Snapshot();
var componentSerialized = SpiritCollectionDocumentCodec.Serialize(componentMigrated);
var componentJson = JObject.Parse(componentSerialized);
var componentJsonInstance = (JObject)componentJson["Instances"]![0]!;
Assert(componentMigrated.Version == SpiritSystemContract.CollectionVersion
       && componentMigrated.Revision >= 1
       && componentMigrationStore.SaveCount == 1
       && componentJsonInstance["Identity"] != null
       && componentJsonInstance["Growth"] != null
       && componentJsonInstance["Training"] != null
       && componentJsonInstance["Equipment"] != null
       && componentJsonInstance["SpiritUid"] == null
       && componentJsonInstance["Level"] == null
       && componentJsonInstance["ArtifactLoadout"] == null
       && !componentSerialized.Contains('\n'),
    "the V12 cutover saves only component state and removes the retired flat schema");
var readModelStoreType = typeof(SpiritCollectionService).Assembly.GetType("Terrias.Dll.Mechanics.SpiritReadModelStore")
                         ?? throw new TypeLoadException("SpiritReadModelStore");
var readModelCurrent = readModelStoreType.GetMethod("Current", BindingFlags.Public | BindingFlags.Static)
                       ?? throw new MissingMethodException("SpiritReadModelStore.Current");
var firstReadModel = readModelCurrent.Invoke(null, null)!;
var repeatedReadModel = readModelCurrent.Invoke(null, null)!;
SpiritCollectionService.ToggleLocked("legacy-v11-component");
var invalidatedReadModel = readModelCurrent.Invoke(null, null)!;
var codexView = (IReadOnlyList<SpiritCodexEntryView>)(invalidatedReadModel.GetType()
    .GetProperty("Codex")?.GetValue(invalidatedReadModel)
    ?? throw new MissingMemberException("SpiritReadModelSnapshot.Codex"));
Assert(ReferenceEquals(firstReadModel, repeatedReadModel)
       && !ReferenceEquals(firstReadModel, invalidatedReadModel)
       && codexView.Count == SpiritGrowthRegistry.RegisteredProfiles().Count,
    "the read model is reused within one account generation, invalidated by a commit, and materializes the complete codex");

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
var capturedUids = new List<string>();
for (var index = 0; index < 7; index++)
{
    var result = SpiritCollectionService.Capture(
        Captured("same-species", "uid-" + index),
        "capture-" + index,
        party,
        60);
    Assert(result.Success && result.Instance?.Level == 1 && result.Instance.Aptitude == 60,
        "captured spirit creates an independent level-one individual");
    capturedUids.Add(result.Instance!.SpiritUid);
}
Assert(SpiritCollectionService.Snapshot().Instances.Count == 7
       && party.PartySlots.Count(uid => !string.IsNullOrWhiteSpace(uid)) == 6
       && SpiritCollectionService.Snapshot().Instances.Select(item => item.ElementId).Distinct(StringComparer.Ordinal).Count() == 1
       && SpiritCollectionService.Snapshot().Instances.All(item => item.ElementSource == SpiritElementService.CaptureDefaultSource),
    "captured duplicate species freeze the same configured default element while the adventure party remains capped at six");
var capturedUid0 = capturedUids[0];
var capturedUid1 = capturedUids[1];
var capturedDefaultElement = SpiritCollectionService.Find(capturedUid0)!.ElementId;
var individualOverrideElement = capturedDefaultElement == "electro" ? "pyro" : "electro";
Assert(SpiritCollectionService.SetElement(capturedUid1, individualOverrideElement)
       && SpiritCollectionService.Find(capturedUid1) is
       {
           ElementSource: SpiritElementService.ExplicitOverrideSource
       }
       && SpiritCollectionService.Find(capturedUid1)?.ElementId == individualOverrideElement
       && SpiritCollectionService.Find(capturedUid0)?.ElementId == capturedDefaultElement,
    "element belongs to the individual so same-species Spirits can diverge without rewriting their species default");
var capturedInstance = SpiritCollectionService.Find(capturedUid0)!;
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
Assert(SpiritCollectionService.ToggleFavorite(capturedUid0)
       && SpiritCollectionService.ToggleLocked(capturedUid0)
       && SpiritCollectionService.Find(capturedUid0) is { Favorite: true, Locked: true },
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
       && party.PartySlots.Where(uid => !string.IsNullOrWhiteSpace(uid)).All(capturedUids.Contains),
    "failed durable writes do not commit the captured individual or mutate the run party");
party.ActiveSpiritUid = capturedUid0;
var experience = SpiritCollectionService.GrantBattleExperience(
    party.PartySlots,
    party.ActiveSpiritUid,
    20,
    "battle-1");
Assert(experience.Count == 6
       && experience.Single(result => result.Instance.SpiritUid == capturedUid0).GainedExperience == 20
       && experience.Where(result => result.Instance.SpiritUid != capturedUid0).All(result => result.GainedExperience == 5),
    "battle experience grants 100 percent to active and 25 percent to other carried spirits");
Assert(SpiritCollectionService.GrantBattleExperience(party.PartySlots, party.ActiveSpiritUid, 20, "battle-1").Count == 0,
    "battle experience tokens prevent duplicate settlement");
SpiritBattleDeploymentService.Begin(party, SpiritCollectionService.Snapshot(), 10, 20);
var deployment = SpiritBattleDeploymentService.DeploymentCardSnapshot();
Assert(deployment?.SpiritUid == capturedUid0
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
var forgedSpeed = deployment!.Clone();
forgedSpeed.Growth.Speed = forgedSpeed.SpiritSpeed == SpiritTrainingService.MaximumSpeed
    ? SpiritTrainingService.MinimumSpeed
    : forgedSpeed.SpiritSpeed + 1;
forgedSpeed.Training.LoadoutHash = SpiritTrainingService.LoadoutHash(forgedSpeed);
forgedSpeed = SpiritDeploymentCodec.Seal(forgedSpeed);
Assert(!SpiritBattleDeploymentService.CanSummon(forgedSpeed, "owner", true, out _),
    "remote deployment rejects a client-rehashed speed that differs from the captured individual's deterministic roll");
var forgedAbility = deployment.Clone();
forgedAbility.Training.EquippedIntentIds = new List<string> { commonIntentIds[0] };
forgedAbility.Training.LoadoutHash = SpiritTrainingService.LoadoutHash(forgedAbility);
forgedAbility = SpiritDeploymentCodec.Seal(forgedAbility);
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
var deploymentJson = SpiritDeploymentCodec.Serialize(starDeployment);
Assert(SpiritDeploymentCodec.TryDeserialize(deploymentJson, out var deploymentRoundTrip, out _)
       && deploymentRoundTrip.SpiritUid == starDeployment.SpiritUid
       && deploymentRoundTrip.SpiritStarRank == 5
       && deploymentRoundTrip.ArtifactBattle.RegistryHash == SpiritArtifactRegistry.RegistryHash
       && deploymentRoundTrip.IntentRegistryHash == SpiritIntentRegistry.RegistryHash
       && deploymentRoundTrip.TrainingRegistryHash == SpiritTrainingRegistry.RegistryHash,
    "the authoritative deployment codec round-trips every registered Spirit feature component");
var runtimeBuilder = typeof(SpiritCardFactory).GetMethod("BuildRuntime", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new MissingMethodException("SpiritCardFactory.BuildRuntime");
var deploymentRuntime = (Dictionary<string, string>)runtimeBuilder.Invoke(
    null,
    new object[] { starDeployment, 0, SpiritBattleDeploymentService.CreateInitialBattleState(starDeployment) })!;
Assert(deploymentRuntime.TryGetValue(TerriasIds.SpiritDeploymentPayloadKey, out var cardPayload)
       && SpiritDeploymentCodec.TryDeserialize(cardPayload, out var cardRoundTrip, out _)
       && cardRoundTrip.PayloadHash == starDeployment.PayloadHash
       && !deploymentRuntime.ContainsKey("TerriasSpiritTrainingRegistryHash")
       && !deploymentRuntime.ContainsKey("TerriasSpiritDeploymentToken"),
    "the real Spirit card runtime writes one versioned deployment payload and no retired flat battle fields");
var tamperedDeployment = JObject.Parse(deploymentJson);
tamperedDeployment["Growth"]!["Level"] = 99;
Assert(!SpiritDeploymentCodec.TryDeserialize(
        tamperedDeployment.ToString(Formatting.None), out _, out var tamperReason)
       && tamperReason.Contains("哈希", StringComparison.Ordinal),
    "deployment integrity rejects a card or network payload whose component data changed after sealing");
Assert(!SpiritDeploymentCodec.TryDeserialize(
        new string('x', SpiritDeploymentCodec.MaximumSerializedBytes + 1), out _, out _),
    "deployment payload guards reject an oversized card or RPC body before deserialization");
var forgedAllocation = starDeployment.Clone();
forgedAllocation.Ascension.Allocations.Luck++;
forgedAllocation = SpiritDeploymentCodec.Seal(forgedAllocation);
Assert(!SpiritBattleDeploymentService.CanSummon(forgedAllocation, "star-owner", true, out _),
    "remote deployment rejects guiyuan allocations that do not match the frozen effective origins");
var forgedElement = starDeployment.Clone();
forgedElement.Element.ElementId = "void";
forgedElement = SpiritDeploymentCodec.Seal(forgedElement);
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

List<SpiritArtifactInstance> CreatePresetArtifacts(string setId, string uidPrefix)
{
    var result = new List<SpiritArtifactInstance>();
    for (var index = 0; index < SpiritArtifactSlots.All.Count; index++)
    {
        var slot = SpiritArtifactSlots.All[index];
        var piece = SpiritArtifactRegistry.PieceFor(setId, slot)
                    ?? throw new InvalidOperationException("Missing artifact piece for preset test: " + setId + "/" + slot);
        var statId = slot == SpiritArtifactSlots.Flower ? SpiritArtifactStats.Life : SpiritArtifactStats.Magic;
        var range = SpiritArtifactRegistry.Range(statId, 3, main: true);
        result.Add(new SpiritArtifactInstance
        {
            ArtifactUid = uidPrefix + "-" + slot,
            SetId = setId,
            PieceId = piece.Id,
            SlotId = slot,
            Rarity = 3,
            Level = 1,
            MainStat = new SpiritArtifactStatRoll { StatId = statId, Value = range.Minimum },
            AcquiredAt = "2026-08-29T00:00:00Z"
        });
    }
    return result;
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

sealed class ZeroArtifactRandom : ISpiritArtifactRandom
{
    public int Next(int exclusiveMaximum) => 0;
}
