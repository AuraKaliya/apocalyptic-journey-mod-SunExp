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
        private const string MaterialPath = RootPath + "/Materials/SunExpFoilHoloFrameOverlay.mat";
        private const string ShaderPath = RootPath + "/Shaders/CardFaceEffect.shader";
        private const string BackgroundPath = RootPath + "/Art/CardBackground.png";
        private const string FramePath = RootPath + "/Art/SunCardFrame.png";
        private const string NoisePath = RootPath + "/Effects/WunaOrbitTrailNoise.png";
        private const string FoilPath = RootPath + "/Effects/PokemonHoloFoil.png";

        [MenuItem("Aura/Card Frame FX Lab/Rebuild Scene")]
        public static void Build()
        {
            PlayerSettings.productName = "AuraCardFrameFxLab";
            ConfigureSpriteTexture(BackgroundPath);
            ConfigureSpriteTexture(FramePath);
            ConfigureEffectTexture(NoisePath);
            ConfigureEffectTexture(FoilPath);
            AssetDatabase.Refresh();

            var backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
            var noiseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            var foilTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FoilPath);
            var material = BuildMaterial(noiseTexture, foilTexture);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvas = CreateCanvas();
            CreateStageBackground(canvas.transform);
            var cardRoot = CreateCardPreview(canvas.transform, backgroundSprite, frameSprite, material, out var backgroundImage, out var baseFrameImage, out var effectFrameImage);
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
            RequireObject("SunCardFrame");
            RequireObject("SunCardFrameFoilOverlay");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || shader.name != "SunExp/CardFaceEffect")
            {
                throw new System.InvalidOperationException("CardFaceEffect shader is not available.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null || material.shader != shader)
            {
                throw new System.InvalidOperationException("Frame overlay material is missing or uses the wrong shader.");
            }

            if (!Mathf.Approximately(material.GetFloat("_SunExpOverlayMode"), 1f))
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
            baseFrameImage = CreateImage("SunCardFrame", root, frameSprite, new Vector2(384f, 384f), Vector2.zero, Color.white, null);
            effectFrameImage = CreateImage("SunCardFrameFoilOverlay", root, frameSprite, new Vector2(384f, 384f), Vector2.zero, Color.white, material);
            effectFrameImage.raycastTarget = false;
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
            controller.ResetSunExpDefaults();
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
                material = new Material(shader) { name = "SunExpFoilHoloFrameOverlay" };
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
            SetFloat(material, "_SunExpEffectMode", 0f);
            SetFloat(material, "_SunExpOverlayMode", 1f);
            SetFloat(material, "_SunExpFrameOnlyOverlay", 0f);
            SetFloat(material, "_SunExpFoilMode", 1f);
            SetFloat(material, "_SunExpFlowSpeed", 0.55f);
            SetFloat(material, "_SunExpFlowScale", 1.22f);
            SetFloat(material, "_SunExpNoiseScale", 4f);
            SetFloat(material, "_SunExpDistortion", 0.009f);
            SetFloat(material, "_SunExpEffectIntensity", 1.04f);
            SetFloat(material, "_SunExpQualityScale", 1f);
            SetFloat(material, "_SunExpEdgeGlow", 0.28f);
            SetFloat(material, "_SunExpSweepFrequency", 4.4f);
            SetFloat(material, "_SunExpSweepWidth", 0.13f);
            SetFloat(material, "_SunExpSweepIntensity", 1.12f);
            SetFloat(material, "_SunExpPrismScale", 13.5f);
            SetFloat(material, "_SunExpPrismStrength", 1f);
            SetFloat(material, "_SunExpFoilGrain", 0.08f);
            SetFloat(material, "_SunExpMirrorSweep", 0.58f);
            SetFloat(material, "_SunExpSwirlStrength", 0.06f);
            SetFloat(material, "_SunExpFoilShardScale", 18f);
            SetFloat(material, "_SunExpFoilShardWarp", 0.08f);
            SetFloat(material, "_SunExpFoilGalaxyDensity", 0.015f);
            SetFloat(material, "_SunExpFoilGlintSpeed", 1.1f);
            SetFloat(material, "_SunExpFoilTextureStrength", 0.6f);
            SetFloat(material, "_SunExpRainbowStrength", 1.25f);
            SetFloat(material, "_SunExpRidgeStrength", 0.7f);
            SetFloat(material, "_SunExpGlareStrength", 0.35f);
            SetFloat(material, "_SunExpPointerAutoSpeed", 0.78f);
            SetFloat(material, "_SunExpFoilOverlayAlpha", 1f);
            SetFloat(material, "_SunExpPointerX", -1f);
            SetFloat(material, "_SunExpPointerY", -1f);
            SetFloat(material, "_SunExpEdgeSample", 2f);
            SetColor(material, "_SunExpHoloColorA", new Color32(255, 240, 166, 255));
            SetColor(material, "_SunExpHoloColorB", new Color32(166, 242, 255, 255));
            SetColor(material, "_SunExpHoloColorC", new Color32(210, 184, 255, 255));
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
