using System;
using System.Collections.Generic;

namespace AuraJourney.Shared;

public static class AuraJourneyMapNodeDataBuilder
{
    public static AuraJourneyMapNodeProjection Build(
        AuraJourneyMapNodeSpec spec,
        Func<string, IDictionary<string, string>?>? resolveMapRow)
    {
        spec ??= new AuraJourneyMapNodeSpec();
        var mapId = FirstNonEmpty(spec.MapId, spec.FallbackMapId);
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return new AuraJourneyMapNodeProjection { Valid = false };
        }

        var row = TryResolve(resolveMapRow, spec.MapId) ?? TryResolve(resolveMapRow, spec.FallbackMapId);
        var data = row == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(row, StringComparer.Ordinal);

        var nodeId = FirstNonEmpty(spec.NodeId, Field(data, "NodeId"), mapId);
        var type = FirstNonEmpty(spec.Type, Field(data, "Type"), LooksLikeEventNode(spec, nodeId) ? AuraJourneyNodeKinds.Event : AuraJourneyNodeKinds.Fight);
        var note = FirstNonEmpty(spec.Note, Field(data, "Note"), type);
        var level = FirstNonEmpty(spec.Level, Field(data, "Level"), "-1");

        data["Id"] = mapId;
        data["Type"] = type;
        data["Note"] = note;
        data["NodeId"] = nodeId;
        data["Level"] = level;

        return new AuraJourneyMapNodeProjection
        {
            Valid = true,
            MapId = mapId,
            NodeId = nodeId,
            Type = type,
            Note = note,
            Level = level,
            DicePolicy = FirstNonEmpty(spec.DicePolicy, AuraJourneyDicePolicies.TreeDice),
            Data = data
        };
    }

    public static bool IsBreakNodeId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value!.IndexOf("Breaks", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeEventNode(AuraJourneyMapNodeSpec spec, string nodeId)
    {
        return !string.IsNullOrWhiteSpace(spec.NodeId)
               || string.Equals(spec.Type, AuraJourneyNodeKinds.Event, StringComparison.OrdinalIgnoreCase)
               || string.Equals(spec.Note, AuraJourneyNodeKinds.Event, StringComparison.OrdinalIgnoreCase)
               || nodeId.IndexOf("Sub_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IDictionary<string, string>? TryResolve(Func<string, IDictionary<string, string>?>? resolveMapRow, string id)
    {
        if (resolveMapRow == null || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return resolveMapRow(id.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static string FirstNonEmpty(params string[] values)
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
