using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;

namespace Terrias.Dll.GameApi;

public static class SolarMemoryRoleCommitApi
{
    private static readonly object Gate = new();
    private static readonly SolarMemoryRoleCommitPendingState PendingState = new();
    private static Action<bool, string>? pendingCompletion;

    public static bool CommitFinal(RoleTable? role, string source)
    {
        return SubmitFinal(role, source, null) != SolarMemoryRoleCommitSubmission.Rejected;
    }

    public static SolarMemoryRoleCommitSubmission SubmitFinal(
        RoleTable? role,
        string source,
        Action<bool, string>? completion)
    {
        if (role == null)
        {
            TerriasLog.Warn("[SolarMemoryRoleCommit] submission skipped because RoleTable.Instance is null. source=" + source);
            return SolarMemoryRoleCommitSubmission.Rejected;
        }

        role.SpecialVarMap ??= new Dictionary<string, string>();
        var hasExistingToken = role.SpecialVarMap.TryGetValue(
            TerriasIds.SolarMemorySetupCommitTokenKey,
            out var token)
            && !string.IsNullOrWhiteSpace(token);
        if (!hasExistingToken)
        {
            token = Guid.NewGuid().ToString("N");
            role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey] = token;
        }
        else
        {
            TerriasLog.Info("[SolarMemoryRoleCommit] retrying idempotent submission. role="
                           + role.Id
                           + ", token="
                           + token);
        }

        var playerManager = PlayerManager.Instance;
        var isRemoteClient = playerManager != null && playerManager.isClient && !playerManager.isServer;
        try
        {
            if (isRemoteClient)
            {
                if (!PendingState.TryBegin(role.Id, token))
                {
                    role.SpecialVarMap.Remove(TerriasIds.SolarMemorySetupCommitTokenKey);
                    return SolarMemoryRoleCommitSubmission.Rejected;
                }

                lock (Gate)
                {
                    if (completion != null || pendingCompletion == null)
                    {
                        pendingCompletion = completion;
                    }
                }
            }

            if (RpcSolarMemoryRoleCommit.Submit(role, source))
            {
                return isRemoteClient
                    ? SolarMemoryRoleCommitSubmission.Pending
                    : SolarMemoryRoleCommitSubmission.Accepted;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar Memory final role submission failed. source=" + source, ex);
        }

        PendingState.Cancel(role.Id, token);
        lock (Gate)
        {
            pendingCompletion = null;
        }
        role.SpecialVarMap.Remove(TerriasIds.SolarMemorySetupCommitTokenKey);
        return SolarMemoryRoleCommitSubmission.Rejected;
    }

    internal static void ReceiveAuthoritativeResult(
        string playerId,
        string token,
        bool accepted,
        string rejectionReason)
    {
        var resolution = PendingState.Resolve(playerId, token, accepted);
        if (!resolution.Matched)
        {
            TerriasLog.Warn("[SolarMemoryRoleCommit] ignored unmatched host result. role="
                           + playerId
                           + ", token="
                           + token);
            return;
        }

        Action<bool, string>? completion;
        lock (Gate)
        {
            completion = pendingCompletion;
            pendingCompletion = null;
        }

        if (!resolution.Accepted)
        {
            var role = RoleTable.Instance;
            if (role?.SpecialVarMap != null
                && string.Equals(role.Id, playerId, StringComparison.Ordinal)
                && role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var localToken)
                && string.Equals(localToken, token, StringComparison.Ordinal))
            {
                role.SpecialVarMap.Remove(TerriasIds.SolarMemorySetupCommitTokenKey);
            }

            TerriasLog.Warn("[SolarMemoryRoleCommit] host rejected final role. role="
                           + playerId
                           + ", token="
                           + token
                           + ", reason="
                           + rejectionReason);
        }
        else
        {
            TerriasLog.Info("[SolarMemoryRoleCommit] host confirmed final role. role="
                           + playerId
                           + ", token="
                           + token);
        }

        completion?.Invoke(resolution.Accepted, rejectionReason ?? "");
    }
}

public enum SolarMemoryRoleCommitSubmission
{
    Rejected,
    Pending,
    Accepted
}
