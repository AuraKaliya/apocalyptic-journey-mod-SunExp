using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraCombatAi.Shared;

public static class CombatModelAdapterProtocol
{
    public const string Protocol = "aura.combat-ai.adapter.v1";
    public const int SchemaVersion = 1;
    public const string ContentKind = "content-low-rank";
    public const string PersonalKind = "personal-residual";

    public const string TransformerProtocol =
        "aura.combat-ai.transformer-adapter.v2";

    public const int TransformerSchemaVersion = 2;

    public const string TransformerContentKind = "content-lora";

    public const string TransformerCampaignKind = "campaign-lora";

    public const string TransformerPreferenceKind = "preference-lora";
}

public sealed class CombatTransformerAdapterManifest
{
    public string Protocol { get; set; } =
        CombatModelAdapterProtocol.TransformerProtocol;

    public int SchemaVersion { get; set; } =
        CombatModelAdapterProtocol.TransformerSchemaVersion;

    public string AdapterId { get; set; } = "";

    public string AdapterKind { get; set; } =
        CombatModelAdapterProtocol.TransformerContentKind;

    public string OwnerModId { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string BaseModelId { get; set; } = "";

    public string BaseModelHash { get; set; } = "";

    public int TokenizerSchemaVersion { get; set; } =
        CombatWorldModelProtocol.TokenSchemaVersion;

    public int RuleIrSchemaVersion { get; set; } = 1;

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public string TrainingDataHash { get; set; } = "";

    public string AdapterWeightHash { get; set; } = "";

    public List<string> SupportedContentIds { get; set; } = new();

    public List<string> QuantizationCompatibility { get; set; } = new();

    public Dictionary<string, double> ValidationMetrics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatTransformerLoRAMatrix
{
    public string TargetModule { get; set; } = "";

    public int InputDimensions { get; set; }

    public int OutputDimensions { get; set; }

    public int Rank { get; set; } = 8;

    public double Alpha { get; set; } = 8d;

    public double Dropout { get; set; } = 0.05d;

    public double[] A { get; set; } = Array.Empty<double>();

    public double[] B { get; set; } = Array.Empty<double>();

    public int ParameterCount => A?.Length + B?.Length ?? 0;
}

public sealed class CombatTransformerLoRAAdapterDefinition
{
    public CombatTransformerAdapterManifest Manifest { get; set; } = new();

    public List<CombatTransformerLoRAMatrix> Matrices { get; set; } = new();

    public int TrainableParameterCount =>
        (Matrices ?? new List<CombatTransformerLoRAMatrix>())
        .Where(item => item != null)
        .Sum(item => item.ParameterCount);
}

public static class CombatTransformerAdapterValidator
{
    public static bool TryValidate(
        CombatTransformerLoRAAdapterDefinition? adapter,
        string expectedBaseModelId,
        string expectedBaseModelHash,
        string expectedContentSetHash,
        out string reason)
    {
        var manifest = adapter?.Manifest;
        if (adapter == null
            || manifest == null
            || manifest.Protocol != CombatModelAdapterProtocol.TransformerProtocol
            || manifest.SchemaVersion
               != CombatModelAdapterProtocol.TransformerSchemaVersion
            || string.IsNullOrWhiteSpace(manifest.AdapterId)
            || string.IsNullOrWhiteSpace(manifest.OwnerModId)
            || string.IsNullOrWhiteSpace(manifest.PackageId)
            || string.IsNullOrWhiteSpace(manifest.BaseModelId)
            || !CanonicalHash(manifest.BaseModelHash)
            || !CanonicalHash(manifest.ContentSetHash)
            || !CanonicalHash(manifest.OwnerModSetHash)
            || !CanonicalHash(manifest.TrainingDataHash)
            || !CanonicalHash(manifest.AdapterWeightHash)
            || manifest.TokenizerSchemaVersion
               != CombatWorldModelProtocol.TokenSchemaVersion
            || manifest.RuleIrSchemaVersion <= 0
            || !KnownKind(manifest.AdapterKind))
        {
            reason = "Transformer LoRA manifest is invalid";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(expectedBaseModelId)
            && !string.Equals(
                manifest.BaseModelId,
                expectedBaseModelId,
                StringComparison.Ordinal))
        {
            reason = "Transformer LoRA base model id does not match";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(expectedBaseModelHash)
            && !string.Equals(
                manifest.BaseModelHash,
                expectedBaseModelHash,
                StringComparison.Ordinal))
        {
            reason = "Transformer LoRA base model hash does not match";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(expectedContentSetHash)
            && !string.Equals(
                manifest.ContentSetHash,
                expectedContentSetHash,
                StringComparison.Ordinal))
        {
            reason = "Transformer LoRA content set does not match";
            return false;
        }

        var matrices = adapter.Matrices
                       ?? new List<CombatTransformerLoRAMatrix>();
        if (matrices.Count == 0
            || matrices.Count > 256
            || matrices.Any(item => !ValidMatrix(item))
            || matrices.GroupBy(
                    item => item.TargetModule,
                    StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
            || adapter.TrainableParameterCount <= 0
            || adapter.TrainableParameterCount > 10_000_000)
        {
            reason = "Transformer LoRA matrices are invalid";
            return false;
        }
        if (matrices.Any(item => ForbiddenTarget(item.TargetModule)))
        {
            reason = "Transformer LoRA cannot target legality or exact chance modules";
            return false;
        }
        if (string.Equals(
                manifest.AdapterKind,
                CombatModelAdapterProtocol.TransformerPreferenceKind,
                StringComparison.Ordinal)
            && matrices.Any(item => !item.TargetModule.StartsWith(
                "actor.",
                StringComparison.Ordinal)))
        {
            reason = "preference LoRA may target only actor modules";
            return false;
        }
        var supportedContentIds = manifest.SupportedContentIds
                                  ?? new List<string>();
        if (supportedContentIds.Any(string.IsNullOrWhiteSpace)
            || supportedContentIds.Distinct(
                    StringComparer.OrdinalIgnoreCase).Count()
               != supportedContentIds.Count
            || (manifest.ValidationMetrics
                ?? new Dictionary<string, double>()).Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key)
                    || !Finite(pair.Value)))
        {
            reason = "Transformer LoRA coverage or validation metrics are invalid";
            return false;
        }
        reason = "";
        return true;
    }

    public static string BuildMergeCacheKey(
        string baseModelHash,
        IEnumerable<CombatTransformerLoRAAdapterDefinition> adapters,
        string backend,
        string precision)
    {
        if (!CanonicalHash(baseModelHash))
        {
            throw new ArgumentException("base model hash is invalid", nameof(baseModelHash));
        }
        var identities = (adapters
                          ?? Array.Empty<CombatTransformerLoRAAdapterDefinition>())
            .Where(item => item?.Manifest != null)
            .Select(item => item.Manifest.AdapterId + "#"
                            + item.Manifest.AdapterWeightHash)
            .OrderBy(item => item, StringComparer.Ordinal);
        var canonical = baseModelHash + "\n"
                        + string.Join("\n", identities) + "\n"
                        + (backend ?? "").Trim().ToLowerInvariant() + "\n"
                        + (precision ?? "").Trim().ToLowerInvariant();
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))
            .Select(value => value.ToString("x2")));
    }

    private static bool ValidMatrix(CombatTransformerLoRAMatrix? item)
    {
        return item != null
               && !string.IsNullOrWhiteSpace(item.TargetModule)
               && item.TargetModule.Length <= 160
               && item.InputDimensions is >= 1 and <= 8192
               && item.OutputDimensions is >= 1 and <= 8192
               && item.Rank is >= 1 and <= 32
               && Finite(item.Alpha)
               && item.Alpha > 0d
               && Finite(item.Dropout)
               && item.Dropout is >= 0d and <= 0.5d
               && item.A?.Length == item.Rank * item.InputDimensions
               && item.B?.Length == item.OutputDimensions * item.Rank
               && Finite(item.A)
               && Finite(item.B);
    }

    private static bool ForbiddenTarget(string value)
    {
        var target = (value ?? "").Trim().ToLowerInvariant();
        return target.Contains("legality")
               || target.Contains("rule-kernel")
               || target.Contains("exact-chance")
               || target.Contains("execution");
    }

    private static bool KnownKind(string value)
    {
        return string.Equals(
                   value,
                   CombatModelAdapterProtocol.TransformerContentKind,
                   StringComparison.Ordinal)
               || string.Equals(
                   value,
                   CombatModelAdapterProtocol.TransformerCampaignKind,
                   StringComparison.Ordinal)
               || string.Equals(
                   value,
                   CombatModelAdapterProtocol.TransformerPreferenceKind,
                   StringComparison.Ordinal);
    }

    private static bool CanonicalHash(string? value)
    {
        return value != null
               && value.Length == 64
               && value.All(character =>
                   character is >= '0' and <= '9'
                   || character is >= 'a' and <= 'f');
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool Finite(IEnumerable<double>? values)
    {
        return values != null && values.All(Finite);
    }
}

