using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraShared.Core;

public enum AuraPresentationMaterialReleaseDisposition
{
    Clean,
    Pending,
    Blocked
}

public readonly struct AuraPresentationMaterialReleaseResult
{
    public AuraPresentationMaterialReleaseResult(
        AuraPresentationMaterialReleaseDisposition disposition,
        string diagnostic)
    {
        Disposition = disposition;
        Diagnostic = diagnostic ?? "";
    }

    public AuraPresentationMaterialReleaseDisposition Disposition { get; }

    public string Diagnostic { get; }

    public bool IsClean => Disposition == AuraPresentationMaterialReleaseDisposition.Clean;

    public bool IsPending => Disposition == AuraPresentationMaterialReleaseDisposition.Pending;

    public bool IsBlocked => Disposition == AuraPresentationMaterialReleaseDisposition.Blocked;
}

public sealed class AuraPresentationMaterialAcquireRequest
{
    public int ViewRootInstanceId { get; set; }

    public int ViewGeneration { get; set; }

    public int TargetInstanceId { get; set; }

    public string OwnerId { get; set; } = "";

    public object? AppliedMaterial { get; set; }

    public Func<bool>? IsTargetAlive { get; set; }

    public Func<object?>? ReadCurrentMaterial { get; set; }

    public Action<object?>? WriteCurrentMaterial { get; set; }

    public Func<object?, int>? MaterialInstanceId { get; set; }

    public Action<object>? ReleaseAppliedMaterial { get; set; }
}

public sealed class AuraPresentationMaterialLease : IDisposable
{
    private readonly AuraPresentationMaterialCoordinator.MaterialTargetKey key;
    private readonly long token;
    private bool releaseRequested;

    internal AuraPresentationMaterialLease(
        AuraPresentationMaterialCoordinator.MaterialTargetKey key,
        long token)
    {
        this.key = key;
        this.token = token;
    }

    public bool IsActive => AuraPresentationMaterialCoordinator.IsLeaseActive(key, token);

    public bool OwnsCurrent => AuraPresentationMaterialCoordinator.LeaseOwnsCurrent(key, token);

    public AuraPresentationMaterialReleaseResult Release()
    {
        if (releaseRequested)
        {
            return AuraPresentationMaterialCoordinator.LeaseReleaseState(key, token);
        }

        releaseRequested = true;
        return AuraPresentationMaterialCoordinator.Release(key, token);
    }

    public void Dispose()
    {
        Release();
    }
}

/// <summary>
/// Coordinates every temporary material layer on one presentation target.
/// Out-of-order releases are retained as pending frames and drained only after
/// every newer owner has released, so the native baseline is restored exactly
/// once and no owner has to remember another owner's material.
/// </summary>
public static class AuraPresentationMaterialCoordinator
{
    private static readonly object Gate = new();
    private static readonly Dictionary<MaterialTargetKey, MaterialTargetState> Targets = new();
    private static readonly Dictionary<int, MaterialTargetKey> ActiveTargetOwners = new();
    private static long nextToken;

