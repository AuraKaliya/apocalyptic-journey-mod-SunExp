using System;
using AuraShared.Core;
using Network.Command;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;

namespace SunExp.Dll.Network;

public enum FieldNetworkCommandKind
{
    Activate = 1,
    SnapshotRequest = 4
}

[Serializable]
public sealed class FieldStateSnapshot
{
    public const int CurrentProtocolVersion = 2;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

    public int FieldId { get; set; }

    public int Stacks { get; set; }

    public int MaxStacks { get; set; }

    public int Epoch { get; set; }

    public int BattleSerial { get; set; }

    public string BattleSessionId { get; set; } = "";

    public static FieldStateSnapshot Capture()
    {
        var snapshot = FieldApi.ActiveFieldSnapshot();
        return new FieldStateSnapshot
        {
            FieldId = (int)snapshot.Field,
            Stacks = Math.Max(0, snapshot.Stacks),
            MaxStacks = Math.Max(0, snapshot.MaxStacks),
            Epoch = Math.Max(0, snapshot.Epoch),
            BattleSerial = FieldNetworkSync.CurrentBattleSerial,
            BattleSessionId = FieldNetworkSync.HostBattleSessionId
        };
    }
}

[Serializable]
public sealed class RpcFieldStateSnapshot : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public FieldStateSnapshot Snapshot { get; set; } = new();

    public bool Accepted { get; set; }

    public RpcFieldStateSnapshot()
    {
    }

    public RpcFieldStateSnapshot(FieldStateSnapshot snapshot)
    {
        Snapshot = snapshot ?? new FieldStateSnapshot();
    }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (!serverSender.IsAvailable || !serverSender.IsLobbyMember || !serverSender.IsLobbyHost)
        {
            Accepted = false;
            return;
        }

        Snapshot = FieldStateSnapshot.Capture();
        Accepted = true;
    }

    public override void RpcExecute()
    {
        if (Accepted)
        {
            FieldNetworkSync.ApplySnapshot(Snapshot, "RpcFieldStateSnapshot");
        }
    }
}

[Serializable]
public sealed class RpcFieldStateRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = FieldStateSnapshot.CurrentProtocolVersion;

    public int CommandKind { get; set; }

    public int FieldId { get; set; }

    public int Amount { get; set; }

    public int Token { get; set; }

    public int BattleSerial { get; set; }

    public string BattleSessionId { get; set; } = "";

    // Intent is a small, server-resolved capability; it is never a caller-controlled effect description.
    public string IntentId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public int RejectionCode { get; set; }

    public FieldStateSnapshot Snapshot { get; set; } = new();

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Snapshot = FieldNetworkSync.ResolveRequest(
            ProtocolVersion,
            (FieldNetworkCommandKind)CommandKind,
            (SunExpFieldId)FieldId,
            Amount,
            Token,
            BattleSerial,
            BattleSessionId,
            IntentId,
            OwnerStatusId,
            serverSender,
            out var rejectionCode);
        RejectionCode = rejectionCode;
    }

    public override void RpcExecute()
    {
        if (RejectionCode != 0)
        {
            SunExpLog.Debug("[FieldNetwork] rejected response received: code="
                + FieldNetworkSync.RejectionName(RejectionCode)
                + "(" + RejectionCode + ")"
                + ", command=" + (FieldNetworkCommandKind)CommandKind
                + ", field=" + (SunExpFieldId)FieldId
                + ", intent=" + (IntentId ?? "")
                + ", owner=" + (OwnerStatusId ?? "")
                + ", token=" + Token
                + ", battleSerial=" + BattleSerial
                + ", battleSession=" + (BattleSessionId ?? "") + ".");
            return;
        }

        FieldNetworkSync.ApplySnapshot(Snapshot, "RpcFieldStateRequest");
    }
}

