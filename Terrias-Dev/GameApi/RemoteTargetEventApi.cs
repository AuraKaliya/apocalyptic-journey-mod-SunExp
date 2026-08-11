using System;
using System.Collections.Generic;
using AuraGameData.Shared.GameApi;
using Fight.ObjTarget;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public sealed class RemoteTargetEventLease
{
    internal RemoteTargetEventLease(IScriptExecutor executor, IDataConfig originalConfig)
    {
        Executor = executor;
        OriginalConfig = originalConfig;
    }

    internal IScriptExecutor Executor { get; }

    internal IDataConfig OriginalConfig { get; }

    public void Restore()
    {
        if (Executor != null && OriginalConfig != null)
        {
            Executor.dataConfig = OriginalConfig;
        }
    }
}

public static class RemoteTargetEventApi
{
    public static RemoteTargetEventLease? Prepare(ObjTargetAction? action)
    {
        if (action == null || !IsTerriasEvent(action.FromDataConfigId))
        {
            return null;
        }

        var target = StatusById(action.InstanceId);
        var executor = (string.IsNullOrWhiteSpace(action.SourceInstanceId)
                ? null
                : StatusById(action.SourceInstanceId)?.MirrorSc)
            ?? target?.MirrorSc;
        var originalConfig = executor?.dataConfig;
        if (executor == null || originalConfig == null)
        {
            return null;
        }

        var authoritative = AuraGameDataHostApi.CopyRow(
            DataType.Card,
            TerriasContentIdCompatibility.LookupCandidates(
                action.FromDataConfigId,
                "terrias",
                "wuna",
                "loneer",
                "columbina"));
        var payload = ObjTargetBase.DeserializeConfigData(action.theData);
        var normalized = ComposePayload(action.FromDataConfigId, authoritative, payload);
        action.theData = ObjTargetBase.SerializeConfigData(normalized);

        // Native ObjTargetAction temporarily replaces dataConfig.data. Give it
        // a disposable config so a remote card cannot overwrite the source
        // character's persistent card/skill definition.
        executor.dataConfig = new DataConfig(
            normalized,
            new Dictionary<string, string>(StringComparer.Ordinal),
            ifPreCompile: false,
            type: DataType.Card);
        return new RemoteTargetEventLease(executor, originalConfig);
    }

    public static Dictionary<string, string> ComposePayload(
        string fromDataConfigId,
        IDictionary<string, string>? authoritative,
        IDictionary<string, string>? payload)
    {
        var result = authoritative == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(authoritative, StringComparer.Ordinal);
        if (payload != null)
        {
            foreach (var entry in payload)
            {
                result[entry.Key] = entry.Value;
            }
        }

        result["Id"] = fromDataConfigId ?? "";
        return result;
    }

    private static bool IsTerriasEvent(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
               && id.StartsWith("Terrias_", StringComparison.Ordinal);
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
               && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
            ? status
            : null;
    }
}
