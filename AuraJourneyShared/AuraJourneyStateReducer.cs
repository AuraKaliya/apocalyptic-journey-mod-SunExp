using System;
using System.Linq;

namespace AuraJourney.Shared;

public static class AuraJourneyStateReducer
{
    public static AuraJourneyState Apply(
        AuraJourneyState? current,
        AuraJourneyCommitRequest request,
        DateTime timestampUtc)
    {
        var state = Clone(current) ?? new AuraJourneyState();
        state.SchemaVersion = AuraJourneyConstants.StateSchemaVersion;
        state.JourneyId = string.IsNullOrWhiteSpace(state.JourneyId) ? request.JourneyId : state.JourneyId;
        state.OwnerModId = string.IsNullOrWhiteSpace(state.OwnerModId) ? request.OwnerModId : state.OwnerModId;
        state.Version++;

        var mutation = request.Mutation ?? new AuraJourneyMutation();
        var run = mutation.Run ?? new AuraJourneyRunBinding();
        if (!string.IsNullOrWhiteSpace(run.RunId)
            || !string.IsNullOrWhiteSpace(run.SaveSlotId)
            || !string.IsNullOrWhiteSpace(run.NativeModeKey))
        {
            state.Run = CloneRun(run);
        }

        if (!string.IsNullOrWhiteSpace(mutation.ActiveNodeId))
        {
            state.ActiveNodeId = mutation.ActiveNodeId.Trim();
        }

        AddDistinct(state.CompletedNodeIds, mutation.CompleteNodeId);
        AddDistinct(state.SelectedRouteIds, mutation.SelectRouteId);

        foreach (var pair in mutation.SetFlags)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                state.Flags[pair.Key.Trim()] = pair.Value;
            }
        }

        foreach (var pair in mutation.SetValues)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                state.Values[pair.Key.Trim()] = pair.Value ?? "";
            }
        }

        foreach (var pair in mutation.AddCounters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var key = pair.Key.Trim();
            state.Counters.TryGetValue(key, out var value);
            state.Counters[key] = value + pair.Value;
        }

        state.Events.Add(new AuraJourneyStateEvent
        {
            Version = state.Version,
            TimestampUtc = timestampUtc.ToString("O"),
            ActorModId = request.OwnerModId ?? "",
            Action = request.Action ?? "",
            NodeId = request.NodeId ?? "",
            Message = request.Message ?? ""
        });

        if (state.Events.Count > 128)
        {
            state.Events.RemoveRange(0, state.Events.Count - 128);
        }

        return state;
    }

    private static AuraJourneyState Clone(AuraJourneyState? state)
    {
        if (state == null)
        {
            return new AuraJourneyState();
        }

        return new AuraJourneyState
        {
            SchemaVersion = state.SchemaVersion,
            JourneyId = state.JourneyId,
            OwnerModId = state.OwnerModId,
            Version = state.Version,
            Run = CloneRun(state.Run),
            ActiveNodeId = state.ActiveNodeId,
            CompletedNodeIds = state.CompletedNodeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SelectedRouteIds = state.SelectedRouteIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Flags = state.Flags.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Values = state.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Counters = state.Counters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Events = state.Events.Select(CloneEvent).ToList()
        };
    }

    private static AuraJourneyRunBinding CloneRun(AuraJourneyRunBinding? source)
    {
        return source == null
            ? new AuraJourneyRunBinding()
            : new AuraJourneyRunBinding
            {
                RunId = source.RunId,
                SaveSlotId = source.SaveSlotId,
                NativeModeKey = source.NativeModeKey,
                NativeModeValue = source.NativeModeValue,
                StartedUtc = source.StartedUtc
            };
    }

    private static AuraJourneyStateEvent CloneEvent(AuraJourneyStateEvent source)
    {
        return new AuraJourneyStateEvent
        {
            Version = source.Version,
            TimestampUtc = source.TimestampUtc,
            ActorModId = source.ActorModId,
            Action = source.Action,
            NodeId = source.NodeId,
            Message = source.Message
        };
    }

    private static void AddDistinct(System.Collections.Generic.List<string> values, string value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length > 0 && !values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(normalized);
        }
    }
}
