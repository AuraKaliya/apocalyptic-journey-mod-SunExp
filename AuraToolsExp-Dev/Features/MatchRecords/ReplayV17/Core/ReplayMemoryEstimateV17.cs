using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

// Admission accounting is an estimate of retained managed data, not a claim
// about process RSS. Large text, sampled tracks and asset arrays are counted.
internal static class ReplayMemoryEstimateV17
{
    internal const long MaximumCaptureBytes = 128L * 1024 * 1024;
    internal static long Document(ReplayDocumentV17 document) => 16_384
        + document.Assets.Sum(asset => asset.Payload?.LongLength ?? asset.ByteLength)
        + document.TruthEvents.Sum(Event) + document.PresentationEvents.Sum(Event)
        + (long)(document.Presentation.Cards.Count + document.Presentation.Entities.Count + document.Presentation.Intents.Count) * 4096;

    internal static long Event(ReplayJournalEventV17 value)
    {
        var size = 1024L;
        if (value.Delta != null)
            foreach (var operation in value.Delta.Operations)
                size += 512L + Card(operation.Card) + operation.Buffs.Count * 192L + operation.Intents.Count * 512L
                    + operation.Extensions.Sum(extension => 192L + Text(extension.PayloadJson) + Text(extension.DisplayText))
                    + operation.Resources.Sum(resource => 192L + Text(resource.DisplayText) + Text(resource.Name));
        var view = value.Presentation;
        if (view != null)
            size += 512L + Text(view.DisplayText) + Text(view.FinalDisplayText) + Text(view.ExtensionPayloadJson)
                + Text(view.ResourcePath) + Card(view.CardView) + view.TransformSamples.Count * 160L + view.WorldTransformSamples.Count * 224L;
        return size;
    }

    private static long Card(ReplayVisibleCardStateV17? value) => value == null ? 0 : 512L + Text(value.RenderedName) + Text(value.RenderedDescription);
    private static long Text(string? value) => (value?.Length ?? 0) * 2L;
}
