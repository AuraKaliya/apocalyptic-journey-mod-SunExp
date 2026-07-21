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
    private static readonly Dictionary<DataType, string> DataTypeNames = Enum
        .GetValues(typeof(DataType))
        .Cast<DataType>()
        .Distinct()
        .ToDictionary(value => value, value => value.ToString());
    private static readonly AuraGameDataNativeSource NativeSource = new();

    static AuraGameDataHostApi()
    {
        AuraGameDataCatalogRuntime.ConfigureSource(NativeSource, rebuildImmediately: false);
        AuraGameDataCatalogRuntime.ConfigureRebuildScheduler(CompileCapturedNativeCatalog);
        ScheduleNativeRefresh(AuraGameDataConstants.RegistryAuthorityId, 1);
    }

    public static AuraGameDataCatalogSnapshot AcquireSnapshot()
    {
        return AuraGameDataCatalogRuntime.AcquireSnapshot();
    }

    public static bool IsNativeCatalogReady => AcquireSnapshot().Version.NativeReady;

    public static AuraGameDataQueryResult Query(DataType dataType, bool includeAllCandidates = false)
    {
        return AuraGameDataCatalogRuntime.Query(new AuraGameDataQuery
        {
            DataType = TypeName(dataType),
            IncludeAllCandidates = includeAllCandidates
        });
    }

    public static AuraGameDataQueryResult QueryHistory(DataType dataType)
    {
        return AuraGameDataCatalogRuntime.QueryHistory(new AuraGameDataQuery
        {
            DataType = TypeName(dataType),
            IncludeAllCandidates = true,
            IncludeDisabled = true,
            IncludeHistory = true
        });
    }

    public static AuraGameDataSnapshot? Resolve(DataType dataType, params string[] candidateIds)
    {
        return AcquireSnapshot().Resolve(TypeName(dataType), candidateIds ?? Array.Empty<string>());
    }

    public static bool TryGet(DataType dataType, string id, out AuraGameDataSnapshot? snapshot)
    {
        return AcquireSnapshot().TryGet(TypeName(dataType), id, out snapshot);
    }

    public static IReadOnlyList<AuraGameDataSnapshot> Table(DataType dataType)
    {
        return AcquireSnapshot().GetTable(TypeName(dataType));
    }

    public static string ResolveId(DataType dataType, IEnumerable<string> candidateIds, string fallback = "")
    {
        return AcquireSnapshot().Resolve(TypeName(dataType), candidateIds ?? Array.Empty<string>())?.Id
            ?? AuraGameDataKey.NormalizeId(fallback)
            ?? "";
    }

    public static List<Dictionary<string, string>> CopyTableForHostInterop(DataType dataType)
    {
        var rows = Table(dataType)
            .Select(item => item.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
            .ToList();
        AuraGameDataDiagnostics.RecordCopiedRows(rows.Count);
        return rows;
    }

    public static Dictionary<string, string>? CopyRow(DataType dataType, params string[] candidateIds)
    {
        var resolved = Resolve(dataType, candidateIds);
        AuraGameDataDiagnostics.RecordCopiedRows(resolved == null ? 0 : 1);
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
        id = AuraGameDataKey.NormalizeId(id);
        return AcquireSnapshot().TryResolveUniqueType(id, out var value)
               && Enum.TryParse(value, true, out DataType dataType)
            ? dataType
            : null;
    }

    public static DataType? ResolveDataType(string id, IEnumerable<DataType> searchOrder)
    {
        id = AuraGameDataKey.NormalizeId(id);
        if (id.Length == 0)
        {
            return null;
        }

        var snapshot = AcquireSnapshot();
        var visited = new HashSet<DataType>();
        DataType? match = null;
        foreach (var dataType in searchOrder ?? Array.Empty<DataType>())
        {
            if (!visited.Add(dataType) || !snapshot.TryGet(TypeName(dataType), id, out _))
            {
                continue;
            }

            if (match.HasValue)
            {
                return null;
            }

            match = dataType;
        }

        return match;
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
            Key = new AuraGameDataKey(TypeName(instance.Type), id),
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
        var forbidden = fields.FirstOrDefault(AuraGameDataFieldPolicy.IsIdentityOrScriptField);
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
        AuraGameDataDiagnostics.RecordMaterialization();
        if (request?.Definition == null)
        {
            return AuraGameDataHostMutationResult.Fail("handle", "A registered definition handle is required.");
        }

        if (!AuraGameDataCatalogRuntime.ValidateHandle(request.Definition, out var snapshot) || snapshot == null)
        {
            return AuraGameDataHostMutationResult.Fail("handle", "Definition handle is stale or invalid.");
        }

        return MaterializeSnapshot(snapshot, request);
    }

    public static AuraGameDataHostMutationResult Materialize(DataType dataType, params string[] candidateIds)
    {
        AuraGameDataDiagnostics.RecordMaterialization();
        var snapshot = AcquireSnapshot().Resolve(TypeName(dataType), candidateIds ?? Array.Empty<string>());
        return snapshot == null
            ? AuraGameDataHostMutationResult.Fail("resolve", "Game-data definition was not found in the effective catalog.")
            : MaterializeSnapshot(snapshot, new AuraGameDataMaterializeRequest());
    }

    private static AuraGameDataHostMutationResult MaterializeSnapshot(
        AuraGameDataSnapshot snapshot,
        AuraGameDataMaterializeRequest request)
    {
        if (!Enum.TryParse(snapshot.DataType, true, out DataType dataType))
        {
            return AuraGameDataHostMutationResult.Fail("type", "Unsupported DataType: " + snapshot.DataType);
        }

        if ((request.DataOverrides?.Keys ?? Enumerable.Empty<string>()).Any(AuraGameDataFieldPolicy.IsIdentityOrScriptField))
        {
            return AuraGameDataHostMutationResult.Fail("validate", "Data overrides may not change identity or script fields.");
        }

        try
        {
            var overrides = request.DataOverrides ?? new Dictionary<string, string>();
            var data = snapshot.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var pair in overrides)
            {
                data[pair.Key] = pair.Value ?? "";
            }

            data["Id"] = snapshot.Id;
            var instance = new DataConfig(
                data,
                new Dictionary<string, string>(StringComparer.Ordinal),
                request.PreCompile,
                dataType);

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
            if (!AuraGameDataFieldPolicy.IsIdentityOrScriptField(pair.Key))
            {
                data[pair.Key] = pair.Value ?? "";
            }
        }

        foreach (var pair in varsOverrides ?? new Dictionary<string, string>())
        {
            if (!AuraGameDataFieldPolicy.IsIdentityOrScriptField(pair.Key))
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

    public static AuraGameDataMutationResult RegisterNativeOwnershipV5(
        string ownerModId,
        params string[] prefixes)
    {
        ownerModId = (ownerModId ?? "").Trim();
        var normalizedPrefixes = (prefixes ?? Array.Empty<string>())
            .Select(AuraGameDataKey.NormalizeId)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ownerModId.Length == 0 || normalizedPrefixes.Length == 0)
        {
            return AuraGameDataMutationResult.Failed("Owner and at least one full-id prefix are required.");
        }

        var result = AuraGameDataCatalogRuntime.RegisterOwnerRules(
            ownerModId,
            normalizedPrefixes.Select(prefix => new AuraGameDataOwnerRule
            {
                OwnerModId = ownerModId,
                WriterId = ownerModId,
                IdPrefix = prefix
            }));
        ScheduleNativeRefresh(ownerModId, 2);
        ScheduleNativeRefresh(ownerModId, 20);
        ScheduleNativeRefresh(ownerModId, 120);
        return result;
    }

    public static void InvalidateNativeCatalog()
    {
        NativeSource.Invalidate();
        StartCooperativeNativeRefresh(AuraGameDataConstants.RegistryAuthorityId);
    }

    private static void CompileCapturedNativeCatalog()
    {
        AuraGameDataCatalogBuildRequest request;
        try
        {
            request = AuraGameDataCatalogRuntime.CaptureBuildRequest();
        }
        catch
        {
            AuraGameDataCatalogRuntime.Rebuild();
            return;
        }

        if (!request.Source.IsComplete || !AuraGameDataCatalogRuntime.IsBuildCurrent(request))
        {
            return;
        }

        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<AuraGameDataCatalogSnapshot>
            {
                OwnerId = AuraGameDataConstants.RegistryAuthorityId,
                Key = "catalog-compile",
                Source = "AuraGameData.CatalogCompile",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                CompletionPriority = 100,
                Work = cancellation =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    return AuraGameDataCatalogRuntime.Compile(request);
                },
                IsStillCurrent = () => AuraGameDataCatalogRuntime.IsBuildCurrent(request),
                ApplyOnMainThread = snapshot => AuraGameDataCatalogRuntime.Publish(snapshot),
                OnFailedOnMainThread = _ => AuraGameDataCatalogRuntime.Rebuild()
            });
        if (!queued)
        {
            AuraGameDataCatalogRuntime.Publish(AuraGameDataCatalogRuntime.Compile(request));
        }
    }

    private static void ScheduleNativeRefresh(string ownerModId, int delayFrames)
    {
        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraGameDataConstants.RegistryAuthorityId,
            Key = "game-data-native-refresh-" + delayFrames,
            Source = ownerModId + ".GameData.NativeRefresh",
            DelayFrames = delayFrames,
            Phase = AuraSharedFramePhase.Reconcile,
            EstimatedCost = 2,
            Action = () =>
            {
                NativeSource.Invalidate();
                StartCooperativeNativeRefresh(ownerModId);
            }
        });
    }

    private static void StartCooperativeNativeRefresh(string ownerModId)
    {
        NativeSource.BeginCooperativeCapture();
        AuraSharedFrameScheduler.RunCooperative(new AuraSharedFrameWorkRequest
        {
            OwnerId = AuraGameDataConstants.RegistryAuthorityId,
            Key = "game-data-native-capture",
            Source = ownerModId + ".GameData.NativeCapture",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 100,
            EstimatedCost = 2,
            MaximumSlices = 64,
            SliceBudgetMilliseconds = 4d,
            ExecuteSlice = NativeSource.CaptureSlice,
            OnCompleted = _ => CompileCapturedNativeCatalog(),
            OnCancelled = _ => CompileCapturedNativeCatalog()
        });
    }

    internal static string TypeName(DataType dataType)
    {
        return DataTypeNames.TryGetValue(dataType, out var name)
            ? name
            : dataType.ToString();
    }

}

