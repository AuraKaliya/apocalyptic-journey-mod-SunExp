using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraMode.Shared;
using AuraShared.Core;
using AudioArbiter.Shared;
using AuraSkin.Shared.Mechanics;
using Data.Save;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Media;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Capture;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayRecorder
{
    private static readonly object Gate = new();
    private static readonly List<string> Diagnostics = new();
    private static readonly ReplayAudioAttachmentCaptureV11 AudioCapture = new();
    private static MatchRecord? activeRecord;
    private static ReplayDocumentV11? activeDocument;
    private static ReplayContentCatalogBuilderV11? catalog;
    private static ReplayLogicalStateV11? lastState;
    private static ActiveAction? activeAction;
    private static long startedTimestamp;
    private static long nextSequence;
    private static int nextActionIndex;
    private static int turnIndex = 1;
    private static bool firstPlayerRoundSeen;
    private static string lastAudioCueKey = "";
    private static long lastAudioStartSample = long.MinValue;
    private static string lastBgmResourceId = "";
    private static ReplayAudioCueV11? activeBgmCue;
    private static bool completionPrepared;
    private static string pendingCompletionResult = "";

    internal static bool IsRecording
    {
        get
        {
            lock (Gate) return activeDocument != null;
        }
    }

    internal static void Start(object[]? arguments)
    {
        if (!AuraToolsMatchRecordsRuntime.ReplayEnabled || MatchReplaySessionState.IsPlayback) return;
        var levelId = Argument<string>(arguments, 0) ?? FightManager.Instance?.level ?? "";
        var roleQueue = Argument<byte[]>(arguments, 1) ?? Array.Empty<byte>();
        var temporaryRoles = Argument<byte[]>(arguments, 2) ?? Array.Empty<byte>();
        var enemyPositive = Argument<float>(arguments, 3);
        var enemyHp = Argument<float>(arguments, 4);
        CaptureRuntimeContext(out var mapMode, out var mapLevel, out var diceJson);
        lock (Gate)
        {
            ResetNoLock();
            var recordId = Guid.NewGuid().ToString("N");
            activeRecord = new MatchRecord
            {
                RecordId = recordId,
                SessionId = recordId,
                AdventureId = Features.DamageMeter.Network.DamageMeterNetworkRuntime.CurrentAdventureId,
                LevelId = levelId,
                BattleTitle = AuraToolsPlayerDisplay.LevelName(levelId),
                StartedUtc = DateTime.UtcNow.ToString("O"),
                Collection = MatchRecordCollections.Auto,
                ReplayProtocol = MatchReplayProtocol.Version,
                GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
                ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
                ModFingerprint = CurrentRuntimeFingerprint(),
                RequiredCapabilities = MatchReplayCapabilities.Supported.ToList(),
                InitialState = new MatchReplayInitialState
                {
                    LevelId = levelId,
                    BackgroundScene = GameApp.Instance?.NowBackground?.name ?? "",
                    MapMode = mapMode,
                    MapLevel = mapLevel,
                    DiceJson = diceJson,
                    RoleQueue = (byte[])roleQueue.Clone(),
                    TemporaryRoles = (byte[])temporaryRoles.Clone(),
                    EnemyPositive = enemyPositive,
                    EnemyHp = enemyHp,
                    RoleTableJson = RoleTable.Instance == null ? "" : AuraSharedJson.Serialize(RoleTable.Instance)
                }
            };
            activeDocument = new ReplayDocumentV11
            {
                Header = new ReplayDocumentHeaderV11
                {
                    RecordId = recordId,
                    AdventureId = activeRecord.AdventureId,
                    SessionId = recordId,
                    LevelId = levelId,
                    BattleTitle = activeRecord.BattleTitle,
                    StartedUtc = activeRecord.StartedUtc,
                    GameBuild = activeRecord.GameBuild,
                    ToolBuild = activeRecord.ToolBuild,
                    RendererBuild = activeRecord.ToolBuild,
                    RuntimeFingerprint = activeRecord.ModFingerprint,
                    RequiredCapabilities = activeRecord.RequiredCapabilities.ToList()
                },
                NativeBattle = new ReplayNativeBattleContextV11
                {
                    SceneName = activeRecord.InitialState.BackgroundScene,
                    BackgroundScene = activeRecord.InitialState.BackgroundScene,
                    MapMode = activeRecord.InitialState.MapMode,
                    MapLevel = activeRecord.InitialState.MapLevel,
                    DiceJson = activeRecord.InitialState.DiceJson,
                    RoleQueue = (byte[])activeRecord.InitialState.RoleQueue.Clone(),
                    TemporaryRoles = (byte[])activeRecord.InitialState.TemporaryRoles.Clone(),
                    EnemyPositive = activeRecord.InitialState.EnemyPositive,
                    EnemyHp = activeRecord.InitialState.EnemyHp,
                    RoleTableJson = activeRecord.InitialState.RoleTableJson,
                    SkinSelections = CaptureSkinSelections(activeRecord.InitialState)
                }
            };
            catalog = new ReplayContentCatalogBuilderV11();
            AudioArbiterRuntime.ResolvedPlayback += OnResolvedPlayback;
            startedTimestamp = Stopwatch.GetTimestamp();
        }
    }

    internal static void StartFromCurrentFight()
    {
        if (FightManager.Instance == null) return;
        lock (Gate)
        {
            if (activeDocument == null) Start(new object[] { FightManager.Instance.level ?? "" });
            EnsureBaselineNoLock();
        }
    }

    internal static void BeginCardAction(object? target)
    {
        if (target == null) return;
        lock (Gate)
        {
            if (!EnsureBaselineNoLock()) return;
            BeginActionNoLock(ReplayFactCaptureV11.CaptureActionSource(target, catalog!));
        }
    }

    internal static void EndCardAction(object? target)
    {
        lock (Gate)
        {
            if (activeAction == null) return;
            if (target != null)
            {
                var latest = ReplayFactCaptureV11.CaptureActionSource(target, catalog!);
                if (!string.IsNullOrWhiteSpace(latest.SourceInstanceId)) activeAction.Source = latest;
            }

            EndActionNoLock();
        }
    }

    internal static void BeginEnemyIntentAction(object? target, object[]? arguments)
    {
        lock (Gate)
        {
            if (!EnsureBaselineNoLock()) return;
            var intent = ReplayIntentCaptureV11.CaptureExecuting(target, arguments, catalog!);
            if (intent == null) return;
            var definition = catalog!.Manifest.Definitions.FirstOrDefault(item => item.Content.Key == intent.Content.Key);
            BeginActionNoLock(new ReplayActionSourceV11
            {
                ActorId = intent.ActorId,
                SourceInstanceId = intent.InstanceId,
                Content = intent.Content,
                Label = definition?.Display.Name ?? intent.Content.StableContentId,
                PresentationKind = ReplayPresentationKindsV11.EnemyIntent,
                ActionState = DisplayValue(definition, "Action", "Idle"),
                EffectName = DisplayValue(definition, "Effects", "")
            });
        }
    }

    internal static void EndEnemyIntentAction(object? target)
    {
        lock (Gate)
        {
            if (activeAction == null) return;
            if (target is Enemy enemy
                && !string.Equals(
                    activeAction.Source.ActorId,
                    enemy.Status?.InstanceId ?? enemy.InstanceId ?? "",
                    StringComparison.Ordinal))
            {
                return;
            }

            EndActionNoLock();
        }
    }

    internal static void CaptureRemoteCommand(AuraRemoteCombatActionContext context)
    {
        if (context == null
            || !string.Equals(context.Kind, AuraRemoteCombatActionKinds.CardUse, StringComparison.Ordinal)
            || context.CardData == null)
        {
            return;
        }

        lock (Gate)
        {
            if (!EnsureBaselineNoLock()) return;
            var config = context.CardData;
            var stableId = ReplayFactCaptureV11.Read(config.data, "Id");
            var content = catalog!.Register("Card", config, stableId);
            BeginActionNoLock(new ReplayActionSourceV11
            {
                ActorId = context.ActorId ?? "",
                SourceInstanceId = config is DataConfig concrete
                    ? concrete.InstanceID ?? ""
                    : ReplayFactCaptureV11.Read(config.Vars, "InstanceID"),
                Content = content,
                Label = ReplayFactCaptureV11.First(
                    ReplayFactCaptureV11.Read(config.Vars, "Name"),
                    ReplayFactCaptureV11.Read(config.data, "Name"),
                    stableId),
                PresentationKind = string.Equals(
                    ReplayFactCaptureV11.Read(config.data, "Action"),
                    "Skill",
                    StringComparison.OrdinalIgnoreCase)
                    ? ReplayPresentationKindsV11.Skill
                    : ReplayPresentationKindsV11.Card,
                ActionState = ReplayFactCaptureV11.First(
                    ReplayFactCaptureV11.Read(config.Vars, "Action"),
                    ReplayFactCaptureV11.Read(config.data, "Action"),
                    "Idle"),
                EffectName = ReplayFactCaptureV11.First(
                    ReplayFactCaptureV11.Read(config.Vars, "Effects"),
                    ReplayFactCaptureV11.Read(config.data, "Effects"))
            }, remote: true);
            EndActionNoLock();
        }
    }

    internal static void ObserveAuthoritativeStatus(AuraAuthoritativeStatusContext context)
    {
        lock (Gate)
        {
            if (activeAction == null || !activeAction.FinalizationScheduled) return;
            ScheduleFinalizationNoLock(activeAction.ActionId);
        }
    }

    internal static void CaptureActionPresentation(object[]? arguments)
    {
        if (MatchReplaySessionState.IsPlayback
            || arguments == null
            || arguments.Length == 0
            || arguments[0] is not IScriptExecutor executor)
            return;
        try
        {
            var presentation = MatchReplayActionPresentationCapture.Capture(executor);
            lock (Gate)
            {
                if (activeAction == null || presentation == null) return;
                activeAction.NativePresentation = presentation;
            }
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                if (Diagnostics.Count < 32)
                    Diagnostics.Add("native action presentation: " + ex.Message);
            }
        }
    }

    internal static void CaptureNativeAudio(object[]? arguments, string bus)
    {
        var resourceId = ExtractNativeResourceIds(arguments).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resourceId)) return;
        if (string.Equals(bus, "Effect", StringComparison.OrdinalIgnoreCase)
            && !resourceId.StartsWith("Sounds/", StringComparison.OrdinalIgnoreCase))
        {
            resourceId = "Sounds/" + resourceId;
        }
        lock (Gate)
        {
            var cue = RecordNativeAudioCueNoLock(resourceId, bus, bus);
            var clip = arguments?.OfType<AudioClip>().FirstOrDefault() ?? ResolveNativeClip(resourceId);
            AttachAudioNoLock(cue, clip, "Native." + bus);
        }
    }

    internal static void CaptureNativeBgm(object[]? arguments)
    {
        var resourceId = ExtractNativeResourceIds(arguments).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resourceId)) return;
        lock (Gate)
        {
            RecordNativeBgmNoLock(resourceId, arguments?.OfType<AudioClip>().FirstOrDefault());
        }
    }

    internal static void CaptureCurrentBgm()
    {
        lock (Gate)
        {
            if (activeDocument != null) CaptureLevelNativeBgmNoLock();
        }
    }

    internal static void CaptureCheckpointIfDue()
    {
    }

    internal static void StartTurn()
    {
        lock (Gate)
        {
            if (!EnsureBaselineNoLock()) return;
            FlushActionNoLock();
            if (firstPlayerRoundSeen) turnIndex++;
            else firstPlayerRoundSeen = true;
            var after = ReplayFactCaptureV11.CaptureState(turnIndex, catalog!);
            AddEventNoLock(new ReplayTimelineEventV11
            {
                TimeTicks = ElapsedTicks(),
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV11.TurnChanged,
                ActorId = after.ActiveActorId,
                Delta = ReplayProjectionStateV11.CreateDelta(lastState!, after)
            });
            lastState = after;
        }
    }

    internal static void Complete(string result)
    {
        CompletionSnapshot? completion;
        lock (Gate)
        {
            if (activeDocument == null
                || activeRecord == null
                || completionPrepared)
            {
                return;
            }

            EnsureBaselineNoLock();
            FlushActionNoLock();
            if (lastState != null && catalog != null)
            {
                var finalState = ReplayFactCaptureV11.CaptureState(turnIndex, catalog);
                if (!string.Equals(
                        ReplayProjectionStateV11.Hash(lastState),
                        ReplayProjectionStateV11.Hash(finalState),
                        StringComparison.Ordinal))
                {
                    AddEventNoLock(new ReplayTimelineEventV11
                    {
                        TimeTicks = ElapsedTicks(),
                        TurnIndex = turnIndex,
                        EventType = ReplayEventTypesV11.StateChanged,
                        ActorId = finalState.ActiveActorId,
                        Delta = ReplayProjectionStateV11.CreateDelta(lastState, finalState)
                    });
                    lastState = finalState;
                }
            }

            AddEventNoLock(new ReplayTimelineEventV11
            {
                TimeTicks = ElapsedTicks(),
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV11.BattleCompleted,
                ActorId = lastState?.ActiveActorId ?? ""
            });
            completionPrepared = true;
            pendingCompletionResult = string.IsNullOrWhiteSpace(result)
                ? "Unknown"
                : result.Trim();
            if (AudioCapture.PendingCount > 0)
            {
                AudioCapture.Drained -= OnAudioCapturesDrained;
                AudioCapture.Drained += OnAudioCapturesDrained;
                return;
            }
            completion = DetachCompletionNoLock();
        }

        if (completion != null)
        {
            QueueFinalization(
                completion.Record,
                completion.Document,
                completion.Diagnostics);
        }
    }

    private static CompletionSnapshot? DetachCompletionNoLock()
    {
        if (activeDocument == null || activeRecord == null) return null;
        var record = activeRecord;
        var document = activeDocument;
        document.Content = catalog?.Manifest ?? new ReplayContentManifestV11();
        document.Attachments = catalog?.Attachments ?? new List<ReplayAttachmentV11>();
        record.Result = pendingCompletionResult.Length == 0
            ? "Unknown"
            : pendingCompletionResult;
        record.EndedUtc = DateTime.UtcNow.ToString("O");
        record.TurnCount = Math.Max(1, turnIndex);
        record.EventCount = document.Events.Count;
        record.StatisticsJson = AuraSharedJson.SerializeCompact(
            AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
        record.CaptureDiagnostics = new List<string>(Diagnostics);
        document.Header.Result = record.Result;
        document.Header.EndedUtc = record.EndedUtc;
        document.Header.LevelId = record.LevelId;
        var completion = new CompletionSnapshot(
            record,
            document,
            new List<string>(Diagnostics));
        ResetNoLock();
        return completion;
    }

    internal static void Abort()
    {
        lock (Gate) ResetNoLock();
    }

    internal static string CurrentRuntimeFingerprint()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(item => !item.IsDynamic)
            .Where(item => IsReplayDependencyAssembly(item.GetName().Name ?? ""))
            .Select(item =>
            {
                try
                {
                    return (item.GetName().Name ?? "") + "|"
                           + (item.GetName().Version?.ToString() ?? "") + "|"
                           + item.ManifestModule.ModuleVersionId.ToString("N");
                }
                catch
                {
                    return item.FullName ?? "unknown";
                }
            })
            .OrderBy(item => item, StringComparer.Ordinal);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", assemblies)))
            .Select(item => item.ToString("x2")));
    }

    private static bool EnsureBaselineNoLock()
    {
        if (activeDocument == null || activeRecord == null || catalog == null || FightManager.Instance == null)
        {
            return false;
        }

        if (lastState != null) return true;
        turnIndex = 1;
        lastState = ReplayFactCaptureV11.CaptureState(turnIndex, catalog);
        activeDocument.InitialState = ReplayProjectionStateV11.Clone(lastState);
        var nativeBaseline = MatchReplayStateCapture.CaptureProjectionSnapshot(turnIndex);
        nativeBaseline.RoleTableJson = activeRecord.InitialState.RoleTableJson;
        activeRecord.InitialState.BaselineState = nativeBaseline;
        catalog.RegisterBackground(
            activeRecord.LevelId,
            GameApp.Instance?.NowBackground?.name ?? activeRecord.LevelId,
            GameApp.Instance?.NowBackground);
        CaptureLevelNativeBgmNoLock();
        return true;
    }

    private static bool IsReplayDependencyAssembly(string name)
    {
        return string.Equals(name, "Witch", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Witch.Core", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Plugins", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Mirror", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Aura", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".Aura", StringComparison.OrdinalIgnoreCase);
    }

    private static void OnAudioCapturesDrained()
    {
        CompletionSnapshot? completion;
        lock (Gate)
        {
            AudioCapture.Drained -= OnAudioCapturesDrained;
            completion = completionPrepared ? DetachCompletionNoLock() : null;
        }
        if (completion != null)
            QueueFinalization(completion.Record, completion.Document, completion.Diagnostics);
    }

    private static void CaptureLevelNativeBgmNoLock()
    {
        if (activeRecord == null) return;
        var level = AuraGameDataHostApi.Resolve(DataType.Level, activeRecord.LevelId);
        if (level == null || !level.Fields.TryGetValue("BGM", out var configured)) return;
        var resourceId = (configured ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => NormalizeNativeResourceId(value))
            .FirstOrDefault(value => value.Length > 0);
        if (!string.IsNullOrWhiteSpace(resourceId)) RecordNativeBgmNoLock(resourceId);
    }

    private static void RecordNativeBgmNoLock(string resourceId, AudioClip? capturedClip = null)
    {
        var normalized = NormalizeNativeResourceId(resourceId);
        if (normalized.Length == 0
            || activeDocument == null
            || lastState == null
            || string.Equals(normalized, lastBgmResourceId, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            lastBgmResourceId = normalized;
            var timelineTicks = nextSequence == 0 ? 0 : ElapsedTicks();
            var startSample = timelineTicks * ReplayOfflineAudioMixer.SampleRate / ReplayProtocolV11.TimebaseTicksPerSecond;
            if (activeBgmCue != null)
            {
                activeBgmCue.DurationSamples = Math.Max(0, startSample - activeBgmCue.StartSample);
                activeBgmCue.FadeOutSamples = Math.Min(ReplayOfflineAudioMixer.SampleRate / 4, activeBgmCue.DurationSamples);
            }
            var cue = new ReplayAudioCueV11
            {
                AssetSha256 = "",
                NativeResourceId = normalized,
                ResolutionPolicy = "embedded-required",
                OwnerModId = "Witch",
                ProviderId = "native:" + normalized,
                Kind = "BattleBgm",
                StartSample = startSample,
                SourceOffsetSample = 0,
                DurationSamples = 0,
                GainQ16 = 65_536,
                LoopStartSample = 0,
                LoopEndSample = 0,
                FadeInSamples = ReplayOfflineAudioMixer.SampleRate / 8,
                Bus = "Bgm"
            };
            activeBgmCue = cue;
            AttachAudioNoLock(cue, capturedClip ?? ResolveNativeClip(normalized), "BattleBgm");
            AddEventNoLock(new ReplayTimelineEventV11
            {
                TimeTicks = timelineTicks,
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV11.StateChanged,
                ActorId = lastState?.ActiveActorId ?? "",
                Audio = new List<ReplayAudioCueV11> { cue }
            });
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] native BGM cue capture skipped: " + ex.Message);
        }
    }

    private static ReplayAudioCueV11? RecordNativeAudioCueNoLock(string resourceId, string bus, string kind)
    {
        var normalized = NormalizeNativeResourceId(resourceId);
        if (normalized.Length == 0 || activeDocument == null || lastState == null) return null;
        var startSample = ElapsedTicks()
                          * ReplayOfflineAudioMixer.SampleRate
                          / ReplayProtocolV11.TimebaseTicksPerSecond;
        var key = (bus ?? "") + "|" + normalized;
        if (string.Equals(key, lastAudioCueKey, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(startSample - lastAudioStartSample) <= ReplayOfflineAudioMixer.SampleRate / 20)
            return null;
        lastAudioCueKey = key;
        lastAudioStartSample = startSample;
        var cue = new ReplayAudioCueV11
        {
            NativeResourceId = normalized,
            ResolutionPolicy = "embedded-required",
            OwnerModId = "Witch",
            ProviderId = "native:" + normalized,
            Kind = kind ?? "NativeAudio",
            StartSample = startSample,
            GainQ16 = 65_536,
            Bus = string.IsNullOrWhiteSpace(bus) ? "Effect" : (bus ?? "Effect").Trim()
        };
        if (activeAction != null)
        {
            activeAction.Audio.Add(cue);
        }
        else
        {
            AddEventNoLock(new ReplayTimelineEventV11
            {
                TimeTicks = ElapsedTicks(),
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV11.StateChanged,
                ActorId = lastState.ActiveActorId,
                Audio = new List<ReplayAudioCueV11> { cue }
            });
        }
        return cue;
    }

    private static void AttachAudioNoLock(ReplayAudioCueV11? cue, AudioClip? clip, string usage)
    {
        if (cue == null) return;
        AudioCapture.Request(clip, usage, attachment =>
        {
            lock (Gate)
            {
                if (attachment == null || catalog == null)
                {
                    if (Diagnostics.Count < 32)
                        Diagnostics.Add("required replay audio could not be captured: " + usage);
                    return;
                }
                cue.AssetSha256 = catalog.RegisterAttachment(attachment);
                cue.ResolutionPolicy = "embedded-required";
                if (clip != null)
                {
                    cue.DurationSamples = cue.DurationSamples > 0
                        ? cue.DurationSamples
                        : (long)Math.Ceiling(clip.samples / (double)clip.frequency * ReplayOfflineAudioMixer.SampleRate);
                    if (string.Equals(cue.Bus, "Bgm", StringComparison.OrdinalIgnoreCase))
                        cue.LoopEndSample = clip.samples;
                }
            }
        });
    }

    private static AudioClip? ResolveNativeClip(string resourceId)
    {
        var normalized = NormalizeNativeResourceId(resourceId);
        if (normalized.Length == 0) return null;
        foreach (var candidate in new[]
                 {
                     normalized,
                     normalized.StartsWith("Sounds/", StringComparison.OrdinalIgnoreCase) ? normalized : "Sounds/" + normalized,
                     normalized.StartsWith("BGM/", StringComparison.OrdinalIgnoreCase) ? normalized : "BGM/" + normalized,
                     normalized.StartsWith("Sounds/BGM/", StringComparison.OrdinalIgnoreCase) ? normalized : "Sounds/BGM/" + normalized
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var clip = AuraToolsResourceCache.Load<AudioClip>(candidate);
                if (clip != null) return clip;
            }
            catch
            {
            }
        }
        return null;
    }

    private static void OnResolvedPlayback(ResolvedSoundPlayback playback)
    {
        if (playback?.Clip is not AudioClip clip) return;
        lock (Gate)
        {
            if (activeDocument == null || lastState == null) return;
            var startSample = ElapsedTicks() * ReplayOfflineAudioMixer.SampleRate / ReplayProtocolV11.TimebaseTicksPerSecond;
            var key = (playback.Bus ?? "Effect") + "|resolved|" + clip.GetInstanceID();
            if (string.Equals(key, lastAudioCueKey, StringComparison.Ordinal)
                && Math.Abs(startSample - lastAudioStartSample) <= ReplayOfflineAudioMixer.SampleRate / 20)
                return;
            lastAudioCueKey = key;
            lastAudioStartSample = startSample;
            var cue = new ReplayAudioCueV11
            {
                OwnerModId = playback.OwnerModId ?? "",
                ProviderId = playback.ProviderId ?? "",
                Kind = playback.Request?.Kind ?? "ResolvedAudio",
                StartSample = startSample,
                GainQ16 = (int)Math.Round(Math.Max(0f, playback.VolumeMultiplier) * 65_536f),
                Bus = string.IsNullOrWhiteSpace(playback.Bus) ? "Effect" : playback.Bus ?? "Effect",
                ResolutionPolicy = "embedded-required"
            };
            if (activeAction != null) activeAction.Audio.Add(cue);
            else
            {
                AddEventNoLock(new ReplayTimelineEventV11
                {
                    TimeTicks = ElapsedTicks(),
                    TurnIndex = turnIndex,
                    EventType = ReplayEventTypesV11.StateChanged,
                    ActorId = lastState.ActiveActorId,
                    Audio = new List<ReplayAudioCueV11> { cue }
                });
            }
            AttachAudioNoLock(cue, clip, "Resolved." + cue.Kind);
        }
    }

    private static IEnumerable<string> ExtractNativeResourceIds(object[]? arguments)
    {
        foreach (var argument in arguments ?? Array.Empty<object>())
        {
            if (argument is string text)
            {
                var value = NormalizeNativeResourceId(text);
                if (value.Length > 0) yield return value;
                continue;
            }
            if (argument is IEnumerable<string> values)
            {
                foreach (var textValue in values)
                {
                    var value = NormalizeNativeResourceId(textValue);
                    if (value.Length > 0) yield return value;
                }
            }
        }
    }

    private static string NormalizeNativeResourceId(string value)
    {
        var id = (value ?? "").Trim().Replace('\\', '/').TrimStart('/');
        if (id.Length == 0
            || id.Length > 240
            || id.Contains(":")
            || id.Split('/').Any(segment => segment == "..")
            || id.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("SharedResources/", StringComparison.OrdinalIgnoreCase))
            return "";
        return id;
    }

    private static void BeginActionNoLock(ReplayActionSourceV11 source, bool remote = false)
    {
        if (activeDocument == null || lastState == null) return;
        if (activeAction != null)
        {
            if (!activeAction.FinalizationScheduled && !remote)
            {
                activeAction.Depth++;
                return;
            }

            FlushActionNoLock();
        }

        var actionId = "action-" + (++nextActionIndex).ToString("D6");
        activeAction = new ActiveAction
        {
            ActionId = actionId,
            Source = source,
            Before = ReplayProjectionStateV11.Clone(lastState),
            Depth = 1,
            IsRemote = remote
        };
        AddEventNoLock(ReplayActionDerivationV11.Started(
            nextSequence + 1,
            ElapsedTicks(),
            turnIndex,
            actionId,
            source));
        activeAction.StartedEventId = activeDocument.Events[activeDocument.Events.Count - 1].EventId;
    }

    private static void EndActionNoLock()
    {
        if (activeAction == null) return;
        activeAction.Depth = Math.Max(0, activeAction.Depth - 1);
        if (activeAction.Depth > 0 || activeAction.FinalizationScheduled) return;
        activeAction.FinalizationScheduled = true;
        ScheduleFinalizationNoLock(activeAction.ActionId);
    }

    private static void ScheduleFinalizationNoLock(string actionId)
    {
        if (activeAction == null
            || !string.Equals(
                activeAction.ActionId,
                actionId,
                StringComparison.Ordinal))
        {
            return;
        }
        var generation = ++activeAction.FinalizationGeneration;
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV11.Action.Finalize."
                  + actionId
                  + "."
                  + generation,
            Source = "MatchRecords.ReplayV11.Capture",
            DelayFrames = 2,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 25,
            EstimatedCost = 1,
            Action = () => FinalizeAction(actionId, generation)
        });
    }

    private static void FinalizeAction(string actionId, long generation)
    {
        lock (Gate)
        {
            if (activeAction == null
                || catalog == null
                || !string.Equals(activeAction.ActionId, actionId, StringComparison.Ordinal)
                || activeAction.FinalizationGeneration != generation)
            {
                return;
            }

            if (activeAction.Depth > 0)
            {
                ScheduleFinalizationNoLock(actionId);
                return;
            }

            FlushActionNoLock(ReplayFactCaptureV11.CaptureState(turnIndex, catalog));
        }
    }

    private static void FlushActionNoLock(ReplayLogicalStateV11? settled = null)
    {
        if (activeAction == null || activeDocument == null || catalog == null) return;
        var after = settled ?? ReplayFactCaptureV11.CaptureState(turnIndex, catalog);
        var completed = ReplayActionDerivationV11.Completed(
            nextSequence + 1,
            ElapsedTicks(),
            turnIndex,
            activeAction.ActionId,
            activeAction.Source,
            activeAction.Before,
            after,
            activeAction.StartedEventId);
        var nativePresentation = activeAction.NativePresentation ?? new MatchReplayActionPresentationState
        {
            ActorAnimationState = activeAction.Source.ActionState,
            EffectName = activeAction.Source.EffectName,
            EffectDelayMilliseconds = 0,
            PresentationDurationMilliseconds = 360
        };
        completed.NativePresentation = new ReplayNativeActionPresentationV11
            {
                ActorAnimationState = nativePresentation.ActorAnimationState,
                EffectName = nativePresentation.EffectName,
                EffectDelayMilliseconds = nativePresentation.EffectDelayMilliseconds,
                PresentationDurationMilliseconds = nativePresentation.PresentationDurationMilliseconds,
                Targets = nativePresentation.Targets.Select(value => new ReplayNativeTargetPresentationV11
                {
                    TargetId = value.TargetId,
                    AnimationState = value.AnimationState
                }).ToList()
            };
        completed.Audio.AddRange(activeAction.Audio);
        AddEventNoLock(completed);
        lastState = after;
        activeAction = null;
    }

    private static void AddEventNoLock(ReplayTimelineEventV11 value)
    {
        if (activeDocument == null) return;
        value.Sequence = ++nextSequence;
        value.EventId = "event-" + value.Sequence.ToString("D8");
        value.TimeTicks = Math.Max(0, value.TimeTicks);
        value.TurnIndex = Math.Max(1, value.TurnIndex);
        activeDocument.Events.Add(value);
        if (value.Sequence % ReplayProtocolV11.DefaultCheckpointInterval == 0
            || string.Equals(value.EventType, ReplayEventTypesV11.TurnChanged, StringComparison.Ordinal))
        {
            activeDocument.Checkpoints.Add(new ReplayCheckpointV11 { EventSequence = value.Sequence });
        }
    }

    private static void QueueFinalization(
        MatchRecord record,
        ReplayDocumentV11 document,
        IReadOnlyCollection<string> diagnostics)
    {
        var database = MatchRecordStorage.Database;
        var limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<FinalizationResult>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV11.Finalize." + record.RecordId,
            Source = "MatchRecords.ReplayV11.Finalize",
            Kind = AuraSharedBackgroundWorkKind.Io,
            Work = _ => FinalizeDetached(record, document, diagnostics, database, limit),
            ApplyOnMainThread = LogFinalization,
            OnFailedOnMainThread = ex =>
            {
                AuraToolsLog.Warn("[MatchRecords] v11 background finalization failed: " + ex.Message);
                LogFinalization(FinalizeDetached(record, document, diagnostics, database, limit));
            }
        });
        if (!accepted) LogFinalization(FinalizeDetached(record, document, diagnostics, database, limit));
    }

    private static FinalizationResult FinalizeDetached(
        MatchRecord record,
        ReplayDocumentV11 document,
        IReadOnlyCollection<string> diagnostics,
        MatchRecordDatabase database,
        int limit)
    {
        try
        {
            var validation = ReplayDocumentFinalizerV11.FinalizeAndValidate(document);
            record.ContentDependencies = document.Content.Dependencies.Select(item => item.OwnerModId).ToList();
            record.ContentSha256 = document.Header.DocumentSha256;
            record.EventCount = document.Events.Count;
            var analysis = MatchAnalysisBuilder.BuildV11(record, document);
            var canStoreReplay = validation.IsValid && diagnostics.Count == 0;
            if (!canStoreReplay)
            {
                record.CaptureDiagnostics = diagnostics.Concat(validation.Errors).Distinct(StringComparer.Ordinal).ToList();
                var storedSummary = database.SaveSummaryV11(record, analysis);
                return new FinalizationResult
                {
                    Stored = storedSummary,
                    RecordId = record.RecordId,
                    ReplayReady = false,
                    Message = storedSummary
                        ? "对局摘要已保存；v11 回放验证失败：" + string.Join("; ", record.CaptureDiagnostics)
                        : "v11 回放和摘要均未保存。"
                };
            }

            var stored = database.SaveV11(
                record,
                document,
                analysis,
                AuraToolsConfigService.MatchExperience.MatchRecords.Replay.ChunkTargetBytes);
            var removed = stored ? database.EnforceAutoLimit(limit) : 0;
            return new FinalizationResult
            {
                Stored = stored,
                RecordId = record.RecordId,
                ReplayReady = stored,
                Removed = removed,
                Message = stored ? "Replay Document v11 已验证并保存。" : "记录 ID 已存在，v11 回放未重复保存。"
            };
        }
        catch (Exception ex)
        {
            try
            {
                record.CaptureDiagnostics = diagnostics.Concat(new[] { "v11 finalization: " + ex.Message })
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var analysis = MatchAnalysisBuilder.BuildV11(record, document);
                var stored = database.SaveSummaryV11(record, analysis);
                return new FinalizationResult
                {
                    Stored = stored,
                    RecordId = record.RecordId,
                    Message = stored ? "仅保存了对局摘要：" + ex.Message : ex.Message
                };
            }
            catch (Exception fallback)
            {
                return new FinalizationResult
                {
                    RecordId = record.RecordId,
                    Message = ex.Message + "; summary fallback failed: " + fallback.Message
                };
            }
        }
    }

    private static void LogFinalization(FinalizationResult result)
    {
        if (result.Stored)
        {
            AuraToolsLog.Info("[MatchRecords] " + result.Message
                              + " record=" + result.RecordId
                              + ", ready=" + result.ReplayReady
                              + (result.Removed > 0 ? ", retention-removed=" + result.Removed : "") + ".");
        }
        else
        {
            AuraToolsLog.Warn("[MatchRecords] " + result.Message + " record=" + result.RecordId + ".");
        }
    }

    private static long ElapsedTicks()
    {
        if (startedTimestamp == 0) return 0;
        var elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
        return Math.Max(0L, (long)(elapsed * (double)ReplayProtocolV11.TimebaseTicksPerSecond / Stopwatch.Frequency));
    }

    private static void ResetNoLock()
    {
        AudioArbiterRuntime.ResolvedPlayback -= OnResolvedPlayback;
        AudioCapture.Drained -= OnAudioCapturesDrained;
        AudioCapture.Cancel();
        activeRecord = null;
        activeDocument = null;
        catalog = null;
        lastState = null;
        activeAction = null;
        startedTimestamp = 0;
        nextSequence = 0;
        nextActionIndex = 0;
        turnIndex = 1;
        firstPlayerRoundSeen = false;
        lastAudioCueKey = "";
        lastAudioStartSample = long.MinValue;
        lastBgmResourceId = "";
        activeBgmCue = null;
        completionPrepared = false;
        pendingCompletionResult = "";
        Diagnostics.Clear();
    }

    private static T? Argument<T>(object[]? arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Length && arguments[index] is T value
            ? value
            : default;
    }

    private static void CaptureRuntimeContext(out string mapMode, out int mapLevel, out string diceJson)
    {
        mapMode = "";
        mapLevel = 0;
        diceJson = "";
        try
        {
            var map = MapManager.Instance;
            var mode = map?.ModeMapManager;
            mapMode = map?.CurrentMode?.Trim() ?? "";
            mapLevel = Math.Max(0, mode?.Level ?? 0);
            if (mode?.NowDice != null) diceJson = AuraSharedJson.Serialize(mode.NowDice);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay native context capture failed: " + ex.Message);
        }
    }

    private static string DisplayValue(ReplayContentDefinitionV11? definition, string key, string fallback)
    {
        return definition?.Display?.Values?
                   .LastOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal))?.Value
               ?? fallback;
    }

    private static List<ReplayScopedSkinSelectionV11> CaptureSkinSelections(MatchReplayInitialState initialState)
    {
        var roles = new Dictionary<string, RoleTable>(StringComparer.Ordinal);
        try
        {
            var temporaryJson = initialState.TemporaryRoles == null || initialState.TemporaryRoles.Length == 0
                ? ""
                : GZip.DecompressToString(initialState.TemporaryRoles);
            var temporary = string.IsNullOrWhiteSpace(temporaryJson)
                ? null
                : AuraSharedJson.Deserialize<Dictionary<string, string>>(temporaryJson);
            foreach (var pair in temporary ?? new Dictionary<string, string>())
            {
                var role = AuraSharedJson.Deserialize<RoleTable>(pair.Value);
                if (role != null) roles[pair.Key] = role;
            }
        }
        catch
        {
        }
        if (RoleTable.Instance != null && !string.IsNullOrWhiteSpace(RoleTable.Instance.Id))
            roles[RoleTable.Instance.Id] = RoleTable.Instance;
        var result = new List<ReplayScopedSkinSelectionV11>();
        foreach (var pair in roles)
        {
            var careerId = pair.Value.Career?.data != null
                           && pair.Value.Career.data.TryGetValue("Id", out var id)
                ? id ?? ""
                : "";
            if (careerId.Length == 0) continue;
            var skinId = SkinRuntime.GetSelectedQualifiedSkinId(careerId, pair.Key);
            if (skinId.Length == 0) continue;
            result.Add(new ReplayScopedSkinSelectionV11
            {
                InstanceId = pair.Key,
                CareerId = careerId,
                QualifiedSkinId = skinId
            });
        }
        return result.OrderBy(value => value.InstanceId, StringComparer.Ordinal).ToList();
    }

    private sealed class ActiveAction
    {
        internal string ActionId { get; set; } = "";

        internal string StartedEventId { get; set; } = "";

        internal ReplayActionSourceV11 Source { get; set; } = new();

        internal ReplayLogicalStateV11 Before { get; set; } = new();

        internal int Depth { get; set; }

        internal bool IsRemote { get; set; }

        internal bool FinalizationScheduled { get; set; }

        internal MatchReplayActionPresentationState? NativePresentation { get; set; }

        internal long FinalizationGeneration { get; set; }

        internal List<ReplayAudioCueV11> Audio { get; } = new();
    }

    private sealed class CompletionSnapshot
    {
        public CompletionSnapshot(
            MatchRecord record,
            ReplayDocumentV11 document,
            IReadOnlyCollection<string> diagnostics)
        {
            Record = record;
            Document = document;
            Diagnostics = diagnostics;
        }

        public MatchRecord Record { get; }

        public ReplayDocumentV11 Document { get; }

        public IReadOnlyCollection<string> Diagnostics { get; }
    }

    private sealed class FinalizationResult
    {
        internal bool Stored { get; set; }

        internal bool ReplayReady { get; set; }

        internal string RecordId { get; set; } = "";

        internal int Removed { get; set; }

        internal string Message { get; set; } = "";
    }
}
