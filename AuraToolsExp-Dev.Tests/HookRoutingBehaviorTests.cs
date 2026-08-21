using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

internal static partial class AuraToolsTestSuite
{
    public static void TestAuraRoutedHookOwnershipAndLeases()
    {
        var config = new ModConfig();
        var order = new List<string>();
        Action<ModHookContext> low = _ => order.Add("low");
        Action<ModHookContext> high = _ => order.Add("high");
        var lowLease = AuraSharedHooks.RegisterBeforeRouted(
            config,
            "Test.Route.Before",
            Request("OwnerA", "Low", 0, low));
        var duplicateLowLease = AuraSharedHooks.RegisterBeforeRouted(
            config,
            "Test.Route.Before",
            Request("OwnerA", "Low", 0, low));
        var highLease = AuraSharedHooks.RegisterBeforeRouted(
            config,
            "Test.Route.Before",
            Request("OwnerB", "High", 100, high));

        Assert(config.BeforeRegistrationCount("Test.Route.Before") == 1,
            "routed hooks install one native callback for a target and phase");
        config.InvokeBefore("Test.Route.Before");
        Assert(order.SequenceEqual(new[] { "high", "low" }),
            "routed hook subscribers dispatch once in deterministic priority order");

        order.Clear();
        duplicateLowLease.Dispose();
        config.InvokeBefore("Test.Route.Before");
        Assert(order.SequenceEqual(new[] { "high", "low" }),
            "disposing one idempotent lease preserves the remaining subscriber lease");

        order.Clear();
        lowLease.Dispose();
        config.InvokeBefore("Test.Route.Before");
        Assert(order.SequenceEqual(new[] { "high" }),
            "disposing the final owner lease removes its routed subscriber");

        var warnings = new List<string>();
        var conflictLease = AuraSharedHooks.RegisterBeforeRouted(
            config,
            "Test.Route.Before",
            Request("OwnerB", "High", 100, _ => order.Add("conflict")),
            warn: warnings.Add);
        order.Clear();
        config.InvokeBefore("Test.Route.Before");
        Assert(order.SequenceEqual(new[] { "high" })
               && warnings.Count == 1,
            "owner-qualified identity conflicts are rejected without replacing the active handler");
        conflictLease.Dispose();

        var afterCount = 0;
        var failingLease = AuraSharedHooks.RegisterAfterRouted(
            config,
            "Test.Route.After",
            Request("OwnerA", "Failing", 100, _ => throw new InvalidOperationException("expected")));
        var healthyLease = AuraSharedHooks.RegisterAfterRouted(
            config,
            "Test.Route.After",
            Request("OwnerB", "Healthy", 0, _ => afterCount++));
        config.InvokeAfter("Test.Route.After");
        Assert(config.AfterRegistrationCount("Test.Route.After") == 1
               && afterCount == 1,
            "safe routed dispatch isolates one subscriber failure from later subscribers");

        failingLease.Dispose();
        healthyLease.Dispose();
        highLease.Dispose();

        var hotConfig = new ModConfig();
        var hotCalls = 0;
        using var hotLease = AuraSharedHooks.RegisterBeforeRouted(
            hotConfig,
            "Test.Route.HotPath",
            Request("OwnerHot", "Counter", 0, _ => hotCalls++));
        var hotContext = new ModHookContext();
        hotConfig.InvokeBefore("Test.Route.HotPath", hotContext);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            hotConfig.InvokeBefore("Test.Route.HotPath", hotContext);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread()
                        - allocatedBefore;
        Assert(hotCalls == 10_001 && allocated <= 256,
            "routed native dispatch remains allocation-free across ten thousand hot-path calls");
    }

    public static void TestAuraToolsHookOwnerActivation()
    {
        var config = new ModConfig();
        var calls = 0;
        using var lease = AuraToolsHookRegistry.Before(
            config,
            "Test.AuraTools.OwnerActivation",
            _ => calls++,
            "FeatureOwner");
        config.InvokeBefore("Test.AuraTools.OwnerActivation");
        Assert(calls == 1,
            "AuraTools owned hooks dispatch while their module owner is active");

        AuraToolsHookRegistry.SetOwnerActive("FeatureOwner", false);
        config.InvokeBefore("Test.AuraTools.OwnerActivation");
        Assert(calls == 1,
            "AuraTools module deactivation removes the owner from the routed snapshot");

        AuraToolsHookRegistry.SetOwnerActive("FeatureOwner", true);
        config.InvokeBefore("Test.AuraTools.OwnerActivation");
        Assert(calls == 2
               && config.BeforeRegistrationCount(
                   "Test.AuraTools.OwnerActivation") == 1,
            "AuraTools module reactivation restores one subscriber without another native hook");
    }

    private static AuraRoutedHookRequest Request(
        string owner,
        string handlerId,
        int priority,
        Action<ModHookContext> handler)
    {
        return new AuraRoutedHookRequest
        {
            OwnerModId = owner,
            HandlerId = handlerId,
            Priority = priority,
            Handler = handler,
            SafeInvoke = true
        };
    }
}