internal sealed class AuraGameDataNativeSource : IAuraGameDataSource
{
    private readonly object gate = new();
    private AuraGameDataSourceSnapshot? cached;
    private List<AuraGameDataDefinition>? captureDefinitions;
    private DataType[] captureTypes = Array.Empty<DataType>();
    private IReadOnlyList<Dictionary<string, string>>? captureRows;
    private int captureIndex;
    private int captureRowIndex;
    private long captureGeneration;
    private long revision = 1;

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

    public AuraGameDataSourceSnapshot Capture()
    {
        AuraGameDataDiagnostics.RecordNativeCapture();
        lock (gate)
        {
            return cached ?? new AuraGameDataSourceSnapshot(
                revision,
                Array.Empty<AuraGameDataDefinition>(),
                isComplete: false);
        }
    }

    public void BeginCooperativeCapture()
    {
        lock (gate)
        {
            captureDefinitions = new List<AuraGameDataDefinition>();
            captureTypes = Enum.GetValues(typeof(DataType)).Cast<DataType>().ToArray();
            captureRows = null;
            captureIndex = 0;
            captureRowIndex = 0;
            captureGeneration = revision;
        }
    }

    public bool CaptureSlice(AuraSharedFrameSliceContext context)
    {
        while (!context.IsBudgetExhausted)
        {
            DataType dataType;
            Dictionary<string, string>? row = null;
            var loadRows = false;
            lock (gate)
            {
                if (captureDefinitions == null || captureIndex >= captureTypes.Length)
                {
                    CompleteCooperativeCaptureNoLock();
                    return true;
                }

                dataType = captureTypes[captureIndex];
                if (captureRows == null)
                {
                    loadRows = true;
                }
                else if (captureRowIndex < captureRows.Count)
                {
                    row = captureRows[captureRowIndex++];
                }
                else
                {
                    captureRows = null;
                    captureRowIndex = 0;
                    captureIndex++;
                    continue;
                }
            }

            if (loadRows)
            {
                var rows = LoadRows(dataType);
                lock (gate)
                {
                    if (captureDefinitions == null || captureGeneration != revision)
                    {
                        return true;
                    }

                    captureRows = rows;
                    captureRowIndex = 0;
                }

                continue;
            }

            var definition = row == null ? null : BuildDefinition(dataType, row);
            if (definition != null)
            {
                lock (gate)
                {
                    if (captureDefinitions != null && captureGeneration == revision)
                    {
                        captureDefinitions.Add(definition);
                    }
                }
            }
        }

        return false;
    }

