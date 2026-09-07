using System;
using System.Reflection;
using AuraShared.Core;

namespace Terrias.Dll.Infrastructure;

public static class TerriasPerformanceSettings
{
    public const string SharedDiagnosticsOwnerId = "AuraShared";
    public const string SharedDiagnosticsFeatureId = "Diagnostics.Performance";
    public const string UiPoolingFeatureId = "UI.ObjectPooling";
    public const string FieldVisualFeatureId = "Field.EnvironmentPresentation";

    private const string LegacyCountersKey = "TerriasPerfCounters";
    private const string WunaOrbitFireEnabledKey = "TerriasWunaOrbitFireEnabled";
    private const string WunaOrbitFireDisabledKey = "TerriasWunaOrbitFireDisabled";
    private const string LegacyUiPoolKey = "TerriasUiPool";
    private const int RefreshMilliseconds = 1000;

    private static MethodInfo? getGameVarMethod;
    private static bool gameVarMethodResolved;
    private static bool featureDefaultsRegistered;
    private static int lastRefreshTick = int.MinValue;
    private static bool cachedCountersEnabled;
    private static bool cachedWunaOrbitFireEnabled;
    private static bool cachedUiPoolEnabled = true;
    private static bool cachedFieldVisualsEnabled = true;

    public static bool FieldVisualsEnabled
    {
        get { RefreshIfNeeded(); return cachedFieldVisualsEnabled; }
    }

    public static float FieldVisualGeometryInterval(bool lowQuality, bool reducedMotion) =>
        reducedMotion ? 1f / 12f : lowQuality ? 1f / 15f : 1f / 30f;

    public static int FieldVisualParticleBudget(bool lowQuality) => lowQuality ? 12 : 36;

    public static bool CountersEnabled
    {
        get
        {
            RefreshIfNeeded();
            return cachedCountersEnabled;
        }
    }

    public static bool WunaOrbitFireEnabled
    {
        get
        {
            RefreshIfNeeded();
            return cachedWunaOrbitFireEnabled;
        }
    }

    public static bool UiPoolEnabled
    {
        get
        {
            RefreshIfNeeded();
            return cachedUiPoolEnabled;
        }
    }

    public static int UiPoolCapacityPerKey => 64;

    public static int FrameSchedulerBudget => 32;

    public static int CardUseFxRibbonSamples => 32;

    public static int WunaCoreSections => 96;

    public static int WunaDetailTongues => 3;

    public static int WunaDetailSparks => 3;

    public static int WunaOrbitFlamesPerRail => 3;

    public static int WunaAlphaSampleGrid => 96;

    public static void RegisterFeatureDefaults()
    {
        if (featureDefaultsRegistered)
        {
            return;
        }

        featureDefaultsRegistered = true;
        AuraFeatureSwitchRuntime.RegisterFeature(TerriasIds.ModId, FieldVisualFeatureId,
            defaultEnabled: true, "Terrias field presentation default");
        AuraFeatureSwitchRuntime.RegisterFeature(
            SharedDiagnosticsOwnerId,
            SharedDiagnosticsFeatureId,
            defaultEnabled: false,
            "AuraShared diagnostics default");
        AuraFeatureSwitchRuntime.RegisterFeature(
            TerriasIds.ModId,
            UiPoolingFeatureId,
            defaultEnabled: true,
            "Terrias UI pooling default");
        lastRefreshTick = int.MinValue;
    }

    public static void Refresh()
    {
        lastRefreshTick = int.MinValue;
        RefreshIfNeeded();
    }

    public static float WunaGeometryInterval(bool activePulse)
    {
        return activePulse ? 1f / 30f : 1f / 18f;
    }

