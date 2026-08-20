using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Capture;

internal static class ReplayActionDerivationV10
{
    internal static ReplayTimelineEventV10 Started(
        long sequence,
        long timeTicks,
        int turnIndex,
        string actionId,
        ReplayActionSourceV10 source)
    {
        return new ReplayTimelineEventV10
        {
            Sequence = sequence,
            TimeTicks = Math.Max(0, timeTicks),
            TurnIndex = Math.Max(1, turnIndex),
            EventId = "event-" + sequence.ToString("D8"),
            ActionId = actionId ?? "",
            EventType = ReplayEventTypesV10.ActionStarted,
            ActorId = source?.ActorId ?? "",
            SourceInstanceId = source?.SourceInstanceId ?? ""
        };
    }

    internal static ReplayTimelineEventV10 Completed(
        long sequence,
        long timeTicks,
        int turnIndex,
        string actionId,
        ReplayActionSourceV10 source,
        ReplayLogicalStateV10 before,
        ReplayLogicalStateV10 after,
        string causeEventId = "")
    {
        var delta = ReplayProjectionStateV10.CreateDelta(before, after);
        var value = new ReplayTimelineEventV10
        {
            Sequence = sequence,
            TimeTicks = Math.Max(0, timeTicks),
            TurnIndex = Math.Max(1, turnIndex),
            EventId = "event-" + sequence.ToString("D8"),
            ActionId = actionId ?? "",
            CauseEventId = causeEventId ?? "",
            EventType = ReplayEventTypesV10.ActionCompleted,
            ActorId = source?.ActorId ?? "",
            SourceInstanceId = source?.SourceInstanceId ?? "",
            Delta = delta
        };
        value.Presentation.Add(new ReplayPresentationCueV10
        {
            CueId = value.EventId + ".source",
            Kind = source?.PresentationKind ?? ReplayPresentationKindsV10.Notice,
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
            value.Semantics.Add(new ReplaySemanticEventV10
            {
                Kind = ReplaySemanticKindsV10.Resource,
                Action = "PlayerPowerSet",
                ActorId = source?.ActorId ?? "",
                Value = change,
                SecondaryValue = after.PlayerPower,
                Label = "Power"
            });
            value.Presentation.Add(Cue(
                value,
                ReplayPresentationKindsV10.Resource,
                source?.ActorId ?? "",
                change,
                "Power"));
        }

        return value;
    }

    private static void AddActorOutcomes(
        ReplayTimelineEventV10 value,
        ReplayLogicalStateV10 before,
        ReplayLogicalStateV10 after)
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
                    ReplaySemanticKindsV10.Damage,
                    "HpDamage",
                    value.ActorId,
                    next.InstanceId,
                    -hpChange,
                    next.CurrentHp,
                    "HP"));
                value.Presentation.Add(Cue(value, ReplayPresentationKindsV10.Hit, next.InstanceId, -hpChange, "Damage"));
            }
            else if (hpChange > 0)
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV10.Healing,
                    "HpSet",
                    value.ActorId,
                    next.InstanceId,
                    hpChange,
                    next.CurrentHp,
                    "HP"));
                value.Presentation.Add(Cue(value, ReplayPresentationKindsV10.Heal, next.InstanceId, hpChange, "Healing"));
            }

            if (defenseChange != 0)
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV10.Defense,
                    "DefenseSet",
                    value.ActorId,
                    next.InstanceId,
                    defenseChange,
                    next.Defense,
                    "Defense"));
                value.Presentation.Add(Cue(value, ReplayPresentationKindsV10.Block, next.InstanceId, defenseChange, "Defense"));
            }

            AddBuffOutcomes(value, old, next);
            if (!string.Equals(old.State, next.State, StringComparison.Ordinal))
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV10.State,
                    "ActorStateSet",
                    value.ActorId,
                    next.InstanceId,
                    0,
                    0,
                    next.State));
                if (string.Equals(next.State, "Dead", StringComparison.OrdinalIgnoreCase))
                {
                    value.Presentation.Add(Cue(value, ReplayPresentationKindsV10.Death, next.InstanceId, 0, "Death"));
                }
            }
        }
    }

    private static void AddBuffOutcomes(
        ReplayTimelineEventV10 value,
        ReplayActorStateV10 before,
        ReplayActorStateV10 after)
    {
        var previous = Index(before.Buffs, item => item.InstanceId);
        var current = Index(after.Buffs, item => item.InstanceId);
        foreach (var pair in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var action = previous.TryGetValue(pair.Key, out var old)
                ? old.Level == pair.Value.Level ? "BuffUpdated" : "BuffLevelSet"
                : "BuffAdded";
            if (old != null && ReplayCanonicalJsonV10.Sha256(old) == ReplayCanonicalJsonV10.Sha256(pair.Value)) continue;
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV10.Buff,
                action,
                value.ActorId,
                after.InstanceId,
                pair.Value.Level - (old?.Level ?? 0),
                pair.Value.Level,
                pair.Value.Content.StableContentId));
            value.Presentation.Add(Cue(value, ReplayPresentationKindsV10.Buff, after.InstanceId, pair.Value.Level, pair.Value.Content.StableContentId));
        }

        foreach (var pair in previous.Where(item => !current.ContainsKey(item.Key)))
        {
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV10.Buff,
                "BuffRemoved",
                value.ActorId,
                after.InstanceId,
                -pair.Value.Level,
                0,
                pair.Value.Content.StableContentId));
        }
    }

    private static void AddCardOutcomes(
        ReplayTimelineEventV10 value,
        ReplayLogicalStateV10 before,
        ReplayLogicalStateV10 after)
    {
        var previous = Index(before.Cards, item => item.InstanceId);
        var current = Index(after.Cards, item => item.InstanceId);
        foreach (var pair in current.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!previous.TryGetValue(pair.Key, out var old))
            {
                value.Semantics.Add(Semantic(
                    ReplaySemanticKindsV10.Card,
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
                    ReplaySemanticKindsV10.Card,
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
                ReplaySemanticKindsV10.Card,
                "CardRemoved",
                value.ActorId,
                "",
                -1,
                0,
                pair.Value.Content.StableContentId));
        }
    }

    private static void AddIntentOutcomes(
        ReplayTimelineEventV10 value,
        ReplayLogicalStateV10 before,
        ReplayLogicalStateV10 after)
    {
        var previous = Index(before.Intents, item => item.InstanceId);
        var current = Index(after.Intents, item => item.InstanceId);
        foreach (var pair in current.Where(item => !previous.ContainsKey(item.Key)))
        {
            value.Semantics.Add(Semantic(
                ReplaySemanticKindsV10.Intent,
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
                ReplaySemanticKindsV10.Intent,
                "IntentRemoved",
                pair.Value.ActorId,
                "",
                -1,
                pair.Value.SlotIndex,
                pair.Value.Content.StableContentId));
        }
    }

    private static ReplaySemanticEventV10 Semantic(
        string kind,
        string action,
        string actor,
        string target,
        long value,
        long after,
        string label)
    {
        return new ReplaySemanticEventV10
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

    private static ReplayPresentationCueV10 Cue(
        ReplayTimelineEventV10 value,
        string kind,
        string target,
        long amount,
        string label)
    {
        return new ReplayPresentationCueV10
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
