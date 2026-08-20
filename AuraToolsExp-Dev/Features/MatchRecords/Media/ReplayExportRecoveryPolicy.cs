using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class ReplayExportRecoveryActions
{
    internal const string ResumeRendering = "ResumeRendering";
    internal const string FailAndDeletePartial = "FailAndDeletePartial";
    internal const string ValidatePartial = "ValidatePartial";
    internal const string ResumeCommit = "ResumeCommit";
    internal const string VerifyReady = "VerifyReady";
    internal const string CleanupTerminal = "CleanupTerminal";
}

internal static class ReplayExportRecoveryPolicy
{
    internal static string Resolve(string state, bool stagingExists, bool targetExists)
    {
        if (state == MatchReplayExportStates.Planned) return ReplayExportRecoveryActions.ResumeRendering;
        if (state == MatchReplayExportStates.Rendering || state == MatchReplayExportStates.Encoding)
        {
            return ReplayExportRecoveryActions.FailAndDeletePartial;
        }
        if (state == MatchReplayExportStates.Validating)
        {
            return stagingExists || targetExists
                ? ReplayExportRecoveryActions.ValidatePartial
                : ReplayExportRecoveryActions.FailAndDeletePartial;
        }
        if (state == MatchReplayExportStates.Committing)
        {
            return stagingExists || targetExists
                ? ReplayExportRecoveryActions.ResumeCommit
                : ReplayExportRecoveryActions.FailAndDeletePartial;
        }
        if (state == MatchReplayExportStates.Ready) return ReplayExportRecoveryActions.VerifyReady;
        return ReplayExportRecoveryActions.CleanupTerminal;
    }
}
