using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private static readonly AuraToolsLowHealthPresentationLatch Latch = new();
    private static long sequence;

    public static void AfterVocalState(ModHookContext context)
    {
        if (context?.Target is not IStatusManager status)
        {
            return;
        }

        var id = (status.InstanceId ?? "").Trim();
        var vocalState = context.Arguments?.FirstOrDefault()?.ToString() ?? "";
        if (!AuraToolsConfigService.SkillCg.Enabled
            || !IsLocalRole(status)
            || !string.Equals(vocalState, IStatusManager.VocalState.Dying.ToString(), StringComparison.Ordinal)
            || id.Length == 0)
        {
            return;
        }

        var after = Ratio(status);
        var roleId = AuraSharedIdentity.SelectRoleId(AuraToolsSkillCgRuntime.ReadCurrentCareerId());
        if (string.IsNullOrWhiteSpace(roleId) || !Latch.TryEnter(id)) return;
        var currentSequence = ++sequence;
        var signal = new AuraCgSignalContext
        {
            SignalId = AuraCgSignals.RoleLowHealthEntered,
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
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["resolvedCgId"] = AuraToolsRoleCgCatalog.ResolveSelectedCgId(
                    roleId,
                    AuraToolsRoleCgChannels.LowHealth)
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
        Latch.Reset();
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
