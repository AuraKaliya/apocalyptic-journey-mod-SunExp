using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace AuraCombatAi.Shared;

public sealed class CombatPolicyValueCandidate
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string ActionKind { get; set; } = "";

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatPolicyValueInput
{
    public Dictionary<string, double> StateFeatures { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CombatPolicyValueCandidate> Candidates { get; set; } = new();
}

public sealed class CombatPolicyValuePrediction
{
    private Dictionary<string, double>? policyLogits;
    private Dictionary<string, List<double>>? actionReturnQuantiles;
    private string[]? denseCandidateIds;
    private double[]? densePolicyLogits;
    private double[]? denseActionReturnQuantiles;
    private int denseCandidateCount;
    private int denseActionQuantileCount;

    public Dictionary<string, double> PolicyLogits
    {
        get
        {
            if (policyLogits != null)
            {
                return policyLogits;
            }
            policyLogits = new Dictionary<string, double>(
                Math.Max(0, denseCandidateCount),
                StringComparer.Ordinal);
            for (var index = 0; index < denseCandidateCount; index++)
            {
                policyLogits[denseCandidateIds![index] ?? ""] =
                    densePolicyLogits![index];
            }
            return policyLogits;
        }
        set
        {
            policyLogits = value;
            ClearDenseStorage();
        }
    }

    public Dictionary<string, List<double>> ActionReturnQuantiles
    {
        get
        {
            if (actionReturnQuantiles != null)
            {
                return actionReturnQuantiles;
            }
            actionReturnQuantiles = new Dictionary<string, List<double>>(
                Math.Max(0, denseCandidateCount),
                StringComparer.Ordinal);
            if (denseActionQuantileCount <= 0
                || denseActionReturnQuantiles == null)
            {
                return actionReturnQuantiles;
            }
            for (var candidateIndex = 0;
                 candidateIndex < denseCandidateCount;
                 candidateIndex++)
            {
                var quantiles = new List<double>(denseActionQuantileCount);
                var offset = candidateIndex * denseActionQuantileCount;
                for (var quantileIndex = 0;
                     quantileIndex < denseActionQuantileCount;
                     quantileIndex++)
                {
                    quantiles.Add(denseActionReturnQuantiles[
                        offset + quantileIndex]);
                }
                actionReturnQuantiles[
                    denseCandidateIds![candidateIndex] ?? ""] = quantiles;
            }
            return actionReturnQuantiles;
        }
        set
        {
            actionReturnQuantiles = value;
            ClearDenseStorage();
        }
    }

    public double ExpectedReturn { get; set; }

    public double WinProbability { get; set; }

    public double DeathProbability { get; set; }

    public double ExpectedRemainingHpRatio { get; set; }

    public double ExpectedRemainingTurns { get; set; }

    public double Uncertainty { get; set; }

    internal void PrepareCandidates(int count, int actionQuantileCount)
    {
        denseCandidateCount = Math.Max(0, count);
        denseActionQuantileCount = Math.Max(0, actionQuantileCount);
        denseCandidateIds = denseCandidateCount == 0
            ? Array.Empty<string>()
            : new string[denseCandidateCount];
        densePolicyLogits = denseCandidateCount == 0
            ? Array.Empty<double>()
            : new double[denseCandidateCount];
        denseActionReturnQuantiles =
            denseCandidateCount == 0 || denseActionQuantileCount == 0
                ? Array.Empty<double>()
                : new double[checked(
                    denseCandidateCount * denseActionQuantileCount)];
        policyLogits = null;
        actionReturnQuantiles = null;
    }

    internal void SetCandidate(int index, string candidateId, double policyLogit)
    {
        denseCandidateIds![index] = candidateId ?? "";
        densePolicyLogits![index] = policyLogit;
    }

    internal void SetActionQuantile(int candidateIndex, int quantileIndex,
        double value)
    {
        denseActionReturnQuantiles![
            candidateIndex * denseActionQuantileCount + quantileIndex] = value;
    }

    internal bool TryGetPolicyLogit(string candidateId, out double value)
    {
        if (policyLogits != null
            && policyLogits.TryGetValue(candidateId ?? "", out value))
        {
            return true;
        }
        var index = DenseCandidateIndex(candidateId);
        if (index >= 0)
        {
            value = densePolicyLogits![index];
            return true;
        }
        value = 0d;
        return false;
    }

    internal void SetPolicyLogit(string candidateId, double value)
    {
        var index = DenseCandidateIndex(candidateId);
        if (index >= 0)
        {
            densePolicyLogits![index] = value;
        }
        if (policyLogits != null || index < 0)
        {
            PolicyLogits[candidateId ?? ""] = value;
        }
    }

    internal bool TryGetActionQuantiles(
        string candidateId,
        out CombatPolicyValueQuantileView quantiles)
    {
        if (actionReturnQuantiles != null
            && actionReturnQuantiles.TryGetValue(
                candidateId ?? "",
                out var materialized)
            && materialized != null)
        {
            quantiles = new CombatPolicyValueQuantileView(materialized);
            return true;
        }
        var index = DenseCandidateIndex(candidateId);
        if (index >= 0
            && denseActionQuantileCount > 0
            && denseActionReturnQuantiles != null)
        {
            quantiles = new CombatPolicyValueQuantileView(
                denseActionReturnQuantiles,
                index * denseActionQuantileCount,
                denseActionQuantileCount);
            return true;
        }
        quantiles = default;
        return false;
    }

    private int DenseCandidateIndex(string? candidateId)
    {
        if (denseCandidateIds == null)
        {
            return -1;
        }
        var id = candidateId ?? "";
        for (var index = 0; index < denseCandidateCount; index++)
        {
            if (string.Equals(
                    denseCandidateIds[index],
                    id,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }

    private void ClearDenseStorage()
    {
        denseCandidateIds = null;
        densePolicyLogits = null;
        denseActionReturnQuantiles = null;
        denseCandidateCount = 0;
        denseActionQuantileCount = 0;
    }
}

internal readonly struct CombatPolicyValueQuantileView
{
    private readonly double[]? buffer;
    private readonly List<double>? list;
    private readonly int offset;

    public CombatPolicyValueQuantileView(double[] buffer, int offset, int count)
    {
        this.buffer = buffer;
        list = null;
        this.offset = offset;
        Count = count;
    }

    public CombatPolicyValueQuantileView(List<double> list)
    {
        buffer = null;
        this.list = list;
        offset = 0;
        Count = list?.Count ?? 0;
    }

    public int Count { get; }

    public double this[int index] => list != null
        ? list[index]
        : buffer![offset + index];
}

public interface ICombatPolicyValueModel
{
    string ModelId { get; }

    CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input);

    IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs);
}

public sealed class CombatPolicyValueBatchDiagnosticsSnapshot
{
    public long Requests { get; set; }

    public long BatchEvaluations { get; set; }

    public long BatchedInputs { get; set; }

    public long FullBatchEvaluations { get; set; }

    public long TimeoutFlushes { get; set; }

    public long WaitStopwatchTicks { get; set; }

    public long DirectFallbackRequests { get; set; }

    public long AdaptiveFallbackActivations { get; set; }

    public long DirectEvaluations { get; set; }

    public long DirectInputs { get; set; }

    public long DirectStopwatchTicks { get; set; }

    public long DirectAllocatedBytes { get; set; }

    public long SparseInputs { get; set; }

    public long SparseNonZeroFeatures { get; set; }

    public long SparseDenseEquivalentFeatures { get; set; }

    public long SparseWeightMultiplications { get; set; }

    public long DenseEquivalentWeightMultiplications { get; set; }

    public double AverageDirectEvaluationMicroseconds => DirectEvaluations <= 0
        ? 0d
        : DirectStopwatchTicks
          * 1_000_000d
          / Stopwatch.Frequency
          / DirectEvaluations;

    public double AverageDirectAllocatedBytes => DirectInputs <= 0
        ? 0d
        : DirectAllocatedBytes / (double)DirectInputs;

    public double AverageBatchSize => BatchEvaluations <= 0
        ? 0d
        : BatchedInputs / (double)BatchEvaluations;

    public double AverageWaitMicroseconds => Requests <= 0
        ? 0d
        : WaitStopwatchTicks
          * 1_000_000d
          / Stopwatch.Frequency
          / Requests;

    public double AverageSparseFeatureCount => SparseInputs <= 0
        ? 0d
        : SparseNonZeroFeatures / (double)SparseInputs;

    public double SparseFeatureDensity => SparseDenseEquivalentFeatures <= 0
        ? 0d
        : SparseNonZeroFeatures
          / (double)SparseDenseEquivalentFeatures;

    public double WeightMultiplicationReduction =>
        DenseEquivalentWeightMultiplications <= 0
            ? 0d
            : 1d
              - SparseWeightMultiplications
              / (double)DenseEquivalentWeightMultiplications;

    public CombatPolicyValueBatchDiagnosticsSnapshot DeltaFrom(
        CombatPolicyValueBatchDiagnosticsSnapshot? baseline)
    {
        baseline ??= new CombatPolicyValueBatchDiagnosticsSnapshot();
        return new CombatPolicyValueBatchDiagnosticsSnapshot
        {
            Requests = Math.Max(0L, Requests - baseline.Requests),
            BatchEvaluations = Math.Max(
                0L,
                BatchEvaluations - baseline.BatchEvaluations),
            BatchedInputs = Math.Max(0L, BatchedInputs - baseline.BatchedInputs),
            FullBatchEvaluations = Math.Max(
                0L,
                FullBatchEvaluations - baseline.FullBatchEvaluations),
            TimeoutFlushes = Math.Max(0L, TimeoutFlushes - baseline.TimeoutFlushes),
            WaitStopwatchTicks = Math.Max(
                0L,
                WaitStopwatchTicks - baseline.WaitStopwatchTicks),
            DirectFallbackRequests = Math.Max(
                0L,
                DirectFallbackRequests - baseline.DirectFallbackRequests),
            AdaptiveFallbackActivations = Math.Max(
                0L,
                AdaptiveFallbackActivations
                - baseline.AdaptiveFallbackActivations),
            DirectEvaluations = Math.Max(
                0L,
                DirectEvaluations - baseline.DirectEvaluations),
            DirectInputs = Math.Max(
                0L,
                DirectInputs - baseline.DirectInputs),
            DirectStopwatchTicks = Math.Max(
                0L,
                DirectStopwatchTicks - baseline.DirectStopwatchTicks),
            DirectAllocatedBytes = Math.Max(
                0L,
                DirectAllocatedBytes - baseline.DirectAllocatedBytes),
            SparseInputs = Math.Max(
                0L,
                SparseInputs - baseline.SparseInputs),
            SparseNonZeroFeatures = Math.Max(
                0L,
                SparseNonZeroFeatures - baseline.SparseNonZeroFeatures),
            SparseDenseEquivalentFeatures = Math.Max(
                0L,
                SparseDenseEquivalentFeatures
                - baseline.SparseDenseEquivalentFeatures),
            SparseWeightMultiplications = Math.Max(
                0L,
                SparseWeightMultiplications
                - baseline.SparseWeightMultiplications),
            DenseEquivalentWeightMultiplications = Math.Max(
                0L,
                DenseEquivalentWeightMultiplications
                - baseline.DenseEquivalentWeightMultiplications)
        };
    }
}

public static class CombatPolicyValueBatchDiagnostics
{
    private static long requests;
    private static long batchEvaluations;
    private static long batchedInputs;
    private static long fullBatchEvaluations;
    private static long timeoutFlushes;
    private static long waitStopwatchTicks;
    private static long directFallbackRequests;
    private static long adaptiveFallbackActivations;
    private static long directEvaluations;
    private static long directInputs;
    private static long directStopwatchTicks;
    private static long directAllocatedBytes;
    private static long sparseInputs;
    private static long sparseNonZeroFeatures;
    private static long sparseDenseEquivalentFeatures;
    private static long sparseWeightMultiplications;
    private static long denseEquivalentWeightMultiplications;

    public static CombatPolicyValueBatchDiagnosticsSnapshot Capture()
    {
        return new CombatPolicyValueBatchDiagnosticsSnapshot
        {
            Requests = Interlocked.Read(ref requests)
                       + Interlocked.Read(ref directInputs),
            BatchEvaluations = Interlocked.Read(ref batchEvaluations),
            BatchedInputs = Interlocked.Read(ref batchedInputs),
            FullBatchEvaluations = Interlocked.Read(ref fullBatchEvaluations),
            TimeoutFlushes = Interlocked.Read(ref timeoutFlushes),
            WaitStopwatchTicks = Interlocked.Read(ref waitStopwatchTicks),
            DirectFallbackRequests = Interlocked.Read(
                ref directFallbackRequests),
            AdaptiveFallbackActivations = Interlocked.Read(
                ref adaptiveFallbackActivations),
            DirectEvaluations = Interlocked.Read(ref directEvaluations),
            DirectInputs = Interlocked.Read(ref directInputs),
            DirectStopwatchTicks = Interlocked.Read(ref directStopwatchTicks),
            DirectAllocatedBytes = Interlocked.Read(ref directAllocatedBytes),
            SparseInputs = Interlocked.Read(ref sparseInputs),
            SparseNonZeroFeatures = Interlocked.Read(
                ref sparseNonZeroFeatures),
            SparseDenseEquivalentFeatures = Interlocked.Read(
                ref sparseDenseEquivalentFeatures),
            SparseWeightMultiplications = Interlocked.Read(
                ref sparseWeightMultiplications),
            DenseEquivalentWeightMultiplications = Interlocked.Read(
                ref denseEquivalentWeightMultiplications)
        };
    }

    internal static void RequestCompleted(long waitTicks)
    {
        Interlocked.Increment(ref requests);
        Interlocked.Add(ref waitStopwatchTicks, Math.Max(0L, waitTicks));
    }

    internal static void BatchCompleted(
        int count,
        int maximumBatchSize,
        bool timeoutFlush)
    {
        Interlocked.Increment(ref batchEvaluations);
        Interlocked.Add(ref batchedInputs, Math.Max(0, count));
        if (count >= maximumBatchSize)
        {
            Interlocked.Increment(ref fullBatchEvaluations);
        }
        if (timeoutFlush)
        {
            Interlocked.Increment(ref timeoutFlushes);
        }
    }

    internal static void DirectFallbackCompleted(long elapsedTicks)
    {
        Interlocked.Increment(ref requests);
        Interlocked.Increment(ref directFallbackRequests);
        Interlocked.Add(
            ref waitStopwatchTicks,
            Math.Max(0L, elapsedTicks));
    }

    internal static void DirectEvaluationCompleted(
        int count,
        long elapsedTicks,
        long allocatedBytes = 0L)
    {
        Interlocked.Increment(ref directEvaluations);
        Interlocked.Add(ref directInputs, Math.Max(0, count));
        Interlocked.Add(
            ref directStopwatchTicks,
            Math.Max(0L, elapsedTicks));
        Interlocked.Add(
            ref directAllocatedBytes,
            Math.Max(0L, allocatedBytes));
    }

    internal static void SparseInputCompleted(
        int denseFeatures,
        int sparseFeatures,
        int outputDimensions)
    {
        var safeDenseFeatures = Math.Max(0, denseFeatures);
        var safeSparseFeatures = Math.Max(0, sparseFeatures);
        var safeOutputDimensions = Math.Max(0, outputDimensions);
        Interlocked.Increment(ref sparseInputs);
        Interlocked.Add(ref sparseNonZeroFeatures, safeSparseFeatures);
        Interlocked.Add(
            ref sparseDenseEquivalentFeatures,
            safeDenseFeatures);
        Interlocked.Add(
            ref sparseWeightMultiplications,
            (long)safeSparseFeatures * safeOutputDimensions);
        Interlocked.Add(
            ref denseEquivalentWeightMultiplications,
            (long)safeDenseFeatures * safeOutputDimensions);
    }

    internal static void AdaptiveFallbackActivated()
    {
        Interlocked.Increment(ref adaptiveFallbackActivations);
    }
}

public sealed class NullCombatPolicyValueModel : ICombatPolicyValueModel
{
    public static readonly NullCombatPolicyValueModel Instance = new();

    public string ModelId => "none";

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
        return new CombatPolicyValuePrediction();
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        return Enumerable.Range(0, inputs?.Count ?? 0)
            .Select(_ => new CombatPolicyValuePrediction())
            .ToList();
    }
}

public sealed class ConcurrentBatchedCombatPolicyValueModel :
    ICombatPolicyValueModel
{
    [ThreadStatic]
    private static List<CombatPolicyValueInput>? threadInputs;

    private readonly ICombatPolicyValueModel inner;
    private readonly int maximumBatchSize;
    private readonly long coalescingTicks;
    private readonly object gate = new();
    private readonly List<BatchRequest> pending = new();
    private readonly Stack<BatchRequest> requestPool = new();
    private readonly Stack<List<BatchRequest>> batchPool = new();
    private long batchEvaluationCount;
    private long batchedInputCount;
    private long timeoutFlushCount;
    private int adaptiveFallbackActive;

    public ConcurrentBatchedCombatPolicyValueModel(
        ICombatPolicyValueModel inner,
        int maximumBatchSize,
        TimeSpan? coalescingWindow = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maximumBatchSize = Math.Max(2, maximumBatchSize);
        var window = coalescingWindow ?? TimeSpan.FromTicks(1000);
        coalescingTicks = Math.Max(
            1L,
            (long)Math.Ceiling(
                Math.Max(0d, window.TotalSeconds)
                * Stopwatch.Frequency));
    }

    public string ModelId => inner.ModelId;

    public long BatchEvaluationCount =>
        Interlocked.Read(ref batchEvaluationCount);

    public long BatchedInputCount =>
        Interlocked.Read(ref batchedInputCount);

    public bool AdaptiveFallbackActive =>
        Volatile.Read(ref adaptiveFallbackActive) != 0;

    public CombatPolicyValuePrediction Evaluate(
        CombatPolicyValueInput input)
    {
        if (AdaptiveFallbackActive)
        {
            var directStarted = Stopwatch.GetTimestamp();
            var directResult = inner.Evaluate(input);
            CombatPolicyValueBatchDiagnostics.DirectFallbackCompleted(
                Stopwatch.GetTimestamp() - directStarted);
            return directResult;
        }
        var requestStarted = Stopwatch.GetTimestamp();
        BatchRequest request;
        List<BatchRequest>? batch = null;
        var timeoutFlush = false;
        lock (gate)
        {
            request = RentRequest();
            request.Input = input;
            pending.Add(request);
            if (pending.Count >= maximumBatchSize)
            {
                batch = DrainPending();
            }
        }

        if (batch == null)
        {
            var deadline = Stopwatch.GetTimestamp() + coalescingTicks;
            var spinner = new SpinWait();
            while (!request.Completed.IsSet
                   && Stopwatch.GetTimestamp() < deadline)
            {
                if (spinner.Count < 10)
                {
                    spinner.SpinOnce();
                }
                else
                {
                    Thread.Yield();
                }
            }
            if (!request.Completed.IsSet)
            {
                lock (gate)
                {
                    if (!request.Completed.IsSet
                         && pending.Contains(request))
                    {
                        batch = DrainPending();
                        timeoutFlush = true;
                    }
                }
            }
        }

        if (batch != null)
        {
            Execute(batch, timeoutFlush);
        }
        request.Completed.Wait();
        CombatPolicyValueBatchDiagnostics.RequestCompleted(
            Stopwatch.GetTimestamp() - requestStarted);
        var result = request.Result ?? new CombatPolicyValuePrediction();
        var error = request.Error;
        lock (gate)
        {
            ReturnRequest(request);
        }
        if (error != null)
        {
            throw error;
        }
        return result;
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        if (inputs == null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }
        var count = inputs.Count;
        if (count == 0)
        {
            return Array.Empty<CombatPolicyValuePrediction>();
        }
        Interlocked.Increment(ref batchEvaluationCount);
        Interlocked.Add(ref batchedInputCount, count);
        CombatPolicyValueBatchDiagnostics.BatchCompleted(
            count,
            maximumBatchSize,
            timeoutFlush: false);
        return inner.EvaluateBatch(inputs);
    }

    private BatchRequest RentRequest()
    {
        if (requestPool.Count == 0)
        {
            return new BatchRequest();
        }
        var request = requestPool.Pop();
        request.Completed.Reset();
        return request;
    }

    private void ReturnRequest(BatchRequest request)
    {
        request.Input = null;
        request.Result = null;
        request.Error = null;
        requestPool.Push(request);
    }

    private List<BatchRequest> DrainPending()
    {
        var batch = batchPool.Count == 0
            ? new List<BatchRequest>(maximumBatchSize)
            : batchPool.Pop();
        batch.AddRange(pending);
        pending.Clear();
        return batch;
    }

    private void Execute(List<BatchRequest> batch, bool timeoutFlush)
    {
        try
        {
            var inputs = threadInputs ??= new List<CombatPolicyValueInput>(8);
            inputs.Clear();
            for (var i = 0; i < batch.Count; i++)
            {
                inputs.Add(
                    batch[i].Input ?? new CombatPolicyValueInput());
            }
            IReadOnlyList<CombatPolicyValuePrediction> results;
            try
            {
                results = inner.EvaluateBatch(inputs);
            }
            finally
            {
                inputs.Clear();
            }
            if (results.Count != batch.Count)
            {
                throw new InvalidOperationException(
                    "Policy-value batch result count mismatch.");
            }
            Interlocked.Increment(ref batchEvaluationCount);
            Interlocked.Add(ref batchedInputCount, batch.Count);
            if (timeoutFlush)
            {
                Interlocked.Increment(ref timeoutFlushCount);
            }
            CombatPolicyValueBatchDiagnostics.BatchCompleted(
                batch.Count,
                maximumBatchSize,
                timeoutFlush);
            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].Result = results[i];
            }
            TryEnableAdaptiveFallback();
        }
        catch (Exception exception)
        {
            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].Error = exception;
            }
        }
        finally
        {
            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].Completed.Set();
            }
            lock (gate)
            {
                batch.Clear();
                batchPool.Push(batch);
            }
        }
    }

    private void TryEnableAdaptiveFallback()
    {
        const long minimumBatchEvaluations = 2048L;
        var evaluations = Interlocked.Read(ref batchEvaluationCount);
        if (evaluations < minimumBatchEvaluations
            || AdaptiveFallbackActive)
        {
            return;
        }
        var averageBatchSize = Interlocked.Read(ref batchedInputCount)
                               / (double)Math.Max(1L, evaluations);
        var timeoutRate = Interlocked.Read(ref timeoutFlushCount)
                          / (double)Math.Max(1L, evaluations);
        if (averageBatchSize >= 1.15d || timeoutRate < 0.95d)
        {
            return;
        }
        if (Interlocked.CompareExchange(
                ref adaptiveFallbackActive,
                1,
                0) == 0)
        {
            CombatPolicyValueBatchDiagnostics.AdaptiveFallbackActivated();
        }
    }

    private sealed class BatchRequest
    {
        public ManualResetEventSlim Completed { get; } = new(false);

        public CombatPolicyValueInput? Input { get; set; }

        public CombatPolicyValuePrediction? Result { get; set; }

        public Exception? Error { get; set; }
    }
}

