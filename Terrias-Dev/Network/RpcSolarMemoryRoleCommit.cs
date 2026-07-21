using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Network.Command;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Network;

public sealed class RpcSolarMemoryRoleCommit : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private static readonly HashSet<string> CommittedTokens = new(StringComparer.Ordinal);
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

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

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
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
        return ApplyOnServer(role, source, TerriasRpcSender.Unbound, remoteRpc: false);
    }

    internal static bool ApplyOnServer(
        RoleTable? role,
        string source,
        TerriasRpcSender sender,
        bool remoteRpc)
    {
        var claimedToken = "";
        try
        {
            if (role == null || string.IsNullOrWhiteSpace(role.Id))
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected empty role. source=" + source);
                return false;
            }

            if (!ValidateSender(role, source, sender, remoteRpc))
            {
                return false;
            }

            if (GameSaveManager.GetValue<string>(TerriasIds.SolarMemoryModeKey) != "1")
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected outside Solar Memory mode. role=" + role.Id + ", source=" + source);
                return false;
            }

            if (role.SpecialVarMap == null
                || !role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupFinishedKey, out var setupFinished)
                || setupFinished != "1")
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected unfinished setup. role=" + role.Id + ", source=" + source);
                return false;
            }

            if (!role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var commitToken)
                || string.IsNullOrWhiteSpace(commitToken))
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected missing commit token. role=" + role.Id + ", source=" + source);
                return false;
            }

            var server = global::GameServer.Instance;
            if (server != null)
            {
                var players = server.LobbyInfo?.AddedPlayers;
                if (players != null && players.Count > 0 && !players.Any(player => player.Id == role.Id))
                {
                    TerriasLog.Warn("[SolarMemoryRoleCommit] rejected role outside lobby. role=" + role.Id + ", source=" + source);
                    return false;
                }
            }

            if (!CommittedTokens.Add(commitToken))
            {
                TerriasLog.Info("[SolarMemoryRoleCommit] duplicate network commit ignored. role=" + role.Id + ", token=" + commitToken);
                return true;
            }

            claimedToken = commitToken;
            if (server != null)
            {
                server.RoleTables[role.Id] = role;
            }

            GameSaveManager.UpdateRoles(role);
            TerriasLog.Info("[SolarMemoryRoleCommit] committed final role. role=" + role.Id + ", token=" + commitToken + ", source=" + source);
            return true;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(claimedToken))
            {
                CommittedTokens.Remove(claimedToken);
            }

            TerriasLog.Error("Solar Memory final role commit failed. source=" + source, ex);
            return false;
        }
    }

    private static bool ValidateSender(
        RoleTable role,
        string source,
        TerriasRpcSender? sender,
        bool remoteRpc)
    {
        sender ??= TerriasRpcSender.Unbound;
        if ((remoteRpc || IsMultiplayerLobby()) && !sender.IsAvailable)
        {
            TerriasLog.Warn("[SolarMemoryRoleCommit] rejected missing server sender. role=" + role.Id + ", source=" + source);
            return false;
        }

        if (sender.IsAvailable && !sender.IsLobbyMember)
        {
            TerriasLog.Warn("[SolarMemoryRoleCommit] rejected sender outside lobby. role="
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
            TerriasLog.Warn("[SolarMemoryRoleCommit] rejected sender mismatch. role="
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
