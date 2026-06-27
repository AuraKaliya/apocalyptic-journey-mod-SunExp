using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Mechanics;
using AuraSkin.Shared.Models;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;

internal sealed class DamageSettlementCgPreparedClip
{
    public string Key { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string Source { get; set; } = "";

    public string CacheDirectory { get; set; } = "";

    public float FrameSeconds { get; set; } = DamageSettlementCgAnimationSpec.DefaultFrameSeconds;

    public bool Loop { get; set; } = true;

    public List<string> FrameFiles { get; set; } = new();

    public List<Sprite>? LoadedFrames { get; set; }

    public bool OwnsLoadedFrameTextures { get; set; }
}

internal static class DamageSettlementCgAssetCache
{
    private const string CacheSystem = "SettlementCG";
    private static readonly Dictionary<string, DamageSettlementCgPreparedClip> PreparedByKey =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AttemptedKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void BeginAdventure()
    {
        ReleaseLoadedFrames();
        PreparedByKey.Clear();
        AttemptedKeys.Clear();
        AuraToolsLog.Info("[SettlementCG] idle cache reset for adventure.");
    }

    public static void PrepareForTeam(IEnumerable<OutOfRunTeamMemberSnapshot> teamMembers)
    {
        foreach (var member in (teamMembers ?? Array.Empty<OutOfRunTeamMemberSnapshot>())
                 .Where(member => member != null))
        {
            Prepare(member.RoleId, member.PlayerId, member.InstanceId);
        }
    }

    public static void AttachPreparedClipKeys(IEnumerable<DamageSettlementCgEntry> entries)
    {
        foreach (var entry in (entries ?? Array.Empty<DamageSettlementCgEntry>())
                 .Where(entry => entry != null))
        {
            if (TryFindPrepared(entry, out var prepared))
            {
                entry.PreparedClipKey = prepared.Key;
            }
        }
    }

    public static DamageSettlementCgIdleClip? ResolvePreparedClip(DamageSettlementCgEntry entry)
    {
        if (!TryFindPrepared(entry, out var prepared))
        {
            return null;
        }

        if (prepared.LoadedFrames == null)
        {
            prepared.LoadedFrames = LoadFrames(prepared);
            prepared.OwnsLoadedFrameTextures = true;
        }

        return prepared.LoadedFrames.Count == 0
            ? null
            : new DamageSettlementCgIdleClip
            {
                Source = prepared.Source,
                FrameSeconds = prepared.FrameSeconds,
                Loop = prepared.Loop,
                Frames = prepared.LoadedFrames
            };
    }

    private static void Prepare(string roleId, string playerId, string instanceId)
    {
        var normalizedRoleId = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRoleId))
        {
            return;
        }

        var key = ExactKey(normalizedRoleId, playerId, instanceId);
        if (PreparedByKey.ContainsKey(key) || AttemptedKeys.Contains(key))
        {
            return;
        }