/// <summary>
/// Spreads synchronous campaign inference over independent batching queues.
/// A managed thread always selects the same lane, which preserves coalescing
/// while removing the single queue lock as parallel campaign counts grow.
/// </summary>
public sealed class ShardedBatchedCombatPolicyValueModel :
    ICombatPolicyValueModel
{
    private readonly ConcurrentBatchedCombatPolicyValueModel[] lanes;

    public ShardedBatchedCombatPolicyValueModel(
        ICombatPolicyValueModel inner,
        int laneCount,
        int maximumBatchSizePerLane,
        TimeSpan? coalescingWindow = null)
    {
        if (inner == null)
        {
            throw new ArgumentNullException(nameof(inner));
        }
        lanes = Enumerable.Range(0, Math.Max(1, laneCount))
            .Select(_ => new ConcurrentBatchedCombatPolicyValueModel(
                inner,
                maximumBatchSizePerLane,
                coalescingWindow))
            .ToArray();
    }

    public string ModelId => lanes[0].ModelId;

    public int LaneCount => lanes.Length;

    public long BatchEvaluationCount => lanes.Sum(lane =>
        lane.BatchEvaluationCount);

    public long BatchedInputCount => lanes.Sum(lane =>
        lane.BatchedInputCount);

    public CombatPolicyValuePrediction Evaluate(
        CombatPolicyValueInput input)
    {
        return CurrentLane().Evaluate(input);
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        return CurrentLane().EvaluateBatch(inputs);
    }

    private ConcurrentBatchedCombatPolicyValueModel CurrentLane()
    {
        var threadId = Thread.CurrentThread.ManagedThreadId & int.MaxValue;
        return lanes[threadId % lanes.Length];
    }
}

