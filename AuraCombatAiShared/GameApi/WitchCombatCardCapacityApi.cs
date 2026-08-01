using System;
using System.Collections;
using System.Reflection;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraCombatAi.Shared.GameApi;

/// <summary>
/// Keeps the game's hand-capacity and draw-pile checks behind one compatibility
/// boundary. The native CardTopCheck contract includes queued card views, which
/// RoleTable.CardTopCount and the visible hand count alone do not capture.
/// </summary>
internal static class WitchCombatCardCapacityApi
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo? CardTopCheckMethod =
        typeof(ScriptExecutor).GetMethod(
            "CardTopCheck",
            InstanceFlags,
            null,
            Type.EmptyTypes,
            null);
    private static readonly MemberInfo? CreateCardQueueMember =
        (MemberInfo?)typeof(FightUI).GetField("createCardQueue", InstanceFlags)
        ?? typeof(FightUI).GetProperty("createCardQueue", InstanceFlags);
    private static readonly MemberInfo? CardTopCountMember =
        (MemberInfo?)typeof(FightUI).GetProperty("CardTopCount", InstanceFlags)
        ?? typeof(FightUI).GetField("CardTopCount", InstanceFlags);

    public static bool TryObserve(
        FightUI? fightUi,
        out int visibleHandCount,
        out int pendingCardCount,
        out int handLimit)
    {
        fightUi ??= WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        visibleHandCount = Math.Max(0, FightUI.cardItemList?.Count ?? 0);
        pendingCardCount = CountOf(ReadMember(CreateCardQueueMember, fightUi));
        handLimit = IntegerOf(ReadMember(CardTopCountMember, fightUi));
        if (handLimit <= 0)
        {
            handLimit = RoleTable.Instance?.CardTopCount ?? 0;
        }
        if (handLimit <= 0)
        {
            handLimit = CombatActionExecutionPolicy.DefaultHandLimit;
        }
        return fightUi != null;
    }

    public static int AvailableHandSlots(FightUI? fightUi)
    {
        TryObserve(
            fightUi,
            out var visibleHandCount,
            out var pendingCardCount,
            out var handLimit);
        return Math.Max(0, handLimit - visibleHandCount - pendingCardCount);
    }

    public static bool IsAtNativeHandLimit(
        IDataConfig? source,
        FightUI? fightUi,
        out string diagnostic)
    {
        try
        {
            var executor = source?.scriptExecutor;
            if (executor != null && CardTopCheckMethod != null)
            {
                var result = CardTopCheckMethod.Invoke(executor, null);
                if (result is bool atLimit)
                {
                    diagnostic = "native ScriptExecutor.CardTopCheck";
                    return atLimit;
                }
            }
        }
        catch
        {
            // Fall through to the deterministic mirror of CardTopCheck.
        }

        var available = AvailableHandSlots(fightUi);
        diagnostic = "mirrored CardTopCheck capacity";
        return available <= 0;
    }

    public static bool HasDrawPileCard()
    {
        return (FightCardManager.Instance?.cardList?.Count ?? 0) > 0;
    }

    private static object? ReadMember(MemberInfo? member, object? target)
    {
        if (member == null || target == null)
        {
            return null;
        }
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(target),
                PropertyInfo property => property.GetValue(target, null),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static int CountOf(object? value)
    {
        if (value is ICollection collection)
        {
            return Math.Max(0, collection.Count);
        }
        if (value is IEnumerable enumerable)
        {
            var count = 0;
            foreach (var _ in enumerable)
            {
                count++;
            }
            return count;
        }
        return 0;
    }

    private static int IntegerOf(object? value)
    {
        try
        {
            return value == null ? 0 : Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }
}
