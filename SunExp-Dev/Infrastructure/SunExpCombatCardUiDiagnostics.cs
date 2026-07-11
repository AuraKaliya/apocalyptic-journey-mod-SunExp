using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Infrastructure;

/// <summary>
/// Associates MOD-owned visual work with the native card-UI method currently
/// being measured.  Disabled performance counters keep this path allocation-free.
/// </summary>
public static class SunExpCombatCardUiDiagnostics
{
    [ThreadStatic] private static Stack<Scope>? scopes;

    public static void Begin(string target, ModHookContext context)
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        scopes ??= new Stack<Scope>();
        scopes.Push(new Scope(target, CardId(context.Arguments)));
    }

    public static string End(string target)
    {
        if (!SunExpPerformanceSettings.CountersEnabled || scopes == null || scopes.Count == 0)
        {
            return "";
        }

        var scope = scopes.Pop();
        if (!string.Equals(scope.Target, target, StringComparison.Ordinal))
        {
            SunExpPerformanceCounters.Record("CombatCardUi.Diagnostics.StackMismatch");
        }

        if (scope.Segments.Count == 0)
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var pair in scope.Segments)
        {
            parts.Add(pair.Key + "=" + pair.Value.ToString("0.###") + "ms");
            SunExpPerformanceCounters.Record("CombatCardUi." + target + ".Segment." + pair.Key);
        }

        return " card=" + scope.CardId + "; segments=" + string.Join(",", parts);
    }

    public static void RecordCurrentSegment(string name, long startTimestamp)
    {
        if (!SunExpPerformanceSettings.CountersEnabled || startTimestamp <= 0L || scopes == null || scopes.Count == 0)
        {
            return;
        }

        var elapsed = SunExpPerformanceCounters.ElapsedMilliseconds(startTimestamp);
        var scope = scopes.Peek();
        var key = string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
        scope.Segments[key] = scope.Segments.TryGetValue(key, out var previous) ? previous + elapsed : elapsed;
    }

    private static string CardId(object[]? args)
    {
        if (args == null || args.Length == 0 || args[0] is not IDataConfig config)
        {
            return "unknown";
        }

        return DictionaryUtil.Get(config.data, "Id", "unknown");
    }

    private sealed class Scope
    {
        public Scope(string target, string cardId)
        {
            Target = target ?? "";
            CardId = cardId ?? "unknown";
        }

        public string Target { get; }
        public string CardId { get; }
        public Dictionary<string, double> Segments { get; } = new(StringComparer.Ordinal);
    }
}
