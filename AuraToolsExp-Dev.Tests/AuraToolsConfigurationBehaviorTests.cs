using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraCg.Shared;
using AuraToolsExp.Dll.Features.Cg;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Infrastructure;
using AuraSkin.Shared.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
internal static partial class AuraToolsTestSuite
{
    public static void TestPresentationOwnershipMigrations()
    {
        var cardVisual = new AuraToolsCardVisualSettings();
        cardVisual.Normalize();
        Assert(cardVisual.Themes.Count == 0 && cardVisual.DynamicEffects.Count == 0,
            "card visuals start without a global whitelist; theme presets are seeded only by the runtime");
        var legacyCardVisual = JsonConvert.DeserializeObject<AuraToolsCardVisualSettings>(
            "{\"schemaVersion\":1,\"dynamicEffects\":{\"Terrias:legacy\":{\"enabled\":true,\"effectId\":\"foil-holo\",\"parameters\":{}}}}")!;
        legacyCardVisual.Normalize();
        Assert(legacyCardVisual.SchemaVersion == 2
               && legacyCardVisual.DynamicEffectOverrides.TryGetValue("Terrias:legacy", out var migratedEffect)
               && migratedEffect.EffectId == "foil-holo"
               && !JsonConvert.SerializeObject(legacyCardVisual).Contains("dynamicEffects", StringComparison.Ordinal),
            "card visual schema v1 effects migrate once into explicit v2 overrides");

        var skin = new AuraToolsSkinSettings
        {
            SelectionSchemaVersion = 3,
            ResourceOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Terrias:Terrias_wuna_wuna:Terrias.Terrias_wuna_wuna.summer_cool"] = false,
                ["Terrias:Terrias_columbina_columbina:Terrias.Terrias_columbina_columbina.restore_colors"] = true
            }
        };
        skin.Normalize();
        Assert(skin.SchemaVersion == 4
               && skin.ResourceOverrides.ContainsKey("AuraToolsExp:Terrias_wuna_wuna:AuraToolsExp.Terrias_wuna_wuna.summer_cool")
               && skin.ResourceOverrides.ContainsKey("AuraToolsExp:Terrias_columbina_columbina:AuraToolsExp.Terrias_columbina_columbina.restore_colors")
               && skin.MigrateLegacyCandidateSelection(Array.Empty<string>()),
            "Terrias-owned replacement-skin preferences migrate once to AuraToolsExp ownership");

