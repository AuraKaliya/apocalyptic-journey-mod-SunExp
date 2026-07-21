using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class CardVisualEffectSpec
{
    public CardVisualEffectSpec(
        string ownerModId,
        string id,
        CardVisualEffectTarget target,
        string visualEffectId,
        string displayName,
        int priority,
        IEnumerable<string>? cardIds)
    {
        OwnerModId = ownerModId;
        Id = id;
        Target = target;
        VisualEffectId = visualEffectId;
        DisplayName = displayName;
        Priority = priority;
        CardIds = Normalize(cardIds).Distinct(System.StringComparer.Ordinal).ToArray();
    }

    public string OwnerModId { get; }

    public string Id { get; }

    public CardVisualEffectTarget Target { get; }

    public string VisualEffectId { get; }

    public string DisplayName { get; }

    public int Priority { get; }

    public IReadOnlyList<string> CardIds { get; }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values)
    {
        if (values == null)
        {
            yield break;
        }

        foreach (var value in values)
        {
            var normalized = value?.Trim() ?? "";
            if (normalized.Length > 0)
            {
                yield return normalized;
            }
        }
    }
}
