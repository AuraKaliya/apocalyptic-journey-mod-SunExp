using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public readonly struct ProjectionDeckCardCapability
{
    public ProjectionDeckCardCapability(bool actorSafe, string reason = "")
    {
        ActorSafe = actorSafe;
        Reason = reason ?? "";
    }

    public bool ActorSafe { get; }

    public string Reason { get; }

    public static ProjectionDeckCardCapability Safe()
    {
        return new ProjectionDeckCardCapability(true);
    }

    public static ProjectionDeckCardCapability Reject(string reason)
    {
        return new ProjectionDeckCardCapability(false, reason);
    }
}

public sealed class ProjectionActorDeckResult
{
    public bool Success { get; set; }

    public ProjectionDeckRecipe? EffectiveRecipe { get; set; }

    public IReadOnlyList<ProjectionDeckCardRecipe> RejectedCards { get; set; } =
        Array.Empty<ProjectionDeckCardRecipe>();

    public bool UsesBasicAction { get; set; }

    public string FailureReason { get; set; } = "";
}

public static class ProjectionActorDeckProjection
{
    public static ProjectionActorDeckResult Build(
        ProjectionDeckRecipe? source,
        Func<ProjectionDeckCardRecipe, ProjectionDeckCardCapability> inspect,
        ProjectionDeckCardRecipe basicAction)
    {
        if (source == null || source.Cards.Count == 0)
        {
            return Fail("projection role deck is unavailable");
        }
        if (inspect == null || basicAction == null || string.IsNullOrWhiteSpace(basicAction.CardId))
        {
            return Fail("projection actor deck policy is unavailable");
        }

        var accepted = new List<ProjectionDeckCardRecipe>();
        var rejected = new List<ProjectionDeckCardRecipe>();
        var capabilities = new Dictionary<string, ProjectionDeckCardCapability>(StringComparer.Ordinal);
        foreach (var card in source.Cards)
        {
            if (!capabilities.TryGetValue(card.Identity, out var capability))
            {
                capability = inspect(card);
                capabilities[card.Identity] = capability;
            }
            if (capability.ActorSafe)
            {
                accepted.Add(card);
            }
            else
            {
                rejected.Add(card);
            }
        }

        var usesBasicAction = accepted.Count == 0;
        if (usesBasicAction)
        {
            var basicCapability = inspect(basicAction);
            if (!basicCapability.ActorSafe)
            {
                return Fail("projection basic action is unavailable: " + basicCapability.Reason, rejected);
            }
            accepted.Add(basicAction);
        }

        return new ProjectionActorDeckResult
        {
            Success = true,
            EffectiveRecipe = new ProjectionDeckRecipe(accepted),
            RejectedCards = rejected.ToArray(),
            UsesBasicAction = usesBasicAction
        };
    }

    private static ProjectionActorDeckResult Fail(
        string reason,
        IReadOnlyList<ProjectionDeckCardRecipe>? rejected = null)
    {
        return new ProjectionActorDeckResult
        {
            FailureReason = reason ?? "",
            RejectedCards = rejected ?? Array.Empty<ProjectionDeckCardRecipe>()
        };
    }
}
