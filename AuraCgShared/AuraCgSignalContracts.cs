using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraCg.Shared;

public static class AuraCgSubjectTypes
{
    public const string Role = "role";
    public const string Card = "card";
    public const string Event = "event";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (string.Equals(normalized, Card, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "cardUse", StringComparison.OrdinalIgnoreCase))
        {
            return Card;
        }

        if (string.Equals(normalized, Event, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "battle", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "adventure", StringComparison.OrdinalIgnoreCase))
        {
            return Event;
        }

        return Role;
    }
}

public static class AuraCgSignals
{
    public const string RoleFeastCompleted = "aura.role.feast.completed";
    public const string RoleSkillCommitted = "aura.role.skill.committed";
    public const string RoleLowHealthEntered = "aura.role.low-health.entered";

    [Obsolete("Use RoleLowHealthEntered. Low-health presentation now follows the native Dying decision.")]
    public const string RoleLowHealthCrossedDown = RoleLowHealthEntered;
    public const string CardUsePresentationCommitted = "aura.card.use.presentation-committed";
    public const string BattleOpening = "aura.battle.opening";
    public const string BattleVictory = "aura.battle.outcome.victory";
    public const string BattleDefeat = "aura.battle.outcome.defeat";
    public const string AdventureSettlementEntering = "aura.adventure.settlement.entering";

    public static string FromLegacyKind(string? kind)
    {
        var normalized = (kind ?? "").Trim();
        if (string.Equals(normalized, "card", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "cardUse", StringComparison.OrdinalIgnoreCase))
        {
            return CardUsePresentationCommitted;
        }

        if (string.Equals(normalized, "feast", StringComparison.OrdinalIgnoreCase))
        {
            return RoleFeastCompleted;
        }

        return RoleSkillCommitted;
    }

    public static string SubjectType(string? signalId)
    {
        var signal = (signalId ?? "").Trim();
        if (signal.StartsWith("aura.card.", StringComparison.OrdinalIgnoreCase))
        {
            return AuraCgSubjectTypes.Card;
        }

        if (signal.StartsWith("aura.battle.", StringComparison.OrdinalIgnoreCase)
            || signal.StartsWith("aura.adventure.", StringComparison.OrdinalIgnoreCase))
        {
            return AuraCgSubjectTypes.Event;
        }

        return AuraCgSubjectTypes.Role;
    }
}

[Serializable]
public sealed class AuraCgSignalContext
{
    public string SignalId { get; set; } = "";

    public string SubjectType { get; set; } = "";

    public string SubjectId { get; set; } = "";

    public long ActionSequence { get; set; }

    public string EventToken { get; set; } = "";

