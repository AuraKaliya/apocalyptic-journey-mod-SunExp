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
    NoTarget,
    AllEnemies,
    AllFriendlies,
    RandomEnemyN,
    RandomFriendlyN,
    DeclaredTargetSet
}

public sealed class ProjectionCardTargetDeclaration
{
    public string CardId { get; set; } = "";
    public ProjectionCardTargetMode Mode { get; set; } = ProjectionCardTargetMode.Self;
    public int Count { get; set; } = 1;
    public bool IncludeSelf { get; set; }
    public string SetKinds { get; set; } = "";
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

    public static ProjectionCardTargetDeclaration ResolveDeclaration(IDataConfig? config)
    {
        var id = TerriasContentIdCompatibility.Canonicalize(DictionaryUtil.Get(config?.data, "Id"));
        lock (SyncRoot)
        {
            if (Declarations.TryGetValue(id, out var declaration))
            {
                return declaration;
            }
        }
        var countText = DictionaryUtil.Get(
            config?.Vars,
            "TerriasProjectionTargetCount",
            DictionaryUtil.Get(config?.data, "TerriasProjectionTargetCount", "1"));
        return new ProjectionCardTargetDeclaration
        {
            CardId = id,
            Mode = Resolve(config),
            Count = int.TryParse(countText, out var count) ? Math.Max(1, count) : 1,
            IncludeSelf = DictionaryUtil.Get(
                    config?.Vars,
                    "TerriasProjectionIncludeSelf",
                    DictionaryUtil.Get(config?.data, "TerriasProjectionIncludeSelf"))
                .Equals("True", StringComparison.OrdinalIgnoreCase),
            SetKinds = DictionaryUtil.Get(
                config?.Vars,
                "TerriasProjectionTargetSet",
                DictionaryUtil.Get(config?.data, "TerriasProjectionTargetSet"))
        };
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
            ProjectionCardTargetMode.AllEnemies or ProjectionCardTargetMode.RandomEnemyN =>
                target != null && Contains(enemies, target),
            ProjectionCardTargetMode.AllFriendlies or ProjectionCardTargetMode.RandomFriendlyN =>
                target != null && (Same(actor, target) || Contains(friendlies, target)),
            ProjectionCardTargetMode.DeclaredTargetSet => target != null,
            _ => false
        };
    }

    public static bool IsLegalTargetSet(
        ProjectionCardTargetDeclaration declaration,
        IStatusManager actor,
        IReadOnlyCollection<IStatusManager> targets,
        IReadOnlyCollection<IStatusManager> enemies,
        IReadOnlyCollection<IStatusManager> friendlies)
    {
        var mode = declaration.Mode;
        var values = targets;
        if (mode == ProjectionCardTargetMode.NoTarget) return values.Count == 0;
        if (mode == ProjectionCardTargetMode.Self) return values.Count == 1 && Same(actor, values.First());
        if (mode == ProjectionCardTargetMode.AllEnemies)
            return values.Count == enemies.Count && values.All(target => Contains(enemies, target));
        if (mode == ProjectionCardTargetMode.AllFriendlies)
        {
            var expected = friendlies.Count + (declaration.IncludeSelf ? 1 : 0);
            return values.Count == expected
                   && values.All(target => Same(actor, target) || Contains(friendlies, target));
        }
        if (mode == ProjectionCardTargetMode.RandomEnemyN)
            return values.Count > 0 && values.Count <= declaration.Count
                                    && values.All(target => Contains(enemies, target));
        if (mode == ProjectionCardTargetMode.RandomFriendlyN)
            return values.Count > 0 && values.Count <= declaration.Count
                                    && values.All(target => Same(actor, target) || Contains(friendlies, target));
        if (mode == ProjectionCardTargetMode.DeclaredTargetSet)
            return values.Count > 0 && values.All(target => Same(actor, target)
                                                      || Contains(enemies, target)
                                                      || Contains(friendlies, target));
        return values.Count == 1
               && IsLegalTarget(mode, actor, values.First(), enemies, friendlies);
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
