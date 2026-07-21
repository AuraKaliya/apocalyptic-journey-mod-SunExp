using AuraCardFrameFxLab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AuraCardFrameFxLabEditor
{
    public static class AuraCardFrameFxLabSceneBuilder
    {
        private const string RootPath = "Assets/AuraCardFrameFxLab";
        private const string ScenePath = RootPath + "/Scenes/CardFrameFxLab.unity";
        private const string MaterialPath = RootPath + "/Materials/TerriasFoilHoloFrameOverlay.mat";
        private const string ShaderPath = RootPath + "/Shaders/CardFaceEffect.shader";
        private const string BackgroundPath = RootPath + "/Art/CardBackground.png";
        private const string FramePath = RootPath + "/Art/SunCardFrame.png";
        private const string CardArtPath = RootPath + "/Art/BlazingCrownCollapse.png";
        private const string NoisePath = RootPath + "/Effects/WunaOrbitTrailNoise.png";
        private const string FoilPath = RootPath + "/Effects/PokemonHoloFoil.png";

        [MenuItem("Aura/Card Frame FX Lab/Rebuild Scene")]
        public static void Build()
        {
            PlayerSettings.productName = "AuraCardFrameFxLab";
            ConfigureSpriteTexture(BackgroundPath);
            ConfigureSpriteTexture(FramePath);
            ConfigureSpriteTexture(CardArtPath);
            ConfigureEffectTexture(NoisePath);
            ConfigureEffectTexture(FoilPath);
            AssetDatabase.Refresh();

            var backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
            var cardArtSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardArtPath);
            var noiseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            var foilTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FoilPath);
            var material = BuildMaterial(noiseTexture, foilTexture);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvas = CreateCanvas();
            CreateStageBackground(canvas.transform);
            var cardRoot = CreateCardPreview(canvas.transform, backgroundSprite, frameSprite, cardArtSprite, material, out var backgroundImage, out var baseFrameImage, out var effectFrameImage);
            CreateCamera();
            CreateController(backgroundImage, baseFrameImage, effectFrameImage, material, noiseTexture, foilTexture);

            Selection.activeGameObject = cardRoot.gameObject;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("AuraCardFrameFxLab scene rebuilt: " + ScenePath);
        }

        [MenuItem("Aura/Card Frame FX Lab/Smoke Test")]
        public static void SmokeTest()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new System.InvalidOperationException("Unable to open scene: " + ScenePath);
            }

            var controller = Object.FindObjectOfType<AuraCardFrameFxLabController>();
            if (controller == null)
            {
                throw new System.InvalidOperationException("Missing LabController with AuraCardFrameFxLabController.");
            }

            RequireObject("CardPreview");
            RequireObject("CardBackground");
            RequireObject("BlazingCrownCollapseArt");
            RequireObject("SunCardFrame");
            RequireObject("SunCardFrameFoilOverlay");
            RequireObject("CardCost");
            RequireObject("CardTitle");
            RequireObject("CardDescription");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || shader.name != "Terrias/CardFaceEffect")
            {
                throw new System.InvalidOperationException("CardFaceEffect shader is not available.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null || material.shader != shader)
            {
                throw new System.InvalidOperationException("Frame overlay material is missing or uses the wrong shader.");
            }

            if (!Mathf.Approximately(material.GetFloat("_TerriasOverlayMode"), 1f))
            {
                throw new System.InvalidOperationException("Frame overlay material is not in overlay mode.");
            }

            var buildSettingsHasScene = false;
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                buildSettingsHasScene |= buildScene.enabled && buildScene.path == ScenePath;
            }

            if (!buildSettingsHasScene)
            {
                throw new System.InvalidOperationException("EditorBuildSettings does not include " + ScenePath);
            }

            Debug.Log("AuraCardFrameFxLab smoke test passed.");
        }

        private static Canvas CreateCanvas()
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void RequireObject(string objectName)
        {
            if (GameObject.Find(objectName) == null)
            {
                throw new System.InvalidOperationException("Missing scene object: " + objectName);
            }
        }

        private static void CreateStageBackground(Transform parent)
        {
            var image = CreateImage("StageBackground", parent, null, new Vector2(1280f, 720f), Vector2.zero, new Color32(15, 16, 24, 255), null);
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateCardPreview(
            Transform parent,
            Sprite backgroundSprite,
            Sprite frameSprite,
            Sprite cardArtSprite,
            Material material,
            out Image backgroundImage,
            out Image baseFrameImage,
            out Image effectFrameImage)
        {
            var rootObject = new GameObject("CardPreview", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(-230f, 0f);
            root.sizeDelta = new Vector2(384f, 384f);

            var shadow = CreateImage("PreviewBackdrop", root, null, new Vector2(438f, 500f), Vector2.zero, new Color32(5, 6, 10, 210), null);
            shadow.transform.SetAsFirstSibling();
            backgroundImage = CreateImage("CardBackground", root, backgroundSprite, new Vector2(384f, 384f), Vector2.zero, Color.white, null);
            var artImage = CreateImage("BlazingCrownCollapseArt", root, cardArtSprite, new Vector2(176f, 176f), new Vector2(0f, 76f), Color.white, null);
            artImage.preserveAspect = false;
            baseFrameImage = CreateImage("SunCardFrame", root, frameSprite, new Vector2(384f, 384f), Vector2.zero, Color.white, null);
            effectFrameImage = CreateImage("SunCardFrameFoilOverlay", root, frameSprite, new Vector2(384f, 384f), Vector2.zero, Color.white, material);
            effectFrameImage.raycastTarget = false;
            CreateText("CardCost", root, "3", new Vector2(34f, 26f), new Vector2(-82f, 154f), 29, FontStyle.Bold, TextAnchor.MiddleCenter, new Color32(255, 222, 42, 255));
            CreateText("CardTitle", root, "炽冕崩落", new Vector2(152f, 28f), new Vector2(10f, 153f), 21, FontStyle.Bold, TextAnchor.MiddleLeft, new Color32(242, 246, 255, 255));
            CreateText(
                "CardDescription",
                root,
                "对敌方全体造成40（40+星辉*系数）点伤害。若没有圣灵星辉，自身承受等额反噬。随后结束圣灵星辉，消耗全部聚炎，自身获得消耗聚炎层数一半的灼烧。",
                new Vector2(182f, 86f),
                new Vector2(0f, -119f),
                14,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                new Color32(244, 240, 228, 255));
            return root;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 position, Color color, Material material)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.material = material;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, string text, Vector2 size, Vector2 position, int fontSize, FontStyle style, TextAnchor alignment, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var label = textObject.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            label.supportRichText = true;

            var shadow = textObject.GetComponent<Shadow>();
            shadow.effectColor = new Color32(24, 28, 44, 190);
            shadow.effectDistance = new Vector2(1f, -1f);
            shadow.useGraphicAlpha = true;
            return label;
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("SceneCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(15, 16, 24, 255);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void CreateController(Image backgroundImage, Image baseFrameImage, Image effectFrameImage, Material material, Texture2D noiseTexture, Texture2D foilTexture)
        {
            var controllerObject = new GameObject("LabController", typeof(AuraCardFrameFxLabController));
            var controller = controllerObject.GetComponent<AuraCardFrameFxLabController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("backgroundImage").objectReferenceValue = backgroundImage;
            serialized.FindProperty("baseFrameImage").objectReferenceValue = baseFrameImage;
            serialized.FindProperty("effectFrameImage").objectReferenceValue = effectFrameImage;
            serialized.FindProperty("frameEffectMaterial").objectReferenceValue = material;
            serialized.FindProperty("noiseTexture").objectReferenceValue = noiseTexture;
            serialized.FindProperty("foilTexture").objectReferenceValue = foilTexture;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            controller.ResetTerriasDefaults();
            EditorUtility.SetDirty(controller);
        }

        private static Material BuildMaterial(Texture2D noiseTexture, Texture2D foilTexture)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                throw new System.InvalidOperationException("Missing shader: " + ShaderPath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "TerriasFoilHoloFrameOverlay" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            SetMaterialDefaults(material, noiseTexture, foilTexture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetMaterialDefaults(Material material, Texture2D noiseTexture, Texture2D foilTexture)
        {
            SetTexture(material, "_NoiseTex", noiseTexture);
            SetTexture(material, "_FoilTex", foilTexture);
            SetFloat(material, "_TerriasEffectMode", 0f);
            SetFloat(material, "_TerriasOverlayMode", 1f);
            SetFloat(material, "_TerriasFrameOnlyOverlay", 0f);
            SetFloat(material, "_TerriasFoilMode", 1.603f);
            SetFloat(material, "_TerriasFlowSpeed", -2f);
            SetFloat(material, "_TerriasFlowScale", 1.603f);
            SetFloat(material, "_TerriasNoiseScale", 7.722f);
            SetFloat(material, "_TerriasDistortion", 0.021f);
            SetFloat(material, "_TerriasEffectIntensity", 1.093f);
            SetFloat(material, "_TerriasQualityScale", 1f);
            SetFloat(material, "_TerriasEdgeGlow", 0.12f);
            SetFloat(material, "_TerriasSweepFrequency", 3.716f);
            SetFloat(material, "_TerriasSweepWidth", 0.18f);
            SetFloat(material, "_TerriasSweepIntensity", 0.892f);
            SetFloat(material, "_TerriasPrismScale", 18.19f);
            SetFloat(material, "_TerriasPrismStrength", 0.759f);
            SetFloat(material, "_TerriasFoilGrain", 0.453f);
            SetFloat(material, "_TerriasMirrorSweep", 0.846f);
            SetFloat(material, "_TerriasSwirlStrength", 0.206f);
            SetFloat(material, "_TerriasFoilShardScale", 15.367f);
            SetFloat(material, "_TerriasFoilShardWarp", 0.162f);
            SetFloat(material, "_TerriasFoilGalaxyDensity", 0.148f);
            SetFloat(material, "_TerriasFoilGlintSpeed", 1.1f);
            SetFloat(material, "_TerriasFoilTextureStrength", 0.967f);
            SetFloat(material, "_TerriasRainbowStrength", 1.25f);
            SetFloat(material, "_TerriasRidgeStrength", 0.523f);
            SetFloat(material, "_TerriasGlareStrength", 1.008f);
            SetFloat(material, "_TerriasPointerAutoSpeed", 0.646f);
            SetFloat(material, "_TerriasFoilOverlayAlpha", 1.557f);
            SetFloat(material, "_TerriasPointerX", -1f);
            SetFloat(material, "_TerriasPointerY", -1f);
            SetFloat(material, "_TerriasEdgeSample", 3.218f);
            SetColor(material, "_TerriasHoloColorA", new Color32(255, 240, 166, 255));
            SetColor(material, "_TerriasHoloColorB", new Color32(166, 242, 255, 255));
            SetColor(material, "_TerriasHoloColorC", new Color32(210, 184, 255, 255));
        }

        private static void ConfigureSpriteTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = 100f;
            importer.SaveAndReimport();
        }

        private static void ConfigureEffectTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }

        private static void SetTexture(Material material, string propertyName, Texture texture)
        {
            if (texture != null && material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, texture);
            }
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetColor(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }
    }

    [InitializeOnLoad]
    internal static class AuraCardFrameFxLabStartup
    {
        private const string ScenePath = "Assets/AuraCardFrameFxLab/Scenes/CardFrameFxLab.unity";

        static AuraCardFrameFxLabStartup()
        {
            EditorApplication.delayCall += OpenDefaultSceneWhenProjectStarts;
        }

        private static void OpenDefaultSceneWhenProjectStarts()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!System.IO.File.Exists(ScenePath))
            {
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(activeScene.path))
            {
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }
}
