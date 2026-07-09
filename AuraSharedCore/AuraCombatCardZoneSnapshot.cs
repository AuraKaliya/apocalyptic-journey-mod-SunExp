using System;
using System.Collections.Generic;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace AuraShared.Core;

public enum AuraCombatCardZoneKind
{
    Unknown,
    FightUiActive,
    FightUiWait,
    ExecutorHand,
    ExecutorWait,
    ExecutorDeck,
    ExecutorUsed,
    ManagerDraw,
    ManagerUsed
}

public sealed class AuraCombatCardZoneSnapshotOptions
{
    public bool IncludeFightUiActive { get; set; } = true;

    public bool IncludeFightUiWait { get; set; } = true;

    public bool IncludeExecutorHand { get; set; } = true;

    public bool IncludeExecutorWait { get; set; } = true;

    public bool IncludeExecutorDeck { get; set; }

    public bool IncludeExecutorUsed { get; set; }

    public bool IncludeManagerDraw { get; set; }

    public bool IncludeManagerUsed { get; set; }
}

public sealed class AuraCombatCardReference
{
    public AuraCombatCardZoneKind Zone { get; set; }

    public CardItem? Card { get; set; }

    public IDataConfig? Config { get; set; }

    public Transform? Root { get; set; }

    public string CardId { get; set; } = "";

    public string InstanceId { get; set; } = "";
}

public sealed class AuraCombatCardZoneSnapshot
{
    private readonly IReadOnlyDictionary<AuraCombatCardZoneKind, int> zoneCounts;

    private AuraCombatCardZoneSnapshot(
        IReadOnlyList<AuraCombatCardReference> cards,
        IReadOnlyDictionary<AuraCombatCardZoneKind, int> zoneCounts)
    {
        Cards = cards;
        this.zoneCounts = zoneCounts;
    }

    public IReadOnlyList<AuraCombatCardReference> Cards { get; }

    public int Count(AuraCombatCardZoneKind zone)
    {
        return zoneCounts.TryGetValue(zone, out var count) ? count : 0;
    }

    public static AuraCombatCardZoneSnapshot Capture(
        ScriptExecutor? executor,
        AuraCombatCardZoneSnapshotOptions? options = null)
    {
        options ??= new AuraCombatCardZoneSnapshotOptions();

        var builder = new Builder();
        if (options.IncludeFightUiActive)
        {
            builder.AddCardItems(FightUI.cardItemList, AuraCombatCardZoneKind.FightUiActive);
        }

        if (options.IncludeFightUiWait)
        {
            builder.AddCardItems(FightUI.WaitCard, AuraCombatCardZoneKind.FightUiWait);
        }

        if (executor != null)
        {
            if (options.IncludeExecutorHand)
            {
                builder.AddCardItems(executor.HandCard, AuraCombatCardZoneKind.ExecutorHand);
            }

            if (options.IncludeExecutorWait)
            {
                builder.AddCardItems(executor.WaitCard, AuraCombatCardZoneKind.ExecutorWait);
            }

            if (options.IncludeExecutorDeck)
            {
                builder.AddConfigs(executor.DeckCard, AuraCombatCardZoneKind.ExecutorDeck);
            }

            if (options.IncludeExecutorUsed)
            {
                builder.AddConfigs(executor.UsedCard, AuraCombatCardZoneKind.ExecutorUsed);
            }
        }

        var manager = FightCardManager.Instance;
        if (manager != null)
        {
            if (options.IncludeManagerDraw)
            {
                builder.AddConfigs(manager.cardList, AuraCombatCardZoneKind.ManagerDraw);
            }

            if (options.IncludeManagerUsed)
            {
                builder.AddConfigs(manager.usedCardList, AuraCombatCardZoneKind.ManagerUsed);
            }
        }

        return new AuraCombatCardZoneSnapshot(builder.ToArray(), builder.Counts());
    }

    private sealed class Builder
    {
        private readonly List<AuraCombatCardReference> cards = new();
        private readonly Dictionary<AuraCombatCardZoneKind, int> zoneCounts = new();
        private readonly HashSet<CardItem> seenCards = new();
        private readonly HashSet<IDataConfig> seenConfigs = new();
        private readonly HashSet<string> seenConfigInstanceIds = new(StringComparer.Ordinal);

        public void AddCardItems(IEnumerable<CardItem>? source, AuraCombatCardZoneKind zone)
        {
            if (source == null)
            {
                return;
            }

            foreach (var card in source)
            {
                Increment(zone);
                AddCard(card, zone);
            }
        }

        public void AddConfigs(IEnumerable<IDataConfig>? source, AuraCombatCardZoneKind zone)
        {
            if (source == null)
            {
                return;
            }

            foreach (var config in source)
            {
                Increment(zone);
                AddConfig(config, zone);
            }
        }

        public AuraCombatCardReference[] ToArray()
        {
            return cards.ToArray();
        }

        public IReadOnlyDictionary<AuraCombatCardZoneKind, int> Counts()
        {
            return new Dictionary<AuraCombatCardZoneKind, int>(zoneCounts);
        }

        private void AddCard(CardItem? card, AuraCombatCardZoneKind zone)
        {
            if (card == null || !seenCards.Add(card))
            {
                return;
            }

            var config = card.dataConfig;
            if (!MarkConfig(config))
            {
                return;
            }

            cards.Add(new AuraCombatCardReference
            {
                Zone = zone,
                Card = card,
                Config = config,
                Root = card.transform,
                CardId = ReadCardId(config, card.data),
                InstanceId = ReadInstanceId(config)
            });
        }

        private void AddConfig(IDataConfig? config, AuraCombatCardZoneKind zone)
        {
            if (!MarkConfig(config))
            {
                return;
            }

            cards.Add(new AuraCombatCardReference
            {
                Zone = zone,
                Config = config,
                CardId = ReadCardId(config, null),
                InstanceId = ReadInstanceId(config)
            });
        }

        private bool MarkConfig(IDataConfig? config)
        {
            if (config == null)
            {
                return true;
            }

            var instanceId = ReadInstanceId(config);
            if (instanceId.Length > 0 && !seenConfigInstanceIds.Add(instanceId))
            {
                return false;
            }

            return seenConfigs.Add(config);
        }

        private void Increment(AuraCombatCardZoneKind zone)
        {
            zoneCounts.TryGetValue(zone, out var count);
            zoneCounts[zone] = count + 1;
        }
    }

    private static string ReadCardId(IDataConfig? config, IDictionary<string, string>? fallbackData)
    {
        return ReadValue(config?.Vars, "Id")
               ?? ReadValue(config?.data, "Id")
               ?? ReadValue(fallbackData, "Id")
               ?? "";
    }

    private static string ReadInstanceId(IDataConfig? config)
    {
        try
        {
            return (config?.InstanceID ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string? ReadValue(IDictionary<string, string>? values, string key)
    {
        if (values == null || !values.TryGetValue(key, out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
