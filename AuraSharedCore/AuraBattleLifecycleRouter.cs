using System;
using System.Collections.Generic;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace AuraShared.Core;

public enum AuraBattleOutcome
{
    Win,
    Escape,
    Loss
}

public sealed class AuraBattleOutcomeContext
{
    public AuraBattleOutcome Outcome { get; internal set; }
    public string NativeSource { get; internal set; } = "";
    public ModHookContext NativeContext { get; internal set; } = new();
}

public sealed class AuraBattleLifecycleSubscription
{
    public Action<ModHookContext>? AdventureStarting { get; set; }
    public Action<ModHookContext>? BattleInitializing { get; set; }
    public Action<ModHookContext>? BattleManagerInitialized { get; set; }
    public Action<ModHookContext>? BattleMaterialized { get; set; }
    public Action<ModHookContext>? BattleOpening { get; set; }
    public Action<ModHookContext>? FightStartSignaled { get; set; }
    public Action<ModHookContext>? BattleReady { get; set; }
    public Action<ModHookContext>? ActionLoopStarting { get; set; }
    public Action<ModHookContext>? PlayerTurnEntering { get; set; }
    public Action<ModHookContext>? PlayerRoundStarting { get; set; }
    public Action<ModHookContext>? PlayerRoundReady { get; set; }
    public Action<ModHookContext>? BattleRestarting { get; set; }
    public Action<ModHookContext>? BattleRestarted { get; set; }
    public Action<AuraBattleOutcomeContext>? OutcomeEntering { get; set; }
    public Action<AuraBattleOutcomeContext>? BattleSettling { get; set; }
    public Action<AuraBattleOutcomeContext>? BattleEnded { get; set; }
}

public static class AuraBattleLifecycleRouter
{
    public const string GameEntryStartGame = "GameEntryUI.StartGame";
    public const string FightStartInit = "Fight_Start.Init";
    public const string FightManagerInit = "FightManager.Init";
    public const string FightInitInit = "FightInit.Init";
    public const string FightPlayerTurnInit = "Fight_PlayerTurn.Init";
    public const string FightManagerDoAllAction = "FightManager.DOAllAction";
    public const string FightManagerClearFightUi = "FightManager.UserCode_ClearFightui";
    public const string FightWinInit = "Fight_Win.Init";
    public const string FightEscapeInit = "Fight_Escape.Init";
    public const string FightLossInit = "Fight_Loss.Init";
    public const string FightWinResetStates = "Fight_Win.ResetStates";
    public const string FightEscapeResetStates = "Fight_Escape.ResetStates";

    private const string RuntimeOwnerId = "AuraBattleLifecycle";
    private static readonly object Gate = new();
    private static readonly object SignalOwner = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static Handler[] handlerSnapshot = Array.Empty<Handler>();
    private static bool initialized;
    private static long pendingRestartSessionId;
    private static string signalStatusId = "";
    private static AuraBattleOutcome? enteredOutcome;

    public static long CurrentBattleSessionId => AuraLifecycleSessionRuntime.CurrentBattleSessionId;

    public static long EnsureBattleSession() => AuraLifecycleSessionRuntime.EnsureBattleSession();

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        AuraBattleLifecycleSubscription subscription,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        if (subscription == null) return EmptyDisposable.Instance;

        var owner = string.IsNullOrWhiteSpace(ownerModId) ? "AuraShared" : ownerModId.Trim();
        var id = owner + ":" + (string.IsNullOrWhiteSpace(handlerId) ? Guid.NewGuid().ToString("N") : handlerId.Trim());
        lock (Gate)
        {
            EnsureInitialized(modConfig, info, warn);
            Handlers[id] = new Handler(id, subscription, warn);
            RebuildSnapshotNoLock();
        }

