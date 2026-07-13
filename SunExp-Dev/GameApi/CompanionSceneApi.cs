using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunExp.Dll.GameApi;

public static class CompanionSceneApi
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<int> BattleSceneHandles = new();

    public static bool MoveToOwnerScene(GameObject? instance, GameObject? owner, string source)
    {
        if (instance == null || owner == null)
        {
            return false;
        }

        var ownerScene = owner.scene;
        if (!TrackBattleScene(ownerScene, source))
        {
            return false;
        }

        try
        {
            if (instance.scene.handle == ownerScene.handle)
            {
                return true;
            }

            if (instance.transform.parent != null)
            {
                SunExpLog.Warn("[CompanionScene] cannot move non-root object from " + source + ": " + instance.name);
                return false;
            }

            SceneManager.MoveGameObjectToScene(instance, ownerScene);
            SunExpPerformanceCounters.Record("CompanionScene.ObjectMoved");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[CompanionScene] object move failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool TrackBattleScene(Scene scene, string source)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        lock (SyncRoot)
        {
            BattleSceneHandles.Add(scene.handle);
        }

        SunExpLog.Debug("[CompanionScene] tracked scene=" + scene.name + ", handle=" + scene.handle + ", source=" + source);
        return true;
    }

    public static bool IsTracked(Scene scene)
    {
        return scene.IsValid() && IsTracked(scene.handle);
    }

    public static bool IsTracked(int sceneHandle)
    {
        lock (SyncRoot)
        {
            return BattleSceneHandles.Contains(sceneHandle);
        }
    }

    public static bool IsSceneLoaded(int sceneHandle)
    {
        try
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.handle == sceneHandle)
                {
                    return scene.IsValid() && scene.isLoaded;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearTrackedScenes(string source)
    {
        int count;
        lock (SyncRoot)
        {
            count = BattleSceneHandles.Count;
            BattleSceneHandles.Clear();
        }

        SunExpLog.Debug("[CompanionScene] cleared tracked scenes from " + source + ": count=" + count);
    }
}
