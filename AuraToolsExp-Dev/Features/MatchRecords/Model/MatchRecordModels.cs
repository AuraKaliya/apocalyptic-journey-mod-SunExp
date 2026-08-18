using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal static class MatchReplayProtocol
{
    internal const int Version = 9;
    internal const int MinimumSupportedVersion = 8;
}

internal static class MatchReplayCapabilities
{
    internal const string AuthoritativeFramesV1 = "authoritative-frames.v1";
    internal const string StateProjectionV1 = "state-projection.v1";
    internal const string PresentationTimelineV1 = "presentation-timeline.v1";
    internal const string IndexedSeekV1 = "indexed-seek.v1";
    internal const string AsyncFinalizationV1 = "async-finalization.v1";
    internal const string CausalityV1 = "causality.v1";
    internal const string RuntimeContextV1 = "runtime-context.v1";
    internal const string CardPresentationReadyV1 = "card-presentation-ready.v1";
    internal const string IncrementalHandV1 = "incremental-hand.v1";
    internal const string EntityDeltaV2 = "entity-delta.v2";
    internal const string OutcomeCuesV1 = "outcome-cues.v1";
    internal const string PassiveHudV1 = "passive-hud.v1";
    internal const string NativeActionPresentationV1 = "native-action-presentation.v1";
    internal const string NativeActionPresentationV2 = "native-action-presentation.v2";
    internal const string EnemyIntentFramesV1 = "enemy-intent-frames.v1";
    internal const string RemotePlayerActionsV1 = "remote-player-actions.v1";

    internal static readonly string[] Supported =
    {
        AuthoritativeFramesV1,
        StateProjectionV1,
        PresentationTimelineV1,
        IndexedSeekV1,
        AsyncFinalizationV1,
        CausalityV1,
        RuntimeContextV1,
        CardPresentationReadyV1,
        IncrementalHandV1,
        EntityDeltaV2,
        OutcomeCuesV1,
        PassiveHudV1,
        NativeActionPresentationV1,
        NativeActionPresentationV2,
        EnemyIntentFramesV1,
        RemotePlayerActionsV1
    };
}

internal static class MatchRecordCollections
{
    internal const string Auto = "Auto";
    internal const string Favorite = "Favorite";
}

internal static class MatchReplayStates
{
    internal const string Ready = "Ready";
    internal const string Incomplete = "Incomplete";
    internal const string Corrupt = "Corrupt";
}

internal static class MatchRecordOrigins
{
    internal const string Auto = "Auto";
    internal const string Imported = "Imported";
}

internal static class MatchReplayEventKinds
{
    internal const string TurnFrame = "TurnFrame";
    internal const string ActionFrame = "ActionFrame";
    internal const string SeekCheckpoint = "SeekCheckpoint";

    // Retained as data labels so analysis/import code can identify obsolete captures.
    // Authoritative-frame protocols (v8+) never record or execute these command-replay events.
    internal const string ActionBegin = "ActionBegin";
    internal const string ActionEnd = "ActionEnd";
    internal const string ActionCommand = "ActionCommand";
    internal const string ClientCommand = "ClientCommand";
    internal const string TargetCommand = "TargetCommand";
    internal const string StatusSnapshot = "StatusSnapshot";
    internal const string Checkpoint = "Checkpoint";
}

internal sealed class MatchRecord
{
    public long Sequence { get; set; }

    public string RecordId { get; set; } = "";

    public string AdventureId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string Result { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public string Collection { get; set; } = MatchRecordCollections.Auto;

    public bool IsFavorite { get; set; }

    public string Origin { get; set; } = MatchRecordOrigins.Auto;

    public string Tags { get; set; } = "";

    public string Notes { get; set; } = "";

    public string ReplayState { get; set; } = MatchReplayStates.Ready;

    public int ReplayProtocol { get; set; } = MatchReplayProtocol.Version;

    public string GameBuild { get; set; } = "";

    public string ToolBuild { get; set; } = "";

    public string ModFingerprint { get; set; } = "";

    public List<string> RequiredCapabilities { get; set; } = new();

    public List<string> OptionalCapabilities { get; set; } = new();

    public List<string> ContentDependencies { get; set; } = new();

    public string ContentSha256 { get; set; } = "";

    public int EventCount { get; set; }

    public int TurnCount { get; set; }

    public long CompressedBytes { get; set; }

    public string StatisticsJson { get; set; } = "";

    public List<string> CaptureDiagnostics { get; set; } = new();

    public MatchReplayInitialState InitialState { get; set; } = new();
}

internal sealed class MatchReplayInitialState
{
    public string LevelId { get; set; } = "";

