using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioArbiter.Shared;

internal enum AudioPresentationClaimResult
{
    Claimed,
    NotPresentation,
    FightSessionNotReady,
    StaleFightSession,
    Duplicate
}

internal sealed class AudioNetworkSessionState
{
    private const float RecentLocalPlayRetentionSeconds = 1f;
    private readonly int maximumPlaybackClaims;
    private readonly HashSet<string> receivedEventIds = new(StringComparer.Ordinal);
    private readonly Queue<string> receivedEventOrder = new();
    private readonly Dictionary<string, string> recentLocalPlayIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> recentLocalPlayTimes = new(StringComparer.Ordinal);
    private long localPlaybackCounter;
    private string fightToken = "";

    public AudioNetworkSessionState(int maximumPlaybackClaims)
    {
        this.maximumPlaybackClaims = Math.Max(1, maximumPlaybackClaims);
    }

    public string FightToken => fightToken;

    public int PlaybackClaimCount => receivedEventIds.Count;

    public int RecentLocalPlayCount => recentLocalPlayIds.Count;

    public void SetFightToken(string value)
    {
        fightToken = (value ?? "").Trim();
    }

    public void ResetTransient()
    {
        receivedEventIds.Clear();
        receivedEventOrder.Clear();
        recentLocalPlayIds.Clear();
        recentLocalPlayTimes.Clear();
        localPlaybackCounter = 0;
        fightToken = "";
    }

    public void ApplyFightToken(string value)
    {
        ResetTransient();
        SetFightToken(value);
    }

    public AudioPresentationClaimResult TryClaimPresentation(
        SoundPlaybackRequest request,
        bool isMultiplayerSession)
    {
        if (!AudioNetworkPolicy.IsCardUsePresentation(request))
        {
            return AudioPresentationClaimResult.NotPresentation;
        }

        if (isMultiplayerSession)
        {
            if (string.IsNullOrWhiteSpace(fightToken))
            {
                return AudioPresentationClaimResult.FightSessionNotReady;
            }

            if (!string.Equals(request.FightToken, fightToken, StringComparison.Ordinal))
            {
                return AudioPresentationClaimResult.StaleFightSession;
            }
        }

        var key = AudioNetworkPolicy.PresentationDedupeKey(request);
        if (!receivedEventIds.Add(key))
        {
            return AudioPresentationClaimResult.Duplicate;
        }

        receivedEventOrder.Enqueue(key);
        while (receivedEventOrder.Count > maximumPlaybackClaims)
        {
            receivedEventIds.Remove(receivedEventOrder.Dequeue());
        }

        return AudioPresentationClaimResult.Claimed;
    }

    public string ReuseOrCreateLocalPlayId(
        string actionKey,
        string issuerPlayerId,
        float now,
        float reuseSeconds)
    {
        actionKey ??= "";
        if (recentLocalPlayIds.TryGetValue(actionKey, out var existing)
            && recentLocalPlayTimes.TryGetValue(actionKey, out var last)
            && now - last <= reuseSeconds)
        {
            recentLocalPlayTimes[actionKey] = now;
            return existing;
        }

        var issuer = string.IsNullOrWhiteSpace(issuerPlayerId) ? "solo" : issuerPlayerId;
        var id = issuer + ":audio:" + (++localPlaybackCounter) + ":" + fightToken;
        recentLocalPlayIds[actionKey] = id;
        recentLocalPlayTimes[actionKey] = now;
        foreach (var stale in recentLocalPlayTimes
                     .Where(pair => now - pair.Value > RecentLocalPlayRetentionSeconds)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            recentLocalPlayTimes.Remove(stale);
            recentLocalPlayIds.Remove(stale);
        }

        return id;
    }
}
