using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayHandLifecycleContractV17
{
    internal const string Contract = "observed-arrival-and-layout.v1";
    internal const string Arrival = "Draw";
    internal const string Layout = "HandLayout";

    internal static void Validate(ReplayDocumentV17 document, ICollection<string> errors)
    {
        if (document.Presentation.Ui.HandPresentationContract == null) return;
        if (document.Presentation.Ui.HandPresentationContract != Contract)
        {
            errors.Add("hand-presentation-contract-unsupported");
            return;
        }
        var initial = document.InitialState.Cards.Where(card => card.Zone == "Hand")
            .Select(card => card.CardInstanceId).ToHashSet(StringComparer.Ordinal);
        var inHand = new HashSet<string>(initial, StringComparer.Ordinal);
        var arrivals = document.PresentationEvents.Where(item => item.EventType == ReplayEventTypesV17.CardMotionPresented
                && item.Presentation?.Kind == Arrival)
            .GroupBy(item => item.Presentation!.SourceInstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.TimeTicks).ToArray(), StringComparer.Ordinal);
        var exitedAt = new Dictionary<string, long>(StringComparer.Ordinal);
        var usedArrivals = new HashSet<long>();
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        foreach (var operation in value.Delta?.Operations ?? new List<ReplayStateOperationV17>())
        {
            var card = operation.Card;
            if (operation.Kind == ReplayStateOperationKindsV17.RemoveVisibleCard)
            {
                if (inHand.Remove(operation.CardInstanceId)) exitedAt[operation.CardInstanceId] = value.TimeTicks;
                continue;
            }
            if (operation.Kind is not ReplayStateOperationKindsV17.AddVisibleCard and not ReplayStateOperationKindsV17.MoveVisibleCard || card == null) continue;
            if (card.Zone != "Hand")
            {
                if (inHand.Remove(card.CardInstanceId)) exitedAt[card.CardInstanceId] = value.TimeTicks;
                continue;
            }
            if (inHand.Add(card.CardInstanceId)
                && !ConsumeArrival(card.CardInstanceId, exitedAt.TryGetValue(card.CardInstanceId, out var exit) ? exit : 0, value.TimeTicks))
                errors.Add("hand-arrival-presentation-missing:" + card.CardInstanceId + ":" + value.Sequence);
        }
        foreach (var value in document.PresentationEvents.Where(item => item.EventType == ReplayEventTypesV17.CardMotionPresented))
        {
            var message = value.Presentation!;
            if (message.Kind is Arrival or Layout or "Hand")
            {
                if (string.IsNullOrWhiteSpace(message.VisualInstanceId))
                    errors.Add("hand-visual-identity-missing:" + value.Sequence);
                if (message.Kind != Arrival && !initial.Contains(message.SourceInstanceId)
                    && !HasArrival(message.SourceInstanceId, 0, value.TimeTicks))
                    errors.Add("hand-interaction-before-appearance:" + message.SourceInstanceId + ":" + value.Sequence);
            }
            if (message.CardView != null && (message.CardView.CardInstanceId != message.SourceInstanceId
                || message.CardView.DescriptorId != message.DescriptorId))
                errors.Add("card-view-snapshot-identity-mismatch:" + value.Sequence);
        }

        bool HasArrival(string id, long since, long until) => arrivals.TryGetValue(id, out var views)
            && views.Any(item => item.TimeTicks >= since && item.TimeTicks <= until);
        bool ConsumeArrival(string id, long since, long until)
        {
            if (!arrivals.TryGetValue(id, out var views)) return false;
            var view = views.FirstOrDefault(item => item.TimeTicks >= since && item.TimeTicks <= until && !usedArrivals.Contains(item.Sequence));
            return view != null && usedArrivals.Add(view.Sequence);
        }
    }
}
