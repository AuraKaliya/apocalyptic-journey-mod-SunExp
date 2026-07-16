using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
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
    [ThreadStatic] private static Stack<long>? keywordStarts;

    public static void Initialize(ModConfig modConfig)
    {
        if (!AuraToolsPerformanceSettings.DiagnosticsEnabled)
        {
            AuraToolsLog.Debug("[CardUiBenchmark] performance diagnostics disabled.");
            return;
        }

        Register(modConfig, "CardItem.DataUpdate");
        Register(modConfig, "AttackCardItem.DataUpdate");
        RegisterKeywordDisplay(modConfig);
        AuraToolsLog.Performance("[CardUiBenchmark] slow-card incremental benchmark initialized.");
    }

    private static void RegisterKeywordDisplay(ModConfig modConfig)
    {
        Registrations.Add(AuraToolsHookRegistry.BeforeRouted(
            modConfig,
            "KeywordDisplay.SetText",
            _ =>
            {
                keywordStarts ??= new Stack<long>();
                keywordStarts.Push(Stopwatch.GetTimestamp());
            },
            "CardUiBenchmark"));
        Registrations.Add(AuraToolsHookRegistry.AfterRouted(
            modConfig,
            "KeywordDisplay.SetText",
            _ =>
            {
                if (keywordStarts == null || keywordStarts.Count == 0)
                {
                    return;
                }

                var elapsed = ElapsedMilliseconds(keywordStarts.Pop());
                if (starts != null && starts.Count > 0)
                {
                    starts.Peek().KeywordMilliseconds += elapsed;
                }
            },
            "CardUiBenchmark"));
    }

    private static void Register(ModConfig modConfig, string target)
    {
        Registrations.Add(AuraToolsHookRegistry.BeforeRouted(
            modConfig,
            target,
            context => Begin(target, context),
            "CardUiBenchmark"));
        Registrations.Add(AuraToolsHookRegistry.AfterRouted(
            modConfig,
            target,
            context => End(target, context),
            "CardUiBenchmark"));
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
        var descriptionStart = Stopwatch.GetTimestamp();
        var description = card.dataConfig.Description();
        var descriptionMilliseconds = ElapsedMilliseconds(descriptionStart);
        var highlightStart = Stopwatch.GetTimestamp();
        description.Highlight(new List<string>());
        var highlightMilliseconds = ElapsedMilliseconds(highlightStart);
        var iconPath = card.dataConfig.data != null
                       && card.dataConfig.data.TryGetValue("Icon", out var iconValue)
            ? iconValue ?? ""
            : "";
        var iconBytes = TryGetModFileLength(iconPath);
        AuraToolsLog.Performance("[CardUiBenchmark] card="
                           + id
                           + ", fullDataUpdateMs="
                           + fullMilliseconds.ToString("0.###")
                           + ", costOnlyMs="
                           + deltaMilliseconds.ToString("0.###")
                           + ", costOnlyAccepted="
                           + accepted
                           + ", descriptionProbeMs="
                           + descriptionMilliseconds.ToString("0.###")
                           + ", highlightProbeMs="
                           + highlightMilliseconds.ToString("0.###")
                           + ", keywordSetTextMs="
                           + sample.KeywordMilliseconds.ToString("0.###")
                           + ", iconBytes="
                           + iconBytes
                           + ", iconPath="
                           + iconPath);
        ScheduleIconDecodeProbe(id, iconPath, iconBytes);
    }

    private static long TryGetModFileLength(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
        {
            return -1L;
        }

        try
        {
            var resolved = ResourceLoader.ResolveModPath(path);
            return File.Exists(resolved) ? new FileInfo(resolved).Length : -1L;
        }
        catch
        {
            return -1L;
        }
    }

    private static void ScheduleIconDecodeProbe(string cardId, string iconPath, long iconBytes)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !iconPath.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AuraSharedFrameScheduler.RunAfterFramesBudgeted(
            "AuraTools.CardUiBenchmark.IconDecode." + cardId,
            15,
            () => RunIconDecodeProbe(cardId, iconPath, iconBytes));
    }

    private static void RunIconDecodeProbe(string cardId, string iconPath, long iconBytes)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var sprite = AuraToolsResourceCache.Load<Sprite>(iconPath, true);
            var texture = sprite?.texture;
            AuraToolsLog.Performance("[CardUiBenchmark.IconDecodeProbe] card="
                               + cardId
                               + ", iconDecodeMs="
                               + ElapsedMilliseconds(started).ToString("0.###")
                               + ", iconBytes="
                               + iconBytes
                               + ", width="
                               + (texture?.width ?? 0)
                               + ", height="
                               + (texture?.height ?? 0)
                               + ", iconPath="
                               + iconPath);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Performance("[CardUiBenchmark.IconDecodeProbe] failed: card=" + cardId + ", error=" + ex.Message);
        }
    }

    private static double ElapsedMilliseconds(long start)
    {
        return start <= 0L ? 0d : (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    }

    private sealed class SampleStart
    {
        public SampleStart(string target, long timestamp)
        {
            Target = target;
            Timestamp = timestamp;
        }

        public string Target { get; }
        public long Timestamp { get; }

        public double KeywordMilliseconds { get; set; }
    }
}
