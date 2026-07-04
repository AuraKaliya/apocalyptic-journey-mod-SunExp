using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class TongtianTowerFloorPlan
{
    public int Floor { get; set; }

    public int BuildingSlot { get; set; }

    public List<TongtianTowerSlotPlan> Slots { get; set; } = new();

    public bool IsValid =>
        Floor > 0
        && Slots.Count == 2
        && TryGetSlot(SunExpIds.TongtianTowerStartSlotIndex, out var start)
        && start.Kind == TongtianTowerNodeKind.Monster
        && TryGetSlot(SunExpIds.TongtianTowerBossSlotIndex, out var boss)
        && IsBossKind(boss.Kind)
        && Slots.All(slot => slot.VisualSlot == SunExpIds.TongtianTowerStartSlotIndex
            || slot.VisualSlot == SunExpIds.TongtianTowerBossSlotIndex);

    private static bool IsBossKind(TongtianTowerNodeKind kind)
    {
        return kind == TongtianTowerNodeKind.Boss || kind == TongtianTowerNodeKind.EndlessBoss;
    }

    public bool TryGetSlot(int visualSlot, out TongtianTowerSlotPlan slot)
    {
        slot = Slots.FirstOrDefault(current => current.VisualSlot == visualSlot)!;
        return slot != null;
    }

    public IEnumerable<int> FixedSlots()
    {
        yield return SunExpIds.TongtianTowerStartSlotIndex;
        yield return SunExpIds.TongtianTowerBossSlotIndex;
    }

    public IEnumerable<string> Summaries()
    {
        foreach (var slot in Slots.OrderBy(current => current.VisualSlot))
        {
            yield return slot.MapId
                + "/"
                + slot.NodeId
                + ":"
                + slot.Kind;
        }
    }

    public void Normalize()
    {
        Floor = Math.Max(1, Floor);
        BuildingSlot = -1;
        Slots ??= new List<TongtianTowerSlotPlan>();
        foreach (var slot in Slots)
        {
            slot.Normalize();
        }
    }
}

public sealed class TongtianTowerSlotPlan
{
    public int VisualSlot { get; set; }

    public TongtianTowerNodeKind Kind { get; set; }

    public bool Locked { get; set; }

    public string Source { get; set; } = "";

    public Dictionary<string, string> Data { get; set; } = new(StringComparer.Ordinal);

    public string MapId => Field("Id");

    public string NodeId => Field("NodeId", MapId);

    public string Type => Field("Type", IsSafeNodeKind(Kind) ? "Build" : "Fight");

    public static TongtianTowerSlotPlan FromNode(int visualSlot, TongtianTowerNodeKind kind, MapTree.Node node)
    {
        var data = node.data == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(node.data, StringComparer.Ordinal);
        data[SunExpIds.TongtianTowerNodeSlotKey] = Math.Max(0, visualSlot).ToString();
        data[SunExpIds.TongtianTowerNodeKindKey] = kind.ToString();
        data[SunExpIds.TongtianTowerNodeLockedKey] = IsBossKind(kind) ? "1" : "0";

        return new TongtianTowerSlotPlan
        {
            VisualSlot = visualSlot,
            Kind = kind,
            Locked = IsBossKind(kind),
            Source = DictionaryUtil.Get(data, SunExpIds.TongtianTowerNodePoolSourceKey),
            Data = data
        };
    }

    public MapTree.Node ToNode(MapTree tree)
    {
        Normalize();
        var note = Field("Note", Kind.ToString());
        var node = new MapTree.Node(note)
        {
            type = note,
            data = new Dictionary<string, string>(Data, StringComparer.Ordinal),
            NodeDice = tree?.treedice ?? Dice.Default
        };
        MapNodeSafetyService.EnsureNodeDice(tree, node, "TongtianTowerSlotPlan.ToNode");
        return node;
    }

    public void Normalize()
    {
        Data ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (Data.Comparer != StringComparer.Ordinal)
        {
            Data = new Dictionary<string, string>(Data, StringComparer.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(Field("Id")))
        {
            Data["Id"] = "map_0";
        }

        if (string.IsNullOrWhiteSpace(Field("NodeId")))
        {
            Data["NodeId"] = Data["Id"];
        }

        Data["Type"] = IsSafeNodeKind(Kind) ? "Build" : "Fight";
        Data[SunExpIds.TongtianTowerNodeSlotKey] = Math.Max(0, VisualSlot).ToString();
        Data[SunExpIds.TongtianTowerNodeKindKey] = Kind.ToString();
        Data[SunExpIds.TongtianTowerNodeLockedKey] = Locked ? "1" : "0";
    }

    private static bool IsSafeNodeKind(TongtianTowerNodeKind kind)
    {
        return kind == TongtianTowerNodeKind.Rest || kind == TongtianTowerNodeKind.Building;
    }

    private static bool IsBossKind(TongtianTowerNodeKind kind)
    {
        return kind == TongtianTowerNodeKind.Boss || kind == TongtianTowerNodeKind.EndlessBoss;
    }

    private string Field(string key, string fallback = "")
    {
        return Data != null && Data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
