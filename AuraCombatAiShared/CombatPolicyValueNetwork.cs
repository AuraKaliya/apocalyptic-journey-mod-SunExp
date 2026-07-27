using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatPolicyValueCandidate
{
    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

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

public sealed class CombatPolicyValueNetworkDefinition
{
    public string ModelProtocol { get; set; } = "aura.combat-policy-value.mlp.v1";

    public int ProtocolVersion { get; set; } = 1;

    public int FeatureSchemaVersion { get; set; } =
        CombatPolicyValueProtocol.FeatureSchemaVersion;

    public string ModelId { get; set; } = "";

    public string DecisionProfile { get; set; } = "balanced";

    public int StateDimensions { get; set; } = 128;

    public int ActionDimensions { get; set; } = 96;

    public int HiddenDimensions { get; set; } = 64;

    public string FeatureEncodingMode { get; set; } = "partitioned-v2";

    public double[] StateWeights { get; set; } = Array.Empty<double>();

    public double[] StateBias { get; set; } = Array.Empty<double>();

    public double[] ActionWeights { get; set; } = Array.Empty<double>();

    public double[] ActionBias { get; set; } = Array.Empty<double>();

    public double[] PolicyWeights { get; set; } = Array.Empty<double>();

    public double PolicyBias { get; set; }

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

    public ManagedCombatPolicyValueModel(CombatPolicyValueNetworkDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (!CombatPolicyValueNetworkValidator.TryValidate(definition, out var reason))
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
        var state = CombatPolicyValueEncoding.EncodeState(
            input.StateFeatures,
            definition.StateDimensions,
            definition.FeatureEncodingMode);
        var hidden = DenseTanh(
            state,
            definition.StateWeights,
            definition.StateBias,
            definition.HiddenDimensions);
        var result = new CombatPolicyValuePrediction
        {
            ExpectedReturn = Clamp(Dot(hidden, definition.ValueWeights) + definition.ValueBias, -1d, 1d),
            WinProbability = Sigmoid(Dot(hidden, definition.WinWeights) + definition.WinBias),
            DeathProbability = Sigmoid(Dot(hidden, definition.RiskWeights) + definition.RiskBias),
            ExpectedRemainingHpRatio = Sigmoid(Dot(hidden, definition.HpWeights) + definition.HpBias),
            ExpectedRemainingTurns = Math.Max(
                0d,
                SoftPlus(Dot(hidden, definition.TurnWeights) + definition.TurnBias))
        };

        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        for (var i = 0; i < input.Candidates.Count; i++)
        {
            var candidate = input.Candidates[i] ?? new CombatPolicyValueCandidate();
            var action = CombatPolicyValueEncoding.EncodeCandidate(
                candidate,
                definition.ActionDimensions);
            var actionHidden = DenseTanh(
                action,
                definition.ActionWeights,
                definition.ActionBias,
                definition.HiddenDimensions);
            var interaction = 0d;
            for (var j = 0; j < hidden.Length; j++)
            {
                interaction += hidden[j] * actionHidden[j] * definition.PolicyWeights[j];
            }
            var logit = Clamp(interaction + definition.PolicyBias, -30d, 30d);
            result.PolicyLogits[candidate.CandidateId ?? ""] = logit;
            minimum = Math.Min(minimum, logit);
            maximum = Math.Max(maximum, logit);
        }
        result.Uncertainty = input.Candidates.Count <= 1
            ? 0d
            : 1d / (1d + Math.Max(0d, maximum - minimum));
        return result;
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        var result = new List<CombatPolicyValuePrediction>(inputs?.Count ?? 0);
        for (var i = 0; i < (inputs?.Count ?? 0); i++)
        {
            result.Add(Evaluate(inputs![i]));
        }
        return result;
    }

    private static double[] DenseTanh(
        IReadOnlyList<double> input,
        IReadOnlyList<double> weights,
        IReadOnlyList<double> bias,
        int outputs)
    {
        var result = new double[outputs];
        for (var output = 0; output < outputs; output++)
        {
            var value = bias[output];
            var offset = output * input.Count;
            for (var inputIndex = 0; inputIndex < input.Count; inputIndex++)
            {
                value += input[inputIndex] * weights[offset + inputIndex];
            }
            result[output] = Math.Tanh(value);
        }
        return result;
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var result = 0d;
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            result += left[i] * right[i];
        }
        return result;
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
        if (model == null
            || model.ModelProtocol != "aura.combat-policy-value.mlp.v1"
            || model.ProtocolVersion != 1
            || model.FeatureSchemaVersion
               != CombatPolicyValueProtocol.FeatureSchemaVersion)
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
        if (!string.Equals(
                model.FeatureEncodingMode,
                "hashed-v1",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                model.FeatureEncodingMode,
                "partitioned-v2",
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

public static class CombatPolicyValueEncoding
{
    private static readonly HashSet<string> ForbiddenStateFeatureKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "journeyProgress",
            "journeyBattleIndex",
            "journeyRemainingBattles",
            "finalBossVictory",
            "campaignVictory",
            "campaignCompletedBattles",
            "campaignTotalBattles",
            "failureBattleIndex",
            "outcome"
        };

    private static readonly string[] ForbiddenStateFeaturePrefixes =
    {
        "label:",
        "label.",
        "target:",
        "target.",
        "posthoc:",
        "posthoc.",
        "future:",
        "future."
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
        return EncodeState(values, dimensions, "hashed-v1");
    }

    public static double[] EncodeState(
        IReadOnlyDictionary<string, double>? values,
        int dimensions,
        string encodingMode)
    {
        var sanitized = SanitizeStateFeatures(values);
        if (!string.Equals(
                encodingMode,
                "partitioned-v2",
                StringComparison.OrdinalIgnoreCase)
            || dimensions < 64)
        {
            return Encode(sanitized, dimensions, "state");
        }
        var result = new double[Math.Max(1, dimensions)];
        foreach (var pair in sanitized)
        {
            var range = StateRange(pair.Key, result.Length);
            AddRange(
                result,
                range.Start,
                range.Length,
                "state:" + pair.Key,
                Normalize(pair.Value));
        }
        return result;
    }

    public static Dictionary<string, double> SanitizeStateFeatures(
        IReadOnlyDictionary<string, double>? values)
    {
        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, double>())
        {
            if (!IsPermittedStateFeature(pair.Key))
            {
                continue;
            }
            result[pair.Key] = pair.Value;
        }
        return result;
    }

    public static bool IsPermittedStateFeature(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || ForbiddenStateFeatureKeys.Contains(key!))
        {
            return false;
        }
        var permittedKey = key!;
        return !ForbiddenStateFeaturePrefixes.Any(prefix =>
            permittedKey.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));
    }

    public static double[] EncodeCandidate(
        CombatPolicyValueCandidate candidate,
        int dimensions)
    {
        var result = Encode(candidate.Features, dimensions, "action");
        Add(result, "source:" + (candidate.SourceId ?? ""), 1d);
        return result;
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
        foreach (var id in state.DrawPileCardIds ?? new List<string>())
        {
            Add(result, "draw:" + id, 1d);
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
        var result = new Dictionary<string, double>(
            action.Features ?? new Dictionary<string, double>(),
            StringComparer.OrdinalIgnoreCase)
        {
            ["cost"] = action.Cost,
            ["ruleScore"] = candidate.RuleScore,
            ["baseRuleScore"] = candidate.BaseRuleScore,
            ["planScore"] = candidate.PlanScore
        };
        var semantics = action.Semantics ?? new CombatActionSemantics();
        result["damage"] = semantics.Damage;
        result["trueDamage"] = semantics.TrueDamage;
        result["damageOverTime"] = semantics.DamageOverTime;
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
        var safeLength = Math.Max(1, Math.Min(length, values.Length - start));
        var hash = Hash(key);
        var index = start + (int)(hash % (uint)safeLength);
        var sign = (hash & 0x80000000u) == 0u ? 1d : -1d;
        values[index] += sign * amount;
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
        return ScaleRange(0, 32, dimensions);
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
