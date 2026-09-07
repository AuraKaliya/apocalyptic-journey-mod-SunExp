using System;
using System.Collections.Generic;

namespace Terrias.Dll.Contracts;

public enum ProjectionSummonFailureCode
{
    None,
    TransportNotSent,
    ProtocolMismatch,
    BattleEpochMismatch,
    CardModelMismatch,
    RoleDeckUnavailable,
    RoleDeckTimedOut,
    UnknownRole,
    MissingSender,
    SenderOutsideLobby,
    OwnerMismatch,
    TokenConflict,
    OwnerAlreadyHasProjection,
    FriendlySeatsFull,
    SeatReservationExpired,
    TurnTransactionUnavailable,
    SpawnFailed,
    Cancelled
}