public static class FieldNetworkSync
{
    private const string DomainId = "FieldState";
    private static readonly AuraAuthoritativeSyncDomain SyncDomain =
        AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = SunExpIds.ModId,
            DomainId = DomainId,
            SnapshotRequestThrottleSeconds = 1.0d,
            MaxResolvedTokens = 256
        });
    private static string hostBattleSessionId = Guid.NewGuid().ToString("N");
    private static string remoteBattleSessionId = "";

    public static int CurrentBattleSerial => SyncDomain.CurrentSession;

    internal static string HostBattleSessionId => hostBattleSessionId;

    public static bool RequestActivate(ScriptExecutor? executor, SunExpFieldId field, int amount, string intentId)
    {
        return SendRequest(
            FieldNetworkCommandKind.Activate,
            field,
            amount,
            intentId,
            executor?.Self?.InstanceId ?? "");
    }

    public static void RequestSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsClientOnly()
            || !SyncDomain.TryBeginSnapshotRequest())
        {
            return;
        }

        SendRequest(FieldNetworkCommandKind.SnapshotRequest, SunExpFieldId.None, 0, source, "");
    }

    public static void BroadcastSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsMultiplayerSession()
            || SunExpNetworkRuntime.IsClientOnly())
        {
            return;
        }

        var command = new RpcFieldStateSnapshot();
        command.BindServerSender(SunExpRpcAuthorityRuntime.CreateLocalServerSender(source));
        SunExpNetworkRuntime.Send(command, source ?? "FieldNetworkSync.BroadcastSnapshot");
    }

    public static FieldStateSnapshot ResolveRequest(
        int protocolVersion,
        FieldNetworkCommandKind commandKind,
        SunExpFieldId field,
        int amount,
        int token,
        int requestBattleSerial,
        string requestBattleSessionId,
        string intentId,
        string ownerStatusId,
        SunExpRpcSender sender,
        out int rejectionCode)
    {
        rejectionCode = ValidateRequest(protocolVersion, commandKind, field, amount, token, requestBattleSerial, requestBattleSessionId, intentId, ownerStatusId, sender);
        if (rejectionCode != 0)
        {
            LogRejectedRequest(
                rejectionCode,
                commandKind,
                field,
                intentId,
                ownerStatusId,
                sender,
                token,
                requestBattleSerial,
                requestBattleSessionId);
            return FieldStateSnapshot.Capture();
        }

        switch (commandKind)
        {
            case FieldNetworkCommandKind.Activate:
                var resolvedAmount = ResolveActivateIntent(field, intentId, ownerStatusId, sender.PlayerId);
                if (resolvedAmount <= 0)
                {
                    rejectionCode = 9;
                    LogRejectedRequest(
                        rejectionCode,
                        commandKind,
                        field,
                        intentId,
                        ownerStatusId,
                        sender,
                        token,
                        requestBattleSerial,
                        requestBattleSessionId);
                    return FieldStateSnapshot.Capture();
                }

                LogAcceptedActivation(
                    field,
                    intentId,
                    ownerStatusId,
                    sender,
                    token,
                    requestBattleSerial,
                    requestBattleSessionId,
                    resolvedAmount);
                break;
        }

        return FieldStateSnapshot.Capture();
    }

    public static void ApplySnapshot(FieldStateSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != FieldStateSnapshot.CurrentProtocolVersion
            || string.IsNullOrWhiteSpace(snapshot.BattleSessionId)
            || !SyncDomain.AcceptRemoteSnapshotSession(snapshot.BattleSerial))
        {
            return;
        }

        if (!string.Equals(remoteBattleSessionId, snapshot.BattleSessionId, StringComparison.Ordinal))
        {
            remoteBattleSessionId = snapshot.BattleSessionId;
        }

        FieldApi.ApplyNetworkSnapshot((SunExpFieldId)snapshot.FieldId, snapshot.Stacks, snapshot.Epoch, source);
    }

    public static void ResetFightState()
    {
        SyncDomain.ResetSession();
        if (SunExpNetworkRuntime.IsClientOnly())
        {
            remoteBattleSessionId = "";
            return;
        }

        hostBattleSessionId = Guid.NewGuid().ToString("N");
        remoteBattleSessionId = hostBattleSessionId;
    }

    private static int ValidateRequest(
        int protocolVersion,
        FieldNetworkCommandKind commandKind,
        SunExpFieldId field,
        int amount,
        int token,
        int requestBattleSerial,
        string requestBattleSessionId,
        string intentId,
        string ownerStatusId,
        SunExpRpcSender sender)
    {
        if (protocolVersion != FieldStateSnapshot.CurrentProtocolVersion)
        {
            return 1;
        }

        if (SunExpNetworkRuntime.IsMultiplayerSession())
        {
            if (!sender.IsAvailable)
            {
                return 2;
            }

            if (!sender.IsLobbyMember)
            {
                return 3;
            }
        }

        if (commandKind != FieldNetworkCommandKind.SnapshotRequest
            && field == SunExpFieldId.None)
        {
            return 4;
        }

        if (commandKind == FieldNetworkCommandKind.Activate && amount <= 0)
        {
            return 5;
        }

        if (!SyncDomain.TryClaimToken(sender.PlayerId, token))
        {
            return 6;
        }

        if (commandKind == FieldNetworkCommandKind.SnapshotRequest)
        {
            return 0;
        }

        if (!string.Equals(requestBattleSessionId, hostBattleSessionId, StringComparison.Ordinal))
        {
            return 7;
        }

        return ValidateActivateIntent(field, intentId, ownerStatusId, sender) ? 0 : 8;
    }

    private static bool SendRequest(FieldNetworkCommandKind commandKind, SunExpFieldId field, int amount, string intentId, string ownerStatusId)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsClientOnly())
        {
            return false;
        }

        return SunExpNetworkRuntime.Send(new RpcFieldStateRequest
        {
            CommandKind = (int)commandKind,
            FieldId = (int)field,
            Amount = Math.Max(0, amount),
            Token = SyncDomain.NextToken(),
            BattleSerial = CurrentBattleSerial,
            BattleSessionId = remoteBattleSessionId,
            IntentId = intentId ?? "",
            OwnerStatusId = ownerStatusId ?? ""
        }, "FieldNetworkSync.SendRequest:" + (intentId ?? ""));
    }

    private static bool ValidateActivateIntent(SunExpFieldId field, string intentId, string ownerStatusId, SunExpRpcSender sender)
    {
        if (!SunExpStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, ownerStatusId, out _))
        {
            return false;
        }

        return FieldActivationIntentCatalog.TryResolve(field, intentId, out _);
    }

    private static int ResolveActivateIntent(SunExpFieldId field, string intentId, string ownerStatusId, string senderPlayerId)
    {
        if (!FieldActivationIntentCatalog.TryResolve(field, intentId, out var definition))
        {
            return 0;
        }

        var amount = definition.FixedAmount;
        if (definition.AmountPolicy == FieldActivationAmountPolicy.AuthoritativeScorchingCanopyCarrierStacks)
        {
            amount = ResolveAuthoritativeCarrierStacks(ownerStatusId);
            if (amount <= 0)
            {
                return 0;
            }
        }

        FieldApi.ActivateFieldAuthoritative(field, amount, "FieldNetworkSync.Intent:" + intentId + ":" + senderPlayerId, broadcast: false);
        return amount;
    }

    private static int ResolveAuthoritativeCarrierStacks(string ownerStatusId)
    {
        try
        {
            var statuses = FightManager.Instance?.statuses;
            if (statuses == null || !statuses.TryGetValue(ownerStatusId, out var status) || status == null)
            {
                return 0;
            }

            return Math.Max(0, status.GetBuff(SunExpIds.ScorchingCanopy)?.buffConfig?.Level ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    internal static string RejectionName(int rejectionCode)
    {
        switch (rejectionCode)
        {
            case 0:
                return "Accepted";
            case 1:
                return "ProtocolMismatch";
            case 2:
                return "SenderUnavailable";
            case 3:
                return "SenderNotInLobby";
            case 4:
                return "MissingField";
            case 5:
                return "InvalidRequestedAmount";
            case 6:
                return "DuplicateToken";
            case 7:
                return "BattleSessionMismatch";
            case 8:
                return "InvalidActivationIntent";
            case 9:
                return "ActivationResolutionFailed";
            default:
                return "Unknown";
        }
    }

    private static void LogRejectedRequest(
        int rejectionCode,
        FieldNetworkCommandKind commandKind,
        SunExpFieldId field,
        string intentId,
        string ownerStatusId,
        SunExpRpcSender sender,
        int token,
        int requestBattleSerial,
        string requestBattleSessionId)
    {
        SunExpLog.Warn("[FieldNetwork] request rejected: code="
            + RejectionName(rejectionCode)
            + "(" + rejectionCode + ")"
            + ", command=" + commandKind
            + ", field=" + field + "(" + (int)field + ")"
            + ", intent=" + (intentId ?? "")
            + ", sender=" + (sender?.PlayerId ?? "")
            + ", owner=" + (ownerStatusId ?? "")
            + ", token=" + token
            + ", battleSerial=" + requestBattleSerial
            + ", requestSession=" + (requestBattleSessionId ?? "")
            + ", hostSession=" + hostBattleSessionId + ".");
    }

    private static void LogAcceptedActivation(
        SunExpFieldId field,
        string intentId,
        string ownerStatusId,
        SunExpRpcSender sender,
        int token,
        int requestBattleSerial,
        string requestBattleSessionId,
        int resolvedAmount)
    {
        SunExpLog.InfoAlways("[FieldNetwork] activation accepted: code=Accepted(0)"
            + ", field=" + field + "(" + (int)field + ")"
            + ", intent=" + (intentId ?? "")
            + ", sender=" + (sender?.PlayerId ?? "")
            + ", owner=" + (ownerStatusId ?? "")
            + ", token=" + token
            + ", battleSerial=" + requestBattleSerial
            + ", battleSession=" + (requestBattleSessionId ?? "")
            + ", resolvedAmount=" + resolvedAmount + ".");
    }
}
