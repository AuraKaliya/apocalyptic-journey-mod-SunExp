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
        AuraJourneyCurrentNodeProjectionRuntime.Initialize(modConfig, ownerModId);
        AuraSharedDiagnostics.Info(AuraJourneyConstants.SystemName, ownerModId, "Initialize", "AuraJourneyShared initialized.");
        return root;
    }

    public static AuraSharedConfigWriteResult RegisterJourney(string ownerModId, AuraJourneyDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var originalJourneyId = definition.JourneyId ?? "";
        NormalizeDefinition(ownerModId, definition);
        var qualifiedJourneyId = definition.JourneyId ?? "";
        WarnIfLegacyJourneyId(definition.OwnerModId, originalJourneyId, qualifiedJourneyId, "RegisterJourney");
        var fileName = DefinitionFileName(qualifiedJourneyId);
        var result = AuraSharedConfigStore.WriteShared(
            definition.OwnerModId,
            AuraJourneyConstants.SystemName,
            fileName,
            definition,
            schemaVersion: AuraJourneyConstants.DefinitionSchemaVersion);

        if (result.Success)
        {
            AuraSharedRegistry.RegisterResource(definition.OwnerModId, new AuraSharedResourceRecord
            {
                System = AuraJourneyConstants.SystemName,
                ResourceId = qualifiedJourneyId,
                OwnerModId = definition.OwnerModId,
                Kind = "JourneyDefinition",
                Tags = definition.Tags.ToArray()
            });
        }

        AuraSharedDiagnostics.Write(AuraSharedDiagnostics.Create(
            AuraJourneyConstants.SystemName,
            definition.OwnerModId,
            result.Success ? "Info" : "Warn",
            "RegisterJourney",
            result.Success ? "Journey registered: " + qualifiedJourneyId : "Journey registration failed: " + result.Message,
            isAuthority: true,
            correlationId: qualifiedJourneyId));
        return result;
    }

    public static AuraSharedConfigSnapshot<AuraJourneyDefinition> ReadJourney(string callerId, string journeyId)
    {
        var rawJourneyId = (journeyId ?? "").Trim();
        var qualifiedJourneyId = QualifyJourneyId(callerId, rawJourneyId);
        var snapshot = AuraSharedConfigStore.ReadShared(
            callerId,
            AuraJourneyConstants.SystemName,
            DefinitionFileName(qualifiedJourneyId),
            new AuraJourneyDefinition { JourneyId = qualifiedJourneyId });
        if (snapshot.Found || string.Equals(rawJourneyId, qualifiedJourneyId, StringComparison.OrdinalIgnoreCase))
        {
            NormalizeSnapshotValue(snapshot.Value, callerId, qualifiedJourneyId);
            return snapshot;
        }

        var legacy = AuraSharedConfigStore.ReadShared(
            callerId,
            AuraJourneyConstants.SystemName,
            DefinitionFileName(rawJourneyId),
            new AuraJourneyDefinition { JourneyId = qualifiedJourneyId });
        if (legacy.Found)
        {
            NormalizeSnapshotValue(legacy.Value, callerId, qualifiedJourneyId);
            AuraSharedDiagnostics.Warn(
                AuraJourneyConstants.SystemName,
                callerId,
                "ReadJourney",
                "Read legacy unqualified journey definition: " + rawJourneyId + " -> " + qualifiedJourneyId,
                true,
                qualifiedJourneyId);
        }

        return legacy;
    }

    public static AuraSharedConfigSnapshot<AuraJourneyState> ReadState(string callerId, string journeyId)
    {
        var rawJourneyId = (journeyId ?? "").Trim();
        var qualifiedJourneyId = QualifyJourneyId(callerId, rawJourneyId);
        var snapshot = AuraSharedConfigStore.ReadRuntime(
            callerId,
            AuraJourneyConstants.SystemName,
            StateFileName(qualifiedJourneyId),
            new AuraJourneyState { JourneyId = qualifiedJourneyId });
        if (snapshot.Found || string.Equals(rawJourneyId, qualifiedJourneyId, StringComparison.OrdinalIgnoreCase))
        {
            NormalizeSnapshotValue(snapshot.Value, callerId, qualifiedJourneyId);
            return snapshot;
        }

        var legacy = AuraSharedConfigStore.ReadRuntime(
            callerId,
            AuraJourneyConstants.SystemName,
            StateFileName(rawJourneyId),
            new AuraJourneyState { JourneyId = qualifiedJourneyId });
        if (legacy.Found)
        {
            NormalizeSnapshotValue(legacy.Value, callerId, qualifiedJourneyId);
            AuraSharedDiagnostics.Warn(
                AuraJourneyConstants.SystemName,
                callerId,
                "ReadState",
                "Read legacy unqualified journey state: " + rawJourneyId + " -> " + qualifiedJourneyId,
                true,
                qualifiedJourneyId);
        }

        return legacy;
    }

    public static AuraSharedConfigWriteResult PublishActiveMode(
        string ownerModId,
        string journeyId,
        string modeId,
        bool isActive,
        string source,
        bool isAuthority = true)
    {
        var owner = (ownerModId ?? "").Trim();
        var qualifiedJourneyId = QualifyJourneyId(owner, journeyId);
        var state = new AuraJourneyActiveMode
        {
            OwnerModId = owner,
            JourneyId = qualifiedJourneyId,
            ModeId = (modeId ?? "").Trim(),
            IsActive = isActive,
            Source = (source ?? "").Trim(),
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };

        var result = AuraSharedConfigStore.WriteRuntime(
            string.IsNullOrWhiteSpace(owner) ? "AuraJourney" : owner,
            AuraJourneyConstants.SystemName,
            ActiveModeFileName(),
            state,
            expectedRevision: -1,
            schemaVersion: 1);

        AuraSharedDiagnostics.Write(AuraSharedDiagnostics.Create(
            AuraJourneyConstants.SystemName,
            owner,
            result.Success ? "Info" : "Warn",
            "PublishActiveMode",
            result.Success ? "Active journey mode updated: " + qualifiedJourneyId + " active=" + isActive : "Active journey mode update failed: " + result.Message,
            isAuthority,
            qualifiedJourneyId));
        return result;
    }

    public static AuraSharedConfigSnapshot<AuraJourneyActiveMode> ReadActiveMode(string callerId)
    {
        return AuraSharedConfigStore.ReadRuntime(
            callerId,
            AuraJourneyConstants.SystemName,
            ActiveModeFileName(),
            new AuraJourneyActiveMode());
    }

    public static bool IsJourneyActive(string callerId, string ownerModId, string journeyId)
    {
        var snapshot = ReadActiveMode(callerId);
        var value = snapshot.Value;
        if (value == null || !value.IsActive)
        {
            return false;
        }

        var qualifiedJourneyId = QualifyJourneyId(ownerModId, journeyId);
        return string.Equals(value.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(value.JourneyId, qualifiedJourneyId, StringComparison.OrdinalIgnoreCase);
    }

    public static AuraJourneyCommitResult TryCommit(AuraJourneyCommitRequest request)
    {
        try
        {
            if (request == null)
            {
                return Failure("Journey commit request is null.");
            }

            var originalJourneyId = (request.JourneyId ?? "").Trim();
            request.JourneyId = originalJourneyId;
            request.OwnerModId = (request.OwnerModId ?? "").Trim();
            request.AuthorityId = string.IsNullOrWhiteSpace(request.AuthorityId) ? request.OwnerModId : request.AuthorityId.Trim();
            if (string.IsNullOrWhiteSpace(request.JourneyId) || string.IsNullOrWhiteSpace(request.OwnerModId))
            {
                return Failure("JourneyId and ownerModId are required.");
            }

            request.JourneyId = QualifyJourneyId(request.OwnerModId, request.JourneyId);
            WarnIfLegacyJourneyId(request.OwnerModId, originalJourneyId, request.JourneyId, "Commit");

            if (!request.IsAuthority)
            {
                AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, request.OwnerModId, "Commit", "Non-authority journey commit rejected.", false, request.JourneyId);
                return Failure("Only the authoritative side may advance journey state.");
            }

            var snapshot = ReadState(request.OwnerModId, request.JourneyId);
            var next = AuraJourneyStateReducer.Apply(snapshot.Value, request, DateTime.UtcNow);
            next.JourneyId = request.JourneyId;
            next.OwnerModId = request.OwnerModId;
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

    public static string QualifyJourneyId(string ownerModId, string journeyId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = (journeyId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        if (IsQualifiedJourneyId(id) || string.IsNullOrWhiteSpace(owner))
        {
            return id;
        }

        return owner + ":" + id;
    }

    public static bool IsQualifiedJourneyId(string journeyId)
    {
        var id = (journeyId ?? "").Trim();
        var separator = id.IndexOf(':');
        return separator > 0 && separator < id.Length - 1;
    }

    public static string LocalJourneyId(string journeyId)
    {
        var id = (journeyId ?? "").Trim();
        var separator = id.IndexOf(':');
        return separator >= 0 && separator < id.Length - 1 ? id.Substring(separator + 1) : id;
    }

    private static void NormalizeDefinition(string ownerModId, AuraJourneyDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        definition.SchemaVersion = AuraJourneyConstants.DefinitionSchemaVersion;
        definition.OwnerModId = string.IsNullOrWhiteSpace(definition.OwnerModId) ? ownerModId : definition.OwnerModId.Trim();
        definition.JourneyId = QualifyJourneyId(definition.OwnerModId, definition.JourneyId);
        definition.DisplayName = (definition.DisplayName ?? "").Trim();
        definition.EntryNodeId = (definition.EntryNodeId ?? "").Trim();
        definition.Tags ??= new System.Collections.Generic.List<string>();
        definition.Nodes ??= new System.Collections.Generic.List<AuraJourneyNodeDefinition>();
        if (string.IsNullOrWhiteSpace(definition.JourneyId))
        {
            throw new InvalidOperationException("JourneyId is required.");
        }
    }

    private static void NormalizeSnapshotValue(AuraJourneyDefinition value, string ownerModId, string journeyId)
    {
        if (value == null)
        {
            return;
        }

        value.OwnerModId = ResolveSnapshotOwner(value.OwnerModId, journeyId, ownerModId);
        value.JourneyId = QualifyJourneyId(value.OwnerModId, string.IsNullOrWhiteSpace(value.JourneyId) ? journeyId : value.JourneyId);
    }

    private static void NormalizeSnapshotValue(AuraJourneyState value, string ownerModId, string journeyId)
    {
        if (value == null)
        {
            return;
        }

        value.OwnerModId = ResolveSnapshotOwner(value.OwnerModId, journeyId, ownerModId);
        value.JourneyId = QualifyJourneyId(value.OwnerModId, string.IsNullOrWhiteSpace(value.JourneyId) ? journeyId : value.JourneyId);
    }

    private static string ResolveSnapshotOwner(string valueOwnerModId, string journeyId, string fallbackOwnerModId)
    {
        var owner = (valueOwnerModId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(owner))
        {
            return owner;
        }

        var id = (journeyId ?? "").Trim();
        var separator = id.IndexOf(':');
        if (separator > 0)
        {
            return id.Substring(0, separator);
        }

        return (fallbackOwnerModId ?? "").Trim();
    }

    private static void WarnIfLegacyJourneyId(string ownerModId, string originalJourneyId, string qualifiedJourneyId, string phase)
    {
        var original = (originalJourneyId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(original)
            || string.Equals(original, qualifiedJourneyId, StringComparison.OrdinalIgnoreCase)
            || IsQualifiedJourneyId(original))
        {
            return;
        }

        AuraSharedDiagnostics.Warn(
            AuraJourneyConstants.SystemName,
            ownerModId,
            phase,
            "JourneyId should be owner-qualified. Normalized " + original + " -> " + qualifiedJourneyId + ".",
            true,
            qualifiedJourneyId);
    }

    private static string DefinitionFileName(string journeyId)
    {
        return AuraSharedIdentity.SafeId(journeyId, "journey") + ".definition.json";
    }

    private static string StateFileName(string journeyId)
    {
        return AuraSharedIdentity.SafeId(journeyId, "journey") + ".state.json";
    }

    private static string ActiveModeFileName()
    {
        return "active-mode.state.json";
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
