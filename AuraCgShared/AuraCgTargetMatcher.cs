using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;

namespace AuraCg.Shared;

public static class AuraCgTargetMatcher
{
    public static bool MatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        var normalizedRole = (roleId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return true;
        }

        var targets = (entry.TargetRoleIds ?? new List<string>())
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => target.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targets.Contains("*", StringComparer.Ordinal))
        {
            return true;
        }

        return targets.Any(target => AuraSharedContentId.Resolve(
            target,
            new[] { normalizedRole },
            entry.OwnerModId,
            AuraSharedIdentity.OfficialCareerPrefix).Success);
    }
}
