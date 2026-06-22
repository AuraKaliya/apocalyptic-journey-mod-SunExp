using System;

namespace AuraOnline.Shared;

public static class AuraChatRuntime
{
    private static readonly AuraChatLocalStore Store = new(80);
    private static string ownerModId = "";
    private static int sequence;

    public static event Action? Changed;

    public static event Action? StatusChanged;

    public static int MaxMessages
    {
        get => Store.MaxMessages;
        set => Store.MaxMessages = Math.Max(1, value);
    }

    public static string ModSyncStatus { get; private set; } = "等待联机玩家信息。";

    public static System.Collections.Generic.IReadOnlyList<AuraChatMessage> Messages => Store.Messages;

    public static void Initialize(string modId, int maxMessages = 80)
    {
        ownerModId = string.IsNullOrWhiteSpace(modId) ? "Unknown" : modId.Trim();
        MaxMessages = maxMessages;
    }

    public static AuraChatMessage ConfirmCatalogMessage(string senderId, string senderName, string contentKind, string contentId)
    {
        if (!AuraChatCatalogStore.TryResolveContent(contentKind, contentId, AuraChatCatalogStore.CatalogHash, out var rawText, out var reason))
        {
            throw new InvalidOperationException("Cannot confirm catalog message: " + reason);
        }

        return new AuraChatMessage
        {
            MessageId = ownerModId + ":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ":" + (++sequence),
            Sequence = sequence,
            Area = AuraChatAreas.Chat,
            Kind = contentKind,
            SenderId = senderId ?? "",
            SenderName = senderName ?? "",
            OwnerModId = ownerModId,
            RawText = rawText,
            ContentKind = contentKind ?? "",
            ContentId = contentId ?? "",
            CatalogHash = AuraChatCatalogStore.CatalogHash,
            ServerTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public static void Receive(AuraChatMessage message)
    {
        if (!AuraChatCatalogStore.TryNormalizeIncoming(message, out var normalized, out _))
        {
            return;
        }

        if (Store.Add(normalized))
        {
            Changed?.Invoke();
        }
    }

    public static void ClearMessages()
    {
        Store.Clear();
        Changed?.Invoke();
    }

    public static void SetModSyncStatus(string status)
    {
        ModSyncStatus = AuraChatTextLimiter.WrapPlainText(AuraChatTextLimiter.LimitSystemLine(status));
        StatusChanged?.Invoke();
    }
}
