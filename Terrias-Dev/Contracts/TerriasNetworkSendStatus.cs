using System;
using System.Collections.Generic;
using AuraShared.Core;
using System.Linq;
using Network.Command;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Contracts;

public enum TerriasNetworkSendStatus
{
    Sent,
    NotAttempted,
    DispatchUnknown
}
