using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace AuraCombatSimulation.Shared;

public enum CombatCampaignEncounterKind
{
    Normal,
    Elite,
    Boss,
    FinalBoss
}

public enum CombatCampaignRewardKind
{
    Card,
    Relic,
    Blessing
}

public sealed class CombatCampaignAttributePreset
{
    public int Main { get; set; }

    public int Secondary { get; set; }

    public int Unselected { get; set; }
}

public sealed class CombatCampaignLayerDefinition
{
    public int LayerNumber { get; set; }

    public int NativeBand { get; set; }

    public CombatCampaignAttributePreset Attributes { get; set; } = new();

    public List<CombatCampaignEncounterKind> Route { get; set; } = new();

    public int MaxHpGainAfterClear { get; set; } = 40;
}

public sealed class CombatCampaignEnemyCatalogEntry
{
    public string EnemyId { get; set; } = "";

    public int NativeLevel { get; set; } = 1;
}

public sealed class CombatCampaignEncounterDefinition
{
    public string EncounterId { get; set; } = "";

    public CombatCampaignEncounterKind Kind { get; set; }

    public int NativeBand { get; set; }

    public List<string> EnemyIds { get; set; } = new();
}

public sealed class CombatCampaignRewardDefinition
{
    public string RewardId { get; set; } = "";

    public CombatCampaignRewardKind Kind { get; set; }

    public int Tier { get; set; } = 1;

    public double BaseValue { get; set; }

    public bool Negative { get; set; }

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Approximate;

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PermanentAttributeBonuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int MainAttributeCapBonus { get; set; }

    public int SecondaryAttributeCapBonus { get; set; }

    public int UnselectedAttributeCapBonus { get; set; }

    public int MaxHpBonus { get; set; }

    public List<CombatInitialStatus> InitialStatuses { get; set; } = new();
}

public sealed class CombatCampaignHardAffixDefinition
{
    public string AffixId { get; set; } = "";

    public int Stacks { get; set; } = 1;

    public bool CombatRelevant { get; set; }

    public bool Implemented { get; set; }
}

public sealed class CombatCampaignDifficultyDefinition
{
    public string DifficultyId { get; set; } = "normal";

    public string DisplayName { get; set; } = "普通难度";

    public double EnemyHpMultiplier { get; set; } = 1d;

    public double EnemyAttackMultiplier { get; set; } = 1d;

    public bool ApplyGameLevelShield { get; set; }

    public List<CombatInitialStatus> EnemyInitialStatuses { get; set; } = new();

    public List<CombatCampaignHardAffixDefinition> HardAffixes { get; set; } = new();
}

public sealed class CombatCampaignDefinition
{
    public int SchemaVersion { get; set; } = 2;

    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "2";

    public string RulesetVersion { get; set; } = "1";

    public CombatPlayerSetup Player { get; set; } = new();

    public string MainAttributeId { get; set; } = "Strength";

    public string SecondaryAttributeId { get; set; } = "Wisdom";

    public List<string> AttributeIds { get; set; } =
        new() { "Strength", "Lucky", "Perceive", "Wisdom" };

    public int MainAttributeUpperBound { get; set; } = 40;

    public int SecondaryAttributeUpperBound { get; set; } = 39;

    public int UnselectedAttributeUpperBound { get; set; } = 20;

    public List<CombatCampaignLayerDefinition> Layers { get; set; } = new();

    public List<CombatCampaignEnemyCatalogEntry> Enemies { get; set; } = new();

    public List<CombatCampaignEncounterDefinition> Encounters { get; set; } = new();

    public List<CombatCampaignRewardDefinition> Rewards { get; set; } = new();

    public List<CombatCampaignDifficultyDefinition> Difficulties { get; set; } = new();

    public int CardOfferRounds { get; set; } = 2;

    public int CardChoicesPerRound { get; set; } = 3;

    public int RelicLimit { get; set; } = 6;

    public bool AllowSkipCardReward { get; set; } = true;

    public bool BlessingsAreMandatory { get; set; } = true;

    public bool ExcludeNegativeBlessings { get; set; } = true;

    public bool RewardAfterFinalBoss { get; set; }

