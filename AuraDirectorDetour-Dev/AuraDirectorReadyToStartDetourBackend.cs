using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AuraDirector.Shared;
using HarmonyLib;

namespace AuraDirector.Detour;

public sealed class AuraDirectorReadyToStartDetourBackend : IAuraDirectorStartGateProvider, IDisposable
{
    public const string BackendId = "AuraDirector.ReadyToStart.Harmony.v1";
    public const string HarmonyId = "AuraDirector.Shared.ReadyToStart.Harmony.v1";
    public const string ReadyToStartCapabilityV1 = "ReadyToStartGate.V1";
    public const string VerifiedReadyToStartBodySha256V1 = "5BC8DA8FF9659712B6CA63AC833CF23F00414265BC880444849881B097CE9CB6";

    public static IReadOnlyDictionary<string, string> VerifiedMethodCapabilities { get; } =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [VerifiedReadyToStartBodySha256V1] = ReadyToStartCapabilityV1
            });

    private static readonly object ActiveGate = new();
    private static AuraDirectorReadyToStartDetourBackend? active;

    private readonly Harmony harmony = new(HarmonyId);
    private readonly Action<string> log;
    private AuraDirectorOneShotHoldRegistry<FightManager>? registry;
    private MethodInfo? targetMethod;

    public AuraDirectorReadyToStartDetourBackend(Action<string>? log = null)
    {
        this.log = log ?? (_ => { });
    }

    public bool IsInstalled { get; private set; }

    public string ProviderId => BackendId;

    public AuraDirectorCapabilityProbeResult ProbeCapability()
    {
        return Probe();
    }

    public int HeldCount => registry?.HeldCount ?? 0;

    public static bool IsOwnedPrefixInstalled()
    {
        try
        {
            var method = typeof(FightManager).GetMethod(nameof(FightManager.ReadyToStart), Type.EmptyTypes);
            var patchInfo = method == null ? null : Harmony.GetPatchInfo(method);
            return patchInfo?.Prefixes.Any(patch => patch.owner == HarmonyId) == true;
        }
        catch
        {
            return false;
        }
    }

    public static AuraDirectorCapabilityProbeResult Probe()
    {
        try
        {
            return ProbeCore();
        }
        catch (Exception ex)
        {
            return Unsupported("detour-probe-failed", ex.ToString());
        }
    }

    private static AuraDirectorCapabilityProbeResult ProbeCore()
    {
        var method = typeof(FightManager).GetMethod(
            nameof(FightManager.ReadyToStart),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (method == null || method.ReturnType != typeof(void) || method.IsStatic)
        {
            return Unsupported("detour-target-shape-mismatch", "FightManager.ReadyToStart must be a public instance void method with no arguments.");
        }

        var body = method.GetMethodBody()?.GetILAsByteArray();
        if (body == null || body.Length == 0)
        {
            return Unsupported("detour-target-body-unavailable", "FightManager.ReadyToStart has no readable IL body for capability validation.");
        }

        var hash = ComputeSha256(body);
        if (!VerifiedMethodCapabilities.TryGetValue(hash, out var capabilityProfile))
        {
            return Unsupported(
                "detour-target-capability-unverified",
                "FightManager.ReadyToStart method-body SHA-256 is not in the verified capability allowlist: " + hash);
        }

        return new AuraDirectorCapabilityProbeResult
        {
            Supported = true,
            Code = "detour-compatible",
            Detail = "Verified public ReadyToStart target capability " + capabilityProfile + " (method body " + hash + ")",
            Details = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["capabilityProfile"] = capabilityProfile,
                ["methodBodySha256"] = hash,
                ["assemblyLocation"] = method.DeclaringType?.Assembly.Location ?? ""
            }
        };
    }

    public AuraDirectorCapabilityProbeResult Install(IAuraDirectorNativeStartHoldSink sink)
    {
        if (sink == null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        var capability = Probe();
        if (!capability.Supported)
        {
            return capability;
        }

        lock (ActiveGate)
        {
            if (IsInstalled)
            {
                return Supported("detour-already-installed", "The ReadyToStart detour is already installed by this backend instance.");
            }
            if (active != null)
            {
                return Unsupported("detour-owner-conflict", "Another AuraDirector ReadyToStart detour backend is already active.");
            }

            targetMethod = typeof(FightManager).GetMethod(nameof(FightManager.ReadyToStart), Type.EmptyTypes);
            registry = new AuraDirectorOneShotHoldRegistry<FightManager>(
                BackendId,
                sink,
                fightManager => fightManager.ReadyToStart(),
                log);
            active = this;

            try
            {
                var prefix = typeof(AuraDirectorReadyToStartDetourBackend).GetMethod(
                    nameof(Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(Prefix));
                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix) { priority = Priority.First });

                if (!IsOwnedPrefixInstalled())
                {
                    throw new InvalidOperationException("Harmony did not report the expected prefix owner after patching.");
                }

                IsInstalled = true;
                return Supported("detour-installed", "ReadyToStart prefix installed and ownership verified.");
            }
            catch (Exception ex)
            {
                try
                {
                    harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception cleanupException)
                {
                    log("ReadyToStart install cleanup failed open: " + cleanupException);
                }
                active = null;
                registry = null;
                targetMethod = null;
                return Unsupported("detour-install-failed", ex.ToString());
            }
        }
    }

    public int Uninstall(string releaseReason = "backend-uninstall")
    {
        AuraDirectorOneShotHoldRegistry<FightManager>? registryToRelease;
        lock (ActiveGate)
        {
            if (!IsInstalled)
            {
                return 0;
            }
            IsInstalled = false;
            registryToRelease = registry;
        }

        // Release outside ActiveGate because re-entry must pass through Prefix and consume bypass.
        var released = registryToRelease?.StopAndReleaseAll(releaseReason) ?? 0;

        lock (ActiveGate)
        {
            try
            {
                harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                // A stale prefix is still fail-open after active is cleared below.
                log("ReadyToStart unpatch failed open: " + ex);
            }
            finally
            {
                registry = null;
                targetMethod = null;
                if (ReferenceEquals(active, this))
                {
                    active = null;
                }
            }
        }
        return released;
    }

    public void Dispose()
    {
        Uninstall();
    }

    private static bool Prefix(FightManager __instance)
    {
        AuraDirectorReadyToStartDetourBackend? backend;
        lock (ActiveGate)
        {
            backend = active;
        }

        if (backend?.registry == null)
        {
            return true;
        }

        try
        {
            return backend.registry.Intercept(__instance);
        }
        catch (Exception ex)
        {
            backend.log("ReadyToStart prefix failed open: " + ex);
            return true;
        }
    }

    private static AuraDirectorCapabilityProbeResult Supported(string code, string detail)
    {
        return new AuraDirectorCapabilityProbeResult
        {
            Supported = true,
            Code = code,
            Detail = detail
        };
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

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "");
    }
}
