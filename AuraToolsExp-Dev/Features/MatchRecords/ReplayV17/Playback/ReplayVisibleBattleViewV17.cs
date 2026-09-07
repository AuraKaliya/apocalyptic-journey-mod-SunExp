using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using AuraReplay.Presentation.Shared;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal sealed class ReplayVisibleBattleViewV17 : IDisposable
{
    private const int ReplayLayer = 30;
    private readonly ReplayPresentationCapsuleV17 capsule;
    private readonly Transform worldRoot;
    private readonly IReadOnlyDictionary<string, ReplayEntityDescriptorV17> entityDescriptors;
    private readonly IReadOnlyDictionary<string, ReplayCardDescriptorV17> cardDescriptors;
    private readonly IReadOnlyDictionary<string, ReplayBuffDescriptorV17> buffDescriptors;
    private readonly IReadOnlyDictionary<string, ReplayIntentDescriptorV17> intentDescriptors;
    private readonly Dictionary<string, ReplayCombatantProjectionV17> combatants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayCombatHudProjectionV17> combatantHuds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayEntityPresentationBindingV17> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeferredPortableExtension> deferredPortableExtensions = new(StringComparer.Ordinal);
    private readonly Transform canvasRoot;
    private readonly Camera camera;
    private readonly ReplayUiTemplateCacheV17 uiTemplates;
    private readonly ReplayExtensionIntentVisualsV17 extensionIntents;
    private readonly ReplaySceneResourceProjectionV17 background = null!;
    private readonly ReplayBattleHudProjectionV17 hud = null!;
    private readonly ReplayHandProjectionV17 hand = null!;
    private readonly ReplayCardInstructionProjectionV17 cardInstruction = null!;
    private readonly ReplayFloatingPresentationV17 floating = null!;
    private readonly ReplayExtensionRendererSetV17 extensionRenderers = null!;
    private bool hudVisible;
    private bool disposed;

    internal ReplayVisibleBattleViewV17(
        ReplayPresentationCapsuleV17 capsule,
        Transform captureRoot,
        Transform canvasRoot,
        Camera camera,
        bool includeHud,
        ReplayExtensionIntentVisualsV17 extensionIntents)
    {
        this.capsule = capsule ?? throw new ArgumentNullException(nameof(capsule));
        this.extensionIntents = extensionIntents ?? throw new ArgumentNullException(nameof(extensionIntents));
        entityDescriptors = Index(capsule.Entities, item => item.DescriptorId);
        cardDescriptors = Index(capsule.Cards, item => item.DescriptorId);
        buffDescriptors = Index(capsule.Buffs, item => item.DescriptorId);
        intentDescriptors = Index(capsule.Intents, item => item.DescriptorId);
        this.canvasRoot = canvasRoot;
        this.camera = camera;
        uiTemplates = new ReplayUiTemplateCacheV17(capsule.Ui);
        PreflightResources();
        hudVisible = includeHud;

        var root = new GameObject("ReplayVisibleBattleProjection");
        root.transform.SetParent(captureRoot, false);
        root.layer = ReplayLayer;
        worldRoot = root.transform;
        try
        {
            background = new ReplaySceneResourceProjectionV17(worldRoot, camera, capsule.Scene);
            hud = new ReplayBattleHudProjectionV17(canvasRoot, uiTemplates, includeHud);
            hand = new ReplayHandProjectionV17(
                hud.HandContainer, (RectTransform)canvasRoot,
                new Vector2(capsule.Scene.ReferenceWidth, capsule.Scene.ReferenceHeight),
                cardDescriptors, uiTemplates, includeHud);
            cardInstruction = new ReplayCardInstructionProjectionV17(
                hud.CenterCardContainer, (RectTransform)canvasRoot,
                new Vector2(capsule.Scene.ReferenceWidth, capsule.Scene.ReferenceHeight),
                cardDescriptors,
                uiTemplates);
            floating = new ReplayFloatingPresentationV17(
                hud.NativeRoot,
                camera,
                new Vector2(capsule.Scene.ReferenceWidth, capsule.Scene.ReferenceHeight));
            extensionRenderers = new ReplayExtensionRendererSetV17(
                capsule.Modules,
                new AuraReplayPresentationRenderContext
                {
                    CanvasRoot = hud.NativeRoot,
                    WorldRoot = worldRoot,
                    Camera = camera,
                    ReferenceWidth = capsule.Scene.ReferenceWidth,
                    ReferenceHeight = capsule.Scene.ReferenceHeight,
                    EntityRootResolver = entityId => combatants.Values.LastOrDefault(item =>
                        string.Equals(item.EntityId, entityId, StringComparison.Ordinal))?.RootTransform
                });
        }
        catch
        {
            try { extensionRenderers?.Dispose(); } catch { }
            try { floating?.Dispose(); } catch { }
            try { cardInstruction?.Dispose(); } catch { }
            try { hand?.Dispose(); } catch { }
            try { hud?.Dispose(); } catch { }
            try { background?.Dispose(); } catch { }
            Object.Destroy(worldRoot.gameObject);
            throw;
        }
    }

    internal void SetHudVisible(bool visible)
    {
        hud.SetVisible(visible);
        hand.SetVisible(visible);
        hudVisible = visible;
        foreach (var value in combatantHuds.Values) value.SetVisible(visible);
    }

    internal void Restore(ReplayVisibleStateV17 state, ReplayPresentationCheckpointV17? checkpoint)
    {
        foreach (var value in combatants.Values) value.Dispose();
        foreach (var value in combatantHuds.Values) value.Dispose();
        combatants.Clear();
        combatantHuds.Clear();
        bindings.Clear();
        ClearTransientPresentation();
        foreach (var binding in checkpoint?.EntityBindings ?? new List<ReplayEntityPresentationBindingV17>())
            BindEntity(binding, state);
        foreach (var view in checkpoint?.EntityViews ?? new List<ReplayEntityViewStateV17>())
            if (combatants.TryGetValue(EntityKey(view.EntityId, view.SpawnGeneration), out var combatant))
                combatant.PlayAnimation(
                    view.AnimationState,
                    view.AnimationStartedTicks,
                    view.AnimationEndsTicks > view.AnimationStartedTicks
                        ? view.AnimationEndsTicks - view.AnimationStartedTicks
                        : 0L,
                    Array.Empty<ReplayWorldTransformSampleV17>());
        ApplyState(state);
    }

    internal void ApplyState(ReplayVisibleStateV17 state)
    {
        if (state == null) return;
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in state.Entities)
        {
            var key = EntityKey(entity.EntityId, entity.SpawnGeneration);
            active.Add(key);
            if (combatants.TryGetValue(key, out var view))
            {
                view.Apply(entity);
                if (combatantHuds.TryGetValue(key, out var entityHud))
                    entityHud.Apply(
                        entity,
                        state.Intents.Where(item =>
                            string.Equals(item.ActorId, entity.EntityId, StringComparison.Ordinal)).ToList());
            }
        }
        foreach (var key in combatants.Keys.Where(key => !active.Contains(key)).ToList())
        {
            var removedEntityId = combatants[key].EntityId;
            combatants[key].Dispose();
            if (combatantHuds.TryGetValue(key, out var entityHud)) entityHud.Dispose();
            combatants.Remove(key);
            combatantHuds.Remove(key);
            bindings.Remove(key);
            foreach (var deferred in deferredPortableExtensions.Values.Where(item =>
                         string.Equals(item.Message.ActorId, removedEntityId, StringComparison.Ordinal)))
                deferred.Applied = false;
        }
        hud.Apply(state);
        hand.Apply(state.PerspectivePlayerId, state.Cards);
    }

    internal void BindEntity(ReplayEntityPresentationBindingV17 source, ReplayVisibleStateV17 state)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.EntityId)) return;
        var binding = ReplayCanonicalJsonV17.Clone(source);
        var key = EntityKey(binding.EntityId, binding.SpawnGeneration);
        if (!entityDescriptors.TryGetValue(binding.DescriptorId, out var descriptor))
            throw new InvalidOperationException("Replay entity descriptor is missing: " + binding.DescriptorId);
        if (!binding.HasMeasuredLayout)
            throw new InvalidOperationException("Replay entity has no measured native layout: " + binding.EntityId);
        if (combatants.TryGetValue(key, out var previous)) previous.Dispose();
        if (combatantHuds.TryGetValue(key, out var previousHud)) previousHud.Dispose();
        bindings[key] = binding;
        var view = ReplayCombatantProjectionV17.Create(
            worldRoot,
            descriptor,
            binding);
        combatants[key] = view;
        foreach (var deferred in deferredPortableExtensions.Values.Where(item =>
                     string.Equals(item.Message.ActorId, binding.EntityId, StringComparison.Ordinal)))
            deferred.Applied = false;
        combatantHuds[key] = new ReplayCombatHudProjectionV17(
            hud.NativeRoot,
            camera,
            new Vector2(capsule.Scene.ReferenceWidth, capsule.Scene.ReferenceHeight),
            descriptor,
            binding,
            buffDescriptors,
            intentDescriptors,
            uiTemplates);
        combatantHuds[key].SetVisible(hudVisible);
        var entity = state.Entities.LastOrDefault(item =>
            string.Equals(item.EntityId, binding.EntityId, StringComparison.Ordinal)
            && item.SpawnGeneration == binding.SpawnGeneration);
        if (entity != null)
        {
            view.Apply(entity);
            combatantHuds[key].Apply(
                entity,
                state.Intents.Where(item => string.Equals(item.ActorId, entity.EntityId, StringComparison.Ordinal)).ToList());
        }
    }

    internal void PresentCardMotion(ReplayPresentationMessageV17 message, long logicalTicks) =>
        cardInstruction.Show(message, logicalTicks);

    internal void PlayEntityAnimation(ReplayPresentationMessageV17 message, long startTicks)
    {
        ApplyCamera(message);
        var combatant = combatants.Values.LastOrDefault(item =>
            string.Equals(item.EntityId, message.ActorId, StringComparison.Ordinal));
        combatant?.PlayAnimation(
            message.AnimationState,
            startTicks,
            message.DurationTicks,
            message.WorldTransformSamples);
    }

    internal void ApplyCamera(ReplayPresentationMessageV17 message)
    {
        if (message?.HasCameraState != true) return;
        camera.transform.localPosition = ReplayPresentationPrimitivesV17.Vector(message.CameraPosition);
        camera.transform.localEulerAngles = ReplayPresentationPrimitivesV17.Vector(message.CameraRotation);
        if (camera.orthographic && message.CameraOrthographicSizeQ16 > 0)
            camera.orthographicSize = ReplayPresentationPrimitivesV17.FromQ16(message.CameraOrthographicSizeQ16);
    }

    internal void PresentDamageText(ReplayPresentationMessageV17 message, long logicalTicks)
    {
        var target = message.TargetIds.FirstOrDefault() ?? message.ActorId;
        floating.ShowDamage(message, PositionForEntity(target), logicalTicks);
    }

    internal void PresentTurnTransition(ReplayPresentationMessageV17 message, long logicalTicks) =>
        floating.ShowBanner(
            string.IsNullOrWhiteSpace(message.DisplayText) ? "Turn" : message.DisplayText,
            logicalTicks,
            message.DurationTicks);

    internal void PresentExtension(ReplayPresentationMessageV17 message, long logicalTicks)
    {
        hud.PresentExtension(message, logicalTicks);
        extensionRenderers.Apply(message, logicalTicks);
        if (!IsPortableEntityExtension(message)) return;
        var deferredKey = DeferredExtensionKey(message);
        if (message.Persistent)
            deferredPortableExtensions[deferredKey] = new DeferredPortableExtension(
                ReplayFastCloneV17.Presentation(message),
                logicalTicks);
        var applied = ApplyPortableEntityExtension(message, logicalTicks);
        if (message.Persistent && deferredPortableExtensions.TryGetValue(deferredKey, out var persistent))
            persistent.Applied = applied;
        else if (!applied)
            deferredPortableExtensions[deferredKey] = new DeferredPortableExtension(
                ReplayFastCloneV17.Presentation(message),
                logicalTicks);
    }

    private bool ApplyPortableEntityExtension(ReplayPresentationMessageV17 message, long logicalTicks)
    {
        var combatantPair = combatants.LastOrDefault(item =>
            string.Equals(item.Value.EntityId, message.ActorId, StringComparison.Ordinal));
        var combatant = combatantPair.Value;
        if (combatant == null) return false;
        combatantHuds.TryGetValue(combatantPair.Key, out var entityHud);
        if (string.Equals(message.Kind, "OwnerAttachedFocus", StringComparison.Ordinal))
        {
            var payload = ParsePayload(message.ExtensionPayloadJson);
            var directed = payload.Value<string>("intentType") is "Attack" or "Interference";
            var direction = directed ? Vector2.right : Vector2.up;
            var target = combatants.Values.FirstOrDefault(item =>
                (message.TargetIds ?? new List<string>()).Contains(item.EntityId));
            if (directed && target != null)
            {
                var sourcePoint = camera.WorldToViewportPoint(combatant.BodyWorldBounds.center);
                var targetPoint = camera.WorldToViewportPoint(
                    target.RootTransform.Find("Center")?.position ?? target.BodyWorldBounds.center);
                var delta = new Vector2((targetPoint.x - sourcePoint.x) * camera.aspect, targetPoint.y - sourcePoint.y);
                if (delta.sqrMagnitude > 0.000001f) direction = delta.normalized;
            }
            combatant.PlayPortableFocus(
                logicalTicks,
                Math.Max(1L, message.DurationTicks),
                payload.Value<int?>("travelPixels") ?? 0,
                (payload.Value<int?>("peakScaleQ16") ?? 65_536) / 65_536f,
                direction);
        }
        else if (string.Equals(message.Kind, "VisibilityChanged", StringComparison.Ordinal))
        {
            var payload = ParsePayload(message.ExtensionPayloadJson);
            combatant.SetExtensionVisible(payload.Value<bool?>("visible") ?? true);
            if (payload.Value<bool?>("visible") == false) entityHud?.ClearExtensionIntent();
        }
        else if (string.Equals(message.Kind, "IntentChanged", StringComparison.Ordinal))
        {
            entityHud?.PresentExtensionIntent(extensionIntents.Get(message.ExtensionPayloadJson));
        }
        return true;
    }

    internal Vector3 PositionForEntity(string entityId)
    {
        var combatant = combatants.Values.LastOrDefault(item =>
            string.Equals(item.EntityId, entityId, StringComparison.Ordinal));
        return combatant?.Position ?? Vector3.zero;
    }

    internal void ClearTransientPresentation()
    {
        cardInstruction.Clear();
        floating.Clear();
        extensionRenderers.Reset();
        deferredPortableExtensions.Clear();
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var pair in combatants) pair.Value.Tick(logicalTicks);
        foreach (var pair in combatants)
        {
            if (!bindings.TryGetValue(pair.Key, out var binding)
                || binding.CustomPresentation == null
                || string.IsNullOrWhiteSpace(binding.CustomPresentation.OwnerEntityId)) continue;
            var owner = combatants.Values.LastOrDefault(item =>
                string.Equals(item.EntityId, binding.CustomPresentation.OwnerEntityId, StringComparison.Ordinal));
            if (owner != null)
                pair.Value.ApplyCustomPresentation(
                    owner,
                    camera,
                    Math.Max(1, capsule.Scene.ReferenceHeight),
                    binding.CustomPresentation);
        }
        foreach (var pair in combatants)
        {
            if (combatantHuds.TryGetValue(pair.Key, out var entityHud))
                entityHud.UpdateWorldAnchors(
                    pair.Value.BottomWorldPosition,
                    pair.Value.HeadWorldPosition,
                    pair.Value.AttachmentWorldBounds);
        }
        cardInstruction.Tick(logicalTicks);
        hand.SetMovingSources(cardInstruction.ActiveSourceIds);
        floating.Tick(logicalTicks);
        hud.Tick(logicalTicks);
        extensionRenderers.Tick(logicalTicks);
        foreach (var pair in deferredPortableExtensions.ToList())
        {
            var value = pair.Value;
            if (value.Applied) continue;
            var active = value.Message.Persistent
                         || logicalTicks < value.StartTicks + Math.Max(1L, value.Message.DurationTicks);
            if (!active)
            {
                deferredPortableExtensions.Remove(pair.Key);
                continue;
            }
            value.Applied = ApplyPortableEntityExtension(value.Message, value.StartTicks);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var value in combatants.Values) value.Dispose();
        foreach (var value in combatantHuds.Values) value.Dispose();
        combatants.Clear();
        combatantHuds.Clear();
        bindings.Clear();
        deferredPortableExtensions.Clear();
        floating.Dispose();
        extensionRenderers.Dispose();
        cardInstruction.Dispose();
        hand.Dispose();
        hud.Dispose();
        background.Dispose();
        if (worldRoot != null) Object.Destroy(worldRoot.gameObject);
    }

    private static IReadOnlyDictionary<string, T> Index<T>(IEnumerable<T> values, Func<T, string> key) =>
        (values ?? Array.Empty<T>())
        .Where(item => item != null && !string.IsNullOrWhiteSpace(key(item)))
        .GroupBy(key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

    private static string EntityKey(string entityId, int generation) => entityId + "|" + generation;

    private static bool IsPortableEntityExtension(ReplayPresentationMessageV17 message) =>
        message != null
        && !string.IsNullOrWhiteSpace(message.ActorId)
        && (string.Equals(message.Kind, "OwnerAttachedFocus", StringComparison.Ordinal)
            || string.Equals(message.Kind, "VisibilityChanged", StringComparison.Ordinal)
            || string.Equals(message.Kind, "IntentChanged", StringComparison.Ordinal));

    private static string DeferredExtensionKey(ReplayPresentationMessageV17 message) =>
        message.Persistent
            ? (message.ExtensionOwnerModId ?? "") + "|" + (message.ExtensionTypeId ?? "") + "|"
              + (message.ActorId ?? "") + "|" + (message.Kind ?? "")
            : "event|" + (message.ExtensionEventId ?? "");

    private static JObject ParsePayload(string value)
    {
        try { return JObject.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value); }
        catch { return new JObject(); }
    }

    private sealed class DeferredPortableExtension
    {
        internal DeferredPortableExtension(ReplayPresentationMessageV17 message, long startTicks)
        {
            Message = message;
            StartTicks = startTicks;
        }

        internal ReplayPresentationMessageV17 Message { get; }
        internal long StartTicks { get; }
        internal bool Applied { get; set; }
    }

    private void PreflightResources()
    {
        foreach (var descriptor in entityDescriptors.Values)
            foreach (var animation in descriptor.Animations)
                if (ReplayResourceResolverV17.Sprites(animation.ResourcePath, animation.FrameNames).Length == 0)
                    throw new InvalidOperationException(
                        "Replay animation resource is missing: " + descriptor.DescriptorId + "/" + animation.State);
        foreach (var descriptor in cardDescriptors.Values)
        {
            _ = ReplayResourceResolverV17.RequiredSprite(
                string.IsNullOrWhiteSpace(descriptor.ResolvedSkinFrameResourcePath)
                    ? descriptor.FrameResourcePath
                    : descriptor.ResolvedSkinFrameResourcePath,
                "card-frame:" + descriptor.DescriptorId);
            _ = ReplayResourceResolverV17.RequiredTextureOrSprite(
                descriptor.IconResourcePath,
                "card-artwork:" + descriptor.DescriptorId);
        }
        foreach (var descriptor in buffDescriptors.Values)
            _ = ReplayResourceResolverV17.RequiredSprite(descriptor.IconResourcePath, "buff:" + descriptor.DescriptorId);
        foreach (var descriptor in intentDescriptors.Values)
        {
            _ = ReplayResourceResolverV17.RequiredSprite(
                descriptor.BackIconResourcePath,
                "intent-background:" + descriptor.DescriptorId);
            _ = ReplayResourceResolverV17.RequiredSprite(descriptor.IconResourcePath, "intent-icon:" + descriptor.DescriptorId);
        }
    }
}

