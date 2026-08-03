using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatModelAdapterProtocol
{
    public const string Protocol = "aura.combat-ai.adapter.v1";
    public const int SchemaVersion = 1;
    public const string ContentKind = "content-low-rank";
    public const string PersonalKind = "personal-residual";
}

public sealed class CombatDecisionAdapterManifest
{
    public string Protocol { get; set; } = CombatModelAdapterProtocol.Protocol;

    public int SchemaVersion { get; set; } = CombatModelAdapterProtocol.SchemaVersion;

    public string AdapterId { get; set; } = "";

    public string AdapterKind { get; set; } = CombatModelAdapterProtocol.PersonalKind;

    public string OwnerModId { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string BaseModelId { get; set; } = "";

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public int FeatureSchemaVersion { get; set; } =
        CombatPolicyValueProtocol.FeatureSchemaVersion;

    public bool AdjustsPolicy { get; set; } = true;

    public bool AdjustsActionValue { get; set; }

    public double MaximumPolicyDelta { get; set; } = 1d;

    public double MaximumActionValueDelta { get; set; }
}

public sealed class CombatLowRankPolicyAdapterDefinition
{
    public CombatDecisionAdapterManifest Manifest { get; set; } = new()
    {
        AdapterKind = CombatModelAdapterProtocol.ContentKind
    };

    public int StateDimensions { get; set; } = 256;

    public int ActionDimensions { get; set; } = 192;

    public int Rank { get; set; } = 4;

    public string FeatureEncodingMode { get; set; } = "partitioned-v3";

    public double[] StateFactors { get; set; } = Array.Empty<double>();

    public double[] ActionFactors { get; set; } = Array.Empty<double>();

    public double[] RankWeights { get; set; } = Array.Empty<double>();

