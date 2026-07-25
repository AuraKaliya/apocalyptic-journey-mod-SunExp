using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public sealed class CombatJourneyTrainingEpisode
{
    public string Protocol { get; set; } = "aura.combat-journey.episode.v1";

    public int SchemaVersion { get; set; } = 1;

    public string JourneyRunId { get; set; } = "";

    public string JourneyId { get; set; } = "";

    public string ModeId { get; set; } = "Normal";

    public string Source { get; set; } = "live-world-simulation";

    public string PolicyId { get; set; } = "human";

    public string RoleId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public List<string> InitialDeck { get; set; } = new();

    public List<CombatJourneyBattleTrainingRecord> Battles { get; set; } = new();

    public List<CombatJourneyRewardTrainingRecord> Rewards { get; set; } = new();

    public List<string> FinalDeck { get; set; } = new();

    public string Outcome { get; set; } = "unknown";

    public bool Complete { get; set; }

    public bool ReachedBoss { get; set; }

    public bool BossVictory { get; set; }

    public string TerminalReason { get; set; } = "";

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime EndedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CombatJourneyBattleTrainingRecord
{
    public int BattleIndex { get; set; }

    public long BattleSessionId { get; set; }

    public string StageId { get; set; } = "";

    public string EnemyId { get; set; } = "";

    public bool IsBoss { get; set; }

    public string Outcome { get; set; } = "unknown";

    public int FinalPlayerHp { get; set; }
}

public sealed class CombatJourneyRewardTrainingRecord
{
    public int RewardIndex { get; set; }

    public int AfterBattleIndex { get; set; }

    public string StageId { get; set; } = "";

    public List<string> OfferedCardIds { get; set; } = new();

    public string SelectedCardId { get; set; } = "";

    public bool Skipped { get; set; }

    public string SelectedBy { get; set; } = "human";

    public List<string> DeckBefore { get; set; } = new();

    public List<string> DeckAfter { get; set; } = new();

    public List<CombatRewardScore> Scores { get; set; } = new();
}

public static class CombatJourneyTrainingEpisodeFactory
{
    public static CombatJourneyTrainingEpisode FromSimulation(
        CombatJourneyDefinition definition,
        CombatJourneyWorldPlan plan,
        CombatJourneyResult result,
        string rulesetHash)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (result == null) throw new ArgumentNullException(nameof(result));

        var episode = new CombatJourneyTrainingEpisode
        {
            JourneyRunId = definition.JourneyId
                           + ":"
                           + plan.WorldSeed
                           + ":"
                           + (result.PolicyId ?? ""),
            JourneyId = definition.JourneyId,
            ModeId = "offline-standard-evaluation",
            Source = "offline-standard-evaluation",
            PolicyId = result.PolicyId ?? "",
            RoleId = definition.Player?.RoleId ?? "",
            WorldSeed = plan.WorldSeed,
            PlanHash = plan.PlanHash,
            RulesetHash = rulesetHash ?? "",
            InitialDeck = new List<string>(definition.Player?.Deck ?? new List<string>()),
            FinalDeck = new List<string>(result.FinalDeck ?? new List<string>()),
            Outcome = result.JourneyVictory ? "victory" : "defeat",
            Complete = !result.Invalid
                       && (result.JourneyVictory
                           || result.Battles.Any(battle =>
                               battle.Outcome == CombatSimulationOutcome.Defeat)),
            ReachedBoss = result.ReachedBoss,
            BossVictory = result.BossVictory,
            TerminalReason = result.Invalid
                ? "invalid-simulation"
                : result.JourneyVictory
                    ? "final-boss-victory"
                    : "journey-defeat",
            StartedUtc = DateTime.UtcNow,
            EndedUtc = DateTime.UtcNow
        };
        for (var index = 0; index < result.Battles.Count; index++)
        {
            var battle = result.Battles[index];
            var planned = index < plan.Encounters.Count
                ? plan.Encounters[index]
                : new CombatJourneyPlannedEncounter { Index = index };
            episode.Battles.Add(new CombatJourneyBattleTrainingRecord
            {
                BattleIndex = index,
                StageId = planned.StageId,
                EnemyId = planned.EnemyId,
                IsBoss = planned.IsBoss,
                Outcome = battle.Outcome.ToString().ToLowerInvariant(),
                FinalPlayerHp = battle.FinalPlayerHp
            });
        }
        for (var index = 0; index < result.Rewards.Count; index++)
        {
            var reward = result.Rewards[index];
            episode.Rewards.Add(new CombatJourneyRewardTrainingRecord
            {
                RewardIndex = index,
                AfterBattleIndex = reward.EncounterIndex,
                StageId = reward.StageId,
                OfferedCardIds = new List<string>(reward.OfferedCardIds),
                SelectedCardId = reward.SelectedCardId,
                Skipped = reward.Skipped,
                SelectedBy = "reward-value/system-fit/build-tendency",
                Scores = new List<CombatRewardScore>(reward.Scores)
            });
        }
        return episode;
    }
}