internal sealed class ReplaySceneResourceProjectionV17 : IDisposable
{
    private const int ReplayLayer = 30;
    private readonly GameObject root;

    internal ReplaySceneResourceProjectionV17(
        Transform parent,
        Camera camera,
        ReplaySceneDescriptorV17 scene)
    {
        root = new GameObject("ReplaySceneResource:" + (scene.SceneResourceId ?? "scene"));
        root.transform.SetParent(parent, false);
        root.layer = ReplayLayer;
        var sceneId = scene.SceneResourceId ?? "";
        var candidates = new[]
        {
            scene.SceneResourcePath ?? "",
            sceneId,
            "Scene/" + sceneId,
            "Background/" + sceneId,
            "UI/Scene/" + sceneId
        }.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Any(TryCopyPrefabSprites)) return;
        var sprite = candidates.Select(ReplayResourceResolverV17.Sprite).FirstOrDefault(item => item != null);
        if (sprite == null)
            throw new InvalidOperationException("Replay scene resource is missing: " + scene.SceneResourcePath);
        var value = new GameObject("BackgroundSprite", typeof(SpriteRenderer));
        value.transform.SetParent(root.transform, false);
        value.layer = ReplayLayer;
        value.transform.localPosition = new Vector3(0f, 0f, 8f);
        var renderer = value.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -10_000;
        var height = camera.orthographicSize * 2f;
        var width = height * camera.aspect;
        value.transform.localScale = new Vector3(
            width / Math.Max(0.01f, sprite.bounds.size.x),
            height / Math.Max(0.01f, sprite.bounds.size.y),
            1f);
    }

    private bool TryCopyPrefabSprites(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var prefab = AuraToolsResourceCache.Load<GameObject>(path, true)
                     ?? AuraToolsResourceCache.Load<GameObject>(path, false);
        if (prefab == null) return false;
        var renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var source in renderers.Where(item => item?.sprite != null))
        {
            var value = new GameObject("SceneSprite:" + source.name, typeof(SpriteRenderer));
            value.transform.SetParent(root.transform, false);
            value.layer = ReplayLayer;
            value.transform.localPosition = prefab.transform.InverseTransformPoint(source.transform.position);
            value.transform.localRotation = Quaternion.Inverse(prefab.transform.rotation) * source.transform.rotation;
            value.transform.localScale = source.transform.lossyScale;
            var target = value.GetComponent<SpriteRenderer>();
            target.sprite = source.sprite;
            target.color = source.color;
            target.flipX = source.flipX;
            target.flipY = source.flipY;
            target.sortingOrder = source.sortingOrder - 10_000;
            target.sortingLayerID = source.sortingLayerID;
        }
        return renderers.Any(item => item?.sprite != null);
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }
}

