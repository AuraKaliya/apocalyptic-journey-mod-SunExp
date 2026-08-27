using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Playback;

internal sealed class ReplaySceneRuntime : IDisposable
{
    private const int ReplayLayer = 30;
    private readonly ReplayDocumentV12 document;
    private readonly ReplayAssetCacheV12 assets;
    private readonly Dictionary<string, ReplayEntityDescriptorV12> entityDescriptors;
    private readonly Dictionary<string, ReplayCardDescriptorV12> cardDescriptors;
    private readonly Dictionary<string, ReplayBuffDescriptorV12> buffDescriptors;
    private readonly Dictionary<string, ReplayIntentDescriptorV12> intentDescriptors;
    private readonly Dictionary<string, ReplayEffectDescriptorV12> effectDescriptors;
    private readonly Dictionary<string, Vector3> anchors;
    private readonly Dictionary<string, ReplayCombatantViewV12> combatants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayEntityPresentationBindingV12> bindings = new(StringComparer.Ordinal);
    private readonly List<PendingEffect> pendingEffects = new();
    private readonly GameObject root;
    private readonly Camera camera;
    private readonly ReplayHudRuntimeV12 hud;
    private readonly ReplayCardPresenterV12 cards;
    private readonly ReplayPovHandRuntimeV12 povHand;
    private readonly ReplayEffectRuntimeV12 effects;
    private readonly ReplayAudioRuntimeV12 audio;
    private long logicalTicks;
    private bool disposed;

    internal ReplaySceneRuntime(ReplayDocumentV12 document, ReplayPovSidecarV12? pov, bool includeHud)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        assets = new ReplayAssetCacheV12(document.Assets.Concat(pov?.Assets ?? new List<ReplayAssetV12>()));
        entityDescriptors = document.Presentation.Entities.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        cardDescriptors = document.Presentation.Cards
            .Concat(pov?.PrivateCards ?? new List<ReplayCardDescriptorV12>())
            .GroupBy(item => item.DescriptorId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        buffDescriptors = document.Presentation.Buffs.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        intentDescriptors = document.Presentation.Intents.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        effectDescriptors = document.Presentation.Effects.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        anchors = document.Presentation.Scene.Anchors.ToDictionary(
            item => item.AnchorId,
            item => new Vector3(FromQ16(item.Position.X), FromQ16(item.Position.Y), 0f),
            StringComparer.Ordinal);

        root = new GameObject("AuraToolsReplaySceneV12");
        Object.DontDestroyOnLoad(root);
        SetLayer(root, ReplayLayer);
        camera = CreateCamera(root.transform, document.Presentation.Scene);
        CreateBackground(root.transform, document.Presentation.Scene);
        var canvas = CreateCanvas(root.transform, camera, document.Presentation.Scene);
        hud = new ReplayHudRuntimeV12(canvas.transform, intentDescriptors, includeHud);
        cards = new ReplayCardPresenterV12(canvas.transform, assets, cardDescriptors);
        povHand = new ReplayPovHandRuntimeV12(canvas.transform, assets, cardDescriptors, pov != null);
        effects = new ReplayEffectRuntimeV12(root.transform, assets, effectDescriptors);
        audio = new ReplayAudioRuntimeV12(root.transform, assets);
    }

    internal Camera Camera => camera;

    internal GameObject Root => root;

    internal void SetHudVisible(bool visible) => hud.SetVisible(visible);

    internal void SetPlaybackSpeed(float speed) => audio.SetTransportSpeed(speed);

    internal void SetPaused(bool paused) => audio.SetPaused(paused);

    internal void Restore(ReplayPublicStateV12 state, ReplayPresentationCheckpointV12? checkpoint)
    {
        foreach (var value in combatants.Values) value.Dispose();
        combatants.Clear();
        bindings.Clear();
        cards.Clear();
        povHand.Apply(Array.Empty<ReplayPublicCardStateV12>());
        effects.Clear();
        pendingEffects.Clear();
        audio.StopAll();
        foreach (var binding in checkpoint?.EntityBindings ?? new List<ReplayEntityPresentationBindingV12>())
            BindEntity(binding, state);
        foreach (var view in checkpoint?.EntityViews ?? new List<ReplayEntityViewStateV12>())
            if (combatants.TryGetValue(EntityKey(view.EntityId, view.SpawnGeneration), out var combatant))
                combatant.PlayAnimation(
                    view.AnimationState,
                    restart: true,
                    view.AnimationStartedTicks,
                    view.AnimationEndsTicks > view.AnimationStartedTicks
                        ? view.AnimationEndsTicks - view.AnimationStartedTicks
                        : 0L);
        ApplyState(state);
    }

    internal void ApplyState(ReplayPublicStateV12 state)
    {
        foreach (var value in combatants.Values)
        {
            var entity = state.Entities.LastOrDefault(item =>
                string.Equals(item.EntityId, value.EntityId, StringComparison.Ordinal)
                && item.SpawnGeneration == value.SpawnGeneration);
            if (entity != null) value.Apply(entity);
        }
        var active = state.Entities.Select(item => EntityKey(item.EntityId, item.SpawnGeneration)).ToHashSet(StringComparer.Ordinal);
        foreach (var key in combatants.Keys.Where(key => !active.Contains(key)).ToList())
        {
            combatants[key].Dispose();
            combatants.Remove(key);
            bindings.Remove(key);
        }
        hud.Apply(state);
    }

    internal void ApplyPovCards(IReadOnlyList<ReplayPublicCardStateV12> values) => povHand.Apply(values);

