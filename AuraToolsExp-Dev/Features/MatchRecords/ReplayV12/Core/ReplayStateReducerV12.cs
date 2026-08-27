using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal sealed class ReplayStateDiffV12
{
    internal List<ReplayEntityStateV12> Spawned { get; } = new();

    internal List<ReplayEntityStateV12> Despawned { get; } = new();

    internal ReplayStateDeltaV12 Delta { get; } = new();

    internal bool HasChanges => Spawned.Count > 0 || Despawned.Count > 0 || Delta.Operations.Count > 0;
}

internal sealed class ReplayStateReducerV12
{
    private ReplayPublicStateV12 current = new();
    private long lastTruthSequence;

    internal ReplayPublicStateV12 Current => Normalize(current);

    internal long LastTruthSequence => lastTruthSequence;

    internal void Reset(ReplayPublicStateV12 state, long truthSequence = 0)
    {
        current = Normalize(state);
        lastTruthSequence = Math.Max(0, truthSequence);
    }

    internal void Apply(ReplayJournalEventV12 value, bool verifyHashes = true)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (!string.Equals(value.Lane, ReplayJournalLanesV12.Truth, StringComparison.Ordinal))
            throw new InvalidOperationException("Only truth events can mutate replay public state.");
        if (value.Sequence <= lastTruthSequence)
            throw new InvalidOperationException("Replay truth sequence is not strictly increasing at " + value.Sequence + ".");