internal sealed class ReplayBattleHudProjectionV17 : IDisposable
{
    private readonly GameObject root;
    private readonly TMP_Text round;
    private readonly TMP_Text power;
    private readonly TMP_Text drawCount;
    private readonly TMP_Text discardCount;
    private readonly GameObject singleSkillRoot;
    private readonly GameObject pairedSkillRoot;
    private long extensionHideAt;
    private string persistentRoundText = "";

    internal ReplayBattleHudProjectionV17(
        Transform parent,
        ReplayUiTemplateCacheV17 templates,
        bool visible)
    {
        root = ReplayNativePrefabInstanceV17.Clone(
            templates.FightUiTemplate,
            parent,
            "ReplayNativeFightUI");
        if (root.transform is RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
        }
        power = RequiredText("Left/Time/total/val");
        drawCount = RequiredText("Left/Card/val");
        discardCount = RequiredText("ClockBoard/弃牌堆/val");
        round = RequiredText("Process/Tip/Text");
        singleSkillRoot = root.transform.Find("Left/Skill1")?.gameObject
                          ?? throw new InvalidOperationException("Native replay FightUI has no single skill root.");
        pairedSkillRoot = root.transform.Find("Left/Skill2")?.gameObject
                          ?? throw new InvalidOperationException("Native replay FightUI has no paired skill root.");
        HideControl("ClockBoard/确定");
        HideControl("ClockBoard/结束战斗");
        HideControl("ClockBoard/重开战斗");
        DisableControl("ClockBoard/结束回合");
        root.SetActive(visible);
    }

