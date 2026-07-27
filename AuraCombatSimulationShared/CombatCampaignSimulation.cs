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

public enum CombatCampaignCardAcquisition
{
    RewardPool,
    GeneratedOnly,
    StartingOnly,
    CurseOnly,
    SkillOnly
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

    public CombatCampaignCardAcquisition CardAcquisition { get; set; } =
        CombatCampaignCardAcquisition.RewardPool;

    public int Tier { get; set; } = 1;

    public double OfferWeight { get; set; } = 1d;

    public double BaseValue { get; set; }

    public bool Negative { get; set; }

    public CombatRuleFidelity Fidelity { get; set; } = CombatRuleFidelity.Approximate;

    public string NativeScriptHash { get; set; } = "";

    public string OwnScript { get; set; } = "";

    public string FightScript { get; set; } = "";

    public Dictionary<string, string> InitialVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> PermanentAttributeBonuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int MainAttributeCapBonus { get; set; }

    public int SecondaryAttributeCapBonus { get; set; }

    public int UnselectedAttributeCapBonus { get; set; }

    public int MaxHpBonus { get; set; }

    public int CurrentHpBonus { get; set; }

    public int RandomCardRemovalCount { get; set; }

    public string OneTimeSpecialVariableKey { get; set; } = "";

    public int ReplacementRelicTier { get; set; }

    public List<string> GrantedCardIds { get; set; } = new();

    public List<string> GrantedBlessingIds { get; set; } = new();

    public List<string> GrantedRelicIds { get; set; } = new();

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

    public bool MovePlayedCardAfterResolution { get; set; }

    public List<string> InitialDiscardCards { get; set; } = new();

    public int DirectHpLossAfterPlayerCard { get; set; }

    public int AdditionalEnemyHpMultiplierMinimumGameLevel { get; set; } =
        int.MaxValue;

    public double AdditionalEnemyHpMultiplier { get; set; } = 1d;

    public Dictionary<string, double> PlayerVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> EnemyVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

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

    public int InitialMoney { get; set; } = 100;

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

    public int CardOfferRounds { get; set; } = 1;

    public int CardChoicesPerRound { get; set; } = 3;

    public List<CombatCampaignEncounterKind> CardRewardEncounterKinds { get; set; } =
        new() { CombatCampaignEncounterKind.Normal };

    public int TargetDeckSizeMinimum { get; set; } = 28;

    public int TargetDeckSizeMaximum { get; set; } = 40;

    public int DeckSizeAlertThreshold { get; set; } = 45;

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

    public bool RetainBlockBetweenTurns { get; set; } = true;

    public bool RequireAuthoritativeRules { get; set; } = true;

    public CombatSimulationTraceLevel TraceLevel { get; set; } =
        CombatSimulationTraceLevel.Summary;

    public bool FullTraceFinalEncounterOnly { get; set; }

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

    public double ArchetypeFit { get; set; }

    public double SurvivalFit { get; set; }

    public double EnergyFit { get; set; }

    public double DilutionPenalty { get; set; }

    public double RiskPenalty { get; set; }
}

public sealed class CombatCampaignBuildPlan
{
    public int LayerNumber { get; set; }

    public string PrimaryArchetype { get; set; } = "";

    public string SecondaryArchetype { get; set; } = "";

    public int TargetDeckSizeMinimum { get; set; } = 28;

    public int TargetDeckSizeMaximum { get; set; } = 40;

    public bool DeckSizeAlert { get; set; }

    public int Revision { get; set; }

