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
        AuraToolsCgSceneAssetResolver.ReloadCatalog();
        SkillCgArbiterRuntime.RegisterSceneAssetResolver(
            modConfig,
            AuraToolsIds.ModId,
            new AuraToolsCgSceneAssetResolver());
        AuraToolsRoleCgCatalog.SynchronizeManualContribution();

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
        AuraToolsRoleCgCatalog.SynchronizeManualContribution();
        SkillCgArbiterRuntime.Initialize(null, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
        EnsureRegistryStateCurrent(force: true);
        EnsureHooksMatchConfig();
    }

    public static void ApplyRoleCgConfiguration()
    {
        if (!initialized)
        {
            return;
        }

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
        signalHooks.AfterRouted(
            "StatusManager.PlayVocal",
            AuraToolsCgLowHealthSignalService.AfterVocalState,
            "RoleLowHealth.NativeDying");
        signalHooks.BeforeRouted(
            "PlayerInfo.GiveWin",
            AuraToolsCgOutcomeReasonService.ObserveGiveWin,
            "EventCg.OutcomeReason.GiveWin");
        signalHooks.BeforeRouted(
            "PlayerInfo.WinTheFight",
            AuraToolsCgOutcomeReasonService.ObserveGiveWin,
            "EventCg.OutcomeReason.WinTheFight");
        signalHooks.BeforeRouted(
            "ScriptExecutor.EscapeFight",
            AuraToolsCgOutcomeReasonService.ObserveEscape,
            "EventCg.OutcomeReason.EscapeFight");
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

            var trigger = new SkillCgTriggerContext
            {
                TriggerKind = "skill",
                ActionSequence = context.Sequence,
                EventToken = context.TransactionId,
                Action = "Skill",
                SkillId = context.SkillDataId,
                CardId = context.SkillDataId,
                OwnerInstanceId = context.OwnerStatusId,
                OwnerRoleId = string.IsNullOrWhiteSpace(context.OwnerRoleId)
                    ? ReadCurrentCareerId()
                    : context.OwnerRoleId,
                CreatedAt = Time.unscaledTime
            };
            if (ShouldEmitLocalRequest(trigger))
            {
                var signal = AuraCgSignalContext.FromLegacy(trigger);
                signal.Facts["resolvedCgId"] = AuraToolsRoleCgCatalog.ResolveSelectedCgId(
                    trigger.OwnerRoleId,
                    AuraToolsRoleCgChannels.Skill,
                    context.SkillDataId);
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
                var builtInScene = string.Equals(entry.OwnerModId, AuraToolsIds.ModId, StringComparison.OrdinalIgnoreCase)
                                   && entry.CgId.StartsWith("event.", StringComparison.OrdinalIgnoreCase);
                var enabled = builtInScene
                    ? AuraToolsConfigService.SkillCg.EventCg
                        .GetScene(AuraToolsEventCgSceneIds.FromCgId(entry.CgId)).Enabled
                    : entry.DefaultActivation.Enabled;
                overrides.Add(new AuraCgLocalActivationOverride
                {
                    OwnerModId = entry.OwnerModId,
                    CgId = entry.CgId,
                    Enabled = eventEnabled && enabled
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
            var selectedByPlayer = AuraToolsConfigService.SkillCg.RoleSelections.Values.Any(value =>
                string.Equals(value, entry.QualifiedCgId, StringComparison.OrdinalIgnoreCase));
            overrides.Add(new AuraCgLocalActivationOverride
            {
                OwnerModId = entry.OwnerModId,
                CgId = entry.CgId,
                Enabled = skillEnabled && (entry.DefaultActivation.Enabled || selectedByPlayer)
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
            AuraToolsCgLowHealthSignalService.Reset();
        });
    }

    private static void OnFightRestarting(ModHookContext context)
    {
        RunHook("fight restarting", () =>
        {
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
        if (settings.FocusX.HasValue) request.FocusX = settings.FocusX.Value;
        if (settings.FocusY.HasValue) request.FocusY = settings.FocusY.Value;
        if (settings.SafeScale.HasValue) request.SafeScale = settings.SafeScale.Value;
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
        var key = (request.OwnerModId ?? "").Trim() + ":" + cgId;
        if (!AuraToolsConfigService.SkillCg.RoleEntries.TryGetValue(key, out var local)
            || local?.Presentation == null)
        {
            return;
        }

        local.Normalize();
        var presentation = local.Presentation;
        if (!string.IsNullOrWhiteSpace(presentation.PresentationMode)) request.PresentationMode = presentation.PresentationMode;
        if (!string.IsNullOrWhiteSpace(presentation.FitMode)) request.FitMode = presentation.FitMode;
        if (presentation.FadeIn.HasValue) request.FadeIn = presentation.FadeIn.Value;
        if (presentation.Hold.HasValue) request.Hold = presentation.Hold.Value;
        if (presentation.FadeOut.HasValue) request.FadeOut = presentation.FadeOut.Value;
        if (presentation.FocusX.HasValue) request.FocusX = presentation.FocusX.Value;
        if (presentation.FocusY.HasValue) request.FocusY = presentation.FocusY.Value;
        if (presentation.SafeScale.HasValue) request.SafeScale = presentation.SafeScale.Value;
        request.Normalize();
    }
}
