using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AuraReplay.Presentation.Shared;
using AuraReplay.VisibleState.Shared;
using AuraToolsExp.Dll.Features.CardVisual;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Witch;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording;

internal sealed class ReplayCapturedActionSourceV17
{
    internal string Kind { get; set; } = ReplayTransactionKindsV17.Card;
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

internal sealed class ReplayCaptureCatalogV17
{
    private readonly Dictionary<string, ReplayContentProvenanceV17> provenanceCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> ownerVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ReplayAssetV17> assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ReplayEntityDescriptorV17> entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayCardDescriptorV17> cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayBuffDescriptorV17> buffs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayIntentDescriptorV17> intents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayEffectDescriptorV17> effects = new(StringComparer.Ordinal);
    private readonly HashSet<string> presentationModuleKeys = new(StringComparer.Ordinal);
    private int revision;
    internal ReplayPresentationCapsuleV17 Capsule => CreateCapsule();

    internal ReplayPresentationCapsuleV17 DetachCapsule() => CreateCapsule();

    private ReplayPresentationCapsuleV17 CreateCapsule()
    {
        return new ReplayPresentationCapsuleV17
        {
            Scene = CloneScene(Scene),
            Ui = ReplayCanonicalJsonV17.Clone(Ui),
            Entities = entities.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
                .ToList(),
            Cards = cards.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
                .ToList(),
            Buffs = buffs.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
                .ToList(),
            Intents = intents.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
                .ToList(),
            Effects = effects.Values.OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
                .ToList(),
            Modules = AuraReplayPresentationRuntime.SnapshotModules()
                .Where(item => presentationModuleKeys.Contains(item.OwnerModId + "|" + item.TypeId))
                .Select(item => new ReplayPresentationModuleRequirementV17
                {
                    OwnerModId = item.OwnerModId,
                    TypeId = item.TypeId,
                    SchemaVersion = item.SchemaVersion,
                    Portability = item.Portability,
                    BuildIdentity = item.BuildIdentity,
                    RendererCapability = item.RendererCapability
                })
                .OrderBy(item => item.OwnerModId, StringComparer.Ordinal)
                .ThenBy(item => item.TypeId, StringComparer.Ordinal)
                .ToList()
        };
    }

    internal ReplaySceneDescriptorV17 Scene { get; } = CreateSceneDescriptor();

    internal ReplayUiTemplateDescriptorV17 Ui { get; } = new() { HandPresentationContract = ReplayHandLifecycleContractV17.Contract };

    internal int AssetCount => assets.Count;
    internal long AssetBytes => assets.Values.Sum(asset => asset.Payload?.LongLength ?? asset.ByteLength);
    internal int DescriptorCount => entities.Count + cards.Count + intents.Count + buffs.Count + effects.Count;

    internal int Revision => revision;

    internal void ObservePresentationModule(string ownerModId, string typeId)
    {
        var key = (ownerModId ?? "").Trim() + "|" + (typeId ?? "").Trim();
        if (key == "|" || !presentationModuleKeys.Add(key)) return;
        revision++;
    }

    internal List<ReplayAssetV17> DetachAssets()
    {
        var result = assets.Values.OrderBy(item => item.Sha256, StringComparer.Ordinal).ToList();
        assets.Clear();
        return result;
    }

    internal List<ReplayAssetV17> SnapshotAssets() => assets.Values
        .OrderBy(item => item.Sha256, StringComparer.Ordinal)
        .Select(item => new ReplayAssetV17
        {
            Sha256 = item.Sha256 ?? "",
            MediaType = item.MediaType ?? "",
            Extension = item.Extension ?? "",
            Usage = item.Usage ?? "",
            ByteLength = item.ByteLength,
            Width = item.Width,
            Height = item.Height,
            SampleRate = item.SampleRate,
            Channels = item.Channels,
            SampleFrames = item.SampleFrames,
            Required = item.Required,
            Payload = item.Payload == null ? Array.Empty<byte>() : (byte[])item.Payload.Clone()
        })
        .ToList();

    private static ReplaySceneDescriptorV17 CloneScene(ReplaySceneDescriptorV17 value) => new()
    {
        DescriptorId = value.DescriptorId ?? "scene",
        ReferenceWidth = value.ReferenceWidth,
        ReferenceHeight = value.ReferenceHeight,
        BackgroundAssetSha256 = value.BackgroundAssetSha256 ?? "",
        ClearColor = ReplayFastCloneV17.Color(value.ClearColor),
        CameraOrthographicSizeQ16 = value.CameraOrthographicSizeQ16,
        CameraPosition = new ReplayVector3Q16V17
        {
            X = value.CameraPosition?.X ?? 0,
            Y = value.CameraPosition?.Y ?? 0,
            Z = value.CameraPosition?.Z ?? 0
        },
        CameraRotation = new ReplayVector3Q16V17
        {
            X = value.CameraRotation?.X ?? 0,
            Y = value.CameraRotation?.Y ?? 0,
            Z = value.CameraRotation?.Z ?? 0
        },
        CameraOrthographic = value.CameraOrthographic,
        CameraFieldOfViewQ16 = value.CameraFieldOfViewQ16,
        SceneResourcePath = value.SceneResourcePath ?? "",
        SceneResourceId = value.SceneResourceId ?? ""
    };

    internal void CaptureBackground(GameObject? background)
    {
        var sceneName = background?.name ?? "";
        Scene.SceneResourceId = sceneName;
        Scene.SceneResourcePath = sceneName;
        Scene.ReferenceWidth = Math.Max(1, Screen.width);
        Scene.ReferenceHeight = Math.Max(1, Screen.height);
        revision++;
        var camera = Camera.main;
        if (camera == null) return;
        Scene.CameraPosition = Vector(camera.transform.position);
        Scene.CameraRotation = Vector(camera.transform.eulerAngles);
        Scene.CameraOrthographic = camera.orthographic;
        Scene.CameraOrthographicSizeQ16 = Quantize(camera.orthographicSize);
        Scene.CameraFieldOfViewQ16 = Quantize(camera.fieldOfView);
        Scene.ClearColor = ParseColor(ColorUtility.ToHtmlStringRGBA(camera.backgroundColor));
    }

