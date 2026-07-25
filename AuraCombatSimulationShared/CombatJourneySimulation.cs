using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace AuraCombatSimulation.Shared;

public sealed class CombatRewardCardDefinition
{
    public string CardId { get; set; } = "";

    public double BaseValue { get; set; }

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatJourneyStageDefinition
{
    public string StageId { get; set; } = "";

    public List<string> EncounterPool { get; set; } = new();

    public bool IsBoss { get; set; }

    public bool OfferRewardAfterVictory { get; set; } = true;
}

public sealed class CombatJourneyDefinition
{
    public string JourneyId { get; set; } = "";

    public string RulesetVersion { get; set; } = "1";

    public CombatPlayerSetup Player { get; set; } = new();

    public List<CombatJourneyStageDefinition> Stages { get; set; } = new();

    public List<CombatRewardCardDefinition> RewardPool { get; set; } = new();

    public int RewardChoices { get; set; } = 3;

    public Dictionary<string, double> RolePrior { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> BuildTendency { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> BossPreference { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool AllowSkipReward { get; set; } = true;

    public int InitialDraw { get; set; } = 5;

    public int DrawPerTurn { get; set; } = 5;

    public int HandLimit { get; set; } = 10;

    public bool RetainBlockBetweenTurns { get; set; }

    public bool RequireAuthoritativeRules { get; set; } = true;

    public CombatSimulationTraceLevel TraceLevel { get; set; } =
        CombatSimulationTraceLevel.Summary;

    public CombatSimulationLimits Limits { get; set; } = new();
}

public sealed class CombatJourneyPlannedEncounter
{
    public int Index { get; set; }

    public string StageId { get; set; } = "";

    public string EnemyId { get; set; } = "";

    public bool IsBoss { get; set; }

    public List<string> RewardOffer { get; set; } = new();
}

public sealed class CombatJourneyWorldPlan
{
    public string JourneyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public List<CombatJourneyPlannedEncounter> Encounters { get; set; } = new();
}

public sealed class CombatRewardScore
{
    public string CardId { get; set; } = "";

    public double Total { get; set; }

    public double BaseValue { get; set; }

    public double SystemFit { get; set; }

    public double BuildTendency { get; set; }

    public double BossFit { get; set; }

    public double GapFill { get; set; }

    public double BloatPenalty { get; set; }

    public double RedundancyPenalty { get; set; }
}

public sealed class CombatRewardSelection
{
    public int EncounterIndex { get; set; }

    public string StageId { get; set; } = "";

    public List<string> OfferedCardIds { get; set; } = new();

    public string SelectedCardId { get; set; } = "";

    public bool Skipped { get; set; }

    public List<CombatRewardScore> Scores { get; set; } = new();
}

public sealed class CombatJourneyCheckpoint
{
    public string JourneyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public int NextEncounterIndex { get; set; }

    public int CurrentHp { get; set; }

    public List<string> Deck { get; set; } = new();

    public List<CombatSimulationResult> Battles { get; set; } = new();

    public List<CombatRewardSelection> Rewards { get; set; } = new();

    public bool Completed { get; set; }
}

public sealed class CombatJourneyResult
{
    public string JourneyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public bool ReachedBoss { get; set; }

    public bool BossVictory { get; set; }

    public bool JourneyVictory { get; set; }

    public bool Invalid { get; set; }

    public int CompletedBattles { get; set; }

    public int FinalPlayerHp { get; set; }

    public List<string> FinalDeck { get; set; } = new();

    public List<CombatSimulationResult> Battles { get; set; } = new();

    public List<CombatRewardSelection> Rewards { get; set; } = new();

    public CombatJourneyCheckpoint Checkpoint { get; set; } = new();
}

public sealed class CombatJourneyPairResult
{
    public CombatJourneyWorldPlan WorldPlan { get; set; } = new();

    public CombatJourneyResult Baseline { get; set; } = new();

    public CombatJourneyResult Learned { get; set; } = new();
}

public static class CombatJourneyWorldPlanner
{
    public static CombatJourneyWorldPlan Build(CombatJourneyDefinition definition, ulong worldSeed)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        Validate(definition);
        var plan = new CombatJourneyWorldPlan
        {
            JourneyId = definition.JourneyId,
            WorldSeed = worldSeed
        };
        string previousEnemy = "";
        for (var index = 0; index < definition.Stages.Count; index++)
        {
            var stage = definition.Stages[index];
            var enemyIndex = NextIndex(
                worldSeed,
                "encounter",
                index,
                stage.EncounterPool.Count);
            var enemyId = stage.EncounterPool[enemyIndex];
            if (stage.EncounterPool.Count > 1
                && string.Equals(enemyId, previousEnemy, StringComparison.OrdinalIgnoreCase))
            {
                enemyId = stage.EncounterPool[(enemyIndex + 1) % stage.EncounterPool.Count];
            }
            previousEnemy = enemyId;
            var planned = new CombatJourneyPlannedEncounter
            {
                Index = index,
                StageId = stage.StageId,
                EnemyId = enemyId,
                IsBoss = stage.IsBoss
            };
            if (stage.OfferRewardAfterVictory && !stage.IsBoss)
            {
                planned.RewardOffer = PickDistinct(
                    definition.RewardPool.Select(item => item.CardId).ToList(),
                    Math.Min(
                        Math.Max(1, definition.RewardChoices),
                        definition.RewardPool.Count),
                    worldSeed,
                    "reward",
                    index);
            }
            plan.Encounters.Add(planned);
        }
        plan.PlanHash = HashPlan(plan);
        return plan;
    }

    public static void Validate(CombatJourneyDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.JourneyId))
        {
            throw new ArgumentException("JourneyId is required.", nameof(definition));
        }
        if (definition.Player == null || definition.Player.Deck.Count == 0)
        {
            throw new ArgumentException("A non-empty starting deck is required.", nameof(definition));
        }
        if (definition.Stages.Count == 0
            || definition.Stages.Any(stage => stage.EncounterPool.Count == 0))
        {
            throw new ArgumentException("Every journey stage requires an encounter pool.", nameof(definition));
        }
        if (definition.Stages.Count(stage => stage.IsBoss) != 1
            || !definition.Stages[definition.Stages.Count - 1].IsBoss)
        {
            throw new ArgumentException("The journey requires one final boss stage.", nameof(definition));
        }
        var duplicateRewards = definition.RewardPool
            .Where(item => string.IsNullOrWhiteSpace(item.CardId))
            .Select(item => item.CardId)
            .Concat(
                definition.RewardPool
                    .GroupBy(item => item.CardId, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key))
            .ToList();
        if (duplicateRewards.Count > 0)
        {
            throw new ArgumentException("Reward card ids must be non-empty and unique.", nameof(definition));
        }
    }

