using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using GameUIManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.PixelEmoji;

public static class AuraToolsPixelEmojiRuntime
{
    private const string InjectedPrefix = "AuraToolsPixelEmoji-";
    private static readonly Dictionary<string, DateTime> LastServerSendByPlayer = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> ReceivedEvents = new(StringComparer.Ordinal);
    private static bool initialized;

    public static bool Enabled => AuraToolsConfigService.Root.PixelEmoji.Enabled
                                  && AuraToolsConfigService.PixelEmoji.Enabled;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        RemoveMissingFavorites();
        AuraToolsHookRegistry.After(modConfig, "EmojiPanelUI.CreateEmoji", AfterCreateEmoji, "PixelEmoji");
        AuraToolsHookRegistry.Before(modConfig, "UIAnimation.Play", BeforeUiAnimationPlay, "PixelEmoji");
    }

    public static bool SetFavorite(string itemId, bool favorite, out string error)
    {
        error = "";
        var settings = AuraToolsConfigService.PixelEmoji;
        settings.Normalize();
        settings.FavoriteIds.RemoveAll(value => string.Equals(value, itemId, StringComparison.OrdinalIgnoreCase));
        if (favorite)
        {
            if (PixelEmojiLibraryStore.Find(itemId) == null)
            {
                error = "作品不存在。";
                return false;
            }
            if (settings.FavoriteIds.Count >= settings.MaxFavorites)
            {
                error = "收藏已达到 " + settings.MaxFavorites + " 个上限。";
                return false;
            }

            settings.FavoriteIds.Add(itemId);
        }

        AuraToolsConfigService.SavePixelEmoji();
        return true;
    }

    public static bool Delete(string itemId, out string error)
    {
        if (!PixelEmojiLibraryStore.Delete(itemId, out error))
        {
            return false;
        }

        PixelEmojiAssetCache.RemoveItem(itemId);
        AuraToolsConfigService.PixelEmoji.FavoriteIds.RemoveAll(value => string.Equals(value, itemId, StringComparison.OrdinalIgnoreCase));
        AuraToolsConfigService.SavePixelEmoji();
        return true;
    }

    public static bool Send(PixelEmojiDocument document, out string error)
    {
        error = "";
        if (!Enabled)
        {
            error = "像素表情工坊尚未启用。";
            return false;
        }
        if (document == null || !document.TryReadFrames(out var frames))
        {
            error = "表情数据无效。";
            return false;
        }

        var manager = PlayerManager.Instance;
        if (manager == null || string.IsNullOrWhiteSpace(manager.PlayerId))
        {
            error = "当前不在可发送表情的冒险中。";
            return false;
        }

        var asset = PixelEmojiAssetCache.Get(document.Id, frames, document.PlaybackMode);
        DialogueManager.Instance?.ShowEmoji(manager.PlayerId, asset.Gif);

        if (!AuraToolsConfigService.PixelEmoji.SyncRemote)
        {
            return true;
        }

        var presentation = new PixelEmojiPresentation
        {
            EventId = Guid.NewGuid().ToString("N"),
            CreatedUtcTicks = DateTime.UtcNow.Ticks,
            IssuerPlayerId = manager.PlayerId,
            IssuerPlayerName = manager.playerInfo?.Name ?? "",
            FrameDurationMilliseconds = PixelEmojiAnimationCodec.FrameDurationMilliseconds,
            PlaybackMode = document.PlaybackMode,
            FramesBase64 = PixelEmojiAnimationCodec.EncodeFrames(frames),
            ContentHash = PixelEmojiAnimationCodec.Sha256(frames, document.PlaybackMode)
        };
        if (!AuraToolsRpcTransport.Send(
                manager,
                new AuraToolsPixelEmojiCommand(presentation),
                "PixelEmoji.Presentation",
                excludeOwner: true))
        {
            error = "本地已显示，但联机表情发送失败。";
            return false;
        }

        return true;
    }

    internal static bool AcceptOnServer(AuraToolsRpcSender sender, PixelEmojiPresentation presentation, out string rejection)
    {
        rejection = "";
        if (sender == null || !sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "发送者不是当前房间成员";
            return false;
        }
        if (!presentation.TryReadFrames(out _, out rejection))
        {
            return false;
        }

        var age = DateTime.UtcNow - new DateTime(Math.Max(DateTime.MinValue.Ticks, Math.Min(DateTime.MaxValue.Ticks, presentation.CreatedUtcTicks)), DateTimeKind.Utc);
        if (age.TotalSeconds > 10 || age.TotalSeconds < -3)
        {
            rejection = "表情事件已过期";
            return false;
        }

        lock (LastServerSendByPlayer)
        {
            Prune(LastServerSendByPlayer, TimeSpan.FromMinutes(2));
            if (LastServerSendByPlayer.TryGetValue(sender.PlayerId, out var last)
                && (DateTime.UtcNow - last).TotalSeconds < 1)
            {
                rejection = "表情发送过于频繁";
                return false;
            }
            LastServerSendByPlayer[sender.PlayerId] = DateTime.UtcNow;
        }

        presentation.IssuerPlayerId = sender.PlayerId;
        presentation.IssuerPlayerName = sender.PlayerName;
        presentation.RejectionReason = "";
        return true;
    }

    internal static void Receive(PixelEmojiPresentation presentation)
    {
        if (!Enabled || !AuraToolsConfigService.PixelEmoji.SyncRemote || presentation == null
            || !string.IsNullOrWhiteSpace(presentation.RejectionReason)
            || !presentation.TryReadFrames(out var frames, out _)
            || string.IsNullOrWhiteSpace(presentation.IssuerPlayerId))
        {
            return;
        }

        var key = presentation.IssuerPlayerId + ":" + presentation.EventId + ":" + presentation.ContentHash;
        lock (ReceivedEvents)
        {
            Prune(ReceivedEvents, TimeSpan.FromSeconds(30));
            if (ReceivedEvents.ContainsKey(key))
            {
                return;
            }
            ReceivedEvents[key] = DateTime.UtcNow;
        }

        var asset = PixelEmojiAssetCache.Get("remote-" + presentation.ContentHash, frames, presentation.PlaybackMode);
        DialogueManager.Instance?.ShowEmoji(presentation.IssuerPlayerId, asset.Gif);
    }

    private static void BeforeUiAnimationPlay(ModHookContext context)
    {
        if (context.Target is UIAnimation animation
            && PixelEmojiAssetCache.TryGetPlaybackMode(animation.GifAsset, out var playbackMode))
        {
            animation.Loop = playbackMode == PixelEmojiPlaybackMode.Loop;
        }
    }

    private static void AfterCreateEmoji(ModHookContext context)
    {
        if (context.Target is not EmojiPanelUI panel || panel.EmojiPrefab == null)
        {
            return;
        }

        var parent = panel.EmojiPrefab.parent;
        if (parent == null)
        {
            return;
        }
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index);
            if (child.name.StartsWith(InjectedPrefix, StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        if (!Enabled)
        {
            return;
        }

        var items = PixelEmojiLibraryStore.GetItems().ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        PixelEmojiAssetCache.RetainLocalItems(AuraToolsConfigService.PixelEmoji.FavoriteIds);
        foreach (var favoriteId in AuraToolsConfigService.PixelEmoji.FavoriteIds)
        {
            if (!items.TryGetValue(favoriteId, out var item)
                || !item.TryReadFrames(out var frames))
            {
                continue;
            }

            try
            {
                var asset = PixelEmojiAssetCache.Get(item.Id, frames, item.PlaybackMode);
                var clone = Object.Instantiate(panel.EmojiPrefab, parent, false);
                clone.name = InjectedPrefix + item.Id;
                clone.gameObject.SetActive(true);
                var animation = clone.Find("Icon")?.GetComponent<UIAnimation>();
                if (animation != null)
                {
                    animation.SetGif(asset.Gif);
                    animation.Loop = item.PlaybackMode == PixelEmojiPlaybackMode.Loop;
                    animation.AutoPlay = true;
                    animation.Play();
                }
                foreach (var originalButton in clone.GetComponentsInChildren<Button>(true))
                {
                    originalButton.enabled = false;
                }
                AddClickShield(clone, () =>
                {
                    if (!Send(item, out var sendError) && !string.IsNullOrWhiteSpace(sendError))
                    {
                        GameUIManager.Instance?.ShowTip(sendError);
                    }
                });
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[PixelEmoji] native list injection failed: " + ex.Message);
            }
        }
    }

    private static void AddClickShield(Transform parent, Action clicked)
    {
        var shield = new GameObject("AuraToolsPixelEmojiClick", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        shield.transform.SetParent(parent, false);
        shield.transform.SetAsLastSibling();
        var rect = shield.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = shield.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.001f);
        var button = shield.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => clicked());
    }

    private static void RemoveMissingFavorites()
    {
        var ids = new HashSet<string>(PixelEmojiLibraryStore.GetItems().Select(item => item.Id), StringComparer.OrdinalIgnoreCase);
        var settings = AuraToolsConfigService.PixelEmoji;
        var before = settings.FavoriteIds.Count;
        settings.FavoriteIds.RemoveAll(id => !ids.Contains(id));
        if (before != settings.FavoriteIds.Count)
        {
            AuraToolsConfigService.SavePixelEmoji();
        }
    }

    private static void Prune(Dictionary<string, DateTime> values, TimeSpan ttl)
    {
        var threshold = DateTime.UtcNow - ttl;
        foreach (var key in values.Where(pair => pair.Value < threshold).Select(pair => pair.Key).ToList())
        {
            values.Remove(key);
        }
    }
}