public sealed class CombatPolicyValueNetworkDefinition
{
    public string ModelProtocol { get; set; } = "aura.combat-policy-value.mlp.v2";

    public int ProtocolVersion { get; set; } = 2;

    public int FeatureSchemaVersion { get; set; } =
        CombatPolicyValueProtocol.FeatureSchemaVersion;

    public string ModelId { get; set; } = "";

    public string DecisionProfile { get; set; } = "balanced";

    public int StateDimensions { get; set; } = 1024;

    public int ActionDimensions { get; set; } = 1024;

    public int HiddenDimensions { get; set; } = 512;

    public string FeatureEncodingMode { get; set; } = "partitioned-v3";

    public double PolicyTemperature { get; set; } = 1d;

    public double[] StateWeights { get; set; } = Array.Empty<double>();

    public double[] StateBias { get; set; } = Array.Empty<double>();

    public double[] ActionWeights { get; set; } = Array.Empty<double>();

    public double[] ActionBias { get; set; } = Array.Empty<double>();

    public double[] PolicyWeights { get; set; } = Array.Empty<double>();

    public double PolicyBias { get; set; }

    public int ActionQuantileCount { get; set; } = 16;

    public bool ActionQuantileHeadReady { get; set; }

    public double[] ActionQuantileWeights { get; set; } = Array.Empty<double>();

    public double[] ActionQuantileBias { get; set; } = Array.Empty<double>();

    public double[] ValueWeights { get; set; } = Array.Empty<double>();

    public double ValueBias { get; set; }

    public double[] WinWeights { get; set; } = Array.Empty<double>();

    public double WinBias { get; set; }

    public double[] RiskWeights { get; set; } = Array.Empty<double>();

    public double RiskBias { get; set; }

    public double[] HpWeights { get; set; } = Array.Empty<double>();

    public double HpBias { get; set; }

    public double[] TurnWeights { get; set; } = Array.Empty<double>();

    public double TurnBias { get; set; }

    public Dictionary<string, double> Metrics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ManagedCombatPolicyValueModel : ICombatPolicyValueModel
{
    private const int ActionTowerCacheCapacity = 256;

    private static long nextCacheIdentity;
    private readonly CombatPolicyValueRuntimeDefinition definition;
    private readonly long cacheIdentity;
    private long actionTowerCacheHits;
    private long actionTowerCacheMisses;
    [ThreadStatic]
    private static InferenceWorkspace? threadWorkspace;
    [ThreadStatic]
    private static BatchInferenceWorkspace? threadBatchWorkspace;
    [ThreadStatic]
    private static ActionTowerCacheSet? threadActionTowerCaches;
    [ThreadStatic]
    private static int[]? threadSparseIndexes;

    public ManagedCombatPolicyValueModel(
        CombatPolicyValueNetworkDefinition definition,
        bool allowDiagnosticLegacySchema = false)
        : this(CombatPolicyValueArtifactProtocol.FromTrainingDefinition(
            definition,
            allowDiagnosticLegacySchema))
    {
    }

    public ManagedCombatPolicyValueModel(
        CombatPolicyValueRuntimeDefinition definition)
    {
        this.definition = definition
                          ?? throw new ArgumentNullException(nameof(definition));
        if (!CombatPolicyValueArtifactProtocol.TryValidateRuntime(
                definition,
                out var reason))
        {
            throw new ArgumentException(reason, nameof(definition));
        }
        cacheIdentity = Interlocked.Increment(ref nextCacheIdentity);
    }

    public string ModelId => string.IsNullOrWhiteSpace(definition.ModelId)
        ? "combat-policy-value"
        : definition.ModelId;

    public long ActionTowerCacheHits => Interlocked.Read(
        ref actionTowerCacheHits);

    public long ActionTowerCacheMisses => Interlocked.Read(
        ref actionTowerCacheMisses);

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
        var evaluationStarted = Stopwatch.GetTimestamp();
        var evaluationAllocatedBytes = CurrentThreadAllocatedBytes();
        input ??= new CombatPolicyValueInput();
        var workspace = threadWorkspace ??= new InferenceWorkspace();
        workspace.Prepare(
            definition.StateDimensions,
            definition.ActionDimensions,
            definition.HiddenDimensions);
        var state = workspace.State;
        var hidden = workspace.Hidden;
        var action = workspace.Action;
        var actionHidden = workspace.ActionHidden;
        {
            CombatPolicyValueEncoding.EncodeStateInto(
                input.StateFeatures,
                state,
                definition.StateDimensions,
                definition.FeatureEncodingMode);
            SparseTanhInto(
                state,
                0,
                definition.StateDimensions,
                definition.StateWeightsByInput,
                definition.StateBias,
                hidden,
                0,
                definition.HiddenDimensions);
            var result = PredictionFromHidden(
                hidden,
                0);
            result.PrepareCandidates(
                input.Candidates.Count,
                definition.ActionQuantileHeadReady
                    ? definition.ActionQuantileCount
                    : 0);
            var minimum = double.PositiveInfinity;
            var maximum = double.NegativeInfinity;
            for (var i = 0; i < input.Candidates.Count; i++)
            {
                var candidate = input.Candidates[i]
                                ?? new CombatPolicyValueCandidate();
                ResolveActionHidden(
                    candidate,
                    action,
                    0,
                    actionHidden,
                    0);
                var logit = PolicyLogit(
                    hidden,
                    0,
                    actionHidden,
                    0,
                    definition.HiddenDimensions);
                result.SetCandidate(i, candidate.CandidateId ?? "", logit);
                if (definition.ActionQuantileHeadReady)
                {
                    ActionQuantilesInto(
                        hidden,
                        0,
                        actionHidden,
                        0,
                        result,
                        i);
                }
                minimum = Math.Min(minimum, logit);
                maximum = Math.Max(maximum, logit);
            }
            result.Uncertainty = input.Candidates.Count <= 1
                ? 0d
                : 1d / (1d + Math.Max(0d, maximum - minimum));
            CombatPolicyValueBatchDiagnostics.DirectEvaluationCompleted(
                1,
                Stopwatch.GetTimestamp() - evaluationStarted,
                CurrentThreadAllocatedBytes() - evaluationAllocatedBytes);
            return result;
        }
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        var count = inputs?.Count ?? 0;
        if (count == 0)
        {
            return Array.Empty<CombatPolicyValuePrediction>();
        }
        var evaluationStarted = Stopwatch.GetTimestamp();
        var evaluationAllocatedBytes = CurrentThreadAllocatedBytes();
        var stateCount = checked(count * definition.StateDimensions);
        var hiddenCount = checked(count * definition.HiddenDimensions);
        var candidateCount = 0;
        for (var index = 0; index < count; index++)
        {
            candidateCount += inputs![index]?.Candidates.Count ?? 0;
        }
        var actionCount = checked(
            Math.Max(1, candidateCount) * definition.ActionDimensions);
        var actionHiddenCount = checked(
            Math.Max(1, candidateCount) * definition.HiddenDimensions);
        var workspace =
            threadBatchWorkspace ??= new BatchInferenceWorkspace();
        workspace.Prepare(
            stateCount,
            hiddenCount,
            actionCount,
            actionHiddenCount,
            count,
            candidateCount);
        var states = workspace.States;
        var hidden = workspace.Hidden;
        var actions = workspace.Actions;
        var actionHidden = workspace.ActionHidden;
        {
            var results = new CombatPolicyValuePrediction[count];
            var candidateOwners = workspace.CandidateOwners;
            var candidateIndexes = workspace.CandidateIndexes;
            var candidates = workspace.Candidates;
            var cursor = 0;
            for (var inputIndex = 0; inputIndex < count; inputIndex++)
            {
                var input = inputs![inputIndex]
                            ?? new CombatPolicyValueInput();
                CombatPolicyValueEncoding.EncodeStateInto(
                    input.StateFeatures,
                    states,
                    inputIndex * definition.StateDimensions,
                    definition.StateDimensions,
                    definition.FeatureEncodingMode);
                for (var inputCandidateIndex = 0;
                     inputCandidateIndex < input.Candidates.Count;
                     inputCandidateIndex++)
                {
                    var source = input.Candidates[inputCandidateIndex];
                    var candidate = source ?? new CombatPolicyValueCandidate();
                    candidateOwners[cursor] = inputIndex;
                    candidateIndexes[cursor] = inputCandidateIndex;
                    candidates[cursor] = candidate;
                    ResolveActionHidden(
                        candidate,
                        actions,
                        cursor * definition.ActionDimensions,
                        actionHidden,
                        cursor * definition.HiddenDimensions);
                    cursor++;
                }
            }
            for (var inputIndex = 0; inputIndex < count; inputIndex++)
            {
                SparseTanhInto(
                    states,
                    inputIndex * definition.StateDimensions,
                    definition.StateDimensions,
                    definition.StateWeightsByInput,
                    definition.StateBias,
                    hidden,
                    inputIndex * definition.HiddenDimensions,
                    definition.HiddenDimensions);
            }
            for (var inputIndex = 0; inputIndex < count; inputIndex++)
            {
                results[inputIndex] = PredictionFromHidden(
                    hidden,
                    inputIndex * definition.HiddenDimensions);
                results[inputIndex].PrepareCandidates(
                    inputs![inputIndex]?.Candidates.Count ?? 0,
                    definition.ActionQuantileHeadReady
                        ? definition.ActionQuantileCount
                        : 0);
            }
            var minimum = workspace.Minimum;
            var maximum = workspace.Maximum;
            for (var inputIndex = 0; inputIndex < count; inputIndex++)
            {
                minimum[inputIndex] = double.PositiveInfinity;
                maximum[inputIndex] = double.NegativeInfinity;
            }
            for (var candidateIndex = 0;
                 candidateIndex < candidateCount;
                 candidateIndex++)
            {
                var owner = candidateOwners[candidateIndex];
                var logit = PolicyLogit(
                    hidden,
                    owner * definition.HiddenDimensions,
                    actionHidden,
                    candidateIndex * definition.HiddenDimensions,
                    definition.HiddenDimensions);
                var ownerCandidateIndex = candidateIndexes[candidateIndex];
                results[owner].SetCandidate(
                    ownerCandidateIndex,
                    candidates[candidateIndex].CandidateId ?? "",
                    logit);
                if (definition.ActionQuantileHeadReady)
                {
                    ActionQuantilesInto(
                        hidden,
                        owner * definition.HiddenDimensions,
                        actionHidden,
                        candidateIndex * definition.HiddenDimensions,
                        results[owner],
                        ownerCandidateIndex);
                }
                minimum[owner] = Math.Min(minimum[owner], logit);
                maximum[owner] = Math.Max(maximum[owner], logit);
            }
            for (var inputIndex = 0; inputIndex < count; inputIndex++)
            {
                var input = inputs![inputIndex]
                            ?? new CombatPolicyValueInput();
                results[inputIndex].Uncertainty =
                    input.Candidates.Count <= 1
                        ? 0d
                        : 1d / (1d + Math.Max(
                            0d,
                            maximum[inputIndex] - minimum[inputIndex]));
            }
            Array.Clear(candidates, 0, candidateCount);
            CombatPolicyValueBatchDiagnostics.DirectEvaluationCompleted(
                count,
                Stopwatch.GetTimestamp() - evaluationStarted,
                CurrentThreadAllocatedBytes() - evaluationAllocatedBytes);
            return results;
        }
    }

