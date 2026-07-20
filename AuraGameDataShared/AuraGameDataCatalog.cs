using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;

namespace AuraGameData.Shared;

public interface IAuraGameDataSource
{
    long Revision { get; }

    IReadOnlyList<AuraGameDataDefinition> Read(string dataType);

    void Invalidate();
}

public sealed class AuraGameDataCatalog
{
    private readonly IAuraGameDataSource source;

    public AuraGameDataCatalog(IAuraGameDataSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public AuraGameDataQueryResult Query(
        AuraGameDataRegistryDocument document,
        long registryRevision,
        AuraGameDataQuery query)
    {
        document ??= new AuraGameDataRegistryDocument();
        document.Normalize();
        query ??= new AuraGameDataQuery();
        query.Normalize();

        var definitions = new List<AuraGameDataDefinition>();
        definitions.AddRange(source.Read(query.DataType).Select(value => value.Clone()));
        definitions.AddRange(document.Definitions
            .Where(value => string.Equals(value.Key.DataType, query.DataType, StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Clone()));

        var candidates = definitions
            .Where(value => query.IncludeDisabled || value.Enabled)
            .Where(value => query.IncludeHistory || !value.Retired)
            .Where(value => query.OwnerModIds.Count == 0
                || query.OwnerModIds.Contains(value.OwnerModId, StringComparer.OrdinalIgnoreCase))
            .Select(value => new RankedDefinition(
                value,
                CandidateRank(value, query.CandidateIds),
                SourceRank(value.SourceKind, query.SourceOrder)))
            .Where(value => query.CandidateIds.Count == 0 || value.CandidateRank < int.MaxValue)
            .OrderBy(value => value.CandidateRank)
            .ThenBy(value => value.SourceRank)
            .ThenByDescending(value => value.Definition.Priority)
            .ThenByDescending(value => value.Definition.Revision)
            .ThenBy(value => value.Definition.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!query.IncludeAllCandidates)
        {
            candidates = candidates
                .GroupBy(value => value.Definition.Key.Canonical, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(value => value.CandidateRank)
                .ThenBy(value => value.Definition.Key.Id, StringComparer.Ordinal)
                .ToList();
        }

        var revision = Math.Max(Math.Max(0, registryRevision), Math.Max(0, source.Revision));
        return new AuraGameDataQueryResult(
            revision,
            candidates.Select(value => new AuraGameDataSnapshot(value.Definition)).ToList());
    }

    public AuraGameDataQueryResult QueryHistory(
        AuraGameDataRegistryDocument document,
        long registryRevision,
        AuraGameDataQuery query)
    {
        document ??= new AuraGameDataRegistryDocument();
        document.Normalize();
        query ??= new AuraGameDataQuery();
        query.Normalize();
        var sourceOrder = query.SourceOrder;
        var items = document.Definitions
            .Where(value => value.Retired)
            .Where(value => string.IsNullOrWhiteSpace(query.DataType)
                || string.Equals(value.Key.DataType, query.DataType, StringComparison.OrdinalIgnoreCase))
            .Where(value => query.OwnerModIds.Count == 0
                || query.OwnerModIds.Contains(value.OwnerModId, StringComparer.OrdinalIgnoreCase))
            .Select(value => new RankedDefinition(
                value,
                CandidateRank(value, query.CandidateIds),
                SourceRank(value.SourceKind, sourceOrder)))
            .Where(value => query.CandidateIds.Count == 0 || value.CandidateRank < int.MaxValue)
            .OrderBy(value => value.CandidateRank)
            .ThenBy(value => value.SourceRank)
            .ThenByDescending(value => value.Definition.Revision)
            .Select(value => new AuraGameDataSnapshot(value.Definition))
            .ToList();
        return new AuraGameDataQueryResult(Math.Max(0, registryRevision), items);
    }

    private static int CandidateRank(AuraGameDataDefinition definition, IReadOnlyList<string> candidateIds)
    {
        if (candidateIds.Count == 0)
        {
            return 0;
        }

        for (var index = 0; index < candidateIds.Count; index++)
        {
            var candidate = candidateIds[index];
            if (string.Equals(definition.Key.Id, candidate, StringComparison.Ordinal)
                || definition.Aliases.Contains(candidate, StringComparer.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static int SourceRank(string sourceKind, IReadOnlyList<string> sourceOrder)
    {
        for (var index = 0; index < sourceOrder.Count; index++)
        {
            if (string.Equals(sourceOrder[index], sourceKind, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private sealed class RankedDefinition
    {
        public RankedDefinition(AuraGameDataDefinition definition, int candidateRank, int sourceRank)
        {
            Definition = definition;
            CandidateRank = candidateRank;
            SourceRank = sourceRank;
        }

        public AuraGameDataDefinition Definition { get; }

        public int CandidateRank { get; }

        public int SourceRank { get; }
    }
}

public static class AuraGameDataCatalogRuntime
{
    private static readonly object Gate = new();
    private static IAuraGameDataSource source = new EmptySource();
    private static AuraGameDataRegistryDocument? cachedDocument;
    private static long cachedRevision = -1;

    public static event Action<long>? Changed;

    public static void ConfigureSource(IAuraGameDataSource gameDataSource)
    {
        lock (Gate)
        {
            source = gameDataSource ?? new EmptySource();
            source.Invalidate();
        }

        NotifyChanged(CurrentRevision());
    }

    public static AuraGameDataQueryResult Query(AuraGameDataQuery query)
    {
        var document = ReadDocument(out var revision);
        IAuraGameDataSource current;
        lock (Gate)
        {
            current = source;
        }

        return new AuraGameDataCatalog(current).Query(document, revision, query);
    }

    public static AuraGameDataQueryResult QueryHistory(AuraGameDataQuery query)
    {
        var document = ReadDocument(out var revision);
        IAuraGameDataSource current;
        lock (Gate)
        {
            current = source;
        }

        return new AuraGameDataCatalog(current).QueryHistory(document, revision, query);
    }

    public static AuraGameDataSnapshot? Resolve(string dataType, IEnumerable<string> candidateIds)
    {
        return Query(new AuraGameDataQuery
        {
            DataType = dataType,
            CandidateIds = new List<string>(candidateIds ?? Array.Empty<string>())
        }).Items.FirstOrDefault();
    }

    public static AuraGameDataMutationResult Register(string callerId, AuraGameDataDefinition definition, long expectedRevision = -1)
    {
        if (!ValidateRegistration(callerId, definition, out var failure))
        {
            return AuraGameDataMutationResult.Failed(failure);
        }

        definition.Normalize();
        return Mutate(document =>
        {
            var existing = Find(document, definition.Key, definition.OwnerModId);
            if (existing != null && expectedRevision >= 0 && existing.Revision != expectedRevision)
            {
                return PendingMutation.ConflictResult("Definition revision conflict.", existing.Revision);
            }

            if (existing != null && !CanWrite(callerId, existing))
            {
                return PendingMutation.ConflictResult("Only the owner or recorded writer may update a definition.", existing.Revision);
            }

            if (existing != null && !existing.Retired && SemanticallyEquals(existing, definition))
            {
                return PendingMutation.Unchanged(existing);
            }

            var next = definition.Clone();
            next.Revision = Math.Max(1, (existing?.Revision ?? 0) + 1);
            next.Retired = false;
            next.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            document.Definitions.RemoveAll(value => SameDefinition(value, next.Key, next.OwnerModId));
            document.Definitions.Add(next);
            document.Normalize();
            return PendingMutation.Applied(next);
        });
    }

    public static AuraGameDataMutationResult RegisterBatch(
        string callerId,
        IEnumerable<AuraGameDataDefinition> definitions)
    {
        var batch = (definitions ?? Array.Empty<AuraGameDataDefinition>())
            .Where(value => value != null)
            .Select(value => value.Clone())
            .ToList();
        if (batch.Count == 0)
        {
            return AuraGameDataMutationResult.Failed("At least one definition is required.");
        }

        foreach (var definition in batch)
        {
            if (!ValidateRegistration(callerId, definition, out var failure))
            {
                return AuraGameDataMutationResult.Failed(failure);
            }
        }

        return Mutate(document =>
        {
            AuraGameDataDefinition? last = null;
            var changed = false;
            foreach (var definition in batch)
            {
                var existing = Find(document, definition.Key, definition.OwnerModId);
                if (existing != null && !CanWrite(callerId, existing))
                {
                    return PendingMutation.ConflictResult(
                        "Only the owner or recorded writer may update a definition: " + definition.QualifiedId,
                        existing.Revision);
                }

                if (existing != null && !existing.Retired && SemanticallyEquals(existing, definition))
                {
                    last = existing;
                    continue;
                }

                var next = definition.Clone();
                next.Revision = Math.Max(1, (existing?.Revision ?? 0) + 1);
                next.Retired = false;
                next.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                document.Definitions.RemoveAll(value => SameDefinition(value, next.Key, next.OwnerModId));
                document.Definitions.Add(next);
                last = next;
                changed = true;
            }

            document.Normalize();
            return changed
                ? PendingMutation.Applied(last!)
                : PendingMutation.Unchanged(last!);
        });
    }

    public static AuraGameDataMutationResult Patch(
        string callerId,
        AuraGameDataKey key,
        string ownerModId,
        AuraGameDataPatch patch,
        long expectedRevision)
    {
        key ??= new AuraGameDataKey();
        key.Normalize();
        ownerModId = (ownerModId ?? "").Trim();
        patch ??= new AuraGameDataPatch();
        patch.Normalize();
        if (string.IsNullOrWhiteSpace(callerId) || string.IsNullOrWhiteSpace(ownerModId)
            || string.IsNullOrWhiteSpace(key.DataType) || string.IsNullOrWhiteSpace(key.Id))
        {
            return AuraGameDataMutationResult.Failed("Patch identity is incomplete.");
        }

        if (ContainsScriptField(patch.SetFields.Keys) || ContainsScriptField(patch.RemoveFields))
        {
            return AuraGameDataMutationResult.Failed("Script fields are registration-time only.");
        }

        return Mutate(document =>
        {
            var existing = Find(document, key, ownerModId);
            if (existing == null)
            {
                return PendingMutation.ConflictResult("Definition does not exist.", 0);
            }

            if (!CanWrite(callerId, existing))
            {
                return PendingMutation.ConflictResult("Only the owner or recorded writer may patch a definition.", existing.Revision);
            }

            if (expectedRevision >= 0 && existing.Revision != expectedRevision)
            {
                return PendingMutation.ConflictResult("Definition revision conflict.", existing.Revision);
            }

            var next = existing.Clone();
            foreach (var pair in patch.SetFields.Where(pair => !string.Equals(pair.Key, "Id", StringComparison.Ordinal)))
            {
                next.Fields[pair.Key] = pair.Value ?? "";
            }

            foreach (var field in patch.RemoveFields)
            {
                next.Fields.Remove(field);
            }

            if (patch.Aliases != null)
            {
                next.Aliases = patch.Aliases.ToList();
            }

            if (patch.Enabled.HasValue)
            {
                next.Enabled = patch.Enabled.Value;
            }

            if (patch.Priority.HasValue)
            {
                next.Priority = patch.Priority.Value;
            }

            next.Revision++;
            next.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            next.Normalize();
            document.Definitions.RemoveAll(value => SameDefinition(value, key, ownerModId));
            document.Definitions.Add(next);
            document.Normalize();
            return PendingMutation.Applied(next);
        });
    }

    public static AuraGameDataMutationResult Retire(
        string callerId,
        AuraGameDataKey key,
        string ownerModId,
        long expectedRevision)
    {
        key ??= new AuraGameDataKey();
        key.Normalize();
        ownerModId = (ownerModId ?? "").Trim();
        return Mutate(document =>
        {
            var existing = Find(document, key, ownerModId);
            if (existing == null)
            {
                return PendingMutation.ConflictResult("Definition does not exist.", 0);
            }

            if (!CanWrite(callerId, existing))
            {
                return PendingMutation.ConflictResult("Only the owner or recorded writer may retire a definition.", existing.Revision);
            }

            if (expectedRevision >= 0 && existing.Revision != expectedRevision)
            {
                return PendingMutation.ConflictResult("Definition revision conflict.", existing.Revision);
            }

            existing.Retired = true;
            existing.Enabled = false;
            existing.Revision++;
            existing.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            document.Normalize();
            return PendingMutation.Applied(existing);
        });
    }

    public static bool ValidateHandle(AuraGameDataDefinitionHandle? handle, out AuraGameDataSnapshot? snapshot)
    {
        snapshot = null;
        if (handle?.Key == null || string.IsNullOrWhiteSpace(handle.Token))
        {
            return false;
        }

        var result = Query(new AuraGameDataQuery
        {
            DataType = handle.Key.DataType,
            CandidateIds = new List<string> { handle.Key.Id },
            OwnerModIds = string.IsNullOrWhiteSpace(handle.OwnerModId)
                ? new List<string>()
                : new List<string> { handle.OwnerModId },
            IncludeAllCandidates = true
        });
        snapshot = result.Items.FirstOrDefault(value =>
            string.Equals(value.Key.Id, handle.Key.Id, StringComparison.Ordinal)
            && string.Equals(value.OwnerModId, handle.OwnerModId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.SourceKind, handle.SourceKind, StringComparison.OrdinalIgnoreCase)
            && value.Revision == handle.Revision);
        return snapshot != null && string.Equals(handle.Token, Token(snapshot.Definition), StringComparison.Ordinal);
    }

    public static AuraGameDataDefinitionHandle CreateHandle(AuraGameDataSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return CreateHandle(snapshot.Definition);
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            cachedDocument = null;
            cachedRevision = -1;
            source.Invalidate();
        }

        NotifyChanged(CurrentRevision());
    }

    private static AuraGameDataMutationResult Mutate(Func<AuraGameDataRegistryDocument, PendingMutation> change)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var document = ReadDocument(out var storageRevision).Clone();
            var pending = change(document);
            if (!pending.Success)
            {
                return AuraGameDataMutationResult.Failed(pending.Message, pending.Conflict, pending.Revision);
            }

            if (!pending.RequiresWrite)
            {
                return new AuraGameDataMutationResult
                {
                    Success = true,
                    Revision = storageRevision,
                    Message = "Unchanged",
                    Handle = pending.Definition == null ? null : CreateHandle(pending.Definition)
                };
            }

            var write = AuraSharedConfigStore.WriteShared(
                AuraGameDataConstants.RegistryAuthorityId,
                AuraGameDataConstants.SystemName,
                AuraGameDataConstants.RegistryFileName,
                document,
                storageRevision,
                AuraGameDataConstants.SchemaVersion);
            if (write.Success)
            {
                Cache(document, write.Revision);
                NotifyChanged(write.Revision);
                return new AuraGameDataMutationResult
                {
                    Success = true,
                    Revision = write.Revision,
                    Message = "Applied",
                    Handle = pending.Definition == null ? null : CreateHandle(pending.Definition)
                };
            }

            if (!write.Conflict)
            {
                return AuraGameDataMutationResult.Failed(write.Message, false, write.Revision);
            }

            InvalidateDocument();
        }

        return AuraGameDataMutationResult.Failed("Registry write conflicted repeatedly.", true);
    }

    private static AuraGameDataRegistryDocument ReadDocument(out long revision)
    {
        lock (Gate)
        {
            if (cachedDocument != null)
            {
                revision = Math.Max(0, cachedRevision);
                return cachedDocument.Clone();
            }
        }

        var snapshot = AuraSharedConfigStore.ReadShared(
            AuraGameDataConstants.RegistryAuthorityId,
            AuraGameDataConstants.SystemName,
            AuraGameDataConstants.RegistryFileName,
            new AuraGameDataRegistryDocument());
        var document = snapshot.SchemaVersion != 0 && snapshot.SchemaVersion != AuraGameDataConstants.SchemaVersion
            ? new AuraGameDataRegistryDocument()
            : snapshot.Value ?? new AuraGameDataRegistryDocument();
        document.Normalize();
        revision = snapshot.Found ? Math.Max(0, snapshot.Revision) : 0;
        Cache(document, revision);
        return document.Clone();
    }

    private static bool ValidateRegistration(
        string callerId,
        AuraGameDataDefinition? definition,
        out string failure)
    {
        failure = "";
        if (definition == null)
        {
            failure = "Definition is required.";
            return false;
        }

        definition.Normalize();
        callerId = (callerId ?? "").Trim();
        if (definition.SchemaVersion != AuraGameDataConstants.SchemaVersion)
        {
            failure = "Only schemaVersion 4 game-data registration is accepted.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(callerId)
            || string.IsNullOrWhiteSpace(definition.Key.DataType)
            || string.IsNullOrWhiteSpace(definition.Key.Id)
            || string.IsNullOrWhiteSpace(definition.OwnerModId)
            || string.IsNullOrWhiteSpace(definition.WriterId))
        {
            failure = "Definition identity, owner, and writer are required.";
            return false;
        }

        if (!string.Equals(callerId, definition.WriterId, StringComparison.OrdinalIgnoreCase))
        {
            failure = "Caller must match writerId.";
            return false;
        }

        if (string.Equals(definition.SourceKind, AuraGameDataSourceKinds.Native, StringComparison.OrdinalIgnoreCase))
        {
            failure = "Native projections may only be supplied by the host adapter.";
            return false;
        }

        if (string.Equals(definition.SourceKind, AuraGameDataSourceKinds.UserManual, StringComparison.OrdinalIgnoreCase)
            && ContainsScriptField(definition.Fields.Keys))
        {
            failure = "Manual definitions may not register script fields.";
            return false;
        }

        return true;
    }

    private static bool CanWrite(string callerId, AuraGameDataDefinition definition)
    {
        return !string.IsNullOrWhiteSpace(callerId)
            && (string.Equals(callerId, definition.OwnerModId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(callerId, definition.WriterId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsScriptField(IEnumerable<string> fields)
    {
        return (fields ?? Array.Empty<string>()).Any(field =>
            !string.IsNullOrWhiteSpace(field)
            && field.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static AuraGameDataDefinition? Find(
        AuraGameDataRegistryDocument document,
        AuraGameDataKey key,
        string ownerModId)
    {
        return document.Definitions.FirstOrDefault(value => SameDefinition(value, key, ownerModId));
    }

    private static bool SameDefinition(AuraGameDataDefinition value, AuraGameDataKey key, string ownerModId)
    {
        return value.Key.Equals(key)
            && string.Equals(value.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SemanticallyEquals(AuraGameDataDefinition left, AuraGameDataDefinition right)
    {
        return left.Key.Equals(right.Key)
            && string.Equals(left.OwnerModId, right.OwnerModId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.WriterId, right.WriterId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.SourceKind, right.SourceKind, StringComparison.OrdinalIgnoreCase)
            && left.Priority == right.Priority
            && left.Enabled == right.Enabled
            && left.Aliases.SequenceEqual(right.Aliases, StringComparer.Ordinal)
            && left.Fields.Count == right.Fields.Count
            && left.Fields.All(pair => right.Fields.TryGetValue(pair.Key, out var value)
                && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static AuraGameDataDefinitionHandle CreateHandle(AuraGameDataDefinition definition)
    {
        return new AuraGameDataDefinitionHandle
        {
            Key = definition.Key.Clone(),
            OwnerModId = definition.OwnerModId,
            SourceKind = definition.SourceKind,
            Revision = definition.Revision,
            Token = Token(definition)
        };
    }

    private static string Token(AuraGameDataDefinition definition)
    {
        var text = definition.Key.Canonical
            + "|" + definition.OwnerModId
            + "|" + definition.WriterId
            + "|" + definition.SourceKind
            + "|" + definition.Revision.ToString(CultureInfo.InvariantCulture);
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }

    private static void Cache(AuraGameDataRegistryDocument document, long revision)
    {
        lock (Gate)
        {
            cachedDocument = document.Clone();
            cachedRevision = Math.Max(0, revision);
        }
    }

    private static void InvalidateDocument()
    {
        lock (Gate)
        {
            cachedDocument = null;
            cachedRevision = -1;
        }
    }

    private static long CurrentRevision()
    {
        lock (Gate)
        {
            return Math.Max(Math.Max(0, cachedRevision), Math.Max(0, source.Revision));
        }
    }

    private static void NotifyChanged(long revision)
    {
        try
        {
            Changed?.Invoke(Math.Max(0, revision));
        }
        catch
        {
        }
    }

    private sealed class PendingMutation
    {
        public bool Success { get; private set; }

        public bool Conflict { get; private set; }

        public string Message { get; private set; } = "";

        public long Revision { get; private set; }

        public AuraGameDataDefinition? Definition { get; private set; }

        public bool RequiresWrite { get; private set; }

        public static PendingMutation Applied(AuraGameDataDefinition definition)
        {
            return new PendingMutation
            {
                Success = true,
                RequiresWrite = true,
                Definition = definition.Clone(),
                Revision = definition.Revision
            };
        }

        public static PendingMutation Unchanged(AuraGameDataDefinition definition)
        {
            return new PendingMutation
            {
                Success = true,
                RequiresWrite = false,
                Definition = definition.Clone(),
                Revision = definition.Revision
            };
        }

        public static PendingMutation ConflictResult(string message, long revision)
        {
            return new PendingMutation { Conflict = true, Message = message, Revision = Math.Max(0, revision) };
        }
    }

    private sealed class EmptySource : IAuraGameDataSource
    {
        public long Revision => 0;

        public IReadOnlyList<AuraGameDataDefinition> Read(string dataType)
        {
            return Array.Empty<AuraGameDataDefinition>();
        }

        public void Invalidate()
        {
        }
    }
}
