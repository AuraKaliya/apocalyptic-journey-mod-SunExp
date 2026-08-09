using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SandroneCatRuntime
{
    private const string PartnerLocalId = "sandrone_cat";
    private const string PartnerFullId = "Terrias_terrias_sandrone_cat";
    private const string BlessingLocalId = "sandrone_cat_placeholder";
    private const string BlessingFullId = "Terrias_terrias_sandrone_cat_placeholder";

    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.After(
            modConfig,
            "GameEntryUI.CheckCareer",
            CleanupPlaceholderBlessing,
            "SandroneCat");
        TerriasBattleLifecycleRouter.Register("SandroneCat", new TerriasBattleLifecycleSubscription
        {
            FightStarted = OnFightStarted,
            FightEnding = OnFightEnding
        });
    }

    private static void CleanupPlaceholderBlessing(ModHookContext context)
    {
        try
        {
            PartnerApi.RemovePlaceholderBlessing(BlessingLocalId, BlessingFullId);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Sandrone Cat placeholder blessing cleanup failed", ex);
        }
    }

    private static void OnFightStarted(ModHookContext context)
    {
        var status = FightPlayer.Instance?.Status;
        if (status == null || !PartnerApi.IsCurrentPartner(PartnerLocalId, PartnerFullId))
        {
            return;
        }

        RunStep("trait buff", () =>
        {
            if (status.GetBuff(TerriasIds.SandroneCatTrait) == null)
            {
                status.AddBuff(TerriasIds.SandroneCatTrait, 1);
            }
        });
        RunStep("combat registration", () => SandroneCatMaxHpService.ApplyBattleStart(status));
    }

    private static void OnFightEnding(ModHookContext context)
    {
        var status = FightPlayer.Instance?.Status;
        if (status == null)
        {
            return;
        }

        RunStep("combat-end max HP", () => SandroneCatMaxHpService.ApplyBattleEnd(status));
    }

    private static void RunStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Sandrone Cat lifecycle step failed: " + step, ex);
        }
    }
}