    public string Action { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string SkillId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string BattleId { get; set; } = "";

    public string ModeId { get; set; } = "";

    public string Outcome { get; set; } = "";

    public float CreatedAt { get; set; }

    public AuraCgScenePlan? ScenePlan { get; set; }

    [JsonIgnore]
    public AuraCgSceneSourceSnapshot? SceneSource { get; set; }

    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public Action<SkillCgRequest>? ConfigureResolvedRequest { get; set; }

    public void Normalize()
    {
        SignalId = (SignalId ?? "").Trim().ToLowerInvariant();
        SubjectType = AuraCgSubjectTypes.Normalize(string.IsNullOrWhiteSpace(SubjectType)
            ? AuraCgSignals.SubjectType(SignalId)
            : SubjectType);
        RoleId = (RoleId ?? "").Trim();
        CardId = (CardId ?? "").Trim();
        SkillId = (SkillId ?? "").Trim();
        OwnerInstanceId = (OwnerInstanceId ?? "").Trim();
        BattleId = (BattleId ?? "").Trim();
        ModeId = (ModeId ?? "").Trim();
        Outcome = (Outcome ?? "").Trim();
        Action = (Action ?? "").Trim();
        SubjectId = ResolveSubjectId();
        EventToken = string.IsNullOrWhiteSpace(EventToken)
            ? OwnerInstanceId + ":" + SignalId + ":" + ActionSequence.ToString()
            : EventToken.Trim();
        Facts = CleanFacts(Facts);
        Metrics = CleanMetrics(Metrics);
        AddBuiltInFacts();
        if (ScenePlan != null)
        {
            ScenePlan.SignalId = SignalId;
            ScenePlan.EventToken = EventToken;
            ScenePlan.Normalize();
        }
    }

    public SkillCgTriggerContext ToLegacyTrigger()
    {
        Normalize();
        return new SkillCgTriggerContext
        {
            SignalId = SignalId,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            TriggerKind = string.Equals(SubjectType, AuraCgSubjectTypes.Card, StringComparison.Ordinal)
                ? "card"
                : string.Equals(SignalId, AuraCgSignals.RoleFeastCompleted, StringComparison.Ordinal)
                    ? "feast"
                    : string.Equals(SubjectType, AuraCgSubjectTypes.Event, StringComparison.Ordinal)
                        ? "event"
                        : "skill",
            ActionSequence = ActionSequence,
            EventToken = EventToken,
            Action = Action,
            CardId = CardId,
            SkillId = SkillId,
            OwnerInstanceId = OwnerInstanceId,
            OwnerRoleId = RoleId,
            BattleId = BattleId,
            ModeId = ModeId,
            Outcome = Outcome,
            CreatedAt = CreatedAt,
            ScenePlan = ScenePlan,
            Facts = new Dictionary<string, string>(Facts, StringComparer.OrdinalIgnoreCase),
            Metrics = new Dictionary<string, double>(Metrics, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static AuraCgSignalContext FromLegacy(SkillCgTriggerContext? context)
    {
        context ??= new SkillCgTriggerContext();
        var signalId = string.IsNullOrWhiteSpace(context.SignalId)
            ? AuraCgSignals.FromLegacyKind(context.TriggerKind)
            : context.SignalId;
        var subjectType = string.IsNullOrWhiteSpace(context.SubjectType)
            ? AuraCgSignals.SubjectType(signalId)
            : context.SubjectType;
        var result = new AuraCgSignalContext
        {
            SignalId = signalId,
            SubjectType = subjectType,
            SubjectId = context.SubjectId,
            ActionSequence = context.ActionSequence,
            EventToken = context.EventToken,
            Action = context.Action,
            RoleId = context.OwnerRoleId,
            CardId = context.CardId,
            SkillId = context.SkillId,
            OwnerInstanceId = context.OwnerInstanceId,
            BattleId = context.BattleId,
            ModeId = context.ModeId,
            Outcome = context.Outcome,
            CreatedAt = context.CreatedAt,
            ScenePlan = context.ScenePlan,
            Facts = new Dictionary<string, string>(
                context.Facts ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
            Metrics = new Dictionary<string, double>(
                context.Metrics ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase)
        };
        result.Normalize();
        return result;
    }

    private void AddBuiltInFacts()
    {
        AddFact("roleId", RoleId);
        AddFact("cardId", CardId);
        AddFact("skillId", SkillId);
        AddFact("battleId", BattleId);
        AddFact("modeId", ModeId);
        AddFact("outcome", Outcome);
    }

    private void AddFact(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !Facts.ContainsKey(key))
        {
            Facts[key] = value.Trim();
        }
    }

    private string ResolveSubjectId()
    {
        var explicitId = (SubjectId ?? "").Trim();
        if (explicitId.Length > 0)
        {
            return explicitId;
        }

        if (string.Equals(SubjectType, AuraCgSubjectTypes.Card, StringComparison.Ordinal))
        {
            return CardId;
        }

        if (string.Equals(SubjectType, AuraCgSubjectTypes.Event, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(BattleId)) return BattleId;
            if (!string.IsNullOrWhiteSpace(ModeId)) return ModeId;
            return "*";
        }

        return RoleId;
    }

    private static Dictionary<string, string> CleanFacts(IDictionary<string, string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, string>())
        {
            var key = (pair.Key ?? "").Trim();
            if (key.Length > 0)
            {
                result[key] = (pair.Value ?? "").Trim();
            }
        }

        return result;
    }

    private static Dictionary<string, double> CleanMetrics(IDictionary<string, double>? values)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, double>())
        {
            var key = (pair.Key ?? "").Trim();
            if (key.Length > 0 && !double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value))
            {
                result[key] = pair.Value;
            }
        }

        return result;
    }
}

public sealed class AuraCgMatchSpec
{
    public Dictionary<string, List<string>> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> MinimumMetrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> MaximumMetrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        Facts = NormalizeFacts(Facts);
        MinimumMetrics = NormalizeMetrics(MinimumMetrics);
        MaximumMetrics = NormalizeMetrics(MaximumMetrics);
    }

    public bool Matches(AuraCgSignalContext context)
    {
        context ??= new AuraCgSignalContext();
        context.Normalize();
        Normalize();
        foreach (var pair in Facts)
        {
            if (!context.Facts.TryGetValue(pair.Key, out var actual)
                || !pair.Value.Any(expected => string.Equals(expected, "*", StringComparison.Ordinal)
                                               || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        foreach (var pair in MinimumMetrics)
        {
            if (!context.Metrics.TryGetValue(pair.Key, out var actual) || actual < pair.Value)
            {
                return false;
            }
        }

        foreach (var pair in MaximumMetrics)
        {
            if (!context.Metrics.TryGetValue(pair.Key, out var actual) || actual > pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, List<string>> NormalizeFacts(
        IDictionary<string, List<string>>? values)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, List<string>>())
        {
            var key = (pair.Key ?? "").Trim();
            var accepted = (pair.Value ?? new List<string>())
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (key.Length > 0 && accepted.Count > 0)
            {
                result[key] = accepted;
            }
        }

        return result;
    }

    private static Dictionary<string, double> NormalizeMetrics(IDictionary<string, double>? values)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, double>())
        {
            var key = (pair.Key ?? "").Trim();
            if (key.Length > 0 && !double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value))
            {
                result[key] = pair.Value;
            }
        }

        return result;
    }
}
