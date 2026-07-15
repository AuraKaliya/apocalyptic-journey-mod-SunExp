using System;
using System.Collections.Generic;

namespace AuraDirector.Shared;

public enum AuraDirectorActorKind
{
    Other = 0,
    Player = 1,
    Enemy = 2,
    Boss = 3
}

public enum AuraDirectorActorSide
{
    Neutral = 0,
    Friendly = 1,
    Hostile = 2
}

public enum AuraDirectorBlockingMode
{
    None = 0,
    InputOnly = 1,
    InputAndProgression = 2
}

public enum AuraDirectorFailurePolicy
{
    ContinueWithSilentCue = 0,
    SkipActor = 1,
    AbortPlan = 2
}

public enum AuraDirectorCueKind
{
    Wait = 0,
    Letterbox = 1,
    PortraitSlide = 2
}

public enum AuraDirectorDirection
{
    None = 0,
    RightToLeft = 1,
    LeftToRight = 2
}

[Serializable]
public sealed class AuraDirectorResourceRef
{
    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string ResourceId { get; set; } = "";

    public string VariantId { get; set; } = "";

    public AuraDirectorResourceRef Normalized()
    {
        return new AuraDirectorResourceRef
        {
            ProviderId = Clean(ProviderId),
            OwnerModId = Clean(OwnerModId),
            ResourceId = Clean(ResourceId),
            VariantId = Clean(VariantId)
        };
    }

    internal static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }
}

[Serializable]
public sealed class AuraDirectorActorRef
{
    public string ActorKey { get; set; } = "";

    public AuraDirectorActorKind ActorKind { get; set; }

    public AuraDirectorActorSide Side { get; set; }

    public string OwnerPlayerId { get; set; } = "";

    public string ContentOwnerModId { get; set; } = "";

    public string ContentId { get; set; } = "";

    public AuraDirectorResourceRef Resource { get; set; } = new();

    public AuraDirectorActorRef Normalized()
    {
        return new AuraDirectorActorRef
        {
            ActorKey = AuraDirectorResourceRef.Clean(ActorKey),
            ActorKind = ActorKind,
            Side = Side,
            OwnerPlayerId = AuraDirectorResourceRef.Clean(OwnerPlayerId),
            ContentOwnerModId = AuraDirectorResourceRef.Clean(ContentOwnerModId),
            ContentId = AuraDirectorResourceRef.Clean(ContentId),
            Resource = (Resource ?? new AuraDirectorResourceRef()).Normalized()
        };
    }
}

[Serializable]
public sealed class AuraDirectorStrategyRef
{
    public string StrategyId { get; set; } = AuraDirectorPlanCompiler.AlternatingPortraitStrategyId;

    public int StrategyVersion { get; set; } = AuraDirectorPlanCompiler.AlternatingPortraitStrategyVersion;

    public string ProfileId { get; set; } = AuraDirectorPlanCompiler.DefaultOpeningProfileId;

    public AuraDirectorStrategyRef Normalized()
    {
        return new AuraDirectorStrategyRef
        {
            StrategyId = AuraDirectorResourceRef.Clean(StrategyId),
            StrategyVersion = StrategyVersion,
            ProfileId = AuraDirectorResourceRef.Clean(ProfileId)
        };
    }
}

[Serializable]
public sealed class AuraDirectorRequest
{
    public string OwnerModId { get; set; } = "";

    public string RequestId { get; set; } = "";

    public long BattleSessionId { get; set; }

    public List<AuraDirectorActorRef> Actors { get; set; } = new();

    public AuraDirectorStrategyRef Strategy { get; set; } = new();

    public AuraDirectorBlockingMode BlockingMode { get; set; } = AuraDirectorBlockingMode.InputAndProgression;

    public AuraDirectorFailurePolicy FailurePolicy { get; set; } = AuraDirectorFailurePolicy.ContinueWithSilentCue;

    public double HardTimeoutSeconds { get; set; } = 20d;
}

[Serializable]
public sealed class AuraDirectorCue
{
    public string CueId { get; set; } = "";

    public string TrackId { get; set; } = "";

    public AuraDirectorCueKind CueKind { get; set; }

    public string ActorKey { get; set; } = "";

    public double StartSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public int Layer { get; set; }

    public AuraDirectorDirection Direction { get; set; }

    public AuraDirectorResourceRef Resource { get; set; } = new();

    public double EnterSeconds { get; set; }

    public double HoldSeconds { get; set; }

    public double ExitSeconds { get; set; }

    public double FocusBarRatio { get; set; }

    public double StartXRatio { get; set; }

    public double FocusXRatio { get; set; }

    public double EndXRatio { get; set; }
}

[Serializable]
public sealed class AuraDirectorPlanDescriptor
{
    public int ProtocolVersion { get; set; } = AuraDirectorPlanCompiler.CurrentProtocolVersion;

    public string OwnerModId { get; set; } = "";

    public string RequestId { get; set; } = "";

    public long BattleSessionId { get; set; }

    public List<AuraDirectorActorRef> Actors { get; set; } = new();

    public AuraDirectorStrategyRef Strategy { get; set; } = new();

    public AuraDirectorBlockingMode BlockingMode { get; set; }

    public AuraDirectorFailurePolicy FailurePolicy { get; set; }

    public double HardTimeoutSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public string PlanHash { get; set; } = "";
}

public sealed class AuraDirectorCompileResult
{
    private AuraDirectorCompileResult(bool success, string rejectionCode, AuraDirectorPlanDescriptor? descriptor, IReadOnlyList<AuraDirectorCue> cues)
    {
        Success = success;
        RejectionCode = rejectionCode;
        Descriptor = descriptor;
        Cues = cues;
    }

    public bool Success { get; }

    public string RejectionCode { get; }

    public AuraDirectorPlanDescriptor? Descriptor { get; }

    public IReadOnlyList<AuraDirectorCue> Cues { get; }

    public static AuraDirectorCompileResult Accepted(AuraDirectorPlanDescriptor descriptor, IReadOnlyList<AuraDirectorCue> cues)
    {
        return new AuraDirectorCompileResult(true, "", descriptor, cues);
    }

    public static AuraDirectorCompileResult Rejected(string code)
    {
        return new AuraDirectorCompileResult(false, AuraDirectorResourceRef.Clean(code), null, Array.Empty<AuraDirectorCue>());
    }
}
