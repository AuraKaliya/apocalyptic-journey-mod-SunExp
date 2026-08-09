using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AuraCombatSimulation.Shared;

public enum CombatInteractionKind
{
    Unknown,
    ChooseCards,
    BurnCards,
    DiscardCards,
    RetrieveCards,
    CopyCards,
    ModifyCards,
    TransferCards
}

public enum CombatInteractionZone
{
    Unknown,
    Hand,
    DrawPile,
    DiscardPile,
    Deck,
    Generated,
    AdventureDeck
}

public enum CombatInteractionEffectKind
{
    Unknown,
    BurnSelected,
    DiscardSelected,
    RetainSelected,
    DuplicateSelected,
    ModifySelectedCost,
    ModifySelectedPersistentCost,
    ModifySelectedExtraUses,
    TransferSelectedCopy,
    AddStatusPerSelected,
    AddStatusBySelectionCount
}

public sealed class CombatInteractionEffectDefinition
{
    public CombatInteractionEffectKind Kind { get; set; }

    public string DefinitionId { get; set; } = "";

    public double Amount { get; set; }

    public double BaseAmount { get; set; }

    public double AmountPerSelection { get; set; }

    public int Duration { get; set; }

    public CombatInteractionEffectDefinition Clone()
    {
        return (CombatInteractionEffectDefinition)MemberwiseClone();
    }
}

/// <summary>
/// Serializable contract for a native follow-up selection.  Confirmation,
/// prompt cancellation and undoing the parent action are deliberately kept as
/// separate concepts: the native game does not treat them as equivalent.
/// </summary>
public sealed class CombatInteractionDefinition
{
    public const int CurrentContractVersion = 2;

    public int ContractVersion { get; set; } = CurrentContractVersion;

    public string SourceApi { get; set; } = "";

    public string NativeMode { get; set; } = "";

    public CombatInteractionKind Kind { get; set; }

    public CombatInteractionZone Zone { get; set; }

    public int MinSelections { get; set; }

    public int MaxSelections { get; set; } = 1;

    public bool CanConfirmEarly { get; set; }

    public bool CanConfirmEmpty { get; set; }

    public bool CanCancelPrompt { get; set; }

    public bool CanCancelParentAction { get; set; }

    public bool PromptMandatory { get; set; } = true;

    public bool DistinctSelections { get; set; } = true;

    public bool OrderedSelections { get; set; }

    public bool EffectsComplete { get; set; }

    public List<CombatInteractionEffectDefinition> SelectionEffects { get; set; } =
        new();

    public CombatInteractionDefinition Clone()
    {
        return new CombatInteractionDefinition
        {
            ContractVersion = ContractVersion,
            SourceApi = SourceApi,
            NativeMode = NativeMode,
            Kind = Kind,
            Zone = Zone,
            MinSelections = MinSelections,
            MaxSelections = MaxSelections,
            CanConfirmEarly = CanConfirmEarly,
            CanConfirmEmpty = CanConfirmEmpty,
            CanCancelPrompt = CanCancelPrompt,
            CanCancelParentAction = CanCancelParentAction,
            PromptMandatory = PromptMandatory,
            DistinctSelections = DistinctSelections,
            OrderedSelections = OrderedSelections,
            EffectsComplete = EffectsComplete,
            SelectionEffects = (SelectionEffects ?? new List<CombatInteractionEffectDefinition>())
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList()
        };
    }

    public CombatInteractionDefinition Normalize()
    {
        var copy = Clone();
        copy.ContractVersion = CurrentContractVersion;
        copy.MinSelections = Math.Max(0, copy.MinSelections);
        copy.MaxSelections = Math.Max(copy.MinSelections, copy.MaxSelections);
        copy.CanConfirmEmpty = copy.CanConfirmEmpty && copy.MinSelections == 0;
        copy.CanConfirmEarly = copy.CanConfirmEarly
                               || copy.MinSelections < copy.MaxSelections;
        copy.PromptMandatory = !copy.CanCancelPrompt;
        return copy;
    }
}

