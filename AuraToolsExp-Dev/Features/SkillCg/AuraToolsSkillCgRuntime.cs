using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Cg;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.SkillCg;

public static class AuraToolsSkillCgRuntime
{
    private static readonly List<IDisposable> HookRegistrations = new();
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);
    private static ModConfig? modConfig;
    private static bool hooksRegistered;
    private static int hookFailureCount;
    private static bool safeModeDisabled;
    private static int adventurePreloadSequence;
    private static bool reconcilingRegistry;
    private static long observedRegistryRevision = -1;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized) return;
        initialized = true;
        AuraToolsSkillCgRuntime.modConfig = modConfig;
        SkillCgArbiterRuntime.Initialize(modConfig, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
        AuraToolsCgVisualBootstrap.Initialize();
        SkillCgArbiterRuntime.RegisterSceneAssetResolver(
            modConfig,
            AuraToolsIds.ModId,
            new AuraToolsCgSceneAssetResolver());
        SkillCgArbiterRuntime.RegisterProvider(modConfig, AuraToolsIds.ModId, new AuraToolsSkillCgProvider());

        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.SkillCg,
            Reconfigure);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.CardUseCg,
            Reconfigure);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.EventCg,
            Reconfigure);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.FeastCg,
            Reconfigure);
        AuraCgRegistryRuntime.Changed += OnRegistryChanged;
        EnsureRegistryStateCurrent();
        EnsureHooksMatchConfig();
    }

    private static void Reconfigure()
    {
        hookFailureCount = 0;
        safeModeDisabled = false;
        AuraToolsSkillCgProvider.ClearPathCache();
        SkillCgArbiterRuntime.Initialize(null, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
        EnsureRegistryStateCurrent(force: true);
        EnsureHooksMatchConfig();
    }

    private static bool HooksEnabled => !safeModeDisabled
                                        && (AuraToolsConfigService.SkillCg.Enabled
                                            || AuraToolsConfigService.SkillCg.CardUseCg.Enabled
                                            || AuraToolsConfigService.SkillCg.EventCg.Enabled);

    private static void EnsureHooksMatchConfig()
    {
        if (HooksEnabled)
        {
            EnsureHooksRegistered();
            return;
        }

        ReleaseHooks();
    }

    private static void EnsureHooksRegistered()
    {
        if (hooksRegistered || modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraCardActionTransactionRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            AuraToolsIds.ModId + ".SkillCG",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.PresentationCommitted,
                Handler = BeforeCombatAction
            },
            warn: AuraToolsLog.Warn));
        HookRegistrations.Add(AuraSkillActionTransactionRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            AuraToolsIds.ModId + ".SkillCG.SkillAction",
            new AuraSkillActionSubscription
            {
                Phases = AuraSkillActionPhase.Committed,
                Handler = OnSkillActionCommitted
            },
            warn: AuraToolsLog.Warn));
        HookRegistrations.Add(AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "SkillCG",
            new AuraBattleLifecycleSubscription
            {
                AdventureStarting = OnAdventureStart,
                BattleOpening = OnFightStart,
                BattleRestarting = OnFightRestarting,
                OutcomeEntering = AuraToolsCgEventSignalService.OutcomeEntering,
                BattleEnded = outcome => OnFightEnded(outcome.NativeContext)
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));
        var signalHooks = new AuraHookRegistry(
            modConfig,
            AuraToolsIds.ModId + ".CG.Signals",
            AuraToolsLog.Debug,
            AuraToolsLog.Warn);
        signalHooks.BeforeRouted(
            "StatusManager.set_CurHp",
            AuraToolsCgLowHealthSignalService.BeforeCurHpChanged,
            "RoleLowHealth.Before");
        signalHooks.AfterRouted(
            "StatusManager.set_CurHp",
            AuraToolsCgLowHealthSignalService.AfterCurHpChanged,
            "RoleLowHealth.After");
        signalHooks.BeforeRouted(
            "GameApp.GameOver",
            AuraToolsCgEventSignalService.AdventureSettlement,
            "AdventureSettlement.GameApp");
        signalHooks.BeforeRouted(
            "PlayerManager.GameOver",
            AuraToolsCgEventSignalService.AdventureSettlement,
            "AdventureSettlement.PlayerManager");
        signalHooks.AfterRouted(
            "GameExitUI.Start",
            AuraToolsCgEventSignalService.AdventureSettlement,
            "AdventureSettlement.ExitUi");
        HookRegistrations.Add(signalHooks);
        hooksRegistered = true;
        AuraToolsLog.Info("[SkillCG] routed hooks enabled.");
    }

    private static void ReleaseHooks()
    {
        if (!hooksRegistered && HookRegistrations.Count == 0)
        {
            return;
        }

        for (var i = HookRegistrations.Count - 1; i >= 0; i--)
        {
            try
            {
                HookRegistrations[i].Dispose();
            }
            catch
            {
            }
        }

        HookRegistrations.Clear();
        hooksRegistered = false;
        SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "disabled");
        AuraToolsSkillCgProvider.ClearOwnerRoles();
        AuraToolsCgLowHealthSignalService.Reset();
        AuraToolsCgEventSignalService.Reset();
        AuraToolsLog.Info("[SkillCG] routed hooks disabled.");
    }

    private static void BeforeCombatAction(AuraCardActionContext context)
    {
        RunHook("card action", () =>
        {
            EnsureRegistryStateCurrent();
            if ((!AuraToolsConfigService.SkillCg.Enabled && !AuraToolsConfigService.SkillCg.CardUseCg.Enabled)
                || string.IsNullOrWhiteSpace(context.CardDataId))
            {
                return;
            }

            var trigger = BuildTriggerContext(context);
            if (trigger == null || !ShouldEmitLocalRequest(trigger))
            {
                return;
            }

            var signal = AuraCgSignalContext.FromLegacy(trigger);
            signal.ConfigureResolvedRequest = request =>
            {
                request.DisableSync = !AuraToolsConfigService.SkillCg.SyncRemote;
                ApplyCardUsePresentationOverride(request);
            };
            SkillCgArbiterRuntime.EmitSignal(AuraToolsConfigService.SkillCg, AuraToolsIds.ModId, signal);
        });
    }

    private static SkillCgTriggerContext? BuildTriggerContext(AuraCardActionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CardDataId))
        {
            return null;
        }

        AuraToolsSkillCgProvider.RememberOwnerRole(context.OwnerStatusId, context.OwnerRoleId);
        return new SkillCgTriggerContext
        {
            TriggerKind = "card",
            ActionSequence = context.Sequence,
            EventToken = context.TransactionId,
            Action = context.Action,
            CardId = context.CardDataId,
            OwnerInstanceId = context.OwnerStatusId,
            OwnerRoleId = context.OwnerRoleId,
            CreatedAt = context.CreatedAt
        };
    }

    private static void OnSkillActionCommitted(AuraSkillActionContext context)
    {
        RunHook("skill action", () =>
        {
            EnsureRegistryStateCurrent();
            if (!AuraToolsConfigService.SkillCg.Enabled
                || string.IsNullOrWhiteSpace(context.SkillDataId))
            {
                return;
            }

            AuraToolsSkillCgProvider.RememberOwnerRole(context.OwnerStatusId, context.OwnerRoleId);
            var trigger = new SkillCgTriggerContext
            {
                TriggerKind = "skill",
                ActionSequence = context.Sequence,
                EventToken = context.TransactionId,
                Action = "Skill",
                SkillId = context.SkillDataId,
                CardId = context.SkillDataId,
                OwnerInstanceId = context.OwnerStatusId,
                OwnerRoleId = context.OwnerRoleId,
                CreatedAt = Time.unscaledTime
            };
            if (ShouldEmitLocalRequest(trigger))
            {
                var signal = AuraCgSignalContext.FromLegacy(trigger);
                signal.ConfigureResolvedRequest = request =>
                {
                    request.DisableSync = !AuraToolsConfigService.SkillCg.SyncRemote;
                    ApplyRegisteredSkillPresentationOverride(request);
                };
                SkillCgArbiterRuntime.EmitSignal(
                    AuraToolsConfigService.SkillCg,
                    AuraToolsIds.ModId,
                    signal);
            }
        });
    }

    private static bool ShouldEmitLocalRequest(SkillCgTriggerContext trigger)
    {
        if (PlayerManager.Instance == null)
        {
            return true;
        }

        var localStatusId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        if (string.IsNullOrWhiteSpace(trigger.OwnerInstanceId)
            || string.IsNullOrWhiteSpace(localStatusId)
            || string.Equals(trigger.OwnerInstanceId, localStatusId, StringComparison.Ordinal))
        {
            return true;
        }

        LogDiagnostic("remote-owner:" + trigger.OwnerInstanceId + ":" + trigger.CardId,
            "[SkillCG] local request skipped for remote owner: owner="
            + trigger.OwnerInstanceId
            + ", local="
            + localStatusId
            + ", card="
            + trigger.CardId);
        return false;
    }

    internal static string ReadCurrentCareerId()
    {
        return ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
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

    private static void OnFightStart(ModHookContext context)
    {
        RunHook("fight start", () =>
        {
            AuraToolsSkillCgProvider.ClearOwnerRoles();
            EnsureRegistryStateCurrent();
            AuraToolsCgLowHealthSignalService.Reset();
            AuraToolsCgEventSignalService.BattleOpening(context);
        });
    }

    private static void OnAdventureStart(ModHookContext context)
    {
        RunHook("adventure preload", () =>
        {
            EnsureRegistryStateCurrent();
            AuraToolsCgEventSignalService.BeginAdventure(context);
            PreloadAdventureCg();
        });
    }

    private static void OnRegistryChanged(long revision)
    {
        if (revision != observedRegistryRevision)
        {
            EnsureRegistryStateCurrent();
        }
    }

    private static void EnsureRegistryStateCurrent(bool force = false)
    {
        if (reconcilingRegistry)
        {
            return;
        }

        var snapshot = AuraCgRegistryRuntime.GetSnapshot();
        if (!force && snapshot.Revision == observedRegistryRevision)
        {
            return;
        }

        reconcilingRegistry = true;
        try
        {
            // Legacy settings are mapped only after every currently loaded
            // content/tool manifest is visible. New registrations notify us,
            // so late loading and late joining use the same path.
            AuraToolsConfigService.ImportRegisteredSkillCgDefaults();
            SynchronizeRegisteredEffectiveState(snapshot);
            observedRegistryRevision = snapshot.Revision;
        }
        finally
        {
            reconcilingRegistry = false;
        }
    }

    private static void SynchronizeRegisteredEffectiveState(AuraCgRegistrySnapshot snapshot)
    {
        var rootEnabled = !safeModeDisabled;
        var skillEnabled = rootEnabled && AuraToolsConfigService.SkillCg.Enabled;
        var cardUseEnabled = rootEnabled && AuraToolsConfigService.SkillCg.CardUseCg.Enabled;
        var eventEnabled = rootEnabled && AuraToolsConfigService.SkillCg.EventCg.Enabled;
        var feastEnabled = skillEnabled && AuraToolsConfigService.MatchExperience.Feast.IsCgEffective;
        var overrides = new List<AuraCgLocalActivationOverride>();
        foreach (var entry in snapshot.Entries)
        {
            if (string.Equals(entry.SubjectType, AuraCgSubjectTypes.Card, StringComparison.OrdinalIgnoreCase))
            {
                var cardConfigured = !AuraToolsConfigService.SkillCg.CardUseCg.RegisteredEntries.TryGetValue(
                                         entry.QualifiedCgId,
                                         out var enabled)
                                     || enabled;
                overrides.Add(new AuraCgLocalActivationOverride
                {
                    OwnerModId = entry.OwnerModId,
                    CgId = entry.CgId,
                    Enabled = cardUseEnabled && cardConfigured
                });
                continue;
            }

            if (string.Equals(entry.SubjectType, AuraCgSubjectTypes.Event, StringComparison.OrdinalIgnoreCase))
            {
                overrides.Add(new AuraCgLocalActivationOverride
                {
                    OwnerModId = entry.OwnerModId,
                    CgId = entry.CgId,
                    Enabled = eventEnabled
                });
                continue;
            }

            if (entry.Signals.Any(signal => string.Equals(
                    signal,
                    AuraCgSignals.RoleFeastCompleted,
                    StringComparison.OrdinalIgnoreCase)))
            {
                overrides.Add(new AuraCgLocalActivationOverride
                {
                    OwnerModId = entry.OwnerModId,
                    CgId = entry.CgId,
                    Enabled = feastEnabled
                });
                continue;
            }

            if (!string.Equals(entry.SubjectType, AuraCgSubjectTypes.Role, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var configured = AuraToolsConfigService.SkillCg.Roles.Values
                .Where(role => role != null && role.Enabled)
                .SelectMany(role => role.Rules ?? Enumerable.Empty<SkillCgRuleSettings>())
                .Where(rule => rule != null
                               && string.Equals(rule.SourceOwnerModId, entry.OwnerModId, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(rule.SourceCgId, entry.CgId, StringComparison.OrdinalIgnoreCase))
                .Any(rule => rule.Enabled);

            // Imported entries default to enabled. Once a tool rule exists, its
            // local enabled state is the effective state for this machine.
            var hasConfiguredRule = AuraToolsConfigService.SkillCg.Roles.Values
                .Where(role => role != null)
                .SelectMany(role => role.Rules ?? Enumerable.Empty<SkillCgRuleSettings>())
                .Any(rule => rule != null
                             && string.Equals(rule.SourceOwnerModId, entry.OwnerModId, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(rule.SourceCgId, entry.CgId, StringComparison.OrdinalIgnoreCase));
            overrides.Add(new AuraCgLocalActivationOverride
            {
                OwnerModId = entry.OwnerModId,
                CgId = entry.CgId,
                Enabled = skillEnabled && (!hasConfiguredRule || configured)
            });
        }

        AuraCgActivationRuntime.ReplaceLocalOverrides(AuraToolsIds.ModId, overrides);
    }

    private static void PreloadAdventureCg()
    {
        var key = "AuraToolsExp.Adventure." + (++adventurePreloadSequence).ToString();
        if (AuraToolsConfigService.SkillCg.Enabled)
        {
            SkillCgArbiterRuntime.EnsureAdventurePreloaded(
                AuraToolsIds.ModId,
                "",
                key + ".registered",
                new[] { SkillCgArbiterRuntime.SkillCgKind, SkillCgArbiterRuntime.FeastCgKind });
            SkillCgArbiterRuntime.PreloadCg(AuraToolsIds.ModId, AuraToolsSkillCgProvider.BuildConfiguredPreloadRequests());
        }

        if (AuraToolsConfigService.SkillCg.CardUseCg.Enabled)
        {
            SkillCgArbiterRuntime.EnsureAdventurePreloaded(
                AuraToolsIds.ModId,
                "",
                key + ".card-use",
                new[] { SkillCgArbiterRuntime.CardUseCgKind });
        }
    }

    private static void OnFightEnded(ModHookContext context)
    {
        RunHook("fight ended", () =>
        {
            AuraToolsSkillCgProvider.ClearOwnerRoles();
            AuraToolsCgLowHealthSignalService.Reset();
        });
    }

    private static void OnFightRestarting(ModHookContext context)
    {
        RunHook("fight restarting", () =>
        {
            AuraToolsSkillCgProvider.ClearOwnerRoles();
            AuraToolsCgLowHealthSignalService.Reset();
            AuraToolsCgEventSignalService.BattleRestarting();
        });
    }

    private static void RunHook(string source, Action action)
    {
        if (safeModeDisabled)
        {
            return;
        }

        try
        {
            action();
            hookFailureCount = 0;
        }
        catch (Exception ex)
        {
            hookFailureCount++;
            var maxFailures = AuraToolsConfigService.SkillCg.MaxHookFailures;
            AuraToolsLog.Warn("[SkillCG] hook failed. source=" + source
                              + ", failures=" + hookFailureCount + "/" + maxFailures
                              + ", error=" + ex.Message);
            if (!AuraToolsConfigService.SkillCg.DisableAfterFailures || hookFailureCount < maxFailures)
            {
                return;
            }

            safeModeDisabled = true;
            SynchronizeRegisteredEffectiveState(AuraCgRegistryRuntime.GetSnapshot());
            SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "safe-mode-disabled");
            AuraToolsSkillCgProvider.ClearOwnerRoles();
            AuraToolsLog.Warn("[SkillCG] safe mode disabled hooks after repeated failures. source="
                              + source + ", failures=" + hookFailureCount + ".");
        }
    }

    internal static void LogDiagnostic(string key, string message)
    {
        if (DiagnosticKeys.Add(key))
        {
            AuraToolsLog.Debug(message);
        }
    }

    public static bool PreviewRegisteredCardUseCg(string ownerModId, string cgId)
    {
        var request = SkillCgArbiterRuntime.BuildCardUsePreviewRequest(
            AuraToolsIds.ModId,
            ownerModId,
            cgId);
        if (request == null) return false;
        ApplyCardUsePresentationOverride(request);
        SkillCgArbiterRuntime.RequestCg(AuraToolsIds.ModId, request);
        return true;
    }

    internal static void ApplyCardUsePresentationOverride(SkillCgRequest request)
    {
        var providerId = request.ProviderId ?? "";
        const string marker = ".SkillCG.";
        var markerIndex = providerId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return;
        var cgId = providerId.Substring(markerIndex + marker.Length).Trim();
        var key = (request.OwnerModId ?? "").Trim() + ":" + cgId;
        if (!AuraToolsConfigService.SkillCg.CardUseCg.PresentationOverrides.TryGetValue(key, out var settings)
            || settings == null)
        {
            return;
        }

        settings.Normalize();
        if (!string.IsNullOrWhiteSpace(settings.PresentationMode)) request.PresentationMode = settings.PresentationMode;
        if (!string.IsNullOrWhiteSpace(settings.FitMode)) request.FitMode = settings.FitMode;
        if (settings.FadeIn.HasValue) request.FadeIn = settings.FadeIn.Value;
        if (settings.Hold.HasValue) request.Hold = settings.Hold.Value;
        if (settings.FadeOut.HasValue) request.FadeOut = settings.FadeOut.Value;
        if (settings.FrameSeconds.HasValue) request.FrameSeconds = settings.FrameSeconds.Value;
        if (!string.IsNullOrWhiteSpace(settings.AlphaMode)) request.AlphaMode = settings.AlphaMode;
        if (settings.KeyThreshold.HasValue) request.KeyThreshold = settings.KeyThreshold.Value;
        if (settings.KeySoftness.HasValue) request.KeySoftness = settings.KeySoftness.Value;
        if (!string.IsNullOrWhiteSpace(settings.FlashMode)) request.FlashMode = settings.FlashMode;
        if (settings.FlashAtSeconds.HasValue) request.FlashAtSeconds = settings.FlashAtSeconds.Value;
        if (settings.FlashDuration.HasValue) request.FlashDuration = settings.FlashDuration.Value;
        if (settings.FlashStartFrame.HasValue) request.FlashStartFrame = settings.FlashStartFrame.Value;
        if (settings.FlashEndFrame.HasValue) request.FlashEndFrame = settings.FlashEndFrame.Value;
        if (settings.FlashPulseEveryFrames.HasValue) request.FlashPulseEveryFrames = settings.FlashPulseEveryFrames.Value;
        if (settings.FlashStrength.HasValue) request.FlashStrength = settings.FlashStrength.Value;
        request.Normalize();
    }

    internal static void ApplyRegisteredSkillPresentationOverride(SkillCgRequest request)
    {
        var providerId = request.ProviderId ?? "";
        const string marker = ".SkillCG.";
        var markerIndex = providerId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return;
        var cgId = providerId.Substring(markerIndex + marker.Length).Trim();
        var rule = AuraToolsConfigService.SkillCg.Roles.Values
            .Where(role => role != null && role.Enabled)
            .SelectMany(role => role.Rules ?? Enumerable.Empty<SkillCgRuleSettings>())
            .FirstOrDefault(candidate => candidate != null
                                         && candidate.Enabled
                                         && string.Equals(candidate.SourceOwnerModId, request.OwnerModId, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(candidate.SourceCgId, cgId, StringComparison.OrdinalIgnoreCase));
        if (rule == null) return;
        var presentation = rule.EffectivePresentation;
        request.FadeIn = presentation.FadeIn;
        request.Hold = presentation.Hold;
        request.FadeOut = presentation.FadeOut;
        request.PresentationMode = presentation.Mode;
        request.FitMode = presentation.Fit;
        request.FocusX = presentation.FocusX;
        request.FocusY = presentation.FocusY;
        request.SafeScale = presentation.SafeScale;
        request.Normalize();
    }
}

public sealed class AuraToolsSkillCgProvider
{
    private const float ImagePathCacheSeconds = 2f;
    private static readonly Dictionary<string, string> OwnerRoleIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CachedImagePath> ImagePathCache = new(StringComparer.OrdinalIgnoreCase);

    public string ProviderId => AuraToolsIds.ModId + ".SkillCG.Provider";

    public string OwnerModId => AuraToolsIds.ModId;

    public int Priority => 0;

    public static void RememberOwnerRole(string ownerInstanceId, string roleId)
    {
        var normalized = AuraSharedIdentity.SelectRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ownerInstanceId))
        {
            OwnerRoleIds[ownerInstanceId] = normalized;
        }
    }

    public static void ClearOwnerRoles()
    {
        OwnerRoleIds.Clear();
    }

    public static void ClearPathCache()
    {
        ImagePathCache.Clear();
    }

    public static IReadOnlyList<SkillCgRequest> BuildConfiguredPreloadRequests()
    {
        var requests = new List<SkillCgRequest>();
        foreach (var role in AuraToolsConfigService.SkillCg.Roles.Values)
        {
            if (role == null || !role.Enabled)
            {
                continue;
            }

            foreach (var rule in role.Rules)
            {
                if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Image))
                {
                    continue;
                }

                var imagePath = AuraToolsConfiguredResourceResolver.ResolveSkillCgPath(rule.Image);
                if (!File.Exists(imagePath))
                {
                    AuraToolsLog.Warn("[SkillCG] preload image missing: " + rule.Image);
                    continue;
                }

                var presentation = rule.EffectivePresentation;
                requests.Add(new SkillCgRequest
                {
                    ProviderId = string.IsNullOrWhiteSpace(rule.ProviderId)
                        ? AuraToolsIds.ModId + ".SkillCG." + role.RoleId + "." + (string.IsNullOrWhiteSpace(rule.CardId) ? "*" : rule.CardId)
                        : rule.ProviderId,
                    OwnerModId = AuraToolsIds.ModId,
                    TriggerKind = "skill",
                    CardId = string.IsNullOrWhiteSpace(rule.CardId) ? "*" : rule.CardId,
                    ImagePath = imagePath,
                    ImageResource = rule.Image,
                    Priority = rule.Priority,
                    FadeIn = presentation.FadeIn,
                    Hold = presentation.Hold,
                    FadeOut = presentation.FadeOut,
                    PresentationMode = presentation.Mode,
                    FitMode = presentation.Fit,
                    FocusX = presentation.FocusX,
                    FocusY = presentation.FocusY,
                    SafeScale = presentation.SafeScale,
                    CreatedAt = Time.unscaledTime,
                    DisableSync = true
                });
            }
        }

        return requests;
    }

    public IEnumerable<SkillCgRequest> BuildRequests(object context)
    {
        if (context is not SkillCgTriggerContext trigger)
        {
            yield break;
        }

        if (!AuraToolsConfigService.SkillCg.Enabled
            || !string.Equals(
                trigger.SignalId,
                AuraCgSignals.RoleSkillCommitted,
                StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var roleId = ResolveRoleId(trigger);
        var emitted = false;

        foreach (var role in MatchingRoles(roleId))
        {
            foreach (var rule in role.Rules)
            {
                if (!RuleMatches(rule, trigger))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.SourceOwnerModId)
                    && !string.IsNullOrWhiteSpace(rule.SourceCgId)
                    && !AuraCgActivationRuntime.IsLocallyEnabled(rule.SourceOwnerModId, rule.SourceCgId))
                {
                    AuraToolsSkillCgRuntime.LogDiagnostic(
                        "activation-skip:" + rule.SourceOwnerModId + ":" + rule.SourceCgId,
                        "[SkillCG] registered CG skipped by activation: source="
                        + rule.SourceOwnerModId + ":" + rule.SourceCgId
                        + ", consumer=" + AuraToolsIds.ModId
                        + ", role=" + roleId
                        + ", card=" + trigger.CardId);
                    continue;
                }

                // Registered entries are emitted from the live registry above;
                // imported v6 rules only carry the user's local activation and
                // presentation override during the bounded migration window.
                if (!string.IsNullOrWhiteSpace(rule.SourceOwnerModId)
                    && !string.IsNullOrWhiteSpace(rule.SourceCgId))
                {
                    continue;
                }

                var imagePath = AuraToolsConfiguredResourceResolver.ResolveSkillCgPath(rule.Image);
                if (!ImageExists(rule.Image, imagePath))
                {
                    AuraToolsLog.Warn("[SkillCG] image missing: " + rule.Image);
                    continue;
                }

                var requestCardId = trigger.CardId;
                var presentation = rule.EffectivePresentation;
                emitted = true;
                AuraToolsSkillCgRuntime.LogDiagnostic(
                    "match:" + role.RoleId + ":" + rule.ProviderId + ":" + requestCardId,
                    "[SkillCG] matched rule: provider="
                    + (string.IsNullOrWhiteSpace(rule.ProviderId) ? "<auto>" : rule.ProviderId)
                    + ", role=" + role.RoleId
                    + ", card=" + requestCardId
                    + ", image=" + rule.Image);
                yield return new SkillCgRequest
                {
                    ProviderId = string.IsNullOrWhiteSpace(rule.ProviderId)
                        ? AuraToolsIds.ModId + ".SkillCG." + role.RoleId + "." + requestCardId
                        : rule.ProviderId,
                    OwnerModId = AuraToolsIds.ModId,
                    SignalId = AuraCgSignals.RoleSkillCommitted,
                    SubjectType = AuraCgSubjectTypes.Role,
                    SubjectId = roleId,
                    TriggerKind = string.IsNullOrWhiteSpace(trigger.TriggerKind)
                        ? "skill"
                        : trigger.TriggerKind,
                    CardId = requestCardId,
                    OwnerInstanceId = trigger.OwnerInstanceId,
                    ImagePath = imagePath,
                    ImageResource = rule.Image,
                    Priority = rule.Priority,
                    FadeIn = presentation.FadeIn,
                    Hold = presentation.Hold,
                    FadeOut = presentation.FadeOut,
                    PresentationMode = presentation.Mode,
                    FitMode = presentation.Fit,
                    FocusX = presentation.FocusX,
                    FocusY = presentation.FocusY,
                    SafeScale = presentation.SafeScale,
                    CreatedAt = Time.unscaledTime,
                    ActionSequence = trigger.ActionSequence,
                    EventToken = trigger.EventToken,
                    DisableSync = true
                };
            }
        }

        if (!emitted)
        {
            AuraToolsSkillCgRuntime.LogDiagnostic(
                "no-match:" + roleId + ":" + trigger.CardId,
                "[SkillCG] no AuraTools rule emitted: role=" + roleId + ", card=" + trigger.CardId);
        }
    }

    private static IEnumerable<SkillCgRoleSettings> MatchingRoles(string roleId)
    {
        var normalizedRoleId = RoleCatalog.NormalizeRoleId(roleId);
        var configuredRoleIds = AuraToolsConfigService.SkillCg.Roles
            .Where(pair => !string.Equals(pair.Key, "*", StringComparison.Ordinal))
            .SelectMany(pair => new[] { pair.Key, pair.Value?.RoleId ?? "" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchingConfiguredRoleIds = configuredRoleIds
            .Where(configuredRoleId => AuraSharedContentId.Resolve(
                configuredRoleId,
                new[] { normalizedRoleId },
                knownPrefixes: new[] { AuraSharedIdentity.OfficialCareerPrefix }).Success)
            .ToList();
        var resolvedRoleId = matchingConfiguredRoleIds.Count == 1
            ? matchingConfiguredRoleIds[0]
            : normalizedRoleId;
        foreach (var pair in AuraToolsConfigService.SkillCg.Roles)
        {
            var role = pair.Value;
            if (role == null || !role.Enabled)
            {
                continue;
            }

            if (string.Equals(role.RoleId, "*", StringComparison.Ordinal)
                || string.Equals(pair.Key, "*", StringComparison.Ordinal)
                || string.Equals(RoleCatalog.NormalizeRoleId(role.RoleId), RoleCatalog.NormalizeRoleId(resolvedRoleId), StringComparison.OrdinalIgnoreCase)
                || string.Equals(RoleCatalog.NormalizeRoleId(pair.Key), RoleCatalog.NormalizeRoleId(resolvedRoleId), StringComparison.OrdinalIgnoreCase))
            {
                yield return role;
            }
        }
    }

    private static string ResolveRoleId(SkillCgTriggerContext trigger)
    {
        var triggerRole = AuraSharedIdentity.SelectRoleId(trigger.OwnerRoleId);
        if (!string.IsNullOrWhiteSpace(triggerRole))
        {
            return triggerRole;
        }

        if (!string.IsNullOrWhiteSpace(trigger.OwnerInstanceId)
            && OwnerRoleIds.TryGetValue(trigger.OwnerInstanceId, out var roleId)
            && !string.IsNullOrWhiteSpace(roleId))
        {
            return AuraSharedIdentity.SelectRoleId(roleId);
        }

        return AuraSharedIdentity.SelectRoleId(AuraToolsSkillCgRuntime.ReadCurrentCareerId());
    }

    private static bool RuleMatches(SkillCgRuleSettings rule, SkillCgTriggerContext trigger)
    {
        if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Image))
        {
            return false;
        }

        return AuraSharedContentId.Matches(rule.CardId, trigger.CardId, rule.SourceOwnerModId, "careercard_")
               && Matches(rule.Action, trigger.Action);
    }

    private static bool Matches(string pattern, string value)
    {
        var normalizedPattern = (pattern ?? "").Trim();
        var normalizedValue = (value ?? "").Trim();
        return string.Equals(normalizedPattern, "*", StringComparison.Ordinal)
               || string.Equals(normalizedPattern, normalizedValue, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedPattern.TrimStart('*'), normalizedValue.TrimStart('*'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ImageExists(string resource, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var key = (resource ?? "") + "|" + path;
        var now = Time.unscaledTime;
        if (ImagePathCache.TryGetValue(key, out var cached) && now <= cached.ExpiresAt)
        {
            return cached.Exists;
        }

        var exists = File.Exists(path);
        ImagePathCache[key] = new CachedImagePath(exists, now + ImagePathCacheSeconds);
        if (ImagePathCache.Count > 256)
        {
            foreach (var staleKey in ImagePathCache
                         .Where(pair => now > pair.Value.ExpiresAt)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                ImagePathCache.Remove(staleKey);
            }
        }

        return exists;
    }

    private readonly struct CachedImagePath
    {
        public CachedImagePath(bool exists, float expiresAt)
        {
            Exists = exists;
            ExpiresAt = expiresAt;
        }

        public bool Exists { get; }

        public float ExpiresAt { get; }
    }
}

public static class AuraToolsSkillCgEditor
{
    private const float SkillCgRuleBlockHeight = 240f;
    private static Transform? roleContent;
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.SkillCgEditor", parent, "角色 CG 配置", RefreshAndSave);
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 10f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;
        hintText = Settings.AuraToolsUi.AddText(
            toolbar.transform,
            RoleCgSummary(),
            14,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            34f,
            1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "扫描角色", () => RefreshRoles(true), 92f);
        Settings.AuraToolsUi.AddButton(
            toolbar.transform,
            "美餐资源",
            () => AuraToolsFeastRoleEditor.Show(window.transform),
            92f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "保存", RefreshAndSave, 78f);

        var behavior = Settings.AuraToolsUi.CreateLayout("Behavior", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(
            behavior,
            Settings.AuraToolsUi.InlineRowHeight);
        var behaviorLayout = behavior.AddComponent<HorizontalLayoutGroup>();
        behaviorLayout.spacing = 8f;
        behaviorLayout.childControlWidth = true;
        behaviorLayout.childControlHeight = true;
        behaviorLayout.childForceExpandWidth = false;
        behaviorLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddToggle(
            behavior.transform,
            AuraToolsConfigService.SkillCg.SyncRemote,
            value =>
            {
                AuraToolsConfigService.SkillCg.SyncRemote = value;
                AuraToolsConfigService.SaveSkillCg();
            });
        Settings.AuraToolsUi.AddText(
            behavior.transform,
            "联机同步",
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.Text,
            Settings.AuraToolsUi.TextMinHeight,
            0f,
            82f);
        Settings.AuraToolsUi.AddText(
            behavior.transform,
            "低生命阈值",
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.Text,
            Settings.AuraToolsUi.TextMinHeight,
            0f,
            92f);
        var thresholdInput = Settings.AuraToolsUi.AddInput(
            behavior.transform,
            Math.Round(AuraToolsConfigService.SkillCg.LowHealthThreshold * 100f).ToString(),
            value =>
            {
                if (float.TryParse(value, out var percentage))
                {
                    AuraToolsConfigService.SkillCg.LowHealthThreshold = Math.Max(5f, Math.Min(95f, percentage)) / 100f;
                    AuraToolsConfigService.SaveSkillCg();
                    if (hintText != null) hintText.text = RoleCgSummary();
                }
            },
            70f,
            Settings.AuraToolsUi.StandardButtonHeight);
        thresholdInput.contentType = InputField.ContentType.DecimalNumber;
        Settings.AuraToolsUi.AddText(
            behavior.transform,
            "%",
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            0f,
            22f);
        Settings.AuraToolsUi.AddToggle(
            behavior.transform,
            AuraToolsConfigService.MatchExperience.Feast.Cg.Enabled,
            value =>
            {
                AuraToolsConfigService.MatchExperience.Feast.Cg.Enabled = value;
                AuraToolsConfigService.SaveFeastCg();
                if (hintText != null) hintText.text = RoleCgSummary();
            });
        Settings.AuraToolsUi.AddText(
            behavior.transform,
            "美餐 CG",
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.Text,
            Settings.AuraToolsUi.TextMinHeight,
            0f,
            82f);

        roleContent = Settings.AuraToolsUi.CreateScroll(window.transform, "SkillCgRoles");
        RefreshRoles(false);
    }

    private static string RoleCgSummary()
    {
        var ruleCount = AuraToolsConfigService.SkillCg.Roles.Values.Sum(role => role.Rules.Count);
        return "技能规则 " + ruleCount
               + " · 低生命 " + Math.Round(AuraToolsConfigService.SkillCg.LowHealthThreshold * 100f) + "%"
               + " · 美餐 " + (AuraToolsConfigService.MatchExperience.Feast.Cg.Enabled ? "开启" : "关闭");
    }

    private static void RefreshRoles(bool forceScan)
    {
        EnsureRoleEntries(forceScan);
        RefreshRows();
    }

    private static void EnsureRoleEntries(bool forceScan)
    {
        foreach (var role in RoleCatalog.GetRoles(forceScan))
        {
            if (AuraToolsConfigService.SkillCg.Roles.ContainsKey(role.Id))
            {
                var existing = AuraToolsConfigService.SkillCg.Roles[role.Id];
                if (string.IsNullOrWhiteSpace(existing.DisplayName)
                    || IsRuleDisplayName(existing.DisplayName, existing.Rules))
                {
                    existing.DisplayName = role.DisplayName;
                }

                continue;
            }

            AuraToolsConfigService.SkillCg.Roles[role.Id] = new SkillCgRoleSettings
            {
                Enabled = false,
                RoleId = role.Id,
                DisplayName = role.DisplayName
            };
        }
    }

    private static void RefreshRows()
    {
        if (roleContent == null)
        {
            return;
        }

        var viewState = AuraUiViewState.CaptureForContent(roleContent);
        Settings.AuraToolsUi.ClearChildren(roleContent);
        foreach (var pair in AuraToolsConfigService.SkillCg.Roles.OrderBy(pair => RoleDisplayName(pair.Value)).ThenBy(pair => pair.Key))
        {
            CreateRoleRow(pair.Key, pair.Value);
        }
        AuraUiViewState.RestoreAfterLayout(
            roleContent,
            viewState,
            "AuraTools.SkillCg.Roles");
    }

    private static void CreateRoleRow(string key, SkillCgRoleSettings role)
    {
        var box = Settings.AuraToolsUi.CreateLayout("Role-" + key, roleContent!);
        Settings.AuraToolsUi.AddSectionImage(box);
        var boxElement = Settings.AuraToolsUi.EnsureLayoutElement(box);
        boxElement.minHeight = 112f;
        boxElement.flexibleHeight = 0f;
        var layout = box.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = Settings.AuraToolsUi.CreateLayout("Header", box.transform);
        Settings.AuraToolsUi.SetFixedHeight(header, Settings.AuraToolsUi.ModuleHeaderHeight);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddToggle(header.transform, role.Enabled, value => role.Enabled = value);
        Settings.AuraToolsUi.AddText(header.transform, RoleDisplayName(role), Settings.AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(header.transform, "本地目录", () => FileResourceUtil.OpenDirectory(FileResourceUtil.RoleSkillCgDirectory(role.RoleId)), 92f, 30f);
        Settings.AuraToolsUi.AddButton(header.transform, "添加规则", () =>
        {
            var dir = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
            var relative = AuraToolsConfigService.ToDataRelativePath(Path.Combine(dir, "skill_cg_" + (role.Rules.Count + 1) + ".png"));
            var defaultSkill = DefaultActiveSkillForNewRule(role);
            role.Rules.Add(new SkillCgRuleSettings
            {
                Enabled = true,
                CardId = defaultSkill,
                Action = "*",
                Image = relative,
                ProviderId = AuraToolsIds.ModId + ".SkillCG." + FileResourceUtil.SafeFolderName(role.RoleId) + "." + (role.Rules.Count + 1)
            });
            RefreshRows();
        }, 92f, 30f);

        foreach (var rule in role.Rules)
        {
            CreateRuleBlock(box.transform, role, rule);
        }
    }

    private static string RoleDisplayName(SkillCgRoleSettings role)
    {
        var catalogName = Settings.AuraToolsPlayerDisplay.RoleName(role.RoleId);
        if (!string.Equals(catalogName, "未知角色", StringComparison.Ordinal))
        {
            return catalogName;
        }
        var displayName = (role.DisplayName ?? "").Trim();
        return string.IsNullOrWhiteSpace(displayName)
               || string.Equals(displayName, role.RoleId, StringComparison.OrdinalIgnoreCase)
            ? "未知角色"
            : displayName;
    }

    private static bool IsRuleDisplayName(string displayName, IEnumerable<SkillCgRuleSettings>? rules)
    {
        var value = displayName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return (rules ?? Enumerable.Empty<SkillCgRuleSettings>())
            .Any(rule => string.Equals(rule.DisplayName, value, StringComparison.OrdinalIgnoreCase));
    }

    private static void CreateRuleBlock(Transform parent, SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        rule.Action = "*";
        rule.Presentation ??= SkillCgPresentationSettings.CreateInherited();
        var block = Settings.AuraToolsUi.CreateLayout("RuleBlock-" + rule.ProviderId, parent);
        Settings.AuraToolsUi.SetFixedHeight(block, SkillCgRuleBlockHeight);
        Settings.AuraToolsUi.AddImage(block, Settings.AuraToolsUi.Row);
        var blockLayout = block.AddComponent<VerticalLayoutGroup>();
        blockLayout.padding = new RectOffset(8, 8, 5, 5);
        blockLayout.spacing = 6f;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;
        blockLayout.childForceExpandWidth = true;
        blockLayout.childForceExpandHeight = false;

        var top = Settings.AuraToolsUi.CreateLayout("RuleTop", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(top, Settings.AuraToolsUi.StandardButtonHeight);
        var topLayout = top.AddComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 8f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandWidth = false;
        topLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddToggle(top.transform, rule.Enabled, value => rule.Enabled = value);
        Settings.AuraToolsUi.AddText(top.transform, RuleDisplayName(rule), Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(top.transform, "\u5220\u9664", () =>
        {
            role.Rules.Remove(rule);
            RefreshRows();
        }, 72f, Settings.AuraToolsUi.CompactButtonHeight);

        var skillRow = Settings.AuraToolsUi.CreateLayout("RuleSkill", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(skillRow, Settings.AuraToolsUi.StandardButtonHeight);
        var skillLayout = skillRow.AddComponent<HorizontalLayoutGroup>();
        skillLayout.spacing = 8f;
        skillLayout.childControlWidth = true;
        skillLayout.childControlHeight = true;
        skillLayout.childForceExpandWidth = false;
        skillLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddText(skillRow.transform, "触发技能", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 80f);
        var skillOptions = BuildSkillOptions(role, rule);
        Settings.AuraToolsUi.AddSelectButton(skillRow.transform, skillOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(skillOptions, rule.CardId), index =>
        {
            if (index >= 0 && index < skillOptions.Count)
            {
                rule.CardId = skillOptions[index].Id;
                rule.Action = "*";
            }
        }, 300f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(skillRow.transform, "优先级", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 70f);
        Settings.AuraToolsUi.AddInput(skillRow.transform, rule.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                rule.Priority = priority;
            }
        }, 80f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(skillRow.transform, "", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);

        var presentationRow = Settings.AuraToolsUi.CreateLayout("RulePresentation", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(presentationRow, Settings.AuraToolsUi.StandardButtonHeight);
        var presentationLayout = presentationRow.AddComponent<HorizontalLayoutGroup>();
        presentationLayout.spacing = 8f;
        presentationLayout.childControlWidth = true;
        presentationLayout.childControlHeight = true;
        presentationLayout.childForceExpandWidth = false;
        presentationLayout.childForceExpandHeight = false;

        var effective = rule.EffectivePresentation;
        Settings.AuraToolsUi.AddText(presentationRow.transform, "演出方式", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 80f);
        var modeOptions = BuildPresentationModeOptions();
        Settings.AuraToolsUi.AddSelectButton(presentationRow.transform, modeOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(modeOptions, effective.Mode), index =>
        {
            if (index >= 0 && index < modeOptions.Count)
            {
                rule.Presentation.Mode = modeOptions[index].Id;
            }
        }, 180f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(presentationRow.transform, "画面适配", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 90f);
        var fitOptions = BuildFitOptions();
        Settings.AuraToolsUi.AddSelectButton(presentationRow.transform, fitOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(fitOptions, effective.Fit), index =>
        {
            if (index >= 0 && index < fitOptions.Count)
            {
                rule.Presentation.Fit = fitOptions[index].Id;
            }
        }, 160f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(presentationRow.transform, "", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);

        var transformRow = Settings.AuraToolsUi.CreateLayout("RuleTransform", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(transformRow, Settings.AuraToolsUi.StandardButtonHeight);
        var transformLayout = transformRow.AddComponent<HorizontalLayoutGroup>();
        transformLayout.spacing = 8f;
        transformLayout.childControlWidth = true;
        transformLayout.childControlHeight = true;
        transformLayout.childForceExpandWidth = false;
        transformLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddText(transformRow.transform, "横向焦点", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 72f);
        Settings.AuraToolsUi.AddInput(transformRow.transform, FormatFloat(effective.FocusX), value => rule.Presentation.FocusX = ParseClampedFloat(value, rule.Presentation.FocusX, 0f, 1f), 70f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(transformRow.transform, "纵向焦点", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 72f);
        Settings.AuraToolsUi.AddInput(transformRow.transform, FormatFloat(effective.FocusY), value => rule.Presentation.FocusY = ParseClampedFloat(value, rule.Presentation.FocusY, 0f, 1f), 70f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(transformRow.transform, "画面缩放", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 72f);
        Settings.AuraToolsUi.AddInput(transformRow.transform, FormatFloat(effective.SafeScale), value => rule.Presentation.SafeScale = ParseClampedFloat(value, rule.Presentation.SafeScale, 1f, 3f), 70f, Settings.AuraToolsUi.StandardButtonHeight);
        Settings.AuraToolsUi.AddText(transformRow.transform, "", Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);

        var bottom = Settings.AuraToolsUi.CreateLayout("RuleBottom", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(bottom, Settings.AuraToolsUi.StandardButtonHeight);
        var bottomLayout = bottom.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 8f;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = false;
        bottomLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddText(bottom.transform, "\u56fe\u7247", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        Settings.AuraToolsUi.AddInput(bottom.transform, rule.Image,
            value => ApplyRuleImagePath(role, rule, value, false, false), 220f,
            Settings.AuraToolsUi.StandardButtonHeight, flexibleWidth: true);
        Settings.AuraToolsUi.AddButton(bottom.transform, "选择图片", () => PickRuleImage(role, rule), 92f,
            Settings.AuraToolsUi.CompactButtonHeight);
        Settings.AuraToolsUi.AddButton(bottom.transform, "图片目录", () => OpenRuleImageDirectory(role, rule), 92f,
            Settings.AuraToolsUi.CompactButtonHeight);
    }

    private sealed class SkillDropdownOption
    {
        public string Id { get; set; } = "";

        public string Label { get; set; } = "";
    }

    private static List<SkillDropdownOption> BuildPresentationModeOptions()
    {
        return new List<SkillDropdownOption>
        {
            new() { Id = "slide", Label = "从右到左" },
            new() { Id = "fullscreenFade", Label = "全屏淡入淡出" },
            new() { Id = "centerFade", Label = "居中淡入淡出" }
        };
    }

    private static List<SkillDropdownOption> BuildFitOptions()
    {
        return new List<SkillDropdownOption>
        {
            new() { Id = "contain", Label = "完整显示" },
            new() { Id = "cover", Label = "全屏裁切" },
            new() { Id = "stretch", Label = "拉伸填充" }
        };
    }

    private static string DefaultActiveSkillForNewRule(SkillCgRoleSettings role)
    {
        var used = new HashSet<string>(
            role.Rules
                .Select(rule => rule.CardId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var skill in RoleCatalog.GetRoleSkills(role.RoleId))
        {
            if (!used.Contains(skill.Id))
            {
                return skill.Id;
            }
        }

        return RoleCatalog.GetRoleSkills(role.RoleId).FirstOrDefault()?.Id ?? "*";
    }

    private static List<SkillDropdownOption> BuildSkillOptions(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var options = new List<SkillDropdownOption>
        {
            new()
            {
                Id = "*",
                Label = "任意技能"
            }
        };

        foreach (var skill in RoleCatalog.GetRoleSkills(role.RoleId))
        {
            if (options.Any(option => string.Equals(option.Id, skill.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new SkillDropdownOption
            {
                Id = skill.Id,
                Label = SkillLabel(skill.Id, skill.DisplayName)
            });
        }

        var current = rule.CardId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Any(option => string.Equals(option.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new SkillDropdownOption
            {
                Id = current,
                Label = "自定义技能"
            });
        }

        return options;
    }

    private static int SelectedOptionIndex(IReadOnlyList<SkillDropdownOption> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Id, value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string SkillLabel(string id, string displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        return name;
    }

    private static string RuleDisplayName(SkillCgRuleSettings rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.DisplayName))
        {
            return rule.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(rule.CardId) || string.Equals(rule.CardId, "*", StringComparison.Ordinal)
            ? "自定义CG"
            : Settings.AuraToolsPlayerDisplay.CardName(rule.CardId);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static float ParseClampedFloat(string value, float fallback, float min, float max)
    {
        var text = (value ?? "").Trim().Replace(',', '.');
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return Mathf.Clamp(parsed, min, max);
    }

    private static void OpenRuleImageDirectory(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var directory = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
        try
        {
            if (!string.IsNullOrWhiteSpace(rule.Image))
            {
                var path = AuraToolsConfiguredResourceResolver.ResolveSkillCgPath(rule.Image);
                if (File.Exists(path))
                {
                    directory = Path.GetDirectoryName(path) ?? directory;
                }
                else if (Directory.Exists(path))
                {
                    directory = path;
                }
                else
                {
                    var parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        directory = parent;
                    }
                }
            }
        }
        catch
        {
        }

        FileResourceUtil.OpenDirectory(directory);
    }

    private static void PickRuleImage(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var directory = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
        SetHint("正在打开图片选择器...");
        OptionalFileDialog.PickImageFileAsync(directory, result =>
        {
            if (result.Selected)
            {
                ApplyRuleImagePath(role, rule, result.Path, true, true);
                return;
            }

            if (result.Status == OptionalFileDialogStatus.Cancelled)
            {
                SetHint("已取消选择图片。");
                return;
            }

            AuraToolsLog.Warn("[SkillCG] image picker unavailable: " + result.Message);
            SetHint("无法打开系统文件选择器；请使用路径输入框修改，或先把图片放进角色目录。");
        });
    }

    private static void ApplyRuleImagePath(SkillCgRoleSettings role, SkillCgRuleSettings rule, string path, bool refresh, bool save)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            rule.Image = "";
            SetHint("已清空图片路径。");
        }
        else
        {
            var imported = FileResourceUtil.ImportImagePath(
                trimmed,
                FileResourceUtil.RoleSkillCgDirectory(role.RoleId),
                RuleImageBaseName(role, rule),
                out var message);
            rule.Image = string.IsNullOrWhiteSpace(imported) ? trimmed : imported;
            if (string.IsNullOrWhiteSpace(imported))
            {
                AuraToolsLog.Warn("[SkillCG] image path kept as typed: " + message);
                SetHint(message + " 已保留输入路径。");
            }
            else
            {
                var activeRole = RoleCatalog.GetRoles().FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, RoleCatalog.NormalizeRoleId(role.RoleId), StringComparison.OrdinalIgnoreCase));
                FileResourceUtil.RegisterManualDirectory(
                    AuraSharedSystems.Cg,
                    "SkillCg",
                    "Role",
                    role.RoleId,
                    activeRole?.OwnerModId ?? AuraToolsIds.ModId,
                    "user-imports",
                    FileResourceUtil.RoleSkillCgDirectory(role.RoleId),
                    out _);
                role.Enabled = true;
                rule.Enabled = true;
                SetHint(message + " " + rule.Image);
            }
        }

        if (save)
        {
            AuraToolsConfigService.SkillCg.Normalize();
            AuraToolsConfigService.SaveSkillCg();
        }

        if (refresh)
        {
            RefreshRows();
        }
    }

    private static string RuleImageBaseName(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var index = role.Rules.IndexOf(rule);
        return index >= 0 ? "skill_cg_" + (index + 1) : "skill_cg";
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void RefreshAndSave()
    {
        foreach (var role in AuraToolsConfigService.SkillCg.Roles.Values)
        {
            foreach (var rule in role.Rules)
            {
                rule.Action = "*";
            }
        }

        AuraToolsConfigService.SkillCg.Normalize();
        AuraToolsConfigService.SaveSkillCg();
        SetHint("已保存技能CG配置。");
    }
}
