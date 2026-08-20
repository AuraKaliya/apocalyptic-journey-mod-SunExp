using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Runtime;

internal sealed class ReplayTimelineController
{
    private readonly ReplayDocumentV10 document;
    private readonly List<ReplayTimelineEventV10> events;
    private readonly ReplayProjectionEngine projector = new();
    private int eventIndex;
    private long currentTicks;

    internal ReplayTimelineController(ReplayDocumentV10 document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        events = document.Events.OrderBy(item => item.Sequence).ToList();
        projector.Reset(document.InitialState);
        DurationTicks = events.Count == 0
            ? ReplayProtocolV10.TimebaseTicksPerSecond
            : events.Max(item => item.TimeTicks + PresentationDuration(item));
    }

    internal ReplayLogicalStateV10 State => projector.Current;

    internal ReplayTimelineEventV10? CurrentEvent { get; private set; }

    internal long CurrentTicks => currentTicks;

    internal long DurationTicks { get; }

    internal int EventIndex => eventIndex;

    internal int EventCount => events.Count;

    internal bool IsFinished => eventIndex >= events.Count && currentTicks >= DurationTicks;

    internal float Progress => DurationTicks <= 0 ? 1f : Math.Min(1f, currentTicks / (float)DurationTicks);

    internal void Advance(long ticks)
    {
        SeekTime(Math.Min(DurationTicks, currentTicks + Math.Max(0, ticks)));
    }

    internal void SeekTime(long targetTicks)
    {
        var normalized = Math.Max(0, Math.Min(DurationTicks, targetTicks));
        if (normalized < currentTicks)
        {
            RestoreBeforeTime(normalized);
        }

        while (eventIndex < events.Count && events[eventIndex].TimeTicks <= normalized)
        {
            projector.Apply(events[eventIndex]);
            CurrentEvent = events[eventIndex];
            eventIndex++;
        }

        currentTicks = normalized;
    }

    internal void SeekSequence(long sequence)
    {
        var target = events.FirstOrDefault(item => item.Sequence >= sequence);
        SeekTime(target?.TimeTicks ?? DurationTicks);
    }

    internal void SeekTurn(int direction)
    {
        var currentTurn = Math.Max(1, State.TurnIndex);
        var targetTurn = Math.Max(1, currentTurn + (direction < 0 ? -1 : 1));
        var target = events.FirstOrDefault(item => item.TurnIndex >= targetTurn);
        if (target == null && direction < 0)
        {
            target = events.LastOrDefault(item => item.TurnIndex <= targetTurn);
        }
        SeekTime(target?.TimeTicks ?? (direction < 0 ? 0 : DurationTicks));
    }

    internal void SeekAction(int direction)
    {
        var actions = events.Where(item => item.EventType == ReplayEventTypesV10.ActionStarted).ToList();
        if (actions.Count == 0) return;
        ReplayTimelineEventV10? target;
        if (direction < 0)
        {
            target = actions.LastOrDefault(item => item.TimeTicks < currentTicks - 1) ?? actions[0];
        }
        else
        {
            target = actions.FirstOrDefault(item => item.TimeTicks > currentTicks + 1) ?? actions[actions.Count - 1];
        }
        SeekTime(target.TimeTicks);
    }

    private void RestoreBeforeTime(long targetTicks)
    {
        var checkpoint = document.Checkpoints
            .Where(item => item.TimeTicks <= targetTicks)
            .OrderByDescending(item => item.TimeTicks)
            .ThenByDescending(item => item.EventSequence)
            .FirstOrDefault();
        if (checkpoint == null)
        {
            projector.Reset(document.InitialState);
            eventIndex = 0;
            CurrentEvent = null;
            currentTicks = 0;
            return;
        }

        projector.Restore(checkpoint);
        eventIndex = events.FindIndex(item => item.Sequence > checkpoint.EventSequence);
        if (eventIndex < 0) eventIndex = events.Count;
        CurrentEvent = checkpoint.EventSequence <= 0
            ? null
            : events.LastOrDefault(item => item.Sequence == checkpoint.EventSequence);
        currentTicks = checkpoint.TimeTicks;
    }

    private static long PresentationDuration(ReplayTimelineEventV10 value)
    {
        var cues = value.Presentation ?? new List<ReplayPresentationCueV10>();
        return Math.Max(160_000L, cues.Count == 0
            ? 160_000L
            : cues.Max(item => item.StartOffsetTicks + item.DurationTicks));
    }
}
