using System;
using System.Collections.Generic;
using AuraJourney.Shared;
using AuraMode.Shared;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class SunExpModeApi
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraModeRuntime.Initialize(modConfig, SunExpIds.ModId);
        Register(CreateSolarMemoryDefinition());
        Register(CreateEndlessAbyssDefinition());
    }

    public static AuraModeTransitionResult ActivateSolarMemory(SaveInfo saveInfo, string source)
    {
        var result = Activate(SunExpIds.SolarMemorySemanticModeId, saveInfo, source);
        if (result.Success)
        {
            AuraJourneyRuntime.PublishActiveMode(
                SunExpIds.ModId,
                SolarMemoryJourneyApi.JourneyId,
                SunExpIds.SolarMemorySemanticModeId,
                true,
                source);
        }
        return result;
    }

    public static AuraModeTransitionResult ActivateEndlessAbyss(SaveInfo saveInfo, string source)
    {
        return Activate(SunExpIds.EndlessAbyssSemanticModeId, saveInfo, source);
    }

    public static void ReconcileSelectedSave(string source)
    {
        var preferCurrentRun = source.IndexOf("MapManager", StringComparison.OrdinalIgnoreCase) >= 0;
        var saveInfo = preferCurrentRun
            ? GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave
            : GameEntryUI.selectedSave ?? GameSaveManager.GetNowSave();
        if (IsFlagSet(saveInfo, SunExpIds.SolarMemoryModeKey))
        {
            ActivateSolarMemory(saveInfo!, source);
            return;
        }
        if (IsFlagSet(saveInfo, SunExpIds.EndlessSeaModeKey))
        {
            ActivateEndlessAbyss(saveInfo!, source);
            return;
        }

        var current = AuraModeRuntime.Current(SunExpIds.ModId, refresh: true);
        if (current == null
            || !string.Equals(current.OwnerModId, SunExpIds.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = AuraModeRuntime.DeactivateMode(
            SunExpIds.ModId,
            current.ModeId,
            "",
            source);
        if (result.Success
            && string.Equals(current.ModeId, SunExpIds.SolarMemorySemanticModeId, StringComparison.OrdinalIgnoreCase))
        {
            AuraJourneyRuntime.PublishActiveMode(
                SunExpIds.ModId,
                SolarMemoryJourneyApi.JourneyId,
                SunExpIds.SolarMemorySemanticModeId,
                false,
                source);
        }
    }

    private static AuraModeTransitionResult Activate(string modeId, SaveInfo saveInfo, string source)
    {
        var runId = RunId(saveInfo);
        return AuraModeRuntime.ActivateMode(
            SunExpIds.ModId,
            modeId,
            new AuraModeRunBinding
            {
                RunId = runId,
                SaveSlotId = saveInfo.Name ?? "",
                StartedUtc = saveInfo.startTime == default ? "" : saveInfo.startTime.ToUniversalTime().ToString("O")
            },
            source);
    }

    private static void Register(AuraModeDefinition definition)
    {
        var result = AuraModeRuntime.RegisterMode(SunExpIds.ModId, definition);
        if (!result.Success)
        {
            SunExpLog.Warn("Mode registration failed: " + definition.ModeId + " -> " + result.Message);
        }
    }

    private static AuraModeDefinition CreateSolarMemoryDefinition()
    {
        return new AuraModeDefinition
        {
            ModeId = SunExpIds.SolarMemorySemanticModeId,
            OwnerModId = SunExpIds.ModId,
            Aliases = new List<string>
            {
                "solar-memory",
                "SunExp.SolarMemory",
                "SunExp_SolarMemoryMode"
            },
            Display = new AuraModeDisplay
            {
                NameKey = "SunExp.Mode.SolarMemory",
                FallbackName = SunExpIds.SolarMemoryTitle
            },
            Host = NativeNormalHost(),
            JourneyId = SolarMemoryJourneyApi.JourneyId,
            DefaultPolicies = ContentOwnedStarterDeckPolicy(),
            Capabilities = NativeCombatCapabilities(),
            Tags = new List<string> { "normal-hosted", "boss-rush", "journey" }
        };
    }

    private static AuraModeDefinition CreateEndlessAbyssDefinition()
    {
        return new AuraModeDefinition
        {
            ModeId = SunExpIds.EndlessAbyssSemanticModeId,
            OwnerModId = SunExpIds.ModId,
            Aliases = new List<string>
            {
                "SunExp.EndlessSea",
                "SunExpEndlessSea",
                "SunExp_EndlessSeaMode",
                "endless-abyss"
            },
            Display = new AuraModeDisplay
            {
                NameKey = "SunExp.Mode.EndlessAbyss",
                FallbackName = SunExpIds.EndlessSeaTitle
            },
            Host = NativeNormalHost(),
            DefaultPolicies = ContentOwnedStarterDeckPolicy(),
            Capabilities = NativeCombatCapabilities(),
            Tags = new List<string> { "normal-hosted", "endless" }
        };
    }

    private static AuraModeHost NativeNormalHost()
    {
        return new AuraModeHost
        {
            NativeModeType = SunExpIds.NativeNormalModeType,
            RuntimeManagerHint = "NormalMapManager"
        };
    }

    private static AuraModePolicies ContentOwnedStarterDeckPolicy()
    {
        return new AuraModePolicies
        {
            StarterDeck = new AuraModeStarterDeckPolicy
            {
                MutationAuthority = AuraModeStarterDeckAuthorities.ModeOwnerExclusive,
                ProviderId = SunExpIds.ModId
            }
        };
    }

    private static AuraModeCapabilities NativeCombatCapabilities()
    {
        return new AuraModeCapabilities
        {
            CombatContractId = AuraModeCombatContracts.NativeCombatV1
        };
    }

    private static string RunId(SaveInfo saveInfo)
    {
        if (IsFlagSet(saveInfo, SunExpIds.EndlessSeaModeKey)
            && saveInfo.GameVars.TryGetValue(SunExpIds.EndlessSeaRunIdKey, out var endlessRunId)
            && !string.IsNullOrWhiteSpace(endlessRunId))
        {
            return endlessRunId.Trim();
        }
        return string.IsNullOrWhiteSpace(saveInfo.Name) ? saveInfo.Seed ?? "" : saveInfo.Name.Trim();
    }

    private static bool IsFlagSet(SaveInfo? saveInfo, string key)
    {
        return saveInfo?.GameVars != null
               && saveInfo.GameVars.TryGetValue(key, out var value)
               && value == "1";
    }
}
