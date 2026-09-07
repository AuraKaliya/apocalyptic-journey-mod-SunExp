using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace EventCgPreview
{
    public static class PreviewEditor
    {
        public static void Build()
        {
            var output = Environment.GetCommandLineArgs().First(arg => arg.StartsWith("-cgBuildOutput="))
                .Substring("-cgBuildOutput=".Length);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Preview.unity");
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Preview.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("CG preview player build failed.");
        }
    }
}
