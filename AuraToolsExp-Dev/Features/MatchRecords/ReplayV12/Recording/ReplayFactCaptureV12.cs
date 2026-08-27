using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.CardVisual;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using UnityEngine;
using Witch;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Recording;

internal sealed class ReplayCapturedActionSourceV12
{
    internal string Kind { get; set; } = ReplayTransactionKindsV12.Card;
    internal string IssuerPlayerId { get; set; } = "";
    internal string ActorId { get; set; } = "";
    internal string SourceInstanceId { get; set; } = "";
    internal string DescriptorId { get; set; } = "";
    internal string Label { get; set; } = "";
    internal string AnimationState { get; set; } = "Idle";
    internal string EffectDescriptorId { get; set; } = "";
    internal string SourceZone { get; set; } = "";
    internal int SourceSlot { get; set; } = -1;
}

internal sealed class ReplayCaptureCatalogV12
{
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<string, ReplayAssetV12> assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ReplayEntityDescriptorV12> entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayCardDescriptorV12> cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayBuffDescriptorV12> buffs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayIntentDescriptorV12> intents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayEffectDescriptorV12> effects = new(StringComparer.Ordinal);

    internal ReplayPresentationCapsuleV12 Capsule => new()
    {
        Scene = ReplayCanonicalJsonV12.Clone(Scene),
        Entities = entities.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .Select(ReplayCanonicalJsonV12.Clone).ToList(),
        Cards = cards.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .Select(ReplayCanonicalJsonV12.Clone).ToList(),
        Buffs = buffs.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .Select(ReplayCanonicalJsonV12.Clone).ToList(),
        Intents = intents.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .Select(ReplayCanonicalJsonV12.Clone).ToList(),
        Effects = effects.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .Select(ReplayCanonicalJsonV12.Clone).ToList()
    };

    internal ReplaySceneDescriptorV12 Scene { get; } = CreateSceneDescriptor();

    internal List<ReplayAssetV12> Assets => assets.Values.OrderBy(item => item.Sha256, StringComparer.Ordinal)
        .Select(ReplayCanonicalJsonV12.CloneAssetWithPayload).ToList();

    internal void CaptureBackground(GameObject? background)
    {
        var sprite = background == null
            ? null
            : background.GetComponentsInChildren<SpriteRenderer>(includeInactive: true)
                .Where(item => item?.sprite != null)
                .OrderByDescending(item => item.sprite.rect.width * item.sprite.rect.height)
                .Select(item => item.sprite)
                .FirstOrDefault();
        Scene.BackgroundAssetSha256 = sprite == null
            ? CaptureFallbackTexture("battle-background", "Background", 64, 36)
            : CaptureTexture(sprite.texture, "Background", required: true);
    }

    internal ReplayEntityDescriptorV12 RegisterEntity(
        StatusManager? status,
        string archetype,
        IDataConfig? config,
        string stableId)
    {
        var provenance = Provenance(EntityContentKind(archetype), stableId);
        var descriptorId = DescriptorId("entity", provenance);
        if (entities.TryGetValue(descriptorId, out var existing)) return existing;
        var descriptor = new ReplayEntityDescriptorV12
        {
            DescriptorId = descriptorId,
            Archetype = archetype,
            Provenance = provenance,
            Name = First(Read(config?.Vars, "Name"), Localize(config, "Name"), Read(config?.data, "Name"), stableId),
            Subtitle = First(Read(config?.Vars, "Tag"), Read(config?.data, "Tag"), EntityContentKind(archetype))
        };
        CaptureAnimations(status, descriptor);
        if (descriptor.Animations.Count == 0)
        {
            var fallback = CaptureFallbackTexture(descriptorId, "Entity.Fallback", 128, 128);
            descriptor.Animations.Add(new ReplayAnimationDescriptorV12
            {
                State = "Idle",
                Loop = true,
                Frames = new List<ReplaySpriteFrameV12>
                {
                    new() { AssetSha256 = fallback, RectWidth = 128, RectHeight = 128 }
                }
            });
        }
        entities.Add(descriptorId, descriptor);
        return descriptor;
    }

