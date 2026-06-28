using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace AuraOnline.Shared;

public sealed class AuraOnlineHostModSyncSession
{
    private readonly string currentModId;
    private readonly Action<string>? info;
    private readonly Action<string>? warn;

    public AuraOnlineHostModSyncSession(string currentModId, Action<string>? info = null, Action<string>? warn = null)
    {
        this.currentModId = (currentModId ?? "").Trim();
        this.info = info;
        this.warn = warn;
    }

    public event Action? Changed;

    public bool IsRunning { get; private set; }

    public string ActionStatus { get; private set; } = "";

    public int CountPendingActions(AuraChatModSyncState? state)
    {
        return BuildPlan(state).Count;
    }

    public void SetActionStatus(string status)
    {
        UpdateActionStatus(status);
    }

    public bool IsLocalHost(AuraChatModSyncState? state)
    {
        return state == null
               || string.IsNullOrWhiteSpace(state.HostPlayerId)
               || string.IsNullOrWhiteSpace(state.LocalPlayerId)
               || string.Equals(state.HostPlayerId, state.LocalPlayerId, StringComparison.Ordinal);
    }

    public void StartSync(AuraChatModSyncState? state, bool showRestartPrompt = true)
    {
        if (IsRunning)
        {
            return;
        }

        var plan = BuildPlan(state);
        if (plan.Count == 0)
        {
            UpdateActionStatus("当前没有需要同步的房主MOD差异。");
            return;
        }

        LogPlan(plan);
        RunSyncAsync(plan, showRestartPrompt).Forget();
    }

    private async UniTaskVoid RunSyncAsync(IReadOnlyList<HostModSyncAction> plan, bool showRestartPrompt)
    {
        IsRunning = true;
        NotifyChanged();
        var changed = false;
        var failures = new List<string>();

        try
        {
            for (var index = 0; index < plan.Count; index++)
            {
                var action = plan[index];
                var prefix = "[" + (index + 1) + "/" + plan.Count + "] ";
                UpdateActionStatus(prefix + action.ModName + " " + action.StatusText);

                try
                {
                    switch (action.Kind)
                    {
                        case HostModSyncActionKind.DownloadEnable:
                            changed |= await DownloadAndEnableAsync(action, index + 1, plan.Count);
                            break;
                        case HostModSyncActionKind.EnableExisting:
                        case HostModSyncActionKind.DisableExisting:
                            changed |= ApplyLocalEnabledState(action, out var reason);
                            if (!string.IsNullOrEmpty(reason))
                            {
                                failures.Add(action.ModName + ": " + reason);
                            }

                            break;
                        default:
                            failures.Add(action.ModName + ": " + action.StatusText);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(action.ModName + ": " + ex.Message);
                    warn?.Invoke("Host mod sync action failed: " + action.ModName + " -> " + ex);
                }
            }

            var completed = "房主MOD同步完成";
            if (failures.Count > 0)
            {
                completed += "，失败 " + failures.Count + " 项: " + string.Join("; ", failures.Take(3));
            }

            if (changed)
            {
                completed += "，需要重启游戏生效。";
            }

            SetActionStatus(completed);
            if (changed && showRestartPrompt)
            {
                ShowRestartPrompt();
            }
        }
        finally
        {
            IsRunning = false;
            NotifyChanged();
        }
    }

    private List<HostModSyncAction> BuildPlan(AuraChatModSyncState? state)
    {
        var plan = new List<HostModSyncAction>();
        if (IsLocalHost(state))
        {
            return plan;
        }

        foreach (var row in state!.Rows)
        {
            if (IsCurrentMod(row, state.CurrentModId) || IsCurrentMod(row, currentModId))
            {
                continue;
            }

            var hostEnabled = row.HostMod?.Enabled == true;
            var localEnabled = row.LocalMod?.Enabled == true;
            if (hostEnabled == localEnabled)
            {
                continue;
            }

            if (hostEnabled)
            {
                if (row.LocalMod != null)
                {
                    plan.Add(new HostModSyncAction(row.ModName, HostModSyncActionKind.EnableExisting, true, row.HostMod, row.LocalMod));
                }
                else if ((row.HostMod?.PublishedFileId ?? 0UL) != 0UL)
                {
                    plan.Add(new HostModSyncAction(row.ModName, HostModSyncActionKind.DownloadEnable, true, row.HostMod, null));
                }
                else
                {
                    plan.Add(new HostModSyncAction(row.ModName, HostModSyncActionKind.Unsupported, true, row.HostMod, null));
                }
            }
            else if (row.LocalMod != null)
            {
                plan.Add(new HostModSyncAction(row.ModName, HostModSyncActionKind.DisableExisting, false, row.HostMod, row.LocalMod));
            }
        }

        return plan;
    }

    private async UniTask<bool> DownloadAndEnableAsync(HostModSyncAction action, int index, int total)
    {
        var publishedFileId = action.HostMod?.PublishedFileId ?? 0UL;
        if (publishedFileId == 0UL)
        {
            throw new InvalidOperationException("房主MOD没有可用的创意工坊ID");
        }

        var completed = false;
        var lastMessage = "";
        var completedTargetDirectory = "";
        var info = new SteamWorkshopModInfo
        {
            PublishedFileId = publishedFileId,
            Title = action.ModName
        };

        await SteamWorkshopDownloadService.Instance.ToggleDownloadAsync(
            info,
            progress =>
            {
                lastMessage = progress?.Message ?? "";
                if (progress != null && progress.State == SteamWorkshopDownloadState.Completed)
                {
                    completed = true;
                    completedTargetDirectory = progress.TargetDirectory ?? "";
                }

                var percent = progress == null ? "" : " " + Mathf.RoundToInt(Mathf.Clamp01(progress.Progress) * 100f) + "%";
                UpdateActionStatus("[" + index + "/" + total + "] " + action.ModName + " " + lastMessage + percent);
            },
            CancellationToken.None);

        if (!completed)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(lastMessage) ? "下载未完成" : lastMessage);
        }

