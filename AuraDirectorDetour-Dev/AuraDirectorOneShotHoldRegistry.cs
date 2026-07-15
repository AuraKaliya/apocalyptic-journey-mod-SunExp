using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using AuraDirector.Shared;

namespace AuraDirector.Detour;

internal sealed class AuraDirectorOneShotHoldRegistry<TTarget>
    where TTarget : class
{
    private sealed class ReferenceComparer : IEqualityComparer<TTarget>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(TTarget? x, TTarget? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(TTarget obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    private sealed class Hold : IAuraDirectorNativeStartHold
    {
        private readonly AuraDirectorOneShotHoldRegistry<TTarget> owner;
        private int state;
        private string releaseReason = "";

        public Hold(AuraDirectorOneShotHoldRegistry<TTarget> owner, TTarget target)
        {
            this.owner = owner;
            Target = target;
        }

        public TTarget Target { get; }

        public string BackendId => owner.backendId;

        public object NativeTarget => Target;

        public bool IsReleased => Volatile.Read(ref state) > 0;

        public string ReleaseReason => releaseReason;

        public bool TryRelease(string reason)
        {
            if (Interlocked.CompareExchange(ref state, -1, 0) != 0)
            {
                return false;
            }

            releaseReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
            Volatile.Write(ref state, 1);
            return owner.Release(this);
        }

        public void Abandon()
        {
            Interlocked.CompareExchange(ref state, 2, 0);
        }
    }

    private readonly object gate = new();
    private readonly string backendId;
    private readonly IAuraDirectorNativeStartHoldSink sink;
    private readonly Action<TTarget> resumeOriginal;
    private readonly Action<string> log;
    private readonly Dictionary<TTarget, Hold> held = new(ReferenceComparer.Instance);
    private readonly HashSet<TTarget> bypass = new(ReferenceComparer.Instance);
    private bool accepting = true;

    public AuraDirectorOneShotHoldRegistry(
        string backendId,
        IAuraDirectorNativeStartHoldSink sink,
        Action<TTarget> resumeOriginal,
        Action<string>? log = null)
    {
        this.backendId = string.IsNullOrWhiteSpace(backendId) ? "unknown" : backendId.Trim();
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.resumeOriginal = resumeOriginal ?? throw new ArgumentNullException(nameof(resumeOriginal));
        this.log = log ?? (_ => { });
    }

    public int HeldCount
    {
        get
        {
            lock (gate)
            {
                return held.Count;
            }
        }
    }

    public bool Intercept(TTarget target)
    {
        if (target == null)
        {
            return true;
        }

        Hold hold;
        lock (gate)
        {
            if (bypass.Remove(target))
            {
                return true;
            }
            if (!accepting)
            {
                return true;
            }
            if (held.ContainsKey(target))
            {
                return false;
            }

            hold = new Hold(this, target);
            held.Add(target, hold);
        }

        var accepted = false;
        try
        {
            accepted = sink.TryAccept(hold);
        }
        catch (Exception ex)
        {
            log("Native start hold sink failed open: " + ex);
        }

        if (accepted || hold.IsReleased)
        {
            return false;
        }

        lock (gate)
        {
            if (held.TryGetValue(target, out var current) && ReferenceEquals(current, hold))
            {
                held.Remove(target);
            }
        }
        hold.Abandon();
        return true;
    }

    public int StopAndReleaseAll(string reason)
    {
        Hold[] snapshot;
        lock (gate)
        {
            accepting = false;
            snapshot = held.Values.ToArray();
        }

        var released = 0;
        foreach (var hold in snapshot)
        {
            if (hold.TryRelease(reason))
            {
                released++;
            }
        }
        return released;
    }

    private bool Release(Hold hold)
    {
        lock (gate)
        {
            if (!held.TryGetValue(hold.Target, out var current) || !ReferenceEquals(current, hold))
            {
                return false;
            }
            held.Remove(hold.Target);
            bypass.Add(hold.Target);
        }

        try
        {
            resumeOriginal(hold.Target);
            return true;
        }
        catch (Exception ex)
        {
            log("Native start resume failed: " + ex);
            return false;
        }
        finally
        {
            lock (gate)
            {
                bypass.Remove(hold.Target);
            }
        }
    }
}
