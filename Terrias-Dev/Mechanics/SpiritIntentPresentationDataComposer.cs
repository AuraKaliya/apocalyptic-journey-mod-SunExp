using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Preserves the captured enemy-card presentation while forcing the runtime
/// identity and executable scripts through a Terrias-owned adapter row.
/// Native ScriptExecutor precompilation resolves delegates by data Id before
/// considering replaced script text, so retaining the source Id is unsafe.
/// </summary>
public static class SpiritIntentPresentationDataComposer
{
    private static readonly string[] AdapterFields =
    {
        "Id",
        "InitScript",
        "TargetScript",
        "UseScript"
    };

    public static Dictionary<string, string> Compose(
        IDictionary<string, string> source,
        IDictionary<string, string> adapter)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (adapter == null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        var composed = new Dictionary<string, string>(source, StringComparer.Ordinal);
        foreach (var field in AdapterFields)
        {
            if (!adapter.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Spirit intent adapter is missing required field: " + field);
            }

            composed[field] = value;
        }

        return composed;
    }

    public static Dictionary<string, string> PresentationOverrides(
        IDictionary<string, string> composed)
    {
        if (composed == null)
        {
            throw new ArgumentNullException(nameof(composed));
        }

        var overrides = new Dictionary<string, string>(composed, StringComparer.Ordinal);
        foreach (var field in AdapterFields)
        {
            overrides.Remove(field);
        }

        return overrides;
    }

    public static void AppendElementRule(IDictionary<string, string> presentation, string elementId)
    {
        if (presentation == null || SpiritElementService.NormalizeId(elementId).Length == 0)
        {
            return;
        }

        var arguments = new[] { "element", SpiritElementService.DisplayName(elementId) };
        Append(presentation, "Description", TerriasTextCatalog.Format("ui.spirit.element_segment_rule", arguments));
        foreach (var locale in TerriasLocale.Supported)
        {
            Append(
                presentation,
                TerriasLocale.FieldName("Description", locale),
                TerriasTextCatalog.FormatForLocale("ui.spirit.element_segment_rule", locale, arguments));
        }

        presentation[TerriasIds.SpiritElementIdKey] = SpiritElementService.NormalizeId(elementId);
    }

    private static void Append(IDictionary<string, string> presentation, string key, string line)
    {
        if (!presentation.TryGetValue(key, out var current) || string.IsNullOrWhiteSpace(current)
            || string.IsNullOrWhiteSpace(line) || current.IndexOf(line, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        presentation[key] = current.TrimEnd() + "\n" + line;
    }
}
