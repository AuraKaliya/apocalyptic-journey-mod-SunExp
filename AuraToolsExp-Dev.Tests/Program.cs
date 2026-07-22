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

var assertions = 0;

TestRoundAndDpt();
TestShieldViewRecalculation();
TestSnapshotRecovery();
TestSequenceAndSessionGuards();
TestLongRunningTotals();
TestRunAggregateSurvivesHistoryRetention();
TestFilteringAndGrandTotal();
TestDetailLimit();
TestAdventureHistory();
TestBestHitAndScientificFormat();
TestOutOfRunHistoryBuilder();
TestDeterministicAllocation();
TestHotkeyNames();
TestInputFaultGate();
TestDamageMeterSettingsNormalization();
TestConfigModelSerializationCompatibility();
TestCardRefreshSettingsAndPoolPolicy();
TestLoggingSettingsNormalization();
TestDamageSettlementCgSettingsAndLayout();
TestDamageSettlementCgPayloadOrdering();
TestDamageSettlementCgAnimationSpec();
TestDamageSettlementCgPreparationPolicy();
TestSkillCgPresentationNormalization();
TestFeastRoleResourceIdentity();
TestSkinRemoteSelectionPolicy();
TestSafeBoxDataCompatibility();
TestRpcPayloadBudgetUsesUtf8Bytes();
TestModSyncRequestTracker();
TestDamageMeterAuthorityPolicy();
TestDamageCaptureFrameWindow();
TestDamageCaptureMatchingPolicy();
TestDamageEventFactory();
TestDamageMeterHookRegistrationSet();
TestDamageMeterHudPresenter();
TestStarterDeckCardClassification();
TestStarterDeckDeckBuilder();
TestRuntimeArchitectureGuards();

Console.WriteLine($"AuraToolsExp tests passed: {assertions} assertions.");
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

void TestModSyncRequestTracker()
{
    var tracker = new AuraToolsModSyncRequestTracker();
    var now = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);
    tracker.Begin("request-a", now, TimeSpan.FromSeconds(5), AuraToolsModSyncRequestMode.Targeted);

    Assert(tracker.IsPending
           && tracker.IsPendingRequest("request-a")
           && tracker.Mode == AuraToolsModSyncRequestMode.Targeted,
        "mod sync request tracker starts a correlated pending request");
    Assert(tracker.Matches("request-a") && tracker.Matches(""),
        "mod sync request tracker accepts correlated and legacy responses while pending");
    Assert(!tracker.Matches("request-b"),
        "mod sync request tracker rejects unrelated responses");
    Assert(!tracker.IsExpired(now.AddSeconds(4)) && tracker.IsExpired(now.AddSeconds(5)),
        "mod sync request tracker expires at its bounded deadline");

    tracker.Begin("request-b", now, TimeSpan.FromSeconds(10), AuraToolsModSyncRequestMode.BroadcastFallback);
    Assert(tracker.Mode == AuraToolsModSyncRequestMode.BroadcastFallback
           && tracker.IsPendingRequest("request-b")
           && !tracker.Matches("request-a"),
        "mod sync request tracker replaces a timed-out targeted request with one broadcast fallback");

    tracker.Clear();
    Assert(!tracker.IsPending && !tracker.Matches("request-b"),
        "mod sync request tracker rejects late responses after completion or fallback");
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

void TestRunAggregateSurvivesHistoryRetention()
{
    var history = new DamageHistoryStore();
    var run = new DamageRunLedger();
    run.BeginAdventure("endless", "start");
    long expectedTotal = 0;
    var expectedRounds = DamageMeterProtocol.MaxFightHistory + 5;

    for (var index = 1; index <= expectedRounds; index++)
    {
        var ledger = new DamageLedger();
        ledger.StartFight("endless-fight-" + index, true);
        ledger.StartRound(1);
        var damage = Event(ledger, 1, "alpha", index, 0, DamageTeam.Friendly, "card_" + index);
        Assert(ledger.Apply(damage), "endless fight event accepted " + index);
        Assert(run.Apply(damage), "run aggregate event accepted " + index);
        ledger.EndFight();
        var snapshot = ledger.CreateSnapshot();
        Assert(run.RecordEncounter(snapshot), "run aggregate records encounter " + index);
        Assert(!run.RecordEncounter(snapshot), "run aggregate rejects duplicate encounter " + index);
        Assert(history.Archive(snapshot, "Win", index.ToString()), "bounded fight history archives " + index);
        expectedTotal += index;
    }

    Assert(history.Records.Count == DamageMeterProtocol.MaxFightHistory,
        "bounded fight history still trims old fights");

    var historyRecord = OutOfRunDamageHistoryBuilder.Build(
        history.Records,
        new OutOfRunDamageHistoryBuildRequest
        {
            AdventureId = "endless",
            TeamMembers = new[]
            {
                new OutOfRunTeamMemberSnapshot { InstanceId = "alpha", PlayerId = "alpha" }
            }
        });
    Assert(historyRecord.TeamTotalDamage < expectedTotal,
        "bounded history no longer represents endless totals");

    var aggregate = run.CreateSnapshot();
    var runRecord = OutOfRunDamageHistoryBuilder.Build(
        aggregate,
        new OutOfRunDamageHistoryBuildRequest
        {
            AdventureId = "endless",
            TeamMembers = new[]
            {
                new OutOfRunTeamMemberSnapshot { InstanceId = "alpha", PlayerId = "alpha" }
            }
        });
    Assert(aggregate.EncounterCount == expectedRounds
           && aggregate.TotalRounds == expectedRounds
           && aggregate.ConfirmedEventCount == expectedRounds,
        "run aggregate keeps unbounded encounter metadata");
    Assert(runRecord.TeamTotalDamage == expectedTotal
           && runRecord.TotalRounds == expectedRounds
           && runRecord.TeamMembers[0].TotalDamage == expectedTotal,
        "run aggregate powers endless out-of-run totals");

    var restored = new DamageRunLedger();
    Assert(restored.ApplySnapshot(aggregate), "run aggregate snapshot restores");
    var stale = restored.CreateSnapshot();
    var extraLedger = new DamageLedger();
    extraLedger.StartFight("endless-extra", true);
    extraLedger.StartRound(1);
    var extraDamage = Event(extraLedger, 1, "alpha", 10, 0, DamageTeam.Friendly, "extra");
    Assert(extraLedger.Apply(extraDamage), "extra event accepted");
    Assert(restored.Apply(extraDamage), "restored aggregate advances");
    Assert(!restored.ApplySnapshot(stale), "stale run aggregate snapshot cannot roll totals back");
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

void TestConfigModelSerializationCompatibility()
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
        "{\"schemaVersion\":1,\"starterDeck\":{\"preferRoleModProfile\":false},\"safeBox\":null,\"modSync\":null,\"feast\":null,\"damageMeter\":null,\"cardRefresh\":null}")!;
    matchExperience.Normalize();
    Assert(matchExperience.SchemaVersion == 9
           && matchExperience.StarterDeck.PreferRoleModProfile
           && matchExperience.SafeBox != null
           && matchExperience.ModSync != null
           && matchExperience.Feast.Enabled
           && matchExperience.DamageMeter != null
           && matchExperience.CardRefresh != null,
        "match-experience config keeps legacy schema migration and nested defaults after the file split");

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

