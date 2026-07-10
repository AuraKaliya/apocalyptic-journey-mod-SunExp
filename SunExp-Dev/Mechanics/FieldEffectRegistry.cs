using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class FieldEffectDefinition
{
    public FieldEffectDefinition(
        SunExpFieldId field,
        string slug,
        string buffId,
        int fallbackMaxStacks,
        bool hasRoundStartHandler,
        bool hasBuffAddedPolicy)
    {
        Field = field;
        Slug = slug ?? "";
        BuffId = buffId ?? "";
        FallbackMaxStacks = Math.Max(1, fallbackMaxStacks);
        HasRoundStartHandler = hasRoundStartHandler;
        HasBuffAddedPolicy = hasBuffAddedPolicy;
    }

    public SunExpFieldId Field { get; }

    public string Slug { get; }

    public string BuffId { get; }

    public int FallbackMaxStacks { get; }

    public bool HasRoundStartHandler { get; }

    public bool HasBuffAddedPolicy { get; }
}

public static class FieldEffectRegistry
{
    private static readonly Dictionary<SunExpFieldId, FieldEffectDefinition> ByField = new()
    {
        [SunExpFieldId.ScorchingCanopy] = new FieldEffectDefinition(
            SunExpFieldId.ScorchingCanopy,
            "scorching_canopy",
            SunExpIds.ScorchingCanopy,
            fallbackMaxStacks: 9,
            hasRoundStartHandler: true,
            hasBuffAddedPolicy: true)
    };

    public static IReadOnlyCollection<FieldEffectDefinition> Definitions => ByField.Values;

    public static bool TryGet(SunExpFieldId field, out FieldEffectDefinition definition)
    {
        return ByField.TryGetValue(field, out definition);
    }

    public static FieldEffectDefinition? DefinitionFor(SunExpFieldId field)
    {
        return ByField.TryGetValue(field, out var definition) ? definition : null;
    }

    public static string FieldBuffId(SunExpFieldId field)
    {
        return DefinitionFor(field)?.BuffId ?? "";
    }

    public static string FieldSlug(SunExpFieldId field)
    {
        return DefinitionFor(field)?.Slug ?? "";
    }

    public static int FallbackMaxStacks(SunExpFieldId field)
    {
        return DefinitionFor(field)?.FallbackMaxStacks ?? 1;
    }

    public static SunExpFieldId ParseFieldId(string? fieldId)
    {
        if (string.IsNullOrWhiteSpace(fieldId))
        {
            return SunExpFieldId.None;
        }

        var value = fieldId!.Trim();
        foreach (var definition in ByField.Values)
        {
            if (string.Equals(value, definition.Slug, StringComparison.Ordinal))
            {
                return definition.Field;
            }
        }

        return SunExpFieldId.None;
    }

    public static SunExpFieldId FieldIdFromBuffId(string? buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return SunExpFieldId.None;
        }

        var value = buffId!.Trim();
        foreach (var definition in ByField.Values)
        {
            if (string.Equals(value, definition.BuffId, StringComparison.Ordinal)
                || string.Equals(value, definition.Slug, StringComparison.Ordinal))
            {
                return definition.Field;
            }
        }

        return SunExpFieldId.None;
    }
}
