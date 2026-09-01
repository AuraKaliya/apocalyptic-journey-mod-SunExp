using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraReplay.VisibleState.Shared;

public sealed class AuraReplayVisibleCaptureContext
{
    public string RecordId { get; set; } = "";
    public string LevelId { get; set; } = "";
    public string PerspectivePlayerId { get; set; } = "";
    public int RoundSequence { get; set; }
    public int ActorTurnSequence { get; set; }
}

public sealed class AuraReplayVisibleStateItem
{
    public string InstanceId { get; set; } = "";
    public string DisplayText { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}

public interface IAuraReplayVisibleStateProvider
{
    string OwnerModId { get; }
    string TypeId { get; }
    int SchemaVersion { get; }
    IReadOnlyList<AuraReplayVisibleStateItem> Capture(AuraReplayVisibleCaptureContext context);
}

public static class AuraReplayEntityPresentationModes
{
    public const string WorldEntity = "WorldEntity";
    public const string OwnerAttachedProxy = "OwnerAttachedProxy";
}

public static class AuraReplayEntityHudModes
{
    public const string NativeHorizontal = "NativeHorizontal";
    public const string DetachedRightVertical = "DetachedRightVertical";
}

public sealed class AuraReplayEntityPresentationItem
{
    public string EntityId { get; set; } = "";
    public string PresentationMode { get; set; } = AuraReplayEntityPresentationModes.WorldEntity;
    public string OwnerEntityId { get; set; } = "";
    public int ReferenceHeightPixels { get; set; }
    public int HorizontalOverlapQ16 { get; set; }
    public int SortingOrderOffset { get; set; }
    public string HudMode { get; set; } = AuraReplayEntityHudModes.NativeHorizontal;
    public int HudScaleQ16 { get; set; } = 65_536;
    public int HudRotationQ16 { get; set; }
    public string BadgeIconResourcePath { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public int AttackFocusTravelPixels { get; set; }
    public int InterferenceFocusTravelPixels { get; set; }
    public int SupportFocusTravelPixels { get; set; }
}

public interface IAuraReplayEntityPresentationProvider
{
    string OwnerModId { get; }
    int SchemaVersion { get; }
    IReadOnlyList<AuraReplayEntityPresentationItem> Capture(AuraReplayVisibleCaptureContext context);
}

/// <summary>
/// Process-local registry for owner-qualified, visible-data-only replay extensions.
/// Providers return precomputed data and never receive Unity objects or restore callbacks.
/// </summary>
public static class AuraReplayVisibleStateRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RegistrationEntry> Entries = new(StringComparer.Ordinal);
    private static long generation;

    public static IDisposable Register(IAuraReplayVisibleStateProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        var owner = NormalizeIdentity(provider.OwnerModId, nameof(provider.OwnerModId));
        var type = NormalizeIdentity(provider.TypeId, nameof(provider.TypeId));
        if (provider.SchemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(provider.SchemaVersion));
        var key = owner + "|" + type;
        lock (Gate)
        {
            if (Entries.ContainsKey(key))
                throw new InvalidOperationException("Replay visible-state provider is already registered: " + key);
            var entry = new RegistrationEntry(provider, ++generation);
            Entries.Add(key, entry);
            return new RegistrationLease(key, entry.Generation);
        }
    }

    public static IReadOnlyList<IAuraReplayVisibleStateProvider> Snapshot()
    {
        lock (Gate)
            return Entries.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Value.Provider)
                .ToArray();
    }

    public static void ClearOwner(string ownerModId)
    {
        var owner = (ownerModId ?? "").Trim();
        if (owner.Length == 0) return;
        lock (Gate)
            foreach (var key in Entries.Where(item =>
                         string.Equals(item.Value.Provider.OwnerModId, owner, StringComparison.Ordinal))
                     .Select(item => item.Key).ToList())
                Entries.Remove(key);
    }

    private static string NormalizeIdentity(string value, string parameter)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0 || normalized.Length > 128
            || normalized.Any(character => !char.IsLetterOrDigit(character)
                                           && character is not '.' and not '-' and not '_'))
            throw new ArgumentException("Replay visible-state identity is invalid.", parameter);
        return normalized;
    }

    private sealed class RegistrationEntry
    {
        internal RegistrationEntry(IAuraReplayVisibleStateProvider provider, long generation)
        {
            Provider = provider;
            Generation = generation;
        }

        internal IAuraReplayVisibleStateProvider Provider { get; }
        internal long Generation { get; }
    }

    private sealed class RegistrationLease : IDisposable
    {
        private readonly string key;
        private readonly long leaseGeneration;
        private bool disposed;

        internal RegistrationLease(string key, long leaseGeneration)
        {
            this.key = key;
            this.leaseGeneration = leaseGeneration;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
                if (Entries.TryGetValue(key, out var entry) && entry.Generation == leaseGeneration)
                    Entries.Remove(key);
        }
    }
}

public static class AuraReplayEntityPresentationRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RegistrationEntry> Entries = new(StringComparer.Ordinal);
    private static long generation;

    public static IDisposable Register(IAuraReplayEntityPresentationProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        var owner = NormalizeIdentity(provider.OwnerModId, nameof(provider.OwnerModId));
        if (provider.SchemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(provider.SchemaVersion));
        lock (Gate)
        {
            if (Entries.ContainsKey(owner))
                throw new InvalidOperationException("Replay entity-presentation provider is already registered: " + owner);
            var entry = new RegistrationEntry(provider, ++generation);
            Entries.Add(owner, entry);
            return new RegistrationLease(owner, entry.Generation);
        }
    }

    public static IReadOnlyList<IAuraReplayEntityPresentationProvider> Snapshot()
    {
        lock (Gate)
            return Entries.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Value.Provider)
                .ToArray();
    }

    public static void ClearOwner(string ownerModId)
    {
        var owner = (ownerModId ?? "").Trim();
        if (owner.Length == 0) return;
        lock (Gate) Entries.Remove(owner);
    }

    private static string NormalizeIdentity(string value, string parameter)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0 || normalized.Length > 128
            || normalized.Any(character => !char.IsLetterOrDigit(character)
                                           && character is not '.' and not '-' and not '_'))
            throw new ArgumentException("Replay entity-presentation owner identity is invalid.", parameter);
        return normalized;
    }

    private sealed class RegistrationEntry
    {
        internal RegistrationEntry(IAuraReplayEntityPresentationProvider provider, long generation)
        {
            Provider = provider;
            Generation = generation;
        }

        internal IAuraReplayEntityPresentationProvider Provider { get; }
        internal long Generation { get; }
    }

    private sealed class RegistrationLease : IDisposable
    {
        private readonly string owner;
        private readonly long leaseGeneration;
        private bool disposed;

        internal RegistrationLease(string owner, long leaseGeneration)
        {
            this.owner = owner;
            this.leaseGeneration = leaseGeneration;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
                if (Entries.TryGetValue(owner, out var entry) && entry.Generation == leaseGeneration)
                    Entries.Remove(owner);
        }
    }
}
