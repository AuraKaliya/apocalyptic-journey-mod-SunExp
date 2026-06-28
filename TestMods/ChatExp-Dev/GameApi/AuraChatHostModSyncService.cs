using AuraOnline.Shared;
using ChatExp.Dll.Infrastructure;

namespace ChatExp.Dll.GameApi;

public static class AuraChatHostModSyncService
{
    private static readonly AuraOnlineHostModSyncSession Session = new(ChatExpIds.ModId, ChatExpLog.Info, ChatExpLog.Warn);

    static AuraChatHostModSyncService()
    {
        Session.Changed += () => AuraChatRuntime.SetModSyncActionStatus(Session.ActionStatus);
    }

    public static bool IsRunning => Session.IsRunning;

    public static int CountPendingActions(AuraChatModSyncState? state)
    {
        return Session.CountPendingActions(state);
    }

    public static void StartSync()
    {
        Session.StartSync(AuraChatRuntime.ModSyncState);
    }
}
