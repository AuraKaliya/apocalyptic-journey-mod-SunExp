using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AuraCombatAi.Shared;

/// <summary>
/// Immutable decision-preparation inputs for an isolated combat worker.
/// The shared layer only freezes provider interfaces; all concrete semantics
/// remain owned by the registering consumer.
/// </summary>
public sealed class CombatDecisionPreparationSnapshot
{
    private readonly ICombatSemanticProvider[] semanticProviders;
    private readonly ICombatRoleStrategyProvider[] roleStrategyProviders;
    private readonly ICombatSkillTimingProvider[] skillTimingProviders;
    private readonly ICombatPreflightRule[] preflightRules;

    internal CombatDecisionPreparationSnapshot(
        ICombatSemanticProvider[] semanticProviders,
        ICombatRoleStrategyProvider[] roleStrategyProviders,
        ICombatSkillTimingProvider[] skillTimingProviders,
        ICombatPreflightRule[] preflightRules)
    {
        this.semanticProviders = semanticProviders ??
                                 Array.Empty<ICombatSemanticProvider>();
        this.roleStrategyProviders = roleStrategyProviders ??
                                     Array.Empty<ICombatRoleStrategyProvider>();
        this.skillTimingProviders = skillTimingProviders ??
                                    Array.Empty<ICombatSkillTimingProvider>();
        this.preflightRules = preflightRules ??
                              Array.Empty<ICombatPreflightRule>();
    }

    public static CombatDecisionPreparationSnapshot Empty { get; } = new(
        Array.Empty<ICombatSemanticProvider>(),
        Array.Empty<ICombatRoleStrategyProvider>(),
        Array.Empty<ICombatSkillTimingProvider>(),
        Array.Empty<ICombatPreflightRule>());

    public int SemanticProviderCount => semanticProviders.Length;

    public int RoleStrategyProviderCount => roleStrategyProviders.Length;

    public int SkillTimingProviderCount => skillTimingProviders.Length;

    public int PreflightRuleCount => preflightRules.Length;

    public bool IsEmpty => SemanticProviderCount == 0
                           && RoleStrategyProviderCount == 0
                           && SkillTimingProviderCount == 0
                           && PreflightRuleCount == 0;

    public bool EvaluatePreflight(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        for (var i = 0; i < preflightRules.Length; i++)
        {
            if (!preflightRules[i].IsLegal(state, action, out reason))
            {
                return false;
            }
        }
        reason = "";
        return true;
    }

    public void ApplySemantics(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        for (var i = 0; i < semanticProviders.Length; i++)
        {
            if (semanticProviders[i].TryDescribe(
                    state,
                    action,
                    out var semantics)
                && semantics != null)
            {
                action.Semantics = semantics;
                action.SemanticSource = "provider";
                action.SemanticFidelity = CombatKnowledgeFidelity.Authoritative;
                return;
            }
        }
    }

    public bool EnrichRoleStrategies(CombatStateObservation state)
    {
        if (state == null)
        {
            return false;
        }
        var enriched = false;
        for (var i = 0; i < roleStrategyProviders.Length; i++)
        {
            enriched |= roleStrategyProviders[i].TryEnrich(state);
        }
        return enriched;
    }

    public bool EnrichSkillTimings(CombatStateObservation state)
    {
        if (state == null)
        {
            return false;
        }
        var enriched = false;
        for (var i = 0; i < skillTimingProviders.Length; i++)
        {
            enriched |= skillTimingProviders[i].TryEnrich(state);
        }
        return enriched;
    }
}

