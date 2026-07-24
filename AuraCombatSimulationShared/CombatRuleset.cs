using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AuraCombatSimulation.Shared;

public sealed class CombatRuleset
{
    private readonly Dictionary<string, CombatCardDefinition> cards;
    private readonly Dictionary<string, CombatEnemyDefinition> enemies;
    private readonly Dictionary<string, CombatStatusDefinition> statuses;

    internal CombatRuleset(
        string version,
        string rulesetHash,
        Dictionary<string, CombatCardDefinition> cards,
        Dictionary<string, CombatEnemyDefinition> enemies,
        Dictionary<string, CombatStatusDefinition> statuses)
    {
        Version = version;
        RulesetHash = rulesetHash;
        this.cards = cards;
        this.enemies = enemies;
        this.statuses = statuses;
    }

    public static CombatRuleset Empty { get; } = new(
        "empty",
        "0000000000000000",
        new Dictionary<string, CombatCardDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, CombatEnemyDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, CombatStatusDefinition>(StringComparer.OrdinalIgnoreCase));

    public string Version { get; }

    public string RulesetHash { get; }

    public int CardCount => cards.Count;

    public int EnemyCount => enemies.Count;

    public int StatusCount => statuses.Count;

    public bool TryGetCard(string cardId, out CombatCardDefinition definition)
    {
        if (cards.TryGetValue(cardId ?? "", out var stored))
        {
            definition = stored.Clone();
            return true;
        }
        definition = new CombatCardDefinition();
        return false;
    }

    public bool TryGetEnemy(string enemyId, out CombatEnemyDefinition definition)
    {
        if (enemies.TryGetValue(enemyId ?? "", out var stored))
        {
            definition = stored.Clone();
            return true;
        }
        definition = new CombatEnemyDefinition();
        return false;
    }

    public bool TryGetStatus(string statusId, out CombatStatusDefinition definition)
    {
        if (statuses.TryGetValue(statusId ?? "", out var stored))
        {
            definition = stored.Clone();
            return true;
        }
        definition = new CombatStatusDefinition();
        return false;
    }

    internal bool TryGetCardCore(string cardId, out CombatCardDefinition definition)
    {
        return cards.TryGetValue(cardId ?? "", out definition!);
    }

    internal bool TryGetEnemyCore(string enemyId, out CombatEnemyDefinition definition)
    {
        return enemies.TryGetValue(enemyId ?? "", out definition!);
    }

    internal bool TryGetStatusCore(string statusId, out CombatStatusDefinition definition)
    {
        return statuses.TryGetValue(statusId ?? "", out definition!);
    }

    public IReadOnlyList<CombatCardDefinition> SnapshotCards()
    {
        return cards.Values.Select(card => card.Clone()).ToList();
    }

    public IReadOnlyList<CombatEnemyDefinition> SnapshotEnemies()
    {
        return enemies.Values.Select(enemy => enemy.Clone()).ToList();
    }

    public IReadOnlyList<CombatStatusDefinition> SnapshotStatuses()
    {
        return statuses.Values.Select(status => status.Clone()).ToList();
    }
}

public sealed class CombatRulesetBuildResult
{
    public bool Success { get; set; }

    public CombatRuleset Ruleset { get; set; } = CombatRuleset.Empty;

    public List<string> Errors { get; set; } = new();
}

public sealed class CombatRulesetBuilder
{
    private readonly Dictionary<string, CombatCardDefinition> cards =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CombatEnemyDefinition> enemies =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CombatStatusDefinition> statuses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> errors = new();
    private bool frozen;

    public CombatRulesetBuilder(string version)
    {
        Version = string.IsNullOrWhiteSpace(version) ? "1" : version.Trim();
    }

    public string Version { get; }

    public CombatRulesetBuilder RegisterCard(CombatCardDefinition? definition)
    {
        EnsureMutable();
        if (!ValidateIdentity(definition?.OwnerModId, definition?.CardId, "card", out var key))
        {
            return this;
        }
        if (definition!.Cost < 0 || definition.Effects == null)
        {
            errors.Add("card " + key + " has invalid cost or effects");
            return this;
        }
        if (cards.ContainsKey(key))
        {
            errors.Add("duplicate card definition: " + key);
        }
        else
        {
            cards[key] = definition.Clone();
        }
        return this;
    }

