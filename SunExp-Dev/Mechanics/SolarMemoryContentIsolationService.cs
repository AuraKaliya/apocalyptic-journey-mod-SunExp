using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

internal sealed class SolarMemoryMapSelectionReplacement
{
    public SolarMemoryMapSelectionReplacement(string mapId, string nodeId)
    {
        MapId = mapId?.Trim() ?? "";
        NodeId = nodeId?.Trim() ?? "";
    }

    public string MapId { get; }

    public string NodeId { get; }
}

internal static class SolarMemoryContentIsolationService
{
    public static bool RequiresReplacement(string? mapId, string? nodeId)
    {
        return SunExpIds.IsSolarMemoryExclusiveMapId(mapId)
               || SunExpIds.IsSolarMemoryExclusiveEventId(nodeId);
    }

    public static int SanitizeSelectionArrays(
        string[]? maps,
        string[]? mapData,
        Func<string, string, int, SolarMemoryMapSelectionReplacement?> replacementResolver)
    {
        if (maps == null
            || mapData == null
            || replacementResolver == null
            || maps.Length == 0
            || mapData.Length == 0)
        {
            return 0;
        }

        var replaced = 0;
        var count = Math.Min(maps.Length, mapData.Length);
        for (var i = 0; i < count; i++)
        {
            var mapId = maps[i] ?? "";
            var nodeId = mapData[i] ?? "";
            if (!RequiresReplacement(mapId, nodeId))
            {
                continue;
            }

            var replacement = replacementResolver(mapId, nodeId, i);
            if (!IsSafe(replacement))
            {
                continue;
            }

            maps[i] = replacement!.MapId;
            mapData[i] = replacement.NodeId;
            replaced++;
        }

        return replaced;
    }

    private static bool IsSafe(SolarMemoryMapSelectionReplacement? replacement)
    {
        return replacement != null
               && !string.IsNullOrWhiteSpace(replacement.MapId)
               && !string.IsNullOrWhiteSpace(replacement.NodeId)
               && !RequiresReplacement(replacement.MapId, replacement.NodeId);
    }
}
