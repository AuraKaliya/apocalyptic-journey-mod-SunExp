using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AuraDirector.Detour;
using AuraDirector.Shared;
using HarmonyLib;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        var managedPath = args.Length > 0 ? Path.GetFullPath(args[0]) : "";
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) => ResolveManagedAssembly(managedPath, eventArgs);

        try
        {
            TestVerifiedBuildCatalog();
            TestHarmonyHoldAndResume();
            TestImmediateReleaseReentry();
            TestRejectedAndFailedSinksRunOriginal();
            TestReleaseAll();
            TestCurrentGameTargetCapabilityGate();
            Console.WriteLine("AuraDirector detour tests passed: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestVerifiedBuildCatalog()
    {
        Assert(
            AuraDirectorReadyToStartDetourBackend.VerifiedWitchBuilds.TryGetValue(
                AuraDirectorReadyToStartDetourBackend.VerifiedWitchSha256,
                out var legacyBuild)
            && legacyBuild == "1.0.23816797",
            "the previous verified Witch build remains allowlisted");
        Assert(
            AuraDirectorReadyToStartDetourBackend.VerifiedWitchBuilds.TryGetValue(
                AuraDirectorReadyToStartDetourBackend.VerifiedWitchSha256V24591395,
                out var currentBuild)
            && currentBuild == "1.0.24591395",
            "the current Witch build is allowlisted");
        Assert(
            !AuraDirectorReadyToStartDetourBackend.VerifiedWitchBuilds.ContainsKey(
                new string('0', 64)),
            "unknown Witch hashes remain outside the allowlist");
    }

    private static Assembly? ResolveManagedAssembly(string managedPath, ResolveEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(managedPath))
        {
            return null;
        }

        var name = new AssemblyName(eventArgs.Name).Name;
        var candidate = Path.Combine(managedPath, name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    private static void TestHarmonyHoldAndResume()
    {
        var harmony = new Harmony("AuraDirector.Detour.Tests.fixture");
        var sink = new CapturingSink();
        var registry = new AuraDirectorOneShotHoldRegistry<FixtureTarget>(
            "fixture",
            sink,
            target => target.ReadyToStart());
        FixturePatch.Registry = registry;
        var targetMethod = AccessTools.Method(typeof(FixtureTarget), nameof(FixtureTarget.ReadyToStart));
        var prefix = AccessTools.Method(typeof(FixturePatch), nameof(FixturePatch.Prefix));
        harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix));

        try
        {
            var target = new FixtureTarget();
            target.ReadyToStart();
            target.ReadyToStart();
            Assert(target.OriginalCalls == 0, "prefix suppresses the original while the hold is active");
            Assert(sink.AcceptedCount == 1 && registry.HeldCount == 1, "duplicate calls share one active hold");
            Assert(sink.LastHold != null && sink.LastHold.TryRelease("fixture-complete"), "hold releases once");
            Assert(target.OriginalCalls == 1, "release re-enters and executes the original exactly once");
            Assert(sink.LastHold!.ReleaseReason == "fixture-complete", "release reason is retained");
            Assert(!sink.LastHold.TryRelease("duplicate"), "duplicate release is idempotent");
            Assert(target.OriginalCalls == 1 && registry.HeldCount == 0, "duplicate release does not execute the original again");
        }
        finally
        {
            harmony.UnpatchAll("AuraDirector.Detour.Tests.fixture");
            FixturePatch.Registry = null;
        }

        var restored = new FixtureTarget();
        restored.ReadyToStart();
        Assert(restored.OriginalCalls == 1, "unpatch restores the unmodified method");
    }

    private static void TestRejectedAndFailedSinksRunOriginal()
    {
        var rejected = new FixtureTarget();
        var rejectRegistry = new AuraDirectorOneShotHoldRegistry<FixtureTarget>(
            "fixture",
            new RejectingSink(),
            target => target.ReadyToStart());
        Assert(rejectRegistry.Intercept(rejected), "rejected holds fail open");

        var failed = new FixtureTarget();
        var failedRegistry = new AuraDirectorOneShotHoldRegistry<FixtureTarget>(
            "fixture",
            new ThrowingSink(),
            target => target.ReadyToStart());
        Assert(failedRegistry.Intercept(failed), "sink exceptions fail open");
    }

    private static void TestImmediateReleaseReentry()
    {
        var harmony = new Harmony("AuraDirector.Detour.Tests.immediate");
        var registry = new AuraDirectorOneShotHoldRegistry<FixtureTarget>(
            "fixture",
            new ImmediateReleaseSink(),
            target => target.ReadyToStart());
        FixturePatch.Registry = registry;
        var targetMethod = AccessTools.Method(typeof(FixtureTarget), nameof(FixtureTarget.ReadyToStart));
        var prefix = AccessTools.Method(typeof(FixturePatch), nameof(FixturePatch.Prefix));
        harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix));

        try
        {
            var target = new FixtureTarget();
            target.ReadyToStart();
            Assert(target.OriginalCalls == 1 && registry.HeldCount == 0,
                "synchronous release re-enters through bypass without recursion or duplication");
        }
        finally
        {
            harmony.UnpatchAll("AuraDirector.Detour.Tests.immediate");
            FixturePatch.Registry = null;
        }
    }

    private static void TestReleaseAll()
    {
        var sink = new CapturingSink();
        var registry = new AuraDirectorOneShotHoldRegistry<FixtureTarget>(
            "fixture",
            sink,
            target => target.ReadyToStart());
        var first = new FixtureTarget();
        var second = new FixtureTarget();
        Assert(!registry.Intercept(first) && !registry.Intercept(second), "multiple targets can be held independently");
        Assert(registry.StopAndReleaseAll("shutdown") == 2, "shutdown releases all active holds");
        Assert(first.OriginalCalls == 1 && second.OriginalCalls == 1, "release-all resumes every original once");
        Assert(registry.Intercept(new FixtureTarget()), "stopped registry no longer accepts new holds");
    }

    private static void TestCurrentGameTargetCapabilityGate()
    {
        var probe = AuraDirectorReadyToStartDetourBackend.Probe();
        using var backend = new AuraDirectorReadyToStartDetourBackend();
        if (!probe.Supported)
        {
            Assert(probe.Code == "detour-target-build-unverified",
                "unknown Witch.dll builds fail closed at the capability probe");
            var rejected = backend.Install(new RejectingSink());
            Assert(!rejected.Supported
                   && rejected.Code == "detour-target-build-unverified"
                   && !backend.IsInstalled,
                "unverified game builds cannot install the detour backend");
            Assert(!AuraDirectorReadyToStartDetourBackend.IsOwnedPrefixInstalled(),
                "an unverified target never receives the Harmony prefix");
            return;
        }

        Assert(probe.Code == "detour-compatible", "allowlisted Witch.dll builds pass the capability probe");
        Assert(probe.Detail.Contains("1.0.24591395"), "the current capability probe reports its verified game build");
        var installed = backend.Install(new RejectingSink());
        Assert(installed.Supported && installed.Code == "detour-installed" && backend.IsInstalled,
            "Harmony prefix installs on the current ReadyToStart method");
        Assert(AuraDirectorReadyToStartDetourBackend.IsOwnedPrefixInstalled(),
            "actual target patch reports the isolated owner id");
        Assert(backend.Uninstall("test-complete") == 0 && !backend.IsInstalled,
            "actual target patch uninstalls without leaving held calls");
        Assert(!AuraDirectorReadyToStartDetourBackend.IsOwnedPrefixInstalled(),
            "uninstall removes only the isolated owner prefix");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + message);
        }
        assertions++;
    }

    private sealed class CapturingSink : IAuraDirectorNativeStartHoldSink
    {
        public int AcceptedCount { get; private set; }

        public IAuraDirectorNativeStartHold? LastHold { get; private set; }

        public bool TryAccept(IAuraDirectorNativeStartHold hold)
        {
            AcceptedCount++;
            LastHold = hold;
            return true;
        }
    }

    private sealed class RejectingSink : IAuraDirectorNativeStartHoldSink
    {
        public bool TryAccept(IAuraDirectorNativeStartHold hold)
        {
            return false;
        }
    }

    private sealed class ThrowingSink : IAuraDirectorNativeStartHoldSink
    {
        public bool TryAccept(IAuraDirectorNativeStartHold hold)
        {
            throw new InvalidOperationException("expected test failure");
        }
    }

    private sealed class ImmediateReleaseSink : IAuraDirectorNativeStartHoldSink
    {
        public bool TryAccept(IAuraDirectorNativeStartHold hold)
        {
            hold.TryRelease("immediate");
            return true;
        }
    }

    private sealed class FixtureTarget
    {
        public int OriginalCalls { get; private set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ReadyToStart()
        {
            OriginalCalls++;
        }
    }

    private static class FixturePatch
    {
        public static AuraDirectorOneShotHoldRegistry<FixtureTarget>? Registry { get; set; }

        public static bool Prefix(FixtureTarget __instance)
        {
            return Registry?.Intercept(__instance) ?? true;
        }
    }
}
