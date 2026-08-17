using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal sealed class MatchReplayStateDiff
{
    internal List<string> Paths { get; } = new();

    internal bool IsMatch => Paths.Count == 0;

    internal string Summary(int maximum = 8)
    {
        if (Paths.Count == 0)
        {
            return "none";
        }

        var selected = Paths.Take(Math.Max(1, maximum));
        return string.Join(", ", selected)
               + (Paths.Count > maximum ? " (+" + (Paths.Count - maximum) + ")" : "");
    }
}

internal static class MatchReplayStateComparer
{
    internal static MatchReplayStateDiff Compare(
        MatchReplayStateSnapshot expected,
        MatchReplayStateSnapshot actual)
    {
        var diff = new MatchReplayStateDiff();
        Add(diff, "level", expected.LevelId, actual.LevelId);
        Add(diff, "turn", expected.TurnIndex, actual.TurnIndex);
        Add(diff, "enemy.positive", expected.EnemyPositive, actual.EnemyPositive);
        Add(diff, "enemy.hp", expected.EnemyHp, actual.EnemyHp);
        Add(diff, "player.power", expected.PlayerPower, actual.PlayerPower);
        Add(diff, "player.maxPower", expected.PlayerMaxPower, actual.PlayerMaxPower);
        Add(diff, "cards.top", expected.CardTopCount, actual.CardTopCount);

        CompareStatuses(diff, expected.Statuses, actual.Statuses);
        CompareCards(diff, expected.Cards, actual.Cards);
        CompareEnemyIntents(diff, expected.EnemyIntents, actual.EnemyIntents);
        return diff;
    }