    internal Transform HandContainer => root.transform.Find("container")
        ?? throw new InvalidOperationException("Native replay FightUI has no hand container.");

    internal Transform CenterCardContainer => root.transform.Find("CenterCardContainer")
        ?? throw new InvalidOperationException("Native replay FightUI has no center-card container.");

    internal Transform NativeRoot => root.transform;

    internal void Apply(ReplayVisibleStateV17 state)
    {
        var resource = state.Resources.FirstOrDefault(item =>
            string.Equals(item.ResourceId, "Power", StringComparison.OrdinalIgnoreCase));
        power.text = resource == null
            ? "0/0"
            : string.IsNullOrWhiteSpace(resource.DisplayText)
                ? resource.Value + "/" + resource.Maximum
                : resource.DisplayText;
        drawCount.text = Zone(state, "Draw").ToString();
        discardCount.text = Zone(state, "Discard").ToString();
        var skillOne = state.Resources.FirstOrDefault(item =>
            string.Equals(item.ResourceId, "Skill1", StringComparison.OrdinalIgnoreCase));
        var skillTwo = state.Resources.FirstOrDefault(item =>
            string.Equals(item.ResourceId, "Skill2", StringComparison.OrdinalIgnoreCase));
        if (skillTwo != null)
        {
            singleSkillRoot.SetActive(false);
            pairedSkillRoot.SetActive(true);
            BindSkill(root.transform.Find("Left/Skill2/Skill1"), skillOne);
            BindSkill(root.transform.Find("Left/Skill2/Skill2"), skillTwo);
        }
        else
        {
            pairedSkillRoot.SetActive(false);
            singleSkillRoot.SetActive(skillOne != null);
            BindSkill(root.transform.Find("Left/Skill1"), skillOne);
        }
        persistentRoundText = "Round " + Math.Max(1, state.RoundSequence)
                              + "  ·  Turn " + Math.Max(1, state.ActorTurnSequence)
                              + (string.IsNullOrWhiteSpace(state.Outcome) ? "" : "  ·  " + state.Outcome);
        if (extensionHideAt == 0) round.text = persistentRoundText;
    }

