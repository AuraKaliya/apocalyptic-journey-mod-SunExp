using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsVoiceSkillDescriptor
{
    public string Id { get; set; } = "";

    public int Slot { get; set; }
}

public static class AuraToolsVoiceSkillBindingMigration
{
    private const string SkillVoice = "SkillVoice";
    private const string Committed = "Committed";

    public static bool Migrate(
        AuraToolsVoiceBindingSettings settings,
        string providerKind,
        string providerStage,
        int? manifestSkillSlot,
        IEnumerable<AuraToolsVoiceSkillDescriptor>? configuredSkills)
    {
        if (settings == null
            || !string.Equals(providerKind, SkillVoice, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var skills = (configuredSkills ?? Array.Empty<AuraToolsVoiceSkillDescriptor>())
            .Where(skill => skill != null && skill.Slot > 0 && !string.IsNullOrWhiteSpace(skill.Id))
            .ToArray();
        if (skills.Length == 0)
        {
            // Role data can be unavailable during early startup. Keep the legacy
            // value only as migration input; the runtime never matches it.
            return false;
        }

        int? resolvedSlot = null;
        if (settings.SkillSlot.HasValue
            && skills.Any(skill => skill.Slot == settings.SkillSlot.Value))
        {
            resolvedSlot = settings.SkillSlot;
        }
        else if (!string.IsNullOrWhiteSpace(settings.ActionId))
        {
            resolvedSlot = skills
                .Where(skill => MatchesId(settings.ActionId, skill.Id))
                .Select(skill => (int?)skill.Slot)
                .FirstOrDefault();
        }

        if (!resolvedSlot.HasValue
            && manifestSkillSlot.HasValue
            && skills.Any(skill => skill.Slot == manifestSkillSlot.Value))
        {
            resolvedSlot = manifestSkillSlot;
        }

        var targetStage = string.IsNullOrWhiteSpace(providerStage) ? Committed : providerStage.Trim();
        var changed = settings.SkillSlot != resolvedSlot
                      || !string.IsNullOrEmpty(settings.ActionId)
                      || !string.Equals(settings.Signal, SkillVoice, StringComparison.Ordinal)
                      || !string.Equals(settings.Stage, targetStage, StringComparison.Ordinal);
        settings.SkillSlot = resolvedSlot;
        settings.ActionId = "";
        settings.Signal = SkillVoice;
        settings.Stage = targetStage;
        return changed;
    }

    private static bool MatchesId(string leftValue, string rightValue)
    {
        var left = NormalizeId(leftValue);
        var right = NormalizeId(rightValue);
        return left.Length > 0
               && right.Length > 0
               && (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                   || left.EndsWith("_" + right, StringComparison.OrdinalIgnoreCase)
                   || right.EndsWith("_" + left, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeId(string value)
    {
        return (value ?? "").Trim().TrimStart('*');
    }
}
