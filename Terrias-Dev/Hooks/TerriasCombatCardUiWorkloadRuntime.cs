using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpCombatCardUiWorkloadRuntime
{
    private const double SlowMethodWarningMilliseconds = 16.0;
    [ThreadStatic] private static Stack<StartEntry>? starts;

    public static void Initialize(ModConfig modConfig)
    {
        if (!SunExpPerformanceSettings.CountersEnabled)
        {
            SunExpLog.Info("Combat card UI workload diagnostics disabled");
            return;
        }

        RegisterMeasured(modConfig, SunExpHookTargets.FightUiCreateCardItem);
        RegisterMeasured(modConfig, SunExpHookTargets.FightUiCreateCardItemInternal);
        RegisterMeasured(modConfig, SunExpHookTargets.FightUiUpdateCardMsg);
        RegisterMeasured(modConfig, "FightUI.UpdateCardItemPos");
        RegisterMeasured(modConfig, SunExpHookTargets.ICardSetCardStyle);
        RegisterMeasured(modConfig, SunExpHookTargets.ICardSetCardMsg);
        RegisterMeasured(modConfig, SunExpHookTargets.ScriptExecutorRunScript);
        RegisterMeasured(modConfig, SunExpHookTargets.LocalizeExDescription);
        RegisterMeasured(modConfig, SunExpHookTargets.TextTranslatorTranslate);
        RegisterMeasured(modConfig, SunExpHookTargets.CardItemInit);
        RegisterMeasured(modConfig, SunExpHookTargets.AttackCardItemInit);
        RegisterMeasured(modConfig, SunExpHookTargets.CardItemDataUpdate);
        RegisterMeasured(modConfig, SunExpHookTargets.AttackCardItemDataUpdate);
        RegisterMeasured(modConfig, SunExpHookTargets.CardItemDrawEffect);
        RegisterMeasured(modConfig, SunExpHookTargets.CommonCardItemDrawEffect);
        RegisterMeasured(modConfig, SunExpHookTargets.AttackCardItemDrawEffect);
        RegisterMeasured(modConfig, SunExpHookTargets.FightCardManagerCardTagCheck);
        RegisterRefreshCauses(modConfig);
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
        if (string.Equals(target, SunExpHookTargets.FightUiUpdateCardMsg, StringComparison.Ordinal))
        {
            SunExpCombatCardUiDiagnostics.BeginRefreshBatch(context);
        }

        SunExpPerformanceCounters.Record("CombatCardUi." + key + ".Before");
        SunExpCombatUiWorkload.Begin(target);
        SunExpCombatCardUiDiagnostics.Begin(key, context);
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
        var elapsed = start <= 0L ? 0d : SunExpPerformanceCounters.ElapsedMilliseconds(start);
        var segmentSummary = SunExpCombatCardUiDiagnostics.End(key, elapsed);
        if (string.Equals(target, SunExpHookTargets.FightUiCreateCardItemInternal, StringComparison.Ordinal))
        {
            segmentSummary += CombatCardViewConstructionDiagnostics.FormatRecent();
        }
        if (string.Equals(target, SunExpHookTargets.CardItemDataUpdate, StringComparison.Ordinal)
            || string.Equals(target, SunExpHookTargets.AttackCardItemDataUpdate, StringComparison.Ordinal))
        {
            SunExpCombatCardUiDiagnostics.RecordRefreshCard(context, elapsed);
        }
        else if (string.Equals(target, SunExpHookTargets.FightUiUpdateCardMsg, StringComparison.Ordinal))
        {
            segmentSummary += SunExpCombatCardUiDiagnostics.EndRefreshBatch(elapsed);
        }
        if (start <= 0L || !SunExpPerformanceSettings.CountersEnabled)
        {
            return;
        }

        if (elapsed >= SlowMethodWarningMilliseconds)
        {
            SunExpLog.Warn("Slow combat card UI method: target="
                + target
                + ", elapsedMs="
                + elapsed.ToString("0.###")
                + ", receiver="
                + TargetName(context.Target)
                + ", args="
                + ArgumentShape(context.Arguments)
                + segmentSummary);
        }
    }

    private static void RegisterRefreshCauses(ModConfig config)
    {
        SunExpHookRegistry.Before(
            config,
            SunExpHookTargets.BuffItemConfigSetLevel,
            SunExpCombatCardUiDiagnostics.BeginBuffLevelChange,
            "CombatCardUiRefreshCause");
        SunExpHookRegistry.After(
            config,
            SunExpHookTargets.BuffItemConfigSetLevel,
            SunExpCombatCardUiDiagnostics.EndBuffLevelChange,
            "CombatCardUiRefreshCause");
        SunExpHookRegistry.After(
            config,
            SunExpHookTargets.StatusManagerAddBuff,
            context => SunExpCombatCardUiDiagnostics.RecordBuffMutation("add", context),
            "CombatCardUiRefreshCause");
        SunExpHookRegistry.After(
            config,
            SunExpHookTargets.StatusManagerRemoveBuff,
            context => SunExpCombatCardUiDiagnostics.RecordBuffMutation("remove", context),
            "CombatCardUiRefreshCause");
        SunExpHookRegistry.After(
            config,
            SunExpHookTargets.FightPlayerTurnInit,
            context => SunExpCombatCardUiDiagnostics.RecordRefreshCause("player-turn"),
            "CombatCardUiRefreshCause");
        SunExpHookRegistry.After(
            config,
            SunExpHookTargets.BuffBarUiCheckAllBuff,
            context => SunExpCombatCardUiDiagnostics.RecordRefreshCause("buff-bar-check"),
            "CombatCardUiRefreshCause");
    }

    private static void PushStart(string key, long start)
    {
        if (start <= 0L)
        {
            return;
        }

        starts ??= new Stack<StartEntry>();
        starts.Push(new StartEntry(key, start));
    }

    private static long PopStart(string key)
    {
        if (starts == null || starts.Count == 0)
        {
            return 0L;
        }

        var entry = starts.Pop();
        if (!string.Equals(entry.Key, key, StringComparison.Ordinal))
        {
            SunExpPerformanceCounters.Record("CombatCardUi.Workload.StackMismatch");
        }

        return entry.Start;
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

    private readonly struct StartEntry
    {
        public StartEntry(string key, long start)
        {
            Key = key;
            Start = start;
        }

        public string Key { get; }

        public long Start { get; }
    }
}
