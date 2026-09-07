using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;


namespace Terrias.Dll.Contracts;

[Serializable]
public class ProjectionCompanionSnapshot
{
    public int ProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;

    public int BattleEpoch { get; set; }

    public string CardModelVersion { get; set; } = "";

    public string Generation { get; set; } = "";

    public long StateRevision { get; set; }

    public long ActionSequence { get; set; }

    public long CompletedTurnSequence { get; set; }

    public int SummonRoundSequence { get; set; }

    public string SummonTurnToken { get; set; } = "";

    public long SummonTurnOrder { get; set; }

    public long SummonTurnRevision { get; set; }

    public ProjectionSummonTurnTransactionState SummonTurnState { get; set; }

    public string SummonTurnDetail { get; set; } = "";

    public bool Active { get; set; } = true;

    public string RoleId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string ExecutionRoutePlayerId { get; set; } = "";

    public string StatusId { get; set; } = "";

    public int SlotIndex { get; set; } = -1;

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Attack { get; set; }

    public int Armor { get; set; }

    public int MaxMagic { get; set; }

    public int CurrentMagic { get; set; }

}
