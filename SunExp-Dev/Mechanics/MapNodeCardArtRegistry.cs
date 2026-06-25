using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class MapNodeCardArtRegistry
{
    private static readonly MapNodeCardArtSpec[] Specs =
    {
        new(
            SunExpIds.SolarBossSecondSunMapTexturePath,
            MapNodeCardArtFitMode.ContainTrimmed,
            mapIds: new[] { SunExpIds.SolarBossSecondSunMapId, SunExpIds.SolarBossSecondSunShortMapId },
            levelIds: new[] { SunExpIds.SolarBossSecondSunLevelId, "level_second_sun_last_day" },
            enemyIds: new[] { SunExpIds.SolarBossSecondSunEnemyId, "boss_second_sun_last_day" },
            priority: 100),
        new(
            SunExpIds.SolarBossSaintWunaMapTexturePath,
            MapNodeCardArtFitMode.ContainTrimmed,
            mapIds: new[] { SunExpIds.SolarBossSaintWunaMapId, SunExpIds.SolarBossSaintWunaShortMapId },
            levelIds: new[] { SunExpIds.SolarBossSaintWunaLevelId, "level_saint_wuna" },
            enemyIds: new[] { SunExpIds.SolarBossSaintWunaEnemyId, "boss_saint_wuna" },
            priority: 100)
    };

    public static IReadOnlyList<MapNodeCardArtSpec> All => Specs;

    public static MapNodeCardArtSpec? Resolve(IReadOnlyDictionary<string, string>? nodeData, string? enemyId = null)
    {
        var mapId = Get(nodeData, "Id");
        var levelId = Get(nodeData, "NodeId");
        return Specs
            .OrderByDescending(spec => spec.Priority)
            .FirstOrDefault(spec => spec.Matches(mapId, levelId, enemyId));
    }

    private static string Get(IReadOnlyDictionary<string, string>? data, string key)
    {
        return data != null && data.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
