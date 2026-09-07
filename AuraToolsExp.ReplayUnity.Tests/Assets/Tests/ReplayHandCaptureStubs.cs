using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording;
using UnityEngine;
using Witch.UI.Window;

// Native game API and journal sink are fixture boundaries. The hand arrival,
// layout, identity/deduplication and commit adapter itself is production code.
public sealed class DataConfig { public string InstanceID = ""; public string Name = ""; public int Cost; }
public sealed class CardItem : MonoBehaviour { public DataConfig dataConfig; public bool draging; public bool ignore; public bool hasDone; }
public sealed class CardContainer : MonoBehaviour { }
namespace Witch.UI.Window
{
    public sealed class FightUI : MonoBehaviour
    {
        public static List<CardItem> cardItemList = new();
        public CardContainer cardContainer;
    }
}
namespace AuraToolsExp.Dll.Features.MatchRecords.Playback
{
    internal static class MatchReplaySessionState { internal static bool IsPlayback; }
}
namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording
{
    internal sealed class ReplayCapturedActionSourceV17 { internal string Kind, Label, SourceInstanceId; }
    internal static class ReplayFactCaptureV17
    {
        internal static ReplayCapturedActionSourceV17 CaptureActionSource(CardItem card, object catalog) =>
            new() { SourceInstanceId = card.dataConfig.InstanceID };
        internal static ReplayVisibleCardStateV17 CaptureCardView(CardItem card, object catalog) =>
            new() { CardInstanceId = card.dataConfig.InstanceID, DescriptorId = "fixture", RenderedName = card.dataConfig.Name,
                DisplayedCost = card.dataConfig.Cost, Zone = "Hand", HasMeasuredLayout = true };
    }
}
namespace AuraToolsExp.Dll.Features.MatchRecords.Recording
{
    internal static partial class MatchReplayRecorder
    {
        private static readonly object Gate = new();
        private static readonly object catalog = new();
        private static readonly Dictionary<string, CardMotionObservation> PendingCardMotionObservations = new();
        private static readonly Dictionary<string, PendingPresentationTiming> PendingCardMotions = new();
        internal static readonly List<(long Time, string[] Cards)> Commits = new();
        internal static readonly List<(long Time, string Card, string Kind, Vector3 Position)> Starts = new();
        internal static long Clock;
        internal static bool StateBarrierRequested;
        private static bool CanCaptureNoLock() => true;
        private static long ElapsedTicks() => Clock;
        private static string BeginSourceTransactionNoLock(ReplayCapturedActionSourceV17 source, bool pushContext) => source.SourceInstanceId;
        private static string BeginSystemTransactionNoLock(string kind, string source) => source;
        private static void ApplyCurrentStateNoLock(string tx) => Commits.Add((Clock, FightUI.cardItemList.Where(card => !card.hasDone).Select(card => card.dataConfig.InstanceID).ToArray()));
        private static void MarkAndCompleteSystemTransactionNoLock(string tx) { }
        private static void QueueCaptureBatchNoLock() { }
        private static void RequestStableBarrierNoLock(string reason, bool needsStateCapture) => StateBarrierRequested |= needsStateCapture;
        internal static void FlushFixtureBarrier()
        {
            if (!StateBarrierRequested) return;
            StateBarrierRequested = false;
            ApplyCurrentStateNoLock("frame-or-action-barrier");
        }
        private static KeyValuePair<string, CardMotionObservation> MotionFor(CardItem card) =>
            PendingCardMotionObservations.FirstOrDefault(pair => pair.Value.Visual == card);
        private static void CaptureCardMotionSampleNoLock(string key, CardMotionObservation value, long time) { }
        private static string StartMotionNoLock(string id, string tx, CardItem card, FightUI ui, string kind, bool owns)
        {
            var key = id + ":" + Starts.Count;
            Starts.Add((Clock, id, kind, card.transform.localPosition));
            PendingCardMotionObservations[key] = new() { Visual = card };
            PendingCardMotions[key] = new() { Event = new() { Presentation = new() { Kind = kind, SourceInstanceId = id } } };
            return key;
        }
        internal static void ResetFixture()
        {
            ObservedHandViews.Clear(); PendingCardMotionObservations.Clear(); PendingCardMotions.Clear();
            Starts.Clear(); Commits.Clear(); Clock = 0; FightUI.cardItemList.Clear();
            StateBarrierRequested = false;
            Playback.MatchReplaySessionState.IsPlayback = false;
        }
        internal static void EndFixtureMotion(CardItem card)
        {
            var pair = MotionFor(card);
            if (pair.Value != null) { PendingCardMotionObservations.Remove(pair.Key); PendingCardMotions.Remove(pair.Key); }
        }
        internal static ReplayVisibleCardStateV17 FixtureSnapshot(CardItem card) => PendingCardMotions[MotionFor(card).Key].Event.Presentation.CardView;
        private sealed class CardMotionObservation
        {
            internal CardItem Visual;
            internal bool IsHandMotion, PointerReleased, AwaitNativeHandSettled, CaptureStateOnComplete, AwaitingInitialCardBinding;
        }
        private sealed class PendingPresentationTiming { internal ReplayJournalEventV17 Event; }
    }
}