/// <summary>
/// Shared, content-id-agnostic inference for Witch native selection APIs.
/// The same inference is consumed by live observation, simulation and tooling.
/// </summary>
public static class CombatInteractionContractInference
{
    private static readonly string[] SupportedApis =
    {
        "ChooseCardToAction",
        "BurnCard",
        "ThrowCard",
        "PackToDeckAction",
        "CopyCardWare",
        "OutFightSelectCardToAction"
    };

    public static bool TryInfer(
        string? script,
        out CombatInteractionDefinition definition)
    {
        definition = new CombatInteractionDefinition();
        if (string.IsNullOrWhiteSpace(script))
        {
            return false;
        }

        foreach (var api in SupportedApis)
        {
            if (!TryReadInvocation(script!, api, out var invocation, out var arguments))
            {
                continue;
            }

            var maximum = arguments.Count == 0
                ? 1
                : ReadNonNegativeInteger(arguments[0], 1);
            maximum = Math.Max(0, maximum);
            var nativeMode = ReadNativeMode(api, arguments);
            var optional = string.Equals(nativeMode, "2", StringComparison.Ordinal);
            definition = new CombatInteractionDefinition
            {
                SourceApi = api,
                NativeMode = nativeMode,
                Kind = KindOf(api),
                Zone = ZoneOf(api, invocation),
                MinSelections = optional ? 0 : maximum,
                MaxSelections = maximum,
                CanConfirmEarly = optional,
                CanConfirmEmpty = optional,
                CanCancelPrompt = false,
                CanCancelParentAction = false,
                PromptMandatory = true,
                DistinctSelections = true,
                OrderedSelections = false
            };
            InferEffects(api, invocation, definition);
            definition.EffectsComplete = IsEffectComplete(definition);
            definition = definition.Normalize();
            return true;
        }

        return false;
    }

