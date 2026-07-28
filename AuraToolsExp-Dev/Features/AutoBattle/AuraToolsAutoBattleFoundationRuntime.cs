using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum AutoBattleFoundationStage
{
    Idle,
    Queued,
    Training,
    Writing,
    Completed,
    Cancelling,
    Cancelled,
    Failed
}

internal sealed class AutoBattleFoundationStatus
{
    public AutoBattleFoundationStage Stage { get; set; }

    public string Message { get; set; } = "尚未训练底模";

    public string Phase { get; set; } = "";

    public int CompletedCampaigns { get; set; }

    public int RequestedCampaigns { get; set; }

    public double NormalWinRate { get; set; }

    public double AdvancedWinRate { get; set; }

    public bool AcceptancePassed { get; set; }

    public string ModelId { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public int WorkerCount { get; set; }

    public int ActiveWorkerCount { get; set; }

    public int PeakWorkerCount { get; set; }

    public int ObservedWorkerThreads { get; set; }

    public int CompletedBattles { get; set; }

    public int MaximumCompletedBattleDepth { get; set; }

    public int MaximumActiveBattleDepth { get; set; }

    public int Depth1To5Campaigns { get; set; }

    public int Depth6To10Campaigns { get; set; }

    public int Depth11To20Campaigns { get; set; }

    public int Depth21To30Campaigns { get; set; }

    public int Depth31To37Campaigns { get; set; }

    public double ProjectedBattleDepth { get; set; }

    public long SearchSimulations { get; set; }

    public int SearchEarlyStops { get; set; }

    public double SearchSimulationsPerSecond { get; set; }

    public double CampaignsPerSecond { get; set; }

    public double BattlesPerSecond { get; set; }

    public double ElapsedSeconds { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public int ModelEpoch { get; set; }

    public int ModelTotalEpochs { get; set; }

    public int ModelCompletedFrames { get; set; }

    public int ModelTotalFrames { get; set; }

    public double ModelValidationLoss { get; set; }

    public int ModelBestEpoch { get; set; }

    public int ModelStaleEpochs { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public string EarlyStopReason { get; set; } = "";

    public string ProgressDiagnostic { get; set; } = "";

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Busy => Stage is AutoBattleFoundationStage.Queued
        or AutoBattleFoundationStage.Training
        or AutoBattleFoundationStage.Writing
        or AutoBattleFoundationStage.Cancelling;

    public AutoBattleFoundationStatus Clone()
    {
        return (AutoBattleFoundationStatus)MemberwiseClone();
    }
}

internal static class AuraToolsAutoBattleFoundationRuntime
{
    private const string WorkKey = "AutoBattle.FoundationTraining";
    private const string ReadinessWorkKey = "AutoBattle.FoundationPackageValidation";
    private static readonly object Gate = new();
    private static AutoBattleFoundationStatus status = new();
    private static CancellationTokenSource? cancellation;
    private static bool readinessQueued;
    private static bool readinessReady;
    private static string readinessMessage = "权威知识包尚未校验";
    private static DateTime readinessRetryAfterUtc = DateTime.MinValue;

    public static void BeginReadinessRefresh()
    {
        lock (Gate)
        {
            if (readinessQueued
                || readinessReady
                || DateTime.UtcNow < readinessRetryAfterUtc)
            {
                return;
            }
            readinessQueued = true;
            readinessMessage = "正在后台读取并校验预编译权威知识包";
        }
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<FoundationReadinessResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = ReadinessWorkKey,
                Source = "AutoBattle.FoundationPackageValidation",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => ResolveReadiness(),
                ApplyOnMainThread = result =>
                {
                    lock (Gate)
                    {
                        readinessQueued = false;
                        readinessReady = result.Ready;
                        readinessMessage = result.Message;
                        readinessRetryAfterUtc = result.Ready
                            ? DateTime.MaxValue
                            : DateTime.UtcNow.AddSeconds(10d);
                    }
                    AuraToolsLog.Info(
                        "[AutoBattle][Foundation] " + result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    lock (Gate)
                    {
                        readinessQueued = false;
                        readinessReady = false;
                        readinessMessage = "权威知识包校验失败：" + ex.Message;
                        readinessRetryAfterUtc = DateTime.UtcNow.AddSeconds(10d);
                    }
                    AuraToolsLog.Warn(
                        "[AutoBattle][Foundation] readiness: " + ex);
                }
            });
        if (!queued)
        {
            lock (Gate)
            {
                readinessQueued = false;
                readinessReady = false;
                readinessMessage = "权威知识包校验任务未能提交";
                readinessRetryAfterUtc = DateTime.UtcNow.AddSeconds(10d);
            }
        }
    }

    public static bool Queue(
        AutoBattleSettings settings,
        out string message)
    {
        if (settings == null)
        {
            message = "自动战斗设置为空";
            return false;
        }
        settings.Normalize();
        if (AuraToolsAutoBattleModelRuntime.AnyTrainingBusy()
            || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy)
        {
            message = "候选训练、模拟评估或导入任务仍在运行";
            return false;
        }
        if (AuraToolsFoundationWorkerRuntime.ExternalTrainingActive())
        {
            message = "独立训练控制台已有底模任务正在运行";
            return false;
        }
        if (!CheckReadiness(out var currentReadinessMessage))
        {
            message = currentReadinessMessage;
            return false;
        }
        var liveFoundation = settings.FoundationTraining;
        if (liveFoundation.RandomizeRunSeed || liveFoundation.RunSeed == 0UL)
        {
            liveFoundation.RunSeed = GenerateRunSeed();
            AuraToolsConfigService.SaveMatchExperience();
        }
        var snapshot = AuraSharedJson.Deserialize<AutoBattleSettings>(
                           AuraSharedJson.Serialize(settings))
                       ?? new AutoBattleSettings();
        snapshot.Normalize();
        var requested = TotalCampaigns(snapshot.FoundationTraining);
        lock (Gate)
        {
            if (status.Busy)
            {
                message = "底模训练已经在运行";
                return false;
            }
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            status = new AutoBattleFoundationStatus
            {
                Stage = AutoBattleFoundationStage.Queued,
                Message = "底模训练已排队",
                RequestedCampaigns = requested,
                WorkerCount = snapshot.FoundationTraining.Parallelism
            };
        }

        var ownedCancellation = cancellation;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<FoundationWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = WorkKey,
                Source = "AutoBattle.FoundationWorldSimulationTraining",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = schedulerToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        schedulerToken,
                        ownedCancellation.Token);
                    return Run(snapshot, linked.Token);
                },
                ApplyOnMainThread = result =>
                {
                    if (result.AcceptancePassed
                        && !string.IsNullOrWhiteSpace(result.ModelId))
                    {
                        var current = AuraToolsConfigService.MatchExperience.AutoBattle;
                        current.SelectedModelId = result.ModelId;
                        current.TrainedModelMode = "off";
                        current.CaptureTrainingSamples = false;
                        current.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                        AuraToolsAutoBattleRuntime.ReloadModels();
                    }
                    SetStatus(
                        result.Cancelled
                            ? AutoBattleFoundationStage.Cancelled
                            : result.Success
                                ? AutoBattleFoundationStage.Completed
                                : AutoBattleFoundationStage.Failed,
                        result.Message,
                        result.CompletedCampaigns,
                        result.RequestedCampaigns,
                        result.NormalWinRate,
                        result.AdvancedWinRate,
                        result.AcceptancePassed,
                        result.ModelId,
                        result.ResultDirectory,
                        result.WorkerCount,
                        result.CampaignsPerSecond,
                        result.BattlesPerSecond,
                        result.ElapsedSeconds,
                        0d,
                        result.EarlyStopReason,
                        result.ActiveWorkerCount,
                        result.PeakWorkerCount,
                        result.ObservedWorkerThreads,
                        result.CompletedBattles,
                        result.Gen0Collections,
                        result.Gen1Collections,
                        result.Gen2Collections,
                        result.MaximumCompletedBattleDepth,
                        result.MaximumActiveBattleDepth,
                        result.Depth1To5Campaigns,
                        result.Depth6To10Campaigns,
                        result.Depth11To20Campaigns,
                        result.Depth21To30Campaigns,
                        result.Depth31To37Campaigns,
                        result.ProjectedBattleDepth,
                        result.SearchSimulations,
                        result.SearchEarlyStops,
                        result.SearchSimulationsPerSecond);
                    var effective =
                        AuraToolsConfigService.MatchExperience.AutoBattle;
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        effective.Profile,
                        effective.SelectedModelId,
                        force: true);
                    (result.Success ? (Action<string>)AuraToolsLog.Info : AuraToolsLog.Warn)(
                        "[AutoBattle][Foundation] " + result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    SetStatus(
                        AutoBattleFoundationStage.Failed,
                        "底模训练失败：" + ex.Message);
                    AuraToolsLog.Warn("[AutoBattle][Foundation] " + ex);
                }
            });
        if (!queued)
        {
            SetStatus(AutoBattleFoundationStage.Failed, "底模训练任务未能提交");
            message = "底模训练任务未能提交";
            return false;
        }
        message = "底模训练已提交";
        return true;
    }

    public static void Cancel()
    {
        lock (Gate)
        {
            cancellation?.Cancel();
            if (status.Busy)
            {
                status.Stage = AutoBattleFoundationStage.Cancelling;
                status.Message = "正在取消底模训练";
                status.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    public static AutoBattleFoundationStatus GetStatus()
    {
        lock (Gate)
        {
            return status.Clone();
        }
    }

    public static bool CheckReadiness(out string message)
    {
        lock (Gate)
        {
            if (readinessReady
                && AuraToolsAutoBattleSimulationRuntime.TryGetCachedFoundationPackage(
                    out _,
                    out _))
            {
                message = readinessMessage;
                return true;
            }
            message = readinessMessage;
        }
        BeginReadinessRefresh();
        return false;
    }

    public static void ResetAfterDataClear()
    {
        lock (Gate)
        {
            cancellation?.Dispose();
            cancellation = null;
            status = new AutoBattleFoundationStatus();
            readinessQueued = false;
            readinessReady = false;
            readinessMessage = "权威知识包尚未校验";
            readinessRetryAfterUtc = DateTime.MinValue;
        }
        AuraToolsAutoBattleSimulationRuntime.InvalidateFoundationPackageCache();
        BeginReadinessRefresh();
    }

    public static void OpenResultDirectory()
    {
        var current = GetStatus();
        var path = Directory.Exists(current.ResultDirectory)
            ? current.ResultDirectory
            : AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory;
        Directory.CreateDirectory(path);
        FileResourceUtil.OpenDirectory(path);
    }

    private static FoundationWorkResult Run(
        AutoBattleSettings settings,
        CancellationToken cancellationToken)
    {
        if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out var sourceCampaign,
                out var ruleset,
                out var resolveMessage))
        {
            return FoundationWorkResult.Failed(
                resolveMessage,
                TotalCampaigns(settings.FoundationTraining));
        }
        var trainingCampaign = CloneCampaign(sourceCampaign);
        trainingCampaign.TraceLevel = CombatSimulationTraceLevel.Summary;
        trainingCampaign.RequireAuthoritativeRules = true;
        trainingCampaign.RetainBlockBetweenTurns = true;
        var validationCampaign = CloneCampaign(sourceCampaign);
        validationCampaign.TraceLevel = CombatSimulationTraceLevel.Full;
        validationCampaign.FullTraceFinalEncounterOnly = true;
        validationCampaign.RequireAuthoritativeRules = true;
        validationCampaign.RetainBlockBetweenTurns = true;
        var foundation = settings.FoundationTraining;
        foundation.Normalize();
        SetStatus(
            AutoBattleFoundationStage.Training,
            "正在校验构建期权威程序包",
            requested: TotalCampaigns(foundation),
            workerCount: foundation.Parallelism);
        var packageValidation = AuraToolsNativeProgramPackageAudit.Validate(
            sourceCampaign,
            ruleset);
        if (!packageValidation.Success)
        {
            return FoundationWorkResult.Failed(
                "权威预编译程序包校验失败："
                + string.Join("；", packageValidation.Errors.Take(5)),
                TotalCampaigns(foundation));
        }
        var decisionProfile =
            AuraToolsAutoBattleSimulationRuntime.BuildDecisionProfile(settings);
        decisionProfile.SearchBudgetMode = "dynamic";
        decisionProfile.SearchQuality = "deep";
        decisionProfile.SearchMinimumSimulations = 64;
        decisionProfile.SearchStabilityWindow = 32;
        decisionProfile.SearchStableChecks = 2;
        var successArchiveDirectory = SuccessArchiveDirectory();
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                    + "-foundation";
        var resultDirectory = Path.Combine(
            AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
            runId);
        Directory.CreateDirectory(resultDirectory);
        var preparedJob = CombatFoundationWorkerJobFactory.Create(
            new CombatFoundationWorkerJobBuildRequest
            {
                JobId = runId,
                ResultDirectory = resultDirectory,
                SuccessArchiveDirectory = successArchiveDirectory,
                CheckpointPath = Path.Combine(
                    AuraToolsAutoBattleSimulationRuntime
                        .ResultsRootDirectory,
                    CombatFoundationWorkerProtocol.CheckpointFileName),
                CheckpointEpisodesPath = Path.Combine(
                    AuraToolsAutoBattleSimulationRuntime
                        .ResultsRootDirectory,
                    CombatFoundationWorkerProtocol
                        .CheckpointEpisodesFileName),
                ExpectedRulesetHash = ruleset.RulesetHash,
                NativeProgramPackageHash =
                    CurrentRuntimePackageHash(),
                Parameters = ToSharedParameters(
                    foundation,
                    settings.Training.MinimumEpisodes,
                    decisionProfile.Id),
                Profile = decisionProfile,
                TrainingCampaign = trainingCampaign,
                ValidationCampaign = validationCampaign,
                Ruleset = new CombatRulesetDocument
                {
                    Version = ruleset.Version,
                    Cards = ruleset.SnapshotCards().ToList(),
                    Enemies = ruleset.SnapshotEnemies().ToList(),
                    Statuses = ruleset.SnapshotStatuses().ToList()
                }
            });
        var request = preparedJob.Request;
        var requested = TotalCampaigns(foundation);
        var completed = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastTelemetryLogMilliseconds = -10_000L;
        var lastTelemetryLogStage = "";
        var telemetryLogGate = new object();
        request.Progress = (current, total, progressMessage) =>
        {
            UpdateMaximum(ref completed, current);
            var elapsedSeconds = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
            var rate = completed / elapsedSeconds;
            var remainingSeconds = rate <= 0d
                ? 0d
                : Math.Max(0d, total - completed) / rate;
            UpdateTrainingProgress(
                progressMessage,
                completed,
                total,
                foundation.Parallelism,
                rate,
                elapsedSeconds,
                remainingSeconds);
        };
        request.Telemetry = snapshot =>
        {
            UpdateMaximum(ref completed, snapshot.CompletedCampaigns);
            UpdateTrainingTelemetry(snapshot);
            var elapsedMilliseconds = (long)(snapshot.ElapsedSeconds * 1000d);
            lock (telemetryLogGate)
            {
                var stageChanged = !string.Equals(
                    lastTelemetryLogStage,
                    snapshot.Stage,
                    StringComparison.Ordinal);
                if (!stageChanged
                    && elapsedMilliseconds
                       - lastTelemetryLogMilliseconds < 10_000L)
                {
                    return;
                }
                lastTelemetryLogMilliseconds = elapsedMilliseconds;
                lastTelemetryLogStage = snapshot.Stage ?? "";
            }
            AuraToolsLog.Info(
                "[AutoBattle][Foundation][Telemetry] stage="
                + snapshot.Stage
                + ", phase="
                + snapshot.Phase
                + ", modelEpoch="
                + snapshot.ModelEpoch
                + "/"
                + snapshot.ModelTotalEpochs
                + ", modelFrames="
                + snapshot.ModelCompletedFrames
                + "/"
                + snapshot.ModelTotalFrames
                + ", modelLoss="
                + snapshot.ModelValidationLoss.ToString("F5")
                + ", active="
                + snapshot.ActiveCampaigns
                + ", peak="
                + snapshot.PeakConcurrentCampaigns
                + ", configured="
                + foundation.Parallelism
                + ", effective="
                + snapshot.EffectiveParallelism
                + ", observedThreads="
                + snapshot.ObservedWorkerThreads
                + ", campaigns="
                + snapshot.CompletedCampaigns
                + "/"
                + snapshot.RequestedCampaigns
                + ", battles="
                + snapshot.CompletedBattles
                + ", campaignRate="
                + snapshot.CampaignsPerSecond.ToString("F3")
                + "/s, battleRate="
                + snapshot.BattlesPerSecond.ToString("F2")
                + "/s, depthMax="
                + snapshot.MaximumCompletedBattleDepth
                + ", activeDepthMax="
                + snapshot.MaximumActiveBattleDepth
                + ", depthBuckets="
                + snapshot.Depth1To5Campaigns
                + "/"
                + snapshot.Depth6To10Campaigns
                + "/"
                + snapshot.Depth11To20Campaigns
                + "/"
                + snapshot.Depth21To30Campaigns
                + "/"
                + snapshot.Depth31To37Campaigns
                + ", search="
                + snapshot.SearchSimulations
                + " sims ("
                + snapshot.SearchSimulationsPerSecond.ToString("F0")
                + "/s), earlyStops="
                + snapshot.SearchEarlyStops
                + ", workEta="
                + snapshot.EstimatedRemainingSeconds.ToString("F0")
                + "s, gc="
                + snapshot.Gen0Collections
                + "/"
                + snapshot.Gen1Collections
                + "/"
                + snapshot.Gen2Collections);
        };
        AuraToolsLog.Info(
            "[AutoBattle][Foundation][Telemetry] queued configuredWorkers="
            + foundation.Parallelism
            + ", processorCount="
            + Environment.ProcessorCount
            + ", requestedCampaigns="
            + requested
            + ", runSeed="
            + foundation.RunSeed
            + ", randomizeRunSeed="
            + foundation.RandomizeRunSeed);
        var incrementallyArchivedCases =
            new HashSet<string>(StringComparer.Ordinal);
        var incrementalArchiveErrors = new List<string>();
        if (foundation.EnableSuccessCaseArchive
            && string.Equals(
                foundation.ExecutionMode,
                "inprocess",
                StringComparison.Ordinal))
        {
            request.ObservationRecorded = observation =>
            {
                try
                {
                    PersistObservation(
                        successArchiveDirectory,
                        observation);
                }
                catch (Exception ex)
                {
                    incrementalArchiveErrors.Add(ex.ToString());
                }
            };
            request.SuccessCaseRecorded = successCase =>
            {
                try
                {
                    if (PersistSuccessCase(
                            successArchiveDirectory,
                            successCase))
                    {
                        incrementallyArchivedCases.Add(
                            successCase.Observation.CaseId);
                    }
                }
                catch (Exception ex)
                {
                    incrementalArchiveErrors.Add(ex.ToString());
                }
            };
        }

        CombatCampaignFoundationTrainingResult trained;
        try
        {
            if (string.Equals(
                    foundation.ExecutionMode,
                    "external",
                    StringComparison.Ordinal))
            {
                if (!AuraToolsFoundationWorkerRuntime.IsAvailable(
                        out var unavailable))
                {
                    throw new InvalidOperationException(unavailable);
                }
                var telemetryCallback = request.Telemetry
                                        ?? (_ => { });
                request.Progress = null;
                request.Telemetry = null;
                request.Checkpoint = null;
                var workerResult = AuraToolsFoundationWorkerRuntime.Run(
                    preparedJob,
                    telemetryCallback,
                    UpdateTrainingDiagnostic,
                    cancellationToken);
                trained = workerResult.Training
                          ?? throw new InvalidOperationException(
                              "独立训练器没有返回训练结果");
            }
            else
            {
                trained = new CombatCampaignFoundationTrainer(
                    new CombatCampaignRunner(
                        new CombatSimulationEngine(
                            new AuraToolsNativeRewardExtensionFactory()))).Run(
                    request,
                    ruleset,
                    cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            var currentStatus = GetStatus();
            var resumeCheckpoint = Path.Combine(
                AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
                CombatFoundationWorkerProtocol.CheckpointFileName);
            var resumable = File.Exists(resumeCheckpoint);
            return new FoundationWorkResult
            {
                Cancelled = true,
                Message = resumable
                    ? "底模训练已取消；断点已保留，下次将自动继续"
                    : "底模训练已取消",
                CompletedCampaigns = completed,
                RequestedCampaigns = requested,
                ResultDirectory = resultDirectory,
                WorkerCount = currentStatus.WorkerCount,
                ActiveWorkerCount = currentStatus.ActiveWorkerCount,
                PeakWorkerCount = currentStatus.PeakWorkerCount,
                ObservedWorkerThreads = currentStatus.ObservedWorkerThreads,
                CompletedBattles = currentStatus.CompletedBattles,
                CampaignsPerSecond = currentStatus.CampaignsPerSecond,
                BattlesPerSecond = currentStatus.BattlesPerSecond,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                Gen0Collections = currentStatus.Gen0Collections,
                Gen1Collections = currentStatus.Gen1Collections,
                Gen2Collections = currentStatus.Gen2Collections,
                MaximumCompletedBattleDepth =
                    currentStatus.MaximumCompletedBattleDepth,
                MaximumActiveBattleDepth =
                    currentStatus.MaximumActiveBattleDepth,
                Depth1To5Campaigns = currentStatus.Depth1To5Campaigns,
                Depth6To10Campaigns = currentStatus.Depth6To10Campaigns,
                Depth11To20Campaigns = currentStatus.Depth11To20Campaigns,
                Depth21To30Campaigns = currentStatus.Depth21To30Campaigns,
                Depth31To37Campaigns = currentStatus.Depth31To37Campaigns,
                ProjectedBattleDepth = currentStatus.ProjectedBattleDepth,
                SearchSimulations = currentStatus.SearchSimulations,
                SearchEarlyStops = currentStatus.SearchEarlyStops,
                SearchSimulationsPerSecond =
                    currentStatus.SearchSimulationsPerSecond
            };
        }

        UpdateMaximum(ref completed, trained.CompletedCampaigns);
        var finalElapsedSeconds = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
        var finalCampaignRate = completed / finalElapsedSeconds;
        SetStatus(
            AutoBattleFoundationStage.Writing,
            "正在写入底模训练报告",
            completed,
            requested,
            workerCount: trained.EffectiveParallelism,
            campaignsPerSecond: finalCampaignRate,
            battlesPerSecond: trained.CompletedBattles / finalElapsedSeconds,
            elapsedSeconds: finalElapsedSeconds,
            activeWorkerCount: 0,
            peakWorkerCount: trained.PeakConcurrentCampaigns,
            observedWorkerThreads: trained.ObservedWorkerThreads,
            completedBattles: trained.CompletedBattles,
            gen0Collections: trained.Gen0Collections,
            gen1Collections: trained.Gen1Collections,
            gen2Collections: trained.Gen2Collections,
            maximumCompletedBattleDepth: trained.MaximumCompletedBattleDepth,
            depth1To5Campaigns: trained.Depth1To5Campaigns,
            depth6To10Campaigns: trained.Depth6To10Campaigns,
            depth11To20Campaigns: trained.Depth11To20Campaigns,
            depth21To30Campaigns: trained.Depth21To30Campaigns,
            depth31To37Campaigns: trained.Depth31To37Campaigns,
            projectedBattleDepth: trained.ProjectedBattleDepth,
            searchSimulations: trained.SearchSimulations,
            searchEarlyStops: trained.SearchEarlyStops,
            searchSimulationsPerSecond: trained.SearchSimulations
                                        / finalElapsedSeconds);
        if (foundation.EnableSuccessCaseArchive
            && (trained.CampaignObservations.Count > 0
            || trained.SuccessCases.Count > 0)
           )
        {
            try
            {
                PersistSuccessCases(
                    resultDirectory,
                    successArchiveDirectory,
                    trained,
                    incrementallyArchivedCases,
                    incrementalArchiveErrors);
            }
            catch (Exception ex)
            {
                trained.CaseAnalysis = CombatFoundationCaseLearning.Analyze(
                    trained.CampaignObservations);
                trained.SuccessArchiveError = ex.ToString();
                AuraToolsLog.Info(
                    "[AutoBattle][Foundation][SuccessArchive] write failed "
                    + "without invalidating training: "
                    + ex);
                trained.CampaignObservations.Clear();
                trained.SuccessCases.Clear();
            }
        }
        foreach (var validationRun in trained.ValidationRuns)
        {
            AuraToolsAutoBattleSimulationRuntime.PruneCampaignTrace(validationRun);
        }
        if (trained.GeneratedReplayEpisodes == 0
            && trained.Replay.Count > 0)
        {
            trained.GeneratedReplayEpisodes = trained.Replay.Count;
            trained.PersistedReplayEpisodes = trained.Success
                ? trained.Replay.Count
                : 0;
        }
        WriteReports(
            resultDirectory,
            sourceCampaign,
            ruleset,
            trained,
            foundation,
            decisionProfile);
        var modelId = "";
        if (trained.Success
            && trained.AcceptancePassed
            && trained.Champion != null)
        {
            modelId = AuraToolsAutoBattleModelRuntime.SaveFoundationModel(
                decisionProfile.Id,
                trained.Champion,
                trained.Validation,
                resultDirectory);
        }
        return new FoundationWorkResult
        {
            Success = trained.Success,
            Message = trained.Message
                      + (trained.AcceptancePassed
                          ? "；已保存为 career_1 底模，默认保持关闭"
                          : "；未保存为底模，可打开报告检查失败层级与终局流程"),
            CompletedCampaigns = completed,
            RequestedCampaigns = requested,
            NormalWinRate = trained.Validation.NormalWinRate,
            AdvancedWinRate = trained.Validation.AdvancedWinRate,
            AcceptancePassed = trained.AcceptancePassed,
            ModelId = modelId,
            ResultDirectory = resultDirectory,
            WorkerCount = trained.EffectiveParallelism,
            ActiveWorkerCount = 0,
            PeakWorkerCount = trained.PeakConcurrentCampaigns,
            ObservedWorkerThreads = trained.ObservedWorkerThreads,
            CompletedBattles = trained.CompletedBattles,
            CampaignsPerSecond = finalCampaignRate,
            BattlesPerSecond = trained.CompletedBattles / finalElapsedSeconds,
            ElapsedSeconds = finalElapsedSeconds,
            EarlyStopReason = trained.EarlyStopReason,
            Gen0Collections = trained.Gen0Collections,
            Gen1Collections = trained.Gen1Collections,
            Gen2Collections = trained.Gen2Collections,
            MaximumCompletedBattleDepth = trained.MaximumCompletedBattleDepth,
            Depth1To5Campaigns = trained.Depth1To5Campaigns,
            Depth6To10Campaigns = trained.Depth6To10Campaigns,
            Depth11To20Campaigns = trained.Depth11To20Campaigns,
            Depth21To30Campaigns = trained.Depth21To30Campaigns,
            Depth31To37Campaigns = trained.Depth31To37Campaigns,
            ProjectedBattleDepth = trained.ProjectedBattleDepth,
            SearchSimulations = trained.SearchSimulations,
            SearchEarlyStops = trained.SearchEarlyStops,
            SearchSimulationsPerSecond = trained.SearchSimulations
                                         / finalElapsedSeconds
        };
    }

    private static string SuccessArchiveDirectory()
    {
        return Path.Combine(
            AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
            "foundation-success-cases");
    }

    private static void PersistObservation(
        string archiveRoot,
        CombatFoundationCampaignObservation observation)
    {
        var path = Path.Combine(
            archiveRoot,
            "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
            observation.CompatibilityKey,
            "observations",
            observation.CaseId + ".json");
        if (!File.Exists(path))
        {
            WriteText(path, AuraSharedJson.Serialize(observation));
        }
    }

    private static bool PersistSuccessCase(
        string archiveRoot,
        CombatFoundationSuccessCase successCase)
    {
        var observation = successCase.Observation;
        var compatibilityDirectory = Path.Combine(
            archiveRoot,
            "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
            observation.CompatibilityKey);
        var casePath = Path.Combine(
            compatibilityDirectory,
            "cases",
            observation.CaseId + ".json");
        var added = !File.Exists(casePath);
        if (added)
        {
            WriteText(casePath, AuraSharedJson.Serialize(successCase));
        }
        if (successCase.Episodes.Count > 0)
        {
            var expertCasePath = Path.Combine(
                compatibilityDirectory,
                "expert-cases",
                observation.CaseId + ".json");
            if (!File.Exists(expertCasePath))
            {
                WriteText(
                    expertCasePath,
                    AuraSharedJson.Serialize(successCase));
            }
        }
        return added;
    }

    private static void PersistSuccessCases(
        string resultDirectory,
        string archiveRoot,
        CombatCampaignFoundationTrainingResult result,
        ISet<string> incrementallyArchivedCases,
        IReadOnlyList<string> incrementalArchiveErrors)
    {
        Directory.CreateDirectory(archiveRoot);
        var currentObservations = result.CampaignObservations
            .Where(item => item != null)
            .GroupBy(item => item.CaseId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.CaseId, StringComparer.Ordinal)
            .ToList();
        var observationIndex = new StringBuilder();
        foreach (var observation in currentObservations)
        {
            var observationPath = Path.Combine(
                archiveRoot,
                "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
                observation.CompatibilityKey,
                "observations",
                observation.CaseId + ".json");
            if (!File.Exists(observationPath))
            {
                WriteText(
                    observationPath,
                    AuraSharedJson.Serialize(observation));
            }
            observationIndex.AppendLine(
                AuraSharedJson.SerializeCompact(observation));
        }
        WriteText(
            Path.Combine(
                resultDirectory,
                "foundation-case-observations-v1.jsonl"),
            observationIndex.ToString());
        var cumulativeObservations =
            new List<CombatFoundationCampaignObservation>();
        foreach (var compatibilityKey in currentObservations
                     .Select(item => item.CompatibilityKey)
                     .Distinct(StringComparer.Ordinal))
        {
            var observationDirectory = Path.Combine(
                archiveRoot,
                "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
                compatibilityKey,
                "observations");
            if (!Directory.Exists(observationDirectory))
            {
                continue;
            }
            foreach (var path in Directory.EnumerateFiles(
                         observationDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(item => item, StringComparer.Ordinal)
                     .Take(20_000))
            {
                try
                {
                    var observation = AuraSharedJson
                        .Deserialize<CombatFoundationCampaignObservation>(
                            File.ReadAllText(path));
                    if (observation != null)
                    {
                        cumulativeObservations.Add(observation);
                    }
                }
                catch (Exception ex)
                {
                    AuraToolsLog.Info(
                        "[AutoBattle][Foundation][SuccessArchive] ignored "
                        + Path.GetFileName(path)
                        + ": "
                        + ex.Message);
                }
            }
        }
        result.CaseAnalysis = CombatFoundationCaseLearning.Analyze(
            cumulativeObservations.Count == 0
                ? currentObservations
                : cumulativeObservations);
        var archived = 0;
        var duplicates = 0;
        var index = new StringBuilder();
        foreach (var successCase in result.SuccessCases
                     .Where(item =>
                         item?.Observation?.ArchiveEligible == true)
                     .GroupBy(
                         item => item.Observation.CaseId,
                         StringComparer.Ordinal)
                     .Select(group => group.First())
                     .OrderBy(
                         item => item.Observation.CompatibilityKey,
                         StringComparer.Ordinal)
                     .ThenBy(
                         item => item.Observation.CaseId,
                         StringComparer.Ordinal))
        {
            var observation = successCase.Observation;
            var casePath = Path.Combine(
                archiveRoot,
                "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
                observation.CompatibilityKey,
                "cases",
                observation.CaseId + ".json");
            if (File.Exists(casePath))
            {
                if (incrementallyArchivedCases.Contains(observation.CaseId))
                {
                    archived++;
                }
                else
                {
                    duplicates++;
                }
            }
            else
            {
                WriteText(casePath, AuraSharedJson.Serialize(successCase));
                archived++;
            }
            if (successCase.Episodes.Count > 0)
            {
                var expertCasePath = Path.Combine(
                    archiveRoot,
                    "v" + CombatFoundationCaseLearning.ArchiveSchemaVersion,
                    observation.CompatibilityKey,
                    "expert-cases",
                    observation.CaseId + ".json");
                if (!File.Exists(expertCasePath))
                {
                    WriteText(
                        expertCasePath,
                        AuraSharedJson.Serialize(successCase));
                }
            }
            index.AppendLine(AuraSharedJson.SerializeCompact(observation));
        }
        var indexPath = Path.Combine(
            resultDirectory,
            "foundation-success-case-index-v1.jsonl");
        WriteText(indexPath, index.ToString());
        WriteText(
            Path.Combine(
                resultDirectory,
                "foundation-success-analysis-v1.json"),
            AuraSharedJson.Serialize(result.CaseAnalysis));
        result.ArchivedSuccessCases = archived;
        result.DuplicateSuccessCases = duplicates;
        result.SuccessArchiveDirectory = archiveRoot;
        result.SuccessCaseIndexPath = indexPath;
        if (incrementalArchiveErrors.Count > 0)
        {
            result.SuccessArchiveError = string.Join(
                Environment.NewLine,
                incrementalArchiveErrors.Take(4));
        }
        result.CampaignObservations.Clear();
        result.SuccessCases.Clear();
    }

    private static void WriteReports(
        string resultDirectory,
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatCampaignFoundationTrainingResult result,
        AutoBattleFoundationTrainingSettings settings,
        CombatDecisionProfile profile)
    {
        var writeFullReplay = result.Success;
        var generatedReplayEpisodes = Math.Max(
            result.GeneratedReplayEpisodes,
            result.Replay.Count);
        var retainedReplayEpisodes = writeFullReplay
            ? Math.Max(
                result.PersistedReplayEpisodes,
                result.Replay.Count)
            : 0;
        var validationCardDecisions = result.ValidationRuns
            .SelectMany(run => run.Rewards)
            .SelectMany(reward => reward.Cards)
            .ToList();
        var validationDeckSizes = result.ValidationRuns
            .Select(run => run.FinalState.Deck.Count)
            .ToList();
        var generatedOnlyViolations = result.ValidationRuns
            .SelectMany(run => run.FinalState.Deck)
            .Count(CombatCampaignCardAcquisitionPolicy
                .IsGeneratedOnlyIdentifier);
        var completedValidationRuns = result.ValidationRuns
            .Where(run => run.FinalBossVictory && !run.Invalid)
            .ToList();
        var failedValidationRuns = result.ValidationRuns
            .Where(run => !run.FinalBossVictory || run.Invalid)
            .ToList();
        var cardSkipReasons = validationCardDecisions
            .Where(item => item.Skipped)
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.SkipReason)
                    ? "unspecified"
                    : item.SkipReason,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var layerDeckStatistics = result.ValidationRuns
            .SelectMany(run => run.Rewards)
            .GroupBy(reward => reward.BuildPlan.LayerNumber)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                layer = group.Key,
                decisions = group.Count(),
                averageDeckSize = group.Average(reward =>
                    reward.BuildPlan.SynergySources.TryGetValue(
                        "card",
                        out var cards)
                        ? cards
                        : 0),
                averageTargetMinimum = group.Average(reward =>
                    reward.BuildPlan.TargetDeckSizeMinimum),
                averageTargetMaximum = group.Average(reward =>
                    reward.BuildPlan.TargetDeckSizeMaximum)
            })
            .ToList();
        var failureClusters = failedValidationRuns
            .GroupBy(
                run => run.Battles.LastOrDefault()?.ScenarioId
                       ?? "no-battle",
                StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        WriteText(
            Path.Combine(resultDirectory, "foundation-training-report.json"),
            AuraSharedJson.Serialize(new
            {
                schemaVersion = 6,
                reportKind = "career_1-world-simulation-foundation-training",
                createdUtc = DateTime.UtcNow,
                roleId = campaign.Player.RoleId,
                campaignId = campaign.CampaignId,
                campaignVersion = campaign.CampaignVersion,
                rulesetVersion = ruleset.Version,
                rulesetHash = ruleset.RulesetHash,
                nativeProgramProtocol =
                    NativeRewardScriptGlobals.PrecompiledProgramProtocol,
                nativeProgramCount =
                    NativeRewardScriptGlobals.PrecompiledProgramCount,
                cardPoolScope = AuraToolsAutoBattleModelRuntime.CurrentCardPoolScope,
                decisionProfile = profile.Id,
                trainingSeeds = new
                {
                    result.RunSeed,
                    preflightSeedStart = result.TrainingSeedStart,
                    result.TrainingSeedStart,
                    result.ArenaSeedStart,
                    result.TuningSeedStart,
                    result.ValidationSeedStart,
                    result.ModelRandomSeed,
                    randomized = settings.RandomizeRunSeed
                },
                trainingProtocols = new
                {
                    terminalCredit =
                        CombatFoundationTerminalCreditProtocol.Version,
                    hardEncounterCounterfactual =
                        CombatFoundationCounterfactualProtocol.Version,
                    frameStratification =
                        CombatPolicyValueFrameStratificationProtocol.Version,
                    caseArchive =
                        CombatFoundationCaseArchiveProtocol.Version,
                    stagnation =
                        CombatFoundationStagnationProtocol.Version,
                    workerSchema =
                        CombatFoundationWorkerProtocol.SchemaVersion
                },
                settings.Iterations,
                settings.TrainingCampaignsPerIteration,
                settings.ArenaCampaignsPerDifficulty,
                settings.ArenaConfirmationCampaignsPerDifficulty,
                settings.NormalValidationCampaigns,
                settings.AdvancedValidationCampaigns,
                settings.PreflightCampaignsPerDifficulty,
                settings.Parallelism,
                settings.EarlyValidationStop,
                settings.EnableCurriculum,
                settings.EnableStratifiedReplay,
                settings.EnableHardSeedCurriculum,
                settings.EnableCounterfactualHardEncounters,
                settings.EnableSuccessCaseArchive,
                settings.EnableArenaRecovery,
                settings.ArenaInvalidRetryCount,
                settings.ArenaInvalidRateLimit,
                settings.EnableTuningArena,
                settings.TuningNormalCampaigns,
                settings.TuningAdvancedCampaigns,
                settings.MaximumConsecutiveRejectedIterations,
                settings.NormalAcceptanceRate,
                settings.AdvancedAcceptanceRate,
                settings.SuccessExpertReplayShare,
                settings.HardSeedReplayShare,
                settings.SelfPlayExplorationProbability,
                settings.SelfPlayExplorationTemperature,
                settings.EnableFrameStratification,
                settings.ModelMaximumFrameStratumWeight,
                result.Success,
                computationSucceeded = result.Success,
                result.AcceptancePassed,
                result.Message,
                result.RequestedCampaigns,
                result.CompletedCampaigns,
                result.InvalidTrainingCampaigns,
                result.DiscardedInvalidEpisodes,
                result.DiscardedCounterfactualEpisodes,
                result.TerminalConsistencyViolations,
                result.FeatureLeakageViolations,
                result.StoppedForStagnation,
                result.ConsecutiveRejectedIterations,
                result.IterationStopReason,
                result.Compatibility,
                result.HardSeedHistory,
                result.ArenaRetryAttempts,
                result.ArenaRecoveredCampaigns,
                result.ArenaIsolatedPairs,
                result.ArenaReplacementPairs,
                result.ArenaInvalidSignatures,
                result.TrainingFailureCounts,
                result.TrainingFailures,
                result.ArenaFailureCounts,
                result.ArenaFailures,
                result.EarlyStopReason,
                replayPersistence = new
                {
                    fullReplayWritten = writeFullReplay,
                    generatedEpisodes = generatedReplayEpisodes,
                    retainedEpisodes = retainedReplayEpisodes,
                    omittedEpisodes =
                        generatedReplayEpisodes - retainedReplayEpisodes,
                    reason = writeFullReplay
                        ? "bounded-prioritized-diverse-replay"
                        : "failed-training-keeps-only-reproduction-metadata"
                },
                successCaseLearning = new
                {
                    result.LoadedExpertReplayEpisodes,
                    result.ArchivedSuccessCases,
                    result.DuplicateSuccessCases,
                    result.SuccessArchiveDirectory,
                    result.SuccessCaseIndexPath,
                    result.SuccessArchiveError,
                    analysis = result.CaseAnalysis,
                    replayPolicy =
                        "bounded-success-demonstrations-plus-balanced-failures"
                },
                progression = new
                {
                    cardAcquisition = new
                    {
                        rewardPool = campaign.Rewards.Count(item =>
                            item.Kind == CombatCampaignRewardKind.Card
                            && item.CardAcquisition
                            == CombatCampaignCardAcquisition.RewardPool),
                        generatedOnly = campaign.Rewards.Count(item =>
                            item.Kind == CombatCampaignRewardKind.Card
                            && item.CardAcquisition
                            == CombatCampaignCardAcquisition.GeneratedOnly),
                        startingOnly = campaign.Rewards.Count(item =>
                            item.Kind == CombatCampaignRewardKind.Card
                            && item.CardAcquisition
                            == CombatCampaignCardAcquisition.StartingOnly),
                        curseOnly = campaign.Rewards.Count(item =>
                            item.Kind == CombatCampaignRewardKind.Card
                            && item.CardAcquisition
                            == CombatCampaignCardAcquisition.CurseOnly),
                        skillOnly = campaign.Rewards.Count(item =>
                            item.Kind == CombatCampaignRewardKind.Card
                            && item.CardAcquisition
                            == CombatCampaignCardAcquisition.SkillOnly)
                    },
                    campaign.CardOfferRounds,
                    campaign.CardRewardEncounterKinds,
                    campaign.TargetDeckSizeMinimum,
                    campaign.TargetDeckSizeMaximum,
                    campaign.DeckSizeAlertThreshold,
                    validationCampaigns = result.ValidationRuns.Count,
                    cardDecisions = validationCardDecisions.Count,
                    selectedCards = validationCardDecisions.Count(item =>
                        !item.Skipped),
                    skippedCards = validationCardDecisions.Count(item =>
                        item.Skipped),
                    cardSkipReasons,
                    skipRate = validationCardDecisions.Count == 0
                        ? 0d
                        : validationCardDecisions.Count(item => item.Skipped)
                          / (double)validationCardDecisions.Count,
                    minimumFinalDeckSize = validationDeckSizes.Count == 0
                        ? 0
                        : validationDeckSizes.Min(),
                    averageFinalDeckSize = validationDeckSizes.Count == 0
                        ? 0d
                        : validationDeckSizes.Average(),
                    maximumFinalDeckSize = validationDeckSizes.Count == 0
                        ? 0
                        : validationDeckSizes.Max(),
                    generatedOnlyDeckViolations = generatedOnlyViolations,
                    completedCampaignDecks = completedValidationRuns
                        .Select(run => run.FinalState.Deck.Count)
                        .ToList(),
                    failedCampaignDecks = failedValidationRuns
                        .Select(run => run.FinalState.Deck.Count)
                        .ToList(),
                    layerDeckStatistics,
                    planSwitches = result.ValidationRuns.Sum(run =>
                        run.FinalState.BuildPlan.Revision),
                    finalBuildPlans = result.ValidationRuns
                        .Select(run => run.FinalState.BuildPlan)
                        .ToList()
                },
                telemetry = new
                {
                    result.EffectiveParallelism,
                    result.PeakConcurrentCampaigns,
                    result.ObservedWorkerThreads,
                    result.CompletedBattles,
                    result.MaximumCompletedBattleDepth,
                    result.Depth1To5Campaigns,
                    result.Depth6To10Campaigns,
                    result.Depth11To20Campaigns,
                    result.Depth21To30Campaigns,
                    result.Depth31To37Campaigns,
                    result.ProjectedBattleDepth,
                    result.EstimatedRemainingSeconds,
                    result.PolicyDecisions,
                    result.SearchSimulations,
                    result.SearchNodes,
                    result.SearchEarlyStops,
                    result.SearchBudgetTierCounts,
                    result.RuleTerminalOverrides,
                    result.CertifiedLoops,
                    result.SustainableControlLoops,
                    result.FakeLoops,
                    result.BlockedLoops,
                    result.ModelCompletedEpochs,
                    result.ModelConfiguredEpochs,
                    result.ModelBestEpoch,
                    result.ModelEarlyStopped,
                    result.ModelBestValidationLoss,
                    result.ElapsedSeconds,
                    campaignsPerSecond = result.CompletedCampaigns
                                         / Math.Max(0.001d, result.ElapsedSeconds),
                    battlesPerSecond = result.CompletedBattles
                                       / Math.Max(0.001d, result.ElapsedSeconds),
                    result.Gen0Collections,
                    result.Gen1Collections,
                    result.Gen2Collections
                },
                result.Preflight,
                result.ExpertReplaySelection,
                result.RewardResidualTraining,
                result.CaseArchiveLoad,
                result.CapabilityProbe,
                result.Validation,
                failureClusters,
                trainingIterations = result.Iterations,
                validationRuns = result.ValidationRuns
            }));
        var episodesPath = Path.Combine(
            resultDirectory,
            "foundation-training-episodes-v3.jsonl");
        if (!writeFullReplay
            || result.Replay.Count > 0
            || !File.Exists(episodesPath))
        {
            using var writer = new StreamWriter(
                episodesPath,
                append: false,
                Encoding.UTF8);
            if (writeFullReplay)
            {
                foreach (var episode in result.Replay)
                {
                    writer.WriteLine(AuraSharedJson.SerializeCompact(episode));
                }
            }
        }
        if (!writeFullReplay)
        {
            WriteText(
                Path.Combine(
                    resultDirectory,
                    "foundation-training-failure-repro-v1.json"),
                AuraSharedJson.Serialize(new
                {
                    schemaVersion = 1,
                    reportKind = "foundation-training-failure-reproduction",
                    createdUtc = DateTime.UtcNow,
                    campaignId = campaign.CampaignId,
                    campaignVersion = campaign.CampaignVersion,
                    rulesetVersion = ruleset.Version,
                    rulesetHash = ruleset.RulesetHash,
                    result.RunSeed,
                    result.TrainingSeedStart,
                    result.ArenaSeedStart,
                    result.TuningSeedStart,
                    result.ValidationSeedStart,
                    result.ModelRandomSeed,
                    settings.Parallelism,
                    settings.Iterations,
                    settings.TrainingCampaignsPerIteration,
                    settings.ArenaCampaignsPerDifficulty,
                    result.Compatibility,
                    result.ExpertReplaySelection,
                    result.RewardResidualTraining,
                    result.CapabilityProbe,
                    result.HardSeedHistory,
                    result.ArenaRetryAttempts,
                    result.ArenaRecoveredCampaigns,
                    result.ArenaIsolatedPairs,
                    result.ArenaReplacementPairs,
                    result.ArenaInvalidSignatures,
                    result.SearchBudgetTierCounts,
                    result.Message,
                    result.TrainingFailureCounts,
                    result.TrainingFailures,
                    result.ArenaFailureCounts,
                    result.ArenaFailures,
                    omittedReplayEpisodes =
                        generatedReplayEpisodes - retainedReplayEpisodes
                }));
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("# 成功案例学习摘要");
        markdown.AppendLine();
        markdown.AppendLine(
            "- 案例库：本轮新增 "
            + result.ArchivedSuccessCases
            + "，重复案例 "
            + result.DuplicateSuccessCases
            + "，本轮载入教师轨迹 "
            + result.LoadedExpertReplayEpisodes
            + "（配置占比 "
            + settings.SuccessExpertReplayShare.ToString("P0")
            + "）。");
        markdown.AppendLine(
            "- 累计兼容案例：有效 "
            + result.CaseAnalysis.IntegrityValidCases
            + "，成功 "
            + result.CaseAnalysis.SuccessfulCases
            + "，失败 "
            + result.CaseAnalysis.FailedCases
            + "，成功—失败匹配对 "
            + result.CaseAnalysis.MatchedPairs
            + "。");
        markdown.AppendLine(
            "- 成功平均终局牌组："
            + result.CaseAnalysis.SuccessfulAverageDeckSize.ToString("F1")
            + "；失败平均终局牌组："
            + result.CaseAnalysis.FailedAverageDeckSize.ToString("F1")
            + "；成功平均稳健度："
            + result.CaseAnalysis.SuccessfulAverageRobustness.ToString("P1")
            + "。");
        if (!string.IsNullOrWhiteSpace(result.SuccessArchiveError))
        {
            markdown.AppendLine("- 案例库写入异常：" + result.SuccessArchiveError);
        }
        markdown.AppendLine(
            "- Expert replay strata: normal/advanced "
            + result.ExpertReplaySelection.SelectedNormalEpisodes
            + "/"
            + result.ExpertReplaySelection.SelectedAdvancedEpisodes
            + "; cases "
            + result.ExpertReplaySelection.SelectedCases
            + "/"
            + result.ExpertReplaySelection.CompatibleCases
            + "; distinct runs "
            + result.ExpertReplaySelection.DistinctRuns
            + "; quota shortfall "
            + (result.ExpertReplaySelection.QuotaShortfalls.Count == 0
                ? "none"
                : string.Join(
                    ",",
                    result.ExpertReplaySelection.QuotaShortfalls.Select(
                        item => item.Key + "=" + item.Value))));
        markdown.AppendLine(
            "- Reward residuals: "
            + result.RewardResidualTraining.Residuals.Count
            + " bounded build adjustments (card/relic/blessing "
            + result.RewardResidualTraining.CardResiduals
            + "/"
            + result.RewardResidualTraining.RelicResiduals
            + "/"
            + result.RewardResidualTraining.BlessingResiduals
            + ") from "
            + result.RewardResidualTraining.SuccessfulObservations
            + " successes / "
            + result.RewardResidualTraining.FailedObservations
            + " late failures; max |residual| "
            + result.RewardResidualTraining.MaximumAbsoluteResidual
                .ToString("F2"));
        markdown.AppendLine(
            "- Case archive load: cases "
            + result.CaseArchiveLoad.LoadedCases
            + "/"
            + result.CaseArchiveLoad.ExpertCaseFiles
            + " files / "
            + result.CaseArchiveLoad.DistinctLoadedCases
            + " distinct"
            + ", observations "
            + result.CaseArchiveLoad.LoadedObservations
            + "/"
            + result.CaseArchiveLoad.ObservationFiles
            + " files / "
            + result.CaseArchiveLoad.DistinctLoadedObservations
            + " distinct"
            + ", rejected "
            + (result.CaseArchiveLoad.RejectedCaseFiles
               + result.CaseArchiveLoad.RejectedObservationFiles)
            + ", migrated cases/observations "
            + result.CaseArchiveLoad.MigratedCases
            + "/"
            + result.CaseArchiveLoad.MigratedObservations
            + ", owner "
            + result.CaseArchiveLoad.OwnerRuntime
            + ", protocol "
            + result.CaseArchiveLoad.ProtocolVersion
            + "; "
            + result.CaseArchiveLoad.Message);
        foreach (var recommendation in result.CaseAnalysis.Recommendations)
        {
            markdown.AppendLine("- 优化提示：" + recommendation);
        }
        markdown.AppendLine();
        markdown.AppendLine("# career_1 世界推演底模训练报告");
        markdown.AppendLine();
        markdown.AppendLine("- 卡池：本体 Normal 全离线卡包，排除联机包、诅咒奖励、衍生牌和 MOD 牌");
        markdown.AppendLine("- 训练 / 竞技场 / 最终验证种子：完全隔离");
        markdown.AppendLine("- RunSeed：" + result.RunSeed
                            + "（关闭“每次训练生成新 RunSeed”并填入此值即可复现）");
        markdown.AppendLine("- 派生种子：训练 "
                            + result.TrainingSeedStart
                            + "；竞技场 "
                            + result.ArenaSeedStart
                            + "；调参 "
                            + result.TuningSeedStart
                            + "；模型 "
                            + result.ModelRandomSeed
                            + "；固定验证 "
                            + result.ValidationSeedStart);
        markdown.AppendLine("- 训练策略：课程难度 "
                            + settings.EnableCurriculum
                            + "；分层回放 "
                            + settings.EnableStratifiedReplay
                            + "；困难种子课程 "
                            + settings.EnableHardSeedCurriculum
                            + "（占比 "
                            + settings.HardSeedReplayShare.ToString("P0")
                            + "；困难遭遇反事实 "
                            + settings.EnableCounterfactualHardEncounters
                            + "；frame 分层 "
                            + settings.EnableFrameStratification
                            + "（最大权重 "
                            + settings.ModelMaximumFrameStratumWeight
                                .ToString("F1")
                            + "）"
                            + "）"
                            + "；自博弈探索率 "
                            + settings.SelfPlayExplorationProbability.ToString("P1")
                            + "；温度 "
                            + settings.SelfPlayExplorationTemperature.ToString("F2"));
        markdown.AppendLine("- 兼容性清单：规则 "
                            + result.Compatibility.RulesetHash
                            + "；原生程序包 "
                            + result.Compatibility.NativeProgramPackageHash
                            + "；战役 "
                            + result.Compatibility.CampaignId
                            + "@"
                            + result.Compatibility.CampaignVersion
                            + "/"
                            + result.Compatibility.TrainingCampaignHash
                            + "/"
                            + result.Compatibility.ValidationCampaignHash
                            + "；特征 "
                            + result.Compatibility.FeatureSchemaVersion
                            + "/"
                            + result.Compatibility.FeatureEncodingMode
                            + "；网络 "
                            + result.Compatibility.StateDimensions
                            + "/"
                            + result.Compatibility.ActionDimensions
                            + "/"
                            + result.Compatibility.HiddenDimensions);
        markdown.AppendLine("- CPU 并行工作线程：" + settings.Parallelism);
        markdown.AppendLine("- 回放保留："
                            + (writeFullReplay
                                ? retainedReplayEpisodes
                                  + " 条完整训练轨迹"
                                : (generatedReplayEpisodes - retainedReplayEpisodes)
                                  + " 条完整轨迹已省略；精确失败种子保存在 foundation-training-failure-repro-v1.json"));
        markdown.AppendLine("- 权威本体程序：构建期静态编译，运行时只按哈希分派");
        markdown.AppendLine("- 训练前权威快检："
                            + result.Preflight.CompletedCampaigns
                            + " 个战役，无效 "
                            + result.Preflight.InvalidCampaigns);
        markdown.AppendLine("- 无效自博弈战役 / 丢弃轨迹："
                            + result.InvalidTrainingCampaigns
                            + " / "
                            + result.DiscardedInvalidEpisodes);
        markdown.AppendLine("- 终局一致性违规："
                            + result.TerminalConsistencyViolations
                            + "；训练特征泄漏字段："
                            + result.FeatureLeakageViolations);
        if (result.TrainingFailures.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("## 自博弈完整性失败");
            markdown.AppendLine();
            foreach (var failure in result.TrainingFailures)
            {
                markdown.AppendLine("- "
                                    + failure.DifficultyId
                                    + " / seed "
                                    + failure.WorldSeed
                                    + " / completed battles "
                                    + failure.CompletedBattles
                                    + " / "
                                    + string.Join(" | ", failure.Reasons));
            }
        }
        if (result.ArenaFailures.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("## 竞技场完整性失败");
            markdown.AppendLine();
            foreach (var failure in result.ArenaFailures)
            {
                markdown.AppendLine("- iteration "
                                    + failure.Iteration
                                    + " / "
                                    + failure.Competitor
                                    + " / "
                                    + failure.DifficultyId
                                    + " / seed "
                                    + failure.WorldSeed
                                    + " / completed battles "
                                    + failure.CompletedBattles
                                    + " / "
                                    + string.Join(" | ", failure.Reasons));
            }
        }
        markdown.AppendLine("- 验收线：普通 "
                            + result.Validation.RequiredNormalVictories
                            + "/"
                            + result.Validation.NormalPlannedCampaigns
                            + "（"
                            + result.Validation.RequiredNormalWinRate.ToString("P0")
                            + "）；高级 "
                            + result.Validation.RequiredAdvancedVictories
                            + "/"
                            + result.Validation.AdvancedPlannedCampaigns
                            + "（"
                            + result.Validation.RequiredAdvancedWinRate.ToString("P0")
                            + "）");
        markdown.AppendLine("- 竞技场完整性恢复：重试 "
                            + result.ArenaRetryAttempts
                            + "；恢复 "
                            + result.ArenaRecoveredCampaigns
                            + "；隔离 "
                            + result.ArenaIsolatedPairs
                            + "；替补 "
                            + result.ArenaReplacementPairs
                            + "；无效签名 "
                            + (result.ArenaInvalidSignatures.Count == 0
                                ? "none"
                                : string.Join(
                                    ", ",
                                    result.ArenaInvalidSignatures.Select(
                                        item => item.Key + "=" + item.Value))));
        markdown.AppendLine("- Observed concurrency peak: "
                            + result.PeakConcurrentCampaigns
                            + "; worker threads: "
                            + result.ObservedWorkerThreads);
        markdown.AppendLine("- Completed battles: "
                            + result.CompletedBattles
                            + "; throughput: "
                            + (result.CompletedBattles
                               / Math.Max(0.001d, result.ElapsedSeconds)).ToString("F2")
                            + " battles/s");
        markdown.AppendLine("- Campaign depth max: "
                            + result.MaximumCompletedBattleDepth
                            + "/37; distribution 1-5/6-10/11-20/21-30/31-37: "
                            + result.Depth1To5Campaigns
                            + "/"
                            + result.Depth6To10Campaigns
                            + "/"
                            + result.Depth11To20Campaigns
                            + "/"
                            + result.Depth21To30Campaigns
                            + "/"
                            + result.Depth31To37Campaigns);
        markdown.AppendLine("- Search work: "
                            + result.PolicyDecisions
                            + " decisions; "
                            + result.SearchSimulations
                            + " simulations; "
                            + result.SearchEarlyStops
                            + " early stops");
        markdown.AppendLine("- Search budget tiers: "
                            + (result.SearchBudgetTierCounts.Count == 0
                                ? "none"
                                : string.Join(
                                    ", ",
                                    result.SearchBudgetTierCounts
                                        .OrderBy(item => item.Key)
                                        .Select(item =>
                                            item.Key + "=" + item.Value))));
        markdown.AppendLine("- Effective exploration: "
                            + result.ExplorationDecisions
                            + " activated; "
                            + result.ExplorationActionOverrides
                            + " selected a non-greedy action; mean max root visit share "
                            + result.RootMaximumVisitShareMean.ToString("P2"));
        markdown.AppendLine("- Authoritative teacher: "
                            + result.AuthoritativeActionsAudited
                            + " exact branches audited; "
                            + result.AuthoritativeSemanticMismatches
                            + " semantic mismatches; "
                            + result.AuthoritativeTeacherOverrides
                            + " corrected actions");
        markdown.AppendLine("- 循环安全：认证无限 "
                            + result.CertifiedLoops
                            + "；可持续控制循环 "
                            + result.SustainableControlLoops
                            + "；假无限 "
                            + result.FakeLoops
                            + "；受限循环 "
                            + result.BlockedLoops
                            + "；复活/逃生终局覆写 "
                            + result.RuleTerminalOverrides);
        markdown.AppendLine("- Model training: "
                            + result.ModelCompletedEpochs
                            + "/"
                            + result.ModelConfiguredEpochs
                            + " epochs; best "
                            + result.ModelBestEpoch
                            + "; validation loss "
                            + result.ModelBestValidationLoss.ToString("F5")
                            + "; early stopped "
                            + result.ModelEarlyStopped);
        markdown.AppendLine("- GC Gen0/1/2: "
                            + result.Gen0Collections
                            + "/"
                            + result.Gen1Collections
                            + "/"
                            + result.Gen2Collections);
        if (!string.IsNullOrWhiteSpace(result.EarlyStopReason))
        {
            markdown.AppendLine("- 提前结束验证：" + result.EarlyStopReason);
        }
        markdown.AppendLine("- 普通结果："
                            + ValidationSummary(
                                result.Validation.NormalVictories,
                                result.Validation.NormalCampaigns,
                                result.Validation.NormalWinRate,
                                result.Validation.NormalPlannedCampaigns));
        markdown.AppendLine("- 高级结果："
                            + ValidationSummary(
                                result.Validation.AdvancedVictories,
                                result.Validation.AdvancedCampaigns,
                                result.Validation.AdvancedWinRate,
                                result.Validation.AdvancedPlannedCampaigns));
        markdown.AppendLine("- 正式隔离验收：" + (result.AcceptancePassed ? "通过" : "未通过"));
        markdown.AppendLine();
        markdown.AppendLine("## 训练迭代");
        markdown.AppendLine();
        foreach (var arm in result.CapabilityProbe.Arms)
        {
            markdown.AppendLine(
                "- Capability probe "
                + arm.ArmId
                + ": normal "
                + arm.NormalVictories
                + "/"
                + arm.NormalCampaigns
                + "; advanced "
                + arm.AdvancedVictories
                + "/"
                + arm.AdvancedCampaigns
                + "; invalid "
                + arm.InvalidCampaigns
                + "; mean depth "
                + arm.AverageCompletedBattles.ToString("F1"));
        }
        foreach (var iteration in result.Iterations)
        {
            markdown.AppendLine("- 第 "
                                + iteration.Iteration
                                + " 轮：轨迹 "
                                + iteration.ReplayEpisodes
                                + "（训练采样 "
                                + iteration.TrainingReplayEpisodes
                                + "，普通/高级/成功 "
                                + iteration.TrainingReplayNormalEpisodes
                                + "/"
                                + iteration.TrainingReplayAdvancedEpisodes
                                + "/"
                                + iteration.TrainingReplaySuccessfulEpisodes
                                + "; deduplicated "
                                + iteration.TrainingReplayDroppedDuplicates
                                + "; target normal share "
                                + iteration.TrainingReplayTargetNormalShare
                                    .ToString("P0")
                                + "; campaigns "
                                + iteration.TrainingReplaySelectedCampaigns
                                + "/"
                                + iteration.TrainingReplaySourceCampaigns
                                + "; quota shortfall "
                                + (iteration.TrainingReplayQuotaShortfalls.Count
                                    == 0
                                    ? "none"
                                    : string.Join(
                                        ",",
                                        iteration.TrainingReplayQuotaShortfalls
                                            .Select(item =>
                                                item.Key + "=" + item.Value)))
                                + "；困难种子 "
                                + iteration.HardSeedTrainingCampaigns
                                + "/"
                                + iteration.HardSeedSourceCampaigns
                                + "，通过 "
                                + iteration.HardSeedTrainingVictories
                                + "; encounter-local "
                                + iteration.HardSeedEncounterCampaigns
                                + "; counterfactual victories "
                                + iteration.HardSeedCounterfactualVictories
                                + ", improvements "
                                + iteration.HardSeedCounterfactualImprovements
                                + ", rejected "
                                + iteration.HardSeedCounterfactualRejected
                                + "/"
                                + iteration.HardSeedCounterfactualCampaigns
                                + "; effective hard share "
                                + iteration.EffectiveHardSeedReplayShare
                                    .ToString("P0")
                                + "; frame strata "
                                + iteration.ModelFrameStrata.Count
                                + " (weight "
                                + iteration.ModelMinimumFrameWeight
                                    .ToString("F2")
                                + "-"
                                + iteration.ModelMaximumFrameWeight
                                    .ToString("F2")
                                + ")"
                                + "，簇 "
                                + (iteration.HardSeedClusters.Count == 0
                                    ? "none"
                                    : string.Join(
                                        ",",
                                        iteration.HardSeedClusters.Select(
                                            item => item.Key + "=" + item.Value)))
                                + "；本轮高级战役 "
                                + iteration.AdvancedTrainingCampaigns
                                + "）"
                                + "；课程 "
                                + iteration.CurriculumStage
                                + "（普通/高级 LCB "
                                + iteration.NormalWilsonLowerBound.ToString("P1")
                                + "/"
                                + iteration.AdvancedWilsonLowerBound.ToString("P1")
                                + "）"
                                + "；普通 "
                                + iteration.CandidateNormalWinRate.ToString("P1")
                                + "；高级 "
                                + iteration.CandidateAdvancedWinRate.ToString("P1")
                                + "；有效配对 "
                                + iteration.ValidArenaPairs
                                + "（筛选/确认 "
                                + iteration.ArenaScreeningPairs
                                + "/"
                                + iteration.ArenaConfirmationPairs
                                + "；候选独赢/冠军独赢 "
                                + iteration.CandidateOnlyWins
                                + "/"
                                + iteration.ChampionOnlyWins
                                + "；配对胜率 LCB "
                                + iteration.PairedWinWilsonLowerBound
                                    .ToString("P1")
                                + "；得分/深度增益 "
                                + iteration.CandidateScoreGain
                                    .ToString("+0.000;-0.000;0.000")
                                + "/"
                                + iteration.CandidateDepthGain
                                    .ToString("+0.00;-0.00;0.00")
                                + "（"
                                + iteration.IterativeGainKind
                                + "）"
                                + "）"
                                + "；无效基准/候选 "
                                + iteration.InvalidChampionCampaigns
                                + "/"
                                + iteration.InvalidCandidateCampaigns
                                + "；"
                                + iteration.PromotionKind
                                + " ("
                                + iteration.PromotionReason
                                + ")");
            markdown.AppendLine("  - 调参候选 "
                                + iteration.TuningCandidateCount
                                + "，选中 epoch "
                                + iteration.TuningSelectedEpoch
                                + "，得分 "
                                + iteration.TuningSelectedScore.ToString("F3")
                                + "，无效 "
                                + iteration.TuningInvalidCampaigns
                                + "；困难来源 "
                                + (iteration.HardSeedSourceCategories.Count == 0
                                    ? "none"
                                    : string.Join(
                                        ", ",
                                        iteration.HardSeedSourceCategories
                                            .Select(item =>
                                                item.Key + "=" + item.Value)))
                                + "；竞技场恢复 "
                                + iteration.ArenaRetryAttempts
                                + "/"
                                + iteration.ArenaRecoveredCampaigns
                                + "/"
                                + iteration.ArenaIsolatedPairs
                                + "/"
                                + iteration.ArenaReplacementPairs);
        }
        markdown.AppendLine();
        markdown.AppendLine("## 最终隔离验证详情");
        markdown.AppendLine();
        foreach (var run in result.ValidationRuns
                     .OrderBy(item => item.DifficultyId, StringComparer.Ordinal)
                     .ThenBy(item => item.WorldSeed))
        {
            markdown.AppendLine("## "
                                + run.DifficultyId
                                + " · 种子 "
                                + run.WorldSeed);
            markdown.AppendLine();
            AuraToolsAutoBattleSimulationRuntime.AppendCampaignSide(
                markdown,
                "底模",
                run,
                ruleset);
        }
        WriteText(
            Path.Combine(resultDirectory, "foundation-training-report.md"),
            markdown.ToString());
        WriteText(
            Path.Combine(resultDirectory, "foundation-training-summary.html"),
            BuildFoundationHtml(campaign, result, settings));
    }

    private static string BuildFoundationHtml(
        CombatCampaignDefinition campaign,
        CombatCampaignFoundationTrainingResult result,
        AutoBattleFoundationTrainingSettings settings)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<title>世界推演底模训练报告</title>");
        html.AppendLine(
            "<style>body{font:15px/1.55 sans-serif;background:#100c1d;color:#eee;padding:24px}"
            + "h1,h2{color:#f1cc70}.cards{display:flex;gap:12px;flex-wrap:wrap}"
            + ".card{background:#211936;border:1px solid #5a4778;border-radius:8px;padding:12px 16px}"
            + ".ok{color:#83e39d}.bad{color:#ff887d}table{border-collapse:collapse;width:100%;margin-top:16px}"
            + "th,td{border-bottom:1px solid #49395f;padding:8px;text-align:left}code{color:#dfbfff}</style>");
        html.AppendLine("</head><body><h1>世界推演底模训练报告</h1>");
        html.Append("<p>角色 <code>")
            .Append(Html(campaign.Player.RoleId))
            .Append("</code> · 线程 ")
            .Append(settings.Parallelism)
            .Append(" · 完成 ")
            .Append(result.CompletedCampaigns)
            .Append("/")
            .Append(result.RequestedCampaigns)
            .Append(" · RunSeed <code>")
            .Append(result.RunSeed)
            .Append("</code>")
            .AppendLine("</p>");
        html.AppendLine("<div class=\"cards\">");
        AppendMetric(
            html,
            "普通难度",
            ValidationSummary(
                result.Validation.NormalVictories,
                result.Validation.NormalCampaigns,
                result.Validation.NormalWinRate,
                result.Validation.NormalPlannedCampaigns),
            result.Validation.NormalCampaigns > 0
            && result.Validation.NormalVictories
            >= result.Validation.RequiredNormalVictories);
        AppendMetric(
            html,
            "高级难度",
            ValidationSummary(
                result.Validation.AdvancedVictories,
                result.Validation.AdvancedCampaigns,
                result.Validation.AdvancedWinRate,
                result.Validation.AdvancedPlannedCampaigns),
            result.Validation.AdvancedCampaigns > 0
            && result.Validation.AdvancedVictories
            >= result.Validation.RequiredAdvancedVictories);
        AppendMetric(
            html,
            "训练前权威快检",
            result.Preflight.CompletedCampaigns
            + " 个战役 · 无效 "
            + result.Preflight.InvalidCampaigns,
            result.Preflight.Passed);
        AppendMetric(
            html,
            "正式验收",
            result.AcceptancePassed ? "已通过" : "未通过",
            result.AcceptancePassed);
        AppendMetric(
            html,
            "训练完整性",
            "终局违规 "
            + result.TerminalConsistencyViolations
            + " · 泄漏字段 "
            + result.FeatureLeakageViolations,
            result.TerminalConsistencyViolations == 0
            && result.FeatureLeakageViolations == 0);
        AppendMetric(
            html,
            "Parallel telemetry",
            "peak "
            + result.PeakConcurrentCampaigns
            + "/"
            + result.EffectiveParallelism
            + " · "
            + result.ObservedWorkerThreads
            + " threads · "
            + (result.CompletedBattles
               / Math.Max(0.001d, result.ElapsedSeconds)).ToString("F2")
            + " battles/s",
            result.PeakConcurrentCampaigns > 1);
        AppendMetric(
            html,
            "成功案例库",
            "新增 "
            + result.ArchivedSuccessCases
            + " / 重复 "
            + result.DuplicateSuccessCases
            + " / 教师轨迹 "
            + result.LoadedExpertReplayEpisodes,
            string.IsNullOrWhiteSpace(result.SuccessArchiveError));
        AppendMetric(
            html,
            "成功—失败对照",
            result.CaseAnalysis.SuccessfulCases
            + " 成功 / "
            + result.CaseAnalysis.FailedCases
            + " 失败 / "
            + result.CaseAnalysis.MatchedPairs
            + " 匹配对",
            result.CaseAnalysis.IntegrityValidCases > 0);
        AppendMetric(
            html,
            "Campaign depth / search",
            "max "
            + result.MaximumCompletedBattleDepth
            + "/37 路 "
            + result.SearchSimulations
            + " simulations 路 "
            + result.SearchEarlyStops
            + " early stops",
            result.MaximumCompletedBattleDepth > 0);
        AppendMetric(
            html,
            "精确教师 / 有效探索",
            result.AuthoritativeActionsAudited
            + " branches · "
            + result.AuthoritativeSemanticMismatches
            + " mismatch · "
            + result.AuthoritativeTeacherOverrides
            + " corrections · "
            + result.ExplorationActionOverrides
            + "/"
            + result.ExplorationDecisions
            + " exploration overrides",
            result.AuthoritativeActionsAudited > 0
            && result.ExplorationDecisions > 0);
        AppendMetric(
            html,
            "循环安全 / 终局规则",
            "认证 "
            + result.CertifiedLoops
            + " · 控制 "
            + result.SustainableControlLoops
            + " · 假无限 "
            + result.FakeLoops
            + " · 受限 "
            + result.BlockedLoops
            + " · 终局覆写 "
            + result.RuleTerminalOverrides,
            result.FakeLoops == 0 && result.BlockedLoops == 0);
        AppendMetric(
            html,
            "Model minibatch",
            result.ModelCompletedEpochs
            + "/"
            + result.ModelConfiguredEpochs
            + " epochs · best "
            + result.ModelBestEpoch
            + " · loss "
            + result.ModelBestValidationLoss.ToString("F5"),
            result.ModelCompletedEpochs > 0);
        html.AppendLine("</div>");
        if (result.TrainingFailures.Count > 0)
        {
            html.AppendLine(
                "<h2>自博弈完整性失败</h2><table><thead><tr>"
                + "<th>难度</th><th>Seed</th><th>完成战斗</th><th>原因</th>"
                + "</tr></thead><tbody>");
            foreach (var failure in result.TrainingFailures)
            {
                html.Append("<tr><td>")
                    .Append(Html(failure.DifficultyId))
                    .Append("</td><td>")
                    .Append(failure.WorldSeed)
                    .Append("</td><td>")
                    .Append(failure.CompletedBattles)
                    .Append("</td><td>")
                    .Append(Html(string.Join(" | ", failure.Reasons)))
                    .AppendLine("</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
        if (result.ArenaFailures.Count > 0)
        {
            html.AppendLine(
                "<h2>竞技场完整性失败</h2><table><thead><tr>"
                + "<th>轮次</th><th>角色</th><th>难度</th><th>Seed</th>"
                + "<th>完成战斗</th><th>原因</th></tr></thead><tbody>");
            foreach (var failure in result.ArenaFailures)
            {
                html.Append("<tr><td>")
                    .Append(failure.Iteration)
                    .Append("</td><td>")
                    .Append(Html(failure.Competitor))
                    .Append("</td><td>")
                    .Append(Html(failure.DifficultyId))
                    .Append("</td><td>")
                    .Append(failure.WorldSeed)
                    .Append("</td><td>")
                    .Append(failure.CompletedBattles)
                    .Append("</td><td>")
                    .Append(Html(string.Join(" | ", failure.Reasons)))
                    .AppendLine("</td></tr>");
            }
            html.AppendLine("</tbody></table>");
        }
        html.AppendLine(
            "<h2>隔离验证冒险</h2><table><thead><tr>"
            + "<th>难度</th><th>种子</th><th>结果</th><th>进度</th>"
            + "<th>最终生命</th><th>牌组</th><th>遗物</th><th>祝福</th><th>终战回合</th>"
            + "</tr></thead><tbody>");
        foreach (var run in result.ValidationRuns
                     .OrderBy(item => item.DifficultyId, StringComparer.Ordinal)
                     .ThenBy(item => item.WorldSeed))
        {
            var terminal = run.Battles.LastOrDefault();
            html.Append("<tr><td>")
                .Append(Html(run.DifficultyId))
                .Append("</td><td>")
                .Append(run.WorldSeed)
                .Append("</td><td class=\"")
                .Append(run.FinalBossVictory ? "ok" : "bad")
                .Append("\">")
                .Append(run.FinalBossVictory ? "最终首领胜利" : "未通关")
                .Append("</td><td>")
                .Append(run.CompletedBattles)
                .Append("/37</td><td>")
                .Append(run.FinalState.CurrentHp)
                .Append("/")
                .Append(run.FinalState.MaxHp)
                .Append("</td><td title=\"")
                .Append(Html(string.Join(",", run.FinalState.Deck)))
                .Append("\">")
                .Append(run.FinalState.Deck.Count)
                .Append("</td><td>")
                .Append(run.FinalState.Relics.Count)
                .Append("</td><td>")
                .Append(run.FinalState.Blessings.Count)
                .Append("</td><td>")
                .Append(terminal?.Turns ?? 0)
                .AppendLine("</td></tr>");
        }
        html.AppendLine("</tbody></table>");
        html.AppendLine(
            "<p>完整奖励评分、最终构筑和最终首领逐事件流程见 "
            + "<code>foundation-training-report.md</code>；"
            + "机器可读数据见 <code>foundation-training-report.json</code>。</p>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void AppendMetric(
        StringBuilder html,
        string label,
        string value,
        bool passed)
    {
        html.Append("<div class=\"card\"><strong>")
            .Append(Html(label))
            .Append("</strong><div class=\"")
            .Append(passed ? "ok" : "bad")
            .Append("\">")
            .Append(Html(value))
            .AppendLine("</div></div>");
    }

    private static string Html(string value)
    {
        return (value ?? "")
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string ValidationSummary(
        int victories,
        int campaigns,
        double winRate,
        int plannedCampaigns = 0)
    {
        if (campaigns <= 0)
        {
            return plannedCampaigns > 0
                ? "未执行（计划 " + plannedCampaigns + "）"
                : "未执行";
        }
        var executed = victories
                       + "/"
                       + campaigns
                       + "（已执行，"
                       + winRate.ToString("P1")
                       + "）";
        return plannedCampaigns > campaigns
            ? executed + "；计划 " + plannedCampaigns + "，已提前结束"
            : executed;
    }

    private static CombatCampaignDefinition CloneCampaign(
        CombatCampaignDefinition source)
    {
        return AuraSharedJson.Deserialize<CombatCampaignDefinition>(
                   AuraSharedJson.Serialize(source))
               ?? throw new InvalidOperationException("无法克隆底模训练推演包");
    }

    private static int TotalCampaigns(AutoBattleFoundationTrainingSettings settings)
    {
        settings ??= new AutoBattleFoundationTrainingSettings();
        settings.Normalize();
        return settings.Iterations
               * (settings.TrainingCampaignsPerIteration
                  + (settings.ArenaCampaignsPerDifficulty
                     + (settings.ArenaCampaignsPerDifficulty >= 32
                         ? settings.ArenaConfirmationCampaignsPerDifficulty
                         : 0)) * 4
                  + (settings.EnableTuningArena
                      ? settings.ModelRetainedCandidates
                        * (settings.TuningNormalCampaigns
                           + settings.TuningAdvancedCampaigns)
                      : 0))
               + settings.NormalValidationCampaigns
               + settings.AdvancedValidationCampaigns
               + settings.CapabilityProbeCampaignsPerDifficulty * 2 * 3;
    }

    private static CombatFoundationTrainingParameters ToSharedParameters(
        AutoBattleFoundationTrainingSettings source,
        int minimumEpisodes,
        string decisionProfile)
    {
        return new CombatFoundationTrainingParameters
        {
            RunSeed = source.RunSeed,
            DecisionProfile = decisionProfile,
            Iterations = source.Iterations,
            TrainingCampaignsPerIteration =
                source.TrainingCampaignsPerIteration,
            ArenaCampaignsPerDifficulty =
                source.ArenaCampaignsPerDifficulty,
            ArenaConfirmationCampaignsPerDifficulty =
                source.ArenaConfirmationCampaignsPerDifficulty,
            NormalValidationCampaigns =
                source.NormalValidationCampaigns,
            AdvancedValidationCampaigns =
                source.AdvancedValidationCampaigns,
            CapabilityProbeCampaignsPerDifficulty =
                source.CapabilityProbeCampaignsPerDifficulty,
            PreflightCampaignsPerDifficulty =
                source.PreflightCampaignsPerDifficulty,
            MaximumDegreeOfParallelism = source.Parallelism,
            EnableEarlyValidationStop = source.EarlyValidationStop,
            EnableCurriculum = source.EnableCurriculum,
            EnableStratifiedReplay = source.EnableStratifiedReplay,
            EnableHardSeedCurriculum =
                source.EnableHardSeedCurriculum,
            EnableCounterfactualHardEncounters =
                source.EnableCounterfactualHardEncounters,
            EnableSuccessCaseArchive =
                source.EnableSuccessCaseArchive,
            EnableArenaRecovery = source.EnableArenaRecovery,
            ArenaInvalidRetryCount = source.ArenaInvalidRetryCount,
            ArenaInvalidRateLimit = source.ArenaInvalidRateLimit,
            EnableTuningArena = source.EnableTuningArena,
            TuningNormalCampaigns = source.TuningNormalCampaigns,
            TuningAdvancedCampaigns = source.TuningAdvancedCampaigns,
            MaximumConsecutiveRejectedIterations =
                source.MaximumConsecutiveRejectedIterations,
            NormalAcceptanceRate = source.NormalAcceptanceRate,
            AdvancedAcceptanceRate = source.AdvancedAcceptanceRate,
            SuccessExpertReplayShare =
                source.SuccessExpertReplayShare,
            HardSeedReplayShare = source.HardSeedReplayShare,
            SelfPlayExplorationProbability =
                source.SelfPlayExplorationProbability,
            SelfPlayExplorationTemperature =
                source.SelfPlayExplorationTemperature,
            ModelEpochs = source.ModelEpochs,
            ModelMinimumEpochs = source.ModelMinimumEpochs,
            ModelEarlyStoppingPatience =
                source.ModelEarlyStoppingPatience,
            ModelEarlyStoppingMinimumDelta =
                source.ModelEarlyStoppingMinimumDelta,
            ModelBatchSize = source.ModelBatchSize,
            EnableFrameStratification =
                source.EnableFrameStratification,
            ModelMaximumFrameStratumWeight =
                source.ModelMaximumFrameStratumWeight,
            ModelReplayEpisodeLimit =
                source.ModelReplayEpisodeLimit,
            ModelRetainedCandidates =
                source.ModelRetainedCandidates,
            ModelLearningRate = source.ModelLearningRate,
            ModelL2 = source.ModelL2,
            ModelStateDimensions = source.ModelStateDimensions,
            ModelActionDimensions = source.ModelActionDimensions,
            ModelHiddenDimensions = source.ModelHiddenDimensions,
            ModelFeatureEncodingMode =
                source.ModelFeatureEncodingMode,
            MinimumEpisodes = minimumEpisodes,
            TrainingSeedStart = source.TrainingSeedStart,
            ArenaSeedStart = source.ArenaSeedStart,
            TuningSeedStart = source.TuningSeedStart,
            ValidationSeedStart = source.ValidationSeedStart
        }.Normalized();
    }

    private static ulong GenerateRunSeed()
    {
        var bytes = new byte[sizeof(ulong)];
        using (var generator = RandomNumberGenerator.Create())
        {
            generator.GetBytes(bytes);
        }
        var value = BitConverter.ToUInt64(bytes, 0);
        return value == 0UL ? 1UL : value;
    }

    private static string CurrentRuntimePackageHash()
    {
        try
        {
            var path =
                System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                using var hash = SHA256.Create();
                return BitConverter.ToString(hash.ComputeHash(stream))
                    .Replace("-", "");
            }
        }
        catch
        {
            // The protocol/count fallback remains deterministic for hosts that
            // shadow-copy or do not expose the loaded assembly path.
        }
        return NativeRewardScriptGlobals.PrecompiledProgramProtocol
               + ":"
               + NativeRewardScriptGlobals.PrecompiledProgramCount;
    }

    private static void UpdateTrainingProgress(
        string message,
        int completed,
        int requested,
        int configuredWorkers,
        double campaignsPerSecond,
        double elapsedSeconds,
        double estimatedRemainingSeconds)
    {
        lock (Gate)
        {
            if (status.Stage != AutoBattleFoundationStage.Training)
            {
                return;
            }
            status.Message = message ?? "";
            status.CompletedCampaigns = completed;
            status.RequestedCampaigns = requested;
            status.WorkerCount = Math.Max(
                status.WorkerCount,
                configuredWorkers);
            status.CampaignsPerSecond = campaignsPerSecond;
            status.ElapsedSeconds = elapsedSeconds;
            status.EstimatedRemainingSeconds = estimatedRemainingSeconds;
            status.UpdatedUtc = DateTime.UtcNow;
        }
    }

    private static void UpdateTrainingTelemetry(
        CombatCampaignFoundationTelemetry telemetry)
    {
        if (telemetry == null)
        {
            return;
        }
        lock (Gate)
        {
            if (status.Stage != AutoBattleFoundationStage.Training)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(telemetry.Stage))
            {
                status.Message = "训练阶段：" + telemetry.Stage;
            }
            status.Phase = telemetry.Phase ?? "";
            status.WorkerCount = telemetry.EffectiveParallelism;
            status.ActiveWorkerCount = telemetry.ActiveCampaigns;
            status.PeakWorkerCount = telemetry.PeakConcurrentCampaigns;
            status.ObservedWorkerThreads = telemetry.ObservedWorkerThreads;
            status.CompletedCampaigns = Math.Max(
                status.CompletedCampaigns,
                telemetry.CompletedCampaigns);
            status.RequestedCampaigns = telemetry.RequestedCampaigns;
            status.CompletedBattles = telemetry.CompletedBattles;
            status.MaximumCompletedBattleDepth =
                telemetry.MaximumCompletedBattleDepth;
            status.MaximumActiveBattleDepth =
                telemetry.MaximumActiveBattleDepth;
            status.Depth1To5Campaigns = telemetry.Depth1To5Campaigns;
            status.Depth6To10Campaigns = telemetry.Depth6To10Campaigns;
            status.Depth11To20Campaigns = telemetry.Depth11To20Campaigns;
            status.Depth21To30Campaigns = telemetry.Depth21To30Campaigns;
            status.Depth31To37Campaigns = telemetry.Depth31To37Campaigns;
            status.ProjectedBattleDepth = telemetry.ProjectedBattleDepth;
            status.SearchSimulations = telemetry.SearchSimulations;
            status.SearchEarlyStops = telemetry.SearchEarlyStops;
            status.SearchSimulationsPerSecond =
                telemetry.SearchSimulationsPerSecond;
            status.CampaignsPerSecond = telemetry.CampaignsPerSecond;
            status.BattlesPerSecond = telemetry.BattlesPerSecond;
            status.ElapsedSeconds = telemetry.ElapsedSeconds;
            status.EstimatedRemainingSeconds =
                telemetry.EstimatedRemainingSeconds;
            status.ModelEpoch = telemetry.ModelEpoch;
            status.ModelTotalEpochs = telemetry.ModelTotalEpochs;
            status.ModelCompletedFrames = telemetry.ModelCompletedFrames;
            status.ModelTotalFrames = telemetry.ModelTotalFrames;
            status.ModelValidationLoss = telemetry.ModelValidationLoss;
            status.ModelBestEpoch = telemetry.ModelBestEpoch;
            status.ModelStaleEpochs = telemetry.ModelStaleEpochs;
            status.ModelEarlyStopped = telemetry.ModelEarlyStopped;
            status.Gen0Collections = telemetry.Gen0Collections;
            status.Gen1Collections = telemetry.Gen1Collections;
            status.Gen2Collections = telemetry.Gen2Collections;
            status.ProgressDiagnostic = "";
            status.UpdatedUtc = DateTime.UtcNow;
        }
    }

    private static void UpdateTrainingDiagnostic(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return;
        }
        lock (Gate)
        {
            if (status.Stage != AutoBattleFoundationStage.Training)
            {
                return;
            }
            status.ProgressDiagnostic = diagnostic;
            status.Message = "无法同步底模训练进度：" + diagnostic;
            status.UpdatedUtc = DateTime.UtcNow;
        }
    }

    private static void SetStatus(
        AutoBattleFoundationStage stage,
        string message,
        int completed = 0,
        int requested = 0,
        double normalWinRate = 0d,
        double advancedWinRate = 0d,
        bool acceptancePassed = false,
        string modelId = "",
        string resultDirectory = "",
        int workerCount = 0,
        double campaignsPerSecond = 0d,
        double battlesPerSecond = 0d,
        double elapsedSeconds = 0d,
        double estimatedRemainingSeconds = 0d,
        string earlyStopReason = "",
        int activeWorkerCount = 0,
        int peakWorkerCount = 0,
        int observedWorkerThreads = 0,
        int completedBattles = 0,
        int gen0Collections = 0,
        int gen1Collections = 0,
        int gen2Collections = 0,
        int maximumCompletedBattleDepth = 0,
        int maximumActiveBattleDepth = 0,
        int depth1To5Campaigns = 0,
        int depth6To10Campaigns = 0,
        int depth11To20Campaigns = 0,
        int depth21To30Campaigns = 0,
        int depth31To37Campaigns = 0,
        double projectedBattleDepth = 0d,
        long searchSimulations = 0L,
        int searchEarlyStops = 0,
        double searchSimulationsPerSecond = 0d)
    {
        lock (Gate)
        {
            status = new AutoBattleFoundationStatus
            {
                Stage = stage,
                Message = message ?? "",
                CompletedCampaigns = completed,
                RequestedCampaigns = requested,
                NormalWinRate = normalWinRate,
                AdvancedWinRate = advancedWinRate,
                AcceptancePassed = acceptancePassed,
                ModelId = modelId ?? "",
                ResultDirectory = resultDirectory ?? "",
                WorkerCount = workerCount,
                ActiveWorkerCount = activeWorkerCount,
                PeakWorkerCount = peakWorkerCount,
                ObservedWorkerThreads = observedWorkerThreads,
                CompletedBattles = completedBattles,
                MaximumCompletedBattleDepth = maximumCompletedBattleDepth,
                MaximumActiveBattleDepth = maximumActiveBattleDepth,
                Depth1To5Campaigns = depth1To5Campaigns,
                Depth6To10Campaigns = depth6To10Campaigns,
                Depth11To20Campaigns = depth11To20Campaigns,
                Depth21To30Campaigns = depth21To30Campaigns,
                Depth31To37Campaigns = depth31To37Campaigns,
                ProjectedBattleDepth = projectedBattleDepth,
                SearchSimulations = searchSimulations,
                SearchEarlyStops = searchEarlyStops,
                SearchSimulationsPerSecond = searchSimulationsPerSecond,
                CampaignsPerSecond = campaignsPerSecond,
                BattlesPerSecond = battlesPerSecond,
                ElapsedSeconds = elapsedSeconds,
                EstimatedRemainingSeconds = estimatedRemainingSeconds,
                EarlyStopReason = earlyStopReason ?? "",
                Gen0Collections = gen0Collections,
                Gen1Collections = gen1Collections,
                Gen2Collections = gen2Collections,
                UpdatedUtc = DateTime.UtcNow
            };
        }
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(
                ref target,
                value,
                current);
            if (observed == current)
            {
                return;
            }
            current = observed;
        }
    }

    private static FoundationReadinessResult ResolveReadiness()
    {
        if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out var campaign,
                out var ruleset,
                out var message))
        {
            return new FoundationReadinessResult
            {
                Message = message
            };
        }
        var packageValidation = AuraToolsNativeProgramPackageAudit.Validate(
            campaign,
            ruleset);
        return packageValidation.Success
            ? new FoundationReadinessResult
            {
                Ready = true,
                Message = "权威知识包已就绪；"
                          + packageValidation.ReferencedProgramCount
                          + " 个本体程序已在构建期编译"
            }
            : new FoundationReadinessResult
            {
                Message = "权威预编译程序包校验失败："
                          + string.Join(
                              "；",
                              packageValidation.Errors.Take(5))
            };
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(path, text, createBackup: false);
    }

    private sealed class FoundationWorkResult
    {
        public bool Success { get; set; }

        public bool Cancelled { get; set; }

        public string Message { get; set; } = "";

        public int CompletedCampaigns { get; set; }

        public int RequestedCampaigns { get; set; }

        public double NormalWinRate { get; set; }

        public double AdvancedWinRate { get; set; }

        public bool AcceptancePassed { get; set; }

        public string ModelId { get; set; } = "";

        public string ResultDirectory { get; set; } = "";

        public int WorkerCount { get; set; }

        public int ActiveWorkerCount { get; set; }

        public int PeakWorkerCount { get; set; }

        public int ObservedWorkerThreads { get; set; }

        public int CompletedBattles { get; set; }

        public int MaximumCompletedBattleDepth { get; set; }

        public int MaximumActiveBattleDepth { get; set; }

        public int Depth1To5Campaigns { get; set; }

        public int Depth6To10Campaigns { get; set; }

        public int Depth11To20Campaigns { get; set; }

        public int Depth21To30Campaigns { get; set; }

        public int Depth31To37Campaigns { get; set; }

        public double ProjectedBattleDepth { get; set; }

        public long SearchSimulations { get; set; }

        public int SearchEarlyStops { get; set; }

        public double SearchSimulationsPerSecond { get; set; }

        public double CampaignsPerSecond { get; set; }

        public double BattlesPerSecond { get; set; }

        public double ElapsedSeconds { get; set; }

        public string EarlyStopReason { get; set; } = "";

        public int Gen0Collections { get; set; }

        public int Gen1Collections { get; set; }

        public int Gen2Collections { get; set; }

        public static FoundationWorkResult Failed(string message, int requested)
        {
            return new FoundationWorkResult
            {
                Message = message,
                RequestedCampaigns = requested
            };
        }
    }

    private sealed class FoundationReadinessResult
    {
        public bool Ready { get; set; }

        public string Message { get; set; } = "";
    }
}
