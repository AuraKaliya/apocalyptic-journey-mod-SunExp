using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace AuraCombatAi.Shared;

public sealed class CombatPolicyValueArtifactManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ArtifactKind { get; set; } =
        CombatPolicyValueArtifactProtocol.ArtifactKind;

    public string Precision { get; set; } =
        CombatPolicyValueArtifactProtocol.Precision;

    public string WeightLayout { get; set; } =
        CombatPolicyValueArtifactProtocol.WeightLayout;

    public string WeightsFile { get; set; } = "";

    public string WeightsSha256 { get; set; } = "";

    public long WeightsByteLength { get; set; }

    public int WeightValueCount { get; set; }

    public string ModelProtocol { get; set; } = "aura.combat-policy-value.mlp.v2";

    public int ProtocolVersion { get; set; } = 2;

    public int FeatureSchemaVersion { get; set; } =
        CombatPolicyValueProtocol.FeatureSchemaVersion;

    public string ModelId { get; set; } = "";

    public string DecisionProfile { get; set; } = "balanced";

    public int StateDimensions { get; set; }

    public int ActionDimensions { get; set; }

    public int HiddenDimensions { get; set; }

    public string FeatureEncodingMode { get; set; } = "partitioned-v3";

    public float PolicyTemperature { get; set; } = 1f;

    public int ActionQuantileCount { get; set; } = 16;

    public bool ActionQuantileHeadReady { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CombatPolicyValueRuntimeDefinition
{
    public string ModelProtocol { get; set; } = "aura.combat-policy-value.mlp.v2";

    public int ProtocolVersion { get; set; } = 2;

    public int FeatureSchemaVersion { get; set; } =
        CombatPolicyValueProtocol.FeatureSchemaVersion;

    public string ModelId { get; set; } = "";

    public string DecisionProfile { get; set; } = "balanced";

    public int StateDimensions { get; set; }

    public int ActionDimensions { get; set; }

    public int HiddenDimensions { get; set; }

    public string FeatureEncodingMode { get; set; } = "partitioned-v3";

    public float PolicyTemperature { get; set; } = 1f;

    public float[] StateWeightsByInput { get; set; } = Array.Empty<float>();

    public float[] StateBias { get; set; } = Array.Empty<float>();

    public float[] ActionWeightsByInput { get; set; } = Array.Empty<float>();

    public float[] ActionBias { get; set; } = Array.Empty<float>();

    public float[] PolicyWeights { get; set; } = Array.Empty<float>();

    public float PolicyBias { get; set; }

    public int ActionQuantileCount { get; set; } = 16;

    public bool ActionQuantileHeadReady { get; set; }

    public float[] ActionQuantileWeights { get; set; } = Array.Empty<float>();

    public float[] ActionQuantileBias { get; set; } = Array.Empty<float>();

    public float[] ValueWeights { get; set; } = Array.Empty<float>();

    public float ValueBias { get; set; }

    public float[] WinWeights { get; set; } = Array.Empty<float>();

    public float WinBias { get; set; }

    public float[] RiskWeights { get; set; } = Array.Empty<float>();

    public float RiskBias { get; set; }

    public float[] HpWeights { get; set; } = Array.Empty<float>();

    public float HpBias { get; set; }

    public float[] TurnWeights { get; set; } = Array.Empty<float>();

    public float TurnBias { get; set; }
}

public static class CombatPolicyValueArtifactProtocol
{
    public const string ArtifactKind = "aura.combat-policy-value.weights";

    public const string Precision = "float32-le";

    public const string WeightLayout = "fixed-v1-state-action-input-major";

    public const long MaximumWeightBytes = 32L * 1024L * 1024L;

    public static CombatPolicyValueArtifactManifest Write(
        string weightsPath,
        CombatPolicyValueNetworkDefinition model)
    {
        if (string.IsNullOrWhiteSpace(weightsPath))
        {
            throw new ArgumentException("权重路径为空", nameof(weightsPath));
        }
        if (!CombatPolicyValueNetworkValidator.TryValidate(
                model,
                out var diagnostic))
        {
            throw new ArgumentException(
                "无法发布无效的策略价值模型：" + diagnostic,
                nameof(model));
        }

        var fullPath = Path.GetFullPath(weightsPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        CombatFoundationCheckpointStorage.WriteAtomicStream(
            fullPath,
            stream => WritePayload(stream, model),
            retainBackup: false);

        var info = new FileInfo(fullPath);
        var manifest = CreateManifest(
            model,
            Path.GetFileName(fullPath),
            info.Length,
            HashFile(fullPath));
        if (!TryValidateManifest(manifest, out diagnostic))
        {
            throw new InvalidDataException(diagnostic);
        }
        return manifest;
    }

    public static CombatPolicyValueArtifactManifest CreateManifest(
        CombatPolicyValueNetworkDefinition model,
        string weightsFile,
        long weightsByteLength,
        string weightsSha256)
    {
        return new CombatPolicyValueArtifactManifest
        {
            WeightsFile = Path.GetFileName(weightsFile ?? ""),
            WeightsByteLength = weightsByteLength,
            WeightsSha256 = weightsSha256 ?? "",
            WeightValueCount = WeightValueCount(
                model.StateDimensions,
                model.ActionDimensions,
                model.HiddenDimensions,
                model.ActionQuantileCount),
            ModelProtocol = model.ModelProtocol,
            ProtocolVersion = model.ProtocolVersion,
            FeatureSchemaVersion = model.FeatureSchemaVersion,
            ModelId = model.ModelId,
            DecisionProfile = model.DecisionProfile,
            StateDimensions = model.StateDimensions,
            ActionDimensions = model.ActionDimensions,
            HiddenDimensions = model.HiddenDimensions,
            FeatureEncodingMode = model.FeatureEncodingMode,
            PolicyTemperature = (float)model.PolicyTemperature,
            ActionQuantileCount = model.ActionQuantileCount,
            ActionQuantileHeadReady = model.ActionQuantileHeadReady,
            CreatedUtc = model.CreatedUtc
        };
    }

    public static CombatPolicyValueRuntimeDefinition FromTrainingDefinition(
        CombatPolicyValueNetworkDefinition model,
        bool allowDiagnosticLegacySchema = false)
    {
        if (!CombatPolicyValueNetworkValidator.TryValidate(
                model,
                out var diagnostic,
                allowDiagnosticLegacySchema))
        {
            throw new ArgumentException(diagnostic, nameof(model));
        }
        return new CombatPolicyValueRuntimeDefinition
        {
            ModelProtocol = model.ModelProtocol,
            ProtocolVersion = model.ProtocolVersion,
            FeatureSchemaVersion = model.FeatureSchemaVersion,
            ModelId = model.ModelId,
            DecisionProfile = model.DecisionProfile,
            StateDimensions = model.StateDimensions,
            ActionDimensions = model.ActionDimensions,
            HiddenDimensions = model.HiddenDimensions,
            FeatureEncodingMode = model.FeatureEncodingMode,
            PolicyTemperature = (float)model.PolicyTemperature,
            StateWeightsByInput = TransposeToFloat(
                model.StateWeights,
                model.StateDimensions,
                model.HiddenDimensions),
            StateBias = ToFloat(model.StateBias),
            ActionWeightsByInput = TransposeToFloat(
                model.ActionWeights,
                model.ActionDimensions,
                model.HiddenDimensions),
            ActionBias = ToFloat(model.ActionBias),
            PolicyWeights = ToFloat(model.PolicyWeights),
            PolicyBias = (float)model.PolicyBias,
            ActionQuantileCount = model.ActionQuantileCount,
            ActionQuantileHeadReady = model.ActionQuantileHeadReady,
            ActionQuantileWeights = ToFloat(model.ActionQuantileWeights),
            ActionQuantileBias = ToFloat(model.ActionQuantileBias),
            ValueWeights = ToFloat(model.ValueWeights),
            ValueBias = (float)model.ValueBias,
            WinWeights = ToFloat(model.WinWeights),
            WinBias = (float)model.WinBias,
            RiskWeights = ToFloat(model.RiskWeights),
            RiskBias = (float)model.RiskBias,
            HpWeights = ToFloat(model.HpWeights),
            HpBias = (float)model.HpBias,
            TurnWeights = ToFloat(model.TurnWeights),
            TurnBias = (float)model.TurnBias
        };
    }

    public static CombatPolicyValueNetworkDefinition ToTrainingDefinition(
        CombatPolicyValueRuntimeDefinition runtime)
    {
        if (!TryValidateRuntime(runtime, out var diagnostic))
        {
            throw new ArgumentException(diagnostic, nameof(runtime));
        }
        return new CombatPolicyValueNetworkDefinition
        {
            ModelProtocol = runtime.ModelProtocol,
            ProtocolVersion = runtime.ProtocolVersion,
            FeatureSchemaVersion = runtime.FeatureSchemaVersion,
            ModelId = runtime.ModelId,
            DecisionProfile = runtime.DecisionProfile,
            StateDimensions = runtime.StateDimensions,
            ActionDimensions = runtime.ActionDimensions,
            HiddenDimensions = runtime.HiddenDimensions,
            FeatureEncodingMode = runtime.FeatureEncodingMode,
            PolicyTemperature = runtime.PolicyTemperature,
            StateWeights = TransposeToDouble(
                runtime.StateWeightsByInput,
                runtime.StateDimensions,
                runtime.HiddenDimensions),
            StateBias = ToDouble(runtime.StateBias),
            ActionWeights = TransposeToDouble(
                runtime.ActionWeightsByInput,
                runtime.ActionDimensions,
                runtime.HiddenDimensions),
            ActionBias = ToDouble(runtime.ActionBias),
            PolicyWeights = ToDouble(runtime.PolicyWeights),
            PolicyBias = runtime.PolicyBias,
            ActionQuantileCount = runtime.ActionQuantileCount,
            ActionQuantileHeadReady = runtime.ActionQuantileHeadReady,
            ActionQuantileWeights = ToDouble(runtime.ActionQuantileWeights),
            ActionQuantileBias = ToDouble(runtime.ActionQuantileBias),
            ValueWeights = ToDouble(runtime.ValueWeights),
            ValueBias = runtime.ValueBias,
            WinWeights = ToDouble(runtime.WinWeights),
            WinBias = runtime.WinBias,
            RiskWeights = ToDouble(runtime.RiskWeights),
            RiskBias = runtime.RiskBias,
            HpWeights = ToDouble(runtime.HpWeights),
            HpBias = runtime.HpBias,
            TurnWeights = ToDouble(runtime.TurnWeights),
            TurnBias = runtime.TurnBias,
            CreatedUtc = DateTime.UtcNow
        };
    }

    public static bool TryLoad(
        string directory,
        CombatPolicyValueArtifactManifest? manifest,
        out CombatPolicyValueRuntimeDefinition runtime,
        out string diagnostic)
    {
        runtime = new CombatPolicyValueRuntimeDefinition();
        if (!TryValidateManifest(manifest, out diagnostic))
        {
            return false;
        }
        try
        {
            var root = Path.GetFullPath(directory ?? "");
            var path = Path.GetFullPath(Path.Combine(root, manifest!.WeightsFile));
            if (!string.Equals(
                    Path.GetDirectoryName(path)?.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    root.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "FP32 权重文件必须与清单位于同一目录";
                return false;
            }
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != manifest.WeightsByteLength)
            {
                diagnostic = "FP32 权重文件缺失或长度不匹配";
                return false;
            }
            if (!string.Equals(
                    HashFile(path),
                    manifest.WeightsSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "FP32 权重文件哈希不匹配";
                return false;
            }
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream);
            runtime = ReadRuntime(reader, manifest);
            if (stream.Position != stream.Length)
            {
                diagnostic = "FP32 权重文件包含未声明的尾部数据";
                runtime = new CombatPolicyValueRuntimeDefinition();
                return false;
            }
            if (!TryValidateRuntime(runtime, out diagnostic))
            {
                runtime = new CombatPolicyValueRuntimeDefinition();
                return false;
            }
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "读取 FP32 权重失败：" + ex.Message;
            runtime = new CombatPolicyValueRuntimeDefinition();
            return false;
        }
    }

    public static bool TryValidatePayload(
        string directory,
        CombatPolicyValueArtifactManifest? manifest,
        out string diagnostic)
    {
        if (!TryValidateManifest(manifest, out diagnostic))
        {
            return false;
        }
        try
        {
            var root = Path.GetFullPath(directory ?? "");
            var path = Path.GetFullPath(Path.Combine(root, manifest!.WeightsFile));
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != manifest.WeightsByteLength)
            {
                diagnostic = "FP32 权重文件缺失或长度不匹配";
                return false;
            }
            if (!string.Equals(
                    Path.GetDirectoryName(path),
                    root.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    HashFile(path),
                    manifest.WeightsSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "FP32 权重文件路径或哈希不匹配";
                return false;
            }
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "校验 FP32 权重失败：" + ex.Message;
            return false;
        }
    }

    public static bool TryValidateManifest(
        CombatPolicyValueArtifactManifest? manifest,
        out string diagnostic)
    {
        if (manifest == null
            || manifest.SchemaVersion != 1
            || !string.Equals(
                manifest.ArtifactKind,
                ArtifactKind,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.Precision,
                Precision,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.WeightLayout,
                WeightLayout,
                StringComparison.Ordinal))
        {
            diagnostic = "FP32 权重清单协议不兼容";
            return false;
        }
        var expectedCount = WeightValueCount(
            manifest.StateDimensions,
            manifest.ActionDimensions,
            manifest.HiddenDimensions,
            manifest.ActionQuantileCount);
        if (manifest.StateDimensions < 16
            || manifest.StateDimensions > 2048
            || manifest.ActionDimensions < 16
            || manifest.ActionDimensions > 2048
            || manifest.HiddenDimensions < 8
            || manifest.HiddenDimensions > 1024
            || manifest.ActionQuantileCount < 4
            || manifest.ActionQuantileCount > 64
            || manifest.WeightValueCount != expectedCount
            || manifest.WeightsByteLength != expectedCount * sizeof(float)
            || manifest.WeightsByteLength <= 0
            || manifest.WeightsByteLength > MaximumWeightBytes
            || !string.Equals(
                manifest.ModelProtocol,
                "aura.combat-policy-value.mlp.v2",
                StringComparison.Ordinal)
            || manifest.ProtocolVersion != 2
            || string.IsNullOrWhiteSpace(manifest.ModelId)
            || string.IsNullOrWhiteSpace(manifest.DecisionProfile)
            || string.IsNullOrWhiteSpace(manifest.WeightsFile)
            || !string.Equals(
                Path.GetFileName(manifest.WeightsFile),
                manifest.WeightsFile,
                StringComparison.Ordinal)
            || !ValidHash(manifest.WeightsSha256)
            || !Finite(manifest.PolicyTemperature)
            || manifest.PolicyTemperature < 0.25f
            || manifest.PolicyTemperature > 4f
            || manifest.FeatureSchemaVersion
               != CombatPolicyValueProtocol.FeatureSchemaVersion
            || !string.Equals(
                manifest.FeatureEncodingMode,
                "partitioned-v3",
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = "FP32 权重清单字段或尺寸无效";
            return false;
        }
        diagnostic = "";
        return true;
    }

    public static bool TryValidateRuntime(
        CombatPolicyValueRuntimeDefinition? runtime,
        out string diagnostic)
    {
        if (runtime == null)
        {
            diagnostic = "FP32 运行时模型为空";
            return false;
        }
        var hidden = runtime.HiddenDimensions;
        if (!string.Equals(
                runtime.ModelProtocol,
                "aura.combat-policy-value.mlp.v2",
                StringComparison.Ordinal)
            || runtime.ProtocolVersion != 2
            || runtime.FeatureSchemaVersion < 1
            || runtime.FeatureSchemaVersion
               > CombatPolicyValueProtocol.FeatureSchemaVersion
            || string.IsNullOrWhiteSpace(runtime.ModelId)
            || string.IsNullOrWhiteSpace(runtime.DecisionProfile)
            || !string.Equals(
                runtime.FeatureEncodingMode,
                "partitioned-v3",
                StringComparison.OrdinalIgnoreCase)
            || runtime.StateDimensions < 16
            || runtime.StateDimensions > 2048
            || runtime.ActionDimensions < 16
            || runtime.ActionDimensions > 2048
            || hidden < 8
            || hidden > 1024
            || runtime.ActionQuantileCount < 4
            || runtime.ActionQuantileCount > 64
            || runtime.PolicyTemperature < 0.25f
            || runtime.PolicyTemperature > 4f
            || !Length(
                runtime.StateWeightsByInput,
                runtime.StateDimensions * hidden)
            || !Length(runtime.StateBias, hidden)
            || !Length(
                runtime.ActionWeightsByInput,
                runtime.ActionDimensions * hidden)
            || !Length(runtime.ActionBias, hidden)
            || !Length(runtime.PolicyWeights, hidden)
            || !Length(
                runtime.ActionQuantileWeights,
                runtime.ActionQuantileCount * hidden)
            || !Length(runtime.ActionQuantileBias, runtime.ActionQuantileCount)
            || !Length(runtime.ValueWeights, hidden)
            || !Length(runtime.WinWeights, hidden)
            || !Length(runtime.RiskWeights, hidden)
            || !Length(runtime.HpWeights, hidden)
            || !Length(runtime.TurnWeights, hidden)
            || !Finite(runtime.StateWeightsByInput)
            || !Finite(runtime.StateBias)
            || !Finite(runtime.ActionWeightsByInput)
            || !Finite(runtime.ActionBias)
            || !Finite(runtime.PolicyWeights)
            || !Finite(runtime.ActionQuantileWeights)
            || !Finite(runtime.ActionQuantileBias)
            || !Finite(runtime.ValueWeights)
            || !Finite(runtime.WinWeights)
            || !Finite(runtime.RiskWeights)
            || !Finite(runtime.HpWeights)
            || !Finite(runtime.TurnWeights)
            || !Finite(runtime.PolicyTemperature)
            || !Finite(runtime.PolicyBias)
            || !Finite(runtime.ValueBias)
            || !Finite(runtime.WinBias)
            || !Finite(runtime.RiskBias)
            || !Finite(runtime.HpBias)
            || !Finite(runtime.TurnBias))
        {
            diagnostic = "FP32 运行时模型尺寸或数值无效";
            return false;
        }
        diagnostic = "";
        return true;
    }

    private static void WritePayload(
        Stream stream,
        CombatPolicyValueNetworkDefinition model)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        WriteTransposed(
            writer,
            model.StateWeights,
            model.StateDimensions,
            model.HiddenDimensions);
        Write(writer, model.StateBias);
        WriteTransposed(
            writer,
            model.ActionWeights,
            model.ActionDimensions,
            model.HiddenDimensions);
        Write(writer, model.ActionBias);
        Write(writer, model.PolicyWeights);
        writer.Write((float)model.PolicyBias);
        Write(writer, model.ActionQuantileWeights);
        Write(writer, model.ActionQuantileBias);
        Write(writer, model.ValueWeights);
        writer.Write((float)model.ValueBias);
        Write(writer, model.WinWeights);
        writer.Write((float)model.WinBias);
        Write(writer, model.RiskWeights);
        writer.Write((float)model.RiskBias);
        Write(writer, model.HpWeights);
        writer.Write((float)model.HpBias);
        Write(writer, model.TurnWeights);
        writer.Write((float)model.TurnBias);
        writer.Flush();
    }

    private static CombatPolicyValueRuntimeDefinition ReadRuntime(
        BinaryReader reader,
        CombatPolicyValueArtifactManifest manifest)
    {
        var hidden = manifest.HiddenDimensions;
        return new CombatPolicyValueRuntimeDefinition
        {
            ModelProtocol = manifest.ModelProtocol,
            ProtocolVersion = manifest.ProtocolVersion,
            FeatureSchemaVersion = manifest.FeatureSchemaVersion,
            ModelId = manifest.ModelId,
            DecisionProfile = manifest.DecisionProfile,
            StateDimensions = manifest.StateDimensions,
            ActionDimensions = manifest.ActionDimensions,
            HiddenDimensions = hidden,
            FeatureEncodingMode = manifest.FeatureEncodingMode,
            PolicyTemperature = manifest.PolicyTemperature,
            StateWeightsByInput = Read(
                reader,
                manifest.StateDimensions * hidden),
            StateBias = Read(reader, hidden),
            ActionWeightsByInput = Read(
                reader,
                manifest.ActionDimensions * hidden),
            ActionBias = Read(reader, hidden),
            PolicyWeights = Read(reader, hidden),
            PolicyBias = reader.ReadSingle(),
            ActionQuantileCount = manifest.ActionQuantileCount,
            ActionQuantileHeadReady = manifest.ActionQuantileHeadReady,
            ActionQuantileWeights = Read(
                reader,
                manifest.ActionQuantileCount * hidden),
            ActionQuantileBias = Read(reader, manifest.ActionQuantileCount),
            ValueWeights = Read(reader, hidden),
            ValueBias = reader.ReadSingle(),
            WinWeights = Read(reader, hidden),
            WinBias = reader.ReadSingle(),
            RiskWeights = Read(reader, hidden),
            RiskBias = reader.ReadSingle(),
            HpWeights = Read(reader, hidden),
            HpBias = reader.ReadSingle(),
            TurnWeights = Read(reader, hidden),
            TurnBias = reader.ReadSingle()
        };
    }

    private static int WeightValueCount(
        int stateDimensions,
        int actionDimensions,
        int hiddenDimensions,
        int quantileCount)
    {
        try
        {
            return checked(
                stateDimensions * hiddenDimensions
                + hiddenDimensions
                + actionDimensions * hiddenDimensions
                + hiddenDimensions
                + hiddenDimensions
                + 1
                + quantileCount * hiddenDimensions
                + quantileCount
                + hiddenDimensions + 1
                + hiddenDimensions + 1
                + hiddenDimensions + 1
                + hiddenDimensions + 1
                + hiddenDimensions + 1);
        }
        catch (OverflowException)
        {
            return -1;
        }
    }

    private static void Write(BinaryWriter writer, IEnumerable<double> values)
    {
        foreach (var value in values)
        {
            writer.Write((float)value);
        }
    }

    private static void WriteTransposed(
        BinaryWriter writer,
        IReadOnlyList<double> outputMajor,
        int inputDimensions,
        int outputDimensions)
    {
        for (var input = 0; input < inputDimensions; input++)
        {
            for (var output = 0; output < outputDimensions; output++)
            {
                writer.Write((float)outputMajor[output * inputDimensions + input]);
            }
        }
    }

    private static float[] Read(BinaryReader reader, int count)
    {
        var values = new float[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = reader.ReadSingle();
        }
        return values;
    }

    private static float[] ToFloat(IReadOnlyList<double> source)
    {
        var result = new float[source.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = (float)source[index];
        }
        return result;
    }

    private static float[] TransposeToFloat(
        IReadOnlyList<double> source,
        int inputDimensions,
        int outputDimensions)
    {
        var result = new float[checked(inputDimensions * outputDimensions)];
        for (var output = 0; output < outputDimensions; output++)
        {
            var sourceOffset = output * inputDimensions;
            for (var input = 0; input < inputDimensions; input++)
            {
                result[input * outputDimensions + output] =
                    (float)source[sourceOffset + input];
            }
        }
        return result;
    }

    private static double[] ToDouble(IReadOnlyList<float> source)
    {
        var result = new double[source.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = source[index];
        }
        return result;
    }

    private static double[] TransposeToDouble(
        IReadOnlyList<float> inputMajor,
        int inputDimensions,
        int outputDimensions)
    {
        var result = new double[checked(inputDimensions * outputDimensions)];
        for (var input = 0; input < inputDimensions; input++)
        {
            var sourceOffset = input * outputDimensions;
            for (var output = 0; output < outputDimensions; output++)
            {
                result[output * inputDimensions + input] =
                    inputMajor[sourceOffset + output];
            }
        }
        return result;
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static bool ValidHash(string value)
    {
        return value != null
               && value.Length == 64
               && value.All(character =>
                   character >= '0' && character <= '9'
                   || character >= 'a' && character <= 'f'
                   || character >= 'A' && character <= 'F');
    }

    private static bool Length(float[]? values, int expected) =>
        values != null && values.Length == expected;

    private static bool Finite(IEnumerable<float> values) =>
        values.All(Finite);

    private static bool Finite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
