using System;

namespace Terrias.Dll.Mechanics;

internal readonly struct SolarMemoryRoleCommitResolution
{
    public SolarMemoryRoleCommitResolution(bool matched, bool accepted)
    {
        Matched = matched;
        Accepted = accepted;
    }

    public bool Matched { get; }

    public bool Accepted { get; }
}

internal sealed class SolarMemoryRoleCommitPendingState
{
    private readonly object gate = new();
    private string playerId = "";
    private string token = "";

    public bool IsPending(string candidatePlayerId, string candidateToken)
    {
        lock (gate)
        {
            return Matches(candidatePlayerId, candidateToken);
        }
    }

    public bool TryBegin(string candidatePlayerId, string candidateToken)
    {
        if (string.IsNullOrWhiteSpace(candidatePlayerId) || string.IsNullOrWhiteSpace(candidateToken))
        {
            return false;
        }

        lock (gate)
        {
            if (!string.IsNullOrEmpty(token))
            {
                return Matches(candidatePlayerId, candidateToken);
            }

            playerId = candidatePlayerId;
            token = candidateToken;
            return true;
        }
    }

    public SolarMemoryRoleCommitResolution Resolve(
        string candidatePlayerId,
        string candidateToken,
        bool accepted)
    {
        lock (gate)
        {
            if (!Matches(candidatePlayerId, candidateToken))
            {
                return default;
            }

            playerId = "";
            token = "";
            return new SolarMemoryRoleCommitResolution(true, accepted);
        }
    }

    public bool Cancel(string candidatePlayerId, string candidateToken)
    {
        return Resolve(candidatePlayerId, candidateToken, accepted: false).Matched;
    }

    private bool Matches(string candidatePlayerId, string candidateToken)
    {
        return !string.IsNullOrEmpty(token)
               && string.Equals(playerId, candidatePlayerId, StringComparison.Ordinal)
               && string.Equals(token, candidateToken, StringComparison.Ordinal);
    }
}
