using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class ShaderAssetLoader
{
    public static Shader? ResolveShader(ShaderVisualSpec? spec, string fallbackShaderName, string logPrefix)
    {
        var shader = TryLoadBundleMaterialShader(spec, logPrefix)
            ?? TryLoadBundleShader(spec, logPrefix)
            ?? TryFindShader(spec?.ShaderName, logPrefix)
            ?? TryLoadShader(spec?.ShaderPath, logPrefix)
            ?? TryLoadMaterialShader(spec?.MaterialPath, logPrefix)
            ?? TryFindShader(fallbackShaderName, logPrefix);

        var bundlePath = spec?.BundlePath;
        if (shader == null && !string.IsNullOrWhiteSpace(bundlePath))
        {
            SunExpLog.Debug(logPrefix + " shader bundle declared but no loaded shader was found: " + bundlePath);
        }

        return shader;
    }

    private static Shader? TryLoadBundleMaterialShader(ShaderVisualSpec? spec, string logPrefix)
    {
        if (spec == null)
        {
            return null;
        }

        return AssetBundleCache.LoadAsset<Material>(spec.BundlePath, spec.MaterialPath, logPrefix)?.shader;
    }

    private static Shader? TryLoadBundleShader(ShaderVisualSpec? spec, string logPrefix)
    {
        if (spec == null)
        {
            return null;
        }

        return AssetBundleCache.LoadAsset<Shader>(spec.BundlePath, spec.ShaderPath, logPrefix);
    }

    private static Shader? TryFindShader(string? shaderName, string logPrefix)
    {
        var resolvedName = shaderName?.Trim();
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            return null;
        }

        try
        {
            return Shader.Find(resolvedName);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " shader lookup failed: " + resolvedName + " (" + ex.Message + ")");
            return null;
        }
    }

    private static Shader? TryLoadShader(string? path, string logPrefix)
    {
        var resolvedPath = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        try
        {
            return SunExpResourceCache.Load<Shader>(resolvedPath, true);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " shader asset load failed: " + resolvedPath + " (" + ex.Message + ")");
            return null;
        }
    }

    private static Shader? TryLoadMaterialShader(string? path, string logPrefix)
    {
        var resolvedPath = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        try
        {
            return SunExpResourceCache.Load<Material>(resolvedPath, true)?.shader;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn(logPrefix + " shader material load failed: " + resolvedPath + " (" + ex.Message + ")");
            return null;
        }
    }
}
