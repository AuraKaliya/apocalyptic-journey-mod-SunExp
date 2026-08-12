using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using Witch;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayRecorder
{
    private static readonly object Gate = new();
    private static MatchRecord? activeRecord;
    private static MatchReplayWorkingBuffer? workingBuffer;
    private static ActiveAction? activeAction;
    private static MatchReplayStateSnapshot? lastAuthoritativeState;
    private static long startedTimestamp;
    private static long nextSequence;
    private static int turnIndex;
    private static int nextActionIndex;
    private static int completedActionCount;
    private static int actionsSinceCheckpoint;
    private static bool firstPlayerRoundSeen;
    private static readonly Dictionary<string, int> EventKindCounts = new(StringComparer.Ordinal);
    private static readonly List<string> CaptureDiagnostics = new();

    internal static bool IsRecording
    {
        get
        {
            lock (Gate)
            {
                return activeRecord != null;
            }
        }
    }

    internal static void Start(object[]? arguments)
    {
        if (!AuraToolsMatchRecordsRuntime.ReplayEnabled || MatchReplaySessionState.IsPlayback)
        {
            return;
        }

        var levelId = Argument<string>(arguments, 0) ?? "";
        var roleQueue = Argument<byte[]>(arguments, 1) ?? Array.Empty<byte>();
        var temporaryRoles = Argument<byte[]>(arguments, 2) ?? Array.Empty<byte>();
        var enemyPositive = Argument<float>(arguments, 3);
        var enemyHp = Argument<float>(arguments, 4);
        if (IsRecording)
        {
            Abort();
        }

        CaptureRuntimeContext(out var mapMode, out var mapLevel, out var diceJson);
        var requiredCapabilities = new List<string>
        {
            MatchReplayCapabilities.AuthoritativeFramesV1,
            MatchReplayCapabilities.StateProjectionV1,
            MatchReplayCapabilities.PresentationTimelineV1,
            MatchReplayCapabilities.IndexedSeekV1,
            MatchReplayCapabilities.AsyncFinalizationV1,
            MatchReplayCapabilities.CardPresentationReadyV1,
            MatchReplayCapabilities.IncrementalHandV1,
            MatchReplayCapabilities.OutcomeCuesV1,
            MatchReplayCapabilities.PassiveHudV1
        };
        if (!string.IsNullOrWhiteSpace(diceJson))
        {
            requiredCapabilities.Add(MatchReplayCapabilities.RuntimeContextV1);
        }

        lock (Gate)
        {
            var recordId = Guid.NewGuid().ToString("N");
            activeRecord = new MatchRecord
            {
                RecordId = recordId,
                SessionId = recordId,
                AdventureId = DamageMeterNetworkRuntime.CurrentAdventureId,
                LevelId = levelId,
                StartedUtc = DateTime.UtcNow.ToString("O"),
                Collection = MatchRecordCollections.Auto,
                ReplayProtocol = MatchReplayProtocol.Version,
                GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
                ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
                ModFingerprint = CurrentRuntimeFingerprint(),
                RequiredCapabilities = requiredCapabilities,
                OptionalCapabilities = new List<string> { MatchReplayCapabilities.CausalityV1 },
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
            var settings = AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay;
            workingBuffer = new MatchReplayWorkingBuffer(
                settings.ChunkTargetBytes,
                settings.WorkingMemoryBudgetMb * 1024L * 1024L,
                Path.Combine(MatchRecordStorage.TemporaryDirectory, "recording-" + recordId),
                deferCompression: true);
            startedTimestamp = Stopwatch.GetTimestamp();
            nextSequence = 0;
            turnIndex = 1;
            nextActionIndex = 0;
            completedActionCount = 0;
            actionsSinceCheckpoint = 0;
            firstPlayerRoundSeen = false;
            activeAction = null;
            lastAuthoritativeState = null;
            EventKindCounts.Clear();
            CaptureDiagnostics.Clear();
        }
    }

    internal static void StartFromCurrentFight()
    {
        if (IsRecording || FightManager.Instance == null)
        {
            return;
        }

        var manager = FightManager.Instance;
        var temporaryRoles = manager.TempRoleList == null || manager.TempRoleList.Count == 0
            ? ""
            : JsonConvert.SerializeObject(manager.TempRoleList);
        Start(new object[]
        {
            manager.level ?? "",
            GZip.CompressString(JsonConvert.SerializeObject(manager.roleQueue)),
            GZip.CompressString(temporaryRoles),
            manager.SumOfEnemyPositive,
            manager.EnemyHp
        });
    }

    internal static void BeginCardAction(object? target)
    {
        BeginAction(target, target is SkillItem ? "SkillUse" : "CardUse");
    }

    internal static void EndCardAction(object? target)
    {
        if (target != null)
        {
            try
            {
                var finalPresentation = MatchReplayCardStateCapture.CaptureOne(ResolveConfig(target));
                lock (Gate)
                {
                    if (activeAction != null && finalPresentation != null)
                    {
                        activeAction.SourcePresentation = finalPresentation;
                    }
                }
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[MatchRecords] final card presentation capture degraded: " + ex.Message);
            }
        }

        EndAction();
    }

    private static void BeginAction(object? target, string kind)
    {
        if (target == null || MatchReplaySessionState.IsPlayback)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                if (activeRecord == null || workingBuffer == null)
                {
                    return;
                }

                var config = ResolveConfig(target);
                var sourceId = Value(config?.data, "Id");
                var sourceInstanceId = config is DataConfig dataConfig
                    ? dataConfig.InstanceID ?? ""
                    : Value(config?.Vars, "InstanceID");
                if (activeAction != null
                    && MatchReplayActionBoundaryPolicy.ShouldNest(activeAction.FinalizationScheduled))
                {
                    activeAction.Depth++;
                    return;
                }

                // Any begin after the previous outer end is a new user-visible action. The
                // runtime has already accepted the new use, so the previous state is final at
                // this exact boundary even if its deferred callback has not run yet.
                if (activeAction != null)
                {
                    FlushPendingActionNoLock();
                }

                activeAction = new ActiveAction
                {
                    ActionId = "action-" + (++nextActionIndex).ToString("D6"),
                    ActionIndex = nextActionIndex,
                    TurnIndex = Math.Max(1, turnIndex),
                    StartedMilliseconds = ElapsedMilliseconds(),
                    Kind = kind,
                    ActorId = ResolveActorId(target),
                    SourceId = sourceId,
                    SourceInstanceId = sourceInstanceId,
                    Label = First(Value(config?.data, "Name"), Value(config?.data, "DisplayName"), Value(config?.data, "Id")),
                    SourcePresentation = MatchReplayCardStateCapture.CaptureOne(config),
                    Before = lastAuthoritativeState == null
                        ? MatchReplayStateCapture.CaptureProjectionSnapshot(turnIndex)
                        : MatchReplayProjectionState.Clone(lastAuthoritativeState),
                    Depth = 1
                };
            }
        }
        catch (Exception ex)
        {
            RecordCaptureDiagnostic("action begin", ex);
        }
    }

    private static void EndAction()
    {
        if (MatchReplaySessionState.IsPlayback)
        {
            return;
        }

        string actionId;
        lock (Gate)
        {
            if (activeAction == null)
            {
                return;
            }

            activeAction.Depth = Math.Max(0, activeAction.Depth - 1);
            if (activeAction.Depth > 0 || activeAction.FinalizationScheduled)
            {
                return;
            }

            activeAction.FinalizationScheduled = true;
            actionId = activeAction.ActionId;
        }

        ScheduleFinalization(actionId);
    }

    private static void ScheduleFinalization(string actionId)
    {
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "MatchReplay.Action.Finalize." + actionId,
            Source = "MatchRecords.Replay.ActionFinalize",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 25,
            EstimatedCost = 1,
            Action = () => FinalizeAction(actionId)
        });
    }

    private static void FinalizeAction(string actionId)
    {
        try
        {
            lock (Gate)
            {
                if (activeAction == null || !string.Equals(activeAction.ActionId, actionId, StringComparison.Ordinal))
                {
                    return;
                }

                if (activeAction.Depth > 0)
                {
                    ScheduleFinalization(actionId);
                    return;
                }

                var snapshot = MatchReplayStateCapture.CaptureProjectionSnapshot(activeAction.TurnIndex);
                var stateHash = MatchReplayProjectionState.Hash(snapshot);
                var decision = activeAction.Convergence.Observe(stateHash);
                if (decision == MatchReplayActionFinalizationDecision.Observe)
                {
                    ScheduleFinalization(actionId);
                    return;
                }

                if (decision == MatchReplayActionFinalizationDecision.FinalizeDeadline)
                {
                    AddCaptureDiagnosticNoLock("action projection did not converge: " + actionId);
                    AuraToolsLog.Warn("[MatchRecords] replay action projection reached its observation deadline; "
                                      + "the match will be stored as analysis-only: " + actionId + ".");
                }

                FlushPendingActionNoLock(snapshot);
            }
        }
        catch (Exception ex)
        {
            RecordCaptureDiagnostic("action finalize", ex);
        }
    }

    internal static void CaptureCheckpointIfDue()
    {
        // Action frames already contain authoritative deltas. Periodic full seek checkpoints
        // are emitted when an action is finalized, never from every command hook.
    }

    internal static void StartTurn()
    {
        try
        {
            lock (Gate)
            {
                if (activeRecord == null || workingBuffer == null)
                {
                    return;
                }

                FlushPendingActionNoLock();
                if (firstPlayerRoundSeen)
                {
                    turnIndex++;
                }
                else
                {
                    firstPlayerRoundSeen = true;
                }

                var snapshot = MatchReplayStateCapture.CaptureProjectionSnapshot(turnIndex);
                lastAuthoritativeState = MatchReplayProjectionState.Clone(snapshot);
                if (activeRecord.InitialState.BaselineState == null)
                {
                    var baseline = MatchReplayProjectionState.Clone(snapshot);
                    baseline.RoleTableJson = activeRecord.InitialState.RoleTableJson;
                    activeRecord.InitialState.BaselineState = baseline;
                }

                AddTurnFrameNoLock(snapshot);
            }
        }
        catch (Exception ex)
        {
            RecordCaptureDiagnostic("turn frame", ex);
        }
    }

    internal static void Complete(string result)
    {
        MatchRecord? record;
        MatchReplayWorkingBuffer? buffer;
        Dictionary<string, int> eventKindCounts;
        int completedTurns;
        lock (Gate)
        {
            if (activeRecord == null || workingBuffer == null || MatchReplaySessionState.IsPlayback)
            {
                return;
            }

            try
            {
                FlushPendingActionNoLock();
            }
            catch (Exception ex)
            {
                AddCaptureDiagnosticNoLock("final action: " + ex.Message);
            }
            record = activeRecord;
            buffer = workingBuffer;
            completedTurns = Math.Max(1, turnIndex);
            if (record.InitialState.BaselineState == null)
            {
                AddCaptureDiagnosticNoLock("missing baseline state");
            }
            record.CaptureDiagnostics = new List<string>(CaptureDiagnostics);
            record.Result = string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim();
            record.EndedUtc = DateTime.UtcNow.ToString("O");
            record.EventCount = buffer.EventCount;
            record.TurnCount = completedTurns;
            record.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
            eventKindCounts = new Dictionary<string, int>(EventKindCounts, StringComparer.Ordinal);
            activeRecord = null;
            workingBuffer = null;
            activeAction = null;
            lastAuthoritativeState = null;
            EventKindCounts.Clear();
            CaptureDiagnostics.Clear();
        }

        var autoRecordLimit = AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience
            .MatchRecords.Replay.AutoRecordLimit;
        QueueFinalization(
            record,
            buffer,
            eventKindCounts,
            MatchRecordStorage.Database,
            autoRecordLimit);
    }

    internal static void Abort()
    {
        lock (Gate)
        {
            activeRecord = null;
            workingBuffer?.Dispose();
            workingBuffer = null;
            activeAction = null;
            lastAuthoritativeState = null;
            nextActionIndex = 0;
            completedActionCount = 0;
            actionsSinceCheckpoint = 0;
            firstPlayerRoundSeen = false;
            EventKindCounts.Clear();
            CaptureDiagnostics.Clear();
        }
    }

    internal static string CurrentRuntimeFingerprint()
    {
        var values = new[]
        {
            typeof(FightManager).Assembly.GetName().Name ?? "Witch",
            typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(FightManager).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
            typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(AuraToolsMatchRecordsRuntime).Assembly.ManifestModule.ModuleVersionId.ToString("N")
        };
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values)));
        return string.Concat(hash.Select(value => value.ToString("x2")));
    }

    private static void FlushPendingActionNoLock(MatchReplayStateSnapshot? settledState = null)
    {
        if (activeAction == null || activeRecord == null || workingBuffer == null)
        {
            return;
        }

        var action = activeAction;
        var after = settledState == null
            ? MatchReplayStateCapture.CaptureProjectionSnapshot(action.TurnIndex)
            : MatchReplayProjectionState.Clone(settledState);
        lastAuthoritativeState = MatchReplayProjectionState.Clone(after);
        var delta = MatchReplayProjectionState.CreateDelta(action.Before, after);
        var derived = MatchReplayActionDerivation.Build(
            action.ActionId,
            action.Kind,
            action.ActorId,
            action.SourceId,
            action.SourceInstanceId,
            action.Label,
            action.SourcePresentation,
            action.Before,
            after);
        var frame = new MatchReplayActionFrame
        {
            ActionId = action.ActionId,
            ActionIndex = action.ActionIndex,
            TurnIndex = action.TurnIndex,
            StartedMilliseconds = action.StartedMilliseconds,
            EndedMilliseconds = ElapsedMilliseconds(),
            DurationMilliseconds = derived.DurationMilliseconds,
            Kind = action.Kind,
            ActorId = action.ActorId,
            SourceId = action.SourceId,
            SourceInstanceId = action.SourceInstanceId,
            Label = action.Label,
            SourcePresentation = action.SourcePresentation,
            Delta = delta,
            CardTransitions = derived.CardTransitions,
            Presentation = derived.Presentation,
            Semantics = derived.Semantics,
            FinalStateHash = MatchReplayProjectionState.Hash(after)
        };
        var semantic = new MatchSemanticEvent
        {
            EventId = "event-" + (nextSequence + 1),
            ActionId = action.ActionId,
            RootActionId = action.ActionId,
            Category = MatchSemanticCategories.Card,
            Action = action.Kind,
            ActorId = action.ActorId,
            SourceId = action.SourceId,
            SourceInstanceId = action.SourceInstanceId,
            Label = action.Label,
            AttributionConfidence = MatchAttributionConfidence.Exact,
            IsKeyEvent = true
        };
        workingBuffer.Add(new MatchReplayEvent
        {
            Sequence = ++nextSequence,
            TurnIndex = action.TurnIndex,
            ElapsedMilliseconds = frame.EndedMilliseconds,
            Kind = MatchReplayEventKinds.ActionFrame,
            TypeName = typeof(MatchReplayActionFrame).FullName ?? nameof(MatchReplayActionFrame),
            Semantic = semantic,
            ActionFrame = frame
        });
        IncrementKindNoLock(MatchReplayEventKinds.ActionFrame);
        completedActionCount++;
        actionsSinceCheckpoint++;
        activeAction = null;

        var interval = AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.CheckpointEventInterval;
        if (actionsSinceCheckpoint >= Math.Max(1, interval))
        {
            AddSeekCheckpointNoLock(after);
        }
    }

    private static void AddTurnFrameNoLock(MatchReplayStateSnapshot snapshot)
    {
        if (workingBuffer == null)
        {
            return;
        }

        var frame = new MatchReplayTurnFrame
        {
            TurnIndex = Math.Max(1, turnIndex),
            ActiveActorId = FightPlayer.Instance?.Status?.InstanceId ?? "",
            State = MatchReplayProjectionState.Clone(snapshot),
            StateHash = MatchReplayProjectionState.Hash(snapshot)
        };
        workingBuffer.Add(new MatchReplayEvent
        {
            Sequence = ++nextSequence,
            TurnIndex = frame.TurnIndex,
            ElapsedMilliseconds = ElapsedMilliseconds(),
            Kind = MatchReplayEventKinds.TurnFrame,
            TypeName = typeof(MatchReplayTurnFrame).FullName ?? nameof(MatchReplayTurnFrame),
            TurnFrame = frame
        });
        IncrementKindNoLock(MatchReplayEventKinds.TurnFrame);
        AddSeekCheckpointNoLock(snapshot);
    }

    private static void AddSeekCheckpointNoLock(MatchReplayStateSnapshot snapshot)
    {
        if (workingBuffer == null)
        {
            return;
        }

        var checkpoint = new MatchReplaySeekCheckpoint
        {
            TurnIndex = Math.Max(1, turnIndex),
            CompletedActionCount = completedActionCount,
            State = MatchReplayProjectionState.Clone(snapshot),
            StateHash = MatchReplayProjectionState.Hash(snapshot)
        };
        workingBuffer.Add(new MatchReplayEvent
        {
            Sequence = ++nextSequence,
            TurnIndex = checkpoint.TurnIndex,
            ElapsedMilliseconds = ElapsedMilliseconds(),
            Kind = MatchReplayEventKinds.SeekCheckpoint,
            TypeName = typeof(MatchReplaySeekCheckpoint).FullName ?? nameof(MatchReplaySeekCheckpoint),
            SeekCheckpoint = checkpoint
        });
        IncrementKindNoLock(MatchReplayEventKinds.SeekCheckpoint);
        actionsSinceCheckpoint = 0;
    }

    private static void QueueFinalization(
        MatchRecord record,
        MatchReplayWorkingBuffer buffer,
        IReadOnlyDictionary<string, int> eventKindCounts,
        MatchRecordDatabase database,
        int autoRecordLimit)
    {
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<FinalizationResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "MatchReplay.Finalize." + record.RecordId,
                Source = "MatchRecords.Replay.Finalize",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => CompleteDetached(
                    record,
                    buffer,
                    eventKindCounts,
                    database,
                    autoRecordLimit),
                ApplyOnMainThread = LogFinalization,
                OnFailedOnMainThread = ex =>
                {
                    AuraToolsLog.Warn("[MatchRecords] replay background finalization failed; retrying locally: "
                                      + ex.Message);
                    LogFinalization(CompleteDetached(
                        record,
                        buffer,
                        eventKindCounts,
                        database,
                        autoRecordLimit));
                }
            });
        if (!accepted)
        {
            LogFinalization(CompleteDetached(
                record,
                buffer,
                eventKindCounts,
                database,
                autoRecordLimit));
        }
    }

    private static FinalizationResult CompleteDetached(
        MatchRecord record,
        MatchReplayWorkingBuffer buffer,
        IReadOnlyDictionary<string, int> eventKindCounts,
        MatchRecordDatabase database,
        int autoRecordLimit)
    {
        try
        {
            var quality = MatchReplayCaptureQuality.EvaluateRecording(
                eventKindCounts,
                record.InitialState.BaselineState != null,
                record.CaptureDiagnostics);
            record.ReplayState = quality.CanPlay ? MatchReplayStates.Ready : MatchReplayStates.Incomplete;
            var events = buffer.ReadEvents().ToList();
            var chunks = buffer.ReadChunks().ToList();
            var analysis = MatchAnalysisBuilder.Build(record, events);
            var stored = database.SaveStreaming(record, chunks, analysis);
            var removed = 0;
            var retentionMessage = "";
            if (stored)
            {
                try
                {
                    removed = database.EnforceAutoLimit(autoRecordLimit);
                }
                catch (Exception ex)
                {
                    retentionMessage = "自动清理失败，但本场记录已经保存：" + ex.Message;
                }
            }

            return new FinalizationResult
            {
                Stored = stored,
                RecordId = record.RecordId,
                EventCount = record.EventCount,
                ChunkCount = chunks.Count,
                SemanticCount = events.Sum(item => item.ActionFrame?.Semantics?.Count ?? 0),
                CardTransitionCount = events.Sum(item => item.ActionFrame?.CardTransitions?.Count ?? 0),
                ReplayState = record.ReplayState,
                Kinds = quality.DescribeCounts(),
                Removed = removed,
                Message = FirstNonEmpty(quality.CanPlay ? "" : quality.Message, retentionMessage)
            };
        }
        catch (Exception ex)
        {
            try
            {
                if (database.Get(record.RecordId) != null)
                {
                    return new FinalizationResult
                    {
                        Stored = true,
                        RecordId = record.RecordId,
                        EventCount = record.EventCount,
                        ReplayState = record.ReplayState,
                        Kinds = MatchReplayCaptureQuality.EvaluateCounts(eventKindCounts).DescribeCounts(),
                        Message = "对局已经保存，但后台保留策略报告异常：" + ex.Message
                    };
                }
            }
            catch
            {
            }

            return StoreAnalysisOnlyFallback(record, database, autoRecordLimit, ex);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private static FinalizationResult StoreAnalysisOnlyFallback(
        MatchRecord record,
        MatchRecordDatabase database,
        int autoRecordLimit,
        Exception replayFailure)
    {
        try
        {
            var diagnostic = "replay finalization: " + replayFailure.Message;
            record.CaptureDiagnostics ??= new List<string>();
            if (!record.CaptureDiagnostics.Contains(diagnostic, StringComparer.Ordinal))
            {
                record.CaptureDiagnostics.Add(diagnostic);
            }

            record.ReplayState = MatchReplayStates.Incomplete;
            record.EventCount = 0;
            record.CompressedBytes = 0;
            record.ContentSha256 = "";
            var events = Array.Empty<MatchReplayEvent>();
            var analysis = MatchAnalysisBuilder.Build(record, events);
            var stored = database.SaveStreaming(record, Array.Empty<MatchReplayChunk>(), analysis);
            var removed = 0;
            if (stored)
            {
                try
                {
                    removed = database.EnforceAutoLimit(autoRecordLimit);
                }
                catch
                {
                }
            }

            return new FinalizationResult
            {
                Stored = stored,
                RecordId = record.RecordId,
                EventCount = 0,
                ChunkCount = 0,
                ReplayState = record.ReplayState,
                Kinds = "",
                Removed = removed,
                Message = "回放数据整理失败；对局与统计已按仅分析模式保留：" + replayFailure.Message
            };
        }
        catch (Exception fallbackFailure)
        {
            return new FinalizationResult
            {
                RecordId = record.RecordId,
                Error = replayFailure.Message + "; analysis-only fallback failed: " + fallbackFailure.Message
            };
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static void LogFinalization(FinalizationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AuraToolsLog.Warn("[MatchRecords] replay finalization failed: " + result.Error);
            return;
        }

        if (!result.Stored)
        {
            AuraToolsLog.Warn("[MatchRecords] replay was not stored: record=" + result.RecordId + ".");
            return;
        }

        AuraToolsLog.Info("[MatchRecords] v8 replay stored: events=" + result.EventCount
                          + ", chunks=" + result.ChunkCount
                          + ", semantics=" + result.SemanticCount
                          + ", card-transitions=" + result.CardTransitionCount
                          + ", state=" + result.ReplayState
                          + ", kinds=" + result.Kinds
                          + (result.Removed > 0 ? ", retention-removed=" + result.Removed : "")
                          + (string.IsNullOrWhiteSpace(result.Message) ? "" : ", note=" + result.Message)
                          + ".");
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
            if (mode?.NowDice != null)
            {
                diceJson = JsonConvert.SerializeObject(mode.NowDice);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay view context capture degraded: " + ex.Message);
        }
    }

    private static IDataConfig? ResolveConfig(object target)
    {
        return target switch
        {
            CardItem card => card.dataConfig,
            SkillItem skill => skill.dataConfig,
            _ => null
        };
    }

    private static string ResolveActorId(object target)
    {
        return target switch
        {
            CardItem card => card.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
            SkillItem skill => skill.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
            _ => FightPlayer.Instance?.Status?.InstanceId ?? ""
        };
    }

    private static void RecordCaptureDiagnostic(string stage, Exception ex)
    {
        var message = stage + ": " + ex.Message;
        lock (Gate)
        {
            AddCaptureDiagnosticNoLock(message);
        }

        AuraToolsLog.Warn("[MatchRecords] replay " + stage + " failed: " + ex.Message);
    }

    private static void AddCaptureDiagnosticNoLock(string message)
    {
        var normalized = (message ?? "").Trim();
        if (normalized.Length == 0
            || CaptureDiagnostics.Contains(normalized, StringComparer.Ordinal)
            || CaptureDiagnostics.Count >= 16)
        {
            return;
        }

        CaptureDiagnostics.Add(normalized);
    }

    private static void IncrementKindNoLock(string kind)
    {
        EventKindCounts[kind] = EventKindCounts.TryGetValue(kind, out var count) ? count + 1 : 1;
    }

    private static long ElapsedMilliseconds()
    {
        var elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
        return Math.Max(0L, (long)(elapsed * 1000d / Stopwatch.Frequency));
    }

    private static T? Argument<T>(object[]? arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Length && arguments[index] is T value
            ? value
            : default;
    }

    private static string Value(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static string Value(IEnumerable<MatchReplayStringValue>? values, string key)
    {
        return values?.LastOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Value ?? "";
    }

    private static string First(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private sealed class ActiveAction
    {
        internal string ActionId { get; set; } = "";
        internal int ActionIndex { get; set; }
        internal int TurnIndex { get; set; }
        internal long StartedMilliseconds { get; set; }
        internal string Kind { get; set; } = "";
        internal string ActorId { get; set; } = "";
        internal string SourceId { get; set; } = "";
        internal string SourceInstanceId { get; set; } = "";
        internal string Label { get; set; } = "";
        internal MatchReplayCardState? SourcePresentation { get; set; }
        internal MatchReplayStateSnapshot Before { get; set; } = new();
        internal int Depth { get; set; }
        internal bool FinalizationScheduled { get; set; }
        internal MatchReplayActionConvergenceTracker Convergence { get; } = new();
    }

    private sealed class FinalizationResult
    {
        internal bool Stored { get; set; }
        internal string RecordId { get; set; } = "";
        internal int EventCount { get; set; }
        internal int ChunkCount { get; set; }
        internal int SemanticCount { get; set; }
        internal int CardTransitionCount { get; set; }
        internal string ReplayState { get; set; } = "";
        internal string Kinds { get; set; } = "";
        internal int Removed { get; set; }
        internal string Message { get; set; } = "";
        internal string Error { get; set; } = "";
    }
}
