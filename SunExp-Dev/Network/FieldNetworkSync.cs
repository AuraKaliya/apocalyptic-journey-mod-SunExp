using System;
using System.Collections.Generic;
using Network.Command;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Network;

public enum FieldNetworkCommandKind
{
    Activate = 1,
    Set = 2,
    Clear = 3,
    SnapshotRequest = 4
}

[Serializable]
public sealed class FieldStateSnapshot
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

    public int FieldId { get; set; }

    public int Stacks { get; set; }

    public int MaxStacks { get; set; }

    public int Epoch { get; set; }

    public int BattleSerial { get; set; }

    public static FieldStateSnapshot Capture()
    {
        var snapshot = FieldApi.ActiveFieldSnapshot();
        return new FieldStateSnapshot
        {
            FieldId = (int)snapshot.Field,
            Stacks = Math.Max(0, snapshot.Stacks),
            MaxStacks = Math.Max(0, snapshot.MaxStacks),
            Epoch = Math.Max(0, snapshot.Epoch),
            BattleSerial = FieldNetworkSync.CurrentBattleSerial
        };
    }
}

[Serializable]
public sealed class RpcFieldStateSnapshot : RpcCommandBase
{
    public FieldStateSnapshot Snapshot { get; set; } = new();

    public RpcFieldStateSnapshot()
    {
    }

    public RpcFieldStateSnapshot(FieldStateSnapshot snapshot)
    {
        Snapshot = snapshot ?? new FieldStateSnapshot();
    }

    public override void RpcExecute()
    {
        FieldNetworkSync.ApplySnapshot(Snapshot, "RpcFieldStateSnapshot");
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
            serverSender,
            out var rejectionCode);
        RejectionCode = rejectionCode;
    }

    public override void RpcExecute()
    {
        if (RejectionCode != 0)
        {
            SunExpLog.Debug("[FieldNetwork] request rejected: code=" + RejectionCode + ", token=" + Token + ".");
            return;
        }

        FieldNetworkSync.ApplySnapshot(Snapshot, "RpcFieldStateRequest");
    }
}

public static class FieldNetworkSync
{
    private const double SnapshotRequestThrottleSeconds = 1.0d;
    private const int MaxResolvedTokens = 256;
    private static readonly object Sync = new();
    private static readonly HashSet<int> ResolvedTokens = new();
    private static readonly Queue<int> ResolvedTokenOrder = new();
    private static int nextToken = Environment.TickCount;
    private static int battleSerial = 1;
    private static DateTime lastSnapshotRequestAtUtc = DateTime.MinValue;

    public static int CurrentBattleSerial => battleSerial;

    public static bool RequestActivate(SunExpFieldId field, int amount, string source)
    {
        return SendRequest(FieldNetworkCommandKind.Activate, field, amount, source);
    }

    public static bool RequestSet(SunExpFieldId field, int stacks, string source)
    {
        return SendRequest(FieldNetworkCommandKind.Set, field, stacks, source);
    }

    public static bool RequestClear(SunExpFieldId field, string source)
    {
        return SendRequest(FieldNetworkCommandKind.Clear, field, 0, source);
    }

    public static void RequestSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsClientOnly()
            || IsSnapshotRequestThrottled())
        {
            return;
        }

        SendRequest(FieldNetworkCommandKind.SnapshotRequest, SunExpFieldId.None, 0, source);
    }

    public static void BroadcastSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsMultiplayerSession()
            || SunExpNetworkRuntime.IsClientOnly())
        {
            return;
        }

        SunExpNetworkRuntime.Send(new RpcFieldStateSnapshot(FieldStateSnapshot.Capture()), source ?? "FieldNetworkSync.BroadcastSnapshot");
    }

    public static FieldStateSnapshot ResolveRequest(
        int protocolVersion,
        FieldNetworkCommandKind commandKind,
        SunExpFieldId field,
        int amount,
        int token,
        int requestBattleSerial,
        SunExpRpcSender sender,
        out int rejectionCode)
    {
        rejectionCode = ValidateRequest(protocolVersion, commandKind, field, amount, token, requestBattleSerial, sender);
        if (rejectionCode != 0)
        {
            return FieldStateSnapshot.Capture();
        }

        switch (commandKind)
        {
            case FieldNetworkCommandKind.Activate:
                FieldApi.ActivateFieldAuthoritative(field, amount, "FieldNetworkSync.ResolveRequest", broadcast: false);
                break;
            case FieldNetworkCommandKind.Set:
                FieldApi.SetSharedFieldStateAuthoritative(field, amount, "FieldNetworkSync.ResolveRequest", broadcast: false);
                break;
            case FieldNetworkCommandKind.Clear:
                FieldApi.TryClearActiveFieldAuthoritative("FieldNetworkSync.ResolveRequest", field, broadcast: false);
                break;
        }

        return FieldStateSnapshot.Capture();
    }

    public static void ApplySnapshot(FieldStateSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != FieldStateSnapshot.CurrentProtocolVersion
            || snapshot.BattleSerial != CurrentBattleSerial)
        {
            return;
        }

        FieldApi.ApplyNetworkSnapshot((SunExpFieldId)snapshot.FieldId, snapshot.Stacks, snapshot.Epoch, source);
    }

    public static void ResetFightState()
    {
        lock (Sync)
        {
            ResolvedTokens.Clear();
            ResolvedTokenOrder.Clear();
            lastSnapshotRequestAtUtc = DateTime.MinValue;
            unchecked
            {
                battleSerial++;
                if (battleSerial <= 0)
                {
                    battleSerial = 1;
                }
            }
        }
    }

    private static int ValidateRequest(
        int protocolVersion,
        FieldNetworkCommandKind commandKind,
        SunExpFieldId field,
        int amount,
        int token,
        int requestBattleSerial,
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

        if ((commandKind == FieldNetworkCommandKind.Activate || commandKind == FieldNetworkCommandKind.Set)
            && amount <= 0)
        {
            return 5;
        }

        if (requestBattleSerial != CurrentBattleSerial)
        {
            return 7;
        }

        return ClaimToken(token) ? 0 : 6;
    }

    private static bool SendRequest(FieldNetworkCommandKind commandKind, SunExpFieldId field, int amount, string source)
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
            Token = NextToken(),
            BattleSerial = CurrentBattleSerial
        }, source ?? "FieldNetworkSync.SendRequest");
    }

    private static bool ClaimToken(int token)
    {
        if (token == 0)
        {
            return true;
        }

        lock (Sync)
        {
            if (!ResolvedTokens.Add(token))
            {
                return false;
            }

            ResolvedTokenOrder.Enqueue(token);
            while (ResolvedTokenOrder.Count > MaxResolvedTokens)
            {
                ResolvedTokens.Remove(ResolvedTokenOrder.Dequeue());
            }

            return true;
        }
    }

    private static int NextToken()
    {
        lock (Sync)
        {
            unchecked
            {
                nextToken++;
                if (nextToken == 0)
                {
                    nextToken = 1;
                }

                return nextToken;
            }
        }
    }

    private static bool IsSnapshotRequestThrottled()
    {
        var now = DateTime.UtcNow;
        if ((now - lastSnapshotRequestAtUtc).TotalSeconds < SnapshotRequestThrottleSeconds)
        {
            return true;
        }

        lastSnapshotRequestAtUtc = now;
        return false;
    }
}
