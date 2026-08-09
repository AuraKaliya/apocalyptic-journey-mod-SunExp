using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public enum CombatSemanticCoverageLevel
{
    ProjectedAndRealized,
    RealizedFactOnly,
    Unsupported
}

public sealed class CombatSemanticCoverageEntry
{
    public string OwnerKind { get; set; } = "";

    public string OwnerId { get; set; } = "";

    public string Trigger { get; set; } = "";

    public string EffectKind { get; set; } = "";

    public CombatSemanticCoverageLevel Level { get; set; }
}

public sealed class CombatSemanticCoverageReport
{
    public const string CurrentVersion =
        "semantic-coverage-v1-structured-native-inventory";

    public string Version { get; set; } = CurrentVersion;

    public List<CombatSemanticCoverageEntry> Entries { get; set; } = new();

    public List<string> Errors { get; set; } = new();

    public int ProjectedCount => Entries.Count(item =>
        item.Level == CombatSemanticCoverageLevel.ProjectedAndRealized);

    public int RealizedOnlyCount => Entries.Count(item =>
        item.Level == CombatSemanticCoverageLevel.RealizedFactOnly);

    public int UnsupportedCount => Entries.Count(item =>
        item.Level == CombatSemanticCoverageLevel.Unsupported);

    public bool Complete => Errors.Count == 0 && UnsupportedCount == 0;
}

public static class CombatSemanticCoverageAudit
{
    public static CombatSemanticCoverageReport Analyze(
        CombatCampaignDefinition? campaign,
        CombatRuleset ruleset)
    {
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        var report = new CombatSemanticCoverageReport();
        foreach (var status in ruleset.SnapshotStatuses()
                     .OrderBy(item => item.StatusId, StringComparer.Ordinal))
        {
            foreach (var trigger in status.Triggers
                         .OrderBy(item => item.EventKind)
                         .ThenBy(item => item.TriggerId, StringComparer.Ordinal))
            {
                foreach (var effect in trigger.Effects)
                {
                    report.Entries.Add(new CombatSemanticCoverageEntry
                    {
                        OwnerKind = "status",
                        OwnerId = status.StatusId,
                        Trigger = trigger.EventKind + ":" + trigger.TriggerId,
                        EffectKind = effect.Kind.ToString(),
                        Level = IsStaticallyProjected(trigger.EventKind)
                            ? CombatSemanticCoverageLevel.ProjectedAndRealized
                            : CombatSemanticCoverageLevel.RealizedFactOnly
                    });
                }
            }
            if (UsesNativeScript(status.Metadata))
            {
                AddNative(report, "status", status.StatusId);
            }
        }
        foreach (var card in ruleset.SnapshotCards()
                     .Where(item => UsesNativeScript(item.Metadata))
                     .OrderBy(item => item.CardId, StringComparer.Ordinal))
        {
            AddNative(report, "card", card.CardId);
        }
        foreach (var reward in (campaign?.Rewards
                                ?? new List<CombatCampaignRewardDefinition>())
                     .Where(item => !string.IsNullOrWhiteSpace(item.FightScript))
                     .OrderBy(item => item.RewardId, StringComparer.Ordinal))
        {
            AddNative(
                report,
                reward.Kind.ToString().ToLowerInvariant(),
                reward.RewardId);
        }
        report.Entries = report.Entries
            .OrderBy(item => item.OwnerKind, StringComparer.Ordinal)
            .ThenBy(item => item.OwnerId, StringComparer.Ordinal)
            .ThenBy(item => item.Trigger, StringComparer.Ordinal)
            .ThenBy(item => item.EffectKind, StringComparer.Ordinal)
            .ToList();
        return report;
    }

    private static bool IsStaticallyProjected(
        CombatSimulationEventKind eventKind)
    {
        return eventKind is CombatSimulationEventKind.ActionResolved
            or CombatSimulationEventKind.TurnStarted
            or CombatSimulationEventKind.TurnEnded;
    }

    private static bool UsesNativeScript(
        IReadOnlyDictionary<string, string> metadata)
    {
        return metadata.TryGetValue("NativeExecution", out var execution)
               && string.Equals(
                   execution,
                   "Script",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void AddNative(
        CombatSemanticCoverageReport report,
        string ownerKind,
        string ownerId)
    {
        report.Entries.Add(new CombatSemanticCoverageEntry
        {
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            Trigger = "native-event-bridge",
            EffectKind = "runtime-factual-events",
            Level = CombatSemanticCoverageLevel.RealizedFactOnly
        });
    }
}