public sealed class CombatTransformerAdapterComposition
{
    public List<CombatTransformerLoRAAdapterDefinition> ActiveAdapters {
        get;
        set;
    } = new();

    public Dictionary<string, string> RejectedAdapters { get; set; } =
        new(StringComparer.Ordinal);

    public string MergeCacheKey { get; set; } = "";

    public static CombatTransformerAdapterComposition Compose(
        IEnumerable<CombatTransformerLoRAAdapterDefinition>? adapters,
        string baseModelId,
        string baseModelHash,
        string contentSetHash,
        string ownerModSetHash,
        string backend,
        string precision,
        int maximumActiveAdapters = 8)
    {
        var result = new CombatTransformerAdapterComposition();
        var maximum = Math.Max(1, Math.Min(32, maximumActiveAdapters));
        var candidates = (adapters
                          ?? Array.Empty<CombatTransformerLoRAAdapterDefinition>())
            .Where(item => item?.Manifest != null)
            .ToList();
        var duplicateIds = candidates
            .GroupBy(item => item.Manifest.AdapterId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var adapter in candidates
                     .OrderBy(item => item.Manifest.AdapterId, StringComparer.Ordinal))
        {
            var id = adapter.Manifest.AdapterId;
            if (duplicateIds.Contains(id))
            {
                result.RejectedAdapters[id] =
                    "duplicate Transformer LoRA adapter id";
                continue;
            }
            if (!CombatTransformerAdapterValidator.TryValidate(
                    adapter,
                    baseModelId,
                    baseModelHash,
                    contentSetHash,
                    out var reason))
            {
                result.RejectedAdapters[id] = reason;
                continue;
            }
            if (!string.Equals(
                    adapter.Manifest.OwnerModSetHash,
                    ownerModSetHash,
                    StringComparison.Ordinal))
            {
                result.RejectedAdapters[id] =
                    "Transformer LoRA owner mod set does not match";
                continue;
            }
            if (!SupportsPrecision(adapter.Manifest, backend, precision))
            {
                result.RejectedAdapters[id] =
                    "Transformer LoRA does not declare the requested precision";
                continue;
            }
            if (result.ActiveAdapters.Count >= maximum)
            {
                result.RejectedAdapters[id] =
                    "active Transformer LoRA limit exceeded";
                continue;
            }
            result.ActiveAdapters.Add(adapter);
        }
        result.MergeCacheKey = CombatTransformerAdapterValidator.BuildMergeCacheKey(
            baseModelHash,
            result.ActiveAdapters,
            backend,
            precision);
        return result;
    }

