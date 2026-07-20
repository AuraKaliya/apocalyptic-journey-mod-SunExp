using System;
using System.IO;

namespace AuraShared.Core;

public static class AuraSharedResourcePathPolicy
{
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
        return Join(ResourceDirectory(scope, ownerModId, resourceId), "aura.resource.json");
    }

    public static string ResourceStatePath(
        AuraSharedScopeKey scope,
        string ownerModId,
        string resourceId)
    {
        return Join(ResourceDirectory(scope, ownerModId, resourceId), "aura.state.json");
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
