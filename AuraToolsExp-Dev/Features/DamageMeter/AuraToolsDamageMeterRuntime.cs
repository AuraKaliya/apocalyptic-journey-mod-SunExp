using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AuraJourney.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter;

public static class AuraToolsDamageMeterRuntime
{
    private static readonly List<IDisposable> HookRegistrations = new();
    private static readonly DamageFrameWindow<HitFrame> HitFrames = new(256);
    private static readonly DamageFrameWindow<PureHpFrame> PureHpFrames = new(128, ReleasePureFrameTargets);
    private static readonly DamageFrameWindow<HpSetterFrame> HpSetterFrames = new(128);
    private static readonly DamageFrameWindow<BuffApplicationFrame> BuffFrames = new(128);
    private static readonly Stack<List<TargetHpFrame>> TargetFrameListPool = new();
    private static readonly Stack<TargetHpFrame> TargetFramePool = new();
    private static readonly List<TargetHpFrame> EmptyTargetFrames = new();
    private static readonly Dictionary<string, byte[]> AvatarPngCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BuffAttributionEngine BuffAttribution = new();
    private const int MaxAvatarCacheEntries = 32;
    private const int MaxTargetFrameListPool = 32;
    private const int MaxTargetFramePool = 256;
    private static ModConfig? modConfig;
    private static bool initialized;
    private static bool hooksRegistered;
    private static bool endingSent;
    private static bool adventureSettlementRecorded;
    private static bool adventureHistoryRestoreAttempted;
    private static bool outOfRunHistoryLoaded;
    private static float nextRefreshAt;
    private static float uiRetryBlockedUntil;
    private static float nextUiFailureLogAt;
    private static bool disabledUiHidden;
    private static bool preparationUiActive;
    private static int lastRoundStartFrame = -10000;
    private static object? lastRoundUnit;
    private static long nextCallId;
    private static bool uiDirty = true;
    private static int lastPruneFrame = -1;
    private static readonly Dictionary<Type, DamageTextAccessor> DamageTextAccessors = new();

    public static bool Visible { get; private set; }

    public static bool Available { get; private set; }

    public static bool Enabled => AuraToolsConfigService.Root.MatchExperience.Enabled
                                  && AuraToolsConfigService.MatchExperience.DamageMeter.Enabled;

    internal static DamageLedger Ledger => DamageMeterNetworkRuntime.Ledger;

    internal static DamageRunLedger RunAggregate => DamageMeterNetworkRuntime.RunAggregate;

    internal static DamageHistoryStore History => DamageMeterNetworkRuntime.History;

    internal static OutOfRunDamageHistoryStore OutOfRunHistory => DamageMeterNetworkRuntime.OutOfRunHistory;

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
        AuraToolsDamageMeterRuntime.modConfig = modConfig;
        AuraToolsConfigService.Changed += OnConfigChanged;
        EnsureHooksMatchConfig();
        if (AuraToolsConfigService.MatchExperience.DamageMeter.LoadHistoryOnStartup)
        {
            EnsureOutOfRunHistoryLoaded("startup");
        }

