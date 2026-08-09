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
    public static void TestDamageMeterSettingsNormalization()
    {
        var settings = new DamageMeterSettings
        {
            FriendlyOnly = true,
            ShowPanelByDefault = true,
            IncludeUnknownTeam = true,
            CountShieldLoss = false,
            MaxRows = 12,
            ShowAverageDpt = false,
            ShowTeamShare = false,
            UiRefreshIntervalMs = 0,
            SubmitBatchIntervalMs = 0,
            MaxEventsPerBatch = 0
        };
    
        settings.Normalize();
        Assert(!settings.ShowPanelByDefault, "DPS panel is always collapsed by default");
        Assert(!settings.IncludeUnknownTeam, "friendly-only DPS excludes unknown-team damage");
        Assert(settings.CountShieldLoss, "shield damage display is always enabled");
        Assert(settings.MaxRows == 6, "DPS row count uses the fixed default");
        Assert(settings.ShowAverageDpt, "average DPT display is always enabled");
        Assert(settings.ShowTeamShare, "team damage share display is always enabled");
        Assert(settings.UiRefreshIntervalMs == 1000, "DPS UI refresh falls back to the bounded default");
        Assert(settings.SubmitBatchIntervalMs == 250, "DPS network submit batching falls back to the bounded default");
        Assert(settings.MaxEventsPerBatch == 24, "DPS network submit batch size falls back to the bounded default");
        Assert(settings.SettlementCg.Enabled
               && settings.SettlementCg.BackgroundResource == "Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png"
               && settings.SettlementCg.BaseWidth == 1600
               && settings.SettlementCg.BaseHeight == 900
               && settings.SettlementCg.SlotSize == 180,
            "DPS settlement CG defaults normalize with the damage meter");
    
        settings.FriendlyOnly = false;
        settings.UiRefreshIntervalMs = 20;
        settings.SubmitBatchIntervalMs = 5000;
        settings.MaxEventsPerBatch = 1000;
        settings.Normalize();
        Assert(settings.IncludeUnknownTeam, "unfiltered DPS includes unknown-team damage");
        Assert(settings.UiRefreshIntervalMs == 100
               && settings.SubmitBatchIntervalMs == 1000
               && settings.MaxEventsPerBatch == 64,
            "DPS performance knobs are clamped to runtime-safe bounds");
    }
    
    public static void TestConfigModelSerializationCompatibility()
    {
        var root = JsonConvert.DeserializeObject<AuraToolsRootConfig>(
            "{\"schemaVersion\":0,\"audio\":null,\"matchExperience\":null,\"skillCg\":null,\"skin\":null,\"logging\":null}")!;
        root.Normalize();
        Assert(root.SchemaVersion == 1
               && root.Audio.ConfigFile == "AudioSettings.json"
               && root.MatchExperience.ConfigFile == "MatchExperienceSettings.json"
               && root.SkillCg.ConfigFile == "SkillCgSettings.json"
               && root.Skin.ConfigFile == "SkinSettings.json"
               && root.Logging.ConfigFile == "LoggingSettings.json",
            "root config preserves module-file defaults after JSON deserialization");
    
        var rootJson = JsonConvert.SerializeObject(root);
        var restoredRoot = JsonConvert.DeserializeObject<AuraToolsRootConfig>(rootJson)!;
        Assert(restoredRoot.Audio.Enabled
               && restoredRoot.Logging.Enabled
               && rootJson.Contains("\"matchExperience\"")
               && rootJson.Contains("\"configFile\""),
            "root config keeps its established JSON property contract across a round trip");
    
        var audio = JsonConvert.DeserializeObject<AuraToolsAudioSettings>(
            "{\"schemaVersion\":1,\"audioSystemVersion\":\" \",\"battleBgm\":{\"common\":{\"relativePath\":\"Audio/Common/battle_bgm.mp3\"}},\"cardUse\":null}")!;
        audio.Normalize();
        Assert(audio.SchemaVersion == 3
               && audio.AudioSystemVersion == "2.0.0"
               && audio.BattleBgm.Common.RelativePath == "Audio/Common/battle_bgm.mp3"
               && audio.CardUse.Common.RelativePath == "Audio/Global/all/CardUse/AuraToolsExp/default-card-use/content.mp3",
            "audio config preserves user resource paths while recovering missing domains");
    
        var matchExperience = JsonConvert.DeserializeObject<AuraToolsMatchExperienceSettings>(
            "{\"schemaVersion\":1,\"starterDeck\":{\"preferRoleModProfile\":false},\"safeBox\":null,\"modSync\":null,\"feast\":null,\"damageMeter\":null,\"cardRefresh\":null,\"autoBattle\":null}")!;
        matchExperience.Normalize();
        Assert(matchExperience.SchemaVersion == 26
               && matchExperience.StarterDeck.PreferRoleModProfile
               && matchExperience.SafeBox != null
               && matchExperience.ModSync != null
               && matchExperience.Feast.Enabled
               && matchExperience.DamageMeter != null
               && matchExperience.CardRefresh != null
               && matchExperience.AutoBattle != null
               && matchExperience.AutoBattle.Profile == "balanced"
               && matchExperience.AutoBattle.UnknownActionPolicy == "conservative"
               && matchExperience.AutoBattle.TrainingMode == "hybrid"
               && matchExperience.AutoBattle.ShowPredictionMarkers
               && matchExperience.AutoBattle.TrainedModelMode == "off"
               && matchExperience.AutoBattle.SearchQuality == "balanced"
               && matchExperience.AutoBattle.GameParameters.ActivePreset.RoleId
                  == "career_1"
               && matchExperience.AutoBattle.GameParameters.ActivePreset.PartnerId
                  == "Partner_10001"
               && matchExperience.AutoBattle.GameParameters.ActivePreset
                   .EnabledRewardCardPackIds.Contains("cardpack_1")
               && matchExperience.AutoBattle.GameParameters.ActivePreset
                   .EnabledRewardCardPackIds.Contains("cardpack_2")
               && !matchExperience.AutoBattle.GameParameters.ActivePreset
                   .EnabledRewardCardPackIds.Contains("cardpack_13")
               && matchExperience.AutoBattle.GameParameters.ActivePreset
                      .PreferredDeckSizeMinimum == 15
               && matchExperience.AutoBattle.GameParameters.ActivePreset
                      .PreferredDeckSizeMaximum == 24
               && matchExperience.AutoBattle.Training.Preset == AutoBattleTrainingSettings.SteadyPreset
               && matchExperience.AutoBattle.Training.Epochs == 80
               && matchExperience.AutoBattle.Training.MaximumCorrection == 0.75d
               && matchExperience.AutoBattle.SelectedModelId == ""
               && matchExperience.AutoBattle.EvaluationModelId == ""
               && matchExperience.AutoBattle.Simulation.ScenarioId
               == "witch.world-simulation.standard-v2"
               && matchExperience.AutoBattle.Simulation.DifficultyId == "normal"
               && matchExperience.AutoBattle.Simulation.SimulationCount == 8
               && matchExperience.AutoBattle.Simulation.Parallelism == 2,
            "match-experience config reconstructs the sole current auto-battle defaults");
    
        var steadyTraining = AutoBattleTrainingSettings.CreateSteady();
        Assert(steadyTraining.Preset == AutoBattleTrainingSettings.SteadyPreset
               && steadyTraining.Epochs == 80
               && steadyTraining.MinimumPreferencePairs == 15
               && steadyTraining.MaximumCorrection == 0.75d,
            "new auto-battle training settings default to the bounded steady preset");
        steadyTraining.ApplyPreset(AutoBattleTrainingSettings.AdaptivePreset);
        Assert(steadyTraining.Epochs == 180
               && steadyTraining.MinimumPreferencePairs == 30
               && steadyTraining.MaximumCorrection == 2d,
            "auto-battle training presets apply a complete reproducible parameter set");
    
        var trainedModel = JsonConvert.DeserializeObject<AutoBattleSettings>(
            "{\"trainedModelMode\":\"active\"}")!;
        trainedModel.Normalize();
        Assert(trainedModel.TrainedModelMode == "active",
            "current trained-model mode preserves bounded active state");
        trainedModel.TrainedModelMode = "off";
        trainedModel.Normalize();
        Assert(trainedModel.TrainedModelMode == "off",
            "explicitly disabling the trained model survives normalization");
        trainedModel.Simulation.SimulationCount = 500000;
        trainedModel.Simulation.Parallelism = 99;
        trainedModel.Simulation.MinimumAuthoritativeCoverage = double.NaN;
        trainedModel.NetworkDeathRiskWeight = double.NaN;
        trainedModel.SemanticCoverageRiskWeight = 2d;
        trainedModel.Normalize();
        Assert(trainedModel.Simulation.SimulationCount == 100000
               && trainedModel.Simulation.Parallelism == 16
               && trainedModel.Simulation.MinimumAuthoritativeCoverage == 1d
               && trainedModel.NetworkDeathRiskWeight == 1d
               && trainedModel.SemanticCoverageRiskWeight == 1d,
            "headless simulation settings clamp workload and release-gate thresholds");
    
        var legacyFeast = JsonConvert.DeserializeObject<FeastSettings>(
            "{\"roles\":{\"role-a\":{\"selectedCgId\":\"Terrias:feast-a\"}}}")!;
        legacyFeast.Normalize();
        var migratedRole = legacyFeast.Roles["role-a"];
        Assert(migratedRole.CandidateSelectionConfigured
               && migratedRole.EnabledCgIds.SequenceEqual(new[] { "Terrias:feast-a" })
               && migratedRole.SelectionMode == "priority",
            "legacy single Feast selection migrates to the enabled candidate list");
        Assert(migratedRole.MigrateLegacyCandidateSelection(new[] { "Terrias:feast-a", "ContentB:feast-b" })
               && migratedRole.ResourceOverrides["Terrias:feast-a"]
               && !migratedRole.ResourceOverrides["ContentB:feast-b"],
            "legacy Feast whitelist migrates once into sparse overrides for the candidates seen during migration");
        var unconfiguredRole = new FeastRoleSettings();
        unconfiguredRole.SetCandidateEnabled("ContentB:feast-b", false, new[] { "ContentA:feast-a", "ContentB:feast-b" });
        Assert(!unconfiguredRole.CandidateSelectionConfigured
               && !unconfiguredRole.ResourceOverrides["ContentB:feast-b"]
               && unconfiguredRole.IsCandidateEnabled("ContentA:feast-a")
               && unconfiguredRole.IsCandidateEnabled("NewContent:feast-new"),
            "Feast uses sparse overrides so newly scanned resources remain enabled after manual configuration");
        var legacyManualRole = JsonConvert.DeserializeObject<FeastRoleSettings>(
            "{\"roleId\":\"role-a\",\"localCustomized\":true,\"localResource\":\"CG/legacy.png\"}")!;
        legacyManualRole.Normalize("role-a", FeastSettings.CreateDefaultPresentation());
        Assert(legacyManualRole.ManualResources.Count == 1
               && !legacyManualRole.LocalCustomized
               && string.IsNullOrWhiteSpace(legacyManualRole.LocalResource),
            "legacy Feast manual files migrate once and cannot reappear after the manual candidate is removed");
    
        var skin = JsonConvert.DeserializeObject<AuraToolsSkinSettings>(
            "{\"schemaVersion\":0,\"autoInstallBundledSkins\":false}")!;
        skin.Normalize();
        Assert(skin.SchemaVersion == 3 && skin.AutoInstallBundledSkins,
            "skin config keeps its always-on bundled installation policy after the file split");
        skin.SetCandidateEnabled("ContentB:summer", false, new[] { "ContentA:summer", "ContentB:summer" });
        Assert(!skin.CandidateSelectionConfigured
               && skin.SelectionSchemaVersion == 3
               && skin.IsCandidateEnabled("ContentA:summer")
               && !skin.IsCandidateEnabled("ContentB:summer")
               && skin.IsCandidateEnabled("NewContent:summer"),
            "skin ManualSelection gating uses sparse overrides and admits newly scanned candidates by default");
        var qualifiedSkin = new SkinDefinition
        {
            OwnerModId = "ContentA",
            TargetCareerId = "role-a",
            SkinId = "summer"
        };
        Assert(qualifiedSkin.QualifiedSkinId == "ContentA:role-a:summer"
               && qualifiedSkin.SemanticKey == "role-a::summer"
               && SkinDefinition.Qualify("ContentA", "role-b", "summer") != qualifiedSkin.QualifiedSkinId,
            "skin hard identity includes owner, canonical role, and local skin id while semantic grouping omits owner");
    }
    
    public static void TestCardRefreshSettingsAndPoolPolicy()
    {
        var settings = new AuraToolsMatchExperienceSettings
        {
            SchemaVersion = 1,
            CardRefresh = null!
        };
        settings.Normalize();
        Assert(settings.SchemaVersion == 26, "match-experience settings migrate to the game-bound auto-battle schema");
        Assert(settings.CardRefresh != null && !settings.CardRefresh.Enabled,
            "card refresh is restored with a disabled default during normalization");
        var removedFoundationConfig =
            JsonConvert.DeserializeObject<AuraToolsMatchExperienceSettings>(
                "{\"schemaVersion\":25,\"autoBattle\":{\"foundationTraining\":{\"parallelismProfile\":\"auto\",\"iterations\":8}}}")!;
        removedFoundationConfig.Normalize();
        Assert(removedFoundationConfig.SchemaVersion == 26
               && removedFoundationConfig.AutoBattle.Training.Preset
                  == AutoBattleTrainingSettings.SteadyPreset,
            "removed in-game foundation-training settings are ignored by the current config schema");
        var candidates = new[] { "a", "b", "c", "d", "e", "f" };
        var alternatives = CardRefreshPoolPolicy.PreferDifferentChoices(
            candidates,
            new[] { "a", "b", "c" },
            3,
            id => id);
        Assert(alternatives.SequenceEqual(new[] { "d", "e", "f" }),
            "refresh excludes the visible trio when a full alternative trio exists");
    
        var fallback = CardRefreshPoolPolicy.PreferDifferentChoices(
            candidates.Take(4),
            new[] { "a", "b", "c" },
            3,
            id => id);
        Assert(fallback.SequenceEqual(candidates.Take(4)),
            "refresh falls back to the full eligible pool when alternatives are insufficient");
    }
    
    public static void TestLoggingSettingsNormalization()
    {
        var defaults = new AuraToolsLoggingSettings();
        defaults.Normalize();
        Assert(defaults.SchemaVersion == 4, "logging settings use the opt-in performance-diagnostics schema");
        Assert(!defaults.PerformanceDiagnostics, "performance diagnostics default to disabled");
        Assert(defaults.MinimumLevel == LoggingLevelNames.Info, "logging defaults keep AuraTools lifecycle logs visible");
        Assert(!defaults.MirrorUnityLog && !defaults.MirrorCommandsLog, "logging defaults do not mirror high-volume logs");
        Assert(defaults.EnabledSources.SequenceEqual(new[] { "AuraTools" }), "logging defaults to the AuraTools source only");
        Assert(!defaults.UnityLogTypes.Contains("Log"), "logging defaults do not mirror Unity info logs");
        Assert(defaults.StackTraceMode == LoggingStackTraceModes.ErrorsOnly, "logging defaults keep stack traces to errors");
        Assert(defaults.MaxQueueLength == 1024, "logging default queue is bounded");
    
        var legacy = new AuraToolsLoggingSettings
        {
            SchemaVersion = 1,
            MinimumLevel = LoggingLevelNames.Debug,
            MirrorUnityLog = true,
            MirrorCommandsLog = true,
            EnabledSources = new List<string> { "AuraTools", "Unity", "Command" },
            UnityLogTypes = new List<string> { "Log", "Warning", "Error", "Exception", "Assert" },
            StackTraceMode = LoggingStackTraceModes.All,
            MaxQueueLength = 4096
        };
        legacy.Normalize();
        Assert(legacy.SchemaVersion == 4, "legacy logging settings migrate schema");
        Assert(legacy.MinimumLevel == LoggingLevelNames.Info
               && !legacy.MirrorUnityLog
               && !legacy.MirrorCommandsLog
               && legacy.EnabledSources.SequenceEqual(new[] { "AuraTools" })
               && !legacy.UnityLogTypes.Contains("Log")
               && legacy.StackTraceMode == LoggingStackTraceModes.ErrorsOnly
               && legacy.MaxQueueLength == 1024,
            "legacy high-volume logging defaults migrate to low-overhead AuraTools values");
    
        var legacyInfoMirror = new AuraToolsLoggingSettings
        {
            SchemaVersion = 1,
            MinimumLevel = LoggingLevelNames.Info,
            MirrorUnityLog = true,
            MirrorCommandsLog = true
        };
        legacyInfoMirror.Normalize();
        Assert(legacyInfoMirror.SchemaVersion == 4
               && legacyInfoMirror.MinimumLevel == LoggingLevelNames.Info
               && !legacyInfoMirror.MirrorUnityLog
               && !legacyInfoMirror.MirrorCommandsLog
               && legacyInfoMirror.EnabledSources.SequenceEqual(new[] { "AuraTools" }),
            "schema-v1 Info mirror defaults migrate away from Unity and command mirrors");
    
        var warningOnly = new AuraToolsLoggingSettings
        {
            SchemaVersion = 2,
            MinimumLevel = LoggingLevelNames.Warning,
            MirrorUnityLog = false,
            MirrorCommandsLog = false,
            EnabledSources = new List<string> { "AuraTools" },
            UnityLogTypes = new List<string> { "Warning", "Error", "Exception", "Assert" },
            StackTraceMode = LoggingStackTraceModes.ErrorsOnly,
            MaxQueueLength = 1024
        };
        warningOnly.Normalize();
        Assert(warningOnly.SchemaVersion == 4
               && warningOnly.MinimumLevel == LoggingLevelNames.Info
               && !warningOnly.MirrorUnityLog
               && !warningOnly.MirrorCommandsLog,
            "schema-v2 warning-only defaults migrate so host logs are not empty");
    
        var custom = new AuraToolsLoggingSettings
        {
            SchemaVersion = 4,
            PerformanceDiagnostics = true,
            MinimumLevel = LoggingLevelNames.Debug,
            MirrorUnityLog = true,
            MirrorCommandsLog = false,
            EnabledSources = new List<string> { "AuraTools", "Unity" },
            UnityLogTypes = new List<string> { "Warning", "Error" },
            StackTraceMode = LoggingStackTraceModes.All,
            MaxQueueLength = 2048
        };
        custom.Normalize();
        Assert(custom.PerformanceDiagnostics
               && custom.MinimumLevel == LoggingLevelNames.Debug
               && custom.MirrorUnityLog
               && !custom.MirrorCommandsLog
               && custom.EnabledSources.SequenceEqual(new[] { "AuraTools", "Unity" })
               && custom.StackTraceMode == LoggingStackTraceModes.All
               && custom.MaxQueueLength == 2048,
            "schema-v4 custom logging and diagnostics choices are preserved");
    }
    
    public static void TestDamageSettlementCgSettingsAndLayout()
    {
        var settings = new DamageSettlementCgSettings
        {
            BackgroundResource = "",
            BaseWidth = 0,
            BaseHeight = -1,
            SlotSize = 0,
            FadeIn = -1f,
            Hold = 100f,
            FadeOut = 99f
        };
        settings.Normalize();
        Assert(settings.BackgroundResource == "Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png",
            "settlement CG background falls back to bundled resource");
        Assert(settings.BaseWidth == 1 && settings.BaseHeight == 1 && settings.SlotSize == 1,
            "settlement CG dimensions are clamped positive");
        Assert(settings.FadeIn == 0f && Math.Abs(settings.Hold - 30f) < 0.001f && Math.Abs(settings.FadeOut - 5f) < 0.001f,
            "settlement CG timing is clamped");
    
        settings = new DamageSettlementCgSettings();
        settings.Normalize();
        var layout = DamageSettlementCgLayout.Calculate(1920f, 1080f, settings);
        Assert(Math.Abs(layout.Scale - 1.2f) < 0.001f, "settlement CG uses cover scale at 16:9");
        var first = layout.SlotForRank(1)!.Rect;
        Assert(Math.Abs(first.X - 780f) < 0.001f
               && Math.Abs(first.Y - 336f) < 0.001f
               && Math.Abs(first.Width - 216f) < 0.001f,
            "rank one slot scales from 1600x900 coordinates");
    
        var wide = DamageSettlementCgLayout.Calculate(2560f, 1080f, settings);
        Assert(Math.Abs(wide.Scale - 1.6f) < 0.001f
               && Math.Abs(wide.Background.Y + 180f) < 0.001f,
            "settlement CG cover crops vertically on ultrawide viewports");
    }
    
    public static void TestDamageSettlementCgPayloadOrdering()
    {
        var record = new OutOfRunDamageHistoryRecord
        {
            AdventureId = "adventure",
            EndedUtc = "now",
            TeamMembers = new List<OutOfRunTeamMemberSnapshot>
            {
                new() { InstanceId = "p4", PlayerId = "p4", RoleId = "role4", RoleDisplayName = "D", TotalDamage = 10, Dps = 10 },
                new() { InstanceId = "p1", PlayerId = "p1", RoleId = "role1", RoleDisplayName = "A", TotalDamage = 100, Dps = 20 },
                new() { InstanceId = "p3", PlayerId = "p3", RoleId = "role3", RoleDisplayName = "C", TotalDamage = 50, Dps = 30 },
                new() { InstanceId = "p2", PlayerId = "p2", RoleId = "role2", RoleDisplayName = "B", TotalDamage = 100, Dps = 15 },
                new() { InstanceId = "p5", PlayerId = "p5", RoleId = "role5", RoleDisplayName = "E", TotalDamage = 1, Dps = 1 },
                new() { InstanceId = "e0", PlayerId = "e0", RoleDisplayName = "Enemy", TotalDamage = 999, Dps = 999 },
                new() { InstanceId = "p1-alias", PlayerId = "p1", RoleId = "role1", RoleDisplayName = "A", TotalDamage = 90, Dps = 18 }
            }
        };
    
        var payload = DamageSettlementCgBuilder.Build(record, new DamageSettlementCgSettings());
        Assert(payload.Entries.Count == 4, "settlement CG payload keeps the four display slots");
        Assert(payload.Entries[0].InstanceId == "p1"
               && payload.Entries[1].InstanceId == "p2"
               && payload.Entries[2].InstanceId == "p3"
               && payload.Entries[3].InstanceId == "p4",
            "settlement CG payload orders by total DPS damage with deterministic tie breakers");
        Assert(payload.Entries.Select(entry => entry.Rank).SequenceEqual(new[] { 1, 2, 3, 4 }),
            "settlement CG payload assigns rank numbers");
        Assert(payload.Entries.All(entry => entry.InstanceId != "e0")
               && payload.Entries.Count(entry => entry.PlayerId == "p1") == 1,
            "settlement CG excludes non-role combatants and deduplicates real players");
    
        payload = DamageSettlementCgBuilder.Build(new OutOfRunDamageHistoryRecord
        {
            TeamMembers = new List<OutOfRunTeamMemberSnapshot>
            {
                new() { InstanceId = "solo", PlayerId = "solo", RoleId = "role1", RoleDisplayName = "A", TotalDamage = 100, Dps = 20 }
            }
        }, new DamageSettlementCgSettings());
        Assert(payload.Entries.Count == 1 && payload.Entries[0].InstanceId == "solo",
            "settlement CG payload does not pad missing display slots with test data");
    }
    
    public static void TestDamageSettlementCgAnimationSpec()
    {
        var native = DamageSettlementCgAnimationSpec.FromJson(
            "{\"FrameCount\":2,\"FrameRate\":8}",
            new[] { "Idle_10", "Idle_00", "Idle_01" });
        Assert(native.OrderedFrameNames.SequenceEqual(new[] { "Idle_00", "Idle_01" })
               && Math.Abs(native.FrameSeconds - 0.125f) < 0.001f,
            "native idle animation config uses frame count and frame rate");
    
        var shared = DamageSettlementCgAnimationSpec.FromJson(
            "{\"AnimationPerFrame\":0.2,\"isLoop\":false,\"Direction\":\"Left\"}",
            new[] { "matte_00002", "matte_00001" });
        Assert(shared.OrderedFrameNames.SequenceEqual(new[] { "matte_00001", "matte_00002" })
               && Math.Abs(shared.FrameSeconds - 0.2f) < 0.001f
               && !shared.Loop
               && shared.Direction == "Left",
            "shared skin idle animation config uses AnimationPerFrame and natural frame order");
    }
    
    public static void TestDamageSettlementCgPreparationPolicy()
    {
        Assert(DamageSettlementCgPreparationPolicy.ShouldWait(0f, hasPendingPreparation: true, isCurrentGeneration: true),
            "settlement CG waits while synchronized role resources are preparing");
        Assert(!DamageSettlementCgPreparationPolicy.ShouldWait(
                DamageSettlementCgPreparationPolicy.MaximumWaitSeconds,
                hasPendingPreparation: true,
                isCurrentGeneration: true),
            "settlement CG preparation wait has a bounded deadline");
        Assert(!DamageSettlementCgPreparationPolicy.ShouldWait(0.1f, hasPendingPreparation: false, isCurrentGeneration: true),
            "settlement CG starts immediately when all role resources are ready");
        Assert(!DamageSettlementCgPreparationPolicy.ShouldWait(0.1f, hasPendingPreparation: true, isCurrentGeneration: false),
            "superseded settlement CG routines stop waiting");
    }
}
