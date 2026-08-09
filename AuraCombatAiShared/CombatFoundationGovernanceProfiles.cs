using System;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatFoundationGovernanceProfileNames
{
    public const string Development = "development";

    public const string Release = "release";

    public const string Custom = "custom";
}

public sealed class CombatFoundationGovernancePlan
{
    public string Profile { get; set; } =
        CombatFoundationGovernanceProfileNames.Release;

    public int TuningInterval { get; set; } = 1;

    public int TuningNormalCampaigns { get; set; }

    public int TuningAdvancedCampaigns { get; set; }

    public int TuningScreeningNormalCampaigns { get; set; }

    public int TuningScreeningAdvancedCampaigns { get; set; }

    public int TuningFinalistCount { get; set; } = 1;

    public int CapabilityProbeTeacherCampaignsPerDifficulty { get; set; }

    public int AutoTuneSampleCampaigns { get; set; }

    public int ArenaEvaluationInterval { get; set; } = 6;

    public bool ArenaConfirmationFinalIterationOnly { get; set; } = true;

    public bool RunsArenaAtIteration(int iterationIndex, int totalIterations)
    {
        var iteration = Math.Max(0, iterationIndex);
        var total = Math.Max(1, totalIterations);
        return iteration == total - 1
               || (iteration + 1) % Math.Max(1, ArenaEvaluationInterval) == 0;
    }

    public bool RunsFormalConfirmationAtIteration(
        int iterationIndex,
        int totalIterations)
    {
        return !ArenaConfirmationFinalIterationOnly
               && RunsArenaAtIteration(iterationIndex, totalIterations)
               || iterationIndex == Math.Max(1, totalIterations) - 1;
    }

    public int ScheduledArenaIterations(int totalIterations)
    {
        return Enumerable.Range(0, Math.Max(1, totalIterations))
            .Count(iteration => RunsArenaAtIteration(iteration, totalIterations));
    }

    public bool RunsTuningAtIteration(int iterationIndex, int totalIterations)
    {
        var iteration = Math.Max(0, iterationIndex);
        var total = Math.Max(1, totalIterations);
        if (!string.Equals(
                Profile,
                CombatFoundationGovernanceProfileNames.Custom,
                StringComparison.Ordinal))
        {
            return iteration == total - 1;
        }
        if (TuningInterval <= 1
            || iteration == 0
            || iteration == total - 1)
        {
            return true;
        }
        return iteration % TuningInterval == 0
               && total - 1 - iteration >= TuningInterval;
    }

    public int ScheduledTuningIterations(int totalIterations)
    {
        var count = 0;
        for (var iteration = 0; iteration < Math.Max(1, totalIterations); iteration++)
        {
            if (RunsTuningAtIteration(iteration, totalIterations))
            {
                count++;
            }
        }
        return count;
    }
}

public static class CombatFoundationGovernanceProfiles
{
    public static string Normalize(string? profile)
    {
        var value = (profile ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            CombatFoundationGovernanceProfileNames.Development => value,
            CombatFoundationGovernanceProfileNames.Custom => value,
            _ => CombatFoundationGovernanceProfileNames.Release
        };
    }

    public static CombatFoundationGovernancePlan Resolve(
        string? profile,
        int tuningInterval,
        int tuningNormalCampaigns,
        int tuningAdvancedCampaigns,
        int tuningScreeningNormalCampaigns,
        int tuningScreeningAdvancedCampaigns,
        int tuningFinalistCount,
        int capabilityProbeTeacherCampaignsPerDifficulty,
        int autoTuneSampleCampaigns,
        int arenaEvaluationInterval = 6,
        bool arenaConfirmationFinalIterationOnly = true)
    {
        var normalized = Normalize(profile);
        var normal = Math.Max(0, Math.Min(64, tuningNormalCampaigns));
        var advanced = Math.Max(0, Math.Min(64, tuningAdvancedCampaigns));
        var plan = new CombatFoundationGovernancePlan
        {
            Profile = normalized,
            TuningInterval = Math.Max(1, Math.Min(8, tuningInterval)),
            TuningNormalCampaigns = normal,
            TuningAdvancedCampaigns = advanced,
            TuningScreeningNormalCampaigns = Math.Max(
                0,
                Math.Min(normal, tuningScreeningNormalCampaigns)),
            TuningScreeningAdvancedCampaigns = Math.Max(
                0,
                Math.Min(advanced, tuningScreeningAdvancedCampaigns)),
            TuningFinalistCount = Math.Max(1, Math.Min(5, tuningFinalistCount)),
            CapabilityProbeTeacherCampaignsPerDifficulty = Math.Max(
                0,
                Math.Min(128, capabilityProbeTeacherCampaignsPerDifficulty)),
            AutoTuneSampleCampaigns = Math.Max(
                4,
                Math.Min(64, autoTuneSampleCampaigns)),
            ArenaEvaluationInterval = Math.Max(
                1,
                Math.Min(12, arenaEvaluationInterval)),
            ArenaConfirmationFinalIterationOnly =
                arenaConfirmationFinalIterationOnly
        };
        if (!string.Equals(
                normalized,
                CombatFoundationGovernanceProfileNames.Development,
                StringComparison.Ordinal))
        {
            return plan;
        }

        plan.TuningInterval = Math.Max(2, plan.TuningInterval);
        plan.TuningNormalCampaigns = Math.Min(16, plan.TuningNormalCampaigns);
        plan.TuningAdvancedCampaigns = Math.Min(32, plan.TuningAdvancedCampaigns);
        plan.TuningScreeningNormalCampaigns = Math.Min(
            4,
            plan.TuningNormalCampaigns);
        plan.TuningScreeningAdvancedCampaigns = Math.Min(
            8,
            plan.TuningAdvancedCampaigns);
        plan.TuningFinalistCount = 1;
        plan.CapabilityProbeTeacherCampaignsPerDifficulty = Math.Min(
            16,
            plan.CapabilityProbeTeacherCampaignsPerDifficulty);
        plan.AutoTuneSampleCampaigns = Math.Min(16, plan.AutoTuneSampleCampaigns);
        return plan;
    }
}
