using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public enum TerriasBuffMutationKind
{
    Add,
    Remove,
    SetLevel
}

public readonly struct TerriasBuffMutationContext
{
    public TerriasBuffMutationContext(
        TerriasBuffMutationKind kind,
        IStatusManager? status,
        string buffId,
        int beforeLevel,
        int afterLevel,
        int requestedLevel,
        object[]? arguments,
        bool wasNativeRefreshPending,
        bool isNativeRefreshPending,
        bool containsNestedMutation = false)
    {
        Kind = kind;
        Status = status;
        BuffId = buffId ?? "";
        BeforeLevel = beforeLevel;
        AfterLevel = afterLevel;
        RequestedLevel = requestedLevel;
        Arguments = arguments;
        WasNativeRefreshPending = wasNativeRefreshPending;
        IsNativeRefreshPending = isNativeRefreshPending;
        ContainsNestedMutation = containsNestedMutation;
    }

    public TerriasBuffMutationKind Kind { get; }
    public IStatusManager? Status { get; }
    public string BuffId { get; }
    public int BeforeLevel { get; }
    public int AfterLevel { get; }
    public int RequestedLevel { get; }
    public int Delta => AfterLevel - BeforeLevel;
    public bool Changed => BeforeLevel != AfterLevel;
    public object[]? Arguments { get; }
    public bool WasNativeRefreshPending { get; }
    public bool IsNativeRefreshPending { get; }
    public bool ContainsNestedMutation { get; }
}

public readonly struct TerriasBuffCheckContext
{
    public TerriasBuffCheckContext(
        IStatusManager? status,
        string way,
        bool wasPending,
        bool isPending,
        IReadOnlyList<TerriasBuffMutationContext> mutations)
    {
        Status = status;
        Way = way ?? "";
        WasPending = wasPending;
        IsPending = isPending;
        Mutations = mutations;
    }

    public IStatusManager? Status { get; }
    public string Way { get; }
    public bool WasPending { get; }
    public bool IsPending { get; }
    public IReadOnlyList<TerriasBuffMutationContext> Mutations { get; }
}

public sealed class TerriasBuffMutationSubscription
{
    public int Priority { get; set; }
    public Action<TerriasBuffMutationContext>? BeforeAdd { get; set; }
    public Action<TerriasBuffMutationContext>? Changed { get; set; }
    public Action<TerriasBuffCheckContext>? CheckCompleted { get; set; }
}