    internal void PresentExtension(ReplayPresentationMessageV17 message, long logicalTicks)
    {
        round.text = string.IsNullOrWhiteSpace(message.DisplayText)
            ? message.ExtensionOwnerModId + " · " + message.ExtensionTypeId
            : message.DisplayText;
        extensionHideAt = logicalTicks + Math.Max(240_000L, message.DurationTicks);
    }

    internal void Tick(long logicalTicks)
    {
        if (extensionHideAt > 0 && logicalTicks >= extensionHideAt)
        {
            round.text = persistentRoundText;
            extensionHideAt = 0;
        }
    }

    internal void SetVisible(bool visible) => root.SetActive(visible);

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }

    private TMP_Text RequiredText(string path) => root.transform.Find(path)?.GetComponent<TMP_Text>()
        ?? throw new InvalidOperationException("Native replay FightUI text node is missing: " + path);

    private void HideControl(string path)
    {
        var value = root.transform.Find(path)
                    ?? throw new InvalidOperationException("Native replay FightUI control is missing: " + path);
        value.gameObject.SetActive(false);
    }

    private void DisableControl(string path)
    {
        var value = root.transform.Find(path)
                    ?? throw new InvalidOperationException("Native replay FightUI control is missing: " + path);
        foreach (var selectable in value.GetComponentsInChildren<Selectable>(true)) selectable.interactable = false;
    }

    private static int Zone(ReplayVisibleStateV17 state, string zone) => state.ZoneCounts
        .Where(item => string.Equals(item.Zone, zone, StringComparison.OrdinalIgnoreCase))
        .Select(item => item.Count)
        .DefaultIfEmpty(0)
        .Max();

    private static void BindSkill(Transform? root, ReplayVisibleResourceStateV17? value)
    {
        if (root == null) return;
        root.gameObject.SetActive(value != null);
        if (value == null) return;
        var icon = root.Find("Icon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = ReplayResourceResolverV17.RequiredSprite(
                value.ResourcePath,
                "fight-skill:" + value.ResourceId);
            icon.enabled = true;
            icon.raycastTarget = false;
        }
        var cooldown = root.Find("CD");
        if (cooldown != null) cooldown.gameObject.SetActive(value.Value > 0);
        var text = root.Find("CD/val")?.GetComponent<TMP_Text>();
        if (text != null) text.text = Math.Max(0, value.Value).ToString();
    }
}

