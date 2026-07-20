using System;
using System.Collections.Generic;
using System.IO;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedResourceProtocol
{
    public const int CurrentSchemaVersion = 3;
    public const string DefaultManifestPath = "SharedResources/aura.registration.json";

    public static event Action<string, long>? ScopeChanged;

    public static AuraSharedRegistrationResultV3 RegisterManifest(
        ModConfig modConfig,
        string ownerModId,
        string manifestRelativePath = DefaultManifestPath)
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        try
        {
            var modRoot = Path.GetFullPath(modConfig.DirectoryName);
            var manifestPath = Path.GetFullPath(Path.Combine(
                modRoot,
                AuraSharedPaths.NormalizeRelativePath(manifestRelativePath)
                    .Replace('/', Path.DirectorySeparatorChar)));
            if (!AuraSharedPaths.IsInsideDirectory(manifestPath, modRoot) || !File.Exists(manifestPath))
            {
                return Failed(ownerModId, "Registration manifest is missing or outside its Mod: " + manifestPath);
            }

            var manifest = AuraSharedJson.Deserialize<AuraSharedRegistrationManifestV3>(File.ReadAllText(manifestPath));
            if (manifest == null)
            {
                return Failed(ownerModId, "Registration manifest JSON is invalid: " + manifestPath);
            }

            return Register(ownerModId, manifest, Path.GetDirectoryName(manifestPath) ?? modRoot);
        }
        catch (Exception ex)
        {
            return Failed(ownerModId, ex.Message);
        }
    }

    public static AuraSharedRegistrationResultV3 Register(
        string ownerModId,
        AuraSharedRegistrationManifestV3 manifest,
        string baseDirectory)
    {
        try
        {
            var responseJson = AuraSharedRuntime.InvokeComponent(
                null,
                ownerModId,
                "RegisterPackageV3Json",
                ownerModId,
                AuraSharedJson.Serialize(manifest),
                baseDirectory) as string;
            var result = string.IsNullOrWhiteSpace(responseJson)
                ? Failed(ownerModId, "Shared v3 registration returned no response.")
                : AuraSharedJson.Deserialize<AuraSharedRegistrationResultV3>(responseJson!)
                  ?? Failed(ownerModId, "Shared v3 registration response is invalid.");
            foreach (var scopeKey in result.ChangedScopeKeys ?? new List<string>())
            {
                try
                {
                    ScopeChanged?.Invoke(scopeKey, result.Revision);
                }
                catch
                {
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            return Failed(ownerModId, ex.Message);
        }
    }

    public static AuraSharedResourceResolutionV3 Resolve(string callerId, string relativeOrAbsolutePath)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "ResolveResourceV3Json",
                relativeOrAbsolutePath) as string;
            return string.IsNullOrWhiteSpace(json)
                ? Direct(relativeOrAbsolutePath)
                : AuraSharedJson.Deserialize<AuraSharedResourceResolutionV3>(json!) ?? Direct(relativeOrAbsolutePath);
        }
        catch
        {
            return Direct(relativeOrAbsolutePath);
        }
    }

    public static string ResolvePath(string callerId, string relativeOrAbsolutePath)
    {
        return Resolve(callerId, relativeOrAbsolutePath).ResolvedPath;
    }

    public static long GetScopeRevision(string callerId, string scopeKey)
    {
        var value = AuraSharedRuntime.InvokeComponent(null, callerId, "GetScopeRevisionV3", scopeKey);
        return long.TryParse(Convert.ToString(value), out var revision) ? revision : 0;
    }

    public static AuraSharedCatalogSnapshotV3 QueryCatalog(string callerId, AuraSharedCatalogQueryV3? query = null)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "QueryCatalogV3Json",
                AuraSharedJson.Serialize(query ?? new AuraSharedCatalogQueryV3())) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedCatalogSnapshotV3()
                : AuraSharedJson.Deserialize<AuraSharedCatalogSnapshotV3>(json!) ?? new AuraSharedCatalogSnapshotV3();
        }
        catch
        {
            return new AuraSharedCatalogSnapshotV3();
        }
    }

    public static AuraSharedEffectiveResolutionV3 ResolveEffective(
        string callerId,
        AuraSharedScopeKey scope,
        AuraSharedLocalOverrideV3? localOverride = null)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "ResolveEffectiveV3Json",
                AuraSharedJson.Serialize(scope),
                localOverride == null ? "" : AuraSharedJson.Serialize(localOverride)) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedEffectiveResolutionV3 { ScopeKey = scope?.Key ?? "", Outcome = "Unavailable", Fallback = "CoreUnavailable" }
                : AuraSharedJson.Deserialize<AuraSharedEffectiveResolutionV3>(json!)
                  ?? new AuraSharedEffectiveResolutionV3 { ScopeKey = scope?.Key ?? "", Outcome = "Unavailable", Fallback = "InvalidResponse" };
        }
        catch (Exception ex)
        {
            return new AuraSharedEffectiveResolutionV3
            {
                ScopeKey = scope?.Key ?? "",
                Outcome = "Unavailable",
                Fallback = "Error:" + ex.Message
            };
        }
    }

    public static AuraSharedUserOverrideDocumentV3 ReadUserOverride(
        string callerId,
        AuraSharedScopeKey scope)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "ReadUserOverrideV3Json",
                AuraSharedJson.Serialize(scope)) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedUserOverrideDocumentV3()
                : AuraSharedJson.Deserialize<AuraSharedUserOverrideDocumentV3>(json!)
                  ?? new AuraSharedUserOverrideDocumentV3();
        }
        catch
        {
            return new AuraSharedUserOverrideDocumentV3();
        }
    }

    public static AuraSharedUserOverrideWriteResultV3 WriteUserOverride(
        string callerId,
        AuraSharedScopeKey scope,
        AuraSharedLocalOverrideV3 localOverride,
        long expectedRevision)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "WriteUserOverrideV3Json",
                AuraSharedJson.Serialize(scope),
                callerId,
                AuraSharedJson.Serialize(localOverride),
                expectedRevision) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedUserOverrideWriteResultV3 { Message = "Shared v3 override returned no response." }
                : AuraSharedJson.Deserialize<AuraSharedUserOverrideWriteResultV3>(json!)
                  ?? new AuraSharedUserOverrideWriteResultV3 { Message = "Shared v3 override response is invalid." };
        }
        catch (Exception ex)
        {
            return new AuraSharedUserOverrideWriteResultV3 { Message = ex.Message };
        }
    }

    private static AuraSharedResourceResolutionV3 Direct(string value)
    {
        var path = Path.IsPathRooted(value ?? "")
            ? Path.GetFullPath(value ?? "")
            : AuraSharedPaths.ResolveSharedPath(value ?? "");
        var found = File.Exists(path) || Directory.Exists(path);
        return new AuraSharedResourceResolutionV3
        {
            Success = found,
            Active = false,
            UsedLegacyPath = true,
            ResolvedPath = path,
            Outcome = found ? "DirectCompatibility" : "Missing",
            Fallback = "Unregistered"
        };
    }

    private static AuraSharedRegistrationResultV3 Failed(string ownerModId, string message)
    {
        return new AuraSharedRegistrationResultV3
        {
            Success = false,
            OwnerModId = ownerModId ?? "",
            Message = message ?? ""
        };
    }
}
