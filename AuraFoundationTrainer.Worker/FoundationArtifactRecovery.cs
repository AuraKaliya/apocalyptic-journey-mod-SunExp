using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

public static class FoundationArtifactRecovery
{
    public static int Run(string resultDirectory)
    {
        try
        {
            var root = Path.GetFullPath(resultDirectory ?? "");
            var jobPath = Path.Combine(root, "foundation-worker-job.json");
            var resultPath = Path.Combine(root, "foundation-worker-result.json");
            var bundleDirectory = Path.Combine(
                root,
                FoundationArtifactBundleWriter.DirectoryName);
            var candidateDirectory = Path.Combine(bundleDirectory, "model");
            var candidatePath = Path.Combine(
                candidateDirectory,
                "candidate-model-v1.json");
            if (!File.Exists(jobPath)
                || !File.Exists(resultPath)
                || !File.Exists(candidatePath))
            {
                throw new FileNotFoundException(
                    "历史恢复需要 foundation-worker-job.json、"
                    + "foundation-worker-result.json 与 candidate-model-v1.json");
            }

            var job = Deserialize<CombatFoundationWorkerJob>(jobPath)
                      ?? throw new InvalidDataException("历史 Worker 任务无效");
            var sourceResult = Deserialize<CombatFoundationWorkerResult>(
                                   resultPath)
                               ?? throw new InvalidDataException(
                                   "历史 Worker 结果无效");
            var candidate = Deserialize<FoundationCandidateModelManifest>(
                                candidatePath)
                            ?? throw new InvalidDataException(
                                "历史候选模型清单无效");
            if (!CombatFoundationWorkerProtocol.TryValidateJob(
                    job,
                    out var diagnostic))
            {
                throw new InvalidDataException(
                    "历史 Worker 任务复验失败：" + diagnostic);
            }
            if (!CombatPolicyValueArtifactProtocol.TryLoad(
                    candidateDirectory,
                    candidate.Artifact,
                    out var runtimeModel,
                    out diagnostic))
            {
                throw new InvalidDataException(
                    "历史候选模型权重复验失败：" + diagnostic);
            }
            var training = sourceResult.Training
                           ?? throw new InvalidDataException(
                               "历史 Worker 结果缺少训练证据");
            var model = CombatPolicyValueArtifactProtocol.ToTrainingDefinition(
                runtimeModel);
            if (!string.Equals(
                    candidate.ModelId,
                    model.ModelId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    training.EvaluatedModelId,
                    model.ModelId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    training.ValidationModelId,
                    model.ModelId,
                    StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(training.CapabilityProbeModelId)
                   && !string.Equals(
                       training.CapabilityProbeModelId,
                       model.ModelId,
                       StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "历史候选模型与竞技场、能力探针或最终验证的模型 ID 不一致");
            }

            var expectedNormal = training.Validation.NormalPlannedCampaigns > 0
                ? training.Validation.NormalPlannedCampaigns
                : Math.Max(1, job.Request.NormalValidationCampaigns);
            var expectedAdvanced = training.Validation.AdvancedPlannedCampaigns > 0
                ? training.Validation.AdvancedPlannedCampaigns
                : Math.Max(1, job.Request.AdvancedValidationCampaigns);
            training.RuntimeSafetyPassed =
                CombatCampaignFoundationTrainer.RuntimeSafetyPassed(
                    training.Validation,
                    training.TerminalConsistencyViolations,
                    training.FeatureLeakageViolations,
                    training.SemanticGatePassed,
                    expectedNormal,
                    expectedAdvanced);
            training.RawIsolationPassed =
                CombatCampaignFoundationTrainer.RawIsolationPassed(
                    training.Validation,
                    training.RuntimeSafetyPassed);
            training.SameModelEvidenceBound =
                CombatCampaignFoundationTrainer.SameModelEvidenceBound(
                    model.ModelId,
                    training.EvaluatedModelId,
                    training.ValidationModelId,
                    training.CapabilityProbeModelId);
            if (!sourceResult.Success
                || !training.Success
                || !training.RuntimeSafetyPassed
                || !training.SameModelEvidenceBound)
            {
                throw new InvalidDataException(
                    "历史结果未达到实验底模所需的完整运行时安全与同模型证据门禁");
            }

            training.AcceptancePassed = false;
            training.ExperimentalEligibilityPassed = true;
            training.DeploymentTier = CombatFoundationDeploymentTier.Experimental;
            training.AcceptanceKind =
                CombatFoundationPromotionProtocol.ExperimentalRuntimeTest;
            training.DeploymentTierReason = ExperimentalReason(training);
            training.LatestTrainingModel = model;
            if (training.Iterations.Any(item =>
                    item.AbsoluteQualificationGatePassed
                    && string.Equals(
                        item.CandidateModelId,
                        model.ModelId,
                        StringComparison.Ordinal)))
            {
                training.AbsoluteQualifiedBestModel = model;
            }

            var recoveredResult = sourceResult;
            recoveredResult.Success = true;
            recoveredResult.CompletionKind =
                "training-experimental-recovered";
            recoveredResult.Training = training;
            recoveredResult.ArtifactBundleDirectory = bundleDirectory;
            var sourceResultHash = HashFile(resultPath);
            var sourceCandidateHash = HashFile(candidatePath);
            var workerPath = Environment.ProcessPath;
            var workerHash = !string.IsNullOrWhiteSpace(workerPath)
                             && File.Exists(workerPath)
                ? HashFile(workerPath)
                : new string('0', 64);
            var package = CombatFoundationModelPackageProtocol.Create(
                job,
                recoveredResult,
                workerHash);
            package.RecoveredFromCandidateArtifact = true;
            package.RecoverySourceResultSha256 = sourceResultHash;
            package.RecoverySourceCandidateSha256 = sourceCandidateHash;

            var deploymentDirectory = Path.Combine(
                bundleDirectory,
                "deployment");
            Directory.CreateDirectory(deploymentDirectory);
            var packagePath = Path.Combine(
                deploymentDirectory,
                CombatFoundationModelPackageProtocol.FileName);
            var weightsPath = Path.Combine(
                deploymentDirectory,
                CombatFoundationModelPackageProtocol.WeightsFileName);
            package.ModelArtifact = CombatPolicyValueArtifactProtocol.Write(
                weightsPath,
                model);
            package.Model = null;
            CombatFoundationCheckpointStorage.WriteAtomicText(
                packagePath,
                JsonConvert.SerializeObject(package, Formatting.None),
                retainBackup: false);

            var reloaded = Deserialize<CombatFoundationModelPackage>(packagePath);
            if (!CombatFoundationModelPackageProtocol.TryValidate(
                    reloaded,
                    out diagnostic)
                || !CombatPolicyValueArtifactProtocol.TryLoad(
                    deploymentDirectory,
                    reloaded?.ModelArtifact,
                    out var reloadedModel,
                    out diagnostic)
                || !string.Equals(
                    reloadedModel.ModelId,
                    model.ModelId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "历史恢复底模发布后复验失败：" + diagnostic);
            }

            var warnings = new List<string>
            {
                "该包由历史候选模型与原 Worker 结果重新复验生成，未重新执行训练。"
            };
            var databasePath = Path.Combine(
                bundleDirectory,
                FoundationArtifactBundleWriter.DatabaseFileName);
            if (!File.Exists(databasePath))
            {
                warnings.Add(
                    "原终端 SQLite 归档未完成，历史恢复无法重建逐回合数据库；"
                    + "候选模型、能力报告与验证摘要仍可核验。");
            }
            var manifestPath = FoundationArtifactBundleWriter
                .WriteRecoveryManifest(
                    job,
                    training,
                    recoveredResult.CompletionKind,
                    warnings);
            FoundationArtifactBundleWriter.AttachDeploymentPackage(
                bundleDirectory,
                packagePath,
                weightsPath);

            Console.WriteLine(JsonConvert.SerializeObject(new
            {
                Success = true,
                ModelId = model.ModelId,
                DeploymentTier = package.DeploymentTier,
                QualityCertification = package.QualityCertification,
                CapabilityStatus = package.CapabilityStatus,
                RuntimeSafetyPassed = training.RuntimeSafetyPassed,
                RawIsolationPassed = training.RawIsolationPassed,
                FormalIsolationPassed = package.Acceptance
                    ?.FormalIsolationPassed == true,
                PackagePath = packagePath,
                WeightsPath = weightsPath,
                ManifestPath = manifestPath,
                Warnings = warnings
            }, Formatting.Indented));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "Historical foundation artifact recovery failed: " + ex);
            return 1;
        }
    }

    private static string ExperimentalReason(
        CombatCampaignFoundationTrainingResult training)
    {
        var findings = new List<string>();
        if (!training.Validation.Passed)
        {
            findings.Add("未通过正式 Wilson 置信下界");
        }
        if (!training.RawIsolationPassed)
        {
            findings.Add("原始胜率未达到配置目标");
        }
        if (string.Equals(
                training.CapabilityProbe.BaselineGateVerdict,
                "fail",
                StringComparison.Ordinal))
        {
            findings.Add("能力探针检测到相对基线回退");
        }
        else if (!training.CapabilityProbe.PassedBaselineGate)
        {
            findings.Add("能力探针尚无结论性增益证据");
        }
        return "历史候选模型的技术兼容、哈希、同模型证据与运行时安全复验通过；"
               + string.Join("；", findings)
               + "，仅供实机配置测试与问题收集";
    }

    private static T? Deserialize<T>(string path)
    {
        return JsonConvert.DeserializeObject<T>(
            File.ReadAllText(path, new UTF8Encoding(false, true)),
            new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
