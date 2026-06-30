using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.GameApi;

public static class CardVisualEffectApi
{
    public static void RegisterEffect(
        string ownerModId,
        string id,
        CardVisualEffectTarget target,
        string visualEffectId,
        string displayName,
        int priority,
        IEnumerable<string>? cardIds)
    {
        CardVisualEffectRegistry.Register(new CardVisualEffectSpec(
            ownerModId,
            id,
            target,
            visualEffectId,
            displayName,
            priority,
            cardIds));
    }

    public static void RegisterFrameEffect(
        string ownerModId,
        string id,
        string visualEffectId,
        string displayName,
        int priority,
        IEnumerable<string>? cardIds)
    {
        RegisterEffect(
            ownerModId,
            id,
            CardVisualEffectTarget.Frame,
            visualEffectId,
            displayName,
            priority,
            cardIds);
    }

    public static void RegisterSunExpDefaults()
    {
        CardVisualEffectRegistry.ClearOwner(SunExpIds.ModId);
        RegisterFrameEffect(
            SunExpIds.ModId,
            SunExpIds.BlazingCrownCollapseHoloEffectBindingId,
            SunExpIds.CardFrameHoloFlowVisualEffectId,
            "Blazing Crown Collapse Holo Flow",
            100,
            SunExpIds.BlazingCrownCollapseCardEffectIds);
        SunExpLog.Info("Card visual effect registry initialized: effects=" + CardVisualEffectRegistry.EffectCount);
    }
}
