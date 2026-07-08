using System;
using System.Reflection;

namespace SunExp.Dll.Infrastructure;

public static class SunExpPerformanceSettings
{
    private const string CountersKey = "SunExpPerfCounters";
    private const string WunaOrbitFireEnabledKey = "SunExpWunaOrbitFireEnabled";
    private const string WunaOrbitFireDisabledKey = "SunExpWunaOrbitFireDisabled";
    private const string UiPoolKey = "SunExpUiPool";
    private const int RefreshMilliseconds = 1000;

    private static MethodInfo? getGameVarMethod;
    private static bool gameVarMethodResolved;
    private static int lastRefreshTick = int.MinValue;
    private static bool cachedCountersEnabled;
    private static bool cachedWunaOrbitFireEnabled;
    private static bool cachedUiPoolEnabled = true;

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

    public static bool CardFaceEffectsEnabled => true;

    public static float CardFaceEffectQualityScale => 0.86f;

    public static bool CardFrameEffectsEnabled => true;

    public static float CardFrameEffectQualityScale => CardFaceEffectQualityScale;

    public static int WunaCoreSections => 96;

    public static int WunaDetailTongues => 3;

    public static int WunaDetailSparks => 3;

    public static int WunaOrbitFlamesPerRail => 3;

    public static int WunaAlphaSampleGrid => 96;

    public static float WunaGeometryInterval(bool activePulse)
    {
        return activePulse ? 1f / 30f : 1f / 18f;
    }

    private static void RefreshIfNeeded()
    {
        var now = Environment.TickCount;
        if ((uint)(now - lastRefreshTick) < RefreshMilliseconds)
        {
            return;
        }

        try
        {
            cachedCountersEnabled = ReadFlag(CountersKey, false);
            cachedWunaOrbitFireEnabled = ReadFlag(WunaOrbitFireEnabledKey, false)
                && !ReadFlag(WunaOrbitFireDisabledKey, false);
            cachedUiPoolEnabled = ReadFlag(UiPoolKey, true);
        }
        catch
        {
            cachedCountersEnabled = false;
            cachedWunaOrbitFireEnabled = false;
            cachedUiPoolEnabled = true;
        }

        lastRefreshTick = now;
    }

    private static bool ReadFlag(string key, bool fallback)
    {
        var text = ReadGameVar(key).Trim();
        if (text.Length == 0)
        {
            return fallback;
        }

        return text == "1"
            || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
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
