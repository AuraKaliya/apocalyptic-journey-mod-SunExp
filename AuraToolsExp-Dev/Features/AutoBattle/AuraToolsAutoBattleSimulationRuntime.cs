using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
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

    public string DifficultyId { get; set; } = "normal";

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

    public string DifficultyId { get; set; } = "normal";

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

    public int RawBaselineVictories { get; set; }

    public int RawLearnedVictories { get; set; }

    public double RawBaselineWinRate { get; set; }

    public double RawLearnedWinRate { get; set; }

    public int BaselineReachedFinalBoss { get; set; }

    public int LearnedReachedFinalBoss { get; set; }

    public double BaselineMeanCompletedBattles { get; set; }

    public double LearnedMeanCompletedBattles { get; set; }

    public int BaselineMaximumCompletedBattles { get; set; }

    public int LearnedMaximumCompletedBattles { get; set; }

    public bool FormalRatesAvailable => AuthoritativePairs > 0;

    public double AuthoritativeCoverage { get; set; }

    public double BaselineWinRate { get; set; }

    public double LearnedWinRate { get; set; }

    public double RequiredLearnedWinRate { get; set; }

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
    private static readonly object FoundationPackageCacheGate = new();
    private static readonly Dictionary<string, ResultPresentationCacheEntry> ResultCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static FoundationPackageCacheEntry? foundationPackageCache;
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
        foreach (var path in JourneyFiles())
        {
            try
            {
                var journey = AuraSharedJson.Deserialize<CombatJourneyDefinition>(
                    File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(journey?.JourneyId))
                {
                    ids.Add(journey!.JourneyId.Trim());
                }
            }
            catch
            {
                // Invalid files are reported when the selected journey is resolved.
            }
        }
        foreach (var path in CampaignFiles())
        {
            try
            {
                var campaign = AuraSharedJson.Deserialize<CombatCampaignDefinition>(
                    File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(campaign?.CampaignId))
                {
                    ids.Add(campaign!.CampaignId.Trim());
                }
            }
            catch
            {
                // Invalid files are reported when the selected campaign is resolved.
            }
        }
        return ids
            .OrderByDescending(id => id.IndexOf(
                "standard-v2",
                StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToArray();
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
        if (!TryResolveRequestedScenario(request, out message))
        {
            return false;
        }
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
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        request.Settings.Profile,
                        request.Settings.SelectedModelId,
                        force: true);
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
        if (!TryResolveRequestedScenario(request, out message))
        {
            return false;
        }
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
                    AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
                        request.Settings.Profile,
                        request.Settings.SelectedModelId,
                        force: true);
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

    public static void ResetAfterDataClear()
    {
        lock (Gate)
        {
            status = new AutoBattleSimulationStatus();
            cancellation?.Dispose();
            cancellation = null;
        }
        lock (ResultCacheGate)
        {
            ResultCache.Clear();
        }
        InvalidateFoundationPackageCache();
    }

    internal static bool TryResolveFoundationPackage(
        out CombatCampaignDefinition campaign,
        out CombatRuleset ruleset,
        out string message)
    {
        lock (FoundationPackageCacheGate)
        {
            if (foundationPackageCache != null)
            {
                campaign = foundationPackageCache.Campaign;
                ruleset = foundationPackageCache.Ruleset;
                message = "";
                return true;
            }
        }
        campaign = ResolveCampaign("witch.world-simulation.standard-v2")
                   ?? new CombatCampaignDefinition();
        if (string.IsNullOrWhiteSpace(campaign.CampaignId))
        {
            ruleset = CombatRuleset.Empty;
            message = "未找到随 MOD 发布的固定七层世界推演包";
            return false;
        }
        var build = ResolveRuleset(campaign.RulesetVersion);
        if (!build.Success)
        {
            ruleset = CombatRuleset.Empty;
            message = "底模规则集构建失败：" + string.Join("；", build.Errors);
            return false;
        }
        ruleset = build.Ruleset;
        var readinessProblems = FoundationReadinessProblems(campaign, ruleset);
        if (readinessProblems.Count > 0)
        {
            message = "底模训练尚未就绪：" + string.Join("；", readinessProblems);
            return false;
        }
        lock (FoundationPackageCacheGate)
        {
            foundationPackageCache ??= new FoundationPackageCacheEntry
            {
                Campaign = campaign,
                Ruleset = ruleset
            };
            campaign = foundationPackageCache.Campaign;
            ruleset = foundationPackageCache.Ruleset;
        }
        message = "";
        return true;
    }

    internal static bool TryGetCachedFoundationPackage(
        out CombatCampaignDefinition campaign,
        out CombatRuleset ruleset)
    {
        lock (FoundationPackageCacheGate)
        {
            if (foundationPackageCache == null)
            {
                campaign = new CombatCampaignDefinition();
                ruleset = CombatRuleset.Empty;
                return false;
            }
            campaign = foundationPackageCache.Campaign;
            ruleset = foundationPackageCache.Ruleset;
            return true;
        }
    }

    internal static void InvalidateFoundationPackageCache()
    {
        lock (FoundationPackageCacheGate)
        {
            foundationPackageCache = null;
        }
    }

    private static List<string> FoundationReadinessProblems(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        var problems = new List<string>();
        if (!campaign.RequireAuthoritativeRules)
        {
            problems.Add("世界推演包仍允许近似规则");
        }

        var requiredCardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiredEnemyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiredStatusIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cardQueue = new Queue<string>();
        var enemyQueue = new Queue<string>();
        var statusQueue = new Queue<string>();

        void RequireCard(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && requiredCardIds.Add(id))
            {
                cardQueue.Enqueue(id);
            }
        }

        void RequireEnemy(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && requiredEnemyIds.Add(id))
            {
                enemyQueue.Enqueue(id);
            }
        }

        void RequireStatus(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && requiredStatusIds.Add(id))
            {
                statusQueue.Enqueue(id);
            }
        }

        void RequireEffectDependencies(CombatSimulationEffectDefinition effect)
        {
            switch (effect.Kind)
            {
                case CombatSimulationEffectKind.AddStatus:
                case CombatSimulationEffectKind.RemoveStatus:
                case CombatSimulationEffectKind.ModifyStatusCounter:
                    RequireStatus(effect.DefinitionId);
                    break;
                case CombatSimulationEffectKind.CreateCard:
                    RequireCard(effect.DefinitionId);
                    break;
                case CombatSimulationEffectKind.SummonEnemy:
                    RequireEnemy(effect.DefinitionId);
                    break;
            }
        }

        foreach (var cardId in campaign.Player.Deck)
        {
            RequireCard(cardId);
        }
        foreach (var reward in campaign.Rewards)
        {
            if (reward.Kind == CombatCampaignRewardKind.Card)
            {
                RequireCard(reward.RewardId);
            }
            foreach (var status in reward.InitialStatuses)
            {
                RequireStatus(status.StatusId);
            }
        }
        foreach (var difficulty in campaign.Difficulties)
        {
            foreach (var cardId in difficulty.InitialDiscardCards)
            {
                RequireCard(cardId);
            }
            foreach (var status in difficulty.EnemyInitialStatuses)
            {
                RequireStatus(status.StatusId);
            }
        }
        foreach (var status in campaign.Player.InitialStatuses)
        {
            RequireStatus(status.StatusId);
        }
        foreach (var enemyId in campaign.Enemies.Select(enemy => enemy.EnemyId)
                     .Concat(campaign.Encounters.SelectMany(encounter => encounter.EnemyIds)))
        {
            RequireEnemy(enemyId);
        }

        while (cardQueue.Count > 0 || enemyQueue.Count > 0 || statusQueue.Count > 0)
        {
            while (cardQueue.Count > 0)
            {
                var cardId = cardQueue.Dequeue();
                if (ruleset.TryGetCard(cardId, out var card))
                {
                    foreach (var effect in card.Effects
                                 .Concat(card.DrawEffects)
                                 .Concat(card.DiscardEffects))
                    {
                        RequireEffectDependencies(effect);
                    }
                }
            }
            while (enemyQueue.Count > 0)
            {
                var enemyId = enemyQueue.Dequeue();
                if (ruleset.TryGetEnemy(enemyId, out var enemy))
                {
                    foreach (var status in enemy.InitialStatuses)
                    {
                        RequireStatus(status.StatusId);
                    }
                    foreach (var effect in enemy.Intents.SelectMany(intent => intent.Effects))
                    {
                        RequireEffectDependencies(effect);
                    }
                }
            }
            while (statusQueue.Count > 0)
            {
                var statusId = statusQueue.Dequeue();
                if (ruleset.TryGetStatus(statusId, out var status))
                {
                    foreach (var effect in status.Triggers
                                 .SelectMany(trigger => trigger.Effects))
                    {
                        RequireEffectDependencies(effect);
                    }
                }
            }
        }

        var missingCards = 0;
        var projectedCards = 0;
        foreach (var cardId in requiredCardIds)
        {
            if (!ruleset.TryGetCard(cardId, out var card))
            {
                missingCards++;
            }
            else if (card.Fidelity != CombatRuleFidelity.Authoritative)
            {
                projectedCards++;
            }
        }
        if (missingCards > 0 || projectedCards > 0)
        {
            problems.Add(
                "全卡包卡牌语义缺失 "
                + missingCards
                + "、非权威 "
                + projectedCards);
        }

        var missingEnemies = 0;
        var projectedEnemies = 0;
        foreach (var enemyId in requiredEnemyIds)
        {
            if (!ruleset.TryGetEnemy(enemyId, out var enemy))
            {
                missingEnemies++;
            }
            else if (enemy.Fidelity != CombatRuleFidelity.Authoritative)
            {
                projectedEnemies++;
            }
        }
        if (missingEnemies > 0 || projectedEnemies > 0)
        {
            problems.Add(
                "本体敌人意图缺失 "
                + missingEnemies
                + "、非权威 "
                + projectedEnemies);
        }

        var missingStatuses = 0;
        var projectedStatuses = 0;
        foreach (var statusId in requiredStatusIds)
        {
            if (!ruleset.TryGetStatus(statusId, out var status))
            {
                missingStatuses++;
            }
            else if (status.Fidelity != CombatRuleFidelity.Authoritative)
            {
                projectedStatuses++;
            }
        }
        if (missingStatuses > 0 || projectedStatuses > 0)
        {
            problems.Add(
                "战斗状态语义缺失 "
                + missingStatuses
                + "、非权威 "
                + projectedStatuses);
        }

        var projectedRewards = campaign.Rewards.Count(reward =>
            reward.Kind != CombatCampaignRewardKind.Card
            && reward.Fidelity != CombatRuleFidelity.Authoritative);
        if (projectedRewards > 0)
        {
            problems.Add("遗物/祝福效果仍有 " + projectedRewards + " 项非权威");
        }
        var rewardScriptFailures =
            AuraToolsNativeRewardScriptAudit.Validate(campaign);
        if (rewardScriptFailures.Count > 0)
        {
            problems.Add(
                "遗物/祝福原生脚本兼容失败 "
                + rewardScriptFailures.Count
                + " 项："
                + string.Join("；", rewardScriptFailures.Take(3)));
        }

        var missingAffixes = campaign.Difficulties
            .SelectMany(difficulty => difficulty.HardAffixes)
            .Count(affix => affix.CombatRelevant && !affix.Implemented);
        if (missingAffixes > 0)
        {
            problems.Add("高级难度仍有 " + missingAffixes + " 个战斗词条未实现");
        }
        return problems;
    }

    public static void OpenInputDirectory()
    {
        Directory.CreateDirectory(InputDirectory);
        FileResourceUtil.OpenDirectory(InputDirectory);
    }

    public static void OpenResultDirectory(
        string profile = "balanced",
        string modelId = "")
    {
        var current = GetStatus();
        var presentation = GetResultPresentation(profile, modelId);
        var path = Directory.Exists(current.ResultDirectory)
            ? current.ResultDirectory
            : Directory.Exists(presentation.ResultDirectory)
                ? presentation.ResultDirectory
            : ResultsRootDirectory;
        Directory.CreateDirectory(path);
        FileResourceUtil.OpenDirectory(path);
    }

    public static AutoBattleSimulationResultPresentation GetResultPresentation(
        string profile,
        string modelId = "")
    {
        var normalizedProfile = string.IsNullOrWhiteSpace(profile)
            ? "balanced"
            : profile.Trim().ToLowerInvariant();
        var requestedModelId = string.IsNullOrWhiteSpace(modelId)
            ? AuraToolsAutoBattleModelRuntime.CandidateModelId(normalizedProfile)
            : modelId.Trim();
        var useModelSpecific = !string.Equals(
            requestedModelId,
            "none",
            StringComparison.Ordinal);
        var cacheKey = normalizedProfile + "|" + requestedModelId;
        var normalPath = LatestSummaryPath(
            normalizedProfile,
            "normal",
            useModelSpecific ? requestedModelId : "");
        var advancedPath = LatestSummaryPath(
            normalizedProfile,
            "advanced",
            useModelSpecific ? requestedModelId : "");
        var pairedPath = normalPath;
        var evolutionPath = LatestEvolutionPath(normalizedProfile);
        var pairedWrite = File.Exists(pairedPath)
            ? File.GetLastWriteTimeUtc(pairedPath)
            : DateTime.MinValue;
        var advancedWrite = File.Exists(advancedPath)
            ? File.GetLastWriteTimeUtc(advancedPath)
            : DateTime.MinValue;
        if (advancedWrite > pairedWrite)
        {
            pairedWrite = advancedWrite;
        }
        var evolutionWrite = File.Exists(evolutionPath)
            ? File.GetLastWriteTimeUtc(evolutionPath)
            : DateTime.MinValue;
        lock (ResultCacheGate)
        {
            if (ResultCache.TryGetValue(cacheKey, out var cached)
                && cached.PairedWriteUtc == pairedWrite
                && cached.EvolutionWriteUtc == evolutionWrite)
            {
                return cached.Presentation;
            }
        }
        var paired = ReadSummary<AutoBattleSimulationSummary>(
            pairedPath);
        var advanced = ReadSummary<AutoBattleSimulationSummary>(advancedPath);
        if (paired == null && advanced != null)
        {
            paired = advanced;
        }
        var evolution = ReadSummary<AutoBattleEvolutionSummary>(
            evolutionPath);
        if (paired == null && evolution == null)
        {
            return CachePresentation(
                cacheKey,
                pairedWrite,
                evolutionWrite,
                new AutoBattleSimulationResultPresentation());
        }
        if (evolution != null
            && (paired == null || evolution.CompletedUtc >= paired.CompletedUtc))
        {
            var last = evolution.Iterations.LastOrDefault();
            return CachePresentation(
                cacheKey,
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
        var formalAvailable = paired.AuthoritativePairs > 0;
        return CachePresentation(
            cacheKey,
            pairedWrite,
            evolutionWrite,
            new AutoBattleSimulationResultPresentation
        {
            Available = true,
            GatePassed = paired.GatePassed,
            Title = "最近结果 · 世界推演评估 · "
                    + paired.CompletedUtc.ToLocalTime().ToString("MM-dd HH:mm"),
            Primary = formalAvailable
                ? "正式有效样本 "
                  + paired.AuthoritativePairs
                  + "/"
                  + paired.CompletedPairs
                  + " · 学习模型 "
                  + paired.LearnedWinRate.ToString("P1")
                  + " · 底模 "
                  + paired.BaselineWinRate.ToString("P1")
                : "正式胜率不可用 · 正式有效样本 0/"
                  + paired.CompletedPairs
                  + "（不能显示为 0% 胜率）",
            Secondary = "探索性原始通关：学习模型 "
                        + paired.RawLearnedVictories
                        + "/"
                        + paired.CompletedPairs
                        + "（"
                        + paired.RawLearnedWinRate.ToString("P1")
                        + "） · 底模 "
                        + paired.RawBaselineVictories
                        + "/"
                        + paired.CompletedPairs
                        + "（"
                        + paired.RawBaselineWinRate.ToString("P1")
                        + "） · 学习模型最远 "
                        + paired.LearnedMaximumCompletedBattles
                        + "/37 战",
            Detail = "验证门槛"
                     + (paired.GatePassed ? "通过" : "未通过")
                     + " · 普通"
                     + ValidationBadge(ReadSummary<AutoBattleSimulationSummary>(normalPath), paired.ModelId)
                     + " · 高级"
                     + ValidationBadge(advanced, paired.ModelId)
                     + " · 当前难度验证线 "
                     + paired.RequiredLearnedWinRate.ToString("P0")
                     + " · 规则覆盖 "
                     + paired.AuthoritativeCoverage.ToString("P1")
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
        var candidateModelId = AuraToolsAutoBattleModelRuntime.CandidateModelId(profile);
        var candidatePromotion = !string.Equals(
            candidateModelId,
            "none",
            StringComparison.Ordinal)
                                 && string.Equals(
                                     candidateModelId,
                                     modelId,
                                     StringComparison.Ordinal);
        if (candidatePromotion
                ? !AuraToolsAutoBattleModelRuntime.CandidateMeetsValidationGate(
                    profile,
                    out reason)
                : !AuraToolsAutoBattleModelRuntime.MeetsValidationGate(
                    profile,
                    out reason))
        {
            return false;
        }
        var normalPath = LatestSummaryPath(profile, "normal", modelId);
        var advancedPath = LatestSummaryPath(profile, "advanced", modelId);
        var paths = new[]
            {
                normalPath,
                advancedPath
            }
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            reason = "尚未完成同种子底模对照模拟";
            return false;
        }
        try
        {
            var summaries = paths
                .Select(path => AuraSharedJson.Deserialize<AutoBattleSimulationSummary>(
                    File.ReadAllText(path)))
                .Where(item => item != null)
                .Cast<AutoBattleSimulationSummary>()
                .ToList();
            var passed = summaries.Any(summary =>
                summary.GatePassed
                && string.Equals(summary.ModelId, modelId, StringComparison.Ordinal));
            if (!passed)
            {
                reason = summaries
                             .OrderByDescending(item => item.CompletedUtc)
                             .FirstOrDefault()?.GateReason
                         ?? "模拟评估结果与当前模型不匹配";
                return false;
            }
            if (!AuraToolsAutoBattleGameValidationRuntime.CanPromote(
                    profile,
                    modelId,
                    out reason))
            {
                return false;
            }
            reason = "分组验证、外部同种子评测与游戏主体回执均已通过";
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
            SearchBudgetMode = "dynamic",
            SearchQuality = settings.SearchQuality
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
        var campaign = ResolveCampaign(request.Simulation.ScenarioId);
        var journey = campaign == null
            ? ResolveJourney(request.Simulation.ScenarioId)
            : null;
        var scenario = campaign == null && journey == null
            ? ResolveScenario(request.Simulation.ScenarioId)
            : null;
        if (scenario == null && journey == null && campaign == null)
        {
            return SimulationWorkResult.Failed(
                "未找到评估场景。请检查随 MOD 发布的 *.campaign.json，或将自定义旅程/场景放入高级输入目录。",
                request);
        }
        var scenarioId = campaign?.CampaignId ?? journey?.JourneyId ?? scenario!.ScenarioId;
        var rulesetVersion = campaign?.RulesetVersion
                             ?? journey?.RulesetVersion
                             ?? scenario!.RulesetVersion;
        var rulesetResult = ResolveRuleset(rulesetVersion);
        if (!rulesetResult.Success)
        {
            return SimulationWorkResult.Failed(
                "权威规则集构建失败：" + string.Join("；", rulesetResult.Errors),
                request,
                scenarioId);
        }

        var profile = BuildDecisionProfile(request.Settings);
        var baselineFactory = new CombatDecisionSimulationPolicyFactory(profile);
        IDecisionResidualModel residual;
        ICombatSearchGuidanceModel guidance;
        ICombatPolicyValueModel policyValue;
        string modelId;
        string residualDiagnostic;
        string guidanceDiagnostic;
        string policyValueDiagnostic;
        if (!string.IsNullOrWhiteSpace(request.Settings.SelectedModelId)
            && AuraToolsAutoBattleModelRuntime.TryLoadLibraryModel(
                profile.Id,
                request.Settings.SelectedModelId,
                out residual,
                out guidance,
                out policyValue,
                out modelId,
                out var libraryDiagnostic))
        {
            residualDiagnostic = libraryDiagnostic;
            guidanceDiagnostic = "模型库内搜索引导";
            policyValueDiagnostic = "模型库内长期策略价值";
        }
        else if (AuraToolsAutoBattleModelRuntime.TryLoadCandidate(
                profile.Id,
                out residual,
                out guidance,
                out policyValue,
                out modelId,
                out var candidateDiagnostic))
        {
            residualDiagnostic = candidateDiagnostic;
            guidanceDiagnostic = "候选包内搜索引导";
            policyValueDiagnostic = "候选包内长期策略价值";
        }
        else
        {
            residual = AuraToolsAutoBattleModelRuntime.Load(
                profile.Id,
                true,
                out residualDiagnostic,
                request.Settings.SelectedModelId);
            guidance = AuraToolsAutoBattleModelRuntime.LoadSearchGuidance(
                profile.Id,
                true,
                out guidanceDiagnostic,
                request.Settings.SelectedModelId);
            policyValue = AuraToolsAutoBattleModelRuntime.LoadPolicyValue(
                profile.Id,
                true,
                out policyValueDiagnostic,
                request.Settings.SelectedModelId);
            modelId = string.Join(
                "+",
                new[] { residual.ModelId, guidance.ModelId, policyValue.ModelId }
                    .Where(id => !string.Equals(id, "none", StringComparison.Ordinal)));
            if (string.IsNullOrWhiteSpace(modelId))
            {
                modelId = "none";
            }
        }
        var learnedFactory = new CombatDecisionSimulationPolicyFactory(
            profile,
            residual,
            guidance,
            policyValue);
        if (campaign != null)
        {
            return RunCampaignEvaluation(
                request,
                campaign,
                rulesetResult.Ruleset,
                profile.Id,
                modelId,
                baselineFactory,
                learnedFactory,
                residualDiagnostic,
                guidanceDiagnostic,
                policyValueDiagnostic,
                token);
        }
        if (journey != null)
        {
            return RunJourneyEvaluation(
                request,
                journey,
                rulesetResult.Ruleset,
                profile.Id,
                modelId,
                baselineFactory,
                learnedFactory,
                residualDiagnostic,
                guidanceDiagnostic,
                policyValueDiagnostic,
                token);
        }

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
            scenarioId = scenario!.ScenarioId,
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
                        var engine = new CombatSimulationEngine(
                            new AuraToolsNativeRewardExtensionFactory());
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
                        if (episode != null)
                        {
                            // Formal paired evaluation is promotion evidence only.
                            // Keeping this provenance explicit prevents the evaluator
                            // from feeding its own results back into model training.
                            episode.Provenance = "offline-formal-evaluation";
                        }
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

    private static SimulationWorkResult RunCampaignEvaluation(
        SimulationRequest request,
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        string profileId,
        string modelId,
        ICombatSimulationPolicyFactory baselineFactory,
        ICombatSimulationPolicyFactory learnedFactory,
        string residualDiagnostic,
        string guidanceDiagnostic,
        string policyValueDiagnostic,
        CancellationToken token)
    {
        try
        {
            CombatCampaignWorldPlanner.Validate(campaign);
            CombatCampaignWorldPlanner.ResolveDifficulty(
                campaign,
                request.Simulation.DifficultyId);
        }
        catch (Exception ex)
        {
            return SimulationWorkResult.Failed(
                "七层情景模拟定义无效：" + ex.Message,
                request,
                campaign.CampaignId);
        }

        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var resultDirectory = Path.Combine(ResultsRootDirectory, runId);
        var tracesDirectory = Path.Combine(resultDirectory, "traces");
        Directory.CreateDirectory(resultDirectory);
        Directory.CreateDirectory(tracesDirectory);
        var resultsPath = Path.Combine(resultDirectory, "campaigns.jsonl");
        WriteText(
            Path.Combine(resultDirectory, "manifest.json"),
            AuraSharedJson.Serialize(new
            {
                schemaVersion = 3,
                runId,
                kind = "fixed-seven-layer-campaign-paired-evaluation",
                createdUtc = DateTime.UtcNow,
                scenarioId = campaign.CampaignId,
                campaign.CampaignVersion,
                difficultyId = request.Simulation.DifficultyId,
                rulesetHash = ruleset.RulesetHash,
                profile = profileId,
                modelId,
                request.Simulation.SimulationCount,
                request.Simulation.SeedStart,
                request.Simulation.Parallelism,
                battleCountPerCampaign = 37,
                route = "6x(2 normal + 1 elite + 2 normal + 1 boss) + final boss",
                baselinePolicy = baselineFactory.PolicyId,
                learnedPolicy = learnedFactory.PolicyId,
                residualDiagnostic,
                guidanceDiagnostic,
                policyValueDiagnostic
            }));

        SetStatus(
            AutoBattleSimulationStage.Running,
            "正在执行固定七层完整冒险对照（每次 37 场战斗）",
            requested: request.Simulation.SimulationCount,
            scenarioId: campaign.CampaignId,
            resultDirectory: resultDirectory);
        var aggregate = new SimulationAggregate();
        var reportPairs = new List<CombatCampaignPairResult>();
        var outputGate = new object();
        using (var writer = new StreamWriter(resultsPath, append: false))
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
                        var worldSeed = request.Simulation.SeedStart + (ulong)index;
                        var pair = new CombatCampaignRunner(
                            new CombatSimulationEngine(
                                new AuraToolsNativeRewardExtensionFactory()))
                            .RunPaired(
                            campaign,
                            request.Simulation.DifficultyId,
                            worldSeed,
                            ruleset,
                            baselineFactory,
                            learnedFactory,
                            token);
                        var baselineCombat = CampaignAsCombatResult(pair.Baseline);
                        var learnedCombat = CampaignAsCombatResult(pair.Learned);
                        var compactPair = BuildPair(baselineCombat, learnedCombat);
                        PruneCampaignTrace(pair.Baseline);
                        PruneCampaignTrace(pair.Learned);
                        var retainTrace = request.Simulation.RetainDivergentTraces
                                          && (compactPair.Divergent
                                              || pair.Baseline.Invalid
                                              || pair.Learned.Invalid
                                              || !pair.Learned.CampaignVictory);
                        lock (outputGate)
                        {
                            reportPairs.Add(pair);
                            aggregate.Add(
                                compactPair,
                                baselineCombat,
                                learnedCombat,
                                request.Simulation.MinimumAuthoritativeCoverage,
                                pair.Baseline.FinalBossVictory,
                                pair.Learned.FinalBossVictory,
                                pair.Baseline.ReachedFinalBoss,
                                pair.Learned.ReachedFinalBoss,
                                pair.Baseline.CompletedBattles,
                                pair.Learned.CompletedBattles);
                            writer.WriteLine(AuraSharedJson.SerializeCompact(pair));
                            if ((aggregate.CompletedPairs & 3) == 0)
                            {
                                writer.Flush();
                            }
                            if ((aggregate.CompletedPairs & 1) == 0
                                || aggregate.CompletedPairs == request.Simulation.SimulationCount)
                            {
                                SetStatus(
                                    AutoBattleSimulationStage.Running,
                                    "正在执行固定七层完整冒险对照（每次 37 场战斗）",
                                    aggregate.CompletedPairs,
                                    request.Simulation.SimulationCount,
                                    campaign.CampaignId,
                                    resultDirectory);
                            }
                        }
                        if (retainTrace)
                        {
                            WriteText(
                                Path.Combine(tracesDirectory, worldSeed + ".json"),
                                AuraSharedJson.Serialize(pair));
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
            "正在汇总七层完整冒险结果",
            aggregate.CompletedPairs,
            request.Simulation.SimulationCount,
            campaign.CampaignId,
            resultDirectory);
        var summary = aggregate.ToSummary(
            runId,
            campaign.CampaignId,
            profileId,
            modelId,
            ruleset.RulesetHash,
            request.Simulation);
        summary.DifficultyId = request.Simulation.DifficultyId;
        summary.ResultDirectory = resultDirectory;
        WriteCampaignTrainingReport(
            resultDirectory,
            campaign,
            ruleset,
            summary,
            reportPairs.OrderBy(item => item.WorldPlan.WorldSeed).ToList());
        var summaryPath = Path.Combine(resultDirectory, "summary.json");
        WriteText(summaryPath, AuraSharedJson.Serialize(summary));
        WriteText(
            Path.Combine(resultDirectory, "status.json"),
            AuraSharedJson.Serialize(new
            {
                stage = summary.Cancelled ? "cancelled" : "completed",
                kind = "fixed-seven-layer-campaign",
                summary.DifficultyId,
                summary.CompletedPairs,
                summary.RequestedPairs,
                summary.GatePassed,
                summary.GateReason,
                updatedUtc = DateTime.UtcNow
            }));
        if (!summary.Cancelled)
        {
            WriteText(
                LatestSummaryPath(profileId, summary.DifficultyId),
                AuraSharedJson.Serialize(summary));
            WriteText(
                LatestSummaryPath(profileId, summary.DifficultyId, modelId),
                AuraSharedJson.Serialize(summary));
        }
        return new SimulationWorkResult
        {
            Success = !summary.Cancelled,
            Cancelled = summary.Cancelled,
            Message = summary.Cancelled
                ? "七层情景模拟已取消"
                : (string.Equals(summary.DifficultyId, "advanced", StringComparison.Ordinal)
                    ? "高级难度"
                    : "普通难度")
                  + "评估任务已完成：正式有效样本 "
                  + summary.AuthoritativePairs
                  + "/"
                  + summary.CompletedPairs
                  + (summary.FormalRatesAvailable
                      ? "，正式学习/底模胜率 "
                        + summary.LearnedWinRate.ToString("P1")
                        + "/"
                        + summary.BaselineWinRate.ToString("P1")
                      : "，正式胜率不可用（不能按 0% 解读）")
                  + "；探索性原始学习/底模通关 "
                  + summary.RawLearnedVictories
                  + "/"
                  + summary.RawBaselineVictories
                  + "；验证标记"
                  + (summary.GatePassed ? "已获得" : "未获得"),
            CompletedPairs = summary.CompletedPairs,
            RequestedPairs = summary.RequestedPairs,
            ScenarioId = summary.ScenarioId,
            ResultDirectory = resultDirectory,
            GatePassed = summary.GatePassed
        };
    }

    internal static void PruneCampaignTrace(CombatCampaignResult result)
    {
        if (result.Battles.Count <= 1)
        {
            return;
        }
        for (var index = 0; index < result.Battles.Count - 1; index++)
        {
            result.Battles[index].Events.Clear();
        }
    }

    private static void WriteCampaignTrainingReport(
        string resultDirectory,
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        AutoBattleSimulationSummary summary,
        IReadOnlyList<CombatCampaignPairResult> pairs)
    {
        WriteText(
            Path.Combine(resultDirectory, "training-report.json"),
            AuraSharedJson.Serialize(new
            {
                schemaVersion = 1,
                reportKind = "role-specific-world-simulation-training-batch",
                createdUtc = DateTime.UtcNow,
                roleId = campaign.Player.RoleId,
                cardPoolScope = "base-game/offline-card-packs/all-unlocked/no-curse",
                startingState = campaign.Player,
                summary,
                formalStatisticsRule =
                    "Only authoritative pairs contribute to formal win rates and validation badges.",
                exploratoryStatisticsRule =
                    "Raw final-boss victories are retained even when rule coverage is insufficient.",
                runs = pairs
            }));

        var markdown = new StringBuilder();
        markdown.AppendLine("# 世界推演训练 / 评估批次报告");
        markdown.AppendLine();
        markdown.AppendLine("- 角色：`" + campaign.Player.RoleId + "`");
        markdown.AppendLine("- 卡池：本体离线卡包全部解锁；排除联机包、诅咒、衍生/无归属牌和 MOD 牌");
        markdown.AppendLine("- 难度：`" + summary.DifficultyId + "`");
        markdown.AppendLine("- 模型：`" + summary.ModelId + "`");
        markdown.AppendLine("- 正式有效样本："
                            + summary.AuthoritativePairs
                            + "/"
                            + summary.CompletedPairs);
        markdown.AppendLine(summary.FormalRatesAvailable
            ? "- 正式胜率：学习模型 "
              + summary.LearnedWinRate.ToString("P1")
              + "；底模 "
              + summary.BaselineWinRate.ToString("P1")
            : "- 正式胜率：不可用（没有满足规则覆盖门槛的样本，不能按 0% 解读）");
        markdown.AppendLine("- 探索性原始通关：学习模型 "
                            + summary.RawLearnedVictories
                            + "/"
                            + summary.CompletedPairs
                            + "；底模 "
                            + summary.RawBaselineVictories
                            + "/"
                            + summary.CompletedPairs);
        markdown.AppendLine("- 验证标记：" + (summary.GatePassed ? "通过" : "未通过"));
        markdown.AppendLine();

        foreach (var pair in pairs)
        {
            markdown.AppendLine("## 世界种子 " + pair.WorldPlan.WorldSeed);
            markdown.AppendLine();
            AppendCampaignSide(markdown, "学习模型", pair.Learned, ruleset);
            AppendCampaignSide(markdown, "底模", pair.Baseline, ruleset);
        }
        WriteText(
            Path.Combine(resultDirectory, "training-report.md"),
            markdown.ToString());
    }

    internal static void AppendCampaignSide(
        StringBuilder markdown,
        string label,
        CombatCampaignResult result,
        CombatRuleset ruleset)
    {
        markdown.AppendLine("### " + label);
        markdown.AppendLine();
        markdown.AppendLine("- 结果："
                            + (result.FinalBossVictory ? "最终首领胜利" : "未通关")
                            + "；完成 "
                            + result.CompletedBattles
                            + "/37 战；到达最终首领："
                            + (result.ReachedFinalBoss ? "是" : "否"));
        markdown.AppendLine("- 最终生命："
                            + result.FinalState.CurrentHp
                            + "/"
                            + result.FinalState.MaxHp);
        markdown.AppendLine("- 最终战斗余额："
                            + result.FinalState.Money);
        markdown.AppendLine("- 最终属性："
                            + string.Join(
                                "，",
                                result.FinalState.Attributes
                                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                                    .Select(item => item.Key + "=" + item.Value)));
        markdown.AppendLine("- 最终牌组（"
                            + result.FinalState.Deck.Count
                            + "）："
                            + FormatDeck(result.FinalState.Deck, ruleset));
        markdown.AppendLine("- 遗物（"
                            + result.FinalState.Relics.Count
                            + "/6）："
                            + FormatIds(result.FinalState.Relics));
        markdown.AppendLine("- 祝福（"
                            + result.FinalState.Blessings.Count
                            + "，无上限）："
                            + FormatIds(result.FinalState.Blessings));
        markdown.AppendLine();
        markdown.AppendLine("#### 构筑过程");
        markdown.AppendLine();
        if (result.Rewards.Count == 0)
        {
            markdown.AppendLine("- 尚未获得战斗奖励。");
        }
        foreach (var reward in result.Rewards.OrderBy(item => item.EncounterIndex))
        {
            markdown.Append("- 第 ")
                .Append(reward.EncounterIndex + 1)
                .Append(" 战 `")
                .Append(reward.EncounterId)
                .Append("`：");
            foreach (var card in reward.Cards.OrderBy(item => item.Round))
            {
                var selectedScore = card.Scores.FirstOrDefault(item =>
                    string.Equals(
                        item.RewardId,
                        card.SelectedId,
                        StringComparison.OrdinalIgnoreCase));
                markdown.Append(" 第")
                    .Append(card.Round)
                    .Append("轮[")
                    .Append(string.Join(", ", card.OfferedIds))
                    .Append("]→")
                    .Append(card.Skipped ? "跳过" : card.SelectedId);
                if (selectedScore != null)
                {
                    markdown.Append("(")
                        .Append(selectedScore.Total.ToString("0.00"))
                        .Append(")");
                }
                markdown.Append("；");
            }
            markdown.Append(" 遗物 ")
                .Append(reward.Relic.OfferedId)
                .Append("→")
                .Append(reward.Relic.Decision);
            if (!string.IsNullOrWhiteSpace(reward.Relic.ResolvedId)
                && !string.Equals(
                    reward.Relic.ResolvedId,
                    reward.Relic.OfferedId,
                    StringComparison.OrdinalIgnoreCase))
            {
                markdown.Append("（实际转化为 ")
                    .Append(reward.Relic.ResolvedId)
                    .Append("）");
            }
            if (!string.IsNullOrWhiteSpace(reward.Relic.ReplacedId))
            {
                markdown.Append("（替换 ")
                    .Append(reward.Relic.ReplacedId)
                    .Append("）");
            }
            markdown.Append("；祝福 ")
                .Append(reward.Blessing.OfferedId)
                .Append(reward.Blessing.Acquired ? "→获取" : "→未获取")
                .AppendLine();
        }
        markdown.AppendLine();
        markdown.AppendLine("#### "
                            + (result.ReachedFinalBoss ? "最终首领完整战斗流程" : "失败战斗完整流程"));
        markdown.AppendLine();
        var terminal = result.Battles.LastOrDefault();
        if (terminal == null)
        {
            markdown.AppendLine("- 没有战斗记录。");
            markdown.AppendLine();
            return;
        }
        markdown.AppendLine("- 场景：`"
                            + terminal.ScenarioId
                            + "`；结果："
                            + terminal.Outcome
                            + "；终局判定："
                            + terminal.TerminalResolution
                            + "；回合："
                            + terminal.Turns
                            + "；最终生命："
                            + terminal.FinalPlayerHp);
        if (terminal.InitialTerminalOutcome
            != CombatSimulationOutcome.None
            && (terminal.InitialTerminalOutcome != terminal.Outcome
                || terminal.InitialTerminationReason
                != terminal.TerminationReason))
        {
            markdown.AppendLine("- 初始终局："
                                + terminal.InitialTerminalOutcome
                                + "/"
                                + terminal.InitialTerminationReason
                                + "；玩家生命 "
                                + terminal.InitialTerminalPlayerHp
                                + "；存活敌人 "
                                + terminal.InitialTerminalLivingEnemyCount
                                + "；结算后改判为 "
                                + terminal.Outcome
                                + "/"
                                + terminal.TerminationReason);
        }
        foreach (var turn in terminal.TurnsSummary)
        {
            markdown.AppendLine("- 回合 "
                                + turn.Turn
                                + "：玩家 "
                                + turn.PlayerHpAtStart
                                + "→"
                                + turn.PlayerHpAtEnd
                                + "；敌方总生命 "
                                + turn.EnemyHpAtStart
                                + "→"
                                + turn.EnemyHpAtEnd
                                + "；动作 "
                                + turn.Actions);
        }
        markdown.AppendLine();
        markdown.AppendLine("事件序列：");
        markdown.AppendLine();
        foreach (var item in terminal.Events)
        {
            markdown.Append("- #")
                .Append(item.Sequence)
                .Append(" T")
                .Append(item.Turn)
                .Append(" ")
                .Append(item.Phase)
                .Append(" ")
                .Append(item.Kind);
            if (!string.IsNullOrWhiteSpace(item.DefinitionId))
            {
                markdown.Append(" `")
                    .Append(DisplayDefinition(item.DefinitionId, ruleset))
                    .Append("`");
            }
            if (item.Amount != 0)
            {
                markdown.Append(" 数值=").Append(item.Amount);
            }
            if (item.SourceActorId != 0 || item.TargetActorId != 0)
            {
                markdown.Append(" 来源=")
                    .Append(item.SourceActorId)
                    .Append(" 目标=")
                    .Append(item.TargetActorId);
            }
            markdown.AppendLine();
        }
        markdown.AppendLine();
    }

    private static string FormatDeck(
        IEnumerable<string> deck,
        CombatRuleset ruleset)
    {
        return string.Join(
            "，",
            deck.GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => DisplayDefinition(group.Key, ruleset)
                                 + "×"
                                 + group.Count()));
    }

    private static string FormatIds(IEnumerable<string> ids)
    {
        var values = ids.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        return values.Length == 0 ? "无" : string.Join("，", values);
    }

    private static string DisplayDefinition(string id, CombatRuleset ruleset)
    {
        if (ruleset.TryGetCard(id, out var card)
            && !string.IsNullOrWhiteSpace(card.DisplayName))
        {
            return card.DisplayName + " (" + id + ")";
        }
        if (ruleset.TryGetEnemy(id, out var enemy)
            && !string.IsNullOrWhiteSpace(enemy.DisplayName))
        {
            return enemy.DisplayName + " (" + id + ")";
        }
        if (ruleset.TryGetStatus(id, out var statusDefinition)
            && !string.IsNullOrWhiteSpace(statusDefinition.DisplayName))
        {
            return statusDefinition.DisplayName + " (" + id + ")";
        }
        return id;
    }

    private static SimulationWorkResult RunJourneyEvaluation(
        SimulationRequest request,
        CombatJourneyDefinition journey,
        CombatRuleset ruleset,
        string profileId,
        string modelId,
        ICombatSimulationPolicyFactory baselineFactory,
        ICombatSimulationPolicyFactory learnedFactory,
        string residualDiagnostic,
        string guidanceDiagnostic,
        string policyValueDiagnostic,
        CancellationToken token)
    {
        try
        {
            CombatJourneyWorldPlanner.Validate(journey);
        }
        catch (Exception ex)
        {
            return SimulationWorkResult.Failed(
                "情景旅程定义无效：" + ex.Message,
                request,
                journey.JourneyId);
        }
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var resultDirectory = Path.Combine(ResultsRootDirectory, runId);
        var tracesDirectory = Path.Combine(resultDirectory, "traces");
        var checkpointsDirectory = Path.Combine(resultDirectory, "checkpoints");
        Directory.CreateDirectory(resultDirectory);
        Directory.CreateDirectory(tracesDirectory);
        Directory.CreateDirectory(checkpointsDirectory);
        var resultsPath = Path.Combine(resultDirectory, "journeys.jsonl");
        WriteText(
            Path.Combine(resultDirectory, "manifest.json"),
            AuraSharedJson.Serialize(new
            {
                schemaVersion = 2,
                runId,
                kind = "scenario-journey-paired-evaluation",
                createdUtc = DateTime.UtcNow,
                scenarioId = journey.JourneyId,
                rulesetHash = ruleset.RulesetHash,
                profile = profileId,
                modelId,
                request.Simulation.SimulationCount,
                request.Simulation.SeedStart,
                request.Simulation.Parallelism,
                stageCount = journey.Stages.Count,
                bossStage = journey.Stages.Last().StageId,
                baselinePolicy = baselineFactory.PolicyId,
                learnedPolicy = learnedFactory.PolicyId,
                residualDiagnostic,
                guidanceDiagnostic,
                policyValueDiagnostic
            }));

        SetStatus(
            AutoBattleSimulationStage.Running,
            "正在执行同世界种子的完整情景旅程对照",
            requested: request.Simulation.SimulationCount,
            scenarioId: journey.JourneyId,
            resultDirectory: resultDirectory);
        var aggregate = new SimulationAggregate();
        var outputGate = new object();
        using (var writer = new StreamWriter(resultsPath, append: false))
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
                        var worldSeed = request.Simulation.SeedStart + (ulong)index;
                        var plan = CombatJourneyWorldPlanner.Build(journey, worldSeed);
                        var runner = new CombatJourneyRunner();
                        var baseline = runner.Run(
                            journey,
                            plan,
                            ruleset,
                            baselineFactory,
                            checkpointSink: checkpoint => WriteText(
                                Path.Combine(
                                    checkpointsDirectory,
                                    worldSeed + "-baseline.json"),
                                AuraSharedJson.Serialize(checkpoint)),
                            cancellationToken: token);
                        var learned = runner.Run(
                            journey,
                            plan,
                            ruleset,
                            learnedFactory,
                            checkpointSink: checkpoint => WriteText(
                                Path.Combine(
                                    checkpointsDirectory,
                                    worldSeed + "-learned.json"),
                                AuraSharedJson.Serialize(checkpoint)),
                            cancellationToken: token);
                        var baselineCombat = JourneyAsCombatResult(baseline);
                        var learnedCombat = JourneyAsCombatResult(learned);
                        var compactPair = BuildPair(baselineCombat, learnedCombat);
                        var journeyPair = new CombatJourneyPairResult
                        {
                            WorldPlan = plan,
                            Baseline = baseline,
                            Learned = learned
                        };
                        var retainTrace = request.Simulation.RetainDivergentTraces
                                          && (compactPair.Divergent
                                              || baseline.Invalid
                                              || learned.Invalid
                                              || !learned.JourneyVictory);
                        lock (outputGate)
                        {
                            aggregate.Add(
                                compactPair,
                                baselineCombat,
                                learnedCombat,
                                request.Simulation.MinimumAuthoritativeCoverage);
                            writer.WriteLine(AuraSharedJson.SerializeCompact(journeyPair));
                            if ((aggregate.CompletedPairs & 15) == 0)
                            {
                                writer.Flush();
                            }
                            if ((aggregate.CompletedPairs & 7) == 0
                                || aggregate.CompletedPairs
                                == request.Simulation.SimulationCount)
                            {
                                SetStatus(
                                    AutoBattleSimulationStage.Running,
                                    "正在执行同世界种子的完整情景旅程对照",
                                    aggregate.CompletedPairs,
                                    request.Simulation.SimulationCount,
                                    journey.JourneyId,
                                    resultDirectory);
                            }
                        }
                        if (retainTrace)
                        {
                            WriteText(
                                Path.Combine(tracesDirectory, worldSeed + ".json"),
                                AuraSharedJson.Serialize(journeyPair));
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
            "正在汇总情景旅程结果",
            aggregate.CompletedPairs,
            request.Simulation.SimulationCount,
            journey.JourneyId,
            resultDirectory);
        var summary = aggregate.ToSummary(
            runId,
            journey.JourneyId,
            profileId,
            modelId,
            ruleset.RulesetHash,
            request.Simulation);
        summary.ResultDirectory = resultDirectory;
        var summaryPath = Path.Combine(resultDirectory, "summary.json");
        WriteText(summaryPath, AuraSharedJson.Serialize(summary));
        WriteText(
            Path.Combine(resultDirectory, "status.json"),
            AuraSharedJson.Serialize(new
            {
                stage = summary.Cancelled ? "cancelled" : "completed",
                kind = "scenario-journey",
                summary.CompletedPairs,
                summary.RequestedPairs,
                summary.GatePassed,
                summary.GateReason,
                updatedUtc = DateTime.UtcNow
            }));
        if (!summary.Cancelled)
        {
            WriteText(LatestSummaryPath(profileId), AuraSharedJson.Serialize(summary));
        }
        return new SimulationWorkResult
        {
            Success = !summary.Cancelled,
            Cancelled = summary.Cancelled,
            Message = summary.Cancelled
                ? "情景旅程评估已取消，检查点已保留"
                : "情景旅程完成：学习模型全旅程成功率 "
                  + summary.LearnedWinRate.ToString("P1")
                  + "，底模 " + summary.BaselineWinRate.ToString("P1")
                  + "，门禁" + (summary.GatePassed ? "通过" : "未通过"),
            CompletedPairs = summary.CompletedPairs,
            RequestedPairs = summary.RequestedPairs,
            ScenarioId = summary.ScenarioId,
            ResultDirectory = resultDirectory,
            GatePassed = summary.GatePassed
        };
    }

    private static CombatSimulationResult JourneyAsCombatResult(CombatJourneyResult journey)
    {
        var coverage = journey.Battles.Count == 0
            ? 0d
            : journey.Battles.Min(item => item.SemanticCoverage);
        return new CombatSimulationResult
        {
            ScenarioId = journey.JourneyId,
            Seed = journey.WorldSeed,
            PolicyId = journey.PolicyId,
            Outcome = journey.Invalid
                ? CombatSimulationOutcome.Invalid
                : journey.JourneyVictory
                    ? CombatSimulationOutcome.Victory
                    : CombatSimulationOutcome.Defeat,
            TerminationReason = journey.Invalid
                ? CombatTerminationReason.UnsupportedRule
                : journey.JourneyVictory
                    ? CombatTerminationReason.Victory
                    : CombatTerminationReason.Defeat,
            Turns = journey.Battles.Sum(item => item.Turns),
            FinalPlayerHp = journey.FinalPlayerHp,
            FinalStateHash = journey.PlanHash
                             + ":"
                             + string.Join(",", journey.FinalDeck),
            SemanticCoverage = coverage
        };
    }

    private static CombatSimulationResult CampaignAsCombatResult(CombatCampaignResult campaign)
    {
        var coverage = Math.Min(
            campaign.BattleSemanticCoverage,
            campaign.ProgressionSemanticCoverage);
        if (campaign.UnsupportedDefinitions.Count > 0)
        {
            coverage = 0d;
        }
        return new CombatSimulationResult
        {
            ScenarioId = campaign.CampaignId,
            Seed = campaign.WorldSeed,
            Outcome = campaign.Invalid
                ? CombatSimulationOutcome.Invalid
                : campaign.CampaignVictory
                    ? CombatSimulationOutcome.Victory
                    : CombatSimulationOutcome.Defeat,
            TerminationReason = campaign.Invalid
                ? CombatTerminationReason.UnsupportedRule
                : campaign.CampaignVictory
                    ? CombatTerminationReason.Victory
                    : CombatTerminationReason.Defeat,
            Turns = campaign.Battles.Sum(item => item.Turns),
            FinalPlayerHp = campaign.FinalState.CurrentHp,
            SemanticCoverage = coverage,
            Metrics = new CombatSimulationMetrics
            {
                DamageDealt = campaign.Battles.Sum(item => item.Metrics.DamageDealt),
                DamageTaken = campaign.Battles.Sum(item => item.Metrics.DamageTaken),
                CardsPlayed = campaign.Battles.Sum(item => item.Metrics.CardsPlayed)
            },
            UnsupportedDefinitions = campaign.UnsupportedDefinitions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToList(),
            FinalStateHash = campaign.PlanHash
                             + ":"
                             + campaign.CompletedBattles
                             + ":"
                             + campaign.FinalState.CurrentHp
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
            AuraToolsAutoBattleModelRuntime.LoadPolicyValueDefinition(
                profile.Id,
                request.Settings.SelectedModelId);
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

    private static CombatJourneyDefinition? ResolveJourney(string journeyId)
    {
        Directory.CreateDirectory(InputDirectory);
        foreach (var path in JourneyFiles())
        {
            var candidate = AuraSharedJson.Deserialize<CombatJourneyDefinition>(
                File.ReadAllText(path));
            if (candidate != null
                && (string.IsNullOrWhiteSpace(journeyId)
                    || string.Equals(
                        candidate.JourneyId,
                        journeyId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
        return null;
    }

    private static CombatCampaignDefinition? ResolveCampaign(string campaignId)
    {
        Directory.CreateDirectory(InputDirectory);
        foreach (var path in CampaignFiles())
        {
            var candidate = AuraSharedJson.Deserialize<CombatCampaignDefinition>(
                File.ReadAllText(path));
            if (candidate != null
                && (string.IsNullOrWhiteSpace(campaignId)
                    || string.Equals(
                        candidate.CampaignId,
                        campaignId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
        return null;
    }

    private static CombatRulesetBuildResult ResolveRuleset(string version)
    {
        var discoveredVersions = new List<string>();
        CombatRulesetBuildResult? matchingFileResult = null;
        foreach (var path in RulesetFiles())
        {
            var document = AuraSharedJson.Deserialize<CombatRulesetDocument>(
                File.ReadAllText(path));
            var localVersion = document?.Version?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(localVersion))
            {
                discoveredVersions.Add(localVersion);
            }
            if (document != null
                && (string.IsNullOrWhiteSpace(version)
                    || string.Equals(
                        localVersion,
                        version.Trim(),
                        StringComparison.OrdinalIgnoreCase)))
            {
                matchingFileResult = CombatSimulationRegistry.BuildRuleset(document);
                if (matchingFileResult.Success
                    && matchingFileResult.Ruleset.CardCount > 0
                    && matchingFileResult.Ruleset.EnemyCount > 0)
                {
                    return matchingFileResult;
                }
            }
        }

        var registered = CombatSimulationRegistry.BuildRuleset(version);
        if (registered.Success
            && registered.Ruleset.CardCount > 0
            && registered.Ruleset.EnemyCount > 0)
        {
            return registered;
        }

        if (discoveredVersions.Count == 0)
        {
            return registered.Success
                ? new CombatRulesetBuildResult
                {
                    Errors = { "没有已注册规则，且输入目录中不存在 ruleset.json" }
                }
                : registered;
        }
        if (matchingFileResult != null)
        {
            return matchingFileResult;
        }
        return new CombatRulesetBuildResult
        {
            Errors =
            {
                "输入 ruleset.json 版本与场景不一致：scenario="
                + version
                + "，可用 ruleset="
                + string.Join(",", discoveredVersions.Distinct(StringComparer.OrdinalIgnoreCase))
            }
        };
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
                SelectedModelId = source.SelectedModelId,
                UnknownActionPolicy = source.UnknownActionPolicy,
                SearchQuality = source.SearchQuality,
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
                DifficultyId = source.Simulation.DifficultyId,
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

    private static bool TryResolveRequestedScenario(
        SimulationRequest request,
        out string message)
    {
        var available = AvailableScenarioIds();
        if (available.Count == 0)
        {
            message = "未找到标准评估包或自定义场景";
            return false;
        }
        if (!available.Contains(
                request.Simulation.ScenarioId,
                StringComparer.OrdinalIgnoreCase))
        {
            request.Simulation.ScenarioId = available[0];
        }
        message = "";
        return true;
    }

    private static string LatestSummaryPath(
        string profile,
        string difficultyId = "",
        string modelId = "")
    {
        Directory.CreateDirectory(ResultsRootDirectory);
        var difficultySuffix = string.IsNullOrWhiteSpace(difficultyId)
            ? ""
            : "-" + difficultyId.Trim().ToLowerInvariant();
        var modelSuffix = string.IsNullOrWhiteSpace(modelId)
            ? ""
            : "-model-" + StableFileKey(modelId);
        return Path.Combine(
            ResultsRootDirectory,
            "latest-summary-"
            + (profile ?? "balanced").Trim().ToLowerInvariant()
            + difficultySuffix
            + modelSuffix
            + ".json");
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

    private static string StableFileKey(string value)
    {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var character in value ?? "")
        {
            hash ^= character;
            hash *= prime;
        }
        return hash.ToString("x16");
    }

    private static string ValidationBadge(
        AutoBattleSimulationSummary? summary,
        string modelId)
    {
        if (summary == null)
        {
            return "○";
        }
        if (!string.Equals(summary.ModelId, modelId, StringComparison.Ordinal))
        {
            return "—";
        }
        return summary.GatePassed ? "✓" : "×";
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
        return SimulationPackageDirectories()
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.scenario.json")
                    .Concat(new[] { Path.Combine(directory, "scenario.json") }))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> JourneyFiles()
    {
        return SimulationPackageDirectories()
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.journey.json")
                    .Concat(new[] { Path.Combine(directory, "journey.json") }))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CampaignFiles()
    {
        return SimulationPackageDirectories()
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.campaign.json")
                    .Concat(new[] { Path.Combine(directory, "campaign.json") }))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> RulesetFiles()
    {
        return SimulationPackageDirectories()
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.ruleset.json")
                    .Concat(new[] { Path.Combine(directory, "ruleset.json") }))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SimulationPackageDirectories()
    {
        var bundled = Path.Combine(
            AuraToolsPaths.BundledConfigDirectory,
            "combat-simulation");
        Directory.CreateDirectory(InputDirectory);
        if (!Directory.Exists(bundled))
        {
            return new[] { InputDirectory };
        }
        return new[] { InputDirectory, bundled };
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
        private double baselineCompletedBattles;
        private double learnedCompletedBattles;

        public int CompletedPairs { get; private set; }

        public int AuthoritativePairs { get; private set; }

        public int InvalidPairs { get; private set; }

        public int DivergentPairs { get; private set; }

        public int BaselineVictories { get; private set; }

        public int LearnedVictories { get; private set; }

        public int RawBaselineVictories { get; private set; }

        public int RawLearnedVictories { get; private set; }

        public int BaselineReachedFinalBoss { get; private set; }

        public int LearnedReachedFinalBoss { get; private set; }

        public int BaselineMaximumCompletedBattles { get; private set; }

        public int LearnedMaximumCompletedBattles { get; private set; }

        public bool Cancelled { get; set; }

        public void Add(
            AutoBattleSimulationPair pair,
            CombatSimulationResult baseline,
            CombatSimulationResult learned,
            double requiredCoverage,
            bool? rawBaselineVictory = null,
            bool? rawLearnedVictory = null,
            bool baselineReachedFinalBoss = false,
            bool learnedReachedFinalBoss = false,
            int baselineProgress = 0,
            int learnedProgress = 0)
        {
            CompletedPairs++;
            if (rawBaselineVictory ?? baseline.Outcome == CombatSimulationOutcome.Victory)
            {
                RawBaselineVictories++;
            }
            if (rawLearnedVictory ?? learned.Outcome == CombatSimulationOutcome.Victory)
            {
                RawLearnedVictories++;
            }
            if (baselineReachedFinalBoss)
            {
                BaselineReachedFinalBoss++;
            }
            if (learnedReachedFinalBoss)
            {
                LearnedReachedFinalBoss++;
            }
            baselineCompletedBattles += baselineProgress;
            learnedCompletedBattles += learnedProgress;
            BaselineMaximumCompletedBattles = Math.Max(
                BaselineMaximumCompletedBattles,
                baselineProgress);
            LearnedMaximumCompletedBattles = Math.Max(
                LearnedMaximumCompletedBattles,
                learnedProgress);
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
            var requiredLearnedWinRate = string.Equals(
                settings.DifficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase)
                ? 0.8d
                : 1d;
            var targetPassed = learnedWinRate + 1e-9d >= requiredLearnedWinRate;
            var complete = !Cancelled && CompletedPairs == settings.SimulationCount;
            var gatePassed = complete
                             && AuthoritativePairs > 0
                             && coveragePassed
                             && regressionPassed
                             && targetPassed;
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
                RawBaselineVictories = RawBaselineVictories,
                RawLearnedVictories = RawLearnedVictories,
                RawBaselineWinRate = CompletedPairs == 0
                    ? 0d
                    : (double)RawBaselineVictories / CompletedPairs,
                RawLearnedWinRate = CompletedPairs == 0
                    ? 0d
                    : (double)RawLearnedVictories / CompletedPairs,
                BaselineReachedFinalBoss = BaselineReachedFinalBoss,
                LearnedReachedFinalBoss = LearnedReachedFinalBoss,
                BaselineMeanCompletedBattles = CompletedPairs == 0
                    ? 0d
                    : baselineCompletedBattles / CompletedPairs,
                LearnedMeanCompletedBattles = CompletedPairs == 0
                    ? 0d
                    : learnedCompletedBattles / CompletedPairs,
                BaselineMaximumCompletedBattles = BaselineMaximumCompletedBattles,
                LearnedMaximumCompletedBattles = LearnedMaximumCompletedBattles,
                AuthoritativeCoverage = coverage,
                BaselineWinRate = baselineWinRate,
                LearnedWinRate = learnedWinRate,
                RequiredLearnedWinRate = requiredLearnedWinRate,
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
                            : !targetPassed
                                ? "学习模型未达到当前难度验证线（要求 "
                                  + requiredLearnedWinRate.ToString("P0")
                                  + "）"
                            : AuthoritativePairs == 0
                                ? "没有权威有效样本"
                                : "通过",
                Cancelled = Cancelled,
                CompletedUtc = DateTime.UtcNow
            };
        }
    }

    private sealed class FoundationPackageCacheEntry
    {
        public CombatCampaignDefinition Campaign { get; set; } = new();

        public CombatRuleset Ruleset { get; set; } = CombatRuleset.Empty;
    }
}
