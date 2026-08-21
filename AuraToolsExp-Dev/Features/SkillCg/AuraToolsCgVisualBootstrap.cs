using System;
using AuraCg.Shared;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.SkillCg;

internal static class AuraToolsCgVisualBootstrap
{
    private const string BundlePath = "Mods/AuraToolsExp/ModResource/VisualBundles/auratools_visuals";
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            var bundle = AuraToolsVisualBundleRuntime.LoadBundle(BundlePath);
            if (bundle == null)
            {
                AuraToolsLog.Warn("[SkillCG] AuraTools visual bundle is missing or invalid: " + BundlePath);
                return;
            }

            SkillCgArbiterRuntime.RegisterAssetBundle(BundlePath, bundle);
            RegisterMaterial("AuraCg/LumaKeyUI", "AuraCgLumaKeyUI");
            RegisterMaterial("AuraCg/MaskedInvertFlash", "AuraCgMaskedInvertFlash");
            RegisterMaterial("AuraCg/ScreenBwFlash", "AuraCgScreenBwFlash");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SkillCG] AuraTools visual bundle initialization failed: " + ex.Message);
        }
    }

    private static void RegisterMaterial(string shaderName, string assetName)
    {
        var material = AuraToolsVisualBundleRuntime.LoadAsset<Material>(BundlePath, assetName);

        if (material == null)
        {
            AuraToolsLog.Warn("[SkillCG] AuraTools CG material is missing: " + assetName);
            return;
        }

        SkillCgArbiterRuntime.RegisterMaterial(shaderName, material);
    }
}
