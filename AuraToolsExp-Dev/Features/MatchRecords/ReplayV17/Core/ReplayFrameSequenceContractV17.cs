using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>
/// Defines the native frame-order contract used by both capture and playback.
/// FrameNames is an ordered sequence, not a set: repeated names are legal and
/// retain the native frame count and timing of resource aliases.
/// </summary>
internal static class ReplayFrameSequenceContractV17
{
    internal const int MaximumFrameNameLength = 512;

    internal static string ValidateNames(IReadOnlyList<string>? frameNames, bool required)
    {
        if (frameNames == null) return "frame-sequence-null";
        if (frameNames.Count == 0) return required ? "frame-sequence-empty" : "";
        if (frameNames.Count > ReplayLimitsV17.MaximumFramesPerAnimation)
            return "frame-sequence-count-exceeded";
        for (var index = 0; index < frameNames.Count; index++)
        {
            var name = frameNames[index] ?? "";
            if (string.IsNullOrWhiteSpace(name)) return "frame-name-empty:" + index;
            if (name.Length > MaximumFrameNameLength) return "frame-name-too-long:" + index;
            if (name.IndexOf('\0') >= 0) return "frame-name-invalid:" + index;
        }
        return "";
    }

    internal static bool TryResolveOrdered<T>(
        IReadOnlyList<T>? available,
        Func<T, string> name,
        IReadOnlyList<string>? expectedNames,
        out List<T> resolved,
        out string error)
    {
        resolved = new List<T>();
        error = ValidateNames(expectedNames, required: true);
        if (error.Length > 0) return false;
        if (available == null)
        {
            error = "resource-frame-sequence-null";
            return false;
        }
        if (available.Count != expectedNames!.Count)
        {
            error = "resource-frame-count-mismatch:" + expectedNames.Count + ":" + available.Count;
            return false;
        }
        resolved.Capacity = available.Count;
        for (var index = 0; index < available.Count; index++)
        {
            var actualName = name(available[index]) ?? "";
            if (!string.Equals(actualName, expectedNames[index], StringComparison.Ordinal))
            {
                resolved.Clear();
                error = "resource-frame-name-mismatch:" + index;
                return false;
            }
            resolved.Add(available[index]);
        }
        return true;
    }
}

/// <summary>
/// Exact managed equivalent of Witch.Core.NaturalStringComparer. Keeping this
/// in the pure replay core prevents capture and playback from disagreeing on
/// equal-name occurrence order.
/// </summary>
internal sealed class ReplayNativeFrameNameComparerV17 : IComparer<string>
{
    private static readonly Regex NumberPattern = new("(\\d+)", RegexOptions.CultureInvariant);

    internal static readonly ReplayNativeFrameNameComparerV17 Instance = new();

    public int Compare(string? left, string? right)
    {
        if (left == null || right == null) return 0;
        var leftParts = NumberPattern.Split(left.Replace(" ", ""));
        var rightParts = NumberPattern.Split(right.Replace(" ", ""));
        var index = 0;
        while (true)
        {
            if (index == leftParts.Length && index == rightParts.Length) return 0;
            if (index == leftParts.Length) return -1;
            if (index == rightParts.Length) return 1;
            if (!string.Equals(leftParts[index], rightParts[index], StringComparison.Ordinal)) break;
            index++;
        }
        return int.TryParse(leftParts[index], out var leftNumber)
               && int.TryParse(rightParts[index], out var rightNumber)
            ? leftNumber.CompareTo(rightNumber)
            : string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase);
    }
}
