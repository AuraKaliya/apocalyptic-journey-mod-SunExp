using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardFrameEffectApplier
{
    private const string LogPrefix = "[CardFrameEffect]";
    private static readonly HashSet<string> LoggedEffects = new();
    private static readonly HashSet<string> LoggedDiagnostics = new();

    public static bool Apply(CardVisualSkinMarker marker, CardVisualEffectSpec effect, IDataConfig? config)
    {
        if (!SunExpPerformanceSettings.CardFrameEffectsEnabled)
        {
            LogDiagnostic(marker, effect, config, "disabled-by-performance");
            return Clear(marker);
        }

        if (marker.FrameImage != null)
        {
            return ApplyImageMaterial(marker, effect, config);
        }

        if (marker.FrameMesh != null)
        {
            return ApplyMeshMaterial(marker, effect, config);
        }

        if (marker.BackgroundImage != null && marker.LastFrameSprite != null)
        {
            return ApplyFallbackImageMaterial(marker, effect, config);
        }

        if (marker.BackgroundMesh != null && marker.LastFrameTexture != null)
        {
            return ApplyFallbackMeshMaterial(marker, effect, config);
        }

        LogDiagnostic(marker, effect, config, "no-frame-target");
        return Clear(marker);
    }

    public static bool Clear(CardVisualSkinMarker marker)
    {
        return marker.ClearFrameEffectMaterial();
    }

    private static bool ApplyImageMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect, IDataConfig? config)
    {
        var material = marker.FrameEffectOwnedMaterial;
        if (material == null || marker.LastFrameEffectId != effect.Id)
        {
            material = CardFrameEffectMaterials.CreateOwnedMaterial(effect);
            if (material == null)
            {
                LogDiagnostic(marker, effect, config, "frame-image-integrated-material-missing");
                return Clear(marker);
            }

            marker.ReplaceOwnedFrameEffectMaterial(material);
        }

        if (marker.LastFrameTexture == null)
        {
            LogDiagnostic(marker, effect, config, "frame-image-integrated-texture-missing");
            return Clear(marker);
        }

        LogDiagnostic(marker, effect, config, "frame-image-integrated-material");
        CardFrameEffectMaterials.ApplyRuntimeTexture(material, marker.LastFrameTexture);
        var changed = marker.ApplyFrameImageEffectMaterial(material)
            || marker.LastFrameEffectId != effect.Id;
        marker.LastFrameEffectId = effect.Id;
        LogApplied(effect);
        return changed;
    }

    private static bool ApplyFallbackImageMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect, IDataConfig? config)
    {
        var material = CardFrameEffectMaterials.SharedUiOverlayMaterial(effect);
        var sprite = marker.LastFrameSprite;
        if (material == null || sprite == null)
        {
            LogDiagnostic(marker, effect, config, "fallback-frame-image-material-or-sprite-missing");
            return Clear(marker);
        }

        LogDiagnostic(marker, effect, config, "fallback-frame-image");
        marker.ClearOwnedFrameEffectMaterial();
        var changed = marker.ApplyFallbackFrameImageEffectOverlay(material, sprite)
            || marker.LastFrameEffectId != effect.Id;
        marker.LastFrameEffectId = effect.Id;
        LogApplied(effect);
        return changed;
    }

    private static bool ApplyMeshMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect, IDataConfig? config)
    {
        var material = marker.FrameEffectOwnedMaterial;
        if (material == null || marker.LastFrameEffectId != effect.Id)
        {
            material = CardFrameEffectMaterials.CreateOwnedMaterial(effect);
            if (material == null)
            {
                LogDiagnostic(marker, effect, config, "frame-mesh-integrated-material-missing");
                return Clear(marker);
            }

            marker.ReplaceOwnedFrameEffectMaterial(material);
        }

        if (marker.LastFrameTexture == null)
        {
            LogDiagnostic(marker, effect, config, "frame-mesh-integrated-texture-missing");
            return Clear(marker);
        }

        LogDiagnostic(marker, effect, config, "frame-mesh-integrated-material");
        CardFrameEffectMaterials.ApplyRuntimeTexture(material, marker.LastFrameTexture);
        var changed = marker.ApplyFrameMeshEffectMaterial(material)
            || marker.LastFrameEffectId != effect.Id;
        marker.LastFrameEffectId = effect.Id;
        LogApplied(effect);
        return changed;
    }

    private static bool ApplyFallbackMeshMaterial(CardVisualSkinMarker marker, CardVisualEffectSpec effect, IDataConfig? config)
    {
        var material = marker.FrameEffectOwnedMaterial;
        if (material == null || marker.LastFrameEffectId != effect.Id)
        {
            material = CardFrameEffectMaterials.CreateOwnedOverlayMaterial(effect);
            if (material == null)
            {
                LogDiagnostic(marker, effect, config, "fallback-frame-mesh-material-missing");
                return Clear(marker);
            }

            marker.ReplaceOwnedFrameEffectMaterial(material);
        }

        LogDiagnostic(marker, effect, config, "fallback-frame-mesh-unified-overlay");
        CardFrameEffectMaterials.ApplyRuntimeTexture(material, marker.LastFrameTexture);
        var changed = marker.ApplyFallbackFrameMeshEffectOverlay(material)
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

    private static void LogDiagnostic(CardVisualSkinMarker marker, CardVisualEffectSpec effect, IDataConfig? config, string route)
    {
        var cardId = DictionaryUtil.Get(config?.data, "Id", "unknown");
        var icon = DictionaryUtil.Get(config?.data, "Icon");
        var pack = DictionaryUtil.Get(config?.data, "PackBelong");
        var key = cardId + "\u001f" + icon + "\u001f" + pack + "\u001f" + effect.Id + "\u001f" + route;
        if (!LoggedDiagnostics.Add(key))
        {
            return;
        }

        SunExpLog.Info(LogPrefix
            + " diag route=" + route
            + ", card=" + cardId
            + ", icon=" + icon
            + ", pack=" + pack
            + ", effect=" + effect.Id
            + ", visualEffect=" + effect.VisualEffectId
            + ", marker={" + marker.FrameEffectDiagnosticSummary() + "}");
    }

}
