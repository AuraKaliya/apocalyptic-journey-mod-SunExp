using System;

namespace SunExp.Dll.Infrastructure;

public static class SunExpFrameDispatcher
{
    private static readonly object SyncRoot = new();
    private static Func<string, Action, bool>? runOnceNextFrame;

    public static void Register(Func<string, Action, bool> dispatcher)
    {
        if (dispatcher == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            runOnceNextFrame = dispatcher;
        }
    }

    public static bool RunOnceNextFrame(string key, Action action)
    {
        if (action == null)
        {
            return false;
        }

        Func<string, Action, bool>? dispatcher;
        lock (SyncRoot)
        {
            dispatcher = runOnceNextFrame;
        }

        if (dispatcher != null)
        {
            return dispatcher(key, action);
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Immediate SunExp frame action failed: " + key, ex);
        }

        return true;
    }
}
