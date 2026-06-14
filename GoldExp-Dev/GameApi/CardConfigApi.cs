using System;
using System.Reflection;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.GameApi;

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

    public static bool HasNativeGoldDream(IDataConfig? config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), GoldExpIds.GoldDreamTag);
    }

    public static bool HasTemporaryGoldDream(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        if (DictionaryUtil.Get(config.Vars, GoldExpIds.TempGoldDream, "0") != "1")
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), GoldExpIds.GoldDreamTag);
    }

    public static bool HasSpecialGoldDream(IDataConfig? config)
    {
        return config != null && DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), GoldExpIds.GoldDreamTag);
    }

    public static bool TryClaimTemporaryGoldDream(IDataConfig config)
    {
        var lockId = EnsureTemporaryGoldDreamLockId(config);
        var sharedKey = TemporaryGoldDreamResolvedKey(lockId);
        var cardResolved = DictionaryUtil.Get(config.Vars, GoldExpIds.TempGoldDreamResolved, "0") == "1";
        if (ExecutorApi.CombatIntGet(sharedKey) == 1)
        {
            if (cardResolved)
            {
                return false;
            }

            lockId = AssignTemporaryGoldDreamLockId(config);
            sharedKey = TemporaryGoldDreamResolvedKey(lockId);
        }

        if (ExecutorApi.CombatIntGet(sharedKey) == 1)
        {
            return false;
        }

        ExecutorApi.CombatIntSet(sharedKey, 1);
        DictionaryUtil.Set(config.Vars, GoldExpIds.TempGoldDreamResolved, "1");
        return true;
    }

    private static string EnsureTemporaryGoldDreamLockId(IDataConfig config)
    {
        var lockId = DictionaryUtil.Get(config.Vars, GoldExpIds.TempGoldDreamLockId);
        return string.IsNullOrWhiteSpace(lockId) || lockId == "0"
            ? AssignTemporaryGoldDreamLockId(config)
            : lockId;
    }

    private static string AssignTemporaryGoldDreamLockId(IDataConfig config)
    {
        var lockId = ExecutorApi.CombatIntAdd(GoldExpIds.TempGoldDreamLockSeq, 1).ToString();
        DictionaryUtil.Set(config.Vars, GoldExpIds.TempGoldDreamLockId, lockId);
        DictionaryUtil.Set(config.Vars, GoldExpIds.TempGoldDreamResolved, "0");
        return lockId;
    }

    private static string TemporaryGoldDreamResolvedKey(string lockId)
    {
        return "GoldExpTempGoldDreamResolved_" + lockId;
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
