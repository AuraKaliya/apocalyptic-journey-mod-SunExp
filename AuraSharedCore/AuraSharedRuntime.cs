using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedRuntime
{
    private const string GlobalObjectName = "AuraShared.Global";
    private const string ComponentFullName = "AuraShared.Core.AuraSharedRuntime+AuraSharedComponent";

    public const string CurrentBuildId = "aura-shared-core-2026-06-22-v2";
    public const int CurrentProtocolVersion = 2;
    public const int MinimumSupportedProtocolVersion = 2;

    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityErrorsShown = new(StringComparer.OrdinalIgnoreCase);

    public static string Initialize(ModConfig? modConfig, string ownerModId, AuraSharedOptions? options = null)
    {
        AuraSharedPaths.Initialize(modConfig, options);
        EnsureCore(modConfig, ownerModId, options);
        return AuraSharedPaths.RootDirectory;
    }

    public static string RootDirectory(ModConfig? modConfig, string ownerModId)
    {
        Initialize(modConfig, ownerModId);
        return AuraSharedPaths.RootDirectory;
    }

    internal static object? InvokeComponent(ModConfig? modConfig, string ownerModId, string methodName, params object?[] args)
    {
        var component = EnsureCore(modConfig, ownerModId, null);
        if (component == null)
        {
            return null;
        }

        try
        {
            return component.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(component, args);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AuraShared] Component call failed: " + methodName + " -> " + AuraSharedReflection.UnwrapMessage(ex));
            return null;
        }
    }

    private static object? EnsureCore(ModConfig? modConfig, string ownerModId, AuraSharedOptions? options)
    {
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            var existing = FindCoreComponent(gameObject);
            if (existing != null)
            {
                if (!ValidateExistingCore(existing, ownerModId))
                {
                    return null;
                }

                if (ReuseLogOwners.Add(ownerModId))
                {
                    Debug.Log("[AuraShared] Reusing global core for " + ownerModId
                              + ", ownerType=" + existing.GetType().Assembly.GetName().Name
                              + ", root=" + AuraSharedPaths.RootDirectory);
                }

                TryInitializeExisting(existing, modConfig, ownerModId, options);
                return existing;
            }
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var component = gameObject.AddComponent<AuraSharedComponent>();
        component.InitializeOwner(modConfig, ownerModId, options);
        Debug.Log("[AuraShared] Created global core, owner=" + ownerModId + ", root=" + AuraSharedPaths.RootDirectory);
        return component;
    }

    private static bool ValidateExistingCore(object existing, string ownerModId)
    {
        var type = existing.GetType();
        var protocolVersion = AuraSharedReflection.ReadInt(existing, "ProtocolVersion", 0);
        var minimumSupported = AuraSharedReflection.ReadInt(existing, "MinimumSupportedProtocolVersion", int.MaxValue);
        var buildId = AuraSharedReflection.ReadString(existing, "BuildId");
        var methodsPresent = new[]
            {
                "InitializeOwner",
                "RegisterResource",
                "RegisterManifestPath",
                "RegisterManifestJson",
                "GetResourcesJson",
                "ReadStorageJson",
                "WriteStorageJson",
                "InstallResourceJson",
                "GetInstalledResourcesJson",
                "GetChangesJson",
                "GetOwners"
            }
            .All(name => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null);
        var compatible = protocolVersion >= MinimumSupportedProtocolVersion
                         && minimumSupported <= CurrentProtocolVersion
                         && methodsPresent;

        if (!compatible && CompatibilityErrorsShown.Add(ownerModId + ":" + type.AssemblyQualifiedName))
        {
            Debug.LogError("[AuraShared] Incompatible global core; shared systems disabled for " + ownerModId
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
            Debug.LogWarning("[AuraShared] Reusing protocol-compatible core with a different build. owner="
                             + ownerModId + ", existingBuildId=" + buildId
                             + ", localBuildId=" + CurrentBuildId);
        }

        return compatible;
    }

    private static object? FindCoreComponent(GameObject gameObject)
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

    private static void TryInitializeExisting(object existing, ModConfig? modConfig, string ownerModId, AuraSharedOptions? options)
    {
        try
        {
            existing.GetType()
                .GetMethod("InitializeOwner", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(existing, new object?[] { modConfig, ownerModId, options });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AuraShared] Existing core initialize failed for " + ownerModId + ": " + AuraSharedReflection.UnwrapMessage(ex));
        }
    }

    public sealed class AuraSharedComponent : MonoBehaviour
    {
        private readonly HashSet<string> owners = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AuraSharedResourceRecord> resources = new(StringComparer.OrdinalIgnoreCase);
        private readonly object resourceGate = new();
        private readonly object changeGate = new();
        private readonly List<AuraSharedChangeRecord> changes = new();
        private long changeSequence;
        private string rootDirectory = "";
        private AuraSharedStorageCoordinator? storage;
        private AuraSharedPackageCoordinator? packages;
        private bool recoveredTransactions;
        private string ensuredStandardDirectoryRoot = "";

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => AuraSharedRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

        public string RootDirectory => rootDirectory;

        public void InitializeOwner(ModConfig? modConfig, string ownerModId, object? options = null)
        {
            var typedOptions = options as AuraSharedOptions ?? new AuraSharedOptions();
            rootDirectory = AuraSharedPaths.Initialize(modConfig, typedOptions);
            if (storage == null)
            {
                storage = new AuraSharedStorageCoordinator(rootDirectory);
                packages = new AuraSharedPackageCoordinator(storage);
            }
            else
            {
                storage.InitializeRoot(rootDirectory);
            }

            if (!recoveredTransactions && packages != null)
            {
                var recovered = packages.RecoverTransactions();
                recoveredTransactions = true;
                if (recovered > 0)
                {
                    Debug.LogWarning("[AuraShared] Recovered " + recovered + " interrupted shared transaction(s).");
                }
            }
            if (!string.IsNullOrWhiteSpace(ownerModId))
            {
                var addedOwner = false;
                lock (resourceGate)
                {
                    addedOwner = owners.Add(ownerModId.Trim());
                }

                if (addedOwner)
                {
                    Debug.Log("[AuraShared] Owner initialized: " + ownerModId + ", root=" + rootDirectory);
                }
            }

            if (!string.Equals(ensuredStandardDirectoryRoot, rootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                AuraSharedPaths.EnsureStandardDirectories();
                ensuredStandardDirectoryRoot = rootDirectory;
            }
        }

        public string[] GetOwners()
        {
            lock (resourceGate)
            {
                return owners.OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        public bool RegisterManifestPath(object? ownerModId, object? manifestPath, object? baseDirectory)
        {
            var owner = Convert.ToString(ownerModId)?.Trim() ?? "";
            var path = Convert.ToString(manifestPath)?.Trim() ?? "";
            var root = Convert.ToString(baseDirectory)?.Trim() ?? "";
            return AuraSharedRegistry.RegisterManifestPathNoComponent(owner, path, root, RegisterResource);
        }

        public bool RegisterManifestJson(object? ownerModId, object? manifestJson, object? baseDirectory)
        {
            var owner = Convert.ToString(ownerModId)?.Trim() ?? "";
            var json = Convert.ToString(manifestJson) ?? "";
            var root = Convert.ToString(baseDirectory)?.Trim() ?? "";
            return AuraSharedRegistry.RegisterManifestJsonNoComponent(owner, json, root, RegisterResource);
        }

        public bool RegisterResource(object? value)
        {
            var record = AuraSharedResourceRecord.FromObject(value);
            if (record == null || !record.Enabled)
            {
                return false;
            }

            record.Normalize();
            if (string.IsNullOrWhiteSpace(record.System) || string.IsNullOrWhiteSpace(record.ResourceId))
            {
                Debug.LogWarning("[AuraShared] Resource registration skipped: system/resourceId is empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.OwnerModId))
            {
                record.OwnerModId = "UnknownOwner";
            }

            lock (resourceGate)
            {
                if (resources.TryGetValue(record.UniqueKey, out var existing))
                {
                    var samePath = string.IsNullOrWhiteSpace(existing.AbsolutePath) && string.IsNullOrWhiteSpace(record.AbsolutePath)
                                   || AuraSharedPaths.IsSamePath(existing.AbsolutePath, record.AbsolutePath);
                    var sameResource = string.Equals(existing.Kind, record.Kind, StringComparison.OrdinalIgnoreCase) && samePath;
                    var sameOwner = existing.SourceOwners.Contains(record.OwnerModId, StringComparer.OrdinalIgnoreCase);
                    if (!sameResource && !sameOwner)
                    {
                        Debug.LogError("[AuraShared] Conflicting shared resource rejected: " + record.UniqueKey
                                       + ", existing=" + existing.AbsolutePath
                                       + ", incoming=" + record.AbsolutePath);
                        return false;
                    }

                    record.SourceOwners = existing.SourceOwners
                        .Concat(record.SourceOwners)
                        .Where(owner => !string.IsNullOrWhiteSpace(owner))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                resources[record.UniqueKey] = record;
                owners.Add(record.OwnerModId);
            }
            Debug.Log("[AuraShared] Resource registered: " + record.UniqueKey);
            return true;
        }

        public string GetResourcesJson(object? system)
        {
            var systemName = Convert.ToString(system)?.Trim() ?? "";
            lock (resourceGate)
            {
                var records = resources.Values
                    .Where(record => string.IsNullOrWhiteSpace(systemName)
                                     || string.Equals(record.System, systemName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(record => record.Priority)
                    .ThenBy(record => record.OwnerModId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(record => record.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return AuraSharedJson.Serialize(records);
            }
        }

        public string ReadStorageJson(object? requestJson)
        {
            var request = DeserializeRequest<AuraSharedStorageRequest>(requestJson);
            return AuraSharedJson.Serialize(request == null || storage == null
                ? new AuraSharedStorageResponse { Success = false, Message = "Shared storage is unavailable." }
                : storage.Read(request));
        }

        public string WriteStorageJson(object? requestJson)
        {
            var request = DeserializeRequest<AuraSharedStorageRequest>(requestJson);
            var response = request == null || storage == null
                ? new AuraSharedStorageResponse { Success = false, Message = "Shared storage is unavailable." }
                : storage.Write(request);
            if (response.Success && request != null)
            {
                PublishChange("Config", request.System, request.FileName, response.Revision);
            }
            return AuraSharedJson.Serialize(response);
        }

        public string InstallResourceJson(object? requestJson)
        {
            var request = DeserializeRequest<AuraSharedInstallRequest>(requestJson);
            var response = request == null || packages == null
                ? new AuraSharedInstallResponse { Success = false, Message = "Shared package engine is unavailable." }
                : packages.Install(request);
            if (response.Success && response.Changed && request != null)
            {
                PublishChange("Resource", request.System, request.LogicalId, 0);
            }
            return AuraSharedJson.Serialize(response);
        }

        public string GetInstalledResourcesJson(object? system)
        {
            var systemName = Convert.ToString(system)?.Trim() ?? "";
            return AuraSharedJson.Serialize(packages == null || string.IsNullOrWhiteSpace(systemName)
                ? Array.Empty<AuraSharedInstalledResource>()
                : packages.GetResources(systemName));
        }

        public string GetChangesJson(object? sinceSequence)
        {
            var since = 0L;
            long.TryParse(Convert.ToString(sinceSequence), out since);
            lock (changeGate)
            {
                return AuraSharedJson.Serialize(new AuraSharedChangeFeed
                {
                    LatestSequence = changeSequence,
                    Changes = changes.Where(change => change.Sequence > since).ToArray()
                });
            }
        }

        private void PublishChange(string kind, string system, string logicalId, long revision)
        {
            lock (changeGate)
            {
                changes.Add(new AuraSharedChangeRecord
                {
                    Sequence = ++changeSequence,
                    Kind = kind,
                    System = system ?? "",
                    LogicalId = logicalId ?? "",
                    Revision = revision,
                    ChangedUtc = DateTime.UtcNow.ToString("O")
                });
                if (changes.Count > 256)
                {
                    changes.RemoveRange(0, changes.Count - 256);
                }
            }
        }

        private static T? DeserializeRequest<T>(object? requestJson) where T : class
        {
            try
            {
                return AuraSharedJson.Deserialize<T>(Convert.ToString(requestJson) ?? "");
            }
            catch
            {
                return null;
            }
        }

        private void OnDestroy()
        {
            storage?.Dispose();
            storage = null;
            packages = null;
        }
    }
}
