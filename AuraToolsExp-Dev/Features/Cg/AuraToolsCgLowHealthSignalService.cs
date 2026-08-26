using System;
using System.Collections.Generic;
using System.Globalization;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Cg;

internal static class AuraToolsCgLowHealthSignalService
{
    private static readonly Dictionary<string, float> BeforeRatios =
        new(StringComparer.Ordinal);
    private static long sequence;

    public static void BeforeCurHpChanged(ModHookContext context)
    {
        if (!AuraToolsConfigService.SkillCg.Enabled
            || context?.Target is not IStatusManager status
            || !IsLocalRole(status))
        {
            return;
        }

        var id = (status.InstanceId ?? "").Trim();
        if (id.Length == 0) return;
        BeforeRatios[id] = Ratio(status);
    }

    public static void AfterCurHpChanged(ModHookContext context)
    {
        if (context?.Target is not IStatusManager status)
        {
            return;
        }

        var id = (status.InstanceId ?? "").Trim();
        if (!AuraToolsConfigService.SkillCg.Enabled || !IsLocalRole(status))
        {
            BeforeRatios.Remove(id);
            return;
        }

        var before = BeforeRatios.TryGetValue(id, out var remembered) ? remembered : 1f;
        BeforeRatios.Remove(id);
        var after = Ratio(status);
        var threshold = AuraToolsConfigService.SkillCg.LowHealthThreshold;
        if (status.CurHp <= 0 || before <= threshold || after > threshold)
        {
            return;
        }

        var roleId = AuraSharedIdentity.SelectRoleId(AuraToolsSkillCgRuntime.ReadCurrentCareerId());
        if (string.IsNullOrWhiteSpace(roleId)) return;
        var currentSequence = ++sequence;
        var signal = new AuraCgSignalContext
        {
            SignalId = AuraCgSignals.RoleLowHealthCrossedDown,
            SubjectType = AuraCgSubjectTypes.Role,
            SubjectId = roleId,
            RoleId = roleId,
            OwnerInstanceId = id,
            ActionSequence = currentSequence,
            EventToken = "role-low-health:"
                         + id + ":"
                         + currentSequence.ToString(CultureInfo.InvariantCulture),
            CreatedAt = Time.unscaledTime,
            Metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["healthRatio"] = after
            },
            ConfigureResolvedRequest = request =>
            {
                request.DisableSync = !AuraToolsConfigService.SkillCg.SyncRemote;
                AuraToolsSkillCgRuntime.ApplyRegisteredSkillPresentationOverride(request);
            }
        };
        SkillCgArbiterRuntime.EmitSignal(
            AuraToolsConfigService.SkillCg,
            AuraToolsIds.ModId,
            signal);
    }

    public static void Reset()
    {
        BeforeRatios.Clear();
    }

    private static bool IsLocalRole(IStatusManager status)
    {
        var local = FightPlayer.Instance?.Status;
        return local != null
               && (ReferenceEquals(local, status)
                   || string.Equals(local.InstanceId, status.InstanceId, StringComparison.Ordinal));
    }

    private static float Ratio(IStatusManager status)
    {
        return status.MaxHp <= 0
            ? 0f
            : Math.Max(0f, Math.Min(1f, (float)status.CurHp / status.MaxHp));
    }
}
