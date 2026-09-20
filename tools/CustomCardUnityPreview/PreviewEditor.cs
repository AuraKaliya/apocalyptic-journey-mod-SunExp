using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CustomCardPreviewEditor
{
    public static void Build()
    {
        if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
        {
            throw new InvalidOperationException("Prepare the matching UGUI TMP resources before building the preview.");
        }
        var path=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-cardBuild=")).Substring(11);
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/PreviewRenderer.asset");
        if(renderer==null){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/PreviewRenderer.asset");}
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/PreviewPipeline.asset");
        if(pipeline==null){pipeline=ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();AssetDatabase.CreateAsset(pipeline,"Assets/PreviewPipeline.asset");}
        var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;serialized.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
        PlayerSettings.colorSpace=ColorSpace.Linear;
        Directory.CreateDirectory(Path.GetDirectoryName(path));Directory.CreateDirectory("Assets/Scenes");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorSceneManager.SaveScene(scene,"Assets/Scenes/Preview.unity");
        PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=900;PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=UnityEngine.FullScreenMode.Windowed;
        var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Preview.unity"},locationPathName=path,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        if(r.summary.result!=BuildResult.Succeeded)throw new Exception("Custom card preview build failed.");
    }
}
