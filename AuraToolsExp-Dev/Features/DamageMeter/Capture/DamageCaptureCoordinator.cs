using System;
using System.Collections.Generic;
using System.Reflection;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Resolution;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using UnityEngine;
using Witch;
using Witch.Core;
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
    private static readonly BuffAttributionEngine BuffAttribution = new();
    private static readonly Action<string, ISourceData> BuffBroadcastListener = OnBroadcastEventWithParam;
    private static bool CaptureEnabled => AuraToolsDamageMeterRuntime.Ledger.InFight && AuraToolsDamageMeterRuntime.Ledger.SharedEnabled;

    internal static void BeforeHit(HitHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("before hit", () =>
        {
            if (!CaptureEnabled)
            {
                return;
            }

            var target = observation.Target;
            DamageMeterPerformanceCounters.RecordHitHook();
            Session.PruneFrames(BuffAttribution.CancelApplication);
            var frame = HitFrames.Rent(Time.frameCount);
            frame.CallId = Session.NextCallId();
            frame.Target = target;
            frame.TargetId = DamageCaptureHostReader.SafeStatusId(target);
            frame.BeforeHp = DamageCaptureHostReader.SafeHp(target);
            frame.BeforeShield = DamageCaptureHostReader.SafeDefend(target);
            frame.DamageType = observation.DamageType;
            frame.SourceDataId = observation.SourceDataId;
            frame.SourceInstanceId = observation.SourceInstanceId;
            HitFrames.Add(frame);
        });
    }

    internal static void AfterDamageTextCreate(DamageTextHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("damage text", () =>
        {
            DamageMeterPerformanceCounters.RecordDamageTextCreateHook();
            if (!CaptureEnabled
                || !Session.TryReadDamageText(observation.Value, out var data))
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
            var hpDamage = DamageCaptureMatchingPolicy.Loss(frame.BeforeHp, DamageCaptureHostReader.SafeHp(target));
            var shieldDamage = DamageCaptureMatchingPolicy.Loss(frame.BeforeShield, DamageCaptureHostReader.SafeDefend(target));
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

    internal static void AfterHit(StatusHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("after hit", () =>
        {
            var target = observation.Target;
            var targetId = DamageCaptureHostReader.SafeStatusId(target);
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

    internal static void BeforePureChangeHp(PureHpHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("before pure hp", () =>
        {
            if (!CaptureEnabled
                || observation.Delta >= 0)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordPureHpHook();
            var executor = observation.Executor;
            Session.PruneFrames(BuffAttribution.CancelApplication);
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
            frame.SourceId = DamageCaptureHostReader.SafeStatusId(source);
            frame.SourceDataId = DamageCaptureHostReader.SafeDataId(executor.dataConfig);
            frame.Targets = targets;
            PureHpFrames.Add(frame);
        });
    }

    internal static void AfterPureChangeHp(PureHpHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("after pure hp", () =>
        {
            var executor = observation.Executor;
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

                var hpDamage = Math.Max(0, target.BeforeHp - DamageCaptureHostReader.SafeHp(target.Target));
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

    internal static void BeforeSetCurHp(StatusHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("before set hp", () =>
        {
            if (!CaptureEnabled)
            {
                return;
            }

            var target = observation.Target;
            var pure = Session.FindPureFrameForTarget(target);
            if (pure == null)
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordHpSetterHook();
            var frame = HpSetterFrames.Rent(Time.frameCount);
            frame.Target = target;
            frame.BeforeHp = DamageCaptureHostReader.SafeHp(target);
            frame.PureFrameId = pure.CallId;
            HpSetterFrames.Add(frame);
        });
    }

    internal static void AfterSetCurHp(StatusHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("after set hp", () =>
        {
            var target = observation.Target;
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
            var hpDamage = Math.Max(0, beforeHp - DamageCaptureHostReader.SafeHp(target));
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

    internal static void BeforeScriptAddBuff(ScriptBuffHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("before add buff", () =>
        {
            if (!CaptureEnabled)
            {
                return;
            }

            var executor = observation.Executor;
            DamageMeterPerformanceCounters.RecordBuffHook();
            var trackerId = BuffAttribution.BeginApplication(
                executor,
                observation.BuffId,
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

    internal static void AfterScriptAddBuff(ScriptBuffHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("after add buff", () =>
        {
            var executor = observation.Executor;
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

    internal static void AfterDamageTextInternalExecute()
    {
        DamageMeterPerformanceCounters.RecordDamageTextExecuteHook();
    }

    internal static void AfterFightUiEnqueueDamageText()
    {
        DamageMeterPerformanceCounters.RecordDamageTextEnqueueHook();
    }

    internal static void BeforeStatusAddBuff(StatusBuffHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("before status add buff", () =>
        {
            if (!CaptureEnabled)
            {
                return;
            }

            var target = observation.Target;
            var buffId = observation.BuffId;
            if (string.IsNullOrWhiteSpace(buffId))
            {
                return;
            }

            DamageMeterPerformanceCounters.RecordBuffHook();
            Session.PruneFrames(BuffAttribution.CancelApplication);
            var frame = StatusBuffFrames.Rent(Time.frameCount);
            frame.Target = target;
            frame.TargetId = DamageCaptureHostReader.SafeStatusId(target);
            frame.BuffId = buffId;
            frame.BeforeLevel = DamageCaptureHostReader.SafeBuffLevel(target, buffId);
            StatusBuffFrames.Add(frame);
        });
    }

    internal static void AfterStatusAddBuff(StatusBuffHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("after status add buff", () =>
        {
            var target = observation.Target;
            var buffId = observation.BuffId;
            var index = Session.FindStatusBuffFrame(target, buffId);
            if (index < 0)
            {
                return;
            }

            var frame = StatusBuffFrames[index];
            var recordedBuffId = frame.BuffId;
            var beforeLevel = frame.BeforeLevel;
            StatusBuffFrames.RemoveAt(index);
            var added = Math.Max(0, DamageCaptureHostReader.SafeBuffLevel(target, recordedBuffId) - beforeLevel);
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

    internal static void AfterRemoveBuff(StatusBuffHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("remove buff", () =>
        {
            BuffAttribution.RemoveBuff(observation.Target, observation.BuffId);
        });
    }

    internal static void AfterBuffLevelChanged(BuffLevelHookObservation observation)
    {
        DamageMeterHookAdapter.RunHook("buff level changed", () =>
        {
            BuffAttribution.OnLevelChanged(observation.Config, observation.Level, Time.frameCount);
        });
    }

    internal static void AttachBuffBroadcastListener()
    {
        DamageMeterHookAdapter.RunHook("buff broadcast listener", () =>
        {
            EventCenter.OnBroadcastEventWithParam -= BuffBroadcastListener;
            EventCenter.OnBroadcastEventWithParam += BuffBroadcastListener;
        });
    }

    internal static void DetachBuffBroadcastListener()
    {
        try
        {
            EventCenter.OnBroadcastEventWithParam -= BuffBroadcastListener;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] buff broadcast listener release failed: " + ex.Message);
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
                                          DamageCaptureHostReader.SafeStatusId(target),
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
        var damage = DamageEventFactory.Create(new ResolvedDamageInput
        {
            SourceInstanceId = normalizedSourceId,
            SourceDisplayName = string.IsNullOrWhiteSpace(sourceName)
                ? CombatantTeamResolver.DisplayName(source, normalizedSourceId)
                : sourceName!,
            SourceTeam = sourceTeam ?? CombatantTeamResolver.Resolve(source, normalizedSourceId),
            TargetInstanceId = DamageCaptureHostReader.SafeStatusId(target),
            SourceDataId = sourceDataId?.Trim() ?? "",
            DetailLabel = DamageDetailResolver.ResolveLabel(sourceDataId ?? "", damageType),
            DamageType = string.IsNullOrWhiteSpace(damageType) ? "Unknown" : damageType.Trim(),
            HpDamage = hpDamage,
            ShieldDamage = shieldDamage,
            FinalDamage = finalDamage,
            AttributionConfidence = confidence
        });
        DamageMeterNetworkRuntime.Submit(damage);
    }

    internal static void ResetSession()
    {
        Session.Reset();
    }

    internal static void ResetAttribution()
    {
        BuffAttribution.Clear();
    }

}