    private CombatPolicyValuePrediction PredictionFromHidden(
        double[] hidden,
        int offset)
    {
        return new CombatPolicyValuePrediction
        {
            ExpectedReturn = Clamp(
                Dot(
                    hidden,
                    offset,
                    definition.ValueWeights,
                    0,
                    definition.HiddenDimensions)
                + definition.ValueBias,
                -1d,
                1d),
            WinProbability = Sigmoid(
                Dot(
                    hidden,
                    offset,
                    definition.WinWeights,
                    0,
                    definition.HiddenDimensions)
                + definition.WinBias),
            DeathProbability = Sigmoid(
                Dot(
                    hidden,
                    offset,
                    definition.RiskWeights,
                    0,
                    definition.HiddenDimensions)
                + definition.RiskBias),
            ExpectedRemainingHpRatio = Sigmoid(
                Dot(
                    hidden,
                    offset,
                    definition.HpWeights,
                    0,
                    definition.HiddenDimensions)
                + definition.HpBias),
            ExpectedRemainingTurns = Math.Max(
                0d,
                SoftPlus(
                    Dot(
                        hidden,
                        offset,
                        definition.TurnWeights,
                        0,
                        definition.HiddenDimensions)
                    + definition.TurnBias))
        };
    }

    private void ResolveActionHidden(
        CombatPolicyValueCandidate candidate,
        double[] encodedAction,
        int actionOffset,
        double[] actionHidden,
        int hiddenOffset)
    {
        var cache = (threadActionTowerCaches ??= new ActionTowerCacheSet())
            .For(cacheIdentity, definition.HiddenDimensions);
        var key = ActionTowerCacheKey.Create(candidate);
        if (cache.TryCopyTo(
                key,
                actionHidden,
                hiddenOffset,
                definition.HiddenDimensions))
        {
            Interlocked.Increment(ref actionTowerCacheHits);
            return;
        }

        Interlocked.Increment(ref actionTowerCacheMisses);
        CombatPolicyValueEncoding.EncodeCandidateInto(
            candidate,
            encodedAction,
            actionOffset,
            definition.ActionDimensions,
            definition.FeatureEncodingMode);
        SparseTanhInto(
            encodedAction,
            actionOffset,
            definition.ActionDimensions,
            definition.ActionWeightsByInput,
            definition.ActionBias,
            actionHidden,
            hiddenOffset,
            definition.HiddenDimensions);
        cache.Store(
            key,
            actionHidden,
            hiddenOffset,
            definition.HiddenDimensions);
    }

    private double PolicyLogit(
        double[] hidden,
        int hiddenOffset,
        double[] actionHidden,
        int actionOffset,
        int length)
    {
        var interaction = Interaction(
            hidden,
            hiddenOffset,
            actionHidden,
            actionOffset,
            definition.PolicyWeights,
            length);
        return Clamp(
            (interaction + definition.PolicyBias)
            / definition.PolicyTemperature,
            -30d,
            30d);
    }

    private void ActionQuantilesInto(
        double[] hidden,
        int hiddenOffset,
        double[] actionHidden,
        int actionOffset,
        CombatPolicyValuePrediction prediction,
        int candidateIndex)
    {
        for (var quantile = 0;
             quantile < definition.ActionQuantileCount;
             quantile++)
        {
            prediction.SetActionQuantile(candidateIndex, quantile, Clamp(
                Interaction(
                    hidden,
                    hiddenOffset,
                    actionHidden,
                    actionOffset,
                    definition.ActionQuantileWeights,
                    quantile * definition.HiddenDimensions,
                    definition.HiddenDimensions)
                + definition.ActionQuantileBias[quantile],
                -1d,
                1d));
        }
    }

    private static void DenseTanhBatch(
        double[] input,
        int batchCount,
        int inputDimensions,
        double[] weights,
        double[] bias,
        double[] output,
        int outputDimensions)
    {
        for (var batch = 0; batch < batchCount; batch++)
        {
            DenseTanhInto(
                input,
                batch * inputDimensions,
                inputDimensions,
                weights,
                bias,
                output,
                batch * outputDimensions,
                outputDimensions);
        }
    }

    private static void DenseTanhInto(
        double[] input,
        int inputDimensions,
        double[] weights,
        double[] bias,
        double[] output,
        int outputDimensions)
    {
        DenseTanhInto(
            input,
            0,
            inputDimensions,
            weights,
            bias,
            output,
            0,
            outputDimensions);
    }

    private static void SparseTanhInto(
        double[] input,
        int inputOffset,
        int inputDimensions,
        float[] weightsByInput,
        float[] bias,
        double[] output,
        int outputOffset,
        int outputDimensions)
    {
        for (var outputIndex = 0;
             outputIndex < outputDimensions;
             outputIndex++)
        {
            output[outputOffset + outputIndex] = bias[outputIndex];
        }
        var sparseIndexes = threadSparseIndexes;
        if (sparseIndexes == null || sparseIndexes.Length < inputDimensions)
        {
            sparseIndexes = new int[inputDimensions];
            threadSparseIndexes = sparseIndexes;
        }
        var sparseCount = 0;
        for (var inputIndex = 0; inputIndex < inputDimensions; inputIndex++)
        {
            if (input[inputOffset + inputIndex] != 0d)
            {
                sparseIndexes[sparseCount++] = inputIndex;
            }
        }
        for (var sparseIndex = 0; sparseIndex < sparseCount; sparseIndex++)
        {
            var inputIndex = sparseIndexes[sparseIndex];
            var value = input[inputOffset + inputIndex];
            var weightOffset = inputIndex * outputDimensions;
            var outputIndex = 0;
            for (; outputIndex < outputDimensions; outputIndex++)
            {
                output[outputOffset + outputIndex] +=
                    weightsByInput[weightOffset + outputIndex] * value;
            }
        }
        for (var outputIndex = 0;
             outputIndex < outputDimensions;
             outputIndex++)
        {
            output[outputOffset + outputIndex] = Math.Tanh(
                output[outputOffset + outputIndex]);
        }
        CombatPolicyValueBatchDiagnostics.SparseInputCompleted(
            inputDimensions,
            sparseCount,
            outputDimensions);
    }

    private static long CurrentThreadAllocatedBytes()
    {
#if NET8_0_OR_GREATER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0L;
#endif
    }

    private static void DenseTanhInto(
        double[] input,
        int inputOffset,
        int inputDimensions,
        double[] weights,
        double[] bias,
        double[] output,
        int outputOffset,
        int outputDimensions)
    {
        for (var outputIndex = 0;
             outputIndex < outputDimensions;
             outputIndex++)
        {
            var weightOffset = outputIndex * inputDimensions;
            var value = bias[outputIndex]
                        + Dot(
                            input,
                            inputOffset,
                            weights,
                            weightOffset,
                            inputDimensions);
            output[outputOffset + outputIndex] = Math.Tanh(value);
        }
    }

    private static double Dot(
        double[] left,
        int leftOffset,
        float[] right,
        int rightOffset,
        int length)
    {
        var result = 0d;
        var index = 0;
        for (; index < length; index++)
        {
            result += left[leftOffset + index]
                      * right[rightOffset + index];
        }
        return result;
    }

    private static double Dot(
        double[] left,
        int leftOffset,
        double[] right,
        int rightOffset,
        int length)
    {
        var result = 0d;
        for (var index = 0; index < length; index++)
        {
            result += left[leftOffset + index]
                      * right[rightOffset + index];
        }
        return result;
    }

    private static double Interaction(
        double[] state,
        int stateOffset,
        double[] action,
        int actionOffset,
        float[] weights,
        int length)
    {
        return Interaction(
            state,
            stateOffset,
            action,
            actionOffset,
            weights,
            0,
            length);
    }

    private static double Interaction(
        double[] state,
        int stateOffset,
        double[] action,
        int actionOffset,
        float[] weights,
        int weightOffset,
        int length)
    {
        var result = 0d;
        var index = 0;
        for (; index < length; index++)
        {
            result += state[stateOffset + index]
                      * action[actionOffset + index]
                      * weights[weightOffset + index];
        }
        return result;
    }

    private sealed class InferenceWorkspace
    {
        public double[] State { get; private set; } = Array.Empty<double>();

        public double[] Hidden { get; private set; } = Array.Empty<double>();

        public double[] Action { get; private set; } = Array.Empty<double>();

        public double[] ActionHidden { get; private set; } =
            Array.Empty<double>();

        public void Prepare(
            int stateDimensions,
            int actionDimensions,
            int hiddenDimensions)
        {
            if (State.Length < stateDimensions)
            {
                State = new double[stateDimensions];
            }
            if (Action.Length < actionDimensions)
            {
                Action = new double[actionDimensions];
            }
            if (Hidden.Length < hiddenDimensions)
            {
                Hidden = new double[hiddenDimensions];
                ActionHidden = new double[hiddenDimensions];
            }
        }
    }

    private sealed class BatchInferenceWorkspace
    {
        public double[] States = Array.Empty<double>();

        public double[] Hidden = Array.Empty<double>();

        public double[] Actions = Array.Empty<double>();

        public double[] ActionHidden = Array.Empty<double>();

        public int[] CandidateOwners = Array.Empty<int>();

