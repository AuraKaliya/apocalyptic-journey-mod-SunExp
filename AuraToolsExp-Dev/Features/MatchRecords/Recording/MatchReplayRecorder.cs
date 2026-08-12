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
using Fight.ActionCommand;
using Fight.ObjTarget;
using Fight.StatusCommand;
using MemoryPack;
using Newtonsoft.Json;
using Witch;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayRecorder
{
    private static readonly object Gate = new();
    private static MatchRecord? activeRecord;
    private static MatchReplayWorkingBuffer? workingBuffer;
    private static long startedTimestamp;
    private static long nextSequence;
    private static int turnIndex;
    private static bool completing;
    private static string activeActionId = "";
    private static int eventsSinceCheckpoint;
    private static bool captureFailed;
    private static string captureFailure = "";

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
                RequiredCapabilities = new List<string>
                {
                    MatchReplayCapabilities.CommandsV1,
                    MatchReplayCapabilities.StatusSnapshotsV1
                },
                OptionalCapabilities = new List<string>
                {
                    MatchReplayCapabilities.CheckpointsV1,
                    MatchReplayCapabilities.CausalityV1
                },
                InitialState = new MatchReplayInitialState
                {
                    LevelId = levelId,
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
                Path.Combine(MatchRecordStorage.TemporaryDirectory, "recording-" + recordId));
            startedTimestamp = Stopwatch.GetTimestamp();
            nextSequence = 0;
            turnIndex = 1;
            activeActionId = "";
            eventsSinceCheckpoint = 0;
            captureFailed = false;
            captureFailure = "";
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

    internal static void Record(object? command)
    {
        if (command == null || MatchReplaySessionState.IsPlayback)
        {
            return;
        }

        try
        {
            string kind;
            byte[] payload;
            switch (command)
            {
                case ActionCommandBase action:
                    kind = MatchReplayEventKinds.ActionCommand;
                    payload = MemoryPackSerializer.Serialize<ActionCommandBase>(action);
                    break;
                case ClientCommandBase client:
                    kind = MatchReplayEventKinds.ClientCommand;
                    payload = MemoryPackSerializer.Serialize<ClientCommandBase>(client);
                    break;
                case ObjTargetBase target:
                    kind = MatchReplayEventKinds.TargetCommand;
                    payload = MemoryPackSerializer.Serialize<ObjTargetBase>(target);
                    break;
                case StatusDataTransfer status:
                    kind = MatchReplayEventKinds.StatusSnapshot;
                    payload = Encoding.UTF8.GetBytes(AuraSharedJson.SerializeCompact(status));
                    break;
                default:
                    return;
            }

            lock (Gate)
            {
                if (activeRecord == null || workingBuffer == null || completing)
                {
                    return;
                }

                var semantic = MatchSemanticEventFactory.From(command);
                ApplyCausality(semantic);
                workingBuffer.Add(new MatchReplayEvent
                {
                    Sequence = ++nextSequence,
                    TurnIndex = Math.Max(1, turnIndex),
                    ElapsedMilliseconds = ElapsedMilliseconds(),
                    Kind = kind,
                    TypeName = command.GetType().FullName ?? command.GetType().Name,
                    Payload = payload,
                    Semantic = semantic
                });
                eventsSinceCheckpoint++;
            }
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                captureFailed = true;
                captureFailure = ex.Message;
            }
            AuraToolsLog.Warn("[MatchRecords] replay event capture failed: " + ex.Message);
        }
    }

    internal static void CaptureCheckpointIfDue()
    {
        lock (Gate)
        {
            var interval = AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.CheckpointEventInterval;
            if (activeRecord != null
                && workingBuffer != null
                && !completing
                && eventsSinceCheckpoint >= interval
                && FightManager.Instance != null)
            {
                AddCheckpointNoLock(MatchReplayStateCapture.Capture(nextSequence + 1, turnIndex));
            }
        }
    }

    internal static void StartTurn()
    {
        lock (Gate)
        {
            if (activeRecord != null && workingBuffer != null)
            {
                turnIndex++;
                activeActionId = "";
                AddCheckpointNoLock(MatchReplayStateCapture.Capture(nextSequence + 1, turnIndex));
            }
        }
    }

    internal static void Complete(string result)
    {
        MatchRecord? record;
        MatchReplayWorkingBuffer? buffer;
        bool invalid;
        string invalidReason;
        lock (Gate)
        {
            if (activeRecord == null || workingBuffer == null || completing || MatchReplaySessionState.IsPlayback)
            {
                return;
            }

            completing = true;
            record = activeRecord;
            buffer = workingBuffer;
            invalid = captureFailed;
            invalidReason = captureFailure;
            activeRecord = null;
            workingBuffer = null;
            captureFailed = false;
            captureFailure = "";
        }

        if (invalid)
        {
            buffer.Dispose();
            lock (Gate) completing = false;
            AuraToolsLog.Warn("[MatchRecords] discarded incomplete replay after capture failure: " + invalidReason);
            return;
        }

        CompleteDetached(record, buffer, result);
    }

    internal static void Abort()
    {
        lock (Gate)
        {
            activeRecord = null;
            workingBuffer?.Dispose();
            workingBuffer = null;
            completing = false;
            activeActionId = "";
            captureFailed = false;
            captureFailure = "";
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

    private static void CompleteDetached(MatchRecord active, MatchReplayWorkingBuffer buffer, string result)
    {
        try
        {
            active.Result = string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim();
            active.EndedUtc = DateTime.UtcNow.ToString("O");
            active.EventCount = buffer.EventCount;
            active.TurnCount = Math.Max(1, turnIndex);
            active.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
            var replaySettings = AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay;
            var analysis = MatchAnalysisBuilder.Build(active, buffer.ReadEvents());
            var chunkCount = buffer.ChunkCount;
            if (MatchRecordStorage.Database.SaveStreaming(active, buffer.ReadChunks(), analysis))
            {
                var removed = MatchRecordStorage.Database.EnforceAutoLimit(replaySettings.AutoRecordLimit);
                AuraToolsLog.Info("[MatchRecords] replay stored: events=" + active.EventCount
                                  + ", chunks=" + chunkCount
                                  + (removed > 0 ? ", retained=" + replaySettings.AutoRecordLimit : "") + ".");
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay finalization failed: " + ex.Message);
        }
        finally
        {
            buffer.Dispose();
            lock (Gate)
            {
                completing = false;
                activeActionId = "";
            }
        }
    }

    private static void AddCheckpointNoLock(MatchReplayCheckpoint checkpoint)
    {
        if (workingBuffer == null) return;
        var payload = MatchReplayPayload.Encode(checkpoint);
        workingBuffer.Add(new MatchReplayEvent
        {
            Sequence = ++nextSequence,
            TurnIndex = Math.Max(1, turnIndex),
            ElapsedMilliseconds = ElapsedMilliseconds(),
            Kind = MatchReplayEventKinds.Checkpoint,
            TypeName = typeof(MatchReplayCheckpoint).FullName ?? nameof(MatchReplayCheckpoint),
            Payload = payload
        });
        eventsSinceCheckpoint = 0;
    }

    private static void ApplyCausality(MatchSemanticEvent semantic)
    {
        semantic.EventId = "event-" + (nextSequence + 1);
        semantic.SourceInstanceId = string.IsNullOrWhiteSpace(semantic.SourceInstanceId) ? semantic.ActorId : semantic.SourceInstanceId;
        semantic.TargetInstanceId = string.IsNullOrWhiteSpace(semantic.TargetInstanceId) ? semantic.TargetId : semantic.TargetInstanceId;
        if (semantic.Category == MatchSemanticCategories.Card)
        {
            activeActionId = "action-" + (nextSequence + 1);
            semantic.ActionId = activeActionId;
            semantic.RootActionId = activeActionId;
            semantic.AttributionConfidence = MatchAttributionConfidence.Exact;
            return;
        }

        if (!string.IsNullOrWhiteSpace(activeActionId))
        {
            semantic.ActionId = activeActionId;
            semantic.CauseId = activeActionId;
            semantic.RootActionId = activeActionId;
            semantic.AttributionConfidence = MatchAttributionConfidence.Inferred;
        }
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
}
