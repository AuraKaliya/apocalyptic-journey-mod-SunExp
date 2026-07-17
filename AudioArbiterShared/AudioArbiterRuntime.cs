using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AudioArbiter.Shared;

public static class AudioArbiterRuntime
{
    private const string GlobalObjectName = "AudioArbiter.Global";
    private const string ComponentFullName = "AudioArbiter.Shared.AudioArbiterRuntime+AudioArbiterComponent";
    public const string CurrentBuildId = "audio-arbiter-2026-07-11-v8";
    public const int CurrentProtocolVersion = 6;
    public const int MinimumSupportedProtocolVersion = 6;
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
        return AudioPropertyReader.ReadString(source, propertyName);
    }

    public static int ReadInt(object? source, string propertyName, int fallback = 0)
    {
        return AudioPropertyReader.ReadInt(source, propertyName, fallback);
    }

    public static bool ReceiveRemote(SoundPlaybackRequest request)
    {
        var arbiter = EnsureArbiter(request.ModConfig, request.OwnerModId);
        if (arbiter == null)
        {
            return false;
        }

        try
        {
            var method = arbiter.GetType().GetMethod("ReceiveRemote", BindingFlags.Instance | BindingFlags.Public);
            return method != null && method.Invoke(arbiter, new object[] { request }) is bool accepted && accepted;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AudioArbiter] Remote sound request failed: " + ex.Message);
            return false;
        }
    }

    public static void ApplyFightSession(string fightToken, string source)
    {
        var arbiter = EnsureArbiter(null, "AudioArbiter");
        arbiter?.GetType().GetMethod("ApplyFightSession", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(arbiter, new object[] { fightToken, source });
    }

    public static void ApplyServerCardUsePresentation(SoundPlaybackRequest request, AuraRpcSender sender)
    {
        var arbiter = EnsureArbiter(null, "AudioArbiter");
        arbiter?.GetType().GetMethod("ApplyServerCardUsePresentation", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(arbiter, new object[] { request, sender });
    }

    public static long ReadLong(object? source, string propertyName, long fallback = 0L)
    {
        return AudioPropertyReader.ReadLong(source, propertyName, fallback);
    }

    public static float ReadFloat(object? source, string propertyName, float fallback = 0f)
    {
        return AudioPropertyReader.ReadFloat(source, propertyName, fallback);
    }

    public static bool ReadBool(object? source, string propertyName, bool fallback = false)
    {
        return AudioPropertyReader.ReadBool(source, propertyName, fallback);
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
        var methodsPresent = new[] { "RegisterSoundProvider", "RegisterManifest", "RequestSound", "ReceiveRemote", "ApplyFightSession", "ApplyServerCardUsePresentation" }
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
        private const float RemoteReplacementPairingSeconds = 0.15f;
        private const float RemoteFallbackSuppressionSeconds = 0.20f;
        private const float LowHealthNoProviderCooldownSeconds = 0.75f;
        private const float LowHealthRecoveryMargin = 0.05f;
        private const float LegacyLowHealthFallbackThreshold = 0.35f;
        private readonly List<SoundProviderHandle> soundProviders = new();
        private readonly AudioNetworkRuntime networkRuntime = new();
        private readonly AudioReplacementCoordinator<AudioClip> replacementCoordinator = new();
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
            AuraRpcAuthorityRuntime.Register(
                modConfig,
                "AudioArbiter",
                command => command is IAudioArbiterServerBoundRpcCommand,
                (command, sender) => ((IAudioArbiterServerBoundRpcCommand)command).BindServerSender(sender),
                Log,
                Warn);
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
                soundProviders.Sort((a, b) => AudioProviderResolver.CompareProviderOrder(
                    a.Priority,
                    a.QualifiedProviderId,
                    b.Priority,
                    b.QualifiedProviderId));
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

                var loaded = AudioManifestLoader.Load(
                    modConfig.DirectoryName,
                    owner,
                    manifestRelativePath,
                    SupportedManifestSchemaVersion,
                    CurrentProtocolVersion);
                if (!loaded.Success)
                {
                    Warn(loaded.Error);
                    return false;
                }

                var registered = 0;
                foreach (var provider in loaded.Providers)
                {
                    if (provider == null)
                    {
                        continue;
                    }

                    var providerId = provider.providerId?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(providerId))
                    {
                        Warn("Manifest provider skipped: providerId is empty. owner=" + loaded.ManifestOwner
                            + ", path=" + loaded.ManifestPath);
                        continue;
                    }

                    var plan = AudioManifestLoader.CreateProviderPlan(
                        provider,
                        loaded.Defaults,
                        loaded.ManifestOwner,
                        modConfig.DirectoryName,
                        AuraSharedPaths.ResolveSharedPath);
                    RegisterSoundProvider(new FileSoundProvider(
                        providerId: plan.ProviderId,
                        ownerModId: plan.OwnerModId,
                        audioPath: plan.AudioPath,
                        priority: plan.Priority,
                        bus: plan.Bus,
                        policy: plan.Policy,
                        hardClaim: plan.HardClaim,
                        condition: AudioManifestMatchPolicy.BuildCondition(provider),
                        cooldownSeconds: plan.CooldownSeconds,
                        sync: plan.Sync,
                        gainDb: plan.GainDb,
                        volumeMultiplier: plan.VolumeMultiplier,
                        kind: plan.Kind,
                        lowHealthCrossDownThreshold: plan.LowHealthCrossDownThreshold,
                        suppressVocalStates: plan.SuppressVocalStates,
                        suppressNarrationIds: plan.SuppressNarrationIds));
                    registered++;
                }

                Log("Manifest registered: owner=" + loaded.ManifestOwner + ", providers=" + registered
                    + ", path=" + loaded.ManifestPath);
                return registered > 0;
            }
            catch (Exception ex)
            {
                Warn("Manifest registration failed: owner=" + owner + " -> " + ex);
                return false;
            }
        }

        public bool RequestSound(object request)
        {
            var normalized = SoundPlaybackRequest.FromObject(request);
            return RequestSoundInternal(normalized, syncRemote: !normalized.IsRemote);
        }

        public bool ReceiveRemote(SoundPlaybackRequest request)
        {
            if (!networkRuntime.TryAcceptRemotePresentation(request))
            {
                return false;
            }

            request.IsRemote = true;
            RequestSoundInternal(request, syncRemote: false, presentationClaimed: true);
            return true;
        }

        private bool RequestSoundInternal(SoundPlaybackRequest request, bool syncRemote, bool presentationClaimed = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.EventId))
                {
                    request.EventId = Guid.NewGuid().ToString("N");
                }

                if (AudioNetworkPolicy.IsCardUsePresentation(request) && !request.IsRemote)
                {
                    if (!networkRuntime.TryPrepareAndRelayLocalPresentation(request, presentationClaimed))
                    {
                        return true;
                    }
                }

                var resolvedMaybe = Resolve(request);
                if (!resolvedMaybe.HasValue)
                {
                    RememberLowHealthNoProvider(request);
                    TraceRequest(request, "No provider resolved");
                    LogCardUseOutcome(request, null, "no-provider");
                    return false;
                }

                var resolved = resolvedMaybe.Value;
                if (!CanPassCooldown(resolved.Provider, request))
                {
                    TraceRequest(request, "Suppressed by cooldown: provider=" + resolved.Provider.ProviderId);
                    LogCardUseOutcome(request, resolved, "provider-cooldown");
                    return false;
                }

                var presentationPlan = AudioPresentationPolicy.CreatePlan(
                    resolved.Provider.Bus,
                    resolved.Provider.Policy,
                    request.Kind,
                    request.IsRemote,
                    RemoteReplacementPairingSeconds,
                    1.0f);
                if (presentationPlan.QueueNativeEffectReplacement)
                {
                    ArmOriginalSuppressions(resolved.Provider);
                    replacementCoordinator.Arm(
                        resolved.Clip,
                        resolved.Provider.Policy,
                        resolved.Provider.VolumeMultiplier,
                        Time.unscaledTime + presentationPlan.PairingSeconds,
                        request.EventId,
                        request.CardId,
                        request.RoleId,
                        resolved.Provider.QualifiedProviderId,
                        request.IsRemote,
                        fallbackAlreadyPlayed: false);
                    if (presentationPlan.StartRemoteFallback)
                    {
                        StartCoroutine(PlayRemoteReplacementFallback(request, resolved));
                    }
                    TraceRequest(request, "Pending effect replacement: provider=" + resolved.Provider.ProviderId);
                    LogCardUseOutcome(request, resolved, presentationPlan.PendingOutcome);
                    networkRuntime.SyncRemote(
                        request,
                        resolved.Provider.ProviderId,
                        resolved.Provider.OwnerModId,
                        resolved.Provider.Sync,
                        syncRemote);
                    return true;
                }

                TraceRequest(request, "Provider resolved: provider=" + resolved.Provider.ProviderId
                    + ", bus=" + resolved.Provider.Bus
                    + ", clip=" + resolved.Clip.name);
                ArmOriginalSuppressions(resolved.Provider);
                PlayResolved(request, resolved);
                LogCardUseOutcome(request, resolved, "played-direct");
                networkRuntime.SyncRemote(
                    request,
                    resolved.Provider.ProviderId,
                    resolved.Provider.OwnerModId,
                    resolved.Provider.Sync,
                    syncRemote);
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
            var resolution = AudioProviderResolver.Resolve<SoundProviderHandle, AudioClip>(
                soundProviders,
                request,
                request.ProviderId,
                request.OwnerModId,
                request.IsRemote,
                (provider, _) => TraceRequest(request, "Provider matched but not ready: provider="
                    + provider.ProviderId + ", state=" + provider.GetLoadState()),
                (provider, clip) => TraceRequest(request, "Provider clip selected: provider="
                    + provider.ProviderId + ", clip=" + clip.name));

            if (resolution.ShouldWarnRemoteMismatch)
            {
                WarnProviderMismatchOnce(request, "Remote sound provider mismatch");
            }

            if (resolution.UsedLegacyFallback)
            {
                WarnProviderMismatchOnce(
                    request,
                    "Local sound provider owner mismatch; falling back to legacy bare provider id");
            }

            if (!resolution.HasSelection || resolution.Provider == null || resolution.Resource == null)
            {
                return null;
            }

            request.ProviderId = resolution.Provider.QualifiedProviderId;
            request.OwnerModId = resolution.Provider.OwnerModId;
            return new ResolvedSound(resolution.Provider, resolution.Resource);
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
            return AudioProviderCooldownPolicy.TryAcquire(
                cooldownUntil,
                provider.QualifiedProviderId,
                request.Kind,
                request.RoleId,
                request.StatusInstanceId,
                provider.CooldownSeconds,
                Time.unscaledTime);
        }

        private void PlayResolved(SoundPlaybackRequest request, ResolvedSound resolved)
        {
            if (AudioPresentationPolicy.IsVocalBus(resolved.Provider.Bus))
            {
                var roleId = AudioPresentationPolicy.ResolveVocalRoleId(
                    request.StatusInstanceId,
                    request.RoleId,
                    request.CareerId,
                    resolved.Provider.OwnerModId,
                    resolved.Provider.ProviderId);
                AudioUnityPlaybackService.PlayVocal(roleId, resolved.Clip, resolved.Provider.VolumeMultiplier);
                TraceRequest(request, "Playing vocal: roleId=" + roleId
                    + ", provider=" + resolved.Provider.ProviderId
                    + ", clip=" + resolved.Clip.name
                    + ", gainDb=" + resolved.Provider.GainDb.ToString("0.##")
                    + ", volumeMultiplier=" + resolved.Provider.VolumeMultiplier.ToString("0.###"));
                return;
            }

            AudioUnityPlaybackService.PlayEffect(resolved.Clip, resolved.Provider.VolumeMultiplier);
            TraceRequest(request, "Playing effect: provider=" + resolved.Provider.ProviderId
                + ", clip=" + resolved.Clip.name
                + ", gainDb=" + resolved.Provider.GainDb.ToString("0.##")
                + ", volumeMultiplier=" + resolved.Provider.VolumeMultiplier.ToString("0.###"));
        }

        private void ArmOriginalSuppressions(SoundProviderHandle provider)
        {
            AudioSuppressionPolicy.ArmNarrationSuppressions(
                suppressNarrationUntil,
                provider.SuppressNarrationIds,
                Time.unscaledTime,
                1.5f);
        }

        private void OnFightStartBefore(ModHookContext context)
        {
            replacementCoordinator.Clear();
            cooldownUntil.Clear();
            lowHealthAnnounced.Clear();
            lastHpRatioByStatus.Clear();
            lowHealthNoProviderUntil.Clear();
            suppressNarrationUntil.Clear();
            networkRuntime.BeginFightSession();
        }

        public void ApplyFightSession(string token, string source)
        {
            if (!networkRuntime.ApplyFightSession(token, source))
            {
                return;
            }

            replacementCoordinator.ClearPairingClaims();
        }

        public void ApplyServerCardUsePresentation(SoundPlaybackRequest request, AuraRpcSender sender)
        {
            networkRuntime.ApplyServerCardUsePresentation(request, sender, ReceiveRemote);
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
            if (!context.IsCardAction || !IsLocalOwnerStatus(context.OwnerStatus, context.OwnerInstanceId))
            {
                return;
            }

            var roleId = string.IsNullOrWhiteSpace(context.OwnerRoleId)
                ? context.CurrentRoleId
                : context.OwnerRoleId;
            RequestSoundInternal(new SoundPlaybackRequest
            {
                EventId = networkRuntime.ReuseOrCreateLocalPlayId(
                    context.OwnerInstanceId,
                    context.CardId,
                    context.Action,
                    context.Effects,
                    Time.unscaledTime),
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

        private static bool IsLocalOwnerStatus(StatusManager? status, string statusInstanceId)
        {
            try
            {
                var playerId = PlayerManager.Instance?.PlayerId ?? "";
                return (!string.IsNullOrWhiteSpace(playerId)
                        && string.Equals(playerId, statusInstanceId, StringComparison.Ordinal))
                       || ReferenceEquals(FightPlayer.Instance?.Status, status);
            }
            catch
            {
                return false;
            }
        }

        private void OnEffectSoundBefore(ModHookContext context)
        {
            if (!replacementCoordinator.HasActivePending(Time.unscaledTime))
            {
                return;
            }

            var effectSound = context.Target as EffectSound;
            if (effectSound == null)
            {
                return;
            }

            var decision = replacementCoordinator.ConsumeNativeEffect(Time.unscaledTime);
            var pending = decision.Pending;
            if (!decision.Handled || pending == null)
            {
                return;
            }

            switch (decision.Action)
            {
                case AudioNativeEffectAction.SuppressOriginal:
                    effectSound.clip = null;
                    break;
                case AudioNativeEffectAction.PlayReplacementAfterDelay:
                    effectSound.clip = null;
                    if (pending.Resource != null)
                    {
                        StartCoroutine(PlayEffectAfterDelay(
                            Math.Max(0f, effectSound.delay),
                            pending.Resource,
                            pending.VolumeMultiplier));
                    }
                    break;
                case AudioNativeEffectAction.ReplaceOriginalClip:
                    effectSound.clip = pending.Resource;
                    break;
            }

            if (pending.IsRemote)
            {
                LogPendingReplacementOutcome(pending, decision.RemoteOutcome);
            }
        }

        private IEnumerator PlayRemoteReplacementFallback(SoundPlaybackRequest request, ResolvedSound resolved)
        {
            var until = Time.unscaledTime + RemoteReplacementPairingSeconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }

            if (replacementCoordinator.TryClaimPairedFallback(request.EventId))
            {
                yield break;
            }

            replacementCoordinator.ClearPendingForEvent(request.EventId);

            PlayResolved(request, resolved);
            LogCardUseOutcome(request, resolved, "remote-fallback-played");

            // The native remote EffectSound may arrive after the presentation
            // packet. Keep a short suppress-only tail so that a late original
            // sound cannot play on top of the already played replacement.
            replacementCoordinator.Arm(
                resolved.Clip,
                SoundPolicies.SuppressOriginal,
                resolved.Provider.VolumeMultiplier,
                Time.unscaledTime + RemoteFallbackSuppressionSeconds,
                request.EventId,
                request.CardId,
                request.RoleId,
                resolved.Provider.QualifiedProviderId,
                request.IsRemote,
                fallbackAlreadyPlayed: true);
        }

        private static IEnumerator PlayEffectAfterDelay(float delaySeconds, AudioClip clip, float volumeMultiplier)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            AudioUnityPlaybackService.PlayEffect(clip, volumeMultiplier);
        }

        private static void LogCardUseOutcome(SoundPlaybackRequest request, ResolvedSound? resolved, string outcome)
        {
            if (!AudioNetworkPolicy.IsCardUsePresentation(request))
            {
                return;
            }

            AuraSharedLog.Info("AudioArbiter", "Card-use presentation outcome: outcome=" + outcome
                + ", eventId=" + request.EventId
                + ", cardId=" + request.CardId
                + ", roleId=" + request.RoleId
                + ", provider=" + (resolved?.Provider.QualifiedProviderId ?? "<none>")
                + ", policy=" + (resolved?.Provider.Policy ?? "<none>")
                + ", remote=" + request.IsRemote);
        }

        private static void LogPendingReplacementOutcome(AudioPendingReplacement<AudioClip> pending, string outcome)
        {
            AuraSharedLog.Info("AudioArbiter", "Card-use presentation outcome: outcome=" + outcome
                + ", eventId=" + pending.EventId
                + ", cardId=" + pending.CardId
                + ", roleId=" + pending.RoleId
                + ", provider=" + pending.ProviderId
                + ", policy=" + pending.Policy
                + ", remote=" + pending.IsRemote);
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

            var shouldSuppress = AudioSuppressionPolicy.ShouldSuppressNarration(
                suppressNarrationUntil,
                ids,
                Time.unscaledTime);

            if (!shouldSuppress)
            {
                return;
            }

            AudioUnityPlaybackService.StopVocalSource("Krisna");
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

}
