using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class ElementalResolutionResult
{
    public ElementalReactionType Reaction { get; set; }

    public ElementalAttachmentType? ConsumedAttachment { get; set; }

    public int ConsumedStacks { get; set; }

    public int PrimaryDamage { get; set; }

    public int AffectedTargets { get; set; }

    public bool AttachedIncomingElement { get; set; }

    public bool IsLunarReaction { get; set; }
}

public static class ElementalReactionService
{
    public static ElementalResolutionResult Hit(
        ScriptExecutor? executor,
        IStatusManager? target,
        ElementalType element,
        int baseDamage,
        string origin)
    {
        return ResolveSingle(executor, target, element, Math.Max(0, baseDamage), hasHit: true, alreadyTargetsWholeSide: false, origin);
    }

    public static IReadOnlyList<ElementalResolutionResult> HitAll(
        ScriptExecutor? executor,
        IReadOnlyList<IStatusManager> targets,
        ElementalType element,
        int baseDamage,
        string origin)
    {
        var results = new List<ElementalResolutionResult>();
        foreach (var target in targets.Where(StatusApi.IsAlive).ToArray())
        {
            results.Add(ResolveSingle(
                executor,
                target,
                element,
                Math.Max(0, baseDamage),
                hasHit: true,
                alreadyTargetsWholeSide: true,
                origin));
        }

        return results;
    }

    public static ElementalResolutionResult Apply(
        ScriptExecutor? executor,
        IStatusManager? target,
        ElementalType element,
        string origin)
    {
        return ResolveSingle(executor, target, element, 0, hasHit: false, alreadyTargetsWholeSide: false, origin);
    }

    private static ElementalResolutionResult ResolveSingle(
        ScriptExecutor? executor,
        IStatusManager? target,
        ElementalType element,
        int baseDamage,
        bool hasHit,
        bool alreadyTargetsWholeSide,
        string origin)
    {
        var result = new ElementalResolutionResult();
        if (executor?.Self == null || target == null || element == ElementalType.None || !StatusApi.IsAlive(target))
        {
            return result;
        }

        var plan = Plan(executor, executor.Self, target, element, baseDamage, hasHit, alreadyTargetsWholeSide, origin);
        if (!CanCommit(plan))
        {
            return result;
        }

        CommitConsumedAttachment(plan);

        if (hasHit && baseDamage > 0)
        {
            result.PrimaryDamage = ExecuteHitDamage(plan);
            result.AffectedTargets = plan.HitTargets.Count;
        }

        ExecuteGeneratedReactionDamage(plan);
        ExecutePostReaction(plan);

        if (ShouldAttachIncomingElement(plan.HasReaction))
        {
            result.AttachedIncomingElement = AttachElement(target, element);
        }

        result.Reaction = plan.Reaction?.Reaction ?? ElementalReactionType.None;
        result.ConsumedAttachment = plan.Reaction?.Existing;
        result.ConsumedStacks = plan.ConsumedStacks;
        result.IsLunarReaction = plan.IsLunarReaction;
        if (plan.HasReaction)
        {
            PlayerApi.ShowCaption(plan.IsLunarReaction
                ? LunarReactionService.DisplayName(plan.Reaction!.Reaction, plan.Reaction.DisplayName)
                : plan.Reaction!.DisplayName);
        }

        SunExpLog.Debug("[ElementalReaction] resolved; origin="
            + origin
            + ", source="
            + (executor.Self.InstanceId ?? "")
            + ", target="
            + (target.InstanceId ?? "")
            + ", element="
            + element
            + ", reaction="
            + result.Reaction
            + ", consumed="
            + result.ConsumedStacks
            + ", damage="
            + result.PrimaryDamage
            + ".");
        return result;
    }

    public static bool ShouldAttachIncomingElement(bool hasReaction)
    {
        // Attachment belongs to the elemental hit, not to the target's
        // post-damage life state. This matters for native Rebirth targets:
        // a lethal elemental hit must still leave its attachment available
        // after the target completes resurrection.
        return !hasReaction;
    }

    private static bool CanCommit(ResolutionPlan plan)
    {
        if (!RequiresNativeDamage(plan) || DamageApi.HasNativeDamageIdentity(plan.Executor))
        {
            return true;
        }

        SunExpLog.Warn("[ElementalReaction] resolution rejected before consuming attachment because its damage source has no native Id; origin="
            + plan.Origin
            + ", source="
            + (plan.Source.InstanceId ?? "")
            + ", target="
            + (plan.Target.InstanceId ?? "")
            + ".");
        return false;
    }

