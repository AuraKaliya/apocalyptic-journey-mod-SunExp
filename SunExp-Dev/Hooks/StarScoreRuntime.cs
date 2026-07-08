using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class StarScoreRuntime
{
    private const string PendingBlessingOvertureVar = "SunExpStarBlessingOverturePending";
    private const string PendingBlessingCostVar = "SunExpStarBlessingHalfCostPending";
    private const string PendingSealBlessingVar = "SunExpMorningStarSealBlessingGain";
    private static readonly Stack<PendingCard> Pending = new();
    private static readonly StarBlessingCostOverrideStore CostOverrides = new();
    private static bool handlerRegistered;

    public static void Initialize(ModConfig modConfig)
    {
        EnsureHandlerRegistered();
        SunExpBattleLifecycleRouter.Register("StarScore", new SunExpBattleLifecycleSubscription
        {
            FightStarted = OnFightStart
        });
        SunExpCardLifecycleRouter.Register("StarScore", new SunExpCardLifecycleSubscription
        {
            BeforeCommonCardUse = OnCardUseBefore,
            BeforeAttackCardUse = OnCardUseBefore,
            AfterCommonCardUse = OnCardUseAfter,
            AfterAttackCardUse = OnCardUseAfter
        });
        RegisterAfter(modConfig, "CommonCardItem.OnBeginDrag", OnCommonCardBeginDragAfter);
        RegisterAfter(modConfig, "CommonCardItem.OnEndDrag", OnCardSelectionEndedAfter);
        RegisterAfter(modConfig, "AttackCardItem.OnPointerDown", OnAttackCardPointerDownAfter);
        RegisterAfter(modConfig, "AttackCardItem.CancelLineMode", OnCardSelectionEndedAfter);
        RegisterAfter(modConfig, "AttackCardItem.CommitOrCancelFromKeyboard", OnCardSelectionEndedAfter);
        RegisterAfter(modConfig, "CardItem.CancelUseDrag", OnCardSelectionEndedAfter);
        RegisterBefore(modConfig, "CardItem.OnDestroy", OnCardDestroyedBefore);
        SunExpLog.Info("Star score runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "StarScore");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "StarScore");
    }

    private static void OnFightStart(ModHookContext context)
    {
        CostOverrides.CancelAll();
        Pending.Clear();
        MorningStarOvertureService.ResetForFight();
        StarScoreCombatStateStore.ClearAll();
        ExecutorApi.CombatIntSet("SunExpStarScorePlayerActionPending", 0);
        SunExpActionEventRouter.ResetForFight("StarScore.Fight_Start.Init");
    }

    private static void OnCommonCardBeginDragAfter(ModHookContext context)
    {
        TryBeginBlessingPreview(context.Target as CardItem);
    }

    private static void OnAttackCardPointerDownAfter(ModHookContext context)
    {
        if (context.Target is AttackCardItem { isLine: true } card)
        {
            TryBeginBlessingPreview(card);
        }
    }

    private static void OnCardSelectionEndedAfter(ModHookContext context)
    {
        CancelBlessingPreview(context.Target as CardItem);
    }

    private static void OnCardDestroyedBefore(ModHookContext context)
    {
        CancelBlessingPreview(context.Target as CardItem);
    }

    private static void OnCardUseBefore(ModHookContext context)
    {
        try
        {
            var card = context.Target as CardItem;
            var config = CardConfigApi.FromActionPayload(context.Target);
            if (config == null || !HasCardUseInterest(config))
            {
                return;
            }

            TryRegisterForPlayer("CardUseBefore");
            BeginCostPreviewsForSelection(card);
            EndlessAbyssGazePressureService.OnCardUseBefore(card, "StarScore.CardUseBefore");

            if (StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(config)))
            {
                CancelBlessingPreview(card);
                return;
            }

            if (DictionaryUtil.Get(config.Vars, PendingBlessingCostVar, "0") == "1")
            {
                return;
            }

            DictionaryUtil.Set(config.Vars, PendingBlessingOvertureVar, "");
            DictionaryUtil.Set(config.Vars, PendingSealBlessingVar, "");
            var player = FightPlayer.Instance?.Status;
            var hasBlessing = player != null && BuffApi.Level(player, SunExpIds.StarBlessing) > 0;
            if (!hasBlessing)
            {
                CancelBlessingPreview(card);
            }

            var actualPaidCost = CardConfigApi.CurrentCost(config);
            var sealBlessingGain = HasMorningStarSeal(config) ? actualPaidCost : 0;
            if (hasBlessing && player != null)
            {
                if (!CostOverrides.TargetCost(config).HasValue)
                {
                    var halfCost = StarBlessingHalfCost(CardConfigApi.CurrentCost(config));
                    CostOverrides.BeginPreview(config, halfCost);
                    EndlessAbyssGazePressureService.OnCardUseBefore(card, "StarScore.CardUseBefore:Fallback");
                    actualPaidCost = CardConfigApi.CurrentCost(config);
                }

                RefreshCard(card);
                DictionaryUtil.Set(config.Vars, PendingBlessingOvertureVar, "1");
                DictionaryUtil.Set(config.Vars, PendingBlessingCostVar, "1");
                ConsumeBuff(player, SunExpIds.StarBlessing, 1);
                CostOverrides.MarkBlessingConsumed(config);
                sealBlessingGain = HasMorningStarSeal(config) ? actualPaidCost : 0;
                PlayerApi.ShowCaption("\u661f\u8fb0\u795d\u798f\uff1a\u672c\u6b21\u51fa\u724c\u8017\u8d39\u51cf\u534a\u3002");
            }
            else if (player != null && actualPaidCost > 0)
            {
                var paidByResonance = ConsumeResonanceAsCost(player, config, actualPaidCost);
                if (paidByResonance > 0)
                {
                    PlayerApi.ShowCaption("\u4f59\u97f3\uff1a\u4ee3\u66ff\u6d88\u8017" + paidByResonance + "\u70b9\u9b54\u80fd\u3002");
                }
            }

            if (sealBlessingGain > 0)
            {
                DictionaryUtil.Set(config.Vars, PendingSealBlessingVar, sealBlessingGain.ToString());
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star blessing before-use hook failed", ex);
        }
    }

    private static void OnCardUseAfter(ModHookContext context)
    {
        try
        {
            var card = context.Target as CardItem;
            var config = CardConfigApi.FromActionPayload(context.Target);
            if (config == null || !CostOverrides.Contains(config))
            {
                return;
            }

            if (CostOverrides.ActionObserved(config))
            {
                CostOverrides.Commit(config);
            }
            else
            {
                var cancelled = CostOverrides.Cancel(config);
                if (cancelled.BlessingConsumed)
                {
                    RefundBlessing();
                }

                ClearPendingUse(config);
            }

            RefreshCard(card);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star blessing after-use hook failed", ex);
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
        EnsureHandlerRegistered();
        SunExpActionEventRouter.EnsureRegistered("StarScore." + source);
    }

    private static void EnsureHandlerRegistered()
    {
        if (handlerRegistered)
        {
            return;
        }

        SunExpActionEventRouter.RegisterHandler("StarScore", OnAction, OnActionAfter);
        handlerRegistered = true;
    }

    private static void OnAction(SunExpActionEventContext context)
    {
        try
        {
            var config = context.Config;
            if (config == null)
            {
                return;
            }

            CostOverrides.MarkActionObserved(config);
            MorningStarOvertureService.OnAction(config);
            var executor = config.scriptExecutor as ScriptExecutor;
            var pendingBlessingOverture = DictionaryUtil.Get(config.Vars, PendingBlessingOvertureVar, "0") == "1";
            var pendingSealBlessing = DictionaryUtil.Get(config.Vars, PendingSealBlessingVar);
            var sealBlessingGain = string.IsNullOrWhiteSpace(pendingSealBlessing)
                ? 0
                : Math.Max(0, DictionaryUtil.ParseInt(pendingSealBlessing));
            Pending.Push(new PendingCard(config, executor, pendingBlessingOverture, sealBlessingGain));
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
            if (pending.Executor != null && pending.BlessingOverturePending)
            {
                CardApi.AddCardToHand(pending.Executor, StarScoreService.RandomBlessingOvertureCardId());
                PlayerApi.ShowCaption("\u661f\u8fb0\u795d\u798f\uff1a\u83b7\u5f97\u968f\u673a\u661f\u8fb0\u5e8f\u66f2\u3002");
            }

            if (pending.Executor != null && pending.SealBlessingGain > 0)
            {
                pending.Executor.SetStatus("Self");
                pending.Executor.AddBuff(SunExpIds.StarBlessing, pending.SealBlessingGain.ToString());
                PlayerApi.ShowCaption("\u542f\u660e\u661f\uff1a\u661f\u8fb0\u795d\u798f+" + pending.SealBlessingGain);
            }

            MorningStarOvertureService.OnActionAfter(pending.Executor);

            DictionaryUtil.Set(pending.Config.Vars, PendingBlessingOvertureVar, "");
            DictionaryUtil.Set(pending.Config.Vars, PendingSealBlessingVar, "");
            DictionaryUtil.Set(pending.Config.Vars, PendingBlessingCostVar, "0");
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

    private static void TryBeginBlessingPreview(CardItem? card)
    {
        if (!HasSelectionPreviewInterest())
        {
            return;
        }

        BeginCostPreviewsForSelection(card);
    }

    private static void BeginCostPreviewsForSelection(CardItem? card)
    {
        try
        {
            var config = card?.dataConfig;
            if (config == null)
            {
                return;
            }

            var player = FightPlayer.Instance?.Status;
            var hasBlessing = player != null && BuffApi.Level(player, SunExpIds.StarBlessing) > 0;
            if (!hasBlessing && !SunExpHardTagState.Active(SunExpHardTagIds.AbyssGaze))
            {
                return;
            }

            var refreshed = false;
            if (player != null
                && !StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(config))
                && hasBlessing
                && CostOverrides.BeginPreview(config, StarBlessingHalfCost(CardConfigApi.CurrentCost(config))))
            {
                refreshed = true;
            }

            EndlessAbyssGazePressureService.BeginCostPreview(card, "StarScorePreview");
            if (refreshed)
            {
                RefreshCard(card);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card cost preview failed", ex);
        }
    }

    private static void CancelBlessingPreview(CardItem? card)
    {
        try
        {
            var config = card?.dataConfig;
            if (config == null)
            {
                return;
            }

            if (!HasSelectionPreviewInterest() && !CostOverrides.Contains(config))
            {
                return;
            }

            var refreshed = EndlessAbyssGazePressureService.CancelCostPreview(card, "StarScorePreviewCancel");
            if (!CostOverrides.Contains(config))
            {
                if (refreshed)
                {
                    RefreshCard(card);
                }

                return;
            }

            var cancelled = CostOverrides.Cancel(config);
            if (cancelled.BlessingConsumed)
            {
                RefundBlessing();
                ClearPendingUse(config);
            }

            RefreshCard(card);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star blessing preview rollback failed", ex);
        }
    }

    private static void ClearPendingUse(IDataConfig config)
    {
        DictionaryUtil.Set(config.Vars, PendingBlessingOvertureVar, "");
        DictionaryUtil.Set(config.Vars, PendingSealBlessingVar, "");
        DictionaryUtil.Set(config.Vars, PendingBlessingCostVar, "0");
    }

    private static void RefundBlessing()
    {
        var player = FightPlayer.Instance?.Status;
        if (player != null)
        {
            BuffApi.SetExactLevel(
                player,
                SunExpIds.StarBlessing,
                BuffApi.Level(player, SunExpIds.StarBlessing) + 1);
        }
    }

    private static void RefreshCard(CardItem? card)
    {
        SunExpCardRefreshQueue.RequestDataUpdate(card, "StarScore");
    }

    private static int StarBlessingHalfCost(int currentCost)
    {
        var cost = Math.Max(0, currentCost);
        return (cost + 1) / 2;
    }

    private static int ConsumeResonanceAsCost(IStatusManager status, IDataConfig config, int currentCost)
    {
        var resonance = Math.Max(0, BuffApi.Level(status, SunExpIds.Resonance));
        var consumed = Math.Min(Math.Max(0, currentCost), resonance);
        if (consumed <= 0)
        {
            return 0;
        }

        CardMutationService.AdjustOnceCost(config, -consumed);
        ConsumeBuff(status, SunExpIds.Resonance, consumed);
        return consumed;
    }

    private static bool HasMorningStarSeal(IDataConfig config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), SunExpIds.MorningStarSealTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), SunExpIds.MorningStarSealTag);
    }

    private static bool HasSelectionPreviewInterest()
    {
        var player = FightPlayer.Instance?.Status;
        return (player != null && BuffApi.Level(player, SunExpIds.StarBlessing) > 0)
            || SunExpHardTagState.Active(SunExpHardTagIds.AbyssGaze);
    }

    private static bool HasCardUseInterest(IDataConfig config)
    {
        var player = FightPlayer.Instance?.Status;
        return StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(config))
            || HasMorningStarSeal(config)
            || SunExpHardTagState.Active(SunExpHardTagIds.AbyssGaze)
            || (player != null
                && (BuffApi.Level(player, SunExpIds.StarBlessing) > 0
                    || BuffApi.Level(player, SunExpIds.Resonance) > 0));
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
        public PendingCard(IDataConfig config, ScriptExecutor? executor, bool blessingOverturePending, int sealBlessingGain)
        {
            Config = config;
            Executor = executor;
            BlessingOverturePending = blessingOverturePending;
            SealBlessingGain = sealBlessingGain;
        }

        public IDataConfig Config { get; }

        public ScriptExecutor? Executor { get; }

        public bool BlessingOverturePending { get; }

        public int SealBlessingGain { get; }
    }
}
