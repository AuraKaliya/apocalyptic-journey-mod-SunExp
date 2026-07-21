using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class RoleActionAnimationRuntime
{
    private static readonly Stack<EffectPatch> EffectPatches = new();

    public static void Initialize(ModConfig modConfig)
    {
        TerriasCombatActionRouter.Register("RoleActionAnimation", new TerriasCombatActionSubscription
        {
            BeforeFightUiActionAnimation = BeforeCallActionAnimation,
            AfterFightUiActionAnimation = AfterCallActionAnimation
        });
    }

    private static void BeforeCallActionAnimation(ModHookContext context)
    {
        try
        {
            var executor = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as IScriptExecutor
                : null;
            if (!ShouldNormalize(executor))
            {
                return;
            }

            NormalizeTargets(executor!);
            var currentRoleId = ReadCurrentRoleId();
            var ownerRoleId = ReadOwnerRoleId(executor);
            if (RoleActionPresentationCatalog.UsesWunaEffectNormalization(currentRoleId, ownerRoleId))
            {
                NormalizeExplicitEffects(executor!);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("Role action animation normalization failed: " + ex.Message);
        }
    }

    private static void AfterCallActionAnimation(ModHookContext context)
    {
        try
        {
            if (EffectPatches.Count == 0)
            {
                return;
            }

            var executor = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as IScriptExecutor
                : null;
            var patch = EffectPatches.Peek();
            if (!ReferenceEquals(patch.DataConfig, executor?.dataConfig))
            {
                return;
            }

            EffectPatches.Pop();
            patch.DataConfig.data["Effects"] = patch.OriginalEffects;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("Role action animation effect restore failed: " + ex.Message);
        }
    }

    private static bool ShouldNormalize(IScriptExecutor? executor)
    {
        var dataConfig = executor?.dataConfig;
        if (executor?.Self == null || dataConfig?.data == null || dataConfig.Type != DataType.Card)
        {
            return false;
        }

        var currentRoleId = ReadCurrentRoleId();
        var ownerRoleId = ReadOwnerRoleId(executor);
        if (!RoleActionPresentationCatalog.SupportsRole(currentRoleId, ownerRoleId))
        {
            return false;
        }

        var action = ReadData(dataConfig, "Action");
        return string.Equals(action, "Attack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Skill", StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizeTargets(IScriptExecutor executor)
    {
        var dataConfig = executor.dataConfig;
        var cardId = NormalizeCardId(ReadData(dataConfig, "Id"));
        var action = ReadData(dataConfig, "Action");
        var targetMode = RoleActionPresentationCatalog.TargetMode(cardId);
        var self = executor.Self;
        var currentTargets = executor.Object;
        if (currentTargets == null)
        {
            currentTargets = new List<IStatusManager>();
            executor.Object = currentTargets;
        }

        var before = DescribeTargets(currentTargets);
        if (targetMode == RoleActionTargetMode.AllOpponents)
        {
            executor.SetStatus("AllTarget");
            LogTargetNormalized(dataConfig, action, "all-opponents", before, DescribeTargets(executor.Object));
            return;
        }

        if (targetMode == RoleActionTargetMode.SelfOnly)
        {
            currentTargets.Clear();
            LogTargetNormalized(dataConfig, action, "self-only", before, DescribeTargets(currentTargets));
            return;
        }

        var hasNonSelfTarget = currentTargets.Any(target => IsValidNonSelfTarget(self, target));
        if (hasNonSelfTarget)
        {
            currentTargets.RemoveAll(target => !IsValidNonSelfTarget(self, target));
            var after = DescribeTargets(currentTargets);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                LogTargetNormalized(dataConfig, action, "remove-self-target", before, after);
            }

            return;
        }

        if (IsValidNonSelfTarget(self, executor.Target))
        {
            currentTargets.Clear();
            currentTargets.Add(executor.Target);
            LogTargetNormalized(dataConfig, action, "primary-target", before, DescribeTargets(currentTargets));
            return;
        }

        // Self-only actions should not play target-side hit effects on the actor.
        if (currentTargets.Count == 1 && ReferenceEquals(currentTargets[0], self))
        {
            currentTargets.Clear();
            LogTargetNormalized(dataConfig, action, "clear-self-only", before, DescribeTargets(currentTargets));
        }
    }

    private static void NormalizeExplicitEffects(IScriptExecutor executor)
    {
        var dataConfig = executor.dataConfig;
        var effects = ReadData(dataConfig, "Effects");
        if (string.IsNullOrWhiteSpace(effects))
        {
            return;
        }

        var normalized = string.Join(",", effects
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeEffectName)
            .Where(item => !string.IsNullOrWhiteSpace(item)));
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(effects, normalized, StringComparison.Ordinal))
        {
            return;
        }

        EffectPatches.Push(new EffectPatch(dataConfig, effects));
        dataConfig.data["Effects"] = normalized;
        TerriasLog.Info("Role action effect normalized: card=" + ReadData(dataConfig, "Id")
            + ", action=" + ReadData(dataConfig, "Action")
            + ", from=" + effects + ", to=" + normalized);
    }

    private static string NormalizeEffectName(string effect)
    {
        var value = (effect ?? "").Trim();
        if (string.Equals(value, "slash", StringComparison.OrdinalIgnoreCase))
        {
            return "Hit";
        }

        return value;
    }

    private static bool IsValidNonSelfTarget(IStatusManager? self, IStatusManager? target)
    {
        return self != null
            && target != null
            && !string.IsNullOrWhiteSpace(target.InstanceId)
            && !string.Equals(self.InstanceId, target.InstanceId, StringComparison.Ordinal);
    }

    private static string ReadCurrentRoleId()
    {
        return ReadData(RoleTable.Instance?.Career ?? GameEntryUI.career, "Id");
    }

    private static string ReadOwnerRoleId(IScriptExecutor? executor)
    {
        try
        {
            var owner = executor?.Self as StatusManager;
            return AuraSharedReflection.ReadString(owner?.fatherObject, "Id", "id");
        }
        catch
        {
            return "";
        }
    }

    private static void LogTargetNormalized(
        IDataConfig dataConfig,
        string action,
        string reason,
        string before,
        string after)
    {
        TerriasLog.Info("Role action target normalized: card=" + ReadData(dataConfig, "Id")
            + ", action=" + action
            + ", reason=" + reason
            + ", before=" + before
            + ", after=" + after);
    }

    private static string DescribeTargets(IEnumerable<IStatusManager>? targets)
    {
        if (targets == null)
        {
            return "none";
        }

        var values = targets
            .Select(DescribeTarget)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return values.Count == 0 ? "none" : string.Join("|", values);
    }

    private static string DescribeTarget(IStatusManager? target)
    {
        if (target == null)
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(target.InstanceId) ? "unknown" : target.InstanceId;
    }

    private static string ReadData(IDataConfig? dataConfig, string key)
    {
        try
        {
            return dataConfig?.data != null && dataConfig.data.TryGetValue(key, out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeCardId(string value)
    {
        return (value ?? "").Trim();
    }

    private readonly struct EffectPatch
    {
        public EffectPatch(IDataConfig dataConfig, string originalEffects)
        {
            DataConfig = dataConfig;
            OriginalEffects = originalEffects;
        }

        public IDataConfig DataConfig { get; }

        public string OriginalEffects { get; }
    }
}