    private static void InferEffects(
        string api,
        string invocation,
        CombatInteractionDefinition definition)
    {
        if (string.Equals(api, "BurnCard", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(invocation, "InternalBurning", "BurnCardByData"))
        {
            AddEffect(definition, CombatInteractionEffectKind.BurnSelected);
        }
        if (string.Equals(api, "ThrowCard", StringComparison.OrdinalIgnoreCase))
        {
            AddEffect(definition, CombatInteractionEffectKind.DiscardSelected);
        }
        if (ContainsAny(
                invocation,
                "SpecialTags.Add(\"Retain\"",
                "SpecialTag.Add(\"Retain\"",
                "Vars[\"SpecialTag\"] = \"Retain\"",
                "Vars[\"SpecialTag\"] += \",Retain\""))
        {
            AddEffect(definition, CombatInteractionEffectKind.RetainSelected);
        }
        if (ContainsAny(invocation, "CreateCard(", "CopyCardWare("))
        {
            AddEffect(definition, CombatInteractionEffectKind.DuplicateSelected);
        }
        if (Regex.IsMatch(invocation, "OnceExCost[^;\\r\\n]*=\\s*\\\"?-?3", RegexOptions.IgnoreCase)
            || Regex.IsMatch(invocation, @"OnceExCost\s*=\s*-?3", RegexOptions.IgnoreCase))
        {
            AddEffect(definition, CombatInteractionEffectKind.ModifySelectedCost, amount: -3d);
        }
        if (Regex.IsMatch(invocation, @"TotalExCost[^;\r\n]*\+\s*1", RegexOptions.IgnoreCase))
        {
            AddEffect(definition, CombatInteractionEffectKind.ModifySelectedPersistentCost, amount: 1d);
        }
        if (Regex.IsMatch(invocation, @"ExUseCount[^;\r\n]*\+\s*1", RegexOptions.IgnoreCase))
        {
            AddEffect(definition, CombatInteractionEffectKind.ModifySelectedExtraUses, amount: 1d);
        }
        if (ContainsAny(invocation, "AddCardById(", "AddCardByData("))
        {
            AddEffect(definition, CombatInteractionEffectKind.TransferSelectedCopy, amount: 1d);
        }

        var statusMatches = Regex.Matches(
            invocation,
            "AddBuff\\s*\\(\\s*\\\"(?<id>[^\\\"]+)\\\"\\s*,\\s*(?<amount>[^\\),;]+)",
            RegexOptions.IgnoreCase);
        foreach (Match match in statusMatches)
        {
            var id = match.Groups["id"].Value.Trim();
            var expression = match.Groups["amount"].Value.Trim();
            if (TryReadSelectionCountExpression(expression, out var baseAmount, out var perSelection))
            {
                AddEffect(
                    definition,
                    CombatInteractionEffectKind.AddStatusBySelectionCount,
                    id,
                    baseAmount,
                    baseAmount,
                    perSelection);
            }
            else if (TryReadNumber(expression, out var amount)
                     && ContainsAny(invocation, "foreach", "ForEach"))
            {
                AddEffect(
                    definition,
                    CombatInteractionEffectKind.AddStatusPerSelected,
                    id,
                    amount);
            }
        }
        var countVariable = Regex.Match(
            invocation,
            @"(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*[^;]*?\(\s*(?<base>\d+(?:\.\d+)?)\s*-\s*(?<per>\d+(?:\.\d+)?)\s*\*\s*[A-Za-z_][A-Za-z0-9_]*\.Count",
            RegexOptions.IgnoreCase);
        if (countVariable.Success
            && double.TryParse(
                countVariable.Groups["base"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var variableBase)
            && double.TryParse(
                countVariable.Groups["per"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var variablePer))
        {
            var variableName = Regex.Escape(countVariable.Groups["variable"].Value);
            var statusByVariable = Regex.Match(
                invocation,
                "AddBuff\\s*\\(\\s*\\\"(?<id>[^\\\"]+)\\\"\\s*,\\s*"
                + variableName,
                RegexOptions.IgnoreCase);
            if (statusByVariable.Success)
            {
                AddEffect(
                    definition,
                    CombatInteractionEffectKind.AddStatusBySelectionCount,
                    statusByVariable.Groups["id"].Value,
                    variableBase,
                    variableBase,
                    -Math.Abs(variablePer));
            }
        }
    }

    private static bool IsEffectComplete(CombatInteractionDefinition definition)
    {
        if (definition.SelectionEffects.Count > 0)
        {
            return definition.SelectionEffects.All(item =>
                item.Kind != CombatInteractionEffectKind.Unknown);
        }

        // Retrieval APIs have a useful, known selection destination even when
        // the callback is hidden inside the native UI implementation.
        return definition.Kind == CombatInteractionKind.RetrieveCards;
    }

    private static void AddEffect(
        CombatInteractionDefinition definition,
        CombatInteractionEffectKind kind,
        string definitionId = "",
        double amount = 0d,
        double baseAmount = 0d,
        double amountPerSelection = 0d)
    {
        if (definition.SelectionEffects.Any(item =>
                item.Kind == kind
                && string.Equals(item.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        definition.SelectionEffects.Add(new CombatInteractionEffectDefinition
        {
            Kind = kind,
            DefinitionId = definitionId ?? "",
            Amount = amount,
            BaseAmount = baseAmount,
            AmountPerSelection = amountPerSelection
        });
    }

    private static CombatInteractionKind KindOf(string api)
    {
        switch (api.ToLowerInvariant())
        {
            case "burncard": return CombatInteractionKind.BurnCards;
            case "throwcard": return CombatInteractionKind.DiscardCards;
            case "packtodeckaction": return CombatInteractionKind.RetrieveCards;
            case "copycardware": return CombatInteractionKind.CopyCards;
            case "outfightselectcardtoaction": return CombatInteractionKind.TransferCards;
            default: return CombatInteractionKind.ChooseCards;
        }
    }

    private static CombatInteractionZone ZoneOf(string api, string invocation)
    {
        if (string.Equals(api, "PackToDeckAction", StringComparison.OrdinalIgnoreCase))
        {
            if (invocation.IndexOf("UsedCard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CombatInteractionZone.DiscardPile;
            }
            return CombatInteractionZone.DrawPile;
        }
        if (string.Equals(api, "OutFightSelectCardToAction", StringComparison.OrdinalIgnoreCase))
        {
            return CombatInteractionZone.AdventureDeck;
        }
        if (string.Equals(api, "CopyCardWare", StringComparison.OrdinalIgnoreCase))
        {
            return CombatInteractionZone.Deck;
        }
        return CombatInteractionZone.Hand;
    }

    private static string ReadNativeMode(string api, IReadOnlyList<string> arguments)
    {
        var modeIndex = string.Equals(api, "ChooseCardToAction", StringComparison.OrdinalIgnoreCase)
            ? 2
            : string.Equals(api, "BurnCard", StringComparison.OrdinalIgnoreCase)
              || string.Equals(api, "ThrowCard", StringComparison.OrdinalIgnoreCase)
                ? 1
                : -1;
        if (modeIndex < 0 || arguments.Count <= modeIndex)
        {
            return "";
        }
        return arguments[modeIndex].Trim().Trim('"', '\'');
    }

    private static int ReadNonNegativeInteger(string value, int fallback)
    {
        var match = Regex.Match(value ?? "", @"-?\d+");
        return match.Success
               && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : fallback;
    }

    private static bool TryReadNumber(string value, out double result)
    {
        return double.TryParse(
            (value ?? "").Trim().Trim('"'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryReadSelectionCountExpression(
        string expression,
        out double baseAmount,
        out double amountPerSelection)
    {
        baseAmount = 0d;
        amountPerSelection = 0d;
        if (string.IsNullOrWhiteSpace(expression)
            || expression.IndexOf("Count", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }
        var values = Regex.Matches(expression, @"-?\d+(?:\.\d+)?")
            .Cast<Match>()
            .Select(item => double.Parse(item.Value, CultureInfo.InvariantCulture))
            .ToList();
        if (values.Count < 2)
        {
            return false;
        }
        baseAmount = values[0];
        amountPerSelection = expression.IndexOf("-", StringComparison.Ordinal) >= 0
            ? -Math.Abs(values[1])
            : Math.Abs(values[1]);
        return true;
    }

    private static bool TryReadInvocation(
        string script,
        string api,
        out string invocation,
        out List<string> arguments)
    {
        invocation = "";
        arguments = new List<string>();
        var searchFrom = 0;
        while (searchFrom < script.Length)
        {
            var nameIndex = script.IndexOf(api, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
            {
                return false;
            }
            var open = nameIndex + api.Length;
            while (open < script.Length && char.IsWhiteSpace(script[open])) open++;
            if (open >= script.Length || script[open] != '(')
            {
                searchFrom = nameIndex + api.Length;
                continue;
            }
            if (!TryFindMatchingParenthesis(script, open, out var close))
            {
                return false;
            }
            invocation = script.Substring(nameIndex, close - nameIndex + 1);
            arguments = SplitTopLevel(script.Substring(open + 1, close - open - 1));
            return true;
        }
        return false;
    }

    private static bool TryFindMatchingParenthesis(string text, int open, out int close)
    {
        close = -1;
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var i = open; i < text.Length; i++)
        {
            var current = text[i];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }
            if (current == '"' || current == '\'')
            {
                quote = current;
                continue;
            }
            if (current == '(') depth++;
            else if (current == ')' && --depth == 0)
            {
                close = i;
                return true;
            }
        }
        return false;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var result = new List<string>();
        var start = 0;
        var parens = 0;
        var braces = 0;
        var brackets = 0;
        var quote = '\0';
        var escaped = false;
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }
            if (current == '"' || current == '\'') quote = current;
            else if (current == '(') parens++;
            else if (current == ')') parens--;
            else if (current == '{') braces++;
            else if (current == '}') braces--;
            else if (current == '[') brackets++;
            else if (current == ']') brackets--;
            else if (current == ',' && parens == 0 && braces == 0 && brackets == 0)
            {
                result.Add(text.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        result.Add(text.Substring(start).Trim());
        return result;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle =>
            value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
