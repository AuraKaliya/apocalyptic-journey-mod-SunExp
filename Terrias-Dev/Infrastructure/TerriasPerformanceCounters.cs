using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Terrias.Dll.Infrastructure;

public static class TerriasPerformanceCounters
{
    private const int SummaryIntervalMilliseconds = 10000;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Counter> Counters = new(StringComparer.Ordinal);
    private static long lastSummaryTimestamp = Stopwatch.GetTimestamp();

    public static long Timestamp()
    {
        return TerriasPerformanceSettings.CountersEnabled ? Stopwatch.GetTimestamp() : 0L;
    }

    public static void Record(string name)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        Add(name, 0L);
    }

    public static void RecordDuration(string name, long startTimestamp)
    {
        if (startTimestamp <= 0L || !TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        Add(name, Stopwatch.GetTimestamp() - startTimestamp);
    }

    public static double ElapsedMilliseconds(long startTimestamp)
    {
        return startTimestamp <= 0L ? 0.0 : TicksToMilliseconds(Stopwatch.GetTimestamp() - startTimestamp);
    }

    public static double RecordHotspot(
        string name,
        long startTimestamp,
        string details = "",
        bool logFirstSample = false,
        double slowWarningMilliseconds = 8.0)
    {
        if (startTimestamp <= 0L || !TerriasPerformanceSettings.CountersEnabled)
        {
            return 0.0;
        }

        var elapsedTicks = Math.Max(0L, Stopwatch.GetTimestamp() - startTimestamp);
        Add(name, elapsedTicks);
        var elapsedMilliseconds = TicksToMilliseconds(elapsedTicks);
        var message = "[PerfHotspot] name="
            + name
            + ", elapsedMs="
            + elapsedMilliseconds.ToString("0.###")
            + (string.IsNullOrWhiteSpace(details) ? "" : ", " + details);
        if (elapsedMilliseconds >= Math.Max(0.0, slowWarningMilliseconds))
        {
            TerriasLog.Warn(message);
        }
        else if (logFirstSample)
        {
            TerriasLog.InfoOnceAlways("perf-hotspot:" + name, message);
        }

        return elapsedMilliseconds;
    }

    public static void MaybeLogSummary()
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = (now - lastSummaryTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMilliseconds < SummaryIntervalMilliseconds)
        {
            return;
        }

        List<string> lines;
        lock (Sync)
        {
            if (Counters.Count == 0)
            {
                lastSummaryTimestamp = now;
                return;
            }

            lines = Counters
                .OrderByDescending(pair => pair.Value.TotalTicks)
                .Take(12)
                .Select(pair => pair.Key
                    + ": count="
                    + pair.Value.Count
                    + ", totalMs="
                    + TicksToMilliseconds(pair.Value.TotalTicks).ToString("0.###")
                    + ", maxMs="
                    + TicksToMilliseconds(pair.Value.MaxTicks).ToString("0.###"))
                .ToList();
            Counters.Clear();
            lastSummaryTimestamp = now;
        }

        TerriasLog.InfoAlways("[Perf] " + string.Join("; ", lines));
    }

    private static void Add(string name, long elapsedTicks)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (Sync)
        {
            if (!Counters.TryGetValue(name, out var counter))
            {
                counter = new Counter();
                Counters[name] = counter;
            }

            counter.Count++;
            counter.TotalTicks += Math.Max(0L, elapsedTicks);
            if (elapsedTicks > counter.MaxTicks)
            {
                counter.MaxTicks = elapsedTicks;
            }
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private sealed class Counter
    {
        public long Count;
        public long TotalTicks;
        public long MaxTicks;
    }
}
