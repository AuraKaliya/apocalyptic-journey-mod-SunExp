using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Network.Command;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using GameUIManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.SkillCg.Arbiter;

public static class SkillCgArbiterRuntime
{
    private const string GlobalObjectName = "AuraToolsExp.CgArbiter.Global";
    private const string ComponentFullName = "AuraToolsExp.Dll.Features.SkillCg.Arbiter.SkillCgArbiterRuntime+SkillCgArbiterComponent";
    private const string LegacyResourceDirectoryName = "ModResource";
    private const float SlideDurationSeconds = 2.0f;
    private const float SlideImageHeightRatio = 0.85f;
    private const float SlideStartXRatio = 1.18f;
    private const float SlideEndXRatio = -0.18f;
    private const float SlideCenterSlowStrength = 0.65f;
    private const float AlphaFadeInStartXRatio = 1.05f;
    private const float AlphaFadeInEndXRatio = 0.82f;
    private const float AlphaFadeOutStartXRatio = 0.18f;
    private const float AlphaFadeOutEndXRatio = -0.05f;
    public const int CurrentProtocolVersion = 1;
    public const int MinimumSupportedProtocolVersion = 1;
    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> ModDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DataDirectories = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig? modConfig, string ownerModId, SkillCgArbiterOptions? options = null)
    {
        if (modConfig != null && !string.IsNullOrWhiteSpace(modConfig.DirectoryName))
        {
            ModDirectories[ownerModId] = SafeFullPath(modConfig.DirectoryName);
        }

        RegisterOwnerDataDirectory(ownerModId);

        if (modConfig != null && !string.IsNullOrWhiteSpace(modConfig.DirectoryName))
        {
            RegisterDerivedDataDirectory(ownerModId, modConfig.DirectoryName);
        }

        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "Configure", options ?? new SkillCgArbiterOptions());
    }

    public static void RegisterProvider(ModConfig modConfig, string ownerModId, object provider)
    {
        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "RegisterProvider", provider);
    }

    public static void Trigger(object ownerToken, string ownerModId, SkillCgTriggerContext context)
    {
        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "Trigger", context);
    }

    public static void RequestCg(string ownerModId, SkillCgRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerModId))
        {
            request.OwnerModId = ownerModId;
        }

        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "RequestCg", request);
    }

    public static string ResolveImagePath(string ownerModId, string imageResource, string fallbackPath = "")
    {
        var resource = imageResource?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(resource))
        {
            return fallbackPath?.Trim() ?? "";
        }

        if (Path.IsPathRooted(resource))
        {
            return resource;
        }

        var normalizedResource = NormalizeRelativeResourcePath(resource);
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (DataDirectories.TryGetValue(ownerModId, out var dataDirectory))
        {
            AddCandidate(candidates, seen, dataDirectory, normalizedResource);
        }

        if (ModDirectories.TryGetValue(ownerModId, out var modDirectory))
        {
            AddCandidate(candidates, seen, modDirectory, normalizedResource);
            AddLegacyResourceCandidate(candidates, seen, modDirectory, normalizedResource);
        }

        AddCandidate(candidates, seen, fallbackPath?.Trim() ?? "");

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates.Count > 0 ? candidates[0] : normalizedResource;
    }

    private static void RegisterOwnerDataDirectory(string ownerModId)
    {
        if (!string.Equals(ownerModId, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var dataDirectory = SafeFullPath(AuraToolsConfigService.DataRootDirectory);
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            DataDirectories[ownerModId] = dataDirectory;
        }
    }

    private static void RegisterDerivedDataDirectory(string ownerModId, string modDirectory)
    {
        if (DataDirectories.ContainsKey(ownerModId))
        {
            return;
        }

        var packageDirectory = SafeFullPath(modDirectory);
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            return;
        }

        var current = new DirectoryInfo(packageDirectory);
        while (current != null)
        {
            if (string.Equals(current.Name, "Mods", StringComparison.OrdinalIgnoreCase))
            {
                var parent = current.Parent;
                if (parent != null)
                {
                    DataDirectories[ownerModId] = Path.Combine(parent.FullName, AuraToolsIds.DataRootDirectoryName, ownerModId);
                }

                return;
            }

            current = current.Parent;
        }
    }

    private static string NormalizeRelativeResourcePath(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static void AddLegacyResourceCandidate(List<string> candidates, HashSet<string> seen, string rootDirectory, string normalizedResource)
    {
        if (!StartsWithSegment(normalizedResource, AuraToolsIds.ResourceDirectoryName))
        {
            return;
        }

        var rest = normalizedResource.Substring(AuraToolsIds.ResourceDirectoryName.Length).TrimStart('/', '\\');
        var legacyResource = string.IsNullOrWhiteSpace(rest)
            ? LegacyResourceDirectoryName
            : LegacyResourceDirectoryName + "/" + rest;
        AddCandidate(candidates, seen, rootDirectory, legacyResource);
    }

    private static bool StartsWithSegment(string value, string segment)
    {
        return value.Equals(segment, StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidate(List<string> candidates, HashSet<string> seen, string rootDirectory, string relativeResource)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(relativeResource))
        {
            return;
        }

        AddCandidate(candidates, seen, Path.Combine(rootDirectory, relativeResource.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void AddCandidate(List<string> candidates, HashSet<string> seen, string path)
    {
        var candidate = SafeFullPath(path);
        if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
        {
            candidates.Add(candidate);
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
        }
        catch
        {
            return "";
        }
    }

    public static void Clear(string ownerModId, string reason)
    {
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject == null)
        {
            return;
        }

        var existing = FindArbiterComponent(gameObject);
        Invoke(existing, "ClearQueue", reason);
    }

    private static object EnsureArbiter(string ownerModId)
    {
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            var existing = FindArbiterComponent(gameObject);
            if (existing != null)
            {
                if (ReuseLogOwners.Add(ownerModId))
                {
                    SkillCgExpLog.InfoOnce("reuse-arbiter:" + ownerModId, "Reusing global CG arbiter for " + ownerModId + ".");
                }

                return existing;
            }
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var component = gameObject.AddComponent<SkillCgArbiterComponent>();
        SkillCgExpLog.InfoOnce("create-arbiter", "Created global CG arbiter. owner=" + ownerModId);
        return component;
    }

    private static object? FindArbiterComponent(GameObject gameObject)
    {
        foreach (var component in gameObject.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().FullName == ComponentFullName)
            {
                return component;
            }
        }

        return null;
    }

    private static void Invoke(object? target, string methodName, object? argument)
    {
        if (target == null)
        {
            return;
        }

        target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(target, new[] { argument });
    }

    public sealed class SkillCgArbiterComponent : MonoBehaviour
    {
        private readonly List<ProviderHandle> providers = new();
        private readonly List<QueuedRequest> queue = new();
        private readonly Dictionary<string, float> recentKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> spriteCache = new(StringComparer.OrdinalIgnoreCase);
        private SkillCgArbiterOptions options = new();
        private bool playing;
        private long enqueueSequence;
        private GameObject? overlayRoot;
        private CanvasGroup? overlayGroup;
        private Image? overlayImage;
        private int playGeneration;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => SkillCgArbiterRuntime.MinimumSupportedProtocolVersion;

        public void Configure(object? value)
        {
            if (value is not SkillCgArbiterOptions typed)
            {
                return;
            }

            var normalized = typed.Normalized();
            options = new SkillCgArbiterOptions
            {
                MaxQueueLength = Mathf.Max(options.MaxQueueLength, normalized.MaxQueueLength),
                MaxRequestAgeSeconds = Mathf.Max(options.MaxRequestAgeSeconds, normalized.MaxRequestAgeSeconds),
                DuplicateWindowSeconds = Mathf.Min(options.DuplicateWindowSeconds, normalized.DuplicateWindowSeconds)
            }.Normalized();
            SkillCgExpLog.InfoOnce(
                "arbiter-configured",
                "CG queue configured. maxQueue=" + options.MaxQueueLength
                + ", maxAge=" + options.MaxRequestAgeSeconds.ToString("0.##") + "s"
                + ", duplicateWindow=" + options.DuplicateWindowSeconds.ToString("0.##") + "s");
        }

        public void RegisterProvider(object? provider)
        {
            if (provider == null)
            {
                SkillCgExpLog.WarnOnce("provider-null", "Provider registration skipped: provider is null.");
                return;
            }

            try
            {
                var handle = new ProviderHandle(provider);
                if (string.IsNullOrWhiteSpace(handle.ProviderId))
                {
                    SkillCgExpLog.WarnOnce("provider-empty-id:" + provider.GetType().FullName, "Provider registration skipped: ProviderId is empty.");
                    return;
                }

                providers.RemoveAll(item => string.Equals(item.ProviderId, handle.ProviderId, StringComparison.OrdinalIgnoreCase));
                providers.Add(handle);
                providers.Sort((a, b) =>
                {
                    var priority = b.Priority.CompareTo(a.Priority);
                    return priority != 0 ? priority : string.Compare(a.ProviderId, b.ProviderId, StringComparison.OrdinalIgnoreCase);
                });
                SkillCgExpLog.InfoOnce("provider:" + handle.ProviderId, "CG provider registered: " + handle.Describe());
            }
            catch (Exception ex)
            {
                SkillCgExpLog.WarnOnce("provider-failed:" + provider.GetType().FullName, "Provider registration failed: " + ex.Message);
            }
        }

        public void Trigger(object? value)
        {
            if (value is not SkillCgTriggerContext context)
            {
                return;
            }

            var batch = new List<SkillCgRequest>();
            foreach (var provider in providers)
            {
                provider.AppendRequests(context, batch);
            }

            if (batch.Count == 0)
            {
                return;
            }

            batch.Sort((a, b) =>
            {
                var actionCompare = a.ActionSequence.CompareTo(b.ActionSequence);
                if (actionCompare != 0)
                {
                    return actionCompare;
                }

                var priorityCompare = b.Priority.CompareTo(a.Priority);
                return priorityCompare != 0
                    ? priorityCompare
                    : string.Compare(a.ProviderId, b.ProviderId, StringComparison.OrdinalIgnoreCase);
            });

            var accepted = 0;
            foreach (var request in batch)
            {
                if (TryEnqueue(request))
                {
                    accepted++;
                    SyncRemote(request);
                }
            }

            if (accepted > 0 && !playing)
            {
                StartCoroutine(PlayQueue(playGeneration));
            }
        }

        public void RequestCg(object? value)
        {
            if (value is not SkillCgRequest request)
            {
                return;
            }

            if (TryEnqueue(request) && !playing)
            {
                StartCoroutine(PlayQueue(playGeneration));
            }
        }

        public void ClearQueue(object? reason)
        {
            playGeneration++;
            queue.Clear();
            recentKeys.Clear();
            playing = false;
            if (overlayGroup != null)
            {
                overlayGroup.alpha = 0f;
            }

            if (overlayRoot != null)
            {
                overlayRoot.SetActive(false);
            }

            SkillCgExpLog.DebugLog("CG queue cleared: " + (reason as string ?? "<none>"));
        }

        private bool TryEnqueue(SkillCgRequest request)
        {
            request.Normalize();
            if (string.IsNullOrWhiteSpace(request.ImagePath))
            {
                SkillCgExpLog.WarnOnce("empty-image:" + request.ProviderId, "CG request skipped: image path is empty. provider=" + request.ProviderId);
                return false;
            }

            PruneRecentKeys();
            var duplicateKey = request.DuplicateKey;
            if (recentKeys.TryGetValue(duplicateKey, out var lastTime)
                && Time.unscaledTime - lastTime <= options.DuplicateWindowSeconds)
            {
                SkillCgExpLog.DebugLog("Duplicate CG request skipped: " + duplicateKey);
                return false;
            }

            recentKeys[duplicateKey] = Time.unscaledTime;
            queue.Add(new QueuedRequest(request, ++enqueueSequence));
            if (queue.Count > options.MaxQueueLength)
            {
                queue.Sort(QueuedRequest.CompareForQueue);
                var dropCount = queue.Count - options.MaxQueueLength;
                queue.RemoveRange(0, dropCount);
                SkillCgExpLog.WarnOnce("queue-full", "CG queue is full; oldest pending CG requests will be dropped. max=" + options.MaxQueueLength);
            }

            queue.Sort(QueuedRequest.CompareForQueue);
            SkillCgExpLog.DebugLog("CG queued: provider=" + request.ProviderId + ", card=" + request.CardId + ", queue=" + queue.Count);
            return true;
        }

        private void SyncRemote(SkillCgRequest request)
        {
            if (request.DisableSync || request.IsRemote)
            {
                return;
            }

            var playerManager = PlayerManager.Instance;
            if (playerManager == null)
            {
                return;
            }

            try
            {
                playerManager.SendRpcCommandExcludeOwner(new RpcSkillCgEvent(request));
            }
            catch (Exception ex)
            {
                SkillCgExpLog.WarnOnce("remote-sync-failed", "Remote CG sync failed once; later errors are suppressed. error=" + ex.Message);
                SkillCgExpLog.DebugLog("Remote CG sync exception: " + ex);
            }
        }

        private IEnumerator PlayQueue(int generation)
        {
            playing = true;
            while (generation == playGeneration && queue.Count > 0)
            {
                var item = queue[0];
                queue.RemoveAt(0);
                if (Time.unscaledTime - item.Request.CreatedAt > options.MaxRequestAgeSeconds)
                {
                    SkillCgExpLog.WarnOnce("request-stale", "Stale CG requests are being skipped. maxAge=" + options.MaxRequestAgeSeconds.ToString("0.##") + "s");
                    continue;
                }

                yield return PlayRequest(item.Request, generation);
            }

            if (generation == playGeneration)
            {
                playing = false;
            }
        }

        private IEnumerator PlayRequest(SkillCgRequest request, int generation)
        {
            var spriteReady = false;
            Sprite? sprite = null;
            yield return LoadSprite(request.ImagePath, result =>
            {
                sprite = result;
                spriteReady = true;
            });

            if (!spriteReady || sprite == null)
            {
                yield break;
            }

            if (generation != playGeneration)
            {
                yield break;
            }

            if (!EnsureOverlay())
            {
                yield break;
            }

            overlayRoot!.SetActive(true);
            overlayRoot.transform.SetAsLastSibling();
            overlayImage!.sprite = sprite;
            overlayImage.enabled = true;
            overlayGroup!.alpha = 0f;

            SkillCgExpLog.DebugLog(
                "CG play slide: provider=" + request.ProviderId
                + ", card=" + request.CardId
                + ", image=" + Path.GetFileName(request.ImagePath)
                + ", duration=" + SlideDurationSeconds.ToString("0.##") + "s");
            yield return SlideRightToLeft(sprite, generation);

            if (generation != playGeneration)
            {
                yield break;
            }

            overlayImage.sprite = null;
            overlayRoot.SetActive(false);
        }

        private IEnumerator SlideRightToLeft(Sprite sprite, int generation)
        {
            if (overlayRoot == null || overlayGroup == null || overlayImage == null)
            {
                yield break;
            }

            var imageRect = overlayImage.rectTransform;
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);

            var elapsed = 0f;
            while (generation == playGeneration && elapsed < SlideDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / SlideDurationSeconds);
                var viewport = GetOverlayViewportSize();
                var xRatio = EvaluateSlideXRatio(progress);

                imageRect.sizeDelta = CalculateImageSize(sprite, viewport);
                imageRect.anchoredPosition = new Vector2((xRatio - 0.5f) * viewport.x, 0f);
                overlayGroup.alpha = EvaluateSlideAlpha(xRatio);
                yield return null;
            }

            if (generation == playGeneration)
            {
                var viewport = GetOverlayViewportSize();
                imageRect.sizeDelta = CalculateImageSize(sprite, viewport);
                imageRect.anchoredPosition = new Vector2((SlideEndXRatio - 0.5f) * viewport.x, 0f);
                overlayGroup.alpha = 0f;
            }
        }

        private Vector2 GetOverlayViewportSize()
        {
            if (overlayRoot != null)
            {
                var rect = overlayRoot.GetComponent<RectTransform>().rect;
                if (rect.width > 1f && rect.height > 1f)
                {
                    return new Vector2(rect.width, rect.height);
                }
            }

            return new Vector2(Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
        }

        private static Vector2 CalculateImageSize(Sprite sprite, Vector2 viewport)
        {
            var spriteRect = sprite.rect;
            var aspect = spriteRect.height <= 0f ? 1f : spriteRect.width / spriteRect.height;
            var height = Mathf.Max(1f, viewport.y * SlideImageHeightRatio);
            return new Vector2(height * aspect, height);
        }

        private static float EvaluateSlideXRatio(float progress)
        {
            var t = Mathf.Clamp01(progress);
            var remappedProgress = Mathf.Clamp01(t + SlideCenterSlowStrength * Mathf.Sin(2f * Mathf.PI * t) / (2f * Mathf.PI));
            return Mathf.Lerp(SlideStartXRatio, SlideEndXRatio, remappedProgress);
        }

        private static float EvaluateSlideAlpha(float xRatio)
        {
            if (xRatio >= AlphaFadeInStartXRatio || xRatio <= AlphaFadeOutEndXRatio)
            {
                return 0f;
            }

            if (xRatio > AlphaFadeInEndXRatio)
            {
                return Mathf.InverseLerp(AlphaFadeInStartXRatio, AlphaFadeInEndXRatio, xRatio);
            }

            if (xRatio < AlphaFadeOutStartXRatio)
            {
                return Mathf.InverseLerp(AlphaFadeOutEndXRatio, AlphaFadeOutStartXRatio, xRatio);
            }

            return 1f;
        }

        private IEnumerator LoadSprite(string path, Action<Sprite?> onLoaded)
        {
            if (spriteCache.TryGetValue(path, out var cached) && cached != null)
            {
                onLoaded(cached);
                yield break;
            }

            if (!File.Exists(path))
            {
                SkillCgExpLog.WarnOnce("missing-image:" + path, "CG image not found: " + path);
                onLoaded(null);
                yield break;
            }

            using var request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                SkillCgExpLog.WarnOnce("image-load-failed:" + path, "CG image failed to load: " + Path.GetFileName(path) + ", error=" + request.error);
                onLoaded(null);
                yield break;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null)
            {
                SkillCgExpLog.WarnOnce("image-empty:" + path, "CG image load returned empty texture: " + path);
                onLoaded(null);
                yield break;
            }

            texture.name = Path.GetFileNameWithoutExtension(path);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            spriteCache[path] = sprite;
            SkillCgExpLog.InfoOnce("image-loaded:" + path, "CG image loaded: " + Path.GetFileName(path) + " (" + texture.width + "x" + texture.height + ")");
            onLoaded(sprite);
        }

        private bool EnsureOverlay()
        {
            var manager = GameUIManager.Instance;
            var parent = manager?.upperCanvasTf ?? manager?.canvasTf;
            if (parent == null)
            {
                SkillCgExpLog.WarnOnce("ui-parent-missing", "CG overlay skipped: UI canvas is not ready.");
                return false;
            }

            if (overlayRoot != null && overlayRoot.transform.parent == parent)
            {
                return true;
            }

            if (overlayRoot != null)
            {
                Destroy(overlayRoot);
            }

            overlayRoot = new GameObject("AuraToolsExp.SkillCG.OverlayRoot", typeof(RectTransform), typeof(CanvasGroup));
            overlayRoot.transform.SetParent(parent, false);
            var rect = overlayRoot.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayGroup = overlayRoot.GetComponent<CanvasGroup>();
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            var imageObject = new GameObject("AuraToolsExp.SkillCG.Image", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(overlayRoot.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            overlayImage = imageObject.GetComponent<Image>();
            overlayImage.color = Color.white;
            overlayImage.preserveAspect = true;
            overlayImage.raycastTarget = false;
            overlayRoot.SetActive(false);
            SkillCgExpLog.InfoOnce("overlay-created", "CG overlay created under " + parent.name + ".");
            return true;
        }

        private IEnumerator Fade(float from, float to, float seconds, int generation)
        {
            if (overlayGroup == null)
            {
                yield break;
            }

            if (seconds <= 0f)
            {
                overlayGroup.alpha = to;
                yield break;
            }

            var elapsed = 0f;
            while (generation == playGeneration && elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                overlayGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            if (generation == playGeneration)
            {
                overlayGroup.alpha = to;
            }
        }

        private IEnumerator Wait(float seconds, int generation)
        {
            var elapsed = 0f;
            while (generation == playGeneration && elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void PruneRecentKeys()
        {
            var now = Time.unscaledTime;
            var expired = recentKeys
                .Where(item => now - item.Value > options.DuplicateWindowSeconds)
                .Select(item => item.Key)
                .ToList();
            foreach (var key in expired)
            {
                recentKeys.Remove(key);
            }
        }
    }

    private sealed class ProviderHandle
    {
        private readonly object provider;
        private readonly Type providerType;

        public ProviderHandle(object provider)
        {
            this.provider = provider;
            providerType = provider.GetType();
            ProviderId = ReadString("ProviderId", providerType.FullName ?? "unknown");
            OwnerModId = ReadString("OwnerModId", "");
            Priority = ReadInt("Priority", 0);
        }

        public string ProviderId { get; }

        public string OwnerModId { get; }

        public int Priority { get; }

        public void AppendRequests(SkillCgTriggerContext context, List<SkillCgRequest> output)
        {
            try
            {
                var method = providerType.GetMethod("BuildRequests", BindingFlags.Instance | BindingFlags.Public);
                var value = method?.Invoke(provider, new object[] { context });
                if (value is not IEnumerable items)
                {
                    return;
                }

                foreach (var item in items)
                {
                    var request = SkillCgRequest.FromObject(item, ProviderId, OwnerModId, Priority, context);
                    if (request != null)
                    {
                        output.Add(request);
                    }
                }
            }
            catch (Exception ex)
            {
                SkillCgExpLog.WarnOnce("provider-build-failed:" + ProviderId, "Provider BuildRequests failed once: " + ProviderId + " -> " + ex.Message);
                SkillCgExpLog.DebugLog("Provider BuildRequests exception: " + ex);
            }
        }

        public string Describe()
        {
            return "providerId=" + ProviderId + ", owner=" + OwnerModId + ", priority=" + Priority;
        }

        private string ReadString(string name, string fallback)
        {
            try
            {
                return providerType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider) as string ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private int ReadInt(string name, int fallback)
        {
            try
            {
                var value = providerType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider);
                return value is int typed ? typed : fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}

public sealed class SkillCgArbiterOptions
{
    public int MaxQueueLength { get; set; } = 8;

    public float MaxRequestAgeSeconds { get; set; } = 6f;

    public float DuplicateWindowSeconds { get; set; } = 0.2f;

    public SkillCgArbiterOptions Normalized()
    {
        return new SkillCgArbiterOptions
        {
            MaxQueueLength = Mathf.Clamp(MaxQueueLength, 1, 30),
            MaxRequestAgeSeconds = Mathf.Clamp(MaxRequestAgeSeconds, 0.5f, 30f),
            DuplicateWindowSeconds = Mathf.Clamp(DuplicateWindowSeconds, 0.02f, 2f)
        };
    }
}

[Serializable]
public sealed class SkillCgTriggerContext
{
    public long ActionSequence { get; set; }

    public string Action { get; set; } = "";

    public string CardId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public float CreatedAt { get; set; }
}

[Serializable]
public sealed class SkillCgRequest
{
    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public string ImageResource { get; set; } = "";

    public int Priority { get; set; }

    public float FadeIn { get; set; } = 0.35f;

    public float Hold { get; set; } = 1f;

    public float FadeOut { get; set; } = 0.45f;

    public float CreatedAt { get; set; }

    public long ActionSequence { get; set; }

    public bool IsRemote { get; set; }

    public bool DisableSync { get; set; }

    public string DuplicateKey => ProviderId + "|" + OwnerInstanceId + "|" + CardId + "|" + (string.IsNullOrWhiteSpace(ImageResource) ? ImagePath : ImageResource);

    public void Normalize()
    {
        ProviderId = string.IsNullOrWhiteSpace(ProviderId) ? "unknown" : ProviderId.Trim();
        OwnerModId = OwnerModId?.Trim() ?? "";
        CardId = CardId?.Trim() ?? "";
        OwnerInstanceId = OwnerInstanceId?.Trim() ?? "";
        ImagePath = ImagePath?.Trim() ?? "";
        ImageResource = ImageResource?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ImageResource) && !string.IsNullOrWhiteSpace(ImagePath))
        {
            ImageResource = Path.GetFileName(ImagePath);
        }

        FadeIn = Mathf.Max(0f, FadeIn);
        Hold = Mathf.Max(0f, Hold);
        FadeOut = Mathf.Max(0f, FadeOut);
        if (CreatedAt <= 0f)
        {
            CreatedAt = Time.unscaledTime;
        }
    }

    public static SkillCgRequest? FromObject(object? source, string providerId, string ownerModId, int priority, SkillCgTriggerContext context)
    {
        if (source == null)
        {
            return null;
        }

        if (source is SkillCgRequest request)
        {
            return request;
        }

        var type = source.GetType();
        return new SkillCgRequest
        {
            ProviderId = ReadString(type, source, "ProviderId", providerId),
            OwnerModId = ReadString(type, source, "OwnerModId", ownerModId),
            CardId = ReadString(type, source, "CardId", context.CardId),
            OwnerInstanceId = ReadString(type, source, "OwnerInstanceId", context.OwnerInstanceId),
            ImagePath = ReadString(type, source, "ImagePath", ""),
            ImageResource = ReadString(type, source, "ImageResource", ""),
            Priority = ReadInt(type, source, "Priority", priority),
            FadeIn = ReadFloat(type, source, "FadeIn", 0.35f),
            Hold = ReadFloat(type, source, "Hold", 1f),
            FadeOut = ReadFloat(type, source, "FadeOut", 0.45f),
            CreatedAt = ReadFloat(type, source, "CreatedAt", Time.unscaledTime),
            ActionSequence = ReadLong(type, source, "ActionSequence", context.ActionSequence),
            IsRemote = ReadBool(type, source, "IsRemote", false),
            DisableSync = ReadBool(type, source, "DisableSync", false)
        };
    }

    private static string ReadString(Type type, object source, string name, string fallback)
    {
        try
        {
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) as string ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static int ReadInt(Type type, object source, string name, int fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is int typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static float ReadFloat(Type type, object source, string name, float fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is float typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static long ReadLong(Type type, object source, string name, long fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is long typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(Type type, object source, string name, bool fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is bool typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

[Serializable]
public sealed class SkillCgNetworkEvent
{
    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string ImageResource { get; set; } = "";

    public int Priority { get; set; }

    public float FadeIn { get; set; } = 0.35f;

    public float Hold { get; set; } = 1f;

    public float FadeOut { get; set; } = 0.45f;

    public long ActionSequence { get; set; }
}

[Serializable]
public sealed class RpcSkillCgEvent : RpcCommandBase
{
    public RpcSkillCgEvent()
    {
        Event = new SkillCgNetworkEvent();
    }

    public RpcSkillCgEvent(SkillCgRequest request)
    {
        request.Normalize();
        Event = new SkillCgNetworkEvent
        {
            ProviderId = request.ProviderId,
            OwnerModId = request.OwnerModId,
            CardId = request.CardId,
            OwnerInstanceId = request.OwnerInstanceId,
            ImageResource = string.IsNullOrWhiteSpace(request.ImageResource) ? Path.GetFileName(request.ImagePath) : request.ImageResource,
            Priority = request.Priority,
            FadeIn = request.FadeIn,
            Hold = request.Hold,
            FadeOut = request.FadeOut,
            ActionSequence = request.ActionSequence
        };
    }

    public SkillCgNetworkEvent Event { get; set; }

    public override void RpcExecute()
    {
        var ownerModId = string.IsNullOrWhiteSpace(Event.OwnerModId) ? "AuraToolsExp" : Event.OwnerModId;
        SkillCgArbiterRuntime.Initialize(null, ownerModId);
        SkillCgArbiterRuntime.RequestCg(ownerModId, new SkillCgRequest
        {
            ProviderId = Event.ProviderId,
            OwnerModId = ownerModId,
            CardId = Event.CardId,
            OwnerInstanceId = Event.OwnerInstanceId,
            ImageResource = Event.ImageResource,
            ImagePath = SkillCgArbiterRuntime.ResolveImagePath(ownerModId, Event.ImageResource),
            Priority = Event.Priority,
            FadeIn = Event.FadeIn,
            Hold = Event.Hold,
            FadeOut = Event.FadeOut,
            CreatedAt = Time.unscaledTime,
            ActionSequence = Event.ActionSequence,
            IsRemote = true,
            DisableSync = true
        });
    }
}

internal readonly struct QueuedRequest
{
    public QueuedRequest(SkillCgRequest request, long enqueueSequence)
    {
        Request = request;
        EnqueueSequence = enqueueSequence;
    }

    public SkillCgRequest Request { get; }

    private long EnqueueSequence { get; }

    public static int CompareForQueue(QueuedRequest a, QueuedRequest b)
    {
        var actionCompare = a.Request.ActionSequence.CompareTo(b.Request.ActionSequence);
        if (actionCompare != 0)
        {
            return actionCompare;
        }

        var priorityCompare = b.Request.Priority.CompareTo(a.Request.Priority);
        return priorityCompare != 0 ? priorityCompare : a.EnqueueSequence.CompareTo(b.EnqueueSequence);
    }
}

