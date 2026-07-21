using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using AuraShared.Core;

namespace AuraGameData.Shared;

public interface IAuraGameDataSource
{
    long Revision { get; }

    AuraGameDataSourceSnapshot Capture();

    void Invalidate();
}

public sealed class AuraGameDataCatalogBuildRequest
{
    internal AuraGameDataCatalogBuildRequest(
        AuraGameDataSourceSnapshot source,
        AuraGameDataRegistryDocument registry,
        long registryRevision,
        long epoch)
    {
        Source = source;
        Registry = registry;
        RegistryRevision = Math.Max(0, registryRevision);
        Epoch = Math.Max(0, epoch);
    }

    public AuraGameDataSourceSnapshot Source { get; }

    public AuraGameDataRegistryDocument Registry { get; }

    public long RegistryRevision { get; }

    public long Epoch { get; }
}

public sealed class AuraGameDataCatalogSnapshot
{
    private readonly Dictionary<string, TypeIndex> types;
    private readonly Dictionary<string, string> uniqueTypeById;
    private readonly Dictionary<string, AuraGameDataSnapshot> definitionsByIdentity;
    private readonly IReadOnlyList<AuraGameDataSnapshot> history;

    internal AuraGameDataCatalogSnapshot(
        AuraGameDataCatalogVersion version,
        Dictionary<string, TypeIndex> types,
        Dictionary<string, string> uniqueTypeById,
        Dictionary<string, AuraGameDataSnapshot> definitionsByIdentity,
        IReadOnlyList<AuraGameDataSnapshot> history)
    {
        Version = version;
        this.types = types;
        this.uniqueTypeById = uniqueTypeById;
        this.definitionsByIdentity = definitionsByIdentity;
        this.history = history;
    }

    public AuraGameDataCatalogVersion Version { get; }

    public bool TryGet(string dataType, string id, out AuraGameDataSnapshot? snapshot)
    {
        snapshot = null;
        dataType = AuraGameDataKey.NormalizeDataType(dataType);
        id = AuraGameDataKey.NormalizeId(id);
        var hit = dataType.Length > 0
            && id.Length > 0
            && types.TryGetValue(dataType, out var index)
            && index.TryGet(id, out snapshot);
        AuraGameDataDiagnostics.RecordPointLookup(hit);
        return hit;
    }

    public AuraGameDataSnapshot? Resolve(string dataType, IEnumerable<string>? candidateIds)
    {
        AuraGameDataDiagnostics.RecordCandidateResolve();
        if (!types.TryGetValue(AuraGameDataKey.NormalizeDataType(dataType), out var index))
        {
            return null;
        }

        foreach (var candidate in candidateIds ?? Array.Empty<string>())
        {
            if (index.TryGet(AuraGameDataKey.NormalizeId(candidate), out var snapshot))
            {
                return snapshot;
            }
        }

        return null;
    }

    public IReadOnlyList<AuraGameDataSnapshot> GetTable(string dataType)
    {
        AuraGameDataDiagnostics.RecordTableView();
        return types.TryGetValue(AuraGameDataKey.NormalizeDataType(dataType), out var index)
            ? index.Rows
            : Array.Empty<AuraGameDataSnapshot>();
    }

    public bool TryResolveUniqueType(string id, out string dataType)
    {
        AuraGameDataDiagnostics.RecordUniqueTypeResolve();
        return uniqueTypeById.TryGetValue(AuraGameDataKey.NormalizeId(id), out dataType!);
    }

