using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>
/// Resolves every native extension intent before activation. The original schema-1
/// payload carried configured paths (including native fallback requests). New
/// writers explicitly declare resolved paths, which must exist without fallback.
/// This is a read-only materialization under the already verified game build;
/// sealed events, checkpoints and hashes are never rewritten.
/// </summary>
internal sealed class ReplayExtensionIntentVisualsV17
{
    internal const string ResolvedContract = "native-intent-resolved.v1";
    private readonly Dictionary<string, ReplayExtensionIntentVisualV17> visuals = new(StringComparer.Ordinal);

    internal ReplayExtensionIntentVisualsV17(IEnumerable<ReplayJournalEventV17> events, Func<string, bool> exists)
    {
        foreach (var item in events)
        {
            var message = item.Presentation;
            if (item.EventType != ReplayEventTypesV17.ExtensionPresented || message?.Kind != "IntentChanged") continue;
            var json = message.ExtensionPayloadJson ?? "";
            if (message.ExtensionSchemaVersion != 1)
                throw new InvalidOperationException("Unsupported native extension intent schema: " + message.ExtensionSchemaVersion);
            if (visuals.ContainsKey(json)) continue;
            visuals.Add(json, ResolvePayload(message.ExtensionSchemaVersion, json, exists));
        }
    }

    internal static ReplayExtensionIntentVisualV17 ResolvePayload(int schema, string json, Func<string, bool> exists)
    {
        if (schema != 1) throw new InvalidOperationException("Unsupported native extension intent schema: " + schema);
        var payload = JObject.Parse(json);
        var contract = payload.Value<string>("visualResourceContract") ?? "";
        if (contract.Length > 0 && contract != ResolvedContract)
            throw new InvalidOperationException("Unsupported native intent resource contract: " + contract);
        var wait = payload.Value<bool?>("isWait") == true;
        return new ReplayExtensionIntentVisualV17(
            wait,
            wait ? "" : Resolve(payload.Value<string>("iconResourcePath") ?? "",
                ReplayIntentVisualContractV17.DefaultIconResourcePath, contract.Length == 0, exists),
            wait ? "" : Resolve(payload.Value<string>("backIconResourcePath") ?? "",
                ReplayIntentVisualContractV17.DefaultBackIconResourcePath, contract.Length == 0, exists),
            payload.Value<string>("displayValue") ?? "");
    }

    internal ReplayExtensionIntentVisualV17 Get(string payloadJson) => visuals.TryGetValue(payloadJson, out var visual)
        ? visual
        : throw new InvalidOperationException("Extension intent was not included in resource preflight.");

    private static string Resolve(string path, string fallback, bool legacyRequest, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Replay extension intent resource path is missing.");
        if (ReplayIntentVisualContractV17.TryResolve(path, legacyRequest ? fallback : "", exists,
                out var result, out var error)) return result.ResolvedPath;
        throw new InvalidOperationException("Replay extension intent resource preflight failed: " + error);
    }
}

internal sealed class ReplayExtensionIntentVisualV17
{
    internal ReplayExtensionIntentVisualV17(bool isWait, string icon, string background, string displayValue)
    {
        IsWait = isWait;
        IconResourcePath = icon;
        BackgroundResourcePath = background;
        DisplayValue = displayValue;
    }

    internal bool IsWait { get; }
    internal string IconResourcePath { get; }
    internal string BackgroundResourcePath { get; }
    internal string DisplayValue { get; }
}
