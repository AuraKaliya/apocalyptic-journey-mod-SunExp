using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SpecialTagRuntime
{
    private static readonly Dictionary<string, PendingCard> Pending = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        AuraCardActionTransactionRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "SpecialTag.WhiteRadiance",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.NativeStarted
                         | AuraCardActionPhase.Committed
                         | AuraCardActionPhase.Aborted,
                Handler = OnCardAction
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
        AuraBattleLifecycleRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "SpecialTag",
            new AuraBattleLifecycleSubscription
            {
                FightInitializing = _ => ResetForFight("FightInit.Init")
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
        TerriasLog.Info("SpecialTag runtime initialized");
    }

    private static void ResetForFight(string source)
    {
        try
        {
            RuntimeCardAttachmentService.ClearTemporaryAttachments(source);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Failed to clear runtime card attachments from Fight_Start.Init", ex);
        }

        Pending.Clear();
    }

    private static void OnCardAction(AuraCardActionContext context)
    {
        if (context.Phase == AuraCardActionPhase.NativeStarted)
        {
            Capture(context);
            return;
        }

        if (context.Phase == AuraCardActionPhase.Committed)
        {
            Resolve(context.TransactionId);
            return;
        }

        Pending.Remove(context.TransactionId);
    }

    private static void Capture(AuraCardActionContext context)
    {
        try
        {
            var config = context.Config;
            if (config == null)
            {
                TerriasLog.Debug("Action skipped: payload has no IDataConfig");
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
            var cost = context.StartCost;
            Pending[context.TransactionId] = new PendingCard(config, cost, kind);
            TerriasLog.Debug("White radiance captured: kind=" + kind + ", id=" + CardConfigApi.Id(config) + ", cost=" + cost + ", instance=" + config.InstanceID);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Action listener failed", ex);
        }
    }

    private static void Resolve(string transactionId)
    {
        try
        {
            if (!Pending.TryGetValue(transactionId, out var pending))
            {
                return;
            }

            Pending.Remove(transactionId);
            if (pending.Kind == "temporary" && !CardConfigApi.TryClaimTemporaryWhiteRadiance(pending.Config))
            {
                TerriasLog.Debug("Temp white radiance skipped: already resolved, id=" + CardConfigApi.Id(pending.Config));
                return;
            }

            var executor = pending.Config.scriptExecutor as ScriptExecutor;
            if (executor == null)
            {
                TerriasLog.Warn("Temp white radiance skipped: executor missing, id=" + CardConfigApi.Id(pending.Config));
                return;
            }

            var cost = CardConfigApi.ResolveSolarTriggerCost(pending.Config, pending.Cost);
            CardConfigApi.ClearSolarTriggerCost(pending.Config);
            SolarRadianceService.HandleSolarCardUsed(executor, cost, "ActionAfter." + pending.Kind);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("ActionAfter listener failed", ex);
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
