using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Capture;

internal static class ReplayActionDerivationV11
{
    internal static ReplayTimelineEventV11 Started(
        long sequence,
        long timeTicks,
        int turnIndex,
        string actionId,
        ReplayActionSourceV11 source)
    {
        return new ReplayTimelineEventV11
        {
            Sequence = sequence,
            TimeTicks = Math.Max(0, timeTicks),
            TurnIndex = Math.Max(1, turnIndex),
            EventId = "event-" + sequence.ToString("D8"),
            ActionId = actionId ?? "",
            EventType = ReplayEventTypesV11.ActionStarted,
            ActorId = source?.ActorId ?? "",
            SourceInstanceId = source?.SourceInstanceId ?? ""
        };
    }

    internal static ReplayTimelineEventV11 Completed(
        long sequence,
        long timeTicks,
        int turnIndex,
        string actionId,
        ReplayActionSourceV11 source,
        ReplayLogicalStateV11 before,
        ReplayLogicalStateV11 after,
        string causeEventId = "")
    {
        var delta = ReplayProjectionStateV11.CreateDelta(before, after);
        var value = new ReplayTimelineEventV11
        {
            Sequence = sequence,
            TimeTicks = Math.Max(0, timeTicks),
            TurnIndex = Math.Max(1, turnIndex),
            EventId = "event-" + sequence.ToString("D8"),
            ActionId = actionId ?? "",
            CauseEventId = causeEventId ?? "",
            EventType = ReplayEventTypesV11.ActionCompleted,
            ActorId = source?.ActorId ?? "",
            SourceInstanceId = source?.SourceInstanceId ?? "",
            Delta = delta
        };
        value.Presentation.Add(new ReplayPresentationCueV11
        {
            CueId = value.EventId + ".source",
            Kind = source?.PresentationKind ?? ReplayPresentationKindsV11.Notice,
            DurationTicks = 480_000,
            ActorId = source?.ActorId ?? "",
            SourceInstanceId = source?.SourceInstanceId ?? "",
            Label = source?.Label ?? ""
        });
        AddActorOutcomes(value, before, after);
        AddCardOutcomes(value, before, after);
        AddIntentOutcomes(value, before, after);
        if (before.PlayerPower != after.PlayerPower || before.PlayerMaxPower != after.PlayerMaxPower)
        {
            var change = after.PlayerPower - before.PlayerPower;
            value.Semantics.Add(new ReplaySemanticEventV11
            {
                Kind = ReplaySemanticKindsV11.Resource,
                Action = "PlayerPowerSet",
                ActorId = source?.ActorId ?? "",
                Value = change,
                SecondaryValue = after.PlayerPower,
                Label = "Power"
            });
            value.Presentation.Add(Cue(
                value,
                ReplayPresentationKindsV11.Resource,
                source?.ActorId ?? "",
                change,
                "Power"));
        }

        return value;
    }

