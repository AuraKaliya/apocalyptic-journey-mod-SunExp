using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AuraShared.Core;

public static class AuraSharedDictionary
{
    public static string Get(IDictionary<string, string>? values, string key, string fallback = "")
    {
        if (values == null || key == null)
        {
            return fallback;
        }

        return values.TryGetValue(key, out var value) && value != null ? value : fallback;
    }

    public static void Set(IDictionary<string, string>? values, string key, object? value)
    {
        if (values == null || key == null)
        {
            return;
        }

        values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    public static int GetInt(IDictionary<string, string>? values, string key, int fallback = 0)
    {
        return ParseInt(Get(values, key), fallback);
    }

    public static int ParseInt(string? value, int fallback = 0)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    public static bool ContainsToken(string? text, string token, StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return SplitTokens(text).Any(part => string.Equals(part, token, comparison));
    }

    public static string AppendToken(string? text, string token, StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return text ?? "";
        }

        var values = SplitTokens(text).ToList();
        if (!values.Any(part => string.Equals(part, token, comparison)))
        {
            values.Add(token.Trim());
        }

        return string.Join(",", values);
    }

    public static string RemoveToken(string? text, string token, StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return text ?? "";
        }

        return string.Join(",", SplitTokens(text).Where(part => !string.Equals(part, token, comparison)));
    }

    public static IEnumerable<string> SplitTokens(string? text)
    {
        return (text ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0);
    }
}
