using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardFaceEffectApplier
{
    private const string LogPrefix = "[CardFaceEffect]";
    private static readonly HashSet<string> LoggedEffects = new();

    public static bool Apply(CardVisualSkinMarker marker, CardVisualEffectSpec effect)
    {
        if (!SunExpPerformanceSettings.CardFaceEffectsEnabled)
        {
            return Clear(marker);
        }

        if (marker.FaceImage != null)
        {
            return ApplyImageMaterial(marker, effect);
        }

        if (marker.FaceMesh != null)
        {
            return ApplyMeshMaterial(marker, effect);
        }

        return Clear(marker);
    }

    public static bool Clear(CardVisualSkinMarker marker)
    {
        return marker.ClearFaceEffectMaterial();
    }

    private static bool ApplyImageMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect)
    {
        var material = CardFaceEffectMaterials.SharedUiMaterial(effect);
        if (material == null)
        {
            return Clear(marker);
        }

        marker.ClearOwnedFaceEffectMaterial();
        var changed = marker.ApplyFaceImageEffectMaterial(material)
            || marker.LastFaceEffectId != effect.Id;
        marker.LastFaceEffectId = effect.Id;
        LogApplied(effect);
        return changed;
    }

    private static bool ApplyMeshMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect)
    {
        var material = marker.FaceEffectOwnedMaterial;
        if (material == null || marker.LastFaceEffectId != effect.Id)
        {
            material = CardFaceEffectMaterials.CreateOwnedMaterial(effect);
            if (material == null)
            {
                return Clear(marker);
            }

            marker.ReplaceOwnedFaceEffectMaterial(material);
        }

        CardFaceEffectMaterials.ApplyRuntimeTexture(material, marker.LastFaceTexture ?? marker.FaceMaterial?.mainTexture);
        var changed = marker.ApplyFaceMeshEffectMaterial(material)
            || marker.LastFaceEffectId != effect.Id;
        marker.LastFaceEffectId = effect.Id;
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
