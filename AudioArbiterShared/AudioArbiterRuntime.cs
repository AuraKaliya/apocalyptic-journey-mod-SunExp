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
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AudioArbiter.Shared;

public static class AudioArbiterRuntime
{
    private const string GlobalObjectName = "AudioArbiter.Global";
    private const string ComponentFullName = "AudioArbiter.Shared.AudioArbiterRuntime+AudioArbiterComponent";
    public const string CurrentBuildId = "audio-arbiter-2026-07-08-v6";
    public const int CurrentProtocolVersion = 4;
    public const int MinimumSupportedProtocolVersion = 4;
    public const int SupportedManifestSchemaVersion = 2;

    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityErrorsShown = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig, string ownerModId)
    {
        EnsureArbiter(modConfig, ownerModId);
    }

    public static void RegisterSoundProvider(ModConfig modConfig, string ownerModId, object provider)
    {
        var arbiter = EnsureArbiter(modConfig, ownerModId);
        if (arbiter == null)
        {
            return;
        }

        try
        {
            arbiter.GetType().GetMethod("RegisterSoundProvider", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(arbiter, new[] { provider });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Sound provider registration failed for " + ownerModId + ": " + ex.Message);
        }
    }

    public static bool RegisterManifest(ModConfig modConfig, string ownerModId, string manifestRelativePath = "audio.registry.json")
    {
        var arbiter = EnsureArbiter(modConfig, ownerModId);
        if (arbiter == null)
        {
            return false;
        }

        try
        {
            var method = arbiter.GetType().GetMethod("RegisterManifest", BindingFlags.Instance | BindingFlags.Public);
            return method != null
                && method.Invoke(arbiter, new object[] { modConfig, ownerModId, manifestRelativePath }) is bool registered
                && registered;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Manifest registration failed for " + ownerModId + ": " + ex.Message);
            return false;
        }
    }

    public static bool RequestSound(SoundPlaybackRequest request)
    {
        var arbiter = EnsureArbiter(request.ModConfig, request.OwnerModId);
        if (arbiter == null)
        {
            return false;
        }

        try
        {
            var method = arbiter.GetType().GetMethod("RequestSound", BindingFlags.Instance | BindingFlags.Public);
            return method != null && method.Invoke(arbiter, new object[] { request }) is bool played && played;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Sound request failed for " + request.OwnerModId + ": " + ex.Message);
            return false;
        }
    }

    public static string ReadString(object? source, string propertyName)
    {
        return PropertyReader.ReadString(source, propertyName);
    }

    public static int ReadInt(object? source, string propertyName, int fallback = 0)
    {
        return PropertyReader.ReadInt(source, propertyName, fallback);
    }

    public static long ReadLong(object? source, string propertyName, long fallback = 0L)
    {
        return PropertyReader.ReadLong(source, propertyName, fallback);
    }

    public static float ReadFloat(object? source, string propertyName, float fallback = 0f)
    {
        return PropertyReader.ReadFloat(source, propertyName, fallback);
    }

    public static bool ReadBool(object? source, string propertyName, bool fallback = false)
    {
        return PropertyReader.ReadBool(source, propertyName, fallback);
    }

    private static object? EnsureArbiter(ModConfig? modConfig, string ownerModId)
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            var existing = FindArbiterComponent(gameObject);
            if (existing != null)
            {
                if (!ValidateExistingArbiter(existing, ownerModId))
                {
                    return null;
                }

                if (ReuseLogOwners.Add(ownerModId))
                {
                    AuraSharedLog.DebugLog(
                        "AudioArbiter",
                        "Reusing global arbiter for " + ownerModId
                        + ", ownerType=" + existing.GetType().Assembly.GetName().Name,
                        false);
                }

                TryInitializeExisting(existing, modConfig, ownerModId);
                return existing;
            }
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var component = gameObject.AddComponent<AudioArbiterComponent>();
        component.InitializeOwner(modConfig, ownerModId);
        AuraSharedLog.DebugLog("AudioArbiter", "Created global arbiter, owner=" + ownerModId, false);
        return component;
    }

    private static bool ValidateExistingArbiter(object existing, string ownerModId)
    {
        var type = existing.GetType();
        var protocolVersion = ReadIntProperty(existing, "ProtocolVersion", 0);
        var minimumSupported = ReadIntProperty(existing, "MinimumSupportedProtocolVersion", int.MaxValue);
        var buildId = ReadStringProperty(existing, "BuildId");
        var methodsPresent = new[] { "RegisterSoundProvider", "RegisterManifest", "RequestSound" }
            .All(name => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null);
        var compatible = protocolVersion >= MinimumSupportedProtocolVersion
            && minimumSupported <= CurrentProtocolVersion
            && methodsPresent;

        if (!compatible && CompatibilityErrorsShown.Add(ownerModId + ":" + type.AssemblyQualifiedName))
        {
            Debug.LogError("[AudioArbiter] Incompatible global arbiter; audio features disabled for " + ownerModId
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
            Debug.LogWarning("[AudioArbiter] Reusing protocol-compatible arbiter with a different build. owner="
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

    private static void TryInitializeExisting(object existing, ModConfig? modConfig, string ownerModId)
    {
        try
        {
            existing.GetType()
                .GetMethod("InitializeOwner", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(existing, new object?[] { modConfig, ownerModId });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Existing arbiter initialize failed: " + ex.Message);
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

    public sealed class AudioArbiterComponent : MonoBehaviour
    {
        private const float LowHealthNoProviderCooldownSeconds = 0.75f;
        private const float LowHealthRecoveryMargin = 0.05f;
        private const float LegacyLowHealthFallbackThreshold = 0.35f;
        private readonly List<SoundProviderHandle> soundProviders = new();
        private readonly HashSet<string> receivedEventIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> lowHealthAnnounced = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> providerMismatchWarnings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> lastHpRatioByStatus = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> lowHealthNoProviderUntil = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, float> suppressNarrationUntil = new();
        private static readonly Dictionary<string, MemberInfo?> IntMemberCache = new(StringComparer.Ordinal);
        private LowHealthProviderIndex lowHealthProviderIndex = LowHealthProviderIndex.Empty;
        private bool lowHealthProviderIndexDirty = true;
        private string ownerModId = "";
        private string lastAnnouncedCareerSelectionId = "";
        private bool hooksRegistered;
        private PendingReplacement? pendingReplacement;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => AudioArbiterRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

        public void InitializeOwner(ModConfig? modConfig, string owner)
        {
            ownerModId = owner;
            if (hooksRegistered || modConfig == null)
            {
                return;
            }

            hooksRegistered = true;
            RegisterAfter(modConfig, "GameEntryUI.Init", OnCareerSelectionSessionReset);
            RegisterBefore(modConfig, "Fight_Start.Init", OnFightStartBefore);
            RegisterAfter(modConfig, "Fight_Start.Init", OnFightStartAfter);
            RegisterAfter(modConfig, "GameEntryUI.ShowDetail", OnCareerDetailShown);
            AuraCombatActionRouter.RegisterBefore(
                modConfig,
                ownerModId + ".Audio",
                OnCombatActionBefore,
                Log,
                Warn);
            RegisterBefore(modConfig, "EffectSound.Start", OnEffectSoundBefore);
            RegisterAfter(modConfig, "BuffItem.Init", OnBuffInitAfter);
            RegisterAfter(modConfig, "StatusManager.PlayVocal", OnStatusVocalAfter);
            RegisterAfter(modConfig, "NarrationManager.Play", OnNarrationPlayAfter);
            RegisterAfter(modConfig, "ScriptExecutor.ChangeHp", OnPotentialHpChangedAfter);
            RegisterAfter(modConfig, "ScriptExecutor.PureChangeHp", OnPotentialHpChangedAfter);
            RegisterAfter(modConfig, "ScriptExecutor.SetHp", OnPotentialHpChangedAfter);
            RegisterAfter(modConfig, "ScriptExecutor.ChangeMaxHp", OnPotentialHpChangedAfter);
            RegisterAfter(modConfig, "ScriptExecutor.Damage", OnPotentialHpChangedAfter);
            RegisterAfter(modConfig, "ScriptExecutor.OnlineDamage", OnPotentialHpChangedAfter);
            RegisterAfter(modConfig, "StatusManager.set_CurHp", OnStatusHpChangedAfter);
            RegisterAfter(modConfig, "StatusManager.set_MaxHp", OnStatusHpChangedAfter);
            RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightWinAfter);
            RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEscapeAfter);
            Log("Hooks registered by owner=" + ownerModId);
        }

        public void RegisterSoundProvider(object provider)
        {
            try
            {
                var handle = new SoundProviderHandle(provider);
                if (string.IsNullOrWhiteSpace(handle.ProviderId))
                {
                    Warn("Sound provider skipped: ProviderId is empty. providerType=" + provider.GetType().FullName);
                    handle.Dispose("empty ProviderId");
                    return;
                }

                foreach (var previous in soundProviders
                             .Where(item => string.Equals(item.QualifiedProviderId, handle.QualifiedProviderId, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    previous.Dispose("replaced by new registration");
                }

                soundProviders.RemoveAll(item => string.Equals(item.QualifiedProviderId, handle.QualifiedProviderId, StringComparison.OrdinalIgnoreCase));
                soundProviders.Add(handle);
                lowHealthNoProviderUntil.Clear();
                lowHealthProviderIndexDirty = true;
                soundProviders.Sort((a, b) =>
                {
                    var priority = b.Priority.CompareTo(a.Priority);
                    return priority != 0 ? priority : string.Compare(a.QualifiedProviderId, b.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
                });
                Log("Sound provider registered: " + handle.Describe() + ", count=" + soundProviders.Count);
            }
            catch (Exception ex)
            {
                Warn("Sound provider registration failed: " + ex);
            }
        }

        public bool RegisterManifest(ModConfig modConfig, string owner, string manifestRelativePath)
        {
            try
            {
                if (modConfig == null)
                {
                    Warn("Manifest registration skipped: mod config is null. owner=" + owner);
                    return false;
                }

                var manifestPath = Path.Combine(modConfig.DirectoryName, string.IsNullOrWhiteSpace(manifestRelativePath)
                    ? "audio.registry.json"
                    : manifestRelativePath);
                if (!File.Exists(manifestPath))
                {
                    Warn("Manifest registration skipped: file missing. owner=" + owner + ", path=" + manifestPath);
                    return false;
                }

                var manifest = DeserializeManifest(File.ReadAllText(manifestPath));
                if (manifest == null)
                {
                    Warn("Manifest registration skipped: JSON is empty or invalid. owner=" + owner + ", path=" + manifestPath);
                    return false;
                }

                if (manifest.schemaVersion <= 0)
                {
                    manifest.schemaVersion = 1;
                }

                if (manifest.schemaVersion > SupportedManifestSchemaVersion)
                {
                    Warn("Manifest registration skipped: unsupported schemaVersion=" + manifest.schemaVersion
                        + ", supported=" + SupportedManifestSchemaVersion
                        + ", owner=" + owner);
                    return false;
                }

                if (manifest.audioProtocol != null && manifest.audioProtocol.minVersion > CurrentProtocolVersion)
                {
                    Warn("Manifest registration skipped: protocol minVersion=" + manifest.audioProtocol.minVersion
                        + ", runtime=" + CurrentProtocolVersion
                        + ", owner=" + owner);
                    return false;
                }

                var manifestOwner = string.IsNullOrWhiteSpace(manifest.ownerModId) ? owner : manifest.ownerModId.Trim();
                var defaults = manifest.defaults ?? new AudioRegistryDefaults();
                var providers = manifest.providers ?? Array.Empty<AudioProviderManifest>();
                var registered = 0;
                foreach (var provider in providers)
                {
                    if (provider == null)
                    {
                        continue;
                    }

                    var providerId = provider.providerId?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(providerId))
                    {
                        Warn("Manifest provider skipped: providerId is empty. owner=" + manifestOwner + ", path=" + manifestPath);
                        continue;
                    }

                    var audioPath = ResolveManifestPath(modConfig.DirectoryName, provider.path);
                    RegisterSoundProvider(new FileSoundProvider(
                        providerId: providerId,
                        ownerModId: string.IsNullOrWhiteSpace(provider.ownerModId) ? manifestOwner : provider.ownerModId.Trim(),
                        audioPath: audioPath,
                        priority: provider.priority,
                        bus: Coalesce(provider.bus, defaults.bus, SoundBuses.Effect),
                        policy: Coalesce(provider.policy, defaults.policy, SoundPolicies.Additive),
                        hardClaim: provider.hardClaim ?? defaults.hardClaim ?? false,
                        condition: BuildManifestCondition(provider),
                        cooldownSeconds: provider.cooldownSeconds ?? defaults.cooldownSeconds ?? 0f,
                        sync: provider.sync ?? defaults.sync ?? true,
                        gainDb: provider.gainDb ?? defaults.gainDb ?? 0f,
                        volumeMultiplier: provider.volumeMultiplier ?? defaults.volumeMultiplier ?? 1f,
                        kind: provider.kind,
                        lowHealthCrossDownThreshold: provider.match?.hpRatioCrossDown,
                        suppressVocalStates: provider.suppressOriginal?.vocalStates ?? Array.Empty<string>(),
                        suppressNarrationIds: provider.suppressOriginal?.narrationIds ?? Array.Empty<int>()));
                    registered++;
                }

                Log("Manifest registered: owner=" + manifestOwner + ", providers=" + registered + ", path=" + manifestPath);
                return registered > 0;
            }
            catch (Exception ex)
            {
                Warn("Manifest registration failed: owner=" + owner + " -> " + ex);
                return false;
            }
        }

        private static AudioRegistryManifest? DeserializeManifest(string json)
        {
            try
            {
                var jsonConvert = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json")
                    ?? Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
                var method = jsonConvert?.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
                if (method != null)
                {
                    return method.Invoke(null, new object[] { json, typeof(AudioRegistryManifest) }) as AudioRegistryManifest;
                }
            }
            catch
            {
            }

            try
            {
                var jsonUtility = Type.GetType("UnityEngine.JsonUtility, UnityEngine.JSONSerializeModule")
                    ?? Assembly.Load("UnityEngine.JSONSerializeModule").GetType("UnityEngine.JsonUtility");
                var method = jsonUtility?.GetMethod("FromJson", new[] { typeof(string), typeof(Type) });
                if (method != null)
                {
                    return method.Invoke(null, new object[] { json, typeof(AudioRegistryManifest) }) as AudioRegistryManifest;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ResolveManifestPath(string modRoot, string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            {
                return "";
            }

            const string sharedPrefix = "Shared:";
            if (relativeOrAbsolutePath.StartsWith(sharedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AuraSharedPaths.ResolveSharedPath(relativeOrAbsolutePath.Substring(sharedPrefix.Length));
            }

            return Path.IsPathRooted(relativeOrAbsolutePath)
                ? relativeOrAbsolutePath
                : Path.Combine(modRoot, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Coalesce(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }

        private static Func<object?, bool> BuildManifestCondition(AudioProviderManifest provider)
        {
            var match = provider.match ?? new AudioProviderMatch();
            var kind = provider.kind?.Trim() ?? "";
            var vocalState = provider.vocalState?.Trim() ?? "";
            var careerIds = ToSet(match.careerIds);
            var roleIds = ToSet(match.roleIds);
            var cardIds = ToSet(match.cardIds);
            var buffIds = ToSet(match.buffIds);
            var effectNames = ToSet(match.effectNames);
            var actionNames = ToSet(match.actionNames);
            var battleResults = ToSet(match.battleResults);
            var localOwnerOnly = match.localOwnerOnly ?? false;
            var hpRatioCrossDown = match.hpRatioCrossDown;

            return request =>
            {
                if (!string.IsNullOrWhiteSpace(kind)
                    && !string.Equals(ReadString(request, "Kind"), kind, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(vocalState)
                    && !string.Equals(ReadString(request, "VocalState"), vocalState, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (careerIds.Count > 0 && !MatchesAnyId(careerIds, ReadString(request, "CareerId")))
                {
                    return false;
                }

                if (roleIds.Count > 0 && !MatchesAnyId(roleIds, ReadString(request, "RoleId")))
                {
                    return false;
                }

                if (cardIds.Count > 0 && !cardIds.Contains(ReadString(request, "CardId")))
                {
                    return false;
                }

                if (buffIds.Count > 0 && !buffIds.Contains(ReadString(request, "BuffId")))
                {
                    return false;
                }

                if (effectNames.Count > 0 && !effectNames.Contains(ReadString(request, "EffectName")))
                {
                    return false;
                }

                if (actionNames.Count > 0 && !actionNames.Contains(ReadString(request, "ActionName")))
                {
                    return false;
                }

                if (battleResults.Count > 0 && !battleResults.Contains(ReadString(request, "BattleResult")))
                {
                    return false;
                }

                if (localOwnerOnly && !ReadBool(request, "IsRemote", false) && !ReadBool(request, "IsLocalOwner", false))
                {
                    return false;
                }

                if (hpRatioCrossDown.HasValue)
                {
                    var threshold = hpRatioCrossDown.Value;
                    if (!(ReadFloat(request, "PreviousHpRatio", 0f) > threshold
                          && ReadFloat(request, "HpRatio", 0f) <= threshold))
                    {
                        return false;
                    }
                }

                return true;
            };
        }

        private static HashSet<string> ToSet(string[]? values)
        {
            return new HashSet<string>(
                values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool MatchesAnyId(HashSet<string> accepted, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (accepted.Contains(value))
            {
                return true;
            }

            return accepted.Any(id =>
                value.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("_" + id, StringComparison.OrdinalIgnoreCase));
        }

        public bool RequestSound(object request)
        {
            var normalized = SoundPlaybackRequest.FromObject(request);
            return RequestSoundInternal(normalized, syncRemote: !normalized.IsRemote);
        }

        public void ReceiveRemote(SoundPlaybackRequest request)
        {
            if (IsExpiredPresentation(request))
            {
                TraceRequest(request, "Discarded expired presentation event");
                return;
            }

            if (string.IsNullOrWhiteSpace(request.EventId))
            {
                request.EventId = Guid.NewGuid().ToString("N");
            }

            if (!receivedEventIds.Add(request.EventId))
            {
                return;
            }

            request.IsRemote = true;
            RequestSoundInternal(request, syncRemote: false);
        }

        private bool RequestSoundInternal(SoundPlaybackRequest request, bool syncRemote)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.EventId))
                {
                    request.EventId = Guid.NewGuid().ToString("N");
                }

                if (IsCardUsePresentation(request) && !request.IsRemote)
                {
                    if (IsClientWaitingForHostPresentation())
                    {
                        TraceRequest(request, "Card-use presentation deferred to host relay");
                        return true;
                    }

                    PublishHostCardUsePresentation(request);
                }

                var resolvedMaybe = Resolve(request);
                if (!resolvedMaybe.HasValue)
                {
                    RememberLowHealthNoProvider(request);
                    TraceRequest(request, "No provider resolved");
                    return false;
                }

                var resolved = resolvedMaybe.Value;
                if (!CanPassCooldown(resolved.Provider, request))
                {
                    TraceRequest(request, "Suppressed by cooldown: provider=" + resolved.Provider.ProviderId);
                    return false;
                }

                if (string.Equals(resolved.Provider.Bus, SoundBuses.Effect, StringComparison.OrdinalIgnoreCase)
                    && IsReplacementPolicy(resolved.Provider.Policy)
                    && !request.IsRemote
                    && string.Equals(request.Kind, SoundEventKinds.CardUse, StringComparison.OrdinalIgnoreCase))
                {
                    ArmOriginalSuppressions(resolved.Provider);
                    pendingReplacement = new PendingReplacement(
                        resolved.Clip,
                        resolved.Provider.Policy,
                        resolved.Provider.VolumeMultiplier,
                        Time.unscaledTime + 1.0f,
                        1);
                    TraceRequest(request, "Pending effect replacement: provider=" + resolved.Provider.ProviderId);
                    SyncRemote(request, resolved.Provider, syncRemote);
                    return true;
                }

                TraceRequest(request, "Provider resolved: provider=" + resolved.Provider.ProviderId
                    + ", bus=" + resolved.Provider.Bus
                    + ", clip=" + resolved.Clip.name);
                ArmOriginalSuppressions(resolved.Provider);
                PlayResolved(request, resolved);
                SyncRemote(request, resolved.Provider, syncRemote);
                return true;
            }
            catch (Exception ex)
            {
                Warn("RequestSound failed: " + ex);
                return false;
            }
        }

        private ResolvedSound? Resolve(SoundPlaybackRequest request)
        {
            var requestedProviderId = (request.ProviderId ?? "").Trim();
            if (requestedProviderId.Length == 0)
            {
                return ResolveWithProviderMatcher(request, _ => true);
            }

            var requestedOwnerModId = (request.OwnerModId ?? "").Trim();
            var hasOwnerScope = requestedOwnerModId.Length > 0;
            var isQualifiedProviderId = requestedProviderId.Contains(":");

            if (hasOwnerScope || isQualifiedProviderId)
            {
                var strictIdentityMatched = false;
                var resolved = ResolveWithProviderMatcher(request, provider =>
                {
                    var matched = provider.MatchesProviderRequest(
                        requestedProviderId,
                        requestedOwnerModId,
                        ownerStrict: true);
                    if (matched)
                    {
                        strictIdentityMatched = true;
                    }

                    return matched;
                });

                if (resolved.HasValue || strictIdentityMatched || request.IsRemote || isQualifiedProviderId)
                {
                    if (!resolved.HasValue && request.IsRemote && !strictIdentityMatched)
                    {
                        WarnProviderMismatchOnce(request, "Remote sound provider mismatch");
                    }

                    return resolved;
                }

                WarnProviderMismatchOnce(
                    request,
                    "Local sound provider owner mismatch; falling back to legacy bare provider id");
            }

            return ResolveWithProviderMatcher(request, provider => provider.MatchesProviderId(requestedProviderId));
        }

        private ResolvedSound? ResolveWithProviderMatcher(
            SoundPlaybackRequest request,
            Func<SoundProviderHandle, bool> matchesProvider)
        {
            foreach (var provider in soundProviders)
            {
                if (!matchesProvider(provider))
                {
                    continue;
                }

                if (!provider.Evaluate(request))
                {
                    continue;
                }

                if (!string.Equals(provider.GetLoadState(), "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    TraceRequest(request, "Provider matched but not ready: provider=" + provider.ProviderId
                        + ", state=" + provider.GetLoadState());
                    if (provider.HardClaim)
                    {
                        return null;
                    }

                    continue;
                }

                var clip = provider.GetClip(request);
                if (clip != null)
                {
                    request.ProviderId = provider.QualifiedProviderId;
                    request.OwnerModId = provider.OwnerModId;
                    TraceRequest(request, "Provider clip selected: provider=" + provider.ProviderId
                        + ", clip=" + clip.name);
                    return new ResolvedSound(provider, clip);
                }

                if (provider.HardClaim)
                {
                    return null;
                }
            }

            return null;
        }

        private void WarnProviderMismatchOnce(SoundPlaybackRequest request, string message)
        {
            var key = message
                + "|" + request.ProviderId
                + "|" + request.OwnerModId
                + "|" + request.Kind
                + "|" + request.RoleId
                + "|" + request.StatusInstanceId
                + "|" + request.IsRemote;
            if (!providerMismatchWarnings.Add(key))
            {
                return;
            }

            Warn(message
                + ": providerId=" + DisplayLogValue(request.ProviderId)
                + ", owner=" + DisplayLogValue(request.OwnerModId)
                + ", kind=" + DisplayLogValue(request.Kind)
                + ", role=" + DisplayLogValue(request.RoleId)
                + ", status=" + DisplayLogValue(request.StatusInstanceId)
                + ", remote=" + request.IsRemote
                + ".");
        }

        private static string DisplayLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
        }

        private bool CanPassCooldown(SoundProviderHandle provider, SoundPlaybackRequest request)
        {
            var key = provider.QualifiedProviderId + "|" + request.Kind + "|" + request.RoleId + "|" + request.StatusInstanceId;
            if (cooldownUntil.TryGetValue(key, out var until) && Time.unscaledTime < until)
            {
                return false;
            }

            if (provider.CooldownSeconds > 0f)
            {
                cooldownUntil[key] = Time.unscaledTime + provider.CooldownSeconds;
            }

            return true;
        }

        private void PlayResolved(SoundPlaybackRequest request, ResolvedSound resolved)
        {
            var manager = AudioManager.Instance;
            if (manager == null || resolved.Clip == null)
            {
                return;
            }

                if (string.Equals(resolved.Provider.Bus, SoundBuses.Vocal, StringComparison.OrdinalIgnoreCase))
                {
                    var roleId = !string.IsNullOrWhiteSpace(request.StatusInstanceId)
                        ? request.StatusInstanceId
                        : string.IsNullOrWhiteSpace(request.RoleId)
                            ? request.CareerId
                            : request.RoleId;
                if (string.IsNullOrWhiteSpace(roleId))
                {
                        roleId = resolved.Provider.OwnerModId + "." + resolved.Provider.ProviderId;
                    }

                PlayVocal(manager, roleId, resolved.Clip, resolved.Provider.VolumeMultiplier);
                TraceRequest(request, "Playing vocal: roleId=" + roleId
                    + ", provider=" + resolved.Provider.ProviderId
                    + ", clip=" + resolved.Clip.name
                    + ", gainDb=" + resolved.Provider.GainDb.ToString("0.##")
                    + ", volumeMultiplier=" + resolved.Provider.VolumeMultiplier.ToString("0.###"));
                return;
            }

            PlayEffect(manager, resolved.Clip, resolved.Provider.VolumeMultiplier);
            TraceRequest(request, "Playing effect: provider=" + resolved.Provider.ProviderId
                + ", clip=" + resolved.Clip.name
                + ", gainDb=" + resolved.Provider.GainDb.ToString("0.##")
                + ", volumeMultiplier=" + resolved.Provider.VolumeMultiplier.ToString("0.###"));
        }

        private static void PlayVocal(AudioManager manager, string roleId, AudioClip clip, float volumeMultiplier)
        {
            if (Math.Abs(volumeMultiplier - 1f) < 0.001f)
            {
                manager.PlayVocal(roleId, clip);
                return;
            }

            var source = GetOrCreateVocalSource(manager, roleId);
            if (source == null)
            {
                manager.PlayVocal(roleId, clip);
                return;
            }

            source.Stop();
            source.clip = clip;
            source.volume = ResolveManagerVolume(manager, "NarrationVolume");
            source.PlayOneShot(clip, volumeMultiplier);
        }

        private static void PlayEffect(AudioManager manager, AudioClip clip, float volumeMultiplier)
        {
            if (Math.Abs(volumeMultiplier - 1f) < 0.001f)
            {
                manager.PlayEffect(clip);
                return;
            }

            var source = ReadMember(manager, "effectSource") as AudioSource;
            if (source == null)
            {
                manager.PlayEffect(clip);
                return;
            }

            source.PlayOneShot(clip, ResolveManagerVolume(manager, "EffectVolume") * volumeMultiplier);
        }

        private static AudioSource? GetOrCreateVocalSource(AudioManager manager, string roleId)
        {
            var sources = ReadMember(manager, "_vocalSources") as System.Collections.IDictionary;
            if (sources == null)
            {
                return null;
            }

            if (sources.Contains(roleId) && sources[roleId] is AudioSource existing)
            {
                return existing;
            }

            var source = manager.gameObject.AddComponent<AudioSource>();
            var vocalGroup = ReadMember(manager, "vocalGroup") as UnityEngine.Audio.AudioMixerGroup;
            if (vocalGroup != null)
            {
                source.outputAudioMixerGroup = vocalGroup;
            }

            sources[roleId] = source;
            return source;
        }

        private static void StopVocalSource(string roleId)
        {
            try
            {
                var manager = AudioManager.Instance;
                if (manager == null || string.IsNullOrWhiteSpace(roleId))
                {
                    return;
                }

                var sources = ReadMember(manager, "_vocalSources") as System.Collections.IDictionary;
                if (sources != null && sources.Contains(roleId) && sources[roleId] is AudioSource source)
                {
                    source.Stop();
                }
            }
            catch
            {
            }
        }

        private static float ResolveManagerVolume(AudioManager manager, string volumeField)
        {
            var volume = ReadFloatMember(manager, volumeField, 1f);
            if (ReadMember(manager, "audioMixer") != null)
            {
                return volume;
            }

            return volume * ReadFloatMember(manager, "masterVolume", 1f);
        }

        private static object? ReadMember(object target, string name)
        {
            try
            {
                var type = target.GetType();
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return property?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static float ReadFloatMember(object target, string name, float fallback)
        {
            try
            {
                var value = ReadMember(target, name);
                if (value is float typed)
                {
                    return typed;
                }

                return float.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void SyncRemote(SoundPlaybackRequest request, SoundProviderHandle provider, bool syncRemote)
        {
            if (!syncRemote || request.DisableSync || request.IsRemote || !provider.Sync || IsCardUsePresentation(request))
            {
                return;
            }

            var playerManager = PlayerManager.Instance;
            if (playerManager == null)
            {
                return;
            }

            // Keep the RPC payload's bare ProviderId for older receivers; new receivers use OwnerModId to disambiguate.
            request.ProviderId = provider.ProviderId;
            request.OwnerModId = provider.OwnerModId;
            try
            {
                playerManager.SendRpcCommandExcludeOwner(new RpcAudioEvent(request));
            }
            catch (Exception ex)
            {
                Warn("Remote sound sync failed: " + ex.Message);
            }
        }

        private void ArmOriginalSuppressions(SoundProviderHandle provider)
        {
            if (provider.SuppressNarrationIds.Count == 0)
            {
                return;
            }

            var until = Time.unscaledTime + 1.5f;
            foreach (var id in provider.SuppressNarrationIds)
            {
                suppressNarrationUntil[id] = until;
            }
        }

        private static bool IsReplacementPolicy(string policy)
        {
            return string.Equals(policy, SoundPolicies.Replace, StringComparison.OrdinalIgnoreCase)
                || string.Equals(policy, SoundPolicies.ReplaceOriginal, StringComparison.OrdinalIgnoreCase)
                || string.Equals(policy, SoundPolicies.SuppressOriginal, StringComparison.OrdinalIgnoreCase);
        }

        private void OnFightStartBefore(ModHookContext context)
        {
            receivedEventIds.Clear();
            cooldownUntil.Clear();
            lowHealthAnnounced.Clear();
            lastHpRatioByStatus.Clear();
            lowHealthNoProviderUntil.Clear();
            suppressNarrationUntil.Clear();
            pendingReplacement = null;
        }

        private bool IsClientWaitingForHostPresentation()
        {
            try
            {
                return PlayerManager.Instance != null && !PlayerManager.Instance.isServer;
            }
            catch
            {
                return false;
            }
        }

        private void PublishHostCardUsePresentation(SoundPlaybackRequest request)
        {
            var playerManager = PlayerManager.Instance;
            if (playerManager == null || !playerManager.isServer)
            {
                return;
            }

            var presentation = new SoundPlaybackRequest
            {
                EventId = Guid.NewGuid().ToString("N"),
                OwnerModId = ownerModId,
                Kind = SoundEventKinds.CardUse,
                CareerId = request.CareerId,
                RoleId = request.RoleId,
                StatusInstanceId = request.StatusInstanceId,
                CardId = request.CardId,
                EffectName = request.EffectName,
                ActionName = request.ActionName,
                SourceName = request.SourceName,
                CreatedAtUtcTicks = DateTime.UtcNow.Ticks,
                MaxAgeMilliseconds = SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds
            };

            try
            {
                playerManager.SendRpcCommandExcludeOwner(new RpcAudioEvent(presentation));
                TraceRequest(presentation, "Host card-use presentation relayed");
            }
            catch (Exception ex)
            {
                Warn("Host card-use presentation relay failed: " + ex.Message);
            }
        }

        private static bool IsCardUsePresentation(SoundPlaybackRequest request)
        {
            return string.Equals(request.Kind, SoundEventKinds.CardUse, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExpiredPresentation(SoundPlaybackRequest request)
        {
            if (!IsCardUsePresentation(request)
                || request.CreatedAtUtcTicks <= 0
                || request.MaxAgeMilliseconds <= 0)
            {
                return false;
            }

            var elapsedTicks = DateTime.UtcNow.Ticks - request.CreatedAtUtcTicks;
            return elapsedTicks > TimeSpan.TicksPerMillisecond * request.MaxAgeMilliseconds;
        }

        private void OnFightStartAfter(ModHookContext context)
        {
            SeedKnownHpRatios();
        }

        private void SeedKnownHpRatios()
        {
            try
            {
                var statuses = FightManager.Instance?.statuses;
                if (statuses == null)
                {
                    return;
                }

                foreach (var status in statuses.Values)
                {
                    SeedHpRatio(status);
                }
            }
            catch (Exception ex)
            {
                Warn("HP ratio seed failed: " + ex.Message);
            }
        }

        private void SeedHpRatio(StatusManager? status)
        {
            if (status == null)
            {
                return;
            }

            var statusId = ResolveStatusId(status);
            var ratio = ReadHpRatio(status);
            if (!string.IsNullOrWhiteSpace(statusId) && ratio > 0f)
            {
                lastHpRatioByStatus[statusId] = ratio;
            }
        }

        private void OnCareerSelectionSessionReset(ModHookContext context)
        {
            lastAnnouncedCareerSelectionId = "";
        }

        private void OnCareerDetailShown(ModHookContext context)
        {
            var showCareer = context.Arguments != null && context.Arguments.Length > 0 ? context.Arguments[0] as ShowCareer : null;
            if (showCareer?.dataConfig != null)
            {
                RequestCareerSelected(ReadDataId(showCareer.dataConfig), "GameEntryUI.ShowDetail");
            }

        }

        private void RequestCareerSelected(string careerId, string source)
        {
            if (string.IsNullOrWhiteSpace(careerId))
            {
                return;
            }

            if (string.Equals(lastAnnouncedCareerSelectionId, careerId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lastAnnouncedCareerSelectionId = careerId;
            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.CareerSelected,
                CareerId = careerId,
                RoleId = careerId,
                SourceName = source
            }, syncRemote: true);
        }

        private void OnActionAnimationBefore(ModHookContext context)
        {
            var executor = context.Arguments != null && context.Arguments.Length > 0 ? context.Arguments[0] as IScriptExecutor : null;
            if (!IsCardScriptExecutor(executor))
            {
                return;
            }

            var data = executor?.dataConfig;
            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.CardUse,
                CardId = ReadDataId(data),
                CareerId = ReadCurrentCareerId(),
                RoleId = ReadCurrentCareerId(),
                StatusInstanceId = executor?.Self?.InstanceId ?? "",
                EffectName = ReadDataValue(data, "Effects"),
                ActionName = ReadDataValue(data, "Action"),
                SourceName = "FightUI.CallActionAnimation"
            }, syncRemote: true);

            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.SkillVoice,
                CardId = ReadDataId(data),
                CareerId = ReadCurrentCareerId(),
                RoleId = ReadCurrentCareerId(),
                StatusInstanceId = executor?.Self?.InstanceId ?? "",
                EffectName = ReadDataValue(data, "Effects"),
                ActionName = ReadDataValue(data, "Action"),
                SourceName = "FightUI.CallActionAnimation"
            }, syncRemote: true);
        }

        private void OnCombatActionBefore(AuraCombatActionContext context)
        {
            if (!context.IsCardAction)
            {
                return;
            }

            var roleId = string.IsNullOrWhiteSpace(context.OwnerRoleId)
                ? context.CurrentRoleId
                : context.OwnerRoleId;
            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.CardUse,
                CardId = context.CardId,
                CareerId = context.CurrentRoleId,
                RoleId = roleId,
                StatusInstanceId = context.OwnerInstanceId,
                EffectName = context.Effects,
                ActionName = context.Action,
                SourceName = "FightUI.CallActionAnimation"
            }, syncRemote: true);

            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.SkillVoice,
                CardId = context.CardId,
                CareerId = context.CurrentRoleId,
                RoleId = roleId,
                StatusInstanceId = context.OwnerInstanceId,
                EffectName = context.Effects,
                ActionName = context.Action,
                SourceName = "FightUI.CallActionAnimation"
            }, syncRemote: true);
        }

        private void OnEffectSoundBefore(ModHookContext context)
        {
            if (pendingReplacement == null || Time.unscaledTime > pendingReplacement.Value.UntilTime || pendingReplacement.Value.Remaining <= 0)
            {
                pendingReplacement = null;
                return;
            }

            var effectSound = context.Target as EffectSound;
            if (effectSound == null)
            {
                return;
            }

            var pending = pendingReplacement.Value;
            if (pending.Clip != null)
            {
                if (string.Equals(pending.Policy, SoundPolicies.SuppressOriginal, StringComparison.OrdinalIgnoreCase))
                {
                    effectSound.clip = null;
                }
                else if (Math.Abs(pending.VolumeMultiplier - 1f) >= 0.001f)
                {
                    effectSound.clip = null;
                    StartCoroutine(PlayEffectAfterDelay(Math.Max(0f, effectSound.delay), pending.Clip, pending.VolumeMultiplier));
                }
                else
                {
                    effectSound.clip = pending.Clip;
                }
            }

            pendingReplacement = pending.ConsumeOne();
        }

        private static IEnumerator PlayEffectAfterDelay(float delaySeconds, AudioClip clip, float volumeMultiplier)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            var manager = AudioManager.Instance;
            if (manager != null)
            {
                PlayEffect(manager, clip, volumeMultiplier);
            }
        }

        private void OnBuffInitAfter(ModHookContext context)
        {
            var args = context.Arguments;
            var config = args != null && args.Length > 0 ? args[0] as BuffItemConfig : null;
            var status = args != null && args.Length > 1 ? args[1] as StatusManager : null;
            if (config == null)
            {
                return;
            }

            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.BuffApplied,
                BuffId = config.BuffId,
                CareerId = ReadCurrentCareerId(),
                RoleId = ReadCurrentCareerId(),
                StatusInstanceId = status?.InstanceId ?? "",
                SourceName = "BuffItem.Init"
            }, syncRemote: true);
        }

        private void OnStatusVocalAfter(ModHookContext context)
        {
            var status = context.Target as StatusManager;
            var state = context.Arguments != null && context.Arguments.Length > 0 ? context.Arguments[0]?.ToString() ?? "" : "";
            if (status == null || string.IsNullOrWhiteSpace(state))
            {
                return;
            }

            var request = new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.VocalState,
                VocalState = state,
                CareerId = ReadCurrentCareerId(),
                RoleId = ReadStatusRoleId(status),
                StatusInstanceId = ResolveStatusId(status),
                SourceName = "StatusManager.PlayVocal.After"
            };
            TraceRequest(request, "VocalState event observed");
            RequestSoundInternal(request, syncRemote: true);
        }

        private void OnNarrationPlayAfter(ModHookContext context)
        {
            var ids = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as int[]
                : null;
            if (ids == null || ids.Length == 0 || suppressNarrationUntil.Count == 0)
            {
                return;
            }

            var now = Time.unscaledTime;
            var shouldSuppress = ids.Any(id =>
                suppressNarrationUntil.TryGetValue(id, out var until) && now <= until);
            foreach (var expired in suppressNarrationUntil
                         .Where(item => now > item.Value)
                         .Select(item => item.Key)
                         .ToList())
            {
                suppressNarrationUntil.Remove(expired);
            }

            if (!shouldSuppress)
            {
                return;
            }

            StopVocalSource("Krisna");
            Log("Original narration suppressed: ids=" + string.Join(",", ids));
        }

        private void OnPotentialHpChangedAfter(ModHookContext context)
        {
            var executor = context.Target as IScriptExecutor;
            if (executor == null)
            {
                return;
            }

            TryRequestLowHealthVoice(executor.Self as StatusManager, "ScriptExecutor.HpChanged.Self");
            var targets = executor.Object;
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            foreach (var target in targets)
            {
                TryRequestLowHealthVoice(target as StatusManager, "ScriptExecutor.HpChanged.Target");
            }
        }

        private void OnStatusHpChangedAfter(ModHookContext context)
        {
            TryRequestLowHealthVoice(context.Target as StatusManager, "StatusManager.HpChanged");
        }

        private void TryRequestLowHealthVoice(StatusManager? status, string source)
        {
            if (status == null)
            {
                return;
            }

            var maxHp = ReadIntMember(status, "MaxHp");
            var hp = ReadIntMember(status, "CurHp");
            if (hp <= 0)
            {
                hp = ReadIntMember(status, "Hp");
            }

            if (maxHp <= 0 || hp <= 0)
            {
                return;
            }

            var ratio = (float)hp / maxHp;
            var statusId = ResolveStatusId(status);

            if (string.IsNullOrWhiteSpace(statusId))
            {
                return;
            }

            if (!lastHpRatioByStatus.TryGetValue(statusId, out var previousRatio))
            {
                lastHpRatioByStatus[statusId] = ratio;
                return;
            }

            lastHpRatioByStatus[statusId] = ratio;
            if (ratio > previousRatio)
            {
                ResetLowHealthAnnouncementIfRecovered(statusId, ratio);
                return;
            }

            if (ratio >= previousRatio || lowHealthAnnounced.Contains(statusId))
            {
                return;
            }

            var statusRoleId = ReadStatusRoleId(status, fallbackToCurrent: false);
            var careerId = IsLocalPlayerStatus(status) ? ReadCurrentCareerId() : statusRoleId;
            if (string.IsNullOrWhiteSpace(statusRoleId) && IsLocalPlayerStatus(status))
            {
                statusRoleId = careerId;
            }

            if (string.IsNullOrWhiteSpace(careerId) && string.IsNullOrWhiteSpace(statusRoleId))
            {
                Log("LowHealth event skipped: role id missing, source=" + source
                    + ", statusInstance=" + status.InstanceId
                    + ", hp=" + hp
                    + ", maxHp=" + maxHp
                    + ", ratio=" + ratio.ToString("0.###"));
                return;
            }

            var request = new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.LowHealth,
                CareerId = careerId,
                RoleId = string.IsNullOrWhiteSpace(statusRoleId) ? careerId : statusRoleId,
                StatusInstanceId = statusId,
                Hp = hp,
                MaxHp = maxHp,
                PreviousHpRatio = previousRatio,
                HpRatio = ratio,
                SourceName = source,
                IsLocalOwner = IsLocalPlayerStatus(status)
            };
            if (IsLowHealthNoProviderSuppressed(request))
            {
                return;
            }

            if (!ShouldAttemptLowHealthRequest(request))
            {
                return;
            }

            TraceRequest(request, "LowHealth event observed");
            if (RequestSoundInternal(request, syncRemote: true))
            {
                lowHealthAnnounced.Add(statusId);
            }
        }

        private void ResetLowHealthAnnouncementIfRecovered(string statusId, float ratio)
        {
            var threshold = LowestConfiguredLowHealthThreshold();
            var resetAt = threshold >= 0f ? threshold + LowHealthRecoveryMargin : 0.5f;
            if (ratio >= resetAt)
            {
                lowHealthAnnounced.Remove(statusId);
            }
        }

        private bool ShouldAttemptLowHealthRequest(SoundPlaybackRequest request)
        {
            var index = GetLowHealthProviderIndex();
            if (index.ExplicitCandidates > 0)
            {
                return index.ThresholdCandidates < index.ExplicitCandidates
                       || index.CrossedThreshold(request.PreviousHpRatio, request.HpRatio);
            }

            if (!index.HasUnknownProvider)
            {
                return false;
            }

            return request.PreviousHpRatio > LegacyLowHealthFallbackThreshold
                   && request.HpRatio <= LegacyLowHealthFallbackThreshold;
        }

        private float LowestConfiguredLowHealthThreshold()
        {
            return GetLowHealthProviderIndex().LowestThreshold;
        }

        private LowHealthProviderIndex GetLowHealthProviderIndex()
        {
            if (!lowHealthProviderIndexDirty)
            {
                return lowHealthProviderIndex;
            }

            var hasUnknownProvider = false;
            var explicitCandidates = 0;
            var thresholdCandidates = 0;
            var threshold = -1f;
            var thresholds = new List<float>();
            foreach (var provider in soundProviders)
            {
                if (string.IsNullOrWhiteSpace(provider.Kind))
                {
                    hasUnknownProvider = true;
                    continue;
                }

                if (!IsLowHealthProvider(provider))
                {
                    continue;
                }

                explicitCandidates++;
                if (provider.LowHealthCrossDownThreshold < 0f)
                {
                    continue;
                }

                thresholdCandidates++;
                thresholds.Add(provider.LowHealthCrossDownThreshold);
                threshold = threshold < 0f
                    ? provider.LowHealthCrossDownThreshold
                    : Math.Min(threshold, provider.LowHealthCrossDownThreshold);
            }

            lowHealthProviderIndex = new LowHealthProviderIndex(
                hasUnknownProvider,
                explicitCandidates,
                thresholdCandidates,
                threshold,
                thresholds.ToArray());
            lowHealthProviderIndexDirty = false;
            return lowHealthProviderIndex;
        }

        private static bool IsLowHealthProvider(SoundProviderHandle provider)
        {
            return string.Equals(provider.Kind, SoundEventKinds.LowHealth, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLowHealthNoProviderSuppressed(SoundPlaybackRequest request)
        {
            if (!IsLowHealthRequest(request))
            {
                return false;
            }

            var key = LowHealthNoProviderKey(request);
            if (!lowHealthNoProviderUntil.TryGetValue(key, out var until))
            {
                return false;
            }

            if (Time.unscaledTime < until)
            {
                return true;
            }

            lowHealthNoProviderUntil.Remove(key);
            return false;
        }

        private void RememberLowHealthNoProvider(SoundPlaybackRequest request)
        {
            if (!IsLowHealthRequest(request))
            {
                return;
            }

            lowHealthNoProviderUntil[LowHealthNoProviderKey(request)] =
                Time.unscaledTime + LowHealthNoProviderCooldownSeconds;
        }

        private static bool IsLowHealthRequest(SoundPlaybackRequest request)
        {
            return string.Equals(request.Kind, SoundEventKinds.LowHealth, StringComparison.OrdinalIgnoreCase);
        }

        private static string LowHealthNoProviderKey(SoundPlaybackRequest request)
        {
            var ratioBucket = Mathf.Clamp(Mathf.FloorToInt(request.HpRatio * 10f), 0, 10);
            return request.StatusInstanceId
                + "|"
                + request.RoleId
                + "|"
                + request.CareerId
                + "|"
                + ratioBucket;
        }

        private void OnFightWinAfter(ModHookContext context)
        {
            RequestBattleCompleted("Win", "Fight_Win.ResetStates");
        }

        private void OnFightEscapeAfter(ModHookContext context)
        {
            RequestBattleCompleted("Escape", "Fight_Escape.ResetStates");
        }

        private void RequestBattleCompleted(string result, string source)
        {
            RequestSoundInternal(new SoundPlaybackRequest
            {
                Kind = SoundEventKinds.BattleCompleted,
                BattleResult = result,
                CareerId = ReadCurrentCareerId(),
                RoleId = ReadCurrentCareerId(),
                SourceName = source
            }, syncRemote: true);
        }

        private static bool IsCardScriptExecutor(IScriptExecutor? executor)
        {
            try
            {
                var dataConfig = executor?.dataConfig;
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

        private static string ReadDataId(IDataConfig? data)
        {
            try
            {
                if (data?.data != null && data.data.TryGetValue("Id", out var id))
                {
                    return id ?? "";
                }
            }
            catch
            {
            }

            return "";
        }

        private static string ReadDataValue(IDataConfig? data, string key)
        {
            try
            {
                if (data?.data != null && data.data.TryGetValue(key, out var value))
                {
                    return value ?? "";
                }
            }
            catch
            {
            }

            return "";
        }

        private static string ReadCurrentCareerId()
        {
            return ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
        }

        private static string ReadStatusRoleId(StatusManager status, bool fallbackToCurrent = true)
        {
            try
            {
                var id = AuraSharedReflection.ReadString(status.fatherObject, "Id", "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
            catch
            {
            }

            return fallbackToCurrent ? ReadCurrentCareerId() : "";
        }

        private static string ResolveStatusId(StatusManager status)
        {
            if (!string.IsNullOrWhiteSpace(status.InstanceId))
            {
                return status.InstanceId;
            }

            return ReadStatusRoleId(status);
        }

        private static float ReadHpRatio(StatusManager status)
        {
            var maxHp = ReadIntMember(status, "MaxHp");
            var hp = ReadIntMember(status, "CurHp");
            if (hp <= 0)
            {
                hp = ReadIntMember(status, "Hp");
            }

            return maxHp <= 0 || hp <= 0 ? 0f : (float)hp / maxHp;
        }

        private static bool IsLocalPlayerStatus(StatusManager status)
        {
            try
            {
                var playerId = PlayerManager.Instance?.PlayerId;
                if (!string.IsNullOrWhiteSpace(playerId)
                    && string.Equals(playerId, status.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return ReferenceEquals(FightPlayer.Instance?.Status, status);
            }
            catch
            {
                return false;
            }
        }

        private static int ReadIntMember(object source, string memberName)
        {
            try
            {
                var type = source.GetType();
                var key = type.FullName + "|" + memberName;
                if (!IntMemberCache.TryGetValue(key, out var member))
                {
                    member = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? (MemberInfo?)type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    IntMemberCache[key] = member;
                }

                var value = member switch
                {
                    PropertyInfo property => property.GetValue(source),
                    FieldInfo field => field.GetValue(source),
                    _ => null
                };
                if (value is int typed)
                {
                    return typed;
                }

                return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
            }
            catch
            {
                return 0;
            }
        }

        private readonly struct LowHealthProviderIndex
        {
            public static readonly LowHealthProviderIndex Empty = new(false, 0, 0, -1f, Array.Empty<float>());

            public LowHealthProviderIndex(
                bool hasUnknownProvider,
                int explicitCandidates,
                int thresholdCandidates,
                float lowestThreshold,
                float[] thresholds)
            {
                HasUnknownProvider = hasUnknownProvider;
                ExplicitCandidates = explicitCandidates;
                ThresholdCandidates = thresholdCandidates;
                LowestThreshold = lowestThreshold;
                Thresholds = thresholds ?? Array.Empty<float>();
            }

            public bool HasUnknownProvider { get; }

            public int ExplicitCandidates { get; }

            public int ThresholdCandidates { get; }

            public float LowestThreshold { get; }

            private float[] Thresholds { get; }

            public bool CrossedThreshold(float previousRatio, float ratio)
            {
                for (var i = 0; i < Thresholds.Length; i++)
                {
                    var threshold = Thresholds[i];
                    if (previousRatio > threshold && ratio <= threshold)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
        {
            try
            {
                config.AddMethodHookBefore(target, action);
                Log("Hook before registered: " + target);
            }
            catch (Exception ex)
            {
                Warn("Hook before failed: " + target + " -> " + ex.Message);
            }
        }

        private void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
        {
            try
            {
                config.AddMethodHookAfter(target, action);
                Log("Hook after registered: " + target);
            }
            catch (Exception ex)
            {
                Warn("Hook after failed: " + target + " -> " + ex.Message);
            }
        }

        private void Log(string message)
        {
            AuraSharedLog.DebugLog("AudioArbiter", message, false);
        }

        private void Warn(string message)
        {
            Debug.LogWarning("[AudioArbiter] " + message);
        }

        private void TraceRequest(SoundPlaybackRequest request, string message)
        {
            if (!ShouldTrace(request))
            {
                return;
            }

            Log(message
                + ", eventId=" + request.EventId
                + ", kind=" + request.Kind
                + ", vocalState=" + request.VocalState
                + ", careerId=" + request.CareerId
                + ", roleId=" + request.RoleId
                + ", statusInstance=" + request.StatusInstanceId
                + ", hp=" + request.Hp
                + ", maxHp=" + request.MaxHp
                + ", ratio=" + request.HpRatio.ToString("0.###")
                + ", source=" + request.SourceName
                + ", remote=" + request.IsRemote);
        }

        private static bool ShouldTrace(SoundPlaybackRequest request)
        {
            return string.Equals(request.Kind, SoundEventKinds.LowHealth, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(request.Kind, SoundEventKinds.VocalState, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(request.VocalState, "Dying", StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class SoundProviderHandle
    {
        private readonly object provider;
        private readonly Type providerType;

        public SoundProviderHandle(object provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            providerType = provider.GetType();
            ProviderId = ReadString("ProviderId", providerType.FullName ?? "");
            OwnerModId = ReadString("OwnerModId", "");
            if (string.IsNullOrWhiteSpace(OwnerModId))
            {
                OwnerModId = providerType.Assembly.GetName().Name ?? "";
            }

            QualifiedProviderId = QualifyProviderId(OwnerModId, ProviderId);
            Kind = ReadString("Kind", "");
            LowHealthCrossDownThreshold = ReadFloat("LowHealthCrossDownThreshold", -1f);
            Priority = ReadInt("Priority", 0);
            HardClaim = ReadBool("HardClaim", false);
            Sync = ReadBool("Sync", true);
            CooldownSeconds = ReadFloat("CooldownSeconds", 0f);
            GainDb = ReadFloat("GainDb", 0f);
            VolumeMultiplier = Math.Max(0f, ReadFloat("VolumeMultiplier", 1f)) * Mathf.Pow(10f, GainDb / 20f);
            Bus = ReadString("Bus", SoundBuses.Effect);
            Policy = ReadString("Policy", SoundPolicies.Additive);
            SuppressVocalStates = SplitString(ReadString("SuppressVocalStates", ""));
            SuppressNarrationIds = SplitInts(ReadString("SuppressNarrationIds", ""));
        }

        public string ProviderId { get; }

        public string OwnerModId { get; }

        public string QualifiedProviderId { get; }

        public string Kind { get; }

        public float LowHealthCrossDownThreshold { get; }

        public int Priority { get; }

        public bool HardClaim { get; }

        public bool Sync { get; }

        public float CooldownSeconds { get; }

        public float GainDb { get; }

        public float VolumeMultiplier { get; }

        public string Bus { get; }

        public string Policy { get; }

        public HashSet<string> SuppressVocalStates { get; }

        public HashSet<int> SuppressNarrationIds { get; }

        public bool Evaluate(object request)
        {
            return InvokeBool("Evaluate", request, true);
        }

        public bool MatchesProviderId(string requestedProviderId)
        {
            return MatchesProviderRequest(requestedProviderId, "", ownerStrict: false);
        }

        public bool MatchesProviderRequest(string requestedProviderId, string requestedOwnerModId, bool ownerStrict)
        {
            var request = (requestedProviderId ?? "").Trim();
            var owner = (requestedOwnerModId ?? "").Trim();
            if (request.Length == 0)
            {
                return true;
            }

            if (string.Equals(request, QualifiedProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(request, ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !ownerStrict
                || owner.Length == 0
                || string.Equals(owner, OwnerModId, StringComparison.OrdinalIgnoreCase);
        }

        public string GetLoadState()
        {
            return InvokeString("GetLoadState", "Disabled");
        }

        public AudioClip? GetClip(object request)
        {
            try
            {
                var method = providerType.GetMethod("GetClip", BindingFlags.Instance | BindingFlags.Public);
                if (method == null)
                {
                    return null;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 0
                    ? method.Invoke(provider, Array.Empty<object>()) as AudioClip
                    : method.Invoke(provider, new[] { request }) as AudioClip;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AudioArbiter] Provider GetClip failed: " + ProviderId + " -> " + ex.Message);
                return null;
            }
        }

        public string Describe()
        {
            return "providerId=" + ProviderId
                + ", qualifiedProviderId=" + QualifiedProviderId
                + ", owner=" + OwnerModId
                + ", priority=" + Priority
                + ", bus=" + Bus
                + ", policy=" + Policy
                + ", hardClaim=" + HardClaim
                + ", sync=" + Sync
                + ", gainDb=" + GainDb.ToString("0.##")
                + ", volumeMultiplier=" + VolumeMultiplier.ToString("0.###")
                + ", suppressNarrationIds=" + string.Join("|", SuppressNarrationIds);
        }

        public void Dispose(string reason)
        {
            try
            {
                if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                    AuraSharedLog.DebugLog("AudioArbiter", "Sound provider disposed: " + ProviderId + ", reason=" + reason, false);
                    return;
                }

                providerType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                    ?.Invoke(provider, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AudioArbiter] Sound provider dispose failed: " + ProviderId + " -> " + ex.Message);
            }
        }

        private string ReadString(string propertyName, string fallback)
        {
            return PropertyReader.ReadString(provider, propertyName, fallback);
        }

        private int ReadInt(string propertyName, int fallback)
        {
            return PropertyReader.ReadInt(provider, propertyName, fallback);
        }

        private bool ReadBool(string propertyName, bool fallback)
        {
            return PropertyReader.ReadBool(provider, propertyName, fallback);
        }

        private float ReadFloat(string propertyName, float fallback)
        {
            return PropertyReader.ReadFloat(provider, propertyName, fallback);
        }

        private static HashSet<string> SplitString(string value)
        {
            return new HashSet<string>(
                (value ?? "")
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<int> SplitInts(string value)
        {
            var result = new HashSet<int>();
            foreach (var item in (value ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(item.Trim(), out var id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private bool InvokeBool(string methodName, object arg, bool fallback)
        {
            try
            {
                var method = providerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
                if (method == null)
                {
                    return fallback;
                }

                return method.Invoke(provider, new[] { arg }) is bool value ? value : fallback;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AudioArbiter] Provider " + methodName + " failed: " + ProviderId + " -> " + ex.Message);
                return false;
            }
        }

        private string InvokeString(string methodName, string fallback)
        {
            try
            {
                var method = providerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
                return method?.Invoke(provider, Array.Empty<object>()) as string ?? fallback;
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

    private readonly struct ResolvedSound
    {
        public ResolvedSound(SoundProviderHandle provider, AudioClip clip)
        {
            Provider = provider;
            Clip = clip;
        }

        public SoundProviderHandle Provider { get; }

        public AudioClip Clip { get; }
    }

    private readonly struct PendingReplacement
    {
        public PendingReplacement(AudioClip? clip, string policy, float volumeMultiplier, float untilTime, int remaining)
        {
            Clip = clip;
            Policy = policy;
            VolumeMultiplier = volumeMultiplier;
            UntilTime = untilTime;
            Remaining = remaining;
        }

        public AudioClip? Clip { get; }

        public string Policy { get; }

        public float VolumeMultiplier { get; }

        public float UntilTime { get; }

        public int Remaining { get; }

        public PendingReplacement? ConsumeOne()
        {
            var next = Remaining - 1;
            return next <= 0 ? null : new PendingReplacement(Clip, Policy, VolumeMultiplier, UntilTime, next);
        }
    }

    private static class PropertyReader
    {
        public static string ReadString(object? source, string propertyName, string fallback = "")
        {
            try
            {
                var value = Read(source, propertyName);
                return value?.ToString() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static int ReadInt(object? source, string propertyName, int fallback)
        {
            try
            {
                var value = Read(source, propertyName);
                if (value is int typed)
                {
                    return typed;
                }

                return int.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static long ReadLong(object? source, string propertyName, long fallback)
        {
            try
            {
                var value = Read(source, propertyName);
                return value is long typed ? typed : Convert.ToInt64(value);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool ReadBool(object? source, string propertyName, bool fallback)
        {
            try
            {
                var value = Read(source, propertyName);
                if (value is bool typed)
                {
                    return typed;
                }

                return bool.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static float ReadFloat(object? source, string propertyName, float fallback)
        {
            try
            {
                var value = Read(source, propertyName);
                if (value is float typed)
                {
                    return typed;
                }

                return float.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static object? Read(object? source, string propertyName)
        {
            if (source == null)
            {
                return null;
            }

            return source.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(source);
        }
    }
}

[Serializable]
public sealed class AudioRegistryManifest
{
    public int schemaVersion = 1;
    public string ownerModId = "";
    public AudioProtocolManifest? audioProtocol;
    public AudioRegistryDefaults? defaults;
    public AudioProviderManifest[]? providers;
}

[Serializable]
public sealed class AudioProtocolManifest
{
    public int minVersion = 1;
    public int preferredVersion = 1;
}

[Serializable]
public sealed class AudioRegistryDefaults
{
    public string bus = "";
    public string policy = "";
    public bool? hardClaim;
    public bool? sync;
    public float? cooldownSeconds;
    public float? gainDb;
    public float? volumeMultiplier;
}

[Serializable]
public sealed class AudioProviderManifest
{
    public string providerId = "";
    public string ownerModId = "";
    public string kind = "";
    public string vocalState = "";
    public string bus = "";
    public string policy = "";
    public string path = "";
    public int priority;
    public bool? hardClaim;
    public bool? sync;
    public float? cooldownSeconds;
    public float? gainDb;
    public float? volumeMultiplier;
    public AudioProviderMatch? match;
    public AudioSuppressOriginal? suppressOriginal;
}

[Serializable]
public sealed class AudioProviderMatch
{
    public string[]? careerIds;
    public string[]? roleIds;
    public string[]? cardIds;
    public string[]? buffIds;
    public string[]? effectNames;
    public string[]? actionNames;
    public string[]? battleResults;
    public bool? localOwnerOnly;
    public float? hpRatioCrossDown;
}

[Serializable]
public sealed class AudioSuppressOriginal
{
    public string[]? vocalStates;
    public int[]? narrationIds;
}

public static class SoundEventKinds
{
    public const string CardUse = "CardUse";
    public const string SkillVoice = "SkillVoice";
    public const string CareerSelected = "CareerSelected";
    public const string BuffApplied = "BuffApplied";
    public const string LowHealth = "LowHealth";
    public const string BattleCompleted = "BattleCompleted";
    public const string VocalState = "VocalState";
}

public static class SoundBuses
{
    public const string Effect = "Effect";
    public const string Vocal = "Vocal";
    public const string Ui = "Ui";
}

public static class SoundPolicies
{
    public const string Additive = "Additive";
    public const string Replace = "Replace";
    public const string ReplaceOriginal = "ReplaceOriginal";
    public const string SuppressOriginal = "SuppressOriginal";
}

[Serializable]
public sealed class SoundPlaybackRequest
{
    public const int DefaultPresentationMaxAgeMilliseconds = 10000;
    [NonSerialized]
    public ModConfig? ModConfig;

    public string EventId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string Kind { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string StatusInstanceId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string BuffId { get; set; } = "";

    public string EffectName { get; set; } = "";

    public string ActionName { get; set; } = "";

    public string VocalState { get; set; } = "";

    public string BattleResult { get; set; } = "";

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public float PreviousHpRatio { get; set; }

    public float HpRatio { get; set; }

    public string SourceName { get; set; } = "";

    public long CreatedAtUtcTicks { get; set; }

    public int MaxAgeMilliseconds { get; set; }

    public bool IsRemote { get; set; }

    public bool DisableSync { get; set; }

    public bool IsLocalOwner { get; set; }

    public static SoundPlaybackRequest FromObject(object request)
    {
        if (request is SoundPlaybackRequest typed)
        {
            return typed;
        }

        return new SoundPlaybackRequest
        {
            EventId = AudioArbiterRuntime.ReadString(request, nameof(EventId)),
            ProviderId = AudioArbiterRuntime.ReadString(request, nameof(ProviderId)),
            OwnerModId = AudioArbiterRuntime.ReadString(request, nameof(OwnerModId)),
            Kind = AudioArbiterRuntime.ReadString(request, nameof(Kind)),
            CareerId = AudioArbiterRuntime.ReadString(request, nameof(CareerId)),
            RoleId = AudioArbiterRuntime.ReadString(request, nameof(RoleId)),
            StatusInstanceId = AudioArbiterRuntime.ReadString(request, nameof(StatusInstanceId)),
            CardId = AudioArbiterRuntime.ReadString(request, nameof(CardId)),
            BuffId = AudioArbiterRuntime.ReadString(request, nameof(BuffId)),
            EffectName = AudioArbiterRuntime.ReadString(request, nameof(EffectName)),
            ActionName = AudioArbiterRuntime.ReadString(request, nameof(ActionName)),
            VocalState = AudioArbiterRuntime.ReadString(request, nameof(VocalState)),
            BattleResult = AudioArbiterRuntime.ReadString(request, nameof(BattleResult)),
            Hp = AudioArbiterRuntime.ReadInt(request, nameof(Hp), 0),
            MaxHp = AudioArbiterRuntime.ReadInt(request, nameof(MaxHp), 0),
            PreviousHpRatio = AudioArbiterRuntime.ReadFloat(request, nameof(PreviousHpRatio), 0f),
            HpRatio = AudioArbiterRuntime.ReadFloat(request, nameof(HpRatio), 0f),
            SourceName = AudioArbiterRuntime.ReadString(request, nameof(SourceName)),
            CreatedAtUtcTicks = AudioArbiterRuntime.ReadLong(request, nameof(CreatedAtUtcTicks), 0L),
            MaxAgeMilliseconds = AudioArbiterRuntime.ReadInt(request, nameof(MaxAgeMilliseconds), 0),
            IsLocalOwner = AudioArbiterRuntime.ReadBool(request, nameof(IsLocalOwner), false)
        };
    }
}

public sealed class FileSoundProvider : IDisposable
{
    private readonly Func<object?, bool>? condition;
    private readonly string audioPath;
    private readonly ProviderRunner runner;
    private AudioClip? clip;
    private string loadState = "NotStarted";
    private int generation;
    private bool disposed;

    public FileSoundProvider(
        string providerId,
        string ownerModId,
        string audioPath,
        int priority,
        string bus,
        string policy,
        bool hardClaim,
        Func<object?, bool>? condition,
        float cooldownSeconds = 0f,
        bool sync = true,
        float gainDb = 0f,
        float volumeMultiplier = 1f,
        string kind = "",
        float? lowHealthCrossDownThreshold = null,
        string[]? suppressVocalStates = null,
        int[]? suppressNarrationIds = null)
    {
        ProviderId = providerId;
        OwnerModId = ownerModId;
        Kind = (kind ?? "").Trim();
        LowHealthCrossDownThreshold = lowHealthCrossDownThreshold ?? -1f;
        Priority = priority;
        Bus = bus;
        Policy = policy;
        HardClaim = hardClaim;
        CooldownSeconds = cooldownSeconds;
        Sync = sync;
        GainDb = gainDb;
        VolumeMultiplier = volumeMultiplier;
        SuppressVocalStates = string.Join("|", suppressVocalStates ?? Array.Empty<string>());
        SuppressNarrationIds = string.Join("|", suppressNarrationIds ?? Array.Empty<int>());
        this.audioPath = audioPath;
        this.condition = condition;

        var gameObject = new GameObject("AudioProvider." + ownerModId + "." + providerId);
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<ProviderRunner>();
        StartLoad();
    }

    public string ProviderId { get; }

    public string OwnerModId { get; }

    public string Kind { get; }

    public float LowHealthCrossDownThreshold { get; }

    public int Priority { get; }

    public string Bus { get; }

    public string Policy { get; }

    public bool HardClaim { get; }

    public bool Sync { get; }

    public float CooldownSeconds { get; }

    public float GainDb { get; }

    public float VolumeMultiplier { get; }

    public string SuppressVocalStates { get; }

    public string SuppressNarrationIds { get; }

    public bool Evaluate(object? request)
    {
        return condition == null || condition(request);
    }

    public string GetLoadState()
    {
        return loadState;
    }

    public AudioClip? GetClip(object? request)
    {
        return clip;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        generation++;
        runner.StopAllCoroutines();
        if (runner.gameObject != null)
        {
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        clip = null;
        loadState = "Disposed";
    }

    private void StartLoad()
    {
        generation++;
        var currentGeneration = generation;
        if (!File.Exists(audioPath))
        {
            loadState = "Missing";
            Debug.LogWarning("[AudioArbiter] Sound file missing: provider=" + ProviderId + ", path=" + audioPath);
            return;
        }

        var extension = Path.GetExtension(audioPath).ToLowerInvariant();
        if (extension == ".mp4" || extension == ".m4v" || extension == ".mov")
        {
            loadState = "Unsupported";
            Debug.LogWarning("[AudioArbiter] Sound file uses a video container and will not be loaded as AudioClip. "
                + "Export the audio track as .mp3, .wav, or .ogg. provider=" + ProviderId + ", path=" + audioPath);
            return;
        }

        loadState = "Loading";
        runner.LoadAudio(audioPath, currentGeneration, (completedGeneration, loadedClip, error) =>
        {
            if (disposed || completedGeneration != generation)
            {
                return;
            }

            if (loadedClip == null)
            {
                loadState = "Failed";
                Debug.LogWarning("[AudioArbiter] Sound load failed: provider=" + ProviderId + ", error=" + (error ?? "<none>"));
                return;
            }

            loadedClip.name = Path.GetFileNameWithoutExtension(audioPath);
            clip = loadedClip;
            loadState = "Ready";
            AuraSharedLog.DebugLog("AudioArbiter", "Sound loaded: provider=" + ProviderId + ", clip=" + loadedClip.name, false);
        });
    }

    private sealed class ProviderRunner : MonoBehaviour
    {
        public void LoadAudio(string path, int generation, Action<int, AudioClip?, string?> onCompleted)
        {
            StartCoroutine(LoadAudioCoroutine(path, generation, onCompleted));
        }

        private static IEnumerator LoadAudioCoroutine(string path, int generation, Action<int, AudioClip?, string?> onCompleted)
        {
            var uri = new Uri(path).AbsoluteUri;
            string? lastError = null;
            foreach (var audioType in ResolveAudioTypes(path))
            {
                using var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    lastError = "type=" + audioType + ", result=" + request.result + ", error=" + request.error;
                    continue;
                }

                AudioClip? loadedClip = null;
                string? error = null;
                try
                {
                    loadedClip = DownloadHandlerAudioClip.GetContent(request);
                }
                catch (Exception ex)
                {
                    error = ex.ToString();
                }

                if (loadedClip != null)
                {
                    onCompleted(generation, loadedClip, null);
                    yield break;
                }

                lastError = "type=" + audioType + ", contentError=" + (error ?? "AudioClip is null");
            }

            onCompleted(generation, null, lastError);
        }

        private static AudioType[] ResolveAudioTypes(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                    return new[] { AudioType.WAV };
                case ".ogg":
                    return new[] { AudioType.OGGVORBIS };
                case ".m4a":
                case ".aac":
                case ".mp3":
                default:
                    return new[] { AudioType.MPEG };
            }
        }
    }
}

public sealed class RpcAudioEvent : RpcCommandBase
{
    public RpcAudioEvent()
    {
        Event = new SoundPlaybackRequest();
    }

    public RpcAudioEvent(SoundPlaybackRequest request)
    {
        Event = new SoundPlaybackRequest
        {
            EventId = request.EventId,
            ProviderId = request.ProviderId,
            OwnerModId = request.OwnerModId,
            Kind = request.Kind,
            CareerId = request.CareerId,
            RoleId = request.RoleId,
            StatusInstanceId = request.StatusInstanceId,
            CardId = request.CardId,
            BuffId = request.BuffId,
            EffectName = request.EffectName,
            ActionName = request.ActionName,
            VocalState = request.VocalState,
            BattleResult = request.BattleResult,
            Hp = request.Hp,
            MaxHp = request.MaxHp,
            PreviousHpRatio = request.PreviousHpRatio,
            HpRatio = request.HpRatio,
            SourceName = request.SourceName,
            CreatedAtUtcTicks = request.CreatedAtUtcTicks,
            MaxAgeMilliseconds = request.MaxAgeMilliseconds,
            IsLocalOwner = request.IsLocalOwner,
            DisableSync = true
        };
    }

    public SoundPlaybackRequest Event { get; set; }

    public override void RpcExecute()
    {
        AudioArbiterRuntime.Initialize(null!, Event.OwnerModId);
        var arbiter = typeof(AudioArbiterRuntime)
            .GetMethod("RequestSound", BindingFlags.Static | BindingFlags.Public);

        Event.IsRemote = true;
        Event.DisableSync = true;
        arbiter?.Invoke(null, new object[] { Event });
    }
}
