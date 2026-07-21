using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class MorningStarDimmedService
{
    public const string CostMarker = "TerriasHard_MorningStarDimmedCostApplied";
    private const string FeatureId = "MorningStarDimmed";

    public static void OnFightStarted(ScriptExecutor? executor, string source)
    {
        if (!Active())
        {
            return;
        }

        TerriasLifecycleStepRunner.RunBattleOnce(
            FeatureId,
            "FightInitialized",
            new[]
            {
                new AuraSharedFrameStep
                {
                    Name = "MaxPower",
                    Phase = AuraSharedFramePhase.CriticalLifecycle,
                    EstimatedCost = 1,
                    Work = () =>
                    {
                        ApplyMaxPowerOnce(executor ?? CurrentPlayerExecutor(), source);
                        return AuraSharedFrameStepResult.Complete;
                    }
                },
                new AuraSharedFrameStep
                {
                    Name = "CombatCards",
                    EstimatedCost = 4,
                    Work = () =>
                    {
                        ApplyToCombatCards(executor ?? CurrentPlayerExecutor(), source + ":fight-start");
                        return AuraSharedFrameStepResult.Complete;
                    }
                }
            },
            AuraSharedFramePhase.GameplayMutation,
            priority: 10,
            estimatedCost: 3);
    }

    public static int ApplyToCombatCards(ScriptExecutor? executor, string source)
    {
        if (!Active())
        {
            return 0;
        }

        var changed = 0;
        var snapshot = AuraCombatCardZoneSnapshot.Capture(executor, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true,
            IncludeExecutorHand = executor != null,
            IncludeExecutorWait = executor != null,
            IncludeExecutorDeck = executor != null,
            IncludeExecutorUsed = executor != null,
            IncludeManagerDraw = true,
            IncludeManagerUsed = true
        });

        foreach (var reference in snapshot.Cards)
        {
            var referenceSource = source + ":" + SourceSuffix(reference.Zone);
            if (reference.Card != null)
            {
                if (ApplyToCard(reference.Card, referenceSource))
                {
                    changed++;
                }

                continue;
            }

            if (ApplyToConfig(reference.Config, referenceSource))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            TerriasLog.Debug("[MorningStarDimmed] applied cost +1 to " + changed + " cards from " + source + ".");
        }

        return changed;
    }

    public static bool ApplyToCard(CardItem? card, string source)
    {
        if (card?.dataConfig == null || !ApplyToConfig(card.dataConfig, source))
        {
            return false;
        }

        TerriasCardRefreshQueue.RequestCostUpdate(card, "MorningStarDimmed:" + source);
        return true;
    }

    public static bool ApplyToConfig(IDataConfig? config, string source)
    {
        if (config == null || DictionaryUtil.Get(config.Vars, CostMarker, "0") == "1")
        {
            return false;
        }

        var current = DictionaryUtil.GetInt(config.Vars, "TotalExCost");
        DictionaryUtil.Set(config.Vars, "TotalExCost", (current + 1).ToString());
        DictionaryUtil.Set(config.Vars, CostMarker, "1");
        TerriasLog.Debug("[MorningStarDimmed] cost +1 card="
            + CardConfigApi.Id(config)
            + " from "
            + source
            + ".");
        return true;
    }

    private static void ApplyMaxPowerOnce(ScriptExecutor? executor, string source)
    {
        if (!Active() || executor == null)
        {
            return;
        }

        var statusId = executor.Self?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "local";
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                TerriasIds.ModId,
                FeatureId,
                "MaxPower",
                statusId,
                "Power",
                "MaxPower+1"))
        {
            TerriasLog.Debug("[MorningStarDimmed] max power already applied for " + statusId + " from " + source + ".");
            return;
        }

        if (PlayerPowerApi.TryChangeMaxPower(1))
        {
            TerriasLog.Info("[MorningStarDimmed] max power +1 applied for " + statusId + " from " + source + ".");
            return;
        }

        TerriasLog.Warn("[MorningStarDimmed] max power +1 could not be applied for "
            + statusId
            + " from "
            + source
            + ".");
    }

    private static bool Active()
    {
        return TerriasHardTagState.Active(TerriasHardTagIds.MorningStarDimmed);
    }

    private static ScriptExecutor? CurrentPlayerExecutor()
    {
        return FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
    }

    private static string SourceSuffix(AuraCombatCardZoneKind zone)
    {
        return zone switch
        {
            AuraCombatCardZoneKind.FightUiActive => "fight-ui",
            AuraCombatCardZoneKind.FightUiWait => "wait-ui",
            AuraCombatCardZoneKind.ExecutorHand => "hand",
            AuraCombatCardZoneKind.ExecutorWait => "wait",
            AuraCombatCardZoneKind.ExecutorDeck => "deck",
            AuraCombatCardZoneKind.ExecutorUsed => "used",
            AuraCombatCardZoneKind.ManagerDraw => "draw",
            AuraCombatCardZoneKind.ManagerUsed => "discard",
            _ => "combat"
        };
    }
}