        var targetDirectory = ResolveWorkshopTargetDirectory(publishedFileId, completedTargetDirectory, info.ModsInstallDirectory);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new InvalidOperationException("创意工坊MOD目标路径不可用");
        }

        _ = TryWriteEnabledState(targetDirectory, true, publishedFileId, out var reason);
        if (!string.IsNullOrEmpty(reason))
        {
            throw new InvalidOperationException(reason);
        }

        return true;
    }

    private bool ApplyLocalEnabledState(HostModSyncAction action, out string reason)
    {
        reason = "";
        var local = action.LocalMod;
        if (local == null)
        {
            reason = "本地MOD不存在";
            return false;
        }

        var targetDirectory = ResolveLocalTargetDirectory(local);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            reason = "本地MOD路径不可用";
            return false;
        }

        return TryWriteEnabledState(targetDirectory, action.TargetEnabled, local.PublishedFileId, out reason);
    }

    private bool TryWriteEnabledState(string localDirectory, bool enabled, ulong publishedFileId, out string reason)
    {
        reason = "";
        var configPath = Path.Combine(localDirectory, "ModConfig.json");
        if (!File.Exists(configPath))
        {
            reason = "ModConfig.json 不存在";
            return false;
        }

        var changed = false;
        var json = JObject.Parse(File.ReadAllText(configPath));
        var previous = json.TryGetValue("Enabled", StringComparison.OrdinalIgnoreCase, out var token)
            && token.Type == JTokenType.Boolean
            && token.Value<bool>();

        if (previous != enabled)
        {
            json["Enabled"] = enabled;
            File.WriteAllText(configPath, json.ToString(Formatting.Indented));
            changed = true;
        }

        if (publishedFileId != 0UL)
        {
            try
            {
                var previousWorkshopState = TryReadWorkshopEnabledState(publishedFileId, out var workshopEnabled)
                    ? workshopEnabled
                    : previous;
                if (SaveWorkshopEnabledState(publishedFileId, enabled))
                {
                    changed |= previousWorkshopState != enabled;
                }
            }
            catch (Exception ex)
            {
                warn?.Invoke("Save workshop enabled state failed: " + publishedFileId + " -> " + ex.Message);
            }
        }

        UpdateRuntimeModState(localDirectory, publishedFileId, enabled);
        return changed;
    }

    private static bool TryReadWorkshopEnabledState(ulong publishedFileId, out bool enabled)
    {
        enabled = false;
        if (publishedFileId == 0UL)
        {
            return false;
        }

        var service = SteamWorkshopDownloadService.Instance;
        var method = service.GetType().GetMethod(
            "TryGetWorkshopEnabledState",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(ulong), typeof(bool).MakeByRefType() },
            null);
        if (method != null)
        {
            var args = new object[] { publishedFileId, false };
            if (method.Invoke(service, args) is bool success && success)
            {
                enabled = args[1] is bool value && value;
                return true;
            }
        }

        return TryReadWorkshopStateFile(publishedFileId, out enabled);
    }

    private static bool SaveWorkshopEnabledState(ulong publishedFileId, bool enabled)
    {
        if (publishedFileId == 0UL)
        {
            return false;
        }

        var service = SteamWorkshopDownloadService.Instance;
        var method = service.GetType().GetMethod(
            "SaveWorkshopEnabledState",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(ulong), typeof(bool) },
            null);
        if (method != null && method.Invoke(service, new object[] { publishedFileId, enabled }) is bool success)
        {
            return success;
        }

        return WriteWorkshopStateFile(publishedFileId, enabled);
    }

    private static bool TryReadWorkshopStateFile(ulong publishedFileId, out bool enabled)
    {
        enabled = false;
        var path = WorkshopStatePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var json = JObject.Parse(File.ReadAllText(path));
        var states = json["EnabledByPublishedFileId"] as JObject;
        var token = states?[publishedFileId.ToString()];
        if (token == null || token.Type != JTokenType.Boolean)
        {
            return false;
        }

        enabled = token.Value<bool>();
        return true;
    }

    private static bool WriteWorkshopStateFile(ulong publishedFileId, bool enabled)
    {
        var path = WorkshopStatePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = File.Exists(path)
            ? JObject.Parse(File.ReadAllText(path))
            : new JObject();
        var states = json["EnabledByPublishedFileId"] as JObject;
        if (states == null)
        {
            states = new JObject();
            json["EnabledByPublishedFileId"] = states;
        }

        states[publishedFileId.ToString()] = enabled;
        File.WriteAllText(path, json.ToString(Formatting.Indented));
        return true;
    }

    private static string WorkshopStatePath()
    {
        try
        {
            return Path.Combine(Globals.ModsPath, "WorkshopModStates.json");
        }
        catch
        {
            return "";
        }
    }

    private string ResolveLocalTargetDirectory(AuraChatModSnapshot local)
    {
        if (!string.IsNullOrWhiteSpace(local.DirectoryName))
        {
            return local.DirectoryName;
        }

        return local.PublishedFileId == 0UL
            ? ""
            : ResolveWorkshopTargetDirectory(local.PublishedFileId, "", "");
    }

    private string ResolveWorkshopTargetDirectory(ulong publishedFileId, string completedTargetDirectory, string infoTargetDirectory)
    {
        if (!string.IsNullOrWhiteSpace(completedTargetDirectory))
        {
            return completedTargetDirectory;
        }

        if (!string.IsNullOrWhiteSpace(infoTargetDirectory))
        {
            return infoTargetDirectory;
        }

        try
        {
            return SteamWorkshopDownloadService.Instance.GetModsTargetDirectory(publishedFileId) ?? "";
        }
        catch (Exception ex)
        {
            warn?.Invoke("Resolve workshop target directory failed: " + ex.Message);
            return "";
        }
    }

    private static void UpdateRuntimeModState(string localDirectory, ulong publishedFileId, bool enabled)
    {
        if (Singleton<GameConfigManager>.Instance == null)
        {
            return;
        }

        var normalized = NormalizePath(localDirectory);
        foreach (var config in Singleton<GameConfigManager>.Instance.modConfigs)
        {
            var sameDirectory = NormalizePath(config.DirectoryName) == normalized;
            var sameWorkshop = publishedFileId != 0UL && config.WorkshopPublishedFileId == publishedFileId;
            if (sameDirectory || sameWorkshop)
            {
                config.Enabled = enabled;
            }
        }
    }

    private static void ShowRestartPrompt()
    {
        var manager = UIManager.Instance;
        if (manager == null)
        {
            return;
        }

        var method = typeof(UIManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(item => item.Name == "ShowModalWindow" && item.GetParameters().Length == 10);

        method?.Invoke(manager, new object?[]
        {
            "Tips",
            "MOD配置已同步，需要重启游戏后生效。是否现在退出游戏？",
            new UnityAction(() => Application.Quit()),
            0f,
            null,
            true,
            true,
            "Yes",
            "No",
            true
        });
    }

    private void UpdateActionStatus(string status)
    {
        ActionStatus = AuraChatTextLimiter.LimitSystemLine(status);
        info?.Invoke("[ModSync] " + ActionStatus);
        NotifyChanged();
    }

    private void LogPlan(IReadOnlyList<HostModSyncAction> plan)
    {
        var preview = string.Join(
            "; ",
            plan.Take(12).Select(action =>
                action.ModName
                + "="
                + action.StatusText
                + "#hostPid:"
                + (action.HostMod?.PublishedFileId ?? 0UL)
                + "#localPid:"
                + (action.LocalMod?.PublishedFileId ?? 0UL)));
        info?.Invoke("[ModSync] plan actions=" + plan.Count + (preview.Length == 0 ? "" : " " + preview));
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }

    private static bool IsCurrentMod(AuraChatModSyncRow row, string modId)
    {
        return !string.IsNullOrWhiteSpace(modId)
               && (string.Equals(row.ModName, modId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(row.ModKey, modId, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "" : path.Replace('\\', '/').TrimEnd('/');
    }

    private enum HostModSyncActionKind
    {
        EnableExisting,
        DisableExisting,
        DownloadEnable,
        Unsupported
    }

    private sealed class HostModSyncAction
    {
        public HostModSyncAction(
            string modName,
            HostModSyncActionKind kind,
            bool targetEnabled,
            AuraChatModSnapshot? hostMod,
            AuraChatModSnapshot? localMod)
        {
            ModName = modName;
            Kind = kind;
            TargetEnabled = targetEnabled;
            HostMod = hostMod;
            LocalMod = localMod;
        }

        public string ModName { get; }

        public HostModSyncActionKind Kind { get; }

        public bool TargetEnabled { get; }

        public AuraChatModSnapshot? HostMod { get; }

        public AuraChatModSnapshot? LocalMod { get; }

        public string StatusText
        {
            get
            {
                return Kind switch
                {
                    HostModSyncActionKind.EnableExisting => "启用本地MOD",
                    HostModSyncActionKind.DisableExisting => "禁用本地MOD",
                    HostModSyncActionKind.DownloadEnable => "下载并启用",
                    _ => "无创意工坊ID，需手动安装"
                };
            }
        }
    }
}
