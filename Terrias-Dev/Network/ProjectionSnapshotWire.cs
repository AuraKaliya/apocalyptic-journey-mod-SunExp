using System;

namespace Terrias.Dll.Network;

// The native RPC serializer emits TypeNameHandling.All. These wire identities
// remain stable while the application consumes transport-independent contracts.

[Serializable]
public sealed class ProjectionCompanionSnapshot : Contracts.ProjectionCompanionSnapshot
{
    public ProjectionCompanionSnapshot() { }
    public ProjectionCompanionSnapshot(Contracts.ProjectionCompanionSnapshot value)
    {
        ProtocolVersion = value.ProtocolVersion;
        BattleEpoch = value.BattleEpoch;
        CardModelVersion = value.CardModelVersion;
        Generation = value.Generation;
        StateRevision = value.StateRevision;
        ActionSequence = value.ActionSequence;
        CompletedTurnSequence = value.CompletedTurnSequence;
        SummonRoundSequence = value.SummonRoundSequence;
        SummonTurnToken = value.SummonTurnToken;
        SummonTurnOrder = value.SummonTurnOrder;
        SummonTurnRevision = value.SummonTurnRevision;
        SummonTurnState = value.SummonTurnState;
        SummonTurnDetail = value.SummonTurnDetail;
        Active = value.Active;
        RoleId = value.RoleId;
        OwnerStatusId = value.OwnerStatusId;
        OwnerPlayerId = value.OwnerPlayerId;
        ExecutionRoutePlayerId = value.ExecutionRoutePlayerId;
        StatusId = value.StatusId;
        SlotIndex = value.SlotIndex;
        MaxHp = value.MaxHp;
        CurrentHp = value.CurrentHp;
        Attack = value.Attack;
        Armor = value.Armor;
        MaxMagic = value.MaxMagic;
        CurrentMagic = value.CurrentMagic;
    }
}

[Serializable]
public sealed class ProjectionSummonResultSnapshot : Contracts.ProjectionSummonResultSnapshot
{
    public ProjectionSummonResultSnapshot() { }
    public ProjectionSummonResultSnapshot(Contracts.ProjectionSummonResultSnapshot value)
    {
        ServerProtocolVersion = value.ServerProtocolVersion;
        ServerBattleEpoch = value.ServerBattleEpoch;
        ServerCardModelVersion = value.ServerCardModelVersion;
        Token = value.Token;
        RoleId = value.RoleId;
        OwnerStatusId = value.OwnerStatusId;
        OwnerPlayerId = value.OwnerPlayerId;
        StatusId = value.StatusId;
        Generation = value.Generation;
        Accepted = value.Accepted;
        Terminal = value.Terminal;
        FailureCode = value.FailureCode;
        FailureCategory = value.FailureCategory;
        Retryable = value.Retryable;
        RefundCard = value.RefundCard;
        Detail = value.Detail;
    }
}

[Serializable]
public sealed class ProjectionSummonTurnSnapshot : Contracts.ProjectionSummonTurnSnapshot
{
    public ProjectionSummonTurnSnapshot() { }
    public ProjectionSummonTurnSnapshot(Contracts.ProjectionSummonTurnSnapshot value)
    {
        ProtocolVersion = value.ProtocolVersion;
        BattleEpoch = value.BattleEpoch;
        Token = value.Token;
        RoundSequence = value.RoundSequence;
        Order = value.Order;
        Revision = value.Revision;
        State = value.State;
        StatusId = value.StatusId;
        Generation = value.Generation;
        Detail = value.Detail;
    }
}
