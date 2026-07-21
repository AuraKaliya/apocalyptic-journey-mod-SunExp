using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class MapNodeCardArtRegistry
{
    public static IReadOnlyList<MapNodeCardArtSpec> All => VisualRegistry.MapNodeArtSpecs();

    public static MapNodeCardArtSpec? Resolve(IReadOnlyDictionary<string, string>? nodeData, string? enemyId = null)
    {
        var mapId = Get(nodeData, "Id");
        var levelId = Get(nodeData, "NodeId");
        return VisualRegistry.MapNodeArtSpecs()
            .OrderByDescending(spec => spec.Priority)
            .FirstOrDefault(spec => spec.Matches(mapId, levelId, enemyId));
    }

    private static string Get(IReadOnlyDictionary<string, string>? data, string key)
    {
        return data != null && data.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