    public double Bias { get; set; }
}

public static class CombatModelAdapterValidator
{
    public static bool TryValidate(
        CombatDecisionAdapterManifest? manifest,
        string expectedBaseModelId,
        string expectedContentSetHash,
        out string reason)
    {
        if (manifest == null
            || manifest.Protocol != CombatModelAdapterProtocol.Protocol
            || manifest.SchemaVersion != CombatModelAdapterProtocol.SchemaVersion
            || manifest.FeatureSchemaVersion
               != CombatPolicyValueProtocol.FeatureSchemaVersion
            || string.IsNullOrWhiteSpace(manifest.AdapterId)
            || !(string.Equals(
                     manifest.AdapterKind,
                     CombatModelAdapterProtocol.ContentKind,
                     StringComparison.Ordinal)
                 || string.Equals(
                     manifest.AdapterKind,
                     CombatModelAdapterProtocol.PersonalKind,
                     StringComparison.Ordinal))
            || !CanonicalHash(manifest.ContentSetHash)
            || !CanonicalHash(manifest.OwnerModSetHash)
            || !FiniteInRange(manifest.MaximumPolicyDelta, 0d, 4d)
            || !FiniteInRange(manifest.MaximumActionValueDelta, 0d, 1d))
        {
            reason = "模型适配器协议、特征版本或修正上限无效";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(expectedBaseModelId)
            && !string.Equals(
                manifest.BaseModelId,
                expectedBaseModelId,
                StringComparison.Ordinal))
        {
            reason = "模型适配器绑定的底模不匹配";
            return false;
        }
        if (string.Equals(
                manifest.AdapterKind,
                CombatModelAdapterProtocol.PersonalKind,
                StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(manifest.BaseModelId))
        {
            reason = "玩家适配器必须绑定明确的底模";
            return false;
        }
        if (string.Equals(
                manifest.AdapterKind,
                CombatModelAdapterProtocol.PersonalKind,
                StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(manifest.OwnerModId)
                || !manifest.AdjustsPolicy))
        {
            reason = "玩家适配器必须声明所有者并启用策略残差";
            return false;
        }
        if (string.Equals(
                manifest.AdapterKind,
                CombatModelAdapterProtocol.PersonalKind,
                StringComparison.Ordinal)
            && !string.Equals(
                manifest.ContentSetHash,
                expectedContentSetHash,
                StringComparison.Ordinal))
        {
            reason = "玩家适配器绑定的内容集合不匹配";
            return false;
        }
        if (string.Equals(
                manifest.AdapterKind,
                CombatModelAdapterProtocol.PersonalKind,
                StringComparison.Ordinal)
            && manifest.AdjustsActionValue)
        {
            reason = "玩家适配器不得修改动作 Q；动作 Q 只接受权威内容训练";
            return false;
        }
        reason = "";
        return true;
    }

    public static bool TryValidate(
        CombatLowRankPolicyAdapterDefinition? adapter,
        string expectedBaseModelId,
        string expectedContentSetHash,
        out string reason)
    {
        if (adapter == null)
        {
            reason = "低秩内容适配器为空";
            return false;
        }
        if (!TryValidate(
                adapter.Manifest,
                expectedBaseModelId,
                expectedContentSetHash,
                out reason))
        {
            return false;
        }
        if (!string.Equals(
                adapter.Manifest.AdapterKind,
                CombatModelAdapterProtocol.ContentKind,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(adapter.Manifest.OwnerModId)
            || string.IsNullOrWhiteSpace(adapter.Manifest.PackageId)
            || string.IsNullOrWhiteSpace(adapter.Manifest.BaseModelId)
            || !adapter.Manifest.AdjustsPolicy
            || adapter.Manifest.AdjustsActionValue
            || adapter.Manifest.MaximumActionValueDelta != 0d
            || adapter.StateDimensions < 16
            || adapter.StateDimensions > 512
            || adapter.ActionDimensions < 16
            || adapter.ActionDimensions > 512
            || adapter.Rank < 1
            || adapter.Rank > 32
            || !string.Equals(
                adapter.FeatureEncodingMode,
                "partitioned-v3",
                StringComparison.OrdinalIgnoreCase)
            || adapter.StateFactors?.Length
               != adapter.StateDimensions * adapter.Rank
            || adapter.ActionFactors?.Length
               != adapter.ActionDimensions * adapter.Rank
            || adapter.RankWeights?.Length != adapter.Rank
            || !Finite(adapter.StateFactors)
            || !Finite(adapter.ActionFactors)
            || !Finite(adapter.RankWeights)
            || double.IsNaN(adapter.Bias)
            || double.IsInfinity(adapter.Bias))
        {
            reason = "低秩内容适配器结构或权重无效";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool FiniteInRange(double value, double minimum, double maximum)
    {
        return !double.IsNaN(value)
               && !double.IsInfinity(value)
               && value >= minimum
               && value <= maximum;
    }

    private static bool Finite(IEnumerable<double>? values)
    {
        return values != null && values.All(value =>
            !double.IsNaN(value) && !double.IsInfinity(value));
    }

    private static bool CanonicalHash(string? value)
    {
        return value != null
               && value.Length == 64
               && value.All(character =>
                   character is >= '0' and <= '9'
                   || character is >= 'a' and <= 'f');
    }
}

public sealed class AdaptedCombatPolicyValueModel : ICombatPolicyValueModel
{
    private readonly ICombatPolicyValueModel basis;
    private readonly IReadOnlyList<CombatLowRankPolicyAdapterDefinition> adapters;

    public AdaptedCombatPolicyValueModel(
        ICombatPolicyValueModel basis,
        IEnumerable<CombatLowRankPolicyAdapterDefinition> adapters)
    {
        this.basis = basis ?? throw new ArgumentNullException(nameof(basis));
        this.adapters = (adapters ?? Array.Empty<CombatLowRankPolicyAdapterDefinition>())
            .ToArray();
    }

    public string ModelId => basis.ModelId;

    public IReadOnlyList<string> AdapterIds => adapters
        .Select(item => item.Manifest.AdapterId)
        .ToArray();

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
        var result = basis.Evaluate(input);
        Apply(input, result);
        return result;
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        var results = basis.EvaluateBatch(inputs);
        for (var index = 0; index < results.Count; index++)
        {
            Apply(inputs[index], results[index]);
        }
        return results;
    }

    private void Apply(
        CombatPolicyValueInput input,
        CombatPolicyValuePrediction result)
    {
        foreach (var adapter in adapters)
        {
            if (!adapter.Manifest.AdjustsPolicy)
            {
                continue;
            }
            var state = CombatPolicyValueEncoding.EncodeState(
                input.StateFeatures,
                adapter.StateDimensions,
                adapter.FeatureEncodingMode);
            foreach (var candidate in input.Candidates)
            {
                var action = CombatPolicyValueEncoding.EncodeCandidate(
                    candidate,
                    adapter.ActionDimensions,
                    adapter.FeatureEncodingMode);
                var delta = LowRankDelta(adapter, state, action);
                var key = candidate.CandidateId ?? "";
                result.PolicyLogits.TryGetValue(key, out var basisLogit);
                result.PolicyLogits[key] = Clamp(
                    basisLogit + delta,
                    -30d,
                    30d);
            }
        }
    }

    private static double LowRankDelta(
        CombatLowRankPolicyAdapterDefinition adapter,
        IReadOnlyList<double> state,
        IReadOnlyList<double> action)
    {
        var delta = adapter.Bias;
        for (var rank = 0; rank < adapter.Rank; rank++)
        {
            var stateProjection = 0d;
            var actionProjection = 0d;
            for (var index = 0; index < state.Count; index++)
            {
                stateProjection += state[index]
                                   * adapter.StateFactors[
                                       rank * adapter.StateDimensions + index];
            }
            for (var index = 0; index < action.Count; index++)
            {
                actionProjection += action[index]
                                    * adapter.ActionFactors[
                                        rank * adapter.ActionDimensions + index];
            }
            delta += adapter.RankWeights[rank]
                     * Math.Tanh(stateProjection)
                     * Math.Tanh(actionProjection);
        }
        var limit = Math.Max(0d, adapter.Manifest.MaximumPolicyDelta);
        return Clamp(delta, -limit, limit);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}