    internal ReplayCardDescriptorV12 RegisterCard(IDataConfig? config, string stableId)
    {
        var provenance = Provenance("Card", stableId);
        var descriptorId = DescriptorId("card", provenance);
        if (cards.TryGetValue(descriptorId, out var existing)) return existing;
        var data = config?.data;
        var vars = config?.Vars;
        var artwork = CaptureResource(First(
            Read(vars, "CardImage"),
            Read(data, "CardImage"),
            Read(vars, "Picture"),
            Read(data, "Picture"),
            Read(vars, "Icon"),
            Read(data, "Icon")), "Card.Artwork");
        if (artwork.Length == 0) artwork = CaptureFallbackTexture(descriptorId, "Card.Fallback", 256, 360);
        var visual = config == null
            ? new Dictionary<string, string>()
            : AuraToolsCardVisualRuntime.CaptureReplaySnapshot(config)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var descriptor = new ReplayCardDescriptorV12
        {
            DescriptorId = descriptorId,
            Provenance = provenance,
            Name = First(Read(vars, "Name"), Localize(config, "Name"), Read(data, "Name"), Read(data, "DisplayName"), stableId),
            Description = First(
                Read(vars, "Description"),
                Localize(config, "Description"),
                Read(data, "Description"),
                string.Join("\n", new[] { Read(data, "Description1"), Read(data, "Description2") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))),
            Tag = First(Read(vars, "Tag"), Read(data, "Tag")),
            ArtworkAssetSha256 = artwork,
            ThemeProfile = visual.TryGetValue(AuraToolsCardVisualRuntime.ReplayThemeIdKey, out var theme)
                && IsSafeProfile(theme)
                    ? theme
                    : "default",
            AccentColor = ParseColor(First(Read(vars, "Color"), Read(data, "Color")))
        };
        cards.Add(descriptorId, descriptor);
        return descriptor;
    }

    internal ReplayBuffDescriptorV12 RegisterBuff(IDataConfig? config, string stableId)
    {
        var provenance = Provenance("Buff", stableId);
        var descriptorId = DescriptorId("buff", provenance);
        if (buffs.TryGetValue(descriptorId, out var existing)) return existing;
        var icon = CaptureResource(First(Read(config?.Vars, "Icon"), Read(config?.data, "Icon")), "Buff.Icon");
        if (icon.Length == 0) icon = CaptureFallbackTexture(descriptorId, "Buff.Fallback", 64, 64);
        var descriptor = new ReplayBuffDescriptorV12
        {
            DescriptorId = descriptorId,
            Provenance = provenance,
            Name = First(Read(config?.Vars, "Name"), Localize(config, "Name"), Read(config?.data, "Name"), stableId),
            Description = First(Read(config?.Vars, "Description"), Localize(config, "Description"), Read(config?.data, "Description")),
            IconAssetSha256 = icon
        };
        buffs.Add(descriptorId, descriptor);
        return descriptor;
    }

    internal ReplayIntentDescriptorV12 RegisterIntent(IDataConfig? config, string stableId)
    {
        var provenance = Provenance("Intent", stableId);
        var descriptorId = DescriptorId("intent", provenance);
        if (intents.TryGetValue(descriptorId, out var existing)) return existing;
        var icon = CaptureResource(First(
            Read(config?.Vars, "Icon"),
            Read(config?.data, "Icon"),
            Read(config?.Vars, "BackIcon"),
            Read(config?.data, "BackIcon")), "Intent.Icon");
        if (icon.Length == 0) icon = CaptureFallbackTexture(descriptorId, "Intent.Fallback", 64, 64);
        var descriptor = new ReplayIntentDescriptorV12
        {
            DescriptorId = descriptorId,
            Provenance = provenance,
            Name = First(Read(config?.Vars, "Name"), Localize(config, "Name"), Read(config?.data, "Name"), stableId),
            Description = First(Read(config?.Vars, "Description"), Localize(config, "Description"), Read(config?.data, "Description")),
            IconAssetSha256 = icon
        };
        intents.Add(descriptorId, descriptor);
        return descriptor;
    }

    internal string RegisterEffect(string effectName)
    {
        var normalized = NormalizeId(effectName);
        if (normalized.Length == 0) return "";
        var descriptorId = "effect:" + normalized + ":"
                           + ReplayCanonicalJsonV12.Sha256Text(effectName.Trim()).Substring(0, 16);
        if (!effects.ContainsKey(descriptorId))
        {
            var descriptor = new ReplayEffectDescriptorV12
            {
                DescriptorId = descriptorId,
                Primitive = "Flash",
                DurationTicks = 240_000,
                Color = new ReplayColorQ8V12 { R = 255, G = 225, B = 150, A = 210 }
            };
            foreach (var sprite in ResolveEffectSprites(effectName))
            {
                var hash = CaptureTexture(sprite.texture, "Effect." + normalized, required: true);
                descriptor.Frames.Add(new ReplaySpriteFrameV12
                {
                    AssetSha256 = hash,
                    RectX = (int)Math.Round(sprite.rect.x),
                    RectY = (int)Math.Round(sprite.rect.y),
                    RectWidth = (int)Math.Round(sprite.rect.width),
                    RectHeight = (int)Math.Round(sprite.rect.height),
                    PivotXQ16 = sprite.rect.width <= 0 ? 32_768 : Quantize(sprite.pivot.x / sprite.rect.width),
                    PivotYQ16 = sprite.rect.height <= 0 ? 32_768 : Quantize(sprite.pivot.y / sprite.rect.height),
                    PixelsPerUnitQ16 = Quantize(sprite.pixelsPerUnit)
                });
            }
            if (descriptor.Frames.Count > 0)
            {
                descriptor.Primitive = "SpriteSequence";
                descriptor.Color = new ReplayColorQ8V12 { R = 255, G = 255, B = 255, A = 255 };
                descriptor.DurationTicks = Math.Max(120_000L,
                    descriptor.Frames.Count * ReplayProtocolV12.TimebaseTicksPerSecond * 65_536L
                    / Math.Max(1, descriptor.FramesPerSecondQ16));
            }
            effects.Add(descriptorId, descriptor);
        }
        return descriptorId;
    }

