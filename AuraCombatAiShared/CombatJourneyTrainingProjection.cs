using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public static class CombatJourneyTrainingProjection
{
    public static void ApplyJourneyReturns(
        IList<CombatEpisode> combatEpisodes,
        IEnumerable<CombatJourneyTrainingEpisode>? journeys,
        double battleDiscount = 0.97d)
    {
        if (combatEpisodes == null)
        {
            throw new ArgumentNullException(nameof(combatEpisodes));
        }

        var bySession = combatEpisodes
            .Where(episode => episode != null && episode.BattleSessionId > 0)
            .GroupBy(episode => episode.BattleSessionId)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var journey in journeys ?? Array.Empty<CombatJourneyTrainingEpisode>())
        {
            if (journey == null
                || !journey.Complete
                || (!string.Equals(journey.Outcome, "victory", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(journey.Outcome, "defeat", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var terminal = string.Equals(
                journey.Outcome,
                "victory",
                StringComparison.OrdinalIgnoreCase)
                ? 1d
                : -1d;
            var ordered = (journey.Battles ?? new List<CombatJourneyBattleTrainingRecord>())
                .Where(battle => battle != null && battle.BattleSessionId > 0)
                .OrderBy(battle => battle.BattleIndex)
                .ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                if (!bySession.TryGetValue(ordered[index].BattleSessionId, out var episode))
                {
                    continue;
                }

                episode.JourneyRunId = journey.JourneyRunId ?? "";
                episode.JourneyBattleIndex = ordered[index].BattleIndex;
                var remainingBattles = ordered.Count - index - 1;
                var journeyReturn = terminal * Math.Pow(
                    Math.Max(0.5d, Math.Min(1d, battleDiscount)),
                    remainingBattles);
                for (var frameIndex = 0; frameIndex < episode.Frames.Count; frameIndex++)
                {
                    var frame = episode.Frames[frameIndex];
                    frame.LongTermReturn = journeyReturn
                                           * Math.Pow(
                                               0.99d,
                                               episode.Frames.Count - frameIndex - 1);
                    frame.WinTarget = terminal > 0d ? 1d : 0d;
                    frame.DeathTarget = terminal < 0d ? 1d : 0d;
                    frame.StateFeatures["journeyBattleIndex"] = ordered[index].BattleIndex;
                    frame.StateFeatures["journeyRemainingBattles"] = remainingBattles;
                }
            }
        }
    }
}
