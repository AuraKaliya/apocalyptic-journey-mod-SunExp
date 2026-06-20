using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;
using Witch;

namespace SunExp.Dll.GameApi;

public static class SolarMemoryRoleCommitApi
{
    public static bool CommitFinal(RoleTable? role, string source)
    {
        if (role == null)
        {
            SunExpLog.Warn("[SolarMemoryRoleCommit] submission skipped because RoleTable.Instance is null. source=" + source);
            return false;
        }

        role.SpecialVarMap ??= new Dictionary<string, string>();
        if (role.SpecialVarMap.TryGetValue(SunExpIds.SolarMemorySetupCommitTokenKey, out var existingToken)
            && !string.IsNullOrWhiteSpace(existingToken))
        {
            SunExpLog.Info("[SolarMemoryRoleCommit] duplicate local submission ignored. role=" + role.Id + ", token=" + existingToken);
            return true;
        }

        var token = Guid.NewGuid().ToString("N");
        role.SpecialVarMap[SunExpIds.SolarMemorySetupCommitTokenKey] = token;
        try
        {
            var playerManager = PlayerManager.Instance;
            if (playerManager != null && playerManager.isClient && !playerManager.isServer)
            {
                playerManager.SendRpcCommand(new RpcSolarMemoryRoleCommit(role, source));
                SunExpLog.Info("[SolarMemoryRoleCommit] submitted final role to host. role=" + role.Id + ", token=" + token + ", source=" + source);
                return true;
            }

            if (RpcSolarMemoryRoleCommit.ApplyOnServer(role, source))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar Memory final role submission failed. source=" + source, ex);
        }

        role.SpecialVarMap.Remove(SunExpIds.SolarMemorySetupCommitTokenKey);
        return false;
    }
}