    internal string RegisterAsset(ReplayAssetV12? asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Sha256)) return "";
        assets[asset.Sha256] = ReplayCanonicalJsonV12.CloneAssetWithPayload(asset);
        return asset.Sha256;
    }

    internal ReplayEntityPresentationBindingV12 Binding(
        ReplayEntityStateV12 state,
        string descriptorId,
        StatusManager? status)
    {
        var anchorId = (state.Team == ReplayTeamsV12.Enemy ? "enemy-" : "friendly-") + state.SlotIndex;
        EnsureAnchor(anchorId, state.Team, state.SlotIndex);
        var renderer = status?.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(item => item?.sprite != null);
        var color = renderer?.color ?? Color.white;
        return new ReplayEntityPresentationBindingV12
        {
            EntityId = state.EntityId,
            SpawnGeneration = state.SpawnGeneration,
            DescriptorId = descriptorId,
            LayoutAnchor = anchorId,
            ScaleQ16 = Quantize(renderer?.transform.lossyScale.x ?? 1f),
            SortingOrder = renderer?.sortingOrder ?? 0,
            FlipX = renderer?.flipX ?? false,
            Color = new ReplayColorQ8V12
            {
                R = ToByte(color.r),
                G = ToByte(color.g),
                B = ToByte(color.b),
                A = ToByte(color.a)
            }
        };
    }

    private void EnsureAnchor(string anchorId, string team, int slotIndex)
    {
        if (Scene.Anchors.Any(item => string.Equals(item.AnchorId, anchorId, StringComparison.Ordinal))) return;
        var slot = Math.Max(0, slotIndex);
        var column = slot % 6;
        var row = slot / 6;
        var enemy = string.Equals(team, ReplayTeamsV12.Enemy, StringComparison.Ordinal);
        Scene.Anchors.Add(new ReplayLayoutAnchorV12
        {
            AnchorId = anchorId,
            Position = new ReplayVector2Q16V12
            {
                X = Quantize((enemy ? 1.05f : -5.55f) + column * 1.22f),
                Y = Quantize((enemy ? -0.65f : -1.25f) + row * 1.30f)
            }
        });
    }

    private void CaptureAnimations(StatusManager? status, ReplayEntityDescriptorV12 descriptor)
    {
        var states = new Dictionary<string, List<Sprite>>(StringComparer.Ordinal);
        var owner = status?.fatherObject;
        if (owner != null)
        {
            var type = owner.GetType();
            var member = (object?)type.GetProperty("AnimatedStateSprites", InstanceMembers)?.GetValue(owner)
                         ?? type.GetField("AnimatedStateSprites", InstanceMembers)?.GetValue(owner)
                         ?? type.GetProperty("animatedStateSprites", InstanceMembers)?.GetValue(owner)
                         ?? type.GetField("animatedStateSprites", InstanceMembers)?.GetValue(owner);
            foreach (var pair in EnumerateDictionary(member))
            {
                var sprites = EnumerateSprites(pair.Value).ToList();
                if (sprites.Count > 0) states[pair.Key] = sprites;
            }
        }
        var current = status?.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(item => item?.sprite != null)
            .OrderByDescending(item => item.sprite.rect.width * item.sprite.rect.height)
            .Select(item => item.sprite)
            .FirstOrDefault();
        if (current != null && !states.ContainsKey("Idle")) states["Idle"] = new List<Sprite> { current };
        foreach (var pair in states.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var animation = new ReplayAnimationDescriptorV12
            {
                State = NormalizeAnimationState(pair.Key),
                Loop = string.Equals(pair.Key, "Idle", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(pair.Key, "Wait", StringComparison.OrdinalIgnoreCase)
            };
            foreach (var sprite in pair.Value.Where(item => item != null))
            {
                var hash = CaptureTexture(sprite.texture, "Entity.Animation." + animation.State, required: true);
                animation.Frames.Add(new ReplaySpriteFrameV12
                {
                    AssetSha256 = hash,
                    RectX = (int)Math.Round(sprite.rect.x),
                    RectY = (int)Math.Round(sprite.rect.y),
                    RectWidth = (int)Math.Round(sprite.rect.width),
                    RectHeight = (int)Math.Round(sprite.rect.height),
                    PivotXQ16 = sprite.rect.width <= 0 ? 32_768 : Quantize(sprite.pivot.x / sprite.rect.width),
                    PivotYQ16 = sprite.rect.height <= 0 ? 32_768 : Quantize(sprite.pivot.y / sprite.rect.height),
                    PixelsPerUnitQ16 = Quantize(sprite.pixelsPerUnit)
                });
            }
            if (animation.Frames.Count > 0) descriptor.Animations.Add(animation);
        }
    }

    private string CaptureResource(string resourcePath, string usage)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return "";
        try
        {
            var sprite = ResourceLoader.Load<Sprite>(resourcePath, true);
            if (sprite != null) return CaptureSpriteAsset(sprite, usage);
            var first = ResourceLoader.LoadAll<Sprite>(resourcePath)?.FirstOrDefault(item => item != null);
            if (first != null) return CaptureSpriteAsset(first, usage);
            var texture = ResourceLoader.Load<Texture2D>(resourcePath, true);
            return texture == null ? "" : CaptureTexture(texture, usage, required: true);
        }
        catch
        {
            return "";
        }
    }

    private string CaptureSpriteAsset(Sprite sprite, string usage)
    {
        if (sprite == null || sprite.texture == null) return "";
        var rect = sprite.rect;
        return CaptureTexture(
            sprite.texture,
            usage,
            required: true,
            new Rect(rect.x, rect.y, Math.Max(1f, rect.width), Math.Max(1f, rect.height)));
    }

    private string CaptureTexture(Texture texture, string usage, bool required, Rect? sourceRect = null)
    {
        if (texture == null) return "";
        Texture2D? readable = null;
        RenderTexture? temporary = null;
        var previous = RenderTexture.active;
        try
        {
            var rect = sourceRect ?? new Rect(0f, 0f, texture.width, texture.height);
            var width = Math.Max(1, (int)Math.Round(rect.width));
            var height = Math.Max(1, (int)Math.Round(rect.height));
            temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32);
            if (sourceRect.HasValue)
                Graphics.Blit(
                    texture,
                    temporary,
                    new Vector2(rect.width / Math.Max(1f, texture.width), rect.height / Math.Max(1f, texture.height)),
                    new Vector2(rect.x / Math.Max(1f, texture.width), rect.y / Math.Max(1f, texture.height)));
            else Graphics.Blit(texture, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, readable.width, readable.height), 0, 0, false);
            readable.Apply(false, false);
            var payload = readable.EncodeToPNG();
            var hash = ReplayCanonicalJsonV12.Sha256(payload);
            if (!assets.ContainsKey(hash))
                assets.Add(hash, new ReplayAssetV12
                {
                    Sha256 = hash,
                    MediaType = "image/png",
                    Extension = ".png",
                    Usage = usage ?? "Image",
                    ByteLength = payload.LongLength,
                    Width = readable.width,
                    Height = readable.height,
                    Required = required,
                    Payload = payload
                });
            return hash;
        }
        finally
        {
            RenderTexture.active = previous;
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            if (readable != null) Object.Destroy(readable);
        }
    }

    private static IEnumerable<Sprite> ResolveEffectSprites(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) yield break;
        var seen = new HashSet<int>();
        Sprite? direct = null;
        Sprite[] all = Array.Empty<Sprite>();
        GameObject? prefab = null;
        try
        {
            direct = ResourceLoader.Load<Sprite>(resourcePath, true);
            all = ResourceLoader.LoadAll<Sprite>(resourcePath) ?? Array.Empty<Sprite>();
            prefab = ResourceLoader.Load<GameObject>(resourcePath, true);
        }
        catch
        {
        }
        if (direct != null && seen.Add(direct.GetInstanceID())) yield return direct;
        foreach (var sprite in all.Where(item => item != null))
            if (seen.Add(sprite.GetInstanceID())) yield return sprite;
        foreach (var renderer in prefab?.GetComponentsInChildren<SpriteRenderer>(true) ?? Array.Empty<SpriteRenderer>())
            if (renderer?.sprite != null && seen.Add(renderer.sprite.GetInstanceID())) yield return renderer.sprite;
    }

    private string CaptureFallbackTexture(string identity, string usage, int width, int height)
    {
        var texture = new Texture2D(Math.Max(2, width), Math.Max(2, height), TextureFormat.RGBA32, false);
        try
        {
            var seed = ReplayCanonicalJsonV12.Sha256Text(identity ?? "fallback");
            var r = byte.Parse(seed.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(seed.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(seed.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var primary = new Color32((byte)(48 + r / 2), (byte)(48 + g / 2), (byte)(48 + b / 2), 255);
            var secondary = new Color32((byte)(24 + r / 3), (byte)(24 + g / 3), (byte)(24 + b / 3), 255);
            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
                texture.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? primary : secondary);
            texture.Apply(false, false);
            return CaptureTexture(texture, usage, required: true);
        }
        finally
        {
            Object.Destroy(texture);
        }
    }

    private static ReplaySceneDescriptorV12 CreateSceneDescriptor()
    {
        var result = new ReplaySceneDescriptorV12();
        for (var index = 0; index < 4; index++)
        {
            result.Anchors.Add(new ReplayLayoutAnchorV12
            {
                AnchorId = "friendly-" + index,
                Position = new ReplayVector2Q16V12
                {
                    X = Quantize(-5.4f + index * 1.55f),
                    Y = Quantize(-1.15f)
                }
            });
            result.Anchors.Add(new ReplayLayoutAnchorV12
            {
                AnchorId = "enemy-" + index,
                Position = new ReplayVector2Q16V12
                {
                    X = Quantize(1.25f + index * 1.55f),
                    Y = Quantize(-0.7f)
                }
            });
        }
        return result;
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateDictionary(object? value)
    {
        if (value is not IEnumerable enumerable) yield break;
        foreach (var item in enumerable)
        {
            if (item == null) continue;
            var type = item.GetType();
            var key = type.GetProperty("Key")?.GetValue(item)?.ToString() ?? "";
            var nested = type.GetProperty("Value")?.GetValue(item);
            if (!string.IsNullOrWhiteSpace(key)) yield return new KeyValuePair<string, object?>(key, nested);
        }
    }

    private static IEnumerable<Sprite> EnumerateSprites(object? value)
    {
        if (value is Sprite sprite) yield return sprite;
        else if (value is IEnumerable enumerable)
            foreach (var item in enumerable)
                if (item is Sprite next) yield return next;
    }

    private static ReplayContentProvenanceV12 Provenance(string kind, string stableId)
    {
        var id = string.IsNullOrWhiteSpace(stableId) ? "unknown" : stableId.Trim();
        var owner = Owner(id);
        return new ReplayContentProvenanceV12
        {
            OwnerModId = owner,
            ContentKind = kind ?? "",
            StableContentId = id,
            SourceVersion = ResolveOwnerVersion(owner)
        };
    }

    private static string DescriptorId(string prefix, ReplayContentProvenanceV12 value)
    {
        return prefix + ":" + value.OwnerModId + ":" + value.StableContentId;
    }

    private static string EntityContentKind(string archetype)
    {
        return archetype == ReplayEntityArchetypesV12.EnemyCombatant ? "Enemy"
            : archetype == ReplayEntityArchetypesV12.AlliedCombatant ? "Partner"
            : "Role";
    }

    private static string Owner(string stableId)
    {
        var value = (stableId ?? "").Trim();
        if (value.StartsWith("card_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("enemy_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("buff_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("career_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("role_", StringComparison.OrdinalIgnoreCase)) return "Witch";
        var separator = value.IndexOfAny(new[] { '.', ':', '_' });
        return separator > 0 ? value.Substring(0, separator) : "Witch";
    }

    private static string ResolveOwnerVersion(string owner)
    {
        if (string.Equals(owner, "Witch", StringComparison.OrdinalIgnoreCase))
            return typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item =>
            string.Equals(item.GetName().Name, owner, StringComparison.OrdinalIgnoreCase)
            || (item.GetName().Name ?? "").StartsWith(owner + ".", StringComparison.OrdinalIgnoreCase));
        return assembly?.GetName().Version?.ToString() ?? "unknown";
    }

    private static string NormalizeAnimationState(string value)
    {
        var result = NormalizeId(value);
        return result.Length == 0 ? "Idle" : result;
    }

    private static string NormalizeId(string value)
    {
        var chars = (value ?? "").Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.').Take(96).ToArray();
        return new string(chars);
    }

    private static bool IsSafeProfile(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 96
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static ReplayColorQ8V12 ParseColor(string value)
    {
        if (ColorUtility.TryParseHtmlString(value ?? "", out var color))
            return new ReplayColorQ8V12 { R = ToByte(color.r), G = ToByte(color.g), B = ToByte(color.b), A = ToByte(color.a) };
        return new ReplayColorQ8V12 { R = 210, G = 210, B = 220, A = 255 };
    }

    private static int Quantize(float value) => (int)Math.Round(value * 65_536d);
    private static byte ToByte(float value) => (byte)Math.Round(Mathf.Clamp01(value) * 255f);

    internal static string Read(IDictionary<string, string>? values, string key) =>
        values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";

    internal static string First(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string Localize(IDataConfig? config, string key)
    {
        try
        {
            var value = config?.data?.Localize(key) ?? "";
            return string.Equals(value, key, StringComparison.Ordinal) ? "" : value;
        }
        catch
        {
            return "";
        }
    }
}

internal static class ReplayFactCaptureV12
{
    internal static ReplayPublicStateV12 CapturePublicState(
        int roundSequence,
        int actorTurnSequence,
        ReplayCaptureCatalogV12 catalog)
    {
        var manager = FightManager.Instance;
        var result = new ReplayPublicStateV12
        {
            LevelId = manager?.level ?? "",
            BattlePhase = "Active",
            RoundSequence = Math.Max(1, roundSequence),
            ActorTurnSequence = Math.Max(1, actorTurnSequence)
        };
        if (manager == null) return result;
        var enemySlots = (EnemyManager.Instance?.enemyList ?? new List<Enemy>())
            .Where(item => item?.Status != null)
            .Select((item, index) => (item.Status.InstanceId ?? "", index))
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .ToDictionary(item => item.Item1, item => item.index, StringComparer.Ordinal);
        var friendlySlot = 0;
        foreach (var pair in manager.statuses.Where(item => item.Value != null).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var isEnemy = pair.Value.fatherObject is Enemy;
            var entity = CaptureEntity(
                pair.Key ?? "",
                pair.Value,
                isEnemy ? 0 : friendlySlot++,
                enemySlots,
                catalog);
            if (!string.IsNullOrWhiteSpace(entity.EntityId)) result.Entities.Add(entity);
        }
        result.ActiveActorId = result.Entities.FirstOrDefault(item =>
            item.Team == ReplayTeamsV12.Friendly && item.OwnerPlayerId == (RoleTable.Instance?.Id ?? ""))?.EntityId
            ?? result.Entities.FirstOrDefault(item => item.Team == ReplayTeamsV12.Friendly)?.EntityId
            ?? "";
        CapturePublicCards(result, catalog);
        result.Intents = ReplayIntentCaptureV12.CapturePlans(catalog);
        return ReplayStateReducerV12.Normalize(result);
    }

    internal static ReplayCapturedActionSourceV12 CaptureActionSource(object? target, ReplayCaptureCatalogV12 catalog)
    {
        var config = target switch
        {
            CardItem card => card.dataConfig,
            SkillItem skill => skill.dataConfig,
            _ => null
        };
        var stableId = ReplayCaptureCatalogV12.Read(config?.data, "Id");
        var descriptor = catalog.RegisterCard(config, stableId);
        var actorId = target switch
        {
            CardItem card => card.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
            SkillItem skill => skill.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
            _ => FightPlayer.Instance?.Status?.InstanceId ?? ""
        };
        var sourceInstanceId = config?.InstanceID ?? ReplayCaptureCatalogV12.Read(config?.Vars, "InstanceID");
        var hand = FightUI.cardItemList ?? new List<CardItem>();
        var slot = hand.FindIndex(item => ReferenceEquals(item, target)
                                          || string.Equals(item?.dataConfig?.InstanceID, sourceInstanceId, StringComparison.Ordinal));
        var effect = ReplayCaptureCatalogV12.First(
            ReplayCaptureCatalogV12.Read(config?.Vars, "Effects"),
            ReplayCaptureCatalogV12.Read(config?.data, "Effects"));
        return new ReplayCapturedActionSourceV12
        {
            Kind = target is SkillItem ? ReplayTransactionKindsV12.Skill : ReplayTransactionKindsV12.Card,
            IssuerPlayerId = RoleTable.Instance?.Id ?? "",
            ActorId = actorId,
            SourceInstanceId = sourceInstanceId,
            DescriptorId = descriptor.DescriptorId,
            Label = descriptor.Name,
            AnimationState = ReplayCaptureCatalogV12.First(
                ReplayCaptureCatalogV12.Read(config?.Vars, "Action"),
                ReplayCaptureCatalogV12.Read(config?.data, "Action"),
                "Idle"),
            EffectDescriptorId = catalog.RegisterEffect(effect),
            SourceZone = target is SkillItem ? "Skill" : "Hand",
            SourceSlot = target is SkillItem ? slot : -1
        };
    }

    internal static ReplayEntityPresentationBindingV12 CaptureBinding(
        ReplayEntityStateV12 state,
        ReplayCaptureCatalogV12 catalog)
    {
        var status = FightManager.Instance?.statuses?.Values.FirstOrDefault(item =>
            item != null && string.Equals(item.InstanceId, state.EntityId, StringComparison.Ordinal));
        var descriptor = CaptureEntityDescriptor(status, state, catalog);
        return catalog.Binding(state, descriptor.DescriptorId, status);
    }

    internal static List<ReplayPublicCardStateV12> CapturePrivateCards(ReplayCaptureCatalogV12 catalog)
    {
        var result = new List<ReplayPublicCardStateV12>();
        AddPrivateCards(result, "Draw", FightCardManager.Instance?.cardList, catalog);
        AddPrivateCards(result, "Discard", FightCardManager.Instance?.usedCardList, catalog);
        AddPrivateCards(result, "Nascent", FightCardManager.Instance?.nascentList, catalog);
        var order = 0;
        foreach (var item in (FightUI.cardItemList ?? new List<CardItem>()).Where(item => item?.dataConfig != null))
            result.Add(PrivateCard("Hand", order++, item.dataConfig, catalog));
        return result.OrderBy(item => item.Zone, StringComparer.Ordinal)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.CardInstanceId, StringComparer.Ordinal)
            .ToList();
    }

    private static ReplayEntityStateV12 CaptureEntity(
        string entityId,
        StatusManager status,
        int fallbackSlot,
        IReadOnlyDictionary<string, int> enemySlots,
        ReplayCaptureCatalogV12 catalog)
    {
        var enemy = status.fatherObject as Enemy;
        var player = status.fatherObject as FightPlayer;
        var remote = status.fatherObject as OtherPlayer;
        var partner = status.fatherObject as Partner;
        var config = enemy?.dataConfig ?? partner?.dataConfig;
        var remoteRole = remote == null
            ? null
            : FightManager.Instance?.roleQueue?.FirstOrDefault(item =>
                string.Equals(item.InstanceId, remote.InstanceId, StringComparison.Ordinal))?.career;
        var stableId = enemy != null
            ? ReplayCaptureCatalogV12.Read(config?.data, "Id").Replace("*", "")
            : partner != null
                ? ReplayCaptureCatalogV12.Read(config?.data, "Id").Replace("*", "")
                : player != null
                    ? ReplayCaptureCatalogV12.First(
                        ReplayCaptureCatalogV12.Read(RoleTable.Instance?.Career?.data, "Id"),
                        RoleTable.Instance?.Id ?? "player")
                    : ReplayCaptureCatalogV12.First(
                        ReplayCaptureCatalogV12.Read(remoteRole?.data, "Id"),
                        remote?.Id ?? entityId);
        var archetype = enemy != null
            ? ReplayEntityArchetypesV12.EnemyCombatant
            : partner != null
                ? ReplayEntityArchetypesV12.AlliedCombatant
                : ReplayEntityArchetypesV12.PlayerCombatant;
        var contentConfig = player != null ? RoleTable.Instance?.Career : remoteRole ?? config;
        catalog.RegisterEntity(status, archetype, contentConfig, stableId);
        var result = new ReplayEntityStateV12
        {
            EntityId = entityId,
            SpawnGeneration = 1,
            Team = enemy != null ? ReplayTeamsV12.Enemy : ReplayTeamsV12.Friendly,
            OwnerPlayerId = player != null ? RoleTable.Instance?.Id ?? "" : remote?.InstanceId ?? "",
            SlotIndex = enemySlots.TryGetValue(entityId, out var slot) ? slot : fallbackSlot,
            IsPresent = true,
            IsAlive = status.curHp > 0,
            MaxHp = status.maxHp,
            CurrentHp = status.curHp,
            Defense = status.defend
        };
        foreach (var buff in (status.GetBuffs() ?? Array.Empty<IBuffItem>())
                     .Where(item => item?.buffConfig != null)
                     .OrderBy(item => item.buffConfig.BuffId, StringComparer.Ordinal))
        {
            var configValue = buff.buffConfig;
            var descriptor = catalog.RegisterBuff(configValue.dataConfig, configValue.BuffId ?? "");
            result.Buffs.Add(new ReplayBuffStateV12
            {
                InstanceId = entityId + "|" + (configValue.BuffId ?? ""),
                DescriptorId = descriptor.DescriptorId,
                Level = configValue.Level,
                UpperBound = configValue.UpperBound,
                VisibleDuration = Math.Max(configValue.ReducePerTurn, Math.Max(configValue.ReducePerUse, configValue.ReducePerAttacked))
            });
        }
        return result;
    }

    private static ReplayEntityDescriptorV12 CaptureEntityDescriptor(
        StatusManager? status,
        ReplayEntityStateV12 state,
        ReplayCaptureCatalogV12 catalog)
    {
        if (status == null)
            return catalog.RegisterEntity(
                null,
                state.Team == ReplayTeamsV12.Enemy
                    ? ReplayEntityArchetypesV12.EnemyCombatant
                    : ReplayEntityArchetypesV12.AlliedCombatant,
                null,
                state.EntityId);
        var enemy = status.fatherObject as Enemy;
        var partner = status.fatherObject as Partner;
        var player = status.fatherObject as FightPlayer;
        var config = enemy?.dataConfig ?? partner?.dataConfig ?? (player != null ? RoleTable.Instance?.Career : null);
        var stable = ReplayCaptureCatalogV12.First(
            ReplayCaptureCatalogV12.Read(config?.data, "Id"),
            state.EntityId);
        var archetype = enemy != null
            ? ReplayEntityArchetypesV12.EnemyCombatant
            : partner != null
                ? ReplayEntityArchetypesV12.AlliedCombatant
                : ReplayEntityArchetypesV12.PlayerCombatant;
        return catalog.RegisterEntity(status, archetype, config, stable);
    }

    private static void CapturePublicCards(ReplayPublicStateV12 target, ReplayCaptureCatalogV12 catalog)
    {
        AddPublicCards(target.Cards, "Discard", FightCardManager.Instance?.usedCardList, catalog);
        AddPublicCards(target.Cards, "Nascent", FightCardManager.Instance?.nascentList, catalog);
        target.ZoneCounts.Add(new ReplayPublicZoneCountV12
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Draw",
            Count = FightCardManager.Instance?.cardList?.Count ?? 0
        });
        target.ZoneCounts.Add(new ReplayPublicZoneCountV12
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Discard",
            Count = FightCardManager.Instance?.usedCardList?.Count ?? 0
        });
        target.ZoneCounts.Add(new ReplayPublicZoneCountV12
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Hand",
            Count = FightUI.cardItemList?.Count ?? 0
        });
    }

    private static void AddPublicCards(
        ICollection<ReplayPublicCardStateV12> target,
        string zone,
        IEnumerable<DataConfig>? source,
        ReplayCaptureCatalogV12 catalog)
    {
        var order = 0;
        foreach (var config in source ?? Enumerable.Empty<DataConfig>())
        {
            if (config == null) continue;
            var stableId = ReplayCaptureCatalogV12.Read(config.data, "Id");
            var descriptor = catalog.RegisterCard(config, stableId);
            target.Add(new ReplayPublicCardStateV12
            {
                CardInstanceId = config.InstanceID ?? ReplayCaptureCatalogV12.Read(config.Vars, "InstanceID"),
                DescriptorId = descriptor.DescriptorId,
                OwnerPlayerId = RoleTable.Instance?.Id ?? "",
                Zone = zone,
                Order = order++,
                DisplayedCost = ParseInt(ReplayCaptureCatalogV12.First(
                    ReplayCaptureCatalogV12.Read(config.Vars, "Expend"),
                    ReplayCaptureCatalogV12.Read(config.data, "Expend")))
            });
        }
    }

    private static void AddPrivateCards(
        ICollection<ReplayPublicCardStateV12> target,
        string zone,
        IEnumerable<DataConfig>? source,
        ReplayCaptureCatalogV12 catalog)
    {
        var order = 0;
        foreach (var config in source ?? Enumerable.Empty<DataConfig>())
            if (config != null) target.Add(PrivateCard(zone, order++, config, catalog));
    }

    private static ReplayPublicCardStateV12 PrivateCard(
        string zone,
        int order,
        DataConfig config,
        ReplayCaptureCatalogV12 catalog)
    {
        var descriptor = catalog.RegisterCard(config, ReplayCaptureCatalogV12.Read(config.data, "Id"));
        return new ReplayPublicCardStateV12
        {
            CardInstanceId = config.InstanceID ?? ReplayCaptureCatalogV12.Read(config.Vars, "InstanceID"),
            DescriptorId = descriptor.DescriptorId,
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = zone,
            Order = order,
            DisplayedCost = ParseInt(ReplayCaptureCatalogV12.First(
                ReplayCaptureCatalogV12.Read(config.Vars, "Expend"),
                ReplayCaptureCatalogV12.Read(config.data, "Expend")))
        };
    }

    private static int ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : 0;
}

internal static class ReplayIntentCaptureV12
{
    private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SelectedCardsField = typeof(ObjectAction).GetField("CardList", InstanceFields);

    internal static List<ReplayIntentStateV12> CapturePlans(ReplayCaptureCatalogV12 catalog)
    {
        var result = new List<ReplayIntentStateV12>();
        foreach (var enemy in (EnemyManager.Instance?.enemyList ?? new List<Enemy>())
                     .Where(item => item?.Status != null)
                     .OrderBy(item => item.Status.InstanceId, StringComparer.Ordinal))
        {
            var selected = SelectedCards(enemy);
            for (var slot = 0; slot < selected.Count; slot++)
            {
                var card = selected[slot];
                var config = card?.dataConfig;
                if (config == null || enemy.Status == null) continue;
                var stableId = ReplayCaptureCatalogV12.First(
                    ReplayCaptureCatalogV12.Read(config.data, "Id"),
                    ReplayCaptureCatalogV12.Read(config.Vars, "Id"));
                var descriptor = catalog.RegisterIntent(config, stableId);
                result.Add(new ReplayIntentStateV12
                {
                    IntentInstanceId = config.InstanceID ?? enemy.Status.InstanceId + "|intent|" + slot,
                    ActorId = enemy.Status.InstanceId ?? enemy.InstanceId ?? "",
                    DescriptorId = descriptor.DescriptorId,
                    SlotIndex = slot,
                    DisplayValue = ReplayCaptureCatalogV12.First(
                        ReplayCaptureCatalogV12.Read(config.Vars, "DesVal1"),
                        ReplayCaptureCatalogV12.Read(config.data, "DesVal1")),
                    TargetIds = (config.scriptExecutor?.Object ?? new List<IStatusManager>())
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InstanceId))
                        .Select(item => item.InstanceId)
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                });
            }
        }
        return result;
    }

    private static List<ObjectCard> SelectedCards(Enemy enemy)
    {
        try
        {
            if (enemy.FightAction != null && SelectedCardsField?.GetValue(enemy.FightAction) is IEnumerable selected)
                return selected.Cast<object>().OfType<ObjectCard>().Where(item => item != null).ToList();
        }
        catch
        {
        }
        return (enemy.ActionCards ?? new List<ObjectCard>()).Where(item => item != null).ToList();
    }
}
