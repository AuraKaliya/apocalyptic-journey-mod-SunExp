using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
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
            FightStarted = GrantTraitOnFightStart
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
}
