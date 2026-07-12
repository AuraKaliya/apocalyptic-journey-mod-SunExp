using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class DuskPartnerRuntime
{
    private const string PartnerLocalId = "dusk";
    private const string PartnerFullId = "SunExp_sunexp_dusk";
    private const string BlessingLocalId = "dusk_afterheat_recovery";
    private const string BlessingFullId = "SunExp_sunexp_dusk_afterheat_recovery";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.CheckCareer", CleanupPlaceholderBlessing);
        SunExpBattleLifecycleRouter.Register("DuskPartner", new SunExpBattleLifecycleSubscription
        {
            FightStarted = GrantTraitOnFightStart,
            FightEnding = _ => DuskAfterheatRecoveryService.Deactivate(null, "FightEnding")
        });
        SunExpStatusLifecycleRouter.Register("DuskPartner", new SunExpStatusLifecycleSubscription
        {
            AfterAddBuff = ObserveBurnAfterAdd,
            AfterEnemyInit = ObserveEnemyAfterInit
        });
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "DuskPartner");
    }

    private static void CleanupPlaceholderBlessing(ModHookContext context)
    {
        try
        {
            PartnerApi.RemovePlaceholderBlessing(BlessingLocalId, BlessingFullId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk placeholder blessing cleanup failed", ex);
        }
    }

    private static void GrantTraitOnFightStart(ModHookContext context)
    {
        try
        {
            DuskAfterheatRecoveryService.Deactivate(null, "FightStarted.Reset");
            PartnerApi.RemovePlaceholderBlessing(BlessingLocalId, BlessingFullId);
            var status = FightPlayer.Instance?.Status;
            if (status == null || !PartnerApi.IsCurrentPartner(PartnerLocalId, PartnerFullId))
            {
                return;
            }

            if (BuffApi.TryAddBattleScopedBuffOnce(
                    status,
                    SunExpIds.DuskAfterheatRecoveryTrait,
                    1,
                    "DuskPartner",
                    "FightStarted.GrantTrait"))
            {
                SunExpLog.Info("Granted Dusk afterheat recovery trait: owner=" + status.InstanceId);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk fight start trait grant failed", ex);
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
            SunExpLog.Error("Dusk burn observer attachment failed after AddBuff", ex);
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
            SunExpLog.Error("Dusk burn observer attachment failed after Enemy.Init", ex);
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
