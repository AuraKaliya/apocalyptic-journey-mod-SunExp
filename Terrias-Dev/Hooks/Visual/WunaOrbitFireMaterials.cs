using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Terrias.Dll.Hooks.Visual;

public static class WunaOrbitFireShaderIds
{
    public const string ShaderName = "Terrias/WunaOrbitFire";
    public const string ShaderId = "terrias.wuna_orbit_fire";
    public const string BackCoreEffectId = "terrias.wuna.orbit_fire.core.back";
    public const string FrontCoreEffectId = "terrias.wuna.orbit_fire.core.front";
    public const string BackEffectId = "terrias.wuna.orbit_fire.back";
    public const string FrontEffectId = "terrias.wuna.orbit_fire.front";
    public const string TrailMaskTexturePath = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png";
    public const string TrailNoiseTexturePath = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png";
    public const string FlameAtlasTexturePath = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/Flame02_16x4.png";

    public static readonly int MainTex = Shader.PropertyToID("_MainTex");
    public static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    public static readonly int FlowTime = Shader.PropertyToID("_TerriasFlowTime");
    public static readonly int Intensity = Shader.PropertyToID("_TerriasIntensity");
    public static readonly int Layer = Shader.PropertyToID("_TerriasLayer");
    public static readonly int CoreMode = Shader.PropertyToID("_TerriasCoreMode");
    public static readonly int CoreColor = Shader.PropertyToID("_TerriasCoreColor");
    public static readonly int EdgeColor = Shader.PropertyToID("_TerriasEdgeColor");
    public static readonly int SmokeColor = Shader.PropertyToID("_TerriasSmokeColor");
    public static readonly int NoiseScale = Shader.PropertyToID("_TerriasNoiseScale");
    public static readonly int Distortion = Shader.PropertyToID("_TerriasDistortion");
}

public static class WunaOrbitFireMaterials
{
    private const string LogPrefix = "[WunaOrbitFire]";
    private static bool fallbackLogged;

    public static Material CreateLayerMaterial(bool frontLayer, bool coreLayer, bool flameAtlasLayer = false)
    {
        var effectId = coreLayer
            ? frontLayer ? WunaOrbitFireShaderIds.FrontCoreEffectId : WunaOrbitFireShaderIds.BackCoreEffectId
            : frontLayer ? WunaOrbitFireShaderIds.FrontEffectId : WunaOrbitFireShaderIds.BackEffectId;
        var material = EffectMaterialFactory.CreateMaterial(
            effectId,
            WunaOrbitFireShaderIds.ShaderId,
            WunaOrbitFireShaderIds.ShaderName,
            LogPrefix);

        if (material == null)
        {
            material = CreateFallbackMaterial(frontLayer, coreLayer, flameAtlasLayer);
        }

        material.name = "Terrias_WunaOrbitFire_"
                        + (frontLayer ? "Front" : "Back")
                        + (coreLayer ? "_Core" : flameAtlasLayer ? "_Flames" : "_Detail");
        SetFloatIfPresent(material, WunaOrbitFireShaderIds.Layer, frontLayer ? 1f : -1f);
        SetFloatIfPresent(material, WunaOrbitFireShaderIds.CoreMode, coreLayer ? 1f : 0f);
        EnsureLayerTextures(material, flameAtlasLayer);
        return material;
    }

    public static void DestroyAll(IEnumerable<Material?> materials)
    {
        foreach (var material in materials)
        {
            if (material != null)
            {
                Object.Destroy(material);
            }
        }
    }

    private static Material CreateFallbackMaterial(bool frontLayer, bool coreLayer, bool flameAtlasLayer)
    {
        var shader = Shader.Find("Particles/Additive")
                     ?? Shader.Find("Legacy Shaders/Particles/Additive")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            TerriasLog.Warn(LogPrefix + " fallback shader missing; orbit fire layer will use Unity's default material.");
            return new Material(Shader.Find("Diffuse"));
        }