    internal void RestoreTimedPresentationsAt(
        IEnumerable<ReplayJournalEventV12> presentationEvents,
        long targetTicks,
        bool includeAudio)
    {
        cards.Clear();
        effects.Clear();
        pendingEffects.Clear();
        audio.StopAll();
        foreach (var value in (presentationEvents ?? Array.Empty<ReplayJournalEventV12>())
                     .Where(item => item.TimeTicks <= targetTicks)
                     .OrderBy(item => item.Sequence))
        {
            var message = value.Presentation;
            if (message == null) continue;
            if (value.EventType == ReplayEventTypesV12.SourcePresented)
            {
                var end = value.TimeTicks + Math.Max(240_000L, message.DurationTicks);
                if (targetTicks < end) cards.Show(message, value.TimeTicks);
            }
            else if (value.EventType == ReplayEventTypesV12.EffectPresented)
            {
                var descriptorDuration = effectDescriptors.TryGetValue(message.EffectDescriptorId ?? "", out var descriptor)
                    ? descriptor.DurationTicks
                    : 0L;
                var start = value.TimeTicks + Math.Max(0L, message.DelayTicks);
                var end = start + Math.Max(120_000L, Math.Max(descriptorDuration, message.DurationTicks));
                var position = PositionForEntity(message.TargetIds.FirstOrDefault() ?? message.ActorId);
                if (start > targetTicks)
                    pendingEffects.Add(new PendingEffect(value.Sequence, start, ReplayCanonicalJsonV12.Clone(message), position));
                else if (targetTicks < end)
                    effects.Play(message, position, start);
            }
            else if (includeAudio && value.EventType == ReplayEventTypesV12.AudioPresented && message.Audio != null)
            {
                var durationTicks = message.Audio.DurationSamples * ReplayProtocolV12.TimebaseTicksPerSecond / 48_000L;
                if (durationTicks > 0 && targetTicks < value.TimeTicks + durationTicks)
                    audio.Play(message.Audio, value.TimeTicks, targetTicks);
            }
        }
        cards.Tick(targetTicks);
        effects.Tick(targetTicks);
        audio.Tick(targetTicks);
    }

    internal void ApplyPresentation(ReplayJournalEventV12 value, ReplayPublicStateV12 state, bool suppressAudio)
    {
        var message = value.Presentation;
        if (message == null) return;
        switch (value.EventType)
        {
            case ReplayEventTypesV12.EntityPresented:
            case ReplayEventTypesV12.EntityPresentationChanged:
                if (message.EntityBinding != null) BindEntity(message.EntityBinding, state);
                break;
            case ReplayEventTypesV12.SourcePresented:
                cards.Show(message, value.TimeTicks);
                break;
            case ReplayEventTypesV12.ActorAnimationPresented:
            case ReplayEventTypesV12.HitReactionPresented:
                if (TryCombatant(message.ActorId, out var actor))
                    actor.PlayAnimation(message.AnimationState, restart: true, value.TimeTicks, message.DurationTicks);
                break;
            case ReplayEventTypesV12.EffectPresented:
            {
                var position = PositionForEntity(message.TargetIds.FirstOrDefault() ?? message.ActorId);
                if (message.DelayTicks > 0)
                    pendingEffects.Add(new PendingEffect(
                        value.Sequence,
                        value.TimeTicks + message.DelayTicks,
                        ReplayCanonicalJsonV12.Clone(message),
                        position));
                else effects.Play(message, position, value.TimeTicks);
                break;
            }
            case ReplayEventTypesV12.AudioPresented:
                if (!suppressAudio && message.Audio != null) audio.Play(message.Audio, value.TimeTicks);
                break;
        }
    }

