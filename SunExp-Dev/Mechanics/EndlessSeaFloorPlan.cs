using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class EndlessSeaFloorPlan
{
    public int Floor { get; set; }

    public int BuildingSlot { get; set; }

    public List<EndlessSeaSlotPlan> Slots { get; set; } = new();

    public bool IsValid =>
        Floor > 0
        && Slots.Count == 2
        && TryGetSlot(SunExpIds.EndlessSeaStartSlotIndex, out var start)
        && start.Kind == EndlessSeaNodeKind.Monster
        && TryGetSlot(SunExpIds.EndlessSeaBossSlotIndex, out var boss)
        && IsBossKind(boss.Kind)
        && Slots.All(slot => slot.VisualSlot == SunExpIds.EndlessSeaStartSlotIndex
            || slot.VisualSlot == SunExpIds.EndlessSeaBossSlotIndex);

    private static bool IsBossKind(EndlessSeaNodeKind kind)
    {
        return kind == EndlessSeaNodeKind.Boss || kind == EndlessSeaNodeKind.EndlessBoss;
    }

    public bool TryGetSlot(int visualSlot, out EndlessSeaSlotPlan slot)
    {
        slot = Slots.FirstOrDefault(current => current.VisualSlot == visualSlot)!;
        return slot != null;
    }

    public IEnumerable<int> FixedSlots()
    {
        yield return SunExpIds.EndlessSeaStartSlotIndex;
        yield return SunExpIds.EndlessSeaBossSlotIndex;
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
        Slots ??= new List<EndlessSeaSlotPlan>();
        foreach (var slot in Slots)
        {
            slot.Normalize();
        }
    }
}

public sealed class EndlessSeaSlotPlan
{
    public int VisualSlot { get; set; }

    public EndlessSeaNodeKind Kind { get; set; }

    public bool Locked { get; set; }

    public string Source { get; set; } = "";

    public Dictionary<string, string> Data { get; set; } = new(StringComparer.Ordinal);

    public string MapId => Field("Id");

    public string NodeId => Field("NodeId", MapId);

    public string Type => Field("Type", IsSafeNodeKind(Kind) ? "Build" : "Fight");

    public static EndlessSeaSlotPlan FromNode(int visualSlot, EndlessSeaNodeKind kind, MapTree.Node node)
    {
        var data = node.data == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(node.data, StringComparer.Ordinal);
        data[SunExpIds.EndlessSeaNodeSlotKey] = Math.Max(0, visualSlot).ToString();
        data[SunExpIds.EndlessSeaNodeKindKey] = kind.ToString();
        data[SunExpIds.EndlessSeaNodeLockedKey] = IsBossKind(kind) ? "1" : "0";

        return new EndlessSeaSlotPlan
        {
            VisualSlot = visualSlot,
            Kind = kind,
            Locked = IsBossKind(kind),
            Source = DictionaryUtil.Get(data, SunExpIds.EndlessSeaNodePoolSourceKey),
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
        MapNodeSafetyService.EnsureNodeDice(tree, node, "EndlessSeaSlotPlan.ToNode");
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
        Data[SunExpIds.EndlessSeaNodeSlotKey] = Math.Max(0, VisualSlot).ToString();
        Data[SunExpIds.EndlessSeaNodeKindKey] = Kind.ToString();
        Data[SunExpIds.EndlessSeaNodeLockedKey] = Locked ? "1" : "0";
    }

    private static bool IsSafeNodeKind(EndlessSeaNodeKind kind)
    {
        return kind == EndlessSeaNodeKind.Rest || kind == EndlessSeaNodeKind.Building;
    }

    private static bool IsBossKind(EndlessSeaNodeKind kind)
    {
        return kind == EndlessSeaNodeKind.Boss || kind == EndlessSeaNodeKind.EndlessBoss;
    }

    private string Field(string key, string fallback = "")
    {
        return Data != null && Data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
