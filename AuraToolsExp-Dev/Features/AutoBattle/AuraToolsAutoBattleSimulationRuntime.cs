using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum AutoBattleSimulationStage
{
    Idle,
    Queued,
    Resolving,
    Running,
    Cancelling,
    Writing,
    Completed,
    Cancelled,
    Failed
}

internal enum AutoBattleSimulationOperation
{
    None,
    PairedEvaluation,
    PolicyEvolution
}

internal sealed class AutoBattleSimulationStatus
{
    public AutoBattleSimulationStage Stage { get; set; }

    public AutoBattleSimulationOperation Operation { get; set; }

    public string Message { get; set; } = "尚未运行模拟评估";

    public int CompletedPairs { get; set; }

    public int RequestedPairs { get; set; }

    public string ScenarioId { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public bool GatePassed { get; set; }

    public string ProgressUnit { get; set; } = "局";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Busy => Stage == AutoBattleSimulationStage.Queued
                        || Stage == AutoBattleSimulationStage.Resolving
                        || Stage == AutoBattleSimulationStage.Running
                        || Stage == AutoBattleSimulationStage.Cancelling
                        || Stage == AutoBattleSimulationStage.Writing;

    public AutoBattleSimulationStatus Clone()
    {
        return (AutoBattleSimulationStatus)MemberwiseClone();
    }
}

internal sealed class AutoBattleSimulationSummary
{
    public string RunId { get; set; } = "";

    public string ScenarioId { get; set; } = "";

    public string Profile { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public int RequestedPairs { get; set; }

    public int CompletedPairs { get; set; }

    public int AuthoritativePairs { get; set; }

    public int InvalidPairs { get; set; }

    public int DivergentPairs { get; set; }

    public int BaselineVictories { get; set; }

    public int LearnedVictories { get; set; }

    public double AuthoritativeCoverage { get; set; }

    public double BaselineWinRate { get; set; }

    public double LearnedWinRate { get; set; }

    public double BaselineMeanTurns { get; set; }

    public double LearnedMeanTurns { get; set; }

    public double BaselineMeanFinalPlayerHp { get; set; }

    public double LearnedMeanFinalPlayerHp { get; set; }

    public bool GatePassed { get; set; }

    public string GateReason { get; set; } = "";

    public bool Cancelled { get; set; }

    public DateTime CompletedUtc { get; set; }
}

internal sealed class AutoBattleEvolutionSummary
{
    public int SchemaVersion { get; set; } = 1;

    public string RunId { get; set; } = "";

    public DateTime CompletedUtc { get; set; }

    public string Profile { get; set; } = "";

    public string[] ScenarioIds { get; set; } = Array.Empty<string>();

    public string RulesetHash { get; set; } = "";

    public string InitialChampionId { get; set; } = "none";

    public string ChampionId { get; set; } = "none";

    public string CandidatePath { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public int RequestedBattles { get; set; }

    public int CompletedBattles { get; set; }

    public int ReplayEpisodes { get; set; }

    public int PromotedCount { get; set; }

    public bool GatePassed { get; set; }

    public string Message { get; set; } = "";

    public List<CombatPolicyEvolutionIteration> Iterations { get; set; } = new();
}

internal sealed class AutoBattleSimulationResultPresentation
{
    public bool Available { get; set; }

    public bool GatePassed { get; set; }

    public string Title { get; set; } = "尚无模拟结果";

    public string Primary { get; set; } = "";

    public string Secondary { get; set; } = "";

    public string Detail { get; set; } = "";

    public string ResultDirectory { get; set; } = "";
}

internal static class AuraToolsAutoBattleSimulationRuntime
{
    private const string WorkKey = "AutoBattle.Simulation";
    private const string EvolutionWorkKey = "AutoBattle.PolicyEvolution";
    private static readonly object Gate = new();
    private static readonly object ResultCacheGate = new();
    private static readonly Dictionary<string, ResultPresentationCacheEntry> ResultCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static AutoBattleSimulationStatus status = new();
    private static CancellationTokenSource? cancellation;

    public static string InputDirectory =>
        Path.Combine(AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId), "combat-simulation-input");

    public static string ResultsRootDirectory =>
        Path.Combine(AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId), "combat-simulation-results");

    public static IReadOnlyList<string> AvailableScenarioIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var scenario in CombatSimulationRegistry.SnapshotScenarios())
            {
                if (!string.IsNullOrWhiteSpace(scenario.ScenarioId))
                {
                    ids.Add(scenario.ScenarioId.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[AutoBattle][Simulation] 场景提供者枚举失败：" + ex.Message);
        }

        Directory.CreateDirectory(InputDirectory);
        foreach (var path in ScenarioFiles())
        {
            try
            {
                var scenario = AuraSharedJson.Deserialize<CombatScenarioDefinition>(File.ReadAllText(path));
                var scenarioId = scenario?.ScenarioId;
                if (!string.IsNullOrWhiteSpace(scenarioId))
                {
                    ids.Add(scenarioId!.Trim());
                }
            }
            catch
            {
                // Invalid files are reported when the selected scenario is resolved.
            }
        }
        return ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    public static bool QueueRun(
        AutoBattleSettings settings,
        out string message)
    {
        if (settings == null)
        {
            message = "自动战斗设置为空";
            return false;
        }
        settings.Normalize();
        var request = Snapshot(settings);
        if (AuraToolsAutoBattleModelRuntime.AnyTrainingBusy())
        {
            message = "候选训练或导入正在运行";
            return false;
        }
        lock (Gate)
        {
            if (status.Busy)
            {
                message = "已有模拟评估正在运行";
                return false;
            }
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            status = new AutoBattleSimulationStatus
            {
                Stage = AutoBattleSimulationStage.Queued,
                Operation = AutoBattleSimulationOperation.PairedEvaluation,
                Message = "模拟评估已排队",
                RequestedPairs = request.Simulation.SimulationCount,
                ScenarioId = request.Simulation.ScenarioId,
                ProgressUnit = "对照组"
            };
        }

        var ownedCancellation = cancellation;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<SimulationWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = WorkKey,
                Source = "AutoBattle.PairedSimulation",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = schedulerToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        schedulerToken,
                        ownedCancellation.Token);
                    return Run(request, linked.Token);
                },
                ApplyOnMainThread = result =>
                {
                    SetStatus(
                        result.Cancelled
                            ? AutoBattleSimulationStage.Cancelled
                            : result.Success
                                ? AutoBattleSimulationStage.Completed
                                : AutoBattleSimulationStage.Failed,
                        result.Message,
                        result.CompletedPairs,
                        result.RequestedPairs,
                        result.ScenarioId,
                        result.ResultDirectory,
                        result.GatePassed);
                    (result.Success ? (Action<string>)AuraToolsLog.Info : AuraToolsLog.Warn)(
                        "[AutoBattle][Simulation] " + result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    SetStatus(
                        AutoBattleSimulationStage.Failed,
                        "模拟评估失败：" + ex.Message);
                    AuraToolsLog.Warn("[AutoBattle][Simulation] 模拟评估失败：" + ex);
                }
            });
        if (!queued)
        {
            SetStatus(AutoBattleSimulationStage.Failed, "模拟评估任务未能提交");
            message = "模拟评估任务未能提交";
            return false;
        }

