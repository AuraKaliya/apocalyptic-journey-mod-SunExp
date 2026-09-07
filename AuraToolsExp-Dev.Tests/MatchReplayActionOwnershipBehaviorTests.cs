using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static partial class AuraToolsTestSuite
{
    internal static void TestReplayActionOwnerGeneration()
    {
        var owner = new ReplayActionOwnerGenerationV17();
        Assert(owner.Observe(7, false), "an old inactive native generation does not claim a newly observed action");
        Assert(owner.Observe(8, true), "the actual active native generation becomes the action owner");
        Assert(owner.Observe(8, true), "repeated sampling keeps the same native action owner");
        Assert(!owner.Observe(9, true), "a later action cannot prolong or contribute samples to its predecessor");
        Assert(!owner.Observe(0, false), "native reset terminates the previous generation");
        var noNativeCounter = new ReplayActionOwnerGenerationV17();
        Assert(noNativeCounter.Observe(null, true) && noNativeCounter.Observe(null, false),
            "without a native counter the existing actor-local completion evidence remains responsible");
    }
}
