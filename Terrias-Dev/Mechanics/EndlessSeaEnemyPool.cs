using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaEnemyPool
{
    public static readonly IReadOnlyList<string> NormalBossEnemyIds = new[]
    {
        "enemy_10007",
        "enemy_10015",
        "enemy_10022",
        "enemy_10033",
        "enemy_10020",
        "enemy_10029",
        "enemy_10046",
        "enemy_10047",
        "AbyssPhantom_abyssphantom_abyss_brood_matron",
        "enemy_10061",
        "enemy_10032",
        "enemy_10059"
    };

    public static readonly IReadOnlyList<string> SpecialBossEnemyIds = new[]
    {
        "enemy_10048",
        "enemy_10027",
        "enemy_10060",
        "enemy_10055"
    };

    public static bool IsNormalBossLevel(Dictionary<string, string> mapRow)
    {
        return LevelContainsAny(mapRow, NormalBossEnemyIds);
    }

    public static bool IsSpecialBossLevel(Dictionary<string, string> mapRow)
    {
        return LevelContainsAny(mapRow, SpecialBossEnemyIds);
    }

    public static string? PickNormalBossEnemy()
    {
        return PickExistingEnemy(NormalBossEnemyIds);
    }

    public static string? PickSpecialBossEnemy()
    {
        return PickExistingEnemy(SpecialBossEnemyIds);
    }

    private static bool LevelContainsAny(Dictionary<string, string> mapRow, IReadOnlyList<string> enemyIds)
    {
        var levelId = DictionaryUtil.Get(mapRow, "NodeId");
        var level = TerriasConfigIndex.Row(DataType.Level, levelId);
        if (level == null)
        {
            return false;
        }

        var configured = SplitIds(DictionaryUtil.Get(level, "EnemyIds"));
        return configured.Any(id => enemyIds.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    private static string? PickExistingEnemy(IReadOnlyList<string> ids)
    {
        var existing = ids
            .Where(id => TerriasConfigIndex.Row(DataType.Enemy, id) != null)
            .ToList();
        if (existing.Count == 0)
        {
            return null;
        }

        var index = Math.Abs((MapManager.Instance?.NowDice ?? Dice.Default).Roll().Value) % existing.Count;
        return existing[index];
    }

    private static IEnumerable<string> SplitIds(string value)
    {
        return (value ?? "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim());
    }
}
