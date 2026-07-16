using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Witch.Mod;

namespace AuraMode.Shared;

public static class AuraModeRuntime
{
    private const string ActiveSnapshotFileName = "active-mode.snapshot.json";
    private static readonly object CacheGate = new();
    private static AuraSharedConfigSnapshot<AuraActiveModeSnapshot>? cachedActive;

    public static string Initialize(ModConfig? modConfig, string ownerModId)
    {
        var root = AuraSharedRuntime.Initialize(modConfig, ownerModId);
        ReadActiveMode(ownerModId, refresh: true);
        return root;
    }

    public static AuraSharedConfigWriteResult RegisterMode(string ownerModId, AuraModeDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        NormalizeDefinition(ownerModId, definition);
        if (string.IsNullOrWhiteSpace(definition.OwnerModId) || string.IsNullOrWhiteSpace(definition.ModeId))
        {
            return Failure("ownerModId and modeId are required.");
        }

        var result = AuraSharedConfigStore.WriteShared(
            definition.OwnerModId,
            AuraModeConstants.SystemName,
            DefinitionFileName(definition.ModeId),
            definition,
            schemaVersion: AuraModeConstants.DefinitionSchemaVersion);
        if (result.Success)
        {
            AuraSharedRegistry.RegisterResource(definition.OwnerModId, new AuraSharedResourceRecord
            {
                System = AuraModeConstants.SystemName,
                ResourceId = definition.ModeId,
                OwnerModId = definition.OwnerModId,
                Kind = "ModeDefinition",
                Tags = definition.Tags.ToArray()
            });
        }

        AuraSharedDiagnostics.Write(AuraSharedDiagnostics.Create(
            AuraModeConstants.SystemName,
            definition.OwnerModId,
            result.Success ? "Info" : "Warn",
            "RegisterMode",
            result.Success ? "Mode registered: " + definition.ModeId : "Mode registration failed: " + result.Message,
            true,
            definition.ModeId));
        return result;
    }

    public static AuraSharedConfigSnapshot<AuraModeDefinition> ReadMode(string callerId, string modeId)
    {
        var normalized = NormalizeId(modeId);
        var snapshot = AuraSharedConfigStore.ReadShared(
            callerId,
            AuraModeConstants.SystemName,
            DefinitionFileName(normalized),
            new AuraModeDefinition { ModeId = normalized });
        if (snapshot.Found)
        {
            NormalizeDefinition(snapshot.Value.OwnerModId, snapshot.Value);
        }
        return snapshot;
    }

