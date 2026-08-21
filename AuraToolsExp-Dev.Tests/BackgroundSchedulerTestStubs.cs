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
        public static bool EnsureMainThreadRunner() => true;

        public static bool RunOnceAfterFrames(
            AuraSharedFrameActionRequest request)
        {
            request.Action?.Invoke();
            return true;
        }
    }
}
