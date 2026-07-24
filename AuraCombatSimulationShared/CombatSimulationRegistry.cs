using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public interface ICombatRulesetProvider
{
    void RegisterDefinitions(CombatRulesetBuilder builder);
}

public interface ICombatScenarioProvider
{
    IEnumerable<CombatScenarioDefinition> GetScenarios();
}

public static class CombatSimulationRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RegistrationEntry> Providers =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ScenarioRegistrationEntry> ScenarioProviders =
        new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable RegisterProvider(
        string ownerModId,
        string providerId,
        ICombatRulesetProvider provider,
        int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(ownerModId))
        {
            throw new ArgumentException("Owner MOD id is required.", nameof(ownerModId));
        }
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        var key = ownerModId.Trim() + ":" + providerId.Trim();
        var entry = new RegistrationEntry(
            ownerModId.Trim(),
            providerId.Trim(),
            provider,
            priority);
        lock (Gate)
        {
            Providers[key] = entry;
        }
        return new Registration(() =>
        {
            lock (Gate)
            {
                if (Providers.TryGetValue(key, out var current)
                    && ReferenceEquals(current, entry))
                {
                    Providers.Remove(key);
                }
            }
        });
    }

    public static CombatRulesetBuildResult BuildRuleset(string version)
    {
        RegistrationEntry[] snapshot;
        lock (Gate)
        {
            snapshot = Providers.Values
                .OrderByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.OwnerModId, StringComparer.Ordinal)
                .ThenBy(entry => entry.ProviderId, StringComparer.Ordinal)
                .ToArray();
        }

        var builder = new CombatRulesetBuilder(version);
        var providerErrors = new List<string>();
        foreach (var entry in snapshot)
        {
            try
            {
                entry.Provider.RegisterDefinitions(builder);
            }
            catch (Exception ex)
            {
                providerErrors.Add(
                    "ruleset provider " + entry.OwnerModId + ":" + entry.ProviderId
                    + " failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        var result = builder.Freeze();
        if (providerErrors.Count > 0)
        {
            result.Success = false;
            result.Ruleset = CombatRuleset.Empty;
            result.Errors.InsertRange(0, providerErrors);
        }
        return result;
    }

    public static CombatRulesetBuildResult BuildRuleset(CombatRulesetDocument? document)
    {
        if (document == null)
        {
            return new CombatRulesetBuildResult
            {
                Errors = { "ruleset document is required" }
            };
        }

        var builder = new CombatRulesetBuilder(document.Version);
        foreach (var card in document.Cards ?? new List<CombatCardDefinition>())
        {
            builder.RegisterCard(card);
        }
        foreach (var enemy in document.Enemies ?? new List<CombatEnemyDefinition>())
        {
            builder.RegisterEnemy(enemy);
        }
        foreach (var status in document.Statuses ?? new List<CombatStatusDefinition>())
        {
            builder.RegisterStatus(status);
        }
        return builder.Freeze();
    }

    public static IReadOnlyList<string> SnapshotProviderIds()
    {
        lock (Gate)
        {
            return Providers.Values
                .OrderBy(entry => entry.OwnerModId, StringComparer.Ordinal)
                .ThenBy(entry => entry.ProviderId, StringComparer.Ordinal)
                .Select(entry => entry.OwnerModId + ":" + entry.ProviderId)
                .ToList();
        }
    }

    public static IDisposable RegisterScenarioProvider(
        string ownerModId,
        string providerId,
        ICombatScenarioProvider provider,
        int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(ownerModId))
        {
            throw new ArgumentException("Owner MOD id is required.", nameof(ownerModId));
        }
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }
        var key = ownerModId.Trim() + ":" + providerId.Trim();
        var entry = new ScenarioRegistrationEntry(
            ownerModId.Trim(),
            providerId.Trim(),
            provider,
            priority);
        lock (Gate)
        {
            ScenarioProviders[key] = entry;
        }
        return new Registration(() =>
        {
            lock (Gate)
            {
                if (ScenarioProviders.TryGetValue(key, out var current)
                    && ReferenceEquals(current, entry))
                {
                    ScenarioProviders.Remove(key);
                }
            }
        });
    }

    public static IReadOnlyList<CombatScenarioDefinition> SnapshotScenarios()
    {
        ScenarioRegistrationEntry[] snapshot;
        lock (Gate)
        {
            snapshot = ScenarioProviders.Values
                .OrderByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.OwnerModId, StringComparer.Ordinal)
                .ThenBy(entry => entry.ProviderId, StringComparer.Ordinal)
                .ToArray();
        }
        var scenarios = new Dictionary<string, CombatScenarioDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
        {
            try
            {
                foreach (var scenario in entry.Provider.GetScenarios()
                             ?? Array.Empty<CombatScenarioDefinition>())
                {
                    if (scenario == null || string.IsNullOrWhiteSpace(scenario.ScenarioId))
                    {
                        continue;
                    }
                    var scenarioId = scenario.ScenarioId.Trim();
                    if (!scenarios.ContainsKey(scenarioId))
                    {
                        scenarios[scenarioId] = CombatScenarioCloner.Clone(scenario);
                    }
                }
            }
            catch
            {
                // One content provider must not hide valid scenarios from other owners.
            }
        }
        return scenarios.Values
            .OrderBy(scenario => scenario.ScenarioId, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class RegistrationEntry
    {
        public RegistrationEntry(
            string ownerModId,
            string providerId,
            ICombatRulesetProvider provider,
            int priority)
        {
            OwnerModId = ownerModId;
            ProviderId = providerId;
            Provider = provider;
            Priority = priority;
        }

        public string OwnerModId { get; }

        public string ProviderId { get; }

        public ICombatRulesetProvider Provider { get; }

        public int Priority { get; }
    }

    private sealed class ScenarioRegistrationEntry
    {
        public ScenarioRegistrationEntry(
            string ownerModId,
            string providerId,
            ICombatScenarioProvider provider,
            int priority)
        {
            OwnerModId = ownerModId;
            ProviderId = providerId;
            Provider = provider;
            Priority = priority;
        }

        public string OwnerModId { get; }

        public string ProviderId { get; }

        public ICombatScenarioProvider Provider { get; }

        public int Priority { get; }
    }

    private sealed class Registration : IDisposable
    {
        private readonly Action dispose;
        private bool disposed;

        public Registration(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            dispose();
        }
    }
}
