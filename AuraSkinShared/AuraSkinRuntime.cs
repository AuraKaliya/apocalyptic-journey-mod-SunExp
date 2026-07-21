using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using AuraSkin.Shared.Hooks;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Mechanics;
using AuraSkin.Shared.Services;
using UnityEngine;
using Witch.Mod;

namespace AuraSkin.Shared;

public static class AuraSkinRuntime
{
    private const string GlobalObjectName = "AuraSkin.Global";
    private const string ComponentFullName = "AuraSkin.Shared.AuraSkinRuntime+AuraSkinComponent";

    public const string CurrentBuildId = "aura-skin-shared-2026-07-20-v7";
    public const int CurrentProtocolVersion = 7;
    public const int MinimumSupportedProtocolVersion = 7;

    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityErrorsShown = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig, string ownerModId)
    {
        EnsureRuntime(modConfig, ownerModId);
    }

    public static bool RegisterPackage(
        ModConfig modConfig,
        string ownerModId,
        string packageManifestRelativePath = "SharedResources/Skins/package.json")
    {
        var runtime = EnsureRuntime(modConfig, ownerModId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            var packageDirectory = string.IsNullOrWhiteSpace(modConfig.DirectoryName)
                ? ""
                : System.IO.Path.GetFullPath(modConfig.DirectoryName);
            var normalizedPath = AuraSharedPaths.NormalizeRelativePath(packageManifestRelativePath);
            if (string.IsNullOrWhiteSpace(packageDirectory)
                || string.IsNullOrWhiteSpace(normalizedPath)
                || System.IO.Path.IsPathRooted(normalizedPath))
            {
                return false;
            }

            var manifestPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                packageDirectory,
                normalizedPath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!AuraSharedPaths.IsInsideDirectory(manifestPath, packageDirectory))
            {
                SkinLog.Warn("Rejected skin package manifest outside owner directory: " + manifestPath);
                return false;
            }

            var method = runtime.GetType().GetMethod("RegisterPackage", BindingFlags.Instance | BindingFlags.Public);
            return method != null
                   && method.Invoke(runtime, new object[] { ownerModId, manifestPath }) is bool registered
                   && registered;
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Skin package registration failed for " + ownerModId + ": " + AuraSharedReflection.UnwrapMessage(ex));
            return false;
        }
    }

    public static bool Reload(ModConfig modConfig, string ownerModId)
    {
        var runtime = EnsureRuntime(modConfig, ownerModId);
        if (runtime == null)
        {
            return false;
        }

        try
        {
            runtime.GetType().GetMethod("Reload", BindingFlags.Instance | BindingFlags.Public)?.Invoke(runtime, null);
            return true;
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Skin reload failed for " + ownerModId + ": " + AuraSharedReflection.UnwrapMessage(ex));
            return false;
        }
    }

    private static object? EnsureRuntime(ModConfig? modConfig, string ownerModId)
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            var existing = FindRuntimeComponent(gameObject);
            if (existing != null)
            {
                if (!ValidateExistingRuntime(existing, ownerModId))
                {
                    return null;
                }

                if (ReuseLogOwners.Add(ownerModId))
                {
                    SkinLog.Info("Reusing global skin runtime for " + ownerModId
                                 + ", ownerType=" + existing.GetType().Assembly.GetName().Name);
                }

                TryInitializeExisting(existing, modConfig, ownerModId);
                return existing;
            }
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var component = gameObject.AddComponent<AuraSkinComponent>();
        component.InitializeOwner(modConfig, ownerModId);
        SkinLog.Info("Created global skin runtime, owner=" + ownerModId);
        return component;
    }

    private static bool ValidateExistingRuntime(object existing, string ownerModId)
    {
        var type = existing.GetType();
        var protocolVersion = AuraSharedReflection.ReadInt(existing, "ProtocolVersion", 0);
        var minimumSupported = AuraSharedReflection.ReadInt(existing, "MinimumSupportedProtocolVersion", int.MaxValue);
        var buildId = AuraSharedReflection.ReadString(existing, "BuildId");
        var methodsPresent = new[] { "InitializeOwner", "RegisterPackage", "Reload", "GetOwners" }
            .All(name => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null);
        var compatible = protocolVersion >= MinimumSupportedProtocolVersion
                         && minimumSupported <= CurrentProtocolVersion
                         && methodsPresent;

        if (!compatible && CompatibilityErrorsShown.Add(ownerModId + ":" + type.AssemblyQualifiedName))
        {
            SkinLog.Warn("Incompatible global skin runtime; skin features disabled for " + ownerModId
                          + ". existingAssembly=" + type.Assembly.GetName().Name
                          + ", protocol=" + protocolVersion
                          + ", minSupported=" + minimumSupported
                          + ", buildId=" + (string.IsNullOrWhiteSpace(buildId) ? "<missing>" : buildId)
                          + ", localBuildId=" + CurrentBuildId
                          + ", methodsPresent=" + methodsPresent);
        }

        if (compatible
            && !string.IsNullOrWhiteSpace(buildId)
            && !string.Equals(buildId, CurrentBuildId, StringComparison.Ordinal)
            && ReuseLogOwners.Add("build:" + ownerModId + ":" + buildId))
        {
            SkinLog.Warn("Reusing protocol-compatible skin runtime with a different build. owner="
                         + ownerModId + ", existingBuildId=" + buildId
                         + ", localBuildId=" + CurrentBuildId);
        }

        return compatible;
    }

    private static object? FindRuntimeComponent(GameObject gameObject)
    {
        foreach (var component in gameObject.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().FullName == ComponentFullName)
            {
                return component;
            }
        }

        return null;
    }

    private static void TryInitializeExisting(object existing, ModConfig? modConfig, string ownerModId)
    {
        try
        {
            existing.GetType()
                .GetMethod("InitializeOwner", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(existing, new object?[] { modConfig, ownerModId });
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Existing skin runtime initialize failed for " + ownerModId + ": "
                         + AuraSharedReflection.UnwrapMessage(ex));
        }
    }

    public sealed class AuraSkinComponent : MonoBehaviour
    {
        private readonly HashSet<string> owners = new(StringComparer.OrdinalIgnoreCase);
        private bool initialized;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => AuraSkinRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

        public void InitializeOwner(ModConfig? modConfig, string ownerModId)
        {
            SkinPaths.RegisterOwner(modConfig, ownerModId);
            var ownerAdded = false;
            if (!string.IsNullOrWhiteSpace(ownerModId))
            {
                ownerAdded = owners.Add(ownerModId.Trim());
            }

            if (modConfig == null)
            {
                return;
            }

            if (!initialized)
            {
                SkinRuntime.Initialize();
                SkinRuntimeHooks.Initialize(modConfig);
                initialized = true;
                SkinLog.Info("Skin runtime initialized by " + ownerModId);
                return;
            }

            if (ownerAdded)
            {
                SkinRuntime.Reload();
                SkinLog.Info("Skin registry refreshed after owner registration: " + ownerModId);
            }
        }

        public bool RegisterPackage(string ownerModId, string packageManifestPath)
        {
            if (!string.IsNullOrWhiteSpace(ownerModId))
            {
                owners.Add(ownerModId.Trim());
            }

            var result = SkinPackageInstaller.InstallPackage(ownerModId, packageManifestPath);
            if (initialized && result.Success && (result.Activated || result.CatalogChanged || result.Changed))
            {
                SkinRuntime.Reload();
            }

            return result.Success;
        }

        public void Reload()
        {
            if (initialized)
            {
                SkinRuntime.Reload();
            }
        }

        public string[] GetOwners()
        {
            return owners.OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
