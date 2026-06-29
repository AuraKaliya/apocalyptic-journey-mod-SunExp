using System.Collections.Generic;
using System.IO;
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
}