    internal void Tick(long logicalTicks)
    {
        this.logicalTicks = logicalTicks;
        foreach (var pending in pendingEffects.Where(item => item.StartTicks <= logicalTicks)
                     .OrderBy(item => item.StartTicks).ThenBy(item => item.Sequence).ToList())
        {
            effects.Play(pending.Message, pending.Position, pending.StartTicks);
            pendingEffects.Remove(pending);
        }
        foreach (var value in combatants.Values) value.Tick(logicalTicks);
        cards.Tick(logicalTicks);
        effects.Tick(logicalTicks);
        audio.Tick(logicalTicks);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var value in combatants.Values) value.Dispose();
        combatants.Clear();
        bindings.Clear();
        pendingEffects.Clear();
        cards.Dispose();
        povHand.Dispose();
        effects.Dispose();
        audio.Dispose();
        hud.Dispose();
        assets.Dispose();
        if (root != null) Object.Destroy(root);
    }

    private void BindEntity(ReplayEntityPresentationBindingV12 source, ReplayPublicStateV12 state)
    {
        var binding = ReplayCanonicalJsonV12.Clone(source);
        var key = EntityKey(binding.EntityId, binding.SpawnGeneration);
        if (!entityDescriptors.TryGetValue(binding.DescriptorId, out var descriptor))
            throw new InvalidOperationException("Replay entity descriptor is missing: " + binding.DescriptorId);
        if (combatants.TryGetValue(key, out var previous))
        {
            previous.Dispose();
            combatants.Remove(key);
        }
        bindings[key] = binding;
        var anchor = anchors.TryGetValue(binding.LayoutAnchor ?? "", out var value) ? value : Vector3.zero;
        var entity = state.Entities.LastOrDefault(item =>
            string.Equals(item.EntityId, binding.EntityId, StringComparison.Ordinal)
            && item.SpawnGeneration == binding.SpawnGeneration);
        var view = ReplayCombatantViewV12.Create(root.transform, descriptor, binding, anchor, assets, buffDescriptors);
        combatants[key] = view;
        if (entity != null) view.Apply(entity);
    }

    private bool TryCombatant(string entityId, out ReplayCombatantViewV12 value)
    {
        value = combatants.Values.LastOrDefault(item => string.Equals(item.EntityId, entityId, StringComparison.Ordinal))!;
        return value != null;
    }

    private Vector3 PositionForEntity(string entityId)
    {
        return TryCombatant(entityId, out var value) ? value.Position : Vector3.zero;
    }

    private static Camera CreateCamera(Transform parent, ReplaySceneDescriptorV12 scene)
    {
        var value = new GameObject("ReplayCamera", typeof(Camera));
        value.transform.SetParent(parent, false);
        value.layer = ReplayLayer;
        var camera = value.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = Math.Max(1f, FromQ16(scene.CameraOrthographicSizeQ16));
        camera.aspect = Math.Max(0.25f, scene.ReferenceWidth / (float)Math.Max(1, scene.ReferenceHeight));
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color(scene.ClearColor);
        camera.depth = 1000f;
        camera.cullingMask = 1 << ReplayLayer;
        camera.allowHDR = false;
        camera.allowMSAA = true;
        return camera;
    }

    private void CreateBackground(Transform parent, ReplaySceneDescriptorV12 scene)
    {
        var sprite = assets.FullSprite(scene.BackgroundAssetSha256, 100f);
        if (sprite == null) return;
        var value = new GameObject("ReplayBackground", typeof(SpriteRenderer));
        value.transform.SetParent(parent, false);
        value.layer = ReplayLayer;
        value.transform.position = new Vector3(0f, 0f, 8f);
        var renderer = value.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -1000;
        var height = camera.orthographicSize * 2f;
        var width = height * camera.aspect;
        value.transform.localScale = new Vector3(
            width / Math.Max(0.01f, sprite.bounds.size.x),
            height / Math.Max(0.01f, sprite.bounds.size.y),
            1f);
    }

    private static Canvas CreateCanvas(Transform parent, Camera camera, ReplaySceneDescriptorV12 scene)
    {
        var value = new GameObject("ReplayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        value.transform.SetParent(parent, false);
        value.layer = ReplayLayer;
        var canvas = value.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 1000;
        var scaler = value.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(
            Math.Max(320, scene.ReferenceWidth),
            Math.Max(180, scene.ReferenceHeight));
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static void SetLayer(GameObject value, int layer)
    {
        value.layer = layer;
        foreach (Transform child in value.transform) SetLayer(child.gameObject, layer);
    }

    private static string EntityKey(string id, int generation) => id + "|" + generation;
    internal static float FromQ16(int value) => value / 65_536f;
    internal static Color Color(ReplayColorQ8V12 value) => new(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
    internal static string Display(string value, int maximumCharacters)
    {
        var normalized = (value ?? "").Replace("\r", "").Trim();
        var maximum = Math.Max(1, maximumCharacters);
        return normalized.Length <= maximum ? normalized : normalized.Substring(0, maximum - 1) + ".";
    }

    private sealed class PendingEffect
    {
        internal PendingEffect(long sequence, long startTicks, ReplayPresentationMessageV12 message, Vector3 position)
        {
            Sequence = sequence;
            StartTicks = startTicks;
            Message = message;
            Position = position;
        }

        internal long Sequence { get; }
        internal long StartTicks { get; }
        internal ReplayPresentationMessageV12 Message { get; }
        internal Vector3 Position { get; }
    }
}

internal sealed class ReplayAssetCacheV12 : IDisposable
{
    private readonly Dictionary<string, ReplayAssetV12> assets;
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Sprite> sprites = new();
    private Texture2D? whiteTexture;
    private Sprite? whiteSprite;

    internal ReplayAssetCacheV12(IEnumerable<ReplayAssetV12> values)
    {
        assets = (values ?? Array.Empty<ReplayAssetV12>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    internal Sprite? FullSprite(string sha256, float pixelsPerUnit)
    {
        var texture = Texture(sha256);
        if (texture == null) return null;
        var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), Math.Max(1f, pixelsPerUnit));
        sprites.Add(sprite);
        return sprite;
    }

    internal Sprite? Sprite(ReplaySpriteFrameV12 frame)
    {
        var texture = Texture(frame.AssetSha256);
        if (texture == null) return null;
        var x = Mathf.Clamp(frame.RectX, 0, Math.Max(0, texture.width - 1));
        var y = Mathf.Clamp(frame.RectY, 0, Math.Max(0, texture.height - 1));
        var width = Mathf.Clamp(frame.RectWidth <= 0 ? texture.width : frame.RectWidth, 1, texture.width - x);
        var height = Mathf.Clamp(frame.RectHeight <= 0 ? texture.height : frame.RectHeight, 1, texture.height - y);
        var sprite = UnityEngine.Sprite.Create(
            texture,
            new Rect(x, y, width, height),
            new Vector2(ReplaySceneRuntime.FromQ16(frame.PivotXQ16), ReplaySceneRuntime.FromQ16(frame.PivotYQ16)),
            Math.Max(1f, ReplaySceneRuntime.FromQ16(frame.PixelsPerUnitQ16)));
        sprites.Add(sprite);
        return sprite;
    }

    internal Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply(false, true);
        whiteSprite = UnityEngine.Sprite.Create(whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        sprites.Add(whiteSprite);
        return whiteSprite;
    }

    internal byte[] Bytes(string sha256)
    {
        return assets.TryGetValue(sha256 ?? "", out var value) ? value.Payload ?? Array.Empty<byte>() : Array.Empty<byte>();
    }

    public void Dispose()
    {
        foreach (var sprite in sprites.Where(item => item != null)) Object.Destroy(sprite);
        sprites.Clear();
        foreach (var texture in textures.Values.Where(item => item != null)) Object.Destroy(texture);
        textures.Clear();
        if (whiteTexture != null) Object.Destroy(whiteTexture);
        whiteTexture = null;
        whiteSprite = null;
    }

    private Texture2D? Texture(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        if (textures.TryGetValue(sha256, out var cached)) return cached;
        var bytes = Bytes(sha256);
        if (bytes.Length == 0) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var method = typeof(ImageConversion).GetMethod(
            "LoadImage",
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        if (method?.Invoke(null, new object[] { texture, bytes, false }) is not true)
        {
            Object.Destroy(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        textures[sha256] = texture;
        return texture;
    }
}

internal sealed class ReplayCombatantViewV12 : IDisposable
{
    private readonly GameObject root;
    private readonly SpriteRenderer body;
    private readonly TextMesh name;
    private readonly TextMesh stats;
    private readonly TextMesh buffs;
    private readonly IReadOnlyDictionary<string, ReplayBuffDescriptorV12> buffDescriptors;
    private readonly Color aliveColor;
    private readonly Dictionary<string, AnimationState> animations;
    private readonly Vector3 basePosition;
    private readonly Vector3 baseScale;
    private readonly string safeActionProfile;
    private AnimationState? active;
    private string motionState = "Idle";
    private long animationStartedTicks;
    private long animationEndsTicks;

    private ReplayCombatantViewV12(
        GameObject root,
        SpriteRenderer body,
        TextMesh name,
        TextMesh stats,
        TextMesh buffs,
        Dictionary<string, AnimationState> animations,
        IReadOnlyDictionary<string, ReplayBuffDescriptorV12> buffDescriptors,
        string safeActionProfile,
        string entityId,
        int generation)
    {
        this.root = root;
        this.body = body;
        this.name = name;
        this.stats = stats;
        this.buffs = buffs;
        this.buffDescriptors = buffDescriptors;
        aliveColor = body.color;
        this.animations = animations;
        basePosition = root.transform.localPosition;
        baseScale = root.transform.localScale;
        this.safeActionProfile = safeActionProfile ?? "default";
        EntityId = entityId;
        SpawnGeneration = generation;
    }

    internal string EntityId { get; }
    internal int SpawnGeneration { get; }

    internal static ReplayCombatantViewV12 Create(
        Transform parent,
        ReplayEntityDescriptorV12 descriptor,
        ReplayEntityPresentationBindingV12 binding,
        Vector3 anchor,
        ReplayAssetCacheV12 assets,
        IReadOnlyDictionary<string, ReplayBuffDescriptorV12> buffDescriptors)
    {
        var root = new GameObject("ReplayCombatant:" + binding.EntityId);
        root.transform.SetParent(parent, false);
        root.layer = 30;
        root.transform.localPosition = anchor + new Vector3(
            ReplaySceneRuntime.FromQ16(binding.Offset.X),
            ReplaySceneRuntime.FromQ16(binding.Offset.Y),
            0f);
        root.transform.localScale = Vector3.one * Math.Max(0.05f, ReplaySceneRuntime.FromQ16(binding.ScaleQ16));
        var bodyObject = new GameObject("Body", typeof(SpriteRenderer));
        bodyObject.transform.SetParent(root.transform, false);
        bodyObject.layer = 30;
        var body = bodyObject.GetComponent<SpriteRenderer>();
        body.sortingOrder = 100 + binding.SortingOrder;
        body.flipX = binding.FlipX;
        body.color = ReplaySceneRuntime.Color(binding.Color);
        var animations = new Dictionary<string, AnimationState>(StringComparer.OrdinalIgnoreCase);
        foreach (var animation in descriptor.Animations)
        {
            var frames = animation.Frames.Select(assets.Sprite).Where(item => item != null).Select(item => item!).ToArray();
            if (frames.Length > 0)
                animations[animation.State] = new AnimationState(
                    frames,
                    Math.Max(0.1f, ReplaySceneRuntime.FromQ16(animation.FramesPerSecondQ16)),
                    animation.Loop);
        }
        if (animations.Count == 0) body.sprite = assets.WhiteSprite();
        var name = CreateText(root.transform, "Name", ReplaySceneRuntime.Display(descriptor.Name, 28), 36, new Vector3(0f, 1.45f, 0f), 180);
        var stats = CreateText(root.transform, "Stats", "", 31, new Vector3(0f, 1.05f, 0f), 180);
        var buffs = CreateText(root.transform, "Buffs", "", 23, new Vector3(0f, 0.72f, 0f), 180);
        var result = new ReplayCombatantViewV12(
            root, body, name, stats, buffs, animations, buffDescriptors, descriptor.SafeActionProfile,
            binding.EntityId, binding.SpawnGeneration);
        result.PlayAnimation("Idle", restart: true, 0L, 0L);
        return result;
    }

    internal void Apply(ReplayEntityStateV12 value)
    {
        stats.text = "HP " + value.CurrentHp + "/" + value.MaxHp + (value.Defense > 0 ? "   DEF " + value.Defense : "");
        var visibleBuffs = value.Buffs.Take(3).Select(item =>
        {
            var label = buffDescriptors.TryGetValue(item.DescriptorId ?? "", out var descriptor)
                ? descriptor.Name
                : "Buff";
            return ReplaySceneRuntime.Display(label, 18) + (item.Level != 0 ? " x" + item.Level : "");
        }).ToList();
        if (value.Buffs.Count > visibleBuffs.Count) visibleBuffs.Add("+" + (value.Buffs.Count - visibleBuffs.Count));
        buffs.text = string.Join("   ", visibleBuffs);
        body.enabled = value.IsPresent;
        name.gameObject.SetActive(value.IsPresent);
        stats.gameObject.SetActive(value.IsPresent);
        buffs.gameObject.SetActive(value.IsPresent && value.Buffs.Count > 0);
        body.color = value.IsAlive
            ? aliveColor
            : new Color(aliveColor.r * 0.45f, aliveColor.g * 0.45f, aliveColor.b * 0.45f, aliveColor.a);
    }

    internal Vector3 Position => root.transform.localPosition;

    internal void PlayAnimation(string state, bool restart, long startTicks, long durationTicks)
    {
        var requestedState = string.IsNullOrWhiteSpace(state) ? "Idle" : state;
        if (!animations.TryGetValue(requestedState, out var value))
            value = animations.TryGetValue("Idle", out var idle) ? idle : animations.Values.FirstOrDefault();
        if (value == null) return;
        if (!ReferenceEquals(active, value) || restart)
        {
            active = value;
            motionState = requestedState;
            animationStartedTicks = startTicks;
            animationEndsTicks = durationTicks > 0 ? startTicks + durationTicks : 0L;
            body.sprite = active.Frames[0];
            root.transform.localPosition = basePosition;
            root.transform.localScale = baseScale;
        }
    }

    internal void Tick(long logicalTicks)
    {
        if (animationEndsTicks > 0 && logicalTicks >= animationEndsTicks)
            PlayAnimation("Idle", restart: true, animationEndsTicks, 0L);
        ApplySafeMotion(logicalTicks);
        if (active == null || active.Frames.Length == 0) return;
        var elapsed = Math.Max(0, logicalTicks - animationStartedTicks) / (double)ReplayProtocolV12.TimebaseTicksPerSecond;
        var frame = (int)Math.Floor(elapsed * active.FramesPerSecond);
        if (active.Loop) frame %= active.Frames.Length;
        else frame = Math.Min(active.Frames.Length - 1, frame);
        body.sprite = active.Frames[Math.Max(0, frame)];
    }

    private void ApplySafeMotion(long logicalTicks)
    {
        root.transform.localPosition = basePosition;
        root.transform.localScale = baseScale;
        if (safeActionProfile == "static"
            || animationEndsTicks <= animationStartedTicks
            || string.Equals(motionState, "Idle", StringComparison.OrdinalIgnoreCase)) return;
        var progress = Mathf.Clamp01((logicalTicks - animationStartedTicks)
                                     / (float)Math.Max(1L, animationEndsTicks - animationStartedTicks));
        var pulse = Mathf.Sin(progress * Mathf.PI);
        if (motionState.IndexOf("Hit", StringComparison.OrdinalIgnoreCase) >= 0)
            root.transform.localPosition = basePosition + Vector3.right
                * (Mathf.Sin(progress * Mathf.PI * 6f) * 0.12f);
        else if (motionState.IndexOf("Defend", StringComparison.OrdinalIgnoreCase) >= 0)
            root.transform.localScale = baseScale * (1f + pulse * 0.08f);
        else
            root.transform.localPosition = basePosition + Vector3.right
                * ((body.flipX ? -1f : 1f) * pulse * 0.35f);
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }

    private static TextMesh CreateText(Transform parent, string objectName, string value, int size, Vector3 position, int order)
    {
        var root = new GameObject(objectName, typeof(MeshRenderer), typeof(TextMesh));
        root.transform.SetParent(parent, false);
        root.transform.localPosition = position;
        root.transform.localScale = Vector3.one * 0.025f;
        root.layer = 30;
        var text = root.GetComponent<TextMesh>();
        text.text = value ?? "";
        text.fontSize = size;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.white;
        text.richText = false;
        root.GetComponent<MeshRenderer>().sortingOrder = order;
        return text;
    }

    private sealed class AnimationState
    {
        internal AnimationState(Sprite[] frames, float framesPerSecond, bool loop)
        {
            Frames = frames;
            FramesPerSecond = framesPerSecond;
            Loop = loop;
        }
        internal Sprite[] Frames { get; }
        internal float FramesPerSecond { get; }
        internal bool Loop { get; }
    }
}

internal sealed class ReplayHudRuntimeV12 : IDisposable
{
    private readonly GameObject root;
    private readonly Text text;
    private readonly Text intents;
    private readonly IReadOnlyDictionary<string, ReplayIntentDescriptorV12> descriptors;

    internal ReplayHudRuntimeV12(
        Transform parent,
        IReadOnlyDictionary<string, ReplayIntentDescriptorV12> descriptors,
        bool visible)
    {
        this.descriptors = descriptors;
        root = CreateRect("ReplayHud", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1080f, 112f));
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);
        var image = root.AddComponent<Image>();
        image.color = new Color(0.03f, 0.035f, 0.05f, 0.88f);
        text = CreateUiText(root.transform, "State", 27, TextAnchor.MiddleCenter);
        text.rectTransform.anchorMin = new Vector2(0f, 0.48f);
        text.rectTransform.anchorMax = Vector2.one;
        intents = CreateUiText(root.transform, "Intents", 20, TextAnchor.MiddleCenter);
        intents.rectTransform.anchorMin = Vector2.zero;
        intents.rectTransform.anchorMax = new Vector2(1f, 0.50f);
        root.SetActive(visible);
    }

    internal void Apply(ReplayPublicStateV12 state)
    {
        text.text = "Round " + Math.Max(1, state.RoundSequence)
                    + "   Turn " + Math.Max(1, state.ActorTurnSequence)
                    + (string.IsNullOrWhiteSpace(state.Outcome) ? "" : "   " + ReplaySceneRuntime.Display(state.Outcome, 24));
        intents.text = string.Join("   ", state.Intents.Take(6).Select(item =>
        {
            var name = descriptors.TryGetValue(item.DescriptorId ?? "", out var descriptor)
                ? descriptor.Name
                : "Intent";
            return ReplaySceneRuntime.Display(name, 20)
                   + (string.IsNullOrWhiteSpace(item.DisplayValue) ? "" : " " + ReplaySceneRuntime.Display(item.DisplayValue, 16))
                   + (item.TargetIds.Count == 0
                       ? ""
                       : " -> " + string.Join(",", item.TargetIds.Take(2).Select(target => ReplaySceneRuntime.Display(target, 12))));
        }));
    }

    internal void SetVisible(bool visible) => root.SetActive(visible);

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }

    internal static GameObject CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size)
    {
        var value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        value.layer = 30;
        var rect = value.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        return value;
    }

    internal static Text CreateUiText(Transform parent, string name, int fontSize, TextAnchor anchor)
    {
        var value = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        var text = value.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }
}

internal sealed class ReplayCardPresenterV12 : IDisposable
{
    private readonly GameObject root;
    private readonly Image artwork;
    private readonly Text title;
    private readonly Text description;
    private readonly ReplayAssetCacheV12 assets;
    private readonly IReadOnlyDictionary<string, ReplayCardDescriptorV12> descriptors;
    private long hideAt;

    internal ReplayCardPresenterV12(
        Transform parent,
        ReplayAssetCacheV12 assets,
        IReadOnlyDictionary<string, ReplayCardDescriptorV12> descriptors)
    {
        this.assets = assets;
        this.descriptors = descriptors;
        root = ReplayHudRuntimeV12.CreateRect(
            "ReplayCard",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(360f, 510f));
        var panel = root.AddComponent<Image>();
        panel.color = new Color(0.06f, 0.065f, 0.09f, 0.96f);
        artwork = ReplayHudRuntimeV12.CreateRect(
            "Artwork", root.transform, new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero)
            .AddComponent<Image>();
        artwork.preserveAspect = true;
        artwork.raycastTarget = false;
        title = ReplayHudRuntimeV12.CreateUiText(root.transform, "Title", 30, TextAnchor.UpperCenter);
        title.rectTransform.anchorMin = new Vector2(0.06f, 0.90f);
        title.rectTransform.anchorMax = new Vector2(0.94f, 0.98f);
        description = ReplayHudRuntimeV12.CreateUiText(root.transform, "Description", 21, TextAnchor.UpperLeft);
        description.rectTransform.anchorMin = new Vector2(0.08f, 0.05f);
        description.rectTransform.anchorMax = new Vector2(0.92f, 0.32f);
        root.SetActive(false);
    }

    internal void Show(ReplayPresentationMessageV12 message, long logicalTicks)
    {
        if (!descriptors.TryGetValue(message.DescriptorId ?? "", out var descriptor)) return;
        title.text = ReplaySceneRuntime.Display(descriptor.Name, 48);
        description.text = ReplaySceneRuntime.Display(descriptor.Description, 600);
        artwork.sprite = assets.FullSprite(descriptor.ArtworkAssetSha256, 100f);
        artwork.color = Color.white;
        hideAt = logicalTicks + Math.Max(240_000, message.DurationTicks);
        root.SetActive(true);
    }

    internal void Tick(long logicalTicks)
    {
        if (root.activeSelf && logicalTicks >= hideAt) root.SetActive(false);
    }

    internal void Clear()
    {
        root.SetActive(false);
        hideAt = 0;
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }
}

internal sealed class ReplayPovHandRuntimeV12 : IDisposable
{
    private readonly GameObject root;
    private readonly ReplayAssetCacheV12 assets;
    private readonly IReadOnlyDictionary<string, ReplayCardDescriptorV12> descriptors;

    internal ReplayPovHandRuntimeV12(
        Transform parent,
        ReplayAssetCacheV12 assets,
        IReadOnlyDictionary<string, ReplayCardDescriptorV12> descriptors,
        bool visible)
    {
        this.assets = assets;
        this.descriptors = descriptors;
        root = ReplayHudRuntimeV12.CreateRect(
            "ReplayPovHand",
            parent,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1440f, 224f));
        root.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 18f);
        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        root.SetActive(visible);
    }

    internal void Apply(IReadOnlyList<ReplayPublicCardStateV12> values)
    {
        foreach (Transform child in root.transform) Object.Destroy(child.gameObject);
        foreach (var value in (values ?? Array.Empty<ReplayPublicCardStateV12>()).Take(9))
        {
            if (!descriptors.TryGetValue(value.DescriptorId ?? "", out var descriptor)) continue;
            var card = ReplayHudRuntimeV12.CreateRect(
                "PovCard:" + value.CardInstanceId,
                root.transform,
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                new Vector2(142f, 204f));
            var panel = card.AddComponent<Image>();
            panel.color = new Color(0.055f, 0.06f, 0.085f, 0.96f);
            var artworkObject = ReplayHudRuntimeV12.CreateRect(
                "Artwork", card.transform, new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.zero);
            var artwork = artworkObject.AddComponent<Image>();
            artwork.sprite = assets.FullSprite(descriptor.ArtworkAssetSha256, 100f);
            artwork.preserveAspect = true;
            artwork.raycastTarget = false;
            var label = ReplayHudRuntimeV12.CreateUiText(card.transform, "Label", 18, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = new Vector2(0.06f, 0.04f);
            label.rectTransform.anchorMax = new Vector2(0.94f, 0.29f);
            label.text = ReplaySceneRuntime.Display(descriptor.Name, 28) + "\n" + value.DisplayedCost;
        }
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }
}

