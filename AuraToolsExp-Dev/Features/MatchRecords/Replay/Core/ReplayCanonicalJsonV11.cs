using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal static class ReplayCanonicalJsonV11
{
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        FloatFormatHandling = FloatFormatHandling.String,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Include
    });

    internal static byte[] SerializeUtf8(object value)
    {
        var token = value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
        var normalized = Normalize(token);
        return Encoding.UTF8.GetBytes(normalized.ToString(Formatting.None));
    }

    internal static string Sha256(object value)
    {
        return Sha256(SerializeUtf8(value));
    }

    internal static string Sha256(byte[] bytes)
    {
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(bytes ?? Array.Empty<byte>())
            .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }

    internal static string Sha256Text(string value)
    {
        return Sha256(Encoding.UTF8.GetBytes(value ?? ""));
    }

    internal static string EventChainHash(string previousHash, ReplayTimelineEventV11 value)
    {
        var token = JObject.FromObject(value, Serializer);
        token[nameof(ReplayTimelineEventV11.EventChainHashAfter)] = "";
        var payloadHash = Sha256(Normalize(token).ToString(Formatting.None));
        return Sha256Text((previousHash ?? "") + "\n" + payloadHash);
    }

    internal static string DocumentHash(ReplayDocumentV11 document)
    {
        var token = JObject.FromObject(document, Serializer);
        if (token[nameof(ReplayDocumentV11.Header)] is JObject header)
        {
            header[nameof(ReplayDocumentHeaderV11.DocumentSha256)] = "";
        }

        return Sha256(Encoding.UTF8.GetBytes(Normalize(token).ToString(Formatting.None)));
    }

    private static JToken Normalize(JToken token)
    {
        if (token is JObject sourceObject)
        {
            var result = new JObject();
            foreach (var property in sourceObject.Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                result.Add(property.Name, Normalize(property.Value));
            }

            return result;
        }

        if (token is JArray sourceArray)
        {
            var result = new JArray();
            foreach (var item in sourceArray)
            {
                result.Add(Normalize(item));
            }

            return result;
        }

        return token.DeepClone();
    }
}
