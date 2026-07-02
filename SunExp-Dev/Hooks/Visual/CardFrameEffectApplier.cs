using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardFrameEffectApplier
{
    private const string LogPrefix = "[CardFrameEffect]";
    private static readonly HashSet<string> LoggedEffects = new();

    public static bool Apply(CardVisualSkinMarker marker, CardVisualEffectSpec effect)
    {
        if (!SunExpPerformanceSettings.CardFrameEffectsEnabled)
        {
            return Clear(marker);
        }

        if (marker.FrameImage != null)
        {
            return ApplyImageMaterial(marker, effect);
        }

        if (marker.FrameMesh != null)
        {
            return ApplyMeshMaterial(marker, effect);
        }

        return Clear(marker);
    }

    public static bool Clear(CardVisualSkinMarker marker)
    {
        return marker.ClearFrameEffectMaterial();
    }

    private static bool ApplyImageMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect)
    {
        var material = CardFrameEffectMaterials.SharedUiOverlayMaterial(effect);
        if (material == null)
        {
            return Clear(marker);
        }

        marker.ClearOwnedFrameEffectMaterial();
        var changed = marker.ApplyFrameImageEffectOverlay(material)
            || marker.LastFrameEffectId != effect.Id;
        marker.LastFrameEffectId = effect.Id;
        LogApplied(effect);
        return changed;
    }

    private static bool ApplyMeshMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect)
    {
        var material = marker.FrameEffectOwnedMaterial;
        if (material == null || marker.LastFrameEffectId != effect.Id)
        {
            material = CardFrameEffectMaterials.CreateOwnedOverlayMaterial(effect);
            if (material == null)
            {
                return Clear(marker);
            }

            marker.ReplaceOwnedFrameEffectMaterial(material);
        }

        CardFrameEffectMaterials.ApplyRuntimeTexture(material, marker.LastFrameTexture ?? marker.FrameMaterial?.mainTexture);
        var changed = marker.ApplyFrameMeshEffectOverlay(material)
            || marker.LastFrameEffectId != effect.Id;
        marker.LastFrameEffectId = effect.Id;
        LogApplied(effect);
        return changed;
    }

    private static void LogApplied(CardVisualEffectSpec effect)
    {
        if (LoggedEffects.Add(effect.Id))
        {
            SunExpLog.Info(LogPrefix + " applied " + effect.DisplayName + ": " + effect.VisualEffectId);
        }
    }

}
