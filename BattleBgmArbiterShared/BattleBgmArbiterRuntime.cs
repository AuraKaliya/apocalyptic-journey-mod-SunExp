using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraAudio.Shared;
using AuraShared.Core;
using AudioArbiter.Shared;
using UnityEngine;
using UnityEngine.Networking;
using Witch;
using Witch.Core;
using Witch.UI;
using Witch.Mod;
using Witch.UI.Window;

namespace BattleBgmArbiter.Shared;

public static class BattleBgmArbiterRuntime
{
    private const string GlobalObjectName = "BattleBgmArbiter.Global";
    private const string ComponentFullName = "BattleBgmArbiter.Shared.BattleBgmArbiterRuntime+BattleBgmArbiterComponent";
    public const string CurrentBuildId = "battle-bgm-arbiter-2026-07-20-v5";
    public const int CurrentProtocolVersion = 3;
    public const int MinimumSupportedProtocolVersion = 3;
    public static bool VerboseLogging { get; set; }
    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityWarningsShown = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig, string ownerModId)
    {
        EnsureArbiter(modConfig, ownerModId);
    }

    public static void RegisterProvider(ModConfig modConfig, string ownerModId, object provider)
    {
        var arbiter = EnsureArbiter(modConfig, ownerModId);
        if (arbiter == null)
        {
            return;
        }

        var method = arbiter.GetType().GetMethod("RegisterProvider", BindingFlags.Instance | BindingFlags.Public);
        if (method == null)
        {
            Debug.LogWarning("[BattleBgmArbiter] Existing arbiter does not expose RegisterProvider");
            return;
        }

        try
        {
            method.Invoke(arbiter, new[] { provider });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BattleBgmArbiter] Provider registration failed for " + ownerModId + ": " + ex.Message);
        }
    }

    public static void Signal(ModConfig modConfig, string ownerModId, string signal, object? payload = null)
    {
        var arbiter = EnsureArbiter(modConfig, ownerModId);
        if (arbiter == null)
        {
            return;
        }

        try
        {
            var method = arbiter.GetType().GetMethod("Signal", BindingFlags.Instance | BindingFlags.Public);
            method?.Invoke(arbiter, new[] { signal, payload });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BattleBgmArbiter] Signal failed for " + ownerModId + ": " + ex.Message);
        }
    }

    private static object? EnsureArbiter(ModConfig modConfig, string ownerModId)
    {
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            var existing = FindArbiterComponent(gameObject);
            if (existing != null)
            {
                return ValidateExistingArbiter(existing, ownerModId) ? existing : null;
            }
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var component = gameObject.AddComponent<BattleBgmArbiterComponent>();
        component.InitializeOwner(modConfig, ownerModId);
        AuraSharedLog.DebugLog("BattleBgmArbiter", "Created global arbiter, owner=" + ownerModId, false);
        return component;
    }

    private static bool ValidateExistingArbiter(object existing, string ownerModId)
    {
        var existingType = existing.GetType();
        var protocolVersion = ReadIntProperty(existing, "ProtocolVersion", 0);
        var minimumSupported = ReadIntProperty(existing, "MinimumSupportedProtocolVersion", int.MaxValue);
        var buildId = ReadStringProperty(existing, "BuildId");
        var methodsPresent = new[] { "RegisterProvider", "Signal" }
            .All(name => existingType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null);
        if (ReuseLogOwners.Add(ownerModId))
        {
            AuraSharedLog.DebugLog(
                "BattleBgmArbiter",
                "Reusing global arbiter for " + ownerModId
                + ", ownerType=" + existingType.Assembly.GetName().Name
                + ", protocol=" + (protocolVersion <= 0 ? "unknown" : protocolVersion.ToString())
                + ", minSupported=" + (minimumSupported == int.MaxValue ? "unknown" : minimumSupported.ToString())
                + ", buildId=" + (string.IsNullOrWhiteSpace(buildId) ? "<missing>" : buildId),
                false);
        }

        var compatible = protocolVersion >= MinimumSupportedProtocolVersion
            && minimumSupported <= CurrentProtocolVersion
            && methodsPresent;
        if (!compatible)
        {
            WarnCompatibilityOnce(
                "incompatible:" + existingType.AssemblyQualifiedName,
                "Incompatible global arbiter; BGM features disabled for " + ownerModId
                + ". protocol=" + protocolVersion
                + ", minSupported=" + minimumSupported
                + ", buildId=" + (string.IsNullOrWhiteSpace(buildId) ? "<missing>" : buildId)
                + ", localBuildId=" + CurrentBuildId
                + ", methodsPresent=" + methodsPresent);
        }

        if (compatible
            && !string.IsNullOrWhiteSpace(buildId)
            && !string.Equals(buildId, CurrentBuildId, StringComparison.Ordinal)
            && CompatibilityWarningsShown.Add("build:" + ownerModId + ":" + buildId))
        {
            Debug.LogWarning("[BattleBgmArbiter] Reusing protocol-compatible arbiter with a different build. owner="
                + ownerModId + ", existingBuildId=" + buildId + ", localBuildId=" + CurrentBuildId);
        }

        return compatible;
    }

    private static void WarnCompatibilityOnce(string key, string message)
    {
        if (CompatibilityWarningsShown.Add(key))
        {
            Debug.LogError("[BattleBgmArbiter] " + message);
        }
    }

    private static int ReadIntProperty(object source, string propertyName, int fallback)
    {
        try
        {
            var value = source.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(source);
            return value is int typed ? typed : fallback;
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
            return source.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(source) as string ?? "";
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

    public sealed class BattleBgmArbiterComponent : MonoBehaviour
    {
        private const int SilentSampleRate = 44100;
        private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<ProviderHandle> providers = new();
        private AudioClip? silentClip;
        private BgmSnapshot? preBattleSnapshot;
        private AdventureBgmContext? adventureContext;
        private BattleBgmContext? currentBattleContext;
        private string activeProviderId = "";
        private BattleAudioMode battleMode = BattleAudioMode.None;
        private string ownerModId = "";
        private bool hooksRegistered;
        private bool inBattle;
        private long battleSessionId;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => BattleBgmArbiterRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

        public void InitializeOwner(ModConfig modConfig, string owner)
        {
            ownerModId = owner;
            if (hooksRegistered)
            {
                return;
            }

            hooksRegistered = true;
            RegisterBefore(modConfig, "GameEntryUI.StartGame", CapturePreparationContext);
            RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", CaptureRoleTableContext);
            RegisterAfter(modConfig, "SlotMachineManager.InitRoleTable", CaptureRoleTableContext);
            RegisterAfter(modConfig, "SublimationManager.InitRoleTable", CaptureRoleTableContext);
            RegisterAfter(modConfig, "TeachMapManager.InitRoleTable", CaptureRoleTableContext);
            RegisterBefore(modConfig, "FightInit.Init", OnBeforeFightInit);
            RegisterAfter(modConfig, "FightInit.Init", OnAfterFightInit);
            RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
            RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
            RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);
            Log("Hooks registered by owner=" + ownerModId);
        }

        public void RegisterProvider(object provider)
        {
            try
            {
                var handle = new ProviderHandle(provider);
                if (string.IsNullOrWhiteSpace(handle.ProviderId))
                {
                    Warn("Provider registration skipped: ProviderId is empty. providerType=" + provider.GetType().FullName);
                    handle.Dispose("empty ProviderId");
                    return;
                }

                var replaced = providers
                    .Where(item => string.Equals(item.QualifiedProviderId, handle.QualifiedProviderId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var previous in replaced)
                {
                    previous.Dispose("replaced by new registration");
                }

                providers.RemoveAll(item => string.Equals(item.QualifiedProviderId, handle.QualifiedProviderId, StringComparison.OrdinalIgnoreCase));
                providers.Add(handle);
                providers.Sort((a, b) =>
                {
                    var priorityCompare = b.Priority.CompareTo(a.Priority);
                    return priorityCompare != 0
                        ? priorityCompare
                        : string.Compare(a.QualifiedProviderId, b.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
                });

                Log("Provider registered: " + handle.Describe() + ", count=" + providers.Count);
                EvaluateAdventureProviders("provider registered");
            }
            catch (Exception ex)
            {
                Warn("Provider registration failed: " + ex);
            }
        }

        public void Signal(string signal, object? payload)
        {
            Log("Signal received: " + signal + ", payload=" + (payload == null ? "<null>" : payload.GetType().Name));
            if (string.Equals(signal, "AdventureContextChanged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "CardPackChanged", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "CareerChanged", StringComparison.OrdinalIgnoreCase))
            {
                EnsureAdventureContext("signal:" + signal);
                EvaluateAdventureProviders("signal:" + signal);
                return;
            }

            if (string.Equals(signal, "BattleBgmSwitchRequested", StringComparison.OrdinalIgnoreCase))
            {
                HandleBattleBgmSwitchRequested(payload);
            }
        }

        private void CapturePreparationContext(ModHookContext context)
        {
            try
            {
                if (inBattle)
                {
                    Warn("Clearing stale battle BGM state before new preparation context. previousSession=" + battleSessionId);
                    ClearBattleState("new preparation context");
                }

                var gameEntry = context.Target as GameEntryUI;
                var careerId = ReadDataConfigId(GameEntryUI.career);
                var packs = ReadCardPacksFromGameEntry(gameEntry);
                adventureContext = new AdventureBgmContext
                {
                    AdventureId = Guid.NewGuid().ToString("N"),
                    CareerId = careerId,
                    EnabledCardPackIds = packs,
                    ModeType = ReadModeType(),
                    Source = "GameEntryUI.StartGame",
                    IsMultiplayer = IsMultiplayer(),
                    IsHost = IsHost()
                };

                Log("Preparation context captured: " + adventureContext.Describe());
                EvaluateAdventureProviders("preparation context");
            }
            catch (Exception ex)
            {
                Warn("Failed to capture preparation context: " + ex);
            }
        }

        private void CaptureRoleTableContext(ModHookContext context)
        {
            try
            {
                var roleTable = context.Arguments != null && context.Arguments.Length > 0
                    ? context.Arguments[0] as RoleTable
                    : RoleTable.Instance;
                var source = context.Target == null ? "InitRoleTable" : context.Target.GetType().Name + ".InitRoleTable";
                adventureContext = new AdventureBgmContext
                {
                    AdventureId = adventureContext?.AdventureId ?? Guid.NewGuid().ToString("N"),
                    CareerId = ReadDataConfigId(roleTable?.Career),
                    EnabledCardPackIds = ReadRuntimeCardPacks(),
                    ModeType = ReadModeType(context.Target),
                    Source = source,
                    IsMultiplayer = IsMultiplayer(),
                    IsHost = IsHost()
                };

                Log("Adventure context captured: " + adventureContext.Describe());
                EvaluateAdventureProviders("role table context");
            }
            catch (Exception ex)
            {
                Warn("Failed to capture role table context: " + ex);
            }
        }

        private void OnBeforeFightInit(ModHookContext context)
        {
            try
            {
                EnsureAdventureContext("fight init before");
                if (inBattle)
                {
                    if (!TryClearStaleBattleBeforeNewFight("FightInit.Init"))
                    {
                        Warn("A battle is already active; keeping previous pre-battle snapshot. session=" + battleSessionId);
                        return;
                    }
                }

                battleSessionId++;
                inBattle = true;
                battleMode = BattleAudioMode.None;
                var manager = AudioManager.Instance;
                if (manager == null)
                {
                    Warn("AudioManager.Instance is null before battle; no BGM snapshot saved. session=" + battleSessionId);
                    preBattleSnapshot = null;
                    return;
                }

                preBattleSnapshot = BgmSnapshot.Capture(manager);
                Log("Pre-battle BGM snapshot saved. session=" + battleSessionId + ", " + preBattleSnapshot.Describe());
            }
            catch (Exception ex)
            {
                Warn("Failed before fight init: " + ex);
            }
        }

        private void OnAfterFightInit(ModHookContext context)
        {
            try
            {
                var battleContext = BuildBattleContext();
                currentBattleContext = battleContext;
                activeProviderId = "";
                Log("Battle context built: " + battleContext.Describe());

                foreach (var provider in providers)
                {
                    if (!provider.EvaluateAdventure(adventureContext) || !provider.EvaluateBattle(battleContext))
                    {
                        Log("Provider skipped: " + provider.ProviderId);
                        continue;
                    }

                    var loadState = provider.GetLoadState();
                    Log("Provider candidate: " + provider.ProviderId + ", priority=" + provider.Priority + ", state=" + loadState);

                    if (string.Equals(loadState, "Ready", StringComparison.OrdinalIgnoreCase))
                    {
                        var clip = provider.GetClip();
                        if (clip != null)
                        {
                            ReplaceCurrentBattleBgm(provider, clip, "battle start");
                            battleMode = BattleAudioMode.Replaced;
                            return;
                        }
                    }

                    if (string.Equals(loadState, "Loading", StringComparison.OrdinalIgnoreCase))
                    {
                        if (provider.HardClaim && provider.SilenceWhenLoading)
                        {
                            SilenceCurrentBattleBgm(provider, "battle start loading");
                            battleMode = BattleAudioMode.SilentBecauseLoading;
                            return;
                        }
                    }
                    else if (string.Equals(loadState, "Missing", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(loadState, "Failed", StringComparison.OrdinalIgnoreCase))
                    {
                        if (provider.HardClaim)
                        {
                            if (!provider.FallbackToOriginalWhenFailed && provider.SilenceWhenLoading)
                            {
                                SilenceCurrentBattleBgm(provider, "battle start failed without fallback");
                                battleMode = BattleAudioMode.SilentBecauseLoading;
                                Log("Provider hard-claimed and failed without original fallback; silencing battle BGM. provider=" + provider.ProviderId);
                                return;
                            }

                            battleMode = BattleAudioMode.OriginalBecauseFailedOrMissing;
                            Log("Provider hard-claimed but is unavailable; keeping original battle BGM. provider=" + provider.ProviderId
                                + ", fallbackOriginal=" + provider.FallbackToOriginalWhenFailed);
                            return;
                        }
                    }

                    if (provider.HardClaim)
                    {
                        battleMode = BattleAudioMode.OriginalBecauseFailedOrMissing;
                        Log("Provider hard-claimed with unhandled state; keeping original battle BGM. provider=" + provider.ProviderId);
                        return;
                    }
                }

                battleMode = BattleAudioMode.OriginalBecauseNoProvider;
                Log("No provider selected; keeping original battle BGM");
            }
            catch (Exception ex)
            {
                battleMode = BattleAudioMode.OriginalBecauseFailedOrMissing;
                Warn("Failed after fight init; keeping original battle BGM. Error: " + ex);
            }
        }

        private void OnFightEnded(ModHookContext context)
        {
            try
            {
                var hookTargetName = context.Target == null ? "<null>" : context.Target.GetType().Name;
                if (string.Equals(hookTargetName, "Fight_Loss", StringComparison.Ordinal)
                    && FightManager.Instance != null
                    && FightManager.Instance.IsFake)
                {
                    Log("Fake loss detected; BGM settlement deferred until escape reset. session=" + battleSessionId);
                    return;
                }

                if (!inBattle)
                {
                    Log("Duplicate fight end ignored. target=" + hookTargetName + ", lastSession=" + battleSessionId);
                    return;
                }

                var completedSession = battleSessionId;
                var completedMode = battleMode;
                var snapshot = preBattleSnapshot;
                ClearBattleState("fight end claimed by " + hookTargetName);
                Log("Fight end detected. target=" + hookTargetName + ", session=" + completedSession + ", battleMode=" + completedMode);

                var manager = AudioManager.Instance;
                if (manager == null)
                {
                    Warn("AudioManager.Instance is null on fight end; cannot restore BGM. session=" + completedSession);
                    return;
                }

                if (snapshot == null)
                {
                    Warn("No pre-battle snapshot exists; leaving current BGM unchanged. session=" + completedSession);
                    return;
                }

                snapshot.Restore(manager);
                Log("Pre-battle BGM restored. session=" + completedSession + ", " + snapshot.Describe());
            }
            catch (Exception ex)
            {
                Warn("Failed to restore pre-battle BGM: " + ex);
                ClearBattleState("fight end exception");
            }
        }

        private void HandleBattleBgmSwitchRequested(object? payload)
        {
            try
            {
                var providerId = ReadPayloadString(payload, "ProviderId");
                var reason = ReadPayloadString(payload, "Reason");
                var force = ReadPayloadBool(payload, "Force", false);
                var allowSilenceWhenLoading = ReadPayloadBool(payload, "AllowSilenceWhenLoading", false);
                var restartIfSameClip = ReadPayloadBool(payload, "RestartIfSameClip", true);

                if (!inBattle)
                {
                    Log("Battle switch ignored: no active battle. provider=" + providerId + ", reason=" + reason);
                    return;
                }

                if (string.IsNullOrWhiteSpace(providerId))
                {
                    Warn("Battle switch ignored: ProviderId is empty. reason=" + reason);
                    return;
                }

                var provider = providers.FirstOrDefault(item => item.MatchesProviderId(providerId));
                if (provider == null)
                {
                    Warn("Battle switch ignored: provider not registered. provider=" + providerId + ", reason=" + reason);
                    return;
                }

                var battleContext = currentBattleContext ?? BuildBattleContext();
                currentBattleContext = battleContext;
                if (!provider.EvaluateAdventure(adventureContext) || !provider.EvaluateBattle(battleContext))
                {
                    Log("Battle switch ignored: provider is not eligible for current battle. provider=" + providerId + ", reason=" + reason);
                    return;
                }

                if (!provider.AllowMidBattleSwitch && !force)
                {
                    Log("Battle switch ignored: provider does not allow mid-battle switch. provider=" + providerId + ", reason=" + reason);
                    return;
                }

                var loadState = provider.GetLoadState();
                Log("Battle switch requested: provider=" + providerId + ", reason=" + reason + ", state=" + loadState + ", force=" + force);

                if (string.Equals(loadState, "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    var clip = provider.GetClip(payload);
                    if (clip == null)
                    {
                        Warn("Battle switch ignored: provider returned null clip. provider=" + providerId + ", reason=" + reason);
                        return;
                    }

                    var manager = AudioManager.Instance;
                    var currentClip = manager?.bgmSource == null ? null : manager.bgmSource.clip;
                    if (!restartIfSameClip && ReferenceEquals(currentClip, clip))
                    {
                        Log("Battle switch skipped: requested clip is already active. provider=" + providerId + ", reason=" + reason);
                        return;
                    }

                    ReplaceCurrentBattleBgm(provider, clip, "mid-battle switch:" + reason);
                    battleMode = BattleAudioMode.Replaced;
                    return;
                }

                if (string.Equals(loadState, "Loading", StringComparison.OrdinalIgnoreCase) && allowSilenceWhenLoading && provider.SilenceWhenLoading)
                {
                    SilenceCurrentBattleBgm(provider, "mid-battle switch loading:" + reason);
                    battleMode = BattleAudioMode.SilentBecauseLoading;
                    return;
                }

                Log("Battle switch ignored: provider is not ready. provider=" + providerId + ", reason=" + reason + ", state=" + loadState);
            }
            catch (Exception ex)
            {
                Warn("Battle switch request failed: " + ex);
            }
        }

        private void ReplaceCurrentBattleBgm(ProviderHandle provider, AudioClip clip, string reason)
        {
            var manager = AudioManager.Instance;
            if (manager == null)
            {
                Warn("Cannot replace battle BGM because AudioManager.Instance is null");
                return;
            }

            var source = manager.bgmSource;
            var originalClipName = source.clip == null ? "<null>" : source.clip.name;
            source.Stop();
            source.clip = clip;
            source.loop = true;
            source.time = 0f;
            source.Play();
            activeProviderId = provider.QualifiedProviderId;

            Log("Battle BGM replaced by provider=" + provider.ProviderId + ", reason=" + reason + ": " + originalClipName + " -> " + clip.name
                + ", length=" + clip.length.ToString("0.000") + "s");
        }

        private void SilenceCurrentBattleBgm(ProviderHandle provider, string reason)
        {
            var manager = AudioManager.Instance;
            if (manager == null)
            {
                Warn("Cannot silence battle BGM because AudioManager.Instance is null");
                return;
            }

            var source = manager.bgmSource;
            var originalClipName = source.clip == null ? "<null>" : source.clip.name;
            source.Stop();
            source.clip = EnsureSilentClip();
            source.loop = true;
            source.time = 0f;
            source.Play();
            activeProviderId = provider.QualifiedProviderId;

            Log("Battle BGM silenced by provider=" + provider.ProviderId + ", reason=" + reason + ". Original clip=" + originalClipName);
        }

        private AudioClip EnsureSilentClip()
        {
            if (silentClip != null)
            {
                return silentClip;
            }

            silentClip = AudioClip.Create("BattleBgmArbiter.SilentBattleBgm", SilentSampleRate, 1, SilentSampleRate, false);
            Log("Silent placeholder AudioClip created");
            return silentClip;
        }

        private BattleBgmContext BuildBattleContext()
        {
            EnsureAdventureContext("battle context");
            var levelId = FightManager.Instance == null ? "" : FightManager.Instance.level;
            var levelBgm = "";
            var note = "";
            var enemies = new List<string>();

            try
            {
                if (!string.IsNullOrWhiteSpace(levelId))
                {
                    var row = Singleton<GameConfigManager>.Instance.GetOne(DataType.Level, levelId);
                    if (row != null)
                    {
                        row.TryGetValue("BGM", out levelBgm);
                        row.TryGetValue("Note", out note);
                        if (row.TryGetValue("EnemyIds", out var enemyIds) && !string.IsNullOrWhiteSpace(enemyIds))
                        {
                            enemies.AddRange(enemyIds.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Warn("Battle level context read failed: " + ex.Message);
            }

            return new BattleBgmContext
            {
                Adventure = adventureContext,
                CareerId = adventureContext?.CareerId ?? "",
                EnabledCardPackIds = adventureContext?.EnabledCardPackIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                ModeType = adventureContext?.ModeType ?? "",
                LevelId = levelId ?? "",
                LevelBgmName = levelBgm ?? "",
                EnemyIds = enemies,
                IsBoss = string.Equals(note, "boss", StringComparison.OrdinalIgnoreCase),
                IsHighTide = RoleTable.Instance != null && RoleTable.Instance.InHighTide
            };
        }

        private void EnsureAdventureContext(string reason)
        {
            if (adventureContext != null)
            {
                return;
            }

            adventureContext = new AdventureBgmContext
            {
                AdventureId = Guid.NewGuid().ToString("N"),
                CareerId = ReadDataConfigId(RoleTable.Instance?.Career ?? GameEntryUI.career),
                EnabledCardPackIds = ReadRuntimeCardPacks(),
                ModeType = ReadModeType(),
                Source = "fallback:" + reason,
                IsMultiplayer = IsMultiplayer(),
                IsHost = IsHost()
            };
            Log("Fallback adventure context created: " + adventureContext.Describe());
            EvaluateAdventureProviders("fallback context");
        }

        private void EvaluateAdventureProviders(string reason)
        {
            if (adventureContext == null)
            {
                return;
            }

            var eligible = providers
                .Where(provider => provider.EvaluateAdventure(adventureContext))
                .Select(provider => provider.ProviderId)
                .ToList();
            Log("Adventure provider evaluation. reason=" + reason + ", eligible=" + string.Join("|", eligible));
        }

        private static HashSet<string> ReadCardPacksFromGameEntry(GameEntryUI? gameEntry)
        {
            try
            {
                if (gameEntry?.cardPackUI?.UseCardPack != null && gameEntry.cardPackUI.UseCardPack.Count > 0)
                {
                    return new HashSet<string>(gameEntry.cardPackUI.UseCardPack, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            return ReadRuntimeCardPacks();
        }

        private static HashSet<string> ReadRuntimeCardPacks()
        {
            try
            {
                var packs = Singleton<GameRuntimeData>.Instance?.UseCardPack;
                if (packs != null)
                {
                    return new HashSet<string>(packs, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string ReadDataConfigId(DataConfig? dataConfig)
        {
            try
            {
                if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
                {
                    return id ?? "";
                }
            }
            catch
            {
            }

            return "";
        }

        private static string ReadModeType(object? modeManager = null)
        {
            if (modeManager != null)
            {
                return modeManager.GetType().Name;
            }

            try
            {
                return LobbyManager.Instance?.CurrentLobbyModeType ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool IsMultiplayer()
        {
            try
            {
                return LobbyManager.Instance != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsHost()
        {
            try
            {
                return PlayerManager.Instance == null || PlayerManager.Instance.isServer;
            }
            catch
            {
                return true;
            }
        }

        private void ClearBattleState(string reason)
        {
            Log("Battle state cleared. reason=" + reason + ", previousMode=" + battleMode + ", session=" + battleSessionId);
            inBattle = false;
            activeProviderId = "";
            battleMode = BattleAudioMode.None;
            currentBattleContext = null;
            preBattleSnapshot = null;
        }

        private bool TryClearStaleBattleBeforeNewFight(string source)
        {
            var fightUiMissing = false;
            try
            {
                fightUiMissing = UIManager.Instance?.GetUI<FightUI>("FightUI") == null;
            }
            catch
            {
                fightUiMissing = true;
            }

            if (!fightUiMissing)
            {
                return false;
            }

            Warn("Clearing stale battle BGM state before new fight. source="
                + source
                + ", previousSession="
                + battleSessionId
                + ", fightUiMissing="
                + fightUiMissing);
            ClearBattleState("stale before " + source);
            return true;
        }

        private static string ReadPayloadString(object? payload, string propertyName)
        {
            try
            {
                return payload?.GetType()
                    .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(payload) as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool ReadPayloadBool(object? payload, string propertyName, bool fallback)
        {
            try
            {
                var value = payload?.GetType()
                    .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(payload);
                return value is bool typed ? typed : fallback;
            }
            catch
            {
                return fallback;
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

        private static T? GetField<T>(AudioManager manager, string fieldName)
        {
            var field = typeof(AudioManager).GetField(fieldName, InstancePrivate);
            if (field == null)
            {
                Debug.LogWarning("[BattleBgmArbiter] AudioManager private field not found: " + fieldName);
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
                Debug.LogWarning("[BattleBgmArbiter] AudioManager private field not found while restoring: " + fieldName);
                return;
            }

            field.SetValue(manager, value);
        }

        private void Log(string message)
        {
            AuraSharedLog.DebugLog("BattleBgmArbiter", message, false);
        }

        private void Warn(string message)
        {
            Debug.LogWarning("[BattleBgmArbiter] " + message);
        }

        private enum BattleAudioMode
        {
            None,
            Replaced,
            SilentBecauseLoading,
            OriginalBecauseFailedOrMissing,
            OriginalBecauseNoProvider
        }

        private sealed class ProviderHandle
        {
            private readonly object provider;
            private readonly Type providerType;

            public ProviderHandle(object provider)
            {
                this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
                providerType = provider.GetType();
                ProviderId = ReadString("ProviderId", providerType.FullName ?? "unknown");
                OwnerModId = ReadString("OwnerModId", "");
                if (string.IsNullOrWhiteSpace(OwnerModId))
                {
                    OwnerModId = providerType.Assembly.GetName().Name ?? "";
                }

                QualifiedProviderId = QualifyProviderId(OwnerModId, ProviderId);
                Priority = ReadInt("Priority", 0);
                HardClaim = ReadBool("HardClaim", false);
                SilenceWhenLoading = ReadBool("SilenceWhenLoading", false);
                FallbackToOriginalWhenFailed = ReadBool("FallbackToOriginalWhenFailed", true);
                AllowMidBattleSwitch = ReadBool("AllowMidBattleSwitch", false);
            }

            public string ProviderId { get; }

            private string OwnerModId { get; }

            public string QualifiedProviderId { get; }

            public int Priority { get; }

            public bool HardClaim { get; }

            public bool SilenceWhenLoading { get; }

            public bool FallbackToOriginalWhenFailed { get; }

            public bool AllowMidBattleSwitch { get; }

            public bool EvaluateAdventure(object? context)
            {
                return InvokeBool("EvaluateAdventure", context, true);
            }

            public bool EvaluateBattle(object? context)
            {
                return InvokeBool("EvaluateBattle", context, true);
            }

            public bool MatchesProviderId(string requestedProviderId)
            {
                var request = (requestedProviderId ?? "").Trim();
                return request.Length == 0
                       || string.Equals(request, ProviderId, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(request, QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
            }

            public string GetLoadState()
            {
                return InvokeString("GetLoadState", "Disabled");
            }

            public AudioClip? GetClip(object? signalPayload = null)
            {
                try
                {
                    var signalMethod = providerType.GetMethod("GetClipForSignal", BindingFlags.Instance | BindingFlags.Public);
                    if (signalMethod != null)
                    {
                        return signalMethod.Invoke(provider, new[] { signalPayload }) as AudioClip;
                    }

                    return providerType.GetMethod("GetClip", BindingFlags.Instance | BindingFlags.Public)?.Invoke(provider, Array.Empty<object>()) as AudioClip;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BattleBgmArbiter] Provider GetClip failed: " + ProviderId + " -> " + ex.Message);
                    return null;
                }
            }

            public string Describe()
            {
                return "providerId=" + ProviderId
                    + ", qualifiedProviderId=" + QualifiedProviderId
                    + ", owner=" + OwnerModId
                    + ", priority=" + Priority
                    + ", hardClaim=" + HardClaim
                    + ", silenceWhenLoading=" + SilenceWhenLoading
                    + ", fallbackOriginal=" + FallbackToOriginalWhenFailed
                    + ", allowMidBattleSwitch=" + AllowMidBattleSwitch;
            }

            public void Dispose(string reason)
            {
                try
                {
                    if (provider is IDisposable disposable)
                    {
                        disposable.Dispose();
                        AuraSharedLog.DebugLog("BattleBgmArbiter", "Provider disposed: " + ProviderId + ", reason=" + reason, false);
                        return;
                    }

                    var method = providerType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        method.Invoke(provider, Array.Empty<object>());
                        AuraSharedLog.DebugLog("BattleBgmArbiter", "Provider disposed through reflection: " + ProviderId + ", reason=" + reason, false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BattleBgmArbiter] Provider dispose failed: " + ProviderId + " -> " + ex.Message);
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

            private string ReadString(string propertyName, string fallback)
            {
                try
                {
                    return providerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider) as string ?? fallback;
                }
                catch
                {
                    return fallback;
                }
            }

            private int ReadInt(string propertyName, int fallback)
            {
                try
                {
                    var value = providerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider);
                    return value is int typed ? typed : fallback;
                }
                catch
                {
                    return fallback;
                }
            }

            private bool ReadBool(string propertyName, bool fallback)
            {
                try
                {
                    var value = providerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(provider);
                    return value is bool typed ? typed : fallback;
                }
                catch
                {
                    return fallback;
                }
            }

            private bool InvokeBool(string methodName, object? argument, bool fallback)
            {
                try
                {
                    var method = providerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
                    if (method == null)
                    {
                        return fallback;
                    }

                    var result = method.Invoke(provider, new[] { argument });
                    return result is bool typed ? typed : fallback;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BattleBgmArbiter] Provider " + methodName + " failed: " + ProviderId + " -> " + ex.Message);
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
                catch (Exception ex)
                {
                    Debug.LogWarning("[BattleBgmArbiter] Provider " + methodName + " failed: " + ProviderId + " -> " + ex.Message);
                    return "Failed";
                }
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

            private BgmSnapshot(SourceSnapshot? mainSource, SourceSnapshot? backgroundSource, List<AudioClip> playList, List<AudioClip> backgroundPlayList, int bgmIndex, int backgroundBgmIndex, string nowBgmName, string backgroundBgmName, bool mainBgmMuted, bool backgroundBgmMuted)
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
                return source == null
                    ? null
                    : new SourceSnapshot(source.clip, SafeReadTime(source), source.isPlaying, source.loop, source.volume, source.mute);
            }

            public void Restore(AudioSource source)
            {
                source.Stop();
                source.clip = clip;
                source.loop = loop;
                source.volume = volume;
                source.mute = mute;

                if (clip == null)
                {
                    return;
                }

                SafeSetTime(source, time);
                if (wasPlaying)
                {
                    source.Play();
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
                    source.time = Mathf.Clamp(value, 0f, Mathf.Max(0f, source.clip.length - 0.01f));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BattleBgmArbiter] Failed to restore AudioSource time: " + ex.Message);
                }
            }
        }
    }
}

public sealed class AdventureBgmContext
{
    public string AdventureId { get; set; } = "";

    public string CareerId { get; set; } = "";

    public HashSet<string> EnabledCardPackIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ModeType { get; set; } = "";

    public string Source { get; set; } = "";

    public bool IsMultiplayer { get; set; }

    public bool IsHost { get; set; }

    public string Describe()
    {
        return "id=" + AdventureId
            + ", career=" + CareerId
            + ", mode=" + ModeType
            + ", multiplayer=" + IsMultiplayer
            + ", host=" + IsHost
            + ", source=" + Source
            + ", packs=" + string.Join("|", EnabledCardPackIds.OrderBy(item => item));
    }
}

public sealed class BattleBgmContext
{
    public AdventureBgmContext? Adventure { get; set; }

    public string CareerId { get; set; } = "";

    public HashSet<string> EnabledCardPackIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ModeType { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string LevelBgmName { get; set; } = "";

    public List<string> EnemyIds { get; set; } = new();

    public bool IsBoss { get; set; }

    public bool IsHighTide { get; set; }

    public string Describe()
    {
        return "career=" + CareerId
            + ", mode=" + ModeType
            + ", level=" + LevelId
            + ", levelBgm=" + LevelBgmName
            + ", boss=" + IsBoss
            + ", highTide=" + IsHighTide
            + ", enemies=" + string.Join("|", EnemyIds)
            + ", packs=" + string.Join("|", EnabledCardPackIds.OrderBy(item => item));
    }
}

public sealed class BattleBgmSwitchRequest
{
    public string ProviderId { get; set; } = "";

    public string Reason { get; set; } = "";

    public bool Force { get; set; }

    public bool AllowSilenceWhenLoading { get; set; }

    public bool RestartIfSameClip { get; set; } = true;
}

public sealed class FileBattleBgmProvider : IDisposable
{
    private const float FileCheckIntervalSeconds = 60f;

    private readonly Func<object?, bool>? adventureCondition;
    private readonly Func<object?, bool>? battleCondition;
    private readonly string audioPath;
    private readonly ProviderRunner runner;
    private AudioClip? clip;
    private FileSignature cachedSignature = FileSignature.Missing;
    private string loadState = "NotStarted";
    private int generation;
    private bool disposed;
    private AudioFileFormatDescriptor? currentFormat;

    public FileBattleBgmProvider(
        string providerId,
        string ownerModId,
        string audioPath,
        int priority,
        bool hardClaim,
        bool silenceWhenLoading,
        bool fallbackToOriginalWhenFailed,
        Func<object?, bool>? adventureCondition,
        Func<object?, bool>? battleCondition,
        bool allowMidBattleSwitch = false)
    {
        ProviderId = providerId;
        OwnerModId = ownerModId;
        Priority = priority;
        HardClaim = hardClaim;
        SilenceWhenLoading = silenceWhenLoading;
        FallbackToOriginalWhenFailed = fallbackToOriginalWhenFailed;
        AllowMidBattleSwitch = allowMidBattleSwitch;
        this.audioPath = audioPath;
        this.adventureCondition = adventureCondition;
        this.battleCondition = battleCondition;

        var gameObject = new GameObject("BattleBgmProvider." + ownerModId + "." + providerId);
        UnityEngine.Object.DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<ProviderRunner>();
        StartLoad("provider initialize");
        runner.StartFileWatcher(FileCheckIntervalSeconds, CheckAudioFile);
    }

    public string ProviderId { get; }

    public string OwnerModId { get; }

    public int Priority { get; }

    public bool HardClaim { get; }

    public bool SilenceWhenLoading { get; }

    public bool FallbackToOriginalWhenFailed { get; }

    public bool AllowMidBattleSwitch { get; }

    public bool EvaluateAdventure(object? context)
    {
        return adventureCondition == null || adventureCondition(context);
    }

    public bool EvaluateBattle(object? context)
    {
        return battleCondition == null || battleCondition(context);
    }

    public string GetLoadState()
    {
        return loadState;
    }

    public AudioClip? GetClip()
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
        runner.Shutdown();
        if (runner.gameObject != null)
        {
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        clip = null;
        loadState = "Disposed";
        AuraSharedLog.DebugLog("BattleBgmArbiter", LogPrefix + "Provider disposed. provider=" + ProviderId, false);
    }

    private void StartLoad(string reason)
    {
        if (disposed)
        {
            Debug.LogWarning(LogPrefix + "BGM load skipped because provider is disposed. provider=" + ProviderId + ", reason=" + reason);
            return;
        }

        generation++;
        var currentGeneration = generation;
        cachedSignature = ReadCurrentSignature();

        if (!cachedSignature.Exists)
        {
            clip = null;
            loadState = "Missing";
            Debug.LogWarning(LogPrefix + "BGM file missing. provider=" + ProviderId + ", reason=" + reason + ", path=" + audioPath);
            return;
        }

        currentFormat = AudioFileFormatProbe.Probe(audioPath);
        AuraSharedLog.Info("BattleBgmArbiter", LogPrefix + "BGM probe. provider=" + ProviderId
            + ", path=" + audioPath
            + ", sourceExtension=" + Display(Path.GetExtension(audioPath))
            + ", " + currentFormat.Describe());
        if (!currentFormat.Success || !AudioUnityFileLoadPolicy.TryResolve(currentFormat, out var audioType))
        {
            clip = null;
            loadState = "Failed";
            Debug.LogWarning(LogPrefix + "BGM probe failed. provider=" + ProviderId
                + ", path=" + audioPath
                + ", failureCode=" + Display(currentFormat.FailureCode)
                + ", message=" + currentFormat.Message);
            return;
        }

        clip = null;
        loadState = "Loading";
        AuraSharedLog.Info("BattleBgmArbiter", LogPrefix + "BGM load started. provider=" + ProviderId
            + ", reason=" + reason
            + ", generation=" + currentGeneration
            + ", signature=" + cachedSignature
            + ", format=" + currentFormat.Format
            + ", codec=" + currentFormat.Codec
            + ", unityAudioType=" + audioType);
        runner.LoadAudio(audioPath, audioType, currentGeneration, OnLoadCompleted);
    }

    private void OnLoadCompleted(int completedGeneration, AudioClip? loadedClip, string? error)
    {
        if (disposed)
        {
            return;
        }

        if (completedGeneration != generation)
        {
            AuraSharedLog.DebugLog("BattleBgmArbiter", LogPrefix + "Ignored stale load result. provider=" + ProviderId + ", generation=" + completedGeneration + ", active=" + generation, false);
            return;
        }

        cachedSignature = ReadCurrentSignature();
        if (loadedClip == null)
        {
            clip = null;
            loadState = cachedSignature.Exists ? "Failed" : "Missing";
            Debug.LogWarning(LogPrefix + "BGM load failed. provider=" + ProviderId + ", state=" + loadState + ", signature=" + cachedSignature + ", error=" + (error ?? "<none>"));
            return;
        }

        loadedClip.name = Path.GetFileNameWithoutExtension(audioPath);
        clip = loadedClip;
        loadState = "Ready";
        AuraSharedLog.Info("BattleBgmArbiter", LogPrefix + "BGM load ready. provider=" + ProviderId
            + ", path=" + audioPath
            + ", format=" + (currentFormat?.Format.ToString() ?? "Unknown")
            + ", codec=" + (currentFormat?.Codec ?? "Unknown")
            + ", signature=" + cachedSignature
            + ", clip=" + loadedClip.name
            + ", length=" + loadedClip.length.ToString("0.000") + "s"
            + ", frequency=" + loadedClip.frequency
            + ", channels=" + loadedClip.channels);
    }

    private void CheckAudioFile()
    {
        try
        {
            if (disposed)
            {
                return;
            }

            var currentSignature = ReadCurrentSignature();
            if (currentSignature.Equals(cachedSignature))
            {
                if (BattleBgmArbiterRuntime.VerboseLogging)
                {
                    AuraSharedLog.DebugLog("BattleBgmArbiter", LogPrefix + "File watcher check: unchanged. provider=" + ProviderId + ", signature=" + currentSignature + ", state=" + loadState, false);
                }

                return;
            }

            AuraSharedLog.DebugLog("BattleBgmArbiter", LogPrefix + "File watcher check: changed. provider=" + ProviderId + ", cached=" + cachedSignature + ", current=" + currentSignature, false);
            StartLoad("file watcher detected resource change");
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogPrefix + "File watcher failed. provider=" + ProviderId + ", error=" + ex);
        }
    }

    private FileSignature ReadCurrentSignature()
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
            Debug.LogWarning(LogPrefix + "Failed to read BGM file signature. provider=" + ProviderId + ", error=" + ex.Message);
            return FileSignature.Missing;
        }
    }

    private string LogPrefix => "[" + OwnerModId + ".BGM] ";

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value ?? "<none>";
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

    private sealed class ProviderRunner : MonoBehaviour
    {
        private Coroutine? watcherCoroutine;

        public void LoadAudio(
            string path,
            AudioType audioType,
            int generation,
            Action<int, AudioClip?, string?> onCompleted)
        {
            StartCoroutine(LoadAudioCoroutine(path, audioType, generation, onCompleted));
        }

        public void StartFileWatcher(float intervalSeconds, Action onCheck)
        {
            if (watcherCoroutine != null)
            {
                StopCoroutine(watcherCoroutine);
            }

            watcherCoroutine = StartCoroutine(FileWatcherCoroutine(intervalSeconds, onCheck));
        }

        public void Shutdown()
        {
            if (watcherCoroutine != null)
            {
                StopCoroutine(watcherCoroutine);
                watcherCoroutine = null;
            }

            StopAllCoroutines();
        }

        private static IEnumerator LoadAudioCoroutine(
            string path,
            AudioType audioType,
            int generation,
            Action<int, AudioClip?, string?> onCompleted)
        {
            var uri = new Uri(path).AbsoluteUri;
            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onCompleted(generation, null, "result=" + request.result + ", error=" + request.error);
                yield break;
            }

            AudioClip? loadedClip = null;
            string? error = null;
            try
            {
                loadedClip = DownloadHandlerAudioClip.GetContent(request);
                if (loadedClip == null)
                {
                    error = "DownloadHandlerAudioClip.GetContent returned null";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            onCompleted(generation, loadedClip, error);
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
