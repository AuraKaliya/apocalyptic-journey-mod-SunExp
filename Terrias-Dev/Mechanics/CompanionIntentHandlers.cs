using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public interface ICompanionIntentHandler
{
    string HandlerId { get; }

    bool Validate(CompanionIntentDefinition intent, out string reason);

    CompanionResolvedEffect Resolve(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IReadOnlyList<IStatusManager> targets);

    void Execute(ScriptExecutor executor, CompanionResolvedEffect effect);

    void AddDescription(ScriptExecutor executor, CompanionResolvedEffect effect);
}

public static class CompanionIntentHandlerRegistry
{
    public const string DamageSingle = "damage.single";
    public const string DamageMulti = "damage.multi";
    public const string DamageAll = "damage.all";
    public const string BlockSingle = "block.single";
    public const string BlockAll = "block.all";
    public const string ApplyBuff = "buff.apply";
    public const string HealSingle = "heal.single";
    public const string PvpReserved = "pvp.reserved";

    private static readonly Dictionary<string, ICompanionIntentHandler> Handlers =
        new(StringComparer.Ordinal)
        {
            [DamageSingle] = new DamageHandler(DamageSingle, 1, "Single"),
            [DamageMulti] = new DamageHandler(DamageMulti, 2, "Single"),
            [DamageAll] = new DamageHandler(DamageAll, 1, "All"),
            [BlockSingle] = new BlockHandler(BlockSingle, "Single"),
            [BlockAll] = new BlockHandler(BlockAll, "All"),
            [ApplyBuff] = new BuffHandler(),
            [HealSingle] = new HealHandler(),
            [PvpReserved] = new ReservedPvpHandler()
        };

    public static bool TryGet(string? handlerId, out ICompanionIntentHandler handler)
    {
        return Handlers.TryGetValue(handlerId?.Trim() ?? "", out handler!);
    }

    public static bool Validate(CompanionIntentDefinition intent, out string reason)
    {
        if (intent == null)
        {
            reason = "intent is null";
            return false;
        }

        if (!TryGet(intent.HandlerId, out var handler))
        {
            reason = "unknown handler: " + (intent?.HandlerId ?? "");
            return false;
        }

        return handler.Validate(intent, out reason);
    }

    private sealed class DamageHandler : ICompanionIntentHandler
    {
        private readonly int minimumHits;
        private readonly string targetMode;

        public DamageHandler(string handlerId, int minimumHits, string targetMode)
        {
            HandlerId = handlerId;
            this.minimumHits = minimumHits;
            this.targetMode = targetMode;
        }

        public string HandlerId { get; }

        public bool Validate(CompanionIntentDefinition intent, out string reason)
        {
            if (!ValidateTarget(intent, "Enemy", targetMode, out reason))
            {
                return false;
            }

            if (intent.HitCount < minimumHits)
            {
                reason = HandlerId + " requires at least " + minimumHits + " hit(s).";
                return false;
            }

            reason = "";
            return true;
        }

        public CompanionResolvedEffect Resolve(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            IReadOnlyList<IStatusManager> targets)
        {
            return Effect(intent, targets, CompanionIntentExecutor.ResolveValue(state, intent), Math.Max(minimumHits, intent.HitCount));
        }

        public void Execute(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            foreach (var target in CompanionTargetPolicyRegistry.Alive(effect.TargetIds))
            {
                for (var hit = 0; hit < Math.Max(1, effect.RepeatCount); hit++)
                {
                    ExecutorApi.DealDamageToTarget(executor, target, effect.Value);
                    if (!CompanionTargetPolicyRegistry.IsAlive(target))
                    {
                        break;
                    }
                }
            }
        }

        public void AddDescription(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            executor.AddDescription("1", "Damage", Math.Max(0, effect.Value).ToString());
        }
    }

    private sealed class BlockHandler : ICompanionIntentHandler
    {
        private readonly string targetMode;

        public BlockHandler(string handlerId, string targetMode)
        {
            HandlerId = handlerId;
            this.targetMode = targetMode;
        }

        public string HandlerId { get; }

        public bool Validate(CompanionIntentDefinition intent, out string reason)
        {
            return ValidateTarget(intent, "Friendly", targetMode, out reason);
        }

        public CompanionResolvedEffect Resolve(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            IReadOnlyList<IStatusManager> targets)
        {
            return Effect(intent, targets, CompanionIntentExecutor.ResolveValue(state, intent));
        }

        public void Execute(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            foreach (var target in CompanionTargetPolicyRegistry.Alive(effect.TargetIds))
            {
                ExecutorApi.SetStatusForTarget(executor, target, "Self");
                executor.ChangeDefence(Math.Max(0, effect.Value).ToString());
            }
        }

        public void AddDescription(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            executor.AddDescription("1", "Defence", Math.Max(0, effect.Value).ToString());
        }
    }

    private sealed class BuffHandler : ICompanionIntentHandler
    {
        public string HandlerId => ApplyBuff;