    public static bool TryAcquire(
        AuraPresentationMaterialAcquireRequest request,
        out AuraPresentationMaterialLease? lease,
        out string failure)
    {
        lease = null;
        failure = Validate(request);
        if (failure.Length > 0)
        {
            return false;
        }

        var key = new MaterialTargetKey(
            request.ViewRootInstanceId,
            request.ViewGeneration,
            request.TargetInstanceId);
        lock (Gate)
        {
            if (ActiveTargetOwners.TryGetValue(request.TargetInstanceId, out var activeKey)
                && !activeKey.Equals(key))
            {
                failure = "target is still owned by presentation generation "
                          + activeKey.ViewGeneration;
                return false;
            }

            MaterialTargetState? state = null;
            object? current = null;
            var currentId = 0;
            var currentCaptured = false;
            var writeAttempted = false;
            try
            {
                if (!request.IsTargetAlive!())
                {
                    failure = "target is not alive";
                    return false;
                }

                current = request.ReadCurrentMaterial!();
                currentId = request.MaterialInstanceId!(current);
                currentCaptured = true;
                if (!Targets.TryGetValue(key, out state))
                {
                    state = new MaterialTargetState(key, request, current, currentId);
                }
                else
                {
                    if (state.Fault.Length > 0 || state.Frames.Count == 0)
                    {
                        failure = "target is quarantined: " + Describe(state);
                        return false;
                    }

                    var expectedId = state.Frames[state.Frames.Count - 1].AppliedMaterialInstanceId;
                    if (currentId != expectedId)
                    {
                        failure = "renderer material changed outside the coordinator: expected="
                                  + expectedId
                                  + ", current="
                                  + currentId
                                  + ", stack="
                                  + Describe(state);
                        return false;
                    }
                }

                var appliedId = request.MaterialInstanceId(request.AppliedMaterial);
                if (appliedId == 0)
                {
                    failure = "applied material identity is required";
                    return false;
                }
                var token = ++nextToken;
                if (token <= 0)
                {
                    nextToken = token = 1;
                }

                writeAttempted = true;
                request.WriteCurrentMaterial!(request.AppliedMaterial);
                var attachedId = request.MaterialInstanceId(request.ReadCurrentMaterial());
                if (attachedId != appliedId)
                {
                    if (!TryRestore(request, current, currentId, out var rollbackFailure))
                    {
                        Quarantine(state, request.TargetInstanceId, rollbackFailure);
                    }
                    failure = "renderer rejected the coordinated material: expected="
                              + appliedId
                              + ", current="
                              + attachedId
                              + (rollbackFailure.Length == 0
                                  ? ""
                                  : ", rollback=" + rollbackFailure);
                    return false;
                }

                state.Frames.Add(new MaterialFrame(
                    token,
                    request.OwnerId.Trim(),
                    current,
                    currentId,
                    request.AppliedMaterial!,
                    appliedId,
                    request.ReleaseAppliedMaterial));
                Targets[key] = state;
                ActiveTargetOwners[request.TargetInstanceId] = key;
                lease = new AuraPresentationMaterialLease(key, token);
                return true;
            }
            catch (Exception ex)
            {
                var rollbackFailure = "";
                if (writeAttempted
                    && currentCaptured
                    && !TryRestore(request, current, currentId, out rollbackFailure)
                    && state != null)
                {
                    Quarantine(state, request.TargetInstanceId, rollbackFailure);
                }

                failure = "material acquisition failed: "
                          + ex.Message
                          + (rollbackFailure.Length == 0
                              ? ""
                              : ", rollback=" + rollbackFailure);
                return false;
            }
        }
    }

    public static bool IsViewClean(
        int viewRootInstanceId,
        int viewGeneration,
        out string diagnostic)
    {
        lock (Gate)
        {
            var active = Targets.Values
                .Where(value => value.Key.ViewRootInstanceId == viewRootInstanceId
                                && value.Key.ViewGeneration == viewGeneration)
                .ToArray();
            if (active.Length == 0)
            {
                diagnostic = "";
                return true;
            }

            diagnostic = string.Join(" | ", active.Select(Describe));
            return false;
        }
    }

    public static void AbandonView(int viewRootInstanceId, int viewGeneration)
    {
        lock (Gate)
        {
            foreach (var key in Targets.Keys
                         .Where(value => value.ViewRootInstanceId == viewRootInstanceId
                                         && value.ViewGeneration == viewGeneration)
                         .ToArray())
            {
                AbandonState(key);
            }
        }
    }

    public static void AbandonTarget(int targetInstanceId)
    {
        lock (Gate)
        {
            if (ActiveTargetOwners.TryGetValue(targetInstanceId, out var key))
            {
                AbandonState(key);
            }
        }
    }

    internal static bool IsLeaseActive(MaterialTargetKey key, long token)
    {
        lock (Gate)
        {
            return TryFindFrame(key, token, out _, out _);
        }
    }