    internal ReplayEntityDescriptorV17 RegisterEntity(
        StatusManager? status,
        string archetype,
        IDataConfig? config,
        string stableId)
    {
        var provenance = Provenance(EntityContentKind(archetype), stableId);
        var descriptorId = DescriptorId("entity", provenance);
        if (entities.TryGetValue(descriptorId, out var existing)) return existing;
        var descriptor = new ReplayEntityDescriptorV17
        {
            DescriptorId = descriptorId,
            Archetype = archetype,
            Provenance = provenance,
            Name = First(Read(config?.Vars, "Name"), Localize(config, "Name"), Read(config?.data, "Name"), stableId),
            Subtitle = First(Read(config?.Vars, "Tag"), Read(config?.data, "Tag"), EntityContentKind(archetype)),
            NativePrefabResourcePath = First(Read(config?.Vars, "Model"), Read(config?.data, "Model")),
            IdleResourcePath = AnimationResource(config, "Idle"),
            PortraitResourcePath = First(
                Read(config?.Vars, "CareerImage"), Read(config?.data, "CareerImage"),
                Read(config?.Vars, "Avatar"), Read(config?.data, "Avatar"),
                Read(config?.Vars, "ChoiceIcon"), Read(config?.data, "ChoiceIcon"))
        };
        CaptureAnimationCatalog(status, config, descriptor);
        if (descriptor.Animations.Count == 0)
        {
            descriptor.Animations.Add(new ReplayAnimationDescriptorV17
            {
                State = "Idle",
                Loop = true,
                ResourcePath = descriptor.IdleResourcePath
            });
        }
        entities.Add(descriptorId, descriptor);
        revision++;
        return descriptor;
    }

    internal ReplayCardDescriptorV17 RegisterCard(IDataConfig? config, string stableId)
    {
        var provenance = Provenance("Card", stableId);
        var descriptorId = DescriptorId("card", provenance);
        if (cards.TryGetValue(descriptorId, out var existing)) return existing;
        var data = config?.data;
        var vars = config?.Vars;
        var artworkResource = First(
            Read(vars, "CardImage"),
            Read(data, "CardImage"),
            Read(vars, "Picture"),
            Read(data, "Picture"),
            Read(vars, "Icon"),
            Read(data, "Icon"));
        var rarity = First(Read(vars, "Rarity"), Read(data, "Rarity"), "1");
        var tag = First(Read(vars, "Tag"), Read(data, "Tag"));
        var frameResource = tag.IndexOf("Curse", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Icon/CardTemplate/NewTemplate/银卡"
            : rarity == "3"
                ? "Icon/CardTemplate/NewTemplate/彩卡"
                : rarity == "2"
                    ? "Icon/CardTemplate/NewTemplate/金卡"
                    : "Icon/CardTemplate/NewTemplate/铜卡";
        var visual = config == null
            ? new Dictionary<string, string>()
            : AuraToolsCardVisualRuntime.CaptureReplaySnapshot(config)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var themeId = visual.TryGetValue(AuraToolsCardVisualRuntime.ReplayThemeIdKey, out var capturedTheme)
                      && IsSafeProfile(capturedTheme)
            ? capturedTheme
            : "default";
        var skinId = visual.TryGetValue(AuraToolsCardVisualRuntime.ReplaySkinIdKey, out var capturedSkin)
                     && IsSafeProfile(capturedSkin)
            ? capturedSkin
            : "";
        var themeDefinition = skinId.Length == 0 ? null : AuraToolsCardVisualRegistry.Theme(themeId);
        var skinDefinition = themeDefinition == null ? null : AuraToolsCardVisualRegistry.Skin(themeId, skinId);
        var descriptor = new ReplayCardDescriptorV17
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
            Tag = tag,
            ArtworkAssetSha256 = "",
            FrameAssetSha256 = "",
            ThemeProfile = themeId,
            SkinId = skinId,
            ResolvedSkinFrameResourcePath = themeDefinition == null || skinDefinition == null
                ? ""
                : AuraToolsCardVisualRegistry.ResolveThemeAsset(themeDefinition, skinDefinition.Frame),
            ResolvedSkinBackgroundResourcePath = themeDefinition == null || skinDefinition == null
                || string.IsNullOrWhiteSpace(skinDefinition.Background)
                    ? ""
                    : AuraToolsCardVisualRegistry.ResolveThemeAsset(themeDefinition, skinDefinition.Background),
            DynamicEffectId = visual.TryGetValue(AuraToolsCardVisualRuntime.ReplayEffectIdKey, out var effectId)
                ? effectId ?? ""
                : "",
            DynamicEffectParametersJson = visual.TryGetValue(
                AuraToolsCardVisualRuntime.ReplayEffectParametersKey,
                out var effectParameters)
                    ? effectParameters ?? ""
                    : "",
            AccentColor = ParseColor(First(Read(vars, "Color"), Read(data, "Color"))),
            NativeCardType = First(Read(vars, "BaseScript"), Read(data, "BaseScript"), "Common"),
            NativeResourcePath = "UI/CardItem",
            IconResourcePath = artworkResource,
            FrameResourcePath = frameResource,
            Rarity = rarity
        };
        cards.Add(descriptorId, descriptor);
        revision++;
        return descriptor;
    }

    internal void ObserveCardView(ReplayCardDescriptorV17 descriptor, CardItem? card)
    {
        if (descriptor == null || card == null) return;
        var name = card.transform.Find("Front/字体/nameTxt")?.GetComponent<TMP_Text>()?.text ?? "";
        var description = card.transform.Find("Front/字体/msgTxt")?.GetComponent<TMP_Text>()?.text ?? "";
        var changed = false;
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(descriptor.Name, name, StringComparison.Ordinal))
        {
            descriptor.Name = name;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(description)
            && !string.Equals(descriptor.Description, description, StringComparison.Ordinal))
        {
            descriptor.Description = description;
            changed = true;
        }
        if (changed) revision++;
    }

    internal ReplayBuffDescriptorV17 RegisterBuff(IDataConfig? config, string stableId)
    {
        var provenance = Provenance("Buff", stableId);
        var descriptorId = DescriptorId("buff", provenance);
        if (buffs.TryGetValue(descriptorId, out var existing)) return existing;
        var icon = First(Read(config?.Vars, "Icon"), Read(config?.data, "Icon"));
        var descriptor = new ReplayBuffDescriptorV17
        {
            DescriptorId = descriptorId,
            Provenance = provenance,
            Name = First(Read(config?.Vars, "Name"), Localize(config, "Name"), Read(config?.data, "Name"), stableId),
            Description = First(Read(config?.Vars, "Description"), Localize(config, "Description"), Read(config?.data, "Description")),
            IconAssetSha256 = "",
            IconResourcePath = icon,
            Type = First(Read(config?.Vars, "Type"), Read(config?.data, "Type")),
            SortOrder = BuffSortOrder(First(Read(config?.Vars, "Type"), Read(config?.data, "Type")))
        };
        buffs.Add(descriptorId, descriptor);
        revision++;
        return descriptor;
    }

