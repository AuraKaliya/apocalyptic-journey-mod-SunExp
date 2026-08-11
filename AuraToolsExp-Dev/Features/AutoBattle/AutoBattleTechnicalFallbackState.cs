using System;

namespace AuraToolsExp.Dll.Features.AutoBattle;

/// <summary>
/// Tracks availability-only fallbacks. Quality signals must never enter this
/// state: it is reserved for load, inference and execution progress failures.
/// </summary>
internal sealed class AutoBattleTechnicalFallbackState
{
    public const int IsolationFailureThreshold = 3;

    public int ConsecutiveFailures { get; private set; }

    public int FallbackDecisionCount { get; private set; }

    public bool IsolatedForBattle { get; private set; }

    public bool FallbackPending { get; private set; }

    public string LastReason { get; private set; } = "";

    public bool ShouldUseEmergencyBaseline =>
        IsolatedForBattle || FallbackPending;

    public void ResetBattle(bool modelAvailable, string unavailableReason = "")
    {
        ConsecutiveFailures = 0;
        FallbackDecisionCount = 0;
        FallbackPending = false;
        IsolatedForBattle = !modelAvailable;
        LastReason = modelAvailable
            ? ""
            : NormalizeReason("model-load-failed", unavailableReason);
    }

    public void ModelRecovered()
    {
        ConsecutiveFailures = 0;
        FallbackPending = false;
        IsolatedForBattle = false;
    }

    public void ReportFailure(
        string kind,
        string detail,
        bool isolateImmediately = false)
    {
        ConsecutiveFailures++;
        LastReason = NormalizeReason(kind, detail);
        IsolatedForBattle = isolateImmediately
                            || ConsecutiveFailures
                            >= IsolationFailureThreshold;
        FallbackPending = !IsolatedForBattle;
    }

    public bool TryConsumeEmergencyFallback()
    {
        if (!ShouldUseEmergencyBaseline)
        {
            return false;
        }

        FallbackDecisionCount++;
        if (!IsolatedForBattle)
        {
            FallbackPending = false;
        }
        return true;
    }

    public void ReportModelProgress()
    {
        if (IsolatedForBattle)
        {
            return;
        }
        ConsecutiveFailures = 0;
        FallbackPending = false;
    }

    private static string NormalizeReason(string kind, string detail)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(kind)
            ? "runtime-failure"
            : kind.Trim();
        var normalizedDetail = (detail ?? "").Trim();
        return string.IsNullOrWhiteSpace(normalizedDetail)
            ? normalizedKind
            : normalizedKind + "：" + normalizedDetail;
    }
}
