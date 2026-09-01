using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal static class MatchReplayProtocol
{
    internal const int Version = 17;
    internal const int MinimumSupportedVersion = 17;
}

internal static class MatchRecordCollections
{
    internal const string Auto = "Auto";
    internal const string Favorite = "Favorite";
}

internal static class MatchReplayStates
{
    internal const string Ready = "Ready";
    internal const string Recording = "Recording";
    internal const string Finalizing = "Finalizing";
    internal const string Incomplete = "Incomplete";
    internal const string SummaryOnly = "SummaryOnly";
    internal const string Rejected = "Rejected";
    internal const string Corrupt = "Corrupt";
}

internal static class MatchRecordOrigins
{
    internal const string Auto = "Auto";
    internal const string Imported = "Imported";
    internal const string Replicated = "Replicated";
}

internal sealed class MatchRecord
{
    public long Sequence { get; set; }

    public string RecordId { get; set; } = "";

    public string AdventureId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string BattleTitle { get; set; } = "";

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
}

internal static class MatchAttributionConfidence
{
    internal const string Exact = "Exact";
    internal const string Inferred = "Inferred";
    internal const string Unknown = "Unknown";
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
    internal const string Corrupt = "Corrupt";
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
    internal const string Planned = "Planned";
    internal const string Rendering = "Rendering";
    internal const string Encoding = "Encoding";
    internal const string Validating = "Validating";
    internal const string Committing = "Committing";
    internal const string Ready = "Ready";
    internal const string Corrupt = "Corrupt";
    internal const string Failed = "Failed";
    internal const string Cancelled = "Cancelled";
}

internal sealed class MatchReplayExportJob
{
    public string JobId { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string State { get; set; } = MatchReplayExportStates.Planned;

    public long Revision { get; set; }

    public float Progress { get; set; }

    public string OutputPath { get; set; } = "";

    public string StagingPath { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string OutputSha256 { get; set; } = "";

    public string ProfileId { get; set; } = "";

    public string CreatedUtc { get; set; } = "";

    public string UpdatedUtc { get; set; } = "";

    public string ErrorCode { get; set; } = "";

    public bool CancelRequested { get; set; }

    public int AttemptCount { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int FramesPerSecond { get; set; }

    public long FrameCount { get; set; }

    public long AudioSampleFrames { get; set; }

    public long FileBytes { get; set; }

    public string Message { get; set; } = "";

    public long EstimatedBytes { get; set; }
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
