using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AuraCg.Shared;

internal delegate SkillCgRequest? AuraCgProviderRequestFactory(
    object? source,
    string providerId,
    string ownerModId,
    int priority,
    SkillCgTriggerContext context);

internal enum AuraCgProviderRegistrationStatus
{
    Registered,
    NullProvider,
    EmptyProviderId,
    Failed
}

internal sealed class AuraCgProviderRegistrationResult
{
    private AuraCgProviderRegistrationResult(
        AuraCgProviderRegistrationStatus status,
        string providerType,
        string providerId,
        string description,
        string error)
    {
        Status = status;
        ProviderType = providerType;
        ProviderId = providerId;
        Description = description;
        Error = error;
    }

    public AuraCgProviderRegistrationStatus Status { get; }

    public string ProviderType { get; }

    public string ProviderId { get; }

    public string Description { get; }

    public string Error { get; }

    public static AuraCgProviderRegistrationResult Registered(AuraCgProviderHandle handle)
    {
        return new AuraCgProviderRegistrationResult(
            AuraCgProviderRegistrationStatus.Registered,
            handle.ProviderTypeName,
            handle.ProviderId,
            handle.Describe(),
            "");
    }

    public static AuraCgProviderRegistrationResult NullProvider()
    {
        return new AuraCgProviderRegistrationResult(
            AuraCgProviderRegistrationStatus.NullProvider,
            "<null>",
            "",
            "",
            "");
    }

    public static AuraCgProviderRegistrationResult EmptyProviderId(string providerType)
    {
        return new AuraCgProviderRegistrationResult(
            AuraCgProviderRegistrationStatus.EmptyProviderId,
            providerType,
            "",
            "",
            "");
    }

    public static AuraCgProviderRegistrationResult Failed(string providerType, Exception exception)
    {
        return new AuraCgProviderRegistrationResult(
            AuraCgProviderRegistrationStatus.Failed,
            providerType,
            "",
            "",
            exception.Message);
    }
}

internal sealed class AuraCgProviderBuildFailure
{
    public AuraCgProviderBuildFailure(string providerId, Exception exception)
    {
        ProviderId = providerId;
        Exception = exception;
    }

    public string ProviderId { get; }

    public Exception Exception { get; }
}

internal sealed class AuraCgProviderCoordinator
{
    private readonly AuraCgProviderRequestFactory requestFactory;
    private readonly List<AuraCgProviderHandle> providers = new();

    public AuraCgProviderCoordinator(AuraCgProviderRequestFactory requestFactory)
    {
        this.requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
    }

    public int ProviderCount => providers.Count;

    public AuraCgProviderRegistrationResult Register(object? provider)
    {
        if (provider == null)
        {
            return AuraCgProviderRegistrationResult.NullProvider();
        }

        var providerType = provider.GetType().FullName ?? provider.GetType().Name;
        try
        {
            var handle = new AuraCgProviderHandle(provider);
            if (string.IsNullOrWhiteSpace(handle.ProviderId))
            {
                return AuraCgProviderRegistrationResult.EmptyProviderId(providerType);
            }

            providers.RemoveAll(item => string.Equals(
                item.QualifiedProviderId,
                handle.QualifiedProviderId,
                StringComparison.OrdinalIgnoreCase));
            providers.Add(handle);
            providers.Sort(CompareProviders);
            return AuraCgProviderRegistrationResult.Registered(handle);
        }
        catch (Exception ex)
        {
            return AuraCgProviderRegistrationResult.Failed(providerType, ex);
        }
    }

    public List<SkillCgRequest> BuildRequests(
        SkillCgTriggerContext context,
        Action<AuraCgProviderBuildFailure>? onFailure = null)
    {
        var output = new List<SkillCgRequest>();
        foreach (var provider in providers)
        {
            provider.AppendRequests(context, output, requestFactory, onFailure);
        }

        output.Sort(CompareRequests);
        return output;
    }

    private static int CompareProviders(AuraCgProviderHandle left, AuraCgProviderHandle right)
    {
        var priority = right.Priority.CompareTo(left.Priority);
        return priority != 0
            ? priority
            : string.Compare(left.QualifiedProviderId, right.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareRequests(SkillCgRequest left, SkillCgRequest right)
    {
        var action = left.ActionSequence.CompareTo(right.ActionSequence);
        if (action != 0)
        {
            return action;
        }

        var priority = right.Priority.CompareTo(left.Priority);
        return priority != 0
            ? priority
            : string.Compare(left.QualifiedProviderId, right.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class AuraCgProviderHandle
{
    private readonly object provider;
    private readonly Type providerType;

    public AuraCgProviderHandle(object provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        providerType = provider.GetType();
        ProviderId = ReadString("ProviderId", providerType.FullName ?? "unknown");
        OwnerModId = ReadString("OwnerModId", "");
        if (string.IsNullOrWhiteSpace(OwnerModId))
        {
            OwnerModId = providerType.Assembly.GetName().Name ?? "";
        }

        QualifiedProviderId = QualifyProviderId(OwnerModId, ProviderId);
        Priority = ReadInt("Priority", 0);
    }

    public string ProviderTypeName => providerType.FullName ?? providerType.Name;

    public string ProviderId { get; }

    public string OwnerModId { get; }

    public string QualifiedProviderId { get; }

    public int Priority { get; }

    public void AppendRequests(
        SkillCgTriggerContext context,
        List<SkillCgRequest> output,
        AuraCgProviderRequestFactory requestFactory,
        Action<AuraCgProviderBuildFailure>? onFailure)
    {
        try
        {
            var method = providerType.GetMethod("BuildRequests", BindingFlags.Instance | BindingFlags.Public);
            var value = method?.Invoke(provider, new object[] { context });
            if (value is not IEnumerable items)
            {
                return;
            }

            foreach (var item in items)
            {
                var request = requestFactory(item, QualifiedProviderId, OwnerModId, Priority, context);
                if (request != null)
                {
                    output.Add(request);
                }
            }
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(new AuraCgProviderBuildFailure(ProviderId, ex));
        }
    }

    public string Describe()
    {
        return "providerId=" + ProviderId
               + ", qualifiedProviderId=" + QualifiedProviderId
               + ", owner=" + OwnerModId
               + ", priority=" + Priority;
    }

    private string ReadString(string name, string fallback)
    {
        try
        {
            return providerType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider) as string ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private int ReadInt(string name, int fallback)
    {
        try
        {
            var value = providerType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider);
            return value is int typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string QualifyProviderId(string ownerModId, string providerId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = (providerId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "unknown";
        }

        if (id.Contains(":") || string.IsNullOrWhiteSpace(owner))
        {
            return id;
        }

        return owner + ":" + id;
    }
}
