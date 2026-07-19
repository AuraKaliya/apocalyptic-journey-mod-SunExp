using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

[Flags]
public enum FieldEffectPolicyFlags
{
    None = 0,
    RoundStart = 1,
    BuffAdded = 2,
    BurnOverflow = 4
}

public sealed class FieldEffectDefinition
{
    public FieldEffectDefinition(
        SunExpFieldId field,
        string slug,
        string buffId,
        string hudIconPath,
        int maxVisualTier,
        int fallbackMaxStacks,
        bool hasRoundStartHandler,
        bool hasBuffAddedPolicy)
    {
        Field = field;
        Slug = slug ?? "";
        BuffId = buffId ?? "";
        HudIconPath = hudIconPath ?? "";
        MaxVisualTier = Math.Max(1, maxVisualTier);
        FallbackMaxStacks = Math.Max(1, fallbackMaxStacks);
        HasRoundStartHandler = hasRoundStartHandler;
        HasBuffAddedPolicy = hasBuffAddedPolicy;
    }

    public SunExpFieldId Field { get; }

    public string Slug { get; }

    public string BuffId { get; }

    public string HudIconPath { get; }

    public int MaxVisualTier { get; }

    public int FallbackMaxStacks { get; }

    public bool HasRoundStartHandler { get; }

    public bool HasBuffAddedPolicy { get; }

    public FieldEffectPolicyFlags PolicyFlags
    {
        get
        {
            var flags = FieldEffectPolicyFlags.None;
            if (HasRoundStartHandler)
            {
                flags |= FieldEffectPolicyFlags.RoundStart;
            }

            if (HasBuffAddedPolicy)
            {
                flags |= FieldEffectPolicyFlags.BuffAdded;
            }

            if (Field == SunExpFieldId.ScorchingCanopy)
            {
                flags |= FieldEffectPolicyFlags.BurnOverflow;
            }

            return flags;
        }
    }
}

public sealed class FieldEffectRuntimeSpec
{
    public FieldEffectRuntimeSpec(
        FieldEffectDefinition definition,
        int maxStacks,
        string displayName,
        string description)
    {
        Definition = definition;
        MaxStacks = Math.Max(1, maxStacks);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? definition.BuffId : displayName;
        Description = description ?? "";
    }

    public FieldEffectDefinition Definition { get; }

    public SunExpFieldId Field => Definition.Field;

    public string Slug => Definition.Slug;

    public string BuffId => Definition.BuffId;

    public int MaxStacks { get; }

    public FieldEffectPolicyFlags PolicyFlags => Definition.PolicyFlags;

    public string DisplayName { get; }

    public string Description { get; }

    public string HudIconPath => Definition.HudIconPath;

    public int VisualTierForStacks(int stacks)
    {
        return Math.Min(Definition.MaxVisualTier, Math.Max(1, stacks));
    }

    public string HudIconPathForStacks(int stacks)
    {
        return VisualTierForStacks(stacks) > 0 ? HudIconPath : "";
    }
}

public static class FieldEffectRegistry
{
    private static readonly Dictionary<SunExpFieldId, FieldEffectDefinition> ByField = new()
    {
        [SunExpFieldId.ScorchingCanopy] = new FieldEffectDefinition(
            SunExpFieldId.ScorchingCanopy,
            "scorching_canopy",
            SunExpIds.ScorchingCanopy,
            hudIconPath: "Mods/SunExp/ModResource/Images/Buff/Area/\u707c\u70ed\u5929\u5e55",
            maxVisualTier: 9,
            fallbackMaxStacks: 9,
            hasRoundStartHandler: true,
            hasBuffAddedPolicy: true),
        [SunExpFieldId.SamsaraGarden] = new FieldEffectDefinition(
            SunExpFieldId.SamsaraGarden,
            "samsara_garden",
            SunExpIds.SamsaraGarden,
            hudIconPath: "Mods/SunExp/ModResource/Images/Buff/Area/\u8f6e\u56de\u82b1\u5ead",
            maxVisualTier: 4,
            fallbackMaxStacks: 5,
            hasRoundStartHandler: true,
            hasBuffAddedPolicy: false),
        [SunExpFieldId.MoonDomain] = new FieldEffectDefinition(
            SunExpFieldId.MoonDomain,
            "moon_domain",
            SunExpIds.MoonDomain,
            hudIconPath: "Mods/SunExp/ModResource/Images/Buff/Area/月之领域",
            maxVisualTier: 1,
            fallbackMaxStacks: 1,
            hasRoundStartHandler: false,
            hasBuffAddedPolicy: false)
    };
    private static readonly object Sync = new();
    private static volatile Dictionary<SunExpFieldId, FieldEffectRuntimeSpec> RuntimeSpecs = BuildFallbackSpecs();