[Serializable]
public sealed class AuraToolsPixelEmojiCommand : RpcCommandBase, IAuraToolsServerBoundRpcCommand
{
    private AuraToolsRpcSender serverSender = AuraToolsRpcSender.Unbound;

    public AuraToolsPixelEmojiCommand()
    {
        Presentation = new PixelEmojiPresentation();
    }

    public AuraToolsPixelEmojiCommand(PixelEmojiPresentation presentation)
    {
        Presentation = presentation ?? new PixelEmojiPresentation();
    }

    public PixelEmojiPresentation Presentation { get; set; }

    public void BindServerSender(AuraToolsRpcSender sender)
    {
        serverSender = sender ?? AuraToolsRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Presentation ??= new PixelEmojiPresentation();
        if (!AuraToolsPixelEmojiRuntime.AcceptOnServer(serverSender, Presentation, out var rejection))
        {
            Presentation.RejectionReason = rejection;
            Presentation.FramesBase64 = new List<string>();
            Presentation.ContentHash = "";
            AuraToolsLog.Warn("[PixelEmoji] server rejected presentation: " + rejection);
        }
    }

    public override void RpcExecute()
    {
        Presentation ??= new PixelEmojiPresentation();
        AuraToolsPixelEmojiRuntime.Receive(Presentation);
    }
}