    private static bool SupportsPrecision(
        CombatTransformerAdapterManifest manifest,
        string backend,
        string precision)
    {
        var declared = manifest.QuantizationCompatibility
                       ?? new List<string>();
        if (declared.Count == 0)
        {
            return true;
        }
        var normalizedBackend = (backend ?? "").Trim().ToLowerInvariant();
        var normalizedPrecision = (precision ?? "").Trim().ToLowerInvariant();
        return declared.Any(item =>
        {
            var value = (item ?? "").Trim().ToLowerInvariant();
            return value == normalizedPrecision
                   || value == normalizedBackend + ":" + normalizedPrecision;
        });
    }
}

public static class CombatTransformerLoRAMerger
{
    public static double[] MergeModule(
        IReadOnlyList<double> baseWeights,
        int inputDimensions,
        int outputDimensions,
        string targetModule,
        IEnumerable<CombatTransformerLoRAAdapterDefinition>? adapters,
        IEnumerable<string>? activeContentIds = null)
    {
        if (baseWeights == null)
        {
            throw new ArgumentNullException(nameof(baseWeights));
        }
        if (inputDimensions <= 0
            || outputDimensions <= 0
            || baseWeights.Count != inputDimensions * outputDimensions)
        {
            throw new ArgumentException("base module dimensions are invalid");
        }
        var result = baseWeights.ToArray();
        var activeContent = new HashSet<string>(
            activeContentIds ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in (adapters
                                 ?? Array.Empty<CombatTransformerLoRAAdapterDefinition>())
                     .Where(item => item?.Manifest != null)
                     .Where(item => IsActive(item.Manifest, activeContent))
                     .OrderBy(item => item.Manifest.AdapterId, StringComparer.Ordinal))
        {
            foreach (var matrix in (adapter.Matrices
                                    ?? new List<CombatTransformerLoRAMatrix>())
                         .Where(item => item != null
                                        && string.Equals(
                                            item.TargetModule,
                                            targetModule,
                                            StringComparison.Ordinal)))
            {
                if (matrix.InputDimensions != inputDimensions
                    || matrix.OutputDimensions != outputDimensions
                    || matrix.A.Length != matrix.Rank * inputDimensions
                    || matrix.B.Length != outputDimensions * matrix.Rank)
                {
                    throw new InvalidOperationException(
                        "LoRA matrix dimensions do not match " + targetModule);
                }
                var scale = matrix.Alpha / matrix.Rank;
                for (var output = 0; output < outputDimensions; output++)
                {
                    for (var input = 0; input < inputDimensions; input++)
                    {
                        var delta = 0d;
                        for (var rank = 0; rank < matrix.Rank; rank++)
                        {
                            delta += matrix.B[output * matrix.Rank + rank]
                                     * matrix.A[rank * inputDimensions + input];
                        }
                        result[output * inputDimensions + input] += delta * scale;
                    }
                }
            }
        }
        return result;
    }

    private static bool IsActive(
        CombatTransformerAdapterManifest manifest,
        HashSet<string> activeContent)
    {
        var supported = manifest.SupportedContentIds ?? new List<string>();
        return supported.Count == 0
               || supported.Any(activeContent.Contains);
    }
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
            || adapter.StateDimensions > 2048
            || adapter.ActionDimensions < 16
            || adapter.ActionDimensions > 2048
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
                result.TryGetPolicyLogit(key, out var basisLogit);
                result.SetPolicyLogit(key, Clamp(
                    basisLogit + delta,
                    -30d,
                    30d));
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