public static class CombatAiRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SemanticRegistration> SemanticProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, RoleStrategyRegistration>
        RoleStrategyProviders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SkillTimingRegistration>
        SkillTimingProviders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PreflightRegistration> PreflightRules =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, RuntimePreflightRegistration> RuntimePreflightRules =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ThreatRegistration> ThreatProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, EffectRegistration> EffectResolvers =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SimulationRuleRegistration> SimulationRules =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ICombatTrainingSampleSink> TrainingSinks =
        new(StringComparer.OrdinalIgnoreCase);
    private static CombatDecisionPreparationSnapshot decisionPreparationSnapshot =
        CombatDecisionPreparationSnapshot.Empty;
    private static PreflightRegistration[] preflightSnapshot =
        Array.Empty<PreflightRegistration>();
    private static RuntimePreflightRegistration[] runtimePreflightSnapshot =
        Array.Empty<RuntimePreflightRegistration>();
    private static SemanticRegistration[] semanticSnapshot =
        Array.Empty<SemanticRegistration>();
    private static RoleStrategyRegistration[] roleStrategySnapshot =
        Array.Empty<RoleStrategyRegistration>();
    private static SkillTimingRegistration[] skillTimingSnapshot =
        Array.Empty<SkillTimingRegistration>();
    private static ThreatRegistration[] threatSnapshot =
        Array.Empty<ThreatRegistration>();
    private static EffectRegistration[] effectSnapshot =
        Array.Empty<EffectRegistration>();
    private static ICombatSimulationRule[] simulationRuleSnapshot =
        Array.Empty<ICombatSimulationRule>();
    private static ICombatTrainingSampleSink[] trainingSinkSnapshot =
        Array.Empty<ICombatTrainingSampleSink>();
    private static long revision;

    public static long Revision => Volatile.Read(ref revision);

    public static IDisposable RegisterSemanticProvider(
        string ownerModId,
        string providerId,
        ICombatSemanticProvider provider,
        int priority = 0)
    {
        if (provider == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, providerId);
        lock (Gate)
        {
            SemanticProviders[key] = new SemanticRegistration(provider, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                SemanticProviders.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterRoleStrategyProvider(
        string ownerModId,
        string providerId,
        ICombatRoleStrategyProvider provider,
        int priority = 0)
    {
        if (provider == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, providerId);
        lock (Gate)
        {
            RoleStrategyProviders[key] =
                new RoleStrategyRegistration(provider, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                RoleStrategyProviders.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterSkillTimingProvider(
        string ownerModId,
        string providerId,
        ICombatSkillTimingProvider provider,
        int priority = 0)
    {
        if (provider == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, providerId);
        lock (Gate)
        {
            SkillTimingProviders[key] =
                new SkillTimingRegistration(provider, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                SkillTimingProviders.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterPreflightRule(
        string ownerModId,
        string ruleId,
        ICombatPreflightRule rule,
        int priority = 0)
    {
        if (rule == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, ruleId);
        lock (Gate)
        {
            PreflightRules[key] = new PreflightRegistration(rule, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                PreflightRules.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterRuntimePreflightRule(
        string ownerModId,
        string ruleId,
        ICombatRuntimePreflightRule rule,
        int priority = 0)
    {
        if (rule == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, ruleId);
        lock (Gate)
        {
            RuntimePreflightRules[key] = new RuntimePreflightRegistration(rule, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                RuntimePreflightRules.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterTrainingSink(
        string ownerModId,
        string sinkId,
        ICombatTrainingSampleSink sink)
    {
        if (sink == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, sinkId);
        lock (Gate)
        {
            TrainingSinks[key] = sink;
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                TrainingSinks.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterThreatProvider(
        string ownerModId,
        string providerId,
        ICombatThreatProvider provider,
        int priority = 0)
    {
        if (provider == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, providerId);
        lock (Gate)
        {
            ThreatProviders[key] = new ThreatRegistration(provider, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                ThreatProviders.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterEffectResolver(
        string ownerModId,
        string resolverId,
        ICombatEffectResolver resolver,
        int priority = 0)
    {
        if (resolver == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, resolverId);
        lock (Gate)
        {
            EffectResolvers[key] = new EffectRegistration(resolver, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                EffectResolvers.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static IDisposable RegisterSimulationRule(
        string ownerModId,
        string ruleId,
        ICombatSimulationRule rule,
        int priority = 0)
    {
        if (rule == null)
        {
            return EmptyDisposable.Instance;
        }

        var key = Key(ownerModId, ruleId);
        lock (Gate)
        {
            SimulationRules[key] = new SimulationRuleRegistration(rule, priority);
            RebuildSnapshotsNoLock();
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                SimulationRules.Remove(key);
                RebuildSnapshotsNoLock();
            }
        });
    }

    public static bool EvaluatePreflight(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        var snapshot = Volatile.Read(ref preflightSnapshot);

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (!snapshot[i].Rule.IsLegal(state, action, out reason))
            {
                return false;
            }
        }

        reason = "";
        return true;
    }

    public static CombatDecisionPreparationSnapshot SnapshotDecisionPreparation()
    {
        lock (Gate)
        {
            return decisionPreparationSnapshot;
        }
    }

    public static bool EvaluateRuntimePreflight(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatRuntimeActionContext runtime,
        out string reason)
    {
        var snapshot = Volatile.Read(ref runtimePreflightSnapshot);

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (!snapshot[i].Rule.IsLegal(state, action, runtime, out reason))
            {
                return false;
            }
        }

        reason = "";
        return true;
    }

    public static void ApplySemantics(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var snapshot = Volatile.Read(ref semanticSnapshot);

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].Provider.TryDescribe(state, action, out var semantics) && semantics != null)
            {
                action.Semantics = semantics;
                action.SemanticSource = "provider";
                action.SemanticFidelity = CombatKnowledgeFidelity.Authoritative;
                return;
            }
        }

        if (CombatKnowledgeRegistry.TryDescribeAction(
                action,
                out var knowledgeSemantics,
                out var fidelity,
                out var source))
        {
            action.Semantics = knowledgeSemantics;
            action.SemanticSource = source;
            action.SemanticFidelity = fidelity;
        }
    }

    public static bool EnrichRoleStrategies(CombatStateObservation state)
    {
        if (state == null)
        {
            return false;
        }
        var snapshot = Volatile.Read(ref roleStrategySnapshot);
        var enriched = false;
        for (var i = 0; i < snapshot.Length; i++)
        {
            enriched |= snapshot[i].Provider.TryEnrich(state);
        }
        return enriched;
    }

    public static bool EnrichSkillTimings(CombatStateObservation state)
    {
        if (state == null)
        {
            return false;
        }
        var snapshot = Volatile.Read(ref skillTimingSnapshot);
        var enriched = false;
        for (var i = 0; i < snapshot.Length; i++)
        {
            enriched |= snapshot[i].Provider.TryEnrich(state);
        }
        return enriched;
    }

    public static bool TryResolveThreat(
        CombatStateObservation state,
        out CombatThreatForecast forecast)
    {
        var snapshot = Volatile.Read(ref threatSnapshot);

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].Provider.TryForecast(state, out forecast) && forecast != null)
            {
                return true;
            }
        }

        forecast = new CombatThreatForecast();
        return false;
    }

    public static bool TryResolveEffects(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionModel model)
    {
        var snapshot = Volatile.Read(ref effectSnapshot);

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].Resolver.TryResolve(state, action, out model)
                && model != null
                && model.Outcomes.Count > 0)
            {
                return true;
            }
        }

        model = new CombatActionModel();
        return false;
    }

    public static bool EvaluateSimulation(
        CombatSimulationState state,
        CombatActionObservation action,
        out string reason)
    {
        var snapshot = SnapshotSimulationRules();

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (!snapshot[i].IsLegal(state, action, out reason))
            {
                return false;
            }
        }
        reason = "";
        return true;
    }

    public static ICombatSimulationRule[] SnapshotSimulationRules()
    {
        lock (Gate)
        {
            return Volatile.Read(ref simulationRuleSnapshot).ToArray();
        }
    }

    public static void RecordTrainingSample(CombatTrainingSample sample)
    {
        var snapshot = Volatile.Read(ref trainingSinkSnapshot);

        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i].Record(sample);
        }
    }

    private static string Key(string ownerModId, string id)
    {
        var owner = string.IsNullOrWhiteSpace(ownerModId) ? "unknown" : ownerModId.Trim();
        var local = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        return owner + ":" + local;
    }

    private static void RebuildSnapshotsNoLock()
    {
        var semantics = SemanticProviders.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var roleStrategies = RoleStrategyProviders.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var skillTimings = SkillTimingProviders.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var preflights = PreflightRules.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var runtimePreflights = RuntimePreflightRules.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var threats = ThreatProviders.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var effects = EffectResolvers.Values
            .OrderByDescending(item => item.Priority)
            .ToArray();
        var simulationRules = SimulationRules.Values
            .OrderByDescending(item => item.Priority)
            .Select(item => item.Rule)
            .ToArray();
        var trainingSinks = TrainingSinks.Values.ToArray();
        var decisionPreparation = new CombatDecisionPreparationSnapshot(
            semantics.Select(item => item.Provider).ToArray(),
            roleStrategies.Select(item => item.Provider).ToArray(),
            skillTimings.Select(item => item.Provider).ToArray(),
            preflights.Select(item => item.Rule).ToArray());
        Volatile.Write(ref semanticSnapshot, semantics);
        Volatile.Write(ref roleStrategySnapshot, roleStrategies);
        Volatile.Write(ref skillTimingSnapshot, skillTimings);
        Volatile.Write(ref preflightSnapshot, preflights);
        Volatile.Write(ref runtimePreflightSnapshot, runtimePreflights);
        Volatile.Write(ref threatSnapshot, threats);
        Volatile.Write(ref effectSnapshot, effects);
        Volatile.Write(ref simulationRuleSnapshot, simulationRules);
        Volatile.Write(ref trainingSinkSnapshot, trainingSinks);
        Volatile.Write(ref decisionPreparationSnapshot, decisionPreparation);
        Interlocked.Increment(ref revision);
    }

    private sealed class SemanticRegistration
    {
        public SemanticRegistration(ICombatSemanticProvider provider, int priority)
        {
            Provider = provider;
            Priority = priority;
        }

        public ICombatSemanticProvider Provider { get; }

        public int Priority { get; }
    }

    private sealed class RoleStrategyRegistration
    {
        public RoleStrategyRegistration(
            ICombatRoleStrategyProvider provider,
            int priority)
        {
            Provider = provider;
            Priority = priority;
        }

        public ICombatRoleStrategyProvider Provider { get; }

        public int Priority { get; }
    }

    private sealed class SkillTimingRegistration
    {
        public SkillTimingRegistration(
            ICombatSkillTimingProvider provider,
            int priority)
        {
            Provider = provider;
            Priority = priority;
        }

        public ICombatSkillTimingProvider Provider { get; }

        public int Priority { get; }
    }

    private sealed class PreflightRegistration
    {
        public PreflightRegistration(ICombatPreflightRule rule, int priority)
        {
            Rule = rule;
            Priority = priority;
        }

        public ICombatPreflightRule Rule { get; }

        public int Priority { get; }
    }

    private sealed class RuntimePreflightRegistration
    {
        public RuntimePreflightRegistration(
            ICombatRuntimePreflightRule rule,
            int priority)
        {
            Rule = rule;
            Priority = priority;
        }

        public ICombatRuntimePreflightRule Rule { get; }

        public int Priority { get; }
    }

    private sealed class ThreatRegistration
    {
        public ThreatRegistration(ICombatThreatProvider provider, int priority)
        {
            Provider = provider;
            Priority = priority;
        }

        public ICombatThreatProvider Provider { get; }

        public int Priority { get; }
    }

    private sealed class EffectRegistration
    {
        public EffectRegistration(ICombatEffectResolver resolver, int priority)
        {
            Resolver = resolver;
            Priority = priority;
        }

        public ICombatEffectResolver Resolver { get; }

        public int Priority { get; }
    }

    private sealed class SimulationRuleRegistration
    {
        public SimulationRuleRegistration(ICombatSimulationRule rule, int priority)
        {
            Rule = rule;
            Priority = priority;
        }

        public ICombatSimulationRule Rule { get; }

        public int Priority { get; }
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

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
