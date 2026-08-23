using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal static class ReplayCardPresentationContractV11
{
    internal const string TagKey = "Tag";
    internal const string RarityKey = "Rarity";
    internal const string IconKey = "Icon";
    internal const string MissingTagErrorPrefix = "replay card presentation has no explicit Tag field: ";

    internal static void SetValue(
        ICollection<ReplayStringValueV11> values,
        string key,
        string? value,
        bool preserveEmpty)
    {
        if (values == null || string.IsNullOrWhiteSpace(key)) return;
        if (!preserveEmpty && string.IsNullOrWhiteSpace(value)) return;
        if (values is List<ReplayStringValueV11> list)
            list.RemoveAll(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        else
        {
            foreach (var existing in values
                         .Where(item => string.Equals(item.Key, key, StringComparison.Ordinal))
                         .ToList())
                values.Remove(existing);
        }
        values.Add(new ReplayStringValueV11 { Key = key, Value = value ?? "" });
    }

    internal static int NormalizeDocument(ReplayDocumentV11 document)
    {
        if (document == null) return 0;
        var changed = 0;
        foreach (var card in CardSnapshots(document))
        {
            card.Values ??= new List<ReplayStringValueV11>();
            if (card.Values.Any(value => string.Equals(value.Key, TagKey, StringComparison.Ordinal))) continue;
            card.Values.Add(new ReplayStringValueV11 { Key = TagKey, Value = "" });
            changed++;
        }
        return changed;
    }

    internal static List<string> ValidateDocument(ReplayDocumentV11 document)
    {
        var errors = new List<string>();
        if (document == null)
        {
            errors.Add("replay card presentation document is missing");
            return errors;
        }

        var definitions = (document.Content?.Definitions ?? new List<ReplayContentDefinitionV11>())
            .Where(value => value?.Content != null)
            .GroupBy(value => value.Content.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (var card in CardSnapshots(document))
        {
            var identity = card.Content?.StableContentId ?? card.InstanceId ?? "<missing>";
            var values = Values(card.Values);
            if (!values.ContainsKey(TagKey))
                errors.Add(MissingTagErrorPrefix + identity);

            definitions.TryGetValue(card.Content?.Key ?? "", out var definition);
            var display = Values(definition?.Display?.Values);
            if (!HasNonEmpty(values, display, RarityKey))
                errors.Add("replay card presentation has no rarity: " + identity);
            if (!HasNonEmpty(values, display, IconKey))
                errors.Add("replay card presentation has no icon: " + identity);
        }
        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<ReplayCardStateV11> CardSnapshots(ReplayDocumentV11 document)
    {
        foreach (var card in document.InitialState?.Cards ?? new List<ReplayCardStateV11>())
            if (card != null) yield return card;
        foreach (var card in (document.Events ?? new List<ReplayTimelineEventV11>())
                     .Where(value => value?.Delta != null)
                     .SelectMany(value => value.Delta!.CardUpserts ?? new List<ReplayCardStateV11>()))
            if (card != null) yield return card;
    }

    private static Dictionary<string, string> Values(IEnumerable<ReplayStringValueV11>? values)
    {
        return (values ?? Enumerable.Empty<ReplayStringValueV11>())
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Key))
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? "", StringComparer.Ordinal);
    }

    private static bool HasNonEmpty(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> display,
        string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
               || display.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
    }
}