        AuraToolsLog.Info("[DamageMeter] DPT runtime initialized. Network protocol v"
                          + DamageMeterProtocol.Version + ".");
    }

    private static void EnsureHooksMatchConfig()
    {
        if (Enabled)
        {
            EnsureHooksRegistered();
            AuraToolsDamageMeterUi.EnsureDriver();
            return;
        }

        ReleaseHooks();
        AuraToolsDamageMeterUi.ReleaseDriver();
    }

    private static void EnsureHooksRegistered()
    {
        if (hooksRegistered || modConfig == null)
        {
            return;
        }

        RegisterAfter("GameEntryUI.Init", HideForEntryUi);
        RegisterAfter("GameEntryUI.Outlobby", HideForEntryUi);
        RegisterAfter("GameEntryUI.ReturnHouse", HideForEntryUi);
        RegisterAfter("GameEntryUI.ShowCareer", ShowForPreparationUi);
        RegisterAfter("GameEntryUI.ShowDetail", ShowForPreparationUi);
        RegisterAfter("GameEntryUI.ChangeRole", ShowForPreparationUi);
        RegisterBefore("GameEntryUI.StartGame", ShowForStartGame);
        RegisterBefore("GameApp.GameOver", OnAdventureSettlement);
        RegisterBefore("PlayerManager.GameOver", OnAdventureSettlement);
        RegisterAfter("GameExitUI.Start", OnAdventureSettlement);
        RegisterAfter("NormalMapManager.InitRoleTable", ShowForAdventureUi);
        RegisterAfter("SublimationManager.InitRoleTable", ShowForAdventureUi);
        RegisterAfter("SlotMachineManager.InitRoleTable", ShowForAdventureUi);
        RegisterAfter("TopBarUI.Awake", ShowForAdventureUi);
        RegisterAfter("TopBarUI.Start", ShowForAdventureUi);
        RegisterAfter("TopBarUI.ShowLeftUp", ShowForAdventureUi);
        RegisterAfter("MapSelectUI.Start", ShowForAdventureUi);
        RegisterAfter("MapSelectUI.ReadyToSelect", ShowForAdventureUi);
        RegisterAfter("MapSelectUI.ShowMap", ShowForAdventureUi);
        RegisterAfter("MapSelectUI.MapAnimation", ShowForAdventureUi);

        RegisterBefore("StatusManager.Hit", BeforeHit);
        RegisterAfter("StatusManager.Hit", AfterHit);
        RegisterBefore("DamageText.Create", BeforeDamageTextCreate);
        RegisterBefore("ScriptExecutor.PureChangeHp", BeforePureChangeHp);
        RegisterAfter("ScriptExecutor.PureChangeHp", AfterPureChangeHp);
        RegisterBefore("StatusManager.set_CurHp", BeforeSetCurHp);
        RegisterAfter("StatusManager.set_CurHp", AfterSetCurHp);
        RegisterBefore("ScriptExecutor.AddBuff", BeforeScriptAddBuff);
        RegisterAfter("ScriptExecutor.AddBuff", AfterScriptAddBuff);
        RegisterAfter("BuffItemConfig.set_Level", AfterBuffLevelChanged);
        RegisterAfter("StatusManager.RemoveBuff", AfterRemoveBuff);

        HookRegistrations.Add(AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            "DamageMeter",
            new AuraBattleLifecycleSubscription
            {
                FightStarting = OnFightInitStarting,
                FightStarted = OnFightStartFallback,
                PlayerRoundStarted = OnPlayerRoundStart,
                FightEnding = OnFightEnding,
                FightEnded = OnFightEnded
            },
            AuraToolsLog.Debug,
            AuraToolsLog.Warn));

        hooksRegistered = true;
        AuraToolsLog.Info("[DamageMeter] routed hooks enabled.");
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
        ResetCaptureState();
        DamageMeterNetworkRuntime.EndFight("disabled");
        AuraToolsLog.Info("[DamageMeter] routed hooks disabled.");
    }

    public static void Tick()
    {
        if (!Enabled)
        {
            HideDisabledUiSafe();
            return;
        }

        disabledUiHidden = false;
        DamageMeterNetworkRuntime.Tick();
        ReconcileAvailabilitySafe();
        RefreshUiSafe();
        DamageMeterPerformanceCounters.MaybeLog();
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
        var refreshInterval = Math.Max(
            0.1f,
            AuraToolsConfigService.MatchExperience.DamageMeter.UiRefreshIntervalMs / 1000f);
        if (now < uiRetryBlockedUntil || now < nextRefreshAt)
        {
            return;
        }

        if (!uiDirty && !Ledger.InFight)
        {
            nextRefreshAt = now + refreshInterval;
            return;
        }

        if (!Available || !Visible)
        {
            nextRefreshAt = now + refreshInterval;
            uiDirty = false;
            return;
        }

        try
        {
            var startedAt = DamageMeterPerformanceCounters.StartSample();
            nextRefreshAt = now + refreshInterval;
            uiDirty = false;
            AuraToolsDamageMeterUi.Refresh(
                Ledger,
                RunAggregate,
                History,
                AuraToolsConfigService.MatchExperience.DamageMeter,
                NetworkStatus);
            DamageMeterPerformanceCounters.RecordUiRefresh(
                DamageMeterPerformanceCounters.ElapsedMs(startedAt),
                Ledger.Combatants.Count,
                Ledger.InFight);
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
        EnsureHooksMatchConfig();
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
            if (IsSupportedDamageMeterContext(context, allowMapManagerFallback: true))
            {
                preparationUiActive = false;
                SetAvailable(true, "FightInit.Init");
                PrepareSettlementCgAssets("FightInit.Init");
            }

            ResetCaptureState();
            DamageMeterFightIndex.BeginFight();
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
                if (IsActiveDamageMeterContext())
                {
                    SetAvailable(true, "Fight_Start.Init");
                }

                ResetCaptureState();
                DamageMeterFightIndex.BeginFight();
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
        RunHook("entry UI hidden scope", () =>
        {
            DamageSettlementCgRuntime.BeginAdventure();
            SetAvailable(false, GetHookName(context));
        });
    }

    private static void ShowForPreparationUi(ModHookContext context)
    {
        RunHook("preparation UI scope", () =>
        {
            if (!IsSupportedDamageMeterLobby())
            {
                SetAvailable(false, GetHookName(context) + ":unsupported-mode");
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
            if (IsSupportedDamageMeterContext(context, allowMapManagerFallback: false))
            {
                DamageMeterNetworkRuntime.BeginAdventure();
                DamageSettlementCgRuntime.BeginAdventure();
                adventureSettlementRecorded = false;
                adventureHistoryRestoreAttempted = false;
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
            if (!IsSupportedDamageMeterContext(context, allowMapManagerFallback: true))
            {
                return;
            }

            preparationUiActive = false;
            RestoreAdventureHistoryOnce();
            SetAvailable(true, GetHookName(context));
            PrepareSettlementCgAssets(GetHookName(context));
        });
    }

    private static void RestoreAdventureHistoryOnce()
    {
        if (adventureHistoryRestoreAttempted)
        {
            return;
        }

        adventureHistoryRestoreAttempted = true;
        DamageMeterNetworkRuntime.RestoreAdventureHistory();
    }

    private static void PrepareSettlementCgAssets(string source)
    {
        var members = CollectTeamMembers(captureAvatars: false);
        if (members.Count == 0)
        {
            AuraToolsLog.Warn("[SettlementCG] preload skipped: no team members. source=" + source + ".");
            return;
        }

        DamageSettlementCgRuntime.PrepareForTeam(members);
    }

    private static void OnAdventureSettlement(ModHookContext context)
    {
        RunHook("adventure settlement", () =>
        {
            var source = GetHookName(context);
            EnsureOutOfRunHistoryLoaded("adventure settlement:" + source);
            if (adventureSettlementRecorded)
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: already archived. source=" + source + ".");
                return;
            }

            if (!DamageMeterNetworkRuntime.IsHost)
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: not host. source=" + source + ".");
                return;
            }

            if (!IsSupportedDamageMeterAdventureContext())
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: unsupported context. source=" + source + ".");
                return;
            }

            ArchiveActiveFightForSettlement();
            var aggregate = RunAggregate.CreateSnapshot();
            if (!RunAggregate.HasDamage && History.Records.Count == 0)
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: no fight history. source=" + source + ".");
                return;
            }

            adventureSettlementRecorded = true;
            var mode = ResolvePlayMode();
            var request = new OutOfRunDamageHistoryBuildRequest
            {
                AdventureId = DamageMeterNetworkRuntime.CurrentAdventureId,
                ModeId = mode.Id,
                ModeDisplayName = mode.DisplayName,
                Status = IsCurrentAdventureCompleted()
                    ? OutOfRunDamageHistoryStatus.Completed
                    : OutOfRunDamageHistoryStatus.Failed,
                EndedUtc = DateTime.UtcNow.ToString("O"),
                TeamMembers = CollectTeamMembers(AuraToolsConfigService.MatchExperience.DamageMeter.CaptureTeamAvatars)
            };
            var record = RunAggregate.HasDamage
                ? OutOfRunDamageHistoryBuilder.Build(aggregate, request, countShield: true)
                : OutOfRunDamageHistoryBuilder.Build(History.Records, request, countShield: true);
            DamageSettlementCgRuntime.TryPlay(record);

            if (OutOfRunHistory.Add(record))
            {
                OutOfRunDamageHistoryPersistence.SaveDeferred(
                    OutOfRunHistory,
                    AuraToolsConfigService.MatchExperience.DamageMeter.MaxHistoryEnvelopeBytes);
                uiDirty = true;
                AuraToolsLog.Info("[DamageMeter] out-of-run history archived. mode="
                                  + mode.Id + ", status=" + record.Status + ", source=" + source + ".");
            }
            else
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: duplicate adventure id. source=" + source + ".");
            }
        });
    }

    public static int OutOfRunHistoryCount
    {
        get
        {
            EnsureOutOfRunHistoryLoaded("history count");
            return OutOfRunHistory.Records.Count;
        }
    }

    public static void OpenOutOfRunHistory()
    {
        EnsureOutOfRunHistoryLoaded("open history");
        AuraToolsDamageMeterUi.ShowOutOfRunHistory(OutOfRunHistory);
    }

    public static void ClearOutOfRunHistory()
    {
        EnsureOutOfRunHistoryLoaded("clear history");
        OutOfRunDamageHistoryPersistence.Clear(
            OutOfRunHistory,
            AuraToolsConfigService.MatchExperience.DamageMeter.MaxHistoryEnvelopeBytes);
        uiDirty = true;
    }

    private static void EnsureOutOfRunHistoryLoaded(string source)
    {
        if (outOfRunHistoryLoaded)
        {
            return;
        }

        outOfRunHistoryLoaded = true;
        var started = DateTime.UtcNow;
        OutOfRunDamageHistoryPersistence.LoadInto(
            OutOfRunHistory,
            AuraToolsConfigService.MatchExperience.DamageMeter.MaxHistoryEnvelopeBytes);
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
        if (elapsed >= 50d)
        {
            AuraToolsLog.Warn("[DamageMeter] out-of-run history load was slow. source="
                              + source + ", elapsedMs=" + elapsed.ToString("F0", CultureInfo.InvariantCulture) + ".");
        }
    }

    private static void ArchiveActiveFightForSettlement()
    {
        if (!Ledger.InFight)
        {
            return;
        }

        endingSent = true;
        DamageMeterNetworkRuntime.EndFight(IsGameExitLoss() ? "Loss" : "Win");
    }

    private static PlayModeInfo ResolvePlayMode()
    {
        if (IsSolarMemoryMode())
        {
            return new PlayModeInfo("SunExp.SolarMemory", "日耀回忆");
        }

        var modeType = ReadLobbyModeType();
        if (string.IsNullOrWhiteSpace(modeType))
        {
            modeType = MapManager.Instance?.ModeMapManager?.GetType().Name ?? "";
        }

        if (string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modeType, "NormalMapManager", StringComparison.OrdinalIgnoreCase))
        {
            return new PlayModeInfo("Normal", "世界推演");
        }

        if (string.Equals(modeType, "Sublimation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modeType, "SublimationManager", StringComparison.OrdinalIgnoreCase))
        {
            return new PlayModeInfo("Sublimation", "弑神模拟");
        }

        if (string.Equals(modeType, "Slot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modeType, "SlotMachineManager", StringComparison.OrdinalIgnoreCase))
        {
            return new PlayModeInfo("Slot", "老虎机模式");
        }

        return new PlayModeInfo(string.IsNullOrWhiteSpace(modeType) ? "Unknown" : modeType, "未知模式");
    }

    private static bool IsCurrentAdventureCompleted()
    {
        if (IsGameExitLoss())
        {
            return false;
        }

        try
        {
            return MapManager.Instance != null && MapManager.Instance.WinTheGame();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGameExitLoss()
    {
        try
        {
            return GameExitUI.loss;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSolarMemoryMode()
    {
        try
        {
            return AuraJourneyRuntime.IsJourneyActive("AuraTools", "SunExp", "SunExp.SolarMemory");
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<OutOfRunTeamMemberSnapshot> CollectTeamMembers(bool captureAvatars)
    {
        var result = new List<OutOfRunTeamMemberSnapshot>();
        try
        {
            var roleTables = GameServer.Instance?.RoleTables;
            if (roleTables != null && roleTables.Count > 0)
            {
                foreach (var role in roleTables.Values)
                {
                    AddTeamMember(result, role, captureAvatars);
                    if (result.Count >= DamageMeterProtocol.MaxTeamMembers)
                    {
                        break;
                    }
                }
            }
            else
            {
                AddTeamMember(result, RoleTable.Instance, captureAvatars);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] team snapshot failed: " + ex.Message);
        }

        return result;
    }

    private static void AddTeamMember(List<OutOfRunTeamMemberSnapshot> result, RoleTable? role, bool captureAvatars)
    {
        if (role == null || result.Count >= DamageMeterProtocol.MaxTeamMembers)
        {
            return;
        }

        var career = role.Career;
        var playerId = role.Id ?? "";
        var roleId = SafeDataField(career, "Id");
        var roleDisplayName = SafeLocalizedField(career, "Name");
        if (string.IsNullOrWhiteSpace(roleDisplayName))
        {
            roleDisplayName = string.IsNullOrWhiteSpace(roleId) ? playerId : roleId;
        }

        var playerDisplayName = ResolvePlayerDisplayName(playerId);
        if (string.IsNullOrWhiteSpace(playerDisplayName))
        {
            playerDisplayName = string.IsNullOrWhiteSpace(playerId) ? roleDisplayName : playerId;
        }

        var avatarPath = SafeDataField(career, "Avatar");
        if (string.IsNullOrWhiteSpace(avatarPath))
        {
            avatarPath = SafeDataField(career, "DollIcon");
        }

        var avatarBytes = captureAvatars
            ? TryEncodeSprite(avatarPath, playerId, roleId)
            : Array.Empty<byte>();
        result.Add(new OutOfRunTeamMemberSnapshot
        {
            InstanceId = playerId,
            PlayerId = playerId,
            PlayerDisplayName = playerDisplayName,
            RoleId = roleId,
            RoleDisplayName = roleDisplayName,
            DisplayName = playerDisplayName,
            AvatarPngBase64 = avatarBytes.Length == 0 ? "" : Convert.ToBase64String(avatarBytes),
            AvatarSha256 = avatarBytes.Length == 0 ? "" : AuraSharedSecureEnvelope.Sha256Hex(avatarBytes)
        });
    }

    private static string SafeDataField(IDataConfig? dataConfig, string key)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue(key, out var value))
            {
                return value ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    private static string ResolvePlayerDisplayName(string playerId)
    {
        try
        {
            var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
            var lobbyName = "";
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player != null && string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase))
                    {
                        lobbyName = player.Name;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(lobbyName))
            {
                return (lobbyName ?? "").Trim();
            }
        }
        catch
        {
        }

        try
        {
            var playerManager = PlayerManager.Instance;
            var managerName = playerManager?.playerInfo?.Name;
            if (playerManager != null
                && string.Equals(playerManager.PlayerId, playerId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(managerName))
            {
                return (managerName ?? "").Trim();
            }
        }
        catch
        {
        }

        try
        {
            var config = Singleton<GameConfigManager>.Instance;
            if (config != null
                && string.Equals(config.PlayerId, playerId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(config.PlayerName))
            {
                return config.PlayerName.Trim();
            }
        }
        catch
        {
        }

        return "";
    }

    private static string SafeLocalizedField(IDataConfig? dataConfig, string key)
    {
        try
        {
            var localized = dataConfig?.data?.Localize(key) ?? "";
            return string.Equals(localized, key, StringComparison.OrdinalIgnoreCase) ? "" : localized;
        }
        catch
        {
            return SafeDataField(dataConfig, key);
        }
    }

    private static byte[] TryEncodeSprite(string resourcePath, string playerId, string roleId)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return Array.Empty<byte>();
        }

        var cacheKey = resourcePath.Trim();
        if (AvatarPngCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var sprite = AuraToolsResourceCache.Load<Sprite>(resourcePath, true);
            if (sprite == null || sprite.texture == null)
            {
                AuraToolsLog.Warn("[DamageMeter] team avatar skipped: resource not found. player="
                                  + playerId + ", role=" + roleId + ", path=" + resourcePath + ".");
                return Array.Empty<byte>();
            }

            var settings = AuraToolsConfigService.MatchExperience.DamageMeter;
            var rect = sprite.textureRect;
            var pixelCount = Math.Max(1L, (long)Mathf.Max(1, (int)rect.width)
                                      * Mathf.Max(1, (int)rect.height));
            if (pixelCount > settings.MaxAvatarEncodePixels)
            {
                AuraToolsLog.Warn("[DamageMeter] team avatar skipped: sprite too large. player="
                                  + playerId + ", role=" + roleId + ", path=" + resourcePath
                                  + ", pixels=" + pixelCount
                                  + ", maxPixels=" + settings.MaxAvatarEncodePixels + ".");
                return Array.Empty<byte>();
            }

            var texture = CopySpriteTexture(sprite);
            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            if (bytes == null || bytes.Length == 0)
            {
                return Array.Empty<byte>();
            }

            if (bytes.Length > settings.MaxAvatarPngBytes)
            {
                AuraToolsLog.Warn("[DamageMeter] team avatar skipped: PNG too large. player="
                                  + playerId + ", role=" + roleId + ", path=" + resourcePath
                                  + ", bytes=" + bytes.Length
                                  + ", maxBytes=" + settings.MaxAvatarPngBytes + ".");
                return Array.Empty<byte>();
            }

            CacheAvatarPng(cacheKey, bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] team avatar encode failed: player="
                              + playerId + ", role=" + roleId + ", path=" + resourcePath
                              + ", error=" + ex.Message);
            return Array.Empty<byte>();
        }
    }

    private static void CacheAvatarPng(string key, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(key) || bytes.Length == 0)
        {
            return;
        }

        if (AvatarPngCache.Count >= MaxAvatarCacheEntries)
        {
            AvatarPngCache.Clear();
        }

        AvatarPngCache[key] = bytes;
    }

    private static Texture2D CopySpriteTexture(Sprite sprite)
    {
        var rect = sprite.textureRect;
        var x = Mathf.Max(0, (int)rect.x);
        var y = Mathf.Max(0, (int)rect.y);
        var width = Mathf.Max(1, (int)rect.width);
        var height = Mathf.Max(1, (int)rect.height);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            var pixels = sprite.texture.GetPixels(x, y, width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        catch
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(
                sprite.texture.width,
                sprite.texture.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            try
            {
                Graphics.Blit(sprite.texture, temporary);
                RenderTexture.active = temporary;
                texture.ReadPixels(new Rect(x, y, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    private sealed class PlayModeInfo
    {
        public PlayModeInfo(string id, string displayName)
        {
            Id = id ?? "";
            DisplayName = displayName ?? "";
        }

        public string Id { get; }

        public string DisplayName { get; }
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
            if (Available && !IsActiveDamageMeterContext())
            {
                SetAvailable(false, "context-lost");
            }
        }
        catch (Exception ex)
        {
            LogUiFailure("availability reconcile", ex);
        }
    }

    private static bool IsActiveDamageMeterContext()
    {
        return preparationUiActive && IsSupportedDamageMeterLobby()
               || IsSupportedDamageMeterAdventureContext();
    }

    private static bool IsSupportedDamageMeterContext(ModHookContext context, bool allowMapManagerFallback)
    {
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return IsSupportedModeType(modeType);
        }

        if (IsSupportedModeManager(context.Target))
        {
            return true;
        }

        if (IsSupportedDamageMeterAdventureContext())
        {
            return true;
        }

        return allowMapManagerFallback && IsSupportedModeManager(MapManager.Instance?.ModeMapManager);
    }

    private static bool IsSupportedDamageMeterLobby()
    {
        return IsSupportedModeType(ReadLobbyModeType());
    }

    private static bool IsSupportedDamageMeterAdventureContext()
    {
        try
        {
            return IsSupportedModeManager(MapManager.Instance?.ModeMapManager);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedModeType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "Normal", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "Sublimation", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "Slot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedModeManager(object? value)
    {
        var name = value?.GetType().Name ?? "";
        return string.Equals(name, "NormalMapManager", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "SublimationManager", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "SlotMachineManager", StringComparison.OrdinalIgnoreCase);
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

            DamageMeterPerformanceCounters.RecordHitHook();
            PruneFrames();
            var arguments = context.Arguments ?? Array.Empty<object>();
            var frame = HitFrames.Rent(Time.frameCount);
            frame.CallId = ++nextCallId;
            frame.Target = target;
            frame.TargetId = SafeStatusId(target);
            frame.BeforeHp = SafeHp(target);
            frame.BeforeShield = SafeDefend(target);
            frame.DamageType = ArgumentString(arguments, 1);
            frame.SourceDataId = ArgumentString(arguments, 2);
            frame.SourceInstanceId = ArgumentString(arguments, 3);
            HitFrames.Add(frame);
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

            DamageMeterPerformanceCounters.RecordDamageTextHook();
            var frameIndex = FindHitFrame(data);
            if (frameIndex < 0)
            {
                return;
            }

            var frame = HitFrames[frameIndex];
            var target = frame.Target;
            var sourceInstanceId = frame.SourceInstanceId;
            var sourceDataId = frame.SourceDataId;
            var damageType = string.IsNullOrWhiteSpace(data.DamageType) ? frame.DamageType : data.DamageType;
            var hpDamage = Math.Max(0, frame.BeforeHp - SafeHp(target));
            var shieldDamage = Math.Max(0, frame.BeforeShield - SafeDefend(target));
            var finalDamage = Math.Max(0, data.Hit);
            HitFrames.RemoveAt(frameIndex);
            if (hpDamage <= 0 && shieldDamage <= 0)
            {
                return;
            }

            SubmitResolvedDamage(
                target,
                sourceInstanceId,
                sourceDataId,
                damageType,
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
            var index = -1;
            for (var i = HitFrames.Count - 1; i >= 0; i--)
            {
                var frame = HitFrames[i];
                if (ReferenceEquals(frame.Target, target)
                    || !string.IsNullOrWhiteSpace(targetId) && frame.TargetId == targetId)
                {
                    index = i;
                    break;
                }
            }

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

            DamageMeterPerformanceCounters.RecordPureHpHook();
            PruneFrames();
            var source = executor.Self;
            var targets = CaptureTargetHpFrames(executor);
            if (targets.Count == 0)
            {
                ReleaseTargetFrameList(targets);
                return;
            }

            var frame = PureHpFrames.Rent(Time.frameCount);
            frame.CallId = ++nextCallId;
            frame.Executor = executor;
            frame.Source = source;
            frame.SourceId = SafeStatusId(source);
            frame.SourceDataId = SafeDataId(executor.dataConfig);
            frame.Targets = targets;
            PureHpFrames.Add(frame);
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

            var index = -1;
            for (var i = PureHpFrames.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(PureHpFrames[i].Executor, executor))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            var frame = PureHpFrames[index];
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

            PureHpFrames.RemoveAt(index);
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

            var pure = FindPureFrameForTarget(target);
            if (pure == null)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordHpSetterHook();
            var frame = HpSetterFrames.Rent(Time.frameCount);
            frame.Target = target;
            frame.BeforeHp = SafeHp(target);
            frame.PureFrameId = pure.CallId;
            HpSetterFrames.Add(frame);
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

            var setterIndex = -1;
            for (var i = HpSetterFrames.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(HpSetterFrames[i].Target, target))
                {
                    setterIndex = i;
                    break;
                }
            }

            if (setterIndex < 0)
            {
                return;
            }

            var setter = HpSetterFrames[setterIndex];
            var beforeHp = setter.BeforeHp;
            var pureFrameId = setter.PureFrameId;
            HpSetterFrames.RemoveAt(setterIndex);
            var pure = FindPureFrameById(pureFrameId);
            var targetFrame = pure == null ? null : FindTargetFrame(pure, target);
            if (pure == null || targetFrame == null)
            {
                return;
            }

            targetFrame.Recorded = true;
            var hpDamage = Math.Max(0, beforeHp - SafeHp(target));
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

            DamageMeterPerformanceCounters.RecordBuffHook();
            var trackerId = BuffAttribution.BeginApplication(
                executor,
                ArgumentString(context.Arguments, 0),
                Time.frameCount);
            if (trackerId <= 0)
            {
                return;
            }

            var frame = BuffFrames.Rent(Time.frameCount);
            frame.Executor = executor;
            frame.TrackerId = trackerId;
            BuffFrames.Add(frame);
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

            var index = -1;
            for (var i = BuffFrames.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(BuffFrames[i].Executor, executor))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            var frame = BuffFrames[index];
            var trackerId = frame.TrackerId;
            BuffFrames.RemoveAt(index);
            BuffAttribution.CompleteApplication(trackerId);
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
        var emittedBuffParts = BuffAttribution.EmitSplit(
            target,
            sourceDataId,
            hpDamage,
            shieldDamage,
            finalDamage,
            (partSourceId, partSourceName, partSourceTeam, partHp, partShield, partFinal, partConfidence) =>
            {
                SubmitDirectDamage(
                    target,
                    CombatantTeamResolver.ResolveStatus(partSourceId),
                    partSourceId,
                    sourceDataId,
                    damageType,
                    partHp,
                    partShield,
                    partFinal,
                    partConfidence,
                    partSourceName,
                    partSourceTeam);
            });
        if (emittedBuffParts)
        {
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

    private static PureHpFrame? FindPureFrameForTarget(IStatusManager target)
    {
        for (var i = PureHpFrames.Count - 1; i >= 0; i--)
        {
            var frame = PureHpFrames[i];
            for (var j = 0; j < frame.Targets.Count; j++)
            {
                var item = frame.Targets[j];
                if (!item.Recorded && ReferenceEquals(item.Target, target))
                {
                    return frame;
                }
            }
        }

        return null;
    }

    private static PureHpFrame? FindPureFrameById(long callId)
    {
        for (var i = PureHpFrames.Count - 1; i >= 0; i--)
        {
            if (PureHpFrames[i].CallId == callId)
            {
                return PureHpFrames[i];
            }
        }

        return null;
    }

    private static TargetHpFrame? FindTargetFrame(PureHpFrame frame, IStatusManager target)
    {
        for (var i = 0; i < frame.Targets.Count; i++)
        {
            var item = frame.Targets[i];
            if (ReferenceEquals(item.Target, target))
            {
                return item;
            }
        }

        return null;
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
            var accessor = GetDamageTextAccessor(value.GetType());
            data.From = accessor.ReadString(value, "from");
            data.To = accessor.ReadString(value, "to");
            data.DamageType = accessor.ReadString(value, "damageType");
            data.Hit = accessor.ReadInt(value, "hit");
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
        if (lastPruneFrame == frame)
        {
            return;
        }

        lastPruneFrame = frame;
        HitFrames.PruneOlderThan(frame, 4);
        PureHpFrames.PruneOlderThan(frame, 4);
        HpSetterFrames.PruneOlderThan(frame, 4);

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
        DamageMeterFightIndex.Clear();
        nextCallId = 0;
        lastPruneFrame = -1;
        lastRoundStartFrame = -10000;
        lastRoundUnit = null;
    }

    private static DamageTextAccessor GetDamageTextAccessor(Type type)
    {
        if (!DamageTextAccessors.TryGetValue(type, out var accessor))
        {
            accessor = new DamageTextAccessor(type);
            DamageTextAccessors[type] = accessor;
        }

        return accessor;
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

    private static List<TargetHpFrame> CaptureTargetHpFrames(IScriptExecutor executor)
    {
        var frames = RentTargetFrameList();
        foreach (var target in ResolveTargets(executor))
        {
            if (target == null || ContainsTarget(frames, target))
            {
                continue;
            }

            var frame = RentTargetFrame();
            frame.Target = target;
            frame.BeforeHp = SafeHp(target);
            frames.Add(frame);
        }

        return frames;
    }

    private static List<TargetHpFrame> RentTargetFrameList()
    {
        return TargetFrameListPool.Count > 0
            ? TargetFrameListPool.Pop()
            : new List<TargetHpFrame>(4);
    }

    private static TargetHpFrame RentTargetFrame()
    {
        return TargetFramePool.Count > 0 ? TargetFramePool.Pop() : new TargetHpFrame();
    }

    private static void ReleasePureFrameTargets(PureHpFrame frame)
    {
        ReleaseTargetFrameList(frame.Targets);
        frame.Targets = EmptyTargetFrames;
    }

    private static void ReleaseTargetFrameList(List<TargetHpFrame>? frames)
    {
        if (frames == null || ReferenceEquals(frames, EmptyTargetFrames))
        {
            return;
        }

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var frame = frames[i];
            frame.Reset();
            if (TargetFramePool.Count < MaxTargetFramePool)
            {
                TargetFramePool.Push(frame);
            }
        }

        frames.Clear();
        if (TargetFrameListPool.Count < MaxTargetFrameListPool)
        {
            TargetFrameListPool.Push(frames);
        }
    }

    private static bool ContainsTarget(List<TargetHpFrame> frames, IStatusManager target)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            if (ReferenceEquals(frames[i].Target, target))
            {
                return true;
            }
        }

        return false;
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

    private static void RegisterBefore(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            target,
            action,
            warn: AuraToolsLog.Warn,
            safeInvoke: true));
    }

    private static void RegisterAfter(string target, Action<ModHookContext> action)
    {
        if (modConfig == null)
        {
            return;
        }

        HookRegistrations.Add(AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            target,
            action,
            warn: AuraToolsLog.Warn,
            safeInvoke: true));
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

    private sealed class HitFrame : IDamageCaptureFrame
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

        public void Reset()
        {
            CallId = 0;
            Frame = 0;
            Target = null!;
            TargetId = "";
            BeforeHp = 0;
            BeforeShield = 0;
            DamageType = "";
            SourceDataId = "";
            SourceInstanceId = "";
        }
    }

    private sealed class DamageTextAccessor
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Dictionary<string, MemberInfo?> members = new(StringComparer.Ordinal);

        public DamageTextAccessor(Type type)
        {
            members["from"] = FindMember(type, "from");
            members["to"] = FindMember(type, "to");
            members["damageType"] = FindMember(type, "damageType");
            members["hit"] = FindMember(type, "hit");
        }

        public string ReadString(object source, string name)
        {
            return Read(source, name)?.ToString() ?? "";
        }

        public int ReadInt(object source, string name)
        {
            var value = Read(source, name);
            if (value is int typed)
            {
                return typed;
            }

            return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private object? Read(object source, string name)
        {
            return members.TryGetValue(name, out var member)
                ? member switch
                {
                    PropertyInfo property => property.GetValue(source),
                    FieldInfo field => field.GetValue(source),
                    _ => null
                }
                : null;
        }

        private static MemberInfo? FindMember(Type type, string name)
        {
            return type.GetProperty(name, Flags) ?? (MemberInfo?)type.GetField(name, Flags);
        }
    }

    private sealed class PureHpFrame : IDamageCaptureFrame
    {
        public long CallId { get; set; }
        public int Frame { get; set; }
        public IScriptExecutor Executor { get; set; } = null!;
        public IStatusManager? Source { get; set; }
        public string SourceId { get; set; } = "";
        public string SourceDataId { get; set; } = "";
        public List<TargetHpFrame> Targets { get; set; } = new();

        public void Reset()
        {
            CallId = 0;
            Frame = 0;
            Executor = null!;
            Source = null;
            SourceId = "";
            SourceDataId = "";
            Targets = EmptyTargetFrames;
        }
    }

    private sealed class TargetHpFrame
    {
        public IStatusManager Target { get; set; } = null!;
        public int BeforeHp { get; set; }
        public bool Recorded { get; set; }

        public void Reset()
        {
            Target = null!;
            BeforeHp = 0;
            Recorded = false;
        }
    }

    private sealed class BuffApplicationFrame : IDamageCaptureFrame
    {
        public int Frame { get; set; }
        public IScriptExecutor Executor { get; set; } = null!;
        public long TrackerId { get; set; }

        public void Reset()
        {
            Frame = 0;
            Executor = null!;
            TrackerId = 0;
        }
    }

    private sealed class DamageTextInfo
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public int Hit { get; set; }
        public string DamageType { get; set; } = "";
    }

    private sealed class HpSetterFrame : IDamageCaptureFrame
    {
        public int Frame { get; set; }
        public IStatusManager Target { get; set; } = null!;
        public int BeforeHp { get; set; }
        public long PureFrameId { get; set; }

        public void Reset()
        {
            Frame = 0;
            Target = null!;
            BeforeHp = 0;
            PureFrameId = 0;
        }
    }
}