void TestCardRefreshSettingsAndPoolPolicy()
{
    var settings = new AuraToolsMatchExperienceSettings
    {
        SchemaVersion = 1,
        CardRefresh = null!
    };
    settings.Normalize();
    Assert(settings.SchemaVersion == 9, "match-experience settings migrate to multi-candidate Feast selection");
    Assert(settings.CardRefresh != null && !settings.CardRefresh.Enabled,
        "card refresh is restored with a disabled default during normalization");

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

void TestLoggingSettingsNormalization()
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

void TestDamageSettlementCgSettingsAndLayout()
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

void TestDamageSettlementCgPayloadOrdering()
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

void TestDamageSettlementCgAnimationSpec()
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

void TestDamageSettlementCgPreparationPolicy()
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

void TestSkinRemoteSelectionPolicy()
{
    var snapshot = new SkinSelectionSnapshot
    {
        PlayerId = "remote-player",
        CareerId = "Terrias_loneer_loneer",
        SkinId = "night"
    };
    Assert(SkinRemoteSelectionPolicy.ShouldRetain(snapshot, new SkinSelectionResolveResult
        {
            Success = false,
            DefaultSkin = false
        }),
        "missing remote skin resources retain the latest state for reconciliation");
    Assert(!SkinRemoteSelectionPolicy.ShouldRetain(snapshot, new SkinSelectionResolveResult
        {
            Success = true,
            DefaultSkin = true
        }),
        "explicit default skin clears a previous remote override");
    snapshot.SkinId = "";
    Assert(!SkinRemoteSelectionPolicy.ShouldRetain(snapshot, new SkinSelectionResolveResult()),
        "invalid remote skin identity is not retained");
}

void TestSkillCgPresentationNormalization()
{
    var settings = new AuraToolsSkillCgSettings
    {
        DefaultPresentation = new SkillCgPresentationSettings
        {
            Mode = "fullscreenFade",
            Fit = "cover",
            FadeIn = 0.2f,
            Hold = 2f,
            FadeOut = 0.3f,
            FocusX = 0.4f,
            FocusY = 0.6f,
            SafeScale = 1.1f
        },
        Roles = new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase)
        {
            ["career_1"] = new()
            {
                RoleId = "career_1",
                Rules =
                {
                    new SkillCgRuleSettings
                    {
                        CardId = "careercard_1",
                        Image = "CG/Roles/1/skill_cg.png"
                    },
                    new SkillCgRuleSettings
                    {
                        CardId = "careercard_2",
                        Image = "CG/AuraToolsExp/Roles/1/skill_cg_2.png",
                        Presentation = new SkillCgPresentationSettings
                        {
                            Mode = "centerFade",
                            Fit = "stretch",
                            Hold = 1.25f,
                            FocusX = 2f,
                            SafeScale = 0.5f
                        }
                    }
                }
            }
        }
    };

    settings.Normalize();
    var rules = settings.Roles["career_1"].Rules;
    Assert(settings.SchemaVersion == 4
           && rules[0].Image == "CG/Roles/1/skill_cg.png"
           && rules[0].EffectivePresentation.Mode == "fullscreenFade"
           && rules[0].EffectivePresentation.Fit == "cover"
           && Math.Abs(rules[0].EffectivePresentation.Hold - 2f) < 0.001f
           && Math.Abs(rules[0].EffectivePresentation.FocusX - 0.4f) < 0.001f
           && Math.Abs(rules[0].EffectivePresentation.FocusY - 0.6f) < 0.001f
           && Math.Abs(rules[0].EffectivePresentation.SafeScale - 1.1f) < 0.001f,
        "skill CG preserves imported paths and presentation inherits global defaults");
    Assert(rules[1].EffectivePresentation.Mode == "centerFade"
           && rules[1].EffectivePresentation.Fit == "stretch"
           && Math.Abs(rules[1].EffectivePresentation.FadeIn - 0.2f) < 0.001f
           && Math.Abs(rules[1].EffectivePresentation.Hold - 1.25f) < 0.001f
           && Math.Abs(rules[1].EffectivePresentation.FocusX - 1f) < 0.001f
           && Math.Abs(rules[1].EffectivePresentation.FocusY - 0.6f) < 0.001f
           && Math.Abs(rules[1].EffectivePresentation.SafeScale - 1f) < 0.001f,
        "skill CG rule presentation overrides selected fields");
}

void TestRpcPayloadBudgetUsesUtf8Bytes()
{
    var small = new { Kind = "small", Payload = "ok" };
    Assert(AuraToolsRpcPayloadGuard.FitsSoftLimit(
            small,
            AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
            out var smallBytes,
            out var smallError)
           && smallBytes > 0
           && smallError == "",
        "small RPC payload fits the soft budget");

    var oversized = new { Kind = "oversized", Payload = new string('界', 23000) };
    Assert(AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(oversized, out var oversizedBytes, out var oversizedError)
           && oversizedError == ""
           && oversizedBytes > AuraToolsRpcPayloadGuard.MirrorStringLimitBytes,
        "oversized RPC payload is measured by UTF-8 bytes past Mirror's string limit");
    Assert(!AuraToolsRpcPayloadGuard.FitsSoftLimit(
            oversized,
            AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
            out _,
            out _),
        "oversized RPC payload is rejected before Mirror serialization");
    Assert(AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes < AuraToolsRpcPayloadGuard.MirrorStringLimitBytes,
        "soft RPC budget keeps headroom below Mirror's hard string limit");
}

void TestDamageMeterAuthorityPolicy()
{
    var host = new AuraToolsRpcSender("host", "Host", true, true, "test", true);
    var client = new AuraToolsRpcSender("client", "Client", true, false, "test", true);
    Assert(DamageMeterAuthorityPolicy.RequireHostControl(host, out var hostReject)
           && hostReject == "",
        "host control accepted");
    Assert(!DamageMeterAuthorityPolicy.RequireHostControl(client, out var nonHostReject)
           && nonHostReject == "control issuer is not host",
        "non-host control rejected");
    Assert(!DamageMeterAuthorityPolicy.RequireLobbyMember(AuraToolsRpcSender.Unbound, out var missingReject)
           && missingReject == "missing server sender",
        "missing sender rejected");

    var candidate = Event(NewLedger(), 1, "source", 10, 0, DamageTeam.Friendly, "detail");
    candidate.ReporterPlayerId = "spoofed-host";
    Assert(DamageMeterAuthorityPolicy.TryBindReporter(candidate, client, out var bound, out var bindReject)
           && bindReject == "",
        "reporter binding accepted");
    Assert(bound.ReporterPlayerId == "client" && candidate.ReporterPlayerId == "spoofed-host",
        "reporter binding uses server sender and leaves original untouched");

    var outsider = new AuraToolsRpcSender("outsider", "", false, false, "test", true);
    Assert(!DamageMeterAuthorityPolicy.TryBindReporter(candidate, outsider, out _, out var outsideReject)
           && outsideReject == "sender not in lobby",
        "non-lobby sender rejected");
}

void TestFeastRoleResourceIdentity()
{
    var first = AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.FolderName("Mod/Role:A");
    var second = AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.FolderName("Mod\\Role:A");
    Assert(first.StartsWith("Mod_Role_A--", StringComparison.Ordinal)
           && second.StartsWith("Mod_Role_A--", StringComparison.Ordinal)
           && first != second,
        "feast role folders stay readable while hash suffixes prevent sanitized id collisions");
    Assert(AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.CgId("RoleA")
           == AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.CgId("rolea"),
        "feast generated CG identity is stable across case-insensitive role ids");
}

void TestDamageCaptureFrameWindow()
{
    var released = 0;
    var window = new DamageFrameWindow<TestCaptureFrame>(2, _ => released++);
    var first = window.Rent(1);
    first.Value = 11;
    window.Add(first);
    var second = window.Rent(2);
    second.Value = 22;
    window.Add(second);
    var third = window.Rent(3);
    third.Value = 33;
    window.Add(third);

    Assert(window.Count == 2 && released == 1, "capture window evicts oldest frame at capacity");
    Assert(first.Frame == 0 && first.Value == 0, "evicted capture frame is reset before pooling");

    window.PruneOlderThan(8, 4);
    Assert(window.Count == 0 && released == 3, "capture window prunes every expired frame");

    var reused = window.Rent(9);
    Assert(reused.Frame == 9 && reused.Value == 0, "capture frame pool returns reset state");
    window.Add(reused);
    window.Clear();
    Assert(window.Count == 0 && released == 4 && reused.Frame == 0,
        "capture window clear releases and resets remaining frames");
}

void TestDamageCaptureMatchingPolicy()
{
    Assert(DamageCaptureMatchingPolicy.IsHitMatch("target", "source", "target", "source"),
        "damage text pairs with the exact hit frame");
    Assert(DamageCaptureMatchingPolicy.IsHitMatch("target", "source", "target", ""),
        "damage text without a source can pair by target");
    Assert(!DamageCaptureMatchingPolicy.IsHitMatch("target", "source-a", "target", "source-b"),
        "damage text rejects a conflicting source");
    Assert(!DamageCaptureMatchingPolicy.IsHitMatch("target-a", "source", "target-b", "source"),
        "damage text rejects a conflicting target");
    Assert(DamageCaptureMatchingPolicy.Loss(100, 35) == 65
           && DamageCaptureMatchingPolicy.Loss(35, 100) == 0,
        "capture loss is positive-only for damage and healing boundaries");
}

void TestDamageEventFactory()
{
    var damage = DamageEventFactory.Create(new ResolvedDamageInput
    {
        SourceInstanceId = "  ",
        SourceDisplayName = "",
        TargetInstanceId = " target ",
        SourceDataId = new string('x', DamageMeterProtocol.MaxStringLength + 10),
        DetailLabel = " detail ",
        DamageType = " ",
        HpDamage = -1,
        ShieldDamage = DamageMeterProtocol.MaxDamagePerEvent + 1,
        FinalDamage = 12,
        AttributionConfidence = DamageAttributionConfidence.Exact
    });

    Assert(damage.SourceInstanceId == "unknown" && damage.SourceDisplayName == "unknown",
        "damage event factory supplies stable source fallbacks");
    Assert(damage.TargetInstanceId == "target" && damage.DetailLabel == "detail"
           && damage.DamageType == "Unknown",
        "damage event factory trims and normalizes labels");
    Assert(damage.SourceDataId.Length == DamageMeterProtocol.MaxStringLength
           && damage.HpDamage == 0
           && damage.ShieldDamage == DamageMeterProtocol.MaxDamagePerEvent
           && damage.FinalDamage == 12,
        "damage event factory enforces protocol budgets");
}

