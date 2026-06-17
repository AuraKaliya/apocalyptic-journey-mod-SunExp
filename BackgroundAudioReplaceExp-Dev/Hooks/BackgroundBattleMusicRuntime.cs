using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using Witch.Core;
using Witch.Mod;

namespace BackgroundAudioReplaceExp.Dll.Hooks;

public static class BackgroundBattleMusicRuntime
{
    private const string LogPrefix = "[BackgroundAudioReplaceExp] ";
    private const string AudioFileName = "BGM.mp3";
    private const float FileCheckIntervalSeconds = 60f;
    private const int SilentSampleRate = 44100;

    private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private static string modDirectory = "";
    private static string audioPath = "";
    private static AudioClip? replacementClip;
    private static AudioClip? silentClip;
    private static LoaderRunner? runner;
    private static ReplacementLoadState loadState = ReplacementLoadState.NotStarted;
    private static BattleAudioMode battleMode = BattleAudioMode.None;
    private static BgmSnapshot? preBattleSnapshot;
    private static FileSignature cachedSignature = FileSignature.Missing;
    private static int loadGeneration;
    private static bool initialized;
    private static bool inBattle;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            Log("Initialize skipped because runtime is already initialized");
            return;
        }

        initialized = true;
        modDirectory = modConfig.DirectoryName;
        audioPath = Path.Combine(modDirectory, AudioFileName);

        Log("Initialize begin");
        Log("Mod directory: " + modDirectory);
        Log("Replacement audio path: " + audioPath);

        EnsureRunner();
        StartLoad("plugin initialize");
        StartFileWatcher();

        RegisterBefore(modConfig, "FightInit.Init", OnBeforeFightInit);
        RegisterAfter(modConfig, "FightInit.Init", OnAfterFightInit);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);

        Log("Initialize end");
    }

    private static void OnBeforeFightInit(ModHookContext context)
    {
        try
        {
            Log("FightInit.Init before: battle entering, saving current BGM state");
            if (inBattle)
            {
                LogWarning("A battle is already marked active; keeping the previous pre-battle snapshot");
                return;
            }

            var manager = AudioManager.Instance;
            if (manager == null)
            {
                preBattleSnapshot = null;
                LogWarning("AudioManager.Instance is null before battle; no BGM snapshot saved");
                return;
            }

            preBattleSnapshot = BgmSnapshot.Capture(manager);
            inBattle = true;
            battleMode = BattleAudioMode.None;
            Log("Pre-battle BGM snapshot saved: " + preBattleSnapshot.Describe());
        }
        catch (Exception ex)
        {
            LogWarning("Failed to save pre-battle BGM snapshot: " + ex);
        }
    }

    private static void OnAfterFightInit(ModHookContext context)
    {
        try
        {
            Log("FightInit.Init after: deciding battle BGM mode, loadState=" + loadState);

            if (loadState == ReplacementLoadState.NotStarted)
            {
                StartLoad("battle entered while load was not started");
            }

            if (loadState == ReplacementLoadState.Ready && replacementClip != null)
            {
                ReplaceCurrentBattleBgm();
                battleMode = BattleAudioMode.Replaced;
                Log("Battle mode selected: Replaced");
                return;
            }

            if (loadState == ReplacementLoadState.Loading || loadState == ReplacementLoadState.NotStarted)
            {
                SilenceCurrentBattleBgm();
                battleMode = BattleAudioMode.SilentBecauseLoading;
                Log("Battle mode selected: SilentBecauseLoading. This battle will not switch after load completes");
                return;
            }

            battleMode = BattleAudioMode.OriginalBecauseFailedOrMissing;
            Log("Battle mode selected: OriginalBecauseFailedOrMissing. Original game battle BGM remains for this battle");
        }
        catch (Exception ex)
        {
            battleMode = BattleAudioMode.OriginalBecauseFailedOrMissing;
            LogWarning("Failed to apply battle BGM decision; original game battle BGM remains. Error: " + ex);
        }
    }

    private static void OnFightEnded(ModHookContext context)
    {
        try
        {
            var hookTargetName = context.Target == null ? "<null>" : context.Target.GetType().Name;
            Log("Fight end detected by hook target: " + hookTargetName + ", current battleMode=" + battleMode);

            var manager = AudioManager.Instance;
            if (manager == null)
            {
                LogWarning("AudioManager.Instance is null on fight end; cannot restore BGM");
                ResetBattleState();
                return;
            }

            if (preBattleSnapshot == null)
            {
                StopMainBgm(manager, "no pre-battle snapshot exists");
                ResetBattleState();
                return;
            }

            preBattleSnapshot.Restore(manager);
            Log("Pre-battle BGM restored: " + preBattleSnapshot.Describe());
            ResetBattleState();
        }
        catch (Exception ex)
        {
            LogWarning("Failed to restore pre-battle BGM: " + ex);
            ResetBattleState();
        }
    }

    private static void ReplaceCurrentBattleBgm()
    {
        var manager = AudioManager.Instance;
        if (manager == null)
        {
            LogWarning("Cannot replace battle BGM because AudioManager.Instance is null");
            return;
        }

        var source = manager.bgmSource;
        var originalClipName = source.clip == null ? "<null>" : source.clip.name;
        source.Stop();
        source.clip = replacementClip;
        source.loop = true;
        source.time = 0f;
        source.Play();

        Log("Current battle BGM replaced: " + originalClipName + " -> " + replacementClip!.name
            + ", length=" + replacementClip.length.ToString("0.000") + "s");
    }

    private static void SilenceCurrentBattleBgm()
    {
        var manager = AudioManager.Instance;
        if (manager == null)
        {
            LogWarning("Cannot silence battle BGM because AudioManager.Instance is null");
            return;
        }

        var source = manager.bgmSource;
        var originalClipName = source.clip == null ? "<null>" : source.clip.name;
        source.Stop();
        source.clip = EnsureSilentClip();
        source.loop = true;
        source.time = 0f;
        source.Play();

        Log("Current battle BGM silenced while replacement is loading. Original battle clip was: " + originalClipName);
    }

    private static AudioClip EnsureSilentClip()
    {
        if (silentClip != null)
        {
            return silentClip;
        }

        silentClip = AudioClip.Create("BackgroundAudioReplaceExp.SilentBattleBgm", SilentSampleRate, 1, SilentSampleRate, false);
        Log("Silent placeholder AudioClip created");
        return silentClip;
    }

    private static void StartLoad(string reason)
    {
        EnsureRunner();
        loadGeneration++;
        var generation = loadGeneration;
        cachedSignature = ReadCurrentSignature();

        if (!cachedSignature.Exists)
        {
            replacementClip = null;
            loadState = ReplacementLoadState.Missing;
            LogWarning("BGM file is missing. reason=" + reason + ", path=" + audioPath);
            return;
        }

        loadState = ReplacementLoadState.Loading;
        replacementClip = null;
        Log("BGM load started. reason=" + reason + ", generation=" + generation + ", signature=" + cachedSignature);
        runner!.LoadAudio(audioPath, generation, OnLoadCompleted);
    }

    private static void OnLoadCompleted(int generation, AudioClip? loadedClip, string? error)
    {
        if (generation != loadGeneration)
        {
            Log("Ignored stale load result. generation=" + generation + ", activeGeneration=" + loadGeneration);
            return;
        }

        cachedSignature = ReadCurrentSignature();

        if (loadedClip == null)
        {
            replacementClip = null;
            loadState = cachedSignature.Exists ? ReplacementLoadState.Failed : ReplacementLoadState.Missing;
            LogWarning("BGM load failed. generation=" + generation + ", state=" + loadState
                + ", signature=" + cachedSignature + ", error=" + (error ?? "<none>"));
            return;
        }

        loadedClip.name = Path.GetFileNameWithoutExtension(audioPath);
        replacementClip = loadedClip;
        loadState = ReplacementLoadState.Ready;
        Log("BGM load succeeded. generation=" + generation
            + ", signature=" + cachedSignature
            + ", clip=" + loadedClip.name
            + ", length=" + loadedClip.length.ToString("0.000") + "s"
            + ", frequency=" + loadedClip.frequency
            + ", channels=" + loadedClip.channels);
    }

    private static void StartFileWatcher()
    {
        EnsureRunner();
        runner!.StartFileWatcher(FileCheckIntervalSeconds, CheckAudioFile);
        Log("File watcher started. intervalSeconds=" + FileCheckIntervalSeconds.ToString("0"));
    }

    private static void CheckAudioFile()
    {
        try
        {
            var currentSignature = ReadCurrentSignature();
            if (currentSignature.Equals(cachedSignature))
            {
                Log("File watcher check: BGM file unchanged. signature=" + currentSignature + ", loadState=" + loadState);
                return;
            }

            Log("File watcher check: BGM file changed. cached=" + cachedSignature + ", current=" + currentSignature);
            StartLoad("file watcher detected resource change");
        }
        catch (Exception ex)
        {
            LogWarning("File watcher check failed: " + ex);
        }
    }

    private static FileSignature ReadCurrentSignature()
    {
        try
        {
            if (!File.Exists(audioPath))
            {
                return FileSignature.Missing;
            }

            var info = new FileInfo(audioPath);
            return new FileSignature(true, info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex)
        {
            LogWarning("Failed to read BGM file signature: " + ex.Message);
            return FileSignature.Missing;
        }
    }

    private static void StopMainBgm(AudioManager manager, string reason)
    {
        var source = manager.bgmSource;
        source.Stop();
        source.clip = null;
        LogWarning("Stopped current BGM because " + reason);
    }

    private static void ResetBattleState()
    {
        Log("Battle state reset. previousMode=" + battleMode);
        inBattle = false;
        battleMode = BattleAudioMode.None;
        preBattleSnapshot = null;
    }

    private static void EnsureRunner()
    {
        if (runner != null)
        {
            return;
        }

        var gameObject = new GameObject("BackgroundAudioReplaceExp.Runtime");
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<LoaderRunner>();
        Log("Runtime runner created");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            Log("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            LogWarning("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            Log("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            LogWarning("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static T? GetField<T>(AudioManager manager, string fieldName)
    {
        var field = typeof(AudioManager).GetField(fieldName, InstancePrivate);
        if (field == null)
        {
            LogWarning("AudioManager private field not found: " + fieldName);
            return default;
        }

        var value = field.GetValue(manager);
        return value is T typed ? typed : default;
    }

    private static void SetField(AudioManager manager, string fieldName, object? value)
    {
        var field = typeof(AudioManager).GetField(fieldName, InstancePrivate);
        if (field == null)
        {
            LogWarning("AudioManager private field not found while restoring: " + fieldName);
            return;
        }

        field.SetValue(manager, value);
    }

    private static void Log(string message)
    {
        Debug.Log(LogPrefix + message);
    }

    private static void LogWarning(string message)
    {
        Debug.LogWarning(LogPrefix + message);
    }

    private enum ReplacementLoadState
    {
        NotStarted,
        Loading,
        Ready,
        Missing,
        Failed
    }

    private enum BattleAudioMode
    {
        None,
        Replaced,
        SilentBecauseLoading,
        OriginalBecauseFailedOrMissing
    }

    private readonly struct FileSignature : IEquatable<FileSignature>
    {
        public static readonly FileSignature Missing = new(false, -1L, -1L);

        public FileSignature(bool exists, long length, long lastWriteTicks)
        {
            Exists = exists;
            Length = length;
            LastWriteTicks = lastWriteTicks;
        }

        public bool Exists { get; }

        private long Length { get; }

        private long LastWriteTicks { get; }

        public bool Equals(FileSignature other)
        {
            return Exists == other.Exists && Length == other.Length && LastWriteTicks == other.LastWriteTicks;
        }

        public override bool Equals(object? obj)
        {
            return obj is FileSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Exists ? 17 : 23;
                hash = hash * 31 + Length.GetHashCode();
                hash = hash * 31 + LastWriteTicks.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return Exists
                ? "exists,length=" + Length + ",lastWriteUtcTicks=" + LastWriteTicks
                : "missing";
        }
    }

    private sealed class BgmSnapshot
    {
        private readonly SourceSnapshot? mainSource;
        private readonly SourceSnapshot? backgroundSource;
        private readonly List<AudioClip> playList;
        private readonly List<AudioClip> backgroundPlayList;
        private readonly int bgmIndex;
        private readonly int backgroundBgmIndex;
        private readonly string nowBgmName;
        private readonly string backgroundBgmName;
        private readonly bool mainBgmMuted;
        private readonly bool backgroundBgmMuted;

        private BgmSnapshot(
            SourceSnapshot? mainSource,
            SourceSnapshot? backgroundSource,
            List<AudioClip> playList,
            List<AudioClip> backgroundPlayList,
            int bgmIndex,
            int backgroundBgmIndex,
            string nowBgmName,
            string backgroundBgmName,
            bool mainBgmMuted,
            bool backgroundBgmMuted)
        {
            this.mainSource = mainSource;
            this.backgroundSource = backgroundSource;
            this.playList = playList;
            this.backgroundPlayList = backgroundPlayList;
            this.bgmIndex = bgmIndex;
            this.backgroundBgmIndex = backgroundBgmIndex;
            this.nowBgmName = nowBgmName;
            this.backgroundBgmName = backgroundBgmName;
            this.mainBgmMuted = mainBgmMuted;
            this.backgroundBgmMuted = backgroundBgmMuted;
        }

        public static BgmSnapshot Capture(AudioManager manager)
        {
            var mainAudioSource = GetField<AudioSource>(manager, "_bgmSource");
            var backgroundAudioSource = GetField<AudioSource>(manager, "_backgroundBgmSource");
            var currentPlayList = GetField<List<AudioClip>>(manager, "PlayList") ?? new List<AudioClip>();
            var currentBackgroundPlayList = GetField<List<AudioClip>>(manager, "backgroundPlayList") ?? new List<AudioClip>();

            return new BgmSnapshot(
                SourceSnapshot.Capture(mainAudioSource),
                SourceSnapshot.Capture(backgroundAudioSource),
                new List<AudioClip>(currentPlayList),
                new List<AudioClip>(currentBackgroundPlayList),
                GetField<int>(manager, "bgmIndex"),
                GetField<int>(manager, "backgroundBgmIndex"),
                manager.NowBGMName ?? "",
                GetField<string>(manager, "backgroundBgmName") ?? "",
                GetField<bool>(manager, "mainBgmMuted"),
                GetField<bool>(manager, "backgroundBgmMuted"));
        }

        public void Restore(AudioManager manager)
        {
            SetField(manager, "PlayList", new List<AudioClip>(playList));
            SetField(manager, "backgroundPlayList", new List<AudioClip>(backgroundPlayList));
            SetField(manager, "bgmIndex", bgmIndex);
            SetField(manager, "backgroundBgmIndex", backgroundBgmIndex);
            SetField(manager, "backgroundBgmName", backgroundBgmName);
            SetField(manager, "mainBgmMuted", mainBgmMuted);
            SetField(manager, "backgroundBgmMuted", backgroundBgmMuted);
            manager.NowBGMName = nowBgmName;

            var mainAudioSource = GetField<AudioSource>(manager, "_bgmSource") ?? manager.bgmSource;
            var backgroundAudioSource = GetField<AudioSource>(manager, "_backgroundBgmSource");

            if (mainSource != null)
            {
                mainSource.Restore(mainAudioSource);
            }
            else
            {
                mainAudioSource.Stop();
                mainAudioSource.clip = null;
                mainAudioSource.loop = false;
            }

            if (backgroundSource != null && backgroundAudioSource != null)
            {
                backgroundSource.Restore(backgroundAudioSource);
            }
            else if (backgroundAudioSource != null)
            {
                backgroundAudioSource.Stop();
                backgroundAudioSource.clip = null;
            }
        }

        public string Describe()
        {
            return "NowBGMName=" + nowBgmName
                + ", main=" + (mainSource == null ? "<none>" : mainSource.Describe())
                + ", background=" + (backgroundSource == null ? "<none>" : backgroundSource.Describe())
                + ", playListCount=" + playList.Count
                + ", backgroundPlayListCount=" + backgroundPlayList.Count;
        }
    }

    private sealed class SourceSnapshot
    {
        private readonly AudioClip? clip;
        private readonly float time;
        private readonly bool wasPlaying;
        private readonly bool loop;
        private readonly float volume;
        private readonly bool mute;

        private SourceSnapshot(AudioClip? clip, float time, bool wasPlaying, bool loop, float volume, bool mute)
        {
            this.clip = clip;
            this.time = time;
            this.wasPlaying = wasPlaying;
            this.loop = loop;
            this.volume = volume;
            this.mute = mute;
        }

        public static SourceSnapshot? Capture(AudioSource? source)
        {
            if (source == null)
            {
                return null;
            }

            return new SourceSnapshot(source.clip, SafeReadTime(source), source.isPlaying, source.loop, source.volume, source.mute);
        }

        public void Restore(AudioSource source)
        {
            source.Stop();
            source.clip = clip;
            source.loop = loop;
            source.volume = volume;
            source.mute = mute;

            if (clip != null)
            {
                SafeSetTime(source, time);
                if (wasPlaying)
                {
                    source.Play();
                }
            }
        }

        public string Describe()
        {
            return "clip=" + (clip == null ? "<null>" : clip.name)
                + ", time=" + time.ToString("0.000")
                + ", wasPlaying=" + wasPlaying
                + ", loop=" + loop;
        }

        private static float SafeReadTime(AudioSource source)
        {
            try
            {
                return source.time;
            }
            catch
            {
                return 0f;
            }
        }

        private static void SafeSetTime(AudioSource source, float value)
        {
            try
            {
                if (source.clip == null)
                {
                    return;
                }

                source.time = Mathf.Clamp(value, 0f, Mathf.Max(0f, source.clip.length - 0.01f));
            }
            catch (Exception ex)
            {
                LogWarning("Failed to restore AudioSource time: " + ex.Message);
            }
        }
    }

    private sealed class LoaderRunner : MonoBehaviour
    {
        private Coroutine? watcherCoroutine;

        public void LoadAudio(string path, int generation, Action<int, AudioClip?, string?> onCompleted)
        {
            StartCoroutine(LoadAudioCoroutine(path, generation, onCompleted));
        }

        public void StartFileWatcher(float intervalSeconds, Action onCheck)
        {
            if (watcherCoroutine != null)
            {
                StopCoroutine(watcherCoroutine);
            }

            watcherCoroutine = StartCoroutine(FileWatcherCoroutine(intervalSeconds, onCheck));
        }

        private static IEnumerator LoadAudioCoroutine(string path, int generation, Action<int, AudioClip?, string?> onCompleted)
        {
            var uri = new Uri(path).AbsoluteUri;
            Log("UnityWebRequest audio load begin. generation=" + generation + ", uri=" + uri);

            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onCompleted(generation, null, "result=" + request.result + ", error=" + request.error);
                yield break;
            }

            AudioClip? clip = null;
            string? error = null;
            try
            {
                clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    error = "DownloadHandlerAudioClip.GetContent returned null";
                }
            }
            catch (Exception ex)
            {
                error = ex.ToString();
            }

            onCompleted(generation, clip, error);
        }

        private static IEnumerator FileWatcherCoroutine(float intervalSeconds, Action onCheck)
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(intervalSeconds);
                onCheck();
            }
        }
    }
}
