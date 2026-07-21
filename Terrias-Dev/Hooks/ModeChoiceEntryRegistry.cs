using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Hooks;

public static class ModeChoiceEntryRegistry
{
    private static readonly Dictionary<string, ModeChoiceEntryDefinition> EntriesByName = new(StringComparer.Ordinal);

    public static void Register(ModeChoiceEntryDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ObjectName))
        {
            return;
        }

        EntriesByName[definition.ObjectName] = definition;
    }

    public static IReadOnlyList<ModeChoiceEntryDefinition> Entries()
    {
        return EntriesByName.Values
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.ObjectName, StringComparer.Ordinal)
            .ToList();
    }
}
