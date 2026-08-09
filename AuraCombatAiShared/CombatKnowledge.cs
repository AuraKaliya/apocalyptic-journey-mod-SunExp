using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public enum CombatKnowledgeFidelity
{
    Authoritative,
    Derived,
    Approximate,
    Unsupported
}

public sealed class CombatKnowledgePackage
{
    public int SchemaVersion { get; set; } = 1;

    public string OwnerId { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string GameBuild { get; set; } = "";

    public string SourceHash { get; set; } = "";

    public DateTime GeneratedAtUtc { get; set; }

    public CombatKnowledgeInventory Inventory { get; set; } = new();

    public List<CombatKnowledgeActionDefinition> Actions { get; set; } = new();

    public List<CombatKnowledgeStatusDefinition> Statuses { get; set; } = new();

    public List<CombatKnowledgeEnemyDefinition> Enemies { get; set; } = new();

    public List<CombatKnowledgeEncounterDefinition> Encounters { get; set; } = new();
}

public sealed class CombatKnowledgeInventory
{
    public int DiscoveredActions { get; set; }

    public int DiscoveredStatuses { get; set; }

    public int DiscoveredEnemies { get; set; }

    public int DiscoveredEncounters { get; set; }

    public int AuthoritativeActions { get; set; }

    public int AuthoritativeStatuses { get; set; }

    public int AuthoritativeEnemies { get; set; }

    public int UnsupportedScripts { get; set; }
}

public sealed class CombatKnowledgeActionDefinition
{
    public string SourceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatKnowledgeFidelity Fidelity { get; set; } = CombatKnowledgeFidelity.Unsupported;

    public double Confidence { get; set; } = 1d;

    public int BaseCost { get; set; }

    public CombatActionSemantics Semantics { get; set; } = new();

