using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraGameData.Shared.Application;
using AuraShared.Core;
using UnityEngine;

namespace AuraGameData.Shared.GameApi;

public sealed class WitchCardInstancePort : IAuraCardInstancePort
{
    private readonly ScriptExecutor? executor;

    public WitchCardInstancePort(ScriptExecutor? executor = null)
    {
        this.executor = executor;
    }

    public AuraGameInstanceCommandResult Grant(AuraCardGrantCommand command)
    {
        if (!string.Equals(command.TargetZone, "Hand", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(command.TargetZone, "ManagerDraw", StringComparison.OrdinalIgnoreCase))
            {
                return GrantToManagerDraw(command);
            }
            return GrantToOwnedCollection(command);
        }

        if (executor == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "executor", "Hand grants require a ScriptExecutor.");
        }

        if (!AuraGameDataCatalogRuntime.ValidateHandle(command.Definition, out var snapshot) || snapshot == null
            || !string.Equals(snapshot.Key.DataType, DataType.Card.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "handle", "Card definition handle is invalid.");
        }

        var cards = FightCardManager.Instance?.cardList;
        if (cards == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "manager", "FightCardManager card list is unavailable.");
        }

        var before = new HashSet<DataConfig>(cards);
        DataConfig? added = null;
        try
        {
            executor.SetStatus("Self");
            executor.AddCardByData(snapshot.Key.Id, command.RuntimeTags ?? "");
            added = cards.LastOrDefault(card => !before.Contains(card)
                && string.Equals(AuraSharedDictionary.Get(card?.data, "Id").TrimStart('*'), snapshot.Key.Id, StringComparison.Ordinal));
            if (added == null)
            {
                return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "locate", "Created card was not found.");
            }

            if ((command.Presentation?.Count ?? 0) > 0 || (command.Vars?.Count ?? 0) > 0)
            {
                var original = added;
                added = AuraGameDataHostApi.CloneWritable(original, command.Presentation, command.Vars);
                var index = IndexOfReference(cards, original);
                if (index < 0)
                {
                    throw new InvalidOperationException("Created card left the manager list before materialization.");
                }

                cards[index] = added;
                ReplaceCardTags(original, added);
            }

            executor.GetCardFromDeck(added);
            return new AuraGameInstanceCommandResult
            {
                Success = true,
                AggregateKind = AuraGameAggregateKinds.CardZone,
                Message = "Granted",
                Instance = AuraGameDataHostApi.Capture(added)
            };
        }
        catch (Exception ex)
        {
            var rolledBack = RemoveCreated(cards, added, before);
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "grant", ex.Message, rolledBack);
        }
    }

    public AuraGameInstanceCommandResult Remove(AuraCardRemoveCommand command)
    {
        if (string.Equals(command.Zone, "OwnedDeck", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.Zone, "ReserveDeck", StringComparison.OrdinalIgnoreCase))
        {
            return RemoveFromOwnedCollection(command);
        }

        if (!string.IsNullOrWhiteSpace(command.Zone)
            && !string.Equals(command.Zone, "ManagerDraw", StringComparison.OrdinalIgnoreCase))
        {
            return AuraGameInstanceCommandResult.Fail(
                AuraGameAggregateKinds.CardZone,
                "zone",
                "Hand, deck, discard, and burn removal require their owning gameplay use case.");
        }

        var cards = FightCardManager.Instance?.cardList;
        if (cards == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "manager", "FightCardManager card list is unavailable.");
        }

        var card = cards.FirstOrDefault(value => string.Equals(value?.InstanceID, command.InstanceId, StringComparison.Ordinal));
        if (card == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "locate", "Card instance was not found.");
        }

        var snapshot = AuraGameDataHostApi.Capture(card);
        cards.Remove(card);
        ReadCardTags()?.Remove(card);
        return new AuraGameInstanceCommandResult
        {
            Success = true,
            AggregateKind = AuraGameAggregateKinds.CardZone,
            Message = "Removed",
            Instance = snapshot
        };
    }

    private static AuraGameInstanceCommandResult GrantToOwnedCollection(AuraCardGrantCommand command)
    {
        if (!string.Equals(command.TargetZone, "OwnedDeck", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.TargetZone, "ReserveDeck", StringComparison.OrdinalIgnoreCase))
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "zone", "Unsupported card zone: " + command.TargetZone);
        }

        var materialized = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = command.Definition,
            Vars = command.Vars,
            DataOverrides = command.Presentation
        });
        if (!materialized.Success || materialized.Instance is not DataConfig card)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, materialized.FailureStep, materialized.Message);
        }

        var role = RoleTable.Instance;
        if (role == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "role", "RoleTable is unavailable.");
        }

        if (string.Equals(command.TargetZone, "ReserveDeck", StringComparison.OrdinalIgnoreCase))
        {
            if (role.UnCardList == null || role.UnCardList.Count >= role.MaxAlCardCount)
            {
                return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "capacity", "Reserve deck is full.");
            }
            role.UnCardList.Add(card);
        }
        else
        {
            role.cardList.Add(card);
        }

        return new AuraGameInstanceCommandResult
        {
            Success = true,
            AggregateKind = AuraGameAggregateKinds.CardZone,
            Message = "Granted",
            Instance = AuraGameDataHostApi.Capture(card)
        };
    }

    private static AuraGameInstanceCommandResult GrantToManagerDraw(AuraCardGrantCommand command)
    {
        var materialized = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = command.Definition,
            Vars = command.Vars,
            DataOverrides = command.Presentation
        });
        if (!materialized.Success || materialized.Instance is not DataConfig card)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, materialized.FailureStep, materialized.Message);
        }

        var cards = FightCardManager.Instance?.cardList;
        if (cards == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "manager", "FightCardManager card list is unavailable.");
        }

        cards.Add(card);
        var tags = ReadCardTags();
        if (tags != null && !tags.Contains(card))
        {
            tags[card] = new HashSet<string>(AuraSharedDictionary.SplitTokens(command.RuntimeTags));
        }

        return new AuraGameInstanceCommandResult
        {
            Success = true,
            AggregateKind = AuraGameAggregateKinds.CardZone,
            Message = "Granted",
            Instance = AuraGameDataHostApi.Capture(card)
        };
    }

    private static AuraGameInstanceCommandResult RemoveFromOwnedCollection(AuraCardRemoveCommand command)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "role", "RoleTable is unavailable.");
        }

        var cards = string.Equals(command.Zone, "ReserveDeck", StringComparison.OrdinalIgnoreCase)
            ? role.UnCardList
            : role.cardList;
        var card = cards.FirstOrDefault(value => string.Equals(value?.InstanceID, command.InstanceId, StringComparison.Ordinal));
        if (card == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "locate", "Card instance was not found.");
        }

        var snapshot = AuraGameDataHostApi.Capture(card);
        cards.Remove(card);
        return new AuraGameInstanceCommandResult
        {
            Success = true,
            AggregateKind = AuraGameAggregateKinds.CardZone,
            Message = "Removed",
            Instance = snapshot
        };
    }

    private static int IndexOfReference(IList<DataConfig> cards, DataConfig target)
    {
        for (var index = 0; index < cards.Count; index++)
        {
            if (ReferenceEquals(cards[index], target))
            {
                return index;
            }
        }

        return -1;
    }

    private static void ReplaceCardTags(DataConfig source, DataConfig replacement)
    {
        var tags = ReadCardTags();
        if (tags == null)
        {
            return;
        }

        if (tags.Contains(source))
        {
            var values = tags[source];
            tags.Remove(source);
            tags[replacement] = values;
        }
        else if (!tags.Contains(replacement))
        {
            tags[replacement] = new HashSet<string>();
        }
    }

    private static bool RemoveCreated(IList<DataConfig> cards, DataConfig? added, ISet<DataConfig> before)
    {
        var removed = false;
        for (var index = cards.Count - 1; index >= 0; index--)
        {
            if ((added != null && ReferenceEquals(cards[index], added)) || !before.Contains(cards[index]))
            {
                ReadCardTags()?.Remove(cards[index]);
                cards.RemoveAt(index);
                removed = true;
            }
        }

        return removed;
    }

    private static IDictionary? ReadCardTags()
    {
        var manager = FightCardManager.Instance;
        if (manager == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = manager.GetType();
        return type.GetProperty("CardTags", flags)?.GetValue(manager) as IDictionary
            ?? type.GetField("CardTags", flags)?.GetValue(manager) as IDictionary;
    }
}

