using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal readonly struct ReplayNativeAudioClipObservation
{
    internal ReplayNativeAudioClipObservation(string resourceId, bool inheritedSymbolicCall)
    {
        ResourceId = resourceId ?? "";
        InheritedSymbolicCall = inheritedSymbolicCall;
    }

    internal string ResourceId { get; }

    internal bool InheritedSymbolicCall { get; }
}

/// <summary>
/// Correlates public string overloads with the nested AudioClip overload that the
/// native AudioManager actually plays. Symbolic calls never create replay cues.
/// </summary>
internal sealed class ReplayNativeAudioCallTracker
{
    private readonly List<PendingCall> pending = new();

    internal int PendingCount => pending.Count;

    internal void BeginSymbolic(string bus, IReadOnlyList<string>? arguments)
    {
        var resourceId = SelectResource(bus, arguments);
        if (resourceId.Length == 0) return;
        pending.Add(new PendingCall(NormalizeBus(bus), resourceId));
    }

    internal ReplayNativeAudioClipObservation ObserveClip(
        string bus,
        string clipName,
        int clipInstanceId)
    {
        var normalizedBus = NormalizeBus(bus);
        for (var index = pending.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(pending[index].Bus, normalizedBus, StringComparison.OrdinalIgnoreCase)) continue;
            return new ReplayNativeAudioClipObservation(pending[index].ResourceId, true);
        }

        return new ReplayNativeAudioClipObservation(
            "Clip/" + SafeClipName(clipName, clipInstanceId),
            false);
    }

    internal void EndSymbolic(string bus)
    {
        var normalizedBus = NormalizeBus(bus);
        for (var index = pending.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(pending[index].Bus, normalizedBus, StringComparison.OrdinalIgnoreCase)) continue;
            pending.RemoveAt(index);
            return;
        }
    }

    internal void Reset()
    {
        pending.Clear();
    }

    private static string SelectResource(string bus, IReadOnlyList<string>? arguments)
    {
        var values = (arguments ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        if (values.Count == 0) return "";
        return string.Equals(bus, "Vocal", StringComparison.OrdinalIgnoreCase) && values.Count > 1
            ? values[values.Count - 1]
            : values[0];
    }

    private static string NormalizeBus(string bus)
    {
        return string.IsNullOrWhiteSpace(bus) ? "Effect" : bus.Trim();
    }

    private static string SafeClipName(string clipName, int clipInstanceId)
    {
        var value = (clipName ?? "").Trim()
            .Replace('\\', '_')
            .Replace('/', '_')
            .Replace(':', '_');
        while (value.Contains("..")) value = value.Replace("..", "_");
        return value.Length == 0 ? "instance-" + clipInstanceId : value;
    }

    private readonly struct PendingCall
    {
        internal PendingCall(string bus, string resourceId)
        {
            Bus = bus;
            ResourceId = resourceId;
        }

        internal string Bus { get; }

        internal string ResourceId { get; }
    }
}
