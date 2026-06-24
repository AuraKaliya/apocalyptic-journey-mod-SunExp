using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public sealed class CardGrantRequest
{
    private readonly List<CardGrantMutation> mutations = new();

    public CardGrantRequest(string cardId)
    {
        CardId = cardId ?? "";
    }

    public string CardId { get; }

    public string RuntimeTags { get; private set; } = "";

    public string Source { get; private set; } = "";

    public bool AbortOnMutationFailure { get; private set; }

    public IReadOnlyList<CardGrantMutation> Mutations => mutations;

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
        return this;
    }

    public CardGrantRequest Configure(string name, Action<DataConfig> apply)
    {
        mutations.Add(new CardGrantMutation(name, apply));
        return this;
    }

    public CardGrantRequest Configure(CardGrantMutation mutation)
    {
        if (mutation.Apply != null)
        {
            mutations.Add(mutation);
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
    public static bool AddCardToHand(ScriptExecutor self, string cardId, string runtimeTag = "")
    {
        var request = CardGrantRequest.ToHand(cardId);
        if (!string.IsNullOrWhiteSpace(runtimeTag))
        {
            request.WithRuntimeTags(runtimeTag.Split(','));
        }

        return GrantCardToHand(self, request).Success;
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
                SunExpLog.Warn("AddCardToHand " + warning + ", cardId=" + resolved + SourceSuffix(request));
                if (request?.AbortOnMutationFailure == true)
                {
                    return Fail(resolved, added, "mutate:" + mutation.Name, ex.Message, warnings);
                }
            }
        }

        try
        {
            self.GetCardFromDeck(added);
            return CardGrantResult.Ok(resolved, added, warnings);
        }
        catch (Exception ex)
        {
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

        foreach (var candidate in Candidates(id))
        {
            try
            {
                if (Singleton<GameConfigManager>.Instance.GetOne(DataType.Card, candidate) != null)
                {
                    return candidate;
                }
            }
            catch
            {
                // Keep trying fallbacks.
            }
        }

        return id;
    }

    private static string[] Candidates(string id)
    {
        if (id.StartsWith("SunExp_", StringComparison.Ordinal))
        {
            return new[] { id };
        }

        return new[]
        {
            id,
            "SunExp_sunexp_" + id,
            "SunExp_loneer_" + id,
            "SunExp_wuna_" + id
        };
    }

    private static CardGrantResult Fail(string cardId, DataConfig? config, string step, string reason, IReadOnlyList<string> warnings)
    {
        SunExpLog.Warn("AddCardToHand failed: step=" + step + ", cardId=" + cardId + ", error=" + reason);
        return CardGrantResult.Fail(cardId, config, step, reason, warnings);
    }

    private static string SourceSuffix(CardGrantRequest? request)
    {
        var source = request?.Source ?? "";
        return string.IsNullOrWhiteSpace(source) ? "" : ", source=" + source;
    }
}
