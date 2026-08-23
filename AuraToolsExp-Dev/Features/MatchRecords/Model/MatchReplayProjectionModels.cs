using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal static class MatchReplayPresentationCueKinds
{
    internal const string CardUse = "CardUse";
    internal const string SkillUse = "SkillUse";
    internal const string EnemyIntent = "EnemyIntent";
    internal const string ActorAction = "ActorAction";
    internal const string Damage = "Damage";
    internal const string Heal = "Heal";
    internal const string Defend = "Defend";
    internal const string Buff = "Buff";
    internal const string Resource = "Resource";
    internal const string StateChange = "StateChange";
}

internal sealed class MatchReplayActionPresentationState
{
    public string ActorAnimationState { get; set; } = "";

    public string EffectName { get; set; } = "";

    public int EffectDelayMilliseconds { get; set; } = 50;

    public int PresentationDurationMilliseconds { get; set; } = 1040;

    public List<MatchReplayTargetPresentationState> Targets { get; set; } = new();
}

internal sealed class MatchReplayTargetPresentationState
{
    public string TargetId { get; set; } = "";

    public string AnimationState { get; set; } = "";
}

internal static class MatchReplayCardDispositionKinds
{
    internal const string Draw = "Draw";
    internal const string Discard = "Discard";
    internal const string Burn = "Burn";
    internal const string Consume = "Consume";
    internal const string Move = "Move";
    internal const string Reorder = "Reorder";
    internal const string Update = "Update";
    internal const string Remove = "Remove";
}

internal sealed class MatchReplayCardTransition
{
    public string ReplayCardId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string FromZone { get; set; } = "";

    public string ToZone { get; set; } = "";

    public int FromOrder { get; set; } = -1;

    public int ToOrder { get; set; } = -1;

    public string Disposition { get; set; } = MatchReplayCardDispositionKinds.Move;

    public bool PresentationChanged { get; set; }
}

internal sealed class MatchReplayPresentationCue
{
    public string CueId { get; set; } = "";

    public string Kind { get; set; } = "";

    public int StartOffsetMilliseconds { get; set; }

    public int DurationMilliseconds { get; set; }

    public string ActorId { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();

    public string AnimationState { get; set; } = "";

    public string Label { get; set; } = "";

    public long Value { get; set; }
}

internal sealed class MatchReplayTurnFrame
{
    public int TurnIndex { get; set; }

    public string ActiveActorId { get; set; } = "";

    public MatchReplayStateSnapshot State { get; set; } = new();

    public string StateHash { get; set; } = "";
}

internal sealed class MatchReplayActionFrame
{
    public string ActionId { get; set; } = "";

    public int ActionIndex { get; set; }

    public int TurnIndex { get; set; }

    public long StartedMilliseconds { get; set; }

    public long EndedMilliseconds { get; set; }

    public int DurationMilliseconds { get; set; }

    public string Kind { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string Label { get; set; } = "";

    public MatchReplayCardState? SourcePresentation { get; set; }

    public MatchReplayEnemyIntentState? IntentPresentation { get; set; }

    public MatchReplayActionPresentationState? NativePresentation { get; set; }

    public MatchReplayStateDelta Delta { get; set; } = new();

    public List<MatchReplayCardTransition> CardTransitions { get; set; } = new();

    public List<MatchReplayPresentationCue> Presentation { get; set; } = new();

    public List<MatchSemanticEvent> Semantics { get; set; } = new();

    public string FinalStateHash { get; set; } = "";
}

internal sealed class MatchReplayDerivedActionData
{
    internal int DurationMilliseconds { get; set; }

    internal List<MatchReplayCardTransition> CardTransitions { get; set; } = new();

    internal List<MatchReplayPresentationCue> Presentation { get; set; } = new();

    internal List<MatchSemanticEvent> Semantics { get; set; } = new();
}

internal static class MatchReplayActionBoundaryPolicy
{
    internal static bool ShouldNest(bool finalizationScheduled)
    {
        // Card-use methods are synchronous. Any begin observed before the matching outer
        // end is a nested hook/sub-action; once finalization is scheduled, even reusing the
        // same retained card is a distinct subsequent action.
        return !finalizationScheduled;
    }
}

/// <summary>
/// Derives presentation and analysis data exclusively from the recorded before/after states.
/// No command hook, game script, or battle simulation participates in this contract.
/// </summary>
internal static class MatchReplayActionDerivation
{
    private const int ActionDurationMilliseconds = 960;
    private const int OutcomeOffsetMilliseconds = 180;
    private const int OutcomeDurationMilliseconds = 300;

