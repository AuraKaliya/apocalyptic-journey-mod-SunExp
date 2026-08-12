using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using Fight.ActionCommand;
using Fight.ObjTarget;
using Fight.StatusCommand;
using MemoryPack;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchSemanticEventFactory
{
    private static readonly MethodInfo DeserializeAsyncMethod = typeof(MemoryPackSerializer)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(method => method.Name == "DeserializeAsync"
                         && method.IsGenericMethodDefinition
                         && method.GetParameters().Length == 3);

    internal static MatchSemanticEvent From(object command)
    {
        switch (command)
        {
            case UseCard useCard:
                return FromUseCard(useCard);
            case DamageText damageText:
                return FromDamageText(damageText);
            case ActionCommandBase action:
                return new MatchSemanticEvent
                {
                    Category = MatchSemanticCategories.Command,
                    Action = action.Type ?? action.GetType().Name,
                    ActorId = action.From ?? "",
                    Label = action.GetType().Name
                };
            case ObjTargetBase target:
                return new MatchSemanticEvent
                {
                    Category = MatchSemanticCategories.Target,
                    Action = target.Type ?? target.GetType().Name,
                    ActorId = target.SourceInstanceId ?? target.From ?? "",
                    TargetId = target.InstanceId ?? "",
                    SourceId = target.FromDataConfigId ?? "",
                    Label = target.ToAction ?? target.GetType().Name
                };
            case StatusDataTransfer status:
                return new MatchSemanticEvent
                {
                    Category = MatchSemanticCategories.Status,
                    Action = "StatusSnapshot",
                    TargetId = status.InstanceId ?? "",
                    Label = status.state.ToString(),
                    Value = status.curHp,
                    SecondaryValue = status.defend,
                    IsKeyEvent = status.curHp <= 0
                };
            default:
                return new MatchSemanticEvent
                {
                    Category = MatchSemanticCategories.Command,
                    Action = command.GetType().Name,
                    Label = command.GetType().Name
                };
        }
    }

    private static MatchSemanticEvent FromUseCard(UseCard command)
    {
        var cardId = "";
        var label = "使用卡牌";
        try
        {
            var data = Deserialize<UseCard.CardUseData>(command.Value);
            var card = data.cardData;
            cardId = Read(card?.data, "Id");
            label = First(Read(card?.data, "Name"), Read(card?.data, "DisplayName"), cardId, label);
        }
        catch
        {
        }

        return new MatchSemanticEvent
        {
            Category = MatchSemanticCategories.Card,
            Action = "UseCard",
            ActorId = command.From ?? "",
            SourceId = cardId,
            Label = label
        };
    }

    private static MatchSemanticEvent FromDamageText(DamageText command)
    {
        try
        {
            var data = Deserialize<DamageText.DamageTextData>(command.Value);
            return new MatchSemanticEvent
            {
                Category = MatchSemanticCategories.Damage,
                Action = data.damageType ?? "Damage",
                ActorId = data.from ?? command.From ?? "",
                TargetId = data.to ?? "",
                Label = data.damageType ?? "伤害",
                Value = Math.Max(0, data.hit),
                SecondaryValue = Math.Max(0, data.originalVal),
                IsKeyEvent = data.hit >= 100
            };
        }
        catch
        {
        }

        return new MatchSemanticEvent
        {
            Category = MatchSemanticCategories.Damage,
            Action = "Damage",
            ActorId = command.From ?? "",
            Label = "伤害"
        };
    }

    private static string Read(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static T? Deserialize<T>(byte[] payload)
    {
        using var stream = new MemoryStream(payload ?? Array.Empty<byte>(), writable: false);
        var operation = DeserializeAsyncMethod.MakeGenericMethod(typeof(T)).Invoke(
            null,
            new object?[] { stream, null, CancellationToken.None });
        if (operation == null)
        {
            return default;
        }

        var task = operation.GetType().GetMethod("AsTask", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(operation, Array.Empty<object>()) as Task;
        if (task == null)
        {
            return default;
        }

        task.GetAwaiter().GetResult();
        return (T?)task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static string First(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
