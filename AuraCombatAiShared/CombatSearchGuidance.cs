using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public interface ICombatSearchGuidanceModel
{
    string ModelId { get; }

    double PolicyLogit(IReadOnlyDictionary<string, double> features);

    double LeafValue(IReadOnlyDictionary<string, double> features);

    double DeathRisk(IReadOnlyDictionary<string, double> features);
}

public sealed class NullCombatSearchGuidanceModel : ICombatSearchGuidanceModel
{
    public static readonly NullCombatSearchGuidanceModel Instance = new();

    public string ModelId => "none";

    public double PolicyLogit(IReadOnlyDictionary<string, double> features) => 0d;

    public double LeafValue(IReadOnlyDictionary<string, double> features) => 0d;

    public double DeathRisk(IReadOnlyDictionary<string, double> features) => 0d;
}

public sealed class CombatTreeStump
{
    public string Feature { get; set; } = "";

    public double Threshold { get; set; }

    public double LeftValue { get; set; }

    public double RightValue { get; set; }
}

public sealed class CombatTreeEnsemble
{
    public double Bias { get; set; }

    public double MaximumMagnitude { get; set; } = 10d;

    public List<CombatTreeStump> Trees { get; set; } = new();

    public double Evaluate(IReadOnlyDictionary<string, double>? features)
    {
        var value = Finite(Bias);
        for (var i = 0; i < Trees.Count; i++)
        {
            var tree = Trees[i];
            var feature = features != null && features.TryGetValue(tree.Feature, out var raw)
                ? Finite(raw)
                : 0d;
            value += feature <= tree.Threshold
                ? Finite(tree.LeftValue)
                : Finite(tree.RightValue);
        }
        var limit = Math.Max(0d, Math.Min(100d, Finite(MaximumMagnitude)));
        return Math.Max(-limit, Math.Min(limit, Finite(value)));
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}

public sealed class CombatSearchGuidanceDefinition
{
    public string ModelProtocol { get; set; } = "aura.combat-search.gbdt.v1";

    public int ProtocolVersion { get; set; } = 1;

    public int FeatureSchemaVersion { get; set; } = 4;

    public string ModelId { get; set; } = "";

    public string DecisionProfile { get; set; } = "balanced";

    public CombatTreeEnsemble Policy { get; set; } = new();

    public CombatTreeEnsemble Value { get; set; } = new();

    public CombatTreeEnsemble Risk { get; set; } = new();

    public Dictionary<string, double> Metrics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BoundedTreeCombatSearchGuidanceModel : ICombatSearchGuidanceModel
{
    private readonly CombatSearchGuidanceDefinition definition;

    public BoundedTreeCombatSearchGuidanceModel(CombatSearchGuidanceDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.definition.Policy ??= new CombatTreeEnsemble();
        this.definition.Value ??= new CombatTreeEnsemble();
        this.definition.Risk ??= new CombatTreeEnsemble();
    }

    public string ModelId => string.IsNullOrWhiteSpace(definition.ModelId)
        ? "combat-search-gbdt"
        : definition.ModelId;

    public double PolicyLogit(IReadOnlyDictionary<string, double> features)
    {
        return definition.Policy.Evaluate(features);
    }

    public double LeafValue(IReadOnlyDictionary<string, double> features)
    {
        return definition.Value.Evaluate(features);
    }

    public double DeathRisk(IReadOnlyDictionary<string, double> features)
    {
        var logit = Math.Max(-20d, Math.Min(20d, definition.Risk.Evaluate(features)));
        return 1d / (1d + Math.Exp(-logit));
    }
}

public sealed class CombatSearchGuidanceTrainingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public CombatSearchGuidanceDefinition? Model { get; set; }

    public int PolicyExampleCount { get; set; }

