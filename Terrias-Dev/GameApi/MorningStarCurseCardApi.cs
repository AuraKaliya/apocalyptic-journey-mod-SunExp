using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public enum MorningStarCombatCardZone
{
    Unknown,
    Hand,
    Draw,
    Discard
}

public static class MorningStarCurseCardApi
{
    public static IReadOnlyList<IDataConfig> HandCards(ScriptExecutor? self)
    {
        return Capture(self, includeHand: true, includeDraw: false, includeDiscard: false);
    }

    public static IReadOnlyList<IDataConfig> DrawCards(ScriptExecutor? self)
    {
        return Capture(self, includeHand: false, includeDraw: true, includeDiscard: false);
    }

    public static IReadOnlyList<IDataConfig> DiscardCards(ScriptExecutor? self)
    {
        return Capture(self, includeHand: false, includeDraw: false, includeDiscard: true);
    }

    public static IReadOnlyList<IDataConfig> AllOwnedCards(ScriptExecutor? self)
    {
        var result = new List<IDataConfig>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddDistinct(result, seen, DrawCards(self));
        AddDistinct(result, seen, HandCards(self));
        AddDistinct(result, seen, DiscardCards(self));
        return result;
    }

    public static bool TryBurnCard(ScriptExecutor? self, IDataConfig? card)
    {
        if (self?.Self == null || card == null)
        {
            return false;
        }

        var zone = Locate(self, card);
        if (zone == MorningStarCombatCardZone.Unknown)
        {
            return false;
        }

        try
        {
            var method = self.GetType().GetMethod(
                "BurnCardByData",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(IDataConfig) },
                null);
            if (method == null)
            {
                TerriasLog.Warn("[MorningStarCurse] ScriptExecutor.BurnCardByData unavailable.");
                return false;
            }

            method.Invoke(self, new object[] { card });
            if (Contains(AllOwnedCards(self), card))
            {
                return false;
            }

            if (zone != MorningStarCombatCardZone.Hand)
            {
                PublishStandardBurnEvent(self, card);
            }

            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[MorningStarCurse] burn failed: " + ex.Message);
            return false;
        }
    }

    public static IDataConfig? CreateFinishSelectionCard(ScriptExecutor? self, int selectedCount)
    {
        var source = self?.dataConfig;
        if (source?.data == null)
        {
            return null;
        }

        var data = new Dictionary<string, string>(source.data, StringComparer.Ordinal)
        {
            ["Id"] = "*terrias_reverse_formula_finish_selection",
            ["Rarity"] = "4",
            ["Expend"] = "99",
            ["Tag"] = "Nihility",
            ["InitScript"] = "",
            ["DrawScript"] = "",
            ["UseScript"] = "",
            ["DropScript"] = "",
            ["Action"] = "",
            ["PackBelong"] = "",
            ["Name"] = "完成选择",
            ["Name_zh-Hant"] = "完成選擇",
            ["Name_en"] = "Finish Selection",
            ["Name_ja"] = "選択完了",
            ["Description"] = "已选择" + Math.Max(0, selectedCount) + "张诅咒。选择此项完成反转。",
            ["Description_zh-Hant"] = "已選擇" + Math.Max(0, selectedCount) + "張詛咒。選擇此項完成反轉。",
            ["Description_en"] = "Selected " + Math.Max(0, selectedCount) + " Curses. Choose this to resolve the reversal.",
            ["Description_ja"] = "呪いを" + Math.Max(0, selectedCount) + "枚選択済み。これを選ぶと反転を実行する。"
        };
        var vars = new Dictionary<string, string>(source.Vars ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var field in data)
        {
            if (field.Key == "Name"
                || field.Key.StartsWith("Name_", StringComparison.Ordinal)
                || field.Key == "Description"
                || field.Key.StartsWith("Description_", StringComparison.Ordinal))
            {
                vars[field.Key] = field.Value;
            }
        }

        vars["Id"] = data["Id"];
        vars["Tag"] = data["Tag"];
        return new DataConfig(data, vars, false, DataType.Card);
    }

    private static IReadOnlyList<IDataConfig> Capture(
        ScriptExecutor? self,
        bool includeHand,
        bool includeDraw,
        bool includeDiscard)
    {
        var snapshot = AuraCombatCardZoneSnapshot.Capture(self, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = includeHand,
            IncludeFightUiWait = includeHand,
            IncludeExecutorHand = includeHand,
            IncludeExecutorWait = includeHand,
            IncludeExecutorDeck = includeDraw,
            IncludeExecutorUsed = includeDiscard,
            IncludeManagerDraw = includeDraw,
            IncludeManagerUsed = includeDiscard
        });
        return snapshot.Cards
            .Select(reference => reference.Config)
            .Where(config => config != null)
            .Cast<IDataConfig>()
            .ToList();
    }

    private static MorningStarCombatCardZone Locate(ScriptExecutor self, IDataConfig card)
    {
        if (Contains(HandCards(self), card))
        {
            return MorningStarCombatCardZone.Hand;
        }

        if (Contains(DrawCards(self), card))
        {
            return MorningStarCombatCardZone.Draw;
        }

        return Contains(DiscardCards(self), card)
            ? MorningStarCombatCardZone.Discard
            : MorningStarCombatCardZone.Unknown;
    }

    private static void PublishStandardBurnEvent(ScriptExecutor self, IDataConfig card)
    {
        var ownerId = self.Self?.InstanceId ?? "";
        if (ownerId.Length == 0)
        {
            return;
        }

        EventCenter.Instance.EventTrigger("BurnCard" + ownerId, new BurnData(card, ownerId));
    }

    private static bool Contains(IEnumerable<IDataConfig> cards, IDataConfig target)
    {
        var targetId = InstanceId(target);
        return cards.Any(card => ReferenceEquals(card, target)
                                 || targetId.Length > 0
                                 && string.Equals(InstanceId(card), targetId, StringComparison.Ordinal));
    }

    private static void AddDistinct(
        ICollection<IDataConfig> target,
        ISet<string> seen,
        IEnumerable<IDataConfig> source)
    {
        foreach (var card in source)
        {
            var key = InstanceId(card);
            if ((key.Length > 0 && seen.Add(key)) || key.Length == 0 && !target.Contains(card))
            {
                target.Add(card);
            }
        }
    }

    private static string InstanceId(IDataConfig? config)
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
}