    private static bool RequiresNativeDamage(ResolutionPlan plan)
    {
        if (plan.HasHit && plan.BaseDamage > 0)
        {
            return true;
        }

        if (plan.Reaction?.Reaction is ElementalReactionType.Burgeon or ElementalReactionType.Hyperbloom)
        {
            return plan.ConsumedStacks > 0;
        }

        return plan.IsLunarReaction
            && plan.Reaction?.Reaction is ElementalReactionType.ElectroCharged or ElementalReactionType.Crystallize;
    }

    private static ResolutionPlan Plan(
        ScriptExecutor executor,
        IStatusManager source,
        IStatusManager target,
        ElementalType element,
        int baseDamage,
        bool hasHit,
        bool alreadyTargetsWholeSide,
        string origin)
    {
        var attachments = ElementalAttachmentRegistry.PriorityOrder
            .Where(definition => BuffApi.Level(target, definition.BuffId) > 0)
            .Select(definition => definition.Attachment)
            .ToList();
        ElementalReactionRegistry.TryResolve(attachments, element, out var reaction);

        var plan = new ResolutionPlan(executor, source, target, element, baseDamage, hasHit, origin, reaction);
        if (reaction != null)
        {
            var consumedDefinition = ElementalAttachmentRegistry.Definition(reaction.Existing);
            plan.ConsumedStacks = reaction.Existing == ElementalAttachmentType.DendroCore
                ? BuffApi.Level(target, consumedDefinition.BuffId)
                : Math.Min(1, BuffApi.Level(target, consumedDefinition.BuffId));
        }

        var changesScope = !alreadyTargetsWholeSide
            && reaction?.Reaction is ElementalReactionType.Superconduct or ElementalReactionType.ElectroCharged;
        plan.HitTargets = changesScope
            ? TargetApi.SameSideTargets(executor, target)
            : new List<IStatusManager> { target };
        plan.SideTargets = TargetApi.SameSideTargets(executor, target);
        return plan;
    }

    private static void CommitConsumedAttachment(ResolutionPlan plan)
    {
        if (plan.Reaction == null || plan.ConsumedStacks <= 0)
        {
            return;
        }

        var definition = ElementalAttachmentRegistry.Definition(plan.Reaction.Existing);
        plan.Target.RemoveBuff(definition.BuffId);
    }

    private static int ExecuteHitDamage(ResolutionPlan plan)
    {
        var multiplier = plan.Reaction?.Reaction is ElementalReactionType.Melt
            or ElementalReactionType.Vaporize
            or ElementalReactionType.Overloaded
            or ElementalReactionType.Quicken
                ? 2
                : 1;
        var rawDamage = Math.Max(0, plan.BaseDamage * multiplier);
        if (rawDamage <= 0)
        {
            return 0;
        }

        foreach (var target in plan.HitTargets.Where(StatusApi.IsAlive))
        {
            DamageApi.DealDamageToTarget(plan.Executor, target, rawDamage);
        }

        return rawDamage;
    }

    private static void ExecuteGeneratedReactionDamage(ResolutionPlan plan)
    {
        if (plan.Reaction == null || plan.ConsumedStacks <= 0)
        {
            return;
        }

        var magic = ElementalMagicService.Read(plan.Source);
        switch (plan.Reaction.Reaction)
        {
            case ElementalReactionType.Burgeon:
            {
                var damage = Math.Max(0, magic * plan.ConsumedStacks / 2);
                foreach (var target in plan.SideTargets.Where(StatusApi.IsAlive))
                {
                    DamageApi.DealDamageToTarget(plan.Executor, target, damage);
                }

                break;
            }
            case ElementalReactionType.Hyperbloom:
            {
                var damage = Math.Max(0, magic * plan.ConsumedStacks);
                DamageApi.DealDamageToTarget(plan.Executor, plan.Target, damage);
                break;
            }
        }
    }

    private static void ExecutePostReaction(ResolutionPlan plan)
    {
        if (plan.Reaction == null)
        {
            return;
        }

        switch (plan.Reaction.Reaction)
        {
            case ElementalReactionType.ElectroCharged:
                foreach (var target in plan.SideTargets.Where(StatusApi.IsAlive))
                {
                    target.AddBuff(SunExpIds.Vulnerability, 1);
                }

                break;
            case ElementalReactionType.Freeze:
                AddBuffIfAlive(plan.Target, SunExpIds.Frozen, 1);
                break;
            case ElementalReactionType.Burning:
                AddBuffIfAlive(plan.Target, SunExpIds.Burn, 4);
                break;
            case ElementalReactionType.Bloom:
                AddBuffIfAlive(plan.Target, SunExpIds.DendroCore, 1);
                break;
            case ElementalReactionType.Quicken:
                AddBuffIfAlive(plan.Target, SunExpIds.Vulnerability, 1);
                break;
            case ElementalReactionType.Swirl:
                ExecuteSwirl(plan);
                break;
            case ElementalReactionType.Crystallize:
                ElementalCrystalChallengeService.RequestCreate(plan.Source, plan.Target, plan.Origin + ":crystallize");
                break;
        }

        if (plan.IsLunarReaction)
        {
            LunarReactionService.Resolve(
                plan.Executor,
                plan.Source,
                plan.Target,
                plan.Reaction.Reaction,
                plan.Origin);
        }
    }

