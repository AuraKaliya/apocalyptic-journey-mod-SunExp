using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Network.Command;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.Network;

public sealed class RpcSolarMemoryRoleCommit : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private static readonly HashSet<string> CommittedTokens = new(StringComparer.Ordinal);
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

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

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        ApplyOnServer(Role, Source, serverSender, remoteRpc: true);
    }

    public override void RpcExecute()
    {
        // The authoritative role has already been committed by the server.
    }

    internal static bool ApplyOnServer(RoleTable? role, string source)
    {
        return ApplyOnServer(role, source, SunExpRpcSender.Unbound, remoteRpc: false);
    }

    internal static bool ApplyOnServer(
        RoleTable? role,
        string source,
        SunExpRpcSender sender,
        bool remoteRpc)
    {
        var claimedToken = "";
        try
        {
            if (role == null || string.IsNullOrWhiteSpace(role.Id))
            {
                SunExpLog.Warn("[SolarMemoryRoleCommit] rejected empty role. source=" + source);
                return false;
            }

            if (!ValidateSender(role, source, sender, remoteRpc))
            {
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

    private static bool ValidateSender(
        RoleTable role,
        string source,
        SunExpRpcSender? sender,
        bool remoteRpc)
    {
        sender ??= SunExpRpcSender.Unbound;
        if ((remoteRpc || IsMultiplayerLobby()) && !sender.IsAvailable)
        {
            SunExpLog.Warn("[SolarMemoryRoleCommit] rejected missing server sender. role=" + role.Id + ", source=" + source);
            return false;
        }

        if (sender.IsAvailable && !sender.IsLobbyMember)
        {
            SunExpLog.Warn("[SolarMemoryRoleCommit] rejected sender outside lobby. role="
                           + role.Id
                           + ", sender="
                           + sender.PlayerId
                           + ", source="
                           + source);
            return false;
        }

        if (sender.IsAvailable
            && !string.Equals(role.Id, sender.PlayerId, StringComparison.Ordinal))
        {
            SunExpLog.Warn("[SolarMemoryRoleCommit] rejected sender mismatch. role="
                           + role.Id
                           + ", sender="
                           + sender.PlayerId
                           + ", source="
                           + source);
            return false;
        }

        return true;
    }

    private static bool IsMultiplayerLobby()
    {
        var players = global::GameServer.Instance?.LobbyInfo?.AddedPlayers;
        if (players != null && players.Count > 1)
        {
            return true;
        }

        var playerManager = PlayerManager.Instance;
        return playerManager != null && (playerManager.isClient || playerManager.isServer);
    }
}
