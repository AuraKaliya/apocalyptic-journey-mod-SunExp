using System;
using System.Collections.Generic;
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
        var canonical = TerriasContentIdCompatibility.Canonicalize(cardId);
        lock (SyncRoot)
        {
            if (Declarations.TryGetValue(canonical, out var registered))
            {
                return registered;
            }
        }

        var declared = DictionaryUtil.Get(
            config?.Vars,
            "TerriasProjectionExecutionMode",
            DictionaryUtil.Get(config?.data, "TerriasProjectionExecutionMode"));
        if (Enum.TryParse(declared, true, out ProjectionCardExecutionMode parsed))
        {
            return new ProjectionCardExecutionDeclaration
            {
                CardId = canonical,
                Mode = parsed,
                LifecycleSafe = DictionaryUtil.Get(
                        config?.Vars,
                        "TerriasProjectionLifecycleSafe",
                        DictionaryUtil.Get(config?.data, "TerriasProjectionLifecycleSafe"))
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
}
