using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;


namespace Terrias.Dll.Contracts;

[Serializable]
public class ProjectionSummonTurnSnapshot
{
    public int ProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;
    public int BattleEpoch { get; set; }
    public string Token { get; set; } = "";
    public int RoundSequence { get; set; }
    public long Order { get; set; }
    public long Revision { get; set; }
    public ProjectionSummonTurnTransactionState State { get; set; }
    public string StatusId { get; set; } = "";
    public string Generation { get; set; } = "";
    public string Detail { get; set; } = "";

    public ProjectionSummonTurnTransaction ToTransaction()
    {
        return new ProjectionSummonTurnTransaction
        {
            Token = Token,
            RoundSequence = RoundSequence,
            Order = Order,
            Revision = Revision,
            State = State,
            StatusId = StatusId,
            Generation = Generation,
            Detail = Detail
        };
    }
}
