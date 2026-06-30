using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.GameApi;

public static class CardFrameEffectApi
{
    public static void RegisterEffect(
        string ownerModId,
        string id,
        string skinId,
        string visualEffectId,
        string displayName,
        int priority)
    {
        CardFrameEffectRegistry.Register(new CardFrameEffectSpec(
            ownerModId,
            id,
            skinId,
            visualEffectId,
            displayName,
            priority));
    }

    public static void RegisterSunExpDefaults()
    {
        CardFrameEffectRegistry.ClearOwner(SunExpIds.ModId);
        RegisterEffect(
            SunExpIds.ModId,
            SunExpIds.SunCardFrameHoloEffectBindingId,
            SunExpIds.SunCardVisualSkinId,
            SunExpIds.SunCardFrameHoloVisualEffectId,
            "Sun Holo Flow",
            100);
        SunExpLog.Info("Card frame effect registry initialized: effects=" + CardFrameEffectRegistry.EffectCount);
    }
}
