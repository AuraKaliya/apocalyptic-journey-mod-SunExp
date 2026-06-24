using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.DamageMeter;

public static class AuraToolsDamageMeterRuntime
{
    private static readonly List<HitFrame> HitFrames = new();
    private static readonly List<PureHpFrame> PureHpFrames = new();
    private static readonly List<HpSetterFrame> HpSetterFrames = new();
    private static readonly List<BuffApplicationFrame> BuffFrames = new();
    private static readonly BuffDamageAttributionTracker BuffAttribution = new();
    private static bool initialized;
    private static bool endingSent;
    private static float nextRefreshAt;
    private static float uiRetryBlockedUntil;
    private static float nextUiFailureLogAt;
    private static bool disabledUiHidden;
    private static bool preparationUiActive;
    private static int lastRoundStartFrame = -10000;
    private static object? lastRoundUnit;
    private static long nextCallId;
    private static bool uiDirty = true;

    public static bool Visible { get; private set; }

    public static bool Available { get; private set; }

    public static bool Enabled => AuraToolsConfigService.Root.MatchExperience.Enabled
                                  && AuraToolsConfigService.MatchExperience.DamageMeter.Enabled;

    internal static DamageLedger Ledger => DamageMeterNetworkRuntime.Ledger;

    internal static DamageHistoryStore History => DamageMeterNetworkRuntime.History;

    internal static string NetworkStatus
    {
        get
        {
            if (!DamageMeterNetworkRuntime.IsMultiplayer)
            {
                return "单机统计";
            }

            if (!Ledger.SharedEnabled)
            {
                return "联机统计未启用";
            }

            return "主机同步 #" + Ledger.ServerSequence;
        }
    }

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraToolsDamageMeterUi.EnsureDriver();