    public static AuraModeTransitionResult ActivateMode(
        string ownerModId,
        string modeId,
        AuraModeRunBinding run,
        string source,
        AuraModePolicies? resolvedPolicies = null,
        bool isAuthority = true)
    {
        var owner = Clean(ownerModId);
        var qualifiedModeId = QualifyModeId(owner, modeId);
        if (!isAuthority)
        {
            return TransitionFailure("Only the authoritative mode owner may activate a mode.");
        }

        var definitionSnapshot = ReadMode(owner, qualifiedModeId);
        if (!definitionSnapshot.Found
            || !string.Equals(definitionSnapshot.Value.OwnerModId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return TransitionFailure("Registered mode definition not found for owner: " + qualifiedModeId);
        }

        var current = ReadActiveMode(owner, refresh: true);
        var definition = definitionSnapshot.Value;
        var normalizedRun = NormalizeRun(run);
        var normalizedPolicies = NormalizePolicies(resolvedPolicies ?? definition.DefaultPolicies, definition.OwnerModId);
        var currentValue = current.Value;
        if (currentValue != null
            && currentValue.IsActive
            && string.Equals(currentValue.OwnerModId, definition.OwnerModId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentValue.ModeId, definition.ModeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentValue.Run?.RunId, normalizedRun.RunId, StringComparison.Ordinal)
            && string.Equals(currentValue.Run?.SaveSlotId, normalizedRun.SaveSlotId, StringComparison.Ordinal)
            && currentValue.DefinitionRevision == definitionSnapshot.Revision
            && PoliciesEquivalent(currentValue.ResolvedPolicies, normalizedPolicies))
        {
            return new AuraModeTransitionResult
            {
                Success = true,
                Applied = false,
                Revision = current.Revision,
                Message = "The requested mode snapshot is already active.",
                Snapshot = currentValue
            };
        }

        var next = new AuraActiveModeSnapshot
        {
            Status = AuraModeStates.Active,
            ModeId = definition.ModeId,
            OwnerModId = definition.OwnerModId,
            Run = normalizedRun,
            DefinitionRevision = definitionSnapshot.Revision,
            Display = Clone(definition.Display),
            Host = Clone(definition.Host),
            JourneyId = Clean(definition.JourneyId),
            ResolvedPolicies = normalizedPolicies,
            Capabilities = Clone(definition.Capabilities),
            AuthorityId = owner,
            Sequence = Math.Max(0, current.Value?.Sequence ?? 0) + 1,
            Source = Clean(source),
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };
        return WriteTransition(next, current.Revision, applied: true);
    }

    public static AuraModeTransitionResult DeactivateMode(
        string ownerModId,
        string modeId,
        string runId,
        string source,
        bool isAuthority = true)
    {
        if (!isAuthority)
        {
            return TransitionFailure("Only the authoritative mode owner may deactivate a mode.");
        }

        var owner = Clean(ownerModId);
        var qualifiedModeId = QualifyModeId(owner, modeId);
        var current = ReadActiveMode(owner, refresh: true);
        var value = current.Value ?? new AuraActiveModeSnapshot();
        if (!value.IsActive
            || !string.Equals(value.OwnerModId, owner, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(value.ModeId, qualifiedModeId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(runId)
                && !string.Equals(value.Run?.RunId, Clean(runId), StringComparison.Ordinal)))
        {
            return new AuraModeTransitionResult
            {
                Success = true,
                Applied = false,
                Revision = current.Revision,
                Message = "Active mode did not match the conditional deactivation request.",
                Snapshot = value
            };
        }

        var next = Clone(value);
        next.Status = AuraModeStates.Inactive;
        next.Sequence = Math.Max(0, value.Sequence) + 1;
        next.Source = Clean(source);
        next.UpdatedUtc = DateTime.UtcNow.ToString("O");
        return WriteTransition(next, current.Revision, applied: true);
    }

    public static AuraSharedConfigSnapshot<AuraActiveModeSnapshot> ReadActiveMode(string callerId, bool refresh = false)
    {
        lock (CacheGate)
        {
            if (!refresh && cachedActive != null)
            {
                return cachedActive;
            }
        }

        var snapshot = AuraSharedConfigStore.ReadRuntime(
            callerId,
            AuraModeConstants.SystemName,
            ActiveSnapshotFileName,
            new AuraActiveModeSnapshot());
        NormalizeSnapshot(snapshot.Value);
        lock (CacheGate)
        {
            cachedActive = snapshot;
        }
        return snapshot;
    }

    public static AuraActiveModeSnapshot? Current(string callerId, bool refresh = false)
    {
        var value = ReadActiveMode(callerId, refresh).Value;
        return value != null && value.IsActive ? value : null;
    }

    public static bool IsExternalStarterDeckMutationAllowed(AuraActiveModeSnapshot? snapshot, string actorId)
    {
        return EvaluateStarterDeckMutation(snapshot, actorId).Allowed;
    }

    public static AuraModePolicyDecision EvaluateStarterDeckMutation(AuraActiveModeSnapshot? snapshot, string actorId)
    {
        return AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, actorId);
    }

    public static string QualifyModeId(string ownerModId, string modeId)
    {
        var owner = Clean(ownerModId);
        var id = NormalizeId(modeId);
        if (id.IndexOf(':') > 0 || owner.Length == 0)
        {
            return id;
        }
        return owner + ":" + id;
    }

    private static AuraModeTransitionResult WriteTransition(AuraActiveModeSnapshot next, long expectedRevision, bool applied)
    {
        var write = AuraSharedConfigStore.WriteRuntime(
            AuraModeConstants.RuntimeAuthorityId,
            AuraModeConstants.SystemName,
            ActiveSnapshotFileName,
            next,
            expectedRevision,
            AuraModeConstants.ActiveSnapshotSchemaVersion);
        if (write.Success)
        {
            lock (CacheGate)
            {
                cachedActive = new AuraSharedConfigSnapshot<AuraActiveModeSnapshot>
                {
                    Found = true,
                    Revision = write.Revision,
                    SchemaVersion = AuraModeConstants.ActiveSnapshotSchemaVersion,
                    AuthorityId = AuraModeConstants.RuntimeAuthorityId,
                    Value = next
                };
            }
        }

        AuraSharedDiagnostics.Write(AuraSharedDiagnostics.Create(
            AuraModeConstants.SystemName,
            next.OwnerModId,
            write.Success ? "Info" : "Warn",
            next.IsActive ? "ActivateMode" : "DeactivateMode",
            write.Success ? "Active mode transition committed: " + next.ModeId : "Active mode transition failed: " + write.Message,
            true,
            next.Run?.RunId ?? next.ModeId));
        return new AuraModeTransitionResult
        {
            Success = write.Success,
            Applied = write.Success && applied,
            Conflict = write.Conflict,
            Revision = write.Revision,
            Message = write.Message,
            Snapshot = next
        };
    }

    private static void NormalizeDefinition(string ownerModId, AuraModeDefinition definition)
    {
        var owner = Clean(ownerModId);
        definition.SchemaVersion = Math.Max(1, definition.SchemaVersion);
        definition.OwnerModId = owner.Length == 0 ? Clean(definition.OwnerModId) : owner;
        definition.ModeId = QualifyModeId(definition.OwnerModId, definition.ModeId);
        definition.Aliases = CleanList(definition.Aliases)
            .Where(alias => !string.Equals(alias, definition.ModeId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        definition.Display = Clone(definition.Display);
        if (definition.Display.FallbackName.Length == 0)
        {
            definition.Display.FallbackName = definition.ModeId;
        }
        definition.Host = Clone(definition.Host);
        definition.JourneyId = Clean(definition.JourneyId);
        definition.DefaultPolicies = NormalizePolicies(definition.DefaultPolicies, definition.OwnerModId);
        definition.Capabilities = Clone(definition.Capabilities);
        definition.Tags = CleanList(definition.Tags);
        definition.Metadata ??= new Dictionary<string, string>();
    }

    private static void NormalizeSnapshot(AuraActiveModeSnapshot value)
    {
        value.SchemaVersion = Math.Max(1, value.SchemaVersion);
        value.Status = Clean(value.Status);
        value.ModeId = NormalizeId(value.ModeId);
        value.OwnerModId = Clean(value.OwnerModId);
        value.Run = NormalizeRun(value.Run);
        value.Display = Clone(value.Display);
        value.Host = Clone(value.Host);
        value.JourneyId = Clean(value.JourneyId);
        value.ResolvedPolicies = NormalizePolicies(value.ResolvedPolicies, value.OwnerModId);
        value.Capabilities = Clone(value.Capabilities);
        value.AuthorityId = Clean(value.AuthorityId);
        value.Source = Clean(value.Source);
        value.UpdatedUtc = Clean(value.UpdatedUtc);
    }

    private static AuraModePolicies NormalizePolicies(AuraModePolicies? source, string fallbackProvider)
    {
        var authority = Clean(source?.StarterDeck?.MutationAuthority);
        if (!string.Equals(authority, AuraModeStarterDeckAuthorities.ModeOwnerExclusive, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authority, AuraModeStarterDeckAuthorities.OfficialOnly, StringComparison.OrdinalIgnoreCase))
        {
            authority = AuraModeStarterDeckAuthorities.InheritHost;
        }
        return new AuraModePolicies
        {
            StarterDeck = new AuraModeStarterDeckPolicy
            {
                MutationAuthority = authority,
                ProviderId = string.Equals(authority, AuraModeStarterDeckAuthorities.ModeOwnerExclusive, StringComparison.OrdinalIgnoreCase)
                    ? FirstNonEmpty(source?.StarterDeck?.ProviderId, fallbackProvider)
                    : Clean(source?.StarterDeck?.ProviderId)
            }
        };
    }

    private static AuraModeDisplay Clone(AuraModeDisplay? source)
    {
        return new AuraModeDisplay
        {
            NameKey = Clean(source?.NameKey),
            FallbackName = Clean(source?.FallbackName)
        };
    }

    private static AuraModeHost Clone(AuraModeHost? source)
    {
        return new AuraModeHost
        {
            NativeModeType = Clean(source?.NativeModeType),
            RuntimeManagerHint = Clean(source?.RuntimeManagerHint)
        };
    }

    private static AuraModeCapabilities Clone(AuraModeCapabilities? source)
    {
        return new AuraModeCapabilities
        {
            CombatContractId = FirstNonEmpty(source?.CombatContractId, AuraModeCombatContracts.InheritHost)
        };
    }

    private static AuraModeRunBinding NormalizeRun(AuraModeRunBinding? source)
    {
        return new AuraModeRunBinding
        {
            RunId = Clean(source?.RunId),
            SaveSlotId = Clean(source?.SaveSlotId),
            StartedUtc = Clean(source?.StartedUtc)
        };
    }

    private static AuraActiveModeSnapshot Clone(AuraActiveModeSnapshot source)
    {
        return new AuraActiveModeSnapshot
        {
            SchemaVersion = source.SchemaVersion,
            Status = source.Status,
            ModeId = source.ModeId,
            OwnerModId = source.OwnerModId,
            Run = NormalizeRun(source.Run),
            DefinitionRevision = source.DefinitionRevision,
            Display = Clone(source.Display),
            Host = Clone(source.Host),
            JourneyId = source.JourneyId,
            ResolvedPolicies = NormalizePolicies(source.ResolvedPolicies, source.OwnerModId),
            Capabilities = Clone(source.Capabilities),
            AuthorityId = source.AuthorityId,
            Sequence = source.Sequence,
            Source = source.Source,
            UpdatedUtc = source.UpdatedUtc
        };
    }

    private static List<string> CleanList(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(Clean)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DefinitionFileName(string modeId)
    {
        return AuraSharedIdentity.SafeId(modeId, "mode") + ".definition.json";
    }

    private static string NormalizeId(string? value)
    {
        return Clean(value);
    }

    private static string FirstNonEmpty(string? first, string? second)
    {
        var value = Clean(first);
        return value.Length > 0 ? value : Clean(second);
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }

    private static AuraSharedConfigWriteResult Failure(string message)
    {
        return new AuraSharedConfigWriteResult { Success = false, Message = message };
    }

    private static AuraModeTransitionResult TransitionFailure(string message)
    {
        return new AuraModeTransitionResult { Success = false, Message = message };
    }

    private static bool PoliciesEquivalent(AuraModePolicies? left, AuraModePolicies? right)
    {
        var leftStarter = left?.StarterDeck ?? new AuraModeStarterDeckPolicy();
        var rightStarter = right?.StarterDeck ?? new AuraModeStarterDeckPolicy();
        return string.Equals(leftStarter.MutationAuthority, rightStarter.MutationAuthority, StringComparison.OrdinalIgnoreCase)
               && string.Equals(leftStarter.ProviderId, rightStarter.ProviderId, StringComparison.OrdinalIgnoreCase);
    }
}