    private static void ExecuteSwirl(ResolutionPlan plan)
    {
        if (plan.Reaction == null)
        {
            return;
        }

        var attachment = ElementalAttachmentRegistry.Definition(plan.Reaction.Existing);
        if (attachment.Element == ElementalType.None)
        {
            return;
        }

        var secondaryTargets = plan.SideTargets
            .Where(target => !SameStatus(target, plan.Target) && StatusApi.IsAlive(target))
            .OrderBy(target => target.InstanceId, StringComparer.Ordinal)
            .ToList();
        ResolvePropagationBatch(plan.Executor, plan.Source, secondaryTargets, attachment.Element, plan.Origin + ":swirl");
    }

    private static void ResolvePropagationBatch(
        ScriptExecutor executor,
        IStatusManager source,
        IReadOnlyList<IStatusManager> targets,
        ElementalType propagatedElement,
        string origin)
    {
        var plans = targets
            .Select(target => Plan(executor, source, target, propagatedElement, 0, hasHit: false, alreadyTargetsWholeSide: false, origin))
            .Where(CanCommit)
            .ToList();

        foreach (var plan in plans)
        {
            if (plan.HasReaction)
            {
                CommitConsumedAttachment(plan);
            }
            else
            {
                AttachElement(plan.Target, propagatedElement);
            }
        }

        // Generated damage resolves before new vulnerability or control effects so
        // secondary target enumeration cannot change the same Swirl batch's damage.
        foreach (var plan in plans)
        {
            ExecuteGeneratedReactionDamage(plan);
        }

        foreach (var plan in plans)
        {
            ExecutePostReaction(plan);
            if (plan.HasReaction)
            {
                SunExpLog.Debug("[ElementalReaction] swirl secondary; target="
                    + (plan.Target.InstanceId ?? "")
                    + ", element="
                    + propagatedElement
                    + ", reaction="
                    + plan.Reaction!.Reaction
                    + ".");
            }
        }
    }

    private static bool AttachElement(IStatusManager target, ElementalType element)
    {
        if (!ElementalAttachmentRegistry.TryForElement(element, out var definition))
        {
            return false;
        }

        if (BuffApi.Level(target, definition.BuffId) >= definition.UpperBound)
        {
            return true;
        }

        try
        {
            target.AddBuff(definition.BuffId, 1);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ElementalReaction] post-hit attachment failed; target="
                + (target.InstanceId ?? "")
                + ", element="
                + element
                + ", error="
                + ex.Message
                + ".");
            return false;
        }
    }

    private static void AddBuffIfAlive(IStatusManager target, string buffId, int amount)
    {
        if (StatusApi.IsAlive(target) && amount > 0)
        {
            target.AddBuff(buffId, amount);
        }
    }

    private static bool SameStatus(IStatusManager left, IStatusManager right)
    {
        return ReferenceEquals(left, right)
            || !string.IsNullOrWhiteSpace(left.InstanceId)
            && string.Equals(left.InstanceId, right.InstanceId, StringComparison.Ordinal);
    }

    private sealed class ResolutionPlan
    {
        public ResolutionPlan(
            ScriptExecutor executor,
            IStatusManager source,
            IStatusManager target,
            ElementalType incoming,
            int baseDamage,
            bool hasHit,
            string origin,
            ElementalReactionDefinition? reaction)
        {
            Executor = executor;
            Source = source;
            Target = target;
            Incoming = incoming;
            BaseDamage = baseDamage;
            HasHit = hasHit;
            Origin = origin ?? "";
            Reaction = reaction;
        }

        public ScriptExecutor Executor { get; }

        public IStatusManager Source { get; }

        public IStatusManager Target { get; }

        public ElementalType Incoming { get; }

        public int BaseDamage { get; }

        public bool HasHit { get; }

        public string Origin { get; }

        public ElementalReactionDefinition? Reaction { get; }

        public bool HasReaction => Reaction != null;

        public bool IsLunarReaction => Reaction != null && LunarReactionService.IsLunarReaction(Reaction.Reaction);

        public int ConsumedStacks { get; set; }

        public List<IStatusManager> HitTargets { get; set; } = new();

        public List<IStatusManager> SideTargets { get; set; } = new();
    }
}
