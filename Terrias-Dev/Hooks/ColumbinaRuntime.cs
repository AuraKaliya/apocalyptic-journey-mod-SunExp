using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class ColumbinaRuntime
{
    private static readonly object CardGainSync = new();
    private static readonly HashSet<string> SeenCardGains = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.Before(
            modConfig,
            "BuffItem.Init",
            OnBuffItemInitializing,
            "ConstellationPresentation");
        TerriasActionEventRouter.RegisterHandler("Columbina", null, OnActionAfter);
        TerriasBattleLifecycleRouter.Register("Columbina", new TerriasBattleLifecycleSubscription
        {
            FightStarted = _ => OnFightStarted(),
            PlayerRoundStarted = _ => OnPlayerRoundStarted(),
            FightEnded = _ => OnFightEnded()
        });
        TerriasCardLifecycleRouter.Register("Columbina.CardGain", new TerriasCardLifecycleSubscription
        {
            AfterScriptExecutorGetCardFromDeck = OnCardsGained,
            AfterScriptExecutorRandomAddCard = OnCardGained,
            AfterFightUiCreateCardItemInternal = OnCardMaterialized
        });
    }

    private static void OnFightStarted()
    {
        lock (CardGainSync)
        {
            SeenCardGains.Clear();
        }

        if (!ConstellationService.BeginBattle())
        {
            return;
        }

        TerriasActionEventRouter.EnsureRegistered("Columbina.FightStarted");
        ColumbinaBattleStateService.BeginBattle();
        ConstellationService.RestoreLocalForBattle("ColumbinaRuntime.FightStarted");
        ConstellationService.SynchronizeBattleState("ColumbinaRuntime.FightStarted");
    }

    private static void OnFightEnded()
    {
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

    private static void OnCardsGained(ModHookContext context)
    {
        if (!IsLocalCardOwner(context))
        {
            return;
        }

        var config = FirstConfig(context);
        if (config == null || MarkCardGain(config))
        {
            ReduceHomesicknessForCards(1);
        }
    }

    private static void OnCardGained(ModHookContext context)
    {
        if (IsLocalCardOwner(context))
        {
            ReduceHomesicknessForCards(1);
        }
    }

    private static void OnCardMaterialized(ModHookContext context)
    {
        var config = FirstConfig(context);
        if (config != null && MarkCardGain(config))
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

    private static bool MarkCardGain(IDataConfig config)
    {
        var key = config.InstanceID;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = CardConfigApi.Id(config) + ":" + RuntimeHelpers.GetHashCode(config);
        }

        lock (CardGainSync)
        {
            return SeenCardGains.Add(key);
        }
    }

    private static bool IsLocalCardOwner(ModHookContext context)
    {
        var owner = (context.Target as ScriptExecutor)?.Self;
        var local = FightPlayer.Instance?.Status;
        return owner != null && local != null
            && (ReferenceEquals(owner, local)
                || string.Equals(owner.InstanceId, local.InstanceId, StringComparison.Ordinal));
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
        }
    }
}
