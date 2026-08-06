using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationSchedulerSnapshot
{
    public int WorkerCount { get; set; }

    public int ScheduledWork { get; set; }

    public int QueuedWork { get; set; }

    public int RunningWork { get; set; }

    public int CompletedWork { get; set; }

    public int CommittedWork { get; set; }

    public int PeakQueuedWork { get; set; }

    public int PeakRunningWork { get; set; }

    public long RefillCount { get; set; }

    public int SpeculativeDiscardedWork { get; set; }

    public double TailIdleCoreSeconds { get; set; }
}

internal static class CombatFoundationWorkScheduler
{
    public static CombatFoundationSchedulerSnapshot For(
        int count,
        int parallelism,
        CancellationToken cancellationToken,
        Action<int> run,
        Action<CombatFoundationSchedulerSnapshot>? progress = null)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }
        var workCount = Math.Max(0, count);
        var workers = Math.Max(1, Math.Min(workCount == 0 ? 1 : workCount,
            parallelism));
        var metrics = new SchedulerMetrics(workers, progress);
        if (workCount == 0)
        {
            return metrics.Capture();
        }

        metrics.Scheduled(workCount, initial: true);
        var partitioner = Partitioner.Create(
            Enumerable.Range(0, workCount),
            EnumerablePartitionerOptions.NoBuffering);
        Parallel.ForEach(
            partitioner,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = workers
            },
            index =>
            {
                metrics.Started();
                try
                {
                    run(index);
                }
                finally
                {
                    metrics.Completed();
                }
            });
        metrics.Committed(workCount);
        return metrics.Capture(force: true);
    }

    public static CombatFoundationOrderedWorkResult<T> RunOrdered<T>(
        int count,
        int parallelism,
        int decisionInterval,
        CancellationToken cancellationToken,
        Func<int, T> run,
        Func<int, T, T>? commit = null,
        Func<int, bool>? shouldStop = null,
        int maximumLookAhead = 0,
        Action<CombatFoundationSchedulerSnapshot>? progress = null)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }
        var workCount = Math.Max(0, count);
        if (workCount == 0)
        {
            return new CombatFoundationOrderedWorkResult<T>();
        }
        var workers = Math.Max(1, Math.Min(workCount, parallelism));
        var interval = Math.Max(1, decisionInterval);
        var lookAhead = maximumLookAhead <= 0
            ? Math.Max(workers * 2, interval + workers)
            : Math.Max(workers, maximumLookAhead);
        var metrics = new SchedulerMetrics(workers, progress);
        using var work = new BlockingCollection<int>(workers);
        using var completed = new BlockingCollection<KeyValuePair<int, T>>();
        var stopRequested = 0;
        var nextScheduled = 0;
        var nextCommitted = 0;
        var scheduledHighWater = Math.Min(workCount, workers);
        for (; nextScheduled < scheduledHighWater; nextScheduled++)
        {
            work.Add(nextScheduled, cancellationToken);
            metrics.Scheduled(1, initial: true);
        }

        var workersTasks = Enumerable.Range(0, workers)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    foreach (var index in work.GetConsumingEnumerable(
                                 cancellationToken))
                    {
                        if (Volatile.Read(ref stopRequested) != 0)
                        {
                            break;
                        }
                        metrics.Started();
                        try
                        {
                            var value = run(index);
                            completed.Add(
                                new KeyValuePair<int, T>(index, value),
                                cancellationToken);
                        }
                        finally
                        {
                            metrics.Completed();
                        }
                    }
                }
                catch
                {
                    Interlocked.Exchange(ref stopRequested, 1);
                    TryCompleteAdding(work);
                    throw;
                }
            }, CancellationToken.None))
            .ToArray();

        _ = Task.WhenAll(workersTasks).ContinueWith(
            _ => completed.CompleteAdding(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var results = new T[workCount];
        var retained = new bool[workCount];
        var pending = new SortedDictionary<int, T>();
        var stoppedEarly = false;
        try
        {
            foreach (var item in completed.GetConsumingEnumerable(
                         cancellationToken))
            {
                if (Volatile.Read(ref stopRequested) != 0)
                {
                    continue;
                }
                pending[item.Key] = item.Value;
                while (nextScheduled < workCount
                       && nextScheduled - nextCommitted < lookAhead
                       && work.TryAdd(nextScheduled))
                {
                    nextScheduled++;
                    metrics.Scheduled(1, initial: false);
                }

                while (pending.TryGetValue(nextCommitted, out var value))
                {
                    pending.Remove(nextCommitted);
                    results[nextCommitted] = commit == null
                        ? value
                        : commit(nextCommitted, value);
                    retained[nextCommitted] = true;
                    nextCommitted++;
                    metrics.Committed(1);

                    while (nextScheduled < workCount
                           && nextScheduled - nextCommitted < lookAhead
                           && work.TryAdd(nextScheduled))
                    {
                        nextScheduled++;
                        metrics.Scheduled(1, initial: false);
                    }

                    if ((nextCommitted % interval == 0
                         || nextCommitted >= workCount)
                        && shouldStop?.Invoke(nextCommitted) == true)
                    {
                        stoppedEarly = true;
                        Interlocked.Exchange(ref stopRequested, 1);
                        TryCompleteAdding(work);
                        break;
                    }
                }
                if (stoppedEarly)
                {
                    break;
                }
                if (nextScheduled >= workCount)
                {
                    TryCompleteAdding(work);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref stopRequested, 1);
            TryCompleteAdding(work);
            Task.WhenAll(workersTasks).GetAwaiter().GetResult();
        }

        var snapshot = metrics.Capture();
        snapshot.SpeculativeDiscardedWork = Math.Max(
            0,
            snapshot.CompletedWork - nextCommitted);
        progress?.Invoke(snapshot);
        var ordered = new List<T>(nextCommitted);
        for (var index = 0; index < nextCommitted; index++)
        {
            if (retained[index])
            {
                ordered.Add(results[index]);
            }
        }
        return new CombatFoundationOrderedWorkResult<T>
        {
            Items = ordered,
            StoppedEarly = stoppedEarly,
            Metrics = snapshot
        };
    }

    private static void TryCompleteAdding<T>(BlockingCollection<T> queue)
    {
        try
        {
            if (!queue.IsAddingCompleted)
            {
                queue.CompleteAdding();
            }
        }
        catch (InvalidOperationException)
        {
            // Another thread completed the producer concurrently.
        }
    }

    private sealed class SchedulerMetrics
    {
        private readonly object gate = new();
        private readonly int workers;
        private readonly Action<CombatFoundationSchedulerSnapshot>? progress;
        private long lastTimestamp = Stopwatch.GetTimestamp();
        private int scheduled;
        private int queued;
        private int running;
        private int completed;
        private int committed;
        private int peakQueued;
        private int peakRunning;
        private long refills;
        private double tailIdleCoreSeconds;

        public SchedulerMetrics(
            int workers,
            Action<CombatFoundationSchedulerSnapshot>? progress)
        {
            this.workers = Math.Max(1, workers);
            this.progress = progress;
        }

        public void Scheduled(int count, bool initial)
        {
            CombatFoundationSchedulerSnapshot snapshot;
            lock (gate)
            {
                AccumulateTailIdle();
                scheduled += Math.Max(0, count);
                queued += Math.Max(0, count);
                peakQueued = Math.Max(peakQueued, queued);
                if (!initial)
                {
                    refills += Math.Max(0, count);
                }
                snapshot = CaptureUnsafe();
            }
            progress?.Invoke(snapshot);
        }

        public void Started()
        {
            CombatFoundationSchedulerSnapshot snapshot;
            lock (gate)
            {
                AccumulateTailIdle();
                queued = Math.Max(0, queued - 1);
                running++;
                peakRunning = Math.Max(peakRunning, running);
                snapshot = CaptureUnsafe();
            }
            progress?.Invoke(snapshot);
        }

        public void Completed()
        {
            CombatFoundationSchedulerSnapshot snapshot;
            lock (gate)
            {
                AccumulateTailIdle();
                running = Math.Max(0, running - 1);
                completed++;
                snapshot = CaptureUnsafe();
            }
            progress?.Invoke(snapshot);
        }

        public void Committed(int count)
        {
            CombatFoundationSchedulerSnapshot snapshot;
            lock (gate)
            {
                AccumulateTailIdle();
                committed += Math.Max(0, count);
                snapshot = CaptureUnsafe();
            }
            progress?.Invoke(snapshot);
        }

        public CombatFoundationSchedulerSnapshot Capture(bool force = false)
        {
            lock (gate)
            {
                AccumulateTailIdle();
                var snapshot = CaptureUnsafe();
                if (force)
                {
                    progress?.Invoke(snapshot);
                }
                return snapshot;
            }
        }

        private void AccumulateTailIdle()
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = Math.Max(
                0d,
                (now - lastTimestamp) / (double)Stopwatch.Frequency);
            if (queued == 0 && running > 0 && running < workers)
            {
                tailIdleCoreSeconds += (workers - running) * elapsed;
            }
            lastTimestamp = now;
        }

        private CombatFoundationSchedulerSnapshot CaptureUnsafe()
        {
            return new CombatFoundationSchedulerSnapshot
            {
                WorkerCount = workers,
                ScheduledWork = scheduled,
                QueuedWork = queued,
                RunningWork = running,
                CompletedWork = completed,
                CommittedWork = committed,
                PeakQueuedWork = peakQueued,
                PeakRunningWork = peakRunning,
                RefillCount = refills,
                TailIdleCoreSeconds = tailIdleCoreSeconds
            };
        }
    }
}

internal sealed class CombatFoundationOrderedWorkResult<T>
{
    public List<T> Items { get; set; } = new();

    public bool StoppedEarly { get; set; }

    public CombatFoundationSchedulerSnapshot Metrics { get; set; } = new();
}
