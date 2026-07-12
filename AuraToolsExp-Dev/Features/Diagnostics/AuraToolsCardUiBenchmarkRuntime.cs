using System;
using System.Collections.Generic;
using System.Diagnostics;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Diagnostics;

public static class AuraToolsCardUiBenchmarkRuntime
{
    private const double SlowDataUpdateMilliseconds = 8d;
    private static readonly object Gate = new();
    private static readonly HashSet<string> SampledCardIds = new(StringComparer.Ordinal);
    private static readonly List<IDisposable> Registrations = new();
    [ThreadStatic] private static Stack<SampleStart>? starts;

    public static void Initialize(ModConfig modConfig)
    {
        Register(modConfig, "CardItem.DataUpdate");
        Register(modConfig, "AttackCardItem.DataUpdate");
        AuraToolsLog.Debug("[CardUiBenchmark] slow-card incremental benchmark initialized.");
    }

    private static void Register(ModConfig modConfig, string target)
    {
        Registrations.Add(AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            target,
            context => Begin(target, context),
            warn: AuraToolsLog.Warn));
        Registrations.Add(AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            target,
            context => End(target, context),
            warn: AuraToolsLog.Warn));
    }

    private static void Begin(string target, ModHookContext context)
    {
        starts ??= new Stack<SampleStart>();
        starts.Push(new SampleStart(target, Stopwatch.GetTimestamp()));
    }

    private static void End(string target, ModHookContext context)
    {
        if (starts == null || starts.Count == 0)
        {
            return;
        }

        var sample = starts.Pop();
        if (!string.Equals(sample.Target, target, StringComparison.Ordinal)
            || context.Target is not CardItem card
            || card.dataConfig == null)
        {
            return;
        }

        var fullMilliseconds = ElapsedMilliseconds(sample.Timestamp);
        if (fullMilliseconds < SlowDataUpdateMilliseconds)
        {
            return;
        }

        var id = card.dataConfig.data != null
                 && card.dataConfig.data.TryGetValue("Id", out var value)
            ? value ?? "unknown"
            : "unknown";
        lock (Gate)
        {
            if (!SampledCardIds.Add(id))
            {
                return;
            }
        }

        var text = card.transform.Find("Front/cost/cost")?.GetComponent<TMP_Text>();
        if (text == null)
        {
            return;
        }

        var deltaStart = Stopwatch.GetTimestamp();
        var accepted = AuraCardPresentationDelta.TrySetCost(card.transform, text.text);
        var deltaMilliseconds = ElapsedMilliseconds(deltaStart);
        AuraToolsLog.Debug("[CardUiBenchmark] card="
                           + id
                           + ", fullDataUpdateMs="
                           + fullMilliseconds.ToString("0.###")
                           + ", costOnlyMs="
                           + deltaMilliseconds.ToString("0.###")
                           + ", costOnlyAccepted="
                           + accepted);
    }

    private static double ElapsedMilliseconds(long start)
    {
        return start <= 0L ? 0d : (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    }

    private readonly struct SampleStart
    {
        public SampleStart(string target, long timestamp)
        {
            Target = target;
            Timestamp = timestamp;
        }

        public string Target { get; }
        public long Timestamp { get; }
    }
}
