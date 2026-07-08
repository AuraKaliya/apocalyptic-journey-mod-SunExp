using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class WunaActionAnimationRuntime
{
    private static readonly HashSet<string> AllTargetActionCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "blazing_crown_collapse",
        "crown_radiance",
        "canopy_return",
        "*canopy_return",
        "solar_ignition",
        "flamewheel_recurrence"
    };

    private static readonly Stack<EffectPatch> EffectPatches = new();

    public static void Initialize(ModConfig modConfig)
    {
        SunExpCombatActionRouter.Register("WunaActionAnimation", new SunExpCombatActionSubscription
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
            NormalizeExplicitEffects(executor!);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Wuna action animation normalization failed: " + ex.Message);
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
            SunExpLog.Warn("Wuna action animation effect restore failed: " + ex.Message);
        }
    }

    private static bool ShouldNormalize(IScriptExecutor? executor)
    {
        var dataConfig = executor?.dataConfig;
        if (executor?.Self == null || dataConfig?.data == null || dataConfig.Type != DataType.Card)
        {
            return false;
        }

        if (!IsWunaCareer(executor))
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
        var self = executor.Self;
        var currentTargets = executor.Object;
        if (currentTargets == null)
        {
            currentTargets = new List<IStatusManager>();
            executor.Object = currentTargets;
        }

        var before = DescribeTargets(currentTargets);
        var hasNonSelfTarget = currentTargets.Any(target => IsValidNonSelfTarget(self, target));
        if (hasNonSelfTarget)
        {
            return;
        }

        if (AllTargetActionCards.Contains(cardId))
        {
            executor.SetStatus("AllTarget");
            LogTargetNormalized(dataConfig, action, "all-target", before, DescribeTargets(executor.Object));
            return;
        }

        if (IsValidNonSelfTarget(self, executor.Target))
        {
            currentTargets.Clear();
            currentTargets.Add(executor.Target);
            LogTargetNormalized(dataConfig, action, "primary-target", before, DescribeTargets(currentTargets));
            return;
        }

        // Self-only skills should not play target-side hit effects on WuNa.
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
        SunExpLog.Info("Wuna action effect normalized: card=" + ReadData(dataConfig, "Id")
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

    private static bool IsWunaCareer(IScriptExecutor? executor)
    {
        var current = ReadData(RoleTable.Instance?.Career ?? GameEntryUI.career, "Id");
        if (IsWunaRoleId(current))
        {
            return true;
        }

        var ownerId = ReadOwnerRoleId(executor);
        var selected = AuraSharedIdentity.SelectRoleId(ownerId, current);
        return IsWunaRoleId(ownerId) || IsWunaRoleId(selected);
    }

    private static bool IsWunaRoleId(string value)
    {
        var normalized = AuraSharedIdentity.NormalizeRoleId(value).TrimStart('*').Trim();
        return string.Equals(normalized, "wuna", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "SunExp_wuna_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(":wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".wuna", StringComparison.OrdinalIgnoreCase);
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
        SunExpLog.Info("Wuna action target normalized: card=" + ReadData(dataConfig, "Id")
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
