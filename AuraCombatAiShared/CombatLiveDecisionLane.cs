using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace AuraCombatAi.Shared;

public enum CombatLiveDecisionPurpose
{
    Execution,
    Prediction,
    Shadow,
    Teacher,
    GameValidation
}

public enum CombatLiveDecisionAuthority
{
    RuleBaseline,
    Model,
    EmergencyBaseline
}

public enum CombatLiveDecisionPriority
{
    Opportunistic = 0,
    Execution = 100
}

public enum CombatLiveDecisionReceiptStatus
{
    Completed,
    Cancelled,
    Superseded,
    DeadlineExceeded,
    Faulted
}

public interface ICombatLiveDecisionWorker
{
    CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile profile,
        CombatSearchExplorationOptions? exploration,
        CancellationToken cancellationToken);

    long ReleaseRetainedMemory();
}

public sealed class CombatDecisionEngineWorker : ICombatLiveDecisionWorker
{
    private readonly CombatDecisionEngine engine;

    public CombatDecisionEngineWorker(CombatDecisionEngine engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public CombatDecision Choose(
        CombatStateObservation state,
        CombatDecisionProfile profile,
        CombatSearchExplorationOptions? exploration,
        CancellationToken cancellationToken)
    {
        return engine.ChoosePrepared(
            state,
            profile,
            exploration,
            cancellationToken);
    }

    public long ReleaseRetainedMemory()
    {
        return engine.ReleaseRetainedMemory();
    }
}

public sealed class CombatLiveDecisionRequest
{
    public long RequestId { get; internal set; }

    public long BattleSessionId { get; set; }

    public long Generation { get; set; }

    public long ObservationRevision { get; set; }

    public string StateFingerprint { get; set; } = "";

    public CombatLiveDecisionPurpose Purpose { get; set; }

    public CombatLiveDecisionAuthority Authority { get; set; }

    public CombatLiveDecisionPriority Priority { get; set; } =
        CombatLiveDecisionPriority.Opportunistic;

    public string ModelId { get; set; } = "none";

    public CombatStateObservation State { get; set; } = new();

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatSearchExplorationOptions? Exploration { get; set; }

    public ICombatLiveDecisionWorker Worker { get; set; } = null!;

    /// <summary>
    /// A hard availability guard around the normal anytime search budget.
    /// Zero uses four times the configured search budget with a 500 ms floor.
    /// </summary>
    public int HardDeadlineMilliseconds { get; set; }

    internal long SubmittedTimestamp { get; set; }
}

public sealed class CombatLiveDecisionTiming
{
    public double QueueMilliseconds { get; set; }

    public double ComputeMilliseconds { get; set; }

    public double TotalMilliseconds { get; set; }
}

public sealed class CombatLiveDecisionReceipt
{
    public long RequestId { get; internal set; }

    public long BattleSessionId { get; internal set; }

    public long Generation { get; internal set; }

    public long ObservationRevision { get; internal set; }

    public string StateFingerprint { get; internal set; } = "";

    public CombatLiveDecisionPurpose Purpose { get; internal set; }

    public CombatLiveDecisionAuthority Authority { get; internal set; }

    public string ModelId { get; internal set; } = "none";

    public CombatLiveDecisionReceiptStatus Status { get; internal set; }

    public string Reason { get; internal set; } = "";

    public CombatStateObservation State { get; internal set; } = new();

    public CombatDecision Decision { get; internal set; } = new();

    public CombatLiveDecisionTiming Timing { get; internal set; } = new();
}

public sealed class CombatLiveDecisionLaneSnapshot
{
    public bool Running { get; set; }

    public bool HasActiveRequest { get; set; }

    public long ActiveRequestId { get; set; }

    public int PendingExecutionRequests { get; set; }

    public int PendingOpportunisticRequests { get; set; }

    public long SubmittedRequests { get; set; }

    public long CompletedRequests { get; set; }

    public long CancelledRequests { get; set; }

    public long SupersededRequests { get; set; }

