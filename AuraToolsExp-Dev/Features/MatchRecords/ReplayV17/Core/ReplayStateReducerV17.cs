using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal sealed class ReplayStateDiffV17
{
    internal List<ReplayEntityStateV17> Spawned { get; } = new();

    internal List<ReplayEntityStateV17> Despawned { get; } = new();

    internal ReplayStateDeltaV17 Delta { get; } = new();

    internal bool HasChanges => Spawned.Count > 0 || Despawned.Count > 0 || Delta.Operations.Count > 0;
}

internal sealed class ReplayStateReducerV17
{
    private ReplayVisibleStateV17 current = new();
    private long lastTruthSequence;
    private string currentStateHash = "";

    internal ReplayStateReducerV17()
    {
        Reset(new ReplayVisibleStateV17());
    }

    internal ReplayVisibleStateV17 Current => Normalize(current);

    internal long LastTruthSequence => lastTruthSequence;

    internal string CurrentStateHash => currentStateHash;

    internal void Reset(ReplayVisibleStateV17 state, long truthSequence = 0)
    {
        current = Normalize(state);
        lastTruthSequence = Math.Max(0, truthSequence);
        currentStateHash = ReplayCanonicalJsonV17.Sha256(current);
    }

    internal void Apply(ReplayJournalEventV17 value, bool verifyHashes = true)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (!string.Equals(value.Lane, ReplayJournalLanesV17.Truth, StringComparison.Ordinal))
            throw new InvalidOperationException("Only truth events can mutate replay visible state.");
        if (value.Sequence <= lastTruthSequence)
            throw new InvalidOperationException("Replay truth sequence is not strictly increasing at " + value.Sequence + ".");

