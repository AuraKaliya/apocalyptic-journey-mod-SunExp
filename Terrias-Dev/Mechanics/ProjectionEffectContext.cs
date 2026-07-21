using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public enum ProjectionEffectKind
{
    Damage,
    Block,
    Heal,
    Buff
}

/// <summary>
/// Keeps projection action identity separate from the player whose stable
/// combat modifiers and attribution the projection inherits.
/// </summary>
public sealed class ProjectionEffectContext
{
    public ProjectionEffectContext(
        OtherObj actor,
        IStatusManager modifierOwner,
        IStatusManager attributionOwner,
        CompanionIntentDefinition intent)
    {
        Actor = actor;
        ModifierOwner = modifierOwner;
        AttributionOwner = attributionOwner;
        Intent = intent;
    }

    public OtherObj Actor { get; }

    public IStatusManager ModifierOwner { get; }

    public IStatusManager AttributionOwner { get; }

    public CompanionIntentDefinition Intent { get; }

    public int ApplyStableModifier(int value, ProjectionEffectKind kind)
    {
        var multiplier = ProjectionModifierPolicyRegistry.HasActiveConsumableModifier(ModifierOwner)
            ? 1f
            : kind switch
        {
            ProjectionEffectKind.Damage => StatusApi.DynamicMultiplier(ModifierOwner, "PercentDamage"),
            ProjectionEffectKind.Block => StatusApi.DynamicMultiplier(ModifierOwner, "PercentDefence"),
            ProjectionEffectKind.Heal => StatusApi.DynamicMultiplier(ModifierOwner, "PercentHeal"),
            _ => 1f
        };
        return Math.Max(0, (int)Math.Round(value * multiplier, MidpointRounding.AwayFromZero));
    }
}

public static class ProjectionModifierPolicyRegistry
{
    private static readonly HashSet<string> ExplicitConsumableBuffs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> AutoConsumableCache = new(StringComparer.Ordinal);
    private static long autoConsumableCacheEpoch = -1;

    public static void RegisterConsumable(string buffId)
    {
        if (!string.IsNullOrWhiteSpace(buffId))
        {
            ExplicitConsumableBuffs.Add(buffId.Trim());
        }
    }

