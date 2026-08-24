using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public enum ProjectionCardExecutionMode
{
    Unsupported,
    ActorSafe,
    VirtualDeckAdapter
}

public sealed class ProjectionCardExecutionDeclaration
{
    public string CardId { get; set; } = "";
    public ProjectionCardExecutionMode Mode { get; set; }
    public bool LifecycleSafe { get; set; }
}

/// <summary>
/// Content-owned capability declaration. This answers what a projection may
/// execute; the shared combat AI remains responsible only for how to choose.
/// </summary>
public static class ProjectionCardExecutionPolicy
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ProjectionCardExecutionDeclaration> Declarations =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ProjectionCardExecutionDeclaration declaration)
    {
        if (declaration == null || string.IsNullOrWhiteSpace(declaration.CardId))
        {
            return;
        }
        lock (SyncRoot)
        {
            Declarations[TerriasContentIdCompatibility.Canonicalize(declaration.CardId)] = declaration;
        }
    }

    public static ProjectionCardExecutionDeclaration Resolve(
        IDataConfig? config,
        string cardId,
        string script)
    {
        return Resolve(
            config?.data,
            config?.Vars,
            cardId,
            script);
    }

    internal static ProjectionCardExecutionDeclaration Resolve(
        IEnumerable<KeyValuePair<string, string>>? data,
        IEnumerable<KeyValuePair<string, string>>? vars,
        string cardId,
        string script)
    {
        var canonical = TerriasContentIdCompatibility.Canonicalize(cardId);
        lock (SyncRoot)
        {
            if (Declarations.TryGetValue(canonical, out var registered))
            {
                return registered;
            }
        }

        var declared = Read(
            vars,
            "TerriasProjectionExecutionMode",
            Read(data, "TerriasProjectionExecutionMode"));
        if (Enum.TryParse(declared, true, out ProjectionCardExecutionMode parsed))
        {
            return new ProjectionCardExecutionDeclaration
            {
                CardId = canonical,
                Mode = parsed,
                LifecycleSafe = Read(
                        vars,
                        "TerriasProjectionLifecycleSafe",
                        Read(data, "TerriasProjectionLifecycleSafe"))
                    .Equals("True", StringComparison.OrdinalIgnoreCase)
            };
        }

        if (ProjectionWrappedCardPolicy.IsProjectionStateCard(
                TerriasContentIdCompatibility.LocalId(cardId)))
        {
            return new ProjectionCardExecutionDeclaration
            {
                CardId = canonical,
                Mode = ProjectionCardExecutionMode.VirtualDeckAdapter,
                LifecycleSafe = true
            };
        }
        script ??= "";
        if (script.IndexOf("CS.", StringComparison.Ordinal) < 0
            || ProjectionWrappedCardPolicy.IsHeadlessSafe(cardId, script))
        {
            return new ProjectionCardExecutionDeclaration
            {
                CardId = canonical,
                Mode = ProjectionCardExecutionMode.ActorSafe,
                LifecycleSafe = true
            };
        }
        return new ProjectionCardExecutionDeclaration
        {
            CardId = canonical,
            Mode = ProjectionCardExecutionMode.Unsupported,
            LifecycleSafe = false
        };
    }

    internal static bool IsHeadlessScriptSurfaceSafe(string script, out string reason)
    {
        var unsupported = new[]
        {
            "ChooseCard", "SelectCard", "DeckUI", "FightUI.",
            "FightCardManager", "FightPlayer", "GetCard(", "CreateCard(",
            "BurnCard(", "ThrowCard(", "ChangeCard", "TransformCard",
            "CurPowerCount", "ChangePower(", "GainPower("
        };
        var token = unsupported.FirstOrDefault(value =>
            (script ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        if (token != null)
        {
            reason = "projection card requires player-only behavior: " + token;
            return false;
        }
        reason = "";
        return true;
    }

    private static string Read(
        IEnumerable<KeyValuePair<string, string>>? source,
        string key,
        string fallback = "")
    {
        if (source != null)
        {
            foreach (var pair in source)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    return pair.Value ?? "";
                }
            }
        }
        return fallback ?? "";
    }
}
