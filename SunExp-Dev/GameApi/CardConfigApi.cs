using System;
using System.Collections.Generic;
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

        var baseCost = DictionaryUtil.GetInt(config.data, "Expend");
        var scaledBaseCost = Math.Min((int)(baseCost * ReadPlayerCardCostMultiplier()), 4);
        var total = scaledBaseCost
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
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.Vars, "Tag"), SunExpIds.WhiteRadianceTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), SunExpIds.WhiteRadianceTag);
    }

    public static bool HasTemporaryWhiteRadiance(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var hasMarker = DictionaryUtil.Get(config.Vars, SunExpIds.TempWhiteRadiance, "0") == "1"
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.TempWhiteRadiance);
        if (!hasMarker)
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), SunExpIds.WhiteRadianceTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.TempWhiteRadiance);
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
        var lockId = EnsureTemporaryWhiteRadianceLockId(config);
        var sharedKey = TemporaryWhiteRadianceResolvedKey(lockId);
        var cardResolved = DictionaryUtil.Get(config.Vars, SunExpIds.TempWhiteRadianceResolved, "0") == "1";
        if (ExecutorApi.CombatIntGet(sharedKey) == 1)
        {
            if (cardResolved)
            {
                return false;
            }

            lockId = AssignTemporaryWhiteRadianceLockId(config);
            sharedKey = TemporaryWhiteRadianceResolvedKey(lockId);
        }

        if (ExecutorApi.CombatIntGet(sharedKey) == 1)
        {
            return false;
        }

        ExecutorApi.CombatIntSet(sharedKey, 1);
        DictionaryUtil.Set(config.Vars, SunExpIds.TempWhiteRadianceResolved, "1");
        return true;
    }

    private static string EnsureTemporaryWhiteRadianceLockId(IDataConfig config)
    {
        var lockId = DictionaryUtil.Get(config.Vars, SunExpIds.TempWhiteRadianceLockId);
        return string.IsNullOrWhiteSpace(lockId) || lockId == "0"
            ? AssignTemporaryWhiteRadianceLockId(config)
            : lockId;
    }

    private static string AssignTemporaryWhiteRadianceLockId(IDataConfig config)
    {
        var lockId = ExecutorApi.CombatIntAdd("SunExpTempWhiteRadianceLockSeq", 1).ToString();
        DictionaryUtil.Set(config.Vars, SunExpIds.TempWhiteRadianceLockId, lockId);
        DictionaryUtil.Set(config.Vars, SunExpIds.TempWhiteRadianceResolved, "0");
        return lockId;
    }

    private static string TemporaryWhiteRadianceResolvedKey(string lockId)
    {
        return "SunExpTempWhiteRadianceResolved_" + lockId;
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

    private static float ReadPlayerCardCostMultiplier()
    {
        try
        {
            var fightPlayer = FindType("FightPlayer")?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var status = fightPlayer == null ? null : ReadMember(fightPlayer, "Status");
            var dynamicVariables = status == null ? null : ReadMember(status, "dynamicVariables") as IDictionary<string, float>;
            return dynamicVariables != null && dynamicVariables.TryGetValue("CardCost", out var multiplier)
                ? multiplier
                : 1f;
        }
        catch
        {
            return 1f;
        }
    }

    private static Type? FindType(string name)
    {
        return Type.GetType(name)
            ?? Array.Find(AppDomain.CurrentDomain.GetAssemblies(), asm => asm.GetType(name) != null)?.GetType(name);
    }
}
