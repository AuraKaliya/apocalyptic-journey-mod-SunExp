using System;
using System.Collections;
using System.Reflection;
using AuraCg.Shared;
using AuraGameData.Shared.GameApi;
using AuraRole.Shared;
using AuraShared.Core;
using AuraSkin.Shared;
using StarterDeckArbiter.Shared;
using Witch.Mod;
using SunExp.Dll.Features.SkillCg;
using SunExp.Dll.Features.Director;
using SunExp.Dll.Features;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;
using UiTransitionGuardShared;

namespace SunExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        RunStep("XLua assembly registration", RegisterLuaVisibleAssembly);
        RunStep("shared core", () => AuraSharedRuntime.Initialize(modConfig, "SunExp"));
        RunStep("shared game data", RegisterSharedGameData);
        RunStep("shared feature defaults", RegisterSharedFeatureDefaults);
        RunStep("rpc authority", () => SunExpRpcAuthorityRuntime.Initialize(modConfig));
        RunStep("shared resource package", () => RegisterSharedResourcePackage(modConfig));
        RunStep("role registry", () => AuraRoleRegistryRuntime.RegisterManifest(modConfig, "SunExp"));
        RunStep("visual registry", () => VisualRegistry.Load(modConfig));
        RunStep("director runtime", () => SunExpDirectorRuntime.Initialize(modConfig));
        RunStep("endless abyss config", () => EndlessAbyssConfigStore.Load(modConfig));
        RunStep("dimension shop config", () => DimensionShopConfigStore.Load(modConfig));
        RunStep("endless abyss evolution traits", () => EndlessAbyssEvolutionTraitRegistry.Load(modConfig));
        RunStep("card visual skin registry", CardVisualSkinApi.RegisterSunExpDefaults);
        RunStep("card visual effect registry", CardVisualEffectApi.RegisterSunExpDefaults);
        RunStep("card use effect runtime", () => SunExpCardUseFxRuntime.Initialize(modConfig));
        RunStep("CG registry", () => AuraCgRegistryRuntime.RegisterManifest(modConfig, "SunExp"));
        RunStep("skill CG runtime", () => SunExpSkillCgRuntime.Initialize(modConfig));
        RunStep("starter deck profiles", () => StarterDeckArbiterRuntime.RegisterProfileManifest(modConfig, "SunExp"));
        RunStep("shared skin runtime", () => AuraSkinRuntime.Initialize(modConfig, "SunExp"));
        RunStep("shared skin package", () => RegisterSkinPackage(modConfig));
        RunStep("journey runtime", () => SolarMemoryJourneyApi.Initialize(modConfig));
        RunStep("mode runtime", () => SunExpModeApi.Initialize(modConfig));
        RunStep("audio runtime", () => AudioApi.Initialize(modConfig));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, "SunExp"));
        RunStep("performance runtime", () => SunExpFrameScheduler.Initialize(modConfig));
        SunExpLog.Info("SunExp C# entry loaded");
        RunStep("gameplay hooks", () => RuntimeHooks.Initialize(modConfig));
        RunStep("special tags", SpecialTagRuntime.Initialize);
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => SunExpLog.Error("Initialization step failed: " + step, ex));
    }

    private static void RegisterSkinPackage(ModConfig modConfig)
    {
        if (!AuraSkinRuntime.RegisterPackage(modConfig, "SunExp"))
        {
            SunExpLog.Warn("SunExp bundled skin package was rejected; skin package registration skipped.");
        }
    }

    private static void RegisterSharedFeatureDefaults()
    {
        AuraFeatureSwitchRuntime.RegisterFeature(SunExpIds.ModId, "Battle.StartTraitBuffs", defaultEnabled: true, "SunExp default");
        AuraFeatureSwitchRuntime.RegisterFeature(SunExpIds.ModId, "Battle.OpeningDirector", defaultEnabled: true, "SunExp default");
        AuraFeatureSwitchRuntime.RegisterFeature(SunExpIds.ModId, "SolarMemory", defaultEnabled: true, "SunExp default");
    }

    private static void RegisterSharedGameData()
    {
        var result = AuraGameDataHostApi.RegisterNativeOwnershipV5("SunExp", "SunExp_");
        if (!result.Success)
        {
            throw new InvalidOperationException("SunExp v5 game-data ownership registration failed: " + result.Message);
        }
    }

    private static void RegisterSharedResourcePackage(ModConfig modConfig)
    {
        var result = AuraSharedResourceBootstrapper.Bootstrap(modConfig, "SunExp");
        foreach (var response in result.Responses)
        {
            if (!response.Success)
            {
                throw new InvalidOperationException("SunExp shared resource package was rejected: " + response.Message);
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
            SunExpLog.Warn("Unable to register C# script assembly for XLua: LuaEnv is null");
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
                SunExpLog.Warn("Unable to register C# script assembly for XLua: translator assembly list missing");
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
                "assert(xlua.import_type('SunExp.Dll.Scripting.CardScripts'), 'SunExp CardScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.WunaScripts'), 'SunExp WunaScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.LoneerScripts'), 'SunExp LoneerScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.EventScripts'), 'SunExp EventScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.BossScripts'), 'SunExp BossScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.ProjectionScripts'), 'SunExp ProjectionScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.HeartChangeScripts'), 'SunExp HeartChangeScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.BuffScripts'), 'SunExp BuffScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.RelicScripts'), 'SunExp RelicScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.FamiliarGrowthScripts'), 'SunExp FamiliarGrowthScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.DuskPartnerScripts'), 'SunExp DuskPartnerScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.StarClayDollScripts'), 'SunExp StarClayDollScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.ElementalScripts'), 'SunExp ElementalScripts unavailable');"
                + "assert(xlua.import_type('SunExp.Dll.Scripting.ColumbinaScripts'), 'SunExp ColumbinaScripts unavailable');",
                "SunExp.RegisterLuaVisibleAssembly");
            SunExpLog.Info("Registered C# script assembly for XLua: " + assemblyName);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Failed to register C# script assembly for XLua: " + assemblyName, ex);
        }
    }
}
