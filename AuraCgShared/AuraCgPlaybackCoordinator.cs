using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal enum AuraCgPlaybackEnqueueResult
{
    Accepted,
    Invalid,
    EmptyMedia,
    Duplicate
}

internal sealed class AuraCgPlaybackCoordinator
{
    private readonly List<QueuedRequest> queue = new();
    private readonly Dictionary<string, float> recentKeys = new(StringComparer.Ordinal);
    private long enqueueSequence;
    private int generation;
    private bool playing;

    public int Generation => generation;

    public int QueueCount => queue.Count;

    public int RecentKeyCount => recentKeys.Count;

    public bool IsPlaying => playing;

    public bool IsCurrent(int candidateGeneration)
    {
        return candidateGeneration == generation;
    }

    public AuraCgPlaybackEnqueueResult TryEnqueue(
        SkillCgRequest? request,
        float now,
        int maximumQueueLength,
        float duplicateWindowSeconds,
        out int droppedCount)
    {
        droppedCount = 0;
        if (request == null)
        {
            return AuraCgPlaybackEnqueueResult.Invalid;
        }

        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return AuraCgPlaybackEnqueueResult.EmptyMedia;
        }

        var normalizedWindow = Math.Max(0f, duplicateWindowSeconds);
        PruneRecentKeys(now, normalizedWindow);
        var duplicateKey = request.DuplicateKey;
        if (recentKeys.TryGetValue(duplicateKey, out var lastTime)
            && now - lastTime <= normalizedWindow)
        {
            return AuraCgPlaybackEnqueueResult.Duplicate;
        }

        recentKeys[duplicateKey] = now;
        queue.Add(new QueuedRequest(request, ++enqueueSequence));
        queue.Sort(QueuedRequest.CompareForQueue);

        var normalizedMaximum = Math.Max(1, maximumQueueLength);
        if (queue.Count > normalizedMaximum)
        {
            droppedCount = queue.Count - normalizedMaximum;
            queue.RemoveRange(0, droppedCount);
        }

        return AuraCgPlaybackEnqueueResult.Accepted;
    }

    public bool TryBegin(out int playbackGeneration)
    {
        playbackGeneration = generation;
        if (playing || queue.Count == 0)
        {
            return false;
        }

        playing = true;
        return true;
    }

    public bool TryTakeNext(
        int playbackGeneration,
        float now,
        float maximumRequestAgeSeconds,
        out SkillCgRequest? request,
        out int staleSkipped)
    {
        request = null;
        staleSkipped = 0;
        if (!playing || playbackGeneration != generation)
        {
            return false;
        }

        var normalizedMaximumAge = Math.Max(0f, maximumRequestAgeSeconds);
        while (queue.Count > 0)
        {
            var item = queue[0];
            queue.RemoveAt(0);
            if (now - item.Request.CreatedAt > normalizedMaximumAge)
            {
                staleSkipped++;
                continue;
            }

            request = item.Request;
            return true;
        }

        return false;
    }

    public bool Complete(int playbackGeneration)
    {
        if (playbackGeneration != generation)
        {
            return false;
        }

        playing = false;
        return true;
    }

    public void Clear()
    {
        generation++;
        queue.Clear();
        recentKeys.Clear();
        playing = false;
    }

    private void PruneRecentKeys(float now, float duplicateWindowSeconds)
    {
        if (recentKeys.Count == 0)
        {
            return;
        }

        var expired = new List<string>();
        foreach (var item in recentKeys)
        {
            if (now - item.Value > duplicateWindowSeconds)
            {
                expired.Add(item.Key);
            }
        }

        foreach (var key in expired)
        {
            recentKeys.Remove(key);
        }
    }

    private readonly struct QueuedRequest
    {
        public QueuedRequest(SkillCgRequest request, long enqueueSequence)
        {
            Request = request;
            EnqueueSequence = enqueueSequence;
        }

        public SkillCgRequest Request { get; }

        private long EnqueueSequence { get; }

        public static int CompareForQueue(QueuedRequest a, QueuedRequest b)
        {
            var actionCompare = a.Request.ActionSequence.CompareTo(b.Request.ActionSequence);
            if (actionCompare != 0)
            {
                return actionCompare;
            }

            var priorityCompare = b.Request.Priority.CompareTo(a.Request.Priority);
            return priorityCompare != 0 ? priorityCompare : a.EnqueueSequence.CompareTo(b.EnqueueSequence);
        }
    }
}
