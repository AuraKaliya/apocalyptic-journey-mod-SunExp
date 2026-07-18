using System;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageEventFactory
{
    internal static DamageEvent Create(ResolvedDamageInput input)
    {
        var damage = new DamageEvent
        {
            SourceInstanceId = input.SourceInstanceId,
            SourceDisplayName = input.SourceDisplayName,
            SourceTeam = input.SourceTeam,
            TargetInstanceId = input.TargetInstanceId,
            SourceDataId = input.SourceDataId,
            DetailLabel = input.DetailLabel,
            DamageType = input.DamageType,
            HpDamage = input.HpDamage,
            ShieldDamage = input.ShieldDamage,
            FinalDamage = input.FinalDamage,
            AttributionConfidence = input.AttributionConfidence
        };
        Normalize(damage);
        return damage;
    }

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

internal sealed class ResolvedDamageInput
{
    internal string SourceInstanceId { get; set; } = "";
    internal string SourceDisplayName { get; set; } = "";
    internal DamageTeam SourceTeam { get; set; }
    internal string TargetInstanceId { get; set; } = "";
    internal string SourceDataId { get; set; } = "";
    internal string DetailLabel { get; set; } = "";
    internal string DamageType { get; set; } = "";
    internal int HpDamage { get; set; }
    internal int ShieldDamage { get; set; }
    internal int FinalDamage { get; set; }
    internal DamageAttributionConfidence AttributionConfidence { get; set; }
}
