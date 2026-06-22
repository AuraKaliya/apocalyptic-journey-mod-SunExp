using System;
using System.Linq;
using AuraShared.Core;
using Witch.Mod;

namespace AuraJourney.Shared;

public static class AuraJourneyRuntime
{
    public static string Initialize(ModConfig modConfig, string ownerModId)
    {
        var root = AuraSharedRuntime.Initialize(modConfig, ownerModId);
        AuraSharedDiagnostics.Info(AuraJourneyConstants.SystemName, ownerModId, "Initialize", "AuraJourneyShared initialized.");
        return root;
    }

    public static AuraSharedConfigWriteResult RegisterJourney(string ownerModId, AuraJourneyDefinition definition)
    {
        NormalizeDefinition(ownerModId, definition);
        var fileName = DefinitionFileName(definition.JourneyId);
        var result = AuraSharedConfigStore.WriteShared(
            ownerModId,
            AuraJourneyConstants.SystemName,
            fileName,
            definition,
            schemaVersion: AuraJourneyConstants.DefinitionSchemaVersion);

        if (result.Success)
        {
            AuraSharedRegistry.RegisterResource(ownerModId, new AuraSharedResourceRecord
            {
                System = AuraJourneyConstants.SystemName,
                ResourceId = definition.JourneyId,
                OwnerModId = ownerModId,
                Kind = "JourneyDefinition",
                Tags = definition.Tags.ToArray()
            });
        }

        AuraSharedDiagnostics.Write(AuraSharedDiagnostics.Create(
            AuraJourneyConstants.SystemName,
            ownerModId,
            result.Success ? "Info" : "Warn",
            "RegisterJourney",
            result.Success ? "Journey registered: " + definition.JourneyId : "Journey registration failed: " + result.Message,
            isAuthority: true,
            correlationId: definition.JourneyId));
        return result;
    }

    public static AuraSharedConfigSnapshot<AuraJourneyDefinition> ReadJourney(string callerId, string journeyId)
    {
        return AuraSharedConfigStore.ReadShared(
            callerId,
            AuraJourneyConstants.SystemName,
            DefinitionFileName(journeyId),
            new AuraJourneyDefinition { JourneyId = journeyId ?? "" });
    }

    public static AuraSharedConfigSnapshot<AuraJourneyState> ReadState(string callerId, string journeyId)
    {
        return AuraSharedConfigStore.ReadRuntime(
            callerId,
            AuraJourneyConstants.SystemName,
            StateFileName(journeyId),
            new AuraJourneyState { JourneyId = journeyId ?? "" });
    }

    public static AuraJourneyCommitResult TryCommit(AuraJourneyCommitRequest request)
    {
        try
        {
            if (request == null)
            {
                return Failure("Journey commit request is null.");
            }

            request.JourneyId = (request.JourneyId ?? "").Trim();
            request.OwnerModId = (request.OwnerModId ?? "").Trim();
            request.AuthorityId = string.IsNullOrWhiteSpace(request.AuthorityId) ? request.OwnerModId : request.AuthorityId.Trim();
            if (string.IsNullOrWhiteSpace(request.JourneyId) || string.IsNullOrWhiteSpace(request.OwnerModId))
            {
                return Failure("JourneyId and ownerModId are required.");
            }

            if (!request.IsAuthority)
            {
                AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, request.OwnerModId, "Commit", "Non-authority journey commit rejected.", false, request.JourneyId);
                return Failure("Only the authoritative side may advance journey state.");
            }

            var snapshot = ReadState(request.OwnerModId, request.JourneyId);
            var next = AuraJourneyStateReducer.Apply(snapshot.Value, request, DateTime.UtcNow);
            var expectedRevision = request.ExpectedRevision >= 0 ? request.ExpectedRevision : snapshot.Revision;
            var write = AuraSharedConfigStore.WriteRuntime(
                request.AuthorityId,
                AuraJourneyConstants.SystemName,
                StateFileName(request.JourneyId),
                next,
                expectedRevision,
                AuraJourneyConstants.StateSchemaVersion);

            AuraSharedDiagnostics.Write(AuraSharedDiagnostics.Create(
                AuraJourneyConstants.SystemName,
                request.OwnerModId,
                write.Success ? "Info" : "Warn",
                "Commit",
                write.Success ? "Journey state advanced: " + request.Action : "Journey state commit failed: " + write.Message,
                true,
                request.JourneyId));

            return new AuraJourneyCommitResult
            {
                Success = write.Success,
                Conflict = write.Conflict,
                Revision = write.Revision,
                State = next,
                Message = write.Message
            };
        }
        catch (Exception ex)
        {
            AuraSharedDiagnostics.Error(AuraJourneyConstants.SystemName, request?.OwnerModId ?? "", "Commit", "Journey commit failed.", ex, request?.IsAuthority, request?.JourneyId ?? "");
            return Failure(ex.Message);
        }
    }

    public static bool IsNodeAvailable(AuraJourneyNodeDefinition node, AuraJourneyConditionContext context)
    {
        return AuraJourneyConditionEvaluator.EvaluateAll(node?.Conditions, context);
    }

    private static void NormalizeDefinition(string ownerModId, AuraJourneyDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        definition.SchemaVersion = AuraJourneyConstants.DefinitionSchemaVersion;
        definition.OwnerModId = string.IsNullOrWhiteSpace(definition.OwnerModId) ? ownerModId : definition.OwnerModId.Trim();
        definition.JourneyId = (definition.JourneyId ?? "").Trim();
        definition.DisplayName = (definition.DisplayName ?? "").Trim();
        definition.EntryNodeId = (definition.EntryNodeId ?? "").Trim();
        definition.Tags ??= new System.Collections.Generic.List<string>();
        definition.Nodes ??= new System.Collections.Generic.List<AuraJourneyNodeDefinition>();
        if (string.IsNullOrWhiteSpace(definition.JourneyId))
        {
            throw new InvalidOperationException("JourneyId is required.");
        }
    }

    private static string DefinitionFileName(string journeyId)
    {
        return AuraSharedIdentity.SafeId(journeyId, "journey") + ".definition.json";
    }

    private static string StateFileName(string journeyId)
    {
        return AuraSharedIdentity.SafeId(journeyId, "journey") + ".state.json";
    }

    private static AuraJourneyCommitResult Failure(string message)
    {
        return new AuraJourneyCommitResult
        {
            Success = false,
            Message = message ?? ""
        };
    }
}
