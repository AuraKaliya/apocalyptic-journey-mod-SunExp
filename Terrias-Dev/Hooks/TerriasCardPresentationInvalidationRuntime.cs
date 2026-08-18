using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasCardPresentationInvalidationRuntime
{
    [ThreadStatic] private static Stack<BuffMutation>? mutations;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Register(modConfig, TerriasHookTargets.StatusManagerAddBuff, MutationKind.Add);
        Register(modConfig, TerriasHookTargets.StatusManagerRemoveBuff, MutationKind.Remove);
        Register(modConfig, TerriasHookTargets.BuffItemConfigSetLevel, MutationKind.SetLevel);
        TerriasLog.InfoAlways("Card presentation invalidation runtime initialized");
    }

    private static void Register(ModConfig modConfig, string target, MutationKind kind)
    {
        TerriasHookRegistry.Before(modConfig, target, context => Begin(context, kind), "CardPresentationInvalidation");
        TerriasHookRegistry.After(modConfig, target, context => End(context, kind), "CardPresentationInvalidation");
    }

    private static void Begin(ModHookContext context, MutationKind kind)
    {
        mutations ??= new Stack<BuffMutation>();
        var resolved = TryResolve(context, kind, out var status, out var buffId, out var beforeLevel);
        var managed = resolved
            && CardPresentationImpactRegistry.TryForBuffMutation(buffId, beforeLevel, beforeLevel, out _);
        mutations.Push(new BuffMutation(
            kind,
            managed,
            status,
            buffId,
            beforeLevel,
            managed ? CardPresentationInvalidationApi.Capture() : default));
    }

    private static void End(ModHookContext context, MutationKind kind)
    {
        if (mutations == null || mutations.Count == 0)
        {
            return;
        }

        var current = mutations.Peek();
        if (current.Kind != kind)
        {
            return;
        }

        mutations.Pop();
        if (!current.Managed)
        {
            return;
        }

        var afterLevel = BuffApi.Level(current.Status, current.BuffId);
        if (afterLevel == current.BeforeLevel
            || !CardPresentationImpactRegistry.TryForBuffMutation(
                current.BuffId,
                current.BeforeLevel,
                afterLevel,
                out var spec))
        {
            return;
        }

        var source = kind + ":" + current.BuffId + ":" + current.BeforeLevel + "->" + afterLevel;
        var suppressed = CardPresentationInvalidationApi.SuppressNewFullRefresh(current.Snapshot, spec.Impact, source);
        if (suppressed && (spec.Fields & CardPresentationFields.Description) != 0)
        {
            TerriasCardRefreshQueue.RequestDescriptionUpdateForHandCards(
                CardPresentationInvalidationApi.CurrentHandCards(),
                spec.CardIds,
                "BuffDescriptionSubset:" + source);
        }

        TerriasPerformanceCounters.Record("CardPresentation.BuffMutation." + spec.Impact);
    }

    private static bool TryResolve(
        ModHookContext context,
        MutationKind kind,
        out IStatusManager? status,
        out string buffId,
        out int beforeLevel)
    {
        status = null;
        buffId = "";
        beforeLevel = 0;
        if (kind == MutationKind.SetLevel && context.Target is BuffItemConfig levelConfig)
        {
            status = levelConfig.status;
            buffId = levelConfig.BuffId ?? "";
            beforeLevel = Math.Max(0, levelConfig.Level);
            return status != null && buffId.Length > 0;
        }

        status = context.Target as IStatusManager;
        var args = context.Arguments;
        if (status == null || args == null || args.Length == 0)
        {
            return false;
        }

        buffId = args[0] is IBuffItemConfig config
            ? config.BuffId ?? ""
            : Convert.ToString(args[0]) ?? "";
        beforeLevel = BuffApi.Level(status, buffId);
        return buffId.Length > 0;
    }

    private enum MutationKind
    {
        Add,
        Remove,
        SetLevel
    }

    private readonly struct BuffMutation
    {
        public BuffMutation(
            MutationKind kind,
            bool managed,
            IStatusManager? status,
            string buffId,
            int beforeLevel,
            CardPresentationInvalidationSnapshot snapshot)
        {
            Kind = kind;
            Managed = managed;
            Status = status;
            BuffId = buffId ?? "";
            BeforeLevel = beforeLevel;
            Snapshot = snapshot;
        }

        public MutationKind Kind { get; }
        public bool Managed { get; }
        public IStatusManager? Status { get; }
        public string BuffId { get; }
        public int BeforeLevel { get; }
        public CardPresentationInvalidationSnapshot Snapshot { get; }
    }
}
