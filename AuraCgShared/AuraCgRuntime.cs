using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Network.Command;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UiRaycastSafetyShared;
using Witch.Core;
using Witch.Mod;

namespace AuraCg.Shared;

public static class SkillCgArbiterRuntime
{
    public const string SkillCgKind = "skill";
    public const string CardUseCgKind = "cardUse";
    private const string GlobalObjectName = "AuraCg.Global";
    private const string ComponentFullName = "AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent";
    private const float SlideDurationSeconds = 2.0f;
    private const float SlideImageHeightRatio = 0.85f;
    private const float SlideStartXRatio = 1.18f;
    private const float SlideEndXRatio = -0.18f;
    private const float SlideCenterSlowStrength = 0.65f;
    private const float AlphaFadeInStartXRatio = 1.05f;
    private const float AlphaFadeInEndXRatio = 0.82f;
    private const float AlphaFadeOutStartXRatio = 0.18f;
    private const float AlphaFadeOutEndXRatio = -0.05f;
    private const int OverlaySortingOrder = 32760;
    private const string MaskedInvertShaderName = "AuraCg/MaskedInvertFlash";
    private const string LumaKeyShaderName = "AuraCg/LumaKeyUI";
    private const string ScreenBwFlashShaderName = "AuraCg/ScreenBwFlash";
    public const string CurrentBuildId = "aura-cg-shared-2026-06-30-v7";
    public const int CurrentProtocolVersion = 5;
    public const int MinimumSupportedProtocolVersion = 1;
    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityErrorsShown = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DataDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Material> RegisteredMaterials = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, AssetBundle> RegisteredBundles = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig? modConfig, string ownerModId, SkillCgArbiterOptions? options = null)
    {
        if (modConfig != null)
        {
            AuraSharedRuntime.Initialize(modConfig, ownerModId);
            DataDirectories[ownerModId] = AuraSharedPaths.RootDirectory;
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

    public static void RegisterMaterial(string materialId, Material? material)
    {
        var id = (materialId ?? "").Trim();
        if (id.Length == 0 || material == null)
        {
            return;
        }

        RegisteredMaterials[id] = material;
    }

    public static void RegisterAssetBundle(string bundleId, AssetBundle? bundle)
    {
        var id = NormalizeBundleId(bundleId);
        if (id.Length == 0 || bundle == null)
        {
            return;
        }

        RegisteredBundles[id] = bundle;
    }

    public static void PreloadCg(string ownerModId, IEnumerable<SkillCgRequest> requests)
    {
        var batch = (requests ?? Array.Empty<SkillCgRequest>()).ToList();
        if (batch.Count == 0)
        {
            return;
        }

        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "PreloadCg", batch);
    }

    public static IReadOnlyList<SkillCgRegisteredEntryView> GetRegisteredSkillCgEntries(string ownerModId = "")
    {
        return GetRegisteredCgEntriesByKind(SkillCgKind, ownerModId);
    }

    public static IReadOnlyList<SkillCgRegisteredEntryView> GetRegisteredCardUseCgEntries(string ownerModId = "")
    {
        return GetRegisteredCgEntriesByKind(CardUseCgKind, ownerModId);
    }

    private static IReadOnlyList<SkillCgRegisteredEntryView> GetRegisteredCgEntriesByKind(string kind, string ownerModId)
    {
        return AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .Where(entry => IsRegisteredCgEntry(entry, kind))
            .Select(entry => new SkillCgRegisteredEntryView(entry, AuraCgActivationRuntime.GetEffectiveState(entry)))
            .OrderBy(view => view.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(view => view.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(view => view.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<SkillCgRequest> BuildRegisteredRequests(
        string consumerModId,
        SkillCgTriggerContext context,
        string ownerModId = "",
        bool disableSync = false)
    {
        return BuildRegisteredRequestsByKind(SkillCgKind, consumerModId, context, ownerModId, disableSync);
    }

    public static IReadOnlyList<SkillCgRequest> BuildRegisteredCardUseRequests(
        string consumerModId,
        SkillCgTriggerContext context,
        string ownerModId = "",
        bool disableSync = false)
    {
        return BuildRegisteredRequestsByKind(CardUseCgKind, consumerModId, context, ownerModId, disableSync);
    }

    private static IReadOnlyList<SkillCgRequest> BuildRegisteredRequestsByKind(
        string kind,
        string consumerModId,
        SkillCgTriggerContext context,
        string ownerModId,
        bool disableSync)
    {
        var requests = new List<SkillCgRequest>();
        foreach (var entry in AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId))
        {
            var request = BuildRegisteredRequestByKind(entry, kind, consumerModId, context, disableSync);
            if (request != null)
            {
                requests.Add(request);
            }
        }

        return requests
            .OrderByDescending(request => request.Priority)
            .ThenBy(request => request.QualifiedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static SkillCgRequest? BuildRegisteredRequest(
        AuraCgRegistryEntry entry,
        string consumerModId,
        SkillCgTriggerContext context,
        bool disableSync = false)
    {
        return BuildRegisteredRequestByKind(entry, SkillCgKind, consumerModId, context, disableSync);
    }

    public static SkillCgRequest? BuildRegisteredCardUseRequest(
        AuraCgRegistryEntry entry,
        string consumerModId,
        SkillCgTriggerContext context,
        bool disableSync = false)
    {
        return BuildRegisteredRequestByKind(entry, CardUseCgKind, consumerModId, context, disableSync);
    }

    private static SkillCgRequest? BuildRegisteredRequestByKind(
        AuraCgRegistryEntry entry,
        string kind,
        string consumerModId,
        SkillCgTriggerContext context,
        bool disableSync)
    {
        if (!EntryMatchesTrigger(entry, kind, consumerModId, context))
        {
            return null;
        }

        var imageResource = ResolveRegisteredImageResource(entry);
        var imagePath = ResolveImagePath(entry.OwnerModId, imageResource);
        if (!RegisteredMediaExists(entry.Media.Type, imagePath, entry.Media.BundlePath))
        {
            AuraCgLog.WarnOnce("registered-media-missing:" + entry.QualifiedCgId, "Registered CG media is missing: " + entry.QualifiedCgId + ", resource=" + imageResource);
            return null;
        }

        return CreateRegisteredRequest(entry, imageResource, imagePath, context, disableSync);
    }

    public static SkillCgRequest? BuildPreviewRequest(string consumerModId, string ownerModId, string cgId)
    {
        return BuildPreviewRequestByKind(SkillCgKind, consumerModId, ownerModId, cgId);
    }

    public static SkillCgRequest? BuildCardUsePreviewRequest(string consumerModId, string ownerModId, string cgId)
    {
        return BuildPreviewRequestByKind(CardUseCgKind, consumerModId, ownerModId, cgId);
    }

    private static SkillCgRequest? BuildPreviewRequestByKind(string kind, string consumerModId, string ownerModId, string cgId)
    {
        var entry = AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .FirstOrDefault(item => string.Equals(item.CgId, cgId, StringComparison.OrdinalIgnoreCase));
        if (entry == null || !IsRegisteredCgEntry(entry, kind))
        {
            return null;
        }

        var cardId = (entry.CardIds ?? new List<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("*")) ?? "*";
        return BuildRegisteredRequestByKind(entry, kind, consumerModId, new SkillCgTriggerContext
        {
            ActionSequence = -Math.Abs(DateTime.UtcNow.Ticks),
            Action = "*",
            CardId = cardId,
            OwnerRoleId = (entry.TargetRoleIds ?? new List<string>()).FirstOrDefault() ?? "*",
            CreatedAt = Time.unscaledTime
        }, disableSync: true);
    }

    public static bool PreviewRegisteredCg(string consumerModId, string ownerModId, string cgId)
    {
        var request = BuildPreviewRequest(consumerModId, ownerModId, cgId);
        if (request == null)
        {
            return false;
        }

        RequestCg(consumerModId, request);
        return true;
    }

    public static bool PreviewRegisteredCardUseCg(string consumerModId, string ownerModId, string cgId)
    {
        var request = BuildCardUsePreviewRequest(consumerModId, ownerModId, cgId);
        if (request == null)
        {
            return false;
        }

        RequestCg(consumerModId, request);
        return true;
    }

    public static void PreloadRegisteredCg(string consumerModId, string ownerModId = "", string roleId = "")
    {
        PreloadRegisteredCgByKind(SkillCgKind, consumerModId, ownerModId, roleId);
    }

    public static void PreloadRegisteredCardUseCg(string consumerModId, string ownerModId = "", string roleId = "")
    {
        PreloadRegisteredCgByKind(CardUseCgKind, consumerModId, ownerModId, roleId);
    }

    private static void PreloadRegisteredCgByKind(string kind, string consumerModId, string ownerModId, string roleId)
    {
        var requests = AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .Where(entry => IsRegisteredCgEntry(entry, kind))
            .Where(entry => AuraCgActivationRuntime.CanConsumerPlay(entry, consumerModId))
            .Where(entry => string.IsNullOrWhiteSpace(roleId) || EntryMatchesRole(entry, roleId))
            .Select(entry => CreateRegisteredRequest(entry, ResolveRegisteredImageResource(entry), ResolveImagePath(entry.OwnerModId, ResolveRegisteredImageResource(entry)), new SkillCgTriggerContext
            {
                CardId = (entry.CardIds ?? new List<string>()).FirstOrDefault() ?? "*",
                OwnerRoleId = roleId,
                CreatedAt = Time.unscaledTime
            }, disableSync: true))
            .Where(request => request != null)
            .Cast<SkillCgRequest>()
            .ToList();
        PreloadCg(consumerModId, requests);
    }

    private static bool EntryMatchesTrigger(AuraCgRegistryEntry entry, string kind, string consumerModId, SkillCgTriggerContext context)
    {
        return IsRegisteredCgEntry(entry, kind)
               && AuraCgActivationRuntime.CanConsumerPlay(entry, consumerModId)
               && EntryMatchesRole(entry, context.OwnerRoleId)
               && EntryMatchesCard(entry, context.CardId)
               && EntryMatchesAction(context.Action);
    }

    private static bool IsSkillCgEntry(AuraCgRegistryEntry entry)
    {
        return IsRegisteredCgEntry(entry, SkillCgKind);
    }

    private static bool IsRegisteredCgEntry(AuraCgRegistryEntry entry, string kind)
    {
        return entry != null
               && entry.Enabled
               && string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase)
               && (string.Equals(entry.Media.Type, SkillCgMediaTypes.Image, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(entry.Media.Type, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase));
    }

    private static SkillCgRequest CreateRegisteredRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        SkillCgTriggerContext context,
        bool disableSync)
    {
        return new SkillCgRequest
        {
            ProviderId = entry.OwnerModId + ".SkillCG." + entry.CgId,
            OwnerModId = entry.OwnerModId,
            CardId = string.IsNullOrWhiteSpace(context.CardId) ? "*" : context.CardId,
            OwnerInstanceId = context.OwnerInstanceId,
            ImagePath = imagePath,
            ImageResource = imageResource,
            BundlePath = entry.Media.BundlePath,
            BundleAssetPrefix = entry.Media.BundleAssetPrefix,
            MediaType = entry.Media.Type,
            FrameSeconds = entry.Media.FrameSeconds,
            AlphaMode = entry.Media.AlphaMode,
            KeyThreshold = entry.Media.KeyThreshold,
            KeySoftness = entry.Media.KeySoftness,
            FlashAtSeconds = entry.Media.FlashAtSeconds,
            FlashDuration = entry.Media.FlashDuration,
            FlashMode = entry.Media.FlashMode,
            FlashStartFrame = entry.Media.FlashStartFrame,
            FlashEndFrame = entry.Media.FlashEndFrame,
            FlashPulseEveryFrames = entry.Media.FlashPulseEveryFrames,
            FlashStrength = entry.Media.FlashStrength,
            Priority = entry.Priority,
            FadeIn = entry.DefaultPresentation.FadeIn,
            Hold = entry.DefaultPresentation.Hold,
            FadeOut = entry.DefaultPresentation.FadeOut,
            PresentationMode = entry.DefaultPresentation.Mode,
            FitMode = entry.DefaultPresentation.Fit,
            FocusX = entry.DefaultPresentation.FocusX,
            FocusY = entry.DefaultPresentation.FocusY,
            SafeScale = entry.DefaultPresentation.SafeScale,
            CreatedAt = Time.unscaledTime,
            ActionSequence = context.ActionSequence,
            DisableSync = disableSync
        };
    }

    private static string ResolveRegisteredImageResource(AuraCgRegistryEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Media.Resource)
            ? entry.Media.FallbackImage
            : entry.Media.Resource;
    }

    private static bool RegisteredMediaExists(string mediaType, string path, string bundlePath)
    {
        if (!string.IsNullOrWhiteSpace(bundlePath))
        {
            return true;
        }

        if (string.Equals(mediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
        {
            return Directory.Exists(path) || File.Exists(path);
        }

        return File.Exists(path);
    }

    private static bool EntryMatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        var normalizedRole = (roleId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return true;
        }

        foreach (var target in entry.TargetRoleIds ?? new List<string>())
        {
            var normalizedTarget = (target ?? "").Trim();
            if (string.Equals(normalizedTarget, "*", StringComparison.Ordinal)
                || string.Equals(normalizedTarget, normalizedRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EntryMatchesCard(AuraCgRegistryEntry entry, string cardId)
    {
        var normalizedCard = (cardId ?? "").Trim();
        var normalizedTrimmed = normalizedCard.TrimStart('*');
        foreach (var value in entry.CardIds ?? new List<string>())
        {
            var pattern = (value ?? "").Trim();
            if (string.Equals(pattern, "*", StringComparison.Ordinal)
                || string.Equals(pattern, normalizedCard, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pattern.TrimStart('*'), normalizedTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EntryMatchesAction(string action)
    {
        return true;
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

    private static string NormalizeRelativeResourcePath(string value)
    {
        var normalized = (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
        return normalized;
    }

    private static string NormalizeBundleId(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
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
                if (!ValidateExistingArbiter(existing, ownerModId))
                {
                    return null!;
                }

                if (ReuseLogOwners.Add(ownerModId))
                {
                    AuraCgLog.InfoOnce(
                        "reuse-arbiter:" + ownerModId,
                        "Reusing global CG arbiter for " + ownerModId
                        + ", ownerType=" + existing.GetType().Assembly.GetName().Name);
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
        AuraCgLog.InfoOnce("create-arbiter", "Created global CG arbiter. owner=" + ownerModId);
        return component;
    }

    private static bool ValidateExistingArbiter(object existing, string ownerModId)
    {
        var type = existing.GetType();
        var protocolVersion = ReadIntProperty(existing, "ProtocolVersion", 0);
        var minimumSupported = ReadIntProperty(existing, "MinimumSupportedProtocolVersion", int.MaxValue);
        var buildId = ReadStringProperty(existing, "BuildId");
        var methodsPresent = new[] { "Configure", "RegisterProvider", "Trigger", "RequestCg", "PreloadCg", "ClearQueue" }
            .All(name => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null);
        var compatible = protocolVersion >= MinimumSupportedProtocolVersion
            && minimumSupported <= CurrentProtocolVersion
            && methodsPresent;

        if (!compatible && CompatibilityErrorsShown.Add(ownerModId + ":" + type.AssemblyQualifiedName))
        {
            AuraCgLog.WarnOnce(
                "incompatible-arbiter:" + ownerModId,
                "Incompatible global CG arbiter; CG features disabled for " + ownerModId
                + ". existingAssembly=" + type.Assembly.GetName().Name
                + ", protocol=" + protocolVersion
                + ", minSupported=" + minimumSupported
                + ", buildId=" + (string.IsNullOrWhiteSpace(buildId) ? "<missing>" : buildId)
                + ", localBuildId=" + CurrentBuildId
                + ", methodsPresent=" + methodsPresent);
        }

        if (compatible
            && !string.IsNullOrWhiteSpace(buildId)
            && !string.Equals(buildId, CurrentBuildId, StringComparison.Ordinal)
            && ReuseLogOwners.Add("build:" + ownerModId + ":" + buildId))
        {
            AuraCgLog.WarnOnce(
                "build-mismatch:" + ownerModId + ":" + buildId,
                "Reusing protocol-compatible CG arbiter with a different build. owner="
                + ownerModId + ", existingBuildId=" + buildId + ", localBuildId=" + CurrentBuildId);
        }

        return compatible;
    }

    private static int ReadIntProperty(object source, string propertyName, int fallback)
    {
        try
        {
            return source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) is int value
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ReadStringProperty(object source, string propertyName)
    {
        try
        {
            return source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) as string ?? "";
        }
        catch
        {
            return "";
        }
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
        private readonly Dictionary<string, List<Sprite>> sequenceCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AssetBundle?> assetBundleCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> preloadKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Sprite> invertedSpriteCache = new();
        private SkillCgArbiterOptions options = new();
        private bool playing;
        private long enqueueSequence;
        private GameObject? overlayRoot;
        private Canvas? overlayCanvas;
        private CanvasGroup? overlayGroup;
        private Image? overlayImage;
        private Image? overlayFlash;
        private Image? overlayScreenFlash;
        private Sprite? screenFlashSprite;
        private Material? lumaKeyMaterial;
        private bool lumaKeyMaterialResolved;
        private Material? maskedInvertMaterial;
        private bool maskedInvertMaterialResolved;
        private Material? screenBwFlashMaterial;
        private bool screenBwFlashMaterialResolved;
        private int playGeneration;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => SkillCgArbiterRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

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
            AuraCgLog.InfoOnce(
                "arbiter-configured",
                "CG queue configured. maxQueue=" + options.MaxQueueLength
                + ", maxAge=" + options.MaxRequestAgeSeconds.ToString("0.##") + "s"
                + ", duplicateWindow=" + options.DuplicateWindowSeconds.ToString("0.##") + "s");
        }

        public void RegisterProvider(object? provider)
        {
            if (provider == null)
            {
                AuraCgLog.WarnOnce("provider-null", "Provider registration skipped: provider is null.");
                return;
            }

            try
            {
                var handle = new ProviderHandle(provider);
                if (string.IsNullOrWhiteSpace(handle.ProviderId))
                {
                    AuraCgLog.WarnOnce("provider-empty-id:" + provider.GetType().FullName, "Provider registration skipped: ProviderId is empty.");
                    return;
                }

                providers.RemoveAll(item => string.Equals(item.QualifiedProviderId, handle.QualifiedProviderId, StringComparison.OrdinalIgnoreCase));
                providers.Add(handle);
                providers.Sort((a, b) =>
                {
                    var priority = b.Priority.CompareTo(a.Priority);
                    return priority != 0 ? priority : string.Compare(a.QualifiedProviderId, b.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
                });
                AuraCgLog.InfoOnce("provider:" + handle.ProviderId, "CG provider registered: " + handle.Describe());
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("provider-failed:" + provider.GetType().FullName, "Provider registration failed: " + ex.Message);
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
                    : string.Compare(a.QualifiedProviderId, b.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
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

        public void PreloadCg(object? value)
        {
            if (value is not IEnumerable<SkillCgRequest> requests)
            {
                return;
            }

            foreach (var request in requests)
            {
                if (request == null)
                {
                    continue;
                }

                request.Normalize();
                var key = SequenceCacheKey(request);
                if (sequenceCache.ContainsKey(key) || !preloadKeys.Add(key))
                {
                    continue;
                }

                StartCoroutine(PreloadRequest(request, key));
            }
        }

        private IEnumerator PreloadRequest(SkillCgRequest request, string key)
        {
            if (!string.Equals(request.MediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
            {
                preloadKeys.Remove(key);
                yield break;
            }

            List<Sprite> sprites = new();
            yield return LoadSequenceSprites(request, result => sprites = result);
            if (sprites.Count > 0)
            {
                sequenceCache[key] = sprites;
                AuraCgLog.InfoOnce(
                    "sequence-preloaded:" + key,
                    "CG sequence preloaded: provider=" + request.ProviderId
                    + ", frames=" + sprites.Count
                    + ", bundle=" + (string.IsNullOrWhiteSpace(request.BundlePath) ? "<file>" : request.BundlePath));
            }

            preloadKeys.Remove(key);
        }

        public void ClearQueue(object? reason)
        {
            playGeneration++;
            queue.Clear();
            recentKeys.Clear();
            StopAllCoroutines();
            HideOverlay();
            playing = false;

            AuraCgLog.DebugLog("CG queue cleared: " + (reason as string ?? "<none>"));
        }

        private void HideOverlay()
        {
            if (overlayImage != null)
            {
                overlayImage.raycastTarget = false;
                overlayImage.enabled = false;
                overlayImage.sprite = null;
                overlayImage.material = null;
            }

            if (overlayFlash != null)
            {
                overlayFlash.raycastTarget = false;
                overlayFlash.enabled = false;
                overlayFlash.color = Color.clear;
                overlayFlash.sprite = null;
                overlayFlash.material = null;
            }

            if (overlayScreenFlash != null)
            {
                overlayScreenFlash.raycastTarget = false;
                overlayScreenFlash.enabled = false;
                overlayScreenFlash.color = Color.clear;
                overlayScreenFlash.material = null;
            }

            if (overlayGroup != null)
            {
                overlayGroup.alpha = 0f;
                overlayGroup.blocksRaycasts = false;
                overlayGroup.interactable = false;
            }

            if (overlayRoot != null)
            {
                UiRaycastSafeDestroyRuntime.DisableAndHide(overlayRoot, "Aura CG hide overlay", AuraCgLog.DebugLog);
            }
        }

        private void DestroyOverlay()
        {
            if (overlayRoot != null)
            {
                UiRaycastSafeDestroyRuntime.DisableAndDestroyAfterFrame(
                    this,
                    overlayRoot,
                    "Aura CG destroy overlay",
                    AuraCgLog.DebugLog);
            }

            overlayRoot = null;
            overlayCanvas = null;
            overlayGroup = null;
            overlayImage = null;
            overlayFlash = null;
            overlayScreenFlash = null;
            DestroyRuntimeMaterial();
        }

        private bool TryEnqueue(SkillCgRequest request)
        {
            request.Normalize();
            if (string.IsNullOrWhiteSpace(request.ImagePath))
            {
                AuraCgLog.WarnOnce("empty-media:" + request.ProviderId, "CG request skipped: media path is empty. provider=" + request.ProviderId);
                return false;
            }

            PruneRecentKeys();
            var duplicateKey = request.DuplicateKey;
            if (recentKeys.TryGetValue(duplicateKey, out var lastTime)
                && Time.unscaledTime - lastTime <= options.DuplicateWindowSeconds)
            {
                AuraCgLog.DebugLog("Duplicate CG request skipped: " + duplicateKey);
                return false;
            }

            recentKeys[duplicateKey] = Time.unscaledTime;
            queue.Add(new QueuedRequest(request, ++enqueueSequence));
            if (queue.Count > options.MaxQueueLength)
            {
                queue.Sort(QueuedRequest.CompareForQueue);
                var dropCount = queue.Count - options.MaxQueueLength;
                queue.RemoveRange(0, dropCount);
                AuraCgLog.WarnOnce("queue-full", "CG queue is full; oldest pending CG requests will be dropped. max=" + options.MaxQueueLength);
            }

            queue.Sort(QueuedRequest.CompareForQueue);
            AuraCgLog.DebugLog("CG queued: provider=" + request.ProviderId + ", card=" + request.CardId + ", queue=" + queue.Count);
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
                AuraCgLog.WarnOnce("remote-sync-failed", "Remote CG sync failed once; later errors are suppressed. error=" + ex.Message);
                AuraCgLog.DebugLog("Remote CG sync exception: " + ex);
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
                    AuraCgLog.WarnOnce("request-stale", "Stale CG requests are being skipped. maxAge=" + options.MaxRequestAgeSeconds.ToString("0.##") + "s");
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
            if (string.Equals(request.MediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
            {
                yield return PlaySequenceRequest(request, generation);
                yield break;
            }

            yield return PlayImageRequest(request, generation);
        }

        private IEnumerator PlayImageRequest(SkillCgRequest request, int generation)
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
            if (overlayRoot.transform.parent != null)
            {
                overlayRoot.transform.SetAsLastSibling();
            }

            overlayImage!.sprite = sprite;
            overlayImage.material = ResolveLumaKeyMaterial(request);
            overlayImage.raycastTarget = false;
            overlayImage.enabled = true;
            overlayGroup!.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            AuraCgLog.DebugLog(
                "CG play: provider=" + request.ProviderId
                + ", card=" + request.CardId
                + ", image=" + Path.GetFileName(request.ImagePath)
                + ", mode=" + request.PresentationMode
                + ", fit=" + request.FitMode);

            if (string.Equals(request.PresentationMode, SkillCgPresentationModes.FullscreenFade, StringComparison.OrdinalIgnoreCase))
            {
                yield return FullscreenFade(sprite, request, generation);
            }
            else if (string.Equals(request.PresentationMode, SkillCgPresentationModes.CenterFade, StringComparison.OrdinalIgnoreCase))
            {
                yield return CenterFade(sprite, request, generation);
            }
            else
            {
                yield return SlideRightToLeft(sprite, generation);
            }

            if (generation != playGeneration)
            {
                yield break;
            }

            HideOverlay();
        }

        private IEnumerator PlaySequenceRequest(SkillCgRequest request, int generation)
        {
            var spritesReady = false;
            List<Sprite> sprites = new();
            yield return LoadSequenceSprites(request, result =>
            {
                sprites = result;
                spritesReady = true;
            });

            if (!spritesReady || sprites.Count == 0)
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
            if (overlayRoot.transform.parent != null)
            {
                overlayRoot.transform.SetAsLastSibling();
            }

            overlayImage!.sprite = sprites[0];
            overlayImage.material = ResolveLumaKeyMaterial(request);
            overlayImage.raycastTarget = false;
            overlayImage.enabled = true;
            overlayGroup!.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;
            if (overlayFlash != null)
            {
                overlayFlash.enabled = false;
                overlayFlash.color = Color.clear;
                overlayFlash.sprite = null;
                overlayFlash.material = null;
            }
            if (overlayScreenFlash != null)
            {
                overlayScreenFlash.enabled = false;
                overlayScreenFlash.color = Color.clear;
                overlayScreenFlash.material = null;
            }

            ConfigureFullscreenImage(sprites[0], request);

            AuraCgLog.DebugLog(
                "CG play sequence: provider=" + request.ProviderId
                + ", card=" + request.CardId
                + ", frames=" + sprites.Count
                + ", frameSeconds=" + request.FrameSeconds.ToString("0.###")
                + ", fit=" + request.FitMode);

            yield return Fade(0f, 1f, request.FadeIn, generation);
            yield return PlaySequenceFrames(sprites, request, generation);
            DisableMaskedFlash();
            DisableScreenFlash();
            yield return Wait(request.Hold, generation);
            yield return Fade(1f, 0f, request.FadeOut, generation);

            if (generation != playGeneration)
            {
                yield break;
            }

            HideOverlay();
        }

        private IEnumerator SlideRightToLeft(Sprite sprite, int generation)
        {
            if (overlayRoot == null || overlayGroup == null || overlayImage == null)
            {
                yield break;
            }

            var imageRect = overlayImage.rectTransform;
            overlayImage.preserveAspect = true;
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

        private IEnumerator FullscreenFade(Sprite sprite, SkillCgRequest request, int generation)
        {
            if (overlayGroup == null || overlayImage == null)
            {
                yield break;
            }

            ConfigureFullscreenImage(sprite, request);
            yield return Fade(0f, 1f, request.FadeIn, generation);
            yield return Wait(request.Hold, generation);
            yield return Fade(1f, 0f, request.FadeOut, generation);
        }

        private IEnumerator CenterFade(Sprite sprite, SkillCgRequest request, int generation)
        {
            if (overlayGroup == null || overlayImage == null)
            {
                yield break;
            }

            ConfigureCenteredImage(sprite);
            yield return Fade(0f, 1f, request.FadeIn, generation);
            yield return Wait(request.Hold, generation);
            yield return Fade(1f, 0f, request.FadeOut, generation);
        }

        private void ConfigureFullscreenImage(Sprite sprite, SkillCgRequest request)
        {
            if (overlayImage == null)
            {
                return;
            }

            ConfigureFullscreenGraphic(overlayImage, sprite, request);
        }

        private void ConfigureFullscreenGraphic(Image image, Sprite sprite, SkillCgRequest request)
        {
            var imageRect = image.rectTransform;
            imageRect.pivot = new Vector2(0.5f, 0.5f);

            if (string.Equals(request.FitMode, SkillCgFitModes.Stretch, StringComparison.OrdinalIgnoreCase))
            {
                image.preserveAspect = false;
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.offsetMin = Vector2.zero;
                imageRect.offsetMax = Vector2.zero;
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.sizeDelta = Vector2.zero;
                return;
            }

            image.preserveAspect = true;
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            var viewport = GetOverlayViewportSize();
            if (string.Equals(request.FitMode, SkillCgFitModes.Cover, StringComparison.OrdinalIgnoreCase))
            {
                var imageSize = CalculateCoverImageSize(sprite, viewport, request.SafeScale);
                imageRect.sizeDelta = imageSize;
                imageRect.anchoredPosition = CalculateCoverImageOffset(imageSize, viewport, request.FocusX, request.FocusY);
                return;
            }

            imageRect.anchoredPosition = Vector2.zero;
            imageRect.sizeDelta = viewport;
        }

        private void ConfigureCenteredImage(Sprite sprite)
        {
            if (overlayImage == null)
            {
                return;
            }

            var imageRect = overlayImage.rectTransform;
            overlayImage.preserveAspect = true;
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;
            imageRect.sizeDelta = CalculateImageSize(sprite, GetOverlayViewportSize());
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

        private static Vector2 CalculateCoverImageSize(Sprite sprite, Vector2 viewport, float safeScale)
        {
            var spriteRect = sprite.rect;
            var aspect = spriteRect.height <= 0f ? 1f : spriteRect.width / spriteRect.height;
            var viewportAspect = viewport.y <= 0f ? 1f : viewport.x / viewport.y;
            var scale = Mathf.Clamp(safeScale <= 0f ? 1f : safeScale, 1f, 3f);
            if (aspect >= viewportAspect)
            {
                var height = Mathf.Max(1f, viewport.y) * scale;
                return new Vector2(height * aspect, height);
            }

            var width = Mathf.Max(1f, viewport.x) * scale;
            return new Vector2(width, width / Mathf.Max(0.001f, aspect));
        }

        private static Vector2 CalculateCoverImageOffset(Vector2 imageSize, Vector2 viewport, float focusX, float focusY)
        {
            var overflowX = Mathf.Max(0f, imageSize.x - viewport.x);
            var overflowY = Mathf.Max(0f, imageSize.y - viewport.y);
            return new Vector2(
                Mathf.Clamp((0.5f - Mathf.Clamp01(focusX)) * overflowX, -overflowX * 0.5f, overflowX * 0.5f),
                Mathf.Clamp((Mathf.Clamp01(focusY) - 0.5f) * overflowY, -overflowY * 0.5f, overflowY * 0.5f));
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

        private IEnumerator PlaySequenceFrames(IReadOnlyList<Sprite> sprites, SkillCgRequest request, int generation)
        {
            if (overlayImage == null)
            {
                yield break;
            }

            var frameSeconds = Mathf.Max(0.01f, request.FrameSeconds);
            var totalSeconds = Mathf.Max(frameSeconds, sprites.Count * frameSeconds);
            var elapsed = 0f;
            var lastIndex = -1;
            while (generation == playGeneration && elapsed < totalSeconds)
            {
                var index = Mathf.Clamp((int)(elapsed / frameSeconds), 0, sprites.Count - 1);
                if (index != lastIndex)
                {
                    overlayImage.sprite = sprites[index];
                    ConfigureFullscreenImage(sprites[index], request);
                    lastIndex = index;
                }

                UpdateSequenceFlash(request, elapsed, index + 1, sprites[index]);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (generation == playGeneration && sprites.Count > 0)
            {
                overlayImage.sprite = sprites[sprites.Count - 1];
                ConfigureFullscreenImage(sprites[sprites.Count - 1], request);
                UpdateSequenceFlash(request, totalSeconds, sprites.Count, sprites[sprites.Count - 1]);
            }
        }

        private void UpdateSequenceFlash(SkillCgRequest request, float elapsed, int frameNumber, Sprite sprite)
        {
            UpdateScreenBwFlash(request, frameNumber);

            if (ShouldUseMaskedFlash(request))
            {
                UpdateMaskedFlash(request, frameNumber, sprite);
                return;
            }

            UpdateFlash(request, elapsed);
        }

        private void UpdateScreenBwFlash(SkillCgRequest request, int frameNumber)
        {
            if (overlayScreenFlash == null || !ShouldUseScreenBwFlash(request))
            {
                return;
            }

            var startFrame = Mathf.Max(1, request.FlashStartFrame);
            var endFrame = Mathf.Max(startFrame, request.FlashEndFrame <= 0 ? startFrame : request.FlashEndFrame);
            if (frameNumber < startFrame || frameNumber > endFrame)
            {
                DisableScreenFlash();
                return;
            }

            var localFrame = frameNumber - startFrame;
            var baseStrength = Mathf.Clamp01(request.FlashStrength <= 0f ? 1f : request.FlashStrength);
            var pulse = ScreenBwPulse(localFrame) * baseStrength;
            if (pulse <= 0.001f)
            {
                DisableScreenFlash();
                return;
            }

            overlayScreenFlash.sprite = ScreenFlashSprite();
            overlayScreenFlash.raycastTarget = false;
            overlayScreenFlash.enabled = true;
            overlayScreenFlash.rectTransform.anchorMin = Vector2.zero;
            overlayScreenFlash.rectTransform.anchorMax = Vector2.one;
            overlayScreenFlash.rectTransform.offsetMin = Vector2.zero;
            overlayScreenFlash.rectTransform.offsetMax = Vector2.zero;

            var material = ResolveScreenBwFlashMaterial();
            if (material != null && localFrame <= 6 && localFrame % 2 == 0)
            {
                overlayScreenFlash.material = material;
                SetMaterialFloat(material, "_AuraCgFlashStrength", pulse);
                overlayScreenFlash.color = Color.white;
                return;
            }

            overlayScreenFlash.material = null;
            overlayScreenFlash.color = localFrame % 2 == 0
                ? new Color(1f, 1f, 1f, pulse * 0.86f)
                : new Color(0f, 0f, 0f, pulse * 0.72f);
        }

        private static float ScreenBwPulse(int localFrame)
        {
            return localFrame switch
            {
                0 => 1.0f,
                1 => 0.82f,
                2 => 0.68f,
                3 => 0.48f,
                4 => 0.34f,
                5 => 0.24f,
                6 => 0.16f,
                _ => 0.08f
            };
        }

        private void UpdateFlash(SkillCgRequest request, float elapsed)
        {
            if (overlayFlash == null || request.FlashAtSeconds < 0f)
            {
                return;
            }

            var since = elapsed - request.FlashAtSeconds;
            if (since < 0f || since > request.FlashDuration)
            {
                overlayFlash.color = Color.clear;
                overlayFlash.enabled = false;
                return;
            }

            var alpha = Mathf.Clamp01(1f - since / Mathf.Max(0.03f, request.FlashDuration));
            overlayFlash.enabled = alpha > 0.001f;
            overlayFlash.color = new Color(1f, 0.94f, 0.72f, alpha * 0.82f);
        }

        private void UpdateMaskedFlash(SkillCgRequest request, int frameNumber, Sprite sprite)
        {
            if (overlayFlash == null)
            {
                return;
            }

            var startFrame = Mathf.Max(1, request.FlashStartFrame);
            var endFrame = Mathf.Max(startFrame, request.FlashEndFrame <= 0 ? startFrame : request.FlashEndFrame);
            if (frameNumber < startFrame || frameNumber > endFrame)
            {
                DisableMaskedFlash();
                return;
            }

            var pulseEvery = Mathf.Max(1, request.FlashPulseEveryFrames);
            if (pulseEvery > 1 && (frameNumber - startFrame) % pulseEvery != 0)
            {
                DisableMaskedFlash();
                return;
            }

            var strength = Mathf.Clamp01(request.FlashStrength <= 0f ? 1f : request.FlashStrength);
            var material = ResolveMaskedInvertMaterial(request);
            if (material != null)
            {
                overlayFlash.sprite = sprite;
                overlayFlash.material = material;
                SetMaterialFloat(material, "_AuraCgFlashStrength", strength);
                SetMaterialFloat(material, "_AuraCgKeyThreshold", request.KeyThreshold);
                SetMaterialFloat(material, "_AuraCgKeySoftness", request.KeySoftness);
                overlayFlash.color = Color.white;
            }
            else
            {
                overlayFlash.sprite = CreateInvertedSprite(sprite);
                overlayFlash.material = null;
                overlayFlash.color = new Color(1f, 1f, 1f, strength);
            }

            overlayFlash.raycastTarget = false;
            overlayFlash.enabled = overlayFlash.sprite != null;
            ConfigureFullscreenGraphic(overlayFlash, sprite, request);
        }

        private bool ShouldUseMaskedFlash(SkillCgRequest request)
        {
            if (string.Equals(request.FlashMode, SkillCgFlashModes.ScreenBwPulse, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(request.FlashMode, SkillCgFlashModes.MaskedInvert, StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.FlashMode, SkillCgFlashModes.HybridBwPulse, StringComparison.OrdinalIgnoreCase)
                || request.FlashStartFrame > 0
                || request.FlashEndFrame > 0;
        }

        private static bool ShouldUseScreenBwFlash(SkillCgRequest request)
        {
            return string.Equals(request.FlashMode, SkillCgFlashModes.ScreenBwPulse, StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.FlashMode, SkillCgFlashModes.HybridBwPulse, StringComparison.OrdinalIgnoreCase);
        }

        private void DisableMaskedFlash()
        {
            if (overlayFlash == null)
            {
                return;
            }

            overlayFlash.enabled = false;
            overlayFlash.color = Color.clear;
            overlayFlash.sprite = null;
            overlayFlash.material = null;
        }

        private void DisableScreenFlash()
        {
            if (overlayScreenFlash == null)
            {
                return;
            }

            overlayScreenFlash.enabled = false;
            overlayScreenFlash.color = Color.clear;
            overlayScreenFlash.material = null;
        }

        private Material? ResolveMaskedInvertMaterial(SkillCgRequest request)
        {
            if (maskedInvertMaterialResolved)
            {
                return maskedInvertMaterial;
            }

            maskedInvertMaterialResolved = true;
            try
            {
                maskedInvertMaterial = CloneRegisteredMaterial(MaskedInvertShaderName, "AuraCg.MaskedInvertFlash.Runtime");
                if (maskedInvertMaterial != null)
                {
                    return maskedInvertMaterial;
                }

                var shader = Shader.Find(MaskedInvertShaderName);
                if (shader == null)
                {
                    AuraCgLog.WarnOnce(
                        "masked-invert-shader-missing",
                        "Masked invert shader is not loaded; using CPU inverted-sprite fallback. shader=" + MaskedInvertShaderName);
                    return null;
                }

                maskedInvertMaterial = new Material(shader)
                {
                    name = "AuraCg.MaskedInvertFlash.Runtime"
                };
                return maskedInvertMaterial;
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("masked-invert-shader-failed", "Masked invert shader setup failed: " + ex.Message);
                return null;
            }
        }

        private Material? ResolveScreenBwFlashMaterial()
        {
            if (screenBwFlashMaterialResolved)
            {
                return screenBwFlashMaterial;
            }

            screenBwFlashMaterialResolved = true;
            try
            {
                screenBwFlashMaterial = CloneRegisteredMaterial(ScreenBwFlashShaderName, "AuraCg.ScreenBwFlash.Runtime");
                if (screenBwFlashMaterial != null)
                {
                    return screenBwFlashMaterial;
                }

                var shader = Shader.Find(ScreenBwFlashShaderName);
                if (shader != null)
                {
                    screenBwFlashMaterial = new Material(shader)
                    {
                        name = "AuraCg.ScreenBwFlash.Runtime"
                    };
                }
                else
                {
                    AuraCgLog.WarnOnce(
                        "screen-bw-flash-shader-missing",
                        "Screen black-white flash shader is not loaded; using color overlay fallback. shader=" + ScreenBwFlashShaderName);
                }
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("screen-bw-flash-material-failed", "Screen black-white flash material failed: " + ex.Message);
                screenBwFlashMaterial = null;
            }

            return screenBwFlashMaterial;
        }

        private Material? ResolveLumaKeyMaterial(SkillCgRequest request)
        {
            if (!string.Equals(SkillCgAlphaModes.Normalize(request.AlphaMode), SkillCgAlphaModes.BlackKey, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!lumaKeyMaterialResolved)
            {
                lumaKeyMaterialResolved = true;
                try
                {
                    lumaKeyMaterial = CloneRegisteredMaterial(LumaKeyShaderName, "AuraCg.LumaKeyUI.Runtime");
                    if (lumaKeyMaterial != null)
                    {
                        SetMaterialFloat(lumaKeyMaterial, "_AuraCgKeyThreshold", request.KeyThreshold);
                        SetMaterialFloat(lumaKeyMaterial, "_AuraCgKeySoftness", request.KeySoftness);
                        return lumaKeyMaterial;
                    }

                    var shader = Shader.Find(LumaKeyShaderName);
                    if (shader != null)
                    {
                        lumaKeyMaterial = new Material(shader)
                        {
                            name = "AuraCg.LumaKeyUI.Runtime"
                        };
                    }
                    else
                    {
                        AuraCgLog.WarnOnce(
                            "luma-key-shader-missing",
                            "Luma-key shader is not loaded; using CPU black-key fallback. shader=" + LumaKeyShaderName);
                    }
                }
                catch (Exception ex)
                {
                    AuraCgLog.WarnOnce("luma-key-shader-failed", "Luma-key shader setup failed: " + ex.Message);
                }
            }

            if (lumaKeyMaterial != null)
            {
                SetMaterialFloat(lumaKeyMaterial, "_AuraCgKeyThreshold", request.KeyThreshold);
                SetMaterialFloat(lumaKeyMaterial, "_AuraCgKeySoftness", request.KeySoftness);
            }

            return lumaKeyMaterial;
        }

        private static Material? CloneRegisteredMaterial(string materialId, string runtimeName)
        {
            try
            {
                if (!RegisteredMaterials.TryGetValue(materialId, out var source) || source == null)
                {
                    return null;
                }

                return new Material(source)
                {
                    name = runtimeName
                };
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("registered-material-clone-failed:" + materialId, "Registered CG material clone failed: " + materialId + ", error=" + ex.Message);
                return null;
            }
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            try
            {
                if (material.HasProperty(propertyName))
                {
                    material.SetFloat(propertyName, value);
                }
            }
            catch
            {
            }
        }

        private Sprite CreateInvertedSprite(Sprite source)
        {
            var texture = source.texture;
            var key = texture.GetInstanceID();
            if (invertedSpriteCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            try
            {
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var color = pixels[i];
                    color.r = (byte)(255 - color.r);
                    color.g = (byte)(255 - color.g);
                    color.b = (byte)(255 - color.b);
                    pixels[i] = color;
                }

                var invertedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
                {
                    name = texture.name + "_masked_invert"
                };
                invertedTexture.SetPixels32(pixels);
                invertedTexture.Apply(false, false);

                var sprite = Sprite.Create(
                    invertedTexture,
                    new Rect(0f, 0f, invertedTexture.width, invertedTexture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = source.name + "_masked_invert";
                invertedSpriteCache[key] = sprite;
                return sprite;
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("masked-invert-cpu-failed:" + source.name, "CPU masked invert fallback failed: " + ex.Message);
                return source;
            }
        }

        private Sprite ScreenFlashSprite()
        {
            if (screenFlashSprite != null)
            {
                return screenFlashSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "AuraCg.ScreenFlash.WhitePixel"
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);
            screenFlashSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
            screenFlashSprite.name = "AuraCg.ScreenFlash.Sprite";
            return screenFlashSprite;
        }

        private void DestroyRuntimeMaterial()
        {
            if (lumaKeyMaterial != null)
            {
                UnityEngine.Object.Destroy(lumaKeyMaterial);
            }

            if (maskedInvertMaterial != null)
            {
                UnityEngine.Object.Destroy(maskedInvertMaterial);
            }

            if (screenBwFlashMaterial != null)
            {
                UnityEngine.Object.Destroy(screenBwFlashMaterial);
            }

            lumaKeyMaterial = null;
            lumaKeyMaterialResolved = false;
            maskedInvertMaterial = null;
            maskedInvertMaterialResolved = false;
            screenBwFlashMaterial = null;
            screenBwFlashMaterialResolved = false;
        }

        private IEnumerator LoadSequenceSprites(SkillCgRequest request, Action<List<Sprite>> onLoaded)
        {
            var cacheKey = SequenceCacheKey(request);
            if (sequenceCache.TryGetValue(cacheKey, out var cached) && cached.Count > 0)
            {
                onLoaded(cached);
                yield break;
            }

            var result = new List<Sprite>();
            if (!string.IsNullOrWhiteSpace(request.BundlePath))
            {
                yield return LoadBundleSequenceSprites(request, result);
                if (result.Count > 0)
                {
                    sequenceCache[cacheKey] = result;
                    onLoaded(result);
                    yield break;
                }
            }

            foreach (var framePath in ResolveSequenceFramePaths(request.ImagePath))
            {
                Sprite? frame = null;
                yield return LoadSprite(
                    framePath,
                    request.AlphaMode,
                    request.KeyThreshold,
                    request.KeySoftness,
                    sprite => frame = sprite);
                if (frame != null)
                {
                    result.Add(frame);
                }
            }

            if (result.Count > 0)
            {
                sequenceCache[cacheKey] = result;
            }

            if (result.Count == 0)
            {
                AuraCgLog.WarnOnce("sequence-empty:" + request.ImagePath, "CG sequence has no loadable frames: " + request.ImagePath);
            }

            onLoaded(result);
        }

        private IEnumerator LoadBundleSequenceSprites(SkillCgRequest request, List<Sprite> result)
        {
            var bundle = ResolveAssetBundle(request.BundlePath);
            if (bundle == null)
            {
                yield break;
            }

            string[] assetNames;
            try
            {
                var prefix = NormalizeRelativeResourcePath(request.BundleAssetPrefix);
                assetNames = bundle.GetAllAssetNames()
                    .Where(name => IsBundleSequenceAsset(name, prefix))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("bundle-sequence-list-failed:" + request.BundlePath, "CG bundle sequence list failed: " + request.BundlePath + ", error=" + ex.Message);
                yield break;
            }

            foreach (var assetName in assetNames)
            {
                Sprite? sprite = null;
                var spriteRequest = bundle.LoadAssetAsync<Sprite>(assetName);
                yield return spriteRequest;
                sprite = spriteRequest.asset as Sprite;
                if (sprite == null)
                {
                    var textureRequest = bundle.LoadAssetAsync<Texture2D>(assetName);
                    yield return textureRequest;
                    if (textureRequest.asset is Texture2D texture)
                    {
                        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                        sprite.name = Path.GetFileNameWithoutExtension(assetName);
                    }
                }

                if (sprite != null)
                {
                    result.Add(sprite);
                }
            }

            if (result.Count > 0)
            {
                AuraCgLog.InfoOnce(
                    "bundle-sequence-loaded:" + request.BundlePath + ":" + request.BundleAssetPrefix,
                    "CG bundle sequence loaded: bundle=" + request.BundlePath
                    + ", prefix=" + request.BundleAssetPrefix
                    + ", frames=" + result.Count);
            }
        }

        private static bool IsBundleSequenceAsset(string assetName, string prefix)
        {
            var normalized = NormalizeRelativeResourcePath(assetName);
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                var normalizedPrefix = NormalizeRelativeResourcePath(prefix).TrimEnd('/') + "/";
                if (!normalized.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
                    && normalized.IndexOf("/" + normalizedPrefix, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return IsSupportedSequenceFrame(normalized);
        }

        private AssetBundle? ResolveAssetBundle(string bundlePath)
        {
            var id = NormalizeBundleId(bundlePath);
            if (id.Length == 0)
            {
                return null;
            }

            if (RegisteredBundles.TryGetValue(id, out var registered) && registered != null)
            {
                return registered;
            }

            if (assetBundleCache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var resolved = ResolveImagePath("", id, id);
            if (!File.Exists(resolved))
            {
                assetBundleCache[id] = null;
                AuraCgLog.WarnOnce("bundle-missing:" + id, "CG asset bundle is not registered or found: " + id);
                return null;
            }

            try
            {
                var bundle = AssetBundle.LoadFromFile(resolved);
                assetBundleCache[id] = bundle;
                return bundle;
            }
            catch (Exception ex)
            {
                assetBundleCache[id] = null;
                AuraCgLog.WarnOnce("bundle-load-failed:" + id, "CG asset bundle load failed: " + id + ", error=" + ex.Message);
                return null;
            }
        }

        private static string SequenceCacheKey(SkillCgRequest request)
        {
            return (request.BundlePath ?? "")
                + "\u001f" + (request.BundleAssetPrefix ?? "")
                + "\u001f" + (request.ImagePath ?? "")
                + "\u001f" + SkillCgAlphaModes.Normalize(request.AlphaMode)
                + "\u001f" + request.KeyThreshold.ToString("0.####")
                + "\u001f" + request.KeySoftness.ToString("0.####");
        }

        private static IEnumerable<string> ResolveSequenceFramePaths(string path)
        {
            if (Directory.Exists(path))
            {
                return Directory.GetFiles(path)
                    .Where(IsSupportedSequenceFrame)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return File.Exists(path) && IsSupportedSequenceFrame(path)
                ? new[] { path }
                : Array.Empty<string>();
        }

        private static bool IsSupportedSequenceFrame(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerator LoadSprite(string path, Action<Sprite?> onLoaded)
        {
            yield return LoadSprite(path, SkillCgAlphaModes.None, 0.03f, 0.08f, onLoaded);
        }

        private IEnumerator LoadSprite(
            string path,
            string alphaMode,
            float keyThreshold,
            float keySoftness,
            Action<Sprite?> onLoaded)
        {
            var cacheKey = SpriteCacheKey(path, alphaMode, keyThreshold, keySoftness);
            if (spriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                onLoaded(cached);
                yield break;
            }

            if (!File.Exists(path))
            {
                AuraCgLog.WarnOnce("missing-image:" + path, "CG image not found: " + path);
                onLoaded(null);
                yield break;
            }

            using var request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                AuraCgLog.WarnOnce("image-load-failed:" + path, "CG image failed to load: " + Path.GetFileName(path) + ", error=" + request.error);
                onLoaded(null);
                yield break;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null)
            {
                AuraCgLog.WarnOnce("image-empty:" + path, "CG image load returned empty texture: " + path);
                onLoaded(null);
                yield break;
            }

            texture.name = Path.GetFileNameWithoutExtension(path);
            if (ShouldApplyCpuAlphaMode(alphaMode))
            {
                ApplyAlphaMode(texture, alphaMode, keyThreshold, keySoftness, path);
            }

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            spriteCache[cacheKey] = sprite;
            AuraCgLog.InfoOnce("image-loaded:" + path, "CG image loaded: " + Path.GetFileName(path) + " (" + texture.width + "x" + texture.height + ")");
            onLoaded(sprite);
        }

        private bool ShouldApplyCpuAlphaMode(string alphaMode)
        {
            if (!string.Equals(SkillCgAlphaModes.Normalize(alphaMode), SkillCgAlphaModes.BlackKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ResolveLumaKeyMaterial(new SkillCgRequest { AlphaMode = alphaMode }) == null;
        }

        private static string SpriteCacheKey(string path, string alphaMode, float keyThreshold, float keySoftness)
        {
            return path
                + "\u001f" + SkillCgAlphaModes.Normalize(alphaMode)
                + "\u001f" + keyThreshold.ToString("0.####")
                + "\u001f" + keySoftness.ToString("0.####");
        }

        private static void ApplyAlphaMode(Texture2D texture, string alphaMode, float keyThreshold, float keySoftness, string path)
        {
            if (!string.Equals(SkillCgAlphaModes.Normalize(alphaMode), SkillCgAlphaModes.BlackKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var pixels = texture.GetPixels32();
                var threshold = Mathf.Clamp01(keyThreshold);
                var softness = Mathf.Clamp(keySoftness, 0.001f, 1f);
                for (var i = 0; i < pixels.Length; i++)
                {
                    var color = pixels[i];
                    var luma = (0.299f * color.r + 0.587f * color.g + 0.114f * color.b) / 255f;
                    var alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((luma - threshold) / softness));
                    color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * alpha), 0, 255);
                    pixels[i] = color;
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("black-key-failed:" + path, "CG black-key alpha fallback failed: " + Path.GetFileName(path) + ", error=" + ex.Message);
            }
        }

        private bool EnsureOverlay()
        {
            if (overlayRoot != null
                && overlayCanvas != null
                && overlayGroup != null
                && overlayImage != null
                && overlayFlash != null
                && overlayScreenFlash != null)
            {
                return true;
            }

            if (overlayRoot != null || overlayCanvas != null || overlayGroup != null || overlayImage != null || overlayFlash != null || overlayScreenFlash != null)
            {
                DestroyOverlay();
            }

            overlayRoot = new GameObject("AuraCg.OverlayRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(overlayRoot);
            var rect = overlayRoot.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayCanvas = overlayRoot.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = OverlaySortingOrder;

            overlayGroup = overlayRoot.GetComponent<CanvasGroup>();
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            var imageObject = new GameObject("AuraCg.Image", typeof(RectTransform), typeof(Image));
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
            overlayImage.enabled = false;

            var flashObject = new GameObject("AuraCg.Flash", typeof(RectTransform), typeof(Image));
            flashObject.transform.SetParent(overlayRoot.transform, false);
            var flashRect = flashObject.GetComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;

            overlayFlash = flashObject.GetComponent<Image>();
            overlayFlash.color = Color.clear;
            overlayFlash.raycastTarget = false;
            overlayFlash.enabled = false;

            var screenFlashObject = new GameObject("AuraCg.ScreenFlash", typeof(RectTransform), typeof(Image));
            screenFlashObject.transform.SetParent(overlayRoot.transform, false);
            var screenFlashRect = screenFlashObject.GetComponent<RectTransform>();
            screenFlashRect.anchorMin = Vector2.zero;
            screenFlashRect.anchorMax = Vector2.one;
            screenFlashRect.offsetMin = Vector2.zero;
            screenFlashRect.offsetMax = Vector2.zero;

            overlayScreenFlash = screenFlashObject.GetComponent<Image>();
            overlayScreenFlash.color = Color.clear;
            overlayScreenFlash.raycastTarget = false;
            overlayScreenFlash.enabled = false;
            overlayRoot.SetActive(false);
            AuraCgLog.InfoOnce("overlay-created", "CG overlay created on an independent non-interactive canvas.");
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
            if (string.IsNullOrWhiteSpace(OwnerModId))
            {
                OwnerModId = providerType.Assembly.GetName().Name ?? "";
            }

            QualifiedProviderId = QualifyProviderId(OwnerModId, ProviderId);
            Priority = ReadInt("Priority", 0);
        }

        public string ProviderId { get; }

        public string OwnerModId { get; }

        public string QualifiedProviderId { get; }

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
                    var request = SkillCgRequest.FromObject(item, QualifiedProviderId, OwnerModId, Priority, context);
                    if (request != null)
                    {
                        output.Add(request);
                    }
                }
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("provider-build-failed:" + ProviderId, "Provider BuildRequests failed once: " + ProviderId + " -> " + ex.Message);
                AuraCgLog.DebugLog("Provider BuildRequests exception: " + ex);
            }
        }

        public string Describe()
        {
            return "providerId=" + ProviderId
                + ", qualifiedProviderId=" + QualifiedProviderId
                + ", owner=" + OwnerModId
                + ", priority=" + Priority;
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

        private static string QualifyProviderId(string ownerModId, string providerId)
        {
            var owner = (ownerModId ?? "").Trim();
            var id = (providerId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "unknown";
            }

            if (id.Contains(":") || string.IsNullOrWhiteSpace(owner))
            {
                return id;
            }

            return owner + ":" + id;
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
public sealed class SkillCgRegisteredEntryView
{
    public SkillCgRegisteredEntryView()
    {
    }

    public SkillCgRegisteredEntryView(AuraCgRegistryEntry entry, AuraCgActivationEntryState activation)
    {
        OwnerModId = entry.OwnerModId;
        CgId = entry.CgId;
        QualifiedCgId = entry.QualifiedCgId;
        DisplayName = entry.DisplayName;
        Kind = entry.Kind;
        TargetRoleIds = (entry.TargetRoleIds ?? new List<string>()).ToList();
        CardIds = (entry.CardIds ?? new List<string>()).ToList();
        MediaType = entry.Media.Type;
        Resource = entry.Media.Resource;
        FallbackImage = entry.Media.FallbackImage;
        BundlePath = entry.Media.BundlePath;
        BundleAssetPrefix = entry.Media.BundleAssetPrefix;
        FlashMode = entry.Media.FlashMode;
        Priority = entry.Priority;
        Enabled = activation.Enabled;
        ConsumerMode = activation.ConsumerMode;
        ConsumerModId = activation.ConsumerModId;
        UserOverridden = activation.UserOverridden;
    }

    public string OwnerModId { get; set; } = "";

    public string CgId { get; set; } = "";

    public string QualifiedCgId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Kind { get; set; } = "";

    public List<string> TargetRoleIds { get; set; } = new();

    public List<string> CardIds { get; set; } = new();

    public string MediaType { get; set; } = "";

    public string Resource { get; set; } = "";

    public string FallbackImage { get; set; } = "";

    public string BundlePath { get; set; } = "";

    public string BundleAssetPrefix { get; set; } = "";

    public string FlashMode { get; set; } = "";

    public int Priority { get; set; }

    public bool Enabled { get; set; }

    public string ConsumerMode { get; set; } = "";

    public string ConsumerModId { get; set; } = "";

    public bool UserOverridden { get; set; }
}

[Serializable]
public sealed class SkillCgTriggerContext
{
    public long ActionSequence { get; set; }

    public string Action { get; set; } = "";

    public string CardId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string OwnerRoleId { get; set; } = "";

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

    public string BundlePath { get; set; } = "";

    public string BundleAssetPrefix { get; set; } = "";

    public string MediaType { get; set; } = SkillCgMediaTypes.Image;

    public float FrameSeconds { get; set; } = 0.08f;

    public string AlphaMode { get; set; } = SkillCgAlphaModes.None;

    public float KeyThreshold { get; set; } = 0.03f;

    public float KeySoftness { get; set; } = 0.08f;

    public float FlashAtSeconds { get; set; } = -1f;

    public float FlashDuration { get; set; } = 0.18f;

    public string FlashMode { get; set; } = SkillCgFlashModes.Screen;

    public int FlashStartFrame { get; set; }

    public int FlashEndFrame { get; set; }

    public int FlashPulseEveryFrames { get; set; } = 1;

    public float FlashStrength { get; set; } = 0.82f;

    public int Priority { get; set; }

    public float FadeIn { get; set; } = 0.35f;

    public float Hold { get; set; } = 1f;

    public float FadeOut { get; set; } = 0.45f;

    public string PresentationMode { get; set; } = SkillCgPresentationModes.Slide;

    public string FitMode { get; set; } = SkillCgFitModes.Contain;

    public float FocusX { get; set; } = 0.5f;

    public float FocusY { get; set; } = 0.5f;

    public float SafeScale { get; set; } = 1f;

    public float CreatedAt { get; set; }

    public long ActionSequence { get; set; }

    public bool IsRemote { get; set; }

    public bool DisableSync { get; set; }

    public string DuplicateKey => ProviderId + "|" + OwnerInstanceId + "|" + CardId + "|" + (string.IsNullOrWhiteSpace(ImageResource) ? ImagePath : ImageResource) + "|" + BundlePath + "|" + BundleAssetPrefix + "|" + MediaType + "|" + FrameSeconds.ToString("0.###") + "|" + AlphaMode + "|" + FlashMode + "|" + FlashAtSeconds.ToString("0.###") + "|" + FlashStartFrame + "|" + FlashEndFrame + "|" + FlashPulseEveryFrames + "|" + PresentationMode + "|" + FitMode + "|" + FocusX.ToString("0.###") + "|" + FocusY.ToString("0.###") + "|" + SafeScale.ToString("0.###");

    public string QualifiedProviderId => QualifyProviderId(OwnerModId, ProviderId);

    public void Normalize()
    {
        ProviderId = string.IsNullOrWhiteSpace(ProviderId) ? "unknown" : ProviderId.Trim();
        OwnerModId = OwnerModId?.Trim() ?? "";
        CardId = CardId?.Trim() ?? "";
        OwnerInstanceId = OwnerInstanceId?.Trim() ?? "";
        ImagePath = ImagePath?.Trim() ?? "";
        ImageResource = ImageResource?.Trim() ?? "";
        BundlePath = NormalizeBundlePath(BundlePath);
        BundleAssetPrefix = NormalizeRequestRelativePath(BundleAssetPrefix);
        if (string.IsNullOrWhiteSpace(ImageResource) && !string.IsNullOrWhiteSpace(ImagePath))
        {
            ImageResource = Path.GetFileName(ImagePath);
        }

        MediaType = SkillCgMediaTypes.Normalize(MediaType);
        FrameSeconds = Mathf.Max(0.01f, FrameSeconds);
        AlphaMode = SkillCgAlphaModes.Normalize(AlphaMode);
        KeyThreshold = Mathf.Clamp01(KeyThreshold);
        KeySoftness = Mathf.Clamp(KeySoftness, 0.001f, 1f);
        FlashAtSeconds = FlashAtSeconds < 0f ? -1f : FlashAtSeconds;
        FlashDuration = Mathf.Clamp(FlashDuration <= 0f ? 0.18f : FlashDuration, 0.03f, 1f);
        FlashMode = SkillCgFlashModes.Normalize(FlashMode);
        FlashStartFrame = Math.Max(0, FlashStartFrame);
        FlashEndFrame = Math.Max(0, FlashEndFrame);
        if (FlashStartFrame > 0 && FlashEndFrame > 0 && FlashEndFrame < FlashStartFrame)
        {
            FlashEndFrame = FlashStartFrame;
        }

        FlashPulseEveryFrames = Math.Max(1, FlashPulseEveryFrames);
        FlashStrength = Mathf.Clamp01(FlashStrength <= 0f ? 0.82f : FlashStrength);
        FadeIn = Mathf.Max(0f, FadeIn);
        Hold = Mathf.Max(0f, Hold);
        FadeOut = Mathf.Max(0f, FadeOut);
        PresentationMode = SkillCgPresentationModes.Normalize(PresentationMode);
        FitMode = SkillCgFitModes.Normalize(FitMode);
        FocusX = Mathf.Clamp01(FocusX);
        FocusY = Mathf.Clamp01(FocusY);
        SafeScale = Mathf.Clamp(SafeScale <= 0f ? 1f : SafeScale, 1f, 3f);

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
            BundlePath = ReadString(type, source, "BundlePath", ""),
            BundleAssetPrefix = ReadString(type, source, "BundleAssetPrefix", ""),
            MediaType = ReadString(type, source, "MediaType", SkillCgMediaTypes.Image),
            FrameSeconds = ReadFloat(type, source, "FrameSeconds", 0.08f),
            AlphaMode = ReadString(type, source, "AlphaMode", SkillCgAlphaModes.None),
            KeyThreshold = ReadFloat(type, source, "KeyThreshold", 0.03f),
            KeySoftness = ReadFloat(type, source, "KeySoftness", 0.08f),
            FlashAtSeconds = ReadFloat(type, source, "FlashAtSeconds", -1f),
            FlashDuration = ReadFloat(type, source, "FlashDuration", 0.18f),
            FlashMode = ReadString(type, source, "FlashMode", SkillCgFlashModes.Screen),
            FlashStartFrame = ReadInt(type, source, "FlashStartFrame", 0),
            FlashEndFrame = ReadInt(type, source, "FlashEndFrame", 0),
            FlashPulseEveryFrames = ReadInt(type, source, "FlashPulseEveryFrames", 1),
            FlashStrength = ReadFloat(type, source, "FlashStrength", 0.82f),
            Priority = ReadInt(type, source, "Priority", priority),
            FadeIn = ReadFloat(type, source, "FadeIn", 0.35f),
            Hold = ReadFloat(type, source, "Hold", 1f),
            FadeOut = ReadFloat(type, source, "FadeOut", 0.45f),
            PresentationMode = ReadString(type, source, "PresentationMode", SkillCgPresentationModes.Slide),
            FitMode = ReadString(type, source, "FitMode", SkillCgFitModes.Contain),
            FocusX = ReadFloat(type, source, "FocusX", 0.5f),
            FocusY = ReadFloat(type, source, "FocusY", 0.5f),
            SafeScale = ReadFloat(type, source, "SafeScale", 1f),
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

    private static string QualifyProviderId(string ownerModId, string providerId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = (providerId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "unknown";
        }

        if (id.Contains(":") || string.IsNullOrWhiteSpace(owner))
        {
            return id;
        }

        return owner + ":" + id;
    }

    private static string NormalizeBundlePath(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static string NormalizeRequestRelativePath(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
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

    public string BundlePath { get; set; } = "";

    public string BundleAssetPrefix { get; set; } = "";

    public string MediaType { get; set; } = SkillCgMediaTypes.Image;

    public float FrameSeconds { get; set; } = 0.08f;

    public string AlphaMode { get; set; } = SkillCgAlphaModes.None;

    public float KeyThreshold { get; set; } = 0.03f;

    public float KeySoftness { get; set; } = 0.08f;

    public float FlashAtSeconds { get; set; } = -1f;

    public float FlashDuration { get; set; } = 0.18f;

    public string FlashMode { get; set; } = SkillCgFlashModes.Screen;

    public int FlashStartFrame { get; set; }

    public int FlashEndFrame { get; set; }

    public int FlashPulseEveryFrames { get; set; } = 1;

    public float FlashStrength { get; set; } = 0.82f;

    public int Priority { get; set; }

    public float FadeIn { get; set; } = 0.35f;

    public float Hold { get; set; } = 1f;

    public float FadeOut { get; set; } = 0.45f;

    public string PresentationMode { get; set; } = SkillCgPresentationModes.Slide;

    public string FitMode { get; set; } = SkillCgFitModes.Contain;

    public float FocusX { get; set; } = 0.5f;

    public float FocusY { get; set; } = 0.5f;

    public float SafeScale { get; set; } = 1f;

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
            BundlePath = request.BundlePath,
            BundleAssetPrefix = request.BundleAssetPrefix,
            MediaType = request.MediaType,
            FrameSeconds = request.FrameSeconds,
            AlphaMode = request.AlphaMode,
            KeyThreshold = request.KeyThreshold,
            KeySoftness = request.KeySoftness,
            FlashAtSeconds = request.FlashAtSeconds,
            FlashDuration = request.FlashDuration,
            FlashMode = request.FlashMode,
            FlashStartFrame = request.FlashStartFrame,
            FlashEndFrame = request.FlashEndFrame,
            FlashPulseEveryFrames = request.FlashPulseEveryFrames,
            FlashStrength = request.FlashStrength,
            Priority = request.Priority,
            FadeIn = request.FadeIn,
            Hold = request.Hold,
            FadeOut = request.FadeOut,
            PresentationMode = request.PresentationMode,
            FitMode = request.FitMode,
            FocusX = request.FocusX,
            FocusY = request.FocusY,
            SafeScale = request.SafeScale,
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
            BundlePath = Event.BundlePath,
            BundleAssetPrefix = Event.BundleAssetPrefix,
            MediaType = Event.MediaType,
            FrameSeconds = Event.FrameSeconds,
            AlphaMode = Event.AlphaMode,
            KeyThreshold = Event.KeyThreshold,
            KeySoftness = Event.KeySoftness,
            FlashAtSeconds = Event.FlashAtSeconds,
            FlashDuration = Event.FlashDuration,
            FlashMode = Event.FlashMode,
            FlashStartFrame = Event.FlashStartFrame,
            FlashEndFrame = Event.FlashEndFrame,
            FlashPulseEveryFrames = Event.FlashPulseEveryFrames,
            FlashStrength = Event.FlashStrength,
            Priority = Event.Priority,
            FadeIn = Event.FadeIn,
            Hold = Event.Hold,
            FadeOut = Event.FadeOut,
            PresentationMode = Event.PresentationMode,
            FitMode = Event.FitMode,
            FocusX = Event.FocusX,
            FocusY = Event.FocusY,
            SafeScale = Event.SafeScale,
            CreatedAt = Time.unscaledTime,
            ActionSequence = Event.ActionSequence,
            IsRemote = true,
            DisableSync = true
        });
    }
}

public static class SkillCgMediaTypes
{
    public const string Image = "image";
    public const string Sequence = "sequence";

    public static string Normalize(string? value)
    {
        var type = value?.Trim() ?? "";
        if (string.Equals(type, Sequence, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "frames", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "pngSequence", StringComparison.OrdinalIgnoreCase))
        {
            return Sequence;
        }

        return Image;
    }
}

public static class SkillCgAlphaModes
{
    public const string None = "none";
    public const string BlackKey = "blackKey";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, BlackKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "lumaKey", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "black", StringComparison.OrdinalIgnoreCase))
        {
            return BlackKey;
        }

        return None;
    }
}

public static class SkillCgFlashModes
{
    public const string Screen = "screen";
    public const string MaskedInvert = "maskedInvert";
    public const string ScreenBwPulse = "screenBwPulse";
    public const string HybridBwPulse = "hybridBwPulse";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, HybridBwPulse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "hybridBlackWhite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "hybridBw", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "blackWhiteImpact", StringComparison.OrdinalIgnoreCase))
        {
            return HybridBwPulse;
        }

        if (string.Equals(mode, ScreenBwPulse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "screenBlackWhite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "blackWhiteScreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "bwPulse", StringComparison.OrdinalIgnoreCase))
        {
            return ScreenBwPulse;
        }

        if (string.Equals(mode, MaskedInvert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "objectInvert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "maskedDifference", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "difference", StringComparison.OrdinalIgnoreCase))
        {
            return MaskedInvert;
        }

        return Screen;
    }
}

public static class SkillCgPresentationModes
{
    public const string Slide = "slide";
    public const string FullscreenFade = "fullscreenFade";
    public const string CenterFade = "centerFade";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, FullscreenFade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullScreenFade", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullscreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullScreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fade", StringComparison.OrdinalIgnoreCase))
        {
            return FullscreenFade;
        }

        if (string.Equals(mode, CenterFade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "center", StringComparison.OrdinalIgnoreCase))
        {
            return CenterFade;
        }

        return Slide;
    }
}

public static class SkillCgFitModes
{
    public const string Contain = "contain";
    public const string Cover = "cover";
    public const string Stretch = "stretch";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, Cover, StringComparison.OrdinalIgnoreCase))
        {
            return Cover;
        }

        if (string.Equals(mode, Stretch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fill", StringComparison.OrdinalIgnoreCase))
        {
            return Stretch;
        }

        return Contain;
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


