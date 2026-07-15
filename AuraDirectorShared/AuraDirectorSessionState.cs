using System;
using System.Collections.Generic;

namespace AuraDirector.Shared;

public enum AuraDirectorSessionState
{
    Created = 0,
    Preparing = 1,
    Ready = 2,
    Scheduled = 3,
    Playing = 4,
    Completing = 5,
    Releasing = 6,
    Released = 7
}

public sealed class AuraDirectorSessionStateMachine
{
    private static readonly IReadOnlyDictionary<AuraDirectorSessionState, AuraDirectorSessionState> Next =
        new Dictionary<AuraDirectorSessionState, AuraDirectorSessionState>
        {
            [AuraDirectorSessionState.Created] = AuraDirectorSessionState.Preparing,
            [AuraDirectorSessionState.Preparing] = AuraDirectorSessionState.Ready,
            [AuraDirectorSessionState.Ready] = AuraDirectorSessionState.Scheduled,
            [AuraDirectorSessionState.Scheduled] = AuraDirectorSessionState.Playing,
            [AuraDirectorSessionState.Playing] = AuraDirectorSessionState.Completing,
            [AuraDirectorSessionState.Completing] = AuraDirectorSessionState.Releasing,
            [AuraDirectorSessionState.Releasing] = AuraDirectorSessionState.Released
        };

    private readonly object gate = new();
    private AuraDirectorSessionState state = AuraDirectorSessionState.Created;
    private string releaseReason = "";

    public AuraDirectorSessionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public string ReleaseReason
    {
        get
        {
            lock (gate)
            {
                return releaseReason;
            }
        }
    }

    public bool IsReleased => State == AuraDirectorSessionState.Released;

    public bool TryAdvance(AuraDirectorSessionState expectedNext)
    {
        lock (gate)
        {
            if (!Next.TryGetValue(state, out var next) || next != expectedNext)
            {
                return false;
            }
            state = next;
            return true;
        }
    }

    public bool TryBeginRelease(string reason)
    {
        lock (gate)
        {
            if (state == AuraDirectorSessionState.Releasing || state == AuraDirectorSessionState.Released)
            {
                return false;
            }
            releaseReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
            state = AuraDirectorSessionState.Releasing;
            return true;
        }
    }

    public bool TryMarkReleased()
    {
        return TryAdvance(AuraDirectorSessionState.Released);
    }
}
