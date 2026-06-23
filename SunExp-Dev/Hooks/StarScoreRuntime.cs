using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class StarScoreRuntime
{
    private const string PendingPreludeCostVar = "SunExpStarBlessingPreludeCost";
    private const string PendingFreeVar = "SunExpStarBlessingFreePending";
    private static readonly object EventOwner = new();
    private static readonly Stack<PendingCard> Pending = new();
    private static string? registeredStatusId;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart);
        RegisterBefore(modConfig, "CommonCardItem.TrueUse", OnCardUseBefore);
        RegisterBefore(modConfig, "AttackCardItem.TrueUse", OnCardUseBefore);
        SunExpLog.Info("Star score runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Star score " + message));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Star score " + message));
    }

    private static void OnFightStart(ModHookContext context)
    {
        Pending.Clear();
        registeredStatusId = null;
        StarScoreCombatStateStore.ClearAll();
        ExecutorApi.CombatIntSet("SunExpStarScorePlayerActionPending", 0);
        TryRegisterForPlayer("Fight_Start.Init");
    }

    private static void OnCardUseBefore(ModHookContext context)
    {
        try
        {
            TryRegisterForPlayer("CardUseBefore");
            var config = CardConfigApi.FromActionPayload(context.Target);
            if (config == null || StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(config)))
            {
                return;
            }

            if (DictionaryUtil.Get(config.Vars, PendingFreeVar, "0") == "1")
            {
                return;
            }

            var player = FightPlayer.Instance?.Status;
            if (player == null || BuffApi.Level(player, SunExpIds.StarBlessing) <= 0)
            {
                return;
            }

            var baseCost = HasMorningStarSeal(config) ? 0 : CardConfigApi.BaseCost(config);
            DictionaryUtil.Set(config.Vars, PendingPreludeCostVar, baseCost.ToString());
            DictionaryUtil.Set(config.Vars, PendingFreeVar, "1");
            ConsumeBuff(player, SunExpIds.StarBlessing, 1);
            MakeCurrentUseFree(config);
            PlayerApi.ShowCaption("\u661f\u8fb0\u795d\u798f\uff1a\u672c\u6b21\u51fa\u724c\u65e0\u6d88\u8017\u3002");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star blessing before-use hook failed", ex);
        }
    }

    public static void TryApplyResonanceBeforeAddBuff(ModHookContext context)
    {
        try
        {
            StarScoreService.TryApplyResonanceBeforeAddBuff(context.Arguments);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Resonance add-buff hook failed", ex);
        }
    }

    private static void TryRegisterForPlayer(string source)
    {
        try
        {
            var statusId = FightPlayer.Instance?.Status?.InstanceId;
            if (string.IsNullOrWhiteSpace(statusId) || registeredStatusId == statusId)
            {
                return;
            }

            EventCenter.Instance.Clear(EventOwner);
            EventCenter.Instance.AddEventListener("Action" + statusId, new Action<object>(OnAction), EventOwner, EventDispose.OnFightEnd);
            EventCenter.Instance.AddEventListener("ActionAfter" + statusId, new Action(OnActionAfter), EventOwner, EventDispose.OnFightEnd);
            registeredStatusId = statusId;
            SunExpLog.Info("Registered star score player Action listeners from " + source + ": statusId=" + statusId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to register star score listeners from " + source, ex);
        }
    }

    private static void OnAction(object payload)
    {
        try
        {
            var config = CardConfigApi.FromActionPayload(payload);
            if (config == null)
            {
                return;
            }

            var executor = config.scriptExecutor as ScriptExecutor;
            var pendingPreludeCost = DictionaryUtil.Get(config.Vars, PendingPreludeCostVar);
            var preludeCost = string.IsNullOrWhiteSpace(pendingPreludeCost)
                ? -1
                : Math.Max(0, DictionaryUtil.ParseInt(pendingPreludeCost));
            Pending.Push(new PendingCard(config, executor, preludeCost));
            ExecutorApi.CombatIntAdd("SunExpStarScorePlayerActionPending", 1);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star score Action listener failed", ex);
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
            if (pending.Executor != null && pending.PreludeCost >= 0)
            {
                CardApi.AddCardToHand(pending.Executor, StarScoreService.PreludeCardForCost(pending.PreludeCost));
                PlayerApi.ShowCaption("\u83b7\u5f97" + StarScoreService.PreludeDisplayNameForCost(pending.PreludeCost));
            }

            DictionaryUtil.Set(pending.Config.Vars, PendingPreludeCostVar, "");
            DictionaryUtil.Set(pending.Config.Vars, PendingFreeVar, "0");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star score ActionAfter listener failed", ex);
        }
        finally
        {
            ExecutorApi.CombatIntSet("SunExpStarScorePlayerActionPending", Math.Max(0, ExecutorApi.CombatIntGet("SunExpStarScorePlayerActionPending") - 1));
        }
    }

    private static void MakeCurrentUseFree(IDataConfig config)
    {
        var currentCost = CardConfigApi.CurrentCost(config);
        if (currentCost <= 0)
        {
            return;
        }

        var oldOnce = DictionaryUtil.GetInt(config.Vars, "OnceExCost");
        DictionaryUtil.Set(config.Vars, "OnceExCost", (oldOnce - currentCost).ToString());
    }

    private static bool HasMorningStarSeal(IDataConfig config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), SunExpIds.MorningStarSealTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), SunExpIds.MorningStarSealTag);
    }

    private static void ConsumeBuff(IStatusManager status, string buffId, int amount)
    {
        var buff = status.GetBuff(buffId);
        var level = buff?.buffConfig?.Level ?? 0;
        if (level <= amount)
        {
            status.RemoveBuff(buffId);
        }
        else if (buff?.buffConfig != null)
        {
            buff.buffConfig.Level = level - amount;
        }
    }

    private readonly struct PendingCard
    {
        public PendingCard(IDataConfig config, ScriptExecutor? executor, int preludeCost)
        {
            Config = config;
            Executor = executor;
            PreludeCost = preludeCost;
        }

        public IDataConfig Config { get; }

        public ScriptExecutor? Executor { get; }

        public int PreludeCost { get; }
    }
}
