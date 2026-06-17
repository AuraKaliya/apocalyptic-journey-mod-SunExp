using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Witch.Mod;

namespace CardUseCialloExp.Dll.Hooks;

public static class CardUseSoundRuntime
{
    private const string AudioFileName = "audio.mp3";

    private static readonly object EventOwner = new();
    private static string? registeredStatusId;
    private static AudioClip? clip;
    private static AudioLoader? loader;
    private static bool loadStarted;
    private static int registerWaitLogCount;
    private static int skippedBeforeLoadCount;
    private static int playedCount;

    public static void Initialize(ModConfig modConfig)
    {
        Debug.Log("[CardUseCialloExp] initialize begin, modDir=" + modConfig.DirectoryName);
        StartLoadingClip(modConfig);
        TryRegisterForPlayer("Initialize");
        Debug.Log("[CardUseCialloExp] initialize end");
    }

    [HookAfter(typeof(Fight_Start), nameof(Fight_Start.Init))]
    public static void OnFightStart(Fight_Start __instance)
    {
        registeredStatusId = null;
        skippedBeforeLoadCount = 0;
        playedCount = 0;
        Debug.Log("[CardUseCialloExp] fight start detected, listener state reset");
        TryRegisterForPlayer("Fight_Start.Init");
    }

    [HookBefore(typeof(CommonCardItem), nameof(CommonCardItem.TrueUse))]
    public static void BeforeCommonTrueUse(CommonCardItem __instance)
    {
        TryRegisterForPlayer("CommonCardItem.TrueUse.ensure");
    }

    [HookBefore(typeof(AttackCardItem), nameof(AttackCardItem.TrueUse))]
    public static void BeforeAttackTrueUse(AttackCardItem __instance)
    {
        TryRegisterForPlayer("AttackCardItem.TrueUse.ensure");
    }

    private static void StartLoadingClip(ModConfig modConfig)
    {
        if (loadStarted)
        {
            Debug.Log("[CardUseCialloExp] audio load already started");
            return;
        }

        loadStarted = true;
        var audioPath = Path.Combine(modConfig.DirectoryName, AudioFileName);
        Debug.Log("[CardUseCialloExp] resolving audio path: " + audioPath);
        if (!File.Exists(audioPath))
        {
            Debug.LogWarning("[CardUseCialloExp] audio file not found: " + audioPath);
            return;
        }

        var gameObject = new GameObject("CardUseCialloExp.AudioLoader");
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        loader = gameObject.AddComponent<AudioLoader>();
        Debug.Log("[CardUseCialloExp] audio loader created, starting async mp3 load");
        loader.Load(audioPath, loadedClip =>
        {
            clip = loadedClip;
            Debug.Log(clip == null
                ? "[CardUseCialloExp] failed to load card use audio"
                : "[CardUseCialloExp] card use audio loaded: " + clip.name + ", length=" + clip.length.ToString("0.000") + "s");
        });
    }

    private static void TryRegisterForPlayer(string source)
    {
        try
        {
            var player = FightPlayer.Instance;
            var statusId = player?.Status?.InstanceId;
            if (string.IsNullOrWhiteSpace(statusId))
            {
                if (registerWaitLogCount < 5)
                {
                    registerWaitLogCount++;
                    Debug.Log("[CardUseCialloExp] listener not registered from " + source + ": player/status not ready");
                }

                return;
            }

            if (registeredStatusId == statusId)
            {
                return;
            }

            EventCenter.Instance.Clear(EventOwner);
            EventCenter.Instance.AddEventListener("ActionAfter" + statusId, new Action(OnCardPlayed), EventOwner, EventDispose.OnFightEnd);
            registeredStatusId = statusId;
            Debug.Log("[CardUseCialloExp] registered card use listener from " + source + ": statusId=" + statusId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CardUseCialloExp] failed to register card use listener from " + source + ": " + ex);
        }
    }

    private static void OnCardPlayed()
    {
        try
        {
            if (clip == null)
            {
                skippedBeforeLoadCount++;
                Debug.LogWarning("[CardUseCialloExp] card play detected but audio not loaded yet, skipped=" + skippedBeforeLoadCount);
                return;
            }

            playedCount++;
            Debug.Log("[CardUseCialloExp] card play detected, playing audio #" + playedCount + ": " + clip.name);
            AudioManager.Instance.PlayEffect(clip);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CardUseCialloExp] failed to play card use audio: " + ex);
        }
    }

    private sealed class AudioLoader : MonoBehaviour
    {
        public void Load(string path, Action<AudioClip?> onLoaded)
        {
            StartCoroutine(LoadCoroutine(path, onLoaded));
        }

        private static IEnumerator LoadCoroutine(string path, Action<AudioClip?> onLoaded)
        {
            var uri = new Uri(path).AbsoluteUri;
            Debug.Log("[CardUseCialloExp] audio request uri=" + uri);
            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[CardUseCialloExp] audio load error: result=" + request.result + ", error=" + request.error);
                onLoaded(null);
                yield break;
            }

            var loadedClip = DownloadHandlerAudioClip.GetContent(request);
            if (loadedClip != null)
            {
                loadedClip.name = Path.GetFileNameWithoutExtension(path);
                Debug.Log("[CardUseCialloExp] audio request succeeded, clip=" + loadedClip.name);
            }
            else
            {
                Debug.LogWarning("[CardUseCialloExp] audio request succeeded but clip content is null");
            }

            onLoaded(loadedClip);
        }
    }
}
