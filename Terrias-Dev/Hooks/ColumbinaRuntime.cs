using System;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class ColumbinaRuntime
{
    private static readonly ColumbinaCardGainLedger CardGains = new();

    public static void Initialize(ModConfig modConfig)
    {
        TerriasStatusLifecycleRouter.Register("ConstellationPresentation", new TerriasStatusLifecycleSubscription
        {
            BeforeBuffItemInit = OnBuffItemInitializing
        });
        AuraCardActionTransactionRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "Columbina",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.Committed,
                Handler = _ => OnActionAfter()
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
        TerriasBattleLifecycleRouter.Register("Columbina", new TerriasBattleLifecycleSubscription
        {
            BattleOpening = _ => OnFightStarted(),
            PlayerRoundReady = _ => OnPlayerRoundStarted(),
            BattleEnded = _ => OnFightEnded()
        });
        TerriasCardLifecycleRouter.Register("Columbina.CardGain", new TerriasCardLifecycleSubscription
        {
            AfterFightUiCreateCardItemInternal = OnCardMaterialized
        });
    }

    private static void OnFightStarted()
    {
        CardGains.Clear();

        if (!ConstellationService.BeginBattle())
        {
            return;
        }

        ColumbinaBattleStateService.BeginBattle();
        ConstellationService.RestoreLocalForBattle("ColumbinaRuntime.BattleOpening");
        ConstellationService.SynchronizeBattleState("ColumbinaRuntime.BattleOpening");
    }

    private static void OnFightEnded()
    {
        CardGains.Clear();
        ConstellationService.EndBattle();
        ColumbinaBattleStateService.EndBattle();
    }

    private static void OnBuffItemInitializing(ModHookContext context)
    {
        var arguments = context.Arguments ?? Array.Empty<object>();
        var config = arguments.Length > 0 ? arguments[0] as IBuffItemConfig : null;
        var status = arguments.Length > 1 ? arguments[1] as IStatusManager : config?.status;
        ConstellationService.PreparePresentation(config, status, "BuffItem.Init:before");
    }

    private static void OnPlayerRoundStarted()
    {
        if (ColumbinaPassiveService.IsActive(FightPlayer.Instance?.Status))
        {
            ReduceCooldown(TerriasIds.ColumbinaEternalTideCardId, 1);
            ReduceCooldown(TerriasIds.ColumbinaHomesicknessCardId, 1);
        }

        ConstellationService.ResolveLocalRoundStart();
    }

    private static void OnActionAfter()
    {
        ColumbinaMechanics.ResolveActionAfter(FightPlayer.Instance?.Status);
    }

    private static void OnCardMaterialized(ModHookContext context)
    {
        if (context.Target is not FightUI || FightPlayer.Instance?.Status == null) return;
        var config = FirstConfig(context);
        var card = config == null ? null : FightUI.cardItemList?.FirstOrDefault(
            item => item != null && ReferenceEquals(item.dataConfig, config));
        if (card != null && CardGains.TryRecord(card))
        {
            ReduceHomesicknessForCards(1);
        }
    }

    private static IDataConfig? FirstConfig(ModHookContext context)
    {
        foreach (var argument in context.Arguments ?? Array.Empty<object>())
        {
            if (argument is IDataConfig config)
            {
                return config;
            }
        }

        return null;
    }

    private static void ReduceHomesicknessForCards(int count)
    {
        var status = FightPlayer.Instance?.Status;
        if (!ColumbinaPassiveService.IsActive(status))
        {
            return;
        }

        ReduceCooldown(TerriasIds.ColumbinaHomesicknessCardId, Math.Max(1, count));
    }

    private static void ReduceCooldown(string skillId, int amount)
    {
        var current = PlayerApi.GetSkillTime(skillId);
        if (current > 0 && amount > 0)
        {
            PlayerApi.SetSkillTime(skillId, Math.Max(0, current - amount));
            PlayerApi.RefreshSkillDisplay();
        }
    }
}