    public string BackgroundScene { get; set; } = "";

    public string MapMode { get; set; } = "";

    public int MapLevel { get; set; }

    public string DiceJson { get; set; } = "";

    public byte[] RoleQueue { get; set; } = Array.Empty<byte>();

    public byte[] TemporaryRoles { get; set; } = Array.Empty<byte>();

    public float EnemyPositive { get; set; }

    public float EnemyHp { get; set; }

    public string RoleTableJson { get; set; } = "";

    public MatchReplayStateSnapshot? BaselineState { get; set; }
}

internal sealed class MatchReplayEvent
{
    public long Sequence { get; set; }

    public int TurnIndex { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public string Kind { get; set; } = "";

    public string TypeName { get; set; } = "";

    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public MatchSemanticEvent? Semantic { get; set; }

    public MatchReplayActionBoundary? ActionBoundary { get; set; }

    public MatchReplayTurnFrame? TurnFrame { get; set; }

    public MatchReplayActionFrame? ActionFrame { get; set; }

    public MatchReplaySeekCheckpoint? SeekCheckpoint { get; set; }
}

internal static class MatchReplayActionPhases
{
    internal const string Begin = "Begin";
    internal const string End = "End";
}

internal static class MatchReplayActionKinds
{
    internal const string CardUse = "CardUse";
    internal const string SkillUse = "SkillUse";
    internal const string EnemyIntentUse = "EnemyIntentUse";
}

internal sealed class MatchReplayActionBoundary
{
    public string ActionId { get; set; } = "";

    public string ParentActionId { get; set; } = "";

    public int ActionIndex { get; set; }

    public string Phase { get; set; } = "";

    public string Kind { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string Label { get; set; } = "";
}

internal sealed class MatchReplayCheckpoint
{
    public long EventSequence { get; set; }

    public int TurnIndex { get; set; }

    public string ActionId { get; set; } = "";

    public int ActionIndex { get; set; }

    public string StateHash { get; set; } = "";

    public string LogicalStateHash { get; set; } = "";

    public string SnapshotJson { get; set; } = "";

    public bool CanRestore { get; set; }
}

internal sealed class MatchReplayStateSnapshot
{
    public string LevelId { get; set; } = "";

    public int TurnIndex { get; set; }

    public float EnemyPositive { get; set; }

    public float EnemyHp { get; set; }

    public int PlayerPower { get; set; }

    public int PlayerMaxPower { get; set; }

    public string RoleTableJson { get; set; } = "";

    public List<MatchReplayStatusState> Statuses { get; set; } = new();

    public int CardTopCount { get; set; }

    public List<MatchReplayCardState> Cards { get; set; } = new();

    public List<MatchReplayEnemyIntentState> EnemyIntents { get; set; } = new();
}

internal sealed class MatchReplayEnemyIntentState
{
    public string ActorId { get; set; } = "";

    public int SlotIndex { get; set; }

    public string IntentId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string Label { get; set; } = "";

    public string Description { get; set; } = "";

    public string Icon { get; set; } = "";

    public string BackIcon { get; set; } = "";

    public string DisplayValue { get; set; } = "";

    public string ActionState { get; set; } = "";

    public string EffectName { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();
}

internal sealed class MatchReplayCardState
{
    public string Zone { get; set; } = "";

    public int Order { get; set; }

    public string ReplayCardId { get; set; } = "";

    public string CardId { get; set; } = "";

    public int DataType { get; set; }

    public List<MatchReplayStringValue> Data { get; set; } = new();