        AttemptedKeys.Add(key);
        try
        {
            var prepared = TryPrepareFromFileSystem(normalizedRoleId, playerId, instanceId)
                           ?? TryPrepareFromResourceLoader(normalizedRoleId);
            if (prepared == null)
            {
                AuraToolsLog.Warn("[SettlementCG] preload skipped: role=" + normalizedRoleId + ", reason=no idle source.");
                return;
            }

            RegisterPrepared(key, prepared);
            AuraToolsLog.Info("[SettlementCG] preloaded idle: role="
                              + normalizedRoleId
                              + ", source=" + prepared.Source
                              + ", frames=" + prepared.FrameFiles.Count + ".");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SettlementCG] preload failed: role=" + normalizedRoleId + ", error=" + ex.Message);
        }
    }

    private static void RegisterPrepared(string exactKey, DamageSettlementCgPreparedClip prepared)
    {
        PreparedByKey[exactKey] = prepared;
        if (!string.IsNullOrWhiteSpace(prepared.Key))
        {
            PreparedByKey[prepared.Key] = prepared;
        }

        var roleKey = RoleKey(prepared.RoleId);
        if (!PreparedByKey.ContainsKey(roleKey))
        {
            PreparedByKey[roleKey] = prepared;
        }
    }

    private static DamageSettlementCgPreparedClip? TryPrepareFromFileSystem(
        string roleId,
        string playerId,
        string instanceId)
    {
        var source = TryResolveSelectedSkinSource(roleId, playerId, instanceId)
                     ?? TryResolveCareerFileSource(roleId);
        if (source == null)
        {
            return null;
        }

        return CopySourceToSharedCache(roleId, source);
    }

    private static DamageSettlementCgPreparedClip? TryPrepareFromResourceLoader(string roleId)
    {
        var clip = DamageSettlementCgIdleResolver.TryResolveCareerAnimation(roleId);
        if (clip == null || clip.Frames.Count == 0)
        {
            return null;
        }

        return new DamageSettlementCgPreparedClip
        {
            Key = RoleKey(roleId) + ":memory",
            RoleId = roleId,
            Source = clip.Source,
            FrameSeconds = clip.FrameSeconds,
            Loop = clip.Loop,
            LoadedFrames = clip.Frames,
            FrameFiles = clip.Frames.Select(frame => frame != null ? frame.name : "").ToList()
        };
    }

    private static IdleFileSource? TryResolveSelectedSkinSource(
        string roleId,
        string playerId,
        string instanceId)
    {
        var skin = SelectedSkin(roleId, playerId, instanceId);
        var animationDirectory = skin?.Assets?.Animation ?? "";
        if (string.IsNullOrWhiteSpace(animationDirectory))
        {
            return null;
        }

        var idleDirectory = Path.Combine(animationDirectory, "Idle");
        return Directory.Exists(idleDirectory)
            ? new IdleFileSource(idleDirectory, SkinPaths.ToRawResourcePath(idleDirectory), "skin")
            : null;
    }

    private static SkinDefinition? SelectedSkin(string roleId, string playerId, string instanceId)
    {
        var skin = SkinRuntime.GetSelectedSkin(roleId, playerId);
        if (skin != null)
        {
            return skin;
        }

        if (!string.Equals(playerId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            skin = SkinRuntime.GetSelectedSkin(roleId, instanceId);
            if (skin != null)
            {
                return skin;
            }
        }

        return SkinRuntime.GetSelectedSkin(roleId);
    }

    private static IdleFileSource? TryResolveCareerFileSource(string roleId)
    {
        if (!CareerConfigApi.TryCreate(roleId, out var career) || career == null)
        {
            career = new DataConfig(roleId, DataType.Career);
        }

        var animation = DamageSettlementCgIdleResolver.ReadData(career, "Animation");
        if (string.IsNullOrWhiteSpace(animation))
        {
            return null;
        }

        var idleResource = animation.TrimEnd('/', '\\') + "/Idle";
        return TryResolveResourceDirectory(idleResource, out var idleDirectory)
            ? new IdleFileSource(idleDirectory, idleResource, "career")
            : null;
    }

    private static bool TryResolveResourceDirectory(string resourcePath, out string directory)
    {
        directory = "";
        var normalized = (resourcePath ?? "").Trim().Trim('"').Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        foreach (var candidate in CandidateDirectories(normalized))
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full))
                {
                    directory = full;
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateDirectories(string normalized)
    {
        var systemPath = normalized.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(systemPath))
        {
            yield return systemPath;
            yield break;
        }

        if (AuraSharedPaths.StartsWithSegment(normalized, "Mods") && !string.IsNullOrWhiteSpace(AuraSharedPaths.ModsDirectory))
        {
            var gameData = Directory.GetParent(AuraSharedPaths.ModsDirectory)?.FullName ?? "";
            if (!string.IsNullOrWhiteSpace(gameData))
            {
                yield return Path.Combine(gameData, systemPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(AuraSharedPaths.ModsDirectory))
        {
            yield return Path.Combine(AuraSharedPaths.ModsDirectory, systemPath);
        }
    }

    private static DamageSettlementCgPreparedClip? CopySourceToSharedCache(string roleId, IdleFileSource source)
    {
        var frameFiles = Directory.EnumerateFiles(source.Directory, "*.png", SearchOption.TopDirectoryOnly)
            .ToList();
        if (frameFiles.Count == 0)
        {
            return null;
        }

        var byName = frameFiles.ToDictionary(
            file => Path.GetFileNameWithoutExtension(file),
            StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(source.Directory, "config.json");
        var configJson = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        var spec = DamageSettlementCgAnimationSpec.FromJson(configJson, byName.Keys);
        var orderedFiles = spec.OrderedFrameNames
            .Where(name => byName.ContainsKey(name))
            .Select(name => byName[name])
            .ToList();
        if (orderedFiles.Count == 0)
        {
            return null;
        }

        var hash = HashSource(configJson, orderedFiles);
        var cacheDirectory = Path.Combine(
            AuraSharedPaths.CacheDirectory,
            AuraSharedPaths.SafeSegment(AuraToolsIds.ModId, "AuraToolsExp"),
            CacheSystem,
            "Idle",
            AuraSharedPaths.SafeSegment(roleId, "unknown-role"),
            hash);
        Directory.CreateDirectory(cacheDirectory);

        var copiedFiles = new List<string>();
        foreach (var file in orderedFiles)
        {
            var destination = Path.Combine(cacheDirectory, Path.GetFileName(file));
            File.Copy(file, destination, true);
            copiedFiles.Add(destination);
        }

        File.WriteAllText(Path.Combine(cacheDirectory, "config.json"), configJson);
        WriteManifest(cacheDirectory, roleId, source, hash, copiedFiles, spec);

        return new DamageSettlementCgPreparedClip
        {
            Key = RoleKey(roleId) + ":" + hash,
            RoleId = roleId,
            Source = source.ResourcePath,
            CacheDirectory = cacheDirectory,
            FrameSeconds = spec.FrameSeconds,
            Loop = spec.Loop,
            FrameFiles = copiedFiles
        };
    }

    private static void WriteManifest(
        string cacheDirectory,
        string roleId,
        IdleFileSource source,
        string hash,
        IEnumerable<string> copiedFiles,
        DamageSettlementCgAnimationSpec spec)
    {
        var manifest = new JObject
        {
            ["ownerModId"] = AuraToolsIds.ModId,
            ["system"] = CacheSystem,
            ["roleId"] = roleId,
            ["sourceKind"] = source.Kind,
            ["source"] = source.ResourcePath,
            ["sourceDirectory"] = source.Directory,
            ["hash"] = hash,
            ["frameSeconds"] = spec.FrameSeconds,
            ["loop"] = spec.Loop,
            ["direction"] = spec.Direction,
            ["frames"] = new JArray(copiedFiles.Select(Path.GetFileName))
        };
        File.WriteAllText(Path.Combine(cacheDirectory, "manifest.json"), manifest.ToString());
    }

    private static string HashSource(string configJson, IEnumerable<string> orderedFiles)
    {
        var builder = new StringBuilder();
        builder.AppendLine(configJson ?? "");
        foreach (var file in orderedFiles)
        {
            builder.AppendLine(Path.GetFileName(file));
            builder.AppendLine(Convert.ToBase64String(File.ReadAllBytes(file)));
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
    }

    private static bool TryFindPrepared(DamageSettlementCgEntry entry, out DamageSettlementCgPreparedClip prepared)
    {
        prepared = null!;
        if (entry == null)
        {
            return false;
        }

        foreach (var key in CandidateKeys(entry))
        {
            if (PreparedByKey.TryGetValue(key, out prepared))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateKeys(DamageSettlementCgEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.PreparedClipKey))
        {
            yield return entry.PreparedClipKey;
        }

        var roleId = RoleCatalog.NormalizeRoleId(entry.RoleId);
        if (string.IsNullOrWhiteSpace(roleId))
        {
            yield break;
        }

        yield return ExactKey(roleId, entry.PlayerId, entry.InstanceId);
        yield return ExactKey(roleId, entry.InstanceId, entry.PlayerId);
        yield return RoleKey(roleId);
    }

    private static List<Sprite> LoadFrames(DamageSettlementCgPreparedClip prepared)
    {
        var result = new List<Sprite>();
        foreach (var file in prepared.FrameFiles.Where(File.Exists))
        {
            var sprite = DamageSettlementCgIdleResolver.LoadSpriteFromFile(file);
            if (sprite != null)
            {
                result.Add(sprite);
            }
        }

        return result;
    }

    private static void ReleaseLoadedFrames()
    {
        foreach (var prepared in PreparedByKey.Values.Distinct())
        {
            if (prepared.LoadedFrames == null || !prepared.OwnsLoadedFrameTextures)
            {
                continue;
            }

            foreach (var sprite in prepared.LoadedFrames)
            {
                if (sprite == null)
                {
                    continue;
                }

                var texture = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            prepared.LoadedFrames.Clear();
            prepared.OwnsLoadedFrameTextures = false;
        }
    }

    private static string ExactKey(string roleId, string playerId, string instanceId)
    {
        return RoleKey(roleId)
               + "|player=" + (playerId ?? "").Trim()
               + "|instance=" + (instanceId ?? "").Trim();
    }

    private static string RoleKey(string roleId)
    {
        return "role=" + RoleCatalog.NormalizeRoleId(roleId);
    }

    private sealed class IdleFileSource
    {
        public IdleFileSource(string directory, string resourcePath, string kind)
        {
            Directory = directory;
            ResourcePath = resourcePath;
            Kind = kind;
        }

        public string Directory { get; }

        public string ResourcePath { get; }

        public string Kind { get; }
    }
}
