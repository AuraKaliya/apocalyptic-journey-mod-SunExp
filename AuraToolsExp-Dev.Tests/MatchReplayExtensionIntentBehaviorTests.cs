using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;
using Newtonsoft.Json.Linq;

internal static partial class AuraToolsTestSuite
{
    internal static void TestReplayExtensionIntentVisuals()
    {
        var resources = new HashSet<string>(StringComparer.Ordinal)
        {
            ReplayIntentVisualContractV17.DefaultIconResourcePath,
            ReplayIntentVisualContractV17.DefaultBackIconResourcePath,
            "Icon/ActionIcon/负面底"
        };
        var envelope = BuildReplayV17();
        var message = envelope.Document.PresentationEvents.Single(item =>
            item.EventType == ReplayEventTypesV17.ExtensionPresented).Presentation!;
        message.Kind = "IntentChanged";
        message.ExtensionPayloadJson = IntentJson(new
        {
            iconResourcePath = "Icon/ActionIcon/给予异常",
            backIconResourcePath = "Icon/ActionIcon/负面底",
            displayValue = "6",
            isWait = false
        });
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope).IsValid,
            "a historical configured-path extension fixture seals under the original v17 contract");
        var original = ReplayPayloadV17.Encode(envelope);
        var materialized = new ReplayExtensionIntentVisualsV17(envelope.Document.PresentationEvents, resources.Contains);
        var visual = materialized.Get(message.ExtensionPayloadJson);
        Assert(visual.IconResourcePath == ReplayIntentVisualContractV17.DefaultIconResourcePath
               && visual.BackgroundResourcePath == "Icon/ActionIcon/负面底" && visual.DisplayValue == "6",
            "historical requests use exactly the native missing-icon rule while retaining an available background");
        Assert(original.SequenceEqual(ReplayPayloadV17.Encode(envelope))
               && ReplayDocumentValidatorV17.Validate(envelope).IsValid,
            "legacy intent materialization does not mutate sealed events, checkpoints, bytes or roots");

        var payload = JObject.Parse(message.ExtensionPayloadJson);
        payload["visualResourceContract"] = ReplayExtensionIntentVisualsV17.ResolvedContract;
        message.ExtensionPayloadJson = IntentJson(payload);
        ExpectIntentPreflightFailure(envelope, resources, "resolved writers cannot disguise a missing sprite with fallback");
        payload["iconResourcePath"] = ReplayIntentVisualContractV17.DefaultIconResourcePath;
        message.ExtensionPayloadJson = IntentJson(payload);
        Assert(new ReplayExtensionIntentVisualsV17(envelope.Document.PresentationEvents, resources.Contains)
                   .Get(message.ExtensionPayloadJson).IconResourcePath == ReplayIntentVisualContractV17.DefaultIconResourcePath,
            "new resolved extension resources preflight successfully");
        payload.Remove("visualResourceContract");
        payload["backIconResourcePath"] = "missing-background";
        message.ExtensionPayloadJson = IntentJson(payload);
        Assert(new ReplayExtensionIntentVisualsV17(envelope.Document.PresentationEvents, resources.Contains)
                   .Get(message.ExtensionPayloadJson).BackgroundResourcePath == ReplayIntentVisualContractV17.DefaultBackIconResourcePath,
            "legacy background requests follow their own native fallback");
        resources.Remove(ReplayIntentVisualContractV17.DefaultBackIconResourcePath);
        ExpectIntentPreflightFailure(envelope, resources, "missing primary and native fallback fail before playback");
        payload["isWait"] = true;
        message.ExtensionPayloadJson = IntentJson(payload);
        Assert(new ReplayExtensionIntentVisualsV17(envelope.Document.PresentationEvents, _ => false)
                   .Get(message.ExtensionPayloadJson).IsWait,
            "wait intent clears its UI without requiring invisible assets");
        payload["visualResourceContract"] = "unknown.v9";
        message.ExtensionPayloadJson = IntentJson(payload);
        ExpectIntentPreflightFailure(envelope, resources, "unknown resource contracts never silently use the legacy reader");
        payload.Remove("visualResourceContract");
        payload["isWait"] = false;
        payload.Remove("iconResourcePath");
        message.ExtensionPayloadJson = IntentJson(payload);
        ExpectIntentPreflightFailure(envelope, resources, "incomplete payloads cannot invent an intent from the native fallback");
        payload["isWait"] = true;
        message.ExtensionPayloadJson = IntentJson(payload);
        var newer = ReplayCanonicalJsonV17.Clone(envelope.Document.PresentationEvents.Single(item =>
            item.EventType == ReplayEventTypesV17.ExtensionPresented));
        newer.Presentation!.ExtensionSchemaVersion = 2;
        envelope.Document.PresentationEvents.Add(newer);
        ExpectIntentPreflightFailure(envelope, resources, "identical payload caching cannot bypass a newer schema rejection");
    }

    private static void ExpectIntentPreflightFailure(ReplayDocumentEnvelopeV17 envelope, HashSet<string> resources, string reason)
    {
        var failed = false;
        try { _ = new ReplayExtensionIntentVisualsV17(envelope.Document.PresentationEvents, resources.Contains); }
        catch (InvalidOperationException) { failed = true; }
        Assert(failed, reason);
    }

    private static string IntentJson(object value) =>
        System.Text.Encoding.UTF8.GetString(ReplayCanonicalJsonV17.SerializeUtf8(value));
}