    public List<MatchReplayStringValue> Vars { get; set; } = new();
}

internal sealed class MatchReplayStatusState
{
    public string InstanceId { get; set; } = "";

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int Defend { get; set; }

    public string State { get; set; } = "";

    public List<MatchReplayFloatValue> DynamicVariables { get; set; } = new();

    public List<MatchReplayBuffState> Buffs { get; set; } = new();
}

internal sealed class MatchReplayStringValue
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}

internal sealed class MatchReplayFloatValue
{
    public string Key { get; set; } = "";

    public float Value { get; set; }
}

internal sealed class MatchReplayBuffState
{
    public string BuffId { get; set; } = "";

    public int Level { get; set; }

    public int UpperBound { get; set; }

    public int ReducePerTurn { get; set; }

    public int ReducePerUse { get; set; }

    public int ReducePerAttacked { get; set; }

    public List<MatchReplayStringValue> Vars { get; set; } = new();
}

internal static class MatchSemanticCategories
{
    internal const string Card = "Card";
    internal const string EnemyIntent = "EnemyIntent";
    internal const string Damage = "Damage";
    internal const string Heal = "Heal";
    internal const string Defend = "Defend";
    internal const string Buff = "Buff";
    internal const string Resource = "Resource";
    internal const string Status = "Status";
    internal const string Target = "Target";
    internal const string Command = "Command";
}

internal sealed class MatchSemanticEvent
{
    public string EventId { get; set; } = "";

    public string ActionId { get; set; } = "";

    public string CauseId { get; set; } = "";

    public string RootActionId { get; set; } = "";

    public string Category { get; set; } = MatchSemanticCategories.Command;

