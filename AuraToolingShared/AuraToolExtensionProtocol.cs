using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace AuraTooling.Shared;

public static class AuraToolExtensionProtocol
{
    public const int CurrentVersion = 1;
    public const int MinimumSupportedVersion = 1;
    public const string DefaultCategoryId = "extensions";
    public const int MaximumSearchTerms = 16;
    public const int MaximumDisplayLength = 96;
    public const int MaximumDescriptionLength = 240;

    public static bool IsCompatible(
        int protocolVersion,
        int minimumSupportedVersion = 0)
    {
        var minimum = minimumSupportedVersion > 0
            ? minimumSupportedVersion
            : protocolVersion;
        var negotiated = Math.Min(CurrentVersion, protocolVersion);
        return protocolVersion > 0
               && minimum > 0
               && minimum <= protocolVersion
               && negotiated >= MinimumSupportedVersion
               && negotiated >= minimum;
    }
}

public enum AuraToolExtensionAvailability
{
    Ready,
    Disabled,
    Unavailable,
    Degraded,
    Busy,
    RestartRequired
}

public sealed class AuraToolExtensionDescriptor
{
    public int ProtocolVersion { get; set; } = AuraToolExtensionProtocol.CurrentVersion;

    public int MinimumSupportedProtocolVersion { get; set; }

    public string OwnerModId { get; set; } = "";

    public string ModuleId { get; set; } = "";

    public string CategoryId { get; set; } = AuraToolExtensionProtocol.DefaultCategoryId;

    public int Order { get; set; } = 500;

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconKey { get; set; } = "";

    public IReadOnlyList<string> SearchTerms { get; set; } = Array.Empty<string>();

    public bool HasSettingsPage { get; set; }

    public bool Experimental { get; set; }

    public bool RequiresRestartWhenChanged { get; set; }

    public string QualifiedModuleId => OwnerModId + ":" + ModuleId;
}

public sealed class AuraToolExtensionState
{
    public long Revision { get; set; }

    public bool ConfiguredEnabled { get; set; }

    public bool EffectiveEnabled { get; set; }

    public AuraToolExtensionAvailability Availability { get; set; }

    public string Summary { get; set; } = "";

    public string Attention { get; set; } = "";

    public int? ItemCount { get; set; }
}

public sealed class AuraToolExtensionOperationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public static AuraToolExtensionOperationResult Ok(string message = "") => new()
    {
        Success = true,
        Message = message ?? ""
    };

    public static AuraToolExtensionOperationResult Fail(string message) => new()
    {
        Success = false,
        Message = message ?? ""
    };
}

public interface IAuraToolExtensionProvider
{
    AuraToolExtensionDescriptor Descriptor { get; }

    AuraToolExtensionState SnapshotState();

    AuraToolExtensionOperationResult SetEnabled(bool enabled);

    void ShowSettings(Transform parent);
}

public sealed class AuraToolExtensionRegistration
{
    internal AuraToolExtensionRegistration(
        AuraToolExtensionDescriptor descriptor,
        IAuraToolExtensionProvider provider,
        long revision)
    {
        Descriptor = descriptor;
        Provider = provider;
        Revision = revision;
    }

    public AuraToolExtensionDescriptor Descriptor { get; }

    public IAuraToolExtensionProvider Provider { get; }

    public long Revision { get; }
}

public sealed class AuraToolExtensionRegistrationResult
{
    public bool Success { get; set; }

    public bool AlreadyRegistered { get; set; }

    public string Message { get; set; } = "";

    public IDisposable? Handle { get; set; }
}

public static class AuraToolExtensionRegistry
{
    private sealed class Registered
    {
        public AuraToolExtensionDescriptor Descriptor { get; set; } = new();
        public IAuraToolExtensionProvider Provider { get; set; } = null!;
        public long Token { get; set; }
        public long Revision { get; set; }
        public int Leases { get; set; }
        public long StateRevision { get; set; } = -1;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Registered> Entries =
        new(StringComparer.OrdinalIgnoreCase);
    private static long revision;
    private static long token;

    public static event Action<long>? Changed;

