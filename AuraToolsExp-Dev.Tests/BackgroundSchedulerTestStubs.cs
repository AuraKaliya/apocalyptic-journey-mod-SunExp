namespace AuraShared.Core
{
    public enum AuraSharedFramePhase
    {
        Reconcile
    }

    public sealed class AuraSharedFrameActionRequest
    {
        public string OwnerId { get; set; } = "";
        public string Source { get; set; } = "";
        public int DelayFrames { get; set; }
        public AuraSharedFramePhase Phase { get; set; }
        public int Priority { get; set; }
        public Action? Action { get; set; }
    }

    public static class AuraSharedFrameScheduler
    {
        private static readonly List<(long Due, Action Apply)> Pending = new();
        public static long Frame { get; private set; }
        public static bool RunnerAvailable { get; set; } = true;
        public static bool EnsureMainThreadRunner() => RunnerAvailable;

        public static bool RunOnceAfterFrames(
            AuraSharedFrameActionRequest request)
        {
            if (!RunnerAvailable) return false;
            if (request.Action != null) Pending.Add((Frame + Math.Max(1, request.DelayFrames), request.Action));
            return true;
        }

        public static void AdvanceFrame()
        {
            Frame++;
            AuraSharedOrderedWorkQueue.PumpRegistered();
            var due = Pending.Where(item => item.Due <= Frame).ToArray();
            Pending.RemoveAll(item => item.Due <= Frame);
            foreach (var item in due) item.Apply();
        }
    }
}
