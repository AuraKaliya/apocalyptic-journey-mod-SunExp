using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public sealed class TerriasTextCatalogDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("entries")]
    public Dictionary<string, TerriasLocalizedText> Entries { get; set; } = new(StringComparer.Ordinal);
}

public static class TerriasTextCatalog
{
    private const int SupportedSchemaVersion = 1;
    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<string, TerriasLocalizedText> entries =
        new Dictionary<string, TerriasLocalizedText>(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string> aliases =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static void Load(ModConfig modConfig)
    {
        var path = Path.Combine(modConfig.DirectoryName, TerriasIds.LocalizationRegistryFile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Terrias localization registry is missing.", path);
        }

        var document = JsonConvert.DeserializeObject<TerriasTextCatalogDocument>(File.ReadAllText(path))
                       ?? new TerriasTextCatalogDocument();
        if (document.SchemaVersion <= 0 || document.SchemaVersion > SupportedSchemaVersion)
        {
            throw new InvalidDataException("Unsupported Terrias localization schemaVersion=" + document.SchemaVersion);
        }

        var normalized = new Dictionary<string, TerriasLocalizedText>(StringComparer.Ordinal);
        foreach (var pair in document.Entries ?? new Dictionary<string, TerriasLocalizedText>())
        {
            var key = (pair.Key ?? "").Trim();
            if (key.Length == 0 || pair.Value == null)
            {
                continue;
            }

            normalized[key] = pair.Value;
        }

        var aliasIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in normalized.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var locale in TerriasLocale.Supported)
            {
                var value = pair.Value.Exact(locale);
                if (value.Length > 0 && !aliasIndex.ContainsKey(value))
                {
                    aliasIndex[value] = pair.Key;
                }
            }
        }

        lock (SyncRoot)
        {
            entries = normalized;
            aliases = aliasIndex;
        }

        TerriasLog.Info("[Localization] registry loaded: entries=" + normalized.Count);
    }

    public static string Get(string key, IReadOnlyDictionary<string, string>? arguments = null)
    {
        return GetForLocale(key, TerriasLanguageApi.CurrentLocale, arguments);
    }

    public static string Format(string key, params string[] argumentPairs)
    {
        return Get(key, ToArguments(argumentPairs));
    }

    public static string FormatForLocale(string key, string locale, params string[] argumentPairs)
    {
        return GetForLocale(key, locale, ToArguments(argumentPairs));
    }

    public static string GetForLocale(
        string key,
        string locale,
        IReadOnlyDictionary<string, string>? arguments = null)
    {
        var normalizedKey = (key ?? "").Trim();
        TerriasLocalizedText? text;
        lock (SyncRoot)
        {
            entries.TryGetValue(normalizedKey, out text);
        }

        if (text == null)
        {
            if (normalizedKey.Length > 0)
            {
                TerriasLog.WarnOnce("Localization.MissingKey." + normalizedKey,
                    "[Localization] missing text key: " + normalizedKey);
            }

            return ApplyArguments(normalizedKey, arguments);
        }

        if (!text.HasExact(locale))
        {
            TerriasLog.WarnOnce(
                "Localization.MissingLocale." + normalizedKey + "." + TerriasLocale.Normalize(locale),
                "[Localization] missing locale=" + TerriasLocale.Normalize(locale) + " for key=" + normalizedKey);
        }

        return ApplyArguments(text.Resolve(locale, normalizedKey), arguments);
    }

    public static string ResolveLegacy(string source)
    {
        return TryResolveLegacy(source, out var localized) ? localized : source ?? "";
    }

    public static bool TryResolveLegacy(string source, out string localized)
    {
        var value = source ?? "";
        string? key;
        lock (SyncRoot)
        {
            aliases.TryGetValue(value, out key);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            localized = value;
            return false;
        }

        localized = Get(key);
        return true;
    }

    public static bool Contains(string key)
    {
        lock (SyncRoot)
        {
            return entries.ContainsKey((key ?? "").Trim());
        }
    }

    private static string ApplyArguments(string template, IReadOnlyDictionary<string, string>? arguments)
    {
        var result = template ?? "";
        if (arguments == null)
        {
            return result;
        }

        foreach (var pair in arguments)
        {
            var name = (pair.Key ?? "").Trim();
            if (name.Length > 0)
            {
                result = result.Replace("{" + name + "}", pair.Value ?? "");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ToArguments(IReadOnlyList<string>? argumentPairs)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        if (argumentPairs == null)
        {
            return arguments;
        }

        for (var index = 0; index + 1 < argumentPairs.Count; index += 2)
        {
            var name = (argumentPairs[index] ?? "").Trim();
            if (name.Length > 0)
            {
                arguments[name] = argumentPairs[index + 1] ?? "";
            }
        }

        return arguments;
    }
}
