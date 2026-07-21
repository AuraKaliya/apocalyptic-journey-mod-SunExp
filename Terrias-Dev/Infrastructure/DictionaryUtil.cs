using System.Collections.Generic;
using AuraShared.Core;

namespace Terrias.Dll.Infrastructure;

public static class DictionaryUtil
{
    public static string Get(IDictionary<string, string>? values, string key, string fallback = "")
    {
        return AuraSharedDictionary.Get(values, key, fallback);
    }

    public static void Set(IDictionary<string, string>? values, string key, string value)
    {
        AuraSharedDictionary.Set(values, key, value);
    }

    public static int GetInt(IDictionary<string, string>? values, string key, int fallback = 0)
    {
        return AuraSharedDictionary.GetInt(values, key, fallback);
    }

    public static int ParseInt(string? value, int fallback = 0)
    {
        return AuraSharedDictionary.ParseInt(value, fallback);
    }

    public static bool ContainsToken(string? text, string token)
    {
        return AuraSharedDictionary.ContainsToken(text, token);
    }
}