    public int ValueExampleCount { get; set; }
}

public static class CombatSearchGuidanceTrainer
{
    public static CombatSearchGuidanceTrainingResult Train(
        IEnumerable<CombatTrainingSample> source,
        string decisionProfile,
        int rounds = 32,
        double learningRate = 0.08d)
    {
        var profile = NormalizeProfile(decisionProfile);
        var samples = (source ?? Array.Empty<CombatTrainingSample>())
            .Where(sample => sample != null
                             && string.Equals(sample.CompletionState, "Completed", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(NormalizeProfile(sample.DecisionProfile), profile, StringComparison.Ordinal))
            .ToList();
        var policy = new List<TrainingExample>();
        var values = new List<TrainingExample>();
        var risks = new List<TrainingExample>();
        foreach (var sample in samples)
        {
            var selection = sample.Selection ?? new CombatTrainingSelectionTrace();
            var selectedId = selection.ExecutedBy == "human"
                ? selection.ExecutedCandidateId
                : selection.PolicyPreselectedCandidateId;
            foreach (var candidate in sample.Candidates.Where(candidate => candidate.Legal))
            {
                var label = string.Equals(candidate.CandidateId, selectedId, StringComparison.Ordinal) ? 1d : 0d;
                var weight = selection.ExecutedBy == "human" ? 2d : 0.35d;
                policy.Add(new TrainingExample(
                    CombatResidualTrainer.ContextualFeatures(sample, candidate),
                    label,
                    weight));
            }

            if (sample.StateFeatures.Count > 0)
            {
                values.Add(new TrainingExample(
                    sample.StateFeatures,
                    Math.Max(-100d, Math.Min(100d, sample.Reward)),
                    sample.Terminal ? 2d : 1d));
                var defeated = sample.Terminal
                               && string.Equals(sample.BattleOutcome, "defeat", StringComparison.OrdinalIgnoreCase);
                risks.Add(new TrainingExample(sample.StateFeatures, defeated ? 1d : 0d, sample.Terminal ? 2d : 0.5d));
            }
        }

        var result = new CombatSearchGuidanceTrainingResult
        {
            PolicyExampleCount = policy.Count,
            ValueExampleCount = values.Count
        };
        if (policy.Count < 4 || values.Count < 2)
        {
            result.Message = "搜索引导训练数据不足";
            return result;
        }

        var normalizedRounds = Math.Max(8, Math.Min(128, rounds));
        var normalizedRate = Math.Max(0.01d, Math.Min(0.25d, learningRate));
        var policyModel = Fit(policy, normalizedRounds, normalizedRate, logistic: true, 5d);
        var valueModel = Fit(values, normalizedRounds, normalizedRate, logistic: false, 25d);
        var riskModel = Fit(risks, Math.Max(8, normalizedRounds / 2), normalizedRate, logistic: true, 10d);
        var model = new CombatSearchGuidanceDefinition
        {
            ModelId = "aura-combat-search-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            DecisionProfile = profile,
            Policy = policyModel,
            Value = valueModel,
            Risk = riskModel,
            Metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["policyExamples"] = policy.Count,
                ["valueExamples"] = values.Count,
                ["policyTrees"] = policyModel.Trees.Count,
                ["valueTrees"] = valueModel.Trees.Count,
                ["riskTrees"] = riskModel.Trees.Count
            }
        };
        result.Success = true;
        result.Model = model;
        result.Message = "已生成搜索策略、价值与风险树模型";
        return result;
    }

