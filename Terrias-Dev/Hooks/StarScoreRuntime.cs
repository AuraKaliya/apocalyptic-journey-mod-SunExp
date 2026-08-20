using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class StarScoreRuntime
{
    private const string PendingBlessingOvertureVar = "TerriasStarBlessingOverturePending";
    private const string PendingBlessingCostVar = "TerriasStarBlessingHalfCostPending";
    private const string PendingSealBlessingVar = "TerriasMorningStarSealBlessingGain";
    private const string PendingSolarFlameVar = "TerriasSolarFlameSealGain";
    private static readonly Dictionary<string, PendingCard> Pending = new(StringComparer.Ordinal);
    private static readonly StarBlessingCostOverrideStore CostOverrides = new();
    private static readonly ResonanceCostTransactionStore ResonanceCostTransactions = new();
    private static readonly Dictionary<string, string> LastRefreshSignatures = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        AuraCardActionTransactionRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "StarScore",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.NativeStarted
                         | AuraCardActionPhase.Committed
                         | AuraCardActionPhase.Aborted,
                Handler = OnCardAction
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
        TerriasBattleLifecycleRouter.Register("StarScore", new TerriasBattleLifecycleSubscription
        {
            FightStarted = OnFightStart
        });
        TerriasCardLifecycleRouter.Register("StarScore", new TerriasCardLifecycleSubscription
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
        TerriasLog.Info("Star score runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "StarScore");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "StarScore");
    }

    private static void OnFightStart(ModHookContext context)
    {
        CostOverrides.CancelAll();
        ResonanceCostTransactions.CancelAll();
        LastRefreshSignatures.Clear();
        Pending.Clear();
        MorningStarOvertureService.ResetForFight();
        StarScoreCombatStateStore.ClearAll();
        ExecutorApi.CombatIntSet("TerriasStarScorePlayerActionPending", 0);
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
        var card = context.Target as CardItem;
        CancelBlessingPreview(card);
        CancelResonancePayment(card, "SelectionEnded");
    }

    private static void OnCardDestroyedBefore(ModHookContext context)
    {
        var card = context.Target as CardItem;
        CancelBlessingPreview(card);
        CancelResonancePayment(card, "CardDestroyed");
        ForgetRefreshSignature(card);
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

            BeginCostPreviewsForSelection(card);
            EndlessAbyssGazePressureService.OnCardUseBefore(card, "StarScore.CardUseBefore");

            if (StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(config)))
            {
                CancelBlessingPreview(card);
                ClearPendingUse(config);
                if (HasSolarFlameSeal(config))
                {
                    DictionaryUtil.Set(
                        config.Vars,
                        PendingSolarFlameVar,
                        SolarFlameSealFormula.GatheredFlameGain(CardConfigApi.CurrentCost(config)).ToString());
                }

                return;
            }

            if (DictionaryUtil.Get(config.Vars, PendingBlessingCostVar, "0") == "1")
            {
                return;
            }

            DictionaryUtil.Set(config.Vars, PendingBlessingOvertureVar, "");
            DictionaryUtil.Set(config.Vars, PendingSealBlessingVar, "");
            DictionaryUtil.Set(config.Vars, PendingSolarFlameVar, "");
            var player = FightPlayer.Instance?.Status;
            var hasBlessing = player != null && BuffApi.Level(player, TerriasIds.StarBlessing) > 0;
            if (!hasBlessing)
            {
                CancelBlessingPreview(card);
            }

            var actualPaidCost = CardConfigApi.CurrentCost(config);
            var sealBlessingGain = HasMorningStarSeal(config) ? actualPaidCost : 0;
            var solarFlameGain = HasSolarFlameSeal(config)
                ? SolarFlameSealFormula.GatheredFlameGain(actualPaidCost)
                : 0;
            if (hasBlessing && player != null)
            {
                if (!CostOverrides.TargetCost(config).HasValue)
                {
                    var halfCost = StarBlessingHalfCost(CardConfigApi.CurrentCost(config));
                    CostOverrides.BeginPreview(config, halfCost);
                    EndlessAbyssGazePressureService.OnCardUseBefore(card, "StarScore.CardUseBefore:Fallback");
                    actualPaidCost = CardConfigApi.CurrentCost(config);
                }

                RefreshCard(card, "BeforeUse");
                DictionaryUtil.Set(config.Vars, PendingBlessingOvertureVar, "1");
                DictionaryUtil.Set(config.Vars, PendingBlessingCostVar, "1");
                ConsumeBuff(player, TerriasIds.StarBlessing, 1);
                CostOverrides.MarkBlessingConsumed(config);
                sealBlessingGain = HasMorningStarSeal(config) ? actualPaidCost : 0;
                solarFlameGain = HasSolarFlameSeal(config)
                    ? SolarFlameSealFormula.GatheredFlameGain(actualPaidCost)
                    : 0;
                PlayerApi.ShowCaption("\u661f\u8fb0\u795d\u798f\uff1a\u672c\u6b21\u51fa\u724c\u8017\u8d39\u51cf\u534a\u3002");
            }
            else if (player != null && actualPaidCost > 0)
            {
                var paidByResonance = BeginResonancePayment(player, config, actualPaidCost);
                if (paidByResonance > 0)
                {
                    PlayerApi.ShowCaption("\u4f59\u97f3\uff1a\u4ee3\u66ff\u6d88\u8017" + paidByResonance + "\u70b9\u9b54\u80fd\u3002");
                }
            }

            if (sealBlessingGain > 0)
            {
                DictionaryUtil.Set(config.Vars, PendingSealBlessingVar, sealBlessingGain.ToString());
            }

            if (solarFlameGain > 0)
            {
                DictionaryUtil.Set(config.Vars, PendingSolarFlameVar, solarFlameGain.ToString());
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star blessing before-use hook failed", ex);
        }
    }

    private static void OnCardUseAfter(ModHookContext context)
    {
        try
        {
            var card = context.Target as CardItem;
            var config = CardConfigApi.FromActionPayload(context.Target);
            var hasPendingSealEffect = config != null
                && (!string.IsNullOrWhiteSpace(DictionaryUtil.Get(config.Vars, PendingSealBlessingVar))
                    || !string.IsNullOrWhiteSpace(DictionaryUtil.Get(config.Vars, PendingSolarFlameVar)));
            if (config == null
                || (!CostOverrides.Contains(config)
                    && !ResonanceCostTransactions.Contains(config)
                    && !hasPendingSealEffect))
            {
                return;
            }

            if (CostOverrides.Contains(config))
            {
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
            }

            if (ResonanceCostTransactions.Contains(config))
            {
                if (ResonanceCostTransactions.ActionObserved(config))
                {
                    ResonanceCostTransactions.Commit(config);
                }
                else
                {
                    RefundResonance(ResonanceCostTransactions.Cancel(config), "CardUseAfterWithoutAction");
                }
            }

            if (hasPendingSealEffect)
            {
                ClearPendingUse(config);
            }

            RefreshCard(card, "AfterUse");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star blessing after-use hook failed", ex);
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
            TerriasLog.Error("Resonance add-buff hook failed", ex);
        }
    }

    private static void OnCardAction(AuraCardActionContext context)
    {
        if (context.Phase == AuraCardActionPhase.NativeStarted)
        {
            OnAction(context);
            return;
        }

        if (context.Phase == AuraCardActionPhase.Committed)
        {
            OnActionAfter(context.TransactionId);
            return;
        }

        AbortAction(context.TransactionId);
    }

    private static void OnAction(AuraCardActionContext context)
    {
        try
        {
            var config = context.Config;
            if (config == null)
            {
                return;
            }

            CostOverrides.MarkActionObserved(config);
            ResonanceCostTransactions.MarkActionObserved(config);
            var executor = config.scriptExecutor as ScriptExecutor;
            var pendingBlessingOverture = DictionaryUtil.Get(config.Vars, PendingBlessingOvertureVar, "0") == "1";
            var pendingSealBlessing = DictionaryUtil.Get(config.Vars, PendingSealBlessingVar);
            var sealBlessingGain = string.IsNullOrWhiteSpace(pendingSealBlessing)
                ? 0
                : Math.Max(0, DictionaryUtil.ParseInt(pendingSealBlessing));
            var pendingSolarFlame = DictionaryUtil.Get(config.Vars, PendingSolarFlameVar);
            var solarFlameGain = string.IsNullOrWhiteSpace(pendingSolarFlame)
                ? 0
                : Math.Max(0, DictionaryUtil.ParseInt(pendingSolarFlame));
            Pending[context.TransactionId] = new PendingCard(
                config,
                executor,
                context.StartCost,
                pendingBlessingOverture,
                sealBlessingGain,
                solarFlameGain);
            ExecutorApi.CombatIntAdd("TerriasStarScorePlayerActionPending", 1);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star score Action listener failed", ex);
        }
    }

    private static void OnActionAfter(string transactionId)
    {
        try
        {
            if (!Pending.TryGetValue(transactionId, out var pending))
            {
                return;
            }

            Pending.Remove(transactionId);
            if (pending.Executor != null && pending.BlessingOverturePending)
            {
                CardApi.AddCardToHand(pending.Executor, StarScoreService.RandomBlessingOvertureCardId());
                PlayerApi.ShowCaption("\u661f\u8fb0\u795d\u798f\uff1a\u83b7\u5f97\u968f\u673a\u661f\u8fb0\u5e8f\u66f2\u3002");
            }

            if (pending.Executor != null && pending.SealBlessingGain > 0)
            {
                pending.Executor.SetStatus("Self");
                pending.Executor.AddBuff(TerriasIds.StarBlessing, pending.SealBlessingGain.ToString());
                PlayerApi.ShowCaption("\u542f\u660e\u661f\uff1a\u661f\u8fb0\u795d\u798f+" + pending.SealBlessingGain);
            }

            if (pending.Executor != null && pending.SolarFlameGain > 0)
            {
                pending.Executor.SetStatus("Self");
                pending.Executor.AddBuff(TerriasIds.GatheredFlame, pending.SolarFlameGain.ToString());
                PlayerApi.ShowCaption("阳炣：聚焰+" + pending.SolarFlameGain);
            }

            MorningStarOvertureService.OnActionCommitted(
                pending.Config,
                pending.Executor,
                pending.StartCost);

            DictionaryUtil.Set(pending.Config.Vars, PendingBlessingOvertureVar, "");
            DictionaryUtil.Set(pending.Config.Vars, PendingSealBlessingVar, "");
            DictionaryUtil.Set(pending.Config.Vars, PendingSolarFlameVar, "");
            DictionaryUtil.Set(pending.Config.Vars, PendingBlessingCostVar, "0");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star score ActionAfter listener failed", ex);
        }
        finally
        {
            ExecutorApi.CombatIntSet("TerriasStarScorePlayerActionPending", Math.Max(0, ExecutorApi.CombatIntGet("TerriasStarScorePlayerActionPending") - 1));
        }
    }

    private static void AbortAction(string transactionId)
    {
        if (!Pending.TryGetValue(transactionId, out var pending))
        {
            return;
        }

        Pending.Remove(transactionId);
        CostOverrides.Cancel(pending.Config);
        ResonanceCostTransactions.Cancel(pending.Config);
        DictionaryUtil.Set(pending.Config.Vars, PendingBlessingOvertureVar, "");
        DictionaryUtil.Set(pending.Config.Vars, PendingSealBlessingVar, "");
        DictionaryUtil.Set(pending.Config.Vars, PendingSolarFlameVar, "");
        DictionaryUtil.Set(pending.Config.Vars, PendingBlessingCostVar, "0");
        ExecutorApi.CombatIntSet(
            "TerriasStarScorePlayerActionPending",
            Math.Max(0, ExecutorApi.CombatIntGet("TerriasStarScorePlayerActionPending") - 1));
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
            var hasBlessing = player != null && BuffApi.Level(player, TerriasIds.StarBlessing) > 0;
            if (!hasBlessing && !TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze))
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
                RefreshCard(card, "PreviewBegin");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Card cost preview failed", ex);
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
                    RefreshCard(card, "PreviewCancelGaze");
                }

                return;
            }

            var cancelled = CostOverrides.Cancel(config);
            if (cancelled.BlessingConsumed)
            {
                RefundBlessing();
                ClearPendingUse(config);
            }

            RefreshCard(card, "PreviewCancel");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star blessing preview rollback failed", ex);
        }
    }

    private static void ClearPendingUse(IDataConfig config)
    {
        DictionaryUtil.Set(config.Vars, PendingBlessingOvertureVar, "");
        DictionaryUtil.Set(config.Vars, PendingSealBlessingVar, "");
        DictionaryUtil.Set(config.Vars, PendingSolarFlameVar, "");
        DictionaryUtil.Set(config.Vars, PendingBlessingCostVar, "0");
    }

    private static void RefundBlessing()
    {
        var player = FightPlayer.Instance?.Status;
        if (player != null)
        {
            BuffApi.SetExactLevel(
                player,
                TerriasIds.StarBlessing,
                BuffApi.Level(player, TerriasIds.StarBlessing) + 1);
        }
    }

    private static void RefreshCard(CardItem? card, string reason)
    {
        var config = card?.dataConfig;
        if (card == null || config == null)
        {
            return;
        }

        var key = RefreshKey(card, config);
        var signature = RefreshSignature(config);
        if (key.Length > 0
            && LastRefreshSignatures.TryGetValue(key, out var previous)
            && string.Equals(previous, signature, StringComparison.Ordinal))
        {
            TerriasPerformanceCounters.Record("StarScore.RefreshSignatureSkip");
            return;
        }

        if (key.Length > 0)
        {
            LastRefreshSignatures[key] = signature;
        }

        TerriasPerformanceCounters.Record("StarScore.RefreshRequested");
        TerriasCardRefreshQueue.RequestCostUpdate(
            card,
            "StarScore:" + reason + ":" + CardConfigApi.Id(config));
    }

    private static void ForgetRefreshSignature(CardItem? card)
    {
        var config = card?.dataConfig;
        if (card == null || config == null)
        {
            return;
        }

        var key = RefreshKey(card, config);
        if (key.Length > 0)
        {
            LastRefreshSignatures.Remove(key);
        }
    }

    private static string RefreshKey(CardItem card, IDataConfig config)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(config.InstanceID))
            {
                return config.InstanceID;
            }
        }
        catch
        {
            // Fall back to the Unity card instance below.
        }

        try
        {
            return card.GetInstanceID().ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string RefreshSignature(IDataConfig config)
    {
        var player = FightPlayer.Instance?.Status;
        return CardConfigApi.Id(config)
            + "\u001f" + CardConfigApi.CurrentCost(config)
            + "\u001f" + (CostOverrides.Contains(config) ? "preview" : "normal")
            + "\u001f" + (CostOverrides.TargetCost(config)?.ToString() ?? "none")
            + "\u001f" + DictionaryUtil.Get(config.Vars, PendingBlessingCostVar, "0")
            + "\u001f" + DictionaryUtil.Get(config.Vars, "OnceExCost")
            + "\u001f" + DictionaryUtil.Get(config.Vars, "TotalExCost")
            + "\u001f" + (player == null ? 0 : BuffApi.Level(player, TerriasIds.StarBlessing))
            + "\u001f" + (TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze) ? "gaze" : "normal");
    }

    private static int StarBlessingHalfCost(int currentCost)
    {
        var cost = Math.Max(0, currentCost);
        return (cost + 1) / 2;
    }

    private static int BeginResonancePayment(IStatusManager status, IDataConfig config, int currentCost)
    {
        var resonance = Math.Max(0, BuffApi.Level(status, TerriasIds.Resonance));
        var consumed = Math.Min(Math.Max(0, currentCost), resonance);
        if (consumed <= 0)
        {
            return 0;
        }

        var transaction = ResonanceCostTransactions.Begin(status, config, consumed);
        if (!transaction.Found)
        {
            return 0;
        }

        try
        {
            BuffApi.SetExactLevel(status, TerriasIds.Resonance, resonance - transaction.ResonancePaid);
            ResonanceCostTransactions.MarkPaymentApplied(config);
            return transaction.ResonancePaid;
        }
        catch
        {
            ResonanceCostTransactions.Cancel(config);
            try
            {
                BuffApi.SetExactLevel(status, TerriasIds.Resonance, resonance);
            }
            catch (Exception rollbackEx)
            {
                TerriasLog.Error("Resonance payment rollback failed", rollbackEx);
            }

            throw;
        }
    }

    private static void CancelResonancePayment(CardItem? card, string reason)
    {
        var config = card?.dataConfig;
        if (config == null || !ResonanceCostTransactions.Contains(config))
        {
            return;
        }

        RefundResonance(ResonanceCostTransactions.Cancel(config), reason);
        RefreshCard(card, "ResonanceRollback:" + reason);
    }

    private static void RefundResonance(ResonanceCostTransactionResult transaction, string reason)
    {
        if (!transaction.Found
            || !transaction.PaymentApplied
            || transaction.Owner == null
            || transaction.ResonancePaid <= 0)
        {
            return;
        }

        BuffApi.SetExactLevel(
            transaction.Owner,
            TerriasIds.Resonance,
            BuffApi.Level(transaction.Owner, TerriasIds.Resonance) + transaction.ResonancePaid);
        TerriasLog.Debug("[StarScore] refunded " + transaction.ResonancePaid + " Resonance from " + reason + ".");
    }

    private static bool HasMorningStarSeal(IDataConfig config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), TerriasIds.MorningStarSealTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), TerriasIds.MorningStarSealTag);
    }

    private static bool HasSolarFlameSeal(IDataConfig config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), TerriasIds.SolarFlameSealTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), TerriasIds.SolarFlameSealTag);
    }

    private static bool HasSelectionPreviewInterest()
    {
        var player = FightPlayer.Instance?.Status;
        return (player != null && BuffApi.Level(player, TerriasIds.StarBlessing) > 0)
            || TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze);
    }

    private static bool HasCardUseInterest(IDataConfig config)
    {
        var player = FightPlayer.Instance?.Status;
        return StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(config))
            || HasMorningStarSeal(config)
            || HasSolarFlameSeal(config)
            || TerriasHardTagState.Active(TerriasHardTagIds.AbyssGaze)
            || (player != null
                && (BuffApi.Level(player, TerriasIds.StarBlessing) > 0
                    || BuffApi.Level(player, TerriasIds.Resonance) > 0));
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
        public PendingCard(
            IDataConfig config,
            ScriptExecutor? executor,
            int startCost,
            bool blessingOverturePending,
            int sealBlessingGain,
            int solarFlameGain)
        {
            Config = config;
            Executor = executor;
            StartCost = startCost;
            BlessingOverturePending = blessingOverturePending;
            SealBlessingGain = sealBlessingGain;
            SolarFlameGain = solarFlameGain;
        }

        public IDataConfig Config { get; }

        public ScriptExecutor? Executor { get; }

        public int StartCost { get; }

        public bool BlessingOverturePending { get; }

        public int SealBlessingGain { get; }

        public int SolarFlameGain { get; }
    }
}
