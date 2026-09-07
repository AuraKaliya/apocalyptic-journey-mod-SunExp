using Terrias.Dll.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.UI.Window;

internal static partial class Program
{
    private static int assertions;
    private const string WhiteRadiance = "\u767d\u66dc";

    private static void Main()
    {
        TestDictionaryUtil();
        TestLedgerCommitFailures();
        TestProjectionWireIdentity();
        TestRuntimeMemberApi();
        TestTerriasLocalizationValues();
        TestCardCostHelpers();
        TestGoldDreamRules();
        TestBuffPresentationDependencies();
        TestLegacyBattleHookVarMigration();
        TestActionPassiveRegistry();
        TestScriptDelegateBinding();
        TestCardInvalidationContract();
        TestActiveCardPresentationCoverage();
        TestStarBlessingCostOverrideStore();
        TestResonanceCostTransactionStore();
        TestSolarFlameSealFormula();
        TestMorningStarRelicFormula();
        TestMorningStarBlessingFormula();
        TestMorningStarCurseFormula();
        TestSunCardPackSelectionMigration();
        TestCardGrantRequest();
        TestCombatCardTerminalBoundary();
        TestPerformanceSettings();
        TestCardMutationService();
        TestRuntimeCardAttachmentService();
        TestSolarTriggerCostOverride();
        TestWhiteRadianceTags();
        TestTemporaryWhiteRadianceClaim();
        TestSolarMemoryIsolationIds();
        TestSolarMemoryFixedNodeCatalog();
        TestSolarMemoryMapSyncRepair();
        TestSolarMemoryContentIsolation();
        TestSolarMemoryMapPreviewPolicy();
        TestMapNodeTextureFitService();
        TestModeChoiceDragRange();
        TestSpiritManagementSelectionState();
        TestSpiritElementDisplayPolicy();
        TestSpiritWarehouseSelectionPolicy();
        TestSpiritAdventurePartyRemoval();
        TestSpiritStatusBarText();
        TestSpiritArtifactMath();
        TestSpiritArtifactCarouselLayout();
        TestSpiritArtifactTargetSelectorLayout();
        TestSpiritArtifactRowRingPolicy();
        TestSpiritArtifactSelectionState();
        TestSpiritArtifactBatchSelectionState();
        TestSpiritArtifactPresetLayoutPolicy();
        TestSpiritVirtualGridRefreshState();
        TestSpiritArtifactWishNavigationState();
        TestSpiritArtifactCardStylePolicy();
        TestTruthCurrencyAccountTransaction();
        TestPolymorphCooldownSnapshots();
        TestSpiritProfileIdentityResolver();
        TestProjectionTurnQueuePolicy();
        TestProjectionSummonTurnTransactionLedger();
        TestPartnerTurnOrderPolicy();
        TestFriendlyRoleSeatPolicy();
        TestProjectionDeckRecipe();
        TestProjectionActorDeckProjection();
        TestProjectionCardExecutionPolicy();
        TestProjectionNativeStatusRouting();
        TestProjectionScriptExecutionScope();
        TestBattleLifecycleApi();
        TestProjectionProtocolState();
        TestEndlessSeaReplicationClock();
        TestSolarMemoryRoleCommitPendingState();
        TestLoneerStateOwnership();
        TestStarScoreWindow();
        TestStarScoreArrivalCueService();
        TestDimensionShopRandom();
        TestEndlessAbyssEnemyScaling();
        TestEndlessAbyssEvacuationDepth();
        TestEndlessAbyssSettlementBarrierPolicy();
        TestEndlessAbyssSettlementLocalCommit();
        TestWitchArchiveSelectionPolicy();
        TestWitchArchiveTextLoader();

        Console.WriteLine("Terrias C# tests passed: " + assertions + " assertions.");
    }

    private static void TestProjectionNativeStatusRouting()
    {
        var routes = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["semantic-owner"] = new List<string> { "native-owner", "projection-1", "projection-1" },
            ["authority-host"] = new List<string> { "native-host" }
        };