    private static CombatTreeEnsemble Fit(
        IReadOnlyList<TrainingExample> examples,
        int rounds,
        double learningRate,
        bool logistic,
        double maximumMagnitude)
    {
        var model = new CombatTreeEnsemble { MaximumMagnitude = maximumMagnitude };
        if (examples.Count == 0)
        {
            return model;
        }
        var weightedMean = examples.Sum(example => example.Label * example.Weight)
                           / Math.Max(0.000001d, examples.Sum(example => example.Weight));
        model.Bias = logistic
            ? Math.Log(Math.Max(0.001d, weightedMean) / Math.Max(0.001d, 1d - weightedMean))
            : weightedMean;
        var predictions = Enumerable.Repeat(model.Bias, examples.Count).ToArray();
        var features = examples
            .SelectMany(example => example.Features.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToArray();
        for (var round = 0; round < rounds; round++)
        {
            var residuals = new double[examples.Count];
            for (var i = 0; i < examples.Count; i++)
            {
                var prediction = logistic ? Sigmoid(predictions[i]) : predictions[i];
                residuals[i] = (examples[i].Label - prediction) * examples[i].Weight;
            }

            var best = FindBestStump(examples, residuals, features);
            if (best == null || best.Gain <= 0.0000001d)
            {
                break;
            }
            var stump = new CombatTreeStump
            {
                Feature = best.Feature,
                Threshold = best.Threshold,
                LeftValue = best.LeftValue * learningRate,
                RightValue = best.RightValue * learningRate
            };
            model.Trees.Add(stump);
            for (var i = 0; i < examples.Count; i++)
            {
                predictions[i] += Value(examples[i].Features, stump.Feature) <= stump.Threshold
                    ? stump.LeftValue
                    : stump.RightValue;
            }
        }
        return model;
    }

    private static StumpCandidate? FindBestStump(
        IReadOnlyList<TrainingExample> examples,
        IReadOnlyList<double> residuals,
        IReadOnlyList<string> features)
    {
        StumpCandidate? best = null;
        for (var featureIndex = 0; featureIndex < features.Count; featureIndex++)
        {
            var feature = features[featureIndex];
            var values = examples
                .Select(example => Value(example.Features, feature))
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (values.Length < 2)
            {
                continue;
            }
            var stride = Math.Max(1, values.Length / 12);
            for (var split = stride; split < values.Length; split += stride)
            {
                var threshold = (values[split - 1] + values[split]) * 0.5d;
                var leftSum = 0d;
                var rightSum = 0d;
                var leftWeight = 0d;
                var rightWeight = 0d;
                for (var i = 0; i < examples.Count; i++)
                {
                    if (Value(examples[i].Features, feature) <= threshold)
                    {
                        leftSum += residuals[i];
                        leftWeight += examples[i].Weight;
                    }
                    else
                    {
                        rightSum += residuals[i];
                        rightWeight += examples[i].Weight;
                    }
                }
                if (leftWeight <= 0d || rightWeight <= 0d)
                {
                    continue;
                }
                var left = leftSum / leftWeight;
                var right = rightSum / rightWeight;
                var gain = left * left * leftWeight + right * right * rightWeight;
                if (best == null || gain > best.Gain)
                {
                    best = new StumpCandidate(feature, threshold, left, right, gain);
                }
            }
        }
        return best;
    }

    private static double Sigmoid(double value)
    {
        var bounded = Math.Max(-20d, Math.Min(20d, value));
        return 1d / (1d + Math.Exp(-bounded));
    }

    private static double Value(IReadOnlyDictionary<string, double>? values, string key)
    {
        if (values == null || !values.TryGetValue(key, out var value)
            || double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }
        return value;
    }

    private static string NormalizeProfile(string profile)
    {
        var value = (profile ?? "").Trim().ToLowerInvariant();
        return value == "aggressive" || value == "defensive" ? value : "balanced";
    }

    private sealed class TrainingExample
    {
        public TrainingExample(
            IReadOnlyDictionary<string, double> features,
            double label,
            double weight)
        {
            Features = features;
            Label = label;
            Weight = Math.Max(0.0001d, weight);
        }

        public IReadOnlyDictionary<string, double> Features { get; }

        public double Label { get; }

        public double Weight { get; }
    }

    private sealed class StumpCandidate
    {
        public StumpCandidate(
            string feature,
            double threshold,
            double leftValue,
            double rightValue,
            double gain)
        {
            Feature = feature;
            Threshold = threshold;
            LeftValue = leftValue;
            RightValue = rightValue;
            Gain = gain;
        }

        public string Feature { get; }

        public double Threshold { get; }

        public double LeftValue { get; }

        public double RightValue { get; }

        public double Gain { get; }
    }
}
