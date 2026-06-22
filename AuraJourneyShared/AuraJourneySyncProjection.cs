using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraJourney.Shared;

public static class AuraJourneySyncProjection
{
    public static AuraJourneySyncProjectionResult Repair(
        string[]? maps,
        string[]? mapData,
        IEnumerable<AuraJourneySlotRule>? slotRules,
        Func<string, IDictionary<string, string>?>? resolveMapRow = null)
    {
        var result = new AuraJourneySyncProjectionResult();
        if (maps == null || mapData == null)
        {
            result.Message = "Map sync arrays are missing.";
            return result;
        }

        var count = Math.Min(maps.Length, mapData.Length);
        if (count <= 0)
        {
            result.Message = "Map sync arrays are empty.";
            return result;
        }

        foreach (var rule in slotRules?.Where(rule => rule != null) ?? Enumerable.Empty<AuraJourneySlotRule>())
        {
            if (rule.SlotIndex < 0 || rule.SlotIndex >= count)
            {
                result.Skipped++;
                continue;
            }

            if (ShouldPreserve(rule, maps[rule.SlotIndex], mapData[rule.SlotIndex]))
            {
                result.Preserved++;
                continue;
            }

            var projection = AuraJourneyMapNodeDataBuilder.Build(rule.MapNode, resolveMapRow);
            if (!projection.Valid)
            {
                result.Skipped++;
                continue;
            }

            var changed = false;
            if (!string.Equals(maps[rule.SlotIndex], projection.MapId, StringComparison.Ordinal))
            {
                maps[rule.SlotIndex] = projection.MapId;
                changed = true;
            }

            if (!string.Equals(mapData[rule.SlotIndex], projection.NodeId, StringComparison.Ordinal))
            {
                mapData[rule.SlotIndex] = projection.NodeId;
                changed = true;
            }

            if (changed)
            {
                result.Changed = true;
                result.Repaired++;
                result.RepairedSlots.Add(rule.SlotIndex);
            }
        }

        result.Message = result.Changed ? "Map sync arrays repaired." : "Map sync arrays already matched.";
        return result;
    }

    private static bool ShouldPreserve(AuraJourneySlotRule rule, string mapId, string nodeId)
    {
        var policy = rule.ReplacementPolicy ?? AuraJourneyReplacementPolicies.Replace;
        if (string.Equals(policy, AuraJourneyReplacementPolicies.KeepNative, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(policy, AuraJourneyReplacementPolicies.FillEmpty, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(mapId)
            && !string.IsNullOrWhiteSpace(nodeId))
        {
            return true;
        }

        return string.Equals(policy, AuraJourneyReplacementPolicies.PreserveBreak, StringComparison.OrdinalIgnoreCase)
               && (AuraJourneyMapNodeDataBuilder.IsBreakNodeId(mapId) || AuraJourneyMapNodeDataBuilder.IsBreakNodeId(nodeId));
    }
}

public sealed class AuraJourneySyncProjectionResult
{
    public bool Changed { get; set; }

    public int Repaired { get; set; }

    public int Preserved { get; set; }

    public int Skipped { get; set; }

    public List<int> RepairedSlots { get; set; } = new();

    public string Message { get; set; } = "";
}
