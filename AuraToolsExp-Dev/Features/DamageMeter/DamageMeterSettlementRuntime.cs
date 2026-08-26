using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterSettlementRuntime
{
    private static readonly Dictionary<string, byte[]> AvatarPngCache = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly List<OutOfRunTeamMemberSnapshot> AdventureTeamMembers = new();
    private const int MaxAvatarCacheEntries = 32;
    private static bool adventureSettlementRecorded;
    private static bool adventureHistoryRestoreAttempted;
    private static bool historyStorageReady;

    internal static void BeginAdventure()
    {
        adventureSettlementRecorded = false;
        adventureHistoryRestoreAttempted = false;
    }

    internal static void RestoreAdventureHistoryOnce()
    {
        if (adventureHistoryRestoreAttempted)
        {
            return;
        }

        adventureHistoryRestoreAttempted = true;
        DamageMeterNetworkRuntime.RestoreAdventureHistory();
    }

    internal static void OnAdventureSettlement(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("adventure settlement", () =>
        {
            var source = DamageMeterAvailabilityRuntime.GetHookName(context);
            EnsureOutOfRunHistoryLoaded("adventure settlement:" + source);
            if (adventureSettlementRecorded)
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: already archived. source=" + source + ".");
                return;
            }

            if (!DamageMeterNetworkRuntime.IsHost)
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: not host. source=" + source + ".");
                return;
            }

            if (!DamageMeterAvailabilityRuntime.IsSupportedDamageMeterAdventureContext())
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: unsupported context. source=" + source + ".");
                return;
            }

            var mode = ResolvePlayMode();
            var completed = IsCurrentAdventureCompleted(mode.Id);
            ArchiveActiveFightForSettlement(completed);
            var aggregate = AuraToolsDamageMeterRuntime.RunAggregate.CreateSnapshot();
            if (!AuraToolsDamageMeterRuntime.RunAggregate.HasDamage && AuraToolsDamageMeterRuntime.History.TotalCount == 0)
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: no fight history. source=" + source + ".");
                return;
            }

            adventureSettlementRecorded = true;
            var request = new OutOfRunDamageHistoryBuildRequest
            {
                AdventureId = DamageMeterNetworkRuntime.CurrentAdventureId,
                ModeId = mode.Id,
                ModeDisplayName = mode.DisplayName,
                Status = completed
                    ? OutOfRunDamageHistoryStatus.Completed
                    : OutOfRunDamageHistoryStatus.Failed,
                EndedUtc = DateTime.UtcNow.ToString("O"),
                TeamMembers = CollectSettlementTeamMembers(AuraToolsConfigService.MatchExperience.DamageMeter.CaptureTeamAvatars)
            };
            var record = AuraToolsDamageMeterRuntime.RunAggregate.HasDamage
                ? OutOfRunDamageHistoryBuilder.Build(aggregate, request, countShield: true)
                : OutOfRunDamageHistoryBuilder.Build(AuraToolsDamageMeterRuntime.History.Records, request, countShield: true);
            if (DamageHistoryStorage.Database.AppendAdventure(record))
            {
                AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
                AuraToolsLog.Info("[DamageMeter] out-of-run history archived. mode="
                                  + mode.Id + ", status=" + record.Status + ", source=" + source + ".");
            }
            else
            {
                AuraToolsLog.Info("[DamageMeter] out-of-run history skipped: duplicate adventure id. source=" + source + ".");
            }
        });
    }

    public static int OutOfRunHistoryCount
    {
        get
        {
            EnsureOutOfRunHistoryLoaded("history count");
            try
            {
                return DamageHistoryStorage.Database.CountAdventures();
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[DamageMeter] out-of-run history count failed: " + ex.Message);
                return 0;
            }
        }
    }

    public static void OpenOutOfRunHistory()
    {
        EnsureOutOfRunHistoryLoaded("open history");
        OutOfRunDamageHistoryPresenter.Show();
    }

    public static void ClearOutOfRunHistory()
    {
        EnsureOutOfRunHistoryLoaded("clear history");
        try
        {
            DamageHistoryStorage.Database.ClearAdventures();
            AuraToolsDamageMeterRuntime.NotifyLedgerChanged();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] out-of-run history clear failed: " + ex.Message);
        }
    }

    internal static void EnsureOutOfRunHistoryLoaded(string source)
    {
        if (historyStorageReady)
        {
            return;
        }

        historyStorageReady = true;
        var started = DateTime.UtcNow;
        DamageHistoryStorage.EnsureLegacyMigrations();
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
        if (elapsed >= 50d)
        {
            AuraToolsLog.Warn("[DamageMeter] out-of-run history load was slow. source="
                              + source + ", elapsedMs=" + elapsed.ToString("F0", CultureInfo.InvariantCulture) + ".");
        }
    }

    internal static void ArchiveActiveFightForSettlement(bool? completed = null)
    {
        if (!AuraToolsDamageMeterRuntime.Ledger.InFight)
        {
            return;
        }

        DamageMeterLifecycleCoordinator.MarkEndingSent();
        DamageMeterNetworkRuntime.EndFight((completed ?? !IsGameExitLoss()) ? "Win" : "Loss");
    }

    internal static PlayModeInfo ResolvePlayMode()
    {
        var activeMode = AuraModeRuntime.Current(AuraToolsIds.ModId);
        if (activeMode != null)
        {
            var fallbackName = activeMode.Display?.FallbackName;
            var displayName = string.IsNullOrWhiteSpace(fallbackName)
                ? activeMode.ModeId
                : fallbackName!;
            return new PlayModeInfo(activeMode.ModeId, displayName);
        }

        var modeType = DamageMeterAvailabilityRuntime.ReadLobbyModeType();
        if (string.IsNullOrWhiteSpace(modeType))
        {
            modeType = MapManager.Instance?.ModeMapManager?.GetType().Name ?? "";
        }

        if (string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modeType, "NormalMapManager", StringComparison.OrdinalIgnoreCase))
        {
            return new PlayModeInfo("Normal", "世界推演");
        }

        if (string.Equals(modeType, "Sublimation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modeType, "SublimationManager", StringComparison.OrdinalIgnoreCase))
        {
            return new PlayModeInfo("Sublimation", "弑神模拟");
        }

        if (string.Equals(modeType, "Slot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modeType, "SlotMachineManager", StringComparison.OrdinalIgnoreCase))
        {
            return new PlayModeInfo("Slot", "老虎机模式");
        }

        return new PlayModeInfo(string.IsNullOrWhiteSpace(modeType) ? "Unknown" : modeType, "未知模式");
    }

    internal static bool IsCurrentAdventureCompleted(string? expectedModeId = null)
    {
        var activeMode = AuraModeRuntime.Current(AuraToolsIds.ModId);
        var modeId = string.IsNullOrWhiteSpace(expectedModeId) ? activeMode?.ModeId ?? "" : (expectedModeId ?? "").Trim();
        var runId = activeMode != null
                    && string.Equals(activeMode.ModeId, modeId, StringComparison.OrdinalIgnoreCase)
            ? activeMode.Run?.RunId ?? ""
            : "";
        if (AuraModeOutcomeRuntime.TryReadRecent(
                modeId,
                runId,
                TimeSpan.FromSeconds(30),
                out var sharedOutcome))
        {
            AuraToolsLog.Info("[DamageMeter] adventure outcome resolved from shared mode handoff: mode="
                              + sharedOutcome.ModeId
                              + ", runId="
                              + sharedOutcome.RunId
                              + ", status="
                              + sharedOutcome.Status
                              + ", source="
                              + sharedOutcome.Source
                              + ".");
            return sharedOutcome.IsCompleted;
        }

        if (IsGameExitLoss())
        {
            return false;
        }

        try
        {
            return MapManager.Instance != null && MapManager.Instance.WinTheGame();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsGameExitLoss()
    {
        try
        {
            return GameExitUI.loss;
        }
        catch
        {
            return false;
        }
    }

    internal static IReadOnlyList<OutOfRunTeamMemberSnapshot> CollectTeamMembers(bool captureAvatars)
    {
        var result = new List<OutOfRunTeamMemberSnapshot>();
        try
        {
            var roleTables = GameServer.Instance?.RoleTables;
            if (roleTables != null && roleTables.Count > 0)
            {
                foreach (var role in roleTables.Values)
                {
                    AddTeamMember(result, role, captureAvatars);
                    if (result.Count >= DamageMeterProtocol.MaxTeamMembers)
                    {
                        break;
                    }
                }
            }
            else
            {
                AddTeamMember(result, RoleTable.Instance, captureAvatars);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] team snapshot failed: " + ex.Message);
        }

        return result;
    }

    internal static void CaptureAdventureTeamMembers()
    {
        AdventureTeamMembers.Clear();
        foreach (var member in CollectTeamMembers(captureAvatars: false))
        {
            AdventureTeamMembers.Add(CloneTeamMember(member));
        }

        DamageMeterFightIndex.SetFriendlyIdentitySnapshots(AdventureTeamMembers);
    }

    internal static IReadOnlyList<OutOfRunTeamMemberSnapshot> CollectSettlementTeamMembers(bool captureAvatars)
    {
        var current = CollectTeamMembers(captureAvatars);
        var result = new List<OutOfRunTeamMemberSnapshot>();
        foreach (var member in current)
        {
            var stable = FindAdventureTeamMember(member);
            result.Add(MergeTeamMember(stable, member));
        }

        foreach (var stable in AdventureTeamMembers)
        {
            if (FindTeamMember(result, stable) == null)
            {
                result.Add(CloneTeamMember(stable));
            }
        }

        return result;
    }

    internal static OutOfRunTeamMemberSnapshot? FindAdventureTeamMember(OutOfRunTeamMemberSnapshot? member)
    {
        return FindTeamMember(AdventureTeamMembers, member);
    }

    internal static OutOfRunTeamMemberSnapshot? FindTeamMember(
        IEnumerable<OutOfRunTeamMemberSnapshot> members,
        OutOfRunTeamMemberSnapshot? candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        foreach (var member in members)
        {
            if (member == null)
            {
                continue;
            }

            if (SameNonEmpty(member.PlayerId, candidate.PlayerId)
                || SameNonEmpty(member.InstanceId, candidate.InstanceId)
                || SameNonEmpty(member.PlayerId, candidate.InstanceId)
                || SameNonEmpty(member.InstanceId, candidate.PlayerId))
            {
                return member;
            }
        }

        return null;
    }

    internal static OutOfRunTeamMemberSnapshot MergeTeamMember(
        OutOfRunTeamMemberSnapshot? stable,
        OutOfRunTeamMemberSnapshot current)
    {
        stable ??= new OutOfRunTeamMemberSnapshot();
        current ??= new OutOfRunTeamMemberSnapshot();
        return new OutOfRunTeamMemberSnapshot
        {
            InstanceId = FirstNonEmpty(current.InstanceId, stable.InstanceId),
            PlayerId = FirstNonEmpty(current.PlayerId, stable.PlayerId),
            PlayerDisplayName = FirstNonEmpty(stable.PlayerDisplayName, current.PlayerDisplayName),
            RoleId = FirstNonEmpty(current.RoleId, stable.RoleId),
            RoleDisplayName = FirstNonEmpty(current.RoleDisplayName, stable.RoleDisplayName),
            DisplayName = FirstNonEmpty(stable.PlayerDisplayName, current.PlayerDisplayName, stable.DisplayName, current.DisplayName),
            AvatarPngBase64 = FirstNonEmpty(current.AvatarPngBase64, stable.AvatarPngBase64),
            AvatarSha256 = FirstNonEmpty(current.AvatarSha256, stable.AvatarSha256)
        };
    }

    internal static OutOfRunTeamMemberSnapshot CloneTeamMember(OutOfRunTeamMemberSnapshot source)
    {
        return new OutOfRunTeamMemberSnapshot
        {
            InstanceId = source?.InstanceId ?? "",
            PlayerId = source?.PlayerId ?? "",
            PlayerDisplayName = source?.PlayerDisplayName ?? "",
            RoleId = source?.RoleId ?? "",
            RoleDisplayName = source?.RoleDisplayName ?? "",
            DisplayName = source?.DisplayName ?? "",
            AvatarPngBase64 = source?.AvatarPngBase64 ?? "",
            AvatarSha256 = source?.AvatarSha256 ?? ""
        };
    }

    internal static bool SameNonEmpty(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    internal static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return "";
    }

    internal static void AddTeamMember(List<OutOfRunTeamMemberSnapshot> result, RoleTable? role, bool captureAvatars)
    {
        if (role == null || result.Count >= DamageMeterProtocol.MaxTeamMembers)
        {
            return;
        }

        var career = role.Career;
        var playerId = role.Id ?? "";
        var roleId = SafeDataField(career, "Id");
        var roleDisplayName = SafeLocalizedField(career, "Name");
        if (string.IsNullOrWhiteSpace(roleDisplayName))
        {
            roleDisplayName = string.IsNullOrWhiteSpace(roleId) ? playerId : roleId;
        }

        var playerDisplayName = ResolvePlayerDisplayName(playerId);
        if (string.IsNullOrWhiteSpace(playerDisplayName))
        {
            playerDisplayName = string.IsNullOrWhiteSpace(playerId) ? roleDisplayName : playerId;
        }

        var avatarPath = SafeDataField(career, "Avatar");
        if (string.IsNullOrWhiteSpace(avatarPath))
        {
            avatarPath = SafeDataField(career, "DollIcon");
        }

        var avatarBytes = captureAvatars
            ? TryEncodeSprite(avatarPath, playerId, roleId)
            : Array.Empty<byte>();
        result.Add(new OutOfRunTeamMemberSnapshot
        {
            InstanceId = playerId,
            PlayerId = playerId,
            PlayerDisplayName = playerDisplayName,
            RoleId = roleId,
            RoleDisplayName = roleDisplayName,
            DisplayName = playerDisplayName,
            AvatarPngBase64 = avatarBytes.Length == 0 ? "" : Convert.ToBase64String(avatarBytes),
            AvatarSha256 = avatarBytes.Length == 0 ? "" : AuraSharedSecureEnvelope.Sha256Hex(avatarBytes)
        });
    }

    internal static string SafeDataField(IDataConfig? dataConfig, string key)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue(key, out var value))
            {
                return value ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    internal static string ResolvePlayerDisplayName(string playerId)
    {
        try
        {
            var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
            var lobbyName = "";
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player != null && string.Equals(player.Id, playerId, StringComparison.OrdinalIgnoreCase))
                    {
                        lobbyName = player.Name;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(lobbyName))
            {
                return (lobbyName ?? "").Trim();
            }
        }
        catch
        {
        }

        try
        {
            var playerManager = PlayerManager.Instance;
            var managerName = playerManager?.playerInfo?.Name;
            if (playerManager != null
                && string.Equals(playerManager.PlayerId, playerId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(managerName))
            {
                return (managerName ?? "").Trim();
            }
        }
        catch
        {
        }

        try
        {
            var config = Singleton<GameConfigManager>.Instance;
            if (config != null
                && string.Equals(config.PlayerId, playerId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(config.PlayerName))
            {
                return config.PlayerName.Trim();
            }
        }
        catch
        {
        }

        return "";
    }

    internal static string SafeLocalizedField(IDataConfig? dataConfig, string key)
    {
        try
        {
            var localized = dataConfig?.data?.Localize(key) ?? "";
            return string.Equals(localized, key, StringComparison.OrdinalIgnoreCase) ? "" : localized;
        }
        catch
        {
            return SafeDataField(dataConfig, key);
        }
    }

    internal static byte[] TryEncodeSprite(string resourcePath, string playerId, string roleId)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return Array.Empty<byte>();
        }

        var cacheKey = resourcePath.Trim();
        if (AvatarPngCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var sprite = AuraToolsResourceCache.Load<Sprite>(resourcePath, true);
            if (sprite == null || sprite.texture == null)
            {
                AuraToolsLog.Warn("[DamageMeter] team avatar skipped: resource not found. player="
                                  + playerId + ", role=" + roleId + ", path=" + resourcePath + ".");
                return Array.Empty<byte>();
            }

            var settings = AuraToolsConfigService.MatchExperience.DamageMeter;
            var rect = sprite.textureRect;
            var pixelCount = Math.Max(1L, (long)Mathf.Max(1, (int)rect.width)
                                      * Mathf.Max(1, (int)rect.height));
            if (pixelCount > settings.MaxAvatarEncodePixels)
            {
                AuraToolsLog.Warn("[DamageMeter] team avatar skipped: sprite too large. player="
                                  + playerId + ", role=" + roleId + ", path=" + resourcePath
                                  + ", pixels=" + pixelCount
                                  + ", maxPixels=" + settings.MaxAvatarEncodePixels + ".");
                return Array.Empty<byte>();
            }

            var texture = CopySpriteTexture(sprite);
            var bytes = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            if (bytes == null || bytes.Length == 0)
            {
                return Array.Empty<byte>();
            }

            if (bytes.Length > settings.MaxAvatarPngBytes)
            {
                AuraToolsLog.Warn("[DamageMeter] team avatar skipped: PNG too large. player="
                                  + playerId + ", role=" + roleId + ", path=" + resourcePath
                                  + ", bytes=" + bytes.Length
                                  + ", maxBytes=" + settings.MaxAvatarPngBytes + ".");
                return Array.Empty<byte>();
            }

            CacheAvatarPng(cacheKey, bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] team avatar encode failed: player="
                              + playerId + ", role=" + roleId + ", path=" + resourcePath
                              + ", error=" + ex.Message);
            return Array.Empty<byte>();
        }
    }

    internal static void CacheAvatarPng(string key, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(key) || bytes.Length == 0)
        {
            return;
        }

        if (AvatarPngCache.Count >= MaxAvatarCacheEntries)
        {
            AvatarPngCache.Clear();
        }

        AvatarPngCache[key] = bytes;
    }

    internal static Texture2D CopySpriteTexture(Sprite sprite)
    {
        var rect = sprite.textureRect;
        var x = Mathf.Max(0, (int)rect.x);
        var y = Mathf.Max(0, (int)rect.y);
        var width = Mathf.Max(1, (int)rect.width);
        var height = Mathf.Max(1, (int)rect.height);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            var pixels = sprite.texture.GetPixels(x, y, width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        catch
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(
                sprite.texture.width,
                sprite.texture.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            try
            {
                Graphics.Blit(sprite.texture, temporary);
                RenderTexture.active = temporary;
                texture.ReadPixels(new Rect(x, y, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    internal sealed class PlayModeInfo
    {
        public PlayModeInfo(string id, string displayName)
        {
            Id = id ?? "";
            DisplayName = displayName ?? "";
        }

        public string Id { get; }

        public string DisplayName { get; }
    }

    internal static string FightResult(ModHookContext context)
    {
        var name = DamageMeterAvailabilityRuntime.GetHookName(context);
        if (name.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Win";
        }

        if (name.IndexOf("Escape", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Escape";
        }

        if (name.IndexOf("Loss", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Loss";
        }

        return "Unknown";
    }

}
