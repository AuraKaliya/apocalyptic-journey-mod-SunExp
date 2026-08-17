using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Reads the already selected native enemy intent queue. It never selects an intent,
/// runs InitScript/TargetScript/UseScript, or mutates ObjectAction.
/// </summary>
internal static class MatchReplayEnemyIntentApi
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SelectedCardsField = typeof(ObjectAction).GetField("CardList", InstanceFields);
    private static bool fallbackLogged;

    internal static MatchReplayEnemyIntentState? CaptureExecuting(object? target, object[]? arguments)
    {
        if (target is not Enemy enemy || enemy.Status == null || enemy.FightAction == null)
        {
            return null;
        }

        var slot = arguments != null && arguments.Length > 0 && arguments[0] is int value
            ? Math.Max(0, value)
            : 0;
        var card = enemy.FightAction.TryGetCard();
        if (card == null && enemy.ActionCards != null && slot < enemy.ActionCards.Count)
        {
            card = enemy.ActionCards[slot];
        }

        return Capture(enemy, card, slot);
    }

    internal static List<MatchReplayEnemyIntentState> CapturePlans()
    {
        var result = new List<MatchReplayEnemyIntentState>();
        var enemies = EnemyManager.Instance?.enemyList;
        if (enemies == null)
        {
            return result;
        }

        foreach (var enemy in enemies.Where(item => item != null)
                     .OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            var cards = SelectedCards(enemy);
            var usedSlots = new HashSet<int>();
            for (var index = 0; index < cards.Count; index++)
            {
                var slot = ResolveDisplaySlot(enemy.ActionCards, cards[index], index, usedSlots);
                var intent = Capture(enemy, cards[index], slot);
                if (intent != null)
                {
                    result.Add(intent);
                }
            }
        }

        return result;
    }

    private static int ResolveDisplaySlot(
        IReadOnlyList<ObjectCard>? displayed,
        ObjectCard selected,
        int fallback,
        ISet<int> used)
    {
        if (displayed != null)
        {
            for (var index = 0; index < displayed.Count; index++)
            {
                if (!used.Contains(index) && ReferenceEquals(displayed[index], selected))
                {
                    used.Add(index);
                    return index;
                }
            }
        }

        var slot = Math.Max(0, fallback);
        used.Add(slot);
        return slot;
    }

    private static List<ObjectCard> SelectedCards(Enemy enemy)
    {
        try
        {
            if (enemy.FightAction != null
                && SelectedCardsField?.GetValue(enemy.FightAction) is IEnumerable selected)
            {
                return selected.Cast<object>()
                    .OfType<ObjectCard>()
                    .Where(item => item != null)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            LogFallbackOnce("selected enemy intent queue could not be read: " + ex.Message);
        }

        LogFallbackOnce("selected enemy intent queue field is unavailable; using the public action list");
        return (enemy.ActionCards ?? new List<ObjectCard>())
            .Where(item => item != null)
            .ToList();
    }

    private static MatchReplayEnemyIntentState? Capture(Enemy enemy, ObjectCard? card, int slot)
    {
        var config = card?.dataConfig;
        if (config == null || enemy.Status == null)
        {
            return null;
        }

        var data = config.data;
        var vars = config.Vars;
        var executor = config.scriptExecutor;
        return new MatchReplayEnemyIntentState
        {
            ActorId = enemy.Status.InstanceId ?? enemy.InstanceId ?? "",
            SlotIndex = Math.Max(0, slot),
            IntentId = First(Read(data, "Id"), Read(vars, "Id")),
            SourceInstanceId = config.InstanceID ?? "",
            Label = First(Read(vars, "Name"), Read(data, "Name"), Read(data, "DisplayName"), Read(data, "Id")),
            Description = First(Read(vars, "Description"), Read(data, "Description")),
            Icon = First(Read(vars, "Icon"), Read(data, "Icon")),
            BackIcon = First(Read(vars, "BackIcon"), Read(data, "BackIcon")),
            DisplayValue = First(Read(vars, "DesVal1"), Read(data, "DesVal1")),
            ActionState = First(Read(vars, "Action"), Read(data, "Action")),
            EffectName = First(Read(vars, "Effects"), Read(data, "Effects")),
            TargetIds = (executor?.Object ?? new List<IStatusManager>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InstanceId))
                .Select(item => item.InstanceId)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    private static string Read(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static string First(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static void LogFallbackOnce(string detail)
    {
        if (fallbackLogged)
        {
            return;
        }

        fallbackLogged = true;
        AuraToolsLog.Warn("[MatchRecords] enemy intent capture degraded: " + detail + ".");
    }
}
