using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal sealed class MatchReplayActionSpan
{
    internal string ActionId { get; set; } = "";

    internal int ActionIndex { get; set; }

    internal int TurnIndex { get; set; }

    internal int BeginEventIndex { get; set; }

    internal int EndEventIndex { get; set; }

    internal int RestoreEventIndex { get; set; } = -1;
}

internal sealed class MatchReplayActionTimeline
{
    private readonly IReadOnlyList<MatchReplayActionSpan> actions;

    private MatchReplayActionTimeline(IReadOnlyList<MatchReplayActionSpan> actions)
    {
        this.actions = actions;
    }

    internal IReadOnlyList<MatchReplayActionSpan> Actions => actions;

    internal int Count => actions.Count;

    internal static MatchReplayActionTimeline Build(IReadOnlyList<MatchReplayEvent>? source)
    {
        var result = new List<MatchReplayActionSpan>();
        var open = new Dictionary<string, MatchReplayActionSpan>(StringComparer.Ordinal);
        MatchReplayActionSpan? lastEnded = null;
        if (source == null)
        {
            return new MatchReplayActionTimeline(result);
        }

        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            if (item.Kind == MatchReplayEventKinds.ActionFrame && item.ActionFrame != null)
            {
                result.Add(new MatchReplayActionSpan
                {
                    ActionId = item.ActionFrame.ActionId,
                    ActionIndex = Math.Max(1, item.ActionFrame.ActionIndex),
                    TurnIndex = Math.Max(1, item.ActionFrame.TurnIndex),
                    BeginEventIndex = i,
                    EndEventIndex = i,
                    RestoreEventIndex = i
                });
                lastEnded = null;
                continue;
            }

            if (item.Kind == MatchReplayEventKinds.Checkpoint)
            {
                if (lastEnded != null && lastEnded.EndEventIndex == i - 1)
                {
                    lastEnded.RestoreEventIndex = i;
                }

                lastEnded = null;
                continue;
            }

            var boundary = item.ActionBoundary;
            if (boundary == null || string.IsNullOrWhiteSpace(boundary.ActionId))
            {
                lastEnded = null;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(boundary.ParentActionId))
            {
                continue;
            }

            if (item.Kind == MatchReplayEventKinds.ActionBegin)
            {
                var span = new MatchReplayActionSpan
                {
                    ActionId = boundary.ActionId,
                    ActionIndex = Math.Max(1, boundary.ActionIndex),
                    TurnIndex = Math.Max(1, item.TurnIndex),
                    BeginEventIndex = i,
                    EndEventIndex = i
                };
                result.Add(span);
                open[boundary.ActionId] = span;
                lastEnded = null;
            }
            else if (item.Kind == MatchReplayEventKinds.ActionEnd
                     && open.TryGetValue(boundary.ActionId, out var span))
            {
                span.EndEventIndex = i;
                open.Remove(boundary.ActionId);
                lastEnded = span;
            }
            else
            {
                lastEnded = null;
            }

        }

        return new MatchReplayActionTimeline(result
            .OrderBy(item => item.BeginEventIndex)
            .ToList());
    }

    internal int CompletedActionsAtEventIndex(int eventIndex)
    {
        return actions.Count(item => (item.RestoreEventIndex >= 0 ? item.RestoreEventIndex : item.EndEventIndex) < eventIndex);
    }

    internal int EventIndexForCompletedActions(int completedActions, int eventCount)
    {
        var normalized = Math.Max(0, Math.Min(actions.Count, completedActions));
        if (normalized == 0)
        {
            return actions.Count == 0 ? 0 : actions[0].BeginEventIndex;
        }

        var action = actions[normalized - 1];
        var boundary = action.RestoreEventIndex >= 0 ? action.RestoreEventIndex : action.EndEventIndex;
        return Math.Min(eventCount, boundary + 1);
    }

    internal int CompletedActionsForTurn(int turnIndex, int direction)
    {
        if (actions.Count == 0)
        {
            return 0;
        }

        if (direction < 0)
        {
            var first = actions.FirstOrDefault(item => item.TurnIndex >= turnIndex);
            if (first == null) return 0;
            for (var i = 0; i < actions.Count; i++) if (ReferenceEquals(actions[i], first)) return i;
            return 0;
        }

        var last = actions.LastOrDefault(item => item.TurnIndex <= turnIndex);
        if (last == null) return actions.Count;
        for (var i = 0; i < actions.Count; i++) if (ReferenceEquals(actions[i], last)) return i + 1;
        return actions.Count;
    }
}
