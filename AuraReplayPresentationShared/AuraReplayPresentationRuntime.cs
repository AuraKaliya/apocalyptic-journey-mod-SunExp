using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AuraReplay.Presentation.Shared;

public static class AuraReplayPresentationProtocol
{
    public const int Version = 1;
    public const int MaximumPayloadCharacters = 32 * 1024;
    public const int MaximumDisplayCharacters = 4 * 1024;
    public const int MaximumTargets = 64;
    public const int MaximumEventsPerCapture = 1_000_000;
}

public static class AuraReplayPresentationKinds
{
    public const string OwnerAttachedFocus = "OwnerAttachedFocus";
    public const string VisibilityChanged = "VisibilityChanged";
    public const string HudChanged = "HudChanged";
    public const string IntentChanged = "IntentChanged";
    public const string Overlay = "Overlay";
    public const string Effect = "Effect";
}

public interface IAuraReplayPresentationModule
{
    AuraReplayPresentationModuleDescriptor Descriptor { get; }
}

public sealed class AuraReplayPresentationRenderContext
{
    public Transform CanvasRoot { get; set; } = null!;
    public Transform WorldRoot { get; set; } = null!;
    public Camera Camera { get; set; } = null!;
    public int ReferenceWidth { get; set; }
    public int ReferenceHeight { get; set; }
    public Func<string, Transform?>? EntityRootResolver { get; set; }
}

public interface IAuraReplayPresentationRenderer : IDisposable
{
    void Reset();
    void Apply(AuraReplayPresentationEvent value, long logicalMicroseconds);
    void Tick(long logicalMicroseconds);
}

public interface IAuraReplayPresentationRendererModule : IAuraReplayPresentationModule
{
    IAuraReplayPresentationRenderer CreateRenderer(AuraReplayPresentationRenderContext context);
}

public sealed class AuraReplayPresentationEvent
{
    public int ProtocolVersion { get; set; } = AuraReplayPresentationProtocol.Version;
    public string EventId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string TypeId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string Kind { get; set; } = "";
    public string ActorEntityId { get; set; } = "";
    public string OwnerEntityId { get; set; } = "";
    public string IssuerPlayerId { get; set; } = "";
    public List<string> TargetEntityIds { get; set; } = new();
    public string ResourcePath { get; set; } = "";
    public string DisplayText { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public long DurationMicroseconds { get; set; }
    public bool Persistent { get; set; }
    public string DuplicateKey { get; set; } = "";
}

public sealed class AuraReplayCapturedPresentationEvent
{
    public string BattleSessionId { get; set; } = "";
    public long CaptureSequence { get; set; }
    public long StopwatchTimestamp { get; set; }
    public AuraReplayPresentationEvent Event { get; set; } = new();
}

public enum AuraReplayPresentationPublishResult
{
    Published,
    NoCaptureSession,
    Invalid,
    Duplicate,
    CaptureLimitReached,
    SinkFailed
}

/// <summary>
/// Shared, semantic-free capture lane for content-owned battle presentation.
/// Content modules publish owner-qualified, data-only events. A replay recorder
/// owns the single active capture lease and timestamps events at the shared
/// boundary; publishers never receive recorder or storage objects.
/// </summary>
public static class AuraReplayPresentationRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ModuleEntry> Modules = new(StringComparer.Ordinal);
    private static CaptureSession? activeCapture;
    private static long moduleGeneration;
    private static long captureGeneration;

