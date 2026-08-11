using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public readonly struct ExplicitStatusEffectResult
{
    public ExplicitStatusEffectResult(bool applied, int before, int after, string mode)
    {
        Applied = applied;
        Before = before;
        After = after;
        Mode = mode ?? "";
    }

    public bool Applied { get; }

    public int Before { get; }

    public int After { get; }

    public int Delta => After - Before;

    public string Mode { get; }
}

/// <summary>
/// Applies an effect to one already-resolved status without depending on
/// ScriptExecutor.Object or the native ForEachObject multiplayer router.
/// </summary>
public static class ExplicitStatusEffectApi
{
    private const string EclipsedMoonBuffId = "buff_eclipsedmoon";
    private const string DefendPercentVariable = "DefendPercent";

    public static ExplicitStatusEffectResult AddCompanionShield(IStatusManager? target, int baseAmount)
    {
        if (!StatusApi.IsAlive(target) || baseAmount <= 0)
        {
            return Rejected(StatusApi.Defence(target), "shield");
        }

        try
        {
            var eclipsedMoon = target!.GetBuff(EclipsedMoonBuffId);
            if (eclipsedMoon?.buffConfig != null)
            {
                var beforeHp = target.CurHp;
                var healMultiplier = 0.5d + eclipsedMoon.buffConfig.Level * 0.05d;
                var heal = Math.Max(0, (int)Math.Ceiling(baseAmount * healMultiplier));
                if (heal > 0)
                {
                    target.Heal(heal, "Heal");
                    target.UpdateStatus(true);
                }
                return new ExplicitStatusEffectResult(heal > 0, beforeHp, target.CurHp, "eclipsed-moon-heal");
            }

            var before = target.Defend;
            var multiplier = StatusApi.DynamicMultiplier(target, DefendPercentVariable);
            var adjusted = ResolveShieldAmount(baseAmount, multiplier);
            if (adjusted > 0)
            {
                target.Defend = Math.Max(0, target.Defend + adjusted);
                target.UpdateStatus(true);
            }
            return new ExplicitStatusEffectResult(adjusted > 0, before, target.Defend, "shield");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ExplicitStatusEffect] shield failed: target="
                            + (target?.InstanceId ?? "")
                            + ", amount="
                            + baseAmount
                            + ", error="
                            + ex.Message);
            return Rejected(StatusApi.Defence(target), "shield");
        }
    }

    public static ExplicitStatusEffectResult HealCompanion(IStatusManager? target, int amount)
    {
        if (!StatusApi.IsAlive(target) || amount <= 0)
        {
            return Rejected(target?.CurHp ?? 0, "heal");
        }

        var before = target!.CurHp;
        if (!StatusApi.TryHeal(target, amount))
        {
            return Rejected(before, "heal");
        }

        target.UpdateStatus(true);
        return new ExplicitStatusEffectResult(true, before, target.CurHp, "heal");
    }

    public static ExplicitStatusEffectResult AddCompanionBuff(
        IStatusManager? target,
        string buffId,
        int stacks)
    {
        if (!StatusApi.IsAlive(target) || string.IsNullOrWhiteSpace(buffId) || stacks <= 0)
        {
            return Rejected(BuffApi.Level(target, buffId), "buff");
        }

        var before = BuffApi.Level(target, buffId);
        try
        {
            target!.AddBuff(buffId, stacks);
            target.UpdateStatus(true);
            var after = BuffApi.Level(target, buffId);
            return new ExplicitStatusEffectResult(after > before, before, after, "buff");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ExplicitStatusEffect] buff failed: target="
                            + (target?.InstanceId ?? "")
                            + ", buff="
                            + buffId
                            + ", stacks="
                            + stacks
                            + ", error="
                            + ex.Message);
            return Rejected(before, "buff");
        }
    }

    public static int ResolveShieldAmount(int baseAmount, float defendMultiplier)
    {
        if (baseAmount <= 0 || float.IsNaN(defendMultiplier) || float.IsInfinity(defendMultiplier))
        {
            return 0;
        }

        return Math.Max(0, (int)(baseAmount * Math.Max(0f, defendMultiplier)));
    }

    private static ExplicitStatusEffectResult Rejected(int value, string mode)
    {
        return new ExplicitStatusEffectResult(false, value, value, mode);
    }
}