        RegisterAfter(modConfig, "GameEntryUI.Init", HideForEntryUi);
        RegisterAfter(modConfig, "GameEntryUI.Outlobby", HideForEntryUi);
        RegisterAfter(modConfig, "GameEntryUI.ReturnHouse", HideForEntryUi);
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", ShowForPreparationUi);
        RegisterAfter(modConfig, "GameEntryUI.ShowDetail", ShowForPreparationUi);
        RegisterAfter(modConfig, "GameEntryUI.ChangeRole", ShowForPreparationUi);
        RegisterBefore(modConfig, "GameEntryUI.StartGame", ShowForStartGame);
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", ShowForAdventureUi);
        RegisterAfter(modConfig, "TopBarUI.Awake", ShowForAdventureUi);
        RegisterAfter(modConfig, "TopBarUI.Start", ShowForAdventureUi);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", ShowForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.Start", ShowForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ReadyToSelect", ShowForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.ShowMap", ShowForAdventureUi);
        RegisterAfter(modConfig, "MapSelectUI.MapAnimation", ShowForAdventureUi);

        RegisterBefore(modConfig, "StatusManager.Hit", BeforeHit);
        RegisterAfter(modConfig, "StatusManager.Hit", AfterHit);
        RegisterBefore(modConfig, "DamageText.Create", BeforeDamageTextCreate);
        RegisterBefore(modConfig, "ScriptExecutor.PureChangeHp", BeforePureChangeHp);
        RegisterAfter(modConfig, "ScriptExecutor.PureChangeHp", AfterPureChangeHp);
        RegisterBefore(modConfig, "StatusManager.set_CurHp", BeforeSetCurHp);
        RegisterAfter(modConfig, "StatusManager.set_CurHp", AfterSetCurHp);
        RegisterBefore(modConfig, "ScriptExecutor.AddBuff", BeforeScriptAddBuff);
        RegisterAfter(modConfig, "ScriptExecutor.AddBuff", AfterScriptAddBuff);
        RegisterAfter(modConfig, "BuffItemConfig.set_Level", AfterBuffLevelChanged);
        RegisterAfter(modConfig, "StatusManager.RemoveBuff", AfterRemoveBuff);

        RegisterBefore(modConfig, "FightInit.Init", OnFightInitStarting);
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStartFallback);
        RegisterAfter(modConfig, "Fight_PlayerTurn.Init", OnPlayerRoundStart);
        RegisterBefore(modConfig, "Fight_Win.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Loss.Init", OnFightEnding);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);

        AuraToolsConfigService.Changed += OnConfigChanged;
        AuraToolsLog.Info("[DamageMeter] DPT hooks and network protocol v"
                          + DamageMeterProtocol.Version + " registered.");
    }

    public static void Tick()
    {
        if (!Enabled)
        {
            HideDisabledUiSafe();
            return;
        }

        disabledUiHidden = false;
        ReconcileAvailabilitySafe();
        RefreshUiSafe();
    }

    private static void HideDisabledUiSafe()
    {
        var now = Time.unscaledTime;
        if (disabledUiHidden || now < uiRetryBlockedUntil)
        {
            return;
        }

        try
        {
            SetVisible(false);
            disabledUiHidden = true;
        }
        catch (Exception ex)
        {
            uiRetryBlockedUntil = now + 1f;
            LogUiFailure("disabled UI cleanup", ex);
        }
    }

    private static void RefreshUiSafe()
    {
        var now = Time.unscaledTime;
        if (now < uiRetryBlockedUntil || !uiDirty && now < nextRefreshAt)
        {
            return;
        }

        try
        {
            nextRefreshAt = now + 0.2f;
            uiDirty = false;
            AuraToolsDamageMeterUi.Refresh(
                Ledger,
                History,
                AuraToolsConfigService.MatchExperience.DamageMeter,
                NetworkStatus);
        }
        catch (Exception ex)
        {
            uiDirty = true;
            uiRetryBlockedUntil = now + 1f;
            LogUiFailure("UI refresh", ex);
        }
    }

    private static void LogUiFailure(string operation, Exception ex)
    {
        var now = Time.unscaledTime;
        if (now >= nextUiFailureLogAt)
        {
            nextUiFailureLogAt = now + 10f;
            AuraToolsLog.Warn("[DamageMeter] " + operation + " failed: " + ex.Message);
        }
    }

    public static void SetVisible(bool visible)
    {
        Visible = visible;
        uiDirty = true;
        AuraToolsDamageMeterUi.SetVisible(visible && Enabled && Available);
        if (!visible)
        {
            AuraToolsDamageMeterUi.CloseDetails();
        }
    }

    private static void SetAvailable(bool available, string reason)
    {
        if (Available == available)
        {
            AuraToolsDamageMeterUi.SetAvailable(available && Enabled);
            uiDirty = true;
            return;
        }

        Available = available;
        if (available)
        {
            Visible = AuraToolsConfigService.MatchExperience.DamageMeter.ShowPanelByDefault;
        }
        else
        {
            Visible = false;
            preparationUiActive = false;
            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterUi.CloseHistory();
        }

        AuraToolsDamageMeterUi.SetAvailable(available && Enabled);
        AuraToolsDamageMeterUi.SetVisible(available && Enabled && Visible);
        uiDirty = true;
        AuraToolsLog.Info("[DamageMeter] floating UI availability=" + available + "; reason=" + reason + ".");
    }

    public static bool ReportDamage(DamageEvent damage)
    {
        if (damage == null || !Ledger.InFight || !Ledger.SharedEnabled)
        {
            return false;
        }

        NormalizeEvent(damage);
        DamageMeterNetworkRuntime.Submit(damage);
        return true;
    }

    internal static void NotifyLedgerChanged()
    {
        uiDirty = true;
    }

    private static bool CaptureEnabled => Ledger.InFight && Ledger.SharedEnabled;

    private static void OnConfigChanged()
    {
        uiDirty = true;
        if (!Enabled)
        {
            try
            {
                SetVisible(false);
                disabledUiHidden = true;
            }
            catch (Exception ex)
            {
                disabledUiHidden = false;
                uiRetryBlockedUntil = Time.unscaledTime + 1f;
                LogUiFailure("configuration UI cleanup", ex);
            }
        }
        else
        {
            AuraToolsDamageMeterUi.SetAvailable(Available);
            AuraToolsDamageMeterUi.SetVisible(Visible && Available);
        }
    }

    private static void OnFightInitStarting(ModHookContext context)
    {
        RunHook("fight init", () =>
        {
            if (IsWorldSimulationContext(context, allowNormalMapHookFallback: true))
            {
                preparationUiActive = false;
                SetAvailable(true, "FightInit.Init");
            }

            ResetCaptureState();
            endingSent = false;
            DamageMeterNetworkRuntime.StartFight(Enabled);
            AuraToolsDamageMeterUi.CloseDetails();
            uiDirty = true;
        });
    }

    private static void OnFightStartFallback(ModHookContext context)
    {
        RunHook("fight start fallback", () =>
        {
            if (!Ledger.InFight)
            {
                if (IsActiveWorldSimulationContext())
                {
                    SetAvailable(true, "Fight_Start.Init");
                }

                ResetCaptureState();
                endingSent = false;
                DamageMeterNetworkRuntime.StartFight(Enabled);
                uiDirty = true;
            }
        });
    }

    private static void OnPlayerRoundStart(ModHookContext context)
    {
        RunHook("round start", () =>
        {
            if (context.Target != null && ReferenceEquals(lastRoundUnit, context.Target))
            {
                return;
            }

            if (Time.frameCount - lastRoundStartFrame <= 5)
            {
                return;
            }

            lastRoundUnit = context.Target;
            lastRoundStartFrame = Time.frameCount;
            DamageMeterNetworkRuntime.StartRound();
            uiDirty = true;
        });
    }

    private static void OnFightEnding(ModHookContext context)
    {
        RunHook("fight ending", () =>
        {
            if (!endingSent)
            {
                endingSent = true;
                DamageMeterNetworkRuntime.EndFight(FightResult(context));
            }

            AuraToolsDamageMeterUi.CloseDetails();
            uiDirty = true;
        });
    }

    private static void OnFightEnded(ModHookContext context)
    {
        RunHook("fight ended", () =>
        {
            ResetCaptureState();
            uiDirty = true;
        });
    }

    private static void HideForEntryUi(ModHookContext context)
    {
        RunHook("entry UI hidden scope", () => SetAvailable(false, GetHookName(context)));
    }

    private static void ShowForPreparationUi(ModHookContext context)
    {
        RunHook("preparation UI scope", () =>
        {
            if (!IsWorldSimulationLobby())
            {
                SetAvailable(false, GetHookName(context) + ":not-world-simulation");
                return;
            }

            preparationUiActive = true;
            SetAvailable(true, GetHookName(context));
        });
    }

    private static void ShowForStartGame(ModHookContext context)
    {
        RunHook("start game UI scope", () =>
        {
            if (IsWorldSimulationContext(context, allowNormalMapHookFallback: false))
            {
                DamageMeterNetworkRuntime.BeginAdventure();
                AuraToolsDamageMeterUi.CloseHistory();
                preparationUiActive = true;
                SetAvailable(true, "GameEntryUI.StartGame");
            }
        });
    }

    private static void ShowForAdventureUi(ModHookContext context)
    {
        RunHook("adventure UI scope", () =>
        {
            if (!IsWorldSimulationContext(context, allowNormalMapHookFallback: true))
            {
                return;
            }

            preparationUiActive = false;
            DamageMeterNetworkRuntime.RestoreAdventureHistory();
            SetAvailable(true, GetHookName(context));
        });
    }

    private static string FightResult(ModHookContext context)
    {
        var name = GetHookName(context);
        if (name.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Win";
        }

        if (name.IndexOf("Escape", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Escape";
        }

        if (name.IndexOf("Loss", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Loss";
        }

        return "Unknown";
    }

    private static void ReconcileAvailabilitySafe()
    {
        try
        {
            if (Available && !IsActiveWorldSimulationContext())
            {
                SetAvailable(false, "context-lost");
            }
        }
        catch (Exception ex)
        {
            LogUiFailure("availability reconcile", ex);
        }
    }

    private static bool IsActiveWorldSimulationContext()
    {
        return preparationUiActive && IsWorldSimulationLobby()
               || IsWorldSimulationAdventureContext();
    }

    private static bool IsWorldSimulationContext(ModHookContext context, bool allowNormalMapHookFallback)
    {
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase);
        }

        if (IsNormalMapManager(context.Target))
        {
            return true;
        }

        if (IsWorldSimulationAdventureContext())
        {
            return true;
        }

        return allowNormalMapHookFallback && IsNormalMapManager(MapManager.Instance?.ModeMapManager);
    }

    private static bool IsWorldSimulationLobby()
    {
        return string.Equals(ReadLobbyModeType(), "Normal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorldSimulationAdventureContext()
    {
        try
        {
            return IsNormalMapManager(MapManager.Instance?.ModeMapManager);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNormalMapManager(object? value)
    {
        return string.Equals(value?.GetType().Name, "NormalMapManager", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadLobbyModeType()
    {
        try
        {
            return LobbyManager.Instance?.CurrentLobbyModeType ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string GetHookName(ModHookContext context)
    {
        try
        {
            return context.Target?.GetType().Name ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void BeforeHit(ModHookContext context)
    {
        RunHook("before hit", () =>
        {
            if (!CaptureEnabled || context.Target is not IStatusManager target)
            {
                return;
            }

            PruneFrames();
            var arguments = context.Arguments ?? Array.Empty<object>();
            HitFrames.Add(new HitFrame
            {
                CallId = ++nextCallId,
                Frame = Time.frameCount,
                Target = target,
                TargetId = SafeStatusId(target),
                BeforeHp = SafeHp(target),
                BeforeShield = SafeDefend(target),
                DamageType = ArgumentString(arguments, 1),
                SourceDataId = ArgumentString(arguments, 2),
                SourceInstanceId = ArgumentString(arguments, 3)
            });
            Limit(HitFrames, 256);
        });
    }

    private static void BeforeDamageTextCreate(ModHookContext context)
    {
        RunHook("damage text", () =>
        {
            if (!CaptureEnabled
                || context.Arguments == null
                || context.Arguments.Length == 0
                || !TryReadDamageText(context.Arguments[0], out var data))
            {
                return;
            }

            var frameIndex = FindHitFrame(data);
            if (frameIndex < 0)
            {
                return;
            }

            var frame = HitFrames[frameIndex];
            HitFrames.RemoveAt(frameIndex);
            var hpDamage = Math.Max(0, frame.BeforeHp - SafeHp(frame.Target));
            var shieldDamage = Math.Max(0, frame.BeforeShield - SafeDefend(frame.Target));
            var finalDamage = Math.Max(0, data.Hit);
            if (hpDamage <= 0 && shieldDamage <= 0)
            {
                return;
            }

            SubmitResolvedDamage(
                frame.Target,
                frame.SourceInstanceId,
                frame.SourceDataId,
                string.IsNullOrWhiteSpace(data.DamageType) ? frame.DamageType : data.DamageType,
                hpDamage,
                shieldDamage,
                finalDamage,
                DamageAttributionConfidence.Exact);
        });
    }

    private static void AfterHit(ModHookContext context)
    {
        RunHook("after hit", () =>
        {
            if (context.Target is not IStatusManager target)
            {
                return;
            }

            var targetId = SafeStatusId(target);
            var index = HitFrames.FindLastIndex(frame =>
                ReferenceEquals(frame.Target, target)
                || !string.IsNullOrWhiteSpace(targetId) && frame.TargetId == targetId);
            if (index >= 0)
            {
                HitFrames.RemoveAt(index);
            }
        });
    }

    private static void BeforePureChangeHp(ModHookContext context)
    {
        RunHook("before pure hp", () =>
        {
            if (!CaptureEnabled
                || context.Target is not IScriptExecutor executor
                || ParseInt(ArgumentString(context.Arguments, 0)) >= 0)
            {
                return;
            }

            PruneFrames();
            var source = executor.Self;
            PureHpFrames.Add(new PureHpFrame
            {
                CallId = ++nextCallId,
                Frame = Time.frameCount,
                Executor = executor,
                Source = source,
                SourceId = SafeStatusId(source),
                SourceDataId = SafeDataId(executor.dataConfig),
                Targets = ResolveTargets(executor)
                    .Distinct()
                    .Select(target => new TargetHpFrame { Target = target, BeforeHp = SafeHp(target) })
                    .ToList()
            });
            Limit(PureHpFrames, 128);
        });
    }

    private static void AfterPureChangeHp(ModHookContext context)
    {
        RunHook("after pure hp", () =>
        {
            if (context.Target is not IScriptExecutor executor)
            {
                return;
            }

            var index = PureHpFrames.FindLastIndex(frame => ReferenceEquals(frame.Executor, executor));
            if (index < 0)
            {
                return;
            }

            var frame = PureHpFrames[index];
            PureHpFrames.RemoveAt(index);
            foreach (var target in frame.Targets)
            {
                if (target.Recorded)
                {
                    continue;
                }

                var hpDamage = Math.Max(0, target.BeforeHp - SafeHp(target.Target));
                if (hpDamage <= 0)
                {
                    continue;
                }

                SubmitDirectDamage(
                    target.Target,
                    frame.Source,
                    frame.SourceId,
                    frame.SourceDataId,
                    "PureChangeHp",
                    hpDamage,
                    0,
                    hpDamage,
                    string.IsNullOrWhiteSpace(frame.SourceId)
                        ? DamageAttributionConfidence.Unknown
                        : DamageAttributionConfidence.Exact);
            }
        });
    }

    private static void BeforeSetCurHp(ModHookContext context)
    {
        RunHook("before set hp", () =>
        {
            if (!CaptureEnabled || context.Target is not IStatusManager target)
            {
                return;
            }

            var pure = PureHpFrames.LastOrDefault(frame =>
                frame.Targets.Any(item => ReferenceEquals(item.Target, target) && !item.Recorded));
            if (pure == null)
            {
                return;
            }

            HpSetterFrames.Add(new HpSetterFrame
            {
                Frame = Time.frameCount,
                Target = target,
                BeforeHp = SafeHp(target),
                PureFrameId = pure.CallId
            });
            Limit(HpSetterFrames, 128);
        });
    }

    private static void AfterSetCurHp(ModHookContext context)
    {
        RunHook("after set hp", () =>
        {
            if (context.Target is not IStatusManager target)
            {
                return;
            }

            var setterIndex = HpSetterFrames.FindLastIndex(frame => ReferenceEquals(frame.Target, target));
            if (setterIndex < 0)
            {
                return;
            }

            var setter = HpSetterFrames[setterIndex];
            HpSetterFrames.RemoveAt(setterIndex);
            var pure = PureHpFrames.LastOrDefault(frame => frame.CallId == setter.PureFrameId);
            var targetFrame = pure?.Targets.FirstOrDefault(item => ReferenceEquals(item.Target, target));
            if (pure == null || targetFrame == null)
            {
                return;
            }

            targetFrame.Recorded = true;
            var hpDamage = Math.Max(0, setter.BeforeHp - SafeHp(target));
            if (hpDamage <= 0)
            {
                return;
            }

            SubmitDirectDamage(
                target,
                pure.Source,
                pure.SourceId,
                pure.SourceDataId,
                "PureChangeHp",
                hpDamage,
                0,
                hpDamage,
                string.IsNullOrWhiteSpace(pure.SourceId)
                    ? DamageAttributionConfidence.Unknown
                    : DamageAttributionConfidence.Exact);
        });
    }

    private static void BeforeScriptAddBuff(ModHookContext context)
    {
        RunHook("before add buff", () =>
        {
            if (!CaptureEnabled || context.Target is not IScriptExecutor executor)
            {
                return;
            }

            var trackerId = BuffAttribution.BeginApplication(
                executor,
                ArgumentString(context.Arguments, 0),
                Time.frameCount);
            if (trackerId <= 0)
            {
                return;
            }

            BuffFrames.Add(new BuffApplicationFrame
            {
                Executor = executor,
                TrackerId = trackerId,
                Frame = Time.frameCount
            });
            Limit(BuffFrames, 128);
        });
    }

    private static void AfterScriptAddBuff(ModHookContext context)
    {
        RunHook("after add buff", () =>
        {
            if (context.Target is not IScriptExecutor executor)
            {
                return;
            }

            var index = BuffFrames.FindLastIndex(frame => ReferenceEquals(frame.Executor, executor));
            if (index < 0)
            {
                return;
            }

            var frame = BuffFrames[index];
            BuffFrames.RemoveAt(index);
            BuffAttribution.CompleteApplication(frame.TrackerId);
        });
    }

    private static void AfterRemoveBuff(ModHookContext context)
    {
        RunHook("remove buff", () =>
        {
            if (context.Target is IStatusManager target)
            {
                BuffAttribution.RemoveBuff(target, ArgumentString(context.Arguments, 0));
            }
        });
    }

    private static void AfterBuffLevelChanged(ModHookContext context)
    {
        RunHook("buff level changed", () =>
        {
            if (context.Target is IBuffItemConfig config)
            {
                BuffAttribution.OnLevelChanged(
                    config,
                    ParseInt(ArgumentString(context.Arguments, 0)),
                    Time.frameCount);
            }
        });
    }

    private static void SubmitResolvedDamage(
        IStatusManager target,
        string sourceInstanceId,
        string sourceDataId,
        string damageType,
        int hpDamage,
        int shieldDamage,
        int finalDamage,
        DamageAttributionConfidence confidence)
    {
        var buffParts = BuffAttribution.Split(
            target,
            sourceDataId,
            hpDamage,
            shieldDamage,
            finalDamage);
        if (buffParts.Count > 0)
        {
            foreach (var part in buffParts)
            {
                SubmitDirectDamage(
                    target,
                    CombatantTeamResolver.ResolveStatus(part.SourceId),
                    part.SourceId,
                    sourceDataId,
                    damageType,
                    part.HpDamage,
                    part.ShieldDamage,
                    part.FinalDamage,
                    part.Confidence,
                    part.SourceName,
                    part.SourceTeam);
            }

            return;
        }

        var unresolvedBuffOwner = DamageDetailResolver.IsBuff(sourceDataId)
                                  && (string.IsNullOrWhiteSpace(sourceInstanceId)
                                      || string.Equals(
                                          sourceInstanceId,
                                          SafeStatusId(target),
                                          StringComparison.Ordinal));
        if (unresolvedBuffOwner)
        {
            sourceInstanceId = "unknown";
        }

        var source = CombatantTeamResolver.ResolveStatus(sourceInstanceId);
        SubmitDirectDamage(
            target,
            source,
            sourceInstanceId,
            sourceDataId,
            damageType,
            hpDamage,
            shieldDamage,
            finalDamage,
            unresolvedBuffOwner || string.IsNullOrWhiteSpace(sourceInstanceId)
                ? DamageAttributionConfidence.Unknown
                : confidence);
    }

    private static void SubmitDirectDamage(
        IStatusManager target,
        IStatusManager? source,
        string sourceInstanceId,
        string sourceDataId,
        string damageType,
        int hpDamage,
        int shieldDamage,
        int finalDamage,
        DamageAttributionConfidence confidence,
        string? sourceName = null,
        DamageTeam? sourceTeam = null)
    {
        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceInstanceId)
            ? "unknown"
            : sourceInstanceId.Trim();
        var damage = new DamageEvent
        {
            SourceInstanceId = normalizedSourceId,
            SourceDisplayName = string.IsNullOrWhiteSpace(sourceName)
                ? CombatantTeamResolver.DisplayName(source, normalizedSourceId)
                : sourceName!,
            SourceTeam = sourceTeam ?? CombatantTeamResolver.Resolve(source, normalizedSourceId),
            TargetInstanceId = SafeStatusId(target),
            SourceDataId = sourceDataId?.Trim() ?? "",
            DetailLabel = DamageDetailResolver.ResolveLabel(sourceDataId ?? "", damageType),
            DamageType = string.IsNullOrWhiteSpace(damageType) ? "Unknown" : damageType.Trim(),
            HpDamage = Math.Max(0, hpDamage),
            ShieldDamage = Math.Max(0, shieldDamage),
            FinalDamage = Math.Max(0, finalDamage),
            AttributionConfidence = confidence
        };
        NormalizeEvent(damage);
        DamageMeterNetworkRuntime.Submit(damage);
    }

    private static void NormalizeEvent(DamageEvent damage)
    {
        damage.SourceInstanceId = Trim(damage.SourceInstanceId, "unknown");
        damage.SourceDisplayName = Trim(damage.SourceDisplayName, damage.SourceInstanceId);
        damage.TargetInstanceId = Trim(damage.TargetInstanceId, "");
        damage.SourceDataId = Trim(damage.SourceDataId, "");
        damage.DetailLabel = Trim(damage.DetailLabel, damage.SourceDataId);
        damage.DamageType = Trim(damage.DamageType, "Unknown");
        damage.HpDamage = Math.Max(0, Math.Min(DamageMeterProtocol.MaxDamagePerEvent, damage.HpDamage));
        damage.ShieldDamage = Math.Max(0, Math.Min(DamageMeterProtocol.MaxDamagePerEvent, damage.ShieldDamage));
        damage.FinalDamage = Math.Max(0, Math.Min(DamageMeterProtocol.MaxDamagePerEvent, damage.FinalDamage));
    }

    private static string Trim(string value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.Length <= DamageMeterProtocol.MaxStringLength
            ? result
            : result.Substring(0, DamageMeterProtocol.MaxStringLength);
    }

    private static int FindHitFrame(DamageTextInfo data)
    {
        for (var i = HitFrames.Count - 1; i >= 0; i--)
        {
            var frame = HitFrames[i];
            if (!string.Equals(frame.TargetId, data.To, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(data.From)
                && !string.IsNullOrWhiteSpace(frame.SourceInstanceId)
                && !string.Equals(frame.SourceInstanceId, data.From, StringComparison.Ordinal))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool TryReadDamageText(object? value, out DamageTextInfo data)
    {
        data = new DamageTextInfo();
        if (value == null)
        {
            return false;
        }

        try
        {
            data.From = ReflectionUtil.GetMemberValue(value, "from")?.ToString() ?? "";
            data.To = ReflectionUtil.GetMemberValue(value, "to")?.ToString() ?? "";
            data.DamageType = ReflectionUtil.GetMemberValue(value, "damageType")?.ToString() ?? "";
            var hit = ReflectionUtil.GetMemberValue(value, "hit");
            data.Hit = hit == null ? 0 : Convert.ToInt32(hit, CultureInfo.InvariantCulture);
            return !string.IsNullOrWhiteSpace(data.To);
        }
        catch
        {
            return false;
        }
    }

    private static void PruneFrames()
    {
        var frame = Time.frameCount;
        HitFrames.RemoveAll(item => frame - item.Frame > 4);
        PureHpFrames.RemoveAll(item => frame - item.Frame > 4);
        HpSetterFrames.RemoveAll(item => frame - item.Frame > 4);
        for (var i = BuffFrames.Count - 1; i >= 0; i--)
        {
            if (frame - BuffFrames[i].Frame <= 4)
            {
                continue;
            }

            BuffAttribution.CancelApplication(BuffFrames[i].TrackerId);
            BuffFrames.RemoveAt(i);
        }
    }

    private static void ResetCaptureState()
    {
        HitFrames.Clear();
        PureHpFrames.Clear();
        HpSetterFrames.Clear();
        BuffFrames.Clear();
        BuffAttribution.Clear();
        nextCallId = 0;
        lastRoundStartFrame = -10000;
        lastRoundUnit = null;
    }

    private static void Limit<T>(List<T> list, int maximum)
    {
        if (list.Count > maximum)
        {
            list.RemoveRange(0, list.Count - maximum);
        }
    }

    private static IEnumerable<IStatusManager> ResolveTargets(IScriptExecutor executor)
    {
        if (executor.Object != null && executor.Object.Count > 0)
        {
            foreach (var target in executor.Object)
            {
                if (target != null)
                {
                    yield return target;
                }
            }

            yield break;
        }

        if (executor.status != null)
        {
            yield return executor.status;
            yield break;
        }

        if (executor.Target != null)
        {
            yield return executor.Target;
        }
    }

    private static string ArgumentString(object[]? arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Length
            ? arguments[index]?.ToString() ?? ""
            : "";
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static int SafeHp(IStatusManager status)
    {
        try
        {
            return status.CurHp;
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeDefend(IStatusManager status)
    {
        try
        {
            return status.Defend;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeStatusId(IStatusManager? status)
    {
        try
        {
            return status?.InstanceId?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string SafeDataId(IDataConfig? dataConfig)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
            {
                return id?.Trim() ?? "";
            }
        }
        catch
        {
        }

        try
        {
            return dataConfig?.InstanceID?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, warn: AuraToolsLog.Warn, safeInvoke: true);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, warn: AuraToolsLog.Warn, safeInvoke: true);
    }

    private static void RunHook(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] " + name + " failed: " + ex.Message);
        }
    }

    private sealed class HitFrame
    {
        public long CallId { get; set; }
        public int Frame { get; set; }
        public IStatusManager Target { get; set; } = null!;
        public string TargetId { get; set; } = "";
        public int BeforeHp { get; set; }
        public int BeforeShield { get; set; }
        public string DamageType { get; set; } = "";
        public string SourceDataId { get; set; } = "";
        public string SourceInstanceId { get; set; } = "";
    }

    private sealed class PureHpFrame
    {
        public long CallId { get; set; }
        public int Frame { get; set; }
        public IScriptExecutor Executor { get; set; } = null!;
        public IStatusManager? Source { get; set; }
        public string SourceId { get; set; } = "";
        public string SourceDataId { get; set; } = "";
        public List<TargetHpFrame> Targets { get; set; } = new();
    }

    private sealed class TargetHpFrame
    {
        public IStatusManager Target { get; set; } = null!;
        public int BeforeHp { get; set; }
        public bool Recorded { get; set; }
    }

    private sealed class BuffApplicationFrame
    {
        public int Frame { get; set; }
        public IScriptExecutor Executor { get; set; } = null!;
        public long TrackerId { get; set; }
    }

    private sealed class DamageTextInfo
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public int Hit { get; set; }
        public string DamageType { get; set; } = "";
    }

    private sealed class HpSetterFrame
    {
        public int Frame { get; set; }
        public IStatusManager Target { get; set; } = null!;
        public int BeforeHp { get; set; }
        public long PureFrameId { get; set; }
    }
}
