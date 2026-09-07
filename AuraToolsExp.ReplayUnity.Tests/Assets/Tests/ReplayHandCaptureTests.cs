using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using NUnit.Framework;
using UnityEngine;
using Witch.UI.Window;
using Object = UnityEngine.Object;

public sealed class ReplayHandCaptureTests
{
    private GameObject root;
    private FightUI ui;

    [SetUp] public void Setup()
    {
        MatchReplayRecorder.ResetFixture();
        root = new GameObject("FightUI", typeof(RectTransform), typeof(FightUI));
        ui = root.GetComponent<FightUI>();
        var hand = new GameObject("Hand", typeof(RectTransform), typeof(CardContainer));
        hand.transform.SetParent(root.transform, false);
        ui.cardContainer = hand.GetComponent<CardContainer>();
    }
    [TearDown] public void Cleanup()
    {
        Object.DestroyImmediate(root);
        MatchReplayRecorder.ResetFixture();
    }
    private CardItem NewCard(string id)
    {
        var go = new GameObject(id, typeof(RectTransform), typeof(CardItem));
        go.transform.SetParent(ui.cardContainer.transform, false);
        go.transform.localPosition = new Vector3(-1500, 0, 0);
        var card = go.GetComponent<CardItem>();
        card.dataConfig = new DataConfig { InstanceID = id, Name = id, Cost = 1 };
        return card;
    }
    private void NativeCreate(CardItem card)
    {
        MatchReplayRecorder.ObserveCardDraw(card);
        FightUI.cardItemList.Add(card);
        MatchReplayRecorder.BeginHandLayout(ui);
        MatchReplayRecorder.EndHandLayout(ui);
        MatchReplayRecorder.ObserveHandCreated(ui, new object[] { card.dataConfig });
    }

    [Test] public void ConsecutiveDrawsCommitAtFrameBarriersWithoutAnyCardUse()
    {
        var first = NewCard("first");
        MatchReplayRecorder.Clock = 100;
        NativeCreate(first);
        MatchReplayRecorder.FlushFixtureBarrier();
        var second = NewCard("second");
        MatchReplayRecorder.Clock = 200;
        NativeCreate(second);
        MatchReplayRecorder.FlushFixtureBarrier();
        Assert.That(MatchReplayRecorder.Starts.Count(entry => entry.Kind == "Draw"), Is.EqualTo(2));
        Assert.That(MatchReplayRecorder.Commits.Any(value => value.Time == 100 && value.Cards.SequenceEqual(new[] { "first" })), Is.True);
        Assert.That(MatchReplayRecorder.Commits.Any(value => value.Time == 200 && value.Cards.SequenceEqual(new[] { "first", "second" })), Is.True);
        Assert.That(MatchReplayRecorder.Starts[0].Position.x, Is.EqualTo(-1500));
    }

    [Test] public void ArrivalAndExistingHandReflowHaveSeparateContinuousOwners()
    {
        var first = NewCard("first"); NativeCreate(first); MatchReplayRecorder.EndFixtureMotion(first);
        first.transform.localPosition = new Vector3(-100, 0, 0);
        var second = NewCard("second"); NativeCreate(second);
        Assert.That(MatchReplayRecorder.Starts.Count(entry => entry.Card == "first" && entry.Kind == "HandLayout"), Is.EqualTo(1));
        Assert.That(MatchReplayRecorder.Starts.Count(entry => entry.Card == "second"), Is.EqualTo(1), "Nested layout/create callbacks do not duplicate the new view.");
    }

    [Test] public void NestedLayoutAndCreationCoalesceStateButKeepEveryArrival()
    {
        NativeCreate(NewCard("first"));
        NativeCreate(NewCard("second"));
        MatchReplayRecorder.BeginHandLayout(ui);
        MatchReplayRecorder.EndHandLayout(ui);
        Assert.That(MatchReplayRecorder.Commits, Is.Empty, "Native UI callbacks only request reconciliation.");
        Assert.That(MatchReplayRecorder.Starts.Count(entry => entry.Kind == "Draw"), Is.EqualTo(2));
        MatchReplayRecorder.FlushFixtureBarrier();
        Assert.That(MatchReplayRecorder.Commits.Count, Is.EqualTo(1));
        Assert.That(MatchReplayRecorder.Commits[0].Cards, Is.EqualTo(new[] { "first", "second" }));
        MatchReplayRecorder.FlushFixtureBarrier();
        Assert.That(MatchReplayRecorder.Commits.Count, Is.EqualTo(1));
    }

