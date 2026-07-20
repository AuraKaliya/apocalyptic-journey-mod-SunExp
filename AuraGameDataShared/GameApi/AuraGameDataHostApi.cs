using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Witch.Core;

namespace AuraGameData.Shared.GameApi;

public enum AuraGameDataFieldAccess
{
    Base,
    Runtime,
    Effective
}

public sealed class AuraGameDataMaterializeRequest
{
    public AuraGameDataDefinitionHandle? Definition { get; set; }

    public Dictionary<string, string> Vars { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> DataOverrides { get; set; } = new(StringComparer.Ordinal);

    public bool PreCompile { get; set; } = true;
}

public sealed class AuraGameDataHostMutationResult
{
    public bool Success { get; set; }

    public string FailureStep { get; set; } = "";

    public string Message { get; set; } = "";

    public IDataConfig? Instance { get; set; }

    public static AuraGameDataHostMutationResult Fail(string step, string message)
    {
        return new AuraGameDataHostMutationResult
        {
            FailureStep = step ?? "",
            Message = message ?? ""
        };
    }
}

public static class AuraGameDataHostApi
{
    private static readonly AuraGameDataNativeSource NativeSource = new();
    private static readonly object RegistrationGate = new();
    private static readonly Dictionary<string, string[]> OwnerRegistrationPrefixes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PendingOwnerRegistrations = new(StringComparer.OrdinalIgnoreCase);
    private static bool registrationFlushActive;

    static AuraGameDataHostApi()
    {
        AuraGameDataCatalogRuntime.ConfigureSource(NativeSource);
    }

    public static AuraGameDataQueryResult Query(DataType dataType, bool includeAllCandidates = false)
    {
        EnsureConfigured();
        return AuraGameDataCatalogRuntime.Query(new AuraGameDataQuery
        {
            DataType = dataType.ToString(),
            IncludeAllCandidates = includeAllCandidates
        });
    }

    public static AuraGameDataQueryResult QueryHistory(DataType dataType)
    {
        EnsureConfigured();
        return AuraGameDataCatalogRuntime.QueryHistory(new AuraGameDataQuery
        {
            DataType = dataType.ToString(),
            IncludeAllCandidates = true,
            IncludeDisabled = true,
            IncludeHistory = true
        });
    }

    public static AuraGameDataSnapshot? Resolve(DataType dataType, params string[] candidateIds)
    {
        EnsureConfigured();
        return AuraGameDataCatalogRuntime.Resolve(dataType.ToString(), candidateIds ?? Array.Empty<string>());
    }

    public static string ResolveId(DataType dataType, IEnumerable<string> candidateIds, string fallback = "")
    {
        EnsureConfigured();
        var candidates = (candidateIds ?? Array.Empty<string>()).ToList();
        return AuraGameDataCatalogRuntime.Resolve(dataType.ToString(), candidates)?.Key.Id
            ?? AuraGameDataKey.NormalizeId(fallback)
            ?? "";
    }