    private static void CompareEnemyIntents(
        MatchReplayStateDiff diff,
        IEnumerable<MatchReplayEnemyIntentState>? expected,
        IEnumerable<MatchReplayEnemyIntentState>? actual)
    {
        var left = (expected ?? Enumerable.Empty<MatchReplayEnemyIntentState>())
            .OrderBy(IntentKey, StringComparer.Ordinal)
            .ToList();
        var right = (actual ?? Enumerable.Empty<MatchReplayEnemyIntentState>())
            .OrderBy(IntentKey, StringComparer.Ordinal)
            .ToList();
        if (left.Count != right.Count)
        {
            diff.Paths.Add("intent[count]");
        }

        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var prefix = "intent[" + left[index].ActorId + ":" + left[index].SlotIndex + "]";
            Add(diff, prefix + ".actor", left[index].ActorId, right[index].ActorId);
            Add(diff, prefix + ".slot", left[index].SlotIndex, right[index].SlotIndex);
            Add(diff, prefix + ".id", left[index].IntentId, right[index].IntentId);
            Add(diff, prefix + ".instance", left[index].SourceInstanceId, right[index].SourceInstanceId);
            Add(diff, prefix + ".label", left[index].Label, right[index].Label);
            Add(diff, prefix + ".description", left[index].Description, right[index].Description);
            Add(diff, prefix + ".icon", left[index].Icon, right[index].Icon);
            Add(diff, prefix + ".backIcon", left[index].BackIcon, right[index].BackIcon);
            Add(diff, prefix + ".value", left[index].DisplayValue, right[index].DisplayValue);
            Add(diff, prefix + ".action", left[index].ActionState, right[index].ActionState);
            Add(diff, prefix + ".effect", left[index].EffectName, right[index].EffectName);
            var leftTargets = (left[index].TargetIds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal);
            var rightTargets = (right[index].TargetIds ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal);
            if (!leftTargets.SequenceEqual(rightTargets, StringComparer.Ordinal))
            {
                diff.Paths.Add(prefix + ".targets");
            }
        }
    }

    private static void CompareStatuses(
        MatchReplayStateDiff diff,
        IEnumerable<MatchReplayStatusState> expected,
        IEnumerable<MatchReplayStatusState> actual)
    {
        var expectedMap = expected.ToDictionary(item => item.InstanceId ?? "", StringComparer.Ordinal);
        var actualMap = actual.ToDictionary(item => item.InstanceId ?? "", StringComparer.Ordinal);
        foreach (var id in expectedMap.Keys.Union(actualMap.Keys, StringComparer.Ordinal).OrderBy(value => value))
        {
            if (!expectedMap.TryGetValue(id, out var left) || !actualMap.TryGetValue(id, out var right))
            {
                diff.Paths.Add("status[" + id + "].presence");
                continue;
            }

            Add(diff, "status[" + id + "].maxHp", left.MaxHp, right.MaxHp);
            Add(diff, "status[" + id + "].hp", left.CurrentHp, right.CurrentHp);
            Add(diff, "status[" + id + "].defend", left.Defend, right.Defend);
            Add(diff, "status[" + id + "].state", left.State, right.State);
            CompareFloatValues(diff, "status[" + id + "].var", left.DynamicVariables, right.DynamicVariables);
            CompareBuffs(diff, id, left.Buffs, right.Buffs);
        }
    }

    private static void CompareBuffs(
        MatchReplayStateDiff diff,
        string statusId,
        IEnumerable<MatchReplayBuffState> expected,
        IEnumerable<MatchReplayBuffState> actual)
    {
        var leftMap = expected.ToDictionary(item => item.BuffId ?? "", StringComparer.Ordinal);
        var rightMap = actual.ToDictionary(item => item.BuffId ?? "", StringComparer.Ordinal);
        foreach (var id in leftMap.Keys.Union(rightMap.Keys, StringComparer.Ordinal).OrderBy(value => value))
        {
            var prefix = "status[" + statusId + "].buff[" + id + "]";
            if (!leftMap.TryGetValue(id, out var left) || !rightMap.TryGetValue(id, out var right))
            {
                diff.Paths.Add(prefix + ".presence");
                continue;
            }

            Add(diff, prefix + ".level", left.Level, right.Level);
            Add(diff, prefix + ".upper", left.UpperBound, right.UpperBound);
            Add(diff, prefix + ".turn", left.ReducePerTurn, right.ReducePerTurn);
            Add(diff, prefix + ".use", left.ReducePerUse, right.ReducePerUse);
            Add(diff, prefix + ".attacked", left.ReducePerAttacked, right.ReducePerAttacked);
            CompareStringValues(diff, prefix + ".var", left.Vars, right.Vars);
        }
    }

    private static void CompareCards(
        MatchReplayStateDiff diff,
        IEnumerable<MatchReplayCardState> expected,
        IEnumerable<MatchReplayCardState> actual)
    {
        var left = expected.OrderBy(CardKey, StringComparer.Ordinal).ToList();
        var right = actual.OrderBy(CardKey, StringComparer.Ordinal).ToList();
        if (left.Count != right.Count)
        {
            diff.Paths.Add("cards.count");
        }

        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var prefix = "card[" + i + "]";
            Add(diff, prefix + ".zone", left[i].Zone, right[i].Zone);
            Add(diff, prefix + ".order", left[i].Order, right[i].Order);
            Add(diff, prefix + ".instance", left[i].ReplayCardId, right[i].ReplayCardId);
            Add(diff, prefix + ".id", left[i].CardId, right[i].CardId);
            Add(diff, prefix + ".type", left[i].DataType, right[i].DataType);
            CompareStringValues(diff, prefix + ".data", left[i].Data, right[i].Data);
            CompareStringValues(diff, prefix + ".var", left[i].Vars, right[i].Vars);
        }
    }

    private static string CardKey(MatchReplayCardState card)
    {
        return (card.Zone ?? "") + "\u001f" + card.Order.ToString("D8", CultureInfo.InvariantCulture)
               + "\u001f" + (card.ReplayCardId ?? "") + "\u001f" + (card.CardId ?? "");
    }

    private static string IntentKey(MatchReplayEnemyIntentState intent)
    {
        return (intent.ActorId ?? "") + "\u001f"
               + intent.SlotIndex.ToString("D4", CultureInfo.InvariantCulture) + "\u001f"
               + (intent.SourceInstanceId ?? "") + "\u001f" + (intent.IntentId ?? "");
    }

    private static void CompareFloatValues(
        MatchReplayStateDiff diff,
        string prefix,
        IEnumerable<MatchReplayFloatValue> expected,
        IEnumerable<MatchReplayFloatValue> actual)
    {
        var left = expected.ToDictionary(item => item.Key ?? "", item => item.Value, StringComparer.Ordinal);
        var right = actual.ToDictionary(item => item.Key ?? "", item => item.Value, StringComparer.Ordinal);
        foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(value => value))
        {
            if (!left.TryGetValue(key, out var leftValue)
                || !right.TryGetValue(key, out var rightValue)
                || !leftValue.Equals(rightValue))
            {
                diff.Paths.Add(prefix + "[" + key + "]");
            }
        }
    }

    private static void CompareStringValues(
        MatchReplayStateDiff diff,
        string prefix,
        IEnumerable<MatchReplayStringValue> expected,
        IEnumerable<MatchReplayStringValue> actual)
    {
        var left = expected.ToDictionary(item => item.Key ?? "", item => item.Value ?? "", StringComparer.Ordinal);
        var right = actual.ToDictionary(item => item.Key ?? "", item => item.Value ?? "", StringComparer.Ordinal);
        foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(value => value))
        {
            if (!left.TryGetValue(key, out var leftValue)
                || !right.TryGetValue(key, out var rightValue)
                || !string.Equals(leftValue, rightValue, StringComparison.Ordinal))
            {
                diff.Paths.Add(prefix + "[" + key + "]");
            }
        }
    }

    private static void Add<T>(MatchReplayStateDiff diff, string path, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            diff.Paths.Add(path);
        }
    }
}