        public bool Validate(CompanionIntentDefinition intent, out string reason)
        {
            if (string.IsNullOrWhiteSpace(intent.BuffId) || intent.BuffStacks <= 0)
            {
                reason = "buff.apply requires buffId and positive buffStacks.";
                return false;
            }

            if (!CompanionTargetPolicyRegistry.ValidateSpec(intent.Target, out reason))
            {
                return false;
            }

            reason = "";
            return true;
        }

        public CompanionResolvedEffect Resolve(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            IReadOnlyList<IStatusManager> targets)
        {
            var effect = Effect(intent, targets, intent.BuffStacks);
            effect.BuffId = intent.BuffId;
            effect.BuffStacks = intent.BuffStacks;
            return effect;
        }

        public void Execute(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            foreach (var target in CompanionTargetPolicyRegistry.Alive(effect.TargetIds))
            {
                ExecutorApi.AddStatusBuff(executor, target, effect.BuffId, effect.BuffStacks);
            }
        }

        public void AddDescription(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            executor.AddDescription("1", "Buff", Math.Max(0, effect.BuffStacks).ToString());
        }
    }

    private sealed class HealHandler : ICompanionIntentHandler
    {
        public string HandlerId => HealSingle;

        public bool Validate(CompanionIntentDefinition intent, out string reason)
        {
            return ValidateTarget(intent, "Friendly", "Single", out reason);
        }

        public CompanionResolvedEffect Resolve(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            IReadOnlyList<IStatusManager> targets)
        {
            return Effect(intent, targets, CompanionIntentExecutor.ResolveValue(state, intent));
        }

        public void Execute(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            var target = CompanionTargetPolicyRegistry.FirstAlive(effect.TargetIds);
            if (target == null)
            {
                return;
            }

            ExecutorApi.SetStatusForTarget(executor, target, "Self");
            executor.ChangeHp(Math.Max(0, effect.Value).ToString());
        }

        public void AddDescription(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            executor.AddDescription("1", "Value", Math.Max(0, effect.Value).ToString());
        }
    }

    private sealed class ReservedPvpHandler : ICompanionIntentHandler
    {
        public string HandlerId => PvpReserved;

        public bool Validate(CompanionIntentDefinition intent, out string reason)
        {
            return ValidateTarget(intent, "OpponentPlayer", "Single", out reason);
        }

        public CompanionResolvedEffect Resolve(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            IReadOnlyList<IStatusManager> targets)
        {
            return Effect(intent, Array.Empty<IStatusManager>(), 0);
        }

        public void Execute(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
            // Reserved until an authoritative hostile-player card-zone contract exists.
        }

        public void AddDescription(ScriptExecutor executor, CompanionResolvedEffect effect)
        {
        }
    }

    private static CompanionResolvedEffect Effect(
        CompanionIntentDefinition intent,
        IReadOnlyList<IStatusManager> targets,
        int value,
        int repeatCount = 1)
    {
        return new CompanionResolvedEffect
        {
            HandlerId = intent.HandlerId,
            TargetIds = targets.Where(CompanionTargetPolicyRegistry.IsAlive)
                .Select(target => target.InstanceId)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Value = Math.Max(0, value),
            RepeatCount = Math.Max(1, repeatCount)
        };
    }

    private static bool ValidateTarget(
        CompanionIntentDefinition intent,
        string scope,
        string mode,
        out string reason)
    {
        if (!string.Equals(intent.Target?.Scope, scope, StringComparison.Ordinal)
            || !string.Equals(intent.Target?.Mode, mode, StringComparison.Ordinal))
        {
            reason = intent.HandlerId + " requires target " + scope + "/" + mode + ".";
            return false;
        }

        return CompanionTargetPolicyRegistry.ValidateSpec(intent.Target, out reason);
    }
}

public static class CompanionTargetPolicyRegistry
{
    public const string EnemyLowestHp = "enemy.lowest_hp";
    public const string EnemyLowestBuffThenHp = "enemy.lowest_buff_then_lowest_hp";
    public const string EnemyAll = "enemy.all";
    public const string FriendlyOwnerOrSelfDefense = "friendly.owner_or_self_defense";
    public const string FriendlyAll = "friendly.all";
    public const string FriendlyMostWounded = "friendly.most_wounded";
    public const string Self = "self";
    public const string PvpOpponent = "pvp.opponent";

    private static readonly HashSet<string> KnownPolicies = new(StringComparer.Ordinal)
    {
        EnemyLowestHp,
        EnemyLowestBuffThenHp,
        EnemyAll,
        FriendlyOwnerOrSelfDefense,
        FriendlyAll,
        FriendlyMostWounded,
        Self,
        PvpOpponent
    };

    public static bool IsKnown(string? policy)
    {
        return KnownPolicies.Contains(policy?.Trim() ?? "");
    }