    public static List<Dictionary<string, string>> Rows(DataType dataType)
    {
        return Query(dataType)
            .Items
            .Select(item => item.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
            .ToList();
    }

    public static Dictionary<string, string>? Row(DataType dataType, params string[] candidateIds)
    {
        var resolved = Resolve(dataType, candidateIds);
        return resolved == null
            ? null
            : resolved.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public static AuraGameDataDefinitionHandle? ResolveHandle(DataType dataType, params string[] candidateIds)
    {
        var snapshot = Resolve(dataType, candidateIds);
        return snapshot == null ? null : AuraGameDataCatalogRuntime.CreateHandle(snapshot);
    }

    public static DataType? ResolveDataType(string id)
    {
        return ResolveDataType(id, Enum.GetValues(typeof(DataType)).Cast<DataType>());
    }

    public static DataType? ResolveDataType(string id, IEnumerable<DataType> searchOrder)
    {
        id = AuraGameDataKey.NormalizeId(id);
        if (id.Length == 0)
        {
            return null;
        }

        var matches = (searchOrder ?? Array.Empty<DataType>())
            .Distinct()
            .Where(dataType => Resolve(dataType, id) != null)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public static AuraGameDataInstanceSnapshot Capture(IDataConfig? instance)
    {
        if (instance == null)
        {
            return new AuraGameDataInstanceSnapshot();
        }

        var data = instance.data == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(instance.data, StringComparer.Ordinal);
        var vars = instance.Vars == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(instance.Vars, StringComparer.Ordinal);
        var id = AuraSharedDictionary.Get(data, "Id", AuraSharedDictionary.Get(vars, "Id"));
        return new AuraGameDataInstanceSnapshot
        {
            Key = new AuraGameDataKey(instance.Type.ToString(), id),
            InstanceId = instance.InstanceID ?? AuraSharedDictionary.Get(vars, "InstanceID"),
            Data = data,
            Vars = vars
        };
    }

    public static AuraGameDataHostMutationResult PatchVars(IDataConfig? instance, AuraGameDataInstancePatch patch)
    {
        if (instance?.Vars == null)
        {
            return AuraGameDataHostMutationResult.Fail("instance", "IDataConfig Vars are unavailable.");
        }

        patch ??= new AuraGameDataInstancePatch();
        var fields = (patch.SetVars?.Keys ?? Enumerable.Empty<string>())
            .Concat(patch.RemoveVars ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var forbidden = fields.FirstOrDefault(IsIdentityOrScriptField);
        if (!string.IsNullOrWhiteSpace(forbidden))
        {
            return AuraGameDataHostMutationResult.Fail("validate", "Runtime patch may not change field: " + forbidden);
        }

        foreach (var pair in patch.SetVars ?? new Dictionary<string, string>())
        {
            instance.Vars[pair.Key] = pair.Value ?? "";
        }

        foreach (var field in patch.RemoveVars ?? new List<string>())
        {
            instance.Vars.Remove(field);
        }

        return new AuraGameDataHostMutationResult
        {
            Success = true,
            Instance = instance,
            Message = "Applied"
        };
    }

    public static AuraGameDataHostMutationResult Materialize(AuraGameDataMaterializeRequest request)
    {
        if (request?.Definition == null)
        {
            return AuraGameDataHostMutationResult.Fail("handle", "A registered definition handle is required.");
        }

        EnsureConfigured();
        if (!AuraGameDataCatalogRuntime.ValidateHandle(request.Definition, out var snapshot) || snapshot == null)
        {
            return AuraGameDataHostMutationResult.Fail("handle", "Definition handle is stale or invalid.");
        }

        if (!Enum.TryParse(snapshot.Key.DataType, true, out DataType dataType))
        {
            return AuraGameDataHostMutationResult.Fail("type", "Unsupported DataType: " + snapshot.Key.DataType);
        }

        if ((request.DataOverrides?.Keys ?? Enumerable.Empty<string>()).Any(IsIdentityOrScriptField))
        {
            return AuraGameDataHostMutationResult.Fail("validate", "Data overrides may not change identity or script fields.");
        }

        try
        {
            DataConfig instance;
            var overrides = request.DataOverrides ?? new Dictionary<string, string>();
            if (string.Equals(snapshot.SourceKind, AuraGameDataSourceKinds.Native, StringComparison.OrdinalIgnoreCase)
                && overrides.Count == 0
                && request.PreCompile)
            {
                instance = new DataConfig(snapshot.Key.Id, dataType);
            }
            else
            {
                var data = snapshot.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var pair in overrides)
                {
                    data[pair.Key] = pair.Value ?? "";
                }

                data["Id"] = snapshot.Key.Id;
                instance = new DataConfig(
                    data,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    request.PreCompile,
                    dataType);
            }

            var patch = PatchVars(instance, new AuraGameDataInstancePatch
            {
                SetVars = new Dictionary<string, string>(request.Vars ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            });
            return patch.Success
                ? new AuraGameDataHostMutationResult { Success = true, Instance = instance, Message = "Materialized" }
                : patch;
        }
        catch (Exception ex)
        {
            return AuraGameDataHostMutationResult.Fail("materialize", ex.Message);
        }
    }

    public static AuraGameDataHostMutationResult Materialize(DataType dataType, params string[] candidateIds)
    {
        var handle = ResolveHandle(dataType, candidateIds);
        return handle == null
            ? AuraGameDataHostMutationResult.Fail("resolve", "Registered definition was not found.")
            : Materialize(new AuraGameDataMaterializeRequest { Definition = handle });
    }

    public static DataConfig CloneWritable(
        IDataConfig source,
        IReadOnlyDictionary<string, string>? dataOverrides = null,
        IReadOnlyDictionary<string, string>? varsOverrides = null,
        bool preCompile = true)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var data = source.data == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source.data, StringComparer.Ordinal);
        var vars = source.Vars == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source.Vars, StringComparer.Ordinal);
        foreach (var pair in dataOverrides ?? new Dictionary<string, string>())
        {
            if (!IsIdentityOrScriptField(pair.Key))
            {
                data[pair.Key] = pair.Value ?? "";
            }
        }

        foreach (var pair in varsOverrides ?? new Dictionary<string, string>())
        {
            if (!IsIdentityOrScriptField(pair.Key))
            {
                vars[pair.Key] = pair.Value ?? "";
            }
        }

        data["Id"] = AuraSharedDictionary.Get(source.data, "Id", AuraSharedDictionary.Get(source.Vars, "Id"));
        return new DataConfig(data, vars, preCompile, source.Type);
    }

    public static string ReadField(IDataConfig? instance, string field, AuraGameDataFieldAccess access, string fallback = "")
    {
        if (instance == null || string.IsNullOrWhiteSpace(field))
        {
            return fallback;
        }

        if (access == AuraGameDataFieldAccess.Runtime)
        {
            return AuraSharedDictionary.Get(instance.Vars, field, fallback);
        }

        if (access == AuraGameDataFieldAccess.Effective
            && instance.Vars != null
            && instance.Vars.TryGetValue(field, out var runtimeValue))
        {
            return runtimeValue ?? fallback;
        }

        return AuraSharedDictionary.Get(instance.data, field, fallback);
    }

    public static void RegisterOwnerPrefix(string ownerModId, params string[] prefixes)
    {
        NativeSource.RegisterOwnerPrefix(ownerModId, prefixes);
    }

    public static AuraGameDataMutationResult RegisterLoadedDefinitionsV4(string ownerModId, params string[] idPrefixes)
    {
        ownerModId = (ownerModId ?? "").Trim();
        var prefixes = (idPrefixes ?? Array.Empty<string>())
            .Select(AuraGameDataKey.NormalizeId)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ownerModId.Length == 0 || prefixes.Length == 0)
        {
            return AuraGameDataMutationResult.Failed("Owner and at least one full-id prefix are required.");
        }

        RegisterOwnerPrefix(ownerModId, prefixes);
        lock (RegistrationGate)
        {
            OwnerRegistrationPrefixes[ownerModId] = prefixes;
            PendingOwnerRegistrations.Add(ownerModId);
        }

        return TryRegisterLoadedDefinitions(ownerModId, prefixes);
    }

