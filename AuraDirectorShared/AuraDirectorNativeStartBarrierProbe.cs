using System;
using System.Linq;
using System.Reflection;
using Witch.Core;

namespace AuraDirector.Shared;

public static class AuraDirectorNativeStartBarrierProbe
{
    public const string UnsupportedHookCode = "native-hook-not-cancellable";

    private static readonly string[] CancellationMembers =
    {
        "CancelOriginal",
        "SkipOriginal",
        "SuppressOriginal",
        "MethodContext",
        "ReplaceReturnValue"
    };

    public static AuraDirectorCapabilityProbeResult Probe()
    {
        var readyMethod = typeof(FightManager).GetMethod("ReadyToStart", BindingFlags.Instance | BindingFlags.Public);
        if (readyMethod == null)
        {
            return Unsupported("ready-to-start-missing", "FightManager.ReadyToStart was not found.");
        }

        var contextType = typeof(ModHookContext);
        var exposed = contextType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Select(member => member.Name)
            .ToArray();
        var cancellationSurface = CancellationMembers.FirstOrDefault(candidate =>
            exposed.Any(name => string.Equals(name, candidate, StringComparison.Ordinal)));
        if (cancellationSurface == null)
        {
            return Unsupported(
                UnsupportedHookCode,
                "ModHookContext exposes only observational hook data; no supported original-call cancellation member is present.");
        }

        return Unsupported(
            "native-hook-cancellation-unverified",
            "A potential cancellation member was found (" + cancellationSurface + "), but no validated AuraDirector adapter exists for it.");
    }

    private static AuraDirectorCapabilityProbeResult Unsupported(string code, string detail)
    {
        return new AuraDirectorCapabilityProbeResult
        {
            Supported = false,
            Code = code,
            Detail = detail
        };
    }
}