        AuraSharedLog.DebugLog(owner, "[BattleLifecycle] handler registered: " + id, false);
        return new Subscription(id);
    }

    private static void EnsureInitialized(ModConfig modConfig, Action<string>? info, Action<string>? warn)
    {
        if (initialized) return;
        initialized = true;
        var registry = new AuraHookRegistry(modConfig, RuntimeOwnerId, info, warn);
        registry.BeforeRouted(GameEntryStartGame,
            context => Dispatch(context, GameEntryStartGame, h => h.Subscription.AdventureStarting),
            "AdventureStarting");
        registry.BeforeRouted(FightManagerClearFightUi, BeginNativeFightRestart, "BattleRestarting");
        registry.AfterRouted(FightManagerInit,
            context => Dispatch(context, FightManagerInit, h => h.Subscription.BattleManagerInitialized),
            "BattleManagerInitialized");
        registry.BeforeRouted(FightInitInit, context =>
        {
            BeginBattleSession();
            DispatchOneShot(context, FightInitInit, "BattleInitializing", h => h.Subscription.BattleInitializing);
        }, "BattleInitializing");
        registry.AfterRouted(FightInitInit, context =>
        {
            EnsureBattleSession();
            RegisterSignalLane("FightInit.Init");
            DispatchOneShot(context, FightInitInit, "BattleMaterialized", h => h.Subscription.BattleMaterialized);
        }, "BattleMaterialized");
        registry.AfterRouted(FightStartInit, context =>
        {
            EnsureBattleSession();
            DispatchOneShot(context, FightStartInit, "BattleOpening", h => h.Subscription.BattleOpening);
            DispatchRestarted(context);
        }, "BattleOpening");
        registry.BeforeRouted(FightManagerDoAllAction,
            context => Dispatch(context, FightManagerDoAllAction, h => h.Subscription.ActionLoopStarting),
            "ActionLoopStarting");
        registry.BeforeRouted(FightPlayerTurnInit,
            context => Dispatch(context, FightPlayerTurnInit, h => h.Subscription.PlayerTurnEntering),
            "PlayerTurnEntering");
        registry.AfterRouted(FightPlayerTurnInit,
            context => Dispatch(context, FightPlayerTurnInit, h => h.Subscription.PlayerRoundReady),
            "PlayerRoundReady");

        registry.BeforeRouted(FightWinInit, context => DispatchOutcomeEntering(context, FightWinInit, AuraBattleOutcome.Win), "OutcomeEntering.Win");
        registry.BeforeRouted(FightEscapeInit, context => DispatchOutcomeEntering(context, FightEscapeInit, AuraBattleOutcome.Escape), "OutcomeEntering.Escape");
        registry.BeforeRouted(FightLossInit, context => DispatchOutcomeEntering(context, FightLossInit, AuraBattleOutcome.Loss), "OutcomeEntering.Loss");
        registry.BeforeRouted(FightWinResetStates, context => DispatchSettling(context, FightWinResetStates, AuraBattleOutcome.Win), "BattleSettling.Win");
        registry.BeforeRouted(FightEscapeResetStates, context => DispatchSettling(context, FightEscapeResetStates, AuraBattleOutcome.Escape), "BattleSettling.Escape");
        registry.BeforeRouted(FightLossInit, context => DispatchSettling(context, FightLossInit, AuraBattleOutcome.Loss), "BattleSettling.Loss");
        registry.AfterRouted(FightWinResetStates, context => DispatchEnded(context, FightWinResetStates, AuraBattleOutcome.Win), "BattleEnded.Win");
        registry.AfterRouted(FightEscapeResetStates, context => DispatchEnded(context, FightEscapeResetStates, AuraBattleOutcome.Escape), "BattleEnded.Escape");
        registry.AfterRouted(FightLossInit, context => DispatchEnded(context, FightLossInit, AuraBattleOutcome.Loss), "BattleEnded.Loss");
    }

    private static void BeginBattleSession()
    {
        ClearSignalLane();
        enteredOutcome = null;
        AuraLifecycleSessionRuntime.RestartBattleSession();
        AuraLifecycleOperationLedger.ClearScopePrefix("battle:");
    }

    private static void RegisterSignalLane(string source)
    {
        var statusId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        if (statusId.Length == 0 || string.Equals(signalStatusId, statusId, StringComparison.Ordinal)) return;

        try
        {
            ClearSignalLane();
            EventCenter.Instance.AddEventListener(
                "FightStart" + statusId,
                new Action(OnFightStartSignaled),
                SignalOwner,
                EventDispose.OnFightEnd);
            EventCenter.Instance.AddEventListener(
                "StartRound" + statusId,
                new Action(OnPlayerRoundStarting),
                SignalOwner,
                EventDispose.OnFightEnd);
            signalStatusId = statusId;
        }
        catch (Exception ex)
        {
            AuraSharedLog.Warn(RuntimeOwnerId, "Signal lane registration failed from " + source + ": " + ex.Message);
        }
    }

    private static void ClearSignalLane()
    {
        try { EventCenter.Instance.Clear(SignalOwner); } catch { }
        signalStatusId = "";
    }

    private static void OnFightStartSignaled()
    {
        var context = SyntheticContext();
        if (!DispatchOneShot(context, "EventCenter.FightStart", "FightStartSignaled", h => h.Subscription.FightStartSignaled)) return;

        var sessionId = CurrentBattleSessionId;
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = RuntimeOwnerId,
            Key = "BattleReady." + sessionId,
            Source = RuntimeOwnerId + ".BattleReady",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.GameplayMutation,
            Priority = 1000,
            EstimatedCost = 1,
            Action = () =>
            {
                if (AuraLifecycleSessionRuntime.IsBattleSessionActive && CurrentBattleSessionId == sessionId)
                {
                    DispatchOneShot(SyntheticContext(), "EventCenter.FightStart+1f", "BattleReady", h => h.Subscription.BattleReady);
                }
            }
        });
    }

    private static void OnPlayerRoundStarting()
    {
        Dispatch(SyntheticContext(), "EventCenter.StartRound", h => h.Subscription.PlayerRoundStarting);
    }

    private static ModHookContext SyntheticContext()
    {
        return new ModHookContext
        {
            Target = FightManager.Instance,
            Arguments = Array.Empty<object>()
        };
    }

    private static void DispatchOutcomeEntering(ModHookContext context, string source, AuraBattleOutcome outcome)
    {
        var fightType = FightManager.Instance?.fightType ?? FightType.None;
        if (outcome == AuraBattleOutcome.Win && fightType != FightType.Win
            || outcome == AuraBattleOutcome.Escape && fightType != FightType.Escape
            || outcome == AuraBattleOutcome.Loss && fightType != FightType.Loss)
        {
            return;
        }
        if (DispatchOutcomeOneShot(context, source, "OutcomeEntering", outcome, h => h.Subscription.OutcomeEntering))
        {
            enteredOutcome = outcome;
        }
    }

    private static void DispatchSettling(ModHookContext context, string source, AuraBattleOutcome outcome)
    {
        if (enteredOutcome != outcome) return;
        DispatchOutcomeOneShot(context, source, "BattleSettling", outcome, h => h.Subscription.BattleSettling);
    }

    private static void DispatchEnded(ModHookContext context, string source, AuraBattleOutcome outcome)
    {
        if (enteredOutcome != outcome) return;
        if (!DispatchOutcomeOneShot(context, source, "BattleEnded", outcome, h => h.Subscription.BattleEnded)) return;
        ClearSignalLane();
        AuraLifecycleOperationLedger.ClearScopePrefix("battle:");
        AuraLifecycleSessionRuntime.EndBattleSession();
        enteredOutcome = null;
    }

    private static bool DispatchOutcomeOneShot(
        ModHookContext context,
        string source,
        string phase,
        AuraBattleOutcome outcome,
        Func<Handler, Action<AuraBattleOutcomeContext>?> selector)
    {
        if (!TryClaimPhase(phase)) return false;
        var outcomeContext = new AuraBattleOutcomeContext
        {
            Outcome = outcome,
            NativeSource = source,
            NativeContext = context
        };
        var snapshot = handlerSnapshot;
        for (var i = 0; i < snapshot.Length; i++)
        {
            var action = selector(snapshot[i]);
            if (action != null) snapshot[i].InvokeOutcome(source, action, outcomeContext);
        }
        return true;
    }

    private static void BeginNativeFightRestart(ModHookContext context)
    {
        if (context.Target is not FightManager
            || UIManager.Instance?.GetUI<FightUI>("FightUI") == null
            || !AuraLifecycleSessionRuntime.TryBeginBattleRestart(out var interruptedSessionId))
        {
            return;
        }

        lock (Gate) pendingRestartSessionId = interruptedSessionId;
        ClearSignalLane();
        Dispatch(context, FightManagerClearFightUi, h => h.Subscription.BattleRestarting);
        AuraLifecycleOperationLedger.ClearScopePrefix("battle:");
    }

    private static void DispatchRestarted(ModHookContext context)
    {
        long interruptedSessionId;
        lock (Gate)
        {
            if (pendingRestartSessionId <= 0) return;
            interruptedSessionId = pendingRestartSessionId;
            pendingRestartSessionId = 0;
        }

        DispatchOneShot(context, FightStartInit, "BattleRestarted", h => h.Subscription.BattleRestarted);
        AuraSharedLog.Info(RuntimeOwnerId,
            "[BattleRestart] restarted interruptedSession=" + interruptedSessionId + ", rebuiltSession=" + CurrentBattleSessionId,
            mirrorCommands: false);
    }

    private static bool DispatchOneShot(
        ModHookContext context,
        string source,
        string phase,
        Func<Handler, Action<ModHookContext>?> selector)
    {
        if (!TryClaimPhase(phase)) return false;
        Dispatch(context, source, selector);
        return true;
    }

    private static bool TryClaimPhase(string phase)
    {
        if (!AuraLifecycleSessionRuntime.IsBattleSessionActive) return false;
        var sessionId = CurrentBattleSessionId;
        return sessionId > 0 && AuraLifecycleOperationLedger.TryClaim(
            "battle:" + sessionId,
            "AuraShared",
            "BattleLifecycle",
            phase,
            "",
            "phase",
            phase);
    }

    private static void Dispatch(
        ModHookContext context,
        string source,
        Func<Handler, Action<ModHookContext>?> selector)
    {
        var snapshot = handlerSnapshot;
        for (var i = 0; i < snapshot.Length; i++)
        {
            var action = selector(snapshot[i]);
            if (action != null) snapshot[i].Invoke(source, action, context);
        }
    }

    private static void RebuildSnapshotNoLock()
    {
        var next = new Handler[Handlers.Count];
        Handlers.Values.CopyTo(next, 0);
        handlerSnapshot = next;
    }

    private sealed class Handler
    {
        private readonly Action<string>? warn;
        public Handler(string id, AuraBattleLifecycleSubscription subscription, Action<string>? warn)
        {
            Id = id;
            Subscription = subscription;
            this.warn = warn;
        }

        public string Id { get; }
        public AuraBattleLifecycleSubscription Subscription { get; }

        public void Invoke(string source, Action<ModHookContext> action, ModHookContext context)
        {
            try { action(context); }
            catch (Exception ex) { warn?.Invoke("[AuraBattleLifecycle] handler failed: " + Id + " @ " + source + " -> " + ex.Message); }
        }

        public void InvokeOutcome(string source, Action<AuraBattleOutcomeContext> action, AuraBattleOutcomeContext context)
        {
            try { action(context); }
            catch (Exception ex) { warn?.Invoke("[AuraBattleLifecycle] outcome handler failed: " + Id + " @ " + source + " -> " + ex.Message); }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private bool disposed;
        public Subscription(string id) => this.id = id;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
            {
                if (Handlers.Remove(id)) RebuildSnapshotNoLock();
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
