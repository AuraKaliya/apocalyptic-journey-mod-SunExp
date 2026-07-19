using System;
using System.Collections.Generic;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsConfiguredResourceResolver
{
    private static readonly AuraSharedResourceAlias SkillCgDirectoryAlias = new(
        "CG/Roles/",
        "CG/AuraToolsExp/Roles/");

    private static readonly AuraSharedResourceAlias CommonAudioDirectoryAlias = new(
        "Audio/Common/",
        "Audio/AuraToolsExp/Common/");

    private static readonly AuraSharedResourceAlias RoleAudioDirectoryAlias = new(
        "Audio/Roles/",
        "Audio/AuraToolsExp/Roles/");

    public static string ResolveSkillCgPath(string? resource)
    {
        return ResolveExistingPath(resource, SkillCgDirectoryAlias);
    }

    public static string ResolveAudioPath(string? resource)
    {
        return ResolveExistingPath(resource, CommonAudioDirectoryAlias, RoleAudioDirectoryAlias);
    }

    public static string ResolveExistingPath(
        string? resource,
        params AuraSharedResourceAlias[] aliases)
    {
        string first = "";
        var candidates = AuraSharedResourceReference.BuildCandidates(resource, aliases);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var path = AuraToolsConfigService.ResolveConfiguredPath(candidate);
            if (first.Length == 0)
            {
                first = path;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                if (index > 0)
                {
                    AuraToolsLog.DebugOnce(
                        "resource-compat:" + (resource ?? "") + ":" + candidate,
                        "[ResourceCompat] resolved legacy alias: declared="
                        + (resource ?? "") + ", resolved=" + candidate + ", path=" + path);
                }

                return path;
            }
        }

        return first;
    }

    public static IReadOnlyList<string> SkillCgCandidates(string? resource)
    {
        return AuraSharedResourceReference.BuildCandidates(resource, SkillCgDirectoryAlias);
    }

    public static IReadOnlyList<string> AudioCandidates(string? resource)
    {
        return AuraSharedResourceReference.BuildCandidates(resource, CommonAudioDirectoryAlias, RoleAudioDirectoryAlias);
    }
}
