using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal sealed class ReplayProjectionEngine
{
    private ReplayLogicalStateV11 current = new();
    private long lastSequence;

    internal ReplayLogicalStateV11 Current => ReplayProjectionStateV11.Clone(current);

    internal long LastSequence => lastSequence;

    internal void Reset(ReplayLogicalStateV11 state, long sequence = 0)
    {
        current = ReplayProjectionStateV11.Clone(state ?? new ReplayLogicalStateV11());
        lastSequence = Math.Max(0, sequence);
    }

    internal void Apply(ReplayTimelineEventV11 value, bool verifyHash = true)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Sequence != lastSequence + 1)
        {
            throw new InvalidOperationException("Replay event sequence is not contiguous at " + value.Sequence + ".");
        }

        current = ReplayProjectionStateV11.Apply(current, value.Delta);
        lastSequence = value.Sequence;
        if (verifyHash && !string.IsNullOrWhiteSpace(value.StateHashAfter))
        {
            var actual = ReplayProjectionStateV11.Hash(current);
            if (!string.Equals(actual, value.StateHashAfter, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Replay state hash mismatch after event " + value.Sequence + ".");
            }
        }
    }

    internal void Restore(ReplayCheckpointV11 checkpoint)
    {
        if (checkpoint == null)
        {
            throw new ArgumentNullException(nameof(checkpoint));
        }

        var actual = ReplayProjectionStateV11.Hash(checkpoint.State);
        if (!string.Equals(actual, checkpoint.LogicalStateSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Replay checkpoint hash mismatch at event " + checkpoint.EventSequence + ".");
        }

        Reset(checkpoint.State, checkpoint.EventSequence);
    }
}

internal static class ReplayProjectionStateV11
{
    internal static ReplayStateDeltaV11 CreateDelta(ReplayLogicalStateV11 before, ReplayLogicalStateV11 after)
    {
        before ??= new ReplayLogicalStateV11();
        after ??= new ReplayLogicalStateV11();
        var beforeActors = Index(before.Actors, item => item.InstanceId);
        var afterActors = Index(after.Actors, item => item.InstanceId);
        var beforeCards = Index(before.Cards, item => item.InstanceId);
        var afterCards = Index(after.Cards, item => item.InstanceId);
        var beforeIntents = Index(before.Intents, item => item.InstanceId);
        var afterIntents = Index(after.Intents, item => item.InstanceId);
        return new ReplayStateDeltaV11
        {
            LevelChanged = !string.Equals(before.LevelId, after.LevelId, StringComparison.Ordinal),
            LevelId = after.LevelId ?? "",
            TurnChanged = before.TurnIndex != after.TurnIndex,
            TurnIndex = Math.Max(1, after.TurnIndex),
            ActiveActorChanged = !string.Equals(before.ActiveActorId, after.ActiveActorId, StringComparison.Ordinal),
            ActiveActorId = after.ActiveActorId ?? "",
            PlayerPowerChanged = before.PlayerPower != after.PlayerPower
                                 || before.PlayerMaxPower != after.PlayerMaxPower,
            PlayerPower = after.PlayerPower,
            PlayerMaxPower = after.PlayerMaxPower,
            CardTopCountChanged = before.CardTopCount != after.CardTopCount,
            CardTopCount = after.CardTopCount,
            ActorUpserts = Changed(beforeActors, afterActors).Select(Clone).ToList(),
            RemovedActorIds = Removed(beforeActors, afterActors),
            CardUpserts = Changed(beforeCards, afterCards).Select(Clone).ToList(),
            RemovedCardIds = Removed(beforeCards, afterCards),
            IntentUpserts = Changed(beforeIntents, afterIntents).Select(Clone).ToList(),
            RemovedIntentIds = Removed(beforeIntents, afterIntents)
        };
    }