void TestDamageMeterHookRegistrationSet()
{
    var registrations = new DamageMeterHookRegistrationSet();
    var disposed = 0;
    Assert(registrations.Register("before:Hit", () => new TestDisposable(() => disposed++)),
        "hook registration accepts a new key");
    Assert(!registrations.Register("before:Hit", () => new TestDisposable(() => disposed += 100)),
        "hook registration is idempotent by route key");
    Assert(registrations.Register("after:Hit", () => new TestDisposable(() => throw new InvalidOperationException("busy"))),
        "hook registration accepts an independent phase");
    var failures = new List<string>();
    Assert(registrations.DisposeAll((key, _) => failures.Add(key)) == 1,
        "failed hook disposal remains registered for retry");
    Assert(disposed == 1 && failures.SequenceEqual(new[] { "after:Hit" }),
        "hook disposal releases healthy handles and reports the failing key");
}

void TestDamageMeterHudPresenter()
{
    var settings = new DamageMeterSettings();
    settings.Normalize();
    var idle = DamageMeterHudPresenter.Build(
        new DamageLedger(),
        new DamageRunLedger(),
        new DamageHistoryStore(),
        settings,
        "offline");
    Assert(!idle.InFight && idle.Height == 250f && idle.VisibleRows.Count == 0,
        "damage HUD presenter builds the idle layout without runtime UI state");
    Assert(idle.Title.Contains("世界推演", StringComparison.Ordinal)
           && idle.Footer.Contains("offline", StringComparison.Ordinal),
        "damage HUD presenter keeps idle title and network state");

    var ledger = NewLedger();
    ledger.StartRound(1);
    Apply(ledger, 1, "hero", 30, 5, DamageTeam.Friendly, "card");
    var active = DamageMeterHudPresenter.Build(
        ledger,
        new DamageRunLedger(),
        new DamageHistoryStore(),
        settings,
        "host");
    Assert(active.InFight && active.VisibleRows.Count == 1 && active.GrandTotal == 35,
        "damage HUD presenter builds active rows and shield-inclusive totals");
    Assert(active.Title.Contains("回合 1", StringComparison.Ordinal)
           && active.Footer.Contains("本场合计 35", StringComparison.Ordinal),
        "damage HUD presenter formats active fight summary");
}

