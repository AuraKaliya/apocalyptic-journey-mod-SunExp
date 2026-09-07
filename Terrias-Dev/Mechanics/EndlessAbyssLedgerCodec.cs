using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class EndlessAbyssLedgerDocument
{
    public List<string> Entries { get; set; } = new();
}

public static class EndlessAbyssLedgerCodec
{
    public static bool TryCommitClaim(string key, Func<string> read, Action<string> write, int capacity)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return false;
        var document = Read(read());
        if (document.Entries.Contains(key, StringComparer.Ordinal)) return false;
        document.Entries.Add(key);
        if (document.Entries.Count > capacity)
            document.Entries = document.Entries.Skip(document.Entries.Count - capacity).ToList();
        write(JsonConvert.SerializeObject(document));
        return true;
    }

    public static EndlessAbyssLedgerDocument Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EndlessAbyssLedgerDocument();
        try
        {
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var properties = value.Properties().ToList();
            if (properties.Count != 1 || !string.Equals(properties[0].Name, "Entries", StringComparison.OrdinalIgnoreCase)
                || properties[0].Value is not JArray entries
                || entries.Any(item => item.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)item)))
                throw new InvalidDataException("Unsupported or damaged abyss claim ledger.");
            return new EndlessAbyssLedgerDocument { Entries = entries.Select(item => ((string)item!).Trim()).Distinct(StringComparer.Ordinal).ToList() };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Damaged abyss claim ledger; existing claims were not reset.", ex);
        }
    }
}
