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
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageCaptureCoordinator
{
    private static readonly DamageCaptureSession Session = new();
    private static DamageFrameWindow<HitFrame> HitFrames => Session.HitFrames;
    private static DamageFrameWindow<PureHpFrame> PureHpFrames => Session.PureHpFrames;
    private static DamageFrameWindow<HpSetterFrame> HpSetterFrames => Session.HpSetterFrames;
    private static DamageFrameWindow<BuffApplicationFrame> BuffFrames => Session.BuffFrames;
    private static DamageFrameWindow<StatusBuffFrame> StatusBuffFrames => Session.StatusBuffFrames;
    private static BuffAttributionEngine BuffAttribution => Session.BuffAttribution;
    private static readonly Action<string, ISourceData> BuffBroadcastListener = OnBroadcastEventWithParam;
    private static bool CaptureEnabled => AuraToolsDamageMeterRuntime.Ledger.InFight && AuraToolsDamageMeterRuntime.Ledger.SharedEnabled;

    internal static void BeforeHit(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("before hit", () =>
        {
            if (!CaptureEnabled || context.Target is not IStatusManager target)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordHitHook();
            Session.PruneFrames();
            var arguments = context.Arguments ?? Array.Empty<object>();
            var frame = HitFrames.Rent(Time.frameCount);
            frame.CallId = Session.NextCallId();
            frame.Target = target;
            frame.TargetId = SafeStatusId(target);
            frame.BeforeHp = SafeHp(target);
            frame.BeforeShield = SafeDefend(target);
            frame.DamageType = ArgumentString(arguments, 1);
            frame.SourceDataId = ArgumentString(arguments, 2);
            frame.SourceInstanceId = ArgumentString(arguments, 3);
            HitFrames.Add(frame);
        });
    }

    internal static void AfterDamageTextCreate(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("damage text", () =>
        {
            DamageMeterPerformanceCounters.RecordDamageTextCreateHook();
            if (!CaptureEnabled
                || context.Arguments == null
                || context.Arguments.Length == 0
                || !Session.TryReadDamageText(context.Arguments[0], out var data))
            {
                return;
            }

            var frameIndex = Session.FindHitFrame(data);
            if (frameIndex < 0)
            {
                return;
            }

            var frame = HitFrames[frameIndex];
            var target = frame.Target;
            var sourceInstanceId = frame.SourceInstanceId;
            var sourceDataId = frame.SourceDataId;
            var damageType = string.IsNullOrWhiteSpace(data.DamageType) ? frame.DamageType : data.DamageType;
            var hpDamage = Math.Max(0, frame.BeforeHp - SafeHp(target));
            var shieldDamage = Math.Max(0, frame.BeforeShield - SafeDefend(target));
            var finalDamage = Math.Max(0, data.Hit);
            HitFrames.RemoveAt(frameIndex);
            if (hpDamage <= 0 && shieldDamage <= 0)
            {
                return;
            }

            SubmitResolvedDamage(
                target,
                sourceInstanceId,
                sourceDataId,
                damageType,
                hpDamage,
                shieldDamage,
                finalDamage,
                DamageAttributionConfidence.Exact);
        });
    }

    internal static void AfterHit(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("after hit", () =>
        {
            if (context.Target is not IStatusManager target)
            {
                return;
            }

            var targetId = SafeStatusId(target);
            var index = -1;
            for (var i = HitFrames.Count - 1; i >= 0; i--)
            {
                var frame = HitFrames[i];
                if (ReferenceEquals(frame.Target, target)
                    || !string.IsNullOrWhiteSpace(targetId) && frame.TargetId == targetId)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                HitFrames.RemoveAt(index);
            }
        });
    }

    internal static void BeforePureChangeHp(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("before pure hp", () =>
        {
            if (!CaptureEnabled
                || context.Target is not IScriptExecutor executor
                || ParseInt(ArgumentString(context.Arguments, 0)) >= 0)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordPureHpHook();
            Session.PruneFrames();
            var source = executor.Self;
            var targets = Session.CaptureTargetHpFrames(executor);
            if (targets.Count == 0)
            {
                Session.ReleaseTargetFrameList(targets);
                return;
            }

            var frame = PureHpFrames.Rent(Time.frameCount);
            frame.CallId = Session.NextCallId();
            frame.Executor = executor;
            frame.Source = source;
            frame.SourceId = SafeStatusId(source);
            frame.SourceDataId = SafeDataId(executor.dataConfig);
            frame.Targets = targets;
            PureHpFrames.Add(frame);
        });
    }

    internal static void AfterPureChangeHp(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("after pure hp", () =>
        {
            if (context.Target is not IScriptExecutor executor)
            {
                return;
            }

            var index = -1;
            for (var i = PureHpFrames.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(PureHpFrames[i].Executor, executor))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            var frame = PureHpFrames[index];
            foreach (var target in frame.Targets)
            {
                if (target.Recorded)
                {
                    continue;
                }

                var hpDamage = Math.Max(0, target.BeforeHp - SafeHp(target.Target));
                if (hpDamage <= 0)
                {
                    continue;
                }

                SubmitDirectDamage(
                    target.Target,
                    frame.Source,
                    frame.SourceId,
                    frame.SourceDataId,
                    "PureChangeHp",
                    hpDamage,
                    0,
                    hpDamage,
                    string.IsNullOrWhiteSpace(frame.SourceId)
                        ? DamageAttributionConfidence.Unknown
                        : DamageAttributionConfidence.Exact);
            }

            PureHpFrames.RemoveAt(index);
        });
    }

    internal static void BeforeSetCurHp(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("before set hp", () =>
        {
            if (!CaptureEnabled || context.Target is not IStatusManager target)
            {
                return;
            }

            var pure = Session.FindPureFrameForTarget(target);
            if (pure == null)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordHpSetterHook();
            var frame = HpSetterFrames.Rent(Time.frameCount);
            frame.Target = target;
            frame.BeforeHp = SafeHp(target);
            frame.PureFrameId = pure.CallId;
            HpSetterFrames.Add(frame);
        });
    }

    internal static void AfterSetCurHp(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("after set hp", () =>
        {
            if (context.Target is not IStatusManager target)
            {
                return;
            }

            var setterIndex = -1;
            for (var i = HpSetterFrames.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(HpSetterFrames[i].Target, target))
                {
                    setterIndex = i;
                    break;
                }
            }

            if (setterIndex < 0)
            {
                return;
            }

            var setter = HpSetterFrames[setterIndex];
            var beforeHp = setter.BeforeHp;
            var pureFrameId = setter.PureFrameId;
            HpSetterFrames.RemoveAt(setterIndex);
            var pure = Session.FindPureFrameById(pureFrameId);
            var targetFrame = pure == null ? null : DamageCaptureSession.FindTargetFrame(pure, target);
            if (pure == null || targetFrame == null)
            {
                return;
            }

            targetFrame.Recorded = true;
            var hpDamage = Math.Max(0, beforeHp - SafeHp(target));
            if (hpDamage <= 0)
            {
                return;
            }

            SubmitDirectDamage(
                target,
                pure.Source,
                pure.SourceId,
                pure.SourceDataId,
                "PureChangeHp",
                hpDamage,
                0,
                hpDamage,
                string.IsNullOrWhiteSpace(pure.SourceId)
                    ? DamageAttributionConfidence.Unknown
                    : DamageAttributionConfidence.Exact);
        });
    }

    internal static void BeforeScriptAddBuff(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("before add buff", () =>
        {
            if (!CaptureEnabled || context.Target is not IScriptExecutor executor)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordBuffHook();
            var trackerId = BuffAttribution.BeginApplication(
                executor,
                ArgumentString(context.Arguments, 0),
                Time.frameCount);
            if (trackerId <= 0)
            {
                return;
            }

            var frame = BuffFrames.Rent(Time.frameCount);
            frame.Executor = executor;
            frame.TrackerId = trackerId;
            BuffFrames.Add(frame);
        });
    }

    internal static void AfterScriptAddBuff(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("after add buff", () =>
        {
            if (context.Target is not IScriptExecutor executor)
            {
                return;
            }

            var index = -1;
            for (var i = BuffFrames.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(BuffFrames[i].Executor, executor))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            var frame = BuffFrames[index];
            var trackerId = frame.TrackerId;
            BuffFrames.RemoveAt(index);
            BuffAttribution.CompleteApplication(trackerId);
        });
    }

    internal static void AfterDamageTextInternalExecute(ModHookContext context)
    {
        DamageMeterPerformanceCounters.RecordDamageTextExecuteHook();
    }

    internal static void AfterFightUiEnqueueDamageText(ModHookContext context)
    {
        DamageMeterPerformanceCounters.RecordDamageTextEnqueueHook();
    }

    internal static void BeforeStatusAddBuff(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("before status add buff", () =>
        {
            if (!CaptureEnabled || context.Target is not IStatusManager target)
            {
                return;
            }

            var buffId = StatusAddBuffId(context.Arguments);
            if (string.IsNullOrWhiteSpace(buffId))
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordBuffHook();
            Session.PruneFrames();
            var frame = StatusBuffFrames.Rent(Time.frameCount);
            frame.Target = target;
            frame.TargetId = SafeStatusId(target);
            frame.BuffId = buffId;
            frame.BeforeLevel = SafeBuffLevel(target, buffId);
            StatusBuffFrames.Add(frame);
        });
    }

    internal static void AfterStatusAddBuff(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("after status add buff", () =>
        {
            if (context.Target is not IStatusManager target)
            {
                return;
            }

            var buffId = StatusAddBuffId(context.Arguments);
            var index = Session.FindStatusBuffFrame(target, buffId);
            if (index < 0)
            {
                return;
            }

            var frame = StatusBuffFrames[index];
            var recordedBuffId = frame.BuffId;
            var beforeLevel = frame.BeforeLevel;
            StatusBuffFrames.RemoveAt(index);
            var added = Math.Max(0, SafeBuffLevel(target, recordedBuffId) - beforeLevel);
            if (added <= 0)
            {
                return;
            }

            BuffAttribution.RecordObservedApplication(
                target,
                recordedBuffId,
                added,
                Time.frameCount);
        });
    }

    internal static void AfterRemoveBuff(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("remove buff", () =>
        {
            if (context.Target is IStatusManager target)
            {
                BuffAttribution.RemoveBuff(target, ArgumentString(context.Arguments, 0));
            }
        });
    }

    internal static void AfterBuffLevelChanged(ModHookContext context)
    {
        DamageMeterHookAdapter.RunHook("buff level changed", () =>
        {
            if (context.Target is IBuffItemConfig config)
            {
                BuffAttribution.OnLevelChanged(
                    config,
                    ParseInt(ArgumentString(context.Arguments, 0)),
                    Time.frameCount);
            }
        });
    }

    internal static void AttachBuffBroadcastListener(ModHookContext? context)
    {
        DamageMeterHookAdapter.RunHook("buff broadcast listener", () =>
        {
            EventCenter.OnBroadcastEventWithParam -= BuffBroadcastListener;
            EventCenter.OnBroadcastEventWithParam += BuffBroadcastListener;
        });
    }

    internal static void DetachBuffBroadcastListener(ModHookContext? context)
    {
        try
        {
            EventCenter.OnBroadcastEventWithParam -= BuffBroadcastListener;
        }
        catch
        {
        }
    }

    internal static void OnBroadcastEventWithParam(string eventName, ISourceData param)
    {
        DamageMeterHookAdapter.RunHook("buff broadcast attribution", () =>
        {
            if (!CaptureEnabled
                || param is not AddBuffData data
                || string.IsNullOrWhiteSpace(eventName)
                || !eventName.StartsWith("AddBuff", StringComparison.Ordinal))
            {
                return;
            }

            BuffAttribution.ObserveBroadcast(
                data.dataId,
                data.fromId,
                data.toId,
                data.dataFromid,
                Time.frameCount);
        });
    }

    internal static void SubmitResolvedDamage(
        IStatusManager target,
        string sourceInstanceId,
        string sourceDataId,
        string damageType,
        int hpDamage,
        int shieldDamage,
        int finalDamage,
        DamageAttributionConfidence confidence)
    {
        var emittedBuffParts = BuffAttribution.EmitSplit(
            target,
            sourceDataId,
            hpDamage,
            shieldDamage,
            finalDamage,
            (partSourceId, partSourceName, partSourceTeam, partHp, partShield, partFinal, partConfidence) =>
            {
                SubmitDirectDamage(
                    target,
                    CombatantTeamResolver.ResolveStatus(partSourceId),
                    partSourceId,
                    sourceDataId,
                    damageType,
                    partHp,
                    partShield,
                    partFinal,
                    partConfidence,
                    partSourceName,
                    partSourceTeam);
            });
        if (emittedBuffParts)
        {
            return;
        }

        var unresolvedBuffOwner = DamageDetailResolver.IsBuff(sourceDataId)
                                  && (string.IsNullOrWhiteSpace(sourceInstanceId)
                                      || string.Equals(
                                          sourceInstanceId,
                                          SafeStatusId(target),
                                          StringComparison.Ordinal));
        if (unresolvedBuffOwner)
        {
            sourceInstanceId = "unknown";
        }

        var source = CombatantTeamResolver.ResolveStatus(sourceInstanceId);
        SubmitDirectDamage(
            target,
            source,
            sourceInstanceId,
            sourceDataId,
            damageType,
            hpDamage,
            shieldDamage,
            finalDamage,
            unresolvedBuffOwner || string.IsNullOrWhiteSpace(sourceInstanceId)
                ? DamageAttributionConfidence.Unknown
                : confidence);
    }

    internal static void SubmitDirectDamage(
        IStatusManager target,
        IStatusManager? source,
        string sourceInstanceId,
        string sourceDataId,
        string damageType,
        int hpDamage,
        int shieldDamage,
        int finalDamage,
        DamageAttributionConfidence confidence,
        string? sourceName = null,
        DamageTeam? sourceTeam = null)
    {
        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceInstanceId)
            ? "unknown"
            : sourceInstanceId.Trim();
        var damage = new DamageEvent
        {
            SourceInstanceId = normalizedSourceId,
            SourceDisplayName = string.IsNullOrWhiteSpace(sourceName)
                ? CombatantTeamResolver.DisplayName(source, normalizedSourceId)
                : sourceName!,
            SourceTeam = sourceTeam ?? CombatantTeamResolver.Resolve(source, normalizedSourceId),
            TargetInstanceId = SafeStatusId(target),
            SourceDataId = sourceDataId?.Trim() ?? "",
            DetailLabel = DamageDetailResolver.ResolveLabel(sourceDataId ?? "", damageType),
            DamageType = string.IsNullOrWhiteSpace(damageType) ? "Unknown" : damageType.Trim(),
            HpDamage = Math.Max(0, hpDamage),
            ShieldDamage = Math.Max(0, shieldDamage),
            FinalDamage = Math.Max(0, finalDamage),
            AttributionConfidence = confidence
        };
        DamageEventFactory.Normalize(damage);
        DamageMeterNetworkRuntime.Submit(damage);
    }

    internal static void ResetCaptureState()
    {
        Session.Reset();
    }

    internal static string ArgumentString(object[]? arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Length
            ? arguments[index]?.ToString() ?? ""
            : "";
    }

    internal static string StatusAddBuffId(object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0 || arguments[0] == null)
        {
            return "";
        }

        if (arguments[0] is string text)
        {
            return text.Trim();
        }

        if (arguments[0] is IBuffItemConfig config)
        {
            return config.BuffId?.Trim() ?? "";
        }

        if (arguments[0] is IDataConfig dataConfig)
        {
            return SafeDataId(dataConfig);
        }

        return "";
    }

    internal static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    internal static int SafeHp(IStatusManager status)
    {
        try
        {
            return status.CurHp;
        }
        catch
        {
            return 0;
        }
    }

    internal static int SafeBuffLevel(IStatusManager status, string buffId)
    {
        try
        {
            return status.GetBuff(buffId)?.buffConfig?.Level ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static int SafeDefend(IStatusManager status)
    {
        try
        {
            return status.Defend;
        }
        catch
        {
            return 0;
        }
    }

    internal static string SafeStatusId(IStatusManager? status)
    {
        try
        {
            return status?.InstanceId?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string SafeDataId(IDataConfig? dataConfig)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
            {
                return id?.Trim() ?? "";
            }
        }
        catch
        {
        }

        try
        {
            return dataConfig?.InstanceID?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

}
