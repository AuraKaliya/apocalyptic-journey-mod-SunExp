using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraReplay.Presentation.Shared;
using AuraShared.Core;
using Newtonsoft.Json.Linq;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks;

internal sealed class TerriasStarScoreReplayPresentationModule : IAuraReplayPresentationRendererModule
{
    private static IDisposable? registration;
    private static long eventSequence;

    public AuraReplayPresentationModuleDescriptor Descriptor { get; } = new()
    {
        OwnerModId = TerriasIds.ModId,
        TypeId = "StarScoreHudPresentation",
        SchemaVersion = 1,
        Portability = AuraReplayPresentationPortability.ProviderRequired,
        BuildIdentity = typeof(TerriasStarScoreReplayPresentationModule).Assembly.GetName().Version + "+"
                        + typeof(TerriasStarScoreReplayPresentationModule).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
        RendererCapability = "terrias-star-score-hud.v1"
    };

    internal static void Initialize()
    {
        registration?.Dispose();
        registration = AuraReplayPresentationRuntime.Register(new TerriasStarScoreReplayPresentationModule());
    }

    internal static void Publish(StarScoreDisplaySnapshot snapshot)
    {
        if (snapshot == null) return;
        var sequence = Interlocked.Increment(ref eventSequence);
        var eventId = TerriasIds.ModId + ":star-score:" + (snapshot.OwnerStatusId ?? "")
                      + ":v" + snapshot.Version + ":" + sequence;
        AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
        {
            EventId = eventId,
            DuplicateKey = eventId,
            OwnerModId = TerriasIds.ModId,
            TypeId = "StarScoreHudPresentation",
            SchemaVersion = 1,
            Kind = AuraReplayPresentationKinds.HudChanged,
            ActorEntityId = snapshot.OwnerStatusId ?? "",
            OwnerEntityId = snapshot.OwnerStatusId ?? "",
            DisplayText = "StarScore",
            PayloadJson = AuraSharedJson.SerializeCompact(new
            {
                visible = snapshot.HasNotes,
                ownerStatusId = snapshot.OwnerStatusId ?? "",
                notes = snapshot.Notes.Select(note => note.ToString()).ToArray(),
                version = snapshot.Version,
                isCadencePreview = snapshot.IsCadencePreview,
                completedCadencePattern = snapshot.CompletedCadencePattern ?? ""
            }),
            Persistent = true
        });
    }

    internal static void PublishHidden(string ownerStatusId)
    {
        var sequence = Interlocked.Increment(ref eventSequence);
        var eventId = TerriasIds.ModId + ":star-score:" + (ownerStatusId ?? "") + ":hidden:" + sequence;
        AuraReplayPresentationRuntime.Publish(new AuraReplayPresentationEvent
        {
            EventId = eventId,
            DuplicateKey = eventId,
            OwnerModId = TerriasIds.ModId,
            TypeId = "StarScoreHudPresentation",
            SchemaVersion = 1,
            Kind = AuraReplayPresentationKinds.HudChanged,
            ActorEntityId = ownerStatusId ?? "",
            OwnerEntityId = ownerStatusId ?? "",
            PayloadJson = "{\"visible\":false}",
            Persistent = true
        });
    }

    public IAuraReplayPresentationRenderer CreateRenderer(AuraReplayPresentationRenderContext context)
    {
        if (context?.CanvasRoot == null)
            throw new InvalidOperationException("Star Score replay renderer has no native FightUI canvas root.");
        return new Renderer(context.CanvasRoot);
    }

    private sealed class Renderer : IAuraReplayPresentationRenderer
    {
        private readonly Transform parent;
        private StarScoreHudView? view;

        internal Renderer(Transform parent) => this.parent = parent;

        public void Apply(AuraReplayPresentationEvent value, long logicalMicroseconds)
        {
            var payload = JObject.Parse(string.IsNullOrWhiteSpace(value.PayloadJson) ? "{}" : value.PayloadJson);
            if (payload.Value<bool?>("visible") != true)
            {
                Reset();
                return;
            }
            var notes = (payload["notes"]?.Values<string>() ?? Enumerable.Empty<string>())
                .Select(text => Enum.TryParse<StarScoreNote>(text, true, out var note) ? (StarScoreNote?)note : null)
                .Where(note => note.HasValue)
                .Select(note => note!.Value)
                .ToList();
            var snapshot = new StarScoreDisplaySnapshot(
                payload.Value<string>("ownerStatusId") ?? value.OwnerEntityId,
                notes,
                payload.Value<int?>("version") ?? 0,
                isCadencePreview: false,
                payload.Value<string>("completedCadencePattern") ?? "");
            view ??= StarScoreHudView.Create(parent);
            view.SetReplayMode(true);
            view.ApplySnapshot(snapshot);
            view.SetReplayLogicalTime(logicalMicroseconds / 1_000_000f);
        }

        public void Tick(long logicalMicroseconds) =>
            view?.SetReplayLogicalTime(logicalMicroseconds / 1_000_000f);

        public void Reset()
        {
            if (view == null) return;
            var root = view.gameObject;
            view = null;
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }

        public void Dispose() => Reset();
    }
}
