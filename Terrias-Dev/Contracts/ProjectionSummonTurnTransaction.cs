using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Contracts;

public sealed class ProjectionSummonTurnTransaction
{
    public string Token { get; set; } = "";
    public int RoundSequence { get; set; }
    public long Order { get; set; }
    public long Revision { get; set; }
    public ProjectionSummonTurnTransactionState State { get; set; }
    public string StatusId { get; set; } = "";
    public string Generation { get; set; } = "";
    public string Detail { get; set; } = "";

    public bool IsOpen => State is ProjectionSummonTurnTransactionState.Reserved
        or ProjectionSummonTurnTransactionState.Ready;

    public bool IsTerminal => State is ProjectionSummonTurnTransactionState.Failed
        or ProjectionSummonTurnTransactionState.Completed;

    internal bool Claimed { get; set; }

    public ProjectionSummonTurnTransaction Clone()
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
            Detail = Detail,
            Claimed = Claimed
        };
    }
}
