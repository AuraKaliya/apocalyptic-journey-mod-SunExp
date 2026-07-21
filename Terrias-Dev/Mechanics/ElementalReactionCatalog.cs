using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public enum ElementalType
{
    None = 0,
    Pyro,
    Electro,
    Cryo,
    Hydro,
    Dendro,
    Anemo,
    Geo
}

public enum ElementalAttachmentType
{
    Pyro,
    Electro,
    Cryo,
    Hydro,
    Dendro,
    DendroCore,
    Frozen
}

public enum ElementalReactionType
{
    None = 0,
    Melt,
    Vaporize,
    Overloaded,
    Superconduct,
    ElectroCharged,
    Freeze,
    Swirl,
    Crystallize,
    Burning,
    Bloom,
    Quicken,
    Burgeon,
    Hyperbloom
}

public sealed class ElementalAttachmentDefinition
{
    public ElementalAttachmentDefinition(
        ElementalAttachmentType attachment,
        string buffId,
        int priority,
        int upperBound,
        ElementalType element = ElementalType.None)
    {
        Attachment = attachment;
        BuffId = buffId ?? "";
        Priority = priority;
        UpperBound = upperBound;
        Element = element;
    }

    public ElementalAttachmentType Attachment { get; }

    public string BuffId { get; }

    public int Priority { get; }

    public int UpperBound { get; }

    public ElementalType Element { get; }
}

public sealed class ElementalReactionDefinition
{
    public ElementalReactionDefinition(
        ElementalAttachmentType existing,
        ElementalType incoming,
        ElementalReactionType reaction,
        string displayName)
    {
        Existing = existing;
        Incoming = incoming;
        Reaction = reaction;
        DisplayName = displayName ?? reaction.ToString();
    }

    public ElementalAttachmentType Existing { get; }

    public ElementalType Incoming { get; }

    public ElementalReactionType Reaction { get; }

    public string DisplayName { get; }
}

public static class ElementalAttachmentRegistry
{
    private static readonly IReadOnlyList<ElementalAttachmentDefinition> Definitions = new[]
    {
        new ElementalAttachmentDefinition(ElementalAttachmentType.Pyro, TerriasIds.PyroAttachment, 700, 1, ElementalType.Pyro),
        new ElementalAttachmentDefinition(ElementalAttachmentType.Electro, TerriasIds.ElectroAttachment, 600, 1, ElementalType.Electro),
        new ElementalAttachmentDefinition(ElementalAttachmentType.Cryo, TerriasIds.CryoAttachment, 500, 1, ElementalType.Cryo),
        new ElementalAttachmentDefinition(ElementalAttachmentType.Hydro, TerriasIds.HydroAttachment, 400, 1, ElementalType.Hydro),
        new ElementalAttachmentDefinition(ElementalAttachmentType.Dendro, TerriasIds.DendroAttachment, 300, 1, ElementalType.Dendro),
        new ElementalAttachmentDefinition(ElementalAttachmentType.DendroCore, TerriasIds.DendroCore, 200, 5),
        new ElementalAttachmentDefinition(ElementalAttachmentType.Frozen, TerriasIds.Frozen, 100, 1)
    };

    private static readonly Dictionary<ElementalAttachmentType, ElementalAttachmentDefinition> ByAttachment =
        Definitions.ToDictionary(definition => definition.Attachment);

    private static readonly Dictionary<ElementalType, ElementalAttachmentDefinition> ByElement =
        Definitions
            .Where(definition => definition.Element != ElementalType.None)
            .ToDictionary(definition => definition.Element);

    public static IReadOnlyList<ElementalAttachmentDefinition> PriorityOrder => Definitions;

    public static ElementalAttachmentDefinition Definition(ElementalAttachmentType attachment)
    {
        return ByAttachment[attachment];
    }

    public static bool TryForElement(ElementalType element, out ElementalAttachmentDefinition definition)
    {
        return ByElement.TryGetValue(element, out definition!);
    }
}

public static class ElementalReactionRegistry
{
    private static readonly Dictionary<(ElementalAttachmentType Existing, ElementalType Incoming), ElementalReactionDefinition> Definitions =
        BuildDefinitions();

    public static bool TryResolve(
        IEnumerable<ElementalAttachmentType>? existingAttachments,
        ElementalType incoming,
        out ElementalReactionDefinition definition)
    {
        var available = new HashSet<ElementalAttachmentType>(existingAttachments ?? Array.Empty<ElementalAttachmentType>());
        foreach (var attachment in ElementalAttachmentRegistry.PriorityOrder)
        {
            if (available.Contains(attachment.Attachment)
                && Definitions.TryGetValue((attachment.Attachment, incoming), out definition!))
            {
                return true;
            }
        }

        definition = null!;
        return false;
    }

    public static bool TryResolve(
        ElementalAttachmentType existing,
        ElementalType incoming,
        out ElementalReactionDefinition definition)
    {
        return Definitions.TryGetValue((existing, incoming), out definition!);
    }

