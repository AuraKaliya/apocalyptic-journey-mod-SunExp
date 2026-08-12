using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayCompatibilityLevels
{
    internal const string Compatible = "Compatible";
    internal const string Degraded = "Degraded";
    internal const string AnalysisOnly = "AnalysisOnly";
}

internal sealed class MatchReplayCompatibilityResult
{
    internal string Level { get; set; } = MatchReplayCompatibilityLevels.AnalysisOnly;
    internal string Message { get; set; } = "";
    internal bool CanPlay => Level != MatchReplayCompatibilityLevels.AnalysisOnly;
}

internal static class MatchReplayCompatibility
{
    private static readonly HashSet<string> SupportedCapabilities =
        new(MatchReplayCapabilities.Supported, StringComparer.OrdinalIgnoreCase);

    internal static MatchReplayCompatibilityResult Evaluate(MatchRecord record, IEnumerable<MatchReplayEvent>? events = null)
    {
        if (record.ReplayProtocol < MatchReplayProtocol.MinimumSupportedVersion
            || record.ReplayProtocol > MatchReplayProtocol.Version)
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "回放主协议不受当前播放器支持，仅可查看统计。"
            };
        }

        var required = new HashSet<string>(record.RequiredCapabilities ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (record.ReplayProtocol == 2 && required.Count == 0)
        {
            required.Add(MatchReplayCapabilities.CommandsV1);
        }

        var missing = required.Where(value => !SupportedCapabilities.Contains(value)).OrderBy(value => value).ToList();
        if (missing.Count > 0)
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "缺少必要回放能力：" + string.Join("、", missing)
            };
        }

        var unknownKinds = (events ?? Array.Empty<MatchReplayEvent>())
            .Select(item => item.Kind)
            .Where(kind => !IsSupportedKind(kind))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (unknownKinds.Count > 0)
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "存在无法执行的必要事件：" + string.Join("、", unknownKinds)
            };
        }

        var missingOptional = (record.OptionalCapabilities ?? new List<string>())
            .Where(value => !SupportedCapabilities.Contains(value))
            .ToList();
        return new MatchReplayCompatibilityResult
        {
            Level = missingOptional.Count == 0 ? MatchReplayCompatibilityLevels.Compatible : MatchReplayCompatibilityLevels.Degraded,
            Message = missingOptional.Count == 0 ? "兼容当前播放器。" : "部分表现能力不可用，将降级播放。"
        };
    }

    private static bool IsSupportedKind(string kind)
    {
        return kind == MatchReplayEventKinds.ActionCommand
               || kind == MatchReplayEventKinds.ClientCommand
               || kind == MatchReplayEventKinds.TargetCommand
               || kind == MatchReplayEventKinds.StatusSnapshot
               || kind == MatchReplayEventKinds.Checkpoint;
    }
}
