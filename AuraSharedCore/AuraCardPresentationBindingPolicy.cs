using System;
using System.Collections.Generic;

namespace AuraShared.Core;

internal sealed class AuraCardPresentationBindingCandidate
{
    internal object? Root { get; set; }
    internal object? Config { get; set; }
    internal object? Card { get; set; }
    internal string RootInstanceId { get; set; } = "";
    internal string ConfigInstanceId { get; set; } = "";
    internal bool SameSource { get; set; }
    internal bool ExplicitPair { get; set; }
}

internal static class AuraCardPresentationBindingPolicy
{
    internal static bool TrySelectExact(
        IEnumerable<AuraCardPresentationBindingCandidate>? candidates,
        out AuraCardPresentationBindingCandidate binding)
    {
        foreach (var candidate in candidates ?? Array.Empty<AuraCardPresentationBindingCandidate>())
        {
            if (candidate?.Root == null || candidate.Config == null) continue;
            if (candidate.ExplicitPair)
            {
                binding = candidate;
                return true;
            }
            if (!candidate.SameSource) continue;
            if (candidate.RootInstanceId.Length > 0
                && candidate.ConfigInstanceId.Length > 0
                && !string.Equals(
                    candidate.RootInstanceId,
                    candidate.ConfigInstanceId,
                    StringComparison.Ordinal))
                continue;
            binding = candidate;
            return true;
        }

        binding = new AuraCardPresentationBindingCandidate();
        return false;
    }
}
