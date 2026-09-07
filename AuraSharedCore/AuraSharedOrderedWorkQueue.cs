using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AuraShared.Core;

public sealed class AuraSharedWorkMeasurement
{
    public string OwnerId { get; set; } = "";
    public string Source { get; set; } = "";
    public double QueueMilliseconds { get; set; }
    public double WorkMilliseconds { get; set; }
    public double ApplyMilliseconds { get; set; }
    public string Error { get; set; } = "";
}

// FIFO work has different semantics from a replaceable refresh. Once accepted,
// its budget remains charged until the result is handed back to the main thread.
// This queue survives the producer's UI/battle lifetime; it owns managed data only.
public sealed class AuraSharedOrderedWorkQueue
{
    private static readonly object ActiveGate = new();
    private static readonly HashSet<AuraSharedOrderedWorkQueue> Active = new();
    private readonly object gate = new();
    private readonly Queue<Work> pending = new();
    private readonly Queue<Receipt> receipts = new();
    private readonly string owner;
    private readonly string name;
    private readonly AuraSharedBackgroundWorkKind kind;
    private readonly int maximumItems;
    private readonly long maximumBytes;
    private int retainedCount;
    private long retainedBytes;
    private bool running;
    private long sequence;

    public AuraSharedOrderedWorkQueue(string ownerId, string name, AuraSharedBackgroundWorkKind kind,
        int maximumItems = 512, long maximumBytes = 256L * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Work queue identity is required.");
        if (maximumItems <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        owner = ownerId; this.name = name; this.kind = kind;
        this.maximumItems = maximumItems; this.maximumBytes = maximumBytes;
    }

    public event Action<AuraSharedWorkMeasurement>? Measured;
    public event Action<Exception>? CompletionFailed;
    public int AdmissionDeferrals { get; private set; }
    public string LastError { get; private set; } = "";
    public int PendingCount { get { lock (gate) return pending.Count; } }
    public long RetainedBytes { get { lock (gate) return retainedBytes; } }
    public bool IsIdle { get { lock (gate) return !running && retainedCount == 0; } }
    public bool HasCapacity(long bytes = 1024) { lock (gate) return retainedCount < maximumItems && Math.Max(1, bytes) <= maximumBytes - retainedBytes; }
    public double OldestPendingMilliseconds { get { lock (gate) return pending.Count == 0 ? 0 : Milliseconds(pending.Peek().QueuedAt); } }

    public bool TryEnqueue<T>(string source, Func<T> work, Action<T> apply, Action<Exception> failed, long retainedBytes = 1024)
    {
        if (work == null || apply == null || failed == null) throw new ArgumentNullException(nameof(work));
        if (!AuraSharedFrameScheduler.EnsureMainThreadRunner()) return false;
        var cost = Math.Max(1, retainedBytes);
        lock (gate)
        {
            if (retainedCount >= maximumItems || cost > maximumBytes - this.retainedBytes) return false;
            var item = new Work { QueuedAt = Stopwatch.GetTimestamp(), Bytes = cost };
            item.Run = () =>
            {
                var started = Stopwatch.GetTimestamp();
                var measurement = new AuraSharedWorkMeasurement
                {
                    OwnerId = owner, Source = source,
                    QueueMilliseconds = (started - item.QueuedAt) * 1000d / Stopwatch.Frequency
                };
                Action completion;
                try { var result = work(); completion = () => apply(result); }
                catch (Exception ex) { measurement.Error = ex.Message; completion = () => failed(ex); }
                measurement.WorkMilliseconds = Milliseconds(started);
                return new Receipt { Bytes = cost, Apply = completion, Measurement = measurement };
            };
            pending.Enqueue(item);
            retainedCount++;
            this.retainedBytes += cost;
        }
        lock (ActiveGate) Active.Add(this);
        Pump();
        return true;
    }

    public bool Enqueue<T>(string source, Func<T> work, Action<T> apply, Action<Exception> failed, long retainedBytes = 1024)
    {
        if (TryEnqueue(source, work, apply, failed, retainedBytes)) return true;
        failed(new InvalidOperationException("Work capacity exceeded; the operation was not accepted: " + source));
        return false;
    }

    public static void PumpRegistered()
    {
        AuraSharedOrderedWorkQueue[] queues;
        lock (ActiveGate) queues = Active.ToArray();
        foreach (var queue in queues) queue.Pump();
    }

    public void Pump()
    {
        lock (gate)
        {
            if (running || pending.Count == 0) return;
            running = true;
        }
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<int>
        {
            OwnerId = owner, Key = name + ".fifo." + ++sequence,
            Source = owner + "." + name, Kind = kind,
            Work = _ => Drain(), ApplyOnMainThread = _ => Complete(),
            OnFailedOnMainThread = ex => { ReportFailure(ex); Complete(); },
            OnCancelledOnMainThread = _ => Complete()
        });
        if (!accepted) { lock (gate) running = false; AdmissionDeferrals++; }
    }

    private int Drain()
    {
        var count = 0;
        var started = Stopwatch.GetTimestamp();
        while (count < 32 && (count == 0 || Milliseconds(started) < 50d))
        {
            Work item;
            lock (gate) { if (pending.Count == 0) break; item = pending.Dequeue(); }
            var receipt = item.Run();
            lock (gate) receipts.Enqueue(receipt);
            count++;
        }
        return count;
    }

    private void Complete()
    {
        Receipt[] completed;
        lock (gate) { running = false; completed = receipts.ToArray(); receipts.Clear(); }
        foreach (var receipt in completed)
        {
            lock (gate) { retainedCount--; retainedBytes -= receipt.Bytes; }
            var started = Stopwatch.GetTimestamp();
            try { receipt.Apply(); }
            catch (Exception ex) { receipt.Measurement.Error = ex.Message; ReportFailure(ex); }
            finally
            {
                receipt.Measurement.ApplyMilliseconds = Milliseconds(started);
                foreach (var observer in Measured?.GetInvocationList() ?? Array.Empty<Delegate>())
                    try { ((Action<AuraSharedWorkMeasurement>)observer)(receipt.Measurement); }
                    catch (Exception ex) { ReportFailure(ex); }
            }
        }
        if (IsIdle) { lock (ActiveGate) Active.Remove(this); }
        else Pump();
    }

    private void ReportFailure(Exception error)
    {
        LastError = error.Message;
        foreach (var observer in CompletionFailed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action<Exception>)observer)(error); }
            catch (Exception secondary) { LastError = error.Message + "; observer: " + secondary.Message; }
    }

    private static double Milliseconds(long started) => (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
    private sealed class Work { internal long QueuedAt; internal long Bytes; internal Func<Receipt> Run = null!; }
    private sealed class Receipt { internal long Bytes; internal Action Apply = null!; internal AuraSharedWorkMeasurement Measurement = null!; }
}
