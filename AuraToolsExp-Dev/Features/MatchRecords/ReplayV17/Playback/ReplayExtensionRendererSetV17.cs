using System;
using System.Collections.Generic;
using System.Linq;
using AuraReplay.Presentation.Shared;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal sealed class ReplayExtensionRendererSetV17 : IDisposable
{
    private static readonly HashSet<string> BuiltInCapabilities = new(StringComparer.Ordinal)
    {
        "owner-attached-spirit.v1"
    };

    private readonly Dictionary<string, IAuraReplayPresentationRenderer> renderers = new(StringComparer.Ordinal);

    internal ReplayExtensionRendererSetV17(
        IEnumerable<ReplayPresentationModuleRequirementV17> modules,
        AuraReplayPresentationRenderContext context)
    {
        foreach (var module in modules ?? Array.Empty<ReplayPresentationModuleRequirementV17>())
        {
            var key = Key(module.OwnerModId, module.TypeId);
            var renderer = AuraReplayPresentationRuntime.CreateRenderer(
                module.OwnerModId,
                module.TypeId,
                module.SchemaVersion,
                context);
            if (renderer != null)
            {
                renderers[key] = renderer;
                continue;
            }
            if (string.Equals(
                    module.Portability,
                    AuraReplayPresentationPortability.ProviderRequired,
                    StringComparison.Ordinal)
                && !BuiltInCapabilities.Contains(module.RendererCapability ?? ""))
                throw new InvalidOperationException(
                    "Replay presentation renderer is unavailable: " + module.OwnerModId + "/" + module.TypeId
                    + " -> " + module.RendererCapability + ".");
        }
    }

    internal void Apply(ReplayPresentationMessageV17 message, long logicalTicks)
    {
        if (message == null
            || !renderers.TryGetValue(Key(message.ExtensionOwnerModId, message.ExtensionTypeId), out var renderer)) return;
        renderer.Apply(new AuraReplayPresentationEvent
        {
            EventId = message.ExtensionEventId,
            DuplicateKey = message.ExtensionEventId,
            OwnerModId = message.ExtensionOwnerModId,
            TypeId = message.ExtensionTypeId,
            SchemaVersion = message.ExtensionSchemaVersion,
            Kind = message.Kind,
            ActorEntityId = message.ActorId,
            OwnerEntityId = message.OwnerEntityId,
            TargetEntityIds = message.TargetIds?.ToList() ?? new List<string>(),
            ResourcePath = message.ResourcePath,
            DisplayText = message.DisplayText,
            PayloadJson = message.ExtensionPayloadJson,
            DurationMicroseconds = message.DurationTicks,
            Persistent = message.Persistent
        }, logicalTicks);
    }

    internal void Reset()
    {
        foreach (var renderer in renderers.Values) renderer.Reset();
    }

    internal void Tick(long logicalTicks)
    {
        foreach (var renderer in renderers.Values) renderer.Tick(logicalTicks);
    }

    public void Dispose()
    {
        Exception? failure = null;
        foreach (var renderer in renderers.Values)
        {
            try { renderer.Dispose(); }
            catch (Exception ex) { failure ??= ex; }
        }
        renderers.Clear();
        if (failure != null)
            throw new InvalidOperationException("Replay extension renderer teardown was incomplete.", failure);
    }

    private static string Key(string owner, string type) => (owner ?? "").Trim() + "|" + (type ?? "").Trim();
}
