using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class MatchReplayCaptureQualityResult
{
    internal bool CanPlay { get; set; }

    internal string Message { get; set; } = "";

    internal IReadOnlyDictionary<string, int> Counts { get; set; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    internal string DescribeCounts()
    {
        return string.Join(", ", Counts
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key + "=" + item.Value));
    }
}

internal static class MatchReplayCaptureQuality
{
    internal static MatchReplayCaptureQualityResult Evaluate(IEnumerable<MatchReplayEvent>? events)
    {
        return EvaluateCounts(CountEvents(events));
    }

    internal static MatchReplayCaptureQualityResult EvaluateRecording(
        IEnumerable<MatchReplayEvent>? events,
        bool hasBaseline,
        IReadOnlyCollection<string>? diagnostics)
    {
        return EvaluateRecording(CountEvents(events), hasBaseline, diagnostics);
    }

    internal static MatchReplayCaptureQualityResult EvaluateCounts(
        IReadOnlyDictionary<string, int>? counts)
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        if (counts != null)
        {
            foreach (var pair in counts)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                {
                    snapshot[pair.Key] = pair.Value;
                }
            }
        }

        var actionCount = Count(snapshot, MatchReplayEventKinds.ActionFrame);
        var turnCount = Count(snapshot, MatchReplayEventKinds.TurnFrame);
        if (actionCount <= 0)
        {
            return Result(false, "记录缺少权威动作帧，只能查看分析数据。", snapshot);
        }

        if (turnCount <= 0)
        {
            return Result(false, "记录缺少回合基线帧，无法进行确定性状态投影。", snapshot);
        }

        return Result(true, "权威动作帧与回合基线完整。", snapshot);
    }

    internal static MatchReplayCaptureQualityResult EvaluateRecording(
        IReadOnlyDictionary<string, int>? counts,
        bool hasBaseline,
        IReadOnlyCollection<string>? diagnostics)
    {
        var result = EvaluateCounts(counts);
        if (!hasBaseline)
        {
            return Result(false, "记录缺少权威初始状态，只能查看统计与分析。", result.Counts);
        }

        var failures = (diagnostics ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (failures.Count > 0)
        {
            return Result(false, "录制过程已降级，对局已保留但回放仅可用于统计与分析。", result.Counts);
        }

        return result;
    }

    private static int Count(IReadOnlyDictionary<string, int> counts, string kind)
    {
        return counts.TryGetValue(kind, out var value) ? value : 0;
    }

    private static IReadOnlyDictionary<string, int> CountEvents(IEnumerable<MatchReplayEvent>? events)
    {
        return (events ?? Array.Empty<MatchReplayEvent>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Kind))
            .GroupBy(item => item.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static MatchReplayCaptureQualityResult Result(
        bool canPlay,
        string message,
        IReadOnlyDictionary<string, int> counts)
    {
        return new MatchReplayCaptureQualityResult
        {
            CanPlay = canPlay,
            Message = message,
            Counts = counts
        };
    }
}