        message = "模拟评估已提交";
        return true;
    }

    public static bool QueueEvolution(
        AutoBattleSettings settings,
        out string message)
    {
        if (settings == null)
        {
            message = "自动战斗设置为空";
            return false;
        }
        settings.Normalize();
        var request = Snapshot(settings);
        if (AuraToolsAutoBattleModelRuntime.AnyTrainingBusy())
        {
            message = "候选训练或导入正在运行";
            return false;
        }
        lock (Gate)
        {
            if (status.Busy)
            {
                message = "已有模拟或进化训练正在运行";
                return false;
            }
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            status = new AutoBattleSimulationStatus
            {
                Stage = AutoBattleSimulationStage.Queued,
                Operation = AutoBattleSimulationOperation.PolicyEvolution,
                Message = "策略进化训练已排队",
                RequestedPairs = EvolutionBattleCount(request.Simulation),
                ScenarioId = request.Simulation.ScenarioId,
                ProgressUnit = "场战斗"
            };
        }
        var ownedCancellation = cancellation;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<SimulationWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = EvolutionWorkKey,
                Source = "AutoBattle.PolicyEvolution",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = schedulerToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        schedulerToken,
                        ownedCancellation.Token);
                    return RunEvolution(request, linked.Token);
                },
                ApplyOnMainThread = result =>
                {
                    SetStatus(
                        result.Cancelled
                            ? AutoBattleSimulationStage.Cancelled
                            : result.Success
                                ? AutoBattleSimulationStage.Completed
                                : AutoBattleSimulationStage.Failed,
                        result.Message,
                        result.CompletedPairs,
                        result.RequestedPairs,
                        result.ScenarioId,
                        result.ResultDirectory,
                        result.GatePassed);
                    (result.Success ? (Action<string>)AuraToolsLog.Info : AuraToolsLog.Warn)(
                        "[AutoBattle][Evolution] " + result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    SetStatus(
                        AutoBattleSimulationStage.Failed,
                        "策略进化训练失败：" + ex.Message);
                    AuraToolsLog.Warn("[AutoBattle][Evolution] " + ex);
                }
            });
        if (!queued)
        {
            SetStatus(AutoBattleSimulationStage.Failed, "策略进化训练任务未能提交");
            message = "策略进化训练任务未能提交";
            return false;
        }
        message = "策略进化训练已提交";
        return true;
    }

    public static void Cancel()
    {
        lock (Gate)
        {
            cancellation?.Cancel();
            if (status.Busy)
            {
                status.Stage = AutoBattleSimulationStage.Cancelling;
                status.Message = status.Operation == AutoBattleSimulationOperation.PolicyEvolution
                    ? "正在取消策略进化训练"
                    : "正在取消对照评估";
                status.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    public static AutoBattleSimulationStatus GetStatus()
    {
        lock (Gate)
        {
            return status.Clone();
        }
    }

    public static void OpenInputDirectory()
    {
        Directory.CreateDirectory(InputDirectory);
        FileResourceUtil.OpenDirectory(InputDirectory);
    }

    public static void OpenResultDirectory(string profile = "balanced")
    {
        var current = GetStatus();
        var presentation = GetResultPresentation(profile);
        var path = Directory.Exists(current.ResultDirectory)
            ? current.ResultDirectory
            : Directory.Exists(presentation.ResultDirectory)
                ? presentation.ResultDirectory
            : ResultsRootDirectory;
        Directory.CreateDirectory(path);
        FileResourceUtil.OpenDirectory(path);
    }

    public static AutoBattleSimulationResultPresentation GetResultPresentation(
        string profile)
    {
        var normalizedProfile = string.IsNullOrWhiteSpace(profile)
            ? "balanced"
            : profile.Trim().ToLowerInvariant();
        var pairedPath = LatestSummaryPath(normalizedProfile);
        var evolutionPath = LatestEvolutionPath(normalizedProfile);
        var pairedWrite = File.Exists(pairedPath)
            ? File.GetLastWriteTimeUtc(pairedPath)
            : DateTime.MinValue;
        var evolutionWrite = File.Exists(evolutionPath)
            ? File.GetLastWriteTimeUtc(evolutionPath)
            : DateTime.MinValue;
        lock (ResultCacheGate)
        {
            if (ResultCache.TryGetValue(normalizedProfile, out var cached)
                && cached.PairedWriteUtc == pairedWrite
                && cached.EvolutionWriteUtc == evolutionWrite)
            {
                return cached.Presentation;
            }
        }
        var paired = ReadSummary<AutoBattleSimulationSummary>(
            pairedPath);
        var evolution = ReadSummary<AutoBattleEvolutionSummary>(
            evolutionPath);
        if (paired == null && evolution == null)
        {
            return CachePresentation(
                normalizedProfile,
                pairedWrite,
                evolutionWrite,
                new AutoBattleSimulationResultPresentation());
        }
        if (evolution != null
            && (paired == null || evolution.CompletedUtc >= paired.CompletedUtc))
        {
            var last = evolution.Iterations.LastOrDefault();
            return CachePresentation(
                normalizedProfile,
                pairedWrite,
                evolutionWrite,
                new AutoBattleSimulationResultPresentation
            {
                Available = true,
                GatePassed = evolution.GatePassed,
                Title = "最近结果 · 策略进化 · "
                        + evolution.CompletedUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                Primary = "晋升 "
                          + evolution.PromotedCount
                          + "/"
                          + evolution.Iterations.Count
                          + " · 轨迹 "
                          + evolution.ReplayEpisodes
                          + " · 战斗 "
                          + evolution.CompletedBattles
                          + "/"
                          + evolution.RequestedBattles,
                Secondary = last == null
                    ? evolution.Message
                    : "末轮候选胜率 "
                      + last.CandidateWinRate.ToString("P1")
                      + " · Champion "
                      + last.ChampionWinRate.ToString("P1")
                      + " · 综合分 "
                      + last.CandidateArenaScore.ToString("0.00"),
                Detail = "门禁"
                         + (evolution.GatePassed ? "通过" : "未通过")
                         + " · Champion "
                         + CompactId(evolution.ChampionId),
                ResultDirectory = evolution.ResultDirectory
            });
        }
        paired ??= new AutoBattleSimulationSummary();
        return CachePresentation(
            normalizedProfile,
            pairedWrite,
            evolutionWrite,
            new AutoBattleSimulationResultPresentation
        {
            Available = true,
            GatePassed = paired.GatePassed,
            Title = "最近结果 · 对照评估 · "
                    + paired.CompletedUtc.ToLocalTime().ToString("MM-dd HH:mm"),
            Primary = "学习模型胜率 "
                      + paired.LearnedWinRate.ToString("P1")
                      + " · 底模 "
                      + paired.BaselineWinRate.ToString("P1")
                      + " · 差值 "
                      + (paired.LearnedWinRate - paired.BaselineWinRate).ToString("+0.0%;-0.0%;0.0%"),
            Secondary = "平均回合 "
                        + paired.LearnedMeanTurns.ToString("0.00")
                        + " · 平均生命 "
                        + paired.LearnedMeanFinalPlayerHp.ToString("0.0")
                        + " · 权威覆盖 "
                        + paired.AuthoritativeCoverage.ToString("P1"),
            Detail = "门禁"
                     + (paired.GatePassed ? "通过" : "未通过")
                     + " · 无效 "
                     + paired.InvalidPairs
                     + " · 分歧 "
                     + paired.DivergentPairs
                     + " · "
                     + paired.GateReason,
            ResultDirectory = paired.ResultDirectory
        });
    }

    public static bool CanActivateModel(
        string profile,
        string modelId,
        out string reason)
    {
        if (!AuraToolsAutoBattleModelRuntime.MeetsValidationGate(profile, out reason))
        {
            return false;
        }
        var path = LatestSummaryPath(profile);
        if (!File.Exists(path))
        {
            reason = "尚未完成同种子底模对照模拟";
            return false;
        }
        try
        {
            var summary = AuraSharedJson.Deserialize<AutoBattleSimulationSummary>(
                File.ReadAllText(path));
            if (summary == null
                || !summary.GatePassed
                || !string.Equals(summary.ModelId, modelId, StringComparison.Ordinal))
            {
                reason = summary?.GateReason ?? "模拟评估结果与当前模型不匹配";
                return false;
            }
            reason = "分组验证和配对模拟门禁均已通过";
            return true;
        }
        catch (Exception ex)
        {
            reason = "读取模拟门禁失败：" + ex.Message;
            return false;
        }
    }

    internal static CombatDecisionProfile BuildDecisionProfile(AutoBattleSettings settings)
    {
        var profile = new CombatDecisionProfile
        {
            SearchSimulationBudget = settings.SearchSimulationBudget,
            SearchNodeBudget = settings.SearchNodeBudget,
            SearchMaxPly = settings.SearchMaxPly,
            UseChancePuct = true
        };
        switch (settings.Profile)
        {
            case "aggressive":
                profile.Id = "aggressive";
                profile.Weights.Lethal = 2.1d;
                profile.Weights.Tempo = 1.25d;
                profile.Weights.Survival = 0.85d;
                profile.ThreatRiskTolerance = 0.35d;
                profile.DeathRiskLimit = 0.12d;
                profile.TailRiskPenalty = 22d;
                break;
            case "defensive":
                profile.Id = "defensive";
                profile.Weights.Survival = 1.9d;
                profile.Weights.Risk = -1.6d;
                profile.Weights.Lethal = 1.15d;
                profile.ThreatRiskTolerance = 0.9d;
                profile.SurplusDefendRetention = 0.1d;
                profile.DeathRiskLimit = 0.02d;
                profile.TailRiskPenalty = 55d;
                break;
        }
        if (string.Equals(settings.UnknownActionPolicy, "allow", StringComparison.OrdinalIgnoreCase))
        {
            profile.UnknownActionPenalty = 0.35d;
        }
        return profile;
    }

    private static SimulationWorkResult Run(
        SimulationRequest request,
        CancellationToken token)
    {
        SetStatus(
            AutoBattleSimulationStage.Resolving,
            "正在解析权威规则与场景",
            requested: request.Simulation.SimulationCount,
            scenarioId: request.Simulation.ScenarioId);
        var scenario = ResolveScenario(request.Simulation.ScenarioId);
        if (scenario == null)
        {
            return SimulationWorkResult.Failed(
                "未找到战斗场景。内容 MOD 可注册 ICombatScenarioProvider，或将 *.scenario.json 放入输入目录。",
                request);
        }
        var rulesetResult = ResolveRuleset(scenario.RulesetVersion);
        if (!rulesetResult.Success)
        {
            return SimulationWorkResult.Failed(
                "权威规则集构建失败：" + string.Join("；", rulesetResult.Errors),
                request,
                scenario.ScenarioId);
        }

        var profile = BuildDecisionProfile(request.Settings);
        var baselineFactory = new CombatDecisionSimulationPolicyFactory(profile);
        var residual = AuraToolsAutoBattleModelRuntime.Load(profile.Id, true, out var residualDiagnostic);
        var guidance = AuraToolsAutoBattleModelRuntime.LoadSearchGuidance(
            profile.Id,
            true,
            out var guidanceDiagnostic);
        var policyValue = AuraToolsAutoBattleModelRuntime.LoadPolicyValue(
            profile.Id,
            true,
            out var policyValueDiagnostic);
        var modelId = string.Join(
            "+",
            new[] { residual.ModelId, guidance.ModelId, policyValue.ModelId }
                .Where(id => !string.Equals(id, "none", StringComparison.Ordinal)));
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = "none";
        }
        var learnedFactory = new CombatDecisionSimulationPolicyFactory(
            profile,
            residual,
            guidance,
            policyValue);

        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var resultDirectory = Path.Combine(ResultsRootDirectory, runId);
        var tracesDirectory = Path.Combine(resultDirectory, "traces");
        Directory.CreateDirectory(resultDirectory);
        Directory.CreateDirectory(tracesDirectory);
        var resultsPath = Path.Combine(resultDirectory, "results.jsonl");
        var episodesPath = Path.Combine(resultDirectory, "episodes-v1.jsonl");
        var manifest = new
        {
            schemaVersion = 1,
            runId,
            createdUtc = DateTime.UtcNow,
            scenarioId = scenario.ScenarioId,
            rulesetHash = rulesetResult.Ruleset.RulesetHash,
            profile = profile.Id,
            modelId,
            request.Simulation.SimulationCount,
            request.Simulation.SeedStart,
            request.Simulation.Parallelism,
            baselinePolicy = baselineFactory.PolicyId,
            learnedPolicy = learnedFactory.PolicyId,
            residualDiagnostic,
            guidanceDiagnostic,
            policyValueDiagnostic
        };
        WriteText(
            Path.Combine(resultDirectory, "manifest.json"),
            AuraSharedJson.Serialize(manifest));

        SetStatus(
            AutoBattleSimulationStage.Running,
            "正在执行同种子底模 / 学习模型对照",
            requested: request.Simulation.SimulationCount,
            scenarioId: scenario.ScenarioId,
            resultDirectory: resultDirectory);
        var aggregate = new SimulationAggregate();
        var outputGate = new object();
        using (var writer = new StreamWriter(resultsPath, append: false))
        using (var episodeWriter = request.Simulation.CollectPolicyValueEpisodes
                   ? new StreamWriter(episodesPath, append: false)
                   : null)
        {
            try
            {
                Parallel.For(
                    0,
                    request.Simulation.SimulationCount,
                    new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = request.Simulation.Parallelism
                    },
                    index =>
                    {
                        var current = CombatScenarioCloner.Clone(scenario);
                        current.Seed = request.Simulation.SeedStart + (ulong)index;
                        var engine = new CombatSimulationEngine();
                        var baseline = engine.Run(
                            current,
                            rulesetResult.Ruleset,
                            baselineFactory.Create(),
                            token);
                        var learnedPolicy = learnedFactory.Create();
                        var episodePolicy = request.Simulation.CollectPolicyValueEpisodes
                            ? new CombatEpisodeRecordingPolicy(learnedPolicy, profile.Id)
                            : null;
                        var learned = engine.Run(
                            current,
                            rulesetResult.Ruleset,
                            episodePolicy ?? learnedPolicy,
                            token);
                        var episode = episodePolicy?.Complete(learned);
                        var pair = BuildPair(baseline, learned);
                        var retainTrace = request.Simulation.RetainDivergentTraces
                                          && (pair.Divergent
                                              || baseline.Outcome == CombatSimulationOutcome.Invalid
                                              || learned.Outcome == CombatSimulationOutcome.Invalid
                                              || learned.Outcome == CombatSimulationOutcome.Defeat);
                        lock (outputGate)
                        {
                            aggregate.Add(
                                pair,
                                baseline,
                                learned,
                                request.Simulation.MinimumAuthoritativeCoverage);
                            writer.WriteLine(AuraSharedJson.SerializeCompact(pair));
                            if (episode != null)
                            {
                                episodeWriter?.WriteLine(AuraSharedJson.SerializeCompact(episode));
                            }
                            if ((aggregate.CompletedPairs & 15) == 0)
                            {
                                writer.Flush();
                                episodeWriter?.Flush();
                            }
                            if ((aggregate.CompletedPairs & 7) == 0
                                || aggregate.CompletedPairs
                                == request.Simulation.SimulationCount)
                            {
                                SetStatus(
                                    AutoBattleSimulationStage.Running,
                                    "正在执行同种子底模 / 学习模型对照",
                                    aggregate.CompletedPairs,
                                    request.Simulation.SimulationCount,
                                    scenario.ScenarioId,
                                    resultDirectory);
                            }
                        }
                        if (retainTrace)
                        {
                            WriteText(
                                Path.Combine(tracesDirectory, current.Seed + ".json"),
                                AuraSharedJson.Serialize(new { baseline, learned }));
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                aggregate.Cancelled = true;
            }
        }

        SetStatus(
            AutoBattleSimulationStage.Writing,
            "正在汇总模拟结果",
            aggregate.CompletedPairs,
            request.Simulation.SimulationCount,
            scenario.ScenarioId,
            resultDirectory);
        var summary = aggregate.ToSummary(
            runId,
            scenario.ScenarioId,
            profile.Id,
            modelId,
            rulesetResult.Ruleset.RulesetHash,
            request.Simulation);
        summary.ResultDirectory = resultDirectory;
        var summaryPath = Path.Combine(resultDirectory, "summary.json");
        WriteText(summaryPath, AuraSharedJson.Serialize(summary));
        WriteText(
            Path.Combine(resultDirectory, "status.json"),
            AuraSharedJson.Serialize(new
            {
                stage = summary.Cancelled ? "cancelled" : "completed",
                summary.CompletedPairs,
                summary.RequestedPairs,
                summary.GatePassed,
                summary.GateReason,
                updatedUtc = DateTime.UtcNow
            }));
        if (!summary.Cancelled)
        {
            WriteText(LatestSummaryPath(profile.Id), AuraSharedJson.Serialize(summary));
        }

        var persistedSummary = AuraSharedJson.Deserialize<AutoBattleSimulationSummary>(
                                   File.ReadAllText(summaryPath))
                               ?? summary;
        var message = persistedSummary.Cancelled
            ? "模拟评估已取消，已完成 " + persistedSummary.CompletedPairs + "/" + persistedSummary.RequestedPairs
            : "模拟完成：学习模型胜率 "
              + persistedSummary.LearnedWinRate.ToString("P1")
              + "，底模胜率 " + persistedSummary.BaselineWinRate.ToString("P1")
              + "，权威覆盖 " + persistedSummary.AuthoritativeCoverage.ToString("P1")
              + "，门禁" + (persistedSummary.GatePassed ? "通过" : "未通过");
        return new SimulationWorkResult
        {
            Success = !persistedSummary.Cancelled,
            Cancelled = persistedSummary.Cancelled,
            Message = message,
            CompletedPairs = persistedSummary.CompletedPairs,
            RequestedPairs = persistedSummary.RequestedPairs,
            ScenarioId = persistedSummary.ScenarioId,
            ResultDirectory = resultDirectory,
            GatePassed = persistedSummary.GatePassed
        };
    }

    private static SimulationWorkResult RunEvolution(
        SimulationRequest request,
        CancellationToken token)
    {
        var requestedBattles = EvolutionBattleCount(request.Simulation);
        SetStatus(
            AutoBattleSimulationStage.Resolving,
            "正在解析策略进化所需的权威规则与场景",
            requested: requestedBattles,
            scenarioId: request.Simulation.ScenarioId);
        var selected = ResolveScenario(request.Simulation.ScenarioId);
        if (selected == null)
        {
            return SimulationWorkResult.Failed(
                "未找到战斗场景，无法启动策略进化训练",
                request,
                requestedBattles: requestedBattles);
        }
        var rulesetResult = ResolveRuleset(selected.RulesetVersion);
        if (!rulesetResult.Success)
        {
            return SimulationWorkResult.Failed(
                "权威规则集构建失败：" + string.Join("；", rulesetResult.Errors),
                request,
                selected.ScenarioId,
                requestedBattles);
        }

        var profile = BuildDecisionProfile(request.Settings);
        var scenarios = ResolveEvolutionScenarios(selected);
        var initialChampion =
            AuraToolsAutoBattleModelRuntime.LoadPolicyValueDefinition(profile.Id);
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-evolution";
        var resultDirectory = Path.Combine(ResultsRootDirectory, runId);
        Directory.CreateDirectory(resultDirectory);
        SetStatus(
            AutoBattleSimulationStage.Running,
            "正在生成完整战斗轨迹",
            requested: requestedBattles,
            scenarioId: selected.ScenarioId,
            resultDirectory: resultDirectory);

        CombatPolicyEvolutionResult evolution;
        try
        {
            var evolutionRequest = new CombatPolicyEvolutionRequest
            {
                DecisionProfile = profile.Id,
                Iterations = request.Simulation.EvolutionIterations,
                TrainingEpisodesPerIteration =
                    request.Simulation.EvolutionEpisodesPerIteration,
                ArenaEpisodesPerIteration = request.Simulation.EvolutionArenaEpisodes,
                SeedStart = request.Simulation.SeedStart,
                MaximumWinRateRegression =
                    request.Simulation.MaximumWinRateRegression,
                Profile = profile,
                Training = new CombatPolicyValueTrainingOptions
                {
                    Epochs = request.Settings.Training.Epochs,
                    LearningRate = Math.Min(
                        0.02d,
                        request.Settings.Training.LearningRate),
                    L2 = request.Settings.Training.L2,
                    HiddenDimensions =
                        request.Settings.Training.PolicyValueHiddenDimensions,
                    MinimumEpisodes = request.Settings.Training.MinimumEpisodes
                }.Normalized(),
                Scenarios = scenarios,
                Progress = (completed, requested, message) =>
                    SetStatus(
                        AutoBattleSimulationStage.Running,
                        message,
                        completed,
                        requested,
                        selected.ScenarioId,
                        resultDirectory)
            };
            evolution = new CombatPolicyEvolutionRunner().Run(
                evolutionRequest,
                rulesetResult.Ruleset,
                initialChampion,
                token);
        }
        catch (OperationCanceledException)
        {
            WriteText(
                Path.Combine(resultDirectory, "status.json"),
                AuraSharedJson.Serialize(new
                {
                    stage = "cancelled",
                    updatedUtc = DateTime.UtcNow
                }));
            return new SimulationWorkResult
            {
                Cancelled = true,
                Message = "策略进化训练已取消",
                RequestedPairs = requestedBattles,
                ScenarioId = selected.ScenarioId,
                ResultDirectory = resultDirectory
            };
        }

        var completedBattles = Math.Min(
            requestedBattles,
            Math.Max(evolution.Replay.Count, GetStatus().CompletedPairs));
        SetStatus(
            AutoBattleSimulationStage.Writing,
            "正在写入策略进化结果与长期训练轨迹",
            completedBattles,
            requestedBattles,
            selected.ScenarioId,
            resultDirectory);
        var episodesPath = Path.Combine(resultDirectory, "episodes-v1.jsonl");
        using (var writer = new StreamWriter(episodesPath, append: false))
        {
            foreach (var episode in evolution.Replay)
            {
                writer.WriteLine(AuraSharedJson.SerializeCompact(episode));
            }
        }
        var promotedCount = evolution.Iterations.Count(item => item.Promoted);
        string candidatePath = "";
        if (evolution.Success && evolution.Champion != null)
        {
            candidatePath = AuraToolsAutoBattleModelRuntime.WritePolicyValueCandidate(
                profile.Id,
                evolution.Champion);
        }
        var gatePassed = evolution.Success
                         && evolution.Champion != null
                         && promotedCount > 0;
        var evolutionSummary = new AutoBattleEvolutionSummary
        {
            RunId = runId,
            CompletedUtc = DateTime.UtcNow,
            Profile = profile.Id,
            ScenarioIds = scenarios.Select(item => item.ScenarioId).ToArray(),
            RulesetHash = rulesetResult.Ruleset.RulesetHash,
            InitialChampionId = initialChampion?.ModelId ?? "none",
            ChampionId = evolution.Champion?.ModelId ?? "none",
            CandidatePath = candidatePath,
            ResultDirectory = resultDirectory,
            RequestedBattles = requestedBattles,
            CompletedBattles = completedBattles,
            ReplayEpisodes = evolution.Replay.Count,
            PromotedCount = promotedCount,
            GatePassed = gatePassed,
            Message = evolution.Message,
            Iterations = evolution.Iterations
        };
        var evolutionSummaryText = AuraSharedJson.Serialize(evolutionSummary);
        WriteText(
            Path.Combine(resultDirectory, "evolution-summary.json"),
            evolutionSummaryText);
        WriteText(LatestEvolutionPath(profile.Id), evolutionSummaryText);
        WriteText(
            Path.Combine(resultDirectory, "status.json"),
            AuraSharedJson.Serialize(new
            {
                stage = evolution.Success ? "completed" : "failed",
                gatePassed,
                replayEpisodes = evolution.Replay.Count,
                promotedCount,
                updatedUtc = DateTime.UtcNow
            }));
        var message = evolution.Message
                      + "；完整轨迹="
                      + evolution.Replay.Count
                      + "；候选="
                      + (string.IsNullOrWhiteSpace(candidatePath)
                          ? "未生成"
                          : Path.GetFileName(candidatePath));
        return new SimulationWorkResult
        {
            Success = evolution.Success,
            Message = message,
            CompletedPairs = completedBattles,
            RequestedPairs = requestedBattles,
            ScenarioId = selected.ScenarioId,
            ResultDirectory = resultDirectory,
            GatePassed = gatePassed
        };
    }

    private static List<CombatScenarioDefinition> ResolveEvolutionScenarios(
        CombatScenarioDefinition selected)
    {
        var scenarios = CombatSimulationRegistry.SnapshotScenarios()
            .Where(item => string.Equals(
                item.RulesetVersion,
                selected.RulesetVersion,
                StringComparison.OrdinalIgnoreCase))
            .Select(CombatScenarioCloner.Clone)
            .ToList();
        foreach (var path in ScenarioFiles())
        {
            try
            {
                var scenario =
                    AuraSharedJson.Deserialize<CombatScenarioDefinition>(
                        File.ReadAllText(path));
                if (scenario != null
                    && string.Equals(
                        scenario.RulesetVersion,
                        selected.RulesetVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    scenarios.Add(scenario);
                }
            }
            catch
            {
                // The selected scenario path reports parse failures during resolution.
            }
        }
        scenarios.Add(CombatScenarioCloner.Clone(selected));
        return scenarios
            .GroupBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.ScenarioId, StringComparer.Ordinal)
            .ToList();
    }

    private static int EvolutionBattleCount(AutoBattleSimulationSettings settings)
    {
        return settings.EvolutionIterations
               * (settings.EvolutionEpisodesPerIteration
                  + settings.EvolutionArenaEpisodes * 2);
    }

    private static CombatScenarioDefinition? ResolveScenario(string scenarioId)
    {
        var registered = CombatSimulationRegistry.SnapshotScenarios();
        var selected = registered.FirstOrDefault(scenario =>
            string.Equals(scenario.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        if (selected != null)
        {
            return selected;
        }
        if (string.IsNullOrWhiteSpace(scenarioId) && registered.Count > 0)
        {
            return registered[0];
        }

        Directory.CreateDirectory(InputDirectory);
        foreach (var path in ScenarioFiles())
        {
            var candidate = AuraSharedJson.Deserialize<CombatScenarioDefinition>(File.ReadAllText(path));
            if (candidate != null
                && (string.IsNullOrWhiteSpace(scenarioId)
                    || string.Equals(
                        candidate.ScenarioId,
                        scenarioId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
        return null;
    }

    private static CombatRulesetBuildResult ResolveRuleset(string version)
    {
        var registered = CombatSimulationRegistry.BuildRuleset(version);
        if (registered.Success
            && (registered.Ruleset.CardCount > 0
                || registered.Ruleset.EnemyCount > 0
                || registered.Ruleset.StatusCount > 0))
        {
            return registered;
        }

        var path = Path.Combine(InputDirectory, "ruleset.json");
        if (!File.Exists(path))
        {
            return registered.Success
                ? new CombatRulesetBuildResult
                {
                    Errors = { "没有已注册规则，且输入目录中不存在 ruleset.json" }
                }
                : registered;
        }
        var document = AuraSharedJson.Deserialize<CombatRulesetDocument>(File.ReadAllText(path));
        return CombatSimulationRegistry.BuildRuleset(document);
    }

    private static AutoBattleSimulationPair BuildPair(
        CombatSimulationResult baseline,
        CombatSimulationResult learned)
    {
        return new AutoBattleSimulationPair
        {
            Seed = baseline.Seed,
            Divergent = baseline.Outcome != learned.Outcome
                        || baseline.Turns != learned.Turns
                        || baseline.FinalPlayerHp != learned.FinalPlayerHp
                        || !string.Equals(
                            baseline.FinalStateHash,
                            learned.FinalStateHash,
                            StringComparison.Ordinal),
            Baseline = Compact(baseline),
            Learned = Compact(learned)
        };
    }

    private static AutoBattleSimulationResult Compact(CombatSimulationResult result)
    {
        return new AutoBattleSimulationResult
        {
            Outcome = result.Outcome.ToString(),
            TerminationReason = result.TerminationReason.ToString(),
            Turns = result.Turns,
            FinalPlayerHp = result.FinalPlayerHp,
            SemanticCoverage = result.SemanticCoverage,
            DamageDealt = result.Metrics.DamageDealt,
            DamageTaken = result.Metrics.DamageTaken,
            CardsPlayed = result.Metrics.CardsPlayed,
            FinalStateHash = result.FinalStateHash
        };
    }

    private static SimulationRequest Snapshot(AutoBattleSettings source)
    {
        return new SimulationRequest
        {
            Settings = new AutoBattleSettings
            {
                Profile = source.Profile,
                UnknownActionPolicy = source.UnknownActionPolicy,
                SearchSimulationBudget = source.SearchSimulationBudget,
                SearchNodeBudget = source.SearchNodeBudget,
                SearchMaxPly = source.SearchMaxPly,
                Training = new AutoBattleTrainingSettings
                {
                    Preset = source.Training.Preset,
                    Epochs = source.Training.Epochs,
                    LearningRate = source.Training.LearningRate,
                    L2 = source.Training.L2,
                    MaximumCorrection = source.Training.MaximumCorrection,
                    MinimumPreferencePairs = source.Training.MinimumPreferencePairs,
                    MinimumCategoryObservations = source.Training.MinimumCategoryObservations,
                    MinimumEpisodes = source.Training.MinimumEpisodes,
                    PolicyValueHiddenDimensions = source.Training.PolicyValueHiddenDimensions
                }
            },
            Simulation = new AutoBattleSimulationSettings
            {
                ScenarioId = source.Simulation.ScenarioId,
                SimulationCount = source.Simulation.SimulationCount,
                Parallelism = source.Simulation.Parallelism,
                SeedStart = source.Simulation.SeedStart,
                RetainDivergentTraces = source.Simulation.RetainDivergentTraces,
                CollectPolicyValueEpisodes = source.Simulation.CollectPolicyValueEpisodes,
                EvolutionIterations = source.Simulation.EvolutionIterations,
                EvolutionEpisodesPerIteration = source.Simulation.EvolutionEpisodesPerIteration,
                EvolutionArenaEpisodes = source.Simulation.EvolutionArenaEpisodes,
                MinimumAuthoritativeCoverage = source.Simulation.MinimumAuthoritativeCoverage,
                MaximumWinRateRegression = source.Simulation.MaximumWinRateRegression
            }
        };
    }

    private static string LatestSummaryPath(string profile)
    {
        Directory.CreateDirectory(ResultsRootDirectory);
        return Path.Combine(
            ResultsRootDirectory,
            "latest-summary-" + (profile ?? "balanced").Trim().ToLowerInvariant() + ".json");
    }

    private static string LatestEvolutionPath(string profile)
    {
        Directory.CreateDirectory(ResultsRootDirectory);
        return Path.Combine(
            ResultsRootDirectory,
            "latest-evolution-"
            + (profile ?? "balanced").Trim().ToLowerInvariant()
            + ".json");
    }

    private static T? ReadSummary<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return AuraSharedJson.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn(
                "[AutoBattle][Simulation] 读取结果摘要失败："
                + Path.GetFileName(path)
                + "；"
                + ex.Message);
            return null;
        }
    }

    private static string CompactId(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
        return text.Length <= 28 ? text : text.Substring(0, 25) + "...";
    }

    private static AutoBattleSimulationResultPresentation CachePresentation(
        string profile,
        DateTime pairedWriteUtc,
        DateTime evolutionWriteUtc,
        AutoBattleSimulationResultPresentation presentation)
    {
        lock (ResultCacheGate)
        {
            ResultCache[profile] = new ResultPresentationCacheEntry
            {
                PairedWriteUtc = pairedWriteUtc,
                EvolutionWriteUtc = evolutionWriteUtc,
                Presentation = presentation
            };
        }
        return presentation;
    }

    private static void WriteText(string path, string text)
    {
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(path, text, createBackup: false);
    }

    private static IEnumerable<string> ScenarioFiles()
    {
        Directory.CreateDirectory(InputDirectory);
        return Directory.EnumerateFiles(InputDirectory, "*.scenario.json")
            .Concat(new[] { Path.Combine(InputDirectory, "scenario.json") })
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void SetStatus(
        AutoBattleSimulationStage stage,
        string message,
        int completed = 0,
        int requested = 0,
        string scenarioId = "",
        string resultDirectory = "",
        bool gatePassed = false)
    {
        lock (Gate)
        {
            var operation = status.Operation;
            var progressUnit = status.ProgressUnit;
            status = new AutoBattleSimulationStatus
            {
                Stage = stage,
                Operation = operation,
                Message = message ?? "",
                CompletedPairs = completed,
                RequestedPairs = requested,
                ScenarioId = scenarioId ?? "",
                ResultDirectory = resultDirectory ?? "",
                GatePassed = gatePassed,
                ProgressUnit = progressUnit,
                UpdatedUtc = DateTime.UtcNow
            };
        }
    }

    private sealed class SimulationRequest
    {
        public AutoBattleSettings Settings { get; set; } = new();

        public AutoBattleSimulationSettings Simulation { get; set; } = new();
    }

    private sealed class ResultPresentationCacheEntry
    {
        public DateTime PairedWriteUtc { get; set; }

        public DateTime EvolutionWriteUtc { get; set; }

        public AutoBattleSimulationResultPresentation Presentation { get; set; } = new();
    }

    private sealed class SimulationWorkResult
    {
        public bool Success { get; set; }

        public bool Cancelled { get; set; }

        public string Message { get; set; } = "";

        public int CompletedPairs { get; set; }

        public int RequestedPairs { get; set; }

        public string ScenarioId { get; set; } = "";

        public string ResultDirectory { get; set; } = "";

        public bool GatePassed { get; set; }

        public static SimulationWorkResult Failed(
            string message,
            SimulationRequest request,
            string scenarioId = "",
            int? requestedBattles = null)
        {
            return new SimulationWorkResult
            {
                Message = message,
                RequestedPairs = requestedBattles
                                 ?? request.Simulation.SimulationCount,
                ScenarioId = scenarioId
            };
        }
    }

    private sealed class AutoBattleSimulationPair
    {
        public ulong Seed { get; set; }

        public bool Divergent { get; set; }

        public AutoBattleSimulationResult Baseline { get; set; } = new();

        public AutoBattleSimulationResult Learned { get; set; } = new();
    }

    private sealed class AutoBattleSimulationResult
    {
        public string Outcome { get; set; } = "";

        public string TerminationReason { get; set; } = "";

        public int Turns { get; set; }

        public int FinalPlayerHp { get; set; }

        public double SemanticCoverage { get; set; }

        public int DamageDealt { get; set; }

        public int DamageTaken { get; set; }

        public int CardsPlayed { get; set; }

        public string FinalStateHash { get; set; } = "";
    }

    private sealed class SimulationAggregate
    {
        private double baselineTurns;
        private double learnedTurns;
        private double baselineHp;
        private double learnedHp;

        public int CompletedPairs { get; private set; }

        public int AuthoritativePairs { get; private set; }

        public int InvalidPairs { get; private set; }

        public int DivergentPairs { get; private set; }

        public int BaselineVictories { get; private set; }

        public int LearnedVictories { get; private set; }

        public bool Cancelled { get; set; }

        public void Add(
            AutoBattleSimulationPair pair,
            CombatSimulationResult baseline,
            CombatSimulationResult learned,
            double requiredCoverage)
        {
            CompletedPairs++;
            if (pair.Divergent)
            {
                DivergentPairs++;
            }
            if (baseline.Outcome == CombatSimulationOutcome.Invalid
                || learned.Outcome == CombatSimulationOutcome.Invalid)
            {
                InvalidPairs++;
            }
            if (baseline.SemanticCoverage + 1e-9d >= requiredCoverage
                && learned.SemanticCoverage + 1e-9d >= requiredCoverage
                && baseline.Outcome != CombatSimulationOutcome.Invalid
                && learned.Outcome != CombatSimulationOutcome.Invalid)
            {
                AuthoritativePairs++;
                if (baseline.Outcome == CombatSimulationOutcome.Victory)
                {
                    BaselineVictories++;
                }
                if (learned.Outcome == CombatSimulationOutcome.Victory)
                {
                    LearnedVictories++;
                }
                baselineTurns += baseline.Turns;
                learnedTurns += learned.Turns;
                baselineHp += baseline.FinalPlayerHp;
                learnedHp += learned.FinalPlayerHp;
            }
        }

        public AutoBattleSimulationSummary ToSummary(
            string runId,
            string scenarioId,
            string profile,
            string modelId,
            string rulesetHash,
            AutoBattleSimulationSettings settings)
        {
            var coverage = settings.SimulationCount == 0
                ? 0d
                : (double)AuthoritativePairs / settings.SimulationCount;
            var baselineWinRate = AuthoritativePairs == 0
                ? 0d
                : (double)BaselineVictories / AuthoritativePairs;
            var learnedWinRate = AuthoritativePairs == 0
                ? 0d
                : (double)LearnedVictories / AuthoritativePairs;
            var coveragePassed = coverage + 1e-9d >= settings.MinimumAuthoritativeCoverage;
            var regressionPassed = learnedWinRate + settings.MaximumWinRateRegression + 1e-9d
                                   >= baselineWinRate;
            var complete = !Cancelled && CompletedPairs == settings.SimulationCount;
            var gatePassed = complete && AuthoritativePairs > 0 && coveragePassed && regressionPassed;
            return new AutoBattleSimulationSummary
            {
                RunId = runId,
                ScenarioId = scenarioId,
                Profile = profile,
                ModelId = modelId,
                RulesetHash = rulesetHash,
                RequestedPairs = settings.SimulationCount,
                CompletedPairs = CompletedPairs,
                AuthoritativePairs = AuthoritativePairs,
                InvalidPairs = InvalidPairs,
                DivergentPairs = DivergentPairs,
                BaselineVictories = BaselineVictories,
                LearnedVictories = LearnedVictories,
                AuthoritativeCoverage = coverage,
                BaselineWinRate = baselineWinRate,
                LearnedWinRate = learnedWinRate,
                BaselineMeanTurns = AuthoritativePairs == 0 ? 0d : baselineTurns / AuthoritativePairs,
                LearnedMeanTurns = AuthoritativePairs == 0 ? 0d : learnedTurns / AuthoritativePairs,
                BaselineMeanFinalPlayerHp = AuthoritativePairs == 0 ? 0d : baselineHp / AuthoritativePairs,
                LearnedMeanFinalPlayerHp = AuthoritativePairs == 0 ? 0d : learnedHp / AuthoritativePairs,
                GatePassed = gatePassed,
                GateReason = !complete
                    ? "模拟未完整完成"
                    : !coveragePassed
                        ? "权威语义覆盖率不足"
                        : !regressionPassed
                            ? "学习模型胜率回退超过阈值"
                            : AuthoritativePairs == 0
                                ? "没有权威有效样本"
                                : "通过",
                Cancelled = Cancelled,
                CompletedUtc = DateTime.UtcNow
            };
        }
    }
}
