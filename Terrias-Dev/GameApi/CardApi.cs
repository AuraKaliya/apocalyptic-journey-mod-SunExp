using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.GameApi;

public sealed class CardGrantRequest
{
    private readonly List<CardGrantMutation> mutations = new();
    private readonly Dictionary<string, string> runtimePresentation = new(StringComparer.Ordinal);

    public CardGrantRequest(string cardId)
    {
        CardId = cardId ?? "";
    }

    public string CardId { get; }

    public string RuntimeTags { get; private set; } = "";

    public string Source { get; private set; } = "";

    public bool AbortOnMutationFailure { get; private set; }

    public bool RequiresWritableRuntimeConfig { get; private set; }

    public IReadOnlyList<CardGrantMutation> Mutations => mutations;

    public IReadOnlyDictionary<string, string> RuntimePresentation => runtimePresentation;

    public static CardGrantRequest ToHand(string cardId)
    {
        return new CardGrantRequest(cardId);
    }

    public CardGrantRequest WithRuntimeTags(params string[] tags)
    {
        RuntimeTags = JoinTags(tags);
        return this;
    }

    public CardGrantRequest WithSource(string source)
    {
        Source = source ?? "";
        return this;
    }

    public CardGrantRequest RequireMutations()
    {
        AbortOnMutationFailure = true;
        RequiresWritableRuntimeConfig = true;
        return this;
    }

    public CardGrantRequest WithWritableRuntimeConfig()
    {
        RequiresWritableRuntimeConfig = true;
        return this;
    }

    public CardGrantRequest WithRuntimePresentation(IDictionary<string, string> values)
    {
        if (values == null)
        {
            return this;
        }

        foreach (var entry in values)
        {
            if (IsRuntimePresentationKey(entry.Key))
            {
                runtimePresentation[entry.Key] = entry.Value ?? "";
            }
        }

        if (runtimePresentation.Count > 0)
        {
            RequiresWritableRuntimeConfig = true;
        }

        return this;
    }

    public CardGrantRequest Configure(string name, Action<DataConfig> apply)
    {
        mutations.Add(new CardGrantMutation(name, apply));
        RequiresWritableRuntimeConfig = true;
        return this;
    }

    public CardGrantRequest Configure(CardGrantMutation mutation)
    {
        if (mutation.Apply != null)
        {
            mutations.Add(mutation);
            RequiresWritableRuntimeConfig = true;
        }

        return this;
    }

    private static string JoinTags(IEnumerable<string> tags)
    {
        return string.Join(",", tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.Ordinal));
    }

    private static bool IsRuntimePresentationKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key)
            && (string.Equals(key, "Name", StringComparison.Ordinal)
            || key.StartsWith("Name_", StringComparison.Ordinal)
            || string.Equals(key, "Description", StringComparison.Ordinal)
            || key.StartsWith("Description_", StringComparison.Ordinal)
            || string.Equals(key, "Icon", StringComparison.Ordinal)
            || key.StartsWith("Icon_", StringComparison.Ordinal));
    }
}

public sealed class CardGrantMutation
{
    public CardGrantMutation(string name, Action<DataConfig> apply)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "anonymous" : name;
        Apply = apply;
    }

    public string Name { get; }

    public Action<DataConfig> Apply { get; }
}

public sealed class CardGrantResult
{
    private CardGrantResult(
        bool success,
        string cardId,
        DataConfig? config,
        string failureStep,
        string failureReason,
        IReadOnlyList<string> warnings)
    {
        Success = success;
        CardId = cardId;
        Config = config;
        FailureStep = failureStep;
        FailureReason = failureReason;
        Warnings = warnings;
    }

    public bool Success { get; }

    public string CardId { get; }

    public DataConfig? Config { get; }

    public string FailureStep { get; }

    public string FailureReason { get; }

    public IReadOnlyList<string> Warnings { get; }

    public static CardGrantResult Ok(string cardId, DataConfig config, IReadOnlyList<string> warnings)
    {
        return new CardGrantResult(true, cardId, config, "", "", warnings);
    }

    public static CardGrantResult Fail(string cardId, DataConfig? config, string failureStep, string failureReason, IReadOnlyList<string>? warnings = null)
    {
        return new CardGrantResult(false, cardId, config, failureStep, failureReason, warnings ?? Array.Empty<string>());
    }
}

public static class CardApi
{
    public static int HandCardCount(ScriptExecutor? self)
    {
        return self?.HandCard?.Count(card => card?.dataConfig != null) ?? 0;
    }

    public static int ThrowAllHandCards(ScriptExecutor? self)
    {
        var count = HandCardCount(self);
        if (self == null || count <= 0)
        {
            return 0;
        }

        try
        {
            var method = self.GetType().GetMethod("ThrowCard", new[] { typeof(string), typeof(string) });
            if (method == null)
            {
                TerriasLog.Warn("ThrowAllHandCards failed: ScriptExecutor.ThrowCard unavailable");
                return 0;
            }

            method.Invoke(self, new object[] { count.ToString(), "Hand" });
            return count;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("ThrowAllHandCards failed: " + ex.Message);
            return 0;
        }
    }

