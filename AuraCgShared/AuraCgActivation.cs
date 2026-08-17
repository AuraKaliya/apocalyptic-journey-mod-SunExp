using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;

namespace AuraCg.Shared;

public static class AuraCgActivationRuntime
{
    public const string ActivationFileName = "cg.activation.json";
    public const int CurrentActivationSchemaVersion = 1;
    public const string SourceManifestDefault = "ManifestDefault";
    public const string SourceUserOverride = "UserOverride";
    private static readonly object CacheGate = new();
    private static readonly object LocalOverrideGate = new();
    private static readonly Dictionary<string, Dictionary<string, AuraCgLocalActivationEntry>> LocalOverrides =
        new(StringComparer.OrdinalIgnoreCase);
    private static AuraSharedConfigSnapshot<AuraCgActivationDocument>? cachedSnapshot;
    private static DateTime cachedSnapshotUtc;
    private static long localOverrideRevision;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

    public static bool ApplyManifestDefaults(string ownerModId, IEnumerable<AuraCgRegistryEntry> entries)
    {
        var normalizedEntries = (entries ?? Array.Empty<AuraCgRegistryEntry>())
            .Where(entry => entry != null && entry.Enabled && !string.IsNullOrWhiteSpace(entry.CgId))
            .ToList();
        if (normalizedEntries.Count == 0)
        {
            return false;
        }
        foreach (var entry in normalizedEntries)
        {
            entry.Normalize(ownerModId);
        }
        AuraCgLog.InfoOnce(
            "cg-activation-defaults:" + ownerModId,
            "CG activation defaults are resolved directly from manifests. owner="
            + ownerModId
            + ", entries="
            + normalizedEntries.Count);
        return true;
    }

    public static bool CanConsumerPlay(AuraCgRegistryEntry entry, string consumerModId)
    {
        return CanProducerEmit(entry, consumerModId) && IsLocallyEnabled(entry);
    }

    /// <summary>
    /// Checks request-producer ownership only. Recipient-local enablement is intentionally
    /// excluded so one peer cannot suppress another peer's locally enabled presentation.
    /// </summary>
    public static bool CanProducerEmit(AuraCgRegistryEntry entry, string producerModId)
    {
        if (entry == null || !entry.Enabled)
        {
            return false;
        }

        var manifestState = AuraCgActivationEntryState.FromManifest(entry);
        return CanConsumerPlayState(entry.OwnerModId, manifestState, producerModId);
    }

    public static bool IsLocallyEnabled(AuraCgRegistryEntry entry)
    {
        if (entry == null || !entry.Enabled)
        {
            return false;
        }

        return TryGetLocalOverride(entry.QualifiedCgId, out var enabled)
            ? enabled
            : GetEffectiveState(entry).Enabled;
    }

    private static bool TryGetLocalOverride(string qualifiedCgId, out bool enabled)
    {
        lock (LocalOverrideGate)
        {
            AuraCgLocalActivationEntry? selected = null;
            foreach (var manager in LocalOverrides.Values)
            {
                if (!manager.TryGetValue(qualifiedCgId, out var candidate)
                    || (selected != null && selected.Revision >= candidate.Revision))
                {
                    continue;
                }

                selected = candidate;
            }

            if (selected != null)
            {
                enabled = selected.Enabled;
                return true;
            }
        }

        enabled = true;
        return false;
    }

    public static bool IsLocallyEnabled(string ownerModId, string cgId)
    {
        var registered = AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .FirstOrDefault(entry => string.Equals(entry.CgId, cgId, StringComparison.OrdinalIgnoreCase));
        if (registered != null)
        {
            return IsLocallyEnabled(registered);
        }

        var qualifiedCgId = AuraCgRegistryEntry.Qualify(ownerModId, cgId);
        if (TryGetLocalOverride(qualifiedCgId, out var enabled))
        {
            return enabled;
        }

        var state = GetStoredState(qualifiedCgId);
        return state?.Enabled ?? true;
    }

    public static AuraCgActivationEntryState GetLocalEffectiveState(AuraCgRegistryEntry entry)
    {
        var effective = GetEffectiveState(entry);
        return new AuraCgActivationEntryState
        {
            QualifiedCgId = effective.QualifiedCgId,
            OwnerModId = effective.OwnerModId,
            CgId = effective.CgId,
            Enabled = IsLocallyEnabled(entry),
            ConsumerMode = effective.ConsumerMode,
            ConsumerModId = effective.ConsumerModId,
            Source = effective.Source,
            UserOverridden = effective.UserOverridden
        };
    }

