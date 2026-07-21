using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class StarScoreArrivalCue
{
    public StarScoreArrivalCue(
        long sequence,
        StarScoreNote note,
        int slotIndex,
        bool completesCadence,
        string ownerStatusId,
        DateTime createdUtc)
    {
        Sequence = sequence;
        Note = note;
        SlotIndex = Math.Max(0, Math.Min(2, slotIndex));
        CompletesCadence = completesCadence;
        OwnerStatusId = ownerStatusId ?? "";
        CreatedUtc = createdUtc;
    }

    public long Sequence { get; }

    public StarScoreNote Note { get; }

    public int SlotIndex { get; }

    public bool CompletesCadence { get; }

    public string OwnerStatusId { get; }

    public DateTime CreatedUtc { get; }
}

public static class StarScoreArrivalCueService
{
    public const int MaxVisibleRibbonCount = 3;
    public static readonly TimeSpan CueTtl = TimeSpan.FromSeconds(2);

    private const int MaxPendingKeys = 64;
    private const int MaxCuesPerKey = 12;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<StarScoreArrivalCue>> Pending = new(StringComparer.Ordinal);
    private static long nextSequence;

    public static void Record(
        IDataConfig? cardConfig,
        StarScoreNote note,
        int slotIndex,
        bool completesCadence,
        string ownerStatusId)
    {
        var key = Key(cardConfig);
        if (key.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            PruneNoLock(DateTime.UtcNow);
            if (!Pending.TryGetValue(key, out var cues))
            {
                cues = new List<StarScoreArrivalCue>();
                Pending[key] = cues;
            }

            cues.Add(new StarScoreArrivalCue(
                ++nextSequence,
                note,
                slotIndex,
                completesCadence,
                ownerStatusId,
                DateTime.UtcNow));
            if (cues.Count > MaxCuesPerKey)
            {
                cues.RemoveRange(0, cues.Count - MaxCuesPerKey);
            }

            TrimKeysNoLock();
        }
    }

    public static IReadOnlyList<StarScoreArrivalCue> Consume(IDataConfig? cardConfig)
    {
        var key = Key(cardConfig);
        if (key.Length == 0)
        {
            return Array.Empty<StarScoreArrivalCue>();
        }

        lock (Gate)
        {
            PruneNoLock(DateTime.UtcNow);
            if (!Pending.TryGetValue(key, out var cues))
            {
                return Array.Empty<StarScoreArrivalCue>();
            }

            Pending.Remove(key);
            return cues.OrderBy(cue => cue.Sequence).ToList().AsReadOnly();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Pending.Clear();
        }
    }

    private static string Key(IDataConfig? config)
    {
        if (config == null)
        {
            return "";
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(config.InstanceID))
            {
                return "instance:" + config.InstanceID;
            }
        }
        catch
        {
            // Fall back to the stable content id for synthetic/legacy configs.
        }

        var id = CardConfigApi.Id(config);
        return string.IsNullOrWhiteSpace(id) ? "" : "card:" + id;
    }

    private static void PruneNoLock(DateTime now)
    {
        foreach (var key in Pending
                     .Where(pair => pair.Value.Count == 0 || now - pair.Value[pair.Value.Count - 1].CreatedUtc > CueTtl)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            Pending.Remove(key);
        }
    }

    private static void TrimKeysNoLock()
    {
        if (Pending.Count <= MaxPendingKeys)
        {
            return;
        }

        foreach (var key in Pending
                     .OrderBy(pair => pair.Value.Count == 0 ? DateTime.MinValue : pair.Value[pair.Value.Count - 1].CreatedUtc)
                     .Take(Pending.Count - MaxPendingKeys)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            Pending.Remove(key);
        }
    }
}