    public static string DiagnosticsSummary()
    {
        RefreshIfNeeded();
        return "Terrias diagnostics: sharedPerformance="
            + AuraFeatureSwitchRuntime.IsEnabled(SharedDiagnosticsOwnerId, SharedDiagnosticsFeatureId)
            + "; "
            + LegacyCountersKey
            + " raw="
            + FormatRawValue(ReadGameVarSafe(LegacyCountersKey))
            + " effective="
            + cachedCountersEnabled
            + "; TerriasDebug raw="
            + FormatRawValue(ReadGameVarSafe("TerriasDebug"))
            + " effective="
            + ReadFlagSafe("TerriasDebug", false)
            + "; "
            + LegacyUiPoolKey
            + " raw="
            + FormatRawValue(ReadGameVarSafe(LegacyUiPoolKey))
            + " effective="
            + cachedUiPoolEnabled
            + "; "
            + WunaOrbitFireEnabledKey
            + " raw="
            + FormatRawValue(ReadGameVarSafe(WunaOrbitFireEnabledKey))
            + " effective="
            + cachedWunaOrbitFireEnabled;
    }

    private static void RefreshIfNeeded()
    {
        var now = Environment.TickCount;
        if ((uint)(now - lastRefreshTick) < RefreshMilliseconds)
        {
            return;
        }

        RegisterFeatureDefaults();
        try
        {
            cachedCountersEnabled = AuraFeatureSwitchRuntime.IsEnabled(
                                        SharedDiagnosticsOwnerId,
                                        SharedDiagnosticsFeatureId)
                                    || ReadEnabledOnlyFlag(LegacyCountersKey);
            cachedWunaOrbitFireEnabled = ReadFlag(WunaOrbitFireEnabledKey, false)
                && !ReadFlag(WunaOrbitFireDisabledKey, false);
            cachedUiPoolEnabled = AuraFeatureSwitchRuntime.IsEnabled(TerriasIds.ModId, UiPoolingFeatureId)
                                  && ReadDefaultOnFlag(LegacyUiPoolKey);
            cachedFieldVisualsEnabled = AuraFeatureSwitchRuntime.IsEnabled(TerriasIds.ModId, FieldVisualFeatureId)
                                        && ReadDefaultOnFlag("TerriasFieldVisuals");
        }
        catch
        {
            cachedCountersEnabled = false;
            cachedWunaOrbitFireEnabled = false;
            cachedUiPoolEnabled = true;
            cachedFieldVisualsEnabled = true;
        }

        lastRefreshTick = now;
    }

    private static bool ReadDefaultOnFlag(string key)
    {
        var text = ReadGameVar(key).Trim();
        // Missing GameVars and stored zero both read as "0" in the game API.
        // Default-on local presentation features use explicit false/off text
        // for opt-out and treat the ambiguous zero as missing.
        return text.Length == 0 || text == "0" || ReadFlagText(text, true);
    }

    private static bool ReadEnabledOnlyFlag(string key)
    {
        var text = ReadGameVar(key).Trim();
        return text == "1"
               || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadFlag(string key, bool fallback)
    {
        return ReadFlagText(ReadGameVar(key).Trim(), fallback);
    }

    private static bool ReadFlagText(string text, bool fallback)
    {
        if (text.Length == 0)
        {
            return fallback;
        }

        if (text == "0"
            || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text == "1"
            || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fallback;
    }

    private static string ReadGameVarSafe(string key)
    {
        try
        {
            return ReadGameVar(key);
        }
        catch
        {
            return "<error>";
        }
    }

    private static bool ReadFlagSafe(string key, bool fallback)
    {
        try
        {
            return ReadFlag(key, fallback);
        }
        catch
        {
            return fallback;
        }
    }

    private static string FormatRawValue(string value)
    {
        return "'" + (string.IsNullOrEmpty(value) ? "<empty>" : value) + "'";
    }

    private static string ReadGameVar(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        if (!gameVarMethodResolved)
        {
            var playerInfo = typeof(ScriptExecutor).GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);
            getGameVarMethod = playerInfo?.GetMethod("GetGameVar", BindingFlags.Public | BindingFlags.Static);
            gameVarMethodResolved = true;
        }

        var value = getGameVarMethod?.Invoke(null, new object[] { key });
        return Convert.ToString(value) ?? "";
    }
}
