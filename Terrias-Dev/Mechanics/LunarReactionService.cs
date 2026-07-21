using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class LunarReactionService
{
    public static bool IsLunarReaction(ElementalReactionType reaction)
    {
        return FieldApi.IsSharedFieldActive(TerriasFieldId.MoonDomain)
            && reaction is ElementalReactionType.ElectroCharged or ElementalReactionType.Bloom or ElementalReactionType.Crystallize;
    }

    public static string DisplayName(ElementalReactionType reaction, string fallback)
    {
        return reaction switch
        {
            ElementalReactionType.ElectroCharged => "月感电",
            ElementalReactionType.Bloom => "月绽放",
            ElementalReactionType.Crystallize => "月结晶",
            _ => fallback
        };
    }

    public static void Resolve(
        ScriptExecutor executor,
        IStatusManager source,
        IStatusManager target,
        ElementalReactionType reaction,
        string origin)
    {
        switch (reaction)
        {
            case ElementalReactionType.ElectroCharged:
                ResolveElectroCharged(executor, source);
                break;
            case ElementalReactionType.Bloom:
                ResolveBloom(source, target);
                break;
            case ElementalReactionType.Crystallize:
                ResolveCrystallize(executor, source);
                break;
            default:
                return;
        }

        ConstellationService.ResolveColumbinaLunarReaction(source);
        TerriasLog.Debug("[LunarReaction] resolved " + reaction + " from " + origin + "; source=" + (source.InstanceId ?? "") + ".");
    }

    private static void ResolveElectroCharged(ScriptExecutor executor, IStatusManager source)
    {
        var key = Key("ElectroCharged", source);
        var count = CombatVarApi.AddInt(key, 1);
        var damage = LunarReactionRules.ElectroChargedDamage(count);
        DealToOpposingSide(executor, source, damage, trueDamage: true);
        if (ColumbinaPassiveService.IsActive(source))
        {
            DealToOpposingSide(executor, source, damage, trueDamage: true);
        }
    }

    private static void ResolveBloom(IStatusManager source, IStatusManager target)
    {
        if (StatusApi.IsAlive(target))
        {
            target.AddBuff(TerriasIds.DendroCore, 1);
        }

        if (ColumbinaPassiveService.IsActive(source))
        {
            PlayerPartyApi.TryGainPower(source, 2);
        }
    }

    private static void ResolveCrystallize(ScriptExecutor executor, IStatusManager source)
    {
        var added = ColumbinaPassiveService.IsActive(source) ? 2 : 1;
        var key = Key("Crystallize", source);
        var next = LunarReactionRules.AddCrystallizeCounts(CombatVarApi.GetInt(key), added, out var triggerTimes);
        CombatVarApi.SetInt(key, next);
        for (var i = 0; i < triggerTimes; i++)
        {
            var shieldSnapshot = StatusApi.Defence(source);
            DealToOpposingSide(executor, source, shieldSnapshot, trueDamage: false);
        }
    }

    private static void DealToOpposingSide(ScriptExecutor executor, IStatusManager source, int damage, bool trueDamage)
    {
        if (damage <= 0)
        {
            return;
        }

        foreach (var target in TargetApi.OpposingSideTargets(executor, source))
        {
            DamageApi.DealDamageToTarget(executor, target, damage, "AllTarget", trueDamage ? "True" : "");
        }
    }

    private static string Key(string kind, IStatusManager source)
    {
        return "Terrias.Lunar." + kind + "." + (source.InstanceId ?? source.GetHashCode().ToString());
    }
}