        public int[] CandidateIndexes = Array.Empty<int>();

        public CombatPolicyValueCandidate[] Candidates =
            Array.Empty<CombatPolicyValueCandidate>();

        public double[] Minimum = Array.Empty<double>();

        public double[] Maximum = Array.Empty<double>();

        public void Prepare(
            int stateCount,
            int hiddenCount,
            int actionCount,
            int actionHiddenCount,
            int inputCount,
            int candidateCount)
        {
            Ensure(ref States, stateCount);
            Ensure(ref Hidden, hiddenCount);
            Ensure(ref Actions, actionCount);
            Ensure(ref ActionHidden, actionHiddenCount);
            Ensure(ref CandidateOwners, Math.Max(1, candidateCount));
            Ensure(ref CandidateIndexes, Math.Max(1, candidateCount));
            Ensure(ref Candidates, Math.Max(1, candidateCount));
            Ensure(ref Minimum, inputCount);
            Ensure(ref Maximum, inputCount);
        }

        private static void Ensure(ref double[] values, int length)
        {
            if (values.Length < length)
            {
                values = new double[length];
            }
        }

        private static void Ensure(ref int[] values, int length)
        {
            if (values.Length < length)
            {
                values = new int[length];
            }
        }

        private static void Ensure(
            ref CombatPolicyValueCandidate[] values,
            int length)
        {
            if (values.Length < length)
            {
                values = new CombatPolicyValueCandidate[length];
            }
        }
    }

    private sealed class ActionTowerCache
    {
        private readonly ActionTowerCacheKey[] keys;
        private readonly double[][] values;
        private readonly Dictionary<ActionTowerCacheKey, int> indexes = new();
        private long ownerIdentity;
        private int hiddenDimensions;
        private int count;
        private int replacementCursor;

        public ActionTowerCache(int capacity)
        {
            keys = new ActionTowerCacheKey[Math.Max(1, capacity)];
            values = new double[Math.Max(1, capacity)][];
        }

        public void Prepare(
            long identity,
            int dimensions)
        {
            if (ownerIdentity == identity
                && hiddenDimensions == dimensions)
            {
                return;
            }
            ownerIdentity = identity;
            hiddenDimensions = dimensions;
            indexes.Clear();
            count = 0;
            replacementCursor = 0;
        }

        public bool TryCopyTo(
            ActionTowerCacheKey key,
            double[] target,
            int offset,
            int dimensions)
        {
            if (!indexes.TryGetValue(key, out var index)
                || values[index] == null
                || values[index].Length < dimensions)
            {
                return false;
            }
            Array.Copy(values[index], 0, target, offset, dimensions);
            return true;
        }

        public void Store(
            ActionTowerCacheKey key,
            double[] source,
            int offset,
            int dimensions)
        {
            int index;
            if (indexes.TryGetValue(key, out var existing))
            {
                index = existing;
            }
            else if (count < keys.Length)
            {
                index = count++;
            }
            else
            {
                index = replacementCursor;
                indexes.Remove(keys[index]);
                replacementCursor = (replacementCursor + 1) % keys.Length;
            }
            keys[index] = key;
            indexes[key] = index;
            if (values[index] == null || values[index].Length != dimensions)
            {
                values[index] = new double[dimensions];
            }
            Array.Copy(source, offset, values[index], 0, dimensions);
        }
    }

    private sealed class ActionTowerCacheSet
    {
        private const int MaximumModelsPerThread = 4;
        private readonly long[] ownerIdentities =
            new long[MaximumModelsPerThread];
        private readonly ActionTowerCache?[] caches =
            new ActionTowerCache?[MaximumModelsPerThread];
        private int count;
        private int replacementCursor;

        public ActionTowerCache For(
            long ownerIdentity,
            int hiddenDimensions)
        {
            for (var index = 0; index < count; index++)
            {
                if (ownerIdentities[index] != ownerIdentity)
                {
                    continue;
                }
                var existing = caches[index]!;
                existing.Prepare(ownerIdentity, hiddenDimensions);
                return existing;
            }
            int slot;
            if (count < MaximumModelsPerThread)
            {
                slot = count++;
            }
            else
            {
                slot = replacementCursor;
                replacementCursor =
                    (replacementCursor + 1) % MaximumModelsPerThread;
            }
            ownerIdentities[slot] = ownerIdentity;
            var cache = caches[slot] ??= new ActionTowerCache(
                ActionTowerCacheCapacity);
            cache.Prepare(ownerIdentity, hiddenDimensions);
            return cache;
        }
    }

    private readonly struct ActionTowerCacheKey : IEquatable<ActionTowerCacheKey>
    {
        private ActionTowerCacheKey(ulong first, ulong second, int featureCount)
        {
            First = first;
            Second = second;
            FeatureCount = featureCount;
        }

        private ulong First { get; }

        private ulong Second { get; }

        private int FeatureCount { get; }

        public static ActionTowerCacheKey Create(
            CombatPolicyValueCandidate candidate)
        {
            const ulong offset = 14695981039346656037UL;
            var sourceHash = HashText(candidate?.SourceId ?? "", offset);
            var xor = 0UL;
            var sum = 0UL;
            var count = 0;
            foreach (var pair in candidate?.Features
                     ?? (IReadOnlyDictionary<string, double>)EmptyFeatureMap.Instance)
            {
                if (!CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                        pair.Key,
                        pair.Value,
                        out var sanitized))
                {
                    continue;
                }
                var pairHash = HashText(pair.Key ?? "", offset);
                pairHash = Mix(
                    pairHash
                    ^ unchecked((ulong)BitConverter.DoubleToInt64Bits(sanitized)));
                xor ^= RotateLeft(pairHash, count & 31);
                sum += pairHash * 0x9E3779B185EBCA87UL;
                count++;
            }
            return new ActionTowerCacheKey(
                Mix(sourceHash ^ xor ^ (ulong)count),
                Mix(sourceHash + sum + (ulong)count * 0xC2B2AE3D27D4EB4FUL),
                count);
        }

        public bool Equals(ActionTowerCacheKey other)
        {
            return First == other.First
                   && Second == other.Second
                   && FeatureCount == other.FeatureCount;
        }

        public override bool Equals(object? obj)
        {
            return obj is ActionTowerCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)(First ^ (First >> 32));
                hash = (hash * 397) ^ (int)(Second ^ (Second >> 32));
                return (hash * 397) ^ FeatureCount;
            }
        }

        private static ulong HashText(string value, ulong seed)
        {
            var hash = seed;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static ulong RotateLeft(ulong value, int offset)
        {
            return offset == 0
                ? value
                : (value << offset) | (value >> (64 - offset));
        }

        private sealed class EmptyFeatureMap : Dictionary<string, double>
        {
            public static readonly EmptyFeatureMap Instance = new();

            private EmptyFeatureMap()
            {
            }
        }
    }

    private static double Sigmoid(double value)
    {
        value = Clamp(value, -30d, 30d);
        return 1d / (1d + Math.Exp(-value));
    }

    private static double SoftPlus(double value)
    {
        value = Clamp(value, -30d, 30d);
        return Math.Log(1d + Math.Exp(value));
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}

public static class CombatPolicyValueNetworkValidator
{
    public static bool TryValidate(
        CombatPolicyValueNetworkDefinition? model,
        out string reason)
    {
        return TryValidate(
            model,
            out reason,
            allowDiagnosticLegacySchema: false);
    }

