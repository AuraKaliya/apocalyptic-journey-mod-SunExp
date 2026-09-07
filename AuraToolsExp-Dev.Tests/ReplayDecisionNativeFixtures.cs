using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

// Native API/clock and the already-tested recorder journal are the fixture
// boundaries. The decision adapter and private-field read run production code.
public enum FightType { None, Player, OtherTurn, Enemy }
public sealed class FightManager { public static FightManager? Instance; public FightType fightType; }
public sealed class CardItem { public static bool canUse; }
namespace Witch.UI.Window
{
    public sealed class FightUI
    {
        public static bool InIEn;
        public static readonly List<CardItem> WaitCard = new();
        public bool NowAnimation;
        public readonly Queue<object> createCardQueue = new();
        public readonly Queue<object> animationQueue = new();
        public readonly NativeDecisionButton turnButton = new();
        private bool selectConfirmed;
        public void FixtureConfirm(bool value) => selectConfirmed = value;
        public bool FixtureConfirmed => selectConfirmed;
    }
    public sealed class NativeDecisionButton
    {
        public bool isInteractable = true;
        public readonly NativeDecisionObject gameObject = new();
    }
    public sealed class NativeDecisionObject { public bool activeInHierarchy = true; }
}
namespace Witch.UI
{
    public sealed class UIManager
    {
        public static UIManager? Instance;
        public Window.FightUI? Ui;
        public T? GetUI<T>(string name) where T : class => Ui as T;
    }
}
namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording
{
    internal sealed class ReplayCapturedActionSourceV17
    {
        internal string Kind = "", SourceInstanceId = "";
    }
}
namespace AuraToolsExp.Dll.Features.MatchRecords.Recording
{
    internal static partial class MatchReplayRecorder
    {
        private static readonly object Gate = new();
        private static readonly MatchReplayTerminalGate TerminalGate = new();
        private static readonly List<string> ContextStack = new();
        private static readonly Dictionary<string, object> PendingActionObservations = new(), PendingCardMotionObservations = new();
        private static readonly Dictionary<string, FixtureTransaction> Transactions = new();
        private static ReplayJournalBuilderV17? builder;
        private static int roundSequence = 1, actorTurnSequence = 1;
        private static long fixtureTicks;
        internal static readonly List<Exception> FixtureFailures = new();
        private static bool CanCaptureNoLock() => builder != null;
        private static long ElapsedTicks() => fixtureTicks;
        private static void QueueCaptureBatchNoLock() { }
        internal static void MarkCaptureFailure(string stage, Exception error) => FixtureFailures.Add(error);
        private sealed class FixtureTransaction { internal bool SourceCompleted; }
        internal static ReplayDocumentV17 FixtureDocument => builder!.Document;
        internal static void FixtureClock(long value) => fixtureTicks = value;
        internal static void FixtureReset()
        {
            DecisionCapture.Reset(); nativeSelectionOpen = false; nativeSelectionCommitted = false;
            TerminalGate.Reset(); ContextStack.Clear(); PendingActionObservations.Clear(); PendingCardMotionObservations.Clear();
            Transactions.Clear(); FixtureFailures.Clear(); fixtureTicks = 0;
            builder = new ReplayJournalBuilderV17(new ReplayDocumentHeaderCoreV17(), new ReplayVisibleStateV17());
            FightManager.Instance = new FightManager { fightType = FightType.Player };
            Witch.UI.UIManager.Instance = new Witch.UI.UIManager { Ui = new Witch.UI.Window.FightUI() };
            Witch.UI.Window.FightUI.InIEn = false; Witch.UI.Window.FightUI.WaitCard.Clear(); CardItem.canUse = true;
        }
        internal static void FixtureAcceptedCard(bool nested)
        {
            if (nested) ContextStack.Add("parent");
            ObserveAcceptedSourceNoLock(new ReplayV17.Recording.ReplayCapturedActionSourceV17 { Kind = "Card", SourceInstanceId = "accepted-card" });
            if (nested) ContextStack.Clear();
        }
        internal static void FixtureSetSource(bool completed)
        { Transactions["source"] = new FixtureTransaction { SourceCompleted = completed }; }
        internal static void FixtureSetMotion(bool active)
        { PendingCardMotionObservations.Clear(); if (active) PendingCardMotionObservations["arrival"] = new object(); }
        internal static void FixtureTerminal() => TerminalGate.Prepare("Win");
    }
}