    public static int BurnAllHandCards(ScriptExecutor? self)
    {
        return BurnHandCards(self, int.MaxValue);
    }

    public static bool SelectAndBurnHandCards(ScriptExecutor? self, int count)
    {
        var cards = HandCardConfigs(self);
        var burnCount = Math.Min(cards.Count, Math.Max(0, count));
        if (self == null || burnCount <= 0)
        {
            return false;
        }

        var caption = "\u9009\u62e9" + burnCount + "\u5f20\u5361\u724c\u711a\u6bc1";
        var opened = CardSelectionApi.SelectCardsFromCards(
            self,
            cards,
            burnCount,
            _ => true,
            selected => BurnSpecificHandCards(self, selected),
            caption,
            () => BurnHandCards(self, burnCount),
            new AuraCombatAi.Shared.CombatInteractionHint
            {
                OwnerModId = TerriasIds.ModId,
                Purpose = "burn-selected-hand-cards",
                Kind = AuraCombatAi.Shared.CombatPromptKind.BurnCards,
                Zone = AuraCombatAi.Shared.CombatPromptZone.Hand,
                Forced = true,
                PreferLowestValue = true
            });
        if (opened)
        {
            return true;
        }

        BurnHandCards(self, burnCount);
        return false;
    }

    public static int BurnHandCards(ScriptExecutor? self, int count)
    {
        if (self == null)
        {
            return 0;
        }

        var cards = HandCardConfigs(self)
            .Take(Math.Max(0, count))
            .ToList();
        return BurnSpecificHandCards(self, cards);
    }

    private static List<IDataConfig> HandCardConfigs(ScriptExecutor? self)
    {
        return (self?.HandCard ?? Enumerable.Empty<CardItem>())
            .Select(card => card?.dataConfig)
            .Where(card => card != null)
            .Cast<IDataConfig>()
            .ToList();
    }

    private static int BurnSpecificHandCards(ScriptExecutor? self, IReadOnlyList<IDataConfig> cards)
    {
        if (self == null || cards == null || cards.Count == 0)
        {
            return 0;
        }

        var method = self.GetType().GetMethod("BurnCardByData", new[] { typeof(IDataConfig) });
        if (method == null)
        {
            TerriasLog.Warn("BurnHandCards failed: ScriptExecutor.BurnCardByData unavailable");
            return 0;
        }

        var burned = 0;
        foreach (var card in cards)
        {
            try
            {
                method.Invoke(self, new object[] { card! });
                burned++;
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("BurnHandCards item failed: " + ex.Message);
            }
        }

        return burned;
    }

    public static bool AddCardToHand(ScriptExecutor self, string cardId, string runtimeTag = "")
    {
        var request = CardGrantRequest.ToHand(cardId);
        if (!string.IsNullOrWhiteSpace(runtimeTag))
        {
            request.WithRuntimeTags(runtimeTag.Split(','));
        }

        return GrantCardToHand(self, request).Success;
    }

    public static bool MarkForAdventureRemoval(IDataConfig? config)
    {
        if (config?.Vars == null)
        {
            return false;
        }

        DictionaryUtil.Set(config.Vars, "NeedRemove", "True");
        return true;
    }

    public static CardGrantResult GrantCardToHand(ScriptExecutor self, CardGrantRequest request)
    {
        var warnings = new List<string>();
        var cardId = request?.CardId ?? "";
        var resolved = ResolveCardId(cardId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return Fail(resolved, null, "resolve", "unknown cardId=" + cardId, warnings);
        }

        var cards = FightCardManager.Instance?.cardList;
        if (cards == null)
        {
            return Fail(resolved, null, "manager", "combat card manager unavailable", warnings);
        }

        var existingCards = new HashSet<DataConfig>(cards);
        try
        {
            self.SetStatus("Self");
            self.AddCardByData(resolved, request?.RuntimeTags ?? "");
        }
        catch (Exception ex)
        {
            return Fail(resolved, null, "create", ex.Message, warnings);
        }

        var added = cards.LastOrDefault(card => !existingCards.Contains(card)
            && string.Equals(CardConfigApi.Id(card), resolved, StringComparison.Ordinal));
        if (added == null)
        {
            return Fail(resolved, null, "locate", "created card not found", warnings);
        }

        if (NeedsWritableRuntimeConfig(request))
        {
            try
            {
                added = EnsureWritableRuntimeConfig(added, cards, request?.RuntimePresentation);
            }
            catch (Exception ex)
            {
                CleanupCreatedCard(cards, added);
                return Fail(resolved, added, "clone-runtime-config", ex.Message, warnings);
            }
        }

        foreach (var mutation in request?.Mutations ?? Array.Empty<CardGrantMutation>())
        {
            try
            {
                mutation.Apply(added);
            }
            catch (Exception ex)
            {
                var warning = "mutation " + mutation.Name + " failed: " + ex.Message;
                warnings.Add(warning);
                TerriasLog.Warn("AddCardToHand " + warning + ", cardId=" + resolved + SourceSuffix(request));
                if (request?.AbortOnMutationFailure == true)
                {
                    CleanupCreatedCard(cards, added);
                    return Fail(resolved, added, "mutate:" + mutation.Name, ex.Message, warnings);
                }
            }
        }

        try
        {
            if (!CombatCardViewPoolApi.TryMaterialize(self, added, "CardApi.GrantCardToHand" + SourceSuffix(request)))
            {
                self.GetCardFromDeck(added);
                if (CombatCardViewPoolCatalog.IsEligible(added))
                {
                    TerriasPerformanceCounters.Record("CombatCardViewPool.NativeFallback");
                }
            }
            CardGrantPostCommitQueue.Request(new CardGrantPostCommitRequest
            {
                Config = added,
                Source = "CardApi.GrantCardToHand" + SourceSuffix(request),
                RefreshTags = HasPostCommitTagWork(request),
                RefreshVisuals = CardVisualInterestIndex.MayAffect(added),
                DataUpdate = false
            });
            return CardGrantResult.Ok(resolved, added, warnings);
        }
        catch (Exception ex)
        {
            CleanupCreatedCard(cards, added);
            return Fail(resolved, added, "deliver", ex.Message, warnings);
        }
    }