    public long FaultedRequests { get; set; }
}

/// <summary>
/// A single persistent live-combat CPU lane. It never uses the CLR ThreadPool,
/// execution work always wins over observation work, and every accepted request
/// publishes exactly one terminal receipt.
/// </summary>
public sealed class CombatLiveDecisionLane : IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly ConcurrentQueue<CombatLiveDecisionReceipt> receipts = new();
    private readonly Thread thread;
    private CombatLiveDecisionRequest? pendingExecution;
    private CombatLiveDecisionRequest? pendingOpportunistic;
    private CombatLiveDecisionRequest? active;
    private CancellationTokenSource? activeCancellation;
    private string activeCancellationReason = "cancelled";
    private bool disposed;
    private bool running;
    private long nextRequestId;
    private long submittedRequests;
    private long completedRequests;
    private long cancelledRequests;
    private long supersededRequests;
    private long faultedRequests;

    public CombatLiveDecisionLane(string threadName = "AuraCombatAI.LiveDecision")
    {
        thread = new Thread(WorkLoop)
        {
            IsBackground = true,
            Name = string.IsNullOrWhiteSpace(threadName)
                ? "AuraCombatAI.LiveDecision"
                : threadName.Trim(),
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
    }

    public long Submit(CombatLiveDecisionRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Worker == null) throw new ArgumentException(
            "a live decision worker is required",
            nameof(request));
        if (request.State == null) throw new ArgumentException(
            "a prepared combat state is required",
            nameof(request));

        CombatLiveDecisionRequest? superseded = null;
        lock (gate)
        {
            ThrowIfDisposed();
            request.RequestId = ++nextRequestId;
            request.SubmittedTimestamp = Stopwatch.GetTimestamp();
            submittedRequests++;
            if (request.Priority == CombatLiveDecisionPriority.Execution)
            {
                superseded = pendingExecution;
                pendingExecution = request;
                if (active != null)
                {
                    CancelActiveNoLock("superseded-by-execution");
                }
            }
            else
            {
                superseded = pendingOpportunistic;
                pendingOpportunistic = request;
                if (active?.Priority == CombatLiveDecisionPriority.Opportunistic)
                {
                    CancelActiveNoLock("superseded-by-newer-observation");
                }
            }
        }

        if (superseded != null)
        {
            PublishWithoutExecution(
                superseded,
                CombatLiveDecisionReceiptStatus.Superseded,
                "superseded-before-start");
        }
        wake.Set();
        return request.RequestId;
    }

    public int CancelSession(long battleSessionId, string reason)
    {
        CombatLiveDecisionRequest? execution = null;
        CombatLiveDecisionRequest? opportunistic = null;
        lock (gate)
        {
            if (disposed) return 0;
            if (pendingExecution?.BattleSessionId == battleSessionId)
            {
                execution = pendingExecution;
                pendingExecution = null;
            }
            if (pendingOpportunistic?.BattleSessionId == battleSessionId)
            {
                opportunistic = pendingOpportunistic;
                pendingOpportunistic = null;
            }
            if (active?.BattleSessionId == battleSessionId)
            {
                CancelActiveNoLock(reason);
            }
        }

        var cancelled = 0;
        if (execution != null)
        {
            cancelled++;
            PublishWithoutExecution(
                execution,
                CombatLiveDecisionReceiptStatus.Cancelled,
                reason);
        }
        if (opportunistic != null)
        {
            cancelled++;
            PublishWithoutExecution(
                opportunistic,
                CombatLiveDecisionReceiptStatus.Cancelled,
                reason);
        }
        wake.Set();
        return cancelled;
    }

    public bool TryTakeReceipt(out CombatLiveDecisionReceipt receipt)
    {
        return receipts.TryDequeue(out receipt!);
    }