    internal static MatchReplayDerivedActionData Build(
        string actionId,
        string actionKind,
        string actorId,
        string sourceId,
        string sourceInstanceId,
        string label,
        MatchReplayCardState? sourcePresentation,
        MatchReplayStateSnapshot before,
        MatchReplayStateSnapshot after,
        MatchReplayActionPresentationState? nativePresentation = null,
        MatchReplayEnemyIntentState? intentPresentation = null)
    {
        var result = new MatchReplayDerivedActionData
        {
            DurationMilliseconds = Math.Max(
                360,
                Math.Min(
                    1600,
                    nativePresentation?.PresentationDurationMilliseconds ?? ActionDurationMilliseconds)),
            CardTransitions = BuildCardTransitions(before, after, sourcePresentation)
        };
        if (sourcePresentation != null
            && (string.Equals(actionKind, MatchReplayActionKinds.CardUse, StringComparison.Ordinal)
                || string.Equals(actionKind, MatchReplayActionKinds.SkillUse, StringComparison.Ordinal)))
        {
            result.Presentation.Add(new MatchReplayPresentationCue
            {
                CueId = actionId + (string.Equals(actionKind, MatchReplayActionKinds.SkillUse, StringComparison.Ordinal)
                    ? ":skill"
                    : ":card"),
                Kind = string.Equals(actionKind, MatchReplayActionKinds.SkillUse, StringComparison.Ordinal)
                    ? MatchReplayPresentationCueKinds.SkillUse
                    : MatchReplayPresentationCueKinds.CardUse,
                DurationMilliseconds = 640,
                ActorId = actorId,
                Label = label
            });
        }

        if (string.Equals(actionKind, MatchReplayActionKinds.EnemyIntentUse, StringComparison.Ordinal)
            && intentPresentation != null)
        {
            result.Presentation.Add(new MatchReplayPresentationCue
            {
                CueId = actionId + ":intent",
                Kind = MatchReplayPresentationCueKinds.EnemyIntent,
                DurationMilliseconds = 600,
                ActorId = actorId,
                TargetIds = (intentPresentation.TargetIds ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                Label = label
            });
        }

        result.Presentation.Add(new MatchReplayPresentationCue
        {
            CueId = actionId + ":actor",
            Kind = MatchReplayPresentationCueKinds.ActorAction,
            StartOffsetMilliseconds = 80,
            DurationMilliseconds = 880,
            ActorId = actorId,
            TargetIds = (nativePresentation?.Targets?.Select(item => item.TargetId)
                         ?? intentPresentation?.TargetIds
                         ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            AnimationState = string.IsNullOrWhiteSpace(nativePresentation?.ActorAnimationState)
                ? First(Value(sourcePresentation?.Data, "Action"), intentPresentation?.ActionState)
                : nativePresentation!.ActorAnimationState,
            Label = label
        });
        BuildOutcomes(
            result,
            actionId,
            actionKind,
            actorId,
            sourceId,
            sourceInstanceId,
            before,
            after);
        return result;
    }

    internal static List<MatchReplayCardTransition> BuildCardTransitions(
        MatchReplayStateSnapshot before,
        MatchReplayStateSnapshot after,
        MatchReplayCardState? sourcePresentation = null)
    {
        var previous = IndexCards(before?.Cards);
        var current = IndexCards(after?.Cards);
        var ids = new HashSet<string>(previous.Keys, StringComparer.Ordinal);
        ids.UnionWith(current.Keys);
        var burning = IsTrue(Value(sourcePresentation?.Vars, "HasBurn"))
                      || IsTrue(Value(sourcePresentation?.Vars, "IsBurning"))
                      || IsTrue(Value(sourcePresentation?.Data, "Burn"));
        var result = new List<MatchReplayCardTransition>();
        foreach (var replayCardId in ids.OrderBy(id => id, StringComparer.Ordinal))
        {
            previous.TryGetValue(replayCardId, out var from);
            current.TryGetValue(replayCardId, out var to);
            if (from != null && to != null
                             && string.Equals(from.Zone, to.Zone, StringComparison.Ordinal)
                             && from.Order == to.Order
                             && EquivalentCardContent(from, to))
            {
                continue;
            }

            var disposition = ResolveDisposition(from, to, sourcePresentation, burning);
            result.Add(new MatchReplayCardTransition
            {
                ReplayCardId = replayCardId,
                CardId = to?.CardId ?? from?.CardId ?? "",
                FromZone = from?.Zone ?? "",
                ToZone = to?.Zone ?? "",
                FromOrder = from?.Order ?? -1,
                ToOrder = to?.Order ?? -1,
                Disposition = disposition,
                PresentationChanged = from == null
                                      || to == null
                                      || !EquivalentCardContent(from, to)
            });
        }

        return result;
    }

    private static void BuildOutcomes(
        MatchReplayDerivedActionData result,
        string actionId,
        string actionKind,
        string actorId,
        string sourceId,
        string sourceInstanceId,
        MatchReplayStateSnapshot before,
        MatchReplayStateSnapshot after)
    {
        var previous = (before.Statuses ?? new List<MatchReplayStatusState>())
            .Where(item => !string.IsNullOrWhiteSpace(item.InstanceId))
            .GroupBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var current = (after.Statuses ?? new List<MatchReplayStatusState>())
            .Where(item => !string.IsNullOrWhiteSpace(item.InstanceId))
            .GroupBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var outcomeIndex = 0;
        foreach (var targetId in previous.Keys.Union(current.Keys, StringComparer.Ordinal)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            previous.TryGetValue(targetId, out var from);
            current.TryGetValue(targetId, out var to);
            if (from == null || to == null)
            {
                AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Status,
                    MatchReplayPresentationCueKinds.StateChange, from == null ? "StatusAdded" : "StatusRemoved",
                    actorId, sourceId, sourceInstanceId, targetId, 0, 0, "");
                continue;
            }

            var hpDelta = to.CurrentHp - from.CurrentHp;
            if (hpDelta < 0)
            {
                AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Damage,
                    MatchReplayPresentationCueKinds.Damage, "HpLost", actorId, sourceId, sourceInstanceId,
                    targetId, Math.Abs((long)hpDelta), to.CurrentHp, "");
            }
            else if (hpDelta > 0)
            {
                AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Heal,
                    MatchReplayPresentationCueKinds.Heal, "HpGained", actorId, sourceId, sourceInstanceId,
                    targetId, hpDelta, to.CurrentHp, "");
            }

            var defendDelta = to.Defend - from.Defend;
            if (defendDelta < 0)
            {
                AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Damage,
                    MatchReplayPresentationCueKinds.Damage, "ShieldDamage",
                    actorId, sourceId, sourceInstanceId, targetId, Math.Abs((long)defendDelta), to.Defend, "Shield");
            }
            else if (defendDelta > 0)
            {
                AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Defend,
                    MatchReplayPresentationCueKinds.Defend,
                    "DefendGained",
                    actorId, sourceId, sourceInstanceId, targetId, Math.Abs((long)defendDelta), to.Defend, "");
            }

