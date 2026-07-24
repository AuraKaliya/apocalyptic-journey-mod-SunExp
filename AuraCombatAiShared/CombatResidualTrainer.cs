using System;
using System.Collections.Generic;
using System.Linq;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatResidualTrainingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public int CompletedSampleCount { get; set; }

    public int HumanSampleCount { get; set; }

    public int PreferencePairCount { get; set; }

    public DecisionResidualModelDefinition? Model { get; set; }
}

public sealed class CombatResidualTrainingOptions
{
    public string PresetId { get; set; } = "legacy";

    public int Epochs { get; set; } = 100;

    public double LearningRate { get; set; } = 0.05d;

    public double L2 { get; set; } = 0.001d;

    public double MaximumCorrection { get; set; } = 2d;

    public int MinimumPreferencePairs { get; set; } = 1;

    public int MinimumCategoryObservations { get; set; } = 5;

    public CombatResidualTrainingOptions Normalized()
    {
        return new CombatResidualTrainingOptions
        {
            PresetId = string.IsNullOrWhiteSpace(PresetId) ? "custom" : PresetId.Trim().ToLowerInvariant(),
            Epochs = Math.Max(20, Math.Min(300, Epochs)),
            LearningRate = ClampFinite(LearningRate, 0.005d, 0.1d, 0.05d),
            L2 = ClampFinite(L2, 0d, 0.02d, 0.001d),
            MaximumCorrection = ClampFinite(MaximumCorrection, 0.25d, 2d, 2d),
            MinimumPreferencePairs = Math.Max(1, Math.Min(200, MinimumPreferencePairs)),
            MinimumCategoryObservations = Math.Max(3, Math.Min(100, MinimumCategoryObservations))
        };
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        var finite = double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        return Math.Max(minimum, Math.Min(maximum, finite));
    }
}

