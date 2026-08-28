using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactMath
{
    public static int ApplyDamageMultiplier(int damage, int bonusBasisPoints, int maximumBonusPercent = 50)
    {
        var normalizedDamage = Math.Max(0, damage);
        var maximum = Math.Max(0, maximumBonusPercent) * 100;
        var normalizedBonus = Math.Max(0, Math.Min(maximum, bonusBasisPoints));
        return Math.Max(normalizedDamage > 0 ? 1 : 0, (int)Math.Round(
            normalizedDamage * (10000L + normalizedBonus) / 10000d,
            MidpointRounding.AwayFromZero));
    }

    public static string LoadoutHash(IEnumerable<SpiritArtifactBattleItemSnapshot>? items, string registryHash)
    {
        var builder = new StringBuilder(registryHash ?? "");
        foreach (var item in (items ?? Array.Empty<SpiritArtifactBattleItemSnapshot>())
                     .OrderBy(value => value.SlotId, StringComparer.Ordinal))
        {
            builder.Append('|').Append(item.SlotId).Append(':').Append(item.ArtifactUid)
                .Append(':').Append(item.PieceId).Append(':').Append(item.Rarity).Append(':').Append(item.Level)
                .Append(':').Append(item.MainStat?.StatId).Append('=').Append(item.MainStat?.Value ?? 0);
            foreach (var roll in item.SubStatRolls ?? new List<SpiritArtifactStatRoll>())
                builder.Append(',').Append(roll.StatId).Append('=').Append(roll.Value);
        }
        return StableHash(builder.ToString()).ToString("X8");
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value ?? "")
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