    public static IReadOnlyCollection<FieldEffectDefinition> Definitions => ByField.Values;

    public static void WarmupConfigCache(string source)
    {
        lock (Sync)
        {
            var specs = new Dictionary<SunExpFieldId, FieldEffectRuntimeSpec>();
            foreach (var definition in ByField.Values)
            {
                specs[definition.Field] = BuildRuntimeSpec(definition, source);
            }

            RuntimeSpecs = specs;
        }

        SunExpLog.Debug("[FieldEffectRegistry] warmed field config cache from " + (source ?? ""));
    }

    public static bool TryGet(SunExpFieldId field, out FieldEffectDefinition definition)
    {
        return ByField.TryGetValue(field, out definition);
    }

    public static FieldEffectDefinition? DefinitionFor(SunExpFieldId field)
    {
        return ByField.TryGetValue(field, out var definition) ? definition : null;
    }

    public static FieldEffectRuntimeSpec RuntimeSpecFor(SunExpFieldId field)
    {
        var specs = RuntimeSpecs;
        if (specs.TryGetValue(field, out var spec))
        {
            return spec;
        }

        var definition = DefinitionFor(field);
        return definition == null
            ? new FieldEffectRuntimeSpec(
                new FieldEffectDefinition(SunExpFieldId.None, "", "", "", 1, 1, false, false),
                1,
                "",
                "")
            : new FieldEffectRuntimeSpec(definition, definition.FallbackMaxStacks, definition.BuffId, "");
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

    public static int MaxStacks(SunExpFieldId field)
    {
        return RuntimeSpecFor(field).MaxStacks;
    }

    public static int VisualTierForStacks(SunExpFieldId field, int stacks)
    {
        return RuntimeSpecFor(field).VisualTierForStacks(stacks);
    }

    public static FieldEffectPolicyFlags PolicyFlags(SunExpFieldId field)
    {
        return RuntimeSpecFor(field).PolicyFlags;
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

    private static Dictionary<SunExpFieldId, FieldEffectRuntimeSpec> BuildFallbackSpecs()
    {
        var specs = new Dictionary<SunExpFieldId, FieldEffectRuntimeSpec>();
        foreach (var definition in ByField.Values)
        {
            specs[definition.Field] = new FieldEffectRuntimeSpec(
                definition,
                definition.FallbackMaxStacks,
                definition.BuffId,
                "");
        }

        return specs;
    }

    private static FieldEffectRuntimeSpec BuildRuntimeSpec(FieldEffectDefinition definition, string source)
    {
        var maxStacks = definition.FallbackMaxStacks;
        var displayName = definition.BuffId;
        var description = "";
        try
        {
            var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, definition.BuffId);
            maxStacks = Math.Max(1, DictionaryUtil.ParseInt(DictionaryUtil.Get(data, "UpperBound"), definition.FallbackMaxStacks));
            displayName = data.Localize("Name");
            description = data.Localize("Description");
            if (string.IsNullOrWhiteSpace(description) || description == "Description")
            {
                description = data.Localize("Tips");
            }

            description = description.Description();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[FieldEffectRegistry] field spec fallback: field="
                + definition.Slug
                + ", source="
                + (source ?? "")
                + ", error="
                + ex.Message);
        }

        return new FieldEffectRuntimeSpec(definition, maxStacks, displayName, description);
    }
}
