using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class FieldPresentationTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root = new("FieldAcceptanceRoot");
        public readonly Camera Camera;
        public readonly GameObject Background;
        public readonly RectTransform FightUi;
        public readonly RectTransform Hand;
        public readonly RectTransform ProbeButton;
        public readonly Transform Ground;
        public readonly Material NativeMaterial;
        public readonly RenderTexture Target = new(1280, 720, 24);
        private readonly Texture2D pixels = new(1280, 720, TextureFormat.RGBA32, false);
        private readonly RenderPipelineAsset previous = GraphicsSettings.defaultRenderPipeline;
        private readonly RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
        private readonly Renderer2DData data = ScriptableObject.CreateInstance<Renderer2DData>();
        private readonly UniversalRenderPipelineAsset pipeline;
        private readonly List<Object> owned = new();
        private readonly List<FieldVisualMesh> nativeMeshes = new();
        private readonly GraphicRaycaster raycaster;
        private readonly EventSystem events;

        public Fixture()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            pipeline = UniversalRenderPipelineAsset.Create(data);
            pipeline.msaaSampleCount = 1;
            GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = pipeline;
            Camera = new GameObject("FieldAcceptanceCamera", typeof(Camera)).GetComponent<Camera>();
            Camera.transform.SetParent(Root.transform);
            Camera.tag = "MainCamera";
            Camera.transform.position = new Vector3(0f, 0f, -5f);
            Camera.fieldOfView = 95f;
            Camera.aspect = 16f / 9f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.025f, 0.04f, 0.07f);
            Camera.targetTexture = Target;
            Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            Background = new GameObject("NativeForest");
            Background.transform.SetParent(Root.transform);
            Ground = new GameObject("groundPos").transform;
            Ground.SetParent(Background.transform);
            Ground.position = new Vector3(0f, -1.3f, 0f);
            NativeMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = Texture2D.whiteTexture };
            LoadForest();

            var canvasRoot = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(Root.transform);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera;
            canvas.planeDistance = 100f;
            canvas.sortingOrder = 25;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            FightUi = Rect("FightUI", canvasRoot.transform, Vector2.zero, Vector2.zero);
            FightUi.anchorMin = Vector2.zero;
            FightUi.anchorMax = Vector2.one;
            FightUi.gameObject.AddComponent<Canvas>().overrideSorting = false;
            raycaster = FightUi.gameObject.AddComponent<GraphicRaycaster>();
            Hand = Rect("container", FightUi, new Vector2(0f, -375f), new Vector2(1150f, 240f));
            Hand.gameObject.AddComponent<SortingGroup>().sortingOrder = 11;
            for (var i = 0; i < 5; i++)
            {
                var card = new FieldVisualMesh("NativeCard" + i, Hand, NativeMaterial, "Default", i);
                card.Quad(new Rect(-440f + 180f * i, -120f, 160f, 225f), new Color(0.2f, 0.16f, 0.3f), new Color(0.25f, 0.2f, 0.34f));
                card.Quad(new Rect(-424f + 180f * i, 72f, 128f, 10f), Color.white, Color.white);
                card.Commit();
                nativeMeshes.Add(card);
            }
            var left = Rect("Left", FightUi, new Vector2(-780f, -370f), new Vector2(230f, 240f));
            var clock = Rect("ClockBoard", FightUi, new Vector2(780f, -370f), new Vector2(220f, 220f));
            ProbeButton = Rect("EndTurnButton", clock, Vector2.zero, new Vector2(150f, 150f));
            ProbeButton.gameObject.AddComponent<Image>().color = new Color(0.75f, 0.64f, 0.28f);
            ProbeButton.gameObject.AddComponent<Button>();
            var eventRoot = new GameObject("EventSystem", typeof(EventSystem));
            eventRoot.transform.SetParent(Root.transform);
            events = eventRoot.GetComponent<EventSystem>();
            Root.AddComponent<FieldFramePump>();
            FieldFixtureRuntime.Scene = new FieldPresentationScene(FightUi, Background, Camera, Ground, Hand, left, clock);
            FieldFixtureRuntime.Options = new();
            FieldFixtureRuntime.Specs = Terrias.Dll.Mechanics.FieldVisualSpec.Defaults();
            FieldFixtureRuntime.Enabled = true;
            FieldFixtureRuntime.MissingTextures = false;
            FieldPresentationRuntime.Initialize();
            FieldApi.Set(TerriasFieldId.None, 0);
            TerriasBattleLifecycleRouter.Subscription!.BattleInitializing!(null!);
        }

        private void LoadForest()
        {
            var folder = Path.Combine(Application.dataPath, "Fixtures");
            var path = Path.Combine(folder, "forest.json");
            if (File.Exists(path))
            {
                foreach (var item in JObject.Parse(File.ReadAllText(path))["sprites"]!)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    texture.LoadImage(File.ReadAllBytes(Path.Combine(folder, (string)item["file"]!)));
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2((float)item["pivotX"]!, (float)item["pivotY"]!), (float)item["pixelsPerUnit"]!);
                    var go = new GameObject((string)item["name"]!, typeof(SpriteRenderer));
                    go.transform.SetParent(Background.transform);
                    go.transform.localPosition = new Vector3((float)item["x"]!, (float)item["y"]!, 0f);
                    var renderer = go.GetComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    renderer.sharedMaterial = NativeMaterial;
                    renderer.sortingLayerID = (int)item["sortingLayerId"]!;
                    renderer.sortingOrder = (int)item["sortingOrder"]!;
                    owned.Add(texture);
                    owned.Add(sprite);
                }
            }
            else
            {
                var floor = new FieldVisualMesh("NativeFloor", Background.transform, NativeMaterial, "middleground", 0, true);
                floor.Quad(new Rect(-11f, -4f, 22f, 2.7f), new Color(0.07f, 0.13f, 0.17f), new Color(0.18f, 0.32f, 0.38f));
                floor.Commit();
                nativeMeshes.Add(floor);
            }
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        public Color32[] Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            Camera.enabled = false;
            Camera.Render();
            var previousTarget = RenderTexture.active;
            try
            {
                RenderTexture.active = Target;
                pixels.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0);
                pixels.Apply();
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/field-presentation-unity"));
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previousTarget; Camera.enabled = true; }
        }

        public void AssertButtonUsable()
        {
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(Camera, ProbeButton.position) };
            raycaster.Raycast(pointer, hits);
            Assert.That(hits.Any(hit => hit.gameObject == ProbeButton.gameObject), Is.True, "Field effects cannot intercept end-turn input.");
        }

        public int CardPixelIndex()
        {
            var point = Camera.WorldToViewportPoint(Hand.TransformPoint(new Vector3(0f, -20f, 0f)));
            return Mathf.Clamp((int)(point.y * Target.height), 0, Target.height - 1) * Target.width
                + Mathf.Clamp((int)(point.x * Target.width), 0, Target.width - 1);
        }

        public void Resize(int width, int height)
        {
            Camera.targetTexture = null;
            Target.Release();
            Target.width = width;
            Target.height = height;
            Target.Create();
            pixels.Reinitialize(width, height);
            Camera.targetTexture = Target;
            Camera.aspect = (float)width / height;
        }

        public void Dispose()
        {
            TerriasBattleLifecycleRouter.Subscription!.OutcomeEntering!(null!);
            FieldFixtureRuntime.Scene = null;
            foreach (var mesh in nativeMeshes) mesh.Dispose();
            Camera.targetTexture = null;
            Object.DestroyImmediate(Root);
            GraphicsSettings.defaultRenderPipeline = previous;
            QualitySettings.renderPipeline = previousQuality;
            foreach (var item in owned) Object.DestroyImmediate(item);
            foreach (var texture in FieldFixtureRuntime.Textures.Values) Object.DestroyImmediate(texture);
            FieldFixtureRuntime.Textures.Clear();
            Object.DestroyImmediate(NativeMaterial);
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(Target);
            Object.DestroyImmediate(pixels);
        }
    }

    private static IEnumerator Wait(float seconds = 0.15f) { yield return new WaitForSecondsRealtime(seconds); }

    [UnityTest]
    public IEnumerator ThemesRenderBehindNativeUiAndRemoveCleanly()
    {
        using var fixture = new Fixture();
        yield return Wait();
        var baseline = fixture.Capture("00-native-baseline");
        fixture.AssertButtonUsable();
        foreach (var field in new[] { TerriasFieldId.MoonDomain, TerriasFieldId.ScorchingCanopy, TerriasFieldId.SamsaraGarden })
        {
            FieldApi.Set(field);
            yield return Wait(1.7f);
            var pixels = fixture.Capture("01-" + field);
            var changed = pixels.Where((pixel, i) => !pixel.Equals(baseline[i])).Count();
            Assert.That(changed, Is.GreaterThan(10000), "The actual field renderer must change GPU pixels: " + field);
            Assert.That(pixels[fixture.CardPixelIndex()], Is.EqualTo(baseline[fixture.CardPixelIndex()]),
                "Field backlight remains behind the native card face.");
            Assert.That(fixture.Background.GetComponentsInChildren<MeshFilter>().Any(item => item.sharedMesh.vertexCount > 0), Is.True);
            fixture.AssertButtonUsable();
            Assert.That(fixture.Background.GetComponentsInChildren<SpriteRenderer>().All(item => item.sharedMaterial == fixture.NativeMaterial), Is.True);
        }
        FieldApi.Set(TerriasFieldId.None, 0);
        yield return Wait(1.8f);
        Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Null);
        Assert.That(fixture.Capture("02-field-removed"), Is.EqualTo(baseline), "Every native pixel must be restored after field removal.");
        fixture.AssertButtonUsable();
    }

    [UnityTest]
    public IEnumerator RapidChangesSettingsAndMissingArtKeepOwnershipBounded()
    {
        using var fixture = new Fixture();
        FieldApi.Set(TerriasFieldId.ScorchingCanopy);
        yield return Wait();
        FieldApi.Set(TerriasFieldId.MoonDomain);
        FieldApi.Set(TerriasFieldId.SamsaraGarden, 5);
        yield return Wait(1.7f);
        Assert.That(Object.FindObjectsByType<FieldPresentationView>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        FieldFixtureRuntime.Options.BackgroundsEnabled = false;
        yield return Wait();
        Assert.That(fixture.Background.GetComponentsInChildren<MeshFilter>().Where(item => item.name.StartsWith("FieldBackdrop_")).All(item => item.sharedMesh.vertexCount == 0), Is.True);
        FieldFixtureRuntime.Options.Enabled = false;
        yield return Wait();
        Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Null);
        FieldFixtureRuntime.Options.Enabled = true;
        FieldFixtureRuntime.Options.ReducedMotion = true;
        FieldFixtureRuntime.Options.Quality = "low";
        FieldFixtureRuntime.MissingTextures = true;
        FieldFixtureRuntime.Options.BackgroundsEnabled = true;
        FieldApi.Set(TerriasFieldId.MoonDomain);
        yield return Wait(1.7f);
        Assert.That(GameObject.Find("FieldGround"), Is.Not.Null, "Reduced motion and missing art retain the field motif.");
        Assert.That(fixture.Background.GetComponentsInChildren<MeshFilter>().Where(item => item.name.StartsWith("FieldBackdrop_")).All(item => item.sharedMesh.vertexCount == 0), Is.True,
            "Missing art preserves the original background while the field environment remains visible.");
        fixture.AssertButtonUsable();
        var still = fixture.Capture("03-low-reduced-motion");
        yield return Wait();
        Assert.That(fixture.Capture("03-low-reduced-motion-repeat"), Is.EqualTo(still));
        fixture.FightUi.gameObject.SetActive(false);
        yield return Wait();
        Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Null);
        fixture.FightUi.gameObject.SetActive(true);
        yield return Wait();
        Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator SettlementCancelsPendingWorkAndNextBattleRehydrates()
    {
        using var fixture = new Fixture();
        FieldApi.Set(TerriasFieldId.MoonDomain);
        TerriasBattleLifecycleRouter.Subscription!.OutcomeEntering!(null!);
        yield return Wait();
        Assert.That(GameObject.Find("Terrias_FieldPresentation"), Is.Null, "Pending callbacks cannot resurrect effects after settlement.");
        for (var i = 0; i < 3; i++)
        {
            TerriasBattleLifecycleRouter.Subscription.BattleInitializing!(null!);
            TerriasBattleLifecycleRouter.Subscription.BattleMaterialized!(null!);
            yield return Wait();
            Assert.That(Object.FindObjectsByType<FieldPresentationView>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            TerriasBattleLifecycleRouter.Subscription.OutcomeEntering!(null!);
            yield return Wait();
            Assert.That(Object.FindObjectsByType<FieldPresentationView>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Null);
        }
        TerriasBattleLifecycleRouter.Subscription.BattleInitializing!(null!);
        TerriasBattleLifecycleRouter.Subscription.OutcomeEntering!(null!);
        TerriasBattleLifecycleRouter.Subscription.BattleInitializing!(null!);
        yield return Wait();
        Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Not.Null, "Same-frame new battle must not lose its refresh to an old generation.");
    }

    [UnityTest]
    public IEnumerator CameraMotionAspectChangesAndBackgroundReplacementStayAligned()
    {
        using var fixture = new Fixture();
        FieldApi.Set(TerriasFieldId.MoonDomain);
        yield return Wait(1.7f);
        fixture.Camera.transform.position += new Vector3(0.8f, 0.25f, 0f);
        fixture.Resize(1680, 720);
        yield return Wait();
        Assert.That(FieldFixtureRuntime.Scene!.TryWorldBounds(out var bounds), Is.True);
        var backdrop = GameObject.Find("FieldBackdrop_moon_domain").GetComponent<MeshRenderer>().bounds;
        Assert.That(backdrop.min.x, Is.EqualTo(bounds.xMin).Within(0.002f));
        Assert.That(backdrop.max.x, Is.EqualTo(bounds.xMax).Within(0.002f));
        Assert.That(backdrop.min.y, Is.EqualTo(bounds.yMin).Within(0.002f));
        Assert.That(backdrop.max.y, Is.EqualTo(bounds.yMax).Within(0.002f));
        fixture.Capture("04-wide-camera-moved");
        FieldFixtureRuntime.Options.Quality = "low";
        for (var i = 0; i < 6; i++)
        {
            fixture.Camera.transform.position += new Vector3(0.06f, 0f, 0f);
            yield return null;
            Assert.That(FieldFixtureRuntime.Scene.TryWorldBounds(out bounds), Is.True);
            backdrop = GameObject.Find("FieldBackdrop_moon_domain").GetComponent<MeshRenderer>().bounds;
            Assert.That(backdrop.min.x, Is.EqualTo(bounds.xMin).Within(0.002f),
                "Backdrop coverage must follow every camera frame even when particle geometry is throttled.");
        }

        var replacement = new GameObject("ReplacementNativeBackground");
        replacement.transform.SetParent(fixture.Root.transform);
        var ground = new GameObject("groundPos").transform;
        ground.SetParent(replacement.transform);
        ground.position = new Vector3(0f, -0.75f, 0f);
        FieldFixtureRuntime.Scene = new FieldPresentationScene(fixture.FightUi, replacement, fixture.Camera,
            ground, fixture.Hand, null, null);
        yield return Wait();
        Assert.That(fixture.Background.transform.Find("Terrias_FieldEnvironment"), Is.Null);
        Assert.That(replacement.transform.Find("Terrias_FieldEnvironment"), Is.Not.Null);
        Assert.That(Object.FindObjectsByType<FieldPresentationView>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        fixture.AssertButtonUsable();
        FieldFixtureRuntime.Scene = null;
        yield return Wait();
        Assert.That(GameObject.Find("Terrias_FieldEnvironment"), Is.Null, "Losing the native scene must immediately hide field objects.");
    }
}
