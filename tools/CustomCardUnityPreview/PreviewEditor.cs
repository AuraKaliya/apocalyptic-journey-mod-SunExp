using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

public static class CustomCardPreviewEditor
{
    public static void Build()
    {
        if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMPro.TMP_Text).Assembly).resolvedPath;
            var essentials = Directory.GetFiles(package,"*.unitypackage",SearchOption.AllDirectories).First(p=>p.Contains("Essential"));
            AssetDatabase.ImportPackage(essentials,false);
            AssetDatabase.Refresh();
        }
        var path=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-cardBuild=")).Substring(11);
        Directory.CreateDirectory(Path.GetDirectoryName(path));Directory.CreateDirectory("Assets/Scenes");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.SaveScene(scene,"Assets/Scenes/Preview.unity");
        PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=900;PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=UnityEngine.FullScreenMode.Windowed;
        var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Preview.unity"},locationPathName=path,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        if(r.summary.result!=BuildResult.Succeeded)throw new Exception("Custom card preview build failed.");
    }
}
