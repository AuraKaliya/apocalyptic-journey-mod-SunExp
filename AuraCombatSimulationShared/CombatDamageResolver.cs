using System;

namespace AuraCombatSimulation.Shared;

public sealed class CombatDamageResolution
{
    public int OutgoingAmount { get; set; }

    public int IncomingAmount { get; set; }

    public int BlockedAmount { get; set; }

    public int UnboundedHpDamage { get; set; }

    public int HpDamage { get; set; }

    public int DurabilityDamage => BlockedAmount + HpDamage;

    public bool BypassesBlock { get; set; }
}

public static class CombatDamageResolver
{
    public static CombatDamageResolution Resolve(
        CombatActorState? source,
        CombatActorState target,
        CombatRuleset ruleset,
        CombatSimulationEffectKind kind,
        int amount,
        string definitionId = "")
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));

        amount = Math.Max(0, amount);
        var directHpLoss = kind == CombatSimulationEffectKind.DirectHpLoss;
        var bypassesBlock =
            kind is CombatSimulationEffectKind.TrueDamage
                or CombatSimulationEffectKind.DirectHpLoss;
        var outgoingMultiplier = Variable(
            source,
            ruleset,
            "PercentDamage",
            1d);
        var outgoingFlat = Variable(
            source,
            ruleset,
            "DefaultDamage",
            0d);
        var incomingMultiplier = Variable(
            target,
            ruleset,
            "AttackedPercentDamage",
            1d);
        var incomingFlat = Variable(
            target,
            ruleset,
            "AttackedDefaultDamage",
            0d);
        var attributeMultiplier =
            source?.Kind == CombatSimulationActorKind.Player
            && kind == CombatSimulationEffectKind.Damage
                ? 1d
                  + Math.Max(
                      0d,
                      Variable(source, ruleset, "Strength", 0d))
                  * 0.03d
                : 1d;
        var outgoingAmount = WitchRounded(
            (amount * outgoingMultiplier + outgoingFlat)
            * attributeMultiplier);
        var incoming = directHpLoss
            ? Math.Max(
                0,
                WitchRounded(
                    amount
                    * Variable(
                        target,
                        ruleset,
                        "DirectHpLossTaken." + definitionId,
                        1d)
                    * DamageFilterMultiplier(
                        target,
                        ruleset,
                        kind,
                        definitionId)))
            : Math.Max(
                0,
                (int)((outgoingAmount + incomingFlat)
                      * incomingMultiplier
                      * DamageFilterMultiplier(
                          target,
                          ruleset,
                          kind,
                          definitionId)));
        var blocked = bypassesBlock
            ? 0
            : Math.Min(Math.Max(0, target.Block), incoming);
        var unboundedHpDamage = Math.Min(
            Math.Max(0, target.Hp),
            Math.Max(0, incoming - blocked));
        return new CombatDamageResolution
        {
            OutgoingAmount = outgoingAmount,
            IncomingAmount = incoming,
            BlockedAmount = blocked,
            UnboundedHpDamage = unboundedHpDamage,
            HpDamage = ApplyHpLossLimit(
                target,
                ruleset,
                unboundedHpDamage),
            BypassesBlock = bypassesBlock
        };
    }

    public static int WitchRounded(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        if (value <= int.MinValue) return int.MinValue;
        var ceiling = Math.Ceiling(value);
        return (int)(ceiling - value <= 0.01d
            ? ceiling
            : Math.Floor(value));
    }

    private static int ApplyHpLossLimit(
        CombatActorState target,
        CombatRuleset ruleset,
        int requested)
    {
        requested = Math.Max(0, requested);
        if (requested <= 0
            || !target.Variables.TryGetValue(
                "MaxChangeHp",
                out var maximumChangeRatio))
        {
            return requested;
        }
        var ratio = Math.Max(0d, Math.Min(1d, maximumChangeRatio));
        var maximumLoss = Math.Max(
            0,
            (int)Math.Floor(target.MaxHp * ratio));
        var alreadyLost = Math.Max(
            0,
            WitchRounded(Variable(
                target,
                ruleset,
                "HpLossThisAction",
                0d)));
        return Math.Min(
            requested,
            Math.Max(0, maximumLoss - alreadyLost));
    }

    private static double DamageFilterMultiplier(
        CombatActorState target,
        CombatRuleset ruleset,
        CombatSimulationEffectKind kind,
        string definitionId)
    {
        var damageType = kind switch
        {
            CombatSimulationEffectKind.TrueDamage => "True",
            CombatSimulationEffectKind.DirectHpLoss
                when (definitionId ?? "").StartsWith(
                    "buff_",
                    StringComparison.OrdinalIgnoreCase) => "Dot",
            CombatSimulationEffectKind.DirectHpLoss => "DirectHpLoss",
            _ => "Normal"
        };
        var typedMultiplier = Math.Max(
            0d,
            Variable(
                target,
                ruleset,
                "DamageTakenMultiplier." + damageType,
                1d));
        var typeReduction = Math.Max(
            0d,
            Variable(
                target,
                ruleset,
                "DamageFilter." + damageType,
                0d));
        var sourceReduction = string.IsNullOrWhiteSpace(definitionId)
            ? 0d
            : Math.Max(
                0d,
                Variable(
                    target,
                    ruleset,
                    "DamageFilter." + definitionId,
                    0d));
        return typedMultiplier
               * Math.Max(
                   0d,
                   1d - Math.Max(typeReduction, sourceReduction) / 100d);
    }

    private static double Variable(
        CombatActorState? actor,
        CombatRuleset ruleset,
        string key,
        double fallback)
    {
        return CombatSimulationExpressionEvaluator.ResolveVariable(
            actor,
            ruleset,
            key,
            fallback);
    }
}
