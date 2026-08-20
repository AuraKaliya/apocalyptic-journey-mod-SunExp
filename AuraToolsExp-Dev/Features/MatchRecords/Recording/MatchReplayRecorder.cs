using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraMode.Shared;
using AuraShared.Core;
using AudioArbiter.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Media;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Capture;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Witch.UI.Window;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayRecorder
{
    private static readonly object Gate = new();
    private static readonly List<string> Diagnostics = new();
    private static MatchRecord? activeRecord;
    private static ReplayDocumentV10? activeDocument;
    private static ReplayContentCatalogBuilderV10? catalog;
    private static ReplayLogicalStateV10? lastState;
    private static ActiveAction? activeAction;
    private static long startedTimestamp;
    private static long nextSequence;
    private static int nextActionIndex;
    private static int turnIndex = 1;
    private static bool firstPlayerRoundSeen;
    private static string lastAudioHash = "";
    private static long lastAudioStartSample = long.MinValue;
    private static string lastBgmHash = "";
    private static long lastBgmOffset = long.MinValue;
    private static ReplayAudioCueV10? activeBgmCue;

    internal static bool IsRecording
    {
        get
        {
            lock (Gate) return activeDocument != null;
        }
    }

    internal static void Start(object[]? arguments)
    {
        lock (Gate)
        {
            ResetNoLock();
            var recordId = Guid.NewGuid().ToString("N");
            var levelId = Argument<string>(arguments, 0) ?? FightManager.Instance?.level ?? "";
            activeRecord = new MatchRecord
            {
                RecordId = recordId,
                SessionId = recordId,
                AdventureId = Features.DamageMeter.Network.DamageMeterNetworkRuntime.CurrentAdventureId,
                LevelId = levelId,
                StartedUtc = DateTime.UtcNow.ToString("O"),
                Collection = MatchRecordCollections.Auto,
                ReplayProtocol = ReplayProtocolV10.DocumentVersion,
                GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
                ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
                ModFingerprint = CurrentRuntimeFingerprint()
            };
            activeDocument = new ReplayDocumentV10
            {
                Header = new ReplayDocumentHeaderV10
                {
                    RecordId = recordId,
                    AdventureId = activeRecord.AdventureId,
                    SessionId = recordId,
                    LevelId = levelId,
                    StartedUtc = activeRecord.StartedUtc,
                    GameBuild = activeRecord.GameBuild,
                    ToolBuild = activeRecord.ToolBuild,
                    RendererBuild = activeRecord.ToolBuild
                }
            };
            catalog = new ReplayContentCatalogBuilderV10();
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
            BeginActionNoLock(ReplayFactCaptureV10.CaptureActionSource(target, catalog!));
        }
    }

    internal static void EndCardAction(object? target)
    {
        lock (Gate)
        {
            if (activeAction == null) return;
            if (target != null)
            {
                var latest = ReplayFactCaptureV10.CaptureActionSource(target, catalog!);
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
            var intent = ReplayIntentCaptureV10.CaptureExecuting(target, arguments, catalog!);
            if (intent == null) return;
            var definition = catalog!.Manifest.Definitions.FirstOrDefault(item => item.Content.Key == intent.Content.Key);
            BeginActionNoLock(new ReplayActionSourceV10
            {
                ActorId = intent.ActorId,
                SourceInstanceId = intent.InstanceId,
                Content = intent.Content,
                Label = definition?.Display.Name ?? intent.Content.StableContentId,
                PresentationKind = ReplayPresentationKindsV10.EnemyIntent
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
            var stableId = ReplayFactCaptureV10.Read(config.data, "Id");
            var content = catalog!.Register("Card", config, stableId);
            BeginActionNoLock(new ReplayActionSourceV10
            {
                ActorId = context.ActorId ?? "",
                SourceInstanceId = config is DataConfig concrete
                    ? concrete.InstanceID ?? ""
                    : ReplayFactCaptureV10.Read(config.Vars, "InstanceID"),
                Content = content,
                Label = ReplayFactCaptureV10.First(
                    ReplayFactCaptureV10.Read(config.Vars, "Name"),
                    ReplayFactCaptureV10.Read(config.data, "Name"),
                    stableId),
                PresentationKind = string.Equals(
                    ReplayFactCaptureV10.Read(config.data, "Action"),
                    "Skill",
                    StringComparison.OrdinalIgnoreCase)
                    ? ReplayPresentationKindsV10.Skill
                    : ReplayPresentationKindsV10.Card
            }, remote: true);
            EndActionNoLock();
        }
    }

    internal static void ObserveAuthoritativeStatus(AuraAuthoritativeStatusContext context)
    {
        lock (Gate)
        {
            if (activeAction == null || !activeAction.FinalizationScheduled) return;
            activeAction.Convergence.Reset();
            ScheduleFinalizationNoLock(activeAction.ActionId);
        }
    }

    internal static void CaptureActionPresentation(object[]? arguments)
    {
        // v10 derives its tool-owned presentation cues from authoritative state facts.
    }

    internal static void CaptureNativeAudio(object[]? arguments, string bus)
    {
        var clip = arguments?.OfType<AudioClip>().FirstOrDefault();
        if (clip == null)
        {
            var path = arguments?.OfType<string>().LastOrDefault();
            if (!string.IsNullOrWhiteSpace(path))
            {
                var configuredPath = path ?? "";
                var resolved = string.Equals(bus, "Effect", StringComparison.OrdinalIgnoreCase)
                               && !configuredPath.StartsWith("Sounds/", StringComparison.Ordinal)
                    ? "Sounds/" + configuredPath
                    : configuredPath;
                try { clip = ResourceLoader.Load<AudioClip>(resolved); } catch { }
            }
        }
        if (clip == null) return;
        lock (Gate)
        {
            var manager = AudioManager.Instance;
            RecordAudioClipNoLock(
                clip,
                string.Equals(bus, "Vocal", StringComparison.OrdinalIgnoreCase)
                    ? manager?.NarrationVolume ?? 1f
                    : manager?.EffectVolume ?? 1f,
                bus,
                "Witch",
                clip.name ?? "",
                bus);
        }
    }

    internal static void CaptureCurrentBgm()
    {
        lock (Gate)
        {
            if (activeDocument != null) CaptureCurrentBgmNoLock();
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
            var after = ReplayFactCaptureV10.CaptureState(turnIndex, catalog!);
            AddEventNoLock(new ReplayTimelineEventV10
            {
                TimeTicks = ElapsedTicks(),
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV10.TurnChanged,
                ActorId = after.ActiveActorId,
                Delta = ReplayProjectionStateV10.CreateDelta(lastState!, after)
            });
            lastState = after;
        }
    }

    internal static void Complete(string result)
    {
        MatchRecord? record;
        ReplayDocumentV10? document;
        List<string> diagnostics;
        lock (Gate)
        {
            if (activeDocument == null || activeRecord == null) return;
            EnsureBaselineNoLock();
            FlushActionNoLock();
            if (lastState != null && catalog != null)
            {
                var finalState = ReplayFactCaptureV10.CaptureState(turnIndex, catalog);
                if (!string.Equals(
                        ReplayProjectionStateV10.Hash(lastState),
                        ReplayProjectionStateV10.Hash(finalState),
                        StringComparison.Ordinal))
                {
                    AddEventNoLock(new ReplayTimelineEventV10
                    {
                        TimeTicks = ElapsedTicks(),
                        TurnIndex = turnIndex,
                        EventType = ReplayEventTypesV10.StateChanged,
                        ActorId = finalState.ActiveActorId,
                        Delta = ReplayProjectionStateV10.CreateDelta(lastState, finalState)
                    });
                    lastState = finalState;
                }
            }

            AddEventNoLock(new ReplayTimelineEventV10
            {
                TimeTicks = ElapsedTicks(),
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV10.BattleCompleted,
                ActorId = lastState?.ActiveActorId ?? ""
            });
            record = activeRecord;
            document = activeDocument;
            document.Content = catalog?.Manifest ?? new ReplayContentManifestV10();
            document.Attachments = catalog?.Attachments ?? new List<ReplayAttachmentV10>();
            record.Result = string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim();
            record.EndedUtc = DateTime.UtcNow.ToString("O");
            record.TurnCount = Math.Max(1, turnIndex);
            record.EventCount = document.Events.Count;
            record.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
            record.CaptureDiagnostics = new List<string>(Diagnostics);
            document.Header.Result = record.Result;
            document.Header.EndedUtc = record.EndedUtc;
            document.Header.LevelId = record.LevelId;
            diagnostics = new List<string>(Diagnostics);
            ResetNoLock();
        }

        QueueFinalization(record, document, diagnostics);
    }

    internal static void Abort()
    {
        lock (Gate) ResetNoLock();
    }

    internal static string CurrentRuntimeFingerprint()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(item => !item.IsDynamic)
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
        lastState = ReplayFactCaptureV10.CaptureState(turnIndex, catalog);
        activeDocument.InitialState = ReplayProjectionStateV10.Clone(lastState);
        catalog.RegisterBackground(
            activeRecord.LevelId,
            GameApp.Instance?.NowBackground?.name ?? activeRecord.LevelId,
            GameApp.Instance?.NowBackground);
        CaptureCurrentBgmNoLock();
        return true;
    }

    private static void CaptureCurrentBgmNoLock()
    {
        var source = AudioManager.Instance?.bgmSource;
        var clip = source?.clip;
        if (source == null || clip == null || catalog == null || activeDocument == null) return;
        try
        {
            var hash = catalog.CaptureAudioClip(clip, "BattleBgm");
            if (string.IsNullOrWhiteSpace(hash))
            {
                AddDiagnosticNoLock("battle BGM could not be captured: " + clip.name);
                return;
            }
            var offset = Math.Max(0L, (long)Math.Round(source.time * clip.frequency));
            if (string.Equals(hash, lastBgmHash, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(offset - lastBgmOffset) <= Math.Max(1, clip.frequency / 2))
            {
                return;
            }
            lastBgmHash = hash;
            lastBgmOffset = offset;
            var timelineTicks = nextSequence == 0 ? 0 : ElapsedTicks();
            var startSample = timelineTicks * ReplayOfflineAudioMixer.SampleRate / ReplayProtocolV10.TimebaseTicksPerSecond;
            if (activeBgmCue != null)
            {
                activeBgmCue.DurationSamples = Math.Max(0, startSample - activeBgmCue.StartSample);
                activeBgmCue.FadeOutSamples = Math.Min(ReplayOfflineAudioMixer.SampleRate / 4, activeBgmCue.DurationSamples);
            }
            var cue = new ReplayAudioCueV10
            {
                AssetSha256 = hash,
                OwnerModId = "Witch",
                ProviderId = clip.name ?? "",
                Kind = "BattleBgm",
                StartSample = startSample,
                SourceOffsetSample = offset,
                DurationSamples = source.loop
                    ? 0
                    : (long)Math.Ceiling(
                        Math.Max(0, clip.samples - offset) / (double)clip.frequency
                        * ReplayOfflineAudioMixer.SampleRate),
                GainQ16 = (int)Math.Round(Math.Max(0f, source.volume) * 65_536f),
                LoopStartSample = 0,
                LoopEndSample = source.loop ? clip.samples : 0,
                FadeInSamples = ReplayOfflineAudioMixer.SampleRate / 8,
                Bus = "Bgm"
            };
            activeBgmCue = cue;
            AddEventNoLock(new ReplayTimelineEventV10
            {
                TimeTicks = timelineTicks,
                TurnIndex = turnIndex,
                EventType = ReplayEventTypesV10.StateChanged,
                ActorId = lastState?.ActiveActorId ?? "",
                Audio = new List<ReplayAudioCueV10> { cue }
            });
        }
        catch (Exception ex)
        {
            AddDiagnosticNoLock("battle BGM capture failed: " + ex.Message);
        }
    }

    private static void BeginActionNoLock(ReplayActionSourceV10 source, bool remote = false)
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
            Before = ReplayProjectionStateV10.Clone(lastState),
            Depth = 1,
            IsRemote = remote
        };
        AddEventNoLock(ReplayActionDerivationV10.Started(
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
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV10.Action.Finalize." + actionId,
            Source = "MatchRecords.ReplayV10.Capture",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 25,
            EstimatedCost = 1,
            Action = () => FinalizeAction(actionId)
        });
    }

    private static void FinalizeAction(string actionId)
    {
        lock (Gate)
        {
            if (activeAction == null
                || catalog == null
                || !string.Equals(activeAction.ActionId, actionId, StringComparison.Ordinal))
            {
                return;
            }

            if (activeAction.Depth > 0)
            {
                ScheduleFinalizationNoLock(actionId);
                return;
            }

            var revision = ReplayFactCaptureV10.RevisionHash(turnIndex, catalog);
            var decision = activeAction.Convergence.Observe(revision);
            if (decision == MatchReplayActionFinalizationDecision.Observe)
            {
                ScheduleFinalizationNoLock(actionId);
                return;
            }

            if (decision == MatchReplayActionFinalizationDecision.FinalizeDeadline)
            {
                AddDiagnosticNoLock("action projection did not converge: " + actionId);
            }

            FlushActionNoLock(ReplayFactCaptureV10.CaptureState(turnIndex, catalog));
        }
    }

    private static void FlushActionNoLock(ReplayLogicalStateV10? settled = null)
    {
        if (activeAction == null || activeDocument == null || catalog == null) return;
        var after = settled ?? ReplayFactCaptureV10.CaptureState(turnIndex, catalog);
        var completed = ReplayActionDerivationV10.Completed(
            nextSequence + 1,
            ElapsedTicks(),
            turnIndex,
            activeAction.ActionId,
            activeAction.Source,
            activeAction.Before,
            after,
            activeAction.StartedEventId);
        completed.Audio.AddRange(activeAction.Audio);
        AddEventNoLock(completed);
        lastState = after;
        activeAction = null;
    }

    private static void AddEventNoLock(ReplayTimelineEventV10 value)
    {
        if (activeDocument == null) return;
        value.Sequence = ++nextSequence;
        value.EventId = "event-" + value.Sequence.ToString("D8");
        value.TimeTicks = Math.Max(0, value.TimeTicks);
        value.TurnIndex = Math.Max(1, value.TurnIndex);
        activeDocument.Events.Add(value);
        if (value.Sequence % ReplayProtocolV10.DefaultCheckpointInterval == 0
            || string.Equals(value.EventType, ReplayEventTypesV10.TurnChanged, StringComparison.Ordinal))
        {
            activeDocument.Checkpoints.Add(new ReplayCheckpointV10 { EventSequence = value.Sequence });
        }
    }

    private static void QueueFinalization(
        MatchRecord record,
        ReplayDocumentV10 document,
        IReadOnlyCollection<string> diagnostics)
    {
        var database = MatchRecordStorage.Database;
        var limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<FinalizationResult>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV10.Finalize." + record.RecordId,
            Source = "MatchRecords.ReplayV10.Finalize",
            Kind = AuraSharedBackgroundWorkKind.Io,
            Work = _ => FinalizeDetached(record, document, diagnostics, database, limit),
            ApplyOnMainThread = LogFinalization,
            OnFailedOnMainThread = ex =>
            {
                AuraToolsLog.Warn("[MatchRecords] v10 background finalization failed: " + ex.Message);
                LogFinalization(FinalizeDetached(record, document, diagnostics, database, limit));
            }
        });
        if (!accepted) LogFinalization(FinalizeDetached(record, document, diagnostics, database, limit));
    }

    private static FinalizationResult FinalizeDetached(
        MatchRecord record,
        ReplayDocumentV10 document,
        IReadOnlyCollection<string> diagnostics,
        MatchRecordDatabase database,
        int limit)
    {
        try
        {
            var validation = ReplayDocumentFinalizerV10.FinalizeAndValidate(document);
            record.ContentDependencies = document.Content.Dependencies.Select(item => item.OwnerModId).ToList();
            record.ContentSha256 = document.Header.DocumentSha256;
            record.EventCount = document.Events.Count;
            var analysis = MatchAnalysisBuilder.BuildV10(record, document);
            var canStoreReplay = validation.IsValid && diagnostics.Count == 0;
            if (!canStoreReplay)
            {
                record.CaptureDiagnostics = diagnostics.Concat(validation.Errors).Distinct(StringComparer.Ordinal).ToList();
                var storedSummary = database.SaveSummaryV10(record, analysis);
                return new FinalizationResult
                {
                    Stored = storedSummary,
                    RecordId = record.RecordId,
                    ReplayReady = false,
                    Message = storedSummary
                        ? "对局摘要已保存；v10 回放验证失败：" + string.Join("; ", record.CaptureDiagnostics)
                        : "v10 回放和摘要均未保存。"
                };
            }

            var stored = database.SaveV10(
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
                Message = stored ? "Replay Document v10 已验证并保存。" : "记录 ID 已存在，v10 回放未重复保存。"
            };
        }
        catch (Exception ex)
        {
            try
            {
                record.CaptureDiagnostics = diagnostics.Concat(new[] { "v10 finalization: " + ex.Message })
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var analysis = MatchAnalysisBuilder.BuildV10(record, document);
                var stored = database.SaveSummaryV10(record, analysis);
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
        return Math.Max(0L, (long)(elapsed * (double)ReplayProtocolV10.TimebaseTicksPerSecond / Stopwatch.Frequency));
    }

    private static void AddDiagnosticNoLock(string message)
    {
        var value = (message ?? "").Trim();
        if (value.Length > 0 && Diagnostics.Count < 32 && !Diagnostics.Contains(value, StringComparer.Ordinal))
        {
            Diagnostics.Add(value);
        }
    }

    private static void OnResolvedPlayback(ResolvedSoundPlayback playback)
    {
        lock (Gate)
        {
            if (activeDocument == null || catalog == null || playback?.Clip is not AudioClip clip) return;
            RecordAudioClipNoLock(
                clip,
                playback.VolumeMultiplier,
                playback.Bus ?? "Effect",
                playback.OwnerModId ?? "",
                playback.ProviderId ?? "",
                playback.Request?.Kind ?? "ResolvedAudio");
        }
    }

    private static void RecordAudioClipNoLock(
        AudioClip clip,
        float volume,
        string bus,
        string ownerModId,
        string providerId,
        string kind)
    {
        if (activeDocument == null || catalog == null || clip == null) return;
        try
        {
            EnsureBaselineNoLock();
            var hash = catalog.CaptureAudioClip(clip, "Audio." + kind);
            if (string.IsNullOrWhiteSpace(hash))
            {
                AddDiagnosticNoLock("audio could not be captured: " + clip.name);
                return;
            }
            var startSample = ElapsedTicks() * ReplayOfflineAudioMixer.SampleRate / ReplayProtocolV10.TimebaseTicksPerSecond;
            if (string.Equals(hash, lastAudioHash, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(startSample - lastAudioStartSample) <= ReplayOfflineAudioMixer.SampleRate / 20)
            {
                return;
            }
            lastAudioHash = hash;
            lastAudioStartSample = startSample;
            var cue = new ReplayAudioCueV10
            {
                AssetSha256 = hash,
                OwnerModId = ownerModId ?? "",
                ProviderId = providerId ?? "",
                Kind = kind ?? "",
                StartSample = startSample,
                DurationSamples = (long)Math.Ceiling(
                    clip.samples / (double)clip.frequency * ReplayOfflineAudioMixer.SampleRate),
                GainQ16 = (int)Math.Round(Math.Max(0f, volume) * 65_536f),
                Bus = bus ?? "Effect"
            };
            if (activeAction != null)
            {
                activeAction.Audio.Add(cue);
            }
            else
            {
                AddEventNoLock(new ReplayTimelineEventV10
                {
                    TimeTicks = ElapsedTicks(),
                    TurnIndex = turnIndex,
                    EventType = ReplayEventTypesV10.StateChanged,
                    ActorId = lastState?.ActiveActorId ?? "",
                    Audio = new List<ReplayAudioCueV10> { cue }
                });
            }
        }
        catch (Exception ex)
        {
            AddDiagnosticNoLock("audio capture failed: " + ex.Message);
        }
    }

    private static void ResetNoLock()
    {
        AudioArbiterRuntime.ResolvedPlayback -= OnResolvedPlayback;
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
        lastAudioHash = "";
        lastAudioStartSample = long.MinValue;
        lastBgmHash = "";
        lastBgmOffset = long.MinValue;
        activeBgmCue = null;
        Diagnostics.Clear();
    }

    private static T? Argument<T>(object[]? arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Length && arguments[index] is T value
            ? value
            : default;
    }

    private sealed class ActiveAction
    {
        internal string ActionId { get; set; } = "";

        internal string StartedEventId { get; set; } = "";

        internal ReplayActionSourceV10 Source { get; set; } = new();

        internal ReplayLogicalStateV10 Before { get; set; } = new();

        internal int Depth { get; set; }

        internal bool IsRemote { get; set; }

        internal bool FinalizationScheduled { get; set; }

        internal MatchReplayActionConvergenceTracker Convergence { get; } = new();

        internal List<ReplayAudioCueV10> Audio { get; } = new();
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