    private static AuraGameDataMutationResult TryRegisterLoadedDefinitions(string ownerModId, string[] prefixes)
    {
        var definitions = new List<AuraGameDataDefinition>();
        foreach (DataType dataType in Enum.GetValues(typeof(DataType)))
        {
            definitions.AddRange(NativeSource.Read(dataType.ToString())
                .Where(value => prefixes.Any(prefix => value.Key.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(value => new AuraGameDataDefinition
                {
                    SchemaVersion = AuraGameDataConstants.SchemaVersion,
                    Key = value.Key.Clone(),
                    OwnerModId = ownerModId,
                    WriterId = ownerModId,
                    SourceKind = AuraGameDataSourceKinds.Registered,
                    Priority = value.Priority,
                    Enabled = value.Enabled,
                    Aliases = value.Aliases.ToList(),
                    Fields = value.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                }));
        }

        if (definitions.Count == 0)
        {
            return new AuraGameDataMutationResult { Success = true, Message = "Deferred until game tables are available." };
        }

        var result = AuraGameDataCatalogRuntime.RegisterBatch(ownerModId, definitions);
        if (result.Success)
        {
            lock (RegistrationGate)
            {
                PendingOwnerRegistrations.Remove(ownerModId);
            }
        }

        return result;
    }

    public static void InvalidateNativeCatalog()
    {
        NativeSource.Invalidate();
        lock (RegistrationGate)
        {
            foreach (var owner in OwnerRegistrationPrefixes.Keys)
            {
                PendingOwnerRegistrations.Add(owner);
            }
        }
        AuraGameDataCatalogRuntime.Invalidate();
    }

    private static bool IsIdentityOrScriptField(string? field)
    {
        return string.IsNullOrWhiteSpace(field)
            || string.Equals(field, "Id", StringComparison.Ordinal)
            || string.Equals(field, "InstanceID", StringComparison.Ordinal)
            || string.Equals(field, "RawData", StringComparison.Ordinal)
            || (field ?? "").IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void EnsureConfigured()
    {
        KeyValuePair<string, string[]>[] pending;
        lock (RegistrationGate)
        {
            if (registrationFlushActive || PendingOwnerRegistrations.Count == 0)
            {
                return;
            }

            registrationFlushActive = true;
            pending = OwnerRegistrationPrefixes
                .Where(pair => PendingOwnerRegistrations.Contains(pair.Key))
                .Select(pair => new KeyValuePair<string, string[]>(pair.Key, pair.Value.ToArray()))
                .ToArray();
        }

        try
        {
            foreach (var pair in pending)
            {
                TryRegisterLoadedDefinitions(pair.Key, pair.Value);
            }
        }
        finally
        {
            lock (RegistrationGate)
            {
                registrationFlushActive = false;
            }
        }
    }
}

internal sealed class AuraGameDataNativeSource : IAuraGameDataSource
{
    private readonly object gate = new();
    private readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> ownerPrefixes = new(StringComparer.OrdinalIgnoreCase);
    private long revision;

    public long Revision
    {
        get
        {
            lock (gate)
            {
                return revision;
            }
        }
    }

    public IReadOnlyList<AuraGameDataDefinition> Read(string dataType)
    {
        if (!Enum.TryParse(dataType, true, out DataType parsed))
        {
            return Array.Empty<AuraGameDataDefinition>();
        }

        try
        {
            var rows = Singleton<GameConfigManager>.Instance?.GetTable(parsed)?.Getlines();
            if (rows == null)
            {
                return Array.Empty<AuraGameDataDefinition>();
            }

            var signature = Signature(rows);
            lock (gate)
            {
                if (cache.TryGetValue(dataType, out var existing) && existing.Signature == signature)
                {
                    return existing.Definitions.Select(value => value.Clone()).ToList();
                }

                var definitions = rows
                    .Where(row => row != null)
                    .Select(row => BuildDefinition(parsed, row))
                    .Where(value => value != null)
                    .Cast<AuraGameDataDefinition>()
                    .ToList();
                revision++;
                cache[dataType] = new CacheEntry(signature, definitions);
                return definitions.Select(value => value.Clone()).ToList();
            }
        }
        catch
        {
            return Array.Empty<AuraGameDataDefinition>();
        }
    }

    public void RegisterOwnerPrefix(string ownerModId, IEnumerable<string> prefixes)
    {
        ownerModId = (ownerModId ?? "").Trim();
        if (ownerModId.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            foreach (var prefix in prefixes ?? Array.Empty<string>())
            {
                var normalized = AuraGameDataKey.NormalizeId(prefix);
                if (normalized.Length > 0)
                {
                    ownerPrefixes[normalized] = ownerModId;
                }
            }

            cache.Clear();
            revision++;
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            cache.Clear();
            revision++;
        }
    }

    private AuraGameDataDefinition? BuildDefinition(DataType dataType, IDictionary<string, string> row)
    {
        var id = AuraGameDataKey.NormalizeId(AuraSharedDictionary.Get(row, "Id"));
        if (id.Length == 0)
        {
            return null;
        }

        return new AuraGameDataDefinition
        {
            SchemaVersion = AuraGameDataConstants.SchemaVersion,
            Key = new AuraGameDataKey(dataType.ToString(), id),
            OwnerModId = ResolveOwner(id),
            WriterId = AuraGameDataConstants.RegistryAuthorityId,
            SourceKind = AuraGameDataSourceKinds.Native,
            Enabled = true,
            Revision = Revision,
            Fields = new Dictionary<string, string>(row, StringComparer.Ordinal)
        };
    }

    private string ResolveOwner(string id)
    {
        foreach (var pair in ownerPrefixes.OrderByDescending(value => value.Key.Length))
        {
            if (id.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        var separator = id.IndexOf('_');
        if (separator > 0)
        {
            var prefix = id.Substring(0, separator);
            if (prefix.EndsWith("Exp", StringComparison.OrdinalIgnoreCase))
            {
                return prefix;
            }
        }

        return "BaseGame";
    }

    private static ulong Signature(IEnumerable<Dictionary<string, string>> rows)
    {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var row in rows.OrderBy(value => AuraSharedDictionary.Get(value, "Id"), StringComparer.Ordinal))
        {
            foreach (var pair in row.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                foreach (var character in pair.Key + "\u001f" + (pair.Value ?? "") + "\u001e")
                {
                    hash ^= character;
                    hash *= prime;
                }
            }
        }

        return hash;
    }

    private sealed class CacheEntry
    {
        public CacheEntry(ulong signature, IReadOnlyList<AuraGameDataDefinition> definitions)
        {
            Signature = signature;
            Definitions = definitions;
        }

        public ulong Signature { get; }

        public IReadOnlyList<AuraGameDataDefinition> Definitions { get; }
    }
}
