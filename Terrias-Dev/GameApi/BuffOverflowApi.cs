using System;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class BuffOverflowApi
{
    private const int BurnUpperBoundFallback = 1;
    private const int SolarRadianceDefaultUpperBound = 12;
    private const int WunaSolarRadianceUpperBound = 15;

    public static int BurnUpperBound(IStatusManager? target)
    {
        return BuffUpperBound(target, SunExpIds.Burn, BurnUpperBoundFallback);
    }

    public static int SolarRadianceUpperBound(IStatusManager? target)
    {
        return BuffApi.IsWunaPlayerStatus(target)
            ? WunaSolarRadianceUpperBound
            : SolarRadianceDefaultUpperBound;
    }

    public static int BuffUpperBound(IStatusManager? target, string buffId, int fallback)
    {
        if (target != null && !string.IsNullOrWhiteSpace(buffId))
        {
            var liveUpperBound = target.GetBuff(buffId)?.buffConfig?.UpperBound ?? 0;
            if (liveUpperBound > 0)
            {
                return liveUpperBound;
            }
        }

        return ConfiguredBuffUpperBound(buffId, fallback);
    }

    public static void PrepareSolarRadianceUpperBound(IStatusManager? target, string buffId)
    {
        if (target == null || buffId != SunExpIds.SolarRadiance)
        {
            return;
        }

        ApplySolarRadianceUpperBound(target, SolarRadianceUpperBound(target));
    }

    public static void FinalizeSolarRadianceUpperBound(IStatusManager? target, string buffId, int amount)
    {
        if (target == null || buffId != SunExpIds.SolarRadiance)
        {
            return;
        }

        var upperBound = SolarRadianceUpperBound(target);
        var buff = target.GetBuff(SunExpIds.SolarRadiance);
        var current = buff?.buffConfig?.Level ?? 0;
        ApplySolarRadianceUpperBound(target, upperBound);

        if (amount <= 0 || !BuffApi.IsWunaPlayerStatus(target) || buff?.buffConfig == null)
        {
            return;
        }

        var before = Math.Max(0, current - amount);
        var desired = Math.Min(upperBound, before + amount);
        if (desired > buff.buffConfig.Level)
        {
            buff.buffConfig.Level = desired;
        }
    }

    public static bool HandleBurnOverflow(IStatusManager? target, string buffId, int amount)
    {
        if (target == null || buffId != SunExpIds.Burn || amount <= 0 || !FieldApi.IsSharedFieldActive(SunExpFieldId.ScorchingCanopy))
        {
            return false;
        }

        var ward = target.GetBuff(SunExpIds.EmberCloak);
        if (ward?.buffConfig != null && ward.buffConfig.Level > 0)
        {
            return false;
        }

        var upperBound = BurnUpperBound(target);
        var overflow = BuffApi.Level(target, SunExpIds.Burn) + amount - upperBound;
        if (overflow > 0)
        {
            SunExpLog.Debug("Burn overflow converted: target=" + target.InstanceId
                + ", burnBefore=" + BuffApi.Level(target, SunExpIds.Burn)
                + ", add=" + amount
                + ", upperBound=" + upperBound
                + ", overflow=" + overflow);
            target.AddBuff(SunExpIds.BodyBurn, overflow);
            return true;
        }

        return false;
    }

    private static int ConfiguredBuffUpperBound(string buffId, int fallback)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return fallback;
        }

        try
        {
            var data = AuraGameDataHostApi.CopyRow(DataType.Buff, buffId);
            var configured = DictionaryUtil.ParseInt(DictionaryUtil.Get(data, "UpperBound"));
            return configured > 0 ? configured : fallback;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Buff upper bound fallback used: id=" + buffId + ", fallback=" + fallback + ", error=" + ex.Message);
            return fallback;
        }
    }

    private static void ApplySolarRadianceUpperBound(IStatusManager target, int upperBound)
    {
        var buff = target.GetBuff(SunExpIds.SolarRadiance);
        if (buff?.buffConfig == null)
        {
            return;
        }

        var nextUpperBound = Math.Max(1, upperBound);
        if (buff.buffConfig.UpperBound != nextUpperBound)
        {
            buff.buffConfig.UpperBound = nextUpperBound;
        }

        if (buff.buffConfig.Level > nextUpperBound)
        {
            buff.buffConfig.Level = nextUpperBound;
        }
    }
}
