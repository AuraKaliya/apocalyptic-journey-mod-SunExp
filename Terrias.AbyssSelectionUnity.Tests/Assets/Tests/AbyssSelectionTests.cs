using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrias.Dll.Hooks.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class AbyssSelectionTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root = new("AbyssSelectionAcceptance");
        public readonly Camera Camera;
        public readonly RectTransform CanvasRoot;
        public readonly RenderTexture Target = new(1440, 900, 24);
        public readonly EventSystem Events;
        public readonly GraphicRaycaster Raycaster;
        public readonly List<Object> Owned = new();
        private readonly Texture2D pixels = new(1440, 900, TextureFormat.RGBA32, false);
        private readonly RenderPipelineAsset previous = GraphicsSettings.defaultRenderPipeline;
        private readonly RenderPipelineAsset previousQuality = QualitySettings.renderPipeline;
        private readonly Renderer2DData data = ScriptableObject.CreateInstance<Renderer2DData>();
        private readonly UniversalRenderPipelineAsset pipeline;

        public Fixture()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            pipeline = UniversalRenderPipelineAsset.Create(data);
            pipeline.msaaSampleCount = 1;
            GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = pipeline;
            Camera = new GameObject("AbyssSelectionCamera", typeof(Camera)).GetComponent<Camera>();
            Camera.transform.SetParent(Root.transform);
            Camera.transform.position = new Vector3(0f, 0f, -10f);
            Camera.orthographic = true;
            Camera.orthographicSize = 450f;
            Camera.aspect = 1.6f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.035f, 0.05f, 0.08f);
            Camera.targetTexture = Target;
            Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            CanvasRoot = Rect("Canvas", Root.transform, Vector2.zero, Vector2.zero);
            var canvas = CanvasRoot.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera;
            canvas.planeDistance = 10f;
            canvas.sortingOrder = 25;
            var scaler = CanvasRoot.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            Raycaster = CanvasRoot.gameObject.AddComponent<GraphicRaycaster>();
            Events = new GameObject("AbyssSelectionEvents", typeof(EventSystem)).GetComponent<EventSystem>();
            Events.transform.SetParent(Root.transform);
        }

        public Image Card(string file, Vector2 position, Vector2 size)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "Fixtures", file)));
            texture.Apply(false, true);
            Owned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect);
            Owned.Add(sprite);
            var rect = Rect(file, CanvasRoot, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            return image;
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
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/abyss-selection-unity"));
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previousTarget; Camera.enabled = true; }
        }

        public int Pixel(Vector3 world)
        {
            var point = Camera.WorldToViewportPoint(world);
            return Mathf.Clamp((int)(point.y * Target.height), 0, Target.height - 1) * Target.width
                + Mathf.Clamp((int)(point.x * Target.width), 0, Target.width - 1);
        }

        public void AssertHit(GameObject target)
        {
            var hits = new List<RaycastResult>();
            var pointer = new PointerEventData(Events) { position = RectTransformUtility.WorldToScreenPoint(Camera, target.transform.position) };
            Raycaster.Raycast(pointer, hits);
            Assert.That(hits.Any(hit => hit.gameObject == target), Is.True, "Transparent card input remains clickable.");
            Assert.That(hits.Any(hit => hit.gameObject.name == "SelectionSilhouette"), Is.False, "The glow never intercepts input.");
        }

        public void Dispose()
        {
            Camera.targetTexture = null;
            Object.DestroyImmediate(Root);
            TerriasUiSprites.Clear();
            foreach (var item in Owned) if (item != null) Object.DestroyImmediate(item);
            GraphicsSettings.defaultRenderPipeline = previous;
            QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(Target);
            Object.DestroyImmediate(pixels);
        }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    [UnityTest]
    public IEnumerator ActualArtworkHasBrightOuterContoursWithoutChangingItsFace()
    {
        using var fixture = new Fixture();
        var left = fixture.Card("深渊震荡卡片.png", new Vector2(-450f, 0f), new Vector2(390f, 520f));
        var middle = fixture.Card("深渊震荡卡片.png", Vector2.zero, new Vector2(390f, 520f));
        var right = fixture.Card("里程碑卡片.png", new Vector2(450f, 0f), new Vector2(347f, 520f));
        yield return null;
        var first = middle.gameObject.AddComponent<EndlessAbyssSelectionGlow>();
        first.Bind(middle);
        var second = right.gameObject.AddComponent<EndlessAbyssSelectionGlow>();
        second.Bind(right);
        yield return null;
        var baseline = fixture.Capture("01-artwork-unselected");
        first.SetSelected(true);
        second.SetSelected(true);
        yield return null;
        var selected = fixture.Capture("02-artwork-selected");
        Assert.That(selected.Where((pixel, i) => !pixel.Equals(baseline[i])).Count(), Is.GreaterThan(6000));
        Assert.That(selected[fixture.Pixel(middle.transform.position)], Is.EqualTo(baseline[fixture.Pixel(middle.transform.position)]),
            "The selected card face remains unchanged.");
        Assert.That(middle.color, Is.EqualTo(Color.white));
        fixture.AssertHit(middle.gameObject);
        first.SetSelected(false);
        second.SetSelected(false);
        Assert.That(fixture.Capture("03-selection-cleared"), Is.EqualTo(baseline));
        first.OnPointerEnter(new PointerEventData(fixture.Events));
        Assert.That(middle.transform.Find("SelectionSilhouette").GetComponent<Image>().color.a, Is.EqualTo(0.28f));
        first.SetSelected(true);
        first.OnPointerExit(new PointerEventData(fixture.Events));
        Assert.That(middle.transform.Find("SelectionSilhouette").GetComponent<Image>().enabled, Is.True,
            "Selection survives pointer exit.");
    }

    [UnityTest]
    public IEnumerator NativeMeshFrameKeepsItsMaterialAndTransparentClickTarget()
    {
        using var fixture = new Fixture();
        fixture.Camera.orthographic = false;
        fixture.Camera.fieldOfView = 75f;
        var cell = Rect("NativeCardCell", fixture.CanvasRoot, Vector2.zero, new Vector2(280f, 375f));
        var hit = cell.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var button = cell.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        var front = Rect("Front", cell, Vector2.zero, new Vector2(250f, 350f));
        Rect("background", front, Vector2.zero, Vector2.one).gameObject.AddComponent<MeshRenderer>();
        var frame = Rect("FrontBack", front, Vector2.zero, new Vector2(250f, 350f));
        frame.localPosition = new Vector3(0f, 0f, 140f);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-110f, -160f), new Vector3(-110f, 160f), new Vector3(110f, 160f), new Vector3(110f, -160f) };
        mesh.uv = new[] { new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f) };
        mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        frame.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var texture = new Texture2D(64, 96, TextureFormat.RGBA32, false);
        var pixels = new Color32[64 * 96];
        for (var y = 0; y < 96; y++)
        for (var x = 0; x < 64; x++)
            pixels[y * 64 + x] = new Color32(70, 90, 150, (byte)(x >= 5 && x <= 58 && y >= 5 && y <= 90 && (x > 20 || y > 25) ? 255 : 0));
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
        var renderer = frame.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        fixture.Owned.AddRange(new Object[] { mesh, material, texture });
        yield return null;
        Assert.That(UiSilhouetteSource.FromNativeCard(cell, cell).Bounds.width, Is.LessThan(210f),
            "Perspective depth changes the visible card bounds used by the contour.");
        var glow = cell.gameObject.AddComponent<EndlessAbyssSelectionGlow>();
        glow.BindNativeCard(cell);
        var baseline = fixture.Capture("04-native-mesh-unselected");
        glow.SetSelected(true);
        var selected = fixture.Capture("05-native-mesh-selected");
        Assert.That(selected.Where((pixel, i) => !pixel.Equals(baseline[i])).Count(), Is.GreaterThan(1000));
        Assert.That(renderer.sharedMaterial, Is.SameAs(material));
        Assert.That(material.mainTexture, Is.SameAs(texture));
        Assert.That(hit.color.a, Is.Zero);
        fixture.AssertHit(cell.gameObject);
        glow.Clear();
        cell.gameObject.SetActive(false);
        cell.gameObject.SetActive(true);
        glow.BindNativeCard(cell);
        Assert.That(cell.Find("SelectionSilhouette").GetComponent<Image>().enabled, Is.False,
            "A reused cell does not retain an old selection.");
        Assert.That(fixture.Capture("06-native-mesh-reused"), Is.EqualTo(baseline));
    }

    [UnityTest]
    public IEnumerator ScrollMaskClipsGlowAndUnreadableSourceReusesCachedMask()
    {
        using var fixture = new Fixture();
        var viewport = Rect("Viewport", fixture.CanvasRoot, Vector2.zero, new Vector2(330f, 280f));
        viewport.gameObject.AddComponent<Image>();
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var image = fixture.Card("深渊震荡卡片.png", Vector2.zero, new Vector2(270f, 360f));
        image.transform.SetParent(viewport, false);
        image.rectTransform.anchoredPosition = new Vector2(0f, 70f);
        yield return null;
        var glow = image.gameObject.AddComponent<EndlessAbyssSelectionGlow>();
        glow.Bind(image);
        var sprite = image.transform.Find("SelectionSilhouette").GetComponent<Image>().sprite;
        var baseline = fixture.Capture("07-scroll-baseline");
        glow.SetSelected(true);
        var selected = fixture.Capture("08-scroll-selected");
        Assert.That(selected.Where((pixel, i) => !pixel.Equals(baseline[i])).Count(), Is.GreaterThan(800));
        var outside = fixture.Pixel(viewport.TransformPoint(new Vector3(0f, 150f)));
        Assert.That(selected[outside], Is.EqualTo(baseline[outside]), "The halo obeys the scroll viewport mask.");
        glow.Clear();
        glow.Bind(image);
        Assert.That(image.transform.Find("SelectionSilhouette").GetComponent<Image>().sprite, Is.SameAs(sprite),
            "Rebinding reuses the cached alpha mask, including unreadable native textures.");
        Assert.That(fixture.Capture("09-scroll-cleared"), Is.EqualTo(baseline));
    }
}