internal sealed class ReplayEffectRuntimeV12 : IDisposable
{
    private readonly Transform parent;
    private readonly ReplayAssetCacheV12 assets;
    private readonly IReadOnlyDictionary<string, ReplayEffectDescriptorV12> descriptors;
    private readonly List<ActiveEffect> active = new();

    internal ReplayEffectRuntimeV12(
        Transform parent,
        ReplayAssetCacheV12 assets,
        IReadOnlyDictionary<string, ReplayEffectDescriptorV12> descriptors)
    {
        this.parent = parent;
        this.assets = assets;
        this.descriptors = descriptors;
    }

    internal void Play(
        ReplayPresentationMessageV12 message,
        Vector3 position,
        long logicalTicks)
    {
        if (!descriptors.TryGetValue(message.EffectDescriptorId ?? "", out var descriptor)) return;
        var root = new GameObject("ReplayEffect:" + descriptor.DescriptorId, typeof(SpriteRenderer));
        root.transform.SetParent(parent, false);
        root.layer = 30;
        root.transform.localPosition = new Vector3(position.x, position.y, -0.5f);
        root.transform.localScale = Vector3.one * 1.8f;
        var renderer = root.GetComponent<SpriteRenderer>();
        var frames = descriptor.Frames.Select(assets.Sprite).Where(item => item != null).Select(item => item!).ToArray();
        renderer.sprite = frames.Length > 0 ? frames[0] : assets.WhiteSprite();
        renderer.color = ReplaySceneRuntime.Color(descriptor.Color);
        renderer.sortingOrder = 400;
        active.Add(new ActiveEffect(
            root,
            renderer,
            frames,
            Math.Max(0.1f, ReplaySceneRuntime.FromQ16(descriptor.FramesPerSecondQ16)),
            logicalTicks,
            logicalTicks + Math.Max(120_000, Math.Max(descriptor.DurationTicks, message.DurationTicks))));
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var value in active.ToList())
        {
            if (logicalTicks >= value.End)
            {
                Object.Destroy(value.Root);
                active.Remove(value);
                continue;
            }
            var progress = (logicalTicks - value.Start) / (float)Math.Max(1, value.End - value.Start);
            if (value.Frames.Length > 0)
            {
                var frame = (int)Math.Floor(
                    Math.Max(0L, logicalTicks - value.Start)
                    / (double)ReplayProtocolV12.TimebaseTicksPerSecond * value.FramesPerSecond);
                value.Renderer.sprite = value.Frames[Math.Min(value.Frames.Length - 1, Math.Max(0, frame))];
            }
            var color = value.Renderer.color;
            color.a = 1f - Mathf.Clamp01(progress);
            value.Renderer.color = color;
            value.Root.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.1f, progress);
        }
    }

    internal void Clear()
    {
        foreach (var value in active) if (value.Root != null) Object.Destroy(value.Root);
        active.Clear();
    }

    public void Dispose() => Clear();

    private sealed class ActiveEffect
    {
        internal ActiveEffect(
            GameObject root,
            SpriteRenderer renderer,
            Sprite[] frames,
            float framesPerSecond,
            long start,
            long end)
        {
            Root = root;
            Renderer = renderer;
            Frames = frames;
            FramesPerSecond = framesPerSecond;
            Start = start;
            End = end;
        }
        internal GameObject Root { get; }
        internal SpriteRenderer Renderer { get; }
        internal Sprite[] Frames { get; }
        internal float FramesPerSecond { get; }
        internal long Start { get; }
        internal long End { get; }
    }
}