    public Dictionary<string, double> FeatureWeights { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> SynergySources { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatCampaignBuildPlan Clone()
    {
        return new CombatCampaignBuildPlan
        {
            LayerNumber = LayerNumber,
            PrimaryArchetype = PrimaryArchetype,
            SecondaryArchetype = SecondaryArchetype,
            TargetDeckSizeMinimum = TargetDeckSizeMinimum,
            TargetDeckSizeMaximum = TargetDeckSizeMaximum,
            DeckSizeAlert = DeckSizeAlert,
            Revision = Revision,
            FeatureWeights = new Dictionary<string, double>(
                FeatureWeights,
                StringComparer.OrdinalIgnoreCase),
            SynergySources = new Dictionary<string, int>(
                SynergySources,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class CombatCampaignCardDecision
{
    public int Round { get; set; }

    public List<string> OfferedIds { get; set; } = new();

    public string SelectedId { get; set; } = "";

    public bool Skipped { get; set; }

    public double SkipScore { get; set; }

    public string SkipReason { get; set; } = "";

    public List<CombatCampaignRewardScore> Scores { get; set; } = new();
}

public sealed class CombatCampaignRelicDecision
{
    public string OfferedId { get; set; } = "";

    public string Decision { get; set; } = "none";

    public string ReplacedId { get; set; } = "";

    public string ResolvedId { get; set; } = "";

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

    public CombatCampaignBuildPlan BuildPlan { get; set; } = new();
}

public sealed class CombatCampaignState
{
    public ulong WorldSeed { get; set; }

    public int CurrentLayer { get; set; }

    public int CurrentGameLevel { get; set; }

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Money { get; set; }

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

    public Dictionary<string, Dictionary<string, string>> RewardVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SpecialVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> UnsupportedProgressionRules { get; set; } = new();

    public CombatCampaignBuildPlan BuildPlan { get; set; } = new();
}

public static class CombatCampaignCardAcquisitionPolicy
{
    private static readonly HashSet<string> KnownGeneratedOnlyCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SpellCard_1",
            "SpellCard_2",
            "SpellCard_3",
            "SpellCard_4"
        };

    public static bool IsGeneratedOnlyIdentifier(string? cardId)
    {
        var value = (cardId ?? "").Trim();
        return value.StartsWith("*", StringComparison.Ordinal)
               || value.IndexOf("_*", StringComparison.Ordinal) >= 0
               || KnownGeneratedOnlyCards.Contains(value);
    }

    public static bool CanEnterRewardPool(
        CombatCampaignRewardDefinition reward)
    {
        return reward.Kind == CombatCampaignRewardKind.Card
               && reward.CardAcquisition
                  == CombatCampaignCardAcquisition.RewardPool
               && !IsGeneratedOnlyIdentifier(reward.RewardId);
    }

    public static bool CanEnterStartingDeck(
        CombatCampaignRewardDefinition reward)
    {
        return reward.Kind == CombatCampaignRewardKind.Card
               && reward.CardAcquisition is
                   CombatCampaignCardAcquisition.RewardPool
                   or CombatCampaignCardAcquisition.StartingOnly
               && !IsGeneratedOnlyIdentifier(reward.RewardId);
    }
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

public static class CombatCampaignRewardRuleProjector
{
    public static List<CombatScenarioRewardRule> Build(
        CombatCampaignDefinition definition,
        CombatCampaignState state)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (state == null) throw new ArgumentNullException(nameof(state));
        return Build(
            state,
            definition.Rewards.ToDictionary(
                item => item.RewardId,
                StringComparer.OrdinalIgnoreCase));
    }

    internal static List<CombatScenarioRewardRule> Build(
        CombatCampaignState state,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> rewardLookup)
    {
        var result = new List<CombatScenarioRewardRule>();
        foreach (var rewardId in state.Relics
                     .Concat(state.Blessings)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!rewardLookup.TryGetValue(rewardId, out var reward)
                || string.IsNullOrWhiteSpace(reward.FightScript))
            {
                continue;
            }
            result.Add(new CombatScenarioRewardRule
            {
                RewardId = reward.RewardId,
                Kind = reward.Kind.ToString(),
                Stacks = reward.Kind == CombatCampaignRewardKind.Blessing
                    ? Math.Max(
                        1,
                        state.Blessings.Count(item => string.Equals(
                            item,
                            rewardId,
                            StringComparison.OrdinalIgnoreCase)))
                    : 1,
                NativeScriptHash = reward.NativeScriptHash,
                FightScript = reward.FightScript,
                Variables = state.RewardVariables.TryGetValue(
                    rewardId,
                    out var variables)
                    ? new Dictionary<string, string>(
                        variables,
                        StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(
                        reward.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
            });
        }
        return result;
    }
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

    public int TotalBattles { get; set; }

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
        if (definition.CardOfferRounds < 0
            || definition.CardChoicesPerRound < 1
            || definition.TargetDeckSizeMinimum < definition.Player.Deck.Count
            || definition.TargetDeckSizeMaximum < definition.TargetDeckSizeMinimum
            || definition.DeckSizeAlertThreshold <= definition.TargetDeckSizeMaximum)
        {
            throw new ArgumentException(
                "Campaign card cadence and deck-size targets are invalid.",
                nameof(definition));
        }
        var rewardLookup = definition.Rewards
            .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var invalidStarter = definition.Player.Deck.FirstOrDefault(cardId =>
            CombatCampaignCardAcquisitionPolicy.IsGeneratedOnlyIdentifier(cardId)
            || (rewardLookup.TryGetValue(cardId, out var reward)
                && !CombatCampaignCardAcquisitionPolicy.CanEnterStartingDeck(
                    reward)));
        if (!string.IsNullOrWhiteSpace(invalidStarter))
        {
            throw new ArgumentException(
                "Generated/curse/skill-only card cannot enter starting deck: "
                + invalidStarter,
                nameof(definition));
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
            .Where(CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool)
            .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.RewardId, StringComparer.Ordinal)
            .ToList();
        var offersCards = (definition.CardRewardEncounterKinds
                           ?? new List<CombatCampaignEncounterKind>())
            .Contains(kind);
        for (var round = 0;
             offersCards && round < definition.CardOfferRounds;
             round++)
        {
            result.CardRounds.Add(PickWeightedDistinct(
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

    private static List<string> PickWeightedDistinct(
        IReadOnlyList<CombatCampaignRewardDefinition> source,
        int count,
        ulong seed,
        string stream,
        int step)
    {
        var remaining = source
            .Where(item => !string.IsNullOrWhiteSpace(item.RewardId)
                           && item.OfferWeight > 0d
                           && !double.IsNaN(item.OfferWeight)
                           && !double.IsInfinity(item.OfferWeight))
            .ToList();
        var selected = new List<string>();
        for (var index = 0; index < count && remaining.Count > 0; index++)
        {
            var total = remaining.Sum(item => item.OfferWeight);
            if (total <= 0d)
            {
                break;
            }
            var roll = NextUnit(seed, stream + ":" + index, step) * total;
            var cursor = 0d;
            var selectedIndex = remaining.Count - 1;
            for (var candidateIndex = 0; candidateIndex < remaining.Count; candidateIndex++)
            {
                cursor += remaining[candidateIndex].OfferWeight;
                if (roll < cursor)
                {
                    selectedIndex = candidateIndex;
                    break;
                }
            }
            selected.Add(remaining[selectedIndex].RewardId);
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

    private static double NextUnit(ulong seed, string stream, int step)
    {
        var value = seed ^ StableHash(stream) ^ ((ulong)(step + 1) * 0x9E3779B97F4A7C15UL);
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1d / 9007199254740992d);
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
            EncounterId = encounter.EncounterId,
            BuildPlan = RefreshBuildPlan(definition, state).Clone()
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
            var buildPlan = state.BuildPlan;
            var hardCap = state.Deck.Count >= 40;
            var targetCap = state.Deck.Count
                            >= buildPlan.TargetDeckSizeMaximum;
            var survivalOverride = best != null
                                   && state.MaxHp > 0
                                   && state.CurrentHp
                                      / (double)state.MaxHp <= 0.35d
                                   && best.SurvivalFit >= 0.75d
                                   && !hardCap;
            var skipScore = CardSkipScore(state, buildPlan);
            var belowMarginal = best == null
                                || best.Total <= skipScore + 0.10d;
            var skipped = hardCap
                          || (definition.AllowSkipCardReward
                              && !survivalOverride
                              && (targetCap || belowMarginal));
            var skipReason = !skipped
                ? ""
                : hardCap
                    ? "deck-cap"
                    : targetCap
                        ? "layer-target"
                        : best == null
                            ? "no-valid-card"
                            : "below-marginal";
            var decision = new CombatCampaignCardDecision
            {
                Round = round + 1,
                OfferedIds = new List<string>(offer),
                SelectedId = skipped ? "" : best?.RewardId ?? "",
                Skipped = skipped,
                SkipScore = skipScore,
                SkipReason = skipReason,
                Scores = scores
            };
            result.Cards.Add(decision);
            if (!skipped && lookup.TryGetValue(decision.SelectedId, out var selected))
            {
                state.Deck.Add(selected.RewardId);
                ApplyProgressionEffect(definition, state, selected);
                result.BuildPlan = RefreshBuildPlan(definition, state).Clone();
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
        result.BuildPlan = RefreshBuildPlan(definition, state).Clone();
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
            ApplyAcquiredRelic(
                definition,
                encounter,
                state,
                lookup,
                offered,
                result);
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
        ApplyAcquiredRelic(
            definition,
            encounter,
            state,
            lookup,
            offered,
            result);
        return result;
    }

    private static void ApplyAcquiredRelic(
        CombatCampaignDefinition definition,
        CombatCampaignPlannedEncounter encounter,
        CombatCampaignState state,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> lookup,
        CombatCampaignRewardDefinition offered,
        CombatCampaignRelicDecision decision)
    {
        if (offered.ReplacementRelicTier <= 0)
        {
            ApplyProgressionEffect(definition, state, offered);
            decision.ResolvedId = offered.RewardId;
            return;
        }
        var candidates = definition.Rewards
            .Where(item =>
                item.Kind == CombatCampaignRewardKind.Relic
                && item.Tier == offered.ReplacementRelicTier
                && !string.Equals(
                    item.RewardId,
                    offered.RewardId,
                    StringComparison.OrdinalIgnoreCase)
                && !state.Relics.Contains(
                    item.RewardId,
                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(item => item.RewardId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            ApplyProgressionEffect(definition, state, offered);
            decision.ResolvedId = offered.RewardId;
            return;
        }
        var selected = candidates[StableProgressionIndex(
            state.WorldSeed,
            offered.RewardId,
            encounter.Index,
            candidates.Count)];
        var index = state.Relics.FindIndex(item => string.Equals(
            item,
            offered.RewardId,
            StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            state.Relics[index] = selected.RewardId;
        }
        ApplyProgressionEffect(definition, state, selected);
        decision.ResolvedId = selected.RewardId;
    }

    private static int StableProgressionIndex(
        ulong worldSeed,
        string stream,
        int counter,
        int count)
    {
        if (count <= 1) return 0;
        var value = worldSeed
                    ^ ((ulong)(counter + 1) * 0x9E3779B97F4A7C15UL);
        foreach (var item in Encoding.UTF8.GetBytes(stream ?? ""))
        {
            value ^= item;
            value *= 1099511628211UL;
        }
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return (int)((value ^ (value >> 31)) % (ulong)count);
    }

    internal static List<CombatCampaignRewardScore> ScoreRewards(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        IEnumerable<string> ids,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> lookup,
        int encounterIndex,
        CombatCampaignRewardKind kind)
    {
        var buildPlan = RefreshBuildPlan(definition, state);
        var deckFeatures = AggregateBuildFeatures(state, lookup);
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
                var archetypeFit = kind == CombatCampaignRewardKind.Card
                    ? Feature(item, buildPlan.PrimaryArchetype) * 1.15d
                      + Feature(item, buildPlan.SecondaryArchetype) * 0.65d
                    : 0d;
                var offPlanPenalty =
                    kind == CombatCampaignRewardKind.Card
                    && buildPlan.LayerNumber >= 2
                    && archetypeFit <= 0.000001d
                        ? 0.85d
                        : 0d;
                var missingHpRatio = state.MaxHp <= 0
                    ? 0d
                    : Math.Max(
                        0d,
                        1d - state.CurrentHp / (double)state.MaxHp);
                var rebirthPlan =
                    string.Equals(
                        buildPlan.PrimaryArchetype,
                        "rebirth",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        buildPlan.SecondaryArchetype,
                        "rebirth",
                        StringComparison.OrdinalIgnoreCase);
                var riskPenalty =
                    Feature(item, "risk")
                    * (0.9d + missingHpRatio * 1.6d)
                    * (rebirthPlan && Feature(item, "rebirth") > 0d
                        ? 0.4d
                        : 1d)
                    + Feature(item, "gold-cost") * 1.25d
                    + Feature(item, "hard-ban") * 100d;
                var survivalFit = kind == CombatCampaignRewardKind.Card
                    ? (Feature(item, "defense") + Feature(item, "heal"))
                      * (0.35d + missingHpRatio)
                    : 0d;
                var energyFit = kind == CombatCampaignRewardKind.Card
                    ? Feature(item, "energy") * 0.55d
                      + Feature(item, "cycling") * 0.35d
                    : 0d;
                var dilution = kind == CombatCampaignRewardKind.Card
                    ? DeckDilutionPenalty(
                        definition,
                        state,
                        item,
                        buildPlan)
                    : 0d;
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
                    ArchetypeFit = archetypeFit,
                    SurvivalFit = survivalFit,
                    EnergyFit = energyFit,
                    DilutionPenalty = dilution,
                    RiskPenalty = riskPenalty,
                    Total = baseValue + tierValue + systemFit + tendency + bossFit
                            + archetypeFit + survivalFit + energyFit
                            - bloat - redundancy - dilution - offPlanPenalty
                            - riskPenalty
                };
            })
            .OrderByDescending(item => item.Total)
            .ThenBy(item => item.RewardId, StringComparer.Ordinal)
            .ToList();
    }

    internal static CombatCampaignBuildPlan RefreshBuildPlan(
        CombatCampaignDefinition definition,
        CombatCampaignState state)
    {
        var lookup = definition.Rewards
            .GroupBy(item => item.RewardId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var deckFeatures = AggregateBuildFeatures(state, lookup);
        var archetypes = new[]
        {
            "burst",
            "sustained",
            "defense",
            "heal",
            "aoe",
            "cycling",
            "energy",
            "rebirth",
            "time-cage"
        };
        var weights = archetypes.ToDictionary(
            key => key,
            key => DictionaryValue(deckFeatures, key)
                   + DictionaryValue(definition.BuildTendency, key) * 0.55d
                   + DictionaryValue(definition.RolePrior, key) * 0.25d,
            StringComparer.OrdinalIgnoreCase);
        var ranked = weights
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
        var previous = state.BuildPlan ?? new CombatCampaignBuildPlan();
        var layerNumber = Math.Max(1, state.CurrentLayer);
        var layerChanged = previous.LayerNumber != layerNumber;
        var primary = ranked.FirstOrDefault().Key ?? "";
        var secondary = layerNumber >= 4
            ? ranked.Skip(1).FirstOrDefault().Key ?? ""
            : "";
        if (!string.IsNullOrWhiteSpace(previous.PrimaryArchetype)
            && (!layerChanged
                || DictionaryValue(weights, primary)
                   < DictionaryValue(weights, previous.PrimaryArchetype)
                   + 0.12d))
        {
            primary = previous.PrimaryArchetype;
            secondary = layerNumber >= 4
                ? ranked
                      .Select(item => item.Key)
                      .FirstOrDefault(item => !string.Equals(
                          item,
                          primary,
                          StringComparison.OrdinalIgnoreCase))
                  ?? ""
                : "";
        }
        var (targetMinimum, targetMaximum) =
            TargetDeckBand(layerNumber);
        var revised = layerChanged
                      && !string.IsNullOrWhiteSpace(
                          previous.PrimaryArchetype)
                      && !string.Equals(
                          previous.PrimaryArchetype,
                          primary,
                          StringComparison.OrdinalIgnoreCase);
        state.BuildPlan = new CombatCampaignBuildPlan
        {
            LayerNumber = layerNumber,
            PrimaryArchetype = primary,
            SecondaryArchetype = secondary,
            TargetDeckSizeMinimum = targetMinimum,
            TargetDeckSizeMaximum = targetMaximum,
            DeckSizeAlert = state.Deck.Count >= definition.DeckSizeAlertThreshold,
            Revision = previous.Revision + (revised ? 1 : 0),
            FeatureWeights = weights,
            SynergySources = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["card"] = state.Deck.Count,
                ["relic"] = state.Relics.Count,
                ["blessing"] = state.Blessings.Count
            }
        };
        return state.BuildPlan;
    }

    private static double Feature(
        CombatCampaignRewardDefinition item,
        string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0d;
        }
        return item.Features.TryGetValue(key, out var value)
            ? value
            : InferredFeature(item, key);
    }

    private static double InferredFeature(
        CombatCampaignRewardDefinition item,
        string key)
    {
        var rewardId = item.RewardId;
        if (string.Equals(key, "hard-ban", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                rewardId,
                "luckycard_4",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }
        if (string.Equals(key, "gold-cost", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    rewardId,
                    "luckycard_4",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 1d;
            }
            var script = (item.OwnScript ?? "") + "\n" + (item.FightScript ?? "");
            var changesGold =
                script.IndexOf("Money", StringComparison.OrdinalIgnoreCase) >= 0
                || script.IndexOf(
                    "ChangeMoney",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            var hasCost =
                script.IndexOf("ChangeHp", StringComparison.OrdinalIgnoreCase) >= 0
                || script.IndexOf("SetHp", StringComparison.OrdinalIgnoreCase) >= 0
                || script.IndexOf("DiceCheck", StringComparison.OrdinalIgnoreCase) >= 0
                || script.IndexOf("* -", StringComparison.OrdinalIgnoreCase) >= 0
                || script.IndexOf("-=", StringComparison.OrdinalIgnoreCase) >= 0;
            return changesGold && hasCost ? 1d : 0d;
        }
        if (string.Equals(key, "rebirth", StringComparison.OrdinalIgnoreCase))
        {
            if (IdIn(rewardId, "Crowdfundingcard_6", "Crowdfundingcard_47"))
            {
                return 1d;
            }
            if (IdIn(
                    rewardId,
                    "Crowdfundingcard_8",
                    "Crowdfundingcard_10",
                    "Crowdfundingcard_11"))
            {
                return 0.9d;
            }
            if (IdIn(
                    rewardId,
                    "Crowdfundingcard_7",
                    "Crowdfundingcard_9",
                    "Crowdfundingcard_25",
                    "Crowdfundingcard_49",
                    "SpellCard_17",
                    "universalcard_10",
                    "universalcard_15"))
            {
                return 0.4d;
            }
        }
        if (string.Equals(key, "time-cage", StringComparison.OrdinalIgnoreCase))
        {
            if (IdIn(
                    rewardId,
                    "timekeeper_3",
                    "timekeeper_4",
                    "timekeeper_6",
                    "timekeeper_7",
                    "timekeeper_8",
                    "timekeeper_9",
                    "timekeeper_10",
                    "timekeeper_12",
                    "timekeeper_13",
                    "timekeeper_14",
                    "timekeeper_17",
                    "timekeeper_18"))
            {
                return 0.9d;
            }
            if (IdIn(
                    rewardId,
                    "timekeeper_2",
                    "timekeeper_5",
                    "timekeeper_15",
                    "timekeeper_16"))
            {
                return 0.45d;
            }
        }
        return 0d;
    }

    private static bool IdIn(string value, params string[] candidates)
    {
        return candidates.Any(candidate => string.Equals(
            value,
            candidate,
            StringComparison.OrdinalIgnoreCase));
    }

    private static double DictionaryValue(
        IReadOnlyDictionary<string, double> values,
        string key)
    {
        return values != null && values.TryGetValue(key, out var value)
            ? value
            : 0d;
    }

    private static double DeckDilutionPenalty(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        CombatCampaignRewardDefinition item,
        CombatCampaignBuildPlan plan)
    {
        var count = state.Deck.Count;
        var sizePenalty = count < plan.TargetDeckSizeMinimum
            ? 0d
            : count < plan.TargetDeckSizeMaximum
                ? 0.15d
                  + (count - plan.TargetDeckSizeMinimum + 1) * 0.22d
                : 1.75d
                  + (count - plan.TargetDeckSizeMaximum) * 0.45d;
        var planFit = Math.Max(
            0d,
            Feature(item, plan.PrimaryArchetype)
            + Feature(item, plan.SecondaryArchetype) * 0.5d);
        return Math.Max(0d, sizePenalty - Math.Min(0.65d, planFit * 0.25d));
    }

    private static double CardSkipScore(
        CombatCampaignState state,
        CombatCampaignBuildPlan plan)
    {
        if (state.Deck.Count < plan.TargetDeckSizeMinimum)
        {
            return 0d;
        }
        var span = Math.Max(
            1,
            plan.TargetDeckSizeMaximum - plan.TargetDeckSizeMinimum);
        var position = Math.Max(
            0d,
            Math.Min(
                1d,
                (state.Deck.Count - plan.TargetDeckSizeMinimum)
                / (double)span));
        return 1.75d + position * 1.50d;
    }

    private static (int Minimum, int Maximum) TargetDeckBand(int layerNumber)
    {
        return layerNumber switch
        {
            <= 1 => (18, 20),
            2 => (21, 24),
            3 => (24, 27),
            4 => (27, 30),
            5 => (29, 33),
            _ => (30, 35)
        };
    }

    private static Dictionary<string, double> AggregateBuildFeatures(
        CombatCampaignState state,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> lookup)
    {
        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var totalWeight = 0d;
        AddFeatureSources(state.Deck, 1d);
        AddFeatureSources(state.Relics, 1.5d);
        AddFeatureSources(state.Blessings, 1.2d);
        var divisor = Math.Max(1d, totalWeight);
        foreach (var key in result.Keys.ToList())
        {
            result[key] /= divisor;
        }
        return result;

        void AddFeatureSources(IEnumerable<string> ids, double sourceWeight)
        {
            foreach (var id in ids)
            {
                if (!lookup.TryGetValue(id, out var item))
                {
                    continue;
                }
                totalWeight += sourceWeight;
                foreach (var pair in item.Features)
                {
                    result[pair.Key] = result.TryGetValue(
                        pair.Key,
                        out var value)
                        ? value + pair.Value * sourceWeight
                        : pair.Value * sourceWeight;
                }
                foreach (var key in new[]
                         {
                             "rebirth",
                             "time-cage",
                             "gold-cost",
                             "hard-ban"
                         })
                {
                    if (item.Features.ContainsKey(key))
                    {
                        continue;
                    }
                    var inferred = InferredFeature(item, key);
                    if (Math.Abs(inferred) <= 0.000001d)
                    {
                        continue;
                    }
                    result[key] = result.TryGetValue(key, out var inferredValue)
                        ? inferredValue + inferred * sourceWeight
                        : inferred * sourceWeight;
                }
            }
        }
    }

    private static double Dot(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (left == null || right == null) return 0d;
        return left.Sum(item =>
            IsPenaltyFeature(item.Key)
                ? 0d
                : item.Value
                  * (right.TryGetValue(item.Key, out var value) ? value : 0d));
    }

    private static bool IsPenaltyFeature(string key)
    {
        return string.Equals(key, "risk", StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   key,
                   "gold-cost",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   key,
                   "hard-ban",
                   StringComparison.OrdinalIgnoreCase);
    }

    internal static void ApplyProgressionEffect(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        CombatCampaignRewardDefinition reward)
    {
        if (!string.IsNullOrWhiteSpace(reward.OneTimeSpecialVariableKey))
        {
            if (state.SpecialVariables.TryGetValue(
                    reward.OneTimeSpecialVariableKey,
                    out var value)
                && string.Equals(value, "1", StringComparison.Ordinal))
            {
                return;
            }
            state.SpecialVariables[reward.OneTimeSpecialVariableKey] = "1";
        }
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
            Math.Max(0, state.CurrentHp + reward.CurrentHpBonus));
        foreach (var cardId in reward.GrantedCardIds)
        {
            state.Deck.Add(cardId);
        }
        foreach (var blessingId in reward.GrantedBlessingIds)
        {
            if (!state.Blessings.Contains(
                    blessingId,
                    StringComparer.OrdinalIgnoreCase))
            {
                state.Blessings.Add(blessingId);
                var granted = definition.Rewards.FirstOrDefault(item =>
                    item.Kind == CombatCampaignRewardKind.Blessing
                    && string.Equals(
                        item.RewardId,
                        blessingId,
                        StringComparison.OrdinalIgnoreCase));
                if (granted != null)
                {
                    ApplyProgressionEffect(definition, state, granted);
                }
            }
        }
        foreach (var relicId in reward.GrantedRelicIds)
        {
            if (state.Relics.Count < definition.RelicLimit
                && !state.Relics.Contains(
                    relicId,
                    StringComparer.OrdinalIgnoreCase))
            {
                state.Relics.Add(relicId);
                var granted = definition.Rewards.FirstOrDefault(item =>
                    item.Kind == CombatCampaignRewardKind.Relic
                    && string.Equals(
                        item.RewardId,
                        relicId,
                        StringComparison.OrdinalIgnoreCase));
                if (granted != null)
                {
                    ApplyProgressionEffect(definition, state, granted);
                }
            }
        }
        if (reward.RandomCardRemovalCount > 0)
        {
            var removable = state.Deck
                .Select((id, index) => new { id, index })
                .Where(item => !definition.Player.Deck.Contains(
                    item.id,
                    StringComparer.OrdinalIgnoreCase)
                    || state.Deck.Count > definition.Player.Deck.Count)
                .ToList();
            var selected = new List<int>();
            for (var draw = 0;
                 draw < reward.RandomCardRemovalCount
                 && removable.Count > 0;
                 draw++)
            {
                var selectedIndex = StableProgressionIndex(
                    state.WorldSeed,
                    reward.RewardId + ":remove-card",
                    state.CurrentGameLevel * 31 + draw,
                    removable.Count);
                selected.Add(removable[selectedIndex].index);
                removable.RemoveAt(selectedIndex);
            }
            foreach (var index in selected.OrderByDescending(item => item))
            {
                state.Deck.RemoveAt(index);
            }
        }
        if (!state.RewardVariables.ContainsKey(reward.RewardId))
        {
            state.RewardVariables[reward.RewardId] =
                new Dictionary<string, string>(
                    reward.InitialVariables,
                    StringComparer.OrdinalIgnoreCase);
        }
        ClampAttributes(state);
    }

    internal static void RemoveProgressionEffect(
        CombatCampaignDefinition definition,
        CombatCampaignState state,
        CombatCampaignRewardDefinition reward)
    {
        var unsupportedKey = reward.Kind.ToString().ToLowerInvariant()
                             + ":"
                             + reward.RewardId;
        state.UnsupportedProgressionRules.RemoveAll(item => string.Equals(
            item,
            unsupportedKey,
            StringComparison.OrdinalIgnoreCase));
        state.RewardVariables.Remove(reward.RewardId);
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
        return RunCore(
            definition,
            plan,
            ruleset,
            policyFactory,
            resumeFrom,
            checkpointSink,
            null,
            cancellationToken);
    }

    public CombatCampaignResult RunMonitored(
        CombatCampaignDefinition definition,
        CombatCampaignWorldPlan plan,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory policyFactory,
        Action<int, CombatSimulationResult>? battleProgress,
        CancellationToken cancellationToken = default)
    {
        return RunCore(
            definition,
            plan,
            ruleset,
            policyFactory,
            null,
            null,
            battleProgress,
            cancellationToken);
    }

    private CombatCampaignResult RunCore(
        CombatCampaignDefinition definition,
        CombatCampaignWorldPlan plan,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory policyFactory,
        CombatCampaignCheckpoint? resumeFrom,
        Action<CombatCampaignCheckpoint>? checkpointSink,
        Action<int, CombatSimulationResult>? battleProgress,
        CancellationToken cancellationToken)
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
        var rewardLookup = definition.Rewards.ToDictionary(
            item => item.RewardId,
            StringComparer.OrdinalIgnoreCase);
        var rewardCatalog = definition.Rewards
            .Where(item => item.Kind != CombatCampaignRewardKind.Card)
            .Select(item => new CombatScenarioRewardCatalogEntry
            {
                RewardId = item.RewardId,
                Kind = item.Kind.ToString(),
                Tier = item.Tier,
                Negative = item.Negative
            })
            .ToList();
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
                rewardLookup,
                rewardCatalog,
                plan.WorldSeed);
            var battle = engine.Run(
                scenario,
                ruleset,
                policyFactory.Create(),
                cancellationToken);
            checkpoint.Battles.Add(battle);
            battleProgress?.Invoke(checkpoint.Battles.Count, battle);
            checkpoint.NextEncounterIndex = index + 1;
            checkpoint.State.CurrentHp = Math.Max(0, battle.FinalPlayerHp);
            ApplyPersistentBattleDeltas(
                definition,
                checkpoint.State,
                battle,
                rewardLookup);
            checkpoint.State.RewardVariables =
                battle.RewardVariables.ToDictionary(
                    item => item.Key,
                    item => new Dictionary<string, string>(
                        item.Value,
                        StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            checkpoint.State.SpecialVariables =
                new Dictionary<string, string>(
                    battle.CampaignVariables,
                    StringComparer.OrdinalIgnoreCase);
            checkpoint.State.SpecialVariables.Remove("ResurrectionCount");
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
            TotalBattles = plan.Encounters.Count,
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
        CombatSimulationResult battle,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> rewardLookup)
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
            if (string.Equals(pair.Key, "Money", StringComparison.OrdinalIgnoreCase))
            {
                state.Money = Math.Max(0, state.Money + pair.Value);
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
        foreach (var mutation in battle.RewardMutations)
        {
            if (!rewardLookup.TryGetValue(mutation.RewardId, out var reward))
            {
                var unsupported = "reward-mutation:" + mutation.RewardId;
                if (!state.UnsupportedProgressionRules.Contains(
                        unsupported,
                        StringComparer.OrdinalIgnoreCase))
                {
                    state.UnsupportedProgressionRules.Add(unsupported);
                }
                continue;
            }
            var adding = !string.Equals(
                mutation.Operation,
                "Remove",
                StringComparison.OrdinalIgnoreCase);
            if (adding && reward.ReplacementRelicTier > 0)
            {
                var replacements = definition.Rewards
                    .Where(item =>
                        item.Kind == CombatCampaignRewardKind.Relic
                        && item.Tier == reward.ReplacementRelicTier
                        && !string.Equals(
                            item.RewardId,
                            reward.RewardId,
                            StringComparison.OrdinalIgnoreCase)
                        && !state.Relics.Contains(
                            item.RewardId,
                            StringComparer.OrdinalIgnoreCase))
                    .OrderBy(item => item.RewardId, StringComparer.Ordinal)
                    .ToList();
                if (replacements.Count > 0)
                {
                    reward = replacements[(int)(
                        battle.Seed % (ulong)replacements.Count)];
                }
            }
            var collection = reward.Kind == CombatCampaignRewardKind.Relic
                ? state.Relics
                : state.Blessings;
            if (!adding)
            {
                var index = collection.FindIndex(item => string.Equals(
                    item,
                    reward.RewardId,
                    StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    collection.RemoveAt(index);
                    if (!collection.Contains(
                            reward.RewardId,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        CombatCampaignRewardSelector.RemoveProgressionEffect(
                            definition,
                            state,
                            reward);
                    }
                }
                continue;
            }
            if (reward.Kind == CombatCampaignRewardKind.Blessing)
            {
                state.Blessings.Add(reward.RewardId);
                CombatCampaignRewardSelector.ApplyProgressionEffect(
                    definition,
                    state,
                    reward);
                continue;
            }
            if (state.Relics.Contains(
                    reward.RewardId,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (state.Relics.Count >= definition.RelicLimit)
            {
                var scores = CombatCampaignRewardSelector.ScoreRewards(
                    definition,
                    state,
                    state.Relics.Append(reward.RewardId),
                    rewardLookup,
                    state.CurrentGameLevel,
                    CombatCampaignRewardKind.Relic);
                var offered = scores.First(item => string.Equals(
                    item.RewardId,
                    reward.RewardId,
                    StringComparison.OrdinalIgnoreCase));
                var worst = scores
                    .Where(item => state.Relics.Contains(
                        item.RewardId,
                        StringComparer.OrdinalIgnoreCase))
                    .OrderBy(item => item.Total)
                    .ThenByDescending(
                        item => item.RewardId,
                        StringComparer.Ordinal)
                    .First();
                if (offered.Total <= worst.Total)
                {
                    continue;
                }
                var replaceIndex = state.Relics.FindIndex(item => string.Equals(
                    item,
                    worst.RewardId,
                    StringComparison.OrdinalIgnoreCase));
                if (rewardLookup.TryGetValue(worst.RewardId, out var removed))
                {
                    CombatCampaignRewardSelector.RemoveProgressionEffect(
                        definition,
                        state,
                        removed);
                }
                state.Relics[replaceIndex] = reward.RewardId;
            }
            else
            {
                state.Relics.Add(reward.RewardId);
            }
            CombatCampaignRewardSelector.ApplyProgressionEffect(
                definition,
                state,
                reward);
        }
        CombatCampaignRewardSelector.ClampAttributes(state);
    }

    private static CombatScenarioDefinition BuildScenario(
        CombatCampaignDefinition definition,
        CombatCampaignPlannedEncounter encounter,
        CombatCampaignState state,
        CombatCampaignDifficultyDefinition difficulty,
        IReadOnlyDictionary<string, int> enemyLevels,
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> rewardLookup,
        List<CombatScenarioRewardCatalogEntry> rewardCatalog,
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
                    .Concat(SelectedRewardStatuses(rewardLookup, state))
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
            MovePlayedCardAfterResolution =
                difficulty.MovePlayedCardAfterResolution,
            InitialDiscardCards = new List<string>(
                difficulty.InitialDiscardCards
                ?? new List<string>()),
            DirectHpLossAfterPlayerCard =
                Math.Max(0, difficulty.DirectHpLossAfterPlayerCard),
            RequireAuthoritativeRules = definition.RequireAuthoritativeRules,
            TraceLevel = definition.FullTraceFinalEncounterOnly
                         && encounter.Kind != CombatCampaignEncounterKind.FinalBoss
                ? CombatSimulationTraceLevel.Summary
                : definition.TraceLevel,
            Limits = definition.Limits.Normalize()
        };
        scenario.CampaignVariables = new Dictionary<string, string>(
            state.SpecialVariables,
            StringComparer.OrdinalIgnoreCase);
        scenario.CampaignVariables["ResurrectionCount"] = "0";
        scenario.Player.Variables["EncounterKind"] = (int)encounter.Kind;
        scenario.RewardCatalog = rewardCatalog;
        scenario.RewardRules = CombatCampaignRewardRuleProjector.Build(
            state,
            rewardLookup);
        foreach (var pair in difficulty.PlayerVariables
                     ?? new Dictionary<string, double>())
        {
            scenario.Player.Variables[pair.Key] = pair.Value;
        }
        scenario.Player.Variables["Money"] = state.Money;
        for (var index = 0; index < encounter.EnemyIds.Count; index++)
        {
            var enemyId = encounter.EnemyIds[index];
            var nativeLevel = enemyLevels.TryGetValue(enemyId, out var level) ? level : 1;
            var growth = ((encounter.GameLevel - 1) / 12) - nativeLevel + 1;
            var highLevelHpMultiplier =
                encounter.GameLevel
                >= difficulty.AdditionalEnemyHpMultiplierMinimumGameLevel
                    ? Math.Max(
                        0.1d,
                        difficulty.AdditionalEnemyHpMultiplier)
                    : 1d;
            var hpScale = Math.Max(0.1d, 1d + growth * 0.3d)
                          * Math.Max(0.1d, difficulty.EnemyHpMultiplier)
                          * highLevelHpMultiplier;
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
                    .ToList(),
                Variables = new Dictionary<string, double>(
                    difficulty.EnemyVariables
                    ?? new Dictionary<string, double>(),
                    StringComparer.OrdinalIgnoreCase)
            });
            scenario.Enemies[index].Variables["GameLevel"] =
                encounter.GameLevel;
            scenario.Enemies[index].Variables["EncounterKind"] =
                (int)encounter.Kind;
        }
        return scenario;
    }

    private static IEnumerable<CombatInitialStatus> SelectedRewardStatuses(
        IReadOnlyDictionary<string, CombatCampaignRewardDefinition> rewardLookup,
        CombatCampaignState state)
    {
        foreach (var id in state.Relics.Concat(state.Blessings))
        {
            if (!rewardLookup.TryGetValue(id, out var reward)) continue;
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
        CombatCampaignRewardSelector.RefreshBuildPlan(definition, state);
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
            var resumed = CloneCheckpoint(resumeFrom);
            resumed.State.WorldSeed = plan.WorldSeed;
            return resumed;
        }
        var state = new CombatCampaignState
        {
            WorldSeed = plan.WorldSeed,
            MaxHp = definition.Player.MaxHp,
            CurrentHp = definition.Player.CurrentHp,
            Money = Math.Max(0, definition.InitialMoney),
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
        CombatCampaignRewardSelector.RefreshBuildPlan(definition, state);
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
            WorldSeed = source.WorldSeed,
            CurrentLayer = source.CurrentLayer,
            CurrentGameLevel = source.CurrentGameLevel,
            MaxHp = source.MaxHp,
            CurrentHp = source.CurrentHp,
            Money = source.Money,
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
            RewardVariables = source.RewardVariables.ToDictionary(
                item => item.Key,
                item => new Dictionary<string, string>(
                    item.Value,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            SpecialVariables = new Dictionary<string, string>(
                source.SpecialVariables,
                StringComparer.OrdinalIgnoreCase),
            UnsupportedProgressionRules = new List<string>(
                source.UnsupportedProgressionRules),
            BuildPlan = source.BuildPlan?.Clone() ?? new CombatCampaignBuildPlan()
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