    public void Invalidate()
    {
        lock (gate)
        {
            captureDefinitions = null;
            captureTypes = Array.Empty<DataType>();
            captureRows = null;
            captureIndex = 0;
            captureRowIndex = 0;
            revision++;
        }
    }

    private static IReadOnlyList<Dictionary<string, string>> LoadRows(DataType dataType)
    {
        try
        {
            var rows = Singleton<GameConfigManager>.Instance?.GetTable(dataType)?.Getlines();
            return rows == null
                ? Array.Empty<Dictionary<string, string>>()
                : rows;
        }
        catch
        {
            return Array.Empty<Dictionary<string, string>>();
        }
    }

    private void CompleteCooperativeCaptureNoLock()
    {
        if (captureDefinitions != null && captureGeneration == revision)
        {
            cached = new AuraGameDataSourceSnapshot(revision, captureDefinitions.ToArray());
        }

        captureDefinitions = null;
        captureTypes = Array.Empty<DataType>();
        captureRows = null;
        captureIndex = 0;
        captureRowIndex = 0;
    }

    private static AuraGameDataDefinition? BuildDefinition(DataType dataType, IDictionary<string, string> row)
    {
        var id = AuraGameDataKey.NormalizeId(AuraSharedDictionary.Get(row, "Id"));
        if (id.Length == 0)
        {
            return null;
        }

        return new AuraGameDataDefinition
        {
            SchemaVersion = AuraGameDataConstants.SchemaVersion,
            Key = new AuraGameDataKey(AuraGameDataHostApi.TypeName(dataType), id),
            OwnerModId = ResolveOwner(id),
            WriterId = AuraGameDataConstants.RegistryAuthorityId,
            SourceKind = AuraGameDataSourceKinds.Native,
            StorageKind = AuraGameDataStorageKinds.Inline,
            Enabled = true,
            Revision = 0,
            Fields = new Dictionary<string, string>(row, StringComparer.Ordinal)
        };
    }

    private static string ResolveOwner(string id)
    {
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
}