public static class CombatResidualTrainer
{
    private static readonly HashSet<string> LearnedFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "usefulDefend",
        "wastedDefend",
        "effectiveHeal",
        "overheal",
        "effectiveDraw",
        "overdraw",
        "effectiveDamage",
        "overkill",
        "lethal",
        "energyScarcity",
        "freeKnownValue",
        "semanticConfidence",
        "utilitySurvival",
        "utilityLethal",
        "utilityTempo",
        "utilityResource",
        "utilityDeckEconomy",
        "utilityScaling",
        "utilitySynergy",
        "utilityContinuation",
        "utilityRisk",
        "utilityUncertainty",
        "utilityCoordination",
        "categoryAttack",
        "categoryDefend",
        "categorySupport",
        "categorySkill",
        "categoryOther"
    };

    public static CombatResidualTrainingResult Train(
        IEnumerable<CombatTrainingSample> source,
        string decisionProfile)
    {
        return Train(source, decisionProfile, new CombatResidualTrainingOptions());
    }

    public static CombatResidualTrainingResult Train(
        IEnumerable<CombatTrainingSample> source,
        string decisionProfile,
        CombatResidualTrainingOptions? trainingOptions)
    {
        var options = (trainingOptions ?? new CombatResidualTrainingOptions()).Normalized();
        var profile = NormalizeProfile(decisionProfile);
        var samples = (source ?? Array.Empty<CombatTrainingSample>())
            .Where(sample => sample != null
                             && string.Equals(sample.CompletionState, "Completed", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(NormalizeProfile(sample.DecisionProfile), profile, StringComparison.Ordinal))
            .ToList();
        var pairs = BuildPairs(samples);
        var result = new CombatResidualTrainingResult
        {
            CompletedSampleCount = samples.Count,
            HumanSampleCount = samples.Count(sample =>
                string.Equals(sample.Selection?.ExecutedBy, "human", StringComparison.OrdinalIgnoreCase)),
            PreferencePairCount = pairs.Count
        };
        if (pairs.Count < options.MinimumPreferencePairs)
        {
            result.Message = pairs.Count == 0
                ? "当前决策风格没有可训练的人工覆盖样本"
                : "有效偏好对不足：当前 " + pairs.Count
                  + "，最低要求 " + options.MinimumPreferencePairs;
            return result;
        }

        var statistics = BuildStatistics(pairs);
        var vectors = pairs
            .Select(pair => new WeightedVector(
                Difference(pair.Positive, pair.Negative, statistics.Means, statistics.Scales),
                pair.Weight,
                pair.BattleSessionId))
            .Where(vector => vector.Values.Count > 0)
            .ToList();
        if (vectors.Count == 0)
        {
            result.Message = "人工选择与自动预选没有可学习的上下文差异";
            return result;
        }

        var validationAccuracy = GroupedHoldoutAccuracy(pairs, options);
        var weights = FitWeights(statistics, vectors, options, 7);

        weights = weights
            .Where(pair => Math.Abs(pair.Value) >= 0.000001d)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (weights.Count == 0)
        {
            result.Message = "训练完成但没有产生有效权重";
            return result;
        }

        var model = new DecisionResidualModelDefinition
        {
            ModelId = "aura-combat-contextual-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            FeatureSchemaVersion = 4,
            ApplicabilityProtocolVersion = 1,
            DecisionProfile = profile,
            TrainingPreset = options.PresetId,
            MaximumCorrection = options.MaximumCorrection,
            Weights = weights,
            Means = KeepUsed(statistics.Means, weights),
            Scales = KeepUsed(statistics.Scales, weights),
            FeatureMinimums = KeepUsed(statistics.Minimums, weights),
            FeatureMaximums = KeepUsed(statistics.Maximums, weights),
            FeatureObservationCounts = KeepUsed(statistics.Counts, weights),
            CategoryObservationCounts = CategoryCounts(pairs),
            MinimumCategoryObservations = options.MinimumCategoryObservations,
            TrainingParameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["epochs"] = options.Epochs,
                ["learningRate"] = options.LearningRate,
                ["l2"] = options.L2,
                ["maximumCorrection"] = options.MaximumCorrection,
                ["minimumPreferencePairs"] = options.MinimumPreferencePairs,
                ["minimumCategoryObservations"] = options.MinimumCategoryObservations,
                ["randomSeed"] = 7d
            },
            Metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["pairCount"] = pairs.Count,
                ["humanSampleCount"] = result.HumanSampleCount,
                ["completedSampleCount"] = result.CompletedSampleCount,
                ["trainingAccuracy"] = Accuracy(weights, vectors),
                ["groupedValidationAccuracy"] = validationAccuracy,
                ["battleSessionCount"] = pairs.Select(pair => pair.BattleSessionId).Distinct().Count()
            }
        };
        result.Success = true;
        result.Model = model;
        result.Message = "已从 " + pairs.Count + " 个人工覆盖样本生成 "
                         + weights.Count + " 个上下文权重";
        return result;
    }

    public static Dictionary<string, double> ContextualFeatures(
        CombatTrainingSample sample,
        CombatTrainingCandidate candidate)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidate.Features ?? new Dictionary<string, double>())
        {
            if (LearnedFeatures.Contains(pair.Key))
            {
                result[pair.Key] = Finite(pair.Value);
            }
        }

        AddUtility(result, candidate.Utility);
        AddLegacyContext(result, sample, candidate);
        return result;
    }

    private static List<PreferencePair> BuildPairs(IReadOnlyList<CombatTrainingSample> samples)
    {
        var result = new List<PreferencePair>();
        foreach (var sample in samples)
        {
            var selection = sample.Selection ?? new CombatTrainingSelectionTrace();
            if (!string.Equals(selection.ExecutedBy, "human", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(selection.ExecutedCandidateId)
                || string.IsNullOrWhiteSpace(selection.PolicyPreselectedCandidateId)
                || string.Equals(
                    selection.ExecutedCandidateId,
                    selection.PolicyPreselectedCandidateId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var positive = sample.Candidates.FirstOrDefault(candidate =>
                candidate.Legal
                && string.Equals(candidate.CandidateId, selection.ExecutedCandidateId, StringComparison.Ordinal));
            var negative = sample.Candidates.FirstOrDefault(candidate =>
                candidate.Legal
                && string.Equals(candidate.CandidateId, selection.PolicyPreselectedCandidateId, StringComparison.Ordinal));
            if (positive == null || negative == null)
            {
                continue;
            }

            result.Add(new PreferencePair(
                ContextualFeatures(sample, positive),
                ContextualFeatures(sample, negative),
                selection.PolicyVisibleToHuman ? 0.5d : 2d,
                sample.BattleSessionId));
        }

        return result;
    }

    private static void AddUtility(
        IDictionary<string, double> result,
        CombatTrainingUtility? utility)
    {
        utility ??= new CombatTrainingUtility();
        result["utilitySurvival"] = Finite(utility.Survival);
        result["utilityLethal"] = Finite(utility.Lethal);
        result["utilityTempo"] = Finite(utility.Tempo);
        result["utilityResource"] = Finite(utility.Resource);
        result["utilityDeckEconomy"] = Finite(utility.DeckEconomy);
        result["utilityScaling"] = Finite(utility.Scaling);
        result["utilitySynergy"] = Finite(utility.Synergy);
        result["utilityContinuation"] = Finite(utility.Continuation);
        result["utilityRisk"] = Finite(utility.Risk);
        result["utilityUncertainty"] = Finite(utility.Uncertainty);
        result["utilityCoordination"] = Finite(utility.Coordination);
    }

    private static void AddLegacyContext(
        IDictionary<string, double> result,
        CombatTrainingSample sample,
        CombatTrainingCandidate candidate)
    {
        var semantics = candidate.Semantics ?? new CombatActionSemantics();
        var state = sample.StateFeatures ?? new Dictionary<string, double>();
        var expectedBlockable = Value(state, "expectedBlockableDamage");
        var playerDefend = Value(state, "playerDefend");
        var requiredDefend = Math.Max(0d, expectedBlockable - playerDefend);
        var defend = Math.Max(0d, semantics.Defend);
        var usefulDefend = Math.Min(defend, requiredDefend);
        var missingHp = Math.Max(0d, Value(state, "playerMaxHp") - Value(state, "playerHp"));
        var handCapacity = Math.Max(0d, 10d - Value(state, "handCount"));
        var damage = Math.Max(0d, semantics.Damage) * Math.Max(1d, semantics.HitCount)
                     + Math.Max(0d, semantics.TrueDamage)
                     + Math.Max(0d, semantics.DamageOverTime);
        var targetHp = Value(candidate.Features, "targetHp");
        var recognizedSemantics = damage
                            + defend
                            + Math.Max(0d, semantics.Heal)
                            + Math.Max(0d, semantics.Draw)
                            + Math.Max(0d, semantics.EnergyGain)
                            + Math.Max(0d, semantics.Buff)
                            + Math.Max(0d, semantics.Debuff)
                            + Math.Max(0d, semantics.Cleanse)
                            + Math.Max(0d, semantics.CostReduction)
                            + Math.Max(0d, semantics.CardGeneration)
                            + Math.Max(0d, semantics.PersistentValue)
                            + Math.Max(0d, semantics.Scaling) > 0d;

        SetIfMissing(result, "usefulDefend", usefulDefend);
        SetIfMissing(result, "wastedDefend", Math.Max(0d, defend - usefulDefend));
        SetIfMissing(result, "effectiveHeal", Math.Min(Math.Max(0d, semantics.Heal), missingHp));
        SetIfMissing(result, "overheal", Math.Max(0d, semantics.Heal - missingHp));
        SetIfMissing(result, "effectiveDraw", Math.Min(Math.Max(0d, semantics.Draw), handCapacity));
        SetIfMissing(result, "overdraw", Math.Max(0d, semantics.Draw - handCapacity));
        SetIfMissing(result, "effectiveDamage", targetHp > 0d ? Math.Min(targetHp, damage) : damage);
        SetIfMissing(result, "overkill", targetHp > 0d ? Math.Max(0d, damage - targetHp) : 0d);
        SetIfMissing(result, "lethal", targetHp > 0d && damage >= targetHp ? 1d : 0d);
        var power = Value(state, "power");
        var maxPower = Value(state, "maxPower");
        SetIfMissing(result, "energyScarcity", maxPower <= 0d ? 1d : 1d - Math.Min(1d, power / maxPower));
        var usefulNow = damage + usefulDefend
                        + Math.Min(Math.Max(0d, semantics.Heal), missingHp)
                        + Math.Min(Math.Max(0d, semantics.Draw), handCapacity)
                        + Math.Max(0d, semantics.EnergyGain)
                        + Math.Max(0d, semantics.Buff)
                        + Math.Max(0d, semantics.Debuff)
                        + Math.Max(0d, semantics.Cleanse)
                        + Math.Max(0d, semantics.CostReduction)
                        + Math.Max(0d, semantics.CardGeneration)
                        + Math.Max(0d, semantics.PersistentValue)
                        + Math.Max(0d, semantics.Scaling) > 0d;
        SetIfMissing(result, "freeKnownValue", candidate.Cost == 0 && usefulNow && !semantics.RandomOutcome ? 1d : 0d);
        var confidence = recognizedSemantics
            ? 1d - Math.Min(1d, Math.Max(0d, semantics.Uncertainty) / 3d)
            : 0d;
        SetIfMissing(result, "semanticConfidence", semantics.RandomOutcome ? confidence * 0.7d : confidence);
        var category = Category(candidate);
        foreach (var name in new[] { "Attack", "Defend", "Support", "Skill", "Other" })
        {
            SetIfMissing(result, "category" + name, string.Equals(category, name, StringComparison.Ordinal) ? 1d : 0d);
        }
    }

    private static string Category(CombatTrainingCandidate candidate)
    {
        var semantics = candidate.Semantics ?? new CombatActionSemantics();
        if (semantics.Damage > 0d || semantics.TrueDamage > 0d || semantics.DamageOverTime > 0d) return "Attack";
        if (semantics.Defend > 0d) return "Defend";
        if (semantics.Heal > 0d
            || semantics.Draw > 0d
            || semantics.EnergyGain > 0d
            || semantics.Buff > 0d
            || semantics.Debuff > 0d
            || semantics.Cleanse > 0d
            || semantics.CostReduction > 0d
            || semantics.CardGeneration > 0d
            || semantics.PersistentValue > 0d
            || semantics.Scaling > 0d) return "Support";
        return string.Equals(candidate.ActionKind, "UseSkill", StringComparison.OrdinalIgnoreCase)
            ? "Skill"
            : "Other";
    }

    private static TrainingStatistics BuildStatistics(IEnumerable<PreferencePair> pairs)
    {
        var values = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            foreach (var side in new[] { pair.Positive, pair.Negative })
            {
                foreach (var feature in side)
                {
                    if (!values.TryGetValue(feature.Key, out var list))
                    {
                        list = new List<double>();
                        values[feature.Key] = list;
                    }
                    list.Add(Finite(feature.Value));
                }
            }
        }

        var result = new TrainingStatistics();
        foreach (var pair in values)
        {
            var mean = pair.Value.Average();
            var variance = pair.Value.Sum(value => (value - mean) * (value - mean)) / pair.Value.Count;
            result.Means[pair.Key] = mean;
            result.Scales[pair.Key] = Math.Max(0.000001d, Math.Sqrt(variance));
            result.Minimums[pair.Key] = pair.Value.Min();
            result.Maximums[pair.Key] = pair.Value.Max();
            result.Counts[pair.Key] = pair.Value.Count;
        }
        return result;
    }

    private static Dictionary<string, double> Difference(
        IReadOnlyDictionary<string, double> positive,
        IReadOnlyDictionary<string, double> negative,
        IReadOnlyDictionary<string, double> means,
        IReadOnlyDictionary<string, double> scales)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in positive.Keys.Union(negative.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var scale = scales.TryGetValue(key, out var configuredScale)
                ? Math.Max(0.000001d, Math.Abs(configuredScale))
                : 1d;
            var mean = means.TryGetValue(key, out var configuredMean) ? configuredMean : 0d;
            var value = ((positive.TryGetValue(key, out var p) ? p : 0d) - mean) / scale
                        - ((negative.TryGetValue(key, out var n) ? n : 0d) - mean) / scale;
            if (Math.Abs(value) > 0.000000000001d)
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static Dictionary<string, double> CategoryCounts(IEnumerable<PreferencePair> pairs)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            foreach (var side in new[] { pair.Positive, pair.Negative })
            {
                foreach (var category in new[]
                         {
                             "categoryAttack",
                             "categoryDefend",
                             "categorySupport",
                             "categorySkill",
                             "categoryOther"
                         })
                {
                    if (Value(side, category) > 0.5d)
                    {
                        result[category] = result.TryGetValue(category, out var count) ? count + 1d : 1d;
                    }
                }
            }
        }
        return result;
    }

    private static Dictionary<string, double> KeepUsed(
        IReadOnlyDictionary<string, double> source,
        IReadOnlyDictionary<string, double> weights)
    {
        return weights.Keys
            .Where(source.ContainsKey)
            .ToDictionary(key => key, key => source[key], StringComparer.OrdinalIgnoreCase);
    }

    private static double Accuracy(
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyList<WeightedVector> vectors)
    {
        var correct = vectors.Where(vector => Dot(weights, vector.Values) > 0d).Sum(vector => vector.Weight);
        var total = vectors.Sum(vector => vector.Weight);
        return total <= 0d ? 0d : correct / total;
    }

    private static Dictionary<string, double> FitWeights(
        TrainingStatistics statistics,
        IReadOnlyList<WeightedVector> source,
        CombatResidualTrainingOptions options,
        int seed)
    {
        var weights = statistics.Means.Keys.ToDictionary(
            key => key,
            _ => 0d,
            StringComparer.OrdinalIgnoreCase);
        var vectors = source.ToList();
        var random = new Random(seed);
        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            Shuffle(vectors, random);
            var rate = options.LearningRate / Math.Sqrt(1d + epoch * 0.05d);
            foreach (var vector in vectors)
            {
                var score = Math.Max(-30d, Math.Min(30d, Dot(weights, vector.Values)));
                var gradientFactor = vector.Weight / (1d + Math.Exp(score));
                foreach (var pair in vector.Values)
                {
                    var current = weights.TryGetValue(pair.Key, out var configured) ? configured : 0d;
                    weights[pair.Key] = Math.Max(
                        -2d,
                        Math.Min(
                            2d,
                            current + rate * (gradientFactor * pair.Value - options.L2 * current)));
                }
            }
        }
        return weights;
    }

    private static double GroupedHoldoutAccuracy(
        IReadOnlyList<PreferencePair> pairs,
        CombatResidualTrainingOptions options)
    {
        var groups = pairs
            .Select(pair => pair.BattleSessionId)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (groups.Length < 2)
        {
            return 0d;
        }

        var correct = 0d;
        var total = 0d;
        foreach (var group in groups)
        {
            var trainingPairs = pairs.Where(pair => pair.BattleSessionId != group).ToList();
            var validationPairs = pairs.Where(pair => pair.BattleSessionId == group).ToList();
            if (trainingPairs.Count == 0 || validationPairs.Count == 0)
            {
                continue;
            }
            var statistics = BuildStatistics(trainingPairs);
            var trainingVectors = trainingPairs
                .Select(pair => new WeightedVector(
                    Difference(pair.Positive, pair.Negative, statistics.Means, statistics.Scales),
                    pair.Weight,
                    pair.BattleSessionId))
                .Where(vector => vector.Values.Count > 0)
                .ToList();
            var validationVectors = validationPairs
                .Select(pair => new WeightedVector(
                    Difference(pair.Positive, pair.Negative, statistics.Means, statistics.Scales),
                    pair.Weight,
                    pair.BattleSessionId))
                .Where(vector => vector.Values.Count > 0)
                .ToList();
            if (trainingVectors.Count == 0 || validationVectors.Count == 0)
            {
                continue;
            }
            var weights = FitWeights(statistics, trainingVectors, options, 7 + groups.Length);
            correct += validationVectors
                .Where(vector => Dot(weights, vector.Values) > 0d)
                .Sum(vector => vector.Weight);
            total += validationVectors.Sum(vector => vector.Weight);
        }
        return total <= 0d ? 0d : correct / total;
    }

    private static double Dot(
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyDictionary<string, double> values)
    {
        return values.Sum(pair => (weights.TryGetValue(pair.Key, out var weight) ? weight : 0d) * pair.Value);
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var next = random.Next(i + 1);
            var value = values[i];
            values[i] = values[next];
            values[next] = value;
        }
    }

    private static void SetIfMissing(IDictionary<string, double> values, string key, double value)
    {
        if (!values.ContainsKey(key))
        {
            values[key] = Finite(value);
        }
    }

    private static double Value(IReadOnlyDictionary<string, double>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? Finite(value) : 0d;
    }

    private static string NormalizeProfile(string profile)
    {
        var value = (profile ?? "").Trim().ToLowerInvariant();
        return value == "aggressive" || value == "defensive" ? value : "balanced";
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }

    private sealed class PreferencePair
    {
        public PreferencePair(
            Dictionary<string, double> positive,
            Dictionary<string, double> negative,
            double weight,
            long battleSessionId)
        {
            Positive = positive;
            Negative = negative;
            Weight = weight;
            BattleSessionId = battleSessionId;
        }

        public Dictionary<string, double> Positive { get; }

        public Dictionary<string, double> Negative { get; }

        public double Weight { get; }

        public long BattleSessionId { get; }
    }

    private sealed class WeightedVector
    {
        public WeightedVector(
            Dictionary<string, double> values,
            double weight,
            long battleSessionId)
        {
            Values = values;
            Weight = weight;
            BattleSessionId = battleSessionId;
        }

        public Dictionary<string, double> Values { get; }

        public double Weight { get; }

        public long BattleSessionId { get; }
    }

    private sealed class TrainingStatistics
    {
        public Dictionary<string, double> Means { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> Scales { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> Minimums { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> Maximums { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> Counts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