void TestRuntimeArchitectureGuards()
{
    var roleCatalog = ReadRepoText("AuraToolsExp-Dev/Infrastructure/RoleCatalog.cs");
    var roleDisplayResolver = roleCatalog.Substring(
        roleCatalog.IndexOf("private static string ResolveDisplayName", StringComparison.Ordinal),
        roleCatalog.IndexOf("private static List<RoleSkillInfo> ResolveSkills", StringComparison.Ordinal)
        - roleCatalog.IndexOf("private static string ResolveDisplayName", StringComparison.Ordinal));
    Assert(roleDisplayResolver.Contains(".Localize(\"Name\")", StringComparison.Ordinal)
           && !roleDisplayResolver.Contains("new DataConfig", StringComparison.Ordinal),
        "role catalog resolves startup display names from loaded rows without forcing the unfinished DataConfig id cache");
    Assert(roleCatalog.Contains("AuraRoleRegistryRuntime.GetEffectiveSnapshot()", StringComparison.Ordinal)
           && roleCatalog.Contains("cachedCatalogEpoch", StringComparison.Ordinal)
           && roleCatalog.Contains("cachedRoleRevision", StringComparison.Ordinal)
           && roleCatalog.Contains("snapshot.NativeReady", StringComparison.Ordinal)
           && !roleCatalog.Contains("PublishRuntimeRoles", StringComparison.Ordinal),
        "role catalog consumes only effective enabled roles and invalidates on both game-data and role-registry revisions");

    var epochAwareStarterDeckCatalog = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/StarterDeckCardCatalog.cs");
    var epochAwareStarterDeckRuntime = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/AuraToolsStarterDeckRuntime.cs");
    Assert(epochAwareStarterDeckCatalog.Contains("AuraGameDataCatalogRuntime.SnapshotChanged += OnCatalogSnapshotChanged", StringComparison.Ordinal)
           && epochAwareStarterDeckCatalog.Contains("cardCatalogEpoch", StringComparison.Ordinal)
           && epochAwareStarterDeckRuntime.Contains("StarterDeckCardCatalog.Initialize()", StringComparison.Ordinal),
        "starter-deck catalogs invalidate presentation and card snapshots when game-data epochs change");

    var cardRefreshRuntime = ReadRepoText("AuraToolsExp-Dev/Features/CardRefresh/AuraToolsCardRefreshRuntime.cs");
    var cardRefreshNativeApi = ReadRepoText("AuraToolsExp-Dev/Features/CardRefresh/CardChoiceRefreshNativeApi.cs");
    var matchExperienceConfig = ReadRepoText("AuraToolsExp/Config/MatchExperienceSettings.json");
    Assert(cardRefreshRuntime.Contains("AuraUiNativeButtonCloneAdapter.TryClone", StringComparison.Ordinal)
           && cardRefreshRuntime.Contains("AuraToolsHookRegistry.Before(modConfig, \"CardChoiceUI.Start\"", StringComparison.Ordinal)
           && cardRefreshRuntime.Contains("AuraToolsHookRegistry.After(modConfig, \"CardChoiceUI.Start\"", StringComparison.Ordinal)
           && cardRefreshRuntime.Contains("BeforeCardChoiceUiSelect", StringComparison.Ordinal),
        "card refresh uses shared hooks and the native button clone at the card-choice lifecycle boundaries");
    Assert(cardRefreshRuntime.Contains("CaptureCleanTemplates", StringComparison.Ordinal)
           && cardRefreshRuntime.Contains("CloneCurrentDice", StringComparison.Ordinal)
           && cardRefreshNativeApi.Contains("DiceCopyConstructor?.Invoke", StringComparison.Ordinal)
           && cardRefreshNativeApi.Contains("new RandomPool(pool, dice).DrawByRarity", StringComparison.Ordinal)
           && cardRefreshNativeApi.Contains("manager.CardPackCheck", StringComparison.Ordinal),
        "card refresh recreates clean choice items and uses a window-local clone of the native reward draw pipeline");
    Assert(matchExperienceConfig.Contains("\"schemaVersion\": 9", StringComparison.Ordinal)
           && matchExperienceConfig.Contains("\"cardRefresh\"", StringComparison.Ordinal)
           && matchExperienceConfig.Contains("\"enabled\": false", StringComparison.Ordinal),
        "shipped card refresh configuration is present and disabled by default");

    var safeBoxRuntime = ReadRepoText("AuraToolsExp-Dev/Features/SafeBox/AuraToolsSafeBoxRuntime.cs");
    Assert(safeBoxRuntime.Contains("Mods/AuraToolsExp/ModResource/Images/UI/\\u968f\\u8eab\\u4fdd\\u9669\\u7bb1.png", StringComparison.Ordinal)
           && safeBoxRuntime.Contains("AuraUiNativeButtonIconOwner.Apply(manager, icon)", StringComparison.Ordinal),
        "safe box TopBar button owns all native visual-state images for the shipped portable-safe icon");
    Assert(safeBoxRuntime.Contains("AuraUiNativeHoverHint.Attach(buttonObject, HoverHint)", StringComparison.Ordinal),
        "safe box TopBar button registers a native-style portable-safe hover hint");

    var damageMeterRuntime = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/AuraToolsDamageMeterRuntime.cs");
    var damageMeterHookAdapter = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterHookAdapter.cs");
    var damageMeterCapture = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Capture/DamageCaptureCoordinator.cs");
    var damageCaptureSession = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Capture/DamageCaptureSession.cs");
    var damageEventFactory = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Capture/DamageEventFactory.cs");
    var damageMeterSettlement = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterSettlementRuntime.cs");
    var damageMeterAvailability = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterAvailabilityRuntime.cs");
    var damageMeterLifecycle = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterLifecycleCoordinator.cs");
    var damageMeterUiSource = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/AuraToolsDamageMeterUi.cs");
    var damageDetailsPresenter = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageDetailsPresenter.cs");
    var damageHudPresenter = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterHudPresenter.cs");
    var fightHistoryPresenter = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/FightDamageHistoryPresenter.cs");
    var outOfRunHistoryPresenter = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/OutOfRunDamageHistoryPresenter.cs");
    var damageMeterContextMapper = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterHookContextMapper.cs");
    var damageMeterHookRegistrations = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterHookRegistrationSet.cs");
    Assert(damageMeterRuntime.Contains("DamageMeterHookAdapter.Initialize", StringComparison.Ordinal)
           && damageMeterRuntime.Contains("DamageMeterAvailabilityRuntime.ReconcileAvailabilitySafe", StringComparison.Ordinal)
           && !damageMeterRuntime.Contains("ModHookContext", StringComparison.Ordinal)
           && !damageMeterRuntime.Contains("DamageFrameWindow<", StringComparison.Ordinal),
        "damage meter runtime stays a compatibility facade without hook or capture ownership");
    Assert(damageMeterLifecycle.Contains("ResetCaptureServices", StringComparison.Ordinal)
           && damageMeterLifecycle.Contains("DamageMeterNetworkRuntime.StartFight", StringComparison.Ordinal)
           && !damageMeterCapture.Contains("AuraToolsDamageMeterUi", StringComparison.Ordinal)
           && !damageMeterCapture.Contains("ModHookContext", StringComparison.Ordinal)
           && !damageMeterLifecycle.Contains("ModHookContext", StringComparison.Ordinal)
           && damageMeterContextMapper.Contains("MapHit", StringComparison.Ordinal)
           && damageMeterCapture.Contains("DamageEventFactory.Create", StringComparison.Ordinal)
           && damageEventFactory.Contains("DamageMeterProtocol.MaxDamagePerEvent", StringComparison.Ordinal),
        "damage meter hook context is mapped at the adapter while lifecycle and capture stay boundary-focused");
    Assert(damageMeterHookAdapter.Contains("DisposeAll", StringComparison.Ordinal)
           && damageMeterHookRegistrations.Contains("onFailure(key, ex)", StringComparison.Ordinal)
           && damageMeterHookRegistrations.Contains("registrations.Remove(key)", StringComparison.Ordinal),
        "damage meter hook release reports failures and retains failed handles for retry");
    Assert(!damageCaptureSession.Contains("BuffAttributionEngine", StringComparison.Ordinal)
           && !damageCaptureSession.Contains("DamageMeterFightIndex", StringComparison.Ordinal)
           && !damageCaptureSession.Contains("DamageMeterLifecycleCoordinator", StringComparison.Ordinal),
        "damage capture session owns frame state without reverse lifecycle or attribution dependencies");
    Assert(damageMeterUiSource.Contains("DamageDetailsPresenter.Show", StringComparison.Ordinal)
           && damageDetailsPresenter.Contains("RenderHeader", StringComparison.Ordinal)
           && damageDetailsPresenter.Contains("RenderSummary", StringComparison.Ordinal)
           && damageDetailsPresenter.Contains("RenderRows", StringComparison.Ordinal),
        "damage meter details rendering is delegated to a focused presenter");
    Assert(damageMeterUiSource.Contains("DamageMeterHudPresenter.Build", StringComparison.Ordinal)
           && damageHudPresenter.Contains("DamageMeterHudPresentation", StringComparison.Ordinal),
        "damage meter HUD calculations are delegated to a pure presenter");
    Assert(damageMeterUiSource.Contains("FightDamageHistoryPresenter.Show", StringComparison.Ordinal)
           && damageMeterSettlement.Contains("OutOfRunDamageHistoryPresenter.Show", StringComparison.Ordinal)
           && fightHistoryPresenter.Contains("DamageHistoryWindowRenderer.ShowHistory", StringComparison.Ordinal)
           && outOfRunHistoryPresenter.Contains("DamageHistoryWindowRenderer.ShowOutOfRunHistory", StringComparison.Ordinal),
        "fight and out-of-run history use separate presentation entry points");
    Assert(!damageMeterRuntime.Contains("EnsureOutOfRunHistoryLoaded();", StringComparison.Ordinal),
        "damage history load must be source-tagged and lazy");
    Assert(damageMeterRuntime.Contains("LoadHistoryOnStartup", StringComparison.Ordinal),
        "damage history startup load is guarded by config");
    Assert(damageMeterSettlement.Contains("CaptureTeamAvatars", StringComparison.Ordinal),
        "team avatar capture is explicitly configurable");
    Assert(damageMeterSettlement.Contains("AuraModeOutcomeRuntime.TryReadRecent", StringComparison.Ordinal)
           && damageMeterSettlement.Contains("sharedOutcome.IsCompleted", StringComparison.Ordinal)
           && !damageMeterSettlement.Contains("Terrias", StringComparison.Ordinal),
        "damage settlement consumes the generic run-scoped mode outcome without depending on a content mod");
    Assert(damageMeterSettlement.Contains("MaxAvatarEncodePixels", StringComparison.Ordinal)
           && damageMeterSettlement.Contains("MaxAvatarPngBytes", StringComparison.Ordinal),
        "team avatar capture has pixel and byte budgets");
    Assert(damageMeterRuntime.Contains("UiRefreshIntervalMs", StringComparison.Ordinal),
        "damage meter UI refresh is config-throttled");
    Assert(damageMeterRuntime.Contains("!Available || !Visible", StringComparison.Ordinal)
           && damageMeterSettlement.Contains("RestoreAdventureHistoryOnce", StringComparison.Ordinal)
           && !damageMeterRuntime.Contains("uiDirty = true;\r\n            return;", StringComparison.Ordinal)
           && !damageMeterRuntime.Contains("uiDirty = true;\n            return;", StringComparison.Ordinal),
        "damage meter idle UI and adventure-history work must be suppressed when unchanged");
    Assert(damageMeterCapture.Contains("DamageMeterPerformanceCounters.RecordHitHook", StringComparison.Ordinal)
           && damageMeterRuntime.Contains("DamageMeterPerformanceCounters.MaybeLog", StringComparison.Ordinal),
        "damage meter hot hooks must be observable through aggregated performance counters");
    Assert(damageCaptureSession.Contains("DamageFrameWindow<HitFrame>", StringComparison.Ordinal)
           && damageCaptureSession.Contains("ReleaseTargetFrameList", StringComparison.Ordinal)
           && damageCaptureSession.Contains("void Reset()", StringComparison.Ordinal),
        "damage meter capture frames must use bounded pooled frame windows");
    Assert(damageMeterHookAdapter.Contains("RegisterBefore(\"StatusManager.AddBuff\"", StringComparison.Ordinal)
           && damageCaptureSession.Contains("DamageFrameWindow<StatusBuffFrame>", StringComparison.Ordinal)
           && damageMeterCapture.Contains("RecordObservedApplication", StringComparison.Ordinal),
        "damage meter must capture direct StatusManager.AddBuff applications for broadcast-backed DoT attribution");

    var damageMeterNetwork = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Network/DamageMeterNetworkRuntime.cs");
    var damageMeterCommands = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Network/DamageMeterCommands.cs");
    Assert(damageMeterNetwork.Contains("FlushPendingSubmissions", StringComparison.Ordinal)
           && damageMeterCommands.Contains("DamageMeterSubmitBatchCommand", StringComparison.Ordinal),
        "damage meter networking batches submissions through the common pipeline");
    Assert(!damageMeterNetwork.Contains("PendingSubmitBatch.ToList", StringComparison.Ordinal)
           && !damageMeterNetwork.Contains("GetRange(offset, count)", StringComparison.Ordinal)
           && damageMeterNetwork.Contains("new List<DamageEvent>(count)", StringComparison.Ordinal),
        "damage meter batch flushing must avoid whole-batch and GetRange list copies");
    Assert(!damageMeterRuntime.Contains("Endless", StringComparison.OrdinalIgnoreCase)
           && !damageMeterNetwork.Contains("Endless", StringComparison.OrdinalIgnoreCase),
        "damage meter performance path must not special-case endless modes");

    var damageMeterCounters = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/DamageMeterPerformanceCounters.cs");
    Assert(damageMeterCounters.Contains("[DamageMeter:perf]", StringComparison.Ordinal)
           && damageMeterCounters.Contains("LogIntervalMs = 10000", StringComparison.Ordinal)
           && damageMeterCounters.Contains("AuraToolsPerformanceSettings.DiagnosticsEnabled", StringComparison.Ordinal),
        "damage meter performance counters must aggregate only while diagnostics are enabled");

    var performanceSettings = ReadRepoText("AuraToolsExp-Dev/Infrastructure/AuraToolsPerformanceSettings.cs");
    var auraToolsLog = ReadRepoText("AuraToolsExp-Dev/Infrastructure/AuraToolsLog.cs");
    var cardUiBenchmark = ReadRepoText("AuraToolsExp-Dev/Features/Diagnostics/AuraToolsCardUiBenchmarkRuntime.cs");
    Assert(performanceSettings.Contains("PerformanceDiagnostics", StringComparison.Ordinal)
           && auraToolsLog.Contains("public static void Performance", StringComparison.Ordinal)
           && cardUiBenchmark.Contains("if (!AuraToolsPerformanceSettings.DiagnosticsEnabled)", StringComparison.Ordinal)
           && damageMeterHookAdapter.Contains("if (AuraToolsPerformanceSettings.DiagnosticsEnabled)", StringComparison.Ordinal),
        "AuraTools pure benchmark and damage-text diagnostic hooks must be opt-in and use a dedicated visible log channel");

    var damageMeterFightIndex = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Resolution/DamageMeterFightIndex.cs");
    var damageMeterResolvers = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Resolution/DamageMeterResolvers.cs");
    Assert(damageMeterFightIndex.Contains("ResolveLabel", StringComparison.Ordinal)
           && damageMeterFightIndex.Contains("BuffFlags", StringComparison.Ordinal)
           && damageMeterResolvers.Contains("DamageMeterFightIndex.ResolveTeam", StringComparison.Ordinal),
        "damage meter resolver hot paths must route through the fight index cache");
    Assert(damageMeterFightIndex.Contains("EnsureGameDataCacheEpoch", StringComparison.Ordinal)
           && damageMeterFightIndex.Contains("snapshot.Version.NativeReady", StringComparison.Ordinal),
        "damage meter labels and Buff classification do not retain pre-ready negative lookups");

    Assert(damageMeterFightIndex.Contains("FriendlyIdentityIds", StringComparison.Ordinal)
           && damageMeterFightIndex.Contains("RegisterFriendlyIdentity", StringComparison.Ordinal)
           && damageMeterFightIndex.Contains("IsKnownFriendlyIdentity", StringComparison.Ordinal),
        "damage meter must keep player identity authoritative over transformed native combatant shape");
    Assert(damageMeterFightIndex.Contains("FightManager.Instance?.roleQueue", StringComparison.Ordinal)
           && !damageMeterFightIndex.Contains("RoleStatusMap", StringComparison.Ordinal)
           && damageMeterFightIndex.Contains("preferredTeam == DamageTeam.Enemy", StringComparison.Ordinal),
        "damage meter faction indexing must use the real player roster and preserve enemy precedence");
    Assert(damageMeterFightIndex.Contains("OwnerPlayerId", StringComparison.Ordinal)
           && damageMeterFightIndex.Contains("OwnerStatusId", StringComparison.Ordinal)
           && damageMeterResolvers.Contains("ResolveAttribution", StringComparison.Ordinal),
        "damage meter must fold generic owned companions into their explicit player owner");

    var audioArbiter = ReadRepoSourceTree("AudioArbiterShared");
    var audioNetworkPolicy = ReadRepoText("AudioArbiterShared/AudioNetworkPolicy.cs");
    var audioNetworkSession = ReadRepoText("AudioArbiterShared/AudioNetworkSessionState.cs");
    var audioNetworkRuntime = ReadRepoText("AudioArbiterShared/AudioNetworkRuntime.cs");
    var audioHookCatalog = ReadRepoText("AudioArbiterShared/AudioHookCatalog.cs");
    var audioHookAdapter = ReadRepoText("AudioArbiterShared/AudioHookAdapter.cs");
    var audioHookModels = ReadRepoText("AudioArbiterShared/AudioHookModels.cs");
    var audioGameStateReader = ReadRepoText("AudioArbiterShared/AudioGameStateReader.cs");
    var audioHookContextMapper = ReadRepoText("AudioArbiterShared/AudioHookContextMapper.cs");
    var audioLowHealthCoordinator = ReadRepoText("AudioArbiterShared/AudioLowHealthCoordinator.cs");
    var audioRequestFactory = ReadRepoText("AudioArbiterShared/AudioRequestFactory.cs");
    var audioProviderAdapter = ReadRepoText("AudioArbiterShared/AudioProviderAdapter.cs");
    var audioFileProvider = ReadRepoText("AudioArbiterShared/AudioFileSoundProvider.cs");
    var auraToolsAudio = ReadRepoText("AuraToolsExp-Dev/Features/Audio/AuraToolsAudioRuntime.cs");
    Assert(audioArbiter.Contains("RpcAudioPresentationRequest", StringComparison.Ordinal)
           && audioArbiter.Contains("IAudioArbiterServerBoundRpcCommand", StringComparison.Ordinal)
           && audioArbiter.Contains("SenderOwnsStatus", StringComparison.Ordinal)
           && audioArbiter.Contains("DefaultPresentationMaxAgeMilliseconds", StringComparison.Ordinal)
           && auraToolsAudio.Contains("presentationRelay=client-request-host-authorized", StringComparison.Ordinal),
        "card-use audio must use a client-requested, host-authorized bounded presentation relay");
    Assert(audioNetworkPolicy.Contains("ValidateServerCardUsePresentation", StringComparison.Ordinal)
           && audioNetworkPolicy.Contains("ValidateLocalPresentationIdentity", StringComparison.Ordinal)
           && audioNetworkPolicy.Contains("expired presentation", StringComparison.Ordinal)
           && audioNetworkSession.Contains("receivedEventOrder.Count > maximumPlaybackClaims", StringComparison.Ordinal)
           && audioNetworkRuntime.Contains("AudioNetworkSenderSnapshot", StringComparison.Ordinal)
           && audioNetworkRuntime.Contains("RegisterAuthority", StringComparison.Ordinal)
           && audioNetworkRuntime.Contains("AuraRpcAuthorityRuntime.Register", StringComparison.Ordinal)
           && audioNetworkRuntime.Contains("ApplyServerCardUsePresentation", StringComparison.Ordinal)
           && audioNetworkRuntime.Contains("SendRpcCommand", StringComparison.Ordinal),
        "card-use audio authority, session state, sender validation, and RPC transport must stay in the network boundary");
    Assert(audioProviderAdapter.Contains("class SoundProviderHandle", StringComparison.Ordinal)
           && audioProviderAdapter.Contains("GetMethod(\"GetClip\"", StringComparison.Ordinal)
           && audioFileProvider.Contains("class FileSoundProvider", StringComparison.Ordinal)
           && audioFileProvider.Contains("UnityWebRequestMultimedia.GetAudioClip", StringComparison.Ordinal),
        "audio provider reflection and file-loading lifecycle must stay in dedicated adapters");
    Assert(audioHookCatalog.Contains("class AudioHookCatalog", StringComparison.Ordinal)
           && audioHookCatalog.Contains("AudioHookRegistrationKind.CombatActionBefore", StringComparison.Ordinal)
           && audioHookCatalog.Contains("AudioHookCallbackKind.PotentialHpChanged", StringComparison.Ordinal)
           && audioHookCatalog.Contains("ScriptExecutor.OnlineDamage", StringComparison.Ordinal)
           && audioHookAdapter.Contains("class AudioHookAdapter", StringComparison.Ordinal)
           && audioHookAdapter.Contains("AudioHookCatalog.All", StringComparison.Ordinal)
           && audioHookAdapter.Contains("new AuraHookRegistry", StringComparison.Ordinal)
           && audioHookAdapter.Contains("AuraCombatActionRouter.RegisterBefore", StringComparison.Ordinal)
           && audioHookAdapter.Contains("public void Dispose()", StringComparison.Ordinal)
           && audioHookModels.Contains("class AudioCombatActionObservation", StringComparison.Ordinal)
           && audioHookModels.Contains("class AudioStatusSnapshot", StringComparison.Ordinal)
           && audioGameStateReader.Contains("class AudioGameStateReader", StringComparison.Ordinal)
           && audioGameStateReader.Contains("ReadFightStatusSnapshots", StringComparison.Ordinal)
           && audioHookContextMapper.Contains("class AudioHookContextMapper", StringComparison.Ordinal)
           && audioHookContextMapper.Contains("MapExecutorHpChanges", StringComparison.Ordinal)
           && audioLowHealthCoordinator.Contains("class AudioLowHealthCoordinator", StringComparison.Ordinal)
           && audioLowHealthCoordinator.Contains("AudioLowHealthObservationDecision", StringComparison.Ordinal)
           && audioLowHealthCoordinator.Contains("ConfigureProviders", StringComparison.Ordinal)
           && audioLowHealthCoordinator.Contains("RememberNoProvider", StringComparison.Ordinal)
           && audioRequestFactory.Contains("CreateCombatActionBatch", StringComparison.Ordinal)
           && audioRequestFactory.Contains("CreateLowHealth", StringComparison.Ordinal)
           && !audioHookContextMapper.Contains("PlayerManager.Instance", StringComparison.Ordinal)
           && !audioHookContextMapper.Contains("new SoundPlaybackRequest", StringComparison.Ordinal)
           && !audioRequestFactory.Contains("ModHookContext", StringComparison.Ordinal),
        "audio hook, game-state, context, low-health, and request-shape contracts must stay in dedicated boundaries");
    Assert(audioArbiter.Contains("AudioArbiterRuntime.ReceiveRemote(Event)", StringComparison.Ordinal)
           && audioArbiter.Contains("RpcAudioFightSession", StringComparison.Ordinal)
           && audioArbiter.Contains("MaximumPlaybackClaims = 512", StringComparison.Ordinal)
           && audioArbiter.Contains("TryClaimPresentation", StringComparison.Ordinal)
           && audioArbiter.Contains("receivedEventOrder.Dequeue", StringComparison.Ordinal),
        "card-use audio RPC playback must enter the fight-scoped bounded claim ledger");
    Assert(audioArbiter.Contains("AudioPresentationPolicy.CreatePlan", StringComparison.Ordinal)
           && audioArbiter.Contains("presentationPlan.QueueNativeEffectReplacement", StringComparison.Ordinal)
           && !audioArbiter.Contains("&& !request.IsRemote\r\n                    && string.Equals(request.Kind, SoundEventKinds.CardUse", StringComparison.Ordinal)
           && !audioArbiter.Contains("&& !request.IsRemote\n                    && string.Equals(request.Kind, SoundEventKinds.CardUse", StringComparison.Ordinal),
        "remote card-use replacement audio must pair with the native effect instead of direct-playing a second clip");
    Assert(audioArbiter.Contains("RemoteReplacementPairingSeconds = 0.15f", StringComparison.Ordinal)
           && audioArbiter.Contains("PlayRemoteReplacementFallback", StringComparison.Ordinal)
           && audioArbiter.Contains("remote-fallback-played", StringComparison.Ordinal)
           && audioArbiter.Contains("fallback-original-suppressed", StringComparison.Ordinal)
           && audioArbiter.Contains("AudioReplacementCoordinator", StringComparison.Ordinal)
           && audioArbiter.Contains("TryClaimPairedFallback", StringComparison.Ordinal),
        "remote replacement audio must fall back once when no native effect pairs, then suppress a late original");
    Assert(audioArbiter.Contains("Card-use presentation outcome: outcome=", StringComparison.Ordinal)
           && audioArbiter.Contains("cardId=", StringComparison.Ordinal)
           && audioArbiter.Contains("provider=", StringComparison.Ordinal)
           && audioArbiter.Contains("policy=", StringComparison.Ordinal),
        "card-use audio diagnostics must identify resolution and playback outcome");

    var buffAttribution = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Resolution/BuffAttributionEngine.cs");
    Assert(buffAttribution.Contains("class BuffAttributionEngine", StringComparison.Ordinal)
           && buffAttribution.Contains("EmitSplit", StringComparison.Ordinal)
           && buffAttribution.Contains("AddUnknown", StringComparison.Ordinal)
           && buffAttribution.Contains("RefinePendingApplication", StringComparison.Ordinal)
           && buffAttribution.Contains("ObserveBroadcast", StringComparison.Ordinal)
           && buffAttribution.Contains("RecentBuffBroadcast", StringComparison.Ordinal)
           && buffAttribution.Contains("TakeRecentBroadcast", StringComparison.Ordinal)
           && buffAttribution.Contains("MarkPendingCommitted", StringComparison.Ordinal)
           && buffAttribution.Contains("CommittedUnits", StringComparison.Ordinal)
           && !buffAttribution.Contains("HasPendingApplication", StringComparison.Ordinal)
           && buffAttribution.Contains("MaxLotsPerState = 128", StringComparison.Ordinal)
           && buffAttribution.Contains("ConsumeOldest", StringComparison.Ordinal)
           && buffAttribution.Contains("AppendLot", StringComparison.Ordinal)
           && buffAttribution.Contains("CollapseLots", StringComparison.Ordinal)
           && buffAttribution.Contains("ReconcileForEmission", StringComparison.Ordinal)
           && !buffAttribution.Contains("ReconcileLevel", StringComparison.Ordinal)
           && buffAttribution.Contains("AppendTarget(executor.status", StringComparison.Ordinal)
           && buffAttribution.Contains("ConfidenceRank", StringComparison.Ordinal)
           && !buffAttribution.Contains("using System.Linq", StringComparison.Ordinal),
        "buff attribution must use the transaction/state-slot engine without LINQ hot-path allocation");
    Assert(damageMeterCapture.Contains("EventCenter.OnBroadcastEventWithParam", StringComparison.Ordinal)
           && damageMeterCapture.Contains("param is not AddBuffData", StringComparison.Ordinal),
        "buff attribution must refine applications from native AddBuffData broadcasts");

    var damageMeterUi = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/AuraToolsDamageMeterUi.cs");
    Assert(damageMeterUi.Contains("SetTextIfChanged", StringComparison.Ordinal)
           && damageMeterUi.Contains("ShowCurrentDetails", StringComparison.Ordinal)
           && !damageMeterUi.Contains("details.onClick.AddListener(()", StringComparison.Ordinal),
        "damage meter UI rows must refresh by diff and bind click listeners once");
    Assert(damageMeterAvailability.Contains("AuraToolsConfigService.MatchExperience.DamageMeter.ShowPanelByDefault", StringComparison.Ordinal)
           && damageMeterUi.Contains("var panelVisible = available && AuraToolsDamageMeterRuntime.Visible", StringComparison.Ordinal),
        "damage meter availability must preserve the normalized collapsed-by-default presentation state");

    var damageRunLedger = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Model/DamageRunLedger.cs");
    Assert(damageRunLedger.Contains("grandTotalCacheVersion", StringComparison.Ordinal)
           && damageRunLedger.Contains("hasDamageCacheVersion", StringComparison.Ordinal),
        "damage run aggregate totals must be cached for UI refreshes");

    var historyPersistence = ReadRepoText("AuraToolsExp-Dev/Features/DamageMeter/Network/OutOfRunDamageHistoryPersistence.cs");
    Assert(historyPersistence.Contains("NormalizeMaxEnvelopeBytes", StringComparison.Ordinal)
           && historyPersistence.Contains("encrypted envelope too large", StringComparison.Ordinal),
        "out-of-run history persistence enforces envelope budgets");

    var skillCgRuntime = ReadRepoText("AuraToolsExp-Dev/Features/SkillCg/AuraToolsSkillCgRuntime.cs");
    var actionRouter = ReadRepoText("AuraSharedCore/AuraCombatActionRouter.cs");
    Assert(skillCgRuntime.Contains("AuraCombatActionRouter.RegisterBefore", StringComparison.Ordinal)
           && actionRouter.Contains("safeInvoke: true", StringComparison.Ordinal),
        "SkillCG hooks must route through isolated shared action dispatch");
    Assert(skillCgRuntime.Contains("safeModeDisabled", StringComparison.Ordinal)
           && skillCgRuntime.Contains("RunHook(", StringComparison.Ordinal),
        "SkillCG hooks must have runtime failure isolation");
    Assert(!skillCgRuntime.Contains("PreloadOnFightStart", StringComparison.Ordinal),
        "SkillCG must not preload registered CG during fight start");

    var feastManual = ReadRepoText("AuraToolsExp-Dev/Features/Feast/AuraToolsFeastManualResourceStore.cs");
    var feastRuntime = ReadRepoText("AuraToolsExp-Dev/Features/Feast/AuraToolsFeastRuntime.cs");
    var feastRoleEditor = ReadRepoText("AuraToolsExp-Dev/Features/Feast/AuraToolsFeastRoleEditor.cs");
    var skinRuntime = ReadRepoText("AuraToolsExp-Dev/Features/Skin/AuraToolsSkinRuntime.cs");
    var skinEditor = ReadRepoText("AuraToolsExp-Dev/Features/Skin/AuraToolsSkinEditor.cs");
    var settingsUi = ReadRepoText("AuraToolsExp-Dev/Features/Settings/AuraToolsUi.cs");
    var cgCatalogQuery = ReadRepoText("AuraCgShared/AuraCgCatalogQueryService.cs");
    var cgCandidateSelector = ReadRepoText("AuraCgShared/AuraCgCandidateSelector.cs");
    var feastRoleCatalog = ReadRepoText("AuraToolsExp-Dev/Infrastructure/RoleCatalog.cs");
    var cgRegistry = ReadRepoText("AuraCgShared/AuraCgRegistry.cs");
    var feastPackage = ReadRepoText("AuraToolsExp/SharedResources/aura.registration.json");
    var feastRegistry = ReadRepoText("AuraToolsExp/SharedResources/cg.registry.json");
    Assert(feastRoleCatalog.Contains("AuraRoleRegistryRuntime.GetEffectiveSnapshot", StringComparison.Ordinal)
           && !feastRoleCatalog.Contains("AuraRoleRegistryRuntime.PublishRuntimeRoles", StringComparison.Ordinal)
           && feastManual.Contains("AuraSharedResourceProtocol.UpsertManualResource", StringComparison.Ordinal)
           && feastManual.Contains("AuraSharedOriginKinds.UserManual", StringComparison.Ordinal),
        "Feast role discovery and manual imports use the shared v4 authorities");
    Assert(feastRoleCatalog.Contains("AuraRoleRegistryRuntime.GetEffectiveSnapshot", StringComparison.Ordinal)
           && feastRoleCatalog.Contains("MatchesRole", StringComparison.Ordinal)
           && !feastRoleEditor.Contains("AuraToolsFeastRuntime.RegisteredRoleIds()", StringComparison.Ordinal)
           && !feastRoleEditor.Contains("MatchExperience.Feast.Roles", StringComparison.Ordinal),
        "Feast only displays active role identities and uses aliases for resource applicability");
    Assert(!feastManual.Contains("\"Overrides\"", StringComparison.Ordinal)
           && feastManual.Contains("AuraSharedResourcePathPolicy.StorageResourcePath", StringComparison.Ordinal),
        "manual Feast resources use the same bounded physical hierarchy as registered resources");
    Assert(feastRuntime.Contains("AuraCgCatalogQueryService.QueryRegisteredResources", StringComparison.Ordinal)
           && !feastRuntime.Contains("AuraCgRegistryRuntime", StringComparison.Ordinal)
           && cgCatalogQuery.Contains("QueryCatalog", StringComparison.Ordinal)
           && cgCatalogQuery.Contains("entry.CanonicalPath", StringComparison.Ordinal),
        "Feast runtime discovery must use active and available v4 Catalog resources without a legacy CG registry join");
    Assert(feastRuntime.Contains("AuraSharedResourceProtocol.ReadUserOverride", StringComparison.Ordinal)
           && feastRuntime.Contains("AuraSharedResourceProtocol.WriteUserOverride", StringComparison.Ordinal)
           && feastRuntime.Contains("resourceOverrides", StringComparison.Ordinal)
           && feastRuntime.Contains("manualResources", StringComparison.Ordinal)
           && feastRoleEditor.Contains("SetCandidateEnabledForRole", StringComparison.Ordinal),
        "Feast role choices must persist through the role scope aura.user.json instead of a tool-only selection path");
    Assert(feastRuntime.Contains("registered.Count == 0", StringComparison.Ordinal)
           && feastRuntime.Contains("FeastCgSourceKind.Default", StringComparison.Ordinal)
           && feastRuntime.Contains("FeastCgSourceKind.Manual", StringComparison.Ordinal)
           && feastRuntime.Contains("!IsToolProvidedDefault(resource)", StringComparison.Ordinal),
        "Feast default visibility depends only on successful non-tool registration and is independent of manual resources");
    Assert(cgCandidateSelector.Contains("public const string Random = \"random\"", StringComparison.Ordinal)
           && cgCandidateSelector.Contains("public const string Sequential = \"sequential\"", StringComparison.Ordinal)
           && feastRuntime.Contains("AuraCgCandidateSelector.Select", StringComparison.Ordinal),
        "Feast multi-enable selection must share one priority/random/sequential candidate selector");
    Assert(feastPackage.Contains("\"schemaVersion\": 4", StringComparison.Ordinal)
           && feastPackage.Contains("\"participantKind\": \"Tool\"", StringComparison.Ordinal)
           && feastPackage.Contains("\"scopeOwnerModId\"", StringComparison.Ordinal)
           && feastPackage.Contains("\"originKind\"", StringComparison.Ordinal)
           && !feastPackage.Contains("legacyPaths", StringComparison.Ordinal)
           && feastRegistry.Contains("CG/Role/", StringComparison.Ordinal)
           && feastRegistry.Contains("/Feast/AuraToolsExp/", StringComparison.Ordinal)
           && !feastRegistry.Contains("CG/AuraToolsExp/Feast/Roles/", StringComparison.Ordinal),
        "packaged feast defaults must use explicit v4 role ownership and provenance");
    Assert(feastManual.Contains("TryPrepareCanonicalPng", StringComparison.Ordinal)
           && feastManual.Contains("StageTemporary", StringComparison.Ordinal)
           && feastManual.Contains("ImageConversion.EncodeToPNG", StringComparison.Ordinal),
        "feast image import must validate PNG and normalize JPG/JPEG through the shared temporary-resource boundary");
    Assert(settingsUi.Contains("CreateFixedScroll", StringComparison.Ordinal)
           && feastRoleEditor.Contains("CreateScroll", StringComparison.Ordinal)
           && feastRoleEditor.Contains("AuraTools.SharedResourceHistory", StringComparison.Ordinal)
           && feastRoleEditor.Contains("editorRoot", StringComparison.Ordinal)
           && skinEditor.Contains("CreateFixedScroll", StringComparison.Ordinal),
        "Feast uses flexible non-nested overlays with a history view while skin keeps its bounded viewport");
    Assert(skinRuntime.Contains("ConfigureCandidateOverrides", StringComparison.Ordinal)
           && skinRuntime.Contains("\"ManualSelection\"", StringComparison.Ordinal)
           && skinRuntime.Contains("resourceOverrides", StringComparison.Ordinal)
           && skinEditor.Contains("管理待选", StringComparison.Ordinal),
        "skin management must remain ManualSelection with per-role sparse candidate overrides");

    var starterDeckRuntime = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/AuraToolsStarterDeckRuntime.cs");
    var starterDeckModule = ReadRepoSourceTree("AuraToolsExp-Dev/Features/StarterDeck");
    var starterDeckHookAdapter = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/StarterDeckHookAdapter.cs");
    var starterDeckApplication = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/StarterDeckApplicationCoordinator.cs");
    var starterDeckCatalog = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/StarterDeckCardCatalog.cs");
    Assert(starterDeckRuntime.Contains("StarterDeckHookAdapter.Initialize", StringComparison.Ordinal)
           && !starterDeckRuntime.Contains("ModHookContext", StringComparison.Ordinal)
           && !starterDeckRuntime.Contains("RoleTable", StringComparison.Ordinal),
        "starter deck runtime stays an initialization and compatibility facade");
    var starterDeckClassification = ReadRepoText("AuraToolsExp-Dev/Features/StarterDeck/StarterDeckCardClassification.cs");
    Assert(starterDeckHookAdapter.Contains("RegisterBefore(modConfig, \"PlayerManager.CmdSyncRoleTable\"", StringComparison.Ordinal)
           && starterDeckHookAdapter.Contains("ApplyStarterDeckBeforeRoleSubmit", StringComparison.Ordinal)
           && starterDeckHookAdapter.Contains("context.Arguments?.OfType<RoleTable>().FirstOrDefault()", StringComparison.Ordinal),
        "starter deck must apply the local role-table argument before each client submits it natively");
    var registryAwareSkillCgRuntime = ReadRepoText("AuraToolsExp-Dev/Features/SkillCg/AuraToolsSkillCgRuntime.cs");
    Assert(registryAwareSkillCgRuntime.Contains("AuraCgRegistryRuntime.Changed += OnRegistryChanged", StringComparison.Ordinal)
           && registryAwareSkillCgRuntime.Contains("EnsureRegistryStateCurrent", StringComparison.Ordinal)
           && registryAwareSkillCgRuntime.Contains("AuraCgRegistryRuntime.GetSnapshot()", StringComparison.Ordinal),
        "Skill CG effective configuration must refresh from the current shared registry revision, not a fixed load phase");
    var sharedCgRuntime = ReadRepoSourceTree("AuraCgShared");
    Assert(sharedCgRuntime.Contains("registeredRequestResolver(item, false)", StringComparison.Ordinal)
           && sharedCgRuntime.Contains("registeredRequestResolver(item, true)", StringComparison.Ordinal),
        "Skill CG server validation must be independent of the host visual toggle while recipients apply local activation");
    Assert(!starterDeckModule.Contains("NormalMapManager.InitRoleTable", StringComparison.Ordinal),
        "starter deck must not write a provisional deck during early role-table initialization");
    Assert(starterDeckApplication.Contains("ReadDataId(roleTable.Career)", StringComparison.Ordinal)
           && !starterDeckModule.Contains("GameEntryUI.career", StringComparison.Ordinal),
        "starter deck multiplayer role resolution must use the owned role table instead of global lobby selection state");
    Assert(starterDeckApplication.Contains("IsLocalPlayerRoleTable", StringComparison.Ordinal)
           && starterDeckApplication.Contains("playerManager.PlayerId", StringComparison.Ordinal)
           && starterDeckApplication.Contains("ReflectionUtil.ReadString(roleTable, \"Id\", \"id\")", StringComparison.Ordinal),
        "starter deck multiplayer path must guard by local player role-table ownership");
    Assert(!starterDeckModule.Contains("multiplayer world-simulation keeps native per-player decks", StringComparison.Ordinal),
        "starter deck must not skip the whole feature for multiplayer world-simulation runs");
    Assert(starterDeckCatalog.Contains("BuildCareerSkillCardIds", StringComparison.Ordinal)
           && starterDeckCatalog.Contains("gameConfig.GetPackBelong", StringComparison.Ordinal)
           && starterDeckCatalog.Contains("IsExcludedDerivedCard", StringComparison.Ordinal),
        "starter deck catalog uses authoritative career references, host pack ownership, and independent derived-card exclusion");
    Assert(!starterDeckModule.Contains("hasSkillAction", StringComparison.Ordinal)
           && !starterDeckModule.Contains("hasSkillIcon", StringComparison.Ordinal)
           && !starterDeckModule.Contains("IsSkillLikeCard", StringComparison.Ordinal),
        "starter deck classification must not infer career skills from Action or icon presentation fields");
    Assert(starterDeckClassification.Contains("\"衍生牌\"", StringComparison.Ordinal)
           && !starterDeckClassification.Contains("Terrias_wuna_wuna_coronation_token", StringComparison.Ordinal),
        "derived-card filtering is semantic and does not make AuraTools depend on a Terrias content id");
    var wunaCardText = ReadRepoText("Terrias/Text/Card/wuna.csv");
    Assert(wunaCardText.Contains("*wuna_coronation_token,TRUE,衍生牌", StringComparison.Ordinal),
        "Radiance Coronation keeps the content-owned derived-card marker consumed by the generic filter");

    var matchSettings = ReadRepoText("AuraToolsExp/Config/MatchExperienceSettings.json");
    Assert(matchSettings.Contains("\"showPanelByDefault\": false", StringComparison.Ordinal)
           && matchSettings.Contains("\"loadHistoryOnStartup\": false", StringComparison.Ordinal)
           && matchSettings.Contains("\"captureTeamAvatars\": false", StringComparison.Ordinal)
           && matchSettings.Contains("\"uiRefreshIntervalMs\": 1000", StringComparison.Ordinal)
           && matchSettings.Contains("\"submitBatchIntervalMs\": 250", StringComparison.Ordinal)
           && matchSettings.Contains("\"maxEventsPerBatch\": 24", StringComparison.Ordinal),
        "packaged damage meter config defaults to a collapsed panel, lazy history, no hot-path avatar capture, throttled UI, and batched networking");

    var loggingSettings = ReadRepoText("AuraToolsExp/Config/LoggingSettings.json");
    Assert(loggingSettings.Contains("\"minimumLevel\": \"Info\"", StringComparison.Ordinal)
           && loggingSettings.Contains("\"performanceDiagnostics\": false", StringComparison.Ordinal)
           && loggingSettings.Contains("\"mirrorUnityLog\": false", StringComparison.Ordinal)
           && loggingSettings.Contains("\"mirrorCommandsLog\": false", StringComparison.Ordinal),
        "packaged logging config keeps AuraTools lifecycle logs visible without high-volume mirrors");

    var skillCgSettings = ReadRepoText("AuraToolsExp/Config/SkillCgSettings.json");
    Assert(!skillCgSettings.Contains("\"preloadOnFightStart\"", StringComparison.Ordinal)
           && skillCgSettings.Contains("\"disableAfterFailures\": true", StringComparison.Ordinal),
        "packaged SkillCG config uses adventure preload and keeps failure fuse enabled");
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

void TestBestHitAndScientificFormat()
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

void TestOutOfRunHistoryBuilder()
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

void TestSafeBoxDataCompatibility()
{
    var sparse = new Dictionary<string, string>
    {
        ["Name"] = "Sparse"
    };
    var vars = new Dictionary<string, string>
    {
        ["Id"] = "custom_card"
    };

    Assert(AuraToolsSafeBoxDataCompatibility.TryCreateSafeCardData(
            sparse,
            vars,
            out var safeSparse,
            out var sparseId,
            out var sparseChanged),
        "sparse SafeBox card data is repairable");
    Assert(sparseChanged
           && sparseId == "custom_card"
           && safeSparse["Id"] == "custom_card"
           && safeSparse["Expend"] == AuraToolsSafeBoxDataCompatibility.DefaultExpend
           && safeSparse["Icon"] == AuraToolsSafeBoxDataCompatibility.DefaultIcon
           && safeSparse["Description"] == "",
        "sparse SafeBox card data receives required UI fields");

    var complete = new Dictionary<string, string>
    {
        ["Id"] = "card",
        ["Name"] = "Card",
        ["Expend"] = "2",
        ["Tag"] = "",
        ["Icon"] = AuraToolsSafeBoxDataCompatibility.DefaultIcon,
        ["Rarity"] = "1",
        ["Description"] = "done"
    };

    Assert(!AuraToolsSafeBoxDataCompatibility.TryCreateSafeCardData(
            complete,
            null,
            out var safeComplete,
            out var completeId,
            out var completeChanged),
        "complete SafeBox card data is left unchanged");
    Assert(!completeChanged && completeId == "card" && safeComplete["Expend"] == "2",
        "complete SafeBox card data keeps original values");
}

void TestStarterDeckCardClassification()
{
    var careerRows = new List<IDictionary<string, string>>
    {
        new Dictionary<string, string>
        {
            ["Id"] = "career_1",
            ["SkillScript"] = "not_a_card_id",
            ["Skill1"] = "careercard_1",
            ["Skill2"] = "custom_skill_a; custom_skill_b|custom_skill_c"
        }
    };
    var careerSkillCardIds = StarterDeckCardClassification.BuildCareerSkillCardIds(careerRows);
    Assert(careerSkillCardIds.SetEquals(new[]
        {
            "careercard_1",
            "custom_skill_a",
            "custom_skill_b",
            "custom_skill_c"
        }),
        "starter deck career skills come from numbered Career.SkillN references only");

    var ordinaryActionSkillCardIds = new[]
    {
        "burningcard_1",
        "burningcard_2",
        "burningcard_3",
        "burningcard_4",
        "card_13",
        "card_15",
        "card_9",
        "healcard_7",
        "perceivecard_6"
    };
    foreach (var cardId in ordinaryActionSkillCardIds)
    {
        var row = new Dictionary<string, string>
        {
            ["Id"] = cardId,
            ["Action"] = "Skill",
            ["Type"] = cardId == "card_13" ? "消耗攻击牌" : "技能牌"
        };
        Assert(!StarterDeckCardClassification.ShouldExcludeFromStarterDeck(cardId, row, careerSkillCardIds),
            cardId + " Action=Skill remains a normal starter-deck card");
        Assert(StarterDeckCardClassification.ResolveEffectivePackId(row)
               == StarterDeckCardClassification.DefaultCardPackId,
            cardId + " inherits the host default card pack");
    }

    var careerSkillRow = new Dictionary<string, string>
    {
        ["Id"] = "careercard_1",
        ["Action"] = "Attack",
        ["Type"] = "职业技能"
    };
    Assert(StarterDeckCardClassification.ShouldExcludeFromStarterDeck(
            "careercard_*1",
            careerSkillRow,
            careerSkillCardIds),
        "Career.SkillN reference excludes a career skill regardless of Action");

    var coronationToken = new Dictionary<string, string>
    {
        ["Id"] = "Terrias_wuna_wuna_coronation_token",
        ["Action"] = "Skill",
        ["Type"] = "衍生牌"
    };
    Assert(!StarterDeckCardClassification.IsCareerSkillCard(
            coronationToken["Id"],
            careerSkillCardIds),
        "Radiance Coronation is not mislabeled as a career skill");
    Assert(StarterDeckCardClassification.IsExcludedDerivedCard(coronationToken)
           && StarterDeckCardClassification.ShouldExcludeFromStarterDeck(
               coronationToken["Id"],
               coronationToken,
               careerSkillCardIds),
        "Radiance Coronation is independently excluded as a derived card");

    var explicitPack = new Dictionary<string, string> { ["PackBelong"] = " cardpack_7 " };
    Assert(StarterDeckCardClassification.ResolveEffectivePackId(explicitPack) == "cardpack_7",
        "explicit card-pack ownership is preserved");
    Assert(StarterDeckCardClassification.ResolveEffectivePackId(
               new Dictionary<string, string>(),
               _ => "cardpack_host") == "cardpack_host",
        "host card-pack resolution takes precedence");
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

string ReadRepoText(string relativePath)
{
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("Required repo file is missing.", path);
    }

    return File.ReadAllText(path);
}

string ReadRepoSourceTree(string relativeDirectory)
{
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var directory = Path.Combine(repoRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
    if (!Directory.Exists(directory))
    {
        throw new DirectoryNotFoundException("Required repo source directory is missing: " + directory);
    }

    var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (files.Length == 0)
    {
        throw new InvalidOperationException("Required repo source directory has no C# files: " + directory);
    }

    return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
}

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }

    assertions++;
}

