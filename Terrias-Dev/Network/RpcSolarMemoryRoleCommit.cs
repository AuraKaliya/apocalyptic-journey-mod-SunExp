using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Network.Command;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcSolarMemoryRoleCommit : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private static readonly HashSet<string> CommittedTokens = new(StringComparer.Ordinal);
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public RoleTable? Role { get; set; }

    public string Source { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public string CommitToken { get; set; } = "";

    public bool Accepted { get; set; }

    public string RejectionReason { get; set; } = "";

    public RpcSolarMemoryRoleCommit()
    {
    }

    public RpcSolarMemoryRoleCommit(RoleTable role, string source)
    {
        Role = role;
        Source = source ?? "";
        PlayerId = role?.Id ?? "";
        if (role?.SpecialVarMap != null
            && role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var token))
        {
            CommitToken = token ?? "";
        }
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        var role = Role;
        if (serverSender.IsAvailable)
        {
            PlayerId = serverSender.PlayerId;
        }
        else if (role != null && !string.IsNullOrWhiteSpace(role.Id))
        {
            PlayerId = role.Id;
        }

        if (role?.SpecialVarMap != null
            && role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var roleToken))
        {
            CommitToken = roleToken ?? "";
        }

        Accepted = ApplyOnServer(role, Source, serverSender, remoteRpc: true, out var rejectionReason);
        RejectionReason = rejectionReason;
    }

    public override void RpcExecute()
    {
        var playerManager = PlayerManager.Instance;
        if (playerManager == null
            || !string.Equals(PlayerId, playerManager.PlayerId, StringComparison.Ordinal))
        {
            return;
        }

        SolarMemoryRoleCommitApi.ReceiveAuthoritativeResult(
            PlayerId,
            CommitToken,
            Accepted,
            RejectionReason);
    }

    internal static bool Submit(RoleTable role, string source)
    {
        var playerManager = PlayerManager.Instance;
        if (playerManager != null && playerManager.isClient && !playerManager.isServer)
        {
            playerManager.SendRpcCommand(new RpcSolarMemoryRoleCommit(role, source));
            TerriasLog.Info("[SolarMemoryRoleCommit] submitted final role to host. role="
                           + role.Id
                           + ", token="
                           + role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey]
                           + ", source="
                           + source);
            return true;
        }

        return ApplyOnServer(
            role,
            source,
            TerriasRpcAuthorityRuntime.CreateLocalServerSender(source),
            remoteRpc: false,
            out _);
    }

    internal static bool ApplyOnServer(RoleTable? role, string source)
    {
        return ApplyOnServer(role, source, TerriasRpcSender.Unbound, remoteRpc: false, out _);
    }

    internal static bool ApplyOnServer(
        RoleTable? role,
        string source,
        TerriasRpcSender sender,
        bool remoteRpc)
    {
        return ApplyOnServer(role, source, sender, remoteRpc, out _);
    }

    internal static bool ApplyOnServer(
        RoleTable? role,
        string source,
        TerriasRpcSender sender,
        bool remoteRpc,
        out string rejectionReason)
    {
        rejectionReason = "";
        var claimedToken = "";
        try
        {
            if (role == null || string.IsNullOrWhiteSpace(role.Id))
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected empty role. source=" + source);
                rejectionReason = "empty role";
                return false;
            }

            if (!ValidateSender(role, source, sender, remoteRpc, out rejectionReason))
            {
                return false;
            }

            if (GameSaveManager.GetValue<string>(TerriasIds.SolarMemoryModeKey) != "1")
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected outside Solar Memory mode. role=" + role.Id + ", source=" + source);
                rejectionReason = "Solar Memory mode is not active";
                return false;
            }

            if (role.SpecialVarMap == null
                || !role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupFinishedKey, out var setupFinished)
                || setupFinished != "1")
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected unfinished setup. role=" + role.Id + ", source=" + source);
                rejectionReason = "setup is not complete";
                return false;
            }

            if (!role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemorySetupCommitTokenKey, out var commitToken)
                || string.IsNullOrWhiteSpace(commitToken))
            {
                TerriasLog.Warn("[SolarMemoryRoleCommit] rejected missing commit token. role=" + role.Id + ", source=" + source);
                rejectionReason = "commit token is missing";
                return false;
            }

            var server = global::GameServer.Instance;
            if (server != null)
            {
                var players = server.LobbyInfo?.AddedPlayers;
                if (players != null && players.Count > 0 && !players.Any(player => player.Id == role.Id))
                {
                    TerriasLog.Warn("[SolarMemoryRoleCommit] rejected role outside lobby. role=" + role.Id + ", source=" + source);
                    rejectionReason = "role is outside the lobby";
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
            rejectionReason = "server commit failed";
            return false;
        }
    }

    private static bool ValidateSender(
        RoleTable role,
        string source,
        TerriasRpcSender? sender,
        bool remoteRpc,
        out string rejectionReason)
    {
        rejectionReason = "";
        sender ??= TerriasRpcSender.Unbound;
        if ((remoteRpc || IsMultiplayerLobby()) && !sender.IsAvailable)
        {
            TerriasLog.Warn("[SolarMemoryRoleCommit] rejected missing server sender. role=" + role.Id + ", source=" + source);
            rejectionReason = "server sender is missing";
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
            rejectionReason = "sender is outside the lobby";
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
            rejectionReason = "sender does not own the role";
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
