using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AuraShared.Core;

public static class AuraSharedResourcePathPolicy
{
    private const int ReadableResourceDirectoryLimit = 96;
    private const int ReadableAbsoluteResourceDirectoryLimit = 200;

    public static string RootManifestPath()
    {
        return "aura.shared.json";
    }

    public static string FeatureDirectory(AuraSharedScopeKey scope)
    {
        scope ??= new AuraSharedScopeKey();
        scope.Normalize();
        return Join(scope.ModuleId, scope.ScopeType, scope.ScopeId, scope.FeatureId);
    }

    public static string ProviderDirectory(AuraSharedScopeKey scope, string ownerModId)
    {
        return Join(FeatureDirectory(scope), Segment(ownerModId, "UnknownOwner"));
    }

    public static string ResourceDirectory(
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        return Join(ProviderDirectory(scope, ownerModId), Segment(resourceId, "resource"));
    }

    public static string ResourcePath(
        AuraSharedScopeKey scope,
        string ownerModId,
        AuraSharedResourceDeclarationV4 declaration)
    {
        var directory = ResourceDirectory(scope, ownerModId, declaration?.ResourceId ?? "resource");
        if (declaration == null
            || string.Equals(declaration.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase))
        {
            return Join(directory, "content");
        }

        var fileName = declaration.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || string.Equals(fileName, "content", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(declaration.Source);
            fileName = "content" + (string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant());
        }

        return Join(directory, Segment(fileName, "content.bin"));
    }

    public static string StorageResourceDirectory(
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        var logical = ResourceDirectory(scope, ownerModId, resourceId);
        if (logical.Length <= ReadableResourceDirectoryLimit)
        {
            return logical;
        }

        return CompactResourceDirectory(scope, ownerModId, resourceId);
    }

    public static string StorageResourceDirectory(
        string rootDirectory,
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        var logical = ResourceDirectory(scope, ownerModId, resourceId);
        var absolute = Path.GetFullPath(Path.Combine(
            rootDirectory ?? "",
            logical.Replace('/', Path.DirectorySeparatorChar)));
        if (logical.Length <= ReadableResourceDirectoryLimit
            && absolute.Length <= ReadableAbsoluteResourceDirectoryLimit)
        {
            return logical;
        }

        return CompactResourceDirectory(scope, ownerModId, resourceId);
    }

    private static string CompactResourceDirectory(
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {

        scope ??= new AuraSharedScopeKey();
        scope.Normalize();
        var identity = scope.Key + "\n" + (ownerModId ?? "").Trim() + "\n" + (resourceId ?? "").Trim();
        var hash = StableHash(identity);
        return Join(scope.ModuleId, "_Store", hash.Substring(0, 2), hash.Substring(2, 30));
    }

    public static string StorageResourcePath(
        AuraSharedScopeKey scope,
        string ownerModId,
        AuraSharedResourceDeclarationV4 declaration)
    {
        var directory = StorageResourceDirectory(scope, ownerModId, declaration?.ResourceId ?? "resource");
        if (declaration == null
            || string.Equals(declaration.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase))
        {
            return Join(directory, "content");
        }

        var fileName = declaration.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || string.Equals(fileName, "content", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(declaration.Source);
            fileName = "content" + (string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant());
        }

        return Join(directory, Segment(fileName, "content.bin"));
    }

    public static string StorageResourcePath(
        string rootDirectory,
        AuraSharedScopeKey scope,
        string ownerModId,
        AuraSharedResourceDeclarationV4 declaration)
    {
        var directory = StorageResourceDirectory(
            rootDirectory,
            scope,
            ownerModId,
            declaration?.ResourceId ?? "resource");
        if (declaration == null
            || string.Equals(declaration.Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase))
        {
            return Join(directory, "content");
        }

        var fileName = declaration.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || string.Equals(fileName, "content", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(declaration.Source);
            fileName = "content" + (string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant());
        }

        return Join(directory, Segment(fileName, "content.bin"));
    }

    public static string ModuleManifestPath(string moduleId)
    {
        return Join(Segment(moduleId, "General"), "aura.module.json");
    }

    public static string ScopeTypeManifestPath(string moduleId, string scopeType)
    {
        return Join(Segment(moduleId, "General"), Segment(scopeType, "Global"), "aura.scope-type.json");
    }

    public static string ScopeManifestPath(AuraSharedScopeKey scope)
    {
        scope ??= new AuraSharedScopeKey();
        scope.Normalize();
        return Join(scope.ModuleId, scope.ScopeType, scope.ScopeId, "aura.scope.json");
    }

    public static string FeatureManifestPath(AuraSharedScopeKey scope)
    {
        return Join(FeatureDirectory(scope), "aura.feature.json");
    }

    public static string UserOverridePath(AuraSharedScopeKey scope)
    {
        return Join(FeatureDirectory(scope), "aura.user.json");
    }

    public static string ProviderDefaultsPath(AuraSharedScopeKey scope, string ownerModId)
    {
        return Join(ProviderDirectory(scope, ownerModId), "aura.defaults.json");
    }

    public static string ProviderManifestPath(AuraSharedScopeKey scope, string ownerModId)
    {
        return Join(ProviderDirectory(scope, ownerModId), "aura.provider.json");
    }

    public static string ResourceManifestPath(
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        return Join(StorageResourceDirectory(scope, ownerModId, resourceId), "aura.resource.json");
    }

    public static string ResourceManifestPath(
        string rootDirectory,
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        return Join(StorageResourceDirectory(rootDirectory, scope, ownerModId, resourceId), "aura.resource.json");
    }

    public static string ResourceStatePath(
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        return Join(StorageResourceDirectory(scope, ownerModId, resourceId), "aura.state.json");
    }

    public static string ResourceStatePath(
        string rootDirectory,
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        return Join(StorageResourceDirectory(rootDirectory, scope, ownerModId, resourceId), "aura.state.json");
    }

    private static string StableHash(string value)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var item in bytes)
        {
            builder.Append(item.ToString("x2"));
        }
        return builder.ToString();
    }

    private static string Join(params string[] segments)
    {
        return string.Join("/", segments).Replace('\\', '/').Trim('/');
    }

    private static string Segment(string value, string fallback)
    {
        return AuraSharedPaths.SafeSegment((value ?? "").Trim(), fallback);
    }
}
