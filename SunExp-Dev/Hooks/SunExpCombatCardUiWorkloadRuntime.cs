using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpCombatCardUiWorkloadRuntime
{
    private const double SlowMethodWarningMilliseconds = 16.0;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, Stack<long>> Starts = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterMeasured(modConfig, "FightUI.CreateCardItem");
        RegisterMeasured(modConfig, "FightUI.CreateCardItemInternal");
        RegisterMeasured(modConfig, "FightUI.UpdateCardItemPos");
        RegisterMeasured(modConfig, "CardItem.Init");
        RegisterMeasured(modConfig, "AttackCardItem.Init");
        RegisterMeasured(modConfig, "CardItem.DrawEffect");
        RegisterMeasured(modConfig, "CommonCardItem.DrawEffect");
        RegisterMeasured(modConfig, "AttackCardItem.DrawEffect");
        SunExpLog.InfoAlways("Combat card UI workload diagnostics initialized");
    }

    private static void RegisterMeasured(ModConfig config, string target)
    {
        SunExpHookRegistry.Before(config, target, context => Begin(target, context), "CombatCardUiWorkload");
        SunExpHookRegistry.After(config, target, context => End(target, context), "CombatCardUiWorkload");
    }

    private static void Begin(string target, ModHookContext context)
    {
        var key = CounterKey(target);
        SunExpPerformanceCounters.Record("CombatCardUi." + key + ".Before");
        SunExpCombatUiWorkload.Begin(target);
        PushStart(key, SunExpPerformanceCounters.Timestamp());
        SunExpLog.InfoOnceAlways(
            "CombatCardUiWorkload." + key,
            "Combat card UI hook observed: target="
            + target
            + ", receiver="
            + TargetName(context.Target)
            + ", args="
            + ArgumentShape(context.Arguments));
    }

    private static void End(string target, ModHookContext context)
    {
        var key = CounterKey(target);
        var start = PopStart(key);
        SunExpCombatUiWorkload.End(target);
        SunExpPerformanceCounters.RecordDuration("CombatCardUi." + key, start);
        if (start <= 0L || !SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var elapsed = SunExpPerformanceCounters.ElapsedMilliseconds(start);
        if (elapsed >= SlowMethodWarningMilliseconds)
        {
            SunExpLog.Warn("Slow combat card UI method: target="
                + target
                + ", elapsedMs="
                + elapsed.ToString("0.###")
                + ", receiver="
                + TargetName(context.Target)
                + ", args="
                + ArgumentShape(context.Arguments));
        }
    }

    private static void PushStart(string key, long start)
    {
        if (start <= 0L)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!Starts.TryGetValue(key, out var stack))
            {
                stack = new Stack<long>();
                Starts[key] = stack;
            }

            stack.Push(start);
        }
    }

    private static long PopStart(string key)
    {
        lock (SyncRoot)
        {
            if (!Starts.TryGetValue(key, out var stack) || stack.Count == 0)
            {
                return 0L;
            }

            var start = stack.Pop();
            if (stack.Count == 0)
            {
                Starts.Remove(key);
            }

            return start;
        }
    }

    private static string CounterKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '_' && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static string TargetName(object? target)
    {
        return target == null ? "<null>" : target.GetType().FullName ?? target.GetType().Name;
    }

    private static string ArgumentShape(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return "none";
        }

        return string.Join(
            "|",
            args.Select(arg => arg == null ? "null" : arg.GetType().FullName ?? arg.GetType().Name));
    }
}
