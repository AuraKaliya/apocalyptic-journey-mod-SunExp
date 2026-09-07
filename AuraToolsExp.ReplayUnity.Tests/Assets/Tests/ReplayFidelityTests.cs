using System;
using System.Collections;
using System.Collections.Generic;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ReplayFidelityTests
{
    [Test]
    public void AZeroSizedCanvasCannotSilentlyCollapseRecordedCards()
    {
        var root = new GameObject("UninitializedCanvas", typeof(RectTransform));
        try
        {
            var canvas = (RectTransform)root.transform;
            canvas.sizeDelta = Vector2.zero;
            var target = NewRect(canvas, "Card");
            Assert.Throws<InvalidOperationException>(() => ReplayCanvasSpaceV17.Apply(
                target, canvas, new Vector2(1600, 900), Vector2.zero, Vector2.one, Vector3.one, 0));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void CanvasCoordinatesIgnoreNativeContainerOffsetsAndScale()
    {
        var root = new GameObject("Canvas", typeof(RectTransform));
        try
        {
            var canvas = (RectTransform)root.transform;
            canvas.sizeDelta = new Vector2(1920, 1080);
            canvas.position = new Vector3(20, 30, 4);
            canvas.localScale = Vector3.one * 2f;
            canvas.localRotation = Quaternion.Euler(0, 0, 15);
            var parent = NewRect(canvas, "NativeHand");
            parent.anchoredPosition = new Vector2(320, -430);
            parent.localScale = Vector3.one * 0.6f;
            var target = NewRect(parent, "Card");
            ReplayCanvasSpaceV17.Apply(target, canvas, new Vector2(1600, 900),
                new Vector2(100, 0), new Vector2(386, 690), Vector3.one * 0.75f, 20);
            var expected = canvas.TransformPoint(new Vector3(120, -540, 0));
            Assert.That(Vector3.Distance(target.position, expected), Is.LessThan(0.002f));
            Assert.That(target.lossyScale.x / canvas.lossyScale.x, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(Mathf.DeltaAngle(canvas.eulerAngles.z, target.eulerAngles.z), Is.EqualTo(20).Within(0.001f));
            Assert.That(target.sizeDelta, Is.EqualTo(new Vector2(386, 690)));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(false, 40f, 4f)]
    [TestCase(false, 95f, 11f)]
    [TestCase(true, 40f, 4f)]
    public void OwnerAttachedPixelHeightIsIndependentOfProjectionAndDepth(bool orthographic, float fov, float depth)
    {
        var root = new GameObject("Camera", typeof(Camera));
        try
        {
            var camera = root.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = orthographic;
            camera.fieldOfView = fov;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0, 0, -depth);
            var height = ReplayCanvasSpaceV17.WorldHeight(camera, Vector3.zero, 120);
            var viewportHeight = camera.WorldToViewportPoint(Vector3.up * height).y
                                 - camera.WorldToViewportPoint(Vector3.zero).y;
            Assert.That(viewportHeight, Is.EqualTo(120f / 1080f).Within(0.00001f));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [UnityTest]
    public IEnumerator ConcurrentCardsKeepIndependentLifetimesCostsAndTracks()
    {
        var root = new GameObject("Canvas", typeof(RectTransform));
        var template = new GameObject("NativeCardTemplate");
        var canvas = (RectTransform)root.transform;
        canvas.sizeDelta = new Vector2(1920, 1080);
        var parent = NewRect(canvas, "OffsetCentreContainer");
        parent.anchoredPosition = new Vector2(200, 170);
        var presenter = new ReplayCardInstructionProjectionV17(parent, canvas, new Vector2(1600, 900),
            new Dictionary<string, ReplayCardDescriptorV17> { ["card"] = new ReplayCardDescriptorV17 { DescriptorId = "card" } },
            new ReplayUiTemplateCacheV17 { CardTemplate = template });
        try
        {
            presenter.Show(Message("first", 3, 1_000_000, 100), 0);
            presenter.Show(Message("second", 7, 2_000_000, -100), 100_000);
            presenter.Tick(500_000);
            Assert.That(parent.childCount, Is.EqualTo(2));
            Assert.That(parent.Find("first").GetComponent<CardBindingObservation>().Cost, Is.EqualTo(3));
            Assert.That(parent.Find("second").GetComponent<CardBindingObservation>().Cost, Is.EqualTo(7));
            Assert.That(canvas.InverseTransformPoint(parent.Find("first").position).x, Is.EqualTo(120).Within(0.001f));
            Assert.That(canvas.InverseTransformPoint(parent.Find("second").position).x, Is.EqualTo(-120).Within(0.001f));
            presenter.Tick(1_100_000);
            yield return null;
            Assert.That(parent.childCount, Is.EqualTo(1));
            Assert.That(parent.Find("second"), Is.Not.Null);
            presenter.Clear();
            yield return null;
            Assert.That(parent.childCount, Is.Zero);
            var invalid = Message("invalid", 1, 100, 0);
            invalid.TransformSamples.Clear();
            Assert.Throws<InvalidOperationException>(() => presenter.Show(invalid, 0));
            Assert.That(parent.childCount, Is.Zero);
        }
        finally
        {
            presenter.Dispose();
            Object.Destroy(root);
            Object.Destroy(template);
        }
    }

    [Test]
    public void RecordedUnderlyingPoseDoesNotCancelOwnerAttachedFocus()
    {
        var root = new GameObject("ReplayWorld");
        var cameraRoot = new GameObject("ReplayCamera", typeof(Camera));
        var texture = new Texture2D(8, 16);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 16), new Vector2(0.5f, 0.5f), 16);
        ReplayResourceResolverV17.Frames = new[] { sprite };
        ReplayCombatantProjectionV17 owner = null;
        ReplayCombatantProjectionV17 proxy = null;
        try
        {
            var camera = cameraRoot.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = false;
            camera.fieldOfView = 95;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0, 0, -5);
            var descriptor = new ReplayEntityDescriptorV17
            {
                DescriptorId = "fixture",
                Animations = new List<ReplayAnimationDescriptorV17>
                {
                    new() { State = "Idle", ResourcePath = "fixture", FrameNames = new List<string> { "frame" } },
                    new() { State = "Attack", ResourcePath = "fixture", FrameNames = new List<string> { "frame" } }
                }
            };
            owner = ReplayCombatantProjectionV17.Create(root.transform, descriptor,
                new ReplayEntityPresentationBindingV17 { EntityId = "owner", HasMeasuredLayout = true });
            proxy = ReplayCombatantProjectionV17.Create(root.transform, descriptor,
                new ReplayEntityPresentationBindingV17 { EntityId = "proxy", HasMeasuredLayout = true });
            proxy.PlayPortableFocus(0, 400_000, 70, 1.12f, Vector2.down);
            proxy.PlayAnimation("Attack", 0, 400_000,
                new[] { new ReplayWorldTransformSampleV17() });
            proxy.Tick(150_000);
            proxy.ApplyCustomPresentation(owner, camera, 900, new ReplayCustomEntityPresentationV17
            {
                PresentationMode = "OwnerAttachedProxy", ReferenceHeightPixels = 120
            });
            var bounds = proxy.BodyWorldBounds;
            var measuredHeight = camera.WorldToViewportPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)).y
                                 - camera.WorldToViewportPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
            Assert.That(measuredHeight, Is.EqualTo(120f / 1080f * 1.12f).Within(0.0001f));
            var ownerBounds = owner.BodyWorldBounds;
            var ownerTop = camera.WorldToViewportPoint(
                new Vector3(ownerBounds.max.x, ownerBounds.max.y, ownerBounds.center.z)).y;
            var proxyCentre = camera.WorldToViewportPoint(bounds.center).y;
            Assert.That(proxyCentre, Is.EqualTo(ownerTop + measuredHeight * 0.5f - 70f / 1080f).Within(0.0001f));
        }
        finally
        {
            owner?.Dispose();
            proxy?.Dispose();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(cameraRoot);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
            ReplayResourceResolverV17.Frames = Array.Empty<Sprite>();
        }
    }

    private static ReplayPresentationMessageV17 Message(string id, int cost, long duration, int x) => new()
    {
        SourceInstanceId = id, DescriptorId = "card", Value = cost, Kind = "Discard", DurationTicks = duration,
        TransformSamples = new List<ReplayTransformSampleV17>
        {
            new() { CanvasPosition = new ReplayVector2Q16V17 { X = x * 65536, Y = 450 * 65536 },
                CanvasSize = new ReplayVector2Q16V17 { X = 386 * 65536, Y = 690 * 65536 },
                LocalScale = ReplayVector3Q16V17.One(), AlphaQ16 = 65536 }
        }
    };

    [TestCase(false)]
    [TestCase(true)]
    public void AttachmentIgnoresChangingAttackArtworkButFollowsOwnerMovement(bool measured)
    {
        var world = new GameObject("AttachmentWorld");
        var cameraObject = new GameObject("AttachmentCamera", typeof(Camera));
        var texture = new Texture2D(64, 64);
        var narrow = Sprite.Create(texture, new Rect(0, 0, 8, 16), Vector2.one * 0.5f, 16);
        var wide = Sprite.Create(texture, new Rect(0, 0, 64, 64), Vector2.one * 0.5f, 16);
        ReplayResourceResolverV17.Frames = new[] { narrow, wide };
        ReplayCombatantProjectionV17 owner = null;
        ReplayCombatantProjectionV17 proxy = null;
        try
        {
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = Vector3.back * 5;
            var descriptor = new ReplayEntityDescriptorV17
            {
                DescriptorId = "actor", Animations = new List<ReplayAnimationDescriptorV17>
                {
                    new() { State = "Idle", ResourcePath = "frames", FrameDurationTicks = 100_000 },
                    new() { State = "Attack", ResourcePath = "frames", FrameDurationTicks = 100_000 }
                }
            };
            var bounds = measured ? new ReplayBoundsQ16V17
                { Size = new ReplayVector3Q16V17 { X = 65536, Y = 2 * 65536, Z = 65536 } } : null;
            owner = ReplayCombatantProjectionV17.Create(world.transform, descriptor,
                new ReplayEntityPresentationBindingV17 { EntityId = "owner", HasMeasuredLayout = true, AttachmentBounds = bounds });
            proxy = ReplayCombatantProjectionV17.Create(world.transform, descriptor,
                new ReplayEntityPresentationBindingV17 { EntityId = "spirit", HasMeasuredLayout = true });
            var custom = new ReplayCustomEntityPresentationV17
                { PresentationMode = "OwnerAttachedProxy", ReferenceHeightPixels = 120, HorizontalOverlapQ16 = 21845 };
            owner.PlayAnimation("Attack", 0, 1_000_000, Array.Empty<ReplayWorldTransformSampleV17>());
            owner.Tick(0);
            proxy.ApplyCustomPresentation(owner, camera, 1080, custom);
            var initial = proxy.AttachmentWorldBounds;
            var oldArtwork = owner.BodyWorldBounds;
            owner.Tick(150_000);
            proxy.ApplyCustomPresentation(owner, camera, 1080, custom);
            Assert.That(owner.BodyWorldBounds.size.x, Is.GreaterThan(oldArtwork.size.x * 3));
            Assert.That(Vector3.Distance(initial.center, proxy.AttachmentWorldBounds.center), Is.LessThan(0.0001f),
                "A cape/weapon frame cannot move the spirit or its HUD anchor.");
            owner.PlayAnimation("Attack", 0, 1_000_000, new[]
            {
                new ReplayWorldTransformSampleV17 { WorldPosition = new ReplayVector3Q16V17 { X = 2 * 65536 } }
            });
            owner.Tick(150_000);
            proxy.ApplyCustomPresentation(owner, camera, 1080, custom);
            Assert.That(proxy.AttachmentWorldBounds.center.x - initial.center.x, Is.EqualTo(2).Within(0.0001f));
        }
        finally
        {
            owner?.Dispose(); proxy?.Dispose();
            Object.DestroyImmediate(world); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(narrow); Object.DestroyImmediate(wide); Object.DestroyImmediate(texture);
            ReplayResourceResolverV17.Frames = Array.Empty<Sprite>();
        }
    }

    [Test]
    public void OneLogicalCardCanHaveTwoViewsAndBurnStartsAtItsRecordedPhase()
    {
        var root = new GameObject("Canvas", typeof(RectTransform));
        var template = new GameObject("CardTemplate");
        var canvas = (RectTransform)root.transform;
        canvas.sizeDelta = new Vector2(1600, 900);
        var parent = NewRect(canvas, "CardMotionLayer");
        var projection = new ReplayCardInstructionProjectionV17(parent, canvas, canvas.sizeDelta,
            new Dictionary<string, ReplayCardDescriptorV17> { ["card"] = new() { DescriptorId = "card" } },
            new ReplayUiTemplateCacheV17 { CardTemplate = template });
        try
        {
            var message = Message("same-card", 1, 2_000_000, 0);
            message.VisualInstanceId = "hand-view";
            var held = Message("same-card", 1, 1, 0).TransformSamples[0];
            held.OffsetTicks = 500_000;
            var burned = Message("same-card", 1, 1, 200).TransformSamples[0];
            burned.OffsetTicks = 1_000_000;
            burned.HasMaterialFade = true;
            burned.MaterialFadeQ16 = 50 * 65536;
            message.TransformSamples.Add(held);
            message.TransformSamples.Add(burned);
            projection.Show(message, 0);
            projection.Tick(400_000);
            var observation = parent.GetComponentInChildren<CardBindingObservation>();
            Assert.That(canvas.InverseTransformPoint(observation.transform.position).x, Is.EqualTo(0).Within(0.001f));
            Assert.That(observation.BurnPrepared, Is.False, "A later burn cannot replace the card's material while it is being dragged.");
            projection.Tick(750_000);
            Assert.That(canvas.InverseTransformPoint(observation.transform.position).x, Is.EqualTo(100).Within(0.001f));
            Assert.That(observation.BurnPrepared, Is.False);
            projection.Tick(1_000_000);
            Assert.That(observation.BurnPrepared, Is.True);
            var centre = Message("same-card", 1, 2_000_000, -100);
            centre.VisualInstanceId = "centre-view";
            projection.Show(centre, 1_000_000);
            Assert.That(parent.childCount, Is.EqualTo(2), "Native centre presentation does not destroy the same card's hand/exit view.");
        }
        finally
        {
            projection.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(template);
        }
    }

    private static RectTransform NewRect(Transform parent, string name)
    {
        var value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return (RectTransform)value.transform;
    }

    [Test]
    public void ArrivalAndReflowAnimateTogetherThenReturnToExactStaticHandSlots()
    {
        var root = new GameObject("Canvas", typeof(RectTransform));
        var template = new GameObject("CardTemplate");
        var canvas = (RectTransform)root.transform;
        canvas.sizeDelta = new Vector2(1600, 900);
        var staticRoot = NewRect(canvas, "StaticHand");
        var movingRoot = NewRect(canvas, "MovingHand");
        var descriptors = new Dictionary<string, ReplayCardDescriptorV17> { ["card"] = new() { DescriptorId = "card" } };
        var templates = new ReplayUiTemplateCacheV17 { CardTemplate = template };
        var hand = new ReplayHandProjectionV17(staticRoot, canvas, canvas.sizeDelta, descriptors, templates, true);
        var motion = new ReplayCardInstructionProjectionV17(movingRoot, canvas, canvas.sizeDelta, descriptors, templates);
        try
        {
            ReplayVisibleCardStateV17 State(string id, int x, string name) => new()
            {
                CardInstanceId = id, DescriptorId = "card", Zone = "Hand", RenderedName = name, DisplayedCost = 3,
                HasMeasuredLayout = true, IsRevealed = true,
                CanvasPosition = new ReplayVector2Q16V17 { X = x * 65536, Y = 450 * 65536 },
                CanvasSize = new ReplayVector2Q16V17 { X = 386 * 65536, Y = 690 * 65536 }, LocalScale = ReplayVector3Q16V17.One()
            };
            var oldCard = State("old", -200, "Existing card");
            var incoming = State("incoming", 200, "Dynamic Spirit card");
            hand.Apply("", new[] { oldCard, incoming });
            var reflow = Message("old", 3, 1_000_000, -100);
            reflow.Kind = "HandLayout"; reflow.VisualInstanceId = "old-view"; reflow.CardView = oldCard;
            var entry = Message("incoming", 3, 1_000_000, -1500);
            entry.Kind = "Draw"; entry.VisualInstanceId = "new-view"; entry.CardView = incoming;
            var endOld = Message("old", 3, 1, -200).TransformSamples[0]; endOld.OffsetTicks = 1_000_000;
            var endNew = Message("incoming", 3, 1, 200).TransformSamples[0]; endNew.OffsetTicks = 1_000_000;
            reflow.TransformSamples.Add(endOld); entry.TransformSamples.Add(endNew);
            motion.Show(reflow, 0); motion.Show(entry, 0); motion.Tick(500_000);
            hand.SetMovingSources(motion.ActiveSourceIds);
            Assert.That(staticRoot.GetComponentsInChildren<CardBindingObservation>().Length, Is.Zero);
            Assert.That(canvas.InverseTransformPoint(movingRoot.Find("old").position).x, Is.EqualTo(-150).Within(0.001f));
            Assert.That(canvas.InverseTransformPoint(movingRoot.Find("incoming").position).x, Is.EqualTo(-650).Within(0.001f));
            Assert.That(movingRoot.Find("incoming").GetComponent<CardBindingObservation>().RenderedName, Is.EqualTo("Dynamic Spirit card"));
            motion.Tick(1_000_000); hand.SetMovingSources(motion.ActiveSourceIds);
            Assert.That(staticRoot.GetComponentsInChildren<CardBindingObservation>().Length, Is.EqualTo(2));
            Assert.That(canvas.InverseTransformPoint(staticRoot.Find("old").position).x, Is.EqualTo(-200).Within(0.001f));
            Assert.That(canvas.InverseTransformPoint(staticRoot.Find("incoming").position).x, Is.EqualTo(200).Within(0.001f));
            Assert.That(movingRoot.GetComponentsInChildren<CardBindingObservation>().Length, Is.Zero);
        }
        finally
        {
            hand.Dispose(); motion.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(template);
        }
    }
}