    internal static bool LeaseOwnsCurrent(MaterialTargetKey key, long token)
    {
        lock (Gate)
        {
            if (!Targets.TryGetValue(key, out var state)
                || state.Frames.Count == 0
                || state.Frames[state.Frames.Count - 1].Token != token)
            {
                return false;
            }

            try
            {
                var top = state.Frames[state.Frames.Count - 1];
                return state.IsTargetAlive()
                       && state.MaterialInstanceId(state.ReadCurrentMaterial())
                       == top.AppliedMaterialInstanceId;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static AuraPresentationMaterialReleaseResult LeaseReleaseState(
        MaterialTargetKey key,
        long token)
    {
        lock (Gate)
        {
            if (!TryFindFrame(key, token, out var state, out var frame))
            {
                return Clean();
            }

            if (!frame.PendingRelease)
            {
                return new AuraPresentationMaterialReleaseResult(
                    AuraPresentationMaterialReleaseDisposition.Pending,
                    Describe(state));
            }

            return CurrentReleaseState(state);
        }
    }

    internal static AuraPresentationMaterialReleaseResult Release(
        MaterialTargetKey key,
        long token)
    {
        lock (Gate)
        {
            if (!TryFindFrame(key, token, out var state, out var frame))
            {
                return Clean();
            }

            frame.PendingRelease = true;
            return Drain(state);
        }
    }

    private static AuraPresentationMaterialReleaseResult Drain(MaterialTargetState state)
    {
        try
        {
            if (!state.IsTargetAlive())
            {
                AbandonState(state.Key);
                return Clean();
            }

            while (state.Frames.Count > 0)
            {
                var top = state.Frames[state.Frames.Count - 1];
                if (!top.PendingRelease)
                {
                    return new AuraPresentationMaterialReleaseResult(
                        AuraPresentationMaterialReleaseDisposition.Pending,
                        Describe(state));
                }

                var currentId = state.MaterialInstanceId(state.ReadCurrentMaterial());
                if (currentId != top.AppliedMaterialInstanceId)
                {
                    return new AuraPresentationMaterialReleaseResult(
                        AuraPresentationMaterialReleaseDisposition.Blocked,
                        "renderer material changed outside the coordinator: current="
                        + currentId
                        + ", expected="
                        + top.AppliedMaterialInstanceId
                        + ", stack="
                        + Describe(state));
                }

                state.WriteCurrentMaterial(top.OriginalMaterial);
                var restoredId = state.MaterialInstanceId(state.ReadCurrentMaterial());
                if (restoredId != top.OriginalMaterialInstanceId)
                {
                    return new AuraPresentationMaterialReleaseResult(
                        AuraPresentationMaterialReleaseDisposition.Blocked,
                        "renderer did not restore the predecessor material: current="
                        + restoredId
                        + ", expected="
                        + top.OriginalMaterialInstanceId
                        + ", stack="
                        + Describe(state));
                }

                state.Frames.RemoveAt(state.Frames.Count - 1);
                ReleaseMaterial(top);
            }

            RemoveState(state.Key);
            return Clean();
        }
        catch (Exception ex)
        {
            return new AuraPresentationMaterialReleaseResult(
                AuraPresentationMaterialReleaseDisposition.Blocked,
                "material stack drain failed: " + ex.Message + ", stack=" + Describe(state));
        }
    }

    private static AuraPresentationMaterialReleaseResult CurrentReleaseState(MaterialTargetState state)
    {
        if (state.Frames.Count == 0)
        {
            return Clean();
        }

        var top = state.Frames[state.Frames.Count - 1];
        if (!top.PendingRelease)
        {
            return new AuraPresentationMaterialReleaseResult(
                AuraPresentationMaterialReleaseDisposition.Pending,
                Describe(state));
        }

        try
        {
            var currentId = state.MaterialInstanceId(state.ReadCurrentMaterial());
            return currentId == top.AppliedMaterialInstanceId
                ? new AuraPresentationMaterialReleaseResult(
                    AuraPresentationMaterialReleaseDisposition.Pending,
                    Describe(state))
                : new AuraPresentationMaterialReleaseResult(
                    AuraPresentationMaterialReleaseDisposition.Blocked,
                    Describe(state));
        }
        catch
        {
            return new AuraPresentationMaterialReleaseResult(
                AuraPresentationMaterialReleaseDisposition.Blocked,
                Describe(state));
        }
    }

    private static bool TryFindFrame(
        MaterialTargetKey key,
        long token,
        out MaterialTargetState state,
        out MaterialFrame frame)
    {
        if (Targets.TryGetValue(key, out state!))
        {
            frame = state.Frames.FirstOrDefault(value => value.Token == token)!;
            if (frame != null)
            {
                return true;
            }
        }

        state = null!;
        frame = null!;
        return false;
    }

    private static void AbandonState(MaterialTargetKey key)
    {
        if (!Targets.TryGetValue(key, out var state))
        {
            return;
        }

        Targets.Remove(key);
        ActiveTargetOwners.Remove(key.TargetInstanceId);
        foreach (var frame in state.Frames.AsEnumerable().Reverse())
        {
            ReleaseMaterial(frame);
        }
        state.Frames.Clear();
    }

    private static void RemoveState(MaterialTargetKey key)
    {
        Targets.Remove(key);
        ActiveTargetOwners.Remove(key.TargetInstanceId);
    }

    private static bool TryRestore(
        AuraPresentationMaterialAcquireRequest request,
        object? material,
        int materialInstanceId,
        out string failure)
    {
        Exception? writeFailure = null;
        try
        {
            request.WriteCurrentMaterial!(material);
        }
        catch (Exception ex)
        {
            writeFailure = ex;
        }

        try
        {
            var restoredId = request.MaterialInstanceId!(request.ReadCurrentMaterial!());
            if (restoredId == materialInstanceId)
            {
                failure = "";
                return true;
            }

            failure = "renderer did not roll back to material "
                      + materialInstanceId
                      + "; current="
                      + restoredId
                      + (writeFailure == null ? "" : ", write=" + writeFailure.Message);
            return false;
        }
        catch (Exception ex)
        {
            failure = "renderer rollback verification failed: "
                      + ex.Message
                      + (writeFailure == null ? "" : ", write=" + writeFailure.Message);
            return false;
        }
    }

    private static void Quarantine(
        MaterialTargetState state,
        int targetInstanceId,
        string failure)
    {
        state.Fault = failure.Length == 0 ? "unknown acquisition rollback failure" : failure;
        Targets[state.Key] = state;
        ActiveTargetOwners[targetInstanceId] = state.Key;
    }

    private static void ReleaseMaterial(MaterialFrame frame)
    {
        if (frame.MaterialReleased)
        {
            return;
        }

        frame.MaterialReleased = true;
        try
        {
            frame.ReleaseAppliedMaterial?.Invoke(frame.AppliedMaterial);
        }
        catch
        {
            // A release callback cannot be allowed to strand the shared stack.
        }
    }

    private static string Describe(MaterialTargetState state)
    {
        return "root="
               + state.Key.ViewRootInstanceId
               + ", generation="
               + state.Key.ViewGeneration
               + ", target="
               + state.Key.TargetInstanceId
               + (state.Fault.Length == 0 ? "" : ", fault=" + state.Fault)
               + ", layers=["
               + string.Join(",", state.Frames.Select(frame =>
                   frame.OwnerId
                   + "#"
                   + frame.Token
                   + "(material="
                   + frame.AppliedMaterialInstanceId
                   + ",pending="
                   + frame.PendingRelease
                   + ")"))
               + "]";
    }

    private static string Validate(AuraPresentationMaterialAcquireRequest? request)
    {
        if (request == null) return "request is required";
        if (request.ViewRootInstanceId == 0) return "view root identity is required";
        if (request.TargetInstanceId == 0) return "target identity is required";
        if (string.IsNullOrWhiteSpace(request.OwnerId)) return "owner identity is required";
        if (request.AppliedMaterial == null) return "applied material is required";
        if (request.IsTargetAlive == null) return "target liveness callback is required";
        if (request.ReadCurrentMaterial == null) return "material reader is required";
        if (request.WriteCurrentMaterial == null) return "material writer is required";
        if (request.MaterialInstanceId == null) return "material identity callback is required";
        return "";
    }

    private static AuraPresentationMaterialReleaseResult Clean()
    {
        return new AuraPresentationMaterialReleaseResult(
            AuraPresentationMaterialReleaseDisposition.Clean,
            "");
    }

    internal readonly struct MaterialTargetKey : IEquatable<MaterialTargetKey>
    {
        public MaterialTargetKey(int viewRootInstanceId, int viewGeneration, int targetInstanceId)
        {
            ViewRootInstanceId = viewRootInstanceId;
            ViewGeneration = viewGeneration;
            TargetInstanceId = targetInstanceId;
        }

        public int ViewRootInstanceId { get; }
        public int ViewGeneration { get; }
        public int TargetInstanceId { get; }

        public bool Equals(MaterialTargetKey other)
        {
            return ViewRootInstanceId == other.ViewRootInstanceId
                   && ViewGeneration == other.ViewGeneration
                   && TargetInstanceId == other.TargetInstanceId;
        }

        public override bool Equals(object? obj)
        {
            return obj is MaterialTargetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ViewRootInstanceId;
                hash = (hash * 397) ^ ViewGeneration;
                hash = (hash * 397) ^ TargetInstanceId;
                return hash;
            }
        }
    }

    private sealed class MaterialTargetState
    {
        public MaterialTargetState(
            MaterialTargetKey key,
            AuraPresentationMaterialAcquireRequest request,
            object? baselineMaterial,
            int baselineMaterialInstanceId)
        {
            Key = key;
            IsTargetAlive = request.IsTargetAlive!;
            ReadCurrentMaterial = request.ReadCurrentMaterial!;
            WriteCurrentMaterial = request.WriteCurrentMaterial!;
            MaterialInstanceId = request.MaterialInstanceId!;
            BaselineMaterial = baselineMaterial;
            BaselineMaterialInstanceId = baselineMaterialInstanceId;
        }

        public MaterialTargetKey Key { get; }
        public Func<bool> IsTargetAlive { get; }
        public Func<object?> ReadCurrentMaterial { get; }
        public Action<object?> WriteCurrentMaterial { get; }
        public Func<object?, int> MaterialInstanceId { get; }
        public object? BaselineMaterial { get; }
        public int BaselineMaterialInstanceId { get; }
        public List<MaterialFrame> Frames { get; } = new();
        public string Fault { get; set; } = "";
    }

    private sealed class MaterialFrame
    {
        public MaterialFrame(
            long token,
            string ownerId,
            object? originalMaterial,
            int originalMaterialInstanceId,
            object appliedMaterial,
            int appliedMaterialInstanceId,
            Action<object>? releaseAppliedMaterial)
        {
            Token = token;
            OwnerId = ownerId;
            OriginalMaterial = originalMaterial;
            OriginalMaterialInstanceId = originalMaterialInstanceId;
            AppliedMaterial = appliedMaterial;
            AppliedMaterialInstanceId = appliedMaterialInstanceId;
            ReleaseAppliedMaterial = releaseAppliedMaterial;
        }

        public long Token { get; }
        public string OwnerId { get; }
        public object? OriginalMaterial { get; }
        public int OriginalMaterialInstanceId { get; }
        public object AppliedMaterial { get; }
        public int AppliedMaterialInstanceId { get; }
        public Action<object>? ReleaseAppliedMaterial { get; }
        public bool PendingRelease { get; set; }
        public bool MaterialReleased { get; set; }
    }
}
