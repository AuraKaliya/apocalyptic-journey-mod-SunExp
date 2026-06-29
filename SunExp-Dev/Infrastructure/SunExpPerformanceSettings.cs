using System;
using System.Reflection;

namespace SunExp.Dll.Infrastructure;

public static class SunExpPerformanceSettings
{
    private const string QualityKey = "SunExpPerformanceQuality";
    private const string ShortQualityKey = "SunExpPerfQuality";
    private const string LowSpecKey = "SunExpLowSpec";
    private const string CountersKey = "SunExpPerfCounters";
    private const string WunaOrbitFireKey = "SunExpWunaOrbitFire";
    private const int RefreshMilliseconds = 1000;

    private static MethodInfo? getGameVarMethod;
    private static bool gameVarMethodResolved;
    private static int lastRefreshTick = int.MinValue;
    private static SunExpPerformanceQuality cachedQuality = SunExpPerformanceQuality.Balanced;
    private static bool cachedCountersEnabled;
    private static bool cachedWunaOrbitFireEnabled = true;

    public static SunExpPerformanceQuality Quality
    {
        get
        {
            RefreshIfNeeded();
            return cachedQuality;
        }
    }

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
            return cachedWunaOrbitFireEnabled && cachedQuality != SunExpPerformanceQuality.UltraLow;
        }
    }

    public static int FrameSchedulerBudget => Quality switch
    {
        SunExpPerformanceQuality.High => 48,
        SunExpPerformanceQuality.Balanced => 32,
        SunExpPerformanceQuality.Low => 16,
        SunExpPerformanceQuality.UltraLow => 8,
        _ => 32
    };

    public static int WunaCoreSections => Quality switch
    {
        SunExpPerformanceQuality.High => 96,
        SunExpPerformanceQuality.Balanced => 72,
        SunExpPerformanceQuality.Low => 48,
        SunExpPerformanceQuality.UltraLow => 0,
        _ => 72
    };

    public static int WunaDetailTongues => Quality switch
    {
        SunExpPerformanceQuality.High => 3,
        SunExpPerformanceQuality.Balanced => 2,
        SunExpPerformanceQuality.Low => 1,
        SunExpPerformanceQuality.UltraLow => 0,
        _ => 2
    };

    public static int WunaDetailSparks => Quality switch
    {
        SunExpPerformanceQuality.High => 3,
        SunExpPerformanceQuality.Balanced => 1,
        SunExpPerformanceQuality.Low => 0,
        SunExpPerformanceQuality.UltraLow => 0,
        _ => 1
    };

    public static int WunaOrbitFlamesPerRail => Quality switch
    {
        SunExpPerformanceQuality.High => 3,
        SunExpPerformanceQuality.Balanced => 2,
        SunExpPerformanceQuality.Low => 1,
        SunExpPerformanceQuality.UltraLow => 0,
        _ => 2
    };

    public static int WunaAlphaSampleGrid => Quality switch
    {
        SunExpPerformanceQuality.High => 96,
        SunExpPerformanceQuality.Balanced => 72,
        SunExpPerformanceQuality.Low => 48,
        SunExpPerformanceQuality.UltraLow => 32,
        _ => 72
    };

    public static float WunaGeometryInterval(bool activePulse)
    {
        return Quality switch
        {
            SunExpPerformanceQuality.High => activePulse ? 1f / 30f : 1f / 18f,
            SunExpPerformanceQuality.Balanced => activePulse ? 1f / 24f : 1f / 14f,
            SunExpPerformanceQuality.Low => activePulse ? 1f / 16f : 1f / 8f,
            SunExpPerformanceQuality.UltraLow => 1f,
            _ => activePulse ? 1f / 24f : 1f / 14f
        };
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
            cachedQuality = ResolveQuality();
            cachedCountersEnabled = ReadFlag(CountersKey, false);
            cachedWunaOrbitFireEnabled = ReadFlag(WunaOrbitFireKey, true);
        }
        catch
        {
            cachedQuality = SunExpPerformanceQuality.Balanced;
            cachedCountersEnabled = false;
            cachedWunaOrbitFireEnabled = true;
        }

        lastRefreshTick = now;
    }

    private static SunExpPerformanceQuality ResolveQuality()
    {
        var text = ReadGameVar(QualityKey);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = ReadGameVar(ShortQualityKey);
        }

        if (TryParseQuality(text, out var quality))
        {
            return quality;
        }

        return ReadFlag(LowSpecKey, false)
            ? SunExpPerformanceQuality.Low
            : SunExpPerformanceQuality.Balanced;
    }

    private static bool TryParseQuality(string? value, out SunExpPerformanceQuality quality)
    {
        var normalized = (value ?? "").Trim();
        switch (normalized.ToLowerInvariant())
        {
            case "0":
            case "high":
            case "h":
                quality = SunExpPerformanceQuality.High;
                return true;
            case "1":
            case "balanced":
            case "balance":
            case "normal":
            case "default":
                quality = SunExpPerformanceQuality.Balanced;
                return true;
            case "2":
            case "low":
            case "l":
                quality = SunExpPerformanceQuality.Low;
                return true;
            case "3":
            case "ultralow":
            case "ultra_low":
            case "minimal":
            case "off":
            case "disabled":
                quality = SunExpPerformanceQuality.UltraLow;
                return true;
            default:
                quality = SunExpPerformanceQuality.Balanced;
                return false;
        }
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