    public static bool TryValidate(
        CombatPolicyValueNetworkDefinition? model,
        out string reason,
        bool allowDiagnosticLegacySchema)
    {
        if (model == null
            || model.ModelProtocol != "aura.combat-policy-value.mlp.v2"
            || model.ProtocolVersion != 2
            || (!allowDiagnosticLegacySchema
                && model.FeatureSchemaVersion
                   != CombatPolicyValueProtocol.FeatureSchemaVersion)
            || (allowDiagnosticLegacySchema
                && (model.FeatureSchemaVersion < 1
                    || model.FeatureSchemaVersion
                       > CombatPolicyValueProtocol.FeatureSchemaVersion)))
        {
            reason = "策略价值模型协议不兼容";
            return false;
        }
        if (model.StateDimensions < 16
            || model.ActionDimensions < 16
            || model.HiddenDimensions < 8
            || model.StateDimensions > 2048
            || model.ActionDimensions > 2048
            || model.HiddenDimensions > 1024)
        {
            reason = "策略价值模型维度无效";
            return false;
        }
        if (model.ActionQuantileCount < 4
            || model.ActionQuantileCount > 64)
        {
            reason = "策略价值模型动作分位数数量无效";
            return false;
        }
        if (!Finite(model.PolicyTemperature)
            || model.PolicyTemperature < 0.25d
            || model.PolicyTemperature > 4d)
        {
            reason = "策略价值模型策略温度无效";
            return false;
        }
        if (!string.Equals(
                model.FeatureEncodingMode,
                "partitioned-v3",
                StringComparison.OrdinalIgnoreCase))
        {
            reason = "策略价值模型特征编码模式无效";
            return false;
        }
        if (!Length(model.StateWeights, model.StateDimensions * model.HiddenDimensions)
            || !Length(model.StateBias, model.HiddenDimensions)
            || !Length(model.ActionWeights, model.ActionDimensions * model.HiddenDimensions)
            || !Length(model.ActionBias, model.HiddenDimensions)
            || !Length(model.PolicyWeights, model.HiddenDimensions)
            || !Length(
                model.ActionQuantileWeights,
                model.HiddenDimensions * model.ActionQuantileCount)
            || !Length(model.ActionQuantileBias, model.ActionQuantileCount)
            || !Length(model.ValueWeights, model.HiddenDimensions)
            || !Length(model.WinWeights, model.HiddenDimensions)
            || !Length(model.RiskWeights, model.HiddenDimensions)
            || !Length(model.HpWeights, model.HiddenDimensions)
            || !Length(model.TurnWeights, model.HiddenDimensions))
        {
            reason = "策略价值模型权重尺寸无效";
            return false;
        }
        if (!Finite(model.StateWeights)
            || !Finite(model.StateBias)
            || !Finite(model.ActionWeights)
            || !Finite(model.ActionBias)
            || !Finite(model.PolicyWeights)
            || !Finite(model.ActionQuantileWeights)
            || !Finite(model.ActionQuantileBias)
            || !Finite(model.ValueWeights)
            || !Finite(model.WinWeights)
            || !Finite(model.RiskWeights)
            || !Finite(model.HpWeights)
            || !Finite(model.TurnWeights)
            || !Finite(model.PolicyBias)
            || !Finite(model.ValueBias)
            || !Finite(model.WinBias)
            || !Finite(model.RiskBias)
            || !Finite(model.HpBias)
            || !Finite(model.TurnBias))
        {
            reason = "策略价值模型包含非有限权重";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool Length(double[]? value, int expected)
    {
        return value != null && value.Length == expected;
    }

    private static bool Finite(IEnumerable<double> values)
    {
        return values.All(value => !double.IsNaN(value) && !double.IsInfinity(value));
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed class CombatFeatureCollisionTelemetry
{
    public int FeatureCount { get; set; }

    public int UniqueBucketCount { get; set; }

    public int CollisionCount => Math.Max(0, FeatureCount - UniqueBucketCount);

    public double CollisionRate => FeatureCount == 0
        ? 0d
        : (double)CollisionCount / FeatureCount;
}

public static class CombatPolicyValueEncoding
{
    [ThreadStatic]
    private static CombatCompactFeatureBuilder? threadCompactStateBuilder;

    [ThreadStatic]
    private static CombatCompactFeatureBuilder? threadCompactCandidateBuilder;

    private static class FeatureKeys
    {
        private static readonly ConcurrentDictionary<string, string>
            PlayerStatuses = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            Enemies = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            EnemyHp = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            EnemyStatuses = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            Deck = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            Hand = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            RetainedHand = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            Discard = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string>
            Exhaust = new(StringComparer.Ordinal);

        public static string PlayerStatus(string id) =>
            PlayerStatuses.GetOrAdd(id ?? "", value => "playerStatus:" + value);

        public static string Enemy(string id) =>
            Enemies.GetOrAdd(id ?? "", value => "enemy:" + value);

        public static string EnemyHealth(string id) =>
            EnemyHp.GetOrAdd(id ?? "", value => "enemyHp:" + value);

        public static string EnemyStatus(string id) =>
            EnemyStatuses.GetOrAdd(id ?? "", value => "enemyStatus:" + value);

        public static string DeckCard(string id) =>
            Deck.GetOrAdd(id ?? "", value => "deck:" + value);

        public static string HandCard(string id) =>
            Hand.GetOrAdd(id ?? "", value => "hand:" + value);

        public static string RetainedHandCard(string id) =>
            RetainedHand.GetOrAdd(id ?? "", value => "retainedHand:" + value);

        public static string DiscardCard(string id) =>
            Discard.GetOrAdd(id ?? "", value => "discard:" + value);

        public static string ExhaustCard(string id) =>
            Exhaust.GetOrAdd(id ?? "", value => "exhaust:" + value);
    }

    private static readonly Dictionary<string, int> CoreStateIndexes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["playerHp"] = 0,
        ["playerMaxHp"] = 1,
        ["playerDefend"] = 2,
        ["power"] = 3,
        ["maxPower"] = 4,
        ["handCount"] = 5,
        ["enemyCount"] = 6,
        ["enemyHpTotal"] = 7,
        ["expectedIncomingDamage"] = 8,
        ["expectedBlockableDamage"] = 9,
        ["expectedUnblockableDamage"] = 10,
        ["expectedDamageOverTime"] = 11,
        ["turn"] = 12
    };

    private static readonly Dictionary<string, int> CoreActionIndexes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["cost"] = 0,
        ["ruleScore"] = 1,
        ["baseRuleScore"] = 2,
        ["planScore"] = 3,
        ["damage"] = 4,
        ["trueDamage"] = 5,
        ["damageOverTime"] = 6,
        ["selfHpLoss"] = 7,
        ["endOfCycleSelfHpLoss"] = 8,
        ["hitCount"] = 9,
        ["defend"] = 10,
        ["heal"] = 11,
        ["draw"] = 12,
        ["energyGain"] = 13,
        ["buff"] = 14,
        ["debuff"] = 15,
        ["cleanse"] = 16,
        ["costReduction"] = 17,
        ["cardGeneration"] = 18,
        ["persistentValue"] = 19,
        ["scaling"] = 20,
        ["risk"] = 21,
        ["uncertainty"] = 22
    };

    public static double[] Encode(
        IReadOnlyDictionary<string, double>? values,
        int dimensions,
        string prefix)
    {
        var result = new double[Math.Max(1, dimensions)];
        foreach (var pair in values ?? new Dictionary<string, double>())
        {
            Add(result, prefix + ":" + pair.Key, Normalize(pair.Value));
        }
        return result;
    }

    public static double[] EncodeState(
        IReadOnlyDictionary<string, double>? values,
        int dimensions)
    {
        return EncodeState(values, dimensions, "partitioned-v3");
    }

    public static double[] EncodeState(
        IReadOnlyDictionary<string, double>? values,
        int dimensions,
        string encodingMode)
    {
        var result = new double[Math.Max(1, dimensions)];
        EncodeStateInto(
            values,
            result,
            result.Length,
            encodingMode);
        return result;
    }

    public static void EncodeStateInto(
        IReadOnlyDictionary<string, double>? values,
        double[] target,
        int dimensions,
        string encodingMode)
    {
        EncodeStateInto(values, target, 0, dimensions, encodingMode);
    }

    public static void EncodeStateInto(
        IReadOnlyDictionary<string, double>? values,
        double[] target,
        int offset,
        int dimensions,
        string encodingMode)
    {
        RequireCurrentEncoding(encodingMode);
        var safeDimensions = Math.Max(
            1,
            Math.Min(dimensions, target.Length - offset));
        Array.Clear(target, offset, safeDimensions);
        foreach (var pair in values
                 ?? (IReadOnlyDictionary<string, double>)EmptyFeatures.Instance)
        {
            if (!CombatPublicFeaturePolicy.TrySanitizeStateFeature(
                    pair.Key,
                    pair.Value,
                    out var sanitizedValue))
            {
                continue;
            }
            if (TryCoreStateIndex(
                    pair.Key,
                    safeDimensions,
                    out var coreIndex))
            {
                target[offset + coreIndex] += Normalize(sanitizedValue);
                continue;
            }
            var range = StateRange(pair.Key, safeDimensions);
            AddRange(
                target,
                offset + range.Start,
                range.Length,
                "state",
                pair.Key,
                Normalize(sanitizedValue),
                offset + safeDimensions);
        }
    }

    internal static void EncodeStateInto(
        CombatCompactFeatureVector values,
        double[] target,
        int dimensions,
        string encodingMode)
    {
        RequireCurrentEncoding(encodingMode);
        var safeDimensions = Math.Max(1, Math.Min(dimensions, target.Length));
        Array.Clear(target, 0, safeDimensions);
        for (var index = 0; index < values.Count; index++)
        {
            if (!CombatFeatureTokenRegistry.TryResolve(
                    values.TokenIds[index],
                    out var key)
                || !CombatPublicFeaturePolicy.TrySanitizeStateFeature(
                    key,
                    values.Values[index],
                    out var sanitizedValue))
            {
                continue;
            }
            if (TryCoreStateIndex(key, safeDimensions, out var coreIndex))
            {
                target[coreIndex] += Normalize(sanitizedValue);
                continue;
            }
            var range = StateRange(key, safeDimensions);
            AddRange(
                target,
                range.Start,
                range.Length,
                "state",
                key,
                Normalize(sanitizedValue),
                safeDimensions);
        }
    }

    public static Dictionary<string, double> SanitizeStateFeatures(
        IReadOnlyDictionary<string, double>? values)
    {
        return CombatPublicFeaturePolicy.SanitizeState(values);
    }

    public static bool IsPermittedStateFeature(string? key)
    {
        return CombatPublicFeaturePolicy.TrySanitizeStateFeature(
            key,
            1d,
            out _);
    }

    public static CombatFeatureCollisionTelemetry MeasureStateCollisions(
        IReadOnlyDictionary<string, double>? values,
        int dimensions)
    {
        var buckets = new HashSet<int>();
        var count = 0;
        foreach (var pair in values
                 ?? (IReadOnlyDictionary<string, double>)EmptyFeatures.Instance)
        {
            if (!CombatPublicFeaturePolicy.TrySanitizeStateFeature(
                    pair.Key,
                    pair.Value,
                    out _))
            {
                continue;
            }
            count++;
            buckets.Add(StateIndex(pair.Key, Math.Max(1, dimensions)));
        }
        return new CombatFeatureCollisionTelemetry
        {
            FeatureCount = count,
            UniqueBucketCount = buckets.Count
        };
    }

    internal static CombatFeatureCollisionTelemetry MeasureStateCollisionsForFrame(
        CombatEpisodeFrame frame,
        int dimensions)
    {
        if (frame.CompactStateFeatures == null)
        {
            return MeasureStateCollisions(frame.StateFeatures, dimensions);
        }
        var buckets = new HashSet<int>();
        var count = 0;
        var values = frame.CompactStateFeatures;
        for (var index = 0; index < values.Count; index++)
        {
            if (!CombatFeatureTokenRegistry.TryResolve(
                    values.TokenIds[index],
                    out var key)
                || !CombatPublicFeaturePolicy.TrySanitizeStateFeature(
                    key,
                    values.Values[index],
                    out _))
            {
                continue;
            }
            count++;
            buckets.Add(StateIndex(key, Math.Max(1, dimensions)));
        }
        return new CombatFeatureCollisionTelemetry
        {
            FeatureCount = count,
            UniqueBucketCount = buckets.Count
        };
    }

    public static CombatFeatureCollisionTelemetry MeasureCandidateCollisions(
        CombatPolicyValueCandidate candidate,
        int dimensions)
    {
        var safeDimensions = Math.Max(1, dimensions);
        var buckets = new HashSet<int>();
        var count = 0;
        foreach (var pair in candidate.Features)
        {
            if (!CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                    pair.Key,
                    pair.Value,
                    out _))
            {
                continue;
            }
            count++;
            buckets.Add(ActionIndex(pair.Key, safeDimensions));
        }
        count++;
        buckets.Add(SparseActionIndex(
            "source",
            candidate.SourceId ?? "",
            safeDimensions));
        return new CombatFeatureCollisionTelemetry
        {
            FeatureCount = count,
            UniqueBucketCount = buckets.Count
        };
    }

    internal static CombatFeatureCollisionTelemetry MeasureCandidateCollisions(
        CombatEpisodeCandidate candidate,
        int dimensions)
    {
        if (candidate.CompactFeatures == null)
        {
            return MeasureCandidateCollisions(
                new CombatPolicyValueCandidate
                {
                    CandidateId = candidate.CandidateId,
                    SourceId = candidate.SourceId,
                    Features = candidate.Features
                },
                dimensions);
        }
        var safeDimensions = Math.Max(1, dimensions);
        var buckets = new HashSet<int>();
        var count = 0;
        var values = candidate.CompactFeatures;
        for (var index = 0; index < values.Count; index++)
        {
            if (!CombatFeatureTokenRegistry.TryResolve(
                    values.TokenIds[index],
                    out var key)
                || !CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                    key,
                    values.Values[index],
                    out _))
            {
                continue;
            }
            count++;
            buckets.Add(ActionIndex(key, safeDimensions));
        }
        count++;
        buckets.Add(SparseActionIndex(
            "source",
            candidate.SourceId ?? "",
            safeDimensions));
        return new CombatFeatureCollisionTelemetry
        {
            FeatureCount = count,
            UniqueBucketCount = buckets.Count
        };
    }

    public static double[] EncodeCandidate(
        CombatPolicyValueCandidate candidate,
        int dimensions,
        string encodingMode = "partitioned-v3")
    {
        var result = new double[Math.Max(1, dimensions)];
        EncodeCandidateInto(
            candidate,
            result,
            result.Length,
            encodingMode);
        return result;
    }

    public static void EncodeCandidateInto(
        CombatPolicyValueCandidate candidate,
        double[] target,
        int dimensions,
        string encodingMode = "partitioned-v3")
    {
        EncodeCandidateInto(
            candidate,
            target,
            0,
            dimensions,
            encodingMode);
    }

