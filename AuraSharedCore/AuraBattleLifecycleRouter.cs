using System;
using System.Collections.Generic;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace AuraShared.Core;

public sealed class AuraBattleLifecycleSubscription
{
    public Action<ModHookContext>? AdventureStarting { get; set; }

    public Action<ModHookContext>? FightInitializing { get; set; }

    public Action<ModHookContext>? FightStarting { get; set; }

    public Action<ModHookContext>? FightInitialized { get; set; }

    public Action<ModHookContext>? FightOpening { get; set; }

    public Action<ModHookContext>? FightStarted { get; set; }

    public Action<ModHookContext>? PlayerRoundStarted { get; set; }

    public Action<ModHookContext>? FightRestarting { get; set; }

    public Action<ModHookContext>? FightRestarted { get; set; }

    public Action<ModHookContext>? FightEnding { get; set; }

    public Action<ModHookContext>? FightEnded { get; set; }
}

public static class AuraBattleLifecycleRouter
{
    public const string GameEntryStartGame = "GameEntryUI.StartGame";
    public const string FightStartInit = "Fight_Start.Init";
    public const string FightInitInit = "FightInit.Init";
    public const string FightPlayerTurnInit = "Fight_PlayerTurn.Init";
    public const string FightManagerClearFightUi = "FightManager.UserCode_ClearFightui";
    public const string FightWinResetStates = "Fight_Win.ResetStates";
    public const string FightEscapeResetStates = "Fight_Escape.ResetStates";
    public const string FightLossInit = "Fight_Loss.Init";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static Handler[] handlerSnapshot = Array.Empty<Handler>();
    private static bool initialized;
    private static long pendingRestartSessionId;

    public static long CurrentBattleSessionId => AuraLifecycleSessionRuntime.CurrentBattleSessionId;

    public static long EnsureBattleSession()
    {
        return AuraLifecycleSessionRuntime.EnsureBattleSession();
    }

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        AuraBattleLifecycleSubscription subscription,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        if (subscription == null)
        {
            return EmptyDisposable.Instance;
        }

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
        if (initialized)
        {
            return;
        }

        initialized = true;
        var registry = new AuraHookRegistry(modConfig, "AuraBattleLifecycle", info, warn);
        registry.BeforeRouted(GameEntryStartGame, context => Dispatch(context, GameEntryStartGame, h => h.Subscription.AdventureStarting), "AdventureStarting");
        registry.BeforeRouted(FightManagerClearFightUi, BeginNativeFightRestart, "FightRestarting");
        registry.BeforeRouted(FightInitInit, context =>
        {
            BeginBattleSession();
            DispatchPhase(context, FightInitInit, "FightInitializing", h => h.Subscription.FightInitializing);
            DispatchPhase(context, FightInitInit, "FightStarting", h => h.Subscription.FightStarting);
        }, "FightInitializing");
        registry.AfterRouted(FightStartInit, context =>
        {
            EnsureBattleSession();
            DispatchPhase(context, FightStartInit, "FightOpening", h => h.Subscription.FightOpening);
            var started = DispatchPhase(context, FightStartInit, "FightStarted", h => h.Subscription.FightStarted);
            if (started)
            {
                DispatchRestarted(context);
            }
        }, "FightOpening");
        registry.AfterRouted(FightInitInit, context =>
        {
            EnsureBattleSession();
            DispatchPhase(context, FightInitInit, "FightInitialized", h => h.Subscription.FightInitialized);
        }, "FightInitialized");
        registry.AfterRouted(FightPlayerTurnInit, context => Dispatch(context, FightPlayerTurnInit, h => h.Subscription.PlayerRoundStarted), "PlayerRoundStarted");
        registry.BeforeRouted(FightWinResetStates, context => DispatchPhase(context, FightWinResetStates, "FightEnding", h => h.Subscription.FightEnding), "FightEnding");
        registry.BeforeRouted(FightEscapeResetStates, context => DispatchPhase(context, FightEscapeResetStates, "FightEnding", h => h.Subscription.FightEnding), "FightEnding");
        registry.BeforeRouted(FightLossInit, context => DispatchPhase(context, FightLossInit, "FightEnding", h => h.Subscription.FightEnding), "FightEnding");
        registry.AfterRouted(FightWinResetStates, context => DispatchEnded(context, FightWinResetStates), "FightEnded");
        registry.AfterRouted(FightEscapeResetStates, context => DispatchEnded(context, FightEscapeResetStates), "FightEnded");
        registry.AfterRouted(FightLossInit, context => DispatchEnded(context, FightLossInit), "FightEnded");
    }

    private static void BeginBattleSession()
    {
        AuraLifecycleSessionRuntime.RestartBattleSession();
        AuraLifecycleOperationLedger.ClearScopePrefix("battle:");
    }

    private static void BeginNativeFightRestart(ModHookContext context)
    {
        if (context.Target is not FightManager
            || UIManager.Instance?.GetUI<FightUI>("FightUI") == null
            || !AuraLifecycleSessionRuntime.TryBeginBattleRestart(out var interruptedSessionId))
        {
            return;
        }

        lock (Gate)
        {
            pendingRestartSessionId = interruptedSessionId;
        }

        Dispatch(context, FightManagerClearFightUi, h => h.Subscription.FightRestarting);
        AuraLifecycleOperationLedger.ClearScopePrefix("battle:");
        AuraSharedLog.Info(
            "AuraBattleLifecycle",
            "[BattleRestart] restarting interruptedSession=" + interruptedSessionId,
            mirrorCommands: false);
    }

    private static void DispatchRestarted(ModHookContext context)
    {
        long interruptedSessionId;
        lock (Gate)
        {
            if (pendingRestartSessionId <= 0)
            {
                return;
            }

            interruptedSessionId = pendingRestartSessionId;
            pendingRestartSessionId = 0;
        }

        DispatchPhase(context, FightStartInit, "FightRestarted", h => h.Subscription.FightRestarted);
        AuraSharedLog.Info(
            "AuraBattleLifecycle",
            "[BattleRestart] restarted interruptedSession="
            + interruptedSessionId
            + ", rebuiltSession="
            + CurrentBattleSessionId,
            mirrorCommands: false);
    }

    private static void DispatchEnded(ModHookContext context, string source)
    {
        if (!DispatchPhase(context, source, "FightEnded", h => h.Subscription.FightEnded))
        {
            return;
        }

        AuraLifecycleOperationLedger.ClearScopePrefix("battle:");
        AuraLifecycleSessionRuntime.EndBattleSession();
    }

    private static bool DispatchPhase(
        ModHookContext context,
        string source,
        string phase,
        Func<Handler, Action<ModHookContext>?> selector)
    {
        if (!AuraLifecycleSessionRuntime.IsBattleSessionActive)
        {
            return false;
        }

        var sessionId = AuraLifecycleSessionRuntime.CurrentBattleSessionId;
        if (sessionId <= 0)
        {
            return false;
        }
        if (!AuraLifecycleOperationLedger.TryClaim(
                "battle:" + sessionId,
                "AuraShared",
                "BattleLifecycle",
                phase,
                "",
                "phase",
                phase))
        {
            return false;
        }

        Dispatch(context, source, selector);
        return true;
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
            if (action == null)
            {
                continue;
            }

            snapshot[i].Invoke(source, action, context);
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
            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                warn?.Invoke("[AuraBattleLifecycle] handler failed: " + Id + " @ " + source + " -> " + ex.Message);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private bool disposed;

        public Subscription(string id)
        {
            this.id = id;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (Gate)
            {
                if (Handlers.Remove(id))
                {
                    RebuildSnapshotNoLock();
                }
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
