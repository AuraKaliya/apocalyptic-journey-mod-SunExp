using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunExp.Dll.Hooks.Visual;

public static class WunaOrbitFireShaderIds
{
    public const string ShaderName = "SunExp/WunaOrbitFire";
    public const string ShaderId = "sunexp.wuna_orbit_fire";
    public const string BackCoreEffectId = "sunexp.wuna.orbit_fire.core.back";
    public const string FrontCoreEffectId = "sunexp.wuna.orbit_fire.core.front";
    public const string BackEffectId = "sunexp.wuna.orbit_fire.back";
    public const string FrontEffectId = "sunexp.wuna.orbit_fire.front";
    public const string TrailMaskTexturePath = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png";
    public const string TrailNoiseTexturePath = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png";
    public const string FlameAtlasTexturePath = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/Flame02_16x4.png";

    public static readonly int MainTex = Shader.PropertyToID("_MainTex");
    public static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    public static readonly int FlowTime = Shader.PropertyToID("_SunExpFlowTime");
    public static readonly int Intensity = Shader.PropertyToID("_SunExpIntensity");
    public static readonly int Layer = Shader.PropertyToID("_SunExpLayer");
    public static readonly int CoreMode = Shader.PropertyToID("_SunExpCoreMode");
    public static readonly int CoreColor = Shader.PropertyToID("_SunExpCoreColor");
    public static readonly int EdgeColor = Shader.PropertyToID("_SunExpEdgeColor");
    public static readonly int SmokeColor = Shader.PropertyToID("_SunExpSmokeColor");
    public static readonly int NoiseScale = Shader.PropertyToID("_SunExpNoiseScale");
    public static readonly int Distortion = Shader.PropertyToID("_SunExpDistortion");
}

public static class WunaOrbitFireMaterials
{
    private const string LogPrefix = "[WunaOrbitFire]";

    public static Material CreateLayerMaterial(bool frontLayer, bool coreLayer)
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
            material = CreateFallbackMaterial(frontLayer, coreLayer);
        }

        material.name = "SunExp_WunaOrbitFire_" + (frontLayer ? "Front" : "Back") + (coreLayer ? "_Core" : "_Detail");
        SetFloatIfPresent(material, WunaOrbitFireShaderIds.Layer, frontLayer ? 1f : -1f);
        SetFloatIfPresent(material, WunaOrbitFireShaderIds.CoreMode, coreLayer ? 1f : 0f);
        EnsureTrailTextures(material, coreLayer);
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

    private static Material CreateFallbackMaterial(bool frontLayer, bool coreLayer)
    {
        var shader = Shader.Find("Particles/Additive")
                     ?? Shader.Find("Legacy Shaders/Particles/Additive")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            SunExpLog.Warn(LogPrefix + " fallback shader missing; orbit fire layer will use Unity's default material.");
            return new Material(Shader.Find("Diffuse"));
        }

        var material = new Material(shader);
        material.color = frontLayer
            ? coreLayer
                ? new Color(1f, 0.86f, 0.38f, 0.62f)
                : new Color(1f, 0.46f, 0.12f, 0.38f)
            : coreLayer
                ? new Color(1f, 0.62f, 0.22f, 0.32f)
                : new Color(0.95f, 0.24f, 0.08f, 0.2f);
        EnsureTrailTextures(material, coreLayer);
        SunExpLog.Debug(LogPrefix + " shader not found; using simple sprite fallback for " + (frontLayer ? "front" : "back") + " layer.");
        return material;
    }

    private static void EnsureTrailTextures(Material material, bool coreLayer)
    {
        var hasMainTexture = material.HasProperty(WunaOrbitFireShaderIds.MainTex);
        var hasNoiseTexture = IsOrbitFireShader(material) && material.HasProperty(WunaOrbitFireShaderIds.NoiseTex);
        if (!hasMainTexture && !hasNoiseTexture)
        {
            return;
        }

        if (hasMainTexture && TryGetTexture(material, WunaOrbitFireShaderIds.MainTex, out var mainTexture) && mainTexture != null)
        {
            var existingNoise = hasNoiseTexture && TryGetTexture(material, WunaOrbitFireShaderIds.NoiseTex, out var noiseTexture)
                ? noiseTexture
                : null;
            if (existingNoise != null)
            {
                return;
            }
        }

        var mainPath = WunaOrbitFireShaderIds.TrailMaskTexturePath;
        var mask = EffectTextureCache.Load(mainPath, LogPrefix);
        if (mask != null && hasMainTexture)
        {
            TrySetTexture(material, WunaOrbitFireShaderIds.MainTex, mask);
        }

        var noise = EffectTextureCache.Load(WunaOrbitFireShaderIds.TrailNoiseTexturePath, LogPrefix);
        if (noise != null && hasNoiseTexture)
        {
            TrySetTexture(material, WunaOrbitFireShaderIds.NoiseTex, noise);
        }
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
            SunExpLog.Debug(LogPrefix + " texture read skipped for unsupported material property: " + material.name + " (" + ex.Message + ")");
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
            SunExpLog.Debug(LogPrefix + " texture assignment skipped for unsupported material property: " + material.name + " (" + ex.Message + ")");
        }
    }

    private static bool IsOrbitFireShader(Material material)
    {
        return string.Equals(material.shader?.name ?? "", WunaOrbitFireShaderIds.ShaderName, StringComparison.Ordinal);
    }
}