    public static bool ValidateSpec(CompanionIntentTargetSpec? target, out string reason)
    {
        if (target == null || !IsKnown(target.Policy))
        {
            reason = "unknown target policy: " + (target?.Policy ?? "");
            return false;
        }

        var expected = target.Policy switch
        {
            EnemyLowestHp => ("Enemy", "Single"),
            EnemyLowestBuffThenHp => ("Enemy", "Single"),
            EnemyAll => ("Enemy", "All"),
            FriendlyOwnerOrSelfDefense => ("Friendly", "Single"),
            FriendlyAll => ("Friendly", "All"),
            FriendlyMostWounded => ("Friendly", "Single"),
            Self => ("Self", "Single"),
            PvpOpponent => ("OpponentPlayer", "Single"),
            _ => ("", "")
        };
        if (!string.Equals(target.Scope, expected.Item1, StringComparison.Ordinal)
            || !string.Equals(target.Mode, expected.Item2, StringComparison.Ordinal))
        {
            reason = "target policy " + target.Policy + " requires " + expected.Item1 + "/" + expected.Item2 + ".";
            return false;
        }

        reason = "";
        return true;
    }

    public static IReadOnlyList<IStatusManager> Resolve(
        ScriptExecutor executor,
        CompanionBattleState state,
        CompanionIntentDefinition intent)
    {
        var policy = intent.Target?.Policy?.Trim() ?? "";
        switch (policy)
        {
            case EnemyLowestHp:
                return ExecutorApi.EnemyTargets(executor)
                    .Where(IsAlive)
                    .OrderBy(target => target.CurHp)
                    .ThenBy(target => target.InstanceId, StringComparer.Ordinal)
                    .Take(1)
                    .ToArray();
            case EnemyLowestBuffThenHp:
                return ExecutorApi.EnemyTargets(executor)
                    .Where(IsAlive)
                    .OrderBy(target => ExecutorApi.StatusBuffLevel(target, intent.BuffId))
                    .ThenBy(target => target.CurHp)
                    .ThenBy(target => target.InstanceId, StringComparer.Ordinal)
                    .Take(1)
                    .ToArray();
            case EnemyAll:
                return ExecutorApi.EnemyTargets(executor)
                    .Where(IsAlive)
                    .OrderBy(target => target.InstanceId, StringComparer.Ordinal)
                    .ToArray();
            case FriendlyOwnerOrSelfDefense:
                return ResolveDefenseTarget(executor, state);
            case FriendlyAll:
                return FriendlyStatuses().ToArray();
            case FriendlyMostWounded:
                return FriendlyStatuses()
                    .Where(target => target.CurHp < target.MaxHp)
                    .OrderBy(HpPercent)
                    .ThenBy(target => string.Equals(target.InstanceId, state.OwnerStatusId, StringComparison.Ordinal) ? 0 : 1)
                    .ThenBy(target => target.InstanceId, StringComparer.Ordinal)
                    .Take(1)
                    .ToArray();
            case Self:
                var owner = StatusById(state.OwnerStatusId);
                return IsAlive(owner) ? new[] { owner! } : Array.Empty<IStatusManager>();
            case PvpOpponent:
                return Array.Empty<IStatusManager>();
            default:
                return Array.Empty<IStatusManager>();
        }
    }

    public static bool IsValidCommittedTarget(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IStatusManager? target)
    {
        if (state == null || intent?.Target == null || !IsAlive(target))
        {
            return false;
        }

        switch (intent.Target.Scope)
        {
            case "Self":
                return string.Equals(target!.InstanceId, state.OwnerStatusId, StringComparison.Ordinal);
            case "Friendly":
                return CompanionFriendlyRosterService.Contains(target, includeControlled: true);
            case "Enemy":
                return !HeartChangeControlService.IsControlled(target)
                    && EnemyManager.Instance?.enemyList?.Any(enemy =>
                        enemy?.Status != null
                        && string.Equals(enemy.Status.InstanceId, target!.InstanceId, StringComparison.Ordinal)) == true;
            default:
                return false;
        }
    }

    public static IStatusManager? FirstAlive(IEnumerable<string>? targetIds)
    {
        return Alive(targetIds).FirstOrDefault();
    }

    public static IEnumerable<IStatusManager> Alive(IEnumerable<string>? targetIds)
    {
        foreach (var targetId in targetIds ?? Array.Empty<string>())
        {
            var target = StatusById(targetId);
            if (IsAlive(target))
            {
                yield return target!;
            }
        }
    }

    public static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private static IReadOnlyList<IStatusManager> ResolveDefenseTarget(ScriptExecutor executor, CompanionBattleState state)
    {
        var owner = StatusById(state.OwnerStatusId);
        if (IsAlive(owner) && HpPercent(owner!) <= 45)
        {
            return new[] { owner! };
        }

        return IsAlive(owner) ? new[] { owner! } : Array.Empty<IStatusManager>();
    }

    private static IEnumerable<IStatusManager> FriendlyStatuses()
    {
        return CompanionFriendlyRosterService.Snapshot(includeControlled: true)
            .Where(IsAlive)
            .OrderBy(target => target.InstanceId, StringComparer.Ordinal);
    }

    private static IStatusManager? StatusById(string? statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }

    private static int HpPercent(IStatusManager status)
    {
        return status.MaxHp <= 0
            ? 100
            : Math.Max(0, Math.Min(100, status.CurHp * 100 / Math.Max(1, status.MaxHp)));
    }
}
