using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class DuskPartnerRuntime
{
    private const string PartnerLocalId = "dusk";
    private const string PartnerFullId = "Terrias_terrias_dusk";
    private const string BlessingLocalId = "dusk_afterheat_recovery";
    private const string BlessingFullId = "Terrias_terrias_dusk_afterheat_recovery";

    public static void Initialize(ModConfig modConfig)
    {
        DuskAfterheatRecoveryService.Initialize();
        RegisterAfter(modConfig, "GameEntryUI.CheckCareer", CleanupPlaceholderBlessing);
        TerriasBattleLifecycleRouter.Register("DuskPartner", new TerriasBattleLifecycleSubscription
        {
            FightStarted = GrantTraitOnFightStart,
            FightRestarting = _ => DuskAfterheatRecoveryService.Deactivate(null, "FightRestarting"),
            FightEnding = _ => DuskAfterheatRecoveryService.Deactivate(null, "FightEnding")
        });
        TerriasStatusLifecycleRouter.Register("DuskPartner", new TerriasStatusLifecycleSubscription
        {
            AfterAddBuff = ObserveBurnAfterAdd,
            AfterEnemyInit = ObserveEnemyAfterInit
        });
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "DuskPartner");
    }

    private static void CleanupPlaceholderBlessing(ModHookContext context)
    {
        try
        {
            PartnerApi.RemovePlaceholderBlessing(BlessingLocalId, BlessingFullId);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Dusk placeholder blessing cleanup failed", ex);
        }
    }

    private static void GrantTraitOnFightStart(ModHookContext context)
    {
        try
        {
            PartnerApi.RemovePlaceholderBlessing(BlessingLocalId, BlessingFullId);
            var status = FightPlayer.Instance?.Status;
            if (status == null || !PartnerApi.IsCurrentPartner(PartnerLocalId, PartnerFullId))
            {
                return;
            }

            if (status.GetBuff(TerriasIds.DuskAfterheatRecoveryTrait) == null)
            {
                DuskAfterheatRecoveryService.Deactivate(null, "FightStarted.NewStatus");
                status.AddBuff(TerriasIds.DuskAfterheatRecoveryTrait, 1);
                TerriasLog.Info("Granted Dusk afterheat recovery trait: owner=" + status.InstanceId);
            }

            DuskAfterheatRecoveryService.EnsureActive(status, "FightStarted.EnsureActive");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Dusk fight start trait grant failed", ex);
        }
    }

    private static void ObserveBurnAfterAdd(ModHookContext context)
    {
        try
        {
            DuskAfterheatRecoveryService.ObserveBurnAdded(
                context.Target as IStatusManager,
                BuffIdFromArgs(context.Arguments),
                "StatusManager.AddBuff");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Dusk burn observer attachment failed after AddBuff", ex);
        }
    }

    private static void ObserveEnemyAfterInit(ModHookContext context)
    {
        try
        {
            DuskAfterheatRecoveryService.ObserveEnemyInitialized(
                (context.Target as Enemy)?.Status,
                "Enemy.Init");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Dusk burn observer attachment failed after Enemy.Init", ex);
        }
    }

    private static string BuffIdFromArgs(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return "";
        }

        return args[0] is IBuffItemConfig config
            ? config.BuffId ?? ""
            : Convert.ToString(args[0]) ?? "";
    }
}