internal sealed class ReplayFloatingPresentationV17 : IDisposable
{
    private readonly RectTransform canvasRect;
    private readonly Camera camera;
    private readonly Vector2 referenceResolution;
    private readonly GameObject bannerRoot;
    private readonly Text banner;
    private readonly List<FloatingText> values = new();
    private long bannerHideAt;

    internal ReplayFloatingPresentationV17(Transform canvasParent, Camera camera, Vector2 referenceResolution)
    {
        canvasRect = canvasParent as RectTransform
                     ?? throw new InvalidOperationException("Replay capture canvas has no RectTransform.");
        this.camera = camera;
        this.referenceResolution = referenceResolution;
        bannerRoot = ReplayUiV17.Rect(
            "ReplayTurnBanner",
            canvasParent,
            new Vector2(0.5f, 0.66f),
            new Vector2(0.5f, 0.66f),
            new Vector2(0.5f, 0.5f),
            new Vector2(720f, 86f));
        banner = ReplayUiV17.Text(bannerRoot.transform, "Text", 36, TextAnchor.MiddleCenter);
        bannerRoot.SetActive(false);
    }

    internal void ShowDamage(ReplayPresentationMessageV17 message, Vector3 position, long logicalTicks)
    {
        var root = ReplayUiV17.Rect(
            "ReplayDamageText", canvasRect,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(260f, 72f));
        var text = ReplayUiV17.Text(root.transform, "Text", 42, TextAnchor.MiddleCenter);
        text.text = string.IsNullOrWhiteSpace(message.DisplayText)
            ? (message.Value == 0 ? "" : message.Value.ToString())
            : message.DisplayText;
        text.color = message.Value < 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.25f, 0.32f);
        var viewport = camera.WorldToViewportPoint(position + Vector3.up * 1.5f);
        var canvasSize = canvasRect.rect.size;
        if (canvasSize.x <= 0f || canvasSize.y <= 0f) canvasSize = referenceResolution;
        var basePosition = new Vector2(
            (viewport.x - 0.5f) * canvasSize.x,
            (viewport.y - 0.5f) * canvasSize.y);
        root.GetComponent<RectTransform>().anchoredPosition = basePosition;
        values.Add(new FloatingText(
            root,
            text,
            basePosition,
            logicalTicks,
            logicalTicks + Math.Max(240_000L, message.DurationTicks)));
    }

    internal void ShowBanner(string text, long logicalTicks, long durationTicks)
    {
        banner.text = text ?? "";
        bannerHideAt = logicalTicks + Math.Max(240_000L, durationTicks);
        bannerRoot.SetActive(true);
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var value in values.ToList())
        {
            if (logicalTicks >= value.End)
            {
                Object.Destroy(value.Root);
                values.Remove(value);
                continue;
            }
            var progress = Mathf.Clamp01((logicalTicks - value.Start) / (float)Math.Max(1L, value.End - value.Start));
            value.Root.GetComponent<RectTransform>().anchoredPosition = value.BasePosition + Vector2.up * (progress * 70f);
            var color = value.Text.color;
            color.a = 1f - progress;
            value.Text.color = color;
        }
        if (bannerRoot.activeSelf && logicalTicks >= bannerHideAt) bannerRoot.SetActive(false);
    }

    internal void Clear()
    {
        foreach (var value in values) if (value.Root != null) Object.Destroy(value.Root);
        values.Clear();
        bannerRoot.SetActive(false);
        bannerHideAt = 0;
    }

    public void Dispose()
    {
        Clear();
        if (bannerRoot != null) Object.Destroy(bannerRoot);
    }

    private sealed class FloatingText
    {
        internal FloatingText(GameObject root, Text text, Vector2 basePosition, long start, long end)
        {
            Root = root;
            Text = text;
            BasePosition = basePosition;
            Start = start;
            End = end;
        }

        internal GameObject Root { get; }
        internal Text Text { get; }
        internal Vector2 BasePosition { get; }
        internal long Start { get; }
        internal long End { get; }
    }
}

