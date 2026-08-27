using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;

namespace AuraToolsExp.Dll.Features.Cg;

internal static class AuraToolsRoleSkillIdentity
{
    internal static string ResolveEquivalent(
        string declaredSkillId,
        IEnumerable<string> authoritativeSkillIds,
        string ownerModId)
    {
        var available = (authoritativeSkillIds ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolution = AuraSharedContentId.Resolve(
            declaredSkillId,
            available,
            ownerModId,
            "careercard_");
        return resolution.Success ? resolution.ResolvedId : "";
    }

    internal static bool ContainsEquivalent(
        IEnumerable<string> authoritativeSkillIds,
        string candidateSkillId,
        string ownerModId)
    {
        return ResolveEquivalent(candidateSkillId, authoritativeSkillIds, ownerModId).Length > 0;
    }
}