void TestStarterDeckDeckBuilder()
{
    var valid = new HashSet<string>(new[] { "a", "b", "c", "d" }, StringComparer.OrdinalIgnoreCase);
    var excluded = new HashSet<string>(new[] { "b" }, StringComparer.OrdinalIgnoreCase);
    var deck = StarterDeckDeckBuilder.Build(
        new[] { "", "a", "missing", "b" },
        3,
        valid.Contains,
        excluded.Contains,
        new[] { "c", "d" });
    Assert(deck.SequenceEqual(new[] { "a", "c", "d" }),
        "starter deck builder preserves configured order and fills only valid non-excluded cards");

    var bounded = StarterDeckDeckBuilder.Build(
        new[] { "a", "c", "d" },
        2,
        valid.Contains,
        excluded.Contains,
        new[] { "d" });
    Assert(bounded.SequenceEqual(new[] { "a", "c" }),
        "starter deck builder enforces deck size before fallback expansion");

    var compatible = StarterDeckDeckBuilder.Build(
        new[] { "short_a", "legacy_b" },
        2,
        valid.Contains,
        excluded.Contains,
        Array.Empty<string>(),
        id => id == "short_a" ? "a" : id == "legacy_b" ? "b" : id);
    Assert(compatible.SequenceEqual(new[] { "a" }),
        "starter deck builder validates and applies resolved runtime card ids");
}

internal sealed class TestCaptureFrame : IDamageCaptureFrame
{
    public int Frame { get; set; }

    public int Value { get; set; }

    public void Reset()
    {
        Frame = 0;
        Value = 0;
    }
}

internal sealed class TestDisposable : IDisposable
{
    private readonly Action dispose;

    internal TestDisposable(Action dispose) => this.dispose = dispose;

    public void Dispose() => dispose();
}
