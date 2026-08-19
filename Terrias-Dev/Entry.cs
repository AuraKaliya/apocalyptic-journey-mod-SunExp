using System;
using System.Collections;
using System.Reflection;
using AuraCg.Shared;
using AuraGameData.Shared.GameApi;
using AuraRole.Shared;
using AuraShared.Core;
using AuraSkin.Shared;
using Witch.Mod;
using Terrias.Dll.Features.SkillCg;
using Terrias.Dll.Features.Director;
using Terrias.Dll.Features;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using UiTransitionGuardShared;

namespace Terrias.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RunStep("XLua assembly registration", RegisterLuaVisibleAssembly);
        RunStep("shared core", () => AuraSharedRuntime.Initialize(modConfig, "Terrias"));
        RunStep("shared game data", RegisterSharedGameData);
        RunStep("shared feature defaults", RegisterSharedFeatureDefaults);
        RunStep("rpc authority", () => TerriasRpcAuthorityRuntime.Initialize(modConfig));
        RunStep("shared resource package", () => RegisterSharedResourcePackage(modConfig));
        RunStep("localization catalog", () => TerriasTextCatalog.Load(modConfig));
        RunStep("role registry", () => AuraRoleRegistryRuntime.RegisterManifest(modConfig, "Terrias"));
        RunStep("visual registry", () => VisualRegistry.Load(modConfig));
        RunStep("director runtime", () => TerriasDirectorRuntime.Initialize(modConfig));
        RunStep("endless abyss config", () => EndlessAbyssConfigStore.Load(modConfig));
        RunStep("dimension shop config", () => DimensionShopConfigStore.Load(modConfig));
        RunStep("endless abyss evolution traits", () => EndlessAbyssEvolutionTraitRegistry.Load(modConfig));
        RunStep("card visual skin registry", CardVisualSkinApi.RegisterTerriasDefaults);
        RunStep("card visual effect registry", CardVisualEffectApi.RegisterTerriasDefaults);
        RunStep("card use effect runtime", () => TerriasCardUseFxRuntime.Initialize(modConfig));
        RunStep("CG registry", () => AuraCgRegistryRuntime.RegisterManifest(modConfig, "Terrias"));
        RunStep("skill CG runtime", () => TerriasSkillCgRuntime.Initialize(modConfig));
        RunStep("shared skin runtime", () => AuraSkinRuntime.Initialize(modConfig, "Terrias"));
        RunStep("shared skin package", () => RegisterSkinPackage(modConfig));
        RunStep("journey runtime", () => SolarMemoryJourneyApi.Initialize(modConfig));
        RunStep("mode runtime", () => TerriasModeApi.Initialize(modConfig));
        RunStep("audio runtime", () => AudioApi.Initialize(modConfig));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, "Terrias"));
        RunStep("performance runtime", () => TerriasFrameScheduler.Initialize(modConfig));
        TerriasLog.Info("Terrias C# entry loaded");
        RunStep("gameplay hooks", () => RuntimeHooks.Initialize(modConfig));
        RunStep("special tags", SpecialTagRuntime.Initialize);
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => TerriasLog.Error("Initialization step failed: " + step, ex));
    }

    private static void RegisterSkinPackage(ModConfig modConfig)
    {
        if (!AuraSkinRuntime.RegisterPackage(modConfig, "Terrias"))
        {
            TerriasLog.Warn("Terrias bundled skin package was rejected; skin package registration skipped.");
        }
    }

    private static void RegisterSharedFeatureDefaults()
    {
        TerriasPerformanceSettings.RegisterFeatureDefaults();
        AuraFeatureSwitchRuntime.RegisterFeature(TerriasIds.ModId, "Battle.StartTraitBuffs", defaultEnabled: true, "Terrias default");
        AuraFeatureSwitchRuntime.RegisterFeature(TerriasIds.ModId, "Battle.OpeningDirector", defaultEnabled: true, "Terrias default");
        AuraFeatureSwitchRuntime.RegisterFeature(TerriasIds.ModId, "SolarMemory", defaultEnabled: true, "Terrias default");
    }

    private static void RegisterSharedGameData()
    {
        var result = AuraGameDataHostApi.RegisterNativeOwnershipV5("Terrias", "Terrias_");
        if (!result.Success)
        {
            throw new InvalidOperationException("Terrias v5 game-data ownership registration failed: " + result.Message);
        }
    }

    private static void RegisterSharedResourcePackage(ModConfig modConfig)
    {
        var result = AuraSharedResourceBootstrapper.Bootstrap(modConfig, "Terrias");
        foreach (var response in result.Responses)
        {
            if (!response.Success)
            {
                throw new InvalidOperationException("Terrias shared resource package was rejected: " + response.Message);
            }
        }
    }

    private static void RegisterLuaVisibleAssembly()
    {
        var assembly = typeof(Entry).Assembly;
        var assemblyName = assembly.GetName().Name;
        var luaEnv = ScriptExecutor.luaEnv;
        if (luaEnv == null)
        {
            TerriasLog.Warn("Unable to register C# script assembly for XLua: LuaEnv is null");
            return;
        }

        try
        {
            var translator = luaEnv.GetType()
                .GetField("translator", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(luaEnv);
            var assemblies = translator?.GetType()
                .GetField("assemblies", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(translator) as IList;

            if (assemblies == null)
            {
                TerriasLog.Warn("Unable to register C# script assembly for XLua: translator assembly list missing");
                return;
            }

            var alreadyRegistered = false;
            foreach (var item in assemblies)
            {
                if (item is Assembly existing && existing.FullName == assembly.FullName)
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                assemblies.Add(assembly);
            }

            luaEnv.DoString(
                "assert(xlua.import_type('Terrias.Dll.Scripting.CardScripts'), 'Terrias CardScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.WunaScripts'), 'Terrias WunaScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.LoneerScripts'), 'Terrias LoneerScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.EventScripts'), 'Terrias EventScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.BossScripts'), 'Terrias BossScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.ProjectionScripts'), 'Terrias ProjectionScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.BuffScripts'), 'Terrias BuffScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.RelicScripts'), 'Terrias RelicScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.FamiliarGrowthScripts'), 'Terrias FamiliarGrowthScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.DuskPartnerScripts'), 'Terrias DuskPartnerScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.StarClayDollScripts'), 'Terrias StarClayDollScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.ElementalScripts'), 'Terrias ElementalScripts unavailable');"
                + "assert(xlua.import_type('Terrias.Dll.Scripting.ColumbinaScripts'), 'Terrias ColumbinaScripts unavailable');",
                "Terrias.RegisterLuaVisibleAssembly");
            TerriasLog.Info("Registered C# script assembly for XLua: " + assemblyName);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Failed to register C# script assembly for XLua: " + assemblyName, ex);
        }
    }
}
