using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public interface ICombatRulesetProvider
{
    void RegisterDefinitions(CombatRulesetBuilder builder);
}

public static class CombatSimulationRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RegistrationEntry> Providers =
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
