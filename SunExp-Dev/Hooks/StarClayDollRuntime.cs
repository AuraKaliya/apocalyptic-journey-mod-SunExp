using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class StarClayDollRuntime
{
    private const string PartnerLocalId = "star_clay_doll";
    private const string PartnerFullId = "SunExp_sunexp_star_clay_doll";
    private const string BlessingLocalId = "star_clay_doll_placeholder";
    private const string BlessingFullId = "SunExp_sunexp_star_clay_doll_placeholder";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.CheckCareer", CleanupPlaceholderBlessing);
        SunExpBattleLifecycleRouter.Register("StarClayDoll", new SunExpBattleLifecycleSubscription
        {
            FightStarted = GrantTraitOnFightStart
        });
        SunExpStatusLifecycleRouter.Register("StarClayDoll", new SunExpStatusLifecycleSubscription
        {
            AfterHit = ProtectAfterHit
        });
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "StarClayDoll");
    }

    private static void CleanupPlaceholderBlessing(ModHookContext context)
    {
        try
        {
            PartnerApi.RemovePlaceholderBlessing(BlessingLocalId, BlessingFullId);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star Clay Doll placeholder blessing cleanup failed", ex);
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
                    SunExpIds.StarClayDollTrait,
                    1,
                    "StarClayDoll",
                    "FightStarted.GrantTrait"))
            {
                SunExpLog.Info("Granted Star Clay Doll trait: owner=" + status.InstanceId);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star Clay Doll fight start trait grant failed", ex);
        }
    }

    private static void ProtectAfterHit(ModHookContext context)
    {
        try
        {
            if (context.Target is IStatusManager status)
            {
                TryProtect(status);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star Clay Doll body protection failed", ex);
        }
    }

    private static void TryProtect(IStatusManager status)
    {
        if (status.CurHp > 0
            || BuffApi.Level(status, SunExpIds.StarClayBody) <= 0
            || !string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal))
        {
            return;
        }

        if (StatusApi.HasNativeResurrectionAvailable(status))
        {
            SunExpLog.Info("Star Clay Doll protection yielded to native resurrection: owner=" + status.InstanceId);
            return;
        }

        BuffApi.SetExactLevel(status, SunExpIds.StarClayBody, BuffApi.Level(status, SunExpIds.StarClayBody) - 1);
        var nextMax = Math.Max(1, status.MaxHp / 2);
        if (StatusApi.TryStarClayResurrection(status, nextMax))
        {
            PlayerApi.ShowCaption("\u661f\u6ce5\u5080\u8eab\u66ff\u4f60\u627f\u53d7\u4e86\u8fd9\u6b21\u5931\u8d25\u3002");
        }
    }
}