    public bool TryResolveHandle(
        AuraGameDataDefinitionHandle? handle,
        out AuraGameDataSnapshot? snapshot)
    {
        snapshot = null;
        if (handle?.Key == null
            || handle.CatalogEpoch != Version.Epoch
            || string.IsNullOrWhiteSpace(handle.SelectionIdentity)
            || string.IsNullOrWhiteSpace(handle.Token)
            || !string.Equals(handle.Token, handle.SelectionIdentity, StringComparison.Ordinal)
            || !definitionsByIdentity.TryGetValue(handle.SelectionIdentity, out var resolved))
        {
            return false;
        }

        if (!string.Equals(resolved.DataType, handle.Key.DataType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(resolved.Id, handle.Key.Id, StringComparison.Ordinal)
            || !string.Equals(resolved.OwnerModId, handle.OwnerModId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(resolved.SourceKind, handle.SourceKind, StringComparison.OrdinalIgnoreCase)
            || resolved.Revision != handle.Revision)
        {
            return false;
        }

        snapshot = resolved;
        return true;
    }

    public AuraGameDataQueryResult Inspect(AuraGameDataQuery? query)
    {
        query ??= new AuraGameDataQuery();
        query.Normalize();
        if (query.IncludeHistory)
        {
            var inspectedHistory = Rank(
                history.Where(value => query.DataType.Length == 0
                    || string.Equals(value.DataType, query.DataType, StringComparison.OrdinalIgnoreCase)),
                query);
            return new AuraGameDataQueryResult(Version.Epoch, inspectedHistory);
        }

        if (!types.TryGetValue(query.DataType, out var index))
        {
            return new AuraGameDataQueryResult(Version.Epoch, Array.Empty<AuraGameDataSnapshot>());
        }

        if (query.CandidateIds.Count == 0
            && query.OwnerModIds.Count == 0
            && !query.IncludeDisabled
            && !query.IncludeAllCandidates
            && AuraGameDataQuery.NormalizeSourceOrder(query.SourceOrder)
                .SequenceEqual(AuraGameDataSourceKinds.DefaultSearchOrder, StringComparer.OrdinalIgnoreCase))
        {
            return new AuraGameDataQueryResult(Version.Epoch, index.Rows);
        }

        return new AuraGameDataQueryResult(Version.Epoch, Rank(index.AllCandidates, query));
    }

    private static IReadOnlyList<AuraGameDataSnapshot> Rank(
        IEnumerable<AuraGameDataSnapshot> source,
        AuraGameDataQuery query)
    {
        var ranked = source
            .Where(value => query.IncludeDisabled || value.Enabled)
            .Where(value => query.IncludeHistory || !value.Retired)
            .Where(value => query.OwnerModIds.Count == 0
                || query.OwnerModIds.Contains(value.OwnerModId, StringComparer.OrdinalIgnoreCase))
            .Select(value => new RankedSnapshot(
                value,
                CandidateRank(value, query.CandidateIds),
                SourceRank(value.SourceKind, query.SourceOrder)))
            .Where(value => query.CandidateIds.Count == 0 || value.CandidateRank < int.MaxValue)
            .OrderBy(value => value.CandidateRank)
            .ThenBy(value => value.SourceRank)
            .ThenByDescending(value => value.Snapshot.Priority)
            .ThenByDescending(value => value.Snapshot.Revision)
            .ThenBy(value => value.Snapshot.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!query.IncludeAllCandidates)
        {
            ranked = ranked
                .GroupBy(value => value.Snapshot.DataType + ":" + value.Snapshot.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(value => value.CandidateRank)
                .ThenBy(value => value.Snapshot.Id, StringComparer.Ordinal)
                .ToList();
        }

        return ranked.Select(value => value.Snapshot).ToArray();
    }

    private static int CandidateRank(AuraGameDataSnapshot snapshot, IReadOnlyList<string> candidateIds)
    {
        if (candidateIds.Count == 0)
        {
            return 0;
        }

        for (var index = 0; index < candidateIds.Count; index++)
        {
            var candidate = candidateIds[index];
            if (string.Equals(snapshot.Id, candidate, StringComparison.Ordinal)
                || snapshot.Aliases.Contains(candidate, StringComparer.Ordinal))
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

    internal sealed class TypeIndex
    {
        private readonly Dictionary<string, AuraGameDataSnapshot> effectiveByIdOrAlias;

        public TypeIndex(
            Dictionary<string, AuraGameDataSnapshot> effectiveByIdOrAlias,
            IReadOnlyList<AuraGameDataSnapshot> rows,
            IReadOnlyList<AuraGameDataSnapshot> allCandidates)
        {
            this.effectiveByIdOrAlias = effectiveByIdOrAlias;
            Rows = new ReadOnlyCollection<AuraGameDataSnapshot>(rows.ToArray());
            AllCandidates = new ReadOnlyCollection<AuraGameDataSnapshot>(allCandidates.ToArray());
        }

        public IReadOnlyList<AuraGameDataSnapshot> Rows { get; }

        public IReadOnlyList<AuraGameDataSnapshot> AllCandidates { get; }

        public IEnumerable<string> SearchIds => effectiveByIdOrAlias.Keys;

        public bool TryGet(string id, out AuraGameDataSnapshot? snapshot)
        {
            return effectiveByIdOrAlias.TryGetValue(id, out snapshot);
        }
    }

    private sealed class RankedSnapshot
    {
        public RankedSnapshot(AuraGameDataSnapshot snapshot, int candidateRank, int sourceRank)
        {
            Snapshot = snapshot;
            CandidateRank = candidateRank;
            SourceRank = sourceRank;
        }

        public AuraGameDataSnapshot Snapshot { get; }

        public int CandidateRank { get; }

        public int SourceRank { get; }
    }
}

public static class AuraGameDataCatalogCompiler
{
    public static AuraGameDataCatalogSnapshot Compile(
        AuraGameDataSourceSnapshot? nativeSource,
        AuraGameDataRegistryDocument? registry,
        long registryRevision,
        long epoch)
    {
        var started = Stopwatch.GetTimestamp();
        nativeSource ??= new AuraGameDataSourceSnapshot(0, Array.Empty<AuraGameDataDefinition>());
        registry = registry?.Clone() ?? new AuraGameDataRegistryDocument();
        registry.Normalize();

        var nativeDefinitions = nativeSource.Definitions
            .Where(value => value != null)
            .Select(value => PrepareNative(value, nativeSource.Generation, registry.OwnerRules))
            .Where(value => value != null)
            .Cast<AuraGameDataDefinition>()
            .ToList();
        var nativeByKey = nativeDefinitions
            .GroupBy(value => value.Key.Canonical, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var candidates = new List<AuraGameDataDefinition>(nativeDefinitions.Count + registry.Definitions.Count);
        candidates.AddRange(nativeDefinitions);
        foreach (var registered in registry.Definitions)
        {
            var prepared = PrepareRegistered(registered, nativeByKey);
            if (prepared != null)
            {
                candidates.Add(prepared);
            }
        }

        var version = new AuraGameDataCatalogVersion(
            epoch,
            nativeSource.Generation,
            registryRevision,
            AuraGameDataConstants.PolicyVersion,
            nativeSource.IsComplete);
        var definitionsByIdentity = new Dictionary<string, AuraGameDataSnapshot>(StringComparer.Ordinal);
        var types = new Dictionary<string, AuraGameDataCatalogSnapshot.TypeIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var typeGroup in candidates
                     .Where(value => !value.Retired)
                     .GroupBy(value => value.Key.DataType, StringComparer.OrdinalIgnoreCase))
        {
            var snapshots = typeGroup.Select(value => new AuraGameDataSnapshot(value, epoch)).ToArray();
            foreach (var snapshot in snapshots.Where(value => value.Enabled))
            {
                definitionsByIdentity[snapshot.SelectionIdentity] = snapshot;
            }

            var enabledSnapshots = snapshots.Where(value => value.Enabled).ToArray();
            var rows = enabledSnapshots
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .Select(group => RankDefault(group).First())
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            var search = new Dictionary<string, AuraGameDataSnapshot>(StringComparer.Ordinal);
            foreach (var searchGroup in enabledSnapshots
                         .SelectMany(value => value.Aliases
                             .Concat(new[] { value.Id })
                             .Distinct(StringComparer.Ordinal)
                             .Select(id => new SearchCandidate(id, value)))
                         .GroupBy(value => value.Id, StringComparer.Ordinal))
            {
                search[searchGroup.Key] = RankDefault(searchGroup.Select(value => value.Snapshot)).First();
            }

            types[typeGroup.Key] = new AuraGameDataCatalogSnapshot.TypeIndex(search, rows, snapshots);
        }

        var typeSets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var pair in types)
        {
            foreach (var id in pair.Value.SearchIds)
            {
                if (!typeSets.TryGetValue(id, out var values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    typeSets[id] = values;
                }

                values.Add(pair.Key);
            }
        }

        var uniqueTypeById = typeSets
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value.First(), StringComparer.Ordinal);
        var history = registry.History
            .Select(value => new AuraGameDataSnapshot(value, epoch))
            .OrderBy(value => value.DataType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ThenByDescending(value => value.Revision)
            .ToArray();
        var result = new AuraGameDataCatalogSnapshot(version, types, uniqueTypeById, definitionsByIdentity, history);
        AuraGameDataDiagnostics.RecordCatalogBuild(Stopwatch.GetTimestamp() - started);
        return result;
    }

    private static AuraGameDataDefinition? PrepareNative(
        AuraGameDataDefinition value,
        long nativeGeneration,
        IReadOnlyList<AuraGameDataOwnerRule> ownerRules)
    {
        var definition = value.Clone();
        definition.SchemaVersion = AuraGameDataConstants.SchemaVersion;
        definition.SourceKind = AuraGameDataSourceKinds.Native;
        definition.StorageKind = AuraGameDataStorageKinds.Inline;
        definition.WriterId = AuraGameDataConstants.RegistryAuthorityId;
        definition.Revision = Math.Max(1, nativeGeneration);
        definition.Enabled = true;
        definition.Retired = false;
        var owner = ownerRules.FirstOrDefault(rule =>
            definition.Key.Id.StartsWith(rule.IdPrefix, StringComparison.OrdinalIgnoreCase));
        if (owner != null)
        {
            definition.OwnerModId = owner.OwnerModId;
        }

        if (string.IsNullOrWhiteSpace(definition.OwnerModId))
        {
            definition.OwnerModId = "BaseGame";
        }

        definition.Normalize();
        return definition.Key.DataType.Length == 0 || definition.Key.Id.Length == 0 ? null : definition;
    }

    private static AuraGameDataDefinition? PrepareRegistered(
        AuraGameDataDefinition value,
        IReadOnlyDictionary<string, AuraGameDataDefinition> nativeByKey)
    {
        var definition = value.Clone();
        definition.Normalize();
        if (definition.Retired || !definition.Enabled)
        {
            return definition;
        }

        if (string.Equals(definition.StorageKind, AuraGameDataStorageKinds.Overlay, StringComparison.OrdinalIgnoreCase))
        {
            if (!nativeByKey.TryGetValue(definition.Key.Canonical, out var native))
            {
                return null;
            }

            var merged = new Dictionary<string, string>(native.Fields, StringComparer.Ordinal);
            foreach (var field in definition.RemoveFields)
            {
                merged.Remove(field);
            }

            foreach (var pair in definition.Fields)
            {
                merged[pair.Key] = pair.Value ?? "";
            }

            merged["Id"] = definition.Key.Id;
            definition.Fields = merged;
        }

        definition.Normalize();
        return definition;
    }

    private static IOrderedEnumerable<AuraGameDataSnapshot> RankDefault(
        IEnumerable<AuraGameDataSnapshot> values)
    {
        return values
            .OrderBy(value => SourceRank(value.SourceKind))
            .ThenByDescending(value => value.Priority)
            .ThenByDescending(value => value.Revision)
            .ThenBy(value => value.OwnerModId, StringComparer.OrdinalIgnoreCase);
    }

    private static int SourceRank(string value)
    {
        for (var index = 0; index < AuraGameDataSourceKinds.DefaultSearchOrder.Count; index++)
        {
            if (string.Equals(value, AuraGameDataSourceKinds.DefaultSearchOrder[index], StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private sealed class SearchCandidate
    {
        public SearchCandidate(string id, AuraGameDataSnapshot snapshot)
        {
            Id = id;
            Snapshot = snapshot;
        }

        public string Id { get; }

        public AuraGameDataSnapshot Snapshot { get; }
    }
}

public static class AuraGameDataCatalogRuntime
{
    private static readonly object Gate = new();
    private static IAuraGameDataSource source = new EmptySource();
    private static AuraGameDataRegistryDocument? cachedDocument;
    private static long cachedRevision = -1;
    private static long nextEpoch;
    private static AuraGameDataCatalogState state = AuraGameDataCatalogState.Uninitialized;
    private static Action? rebuildScheduler;
    private static AuraGameDataCatalogSnapshot current = AuraGameDataCatalogCompiler.Compile(
        new AuraGameDataSourceSnapshot(0, Array.Empty<AuraGameDataDefinition>(), isComplete: false),
        new AuraGameDataRegistryDocument(),
        0,
        0);

    public static event Action<long>? Changed;

    public static event Action<AuraGameDataCatalogVersion>? SnapshotChanged;

    public static AuraGameDataCatalogState State
    {
        get
        {
            lock (Gate)
            {
                return state;
            }
        }
    }

    public static void ConfigureSource(IAuraGameDataSource gameDataSource)
    {
        ConfigureSource(gameDataSource, rebuildImmediately: true);
    }

    public static void ConfigureSource(
        IAuraGameDataSource gameDataSource,
        bool rebuildImmediately)
    {
        lock (Gate)
        {
            source = gameDataSource ?? new EmptySource();
            state = AuraGameDataCatalogState.Invalidated;
        }

        if (rebuildImmediately)
        {
            Rebuild();
        }
    }

    public static void ConfigureRebuildScheduler(Action? scheduler)
    {
        lock (Gate)
        {
            rebuildScheduler = scheduler;
        }
    }

    public static AuraGameDataCatalogSnapshot AcquireSnapshot()
    {
        return Volatile.Read(ref current);
    }

    public static bool TryGet(string dataType, string id, out AuraGameDataSnapshot? snapshot)
    {
        return AcquireSnapshot().TryGet(dataType, id, out snapshot);
    }

    public static IReadOnlyList<AuraGameDataSnapshot> GetTable(string dataType)
    {
        return AcquireSnapshot().GetTable(dataType);
    }

    public static bool TryResolveUniqueType(string id, out string dataType)
    {
        return AcquireSnapshot().TryResolveUniqueType(id, out dataType);
    }

    public static AuraGameDataQueryResult Query(AuraGameDataQuery query)
    {
        return AcquireSnapshot().Inspect(query);
    }

    public static AuraGameDataQueryResult QueryHistory(AuraGameDataQuery query)
    {
        query ??= new AuraGameDataQuery();
        query.IncludeHistory = true;
        query.IncludeDisabled = true;
        query.IncludeAllCandidates = true;
        return AcquireSnapshot().Inspect(query);
    }

    public static AuraGameDataSnapshot? Resolve(string dataType, IEnumerable<string> candidateIds)
    {
        return AcquireSnapshot().Resolve(dataType, candidateIds);
    }

    public static AuraGameDataMutationResult Register(
        string callerId,
        AuraGameDataDefinition definition,
        long expectedRevision = -1)
    {
        return RegisterBatchCore(callerId, new[] { definition }, expectedRevision);
    }

    public static AuraGameDataMutationResult RegisterBatch(
        string callerId,
        IEnumerable<AuraGameDataDefinition> definitions)
    {
        return RegisterBatchCore(callerId, definitions, -1);
    }

    public static AuraGameDataMutationResult RegisterOwnerRules(
        string callerId,
        IEnumerable<AuraGameDataOwnerRule> rules)
    {
        callerId = (callerId ?? "").Trim();
        var batch = (rules ?? Array.Empty<AuraGameDataOwnerRule>())
            .Where(value => value != null)
            .Select(value => value.Clone())
            .ToList();
        foreach (var rule in batch)
        {
            rule.Normalize();
            if (callerId.Length == 0
                || rule.OwnerModId.Length == 0
                || rule.WriterId.Length == 0
                || rule.IdPrefix.Length == 0
                || !string.Equals(callerId, rule.WriterId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(callerId, rule.OwnerModId, StringComparison.OrdinalIgnoreCase))
            {
                return AuraGameDataMutationResult.Failed("Owner-rule identity, owner, writer, and prefix are required.");
            }
        }

        if (batch.Count == 0)
        {
            return AuraGameDataMutationResult.Failed("At least one owner rule is required.");
        }

        return Mutate(document =>
        {
            var byKey = document.OwnerRules.ToDictionary(
                value => value.OwnerModId + "\u001f" + value.IdPrefix,
                value => value,
                StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var rule in batch)
            {
                var key = rule.OwnerModId + "\u001f" + rule.IdPrefix;
                if (!byKey.TryGetValue(key, out var existing)
                    || existing.Priority != rule.Priority
                    || !string.Equals(existing.WriterId, rule.WriterId, StringComparison.OrdinalIgnoreCase))
                {
                    byKey[key] = rule;
                    changed = true;
                }
            }

            document.OwnerRules = byKey.Values.ToList();
            document.Normalize();
            return changed
                ? PendingMutation.Applied(null)
                : PendingMutation.Unchanged(null);
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
        if (ContainsScriptField(patch.SetFields.Keys) || ContainsScriptField(patch.RemoveFields))
        {
            return AuraGameDataMutationResult.Failed("Script fields are registration-time only.");
        }

        return Mutate(document =>
        {
            var existing = Find(document.Definitions, key, ownerModId);
            if (existing == null)
            {
                return PendingMutation.ConflictResult("Definition does not exist.", 0);
            }

            if (!CanWrite(callerId, existing)
                || expectedRevision >= 0 && existing.Revision != expectedRevision)
            {
                return PendingMutation.ConflictResult("Definition ownership or revision conflict.", existing.Revision);
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

            next.Enabled = patch.Enabled ?? next.Enabled;
            next.Priority = patch.Priority ?? next.Priority;
            next.Revision++;
            next.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            Replace(document.Definitions, next);
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
            var existing = Find(document.Definitions, key, ownerModId);
            if (existing == null)
            {
                return PendingMutation.ConflictResult("Definition does not exist.", 0);
            }

            if (!CanWrite(callerId, existing)
                || expectedRevision >= 0 && existing.Revision != expectedRevision)
            {
                return PendingMutation.ConflictResult("Definition ownership or revision conflict.", existing.Revision);
            }

            var retired = existing.Clone();
            retired.Retired = true;
            retired.Enabled = false;
            retired.Revision++;
            retired.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            document.Definitions.RemoveAll(value => SameDefinition(value, key, ownerModId));
            document.History.RemoveAll(value => SameDefinition(value, key, ownerModId));
            document.History.Add(retired);
            document.Normalize();
            return PendingMutation.Applied(retired);
        });
    }

    public static bool ValidateHandle(
        AuraGameDataDefinitionHandle? handle,
        out AuraGameDataSnapshot? snapshot)
    {
        return AcquireSnapshot().TryResolveHandle(handle, out snapshot);
    }

    public static AuraGameDataDefinitionHandle CreateHandle(AuraGameDataSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return new AuraGameDataDefinitionHandle
        {
            Key = new AuraGameDataKey(snapshot.DataType, snapshot.Id),
            OwnerModId = snapshot.OwnerModId,
            SourceKind = snapshot.SourceKind,
            Revision = snapshot.Revision,
            CatalogEpoch = snapshot.CatalogEpoch,
            SelectionIdentity = snapshot.SelectionIdentity,
            Token = snapshot.SelectionIdentity
        };
    }

    public static void Invalidate()
    {
        IAuraGameDataSource currentSource;
        lock (Gate)
        {
            state = AuraGameDataCatalogState.Invalidated;
            currentSource = source;
        }

        currentSource.Invalidate();
        Rebuild();
    }

    public static void Rebuild()
    {
        try
        {
            Publish(Compile(CaptureBuildRequest()));
        }
        catch
        {
            lock (Gate)
            {
                state = AuraGameDataCatalogState.Failed;
            }
        }
    }

    public static AuraGameDataCatalogBuildRequest CaptureBuildRequest()
    {
        IAuraGameDataSource currentSource;
        lock (Gate)
        {
            state = AuraGameDataCatalogState.Capturing;
            currentSource = source;
        }

        var captured = currentSource.Capture();
        var document = ReadDocument(out var registryRevision);
        lock (Gate)
        {
            state = captured.IsComplete && captured.Generation >= currentSource.Revision
                ? AuraGameDataCatalogState.Compiling
                : AuraGameDataCatalogState.AwaitingNativeCapture;
            return new AuraGameDataCatalogBuildRequest(
                captured,
                document,
                registryRevision,
                ++nextEpoch);
        }
    }

    public static AuraGameDataCatalogSnapshot Compile(AuraGameDataCatalogBuildRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return AuraGameDataCatalogCompiler.Compile(
            request.Source,
            request.Registry,
            request.RegistryRevision,
            request.Epoch);
    }

    public static bool IsBuildCurrent(AuraGameDataCatalogBuildRequest request)
    {
        if (request == null)
        {
            return false;
        }

        lock (Gate)
        {
            return request.Source.IsComplete
                && request.Source.Generation >= source.Revision
                && request.RegistryRevision >= Math.Max(0, cachedRevision)
                && request.Epoch >= current.Version.Epoch;
        }
    }

    public static bool Publish(AuraGameDataCatalogSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        lock (Gate)
        {
            if (!snapshot.Version.NativeReady)
            {
                state = AuraGameDataCatalogState.AwaitingNativeCapture;
                return false;
            }

            if (snapshot.Version.NativeGeneration < source.Revision
                || snapshot.Version.RegistryRevision < Math.Max(0, cachedRevision))
            {
                if (snapshot.Version.NativeGeneration < source.Revision)
                {
                    state = AuraGameDataCatalogState.AwaitingNativeCapture;
                }
                return false;
            }

            Volatile.Write(ref current, snapshot);
            nextEpoch = Math.Max(nextEpoch, snapshot.Version.Epoch);
            state = AuraGameDataCatalogState.Ready;
        }

        AuraGameDataDiagnostics.RecordPublishedEpoch(snapshot.Version.Epoch);
        NotifyChanged(snapshot.Version);
        return true;
    }

    private static AuraGameDataMutationResult RegisterBatchCore(
        string callerId,
        IEnumerable<AuraGameDataDefinition> definitions,
        long expectedRevision)
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
            var byKey = document.Definitions.ToDictionary(
                value => value.QualifiedId,
                value => value,
                StringComparer.OrdinalIgnoreCase);
            AuraGameDataDefinition? last = null;
            var changed = false;
            foreach (var definition in batch)
            {
                byKey.TryGetValue(definition.QualifiedId, out var existing);
                if (existing != null && !CanWrite(callerId, existing))
                {
                    return PendingMutation.ConflictResult("Only the owner or writer may update a definition.", existing.Revision);
                }

                if (existing != null && expectedRevision >= 0 && existing.Revision != expectedRevision)
                {
                    return PendingMutation.ConflictResult("Definition revision conflict.", existing.Revision);
                }

                if (existing != null && SemanticallyEquals(existing, definition))
                {
                    last = existing;
                    continue;
                }

                var next = definition.Clone();
                next.Revision = Math.Max(1, (existing?.Revision ?? 0) + 1);
                next.Retired = false;
                next.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                byKey[next.QualifiedId] = next;
                document.History.RemoveAll(value => SameDefinition(value, next.Key, next.OwnerModId));
                last = next;
                changed = true;
            }

            document.Definitions = byKey.Values.ToList();
            document.Normalize();
            return changed
                ? PendingMutation.Applied(last)
                : PendingMutation.Unchanged(last);
        });
    }

    private static AuraGameDataMutationResult Mutate(
        Func<AuraGameDataRegistryDocument, PendingMutation> change)
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
                return MutationResult(true, "Unchanged", storageRevision, pending.Definition);
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
                RequestRebuild();
                return MutationResult(true, "Applied", write.Revision, pending.Definition);
            }

            if (!write.Conflict)
            {
                return AuraGameDataMutationResult.Failed(write.Message, false, write.Revision);
            }

            InvalidateDocument();
        }

        return AuraGameDataMutationResult.Failed("Registry write conflicted repeatedly.", true);
    }

    private static AuraGameDataMutationResult MutationResult(
        bool success,
        string message,
        long revision,
        AuraGameDataDefinition? definition)
    {
        AuraGameDataDefinitionHandle? handle = null;
        if (definition != null)
        {
            var snapshot = AcquireSnapshot()
                .Inspect(new AuraGameDataQuery
                {
                    DataType = definition.Key.DataType,
                    CandidateIds = new List<string> { definition.Key.Id },
                    OwnerModIds = new List<string> { definition.OwnerModId },
                    IncludeAllCandidates = true,
                    IncludeDisabled = true
                })
                .Items
                .FirstOrDefault(value =>
                    string.Equals(value.SourceKind, definition.SourceKind, StringComparison.OrdinalIgnoreCase)
                    && value.Revision == definition.Revision);
            if (snapshot != null)
            {
                handle = CreateHandle(snapshot);
            }
        }

        return new AuraGameDataMutationResult
        {
            Success = success,
            Message = message,
            Revision = Math.Max(0, revision),
            Handle = handle
        };
    }

    private static AuraGameDataRegistryDocument ReadDocument(out long revision)
    {
        lock (Gate)
        {
            if (cachedDocument != null)
            {
                revision = Math.Max(0, cachedRevision);
                return cachedDocument;
            }
        }

        var stored = AuraSharedConfigStore.ReadShared(
            AuraGameDataConstants.RegistryAuthorityId,
            AuraGameDataConstants.SystemName,
            AuraGameDataConstants.RegistryFileName,
            new AuraGameDataRegistryDocument());
        var document = stored.SchemaVersion != 0
                       && stored.SchemaVersion != AuraGameDataConstants.SchemaVersion
            ? new AuraGameDataRegistryDocument()
            : stored.Value ?? new AuraGameDataRegistryDocument();
        document.Normalize();
        revision = stored.Found ? Math.Max(0, stored.Revision) : 0;
        Cache(document, revision);
        return document;
    }

    private static void RequestRebuild()
    {
        Action? scheduler;
        lock (Gate)
        {
            scheduler = rebuildScheduler;
        }

        if (scheduler != null)
        {
            scheduler();
        }
        else
        {
            Rebuild();
        }
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
            failure = "Only schemaVersion 5 game-data registration is accepted.";
            return false;
        }

        if (callerId.Length == 0
            || definition.Key.DataType.Length == 0
            || definition.Key.Id.Length == 0
            || definition.OwnerModId.Length == 0
            || definition.WriterId.Length == 0
            || !string.Equals(callerId, definition.WriterId, StringComparison.OrdinalIgnoreCase))
        {
            failure = "Definition identity, owner, writer, and matching caller are required.";
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
        return (fields ?? Array.Empty<string>()).Any(AuraGameDataFieldPolicy.IsScriptField);
    }

    private static AuraGameDataDefinition? Find(
        IEnumerable<AuraGameDataDefinition> values,
        AuraGameDataKey key,
        string ownerModId)
    {
        return values.FirstOrDefault(value => SameDefinition(value, key, ownerModId));
    }

    private static bool SameDefinition(
        AuraGameDataDefinition value,
        AuraGameDataKey key,
        string ownerModId)
    {
        return value.Key.Equals(key)
            && string.Equals(value.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase);
    }

    private static void Replace(
        List<AuraGameDataDefinition> values,
        AuraGameDataDefinition definition)
    {
        values.RemoveAll(value => SameDefinition(value, definition.Key, definition.OwnerModId));
        values.Add(definition);
    }

    private static bool SemanticallyEquals(
        AuraGameDataDefinition left,
        AuraGameDataDefinition right)
    {
        return left.Key.Equals(right.Key)
            && string.Equals(left.OwnerModId, right.OwnerModId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.WriterId, right.WriterId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.SourceKind, right.SourceKind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.StorageKind, right.StorageKind, StringComparison.OrdinalIgnoreCase)
            && left.Priority == right.Priority
            && left.Enabled == right.Enabled
            && left.Aliases.SequenceEqual(right.Aliases, StringComparer.Ordinal)
            && left.RemoveFields.SequenceEqual(right.RemoveFields, StringComparer.Ordinal)
            && left.Fields.Count == right.Fields.Count
            && left.Fields.All(pair => right.Fields.TryGetValue(pair.Key, out var value)
                && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static void Cache(AuraGameDataRegistryDocument document, long revision)
    {
        document.Normalize();
        lock (Gate)
        {
            cachedDocument = document;
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

    private static void NotifyChanged(AuraGameDataCatalogVersion version)
    {
        try
        {
            Changed?.Invoke(version.Epoch);
            SnapshotChanged?.Invoke(version);
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

        public static PendingMutation Applied(AuraGameDataDefinition? definition)
        {
            return new PendingMutation
            {
                Success = true,
                RequiresWrite = true,
                Definition = definition?.Clone(),
                Revision = definition?.Revision ?? 0
            };
        }

        public static PendingMutation Unchanged(AuraGameDataDefinition? definition)
        {
            return new PendingMutation
            {
                Success = true,
                Definition = definition?.Clone(),
                Revision = definition?.Revision ?? 0
            };
        }

        public static PendingMutation ConflictResult(string message, long revision)
        {
            return new PendingMutation
            {
                Conflict = true,
                Message = message,
                Revision = Math.Max(0, revision)
            };
        }
    }

    private sealed class EmptySource : IAuraGameDataSource
    {
        public long Revision => 0;

        public AuraGameDataSourceSnapshot Capture()
        {
            return new AuraGameDataSourceSnapshot(0, Array.Empty<AuraGameDataDefinition>());
        }

        public void Invalidate()
        {
        }
    }
}