    public CombatLiveDecisionLaneSnapshot Snapshot()
    {
        lock (gate)
        {
            return new CombatLiveDecisionLaneSnapshot
            {
                Running = running && !disposed,
                HasActiveRequest = active != null,
                ActiveRequestId = active?.RequestId ?? 0L,
                PendingExecutionRequests = pendingExecution == null ? 0 : 1,
                PendingOpportunisticRequests = pendingOpportunistic == null ? 0 : 1,
                SubmittedRequests = submittedRequests,
                CompletedRequests = completedRequests,
                CancelledRequests = cancelledRequests,
                SupersededRequests = supersededRequests,
                FaultedRequests = faultedRequests
            };
        }
    }

    public void Dispose()
    {
        CombatLiveDecisionRequest? execution;
        CombatLiveDecisionRequest? opportunistic;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            execution = pendingExecution;
            opportunistic = pendingOpportunistic;
            pendingExecution = null;
            pendingOpportunistic = null;
            CancelActiveNoLock("lane-disposed");
        }

        if (execution != null)
        {
            PublishWithoutExecution(
                execution,
                CombatLiveDecisionReceiptStatus.Cancelled,
                "lane-disposed");
        }
        if (opportunistic != null)
        {
            PublishWithoutExecution(
                opportunistic,
                CombatLiveDecisionReceiptStatus.Cancelled,
                "lane-disposed");
        }
        wake.Set();
        thread.Join(2000);
    }

    private void WorkLoop()
    {
        lock (gate) running = true;
        try
        {
            while (true)
            {
                CombatLiveDecisionRequest? request;
                CancellationTokenSource cancellation;
                lock (gate)
                {
                    if (disposed && pendingExecution == null
                                 && pendingOpportunistic == null)
                    {
                        return;
                    }
                    request = pendingExecution ?? pendingOpportunistic;
                    if (request != null)
                    {
                        if (ReferenceEquals(request, pendingExecution))
                        {
                            pendingExecution = null;
                        }
                        else
                        {
                            pendingOpportunistic = null;
                        }
                        active = request;
                        activeCancellation = new CancellationTokenSource();
                        activeCancellationReason = "cancelled";
                        cancellation = activeCancellation;
                    }
                    else
                    {
                        cancellation = null!;
                    }
                }

                if (request == null)
                {
                    wake.WaitOne(250);
                    continue;
                }

                Execute(request, cancellation);
                lock (gate)
                {
                    if (ReferenceEquals(active, request)) active = null;
                    if (ReferenceEquals(activeCancellation, cancellation))
                    {
                        activeCancellation = null;
                    }
                }
                cancellation.Dispose();
            }
        }
        finally
        {
            lock (gate) running = false;
            wake.Dispose();
        }
    }

    private void Execute(
        CombatLiveDecisionRequest request,
        CancellationTokenSource cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        var queueMilliseconds = ElapsedMilliseconds(request.SubmittedTimestamp);
        var hardDeadline = request.HardDeadlineMilliseconds > 0
            ? request.HardDeadlineMilliseconds
            : Math.Max(
                500,
                Math.Max(1, request.Profile.SearchTimeBudgetMilliseconds) * 4);
        using var deadline = new CancellationTokenSource(hardDeadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation.Token,
            deadline.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var decision = request.Worker.Choose(
                request.State,
                request.Profile,
                request.Exploration,
                linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            var compute = ElapsedMilliseconds(started);
            Publish(new CombatLiveDecisionReceipt
            {
                RequestId = request.RequestId,
                BattleSessionId = request.BattleSessionId,
                Generation = request.Generation,
                ObservationRevision = request.ObservationRevision,
                StateFingerprint = request.StateFingerprint,
                Purpose = request.Purpose,
                Authority = request.Authority,
                ModelId = request.ModelId,
                Status = CombatLiveDecisionReceiptStatus.Completed,
                State = request.State,
                Decision = decision,
                Timing = new CombatLiveDecisionTiming
                {
                    QueueMilliseconds = queueMilliseconds,
                    ComputeMilliseconds = compute,
                    TotalMilliseconds = ElapsedMilliseconds(
                        request.SubmittedTimestamp)
                }
            });
        }
        catch (OperationCanceledException)
        {
            string reason;
            lock (gate) reason = activeCancellationReason;
            var deadlineExceeded = deadline.IsCancellationRequested
                                   && !cancellation.IsCancellationRequested;
            Publish(new CombatLiveDecisionReceipt
            {
                RequestId = request.RequestId,
                BattleSessionId = request.BattleSessionId,
                Generation = request.Generation,
                ObservationRevision = request.ObservationRevision,
                StateFingerprint = request.StateFingerprint,
                Purpose = request.Purpose,
                Authority = request.Authority,
                ModelId = request.ModelId,
                Status = deadlineExceeded
                    ? CombatLiveDecisionReceiptStatus.DeadlineExceeded
                    : reason.StartsWith("superseded", StringComparison.Ordinal)
                        ? CombatLiveDecisionReceiptStatus.Superseded
                        : CombatLiveDecisionReceiptStatus.Cancelled,
                Reason = deadlineExceeded ? "hard-deadline" : reason,
                State = request.State,
                Timing = new CombatLiveDecisionTiming
                {
                    QueueMilliseconds = queueMilliseconds,
                    ComputeMilliseconds = ElapsedMilliseconds(started),
                    TotalMilliseconds = ElapsedMilliseconds(
                        request.SubmittedTimestamp)
                }
            });
        }
        catch (Exception ex)
        {
            Publish(new CombatLiveDecisionReceipt
            {
                RequestId = request.RequestId,
                BattleSessionId = request.BattleSessionId,
                Generation = request.Generation,
                ObservationRevision = request.ObservationRevision,
                StateFingerprint = request.StateFingerprint,
                Purpose = request.Purpose,
                Authority = request.Authority,
                ModelId = request.ModelId,
                Status = CombatLiveDecisionReceiptStatus.Faulted,
                Reason = ex.GetType().Name + ": " + ex.Message,
                State = request.State,
                Timing = new CombatLiveDecisionTiming
                {
                    QueueMilliseconds = queueMilliseconds,
                    ComputeMilliseconds = ElapsedMilliseconds(started),
                    TotalMilliseconds = ElapsedMilliseconds(
                        request.SubmittedTimestamp)
                }
            });
        }
    }

    private void PublishWithoutExecution(
        CombatLiveDecisionRequest request,
        CombatLiveDecisionReceiptStatus status,
        string reason)
    {
        Publish(new CombatLiveDecisionReceipt
        {
            RequestId = request.RequestId,
            BattleSessionId = request.BattleSessionId,
            Generation = request.Generation,
            ObservationRevision = request.ObservationRevision,
            StateFingerprint = request.StateFingerprint,
            Purpose = request.Purpose,
            Authority = request.Authority,
            ModelId = request.ModelId,
            Status = status,
            Reason = reason ?? "",
            State = request.State,
            Timing = new CombatLiveDecisionTiming
            {
                TotalMilliseconds = ElapsedMilliseconds(
                    request.SubmittedTimestamp)
            }
        });
    }

    private void Publish(CombatLiveDecisionReceipt receipt)
    {
        lock (gate)
        {
            switch (receipt.Status)
            {
                case CombatLiveDecisionReceiptStatus.Completed:
                    completedRequests++;
                    break;
                case CombatLiveDecisionReceiptStatus.Superseded:
                    supersededRequests++;
                    break;
                case CombatLiveDecisionReceiptStatus.Faulted:
                case CombatLiveDecisionReceiptStatus.DeadlineExceeded:
                    faultedRequests++;
                    break;
                default:
                    cancelledRequests++;
                    break;
            }
        }
        receipts.Enqueue(receipt);
    }

    private void CancelActiveNoLock(string reason)
    {
        if (activeCancellation == null) return;
        activeCancellationReason = string.IsNullOrWhiteSpace(reason)
            ? "cancelled"
            : reason.Trim();
        activeCancellation.Cancel();
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(
            nameof(CombatLiveDecisionLane));
    }

    private static double ElapsedMilliseconds(long timestamp)
    {
        if (timestamp <= 0L) return 0d;
        return (Stopwatch.GetTimestamp() - timestamp)
               * 1000d
               / Stopwatch.Frequency;
    }
}
