using System;
using UnityEngine;

namespace Terrias.Dll.Infrastructure;

public static class TerriasCombatUiWorkload
{
    private const int DefaultSettleFrames = 2;
    private static readonly object SyncRoot = new();
    private static int activeDepth;
    private static int busyUntilFrame;
    private static string lastSource = "";

    public static bool IsBusy
    {
        get
        {
            lock (SyncRoot)
            {
                var frame = SafeFrameCount();
                if (frame < 0)
                {
                    return activeDepth > 0;
                }

                return busyUntilFrame >= frame;
            }
        }
    }

    public static int BusyFramesRemaining
    {
        get
        {
            lock (SyncRoot)
            {
                var frame = SafeFrameCount();
                if (activeDepth > 0)
                {
                    return frame < 0 ? DefaultSettleFrames : Math.Max(0, busyUntilFrame - frame + 1);
                }

                return frame < 0 ? 0 : Math.Max(0, busyUntilFrame - frame + 1);
            }
        }
    }

    public static string LastSource
    {
        get
        {
            lock (SyncRoot)
            {
                return lastSource;
            }
        }
    }

    public static void Begin(string source, int settleFrames = DefaultSettleFrames)
    {
        lock (SyncRoot)
        {
            activeDepth++;
            MarkBusyNoLock(source, settleFrames);
        }
    }

    public static void End(string source, int settleFrames = DefaultSettleFrames)
    {
        lock (SyncRoot)
        {
            activeDepth = Math.Max(0, activeDepth - 1);
            MarkBusyNoLock(source, settleFrames);
        }
    }

    public static void MarkBusy(string source, int settleFrames = DefaultSettleFrames)
    {
        lock (SyncRoot)
        {
            MarkBusyNoLock(source, settleFrames);
        }
    }

    private static void MarkBusyNoLock(string source, int settleFrames)
    {
        var frame = SafeFrameCount();
        if (frame >= 0)
        {
            busyUntilFrame = Math.Max(busyUntilFrame, frame + Math.Max(1, settleFrames));
        }

        lastSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
    }

    private static int SafeFrameCount()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return -1;
        }
    }
}
