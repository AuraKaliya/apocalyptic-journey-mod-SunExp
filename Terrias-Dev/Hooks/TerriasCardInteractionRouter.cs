using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasCardInteractionSubscription
{
    public int Priority { get; set; }
    public Action<ModHookContext>? BeforeCommonBeginDrag { get; set; }
    public Action<ModHookContext>? AfterCommonBeginDrag { get; set; }
    public Action<ModHookContext>? AfterCommonEndDrag { get; set; }
    public Action<ModHookContext>? BeforeCommonUseDirectly { get; set; }
    public Action<ModHookContext>? AfterCommonUseDirectly { get; set; }
    public Action<ModHookContext>? AfterAttackPointerDown { get; set; }
    public Action<ModHookContext>? AfterAttackCancelLineMode { get; set; }
    public Action<ModHookContext>? AfterAttackCommitOrCancel { get; set; }
    public Action<ModHookContext>? AfterCardCancelUseDrag { get; set; }
    public Action<ModHookContext>? BeforeCardDestroy { get; set; }
}

public static class TerriasCardInteractionRouter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, TerriasCardInteractionSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static PhaseSnapshot phaseSnapshot = PhaseSnapshot.Empty;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        Before(modConfig, "CommonCardItem.OnBeginDrag", nameof(TerriasCardInteractionSubscription.BeforeCommonBeginDrag), s => s.BeforeCommonBeginDrag);
        After(modConfig, "CommonCardItem.OnBeginDrag", nameof(TerriasCardInteractionSubscription.AfterCommonBeginDrag), s => s.AfterCommonBeginDrag);
        After(modConfig, "CommonCardItem.OnEndDrag", nameof(TerriasCardInteractionSubscription.AfterCommonEndDrag), s => s.AfterCommonEndDrag);
        Before(modConfig, "CommonCardItem.UseCardDirectly", nameof(TerriasCardInteractionSubscription.BeforeCommonUseDirectly), s => s.BeforeCommonUseDirectly);
        After(modConfig, "CommonCardItem.UseCardDirectly", nameof(TerriasCardInteractionSubscription.AfterCommonUseDirectly), s => s.AfterCommonUseDirectly);
        After(modConfig, "AttackCardItem.OnPointerDown", nameof(TerriasCardInteractionSubscription.AfterAttackPointerDown), s => s.AfterAttackPointerDown);
        After(modConfig, "AttackCardItem.CancelLineMode", nameof(TerriasCardInteractionSubscription.AfterAttackCancelLineMode), s => s.AfterAttackCancelLineMode);
        After(modConfig, "AttackCardItem.CommitOrCancelFromKeyboard", nameof(TerriasCardInteractionSubscription.AfterAttackCommitOrCancel), s => s.AfterAttackCommitOrCancel);
        After(modConfig, "CardItem.CancelUseDrag", nameof(TerriasCardInteractionSubscription.AfterCardCancelUseDrag), s => s.AfterCardCancelUseDrag);
        Before(modConfig, "CardItem.OnDestroy", nameof(TerriasCardInteractionSubscription.BeforeCardDestroy), s => s.BeforeCardDestroy);
    }

    public static IDisposable Register(string id, TerriasCardInteractionSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null) return EmptyDisposable.Instance;
        var normalized = id.Trim();
        lock (Gate)
        {
            Subscriptions[normalized] = subscription;
            RebuildNoLock();
        }
        return new Registration(normalized, subscription);
    }

    private static void Before(
        ModConfig config,
        string target,
        string phase,
        Func<TerriasCardInteractionSubscription, Action<ModHookContext>?> selector)
    {
        TerriasHookRegistry.BeforeRouted(config, target, context => Dispatch(phase, target, context, selector), "CardInteraction");
    }

    private static void After(
        ModConfig config,
        string target,
        string phase,
        Func<TerriasCardInteractionSubscription, Action<ModHookContext>?> selector)
    {
        TerriasHookRegistry.AfterRouted(config, target, context => Dispatch(phase, target, context, selector), "CardInteraction");
    }

    private static void Dispatch(
        string phase,
        string source,
        ModHookContext context,
        Func<TerriasCardInteractionSubscription, Action<ModHookContext>?> selector)
    {
        var snapshot = phaseSnapshot.Get(phase);
        for (var i = 0; i < snapshot.Length; i++)
        {
            var action = selector(snapshot[i].Subscription);
            if (action != null) snapshot[i].Invoke(action, context, source);
        }
    }

    private static void RebuildNoLock()
    {
        phaseSnapshot = new PhaseSnapshot(new Dictionary<string, PhaseHandler[]>(StringComparer.Ordinal)
        {
            [nameof(TerriasCardInteractionSubscription.BeforeCommonBeginDrag)] = Build(s => s.BeforeCommonBeginDrag),
            [nameof(TerriasCardInteractionSubscription.AfterCommonBeginDrag)] = Build(s => s.AfterCommonBeginDrag),
            [nameof(TerriasCardInteractionSubscription.AfterCommonEndDrag)] = Build(s => s.AfterCommonEndDrag),
            [nameof(TerriasCardInteractionSubscription.BeforeCommonUseDirectly)] = Build(s => s.BeforeCommonUseDirectly),
            [nameof(TerriasCardInteractionSubscription.AfterCommonUseDirectly)] = Build(s => s.AfterCommonUseDirectly),
            [nameof(TerriasCardInteractionSubscription.AfterAttackPointerDown)] = Build(s => s.AfterAttackPointerDown),
            [nameof(TerriasCardInteractionSubscription.AfterAttackCancelLineMode)] = Build(s => s.AfterAttackCancelLineMode),
            [nameof(TerriasCardInteractionSubscription.AfterAttackCommitOrCancel)] = Build(s => s.AfterAttackCommitOrCancel),
            [nameof(TerriasCardInteractionSubscription.AfterCardCancelUseDrag)] = Build(s => s.AfterCardCancelUseDrag),
            [nameof(TerriasCardInteractionSubscription.BeforeCardDestroy)] = Build(s => s.BeforeCardDestroy)
        });
    }

    private static PhaseHandler[] Build(Func<TerriasCardInteractionSubscription, Action<ModHookContext>?> selector)
    {
        return Subscriptions
            .Where(pair => selector(pair.Value) != null)
            .OrderByDescending(pair => pair.Value.Priority)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PhaseHandler(pair.Key, pair.Value))
            .ToArray();
    }

    private sealed class PhaseSnapshot
    {
        public static readonly PhaseSnapshot Empty = new(new Dictionary<string, PhaseHandler[]>(StringComparer.Ordinal));
        private readonly Dictionary<string, PhaseHandler[]> phases;
        public PhaseSnapshot(Dictionary<string, PhaseHandler[]> phases) => this.phases = phases;
        public PhaseHandler[] Get(string phase) => phases.TryGetValue(phase, out var handlers) ? handlers : Array.Empty<PhaseHandler>();
    }

    private readonly struct PhaseHandler
    {
        private readonly string id;
        public PhaseHandler(string id, TerriasCardInteractionSubscription subscription)
        {
            this.id = id;
            Subscription = subscription;
        }
        public TerriasCardInteractionSubscription Subscription { get; }
        public void Invoke(Action<ModHookContext> action, ModHookContext context, string source)
        {
            try { action(context); }
            catch (Exception ex) { TerriasLog.Error("Card interaction handler failed: " + id + " @ " + source, ex); }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly string id;
        private readonly TerriasCardInteractionSubscription subscription;
        private bool disposed;
        public Registration(string id, TerriasCardInteractionSubscription subscription) { this.id = id; this.subscription = subscription; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
            {
                if (Subscriptions.TryGetValue(id, out var current) && ReferenceEquals(current, subscription))
                {
                    Subscriptions.Remove(id);
                    RebuildNoLock();
                }
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