    public Dictionary<string, double> RolePrior { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> BuildTendency { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> BossPreference { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int InitialDraw { get; set; } = 5;

    public int DrawPerTurn { get; set; } = 5;

    public int HandLimit { get; set; } = 10;

    public bool RetainBlockBetweenTurns { get; set; }

    public bool RequireAuthoritativeRules { get; set; } = true;

    public CombatSimulationTraceLevel TraceLevel { get; set; } =
        CombatSimulationTraceLevel.Summary;

    public CombatSimulationLimits Limits { get; set; } = new();
}

public sealed class CombatCampaignRewardOffer
{
    public List<List<string>> CardRounds { get; set; } = new();

    public string RelicId { get; set; } = "";

    public string BlessingId { get; set; } = "";
}

public sealed class CombatCampaignPlannedEncounter
{
    public int Index { get; set; }

    public int LayerNumber { get; set; }

    public int EncounterInLayer { get; set; }

    public int GameLevel { get; set; }

    public string EncounterId { get; set; } = "";

    public CombatCampaignEncounterKind Kind { get; set; }

    public List<string> EnemyIds { get; set; } = new();

    public bool StartsLayer { get; set; }

    public bool EndsLayer { get; set; }

    public CombatCampaignRewardOffer RewardOffer { get; set; } = new();
}

public sealed class CombatCampaignWorldPlan
{
    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string DifficultyId { get; set; } = "normal";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public List<CombatCampaignPlannedEncounter> Encounters { get; set; } = new();
}

public sealed class CombatCampaignRewardScore
{
    public string RewardId { get; set; } = "";

    public double Total { get; set; }

    public double BaseValue { get; set; }

    public double TierValue { get; set; }

    public double SystemFit { get; set; }

    public double BuildTendency { get; set; }

    public double BossFit { get; set; }

    public double BloatPenalty { get; set; }

    public double RedundancyPenalty { get; set; }
}

public sealed class CombatCampaignCardDecision
{
    public int Round { get; set; }

    public List<string> OfferedIds { get; set; } = new();

    public string SelectedId { get; set; } = "";

    public bool Skipped { get; set; }

    public List<CombatCampaignRewardScore> Scores { get; set; } = new();
}

public sealed class CombatCampaignRelicDecision
{
    public string OfferedId { get; set; } = "";

    public string Decision { get; set; } = "none";

    public string ReplacedId { get; set; } = "";

    public List<CombatCampaignRewardScore> Scores { get; set; } = new();
}

public sealed class CombatCampaignBlessingDecision
{
    public string OfferedId { get; set; } = "";

    public bool Acquired { get; set; }
}

public sealed class CombatCampaignRewardDecision
{
    public int EncounterIndex { get; set; }

    public string EncounterId { get; set; } = "";

    public List<CombatCampaignCardDecision> Cards { get; set; } = new();

    public CombatCampaignRelicDecision Relic { get; set; } = new();

    public CombatCampaignBlessingDecision Blessing { get; set; } = new();
}

public sealed class CombatCampaignState
{
    public int CurrentLayer { get; set; }

    public int CurrentGameLevel { get; set; }

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public Dictionary<string, int> Attributes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> LayerBaseAttributes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PermanentAttributeBonuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> AttributeUpperBounds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deck { get; set; } = new();

    public List<string> Relics { get; set; } = new();

    public List<string> Blessings { get; set; } = new();

    public List<string> UnsupportedProgressionRules { get; set; } = new();
}

public sealed class CombatCampaignCheckpoint
{
    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string DifficultyId { get; set; } = "normal";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public int NextEncounterIndex { get; set; }

    public CombatCampaignState State { get; set; } = new();

    public List<CombatSimulationResult> Battles { get; set; } = new();

    public List<CombatCampaignRewardDecision> Rewards { get; set; } = new();

    public bool Completed { get; set; }
}

public sealed class CombatCampaignResult
{
    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string DifficultyId { get; set; } = "normal";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public bool ReachedFinalBoss { get; set; }

    public bool FinalBossVictory { get; set; }

    public bool CampaignVictory { get; set; }

    public bool Invalid { get; set; }

    public int CompletedBattles { get; set; }

    public double BattleSemanticCoverage { get; set; }

    public double ProgressionSemanticCoverage { get; set; }

    public CombatCampaignState FinalState { get; set; } = new();

    public List<CombatSimulationResult> Battles { get; set; } = new();

    public List<CombatCampaignRewardDecision> Rewards { get; set; } = new();

    public List<string> UnsupportedDefinitions { get; set; } = new();

    public CombatCampaignCheckpoint Checkpoint { get; set; } = new();
}

public sealed class CombatCampaignPairResult
{
    public CombatCampaignWorldPlan WorldPlan { get; set; } = new();

    public CombatCampaignResult Baseline { get; set; } = new();

    public CombatCampaignResult Learned { get; set; } = new();
}

public static class CombatCampaignWorldPlanner
{
    public static CombatCampaignWorldPlan Build(
        CombatCampaignDefinition definition,
        string difficultyId,
        ulong worldSeed)
    {
        Validate(definition);
        var difficulty = ResolveDifficulty(definition, difficultyId);
        var plan = new CombatCampaignWorldPlan
        {
            CampaignId = definition.CampaignId,
            CampaignVersion = definition.CampaignVersion,
            DifficultyId = difficulty.DifficultyId,
            WorldSeed = worldSeed
        };
        var usedRelics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedBlessings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalIndex = 0;
        foreach (var layer in definition.Layers.OrderBy(item => item.LayerNumber))
        {
            for (var localIndex = 0; localIndex < layer.Route.Count; localIndex++)
            {
                var kind = layer.Route[localIndex];
                var pool = definition.Encounters
                    .Where(item => item.Kind == kind
                                   && (kind == CombatCampaignEncounterKind.FinalBoss
                                       || item.NativeBand == layer.NativeBand
                                       || item.NativeBand == -1))
                    .OrderBy(item => item.EncounterId, StringComparer.Ordinal)
                    .ToList();
                if (pool.Count == 0)
                {
                    throw new ArgumentException(
                        "No encounter pool for layer " + layer.LayerNumber + " / " + kind + ".");
                }
                var selected = pool[NextIndex(
                    worldSeed,
                    "encounter:" + kind,
                    globalIndex,
                    pool.Count)];
                var planned = new CombatCampaignPlannedEncounter
                {
                    Index = globalIndex,
                    LayerNumber = layer.LayerNumber,
                    EncounterInLayer = localIndex,
                    GameLevel = globalIndex,
                    EncounterId = selected.EncounterId,
                    Kind = selected.Kind,
                    EnemyIds = new List<string>(selected.EnemyIds),
                    StartsLayer = localIndex == 0,
                    EndsLayer = localIndex == layer.Route.Count - 1
                };
                if (kind != CombatCampaignEncounterKind.FinalBoss
                    || definition.RewardAfterFinalBoss)
                {
                    planned.RewardOffer = BuildRewardOffer(
                        definition,
                        kind,
                        worldSeed,
                        globalIndex,
                        usedRelics,
                        usedBlessings);
                }
                plan.Encounters.Add(planned);
                globalIndex++;
            }
        }
        plan.PlanHash = HashPlan(plan);
        return plan;
    }

    public static void Validate(CombatCampaignDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definition.SchemaVersion != 2)
        {
            throw new ArgumentException("Campaign schemaVersion must be 2.", nameof(definition));
        }
        if (string.IsNullOrWhiteSpace(definition.CampaignId)
            || string.IsNullOrWhiteSpace(definition.CampaignVersion))
        {
            throw new ArgumentException("Campaign identity is required.", nameof(definition));
        }
        if (definition.Player == null || definition.Player.Deck.Count == 0)
        {
            throw new ArgumentException("Campaign requires a frozen starting deck.", nameof(definition));
        }
        var attributes = definition.AttributeIds
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (attributes.Count != 4
            || !attributes.Contains(definition.MainAttributeId, StringComparer.OrdinalIgnoreCase)
            || !attributes.Contains(definition.SecondaryAttributeId, StringComparer.OrdinalIgnoreCase)
            || string.Equals(
                definition.MainAttributeId,
                definition.SecondaryAttributeId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Campaign requires four attributes and distinct main/secondary ids.");
        }
        var layers = definition.Layers.OrderBy(item => item.LayerNumber).ToList();
        if (layers.Count != 7
            || layers.Select(item => item.LayerNumber).Where((number, index) => number != index + 1).Any())
        {
            throw new ArgumentException("Standard campaign requires layers 1 through 7.");
        }
        var normalRoute = new[]
        {
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Elite,
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Normal,
            CombatCampaignEncounterKind.Boss
        };
        if (layers.Take(6).Any(layer => !layer.Route.SequenceEqual(normalRoute))
            || layers[6].Route.Count != 1
            || layers[6].Route[0] != CombatCampaignEncounterKind.FinalBoss)
        {
            throw new ArgumentException("Standard campaign route must contain 36 fights plus one final boss.");
        }
        if (definition.Encounters.Any(item =>
                string.IsNullOrWhiteSpace(item.EncounterId) || item.EnemyIds.Count == 0)
            || definition.Encounters
                .GroupBy(item => item.EncounterId, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Encounter ids must be non-empty and unique.");
        }
        if (definition.Difficulties.Count < 2
            || !definition.Difficulties.Any(item =>
                string.Equals(item.DifficultyId, "normal", StringComparison.OrdinalIgnoreCase))
            || !definition.Difficulties.Any(item =>
                string.Equals(item.DifficultyId, "advanced", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Campaign requires normal and advanced difficulty profiles.");
        }
    }

    public static CombatCampaignDifficultyDefinition ResolveDifficulty(
        CombatCampaignDefinition definition,
        string difficultyId)
    {
        var selected = definition.Difficulties.FirstOrDefault(item =>
            string.Equals(
                item.DifficultyId,
                difficultyId?.Trim(),
                StringComparison.OrdinalIgnoreCase));
        return selected ?? definition.Difficulties.First(item =>
            string.Equals(item.DifficultyId, "normal", StringComparison.OrdinalIgnoreCase));
    }

    private static CombatCampaignRewardOffer BuildRewardOffer(
        CombatCampaignDefinition definition,
        CombatCampaignEncounterKind kind,
        ulong worldSeed,
        int encounterIndex,
        HashSet<string> usedRelics,
        HashSet<string> usedBlessings)
    {
        var result = new CombatCampaignRewardOffer();
        var cards = definition.Rewards
            .Where(item => item.Kind == CombatCampaignRewardKind.Card)
            .Select(item => item.RewardId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        for (var round = 0; round < definition.CardOfferRounds; round++)
        {
            result.CardRounds.Add(PickDistinct(
                cards,
                definition.CardChoicesPerRound,
                worldSeed,
                "card:" + round,
                encounterIndex));
        }
        var (minimumTier, maximumTier) = RewardTierRange(kind);
        var relics = definition.Rewards
            .Where(item => item.Kind == CombatCampaignRewardKind.Relic
                           && item.Tier >= minimumTier
                           && item.Tier <= maximumTier)
            .Select(item => item.RewardId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        result.RelicId = PickUnused(
            relics,
            usedRelics,
            worldSeed,
            "relic",
            encounterIndex);
        var blessings = definition.Rewards
            .Where(item => item.Kind == CombatCampaignRewardKind.Blessing
                           && item.Tier >= minimumTier
                           && item.Tier <= maximumTier
                           && (!definition.ExcludeNegativeBlessings || !item.Negative))
            .Select(item => item.RewardId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        result.BlessingId = PickUnused(
            blessings,
            usedBlessings,
            worldSeed,
            "blessing",
            encounterIndex);
        return result;
    }

    private static (int Minimum, int Maximum) RewardTierRange(CombatCampaignEncounterKind kind)
    {
        return kind switch
        {
            CombatCampaignEncounterKind.Normal => (1, 2),
            CombatCampaignEncounterKind.Elite => (2, 3),
            _ => (3, 4)
        };
    }

    private static string PickUnused(
        IReadOnlyList<string> source,
        HashSet<string> used,
        ulong seed,
        string stream,
        int step)
    {
        if (source.Count == 0)
        {
            return "";
        }
        var candidates = source.Where(item => !used.Contains(item)).ToList();
        if (candidates.Count == 0)
        {
            candidates = source.ToList();
        }
        var selected = candidates[NextIndex(seed, stream, step, candidates.Count)];
        used.Add(selected);
        return selected;
    }

    private static List<string> PickDistinct(
        IReadOnlyList<string> source,
        int count,
        ulong seed,
        string stream,
        int step)
    {
        var remaining = source.ToList();
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
        if (count <= 1) return 0;
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

    private static string HashPlan(CombatCampaignWorldPlan plan)
    {
        var canonical = plan.CampaignId
                        + "|" + plan.CampaignVersion
                        + "|" + plan.DifficultyId
                        + "|" + plan.WorldSeed
                        + "|" + string.Join(
                            ";",
                            plan.Encounters.Select(item =>
                                item.Index + ":" + item.LayerNumber + ":" + item.GameLevel
                                + ":" + item.EncounterId + ":" + item.Kind
                                + ":" + string.Join(",", item.EnemyIds)
                                + ":" + string.Join(
                                    "/",
                                    item.RewardOffer.CardRounds.Select(round =>
                                        string.Join(",", round)))
                                + ":" + item.RewardOffer.RelicId
                                + ":" + item.RewardOffer.BlessingId));
        return StableHash(canonical).ToString("X16");
    }
}

public static class CombatCampaignRewardSelector
{
    public static CombatCampaignRewardDecision Apply(
        CombatCampaignDefinition definition,
        CombatCampaignPlannedEncounter encounter,
        CombatCampaignState state)
    {
        var lookup = definition.Rewards.ToDictionary(
            item => item.RewardId,
            StringComparer.OrdinalIgnoreCase);
        var result = new CombatCampaignRewardDecision
        {
            EncounterIndex = encounter.Index,
            EncounterId = encounter.EncounterId
        };
        for (var round = 0; round < encounter.RewardOffer.CardRounds.Count; round++)
        {
            var offer = encounter.RewardOffer.CardRounds[round];
            var scores = ScoreRewards(
                definition,
                state,
                offer,
                lookup,
                encounter.Index,
                CombatCampaignRewardKind.Card);
            var best = scores.FirstOrDefault();
            var skipped = definition.AllowSkipCardReward && (best == null || best.Total <= 0d);
            var decision = new CombatCampaignCardDecision
            {
                Round = round + 1,
                OfferedIds = new List<string>(offer),
                SelectedId = skipped ? "" : best?.RewardId ?? "",
                Skipped = skipped,
                Scores = scores
            };
            result.Cards.Add(decision);
            if (!skipped && lookup.TryGetValue(decision.SelectedId, out var selected))
            {
                state.Deck.Add(selected.RewardId);
                ApplyProgressionEffect(definition, state, selected);
            }
        }

        result.Relic = ApplyRelic(
            definition,
            encounter,
            state,
            lookup);
        var blessingId = encounter.RewardOffer.BlessingId;
        var acquired = false;
        if (!string.IsNullOrWhiteSpace(blessingId)
            && lookup.TryGetValue(blessingId, out var blessing)
            && (!definition.ExcludeNegativeBlessings || !blessing.Negative))
        {
            if (!state.Blessings.Contains(blessingId, StringComparer.OrdinalIgnoreCase))
            {
                state.Blessings.Add(blessingId);
                ApplyProgressionEffect(definition, state, blessing);
            }
            acquired = true;
        }
        result.Blessing = new CombatCampaignBlessingDecision
        {
            OfferedId = blessingId,
            Acquired = acquired
        };
        return result;
    }

    private static CombatCampaignRelicDecision ApplyRelic(
        CombatCampaignDefinition definition,
        CombatCampaignPlannedEncounter encounter,
        CombatCampaignState state,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> lookup)
    {
        var offeredId = encounter.RewardOffer.RelicId;
        var result = new CombatCampaignRelicDecision { OfferedId = offeredId };
        if (string.IsNullOrWhiteSpace(offeredId)
            || !lookup.TryGetValue(offeredId, out var offered))
        {
            return result;
        }
        var allIds = state.Relics.Concat(new[] { offeredId }).ToList();
        result.Scores = ScoreRewards(
            definition,
            state,
            allIds,
            lookup,
            encounter.Index,
            CombatCampaignRewardKind.Relic);
        if (state.Relics.Contains(offeredId, StringComparer.OrdinalIgnoreCase))
        {
            result.Decision = "keep";
            return result;
        }
        if (state.Relics.Count < definition.RelicLimit)
        {
            state.Relics.Add(offeredId);
            ApplyProgressionEffect(definition, state, offered);
            result.Decision = "acquire";
            return result;
        }
        var offeredScore = result.Scores.First(item =>
            string.Equals(item.RewardId, offeredId, StringComparison.OrdinalIgnoreCase));
        var currentWorst = result.Scores
            .Where(item => state.Relics.Contains(
                item.RewardId,
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(item => item.Total)
            .ThenBy(item => item.RewardId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (currentWorst == null || offeredScore.Total <= currentWorst.Total + 0.05d)
        {
            result.Decision = "keep";
            return result;
        }
        var index = state.Relics.FindIndex(item => string.Equals(
            item,
            currentWorst.RewardId,
            StringComparison.OrdinalIgnoreCase));
        result.Decision = "replace";
        result.ReplacedId = currentWorst.RewardId;
        if (lookup.TryGetValue(currentWorst.RewardId, out var removed))
        {
            RemoveProgressionEffect(definition, state, removed);
        }
        state.Relics[index] = offeredId;
        ApplyProgressionEffect(definition, state, offered);
        return result;
    }

    private static List<CombatCampaignRewardScore> ScoreRewards(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        IEnumerable<string> ids,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> lookup,
        int encounterIndex,
        CombatCampaignRewardKind kind)
    {
        var deckFeatures = AggregateDeckFeatures(state.Deck, lookup);
        var deckCounts = state.Deck
            .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var progress = Math.Max(0d, Math.Min(1d, encounterIndex / 36d));
        return ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(lookup.ContainsKey)
            .Select(id =>
            {
                var item = lookup[id];
                var copies = kind == CombatCampaignRewardKind.Card
                             && deckCounts.TryGetValue(id, out var count)
                    ? count
                    : 0;
                var baseValue = item.BaseValue;
                var tierValue = item.Tier * 0.35d;
                var systemFit = Dot(item.Features, deckFeatures) * (0.25d + progress * 0.75d);
                var tendency = Dot(item.Features, definition.BuildTendency)
                               + Dot(item.Features, definition.RolePrior) * (1d - progress);
                var bossFit = Dot(item.Features, definition.BossPreference)
                              * (0.25d + progress * 0.75d);
                var bloat = kind == CombatCampaignRewardKind.Card
                    ? Math.Max(0, state.Deck.Count - definition.Player.Deck.Count) * 0.04d
                    : 0d;
                var redundancy = copies <= 0 ? 0d : Math.Sqrt(copies) * 0.75d;
                return new CombatCampaignRewardScore
                {
                    RewardId = id,
                    BaseValue = baseValue,
                    TierValue = tierValue,
                    SystemFit = systemFit,
                    BuildTendency = tendency,
                    BossFit = bossFit,
                    BloatPenalty = bloat,
                    RedundancyPenalty = redundancy,
                    Total = baseValue + tierValue + systemFit + tendency + bossFit
                            - bloat - redundancy
                };
            })
            .OrderByDescending(item => item.Total)
            .ThenBy(item => item.RewardId, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, double> AggregateDeckFeatures(
        IReadOnlyList<string> deck,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> lookup)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in deck)
        {
            if (!lookup.TryGetValue(id, out var item)) continue;
            foreach (var pair in item.Features)
            {
                result[pair.Key] = result.TryGetValue(pair.Key, out var value)
                    ? value + pair.Value
                    : pair.Value;
            }
        }
        var divisor = Math.Max(1d, deck.Count);
        foreach (var key in result.Keys.ToList())
        {
            result[key] /= divisor;
        }
        return result;
    }

    private static double Dot(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (left == null || right == null) return 0d;
        return left.Sum(item =>
            item.Value * (right.TryGetValue(item.Key, out var value) ? value : 0d));
    }

    private static void ApplyProgressionEffect(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        CombatCampaignRewardDefinition reward)
    {
        if (reward.Fidelity != CombatRuleFidelity.Authoritative)
        {
            var key = reward.Kind.ToString().ToLowerInvariant() + ":" + reward.RewardId;
            if (!state.UnsupportedProgressionRules.Contains(
                    key,
                    StringComparer.OrdinalIgnoreCase))
            {
                state.UnsupportedProgressionRules.Add(key);
            }
        }
        if (reward.MainAttributeCapBonus != 0)
        {
            state.AttributeUpperBounds[definition.MainAttributeId] +=
                reward.MainAttributeCapBonus;
        }
        if (reward.SecondaryAttributeCapBonus != 0)
        {
            state.AttributeUpperBounds[definition.SecondaryAttributeId] +=
                reward.SecondaryAttributeCapBonus;
        }
        foreach (var attribute in definition.AttributeIds.Where(item =>
                     !string.Equals(item, definition.MainAttributeId, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(
                         item,
                         definition.SecondaryAttributeId,
                         StringComparison.OrdinalIgnoreCase)))
        {
            state.AttributeUpperBounds[attribute] += reward.UnselectedAttributeCapBonus;
        }
        foreach (var pair in reward.PermanentAttributeBonuses)
        {
            if (!state.PermanentAttributeBonuses.ContainsKey(pair.Key)) continue;
            state.PermanentAttributeBonuses[pair.Key] = Math.Max(
                0,
                state.PermanentAttributeBonuses[pair.Key] + pair.Value);
        }
        state.MaxHp = Math.Max(1, state.MaxHp + reward.MaxHpBonus);
        state.CurrentHp = Math.Min(
            state.MaxHp,
            Math.Max(0, state.CurrentHp));
        ClampAttributes(state);
    }

    private static void RemoveProgressionEffect(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        CombatCampaignRewardDefinition reward)
    {
        state.AttributeUpperBounds[definition.MainAttributeId] = Math.Max(
            0,
            state.AttributeUpperBounds[definition.MainAttributeId]
            - reward.MainAttributeCapBonus);
        state.AttributeUpperBounds[definition.SecondaryAttributeId] = Math.Max(
            0,
            state.AttributeUpperBounds[definition.SecondaryAttributeId]
            - reward.SecondaryAttributeCapBonus);
        foreach (var attribute in definition.AttributeIds.Where(item =>
                     !string.Equals(
                         item,
                         definition.MainAttributeId,
                         StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(
                         item,
                         definition.SecondaryAttributeId,
                         StringComparison.OrdinalIgnoreCase)))
        {
            state.AttributeUpperBounds[attribute] = Math.Max(
                0,
                state.AttributeUpperBounds[attribute]
                - reward.UnselectedAttributeCapBonus);
        }
        foreach (var pair in reward.PermanentAttributeBonuses)
        {
            if (!state.PermanentAttributeBonuses.ContainsKey(pair.Key)) continue;
            state.PermanentAttributeBonuses[pair.Key] = Math.Max(
                0,
                state.PermanentAttributeBonuses[pair.Key] - pair.Value);
        }
        if (reward.MaxHpBonus != 0)
        {
            state.MaxHp = Math.Max(1, state.MaxHp - reward.MaxHpBonus);
            state.CurrentHp = Math.Min(state.CurrentHp, state.MaxHp);
        }
        var unsupportedKey = reward.Kind.ToString().ToLowerInvariant()
                             + ":"
                             + reward.RewardId;
        state.UnsupportedProgressionRules.RemoveAll(item => string.Equals(
            item,
            unsupportedKey,
            StringComparison.OrdinalIgnoreCase));
        ClampAttributes(state);
    }

    internal static void ClampAttributes(CombatCampaignState state)
    {
        foreach (var attribute in state.Attributes.Keys.ToList())
        {
            var baseValue = state.LayerBaseAttributes.TryGetValue(attribute, out var baseAmount)
                ? baseAmount
                : 0;
            var bonus = state.PermanentAttributeBonuses.TryGetValue(attribute, out var bonusAmount)
                ? Math.Max(0, bonusAmount)
                : 0;
            var upper = state.AttributeUpperBounds.TryGetValue(attribute, out var upperAmount)
                ? Math.Max(0, upperAmount)
                : int.MaxValue;
            var value = Math.Min(upper, baseValue + bonus);
            state.Attributes[attribute] = value;
            // Overflow is discarded, matching RoleTable.VarsCheck. It does not return
            // later when another effect raises the upper bound.
            state.PermanentAttributeBonuses[attribute] = Math.Max(0, value - baseValue);
        }
    }
}

public sealed class CombatCampaignRunner
{
    private readonly CombatSimulationEngine engine;

    public CombatCampaignRunner(CombatSimulationEngine? engine = null)
    {
        this.engine = engine ?? new CombatSimulationEngine();
    }

    public CombatCampaignResult Run(
        CombatCampaignDefinition definition,
        CombatCampaignWorldPlan plan,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory policyFactory,
        CombatCampaignCheckpoint? resumeFrom = null,
        Action<CombatCampaignCheckpoint>? checkpointSink = null,
        CancellationToken cancellationToken = default)
    {
        CombatCampaignWorldPlanner.Validate(definition);
        if (!string.Equals(definition.CampaignId, plan.CampaignId, StringComparison.Ordinal)
            || !string.Equals(
                definition.CampaignVersion,
                plan.CampaignVersion,
                StringComparison.Ordinal)
            || plan.Encounters.Count != 37)
        {
            throw new ArgumentException("World plan does not match campaign.", nameof(plan));
        }
        var difficulty = CombatCampaignWorldPlanner.ResolveDifficulty(
            definition,
            plan.DifficultyId);
        var checkpoint = InitializeCheckpoint(
            definition,
            plan,
            policyFactory.PolicyId,
            resumeFrom);
        var enemyLevels = definition.Enemies.ToDictionary(
            item => item.EnemyId,
            item => item.NativeLevel,
            StringComparer.OrdinalIgnoreCase);
        var finalIndex = plan.Encounters.FindIndex(item =>
            item.Kind == CombatCampaignEncounterKind.FinalBoss);
        for (var index = checkpoint.NextEncounterIndex; index < plan.Encounters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encounter = plan.Encounters[index];
            if (encounter.StartsLayer)
            {
                ApplyLayerPreset(definition, encounter.LayerNumber, checkpoint.State);
            }
            checkpoint.State.CurrentGameLevel = encounter.GameLevel;
            var scenario = BuildScenario(
                definition,
                encounter,
                checkpoint.State,
                difficulty,
                enemyLevels,
                plan.WorldSeed);
            var battle = engine.Run(
                scenario,
                ruleset,
                policyFactory.Create(),
                cancellationToken);
            checkpoint.Battles.Add(battle);
            checkpoint.NextEncounterIndex = index + 1;
            checkpoint.State.CurrentHp = Math.Max(0, battle.FinalPlayerHp);
            ApplyPersistentBattleDeltas(definition, checkpoint.State, battle);
            if (battle.Outcome == CombatSimulationOutcome.Victory
                && (encounter.Kind != CombatCampaignEncounterKind.FinalBoss
                    || definition.RewardAfterFinalBoss))
            {
                checkpoint.Rewards.Add(CombatCampaignRewardSelector.Apply(
                    definition,
                    encounter,
                    checkpoint.State));
            }
            if (battle.Outcome == CombatSimulationOutcome.Victory
                && encounter.EndsLayer
                && encounter.Kind != CombatCampaignEncounterKind.FinalBoss)
            {
                var layer = definition.Layers.First(item =>
                    item.LayerNumber == encounter.LayerNumber);
                checkpoint.State.MaxHp += layer.MaxHpGainAfterClear;
                checkpoint.State.CurrentHp = Math.Min(
                    checkpoint.State.MaxHp,
                    checkpoint.State.CurrentHp + layer.MaxHpGainAfterClear);
            }
            checkpoint.Completed = index == plan.Encounters.Count - 1
                                   || battle.Outcome != CombatSimulationOutcome.Victory;
            checkpointSink?.Invoke(CloneCheckpoint(checkpoint));
            if (battle.Outcome != CombatSimulationOutcome.Victory)
            {
                break;
            }
        }
        var finalBattle = finalIndex >= 0 && checkpoint.Battles.Count > finalIndex
            ? checkpoint.Battles[finalIndex]
            : null;
        var battleCoverage = checkpoint.Battles.Count == 0
            ? 0d
            : checkpoint.Battles.Average(item => item.SemanticCoverage);
        var progressionSelections = checkpoint.Rewards.Sum(item =>
            item.Cards.Count(card => !card.Skipped)
            + (string.IsNullOrWhiteSpace(item.Relic.OfferedId) ? 0 : 1)
            + (item.Blessing.Acquired ? 1 : 0));
        var unsupportedProgression = checkpoint.State.UnsupportedProgressionRules.Count;
        var progressionCoverage = progressionSelections <= 0
            ? 1d
            : Math.Max(
                0d,
                1d - (double)unsupportedProgression / progressionSelections);
        var unsupported = checkpoint.Battles
            .SelectMany(item => item.UnsupportedDefinitions)
            .Concat(checkpoint.State.UnsupportedProgressionRules)
            .Concat(difficulty.HardAffixes
                .Where(item => item.CombatRelevant && !item.Implemented)
                .Select(item => "hard:" + item.AffixId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var invalid = checkpoint.Battles.Any(item =>
                          item.Outcome == CombatSimulationOutcome.Invalid)
                      || (definition.RequireAuthoritativeRules
                          && (battleCoverage < 0.999999d
                              || progressionCoverage < 0.999999d
                              || unsupported.Count > 0));
        return new CombatCampaignResult
        {
            CampaignId = definition.CampaignId,
            CampaignVersion = definition.CampaignVersion,
            DifficultyId = difficulty.DifficultyId,
            WorldSeed = plan.WorldSeed,
            PlanHash = plan.PlanHash,
            PolicyId = policyFactory.PolicyId,
            ReachedFinalBoss = finalBattle != null,
            FinalBossVictory = finalBattle?.Outcome == CombatSimulationOutcome.Victory,
            CampaignVictory = finalBattle?.Outcome == CombatSimulationOutcome.Victory && !invalid,
            Invalid = invalid,
            CompletedBattles = checkpoint.Battles.Count,
            BattleSemanticCoverage = battleCoverage,
            ProgressionSemanticCoverage = progressionCoverage,
            FinalState = CloneState(checkpoint.State),
            Battles = new List<CombatSimulationResult>(checkpoint.Battles),
            Rewards = new List<CombatCampaignRewardDecision>(checkpoint.Rewards),
            UnsupportedDefinitions = unsupported,
            Checkpoint = CloneCheckpoint(checkpoint)
        };
    }

    public CombatCampaignPairResult RunPaired(
        CombatCampaignDefinition definition,
        string difficultyId,
        ulong worldSeed,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory baseline,
        ICombatSimulationPolicyFactory learned,
        CancellationToken cancellationToken = default)
    {
        var plan = CombatCampaignWorldPlanner.Build(definition, difficultyId, worldSeed);
        return new CombatCampaignPairResult
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

    private static void ApplyPersistentBattleDeltas(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        CombatSimulationResult battle)
    {
        foreach (var pair in battle.PersistentVariableDeltas)
        {
            var attribute = definition.AttributeIds.FirstOrDefault(item =>
                string.Equals(item, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (attribute != null)
            {
                state.PermanentAttributeBonuses[attribute] = Math.Max(
                    0,
                    state.PermanentAttributeBonuses[attribute] + pair.Value);
                continue;
            }
            var cappedAttribute = definition.AttributeIds.FirstOrDefault(item =>
                string.Equals(
                    item + "UpperBound",
                    pair.Key,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    item + "Cap",
                    pair.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (cappedAttribute != null)
            {
                state.AttributeUpperBounds[cappedAttribute] = Math.Max(
                    0,
                    state.AttributeUpperBounds[cappedAttribute] + pair.Value);
                continue;
            }
            if (string.Equals(pair.Key, "MaxHp", StringComparison.OrdinalIgnoreCase))
            {
                state.MaxHp = Math.Max(1, state.MaxHp + pair.Value);
                state.CurrentHp = Math.Min(
                    state.MaxHp,
                    Math.Max(0, state.CurrentHp));
            }
        }
        CombatCampaignRewardSelector.ClampAttributes(state);
    }

    private static CombatScenarioDefinition BuildScenario(
        CombatCampaignDefinition definition,
        CombatCampaignPlannedEncounter encounter,
        CombatCampaignState state,
        CombatCampaignDifficultyDefinition difficulty,
        IReadOnlyDictionary<string, int> enemyLevels,
        ulong worldSeed)
    {
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = definition.CampaignId + ":" + encounter.Index + ":" + encounter.EncounterId,
            RulesetVersion = definition.RulesetVersion,
            Seed = NamedBattleSeed(worldSeed, encounter.Index),
            Player = new CombatPlayerSetup
            {
                RoleId = definition.Player.RoleId,
                MaxHp = state.MaxHp,
                CurrentHp = state.CurrentHp,
                BaseEnergy = definition.Player.BaseEnergy,
                Deck = new List<string>(state.Deck),
                InitialStatuses = definition.Player.InitialStatuses
                    .Concat(SelectedRewardStatuses(definition, state))
                    .Select(CloneStatus)
                    .ToList(),
                Variables = state.Attributes.ToDictionary(
                    item => item.Key,
                    item => (double)item.Value,
                    StringComparer.OrdinalIgnoreCase)
            },
            InitialDraw = definition.InitialDraw,
            DrawPerTurn = definition.DrawPerTurn,
            HandLimit = definition.HandLimit,
            RetainBlockBetweenTurns = definition.RetainBlockBetweenTurns,
            RequireAuthoritativeRules = definition.RequireAuthoritativeRules,
            TraceLevel = definition.TraceLevel,
            Limits = definition.Limits.Normalize()
        };
        for (var index = 0; index < encounter.EnemyIds.Count; index++)
        {
            var enemyId = encounter.EnemyIds[index];
            var nativeLevel = enemyLevels.TryGetValue(enemyId, out var level) ? level : 1;
            var growth = ((encounter.GameLevel - 1) / 12) - nativeLevel + 1;
            var hpScale = Math.Max(0.1d, 1d + growth * 0.3d)
                          * Math.Max(0.1d, difficulty.EnemyHpMultiplier);
            var attackScale = Math.Max(0d, 1d + growth * 0.2d)
                              * Math.Max(0d, difficulty.EnemyAttackMultiplier);
            scenario.Enemies.Add(new CombatEnemySetup
            {
                EnemyId = enemyId,
                InstanceKey = encounter.EncounterId + ":" + (index + 1),
                HpScale = hpScale,
                AttackScale = attackScale,
                InitialBlockBonus = difficulty.ApplyGameLevelShield
                    ? GameLevelShield(encounter.GameLevel)
                    : 0,
                InitialStatuses = difficulty.EnemyInitialStatuses
                    .Select(CloneStatus)
                    .ToList()
            });
        }
        return scenario;
    }

    private static IEnumerable<CombatInitialStatus> SelectedRewardStatuses(
        CombatCampaignDefinition definition,
        CombatCampaignState state)
    {
        var lookup = definition.Rewards.ToDictionary(
            item => item.RewardId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in state.Relics.Concat(state.Blessings))
        {
            if (!lookup.TryGetValue(id, out var reward)) continue;
            foreach (var status in reward.InitialStatuses)
            {
                yield return status;
            }
        }
    }

    private static int GameLevelShield(int gameLevel)
    {
        if (gameLevel < 6) return 25;
        if (gameLevel < 12) return 75;
        if (gameLevel < 18) return 225;
        if (gameLevel < 24) return 675;
        if (gameLevel < 30) return 2025;
        return 6075;
    }

    private static void ApplyLayerPreset(
        CombatCampaignDefinition definition,
        int layerNumber,
        CombatCampaignState state)
    {
        var layer = definition.Layers.First(item => item.LayerNumber == layerNumber);
        state.CurrentLayer = layerNumber;
        foreach (var attribute in definition.AttributeIds)
        {
            var baseValue = string.Equals(
                attribute,
                definition.MainAttributeId,
                StringComparison.OrdinalIgnoreCase)
                ? layer.Attributes.Main
                : string.Equals(
                    attribute,
                    definition.SecondaryAttributeId,
                    StringComparison.OrdinalIgnoreCase)
                    ? layer.Attributes.Secondary
                    : layer.Attributes.Unselected;
            state.LayerBaseAttributes[attribute] = baseValue;
            state.Attributes[attribute] = baseValue;
        }
        CombatCampaignRewardSelector.ClampAttributes(state);
    }

    private static CombatCampaignCheckpoint InitializeCheckpoint(
        CombatCampaignDefinition definition,
        CombatCampaignWorldPlan plan,
        string policyId,
        CombatCampaignCheckpoint? resumeFrom)
    {
        if (resumeFrom != null)
        {
            if (!string.Equals(resumeFrom.CampaignId, definition.CampaignId, StringComparison.Ordinal)
                || !string.Equals(
                    resumeFrom.CampaignVersion,
                    definition.CampaignVersion,
                    StringComparison.Ordinal)
                || !string.Equals(resumeFrom.DifficultyId, plan.DifficultyId, StringComparison.Ordinal)
                || resumeFrom.WorldSeed != plan.WorldSeed
                || !string.Equals(resumeFrom.PlanHash, plan.PlanHash, StringComparison.Ordinal)
                || !string.Equals(resumeFrom.PolicyId, policyId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Checkpoint identity does not match campaign run.",
                    nameof(resumeFrom));
            }
            return CloneCheckpoint(resumeFrom);
        }
        var state = new CombatCampaignState
        {
            MaxHp = definition.Player.MaxHp,
            CurrentHp = definition.Player.CurrentHp,
            Deck = new List<string>(definition.Player.Deck)
        };
        foreach (var attribute in definition.AttributeIds)
        {
            state.Attributes[attribute] = 0;
            state.LayerBaseAttributes[attribute] = 0;
            state.PermanentAttributeBonuses[attribute] = 0;
            state.AttributeUpperBounds[attribute] = string.Equals(
                attribute,
                definition.MainAttributeId,
                StringComparison.OrdinalIgnoreCase)
                ? definition.MainAttributeUpperBound
                : string.Equals(
                    attribute,
                    definition.SecondaryAttributeId,
                    StringComparison.OrdinalIgnoreCase)
                    ? definition.SecondaryAttributeUpperBound
                    : definition.UnselectedAttributeUpperBound;
        }
        return new CombatCampaignCheckpoint
        {
            CampaignId = definition.CampaignId,
            CampaignVersion = definition.CampaignVersion,
            DifficultyId = plan.DifficultyId,
            WorldSeed = plan.WorldSeed,
            PlanHash = plan.PlanHash,
            PolicyId = policyId,
            State = state
        };
    }

    private static CombatCampaignCheckpoint CloneCheckpoint(CombatCampaignCheckpoint source)
    {
        return new CombatCampaignCheckpoint
        {
            CampaignId = source.CampaignId,
            CampaignVersion = source.CampaignVersion,
            DifficultyId = source.DifficultyId,
            WorldSeed = source.WorldSeed,
            PlanHash = source.PlanHash,
            PolicyId = source.PolicyId,
            NextEncounterIndex = source.NextEncounterIndex,
            State = CloneState(source.State),
            Battles = new List<CombatSimulationResult>(source.Battles),
            Rewards = new List<CombatCampaignRewardDecision>(source.Rewards),
            Completed = source.Completed
        };
    }

    private static CombatCampaignState CloneState(CombatCampaignState source)
    {
        return new CombatCampaignState
        {
            CurrentLayer = source.CurrentLayer,
            CurrentGameLevel = source.CurrentGameLevel,
            MaxHp = source.MaxHp,
            CurrentHp = source.CurrentHp,
            Attributes = new Dictionary<string, int>(
                source.Attributes,
                StringComparer.OrdinalIgnoreCase),
            LayerBaseAttributes = new Dictionary<string, int>(
                source.LayerBaseAttributes,
                StringComparer.OrdinalIgnoreCase),
            PermanentAttributeBonuses = new Dictionary<string, int>(
                source.PermanentAttributeBonuses,
                StringComparer.OrdinalIgnoreCase),
            AttributeUpperBounds = new Dictionary<string, int>(
                source.AttributeUpperBounds,
                StringComparer.OrdinalIgnoreCase),
            Deck = new List<string>(source.Deck),
            Relics = new List<string>(source.Relics),
            Blessings = new List<string>(source.Blessings),
            UnsupportedProgressionRules = new List<string>(
                source.UnsupportedProgressionRules)
        };
    }

    private static CombatInitialStatus CloneStatus(CombatInitialStatus source)
    {
        return new CombatInitialStatus
        {
            StatusId = source.StatusId,
            Stacks = source.Stacks,
            Duration = source.Duration
        };
    }

    private static ulong NamedBattleSeed(ulong worldSeed, int index)
    {
        var value = worldSeed + ((ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