    private static List<string> PickDistinct(
        IReadOnlyList<string> source,
        int count,
        ulong seed,
        string stream,
        int step)
    {
        var remaining = source.OrderBy(value => value, StringComparer.Ordinal).ToList();
        var selected = new List<string>();
        for (var index = 0; index < count && remaining.Count > 0; index++)
        {
            var selectedIndex = NextIndex(seed, stream + ":" + index, step, remaining.Count);
            selected.Add(remaining[selectedIndex]);
            remaining.RemoveAt(selectedIndex);
        }
        return selected;
    }

    private static int NextIndex(ulong seed, string stream, int step, int count)
    {
        if (count <= 1)
        {
            return 0;
        }
        var value = seed ^ StableHash(stream) ^ ((ulong)(step + 1) * 0x9E3779B97F4A7C15UL);
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (int)(value % (ulong)count);
    }

    private static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var item in Encoding.UTF8.GetBytes(value ?? ""))
        {
            hash ^= item;
            hash *= prime;
        }
        return hash;
    }

    private static string HashPlan(CombatJourneyWorldPlan plan)
    {
        var canonical = plan.JourneyId
                        + "|" + plan.WorldSeed
                        + "|" + string.Join(
                            ";",
                            plan.Encounters.Select(item =>
                                item.Index
                                + ":" + item.StageId
                                + ":" + item.EnemyId
                                + ":" + item.IsBoss
                                + ":" + string.Join(",", item.RewardOffer)));
        return StableHash(canonical).ToString("X16");
    }
}

