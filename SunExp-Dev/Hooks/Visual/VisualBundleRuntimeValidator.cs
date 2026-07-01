using System.Collections.Generic;
using System.IO;
using AuraCg.Shared;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class VisualBundleRuntimeValidator
{
    private const string LogPrefix = "[VisualBundle]";
    private static readonly HashSet<string> LoggedMissingBundles = new(System.StringComparer.OrdinalIgnoreCase);

    public static void ValidateDeclaredBundles()
    {
        foreach (var bundlePath in VisualRegistry.BundlePaths())
        {
            var resolved = VisualRegistry.ResolveContentPath(bundlePath);
            if (File.Exists(resolved))
            {
                SunExpLog.Info(LogPrefix + " found visual bundle: " + bundlePath);
                continue;
            }

            if (LoggedMissingBundles.Add(resolved))
            {
                SunExpLog.Warn(LogPrefix + " missing declared visual bundle: " + bundlePath + " -> " + resolved);
            }
        }

        ValidateWunaMaterials();
        ValidateCardFaceMaterials();
        RegisterAuraCgMaterials();
    }

    private static void ValidateWunaMaterials()
    {
        var back = AssetBundleCache.LoadAsset<Material>(
            "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
            "SunExp/Materials/WunaOrbitFireBack",
            LogPrefix);
        var front = AssetBundleCache.LoadAsset<Material>(
            "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
            "SunExp/Materials/WunaOrbitFireFront",
            LogPrefix);
        if (back != null && front != null)
        {
            SunExpLog.Info(LogPrefix + " Wuna orbit fire materials loaded from bundle.");
        }
    }

    private static void ValidateCardFaceMaterials()
    {
        var material = AssetBundleCache.LoadAsset<Material>(
            "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
            "SunExp/Materials/CardFaceEffect",
            LogPrefix);
        if (material != null)
        {
            SunExpLog.Info(LogPrefix + " card face effect material loaded from bundle.");
        }
    }

    private static void RegisterAuraCgMaterials()
    {
        const string bundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals";
        var bundle = AssetBundleCache.LoadBundle(bundlePath, LogPrefix);
        if (bundle != null)
        {
            SkillCgArbiterRuntime.RegisterAssetBundle(bundlePath, bundle);
        }

        var lumaKey = AssetBundleCache.LoadAsset<Material>(
            bundlePath,
            "AuraCgLumaKeyUI",
            LogPrefix);
        var maskedInvert = AssetBundleCache.LoadAsset<Material>(
            bundlePath,
            "AuraCgMaskedInvertFlash",
            LogPrefix);
        var screenBwFlash = AssetBundleCache.LoadAsset<Material>(
            bundlePath,
            "AuraCgScreenBwFlash",
            LogPrefix);

        if (lumaKey != null)
        {
            SkillCgArbiterRuntime.RegisterMaterial("AuraCg/LumaKeyUI", lumaKey);
        }

        if (maskedInvert != null)
        {
            SkillCgArbiterRuntime.RegisterMaterial("AuraCg/MaskedInvertFlash", maskedInvert);
        }

        if (screenBwFlash != null)
        {
            SkillCgArbiterRuntime.RegisterMaterial("AuraCg/ScreenBwFlash", screenBwFlash);
        }

        if (lumaKey != null && maskedInvert != null && screenBwFlash != null && bundle != null)
        {
            SunExpLog.Info(LogPrefix + " Aura CG shader materials and bundle registered.");
        }
    }
}