    public static void ReplaceLocalOverrides(
        string managerModId,
        IEnumerable<AuraCgLocalActivationOverride> overrides)
    {
        var manager = (managerModId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(manager))
        {
            return;
        }

        lock (LocalOverrideGate)
        {
            var revision = ++localOverrideRevision;
            var replacement = new Dictionary<string, AuraCgLocalActivationEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in overrides ?? Array.Empty<AuraCgLocalActivationOverride>())
            {
                var key = AuraCgRegistryEntry.Qualify(item?.OwnerModId ?? "", item?.CgId ?? "");
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                replacement[key] = new AuraCgLocalActivationEntry(item!.Enabled, revision);
            }

            LocalOverrides[manager] = replacement;
        }
    }

    public static void SetLocalOverride(
        string managerModId,
        string ownerModId,
        string cgId,
        bool enabled)
    {
        var manager = (managerModId ?? "").Trim();
        var key = AuraCgRegistryEntry.Qualify(ownerModId, cgId);
        if (string.IsNullOrWhiteSpace(manager) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (LocalOverrideGate)
        {
            if (!LocalOverrides.TryGetValue(manager, out var entries))
            {
                entries = new Dictionary<string, AuraCgLocalActivationEntry>(StringComparer.OrdinalIgnoreCase);
                LocalOverrides[manager] = entries;
            }

            entries[key] = new AuraCgLocalActivationEntry(enabled, ++localOverrideRevision);
        }
    }

    public static void ClearLocalOverrides(string managerModId)
    {
        var manager = (managerModId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(manager))
        {
            return;
        }

        lock (LocalOverrideGate)
        {
            LocalOverrides.Remove(manager);
        }
    }

    public static bool CanConsumerPlay(string ownerModId, string cgId, string consumerModId)
    {
        if (string.IsNullOrWhiteSpace(ownerModId) || string.IsNullOrWhiteSpace(cgId))
        {
            return true;
        }

        var registered = AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .FirstOrDefault(entry => string.Equals(entry.CgId, cgId, StringComparison.OrdinalIgnoreCase));
        if (registered != null)
        {
            return CanConsumerPlay(registered, consumerModId);
        }

        var state = GetStoredState(AuraCgRegistryEntry.Qualify(ownerModId, cgId));
        return state == null || CanConsumerPlayState(ownerModId, state, consumerModId);
    }

    public static bool SetOverride(string ownerModId, string cgId, bool enabled, string consumerMode, string consumerModId)
    {
        var key = AuraCgRegistryEntry.Qualify(ownerModId, cgId);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return UpdateState(key, state =>
        {
            state.OwnerModId = ownerModId;
            state.CgId = cgId;
            state.QualifiedCgId = key;
            state.Enabled = enabled;
            state.ConsumerMode = consumerMode;
            state.ConsumerModId = consumerModId;
            state.Source = AuraCgActivationRuntime.SourceUserOverride;
            state.UserOverridden = true;
            state.Normalize();
        });
    }

    public static bool SetEnabledOverride(string ownerModId, string cgId, bool enabled, string consumerModId)
    {
        var registered = AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .FirstOrDefault(entry => string.Equals(entry.CgId, cgId, StringComparison.OrdinalIgnoreCase));
        if (registered == null)
        {
            return SetOverride(ownerModId, cgId, enabled, AuraCgConsumerModes.ContentOwned, consumerModId);
        }

        var effective = GetEffectiveState(registered);
        return SetOverride(
            ownerModId,
            cgId,
            enabled,
            effective.ConsumerMode,
            string.IsNullOrWhiteSpace(effective.ConsumerModId) ? consumerModId : effective.ConsumerModId);
    }

    public static bool ClearOverride(string ownerModId, string cgId)
    {
        var registered = AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .FirstOrDefault(entry => string.Equals(entry.CgId, cgId, StringComparison.OrdinalIgnoreCase));
        if (registered == null)
        {
            return false;
        }

        return UpdateState(registered.QualifiedCgId, state =>
        {
            var desired = AuraCgActivationEntryState.FromManifest(registered);
            state.OwnerModId = desired.OwnerModId;
            state.CgId = desired.CgId;
            state.QualifiedCgId = desired.QualifiedCgId;
            state.Enabled = desired.Enabled;
            state.ConsumerMode = desired.ConsumerMode;
            state.ConsumerModId = desired.ConsumerModId;
            state.Source = desired.Source;
            state.UserOverridden = false;
            state.Normalize();
        });
    }

    public static AuraCgActivationEntryState GetEffectiveState(AuraCgRegistryEntry entry)
    {
        if (entry == null)
        {
            return new AuraCgActivationEntryState();
        }

        var stored = GetStoredState(entry.QualifiedCgId);
        if (stored != null)
        {
            return stored;
        }

        var fallback = AuraCgActivationEntryState.FromManifest(entry);
        fallback.Normalize();
        return fallback;
    }

    private static bool UpdateState(string qualifiedCgId, Action<AuraCgActivationEntryState> update)
    {
        if (string.IsNullOrWhiteSpace(qualifiedCgId) || update == null)
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = ReadDocument(useCache: false);
            var document = snapshot.Value ?? new AuraCgActivationDocument();
            document.Normalize();
            var state = document.Entries.FirstOrDefault(entry =>
                string.Equals(entry.QualifiedCgId, qualifiedCgId, StringComparison.OrdinalIgnoreCase));
            if (state == null)
            {
                state = new AuraCgActivationEntryState { QualifiedCgId = qualifiedCgId };
                document.Entries.Add(state);
            }

            update(state);
            document.Normalize();
            var result = AuraSharedConfigStore.WriteShared(
                AuraCgRegistryRuntime.RegistryAuthorityId,
                AuraSharedSystems.Cg,
                ActivationFileName,
                document,
                snapshot.Found ? snapshot.Revision : 0,
                CurrentActivationSchemaVersion);
            if (result.Success)
            {
                InvalidateCache();
                return true;
            }

            if (!result.Conflict)
            {
                AuraCgLog.WarnOnce("cg-activation-override-failed:" + qualifiedCgId, "CG activation override write failed: " + result.Message);
                return false;
            }
        }

        AuraCgLog.WarnOnce("cg-activation-override-conflict:" + qualifiedCgId, "CG activation override write conflicted repeatedly for " + qualifiedCgId + ".");
        return false;
    }

    private static AuraCgActivationEntryState? GetStoredState(string qualifiedCgId)
    {
        if (string.IsNullOrWhiteSpace(qualifiedCgId))
        {
            return null;
        }

        var snapshot = ReadDocument();
        var document = snapshot.Value ?? new AuraCgActivationDocument();
        document.Normalize();
        return document.Entries.FirstOrDefault(entry =>
            string.Equals(entry.QualifiedCgId, qualifiedCgId, StringComparison.OrdinalIgnoreCase)
            && (entry.UserOverridden
                || string.Equals(
                    entry.Source,
                    SourceUserOverride,
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static bool CanConsumerPlayState(string ownerModId, AuraCgActivationEntryState state, string consumerModId)
    {
        state ??= new AuraCgActivationEntryState();
        state.Normalize();
        if (!state.Enabled)
        {
            return false;
        }

        var consumer = (consumerModId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(consumer))
        {
            return false;
        }

        if (string.Equals(state.ConsumerMode, AuraCgConsumerModes.ContentOwned, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(ownerModId, consumer, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(state.ConsumerMode, AuraCgConsumerModes.ToolManaged, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(state.ConsumerModId)
                   || string.Equals(state.ConsumerModId, consumer, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(state.ConsumerMode, AuraCgConsumerModes.SharedRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(AuraCgRegistryRuntime.RegistryAuthorityId, consumer, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static AuraSharedConfigSnapshot<AuraCgActivationDocument> ReadDocument(bool useCache = true)
    {
        lock (CacheGate)
        {
            if (useCache && cachedSnapshot != null && DateTime.UtcNow - cachedSnapshotUtc <= CacheTtl)
            {
                return cachedSnapshot;
            }

            var snapshot = AuraSharedConfigStore.ReadShared(
                AuraCgRegistryRuntime.RegistryAuthorityId,
                AuraSharedSystems.Cg,
                ActivationFileName,
                new AuraCgActivationDocument());
            snapshot.Value ??= new AuraCgActivationDocument();
            snapshot.Value.Normalize();
            if (useCache)
            {
                cachedSnapshot = snapshot;
                cachedSnapshotUtc = DateTime.UtcNow;
            }

            return snapshot;
        }
    }

    public static void InvalidateCache()
    {
        lock (CacheGate)
        {
            cachedSnapshot = null;
            cachedSnapshotUtc = DateTime.MinValue;
        }
    }
}

public sealed class AuraCgLocalActivationOverride
{
    public string OwnerModId { get; set; } = "";

    public string CgId { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

internal sealed class AuraCgLocalActivationEntry
{
    public AuraCgLocalActivationEntry(bool enabled, long revision)
    {
        Enabled = enabled;
        Revision = revision;
    }

    public bool Enabled { get; }

    public long Revision { get; }
}

public static class AuraCgConsumerModes
{
    public const string Disabled = "disabled";
    public const string ContentOwned = "contentOwned";
    public const string ToolManaged = "toolManaged";
    public const string SharedRuntime = "sharedRuntime";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, Disabled, StringComparison.OrdinalIgnoreCase))
        {
            return Disabled;
        }

        if (string.Equals(mode, ToolManaged, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return ToolManaged;
        }

        if (string.Equals(mode, SharedRuntime, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "shared", StringComparison.OrdinalIgnoreCase))
        {
            return SharedRuntime;
        }

        return ContentOwned;
    }
}

public sealed class AuraCgActivationDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraCgActivationRuntime.CurrentActivationSchemaVersion;

    [JsonProperty("entries")]
    public List<AuraCgActivationEntryState> Entries { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(AuraCgActivationRuntime.CurrentActivationSchemaVersion, SchemaVersion);
        Entries ??= new List<AuraCgActivationEntryState>();
        var normalized = new Dictionary<string, AuraCgActivationEntryState>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            if (entry == null)
            {
                continue;
            }

            entry.Normalize();
            if (string.IsNullOrWhiteSpace(entry.QualifiedCgId))
            {
                continue;
            }

            normalized[entry.QualifiedCgId] = entry;
        }

        Entries = normalized.Values
            .OrderBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool ApplyManifestDefault(AuraCgRegistryEntry entry)
    {
        var key = entry.QualifiedCgId;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var existing = Entries.FirstOrDefault(state =>
            string.Equals(state.QualifiedCgId, key, StringComparison.OrdinalIgnoreCase));
        var desired = AuraCgActivationEntryState.FromManifest(entry);
        if (existing == null)
        {
            Entries.Add(desired);
            Normalize();
            return true;
        }

        existing.OwnerModId = entry.OwnerModId;
        existing.CgId = entry.CgId;
        existing.QualifiedCgId = key;
        if (existing.UserOverridden
            || string.Equals(existing.Source, AuraCgActivationRuntime.SourceUserOverride, StringComparison.OrdinalIgnoreCase))
        {
            existing.Normalize();
            return false;
        }

        var changed = false;
        if (existing.Enabled != desired.Enabled)
        {
            existing.Enabled = desired.Enabled;
            changed = true;
        }

        if (!string.Equals(existing.ConsumerMode, desired.ConsumerMode, StringComparison.Ordinal))
        {
            existing.ConsumerMode = desired.ConsumerMode;
            changed = true;
        }

        if (!string.Equals(existing.ConsumerModId, desired.ConsumerModId, StringComparison.Ordinal))
        {
            existing.ConsumerModId = desired.ConsumerModId;
            changed = true;
        }

        if (!string.Equals(existing.Source, AuraCgActivationRuntime.SourceManifestDefault, StringComparison.Ordinal))
        {
            existing.Source = AuraCgActivationRuntime.SourceManifestDefault;
            changed = true;
        }

        existing.Normalize();
        return changed;
    }
}

public sealed class AuraCgActivationEntryState
{
    [JsonProperty("qualifiedCgId")]
    public string QualifiedCgId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("cgId")]
    public string CgId { get; set; } = "";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("consumerMode")]
    public string ConsumerMode { get; set; } = AuraCgConsumerModes.ContentOwned;

    [JsonProperty("consumerModId")]
    public string ConsumerModId { get; set; } = "";

    [JsonProperty("source")]
    public string Source { get; set; } = AuraCgActivationRuntime.SourceManifestDefault;

    [JsonProperty("userOverridden")]
    public bool UserOverridden { get; set; }

    public static AuraCgActivationEntryState FromManifest(AuraCgRegistryEntry entry)
    {
        entry.DefaultActivation ??= new AuraCgDefaultActivationSpec();
        entry.DefaultActivation.Normalize();
        return new AuraCgActivationEntryState
        {
            QualifiedCgId = entry.QualifiedCgId,
            OwnerModId = entry.OwnerModId,
            CgId = entry.CgId,
            Enabled = entry.Enabled && entry.DefaultActivation.Enabled,
            ConsumerMode = entry.DefaultActivation.ConsumerMode,
            ConsumerModId = entry.DefaultActivation.ConsumerModId,
            Source = AuraCgActivationRuntime.SourceManifestDefault,
            UserOverridden = false
        };
    }

    public void Normalize()
    {
        OwnerModId = (OwnerModId ?? "").Trim();
        CgId = (CgId ?? "").Trim();
        QualifiedCgId = string.IsNullOrWhiteSpace(QualifiedCgId)
            ? AuraCgRegistryEntry.Qualify(OwnerModId, CgId)
            : QualifiedCgId.Trim();
        ConsumerMode = AuraCgConsumerModes.Normalize(ConsumerMode);
        ConsumerModId = (ConsumerModId ?? "").Trim();
        Source = string.IsNullOrWhiteSpace(Source) ? AuraCgActivationRuntime.SourceManifestDefault : Source.Trim();
    }
}

public sealed class AuraCgDefaultActivationSpec
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("consumerMode")]
    public string ConsumerMode { get; set; } = AuraCgConsumerModes.ContentOwned;

    [JsonProperty("consumerModId")]
    public string ConsumerModId { get; set; } = "";

    public void Normalize()
    {
        ConsumerMode = AuraCgConsumerModes.Normalize(ConsumerMode);
        ConsumerModId = (ConsumerModId ?? "").Trim();
    }
}
