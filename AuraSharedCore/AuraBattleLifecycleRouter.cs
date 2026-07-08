using System;
using System.Collections.Generic;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public sealed class AuraBattleLifecycleSubscription
{
    public Action<ModHookContext>? AdventureStarting { get; set; }

    public Action<ModHookContext>? FightStarting { get; set; }

    public Action<ModHookContext>? FightStarted { get; set; }

    public Action<ModHookContext>? PlayerRoundStarted { get; set; }

    public Action<ModHookContext>? FightEnding { get; set; }

    public Action<ModHookContext>? FightEnded { get; set; }
}

public static class AuraBattleLifecycleRouter
{
    public const string GameEntryStartGame = "GameEntryUI.StartGame";
    public const string FightStartInit = "Fight_Start.Init";
    public const string FightInitInit = "FightInit.Init";
    public const string FightPlayerTurnInit = "Fight_PlayerTurn.Init";
    public const string FightWinResetStates = "Fight_Win.ResetStates";
    public const string FightEscapeResetStates = "Fight_Escape.ResetStates";
    public const string FightLossInit = "Fight_Loss.Init";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static bool initialized;

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
        registry.BeforeRouted(FightInitInit, context => Dispatch(context, FightInitInit, h => h.Subscription.FightStarting), "FightStarting");
        registry.AfterRouted(FightStartInit, context => Dispatch(context, FightStartInit, h => h.Subscription.FightStarted), "FightStarted");
        registry.AfterRouted(FightInitInit, context => Dispatch(context, FightInitInit, h => h.Subscription.FightStarted), "FightStarted");
        registry.AfterRouted(FightPlayerTurnInit, context => Dispatch(context, FightPlayerTurnInit, h => h.Subscription.PlayerRoundStarted), "PlayerRoundStarted");
        registry.BeforeRouted(FightWinResetStates, context => Dispatch(context, FightWinResetStates, h => h.Subscription.FightEnding), "FightEnding");
        registry.BeforeRouted(FightEscapeResetStates, context => Dispatch(context, FightEscapeResetStates, h => h.Subscription.FightEnding), "FightEnding");
        registry.BeforeRouted(FightLossInit, context => Dispatch(context, FightLossInit, h => h.Subscription.FightEnding), "FightEnding");
        registry.AfterRouted(FightWinResetStates, context => Dispatch(context, FightWinResetStates, h => h.Subscription.FightEnded), "FightEnded");
        registry.AfterRouted(FightEscapeResetStates, context => Dispatch(context, FightEscapeResetStates, h => h.Subscription.FightEnded), "FightEnded");
        registry.AfterRouted(FightLossInit, context => Dispatch(context, FightLossInit, h => h.Subscription.FightEnded), "FightEnded");
    }

    private static void Dispatch(
        ModHookContext context,
        string source,
        Func<Handler, Action<ModHookContext>?> selector)
    {
        Handler[] snapshot;
        lock (Gate)
        {
            if (Handlers.Count == 0)
            {
                return;
            }

            snapshot = new Handler[Handlers.Count];
            Handlers.Values.CopyTo(snapshot, 0);
        }

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
                Handlers.Remove(id);
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
