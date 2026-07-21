using System;
using System.Collections.Generic;
using AuraJourney.Shared;
using AuraMode.Shared;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class TerriasModeApi
{
    public static void Initialize(ModConfig modConfig)
    {
        AuraModeRuntime.Initialize(modConfig, TerriasIds.ModId);
        Register(CreateSolarMemoryDefinition());
        Register(CreateEndlessAbyssDefinition());
    }

    public static AuraModeTransitionResult ActivateSolarMemory(SaveInfo saveInfo, string source)
    {
        var result = Activate(TerriasIds.SolarMemorySemanticModeId, saveInfo, source);
        if (result.Success)
        {
            AuraJourneyRuntime.PublishActiveMode(
                TerriasIds.ModId,
                SolarMemoryJourneyApi.JourneyId,
                TerriasIds.SolarMemorySemanticModeId,
                true,
                source);
        }
        return result;
    }

    public static AuraModeTransitionResult ActivateEndlessAbyss(SaveInfo saveInfo, string source)
    {
        return Activate(TerriasIds.EndlessAbyssSemanticModeId, saveInfo, source);
    }

    public static void ReconcileSelectedSave(string source)
    {
        var preferCurrentRun = source.IndexOf("MapManager", StringComparison.OrdinalIgnoreCase) >= 0;
        var saveInfo = preferCurrentRun
            ? GameSaveManager.GetNowSave() ?? GameEntryUI.selectedSave
            : GameEntryUI.selectedSave ?? GameSaveManager.GetNowSave();
        if (IsFlagSet(saveInfo, TerriasIds.SolarMemoryModeKey))
        {
            ActivateSolarMemory(saveInfo!, source);
            return;
        }
        if (IsFlagSet(saveInfo, TerriasIds.EndlessSeaModeKey))
        {
            ActivateEndlessAbyss(saveInfo!, source);
            return;
        }

        var current = AuraModeRuntime.Current(TerriasIds.ModId, refresh: true);
        if (current == null
            || !string.Equals(current.OwnerModId, TerriasIds.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = AuraModeRuntime.DeactivateMode(
            TerriasIds.ModId,
            current.ModeId,
            "",
            source);
        if (result.Success
            && string.Equals(current.ModeId, TerriasIds.SolarMemorySemanticModeId, StringComparison.OrdinalIgnoreCase))
        {
            AuraJourneyRuntime.PublishActiveMode(
                TerriasIds.ModId,
                SolarMemoryJourneyApi.JourneyId,
                TerriasIds.SolarMemorySemanticModeId,
                false,
                source);
        }
    }

    private static AuraModeTransitionResult Activate(string modeId, SaveInfo saveInfo, string source)
    {
        var runId = RunId(saveInfo);
        return AuraModeRuntime.ActivateMode(
            TerriasIds.ModId,
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
        var result = AuraModeRuntime.RegisterMode(TerriasIds.ModId, definition);
        if (!result.Success)
        {
            TerriasLog.Warn("Mode registration failed: " + definition.ModeId + " -> " + result.Message);
        }
    }

    private static AuraModeDefinition CreateSolarMemoryDefinition()
    {
        return new AuraModeDefinition
        {
            ModeId = TerriasIds.SolarMemorySemanticModeId,
            OwnerModId = TerriasIds.ModId,
            Aliases = new List<string>
            {
                "solar-memory",
                "Terrias.SolarMemory",
                "Terrias_SolarMemoryMode"
            },
            Display = new AuraModeDisplay
            {
                NameKey = "Terrias.Mode.SolarMemory",
                FallbackName = TerriasIds.SolarMemoryTitle
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
            ModeId = TerriasIds.EndlessAbyssSemanticModeId,
            OwnerModId = TerriasIds.ModId,
            Aliases = new List<string>
            {
                "Terrias.EndlessSea",
                "TerriasEndlessSea",
                "Terrias_EndlessSeaMode",
                "endless-abyss"
            },
            Display = new AuraModeDisplay
            {
                NameKey = "Terrias.Mode.EndlessAbyss",
                FallbackName = TerriasIds.EndlessSeaTitle
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
            NativeModeType = TerriasIds.NativeNormalModeType,
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
                ProviderId = TerriasIds.ModId
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
        if (IsFlagSet(saveInfo, TerriasIds.EndlessSeaModeKey)
            && saveInfo.GameVars.TryGetValue(TerriasIds.EndlessSeaRunIdKey, out var endlessRunId)
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
