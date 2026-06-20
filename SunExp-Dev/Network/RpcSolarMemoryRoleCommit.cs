using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Network.Command;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Network;

public sealed class RpcSolarMemoryRoleCommit : RpcCommandBase
{
    private static readonly HashSet<string> CommittedTokens = new(StringComparer.Ordinal);

    public RoleTable? Role { get; set; }

    public string Source { get; set; } = "";

    public RpcSolarMemoryRoleCommit()
    {
    }

    public RpcSolarMemoryRoleCommit(RoleTable role, string source)
    {
        Role = role;
        Source = source ?? "";
    }

    public override void CmdExecute()
    {
        ApplyOnServer(Role, Source);
    }

    public override void RpcExecute()
    {
        // The authoritative role has already been committed by the server.
    }

    internal static bool ApplyOnServer(RoleTable? role, string source)
    {
        var claimedToken = "";
        try
        {
            if (role == null || string.IsNullOrWhiteSpace(role.Id))
            {
                SunExpLog.Warn("[SolarMemoryRoleCommit] rejected empty role. source=" + source);
                return false;
            }

            if (GameSaveManager.GetValue<string>(SunExpIds.SolarMemoryModeKey) != "1")
            {
                SunExpLog.Warn("[SolarMemoryRoleCommit] rejected outside Solar Memory mode. role=" + role.Id + ", source=" + source);
                return false;
            }

            if (role.SpecialVarMap == null
                || !role.SpecialVarMap.TryGetValue(SunExpIds.SolarMemorySetupFinishedKey, out var setupFinished)
                || setupFinished != "1")
            {
                SunExpLog.Warn("[SolarMemoryRoleCommit] rejected unfinished setup. role=" + role.Id + ", source=" + source);
                return false;
            }

            if (!role.SpecialVarMap.TryGetValue(SunExpIds.SolarMemorySetupCommitTokenKey, out var commitToken)
                || string.IsNullOrWhiteSpace(commitToken))
            {
                SunExpLog.Warn("[SolarMemoryRoleCommit] rejected missing commit token. role=" + role.Id + ", source=" + source);
                return false;
            }

            var server = global::GameServer.Instance;
            if (server != null)
            {
                var players = server.LobbyInfo?.AddedPlayers;
                if (players != null && players.Count > 0 && !players.Any(player => player.Id == role.Id))
                {
                    SunExpLog.Warn("[SolarMemoryRoleCommit] rejected role outside lobby. role=" + role.Id + ", source=" + source);
                    return false;
                }
            }

            if (!CommittedTokens.Add(commitToken))
            {
                SunExpLog.Info("[SolarMemoryRoleCommit] duplicate network commit ignored. role=" + role.Id + ", token=" + commitToken);
                return true;
            }

            claimedToken = commitToken;
            if (server != null)
            {
                server.RoleTables[role.Id] = role;
            }

            GameSaveManager.UpdateRoles(role);
            SunExpLog.Info("[SolarMemoryRoleCommit] committed final role. role=" + role.Id + ", token=" + commitToken + ", source=" + source);
            return true;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(claimedToken))
            {
                CommittedTokens.Remove(claimedToken);
            }

            SunExpLog.Error("Solar Memory final role commit failed. source=" + source, ex);
            return false;
        }
    }
}
