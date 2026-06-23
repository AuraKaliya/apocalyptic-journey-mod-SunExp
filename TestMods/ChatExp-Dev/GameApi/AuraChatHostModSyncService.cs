using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using AuraOnline.Shared;
using ChatExp.Dll.Infrastructure;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace ChatExp.Dll.GameApi;

public static class AuraChatHostModSyncService
{
    private static bool isRunning;

    public static bool IsRunning => isRunning;

    public static int CountPendingActions(AuraChatModSyncState? state)
    {
        return BuildPlan(state).Count;
    }

    public static void StartSync()
    {
        if (isRunning)
        {
            return;
        }

        var plan = BuildPlan(AuraChatRuntime.ModSyncState);
        if (plan.Count == 0)
        {
            AuraChatRuntime.SetModSyncActionStatus("\u5f53\u524d\u6ca1\u6709\u9700\u8981\u540c\u6b65\u7684\u623f\u4e3bMOD\u5dee\u5f02\u3002");
            return;
        }

        RunSyncAsync(plan).Forget();
    }

    private static async UniTaskVoid RunSyncAsync(IReadOnlyList<HostModSyncAction> plan)
    {
        isRunning = true;
        var changed = false;
        var failures = new List<string>();

        try
        {
            for (var index = 0; index < plan.Count; index++)
            {
                var action = plan[index];
                var prefix = "[" + (index + 1) + "/" + plan.Count + "] ";
                AuraChatRuntime.SetModSyncActionStatus(prefix + action.ModName + " " + action.StatusText);

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
                    ChatExpLog.Warn("Host mod sync action failed: " + action.ModName + " -> " + ex);
                }
            }

            var completed = "\u623f\u4e3bMOD\u540c\u6b65\u5b8c\u6210";
            if (failures.Count > 0)
            {
                completed += "\uff0c\u5931\u8d25 " + failures.Count + " \u9879: " + string.Join("; ", failures.Take(3));
            }

            if (changed)
            {
                completed += "\uff0c\u9700\u8981\u91cd\u542f\u6e38\u620f\u751f\u6548\u3002";
            }

            AuraChatRuntime.SetModSyncActionStatus(completed);
            if (changed)
            {
                ShowRestartPrompt();
            }
        }
        finally
        {
            isRunning = false;
            AuraChatRuntime.SetModSyncActionStatus(AuraChatRuntime.ModSyncActionStatus);
        }
    }

    private static List<HostModSyncAction> BuildPlan(AuraChatModSyncState? state)
    {
        var plan = new List<HostModSyncAction>();
        if (state == null
            || string.IsNullOrWhiteSpace(state.HostPlayerId)
            || string.IsNullOrWhiteSpace(state.LocalPlayerId)
            || string.Equals(state.HostPlayerId, state.LocalPlayerId, StringComparison.Ordinal))
        {
            return plan;
        }

        foreach (var row in state.Rows)
        {
            if (IsCurrentMod(row, state.CurrentModId))
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

    private static async UniTask<bool> DownloadAndEnableAsync(HostModSyncAction action, int index, int total)
    {
        var publishedFileId = action.HostMod?.PublishedFileId ?? 0UL;
        if (publishedFileId == 0UL)
        {
            throw new InvalidOperationException("\u623f\u4e3bMOD\u6ca1\u6709\u53ef\u7528\u7684\u521b\u610f\u5de5\u574aID");
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
                AuraChatRuntime.SetModSyncActionStatus("[" + index + "/" + total + "] " + action.ModName + " " + lastMessage + percent);
            },
            CancellationToken.None);

        if (!completed)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(lastMessage) ? "\u4e0b\u8f7d\u672a\u5b8c\u6210" : lastMessage);
        }

        var targetDirectory = ResolveWorkshopTargetDirectory(publishedFileId, completedTargetDirectory, info.ModsInstallDirectory);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new InvalidOperationException("\u521b\u610f\u5de5\u574aMOD\u76ee\u6807\u8def\u5f84\u4e0d\u53ef\u7528");
        }

        _ = TryWriteEnabledState(targetDirectory, true, out var reason);
        if (!string.IsNullOrEmpty(reason))
        {
            throw new InvalidOperationException(reason);
        }

        return true;
    }

    private static bool ApplyLocalEnabledState(HostModSyncAction action, out string reason)
    {
        reason = "";
        var local = action.LocalMod;
        if (local == null)
        {
            reason = "\u672c\u5730MOD\u4e0d\u5b58\u5728";
            return false;
        }

        var targetDirectory = ResolveLocalTargetDirectory(local);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            reason = "\u672c\u5730MOD\u8def\u5f84\u4e0d\u53ef\u7528";
            return false;
        }

        return TryWriteEnabledState(targetDirectory, action.TargetEnabled, out reason);
    }

    private static bool TryWriteEnabledState(string localDirectory, bool enabled, out string reason)
    {
        reason = "";
        var configPath = Path.Combine(localDirectory, "ModConfig.json");
        if (!File.Exists(configPath))
        {
            reason = "ModConfig.json \u4e0d\u5b58\u5728";
            return false;
        }

        var json = JObject.Parse(File.ReadAllText(configPath));
        var previous = json.TryGetValue("Enabled", StringComparison.OrdinalIgnoreCase, out var token)
            && token.Type == JTokenType.Boolean
            && token.Value<bool>();

        json["Enabled"] = enabled;
        File.WriteAllText(configPath, json.ToString(Formatting.Indented));
        UpdateRuntimeModState(localDirectory, enabled);
        return previous != enabled;
    }

    private static string ResolveLocalTargetDirectory(AuraChatModSnapshot local)
    {
        if (!string.IsNullOrWhiteSpace(local.DirectoryName))
        {
            return local.DirectoryName;
        }

        return local.PublishedFileId == 0UL
            ? ""
            : ResolveWorkshopTargetDirectory(local.PublishedFileId, "", "");
    }

    private static string ResolveWorkshopTargetDirectory(ulong publishedFileId, string completedTargetDirectory, string infoTargetDirectory)
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
            ChatExpLog.Warn("Resolve workshop target directory failed: " + ex.Message);
            return "";
        }
    }

    private static void UpdateRuntimeModState(string localDirectory, bool enabled)
    {
        if (Singleton<GameConfigManager>.Instance == null)
        {
            return;
        }

        var normalized = NormalizePath(localDirectory);
        foreach (var config in Singleton<GameConfigManager>.Instance.modConfigs)
        {
            if (NormalizePath(config.DirectoryName) == normalized)
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

        if (method == null)
        {
            return;
        }

        method.Invoke(manager, new object?[]
        {
            "Tips",
            "\u662f\u5426\u8981\u91cd\u542f\u6e38\u620f\u4ee5\u8ba9\u6a21\u7ec4\u66f4\u6539\u751f\u6548?",
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

    private static bool IsCurrentMod(AuraChatModSyncRow row, string currentModId)
    {
        return string.Equals(row.ModName, currentModId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.ModKey, currentModId, StringComparison.OrdinalIgnoreCase);
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
                    HostModSyncActionKind.EnableExisting => "\u542f\u7528\u672c\u5730MOD",
                    HostModSyncActionKind.DisableExisting => "\u7981\u7528\u672c\u5730MOD",
                    HostModSyncActionKind.DownloadEnable => "\u4e0b\u8f7d\u5e76\u542f\u7528",
                    _ => "\u65e0\u521b\u610f\u5de5\u574aID\uff0c\u9700\u624b\u52a8\u5b89\u88c5"
                };
            }
        }
    }
}
