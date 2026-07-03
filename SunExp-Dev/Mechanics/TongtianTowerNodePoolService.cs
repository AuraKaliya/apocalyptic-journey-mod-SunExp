using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerNodePoolService
{
    private const string MonsterNote = "普通";
    private const string EliteNote = "精英";
    private const string BossNote = "首领";
    private const string BuildingNote = "建筑";
    private const string GameMapSource = "game-map";
    private const string FallbackSource = "fallback";

    private static readonly MethodInfo? DiceWithCursorMethod = typeof(Dice).GetMethod(
        "WithCursor",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static MapTree.Node CreateNode(MapTree tree, int floor, int slot, TongtianTowerNodeKind kind)
    {
        var candidates = Candidates(kind, floor).ToList();
        var source = GameMapSource;
        if (candidates.Count == 0)
        {
            candidates = FallbackCandidates(kind);
            source = FallbackSource;
        }

        var row = kind == TongtianTowerNodeKind.Building
            ? PickCycledBuildingRow(candidates, floor)
            : DrawRow(tree, candidates);
        var data = row == null
            ? FallbackData(kind)
            : new Dictionary<string, string>(row);
        NormalizeNodeData(data, floor, slot, kind, source);

        var node = new MapTree.Node(DictionaryUtil.Get(data, "Note", KindNote(kind)))
        {
            type = DictionaryUtil.Get(data, "Note", KindNote(kind)),
            data = data,
            NodeDice = CreateNodeDice(tree)
        };
        MapNodeSafetyService.EnsureNodeDice(tree, node, "TongtianTowerNodePoolService.CreateNode");
        return node;
    }

    public static IReadOnlyList<Dictionary<string, string>> Candidates(TongtianTowerNodeKind kind, int floor)
    {
        var key = "TongtianTower." + kind + ".floor." + Math.Max(1, floor);
        return SunExpConfigIndex.FilteredRows(DataType.Map, key, row => IsCandidate(row, kind, floor));
    }

    private static Dictionary<string, string>? DrawRow(MapTree tree, List<Dictionary<string, string>> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        try
        {
            return new RandomPool(candidates, tree.treedice ?? Dice.Default).DrawByCount(1)[0];
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerNodePool] candidate draw failed: " + ex.Message);
            return candidates
                .OrderBy(row => DictionaryUtil.Get(row, "Id"), StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }

    private static Dictionary<string, string>? PickCycledBuildingRow(List<Dictionary<string, string>> candidates, int floor)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var ordered = candidates
            .OrderBy(row => DictionaryUtil.Get(row, "Id"), StringComparer.Ordinal)
            .ThenBy(row => DictionaryUtil.Get(row, "NodeId"), StringComparer.Ordinal)
            .ToList();
        var index = (Math.Max(1, floor) - 1) % ordered.Count;
        return ordered[index];
    }

    private static bool IsCandidate(Dictionary<string, string> row, TongtianTowerNodeKind kind, int floor)
    {
        if (!IsUsableMapRow(row) || !FitsFloor(row, floor))
        {
            return false;
        }

        return kind switch
        {
            TongtianTowerNodeKind.Boss => IsBoss(row),
            TongtianTowerNodeKind.Building => IsBuilding(row),
            _ => IsMonster(row)
        };
    }

    private static List<Dictionary<string, string>> FallbackCandidates(TongtianTowerNodeKind kind)
    {
        return SunExpConfigIndex.FilteredRows(
            DataType.Map,
            "TongtianTower.Fallback." + kind,
            row =>
            {
                if (!IsUsableMapRow(row))
                {
                    return false;
                }

                return kind switch
                {
                    TongtianTowerNodeKind.Boss => IsFight(row),
                    TongtianTowerNodeKind.Building => IsBuilding(row),
                    _ => IsFight(row) && !IsBoss(row)
                };
            });
    }

    private static bool IsUsableMapRow(Dictionary<string, string>? row)
    {
        if (row == null)
        {
            return false;
        }

        var id = DictionaryUtil.Get(row, "Id");
        var nodeId = DictionaryUtil.Get(row, "NodeId");
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(nodeId)
            || id.StartsWith("*", StringComparison.Ordinal)
            || nodeId.StartsWith("*", StringComparison.Ordinal)
            || id.IndexOf("Breaks", StringComparison.OrdinalIgnoreCase) >= 0
            || nodeId.IndexOf("Breaks", StringComparison.OrdinalIgnoreCase) >= 0
            || string.Equals(DictionaryUtil.Get(row, "Rarity"), "7", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(DictionaryUtil.Get(row, "Type"), "Event", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DictionaryUtil.Get(row, "Note"), "普通事件", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return Singleton<GameRuntimeData>.Instance == null
                || !Singleton<GameRuntimeData>.Instance.IsLocked(id);
        }
        catch
        {
            return true;
        }
    }

    private static bool FitsFloor(Dictionary<string, string> row, int floor)
    {
        var level = DictionaryUtil.ParseInt(DictionaryUtil.Get(row, "Level", "-1"), -1);
        if (level < 0)
        {
            return true;
        }

        var unlockedTier = Math.Min(4, Math.Max(0, (Math.Max(1, floor) - 1) / 3));
        return level <= unlockedTier;
    }

    private static bool IsFight(Dictionary<string, string> row)
    {
        return string.Equals(DictionaryUtil.Get(row, "Type"), "Fight", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMonster(Dictionary<string, string> row)
    {
        if (!IsFight(row) || IsBoss(row))
        {
            return false;
        }

        var note = DictionaryUtil.Get(row, "Note");
        return string.IsNullOrWhiteSpace(note)
            || string.Equals(note, MonsterNote, StringComparison.Ordinal)
            || string.Equals(note, EliteNote, StringComparison.Ordinal);
    }

    private static bool IsBuilding(Dictionary<string, string> row)
    {
        return string.Equals(DictionaryUtil.Get(row, "Type"), "Build", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DictionaryUtil.Get(row, "Note"), BuildingNote, StringComparison.Ordinal);
    }

    private static bool IsBoss(Dictionary<string, string> row)
    {
        if (!IsFight(row))
        {
            return false;
        }

        if (string.Equals(DictionaryUtil.Get(row, "Note"), BossNote, StringComparison.Ordinal))
        {
            return true;
        }

        var nodeId = DictionaryUtil.Get(row, "NodeId");
        try
        {
            var level = SunExpConfigIndex.Row(DataType.Level, nodeId);
            var note = DictionaryUtil.Get(level, "Note");
            return note.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0
                || note.Contains(BossNote);
        }
        catch
        {
            return nodeId.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private static void NormalizeNodeData(
        IDictionary<string, string> data,
        int floor,
        int slot,
        TongtianTowerNodeKind kind,
        string source)
    {
        var id = DictionaryUtil.Get(data, "Id");
        var nodeId = DictionaryUtil.Get(data, "NodeId", id);
        data["Id"] = string.IsNullOrWhiteSpace(id) ? "map_0" : id;
        data["NodeId"] = string.IsNullOrWhiteSpace(nodeId) ? data["Id"] : nodeId;
        data["Type"] = kind == TongtianTowerNodeKind.Building ? "Build" : "Fight";
        data["Note"] = KindNote(kind);
        data["Level"] = DictionaryUtil.Get(data, "Level", "-1");
        data[SunExpIds.TongtianTowerNodeFloorKey] = Math.Max(1, floor).ToString();
        data[SunExpIds.TongtianTowerNodeSlotKey] = Math.Max(0, slot).ToString();
        data[SunExpIds.TongtianTowerNodeKindKey] = kind.ToString();
        data[SunExpIds.TongtianTowerNodePoolSourceKey] = source;
        data[SunExpIds.TongtianTowerNodeLockedKey] = kind == TongtianTowerNodeKind.Boss ? "1" : "0";
    }

    private static string KindNote(TongtianTowerNodeKind kind)
    {
        return kind switch
        {
            TongtianTowerNodeKind.Boss => BossNote,
            TongtianTowerNodeKind.Building => BuildingNote,
            _ => MonsterNote
        };
    }

    private static Dictionary<string, string> FallbackData(TongtianTowerNodeKind kind)
    {
        var note = KindNote(kind);
        return new Dictionary<string, string>
        {
            ["Id"] = "map_0",
            ["Type"] = kind == TongtianTowerNodeKind.Building ? "Build" : "Fight",
            ["Note"] = note,
            ["NodeId"] = "map_0",
            ["Level"] = "-1"
        };
    }

    private static Dice CreateNodeDice(MapTree tree)
    {
        var dice = tree?.treedice;
        if (dice == null)
        {
            return Dice.Default;
        }

        try
        {
            var cursor = dice.Roll().Value;
            return DiceWithCursorMethod?.Invoke(dice, new object[] { cursor }) as Dice ?? dice;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerNodePool] failed to fork NodeDice: " + ex.Message);
            return dice;
        }
    }
}