public static class TerriasBuffMutationRouter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.Ordinal);
    private static Handler[] beforeAddHandlers = Array.Empty<Handler>();
    private static Handler[] changedHandlers = Array.Empty<Handler>();
    private static Handler[] checkHandlers = Array.Empty<Handler>();
    private static bool initialized;

    [ThreadStatic] private static Stack<MutationFrame>? mutationFrames;
    [ThreadStatic] private static Stack<MutationFrame>? mutationFramePool;
    [ThreadStatic] private static Stack<BuffCheckFrame>? checkFrames;
    [ThreadStatic] private static Stack<BuffCheckFrame>? checkFramePool;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        RegisterMutationHooks(modConfig, TerriasHookTargets.StatusManagerAddBuff, TerriasBuffMutationKind.Add);
        RegisterMutationHooks(modConfig, TerriasHookTargets.StatusManagerRemoveBuff, TerriasBuffMutationKind.Remove);
        RegisterMutationHooks(modConfig, TerriasHookTargets.BuffItemConfigSetLevel, TerriasBuffMutationKind.SetLevel);
        TerriasHookRegistry.BeforeRouted(
            modConfig,
            TerriasHookTargets.BuffBarUiCheckAllBuff,
            BeginCheck,
            "BuffMutation.CheckAllBuff");
        TerriasHookRegistry.AfterRouted(
            modConfig,
            TerriasHookTargets.BuffBarUiCheckAllBuff,
            EndCheck,
            "BuffMutation.CheckAllBuff");
    }

    public static IDisposable Register(string id, TerriasBuffMutationSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return EmptyDisposable.Instance;
        }

        var normalized = id.Trim();
        Handler handler;
        lock (Gate)
        {
            handler = new Handler(normalized, subscription);
            Handlers[normalized] = handler;
            RebuildNoLock();
        }

        return new Subscription(normalized, handler);
    }

    private static void RegisterMutationHooks(ModConfig config, string target, TerriasBuffMutationKind kind)
    {
        TerriasHookRegistry.BeforeRouted(
            config,
            target,
            context => BeginMutation(context, kind),
            "BuffMutation." + kind);
        TerriasHookRegistry.AfterRouted(
            config,
            target,
            context => EndMutation(context, kind),
            "BuffMutation." + kind);
    }

    private static void BeginMutation(ModHookContext context, TerriasBuffMutationKind kind)
    {
        Resolve(context, kind, out var status, out var buffId, out var before, out var requested);
        mutationFrames ??= new Stack<MutationFrame>();
        var frame = RentMutationFrame();
        frame.Kind = kind;
        frame.Status = status;
        frame.BuffId = buffId;
        frame.BeforeLevel = before;
        frame.RequestedLevel = requested;
        frame.Arguments = context.Arguments;
        frame.WasNativeRefreshPending = ReadNativePendingFlag();
        mutationFrames.Push(frame);
        if (kind == TerriasBuffMutationKind.Add)
        {
            DispatchBeforeAdd(new TerriasBuffMutationContext(
                kind,
                status,
                buffId,
                before,
                before,
                requested,
                context.Arguments,
                mutationFrames.Peek().WasNativeRefreshPending,
                mutationFrames.Peek().WasNativeRefreshPending));
        }
    }

    private static void EndMutation(ModHookContext context, TerriasBuffMutationKind kind)
    {
        if (mutationFrames == null || mutationFrames.Count == 0)
        {
            return;
        }

        var frame = mutationFrames.Peek();
        if (frame.Kind != kind)
        {
            TerriasLog.Warn("Buff mutation stack mismatch: expected=" + frame.Kind + ", actual=" + kind);
            return;
        }

        mutationFrames.Pop();
        var after = BuffApi.Level(frame.Status, frame.BuffId);
        var mutation = new TerriasBuffMutationContext(
            kind,
            frame.Status,
            frame.BuffId,
            frame.BeforeLevel,
            after,
            frame.RequestedLevel,
            frame.Arguments,
            frame.WasNativeRefreshPending,
            ReadNativePendingFlag(),
            frame.ContainsNestedMutation);
        if (!mutation.Changed)
        {
            ReturnMutationFrame(frame);
            return;
        }

        if (mutationFrames.Count > 0)
        {
            var parent = mutationFrames.Peek();
            if (ReferenceEquals(parent.Status, mutation.Status)
                && string.Equals(parent.BuffId, mutation.BuffId, StringComparison.Ordinal))
            {
                parent.ContainsNestedMutation = true;
            }
        }

        if (!mutation.ContainsNestedMutation && checkFrames != null && checkFrames.Count > 0)
        {
            checkFrames.Peek().Mutations.Add(mutation);
        }

        DispatchChanged(mutation);
        ReturnMutationFrame(frame);
    }

    private static void BeginCheck(ModHookContext context)
    {
        var frame = RentCheckFrame();
        frame.Status = ReadStatusFromBuffBar(context.Target);
        frame.Way = context.Arguments != null && context.Arguments.Length > 0
            ? Convert.ToString(context.Arguments[0]) ?? ""
            : "";
        frame.WasPending = ReadNativePendingFlag();
        checkFrames ??= new Stack<BuffCheckFrame>();
        checkFrames.Push(frame);
    }

    private static void EndCheck(ModHookContext context)
    {
        if (checkFrames == null || checkFrames.Count == 0)
        {
            return;
        }

        var frame = checkFrames.Pop();
        var mutations = frame.Mutations.Count == 0
            ? Array.Empty<TerriasBuffMutationContext>()
            : frame.Mutations.ToArray();
        var completed = new TerriasBuffCheckContext(
            frame.Status,
            frame.Way,
            frame.WasPending,
            ReadNativePendingFlag(),
            mutations);
        ReturnCheckFrame(frame);
        DispatchCheck(completed);
    }

    private static void Resolve(
        ModHookContext context,
        TerriasBuffMutationKind kind,
        out IStatusManager? status,
        out string buffId,
        out int before,
        out int requested)
    {
        status = null;
        buffId = "";
        before = 0;
        requested = 0;
        if (kind == TerriasBuffMutationKind.SetLevel && context.Target is BuffItemConfig levelConfig)
        {
            status = levelConfig.status;
            buffId = levelConfig.BuffId ?? "";
            before = Math.Max(0, levelConfig.Level);
            requested = context.Arguments != null
                        && context.Arguments.Length > 0
                        && context.Arguments[0] is int level
                ? level
                : before;
            return;
        }

        status = context.Target as IStatusManager;
        var args = context.Arguments;
        if (args == null || args.Length == 0)
        {
            return;
        }

        if (args[0] is IBuffItemConfig config)
        {
            buffId = config.BuffId ?? "";
            requested = config.Level;
        }
        else
        {
            buffId = Convert.ToString(args[0]) ?? "";
            requested = DictionaryUtil.ParseInt(Convert.ToString(args.Length > 1 ? args[1] : null));
        }

        before = BuffApi.Level(status, buffId);
    }

    private static IStatusManager? ReadStatusFromBuffBar(object? target)
    {
        return (target as BuffBarUI)?.status;
    }

    private static bool ReadNativePendingFlag()
    {
        try
        {
            return UIManager.Instance?.GetUI<FightUI>("FightUI")?.NeedUpdateCardMsg == true;
        }
        catch
        {
            return false;
        }
    }

    private static BuffCheckFrame RentCheckFrame()
    {
        checkFramePool ??= new Stack<BuffCheckFrame>();
        var frame = checkFramePool.Count > 0 ? checkFramePool.Pop() : new BuffCheckFrame();
        frame.Reset();
        return frame;
    }

    private static MutationFrame RentMutationFrame()
    {
        mutationFramePool ??= new Stack<MutationFrame>();
        var frame = mutationFramePool.Count > 0 ? mutationFramePool.Pop() : new MutationFrame();
        frame.Reset();
        return frame;
    }

    private static void ReturnMutationFrame(MutationFrame frame)
    {
        frame.Reset();
        mutationFramePool ??= new Stack<MutationFrame>();
        mutationFramePool.Push(frame);
    }

    private static void ReturnCheckFrame(BuffCheckFrame frame)
    {
        frame.Reset();
        checkFramePool ??= new Stack<BuffCheckFrame>();
        checkFramePool.Push(frame);
    }

    private static void DispatchBeforeAdd(TerriasBuffMutationContext context)
    {
        var snapshot = beforeAddHandlers;
        for (var i = 0; i < snapshot.Length; i++) snapshot[i].InvokeBeforeAdd(context);
    }

    private static void DispatchChanged(TerriasBuffMutationContext context)
    {
        var snapshot = changedHandlers;
        for (var i = 0; i < snapshot.Length; i++) snapshot[i].InvokeChanged(context);
    }

    private static void DispatchCheck(TerriasBuffCheckContext context)
    {
        var snapshot = checkHandlers;
        for (var i = 0; i < snapshot.Length; i++) snapshot[i].InvokeCheck(context);
    }

    private static void RebuildNoLock()
    {
        beforeAddHandlers = Ordered(handler => handler.Subscription.BeforeAdd != null);
        changedHandlers = Ordered(handler => handler.Subscription.Changed != null);
        checkHandlers = Ordered(handler => handler.Subscription.CheckCompleted != null);
    }

    private static Handler[] Ordered(Func<Handler, bool> predicate)
    {
        return Handlers.Values
            .Where(predicate)
            .OrderByDescending(handler => handler.Subscription.Priority)
            .ThenBy(handler => handler.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class MutationFrame
    {
        public TerriasBuffMutationKind Kind;
        public IStatusManager? Status;
        public string BuffId = "";
        public int BeforeLevel;
        public int RequestedLevel;
        public object[]? Arguments;
        public bool WasNativeRefreshPending;
        public bool ContainsNestedMutation;

        public void Reset()
        {
            Kind = default;
            Status = null;
            BuffId = "";
            BeforeLevel = 0;
            RequestedLevel = 0;
            Arguments = null;
            WasNativeRefreshPending = false;
            ContainsNestedMutation = false;
        }
    }

    private sealed class BuffCheckFrame
    {
        public IStatusManager? Status;
        public string Way = "";
        public bool WasPending;
        public List<TerriasBuffMutationContext> Mutations { get; } = new();

        public void Reset()
        {
            Status = null;
            Way = "";
            WasPending = false;
            Mutations.Clear();
        }
    }

    private sealed class Handler
    {
        public Handler(string id, TerriasBuffMutationSubscription subscription)
        {
            Id = id;
            Subscription = subscription;
        }

        public string Id { get; }
        public TerriasBuffMutationSubscription Subscription { get; }

        public void InvokeBeforeAdd(TerriasBuffMutationContext context) => Invoke(() => Subscription.BeforeAdd?.Invoke(context), "BeforeAdd");
        public void InvokeChanged(TerriasBuffMutationContext context) => Invoke(() => Subscription.Changed?.Invoke(context), "Changed");
        public void InvokeCheck(TerriasBuffCheckContext context) => Invoke(() => Subscription.CheckCompleted?.Invoke(context), "CheckCompleted");

        private void Invoke(Action action, string phase)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                TerriasLog.Error("Buff mutation handler failed: " + Id + " @ " + phase, ex);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private readonly Handler handler;
        private bool disposed;

        public Subscription(string id, Handler handler)
        {
            this.id = id;
            this.handler = handler;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
            {
                if (Handlers.TryGetValue(id, out var current) && ReferenceEquals(current, handler))
                {
                    Handlers.Remove(id);
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
