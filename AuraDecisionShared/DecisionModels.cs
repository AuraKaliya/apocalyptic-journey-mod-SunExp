using System;
using System.Collections.Generic;

namespace AuraDecision.Shared;

public enum DecisionComparison
{
    Always,
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed class DecisionCondition
{
    public string Feature { get; set; } = "";

    public DecisionComparison Comparison { get; set; } = DecisionComparison.Always;

    public double Value { get; set; }
}

public sealed class DecisionGraphNode
{
    public string Id { get; set; } = "";

    public DecisionCondition Condition { get; set; } = new();

    public string TrueNodeId { get; set; } = "";

    public string FalseNodeId { get; set; } = "";

    public bool Reject { get; set; }

    public bool Terminal { get; set; }

    public DecisionUtility UtilityDelta { get; set; } = new();
}

public sealed class DecisionGraph
{
    public string RootNodeId { get; set; } = "";

    public List<DecisionGraphNode> Nodes { get; set; } = new();
}

public sealed class DecisionUtility
{
    public double Survival { get; set; }

    public double Lethal { get; set; }

    public double Tempo { get; set; }

    public double Resource { get; set; }

    public double DeckEconomy { get; set; }

    public double Scaling { get; set; }

    public double Synergy { get; set; }

    public double Continuation { get; set; }

    public double Risk { get; set; }

    public double Uncertainty { get; set; }

    public double Coordination { get; set; }

    public DecisionUtility Clone()
    {
        return (DecisionUtility)MemberwiseClone();
    }

    public void Add(DecisionUtility? other)
    {
        if (other == null)
        {
            return;
        }

        Survival += other.Survival;
        Lethal += other.Lethal;
        Tempo += other.Tempo;
        Resource += other.Resource;
        DeckEconomy += other.DeckEconomy;
        Scaling += other.Scaling;
        Synergy += other.Synergy;
        Continuation += other.Continuation;
        Risk += other.Risk;
        Uncertainty += other.Uncertainty;
        Coordination += other.Coordination;
    }
}

public sealed class DecisionWeights
{
    public double Survival { get; set; } = 1.35;

    public double Lethal { get; set; } = 1.6;

    public double Tempo { get; set; } = 1.0;

    public double Resource { get; set; } = 0.8;

    public double DeckEconomy { get; set; } = 0.55;

    public double Scaling { get; set; } = 0.7;

    public double Synergy { get; set; } = 0.65;

    public double Continuation { get; set; } = 0.9;

    public double Risk { get; set; } = -1.25;

    public double Uncertainty { get; set; } = -0.8;

    public double Coordination { get; set; } = 0.6;

    public double Score(DecisionUtility utility)
    {
        return utility.Survival * Survival
               + utility.Lethal * Lethal
               + utility.Tempo * Tempo
               + utility.Resource * Resource
               + utility.DeckEconomy * DeckEconomy
               + utility.Scaling * Scaling
               + utility.Synergy * Synergy
               + utility.Continuation * Continuation
               + utility.Risk * Risk
               + utility.Uncertainty * Uncertainty
               + utility.Coordination * Coordination;
    }
}

public sealed class DecisionCandidate<TAction>
{
    public string Id { get; set; } = "";

    public TAction Action { get; set; } = default!;

    public bool Legal { get; set; } = true;

    public string RejectionReason { get; set; } = "";

    public DecisionUtility Utility { get; set; } = new();

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DecisionResult<TAction>
{
    public bool HasAction { get; set; }

    public TAction Action { get; set; } = default!;

    public string CandidateId { get; set; } = "";

    public double Score { get; set; }

    public string Reason { get; set; } = "";
}

public interface IDecisionResidualModel
{
    string ModelId { get; }

    int ProtocolVersion { get; }

    double Predict(IReadOnlyDictionary<string, double> features);
}

public interface IContextualDecisionResidualModel : IDecisionResidualModel
{
    DecisionResidualPrediction Evaluate(IReadOnlyDictionary<string, double> features);
}

public sealed class DecisionResidualPrediction
{
    public string ModelId { get; set; } = "";

    public double RawCorrection { get; set; }

    public double Applicability { get; set; }

    public double AppliedCorrection { get; set; }
}

public sealed class NullDecisionResidualModel : IDecisionResidualModel
{
    public static readonly NullDecisionResidualModel Instance = new();

    public string ModelId => "none";

    public int ProtocolVersion => 1;

    public double Predict(IReadOnlyDictionary<string, double> features)
    {
        return 0d;
    }
}

public sealed class DecisionResidualModelDefinition
{
    public string ModelProtocol { get; set; } = "aura.decision-residual.linear.v1";

    public string ModelId { get; set; } = "";

    public int ProtocolVersion { get; set; } = 1;

    public int FeatureSchemaVersion { get; set; } = 5;

    public int ApplicabilityProtocolVersion { get; set; } = 1;

    public string DecisionProfile { get; set; } = "";

    public string TrainingPreset { get; set; } = "";

    public double Bias { get; set; }

