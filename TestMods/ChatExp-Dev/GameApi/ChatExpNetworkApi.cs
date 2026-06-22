using System;
using AuraOnline.Shared;
using ChatExp.Dll.Infrastructure;
using ChatExp.Dll.Network;

namespace ChatExp.Dll.GameApi;

public static class ChatExpNetworkApi
{
    public static bool SendPlayerText(string text)
    {
        var limited = AuraChatTextLimiter.LimitPlayerText(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(limited))
        {
            return false;
        }

        var manager = PlayerManager.Instance;
        if (manager == null)
        {
            ChatExpLog.Warn("PlayerManager is not available; chat send skipped.");
            return false;
        }

        manager.SendRpcCommand(new AuraChatSubmitCommand(limited, manager.PlayerId, Guid.NewGuid().ToString("N")));
        return true;
    }
}