    public static IDisposable Register(IAuraReplayPresentationModule module)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));
        var descriptor = ValidateDescriptor(module.Descriptor);
        var key = ModuleKey(descriptor.OwnerModId, descriptor.TypeId);
        lock (Gate)
        {
            var generation = ++moduleGeneration;
            Modules[key] = new ModuleEntry(module, Clone(descriptor), generation);
            return new ModuleLease(key, generation);
        }
    }

    public static IReadOnlyList<AuraReplayPresentationModuleDescriptor> SnapshotModules()
    {
        lock (Gate)
        {
            return Modules.Values
                .OrderBy(item => item.Descriptor.OwnerModId, StringComparer.Ordinal)
                .ThenBy(item => item.Descriptor.TypeId, StringComparer.Ordinal)
                .Select(item => Clone(item.Descriptor))
                .ToList();
        }
    }

    public static IAuraReplayPresentationRenderer? CreateRenderer(
        string ownerModId,
        string typeId,
        int schemaVersion,
        AuraReplayPresentationRenderContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        IAuraReplayPresentationRendererModule? module;
        lock (Gate)
        {
            module = Modules.TryGetValue(ModuleKey(ownerModId, typeId), out var entry)
                     && entry.Descriptor.SchemaVersion == schemaVersion
                ? entry.Module as IAuraReplayPresentationRendererModule
                : null;
        }
        return module?.CreateRenderer(context);
    }

    public static void ClearOwner(string ownerModId)
    {
        var owner = Normalize(ownerModId);
        if (owner.Length == 0) return;
        lock (Gate)
        {
            foreach (var key in Modules
                         .Where(item => string.Equals(item.Value.Descriptor.OwnerModId, owner, StringComparison.Ordinal))
                         .Select(item => item.Key)
                         .ToList())
                Modules.Remove(key);
        }
    }

    public static IDisposable BeginCapture(
        string battleSessionId,
        Action<AuraReplayCapturedPresentationEvent> sink)
    {
        var session = Normalize(battleSessionId);
        if (session.Length == 0) throw new ArgumentException("Battle session id is required.", nameof(battleSessionId));
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        lock (Gate)
        {
            if (activeCapture != null)
                throw new InvalidOperationException("A replay presentation capture session is already active.");
            var generation = ++captureGeneration;
            activeCapture = new CaptureSession(session, sink, generation);
            return new CaptureLease(generation);
        }
    }

    public static AuraReplayPresentationPublishResult Publish(AuraReplayPresentationEvent value)
    {
        if (!TryNormalize(value, out var normalized)) return AuraReplayPresentationPublishResult.Invalid;
        Action<AuraReplayCapturedPresentationEvent> sink;
        AuraReplayCapturedPresentationEvent captured;
        lock (Gate)
        {
            var capture = activeCapture;
            if (capture == null) return AuraReplayPresentationPublishResult.NoCaptureSession;
            if (!Modules.ContainsKey(ModuleKey(normalized.OwnerModId, normalized.TypeId)))
                return AuraReplayPresentationPublishResult.Invalid;
            var duplicateIdentity = normalized.EventId.Length > 0
                ? "event:" + normalized.EventId
                : "key:" + normalized.OwnerModId + "|" + normalized.TypeId + "|" + normalized.DuplicateKey;
            if (!capture.Seen.Add(duplicateIdentity)) return AuraReplayPresentationPublishResult.Duplicate;
            if (capture.Sequence >= AuraReplayPresentationProtocol.MaximumEventsPerCapture)
            {
                capture.Seen.Remove(duplicateIdentity);
                return AuraReplayPresentationPublishResult.CaptureLimitReached;
            }
            captured = new AuraReplayCapturedPresentationEvent
            {
                BattleSessionId = capture.BattleSessionId,
                CaptureSequence = ++capture.Sequence,
                StopwatchTimestamp = Stopwatch.GetTimestamp(),
                Event = normalized
            };
            sink = capture.Sink;
        }
        try
        {
            sink(captured);
            return AuraReplayPresentationPublishResult.Published;
        }
        catch
        {
            return AuraReplayPresentationPublishResult.SinkFailed;
        }
    }

    public static bool HasActiveCapture
    {
        get { lock (Gate) return activeCapture != null; }
    }

    private static bool TryNormalize(AuraReplayPresentationEvent? value, out AuraReplayPresentationEvent result)
    {
        result = new AuraReplayPresentationEvent();
        if (value == null || value.ProtocolVersion != AuraReplayPresentationProtocol.Version) return false;
        var owner = Normalize(value.OwnerModId);
        var type = Normalize(value.TypeId);
        var kind = Normalize(value.Kind);
        var eventId = Normalize(value.EventId);
        var duplicateKey = Normalize(value.DuplicateKey);
        if (owner.Length == 0 || type.Length == 0 || kind.Length == 0 || value.SchemaVersion <= 0) return false;
        if (eventId.Length == 0 && duplicateKey.Length == 0) return false;
        if (!TryCanonicalizePayload(value.PayloadJson, out var canonicalPayload)) return false;
        if ((value.DisplayText ?? "").Length > AuraReplayPresentationProtocol.MaximumDisplayCharacters) return false;
        var targets = (value.TargetEntityIds ?? new List<string>())
            .Select(Normalize)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(AuraReplayPresentationProtocol.MaximumTargets + 1)
            .ToList();
        if (targets.Count > AuraReplayPresentationProtocol.MaximumTargets) return false;
        result = new AuraReplayPresentationEvent
        {
            ProtocolVersion = AuraReplayPresentationProtocol.Version,
            EventId = eventId,
            OwnerModId = owner,
            TypeId = type,
            SchemaVersion = value.SchemaVersion,
            Kind = kind,
            ActorEntityId = Normalize(value.ActorEntityId),
            OwnerEntityId = Normalize(value.OwnerEntityId),
            IssuerPlayerId = Normalize(value.IssuerPlayerId),
            TargetEntityIds = targets,
            ResourcePath = (value.ResourcePath ?? "").Trim(),
            DisplayText = value.DisplayText ?? "",
            PayloadJson = canonicalPayload,
            DurationMicroseconds = Math.Max(0L, value.DurationMicroseconds),
            Persistent = value.Persistent,
            DuplicateKey = duplicateKey
        };
        return true;
    }

    private static bool TryCanonicalizePayload(string? payload, out string canonical)
    {
        canonical = "";
        var raw = payload ?? "";
        if (raw.Length > AuraReplayPresentationProtocol.MaximumPayloadCharacters) return false;
        var source = string.IsNullOrWhiteSpace(raw) ? "{}" : raw.Trim();
        try
        {
            using var text = new StringReader(source);
            using var reader = new JsonTextReader(text)
            {
                DateParseHandling = DateParseHandling.None,
                MaxDepth = 64
            };
            var token = JToken.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Ignore
            });
            if (reader.Read()) return false;
            canonical = NormalizeJson(token).ToString(Formatting.None);
            return canonical.Length <= AuraReplayPresentationProtocol.MaximumPayloadCharacters;
        }
        catch
        {
            canonical = "";
            return false;
        }
    }

    private static JToken NormalizeJson(JToken token)
    {
        if (token is JObject obj)
        {
            var result = new JObject();
            foreach (var property in obj.Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
                result.Add(property.Name, NormalizeJson(property.Value));
            return result;
        }
        if (token is JArray array) return new JArray(array.Select(NormalizeJson));
        return token.DeepClone();
    }

    private static AuraReplayPresentationModuleDescriptor ValidateDescriptor(
        AuraReplayPresentationModuleDescriptor? source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var result = Clone(source);
        result.OwnerModId = Normalize(result.OwnerModId);
        result.TypeId = Normalize(result.TypeId);
        result.Portability = Normalize(result.Portability);
        if (result.OwnerModId.Length == 0 || result.TypeId.Length == 0 || result.SchemaVersion <= 0)
            throw new InvalidOperationException("Replay presentation module identity is incomplete.");
        if (result.Portability != AuraReplayPresentationPortability.Portable
            && result.Portability != AuraReplayPresentationPortability.ProviderRequired)
            throw new InvalidOperationException("Replay presentation module portability is invalid.");
        if (result.Portability == AuraReplayPresentationPortability.ProviderRequired
            && string.IsNullOrWhiteSpace(result.RendererCapability))
            throw new InvalidOperationException("Provider-required replay presentation needs a renderer capability.");
        return result;
    }

    private static AuraReplayPresentationModuleDescriptor Clone(AuraReplayPresentationModuleDescriptor source) => new()
    {
        OwnerModId = source.OwnerModId ?? "",
        TypeId = source.TypeId ?? "",
        SchemaVersion = source.SchemaVersion,
        Portability = source.Portability ?? "",
        BuildIdentity = source.BuildIdentity ?? "",
        RendererCapability = source.RendererCapability ?? ""
    };

    private static string ModuleKey(string owner, string type) => Normalize(owner) + "|" + Normalize(type);
    private static string Normalize(string? value) => (value ?? "").Trim();

    private sealed class ModuleEntry
    {
        internal ModuleEntry(
            IAuraReplayPresentationModule module,
            AuraReplayPresentationModuleDescriptor descriptor,
            long generation)
        {
            Module = module;
            Descriptor = descriptor;
            Generation = generation;
        }

        internal IAuraReplayPresentationModule Module { get; }
        internal AuraReplayPresentationModuleDescriptor Descriptor { get; }
        internal long Generation { get; }
    }

    private sealed class CaptureSession
    {
        internal CaptureSession(
            string battleSessionId,
            Action<AuraReplayCapturedPresentationEvent> sink,
            long generation)
        {
            BattleSessionId = battleSessionId;
            Sink = sink;
            Generation = generation;
        }

        internal string BattleSessionId { get; }
        internal Action<AuraReplayCapturedPresentationEvent> Sink { get; }
        internal long Generation { get; }
        internal long Sequence { get; set; }
        internal HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ModuleLease : IDisposable
    {
        private string? key;
        private readonly long generation;

        internal ModuleLease(string key, long generation)
        {
            this.key = key;
            this.generation = generation;
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (key == null) return;
                if (Modules.TryGetValue(key, out var current) && current.Generation == generation)
                    Modules.Remove(key);
                key = null;
            }
        }
    }

    private sealed class CaptureLease : IDisposable
    {
        private long generation;

        internal CaptureLease(long generation) => this.generation = generation;

        public void Dispose()
        {
            lock (Gate)
            {
                if (generation == 0) return;
                if (activeCapture?.Generation == generation) activeCapture = null;
                generation = 0;
            }
        }
    }
}
