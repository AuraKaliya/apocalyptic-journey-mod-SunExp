using System;
using System.Collections.Generic;
using System.Globalization;

namespace Terrias.Dll.Mechanics;

public readonly struct CompanionIntentPresentationValue
{
    public CompanionIntentPresentationValue(
        int displayIndex,
        string handlerId,
        int authoritativeValue,
        int repeatCount,
        string displayText)
    {
        DisplayIndex = Math.Max(1, displayIndex);
        HandlerId = handlerId ?? "";
        AuthoritativeValue = Math.Max(0, authoritativeValue);
        RepeatCount = Math.Max(1, repeatCount);
        DisplayText = displayText ?? "0";
    }

    public int DisplayIndex { get; }

    public string HandlerId { get; }

    public int AuthoritativeValue { get; }

    public int RepeatCount { get; }

    public string DisplayText { get; }
}

/// <summary>
/// Converts the committed companion plan into stable DesVal text. The plan is
/// already the authoritative input for execution, so presentation must not run
/// native attacker/target calculations a second time with a different UI-time
/// context.
/// </summary>
public static class CompanionIntentPresentationSnapshot
{
    public const int ClearedDescriptionSlots = 8;

    public static CompanionIntentPresentationValue Resolve(
        CompanionResolvedEffect? effect,
        int displayIndex,
        string spiritElementId = "")
    {
        if (effect == null)
        {
            return new CompanionIntentPresentationValue(displayIndex, "", 0, 1, "0");
        }

        var authoritativeValue = Math.Max(0,
            effect.BuffStacks > 0 ? effect.BuffStacks : effect.Value);
        var repeatCount = Math.Max(1, effect.RepeatCount);
        var displayText = authoritativeValue.ToString(CultureInfo.InvariantCulture);
        if (IsMultiDamage(effect.HandlerId, repeatCount))
        {
            displayText += "×" + repeatCount.ToString(CultureInfo.InvariantCulture);
        }
        if (IsDamage(effect.HandlerId) && SpiritElementService.NormalizeId(spiritElementId).Length > 0)
        {
            displayText = SpiritElementService.DisplayName(spiritElementId) + " · " + displayText;
        }

        return new CompanionIntentPresentationValue(
            displayIndex,
            effect.HandlerId,
            authoritativeValue,
            repeatCount,
            displayText);
    }

    public static int Fingerprint(
        IReadOnlyList<CompanionResolvedEffect> effects,
        IReadOnlyList<CompanionIntentEffectSpec> specs,
        string spiritElementId = "")
    {
        unchecked
        {
            var hash = 17;
            hash = HashText(hash, SpiritElementService.NormalizeId(spiritElementId));
            var count = effects.Count;
            hash = hash * 31 + count;
            for (var index = 0; index < count; index++)
            {
                var effect = effects[index];
                var displayIndex = index < specs.Count
                    ? Math.Max(1, specs[index].DisplayIndex)
                    : index + 1;
                hash = hash * 31 + displayIndex;
                hash = hash * 31 + effect.Value;
                hash = hash * 31 + effect.BuffStacks;
                hash = hash * 31 + effect.RepeatCount;
                hash = HashText(hash, effect.HandlerId);
            }

            return hash;
        }
    }

    private static bool IsMultiDamage(string? handlerId, int repeatCount)
    {
        return repeatCount > 1
            && !string.IsNullOrWhiteSpace(handlerId)
            && handlerId!.StartsWith("damage.", StringComparison.Ordinal);
    }

    private static bool IsDamage(string? handlerId)
    {
        return !string.IsNullOrWhiteSpace(handlerId)
               && handlerId!.StartsWith("damage.", StringComparison.Ordinal);
    }

    private static int HashText(int hash, string? value)
    {
        unchecked
        {
            if (string.IsNullOrEmpty(value))
            {
                return hash * 31;
            }

            for (var index = 0; index < value!.Length; index++)
            {
                hash = hash * 31 + value[index];
            }

            return hash;
        }
    }
}