        var before = ReplayCanonicalJsonV12.StateHash(current);
        if (verifyHashes
            && !string.IsNullOrWhiteSpace(value.StateHashBefore)
            && !string.Equals(before, value.StateHashBefore, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Replay state hash mismatch before event " + value.Sequence + ".");
        }

        var changed = false;
        switch (value.EventType)
        {
            case ReplayEventTypesV12.EntitySpawned:
                ApplySpawn(value.Entity ?? throw new InvalidOperationException("EntitySpawned payload is missing."));
                changed = true;
                break;
            case ReplayEventTypesV12.EntityDespawned:
                ApplyDespawn(value.EntityId, value.SpawnGeneration);
                changed = true;
                break;
            case ReplayEventTypesV12.StateDeltaApplied:
                ApplyDelta(value.Delta ?? throw new InvalidOperationException("StateDeltaApplied payload is missing."));
                changed = value.Delta.Operations.Count > 0;
                break;
        }

        if (changed) current.StateVersion++;
        current = Normalize(current);
        lastTruthSequence = value.Sequence;
        var after = ReplayCanonicalJsonV12.StateHash(current);
        if (verifyHashes
            && !string.IsNullOrWhiteSpace(value.StateHashAfter)
            && !string.Equals(after, value.StateHashAfter, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Replay state hash mismatch after event " + value.Sequence + ".");
        }
    }

    internal static ReplayStateDiffV12 CreateDiff(ReplayPublicStateV12 before, ReplayPublicStateV12 after)
    {
        var left = Normalize(before);
        var right = Normalize(after);
        right.StateVersion = left.StateVersion;
        var result = new ReplayStateDiffV12();
        if (!string.Equals(left.BattlePhase, right.BattlePhase, StringComparison.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV12
            {
                Kind = ReplayStateOperationKindsV12.SetBattlePhase,
                BattlePhase = right.BattlePhase
            });
        if (left.RoundSequence != right.RoundSequence || left.ActorTurnSequence != right.ActorTurnSequence)
            result.Delta.Operations.Add(new ReplayStateOperationV12
            {
                Kind = ReplayStateOperationKindsV12.SetRoundTurn,
                RoundSequence = right.RoundSequence,
                ActorTurnSequence = right.ActorTurnSequence
            });
        if (!string.Equals(left.ActiveActorId, right.ActiveActorId, StringComparison.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV12
            {
                Kind = ReplayStateOperationKindsV12.SetActiveActor,
                ActiveActorId = right.ActiveActorId
            });
        if (!string.Equals(left.Outcome, right.Outcome, StringComparison.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV12
            {
                Kind = ReplayStateOperationKindsV12.SetOutcome,
                Outcome = right.Outcome
            });

        var beforeEntities = left.Entities.ToDictionary(Key, StringComparer.Ordinal);
        var afterEntities = right.Entities.ToDictionary(Key, StringComparer.Ordinal);
        foreach (var pair in beforeEntities.Where(pair => !afterEntities.ContainsKey(pair.Key)))
            result.Despawned.Add(Clone(pair.Value));
        foreach (var pair in afterEntities.Where(pair => !beforeEntities.ContainsKey(pair.Key)))
            result.Spawned.Add(Clone(pair.Value));
        foreach (var pair in afterEntities.Where(pair => beforeEntities.ContainsKey(pair.Key)))
        {
            var previous = beforeEntities[pair.Key];
            var next = pair.Value;
            if (!string.Equals(previous.Team, next.Team, StringComparison.Ordinal)
                || !string.Equals(previous.OwnerPlayerId, next.OwnerPlayerId, StringComparison.Ordinal)
                || previous.SlotIndex != next.SlotIndex)
            {
                throw new InvalidOperationException("Replay entity ownership changed without a new spawn generation: " + next.EntityId);
            }
            if (previous.MaxHp != next.MaxHp
                || previous.CurrentHp != next.CurrentHp
                || previous.Defense != next.Defense)
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.SetEntityVitals,
                    EntityId = next.EntityId,
                    SpawnGeneration = next.SpawnGeneration,
                    MaxHp = next.MaxHp,
                    CurrentHp = next.CurrentHp,
                    Defense = next.Defense
                });
            if (previous.IsPresent != next.IsPresent || previous.IsAlive != next.IsAlive)
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.SetEntityPresence,
                    EntityId = next.EntityId,
                    SpawnGeneration = next.SpawnGeneration,
                    IsPresent = next.IsPresent,
                    IsAlive = next.IsAlive
                });
            if (!Equivalent(previous.Buffs, next.Buffs))
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.ReplaceVisibleBuffs,
                    EntityId = next.EntityId,
                    SpawnGeneration = next.SpawnGeneration,
                    Buffs = next.Buffs.Select(Clone).ToList()
                });
        }

        var beforeIntents = left.Intents.GroupBy(item => item.ActorId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var afterIntents = right.Intents.GroupBy(item => item.ActorId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var actor in beforeIntents.Keys.Concat(afterIntents.Keys).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
        {
            var oldValues = beforeIntents.TryGetValue(actor, out var oldList) ? oldList : new List<ReplayIntentStateV12>();
            var newValues = afterIntents.TryGetValue(actor, out var newList) ? newList : new List<ReplayIntentStateV12>();
            if (!Equivalent(oldValues, newValues))
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.ReplaceVisibleIntents,
                    EntityId = actor,
                    Intents = newValues.Select(Clone).ToList()
                });
        }

        var beforeCards = left.Cards.ToDictionary(item => item.CardInstanceId, StringComparer.Ordinal);
        var afterCards = right.Cards.ToDictionary(item => item.CardInstanceId, StringComparer.Ordinal);
        foreach (var id in beforeCards.Keys.Where(id => !afterCards.ContainsKey(id)).OrderBy(item => item, StringComparer.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV12
            {
                Kind = ReplayStateOperationKindsV12.RemovePublicCard,
                CardInstanceId = id
            });
        foreach (var pair in afterCards.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!beforeCards.TryGetValue(pair.Key, out var oldCard))
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.AddPublicCard,
                    Card = Clone(pair.Value)
                });
            else if (!Equivalent(oldCard, pair.Value))
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.MovePublicCard,
                    Card = Clone(pair.Value),
                    CardInstanceId = pair.Key,
                    OwnerPlayerId = pair.Value.OwnerPlayerId,
                    Zone = pair.Value.Zone,
                    Order = pair.Value.Order
                });
        }

        var beforeZones = left.ZoneCounts.ToDictionary(ZoneKey, StringComparer.Ordinal);
        var afterZones = right.ZoneCounts.ToDictionary(ZoneKey, StringComparer.Ordinal);
        foreach (var key in beforeZones.Keys.Concat(afterZones.Keys).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
        {
            var oldCount = beforeZones.TryGetValue(key, out var oldZone) ? oldZone.Count : 0;
            var newZone = afterZones.TryGetValue(key, out var nextZone) ? nextZone : ParseZoneKey(key);
            if (oldCount != newZone.Count)
                result.Delta.Operations.Add(new ReplayStateOperationV12
                {
                    Kind = ReplayStateOperationKindsV12.SetPublicZoneCount,
                    OwnerPlayerId = newZone.OwnerPlayerId,
                    Zone = newZone.Zone,
                    Count = newZone.Count
                });
        }
        return result;
    }

    internal static ReplayPublicStateV12 Apply(ReplayPublicStateV12 source, ReplayStateDiffV12 diff)
    {
        var reducer = new ReplayStateReducerV12();
        reducer.Reset(source);
        var sequence = 0L;
        foreach (var entity in diff.Despawned)
            reducer.Apply(new ReplayJournalEventV12
            {
                Sequence = ++sequence,
                Lane = ReplayJournalLanesV12.Truth,
                EventType = ReplayEventTypesV12.EntityDespawned,
                EntityId = entity.EntityId,
                SpawnGeneration = entity.SpawnGeneration
            }, verifyHashes: false);
        foreach (var entity in diff.Spawned)
            reducer.Apply(new ReplayJournalEventV12
            {
                Sequence = ++sequence,
                Lane = ReplayJournalLanesV12.Truth,
                EventType = ReplayEventTypesV12.EntitySpawned,
                Entity = Clone(entity)
            }, verifyHashes: false);
        if (diff.Delta.Operations.Count > 0)
            reducer.Apply(new ReplayJournalEventV12
            {
                Sequence = ++sequence,
                Lane = ReplayJournalLanesV12.Truth,
                EventType = ReplayEventTypesV12.StateDeltaApplied,
                Delta = ReplayCanonicalJsonV12.Clone(diff.Delta)
            }, verifyHashes: false);
        return reducer.Current;
    }

    internal static ReplayPublicStateV12 Normalize(ReplayPublicStateV12? source)
    {
        var value = ReplayCanonicalJsonV12.Clone(source ?? new ReplayPublicStateV12());
        value.LevelId = value.LevelId ?? "";
        value.BattlePhase = value.BattlePhase ?? "";
        value.ActiveActorId = value.ActiveActorId ?? "";
        value.Outcome = value.Outcome ?? "";
        value.RoundSequence = Math.Max(0, value.RoundSequence);
        value.ActorTurnSequence = Math.Max(0, value.ActorTurnSequence);
        value.StateVersion = Math.Max(0, value.StateVersion);
        value.Entities = (value.Entities ?? new List<ReplayEntityStateV12>())
            .Where(item => item != null)
            .OrderBy(item => item.EntityId, StringComparer.Ordinal)
            .ThenBy(item => item.SpawnGeneration)
            .ToList();
        foreach (var entity in value.Entities)
        {
            entity.Buffs = (entity.Buffs ?? new List<ReplayBuffStateV12>())
                .Where(item => item != null)
                .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
                .ToList();
        }
        value.Cards = (value.Cards ?? new List<ReplayPublicCardStateV12>())
            .Where(item => item != null)
            .OrderBy(item => item.OwnerPlayerId, StringComparer.Ordinal)
            .ThenBy(item => item.Zone, StringComparer.Ordinal)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.CardInstanceId, StringComparer.Ordinal)
            .ToList();
        value.ZoneCounts = (value.ZoneCounts ?? new List<ReplayPublicZoneCountV12>())
            .Where(item => item != null)
            .OrderBy(item => item.OwnerPlayerId, StringComparer.Ordinal)
            .ThenBy(item => item.Zone, StringComparer.Ordinal)
            .ToList();
        value.Intents = (value.Intents ?? new List<ReplayIntentStateV12>())
            .Where(item => item != null)
            .OrderBy(item => item.ActorId, StringComparer.Ordinal)
            .ThenBy(item => item.SlotIndex)
            .ThenBy(item => item.IntentInstanceId, StringComparer.Ordinal)
            .ToList();
        foreach (var intent in value.Intents)
            intent.TargetIds = (intent.TargetIds ?? new List<string>()).OrderBy(item => item, StringComparer.Ordinal).ToList();
        return value;
    }

    internal static ReplayEntityStateV12 Clone(ReplayEntityStateV12 value) => ReplayCanonicalJsonV12.Clone(value);
    internal static ReplayBuffStateV12 Clone(ReplayBuffStateV12 value) => ReplayCanonicalJsonV12.Clone(value);
    internal static ReplayIntentStateV12 Clone(ReplayIntentStateV12 value) => ReplayCanonicalJsonV12.Clone(value);
    internal static ReplayPublicCardStateV12 Clone(ReplayPublicCardStateV12 value) => ReplayCanonicalJsonV12.Clone(value);

    private void ApplySpawn(ReplayEntityStateV12 entity)
    {
        if (string.IsNullOrWhiteSpace(entity.EntityId)) throw new InvalidOperationException("Replay entity id is empty.");
        if (current.Entities.Any(item => string.Equals(Key(item), Key(entity), StringComparison.Ordinal)))
            throw new InvalidOperationException("Replay entity generation already exists: " + Key(entity));
        current.Entities.Add(Clone(entity));
    }

    private void ApplyDespawn(string entityId, int generation)
    {
        var removed = current.Entities.RemoveAll(item => string.Equals(item.EntityId, entityId, StringComparison.Ordinal)
                                                     && item.SpawnGeneration == generation);
        if (removed != 1) throw new InvalidOperationException("Replay entity generation is missing at despawn: " + entityId);
        current.Intents.RemoveAll(item => string.Equals(item.ActorId, entityId, StringComparison.Ordinal));
    }

    private void ApplyDelta(ReplayStateDeltaV12 delta)
    {
        foreach (var operation in delta.Operations ?? new List<ReplayStateOperationV12>())
        {
            if (!ReplayStateOperationKindsV12.Supported.Contains(operation.Kind ?? ""))
                throw new InvalidOperationException("Unsupported replay state operation: " + operation.Kind);
            switch (operation.Kind)
            {
                case ReplayStateOperationKindsV12.SetBattlePhase:
                    current.BattlePhase = operation.BattlePhase ?? "";
                    break;
                case ReplayStateOperationKindsV12.SetRoundTurn:
                    current.RoundSequence = Math.Max(0, operation.RoundSequence);
                    current.ActorTurnSequence = Math.Max(0, operation.ActorTurnSequence);
                    break;
                case ReplayStateOperationKindsV12.SetActiveActor:
                    current.ActiveActorId = operation.ActiveActorId ?? "";
                    break;
                case ReplayStateOperationKindsV12.SetOutcome:
                    current.Outcome = operation.Outcome ?? "";
                    break;
                case ReplayStateOperationKindsV12.SetEntityVitals:
                {
                    var entity = RequireEntity(operation.EntityId, operation.SpawnGeneration);
                    entity.MaxHp = operation.MaxHp;
                    entity.CurrentHp = operation.CurrentHp;
                    entity.Defense = operation.Defense;
                    break;
                }
                case ReplayStateOperationKindsV12.SetEntityPresence:
                {
                    var entity = RequireEntity(operation.EntityId, operation.SpawnGeneration);
                    entity.IsPresent = operation.IsPresent;
                    entity.IsAlive = operation.IsAlive;
                    break;
                }
                case ReplayStateOperationKindsV12.ReplaceVisibleBuffs:
                    RequireEntity(operation.EntityId, operation.SpawnGeneration).Buffs =
                        (operation.Buffs ?? new List<ReplayBuffStateV12>()).Select(Clone).ToList();
                    break;
                case ReplayStateOperationKindsV12.ReplaceVisibleIntents:
                    current.Intents.RemoveAll(item => string.Equals(item.ActorId, operation.EntityId, StringComparison.Ordinal));
                    current.Intents.AddRange((operation.Intents ?? new List<ReplayIntentStateV12>()).Select(Clone));
                    break;
                case ReplayStateOperationKindsV12.AddPublicCard:
                    AddCard(operation.Card ?? throw new InvalidOperationException("AddPublicCard payload is missing."));
                    break;
                case ReplayStateOperationKindsV12.MovePublicCard:
                    MoveCard(operation.Card ?? throw new InvalidOperationException("MovePublicCard payload is missing."));
                    break;
                case ReplayStateOperationKindsV12.RemovePublicCard:
                    if (current.Cards.RemoveAll(item => string.Equals(item.CardInstanceId, operation.CardInstanceId, StringComparison.Ordinal)) != 1)
                        throw new InvalidOperationException("Replay card is missing at remove: " + operation.CardInstanceId);
                    break;
                case ReplayStateOperationKindsV12.SetPublicZoneCount:
                    SetZoneCount(operation.OwnerPlayerId, operation.Zone, operation.Count);
                    break;
            }
        }
    }

    private ReplayEntityStateV12 RequireEntity(string id, int generation)
    {
        return current.Entities.SingleOrDefault(item => string.Equals(item.EntityId, id, StringComparison.Ordinal)
                                                        && item.SpawnGeneration == generation)
               ?? throw new InvalidOperationException("Replay entity generation is missing: " + id);
    }

    private void AddCard(ReplayPublicCardStateV12 card)
    {
        if (string.IsNullOrWhiteSpace(card.CardInstanceId)
            || current.Cards.Any(item => string.Equals(item.CardInstanceId, card.CardInstanceId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Replay public card id is empty or duplicated: " + card.CardInstanceId);
        current.Cards.Add(Clone(card));
    }

    private void MoveCard(ReplayPublicCardStateV12 card)
    {
        var index = current.Cards.FindIndex(item => string.Equals(item.CardInstanceId, card.CardInstanceId, StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException("Replay public card is missing at move: " + card.CardInstanceId);
        current.Cards[index] = Clone(card);
    }

    private void SetZoneCount(string owner, string zone, int count)
    {
        current.ZoneCounts.RemoveAll(item => string.Equals(item.OwnerPlayerId, owner, StringComparison.Ordinal)
                                             && string.Equals(item.Zone, zone, StringComparison.Ordinal));
        current.ZoneCounts.Add(new ReplayPublicZoneCountV12
        {
            OwnerPlayerId = owner ?? "",
            Zone = zone ?? "",
            Count = Math.Max(0, count)
        });
    }

    private static string Key(ReplayEntityStateV12 value) => value.EntityId + "|" + value.SpawnGeneration;
    private static string ZoneKey(ReplayPublicZoneCountV12 value) => (value.OwnerPlayerId ?? "") + "|" + (value.Zone ?? "");

    private static ReplayPublicZoneCountV12 ParseZoneKey(string key)
    {
        var split = (key ?? "").Split(new[] { '|' }, 2);
        return new ReplayPublicZoneCountV12
        {
            OwnerPlayerId = split.Length > 0 ? split[0] : "",
            Zone = split.Length > 1 ? split[1] : ""
        };
    }

    private static bool Equivalent<T>(T left, T right) =>
        string.Equals(ReplayCanonicalJsonV12.Sha256(left!), ReplayCanonicalJsonV12.Sha256(right!), StringComparison.Ordinal);

    private static bool Equivalent<T>(IEnumerable<T> left, IEnumerable<T> right) =>
        string.Equals(ReplayCanonicalJsonV12.Sha256(left.ToList()), ReplayCanonicalJsonV12.Sha256(right.ToList()), StringComparison.Ordinal);
}