    internal ReplayIntentDescriptorV17 RegisterIntent(IDataConfig? config, string stableId)
    {
        var provenance = Provenance("Intent", stableId);
        var descriptorId = DescriptorId("intent", provenance);
        if (intents.TryGetValue(descriptorId, out var existing)) return existing;
        var requestedIcon = Read(config?.data, "Icon");
        var requestedBackIcon = Read(config?.data, "BackIcon");
        var icon = ReplayIntentVisualCompatibilityApi.ResolveIcon(requestedIcon);
        var backIcon = ReplayIntentVisualCompatibilityApi.ResolveBackIcon(requestedBackIcon);
        if (icon.UsedFallback || backIcon.UsedFallback)
            AuraToolsLog.Debug("[MatchRecords] native intent visual fallback captured: intent=" + stableId
                               + ", icon=" + requestedIcon + "->" + icon.ResolvedPath
                               + ", background=" + requestedBackIcon + "->" + backIcon.ResolvedPath + ".");
        var descriptor = new ReplayIntentDescriptorV17
        {
            DescriptorId = descriptorId,
            Provenance = provenance,
            Name = First(Read(config?.Vars, "Name"), Localize(config, "Name"), Read(config?.data, "Name"), stableId),
            Description = First(Read(config?.Vars, "Description"), Localize(config, "Description"), Read(config?.data, "Description")),
            IconAssetSha256 = "",
            IconResourcePath = icon.ResolvedPath,
            BackIconResourcePath = backIcon.ResolvedPath
        };
        intents.Add(descriptorId, descriptor);
        revision++;
        return descriptor;
    }

    internal string RegisterEffect(string effectName)
    {
        var normalized = NormalizeId(effectName);
        if (normalized.Length == 0) return "";
        var descriptorId = "effect:" + normalized + ":"
                           + ReplayCanonicalJsonV17.Sha256Text(effectName.Trim()).Substring(0, 16);
        if (!effects.ContainsKey(descriptorId))
        {
            var nativeEffects = ReplayNativeEffectCompatibilityApi.Resolve(effectName);
            var descriptor = new ReplayEffectDescriptorV17
            {
                DescriptorId = descriptorId,
                ResourcePath = effectName,
                Primitive = "NativeResource",
                DurationTicks = nativeEffects.Select(item => item.DurationMicroseconds).DefaultIfEmpty(1L).Max(),
                Color = new ReplayColorQ8V17 { R = 255, G = 225, B = 150, A = 210 }
            };
            effects.Add(descriptorId, descriptor);
            revision++;
        }
        return descriptorId;
    }