public sealed class WitchRelicInstancePort : IAuraRelicInstancePort
{
    public AuraGameInstanceCommandResult Grant(AuraRelicGrantCommand command)
    {
        var materialized = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = command.Definition,
            Vars = command.Vars
        });
        if (!materialized.Success || materialized.Instance is not DataConfig relic)
        {
            return AuraGameInstanceCommandResult.Fail(
                AuraGameAggregateKinds.RelicInventory,
                materialized.FailureStep,
                materialized.Message);
        }

        var role = RoleTable.Instance;
        if (role == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.RelicInventory, "role", "RoleTable is unavailable.");
        }

        try
        {
            var canEquip = command.PreferEquippedSlot
                && role.relicList.Count < 6
                && GameObject.Find("Breaks") == null
                && GameObject.Find("Canvas/GameEntryUI") == null
                && !role.IsMoveOn;
            if (canEquip)
            {
                role.relicList.Add(relic);
            }
            else
            {
                role.WithoutArmedRelicList.Add(relic);
            }

            return new AuraGameInstanceCommandResult
            {
                Success = true,
                AggregateKind = AuraGameAggregateKinds.RelicInventory,
                Message = canEquip ? "Equipped" : "Stored",
                Instance = AuraGameDataHostApi.Capture(relic)
            };
        }
        catch (Exception ex)
        {
            role.relicList.Remove(relic);
            role.WithoutArmedRelicList.Remove(relic);
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.RelicInventory, "grant", ex.Message, true);
        }
    }

    public AuraGameInstanceCommandResult Remove(AuraRelicRemoveCommand command)
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.RelicInventory, "role", "RoleTable is unavailable.");
        }

        var relic = role.relicList.FirstOrDefault(value => string.Equals(value?.InstanceID, command.InstanceId, StringComparison.Ordinal))
            ?? role.WithoutArmedRelicList.FirstOrDefault(value => string.Equals(value?.InstanceID, command.InstanceId, StringComparison.Ordinal));
        if (relic == null)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.RelicInventory, "locate", "Relic instance was not found.");
        }

        var snapshot = AuraGameDataHostApi.Capture(relic);
        var removed = role.relicList.Remove(relic) || role.WithoutArmedRelicList.Remove(relic);
        return removed
            ? new AuraGameInstanceCommandResult
            {
                Success = true,
                AggregateKind = AuraGameAggregateKinds.RelicInventory,
                Message = "Removed",
                Instance = snapshot
            }
            : AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.RelicInventory, "remove", "Relic collection rejected removal.");
    }
}
