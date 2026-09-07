using AuraShared.Core;

internal static partial class CoreTestSuite
{
    internal static void TestNativeCardPresentationBoundary()
    {
        var state = new AuraNativeCardPresentationState();
        var releases = 0;
        state.Begin(false, () => releases++);
        state.Begin(true, () => releases++);
        Assert(releases == 1 && !state.AcceptsApply, "native style mutation releases existing material ownership before writing");
        Assert(!state.End() && !state.AcceptsApply, "nested native callbacks cannot reapply effects during the outer refresh");
        state.Begin(true, () => releases++);
        state.End();
        Assert(releases == 1, "nested style resets release the same old ownership only once");
        Assert(state.End() && state.AcceptsApply, "one successful outer completion owns the presentation commit");
        Assert(!state.End(), "duplicate native completion does not produce another presentation commit");
        state.Exit(() => releases++);
        state.Exit(() => releases++);
        state.Begin(true, () => releases++);
        Assert(!state.End() && !state.AcceptsApply && releases == 2,
            "native burn/throw is terminal; late style updates and duplicate exits cannot restore dynamic effects");
        var failed = new AuraNativeCardPresentationState();
        failed.Begin(true, () => { });
        Assert(!failed.AcceptsApply, "a native call without successful completion never exposes a partially rebuilt view");
    }
}