    public static bool HasActiveConsumableModifier(IStatusManager? owner)
    {
        if (owner == null)
        {
            return false;
        }

        foreach (var buff in owner.GetBuffs() ?? Array.Empty<IBuffItem>())
        {
            var config = buff?.buffConfig;
            if (config == null || config.Level <= 0)
            {
                continue;
            }

            var buffId = config.BuffId ?? "";
            if (ExplicitConsumableBuffs.Contains(buffId) || IsAutoConsumable(buffId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAutoConsumable(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        var snapshot = AuraGameDataHostApi.AcquireSnapshot();
        if (snapshot.Version.NativeReady && autoConsumableCacheEpoch != snapshot.Version.Epoch)
        {
            AutoConsumableCache.Clear();
            autoConsumableCacheEpoch = snapshot.Version.Epoch;
        }

        if (snapshot.Version.NativeReady && AutoConsumableCache.TryGetValue(buffId, out var cached))
        {
            return cached;
        }

        var resolved = LooksConsumable(ConfigData(buffId));
        if (snapshot.Version.NativeReady)
        {
            AutoConsumableCache[buffId] = resolved;
        }
        return resolved;
    }

    private static bool LooksConsumable(IDictionary<string, string>? data)
    {
        if (data == null)
        {
            return false;
        }

        var script = string.Join("\n", data.Values ?? Enumerable.Empty<string>());
        var changesInheritedModifier = script.IndexOf("PercentDamage", StringComparison.Ordinal) >= 0
            || script.IndexOf("PercentDefence", StringComparison.Ordinal) >= 0
            || script.IndexOf("PercentHeal", StringComparison.Ordinal) >= 0;
        return changesInheritedModifier
            && script.IndexOf("RemoveBuff", StringComparison.Ordinal) >= 0
            && (script.IndexOf("Action", StringComparison.Ordinal) >= 0
                || script.IndexOf("Attack", StringComparison.Ordinal) >= 0
                || script.IndexOf("TrueUse", StringComparison.Ordinal) >= 0);
    }

    private static IDictionary<string, string>? ConfigData(string buffId)
    {
        try
        {
            return string.IsNullOrWhiteSpace(buffId) ? null : AuraGameDataHostApi.CopyRow(DataType.Buff, buffId);
        }
        catch
        {
            return null;
        }
    }
}

public static class ProjectionEffectContextService
{
    public static ProjectionEffectContext? Create(OtherObj? actor, CompanionBattleState? state)
    {
        if (actor == null || state == null)
        {
            return null;
        }

        var intent = CompanionIntentResolver.Find(state, state.CurrentPlan?.IntentId ?? state.CurrentIntentId);
        return Create(actor, state, intent);
    }

    private static ProjectionEffectContext? Create(
        OtherObj? actor,
        CompanionBattleState? state,
        CompanionIntentDefinition? intent)
    {
        if (actor == null || state == null)
        {
            return null;
        }

        var owner = StatusById(state.OwnerStatusId);
        return owner == null || intent == null
            ? null
            : new ProjectionEffectContext(actor, owner, owner, intent);
    }

    public static CompanionIntentPlan RefreshLockedPlan(
        OtherObj actor,
        CompanionBattleState state,
        CompanionIntentPlan plan)
    {
        var context = Create(actor, state, CompanionIntentResolver.Find(state, plan.IntentId));
        if (context == null || plan.IsWait)
        {
            return plan;
        }

        var intent = context.Intent;
        var previousEffects = plan.ResolvedEffects ?? new List<CompanionResolvedEffect>();
        var effectSpecs = CompanionIntentEffects.Expand(intent);
        var refreshed = new List<CompanionResolvedEffect>();
        for (var index = 0; index < effectSpecs.Count; index++)
        {
            var effectIntent = CompanionIntentEffects.AsDefinition(intent, effectSpecs[index]);
            if (!CompanionIntentHandlerRegistry.TryGet(effectIntent.HandlerId, out var handler))
            {
                return plan;
            }

            var committedIds = index < previousEffects.Count
                ? previousEffects[index].TargetIds
                : plan.OrderedTargetIds;
            var targets = CompanionTargetPolicyRegistry.Alive(committedIds).ToArray();
            if (targets.Length == 0)
            {
                return plan;
            }

            var effect = handler.Resolve(state, effectIntent, targets);
            effect.Value = context.ApplyStableModifier(effect.Value, EffectKind(effect.HandlerId));
            refreshed.Add(effect);
        }

        if (refreshed.Count == 0)
        {
            return plan;
        }

        plan.ResolvedEffects = refreshed;
        plan.ResolvedValue = refreshed[0].Value;
        plan.OrderedTargetIds = refreshed.SelectMany(effect => effect.TargetIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        plan.PreviewThreat = CompanionThreatService.CalculatePreview(
            intent,
            refreshed[0].Value,
            refreshed[0].RepeatCount);
        return plan;
    }

    public static bool IsOwnerAvailable(CompanionBattleState? state)
    {
        var owner = StatusById(state?.OwnerStatusId ?? "");
        return owner != null && owner.CurHp > 0 && owner.state != IStatusManager.State.Dead;
    }

    private static ProjectionEffectKind EffectKind(string handlerId)
    {
        if (string.Equals(handlerId, CompanionIntentHandlerRegistry.DamageSingle, StringComparison.Ordinal)
            || string.Equals(handlerId, CompanionIntentHandlerRegistry.DamageMulti, StringComparison.Ordinal)
            || string.Equals(handlerId, CompanionIntentHandlerRegistry.DamageAll, StringComparison.Ordinal))
        {
            return ProjectionEffectKind.Damage;
        }

        if (string.Equals(handlerId, CompanionIntentHandlerRegistry.BlockSingle, StringComparison.Ordinal)
            || string.Equals(handlerId, CompanionIntentHandlerRegistry.BlockAll, StringComparison.Ordinal))
        {
            return ProjectionEffectKind.Block;
        }

        return string.Equals(handlerId, CompanionIntentHandlerRegistry.HealSingle, StringComparison.Ordinal)
            ? ProjectionEffectKind.Heal
            : ProjectionEffectKind.Buff;
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }
}