    public string Action { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public string TargetInstanceId { get; set; } = "";

    public string AttributionConfidence { get; set; } = MatchAttributionConfidence.Unknown;

    public string Label { get; set; } = "";

    public long Value { get; set; }

    public long SecondaryValue { get; set; }

    public bool IsKeyEvent { get; set; }
}

internal static class MatchAttributionConfidence
{
    internal const string Exact = "Exact";
    internal const string Inferred = "Inferred";
    internal const string Unknown = "Unknown";
}

internal sealed class MatchReplayChunk
{
    public int ChunkIndex { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public int FirstTurnIndex { get; set; }

    public int LastTurnIndex { get; set; }

    public string Sha256 { get; set; } = "";

    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

internal sealed class MatchRecordPage
{
    internal MatchRecordPage(IReadOnlyList<MatchRecord> items, long nextCursor, bool hasMore, int totalCount)
    {
        Items = items;
        NextCursor = nextCursor;
        HasMore = hasMore;
        TotalCount = totalCount;
    }

    internal IReadOnlyList<MatchRecord> Items { get; }

    internal long NextCursor { get; }

    internal bool HasMore { get; }

    internal int TotalCount { get; }
}

internal static class MatchAnalysisProtocol
{
    internal const int Version = 2;
}

internal sealed class MatchAnalysisReport
{
    public int Protocol { get; set; } = MatchAnalysisProtocol.Version;

    public string RecordId { get; set; } = "";

    public string GeneratedUtc { get; set; } = "";

    public int TurnCount { get; set; }

    public long TotalDamage { get; set; }

    public long FriendlyDamageDealt { get; set; }

    public long EnemyDamageDealt { get; set; }

    public long FriendlyDamageTaken { get; set; }

    public long EnemyDamageTaken { get; set; }

    public long HpDamage { get; set; }

    public long ShieldDamage { get; set; }

    public long BestTurnDamage { get; set; }

    public int BestTurnIndex { get; set; }

    public int CardUseCount { get; set; }

    public List<MatchAnalysisTurn> Turns { get; set; } = new();

    public List<MatchAnalysisCombatant> Combatants { get; set; } = new();

    public List<MatchAnalysisCard> Cards { get; set; } = new();

    public List<MatchAnalysisMoment> KeyMoments { get; set; } = new();

    public List<MatchAnalysisDamageFlow> DamageFlows { get; set; } = new();
}

internal sealed class MatchAnalysisDamageFlow
{
    public string SourceTeam { get; set; } = "Unknown";

    public string TargetTeam { get; set; } = "Unknown";

    public long HpDamage { get; set; }

    public long ShieldDamage { get; set; }
}

internal sealed class MatchAnalysisTurn
{
    public int TurnIndex { get; set; }

    public long Damage { get; set; }

    public int CardUses { get; set; }

    public int ActionCount { get; set; }

    public long FirstEventSequence { get; set; }

    public long LastEventSequence { get; set; }
}

internal sealed class MatchAnalysisCombatant
{
    public string InstanceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Team { get; set; } = "";

    public long Damage { get; set; }

    public long BestTurnDamage { get; set; }

    public double AverageDamagePerTurn { get; set; }
}

internal sealed class MatchAnalysisCard
{
    public string CardId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Uses { get; set; }

    public long ObservedFollowUpDamage { get; set; }

    public long AttributedDamage { get; set; }

    public string AttributionConfidence { get; set; } = MatchAttributionConfidence.Unknown;

    public long FirstEventSequence { get; set; }
}

internal sealed class MatchAnalysisMoment
{
    public string Kind { get; set; } = "";

    public string Label { get; set; } = "";

    public int TurnIndex { get; set; }

    public long EventSequence { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public long Value { get; set; }
}

internal static class MatchMediaStates
{
    internal const string Ready = "Ready";
    internal const string Failed = "Failed";
}

internal sealed class MatchMediaAsset
{
    public string MediaId { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string Kind { get; set; } = "Video";

    public string Format { get; set; } = "";

    public string FilePath { get; set; } = "";

    public string CreatedUtc { get; set; } = "";

    public string State { get; set; } = MatchMediaStates.Ready;

    public long DurationMilliseconds { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double FramesPerSecond { get; set; }

    public long FileBytes { get; set; }

    public string Sha256 { get; set; } = "";

    public string TimelineJson { get; set; } = "";

    public string Error { get; set; } = "";
}

internal sealed class MatchMediaTimelineEntry
{
    public int TurnIndex { get; set; }

    public long EventSequence { get; set; }

    public long VideoMilliseconds { get; set; }
}

internal static class MatchReplayExportStates
{
    internal const string Preparing = "Preparing";
    internal const string Rendering = "Rendering";
    internal const string Encoding = "Encoding";
    internal const string Completed = "Completed";
    internal const string Failed = "Failed";
    internal const string Cancelled = "Cancelled";
    internal const string Interrupted = "Interrupted";
}

internal sealed class MatchReplayExportJob
{
    public string JobId { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string State { get; set; } = MatchReplayExportStates.Preparing;

    public float Progress { get; set; }

    public string OutputPath { get; set; } = "";

    public string Message { get; set; } = "";

    public long EstimatedBytes { get; set; }
}

internal sealed class MatchReplayPackageManifest
{
    public string Format { get; set; } = "AuraTools.MatchReplay";

    public int PackageVersion { get; set; } = 1;

    public string ExportedUtc { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string RecordSha256 { get; set; } = "";

    public string AnalysisSha256 { get; set; } = "";

    public Dictionary<string, string> ChunkSha256 { get; set; } = new(StringComparer.Ordinal);

    public string ContentSha256 { get; set; } = "";
}

internal sealed class MatchReplayPackageChunk
{
    public int ChunkIndex { get; set; }

    public long FirstSequence { get; set; }

    public long LastSequence { get; set; }

    public int FirstTurnIndex { get; set; }

    public int LastTurnIndex { get; set; }

    public string EntryName { get; set; } = "";
}

internal sealed class MatchReplayImportPreview
{
    public string Path { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public long PackageBytes { get; set; }

    public int ReplayProtocol { get; set; }

    public string Compatibility { get; set; } = "";

    public string CompatibilityMessage { get; set; } = "";

    public bool Duplicate { get; set; }

    public string ContentSha256 { get; set; } = "";

    public List<string> ContentDependencies { get; set; } = new();

    public string PrivacySummary { get; set; } = "";

    public string Tags { get; set; } = "";

    public string Notes { get; set; } = "";
}
