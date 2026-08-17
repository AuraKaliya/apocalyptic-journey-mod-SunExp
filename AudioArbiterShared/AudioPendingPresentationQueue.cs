using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioArbiter.Shared;

internal sealed class AudioPendingPresentation
{
    public string Key { get; set; } = "";

    public SoundPlaybackRequest Request { get; set; } = new();

    public bool SyncRemote { get; set; }

    public long ExpiresAtUtcTicks { get; set; }
}

internal sealed class AudioPendingPresentationQueue
{
    internal const int DefaultMaximumCount = 128;
    internal const int DefaultWaitMilliseconds = 2000;

    private readonly int maximumCount;
    private readonly Dictionary<string, AudioPendingPresentation> entries = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();

    public AudioPendingPresentationQueue(int maximumCount = DefaultMaximumCount)
    {
        this.maximumCount = Math.Max(1, maximumCount);
    }

    public int Count => entries.Count;

    public bool Enqueue(SoundPlaybackRequest request, long nowUtcTicks, bool syncRemote = false)
    {
        if (request == null)
        {
            return false;
        }

        var key = AudioNetworkPolicy.PresentationDedupeKey(request);
        if (string.IsNullOrWhiteSpace(key) || entries.ContainsKey(key))
        {
            return false;
        }

        var expiresAt = nowUtcTicks + TimeSpan.TicksPerMillisecond * DefaultWaitMilliseconds;
        if (request.CreatedAtUtcTicks > 0 && request.MaxAgeMilliseconds > 0)
        {
            var payloadExpiry = request.CreatedAtUtcTicks
                                + TimeSpan.TicksPerMillisecond * request.MaxAgeMilliseconds;
            expiresAt = Math.Min(expiresAt, payloadExpiry);
        }

        entries[key] = new AudioPendingPresentation
        {
            Key = key,
            Request = request,
            SyncRemote = syncRemote,
            ExpiresAtUtcTicks = expiresAt
        };
        order.Enqueue(key);
        while (entries.Count > maximumCount && order.Count > 0)
        {
            entries.Remove(order.Dequeue());
        }

        return true;
    }

    public IReadOnlyList<AudioPendingPresentation> Snapshot()
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
}
