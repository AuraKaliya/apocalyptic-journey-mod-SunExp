using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Hooks.Ui;

public static class TerriasTransientUiRegistry
{
    private static readonly Dictionary<string, Action<string>> Closers = new(StringComparer.Ordinal);

    public static void Register(string key, Action<string> close)
    {
        if (string.IsNullOrWhiteSpace(key) || close == null)
        {
            return;
        }

        Closers[key.Trim()] = close;
    }

    public static void Unregister(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            Closers.Remove(key.Trim());
        }
    }

    public static void CloseAll(string source)
    {
        var entries = Closers.ToList();
        if (entries.Count == 0)
        {
            return;
        }

        TerriasLog.Debug("[TerriasUiLifecycle] closing transient UI count=" + entries.Count + " from " + source + ".");
        foreach (var entry in entries)
        {
            try
            {
                entry.Value(source + ":" + entry.Key);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[TerriasUiLifecycle] close failed: key=" + entry.Key + ", error=" + ex.Message);
            }
        }
    }
}
