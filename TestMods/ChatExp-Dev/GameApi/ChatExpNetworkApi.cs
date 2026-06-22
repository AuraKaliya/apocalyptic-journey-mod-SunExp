using AuraOnline.Shared;
using ChatExp.Dll.Infrastructure;
using ChatExp.Dll.Network;

namespace ChatExp.Dll.GameApi;

public static class ChatExpNetworkApi
{
    public static bool SendPresetMessage(string messageId)
    {
        return SendCatalogContent(AuraChatKinds.PresetMessage, messageId);
    }

    public static bool SendSticker(string stickerId)
    {
        return SendCatalogContent(AuraChatKinds.Sticker, stickerId);
    }

    private static bool SendCatalogContent(string contentKind, string contentId)
    {
        if (!AuraChatCatalogStore.IsReady)
        {
            ChatExpLog.Warn("Chat catalog is not ready; send skipped.");
            return false;
        }

        if (!AuraChatCatalogStore.TryResolveContent(contentKind, contentId, AuraChatCatalogStore.CatalogHash, out _, out var reason))
        {
            ChatExpLog.Warn("Chat content rejected before send: " + reason);
            return false;
        }

        var manager = PlayerManager.Instance;
        if (manager == null)
        {
            ChatExpLog.Warn("PlayerManager is not available; chat send skipped.");
            return false;
        }

        manager.SendRpcCommand(new AuraChatSubmitCommand(contentKind, contentId, AuraChatCatalogStore.CatalogHash, manager.PlayerId));
        return true;
    }
}
