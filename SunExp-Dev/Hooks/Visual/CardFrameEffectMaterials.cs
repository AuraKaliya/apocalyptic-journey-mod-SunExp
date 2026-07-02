using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Visual;

internal static class CardFrameEffectShaderIds
{
    public const string ShaderName = "SunExp/CardFaceEffect";

    public static readonly int MainTex = Shader.PropertyToID("_MainTex");
    public static readonly int FrameOnlyOverlay = Shader.PropertyToID("_SunExpFrameOnlyOverlay");
    public static readonly int QualityScale = Shader.PropertyToID("_SunExpQualityScale");
}

internal static class CardFrameEffectMaterials
{
    private const string LogPrefix = "[CardFrameEffect]";
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
            SunExpIds.CardFrameHoloFlowShaderId,
            CardFrameEffectShaderIds.ShaderName,
            LogPrefix);
        if (material != null)
        {
            material.name = "SunExp_CardFrameEffect_" + spec.Id;
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
        ApplyQualityScale(material);
        UiOverlayMaterialCache[spec.Id] = material;
        return material;
    }

    public static Material? CreateOwnedOverlayMaterial(CardVisualEffectSpec spec)
    {
        var shared = SharedUiMaterial(spec);
        if (shared == null)
        {
            return null;
        }

        var material = new Material(shared)
        {
            name = shared.name + "_OwnedOverlay"
        };
        ApplyQualityScale(material);
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
        ApplyQualityScale(material);
        return material;
    }

    public static void ApplyRuntimeTexture(Material? material, Texture? texture)
    {
        if (material == null || texture == null || !material.HasProperty(CardFrameEffectShaderIds.MainTex))
        {
            return;
        }

        material.SetTexture(CardFrameEffectShaderIds.MainTex, texture);
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
        if (material == null || !material.HasProperty(CardFrameEffectShaderIds.QualityScale))
        {
            return;
        }

        material.SetFloat(CardFrameEffectShaderIds.QualityScale, 1f);
    }

}
