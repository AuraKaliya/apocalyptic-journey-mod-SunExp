using AuraToolsExp.Dll.Features.DamageMeter.Model;
using Witch;

namespace AuraToolsExp.Dll.Features.DamageMeter.Resolution;

internal static class CombatantTeamResolver
{
    public static DamageTeam Resolve(IStatusManager? status, string instanceId)
    {
        return DamageMeterFightIndex.ResolveTeam(status, instanceId);
    }

    public static IStatusManager? ResolveStatus(string instanceId)
    {
        return DamageMeterFightIndex.ResolveStatus(instanceId);
    }

    public static string DisplayName(IStatusManager? status, string fallback)
    {
        return DamageMeterFightIndex.DisplayName(status, fallback);
    }

    public static DamageSourceAttribution ResolveAttribution(
        IStatusManager? status,
        string instanceId,
        string fallbackDisplayName)
    {
        return DamageMeterFightIndex.ResolveAttribution(status, instanceId, fallbackDisplayName);
    }
}

internal static class DamageDetailResolver
{
    public static bool IsBuff(string dataId)
    {
        return DamageMeterFightIndex.IsBuff(dataId);
    }

    public static string ResolveLabel(string dataId, string damageType)
    {
        return DamageMeterFightIndex.ResolveLabel(dataId, damageType);
    }
}
