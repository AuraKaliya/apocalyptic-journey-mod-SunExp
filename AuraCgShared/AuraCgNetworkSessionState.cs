using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCg.Shared;

internal sealed class AuraCgNetworkSessionState
{
    private const float MinimumLocalActionReuseSeconds = 0.35f;
    private readonly Dictionary<string, string> recentLocalPlayIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> recentLocalPlayTimes = new(StringComparer.Ordinal);
    private readonly AuraCgPlaybackClaimStore playbackClaims;
    private long localPlaybackCounter;
    private string fightToken = "";

    public AuraCgNetworkSessionState(int maximumPlaybackClaims)
    {
        playbackClaims = new AuraCgPlaybackClaimStore(maximumPlaybackClaims);
    }

    public string FightToken => fightToken;

    public int RecentLocalActionCount => recentLocalPlayIds.Count;

    public void SetFightToken(string value)
    {
        fightToken = (value ?? "").Trim();
    }

    public string ReuseOrCreateLocalPlayId(
        string issuerPlayerId,
        string ownerInstanceId,
        string cardId,
        long actionSequence,
        string eventToken,
        float now,
        float duplicateWindowSeconds)
    {
        var reuseWindow = NormalizeReuseWindow(duplicateWindowSeconds);
        PruneRecentLocalPlayIds(now, reuseWindow);
        var key = LocalActionKey(ownerInstanceId, cardId, actionSequence, eventToken);
        if (recentLocalPlayIds.TryGetValue(key, out var existing)
            && recentLocalPlayTimes.TryGetValue(key, out var lastTime)
            && now - lastTime <= reuseWindow)
        {
            recentLocalPlayTimes[key] = now;
            return existing;
        }

        var playId = SanitizeTokenPart(issuerPlayerId)
                     + ":" + SanitizeTokenPart(ownerInstanceId)
                     + ":" + SanitizeTokenPart(cardId)
                     + ":" + (++localPlaybackCounter).ToString()
                     + ":" + fightToken;
        recentLocalPlayIds[key] = playId;
        recentLocalPlayTimes[key] = now;
        return playId;
    }

    public bool TryClaimPlayback(string issuerPlayerId, string playId, out string key)
    {
        return playbackClaims.TryClaim(issuerPlayerId, playId, out key);
    }

    public void ResetTransient()
    {
        recentLocalPlayIds.Clear();
        recentLocalPlayTimes.Clear();
        playbackClaims.Clear();
        fightToken = "";
    }

    public static float NormalizeReuseWindow(float duplicateWindowSeconds)
    {
        return Math.Min(2f, Math.Max(MinimumLocalActionReuseSeconds, duplicateWindowSeconds));
    }

    private void PruneRecentLocalPlayIds(float now, float reuseWindow)
    {
        if (recentLocalPlayTimes.Count == 0)
        {
            return;
        }

        foreach (var key in recentLocalPlayTimes
                     .Where(pair => now - pair.Value > reuseWindow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            recentLocalPlayTimes.Remove(key);
            recentLocalPlayIds.Remove(key);
        }
    }

    private static string LocalActionKey(string ownerInstanceId, string cardId, long actionSequence, string eventToken)
    {
        return (ownerInstanceId ?? "").Trim()
               + "|" + (cardId ?? "").Trim()
               + "|" + actionSequence.ToString()
               + "|" + (eventToken ?? "").Trim();
    }

    private static string SanitizeTokenPart(string value)
    {
        var clean = new string((value ?? "")
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '*' ? ch : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "none" : clean;
    }
}
