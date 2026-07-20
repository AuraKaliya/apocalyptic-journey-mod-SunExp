using System;
using System.Collections.Generic;

namespace AuraGameData.Shared.Application;

public static class AuraGameAggregateKinds
{
    public const string CardZone = "CardZone";
    public const string RelicInventory = "RelicInventory";
    public const string RoleRoster = "RoleRoster";
}

public sealed class AuraGameMutationContext
{
    public string RequesterModId { get; set; } = "";

    public string Source { get; set; } = "";

    public string SessionId { get; set; } = "";

    public bool Authoritative { get; set; } = true;

    public void Normalize()
    {
        RequesterModId = (RequesterModId ?? "").Trim();
        Source = (Source ?? "").Trim();
        SessionId = (SessionId ?? "").Trim();
    }
}

public sealed class AuraCardGrantCommand
{
    public AuraGameDataDefinitionHandle? Definition { get; set; }

    public string TargetZone { get; set; } = "Hand";

    public string RuntimeTags { get; set; } = "";

    public Dictionary<string, string> Vars { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Presentation { get; set; } = new(StringComparer.Ordinal);

    public AuraGameMutationContext Context { get; set; } = new();
}

public sealed class AuraCardRemoveCommand
{
    public string InstanceId { get; set; } = "";

    public string Zone { get; set; } = "";

    public AuraGameMutationContext Context { get; set; } = new();
}

public sealed class AuraRelicGrantCommand
{
    public AuraGameDataDefinitionHandle? Definition { get; set; }

    public bool PreferEquippedSlot { get; set; } = true;

    public Dictionary<string, string> Vars { get; set; } = new(StringComparer.Ordinal);

    public AuraGameMutationContext Context { get; set; } = new();
}

public sealed class AuraRelicRemoveCommand
{
    public string InstanceId { get; set; } = "";

    public AuraGameMutationContext Context { get; set; } = new();
}

public sealed class AuraGameInstanceCommandResult
{
    public bool Success { get; set; }

    public bool RolledBack { get; set; }

    public string AggregateKind { get; set; } = "";

    public string FailureStep { get; set; } = "";

    public string Message { get; set; } = "";

    public AuraGameDataInstanceSnapshot? Instance { get; set; }

    public static AuraGameInstanceCommandResult Fail(string aggregateKind, string step, string message, bool rolledBack = false)
    {
        return new AuraGameInstanceCommandResult
        {
            AggregateKind = aggregateKind ?? "",
            FailureStep = step ?? "",
            Message = message ?? "",
            RolledBack = rolledBack
        };
    }
}

public interface IAuraCardInstancePort
{
    AuraGameInstanceCommandResult Grant(AuraCardGrantCommand command);

    AuraGameInstanceCommandResult Remove(AuraCardRemoveCommand command);
}

public interface IAuraRelicInstancePort
{
    AuraGameInstanceCommandResult Grant(AuraRelicGrantCommand command);

    AuraGameInstanceCommandResult Remove(AuraRelicRemoveCommand command);
}

public sealed class AuraCardInstanceService
{
    private readonly IAuraCardInstancePort port;

    public AuraCardInstanceService(IAuraCardInstancePort port)
    {
        this.port = port ?? throw new ArgumentNullException(nameof(port));
    }

    public AuraGameInstanceCommandResult GrantToHand(AuraCardGrantCommand command)
    {
        return GrantToZone(command, "Hand");
    }

    public AuraGameInstanceCommandResult GrantToOwnedDeck(AuraCardGrantCommand command)
    {
        return GrantToZone(command, "OwnedDeck");
    }

    public AuraGameInstanceCommandResult GrantToReserveDeck(AuraCardGrantCommand command)
    {
        return GrantToZone(command, "ReserveDeck");
    }

    public AuraGameInstanceCommandResult GrantToManagerDraw(AuraCardGrantCommand command)
    {
        return GrantToZone(command, "ManagerDraw");
    }

    private AuraGameInstanceCommandResult GrantToZone(AuraCardGrantCommand command, string zone)
    {
        if (!Validate(command?.Context, command?.Definition, out var failure))
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "validate", failure);
        }

        command!.TargetZone = zone;
        return port.Grant(command);
    }

    public AuraGameInstanceCommandResult Remove(AuraCardRemoveCommand command)
    {
        command ??= new AuraCardRemoveCommand();
        command.Context.Normalize();
        if (string.IsNullOrWhiteSpace(command.Context.RequesterModId)
            || string.IsNullOrWhiteSpace(command.InstanceId))
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "validate", "Requester and instanceId are required.");
        }

        if (!command.Context.Authoritative)
        {
            return AuraGameInstanceCommandResult.Fail(AuraGameAggregateKinds.CardZone, "authority", "Card-zone mutation requires authority.");
        }

        return port.Remove(command);
    }

    private static bool Validate(
        AuraGameMutationContext? context,
        AuraGameDataDefinitionHandle? definition,
        out string failure)
    {
        context ??= new AuraGameMutationContext();
        context.Normalize();
        if (string.IsNullOrWhiteSpace(context.RequesterModId))
        {
            failure = "requesterModId is required.";
            return false;
        }

        if (!context.Authoritative)
        {
            failure = "Card-zone mutation requires authority.";
            return false;
        }

        if (definition == null || !AuraGameDataCatalogRuntime.ValidateHandle(definition, out _))
        {
            failure = "A current registered definition handle is required.";
            return false;
        }

        failure = "";
        return true;
    }
}

public sealed class AuraRelicInstanceService
{
    private readonly IAuraRelicInstancePort port;

    public AuraRelicInstanceService(IAuraRelicInstancePort port)
    {
        this.port = port ?? throw new ArgumentNullException(nameof(port));
    }

    public AuraGameInstanceCommandResult Grant(AuraRelicGrantCommand command)
    {
        command ??= new AuraRelicGrantCommand();
        command.Context.Normalize();
        if (string.IsNullOrWhiteSpace(command.Context.RequesterModId)
            || !command.Context.Authoritative
            || command.Definition == null
            || !AuraGameDataCatalogRuntime.ValidateHandle(command.Definition, out var snapshot)
            || snapshot == null
            || !string.Equals(snapshot.Key.DataType, "Relic", StringComparison.OrdinalIgnoreCase))
        {
            return AuraGameInstanceCommandResult.Fail(
                AuraGameAggregateKinds.RelicInventory,
                "validate",
                "An authoritative requester and current Relic definition handle are required.");
        }

        return port.Grant(command);
    }

    public AuraGameInstanceCommandResult Remove(AuraRelicRemoveCommand command)
    {
        command ??= new AuraRelicRemoveCommand();
        command.Context.Normalize();
        if (string.IsNullOrWhiteSpace(command.Context.RequesterModId)
            || !command.Context.Authoritative
            || string.IsNullOrWhiteSpace(command.InstanceId))
        {
            return AuraGameInstanceCommandResult.Fail(
                AuraGameAggregateKinds.RelicInventory,
                "validate",
                "An authoritative requester and instanceId are required.");
        }

        return port.Remove(command);
    }
}