    private static Dictionary<(ElementalAttachmentType, ElementalType), ElementalReactionDefinition> BuildDefinitions()
    {
        var result = new Dictionary<(ElementalAttachmentType, ElementalType), ElementalReactionDefinition>();

        AddPair(result, ElementalAttachmentType.Cryo, ElementalType.Pyro, ElementalAttachmentType.Pyro, ElementalType.Cryo, ElementalReactionType.Melt, "融化");
        AddPair(result, ElementalAttachmentType.Hydro, ElementalType.Pyro, ElementalAttachmentType.Pyro, ElementalType.Hydro, ElementalReactionType.Vaporize, "蒸发");
        AddPair(result, ElementalAttachmentType.Electro, ElementalType.Pyro, ElementalAttachmentType.Pyro, ElementalType.Electro, ElementalReactionType.Overloaded, "超载");
        AddPair(result, ElementalAttachmentType.Electro, ElementalType.Cryo, ElementalAttachmentType.Cryo, ElementalType.Electro, ElementalReactionType.Superconduct, "超导");
        AddPair(result, ElementalAttachmentType.Electro, ElementalType.Hydro, ElementalAttachmentType.Hydro, ElementalType.Electro, ElementalReactionType.ElectroCharged, "感电");
        AddPair(result, ElementalAttachmentType.Cryo, ElementalType.Hydro, ElementalAttachmentType.Hydro, ElementalType.Cryo, ElementalReactionType.Freeze, "冻结");
        AddPair(result, ElementalAttachmentType.Dendro, ElementalType.Pyro, ElementalAttachmentType.Pyro, ElementalType.Dendro, ElementalReactionType.Burning, "燃烧");
        AddPair(result, ElementalAttachmentType.Dendro, ElementalType.Hydro, ElementalAttachmentType.Hydro, ElementalType.Dendro, ElementalReactionType.Bloom, "绽放");
        AddPair(result, ElementalAttachmentType.Dendro, ElementalType.Electro, ElementalAttachmentType.Electro, ElementalType.Dendro, ElementalReactionType.Quicken, "激化");

        Add(result, ElementalAttachmentType.Frozen, ElementalType.Pyro, ElementalReactionType.Melt, "融化");
        Add(result, ElementalAttachmentType.Frozen, ElementalType.Electro, ElementalReactionType.Superconduct, "超导");
        Add(result, ElementalAttachmentType.DendroCore, ElementalType.Pyro, ElementalReactionType.Burgeon, "烈绽放");
        Add(result, ElementalAttachmentType.DendroCore, ElementalType.Electro, ElementalReactionType.Hyperbloom, "超绽放");

        foreach (var attachment in new[]
                 {
                     ElementalAttachmentType.Pyro,
                     ElementalAttachmentType.Electro,
                     ElementalAttachmentType.Cryo,
                     ElementalAttachmentType.Hydro
                 })
        {
            Add(result, attachment, ElementalType.Anemo, ElementalReactionType.Swirl, "扩散");
            Add(result, attachment, ElementalType.Geo, ElementalReactionType.Crystallize, "结晶");
        }

        return result;
    }

    private static void AddPair(
        IDictionary<(ElementalAttachmentType, ElementalType), ElementalReactionDefinition> definitions,
        ElementalAttachmentType firstExisting,
        ElementalType firstIncoming,
        ElementalAttachmentType secondExisting,
        ElementalType secondIncoming,
        ElementalReactionType reaction,
        string displayName)
    {
        Add(definitions, firstExisting, firstIncoming, reaction, displayName);
        Add(definitions, secondExisting, secondIncoming, reaction, displayName);
    }

    private static void Add(
        IDictionary<(ElementalAttachmentType, ElementalType), ElementalReactionDefinition> definitions,
        ElementalAttachmentType existing,
        ElementalType incoming,
        ElementalReactionType reaction,
        string displayName)
    {
        definitions[(existing, incoming)] = new ElementalReactionDefinition(existing, incoming, reaction, displayName);
    }
}

public static class ElementalTypeParser
{
    public static bool TryParse(string? value, out ElementalType element)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "pyro":
            case "fire":
            case "火":
                element = ElementalType.Pyro;
                return true;
            case "electro":
            case "lightning":
            case "雷":
                element = ElementalType.Electro;
                return true;
            case "cryo":
            case "ice":
            case "冰":
                element = ElementalType.Cryo;
                return true;
            case "hydro":
            case "water":
            case "水":
                element = ElementalType.Hydro;
                return true;
            case "dendro":
            case "grass":
            case "草":
                element = ElementalType.Dendro;
                return true;
            case "anemo":
            case "wind":
            case "风":
                element = ElementalType.Anemo;
                return true;
            case "geo":
            case "rock":
            case "岩":
                element = ElementalType.Geo;
                return true;
            default:
                element = ElementalType.None;
                return false;
        }
    }
}
