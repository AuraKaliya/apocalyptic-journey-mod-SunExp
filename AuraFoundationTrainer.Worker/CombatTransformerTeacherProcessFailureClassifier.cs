using System;
using AuraCombatAi.Shared;

internal sealed class CombatTransformerTeacherProcessFailureClassification
{
    public string FailureKind { get; init; } =
        CombatTransformerTeacherFailureKinds.Process;

    public bool Retryable { get; init; } = true;

    public bool FormalModelBlocked { get; init; }
}

internal static class CombatTransformerTeacherProcessFailureClassifier
{
    public static CombatTransformerTeacherProcessFailureClassification Classify(
        string? standardError)
    {
        var error = standardError ?? "";
        if (ContainsAny(
                error,
                "CUDA out of memory",
                "torch.OutOfMemoryError",
                "CUDNN_STATUS_ALLOC_FAILED",
                "CUBLAS_STATUS_ALLOC_FAILED",
                "DefaultCPUAllocator: not enough memory",
                "cannot allocate memory"))
        {
            return new CombatTransformerTeacherProcessFailureClassification
            {
                FailureKind =
                    CombatTransformerTeacherFailureKinds.TransientResource,
                Retryable = true,
                FormalModelBlocked = false
            };
        }

        if (ContainsAny(
                error,
                "Seed must be between 0 and 2**32 - 1",
                "ModuleNotFoundError:",
                "No module named",
                "Torch not compiled with CUDA enabled",
                "invalid device ordinal",
                "SyntaxError:",
                "IndentationError:"))
        {
            return new CombatTransformerTeacherProcessFailureClassification
            {
                FailureKind =
                    CombatTransformerTeacherFailureKinds.Configuration,
                Retryable = false,
                FormalModelBlocked = true
            };
        }

        if (ContainsAny(
                error,
                "train_teacher.py: error:",
                "unrecognized arguments:",
                "the following arguments are required:",
                "invalid int value:",
                "invalid float value:"))
        {
            return new CombatTransformerTeacherProcessFailureClassification
            {
                FailureKind = CombatTransformerTeacherFailureKinds.Protocol,
                Retryable = false,
                FormalModelBlocked = true
            };
        }

        // Unknown process failures deliberately remain retryable. In
        // particular, data-dependent failures must not be promoted to a
        // permanent training veto by a broad traceback or ValueError match.
        return new CombatTransformerTeacherProcessFailureClassification();
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }
}
