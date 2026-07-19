using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraShared.Core;

public enum AuraSharedContentIdResolutionKind
{
    Missing,
    Exact,
    ProtocolMarker,
    UniqueAlias,
    Ambiguous
}

public sealed class AuraSharedContentIdResolution
{
    public AuraSharedContentIdResolution(
        string declaredId,
        string resolvedId,
        AuraSharedContentIdResolutionKind kind,
        IReadOnlyList<string>? matches = null)
    {
        DeclaredId = declaredId ?? "";
        ResolvedId = resolvedId ?? "";
        Kind = kind;
        Matches = matches ?? Array.Empty<string>();
    }

    public string DeclaredId { get; }

    public string ResolvedId { get; }

    public AuraSharedContentIdResolutionKind Kind { get; }

    public IReadOnlyList<string> Matches { get; }

    public bool Success => Kind == AuraSharedContentIdResolutionKind.Exact
                           || Kind == AuraSharedContentIdResolutionKind.ProtocolMarker
                           || Kind == AuraSharedContentIdResolutionKind.UniqueAlias;
}

public static class AuraSharedContentId
{
    public static string NormalizeProtocolMarkers(string? value)
    {
        var normalized = (value ?? "").Trim();
        return string.Equals(normalized, "*", StringComparison.Ordinal)
            ? normalized
            : normalized.Replace("*", "");
    }

    public static bool Matches(
        string? declaredId,
        string? runtimeId,
        string? ownerModId = null,
        params string[] knownPrefixes)
    {
        var declared = (declaredId ?? "").Trim();
        var runtime = (runtimeId ?? "").Trim();
        if (string.Equals(declared, "*", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(declared, runtime, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedDeclared = NormalizeProtocolMarkers(declared);
        var normalizedRuntime = NormalizeProtocolMarkers(runtime);
        if (string.Equals(normalizedDeclared, normalizedRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var declaredAliases = BuildScopedAliases(normalizedDeclared, ownerModId, knownPrefixes);
        var runtimeAliases = BuildScopedAliases(normalizedRuntime, ownerModId, knownPrefixes);
        return declaredAliases.Overlaps(runtimeAliases);
    }

    public static AuraSharedContentIdResolution Resolve(
        string? declaredId,
        IEnumerable<string>? availableIds,
        string? ownerModId = null,
        params string[] knownPrefixes)
    {
        var declared = (declaredId ?? "").Trim();
        var available = (availableIds ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (declared.Length == 0 || available.Count == 0)
        {
            return new AuraSharedContentIdResolution(declared, "", AuraSharedContentIdResolutionKind.Missing);
        }

        var exact = available.FirstOrDefault(value => string.Equals(value, declared, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return new AuraSharedContentIdResolution(declared, exact, AuraSharedContentIdResolutionKind.Exact, new[] { exact });
        }

        var normalizedDeclared = NormalizeProtocolMarkers(declared);
        var markerMatches = available
            .Where(value => string.Equals(
                NormalizeProtocolMarkers(value),
                normalizedDeclared,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (markerMatches.Count == 1)
        {
            return new AuraSharedContentIdResolution(
                declared,
                markerMatches[0],
                AuraSharedContentIdResolutionKind.ProtocolMarker,
                markerMatches);
        }

        if (markerMatches.Count > 1)
        {
            return new AuraSharedContentIdResolution(
                declared,
                "",
                AuraSharedContentIdResolutionKind.Ambiguous,
                markerMatches);
        }

        var declaredAliases = BuildScopedAliases(normalizedDeclared, ownerModId, knownPrefixes);
        var aliasMatches = available
            .Where(value => declaredAliases.Overlaps(BuildAliases(
                NormalizeProtocolMarkers(value),
                ownerModId,
                knownPrefixes)))
            .ToList();
        return aliasMatches.Count switch
        {
            1 => new AuraSharedContentIdResolution(
                declared,
                aliasMatches[0],
                AuraSharedContentIdResolutionKind.UniqueAlias,
                aliasMatches),
            > 1 => new AuraSharedContentIdResolution(
                declared,
                "",
                AuraSharedContentIdResolutionKind.Ambiguous,
                aliasMatches),
            _ => new AuraSharedContentIdResolution(
                declared,
                "",
                AuraSharedContentIdResolutionKind.Missing)
        };
    }

    private static HashSet<string> BuildScopedAliases(
        string value,
        string? ownerModId,
        IEnumerable<string>? knownPrefixes)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(aliases, value);

        var owner = (ownerModId ?? "").Trim().TrimEnd('_');
        if (owner.Length > 0)
        {
            var ownerPrefix = owner + "_";
            if (value.StartsWith(ownerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Add(aliases, value.Substring(ownerPrefix.Length));
            }
            else
            {
                Add(aliases, ownerPrefix + value);
            }
        }

        foreach (var rawPrefix in knownPrefixes ?? Array.Empty<string>())
        {
            var prefix = (rawPrefix ?? "").Trim();
            if (prefix.Length == 0)
            {
                continue;
            }

            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Add(aliases, value.Substring(prefix.Length));
            }
            else if (AuraSharedIdentity.IsUnsignedNumber(value))
            {
                Add(aliases, prefix + value);
            }
        }

        return aliases;
    }

    private static HashSet<string> BuildAliases(
        string value,
        string? ownerModId,
        IEnumerable<string>? knownPrefixes)
    {
        var aliases = BuildScopedAliases(value, ownerModId, knownPrefixes);
        for (var index = value.IndexOf('_'); index >= 0 && index + 1 < value.Length; index = value.IndexOf('_', index + 1))
        {
            Add(aliases, value.Substring(index + 1));
        }

        return aliases;
    }

    private static void Add(ISet<string> values, string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length > 0)
        {
            values.Add(normalized);
        }
    }
}