    public CombatRulesetBuilder RegisterEnemy(CombatEnemyDefinition? definition)
    {
        EnsureMutable();
        if (!ValidateIdentity(definition?.OwnerModId, definition?.EnemyId, "enemy", out var key))
        {
            return this;
        }
        if (definition!.MaxHp <= 0 || definition.Intents == null || definition.Intents.Count == 0)
        {
            errors.Add("enemy " + key + " has invalid hp or no intents");
            return this;
        }
        if (definition.Intents.Any(intent =>
                string.IsNullOrWhiteSpace(intent.IntentId)
                || intent.Weight < 0
                || intent.Effects == null))
        {
            errors.Add("enemy " + key + " has an invalid intent");
            return this;
        }
        if (definition.Intents
            .GroupBy(intent => intent.IntentId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            errors.Add("enemy " + key + " has duplicate intent ids");
            return this;
        }
        if (enemies.ContainsKey(key))
        {
            errors.Add("duplicate enemy definition: " + key);
        }
        else
        {
            enemies[key] = definition.Clone();
        }
        return this;
    }

    public CombatRulesetBuilder RegisterStatus(CombatStatusDefinition? definition)
    {
        EnsureMutable();
        if (!ValidateIdentity(definition?.OwnerModId, definition?.StatusId, "status", out var key))
        {
            return this;
        }
        if (definition!.Triggers == null
            || definition.Triggers.Any(trigger =>
                string.IsNullOrWhiteSpace(trigger.TriggerId)
                || trigger.Effects == null))
        {
            errors.Add("status " + key + " has an invalid trigger");
            return this;
        }
        if (definition.Triggers
            .GroupBy(trigger => trigger.TriggerId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            errors.Add("status " + key + " has duplicate trigger ids");
            return this;
        }
        if (statuses.ContainsKey(key))
        {
            errors.Add("duplicate status definition: " + key);
        }
        else
        {
            statuses[key] = definition.Clone();
        }
        return this;
    }

    public CombatRulesetBuildResult Freeze()
    {
        EnsureMutable();
        frozen = true;
        ValidateReferences();
        if (errors.Count > 0)
        {
            return new CombatRulesetBuildResult
            {
                Errors = new List<string>(errors)
            };
        }

        var frozenCards = cards.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        var frozenEnemies = enemies.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        var frozenStatuses = statuses.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        var hash = CombatRulesetHasher.Hash(Version, frozenCards, frozenEnemies, frozenStatuses);
        return new CombatRulesetBuildResult
        {
            Success = true,
            Ruleset = new CombatRuleset(Version, hash, frozenCards, frozenEnemies, frozenStatuses)
        };
    }

    private void ValidateReferences()
    {
        foreach (var card in cards.Values)
        {
            ValidateEffects("card " + card.CardId, card.Effects);
        }
        foreach (var enemy in enemies.Values)
        {
            foreach (var intent in enemy.Intents)
            {
                ValidateEffects("enemy intent " + enemy.EnemyId + "/" + intent.IntentId, intent.Effects);
            }
        }
        foreach (var status in statuses.Values)
        {
            foreach (var trigger in status.Triggers)
            {
                ValidateEffects("status trigger " + status.StatusId + "/" + trigger.TriggerId, trigger.Effects);
            }
        }
    }

    private void ValidateEffects(string source, IEnumerable<CombatSimulationEffectDefinition> effects)
    {
        foreach (var effect in effects)
        {
            if (effect == null
                || double.IsNaN(effect.Probability)
                || double.IsInfinity(effect.Probability)
                || effect.Probability < 0d
                || effect.Probability > 1d
                || (effect.Amount < 0
                    && effect.Kind != CombatSimulationEffectKind.ChangeCardCost
                    && effect.Kind != CombatSimulationEffectKind.ModifyVariable))
            {
                errors.Add(source + " has an invalid effect");
                continue;
            }
            if ((effect.Kind == CombatSimulationEffectKind.AddStatus
                 || effect.Kind == CombatSimulationEffectKind.RemoveStatus)
                && !statuses.ContainsKey(effect.DefinitionId ?? ""))
            {
                errors.Add(source + " references unknown status: " + effect.DefinitionId);
            }
            if (effect.Kind == CombatSimulationEffectKind.CreateCard
                && !cards.ContainsKey(effect.DefinitionId ?? ""))
            {
                errors.Add(source + " references unknown card: " + effect.DefinitionId);
            }
            if (effect.Kind == CombatSimulationEffectKind.SummonEnemy
                && !enemies.ContainsKey(effect.DefinitionId ?? ""))
            {
                errors.Add(source + " references unknown enemy: " + effect.DefinitionId);
            }
        }
    }

    private bool ValidateIdentity(
        string? ownerModId,
        string? definitionId,
        string kind,
        out string key)
    {
        key = (definitionId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(ownerModId) || key.Length == 0)
        {
            errors.Add(kind + " definition requires ownerModId and id");
            return false;
        }
        return true;
    }

    private void EnsureMutable()
    {
        if (frozen)
        {
            throw new InvalidOperationException("Combat ruleset builder is already frozen.");
        }
    }
}

internal static class CombatRulesetHasher
{
    public static string Hash(
        string version,
        IReadOnlyDictionary<string, CombatCardDefinition> cards,
        IReadOnlyDictionary<string, CombatEnemyDefinition> enemies,
        IReadOnlyDictionary<string, CombatStatusDefinition> statuses)
    {
        var builder = new StringBuilder();
        builder.Append("v=").Append(version).Append('\n');
        foreach (var card in cards.Values.OrderBy(item => item.CardId, StringComparer.Ordinal))
        {
            builder.Append("c|").Append(card.OwnerModId).Append('|').Append(card.CardId)
                .Append('|').Append(card.Cost).Append('|').Append(card.Exhaust)
                .Append('|').Append(card.RequiresEnemyTarget).Append('|').Append(card.Fidelity).Append('\n');
            AppendEffects(builder, card.Effects);
        }
        foreach (var enemy in enemies.Values.OrderBy(item => item.EnemyId, StringComparer.Ordinal))
        {
            builder.Append("e|").Append(enemy.OwnerModId).Append('|').Append(enemy.EnemyId)
                .Append('|').Append(enemy.MaxHp).Append('|').Append(enemy.InitialBlock)
                .Append('|').Append(enemy.Fidelity).Append('\n');
            foreach (var intent in enemy.Intents.OrderBy(item => item.IntentId, StringComparer.Ordinal))
            {
                builder.Append("i|").Append(intent.IntentId).Append('|').Append(intent.Weight)
                    .Append('|').Append(intent.Priority).Append('|').Append(intent.CooldownTurns)
                    .Append('|').Append(intent.MinimumTurn).Append('|').Append(intent.MaximumTurn)
                    .Append('|').Append(F(intent.MinimumHpRatio)).Append('|').Append(F(intent.MaximumHpRatio))
                    .Append('|').Append(intent.PreventConsecutiveUse).Append('\n');
                AppendEffects(builder, intent.Effects);
            }
        }
        foreach (var status in statuses.Values.OrderBy(item => item.StatusId, StringComparer.Ordinal))
        {
            builder.Append("s|").Append(status.OwnerModId).Append('|').Append(status.StatusId)
                .Append('|').Append(status.Fidelity).Append('|').Append(status.DecayAtRoundEnd)
                .Append('|').Append(status.ReducePerTurn).Append('|').Append(status.ReducePerUse)
                .Append('|').Append(status.ReducePerAttacked).Append('|').Append(status.CanRemainAtZero)
                .Append('\n');
            foreach (var modifier in status.DynamicModifiersPerStack
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.Append("m|").Append(modifier.Key).Append('|').Append(F(modifier.Value))
                    .Append('\n');
            }
            foreach (var trigger in status.Triggers
                         .OrderBy(item => item.Priority)
                         .ThenBy(item => item.TriggerId, StringComparer.Ordinal))
            {
                builder.Append("t|").Append(trigger.TriggerId).Append('|').Append(trigger.EventKind)
                    .Append('|').Append(trigger.Priority).Append('|').Append(trigger.ConsumeStacks)
                    .Append('\n');
                AppendEffects(builder, trigger.Effects);
            }
        }

        var hash = 1469598103934665603UL;
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static void AppendEffects(
        StringBuilder builder,
        IEnumerable<CombatSimulationEffectDefinition> effects)
    {
        foreach (var effect in effects)
        {
            builder.Append("x|").Append(effect.Kind).Append('|').Append(effect.Target)
                .Append('|').Append(effect.Amount).Append('|').Append(F(effect.Probability))
                .Append('|').Append(effect.DefinitionId).Append('|').Append(effect.Duration)
                .Append('|').Append(effect.ScaleWithStatusStacks)
                .Append('|').Append(Expression(effect.AmountExpression)).Append('\n');
        }
    }

    private static string F(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Expression(CombatSimulationValueExpression? expression)
    {
        if (expression == null)
        {
            return "";
        }
        return expression.Operation + "(" + expression.Key + "|" + F(expression.Constant)
               + "|" + string.Join(",", expression.Arguments.Select(Expression)) + ")";
    }
}