public static class CombatJourneyRewardSelector
{
    private static readonly string[] CoreFeatures =
    {
        "burst", "sustained", "defense", "heal", "draw", "energy", "cost",
        "generation", "status", "scaling", "cycling", "aoe", "reliability", "risk"
    };

    public static CombatRewardSelection Select(
        CombatJourneyDefinition definition,
        CombatJourneyPlannedEncounter encounter,
        IReadOnlyList<string> currentDeck)
    {
        var lookup = definition.RewardPool.ToDictionary(
            item => item.CardId,
            StringComparer.OrdinalIgnoreCase);
        var deckCounts = currentDeck
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var deckFeatures = AggregateFeatures(currentDeck, lookup);
        var progress = definition.Stages.Count <= 1
            ? 1d
            : (double)encounter.Index / (definition.Stages.Count - 1);
        var scores = new List<CombatRewardScore>();
        foreach (var cardId in encounter.RewardOffer)
        {
            if (!lookup.TryGetValue(cardId, out var reward))
            {
                continue;
            }
            var roleWeight = 1d - progress;
            var bossWeight = 0.25d + progress * 0.75d;
            var systemFit = Dot(reward.Features, deckFeatures) * (0.25d + progress * 0.75d);
            var tendency = Dot(reward.Features, definition.BuildTendency)
                           + Dot(reward.Features, definition.RolePrior) * roleWeight;
            var bossFit = Dot(reward.Features, definition.BossPreference) * bossWeight;
            var gapFill = GapFill(reward.Features, deckFeatures);
            var copies = deckCounts.TryGetValue(cardId, out var count) ? count : 0;
            var redundancy = copies <= 0 ? 0d : Math.Sqrt(copies) * 0.75d;
            var bloat = Math.Max(0, currentDeck.Count - definition.Player.Deck.Count) * 0.04d;
            var riskPenalty = Feature(reward.Features, "risk") * 0.5d;
            scores.Add(new CombatRewardScore
            {
                CardId = cardId,
                BaseValue = reward.BaseValue,
                SystemFit = systemFit,
                BuildTendency = tendency,
                BossFit = bossFit,
                GapFill = gapFill,
                BloatPenalty = bloat + riskPenalty,
                RedundancyPenalty = redundancy,
                Total = reward.BaseValue
                        + systemFit
                        + tendency
                        + bossFit
                        + gapFill
                        - bloat
                        - riskPenalty
                        - redundancy
            });
        }
        scores = scores
            .OrderByDescending(item => item.Total)
            .ThenBy(item => item.CardId, StringComparer.Ordinal)
            .ToList();
        var best = scores.FirstOrDefault();
        var skip = definition.AllowSkipReward && (best == null || best.Total <= 0d);
        return new CombatRewardSelection
        {
            EncounterIndex = encounter.Index,
            StageId = encounter.StageId,
            OfferedCardIds = new List<string>(encounter.RewardOffer),
            SelectedCardId = skip ? "" : best?.CardId ?? "",
            Skipped = skip,
            Scores = scores
        };
    }

    private static Dictionary<string, double> AggregateFeatures(
        IReadOnlyList<string> deck,
        IReadOnlyDictionary<string, CombatRewardCardDefinition> definitions)
    {
        var result = CoreFeatures.ToDictionary(
            feature => feature,
            _ => 0d,
            StringComparer.OrdinalIgnoreCase);
        foreach (var cardId in deck)
        {
            if (!definitions.TryGetValue(cardId, out var card))
            {
                continue;
            }
            foreach (var feature in card.Features)
            {
                result[feature.Key] = result.TryGetValue(feature.Key, out var current)
                    ? current + feature.Value
                    : feature.Value;
            }
        }
        var scale = Math.Max(1d, deck.Count);
        foreach (var key in result.Keys.ToList())
        {
            result[key] /= scale;
        }
        return result;
    }

    private static double Dot(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        return left.Sum(item => item.Value * Feature(right, item.Key));
    }