    public static event Action<string, long>? StateChanged;

    public static long Revision
    {
        get
        {
            lock (Gate)
            {
                return revision;
            }
        }
    }

    public static AuraToolExtensionRegistrationResult Register(
        string ownerModId,
        IAuraToolExtensionProvider provider)
    {
        if (provider == null)
        {
            return Fail("Extension provider is required.");
        }

        AuraToolExtensionDescriptor descriptor;
        try
        {
            descriptor = NormalizeDescriptor(provider.Descriptor);
        }
        catch (Exception ex)
        {
            return Fail("Extension descriptor failed: " + ex.Message);
        }

        var validation = ValidateDescriptor(ownerModId, descriptor);
        if (!string.IsNullOrWhiteSpace(validation))
        {
            return Fail(validation);
        }

        long changedRevision;
        long registrationToken;
        lock (Gate)
        {
            if (Entries.TryGetValue(descriptor.QualifiedModuleId, out var existing))
            {
                if (ReferenceEquals(existing.Provider, provider))
                {
                    existing.Leases++;
                    return new AuraToolExtensionRegistrationResult
                    {
                        Success = true,
                        AlreadyRegistered = true,
                        Message = "Extension is already registered.",
                        Handle = new RegistrationHandle(
                            descriptor.QualifiedModuleId,
                            existing.Token)
                    };
                }
                return Fail(
                    "Extension identity is already owned by another provider: "
                    + descriptor.QualifiedModuleId);
            }

            registrationToken = ++token;
            changedRevision = ++revision;
            Entries[descriptor.QualifiedModuleId] = new Registered
            {
                Descriptor = descriptor,
                Provider = provider,
                Token = registrationToken,
                Revision = changedRevision,
                Leases = 1
            };
        }

        PublishChanged(changedRevision);
        return new AuraToolExtensionRegistrationResult
        {
            Success = true,
            Message = "Extension registered: " + descriptor.QualifiedModuleId,
            Handle = new RegistrationHandle(
                descriptor.QualifiedModuleId,
                registrationToken)
        };
    }

    public static IReadOnlyList<AuraToolExtensionRegistration> Snapshot()
    {
        lock (Gate)
        {
            return Entries.Values
                .OrderBy(entry => entry.Descriptor.Order)
                .ThenBy(entry => entry.Descriptor.QualifiedModuleId, StringComparer.Ordinal)
                .Select(entry => new AuraToolExtensionRegistration(
                    NormalizeDescriptor(entry.Descriptor),
                    entry.Provider,
                    entry.Revision))
                .ToArray();
        }
    }

    public static bool NotifyStateChanged(
        string ownerModId,
        string moduleId,
        IAuraToolExtensionProvider provider,
        long stateRevision)
    {
        var qualifiedModuleId = (ownerModId ?? "").Trim()
                                + ":"
                                + (moduleId ?? "").Trim();
        var normalizedRevision = Math.Max(0, stateRevision);
        lock (Gate)
        {
            if (!Entries.TryGetValue(qualifiedModuleId, out var existing)
                || !ReferenceEquals(existing.Provider, provider)
                || normalizedRevision <= existing.StateRevision)
            {
                return false;
            }
            existing.StateRevision = normalizedRevision;
        }
        PublishStateChanged(qualifiedModuleId, normalizedRevision);
        return true;
    }

    internal static void ClearForTests()
    {
        long changedRevision;
        lock (Gate)
        {
            Entries.Clear();
            changedRevision = ++revision;
        }
        PublishChanged(changedRevision);
    }

    private static void Unregister(string qualifiedModuleId, long registrationToken)
    {
        var changed = false;
        long changedRevision = 0;
        lock (Gate)
        {
            if (Entries.TryGetValue(qualifiedModuleId, out var existing)
                && existing.Token == registrationToken)
            {
                existing.Leases = Math.Max(0, existing.Leases - 1);
                if (existing.Leases == 0)
                {
                    Entries.Remove(qualifiedModuleId);
                    changedRevision = ++revision;
                    changed = true;
                }
            }
        }
        if (changed)
        {
            PublishChanged(changedRevision);
        }
    }