    private static void AddActorOutcomes(
        ReplayTimelineEventV11 value,
        ReplayLogicalStateV11 before,
        ReplayLogicalStateV11 after)
    {
        var previous = Index(before.Actors, item => item.InstanceId);
        var current = Index(after.Actors, item => item.InstanceId);
        foreach (var pair in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!previous.TryGetValue(pair.Key, out var old)) continue;
            var next = pair.Value;
            var hpChange = next.CurrentHp - old.CurrentHp;
            var defenseChange = next.Defense - old.Defense;
            if (hpChange < 0)
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV11.Damage,
                    "HpDamage",
                    value.ActorId,
                    next.InstanceId,
                    -hpChange,
                    next.CurrentHp,
                    "HP"));
                value.Presentation.Add(Cue(value, ReplayPresentationKindsV11.Hit, next.InstanceId, -hpChange, "Damage"));
            }
            else if (hpChange > 0)
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV11.Healing,
                    "HpSet",
                    value.ActorId,
                    next.InstanceId,
                    hpChange,
                    next.CurrentHp,
                    "HP"));
                value.Presentation.Add(Cue(value, ReplayPresentationKindsV11.Heal, next.InstanceId, hpChange, "Healing"));
            }

            if (defenseChange != 0)
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV11.Defense,
                    "DefenseSet",
                    value.ActorId,
                    next.InstanceId,
                    defenseChange,
                    next.Defense,
                    "Defense"));
                value.Presentation.Add(Cue(value, ReplayPresentationKindsV11.Block, next.InstanceId, defenseChange, "Defense"));
            }

            AddBuffOutcomes(value, old, next);
            if (!string.Equals(old.State, next.State, StringComparison.Ordinal))
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV11.State,
                    "ActorStateSet",
                    value.ActorId,
                    next.InstanceId,
                    0,
                    0,
                    next.State));
                if (string.Equals(next.State, "Dead", StringComparison.OrdinalIgnoreCase))
                {
                    value.Presentation.Add(Cue(value, ReplayPresentationKindsV11.Death, next.InstanceId, 0, "Death"));
                }
            }
        }
    }

    private static void AddBuffOutcomes(
        ReplayTimelineEventV11 value,
        ReplayActorStateV11 before,
        ReplayActorStateV11 after)
    {
        var previous = Index(before.Buffs, item => item.InstanceId);
        var current = Index(after.Buffs, item => item.InstanceId);
        foreach (var pair in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var action = previous.TryGetValue(pair.Key, out var old)
                ? old.Level == pair.Value.Level ? "BuffUpdated" : "BuffLevelSet"
                : "BuffAdded";
            if (old != null && ReplayCanonicalJsonV11.Sha256(old) == ReplayCanonicalJsonV11.Sha256(pair.Value)) continue;
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV11.Buff,
                action,
                value.ActorId,
                after.InstanceId,
                pair.Value.Level - (old?.Level ?? 0),
                pair.Value.Level,
                pair.Value.Content.StableContentId));
            value.Presentation.Add(Cue(value, ReplayPresentationKindsV11.Buff, after.InstanceId, pair.Value.Level, pair.Value.Content.StableContentId));
        }

        foreach (var pair in previous.Where(item => !current.ContainsKey(item.Key)))
        {
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV11.Buff,
                "BuffRemoved",
                value.ActorId,
                after.InstanceId,
                -pair.Value.Level,
                0,
                pair.Value.Content.StableContentId));
        }
    }

    private static void AddCardOutcomes(
        ReplayTimelineEventV11 value,
        ReplayLogicalStateV11 before,
        ReplayLogicalStateV11 after)
    {
        var previous = Index(before.Cards, item => item.InstanceId);
        var current = Index(after.Cards, item => item.InstanceId);
        foreach (var pair in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!previous.TryGetValue(pair.Key, out var old))
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV11.Card,
                    "CardCreated",
                    value.ActorId,
                    "",
                    1,
                    pair.Value.Order,
                    pair.Value.Content.StableContentId));
            }
            else if (!string.Equals(old.Zone, pair.Value.Zone, StringComparison.Ordinal)
                     || old.Order != pair.Value.Order)
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV11.Card,
                    "CardMoved:" + old.Zone + "->" + pair.Value.Zone,
                    value.ActorId,
                    "",
                    0,
                    pair.Value.Order,
                    pair.Value.Content.StableContentId));
            }
        }

        foreach (var pair in previous.Where(item => !current.ContainsKey(item.Key)))
        {
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV11.Card,
                "CardRemoved",
                value.ActorId,
                "",
                -1,
                0,
                pair.Value.Content.StableContentId));
        }
    }

    private static void AddIntentOutcomes(
        ReplayTimelineEventV11 value,
        ReplayLogicalStateV11 before,
        ReplayLogicalStateV11 after)
    {
        var previous = Index(before.Intents, item => item.InstanceId);
        var current = Index(after.Intents, item => item.InstanceId);
        foreach (var pair in current.Where(item => !previous.ContainsKey(item.Key)))
        {
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV11.Intent,
                "IntentAdded",
                pair.Value.ActorId,
                "",
                1,
                pair.Value.SlotIndex,
                pair.Value.Content.StableContentId));
        }

        foreach (var pair in previous.Where(item => !current.ContainsKey(item.Key)))
        {
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV11.Intent,
                "IntentRemoved",
                pair.Value.ActorId,
                "",
                -1,
                pair.Value.SlotIndex,
                pair.Value.Content.StableContentId));
        }
    }

    private static ReplaySemanticEventV11 Semantic(
        string kind,
        string action,
        string actor,
        string target,
        long value,
        long after,
        string label)
    {
        return new ReplaySemanticEventV11
        {
            Kind = kind,
            Action = action,
            ActorId = actor ?? "",
            TargetId = target ?? "",
            Value = value,
            SecondaryValue = after,
            Label = label ?? ""
        };
    }

    private static ReplayPresentationCueV11 Cue(
        ReplayTimelineEventV11 value,
        string kind,
        string target,
        long amount,
        string label)
    {
        return new ReplayPresentationCueV11
        {
            CueId = value.EventId + ".cue-" + value.Presentation.Count.ToString("D3"),
            Kind = kind,
            StartOffsetTicks = 180_000,
            DurationTicks = 480_000,
            ActorId = value.ActorId,
            TargetIds = string.IsNullOrWhiteSpace(target) ? new List<string>() : new List<string> { target },
            Label = label ?? "",
            Value = amount
        };
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T>? source, Func<T, string> key)
    {
        return (source ?? Enumerable.Empty<T>())
            .Where(item => !string.IsNullOrWhiteSpace(key(item)))
            .GroupBy(key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }
}
