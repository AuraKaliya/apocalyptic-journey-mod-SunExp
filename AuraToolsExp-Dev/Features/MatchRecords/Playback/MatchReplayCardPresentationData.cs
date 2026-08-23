using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal sealed class MatchReplayCardPresentationPayload
{
    internal Dictionary<string, string> Data { get; set; } = new(StringComparer.Ordinal);

    internal Dictionary<string, string> Vars { get; set; } = new(StringComparer.Ordinal);

    internal int DataType { get; set; }
}

internal static class MatchReplayCardPresentationData
{
    internal static MatchReplayCardPresentationPayload Compose(
        MatchReplayCardState state,
        int? displayedCost = null)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var data = RestoreValues(state.Data);
        var vars = RestoreValues(state.Vars);
        if (!data.ContainsKey("Tag")) data["Tag"] = "";
        vars["Tag"] = data["Tag"];
        if (!vars.ContainsKey("SpecialTag")) vars["SpecialTag"] = "";
        if (!string.IsNullOrWhiteSpace(state.CardId))
        {
            data["Id"] = state.CardId;
        }

        if (!string.IsNullOrWhiteSpace(state.ReplayCardId))
        {
            vars["InstanceID"] = state.ReplayCardId;
        }

        if (displayedCost.HasValue)
        {
            var cost = Math.Max(0, displayedCost.Value).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            // Native card-face rendering reads Expend from base data. Compose the
            // complete presentation dictionary before DataConfig wraps it read-only,
            // and mirror the override in Vars for dynamic consumers.
            data["Expend"] = cost;
            vars["Expend"] = cost;
        }

        return new MatchReplayCardPresentationPayload
        {
            Data = data,
            Vars = vars,
            DataType = state.DataType
        };
    }

    internal static void RestoreRuntimeIdentity(
        IDictionary<string, string> vars,
        string? replayCardId)
    {
        if (vars == null)
        {
            throw new ArgumentNullException(nameof(vars));
        }

        var normalized = (replayCardId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            // DataConfig(IDictionary, IDictionary, ...) always replaces InstanceID with
            // a new Guid. Replay identity is a recording contract, so it must be restored
            // after construction before the config enters any runtime collection.
            vars["InstanceID"] = normalized;
        }
    }

    private static Dictionary<string, string> RestoreValues(
        IEnumerable<MatchReplayStringValue>? values)
    {
        return (values ?? Enumerable.Empty<MatchReplayStringValue>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value ?? "",
                StringComparer.Ordinal);
    }
}
