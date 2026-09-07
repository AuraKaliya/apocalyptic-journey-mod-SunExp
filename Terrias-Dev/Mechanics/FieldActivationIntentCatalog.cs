using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public enum FieldActivationAmountPolicy
{
    Fixed = 0,
    AuthoritativeScorchingCanopyCarrierStacks = 1
}

public sealed class FieldActivationIntentDefinition
{
    public FieldActivationIntentDefinition(
        TerriasFieldId field,
        string intentId,
        FieldActivationAmountPolicy amountPolicy,
        int fixedAmount)
    {
        Field = field;
        IntentId = intentId ?? "";
        AmountPolicy = amountPolicy;
        FixedAmount = Math.Max(0, fixedAmount);
    }

    public TerriasFieldId Field { get; }

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
    public const string FrostmoonNewGodIntent = "MoonHomecoming.FrostmoonNewGod";

    private static readonly Dictionary<TerriasFieldId, Dictionary<string, FieldActivationIntentDefinition>> Definitions =
        BuildDefinitions();

    public static bool TryResolve(
        TerriasFieldId field,
        string intentId,
        out FieldActivationIntentDefinition definition)
    {
        definition = null!;
        return field != TerriasFieldId.None
               && !string.IsNullOrWhiteSpace(intentId)
               && Definitions.TryGetValue(field, out var byIntent)
               && byIntent.TryGetValue(intentId, out definition);
    }

    private static Dictionary<TerriasFieldId, Dictionary<string, FieldActivationIntentDefinition>> BuildDefinitions()
    {
        var definitions = new Dictionary<TerriasFieldId, Dictionary<string, FieldActivationIntentDefinition>>();
        AddFixed(definitions, TerriasFieldId.ScorchingCanopy, ScorchingCanopyCardIntent, 1);
        AddFixed(definitions, TerriasFieldId.ScorchingCanopy, CanopyReturnCardIntent, 2);
        AddFixed(definitions, TerriasFieldId.ScorchingCanopy, RadiantOathCardIntent, 1);
        Add(definitions, new FieldActivationIntentDefinition(
            TerriasFieldId.ScorchingCanopy,
            ScorchingCanopyCarrierIntent,
            FieldActivationAmountPolicy.AuthoritativeScorchingCanopyCarrierStacks,
            0));
        AddFixed(definitions, TerriasFieldId.MoonDomain, ColumbinaHomesicknessIntent, 1);
        AddFixed(definitions, TerriasFieldId.MoonDomain, FrostmoonNewGodIntent, 1);
        return definitions;
    }

    private static void AddFixed(
        Dictionary<TerriasFieldId, Dictionary<string, FieldActivationIntentDefinition>> definitions,
        TerriasFieldId field,
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
        Dictionary<TerriasFieldId, Dictionary<string, FieldActivationIntentDefinition>> definitions,
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
