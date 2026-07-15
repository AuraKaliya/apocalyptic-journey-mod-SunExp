using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckCardClassification
{
    internal const string DefaultCardPackId = "cardpack_1";

    private static readonly HashSet<string> ExcludedDerivedCardTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "衍生牌",
        "衍生卡",
        "Derived",
        "Derived Card",
        "Generated Card",
        "Token Card"
    };

    internal static HashSet<string> BuildCareerSkillCardIds(
        IEnumerable<IDictionary<string, string>>? careerRows)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in careerRows ?? Array.Empty<IDictionary<string, string>>())
        {
            foreach (var pair in row.Where(pair => IsSkillSlotKey(pair.Key)))
            {
                foreach (var cardId in SplitCardIds(pair.Value))
                {
                    result.Add(NormalizeCardId(cardId));
                }
            }
        }

        return result;
    }

    internal static bool IsCareerSkillCard(string? cardId, ISet<string>? careerSkillCardIds)
    {
        if (careerSkillCardIds == null || string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        return careerSkillCardIds.Contains(NormalizeCardId(cardId));
    }

    internal static bool IsExcludedDerivedCard(IDictionary<string, string>? cardRow)
    {
        if (cardRow == null)
        {
            return false;
        }

        foreach (var key in new[] { "Type", "Type_en", "Type_zh-Hans", "Type_zh-Hant", "Type_ja" })
        {
            if (cardRow.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value)
                && ExcludedDerivedCardTypes.Contains(value.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldExcludeFromStarterDeck(
        string? cardId,
        IDictionary<string, string>? cardRow,
        ISet<string>? careerSkillCardIds)
    {
        return IsCareerSkillCard(cardId, careerSkillCardIds)
               || IsExcludedDerivedCard(cardRow);
    }

    internal static string ResolveEffectivePackId(
        IDictionary<string, string>? cardRow,
        Func<IDictionary<string, string>, string>? hostResolver = null)
    {
        if (cardRow == null)
        {
            return DefaultCardPackId;
        }

        if (hostResolver != null)
        {
            try
            {
                var resolved = hostResolver(cardRow);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved.Trim();
                }
            }
            catch
            {
                // Fall through to the host-compatible local fallback.
            }
        }

        return cardRow.TryGetValue("PackBelong", out var rawPackId)
               && !string.IsNullOrWhiteSpace(rawPackId)
            ? rawPackId.Trim()
            : DefaultCardPackId;
    }

    private static bool IsSkillSlotKey(string? key)
    {
        var candidate = key ?? "";
        if (string.IsNullOrWhiteSpace(candidate) || !candidate.StartsWith("Skill", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = candidate.Substring("Skill".Length);
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static IEnumerable<string> SplitCardIds(string? value)
    {
        return (value ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeCardId)
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }

    private static string NormalizeCardId(string? cardId)
    {
        return (cardId ?? "").Trim().Replace("*", "");
    }
}
