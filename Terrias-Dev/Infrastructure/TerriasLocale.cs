using System;
using System.Collections.Generic;

namespace Terrias.Dll.Infrastructure;

public static class TerriasLocale
{
    public const string ZhHans = "zh-Hans";
    public const string ZhHant = "zh-Hant";
    public const string English = "en";
    public const string Japanese = "ja";

    public static readonly IReadOnlyList<string> Supported = new[]
    {
        ZhHans,
        ZhHant,
        English,
        Japanese
    };

    public static string Normalize(string? language)
    {
        var value = (language ?? "").Trim().Replace('_', '-');
        if (value.Length == 0)
        {
            return ZhHans;
        }

        if (value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ZhHans;
        }

        if (value.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase))
        {
            return ZhHant;
        }

        if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return English;
        }

        if (value.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("jp", StringComparison.OrdinalIgnoreCase))
        {
            return Japanese;
        }

        return ZhHans;
    }

    public static string FieldName(string baseField, string locale)
    {
        var field = (baseField ?? "").Trim();
        return Normalize(locale) switch
        {
            ZhHant => field + "_zh-Hant",
            English => field + "_en",
            Japanese => field + "_ja",
            _ => field
        };
    }
}
