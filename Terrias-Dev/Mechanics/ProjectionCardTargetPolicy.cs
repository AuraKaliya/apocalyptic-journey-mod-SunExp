using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatAi.Shared.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public enum ProjectionCardTargetMode
{
    Self,
    SingleEnemy,
    SingleFriendly,
    AnySingleUnit,
    NoTarget
}

public sealed class ProjectionCardTargetDeclaration
{
    public string CardId { get; set; } = "";
    public ProjectionCardTargetMode Mode { get; set; } = ProjectionCardTargetMode.Self;
}

/// <summary>
/// Terrias owns legal target generation. The shared AI only scores the legal
/// per-target candidates produced here.
/// </summary>
public static class ProjectionCardTargetPolicy
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ProjectionCardTargetDeclaration> Declarations =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ProjectionCardTargetDeclaration declaration)
    {
        if (declaration == null || string.IsNullOrWhiteSpace(declaration.CardId))
        {
            return;
        }
        lock (SyncRoot)
        {
            Declarations[TerriasContentIdCompatibility.Canonicalize(declaration.CardId)] = declaration;
        }
    }

    public static ProjectionCardTargetMode Resolve(IDataConfig? config)
    {
        var id = TerriasContentIdCompatibility.Canonicalize(DictionaryUtil.Get(config?.data, "Id"));
        lock (SyncRoot)
        {
            if (Declarations.TryGetValue(id, out var declaration))
            {
                return declaration.Mode;
            }
        }

        var declared = DictionaryUtil.Get(config?.Vars, "TerriasProjectionTargetMode",
            DictionaryUtil.Get(config?.data, "TerriasProjectionTargetMode"));
        if (Enum.TryParse(declared, true, out ProjectionCardTargetMode parsed))
        {
            return parsed;
        }

        var baseScript = DictionaryUtil.Get(config?.Vars, "BaseScript");
        if (baseScript.EndsWith("AttackCardItem", StringComparison.Ordinal))
        {
            return DictionaryUtil.Get(config?.Vars, "CanSelf").Equals("True", StringComparison.OrdinalIgnoreCase)
                ? ProjectionCardTargetMode.AnySingleUnit
                : ProjectionCardTargetMode.SingleEnemy;
        }

        var enemySemantics = WitchCombatValueEstimator.Estimate(config, false, CombatTargetKind.Enemy);
        if (enemySemantics.Damage > 0d
            || enemySemantics.TrueDamage > 0d
            || enemySemantics.DamageOverTime > 0d
            || enemySemantics.Debuff > 0d)
        {
            return ProjectionCardTargetMode.SingleEnemy;
        }

        return ProjectionCardTargetMode.Self;
    }

    public static bool IsLegalTarget(
        ProjectionCardTargetMode mode,
        IStatusManager actor,
        IStatusManager? target,
        IReadOnlyCollection<IStatusManager> enemies,
        IReadOnlyCollection<IStatusManager> friendlies)
    {
        return mode switch
        {
            ProjectionCardTargetMode.Self => target == null || Same(actor, target),
            ProjectionCardTargetMode.NoTarget => target == null,
            ProjectionCardTargetMode.SingleEnemy => target != null && Contains(enemies, target),
            ProjectionCardTargetMode.SingleFriendly => target != null && Contains(friendlies, target),
            ProjectionCardTargetMode.AnySingleUnit => target == null
                                                       || Same(actor, target)
                                                       || Contains(enemies, target)
                                                       || Contains(friendlies, target),
            _ => false
        };
    }

    private static bool Contains(IEnumerable<IStatusManager> source, IStatusManager target)
    {
        return source.Any(value => Same(value, target));
    }

    private static bool Same(IStatusManager? left, IStatusManager? right)
    {
        return left != null && right != null
                            && string.Equals(left.InstanceId, right.InstanceId, StringComparison.Ordinal);
    }
}