    private static double GapFill(
        IReadOnlyDictionary<string, double> card,
        IReadOnlyDictionary<string, double> deck)
    {
        var defenseGap = Math.Max(0d, 0.35d - Feature(deck, "defense"));
        var drawGap = Math.Max(0d, 0.2d - Feature(deck, "draw"));
        var reliabilityGap = Math.Max(0d, 0.5d - Feature(deck, "reliability"));
        return Feature(card, "defense") * defenseGap
               + Feature(card, "draw") * drawGap
               + Feature(card, "reliability") * reliabilityGap;
    }

    private static double Feature(IReadOnlyDictionary<string, double> values, string key)
    {
        return values != null && values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }
}

public sealed class CombatJourneyRunner
{
    private readonly CombatSimulationEngine engine;

    public CombatJourneyRunner(CombatSimulationEngine? engine = null)
    {
        this.engine = engine ?? new CombatSimulationEngine();
    }

    public CombatJourneyResult Run(
        CombatJourneyDefinition definition,
        CombatJourneyWorldPlan plan,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory policyFactory,
        CombatJourneyCheckpoint? resumeFrom = null,
        Action<CombatJourneyCheckpoint>? checkpointSink = null,
        CancellationToken cancellationToken = default)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (policyFactory == null) throw new ArgumentNullException(nameof(policyFactory));
        CombatJourneyWorldPlanner.Validate(definition);
        if (!string.Equals(definition.JourneyId, plan.JourneyId, StringComparison.Ordinal)
            || plan.Encounters.Count != definition.Stages.Count)
        {
            throw new ArgumentException("World plan does not match the journey.", nameof(plan));
        }
        var checkpoint = InitializeCheckpoint(definition, plan, policyFactory.PolicyId, resumeFrom);
        var bossIndex = plan.Encounters.FindIndex(item => item.IsBoss);
        for (var index = checkpoint.NextEncounterIndex; index < plan.Encounters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encounter = plan.Encounters[index];
            var scenario = new CombatScenarioDefinition
            {
                ScenarioId = definition.JourneyId + ":" + encounter.StageId,
                RulesetVersion = definition.RulesetVersion,
                Seed = NamedBattleSeed(plan.WorldSeed, index),
                Player = new CombatPlayerSetup
                {
                    RoleId = definition.Player.RoleId,
                    MaxHp = definition.Player.MaxHp,
                    CurrentHp = checkpoint.CurrentHp,
                    BaseEnergy = definition.Player.BaseEnergy,
                    Deck = new List<string>(checkpoint.Deck),
                    InitialStatuses = definition.Player.InitialStatuses
                        .Select(CloneStatus)
                        .ToList()
                },
                Enemies =
                {
                    new CombatEnemySetup
                    {
                        EnemyId = encounter.EnemyId,
                        InstanceKey = encounter.StageId + ":1"
                    }
                },
                InitialDraw = definition.InitialDraw,
                DrawPerTurn = definition.DrawPerTurn,
                HandLimit = definition.HandLimit,
                RetainBlockBetweenTurns = definition.RetainBlockBetweenTurns,
                RequireAuthoritativeRules = definition.RequireAuthoritativeRules,
                TraceLevel = definition.TraceLevel,
                Limits = definition.Limits.Normalize()
            };
            var battle = engine.Run(
                scenario,
                ruleset,
                policyFactory.Create(),
                cancellationToken);
            checkpoint.Battles.Add(battle);
            checkpoint.NextEncounterIndex = index + 1;
            checkpoint.CurrentHp = Math.Max(0, battle.FinalPlayerHp);
            if (battle.Outcome == CombatSimulationOutcome.Victory
                && encounter.RewardOffer.Count > 0)
            {
                var selection = CombatJourneyRewardSelector.Select(
                    definition,
                    encounter,
                    checkpoint.Deck);
                checkpoint.Rewards.Add(selection);
                if (!selection.Skipped)
                {
                    checkpoint.Deck.Add(selection.SelectedCardId);
                }
            }
            checkpoint.Completed = index == plan.Encounters.Count - 1
                                   || battle.Outcome != CombatSimulationOutcome.Victory;
            checkpointSink?.Invoke(CloneCheckpoint(checkpoint));
            if (battle.Outcome != CombatSimulationOutcome.Victory)
            {
                break;
            }
        }
        var bossBattle = bossIndex >= 0 && checkpoint.Battles.Count > bossIndex
            ? checkpoint.Battles[bossIndex]
            : null;
        var invalid = checkpoint.Battles.Any(item =>
            item.Outcome == CombatSimulationOutcome.Invalid
            || item.SemanticCoverage < 0.999999d);
        return new CombatJourneyResult
        {
            JourneyId = definition.JourneyId,
            WorldSeed = plan.WorldSeed,
            PlanHash = plan.PlanHash,
            PolicyId = policyFactory.PolicyId,
            ReachedBoss = bossBattle != null,
            BossVictory = bossBattle?.Outcome == CombatSimulationOutcome.Victory,
            JourneyVictory = bossBattle?.Outcome == CombatSimulationOutcome.Victory && !invalid,
            Invalid = invalid,
            CompletedBattles = checkpoint.Battles.Count,
            FinalPlayerHp = checkpoint.CurrentHp,
            FinalDeck = new List<string>(checkpoint.Deck),
            Battles = new List<CombatSimulationResult>(checkpoint.Battles),
            Rewards = new List<CombatRewardSelection>(checkpoint.Rewards),
            Checkpoint = CloneCheckpoint(checkpoint)
        };
    }

    public CombatJourneyPairResult RunPaired(
        CombatJourneyDefinition definition,
        ulong worldSeed,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory baseline,
        ICombatSimulationPolicyFactory learned,
        CancellationToken cancellationToken = default)
    {
        var plan = CombatJourneyWorldPlanner.Build(definition, worldSeed);
        return new CombatJourneyPairResult
        {
            WorldPlan = plan,
            Baseline = Run(
                definition,
                plan,
                ruleset,
                baseline,
                cancellationToken: cancellationToken),
            Learned = Run(
                definition,
                plan,
                ruleset,
                learned,
                cancellationToken: cancellationToken)
        };
    }

    private static CombatJourneyCheckpoint InitializeCheckpoint(
        CombatJourneyDefinition definition,
        CombatJourneyWorldPlan plan,
        string policyId,
        CombatJourneyCheckpoint? resumeFrom)
    {
        if (resumeFrom == null)
        {
            return new CombatJourneyCheckpoint
            {
                JourneyId = definition.JourneyId,
                WorldSeed = plan.WorldSeed,
                PlanHash = plan.PlanHash,
                PolicyId = policyId,
                CurrentHp = definition.Player.CurrentHp,
                Deck = new List<string>(definition.Player.Deck)
            };
        }
        if (!string.Equals(resumeFrom.JourneyId, definition.JourneyId, StringComparison.Ordinal)
            || resumeFrom.WorldSeed != plan.WorldSeed
            || !string.Equals(resumeFrom.PlanHash, plan.PlanHash, StringComparison.Ordinal)
            || !string.Equals(resumeFrom.PolicyId, policyId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Checkpoint identity does not match the requested run.", nameof(resumeFrom));
        }
        return CloneCheckpoint(resumeFrom);
    }

    private static CombatJourneyCheckpoint CloneCheckpoint(CombatJourneyCheckpoint source)
    {
        return new CombatJourneyCheckpoint
        {
            JourneyId = source.JourneyId,
            WorldSeed = source.WorldSeed,
            PlanHash = source.PlanHash,
            PolicyId = source.PolicyId,
            NextEncounterIndex = source.NextEncounterIndex,
            CurrentHp = source.CurrentHp,
            Deck = new List<string>(source.Deck),
            Battles = new List<CombatSimulationResult>(source.Battles),
            Rewards = new List<CombatRewardSelection>(source.Rewards),
            Completed = source.Completed
        };
    }

    private static CombatInitialStatus CloneStatus(CombatInitialStatus source)
    {
        return source.Clone();
    }

    private static ulong NamedBattleSeed(ulong worldSeed, int index)
    {
        var value = worldSeed + ((ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
