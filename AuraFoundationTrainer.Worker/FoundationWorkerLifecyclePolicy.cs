using AuraCombatAi.Shared;

namespace AuraFoundationTrainer.Worker;

internal static class FoundationCheckpointRetentionPolicy
{
    public static List<CombatFoundationCheckpointCatalogEntry> Select(
        IEnumerable<CombatFoundationCheckpointCatalogEntry>? source,
        string currentEntryId,
        string previousRecommendedCheckpointId)
    {
        var candidates = (source
                          ?? Array.Empty<
                              CombatFoundationCheckpointCatalogEntry>())
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => item.CreatedUtc)
                .First())
            .ToList();
        if (candidates.Count
            <= CombatFoundationCheckpointCatalogProtocol.MaximumEntries)
        {
            return candidates
                .OrderByDescending(item => item.CreatedUtc)
                .ToList();
        }

        var retained = new List<CombatFoundationCheckpointCatalogEntry>(
            CombatFoundationCheckpointCatalogProtocol.MaximumEntries);
        var retainedIds = new HashSet<string>(StringComparer.Ordinal);

        void Protect(CombatFoundationCheckpointCatalogEntry? item)
        {
            if (item == null
                || retained.Count
                   >= CombatFoundationCheckpointCatalogProtocol.MaximumEntries
                || !retainedIds.Add(item.Id))
            {
                return;
            }
            retained.Add(item);
        }

        void ProtectId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }
            Protect(candidates.FirstOrDefault(item => string.Equals(
                item.Id,
                id,
                StringComparison.Ordinal)));
        }

        var recommendation = CombatFoundationCheckpointCatalogProtocol
            .Recommend(candidates);
        var bestAnchored = candidates
            .Where(item => item.SupportsModelBranch
                           && item.SelectionAnchorMetrics?.FrameCount > 0)
            .OrderBy(item => item.SelectionAnchorMetrics.CompositeLoss)
            .ThenByDescending(item => item.CreatedUtc)
            .FirstOrDefault();
        var certifiedRecommendation =
            CombatFoundationCheckpointCatalogProtocol.Recommend(
                candidates.Where(item => item.QualityGatesPassed));
        var latestCertifiedIteration = candidates
            .Where(item => item.QualityGatesPassed
                           && string.Equals(
                               item.Stage,
                               "iteration-complete",
                               StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedUtc)
            .FirstOrDefault();
        var latestCompletedIteration = candidates
            .Where(item => string.Equals(
                item.Stage,
                "iteration-complete",
                StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedUtc)
            .FirstOrDefault();

        // The newly committed snapshot must remain resumable, while the
        // recommendation and the evidence that produced it must not be
        // evicted by frequent model-training snapshots.
        ProtectId(currentEntryId);
        Protect(recommendation);
        ProtectId(previousRecommendedCheckpointId);
        Protect(certifiedRecommendation);
        Protect(bestAnchored);
        Protect(latestCertifiedIteration);
        Protect(latestCompletedIteration);

        foreach (var item in candidates
                     .OrderByDescending(item => item.QualityGatesPassed)
                     .ThenByDescending(item => string.Equals(
                         item.Stage,
                         "iteration-complete",
                         StringComparison.Ordinal))
                     .ThenByDescending(item => item.CreatedUtc))
        {
            Protect(item);
        }

        return retained
            .OrderByDescending(item => item.CreatedUtc)
            .ToList();
    }
}

internal sealed class FoundationEvaluatedModelMetadata
{
    public string ModelId { get; init; } = "";

    public int Iteration { get; init; }

    public int EpochsExecuted { get; init; }

    public int SelectedEpoch { get; init; }

    public int BestValidationEpoch { get; init; }

    public int DeploymentSelectedEpoch { get; init; }
}

internal static class FoundationWorkerResultMetadata
{
    public static FoundationEvaluatedModelMetadata Resolve(
        CombatCampaignFoundationTrainingResult training)
    {
        ArgumentNullException.ThrowIfNull(training);

        var evaluatedModelId = training.EvaluatedModelId ?? "";
        CombatCampaignFoundationIteration? selectedIteration;
        if (!string.IsNullOrWhiteSpace(evaluatedModelId))
        {
            selectedIteration = training.Iterations
                .Where(item => string.Equals(
                    item.CandidateModelId,
                    evaluatedModelId,
                    StringComparison.Ordinal))
                .Where(item => training.EvaluatedModelIteration <= 0
                               || item.Iteration
                               == training.EvaluatedModelIteration)
                .LastOrDefault();
            selectedIteration ??= training.Iterations
                .Where(item => string.Equals(
                    item.CandidateModelId,
                    evaluatedModelId,
                    StringComparison.Ordinal))
                .LastOrDefault();
        }
        else
        {
            selectedIteration = training.Iterations.LastOrDefault();
        }

        var history = selectedIteration?.ModelEpochHistory
                      ?? training.ModelEpochHistory
                      ?? new List<CombatPolicyValueEpochMetrics>();
        var executedEpochs = history.Count(item => !item.Calibrated);
        var bestValidationEpoch = history
            .Where(item => !item.Calibrated
                           && item.Validation?.FrameCount > 0)
            .OrderBy(item => item.Validation.CompositeLoss)
            .ThenBy(item => item.Epoch)
            .Select(item => item.Epoch)
            .FirstOrDefault();
        var deploymentSelectedEpoch = selectedIteration?.TuningSelectedEpoch
                                      ?? 0;
        if (bestValidationEpoch <= 0)
        {
            bestValidationEpoch = deploymentSelectedEpoch > 0
                ? deploymentSelectedEpoch
                : training.ModelBestEpoch;
        }
        if (deploymentSelectedEpoch <= 0)
        {
            deploymentSelectedEpoch = bestValidationEpoch;
        }

        return new FoundationEvaluatedModelMetadata
        {
            ModelId = string.IsNullOrWhiteSpace(evaluatedModelId)
                ? selectedIteration?.CandidateModelId ?? ""
                : evaluatedModelId,
            Iteration = selectedIteration?.Iteration
                        ?? Math.Max(0, training.EvaluatedModelIteration),
            EpochsExecuted = executedEpochs,
            SelectedEpoch = deploymentSelectedEpoch,
            BestValidationEpoch = bestValidationEpoch,
            DeploymentSelectedEpoch = deploymentSelectedEpoch
        };
    }
}

internal static class FoundationWorkerProgressFinalizer
{
    public static CombatCampaignFoundationTelemetry Normalize(
        CombatCampaignFoundationTelemetry? telemetry,
        string terminalStage)
    {
        var normalizedStage = string.IsNullOrWhiteSpace(terminalStage)
            ? "completed"
            : terminalStage.Trim().ToLowerInvariant();
        var result = telemetry ?? new CombatCampaignFoundationTelemetry();
        result.Stage = normalizedStage;
        result.Phase = normalizedStage;
        result.ActiveCampaigns = 0;
        result.SchedulerQueuedWork = 0;
        result.SchedulerRunningWork = 0;
        result.MaximumActiveBattleDepth = 0;
        result.EstimatedRemainingSeconds = 0d;
        result.EstimatedRemainingLowerSeconds = 0d;
        result.EstimatedRemainingUpperSeconds = 0d;
        result.PhaseEstimatedRemainingSeconds = 0d;
        return result;
    }
}
