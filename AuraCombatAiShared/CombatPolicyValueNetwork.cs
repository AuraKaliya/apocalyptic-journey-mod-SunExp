using System;
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
    public Dictionary<string, double> PolicyLogits { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, List<double>> ActionReturnQuantiles { get; set; } =
        new(StringComparer.Ordinal);

    public double ExpectedReturn { get; set; }

    public double WinProbability { get; set; }

    public double DeathProbability { get; set; }

    public double ExpectedRemainingHpRatio { get; set; }

    public double ExpectedRemainingTurns { get; set; }

    public double Uncertainty { get; set; }
}

public interface ICombatPolicyValueModel
{
    string ModelId { get; }

    CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input);

    IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs);
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

    public ConcurrentBatchedCombatPolicyValueModel(
        ICombatPolicyValueModel inner,
        int maximumBatchSize,
        TimeSpan? coalescingWindow = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maximumBatchSize = Math.Max(2, maximumBatchSize);
        var window = coalescingWindow ?? TimeSpan.FromTicks(100);
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

    public CombatPolicyValuePrediction Evaluate(
        CombatPolicyValueInput input)
    {
        BatchRequest request;
        List<BatchRequest>? batch = null;
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
                    }
                }
            }
        }

        if (batch != null)
        {
            Execute(batch);
        }
        request.Completed.Wait();
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

    private void Execute(List<BatchRequest> batch)
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
            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].Result = results[i];
            }
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
        var threadId = Environment.CurrentManagedThreadId & int.MaxValue;
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

    public int StateDimensions { get; set; } = 256;

    public int ActionDimensions { get; set; } = 192;

    public int HiddenDimensions { get; set; } = 64;

    public string FeatureEncodingMode { get; set; } = "partitioned-v3";

    public double PolicyTemperature { get; set; } = 1d;

    public double[] StateWeights { get; set; } = Array.Empty<double>();

    public double[] StateBias { get; set; } = Array.Empty<double>();

    public double[] ActionWeights { get; set; } = Array.Empty<double>();

    public double[] ActionBias { get; set; } = Array.Empty<double>();

    public double[] PolicyWeights { get; set; } = Array.Empty<double>();

    public double PolicyBias { get; set; }

    public int ActionQuantileCount { get; set; } = 16;

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
    private readonly CombatPolicyValueNetworkDefinition definition;
    [ThreadStatic]
    private static InferenceWorkspace? threadWorkspace;
    [ThreadStatic]
    private static BatchInferenceWorkspace? threadBatchWorkspace;

    public ManagedCombatPolicyValueModel(
        CombatPolicyValueNetworkDefinition definition,
        bool allowDiagnosticLegacySchema = false)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (!CombatPolicyValueNetworkValidator.TryValidate(
                definition,
                out var reason,
                allowDiagnosticLegacySchema))
        {
            throw new ArgumentException(reason, nameof(definition));
        }
    }

    public string ModelId => string.IsNullOrWhiteSpace(definition.ModelId)
        ? "combat-policy-value"
        : definition.ModelId;

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
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
            DenseTanhInto(
                state,
                definition.StateDimensions,
                definition.StateWeights,
                definition.StateBias,
                hidden,
                definition.HiddenDimensions);
            var result = PredictionFromHidden(
                hidden,
                0);
            var minimum = double.PositiveInfinity;
            var maximum = double.NegativeInfinity;
            for (var i = 0; i < input.Candidates.Count; i++)
            {
                var candidate = input.Candidates[i]
                                ?? new CombatPolicyValueCandidate();
                CombatPolicyValueEncoding.EncodeCandidateInto(
                    candidate,
                    action,
                    definition.ActionDimensions,
                    definition.FeatureEncodingMode);
                DenseTanhInto(
                    action,
                    definition.ActionDimensions,
                    definition.ActionWeights,
                    definition.ActionBias,
                    actionHidden,
                    definition.HiddenDimensions);
                var logit = PolicyLogit(
                    hidden,
                    0,
                    actionHidden,
                    0,
                    definition.HiddenDimensions);
                result.PolicyLogits[candidate.CandidateId ?? ""] = logit;
                result.ActionReturnQuantiles[candidate.CandidateId ?? ""] =
                    ActionQuantiles(hidden, 0, actionHidden, 0);
                minimum = Math.Min(minimum, logit);
                maximum = Math.Max(maximum, logit);
            }
            result.Uncertainty = input.Candidates.Count <= 1
                ? 0d
                : 1d / (1d + Math.Max(0d, maximum - minimum));
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
                foreach (var source in input.Candidates)
                {
                    var candidate = source ?? new CombatPolicyValueCandidate();
                    candidateOwners[cursor] = inputIndex;
                    candidates[cursor] = candidate;
                    CombatPolicyValueEncoding.EncodeCandidateInto(
                        candidate,
                        actions,
                        cursor * definition.ActionDimensions,
                        definition.ActionDimensions,
                        definition.FeatureEncodingMode);
                    cursor++;
                }
            }
            DenseTanhBatch(
                states,
                count,
                definition.StateDimensions,
                definition.StateWeights,
                definition.StateBias,
                hidden,
                definition.HiddenDimensions);
            if (candidateCount > 0)
            {
                DenseTanhBatch(
                    actions,
                    candidateCount,
                    definition.ActionDimensions,
                    definition.ActionWeights,
                    definition.ActionBias,
                    actionHidden,
                    definition.HiddenDimensions);
            }
            for (var inputIndex = 0; inputIndex < count; inputIndex++)
            {
                results[inputIndex] = PredictionFromHidden(
                    hidden,
                    inputIndex * definition.HiddenDimensions);
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
                results[owner].PolicyLogits[
                    candidates[candidateIndex].CandidateId ?? ""] = logit;
                results[owner].ActionReturnQuantiles[
                    candidates[candidateIndex].CandidateId ?? ""] =
                    ActionQuantiles(
                        hidden,
                        owner * definition.HiddenDimensions,
                        actionHidden,
                        candidateIndex * definition.HiddenDimensions);
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

    private List<double> ActionQuantiles(
        double[] hidden,
        int hiddenOffset,
        double[] actionHidden,
        int actionOffset)
    {
        var result = new List<double>(definition.ActionQuantileCount);
        for (var quantile = 0;
             quantile < definition.ActionQuantileCount;
             quantile++)
        {
            result.Add(Clamp(
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
        return result;
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
        double[] right,
        int rightOffset,
        int length)
    {
        var result = 0d;
        var index = 0;
#if NET8_0_OR_GREATER
        if (Vector.IsHardwareAccelerated
            && length >= Vector<double>.Count)
        {
            var vectorSum = Vector<double>.Zero;
            var vectorEnd = length - length % Vector<double>.Count;
            for (; index < vectorEnd; index += Vector<double>.Count)
            {
                vectorSum += new Vector<double>(left, leftOffset + index)
                             * new Vector<double>(
                                 right,
                                 rightOffset + index);
            }
            for (var lane = 0; lane < Vector<double>.Count; lane++)
            {
                result += vectorSum[lane];
            }
        }
#endif
        for (; index < length; index++)
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
        double[] weights,
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
        double[] weights,
        int weightOffset,
        int length)
    {
        var result = 0d;
        var index = 0;
#if NET8_0_OR_GREATER
        if (Vector.IsHardwareAccelerated
            && length >= Vector<double>.Count)
        {
            var vectorSum = Vector<double>.Zero;
            var vectorEnd = length - length % Vector<double>.Count;
            for (; index < vectorEnd; index += Vector<double>.Count)
            {
                vectorSum += new Vector<double>(state, stateOffset + index)
                             * new Vector<double>(
                                 action,
                                 actionOffset + index)
                             * new Vector<double>(weights, weightOffset + index);
            }
            for (var lane = 0; lane < Vector<double>.Count; lane++)
            {
                result += vectorSum[lane];
            }
        }
#endif
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
            || model.StateDimensions > 512
            || model.ActionDimensions > 512
            || model.HiddenDimensions > 512)
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
        var sanitized = SanitizeStateFeatures(values);
        foreach (var pair in sanitized)
        {
            if (TryCoreStateIndex(
                    pair.Key,
                    safeDimensions,
                    out var coreIndex))
            {
                target[offset + coreIndex] += Normalize(pair.Value);
                continue;
            }
            var range = StateRange(pair.Key, safeDimensions);
            AddRange(
                target,
                offset + range.Start,
                range.Length,
                "state:" + pair.Key,
                Normalize(pair.Value),
                offset + safeDimensions);
        }
    }

    public static Dictionary<string, double> SanitizeStateFeatures(
        IReadOnlyDictionary<string, double>? values)
    {
        return CombatPublicFeaturePolicy.SanitizeState(values);
    }

    public static bool IsPermittedStateFeature(string? key)
    {
        return !string.IsNullOrWhiteSpace(key)
               && CombatPublicFeaturePolicy
                   .SanitizeState(new Dictionary<string, double>
                   {
                       [key!] = 1d
                   })
                   .Count == 1;
    }

    public static CombatFeatureCollisionTelemetry MeasureStateCollisions(
        IReadOnlyDictionary<string, double>? values,
        int dimensions)
    {
        var buckets = new HashSet<int>();
        var count = 0;
        foreach (var pair in SanitizeStateFeatures(values))
        {
            count++;
            buckets.Add(StateIndex(pair.Key, Math.Max(1, dimensions)));
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
        foreach (var pair in CombatPublicFeaturePolicy.SanitizeAction(
                     candidate.Features))
        {
            count++;
            buckets.Add(ActionIndex(pair.Key, safeDimensions));
        }
        count++;
        buckets.Add(SparseActionIndex(
            "source:" + (candidate.SourceId ?? ""),
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
        foreach (var pair in CombatPublicFeaturePolicy.SanitizeAction(
                     candidate.Features))
        {
            if (TryCoreActionIndex(
                    pair.Key,
                    safeDimensions,
                    out var coreIndex))
            {
                target[offset + coreIndex] += Normalize(pair.Value);
                continue;
            }
            var sparseStart = Math.Min(24, safeDimensions - 1);
            AddRange(
                target,
                offset + sparseStart,
                Math.Max(1, safeDimensions - sparseStart),
                "action:" + pair.Key,
                Normalize(pair.Value),
                offset + safeDimensions);
        }
        var sourceStart = Math.Min(24, safeDimensions - 1);
        AddRange(
            target,
            offset + sourceStart,
            Math.Max(1, safeDimensions - sourceStart),
            "source:" + (candidate.SourceId ?? ""),
            1d,
            offset + safeDimensions);
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

    public static CombatPolicyValueInput BuildInput(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation>? candidates = null)
    {
        var input = new CombatPolicyValueInput
        {
            StateFeatures = BuildStateFeatures(state)
        };
        foreach (var candidate in candidates ?? Array.Empty<CombatCandidateEvaluation>())
        {
            if (candidate == null || !candidate.Legal || candidate.Action == null)
            {
                continue;
            }
            input.Candidates.Add(new CombatPolicyValueCandidate
            {
                CandidateId = candidate.Action.CandidateId,
                SourceId = candidate.Action.SourceId,
                ActionKind = candidate.Action.Kind.ToString(),
                Features = BuildCandidateFeatures(candidate)
            });
        }
        return input;
    }

    public static Dictionary<string, double> BuildStateFeatures(CombatStateObservation state)
    {
        var result = SanitizeStateFeatures(state?.Features);
        if (state == null)
        {
            return result;
        }
        result["playerHp"] = state.Player?.CurrentHp ?? 0;
        result["playerMaxHp"] = state.Player?.MaxHp ?? 0;
        result["playerDefend"] = state.Player?.Defend ?? 0;
        result["power"] = state.CurrentPower;
        result["maxPower"] = state.MaxPower;
        result["handCount"] = state.HandCount;
        result["enemyCount"] = state.Enemies?.Count ?? 0;
        result["enemyHpTotal"] = state.Enemies?.Sum(enemy => Math.Max(0, enemy.CurrentHp)) ?? 0;
        result["expectedIncomingDamage"] = state.ExpectedIncomingDamage;
        result["turn"] = Value(state.Features, "turn");
        foreach (var status in state.Player?.Statuses ?? new List<CombatStatusObservation>())
        {
            Add(result, "playerStatus:" + status.StatusId, status.Level);
        }
        foreach (var enemy in state.Enemies ?? new List<CombatUnitObservation>())
        {
            Add(result, "enemy:" + enemy.DefinitionId, 1d);
            Add(result, "enemyHp:" + enemy.DefinitionId, enemy.CurrentHp);
            foreach (var status in enemy.Statuses ?? new List<CombatStatusObservation>())
            {
                Add(result, "enemyStatus:" + status.StatusId, status.Level);
            }
        }
        foreach (var id in state.DeckCardIds ?? new List<string>())
        {
            Add(result, "deck:" + id, 1d);
        }
        foreach (var id in state.HandCardIds ?? new List<string>())
        {
            Add(result, "hand:" + id, 1d);
        }
        foreach (var id in state.RetainedHandCardIds ?? new List<string>())
        {
            Add(result, "retainedHand:" + id, 1d);
        }
        foreach (var id in state.DiscardPileCardIds ?? new List<string>())
        {
            Add(result, "discard:" + id, 1d);
        }
        foreach (var id in state.ExhaustPileCardIds ?? new List<string>())
        {
            Add(result, "exhaust:" + id, 1d);
        }
        return result;
    }

    public static Dictionary<string, double> BuildCandidateFeatures(
        CombatCandidateEvaluation candidate)
    {
        var action = candidate.Action ?? new CombatActionObservation();
        var result = CombatPublicFeaturePolicy.SanitizeAction(action.Features);
        result["cost"] = action.Cost;
        result["ruleScore"] = candidate.RuleScore;
        result["baseRuleScore"] = candidate.BaseRuleScore;
        result["planScore"] = candidate.PlanScore;
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
        return result;
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

    private static bool TryCoreStateIndex(
        string key,
        int dimensions,
        out int index)
    {
        var slot = (key ?? "").ToLowerInvariant() switch
        {
            "playerhp" => 0,
            "playermaxhp" => 1,
            "playerdefend" => 2,
            "power" => 3,
            "maxpower" => 4,
            "handcount" => 5,
            "enemycount" => 6,
            "enemyhptotal" => 7,
            "expectedincomingdamage" => 8,
            "expectedblockabledamage" => 9,
            "expectedunblockabledamage" => 10,
            "expecteddamageovertime" => 11,
            "turn" => 12,
            _ => -1
        };
        index = slot < 0 ? -1 : Math.Min(dimensions - 1, slot);
        return slot >= 0;
    }

    private static bool TryCoreActionIndex(
        string key,
        int dimensions,
        out int index)
    {
        var slot = (key ?? "").ToLowerInvariant() switch
        {
            "cost" => 0,
            "rulescore" => 1,
            "baserulescore" => 2,
            "planscore" => 3,
            "damage" => 4,
            "truedamage" => 5,
            "damageovertime" => 6,
            "selfhploss" => 7,
            "endofcycleselfhploss" => 8,
            "hitcount" => 9,
            "defend" => 10,
            "heal" => 11,
            "draw" => 12,
            "energygain" => 13,
            "buff" => 14,
            "debuff" => 15,
            "cleanse" => 16,
            "costreduction" => 17,
            "cardgeneration" => 18,
            "persistentvalue" => 19,
            "scaling" => 20,
            "risk" => 21,
            "uncertainty" => 22,
            _ => -1
        };
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
               + (int)(Hash("state:" + key) % (uint)range.Length);
    }

    private static int ActionIndex(string key, int dimensions)
    {
        return TryCoreActionIndex(key, dimensions, out var coreIndex)
            ? coreIndex
            : SparseActionIndex("action:" + key, dimensions);
    }

    private static int SparseActionIndex(string key, int dimensions)
    {
        var start = Math.Min(24, dimensions - 1);
        var length = Math.Max(1, dimensions - start);
        return start + (int)(Hash(key) % (uint)length);
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
