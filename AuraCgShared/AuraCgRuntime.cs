using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCg.Shared;

public static class SkillCgArbiterRuntime
{
    public const string SkillCgKind = "skill";
    public const string CardUseCgKind = "cardUse";
    public const string FeastCgKind = "feast";
    private const string GlobalObjectName = "AuraCg.Global";
    private const string ComponentFullName = "AuraCg.Shared.SkillCgArbiterRuntime+SkillCgArbiterComponent";
    public const string CurrentBuildId = "aura-cg-shared-2026-07-10-v11";
    public const int CurrentProtocolVersion = 9;
    public const int MinimumSupportedProtocolVersion = CurrentProtocolVersion;
    private const int MaxPreloadSubmissionItems = 256;
    private const string DefaultNetworkOwner = "AuraCgShared";
    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityErrorsShown = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DataDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Material> RegisteredMaterials = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, AssetBundle> RegisteredBundles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AuraCgRegisteredRequestResolver RegisteredRequestResolver = new(
        ownerModId => AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId),
        entry => AuraCgActivationRuntime.GetEffectiveState(entry).Enabled,
        (ownerModId, imageResource) => ResolveImagePath(ownerModId, imageResource),
        () => Time.unscaledTime,
        AuraCgLog.WarnOnce,
        SkillCgKind,
        CardUseCgKind,
        AuraCgNetworkRuntime.MaximumIdentifierLength);

    public static void Initialize(ModConfig? modConfig, string ownerModId, SkillCgArbiterOptions? options = null)
    {
        if (modConfig != null)
        {
            AuraSharedRuntime.Initialize(modConfig, ownerModId);
            DataDirectories[ownerModId] = AuraSharedPaths.RootDirectory;
            AuraCgRpcAuthorityRuntime.Initialize(modConfig);
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
        RequestCg(ownerModId, request, syncRemote: false);
    }

    public static void RequestCg(string ownerModId, SkillCgRequest request, bool syncRemote)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerModId))
        {
            request.OwnerModId = ownerModId;
        }

        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, syncRemote ? "RequestCgAndSync" : "RequestCg", request);
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
        var submission = AuraCgPreloadSubmission<SkillCgRequest>.Capture(requests, MaxPreloadSubmissionItems);
        var batch = submission.Items;
        if (submission.Truncated)
        {
            AuraCgLog.WarnOnce(
                "preload-submission-limit:" + ownerModId,
                "CG preload submission truncated before dispatch. owner=" + ownerModId
                + ", max=" + MaxPreloadSubmissionItems);
        }

        if (batch.Count == 0)
        {
            return;
        }

        var producerId = string.IsNullOrWhiteSpace(ownerModId) ? DefaultNetworkOwner : ownerModId.Trim();
        foreach (var request in batch)
        {
            if (request != null)
            {
                request.PreloadProducerId = producerId;
            }
        }

        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "PreloadCg", batch);
    }

    public static void EnsureAdventurePreloaded(
        string consumerModId,
        string ownerModId,
        string adventureKey,
        IEnumerable<string> kinds,
        string roleId = "")
    {
        var normalizedKinds = (kinds ?? Array.Empty<string>())
            .Select(kind => (kind ?? "").Trim())
            .Where(kind => kind.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedKinds.Length == 0)
        {
            return;
        }

        var requests = BuildRegisteredCgPreloadRequests(consumerModId, ownerModId, roleId, normalizedKinds);
        if (requests.Count == 0)
        {
            return;
        }

        var producerId = string.IsNullOrWhiteSpace(consumerModId) ? DefaultNetworkOwner : consumerModId.Trim();
        foreach (var request in requests)
        {
            request.PreloadProducerId = producerId;
        }

        var arbiter = EnsureArbiter(consumerModId);
        Invoke(arbiter, "EnsureAdventurePreloaded", new SkillCgAdventurePreloadRequest(
            string.IsNullOrWhiteSpace(adventureKey) ? "default" : adventureKey.Trim(),
            requests));
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
        return RegisteredRequestResolver.BuildRequest(
            entry,
            kind,
            context,
            AuraCgActivationRuntime.CanConsumerPlay(entry, consumerModId),
            disableSync);
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
        var requests = BuildRegisteredCgPreloadRequests(consumerModId, ownerModId, roleId, kind);
        PreloadCg(consumerModId, requests);
    }

    private static List<SkillCgRequest> BuildRegisteredCgPreloadRequests(
        string consumerModId,
        string ownerModId,
        string roleId,
        params string[] kinds)
    {
        var kindSet = new HashSet<string>(
            (kinds ?? Array.Empty<string>())
            .Select(kind => (kind ?? "").Trim())
            .Where(kind => kind.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (kindSet.Count == 0)
        {
            return new List<SkillCgRequest>();
        }

        return AuraCgRegistryRuntime.GetRegisteredEntries(ownerModId)
            .Where(entry => kindSet.Any(kind => IsRegisteredCgEntry(entry, kind)))
            .Where(entry => string.IsNullOrWhiteSpace(roleId) || EntryMatchesRole(entry, roleId))
            .Where(entry => !string.Equals(entry.Kind, CardUseCgKind, StringComparison.OrdinalIgnoreCase) || EntryMatchesEnabledRuntimeCardPack(entry))
            .Where(entry => AuraCgActivationRuntime.CanConsumerPlay(entry, consumerModId))
            .Select(entry => CreateRegisteredRequest(entry, ResolveRegisteredImageResource(entry), ResolveImagePath(entry.OwnerModId, ResolveRegisteredImageResource(entry)), new SkillCgTriggerContext
            {
                CardId = (entry.CardIds ?? new List<string>()).FirstOrDefault() ?? "*",
                OwnerRoleId = roleId,
                CreatedAt = Time.unscaledTime
            }, disableSync: true))
            .Where(request => request != null)
            .Cast<SkillCgRequest>()
            .ToList();
    }

    private static bool EntryMatchesEnabledRuntimeCardPack(AuraCgRegistryEntry entry)
    {
        var enabledPacks = ReadRuntimeCardPacks();
        if (enabledPacks.Count == 0)
        {
            return true;
        }

        foreach (var cardId in entry.CardIds ?? new List<string>())
        {
            var pack = ResolveCardPack(cardId);
            if (string.IsNullOrWhiteSpace(pack) || enabledPacks.Contains(pack))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> ReadRuntimeCardPacks()
    {
        try
        {
            var packs = Singleton<GameRuntimeData>.Instance?.UseCardPack;
            return packs == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(packs, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ResolveCardPack(string cardId)
    {
        var id = (cardId ?? "").Trim().TrimStart('*');
        if (id.Length == 0 || string.Equals(id, "*", StringComparison.Ordinal))
        {
            return "";
        }

        try
        {
            var row = Singleton<GameConfigManager>.Instance?.GetOne(DataType.Card, id);
            return row != null && row.TryGetValue("PackBelong", out var pack)
                ? pack?.Trim() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsRegisteredCgEntry(AuraCgRegistryEntry entry, string kind)
    {
        return AuraCgRegistryQueryService.IsRegisteredEntry(entry, kind);
    }

    private static SkillCgRequest CreateRegisteredRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        SkillCgTriggerContext context,
        bool disableSync)
    {
        return RegisteredRequestResolver.CreateRequest(
            entry,
            imageResource,
            imagePath,
            context,
            disableSync);
    }

    private static string ResolveRegisteredImageResource(AuraCgRegistryEntry entry)
    {
        return AuraCgRegistryQueryService.ResolveImageResource(entry);
    }

    private static bool EntryMatchesRole(AuraCgRegistryEntry entry, string roleId)
    {
        return AuraCgRegistryQueryService.MatchesRole(entry, roleId);
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
        return AuraCgMediaPathResolver.NormalizeRelativeResourcePath(value);
    }

    private static string NormalizeBundleId(string value)
    {
        return AuraCgMediaPathResolver.NormalizeBundleId(value);
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
        var clearRequest = new SkillCgClearRequest(ownerModId, reason);
        if (!Invoke(existing, "ClearOwner", clearRequest))
        {
            Invoke(existing, "ClearQueue", reason);
        }
    }

    public static void BeginFightSession(string ownerModId, string reason)
    {
        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "BeginFightSession", new SkillCgFightSessionRequest(ownerModId, reason));
    }

    internal static void ApplyServerPlaybackRequest(SkillCgPlaybackSnapshot playback, AuraCgRpcSender sender)
    {
        var ownerModId = FirstOwnerModId(playback) ?? DefaultNetworkOwner;
        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "ApplyServerPlaybackRequest", new SkillCgServerPlaybackEnvelope(playback, sender));
    }

    internal static void ApplyNetworkPlayback(SkillCgPlaybackSnapshot playback, string source)
    {
        var ownerModId = FirstOwnerModId(playback) ?? DefaultNetworkOwner;
        var arbiter = EnsureArbiter(ownerModId);
        Invoke(arbiter, "ApplyNetworkPlayback", new SkillCgNetworkPlaybackEnvelope(playback, source));
    }

    internal static void ApplyFightSession(string ownerModId, string fightToken, string source)
    {
        var arbiter = EnsureArbiter(string.IsNullOrWhiteSpace(ownerModId) ? DefaultNetworkOwner : ownerModId);
        Invoke(arbiter, "ApplyFightSession", new SkillCgFightSessionRequest(ownerModId, source, fightToken));
    }

    private static string? FirstOwnerModId(SkillCgPlaybackSnapshot? playback)
    {
        return playback?.Events?
            .Select(item => item?.OwnerModId ?? "")
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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
        var methodsPresent = new[] { "Configure", "RegisterProvider", "Trigger", "RequestCg", "PreloadCg", "EnsureAdventurePreloaded", "ClearQueue" }
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

    private static bool Invoke(object? target, string methodName, object? argument)
    {
        if (target == null)
        {
            return false;
        }

        var method = target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (method == null)
        {
            return false;
        }

        method.Invoke(target, new[] { argument });
        return true;
    }

    private sealed class SkillCgClearRequest
    {
        public SkillCgClearRequest(string ownerModId, string? reason)
        {
            OwnerModId = (ownerModId ?? "").Trim();
            var normalizedReason = reason?.Trim() ?? "";
            Reason = string.IsNullOrWhiteSpace(normalizedReason) ? "<none>" : normalizedReason;
        }

        public string OwnerModId { get; }

        public string Reason { get; }
    }

    public sealed class SkillCgAdventurePreloadRequest
    {
        public SkillCgAdventurePreloadRequest(string key, IReadOnlyList<SkillCgRequest> requests)
        {
            Key = string.IsNullOrWhiteSpace(key) ? "default" : key.Trim();
            Requests = requests ?? Array.Empty<SkillCgRequest>();
        }

        public string Key { get; }

        public IReadOnlyList<SkillCgRequest> Requests { get; }
    }

    public sealed class SkillCgArbiterComponent : MonoBehaviour
    {
        private const int MaxAdventurePreloadKeys = 128;
        private const int MaxPendingPreloads = 128;
        private const int MaxPendingPreloadsPerOwner = 64;
        private const int MaxConcurrentPreloads = 2;
        private const int MaxPreloadStartsPerFrame = 1;
        private const float ClearDeduplicateSeconds = 1.0f;
        private readonly AuraCgProviderCoordinator providerCoordinator = new(SkillCgRequest.FromObject);
        private readonly AuraCgPlaybackCoordinator playbackCoordinator = new();
        private readonly AuraCgAdventurePreloadHistory adventurePreloadHistory = new(MaxAdventurePreloadKeys);
        private readonly AuraCgPreloadScheduler<SkillCgRequest> preloadScheduler = new(
            MaxPendingPreloads,
            MaxPendingPreloadsPerOwner,
            MaxConcurrentPreloads);
        private AuraCgOverlayPresenter overlayPresenter = null!;
        private AuraCgUnityMediaRepository mediaRepository = null!;
        private AuraCgNetworkRuntime networkRuntime = null!;
        private SkillCgArbiterOptions options = new();
        private string lastClearKind = "";
        private float lastClearAt = -999f;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => SkillCgArbiterRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

        private void Awake()
        {
            networkRuntime = new AuraCgNetworkRuntime(RegisteredRequestResolver.ResolveNetworkRequest);
            overlayPresenter = new AuraCgOverlayPresenter(this, ResolveRegisteredMaterial);
            mediaRepository = new AuraCgUnityMediaRepository(
                ResolveRegisteredBundle,
                bundleId => ResolveImagePath("", bundleId, bundleId),
                overlayPresenter.ShouldApplyCpuAlphaMode);
        }

        private static AssetBundle? ResolveRegisteredBundle(string bundleId)
        {
            return RegisteredBundles.TryGetValue(bundleId, out var bundle) ? bundle : null;
        }

        private static Material? ResolveRegisteredMaterial(string materialId)
        {
            return RegisteredMaterials.TryGetValue(materialId, out var material) ? material : null;
        }

        private void OnDestroy()
        {
            overlayPresenter?.Destroy();
        }

        private void Update()
        {
            if (playbackCoordinator.IsPlaying)
            {
                return;
            }

            foreach (var work in preloadScheduler.TakeReady(MaxPreloadStartsPerFrame))
            {
                if (IsPreloaded(work.Request))
                {
                    preloadScheduler.Complete(work.Key);
                    FlushReleasedMedia();
                    continue;
                }

                try
                {
                    StartCoroutine(PreloadRequest(work.Request, work.Key));
                }
                catch (Exception ex)
                {
                    preloadScheduler.Complete(work.Key);
                    AuraCgLog.WarnOnce(
                        "preload-start-failed:" + work.Key,
                        "CG preload coroutine failed to start: owner=" + work.OwnerId + ", error=" + ex.Message);
                    FlushReleasedMedia();
                }
            }
        }

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
                DuplicateWindowSeconds = Mathf.Max(options.DuplicateWindowSeconds, normalized.DuplicateWindowSeconds)
            }.Normalized();
            AuraCgLog.InfoOnce(
                "arbiter-configured",
                "CG queue configured. maxQueue=" + options.MaxQueueLength
                + ", maxAge=" + options.MaxRequestAgeSeconds.ToString("0.##") + "s"
                + ", duplicateWindow=" + options.DuplicateWindowSeconds.ToString("0.##") + "s");
        }

        public void RegisterProvider(object? provider)
        {
            var result = providerCoordinator.Register(provider);
            switch (result.Status)
            {
                case AuraCgProviderRegistrationStatus.Registered:
                    AuraCgLog.InfoOnce("provider:" + result.ProviderId, "CG provider registered: " + result.Description);
                    break;
                case AuraCgProviderRegistrationStatus.NullProvider:
                    AuraCgLog.WarnOnce("provider-null", "Provider registration skipped: provider is null.");
                    break;
                case AuraCgProviderRegistrationStatus.EmptyProviderId:
                    AuraCgLog.WarnOnce("provider-empty-id:" + result.ProviderType, "Provider registration skipped: ProviderId is empty.");
                    break;
                case AuraCgProviderRegistrationStatus.Failed:
                    AuraCgLog.WarnOnce("provider-failed:" + result.ProviderType, "Provider registration failed: " + result.Error);
                    break;
            }
        }

        public void Trigger(object? value)
        {
            if (value is not SkillCgTriggerContext context)
            {
                return;
            }

            var batch = providerCoordinator.BuildRequests(context, LogProviderBuildFailure);

            if (batch.Count == 0)
            {
                return;
            }

            if (!QueueLocalRequests(batch))
            {
                return;
            }
        }

        private static void LogProviderBuildFailure(AuraCgProviderBuildFailure failure)
        {
            AuraCgLog.WarnOnce(
                "provider-build-failed:" + failure.ProviderId,
                "Provider BuildRequests failed once: " + failure.ProviderId + " -> " + failure.Exception.Message);
            AuraCgLog.DebugLog("Provider BuildRequests exception: " + failure.Exception);
        }

        public void RequestCg(object? value)
        {
            if (value is not SkillCgRequest request)
            {
                return;
            }

            if (TryEnqueue(request))
            {
                StartPlaybackIfNeeded();
            }
        }

        public void RequestCgAndSync(object? value)
        {
            if (value is not SkillCgRequest request)
            {
                return;
            }

            if (!QueueLocalRequests(new[] { request }))
            {
                return;
            }
        }

        public void PreloadCg(object? value)
        {
            if (value is not IEnumerable<SkillCgRequest> requests)
            {
                return;
            }

            var inspected = 0;
            foreach (var request in requests)
            {
                if (inspected >= MaxPreloadSubmissionItems)
                {
                    AuraCgLog.WarnOnce(
                        "preload-component-submission-limit",
                        "CG preload component submission truncated. max=" + MaxPreloadSubmissionItems);
                    break;
                }

                inspected++;
                if (request == null)
                {
                    continue;
                }

                request.Normalize();
                var key = AuraCgMediaCacheKeys.Preload(request);
                var owner = PreloadOwner(request);
                var result = preloadScheduler.TryEnqueue(key, owner, request, IsPreloaded(request));
                if (result == AuraCgPreloadEnqueueResult.CapacityExceeded)
                {
                    AuraCgLog.WarnOnce(
                        "preload-capacity:" + owner,
                        "CG preload queue capacity reached; excess noncritical preloads are dropped. owner=" + owner
                        + ", pending=" + preloadScheduler.PendingCount
                        + ", ownerPending=" + preloadScheduler.GetOwnerPendingCount(owner));
                }
            }
        }

        public void EnsureAdventurePreloaded(object? value)
        {
            if (value is not SkillCgAdventurePreloadRequest request)
            {
                return;
            }

            if (!adventurePreloadHistory.TryBegin(request.Key))
            {
                AuraCgLog.DebugLog("Adventure CG preload skipped; already queued. key=" + request.Key);
                return;
            }

            AuraCgLog.DebugLog("Adventure CG preload queued. key=" + request.Key + ", count=" + request.Requests.Count);
            PreloadCg(request.Requests);
        }

        private IEnumerator PreloadRequest(SkillCgRequest request, string key)
        {
            try
            {
                if (!string.Equals(request.MediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
                {
                    Sprite? sprite = null;
                    yield return mediaRepository.LoadSprite(request.ImagePath, result => sprite = result);
                    if (sprite != null)
                    {
                        AuraCgLog.InfoOnce(
                            "image-preloaded:" + key,
                            "CG image preloaded: provider=" + request.ProviderId
                            + ", image=" + Path.GetFileName(request.ImagePath));
                    }

                    yield break;
                }

                List<Sprite> sprites = new();
                yield return mediaRepository.LoadSequenceSprites(request, result => sprites = result);
                if (sprites.Count > 0)
                {
                    AuraCgLog.InfoOnce(
                        "sequence-preloaded:" + key,
                        "CG sequence preloaded: provider=" + request.ProviderId
                        + ", frames=" + sprites.Count
                        + ", bundle=" + (string.IsNullOrWhiteSpace(request.BundlePath) ? "<file>" : request.BundlePath));
                }
            }
            finally
            {
                preloadScheduler.Complete(key);
                FlushReleasedMedia();
            }
        }

        private static string PreloadOwner(SkillCgRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.PreloadProducerId))
            {
                return request.PreloadProducerId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.OwnerModId))
            {
                return request.OwnerModId.Trim();
            }

            return string.IsNullOrWhiteSpace(request.ProviderId)
                ? DefaultNetworkOwner
                : request.ProviderId.Trim();
        }

        private bool IsPreloaded(SkillCgRequest request)
        {
            return mediaRepository.IsPreloaded(request);
        }

        public void ClearQueue(object? reason)
        {
            ClearTransientPlayback(reason as string ?? "<none>");
        }

        public void BeginFightSession(object? value)
        {
            networkRuntime.BeginFightSession(value, ClearTransientPlayback);
        }

        public void ApplyFightSession(object? value)
        {
            networkRuntime.ApplyFightSession(value, ClearTransientPlayback);
        }

        public void ClearOwner(object? value)
        {
            if (value is SkillCgClearRequest request)
            {
                ClearTransientPlayback(request.Reason);
                return;
            }

            ClearTransientPlayback(value as string ?? "<none>");
        }

        private void ClearTransientPlayback(string reason)
        {
            if (ShouldSkipDuplicateClear(reason))
            {
                return;
            }

            playbackCoordinator.Clear();
            networkRuntime.ResetTransient();
            overlayPresenter.Hide();
            FlushReleasedMedia();

            AuraCgLog.DebugLog("CG queue cleared: " + reason);
        }

        private bool ShouldSkipDuplicateClear(string reason)
        {
            var kind = NormalizeClearKind(reason);
            var now = Time.unscaledTime;
            if (string.Equals(kind, lastClearKind, StringComparison.Ordinal)
                && now - lastClearAt <= ClearDeduplicateSeconds)
            {
                AuraCgLog.DebugLog("Duplicate CG clear skipped: " + reason);
                return true;
            }

            lastClearKind = kind;
            lastClearAt = now;
            return false;
        }

        private static string NormalizeClearKind(string reason)
        {
            var value = (reason ?? "").Trim().ToLowerInvariant();
            if (value.Contains("fight start"))
            {
                return "fight-start";
            }

            if (value.Contains("fight ended") || value.Contains("fight ending"))
            {
                return "fight-end";
            }

            if (value.Contains("disabled"))
            {
                return "disabled";
            }

            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }

        private bool QueueLocalRequests(IReadOnlyList<SkillCgRequest> requests)
        {
            var batch = (requests ?? Array.Empty<SkillCgRequest>())
                .Where(request => request != null)
                .ToList();
            if (batch.Count == 0 || batch.Count > AuraCgNetworkRuntime.MaximumEventsPerPlayback)
            {
                if (batch.Count > AuraCgNetworkRuntime.MaximumEventsPerPlayback)
                {
                    AuraCgLog.WarnOnce("playback-batch-too-large", "Skill CG playback skipped: event count exceeds network budget.");
                }
                return false;
            }

            SkillCgPlaybackSnapshot? playback = null;
            var syncBatch = batch
                .Where(request => !request.DisableSync && !request.IsRemote)
                .ToList();
            if (syncBatch.Count > 0
                && !networkRuntime.TryPrepareLocalPlaybackBatch(
                    syncBatch,
                    options.DuplicateWindowSeconds,
                    out playback))
            {
                return false;
            }

            var accepted = EnqueueBatch(batch);
            if (accepted <= 0 && playback == null)
            {
                return false;
            }

            if (playback != null)
            {
                networkRuntime.RelayPlayback(playback);
            }

            if (accepted > 0)
            {
                StartPlaybackIfNeeded();
            }

            return accepted > 0 || playback != null;
        }

        private int EnqueueBatch(IEnumerable<SkillCgRequest> requests)
        {
            var accepted = 0;
            foreach (var request in requests ?? Array.Empty<SkillCgRequest>())
            {
                if (request != null && TryEnqueue(request))
                {
                    accepted++;
                }
            }

            return accepted;
        }

        public void ApplyServerPlaybackRequest(object? value)
        {
            networkRuntime.ApplyServerPlaybackRequest(value, EnqueueNetworkPlayback);
        }

        public void ApplyNetworkPlayback(object? value)
        {
            networkRuntime.ApplyNetworkPlayback(value, EnqueueNetworkPlayback);
        }

        private void EnqueueNetworkPlayback(IReadOnlyList<SkillCgRequest> requests)
        {
            EnqueueBatch(requests);
            if (playbackCoordinator.QueueCount > 0)
            {
                StartPlaybackIfNeeded();
            }
        }

        private bool TryEnqueue(SkillCgRequest request)
        {
            request.Normalize();
            var result = playbackCoordinator.TryEnqueue(
                request,
                Time.unscaledTime,
                options.MaxQueueLength,
                options.DuplicateWindowSeconds,
                out var droppedCount);
            if (result == AuraCgPlaybackEnqueueResult.EmptyMedia)
            {
                AuraCgLog.WarnOnce("empty-media:" + request.ProviderId, "CG request skipped: media path is empty. provider=" + request.ProviderId);
                return false;
            }

            if (result == AuraCgPlaybackEnqueueResult.Duplicate)
            {
                AuraCgLog.DebugLog("Duplicate CG request skipped: " + request.DuplicateKey);
                return false;
            }

            if (result != AuraCgPlaybackEnqueueResult.Accepted)
            {
                return false;
            }

            if (droppedCount > 0)
            {
                AuraCgLog.WarnOnce("queue-full", "CG queue is full; oldest pending CG requests will be dropped. max=" + options.MaxQueueLength);
            }

            AuraCgLog.DebugLog("CG queued: provider=" + request.ProviderId + ", card=" + request.CardId + ", queue=" + playbackCoordinator.QueueCount);
            return true;
        }

        private void StartPlaybackIfNeeded()
        {
            if (!playbackCoordinator.TryBegin(out var generation))
            {
                return;
            }

            try
            {
                StartCoroutine(PlayQueue(generation));
            }
            catch
            {
                playbackCoordinator.Complete(generation);
                throw;
            }
        }

        private IEnumerator PlayQueue(int generation)
        {
            while (playbackCoordinator.IsCurrent(generation))
            {
                var hasNext = playbackCoordinator.TryTakeNext(
                    generation,
                    Time.unscaledTime,
                    options.MaxRequestAgeSeconds,
                    out var request,
                    out var staleSkipped);
                if (staleSkipped > 0)
                {
                    AuraCgLog.WarnOnce("request-stale", "Stale CG requests are being skipped. maxAge=" + options.MaxRequestAgeSeconds.ToString("0.##") + "s");
                }

                if (!hasNext || request == null)
                {
                    break;
                }

                yield return PlayRequest(request, generation);
            }

            if (playbackCoordinator.Complete(generation))
            {
                FlushReleasedMedia();
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
            yield return mediaRepository.LoadSprite(request.ImagePath, result =>
            {
                sprite = result;
                spriteReady = true;
            });

            if (!spriteReady || sprite == null)
            {
                yield break;
            }

            if (!playbackCoordinator.IsCurrent(generation))
            {
                yield break;
            }

            if (!overlayPresenter.ShowImage(sprite, request))
            {
                yield break;
            }

            AuraCgLog.DebugLog(
                "CG play: provider=" + request.ProviderId
                + ", card=" + request.CardId
                + ", image=" + Path.GetFileName(request.ImagePath)
                + ", mode=" + request.PresentationMode
                + ", fit=" + request.FitMode);

            yield return overlayPresenter.PlayImage(
                sprite,
                request,
                () => playbackCoordinator.IsCurrent(generation));

            if (!playbackCoordinator.IsCurrent(generation))
            {
                yield break;
            }

            overlayPresenter.Hide();
        }

        private IEnumerator PlaySequenceRequest(SkillCgRequest request, int generation)
        {
            var spritesReady = false;
            List<Sprite> sprites = new();
            yield return mediaRepository.LoadSequenceSprites(request, result =>
            {
                sprites = result;
                spritesReady = true;
            }, () => playbackCoordinator.IsCurrent(generation));

            if (!spritesReady || sprites.Count == 0)
            {
                yield break;
            }

            if (!playbackCoordinator.IsCurrent(generation))
            {
                yield break;
            }

            if (!overlayPresenter.ShowSequence(sprites, request))
            {
                yield break;
            }

            AuraCgLog.DebugLog(
                "CG play sequence: provider=" + request.ProviderId
                + ", card=" + request.CardId
                + ", frames=" + sprites.Count
                + ", frameSeconds=" + request.FrameSeconds.ToString("0.###")
                + ", fit=" + request.FitMode);

            yield return overlayPresenter.PlaySequence(
                sprites,
                request,
                () => playbackCoordinator.IsCurrent(generation),
                mediaRepository.CreateInvertedSprite);

            if (!playbackCoordinator.IsCurrent(generation))
            {
                yield break;
            }

            overlayPresenter.Hide();
        }

        private void FlushReleasedMedia()
        {
            mediaRepository.FlushReleasedMedia(
                !playbackCoordinator.IsPlaying && preloadScheduler.ActiveCount == 0);
        }

    }

}
