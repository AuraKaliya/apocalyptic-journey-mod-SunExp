using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal enum MatchReplayLifecyclePhase
{
    Idle,
    Preparing,
    Prepared,
    Active,
    Exiting
}

internal enum MatchReplayExitKind
{
    StartFailed,
    Completed,
    Cancelled,
    RuntimeFailed,
    ExportCompleted,
    ExportFailed,
    ModuleDisabled
}

internal sealed class MatchReplayExitDecision
{
    internal MatchReplayExitDecision(bool returnToLibrary, string message)
    {
        ReturnToLibrary = returnToLibrary;
        Message = message ?? "";
    }

    internal bool ReturnToLibrary { get; }

    internal string Message { get; }
}

/// <summary>
/// Pure lifecycle contract for one replay session. The origin UI is committed only
/// after native preparation succeeds; only a committed interactive origin can be
/// reconstructed after teardown.
/// </summary>
internal sealed class MatchReplayLifecycleState
{
    internal MatchReplayLifecyclePhase Phase { get; private set; }

    internal string RecordId { get; private set; } = "";

    internal bool ReturnToLibrary { get; private set; }

    internal bool OriginCommitted { get; private set; }

    internal bool IsPlayback => Phase != MatchReplayLifecyclePhase.Idle;

    internal bool IsExiting => Phase == MatchReplayLifecyclePhase.Exiting;

    internal bool TryBeginPreparation(string recordId, bool returnToLibrary, out string message)
    {
        if (Phase != MatchReplayLifecyclePhase.Idle)
        {
            message = Phase == MatchReplayLifecyclePhase.Exiting
                ? "上一场回放仍在退出并恢复对局记录页面，请稍候再试。"
                : "已有对局正在准备或回放。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(recordId))
        {
            message = "回放记录标识为空。";
            return false;
        }

        Phase = MatchReplayLifecyclePhase.Preparing;
        RecordId = recordId.Trim();
        ReturnToLibrary = returnToLibrary;
        OriginCommitted = false;
        message = "";
        return true;
    }

    internal void MarkPrepared()
    {
        Require(MatchReplayLifecyclePhase.Preparing, "mark prepared");
        Phase = MatchReplayLifecyclePhase.Prepared;
    }

    internal void CommitOrigin()
    {
        Require(MatchReplayLifecyclePhase.Prepared, "commit origin");
        OriginCommitted = true;
    }

    internal void MarkActive()
    {
        Require(MatchReplayLifecyclePhase.Prepared, "activate replay");
        Phase = MatchReplayLifecyclePhase.Active;
    }

    internal MatchReplayExitDecision BeginExit(MatchReplayExitKind kind, string detail = "")
    {
        if (Phase == MatchReplayLifecyclePhase.Idle)
        {
            return new MatchReplayExitDecision(false, ExitMessage(kind, detail));
        }

        Phase = MatchReplayLifecyclePhase.Exiting;
        return new MatchReplayExitDecision(
            ReturnToLibrary && OriginCommitted,
            ExitMessage(kind, detail));
    }

    internal void CompleteExit()
    {
        Phase = MatchReplayLifecyclePhase.Idle;
        RecordId = "";
        ReturnToLibrary = false;
        OriginCommitted = false;
    }

    private void Require(MatchReplayLifecyclePhase expected, string operation)
    {
        if (Phase != expected)
        {
            throw new InvalidOperationException(
                "Cannot " + operation + " while replay lifecycle is " + Phase + ".");
        }
    }

    private static string ExitMessage(MatchReplayExitKind kind, string detail)
    {
        var normalized = (detail ?? "").Trim();
        return kind switch
        {
            MatchReplayExitKind.Completed => "回放已结束。",
            MatchReplayExitKind.Cancelled => "已退出回放。",
            MatchReplayExitKind.ExportCompleted => "回放视频导出已完成。",
            MatchReplayExitKind.ModuleDisabled => "回放已停止：战斗回放模块已关闭。",
            MatchReplayExitKind.StartFailed => normalized.Length == 0
                ? "无法开始回放。"
                : normalized,
            MatchReplayExitKind.ExportFailed => normalized.Length == 0
                ? "回放视频导出失败。"
                : "回放视频导出失败：" + normalized,
            _ => normalized.Length == 0
                ? "回放因运行错误而中止。"
                : "回放因运行错误而中止：" + normalized
        };
    }
}

internal static class MatchReplaySessionState
{
    private static readonly MatchReplayLifecycleState State = new();

    internal static MatchReplayLifecyclePhase Phase => State.Phase;

    internal static bool IsPlayback => State.IsPlayback;

    internal static bool IsExiting => State.IsExiting;

    internal static bool TryBeginPreparation(string recordId, bool returnToLibrary, out string message)
    {
        return State.TryBeginPreparation(recordId, returnToLibrary, out message);
    }

    internal static void MarkPrepared() => State.MarkPrepared();

    internal static void CommitOrigin() => State.CommitOrigin();

    internal static void MarkActive() => State.MarkActive();

    internal static MatchReplayExitDecision BeginExit(MatchReplayExitKind kind, string detail = "")
    {
        return State.BeginExit(kind, detail);
    }

    internal static void CompleteExit() => State.CompleteExit();
}
