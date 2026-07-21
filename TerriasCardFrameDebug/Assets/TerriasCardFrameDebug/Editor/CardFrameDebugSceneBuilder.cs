using System;
using System.IO;
using Terrias.CardFrameDebug;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Terrias.CardFrameDebug.Editor
{
    public static class CardFrameDebugSceneBuilder
    {
        private const string AssetRoot = "Assets/TerriasCardFrameDebug";
        private const string BackgroundPath = AssetRoot + "/Art/卡面背景.png";
        private const string FramePath = AssetRoot + "/Art/日耀-卡框1.png";
        private const string ShaderPath = AssetRoot + "/Shaders/CardFrameHoloFlow.shader";
        private const string NoisePath = AssetRoot + "/Textures/DebugFlowNoise.png";
        private const string MaterialPath = AssetRoot + "/Materials/TerriasCardFrameHoloDebug.mat";
        private const string ScenePath = AssetRoot + "/Scenes/CardFrameHoloDebug.unity";
        private const string PreviewPath = AssetRoot + "/Export/card_frame_preview.png";

        [MenuItem("Terrias/Card Frame Debug/Rebuild Preview Scene")]
        public static void Build()
        {
            EnsureFolders();
            AssetDatabase.Refresh();
            ConfigureSprite(BackgroundPath);
            ConfigureSprite(FramePath);
            var noise = EnsureNoiseTexture();
            ConfigureNoiseTexture(NoisePath);
            AssetDatabase.Refresh();

            var material = EnsureFrameMaterial(noise);
            var backgroundSprite = Load<Sprite>(BackgroundPath);
            var frameSprite = Load<Sprite>(FramePath);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = CreateCamera();
            var canvas = CreateCanvas(camera);
            CreateEventSystem();
            CreateBackdrop(canvas.transform);
            var cardRoot = CreateRect("CardPreview", canvas.transform, new Vector2(360, 360), Vector2.zero);
            var background = CreateImage("CardBackground", cardRoot, backgroundSprite, null);
            var frame = CreateImage("SunFrameWithFoil", cardRoot, frameSprite, material);
            background.raycastTarget = false;
            frame.raycastTarget = false;

            var controller = cardRoot.gameObject.AddComponent<CardFrameHoloDebugController>();
            controller.cardRoot = cardRoot;
            controller.cardBackground = background;
            controller.cardFrame = frame;
            controller.frameMaterial = material;
            controller.flowNoise = noise;
            controller.ResetToTerriasDefaults();
            controller.ExportCurrentProfile();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Terrias card frame debug scene ready: " + ScenePath);
        }

        [MenuItem("Terrias/Card Frame Debug/Build And Capture Preview")]
        public static void BuildAndCapture()
        {
            Build();
            CapturePreview();
        }

        [MenuItem("Terrias/Card Frame Debug/Capture Preview")]
        public static void CapturePreview()
        {
            if (!File.Exists(ScenePath))
            {
                Build();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var camera = Camera.main;
            if (camera == null)
            {
                throw new InvalidOperationException("The debug scene has no Main Camera.");
            }

            Canvas.ForceUpdateCanvases();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var screenshot = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                screenshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                screenshot.Apply();
                File.WriteAllBytes(PreviewPath, screenshot.EncodeToPNG());
                AssetDatabase.ImportAsset(PreviewPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("Captured Terrias card frame preview: " + PreviewPath);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }

        private static void EnsureFolders()
        {
            foreach (var folder in new[]
            {
                AssetRoot + "/Art",
                AssetRoot + "/Shaders",
                AssetRoot + "/Scripts",
                AssetRoot + "/Editor",
                AssetRoot + "/Materials",
                AssetRoot + "/Scenes",
                AssetRoot + "/Textures",
                AssetRoot + "/Export"
            })
            {
                Directory.CreateDirectory(folder);
            }
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException("Required debug asset is missing.", path);
            }

            return asset;
        }

        private static void ConfigureSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Sprite texture importer is missing.", path);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100;
            importer.SaveAndReimport();
        }

        private static Texture2D EnsureNoiseTexture()
        {
            if (!File.Exists(NoisePath))
            {
                var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
                var random = new System.Random(20260702);
                for (var y = 0; y < texture.height; y++)
                {
                    for (var x = 0; x < texture.width; x++)
                    {
                        var wave = Mathf.Sin((x * 0.173f) + (y * 0.097f)) * 0.5f + 0.5f;
                        var speckle = (float)random.NextDouble();
                        var value = Mathf.Clamp01(wave * 0.55f + speckle * 0.45f);
                        texture.SetPixel(x, y, new Color(value, 1.0f - value, speckle, 1.0f));
                    }
                }

                texture.Apply();
                File.WriteAllBytes(NoisePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(NoisePath, ImportAssetOptions.ForceSynchronousImport);
            return Load<Texture2D>(NoisePath);
        }

        private static void ConfigureNoiseTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Noise texture importer is missing.", path);
            }

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Material EnsureFrameMaterial(Texture2D noise)
        {
            var shader = Load<Shader>(ShaderPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "TerriasCardFrameHoloDebug"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_NoiseTex", noise);
            material.SetFloat("_TerriasFlowSpeed", 0.36f);
            material.SetFloat("_TerriasFlowScale", 1.65f);
            material.SetFloat("_TerriasNoiseScale", 4.8f);
            material.SetFloat("_TerriasDistortion", 0.018f);
            material.SetFloat("_TerriasEffectIntensity", 0.72f);
            material.SetFloat("_TerriasQualityScale", 1.0f);
            material.SetFloat("_TerriasEdgeGlow", 0.22f);
            material.SetFloat("_TerriasSweepFrequency", 5.6f);
            material.SetFloat("_TerriasSweepWidth", 0.16f);
            material.SetFloat("_TerriasSweepIntensity", 0.9f);
            material.SetFloat("_TerriasPrismScale", 14.0f);
            material.SetFloat("_TerriasPrismStrength", 0.68f);
            material.SetFloat("_TerriasFoilGrain", 0.26f);
            material.SetFloat("_TerriasEdgeSample", 2.0f);
            material.SetColor("_TerriasHoloColorA", new Color(1.0f, 0.78f, 0.32f, 1.0f));
            material.SetColor("_TerriasHoloColorB", new Color(0.42f, 0.92f, 1.0f, 1.0f));
            material.SetColor("_TerriasHoloColorC", new Color(1.0f, 0.42f, 0.86f, 1.0f));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.025f, 0.035f, 1.0f);
            camera.orthographic = true;
            camera.orthographicSize = 5.0f;
            cameraObject.tag = "MainCamera";
            return camera;
        }

        private static Canvas CreateCanvas(Camera camera)
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10.0f;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void CreateEventSystem()
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private static void CreateBackdrop(Transform parent)
        {
            var rect = CreateRect("Backdrop", parent, new Vector2(1280, 720), Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.018f, 0.018f, 0.03f, 1.0f);
            image.raycastTarget = false;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 anchoredPosition)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Material material)
        {
            var rect = CreateRect(name, parent, new Vector2(360, 360), Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.material = material;
            image.color = Color.white;
            image.preserveAspect = true;
            return image;
        }
    }
}
