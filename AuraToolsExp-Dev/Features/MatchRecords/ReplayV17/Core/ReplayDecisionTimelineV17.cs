using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>
/// A read-only viewing plan. Source events, roots and checkpoint hashes remain
/// sealed evidence; the projected document is never persisted or finalized.
/// Only explicitly witnessed input waits are retimed. Silence is not a witness.
/// </summary>
internal sealed class ReplayDecisionTimelineV17
{
    internal const string Contract = "confirmed-decisions.v1";
    internal const string Profile = "decisions-v1";
    internal const long DecisionPauseTicks = ReplayProtocolV17.TimebaseTicksPerSecond / 2;
    internal const string Player = "Player";
    internal const string Selection = "Selection";
    internal const string OtherPlayers = "OtherPlayers";
    internal const string Committed = "Committed";
    internal const string Interrupted = "Interrupted";
    internal const string Terminal = "Terminal";

    private readonly List<Window> windows = new();
    internal ReplayDocumentV17 Document { get; }
    internal bool HasDecisionTiming { get; }
    internal long DurationTicks { get; }
    internal IReadOnlyList<long> DecisionSequences { get; }

    internal ReplayDecisionTimelineV17(ReplayDocumentV17 source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var errors = new List<string>();
        Validate(source, errors);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
        HasDecisionTiming = source.Presentation.Ui.DecisionTimingContract == Contract;
        DecisionSequences = source.PresentationEvents
            .Where(item => item.EventType == ReplayEventTypesV17.DecisionCommitted)
            .OrderBy(item => item.Sequence).Select(item => item.Sequence).ToArray();
        if (!HasDecisionTiming)
        {
            // Supported sealed v17 records predate authoritative input witnesses.
            // They retain their original cadence, through the same player/plan.
            Document = source;
            DurationTicks = Duration(source);
            return;
        }

        ReplayJournalEventV17? opened = null;
        long shift = 0;
        var protectedSpans = ExecutionSpans(source);
        var spanIndex = 0;
        foreach (var item in Boundaries(source))
        {
            if (item.EventType == ReplayEventTypesV17.InputWaitStarted) opened = item;
            if (item.EventType != ReplayEventTypesV17.InputWaitEnded || opened == null) continue;
            var length = item.Presentation!.Kind == Committed ? DecisionPauseTicks : 0L;
            var cursor = opened.TimeTicks;
            while (spanIndex < protectedSpans.Count && protectedSpans[spanIndex].End <= cursor) spanIndex++;
            var residual = new List<(long Start, long End)>();
            for (var index = spanIndex; index < protectedSpans.Count && protectedSpans[index].Start < item.TimeTicks; index++)
            {
                var span = protectedSpans[index];
                if (span.Start > cursor) residual.Add((cursor, Math.Min(span.Start, item.TimeTicks)));
                cursor = Math.Max(cursor, span.End);
            }
            if (cursor < item.TimeTicks || opened.TimeTicks == item.TimeTicks && cursor == item.TimeTicks)
                residual.Add((cursor, item.TimeTicks));
            for (var index = 0; index < residual.Count; index++)
            {
                var gap = residual[index];
                var pause = index == residual.Count - 1 ? length : 0L;
                windows.Add(new Window(gap.Start, gap.End, gap.Start + shift, pause));
                shift += pause - (gap.End - gap.Start);
            }
            opened = null;
        }

        // Clone only timing-bearing objects. The reducer still verifies the
        // original state hashes in original causal sequence; timing is not state.
        Document = new ReplayDocumentV17
        {
            Header = source.Header,
            InitialState = source.InitialState,
            Presentation = source.Presentation,
            Assets = source.Assets,
            TruthEvents = source.TruthEvents.Select(Project).ToList(),
            PresentationEvents = source.PresentationEvents.Select(Project)
                .Where(item => item.EventType != ReplayEventTypesV17.AudioPresented || item.Presentation?.Audio != null).ToList(),
            TruthCheckpoints = source.TruthCheckpoints.Select(value =>
            {
                var copy = ReplayCanonicalJsonV17.Clone(value);
                copy.TimeTicks = ToPlayback(value.TimeTicks);
                return copy;
            }).ToList(),
            PresentationCheckpoints = source.PresentationCheckpoints.Select(value =>
            {
                var copy = ReplayCanonicalJsonV17.Clone(value);
                copy.TimeTicks = ToPlayback(value.TimeTicks);
                return copy;
            }).ToList()
        };
        DurationTicks = Duration(Document);
    }

