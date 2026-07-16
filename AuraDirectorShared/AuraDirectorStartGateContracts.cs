using System;
using System.Collections.Generic;

namespace AuraDirector.Shared;

public sealed class AuraDirectorCapabilityProbeResult
{
    public string ContractId { get; set; } = AuraDirectorProtocol.ContractId;

    public int ProtocolVersion { get; set; } = AuraDirectorProtocol.CurrentRuntimeProtocolVersion;

    public int MinimumSupportedProtocolVersion { get; set; } = AuraDirectorProtocol.MinimumSupportedRuntimeProtocolVersion;

    public string ProviderId { get; set; } = "";

    public bool Supported { get; set; }

    public string Code { get; set; } = "";

    public string Detail { get; set; } = "";

    public Dictionary<string, string> Details { get; set; } = new(StringComparer.Ordinal);
}

public interface IAuraDirectorNativeStartHold
{
    string BackendId { get; }

    object NativeTarget { get; }

    bool IsReleased { get; }

    string ReleaseReason { get; }

    bool TryRelease(string reason);
}

public interface IAuraDirectorNativeStartHoldSink
{
    bool TryAccept(IAuraDirectorNativeStartHold hold);
}

public interface IAuraDirectorStartGateProvider
{
    string ProviderId { get; }

    AuraDirectorCapabilityProbeResult ProbeCapability();

    AuraDirectorCapabilityProbeResult Install(IAuraDirectorNativeStartHoldSink sink);

    int Uninstall(string releaseReason = "provider-uninstall");
}

public interface IAuraDirectorRequestSource
{
    string SourceId { get; }

    int Priority { get; }

    AuraDirectorRequest? BuildRequest(object nativeBattleTarget, long battleSessionId);
}
