using System;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedJson
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };
    private static readonly JsonSerializerSettings CompactSerializerSettings = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
        FloatFormatHandling = FloatFormatHandling.DefaultValue
    };

    public static T? Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json);
    }

    public static object? Deserialize(string json, Type type)
    {
        return JsonConvert.DeserializeObject(json, type);
    }

    public static string Serialize(object? value)
    {
        return JsonConvert.SerializeObject(value, SerializerSettings);
    }

    public static string SerializeCompact(object? value)
    {
        return JsonConvert.SerializeObject(value, CompactSerializerSettings);
    }
}
