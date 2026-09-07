using System;
using System.Collections.Generic;

namespace Terrias.Dll.Contracts;

public enum ProjectionSummonFailureCategory
{
    None,
    Transport,
    Compatibility,
    Synchronization,
    Authorization,
    Capacity,
    Content,
    Runtime,
    Cancelled
}
