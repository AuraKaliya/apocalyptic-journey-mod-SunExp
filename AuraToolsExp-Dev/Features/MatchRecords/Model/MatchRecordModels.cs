using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal static class MatchReplayProtocol
{
    internal const int Version = 2;
}

internal static class MatchRecordCollections
{
    internal const string Auto = "Auto";
    internal const string Favorite = "Favorite";
}

internal static class MatchReplayStates
{
    internal const string Ready = "Ready";
    internal const string Corrupt = "Corrupt";
}

internal static class MatchReplayEventKinds
{
    internal const string ActionCommand = "ActionCommand";
    internal const string ClientCommand = "ClientCommand";
    internal const string TargetCommand = "TargetCommand";
    internal const string StatusSnapshot = "StatusSnapshot";
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

    public string ReplayState { get; set; } = MatchReplayStates.Ready;

    public int ReplayProtocol { get; set; } = MatchReplayProtocol.Version;

    public string GameBuild { get; set; } = "";

    public string ToolBuild { get; set; } = "";

    public string ModFingerprint { get; set; } = "";

    public int EventCount { get; set; }

    public int TurnCount { get; set; }

    public long CompressedBytes { get; set; }

    public string StatisticsJson { get; set; } = "";

    public MatchReplayInitialState InitialState { get; set; } = new();
}

internal sealed class MatchReplayInitialState
{
    public string LevelId { get; set; } = "";

    public byte[] RoleQueue { get; set; } = Array.Empty<byte>();

    public byte[] TemporaryRoles { get; set; } = Array.Empty<byte>();

    public float EnemyPositive { get; set; }

    public float EnemyHp { get; set; }

    public string RoleTableJson { get; set; } = "";
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
}

internal static class MatchSemanticCategories
{
    internal const string Card = "Card";
    internal const string Damage = "Damage";
    internal const string Status = "Status";
    internal const string Target = "Target";
    internal const string Command = "Command";
}

internal sealed class MatchSemanticEvent
{
    public string Category { get; set; } = MatchSemanticCategories.Command;

    public string Action { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string Label { get; set; } = "";

    public long Value { get; set; }

    public long SecondaryValue { get; set; }

    public bool IsKeyEvent { get; set; }
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
    internal const int Version = 1;
}

internal sealed class MatchAnalysisReport
{
    public int Protocol { get; set; } = MatchAnalysisProtocol.Version;

    public string RecordId { get; set; } = "";

    public string GeneratedUtc { get; set; } = "";

    public int TurnCount { get; set; }

    public long TotalDamage { get; set; }

    public long BestTurnDamage { get; set; }

    public int BestTurnIndex { get; set; }

    public int CardUseCount { get; set; }

    public List<MatchAnalysisTurn> Turns { get; set; } = new();

    public List<MatchAnalysisCombatant> Combatants { get; set; } = new();

    public List<MatchAnalysisCard> Cards { get; set; } = new();

    public List<MatchAnalysisMoment> KeyMoments { get; set; } = new();
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
}

internal sealed class MatchReplayExportJob
{
    public string JobId { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string State { get; set; } = MatchReplayExportStates.Preparing;

    public float Progress { get; set; }

    public string OutputPath { get; set; } = "";

    public string Message { get; set; } = "";
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