    public static string ResolveCardId(string cardId)
    {
        var id = (cardId ?? "").Replace("*", "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        return AuraGameDataHostApi.ResolveId(DataType.Card, Candidates(id), id);
    }

    private static string[] Candidates(string id)
    {
        return TerriasContentIdCompatibility.LookupCandidates(
            id,
            "cursecard",
            "terrias",
            "loneer",
            "wuna",
            "columbina",
            "solar_memory");
    }

    private static bool NeedsWritableRuntimeConfig(CardGrantRequest? request)
    {
        return request?.RequiresWritableRuntimeConfig == true || request?.Mutations.Count > 0;
    }

    private static bool HasPostCommitTagWork(CardGrantRequest? request)
    {
        return request != null
            && (!string.IsNullOrWhiteSpace(request.RuntimeTags)
                || request.Mutations.Count > 0
                || request.RequiresWritableRuntimeConfig);
    }

    private static DataConfig EnsureWritableRuntimeConfig(
        DataConfig source,
        IList<DataConfig> cards,
        IReadOnlyDictionary<string, string>? runtimePresentation)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var writable = AuraGameDataHostApi.CloneWritable(source, runtimePresentation, runtimePresentation);
        var index = IndexOfReference(cards, source);
        if (index < 0)
        {
            throw new InvalidOperationException("created card no longer exists in combat card list");
        }

        cards[index] = writable;
        ReplaceCardTags(source, writable);
        return writable;
    }

    private static int IndexOfReference(IList<DataConfig> cards, DataConfig target)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    private static void ReplaceCardTags(DataConfig source, DataConfig replacement)
    {
        try
        {
            var tags = ReadCardTags();
            if (tags == null)
            {
                return;
            }

            if (tags.Contains(source))
            {
                var existingTags = tags[source];
                tags.Remove(source);
                tags[replacement] = existingTags;
                return;
            }

            if (!tags.Contains(replacement))
            {
                tags[replacement] = new HashSet<string>();
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("AddCardToHand card tag replacement skipped: " + ex.Message);
        }
    }

    private static void CleanupCreatedCard(IList<DataConfig> cards, DataConfig? card)
    {
        if (card == null)
        {
            return;
        }

        try
        {
            for (var i = cards.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(cards[i], card))
                {
                    cards.RemoveAt(i);
                    break;
                }
            }

            ReadCardTags()?.Remove(card);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("AddCardToHand cleanup skipped: " + ex.Message);
        }
    }

    private static IDictionary? ReadCardTags()
    {
        try
        {
            var manager = FightCardManager.Instance;
            if (manager == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = manager.GetType();
            var property = type.GetProperty("CardTags", flags);
            if (property?.GetValue(manager) is IDictionary propertyValue)
            {
                return propertyValue;
            }

            var field = type.GetField("CardTags", flags);
            return field?.GetValue(manager) as IDictionary;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("AddCardToHand CardTags lookup skipped: " + ex.Message);
            return null;
        }
    }

    private static CardGrantResult Fail(string cardId, DataConfig? config, string step, string reason, IReadOnlyList<string> warnings)
    {
        TerriasLog.Warn("AddCardToHand failed: step=" + step + ", cardId=" + cardId + ", error=" + reason);
        return CardGrantResult.Fail(cardId, config, step, reason, warnings);
    }

    private static string SourceSuffix(CardGrantRequest? request)
    {
        var source = request?.Source ?? "";
        return string.IsNullOrWhiteSpace(source) ? "" : ", source=" + source;
    }
}
