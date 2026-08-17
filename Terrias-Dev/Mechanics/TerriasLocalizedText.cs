using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class TerriasLocalizedText
{
    [JsonProperty("zh-Hans")]
    public string ZhHans { get; set; } = "";

    [JsonProperty("zh-Hant")]
    public string ZhHant { get; set; } = "";

    [JsonProperty("en")]
    public string English { get; set; } = "";

    [JsonProperty("ja")]
    public string Japanese { get; set; } = "";

    [JsonProperty("legacyFallback", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string LegacyFallback { get; set; } = "";

    public string Resolve(string locale, string fallback = "")
    {
        var normalized = TerriasLocale.Normalize(locale);
        var exact = Exact(normalized);
        if (exact.Length > 0)
        {
            return exact;
        }

        foreach (var candidate in FallbackOrder(normalized))
        {
            var value = Exact(candidate);
            if (value.Length > 0)
            {
                return value;
            }
        }

        return FirstNonEmpty(LegacyFallback, fallback);
    }

    public string Exact(string locale)
    {
        return TerriasLocale.Normalize(locale) switch
        {
            TerriasLocale.ZhHant => Clean(ZhHant),
            TerriasLocale.English => Clean(English),
            TerriasLocale.Japanese => Clean(Japanese),
            _ => Clean(ZhHans)
        };
    }

    public bool HasExact(string locale) => Exact(locale).Length > 0;

    public TerriasLocalizedText Clone()
    {
        return new TerriasLocalizedText
        {
            ZhHans = ZhHans,
            ZhHant = ZhHant,
            English = English,
            Japanese = Japanese,
            LegacyFallback = LegacyFallback
        };
    }

    public static TerriasLocalizedText FromRow(
        IReadOnlyDictionary<string, string>? row,
        string field,
        string legacyFallback = "")
    {
        return new TerriasLocalizedText
        {
            ZhHans = Value(row, field),
            ZhHant = Value(row, field + "_zh-Hant"),
            English = Value(row, field + "_en"),
            Japanese = Value(row, field + "_ja"),
            LegacyFallback = Clean(legacyFallback)
        };
    }

    private static IEnumerable<string> FallbackOrder(string locale)
    {
        if (locale == TerriasLocale.ZhHant)
        {
            yield return TerriasLocale.ZhHans;
            yield return TerriasLocale.English;
            yield return TerriasLocale.Japanese;
            yield break;
        }

        yield return TerriasLocale.ZhHans;
        if (locale != TerriasLocale.English)
        {
            yield return TerriasLocale.English;
        }

        if (locale != TerriasLocale.ZhHant)
        {
            yield return TerriasLocale.ZhHant;
        }

        if (locale != TerriasLocale.Japanese)
        {
            yield return TerriasLocale.Japanese;
        }
    }

    private static string Value(IReadOnlyDictionary<string, string>? row, string key)
    {
        return row != null && row.TryGetValue(key, out var value) ? Clean(value) : "";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            var cleaned = Clean(value);
            if (cleaned.Length > 0)
            {
                return cleaned;
            }
        }

        return "";
    }

    private static string Clean(string? value) => value?.Trim() ?? "";
}
