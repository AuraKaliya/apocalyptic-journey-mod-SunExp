using System;
using System.Collections.Generic;

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
}