internal sealed class PixelEmojiAsset
{
    public string Hash { get; set; } = "";
    public PixelEmojiPlaybackMode PlaybackMode { get; set; } = PixelEmojiPlaybackMode.Loop;
    public List<Texture2D> Textures { get; set; } = new();
    public List<Sprite> Sprites { get; set; } = new();
    public GifAsset Gif { get; set; } = null!;
    public DateTime LastUsedUtc { get; set; }

    public int FrameCount => Sprites.Count;

    public Sprite FirstSprite => Sprites[0];
}

internal static class PixelEmojiAssetCache
{
    private static readonly Dictionary<string, PixelEmojiAsset> Assets = new(StringComparer.OrdinalIgnoreCase);
    private const int RemoteTargetAssets = 32;
    private const int RemoteMaximumAssets = 40;
    private const int RemoteTargetFrames = 96;
    private const int RemoteMaximumFrames = 128;

    public static PixelEmojiAsset Get(
        string itemId,
        IReadOnlyList<byte[]> frames,
        PixelEmojiPlaybackMode playbackMode)
    {
        var hash = PixelEmojiAnimationCodec.Sha256(frames, playbackMode);
        if (Assets.TryGetValue(itemId, out var existing) && string.Equals(existing.Hash, hash, StringComparison.Ordinal))
        {
            existing.LastUsedUtc = DateTime.UtcNow;
            return existing;
        }
        if (existing != null)
        {
            Destroy(existing);
            Assets.Remove(itemId);
        }

        var textures = new List<Texture2D>(frames.Count);
        var sprites = new List<Sprite>(frames.Count);
        try
        {
            for (var index = 0; index < frames.Count; index++)
            {
                var texture = CreateNativeTexture(itemId, index, frames[index], makeNoLongerReadable: true);
                textures.Add(texture);
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, PixelEmojiCodec.NativeSize, PixelEmojiCodec.NativeSize),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = texture.name;
                sprites.Add(sprite);
            }
        }
        catch
        {
            foreach (var sprite in sprites) if (sprite != null) Object.Destroy(sprite);
            foreach (var texture in textures) if (texture != null) Object.Destroy(texture);
            throw;
        }

