using System.Collections.Generic;
using System.IO;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

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
                TerriasLog.Info(LogPrefix + " found visual bundle: " + bundlePath);
                continue;
            }

            if (LoggedMissingBundles.Add(resolved))
            {
                TerriasLog.Warn(LogPrefix + " missing declared visual bundle: " + bundlePath + " -> " + resolved);
            }
        }

        ValidateWunaMaterials();
        ValidateCardFaceMaterials();
    }

    private static void ValidateWunaMaterials()
    {
        var back = AssetBundleCache.LoadAsset<Material>(
            "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
            "Terrias/Materials/WunaOrbitFireBack",
            LogPrefix);
        var front = AssetBundleCache.LoadAsset<Material>(
            "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
            "Terrias/Materials/WunaOrbitFireFront",
            LogPrefix);
        if (back != null && front != null)
        {
            TerriasLog.Info(LogPrefix + " Wuna orbit fire materials loaded from bundle.");
        }
    }

    private static void ValidateCardFaceMaterials()
    {
        var material = AssetBundleCache.LoadAsset<Material>(
            "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
            "Terrias/Materials/CardUseStardust",
            LogPrefix);
        if (material != null)
        {
            TerriasLog.Info(LogPrefix + " Star Score card-use effect material loaded from bundle.");
        }
    }
}
