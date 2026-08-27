using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal static class ReplayCanonicalJsonV12
{
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        FloatFormatHandling = FloatFormatHandling.String,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Include
    });
    private static readonly JsonSerializerSettings StrictReadSettings = new()
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        MissingMemberHandling = MissingMemberHandling.Error,
        MaxDepth = 128
    };

    internal static byte[] SerializeUtf8(object value)
    {
        var token = value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
        var normalized = Normalize(token);
        return Encoding.UTF8.GetBytes(normalized.ToString(Formatting.None));
    }

    internal static T Clone<T>(T value) where T : class, new()
    {
        if (value == null) return new T();
        var json = Encoding.UTF8.GetString(SerializeUtf8(value));
        return JsonConvert.DeserializeObject<T>(json) ?? new T();
    }

    internal static T DeserializeStrict<T>(string json)
    {
        using var text = new StringReader(json ?? "");
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 128
        };
        var token = JToken.Load(reader, new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            LineInfoHandling = LineInfoHandling.Ignore
        });
        if (reader.Read()) throw new JsonSerializationException("Replay v12 JSON contains trailing content.");
        return token.ToObject<T>(JsonSerializer.Create(StrictReadSettings))
               ?? throw new JsonSerializationException("Replay v12 JSON payload is empty.");
    }

    internal static ReplayAssetV12 CloneAssetWithPayload(ReplayAssetV12 value)
    {
        var clone = Clone(value ?? new ReplayAssetV12());
        clone.Payload = value?.Payload == null
            ? Array.Empty<byte>()
            : (byte[])value.Payload.Clone();
        return clone;
    }

    internal static string Sha256(object value) => Sha256(SerializeUtf8(value));

    internal static string Sha256Text(string value) => Sha256(Encoding.UTF8.GetBytes(value ?? ""));

    internal static string Sha256(byte[] value)
    {
        using var hash = SHA256.Create();
        return string.Concat(hash.ComputeHash(value ?? Array.Empty<byte>()).Select(item => item.ToString("x2")));
    }

    internal static string StateHash(ReplayPublicStateV12 state)
    {
        return Sha256(ReplayStateReducerV12.Normalize(state));
    }

    internal static string EventHash(ReplayJournalEventV12 value)
    {
        var clone = Clone(value);
        clone.EventHash = "";
        return Sha256(clone);
    }

    internal static string TruthCheckpointHash(ReplayTruthCheckpointV12 value)
    {
        var clone = Clone(value);
        clone.CheckpointSha256 = "";
        clone.State = ReplayStateReducerV12.Normalize(clone.State);
        return Sha256(clone);
    }

    internal static string PresentationCheckpointHash(ReplayPresentationCheckpointV12 value)
    {
        var clone = Clone(value);
        clone.CheckpointSha256 = "";
        clone.EntityBindings = clone.EntityBindings
            .OrderBy(item => item.EntityId, StringComparer.Ordinal)
            .ThenBy(item => item.SpawnGeneration)
            .ToList();
        clone.EntityViews = clone.EntityViews
            .OrderBy(item => item.EntityId, StringComparer.Ordinal)
            .ThenBy(item => item.SpawnGeneration)
            .ToList();
        return Sha256(clone);
    }

    internal static string TruthRoot(ReplayDocumentV12 document)
    {
        return Sha256(new ReplayTruthRootPayload
        {
            EventHashes = (document?.TruthEvents ?? new List<ReplayJournalEventV12>())
                .OrderBy(item => item.Sequence)
                .Select(item => item.EventHash ?? "")
                .ToList(),
            CheckpointHashes = (document?.TruthCheckpoints ?? new List<ReplayTruthCheckpointV12>())
                .OrderBy(item => item.EventSequence)
                .Select(item => item.CheckpointSha256 ?? "")
                .ToList()
        });
    }

    internal static string PresentationRoot(ReplayDocumentV12 document)
    {
        return Sha256(new ReplayPresentationRootPayload
        {
            EventHashes = (document?.PresentationEvents ?? new List<ReplayJournalEventV12>())
                .OrderBy(item => item.Sequence)
                .Select(item => item.EventHash ?? "")
                .ToList(),
            CheckpointHashes = (document?.PresentationCheckpoints ?? new List<ReplayPresentationCheckpointV12>())
                .OrderBy(item => item.EventSequence)
                .Select(item => item.CheckpointSha256 ?? "")
                .ToList(),
            Presentation = NormalizePresentation(document?.Presentation),
            Assets = (document?.Assets ?? new List<ReplayAssetV12>())
                .OrderBy(item => item.Sha256, StringComparer.Ordinal)
                .Select(item =>
                {
                    var manifest = Clone(item);
                    manifest.Payload = Array.Empty<byte>();
                    return manifest;
                })
                .ToList()
        });
    }

    internal static string DocumentRoot(ReplayDocumentHeaderCoreV12 header)
    {
        return Sha256(header ?? new ReplayDocumentHeaderCoreV12());
    }

    internal static ReplayPresentationCapsuleV12 NormalizePresentation(ReplayPresentationCapsuleV12? source)
    {
        var value = Clone(source ?? new ReplayPresentationCapsuleV12());
        value.Entities = value.Entities.OrderBy(item => item.DescriptorId, StringComparer.Ordinal).ToList();
        foreach (var entity in value.Entities)
        {
            entity.Animations = entity.Animations.OrderBy(item => item.State, StringComparer.Ordinal).ToList();
        }
        value.Cards = value.Cards.OrderBy(item => item.DescriptorId, StringComparer.Ordinal).ToList();
        value.Buffs = value.Buffs.OrderBy(item => item.DescriptorId, StringComparer.Ordinal).ToList();
        value.Intents = value.Intents.OrderBy(item => item.DescriptorId, StringComparer.Ordinal).ToList();
        value.Effects = value.Effects.OrderBy(item => item.DescriptorId, StringComparer.Ordinal).ToList();
        value.Scene.Anchors = value.Scene.Anchors.OrderBy(item => item.AnchorId, StringComparer.Ordinal).ToList();
        return value;
    }

    private static JToken Normalize(JToken token)
    {
        if (token is JObject obj)
        {
            var result = new JObject();
            foreach (var property in obj.Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                result.Add(property.Name, Normalize(property.Value));
            }
            return result;
        }

        if (token is JArray array)
        {
            return new JArray(array.Select(Normalize));
        }

        return token.DeepClone();
    }

    private sealed class ReplayTruthRootPayload
    {
        public List<string> EventHashes { get; set; } = new();

        public List<string> CheckpointHashes { get; set; } = new();
    }

    private sealed class ReplayPresentationRootPayload
    {
        public List<string> EventHashes { get; set; } = new();

        public List<string> CheckpointHashes { get; set; } = new();

        public ReplayPresentationCapsuleV12 Presentation { get; set; } = new();

        public List<ReplayAssetV12> Assets { get; set; } = new();
    }
}
