using System.Collections.Generic;

namespace AuraOnline.Shared;

public sealed class AuraChatStickerSpec
{
    public AuraChatStickerSpec(string packId, string stickerId, string resourcePath)
    {
        PackId = packId;
        StickerId = stickerId;
        ResourcePath = resourcePath;
    }

    public string PackId { get; }

    public string StickerId { get; }

    public string ResourcePath { get; }
}

public static class AuraChatStickerRegistry
{
    private static readonly Dictionary<string, AuraChatStickerSpec> Stickers = new Dictionary<string, AuraChatStickerSpec>();

    public static void Register(string packId, string stickerId, string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(stickerId) || string.IsNullOrWhiteSpace(resourcePath))
        {
            return;
        }

        Stickers[Key(packId, stickerId)] = new AuraChatStickerSpec(packId.Trim(), stickerId.Trim(), resourcePath.Trim());
    }

    public static AuraChatStickerSpec? Resolve(string packId, string stickerId)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(stickerId))
        {
            return null;
        }

        return Stickers.TryGetValue(Key(packId, stickerId), out var spec) ? spec : null;
    }

    public static void Clear()
    {
        Stickers.Clear();
    }

    private static string Key(string packId, string stickerId)
    {
        return packId.Trim().ToLowerInvariant() + ":" + stickerId.Trim().ToLowerInvariant();
    }
}