    internal static ReplayLogicalStateV11 Apply(ReplayLogicalStateV11 source, ReplayStateDeltaV11? delta)
    {
        var result = Clone(source ?? new ReplayLogicalStateV11());
        if (delta == null)
        {
            return result;
        }

        if (delta.LevelChanged) result.LevelId = delta.LevelId ?? "";
        if (delta.TurnChanged) result.TurnIndex = Math.Max(1, delta.TurnIndex);
        if (delta.ActiveActorChanged) result.ActiveActorId = delta.ActiveActorId ?? "";
        if (delta.PlayerPowerChanged)
        {
            result.PlayerPower = delta.PlayerPower;
            result.PlayerMaxPower = delta.PlayerMaxPower;
        }

        if (delta.CardTopCountChanged) result.CardTopCount = Math.Max(0, delta.CardTopCount);
        result.Actors = ApplyCollection(
            result.Actors,
            delta.ActorUpserts,
            delta.RemovedActorIds,
            item => item.InstanceId,
            Clone);
        result.Cards = ApplyCollection(
            result.Cards,
            delta.CardUpserts,
            delta.RemovedCardIds,
            item => item.InstanceId,
            Clone);
        result.Intents = ApplyCollection(
            result.Intents,
            delta.IntentUpserts,
            delta.RemovedIntentIds,
            item => item.InstanceId,
            Clone);
        return result;
    }

    internal static string Hash(ReplayLogicalStateV11 state)
    {
        return ReplayCanonicalJsonV11.Sha256(Normalize(state));
    }

    internal static ReplayLogicalStateV11 Clone(ReplayLogicalStateV11 source)
    {
        return new ReplayLogicalStateV11
        {
            LevelId = source?.LevelId ?? "",
            TurnIndex = Math.Max(1, source?.TurnIndex ?? 1),
            ActiveActorId = source?.ActiveActorId ?? "",
            PlayerPower = source?.PlayerPower ?? 0,
            PlayerMaxPower = source?.PlayerMaxPower ?? 0,
            CardTopCount = source?.CardTopCount ?? 0,
            Actors = (source?.Actors ?? new List<ReplayActorStateV11>()).Select(Clone).ToList(),
            Cards = (source?.Cards ?? new List<ReplayCardStateV11>()).Select(Clone).ToList(),
            Intents = (source?.Intents ?? new List<ReplayIntentStateV11>()).Select(Clone).ToList()
        };
    }

    internal static ReplayActorStateV11 Clone(ReplayActorStateV11 source)
    {
        return new ReplayActorStateV11
        {
            InstanceId = source?.InstanceId ?? "",
            Content = Clone(source?.Content),
            EntityKind = source?.EntityKind ?? "",
            Team = source?.Team ?? ReplayTeamsV11.Neutral,
            OwnerPlayerId = source?.OwnerPlayerId ?? "",
            SlotIndex = source?.SlotIndex ?? 0,
            MaxHp = source?.MaxHp ?? 0,
            CurrentHp = source?.CurrentHp ?? 0,
            Defense = source?.Defense ?? 0,
            State = source?.State ?? "",
            Variables = (source?.Variables ?? new List<ReplayIntValueV11>())
                .Select(item => new ReplayIntValueV11 { Key = item.Key ?? "", Value = item.Value })
                .ToList(),
            Buffs = (source?.Buffs ?? new List<ReplayBuffStateV11>()).Select(Clone).ToList()
        };
    }

    internal static ReplayCardStateV11 Clone(ReplayCardStateV11 source)
    {
        return new ReplayCardStateV11
        {
            InstanceId = source?.InstanceId ?? "",
            Content = Clone(source?.Content),
            Zone = source?.Zone ?? "",
            Order = source?.Order ?? 0,
            DisplayedCost = source?.DisplayedCost ?? 0,
            Values = (source?.Values ?? new List<ReplayStringValueV11>())
                .Select(item => new ReplayStringValueV11 { Key = item.Key ?? "", Value = item.Value ?? "" })
                .ToList()
        };
    }