    public double MaximumCorrection { get; set; } = 2d;

    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> Means { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> Scales { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> FeatureMinimums { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> FeatureMaximums { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> FeatureObservationCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> CategoryObservationCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double MinimumCategoryObservations { get; set; } = 5d;

    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> TrainingParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BoundedLinearDecisionResidualModel : IContextualDecisionResidualModel
{
    private readonly DecisionResidualModelDefinition definition;

    public BoundedLinearDecisionResidualModel(DecisionResidualModelDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.definition.Weights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.Means ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.Scales ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.FeatureMinimums ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.FeatureMaximums ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.FeatureObservationCounts ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.CategoryObservationCounts ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        this.definition.TrainingParameters ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public string ModelId => string.IsNullOrWhiteSpace(definition.ModelId)
        ? "linear-residual"
        : definition.ModelId;

    public int ProtocolVersion => definition.ProtocolVersion;

    public double Predict(IReadOnlyDictionary<string, double> features)
    {
        return Evaluate(features).AppliedCorrection;
    }

    public DecisionResidualPrediction Evaluate(IReadOnlyDictionary<string, double> features)
    {
        var score = Finite(definition.Bias);
        if (features != null)
        {
            foreach (var pair in definition.Weights)
            {
                var raw = features.TryGetValue(pair.Key, out var value) ? Finite(value) : 0d;
                var mean = definition.Means.TryGetValue(pair.Key, out var configuredMean)
                    ? Finite(configuredMean)
                    : 0d;
                var scale = definition.Scales.TryGetValue(pair.Key, out var configuredScale)
                            && Math.Abs(configuredScale) > 0.000001d
                    ? Math.Abs(Finite(configuredScale))
                    : 1d;
                score += ((raw - mean) / scale) * Finite(pair.Value);
            }
        }

        var limit = Math.Max(0d, Math.Min(5d, Finite(definition.MaximumCorrection)));
        var rawCorrection = Math.Max(-limit, Math.Min(limit, Finite(score)));
        var applicability = Applicability(
            features ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
        return new DecisionResidualPrediction
        {
            ModelId = ModelId,
            RawCorrection = rawCorrection,
            Applicability = applicability,
            AppliedCorrection = rawCorrection * applicability
        };
    }

    private double Applicability(IReadOnlyDictionary<string, double> features)
    {
        if (features == null)
        {
            return 0d;
        }

        var semanticConfidence = features.TryGetValue("semanticConfidence", out var configuredConfidence)
            ? Clamp01(Finite(configuredConfidence))
            : 0d;
        if (semanticConfidence <= 0d)
        {
            return 0d;
        }

        var categorySupport = CategorySupport(features);
        var rangeSupport = FeatureRangeSupport(features);
        return Math.Min(semanticConfidence, Math.Min(categorySupport, rangeSupport));
    }

    private double CategorySupport(IReadOnlyDictionary<string, double> features)
    {
        var category = "";
        var categoryValue = 0d;
        foreach (var candidate in new[]
                 {
                     "categoryAttack",
                     "categoryDefend",
                     "categorySupport",
                     "categorySkill",
                     "categoryOther"
                 })
        {
            if (features.TryGetValue(candidate, out var value) && Finite(value) > categoryValue)
            {
                category = candidate;
                categoryValue = Finite(value);
            }
        }

        if (category.Length == 0 || definition.CategoryObservationCounts.Count == 0)
        {
            return 0d;
        }

        var count = definition.CategoryObservationCounts.TryGetValue(category, out var observed)
            ? Math.Max(0d, Finite(observed))
            : 0d;
        var minimum = Math.Max(1d, Finite(definition.MinimumCategoryObservations));
        return Clamp01(count / minimum);
    }

    private double FeatureRangeSupport(IReadOnlyDictionary<string, double> features)
    {
        var support = 1d;
        var inspected = 0;
        foreach (var pair in definition.Weights)
        {
            if (!features.TryGetValue(pair.Key, out var raw)
                || !definition.FeatureMinimums.TryGetValue(pair.Key, out var configuredMinimum)
                || !definition.FeatureMaximums.TryGetValue(pair.Key, out var configuredMaximum))
            {
                continue;
            }

            var minimum = Finite(configuredMinimum);
            var maximum = Finite(configuredMaximum);
            if (maximum < minimum)
            {
                var swap = minimum;
                minimum = maximum;
                maximum = swap;
            }

            var value = Finite(raw);
            var width = Math.Max(
                0.5d,
                Math.Max(
                    maximum - minimum,
                    definition.Scales.TryGetValue(pair.Key, out var scale)
                        ? Math.Abs(Finite(scale)) * 2d
                        : 0d));
            var featureSupport = value < minimum
                ? 1d - (minimum - value) / width
                : value > maximum
                    ? 1d - (value - maximum) / width
                    : 1d;
            support = Math.Min(support, Clamp01(featureSupport));
            inspected++;
        }

        return inspected == 0 ? 0d : support;
    }

    private static double Clamp01(double value)
    {
        return Math.Max(0d, Math.Min(1d, Finite(value)));
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
