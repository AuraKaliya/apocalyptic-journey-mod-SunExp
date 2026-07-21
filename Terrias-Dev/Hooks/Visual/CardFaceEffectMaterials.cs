using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardFaceEffectShaderIds
{
    public const string ShaderName = "SunExp/CardFaceEffect";

    public static readonly int MainTex = Shader.PropertyToID("_MainTex");
    public static readonly int OverlayMode = Shader.PropertyToID("_SunExpOverlayMode");
    public static readonly int FrameOnlyOverlay = Shader.PropertyToID("_SunExpFrameOnlyOverlay");
    public static readonly int QualityScale = Shader.PropertyToID("_SunExpQualityScale");
}

internal static class CardFaceEffectMaterials
{
    private const string LogPrefix = "[CardFaceEffect]";
    private static readonly Dictionary<string, Material?> UiMaterialCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Material?> UiOverlayMaterialCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedMissingMaterials = new(StringComparer.Ordinal);

    public static Material? SharedUiMaterial(CardVisualEffectSpec spec)
    {
        if (UiMaterialCache.TryGetValue(spec.Id, out var cached))
        {
            ApplyQualityScale(cached);
            return cached;
        }

        var material = EffectMaterialFactory.CreateMaterial(
            spec.VisualEffectId,
            SunExpIds.CardFaceEffectShaderId,
            CardFaceEffectShaderIds.ShaderName,
            LogPrefix);
        if (material != null)
        {
            material.name = "SunExp_CardFaceEffect_" + spec.Id;
            ApplyQualityScale(material);
        }
        else if (LoggedMissingMaterials.Add(spec.Id))
        {
            SunExpLog.Warn(LogPrefix + " material unavailable: " + spec.VisualEffectId);
        }

        UiMaterialCache[spec.Id] = material;
        return material;
    }

    public static Material? SharedUiOverlayMaterial(CardVisualEffectSpec spec)
    {
        if (UiOverlayMaterialCache.TryGetValue(spec.Id, out var cached))
        {
            ApplyOverlayMode(cached, true);
            ApplyQualityScale(cached);
            return cached;
        }

        var shared = SharedUiMaterial(spec);
        if (shared == null)
        {
            UiOverlayMaterialCache[spec.Id] = null;
            return null;
        }

        var material = new Material(shared)
        {
            name = shared.name + "_Overlay"
        };
        ApplyOverlayMode(material, true);
        ApplyQualityScale(material);
        UiOverlayMaterialCache[spec.Id] = material;
        return material;
    }

    public static Material? CreateOwnedMaterial(CardVisualEffectSpec spec)
    {
        var shared = SharedUiMaterial(spec);
        if (shared == null)
        {
            return null;
        }

        var material = new Material(shared)
        {
            name = shared.name + "_Owned"
        };
        ApplyOverlayMode(material, false);
        ApplyQualityScale(material);
        return material;
    }

    public static void ApplyRuntimeTexture(Material? material, Texture? texture)
    {
        if (material == null || texture == null || !material.HasProperty(CardFaceEffectShaderIds.MainTex))
        {
            return;
        }

        material.SetTexture(CardFaceEffectShaderIds.MainTex, texture);
    }

    public static void DestroyOwned(Material? material)
    {
        if (material != null)
        {
            Object.Destroy(material);
        }
    }

    private static void ApplyQualityScale(Material? material)
    {
        if (material == null || !material.HasProperty(CardFaceEffectShaderIds.QualityScale))
        {
            return;
        }

        material.SetFloat(CardFaceEffectShaderIds.QualityScale, SunExpPerformanceSettings.CardFaceEffectQualityScale);
    }

    private static void ApplyOverlayMode(Material? material, bool enabled)
    {
        if (material == null || !material.HasProperty(CardFaceEffectShaderIds.OverlayMode))
        {
            return;
        }

        material.SetFloat(CardFaceEffectShaderIds.OverlayMode, enabled ? 1f : 0f);
        if (material.HasProperty(CardFaceEffectShaderIds.FrameOnlyOverlay))
        {
            material.SetFloat(CardFaceEffectShaderIds.FrameOnlyOverlay, 0f);
        }
    }
}
