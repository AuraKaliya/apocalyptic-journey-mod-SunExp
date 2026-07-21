using System;
using System.Collections.Generic;
using System.Reflection;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.GameApi;

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

        if (payload is string cardId)
        {
            return FromCardId(cardId);
        }

        if (payload is IDictionary<string, string> row)
        {
            return FromDataRow(row);
        }

        foreach (var name in new[] { "dataConfig", "DataConfig", "Data", "data", "Config", "config", "Source", "source" })
        {
            var value = ReadMember(payload, name);
            if (ReferenceEquals(value, payload))
            {
                continue;
            }

            var config = FromActionPayload(value);
            if (config != null)
            {
                return config;
            }
        }

        return null;
    }

    private static IDataConfig? FromDataRow(IDictionary<string, string> row)
    {
        var id = DictionaryUtil.Get(row, "Id");
        var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, id);
        return handle == null
            ? null
            : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle }).Instance;
    }

    private static IDataConfig? FromCardId(string? cardId)
    {
        var id = (cardId ?? "").Trim();
        var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, id);
        return handle == null
            ? null
            : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle }).Instance;
    }

    public static string Id(IDataConfig? config)
    {
        return AuraGameDataHostApi.ReadField(config, "Id", AuraGameDataFieldAccess.Base, "unknown");
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

    public static int NativeDisplayCost(IDataConfig? config, object? status = null)
    {
        if (config == null)
        {
            return 0;
        }

        var baseCost = Math.Max(0, DictionaryUtil.GetInt(config.data, "Expend"));
        var extra = DictionaryUtil.GetInt(config.Vars, "TotalExCost")
                    + DictionaryUtil.GetInt(config.Vars, "ExCost")
                    + DictionaryUtil.GetInt(config.Vars, "OnceExCost");
        var multiplier = 1f;
        if (ReadDynamicVariables(status) is IDictionary<string, float> dynamicVariables
            && dynamicVariables.TryGetValue("CardCost", out var currentMultiplier))
        {
            multiplier = currentMultiplier;
        }

        return Math.Max(0, (int)Math.Abs(baseCost * multiplier) + extra);
    }

    private static object? ReadDynamicVariables(object? status)
    {
        if (status == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return status.GetType().GetProperty("dynamicVariables", flags)?.GetValue(status)
               ?? status.GetType().GetField("dynamicVariables", flags)?.GetValue(status);
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

        var overrideText = DictionaryUtil.Get(config.Vars, TerriasIds.SolarTriggerCost);
        if (!string.IsNullOrWhiteSpace(overrideText))
        {
            return Math.Max(0, DictionaryUtil.ParseInt(overrideText));
        }

        return Math.Max(0, fallback);
    }

    public static void ClearSolarTriggerCost(IDataConfig? config)
    {
        DictionaryUtil.Set(config?.Vars, TerriasIds.SolarTriggerCost, "");
    }

    public static bool HasNativeWhiteRadiance(IDataConfig? config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.Vars, "Tag"), TerriasIds.WhiteRadianceTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), TerriasIds.WhiteRadianceTag);
    }

    public static bool HasTemporaryWhiteRadiance(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        var hasMarker = DictionaryUtil.Get(config.Vars, TerriasIds.TempWhiteRadiance, "0") == "1"
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance);
        if (!hasMarker)
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), TerriasIds.WhiteRadianceTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance);
    }

    public static bool HasSpecialWhiteRadiance(IDataConfig? config)
    {
        if (config == null)
        {
            return false;
        }

        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), TerriasIds.WhiteRadianceTag);
    }

    public static bool TryClaimTemporaryWhiteRadiance(IDataConfig config)
    {
        var lockId = EnsureTemporaryWhiteRadianceLockId(config);
        var sharedKey = TemporaryWhiteRadianceResolvedKey(lockId);
        var cardResolved = DictionaryUtil.Get(config.Vars, TerriasIds.TempWhiteRadianceResolved, "0") == "1";
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
        DictionaryUtil.Set(config.Vars, TerriasIds.TempWhiteRadianceResolved, "1");
        return true;
    }

    private static string EnsureTemporaryWhiteRadianceLockId(IDataConfig config)
    {
        var lockId = DictionaryUtil.Get(config.Vars, TerriasIds.TempWhiteRadianceLockId);
        return string.IsNullOrWhiteSpace(lockId) || lockId == "0"
            ? AssignTemporaryWhiteRadianceLockId(config)
            : lockId;
    }

    private static string AssignTemporaryWhiteRadianceLockId(IDataConfig config)
    {
        var lockId = ExecutorApi.CombatIntAdd("TerriasTempWhiteRadianceLockSeq", 1).ToString();
        DictionaryUtil.Set(config.Vars, TerriasIds.TempWhiteRadianceLockId, lockId);
        DictionaryUtil.Set(config.Vars, TerriasIds.TempWhiteRadianceResolved, "0");
        return lockId;
    }

    private static string TemporaryWhiteRadianceResolvedKey(string lockId)
    {
        return "TerriasTempWhiteRadianceResolved_" + lockId;
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
            var dynamicVariables = FightPlayer.Instance?.Status?.dynamicVariables;
            return dynamicVariables != null && dynamicVariables.TryGetValue("CardCost", out var multiplier)
                ? multiplier
                : 1f;
        }
        catch
        {
            return 1f;
        }
    }
}
