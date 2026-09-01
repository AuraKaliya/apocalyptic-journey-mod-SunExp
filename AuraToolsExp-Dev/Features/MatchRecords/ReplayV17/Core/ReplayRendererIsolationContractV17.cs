using System;
using System.Threading;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal enum ReplayRendererCameraReleaseV17
{
    Released,
    Duplicate,
    ForeignLease
}

internal readonly struct ReplayRendererCameraLeaseTokenV17 : IEquatable<ReplayRendererCameraLeaseTokenV17>
{
    internal ReplayRendererCameraLeaseTokenV17(long ownerId, long leaseId, int cameraId)
    {
        OwnerId = ownerId;
        LeaseId = leaseId;
        CameraId = cameraId;
    }

    internal long OwnerId { get; }
    internal long LeaseId { get; }
    internal int CameraId { get; }
    internal bool IsValid => OwnerId > 0 && LeaseId > 0 && CameraId != 0;

    public bool Equals(ReplayRendererCameraLeaseTokenV17 other) =>
        OwnerId == other.OwnerId && LeaseId == other.LeaseId && CameraId == other.CameraId;

    public override bool Equals(object? obj) =>
        obj is ReplayRendererCameraLeaseTokenV17 other && Equals(other);

    public override int GetHashCode() =>
        unchecked((((int)OwnerId * 397) ^ (int)LeaseId) * 397 ^ CameraId);
}

internal sealed class ReplayRendererIsolationContractV17
{
    private static long nextOwnerId;
    private readonly long ownerId = Interlocked.Increment(ref nextOwnerId);
    private long nextLeaseId;
    private ReplayRendererCameraLeaseTokenV17 active;

    internal bool HasActiveCamera => active.IsValid;

    internal ReplayRendererCameraLeaseTokenV17 Acquire(int cameraId)
    {
        if (cameraId == 0) throw new ArgumentOutOfRangeException(nameof(cameraId));
        if (HasActiveCamera)
            throw new InvalidOperationException(
                "The dedicated replay renderer already has an active camera owner.");
        active = new ReplayRendererCameraLeaseTokenV17(ownerId, ++nextLeaseId, cameraId);
        return active;
    }

    internal bool Validate(ReplayRendererCameraLeaseTokenV17 token, int cameraId) =>
        HasActiveCamera && active.Equals(token) && token.CameraId == cameraId;

    internal ReplayRendererCameraReleaseV17 Release(ReplayRendererCameraLeaseTokenV17 token)
    {
        if (!token.IsValid || token.OwnerId != ownerId)
            return ReplayRendererCameraReleaseV17.ForeignLease;
        if (HasActiveCamera)
        {
            if (!active.Equals(token)) return ReplayRendererCameraReleaseV17.ForeignLease;
            active = default;
            return ReplayRendererCameraReleaseV17.Released;
        }
        return token.LeaseId <= nextLeaseId
            ? ReplayRendererCameraReleaseV17.Duplicate
            : ReplayRendererCameraReleaseV17.ForeignLease;
    }
}
