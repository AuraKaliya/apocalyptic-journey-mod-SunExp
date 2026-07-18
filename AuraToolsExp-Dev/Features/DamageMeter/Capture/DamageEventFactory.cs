using System;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageEventFactory
{
    internal static void Normalize(DamageEvent damage)
    {
        damage.SourceInstanceId = Trim(damage.SourceInstanceId, "unknown");
        damage.SourceDisplayName = Trim(damage.SourceDisplayName, damage.SourceInstanceId);
        damage.TargetInstanceId = Trim(damage.TargetInstanceId, "");
        damage.SourceDataId = Trim(damage.SourceDataId, "");
        damage.DetailLabel = Trim(damage.DetailLabel, damage.SourceDataId);
        damage.DamageType = Trim(damage.DamageType, "Unknown");
        damage.HpDamage = Math.Max(0, Math.Min(DamageMeterProtocol.MaxDamagePerEvent, damage.HpDamage));
        damage.ShieldDamage = Math.Max(0, Math.Min(DamageMeterProtocol.MaxDamagePerEvent, damage.ShieldDamage));
        damage.FinalDamage = Math.Max(0, Math.Min(DamageMeterProtocol.MaxDamagePerEvent, damage.FinalDamage));
    }

    private static string Trim(string value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= DamageMeterProtocol.MaxStringLength
            ? result
            : result.Substring(0, DamageMeterProtocol.MaxStringLength);
    }
}