    internal static ReplayIntentStateV11 Clone(ReplayIntentStateV11 source)
    {
        return new ReplayIntentStateV11
        {
            InstanceId = source?.InstanceId ?? "",
            ActorId = source?.ActorId ?? "",
            Content = Clone(source?.Content),
            SlotIndex = source?.SlotIndex ?? 0,
            DisplayValue = source?.DisplayValue ?? "",
            TargetIds = (source?.TargetIds ?? new List<string>()).Where(item => item != null).ToList()
        };
    }

    private static ReplayBuffStateV11 Clone(ReplayBuffStateV11 source)
    {
        return new ReplayBuffStateV11
        {
            InstanceId = source?.InstanceId ?? "",
            Content = Clone(source?.Content),
            Level = source?.Level ?? 0,
            UpperBound = source?.UpperBound ?? 0,
            ReducePerTurn = source?.ReducePerTurn ?? 0,
            ReducePerUse = source?.ReducePerUse ?? 0,
            ReducePerAttacked = source?.ReducePerAttacked ?? 0,
            Values = (source?.Values ?? new List<ReplayStringValueV11>())
                .Select(item => new ReplayStringValueV11 { Key = item.Key ?? "", Value = item.Value ?? "" })
                .ToList()
        };
    }

    private static ReplayContentRefV11 Clone(ReplayContentRefV11? source)
    {
        return new ReplayContentRefV11
        {
            OwnerModId = source?.OwnerModId ?? "Witch",
            ContentKind = source?.ContentKind ?? "",
            StableContentId = source?.StableContentId ?? ""
        };
    }

    private static ReplayLogicalStateV11 Normalize(ReplayLogicalStateV11 source)
    {
        var result = Clone(source);
        result.Actors = result.Actors.OrderBy(item => item.InstanceId, StringComparer.Ordinal).ToList();
        foreach (var actor in result.Actors)
        {
            actor.Variables = actor.Variables.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
            actor.Buffs = actor.Buffs.OrderBy(item => item.InstanceId, StringComparer.Ordinal).ToList();
            foreach (var buff in actor.Buffs)
            {
                buff.Values = buff.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
            }
        }

        result.Cards = result.Cards
            .OrderBy(item => item.Zone, StringComparer.Ordinal)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToList();
        foreach (var card in result.Cards)
        {
            card.Values = card.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
        }

        result.Intents = result.Intents
            .OrderBy(item => item.ActorId, StringComparer.Ordinal)
            .ThenBy(item => item.SlotIndex)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToList();
        return result;
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T>? values, Func<T, string> key)
    {
        return (values ?? Enumerable.Empty<T>())
            .Where(item => !string.IsNullOrWhiteSpace(key(item)))
            .GroupBy(key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    private static IEnumerable<T> Changed<T>(
        IReadOnlyDictionary<string, T> before,
        IReadOnlyDictionary<string, T> after)
    {
        foreach (var pair in after.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(pair.Key, out var previous)
                || !string.Equals(
                    ReplayCanonicalJsonV11.Sha256(previous!),
                    ReplayCanonicalJsonV11.Sha256(pair.Value!),
                    StringComparison.Ordinal))
            {
                yield return pair.Value;
            }
        }
    }

    private static List<string> Removed<T>(
        IReadOnlyDictionary<string, T> before,
        IReadOnlyDictionary<string, T> after)
    {
        return before.Keys.Where(key => !after.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
    }

    private static List<T> ApplyCollection<T>(
        IEnumerable<T> current,
        IEnumerable<T> upserts,
        IEnumerable<string> removed,
        Func<T, string> key,
        Func<T, T> clone)
    {
        var values = Index(current, key);
        foreach (var id in removed ?? Enumerable.Empty<string>())
        {
            values.Remove(id ?? "");
        }

        foreach (var value in upserts ?? Enumerable.Empty<T>())
        {
            var id = key(value);
            if (!string.IsNullOrWhiteSpace(id)) values[id] = clone(value);
        }

        return values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value).ToList();
    }
}
