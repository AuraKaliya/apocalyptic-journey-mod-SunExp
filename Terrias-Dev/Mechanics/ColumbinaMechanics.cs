using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class ColumbinaMechanics
{
    public static void ResolveActionAfter(IStatusManager? actor)
    {
        if (actor == null || !StatusApi.IsAlive(actor))
        {
            return;
        }

        if (FieldApi.IsSharedFieldActive(TerriasFieldId.MoonDomain))
        {
            var c6Sources = ConstellationService.EligibleColumbinaC6Count();
            if (c6Sources > 0)
            {
                actor!.AddBuff(TerriasIds.Extraordinary, 80 * c6Sources);
            }
        }

        // Gravity Ripple is owned by the Buff, not by Columbina's currently
        // active career. Polymorph may suppress New Moon Law, but it must not
        // disable an already-applied Ripple or its Gravity Value progression.
        if (BuffApi.Level(actor, TerriasIds.GravityRipple) <= 0)
        {
            return;
        }

        var executor = DamageApi.CreateCardSourceExecutor(
            actor,
            TerriasIds.ColumbinaEternalTideCardId,
            "Columbina.GravityRipple");
        if (executor == null)
        {
            TerriasLog.Warn("[Columbina] gravity ripple skipped because its native damage source is unavailable.");
            return;
        }

        var targets = TargetApi.OpposingSideTargets(executor, actor).Where(StatusApi.IsAlive).ToList();
        if (targets.Count > 0)
        {
            var target = targets[UnityEngine.Random.Range(0, targets.Count)];
            var damage = Math.Max(1, StatusApi.MaxHp(actor) * 3 / 100);
            var result = ElementalReactionService.Hit(executor, target, ElementalType.Hydro, damage, "Columbina.GravityRipple");
            AddGravityValue(executor, actor, Math.Abs(result.PrimaryDamage) % 10);
        }

        DamageApi.RemoveBuffStacks(executor, actor, TerriasIds.GravityRipple, 1);
    }

    public static void AddGravityValue(ScriptExecutor executor, IStatusManager source, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var before = Math.Max(0, BuffApi.Level(source, TerriasIds.GravityValue));
        var after = Math.Min(100, before + amount);
        BuffApi.SetExactLevel(source, TerriasIds.GravityValue, after);
        if (LunarReactionRules.Crossed(before, after, 50))
        {
            PlayerPartyApi.TryGainPower(source, 1);
            if (ConstellationService.IsColumbinaWithLevel(source, 2))
            {
                TriggerGravityInterference(executor, source, "Constellation2");
            }
        }

        if (LunarReactionRules.Crossed(before, after, 75))
        {
            TargetApi.SetStatusForTarget(executor, source, "Self");
            CombatCardApi.TryDrawPlayerCards(executor, 1, "Columbina.Gravity75");
        }

        if (after >= 100)
        {
            TriggerGravityInterference(executor, source, "Gravity100");
            BuffApi.SetExactLevel(source, TerriasIds.GravityValue, 0);
        }
    }

    public static void TriggerGravityInterference(ScriptExecutor executor, IStatusManager source, string origin)
    {
        var opponents = TargetApi.OpposingSideTargets(executor, source).Where(StatusApi.IsAlive).ToList();
        if (opponents.Count == 0)
        {
            return;
        }

        switch (UnityEngine.Random.Range(0, 3))
        {
            case 0:
                ElementalReactionService.HitAll(executor, opponents, ElementalType.Electro,
                    Math.Max(1, StatusApi.MaxHp(source) * 6 / 100), origin + ":Electro");
                break;
            case 1:
                for (var i = 0; i < 5; i++)
                {
                    var target = opponents[UnityEngine.Random.Range(0, opponents.Count)];
                    ElementalReactionService.Hit(executor, target, ElementalType.Dendro,
                        Math.Max(1, StatusApi.MaxHp(source) * 2 / 100), origin + ":Dendro:" + i);
                }
                break;
            default:
            {
                var target = opponents[UnityEngine.Random.Range(0, opponents.Count)];
                ElementalReactionService.Hit(executor, target, ElementalType.Geo,
                    Math.Max(1, StatusApi.MaxHp(source) / 10), origin + ":Geo");
                break;
            }
        }

        ConstellationService.ResolveInterferenceTriggered(source);
    }
}
