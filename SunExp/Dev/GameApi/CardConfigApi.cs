using System;
using System.Reflection;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class CardConfigApi
{
    public static IDataConfig? FromActionPayload(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is IDataConfig dataConfig)
        {
            return dataConfig;
        }

        foreach (var name in new[] { "dataConfig", "DataConfig", "Data", "data", "Config", "config", "Source", "source" })
        {
            var value = ReadMember(payload, name);
            if (value is IDataConfig config)
            {
                return config;
            }
        }

        return null;
    }

    public static string Id(IDataConfig? config)
    {
        return DictionaryUtil.Get(config?.data, "Id", "unknown");
    }

    public static int CurrentCost(IDataConfig? config)
    {
        if (config == null)
        {
            return 0;
        }

        var total = DictionaryUtil.GetInt(config.data, "Expend")
            + DictionaryUtil.GetInt(config.Vars, "ExCost")
            + DictionaryUtil.GetInt(config.Vars, "OnceExCost")
            + DictionaryUtil.GetInt(config.Vars, "TotalExCost");
        return Math.Max(0, total);
    }

    public static int BaseCost(IDataConfig? config)
    {
        return config == null ? 0 : Math.Max(0, DictionaryUtil.GetInt(config.data, "Expend"));
    }

    public static int ResolveSolarTriggerCost(IDataConfig? config, int fallback)
    {
        if (config == null)
        {
            return Math.Max(0, fallback);
        }

        var overrideText = DictionaryUtil.Get(config.Vars, SunExpIds.SolarTriggerCost);
        if (!string.IsNullOrWhiteSpace(overrideText))
        {
            return Math.Max(0, DictionaryUtil.ParseInt(overrideText));
        }

        return Math.Max(0, fallback);
    }

    public static void ClearSolarTriggerCost(IDataConfig? config)
    {
        DictionaryUtil.Set(config?.Vars, SunExpIds.SolarTriggerCost, "");
    }

    public static bool HasNativeWhiteRadiance(IDataConfig? config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), SunExpIds.WhiteRadianceTag);
    }

    public static bool HasTemporaryWhiteRadiance(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        if (DictionaryUtil.Get(config.Vars, SunExpIds.TempWhiteRadiance, "0") != "1")
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), SunExpIds.WhiteRadianceTag);
    }

    public static bool HasSpecialWhiteRadiance(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), SunExpIds.WhiteRadianceTag);
    }

    public static bool TryClaimTemporaryWhiteRadiance(IDataConfig config)
    {
        if (DictionaryUtil.Get(config.Vars, SunExpIds.TempWhiteRadianceResolved, "0") == "1")
        {
            return false;
        }

        DictionaryUtil.Set(config.Vars, SunExpIds.TempWhiteRadianceResolved, "1");
        return true;
    }

    private static object? ReadMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = source.GetType();
        var property = type.GetProperty(name, flags);
        if (property != null)
        {
            try
            {
                return property.GetValue(source);
            }
            catch
            {
                return null;
            }
        }

        var field = type.GetField(name, flags);
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(source);
        }
        catch
        {
            return null;
        }
    }
}
