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
        RegisterFaceEffect(ownerModId, id, visualEffectId, displayName, priority, cardIds);
    }

    public static void RegisterFaceEffect(
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
            CardVisualEffectTarget.Face,
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
            SunExpIds.CardFaceFoilHoloVisualEffectId,
            "Blazing Crown Collapse Foil Holo",
            100,
            SunExpIds.BlazingCrownCollapseCardEffectIds);
        RegisterFaceEffect(
            SunExpIds.ModId,
            SunExpIds.StellarOvertureStardustEffectBindingId,
            SunExpIds.CardFaceStardustVisualEffectId,
            "Stellar Overture Stardust",
            120,
            SunExpIds.StellarOvertureCardIds);
        SunExpLog.Info("Card visual effect registry initialized: effects=" + CardVisualEffectRegistry.EffectCount);
    }
}
