using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasCombatCardUiWorkloadRuntime
{
    private const double SlowMethodWarningMilliseconds = 16.0;
    [ThreadStatic] private static Stack<StartEntry>? starts;
    private static readonly List<IDisposable> Registrations = new();
    private static ModConfig? activeConfig;
    private static bool initialized;
    private static bool hooksEnabled;

    public static void Initialize(ModConfig modConfig)
    {
        activeConfig = modConfig;
        if (!initialized)
        {
            initialized = true;
            AuraFeatureSwitchRuntime.EffectiveStateChanged += OnFeatureSwitchChanged;
            ScheduleStateMonitor();
        }

        ReconcileHookState();
        TerriasLog.InfoAlways("Combat card UI workload diagnostics controller initialized; enabled="
                              + hooksEnabled);
    }

    private static void RegisterHooks(ModConfig modConfig)
    {
        RegisterMeasured(modConfig, TerriasHookTargets.FightUiCreateCardItem);
        RegisterMeasured(modConfig, TerriasHookTargets.FightUiCreateCardItemInternal);
        RegisterMeasured(modConfig, TerriasHookTargets.FightUiUpdateCardMsg);
        RegisterMeasured(modConfig, "FightUI.UpdateCardItemPos");
        RegisterMeasured(modConfig, TerriasHookTargets.ICardSetCardStyle);
        RegisterMeasured(modConfig, TerriasHookTargets.ICardSetCardMsg);
        RegisterMeasured(modConfig, TerriasHookTargets.ScriptExecutorRunScript);
        RegisterMeasured(modConfig, TerriasHookTargets.LocalizeExDescription);
        RegisterMeasured(modConfig, TerriasHookTargets.TextTranslatorTranslate);
        RegisterMeasured(modConfig, TerriasHookTargets.CardItemInit);
        RegisterMeasured(modConfig, TerriasHookTargets.AttackCardItemInit);
        RegisterMeasured(modConfig, TerriasHookTargets.CardItemDataUpdate);
        RegisterMeasured(modConfig, TerriasHookTargets.AttackCardItemDataUpdate);
        RegisterMeasured(modConfig, TerriasHookTargets.CardItemDrawEffect);
        RegisterMeasured(modConfig, TerriasHookTargets.CommonCardItemDrawEffect);
        RegisterMeasured(modConfig, TerriasHookTargets.AttackCardItemDrawEffect);
        RegisterMeasured(modConfig, TerriasHookTargets.FightCardManagerCardTagCheck);
        RegisterRefreshCauses(modConfig);
        hooksEnabled = true;
        TerriasLog.InfoAlways("Combat card UI workload diagnostics hooks enabled.");
    }

    private static void RegisterMeasured(ModConfig config, string target)
    {
        Registrations.Add(TerriasHookRegistry.BeforeRouted(
            config,
            target,
            context => Begin(target, context),
            "CombatCardUiWorkload"));
        Registrations.Add(TerriasHookRegistry.AfterRouted(
            config,
            target,
            context => End(target, context),
            "CombatCardUiWorkload"));
    }

    private static void Begin(string target, ModHookContext context)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var key = CounterKey(target);
        if (string.Equals(target, TerriasHookTargets.FightUiUpdateCardMsg, StringComparison.Ordinal))
        {
            TerriasCombatCardUiDiagnostics.BeginRefreshBatch(context);
        }

        TerriasPerformanceCounters.Record("CombatCardUi." + key + ".Before");
        TerriasCombatUiWorkload.Begin(target);
        TerriasCombatCardUiDiagnostics.Begin(key, context);
        PushStart(key, TerriasPerformanceCounters.Timestamp());
        TerriasLog.InfoOnceAlways(
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
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var key = CounterKey(target);
        var start = PopStart(key);
        if (start <= 0L)
        {
            return;
        }

        TerriasCombatUiWorkload.End(target);
        TerriasPerformanceCounters.RecordDuration("CombatCardUi." + key, start);
        var elapsed = start <= 0L ? 0d : TerriasPerformanceCounters.ElapsedMilliseconds(start);
        var segmentSummary = TerriasCombatCardUiDiagnostics.End(key, elapsed);
        if (string.Equals(target, TerriasHookTargets.FightUiCreateCardItemInternal, StringComparison.Ordinal))
        {
            segmentSummary += CombatCardViewConstructionDiagnostics.FormatRecent();
        }
        if (string.Equals(target, TerriasHookTargets.CardItemDataUpdate, StringComparison.Ordinal)
            || string.Equals(target, TerriasHookTargets.AttackCardItemDataUpdate, StringComparison.Ordinal))
        {
            TerriasCombatCardUiDiagnostics.RecordRefreshCard(context, elapsed);
        }
        else if (string.Equals(target, TerriasHookTargets.FightUiUpdateCardMsg, StringComparison.Ordinal))
        {
            segmentSummary += TerriasCombatCardUiDiagnostics.EndRefreshBatch(elapsed);
        }
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        if (elapsed >= SlowMethodWarningMilliseconds)
        {
            TerriasLog.Warn("Slow combat card UI method: target="
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
        Registrations.Add(TerriasHookRegistry.BeforeRouted(
            config,
            TerriasHookTargets.BuffItemConfigSetLevel,
            TerriasCombatCardUiDiagnostics.BeginBuffLevelChange,
            "CombatCardUiRefreshCause"));
        Registrations.Add(TerriasHookRegistry.AfterRouted(
            config,
            TerriasHookTargets.BuffItemConfigSetLevel,
            TerriasCombatCardUiDiagnostics.EndBuffLevelChange,
            "CombatCardUiRefreshCause"));
        Registrations.Add(TerriasHookRegistry.AfterRouted(
            config,
            TerriasHookTargets.StatusManagerAddBuff,
            context => TerriasCombatCardUiDiagnostics.RecordBuffMutation("add", context),
            "CombatCardUiRefreshCause"));
        Registrations.Add(TerriasHookRegistry.AfterRouted(
            config,
            TerriasHookTargets.StatusManagerRemoveBuff,
            context => TerriasCombatCardUiDiagnostics.RecordBuffMutation("remove", context),
            "CombatCardUiRefreshCause"));
        Registrations.Add(TerriasBattleLifecycleRouter.Register(
            "CombatCardUiRefreshCause",
            new TerriasBattleLifecycleSubscription
            {
                PlayerRoundReady = context => TerriasCombatCardUiDiagnostics.RecordRefreshCause("player-round-ready")
            }));
        Registrations.Add(TerriasHookRegistry.AfterRouted(
            config,
            TerriasHookTargets.BuffBarUiCheckAllBuff,
            context => TerriasCombatCardUiDiagnostics.RecordRefreshCause("buff-bar-check"),
            "CombatCardUiRefreshCause"));
    }

    private static void OnFeatureSwitchChanged(AuraFeatureSwitchChange change)
    {
        if (!string.Equals(change.OwnerModId, TerriasPerformanceSettings.SharedDiagnosticsOwnerId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(change.FeatureId, TerriasPerformanceSettings.SharedDiagnosticsFeatureId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TerriasPerformanceSettings.Refresh();
        ReconcileHookState();
    }

    private static void ReconcileHookState()
    {
        var enabled = TerriasPerformanceSettings.CountersEnabled;
        if (enabled == hooksEnabled)
        {
            return;
        }

        if (enabled)
        {
            if (activeConfig != null)
            {
                RegisterHooks(activeConfig);
            }
            return;
        }

        for (var i = Registrations.Count - 1; i >= 0; i--)
        {
            Registrations[i].Dispose();
        }

        Registrations.Clear();
        hooksEnabled = false;
        TerriasLog.InfoAlways("Combat card UI workload diagnostics hooks disabled.");
    }

    private static void ScheduleStateMonitor()
    {
        TerriasFrameScheduler.RunOnceAfterFrames(
            "CombatCardUi.DiagnosticsStateMonitor",
            60,
            MonitorState,
            AuraSharedFramePhase.Background,
            priority: -100,
            estimatedCost: 1);
    }

    private static void MonitorState()
    {
        TerriasPerformanceSettings.Refresh();
        ReconcileHookState();
        ScheduleStateMonitor();
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
            TerriasPerformanceCounters.Record("CombatCardUi.Workload.StackMismatch");
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
