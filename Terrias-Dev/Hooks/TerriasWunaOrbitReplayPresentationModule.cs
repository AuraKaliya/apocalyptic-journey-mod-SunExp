using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraReplay.Presentation.Shared;
using AuraShared.Core;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Hooks;

internal sealed class TerriasWunaOrbitReplayPresentationModule : IAuraReplayPresentationRendererModule
{
    private static readonly HashSet<string> VisibleStatuses = new(StringComparer.Ordinal);
    private static IDisposable? registration;
    private static long eventSequence;

    public AuraReplayPresentationModuleDescriptor Descriptor { get; } = new()
    {
        OwnerModId = TerriasIds.ModId,
        TypeId = "WunaOrbitFirePresentation",
        SchemaVersion = 1,
        Portability = AuraReplayPresentationPortability.ProviderRequired,
        BuildIdentity = typeof(TerriasWunaOrbitReplayPresentationModule).Assembly.GetName().Version + "+"
                        + typeof(TerriasWunaOrbitReplayPresentationModule).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
        RendererCapability = "terrias-wuna-orbit-fire.v1"
    };

    internal static void Initialize()
    {
        registration?.Dispose();
        registration = AuraReplayPresentationRuntime.Register(new TerriasWunaOrbitReplayPresentationModule());
    }

    internal static void BeginBattle() => VisibleStatuses.Clear();

    internal static void PublishVisible(string statusId)
    {
        var actor = (statusId ?? "").Trim();
        if (actor.Length == 0 || !VisibleStatuses.Add(actor)) return;
        Publish(actor, AuraReplayPresentationKinds.VisibilityChanged, "{\"visible\":true}", persistent: true);
    }

    internal static void PublishBoost(string statusId, string action)
    {
        var actor = (statusId ?? "").Trim();
        if (actor.Length == 0) return;
        Publish(
            actor,
            AuraReplayPresentationKinds.Effect,
            AuraSharedJson.SerializeCompact(new { action = action ?? "" }),
            persistent: false,
            durationMicroseconds: 950_000L);
    }

    internal static void EndBattle()
    {
        foreach (var actor in VisibleStatuses.ToArray())
            Publish(actor, AuraReplayPresentationKinds.VisibilityChanged, "{\"visible\":false}", persistent: true);
        VisibleStatuses.Clear();
    }

    private static void Publish(
        string actor,
        string kind,
        string payload,
        bool persistent,
        long durationMicroseconds = 0L)
    {
        var sequence = Interlocked.Increment(ref eventSequence);
        var eventId = TerriasIds.ModId + ":wuna-orbit:" + actor + ":" + kind + ":" + sequence;
        AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
        {
            EventId = eventId,
            DuplicateKey = eventId,
            OwnerModId = TerriasIds.ModId,
            TypeId = "WunaOrbitFirePresentation",
            SchemaVersion = 1,
            Kind = kind,
            ActorEntityId = actor,
            OwnerEntityId = actor,
            PayloadJson = payload,
            Persistent = persistent,
            DurationMicroseconds = durationMicroseconds
        });
    }

    public IAuraReplayPresentationRenderer CreateRenderer(AuraReplayPresentationRenderContext context)
    {
        if (context?.EntityRootResolver == null)
            throw new InvalidOperationException("Wuna orbit replay renderer has no entity-root resolver.");
        return new Renderer(context.EntityRootResolver);
    }

    private sealed class Renderer : IAuraReplayPresentationRenderer
    {
        private readonly Func<string, Transform?> resolveEntity;
        private readonly Dictionary<string, WunaOrbitFireController> controllers = new(StringComparer.Ordinal);

        internal Renderer(Func<string, Transform?> resolveEntity) => this.resolveEntity = resolveEntity;

        public void Apply(AuraReplayPresentationEvent value, long logicalMicroseconds)
        {
            if (value.Kind == AuraReplayPresentationKinds.VisibilityChanged)
            {
                var visible = value.PayloadJson.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0;
                if (visible) Ensure(value.ActorEntityId, logicalMicroseconds);
                else Remove(value.ActorEntityId);
                return;
            }
            if (value.Kind != AuraReplayPresentationKinds.Effect) return;
            var controller = Ensure(value.ActorEntityId, logicalMicroseconds);
            var action = value.PayloadJson.IndexOf("Skill", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Skill"
                : value.PayloadJson.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Attack"
                    : "Action";
            controller?.BoostForAction(action);
        }

        public void Tick(long logicalMicroseconds)
        {
            var seconds = logicalMicroseconds / 1_000_000f;
            foreach (var controller in controllers.Values.Where(item => item != null))
                controller.SetReplayLogicalTime(seconds);
        }

        public void Reset()
        {
            foreach (var controller in controllers.Values.Where(item => item != null))
                UnityEngine.Object.Destroy(controller.gameObject);
            controllers.Clear();
        }

        public void Dispose() => Reset();

        private WunaOrbitFireController? Ensure(string entityId, long logicalMicroseconds)
        {
            if (controllers.TryGetValue(entityId ?? "", out var existing) && existing != null)
            {
                existing.SetReplayLogicalTime(logicalMicroseconds / 1_000_000f);
                return existing;
            }
            var entity = resolveEntity(entityId ?? "");
            var body = entity?.Find("Body")?.GetComponent<SpriteRenderer>()
                       ?? entity?.Find("body")?.GetComponent<SpriteRenderer>();
            if (body == null) return null;
            var root = new GameObject("Terrias_WunaOrbitFireReplay");
            root.transform.SetParent(body.transform, false);
            var controller = root.AddComponent<WunaOrbitFireController>();
            controller.ConfigureReplay(body);
            controller.SetReplayLogicalTime(logicalMicroseconds / 1_000_000f);
            controllers[entityId ?? ""] = controller;
            return controller;
        }

        private void Remove(string entityId)
        {
            if (!controllers.TryGetValue(entityId ?? "", out var controller)) return;
            controllers.Remove(entityId ?? "");
            if (controller != null) UnityEngine.Object.Destroy(controller.gameObject);
        }
    }
}