    internal string RegisterAsset(ReplayAssetV17? asset)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.Sha256)) return "";
        if (!assets.ContainsKey(asset.Sha256)) revision++;
        assets[asset.Sha256] = asset;
        return asset.Sha256;
    }

    internal ReplayEntityPresentationBindingV17 Binding(
        ReplayEntityStateV17 state,
        string descriptorId,
        StatusManager? status)
    {
        var root = status?.transform;
        var bodyTransform = root?.Find("body");
        var headTransform = root?.Find("head");
        var bottomTransform = root?.Find("bottom");
        var centerTransform = root?.Find("center");
        var renderer = bodyTransform?.GetComponent<SpriteRenderer>();
        var color = renderer?.color ?? Color.white;
        var statusSize = new Vector2(280f, 78f);
        var statusRect = status?.statusBarObj?.GetComponent<RectTransform>();
        if (statusRect != null && statusRect.rect.width > 0f && statusRect.rect.height > 0f)
            statusSize = statusRect.rect.size;
        return new ReplayEntityPresentationBindingV17
        {
            AttachmentBounds = CaptureAttachmentBounds(root),
            EntityId = state.EntityId,
            SpawnGeneration = state.SpawnGeneration,
            DescriptorId = descriptorId,
            HasMeasuredLayout = root != null
                                && bodyTransform != null
                                && headTransform != null
                                && bottomTransform != null
                                && centerTransform != null
                                && renderer?.sprite != null,
            WorldPosition = Vector(root?.position ?? Vector3.zero),
            WorldEulerAngles = Vector(root?.eulerAngles ?? Vector3.zero),
            RootScale = Vector(root?.lossyScale ?? Vector3.one),
            BodyLocalPosition = Vector(bodyTransform?.localPosition ?? Vector3.zero),
            BodyLocalEulerAngles = Vector(bodyTransform?.localEulerAngles ?? Vector3.zero),
            BodyLocalScale = Vector(bodyTransform?.localScale ?? Vector3.one),
            HeadLocalPosition = Vector(headTransform?.localPosition ?? Vector3.zero),
            BottomLocalPosition = Vector(bottomTransform?.localPosition ?? Vector3.zero),
            CenterLocalPosition = Vector(centerTransform?.localPosition ?? Vector3.zero),
            StatusBarSize = Vector(statusSize),
            HudScaleQ16 = Quantize(ResolveCanvasRelativeScale(status?.statusBarObj?.transform)),
            SortingLayerName = renderer?.sortingLayerName ?? "Default",
            SortingOrder = renderer?.sortingOrder ?? 0,
            FlipX = renderer?.flipX ?? false,
            Color = new ReplayColorQ8V17
            {
                R = ToByte(color.r),
                G = ToByte(color.g),
                B = ToByte(color.b),
                A = ToByte(color.a)
            },
            CustomPresentation = CaptureCustomPresentation(state)
        };
    }

    private static ReplayCustomEntityPresentationV17? CaptureCustomPresentation(ReplayEntityStateV17 state)
    {
        ReplayCustomEntityPresentationV17? result = null;
        foreach (var provider in AuraReplayEntityPresentationRuntime.Snapshot())
        {
            var values = provider.Capture(new AuraReplayVisibleCaptureContext
            {
                PerspectivePlayerId = RoleTable.Instance?.Id ?? "",
                RoundSequence = 0,
                ActorTurnSequence = 0
            }) ?? Array.Empty<AuraReplayEntityPresentationItem>();
            foreach (var value in values.Where(item => item != null
                                                       && string.Equals(item.EntityId, state.EntityId, StringComparison.Ordinal)))
            {
                if (result != null)
                    throw new InvalidOperationException(
                        "Multiple replay entity-presentation providers claimed " + state.EntityId + ".");
                result = new ReplayCustomEntityPresentationV17
                {
                    OwnerModId = provider.OwnerModId,
                    SchemaVersion = provider.SchemaVersion,
                    PresentationMode = value.PresentationMode ?? "",
                    OwnerEntityId = value.OwnerEntityId ?? "",
                    ReferenceHeightPixels = value.ReferenceHeightPixels,
                    HorizontalOverlapQ16 = value.HorizontalOverlapQ16,
                    SortingOrderOffset = value.SortingOrderOffset,
                    HudMode = value.HudMode ?? "",
                    HudScaleQ16 = value.HudScaleQ16,
                    HudRotationQ16 = value.HudRotationQ16,
                    BadgeIconResourcePath = value.BadgeIconResourcePath ?? "",
                    BadgeText = value.BadgeText ?? "",
                    AttackFocusTravelPixels = value.AttackFocusTravelPixels,
                    InterferenceFocusTravelPixels = value.InterferenceFocusTravelPixels,
                    SupportFocusTravelPixels = value.SupportFocusTravelPixels
                };
            }
        }
        return result;
    }

    private void CaptureAnimationCatalog(
        StatusManager? status,
        IDataConfig? config,
        ReplayEntityDescriptorV17 descriptor)
    {
        var role = status?.fatherObject as IRole;
        var states = new Dictionary<string, (IStatusManager.AnimatedState? State, Sprite[] Frames)>(StringComparer.OrdinalIgnoreCase);
        if (role?.AnimatedStateSprites != null)
            foreach (var pair in role.AnimatedStateSprites)
                states[NormalizeAnimationState(pair.Key.ToString())] = (pair.Key, pair.Value ?? Array.Empty<Sprite>());
        if (!states.ContainsKey("Idle")) states["Idle"] = (IStatusManager.AnimatedState.Idle, Array.Empty<Sprite>());
        foreach (var pair in states.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var state = pair.Key;
            var animationLocation = role?.AnimationLocation ?? "";
            IRole.AnimationConfig? animationConfig = null;
            if (role != null && pair.Value.State.HasValue)
            {
                try { animationConfig = role.TryGetAnimationConfig(pair.Value.State.Value); }
                catch { animationConfig = null; }
            }
            var perFrame = animationConfig?.AnimationPerFrame > 0f ? animationConfig.AnimationPerFrame : 0.12f;
            var frameNames = pair.Value.Frames
                .Where(item => item != null)
                .Select(item => item.name ?? "")
                .ToList();
            var frameSequenceError = ReplayFrameSequenceContractV17.ValidateNames(frameNames, required: true);
            if (frameSequenceError.Length > 0)
                throw new InvalidOperationException(
                    "Replay native animation frame sequence is invalid: "
                    + descriptor.DescriptorId + "/" + state + ":" + frameSequenceError);
            descriptor.Animations.Add(new ReplayAnimationDescriptorV17
            {
                State = state,
                ResourcePath = !string.IsNullOrWhiteSpace(animationLocation)
                    ? animationLocation.TrimEnd('/') + "/" + state
                    : AnimationResource(config, state),
                FrameDurationTicks = Math.Max(1L, (long)Math.Round(perFrame * ReplayProtocolV17.TimebaseTicksPerSecond)),
                Loop = animationConfig?.isLoop
                       ?? (string.Equals(state, "Idle", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(state, "Wait", StringComparison.OrdinalIgnoreCase)),
                Direction = animationConfig?.Direction ?? "Left",
                Size = animationConfig?.Size ?? "Normal",
                YOffsetQ16 = Quantize(animationConfig?.YOffset ?? 0f),
                FightYOffsetQ16 = Quantize(animationConfig?.FightYOffset ?? 0f),
                FightXOffsetQ16 = Quantize(animationConfig?.FightXOffset ?? 0f),
                TargetScaleQ16 = Quantize(animationConfig?.TargetScale ?? 1f),
                SoundResourcePath = animationConfig?.SoundPath ?? "",
                FrameNames = frameNames
            });
        }
    }

    private static string AnimationResource(IDataConfig? config, string state)
    {
        var root = First(Read(config?.Vars, "Animation"), Read(config?.data, "Animation"));
        if (string.IsNullOrWhiteSpace(root)) return "";
        return root.TrimEnd('/') + "/" + (string.IsNullOrWhiteSpace(state) ? "Idle" : state);
    }

    private static ReplaySceneDescriptorV17 CreateSceneDescriptor() => new();

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

    private ReplayContentProvenanceV17 Provenance(string kind, string stableId)
    {
        var id = string.IsNullOrWhiteSpace(stableId) ? "unknown" : stableId.Trim();
        var key = kind + "|" + id;
        if (provenanceCache.TryGetValue(key, out var cached)) return cached;
        var owner = Owner(id);
        if (!ownerVersions.TryGetValue(owner, out var version))
            ownerVersions[owner] = version = ResolveOwnerVersion(owner);
        var provenance = new ReplayContentProvenanceV17
        {
            OwnerModId = owner,
            ContentKind = kind ?? "",
            StableContentId = id,
            SourceVersion = version
        };
        provenanceCache.Add(key, provenance);
        return provenance;
    }

    private static string DescriptorId(string prefix, ReplayContentProvenanceV17 value)
    {
        return prefix + ":" + value.OwnerModId + ":" + value.StableContentId;
    }

    private static string EntityContentKind(string archetype)
    {
        return archetype == ReplayEntityArchetypesV17.EnemyCombatant ? "Enemy"
            : archetype == ReplayEntityArchetypesV17.AlliedCombatant ? "Partner"
            : "Role";
    }

    internal static string Owner(string stableId)
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

    internal static string ResolveOwnerVersion(string owner)
    {
        if (string.Equals(owner, "Witch", StringComparison.OrdinalIgnoreCase))
            return (typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown")
                   + "+" + typeof(FightManager).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item =>
            string.Equals(item.GetName().Name, owner, StringComparison.OrdinalIgnoreCase)
            || (item.GetName().Name ?? "").StartsWith(owner + ".", StringComparison.OrdinalIgnoreCase));
        return assembly == null
            ? "unknown"
            : (assembly.GetName().Version?.ToString() ?? "unknown")
              + "+" + assembly.ManifestModule.ModuleVersionId.ToString("N");
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

    private static ReplayColorQ8V17 ParseColor(string value)
    {
        if (ColorUtility.TryParseHtmlString(value ?? "", out var color))
            return new ReplayColorQ8V17 { R = ToByte(color.r), G = ToByte(color.g), B = ToByte(color.b), A = ToByte(color.a) };
        return new ReplayColorQ8V17 { R = 210, G = 210, B = 220, A = 255 };
    }

    internal static ReplayBoundsQ16V17? CaptureAttachmentBounds(Transform? root)
    {
        var collider = root?.GetComponent<BoxCollider>();
        if (collider == null || !collider.enabled) return null;
        return new ReplayBoundsQ16V17 { Center = Vector(collider.center), Size = Vector(collider.size) };
    }

    private static ReplayVector3Q16V17 Vector(Vector3 value) => new()
    {
        X = Quantize(value.x),
        Y = Quantize(value.y),
        Z = Quantize(value.z)
    };

    private static ReplayVector2Q16V17 Vector(Vector2 value) => new()
    {
        X = Quantize(value.x),
        Y = Quantize(value.y)
    };

    private static float ResolveCanvasRelativeScale(Transform? value)
    {
        if (value == null) return 1f;
        var canvas = value.GetComponentInParent<Canvas>();
        var canvasScale = canvas == null ? 1f : Math.Abs(canvas.transform.lossyScale.x);
        if (canvasScale < 0.0001f) canvasScale = 1f;
        var scale = Math.Abs(value.lossyScale.x) / canvasScale;
        return scale is > 0.05f and < 8f ? scale : 1f;
    }

    private static int BuffSortOrder(string type) => (type ?? "").Trim() switch
    {
        "特性" => 0,
        "能力" => 1,
        "正面" => 2,
        "负面" => 3,
        "契印" => 4,
        _ => 100
    };

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

internal static class ReplayFactCaptureV17
{
    private static Material? nativeBurnTemplate;
    internal static ReplayVisibleStateV17 CaptureVisibleState(
        int roundSequence,
        int actorTurnSequence,
        ReplayCaptureCatalogV17 catalog,
        string recordId = "")
    {
        var manager = FightManager.Instance;
        var result = new ReplayVisibleStateV17
        {
            LevelId = manager?.level ?? "",
            PerspectivePlayerId = RoleTable.Instance?.Id ?? "single-player",
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
            item.Team == ReplayTeamsV17.Friendly && item.OwnerPlayerId == (RoleTable.Instance?.Id ?? ""))?.EntityId
            ?? result.Entities.FirstOrDefault(item => item.Team == ReplayTeamsV17.Friendly)?.EntityId
            ?? "";
        CaptureVisibleCards(result, catalog);
        result.Intents = ReplayIntentCaptureV17.CapturePlans(catalog);
        var localPlayer = FightPlayer.Instance;
        result.Resources.Add(new ReplayVisibleResourceStateV17
        {
            OwnerPlayerId = result.PerspectivePlayerId,
            ResourceId = "Power",
            Value = Math.Max(0, localPlayer?.CurPowerCount ?? 0),
            Maximum = Math.Max(0, localPlayer?.MaxPowerCount ?? 0),
            DisplayText = (localPlayer?.CurPowerCount ?? 0) + "/" + (localPlayer?.MaxPowerCount ?? 0)
        });
        CaptureVisibleSkills(result);
        CaptureVisibleExtensions(result, recordId);
        return ReplayStateReducerV17.Normalize(result);
    }

    private static void CaptureVisibleSkills(ReplayVisibleStateV17 target)
    {
        var career = RoleTable.Instance?.Career;
        if (career?.data == null) return;
        for (var index = 1; index <= 2; index++)
        {
            var skillId = ReplayCaptureCatalogV17.Read(career.data, "Skill" + index);
            var icon = ReplayCaptureCatalogV17.Read(career.data, "ActionImage" + index);
            if (string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(icon)) continue;
            var cooldown = RoleTable.Instance?.SkillTime?.TryGetValue(skillId, out var value) == true
                ? Math.Max(0, value)
                : 0;
            target.Resources.Add(new ReplayVisibleResourceStateV17
            {
                OwnerPlayerId = target.PerspectivePlayerId,
                ResourceId = "Skill" + index,
                Value = cooldown,
                Maximum = cooldown,
                DisplayText = cooldown.ToString(),
                Name = skillId,
                ResourcePath = icon
            });
        }
    }

    private static void CaptureVisibleExtensions(ReplayVisibleStateV17 target, string recordId)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in AuraReplayVisibleStateRuntime.Snapshot())
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var values = provider.Capture(new AuraReplayVisibleCaptureContext
                {
                    RecordId = recordId ?? "",
                    LevelId = target.LevelId,
                    PerspectivePlayerId = target.PerspectivePlayerId,
                    RoundSequence = target.RoundSequence,
                    ActorTurnSequence = target.ActorTurnSequence
                }) ?? Array.Empty<AuraReplayVisibleStateItem>();
                foreach (var item in values.Where(item => item != null).Take(128))
                {
                    var instanceId = (item.InstanceId ?? "").Trim();
                    var key = provider.OwnerModId + "|" + provider.TypeId + "|" + instanceId;
                    if (instanceId.Length == 0 || instanceId.Length > 256 || !identities.Add(key)) continue;
                    if (!ReplayCanonicalJsonV17.TryCanonicalizeJsonPayload(item.PayloadJson ?? "", out var canonical)
                        || canonical.Length > ReplayLimitsV17.MaximumTextLength)
                    {
                        AuraToolsLog.Warn("[MatchRecords] ignored invalid replay extension payload: " + key + ".");
                        continue;
                    }
                    var displayText = item.DisplayText ?? "";
                    target.Extensions.Add(new ReplayVisibleExtensionStateV17
                    {
                        OwnerModId = provider.OwnerModId,
                        TypeId = provider.TypeId,
                        InstanceId = instanceId,
                        SchemaVersion = provider.SchemaVersion,
                        DisplayText = displayText.Length <= 1024
                            ? displayText
                            : displayText.Substring(0, 1024),
                        PayloadJson = canonical
                    });
                }
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[MatchRecords] replay visible-state provider failed: "
                                  + provider.OwnerModId + "/" + provider.TypeId + ": " + ex.Message);
            }
            var elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            if (elapsed >= 2d)
                AuraToolsLog.Warn("[MatchRecords:perf] visible-state provider was slow: provider="
                                  + provider.OwnerModId + "/" + provider.TypeId
                                  + ", elapsedMs=" + elapsed.ToString("0.###") + ".");
        }
    }

    internal static ReplayCapturedActionSourceV17 CaptureActionSource(object? target, ReplayCaptureCatalogV17 catalog)
    {
        var config = target switch
        {
            CardItem card => card.dataConfig,
            SkillItem skill => skill.dataConfig,
            _ => null
        };
        var stableId = ReplayCaptureCatalogV17.Read(config?.data, "Id");
        var descriptor = catalog.RegisterCard(config, stableId);
        var actorId = target switch
        {
            CardItem card => card.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
            SkillItem skill => skill.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
            _ => FightPlayer.Instance?.Status?.InstanceId ?? ""
        };
        var sourceInstanceId = config?.InstanceID ?? ReplayCaptureCatalogV17.Read(config?.Vars, "InstanceID");
        var hand = FightUI.cardItemList ?? new List<CardItem>();
        var slot = hand.FindIndex(item => ReferenceEquals(item, target)
                                          || string.Equals(item?.dataConfig?.InstanceID, sourceInstanceId, StringComparison.Ordinal));
        var effect = ReplayCaptureCatalogV17.First(
            ReplayCaptureCatalogV17.Read(config?.Vars, "Effects"),
            ReplayCaptureCatalogV17.Read(config?.data, "Effects"));
        return new ReplayCapturedActionSourceV17
        {
            Kind = target is SkillItem ? ReplayTransactionKindsV17.Skill : ReplayTransactionKindsV17.Card,
            IssuerPlayerId = RoleTable.Instance?.Id ?? "",
            ActorId = actorId,
            SourceInstanceId = sourceInstanceId,
            DescriptorId = descriptor.DescriptorId,
            Label = descriptor.Name,
            AnimationState = ReplayCaptureCatalogV17.First(
                ReplayCaptureCatalogV17.Read(config?.Vars, "Action"),
                ReplayCaptureCatalogV17.Read(config?.data, "Action"),
                "Idle"),
            EffectDescriptorId = catalog.RegisterEffect(effect),
            SourceZone = target is SkillItem ? "Skill" : "Hand",
            SourceSlot = target is SkillItem ? -1 : slot
        };
    }

    internal static ReplayEntityPresentationBindingV17 CaptureBinding(
        ReplayEntityStateV17 state,
        ReplayCaptureCatalogV17 catalog)
    {
        var status = FightManager.Instance?.statuses?.Values.FirstOrDefault(item =>
            item != null && string.Equals(item.InstanceId, state.EntityId, StringComparison.Ordinal));
        var descriptor = CaptureEntityDescriptor(status, state, catalog);
        return catalog.Binding(state, descriptor.DescriptorId, status);
    }

    private static ReplayEntityStateV17 CaptureEntity(
        string entityId,
        StatusManager status,
        int fallbackSlot,
        IReadOnlyDictionary<string, int> enemySlots,
        ReplayCaptureCatalogV17 catalog)
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
            ? ReplayCaptureCatalogV17.Read(config?.data, "Id").Replace("*", "")
            : partner != null
                ? ReplayCaptureCatalogV17.Read(config?.data, "Id").Replace("*", "")
                : player != null
                    ? ReplayCaptureCatalogV17.First(
                        ReplayCaptureCatalogV17.Read(RoleTable.Instance?.Career?.data, "Id"),
                        RoleTable.Instance?.Id ?? "player")
                    : ReplayCaptureCatalogV17.First(
                        ReplayCaptureCatalogV17.Read(remoteRole?.data, "Id"),
                        remote?.Id ?? entityId);
        var archetype = enemy != null
            ? ReplayEntityArchetypesV17.EnemyCombatant
            : partner != null
                ? ReplayEntityArchetypesV17.AlliedCombatant
                : ReplayEntityArchetypesV17.PlayerCombatant;
        var contentConfig = player != null ? RoleTable.Instance?.Career : remoteRole ?? config;
        var presentationDescriptor = catalog.RegisterEntity(status, archetype, contentConfig, stableId);
        var result = new ReplayEntityStateV17
        {
            EntityId = entityId,
            DescriptorId = presentationDescriptor.DescriptorId,
            SpawnGeneration = 1,
            Team = enemy != null ? ReplayTeamsV17.Enemy : ReplayTeamsV17.Friendly,
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
            result.Buffs.Add(new ReplayBuffStateV17
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

    private static ReplayEntityDescriptorV17 CaptureEntityDescriptor(
        StatusManager? status,
        ReplayEntityStateV17 state,
        ReplayCaptureCatalogV17 catalog)
    {
        if (status == null)
            return catalog.RegisterEntity(
                null,
                state.Team == ReplayTeamsV17.Enemy
                    ? ReplayEntityArchetypesV17.EnemyCombatant
                    : ReplayEntityArchetypesV17.AlliedCombatant,
                null,
                state.EntityId);
        var enemy = status.fatherObject as Enemy;
        var partner = status.fatherObject as Partner;
        var player = status.fatherObject as FightPlayer;
        var config = enemy?.dataConfig ?? partner?.dataConfig ?? (player != null ? RoleTable.Instance?.Career : null);
        var stable = ReplayCaptureCatalogV17.First(
            ReplayCaptureCatalogV17.Read(config?.data, "Id"),
            state.EntityId);
        var archetype = enemy != null
            ? ReplayEntityArchetypesV17.EnemyCombatant
            : partner != null
                ? ReplayEntityArchetypesV17.AlliedCombatant
                : ReplayEntityArchetypesV17.PlayerCombatant;
        return catalog.RegisterEntity(status, archetype, config, stable);
    }

    private static void CaptureVisibleCards(ReplayVisibleStateV17 target, ReplayCaptureCatalogV17 catalog)
    {
        AddVisibleCards(target.Cards, "Discard", FightCardManager.Instance?.usedCardList, catalog);
        AddVisibleCards(target.Cards, "Nascent", FightCardManager.Instance?.nascentList, catalog);
        var handOrder = 0;
        foreach (var item in (FightUI.cardItemList ?? new List<CardItem>()).Where(item => item != null && item.dataConfig != null && !item.hasDone))
            target.Cards.Add(VisibleCard("Hand", handOrder++, item.dataConfig, catalog, item));
        target.ZoneCounts.Add(new ReplayVisibleZoneCountV17
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Draw",
            Count = FightCardManager.Instance?.cardList?.Count ?? 0
        });
        target.ZoneCounts.Add(new ReplayVisibleZoneCountV17
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Discard",
            Count = FightCardManager.Instance?.usedCardList?.Count ?? 0
        });
        target.ZoneCounts.Add(new ReplayVisibleZoneCountV17
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Hand",
            Count = handOrder
        });
        target.ZoneCounts.Add(new ReplayVisibleZoneCountV17
        {
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = "Nascent",
            Count = FightCardManager.Instance?.nascentList?.Count ?? 0
        });
    }

    private static void AddVisibleCards(
        ICollection<ReplayVisibleCardStateV17> target,
        string zone,
        IEnumerable<DataConfig>? source,
        ReplayCaptureCatalogV17 catalog)
    {
        var order = 0;
        foreach (var config in source ?? Enumerable.Empty<DataConfig>())
        {
            if (config == null) continue;
            var stableId = ReplayCaptureCatalogV17.Read(config.data, "Id");
            var descriptor = catalog.RegisterCard(config, stableId);
            target.Add(new ReplayVisibleCardStateV17
            {
                CardInstanceId = config.InstanceID ?? ReplayCaptureCatalogV17.Read(config.Vars, "InstanceID"),
                DescriptorId = descriptor.DescriptorId,
                OwnerPlayerId = RoleTable.Instance?.Id ?? "",
                Zone = zone,
                Order = order++,
                DisplayedCost = ParseInt(ReplayCaptureCatalogV17.First(
                    ReplayCaptureCatalogV17.Read(config.Vars, "Expend"),
                    ReplayCaptureCatalogV17.Read(config.data, "Expend"))),
                RenderedName = descriptor.Name,
                RenderedDescription = descriptor.Description,
                EnchantIconResourcePath = EnchantIcon(config)
            });
        }
    }

    internal static ReplayVisibleCardStateV17 CaptureCardView(CardItem card, ReplayCaptureCatalogV17 catalog) =>
        VisibleCard("Hand", Math.Max(0, FightUI.cardItemList?.IndexOf(card) ?? 0), card.dataConfig, catalog, card);

    private static ReplayVisibleCardStateV17 VisibleCard(
        string zone,
        int order,
        DataConfig config,
        ReplayCaptureCatalogV17 catalog,
        CardItem? cardItem = null)
    {
        var descriptor = catalog.RegisterCard(config, ReplayCaptureCatalogV17.Read(config.data, "Id"));
        catalog.ObserveCardView(descriptor, cardItem);
        var result = new ReplayVisibleCardStateV17
        {
            CardInstanceId = config.InstanceID ?? ReplayCaptureCatalogV17.Read(config.Vars, "InstanceID"),
            DescriptorId = descriptor.DescriptorId,
            OwnerPlayerId = RoleTable.Instance?.Id ?? "",
            Zone = zone,
            Order = order,
            DisplayedCost = DisplayedCost(cardItem, config),
            RenderedName = RenderedText(cardItem, "Front/字体/nameTxt", descriptor.Name),
            RenderedDescription = RenderedText(cardItem, "Front/字体/msgTxt", descriptor.Description),
            EnchantIconResourcePath = EnchantIcon(config)
        };
        CaptureCardLayout(result, cardItem, catalog.Scene.ReferenceWidth, catalog.Scene.ReferenceHeight);
        return result;
    }

    private static void CaptureCardLayout(
        ReplayVisibleCardStateV17 target,
        CardItem? cardItem,
        int referenceWidth,
        int referenceHeight)
    {
        var rect = cardItem?.GetComponent<RectTransform>();
        if (rect == null || Screen.width <= 0 || Screen.height <= 0 || rect.rect.width <= 0f || rect.rect.height <= 0f)
            return;
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera ?? Camera.main
            : null;
        var targetWorld = rect.position;
        if (cardItem != null && rect.parent != null)
        {
            var delta = cardItem.initPosition - rect.anchoredPosition;
            targetWorld += rect.parent.TransformVector(new Vector3(delta.x, delta.y, 0f));
        }
        var screen = RectTransformUtility.WorldToScreenPoint(camera, targetWorld);
        if (float.IsNaN(screen.x) || float.IsInfinity(screen.x)
                                  || float.IsNaN(screen.y) || float.IsInfinity(screen.y)) return;
        var canvasScale = canvas == null ? Vector3.one : canvas.transform.lossyScale;
        var relativeScale = new Vector3(
            SafeRatio(rect.lossyScale.x, canvasScale.x),
            SafeRatio(rect.lossyScale.y, canvasScale.y),
            SafeRatio(rect.lossyScale.z, canvasScale.z));
        if (cardItem != null && cardItem.initScale > 0f)
        {
            var currentScale = Math.Abs(rect.localScale.x) < 0.0001f ? 1f : Math.Abs(rect.localScale.x);
            var ratio = cardItem.initScale / currentScale;
            relativeScale = new Vector3(relativeScale.x * ratio, relativeScale.y * ratio, relativeScale.z);
        }
        target.HasMeasuredLayout = true;
        target.CanvasPosition = new ReplayVector2Q16V17
        {
            X = Quantize((screen.x / Screen.width - 0.5f) * Math.Max(1, referenceWidth)),
            Y = Quantize(screen.y / Screen.height * Math.Max(1, referenceHeight))
        };
        target.CanvasSize = new ReplayVector2Q16V17
        {
            X = Quantize(rect.rect.width),
            Y = Quantize(rect.rect.height)
        };
        var targetRotation = cardItem != null && rect.parent != null
            ? rect.parent.eulerAngles.z + cardItem.initAngle.z
            : rect.eulerAngles.z;
        target.RotationZQ16 = Quantize(Mathf.DeltaAngle(canvas?.transform.eulerAngles.z ?? 0f, targetRotation));
        target.LocalScale = new ReplayVector3Q16V17
        {
            X = Quantize(relativeScale.x),
            Y = Quantize(relativeScale.y),
            Z = Quantize(relativeScale.z)
        };
    }

    internal static ReplayTransformSampleV17? CaptureCardTransformSample(
        CardItem? cardItem,
        long offsetTicks,
        int referenceWidth,
        int referenceHeight)
    {
        var rect = cardItem?.GetComponent<RectTransform>();
        if (rect == null || Screen.width <= 0 || Screen.height <= 0
                         || rect.rect.width <= 0f || rect.rect.height <= 0f) return null;
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera ?? Camera.main
            : null;
        var screen = RectTransformUtility.WorldToScreenPoint(camera, rect.position);
        if (float.IsNaN(screen.x) || float.IsInfinity(screen.x)
                                  || float.IsNaN(screen.y) || float.IsInfinity(screen.y)) return null;
        var canvasScale = canvas == null ? Vector3.one : canvas.transform.lossyScale;
        var alpha = 1f;
        for (var current = rect; current != null; current = current.parent as RectTransform)
        {
            var group = current.GetComponent<CanvasGroup>();
            if (group != null) alpha *= group.alpha;
            if (canvas != null && current == canvas.transform) break;
        }
        var hasFade = TryCardMaterialFade(rect, out var materialFade);
        return new ReplayTransformSampleV17
        {
            OffsetTicks = Math.Max(0L, offsetTicks),
            CanvasPosition = new ReplayVector2Q16V17
            {
                X = Quantize((screen.x / Screen.width - 0.5f) * Math.Max(1, referenceWidth)),
                Y = Quantize(screen.y / Screen.height * Math.Max(1, referenceHeight))
            },
            CanvasSize = new ReplayVector2Q16V17
            {
                X = Quantize(rect.rect.width),
                Y = Quantize(rect.rect.height)
            },
            LocalScale = new ReplayVector3Q16V17
            {
                X = Quantize(SafeRatio(rect.lossyScale.x, canvasScale.x)),
                Y = Quantize(SafeRatio(rect.lossyScale.y, canvasScale.y)),
                Z = Quantize(SafeRatio(rect.lossyScale.z, canvasScale.z))
            },
            RotationZQ16 = Quantize(Mathf.DeltaAngle(canvas?.transform.eulerAngles.z ?? 0f, rect.eulerAngles.z)),
            AlphaQ16 = Quantize(Mathf.Clamp01(alpha)),
            HasMaterialFade = hasFade,
            MaterialFadeQ16 = hasFade ? Quantize(materialFade) : 0
        };
    }

    private static bool TryCardMaterialFade(Transform root, out float value)
    {
        if (nativeBurnTemplate == null)
            nativeBurnTemplate = AuraToolsResourceCache.Load<Material>("Material/CardBurn", false);
        foreach (var path in new[]
                 {
                     "Front/icon", "Back/background", "Front/background", "Front/FrontBack", "Front/Icons/Ench/Item"
                 })
        {
            var material = root.Find(path)?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (material == null || nativeBurnTemplate == null
                || material.shader != nativeBurnTemplate.shader || !material.HasProperty("_Fade")) continue;
            value = material.GetFloat("_Fade");
            return true;
        }
        value = 0f;
        return false;
    }

    internal static ReplayWorldTransformSampleV17? CaptureWorldTransformSample(
        StatusManager? status,
        long offsetTicks)
    {
        var root = status?.transform;
        var body = root?.Find("body");
        if (root == null || body == null) return null;
        var sorting = body.GetComponent<SortingGroup>();
        var renderer = body.GetComponent<SpriteRenderer>();
        return new ReplayWorldTransformSampleV17
        {
            AttachmentBounds = ReplayCaptureCatalogV17.CaptureAttachmentBounds(root),
            OffsetTicks = Math.Max(0L, offsetTicks),
            WorldPosition = new ReplayVector3Q16V17
            {
                X = Quantize(root.position.x),
                Y = Quantize(root.position.y),
                Z = Quantize(root.position.z)
            },
            RootScale = new ReplayVector3Q16V17
            {
                X = Quantize(root.lossyScale.x),
                Y = Quantize(root.lossyScale.y),
                Z = Quantize(root.lossyScale.z)
            },
            BodyLocalPosition = new ReplayVector3Q16V17
            {
                X = Quantize(body.localPosition.x),
                Y = Quantize(body.localPosition.y),
                Z = Quantize(body.localPosition.z)
            },
            BodyLocalScale = new ReplayVector3Q16V17
            {
                X = Quantize(body.localScale.x),
                Y = Quantize(body.localScale.y),
                Z = Quantize(body.localScale.z)
            },
            SortingLayerName = sorting?.sortingLayerName ?? renderer?.sortingLayerName ?? "Default",
            SortingOrder = sorting?.sortingOrder ?? renderer?.sortingOrder ?? 0
        };
    }

    private static float SafeRatio(float value, float denominator) => Math.Abs(denominator) < 0.0001f
        ? 1f
        : value / denominator;

    private static int Quantize(float value) => (int)Math.Round(value * 65_536d);

    private static int ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : 0;

    private static int DisplayedCost(CardItem? cardItem, IDataConfig config)
    {
        var rendered = cardItem?.transform.Find("Front/cost/cost")?.GetComponent<TMP_Text>()?.text ?? "";
        if (int.TryParse(rendered, NumberStyles.Integer, CultureInfo.InvariantCulture, out var visible))
            return Math.Max(0, visible);
        var baseCost = ParseInt(ReplayCaptureCatalogV17.First(
            ReplayCaptureCatalogV17.Read(config.Vars, "Expend"),
            ReplayCaptureCatalogV17.Read(config.data, "Expend")));
        var extra = ParseInt(ReplayCaptureCatalogV17.Read(config.Vars, "TotalExCost"))
                    + ParseInt(ReplayCaptureCatalogV17.Read(config.Vars, "ExCost"))
                    + ParseInt(ReplayCaptureCatalogV17.Read(config.Vars, "OnceExCost"));
        return Math.Max(0, baseCost + extra);
    }

    private static string RenderedText(CardItem? cardItem, string path, string fallback)
    {
        var value = cardItem?.transform.Find(path)?.GetComponent<TMP_Text>()?.text ?? "";
        return string.IsNullOrWhiteSpace(value) ? fallback ?? "" : value;
    }

    private static string EnchantIcon(IDataConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.InstanceID)) return "";
        return RoleTable.Instance?.enchasedDict?.TryGetValue(config.InstanceID, out var enchant) == true
            ? ReplayCaptureCatalogV17.First(
                ReplayCaptureCatalogV17.Read(enchant.Vars, "Icon"),
                ReplayCaptureCatalogV17.Read(enchant.data, "Icon"))
            : "";
    }
}

internal static class ReplayIntentCaptureV17
{
    private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SelectedCardsField = typeof(ObjectAction).GetField("CardList", InstanceFields);

    internal static List<ReplayIntentStateV17> CapturePlans(ReplayCaptureCatalogV17 catalog)
    {
        var result = new List<ReplayIntentStateV17>();
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
                var stableId = ReplayCaptureCatalogV17.First(
                    ReplayCaptureCatalogV17.Read(config.data, "Id"),
                    ReplayCaptureCatalogV17.Read(config.Vars, "Id"));
                var descriptor = catalog.RegisterIntent(config, stableId);
                result.Add(new ReplayIntentStateV17
                {
                    IntentInstanceId = config.InstanceID ?? enemy.Status.InstanceId + "|intent|" + slot,
                    ActorId = enemy.Status.InstanceId ?? enemy.InstanceId ?? "",
                    DescriptorId = descriptor.DescriptorId,
                    SlotIndex = slot,
                    DisplayValue = ReplayCaptureCatalogV17.First(
                        ReplayCaptureCatalogV17.Read(config.Vars, "DesVal1"),
                        ReplayCaptureCatalogV17.Read(config.data, "DesVal1")),
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
