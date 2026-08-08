using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatFoundationCheckpointResumeModes
{
    public const string Exact = "exact";

    public const string ModelBranch = "model-branch";

    public static string Normalize(string? value)
    {
        return string.Equals(value, ModelBranch, StringComparison.OrdinalIgnoreCase)
            ? ModelBranch
            : Exact;
    }
}

public static class CombatFoundationCheckpointCatalogProtocol
{
    public const string Version = "foundation-checkpoint-catalog-v1";

    public const int MaximumEntries = 8;

    public const string CatalogFileName = "foundation-checkpoint-catalog-v1.json";

    public const string SelectionAnchorFileName =
        "foundation-model-selection-anchor-v1.jsonl";

    public const string ImmutableDirectoryName = "checkpoints";

    public static string Risk(
        double trainingLoss,
        double validationLoss,
        IReadOnlyList<CombatPolicyValueEpochMetrics>? history,
        out string reason)
    {
        return Risk(
            new CombatPolicyValueMetricSnapshot
            {
                FrameCount = trainingLoss > 0d ? 1 : 0,
                CompositeLoss = trainingLoss
            },
            new CombatPolicyValueMetricSnapshot
            {
                FrameCount = validationLoss > 0d ? 1 : 0,
                CompositeLoss = validationLoss
            },
            null,
            history,
            out reason);
    }

    public static string Risk(
        CombatPolicyValueMetricSnapshot? training,
        CombatPolicyValueMetricSnapshot? validation,
        CombatPolicyValueMetricSnapshot? test,
        IReadOnlyList<CombatPolicyValueEpochMetrics>? history,
        out string reason)
    {
        var assessment = CombatGeneralizationAssessmentProtocol.Assess(
            training,
            validation,
            test,
            history);
        reason = assessment.Reason;
        return assessment.Level;
    }

    public static CombatFoundationCheckpointCatalogEntry? Recommend(
        IEnumerable<CombatFoundationCheckpointCatalogEntry>? source)
    {
        var candidates = (source ?? Array.Empty<CombatFoundationCheckpointCatalogEntry>())
            .Where(item => item != null && item.SupportsModelBranch)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        var anchored = candidates
            .Where(item => item.SelectionAnchorMetrics?.FrameCount > 0)
            .ToList();
        if (anchored.Count > 0)
        {
            var best = anchored.Min(item =>
                item.SelectionAnchorMetrics.CompositeLoss);
            var bestStandardError = anchored
                .Where(item => Math.Abs(
                    item.SelectionAnchorMetrics.CompositeLoss - best) < 1e-12d)
                .Select(item => item.SelectionAnchorMetrics
                    .CompositeLossStandardError)
                .DefaultIfEmpty(0d)
                .Min();
            return anchored
                .Where(item => item.SelectionAnchorMetrics.CompositeLoss
                               <= best + bestStandardError)
                .OrderByDescending(item => item.QualityGatesPassed)
                .ThenBy(item => RiskPriority(item.Risk))
                .ThenBy(item => item.CompletedEpochs)
                .ThenByDescending(item => item.CreatedUtc)
                .First();
        }
        return candidates
            .OrderByDescending(item => item.QualityGatesPassed)
            .ThenBy(item => RiskPriority(item.Risk))
            .ThenBy(item => item.ValidationLoss)
            .ThenBy(item => Math.Max(0d, item.GeneralizationGap))
            .ThenBy(item => item.CompletedEpochs)
            .First();
    }

    private static int RiskPriority(string risk)
    {
        return risk switch
        {
            CombatGeneralizationRiskLevels.Healthy => 0,
            "balanced" => 0,
            CombatGeneralizationRiskLevels.Watch => 1,
            CombatGeneralizationRiskLevels.Underfit => 2,
            CombatGeneralizationRiskLevels.Overfit => 3,
            _ => 4
        };
    }
}

public sealed class CombatFoundationCheckpointCatalog
{
    public string Protocol { get; set; } =
        CombatFoundationCheckpointCatalogProtocol.Version;

    public string RequestFingerprint { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public long Generation { get; set; }

    public int ChecksumVersion { get; set; }

    public string ContentChecksumSha256 { get; set; } = "";

    public string SelectionAnchorPath { get; set; } = "";

    public string SelectionAnchorIdentity { get; set; } = "";

    public int SelectionAnchorEpisodes { get; set; }

    public string RecommendedCheckpointId { get; set; } = "";

    public List<CombatFoundationCheckpointCatalogEntry> Entries { get; set; } =
        new();
}

public sealed class CombatFoundationCheckpointCatalogEntry
{
    public string Id { get; set; } = "";

    public string SourceJobId { get; set; } = "";

    public string RequestFingerprint { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string Stage { get; set; } = "";

    public int NextIteration { get; set; }

    public int CompletedCampaigns { get; set; }

    public int CompletedEpochs { get; set; }

    public int BestEpoch { get; set; }

    public int BestValidationEpoch { get; set; }

    public int DeploymentSelectedEpoch { get; set; }

    public string ModelId { get; set; } = "";

    public string CheckpointPath { get; set; } = "";

    public string CheckpointContentSha256 { get; set; } = "";

    public string EpisodeSnapshotPath { get; set; } = "";

    public string EpisodeSnapshotContentSha256 { get; set; } = "";

    public string ReplayIdentity { get; set; } = "";

    public int EpisodeCount { get; set; }

    public double TrainingLoss { get; set; }

    public double ValidationLoss { get; set; }

    public double TestLoss { get; set; }

    public double GeneralizationGap { get; set; }

    public CombatPolicyValueMetricSnapshot SelectionAnchorMetrics { get; set; } =
        new();

    public string Risk { get; set; } = "unknown";

    public string RiskReason { get; set; } = "";

    public bool EarlyStopped { get; set; }

    public bool QualityGatesPassed { get; set; }

    public bool Recommended { get; set; }

    public bool SupportsExact { get; set; } = true;

    public bool SupportsModelBranch { get; set; } = true;
}
