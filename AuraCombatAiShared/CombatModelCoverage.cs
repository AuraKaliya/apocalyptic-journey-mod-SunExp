using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationTrainingSubject
{
    public int SchemaVersion { get; set; } = 1;

    public string RoleId { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public string GameParameterPresetId { get; set; } = "";

    public string GameParameterHash { get; set; } = "";

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public List<string> StartingDeckCardIds { get; set; } = new();

    public List<string> RoleSkillCardIds { get; set; } = new();

    public List<string> FamiliarBlessingIds { get; set; } = new();

    public List<string> RoleInitialStatusIds { get; set; } = new();

    public int PreferredDeckSizeMinimum { get; set; }

    public int PreferredDeckSizeMaximum { get; set; }
}

public sealed class CombatFoundationDeclaredCoverage
{
    public int SchemaVersion { get; set; } = 1;

    public string Source { get; set; } = "training-campaign";

    public bool EntityCoverageKnown { get; set; }

    public List<string> CardIds { get; set; } = new();

    public List<string> RoleSkillCardIds { get; set; } = new();

    public List<string> EnemyIds { get; set; } = new();

    public List<string> StatusIds { get; set; } = new();

    public List<string> RelicIds { get; set; } = new();

    public List<string> BlessingIds { get; set; } = new();
}

public sealed class CombatModelRuntimeContext
{
    public string RoleId { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public List<string> RoleSkillCardIds { get; set; } = new();

    public List<string> FamiliarBlessingIds { get; set; } = new();

    public int PreferredDeckSizeMinimum { get; set; }

    public int PreferredDeckSizeMaximum { get; set; }
}

public sealed class CombatModelCoverageAssessment
{
    public string Level { get; set; } = "partial";

    public bool RoleMatches { get; set; }

    public bool PartnerMatches { get; set; }

    public bool RoleSkillFallbackRequired { get; set; }

    public bool EntityCoverageKnown { get; set; }

    public List<string> RuntimeExtraCardPackIds { get; set; } = new();

    public List<string> TrainingOnlyCardPackIds { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public string Summary { get; set; } = "";
}

public static class CombatFoundationModelCoverageProtocol
{
    public static CombatFoundationTrainingSubject CreateTrainingSubject(
        CombatCampaignDefinition campaign)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        var player = campaign.Player ?? new CombatPlayerSetup();
        return Normalize(new CombatFoundationTrainingSubject
        {
            RoleId = player.RoleId,
            PartnerId = player.PartnerId,
            GameParameterPresetId = player.GameParameterPresetId,
            GameParameterHash = player.GameParameterHash,
            EnabledRewardCardPackIds =
                new List<string>(campaign.EnabledRewardCardPackIds
                                 ?? new List<string>()),
            StartingDeckCardIds =
                new List<string>(player.Deck ?? new List<string>()),
            RoleSkillCardIds =
                new List<string>(player.SkillCardIds ?? new List<string>()),
            FamiliarBlessingIds =
                new List<string>(
                    player.FamiliarBlessingIds ?? new List<string>()),
            RoleInitialStatusIds =
                (player.InitialStatuses ?? new List<CombatInitialStatus>())
                .Where(item => item != null)
                .Select(item => item.StatusId)
                .ToList(),
            PreferredDeckSizeMinimum = campaign.TargetDeckSizeMinimum,
            PreferredDeckSizeMaximum = campaign.TargetDeckSizeMaximum
        });
    }

    public static CombatFoundationTrainingSubject FromLegacyPackage(
        CombatFoundationModelPackage package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        return Normalize(new CombatFoundationTrainingSubject
        {
            RoleId = package.RoleId,
            PartnerId = package.PartnerId,
            GameParameterPresetId = package.GameParameterPresetId,
            GameParameterHash = package.GameParameterHash,
            EnabledRewardCardPackIds =
                new List<string>(package.EnabledRewardCardPackIds
                                 ?? new List<string>()),
            PreferredDeckSizeMinimum = package.PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum = package.PreferredDeckSizeMaximum
        });
    }

    public static CombatFoundationDeclaredCoverage CreateDeclaredCoverage(
        CombatCampaignDefinition campaign,
        CombatRulesetDocument ruleset,
        CombatFoundationTrainingSubject? subject = null)
    {
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        return CreateDeclaredCoverage(
            campaign,
            ruleset.Cards,
            ruleset.Enemies,
            ruleset.Statuses,
            subject);
    }

    public static CombatFoundationDeclaredCoverage CreateDeclaredCoverage(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatFoundationTrainingSubject? subject = null)
    {
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        return CreateDeclaredCoverage(
            campaign,
            ruleset.SnapshotCards(),
            ruleset.SnapshotEnemies(),
            ruleset.SnapshotStatuses(),
            subject);
    }

    public static CombatFoundationDeclaredCoverage LegacyUnknownCoverage(
        CombatFoundationTrainingSubject subject)
    {
        var normalized = Normalize(subject);
        return new CombatFoundationDeclaredCoverage
        {
            Source = "legacy-package-metadata",
            EntityCoverageKnown = false,
            CardIds = NormalizeIds(normalized.StartingDeckCardIds),
            RoleSkillCardIds = NormalizeIds(normalized.RoleSkillCardIds),
            StatusIds = NormalizeIds(normalized.RoleInitialStatusIds),
            BlessingIds = NormalizeIds(normalized.FamiliarBlessingIds)
        };
    }

    public static CombatModelCoverageAssessment Assess(
        CombatFoundationTrainingSubject subject,
        CombatFoundationDeclaredCoverage coverage,
        CombatModelRuntimeContext runtime)
    {
        var training = Normalize(subject);
        var current = runtime ?? new CombatModelRuntimeContext();
        var trainingPacks = new HashSet<string>(
            NormalizeIds(training.EnabledRewardCardPackIds),
            StringComparer.OrdinalIgnoreCase);
        var runtimePacks = new HashSet<string>(
            NormalizeIds(current.EnabledRewardCardPackIds),
            StringComparer.OrdinalIgnoreCase);
        var roleMatches = string.Equals(
            training.RoleId,
            current.RoleId,
            StringComparison.OrdinalIgnoreCase);
        var partnerMatches = string.Equals(
            training.PartnerId,
            current.PartnerId,
            StringComparison.OrdinalIgnoreCase);
        var runtimeExtra = runtimePacks
            .Where(item => !trainingPacks.Contains(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var trainingOnly = trainingPacks
            .Where(item => !runtimePacks.Contains(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var deckCovered =
            current.PreferredDeckSizeMinimum >= training.PreferredDeckSizeMinimum
            && current.PreferredDeckSizeMaximum
               <= training.PreferredDeckSizeMaximum;
        var result = new CombatModelCoverageAssessment
        {
            RoleMatches = roleMatches,
            PartnerMatches = partnerMatches,
            RoleSkillFallbackRequired = !roleMatches,
            EntityCoverageKnown = coverage?.EntityCoverageKnown == true,
            RuntimeExtraCardPackIds = runtimeExtra,
            TrainingOnlyCardPackIds = trainingOnly
        };
        if (!roleMatches)
        {
            result.Warnings.Add("当前角色不同，角色技能使用默认 AI");
        }
        if (!partnerMatches)
        {
            result.Warnings.Add("当前使魔不同，未覆盖的使魔特征按默认值处理");
        }
        if (runtimeExtra.Count > 0)
        {
            result.Warnings.Add(
                "当前多出的卡包按未知卡牌回退：" + string.Join(", ", runtimeExtra));
        }
        if (!deckCovered)
        {
            result.Warnings.Add("当前卡组规模超出训练倾向，按部分覆盖运行");
        }
        if (coverage?.EntityCoverageKnown != true)
        {
            result.Warnings.Add("旧模型包没有实体级覆盖清单，按兼容模式运行");
        }
        result.Level = roleMatches
                       && partnerMatches
                       && runtimeExtra.Count == 0
                       && deckCovered
            ? "full"
            : "partial";
        result.Summary = result.Level == "full"
            ? trainingOnly.Count > 0
                ? "完全覆盖（训练卡包是当前卡包的超集）"
                : "完全覆盖"
            : "部分覆盖；" + string.Join("；", result.Warnings);
        return result;
    }

    public static CombatFoundationTrainingSubject Normalize(
        CombatFoundationTrainingSubject subject)
    {
        subject ??= new CombatFoundationTrainingSubject();
        subject.RoleId = (subject.RoleId ?? "").Trim();
        subject.PartnerId = (subject.PartnerId ?? "").Trim();
        subject.GameParameterPresetId =
            (subject.GameParameterPresetId ?? "").Trim();
        subject.GameParameterHash = (subject.GameParameterHash ?? "").Trim();
        subject.EnabledRewardCardPackIds =
            NormalizeIds(subject.EnabledRewardCardPackIds);
        subject.StartingDeckCardIds =
            NormalizeIds(subject.StartingDeckCardIds, preserveOrder: true);
        subject.RoleSkillCardIds = NormalizeIds(subject.RoleSkillCardIds);
        subject.FamiliarBlessingIds =
            NormalizeIds(subject.FamiliarBlessingIds);
        subject.RoleInitialStatusIds =
            NormalizeIds(subject.RoleInitialStatusIds);
        subject.PreferredDeckSizeMinimum =
            Math.Max(1, subject.PreferredDeckSizeMinimum);
        subject.PreferredDeckSizeMaximum = Math.Max(
            subject.PreferredDeckSizeMinimum,
            subject.PreferredDeckSizeMaximum);
        return subject;
    }

    private static CombatFoundationDeclaredCoverage CreateDeclaredCoverage(
        CombatCampaignDefinition campaign,
        IEnumerable<CombatCardDefinition> cards,
        IEnumerable<CombatEnemyDefinition> enemies,
        IEnumerable<CombatStatusDefinition> statuses,
        CombatFoundationTrainingSubject? configuredSubject)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        var subject = Normalize(
            configuredSubject ?? CreateTrainingSubject(campaign));
        var enabledPacks = new HashSet<string>(
            subject.EnabledRewardCardPackIds,
            StringComparer.OrdinalIgnoreCase);
        var cardIds = new HashSet<string>(
            subject.StartingDeckCardIds.Concat(subject.RoleSkillCardIds),
            StringComparer.OrdinalIgnoreCase);
        var enemyIds = new HashSet<string>(
            (campaign.Enemies ?? new List<CombatCampaignEnemyCatalogEntry>())
            .Select(item => item.EnemyId)
            .Concat(
                (campaign.Encounters
                 ?? new List<CombatCampaignEncounterDefinition>())
                .SelectMany(item => item.EnemyIds ?? new List<string>())),
            StringComparer.OrdinalIgnoreCase);
        var statusIds = new HashSet<string>(
            subject.RoleInitialStatusIds,
            StringComparer.OrdinalIgnoreCase);
        var relicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blessingIds = new HashSet<string>(
            subject.FamiliarBlessingIds,
            StringComparer.OrdinalIgnoreCase);

        foreach (var difficulty in campaign.Difficulties
                     ?? new List<CombatCampaignDifficultyDefinition>())
        {
            AddStatuses(statusIds, difficulty.EnemyInitialStatuses);
        }
        foreach (var reward in campaign.Rewards
                     ?? new List<CombatCampaignRewardDefinition>())
        {
            if (reward.Kind == CombatCampaignRewardKind.Card
                && (string.IsNullOrWhiteSpace(reward.RewardCardPackId)
                    || enabledPacks.Contains(reward.RewardCardPackId)))
            {
                AddId(cardIds, reward.RewardId);
            }
            else if (reward.Kind == CombatCampaignRewardKind.Relic)
            {
                AddId(relicIds, reward.RewardId);
            }
            else if (reward.Kind == CombatCampaignRewardKind.Blessing)
            {
                AddId(blessingIds, reward.RewardId);
            }
            AddIds(cardIds, reward.GrantedCardIds);
            AddIds(relicIds, reward.GrantedRelicIds);
            AddIds(relicIds, reward.RelicSetRequiredIds);
            AddIds(relicIds, reward.RelicSetConsumedIds);
            AddIds(relicIds, reward.RelicSetGrantedIds);
            AddIds(blessingIds, reward.GrantedBlessingIds);
            AddStatuses(statusIds, reward.InitialStatuses);
        }
        foreach (var strategy in campaign.Strategies
                     ?? new List<CombatCampaignStrategyDefinition>())
        {
            AddIds(cardIds, strategy.RequiredCardIds);
            AddIds(cardIds, strategy.RequiredSkillCardIds);
            AddIds(relicIds, strategy.RequiredRelicIds);
            AddIds(blessingIds, strategy.RequiredBlessingIds);
        }

        var cardMap = (cards ?? Array.Empty<CombatCardDefinition>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.CardId))
            .GroupBy(item => item.CardId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var enemyMap = (enemies ?? Array.Empty<CombatEnemyDefinition>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.EnemyId))
            .GroupBy(item => item.EnemyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var statusMap = (statuses ?? Array.Empty<CombatStatusDefinition>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.StatusId))
            .GroupBy(item => item.StatusId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var pendingCards = new Queue<string>(cardIds);
        var visitedCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pendingCards.Count > 0)
        {
            var cardId = pendingCards.Dequeue();
            if (!visitedCards.Add(cardId)
                || !cardMap.TryGetValue(cardId, out var card))
            {
                continue;
            }
            CollectEffects(
                card.Effects
                    .Concat(card.DrawEffects)
                    .Concat(card.DiscardEffects),
                cardIds,
                statusIds,
                enemyIds,
                cardMap,
                statusMap,
                enemyMap,
                pendingCards);
        }
        foreach (var enemyId in enemyIds.ToArray())
        {
            if (!enemyMap.TryGetValue(enemyId, out var enemy))
            {
                continue;
            }
            AddStatuses(statusIds, enemy.InitialStatuses);
            CollectEffects(
                enemy.Intents.SelectMany(item => item.Effects),
                cardIds,
                statusIds,
                enemyIds,
                cardMap,
                statusMap,
                enemyMap,
                pendingCards);
        }
        var pendingStatuses = new Queue<string>(statusIds);
        var visitedStatuses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pendingStatuses.Count > 0)
        {
            var statusId = pendingStatuses.Dequeue();
            if (!visitedStatuses.Add(statusId)
                || !statusMap.TryGetValue(statusId, out var status))
            {
                continue;
            }
            var previousCount = statusIds.Count;
            CollectEffects(
                status.Triggers.SelectMany(item => item.Effects),
                cardIds,
                statusIds,
                enemyIds,
                cardMap,
                statusMap,
                enemyMap,
                pendingCards);
            if (statusIds.Count > previousCount)
            {
                foreach (var added in statusIds.Where(item =>
                             !visitedStatuses.Contains(item)))
                {
                    pendingStatuses.Enqueue(added);
                }
            }
        }

        return new CombatFoundationDeclaredCoverage
        {
            Source = "training-campaign-declared-v1",
            EntityCoverageKnown = cardMap.Count > 0
                                  && enemyMap.Count > 0,
            CardIds = Sorted(cardIds),
            RoleSkillCardIds = Sorted(subject.RoleSkillCardIds),
            EnemyIds = Sorted(enemyIds),
            StatusIds = Sorted(statusIds),
            RelicIds = Sorted(relicIds),
            BlessingIds = Sorted(blessingIds)
        };
    }

    private static void CollectEffects(
        IEnumerable<CombatSimulationEffectDefinition> effects,
        ISet<string> cardIds,
        ISet<string> statusIds,
        ISet<string> enemyIds,
        IReadOnlyDictionary<string, CombatCardDefinition> cards,
        IReadOnlyDictionary<string, CombatStatusDefinition> statuses,
        IReadOnlyDictionary<string, CombatEnemyDefinition> enemies,
        Queue<string> pendingCards)
    {
        foreach (var effect in effects
                     ?? Array.Empty<CombatSimulationEffectDefinition>())
        {
            foreach (var id in new[]
                     {
                         effect.DefinitionId,
                         effect.SecondaryDefinitionId
                     })
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }
                if (cards.ContainsKey(id) && cardIds.Add(id))
                {
                    pendingCards.Enqueue(id);
                }
                if (statuses.ContainsKey(id))
                {
                    statusIds.Add(id);
                }
                if (enemies.ContainsKey(id))
                {
                    enemyIds.Add(id);
                }
            }
        }
    }

    private static void AddStatuses(
        ISet<string> values,
        IEnumerable<CombatInitialStatus> statuses)
    {
        AddIds(
            values,
            (statuses ?? Array.Empty<CombatInitialStatus>())
            .Where(item => item != null)
            .Select(item => item.StatusId));
    }

    private static void AddIds(ISet<string> values, IEnumerable<string> ids)
    {
        foreach (var id in ids ?? Array.Empty<string>())
        {
            AddId(values, id);
        }
    }

    private static void AddId(ISet<string> values, string id)
    {
        var normalized = (id ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            values.Add(normalized);
        }
    }

    private static List<string> NormalizeIds(
        IEnumerable<string> values,
        bool preserveOrder = false)
    {
        var normalized = (values ?? Array.Empty<string>())
            .Select(item => (item ?? "").Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return (preserveOrder
                ? normalized
                : normalized.OrderBy(
                    item => item,
                    StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> Sorted(IEnumerable<string> values)
    {
        return NormalizeIds(values);
    }
}

public sealed class CoverageAwareCombatPolicyValueModel :
    ICombatPolicyValueModel
{
    private static readonly string[] EntityFeaturePrefixes =
    {
        "deck:",
        "hand:",
        "retainedHand:",
        "draw:",
        "discard:",
        "exhaust:",
        "playerStatus:",
        "enemyStatus:",
        "status:",
        "enemy:",
        "enemyHp:",
        "relic:",
        "blessing:"
    };

    private readonly ICombatPolicyValueModel inner;
    private readonly bool roleMatches;
    private readonly bool entityCoverageKnown;
    private readonly HashSet<string> cardIds;
    private readonly HashSet<string> statusIds;
    private readonly HashSet<string> enemyIds;
    private readonly HashSet<string> relicIds;
    private readonly HashSet<string> blessingIds;

    public CoverageAwareCombatPolicyValueModel(
        ICombatPolicyValueModel inner,
        CombatFoundationTrainingSubject subject,
        CombatFoundationDeclaredCoverage coverage,
        CombatModelRuntimeContext runtime)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        var training =
            CombatFoundationModelCoverageProtocol.Normalize(subject);
        roleMatches = string.Equals(
            training.RoleId,
            runtime?.RoleId,
            StringComparison.OrdinalIgnoreCase);
        entityCoverageKnown = coverage?.EntityCoverageKnown == true;
        cardIds = NewSet(coverage?.CardIds);
        statusIds = NewSet(coverage?.StatusIds);
        enemyIds = NewSet(coverage?.EnemyIds);
        relicIds = NewSet(coverage?.RelicIds);
        blessingIds = NewSet(coverage?.BlessingIds);
    }

    public string ModelId => inner.ModelId;

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
        input ??= new CombatPolicyValueInput();
        var filtered = FilterState(input, out var stateChanged);
        var fallbackIds = input.Candidates
            .Where(ShouldFallback)
            .Select(item => item.CandidateId ?? "")
            .ToList();
        var actionable = input.Candidates
            .Where(item => !string.Equals(
                item?.ActionKind,
                CombatActionKind.EndTurn.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (actionable.Count > 0
            && actionable.All(ShouldFallback))
        {
            return new CombatPolicyValuePrediction();
        }
        var prediction = inner.Evaluate(stateChanged ? filtered : input);
        foreach (var candidateId in fallbackIds)
        {
            prediction.PolicyLogits[candidateId] = 0d;
        }
        return prediction;
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        var count = inputs?.Count ?? 0;
        var results = new CombatPolicyValuePrediction[count];
        var activeInputs = new List<CombatPolicyValueInput>(count);
        var activeIndices = new List<int>(count);
        var fallbackIds = new List<List<string>>(count);
        for (var index = 0; index < count; index++)
        {
            var input = inputs![index] ?? new CombatPolicyValueInput();
            var filtered = FilterState(input, out var stateChanged);
            var fallback = input.Candidates
                .Where(ShouldFallback)
                .Select(item => item.CandidateId ?? "")
                .ToList();
            fallbackIds.Add(fallback);
            var actionable = input.Candidates
                .Where(item => !string.Equals(
                    item?.ActionKind,
                    CombatActionKind.EndTurn.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (actionable.Count > 0 && actionable.All(ShouldFallback))
            {
                results[index] = new CombatPolicyValuePrediction();
                continue;
            }
            activeIndices.Add(index);
            activeInputs.Add(stateChanged ? filtered : input);
        }
        var activeResults = inner.EvaluateBatch(activeInputs);
        for (var activeIndex = 0;
             activeIndex < activeIndices.Count;
             activeIndex++)
        {
            var resultIndex = activeIndices[activeIndex];
            var prediction = activeResults[activeIndex];
            foreach (var candidateId in fallbackIds[resultIndex])
            {
                prediction.PolicyLogits[candidateId] = 0d;
            }
            results[resultIndex] = prediction;
        }
        return results;
    }

    private bool ShouldFallback(CombatPolicyValueCandidate candidate)
    {
        if (candidate == null)
        {
            return true;
        }
        if (!roleMatches
            && string.Equals(
                candidate.ActionKind,
                CombatActionKind.UseSkill.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return entityCoverageKnown
               && string.Equals(
                   candidate.ActionKind,
                   CombatActionKind.PlayCard.ToString(),
                   StringComparison.OrdinalIgnoreCase)
               && !cardIds.Contains((candidate.SourceId ?? "").Trim());
    }

    private CombatPolicyValueInput FilterState(
        CombatPolicyValueInput input,
        out bool changed)
    {
        changed = false;
        if (!entityCoverageKnown || input.StateFeatures.Count == 0)
        {
            return input;
        }
        changed = input.StateFeatures.Keys.Any(key =>
            !IsCoveredStateFeature(key));
        if (!changed)
        {
            return input;
        }
        return new CombatPolicyValueInput
        {
            StateFeatures = input.StateFeatures
                .Where(item => IsCoveredStateFeature(item.Key))
                .ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase),
            Candidates = input.Candidates
        };
    }

    private bool IsCoveredStateFeature(string key)
    {
        return CoveredSuffix(key, "deck:", cardIds)
               || CoveredSuffix(key, "hand:", cardIds)
               || CoveredSuffix(key, "retainedHand:", cardIds)
               || CoveredSuffix(key, "draw:", cardIds)
               || CoveredSuffix(key, "discard:", cardIds)
               || CoveredSuffix(key, "exhaust:", cardIds)
               || CoveredSuffix(key, "playerStatus:", statusIds)
               || CoveredSuffix(key, "enemyStatus:", statusIds)
               || CoveredSuffix(key, "status:", statusIds)
               || CoveredSuffix(key, "enemy:", enemyIds)
               || CoveredSuffix(key, "enemyHp:", enemyIds)
               || CoveredSuffix(key, "relic:", relicIds)
               || CoveredSuffix(key, "blessing:", blessingIds)
               || !IsEntityFeature(key);
    }

    private static bool CoveredSuffix(
        string key,
        string prefix,
        ISet<string> allowed)
    {
        return key != null
               && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && allowed.Contains(key.Substring(prefix.Length));
    }

    private static bool IsEntityFeature(string key)
    {
        return EntityFeaturePrefixes.Any(prefix => (key ?? "").StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> NewSet(IEnumerable<string>? values)
    {
        return new HashSet<string>(
            (values ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }
}
