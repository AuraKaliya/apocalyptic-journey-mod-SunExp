using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public enum FieldActivationAmountPolicy
{
    Fixed = 0,
    AuthoritativeScorchingCanopyCarrierStacks = 1
}

public sealed class FieldActivationIntentDefinition
{
    public FieldActivationIntentDefinition(
        SunExpFieldId field,
        string intentId,
        FieldActivationAmountPolicy amountPolicy,
        int fixedAmount)
    {
        Field = field;
        IntentId = intentId ?? "";
        AmountPolicy = amountPolicy;
        FixedAmount = Math.Max(0, fixedAmount);
    }

    public SunExpFieldId Field { get; }

    public string IntentId { get; }

    public FieldActivationAmountPolicy AmountPolicy { get; }

    public int FixedAmount { get; }
}

/// <summary>
/// Declares the small server-resolved capabilities that may activate a shared
/// field from a non-host client. Client-provided stack counts are never used
/// as the authoritative result.
/// </summary>
public static class FieldActivationIntentCatalog
{
    public const string ScorchingCanopyCardIntent = "card.scorching_canopy";
    public const string CanopyReturnCardIntent = "card.canopy_return";
    public const string RadiantOathCardIntent = "card.radiant_oath";
    public const string ScorchingCanopyCarrierIntent = "carrier.scorching_canopy";
    public const string ColumbinaHomesicknessIntent = "Columbina.Homesickness";

    private static readonly Dictionary<SunExpFieldId, Dictionary<string, FieldActivationIntentDefinition>> Definitions =
        BuildDefinitions();

    public static bool TryResolve(
        SunExpFieldId field,
        string intentId,
        out FieldActivationIntentDefinition definition)
    {
        definition = null!;
        return field != SunExpFieldId.None
               && !string.IsNullOrWhiteSpace(intentId)
               && Definitions.TryGetValue(field, out var byIntent)
               && byIntent.TryGetValue(intentId, out definition);
    }

    private static Dictionary<SunExpFieldId, Dictionary<string, FieldActivationIntentDefinition>> BuildDefinitions()
    {
        var definitions = new Dictionary<SunExpFieldId, Dictionary<string, FieldActivationIntentDefinition>>();
        AddFixed(definitions, SunExpFieldId.ScorchingCanopy, ScorchingCanopyCardIntent, 1);
        AddFixed(definitions, SunExpFieldId.ScorchingCanopy, CanopyReturnCardIntent, 2);
        AddFixed(definitions, SunExpFieldId.ScorchingCanopy, RadiantOathCardIntent, 1);
        Add(definitions, new FieldActivationIntentDefinition(
            SunExpFieldId.ScorchingCanopy,
            ScorchingCanopyCarrierIntent,
            FieldActivationAmountPolicy.AuthoritativeScorchingCanopyCarrierStacks,
            0));
        AddFixed(definitions, SunExpFieldId.MoonDomain, ColumbinaHomesicknessIntent, 1);
        return definitions;
    }

    private static void AddFixed(
        Dictionary<SunExpFieldId, Dictionary<string, FieldActivationIntentDefinition>> definitions,
        SunExpFieldId field,
        string intentId,
        int amount)
    {
        Add(definitions, new FieldActivationIntentDefinition(
            field,
            intentId,
            FieldActivationAmountPolicy.Fixed,
            amount));
    }

    private static void Add(
        Dictionary<SunExpFieldId, Dictionary<string, FieldActivationIntentDefinition>> definitions,
        FieldActivationIntentDefinition definition)
    {
        if (!definitions.TryGetValue(definition.Field, out var byIntent))
        {
            byIntent = new Dictionary<string, FieldActivationIntentDefinition>(StringComparer.Ordinal);
            definitions.Add(definition.Field, byIntent);
        }

        byIntent.Add(definition.IntentId, definition);
    }
}