            BuildBuffOutcomes(result, actionId, ref outcomeIndex, actorId, sourceId, sourceInstanceId,
                targetId, from.Buffs, to.Buffs);
            if (!string.Equals(from.State, to.State, StringComparison.Ordinal))
            {
                AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Status,
                    MatchReplayPresentationCueKinds.StateChange, "StateChanged", actorId, sourceId,
                    sourceInstanceId, targetId, 0, 0, to.State);
            }
        }

        if (before.PlayerPower != after.PlayerPower || before.PlayerMaxPower != after.PlayerMaxPower)
        {
            AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Resource,
                MatchReplayPresentationCueKinds.Resource, "PowerChanged", actorId, sourceId, sourceInstanceId,
                actorId, after.PlayerPower - before.PlayerPower, after.PlayerPower, "Power");
        }
    }

    private static void BuildBuffOutcomes(
        MatchReplayDerivedActionData result,
        string actionId,
        ref int outcomeIndex,
        string actorId,
        string sourceId,
        string sourceInstanceId,
        string targetId,
        IEnumerable<MatchReplayBuffState>? before,
        IEnumerable<MatchReplayBuffState>? after)
    {
        var previous = IndexBuffs(before);
        var current = IndexBuffs(after);
        foreach (var buffId in previous.Keys.Union(current.Keys, StringComparer.Ordinal)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            previous.TryGetValue(buffId, out var from);
            current.TryGetValue(buffId, out var to);
            if (from?.Level == to?.Level)
            {
                continue;
            }

            var action = from == null ? "BuffAdded" : to == null ? "BuffRemoved" : "BuffLevelChanged";
            AddOutcome(result, actionId, ref outcomeIndex, MatchSemanticCategories.Buff,
                MatchReplayPresentationCueKinds.Buff, action, actorId, sourceId, sourceInstanceId,
                targetId, (to?.Level ?? 0) - (from?.Level ?? 0), to?.Level ?? 0, buffId);
        }
    }

    private static void AddOutcome(
        MatchReplayDerivedActionData result,
        string actionId,
        ref int outcomeIndex,
        string category,
        string cueKind,
        string action,
        string actorId,
        string sourceId,
        string sourceInstanceId,
        string targetId,
        long value,
        long secondaryValue,
        string label)
    {
        var suffix = (++outcomeIndex).ToString("D3", CultureInfo.InvariantCulture);
        var eventId = actionId + ":outcome:" + suffix;
        result.Semantics.Add(new MatchSemanticEvent
        {
            EventId = eventId,
            ActionId = actionId,
            CauseId = actionId,
            RootActionId = actionId,
            Category = category,
            Action = action,
            ActorId = actorId,
            TargetId = targetId,
            SourceId = sourceId,
            SourceInstanceId = sourceInstanceId,
            TargetInstanceId = targetId,
            AttributionConfidence = MatchAttributionConfidence.Exact,
            Label = label,
            Value = value,
            SecondaryValue = secondaryValue,
            IsKeyEvent = category == MatchSemanticCategories.Damage
        });
        result.Presentation.Add(new MatchReplayPresentationCue
        {
            CueId = eventId,
            Kind = cueKind,
            StartOffsetMilliseconds = OutcomeOffsetMilliseconds,
            DurationMilliseconds = OutcomeDurationMilliseconds,
            ActorId = actorId,
            TargetIds = string.IsNullOrWhiteSpace(targetId)
                ? new List<string>()
                : new List<string> { targetId },
            Label = label,
            Value = value
        });
    }

    private static Dictionary<string, MatchReplayCardState> IndexCards(IEnumerable<MatchReplayCardState>? source)
    {
        return (source ?? Enumerable.Empty<MatchReplayCardState>())
            .Where(item => !string.IsNullOrWhiteSpace(item.ReplayCardId))
            .GroupBy(item => item.ReplayCardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    private static Dictionary<string, MatchReplayBuffState> IndexBuffs(IEnumerable<MatchReplayBuffState>? source)
    {
        return (source ?? Enumerable.Empty<MatchReplayBuffState>())
            .Where(item => !string.IsNullOrWhiteSpace(item.BuffId))
            .GroupBy(item => item.BuffId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    private static string ResolveDisposition(
        MatchReplayCardState? from,
        MatchReplayCardState? to,
        MatchReplayCardState? source,
        bool burning)
    {
        if (from == null && string.Equals(to?.Zone, "Hand", StringComparison.Ordinal))
        {
            return MatchReplayCardDispositionKinds.Draw;
        }

        if (from != null && to != null
                         && string.Equals(from.Zone, to.Zone, StringComparison.Ordinal)
                         && from.Order == to.Order)
        {
            return MatchReplayCardDispositionKinds.Update;
        }

        if (string.Equals(from?.Zone, "Hand", StringComparison.Ordinal))
        {
            if (string.Equals(to?.Zone, "Discard", StringComparison.Ordinal))
            {
                return MatchReplayCardDispositionKinds.Discard;
            }

            if (to == null)
            {
                return burning && string.Equals(from?.ReplayCardId, source?.ReplayCardId, StringComparison.Ordinal)
                    ? MatchReplayCardDispositionKinds.Burn
                    : MatchReplayCardDispositionKinds.Consume;
            }

            if (string.Equals(to.Zone, "Hand", StringComparison.Ordinal))
            {
                return MatchReplayCardDispositionKinds.Reorder;
            }
        }

        if (to == null)
        {
            return MatchReplayCardDispositionKinds.Remove;
        }

        return MatchReplayCardDispositionKinds.Move;
    }

    private static string Value(IEnumerable<MatchReplayStringValue>? values, string key)
    {
        return values?.LastOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Value ?? "";
    }

    private static string First(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static bool IsTrue(string value)
    {
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static bool EquivalentCardContent(MatchReplayCardState left, MatchReplayCardState right)
    {
        return string.Equals(left.CardId, right.CardId, StringComparison.Ordinal)
               && left.DataType == right.DataType
               && EquivalentValues(left.Data, right.Data)
               && EquivalentValues(left.Vars, right.Vars);
    }

    private static bool EquivalentValues(
        IEnumerable<MatchReplayStringValue>? left,
        IEnumerable<MatchReplayStringValue>? right)
    {
        var leftValues = (left ?? Enumerable.Empty<MatchReplayStringValue>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Select(item => (item.Key ?? "") + "\u001f" + (item.Value ?? ""));
        var rightValues = (right ?? Enumerable.Empty<MatchReplayStringValue>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Select(item => (item.Key ?? "") + "\u001f" + (item.Value ?? ""));
        return leftValues.SequenceEqual(rightValues, StringComparer.Ordinal);
    }
}

internal sealed class MatchReplaySeekCheckpoint
{
    public int TurnIndex { get; set; }

    public int CompletedActionCount { get; set; }

    public MatchReplayStateSnapshot State { get; set; } = new();

    public string StateHash { get; set; } = "";
}

internal sealed class MatchReplayStateDelta
{
    public string LevelId { get; set; } = "";

    public int TurnIndex { get; set; }

    public float EnemyPositive { get; set; }

    public float EnemyHp { get; set; }

    public int PlayerPower { get; set; }

    public int PlayerMaxPower { get; set; }

    public List<MatchReplayStatusState> StatusUpserts { get; set; } = new();

    public List<string> RemovedStatusIds { get; set; } = new();

    public bool ReplaceCards { get; set; }

    public int CardTopCount { get; set; }

    public bool CardTopCountChanged { get; set; }

    public List<MatchReplayCardState> Cards { get; set; } = new();

    public List<MatchReplayCardState> CardUpserts { get; set; } = new();

    public List<string> RemovedCardIds { get; set; } = new();

    public bool ReplaceEnemyIntents { get; set; }

    public List<MatchReplayEnemyIntentState> EnemyIntents { get; set; } = new();

    public List<MatchReplayEnemyIntentState> EnemyIntentUpserts { get; set; } = new();

    public List<string> RemovedEnemyIntentIds { get; set; } = new();
}

/// <summary>
/// Pure replay read model. It never calls Witch, Unity, networking, scripts, or combat commands.
/// Runtime playback projects snapshots produced here onto a presentation-only battle view.
/// </summary>
internal sealed class MatchReplayReadModel
{
    private MatchReplayStateSnapshot current = new();

    internal MatchReplayStateSnapshot Current => MatchReplayProjectionState.Clone(current);

    internal int CurrentTurn => Math.Max(1, current.TurnIndex);

    internal void Reset(MatchReplayStateSnapshot? baseline)
    {
        current = MatchReplayProjectionState.Clone(baseline ?? new MatchReplayStateSnapshot());
    }

    internal void Apply(MatchReplayStateDelta? delta)
    {
        current = MatchReplayProjectionState.Apply(current, delta);
    }
}

internal static class MatchReplayProjectionState
{
    internal static MatchReplayStateDelta CreateDelta(
        MatchReplayStateSnapshot? before,
        MatchReplayStateSnapshot after)
    {
        before ??= new MatchReplayStateSnapshot();
        after ??= new MatchReplayStateSnapshot();
        var previousIds = new HashSet<string>(
            before.Statuses.Select(item => item.InstanceId),
            StringComparer.Ordinal);
        var nextIds = new HashSet<string>(
            after.Statuses.Select(item => item.InstanceId),
            StringComparer.Ordinal);
        var changedStatusIds = new HashSet<string>(
            after.Statuses.Select(item => item.InstanceId),
            StringComparer.Ordinal);
        var previousCards = (before.Cards ?? new List<MatchReplayCardState>())
            .Where(card => !string.IsNullOrWhiteSpace(card.ReplayCardId))
            .GroupBy(card => card.ReplayCardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var nextCards = (after.Cards ?? new List<MatchReplayCardState>())
            .Where(card => !string.IsNullOrWhiteSpace(card.ReplayCardId))
            .GroupBy(card => card.ReplayCardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var previousIntents = (before.EnemyIntents ?? new List<MatchReplayEnemyIntentState>())
            .GroupBy(IntentKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var nextIntents = (after.EnemyIntents ?? new List<MatchReplayEnemyIntentState>())
            .GroupBy(IntentKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        return new MatchReplayStateDelta
        {
            LevelId = after.LevelId ?? "",
            TurnIndex = Math.Max(1, after.TurnIndex),
            EnemyPositive = after.EnemyPositive,
            EnemyHp = after.EnemyHp,
            PlayerPower = after.PlayerPower,
            PlayerMaxPower = after.PlayerMaxPower,
            StatusUpserts = after.Statuses
                .Where(status => changedStatusIds.Contains(status.InstanceId))
                .Select(Clone)
                .ToList(),
            RemovedStatusIds = previousIds.Where(id => !nextIds.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList(),
            ReplaceCards = false,
            CardTopCount = after.CardTopCount,
            CardTopCountChanged = before.CardTopCount != after.CardTopCount,
            Cards = new List<MatchReplayCardState>(),
            CardUpserts = nextCards.Values
                .Where(card => !previousCards.TryGetValue(card.ReplayCardId, out var previous)
                               || !EquivalentCard(previous, card))
                .OrderBy(card => card.Zone, StringComparer.Ordinal)
                .ThenBy(card => card.Order)
                .ThenBy(card => card.ReplayCardId, StringComparer.Ordinal)
                .Select(Clone)
                .ToList(),
            RemovedCardIds = previousCards.Keys
                .Where(id => !nextCards.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList(),
            ReplaceEnemyIntents = false,
            EnemyIntents = new List<MatchReplayEnemyIntentState>(),
            EnemyIntentUpserts = nextIntents
                .Where(pair => !previousIntents.TryGetValue(pair.Key, out var previous)
                               || !EquivalentIntent(previous, pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Clone(pair.Value))
                .ToList(),
            RemovedEnemyIntentIds = previousIntents.Keys
                .Where(id => !nextIntents.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList()
        };
    }

    internal static MatchReplayStateSnapshot Apply(
        MatchReplayStateSnapshot? source,
        MatchReplayStateDelta? delta)
    {
        var result = Clone(source ?? new MatchReplayStateSnapshot());
        if (delta == null)
        {
            return result;
        }

        result.LevelId = delta.LevelId ?? "";
        result.TurnIndex = Math.Max(1, delta.TurnIndex);
        result.EnemyPositive = delta.EnemyPositive;
        result.EnemyHp = delta.EnemyHp;
        result.PlayerPower = delta.PlayerPower;
        result.PlayerMaxPower = delta.PlayerMaxPower;
        var statuses = result.Statuses.ToDictionary(item => item.InstanceId, Clone, StringComparer.Ordinal);
        foreach (var removed in delta.RemovedStatusIds ?? new List<string>())
        {
            statuses.Remove(removed ?? "");
        }

        foreach (var status in delta.StatusUpserts ?? new List<MatchReplayStatusState>())
        {
            if (!string.IsNullOrWhiteSpace(status.InstanceId))
            {
                statuses[status.InstanceId] = Clone(status);
            }
        }

        result.Statuses = statuses.Values.OrderBy(item => item.InstanceId, StringComparer.Ordinal).ToList();
        result.CardTopCount = delta.CardTopCount;
        if (delta.ReplaceCards)
        {
            result.Cards = (delta.Cards ?? new List<MatchReplayCardState>()).Select(Clone).ToList();
        }
        else
        {
            var cards = result.Cards
                .Where(card => !string.IsNullOrWhiteSpace(card.ReplayCardId))
                .ToDictionary(card => card.ReplayCardId, Clone, StringComparer.Ordinal);
            foreach (var removed in delta.RemovedCardIds ?? new List<string>())
            {
                cards.Remove(removed ?? "");
            }

            foreach (var card in delta.CardUpserts ?? new List<MatchReplayCardState>())
            {
                if (!string.IsNullOrWhiteSpace(card.ReplayCardId))
                {
                    cards[card.ReplayCardId] = Clone(card);
                }
            }

            result.Cards = cards.Values
                .OrderBy(card => card.Zone, StringComparer.Ordinal)
                .ThenBy(card => card.Order)
                .ThenBy(card => card.ReplayCardId, StringComparer.Ordinal)
                .ToList();
        }


        if (delta.ReplaceEnemyIntents)
        {
            result.EnemyIntents = (delta.EnemyIntents ?? new List<MatchReplayEnemyIntentState>())
                .Select(Clone)
                .ToList();
        }
        else
        {
            var intents = result.EnemyIntents.ToDictionary(IntentKey, Clone, StringComparer.Ordinal);
            foreach (var removed in delta.RemovedEnemyIntentIds ?? new List<string>())
            {
                intents.Remove(removed ?? "");
            }

            foreach (var intent in delta.EnemyIntentUpserts ?? new List<MatchReplayEnemyIntentState>())
            {
                intents[IntentKey(intent)] = Clone(intent);
            }

            result.EnemyIntents = intents.Values
                .OrderBy(intent => intent.ActorId, StringComparer.Ordinal)
                .ThenBy(intent => intent.SlotIndex)
                .ThenBy(intent => intent.SourceInstanceId, StringComparer.Ordinal)
                .ToList();
        }

        return result;
    }

    internal static MatchReplayStateSnapshot Clone(MatchReplayStateSnapshot source)
    {
        return new MatchReplayStateSnapshot
        {
            LevelId = source.LevelId ?? "",
            TurnIndex = source.TurnIndex,
            EnemyPositive = source.EnemyPositive,
            EnemyHp = source.EnemyHp,
            PlayerPower = source.PlayerPower,
            PlayerMaxPower = source.PlayerMaxPower,
            RoleTableJson = source.RoleTableJson ?? "",
            CardTopCount = source.CardTopCount,
            Statuses = (source.Statuses ?? new List<MatchReplayStatusState>()).Select(Clone).ToList(),
            Cards = (source.Cards ?? new List<MatchReplayCardState>()).Select(Clone).ToList(),
            EnemyIntents = (source.EnemyIntents ?? new List<MatchReplayEnemyIntentState>()).Select(Clone).ToList()
        };
    }

    internal static string Hash(MatchReplayStateSnapshot? source)
    {
        var state = source ?? new MatchReplayStateSnapshot();
        var hash = new StableHash64();
        hash.Add(state.LevelId);
        hash.Add(state.TurnIndex);
        hash.Add(state.EnemyPositive);
        hash.Add(state.EnemyHp);
        hash.Add(state.PlayerPower);
        hash.Add(state.PlayerMaxPower);
        hash.Add(state.CardTopCount);
        foreach (var status in (state.Statuses ?? new List<MatchReplayStatusState>())
                     .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            hash.Add(status.InstanceId);
            hash.Add(status.ContentOwnerModId);
            hash.Add(status.ContentId);
            hash.Add(status.EntityKind);
            hash.Add(status.SlotIndex);
            hash.Add(status.MaxHp);
            hash.Add(status.CurrentHp);
            hash.Add(status.Defend);
            hash.Add(status.State);
            foreach (var variable in (status.DynamicVariables ?? new List<MatchReplayFloatValue>())
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(variable.Key);
                hash.Add(variable.Value);
            }

            foreach (var buff in (status.Buffs ?? new List<MatchReplayBuffState>())
                         .OrderBy(item => item.BuffId, StringComparer.Ordinal))
            {
                hash.Add(buff.BuffId);
                hash.Add(buff.Level);
                hash.Add(buff.UpperBound);
                hash.Add(buff.ReducePerTurn);
                hash.Add(buff.ReducePerUse);
                hash.Add(buff.ReducePerAttacked);
                foreach (var value in (buff.Vars ?? new List<MatchReplayStringValue>())
                             .OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    hash.Add(value.Key);
                    hash.Add(value.Value);
                }
            }
        }

        foreach (var intent in (state.EnemyIntents ?? new List<MatchReplayEnemyIntentState>())
                     .OrderBy(item => item.ActorId, StringComparer.Ordinal)
                     .ThenBy(item => item.SlotIndex)
                     .ThenBy(item => item.SourceInstanceId, StringComparer.Ordinal))
        {
            hash.Add(intent.ActorId);
            hash.Add(intent.SlotIndex);
            hash.Add(intent.IntentId);
            hash.Add(intent.SourceInstanceId);
            hash.Add(intent.Label);
            hash.Add(intent.Description);
            hash.Add(intent.Icon);
            hash.Add(intent.BackIcon);
            hash.Add(intent.DisplayValue);
            hash.Add(intent.ActionState);
            hash.Add(intent.EffectName);
            foreach (var targetId in (intent.TargetIds ?? new List<string>()).OrderBy(id => id, StringComparer.Ordinal))
            {
                hash.Add(targetId);
            }
        }

        foreach (var card in (state.Cards ?? new List<MatchReplayCardState>())
                     .OrderBy(item => item.Zone, StringComparer.Ordinal)
                     .ThenBy(item => item.Order)
                     .ThenBy(item => item.ReplayCardId, StringComparer.Ordinal))
        {
            hash.Add(card.Zone);
            hash.Add(card.Order);
            hash.Add(card.ReplayCardId);
            hash.Add(card.CardId);
            hash.Add(card.DataType);
            foreach (var value in (card.Data ?? new List<MatchReplayStringValue>())
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(value.Key);
                hash.Add(value.Value);
            }

            foreach (var value in (card.Vars ?? new List<MatchReplayStringValue>())
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(value.Key);
                hash.Add(value.Value);
            }
        }

        return hash.Value.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static MatchReplayStatusState Clone(MatchReplayStatusState source)
    {
        return new MatchReplayStatusState
        {
            InstanceId = source.InstanceId ?? "",
            ContentOwnerModId = source.ContentOwnerModId ?? "",
            ContentId = source.ContentId ?? "",
            EntityKind = source.EntityKind ?? "",
            SlotIndex = source.SlotIndex,
            MaxHp = source.MaxHp,
            CurrentHp = source.CurrentHp,
            Defend = source.Defend,
            State = source.State ?? "",
            DynamicVariables = (source.DynamicVariables ?? new List<MatchReplayFloatValue>())
                .Select(item => new MatchReplayFloatValue { Key = item.Key ?? "", Value = item.Value })
                .ToList(),
            Buffs = (source.Buffs ?? new List<MatchReplayBuffState>()).Select(Clone).ToList()
        };
    }

    private static MatchReplayBuffState Clone(MatchReplayBuffState source)
    {
        return new MatchReplayBuffState
        {
            BuffId = source.BuffId ?? "",
            Level = source.Level,
            UpperBound = source.UpperBound,
            ReducePerTurn = source.ReducePerTurn,
            ReducePerUse = source.ReducePerUse,
            ReducePerAttacked = source.ReducePerAttacked,
            Vars = (source.Vars ?? new List<MatchReplayStringValue>())
                .Select(Clone)
                .ToList()
        };
    }

    private static MatchReplayCardState Clone(MatchReplayCardState source)
    {
        return new MatchReplayCardState
        {
            Zone = source.Zone ?? "",
            Order = source.Order,
            ReplayCardId = source.ReplayCardId ?? "",
            CardId = source.CardId ?? "",
            DataType = source.DataType,
            Data = (source.Data ?? new List<MatchReplayStringValue>()).Select(Clone).ToList(),
            Vars = (source.Vars ?? new List<MatchReplayStringValue>()).Select(Clone).ToList()
        };
    }

    internal static MatchReplayEnemyIntentState Clone(MatchReplayEnemyIntentState source)
    {
        return new MatchReplayEnemyIntentState
        {
            ActorId = source.ActorId ?? "",
            SlotIndex = source.SlotIndex,
            IntentId = source.IntentId ?? "",
            SourceInstanceId = source.SourceInstanceId ?? "",
            Label = source.Label ?? "",
            Description = source.Description ?? "",
            Icon = source.Icon ?? "",
            BackIcon = source.BackIcon ?? "",
            DisplayValue = source.DisplayValue ?? "",
            ActionState = source.ActionState ?? "",
            EffectName = source.EffectName ?? "",
            TargetIds = (source.TargetIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
        };
    }

    internal static bool HasCardChanges(MatchReplayStateDelta? delta)
    {
        return delta != null
               && (delta.ReplaceCards
                   || delta.CardTopCountChanged
                   || (delta.CardUpserts?.Count ?? 0) > 0
                   || (delta.RemovedCardIds?.Count ?? 0) > 0);
    }

    internal static bool HasCardIdentityChanges(MatchReplayStateDelta? delta)
    {
        return delta != null
               && (delta.ReplaceCards
                   || (delta.CardUpserts?.Count ?? 0) > 0
                   || (delta.RemovedCardIds?.Count ?? 0) > 0);
    }

    private static bool EquivalentCard(MatchReplayCardState left, MatchReplayCardState right)
    {
        return string.Equals(left.Zone, right.Zone, StringComparison.Ordinal)
               && left.Order == right.Order
               && string.Equals(left.CardId, right.CardId, StringComparison.Ordinal)
               && left.DataType == right.DataType
               && EquivalentValues(left.Data, right.Data)
               && EquivalentValues(left.Vars, right.Vars);
    }

    private static bool EquivalentIntent(MatchReplayEnemyIntentState left, MatchReplayEnemyIntentState right)
    {
        return string.Equals(left.IntentId, right.IntentId, StringComparison.Ordinal)
               && string.Equals(left.Label, right.Label, StringComparison.Ordinal)
               && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
               && string.Equals(left.Icon, right.Icon, StringComparison.Ordinal)
               && string.Equals(left.BackIcon, right.BackIcon, StringComparison.Ordinal)
               && string.Equals(left.DisplayValue, right.DisplayValue, StringComparison.Ordinal)
               && string.Equals(left.ActionState, right.ActionState, StringComparison.Ordinal)
               && string.Equals(left.EffectName, right.EffectName, StringComparison.Ordinal)
               && (left.TargetIds ?? new List<string>()).SequenceEqual(
                   right.TargetIds ?? new List<string>(),
                   StringComparer.Ordinal);
    }

    private static bool EquivalentValues(
        IEnumerable<MatchReplayStringValue>? left,
        IEnumerable<MatchReplayStringValue>? right)
    {
        return (left ?? Enumerable.Empty<MatchReplayStringValue>())
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .ThenBy(value => value.Value, StringComparer.Ordinal)
            .Select(value => (value.Key ?? "") + "\u001f" + (value.Value ?? ""))
            .SequenceEqual(
                (right ?? Enumerable.Empty<MatchReplayStringValue>())
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ThenBy(value => value.Value, StringComparer.Ordinal)
                .Select(value => (value.Key ?? "") + "\u001f" + (value.Value ?? "")),
                StringComparer.Ordinal);
    }

    private static string IntentKey(MatchReplayEnemyIntentState intent)
    {
        return (intent.ActorId ?? "") + "\u001f"
               + intent.SlotIndex.ToString(CultureInfo.InvariantCulture) + "\u001f"
               + (intent.SourceInstanceId ?? "");
    }

    private static MatchReplayStringValue Clone(MatchReplayStringValue source)
    {
        return new MatchReplayStringValue { Key = source.Key ?? "", Value = source.Value ?? "" };
    }

    private struct StableHash64
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong value;
        private bool initialized;

        internal ulong Value => initialized ? value : Offset;

        internal void Add(string? text)
        {
            EnsureInitialized();
            foreach (var character in text ?? "")
            {
                value ^= character;
                value *= Prime;
            }

            value ^= 0xff;
            value *= Prime;
        }

        internal void Add(int number)
        {
            Add(unchecked((uint)number));
        }

        internal void Add(float number)
        {
            Add(unchecked((uint)number.GetHashCode()));
        }

        private void Add(uint number)
        {
            EnsureInitialized();
            for (var shift = 0; shift < 32; shift += 8)
            {
                value ^= (byte)(number >> shift);
                value *= Prime;
            }
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                value = Offset;
                initialized = true;
            }
        }
    }
}