    public static void EncodeCandidateInto(
        CombatPolicyValueCandidate candidate,
        double[] target,
        int offset,
        int dimensions,
        string encodingMode = "partitioned-v3")
    {
        RequireCurrentEncoding(encodingMode);
        var safeDimensions = Math.Max(
            1,
            Math.Min(dimensions, target.Length - offset));
        Array.Clear(target, offset, safeDimensions);
        foreach (var pair in candidate.Features)
        {
            if (!CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                    pair.Key,
                    pair.Value,
                    out var sanitizedValue))
            {
                continue;
            }
            if (TryCoreActionIndex(
                    pair.Key,
                    safeDimensions,
                    out var coreIndex))
            {
                target[offset + coreIndex] += Normalize(sanitizedValue);
                continue;
            }
            var sparseStart = Math.Min(24, safeDimensions - 1);
            AddRange(
                target,
                offset + sparseStart,
                Math.Max(1, safeDimensions - sparseStart),
                "action",
                pair.Key,
                Normalize(sanitizedValue),
                offset + safeDimensions);
        }
        var sourceStart = Math.Min(24, safeDimensions - 1);
        AddRange(
            target,
            offset + sourceStart,
            Math.Max(1, safeDimensions - sourceStart),
            "source",
            candidate.SourceId ?? "",
            1d,
            offset + safeDimensions);
    }

    internal static void EncodeCandidateInto(
        CombatCompactFeatureVector values,
        string sourceId,
        double[] target,
        int dimensions,
        string encodingMode)
    {
        RequireCurrentEncoding(encodingMode);
        var safeDimensions = Math.Max(1, Math.Min(dimensions, target.Length));
        Array.Clear(target, 0, safeDimensions);
        for (var index = 0; index < values.Count; index++)
        {
            if (!CombatFeatureTokenRegistry.TryResolve(
                    values.TokenIds[index],
                    out var key)
                || !CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                    key,
                    values.Values[index],
                    out var sanitizedValue))
            {
                continue;
            }
            if (TryCoreActionIndex(key, safeDimensions, out var coreIndex))
            {
                target[coreIndex] += Normalize(sanitizedValue);
                continue;
            }
            var sparseStart = Math.Min(24, safeDimensions - 1);
            AddRange(
                target,
                sparseStart,
                Math.Max(1, safeDimensions - sparseStart),
                "action",
                key,
                Normalize(sanitizedValue),
                safeDimensions);
        }
        var sourceStart = Math.Min(24, safeDimensions - 1);
        AddRange(
            target,
            sourceStart,
            Math.Max(1, safeDimensions - sourceStart),
            "source",
            sourceId ?? "",
            1d,
            safeDimensions);
    }

    private static void RequireCurrentEncoding(string encodingMode)
    {
        if (!string.Equals(
                encodingMode,
                "partitioned-v3",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "只接受当前策略价值特征编码 partitioned-v3",
                nameof(encodingMode));
        }
    }

    private sealed class EmptyFeatures : Dictionary<string, double>
    {
        public static readonly EmptyFeatures Instance = new();

        private EmptyFeatures()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }

    public static CombatPolicyValueInput BuildInput(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation>? candidates = null)
    {
        var input = new CombatPolicyValueInput();
        BuildInputInto(input, state, candidates);
        return input;
    }

    public static void BuildInputInto(
        CombatPolicyValueInput input,
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation>? candidates = null)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        input.StateFeatures ??= new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        input.Candidates ??= new List<CombatPolicyValueCandidate>();
        BuildStateFeaturesInto(input.StateFeatures, state);
        BuildCandidatesInto(input.Candidates, candidates);
    }

    public static void BuildCandidatesInto(
        List<CombatPolicyValueCandidate> targetCandidates,
        IReadOnlyList<CombatCandidateEvaluation>? candidates)
    {
        if (targetCandidates == null)
        {
            throw new ArgumentNullException(nameof(targetCandidates));
        }
        var writeIndex = 0;
        foreach (var candidate in candidates ?? Array.Empty<CombatCandidateEvaluation>())
        {
            if (candidate == null || !candidate.Legal || candidate.Action == null)
            {
                continue;
            }
            CombatPolicyValueCandidate target;
            if (writeIndex < targetCandidates.Count)
            {
                target = targetCandidates[writeIndex]
                         ?? new CombatPolicyValueCandidate();
                targetCandidates[writeIndex] = target;
            }
            else
            {
                target = new CombatPolicyValueCandidate();
                targetCandidates.Add(target);
            }
            target.CandidateId = candidate.Action.CandidateId;
            target.SourceId = candidate.Action.SourceId;
            target.ActionKind = ActionKindName(candidate.Action.Kind);
            target.Features ??= new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            BuildCandidateFeaturesInto(target.Features, candidate);
            writeIndex++;
        }
        if (targetCandidates.Count > writeIndex)
        {
            targetCandidates.RemoveRange(
                writeIndex,
                targetCandidates.Count - writeIndex);
        }
    }

    public static Dictionary<string, double> BuildStateFeatures(CombatStateObservation state)
    {
        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        BuildStateFeaturesInto(result, state);
        return result;
    }

    public static void BuildStateFeaturesInto(
        Dictionary<string, double> result,
        CombatStateObservation state)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Clear();
        foreach (var pair in state?.Features
                 ?? (IReadOnlyDictionary<string, double>)EmptyFeatures.Instance)
        {
            if (CombatPublicFeaturePolicy.TrySanitizeStateFeature(
                    pair.Key,
                    pair.Value,
                    out var sanitized))
            {
                result[pair.Key] = sanitized;
            }
        }
        if (state == null)
        {
            return;
        }
        result["playerHp"] = state.Player?.CurrentHp ?? 0;
        result["playerMaxHp"] = state.Player?.MaxHp ?? 0;
        result["playerDefend"] = state.Player?.Defend ?? 0;
        result["power"] = state.CurrentPower;
        result["maxPower"] = state.MaxPower;
        result["handCount"] = state.HandCount;
        result["enemyCount"] = state.Enemies?.Count ?? 0;
        var enemyHpTotal = 0;
        if (state.Enemies != null)
        {
            for (var index = 0; index < state.Enemies.Count; index++)
            {
                enemyHpTotal += Math.Max(0, state.Enemies[index].CurrentHp);
            }
        }
        result["enemyHpTotal"] = enemyHpTotal;
        result["expectedIncomingDamage"] = state.ExpectedIncomingDamage;
        result["turn"] = Value(state.Features, "turn");
        foreach (var status in (IReadOnlyList<CombatStatusObservation>?)
                     state.Player?.Statuses
                 ?? Array.Empty<CombatStatusObservation>())
        {
            Add(result, FeatureKeys.PlayerStatus(status.StatusId), status.Level);
        }
        foreach (var enemy in (IReadOnlyList<CombatUnitObservation>?)
                     state.Enemies
                 ?? Array.Empty<CombatUnitObservation>())
        {
            if (!string.IsNullOrWhiteSpace(enemy.DefinitionId))
            {
                Add(result, FeatureKeys.Enemy(enemy.DefinitionId), 1d);
                Add(
                    result,
                    FeatureKeys.EnemyHealth(enemy.DefinitionId),
                    enemy.CurrentHp);
            }
            foreach (var status in (IReadOnlyList<CombatStatusObservation>?)
                         enemy.Statuses
                     ?? Array.Empty<CombatStatusObservation>())
            {
                Add(
                    result,
                    FeatureKeys.EnemyStatus(status.StatusId),
                    status.Level);
            }
        }
        foreach (var id in (IReadOnlyList<string>?)state.DeckCardIds
                 ?? Array.Empty<string>())
        {
            Add(result, FeatureKeys.DeckCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.HandCardIds
                 ?? Array.Empty<string>())
        {
            Add(result, FeatureKeys.HandCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.RetainedHandCardIds
                 ?? Array.Empty<string>())
        {
            Add(result, FeatureKeys.RetainedHandCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.DiscardPileCardIds
                 ?? Array.Empty<string>())
        {
            Add(result, FeatureKeys.DiscardCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.ExhaustPileCardIds
                 ?? Array.Empty<string>())
        {
            Add(result, FeatureKeys.ExhaustCard(id), 1d);
        }
    }

    internal static CombatCompactFeatureVector BuildCompactStateFeatures(
        CombatStateObservation state)
    {
        var builder = threadCompactStateBuilder ??=
            new CombatCompactFeatureBuilder();
        builder.Clear();
        foreach (var pair in state?.Features
                 ?? (IReadOnlyDictionary<string, double>)EmptyFeatures.Instance)
        {
            if (CombatPublicFeaturePolicy.TrySanitizeStateFeature(
                    pair.Key,
                    pair.Value,
                    out var sanitized))
            {
                builder.Set(pair.Key, sanitized);
            }
        }
        if (state == null)
        {
            return builder.Build();
        }
        builder.Set("playerHp", state.Player?.CurrentHp ?? 0);
        builder.Set("playerMaxHp", state.Player?.MaxHp ?? 0);
        builder.Set("playerDefend", state.Player?.Defend ?? 0);
        builder.Set("power", state.CurrentPower);
        builder.Set("maxPower", state.MaxPower);
        builder.Set("handCount", state.HandCount);
        builder.Set("enemyCount", state.Enemies?.Count ?? 0);
        var enemyHpTotal = 0;
        if (state.Enemies != null)
        {
            for (var index = 0; index < state.Enemies.Count; index++)
            {
                enemyHpTotal += Math.Max(0, state.Enemies[index].CurrentHp);
            }
        }
        builder.Set("enemyHpTotal", enemyHpTotal);
        builder.Set("expectedIncomingDamage", state.ExpectedIncomingDamage);
        builder.Set("turn", Value(state.Features, "turn"));
        foreach (var status in (IReadOnlyList<CombatStatusObservation>?)
                     state.Player?.Statuses
                 ?? Array.Empty<CombatStatusObservation>())
        {
            builder.Add(FeatureKeys.PlayerStatus(status.StatusId), status.Level);
        }
        foreach (var enemy in (IReadOnlyList<CombatUnitObservation>?)
                     state.Enemies
                 ?? Array.Empty<CombatUnitObservation>())
        {
            if (!string.IsNullOrWhiteSpace(enemy.DefinitionId))
            {
                builder.Add(FeatureKeys.Enemy(enemy.DefinitionId), 1d);
                builder.Add(
                    FeatureKeys.EnemyHealth(enemy.DefinitionId),
                    enemy.CurrentHp);
            }
            foreach (var status in (IReadOnlyList<CombatStatusObservation>?)
                         enemy.Statuses
                     ?? Array.Empty<CombatStatusObservation>())
            {
                builder.Add(
                    FeatureKeys.EnemyStatus(status.StatusId),
                    status.Level);
            }
        }
        foreach (var id in (IReadOnlyList<string>?)state.DeckCardIds
                 ?? Array.Empty<string>())
        {
            builder.Add(FeatureKeys.DeckCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.HandCardIds
                 ?? Array.Empty<string>())
        {
            builder.Add(FeatureKeys.HandCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.RetainedHandCardIds
                 ?? Array.Empty<string>())
        {
            builder.Add(FeatureKeys.RetainedHandCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.DiscardPileCardIds
                 ?? Array.Empty<string>())
        {
            builder.Add(FeatureKeys.DiscardCard(id), 1d);
        }
        foreach (var id in (IReadOnlyList<string>?)state.ExhaustPileCardIds
                 ?? Array.Empty<string>())
        {
            builder.Add(FeatureKeys.ExhaustCard(id), 1d);
        }
        return builder.Build();
    }

    public static Dictionary<string, double> BuildCandidateFeatures(
        CombatCandidateEvaluation candidate)
    {
        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        BuildCandidateFeaturesInto(result, candidate);
        return result;
    }

    public static void BuildCandidateFeaturesInto(
        Dictionary<string, double> result,
        CombatCandidateEvaluation candidate)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Clear();
        var action = candidate?.Action ?? new CombatActionObservation();
        foreach (var pair in action.Features
                 ?? (IReadOnlyDictionary<string, double>)EmptyFeatures.Instance)
        {
            if (CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                    pair.Key,
                    pair.Value,
                    out var sanitized))
            {
                result[pair.Key] = sanitized;
            }
        }
        result["cost"] = action.Cost;
        result["ruleScore"] = candidate?.RuleScore ?? 0d;
        result["baseRuleScore"] = candidate?.BaseRuleScore ?? 0d;
        result["planScore"] = candidate?.PlanScore ?? 0d;
        var semantics = action.Semantics ?? new CombatActionSemantics();
        result["damage"] = semantics.Damage;
        result["trueDamage"] = semantics.TrueDamage;
        result["damageOverTime"] = semantics.DamageOverTime;
        result["immediateHpDamage"] =
            CombatActionSemanticMetrics.ImmediateHpDamage(semantics);
        result["immediateDurabilityDamage"] =
            semantics.ImmediateDurabilityDamage;
        result["deferredHpDamage"] =
            CombatActionSemanticMetrics.DeferredHpDamage(semantics);
        result["affectedEnemyCount"] = semantics.AffectedEnemyCount;
        result["selfHpLoss"] = semantics.SelfHpLoss;
        result["endOfCycleSelfHpLoss"] =
            semantics.EndOfCycleSelfHpLoss;
        result["hitCount"] = semantics.HitCount;
        result["defend"] = semantics.Defend;
        result["heal"] = semantics.Heal;
        result["draw"] = semantics.Draw;
        result["energyGain"] = semantics.EnergyGain;
        result["buff"] = semantics.Buff;
        result["debuff"] = semantics.Debuff;
        result["cleanse"] = semantics.Cleanse;
        result["costReduction"] = semantics.CostReduction;
        result["cardGeneration"] = semantics.CardGeneration;
        result["persistentValue"] = semantics.PersistentValue;
        result["scaling"] = semantics.Scaling;
        result["risk"] = semantics.Risk;
        result["uncertainty"] = semantics.Uncertainty;
        result["endsTurn"] = semantics.EndsTurn ? 1d : 0d;
        result["damageToBlockSetup"] =
            semantics.DamageToBlockSetup ? 1d : 0d;
        result["actionKindPlayCard"] =
            action.Kind == CombatActionKind.PlayCard ? 1d : 0d;
        result["actionKindUseSkill"] =
            action.Kind == CombatActionKind.UseSkill ? 1d : 0d;
        result["actionKindEndTurn"] =
            action.Kind == CombatActionKind.EndTurn ? 1d : 0d;
    }

    internal static CombatCompactFeatureVector BuildCompactCandidateFeatures(
        CombatCandidateEvaluation candidate)
    {
        var builder = threadCompactCandidateBuilder ??=
            new CombatCompactFeatureBuilder();
        builder.Clear();
        var action = candidate?.Action ?? new CombatActionObservation();
        foreach (var pair in action.Features
                 ?? (IReadOnlyDictionary<string, double>)EmptyFeatures.Instance)
        {
            if (CombatPublicFeaturePolicy.TrySanitizeActionFeature(
                    pair.Key,
                    pair.Value,
                    out var sanitized))
            {
                builder.Set(pair.Key, sanitized);
            }
        }
        builder.Set("cost", action.Cost);
        builder.Set("ruleScore", candidate?.RuleScore ?? 0d);
        builder.Set("baseRuleScore", candidate?.BaseRuleScore ?? 0d);
        builder.Set("planScore", candidate?.PlanScore ?? 0d);
        var semantics = action.Semantics ?? new CombatActionSemantics();
        builder.Set("damage", semantics.Damage);
        builder.Set("trueDamage", semantics.TrueDamage);
        builder.Set("damageOverTime", semantics.DamageOverTime);
        builder.Set(
            "immediateHpDamage",
            CombatActionSemanticMetrics.ImmediateHpDamage(semantics));
        builder.Set(
            "immediateDurabilityDamage",
            semantics.ImmediateDurabilityDamage);
        builder.Set(
            "deferredHpDamage",
            CombatActionSemanticMetrics.DeferredHpDamage(semantics));
        builder.Set("affectedEnemyCount", semantics.AffectedEnemyCount);
        builder.Set("selfHpLoss", semantics.SelfHpLoss);
        builder.Set("endOfCycleSelfHpLoss", semantics.EndOfCycleSelfHpLoss);
        builder.Set("hitCount", semantics.HitCount);
        builder.Set("defend", semantics.Defend);
        builder.Set("heal", semantics.Heal);
        builder.Set("draw", semantics.Draw);
        builder.Set("energyGain", semantics.EnergyGain);
        builder.Set("buff", semantics.Buff);
        builder.Set("debuff", semantics.Debuff);
        builder.Set("cleanse", semantics.Cleanse);
        builder.Set("costReduction", semantics.CostReduction);
        builder.Set("cardGeneration", semantics.CardGeneration);
        builder.Set("persistentValue", semantics.PersistentValue);
        builder.Set("scaling", semantics.Scaling);
        builder.Set("risk", semantics.Risk);
        builder.Set("uncertainty", semantics.Uncertainty);
        builder.Set("endsTurn", semantics.EndsTurn ? 1d : 0d);
        builder.Set(
            "damageToBlockSetup",
            semantics.DamageToBlockSetup ? 1d : 0d);
        builder.Set(
            "actionKindPlayCard",
            action.Kind == CombatActionKind.PlayCard ? 1d : 0d);
        builder.Set(
            "actionKindUseSkill",
            action.Kind == CombatActionKind.UseSkill ? 1d : 0d);
        builder.Set(
            "actionKindEndTurn",
            action.Kind == CombatActionKind.EndTurn ? 1d : 0d);
        return builder.Build();
    }

    private static string ActionKindName(CombatActionKind kind)
    {
        return kind switch
        {
            CombatActionKind.PlayCard => "PlayCard",
            CombatActionKind.UseSkill => "UseSkill",
            CombatActionKind.EndTurn => "EndTurn",
            CombatActionKind.ResolvePrompt => "ResolvePrompt",
            _ => "PlayCard"
        };
    }

    private static void Add(IDictionary<string, double> values, string key, double amount)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }
        values[key] = values.TryGetValue(key, out var current)
                      && !double.IsNaN(current)
                      && !double.IsInfinity(current)
            ? current + amount
            : amount;
    }

    private static void Add(double[] values, string key, double amount)
    {
        var hash = Hash(key);
        var index = (int)(hash % (uint)values.Length);
        var sign = (hash & 0x80000000u) == 0u ? 1d : -1d;
        values[index] += sign * amount;
    }

    private static void AddRange(
        double[] values,
        int start,
        int length,
        string key,
        double amount)
    {
        AddRange(values, start, length, key, amount, values.Length);
    }

    private static void AddRange(
        double[] values,
        int start,
        int length,
        string key,
        double amount,
        int exclusiveEnd)
    {
        var safeLength = Math.Max(
            1,
            Math.Min(length, exclusiveEnd - start));
        var hash = Hash(key);
        var index = start + (int)(hash % (uint)safeLength);
        var sign = (hash & 0x80000000u) == 0u ? 1d : -1d;
        values[index] += sign * amount;
    }

    private static void AddRange(
        double[] values,
        int start,
        int length,
        string prefix,
        string key,
        double amount,
        int exclusiveEnd)
    {
        var safeLength = Math.Max(
            1,
            Math.Min(length, exclusiveEnd - start));
        var hash = Hash(prefix, key);
        var index = start + (int)(hash % (uint)safeLength);
        var sign = (hash & 0x80000000u) == 0u ? 1d : -1d;
        values[index] += sign * amount;
    }

    private static bool TryCoreStateIndex(
        string key,
        int dimensions,
        out int index)
    {
        var slot = CoreStateIndexes.TryGetValue(key ?? "", out var value)
            ? value
            : -1;
        index = slot < 0 ? -1 : Math.Min(dimensions - 1, slot);
        return slot >= 0;
    }

    private static bool TryCoreActionIndex(
        string key,
        int dimensions,
        out int index)
    {
        var slot = CoreActionIndexes.TryGetValue(key ?? "", out var value)
            ? value
            : -1;
        index = slot < 0 ? -1 : Math.Min(dimensions - 1, slot);
        return slot >= 0;
    }

    private static int StateIndex(string key, int dimensions)
    {
        if (TryCoreStateIndex(key, dimensions, out var coreIndex))
        {
            return coreIndex;
        }
        var range = StateRange(key, dimensions);
        return range.Start
               + (int)(Hash("state", key) % (uint)range.Length);
    }

    private static int ActionIndex(string key, int dimensions)
    {
        return TryCoreActionIndex(key, dimensions, out var coreIndex)
            ? coreIndex
            : SparseActionIndex("action", key, dimensions);
    }

    private static int SparseActionIndex(string key, int dimensions)
    {
        var start = Math.Min(24, dimensions - 1);
        var length = Math.Max(1, dimensions - start);
        return start + (int)(Hash(key) % (uint)length);
    }

    private static int SparseActionIndex(
        string prefix,
        string key,
        int dimensions)
    {
        var start = Math.Min(24, dimensions - 1);
        var length = Math.Max(1, dimensions - start);
        return start + (int)(Hash(prefix, key) % (uint)length);
    }

    private static (int Start, int Length) StateRange(
        string key,
        int dimensions)
    {
        var normalized = key ?? "";
        if (normalized.StartsWith("playerStatus:", StringComparison.OrdinalIgnoreCase))
        {
            return ScaleRange(32, 24, dimensions);
        }
        if (normalized.StartsWith("deck:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("hand:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(
                "retainedHand:",
                StringComparison.OrdinalIgnoreCase))
        {
            return ScaleRange(56, 24, dimensions);
        }
        if (normalized.StartsWith("draw:", StringComparison.OrdinalIgnoreCase))
        {
            return ScaleRange(80, 16, dimensions);
        }
        if (normalized.StartsWith("discard:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(
                "exhaust:",
                StringComparison.OrdinalIgnoreCase))
        {
            return ScaleRange(96, 12, dimensions);
        }
        if (normalized.StartsWith("enemy", StringComparison.OrdinalIgnoreCase))
        {
            return ScaleRange(108, 20, dimensions);
        }
        return ScaleRange(16, 16, dimensions);
    }

    private static (int Start, int Length) ScaleRange(
        int start,
        int length,
        int dimensions)
    {
        var scaledStart = (int)Math.Floor(start / 128d * dimensions);
        var scaledEnd = (int)Math.Floor((start + length) / 128d * dimensions);
        scaledStart = Math.Max(0, Math.Min(dimensions - 1, scaledStart));
        scaledEnd = Math.Max(scaledStart + 1, Math.Min(dimensions, scaledEnd));
        return (scaledStart, scaledEnd - scaledStart);
    }

    private static uint Hash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private static uint Hash(string prefix, string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in prefix ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            hash ^= ':';
            hash *= 16777619u;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private static double Normalize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }
        var sign = value < 0d ? -1d : 1d;
        return sign * Math.Log(1d + Math.Abs(value)) / 5d;
    }

    private static double Value(IReadOnlyDictionary<string, double>? values, string key)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }
}