internal static class ReplayResourceResolverV17
{
    internal static Sprite? Sprite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return AuraToolsResourceCache.Load<Sprite>(path, true)
               ?? AuraToolsResourceCache.Load<Sprite>(path, false)
               ?? AuraToolsResourceCache.LoadAll<Sprite>(path).FirstOrDefault();
    }

    internal static Sprite[] Sprites(string path)
        => Sprites(path, Array.Empty<string>());

    internal static Sprite[] Sprites(string path, IReadOnlyList<string> frameNames)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<Sprite>();
        var values = AuraToolsResourceCache.LoadAll<Sprite>(path)
            .Where(item => item != null)
            .ToArray();
        Array.Sort(values, (left, right) => ReplayNativeFrameNameComparerV17.Instance.Compare(left.name, right.name));
        if (frameNames != null && frameNames.Count > 0)
        {
            if (!ReplayFrameSequenceContractV17.TryResolveOrdered(
                    values,
                    item => item.name,
                    frameNames,
                    out var ordered,
                    out var error))
                throw new InvalidOperationException(
                    "Replay resource frame sequence is incompatible: " + path + ":" + error);
            return ordered.ToArray();
        }
        if (values.Length > 0) return values;
        var single = Sprite(path);
        return single == null ? Array.Empty<Sprite>() : new[] { single };
    }

    internal static Sprite RequiredSprite(string path, string usage) => Sprite(path)
        ?? throw new InvalidOperationException("Replay required sprite is missing: " + usage + " -> " + path);

    internal static Object RequiredTextureOrSprite(string path, string usage)
    {
        var value = (Object?)Sprite(path)
                    ?? AuraToolsResourceCache.Load<Texture>(path, true)
                    ?? AuraToolsResourceCache.Load<Texture>(path, false);
        return value ?? throw new InvalidOperationException(
            "Replay required card texture is missing: " + usage + " -> " + path);
    }
}

