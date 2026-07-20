using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Witch.Mod;

namespace AuraShared.Core;

public static class AuraSharedResourceProtocol
{
    public const int CurrentSchemaVersion = AuraSharedResourceSchemaVersions.Current;
    public const string DefaultManifestPath = "SharedResources/aura.registration.json";

    public static event Action<string, long>? ScopeChanged;

    public static AuraSharedRegistrationResultV4 RegisterManifest(
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

            var manifestJson = File.ReadAllText(manifestPath);
            var document = JObject.Parse(manifestJson);
            if (document["schemaVersion"]?.Value<int>() != AuraSharedResourceSchemaVersions.Current)
            {
                return new AuraSharedRegistrationResultV4
                {
                    Success = false,
                    Status = AuraSharedRegistrationStatuses.UnsupportedSchema,
                    OwnerModId = ownerModId ?? "",
                    Message = "UnsupportedSchema: v4 registration requires explicit schemaVersion=4."
                };
            }
            var manifest = AuraSharedJson.Deserialize<AuraSharedRegistrationManifestV4>(manifestJson);
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

    public static AuraSharedRegistrationResultV4 Register(
        string ownerModId,
        AuraSharedRegistrationManifestV4 manifest,
        string baseDirectory)
    {
        try
        {
            var responseJson = AuraSharedRuntime.InvokeComponent(
                null,
                ownerModId,
                "RegisterPackageV4Json",
                ownerModId,
                AuraSharedJson.Serialize(manifest),
                baseDirectory) as string;
            var result = string.IsNullOrWhiteSpace(responseJson)
                ? Failed(ownerModId, "Shared v4 registration returned no response.")
                : AuraSharedJson.Deserialize<AuraSharedRegistrationResultV4>(responseJson!)
                  ?? Failed(ownerModId, "Shared v4 registration response is invalid.");
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

    public static AuraSharedResourceResolutionV4 Resolve(string callerId, string relativeOrAbsolutePath)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "ResolveResourceV4Json",
                relativeOrAbsolutePath) as string;
            return string.IsNullOrWhiteSpace(json)
                ? Unregistered(relativeOrAbsolutePath)
                : AuraSharedJson.Deserialize<AuraSharedResourceResolutionV4>(json!) ?? Unregistered(relativeOrAbsolutePath);
        }
        catch
        {
            return Unregistered(relativeOrAbsolutePath);
        }
    }

    public static AuraSharedRegistrationItemResultV4 UpsertManualResource(
        string callerId,
        AuraSharedManualResourceRequestV4 request)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "UpsertManualResourceV4Json",
                callerId,
                AuraSharedJson.Serialize(request)) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedRegistrationItemResultV4 { Message = "Shared v4 manual resource service returned no response." }
                : AuraSharedJson.Deserialize<AuraSharedRegistrationItemResultV4>(json!)
                  ?? new AuraSharedRegistrationItemResultV4 { Message = "Shared v4 manual resource response is invalid." };
        }
        catch (Exception ex)
        {
            return new AuraSharedRegistrationItemResultV4 { Message = ex.Message };
        }
    }

    public static int ActivateLocalPackages(string callerId)
    {
        var value = AuraSharedRuntime.InvokeComponent(null, callerId, "ActivateLocalPackagesV4", callerId);
        return int.TryParse(Convert.ToString(value), out var count) ? count : 0;
    }

    public static string ResolvePath(string callerId, string relativeOrAbsolutePath)
    {
        return Resolve(callerId, relativeOrAbsolutePath).ResolvedPath;
    }

    public static long GetScopeRevision(string callerId, string scopeKey)
    {
        var value = AuraSharedRuntime.InvokeComponent(null, callerId, "GetScopeRevisionV4", scopeKey);
        return long.TryParse(Convert.ToString(value), out var revision) ? revision : 0;
    }

    public static AuraSharedCatalogSnapshotV4 QueryCatalog(string callerId, AuraSharedCatalogQueryV4? query = null)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "QueryCatalogV4Json",
                AuraSharedJson.Serialize(query ?? new AuraSharedCatalogQueryV4())) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedCatalogSnapshotV4()
                : AuraSharedJson.Deserialize<AuraSharedCatalogSnapshotV4>(json!) ?? new AuraSharedCatalogSnapshotV4();
        }
        catch
        {
            return new AuraSharedCatalogSnapshotV4();
        }
    }

    public static AuraSharedEffectiveResolutionV4 ResolveEffective(
        string callerId,
        AuraSharedScopeKey scope,
        AuraSharedLocalOverrideV4? localOverride = null)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "ResolveEffectiveV4Json",
                AuraSharedJson.Serialize(scope),
                localOverride == null ? "" : AuraSharedJson.Serialize(localOverride)) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedEffectiveResolutionV4 { ScopeKey = scope?.Key ?? "", Outcome = "Unavailable", Fallback = "CoreUnavailable" }
                : AuraSharedJson.Deserialize<AuraSharedEffectiveResolutionV4>(json!)
                  ?? new AuraSharedEffectiveResolutionV4 { ScopeKey = scope?.Key ?? "", Outcome = "Unavailable", Fallback = "InvalidResponse" };
        }
        catch (Exception ex)
        {
            return new AuraSharedEffectiveResolutionV4
            {
                ScopeKey = scope?.Key ?? "",
                Outcome = "Unavailable",
                Fallback = "Error:" + ex.Message
            };
        }
    }

    public static AuraSharedUserOverrideDocumentV4 ReadUserOverride(
        string callerId,
        AuraSharedScopeKey scope)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "ReadUserOverrideV4Json",
                AuraSharedJson.Serialize(scope)) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedUserOverrideDocumentV4()
                : AuraSharedJson.Deserialize<AuraSharedUserOverrideDocumentV4>(json!)
                  ?? new AuraSharedUserOverrideDocumentV4();
        }
        catch
        {
            return new AuraSharedUserOverrideDocumentV4();
        }
    }

    public static AuraSharedUserOverrideWriteResultV4 WriteUserOverride(
        string callerId,
        AuraSharedScopeKey scope,
        AuraSharedLocalOverrideV4 localOverride,
        long expectedRevision)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(
                null,
                callerId,
                "WriteUserOverrideV4Json",
                AuraSharedJson.Serialize(scope),
                callerId,
                AuraSharedJson.Serialize(localOverride),
                expectedRevision) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedUserOverrideWriteResultV4 { Message = "Shared v4 override returned no response." }
                : AuraSharedJson.Deserialize<AuraSharedUserOverrideWriteResultV4>(json!)
                  ?? new AuraSharedUserOverrideWriteResultV4 { Message = "Shared v4 override response is invalid." };
        }
        catch (Exception ex)
        {
            return new AuraSharedUserOverrideWriteResultV4 { Message = ex.Message };
        }
    }

    private static AuraSharedResourceResolutionV4 Unregistered(string value)
    {
        var path = Path.IsPathRooted(value ?? "")
            ? Path.GetFullPath(value ?? "")
            : AuraSharedPaths.ResolveSharedPath(value ?? "");
        return new AuraSharedResourceResolutionV4
        {
            Success = false,
            Active = false,
            ResolvedPath = path,
            Outcome = "Unregistered",
            Fallback = "Unregistered"
        };
    }

    private static AuraSharedRegistrationResultV4 Failed(string ownerModId, string message)
    {
        return new AuraSharedRegistrationResultV4
        {
            Success = false,
            OwnerModId = ownerModId ?? "",
            Message = message ?? ""
        };
    }
}
