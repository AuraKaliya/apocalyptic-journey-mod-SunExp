using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static readonly List<MatchReplayEvent> Events = new();
    private static MatchRecord? activeRecord;
    private static long startedTimestamp;
    private static long nextSequence;
    private static int turnIndex;
    private static bool completing;

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

        lock (Gate)
        {
            if (activeRecord != null)
            {
                CompleteNoLock("Restarted");
            }

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
            Events.Clear();
            startedTimestamp = Stopwatch.GetTimestamp();
            nextSequence = 0;
            turnIndex = 1;
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
                if (activeRecord == null || completing)
                {
                    return;
                }

                Events.Add(new MatchReplayEvent
                {
                    Sequence = ++nextSequence,
                    TurnIndex = Math.Max(1, turnIndex),
                    ElapsedMilliseconds = ElapsedMilliseconds(),
                    Kind = kind,
                    TypeName = command.GetType().FullName ?? command.GetType().Name,
                    Payload = payload,
                    Semantic = MatchSemanticEventFactory.From(command)
                });
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay event capture failed: " + ex.Message);
        }
    }

    internal static void StartTurn()
    {
        lock (Gate)
        {
            if (activeRecord != null)
            {
                turnIndex++;
            }
        }
    }

    internal static void Complete(string result)
    {
        lock (Gate)
        {
            CompleteNoLock(result);
        }
    }

    internal static void Abort()
    {
        lock (Gate)
        {
            activeRecord = null;
            Events.Clear();
            completing = false;
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

    private static void CompleteNoLock(string result)
    {
        if (activeRecord == null || completing || MatchReplaySessionState.IsPlayback)
        {
            return;
        }

        completing = true;
        try
        {
            activeRecord.Result = string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim();
            activeRecord.EndedUtc = DateTime.UtcNow.ToString("O");
            activeRecord.EventCount = Events.Count;
            activeRecord.TurnCount = Events.Count == 0 ? Math.Max(1, turnIndex) : Events.Max(item => item.TurnIndex);
            activeRecord.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
            var replaySettings = AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay;
            var chunks = MatchReplayChunker.Build(Events, replaySettings.ChunkTargetBytes);
            if (MatchRecordStorage.Database.Save(activeRecord, chunks))
            {
                MatchRecordStorage.Database.SaveAnalysis(MatchAnalysisBuilder.Build(activeRecord, Events));
                var removed = MatchRecordStorage.Database.EnforceAutoLimit(replaySettings.AutoRecordLimit);
                AuraToolsLog.Info("[MatchRecords] replay stored: events=" + Events.Count
                                  + ", chunks=" + chunks.Count
                                  + (removed > 0 ? ", retained=" + replaySettings.AutoRecordLimit : "") + ".");
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay finalization failed: " + ex.Message);
        }
        finally
        {
            activeRecord = null;
            Events.Clear();
            completing = false;
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
