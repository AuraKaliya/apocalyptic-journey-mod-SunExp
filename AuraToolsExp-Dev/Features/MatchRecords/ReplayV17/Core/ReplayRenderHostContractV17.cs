using System;
using System.Threading;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal enum ReplayRenderHostPhaseV17
{
    Prepared,
    Preflighted,
    FrameBarrierConfirmed,
    Active,
    Disposed
}

internal enum ReplayRenderLeaseReleaseV17
{
    Released,
    Duplicate,
    StaleGeneration,
    ForeignLease,
    HostDisposed
}

internal readonly struct ReplayRenderSizeV17 : IEquatable<ReplayRenderSizeV17>
{
    internal ReplayRenderSizeV17(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
    }

    internal int Width { get; }
    internal int Height { get; }

    public bool Equals(ReplayRenderSizeV17 other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is ReplayRenderSizeV17 other && Equals(other);
    public override int GetHashCode() => unchecked((Width * 397) ^ Height);
    public override string ToString() => Width + "x" + Height;
}

internal readonly struct ReplayRenderLeaseTokenV17 : IEquatable<ReplayRenderLeaseTokenV17>
{
    internal ReplayRenderLeaseTokenV17(long ownerId, long leaseId, int generation)
    {
        OwnerId = ownerId;
        LeaseId = leaseId;
        Generation = generation;
    }

    internal long OwnerId { get; }
    internal long LeaseId { get; }
    internal int Generation { get; }
    internal bool IsValid => OwnerId > 0 && LeaseId > 0 && Generation > 0;

    public bool Equals(ReplayRenderLeaseTokenV17 other) =>
        OwnerId == other.OwnerId && LeaseId == other.LeaseId && Generation == other.Generation;

    public override bool Equals(object? obj) => obj is ReplayRenderLeaseTokenV17 other && Equals(other);
    public override int GetHashCode() => unchecked((((int)OwnerId * 397) ^ (int)LeaseId) * 397 ^ Generation);
}

internal static class ReplayRenderSizePolicyV17
{
    internal const int MinimumWidth = 320;
    internal const int MinimumHeight = 180;
    internal const int MaximumWidth = 2560;
    internal const int MaximumHeight = 1440;
    internal const int DefaultWidth = 1920;
    internal const int DefaultHeight = 1080;

    internal static ReplayRenderSizeV17 Resolve(
        int screenWidth,
        int screenHeight,
        int referenceWidth,
        int referenceHeight)
    {
        var sourceWidth = referenceWidth > 0 ? referenceWidth : DefaultWidth;
        var sourceHeight = referenceHeight > 0 ? referenceHeight : DefaultHeight;
        var aspect = sourceWidth / (double)sourceHeight;
        if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0d)
            aspect = DefaultWidth / (double)DefaultHeight;

        var availableWidth = Clamp(
            screenWidth > 0 ? screenWidth : sourceWidth,
            MinimumWidth,
            MaximumWidth);
        var availableHeight = Clamp(
            screenHeight > 0 ? screenHeight : sourceHeight,
            MinimumHeight,
            MaximumHeight);

        var width = availableWidth;
        var height = (int)Math.Round(width / aspect, MidpointRounding.AwayFromZero);
        if (height > availableHeight)
        {
            height = availableHeight;
            width = (int)Math.Round(height * aspect, MidpointRounding.AwayFromZero);
        }

        if (width < MinimumWidth)
        {
            width = MinimumWidth;
            height = (int)Math.Round(width / aspect, MidpointRounding.AwayFromZero);
        }
        if (height < MinimumHeight)
        {
            height = MinimumHeight;
            width = (int)Math.Round(height * aspect, MidpointRounding.AwayFromZero);
        }

        if (width > MaximumWidth || height > MaximumHeight)
        {
            var scale = Math.Min(MaximumWidth / (double)width, MaximumHeight / (double)height);
            width = (int)Math.Floor(width * scale);
            height = (int)Math.Floor(height * scale);
        }

        width = MakeEven(Clamp(width, 2, MaximumWidth));
        height = MakeEven(Clamp(height, 2, MaximumHeight));
        return new ReplayRenderSizeV17(width, height);
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : Math.Max(2, value - 1);

    private static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}

internal sealed class ReplayRenderHostContractV17 : IDisposable
{
    private static long nextOwnerId;

    private readonly long ownerId = Interlocked.Increment(ref nextOwnerId);
    private long nextLeaseId;
    private ReplayRenderLeaseTokenV17 activeExportLease;
    private ReplayRenderSizeV17 size;

    internal ReplayRenderHostContractV17(ReplayRenderSizeV17 initialSize)
    {
        size = initialSize;
    }

    internal ReplayRenderHostPhaseV17 Phase { get; private set; } = ReplayRenderHostPhaseV17.Prepared;
    internal ReplayRenderSizeV17 Size => size;
    internal int Generation { get; private set; } = 1;
    internal bool HasExportLease => activeExportLease.IsValid;
    internal bool CanRenderPreflight => Phase == ReplayRenderHostPhaseV17.Prepared && !HasExportLease;
    internal bool CanConfirmFrameBarrier => Phase == ReplayRenderHostPhaseV17.Preflighted && !HasExportLease;
    internal bool CanRenderInteractive => Phase == ReplayRenderHostPhaseV17.Active && !HasExportLease;

    internal void MarkPreflightSucceeded()
    {
        RequirePhase(ReplayRenderHostPhaseV17.Prepared, "complete replay render preflight");
        if (HasExportLease)
            throw new InvalidOperationException("Replay render preflight cannot complete while export owns the target.");
        Phase = ReplayRenderHostPhaseV17.Preflighted;
    }

    internal void ConfirmFrameBarrier()
    {
        RequirePhase(ReplayRenderHostPhaseV17.Preflighted, "confirm replay render frame barrier");
        if (HasExportLease)
            throw new InvalidOperationException(
                "Replay render frame barrier cannot complete while export owns the target.");
        Phase = ReplayRenderHostPhaseV17.FrameBarrierConfirmed;
    }

    internal void Activate()
    {
        RequirePhase(ReplayRenderHostPhaseV17.FrameBarrierConfirmed, "activate replay rendering");
        Phase = ReplayRenderHostPhaseV17.Active;
    }

    internal ReplayRenderLeaseTokenV17 AcquireExport()
    {
        RequirePhase(ReplayRenderHostPhaseV17.Active, "acquire replay export target");
        if (HasExportLease)
            throw new InvalidOperationException("Replay export target already has an active owner.");
        activeExportLease = new ReplayRenderLeaseTokenV17(ownerId, ++nextLeaseId, Generation);
        return activeExportLease;
    }

    internal bool CanRenderExport(ReplayRenderLeaseTokenV17 token) =>
        Phase == ReplayRenderHostPhaseV17.Active
        && HasExportLease
        && activeExportLease.Equals(token)
        && token.Generation == Generation;

    internal ReplayRenderLeaseReleaseV17 ReleaseExport(ReplayRenderLeaseTokenV17 token)
    {
        if (Phase == ReplayRenderHostPhaseV17.Disposed)
            return ReplayRenderLeaseReleaseV17.HostDisposed;
        if (!token.IsValid || token.OwnerId != ownerId)
            return ReplayRenderLeaseReleaseV17.ForeignLease;
        if (token.Generation != Generation)
            return ReplayRenderLeaseReleaseV17.StaleGeneration;
        if (!HasExportLease)
            return token.LeaseId <= nextLeaseId
                ? ReplayRenderLeaseReleaseV17.Duplicate
                : ReplayRenderLeaseReleaseV17.ForeignLease;
        if (!activeExportLease.Equals(token))
            return ReplayRenderLeaseReleaseV17.ForeignLease;

        activeExportLease = default;
        return ReplayRenderLeaseReleaseV17.Released;
    }

    internal bool Resize(ReplayRenderSizeV17 nextSize)
    {
        if (Phase == ReplayRenderHostPhaseV17.Disposed)
            throw new ObjectDisposedException(nameof(ReplayRenderHostContractV17));
        if (HasExportLease)
            throw new InvalidOperationException("Replay interactive target cannot resize while export owns the camera.");
        if (size.Equals(nextSize)) return false;
        size = nextSize;
        Generation = checked(Generation + 1);
        return true;
    }

    public void Dispose()
    {
        if (Phase == ReplayRenderHostPhaseV17.Disposed) return;
        activeExportLease = default;
        Generation = checked(Generation + 1);
        Phase = ReplayRenderHostPhaseV17.Disposed;
    }

    private void RequirePhase(ReplayRenderHostPhaseV17 expected, string operation)
    {
        if (Phase == ReplayRenderHostPhaseV17.Disposed)
            throw new ObjectDisposedException(nameof(ReplayRenderHostContractV17));
        if (Phase != expected)
            throw new InvalidOperationException(
                "Cannot " + operation + " while replay render host is " + Phase + ".");
    }
}