internal sealed class ReplayAudioRuntimeV12 : IDisposable
{
    private static readonly MethodInfo? AudioClipSetData = typeof(AudioClip)
        .GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
    private readonly GameObject root;
    private readonly ReplayAssetCacheV12 assets;
    private readonly List<AudioSource> sources = new();
    private readonly Dictionary<AudioSource, float> sourceRates = new();
    private readonly Dictionary<AudioSource, ActiveCue> activeCues = new();
    private readonly List<AudioClip> clips = new();
    private float transportSpeed = 1f;

    internal ReplayAudioRuntimeV12(Transform parent, ReplayAssetCacheV12 assets)
    {
        this.assets = assets;
        root = new GameObject("ReplayAudio");
        root.transform.SetParent(parent, false);
        root.layer = 30;
    }

    internal void Play(ReplayAudioCueV12 cue, long logicalStartTicks, long resumeAtTicks = -1L)
    {
        var bytes = assets.Bytes(cue.AssetSha256);
        if (!TryDecodePcm16(bytes, out var samples, out var channels, out var sampleRate)) return;
        var clip = AudioClip.Create("ReplayAudio:" + cue.AssetSha256, samples.Length / channels, channels, sampleRate, false);
        if (AudioClipSetData?.Invoke(clip, new object[] { samples, 0 }) is not true)
        {
            Object.Destroy(clip);
            return;
        }
        clips.Add(clip);
        var source = root.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Max(0f, cue.GainQ16 / 65_536f);
        source.panStereo = Mathf.Clamp(cue.PanQ16 / 65_536f, -1f, 1f);
        var sourceRate = Mathf.Clamp(cue.PlaybackRateQ16 / 65_536f, 0.1f, 3f);
        source.pitch = Mathf.Clamp(sourceRate * transportSpeed, 0.1f, 3f);
        source.loop = cue.LoopEndSample > cue.LoopStartSample;
        var resumedTicks = resumeAtTicks < logicalStartTicks ? logicalStartTicks : resumeAtTicks;
        var elapsedTimelineSamples = Math.Max(0L, resumedTicks - logicalStartTicks)
                                     * 48_000L / ReplayProtocolV12.TimebaseTicksPerSecond;
        var elapsedSourceFrames = (long)Math.Floor(
            elapsedTimelineSamples * sourceRate * sampleRate / 48_000d);
        var sourceFrame = Math.Max(0L, cue.SourceOffsetSample + elapsedSourceFrames);
        if (source.loop && cue.LoopEndSample > cue.LoopStartSample && sourceFrame >= cue.LoopEndSample)
            sourceFrame = cue.LoopStartSample + (sourceFrame - cue.LoopStartSample)
                % Math.Max(1L, cue.LoopEndSample - cue.LoopStartSample);
        source.timeSamples = (int)Math.Min(clip.samples - 1L, sourceFrame);
        source.Play();
        sources.Add(source);
        sourceRates[source] = sourceRate;
        var durationSamples = cue.DurationSamples > 0
            ? cue.DurationSamples
            : clip.samples * 48_000L / Math.Max(1, clip.frequency);
        activeCues[source] = new ActiveCue(
            logicalStartTicks,
            logicalStartTicks + durationSamples * ReplayProtocolV12.TimebaseTicksPerSecond / 48_000L,
            source.volume,
            cue.FadeInSamples,
            cue.FadeOutSamples);
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var source in sources.ToList())
        {
            if (source == null || !activeCues.TryGetValue(source, out var cue)) continue;
            if (logicalTicks >= cue.EndTicks)
            {
                source.Stop();
                Object.Destroy(source);
                sources.Remove(source);
                sourceRates.Remove(source);
                activeCues.Remove(source);
                continue;
            }
            var elapsedSamples = Math.Max(0L, logicalTicks - cue.StartTicks) * 48_000L / ReplayProtocolV12.TimebaseTicksPerSecond;
            var remainingSamples = Math.Max(0L, cue.EndTicks - logicalTicks) * 48_000L / ReplayProtocolV12.TimebaseTicksPerSecond;
            var envelope = 1f;
            if (cue.FadeInSamples > 0) envelope = Math.Min(envelope, elapsedSamples / (float)cue.FadeInSamples);
            if (cue.FadeOutSamples > 0) envelope = Math.Min(envelope, remainingSamples / (float)cue.FadeOutSamples);
            source.volume = cue.Gain * Mathf.Clamp01(envelope);
        }
    }

    internal void SetTransportSpeed(float speed)
    {
        transportSpeed = Mathf.Clamp(speed, 0.1f, 4f);
        foreach (var source in sources.Where(item => item != null))
            source.pitch = Mathf.Clamp(sourceRates.TryGetValue(source, out var rate) ? rate * transportSpeed : transportSpeed, 0.1f, 3f);
    }

    internal void SetPaused(bool paused)
    {
        foreach (var source in sources.Where(item => item != null))
        {
            if (paused) source.Pause();
            else source.UnPause();
        }
    }

    internal void StopAll()
    {
        foreach (var source in sources.Where(item => item != null))
        {
            source.Stop();
            Object.Destroy(source);
        }
        sources.Clear();
        sourceRates.Clear();
        activeCues.Clear();
        foreach (var clip in clips.Where(item => item != null)) Object.Destroy(clip);
        clips.Clear();
    }

    public void Dispose()
    {
        StopAll();
        if (root != null) Object.Destroy(root);
    }

    private sealed class ActiveCue
    {
        internal ActiveCue(long startTicks, long endTicks, float gain, long fadeInSamples, long fadeOutSamples)
        {
            StartTicks = startTicks;
            EndTicks = Math.Max(startTicks, endTicks);
            Gain = gain;
            FadeInSamples = Math.Max(0L, fadeInSamples);
            FadeOutSamples = Math.Max(0L, fadeOutSamples);
        }
        internal long StartTicks { get; }
        internal long EndTicks { get; }
        internal float Gain { get; }
        internal long FadeInSamples { get; }
        internal long FadeOutSamples { get; }
    }

    private static bool TryDecodePcm16(byte[] bytes, out float[] samples, out int channels, out int sampleRate)
    {
        samples = Array.Empty<float>();
        channels = 0;
        sampleRate = 0;
        if (bytes == null || bytes.Length < 44
            || Encoding(bytes, 0, 4) != "RIFF"
            || Encoding(bytes, 8, 4) != "WAVE"
            || BitConverter.ToInt16(bytes, 20) != 1
            || BitConverter.ToInt16(bytes, 34) != 16)
            return false;
        channels = BitConverter.ToInt16(bytes, 22);
        sampleRate = BitConverter.ToInt32(bytes, 24);
        var offset = 12;
        var dataOffset = -1;
        var dataLength = 0;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding(bytes, offset, 4);
            var length = BitConverter.ToInt32(bytes, offset + 4);
            if (length < 0 || offset + 8L + length > bytes.Length) return false;
            if (id == "data")
            {
                dataOffset = offset + 8;
                dataLength = length;
                break;
            }
            offset += 8 + length + (length & 1);
        }
        if (channels is < 1 or > 2 || sampleRate <= 0 || dataOffset < 0 || dataLength % 2 != 0) return false;
        samples = new float[dataLength / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = BitConverter.ToInt16(bytes, dataOffset + index * 2) / 32768f;
        return true;
    }

    private static string Encoding(byte[] bytes, int offset, int count) =>
        System.Text.Encoding.ASCII.GetString(bytes, offset, count);
}
