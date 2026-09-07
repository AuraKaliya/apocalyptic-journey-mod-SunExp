using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal sealed class ReplayCombatantProjectionV17 : IDisposable
{
    private readonly GameObject root;
    private readonly SpriteRenderer body;
    private readonly Transform head;
    private readonly Transform bottom;
    private readonly Color aliveColor;
    private readonly Dictionary<string, AnimationState> animations;
    private readonly Vector3 basePosition;
    private readonly Vector3 baseScale;
    private readonly Vector3 baseBodyPosition;
    private readonly Vector3 baseBodyScale;
    private readonly Vector3 baseHeadPosition;
    private readonly Vector3 baseBottomPosition;
    private readonly int baseSortingOrder;
    private readonly Bounds baseAttachmentBounds;
    private Bounds attachmentBounds;
    private readonly float idleFightXOffset;
    private readonly float idleFightYOffset;
    private readonly string idleDirection = "Left";
    private AnimationState? active;
    private long animationStartedTicks;
    private long animationEndsTicks;
    private string activeState = "Idle";
    private float focusProgress;
    private IReadOnlyList<ReplayWorldTransformSampleV17> worldTrack = Array.Empty<ReplayWorldTransformSampleV17>();
    private long portableFocusStartedTicks;
    private long portableFocusEndsTicks;
    private int portableFocusTravelPixels;
    private float portableFocusPeakScale = 1f;
    private Vector2 portableFocusDirection = Vector2.up;
    private bool extensionVisible = true;

    private ReplayCombatantProjectionV17(
        GameObject root,
        SpriteRenderer body,
        Transform head,
        Transform bottom,
        Dictionary<string, AnimationState> animations,
        string entityId,
        int spawnGeneration,
        ReplayBoundsQ16V17? recordedBounds)
    {
        this.root = root;
        this.body = body;
        this.head = head;
        this.bottom = bottom;
        this.animations = animations;
        // Legacy documents retain their recorded idle geometry. An attacking
        // frame's weapon/cape extent must never become the attachment anchor.
        baseAttachmentBounds = recordedBounds == null
            ? ReplayBoundsProjectionV17.Transform(body.sprite.bounds, root.transform.worldToLocalMatrix * body.transform.localToWorldMatrix)
            : ReplayBoundsProjectionV17.FromRecorded(recordedBounds);
        attachmentBounds = baseAttachmentBounds;
        aliveColor = body.color;
        basePosition = root.transform.localPosition;
        baseScale = root.transform.localScale;
        baseBodyPosition = body.transform.localPosition;
        baseBodyScale = body.transform.localScale;
        baseHeadPosition = head.localPosition;
        baseBottomPosition = bottom.localPosition;
        baseSortingOrder = body.sortingOrder;
        if (animations.TryGetValue("Idle", out var idle))
        {
            idleFightXOffset = idle.FightXOffset;
            idleFightYOffset = idle.FightYOffset;
            idleDirection = idle.Direction;
        }
        EntityId = entityId;
        SpawnGeneration = spawnGeneration;
    }

    internal string EntityId { get; }
    internal int SpawnGeneration { get; }
    internal Transform RootTransform => root.transform;
    internal Vector3 Position => root.transform.localPosition;
    internal Vector3 HeadWorldPosition => head.position;
    internal Vector3 BottomWorldPosition => bottom.position;
    internal Bounds BodyWorldBounds => body.bounds;
    internal Bounds AttachmentWorldBounds => ReplayBoundsProjectionV17.Transform(attachmentBounds, root.transform.localToWorldMatrix);

    internal static ReplayCombatantProjectionV17 Create(
        Transform parent,
        ReplayEntityDescriptorV17 descriptor,
        ReplayEntityPresentationBindingV17 binding)
    {
        if (!binding.HasMeasuredLayout)
            throw new InvalidOperationException("Replay entity binding has no measured layout: " + binding.EntityId);
        var root = new GameObject("ReplayCombatant:" + binding.EntityId);
        root.transform.SetParent(parent, false);
        root.layer = 30;
        root.transform.localPosition = ReplayPresentationPrimitivesV17.Vector(binding.WorldPosition);
        root.transform.localEulerAngles = ReplayPresentationPrimitivesV17.Vector(binding.WorldEulerAngles);
        root.transform.localScale = ReplayPresentationPrimitivesV17.Vector(binding.RootScale);
        var bodyObject = new GameObject("Body", typeof(SpriteRenderer));
        bodyObject.transform.SetParent(root.transform, false);
        bodyObject.layer = 30;
        bodyObject.transform.localPosition = ReplayPresentationPrimitivesV17.Vector(binding.BodyLocalPosition);
        bodyObject.transform.localEulerAngles = ReplayPresentationPrimitivesV17.Vector(binding.BodyLocalEulerAngles);
        bodyObject.transform.localScale = ReplayPresentationPrimitivesV17.Vector(binding.BodyLocalScale);
        var body = bodyObject.GetComponent<SpriteRenderer>();
        body.sortingLayerName = binding.SortingLayerName;
        body.sortingOrder = binding.SortingOrder;
        body.flipX = binding.FlipX;
        body.color = ReplayPresentationPrimitivesV17.Color(binding.Color);
        var head = Marker(root.transform, "Head", binding.HeadLocalPosition);
        var bottom = Marker(root.transform, "Bottom", binding.BottomLocalPosition);
        _ = Marker(root.transform, "Center", binding.CenterLocalPosition);
        var animations = new Dictionary<string, AnimationState>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptorAnimation in descriptor.Animations)
        {
            var frames = ReplayResourceResolverV17.Sprites(
                descriptorAnimation.ResourcePath,
                descriptorAnimation.FrameNames);
            if (frames.Length == 0)
                throw new InvalidOperationException(
                    "Replay animation resource is missing: " + descriptor.DescriptorId + "/" + descriptorAnimation.State);
            animations[descriptorAnimation.State] = new AnimationState(
                frames,
                Math.Max(1L, descriptorAnimation.FrameDurationTicks),
                descriptorAnimation.Loop,
                descriptorAnimation.Direction,
                descriptorAnimation.Size,
                ReplayPresentationPrimitivesV17.FromQ16(descriptorAnimation.FightXOffsetQ16),
                ReplayPresentationPrimitivesV17.FromQ16(descriptorAnimation.FightYOffsetQ16));
        }
        if (!animations.TryGetValue("Idle", out var idle))
            throw new InvalidOperationException("Replay entity has no resolvable Idle animation: " + descriptor.DescriptorId);
        body.sprite = idle.Frames[0];
        var result = new ReplayCombatantProjectionV17(
            root, body, head, bottom, animations,
            binding.EntityId, binding.SpawnGeneration, binding.AttachmentBounds);
        result.PlayAnimation("Idle", 0L, 0L, Array.Empty<ReplayWorldTransformSampleV17>());
        return result;
    }

    internal void Apply(ReplayEntityStateV17 value)
    {
        body.enabled = value.IsPresent && extensionVisible;
        body.color = value.IsAlive
            ? aliveColor
            : new Color(aliveColor.r * 0.42f, aliveColor.g * 0.42f, aliveColor.b * 0.42f, aliveColor.a);
    }

    internal void PlayAnimation(
        string state,
        long startTicks,
        long durationTicks,
        IReadOnlyList<ReplayWorldTransformSampleV17>? samples)
    {
        var requested = string.IsNullOrWhiteSpace(state) ? "Idle" : state;
        if (!animations.TryGetValue(requested, out var animation))
            animation = animations.TryGetValue("Idle", out var idle) ? idle : animations.Values.FirstOrDefault();
        active = animation;
        activeState = requested;
        animationStartedTicks = startTicks;
        animationEndsTicks = durationTicks > 0 ? startTicks + durationTicks : 0L;
        worldTrack = (samples ?? Array.Empty<ReplayWorldTransformSampleV17>())
            .OrderBy(item => item.OffsetTicks)
            .ToList();
        attachmentBounds = baseAttachmentBounds;
        if (active != null && active.Frames.Length > 0) body.sprite = active.Frames[0];
        root.transform.localPosition = basePosition;
        root.transform.localScale = baseScale;
        ApplyAnimationLayout(active);
    }

    internal void Tick(long logicalTicks)
    {
        if (animationEndsTicks > 0 && logicalTicks >= animationEndsTicks)
            PlayAnimation("Idle", animationEndsTicks, 0L, Array.Empty<ReplayWorldTransformSampleV17>());
        root.transform.localPosition = basePosition;
        root.transform.localScale = baseScale;
        if (active == null || active.Frames.Length == 0) return;
        focusProgress = 0f;
        if (portableFocusEndsTicks > portableFocusStartedTicks
            && logicalTicks >= portableFocusStartedTicks
            && logicalTicks < portableFocusEndsTicks)
        {
            var portableProgress = Mathf.Clamp01((logicalTicks - portableFocusStartedTicks)
                                                 / (float)(portableFocusEndsTicks - portableFocusStartedTicks));
            focusProgress = portableProgress < 0.3f
                ? portableProgress / 0.3f
                : portableProgress < 0.55f
                    ? 1f
                    : 1f - (portableProgress - 0.55f) / 0.45f;
        }
        if (worldTrack.Count > 0) ApplyWorldTrack(Math.Max(0L, logicalTicks - animationStartedTicks));
        var frame = (int)(Math.Max(0L, logicalTicks - animationStartedTicks) / active.FrameDurationTicks);
        if (active.Loop) frame %= active.Frames.Length;
        else frame = Math.Min(active.Frames.Length - 1, frame);
        body.sprite = active.Frames[Math.Max(0, frame)];
    }

    private void ApplyWorldTrack(long offsetTicks)
    {
        var rightIndex = 0;
        while (rightIndex < worldTrack.Count && worldTrack[rightIndex].OffsetTicks < offsetTicks) rightIndex++;
        var right = worldTrack[Math.Min(worldTrack.Count - 1, rightIndex)];
        var left = worldTrack[Math.Max(0, rightIndex - 1)];
        var amount = right.OffsetTicks <= left.OffsetTicks
            ? 0f
            : Mathf.Clamp01((offsetTicks - left.OffsetTicks) / (float)(right.OffsetTicks - left.OffsetTicks));
        root.transform.localPosition = Vector3.LerpUnclamped(
            ReplayPresentationPrimitivesV17.Vector(left.WorldPosition),
            ReplayPresentationPrimitivesV17.Vector(right.WorldPosition),
            amount);
        root.transform.localScale = Vector3.LerpUnclamped(
            ReplayPresentationPrimitivesV17.Vector(left.RootScale),
            ReplayPresentationPrimitivesV17.Vector(right.RootScale),
            amount);
        body.transform.localPosition = Vector3.LerpUnclamped(
            ReplayPresentationPrimitivesV17.Vector(left.BodyLocalPosition),
            ReplayPresentationPrimitivesV17.Vector(right.BodyLocalPosition),
            amount);
        body.transform.localScale = Vector3.LerpUnclamped(
            ReplayPresentationPrimitivesV17.Vector(left.BodyLocalScale),
            ReplayPresentationPrimitivesV17.Vector(right.BodyLocalScale),
            amount);
        body.sortingLayerName = amount < 0.5f ? left.SortingLayerName : right.SortingLayerName;
        body.sortingOrder = Mathf.RoundToInt(Mathf.Lerp(left.SortingOrder, right.SortingOrder, amount));
        if (left.AttachmentBounds != null && right.AttachmentBounds != null)
        {
            var a = ReplayBoundsProjectionV17.FromRecorded(left.AttachmentBounds);
            var b = ReplayBoundsProjectionV17.FromRecorded(right.AttachmentBounds);
            attachmentBounds = new Bounds(Vector3.Lerp(a.center, b.center, amount), Vector3.Lerp(a.size, b.size, amount));
        }
    }

    internal void PlayPortableFocus(long startTicks, long durationTicks, int travelPixels, float peakScale, Vector2 direction)
    {
        portableFocusStartedTicks = startTicks;
        portableFocusEndsTicks = startTicks + Math.Max(1L, durationTicks);
        portableFocusTravelPixels = Math.Max(0, travelPixels);
        portableFocusPeakScale = Mathf.Clamp(peakScale, 1f, 2f);
        portableFocusDirection = direction;
    }

    internal void SetExtensionVisible(bool visible)
    {
        extensionVisible = visible;
        body.enabled = visible;
    }

    internal void ApplyCustomPresentation(
        ReplayCombatantProjectionV17 owner,
        Camera camera,
        int referenceHeight,
        ReplayCustomEntityPresentationV17 custom)
    {
        if (!string.Equals(custom.PresentationMode, "OwnerAttachedProxy", StringComparison.Ordinal)
            || body.sprite == null || camera == null) return;
        var ownerBounds = owner.AttachmentWorldBounds;
        var targetViewportHeight = Math.Max(1, custom.ReferenceHeightPixels) / ReplayCanvasSpaceV17.ReferencePixelHeight;
        var targetWorldHeight = ReplayCanvasSpaceV17.WorldHeight(
            camera, ownerBounds.center, Math.Max(1, custom.ReferenceHeightPixels));
        var sourceHeight = Math.Max(0.001f, attachmentBounds.size.y);
        var displayScale = Mathf.Clamp(targetWorldHeight / sourceHeight, 0.02f, 4f);
        var targetWorldWidth = attachmentBounds.size.x * displayScale;
        var ownerTopRight = camera.WorldToViewportPoint(new Vector3(ownerBounds.max.x, ownerBounds.max.y, ownerBounds.center.z));
        var ownerCenter = camera.WorldToViewportPoint(ownerBounds.center);
        var viewportWidth = Mathf.Abs(camera.WorldToViewportPoint(
            ownerBounds.center + camera.transform.right * targetWorldWidth).x - ownerCenter.x);
        var portableActive = portableFocusEndsTicks > portableFocusStartedTicks && focusProgress > 0f;
        var pulse = Mathf.Lerp(1f, portableActive ? portableFocusPeakScale : 1f, focusProgress);
        viewportWidth *= pulse;
        targetViewportHeight *= pulse;
        var overlap = ReplayPresentationPrimitivesV17.FromQ16(custom.HorizontalOverlapQ16);
        var desiredViewport = new Vector3(
            ownerTopRight.x - viewportWidth * overlap,
            ownerTopRight.y + targetViewportHeight * 0.5f,
            ownerCenter.z);
        var travelPixels = portableActive
            ? portableFocusTravelPixels
            : activeState is "Attack" or "Skill"
                ? custom.AttackFocusTravelPixels
                : custom.SupportFocusTravelPixels;
        var travel = travelPixels / ReplayCanvasSpaceV17.ReferencePixelHeight * focusProgress;
        desiredViewport.x += portableFocusDirection.x * travel / Mathf.Max(0.001f, camera.aspect);
        desiredViewport.y += portableFocusDirection.y * travel;
        var desiredCenter = camera.ViewportToWorldPoint(desiredViewport);
        var scaled = displayScale * pulse;
        root.transform.localScale = new Vector3(scaled, scaled, 1f);
        root.transform.position = desiredCenter - root.transform.TransformVector(attachmentBounds.center);
        body.sortingLayerID = owner.body.sortingLayerID;
        body.sortingOrder = owner.body.sortingOrder + custom.SortingOrderOffset;
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }

    private void ApplyAnimationLayout(AnimationState? value)
    {
        if (value == null) return;
        body.transform.localPosition = baseBodyPosition + Vector3.right * (value.FightXOffset - idleFightXOffset);
        var facing = Math.Sign(baseBodyScale.x);
        if (facing == 0) facing = 1;
        if (!string.Equals(value.Direction, idleDirection, StringComparison.Ordinal)) facing = -facing;
        body.transform.localScale = new Vector3(
            Math.Abs(baseBodyScale.x) * facing,
            baseBodyScale.y,
            baseBodyScale.z);
        head.localPosition = baseHeadPosition + Vector3.down * (value.FightYOffset - idleFightYOffset);
        bottom.localPosition = baseBottomPosition + Vector3.up * (value.FightYOffset - idleFightYOffset);
        body.sortingOrder = baseSortingOrder + (value.Size == "Small" ? 1 : value.Size == "Big" ? -1 : 0);
    }

    private static Transform Marker(Transform parent, string name, ReplayVector3Q16V17 position)
    {
        var marker = new GameObject(name).transform;
        marker.SetParent(parent, false);
        marker.localPosition = ReplayPresentationPrimitivesV17.Vector(position);
        marker.gameObject.layer = 30;
        return marker;
    }

    private sealed class AnimationState
    {
        internal AnimationState(
            Sprite[] frames,
            long frameDurationTicks,
            bool loop,
            string direction,
            string size,
            float fightXOffset,
            float fightYOffset)
        {
            Frames = frames;
            FrameDurationTicks = frameDurationTicks;
            Loop = loop;
            Direction = direction;
            Size = size;
            FightXOffset = fightXOffset;
            FightYOffset = fightYOffset;
        }

        internal Sprite[] Frames { get; }
        internal long FrameDurationTicks { get; }
        internal bool Loop { get; }
        internal string Direction { get; }
        internal string Size { get; }
        internal float FightXOffset { get; }
        internal float FightYOffset { get; }
    }
}
