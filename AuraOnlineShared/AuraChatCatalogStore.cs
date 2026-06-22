using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuraOnline.Shared;

public static class AuraChatCatalogStore
{
    private static readonly List<AuraChatCatalogMessage> MessagesInternal = new();
    private static readonly List<AuraChatCatalogSticker> StickersInternal = new();
    private static readonly Dictionary<string, AuraChatCatalogMessage> MessagesById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AuraChatCatalogSticker> StickersById = new(StringComparer.Ordinal);

    public static bool IsReady { get; private set; }

    public static string CatalogId { get; private set; } = "";

    public static string CatalogVersion { get; private set; } = "";

    public static string CatalogHash { get; private set; } = "";

    public static string Status { get; private set; } = "消息资源尚未加载。";

    public static IReadOnlyList<AuraChatCatalogMessage> Messages => MessagesInternal;

    public static IReadOnlyList<AuraChatCatalogSticker> Stickers => StickersInternal;

    public static bool LoadEncrypted(
        string filePath,
        string signPublicKeyXml,
        string decryptPrivateKeyXml,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        Clear();
        try
        {
            var catalog = AuraChatCatalogCrypto.LoadEncryptedCatalog(filePath, signPublicKeyXml, decryptPrivateKeyXml, out var hash);
            ValidateCatalog(catalog, Path.GetDirectoryName(filePath) ?? "");
            CatalogId = catalog.CatalogId.Trim();
            CatalogVersion = catalog.CatalogVersion.Trim();
            CatalogHash = hash;

            foreach (var message in catalog.Messages.OrderBy(message => message.Order).ThenBy(message => message.Id, StringComparer.Ordinal))
            {
                MessagesInternal.Add(message);
                MessagesById[message.Id] = message;
            }

            AuraChatStickerRegistry.Clear();
            foreach (var sticker in catalog.Stickers.OrderBy(sticker => sticker.Order).ThenBy(sticker => sticker.Id, StringComparer.Ordinal))
            {
                StickersInternal.Add(sticker);
                StickersById[sticker.Id] = sticker;
                AuraChatStickerRegistry.Register(sticker.PackId, sticker.StickerId, sticker.ResourcePath);
            }

            IsReady = true;
            Status = "消息资源已通过 RSA 签名校验: " + CatalogVersion;
            info?.Invoke(Status);
            return true;
        }
        catch (Exception ex)
        {
            Clear();
            Status = "消息资源校验失败，聊天发送已禁用。";
            warn?.Invoke(Status + " " + ex.Message);
            return false;
        }
    }

    public static bool TryResolveContent(string contentKind, string contentId, string catalogHash, out string rawText, out string reason)
    {
        rawText = "";
        reason = "";
        if (!IsReady)
        {
            reason = "catalog unavailable";
            return false;
        }

        if (!string.Equals(catalogHash, CatalogHash, StringComparison.OrdinalIgnoreCase))
        {
            reason = "catalog hash mismatch";
            return false;
        }

        if (string.Equals(contentKind, AuraChatKinds.PresetMessage, StringComparison.Ordinal))
        {
            if (!MessagesById.TryGetValue(contentId ?? "", out var message))
            {
                reason = "unknown preset message";
                return false;
            }

            rawText = message.Text;
            return true;
        }

        if (string.Equals(contentKind, AuraChatKinds.Sticker, StringComparison.Ordinal))
        {
            if (!StickersById.TryGetValue(contentId ?? "", out var sticker))
            {
                reason = "unknown sticker";
                return false;
            }

            rawText = AuraChatEmojiParser.StickerFallback(sticker.PackId, sticker.StickerId);
            return true;
        }

        reason = "unsupported content kind";
        return false;
    }

    public static bool TryNormalizeIncoming(AuraChatMessage message, out AuraChatMessage normalized, out string reason)
    {
        normalized = message;
        if (message == null)
        {
            reason = "empty message";
            return false;
        }

        if (!TryResolveContent(message.ContentKind, message.ContentId, message.CatalogHash, out var rawText, out reason))
        {
            return false;
        }

        normalized = new AuraChatMessage
        {
            MessageId = message.MessageId,
            Sequence = message.Sequence,
            Area = message.Area,
            Kind = message.Kind,
            SenderId = message.SenderId,
            SenderName = message.SenderName,
            OwnerModId = message.OwnerModId,
            RawText = rawText,
            ContentKind = message.ContentKind,
            ContentId = message.ContentId,
            CatalogHash = message.CatalogHash,
            ServerTimeMs = message.ServerTimeMs
        };
        reason = "";
        return true;
    }

    private static void Clear()
    {
        IsReady = false;
        CatalogId = "";
        CatalogVersion = "";
        CatalogHash = "";
        MessagesInternal.Clear();
        StickersInternal.Clear();
        MessagesById.Clear();
        StickersById.Clear();
        AuraChatStickerRegistry.Clear();
    }

    private static void ValidateCatalog(AuraChatCatalog catalog, string catalogDirectory)
    {
        catalog.Messages ??= new List<AuraChatCatalogMessage>();
        catalog.Stickers ??= new List<AuraChatCatalogSticker>();

        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Unsupported chat catalog schema version.");
        }

        if (string.IsNullOrWhiteSpace(catalog.CatalogId))
        {
            throw new InvalidOperationException("Chat catalog id is empty.");
        }

        var messageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in catalog.Messages)
        {
            message.Id = NormalizeId(message.Id, "message id");
            if (!messageIds.Add(message.Id))
            {
                throw new InvalidOperationException("Duplicate chat message id: " + message.Id);
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                throw new InvalidOperationException("Chat message text is empty: " + message.Id);
            }
        }

        var stickerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sticker in catalog.Stickers)
        {
            sticker.Id = NormalizeId(sticker.Id, "sticker id");
            sticker.PackId = NormalizeId(sticker.PackId, "sticker pack id");
            sticker.StickerId = NormalizeId(sticker.StickerId, "sticker item id");
            if (!stickerIds.Add(sticker.Id))
            {
                throw new InvalidOperationException("Duplicate chat sticker id: " + sticker.Id);
            }

            if (string.IsNullOrWhiteSpace(sticker.ResourcePath))
            {
                throw new InvalidOperationException("Sticker resource path is empty: " + sticker.Id);
            }

            VerifyStickerHash(catalogDirectory, sticker);
        }

        if (messageIds.Count == 0 && stickerIds.Count == 0)
        {
            throw new InvalidOperationException("Chat catalog has no allowed content.");
        }
    }

    private static void VerifyStickerHash(string catalogDirectory, AuraChatCatalogSticker sticker)
    {
        if (string.IsNullOrWhiteSpace(sticker.Sha256))
        {
            return;
        }

        var fullPath = Path.Combine(catalogDirectory, sticker.ResourcePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Sticker resource not found.", fullPath);
        }

        var actual = AuraChatCatalogCrypto.Sha256Hex(File.ReadAllBytes(fullPath));
        if (!string.Equals(actual, sticker.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sticker resource hash mismatch: " + sticker.Id);
        }
    }

    private static string NormalizeId(string value, string label)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Chat catalog " + label + " is empty.");
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-' && ch != '.')
            {
                throw new InvalidOperationException("Chat catalog " + label + " contains invalid characters: " + value);
            }
        }

        return value;
    }
}
