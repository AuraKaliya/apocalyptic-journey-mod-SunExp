using AuraToolsExp.Dll.Features.Settings;

internal static partial class AuraToolsTestSuite
{
    public static void TestNativeContentVisibilityLease()
    {
        var first = new VisibilityTarget(true);
        var second = new VisibilityTarget(false);
        var third = new VisibilityTarget(true);
        var lease = new NativeContentVisibilityLease<VisibilityTarget>();

        Assert(lease.Acquire(
                   new[] { first, second, first, third },
                   target => target.Visible,
                   (target, visible) => target.Visible = visible)
               && lease.IsActive
               && lease.Count == 3
               && !first.Visible
               && !second.Visible
               && !third.Visible,
            "native settings content lease hides visible roots and deduplicates snapshots");

        Assert(!lease.Acquire(
                   new[] { first },
                   target => target.Visible,
                   (target, visible) => target.Visible = visible)
               && lease.Count == 3,
            "native settings content lease is idempotent while active");

        Assert(lease.Release()
               && !lease.IsActive
               && first.Visible
               && !second.Visible
               && third.Visible,
            "native settings content lease restores each original visibility state");
        Assert(!lease.Release(),
            "native settings content lease release is idempotent");

        var restoreContinued = new VisibilityTarget(true);
        var restoreFailed = new VisibilityTarget(true) { ThrowOnRestore = true };
        var errors = 0;
        lease.Acquire(
            new[] { restoreFailed, restoreContinued },
            target => target.Visible,
            (target, visible) =>
            {
                if (visible && target.ThrowOnRestore)
                {
                    throw new InvalidOperationException("restore failed");
                }
                target.Visible = visible;
            },
            _ => errors++);
        lease.Release();
        Assert(errors == 1
               && !lease.IsActive
               && restoreContinued.Visible,
            "native settings content lease isolates one failed restore and completes cleanup");
    }

    public static void TestToolboxTooltipPlacementPolicy()
    {
        var container = new ToolboxTooltipBounds(-400f, -300f, 400f, 300f);
        var middleAnchor = new ToolboxTooltipBounds(-20f, -20f, 20f, 20f);
        var middle = ToolboxTooltipPlacementPolicy.Resolve(container, middleAnchor, 120f, 36f);
        Assert(!middle.AboveAnchor
               && Math.Abs(middle.CenterX) < 0.01f
               && middle.CenterY < middleAnchor.MinimumY,
            "toolbox tooltip prefers the compact below-anchor placement");

        var bottomAnchor = new ToolboxTooltipBounds(-20f, -296f, 20f, -260f);
        var flipped = ToolboxTooltipPlacementPolicy.Resolve(container, bottomAnchor, 120f, 36f);
        Assert(flipped.AboveAnchor && flipped.CenterY > bottomAnchor.MaximumY,
            "toolbox tooltip flips above anchors near the lower boundary");

        var rightAnchor = new ToolboxTooltipBounds(380f, 10f, 410f, 46f);
        var clamped = ToolboxTooltipPlacementPolicy.Resolve(container, rightAnchor, 120f, 36f);
        Assert(clamped.CenterX <= 332.01f,
            "toolbox tooltip remains inside the horizontal safe margin");
    }

    private sealed class VisibilityTarget
    {
        public VisibilityTarget(bool visible)
        {
            Visible = visible;
        }

        public bool Visible { get; set; }

        public bool ThrowOnRestore { get; set; }
    }
}
