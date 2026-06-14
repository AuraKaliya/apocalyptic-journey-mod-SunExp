using System;
using System.Collections.Generic;
using GoldExp.Dll.GameApi;
using GoldExp.Dll.Infrastructure;
using GoldExp.Dll.Mechanics;
using Witch.Mod;

namespace GoldExp.Dll.Hooks;

public static class GoldDreamTagRuntime
{
    private static readonly object EventOwner = new();
    private static readonly Stack<PendingCard> Pending = new();
    private static string? registeredStatusId;

    public static void Initialize()
    {
        GoldExpLog.Info("Gold Dream tag runtime initialized");
        TryRegisterForPlayer("Initialize");
    }

    [HookAfter(typeof(Fight_Start), nameof(Fight_Start.Init))]
    public static void OnFightStart(Fight_Start __instance)
    {
        Pending.Clear();
        registeredStatusId = null;
        GoldDreamService.ApplyGoldenPotentialAtFightStart();
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
            GoldExpLog.Info("Registered player Action listeners from " + source + ": statusId=" + statusId);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Failed to register player Action listeners from " + source, ex);
        }
    }

    private static void OnAction(object payload)
    {
        try
        {
            var config = CardConfigApi.FromActionPayload(payload);
            if (config == null)
            {
                GoldExpLog.Debug("Golden Dream Action skipped: payload has no IDataConfig");
                return;
            }

            var isTemporary = CardConfigApi.HasTemporaryGoldDream(config) && !CardConfigApi.HasNativeGoldDream(config);
            var isNative = CardConfigApi.HasNativeGoldDream(config);
            var isSpecial = CardConfigApi.HasSpecialGoldDream(config) && !isTemporary && !isNative;
            if (!isTemporary && !isNative && !isSpecial)
            {
                return;
            }

            var kind = isTemporary ? "temporary" : isNative ? "native" : "special";
            Pending.Push(new PendingCard(config, kind));
            GoldExpLog.Debug("Golden Dream captured: kind=" + kind + ", id=" + CardConfigApi.Id(config) + ", instance=" + config.InstanceID);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Golden Dream Action listener failed", ex);
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
            if (pending.Kind == "temporary" && !CardConfigApi.TryClaimTemporaryGoldDream(pending.Config))
            {
                GoldExpLog.Debug("Temporary Golden Dream skipped: already resolved, id=" + CardConfigApi.Id(pending.Config));
                return;
            }

            var executor = pending.Config.scriptExecutor as ScriptExecutor;
            if (executor == null)
            {
                GoldExpLog.Warn("Golden Dream skipped: executor missing, id=" + CardConfigApi.Id(pending.Config));
                return;
            }

            GoldDreamService.HandleGoldDreamCardPlayed(executor, "ActionAfter." + pending.Kind);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Golden Dream ActionAfter listener failed", ex);
        }
    }

    private readonly struct PendingCard
    {
        public PendingCard(IDataConfig config, string kind)
        {
            Config = config;
            Kind = kind;
        }

        public IDataConfig Config { get; }

        public string Kind { get; }
    }
}
