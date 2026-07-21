using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.GameApi;

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

    public static void RegisterTerriasDefaults()
    {
        CardVisualEffectRegistry.ClearOwner(TerriasIds.ModId);
        RegisterFrameEffect(
            TerriasIds.ModId,
            TerriasIds.BlazingCrownCollapseHoloEffectBindingId,
            TerriasIds.CardFaceFoilHoloVisualEffectId,
            "Blazing Crown Collapse Foil Holo",
            100,
            TerriasIds.BlazingCrownCollapseCardEffectIds);
        RegisterFrameEffect(
            TerriasIds.ModId,
            TerriasIds.StellarOvertureStardustEffectBindingId,
            TerriasIds.CardFaceStardustVisualEffectId,
            "Stellar Overture Stardust",
            120,
            TerriasIds.StellarOvertureCardEffectIds);
        TerriasLog.Info("Card visual effect registry initialized: effects=" + CardVisualEffectRegistry.EffectCount);
    }
}
