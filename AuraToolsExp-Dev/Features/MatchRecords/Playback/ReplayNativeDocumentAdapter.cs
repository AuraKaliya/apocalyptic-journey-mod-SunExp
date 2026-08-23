using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal sealed class ReplayNativeLoadResult
{
    internal MatchRecord Record { get; set; } = new();

    internal ReplayDocumentV11 Document { get; set; } = new();

    internal List<MatchReplayEvent> Events { get; set; } = new();
}

internal static class ReplayNativeDocumentAdapter
{
    internal static bool TryLoad(string recordId, out ReplayNativeLoadResult result, out string message)
    {
        result = new ReplayNativeLoadResult();
        message = "";
        var record = MatchRecordStorage.Database.Get(recordId);
        var document = MatchRecordStorage.Database.LoadV11(recordId, loadAttachmentPayloads: false);
        if (record == null || document == null)
        {
            message = "找不到可播放的 Replay Document v11。";
            return false;
        }

        var validation = ReplayDocumentValidatorV11.Validate(document);
        if (!validation.IsValid)
        {
            message = "回放完整性校验失败：" + validation.Message;
            return false;
        }

        if (record.ReplayProtocol != MatchReplayProtocol.Version
            || document.Header.DocumentVersion != ReplayProtocolV11.DocumentVersion)
        {
            message = "该记录不是当前原生回放协议 v11。";
            return false;
        }

        var currentFingerprint = MatchReplayRecorder.CurrentRuntimeFingerprint();
        if (!string.Equals(document.Header.RuntimeFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            message = "当前游戏或 MOD 依赖与录制环境不一致；请使用匹配版本，或观看已导出的 MP4。";
            return false;
        }

        ApplyNativeContext(record, document);
        var events = Convert(document, record);
        if (events.Count == 0 || events.All(value => value.Kind != MatchReplayEventKinds.ActionFrame))
        {
            message = "回放没有完整的原生动作帧。";
            return false;
        }

        record.EventCount = events.Count;
        result = new ReplayNativeLoadResult
        {
            Record = record,
            Document = document,
            Events = events
        };
        return true;
    }

    private static void ApplyNativeContext(MatchRecord record, ReplayDocumentV11 document)
    {
        var context = document.NativeBattle ?? new ReplayNativeBattleContextV11();
        record.ReplayProtocol = MatchReplayProtocol.Version;
        record.RequiredCapabilities = (document.Header.RequiredCapabilities ?? new List<string>()).ToList();
        record.ModFingerprint = document.Header.RuntimeFingerprint;
        record.InitialState ??= new MatchReplayInitialState();
        record.InitialState.LevelId = document.Header.LevelId;
        record.InitialState.BackgroundScene = context.BackgroundScene;
        record.InitialState.MapMode = context.MapMode;
        record.InitialState.MapLevel = context.MapLevel;
        record.InitialState.DiceJson = context.DiceJson;
        record.InitialState.RoleQueue = (byte[])(context.RoleQueue ?? Array.Empty<byte>()).Clone();
        record.InitialState.TemporaryRoles = (byte[])(context.TemporaryRoles ?? Array.Empty<byte>()).Clone();
        record.InitialState.EnemyPositive = context.EnemyPositive;
        record.InitialState.EnemyHp = context.EnemyHp;
        record.InitialState.RoleTableJson = context.RoleTableJson;
        var baseline = ToLegacyState(document.InitialState, document, context);
        baseline.RoleTableJson = context.RoleTableJson;
        record.InitialState.BaselineState = baseline;
    }

    private static List<MatchReplayEvent> Convert(ReplayDocumentV11 document, MatchRecord record)
    {
        var result = new List<MatchReplayEvent>();
        var context = document.NativeBattle ?? new ReplayNativeBattleContextV11();
        var definitions = (document.Content?.Definitions ?? new List<ReplayContentDefinitionV11>())
            .GroupBy(value => value.Content.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var engine = new ReplayProjectionEngine();
        engine.Reset(document.InitialState);
        var initial = ToLegacyState(engine.Current, document, context);
        initial.RoleTableJson = context.RoleTableJson;
        result.Add(new MatchReplayEvent
        {
            Sequence = 0,
            TurnIndex = Math.Max(1, initial.TurnIndex),
            ElapsedMilliseconds = 0,
            Kind = MatchReplayEventKinds.TurnFrame,
            TurnFrame = new MatchReplayTurnFrame
            {
                TurnIndex = Math.Max(1, initial.TurnIndex),
                ActiveActorId = document.InitialState.ActiveActorId,
                State = initial,
                StateHash = MatchReplayProjectionState.Hash(initial)
            }
        });

        var starts = new Dictionary<string, long>(StringComparer.Ordinal);
        var actionIndex = 0;
        var checkpointSequences = new HashSet<long>((document.Checkpoints ?? new List<ReplayCheckpointV11>())
            .Where(value => value.EventSequence > 0)
            .Select(value => value.EventSequence));
        foreach (var value in document.Events.OrderBy(item => item.Sequence))
        {
            var before = ToLegacyState(engine.Current, document, context);
            if (value.EventType == ReplayEventTypesV11.ActionStarted && !string.IsNullOrWhiteSpace(value.ActionId))
                starts[value.ActionId] = value.TimeTicks;
            engine.Apply(value);
            var after = ToLegacyState(engine.Current, document, context);
            var elapsed = ToMilliseconds(value.TimeTicks);
            if (value.EventType == ReplayEventTypesV11.TurnChanged)
            {
                result.Add(new MatchReplayEvent
                {
                    Sequence = value.Sequence * 10,
                    TurnIndex = after.TurnIndex,
                    ElapsedMilliseconds = elapsed,
                    Kind = MatchReplayEventKinds.TurnFrame,
                    TurnFrame = new MatchReplayTurnFrame
                    {
                        TurnIndex = after.TurnIndex,
                        ActiveActorId = engine.Current.ActiveActorId,
                        State = after,
                        StateHash = MatchReplayProjectionState.Hash(after)
                    }
                });
            }
            else if (value.EventType == ReplayEventTypesV11.ActionCompleted
                     || value.EventType == ReplayEventTypesV11.StateChanged && value.Delta != null)
            {
                var sourceDefinition = Definition(definitions, value.SourceInstanceId, before, after, document, value);
                var sourceId = sourceDefinition?.Content.StableContentId ?? "";
                var label = sourceDefinition?.Display.Name ?? value.EventType;
                var sourceCard = FindCard(value.SourceInstanceId, sourceId, before, after);
                var intent = before.EnemyIntents.FirstOrDefault(item =>
                    string.Equals(item.ActorId, value.ActorId, StringComparison.Ordinal));
                var native = NativePresentation(value.NativePresentation, value.ActorId);
                var kind = ResolveActionKind(sourceDefinition, value, intent);
                var actionId = string.IsNullOrWhiteSpace(value.ActionId)
                    ? "state-" + value.Sequence.ToString("D8")
                    : value.ActionId;
                var derived = MatchReplayActionDerivation.Build(
                    actionId,
                    kind,
                    value.ActorId,
                    sourceId,
                    value.SourceInstanceId,
                    label,
                    sourceCard,
                    before,
                    after,
                    native,
                    intent);
                var startedTicks = starts.TryGetValue(actionId, out var start) ? start : value.TimeTicks;
                result.Add(new MatchReplayEvent
                {
                    Sequence = value.Sequence * 10,
                    TurnIndex = after.TurnIndex,
                    ElapsedMilliseconds = elapsed,
                    Kind = MatchReplayEventKinds.ActionFrame,
                    ActionFrame = new MatchReplayActionFrame
                    {
                        ActionId = actionId,
                        ActionIndex = ++actionIndex,
                        TurnIndex = after.TurnIndex,
                        StartedMilliseconds = ToMilliseconds(startedTicks),
                        EndedMilliseconds = elapsed,
                        DurationMilliseconds = derived.DurationMilliseconds,
                        Kind = kind,
                        ActorId = value.ActorId,
                        SourceId = sourceId,
                        SourceInstanceId = value.SourceInstanceId,
                        Label = label,
                        SourcePresentation = sourceCard,
                        IntentPresentation = intent,
                        NativePresentation = native,
                        Delta = MatchReplayProjectionState.CreateDelta(before, after),
                        CardTransitions = derived.CardTransitions,
                        Presentation = derived.Presentation,
                        Semantics = derived.Semantics,
                        FinalStateHash = MatchReplayProjectionState.Hash(after)
                    }
                });
            }
            else if (value.EventType == ReplayEventTypesV11.BattleCompleted)
            {
                result.Add(new MatchReplayEvent
                {
                    Sequence = value.Sequence * 10,
                    TurnIndex = after.TurnIndex,
                    ElapsedMilliseconds = elapsed,
                    Kind = MatchReplayEventKinds.BattleResultFrame,
                    BattleResultFrame = new MatchReplayBattleResultFrame { Result = record.Result }
                });
            }

            if (checkpointSequences.Contains(value.Sequence))
            {
                result.Add(new MatchReplayEvent
                {
                    Sequence = value.Sequence * 10 + 9,
                    TurnIndex = after.TurnIndex,
                    ElapsedMilliseconds = elapsed,
                    Kind = MatchReplayEventKinds.SeekCheckpoint,
                    SeekCheckpoint = new MatchReplaySeekCheckpoint
                    {
                        TurnIndex = after.TurnIndex,
                        CompletedActionCount = actionIndex,
                        State = after,
                        StateHash = MatchReplayProjectionState.Hash(after)
                    }
                });
            }
        }

        return result.OrderBy(value => value.Sequence).ToList();
    }

    internal static MatchReplayStateSnapshot ToLegacyState(
        ReplayLogicalStateV11 source,
        ReplayDocumentV11 document,
        ReplayNativeBattleContextV11 context)
    {
        var definitions = (document.Content?.Definitions ?? new List<ReplayContentDefinitionV11>())
            .GroupBy(value => value.Content.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        return new MatchReplayStateSnapshot
        {
            LevelId = source.LevelId,
            TurnIndex = Math.Max(1, source.TurnIndex),
            EnemyPositive = context.EnemyPositive,
            EnemyHp = context.EnemyHp,
            PlayerPower = source.PlayerPower,
            PlayerMaxPower = source.PlayerMaxPower,
            RoleTableJson = context.RoleTableJson,
            CardTopCount = source.CardTopCount,
            Statuses = source.Actors.Select(actor => new MatchReplayStatusState
            {
                InstanceId = actor.InstanceId,
                ContentOwnerModId = actor.Content.OwnerModId,
                ContentId = actor.Content.StableContentId,
                EntityKind = actor.EntityKind,
                SlotIndex = actor.SlotIndex,
                MaxHp = actor.MaxHp,
                CurrentHp = actor.CurrentHp,
                Defend = actor.Defense,
                State = actor.State,
                DynamicVariables = (actor.Variables ?? new List<ReplayIntValueV11>())
                    .Select(item => new MatchReplayFloatValue { Key = item.Key, Value = item.Value })
                    .ToList(),
                Buffs = (actor.Buffs ?? new List<ReplayBuffStateV11>()).Select(buff => new MatchReplayBuffState
                {
                    BuffId = buff.Content.StableContentId,
                    Level = buff.Level,
                    UpperBound = buff.UpperBound,
                    ReducePerTurn = buff.ReducePerTurn,
                    ReducePerUse = buff.ReducePerUse,
                    ReducePerAttacked = buff.ReducePerAttacked,
                    Vars = Values(buff.Values)
                }).ToList()
            }).ToList(),
            Cards = source.Cards.Select(card => Card(card, definitions)).ToList(),
            EnemyIntents = source.Intents.Select(intent => Intent(intent, definitions)).ToList()
        };
    }

    private static MatchReplayCardState Card(
        ReplayCardStateV11 card,
        IReadOnlyDictionary<string, ReplayContentDefinitionV11> definitions)
    {
        definitions.TryGetValue(card.Content.Key, out var definition);
        var data = Values(card.Values);
        var replayValues = data
            .Where(value => value.Key.StartsWith("AuraReplay.CardVisual.", StringComparison.Ordinal))
            .ToList();
        data.RemoveAll(value => value.Key.StartsWith("AuraReplay.CardVisual.", StringComparison.Ordinal));
        Put(data, "Id", card.Content.StableContentId);
        Put(data, "Expend", card.DisplayedCost.ToString());
        Put(data, "Name", definition?.Display.Name);
        Put(data, "Description", definition?.Display.Description);
        foreach (var value in definition?.Display.Values ?? new List<ReplayStringValueV11>())
            Put(data, value.Key, value.Value);
        return new MatchReplayCardState
        {
            Zone = card.Zone,
            Order = card.Order,
            ReplayCardId = card.InstanceId,
            CardId = card.Content.StableContentId,
            DataType = (int)DataType.Card,
            Data = data,
            Vars = replayValues.Concat(new[]
            {
                new MatchReplayStringValue { Key = "InstanceID", Value = card.InstanceId },
                new MatchReplayStringValue { Key = "Expend", Value = card.DisplayedCost.ToString() }
            }).ToList()
        };
    }

    private static MatchReplayEnemyIntentState Intent(
        ReplayIntentStateV11 intent,
        IReadOnlyDictionary<string, ReplayContentDefinitionV11> definitions)
    {
        definitions.TryGetValue(intent.Content.Key, out var definition);
        return new MatchReplayEnemyIntentState
        {
            ActorId = intent.ActorId,
            SlotIndex = intent.SlotIndex,
            IntentId = intent.Content.StableContentId,
            SourceInstanceId = intent.InstanceId,
            Label = definition?.Display.Name ?? intent.Content.StableContentId,
            Description = definition?.Display.Description ?? "",
            Icon = Value(definition?.Display.Values, "Icon"),
            BackIcon = Value(definition?.Display.Values, "BackIcon"),
            DisplayValue = intent.DisplayValue,
            ActionState = Value(definition?.Display.Values, "Action"),
            EffectName = Value(definition?.Display.Values, "Effects"),
            TargetIds = (intent.TargetIds ?? new List<string>()).ToList()
        };
    }

    private static ReplayContentDefinitionV11? Definition(
        IReadOnlyDictionary<string, ReplayContentDefinitionV11> definitions,
        string sourceInstanceId,
        MatchReplayStateSnapshot before,
        MatchReplayStateSnapshot after,
        ReplayDocumentV11 document,
        ReplayTimelineEventV11 value)
    {
        var reference = document.InitialState.Cards
            .Concat(document.Events.Where(item => item.Delta != null).SelectMany(item => item.Delta!.CardUpserts))
            .FirstOrDefault(item => string.Equals(item.InstanceId, sourceInstanceId, StringComparison.Ordinal))?.Content;
        if (reference != null && definitions.TryGetValue(reference.Key, out var direct)) return direct;
        var cueLabel = value.Presentation?.FirstOrDefault()?.Label;
        return definitions.Values.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(cueLabel)
            && string.Equals(item.Display.Name, cueLabel, StringComparison.Ordinal));
    }

    private static MatchReplayCardState? FindCard(
        string instanceId,
        string sourceId,
        MatchReplayStateSnapshot before,
        MatchReplayStateSnapshot after)
    {
        return before.Cards.Concat(after.Cards).FirstOrDefault(card =>
            !string.IsNullOrWhiteSpace(instanceId)
                ? string.Equals(card.ReplayCardId, instanceId, StringComparison.Ordinal)
                : string.Equals(card.CardId, sourceId, StringComparison.Ordinal));
    }

    private static MatchReplayActionPresentationState NativePresentation(
        ReplayNativeActionPresentationV11? source,
        string actorId)
    {
        return new MatchReplayActionPresentationState
        {
            ActorAnimationState = source?.ActorAnimationState ?? "Idle",
            EffectName = source?.EffectName ?? "",
            EffectDelayMilliseconds = Math.Max(0, source?.EffectDelayMilliseconds ?? 0),
            PresentationDurationMilliseconds = Math.Max(360, source?.PresentationDurationMilliseconds ?? 360),
            Targets = (source?.Targets ?? new List<ReplayNativeTargetPresentationV11>())
                .Where(value => !string.IsNullOrWhiteSpace(value.TargetId))
                .Select(value => new MatchReplayTargetPresentationState
                {
                    TargetId = value.TargetId,
                    AnimationState = value.AnimationState
                }).ToList()
        };
    }

    private static string ResolveActionKind(
        ReplayContentDefinitionV11? definition,
        ReplayTimelineEventV11 value,
        MatchReplayEnemyIntentState? intent)
    {
        if (intent != null && value.Presentation.Any(item => item.Kind == ReplayPresentationKindsV11.EnemyIntent))
            return MatchReplayActionKinds.EnemyIntentUse;
        if (string.Equals(definition?.Content.ContentKind, "Skill", StringComparison.OrdinalIgnoreCase)
            || value.Presentation.Any(item => item.Kind == ReplayPresentationKindsV11.Skill))
            return MatchReplayActionKinds.SkillUse;
        return string.Equals(value.EventType, ReplayEventTypesV11.StateChanged, StringComparison.Ordinal)
            ? MatchReplayActionKinds.SystemState
            : MatchReplayActionKinds.CardUse;
    }

    private static List<MatchReplayStringValue> Values(IEnumerable<ReplayStringValueV11>? values)
    {
        return (values ?? Enumerable.Empty<ReplayStringValueV11>())
            .Where(value => !string.IsNullOrWhiteSpace(value.Key))
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => new MatchReplayStringValue
            {
                Key = group.Key,
                Value = group.Last().Value ?? ""
            }).ToList();
    }

    private static void Put(List<MatchReplayStringValue> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        values.RemoveAll(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        values.Add(new MatchReplayStringValue { Key = key, Value = value! });
    }

    private static string Value(IEnumerable<ReplayStringValueV11>? values, string key)
    {
        return (values ?? Enumerable.Empty<ReplayStringValueV11>())
            .LastOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal))?.Value ?? "";
    }

    private static long ToMilliseconds(long ticks)
    {
        return Math.Max(0L, ticks * 1000L / ReplayProtocolV11.TimebaseTicksPerSecond);
    }
}