    private static AuraToolExtensionDescriptor NormalizeDescriptor(
        AuraToolExtensionDescriptor? value)
    {
        if (value == null)
        {
            throw new InvalidOperationException("Descriptor is missing.");
        }

        return new AuraToolExtensionDescriptor
        {
            ProtocolVersion = value.ProtocolVersion,
            MinimumSupportedProtocolVersion =
                value.MinimumSupportedProtocolVersion,
            OwnerModId = (value.OwnerModId ?? "").Trim(),
            ModuleId = (value.ModuleId ?? "").Trim(),
            CategoryId = string.IsNullOrWhiteSpace(value.CategoryId)
                ? AuraToolExtensionProtocol.DefaultCategoryId
                : value.CategoryId.Trim(),
            Order = Math.Max(0, Math.Min(10000, value.Order)),
            DisplayName = TrimTo(value.DisplayName, AuraToolExtensionProtocol.MaximumDisplayLength),
            Description = TrimTo(value.Description, AuraToolExtensionProtocol.MaximumDescriptionLength),
            IconKey = TrimTo(value.IconKey, AuraToolExtensionProtocol.MaximumDisplayLength),
            SearchTerms = (value.SearchTerms ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => TrimTo(item, AuraToolExtensionProtocol.MaximumDisplayLength))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(AuraToolExtensionProtocol.MaximumSearchTerms)
                .ToArray(),
            HasSettingsPage = value.HasSettingsPage,
            Experimental = value.Experimental,
            RequiresRestartWhenChanged = value.RequiresRestartWhenChanged
        };
    }

    private static string ValidateDescriptor(
        string ownerModId,
        AuraToolExtensionDescriptor descriptor)
    {
        var owner = (ownerModId ?? "").Trim();
        if (!AuraToolExtensionProtocol.IsCompatible(
                descriptor.ProtocolVersion,
                descriptor.MinimumSupportedProtocolVersion))
        {
            return "Unsupported AuraTooling protocol range: "
                   + descriptor.MinimumSupportedProtocolVersion
                   + ".."
                   + descriptor.ProtocolVersion;
        }
        if (!string.Equals(
                owner,
                descriptor.OwnerModId,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Registration owner does not match descriptor owner.";
        }
        if (!IsSafeId(descriptor.OwnerModId) || !IsSafeId(descriptor.ModuleId))
        {
            return "Owner and module ids may contain only letters, digits, '.', '_' and '-'.";
        }
        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            return "Extension display name is required.";
        }
        return "";
    }

    private static bool IsSafeId(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.All(character =>
                   char.IsLetterOrDigit(character)
                   || character == '.'
                   || character == '_'
                   || character == '-');
    }

    private static string TrimTo(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum ? text : text.Substring(0, maximum);
    }

    private static AuraToolExtensionRegistrationResult Fail(string message) => new()
    {
        Success = false,
        Message = message ?? ""
    };

    private static void PublishChanged(long changedRevision)
    {
        var handlers = Changed;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<long> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(changedRevision);
            }
            catch
            {
                // Registry ownership must survive a faulty consumer notification.
            }
        }
    }

    private static void PublishStateChanged(
        string qualifiedModuleId,
        long stateRevision)
    {
        var handlers = StateChanged;
        if (handlers == null)
        {
            return;
        }
        foreach (Action<string, long> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(qualifiedModuleId, stateRevision);
            }
            catch
            {
                // Provider state ownership must survive a faulty consumer notification.
            }
        }
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private string qualifiedModuleId;
        private long registrationToken;

        public RegistrationHandle(string qualifiedModuleId, long registrationToken)
        {
            this.qualifiedModuleId = qualifiedModuleId;
            this.registrationToken = registrationToken;
        }

        public void Dispose()
        {
            var tokenToRelease = Interlocked.Exchange(
                ref registrationToken,
                0);
            if (tokenToRelease == 0)
            {
                return;
            }
            var id = qualifiedModuleId;
            qualifiedModuleId = "";
            Unregister(id, tokenToRelease);
        }
    }

}
