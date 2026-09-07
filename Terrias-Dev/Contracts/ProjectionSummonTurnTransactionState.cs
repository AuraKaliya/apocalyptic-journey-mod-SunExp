using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Contracts;

public enum ProjectionSummonTurnTransactionState
{
    Reserved = 1,
    Ready = 2,
    Failed = 3,
    Completed = 4
}
