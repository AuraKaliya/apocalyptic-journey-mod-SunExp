using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayCompatibilityLevels
{
    internal const string Compatible = "Compatible";
    internal const string AnalysisOnly = "AnalysisOnly";
}

internal sealed class MatchReplayCompatibilityResult
{
    internal string Level { get; set; } = MatchReplayCompatibilityLevels.AnalysisOnly;
    internal string Message { get; set; } = "";
    internal bool CanPlay => Level == MatchReplayCompatibilityLevels.Compatible;
}

internal static class MatchReplayCompatibility
{
    private static readonly HashSet<string> SupportedCapabilities =
        new(MatchReplayCapabilities.Supported, StringComparer.OrdinalIgnoreCase);
    private static readonly string[] RequiredProjectionCapabilities =
    {
        MatchReplayCapabilities.AuthoritativeFramesV1,
        MatchReplayCapabilities.StateProjectionV1,
        MatchReplayCapabilities.PresentationTimelineV1,
        MatchReplayCapabilities.IndexedSeekV1,
        MatchReplayCapabilities.CardPresentationReadyV1,
        MatchReplayCapabilities.IncrementalHandV1,
        MatchReplayCapabilities.OutcomeCuesV1,
        MatchReplayCapabilities.PassiveHudV1,
        MatchReplayCapabilities.NativeBattleViewV1,
        MatchReplayCapabilities.ExactDependencyManifestV1
    };

    internal static MatchReplayCompatibilityResult Evaluate(
        MatchRecord record,
        IEnumerable<MatchReplayEvent>? events = null)
    {
        if (string.Equals(record.ReplayState, MatchReplayStates.Incomplete, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "该记录的必要事件族不完整，仅可查看统计与分析。"
            };
        }

        if (string.Equals(record.ReplayState, MatchReplayStates.Corrupt, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "该记录已损坏，仅可查看仍可读取的统计数据。"
            };
        }

        if (record.ReplayProtocol != MatchReplayProtocol.Version)
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "回放主协议不受当前播放器支持，仅可查看统计。"
            };
        }

        var required = new HashSet<string>(record.RequiredCapabilities ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var omittedContract = RequiredProjectionCapabilities
            .Where(value => !required.Contains(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        if (!required.Contains(MatchReplayCapabilities.EntityDeltaV2))
        {
            omittedContract.Add(MatchReplayCapabilities.EntityDeltaV2);
        }
        if (omittedContract.Count > 0)
        {
            return new MatchReplayCompatibilityResult
            {
                Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                Message = "记录未声明完整的只读投影能力：" + string.Join("、", omittedContract)
            };
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

        if (events != null)
        {
            var quality = MatchReplayCaptureQuality.Evaluate(events);
            if (!quality.CanPlay)
            {
                return new MatchReplayCompatibilityResult
                {
                    Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                    Message = quality.Message
                };
            }

            var invalidFrame = events
                .Where(item => item.Kind == MatchReplayEventKinds.ActionFrame)
                .Select(item => item.ActionFrame)
                .Any(frame => frame == null
                              || frame.Delta == null
                              || string.IsNullOrWhiteSpace(frame.FinalStateHash)
                              || frame.Presentation == null
                              || !frame.Presentation.Any(cue =>
                                  cue.Kind == MatchReplayPresentationCueKinds.ActorAction)
                              || (MatchReplayProjectionState.HasCardIdentityChanges(frame.Delta)
                                  && (frame.CardTransitions == null
                                      || frame.CardTransitions.Count == 0)));
            if (invalidFrame)
            {
                return new MatchReplayCompatibilityResult
                {
                    Level = MatchReplayCompatibilityLevels.AnalysisOnly,
                    Message = "动作帧缺少状态、卡牌迁移或表现编排数据，仅可查看统计。"
                };
            }
        }

        return new MatchReplayCompatibilityResult
        {
            Level = MatchReplayCompatibilityLevels.Compatible,
            Message = "原生战斗视图、权威状态和依赖清单完整。"
        };
    }

    private static bool IsSupportedKind(string kind)
    {
        return kind == MatchReplayEventKinds.TurnFrame
               || kind == MatchReplayEventKinds.ActionFrame
               || kind == MatchReplayEventKinds.SeekCheckpoint
               || kind == MatchReplayEventKinds.BattleResultFrame;
    }
}