        var before = currentStateHash;
        if (verifyHashes
            && !string.IsNullOrWhiteSpace(value.StateHashBefore)
            && !string.Equals(before, value.StateHashBefore, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Replay state hash mismatch before event " + value.Sequence + ".");
        }

        var changed = false;
        switch (value.EventType)
        {
            case ReplayEventTypesV17.EntitySpawned:
                ApplySpawn(value.Entity ?? throw new InvalidOperationException("EntitySpawned payload is missing."));
                changed = true;
                break;
            case ReplayEventTypesV17.EntityDespawned:
                ApplyDespawn(value.EntityId, value.SpawnGeneration);
                changed = true;
                break;
            case ReplayEventTypesV17.StateDeltaApplied:
                ApplyDelta(value.Delta ?? throw new InvalidOperationException("StateDeltaApplied payload is missing."));
                changed = value.Delta.Operations.Count > 0;
                break;
        }

        if (changed)
        {
            current.StateVersion++;
            current = Normalize(current);
            currentStateHash = ReplayCanonicalJsonV17.Sha256(current);
        }
        lastTruthSequence = value.Sequence;
        var after = currentStateHash;
        if (verifyHashes
            && !string.IsNullOrWhiteSpace(value.StateHashAfter)
            && !string.Equals(after, value.StateHashAfter, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Replay state hash mismatch after event " + value.Sequence + ".");
        }
    }

    internal static ReplayStateDiffV17 CreateDiff(ReplayVisibleStateV17 before, ReplayVisibleStateV17 after)
    {
        var left = Normalize(before);
        var right = Normalize(after);
        right.StateVersion = left.StateVersion;
        var result = new ReplayStateDiffV17();
        if (!string.Equals(left.PerspectivePlayerId, right.PerspectivePlayerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Replay perspective identity changed inside one battle.");
        if (!string.Equals(left.BattlePhase, right.BattlePhase, StringComparison.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.SetBattlePhase,
                BattlePhase = right.BattlePhase
            });
        if (left.RoundSequence != right.RoundSequence || left.ActorTurnSequence != right.ActorTurnSequence)
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.SetRoundTurn,
                RoundSequence = right.RoundSequence,
                ActorTurnSequence = right.ActorTurnSequence
            });
        if (!string.Equals(left.ActiveActorId, right.ActiveActorId, StringComparison.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.SetActiveActor,
                ActiveActorId = right.ActiveActorId
            });
        if (!string.Equals(left.Outcome, right.Outcome, StringComparison.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.SetOutcome,
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
                || !string.Equals(previous.DescriptorId, next.DescriptorId, StringComparison.Ordinal)
                || previous.SlotIndex != next.SlotIndex)
            {
                throw new InvalidOperationException("Replay entity ownership changed without a new spawn generation: " + next.EntityId);
            }
            if (previous.MaxHp != next.MaxHp
                || previous.CurrentHp != next.CurrentHp
                || previous.Defense != next.Defense)
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.SetEntityVitals,
                    EntityId = next.EntityId,
                    SpawnGeneration = next.SpawnGeneration,
                    MaxHp = next.MaxHp,
                    CurrentHp = next.CurrentHp,
                    Defense = next.Defense
                });
            if (previous.IsPresent != next.IsPresent || previous.IsAlive != next.IsAlive)
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.SetEntityPresence,
                    EntityId = next.EntityId,
                    SpawnGeneration = next.SpawnGeneration,
                    IsPresent = next.IsPresent,
                    IsAlive = next.IsAlive
                });
            if (!EquivalentBuffs(previous.Buffs, next.Buffs))
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.ReplaceVisibleBuffs,
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
            var oldValues = beforeIntents.TryGetValue(actor, out var oldList) ? oldList : new List<ReplayIntentStateV17>();
            var newValues = afterIntents.TryGetValue(actor, out var newList) ? newList : new List<ReplayIntentStateV17>();
            if (!EquivalentIntents(oldValues, newValues))
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.ReplaceVisibleIntents,
                    EntityId = actor,
                    Intents = newValues.Select(Clone).ToList()
                });
        }

        var beforeCards = left.Cards.ToDictionary(item => item.CardInstanceId, StringComparer.Ordinal);
        var afterCards = right.Cards.ToDictionary(item => item.CardInstanceId, StringComparer.Ordinal);
        foreach (var id in beforeCards.Keys.Where(id => !afterCards.ContainsKey(id)).OrderBy(item => item, StringComparer.Ordinal))
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.RemoveVisibleCard,
                CardInstanceId = id
            });
        foreach (var pair in afterCards.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!beforeCards.TryGetValue(pair.Key, out var oldCard))
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.AddVisibleCard,
                    Card = Clone(pair.Value)
                });
            else if (!EquivalentCard(oldCard, pair.Value))
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.MoveVisibleCard,
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
                result.Delta.Operations.Add(new ReplayStateOperationV17
                {
                    Kind = ReplayStateOperationKindsV17.SetVisibleZoneCount,
                    OwnerPlayerId = newZone.OwnerPlayerId,
                    Zone = newZone.Zone,
                    Count = newZone.Count
                });
        }
        if (!EquivalentResources(left.Resources, right.Resources))
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.ReplaceVisibleResources,
                Resources = right.Resources.Select(Clone).ToList()
            });
        if (!EquivalentExtensions(left.Extensions, right.Extensions))
            result.Delta.Operations.Add(new ReplayStateOperationV17
            {
                Kind = ReplayStateOperationKindsV17.ReplaceVisibleExtensions,
                Extensions = right.Extensions.Select(Clone).ToList()
            });
        return result;
    }

    internal static ReplayVisibleStateV17 Apply(ReplayVisibleStateV17 source, ReplayStateDiffV17 diff)
    {
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(source);
        var sequence = 0L;
        foreach (var entity in diff.Despawned)
            reducer.Apply(new ReplayJournalEventV17
            {
                Sequence = ++sequence,
                Lane = ReplayJournalLanesV17.Truth,
                EventType = ReplayEventTypesV17.EntityDespawned,
                EntityId = entity.EntityId,
                SpawnGeneration = entity.SpawnGeneration
            }, verifyHashes: false);
        foreach (var entity in diff.Spawned)
            reducer.Apply(new ReplayJournalEventV17
            {
                Sequence = ++sequence,
                Lane = ReplayJournalLanesV17.Truth,
                EventType = ReplayEventTypesV17.EntitySpawned,
                Entity = Clone(entity)
            }, verifyHashes: false);
        if (diff.Delta.Operations.Count > 0)
            reducer.Apply(new ReplayJournalEventV17
            {
                Sequence = ++sequence,
                Lane = ReplayJournalLanesV17.Truth,
                EventType = ReplayEventTypesV17.StateDeltaApplied,
                Delta = ReplayFastCloneV17.Delta(diff.Delta)
            }, verifyHashes: false);
        return reducer.Current;
    }

    internal static ReplayVisibleStateV17 Normalize(ReplayVisibleStateV17? source)
    {
        source ??= new ReplayVisibleStateV17();
        var value = new ReplayVisibleStateV17
        {
            LevelId = source.LevelId ?? "",
            PerspectivePlayerId = source.PerspectivePlayerId ?? "",
            BattlePhase = source.BattlePhase ?? "",
            RoundSequence = Math.Max(0, source.RoundSequence),
            ActorTurnSequence = Math.Max(0, source.ActorTurnSequence),
            ActiveActorId = source.ActiveActorId ?? "",
            Outcome = source.Outcome ?? "",
            StateVersion = Math.Max(0, source.StateVersion),
            Entities = (source.Entities ?? new List<ReplayEntityStateV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.EntityId, StringComparer.Ordinal)
            .ThenBy(item => item.SpawnGeneration)
            .ToList(),
            Cards = (source.Cards ?? new List<ReplayVisibleCardStateV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.OwnerPlayerId, StringComparer.Ordinal)
            .ThenBy(item => item.Zone, StringComparer.Ordinal)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.CardInstanceId, StringComparer.Ordinal)
            .ToList(),
            ZoneCounts = (source.ZoneCounts ?? new List<ReplayVisibleZoneCountV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.OwnerPlayerId, StringComparer.Ordinal)
            .ThenBy(item => item.Zone, StringComparer.Ordinal)
            .ToList(),
            Intents = (source.Intents ?? new List<ReplayIntentStateV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.ActorId, StringComparer.Ordinal)
            .ThenBy(item => item.SlotIndex)
            .ThenBy(item => item.IntentInstanceId, StringComparer.Ordinal)
            .ToList(),
            Resources = (source.Resources ?? new List<ReplayVisibleResourceStateV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.OwnerPlayerId, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ToList(),
            Extensions = (source.Extensions ?? new List<ReplayVisibleExtensionStateV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.OwnerModId, StringComparer.Ordinal)
            .ThenBy(item => item.TypeId, StringComparer.Ordinal)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToList()
        };
        return value;
    }

    internal static ReplayEntityStateV17 Clone(ReplayEntityStateV17 value) => new()
    {
        EntityId = value?.EntityId ?? "",
        DescriptorId = value?.DescriptorId ?? "",
        SpawnGeneration = value?.SpawnGeneration ?? 1,
        Team = value?.Team ?? ReplayTeamsV17.Neutral,
        OwnerPlayerId = value?.OwnerPlayerId ?? "",
        SlotIndex = value?.SlotIndex ?? 0,
        IsPresent = value?.IsPresent ?? false,
        IsAlive = value?.IsAlive ?? false,
        MaxHp = value?.MaxHp ?? 0,
        CurrentHp = value?.CurrentHp ?? 0,
        Defense = value?.Defense ?? 0,
        Buffs = (value?.Buffs ?? new List<ReplayBuffStateV17>())
            .Where(item => item != null)
            .Select(Clone)
            .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToList()
    };

    internal static ReplayBuffStateV17 Clone(ReplayBuffStateV17 value) => new()
    {
        InstanceId = value?.InstanceId ?? "",
        DescriptorId = value?.DescriptorId ?? "",
        Level = value?.Level ?? 0,
        UpperBound = value?.UpperBound ?? 0,
        VisibleDuration = value?.VisibleDuration ?? 0
    };

    internal static ReplayIntentStateV17 Clone(ReplayIntentStateV17 value) => new()
    {
        IntentInstanceId = value?.IntentInstanceId ?? "",
        ActorId = value?.ActorId ?? "",
        DescriptorId = value?.DescriptorId ?? "",
        SlotIndex = value?.SlotIndex ?? 0,
        DisplayValue = value?.DisplayValue ?? "",
        TargetIds = (value?.TargetIds ?? new List<string>())
            .Select(item => item ?? "")
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList()
    };

    internal static ReplayVisibleCardStateV17 Clone(ReplayVisibleCardStateV17 value) => new()
    {
        CardInstanceId = value?.CardInstanceId ?? "",
        DescriptorId = value?.DescriptorId ?? "",
        OwnerPlayerId = value?.OwnerPlayerId ?? "",
        Zone = value?.Zone ?? "",
        Order = value?.Order ?? 0,
        DisplayedCost = value?.DisplayedCost ?? 0,
        RenderedName = value?.RenderedName ?? "",
        RenderedDescription = value?.RenderedDescription ?? "",
        EnchantIconResourcePath = value?.EnchantIconResourcePath ?? "",
        IsRevealed = value?.IsRevealed ?? false,
        HasMeasuredLayout = value?.HasMeasuredLayout ?? false,
        CanvasPosition = ReplayFastCloneV17.Vector(value?.CanvasPosition),
        CanvasSize = ReplayFastCloneV17.Vector(value?.CanvasSize),
        RotationZQ16 = value?.RotationZQ16 ?? 0,
        LocalScale = ReplayFastCloneV17.Vector(value?.LocalScale)
    };

    internal static ReplayVisibleZoneCountV17 Clone(ReplayVisibleZoneCountV17 value) => new()
    {
        OwnerPlayerId = value?.OwnerPlayerId ?? "",
        Zone = value?.Zone ?? "",
        Count = value?.Count ?? 0
    };

    internal static ReplayVisibleResourceStateV17 Clone(ReplayVisibleResourceStateV17 value) => new()
    {
        OwnerPlayerId = value?.OwnerPlayerId ?? "",
        ResourceId = value?.ResourceId ?? "",
        Value = value?.Value ?? 0,
        Maximum = value?.Maximum ?? 0,
        DisplayText = value?.DisplayText ?? "",
        Name = value?.Name ?? "",
        ResourcePath = value?.ResourcePath ?? ""
    };

    internal static ReplayVisibleExtensionStateV17 Clone(ReplayVisibleExtensionStateV17 value) => new()
    {
        OwnerModId = value?.OwnerModId ?? "",
        TypeId = value?.TypeId ?? "",
        InstanceId = value?.InstanceId ?? "",
        SchemaVersion = value?.SchemaVersion ?? 1,
        DisplayText = value?.DisplayText ?? "",
        PayloadJson = value?.PayloadJson ?? ""
    };

    private void ApplySpawn(ReplayEntityStateV17 entity)
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

    private void ApplyDelta(ReplayStateDeltaV17 delta)
    {
        if ((delta.Operations?.Count ?? 0) > ReplayLimitsV17.MaximumOperationsPerTransaction)
            throw new InvalidOperationException("Replay state delta exceeds the operation budget.");
        foreach (var operation in delta.Operations ?? new List<ReplayStateOperationV17>())
        {
            if (!ReplayStateOperationKindsV17.Supported.Contains(operation.Kind ?? ""))
                throw new InvalidOperationException("Unsupported replay state operation: " + operation.Kind);
            switch (operation.Kind)
            {
                case ReplayStateOperationKindsV17.SetBattlePhase:
                    current.BattlePhase = operation.BattlePhase ?? "";
                    break;
                case ReplayStateOperationKindsV17.SetRoundTurn:
                    current.RoundSequence = Math.Max(0, operation.RoundSequence);
                    current.ActorTurnSequence = Math.Max(0, operation.ActorTurnSequence);
                    break;
                case ReplayStateOperationKindsV17.SetActiveActor:
                    current.ActiveActorId = operation.ActiveActorId ?? "";
                    break;
                case ReplayStateOperationKindsV17.SetOutcome:
                    current.Outcome = operation.Outcome ?? "";
                    break;
                case ReplayStateOperationKindsV17.SetEntityVitals:
                {
                    var entity = RequireEntity(operation.EntityId, operation.SpawnGeneration);
                    entity.MaxHp = operation.MaxHp;
                    entity.CurrentHp = operation.CurrentHp;
                    entity.Defense = operation.Defense;
                    break;
                }
                case ReplayStateOperationKindsV17.SetEntityPresence:
                {
                    var entity = RequireEntity(operation.EntityId, operation.SpawnGeneration);
                    entity.IsPresent = operation.IsPresent;
                    entity.IsAlive = operation.IsAlive;
                    break;
                }
                case ReplayStateOperationKindsV17.ReplaceVisibleBuffs:
                    RequireEntity(operation.EntityId, operation.SpawnGeneration).Buffs =
                        (operation.Buffs ?? new List<ReplayBuffStateV17>()).Select(Clone).ToList();
                    break;
                case ReplayStateOperationKindsV17.ReplaceVisibleIntents:
                    current.Intents.RemoveAll(item => string.Equals(item.ActorId, operation.EntityId, StringComparison.Ordinal));
                    current.Intents.AddRange((operation.Intents ?? new List<ReplayIntentStateV17>()).Select(Clone));
                    break;
                case ReplayStateOperationKindsV17.AddVisibleCard:
                    AddCard(operation.Card ?? throw new InvalidOperationException("AddVisibleCard payload is missing."));
                    break;
                case ReplayStateOperationKindsV17.MoveVisibleCard:
                    MoveCard(operation.Card ?? throw new InvalidOperationException("MoveVisibleCard payload is missing."));
                    break;
                case ReplayStateOperationKindsV17.RemoveVisibleCard:
                    if (current.Cards.RemoveAll(item => string.Equals(item.CardInstanceId, operation.CardInstanceId, StringComparison.Ordinal)) != 1)
                        throw new InvalidOperationException("Replay card is missing at remove: " + operation.CardInstanceId);
                    break;
                case ReplayStateOperationKindsV17.SetVisibleZoneCount:
                    SetZoneCount(operation.OwnerPlayerId, operation.Zone, operation.Count);
                    break;
                case ReplayStateOperationKindsV17.ReplaceVisibleResources:
                    current.Resources = (operation.Resources ?? new List<ReplayVisibleResourceStateV17>())
                        .Select(Clone).ToList();
                    break;
                case ReplayStateOperationKindsV17.ReplaceVisibleExtensions:
                    current.Extensions = (operation.Extensions ?? new List<ReplayVisibleExtensionStateV17>())
                        .Select(Clone).ToList();
                    break;
            }
        }
    }

    private ReplayEntityStateV17 RequireEntity(string id, int generation)
    {
        return current.Entities.SingleOrDefault(item => string.Equals(item.EntityId, id, StringComparison.Ordinal)
                                                        && item.SpawnGeneration == generation)
               ?? throw new InvalidOperationException("Replay entity generation is missing: " + id);
    }

    private void AddCard(ReplayVisibleCardStateV17 card)
    {
        if (string.IsNullOrWhiteSpace(card.CardInstanceId)
            || current.Cards.Any(item => string.Equals(item.CardInstanceId, card.CardInstanceId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Replay visible card id is empty or duplicated: " + card.CardInstanceId);
        current.Cards.Add(Clone(card));
    }

    private void MoveCard(ReplayVisibleCardStateV17 card)
    {
        var index = current.Cards.FindIndex(item => string.Equals(item.CardInstanceId, card.CardInstanceId, StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException("Replay visible card is missing at move: " + card.CardInstanceId);
        current.Cards[index] = Clone(card);
    }

    private void SetZoneCount(string owner, string zone, int count)
    {
        current.ZoneCounts.RemoveAll(item => string.Equals(item.OwnerPlayerId, owner, StringComparison.Ordinal)
                                             && string.Equals(item.Zone, zone, StringComparison.Ordinal));
        current.ZoneCounts.Add(new ReplayVisibleZoneCountV17
        {
            OwnerPlayerId = owner ?? "",
            Zone = zone ?? "",
            Count = Math.Max(0, count)
        });
    }

    private static string Key(ReplayEntityStateV17 value) => value.EntityId + "|" + value.SpawnGeneration;
    private static string ZoneKey(ReplayVisibleZoneCountV17 value) => (value.OwnerPlayerId ?? "") + "|" + (value.Zone ?? "");

    private static ReplayVisibleZoneCountV17 ParseZoneKey(string key)
    {
        var split = (key ?? "").Split(new[] { '|' }, 2);
        return new ReplayVisibleZoneCountV17
        {
            OwnerPlayerId = split.Length > 0 ? split[0] : "",
            Zone = split.Length > 1 ? split[1] : ""
        };
    }

    private static bool EquivalentBuffs(
        IReadOnlyList<ReplayBuffStateV17> left,
        IReadOnlyList<ReplayBuffStateV17> right) => SequenceEquivalent(left, right, (a, b) =>
        string.Equals(a.InstanceId, b.InstanceId, StringComparison.Ordinal)
        && string.Equals(a.DescriptorId, b.DescriptorId, StringComparison.Ordinal)
        && a.Level == b.Level
        && a.UpperBound == b.UpperBound
        && a.VisibleDuration == b.VisibleDuration);

    private static bool EquivalentIntents(
        IReadOnlyList<ReplayIntentStateV17> left,
        IReadOnlyList<ReplayIntentStateV17> right) => SequenceEquivalent(left, right, (a, b) =>
        string.Equals(a.IntentInstanceId, b.IntentInstanceId, StringComparison.Ordinal)
        && string.Equals(a.ActorId, b.ActorId, StringComparison.Ordinal)
        && string.Equals(a.DescriptorId, b.DescriptorId, StringComparison.Ordinal)
        && a.SlotIndex == b.SlotIndex
        && string.Equals(a.DisplayValue, b.DisplayValue, StringComparison.Ordinal)
        && a.TargetIds.SequenceEqual(b.TargetIds, StringComparer.Ordinal));

    private static bool EquivalentCard(ReplayVisibleCardStateV17 left, ReplayVisibleCardStateV17 right) =>
        string.Equals(left.CardInstanceId, right.CardInstanceId, StringComparison.Ordinal)
        && string.Equals(left.DescriptorId, right.DescriptorId, StringComparison.Ordinal)
        && string.Equals(left.OwnerPlayerId, right.OwnerPlayerId, StringComparison.Ordinal)
        && string.Equals(left.Zone, right.Zone, StringComparison.Ordinal)
        && left.Order == right.Order
        && left.DisplayedCost == right.DisplayedCost
        && string.Equals(left.RenderedName, right.RenderedName, StringComparison.Ordinal)
        && string.Equals(left.RenderedDescription, right.RenderedDescription, StringComparison.Ordinal)
        && string.Equals(left.EnchantIconResourcePath, right.EnchantIconResourcePath, StringComparison.Ordinal)
        && left.IsRevealed == right.IsRevealed
        && left.HasMeasuredLayout == right.HasMeasuredLayout
        && Equivalent(left.CanvasPosition, right.CanvasPosition)
        && Equivalent(left.CanvasSize, right.CanvasSize)
        && left.RotationZQ16 == right.RotationZQ16
        && Equivalent(left.LocalScale, right.LocalScale);

    private static bool Equivalent(ReplayVector2Q16V17? left, ReplayVector2Q16V17? right) =>
        (left?.X ?? 0) == (right?.X ?? 0) && (left?.Y ?? 0) == (right?.Y ?? 0);

    private static bool Equivalent(ReplayVector3Q16V17? left, ReplayVector3Q16V17? right) =>
        (left?.X ?? 0) == (right?.X ?? 0)
        && (left?.Y ?? 0) == (right?.Y ?? 0)
        && (left?.Z ?? 0) == (right?.Z ?? 0);

    private static bool EquivalentResources(
        IReadOnlyList<ReplayVisibleResourceStateV17> left,
        IReadOnlyList<ReplayVisibleResourceStateV17> right) => SequenceEquivalent(left, right, (a, b) =>
        string.Equals(a.OwnerPlayerId, b.OwnerPlayerId, StringComparison.Ordinal)
        && string.Equals(a.ResourceId, b.ResourceId, StringComparison.Ordinal)
        && a.Value == b.Value
        && a.Maximum == b.Maximum
        && string.Equals(a.DisplayText, b.DisplayText, StringComparison.Ordinal)
        && string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && string.Equals(a.ResourcePath, b.ResourcePath, StringComparison.Ordinal));

    private static bool EquivalentExtensions(
        IReadOnlyList<ReplayVisibleExtensionStateV17> left,
        IReadOnlyList<ReplayVisibleExtensionStateV17> right) => SequenceEquivalent(left, right, (a, b) =>
        string.Equals(a.OwnerModId, b.OwnerModId, StringComparison.Ordinal)
        && string.Equals(a.TypeId, b.TypeId, StringComparison.Ordinal)
        && string.Equals(a.InstanceId, b.InstanceId, StringComparison.Ordinal)
        && a.SchemaVersion == b.SchemaVersion
        && string.Equals(a.DisplayText, b.DisplayText, StringComparison.Ordinal)
        && string.Equals(a.PayloadJson, b.PayloadJson, StringComparison.Ordinal));

    private static bool SequenceEquivalent<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equivalent)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
            if (!equivalent(left[index], right[index])) return false;
        return true;
    }
}
