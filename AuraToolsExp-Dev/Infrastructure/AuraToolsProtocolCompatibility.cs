using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Infrastructure;

public sealed class AuraToolsProtocolContract
{
    private readonly HashSet<string> supportedCapabilities;

    public AuraToolsProtocolContract(
        string featureId,
        int currentVersion,
        int minimumSupportedVersion,
        IEnumerable<string>? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(featureId))
        {
            throw new ArgumentException("A feature id is required.", nameof(featureId));
        }
        if (currentVersion <= 0
            || minimumSupportedVersion <= 0
            || minimumSupportedVersion > currentVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSupportedVersion),
                "The supported protocol range must be positive and ordered.");
        }

        FeatureId = featureId.Trim();
        CurrentVersion = currentVersion;
        MinimumSupportedVersion = minimumSupportedVersion;
        supportedCapabilities = new HashSet<string>(
            NormalizeCapabilities(capabilities),
            StringComparer.OrdinalIgnoreCase);
    }

    public string FeatureId { get; }

    public int CurrentVersion { get; }

    public int MinimumSupportedVersion { get; }

    public IReadOnlyCollection<string> SupportedCapabilities =>
        supportedCapabilities;

    public bool SupportsVersion(int version)
    {
        return version >= MinimumSupportedVersion
               && version <= CurrentVersion;
    }

    public AuraToolsProtocolNegotiation Negotiate(
        int remoteCurrentVersion,
        int remoteMinimumSupportedVersion = 0,
        IEnumerable<string>? remoteRequiredCapabilities = null)
    {
        var remoteMinimum = remoteMinimumSupportedVersion > 0
            ? remoteMinimumSupportedVersion
            : remoteCurrentVersion;
        if (remoteCurrentVersion <= 0
            || remoteMinimum <= 0
            || remoteMinimum > remoteCurrentVersion)
        {
            return AuraToolsProtocolNegotiation.Reject(
                FeatureId,
                "远端协议范围无效。");
        }

        var negotiatedVersion = Math.Min(CurrentVersion, remoteCurrentVersion);
        if (negotiatedVersion < MinimumSupportedVersion
            || negotiatedVersion < remoteMinimum)
        {
            return AuraToolsProtocolNegotiation.Reject(
                FeatureId,
                "本机与远端没有重叠的协议版本。");
        }

        var required = NormalizeCapabilities(remoteRequiredCapabilities)
            .ToArray();
        if (remoteCurrentVersion > CurrentVersion && required.Length == 0)
        {
            return AuraToolsProtocolNegotiation.Reject(
                FeatureId,
                "更新的协议必须显式声明必要能力。");
        }

        var missing = required
            .Where(capability => !supportedCapabilities.Contains(capability))
            .ToArray();
        if (missing.Length > 0)
        {
            return AuraToolsProtocolNegotiation.Reject(
                FeatureId,
                "缺少必要能力：" + string.Join("、", missing),
                missing);
        }

        return AuraToolsProtocolNegotiation.Accept(
            FeatureId,
            negotiatedVersion,
            negotiatedVersion != CurrentVersion
            || negotiatedVersion != remoteCurrentVersion);
    }

    private static IEnumerable<string> NormalizeCapabilities(
        IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AuraToolsProtocolNegotiation
{
    public string FeatureId { get; private set; } = "";

    public bool Compatible { get; private set; }

    public bool Degraded { get; private set; }

    public int NegotiatedVersion { get; private set; }

    public string Message { get; private set; } = "";

    public IReadOnlyList<string> MissingCapabilities { get; private set; } =
        Array.Empty<string>();

    internal static AuraToolsProtocolNegotiation Accept(
        string featureId,
        int negotiatedVersion,
        bool degraded)
    {
        return new AuraToolsProtocolNegotiation
        {
            FeatureId = featureId,
            Compatible = true,
            Degraded = degraded,
            NegotiatedVersion = negotiatedVersion,
            Message = degraded ? "使用兼容协议运行。" : "协议完全兼容。"
        };
    }

    internal static AuraToolsProtocolNegotiation Reject(
        string featureId,
        string message,
        IReadOnlyList<string>? missingCapabilities = null)
    {
        return new AuraToolsProtocolNegotiation
        {
            FeatureId = featureId,
            Message = message ?? "协议不兼容。",
            MissingCapabilities = missingCapabilities ?? Array.Empty<string>()
        };
    }
}

public sealed class AuraToolsPeerModState
{
    public string PlayerId { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public bool ToolEnabled { get; set; }
}

public sealed class AuraToolsPeerCompatibilityResult
{
    public bool Compatible { get; set; }

    public IReadOnlyList<string> MissingPeers { get; set; } =
        Array.Empty<string>();
}

public static class AuraToolsPeerCompatibility
{
    public static AuraToolsPeerCompatibilityResult Evaluate(
        IEnumerable<AuraToolsPeerModState>? peers)
    {
        var values = (peers ?? Array.Empty<AuraToolsPeerModState>())
            .Where(peer => peer != null)
            .ToArray();
        if (values.Length <= 1)
        {
            return new AuraToolsPeerCompatibilityResult
            {
                Compatible = true
            };
        }

        var missingStates = values
            .Where(peer => !peer.ToolEnabled)
            .ToArray();
        var missing = missingStates
            .Select(peer => string.IsNullOrWhiteSpace(peer.PlayerName)
                ? string.IsNullOrWhiteSpace(peer.PlayerId)
                    ? "未知玩家"
                    : peer.PlayerId
                : peer.PlayerName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new AuraToolsPeerCompatibilityResult
        {
            Compatible = missingStates.Length == 0,
            MissingPeers = missing
        };
    }
}
