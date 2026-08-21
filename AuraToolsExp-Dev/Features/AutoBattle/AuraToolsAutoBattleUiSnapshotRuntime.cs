using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal sealed class AutoBattleUiSnapshot
{
    public long Revision { get; set; }

    public string Profile { get; set; } = "balanced";

    public string ModelId { get; set; } = "";

    public bool Ready { get; set; }

    public bool Loading { get; set; }

    public string Message { get; set; } = "正在读取自动战斗索引";

    public IReadOnlyList<string> ScenarioIds { get; set; } =
        Array.Empty<string>();

    public IReadOnlyList<AutoBattleModelLibraryEntry> Models { get; set; } =
        Array.Empty<AutoBattleModelLibraryEntry>();

    public AutoBattleSimulationResultPresentation Result { get; set; } = new();
}

internal static class AuraToolsAutoBattleUiSnapshotRuntime
{
    private const string WorkKey = "AutoBattle.UiSnapshot";
    private static readonly object Gate = new();
    private static AutoBattleUiSnapshot current = new();
    private static long generation;
    private static bool queued;

    public static event Action? Changed;

    public static AutoBattleUiSnapshot Snapshot(
        string profile,
        string modelId)
    {
        var normalizedProfile = NormalizeProfile(profile);
        var normalizedModel = (modelId ?? "").Trim();
        lock (Gate)
        {
            if (!string.Equals(
                    current.Profile,
                    normalizedProfile,
                    StringComparison.Ordinal)
                || !string.Equals(
                    current.ModelId,
                    normalizedModel,
                    StringComparison.Ordinal))
            {
                return new AutoBattleUiSnapshot
                {
                    Revision = current.Revision,
                    Profile = normalizedProfile,
                    ModelId = normalizedModel,
                    Loading = true
                };
            }
            return current;
        }
    }

    public static void RequestRefresh(
        string profile,
        string modelId,
        bool force = false)
    {
        var normalizedProfile = NormalizeProfile(profile);
        var normalizedModel = (modelId ?? "").Trim();
        long requestGeneration;
        lock (Gate)
        {
            if (queued)
            {
                return;
            }
            if (!force
                && current.Ready
                && string.Equals(
                    current.Profile,
                    normalizedProfile,
                    StringComparison.Ordinal)
                && string.Equals(
                    current.ModelId,
                    normalizedModel,
                    StringComparison.Ordinal))
            {
                return;
            }
            queued = true;
            requestGeneration = ++generation;
            current = new AutoBattleUiSnapshot
            {
                Revision = current.Revision,
                Profile = normalizedProfile,
                ModelId = normalizedModel,
                Loading = true
            };
        }

        var accepted = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<AutoBattleUiSnapshot>
            {
                OwnerId = AuraToolsIds.ModId + ".AutoBattle",
                Key = WorkKey,
                Source = "AutoBattle.UiSnapshot",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => Build(normalizedProfile, normalizedModel),
                ApplyOnMainThread = result =>
                {
                    lock (Gate)
                    {
                        if (requestGeneration != generation)
                        {
                            return;
                        }
                        queued = false;
                        result.Revision = current.Revision + 1;
                        current = result;
                    }
                    Changed?.Invoke();
                },
                OnFailedOnMainThread = ex =>
                {
                    lock (Gate)
                    {
                        if (requestGeneration != generation)
                        {
                            return;
                        }
                        queued = false;
                        current = new AutoBattleUiSnapshot
                        {
                            Revision = current.Revision + 1,
                            Profile = normalizedProfile,
                            ModelId = normalizedModel,
                            Message = "自动战斗索引读取失败：" + ex.Message
                        };
                    }
                    Changed?.Invoke();
                }
            });
        if (accepted)
        {
            return;
        }
        lock (Gate)
        {
            if (requestGeneration == generation)
            {
                queued = false;
                current.Message = "自动战斗索引任务未能提交";
                current.Loading = false;
                current.Revision++;
            }
        }
        Changed?.Invoke();
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            generation++;
            queued = false;
            current = new AutoBattleUiSnapshot
            {
                Revision = current.Revision + 1
            };
        }
    }

    private static AutoBattleUiSnapshot Build(
        string profile,
        string modelId)
    {
        return new AutoBattleUiSnapshot
        {
            Profile = profile,
            ModelId = modelId,
            Ready = true,
            Message = "自动战斗索引已就绪",
            ScenarioIds = AuraToolsAutoBattleSimulationRuntime
                .AvailableScenarioIds()
                .ToArray(),
            Models = AuraToolsAutoBattleModelRuntime
                .SnapshotModelLibrary(profile)
                .ToArray(),
            Result = AuraToolsAutoBattleSimulationRuntime
                .GetResultPresentation(profile, modelId)
        };
    }

    private static string NormalizeProfile(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "balanced"
            : value.Trim().ToLowerInvariant();
    }
}