    [Test] public void ActionBarrierSeparatesArrivalFromImmediateConsumption()
    {
        var card = NewCard("auto");
        NativeCreate(card);
        MatchReplayRecorder.FlushFixtureBarrier(); // Production BeforeCardAction flushes the same coordinator.
        card.hasDone = true;
        MatchReplayRecorder.EndHandLayout(ui);
        MatchReplayRecorder.FlushFixtureBarrier();
        Assert.That(MatchReplayRecorder.Commits.Select(value => value.Cards.Length), Is.EqualTo(new[] { 1, 0 }));
        Assert.That(MatchReplayRecorder.Starts.Count(entry => entry.Kind == "Draw"), Is.EqualTo(1));
    }

    [Test] public void NestedCreationAndImmediateConsumptionStillObserveBirthBeforeRegistration()
    {
        var outer = NewCard("outer");
        MatchReplayRecorder.ObserveCardDraw(outer);
        Assert.That(FightUI.cardItemList, Is.Empty);
        var inner = NewCard("inner"); NativeCreate(inner);
        outer.dataConfig.Name = "Initialized outer";
        FightUI.cardItemList.Add(outer);
        MatchReplayRecorder.ObserveHandCreated(ui, new object[] { outer.dataConfig });
        Assert.That(MatchReplayRecorder.Starts.Select(entry => entry.Card), Is.EqualTo(new[] { "outer", "inner" }));
        Assert.That(MatchReplayRecorder.FixtureSnapshot(outer).RenderedName, Is.EqualTo("Initialized outer"));
        outer.dataConfig.Name = "Later change";
        NativeCreate(NewCard("third"));
        Assert.That(MatchReplayRecorder.FixtureSnapshot(outer).RenderedName, Is.EqualTo("Initialized outer"), "Later draws cannot rewrite an earlier arrival's card face.");
    }

    [Test] public void OtherSurfacesAndPlaybackCannotCreateHandObservations()
    {
        var preview = NewCard("preview"); preview.transform.SetParent(root.transform, false);
        MatchReplayRecorder.ObserveCardDraw(preview);
        MatchReplaySessionState.IsPlayback = true;
        NativeCreate(NewCard("playback"));
        Assert.That(MatchReplayRecorder.Starts, Is.Empty);
        Assert.That(MatchReplayRecorder.Commits, Is.Empty);
    }

    [Test] public void AutoConsumedCardHasAnArrivalButDoesNotReappearInTheStableHand()
    {
        var card = NewCard("auto");
        MatchReplayRecorder.ObserveCardDraw(card);
        card.hasDone = true;
        card.ignore = true;
        card.dataConfig.Name = "Initialized auto card";
        FightUI.cardItemList.Add(card);
        MatchReplayRecorder.BeginHandLayout(ui);
        MatchReplayRecorder.EndHandLayout(ui);
        MatchReplayRecorder.ObserveHandCreated(ui, new object[] { card.dataConfig });
        MatchReplayRecorder.FlushFixtureBarrier();
        Assert.That(MatchReplayRecorder.Starts.Count, Is.EqualTo(1));
        Assert.That(MatchReplayRecorder.Starts[0].Kind, Is.EqualTo("Draw"));
        Assert.That(MatchReplayRecorder.Commits.All(value => value.Cards.Length == 0), Is.True);
        Assert.That(MatchReplayRecorder.FixtureSnapshot(card).RenderedName, Is.EqualTo("Initialized auto card"));
    }

    [Test] public void AQueuedOrRejectedRequestDoesNotInventAHandCard()
    {
        MatchReplayRecorder.ObserveHandRequest(ui);
        Assert.That(MatchReplayRecorder.StateBarrierRequested, Is.True);
        Assert.That(MatchReplayRecorder.Starts, Is.Empty);
        Assert.That(MatchReplayRecorder.Commits, Is.Empty);
    }
}