    internal long ToPlayback(long sourceTicks)
    {
        var low = 0;
        var high = windows.Count - 1;
        var index = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (windows[middle].Start <= sourceTicks) { index = middle; low = middle + 1; }
            else high = middle - 1;
        }
        if (index < 0) return sourceTicks;
        var window = windows[index];
        if (sourceTicks >= window.End) return window.PlaybackStart + window.Length + sourceTicks - window.End;
        return window.PlaybackStart + (long)((decimal)(sourceTicks - window.Start)
            * window.Length / Math.Max(1L, window.End - window.Start));
    }

    private ReplayJournalEventV17 Project(ReplayJournalEventV17 source)
    {
        var copy = ReplayFastCloneV17.Event(source);
        copy.TimeTicks = ToPlayback(source.TimeTicks);
        var message = copy.Presentation;
        if (message == null) return copy;
        var start = ReplayPresentationTimingV17.EffectiveTimeTicks(source);
        var projectedStart = ToPlayback(start);
        message.DelayTicks = projectedStart - copy.TimeTicks;
        message.DurationTicks = Math.Max(0L, ToPlayback(start + message.DurationTicks) - projectedStart);
        // Several samples may collapse onto one instant at an interrupted wait.
        // Keep the last observed pose, not a synthetic path or duplicate offsets.
        foreach (var sample in message.TransformSamples)
            sample.OffsetTicks = ToPlayback(start + sample.OffsetTicks) - projectedStart;
        message.TransformSamples = message.TransformSamples.GroupBy(sample => sample.OffsetTicks)
            .Select(group => group.Last()).ToList();
        foreach (var sample in message.WorldTransformSamples)
            sample.OffsetTicks = ToPlayback(start + sample.OffsetTicks) - projectedStart;
        message.WorldTransformSamples = message.WorldTransformSamples.GroupBy(sample => sample.OffsetTicks)
            .Select(group => group.Last()).ToList();
        if (message.Audio is { } cue)
        {
            var audioStart = cue.StartSample * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L;
            var audioEnd = (cue.StartSample + cue.DurationSamples) * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L;
            cue.StartSample = ToPlayback(audioStart) * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond;
            cue.DurationSamples = Math.Max(0L, ToPlayback(audioEnd) * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond - cue.StartSample);
            // Zero is the native/mixer sentinel for an unspecified full clip,
            // not silence. A cue removed by a cut must disappear from the plan.
            if (source.Presentation!.Audio!.DurationSamples > 0 && cue.DurationSamples == 0)
                message.Audio = null;
            // Source clip/loop offsets stay in sample space. BGM runs continuously
            // on the viewing clock, rather than jumping over the removed silence.
        }
        return copy;
    }

    internal static bool IsBoundary(string eventType) => eventType is
        ReplayEventTypesV17.InputWaitStarted or ReplayEventTypesV17.InputWaitEnded or ReplayEventTypesV17.DecisionCommitted;

    private static IEnumerable<ReplayJournalEventV17> Boundaries(ReplayDocumentV17 document) =>
        document.PresentationEvents.Where(item => IsBoundary(item.EventType)).OrderBy(item => item.Sequence);

    private static List<(long Start, long End)> ExecutionSpans(ReplayDocumentV17 source)
    {
        var intervals = new List<(long Start, long End)>();
        foreach (var item in source.PresentationEvents)
        {
            var message = item.Presentation;
            if (message == null || message.Persistent) continue;
            var required = item.EventType is ReplayEventTypesV17.CardMotionPresented
                or ReplayEventTypesV17.ActorAnimationPresented or ReplayEventTypesV17.HitReactionPresented
                or ReplayEventTypesV17.EffectPresented or ReplayEventTypesV17.DamageTextPresented
                or ReplayEventTypesV17.TurnTransitionPresented or ReplayEventTypesV17.ExtensionPresented;
            var duration = message.DurationTicks;
            if (message.Audio is { } audio && !string.Equals(audio.Bus, "Bgm", StringComparison.OrdinalIgnoreCase))
            {
                required = true;
                duration = Math.Max(duration, audio.DurationSamples * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L);
            }
            if (!required || duration <= 0) continue;
            var start = ReplayPresentationTimingV17.EffectiveTimeTicks(item);
            intervals.Add((start, start + duration));
        }
        var merged = new List<(long Start, long End)>();
        foreach (var span in intervals.OrderBy(item => item.Start))
        {
            if (merged.Count == 0 || merged[merged.Count - 1].End < span.Start) merged.Add(span);
            else
            {
                var last = merged[merged.Count - 1];
                merged[merged.Count - 1] = (last.Start, Math.Max(last.End, span.End));
            }
        }
        return merged;
    }

    internal static void Validate(ReplayDocumentV17 document, ICollection<string> errors)
    {
        var contract = document.Presentation.Ui.DecisionTimingContract;
        var boundaries = Boundaries(document).ToList();
        if (contract == null && boundaries.Count == 0) return;
        if (contract != Contract) { errors.Add("decision-timing-contract-unsupported"); return; }
        ReplayJournalEventV17? open = null;
        long previousTime = -1;
        long? expectedCommitTime = null;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in boundaries)
        {
            var message = item.Presentation;
            if (message == null || item.TimeTicks < previousTime || message.DelayTicks != 0
                || message.DurationTicks != 0 || string.IsNullOrWhiteSpace(message.SourceInstanceId))
            { errors.Add("decision-boundary-invalid:" + item.Sequence); continue; }
            previousTime = item.TimeTicks;
            if (expectedCommitTime.HasValue)
            {
                if (item.EventType != ReplayEventTypesV17.DecisionCommitted || item.TimeTicks != expectedCommitTime.Value)
                    errors.Add("decision-wait-missing-commit:" + item.Sequence);
                expectedCommitTime = null;
            }
            if (item.EventType == ReplayEventTypesV17.InputWaitStarted)
            {
                if (open != null || !identities.Add(message.SourceInstanceId)
                    || message.Kind is not Player and not Selection and not OtherPlayers)
                    errors.Add("decision-wait-open-invalid:" + item.Sequence);
                open = item;
            }
            else if (item.EventType == ReplayEventTypesV17.InputWaitEnded)
            {
                if (open == null || open.Presentation!.SourceInstanceId != message.SourceInstanceId
                    || message.Kind is not Committed and not Interrupted and not Terminal)
                    errors.Add("decision-wait-close-invalid:" + item.Sequence);
                open = null;
                if (message.Kind == Committed) expectedCommitTime = item.TimeTicks;
            }
            else if (open != null || message.Kind is not "Card" and not "Skill" and not "EndTurn" and not Selection)
                errors.Add("decision-commit-invalid:" + item.Sequence);
        }
        if (open != null) errors.Add("decision-wait-unclosed:" + open.Sequence);
        if (expectedCommitTime.HasValue) errors.Add("decision-wait-missing-commit");
        if (document.PresentationEvents.Any(item => item.EventType == ReplayEventTypesV17.CardMotionPresented
            && item.Presentation?.Kind == "Hand")) errors.Add("decision-timeline-speculative-hand-motion");
    }

    internal static long Duration(ReplayDocumentV17 document)
    {
        long maximum = 0;
        foreach (var item in document.TruthEvents.Concat(document.PresentationEvents))
        {
            var duration = Math.Max(0L, item.Presentation?.DurationTicks ?? 0L);
            if (item.Presentation?.Audio is { } audio)
                duration = Math.Max(duration, audio.DurationSamples * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L);
            maximum = Math.Max(maximum, ReplayPresentationTimingV17.EffectiveTimeTicks(item) + duration);
        }
        return maximum == 0 ? 0 : maximum + DecisionPauseTicks;
    }

    private readonly struct Window
    {
        internal Window(long start, long end, long playbackStart, long length)
        { Start = start; End = end; PlaybackStart = playbackStart; Length = length; }
        internal readonly long Start, End, PlaybackStart, Length;
    }
}
