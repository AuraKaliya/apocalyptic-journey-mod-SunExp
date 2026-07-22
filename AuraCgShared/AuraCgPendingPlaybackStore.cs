using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCg.Shared;

internal sealed class AuraCgPendingPlayback
{
    public string Key { get; set; } = "";

    public SkillCgPlaybackSnapshot Playback { get; set; } = new();

    public string Source { get; set; } = "";

    public bool RelayAfterApply { get; set; }

    public long ExpiresAtUtcTicks { get; set; }
}

internal sealed class AuraCgPendingPlaybackStore
{
    internal const int DefaultMaximumCount = 64;
    internal const int DefaultWaitMilliseconds = 2000;

    private readonly int maximumCount;
    private readonly Dictionary<string, AuraCgPendingPlayback> entries = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();

    public AuraCgPendingPlaybackStore(int maximumCount = DefaultMaximumCount)
    {
        this.maximumCount = Math.Max(1, maximumCount);
    }

    public int Count => entries.Count;

    public bool Enqueue(
        SkillCgPlaybackSnapshot playback,
        string source,
        bool relayAfterApply,
        long nowUtcTicks)
    {
        if (playback == null)
        {
            return false;
        }

        var key = Key(playback.IssuerPlayerId, playback.SkillCgPlayId);
        if (string.IsNullOrWhiteSpace(playback.SkillCgPlayId) || entries.ContainsKey(key))
        {
            return false;
        }

        entries[key] = new AuraCgPendingPlayback
        {
            Key = key,
            Playback = playback,
            Source = source ?? "",
            RelayAfterApply = relayAfterApply,
            ExpiresAtUtcTicks = nowUtcTicks + TimeSpan.TicksPerMillisecond * DefaultWaitMilliseconds
        };
        order.Enqueue(key);
        while (entries.Count > maximumCount && order.Count > 0)
        {
            entries.Remove(order.Dequeue());
        }

        return true;
    }

    public IReadOnlyList<AuraCgPendingPlayback> Snapshot()
    {
        return entries.Values
            .OrderBy(entry => entry.ExpiresAtUtcTicks)
            .ToArray();
    }

    public bool Remove(string key)
    {
        return entries.Remove(key ?? "");
    }

    public void Clear()
    {
        entries.Clear();
        order.Clear();
    }

    public static string Key(string issuerPlayerId, string playId)
    {
        return (issuerPlayerId ?? "") + "|" + (playId ?? "");
    }
}
