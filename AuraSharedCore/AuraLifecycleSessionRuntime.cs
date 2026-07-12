namespace AuraShared.Core;

public static class AuraLifecycleSessionRuntime
{
    private static readonly object Gate = new();
    private static long battleSessionId;
    private static bool battleSessionActive;

    public static long CurrentBattleSessionId
    {
        get
        {
            lock (Gate)
            {
                return battleSessionId;
            }
        }
    }

    public static long EnsureBattleSession()
    {
        lock (Gate)
        {
            if (!battleSessionActive)
            {
                battleSessionId++;
                if (battleSessionId <= 0)
                {
                    battleSessionId = 1;
                }

                battleSessionActive = true;
            }

            return battleSessionId;
        }
    }

    public static bool BeginBattleSession()
    {
        lock (Gate)
        {
            if (battleSessionActive)
            {
                return false;
            }

            battleSessionId++;
            if (battleSessionId <= 0)
            {
                battleSessionId = 1;
            }

            battleSessionActive = true;
            return true;
        }
    }

    public static long RestartBattleSession()
    {
        lock (Gate)
        {
            battleSessionId++;
            if (battleSessionId <= 0)
            {
                battleSessionId = 1;
            }

            battleSessionActive = true;
            return battleSessionId;
        }
    }

    public static void EndBattleSession()
    {
        lock (Gate)
        {
            battleSessionActive = false;
        }
    }
}
