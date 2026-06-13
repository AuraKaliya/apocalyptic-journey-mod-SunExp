using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SunExp.Dll.Infrastructure;

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

    public static void Set(IDictionary<string, string>? values, string key, string value)
    {
        if (values == null || key == null)
        {
            return;
        }

        values[key] = value;
    }

    public static int GetInt(IDictionary<string, string>? values, string key, int fallback = 0)
    {
        return ParseInt(Get(values, key), fallback);
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

    public static bool ContainsToken(string? text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var source = text ?? "";
        return source.Split(',')
            .Select(part => part.Trim())
            .Any(part => string.Equals(part, token, StringComparison.Ordinal));
    }
}
