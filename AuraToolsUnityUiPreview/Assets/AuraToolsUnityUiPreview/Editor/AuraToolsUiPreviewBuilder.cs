using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AuraTools.UnityUiPreview.Editor
{
    public static class AuraToolsUiPreviewBuilder
    {
        private const string SceneDirectory = "Assets/AuraToolsUnityUiPreview/Scenes";
        private const string ScenePath = SceneDirectory + "/SettingsUiPreview.unity";

        [MenuItem("Aura/Tools UI Preview/Rebuild Scene")]
        public static void RebuildScene()
        {
            Directory.CreateDirectory(SceneDirectory);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureNativeTextures();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var application = new GameObject("AuraToolsUiPreviewApplication");
            application.AddComponent<SettingsPreviewController>();
            application.AddComponent<PreviewAutomation>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("AuraTools Unity UI preview scene ready: " + ScenePath);
        }

        [MenuItem("Aura/Tools UI Preview/Build Windows Player")]
        public static void BuildWindowsPlayer()
        {
            RebuildScene();
            ConfigurePlayer();
            var output = Environment.GetEnvironmentVariable("AURA_TOOLS_UNITY_PREVIEW_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "output", "unity", "aura-tools-ui-preview", "AuraToolsUiPreview.exe"));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "AuraTools Unity UI preview build failed: " + report.summary.result
                    + ", errors=" + report.summary.totalErrors);
            }
            Debug.Log("AuraTools Unity UI preview player built: " + output
                      + ", size=" + report.summary.totalSize);
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Aura";
            PlayerSettings.productName = "AuraTools UI Preview";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);
        }

        private static void ConfigureNativeTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:Texture2D",
                         new[]
                         {
                             "Assets/AuraToolsUnityUiPreview/Resources/NativeUi",
                             "Assets/AuraToolsUnityUiPreview/Resources/ToolboxV2"
                         }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }
                var changed = importer.textureType != TextureImporterType.Default
                              || importer.npotScale != TextureImporterNPOTScale.None
                              || importer.maxTextureSize < 2048
                              || importer.textureCompression != TextureImporterCompression.Uncompressed
                              || importer.mipmapEnabled
                              || importer.wrapMode != TextureWrapMode.Clamp
                              || importer.filterMode != FilterMode.Bilinear;
                if (!changed)
                {
                    continue;
                }
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }
    }
}
