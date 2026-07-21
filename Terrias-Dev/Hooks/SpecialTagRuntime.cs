using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SpecialTagRuntime
{
    private static readonly Stack<PendingCard> Pending = new();
    private static bool handlerRegistered;

    public static void Initialize()
    {
        EnsureHandlerRegistered();
        SunExpLog.Info("SpecialTag runtime initialized");
        SunExpActionEventRouter.EnsureRegistered("SpecialTag.Initialize");
    }

    [HookAfter(typeof(Fight_Start), nameof(Fight_Start.Init))]
    public static void OnFightStart(Fight_Start __instance)
    {
        try
        {
            RuntimeCardAttachmentService.ClearTemporaryAttachments("Fight_Start.Init");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to clear runtime card attachments from Fight_Start.Init", ex);
        }

        Pending.Clear();
        EnsureHandlerRegistered();
        SunExpActionEventRouter.ResetForFight("SpecialTag.Fight_Start.Init");
    }

    [HookBefore(typeof(CommonCardItem), nameof(CommonCardItem.TrueUse))]
    public static void BeforeCommonTrueUse(CommonCardItem __instance)
    {
        TryRegisterForPlayer("CommonCardItem.TrueUse.ensure");
    }

    [HookBefore(typeof(AttackCardItem), nameof(AttackCardItem.TrueUse))]
    public static void BeforeAttackTrueUse(AttackCardItem __instance)
    {
        TryRegisterForPlayer("AttackCardItem.TrueUse.ensure");
    }

    private static void TryRegisterForPlayer(string source)
    {
        EnsureHandlerRegistered();
        SunExpActionEventRouter.EnsureRegistered("SpecialTag." + source);
    }

    private static void EnsureHandlerRegistered()
    {
        if (handlerRegistered)
        {
            return;
        }

        SunExpActionEventRouter.RegisterHandler("SpecialTag.WhiteRadiance", OnAction, OnActionAfter);
        handlerRegistered = true;
    }

    private static void OnAction(SunExpActionEventContext context)
    {
        try
        {
            var config = context.Config;
            if (config == null)
            {
                SunExpLog.Debug("Action skipped: payload has no IDataConfig");
                return;
            }

            var isTemporary = CardConfigApi.HasTemporaryWhiteRadiance(config) && !CardConfigApi.HasNativeWhiteRadiance(config);
            var isNative = CardConfigApi.HasNativeWhiteRadiance(config);
            var isSpecial = CardConfigApi.HasSpecialWhiteRadiance(config) && !isTemporary && !isNative;
            if (isNative && CardConfigApi.Id(config) == "blazing_crown_collapse")
            {
                return;
            }

            if (!isTemporary && !isNative && !isSpecial)
            {
                return;
            }

            var kind = isTemporary ? "temporary" : isNative ? "native" : "special";
            var cost = CardConfigApi.CurrentCost(config);
            Pending.Push(new PendingCard(config, cost, kind));
            SunExpLog.Debug("White radiance captured: kind=" + kind + ", id=" + CardConfigApi.Id(config) + ", cost=" + cost + ", instance=" + config.InstanceID);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Action listener failed", ex);
        }
    }

    private static void OnActionAfter()
    {
        try
        {
            if (Pending.Count == 0)
            {
                return;
            }

            var pending = Pending.Pop();
            if (pending.Kind == "temporary" && !CardConfigApi.TryClaimTemporaryWhiteRadiance(pending.Config))
            {
                SunExpLog.Debug("Temp white radiance skipped: already resolved, id=" + CardConfigApi.Id(pending.Config));
                return;
            }

            var executor = pending.Config.scriptExecutor as ScriptExecutor;
            if (executor == null)
            {
                SunExpLog.Warn("Temp white radiance skipped: executor missing, id=" + CardConfigApi.Id(pending.Config));
                return;
            }

            var cost = CardConfigApi.ResolveSolarTriggerCost(pending.Config, pending.Cost);
            CardConfigApi.ClearSolarTriggerCost(pending.Config);
            SolarRadianceService.HandleSolarCardUsed(executor, cost, "ActionAfter." + pending.Kind);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("ActionAfter listener failed", ex);
        }
    }

    private readonly struct PendingCard
    {
        public PendingCard(IDataConfig config, int cost, string kind)
        {
            Config = config;
            Cost = cost;
            Kind = kind;
        }

        public IDataConfig Config { get; }

        public int Cost { get; }

        public string Kind { get; }
    }
}
