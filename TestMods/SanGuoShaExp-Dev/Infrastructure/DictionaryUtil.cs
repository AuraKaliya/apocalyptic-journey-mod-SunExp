using System;
using System.Collections.Generic;
using System.Globalization;

namespace SanGuoShaExp.Dll.Infrastructure;

public static class DictionaryUtil
{
    public static string Get(IDictionary<string, string>? values, string key, string fallback = "")
    {
        if (values == null || key == null)
        {
            return fallback;
        }

        return values.TryGetValue(key, out var value) && value != null ? value : fallback;
    }

    public static void Set(IDictionary<string, string>? values, string key, object value)
    {
        if (values == null || key == null)
        {
            return;
        }

        values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    public static int ParseInt(string? value, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }
}