        True(CompanionNativeStatusRouting.Register(routes, "authority-host", "projection-1"),
            "server-authoritative companions register under the host execution route");
        False(routes["semantic-owner"].Contains("projection-1"),
            "semantic ownership does not control native ScriptExecutor locality");
        Equal(1, routes["authority-host"].Count(value => value == "projection-1"),
            "one projection status has exactly one native execution route");
        var stableOwnerOrder = routes["authority-host"].ToArray();
        True(CompanionNativeStatusRouting.Register(routes, "authority-host", "projection-1")
             && routes["authority-host"].SequenceEqual(stableOwnerOrder),
            "repairing an already-correct projection route is idempotent and preserves native status order");
        True(CompanionNativeStatusRouting.Contains(routes, "authority-host", "projection-1"),
            "projection native routing can verify its authority/status invariant");
        var executorVars = new Dictionary<string, string>(StringComparer.Ordinal);
        var projectionWouldSendRemote =
            !CompanionNativeStatusRouting.Contains(routes, "authority-host", "projection-1")
            && !executorVars.ContainsKey("Online");
        var semanticOwnerWouldExecuteProjection =
            CompanionNativeStatusRouting.Contains(routes, "semantic-owner", "projection-1");
        var enemyWouldSendRemote =
            !CompanionNativeStatusRouting.Contains(routes, "authority-host", "enemy-1")
            && !executorVars.ContainsKey("Online");
        False(projectionWouldSendRemote,
            "the host applies projection self BUFFs locally before native replication");
        False(semanticOwnerWouldExecuteProjection,
            "a remote semantic owner never becomes the projection simulation authority");
        True(enemyWouldSendRemote,
            "the same native gate still dispatches non-local target effects through the game's RPC");
        Equal(1, CompanionNativeStatusRouting.Remove(routes, "projection-1"),
            "projection cleanup removes the authoritative route exactly once");
        False(CompanionNativeStatusRouting.Contains(routes, "authority-host", "projection-1"),
            "a removed projection no longer participates in native status routing");
        False(CompanionNativeStatusRouting.Register(routes, "", "projection-2"),
            "projection routing rejects missing owner identity");
    }

    private static void TestBattleLifecycleApi()
    {
        True(BattleLifecycleApi.IsActiveFightType(FightType.Player)
             && BattleLifecycleApi.IsActiveFightType(FightType.Partner),
            "companion continuation accepts active native player and partner phases");
        False(BattleLifecycleApi.IsActiveFightType(FightType.None)
              || BattleLifecycleApi.IsActiveFightType(FightType.Win)
              || BattleLifecycleApi.IsActiveFightType(FightType.Loss)
              || BattleLifecycleApi.IsActiveFightType(FightType.Escape),
            "companion continuation closes for every native terminal fight type");
        AuraBattleLifecycleStateRuntime.ResetForTests();
        FightManager.Instance = new FightManager { fightType = FightType.Partner };
        AuraBattleLifecycleStateRuntime.Begin(1);
        AuraBattleLifecycleStateRuntime.Activate(1);
        True(BattleLifecycleApi.AcceptsCompanionContinuation,
            "synthetic companions may continue only while both shared and native battle phases are active");
        AuraBattleLifecycleStateRuntime.EnterOutcome(1, AuraBattleOutcome.Win);
        False(BattleLifecycleApi.AcceptsCompanionContinuation,
            "OutcomeEntering closes delayed spirit and projection presentation producers before cleanup");
        FightManager.Instance = null;
        AuraBattleLifecycleStateRuntime.ResetForTests();
    }

    private static void TestEndlessSeaReplicationClock()
    {
        var clock = new Terrias.Dll.Application.EndlessSeaReplicationClock();
        False(clock.CanAccept("", 1), "endless sea replication rejects an unbound host session");
        True(clock.Commit("host-a", 3), "endless sea replication accepts its first authority generation");
        False(clock.CanAccept("host-a", 2), "endless sea replication rejects stale generations in one session");
        True(clock.Commit("host-a", 3), "endless sea replication accepts an idempotent equal generation");
        True(clock.Commit("host-b", 1), "a new host session resets the generation domain");
        Equal("host-b", clock.Session, "the committed host session becomes authoritative");
        Equal(1, clock.Generation, "the new host session owns its own generation counter");
    }

    private static void TestProjectionScriptExecutionScope()
    {
        var originalSelf = new FakeStatus("native-self");
        var originalTarget = new FakeStatus("native-target");
        var originalStatus = new FakeStatus("native-status-field");
        var originalObjects = new List<IStatusManager> { originalTarget };
        var projection = new FakeStatus("projection-self");
        var targetA = new FakeStatus("enemy-a");
        var targetB = new FakeStatus("enemy-b");
        var executor = new TestScriptExecutor
        {
            Self = originalSelf,
            Target = originalTarget,
            status = originalStatus,
            Object = originalObjects
        };

        using (ProjectionScriptExecutionScope.Enter(
                   executor,
                   projection,
                   new IStatusManager[] { targetA, targetB }))
        {
            True(ReferenceEquals(projection, executor.Self)
                 && ReferenceEquals(targetA, executor.Target)
                 && executor.status == null,
                "projection script scope exposes the synthetic actor as native Self and clears transient status routing");
            True(executor.Object.SequenceEqual(new IStatusManager[] { targetA, targetB }),
                "projection script scope preserves the authoritative target order");
            False(executor.Vars.ContainsKey("Online"),
                "projection script scope does not counterfeit a received-online marker or suppress native target RPCs");
        }

        True(ReferenceEquals(executor.Self, originalSelf)
             && ReferenceEquals(executor.Target, originalTarget)
             && ReferenceEquals(executor.status, originalStatus)
             && ReferenceEquals(executor.Object, originalObjects),
            "projection execution restores every native executor field and the original target-list identity");
        False(executor.Vars.ContainsKey("Online"),
            "projection execution leaves native online-routing variables unchanged");

        executor.Vars["Online"] = "preexisting-host-route";
        try
        {
            using (ProjectionScriptExecutionScope.Enter(
                       executor,
                       projection,
                       Array.Empty<IStatusManager>()))
            {
                throw new InvalidOperationException("script failure fixture");
            }
        }
        catch (InvalidOperationException)
        {
        }

        Equal("preexisting-host-route",
            executor.Vars["Online"],
            "projection execution restores a pre-existing native Online marker after script failure");
        True(ReferenceEquals(executor.Object, originalObjects)
             && ReferenceEquals(executor.Self, originalSelf),
            "projection execution restores executor state through exception unwinding");
    }


    private static void TestProjectionSummonTurnTransactionLedger()
    {
        True(ProjectionSummonTurnBarrierPolicy.ShouldAcquire(1, 1, 1, false, true),
            "an accepted summon acquires the player-turn barrier without consulting local FightType");
        False(ProjectionSummonTurnBarrierPolicy.ShouldAcquire(1, 0, 1, false, true),
            "the summon barrier waits for native player-turn completion");
        False(ProjectionSummonTurnBarrierPolicy.ShouldAcquire(1, 1, 0, false, true),
            "a round without open summon transactions leaves the native flow untouched");
        var ledger = new ProjectionSummonTurnTransactionLedger();
        ledger.BeginRound(1);
        True(ledger.Reserve("", 1, out var missingReason) == null,
            "summon-turn transactions reject missing command tokens");
        True(missingReason.Contains("token", StringComparison.Ordinal),
            "missing summon-turn tokens return an explicit reason");

        var first = ledger.Reserve("summon-a", 1, out _)!;
        Equal(ProjectionSummonTurnTransactionState.Reserved, first.State,
            "the host reserves the same-round action before spawning its projection");
        Equal(1, ledger.OpenCount(1),
            "a reserved summon holds the player-turn completion barrier");
        var duplicate = ledger.Reserve("summon-a", 1, out _)!;
        Equal(first.Order, duplicate.Order,
            "replayed summon commands reuse the authoritative transaction order");

        True(ledger.TryMarkReady(
                "summon-a",
                "sp0",
                "generation-a",
                out var ready,
                out _),
            "spawn completion binds the reserved transaction to its projection actor");
        Equal(ProjectionSummonTurnTransactionState.Ready, ready.State,
            "a bound summon is ready for the current-round action");
        True(ledger.TryClaimReady(1, out var claimed)
             && claimed.Token == "summon-a",
            "the authority claims the ready transaction in deterministic order");
        False(ledger.TryClaimReady(1, out _),
            "one ready transaction cannot execute twice while claimed");
        True(ledger.TryMarkCompleted("summon-a", out var completed, out _),
            "a projection card action completes the summon-turn transaction");
        Equal(ProjectionSummonTurnTransactionState.Completed, completed.State,
            "completed transactions are terminal");
        Equal(0, ledger.OpenCount(1),
            "the native player-turn barrier releases after terminal completion");

        var second = ledger.Reserve("summon-b", 1, out _)!;
        True(ledger.TryMarkFailed("summon-b", "spawn failed", out var failed, out _),
            "spawn failure resolves a reserved transaction without deadlocking the round");
        Equal(ProjectionSummonTurnTransactionState.Failed, failed.State,
            "failed summon-turn transactions are terminal");
        Equal("spawn failed", failed.Detail,
            "failed summon-turn transactions retain their authoritative diagnostic detail");
        Equal(0, ledger.OpenCount(1),
            "failed transactions also release the native barrier");
        True(first.Order < second.Order,
            "authoritative transaction order remains monotonic");

        var remote = new ProjectionSummonTurnTransactionLedger();
        remote.BeginRound(1);
        True(remote.TryApplyAuthoritative(first, out _),
            "clients accept the authoritative reservation snapshot");
        True(remote.TryApplyAuthoritative(ready, out _),
            "clients advance the reservation to ready from a newer snapshot");
        var conflictingReady = ready.Clone();
        conflictingReady.StatusId = "sp-conflict";
        False(remote.TryApplyAuthoritative(conflictingReady, out var conflictReason),
            "same-revision transaction snapshots cannot rewrite the authoritative actor identity");
        True(conflictReason.Contains("conflicts", StringComparison.Ordinal),
            "same-revision transaction conflicts are diagnosable");
        False(remote.TryApplyAuthoritative(first, out var staleReason),
            "clients reject stale transaction snapshots");
        True(staleReason.Contains("stale", StringComparison.Ordinal),
            "stale transaction rejection is diagnosable");
        True(remote.TryApplyAuthoritative(completed, out _),
            "clients release their barrier only after the host completion snapshot");
        Equal(0, remote.OpenCount(1),
            "remote completion closes the same transaction exactly once");

        var earlyCompletion = new ProjectionSummonTurnTransactionLedger();
        earlyCompletion.BeginRound(1);
        True(earlyCompletion.TryApplyAuthoritative(completed, out _),
            "a completion snapshot arriving before local reservation state is immediately consumable");
        Equal(0, earlyCompletion.OpenCount(1),
            "an early authoritative completion never opens a local barrier");
        False(earlyCompletion.TryApplyAuthoritative(ready, out _),
            "a late ready snapshot cannot reopen an already completed transaction");

        var malformedReady = ready.Clone();
        malformedReady.StatusId = "";
        False(new ProjectionSummonTurnTransactionLedger().TryApplyAuthoritative(
                malformedReady,
                out var malformedReason),
            "ready snapshots without an actor identity fail closed");
        True(malformedReason.Contains("identity", StringComparison.Ordinal),
            "malformed ready snapshot rejection is diagnosable");

        ledger.Reserve("stale-open", 1, out _);
        var stale = ledger.BeginRound(2);
        True(stale.Count == 1 && stale[0].Token == "stale-open",
            "a round transition exposes rather than silently discarding an unresolved transaction");
        ledger.Clear();
        Equal(0, ledger.OpenCount(2),
            "battle cleanup clears every summon-turn transaction");
    }

    private static void TestBuffPresentationDependencies()
    {
        Equal(57, TerriasBuffPresentationDependencyCatalog.OwnedBuffIds.Count,
            "Every shipped Terrias Buff has an explicit presentation dependency rule");
        Equal(57, TerriasBuffPresentationDependencyCatalog.OwnedBuffIds.Distinct(StringComparer.Ordinal).Count(),
            "Terrias Buff presentation dependency ids are unique");
        foreach (var buffId in TerriasBuffPresentationDependencyCatalog.OwnedBuffIds)
        {
            True(TerriasBuffPresentationDependencyCatalog.TryResolve(buffId, out _),
                "Terrias Buff dependency is registered: " + buffId);
        }

        True(TerriasBuffPresentationDependencyCatalog.TryResolve("buff_burn", out var burn)
             && (burn.Fields & TerriasPresentationDirtyFields.Description) != 0,
            "Native Burn is explicitly managed for Terrias dynamic card descriptions");
        True(TerriasBuffPresentationDependencyCatalog.TryResolve("solar_crown", out var crown)
             && crown.ShouldInvalidate(0, 1)
             && !crown.ShouldInvalidate(1, 2),
            "Presence-only Buff dependencies avoid redundant refreshes within the same active threshold");
        True(TerriasBuffPresentationDependencyCatalog.TryResolve("buff_extraordinary", out var extraordinary)
             && extraordinary.Scope == TerriasBuffPresentationScope.LocalPlayer
             && (extraordinary.Fields & TerriasPresentationDirtyFields.Description) != 0,
            "frequently applied native damage Buffs use an explicit Terrias delta rule");
        foreach (var externalId in new[]
                 {
                     "buff_burn", "buff_cripple", "buff_eclipsedmoon", "buff_elements", "buff_evergreen",
                     "buff_extraordinary", "buff_impregnable", "buff_keenedge", "buff_poised", "buff_rebirth",
                     "buff_resilient", "buff_rotten", "buff_Soul", "buff_toxin", "buff_vitality", "buff_VowPower",
                     "buff_vulnerability", "buff_weak"
                 })
        {
            True(TerriasBuffPresentationDependencyCatalog.TryResolve(externalId, out _),
                "Every native Buff directly used by Terrias has an explicit compatibility impact: " + externalId);
        }
        False(TerriasBuffPresentationDependencyCatalog.TryResolve("unknown_external_buff", out _),
            "Unknown external Buffs retain the conservative native full-refresh fallback");

        Equal(
            TerriasPresentationInvalidationDecision.SuppressNoChange,
            TerriasPresentationInvalidationPolicy.Decide(false, true, true, 0, true, true),
            "No-op CheckAllBuff refresh is suppressed for a fully managed Terrias hand");
        Equal(
            TerriasPresentationInvalidationDecision.ConvertToDelta,
            TerriasPresentationInvalidationPolicy.Decide(false, true, true, 2, true, true),
            "Known delta-safe Buff mutations convert the native full refresh to a delta plan");
        Equal(
            TerriasPresentationInvalidationDecision.PreserveNative,
            TerriasPresentationInvalidationPolicy.Decide(false, true, false, 0, true, true),
            "An unmanaged active card preserves the native full refresh");
        Equal(
            TerriasPresentationInvalidationDecision.PreserveNative,
            TerriasPresentationInvalidationPolicy.Decide(false, true, true, 1, false, true),
            "An unknown Buff preserves the native full refresh");
        Equal(
            TerriasPresentationInvalidationDecision.PreserveNative,
            TerriasPresentationInvalidationPolicy.Decide(true, true, true, 0, true, true),
            "A pre-existing native refresh request is never cleared");
    }

    private static void TestLegacyBattleHookVarMigration()
    {
        var vars = new Dictionary<string, string>
        {
            ["TerriasFlamewheelCostHook"] = "1",
            ["TerriasMorningStarBlessingToken_dream_talker"] = "4",
            ["TerriasWunaBurnListener_enemy_1_3"] = "1",
            ["GameplayValue"] = "keep"
        };
        Equal(3, LegacyBattleHookVarMigration.RemoveFrom(vars),
            "retired persistent hook Vars are removed deterministically");
        Equal("keep", vars["GameplayValue"],
            "battle-hook migration preserves unrelated runtime values");
    }

    private static void TestActionPassiveRegistry()
    {
        TerriasActionPassiveRegistry.Clear();
        var executor = new ScriptExecutor { Self = new FakeStatus("passive-owner") };
        var started = 0;
        var committed = 0;
        TerriasActionPassiveRegistry.Register(
            executor,
            "native",
            AuraCardActionPhase.NativeStarted,
            _ => started++);
        TerriasActionPassiveRegistry.Register(
            executor,
            "committed",
            AuraCardActionPhase.Committed,
            _ => committed++);
        TerriasActionPassiveRegistry.Dispatch(new AuraCardActionContext
        {
            OwnerStatusId = "passive-owner",
            Phase = AuraCardActionPhase.NativeStarted
        });
        Equal(1, started, "native-started passives run on their declared shared transaction phase");
        Equal(0, committed, "committed passives do not scan or execute during native-started dispatch");
        TerriasActionPassiveRegistry.Dispatch(new AuraCardActionContext
        {
            OwnerStatusId = "passive-owner",
            Phase = AuraCardActionPhase.Committed
        });
        Equal(1, committed, "committed passives run exactly once on commit");
        TerriasActionPassiveRegistry.Unregister(executor, "committed");
        TerriasActionPassiveRegistry.Dispatch(new AuraCardActionContext
        {
            OwnerStatusId = "passive-owner",
            Phase = AuraCardActionPhase.Committed
        });
        Equal(1, committed, "disposed battle passives leave the hot phase snapshot");
        TerriasActionPassiveRegistry.Clear();
    }

    private static void TestScriptDelegateBinding()
    {
        var executor = new ScriptExecutor();
        var calls = 0;
        string? observed = null;
        void Handler(ScriptExecutor current, string id)
        {
            calls++;
            observed = id;
        }

        ScriptDelegateApi.BindParameterized(executor, "InitScript", "dynamic-card", Handler);
        executor.ScriptDict.TryGetValue("InitScript", out var value);
        var direct = value as Action<ScriptExecutor>;
        True(direct != null,
            "card InitScript is rebound to a cached direct C# delegate after its first bridge call");
        direct!(executor);
        Equal(1, calls, "direct card InitScript delegate executes without re-entering the Lua bridge");
        Equal("dynamic-card", observed, "direct card InitScript delegate preserves the normalized card id");
    }

    private static void TestWitchArchiveSelectionPolicy()
    {
        Equal(-1, WitchArchiveSelectionPolicy.Move(0, 0, 1), "Witch Archive selection rejects an empty catalog");
        Equal(1, WitchArchiveSelectionPolicy.Move(0, 3, 1), "Witch Archive moves to the next character");
        Equal(0, WitchArchiveSelectionPolicy.Move(2, 3, 1), "Witch Archive wraps forward at the end of the rail");
        Equal(2, WitchArchiveSelectionPolicy.Move(0, 3, -1), "Witch Archive wraps backward at the start of the rail");
        Equal(1, WitchArchiveSelectionPolicy.Move(99, 3, -1), "Witch Archive normalizes a stale selected index");
    }

    private static void TestWitchArchiveTextLoader()
    {
        var root = Path.Combine(Path.GetTempPath(), "terrias-witch-archive-" + Guid.NewGuid().ToString("N"));
        var textDirectory = Path.Combine(root, "Text");
        Directory.CreateDirectory(textDirectory);
        try
        {
            File.WriteAllText(Path.Combine(textDirectory, "valid.txt"), "  first\r\n\r\nsecond\rthird  ");
            File.WriteAllText(Path.Combine(textDirectory, "empty.txt"), " \r\n\t ");
            File.WriteAllText(Path.Combine(textDirectory, "wrong.json"), "not text");

            True(
                WitchArchiveTextLoader.TryRead(root, "Text/valid.txt", out var text, out var error),
                "Witch Archive loads an in-root UTF-8 text file");
            Equal("first\n\nsecond\nthird", text, "Witch Archive normalizes line endings and trims only outer whitespace");
            Equal("", error, "Witch Archive leaves no error after a successful text load");

            False(
                WitchArchiveTextLoader.TryRead(root, "../outside.txt", out _, out _),
                "Witch Archive rejects a path that escapes the mod directory");
            False(
                WitchArchiveTextLoader.TryRead(root, Path.Combine(textDirectory, "valid.txt"), out _, out _),
                "Witch Archive rejects absolute text paths");
            False(
                WitchArchiveTextLoader.TryRead(root, "Text/wrong.json", out _, out _),
                "Witch Archive rejects non-text resources");
            False(
                WitchArchiveTextLoader.TryRead(root, "Text/missing.txt", out _, out _),
                "Witch Archive rejects missing text resources");
            False(
                WitchArchiveTextLoader.TryRead(root, "Text/empty.txt", out _, out _),
                "Witch Archive rejects whitespace-only text resources");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void TestDictionaryUtil()
    {
        Equal("Terrias", TerriasIds.ModId, "Terrias shared owner id remains stable");
        Equal(12, DictionaryUtil.ParseInt("12"), "ParseInt parses positive values");
        Equal(-4, DictionaryUtil.ParseInt("-4"), "ParseInt parses negative values");
        Equal(9, DictionaryUtil.ParseInt("not-a-number", 9), "ParseInt returns fallback on invalid text");
        Equal("fallback", DictionaryUtil.Get(null, "key", "fallback"), "DictionaryUtil.Get handles null dictionaries");

        var values = new Dictionary<string, string> { ["A"] = "1" };
        Equal("1", DictionaryUtil.Get(values, "A"), "DictionaryUtil.Get reads existing values");
        DictionaryUtil.Set(values, "B", "2");
        Equal("2", values["B"], "DictionaryUtil.Set writes values");

        True(DictionaryUtil.ContainsToken("Burnout, " + WhiteRadiance + " ,Froze", TerriasIds.WhiteRadianceTag), "ContainsToken trims comma-separated tokens");
        False(DictionaryUtil.ContainsToken(WhiteRadiance + "\u5316", TerriasIds.WhiteRadianceTag), "ContainsToken requires exact token matches");
        True(TerriasIds.IsTechnicalBlessingId("*origin_strength_50"), "Hidden origin milestones are classified as technical blessings");
        True(TerriasIds.IsTechnicalBlessingId(TerriasIds.OriginFortune50Blessing), "Runtime origin milestone ids remain excluded from custom blessing pools");
        False(TerriasIds.IsTechnicalBlessingId("Terrias_terrias_solar_witch"), "Player-facing Solar blessings are not classified as technical");
    }

    private static void TestGoldDreamRules()
    {
        Equal(0L, GoldDreamRules.TotalAssets(-10, -20), "Negative currency values do not reduce verified Assets below zero");
        Equal(1_500L, GoldDreamRules.TotalAssets(500, 1_000), "Verified Assets combine False Gold and real Gold");
        Equal(4_294_967_294L, GoldDreamRules.TotalAssets(int.MaxValue, int.MaxValue), "Verified Assets do not overflow the runtime integer range");
        Equal(GoldenPotentialTier.Zero, GoldDreamRules.PotentialTier(0, 999), "Golden Potential K requires one thousand pre-gain Assets");
        Equal(GoldenPotentialTier.K, GoldDreamRules.PotentialTier(1, 999), "False Gold and real Gold jointly reach Golden Potential K");
        Equal(GoldenPotentialTier.K, GoldDreamRules.PotentialTier(999_999, 0), "Golden Potential K remains active below one million Assets");
        Equal(GoldenPotentialTier.M, GoldDreamRules.PotentialTier(400_000, 600_000), "Combined Assets reach Golden Potential M");
        Equal(GoldenPotentialTier.M, GoldDreamRules.PotentialTier(999_999_999, 0), "Golden Potential M remains active below one billion Assets");
        Equal(GoldenPotentialTier.B, GoldDreamRules.PotentialTier(500_000_000, 500_000_000), "Combined Assets reach Golden Potential B");
        Equal(GoldenPotentialTier.B, GoldDreamRules.PotentialTier(int.MaxValue, int.MaxValue), "Golden Potential verification handles combined Assets above Int32.MaxValue");

        Equal(50, GoldDreamRules.WagerCost(0), "Wager always includes its fifty Gold base payment");
        Equal(150, GoldDreamRules.WagerCost(1_000), "Wager adds ten percent of current real Gold");
        Equal(0, GoldDreamRules.TenPercentIncrease(0), "Golden Dream does not create value from zero");
        Equal(1, GoldDreamRules.TenPercentIncrease(1), "Golden Dream ten percent rounds up");
        Equal(2, GoldDreamRules.TenPercentIncrease(11), "Golden Dream rounds fractional ten percent upward");

        Equal(10, GoldDreamRules.FortuneThrowDamage(100, 0), "Fortune Throw divides the check by ten");
        Equal(27, GoldDreamRules.FortuneThrowDamage(99, 2), "Fortune Throw multiplies by one plus prior Ascensions");
        Equal(int.MaxValue, GoldDreamRules.FortuneThrowDamage(100, int.MaxValue), "Fortune Throw damage saturates instead of overflowing");
        Equal(500, GoldDreamRules.ConvertedRealGold(1_001), "Golden Dreamland converts False Gold at floor fifty percent");
        Equal(int.MaxValue, GoldDreamRules.TotalDebt(int.MaxValue, 20, 30), "Debt totals saturate at the runtime integer limit");

        var normalized = GoldDreamRules.NormalizeDebt(int.MaxValue - 2, 5, 9);
        Equal(int.MaxValue - 2, normalized.DueOne, "Debt normalization preserves the nearest due bucket");
        Equal(2, normalized.DueTwo, "Debt normalization spends remaining capacity on the second bucket");
        Equal(0, normalized.DueThree, "Debt normalization drops only overflow from the latest bucket");

        Equal(
            GoldDreamPaymentState.Inactive,
            GoldDreamRules.PaymentState(false, 1_000, 1_000),
            "Inactive Golden Dream combat state does not publish stale payment values");
        var belowFortuneThreshold = GoldDreamRules.PaymentState(true, 499, 500);
        Equal(100, belowFortuneThreshold.WagerCost, "Golden Dream payment state projects the visible Wager cost");
        True(belowFortuneThreshold.CanUseWager, "Golden Dream payment state projects Wager usability");
        False(belowFortuneThreshold.CanUseFortuneThrow, "Fortune Throw stays disabled below one thousand combined Gold");
        var atFortuneThreshold = GoldDreamRules.PaymentState(true, 500, 500);
        True(atFortuneThreshold.CanUseFortuneThrow, "False Gold and real Gold jointly enable Fortune Throw");
        Equal(
            atFortuneThreshold,
            GoldDreamRules.PaymentState(true, 500, 500),
            "Equivalent payment projections compare equal for refresh deduplication");
        NotEqual(
            belowFortuneThreshold,
            atFortuneThreshold,
            "A visible Fortune Throw usability transition remains refreshable");
    }

    private static void TestCardInvalidationContract()
    {
        AuraSharedFrameScheduler.Reset();
        AuraCardPresentationDelta.Reset();
        TerriasCardDescriptionProjector.Reset();
        FightCardManager.Instance.ResetDiagnostics();
        var card = new CardItem
        {
            dataConfig = new DataConfig(
                new Dictionary<string, string> { ["Id"] = TerriasIds.WagerCardId },
                new Dictionary<string, string> { ["Tag"] = "" })
        };
        card.DataUpdateAction = () => TerriasCardInvalidationService.Invalidate(
            card,
            TerriasCardDirtyFields.Structure,
            "test.reentrant");

        TerriasCardInvalidationService.Invalidate(
            card,
            TerriasCardDirtyFields.TagIndex
            | TerriasCardDirtyFields.DerivedState
            | TerriasCardDirtyFields.Description,
            "test.initial");
        TerriasCardInvalidationService.Invalidate(
            card,
            TerriasCardDirtyFields.TagIndex | TerriasCardDirtyFields.Cost,
            "test.merge");
        var request = AuraSharedFrameScheduler.TakePendingRequest();
        True(request?.ExecuteSlice != null, "Card invalidation schedules a cooperative slice");
        var completed = request!.ExecuteSlice!(new AuraSharedFrameSliceContext());

        True(completed, "Merged card invalidation completes in one slice");
        Equal(1, FightCardManager.Instance.RefreshTagCount, "Merged config/card tag invalidation rebuilds the native tag index once");
        Equal(0, card.RefreshTagCount, "Terrias invalidation never calls native CardItem.RefreshTag");
        Equal(1, TerriasCardDescriptionProjector.RecomputeCount, "Derived card state is recomputed once for a merged request");
        Equal(1, TerriasCardDescriptionProjector.ApplyDescriptionCount, "Description delta is applied once for a merged request");
        Equal(1, AuraCardPresentationDelta.CostUpdates, "Cost delta is applied once for a merged request");
        Equal(0, card.DataUpdateCount, "Successful delta invalidation avoids native full DataUpdate");
        Equal<AuraSharedFrameWorkRequest?>(
            null,
            AuraSharedFrameScheduler.TakePendingRequest(),
            "A completed merged invalidation leaves no extra frame request");

        var nativeContractCard = new CardItem { dataConfig = card.dataConfig };
        nativeContractCard.RefreshTag();
        Equal(1, nativeContractCard.DataUpdateCount, "The test host models native CardItem.RefreshTag as an implicit DataUpdate");

        var fallbackCard = new CardItem
        {
            dataConfig = new DataConfig(
                new Dictionary<string, string> { ["Id"] = TerriasIds.WagerCardId },
                new Dictionary<string, string> { ["Tag"] = "" })
        };
        AuraCardPresentationDelta.CostResult = false;
        fallbackCard.DataUpdateAction = () => TerriasCardInvalidationService.Invalidate(
            fallbackCard,
            TerriasCardDirtyFields.Cost,
            "test.fallback.reentrant");
        TerriasCardInvalidationService.Invalidate(fallbackCard, TerriasCardDirtyFields.Cost, "test.fallback");
        request = AuraSharedFrameScheduler.TakePendingRequest();
        request!.ExecuteSlice!(new AuraSharedFrameSliceContext());
        Equal(1, fallbackCard.DataUpdateCount, "A failed delta adapter falls back to exactly one native DataUpdate");
        Equal<AuraSharedFrameWorkRequest?>(null, AuraSharedFrameScheduler.TakePendingRequest(),
            "A DataUpdate fallback cannot requeue the active dirty-field subset");
        AuraCardPresentationDelta.CostResult = true;

        var structuralCard = new CardItem
        {
            dataConfig = new DataConfig(
                new Dictionary<string, string> { ["Id"] = TerriasIds.WagerCardId },
                new Dictionary<string, string> { ["Tag"] = "" })
        };
        TerriasCardPresentationRouter.ResetDiagnostics();
        AuraCardPresentationRuntime.ResetDiagnostics();
        TerriasCardInvalidationService.Invalidate(structuralCard, TerriasCardDirtyFields.Structure, "test.structure");
        request = AuraSharedFrameScheduler.TakePendingRequest();
        request!.ExecuteSlice!(new AuraSharedFrameSliceContext());
        Equal(1, structuralCard.TransformCount, "Structural invalidation uses the native configured-type rebind exactly once");
        Equal(1, structuralCard.DataUpdateCount, "Structural rebind subsumes ordinary derived and presentation DataUpdate work");
        Equal(1, AuraCardPresentationRuntime.ApplyCount,
            "Structural rebind performs one final shared visual presentation commit");
        Equal(0, TerriasCardPresentationRouter.ApplyCount,
            "Structural rebind does not bypass tool-owned shared presentation subscribers");

        var visualCard = new CardItem
        {
            dataConfig = new DataConfig(
                new Dictionary<string, string> { ["Id"] = TerriasIds.WagerCardId },
                new Dictionary<string, string> { ["Tag"] = "" })
        };
        AuraCardPresentationRuntime.ResetDiagnostics();
        TerriasCardInvalidationService.Invalidate(visualCard, TerriasCardDirtyFields.Visual, "test.visual");
        request = AuraSharedFrameScheduler.TakePendingRequest();
        request!.ExecuteSlice!(new AuraSharedFrameSliceContext());
        Equal(1, AuraCardPresentationRuntime.ApplyCount,
            "Visual invalidation re-enters the shared presentation lifecycle for tool-owned effects");

        var preMaterialized = new DataConfig(
            new Dictionary<string, string> { ["Id"] = TerriasIds.WagerCardId },
            new Dictionary<string, string> { ["Tag"] = "" });
        FightCardManager.Instance.ResetDiagnostics();
        TerriasCardInvalidationService.Invalidate(preMaterialized, TerriasCardDirtyFields.TagIndex, "test.pre-materialized");
        TerriasCardInvalidationService.Acknowledge(preMaterialized, TerriasCardDirtyFields.TagIndex, "test.materialized");
        request = AuraSharedFrameScheduler.TakePendingRequest();
        request!.ExecuteSlice!(new AuraSharedFrameSliceContext());
        Equal(0, FightCardManager.Instance.RefreshTagCount,
            "Synchronous materialization can acknowledge and consume a queued config-only tag invalidation");
    }

    private static void TestTerriasLocalizationValues()
    {
        Equal(TerriasLocale.ZhHans, TerriasLocale.Normalize("zh-CN"),
            "locale aliases normalize Simplified Chinese deterministically");
        Equal(TerriasLocale.ZhHant, TerriasLocale.Normalize("zh_TW"),
            "locale aliases normalize Traditional Chinese deterministically");
        Equal(TerriasLocale.English, TerriasLocale.Normalize("en-US"),
            "regional English locales use the English catalog");
        Equal(TerriasLocale.Japanese, TerriasLocale.Normalize("ja-JP"),
            "regional Japanese locales use the Japanese catalog");
        Equal("Name_ja", TerriasLocale.FieldName("Name", "ja-JP"),
            "runtime presentation fields use the native Japanese suffix");
        Equal("Description", TerriasLocale.FieldName("Description", "zh-CN"),
            "Simplified Chinese keeps the native base field");

        var text = new TerriasLocalizedText
        {
            ZhHans = "简体",
            ZhHant = "繁體",
            English = "English",
            Japanese = "日本語",
            LegacyFallback = "legacy"
        };
        Equal("English", text.Resolve("en-GB"),
            "localized text resolves the exact normalized English value");
        Equal("日本語", text.Resolve("jp"),
            "localized text accepts the supported Japanese alias");

        text.Japanese = "";
        Equal("简体", text.Resolve("ja-JP"),
            "missing Japanese text follows the deterministic Simplified Chinese fallback");
        text.ZhHans = "";
        text.English = "";
        text.ZhHant = "";
        Equal("legacy", text.Resolve("ja-JP", "caller fallback"),
            "legacy persisted text wins only after all localized fields are unavailable");

        var row = new Dictionary<string, string>
        {
            ["Name"] = "基础名",
            ["Name_zh-Hant"] = "基礎名",
            ["Name_en"] = "Base Name",
            ["Name_ja"] = "基本名"
        };
        var fromRow = TerriasLocalizedText.FromRow(row, "Name", "stable-id");
        Equal("Base Name", fromRow.Resolve("en"),
            "localized text captures every native row locale independently");
        Equal("stable-id", fromRow.LegacyFallback,
            "localized row capture retains only a stable compatibility fallback");
    }

    private static void TestActiveCardPresentationCoverage()
    {
        FightUI.cardItemList.Clear();
        TerriasActiveCardPresentationIndex.Clear();
        var card = new CardItem
        {
            dataConfig = new DataConfig(new Dictionary<string, string>
            {
                ["Id"] = TerriasIds.WagerCardId,
                ["Icon"] = "icon",
                ["PackBelong"] = "Terrias_pack"
            })
        };
        FightUI.cardItemList.Add(card);
        False(TerriasActiveCardPresentationIndex.HasCompleteActiveCardCoverage(),
            "A Terrias hand cannot suppress native refresh before every active view is indexed");
        TerriasActiveCardPresentationIndex.Observe(card);
        True(TerriasActiveCardPresentationIndex.HasCompleteActiveCardCoverage(),
            "An exactly indexed Terrias hand proves active-card presentation coverage");

        card.dataConfig = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.FortuneThrowCardId,
            ["Icon"] = "icon",
            ["PackBelong"] = "Terrias_pack"
        });
        False(TerriasActiveCardPresentationIndex.HasCompleteActiveCardCoverage(),
            "A pooled view rebound to another config invalidates the previous coverage proof");

        TerriasActiveCardPresentationIndex.Clear();
        FightUI.cardItemList.Clear();
    }

    private static void TestSolarFlameSealFormula()
    {
        Equal(1, SolarFlameSealFormula.GatheredFlameGain(0), "Solar Flame Seal grants 1 Gathered Flame for a zero-cost card");
        Equal(4, SolarFlameSealFormula.GatheredFlameGain(3), "Solar Flame Seal grants paid cost plus 1");
        Equal(1, SolarFlameSealFormula.GatheredFlameGain(-5), "Solar Flame Seal clamps invalid negative costs before adding 1");
        Equal(int.MaxValue, SolarFlameSealFormula.GatheredFlameGain(int.MaxValue), "Solar Flame Seal gain saturates safely");
    }

    private static void TestMorningStarRelicFormula()
    {
        var paidCard = NewConfig(
            new Dictionary<string, string> { ["Id"] = "timeless_target", ["Expend"] = "3" },
            new Dictionary<string, string>());
        True(MorningStarRelicFormula.IsTimelessClockCandidate(paidCard), "Timeless Clock accepts a positive-cost unmarked card");
        True(MorningStarRelicFormula.MakeTimelessClockFree(paidCard), "Timeless Clock marks its first eligible target");
        Equal(0, CardConfigApi.CurrentCost(paidCard), "Timeless Clock keeps the selected card at zero cost");
        Equal("-999", paidCard.Vars["TotalExCost"], "Timeless Clock uses a combat-scoped total cost override");
        True(CardMutationService.HasRuntimeMarker(paidCard, TerriasIds.TimelessClockZeroCostMarker), "Timeless Clock records its own runtime marker");
        False(MorningStarRelicFormula.IsTimelessClockCandidate(paidCard), "Timeless Clock never selects a card it already zeroed");
        False(MorningStarRelicFormula.MakeTimelessClockFree(paidCard), "Timeless Clock cannot apply twice to the same card instance");

        var freeCard = NewConfig(
            new Dictionary<string, string> { ["Id"] = "already_free", ["Expend"] = "0" },
            new Dictionary<string, string>());
        False(MorningStarRelicFormula.IsTimelessClockCandidate(freeCard), "Timeless Clock skips cards that already cost zero");

        True(MorningStarRelicFormula.ShouldCountNegativeBuffApplication("player", "player", true, true), "Fox-Woman's Harp counts a player-applied enemy debuff event");
        False(MorningStarRelicFormula.ShouldCountNegativeBuffApplication("player", "enemy", true, true), "Fox-Woman's Harp ignores debuffs applied by enemies");
        False(MorningStarRelicFormula.ShouldCountNegativeBuffApplication("player", "player", false, true), "Fox-Woman's Harp ignores self-applied debuffs");
        False(MorningStarRelicFormula.ShouldCountNegativeBuffApplication("player", "player", true, false), "Fox-Woman's Harp ignores positive buffs");

        Equal(StarStonePouchResetPolicy.RemoveWhenExhausted, MorningStarRelicFormula.RelicPouchResetPolicy(false), "A non-Loneer backup pouch is removed when exhausted");
        Equal(StarStonePouchResetPolicy.WhenExhausted, MorningStarRelicFormula.RelicPouchResetPolicy(true), "A Loneer backup pouch refills when exhausted");
        True(MorningStarRelicFormula.ParticipatesInStarStoneOrbit(MorningStarRelicFormula.CareerPouchChannel), "The career pouch participates in Star Stone Orbit");
        False(MorningStarRelicFormula.ParticipatesInStarStoneOrbit(MorningStarRelicFormula.RelicPouchChannel), "The backup pouch never participates in Star Stone Orbit");
        var careerKey = MorningStarRelicFormula.PouchStateKey("player", MorningStarRelicFormula.CareerPouchChannel);
        var relicKey = MorningStarRelicFormula.PouchStateKey("player", MorningStarRelicFormula.RelicPouchChannel);
        False(string.Equals(careerKey, relicKey, StringComparison.Ordinal), "Career and relic pouches use independent owner-channel state keys");
        Equal("", MorningStarRelicFormula.PouchStateKey("", MorningStarRelicFormula.RelicPouchChannel), "Pouch state rejects an empty owner identity");
    }

    private static void TestSpiritAdventurePartyRemoval()
    {
        var slots = new List<string> { "alpha", "beta", "alpha", "", "", "" };
        var active = "alpha";
        True(SpiritAdventurePartyRules.Remove(slots, ref active, "alpha"), "Returning a spirit removes it from every current-adventure slot");
        True(slots.All(uid => uid != "alpha"), "Returned spirit no longer appears in the current-adventure party");
        Equal("", active, "Returning the active spirit clears the current-adventure active selection");
        False(SpiritAdventurePartyRules.Remove(slots, ref active, "missing"), "Returning a spirit not in the current party is a no-op");
        Equal("beta", slots[1], "Returning one spirit preserves the remaining current-adventure party");
    }

    private static void TestSpiritArtifactMath()
    {
        Equal(120, SpiritArtifactMath.ApplyDamageMultiplier(100, 2000),
            "Artifact set damage uses a dedicated 20 percent multiplier");
        Equal(150, SpiritArtifactMath.ApplyDamageMultiplier(125, 2000),
            "Artifact multiplier applies after the existing passive result");
        Equal(300, SpiritArtifactMath.ApplyDamageMultiplier(125, 2000) * 2,
            "Elemental primary multiplier applies after artifact set damage");
        Equal(150, SpiritArtifactMath.ApplyDamageMultiplier(100, 9000),
            "Artifact set damage multiplier is bounded by its safety cap");
        Equal(0, SpiritArtifactMath.ApplyDamageMultiplier(0, 2000),
            "Zero direct damage stays zero");

        var rarityWeights = new Dictionary<string, int> { ["1"] = 6500, ["2"] = 3000, ["3"] = 500 };
        Equal(1, SpiritArtifactRollPolicy.ResolveRarity(0, 6499, rarityWeights, 30),
            "Rarity roll keeps the 65 percent one-star interval");
        Equal(2, SpiritArtifactRollPolicy.ResolveRarity(0, 6500, rarityWeights, 30),
            "Rarity roll enters the two-star interval at its exact boundary");
        Equal(3, SpiritArtifactRollPolicy.ResolveRarity(0, 9500, rarityWeights, 30),
            "Rarity roll enters the five percent three-star interval");
        Equal(3, SpiritArtifactRollPolicy.ResolveRarity(29, 0, rarityWeights, 30),
            "The thirtieth draw is hard-pity three-star regardless of RNG");
        var batch = Enumerable.Repeat(1, 10).ToList();
        SpiritArtifactRollPolicy.EnsureMinimumTwoStar(batch, 1);
        Equal(2, batch[^1], "Ten-pull guarantee promotes the final item when no two-star was rolled");
        True(SpiritArtifactRollPolicy.ForceTargetSet(3, 1, true),
            "An off-target three-star forces the next three-star into the selected set");
        Equal(1, SpiritArtifactRollPolicy.NextTargetFate(3, false, true),
            "Off-target three-star arms target-set fate");
        Equal(0, SpiritArtifactRollPolicy.NextTargetFate(3, true, true),
            "Target three-star clears target-set fate");

        var first = new SpiritArtifactBattleItemSnapshot
        {
            ArtifactUid = "a",
            PieceId = "piece.flower",
            SlotId = SpiritArtifactSlots.Flower,
            Rarity = 3,
            Level = 2,
            MainStat = new SpiritArtifactStatRoll { StatId = SpiritArtifactStats.Life, Value = 18 },
            SubStatRolls = new List<SpiritArtifactStatRoll>
            {
                new() { StatId = SpiritArtifactStats.Magic, Value = 2 }
            }
        };
        var second = new SpiritArtifactBattleItemSnapshot
        {
            ArtifactUid = "b",
            PieceId = "piece.plume",
            SlotId = SpiritArtifactSlots.Plume,
            Rarity = 2,
            Level = 1,
            MainStat = new SpiritArtifactStatRoll { StatId = SpiritArtifactStats.Speed, Value = 4 }
        };
        var ordered = SpiritArtifactMath.LoadoutHash(new[] { first, second }, "REGISTRY");
        var reversed = SpiritArtifactMath.LoadoutHash(new[] { second, first }, "REGISTRY");
        Equal(ordered, reversed, "Artifact loadout hash is independent of input list order");
        second.MainStat.Value++;
        NotEqual(ordered, SpiritArtifactMath.LoadoutHash(new[] { first, second }, "REGISTRY"),
            "Artifact loadout hash changes when a rolled value changes");
    }

    private static void TestSpiritManagementSelectionState()
    {
        var selection = new SpiritTrainingSelectionState();
        selection.EnsureInitialized(Array.Empty<string>());
        Equal(SpiritTrainingTargetKind.IntentSlot, selection.TargetKind,
            "Training UI initializes with an explicit intent-slot target");
        Equal(0, selection.IntentSlotIndex,
            "Training UI initializes the first intent slot when no intent is equipped");
        Equal("", selection.FocusedAbilityId,
            "An empty intent slot does not fall back to the equipped passive");

        selection.SelectIntentSlot(2, null);
        True(selection.TargetsIntentSlot(2),
            "Selecting an empty intent slot preserves it as the replacement target");
        Equal("", selection.FocusedAbilityId,
            "Selecting an empty intent slot keeps the detail focus empty");

        selection.PreviewAbility("passive-candidate");
        True(selection.TargetsIntentSlot(2),
            "Previewing an ability does not implicitly change the replacement target");
        Equal("passive-candidate", selection.FocusedAbilityId,
            "Previewing an ability changes only the detail focus");

        selection.SelectPassiveSlot("species-passive");
        True(selection.TargetsPassiveSlot,
            "Selecting the passive slot makes it the sole replacement target");
        False(selection.TargetsIntentSlot(2),
            "Selecting the passive slot clears the previous intent-slot target");
        Equal(-1, selection.IntentSlotIndex,
            "The passive target does not retain a hidden intent-slot index");

        selection.PreviewAbility("intent-candidate");
        True(selection.TargetsPassiveSlot,
            "Previewing an intent does not silently switch away from the passive target");

        False(SpiritPartySlotInteraction.TrySelectOccupant("  ", out var emptyUid),
            "Clicking an empty party slot performs no selection or mutation");
        Equal("", emptyUid, "An empty party slot exposes no synthetic spirit id");
        True(SpiritPartySlotInteraction.TrySelectOccupant("  spirit-a  ", out var occupantUid),
            "Clicking an occupied party slot selects its occupant");
        Equal("spirit-a", occupantUid,
            "Party-slot selection normalizes the occupant id without changing party data");
    }

    private static void TestSpiritElementDisplayPolicy()
    {
        Equal(16f, SpiritElementDisplayPolicy.IconSize(SpiritElementDisplaySurface.WarehouseCard),
            "Warehouse cards use a compact element icon in the name row");
        Equal(20f, SpiritElementDisplayPolicy.IconSize(SpiritElementDisplaySurface.DetailHeader),
            "The selected-spirit header uses the readable element icon size");
        Equal(14f, SpiritElementDisplayPolicy.IconSize(SpiritElementDisplaySurface.PartySlot),
            "Party slots use the dense element icon size");
        Equal(25f, SpiritElementDisplayPolicy.NameTrailingReserve(SpiritElementDisplaySurface.WarehouseCard),
            "Warehouse names always reserve a fixed trailing element-icon lane");
        Equal(23f, SpiritElementDisplayPolicy.NameTrailingReserve(SpiritElementDisplaySurface.PartySlot),
            "Party names cannot push their element icon away from its anchored lane");
        False(SpiritElementDisplayPolicy.ShowPersistentText,
            "Spirit management surfaces do not repeat persistent element text beside the icon");
        False(SpiritElementDisplayPolicy.ShowAttributeSummaryRow,
            "The attribute tab does not repeat the element already shown after the spirit name");
    }

    private static void TestTruthCurrencyAccountTransaction()
    {
        var runtime = Singleton<Witch.GameRuntimeData>.Instance;
        runtime.Reset(1000);

        True(TruthCurrencyApi.TrySpendAndRecord(160, "draw-a", out var debitReason),
            "Artifact draws debit the account runtime without an active adventure RoleTable");
        Equal("", debitReason, "A committed account debit has no failure reason");
        Equal(840, runtime.Truth, "The account-level Truth balance pays the ten-draw cost");
        True(TruthCurrencyApi.HasDebitToken("draw-a"),
            "The debit receipt is stored in the account runtime transaction journal");
        Equal(1, runtime.SaveCount,
            "The balance and debit receipt cross the native account persistence boundary together");

        True(TruthCurrencyApi.TrySpendAndRecord(160, "draw-a", out _),
            "Replaying the same draw token is idempotent");
        Equal(840, runtime.Truth, "An idempotent replay never charges the account twice");
        Equal(1, runtime.SaveCount, "An idempotent replay does not rewrite the account save");

        True(TruthCurrencyApi.RefundAndRemoveRecord(160, "draw-a"),
            "A failed artifact commit refunds the account transaction");
        Equal(1000, runtime.Truth, "The refund restores the exact account balance");
        False(TruthCurrencyApi.HasDebitToken("draw-a"),
            "The refunded transaction no longer owns a debit receipt");

        runtime.Reset(100);
        False(TruthCurrencyApi.TrySpendAndRecord(160, "draw-b", out var insufficientReason),
            "An account with insufficient Truth cannot prepare a paid draw");
        Equal("真理之晶不足。", insufficientReason,
            "The insufficient-account failure remains player-readable");
        Equal(100, runtime.Truth, "A rejected debit leaves the account balance unchanged");
        False(TruthCurrencyApi.HasDebitToken("draw-b"),
            "A rejected debit never creates an account receipt");

        runtime.Reset(1000);
        runtime.ThrowOnSave = true;
        False(TruthCurrencyApi.TrySpendAndRecord(160, "draw-c", out var persistenceReason),
            "A failed native account save rejects the draw transaction");
        Equal("真理之晶扣除未能持久化，抽取已经回滚。", persistenceReason,
            "A failed native save reports the rolled-back transaction");
        Equal(1000, runtime.Truth, "A failed native save restores the in-memory balance");
        False(TruthCurrencyApi.HasDebitToken("draw-c"),
            "A failed native save restores the account token journal");
        runtime.ThrowOnSave = false;
    }

    private static void TestSpiritArtifactCarouselLayout()
    {
        var geometry = SpiritArtifactCarouselPolicy.CalculateGeometry(520f, 300f);
        Approximately(
            SpiritArtifactCarouselPolicy.BoundaryMargin,
            (300f - geometry.SlotSize) * 0.5f
            - geometry.Radius * (float)Math.Cos(SpiritArtifactCarouselPolicy.ForwardTiltDegrees * Math.PI / 180d),
            0.01f,
            "The rotating equipment circle preserves ten pixels above and below every slot");

        var slotOrder = new[]
        {
            SpiritArtifactSlots.Flower,
            SpiritArtifactSlots.Sands,
            SpiritArtifactSlots.Circlet,
            SpiritArtifactSlots.Goblet,
            SpiritArtifactSlots.Plume
        };
        var points = slotOrder
            .Select(slot => SpiritArtifactCarouselPolicy.CalculatePoint(geometry, slot, 0f))
            .ToArray();
        var firstSide = CircleDistance(points[0], points[1]);
        for (var index = 1; index < points.Length; index++)
        {
            var next = (index + 1) % points.Length;
            Approximately(firstSide, CircleDistance(points[index], points[next]), 0.01f,
                "The five equipment centers remain vertices of one inscribed regular pentagon");
        }
        True(firstSide > geometry.SlotSize,
            "Responsive carousel slots remain separated at the reference equipment size");
        Approximately(0f, points.Average(point => point.CircleX), 0.01f,
            "The rotating pentagon remains centered horizontally around the portrait");
        Approximately(0f, points.Average(point => point.CircleY), 0.01f,
            "The rotating pentagon remains centered vertically around the portrait");

        var compact = SpiritArtifactCarouselPolicy.CalculateGeometry(240f, 220f);
        Approximately(SpiritArtifactCardStylePolicy.EquipmentSlotSize, compact.SlotSize, 0.01f,
            "Compact equipment uses the approved fifty-six-pixel artifact slot");
        Approximately(
            SpiritArtifactCarouselPolicy.BoundaryMargin,
            (220f - compact.SlotSize) * 0.5f
            - compact.Radius * (float)Math.Cos(SpiritArtifactCarouselPolicy.ForwardTiltDegrees * Math.PI / 180d),
            0.01f,
            "Compact equipment preserves the same upper and lower circle margins");

        var sandsTarget = SpiritArtifactCarouselPolicy.FocusTargetPhase(SpiritArtifactSlots.Sands);
        var front = SpiritArtifactCarouselPolicy.CalculatePoint(
            geometry,
            SpiritArtifactSlots.Sands,
            sandsTarget);
        var focusedPoints = slotOrder
            .Select(slot => SpiritArtifactCarouselPolicy.CalculatePoint(geometry, slot, sandsTarget))
            .ToArray();
        var back = focusedPoints.OrderBy(point => point.Depth).First();
        var expectedFrontDepth = geometry.Radius
                                 * (float)Math.Sin(SpiritArtifactCarouselPolicy.ForwardTiltDegrees * Math.PI / 180d);
        Approximately(expectedFrontDepth, front.Depth, 0.001f,
            "A focused equipment slot reaches the lower screen-outward point");
        True(front.Y < 0f,
            "The player-facing focus point is below the portrait rather than on the X axis");
        True(front.Scale > back.Scale && front.Alpha > back.Alpha,
            "The fifteen-degree shallow carousel makes the front slot visually dominant");
        Approximately(front.CircleX, front.X, 0.001f,
            "The forward tilt does not use horizontal X as the depth axis");
        True(Math.Abs(front.Y) < Math.Abs(front.CircleY),
            "The fifteen-degree forward tilt compresses the projected vertical axis");
        var right = SpiritArtifactCarouselPolicy.CalculatePoint(geometry, SpiritArtifactSlots.Sands, 0f);
        var left = SpiritArtifactCarouselPolicy.CalculatePoint(geometry, SpiritArtifactSlots.Plume, 0f);
        Approximately(right.Depth, left.Depth, 0.001f,
            "Left and right slots at the same height have the same player-facing depth");
        Approximately(right.Scale, left.Scale, 0.001f,
            "Near/far scaling follows Z depth instead of horizontal position");

        var dwell = SpiritArtifactCarouselPolicy.SampleAutomaticMotion(0f, 3.1f);
        Approximately(0f, dwell.PhaseDegrees, 0.001f,
            "Automatic carousel dwell performs no sub-pixel RectTransform writes");
        False(dwell.Moving || dwell.CycleComplete,
            "Automatic carousel remains crisp throughout its dwell interval");
        var midpoint = SpiritArtifactCarouselPolicy.SampleAutomaticMotion(0f, 3.6f);
        Approximately(324f, midpoint.PhaseDegrees, 0.001f,
            "Automatic carousel uses eased clockwise motion through the step midpoint");
        True(midpoint.Moving && !midpoint.CycleComplete,
            "The automatic transition exposes one explicit moving state");
        var completedStep = SpiritArtifactCarouselPolicy.SampleAutomaticMotion(0f, 4f);
        Approximately(288f, completedStep.PhaseDegrees, 0.001f,
            "Each automatic cycle advances exactly one seventy-two-degree vertex");
        True(completedStep.CycleComplete,
            "The automatic cycle returns to a stable dwell after the transition");
        Approximately(20f, SpiritArtifactCarouselPolicy.AutomaticCycleSeconds * 5f, 0.001f,
            "Five dwell-and-transition cycles complete one twenty-second revolution");
        Approximately(-108f,
            SpiritArtifactCarouselPolicy.ShortestFocusDelta(0f, SpiritArtifactSlots.Sands),
            0.001f,
            "Focus chooses the short clockwise route when appropriate");
        Approximately(80f,
            SpiritArtifactCarouselPolicy.ShortestFocusDelta(100f, SpiritArtifactSlots.Flower),
            0.001f,
            "Focus chooses the short counter-clockwise route when appropriate");
        True(SpiritArtifactCarouselPolicy.FocusDuration(180f)
             <= SpiritArtifactCarouselPolicy.MaximumFocusSeconds,
            "Functional focus motion always completes in under half a second");
    }

    private static void TestSpiritArtifactTargetSelectorLayout()
    {
        var large = SpiritArtifactTargetSelectorLayoutPolicy.Calculate(760f, 620f);
        Approximately(680f, large.Width, 0.01f,
            "The target selector caps its width inside the artifact workspace");
        Approximately(480f, large.Height, 0.01f,
            "The target selector caps its height inside the artifact workspace");
        Equal(4, large.Columns,
            "A normal artifact workspace presents twelve target sets as four columns");
        True(large.CellWidth >= 140f && large.GridHeight + 108f <= large.Height + 0.01f,
            "The centered target selector keeps cards and footer inside its modal bounds");

        var compactLayout = SpiritArtifactTargetSelectorLayoutPolicy.Calculate(560f, 500f);
        Equal(3, compactLayout.Columns,
            "A compact artifact workspace switches to three target-set columns");
        True(compactLayout.Width <= 560f && compactLayout.Height <= 500f,
            "Responsive target selection never exceeds its owning workspace");
    }

    private static void TestSpiritArtifactRowRingPolicy()
    {
        Equal(0, SpiritArtifactRowRingPolicy.IncomingRowCount(4, 4, 6),
            "Scrolling inside one logical row performs no artifact-cell binding");
        Equal(1, SpiritArtifactRowRingPolicy.IncomingRowCount(4, 5, 6),
            "Crossing one row binds only one incoming pooled row");
        Equal(2, SpiritArtifactRowRingPolicy.IncomingRowCount(5, 3, 6),
            "Reverse scrolling rotates exactly the rows entering from above");
        Equal(6, SpiritArtifactRowRingPolicy.IncomingRowCount(2, 12, 6),
            "A jump beyond the pool performs one bounded full rebind");
        True(SpiritArtifactRowRingPolicy.RequiresFullRebind(-1, 0, 6),
            "The first artifact render initializes every active pooled row once");
        False(SpiritArtifactRowRingPolicy.RequiresFullRebind(8, 9, 6),
            "Ordinary row scrolling never falls back to the retired full-viewport path");
    }

    private static float CircleDistance(
        SpiritArtifactCarouselPoint left,
        SpiritArtifactCarouselPoint right)
    {
        var x = left.CircleX - right.CircleX;
        var y = left.CircleY - right.CircleY;
        return (float)Math.Sqrt(x * x + y * y);
    }

    private static void TestSpiritArtifactSelectionState()
    {
        var state = new SpiritArtifactSelectionState();
        var selected = state.Toggle("artifact-a");
        True(selected.Changed && selected.HasSelection,
            "Clicking an unselected artifact enters the detail-preview state");
        Equal("artifact-a", state.SelectedArtifactUid,
            "Artifact selection stores one authoritative instance id");

        var cleared = state.Toggle("artifact-a");
        True(cleared.Changed && !cleared.HasSelection,
            "Clicking the selected artifact again returns to loadout summary");

        state.Select("artifact-a");
        state.Select("artifact-b");
        Equal("artifact-b", state.SelectedArtifactUid,
            "Clicking a different artifact switches selection without an intermediate empty state");
        var retained = state.Reconcile(new[] { "artifact-a", "artifact-b" });
        False(retained.Changed,
            "Data refresh retains a selected artifact that still exists");
        var removed = state.Reconcile(new[] { "artifact-a" });
        True(removed.Changed && state.SelectedArtifactUid.Length == 0,
            "Dismantling the selected artifact deterministically restores loadout summary");
        False(state.Clear().Changed,
            "Repeated blank-area cancellation is idempotent");
    }

    private static void TestSpiritArtifactBatchSelectionState()
    {
        var state = new SpiritArtifactBatchSelectionState();
        state.Enter("artifact-a");
        True(state.IsActive && state.Count == 1 && state.Contains("artifact-a"),
            "Artifact batch mode carries the current detail selection into cleanup selection");
        True(state.Toggle("artifact-b") && state.Count == 2,
            "Artifact batch clicks add exact instance ids without binding cell objects");
        False(state.Toggle("artifact-a"),
            "Artifact batch clicks deterministically remove an already selected instance");
        state.Add(new[] { "artifact-c", "artifact-d" });
        state.Reconcile(new[] { "artifact-b", "artifact-c" });
        True(state.Count == 2 && state.Contains("artifact-b") && state.Contains("artifact-c"),
            "Artifact batch refresh removes vanished instances while retaining off-screen selections");
        state.Remove(new[] { "artifact-c" });
        True(state.Count == 1 && !state.Contains("artifact-c"),
            "New preset protection can remove a previously selected cleanup candidate");
        state.Replace(new[] { "artifact-e", "artifact-f" });
        True(state.Count == 2 && state.Contains("artifact-f"),
            "Select-all replaces the batch with the authoritative filtered candidate set");
        state.Exit();
        False(state.IsActive || state.Count > 0,
            "Leaving batch mode clears its transient account inventory state");
    }

    private static void TestSpiritArtifactPresetLayoutPolicy()
    {
        var compact = SpiritArtifactPresetLayoutPolicy.Calculate(560f, 500f);
        Approximately(520f, compact.Width, 0.01f,
            "Compact preset manager preserves a 20-pixel workspace margin on both sides");
        Approximately(180f, compact.ListWidth, 0.01f,
            "Compact preset manager reserves a readable master-list column");
        Approximately(50f, compact.MiniCardWidth, 0.01f,
            "Compact preset details fit five complete artifact slots without overflow");
        True(5f * compact.MiniCardWidth + 4f * compact.MiniCardSpacing
             <= compact.Width - 32f - compact.ListWidth - 12f - 24f,
            "Compact preset slot row fits inside the detail pane after all padding and master-detail spacing");
        var expanded = SpiritArtifactPresetLayoutPolicy.Calculate(760f, 560f);
        Approximately(680f, expanded.Width, 0.01f,
            "Expanded preset manager caps its reading width instead of stretching");
        Approximately(210f, expanded.ListWidth, 0.01f,
            "Expanded preset manager gives names and status a wider list column");
        True(5f * expanded.MiniCardWidth + 4f * expanded.MiniCardSpacing
             <= expanded.Width - 32f - expanded.ListWidth - 12f - 24f,
            "Expanded preset slot row preserves detail-pane breathing room");
        Equal(5, SpiritArtifactPresetLayoutPolicy.VisibleRows(332f),
            "Scrollable preset list exposes a stable bounded number of 60-pixel rows");
    }

    private static void TestSpiritVirtualGridRefreshState()
    {
        var state = new SpiritVirtualGridRefreshState();
        True(state.Request(false), "The first virtual-grid refresh request owns the next-frame slot");
        False(state.Request(false), "Repeated scroll refreshes coalesce into the pending next-frame slot");
        False(state.Request(true), "A resize joins rather than duplicates an already pending grid refresh");
        True(state.Drain(), "A coalesced resize upgrades the pending refresh to a forced rebind");
        True(state.Request(false), "The grid can schedule a new refresh after the previous drain");
        False(state.Drain(), "An ordinary scroll refresh preserves the non-forced path");
        True(state.Request(true), "A later resize can schedule independently");
        state.Cancel();
        True(state.Request(false), "Closing a grid cancels stale pending state for its next lifecycle");
        state.Reset();
        True(state.ObserveViewport(640f, 320f),
            "The first viewport observation schedules a responsive grid refresh");
        False(state.ObserveViewport(640f, 320f),
            "Repeated Unity layout callbacks at the same size do not form a refresh loop");
        False(state.ObserveViewport(640.1f, 320.1f),
            "Sub-pixel layout jitter is ignored");
        True(state.ObserveViewport(720f, 320f),
            "A material viewport width change schedules one new refresh");
    }

    private static void TestSpiritArtifactWishNavigationState()
    {
        var state = new SpiritArtifactWishNavigationState();
        state.Reset();
        Equal(
            SpiritArtifactWishNavigationAction.SkipToResults,
            state.RequestEscape(),
            "Escape during the wish video skips forward without discarding the committed result");

        state.MarkResultsVisible();
        Equal(
            SpiritArtifactWishNavigationAction.AcknowledgeAndClose,
            state.RequestEscape(),
            "Escape on the result screen acknowledges the receipt and returns to the artifact page");
        Equal(
            SpiritArtifactWishNavigationAction.None,
            state.RequestClose(),
            "Repeated result-close input is idempotent");

        state.Reset();
        state.MarkResultsVisible();
        Equal(
            SpiritArtifactWishNavigationAction.AcknowledgeAndClose,
            state.RequestClose(),
            "The visible confirmation button follows the same close transaction as Escape");
    }

    private static void TestSpiritArtifactCardStylePolicy()
    {
        Equal(9, SpiritArtifactCardStylePolicy.ColumnsForWidth(808f),
            "The artifact workspace uses its former right-side gap for a ninth column");
        Equal(7, SpiritArtifactCardStylePolicy.ColumnsForWidth(647f),
            "Narrow artifact workspaces reduce columns without enlarging cards");
        Equal(10, SpiritArtifactCardStylePolicy.ColumnsForWidth(890f),
            "Wide artifact workspaces can use ten fixed-size card columns");
        Equal(38, SpiritArtifactCardStylePolicy.HorizontalPaddingForWidth(808f, 9),
            "Unused sub-column width is balanced across both inventory edges");
        True(
            SpiritArtifactCardStylePolicy.CardWidth * SpiritArtifactCardStylePolicy.CardHeight
            < 108f * 126f * 0.5f,
            "The redesigned artifact card occupies less than half the previous area");
        Approximately(16f,
            SpiritArtifactCardStylePolicy.CardHeight - SpiritArtifactCardStylePolicy.ArtHeight,
            0.01f,
            "The rarity artwork leaves a stable white level footer");
        True(SpiritArtifactCardStylePolicy.ArtBottomRightRadius
             > SpiritArtifactCardStylePolicy.CardRadius,
            "The rarity artwork owns the approved enlarged bottom-right corner");
        Approximately(2f, SpiritArtifactCardStylePolicy.HoverStrokeWidth, 0.01f,
            "Hover uses a two-pixel aligned stroke");
        True(SpiritArtifactCardStylePolicy.SelectionHaloWidth
             > SpiritArtifactCardStylePolicy.CardWidth
             && SpiritArtifactCardStylePolicy.SelectionHaloHeight
             > SpiritArtifactCardStylePolicy.CardHeight,
            "Selection uses a centered under-card halo whose covered center cannot expose a seam");

        var start = SpiritArtifactCardMotionPolicy.SelectionPulse(0f);
        var peak = SpiritArtifactCardMotionPolicy.SelectionPulse(
            SpiritArtifactCardMotionPolicy.SelectionPeriodSeconds * 0.5f);
        var loop = SpiritArtifactCardMotionPolicy.SelectionPulse(
            SpiritArtifactCardMotionPolicy.SelectionPeriodSeconds);
        Approximately(0.58f, start.Alpha, 0.001f,
            "Selection breathing starts from the restrained alpha floor");
        Approximately(0.96f, peak.Alpha, 0.001f,
            "Selection breathing reaches one clear visual peak per cycle");
        Approximately(start.Alpha, loop.Alpha, 0.001f,
            "Selection breathing loops without a discontinuity");
        True(peak.Scale <= 1.0181f,
            "Selection breathing expands the covered halo without detaching it from the card");
        True(SpiritArtifactCardMotionPolicy.ShouldRestartSelection(false, true),
            "Selection motion starts only on an actual unselected-to-selected transition");
        False(SpiritArtifactCardMotionPolicy.ShouldRestartSelection(true, true),
            "Virtualized rebinds do not restart an already selected card's phase");
        False(SpiritArtifactCardMotionPolicy.ShouldRestartSelection(true, false),
            "Clearing selection does not create a new breathing phase");
    }

    private static void TestSpiritWarehouseSelectionPolicy()
    {
        var visible = new[] { "first", "active", "remembered" };
        Equal("remembered", SpiritWarehouseSelectionPolicy.ResolveInitial("remembered", "active", visible),
            "Warehouse reopening restores the last manually selected visible spirit");
        Equal("active", SpiritWarehouseSelectionPolicy.ResolveInitial("missing", "active", visible),
            "Warehouse reopening falls back to the active visible spirit");
        Equal("first", SpiritWarehouseSelectionPolicy.ResolveInitial("missing", "also-missing", visible),
            "Warehouse reopening finally falls back to the first sorted visible spirit");
        Equal("", SpiritWarehouseSelectionPolicy.ResolveInitial("remembered", "active", Array.Empty<string>()),
            "An empty warehouse has no selected spirit");

        Equal("active", SpiritWarehouseSelectionPolicy.ResolveVisible("active", visible),
            "Changing sort order preserves a selection that remains visible");
        Equal("first", SpiritWarehouseSelectionPolicy.ResolveVisible("filtered-out", visible),
            "Filtering out the selection chooses the first sorted visible spirit");
        Equal("first", SpiritWarehouseSelectionPolicy.ResolveVisible(" ", new[] { " ", "first", "first" }),
            "Warehouse selection ignores blank and duplicate visible identifiers");
    }

    private static void TestSpiritStatusBarText()
    {
        Equal("1\n3\n9", SpiritStatusBarText.FormatVerticalDigits(139), "Spirit health shows exactly one horizontal digit per vertical line");
        Equal("7", SpiritStatusBarText.FormatVerticalDigits(7), "Single-digit spirit health remains on one line");
        Equal("0", SpiritStatusBarText.FormatVerticalDigits(-5), "Spirit health text clamps invalid negative values");
    }

    private static void TestPolymorphCooldownSnapshots()
    {
        var initialized = new Dictionary<string, int>
        {
            ["skill_a"] = 2,
            ["skill_b"] = -3
        };
        var firstEntry = PolymorphCooldownSnapshotPolicy.ResolveEntry(
            new[] { "skill_a", "skill_b" },
            initialized,
            null);
        Equal(2, firstEntry["skill_a"], "A first polymorph entry keeps the target role's configured initial cooldown");
        Equal(0, firstEntry["skill_b"], "Polymorph cooldown snapshots clamp invalid negative values");

        var revisit = PolymorphCooldownSnapshotPolicy.ResolveEntry(
            new[] { "skill_a", "skill_b" },
            initialized,
            new Dictionary<string, int> { ["skill_a"] = 5 });
        Equal(5, revisit["skill_a"], "Re-entering a form restores that form's saved cooldown");
        Equal(0, revisit["skill_b"], "A missing saved skill falls back to the role's initialized cooldown");
    }

    private static void TestMorningStarBlessingFormula()
    {
        Equal(0, MorningStarBlessingFormula.MissingHealthRecovery(100, 100), "Withered One does not heal at full health");
        Equal(1, MorningStarBlessingFormula.MissingHealthRecovery(100, 1), "Withered One heals at least one HP when health is missing");
        Equal(1, MorningStarBlessingFormula.MissingHealthRecovery(300, 101), "Withered One follows the base-game integer one-percent precedent");
        Equal(2, MorningStarBlessingFormula.MissingHealthRecovery(300, 100), "Withered One heals two HP at two hundred missing health");
        Equal(0, MorningStarBlessingFormula.MissingHealthRecovery(-5, -9), "Withered One normalizes invalid health values safely");
    }

    private static void TestRuntimeMemberApi()
    {
        Equal(42, RuntimeMemberApi.ReadStaticMember(typeof(RuntimeMemberFixture), nameof(RuntimeMemberFixture.Healthy)),
            "runtime member access reads an initialized static property");
        Equal(7, RuntimeMemberApi.ReadStaticMember(typeof(RuntimeMemberFixture), nameof(RuntimeMemberFixture.HealthyField)),
            "runtime member access falls back to a public static field");
        Equal(null, RuntimeMemberApi.ReadStaticMember(typeof(RuntimeMemberFixture), nameof(RuntimeMemberFixture.Unavailable)),
            "runtime member access isolates a host getter that throws before its context exists");
        Equal(null, RuntimeMemberApi.ReadStaticMember(typeof(RuntimeMemberFixture), "Missing"),
            "runtime member access returns null for a missing member");
        Equal(null, RuntimeMemberApi.ReadStaticMember(null, nameof(RuntimeMemberFixture.Healthy)),
            "runtime member access rejects a missing host type");
        Equal(42, RuntimeMemberApi.ReadStaticNonNegativeInt(typeof(RuntimeMemberFixture), nameof(RuntimeMemberFixture.Healthy)),
            "player economy reads an initialized non-negative value");
        Equal(0, RuntimeMemberApi.ReadStaticNonNegativeInt(typeof(RuntimeMemberFixture), nameof(RuntimeMemberFixture.Unavailable)),
            "player economy falls back to zero when the native getter has no role context");
        Equal(0, RuntimeMemberApi.ReadStaticNonNegativeInt(typeof(RuntimeMemberFixture), nameof(RuntimeMemberFixture.Negative)),
            "player economy clamps an invalid negative runtime value");
    }

    private static void TestMorningStarCurseFormula()
    {
        Equal(5, MorningStarCurseFormula.AllBeingsAspectFallbackVowPower, "All-Beings Aspect grants five Vow Power after all blessings are owned");
        Equal(50, MorningStarCurseFormula.ElegyHealthLoss(100), "Morning Star Elegy loses half of current HP");
        Equal(0, MorningStarCurseFormula.ElegyHealthLoss(1), "Morning Star Elegy cannot directly kill a one-HP owner");
        Equal(7, MorningStarCurseFormula.ElegyTriggerCount(50, 100), "Morning Star Elegy reaches seven triggers at the full-health example");
        Equal(0, MorningStarCurseFormula.ElegyTriggerCount(0, 100), "Morning Star Elegy creates no Curse when no HP was lost");
        Equal(7, MorningStarCurseFormula.ElegyTriggerCount(int.MaxValue, 1), "Morning Star Elegy clamps extreme values to seven triggers");

        Equal(270L, MorningStarCurseFormula.BlackSunCrossTheoreticalRecovery(200, 135), "Black Sun Cross does not cap Vow Power at one hundred");
        Equal(150, MorningStarCurseFormula.BlackSunCrossRecovery(200, 50, 135), "Black Sun Cross caps healing only at missing HP");
        Equal(1, MorningStarCurseFormula.BlackSunCrossRecovery(200, 199, 1), "Black Sun Cross heals at least one HP when recovery is positive");
        Equal(0, MorningStarCurseFormula.BlackSunCrossRecovery(200, 200, 135), "Black Sun Cross does not heal at full HP");
        Equal(int.MaxValue, MorningStarCurseFormula.BlackSunCrossRecovery(int.MaxValue, 0, int.MaxValue), "Black Sun Cross saturates safely for extreme values");

        Equal(4, MorningStarCurseFormula.NormalizeTier(99), "Curse fallback rarity is capped at tier four");
        Equal(1, MorningStarCurseFormula.NormalizeTier(-1), "Invalid Curse rarity falls back to tier one");
        Equal(1, MorningStarCurseFormula.ImpregnableGain(7, 5), "Curse reversal respects the native Impregnable cap");
        Equal(0, MorningStarCurseFormula.ImpregnableGain(8, 1), "Curse reversal cannot exceed eight Impregnable");

        var knownStarlight = MorningStarCurseReversalRegistry.Resolve("cursecard_2", 3);
        Equal(6, knownStarlight.Starlight, "Disordered Thoughts uses its registered Starlight reversal");
        var knownPower = MorningStarCurseReversalRegistry.Resolve("Terrias_cursecard_abyss_deficit", 4);
        Equal(1, knownPower.Power, "Terrias Deficit uses its registered Mana reversal");
        var unknown = MorningStarCurseReversalRegistry.Resolve("OtherMod_cards_unknown_curse", 99);
        Equal(4, unknown.VowPower, "Unknown Curse fallback grants normalized-tier Vow Power");
        Equal(4, unknown.Starlight, "Unknown Curse fallback grants normalized-tier Starlight");

        Equal(
            3,
            MorningStarCurseFormula.DistinctBlessingCount(
                new[] { "dream_talker", "dream_talker", "wisher", "blind_one", "other" },
                new[] { "dream_talker", "wisher", "blind_one" }),
            "All-Beings Wish counts distinct registered blessing ids only");

        var executor = new ScriptExecutor();
        var config = new DataConfig(
            new Dictionary<string, string> { ["Id"] = "cursecard_1", ["Tag"] = "Curse" },
            new Dictionary<string, string>());
        var cardItem = new CardItem { dataConfig = config };
        executor.HandCard.Add(cardItem);
        Witch.UI.Window.FightUI.cardItemList.Add(cardItem);
        var snapshot = AuraCombatCardZoneSnapshot.Capture(executor);
        Equal(1, snapshot.Cards.Count, "Combat card zone snapshots deduplicate the same runtime card across UI and executor hand references");
        Witch.UI.Window.FightUI.cardItemList.Clear();
    }

    private static void TestSunCardPackSelectionMigration()
    {
        var selected = new HashSet<string>(StringComparer.Ordinal)
        {
            TerriasIds.RadiantSparkCardPackId,
            "cardpack_ember_crown",
            "base_pack"
        };
        True(SunCardPackSelectionMigration.Apply(selected), "Legacy selected Solar packs migrate to the consolidated pack");
        True(selected.SetEquals(new[] { TerriasIds.SolarEmberCrownCanopyCardPackId, "base_pack" }), "Migration removes all legacy Solar selections and preserves unrelated packs");
        False(SunCardPackSelectionMigration.Apply(selected), "Canonical Solar pack selection is idempotent");

        var disabled = new HashSet<string>(StringComparer.Ordinal) { "base_pack" };
        False(SunCardPackSelectionMigration.Apply(disabled), "Migration does not enable the consolidated Solar pack when no legacy pack was selected");
        False(disabled.Contains(TerriasIds.SolarEmberCrownCanopyCardPackId), "Disabled Solar packs stay disabled after migration");
    }

    private static void TestProjectionTurnQueuePolicy()
    {
        var nativePartner = ProjectionTurnQueueKind.NativePartner;
        False(ProjectionTurnQueuePolicy.ShouldRemoveLegacyAnchor(nativePartner),
            "native Partner action units remain in the native queue");
        False(ProjectionTurnQueuePolicy.ShouldRemoveLegacyAnchor(ProjectionTurnQueueKind.TerriasProjection),
            "Terrias projections remain directly queued in the Partner phase");
        False(ProjectionTurnQueuePolicy.ShouldRemoveLegacyAnchor(ProjectionTurnQueueKind.TerriasSpirit),
            "Terrias spirits remain directly queued in the Partner phase");
        True(ProjectionTurnQueuePolicy.ShouldRemoveLegacyAnchor(ProjectionTurnQueueKind.TerriasAnchor),
            "native Partner queue cleanup removes stale Terrias anchors");

        var isolated = ProjectionTurnQueuePolicy.Analyze(new[]
        {
            ProjectionTurnQueueKind.Other,
            ProjectionTurnQueueKind.NativePartner,
            ProjectionTurnQueueKind.NativePartner,
            ProjectionTurnQueueKind.Other,
            ProjectionTurnQueueKind.TerriasProjection,
            ProjectionTurnQueueKind.TerriasSpirit
        });
        True(isolated.IsIsolated, "direct Terrias partners coexist without a hidden anchor");
        Equal(2, isolated.NativePartnerCount, "queue diagnostics preserve all native Partner action units");
        Equal(1, isolated.DirectProjectionCount, "queue diagnostics preserve the direct projection actor");
        Equal(1, isolated.DirectSpiritCount, "queue diagnostics preserve the direct spirit actor");

        var conflicted = ProjectionTurnQueuePolicy.Analyze(new[]
        {
            ProjectionTurnQueueKind.NativePartner,
            ProjectionTurnQueueKind.TerriasProjection,
            ProjectionTurnQueueKind.TerriasSpirit,
            ProjectionTurnQueueKind.TerriasAnchor
        });
        False(conflicted.IsIsolated, "any stale anchor is reported as a native Partner queue conflict");
        Equal(1, conflicted.NativePartnerCount, "conflict diagnostics do not classify native Partner as a Terrias actor");
        Equal(1, conflicted.AnchorCount, "conflict diagnostics expose the stale Terrias anchor");
    }

    private static void TestPartnerTurnOrderPolicy()
    {
        var source = new[]
        {
            new TurnEntry("player", false, 100),
            new TurnEntry("slow", true, 80),
            new TurnEntry("enemy", false, 100),
            new TurnEntry("fast-a", true, 120),
            new TurnEntry("fast-b", true, 120),
            new TurnEntry("tail", false, 100)
        };
        var ordered = PartnerTurnOrderPolicy.ReorderPartnerSubsequence(
            source,
            value => value.IsPartner,
            value => value.Speed,
            value => value.Id);
        Equal("player,fast-a,enemy,fast-b,slow,tail", string.Join(",", ordered.Select(value => value.Id)),
            "Partner speed sorting preserves all non-Partner positions and keeps equal-speed Partners stable");
    }

    private static void TestFriendlyRoleSeatPolicy()
    {
        Equal(1, FriendlyRoleSeatPolicy.FindOpenSeat(1, Array.Empty<int>(), Array.Empty<int>()),
            "single-player combat leaves three formal role seats available for projections");
        Equal(3, FriendlyRoleSeatPolicy.FindOpenSeat(3, Array.Empty<int>(), Array.Empty<int>()),
            "three-player combat leaves exactly one formal projection seat");
        Equal(-1, FriendlyRoleSeatPolicy.FindOpenSeat(4, Array.Empty<int>(), Array.Empty<int>()),
            "four real players fill the friendly role-seat cap");
        Equal(-1, FriendlyRoleSeatPolicy.FindOpenSeat(2, new[] { 2 }, new[] { 3 }),
            "active and preparing projections share the same four-seat cap");
        Equal(2, FriendlyRoleSeatPolicy.FindOpenSeat(2, new[] { 3 }, new[] { 99, -1 }),
            "invalid companion slots never consume a formal role seat");
    }

    private static void TestProjectionActorDeckProjection()
    {
        True(ProjectionActionAutomationPolicy.DeclaresHeadlessExecutionRoute(
                "projection-card:Terrias_terrias_solar_return"),
            "projection card automation is declared by its stable runtime route rather than an AI feature");
        False(ProjectionActionAutomationPolicy.DeclaresHeadlessExecutionRoute(
                "projection-card:"),
            "an empty projection-card source cannot claim the autonomous execution route");
        False(ProjectionActionAutomationPolicy.DeclaresHeadlessExecutionRoute(
                "projection:end-turn"),
            "projection end-turn is not misdeclared as a card execution route");
        False(ProjectionActorTurnPolicy.CanEndTurn(1),
            "an autonomous projection cannot end while an actor-safe action is legal");
        True(ProjectionActorTurnPolicy.CanEndTurn(0),
            "an autonomous projection may end only after no legal card action remains");
        var source = new ProjectionDeckRecipe(new[]
        {
            new ProjectionDeckCardRecipe("safe-a"),
            new ProjectionDeckCardRecipe("unsafe-a"),
            new ProjectionDeckCardRecipe("safe-a"),
            new ProjectionDeckCardRecipe("unsafe-b")
        });
        var basic = new ProjectionDeckCardRecipe("projection-basic");
        var inspected = 0;
        var filtered = ProjectionActorDeckProjection.Build(
            source,
            card =>
            {
                inspected++;
                return card.CardId.StartsWith("safe", StringComparison.Ordinal)
                    ? ProjectionDeckCardCapability.Safe()
                    : ProjectionDeckCardCapability.Reject("player-only");
            },
            basic);
        True(filtered.Success && !filtered.UsesBasicAction,
            "actor deck keeps the authoritative safe subset without injecting the basic action");
        True(filtered.EffectiveRecipe!.Cards.Select(card => card.CardId)
                .SequenceEqual(new[] { "safe-a", "safe-a" }),
            "actor deck preserves duplicate safe cards while removing unsafe recipes before shuffle");
        Equal(2, filtered.RejectedCards.Count,
            "actor deck reports every removed unsafe recipe once");
        Equal(3, inspected,
            "actor deck inspects duplicate card-and-attachment recipes only once");

        var allUnsafe = ProjectionActorDeckProjection.Build(
            new ProjectionDeckRecipe(new[]
            {
                new ProjectionDeckCardRecipe("unsafe-a"),
                new ProjectionDeckCardRecipe("unsafe-b")
            }),
            card => card.CardId == "projection-basic"
                ? ProjectionDeckCardCapability.Safe()
                : ProjectionDeckCardCapability.Reject("unsupported"),
            basic);
        True(allUnsafe.Success && allUnsafe.UsesBasicAction,
            "an empty actor-safe projection receives the explicit basic action contract");
        True(allUnsafe.EffectiveRecipe!.Cards.Select(card => card.CardId)
                .SequenceEqual(new[] { "projection-basic" }),
            "the basic action is the only executable card when every owner card is unsafe");

        var brokenBasic = ProjectionActorDeckProjection.Build(
            new ProjectionDeckRecipe(new[] { new ProjectionDeckCardRecipe("unsafe") }),
            _ => ProjectionDeckCardCapability.Reject("missing definition"),
            basic);
        False(brokenBasic.Success,
            "summon initialization fails closed when even the required basic action is unavailable");
        True(brokenBasic.FailureReason.Contains("basic action", StringComparison.Ordinal),
            "failed basic action projection returns an explicit initialization reason");
        False(ProjectionActorDeckProjection.Build(null, _ => ProjectionDeckCardCapability.Safe(), basic).Success,
            "a missing authoritative role deck is not silently replaced by the basic action");
    }

    private static void TestProjectionCardExecutionPolicy()
    {
        const string cardScript = "CS.Terrias.Dll.Scripting.CardScripts.Init(self, id);"
                                  + "CS.Terrias.Dll.Scripting.CardScripts.Use(self, id);";
        Equal(
            ProjectionCardExecutionMode.ActorSafe,
            ProjectionCardExecutionPolicy.Resolve(
                null,
                TerriasIds.ProjectionBasicActionCardId,
                cardScript).Mode,
            "projection basic action is an explicit actor-safe Terrias card");
        Equal(
            ProjectionCardExecutionMode.Unsupported,
            ProjectionCardExecutionPolicy.Resolve(
                null,
                "Terrias_terrias_gilded_butterfly",
                cardScript).Mode,
            "an undeclared player-hand mutation card is rejected before entering the actor deck");
        Equal(
            ProjectionCardExecutionMode.ActorSafe,
            ProjectionCardExecutionPolicy.Resolve(
                null,
                "official_card",
                "self.Damage(5);").Mode,
            "a native card without a custom C# bridge retains actor-safe compatibility");
        False(
            ProjectionCardExecutionPolicy.IsHeadlessScriptSurfaceSafe(
                "FightPlayer.Instance.ChangePower(1);",
                out var reason),
            "player-only runtime tokens are rejected during actor-deck projection");
        True(reason.Contains("FightPlayer", StringComparison.Ordinal),
            "actor-deck rejection identifies the player-only dependency");
    }

    private static void TestProjectionDeckRecipe()
    {
        var first = new ProjectionDeckRecipe(new[]
        {
            new ProjectionDeckCardRecipe("card_b"),
            new ProjectionDeckCardRecipe("card_a", attachmentId: "ench_1"),
            new ProjectionDeckCardRecipe("card_a", attachmentId: "ench_1")
        });
        var reordered = new ProjectionDeckRecipe(new[]
        {
            new ProjectionDeckCardRecipe("card_a", attachmentId: "ench_1"),
            new ProjectionDeckCardRecipe("card_b"),
            new ProjectionDeckCardRecipe("card_a", attachmentId: "ench_1")
        });
        Equal(first.Hash, reordered.Hash,
            "projection deck hash is based on the card multiset rather than RoleTable order");

        var changedAttachment = new ProjectionDeckRecipe(new[]
        {
            new ProjectionDeckCardRecipe("card_b"),
            new ProjectionDeckCardRecipe("card_a", attachmentId: "ench_2"),
            new ProjectionDeckCardRecipe("card_a", attachmentId: "ench_1")
        });
        False(string.Equals(first.Hash, changedAttachment.Hash, StringComparison.Ordinal),
            "projection deck hash detects persistent attachment changes");
        Equal(first.BaseHash, changedAttachment.BaseHash,
            "projection client diagnostic hash excludes attachment state owned by the host");

        var oversized = new ProjectionDeckRecipe(Enumerable.Range(0, ProjectionDeckRecipe.MaximumCards + 20)
            .Select(index => new ProjectionDeckCardRecipe("card_" + index)));
        Equal(ProjectionDeckRecipe.MaximumCards, oversized.Cards.Count,
            "projection deck recipes enforce the lightweight actor card cap");
        Equal(first.ShuffleSeed, reordered.ShuffleSeed,
            "matching deck recipes produce the same deterministic shuffle seed");

        Equal(ProjectionRoleDeckSourceKind.ServerRole,
            ProjectionRoleDeckSourcePolicy.Select(true, false, true, true),
            "a remote projection always reads the server-owned RoleTable");
        Equal(ProjectionRoleDeckSourceKind.None,
            ProjectionRoleDeckSourcePolicy.Select(true, false, true, false),
            "a remote projection never falls back to the host's local RoleTable");
        Equal(ProjectionRoleDeckSourceKind.LocalRole,
            ProjectionRoleDeckSourcePolicy.Select(true, true, true, true),
            "the host projection may use the host's current local RoleTable");
    }

    private static void TestProjectionProtocolState()
    {
        var retryable = ProjectionSummonFailureCatalog.Describe(
            ProjectionSummonFailureCode.RoleDeckUnavailable);
        False(retryable.Terminal,
            "a temporarily unavailable host RoleTable does not terminally reject the summon token");
        True(retryable.Retryable,
            "a temporarily unavailable host RoleTable asks the client to retry the same token");
        False(retryable.RefundCard,
            "ambiguous synchronization failures never refund before a terminal result");
        Equal("caption.projection.failure.RoleDeckUnavailable",
            ProjectionSummonFailureCatalog.LocalizationKey(ProjectionSummonFailureCode.RoleDeckUnavailable),
            "projection failures expose a stable localization key instead of a host-language message");
        Equal("caption.projection.failure.SpawnFailed",
            ProjectionSummonFailureCatalog.LocalizationKey(ProjectionSummonFailureCode.None),
            "an invalid display failure safely falls back to the generic stable key");
        var roleDeckTimedOut = ProjectionSummonFailureCatalog.Describe(
            ProjectionSummonFailureCode.RoleDeckTimedOut);
        True(roleDeckTimedOut.Terminal,
            "the host can terminally close a RoleTable wait after bounded same-token retries");
        True(roleDeckTimedOut.RefundCard,
            "a host-confirmed RoleTable timeout returns the consumed role card");

        var pending = new ProjectionSummonTransaction("token", "role", "owner", "hash", 10d);
        False(pending.ShouldExpire(39.9d, 12, 30d),
            "a Projection summon transaction remains active inside its bounded lifetime");
        True(pending.ShouldExpire(40d, 12, 30d),
            "a Projection summon transaction expires without a permanent frame poller");
        pending.SetTerminal();
        False(pending.ShouldExpire(100d, 12, 30d),
            "a terminal Projection transaction ignores stale scheduled retry wakes");

        var incompatible = ProjectionSummonFailureCatalog.Describe(
            ProjectionSummonFailureCode.ProtocolMismatch);
        True(incompatible.Terminal,
            "protocol mismatch is a typed terminal summon result");
        True(incompatible.RefundCard,
            "a terminal compatibility rejection returns the consumed role card");
        False(ProjectionSummonFailureCatalog.Describe(
                ProjectionSummonFailureCode.OwnerMismatch).RefundCard,
            "an unauthorized request cannot manufacture a local refund");
        False(ProjectionSummonFailureCatalog.Describe(
                ProjectionSummonFailureCode.TokenConflict).RefundCard,
            "reusing a pending token with different request data cannot manufacture a refund");

        var authoritative = new ProjectionReplicationClock("generation-a");
        Equal(1L, authoritative.StateRevision,
            "a spawned projection starts at the first public state revision");
        authoritative.CommitAction();
        Equal(1L, authoritative.ActionSequence,
            "committed action frames use a monotonic action sequence");
        Equal(2L, authoritative.StateRevision,
            "a committed action also advances the public state revision");
        authoritative.CompleteTurn();
        Equal(1L, authoritative.CompletedTurnSequence,
            "turn completion has an independent monotonic sequence");

        var remote = new ProjectionReplicationClock("generation-a", 0L);
        True(remote.TryApplyRemote("generation-a", 1L, 0L, 0L, true),
            "a remote mirror accepts the initial spawn revision");
        False(remote.TryApplyRemote("generation-a", 1L, 0L, 0L, true),
            "a remote mirror rejects duplicate public revisions");
        False(remote.TryApplyRemote("generation-b", 3L, 2L, 1L, true),
            "a remote mirror rejects frames from another spawn generation");
        True(remote.TryApplyRemote("generation-a", 4L, 2L, 1L, false),
            "a newer death tombstone retires the matching generation");
        False(remote.MatchesActiveGeneration("generation-a"),
            "an inactive generation rejects late action presentation frames");
        False(remote.TryApplyRemote("generation-a", 5L, 3L, 2L, true),
            "an inactive generation cannot be resurrected by a late active frame");

        var gate = new ProjectionRemoteTurnGate();
        gate.Observe(1L, 3L, 4L, 10d);
        var alreadyCompleted = gate.BeginInvocation();
        Equal(1L, alreadyCompleted,
            "a completion received before Partner.DoAction is consumed immediately");
        True(gate.IsSatisfied(alreadyCompleted),
            "pre-arrived completion satisfies the current client invocation");
        gate.Consume(alreadyCompleted);
        Equal(2L, gate.BeginInvocation(),
            "the next client invocation waits for the next completion sequence");
        True(gate.ShouldQuery(13d, 2d, 1d),
            "a stalled remote turn becomes eligible for a state query");
        gate.MarkQuery(13d);
        False(gate.ShouldQuery(13.5d, 2d, 1d),
            "state queries are rate limited while a remote turn is stalled");
        gate.Release(2L);
        Equal(3L, gate.BeginInvocation(),
            "a disconnected client can soft-release one partner invocation without running AI");

        var transaction = new ProjectionSummonTransaction(
            "token", "role", "owner", "hash", 0d);
        True(transaction.IsDue(0d, 1d),
            "a fresh summon transaction is immediately sendable");
        transaction.MarkAttempt(0d);
        False(transaction.IsDue(0.5d, 1d),
            "a summon retry keeps the same token and observes its retry interval");
        True(transaction.TryClaimRefund(),
            "a terminal summon transaction may claim one refund");
        False(transaction.TryClaimRefund(),
            "a replayed terminal result cannot refund twice");

        var requestIdentity = new ProjectionSummonRequestIdentity(
            "role", "player", "owner", "ABC");
        True(requestIdentity.Matches("role", "player", "owner", "abc"),
            "same-token retries accept the same request identity and case-insensitive hash");
        False(requestIdentity.Matches("other-role", "player", "owner", "abc"),
            "same-token retries reject changed summon content");
        False(requestIdentity.Matches("role", "other-player", "owner", "abc"),
            "same-token retries remain bound to the authoritative sender identity");
    }

    private static void TestSolarMemoryRoleCommitPendingState()
    {
        var state = new SolarMemoryRoleCommitPendingState();
        False(state.TryBegin("", "token"), "Solar Memory role commit rejects an empty player identity");
        True(state.TryBegin("player-a", "token-a"), "Solar Memory role commit tracks the first pending request");
        True(state.TryBegin("player-a", "token-a"), "Solar Memory role commit treats a repeated local submission as idempotent");
        False(state.TryBegin("player-b", "token-b"), "Solar Memory role commit keeps one unambiguous local request pending");

        var mismatchedPlayer = state.Resolve("player-b", "token-a", accepted: true);
        False(mismatchedPlayer.Matched, "Solar Memory role commit ignores an acknowledgement for another player");
        var mismatchedToken = state.Resolve("player-a", "token-b", accepted: true);
        False(mismatchedToken.Matched, "Solar Memory role commit ignores an acknowledgement for another token");
        True(state.IsPending("player-a", "token-a"), "unmatched acknowledgements leave the request pending");

        var accepted = state.Resolve("player-a", "token-a", accepted: true);
        True(accepted.Matched && accepted.Accepted, "matching host acceptance resolves the pending role commit");
        False(state.IsPending("player-a", "token-a"), "accepted role commit cannot be completed twice");

        True(state.TryBegin("player-a", "token-rejected"), "a resolved role commit permits a later retry");
        var rejected = state.Resolve("player-a", "token-rejected", accepted: false);
        True(rejected.Matched && !rejected.Accepted, "matching host rejection resolves the pending role commit as rejected");
    }

    private static void TestSolarMemoryMapPreviewPolicy()
    {
        var valid = new HashSet<string>(StringComparer.Ordinal)
        {
            "Mods/Terrias/valid",
            SolarMemoryMapPreviewPolicy.SaintWunaFallbackAnimation,
            SolarMemoryMapPreviewPolicy.GenericFightFallbackAnimation
        };
        Equal("", SolarMemoryMapPreviewPolicy.ResolveFallback(
                TerriasIds.SolarBossSaintWunaLevelId,
                "Mods/Terrias/valid",
                valid.Contains),
            "map preview keeps an original animation that the native Texture2D loader can read");
        Equal(SolarMemoryMapPreviewPolicy.SaintWunaFallbackAnimation,
            SolarMemoryMapPreviewPolicy.ResolveFallback(
                TerriasIds.SolarBossSaintWunaLevelId,
                "Mods/Terrias/unreadable",
                valid.Contains),
            "Saint Wuna map preview selects its validated native fallback");

        valid.Remove(SolarMemoryMapPreviewPolicy.SaintWunaFallbackAnimation);
        Equal(SolarMemoryMapPreviewPolicy.GenericFightFallbackAnimation,
            SolarMemoryMapPreviewPolicy.ResolveFallback(
                TerriasIds.SolarBossSaintWunaLevelId,
                "Mods/Terrias/unreadable",
                valid.Contains),
            "map preview uses the validated generic fallback when the fixed fallback is unavailable");
        valid.Clear();
        Equal("", SolarMemoryMapPreviewPolicy.ResolveFallback(
                TerriasIds.SolarBossSaintWunaLevelId,
                "Mods/Terrias/unreadable",
                valid.Contains),
            "map preview refuses to install an unvalidated fallback");
    }

    private static void TestEndlessAbyssEnemyScaling()
    {
        var config = new EndlessAbyssEnemyScalingConfig();
        var floorOne = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.Monster, config);
        var floorSix = EndlessAbyssEnemyScalingService.Calculate(6, 1, EndlessSeaNodeKind.Monster, config);
        var floorSeven = EndlessAbyssEnemyScalingService.Calculate(7, 1, EndlessSeaNodeKind.Monster, config);
        var floorThirteen = EndlessAbyssEnemyScalingService.Calculate(13, 1, EndlessSeaNodeKind.Monster, config);

        Approximately(1.0f, (float)floorOne.HpMultiplier, 0.0001f, "Endless Abyss HP scaling starts from the first floor baseline");
        Approximately(1.0f, (float)floorOne.AttackMultiplier, 0.0001f, "Endless Abyss attack scaling starts from the first floor baseline");
        Approximately(1.875f, (float)floorSix.HpMultiplier, 0.0001f, "Endless Abyss HP grows on every pre-endless floor");
        Approximately(1.1425f, (float)floorSix.AttackMultiplier, 0.0001f, "Endless Abyss attack grows on every pre-endless floor");
        Approximately(2.7196f, (float)floorSeven.HpMultiplier, 0.0001f, "Endless Abyss floor seven applies the configured HP phase jump");
        Approximately(1.316224f, (float)floorSeven.AttackMultiplier, 0.0001f, "Endless Abyss floor seven applies the configured attack phase jump");
        Approximately(5.03412f, (float)floorThirteen.HpMultiplier, 0.0001f, "Endless Abyss applies its first six-floor HP cycle after floor seven");
        Approximately(1.6002739f, (float)floorThirteen.AttackMultiplier, 0.0001f, "Endless Abyss applies its first six-floor attack cycle after floor seven");

        var floorEighty = EndlessAbyssEnemyScalingService.Calculate(80, 1, EndlessSeaNodeKind.Monster, config);
        Approximately(88.98844f, (float)floorEighty.HpMultiplier, 0.0001f, "Endless Abyss HP overflow is compressed after its soft cap");
        Approximately(8.549733f, (float)floorEighty.AttackMultiplier, 0.0001f, "Endless Abyss attack overflow is compressed after its soft cap");

        var cappedGaze = EndlessAbyssEnemyScalingService.Calculate(1, 100, EndlessSeaNodeKind.Monster, config);
        Approximately(1.5f, (float)cappedGaze.HpMultiplier, 0.0001f, "Endless Abyss gaze HP growth is capped independently");
        Approximately(1.15f, (float)cappedGaze.AttackMultiplier, 0.0001f, "Endless Abyss gaze attack growth is capped independently");

        var elite = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.Elite, config);
        var boss = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.Boss, config);
        var endlessBoss = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.EndlessBoss, config);
        Approximately(1.12f, (float)elite.HpMultiplier, 0.0001f, "Endless Abyss elite nodes apply their HP factor");
        Approximately(1.05f, (float)elite.AttackMultiplier, 0.0001f, "Endless Abyss elite nodes apply their attack factor");
        Approximately(1.2f, (float)boss.HpMultiplier, 0.0001f, "Endless Abyss boss nodes apply their HP factor");
        Approximately(1.08f, (float)boss.AttackMultiplier, 0.0001f, "Endless Abyss boss nodes apply their attack factor");
        Approximately(1.3f, (float)endlessBoss.HpMultiplier, 0.0001f, "Endless Abyss endless boss nodes apply their HP factor");
        Approximately(1.12f, (float)endlessBoss.AttackMultiplier, 0.0001f, "Endless Abyss endless boss nodes apply their attack factor");
    }

    private static void TestEndlessAbyssEvacuationDepth()
    {
        Equal(0, EndlessAbyssEvacuationDepth.Calculate(1, 0), "Endless Abyss evacuation is available before the first node");
        Equal(5, EndlessAbyssEvacuationDepth.Calculate(1, 5), "Endless Abyss evacuation preserves first-floor node progress");
        Equal(6, EndlessAbyssEvacuationDepth.Calculate(2, 0), "Endless Abyss evacuation includes completed prior floors");
        Equal(39, EndlessAbyssEvacuationDepth.Calculate(7, 3), "Endless Abyss evacuation projects floor and node progress into native depth");
        Equal(0, EndlessAbyssEvacuationDepth.Calculate(0, -3), "Endless Abyss evacuation normalizes invalid floor and level values");
        Equal(int.MaxValue, EndlessAbyssEvacuationDepth.Calculate(int.MaxValue, int.MaxValue), "Endless Abyss evacuation depth saturates instead of overflowing");
    }

    private static void TestEndlessAbyssSettlementBarrierPolicy()
    {
        Equal(EndlessAbyssSettlementBarrierAction.None,
            EndlessAbyssSettlementBarrierPolicy.Evaluate(false, false, false, false, 10, 0, 10),
            "settlement barrier does not advance before the host commits");
        Equal(EndlessAbyssSettlementBarrierAction.Close,
            EndlessAbyssSettlementBarrierPolicy.Evaluate(true, false, true, false, 100, 0, 1),
            "settlement barrier closes immediately after every expected player commits");
        Equal(EndlessAbyssSettlementBarrierAction.ForceCommit,
            EndlessAbyssSettlementBarrierPolicy.Evaluate(true, false, false, false, 10, 0, 10),
            "settlement barrier publishes force-commit at the authoritative deadline");
        Equal(EndlessAbyssSettlementBarrierAction.Close,
            EndlessAbyssSettlementBarrierPolicy.Evaluate(true, false, false, true, 10, 20, 20),
            "settlement barrier closes after the force-commit grace period");
        Equal(EndlessAbyssSettlementBarrierAction.None,
            EndlessAbyssSettlementBarrierPolicy.Evaluate(true, true, true, true, 10, 20, 30),
            "settlement barrier cannot reopen an already closing session");
    }

    private static void TestEndlessAbyssSettlementLocalCommit()
    {
        var commit = new EndlessAbyssSettlementLocalCommit();
        commit.Arm("settlement-1", isClientOnly: true);
        True(commit.IsArmed, "client settlement commit remembers its teardown token");
        True(commit.TryTakeBeforeNetworkTeardown(out var token) && token == "settlement-1",
            "client settlement commit emits exactly once before network teardown");
        False(commit.TryTakeBeforeNetworkTeardown(out _),
            "client settlement commit cannot emit twice");

        commit.Arm("host-settlement", isClientOnly: false);
        False(commit.TryTakeBeforeNetworkTeardown(out _),
            "host settlement close does not impersonate a remote player ACK");
    }

    private static void TestSolarMemoryIsolationIds()
    {
        True(TerriasIds.IsSolarMemoryExclusiveMapId("solar_memory_black_sun_after"), "Short Solar Memory story map ids are exclusive");
        True(TerriasIds.IsSolarMemoryExclusiveMapId("Terrias_terrias_solar_memory_boss_saint_wuna"), "Full Solar Memory boss map ids are exclusive");
        False(TerriasIds.IsSolarMemoryExclusiveMapId("solar_event"), "Retired solar event map ids are no longer shipped exclusive maps");
        False(TerriasIds.IsSolarMemoryExclusiveMapId("map_0"), "Base game map ids are not Solar Memory exclusive");
        True(TerriasIds.IsSolarMemoryExclusiveEventId("Terrias_terrias_Sub_solar_memory_second_sun"), "Full Solar Memory story event ids are exclusive");
        False(TerriasIds.IsSolarMemoryExclusiveEventId("Sub_wuna_event_1"), "Retired Wuna story event ids are no longer shipped exclusive events");
        False(TerriasIds.IsSolarMemoryExclusiveEventId("event_2001"), "Base game event ids are not Solar Memory exclusive");
    }

    private static void TestSolarMemoryFixedNodeCatalog()
    {
        var firstLayer = SolarMemoryFixedNodeCatalog.ForLayer(-1);
        Equal(2, firstLayer.Count, "Solar Memory first layer keeps opening and ending story locks");
        Equal(SolarMemoryFixedNodeCatalog.OpeningSlotIndex, firstLayer[0].SlotIndex, "Solar Memory opening story stays in slot zero");
        Equal(TerriasIds.SolarMemoryMapIds[0], firstLayer[0].MapId, "Solar Memory first opening story resolves from the fixed id catalog");
        Equal(TerriasIds.SolarMemoryFullEventIds[1], firstLayer[1].NodeId, "Solar Memory first ending story resolves the second layer event id");

        var secondLayer = SolarMemoryFixedNodeCatalog.ForLayer(1);
        Equal(3, secondLayer.Count, "Solar Memory second layer keeps two stories and the mirror boss");
        Equal(SolarMemoryFixedNodeCatalog.MidLayerSlotIndex, secondLayer[1].SlotIndex, "Solar Memory second story stays in the fourth slot");
        Equal(TerriasIds.SolarBossOrbitMirrorMapId, secondLayer[2].MapId, "Solar Memory second layer ends at the mirror boss");

        var finalLayer = SolarMemoryFixedNodeCatalog.ForLayer(99);
        Equal(4, finalLayer.Count, "Solar Memory final layer keeps two stories and two fixed bosses");
        Equal(TerriasIds.SolarMemoryMapIds[4], finalLayer[0].MapId, "Solar Memory final layer opening resolves the fifth story map");
        Equal(TerriasIds.SolarMemoryFullEventIds[5], finalLayer[1].NodeId, "Solar Memory final mid slot resolves the sixth story event");
        Equal(TerriasIds.SolarBossSecondSunMapId, finalLayer[2].MapId, "Solar Memory final penultimate slot is the second-sun boss");
        Equal(TerriasIds.SolarBossSaintWunaMapId, finalLayer[3].MapId, "Solar Memory final ending slot is Saint Wuna");
    }

    private static void TestSolarMemoryMapSyncRepair()
    {
        var maps = new[]
        {
            "map_0",
            TerriasIds.SolarBossOrbitMirrorMapId,
            "map_2",
            "map_3",
            "map_4",
            "map_5"
        };
        var mapData = new[] { "node_0", "node_1", "node_2", "node_3", "node_4", "node_5" };
        var repairs = new List<SolarMemoryMapSyncRepair>();

        Equal(5,
            SolarMemoryMapSyncRepairService.Repair(maps, mapData, 2, repairs.Add),
            "Solar Memory sync repair fixes every final-layer lock and misplaced exclusive node");
        Equal(5, repairs.Count, "Solar Memory sync repair reports each changed index once");
        Equal(TerriasIds.SolarMemoryMapIds[4], maps[0], "Solar Memory sync repair restores the final-layer opening story");
        Equal(TerriasIds.SolarMemoryMapIds[4], maps[1], "Solar Memory sync repair replaces misplaced exclusive nodes deterministically");
        Equal("map_2", maps[2], "Solar Memory sync repair preserves ordinary unlocked slots");
        Equal(TerriasIds.SolarMemoryFullEventIds[5], mapData[3], "Solar Memory sync repair restores the final-layer mid story");
        Equal(TerriasIds.SolarBossSecondSunLevelId, mapData[4], "Solar Memory sync repair restores the second-sun level id");
        Equal(TerriasIds.SolarBossSaintWunaLevelId, mapData[5], "Solar Memory sync repair restores the Saint Wuna level id");
        Equal(0,
            SolarMemoryMapSyncRepairService.Repair(maps, mapData, 2),
            "Solar Memory sync repair is idempotent after arrays are normalized");

        var shortMaps = new[] { "map_0", "map_1", "map_2" };
        var shortData = new[] { "node_0" };
        Equal(1,
            SolarMemoryMapSyncRepairService.Repair(shortMaps, shortData, 0),
            "Solar Memory sync repair respects mismatched synchronized array lengths");
    }

    private static void TestSolarMemoryContentIsolation()
    {
        var maps = new[]
        {
            "map_0",
            TerriasIds.SolarMemoryMapIds[0],
            "map_2",
            TerriasIds.SolarBossSaintWunaMapId
        };
        var mapData = new[]
        {
            "node_0",
            TerriasIds.SolarMemoryFullEventIds[0],
            TerriasIds.SolarMemoryFullEventIds[1],
            TerriasIds.SolarBossSaintWunaLevelId
        };
        var resolverCalls = 0;
        var replaced = SolarMemoryContentIsolationService.SanitizeSelectionArrays(
            maps,
            mapData,
            (_, _, index) =>
            {
                resolverCalls++;
                return index switch
                {
                    1 => new SolarMemoryMapSelectionReplacement("safe_event_map", "event_2001"),
                    2 => new SolarMemoryMapSelectionReplacement("safe_fight_map", "level_2001"),
                    _ => new SolarMemoryMapSelectionReplacement(
                        TerriasIds.SolarBossSaintWunaMapId,
                        TerriasIds.SolarBossSaintWunaLevelId)
                };
            });

        Equal(3, resolverCalls, "Solar Memory isolation resolves only exclusive synchronized choices");
        Equal(2, replaced, "Solar Memory isolation applies only safe non-exclusive replacements");
        Equal("map_0", maps[0], "Solar Memory isolation preserves ordinary synchronized choices");
        Equal("safe_event_map", maps[1], "Solar Memory isolation replaces an exclusive map and event pair");
        Equal("safe_fight_map", maps[2], "Solar Memory isolation replaces a normal map carrying an exclusive event id");
        Equal(TerriasIds.SolarBossSaintWunaMapId, maps[3], "Solar Memory isolation rejects an exclusive replacement result");
        False(SolarMemoryContentIsolationService.RequiresReplacement("map_0", "event_2001"), "Solar Memory isolation accepts ordinary map selections");
        True(SolarMemoryContentIsolationService.RequiresReplacement("map_0", TerriasIds.SolarMemoryFullEventIds[0]), "Solar Memory isolation detects exclusive event ids independently");
    }

    private static void TestCombatCardTerminalBoundary()
    {
        AuraBattleLifecycleStateRuntime.ResetForTests();
        const long sessionId = 81;
        AuraBattleLifecycleStateRuntime.Begin(sessionId);
        AuraBattleLifecycleStateRuntime.Activate(sessionId);
        var executor = new ScriptExecutor();

        True(CombatCardApi.TryDrawPlayerCards(executor, 2, "test.active")
             && executor.DrawCountCalls == 1
             && executor.LastDrawCount == 2,
            "active battle draw production routes through the guarded native executor boundary");

        AuraBattleLifecycleStateRuntime.EnterOutcome(sessionId, AuraBattleOutcome.Win);
        False(CombatCardApi.TryDrawPlayerCards(executor, 1, "test.lethal-followup"),
            "draw production is rejected after the authoritative outcome boundary");
        Equal(1, executor.DrawCountCalls,
            "post-lethal draw rejection never reaches the native executor");
        var deckCount = FightCardManager.Instance.cardList.Count;
        var lateGrant = CardApi.GrantCardToHand(executor, CardGrantRequest.ToHand("spark"));
        False(lateGrant.Success
              || lateGrant.FailureStep != "terminal-barrier"
              || FightCardManager.Instance.cardList.Count != deckCount,
            "post-lethal generated-card grants are rejected before mutating the native deck or hand");

        var fightUi = new FightUI { NeedUpdateCardMsg = true, started = true };
        fightUi.createCardQueue.Enqueue(new DataConfig(new Dictionary<string, string> { ["Id"] = "late-one" }));
        fightUi.createCardQueue.Enqueue(new DataConfig(new Dictionary<string, string> { ["Id"] = "late-two" }));
        CardItem.canUse = true;
        Equal(2, FightUiCardTerminalApi.CloseDrawProduction(fightUi, "test.settling"),
            "terminal cleanup reports every queued late draw");
        Equal(0, fightUi.createCardQueue.Count,
            "terminal cleanup closes the native asynchronous draw producer queue");
        False(fightUi.NeedUpdateCardMsg || CardItem.canUse,
            "terminal cleanup disables pending hand refresh and card interaction");

        AuraBattleLifecycleStateRuntime.End(sessionId);
    }

    private static void TestPerformanceSettings()
    {
        TerriasPerformanceSettings.RegisterFeatureDefaults();
        AuraFeatureSwitchRuntime.SetLocalOverride(
            "TestTool",
            TerriasPerformanceSettings.SharedDiagnosticsOwnerId,
            TerriasPerformanceSettings.SharedDiagnosticsFeatureId,
            true);
        TerriasPerformanceSettings.Refresh();
        True(TerriasPerformanceSettings.CountersEnabled,
            "A tool-local shared diagnostics override enables Terrias performance counters");

        AuraFeatureSwitchRuntime.SetLocalOverride(
            "TestTool",
            TerriasPerformanceSettings.SharedDiagnosticsOwnerId,
            TerriasPerformanceSettings.SharedDiagnosticsFeatureId,
            false);
        ScriptExecutor.PlayerInfo.SetGameVar("TerriasPerfCounters", "0");
        TerriasPerformanceSettings.Refresh();
        False(TerriasPerformanceSettings.CountersEnabled,
            "The default zero no longer races an asynchronous GameVar write during counter initialization");

        AuraFeatureSwitchRuntime.SetLocalOverride(
            "TestTool",
            TerriasPerformanceSettings.SharedDiagnosticsOwnerId,
            TerriasPerformanceSettings.SharedDiagnosticsFeatureId,
            null);
        TerriasPerformanceSettings.Refresh();
    }

    private static void TestMapNodeTextureFitService()
    {
        var secondSun = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 20, 20, 90, 91),
            MapNodeCardArtFitMode.ContainTrimmed);
        True(secondSun.ShouldApplyTransform, "Trimmed map-node art owns the icon transform");
        Approximately(182.86f, secondSun.ScaleX, 0.01f, "Wide second-sun art scales the full canvas from the visible-width fit");
        Approximately(272f, secondSun.ScaleY, 0.01f, "Wide second-sun art preserves canvas aspect while fitting visible width");
        Approximately(-0.29f, secondSun.OffsetY, 0.02f, "Asymmetric transparent trim recenters the visible subject");

        var saint = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 63, 64, 70, 130),
            MapNodeCardArtFitMode.ContainTrimmed);
        Approximately(265.28f, saint.ScaleX, 0.01f, "Tall saint art scales the full canvas from the visible-width fit");
        Approximately(394.61f, saint.ScaleY, 0.01f, "Tall saint art keeps the original canvas ratio");
        Approximately(-24.87f, saint.OffsetY, 0.01f, "Large bottom transparency is compensated by a vertical offset");

        var canvas = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 63, 64, 70, 130),
            MapNodeCardArtFitMode.ContainCanvas);
        Approximately(160f, canvas.ScaleX, 0.01f, "Canvas mode fits the full 320px canvas width");
        Approximately(238f, canvas.ScaleY, 0.01f, "Canvas mode fits the full 476px canvas height");
        Approximately(0f, canvas.OffsetY, 0.01f, "Canvas mode does not compensate transparent padding");

        var legacy = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 0, 0, 0, 0),
            MapNodeCardArtFitMode.StretchLegacy);
        False(legacy.ShouldApplyTransform, "Legacy mode leaves native MapItem transform untouched");
    }

    private static void TestDimensionShopRandom()
    {
        Equal(-1, DimensionShopRandom.Index("run", "card", 0, 0), "Dimension shop random handles empty pools");
        Equal(
            DimensionShopRandom.Index("run", "card", 0, 4),
            DimensionShopRandom.Index("run", "card", 0, 4),
            "Dimension shop initial shelves are deterministic for the same run seed");
        Equal(
            DimensionShopRandom.Index("run", "card", 0, 4),
            DimensionShopRandom.Index("run", "card", -5, 4),
            "Dimension shop random clamps invalid counters to the initial draw");

        var cardSequence = Enumerable.Range(0, 64)
            .Select(counter => DimensionShopRandom.Index("run|player", "refresh.card", counter, 4))
            .ToArray();
        var relicSequence = Enumerable.Range(0, 64)
            .Select(counter => DimensionShopRandom.Index("run|player", "refresh.relic", counter, 4))
            .ToArray();
        True(cardSequence.All(index => index >= 0 && index < 4), "Dimension shop random indices stay inside the configured pool");
        False(cardSequence.SequenceEqual(relicSequence), "Dimension shop card and relic refreshes use independent deterministic streams");
        True(cardSequence.Distinct().Count() < cardSequence.Length, "Dimension shop draws permit repeated products instead of tracking a no-repeat bag");

        var shelf = DimensionShopRandom.Sample(new[] { "a", "b", "c", "d" }, "run|player", "cards", 2, 3);
        Equal(3, shelf.Count, "Dimension shop fills three offer slots when the pool is large enough");
        Equal(3, shelf.Distinct().Count(), "Dimension shop samples one shelf without duplicate products");
        True(
            shelf.SequenceEqual(DimensionShopRandom.Sample(new[] { "a", "b", "c", "d" }, "run|player", "cards", 2, 3)),
            "Dimension shop multi-offer shelves are deterministic");
        Equal(
            2,
            DimensionShopRandom.Sample(new[] { "a", "b" }, "run|player", "cards", 2, 3).Count,
            "Dimension shop does not duplicate products to fill a short pool");
    }

    private static void TestModeChoiceDragRange()
    {
        var fiveSlots = ModeChoiceDragRangeService.Calculate(
            -987.5f,
            987.5f,
            355f,
            5,
            50f,
            1920f,
            4,
            96f);
        Approximately(1570f, fiveSlots.ViewportWidth, 0.01f, "Four visible mode slots define the viewport width");
        Approximately(-202.5f, fiveSlots.MinOffset, 0.01f, "Left drag limit fully reveals the fifth mode");
        Approximately(202.5f, fiveSlots.MaxOffset, 0.01f, "Right drag limit fully reveals the first four modes");
        Approximately(202.5f, fiveSlots.DefaultOffset, 0.01f, "Initial position shows the native four modes");
        True(fiveSlots.DragEnabled, "Five mode slots enable horizontal dragging");

        var fourSlots = ModeChoiceDragRangeService.Calculate(
            -785f,
            785f,
            355f,
            4,
            50f,
            1920f,
            4,
            96f);
        Approximately(0f, fourSlots.MinOffset, 0.01f, "Four fitting slots need no negative offset");
        Approximately(0f, fourSlots.MaxOffset, 0.01f, "Four fitting slots need no positive offset");
        False(fourSlots.DragEnabled, "Four fitting slots keep dragging disabled");
    }

    private static void TestSpiritProfileIdentityResolver()
    {
        var profiles = new List<TestSpiritProfile>
        {
            new("10026", "*"),
            new("boss_orbit_mirror_array", "*"),
            new("enemy_exact", "v1"),
            new("enemy_10026", "enemy_10026"),
            new("*", "*")
        };

        SpiritProfileResolution<TestSpiritProfile> Resolve(string enemyId, string variantId) =>
            SpiritProfileIdentityResolver.Resolve(profiles, profile => profile.EnemyId, profile => profile.VariantId, enemyId, variantId);

        var runtimeBaseGame = Resolve("enemy_10026", "enemy_10026");
        Equal("enemy_10026", runtimeBaseGame.MatchedEnemyId, "Raw exact profiles take precedence over canonical aliases");
        Equal("exact", runtimeBaseGame.MatchKind, "Raw exact profile resolution reports its match kind");

        profiles.RemoveAt(3);
        var oldCapturedCard = Resolve("enemy_10026", "enemy_10026");
        Equal("10026", oldCapturedCard.MatchedEnemyId, "Old captured cards resolve the base-game runtime prefix to the stable registry id");
        Equal("*", oldCapturedCard.MatchedVariantId, "Canonical base-game ids retain enemy wildcard fallback");
        Equal("alias-enemy-wildcard", oldCapturedCard.MatchKind, "Base-game prefix normalization is visible in diagnostics");
        True(oldCapturedCard.UsedAlias, "Base-game prefix normalization is marked as an alias match");
        True(oldCapturedCard.UsedVariantWildcard, "Enemy wildcard use is marked in the resolution result");
        False(oldCapturedCard.UsedGlobalFallback, "Known base-game enemies do not reach the global profile");

        var canonical = Resolve("10026", "10026");
        Equal("10026", canonical.MatchedEnemyId, "Canonical registry ids continue to resolve directly");
        Equal("enemy-wildcard", canonical.MatchKind, "Canonical ids use the explicit enemy wildcard profile");

        var terriasRuntime = Resolve("Terrias_terrias_boss_orbit_mirror_array", "Terrias_terrias_boss_orbit_mirror_array");
        Equal("boss_orbit_mirror_array", terriasRuntime.MatchedEnemyId, "Terrias runtime ids resolve to short stable profile ids");
        Equal("alias-enemy-wildcard", terriasRuntime.MatchKind, "Terrias prefix normalization is visible in diagnostics");

        var exactVariant = Resolve("enemy_exact", "v1");
        Equal("exact", exactVariant.MatchKind, "Explicit variant profiles resolve before enemy wildcards");
        Equal("v1", exactVariant.MatchedVariantId, "Explicit variant identity is retained");

        var unknownModEnemy = Resolve("OtherMod_enemy_dragon", "OtherMod_enemy_dragon");
        Equal("*", unknownModEnemy.MatchedEnemyId, "Unknown mod enemies use the global compatibility profile");
        Equal("global-fallback", unknownModEnemy.MatchKind, "Unknown mod fallback is explicit in diagnostics");
        True(unknownModEnemy.UsedGlobalFallback, "Unknown mod fallback is marked in the resolution result");

        SpiritProfileIdentityResolver.ParseProfileKey("spirit:enemy_10026#enemy_10026", out var parsedEnemy, out var parsedVariant);
        Equal("enemy_10026", parsedEnemy, "Persisted spirit profile keys retain their raw enemy id");
        Equal("enemy_10026", parsedVariant, "Persisted spirit profile keys retain their raw variant id");
    }

    private static void TestCardCostHelpers()
    {
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "test_card",
                ["Expend"] = "6"
            },
            new Dictionary<string, string>
            {
                ["ExCost"] = "2",
                ["OnceExCost"] = "1",
                ["TotalExCost"] = "4"
            });

        Equal("test_card", CardConfigApi.Id(config), "CardConfigApi.Id reads data Id");
        Equal(11, CardConfigApi.CurrentCost(config), "CurrentCost caps scaled base cost and includes extra costs");
        Equal(6, CardConfigApi.BaseCost(config), "BaseCost reads only Expend");

        FightPlayer.Instance.Status.dynamicVariables["CardCost"] = 0.5f;
        Equal(10, CardConfigApi.CurrentCost(config), "CurrentCost honors the player CardCost multiplier");
        FightPlayer.Instance.Status.dynamicVariables.Clear();

        var negative = NewConfig(
            new Dictionary<string, string> { ["Expend"] = "-3" },
            new Dictionary<string, string> { ["ExCost"] = "-9" });
        Equal(0, CardConfigApi.CurrentCost(negative), "CurrentCost is clamped to zero");
        Equal(0, CardConfigApi.BaseCost(negative), "BaseCost is clamped to zero");
    }

    private static void TestStarBlessingCostOverrideStore()
    {
        var store = new StarBlessingCostOverrideStore();
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "star_blessing_target",
                ["Expend"] = "3"
            },
            new Dictionary<string, string>
            {
                ["ExCost"] = "1",
                ["OnceExCost"] = "-1",
                ["TotalExCost"] = "0"
            });

        Equal(3, CardConfigApi.CurrentCost(config), "Star blessing test card starts at its normal modified cost");
        True(store.BeginPreview(config, 2), "Star blessing begins one preview transaction");
        Equal(2, CardConfigApi.CurrentCost(config), "Star blessing preview displays halved rounded-up cost");
        False(store.BeginPreview(config, 2), "Star blessing preview is idempotent for the same card instance");
        store.Cancel(config);
        Equal("-1", config.Vars["OnceExCost"], "Cancelling star blessing restores the original one-use modifier");
        Equal(3, CardConfigApi.CurrentCost(config), "Cancelling star blessing restores the normal displayed cost");

        True(store.BeginPreview(config, 2), "Star blessing preview can begin again after cancellation");
        store.MarkBlessingConsumed(config);
        store.MarkActionObserved(config);
        True(store.ActionObserved(config), "Confirmed card action marks the preview transaction committed");
        var committed = store.Commit(config);
        True(committed.BlessingConsumed, "Committed transaction reports that the blessing was consumed");
        Equal("0", config.Vars["OnceExCost"], "Successful play consumes all one-use cost modifiers");
        Equal(4, CardConfigApi.CurrentCost(config), "The card returns to its normal non-once cost after successful play");

        True(store.BeginPreview(config, 2), "A later blessing can preview the same card again");
        store.CancelAll();
        Equal("0", config.Vars["OnceExCost"], "Fight cleanup restores every active preview");
        False(store.Contains(config), "Fight cleanup removes active preview state");
    }

    private static void TestResonanceCostTransactionStore()
    {
        var store = new ResonanceCostTransactionStore();
        var owner = new FakeStatus("resonance-owner");
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "resonance_target",
                ["Expend"] = "4"
            },
            new Dictionary<string, string>
            {
                ["OnceExCost"] = "1"
            });

        var begun = store.Begin(owner, config, 3);
        True(begun.Found, "Resonance begins a cost-payment transaction");
        Equal(3, begun.ResonancePaid, "Resonance records the exact number of substituted Magic points");
        True(ReferenceEquals(owner, begun.Owner), "Resonance records the player who funded the payment");
        Equal("-2", config.Vars["OnceExCost"], "Resonance applies its own one-use cost delta");
        False(store.Begin(owner, config, 1).Found, "Resonance cannot charge the same card transaction twice");

        store.MarkPaymentApplied(config);
        DictionaryUtil.Set(config.Vars, "OnceExCost", "0");
        var cancelled = store.Cancel(config);
        True(cancelled.PaymentApplied, "Cancelled Resonance transaction reports that its Buff payment was applied");
        Equal("3", config.Vars["OnceExCost"], "Cancelling Resonance removes only its own delta and preserves later modifiers");
        False(store.Contains(config), "Cancelling Resonance closes the transaction exactly once");

        DictionaryUtil.Set(config.Vars, "OnceExCost", "1");
        True(store.Begin(owner, config, 2).Found, "Resonance can begin a later transaction for the same card");
        store.MarkPaymentApplied(config);
        store.MarkActionObserved(config);
        True(store.ActionObserved(config), "Card Action marks the Resonance transaction as confirmed");
        var committed = store.Commit(config);
        True(committed.ActionObserved, "Committed Resonance transaction retains Action evidence");
        Equal("0", config.Vars["OnceExCost"], "Successful Resonance payment consumes all one-use cost modifiers");

        DictionaryUtil.Set(config.Vars, "OnceExCost", "2");
        store.Begin(owner, config, 1);
        var cleared = store.CancelAll();
        Equal(1, cleared.Count, "Fight cleanup returns every pending Resonance transaction");
        Equal("2", config.Vars["OnceExCost"], "Fight cleanup removes the pending Resonance delta");
        False(store.Contains(config), "Fight cleanup clears pending Resonance state");
    }

    private static void TestCardGrantRequest()
    {
        AuraBattleLifecycleStateRuntime.ResetForTests();
        AuraBattleLifecycleStateRuntime.Begin(71);
        AuraBattleLifecycleStateRuntime.Activate(71);
        var request = CardGrantRequest.ToHand("spark")
            .WithRuntimeTags("Burnout", "Burnout", "Nihility");
        Equal("Burnout,Nihility", request.RuntimeTags, "CardGrantRequest deduplicates runtime tags");

        var executor = new ScriptExecutor();
        FightCardManager.Instance.cardList.Clear();
        var result = CardApi.GrantCardToHand(
            executor,
            CardGrantRequest.ToHand("spark")
                .Configure(CardMutationService.SetTemporaryCostMutation(1))
                .Configure(CardMutationService.AddSpecialTagsMutation("A", "A", "B")));
        True(result.Success, "CardApi grant succeeds through the unified hand-delivery pipeline");
        Equal("spark", result.CardId, "CardApi grant returns the resolved card id");
        Equal("-1", result.Config!.Vars["TotalExCost"], "CardApi grant applies request mutations before delivery");
        Equal("2", result.Config!.data["Expend"], "CardApi grant mutations do not write base data");
        Equal("A,B", result.Config!.Vars["SpecialTag"], "CardApi grant applies deduplicated SpecialTag mutations");

        FightCardManager.Instance.cardList.Clear();
        var runtimeVarsResult = CardApi.GrantCardToHand(
            executor,
            CardGrantRequest.ToHand("runtime_state_card")
                .Configure("runtime-vars", config =>
                {
                    config.Vars["Name"] = "Runtime role card";
                    config.Vars["RuntimeFlag"] = "1";
                }));
        True(runtimeVarsResult.Success, "CardApi grant keeps runtime Vars writable while base data remains read-only");
        True(runtimeVarsResult.Config!.data is System.Collections.ObjectModel.ReadOnlyDictionary<string, string>, "CardApi grant preserves the game's read-only base data contract");
        Equal("Runtime role card", runtimeVarsResult.Config!.Vars["Name"], "CardApi grant accepts runtime display state through Vars");
        Equal("1", runtimeVarsResult.Config!.Vars["RuntimeFlag"], "CardApi grant accepts runtime flags through Vars");

        FightCardManager.Instance.cardList.Clear();
        var presentationResult = CardApi.GrantCardToHand(
            executor,
            CardGrantRequest.ToHand("runtime_presentation_card")
                .WithRuntimePresentation(new Dictionary<string, string>
                {
                    ["Name"] = "Spirit: Cat",
                    ["Description"] = "Summon one Cat",
                    ["RuntimeFlag"] = "must-stay-in-vars"
                })
                .Configure("runtime-state", config => config.Vars["RuntimeFlag"] = "1"));
        True(presentationResult.Success, "CardApi grant composes runtime presentation before native materialization");
        True(presentationResult.Config!.data is System.Collections.ObjectModel.ReadOnlyDictionary<string, string>, "Runtime presentation remains immutable after DataConfig construction");
        Equal("Spirit: Cat", presentationResult.Config!.data["Name"], "Native card readers receive the dynamic runtime name");
        Equal("Summon one Cat", presentationResult.Config!.data["Description"], "Native card readers receive the dynamic runtime description");
        Equal("Spirit: Cat", presentationResult.Config!.Vars["Name"], "Runtime presentation also remains available through Vars");
        False(presentationResult.Config!.data.ContainsKey("RuntimeFlag"), "Non-presentation runtime state is not copied into the immutable data snapshot");
        Equal("1", presentationResult.Config!.Vars["RuntimeFlag"], "Non-presentation runtime state remains writable in Vars");
        True(CardApi.MarkForAdventureRemoval(presentationResult.Config), "CardApi marks a valid card for adventure removal");
        Equal("True", presentationResult.Config!.Vars["NeedRemove"], "Adventure removal uses the host NeedRemove runtime contract");

        FightCardManager.Instance.cardList.Clear();
        var failing = new ScriptExecutor { ThrowOnDelivery = true };
        var failed = CardApi.GrantCardToHand(failing, CardGrantRequest.ToHand("spark"));
        False(failed.Success, "CardApi grant returns structured failure on delivery errors");
        Equal("deliver", failed.FailureStep, "CardApi grant identifies the failing step");
        Equal(0, FightCardManager.Instance.cardList.Count, "CardApi grant cleans up created combat cards when delivery fails");

        FightCardManager.Instance.usedCardList.Clear();
        True(CardApi.AddCardToDiscardPile(executor, TerriasIds.ForgottenCardId), "CardApi can create a combat card directly in the discard pile");
        Equal(TerriasIds.ForgottenCardId, CardConfigApi.Id(FightCardManager.Instance.usedCardList.Single()), "Discard-pile grants preserve the resolved card id");
        AuraBattleLifecycleStateRuntime.End(71);
    }

    private static void TestCardMutationService()
    {
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "guided",
                ["Expend"] = "3",
                ["Tag"] = "Native"
            },
            new Dictionary<string, string>());

        CardMutationService.SetTemporaryCost(config, 1);
        Equal("-2", config.Vars["TotalExCost"], "Temporary cost is expressed through TotalExCost");
        Equal("3", config.data["Expend"], "Temporary cost leaves base Expend read-only");

        True(CardMutationService.AddSpecialTags(config, "Guidance", "Guidance", "Derived"), "Special tags are added once");
        Equal("Guidance,Derived", config.Vars["SpecialTag"], "Special tags are deduplicated");
        False(CardMutationService.AddSpecialTags(config, "Guidance"), "Existing SpecialTags are not rewritten");
        True(CardMutationService.AddNativeTags(config, "Burnout", "Burnout"), "Native tags are added once");
        Equal("Native,Burnout", config.Vars["Tag"], "Native tags are deduplicated in Vars.Tag");
        Equal("Native", config.data["Tag"], "Native tag mutations do not write base data.Tag");
        False(CardMutationService.AddNativeTags(config, "Burnout"), "Existing native tags are not rewritten");

        CardMutationService.MarkTemporaryWhiteRadiance(config);
        Equal("1", config.Vars[TerriasIds.TempWhiteRadiance], "Temporary white radiance marker is set");
        Equal("0", config.Vars[TerriasIds.TempWhiteRadianceResolved], "Temporary white radiance starts unresolved");
        True(CardMutationService.HasSpecialTag(config, TerriasIds.WhiteRadianceTag), "Temporary white radiance adds the white-radiance SpecialTag");
    }

    private static void TestRuntimeCardAttachmentService()
    {
        ExecutorApi.ResetCombatVars();
        FightCardManager.Instance.cardList.Clear();
        Witch.UI.Window.FightUI.cardItemList.Clear();
        Witch.UI.Window.FightUI.WaitCard.Clear();

        var config = new DataConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "temporary_hand_card",
                ["Tag"] = ""
            },
            new Dictionary<string, string>());
        var card = new CardItem
        {
            dataConfig = config,
            Vars = config.Vars,
            data = new Dictionary<string, string>
            {
                ["Id"] = "temporary_hand_card",
                ["Tag"] = ""
            }
        };
        var executor = new ScriptExecutor();
        executor.HandCard.Add(card);
        Witch.UI.Window.FightUI.cardItemList.Add(card);

        var result = RuntimeCardAttachmentService.AttachToCurrentHand(
            executor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment());

        Equal(1, result.TouchedCardItems, "Runtime attachment touches the current hand card once");
        Equal(1, result.TouchedConfigs, "Runtime attachment touches the hand card config once");
        True(result.Changed > 0, "Runtime attachment records marker/tag changes");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "Tag"), "Burnout"), "Runtime attachment writes native tags to card item Vars.Tag");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout"), "Runtime attachment writes native tags to config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), "Burnout"), "Runtime attachment does not write base config data.Tag");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment writes SpecialTag to card item Vars");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment writes SpecialTag to config Vars");
        Equal("1", config.Vars[TerriasIds.TempWhiteRadiance], "Runtime attachment marks temporary white radiance on config");
        Equal(card.Vars[TerriasIds.TempWhiteRadianceLockId], config.Vars[TerriasIds.TempWhiteRadianceLockId], "Card item and config share the temporary white radiance lock");
        True(CardConfigApi.HasTemporaryWhiteRadiance(config), "Runtime attachment is visible to the white-radiance trigger runtime");
        False(CardConfigApi.HasNativeWhiteRadiance(config), "Runtime hand attachment does not turn white radiance into a native run tag");

        var cleared = RuntimeCardAttachmentService.ClearTemporaryAttachments("test");
        True(cleared > 0, "Runtime attachment cleanup removes temporary card vars at the next fight boundary");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup removes temporary Burnout from config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from config Vars.SpecialTag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes the temporary marker from card Vars");
        False(config.Vars.ContainsKey(TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes temporary white radiance state");
        False(config.Vars.ContainsKey(TerriasIds.TempWhiteRadianceLockId), "Runtime attachment cleanup removes the temporary white radiance lock");
        False(card.Tags.Contains("Burnout"), "Runtime attachment cleanup removes temporary Burnout from visible card tags");
        False(card.Tags.Contains(WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from visible card tags");

        var goldDreamResult = RuntimeCardAttachmentService.AttachToCurrentHand(
            executor,
            RuntimeCardAttachmentService.GoldDreamHandAttachment());
        True(goldDreamResult.Changed > 0, "Golden Dream attachment changes the current hand card");
        True(CardConfigApi.HasGoldDream(config), "Golden Dream attachment is visible to the action runtime");
        True(card.Tags.Contains(TerriasIds.GoldDreamTag), "Golden Dream attachment is visible on the card item");
        RuntimeCardAttachmentService.ClearTemporaryAttachments("test.gold-dream");
        False(CardConfigApi.HasGoldDream(config), "Golden Dream attachment is cleared at the fight boundary");
        False(card.Tags.Contains(TerriasIds.GoldDreamTag), "Generic attachment cleanup removes Golden Dream from visible tags");

        DictionaryUtil.Set(config.Vars, TerriasIds.GoldDreamSkipOnce, "1");
        True(CardConfigApi.TryClaimGoldDreamSkipOnce(config), "Wager claims its deferred Golden Dream trigger once");
        False(CardConfigApi.TryClaimGoldDreamSkipOnce(config), "Wager cannot claim the same Golden Dream skip twice");

        FightCardManager.Instance.cardList.Clear();
        Witch.UI.Window.FightUI.cardItemList.Clear();
        Witch.UI.Window.FightUI.WaitCard.Clear();
        ExecutorApi.ResetCombatVars();

        var waitConfig = new DataConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "temporary_wait_card",
                ["Tag"] = ""
            },
            new Dictionary<string, string>());
        var waitCard = new CardItem
        {
            dataConfig = waitConfig,
            Vars = waitConfig.Vars,
            data = new Dictionary<string, string>
            {
                ["Id"] = "temporary_wait_card",
                ["Tag"] = ""
            }
        };
        var waitExecutor = new ScriptExecutor();
        waitExecutor.WaitCard.Add(waitCard);
        Witch.UI.Window.FightUI.WaitCard.Add(waitCard);

        var waitResult = RuntimeCardAttachmentService.AttachToCurrentHand(
            waitExecutor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment());

        Equal(1, waitResult.TouchedCardItems, "Runtime attachment touches wait-list hand cards");
        Equal(1, waitResult.TouchedConfigs, "Runtime attachment touches wait-list configs once");
        True(waitResult.ExecutorWaitCards > 0, "Runtime attachment scans executor WaitCard");
        True(waitResult.UiWaitCards > 0, "Runtime attachment scans FightUI WaitCard");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitCard.Vars, "Tag"), "Burnout"), "Runtime attachment writes native tags to wait-list card item Vars.Tag");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment writes SpecialTag to wait-list config Vars");

        RuntimeCardAttachmentService.ClearTemporaryAttachments("test.wait");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitConfig.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup removes temporary Burnout from wait-list config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from wait-list config Vars.SpecialTag");

        FightCardManager.Instance.cardList.Clear();
        Witch.UI.Window.FightUI.cardItemList.Clear();
        Witch.UI.Window.FightUI.WaitCard.Clear();

        var nativeBurnoutConfig = new DataConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "native_burnout_card",
                ["Tag"] = "Burnout"
            },
            new Dictionary<string, string>
            {
                ["Tag"] = "Burnout",
                ["SpecialTag"] = WhiteRadiance,
                [TerriasIds.RuntimeMarkersKey] = TerriasIds.TempWhiteRadiance,
                [TerriasIds.TempWhiteRadiance] = "1"
            });
        FightCardManager.Instance.cardList.Add(nativeBurnoutConfig);

        RuntimeCardAttachmentService.ClearTemporaryAttachments("test.legacy");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup preserves native Burnout when base data owns it");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes legacy temporary white radiance without a snapshot");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes legacy temporary markers without a snapshot");
        False(nativeBurnoutConfig.Vars.ContainsKey(TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes legacy temporary state without a snapshot");
    }

    private static void TestSolarTriggerCostOverride()
    {
        var config = NewConfig(
            new Dictionary<string, string> { ["Id"] = "flamewheel_recurrence" },
            new Dictionary<string, string> { [TerriasIds.SolarTriggerCost] = "5" });

        Equal(5, CardConfigApi.ResolveSolarTriggerCost(config, 1), "Solar trigger override wins over fallback");
        CardConfigApi.ClearSolarTriggerCost(config);
        Equal("", config.Vars[TerriasIds.SolarTriggerCost], "ClearSolarTriggerCost blanks the override var");
        Equal(1, CardConfigApi.ResolveSolarTriggerCost(config, 1), "ResolveSolarTriggerCost falls back after clear");
    }

    private static void TestWhiteRadianceTags()
    {
        var native = NewConfig(
            new Dictionary<string, string> { ["Tag"] = "Burnout," + WhiteRadiance },
            new Dictionary<string, string>());
        True(CardConfigApi.HasNativeWhiteRadiance(native), "Native white radiance is read from Vars.Tag");

        var temporary = NewConfig(
            new Dictionary<string, string> { ["Tag"] = "" },
            new Dictionary<string, string>
            {
                ["SpecialTag"] = WhiteRadiance,
                [TerriasIds.TempWhiteRadiance] = "1"
            });
        True(CardConfigApi.HasTemporaryWhiteRadiance(temporary), "Temporary white radiance requires marker and SpecialTag");
        True(CardConfigApi.HasSpecialWhiteRadiance(temporary), "Special white radiance is read from Vars.SpecialTag");
        False(CardConfigApi.HasNativeWhiteRadiance(temporary), "Temporary white radiance is not native");
    }

    private static void TestTemporaryWhiteRadianceClaim()
    {
        ExecutorApi.ResetCombatVars();
        var config = NewConfig();

        True(CardConfigApi.TryClaimTemporaryWhiteRadiance(config), "First temporary white radiance claim succeeds");
        Equal("1", config.Vars[TerriasIds.TempWhiteRadianceResolved], "Successful claim marks card resolved");
        False(CardConfigApi.TryClaimTemporaryWhiteRadiance(config), "Second claim on the same card is blocked");

        var stale = NewConfig(vars: new Dictionary<string, string>
        {
            [TerriasIds.TempWhiteRadianceLockId] = config.Vars[TerriasIds.TempWhiteRadianceLockId],
            [TerriasIds.TempWhiteRadianceResolved] = "0"
        });
        True(CardConfigApi.TryClaimTemporaryWhiteRadiance(stale), "A stale unresolved card lock is renewed");
        NotEqual(config.Vars[TerriasIds.TempWhiteRadianceLockId], stale.Vars[TerriasIds.TempWhiteRadianceLockId], "Renewed stale lock receives a new id");
    }

    private static void TestLoneerStateOwnership()
    {
        LoneerCombatStateStore.ClearAll();
        var owner = new FakeStatus("loneer-a");
        var other = new FakeStatus("loneer-b");
        var selectedFromCareer = LoneerCombatStateStore.ResetForFight(owner)!;
        selectedFromCareer.GuidanceCardId = "selected-guide";
        selectedFromCareer.ClockValue = 7;
        selectedFromCareer.SelectionVersion = 2;

        var readFromSkill = LoneerCombatStateStore.GetOrCreate(owner)!;
        True(ReferenceEquals(selectedFromCareer, readFromSkill), "Loneer state is shared across executors for the same owner");
        Equal("selected-guide", readFromSkill.GuidanceCardId, "Guidance survives executor changes");
        Equal(7, readFromSkill.ClockValue, "Miracle Clock state survives executor changes");
        Equal(2, readFromSkill.SelectionVersion, "Guidance selection version survives executor changes");

        var isolated = LoneerCombatStateStore.GetOrCreate(other)!;
        Equal("", isolated.GuidanceCardId, "Different owners receive isolated guidance state");
        Equal(0, isolated.ClockValue, "Different owners receive isolated Miracle Clock state");
        LoneerCombatStateStore.Remove(owner);
        Equal("", LoneerCombatStateStore.GetOrCreate(owner)!.GuidanceCardId, "Removed combat state does not leak into the next fight");
    }

    private static void TestStarScoreWindow()
    {
        StarScoreCombatStateStore.ClearAll();
        var owner = new FakeStatus("score-owner");
        var score = StarScoreCombatStateStore.GetOrCreate(owner)!;
        score.Record("S", 3);
        score.Record("U", 3);
        score.Record("T", 3);
        score.RecordCompletedCadence("SUT");
        var preview = score.Snapshot(owner.InstanceId, isCadencePreview: true, completedCadencePattern: "SUT");
        Equal(3, preview.Notes.Count, "Star score HUD preview exposes the full completed cadence");
        Equal(StarScoreNote.Turn, preview.Notes[2], "Star score HUD preview keeps typed note identity");
        True(preview.IsCadencePreview, "Star score HUD preview is flagged before cadence collapse");
        Equal("SUT", preview.CompletedCadencePattern, "Star score HUD preview records the completed cadence pattern");

        score.RetainLastNoteAsCadenceStart();

        Equal(1, score.Notes.Count, "Star score keeps the last note after a completed cadence");
        Equal(StarScoreNote.Turn, score.Notes[0], "Star score reuses the last overture as the next cadence start");
        Equal("T", StarScoreNoteCodes.PatternFromNotes(score.Notes), "Star score converts retained notes back to cadence pattern codes");
        score.Record("C", 3);
        score.Record("S", 3);
        Equal(3, score.Notes.Count, "Star score builds the next cadence from the retained note");
        Equal(StarScoreNote.Turn, score.Notes[0], "Star score retained note remains the first note of the next cadence");
        True(ReferenceEquals(score, StarScoreCombatStateStore.GetOrCreate(owner)), "Star score is shared across card executors for the same owner");

        var openingCadence = StarScoreCadenceCatalog.Resolve(new[] { StarScoreNote.Opening, StarScoreNote.Opening, StarScoreNote.Opening });
        Equal("\u542f\u542f\u542f\uff1a\u6025\u677f\u3002\u53cb\u65b9\u5168\u4f53\u4f59\u97f3+1\uff1b\u53cb\u65b9\u5168\u4f53\u62bd2\u5f20\u724c", openingCadence.DisplayText, "Opening cadence tooltip text matches the design copy");
        var defaultCadence = StarScoreCadenceCatalog.Resolve(new[] { StarScoreNote.Opening, StarScoreNote.Sustain, StarScoreNote.Opening });
        Equal("\u542f\u627f\u542f\uff1a\u4e09\u58f0\u548c\u5f26\u3002\u53cb\u65b9\u5168\u4f53\u62bd1\u5f20\u724c", defaultCadence.DisplayText, "Default cadence tooltip text matches the design copy");
        var candidates = StarScoreCadenceCatalog.CandidatesForPrefix(new[] { StarScoreNote.Opening, StarScoreNote.Sustain });
        Equal(4, candidates.Count, "Two-note star score prefixes enumerate four possible third notes");
        True(candidates.Any(row => row.DisplayText == "\u542f\u627f\u8f6c\uff1a\u8c03\u5f8b\u3002\u81ea\u8eab\u4f59\u97f3+1\uff1b\u53cb\u65b9\u5168\u4f53\u4f59\u97f3+1"), "Candidate list includes the named tuning cadence");
    }

    private static void TestStarScoreArrivalCueService()
    {
        StarScoreArrivalCueService.Clear();
        var card = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.StellarOvertureStartCardId
        });
        StarScoreArrivalCueService.Record(card, StarScoreNote.Opening, 0, false, "score-owner");
        StarScoreArrivalCueService.Record(card, StarScoreNote.Sustain, 1, false, "score-owner");
        StarScoreArrivalCueService.Record(card, StarScoreNote.Turn, 2, true, "score-owner");
        StarScoreArrivalCueService.Record(card, StarScoreNote.Close, 1, false, "score-owner");

        var cues = StarScoreArrivalCueService.Consume(card);
        Equal(4, cues.Count, "Card-use FX cue ledger retains every actual extra execution note");
        Equal(0, cues[0].SlotIndex, "First note cue targets slot one");
        Equal(2, cues[2].SlotIndex, "Cadence-completing cue targets slot three");
        True(cues[2].CompletesCadence, "Third note cue marks the cadence preview extension point");
        True(cues[0].Sequence < cues[3].Sequence, "Card-use FX cues preserve execution order");
        Equal(0, StarScoreArrivalCueService.Consume(card).Count, "Card-use FX cue ledger is consumed exactly once");
        Equal(3, StarScoreArrivalCueService.MaxVisibleRibbonCount, "Card-use FX limits one use to three visible ribbons");
    }

    private static FakeDataConfig NewConfig(
        IDictionary<string, string>? data = null,
        IDictionary<string, string>? vars = null)
    {
        return new FakeDataConfig(data, vars);
    }

    private static void True(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + message);
        }
    }

    private static void False(bool condition, string message)
    {
        True(!condition, message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Assertion failed: " + message + ". Expected <" + expected + ">, got <" + actual + ">.");
        }
    }

    private static void Approximately(float expected, float actual, float tolerance, string message)
    {
        assertions++;
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException("Assertion failed: " + message + ". Expected <" + expected + ">, got <" + actual + ">.");
        }
    }

    private static void NotEqual<T>(T unexpected, T actual, string message)
    {
        assertions++;
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new InvalidOperationException("Assertion failed: " + message + ". Did not expect <" + actual + ">.");
        }
    }

    private static class RuntimeMemberFixture
    {
        public static int Healthy => 42;

        public static int HealthyField = 7;

        public static int Unavailable => throw new NullReferenceException("role table unavailable");

        public static int Negative => -5;
    }

    private sealed class TurnEntry
    {
        public TurnEntry(string id, bool isPartner, int speed)
        {
            Id = id;
            IsPartner = isPartner;
            Speed = speed;
        }

        public string Id { get; }

        public bool IsPartner { get; }

        public int Speed { get; }
    }

    private sealed class FakeDataConfig : IDataConfig
    {
        public FakeDataConfig(IDictionary<string, string>? data, IDictionary<string, string>? vars)
        {
            this.data = data ?? new Dictionary<string, string>();
            Vars = vars ?? new Dictionary<string, string>();
            InstanceID = Guid.NewGuid().ToString("N");
        }

        public IDictionary<string, string> data { get; set; }

        public IDictionary<string, string> Vars { get; }

        public string InstanceID { get; }

        public DataType Type => DataType.Card;

        public IScriptExecutor scriptExecutor => throw new NotSupportedException();

        public bool isCompiling => false;
    }

    private sealed class TestScriptExecutor : IScriptExecutor
    {
        public IStatusManager Self { get; set; } = null!;

        public IStatusManager Target { get; set; } = null!;

        public IStatusManager status { get; set; } = null!;

        public List<IStatusManager> Object { get; set; } = new();

        public IDictionary<string, string> Vars { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private sealed class FakePresentationMaterial
    {
        public FakePresentationMaterial(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    private sealed class TestSpiritProfile
    {
        public TestSpiritProfile(string enemyId, string variantId)
        {
            EnemyId = enemyId;
            VariantId = variantId;
        }

        public string EnemyId { get; }

        public string VariantId { get; }
    }
}