    public List<string> Roles { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, string> TableFields { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> RequiredRules { get; set; } = new();

    public List<CombatKnowledgeOperation> Operations { get; set; } = new();

    public string Provenance { get; set; } = "";
}

public sealed class CombatKnowledgeStatusDefinition
{
    public string StatusId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatKnowledgeFidelity Fidelity { get; set; } = CombatKnowledgeFidelity.Unsupported;

    public int UpperBound { get; set; }

    public int ReducePerTurn { get; set; }

    public int ReducePerUse { get; set; }

    public int ReducePerAttacked { get; set; }

    public bool CanRemainAtZero { get; set; }

    public Dictionary<string, double> DynamicModifiersPerStack { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Triggers { get; set; } = new();

    public List<CombatKnowledgeOperation> Operations { get; set; } = new();

    public string Provenance { get; set; } = "";

    public Dictionary<string, string> TableFields { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CombatKnowledgeOperation
{
    public string Stage { get; set; } = "";

    public string Api { get; set; } = "";

    public List<string> Arguments { get; set; } = new();

    public CombatKnowledgeFidelity Fidelity { get; set; } = CombatKnowledgeFidelity.Unsupported;

    public string SourceLocation { get; set; } = "";
}

public sealed class CombatKnowledgeEnemyDefinition
{
    public string EnemyId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public CombatKnowledgeFidelity Fidelity { get; set; } = CombatKnowledgeFidelity.Unsupported;

    public int ActionCount { get; set; } = 1;

    public int MaxHp { get; set; }

    public List<string> ActionIds { get; set; } = new();

    public Dictionary<string, double> Features { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TableFields { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string Provenance { get; set; } = "";
}

public sealed class CombatKnowledgeEncounterDefinition
{
    public string EncounterId { get; set; } = "";

    public CombatKnowledgeFidelity Fidelity { get; set; } = CombatKnowledgeFidelity.Unsupported;

    public List<string> EnemyIds { get; set; } = new();

    public string Provenance { get; set; } = "";
}

public sealed class CombatKnowledgeCoverageReport
{
    public string GameBuild { get; set; } = "";

    public int RequiredDefinitionCount { get; set; }

    public int AuthoritativeDefinitionCount { get; set; }

    public int DerivedDefinitionCount { get; set; }

    public int ApproximateDefinitionCount { get; set; }

    public int UnsupportedDefinitionCount { get; set; }

    public List<string> UnknownDefinitions { get; set; } = new();

    public List<string> NonAuthoritativeDefinitions { get; set; } = new();

    public double AuthoritativeCoverage => RequiredDefinitionCount <= 0
        ? 1d
        : (double)AuthoritativeDefinitionCount / RequiredDefinitionCount;

    public bool IsAuthoritative =>
        RequiredDefinitionCount > 0
        && AuthoritativeDefinitionCount == RequiredDefinitionCount
        && UnknownDefinitions.Count == 0;

    public string Summary =>
        "authoritative=" + AuthoritativeDefinitionCount + "/" + RequiredDefinitionCount
        + ", derived=" + DerivedDefinitionCount
        + ", approximate=" + ApproximateDefinitionCount
        + ", unsupported=" + UnsupportedDefinitionCount
        + ", unknown=" + UnknownDefinitions.Count;
}

public static class CombatKnowledgeRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CombatKnowledgePackage> Packages =
        new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable RegisterPackage(
        CombatKnowledgePackage package,
        out IReadOnlyList<string> errors)
    {
        var validation = Validate(package);
        errors = validation;
        if (validation.Count > 0)
        {
            return EmptyRegistration.Instance;
        }

        var key = Key(package.OwnerId, package.PackageId);
        EnrichCrossDefinitionSemantics(package);
        lock (Gate)
        {
            Packages[key] = package;
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                Packages.Remove(key);
            }
        });
    }

    public static IReadOnlyList<CombatKnowledgePackage> SnapshotPackages()
    {
        lock (Gate)
        {
            return Packages.Values.ToList();
        }
    }

    public static bool TryDescribeAction(
        CombatActionObservation action,
        out CombatActionSemantics semantics,
        out CombatKnowledgeFidelity fidelity,
        out string source)
    {
        var definitions = SnapshotPackages()
            .SelectMany(package => package.Actions.Select(item => new { package, item }))
            .Where(pair => string.Equals(
                pair.item.SourceId,
                action.SourceId,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.item.Fidelity)
            .ThenByDescending(pair => pair.item.Confidence)
            .ToList();
        if (definitions.Count == 0)
        {
            semantics = new CombatActionSemantics();
            fidelity = CombatKnowledgeFidelity.Unsupported;
            source = "";
            return false;
        }

        var selected = definitions[0];
        semantics = CloneSemantics(selected.item.Semantics);
        fidelity = selected.item.Fidelity;
        source = selected.package.OwnerId + ":" + selected.package.PackageId;
        return fidelity != CombatKnowledgeFidelity.Unsupported;
    }

    public static CombatKnowledgeCoverageReport EvaluateCoverage(CombatStateObservation state)
    {
        var report = new CombatKnowledgeCoverageReport();
        var packages = SnapshotPackages();
        report.GameBuild = string.Join(
            ",",
            packages.Select(package => package.GameBuild)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in state.Actions.Where(item => item.Kind != CombatActionKind.EndTurn))
        {
            required.Add("action:" + action.SourceId);
        }
        foreach (var cardId in state.DeckCardIds)
        {
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                required.Add("action:" + cardId);
            }
        }
        AddUnitRequirements(state.Player, required);
        foreach (var unit in state.Friendlies.Concat(state.Enemies))
        {
            AddUnitRequirements(unit, required);
        }

        foreach (var id in required.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var fidelity = ResolveFidelity(id, packages);
            report.RequiredDefinitionCount++;
            switch (fidelity)
            {
                case CombatKnowledgeFidelity.Authoritative:
                    report.AuthoritativeDefinitionCount++;
                    break;
                case CombatKnowledgeFidelity.Derived:
                    report.DerivedDefinitionCount++;
                    report.NonAuthoritativeDefinitions.Add(id);
                    break;
                case CombatKnowledgeFidelity.Approximate:
                    report.ApproximateDefinitionCount++;
                    report.NonAuthoritativeDefinitions.Add(id);
                    break;
                case CombatKnowledgeFidelity.Unsupported:
                    report.UnsupportedDefinitionCount++;
                    report.NonAuthoritativeDefinitions.Add(id);
                    break;
                default:
                    report.UnknownDefinitions.Add(id);
                    break;
            }
        }
        return report;
    }

    private static CombatKnowledgeFidelity? ResolveFidelity(
        string requirement,
        IReadOnlyList<CombatKnowledgePackage> packages)
    {
        var separator = requirement.IndexOf(':');
        var kind = separator < 0 ? "" : requirement.Substring(0, separator);
        var id = separator < 0 ? requirement : requirement.Substring(separator + 1);
        var values = new List<CombatKnowledgeFidelity>();
        foreach (var package in packages)
        {
            if (kind == "action")
            {
                values.AddRange(package.Actions
                    .Where(item => string.Equals(item.SourceId, id, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Fidelity));
            }
            else if (kind == "status")
            {
                values.AddRange(package.Statuses
                    .Where(item => string.Equals(item.StatusId, id, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Fidelity));
            }
            else if (kind == "enemy")
            {
                values.AddRange(package.Enemies
                    .Where(item => string.Equals(item.EnemyId, id, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Fidelity));
            }
        }
        return values.Count == 0 ? null : values.Min();
    }

    private static void AddUnitRequirements(
        CombatUnitObservation unit,
        ISet<string> required)
    {
        if (unit.Kind == CombatTargetKind.Enemy
            && !string.IsNullOrWhiteSpace(unit.DefinitionId))
        {
            required.Add("enemy:" + unit.DefinitionId);
        }
        foreach (var status in unit.Statuses)
        {
            if (!string.IsNullOrWhiteSpace(status.StatusId))
            {
                required.Add("status:" + status.StatusId);
            }
        }
    }

    private static List<string> Validate(CombatKnowledgePackage? package)
    {
        var errors = new List<string>();
        if (package == null)
        {
            errors.Add("knowledge package is null");
            return errors;
        }
        if (package.SchemaVersion != 1)
        {
            errors.Add("unsupported combat knowledge schema: " + package.SchemaVersion);
        }
        if (string.IsNullOrWhiteSpace(package.OwnerId)
            || string.IsNullOrWhiteSpace(package.PackageId)
            || string.IsNullOrWhiteSpace(package.GameBuild)
            || string.IsNullOrWhiteSpace(package.SourceHash))
        {
            errors.Add("knowledge package requires owner, id, game build, and source hash");
        }
        ValidateUnique(package.Actions.Select(item => item.SourceId), "action", errors);
        ValidateUnique(package.Statuses.Select(item => item.StatusId), "status", errors);
        ValidateUnique(package.Enemies.Select(item => item.EnemyId), "enemy", errors);
        ValidateUnique(package.Encounters.Select(item => item.EncounterId), "encounter", errors);
        foreach (var action in package.Actions.Where(item =>
                     item.Fidelity == CombatKnowledgeFidelity.Authoritative
                     && item.Semantics?.OpensInteraction == true
                     && (item.Semantics.Interaction == null
                         || !item.Semantics.Interaction.EffectsComplete)))
        {
            errors.Add(
                "authoritative interaction semantics are incomplete: "
                + action.SourceId);
        }
        return errors;
    }

    private static void ValidateUnique(
        IEnumerable<string> ids,
        string kind,
        ICollection<string> errors)
    {
        foreach (var group in ids.GroupBy(value => value ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                errors.Add(kind + " definition has an empty id");
            }
            else if (group.Count() > 1)
            {
                errors.Add("duplicate " + kind + " definition: " + group.Key);
            }
        }
    }

    internal static CombatActionSemantics CloneSemantics(CombatActionSemantics source)
    {
        return new CombatActionSemantics
        {
            Damage = source.Damage,
            TrueDamage = source.TrueDamage,
            DamageOverTime = source.DamageOverTime,
            SelfHpLoss = source.SelfHpLoss,
            DirectDamage = source.DirectDamage,
            ContextDamage = source.ContextDamage,
            DirectSelfHpLoss = source.DirectSelfHpLoss,
            ContextSelfHpLoss = source.ContextSelfHpLoss,
            DirectHeal = source.DirectHeal,
            ContextHeal = source.ContextHeal,
            ObservedNetHpDelta = source.ObservedNetHpDelta,
            MinimumHpDuringAction = source.MinimumHpDuringAction,
            LethalBeforeRecovery = source.LethalBeforeRecovery,
            EndOfCycleSelfHpLoss = source.EndOfCycleSelfHpLoss,
            HitCount = source.HitCount,
            Defend = source.Defend,
            Heal = source.Heal,
            Draw = source.Draw,
            EnergyGain = source.EnergyGain,
            EnergySetAmount = source.EnergySetAmount,
            EnergyMinimum = source.EnergyMinimum,
            RestoreEnergyToMaximum = source.RestoreEnergyToMaximum,
            CardRetrievals = source.CardRetrievals.Select(item =>
                new CombatCardRetrievalSemantic
                {
                    SourceZone = item.SourceZone,
                    DestinationZone = item.DestinationZone,
                    Amount = item.Amount,
                    RequiredCardTag = item.RequiredCardTag,
                    CandidateBranchCount = item.CandidateBranchCount
                }).ToList(),
            Scaling = source.Scaling,
            DeckValue = source.DeckValue,
            Buff = source.Buff,
            Debuff = source.Debuff,
            Cleanse = source.Cleanse,
            CostReduction = source.CostReduction,
            CardGeneration = source.CardGeneration,
            PersistentValue = source.PersistentValue,
            DamageMultiplierGain = source.DamageMultiplierGain,
            ImmediateHpDamage = source.ImmediateHpDamage,
            ImmediateDurabilityDamage =
                source.ImmediateDurabilityDamage,
            DeferredHpDamage = source.DeferredHpDamage,
            AffectedEnemyCount = source.AffectedEnemyCount,
            TargetEffects = source.TargetEffects
                .Select(item => item.Clone())
                .ToList(),
            StateChanges = new Dictionary<string, double>(
                source.StateChanges,
                StringComparer.OrdinalIgnoreCase),
            CooldownTurns = source.CooldownTurns,
            Risk = source.Risk,
            Uncertainty = source.Uncertainty,
            OpensInteraction = source.OpensInteraction || source.Interaction != null,
            Interaction = source.Interaction?.Clone(),
            RandomOutcome = source.RandomOutcome,
            EndsTurn = source.EndsTurn,
            DamageToBlockSetup = source.DamageToBlockSetup,
            HandTransform = source.HandTransform == null
                ? null
                : new CombatHandTransformSemantic
                {
                    TargetCardId = source.HandTransform.TargetCardId,
                    TargetCardSemantics = CloneSemantics(
                        source.HandTransform.TargetCardSemantics),
                    TransformAllHandCards =
                        source.HandTransform.TransformAllHandCards,
                    PreserveInstances = source.HandTransform.PreserveInstances,
                    ClearsEnhancements =
                        source.HandTransform.ClearsEnhancements,
                    ClearsVariables = source.HandTransform.ClearsVariables,
                    TargetRetained = source.HandTransform.TargetRetained,
                    TargetExhaustsOnUse =
                        source.HandTransform.TargetExhaustsOnUse,
                    GrowthStateKey = source.HandTransform.GrowthStateKey,
                    GrowthPerExhaust = source.HandTransform.GrowthPerExhaust,
                    CurrentGrowthValue =
                        source.HandTransform.CurrentGrowthValue,
                    TargetTier = source.HandTransform.TargetTier,
                    NextTierThreshold =
                        source.HandTransform.NextTierThreshold,
                    CooldownProgressRequired =
                        source.HandTransform.CooldownProgressRequired,
                    CooldownProgressEvent =
                        source.HandTransform.CooldownProgressEvent
                }
        };
    }

    private static void EnrichCrossDefinitionSemantics(
        CombatKnowledgePackage package)
    {
        var statuses = package.Statuses.ToDictionary(
            item => item.StatusId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var action in package.Actions)
        {
            action.Semantics ??= new CombatActionSemantics();
            var script = string.Join(
                "\n",
                action.TableFields
                    .Where(item => item.Key.EndsWith(
                        "Script",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Value));
            action.Semantics.EndsTurn = action.Semantics.EndsTurn
                || script.IndexOf(
                    "ChangeRound(",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var stateChange in action.Semantics.StateChanges.Keys)
            {
                const string prefix = "status:";
                if (!stateChange.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase)
                    || !statuses.TryGetValue(
                        stateChange.Substring(prefix.Length),
                        out var status))
                {
                    continue;
                }
                var recordsDamage = status.Triggers.Any(trigger =>
                    trigger.IndexOf("Hurt", StringComparison.OrdinalIgnoreCase)
                    >= 0);
                var settlesAtTurnEnd = status.Triggers.Any(trigger =>
                    trigger.IndexOf(
                        "EndRound",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                var grantsBlock = status.Operations.Any(operation =>
                    string.Equals(
                        operation.Api,
                        "ChangeDefence",
                        StringComparison.OrdinalIgnoreCase));
                if (recordsDamage && settlesAtTurnEnd && grantsBlock)
                {
                    action.Semantics.DamageToBlockSetup = true;
                }
            }
        }
    }

    private static string Key(string owner, string id)
    {
        return owner.Trim() + ":" + id.Trim();
    }

    private sealed class Registration : IDisposable
    {
        private Action? dispose;

        public Registration(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            var action = dispose;
            dispose = null;
            action?.Invoke();
        }
    }

    private sealed class EmptyRegistration : IDisposable
    {
        public static readonly EmptyRegistration Instance = new();

        public void Dispose()
        {
        }
    }
}
