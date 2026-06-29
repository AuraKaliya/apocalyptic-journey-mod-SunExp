using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SunExp.Dll.Infrastructure;

public static class SunExpPerformanceCounters
{
    private const int SummaryIntervalMilliseconds = 10000;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Counter> Counters = new(StringComparer.Ordinal);
    private static long lastSummaryTimestamp = Stopwatch.GetTimestamp();

    public static long Timestamp()
    {
        return SunExpPerformanceSettings.CountersEnabled ? Stopwatch.GetTimestamp() : 0L;
    }

    public static void Record(string name)
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        Add(name, 0L);
    }

    public static void RecordDuration(string name, long startTimestamp)
    {
        if (startTimestamp <= 0L || !SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        Add(name, Stopwatch.GetTimestamp() - startTimestamp);
        MaybeLogSummary();
    }

    public static void MaybeLogSummary()
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
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

        SunExpLog.Info("[Perf] " + string.Join("; ", lines));
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