        var skillCg = new AuraToolsSkillCgSettings
        {
            CardUseCg = new AuraToolsCardUseCgSettings
            {
                RegisteredEntries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AuraToolsExp:terrias.blazing-crown-collapse"] = false
                },
                PresentationOverrides = new Dictionary<string, CardUseCgPresentationOverrideSettings>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AuraToolsExp:terrias.blazing-crown-collapse"] = new()
                    {
                        FlashStrength = 2f,
                        FrameSeconds = 0f
                    }
                }
            },
            Roles = new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["Terrias_wuna_wuna"] = new SkillCgRoleSettings
                {
                    RoleId = "Terrias_wuna_wuna",
                    Rules = new List<SkillCgRuleSettings>
                    {
                        new()
                        {
                            SourceOwnerModId = "AuraToolsExp",
                            SourceCgId = "wuna.white-sun-prayer",
                            CardId = "Terrias_wuna_wuna_*wuna_white_sun_prayer"
                        }
                    }
                }
            }
        };
        skillCg.Normalize();
        Assert(skillCg.SchemaVersion == 10
               && skillCg.CardUseCg.RegisteredEntries.ContainsKey("Terrias:terrias.blazing-crown-collapse")
               && skillCg.CardUseCg.PresentationOverrides.TryGetValue("Terrias:terrias.blazing-crown-collapse", out var cardUseOverride)
               && cardUseOverride.FlashStrength == 1f
               && Math.Abs(cardUseOverride.FrameSeconds!.Value - 0.01f) < 0.001f
               && skillCg.Roles.Count == 0
               && skillCg.RoleEntries.TryGetValue("Terrias:wuna.white-sun-prayer", out var roleOverride)
               && roleOverride.Presentation.Hold.HasValue
               && skillCg.GetRoleSelection(
                   "Terrias_wuna_wuna",
                   AuraToolsRoleCgContextKeys.SkillChannel,
                   "Terrias_wuna_wuna_*wuna_white_sun_prayer") == "Terrias:wuna.white-sun-prayer",
            "role CG migration preserves its per-role skill selection without copying a registered resource");

        var feastRole = new FeastRoleSettings
        {
            RoleId = "Terrias_wuna_wuna",
            ResourceOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["AuraToolsExp:wuna.feast"] = false
            }
        };
        feastRole.Normalize("Terrias_wuna_wuna", FeastSettings.CreateDefaultPresentation());
        Assert(feastRole.ResourceOverrides.ContainsKey("Terrias:wuna.feast"),
            "Terrias Feast CG preferences migrate back to the content resource owner");
    }

    public static void TestAutoBattleTechnicalFallbackState()
    {
        var state = new AuraToolsExp.Dll.Features.AutoBattle
            .AutoBattleTechnicalFallbackState();
        state.ResetBattle(modelAvailable: true);
        Assert(!state.ShouldUseEmergencyBaseline
               && state.FallbackDecisionCount == 0,
            "a healthy loaded model starts without emergency fallback");

        state.ReportFailure("inference-timeout", "hard watchdog");
        Assert(state.ShouldUseEmergencyBaseline
               && state.TryConsumeEmergencyFallback()
               && !state.ShouldUseEmergencyBaseline
               && state.FallbackDecisionCount == 1,
            "a transient technical failure schedules exactly one emergency decision");

        state.ReportFailure("invalid-output", "candidate missing");
        state.TryConsumeEmergencyFallback();
        state.ReportFailure("no-progress-loop", "same state and action");
        Assert(state.IsolatedForBattle
               && state.ShouldUseEmergencyBaseline,
            "three consecutive technical failures isolate the model for the battle");

        state.ResetBattle(modelAvailable: false, "weights missing");
        Assert(state.IsolatedForBattle
               && state.LastReason.Contains("model-load-failed"),
            "model load failure starts the battle in availability fallback");
        state.ModelRecovered();
        Assert(!state.ShouldUseEmergencyBaseline,
            "a successfully reloaded model clears availability fallback");
    }

    public static void TestDamageMeterSettingsNormalization()
    {
        var settings = new DamageMeterSettings
        {
            DisplayMode = "invalid",
            DisplayScope = "invalid",
            TeamFilter = "invalid",
            UiRefreshIntervalMs = 0,
            SubmitBatchIntervalMs = 0,
            MaxEventsPerBatch = 0
        };
    
        settings.Normalize();
        Assert(settings.DisplayMode == DamageMeterDisplayModes.Table
               && settings.DisplayScope == DamageMeterDisplayScopes.Fight
               && settings.TeamFilter == DamageMeterTeamFilters.All,
            "DPS view choices fall back to table, current fight, and all teams");
        Assert(settings.UiRefreshIntervalMs == 1000, "DPS UI refresh falls back to the bounded default");
        Assert(settings.SubmitBatchIntervalMs == 250, "DPS network submit batching falls back to the bounded default");
        Assert(settings.MaxEventsPerBatch == 24, "DPS network submit batch size falls back to the bounded default");
        Assert(!JsonConvert.SerializeObject(settings).Contains("settlementCg", StringComparison.Ordinal),
            "damage settings no longer serialize an independent settlement CG subsystem");
    
        var legacy = JsonConvert.DeserializeObject<DamageMeterSettings>("{\"friendlyOnly\":true}")!;
        legacy.Normalize();
        Assert(legacy.TeamFilter == DamageMeterTeamFilters.Friendly,
            "legacy friendly-only setting migrates to the friendly team filter");
        var serializedDamageMeter = JsonConvert.SerializeObject(legacy);
        Assert(!serializedDamageMeter.Contains("friendlyOnly", StringComparison.Ordinal)
               && !serializedDamageMeter.Contains("hotkey", StringComparison.Ordinal)
               && serializedDamageMeter.Contains("teamFilter", StringComparison.Ordinal),
            "retired DPS configuration fields are deserialize-only or removed from new files");
        settings.DisplayMode = "bars";
        settings.DisplayScope = "adventure";
        settings.TeamFilter = "enemy";
        settings.UiRefreshIntervalMs = 20;
        settings.SubmitBatchIntervalMs = 5000;
        settings.MaxEventsPerBatch = 1000;
        settings.Normalize();
        Assert(settings.DisplayMode == DamageMeterDisplayModes.Bars
               && settings.DisplayScope == DamageMeterDisplayScopes.Adventure
               && settings.TeamFilter == DamageMeterTeamFilters.Enemy,
            "DPS view choices normalize case without changing their meaning");
        Assert(settings.UiRefreshIntervalMs == 100
               && settings.SubmitBatchIntervalMs == 1000
               && settings.MaxEventsPerBatch == 64,
            "DPS performance knobs are clamped to runtime-safe bounds");
    }

    public static void TestMatchRecordSettingsMigration()
    {
        var defaults = new AuraToolsMatchExperienceSettings();
        defaults.Normalize();
        Assert(!defaults.MatchRecords.Enabled
               && defaults.MatchRecords.Statistics.Enabled
               && !defaults.MatchRecords.Replay.Enabled
               && defaults.MatchRecords.Replay.AutoRecordLimit == 20,
            "match records and replay default off while the DPT child is ready when the module is enabled");

        var legacy = JsonConvert.DeserializeObject<AuraToolsMatchExperienceSettings>(
            "{\"schemaVersion\":29,\"damageMeter\":{\"enabled\":true,\"displayMode\":\"Bars\",\"teamFilter\":\"Enemy\"}}")!;
        legacy.Normalize();
        Assert(legacy.SchemaVersion == 32
               && legacy.MatchRecords.Enabled
               && legacy.MatchRecords.Statistics.Enabled
               && legacy.MatchRecords.Statistics.DisplayMode == DamageMeterDisplayModes.Bars
               && legacy.MatchRecords.Statistics.TeamFilter == DamageMeterTeamFilters.Enemy
               && !legacy.MatchRecords.Replay.Enabled,
            "legacy damageMeter settings migrate into the match-record statistics child without enabling replay");

        var current = JsonConvert.DeserializeObject<AuraToolsMatchExperienceSettings>(
            "{\"matchRecords\":{\"enabled\":true,\"statistics\":{\"enabled\":false},\"replay\":{\"enabled\":true,\"autoRecordLimit\":9999,\"presentationMode\":\"Showcase\"}},\"damageMeter\":{\"enabled\":true}}")!;
        current.Normalize();
        var json = JsonConvert.SerializeObject(current);
        Assert(current.MatchRecords.Enabled
               && !current.MatchRecords.Statistics.Enabled
               && current.MatchRecords.Replay.Enabled
               && current.MatchRecords.Replay.AutoRecordLimit == MatchReplaySettings.MaximumAutoRecordLimit,
            "the new matchRecords section wins over stale settings and fixes replay presentation to Standard");
        Assert(json.Contains("\"matchRecords\"", StringComparison.Ordinal)
               && !json.Contains("\"damageMeter\"", StringComparison.Ordinal),
            "new configuration files serialize only the matchRecords ownership model");
    }
    
    public static void TestConfigModelSerializationCompatibility()
    {
        var root = JsonConvert.DeserializeObject<AuraToolsRootConfig>(
            "{\"schemaVersion\":0,\"audio\":null,\"matchExperience\":null,\"skillCg\":null,\"skin\":null,\"logging\":null}")!;
        root.Normalize();
        Assert(root.SchemaVersion == 2
               && root.Audio.ConfigFile == "AudioSettings.json"
               && root.MatchExperience.ConfigFile == "MatchExperienceSettings.json"
               && root.PixelEmoji.ConfigFile == "PixelEmojiSettings.json"
               && root.SkillCg.ConfigFile == "SkillCgSettings.json"
               && root.Skin.ConfigFile == "SkinSettings.json"
               && root.CardVisual.ConfigFile == "CardVisualSettings.json"
               && root.Logging.ConfigFile == "LoggingSettings.json",
            "root config preserves module-file defaults after JSON deserialization");
    
        var rootJson = JsonConvert.SerializeObject(root);
        var restoredRoot = JsonConvert.DeserializeObject<AuraToolsRootConfig>(rootJson)!;
        Assert(restoredRoot.Audio.Enabled
               && restoredRoot.Logging.Enabled
               && rootJson.Contains("\"matchExperience\"")
               && rootJson.Contains("\"pixelEmoji\"")
               && rootJson.Contains("\"configFile\""),
            "root config keeps its established JSON property contract across a round trip");

        var presetLibrary = new PresetLibrarySettings { SchemaVersion = 0, MaximumPresets = 9999 };
        var archive = new AdventureArchiveSettings { SchemaVersion = 0, MaximumAdventures = 1 };
        var health = new ModHealthSettings { SchemaVersion = 0 };
        var lobby = new LobbyStatusSettings { SchemaVersion = 0 };
        presetLibrary.Normalize();
        archive.Normalize();
        health.Normalize();
        lobby.Normalize();
        Assert(presetLibrary.SchemaVersion == 1
               && presetLibrary.MaximumPresets == 256
               && archive.SchemaVersion == 2
               && archive.MaximumAdventures == 10
               && health.SchemaVersion == 1
               && health.ScanOnOpen
               && lobby.SchemaVersion == 1
               && lobby.ShowLocalHealthSummary,
            "foundation module settings normalize bounded retention and safe default behaviors");
        var migratedArchive = JsonConvert.DeserializeObject<AdventureArchiveSettings>(
            "{\"schemaVersion\":1,\"enabled\":true,\"maximumAdventures\":80,\"captureSnapshots\":false}")!;
        migratedArchive.Normalize();
        var migratedArchiveJson = JsonConvert.SerializeObject(migratedArchive);
        Assert(!migratedArchiveJson.Contains("captureSnapshots", StringComparison.Ordinal)
               && migratedArchiveJson.Contains("\"schemaVersion\":2", StringComparison.Ordinal),
            "adventure history retires the optional snapshot path and migrates to the required v2 contract");
    
        var audio = JsonConvert.DeserializeObject<AuraToolsAudioSettings>(
            "{\"schemaVersion\":1,\"audioSystemVersion\":\" \",\"battleBgm\":{\"common\":{\"relativePath\":\"Audio/Common/battle_bgm.mp3\"}},\"cardUse\":null}")!;
        audio.Normalize();
        Assert(audio.SchemaVersion == 7
               && audio.BattleBgm.Common.RelativePath == "Audio/Common/battle_bgm.mp3"
               && audio.CardUse.Common.RelativePath == "Audio/Global/all/CardUse/AuraToolsExp/default-card-use/content.mp3"
               && audio.Voice.Enabled
               && audio.Voice.Bindings.Count == 0,
            "audio config preserves user resource paths while recovering missing domains");
        var retiredThresholdBinding = JsonConvert.DeserializeObject<AuraToolsVoiceBindingSettings>(
            "{\"providerId\":\"Terrias:Terrias.Wuna.LowHealth\",\"hpRatioThreshold\":0.75}")!;
        retiredThresholdBinding.Normalize("Terrias:Terrias.Wuna.LowHealth");
        Assert(!JsonConvert.SerializeObject(retiredThresholdBinding).Contains("hpRatioThreshold", StringComparison.Ordinal),
            "voice configuration consumes but never rewrites the retired player-defined low-health threshold");
        var voice = new AuraToolsVoiceSettings
        {
            Bindings = new Dictionary<string, AuraToolsVoiceBindingSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["AuraToolsExp.Terrias.Wuna.BattleWin"] = new()
                {
                    ProviderId = "AuraToolsExp.Terrias.Wuna.BattleWin",
                    ResourcePath = "Shared:Audio/Role/Terrias_wuna_wuna/Voice/AuraToolsExp/wuna.voice-pack/content/wuna_battle_win.wav"
                }
            }
        };
        voice.Normalize();
        Assert(voice.Bindings.TryGetValue("Terrias:Terrias.Wuna.BattleWin", out var migratedVoice)
               && migratedVoice.ProviderId == "Terrias:Terrias.Wuna.BattleWin"
               && migratedVoice.ResourcePath.Contains("/Voice/Terrias/"),
            "voice bindings migrate once from tool-owned ids and paths to Terrias content ownership");
        var legacySkillBinding = new AuraToolsVoiceBindingSettings
        {
            Signal = "SkillVoice",
            Stage = "PresentationCommitted",
            ActionId = "*wuna_white_sun_prayer"
        };
        var wunaSkills = new[]
        {
            new AuraToolsVoiceSkillDescriptor
            {
                Id = "Terrias_wuna_wuna_white_sun_prayer",
                Slot = 1
            },
            new AuraToolsVoiceSkillDescriptor
            {
                Id = "Terrias_wuna_wuna_grave_song",
                Slot = 2
            }
        };
        Assert(AuraToolsVoiceSkillBindingMigration.Migrate(
                   legacySkillBinding,
                   "SkillVoice",
                   "Committed",
                   2,
                   wunaSkills)
               && legacySkillBinding.SkillSlot == 1
               && legacySkillBinding.ActionId == ""
               && legacySkillBinding.Stage == "Committed",
            "skill voice binding migrates legacy card/action id to the configured role skill ordinal");
        var manifestSkillBinding = new AuraToolsVoiceBindingSettings();
        Assert(AuraToolsVoiceSkillBindingMigration.Migrate(
                   manifestSkillBinding,
                   "SkillVoice",
                   "Committed",
                   2,
                   wunaSkills)
               && manifestSkillBinding.SkillSlot == 2,
            "new skill voice binding adopts a manifest ordinal that exists in role configuration");
        var boundedSkillBinding = new AuraToolsVoiceBindingSettings
        {
            SkillSlot = 2,
            ActionId = "retired"
        };
        Assert(AuraToolsVoiceSkillBindingMigration.Migrate(
                   boundedSkillBinding,
                   "SkillVoice",
                   "Committed",
                   2,
                   wunaSkills.Take(1))
               && boundedSkillBinding.SkillSlot == null
               && boundedSkillBinding.ActionId == "",
            "skill voice binding rejects an ordinal beyond the role's configured skill count");
        var deferredSkillBinding = new AuraToolsVoiceBindingSettings
        {
            ActionId = "legacy_skill"
        };
        Assert(!AuraToolsVoiceSkillBindingMigration.Migrate(
                   deferredSkillBinding,
                   "SkillVoice",
                   "Committed",
                   1,
                   Array.Empty<AuraToolsVoiceSkillDescriptor>())
               && deferredSkillBinding.ActionId == "legacy_skill",
            "skill voice migration waits for authoritative role data without using the legacy id at runtime");
        var legacyLowHealthBinding = new AuraToolsVoiceBindingSettings
        {
            Signal = "LowHealth",
            Stage = "ThresholdCrossedDown"
        };
        Assert(AuraToolsVoiceNativeStateBindingMigration.Migrate(
                   legacyLowHealthBinding,
                   "VocalState",
                   "Dying")
               && legacyLowHealthBinding.Signal == "VocalState"
               && legacyLowHealthBinding.Stage == "Observed"
               && legacyLowHealthBinding.ActionId == "Dying",
            "legacy threshold voice bindings migrate once to the native Dying observation");
        Assert(AuraToolsConfigSchemaPolicy.IsNewer(
                   storedEnvelopeVersion: 2,
                   storedValue: new AuraToolsAudioSettings(),
                   supportedValue: new AuraToolsAudioSettings())
               && AuraToolsConfigSchemaPolicy.IsNewer(
                   storedEnvelopeVersion: 1,
                   storedValue: new AuraToolsAudioSettings
                   {
                        SchemaVersion = 8
                   },
                   supportedValue: new AuraToolsAudioSettings())
               && !AuraToolsConfigSchemaPolicy.IsNewer(
                   storedEnvelopeVersion: 1,
                   storedValue: new AuraToolsAudioSettings
                   {
                       SchemaVersion = 2
                   },
                   supportedValue: new AuraToolsAudioSettings()),
            "config schema policy migrates older values but keeps newer envelopes and values read-only");
    
        var matchExperience = JsonConvert.DeserializeObject<AuraToolsMatchExperienceSettings>(
            "{\"schemaVersion\":1,\"starterDeck\":{\"globalProfile\":{\"cardIds\":[\"card_1\"],\"deckSize\":11},\"roles\":{\"role_a\":{\"roleId\":\"role_a\",\"cardIds\":[\"card_2\"]}}},\"safeBox\":null,\"modSync\":null,\"feast\":null,\"damageMeter\":null,\"cardRefresh\":null,\"autoBattle\":null}")!;
        matchExperience.Normalize();
        Assert(matchExperience.SchemaVersion == 32
               && matchExperience.StarterDeck.SchemaVersion == StarterDeckSettings.CurrentSchemaVersion
               && matchExperience.StarterDeck.GlobalProfile.CardIds.SequenceEqual(new[] { "card_1" })
               && matchExperience.StarterDeck.GlobalProfile.RelicIds.Count == 0
               && matchExperience.StarterDeck.Roles.TryGetValue("role_a", out var migratedStarterRole)
               && migratedStarterRole != null
               && !migratedStarterRole.InheritCards
               && migratedStarterRole.InheritRelics
               && migratedStarterRole.CardIds.SequenceEqual(new[] { "card_2" })
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
               && matchExperience.AutoBattle.ModelRiskAcknowledgements.Count
                  == 0
               && matchExperience.AutoBattle.EvaluationModelId == ""
               && matchExperience.AutoBattle.Simulation.ScenarioId
               == "witch.world-simulation.standard-v2"
               && matchExperience.AutoBattle.Simulation.DifficultyId == "normal"
               && matchExperience.AutoBattle.Simulation.SimulationCount == 8
               && matchExperience.AutoBattle.Simulation.Parallelism == 2,
            "match-experience config reconstructs the sole current auto-battle defaults");

        var customStart = new StarterDeckSettings
        {
            GlobalProfile = new StarterDeckLocalProfileSettings
            {
                CardIds = Enumerable.Range(0, 20).Select(index => "card_" + index).ToList(),
                RelicIds = new List<string> { "relic_1", "relic_1", "relic_2", "relic_3", "relic_4", "relic_5", "relic_6", "relic_7" }
            },
            Roles = new Dictionary<string, StarterDeckLocalProfileSettings>
            {
                ["role_a"] = new()
                {
                    RoleId = "role_a",
                    InheritCards = false,
                    InheritRelics = false,
                    CardIds = new List<string>(),
                    RelicIds = new List<string>()
                }
            }
        };
        customStart.Normalize();
        Assert(customStart.GlobalProfile.CardIds.Count == StarterDeckSettings.MaximumCardCount
               && customStart.GlobalProfile.RelicIds.SequenceEqual(new[] { "relic_1", "relic_2", "relic_3", "relic_4", "relic_5", "relic_6" })
               && customStart.Roles["role_a"].CardIds.Count == 0
               && !customStart.Roles["role_a"].InheritCards
               && customStart.Roles["role_a"].RelicIds.Count == 0
               && !customStart.Roles["role_a"].InheritRelics,
            "custom-start settings clamp only upper bounds while preserving explicit empty role overrides");
        customStart.Mode = StarterDeckModes.RoleSpecific;
        var effectiveCustomStart = customStart.ResolveEffective("role_a");
        Assert(effectiveCustomStart.CardIds.Count == 0
               && effectiveCustomStart.RelicIds.Count == 0
               && effectiveCustomStart.CardSource == "role"
               && effectiveCustomStart.RelicSource == "role",
            "explicit empty role lists mean game-default cards and an exact empty relic replacement");
        customStart.Roles["role_a"].InheritCards = true;
        customStart.Roles["role_a"].InheritRelics = true;
        effectiveCustomStart = customStart.ResolveEffective("role_a");
        Assert(effectiveCustomStart.CardIds.Count == StarterDeckSettings.MaximumCardCount
               && effectiveCustomStart.RelicIds.Count == StarterDeckSettings.MaximumRelicCount
               && effectiveCustomStart.CardSource == "global"
               && effectiveCustomStart.RelicSource == "global",
            "role inheritance resolves the two custom-start domains independently from global settings");
    
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
            "{\"trainedModelMode\":\"active\",\"inferenceParallelism\":2,\"experimentalModelAcknowledgement\":\"  sha256:abc  \"}")!;
        trainedModel.Normalize();
        Assert(trainedModel.TrainedModelMode == "trial"
               && trainedModel.ModelRiskAcknowledgements.SequenceEqual(
                   new[] { "sha256:abc" })
               && !JsonConvert.SerializeObject(trainedModel).Contains(
                   "experimentalModelAcknowledgement",
                   StringComparison.Ordinal)
               && !JsonConvert.SerializeObject(trainedModel).Contains(
                   "inferenceParallelism",
                   StringComparison.Ordinal),
            "legacy active mode, acknowledgement and dual-tree input migrate without retaining retired fields");
        trainedModel.TrainedModelMode = "full";
        trainedModel.ModelRiskAcknowledgements.Add("sha256:def");
        trainedModel.Normalize();
        Assert(trainedModel.TrainedModelMode == "full"
               && trainedModel.ModelRiskAcknowledgements.SequenceEqual(
                   new[] { "sha256:abc", "sha256:def" }),
            "full model application and per-model risk confirmations survive normalization");
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

        var replacedPresets = JsonConvert.DeserializeObject<AutoBattleGameParameterSettings>(
            "{\"selectedPresetId\":\"standard\",\"presets\":[{\"id\":\"standard\",\"displayName\":\"标准预设\"}]}")!;
        replacedPresets.Normalize();
        Assert(replacedPresets.Presets.Count == 1,
            "deserializing game presets replaces the initialized default list instead of appending to it");

        var standard = AutoBattleGameParameterPreset.CreateDefault();
        var resolvedStandard = standard.CloneAs("standard-2", standard.DisplayName);
        resolvedStandard.ResolvedRoleMaximumHp = 100;
        var legacyPresetChain = new AutoBattleGameParameterSettings
        {
            SelectedPresetId = "standard-2-2",
            Presets = new List<AutoBattleGameParameterPreset>
            {
                standard,
                resolvedStandard,
                resolvedStandard.CloneAs("standard-2-2", standard.DisplayName),
                standard.CloneAs("standard-copy", "我的标准预设")
            }
        };
        legacyPresetChain.Normalize();
        Assert(legacyPresetChain.Presets.Count == 2
               && legacyPresetChain.SelectedPresetId == "standard"
               && legacyPresetChain.ActivePreset.ResolvedRoleMaximumHp == 100
               && legacyPresetChain.Presets.Any(item => item.Id == "standard-copy"),
            "legacy generated duplicate chains collapse into the richest resolved state while an intentionally renamed clone and selection remain stable");
        for (var pass = 0; pass < 5; pass++)
        {
            legacyPresetChain = JsonConvert.DeserializeObject<AutoBattleGameParameterSettings>(
                JsonConvert.SerializeObject(legacyPresetChain))!;
            legacyPresetChain.Normalize();
        }
        Assert(legacyPresetChain.Presets.Count == 2
               && legacyPresetChain.SelectedPresetId == "standard",
            "preset migration is idempotent across repeated config save and reload cycles");
    
        var legacyFeast = JsonConvert.DeserializeObject<FeastSettings>(
            "{\"enabled\":true,\"playCg\":true,\"roles\":{\"role-a\":{\"selectedCgId\":\"Terrias:feast-a\"}}}")!;
        legacyFeast.Normalize();
        var migratedRole = legacyFeast.Cg.Roles["role-a"];
        Assert(legacyFeast.SchemaVersion == FeastSettings.CurrentSchemaVersion
               && legacyFeast.Cg.SchemaVersion == FeastCgSettings.CurrentSchemaVersion
               && legacyFeast.Cg.Enabled
               && legacyFeast.IsCgEffective
               && migratedRole.CandidateSelectionConfigured
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
        legacyFeast.Enabled = false;
        Assert(legacyFeast.Cg.Enabled && !legacyFeast.IsCgEffective,
            "disabling one-click Feast pauses Feast CG without changing the stored CG preference");
        legacyFeast.Enabled = true;
        Assert(legacyFeast.IsCgEffective,
            "re-enabling one-click Feast restores the preserved Feast CG preference");
        legacyFeast.Cg.Enabled = false;
        legacyFeast.Normalize();
        Assert(!legacyFeast.Cg.Enabled && !legacyFeast.IsCgEffective && legacyFeast.Enabled,
            "Feast automation remains enabled when only Feast CG is disabled");
        var splitFeastJson = JsonConvert.SerializeObject(legacyFeast);
        var splitFeastObject = JObject.Parse(splitFeastJson);
        Assert(splitFeastObject["cg"] != null
               && splitFeastObject["playCg"] == null
               && splitFeastObject["roles"] == null,
            "Feast serialization moves presentation configuration under the Feast CG child without legacy flat fields");
    
        var skin = JsonConvert.DeserializeObject<AuraToolsSkinSettings>(
            "{\"schemaVersion\":0,\"autoInstallBundledSkins\":false}")!;
        skin.Normalize();
        Assert(skin.SchemaVersion == 4 && skin.AutoInstallBundledSkins,
            "skin config keeps its always-on bundled installation policy after the file split");
        skin.SetCandidateEnabled("ContentB:summer", false, new[] { "ContentA:summer", "ContentB:summer" });
        Assert(!skin.CandidateSelectionConfigured
               && skin.SelectionSchemaVersion == 4
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
        Assert(settings.SchemaVersion == 32, "match-experience settings migrate to the current Feast-CG split schema");
        Assert(settings.CardRefresh != null && !settings.CardRefresh.Enabled,
            "card refresh is restored with a disabled default during normalization");
        var removedFoundationConfig =
            JsonConvert.DeserializeObject<AuraToolsMatchExperienceSettings>(
                "{\"schemaVersion\":25,\"autoBattle\":{\"foundationTraining\":{\"parallelismProfile\":\"auto\",\"iterations\":8}}}")!;
        removedFoundationConfig.Normalize();
        Assert(removedFoundationConfig.SchemaVersion == 32
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
        Assert(defaults.SchemaVersion == 5, "logging settings use the single-source persistence schema");
        Assert(!defaults.PerformanceDiagnostics, "performance diagnostics default to disabled");
        AuraToolsConfigService.Logging = defaults;
        AuraToolsPerformanceSettings.PublishSharedOverride();
        Assert(!AuraShared.Core.AuraFeatureSwitchRuntime.IsEnabled("AuraShared", "Diagnostics.Performance"),
            "disabled logging diagnostics publish a disabled shared performance override");
        Assert(defaults.MinimumLevel == LoggingLevelNames.Info, "logging defaults keep AuraTools lifecycle logs visible");
        Assert(!defaults.MirrorUnityLog && !defaults.MirrorCommandsLog, "logging defaults do not mirror high-volume logs");
        Assert(!JsonConvert.SerializeObject(defaults).Contains(
                "enabledSources",
                StringComparison.Ordinal),
            "logging no longer serializes the retired duplicate source gate");
        Assert(!defaults.UnityLogTypes.Contains("Log"), "logging defaults do not mirror Unity info logs");
        Assert(defaults.StackTraceMode == LoggingStackTraceModes.ErrorsOnly, "logging defaults keep stack traces to errors");
        Assert(defaults.MaxQueueLength == 1024, "logging default queue is bounded");

        var mirrorDeduplicator = new AuraToolsMirrorDeduplicator();
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        Assert(mirrorDeduplicator.Allow(
                   "Command",
                   "Debug",
                   "AuraTools",
                   "replay initialized",
                   now)
               && !mirrorDeduplicator.Allow(
                   "Unity",
                   "Info",
                   "Log",
                   "[AuraTools] [DEBUG] replay initialized",
                   now.AddMilliseconds(20))
               && mirrorDeduplicator.Allow(
                   "Command",
                   "Debug",
                   "AuraTools",
                   "replay initialized",
                   now.AddMilliseconds(40)),
            "command and Unity mirrors collapse one cross-source echo without suppressing legitimate same-source repeats");
    
        var legacy = new AuraToolsLoggingSettings
        {
            SchemaVersion = 1,
            MinimumLevel = LoggingLevelNames.Debug,
            MirrorUnityLog = true,
            MirrorCommandsLog = true,
            UnityLogTypes = new List<string> { "Log", "Warning", "Error", "Exception", "Assert" },
            StackTraceMode = LoggingStackTraceModes.All,
            MaxQueueLength = 4096
        };
        legacy.Normalize();
        Assert(legacy.SchemaVersion == 5, "legacy logging settings migrate schema");
        Assert(legacy.MinimumLevel == LoggingLevelNames.Info
               && !legacy.MirrorUnityLog
               && !legacy.MirrorCommandsLog
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
        Assert(legacyInfoMirror.SchemaVersion == 5
               && legacyInfoMirror.MinimumLevel == LoggingLevelNames.Info
               && !legacyInfoMirror.MirrorUnityLog
               && !legacyInfoMirror.MirrorCommandsLog,
            "schema-v1 Info mirror defaults migrate away from Unity and command mirrors");
    
        var warningOnly = new AuraToolsLoggingSettings
        {
            SchemaVersion = 2,
            MinimumLevel = LoggingLevelNames.Warning,
            MirrorUnityLog = false,
            MirrorCommandsLog = false,
            UnityLogTypes = new List<string> { "Warning", "Error", "Exception", "Assert" },
            StackTraceMode = LoggingStackTraceModes.ErrorsOnly,
            MaxQueueLength = 1024
        };
        warningOnly.Normalize();
        Assert(warningOnly.SchemaVersion == 5
               && warningOnly.MinimumLevel == LoggingLevelNames.Info
               && !warningOnly.MirrorUnityLog
               && !warningOnly.MirrorCommandsLog,
            "schema-v2 warning-only defaults migrate so host logs are not empty");
    
        var custom = new AuraToolsLoggingSettings
        {
            SchemaVersion = 5,
            PerformanceDiagnostics = true,
            MinimumLevel = LoggingLevelNames.Debug,
            MirrorUnityLog = true,
            MirrorCommandsLog = false,
            UnityLogTypes = new List<string> { "Warning", "Error" },
            StackTraceMode = LoggingStackTraceModes.All,
            MaxQueueLength = 2048
        };
        custom.Normalize();
        AuraToolsConfigService.Logging = custom;
        AuraToolsPerformanceSettings.PublishSharedOverride();
        Assert(AuraShared.Core.AuraFeatureSwitchRuntime.IsEnabled("AuraShared", "Diagnostics.Performance"),
            "the logging performance toggle publishes its effective shared diagnostics state");
        Assert(custom.PerformanceDiagnostics
               && custom.MinimumLevel == LoggingLevelNames.Debug
               && custom.MirrorUnityLog
               && !custom.MirrorCommandsLog
               && custom.StackTraceMode == LoggingStackTraceModes.All
               && custom.MaxQueueLength == 2048,
            "schema-v5 custom logging and diagnostics choices are preserved");
        var loggingRoundTrip = JsonConvert.DeserializeObject<
            AuraToolsLoggingSettings>(JsonConvert.SerializeObject(custom))!;
        loggingRoundTrip.Normalize();
        Assert(loggingRoundTrip.PerformanceDiagnostics
               && loggingRoundTrip.MirrorUnityLog
               && !loggingRoundTrip.MirrorCommandsLog
               && loggingRoundTrip.UnityLogTypes.SequenceEqual(
                   new[] { "Warning", "Error" })
               && loggingRoundTrip.MaxQueueLength == 2048,
            "logging choices survive a close/reopen serialization round trip: "
            + JsonConvert.SerializeObject(loggingRoundTrip));

        var migratedDisabledSource = JsonConvert.DeserializeObject<
            AuraToolsLoggingSettings>(
            "{\"schemaVersion\":4,\"mirrorUnityLog\":true,\"mirrorCommandsLog\":true,\"enabledSources\":[\"AuraTools\",\"Command\"]}")!;
        migratedDisabledSource.Normalize();
        Assert(!migratedDisabledSource.MirrorUnityLog
               && migratedDisabledSource.MirrorCommandsLog,
            "schema-v4 duplicate source switches migrate their previously effective mirror state exactly once");

        var emptyUnityTypes = new AuraToolsLoggingSettings
        {
            SchemaVersion = 5,
            MirrorUnityLog = true,
            UnityLogTypes = new List<string>()
        };
        emptyUnityTypes.Normalize();
        Assert(emptyUnityTypes.UnityLogTypes.Count == 0,
            "an intentionally empty Unity type selection remains empty instead of silently restoring defaults");
    }
    
    public static void TestUnifiedEventCgSettingsAndLegacyMigration()
    {
        var settings = JsonConvert.DeserializeObject<AuraToolsEventCgSettings>(
            "{\"backgroundResource\":\"\",\"baseWidth\":0,\"baseHeight\":-1,\"fadeIn\":-1,\"hold\":100,\"fadeOut\":99,\"specialBattleIds\":[\" boss-a \",\"BOSS-A\",\"boss-b\"]}")!;
        settings.Normalize();
        Assert(settings.SchemaVersion == 3
               && settings.Scenes.Count == AuraToolsEventCgSceneIds.All.Length,
            "event CG migration creates one independent profile for every player-facing scene");
        var opening = settings.GetScene(AuraToolsEventCgSceneIds.BattleOpening);
        Assert(opening.EffectiveBackgroundResource == "",
            "event CG profiles default to the programmatic scene theme without a bitmap background");
        var retiredBitmap = JsonConvert.DeserializeObject<AuraToolsEventCgSettings>(
            "{\"schemaVersion\":2,\"scenes\":{\"victory.standard\":{\"enabled\":true,\"backgroundResource\":\"Mods/AuraToolsExp/ModResource/DPSCG/DPS-CG.png\"}}}")!;
        retiredBitmap.Normalize();
        Assert(retiredBitmap.GetScene(AuraToolsEventCgSceneIds.VictoryStandard).EffectiveBackgroundResource == "",
            "an explicit legacy DPS-CG scene reference migrates one way to the programmatic theme");
        Assert(opening.EffectiveBaseWidth == 1 && opening.EffectiveBaseHeight == 1,
            "migrated event CG dimensions are clamped positive per scene");
        Assert(opening.EffectiveFadeIn == 0f
               && Math.Abs(opening.EffectiveHold - 30f) < 0.001f
               && Math.Abs(opening.EffectiveFadeOut - 5f) < 0.001f,
            "migrated event CG timing is clamped per scene");
        Assert(opening.BattleIds.SequenceEqual(new[] { "boss-a", "boss-b" }, StringComparer.OrdinalIgnoreCase),
            "legacy special battle ids migrate only into the battle-opening profile");
        var serialized = JsonConvert.SerializeObject(settings);
        Assert(serialized.Contains("\"scenes\"", StringComparison.Ordinal)
               && !serialized.Contains("specialBattleIds", StringComparison.Ordinal)
               && !serialized.Contains("backgroundResource\":\"Mods/AuraToolsExp", StringComparison.Ordinal),
            "event CG writes only the final scene-profile schema and omits inherited default paths");

        var damageSettings = JsonConvert.DeserializeObject<DamageMeterSettings>(
            "{\"settlementCg\":{\"enabled\":false,\"syncRemote\":false,\"backgroundResource\":\"legacy.png\",\"baseWidth\":1200,\"baseHeight\":700,\"fadeIn\":0.2,\"hold\":4,\"fadeOut\":0.6}}")!;
        var roleCg = new AuraToolsSkillCgSettings();
        Assert(roleCg.TryImportLegacySettlementCg(damageSettings.TakeLegacySettlementCg()),
            "legacy damage settlement settings migrate exactly once into event CG");
        Assert(!roleCg.EventCg.Enabled
               && !roleCg.EventCg.SyncRemote
               && roleCg.EventCg.GetScene(AuraToolsEventCgSceneIds.AdventureSettlement).EffectiveBackgroundResource == "legacy.png"
               && roleCg.EventCg.GetScene(AuraToolsEventCgSceneIds.AdventureSettlement).EffectiveBaseWidth == 1200
               && roleCg.EventCg.GetScene(AuraToolsEventCgSceneIds.AdventureSettlement).EffectiveBaseHeight == 700,
            "legacy presentation choices survive the bounded migration");
        Assert(!roleCg.TryImportLegacySettlementCg(damageSettings.TakeLegacySettlementCg())
               && !JsonConvert.SerializeObject(damageSettings).Contains("settlementCg", StringComparison.Ordinal),
            "the retired damage settlement configuration is consumed and never re-serialized");

        var legacyRoleCg = JsonConvert.DeserializeObject<AuraToolsSkillCgSettings>(
            "{\"schemaVersion\":7,\"roles\":{\"career_1\":{\"enabled\":true,\"roleId\":\"career_1\",\"rules\":[{\"enabled\":false,\"sourceOwnerModId\":\"Example\",\"sourceCgId\":\"registered\",\"cardId\":\"careercard_1\",\"image\":\"registered.png\"},{\"enabled\":true,\"providerId\":\"legacy.manual\",\"cardId\":\"careercard_1\",\"image\":\"manual.png\"}]}}}")!;
        legacyRoleCg.Normalize();
        Assert(legacyRoleCg.SchemaVersion == 10
               && legacyRoleCg.LegacyRoleRulesMigrated
               && legacyRoleCg.Roles.Count == 0
               && legacyRoleCg.RoleEntries.ContainsKey("Example:registered")
               && legacyRoleCg.ManualRoleEntries.Count == 1
               && legacyRoleCg.GetRoleSelection(
                   "career_1",
                   AuraToolsRoleCgContextKeys.SkillChannel,
                   "careercard_1") == "AuraToolsExp:user.legacy.career_1.2",
            "legacy copied registrations become context selections while genuine manual rules become user-owned entries");
        var legacyRoleJson = JObject.Parse(JsonConvert.SerializeObject(legacyRoleCg));
        Assert(legacyRoleJson["roles"] == null,
            "the retired role-rule writer is removed after one-way migration");
        Assert(legacyRoleJson["roleEntries"]?["Example:registered"]?["enabled"] == null,
            "role entry overrides no longer serialize the retired global activation flag");
        var retiredLowHealth = JsonConvert.DeserializeObject<AuraToolsSkillCgSettings>(
            "{\"schemaVersion\":9,\"lowHealthThreshold\":0.75}")!;
        retiredLowHealth.Normalize();
        Assert(retiredLowHealth.SchemaVersion == 10
               && !JsonConvert.SerializeObject(retiredLowHealth).Contains("lowHealthThreshold", StringComparison.Ordinal),
            "role CG consumes but never rewrites the retired configurable low-health threshold");
    }

    public static void TestCgOutcomeReasonPolicy()
    {
        Assert(AuraToolsCgOutcomeReasonPolicy.ResolveEscape(true) == AuraToolsEventCgOutcomeReasons.MidasEscape,
            "a point-gold escape is classified as its successful victory reason");
        Assert(AuraToolsCgOutcomeReasonPolicy.ResolveEscape(false) == AuraToolsEventCgOutcomeReasons.Escape,
            "an ordinary escape is not classified as battle defeat");
        Assert(AuraToolsCgOutcomeReasonPolicy.ResolveWin(true, false) == AuraToolsEventCgOutcomeReasons.RitualVictory,
            "ritual victory keeps its semantic reason");
        Assert(AuraToolsCgOutcomeReasonPolicy.ResolveWin(false, true) == AuraToolsEventCgOutcomeReasons.CurseVictory,
            "curse victory keeps its semantic reason");
        Assert(AuraToolsCgOutcomeReasonPolicy.ResolveWin(false, false) == AuraToolsEventCgOutcomeReasons.StandardVictory,
            "unclassified wins use the normal victory scene");
    }

    public static void TestRoleCgContextSelections()
    {
        var settings = new AuraToolsSkillCgSettings();
        settings.SetRoleSelection("Career_1", AuraToolsRoleCgContextKeys.SkillChannel, "*", "Example:wildcard");
        Assert(settings.GetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_1")
               == "Example:wildcard",
            "a migrated wildcard skill selection is inherited by concrete skills for the same role");

        settings.SetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_1", "Example:exact");
        Assert(settings.GetRoleSelection("CAREER_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_1")
               == "Example:exact",
            "an exact role-and-skill selection overrides the wildcard fallback without affecting other skills");
        settings.ResetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_1");
        Assert(settings.GetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_1")
               == "Example:wildcard",
            "resetting an exact role CG selection reveals its inherited wildcard preference");
        settings.ResetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_2");
        Assert(settings.GetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_2") == "",
            "resetting a context with only a wildcard preference restores manifest defaults");

        settings.SetRoleSelection(
            "career_1",
            AuraToolsRoleCgContextKeys.SkillChannel,
            "careercard_3",
            AuraToolsRoleCgContextKeys.NoneSelectionCgId);
        Assert(settings.GetRoleSelection("career_1", AuraToolsRoleCgContextKeys.SkillChannel, "careercard_3")
               == AuraToolsRoleCgContextKeys.NoneSelectionCgId,
            "an explicitly disabled legacy context remains distinct from an unset manifest-default context");
    }

    public static void TestRoleCgSkillIdentityAliases()
    {
        var authoritative = new[]
        {
            "Terrias_wuna_wuna_white_sun_prayer",
            "Terrias_wuna_wuna_grave_song"
        };
        Assert(AuraToolsRoleSkillIdentity.ResolveEquivalent(
                   "wuna_white_sun_prayer",
                   authoritative,
                   "Terrias") == "Terrias_wuna_wuna_white_sun_prayer",
            "role CG short ids resolve to the authoritative owner-qualified career skill");
        Assert(AuraToolsRoleSkillIdentity.ContainsEquivalent(
                   authoritative,
                   "*Terrias_wuna_wuna_grave_song",
                   "Terrias"),
            "role CG protocol markers do not create duplicate player-facing skills");
        Assert(!AuraToolsRoleSkillIdentity.ContainsEquivalent(
                   authoritative,
                   "unregistered_skill",
                   "Terrias"),
            "an unrelated skill remains distinct from the authoritative role skill set");
    }

    public static void TestLowHealthNativePresentationLatch()
    {
        var latch = new AuraToolsLowHealthPresentationLatch();
        Assert(latch.TryEnter("local-status")
               && !latch.TryEnter("local-status")
               && !latch.TryEnter(""),
            "native Dying presentation emits once per local status in one battle");
        latch.Reset();
        Assert(latch.TryEnter("local-status"),
            "the native Dying presentation latch resets at the next battle generation");
    }

    public static void TestCgSettingsLayoutBudgets()
    {
        foreach (var viewport in new[] { (Width: 922f, Height: 838f), (Width: 1280f, Height: 720f) })
        {
            var budget = AuraToolsCgSettingsLayoutPolicy.Evaluate(viewport.Width, viewport.Height);
            Assert(budget.Fits,
                "role and event CG single-context layouts fit viewport "
                + viewport.Width + "x" + viewport.Height
                + " without a permanent side rail or nested scroll area");
        }
    }
    
    public static void TestTeamScenePlanning()
    {
        var source = new AuraCgSceneSourceSnapshot
        {
            SceneId = "adventure-settlement",
            EventToken = "settlement-1",
            Participants = new List<AuraCgSceneParticipantSource>
            {
                new() { Order = 3, PlayerId = "p3", RoleId = "role3", RoleVariantId = "skin:3" },
                new() { Order = 0, PlayerId = "p0", RoleId = "role0", RoleVariantId = "skin:0" },
                new() { Order = 1, PlayerId = "p1", RoleId = "role1", RoleVariantId = "skin:1" },
                new() { Order = 2, PlayerId = "p2", RoleId = "role2", RoleVariantId = "skin:2" },
                new() { Order = 9, PlayerId = "p1", RoleId = "duplicate-must-drop" },
                new() { Order = 4, PlayerId = "p4", RoleId = "role4" },
                new() { Order = 5, PlayerId = "p5", RoleId = "role5" },
                new() { Order = 6, PlayerId = "p6", RoleId = "role6" },
                new() { Order = 7, PlayerId = "p7", RoleId = "role7" },
                new() { Order = 8, PlayerId = "p8", RoleId = "role8" }
            }
        };
        var template = new AuraCgSceneTemplateSpec
        {
            MaximumParticipants = 8,
            BackgroundAsset = new AuraCgSceneAssetReference
            {
                OwnerModId = "AuraToolsExp",
                AssetId = "event.background.settlement"
            },
            RoleLayerOwnerModId = "AuraToolsExp",
            RoleLayerAssetPrefix = "role.idle."
        };
        var plan = AuraCgTeamScenePlanner.Build(
            source,
            template,
            "aura.adventure.settlement.entering");
        Assert(plan != null && plan.IsValid(), "team settlement source produces one valid processed scene plan");
        Assert(plan!.Participants.Count == 8
               && plan.Participants.Select(item => item.RoleId).SequenceEqual(
                   Enumerable.Range(0, 8).Select(index => "role" + index)),
            "team scene preserves actual player order, deduplicates players, and caps the processed plan at eight roles");
        Assert(plan.Participants.Select(item => item.SeatIndex).SequenceEqual(Enumerable.Range(0, 8))
               && plan.Participants.All(item => item.Width > 0f && item.Height > 0f),
            "the shared layout resolves stable positions without damage ranking input");
        var wire = JsonConvert.SerializeObject(plan);
        Assert(!wire.Contains("PlayerId", StringComparison.OrdinalIgnoreCase)
               && !wire.Contains("Damage", StringComparison.OrdinalIgnoreCase)
               && !wire.Contains("Path", StringComparison.OrdinalIgnoreCase)
               && wire.Contains("event.background.settlement", StringComparison.Ordinal),
            "the transmitted scene plan contains logical assets and positions, never rich player or damage source data");
    }
    
    public static void TestEventCgArtCatalog()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "tools", "shared-consumers.json")))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Repository root not found.");
        var root = Path.Combine(directory.FullName, "AuraToolsExp", "SharedResources", "EventCg");
        var catalog = AuraToolsEventCgArtCatalog.Parse(File.ReadAllText(Path.Combine(root, "event-cg.art.json")));
        Assert(catalog.Characters.Count == 15 && catalog.PoseCount == 6 && catalog.Themes.Count == 7,
            "the event CG catalog contains the reviewed character range, six poses, and seven themes");
        Assert(catalog.Assets.Values.All(asset => File.Exists(AuraToolsEventCgArtCatalog.ResolveAssetPath(root, asset.Path))),
            "all event CG logical assets resolve inside the published art package");
        var caroline = catalog.FindCharacter("career_5")!;
        Assert(catalog.ResolvePose(caroline, AuraToolsEventCgSceneIds.BattleDefeat) != caroline.Neutral
               && catalog.ResolvePose(caroline, AuraToolsEventCgSceneIds.BattleOpening) == caroline.Neutral,
            "event poses are selected by event and absent poses use the same character's neutral portrait");
        Assert(catalog.FindCharacter("career_5", "Other:skin") == null
               && catalog.FindCharacter("career_11")!.Id == "caroline-alt",
            "custom skins do not accidentally select default art and alternate forms keep separate identities");
        var escaped = false;
        try { AuraToolsEventCgArtCatalog.ResolveAssetPath(root, "../outside.png"); }
        catch (InvalidDataException) { escaped = true; }
        Assert(escaped, "packaged artwork paths cannot escape the art directory");
        var legacy = JsonConvert.DeserializeObject<AuraToolsEventCgSceneSettings>(@"{""hold"":2}")!;
        legacy.Normalize(AuraToolsEventCgSceneIds.VictoryStandard);
        Assert(legacy.MotionEnabled && legacy.EffectiveHold == 2f, "old event settings retain duration and gain the motion default");
        legacy.MotionEnabled = false;
        var roundTrip = JsonConvert.DeserializeObject<AuraToolsEventCgSceneSettings>(JsonConvert.SerializeObject(legacy))!;
        Assert(!roundTrip.MotionEnabled, "reduced-motion choice survives settings serialization");
        var localArtwork = new AuraCgSceneArtwork
        {
            CameraPush = 0.02f,
            Layers = new List<AuraCgSceneArtLayerSpec> { new() { MotionX = 0.02f, MotionY = 0.01f, Pulse = 0.1f } }
        };
        AuraToolsEventCgArtCatalog.ApplyMotionPreference(localArtwork, roundTrip.MotionEnabled);
        Assert(localArtwork.CameraPush == 0f && localArtwork.Layers.All(layer => layer.MotionX == 0f && layer.MotionY == 0f && layer.Pulse == 0f),
            "a recipient's reduced-motion preference disables camera, layer movement, and light pulses");
        roundTrip.ResetPresentation();
        Assert(roundTrip.MotionEnabled && roundTrip.UsesDefaultPresentation, "reset restores the complete event presentation default");
    }
}