internal static class ReplayUiV17
{
    internal static GameObject Rect(
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

    internal static Text Text(Transform parent, string name, int fontSize, TextAnchor anchor)
    {
        var value = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
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

    internal static TextMeshProUGUI Tmp(
        Transform parent,
        string name,
        TMP_FontAsset font,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        var value = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        var text = value.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    internal static GameObject CreateCard(
        Transform parent,
        ReplayVisibleCardStateV17 state,
        ReplayCardDescriptorV17? descriptor,
        Vector2 size,
        GameObject nativeTemplate)
    {
        var card = ReplayNativePrefabInstanceV17.Clone(
            nativeTemplate ?? throw new ArgumentNullException(nameof(nativeTemplate)),
            parent,
            "ReplayCard:" + state.CardInstanceId);
        if (card.transform is not RectTransform rect)
            throw new InvalidOperationException("Native replay CardItem template has no RectTransform.");
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        UpdateCard(card, state, descriptor);
        return card;
    }

    internal static void UpdateCard(
        GameObject card,
        ReplayVisibleCardStateV17 state,
        ReplayCardDescriptorV17? descriptor)
    {
        if (descriptor == null) throw new InvalidOperationException("Native replay card descriptor is missing.");
        var front = card.transform.Find("Front");
        if (front == null) throw new InvalidOperationException("Native replay CardItem has no Front node.");
        if (state.IsRevealed)
            ReplayNativeCardPresentationApi.Apply(
                card.transform,
                state.CardInstanceId,
                descriptor.Provenance?.StableContentId ?? descriptor.DescriptorId,
                string.IsNullOrWhiteSpace(state.RenderedName) ? descriptor.Name : state.RenderedName,
                string.IsNullOrWhiteSpace(state.RenderedDescription) ? descriptor.Description : state.RenderedDescription,
                descriptor.IconResourcePath,
                descriptor.Rarity,
                descriptor.Tag,
                state.DisplayedCost,
                descriptor.ThemeProfile,
                descriptor.SkinId,
                descriptor.DynamicEffectId,
                descriptor.DynamicEffectParametersJson,
                state.EnchantIconResourcePath);
        var back = card.transform.Find("Back");
        if (front != null) front.gameObject.SetActive(state.IsRevealed);
        if (back != null) back.gameObject.SetActive(!state.IsRevealed);
    }

    private static void SetNodeSprite(Transform? node, Sprite? sprite)
    {
        if (node == null) return;
        var image = node.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
        }
        var renderer = node.GetComponent<SpriteRenderer>();
        if (renderer != null) renderer.sprite = sprite;
        var mesh = node.GetComponent<MeshRenderer>();
        if (mesh != null && sprite != null)
        {
            var properties = new MaterialPropertyBlock();
            mesh.GetPropertyBlock(properties);
            properties.SetTexture("_MainTex", sprite.texture);
            mesh.SetPropertyBlock(properties);
        }
    }

    private static void SetText(Transform? node, string value)
    {
        var text = node?.GetComponent<TMP_Text>();
        if (text != null) text.text = value ?? "";
    }

    private static string Display(string value, int maximumCharacters)
    {
        var normalized = (value ?? "").Replace("\r", "").Trim();
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized.Substring(0, Math.Max(1, maximumCharacters - 1)) + ".";
    }
}