        var gif = ScriptableObject.CreateInstance<GifAsset>();
        gif.name = "AuraToolsPixelEmoji-" + itemId;
        gif.gifPath = PlaybackPath(playbackMode, hash);
        gif.frames = sprites.ToList();
        gif.frameDelays = Enumerable.Repeat(PixelEmojiAnimationCodec.FrameDurationSeconds, sprites.Count).ToList();
        var created = new PixelEmojiAsset
        {
            Hash = hash,
            PlaybackMode = playbackMode,
            Textures = textures,
            Sprites = sprites,
            Gif = gif,
            LastUsedUtc = DateTime.UtcNow
        };
        Assets[itemId] = created;

        Prune();
        return created;
    }

    public static List<byte[]> EncodePngSequence(IReadOnlyList<byte[]> frames)
    {
        if (!PixelEmojiAnimationCodec.IsValidFrames(frames))
        {
            throw new ArgumentException("Pixel emoji animation frames are invalid.", nameof(frames));
        }

        var result = new List<byte[]>(frames.Count);
        for (var index = 0; index < frames.Count; index++)
        {
            var texture = CreateNativeTexture("Export", index, frames[index], makeNoLongerReadable: false);
            try
            {
                result.Add(texture.EncodeToPNG());
            }
            finally
            {
                Object.Destroy(texture);
            }
        }
        return result;
    }

    public static bool TryGetPlaybackMode(GifAsset? gif, out PixelEmojiPlaybackMode playbackMode)
    {
        playbackMode = PixelEmojiPlaybackMode.Loop;
        var path = gif?.gifPath ?? "";
        if (path.StartsWith("AuraToolsExp/PixelEmoji/loop/", StringComparison.Ordinal))
        {
            return true;
        }
        if (path.StartsWith("AuraToolsExp/PixelEmoji/once/", StringComparison.Ordinal))
        {
            playbackMode = PixelEmojiPlaybackMode.Once;
            return true;
        }
        return false;
    }

    public static void RetainLocalItems(IReadOnlyCollection<string> itemIds)
    {
        var retained = new HashSet<string>(itemIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in Assets.Keys
                     .Where(key => !key.StartsWith("remote-", StringComparison.OrdinalIgnoreCase) && !retained.Contains(key))
                     .ToList())
        {
            Destroy(Assets[key]);
            Assets.Remove(key);
        }
    }

    public static void RemoveItem(string itemId)
    {
        if (Assets.TryGetValue(itemId, out var asset))
        {
            Destroy(asset);
            Assets.Remove(itemId);
        }
    }

    private static void Prune()
    {
        var remoteKeys = Assets.Keys
            .Where(key => key.StartsWith("remote-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => Assets[key].LastUsedUtc)
            .ToList();
        var frameCount = remoteKeys.Sum(key => Assets[key].FrameCount);
        if (remoteKeys.Count <= RemoteMaximumAssets && frameCount <= RemoteMaximumFrames)
        {
            return;
        }

        var index = 0;
        while ((remoteKeys.Count - index > RemoteTargetAssets || frameCount > RemoteTargetFrames)
               && index < remoteKeys.Count)
        {
            var key = remoteKeys[index++];
            frameCount -= Assets[key].FrameCount;
            Destroy(Assets[key]);
            Assets.Remove(key);
        }
    }

    private static void Destroy(PixelEmojiAsset asset)
    {
        if (asset.Gif != null) Object.Destroy(asset.Gif);
        foreach (var sprite in asset.Sprites) if (sprite != null) Object.Destroy(sprite);
        foreach (var texture in asset.Textures) if (texture != null) Object.Destroy(texture);
        asset.Sprites.Clear();
        asset.Textures.Clear();
    }

    private static Texture2D CreateNativeTexture(string itemId, int frameIndex, byte[] pixels, bool makeNoLongerReadable)
    {
        var texture = new Texture2D(PixelEmojiCodec.NativeSize, PixelEmojiCodec.NativeSize, TextureFormat.RGBA32, false)
        {
            name = "AuraToolsPixelEmoji-" + itemId + "-" + (frameIndex + 1),
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.LoadRawTextureData(PixelEmojiCodec.ExpandToNativeRgba(pixels));
        texture.Apply(false, makeNoLongerReadable);
        return texture;
    }

    private static string PlaybackPath(PixelEmojiPlaybackMode playbackMode, string hash)
    {
        return "AuraToolsExp/PixelEmoji/"
               + (playbackMode == PixelEmojiPlaybackMode.Loop ? "loop/" : "once/")
               + hash;
    }
}
