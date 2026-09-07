using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.GameApi;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ReplayLightingTests
{
    [UnityTest]
    public IEnumerator IsolatedLitHudKeepsItsTextureAndNativeLightOwnership() => VerifyLighting(false);

    [UnityTest]
    public IEnumerator InstalledHpAndDefenseTexturesMatchNativeCameraPixels() => VerifyLighting(true);

    private IEnumerator VerifyLighting(bool useNativeSprites)
    {
        var fixturePath = Path.Combine(Application.dataPath, "Tests/NativeHudFixtures");
        if (useNativeSprites && !File.Exists(Path.Combine(fixturePath, "manifest.json")))
            Assert.Ignore("Supply -GameDataDirectory to extract the installed game's HUD textures.");
        Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
            "This regression requires actual GPU pixels; do not run with -nographics.");
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQualityPipeline = QualitySettings.renderPipeline;
        var data = ScriptableObject.CreateInstance<Renderer2DData>();
        var feature = ScriptableObject.CreateInstance<ReplayGlobalLightRendererFeatureV17>();
        feature.SetActive(false);
        data.rendererFeatures.Add(feature);
        var pipeline = UniversalRenderPipelineAsset.Create(data);
        pipeline.msaaSampleCount = 1;
        var cameraObject = new GameObject("ReplayPixelCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        var lightObject = new GameObject("NativeGlobalLight");
        lightObject.SetActive(false);
        lightObject.layer = 0;
        var light = lightObject.AddComponent<Light2D>();
        typeof(Light2D).GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(light, new[] { 0 });
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1;
        light.color = Color.white;
        lightObject.SetActive(true);
        var spriteObject = new GameObject("NativeLitHud", typeof(SpriteRenderer));
        spriteObject.layer = 30;
        var renderer = spriteObject.GetComponent<SpriteRenderer>();
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        texture.SetPixels(Enumerable.Repeat(new Color(0.9f, 0.65f, 0.2f, 1), 256).ToArray());
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16);
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        renderer.sprite = sprite;
        renderer.sharedMaterial = material;
        var target = new RenderTexture(128, 128, 24);
        var pixels = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        try
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 30;
            camera.targetTexture = target;
            camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            yield return null;
            yield return null;
            camera.enabled = false;
            camera.Render();
            var dark = ReadCenter(target, pixels);
            Assert.That(dark.maxColorComponent, Is.LessThan(0.05f),
                "The old replay mask must reproduce the black lit-HUD regression.");
            feature.SetActive(true);
            camera.Render();
            var lit = ReadCenter(target, pixels);
            Assert.That(lit.r, Is.GreaterThan(0.5f));
            Assert.That(lit.g, Is.GreaterThan(0.35f));
            Assert.That(lit.b, Is.LessThan(lit.g));
            var first = pixels.GetPixels32();
            camera.Render();
            ReadCenter(target, pixels);
            Assert.That(pixels.GetPixels32(), Is.EqualTo(first), "Repeated manual frames retain identical texture pixels.");
            Assert.That(Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(lightObject.layer, Is.Zero);
            Assert.That(light.color, Is.EqualTo(Color.white));
            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(camera.cullingMask, Is.EqualTo(1 << 30));

            // The native renderer remains able to render the same source light,
            // and returning to replay does not accumulate extra global lights.
            feature.SetActive(false);
            camera.cullingMask |= 1;
            camera.Render();
            var native = ReadCenter(target, pixels);
            Assert.That(native.r, Is.EqualTo(lit.r).Within(0.01f));
            Assert.That(native.g, Is.EqualTo(lit.g).Within(0.01f));
            camera.cullingMask = 1 << 30;
            feature.SetActive(true);
            camera.Render();
            Assert.That(ReadCenter(target, pixels).r, Is.EqualTo(lit.r).Within(0.01f));
            if (useNativeSprites)
            {
                foreach (var name in new[] { "background", "defense" })
                {
                    var nativeTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    Sprite? nativeSprite = null;
                    try
                    {
                        Assert.That(nativeTexture.LoadImage(File.ReadAllBytes(Path.Combine(fixturePath, name + ".png"))), Is.True);
                        nativeSprite = Sprite.Create(nativeTexture,
                            new Rect(0, 0, nativeTexture.width, nativeTexture.height), Vector2.one * 0.5f,
                            Mathf.Max(nativeTexture.width, nativeTexture.height));
                        renderer.sprite = nativeSprite;
                        feature.SetActive(false);
                        camera.cullingMask = (1 << 30) | 1;
                        camera.Render();
                        ReadCenter(target, pixels);
                        var nativeFrame = pixels.GetPixels32();
                        Assert.That(nativeFrame.Count(pixel => pixel.r > 40 || pixel.g > 40 || pixel.b > 40), Is.GreaterThan(20),
                            "The actual native texture must have visible color pixels: " + name);
                        camera.cullingMask = 1 << 30;
                        camera.Render();
                        ReadCenter(target, pixels);
                        var darkFrame = pixels.GetPixels32();
                        Assert.That(darkFrame, Is.Not.EqualTo(nativeFrame));
                        File.WriteAllBytes(Path.Combine(fixturePath, name + "-before.png"), pixels.EncodeToPNG());
                        feature.SetActive(true);
                        camera.Render();
                        ReadCenter(target, pixels);
                        Assert.That(pixels.GetPixels32(), Is.EqualTo(nativeFrame),
                            "Every repaired HUD pixel must equal the native-light reference: " + name);
                        File.WriteAllBytes(Path.Combine(fixturePath, name + "-after.png"), pixels.EncodeToPNG());
                    }
                    finally
                    {
                        renderer.sprite = sprite;
                        if (nativeSprite != null) Object.DestroyImmediate(nativeSprite);
                        Object.DestroyImmediate(nativeTexture);
                    }
                }
            }
        }
        finally
        {
            camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(spriteObject);
            Object.DestroyImmediate(lightObject);
            GraphicsSettings.defaultRenderPipeline = previousPipeline;
            QualitySettings.renderPipeline = previousQualityPipeline;
            Object.DestroyImmediate(pipeline);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(feature);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(target);
        }
    }

    private static Color ReadCenter(RenderTexture target, Texture2D pixels)
    {
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixel(target.width / 2, target.height / 2);
        }
        finally { RenderTexture.active = previous; }
    }
}
