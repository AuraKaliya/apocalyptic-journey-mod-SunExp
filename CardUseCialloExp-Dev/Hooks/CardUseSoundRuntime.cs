using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Witch.Mod;
using Witch.UI.Window;

namespace CardUseCialloExp.Dll.Hooks;

public static class CardUseSoundRuntime
{
    private const string AudioFileName = "audio.mp3";
    private const float CardActionReplacementWindowSeconds = 1.0f;
    private const int MaxReplacementsPerCardAction = 1;

    private static AudioClip? clip;
    private static AudioLoader? loader;
    private static bool loadStarted;
    private static float replaceEffectsUntilTime;
    private static int remainingCardActionReplacements;
    private static int cardActionCount;
    private static int skippedBeforeLoadCount;
    private static int replacedCount;

    public static void Initialize(ModConfig modConfig)
    {
        Debug.Log("[CardUseCialloExp] initialize begin, modDir=" + modConfig.DirectoryName);
        StartLoadingClip(modConfig);
        Debug.Log("[CardUseCialloExp] initialize end");
    }

    [HookAfter(typeof(Fight_Start), nameof(Fight_Start.Init))]
    public static void OnFightStart(Fight_Start __instance)
    {
        replaceEffectsUntilTime = 0f;
        remainingCardActionReplacements = 0;
        cardActionCount = 0;
        skippedBeforeLoadCount = 0;
        replacedCount = 0;
        Debug.Log("[CardUseCialloExp] fight start detected, replacement state reset");
    }

    [HookBefore(typeof(FightUI), nameof(FightUI.CallActionAnimation))]
    public static void BeforeCallActionAnimation(FightUI __instance, IScriptExecutor scriptExecutor)
    {
        if (!IsCardScriptExecutor(scriptExecutor))
        {
            return;
        }

        cardActionCount++;
        remainingCardActionReplacements = MaxReplacementsPerCardAction;
        replaceEffectsUntilTime = Time.unscaledTime + CardActionReplacementWindowSeconds;

        if (cardActionCount <= 5 || cardActionCount % 50 == 0)
        {
            Debug.Log("[CardUseCialloExp] card action detected, replacement window opened #" + cardActionCount
                + ", effect=" + ReadEffectName(scriptExecutor));
        }
    }

    [HookBefore(typeof(EffectSound), "Start")]
    public static void BeforeEffectSoundStart(EffectSound __instance)
    {
        try
        {
            if (__instance == null || !ShouldReplaceCurrentEffectSound())
            {
                return;
            }

            if (clip == null)
            {
                skippedBeforeLoadCount++;
                remainingCardActionReplacements = 0;
                Debug.LogWarning("[CardUseCialloExp] card effect sound detected but audio not loaded yet, skipped=" + skippedBeforeLoadCount);
                return;
            }

            if (ReferenceEquals(__instance.clip, clip))
            {
                return;
            }

            var originalName = __instance.clip == null ? "<null>" : __instance.clip.name;
            __instance.clip = clip;
            remainingCardActionReplacements--;
            replacedCount++;
            Debug.Log("[CardUseCialloExp] replaced card effect sound #" + replacedCount + ": " + originalName + " -> " + clip.name);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CardUseCialloExp] failed to replace card effect sound: " + ex);
        }
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

    private static bool ShouldReplaceCurrentEffectSound()
    {
        return remainingCardActionReplacements > 0 && Time.unscaledTime <= replaceEffectsUntilTime;
    }

    private static bool IsCardScriptExecutor(IScriptExecutor? scriptExecutor)
    {
        try
        {
            var dataConfig = scriptExecutor?.dataConfig;
            if (dataConfig == null)
            {
                return false;
            }

            if (dataConfig.Type == DataType.Card)
            {
                return true;
            }

            return dataConfig.data != null
                && dataConfig.data.ContainsKey("Expend")
                && dataConfig.data.ContainsKey("UseScript");
        }
        catch
        {
            return false;
        }
    }

    private static string ReadEffectName(IScriptExecutor? scriptExecutor)
    {
        try
        {
            if (scriptExecutor?.dataConfig?.data != null
                && scriptExecutor.dataConfig.data.TryGetValue("Effects", out var effectName)
                && !string.IsNullOrWhiteSpace(effectName))
            {
                return effectName;
            }
        }
        catch
        {
        }

        return "<none>";
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
