using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SpecialTagRuntime
{
    private static readonly object EventOwner = new();
    private static readonly Stack<PendingCard> Pending = new();
    private static string? registeredStatusId;

    public static void Initialize()
    {
        SunExpLog.Info("SpecialTag runtime initialized");
        TryRegisterForPlayer("Initialize");
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
        registeredStatusId = null;
        TryRegisterForPlayer("Fight_Start.Init");
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
        try
        {
            var player = FightPlayer.Instance;
            var statusId = player?.Status?.InstanceId;
            if (string.IsNullOrWhiteSpace(statusId) || registeredStatusId == statusId)
            {
                return;
            }

            EventCenter.Instance.Clear(EventOwner);
            EventCenter.Instance.AddEventListener("Action" + statusId, new Action<object>(OnAction), EventOwner, EventDispose.OnFightEnd);
            EventCenter.Instance.AddEventListener("ActionAfter" + statusId, new Action(OnActionAfter), EventOwner, EventDispose.OnFightEnd);
            registeredStatusId = statusId;
            SunExpLog.Info("Registered player Action listeners from " + source + ": statusId=" + statusId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to register player Action listeners from " + source, ex);
        }
    }

    private static void OnAction(object payload)
    {
        try
        {
            var config = CardConfigApi.FromActionPayload(payload);
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
