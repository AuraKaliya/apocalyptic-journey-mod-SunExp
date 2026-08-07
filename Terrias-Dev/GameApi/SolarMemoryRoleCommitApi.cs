using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.GameApi;

public static class SolarMemoryRoleCommitApi
{
    public static bool CommitFinal(RoleTable? role, string source)
    {
        if (role == null)
        {
            TerriasLog.Warn("[SolarMemoryRoleCommit] submission skipped because RoleTable.Instance is null. source=" + source);
            return false;
        }

        role.SpecialVarMap ??= new Dictionary<string, string>();
        if (role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var existingToken)
            && !string.IsNullOrWhiteSpace(existingToken))
        {
            TerriasLog.Info("[SolarMemoryRoleCommit] duplicate local submission ignored. role=" + role.Id + ", token=" + existingToken);
            return true;
        }

        var token = Guid.NewGuid().ToString("N");
        role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey] = token;
        try
        {
            if (RpcSolarMemoryRoleCommit.Submit(role, source))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar Memory final role submission failed. source=" + source, ex);
        }

        role.SpecialVarMap.Remove(TerriasIds.SolarMemorySetupCommitTokenKey);
        return false;
    }
}