        var material = new Material(shader);
        material.color = frontLayer
            ? coreLayer
                ? new Color(1f, 0.9f, 0.42f, 0.82f)
                : flameAtlasLayer
                    ? new Color(1f, 0.56f, 0.18f, 0.62f)
                    : new Color(1f, 0.48f, 0.12f, 0.54f)
            : coreLayer
                ? new Color(1f, 0.66f, 0.24f, 0.48f)
                : flameAtlasLayer
                    ? new Color(1f, 0.5f, 0.16f, 0.34f)
                    : new Color(0.95f, 0.24f, 0.08f, 0.32f);
        material.renderQueue = 3000;
        TrySetBlendMode(material);
        EnsureLayerTextures(material, flameAtlasLayer);
        if (!fallbackLogged)
        {
            fallbackLogged = true;
            TerriasLog.Warn(LogPrefix + " shader/material unavailable; using visible fallback orbit material.");
        }

        return material;
    }

    private static void TrySetBlendMode(Material material)
    {
        SetFloatIfPresent(material, "_Mode", 2f);
        SetFloatIfPresent(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetFloatIfPresent(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        material.EnableKeyword("_ALPHABLEND_ON");
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static void EnsureLayerTextures(Material material, bool flameAtlasLayer)
    {
        var hasMainTexture = material.HasProperty(WunaOrbitFireShaderIds.MainTex);
        var hasNoiseTexture = IsOrbitFireShader(material) && material.HasProperty(WunaOrbitFireShaderIds.NoiseTex);
        if (!hasMainTexture && !hasNoiseTexture)
        {
            return;
        }

        if (!flameAtlasLayer
            && hasMainTexture
            && TryGetTexture(material, WunaOrbitFireShaderIds.MainTex, out var mainTexture)
            && mainTexture != null)
        {
            var existingNoise = hasNoiseTexture && TryGetTexture(material, WunaOrbitFireShaderIds.NoiseTex, out var noiseTexture)
                ? noiseTexture
                : null;
            if (existingNoise != null)
            {
                ApplyTextureDefaults(mainTexture, true);
                ApplyTextureDefaults(existingNoise, true);
                return;
            }
        }

        var mainPath = flameAtlasLayer
            ? WunaOrbitFireShaderIds.FlameAtlasTexturePath
            : WunaOrbitFireShaderIds.TrailMaskTexturePath;
        var mask = EffectTextureCache.Load(mainPath, LogPrefix);
        if (mask != null && hasMainTexture)
        {
            ApplyTextureDefaults(mask, !flameAtlasLayer);
            TrySetTexture(material, WunaOrbitFireShaderIds.MainTex, mask);
        }

        var noise = EffectTextureCache.Load(WunaOrbitFireShaderIds.TrailNoiseTexturePath, LogPrefix);
        if (noise != null && hasNoiseTexture)
        {
            ApplyTextureDefaults(noise, true);
            TrySetTexture(material, WunaOrbitFireShaderIds.NoiseTex, noise);
        }
    }

    private static void ApplyTextureDefaults(Texture texture, bool repeat)
    {
        texture.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
    }

    private static void SetFloatIfPresent(Material material, int propertyId, float value)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetFloat(propertyId, value);
        }
    }

    private static bool TryGetTexture(Material material, int propertyId, out Texture? texture)
    {
        texture = null;
        try
        {
            texture = material.GetTexture(propertyId);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug(LogPrefix + " texture read skipped for unsupported material property: " + material.name + " (" + ex.Message + ")");
            return false;
        }
    }

    private static void TrySetTexture(Material material, int propertyId, Texture texture)
    {
        try
        {
            material.SetTexture(propertyId, texture);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug(LogPrefix + " texture assignment skipped for unsupported material property: " + material.name + " (" + ex.Message + ")");
        }
    }

    private static bool IsOrbitFireShader(Material material)
    {
        return string.Equals(material.shader?.name ?? "", WunaOrbitFireShaderIds.ShaderName, StringComparison.Ordinal);
    }
}
