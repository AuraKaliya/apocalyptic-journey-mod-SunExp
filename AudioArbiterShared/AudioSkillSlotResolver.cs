using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioArbiter.Shared;

internal static class AudioSkillSlotResolver
{
    private const string SkillPrefix = "Skill";

    public static int Resolve(IReadOnlyDictionary<string, string>? roleRow, string skillId)
    {
        if (roleRow == null || string.IsNullOrWhiteSpace(skillId))
        {
            return 0;
        }

        foreach (var pair in roleRow
                     .Where(pair => TryReadSlot(pair.Key, out _))
                     .OrderBy(pair => ReadSlot(pair.Key)))
        {
            var slot = ReadSlot(pair.Key);
            if (SplitSkillIds(pair.Value).Any(configured => MatchesId(configured, skillId)))
            {
                return slot;
            }
        }

        return 0;
    }

    public static bool IsConfiguredSlot(IReadOnlyDictionary<string, string>? roleRow, int slot)
    {
        if (roleRow == null || slot <= 0)
        {
            return false;
        }

        return roleRow.Any(pair => ReadSlot(pair.Key) == slot && SplitSkillIds(pair.Value).Any());
    }

    private static IEnumerable<string> SplitSkillIds(string value)
    {
        return (value ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeId)
            .Where(id => id.Length > 0);
    }

    private static bool MatchesId(string configuredId, string activeId)
    {
        var configured = NormalizeId(configuredId);
        var active = NormalizeId(activeId);
        if (configured.Length == 0 || active.Length == 0)
        {
            return false;
        }

        return string.Equals(configured, active, StringComparison.OrdinalIgnoreCase)
               || configured.EndsWith("_" + active, StringComparison.OrdinalIgnoreCase)
               || active.EndsWith("_" + configured, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeId(string value)
    {
        return (value ?? "").Trim().TrimStart('*');
    }

    private static int ReadSlot(string key)
    {
        return TryReadSlot(key, out var slot) ? slot : 0;
    }

    private static bool TryReadSlot(string key, out int slot)
    {
        slot = 0;
        if (string.IsNullOrWhiteSpace(key)
            || !key.StartsWith(SkillPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(key.Substring(SkillPrefix.Length), out slot) && slot > 0;
    }
}
