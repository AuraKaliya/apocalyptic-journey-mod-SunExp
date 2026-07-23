using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatAiRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SemanticRegistration> SemanticProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PreflightRegistration> PreflightRules =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ThreatRegistration> ThreatProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ICombatTrainingSampleSink> TrainingSinks =
        new(StringComparer.OrdinalIgnoreCase);

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
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                SemanticProviders.Remove(key);
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
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                PreflightRules.Remove(key);
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
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                TrainingSinks.Remove(key);
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
        }

        return new Registration(() =>
        {
            lock (Gate)
            {
                ThreatProviders.Remove(key);
            }
        });
    }

    public static bool EvaluatePreflight(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        PreflightRegistration[] snapshot;
        lock (Gate)
        {
            snapshot = PreflightRules.Values.OrderByDescending(item => item.Priority).ToArray();
        }

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

    public static void ApplySemantics(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        SemanticRegistration[] snapshot;
        lock (Gate)
        {
            snapshot = SemanticProviders.Values.OrderByDescending(item => item.Priority).ToArray();
        }

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].Provider.TryDescribe(state, action, out var semantics) && semantics != null)
            {
                action.Semantics = semantics;
                return;
            }
        }
    }

    public static bool TryResolveThreat(
        CombatStateObservation state,
        out CombatThreatForecast forecast)
    {
        ThreatRegistration[] snapshot;
        lock (Gate)
        {
            snapshot = ThreatProviders.Values.OrderByDescending(item => item.Priority).ToArray();
        }

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

    public static void RecordTrainingSample(CombatTrainingSample sample)
    {
        ICombatTrainingSampleSink[] snapshot;
        lock (Gate)
        {
            snapshot = TrainingSinks.Values.ToArray();
        }

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
