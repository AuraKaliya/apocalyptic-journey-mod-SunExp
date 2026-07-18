using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using UnityEngine;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal sealed class DamageCaptureSession
{
    private const int MaxTargetFrameListPool = 32;
    private const int MaxTargetFramePool = 256;
    internal static readonly List<TargetHpFrame> EmptyTargetFrames = new();

    private readonly Stack<List<TargetHpFrame>> targetFrameListPool = new();
    private readonly Stack<TargetHpFrame> targetFramePool = new();
    private readonly Dictionary<Type, DamageTextAccessor> damageTextAccessors = new();
    private long nextCallId;
    private int lastPruneFrame = -1;

    internal DamageCaptureSession()
    {
        PureHpFrames = new DamageFrameWindow<PureHpFrame>(128, ReleasePureFrameTargets);
    }

    internal DamageFrameWindow<HitFrame> HitFrames { get; } = new(256);
    internal DamageFrameWindow<PureHpFrame> PureHpFrames { get; }
    internal DamageFrameWindow<HpSetterFrame> HpSetterFrames { get; } = new(128);
    internal DamageFrameWindow<BuffApplicationFrame> BuffFrames { get; } = new(128);
    internal DamageFrameWindow<StatusBuffFrame> StatusBuffFrames { get; } = new(128);
    internal long NextCallId() => ++nextCallId;

    internal int FindHitFrame(DamageTextInfo data)
    {
        for (var i = HitFrames.Count - 1; i >= 0; i--)
        {
            var frame = HitFrames[i];
            if (!DamageCaptureMatchingPolicy.IsHitMatch(
                    frame.TargetId,
                    frame.SourceInstanceId,
                    data.To,
                    data.From))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    internal PureHpFrame? FindPureFrameForTarget(IStatusManager target)
    {
        for (var i = PureHpFrames.Count - 1; i >= 0; i--)
        {
            var frame = PureHpFrames[i];
            for (var j = 0; j < frame.Targets.Count; j++)
            {
                var item = frame.Targets[j];
                if (!item.Recorded && ReferenceEquals(item.Target, target))
                {
                    return frame;
                }
            }
        }

        return null;
    }

    internal PureHpFrame? FindPureFrameById(long callId)
    {
        for (var i = PureHpFrames.Count - 1; i >= 0; i--)
        {
            if (PureHpFrames[i].CallId == callId)
            {
                return PureHpFrames[i];
            }
        }

        return null;
    }

    internal static TargetHpFrame? FindTargetFrame(PureHpFrame frame, IStatusManager target)
    {
        for (var i = 0; i < frame.Targets.Count; i++)
        {
            var item = frame.Targets[i];
            if (ReferenceEquals(item.Target, target))
            {
                return item;
            }
        }

        return null;
    }

    internal bool TryReadDamageText(object? value, out DamageTextInfo data)
    {
        data = new DamageTextInfo();
        if (value == null)
        {
            return false;
        }

        try
        {
            var accessor = GetDamageTextAccessor(value.GetType());
            data.From = accessor.ReadString(value, "from");
            data.To = accessor.ReadString(value, "to");
            data.DamageType = accessor.ReadString(value, "damageType");
            data.Hit = accessor.ReadInt(value, "hit");
            return !string.IsNullOrWhiteSpace(data.To);
        }
        catch
        {
            return false;
        }
    }

    internal void PruneFrames(Action<long> cancelBuffApplication)
    {
        var frame = Time.frameCount;
        if (lastPruneFrame == frame)
        {
            return;
        }

        lastPruneFrame = frame;
        HitFrames.PruneOlderThan(frame, 4);
        PureHpFrames.PruneOlderThan(frame, 4);
        HpSetterFrames.PruneOlderThan(frame, 4);
        StatusBuffFrames.PruneOlderThan(frame, 4);

        for (var i = BuffFrames.Count - 1; i >= 0; i--)
        {
            if (frame - BuffFrames[i].Frame <= 4)
            {
                continue;
            }

            cancelBuffApplication(BuffFrames[i].TrackerId);
            BuffFrames.RemoveAt(i);
        }
    }

    internal void Reset()
    {
        HitFrames.Clear();
        PureHpFrames.Clear();
        HpSetterFrames.Clear();
        BuffFrames.Clear();
        StatusBuffFrames.Clear();
        nextCallId = 0;
        lastPruneFrame = -1;
    }

    internal List<TargetHpFrame> CaptureTargetHpFrames(IScriptExecutor executor)
    {
        var frames = RentTargetFrameList();
        foreach (var target in ResolveTargets(executor))
        {
            if (target == null || ContainsTarget(frames, target))
            {
                continue;
            }

            var frame = RentTargetFrame();
            frame.Target = target;
            frame.BeforeHp = DamageCaptureHostReader.SafeHp(target);
            frames.Add(frame);
        }

        return frames;
    }

    internal void ReleaseTargetFrameList(List<TargetHpFrame>? frames)
    {
        if (frames == null || ReferenceEquals(frames, EmptyTargetFrames))
        {
            return;
        }

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var frame = frames[i];
            frame.Reset();
            if (targetFramePool.Count < MaxTargetFramePool)
            {
                targetFramePool.Push(frame);
            }
        }

        frames.Clear();
        if (targetFrameListPool.Count < MaxTargetFrameListPool)
        {
            targetFrameListPool.Push(frames);
        }
    }

    internal int FindStatusBuffFrame(IStatusManager target, string buffId)
    {
        buffId = buffId?.Trim() ?? "";
        var targetId = DamageCaptureHostReader.SafeStatusId(target);
        for (var i = StatusBuffFrames.Count - 1; i >= 0; i--)
        {
            var frame = StatusBuffFrames[i];
            if (!string.IsNullOrWhiteSpace(buffId)
                && !string.Equals(frame.BuffId, buffId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ReferenceEquals(frame.Target, target)
                || !string.IsNullOrWhiteSpace(targetId)
                && string.Equals(frame.TargetId, targetId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private DamageTextAccessor GetDamageTextAccessor(Type type)
    {
        if (!damageTextAccessors.TryGetValue(type, out var accessor))
        {
            accessor = new DamageTextAccessor(type);
            damageTextAccessors[type] = accessor;
        }

        return accessor;
    }

    private static IEnumerable<IStatusManager> ResolveTargets(IScriptExecutor executor)
    {
        if (executor.Object != null && executor.Object.Count > 0)
        {
            foreach (var target in executor.Object)
            {
                if (target != null)
                {
                    yield return target;
                }
            }

            yield break;
        }

        if (executor.status != null)
        {
            yield return executor.status;
            yield break;
        }

        if (executor.Target != null)
        {
            yield return executor.Target;
        }
    }

    private List<TargetHpFrame> RentTargetFrameList()
    {
        return targetFrameListPool.Count > 0
            ? targetFrameListPool.Pop()
            : new List<TargetHpFrame>(4);
    }

    private TargetHpFrame RentTargetFrame()
    {
        return targetFramePool.Count > 0 ? targetFramePool.Pop() : new TargetHpFrame();
    }

    private void ReleasePureFrameTargets(PureHpFrame frame)
    {
        ReleaseTargetFrameList(frame.Targets);
        frame.Targets = EmptyTargetFrames;
    }

    private static bool ContainsTarget(List<TargetHpFrame> frames, IStatusManager target)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            if (ReferenceEquals(frames[i].Target, target))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class HitFrame : IDamageCaptureFrame
{
    public long CallId { get; set; }
    public int Frame { get; set; }
    public IStatusManager Target { get; set; } = null!;
    public string TargetId { get; set; } = "";
    public int BeforeHp { get; set; }
    public int BeforeShield { get; set; }
    public string DamageType { get; set; } = "";
    public string SourceDataId { get; set; } = "";
    public string SourceInstanceId { get; set; } = "";

    public void Reset()
    {
        CallId = 0;
        Frame = 0;
        Target = null!;
        TargetId = "";
        BeforeHp = 0;
        BeforeShield = 0;
        DamageType = "";
        SourceDataId = "";
        SourceInstanceId = "";
    }
}

internal sealed class DamageTextAccessor
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<string, MemberInfo?> members = new(StringComparer.Ordinal);

    internal DamageTextAccessor(Type type)
    {
        members["from"] = FindMember(type, "from");
        members["to"] = FindMember(type, "to");
        members["damageType"] = FindMember(type, "damageType");
        members["hit"] = FindMember(type, "hit");
    }

    internal string ReadString(object source, string name) => Read(source, name)?.ToString() ?? "";

    internal int ReadInt(object source, string name)
    {
        var value = Read(source, name);
        if (value is int typed)
        {
            return typed;
        }

        return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private object? Read(object source, string name)
    {
        return members.TryGetValue(name, out var member)
            ? member switch
            {
                PropertyInfo property => property.GetValue(source),
                FieldInfo field => field.GetValue(source),
                _ => null
            }
            : null;
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        return type.GetProperty(name, Flags) ?? (MemberInfo?)type.GetField(name, Flags);
    }
}

internal sealed class PureHpFrame : IDamageCaptureFrame
{
    public long CallId { get; set; }
    public int Frame { get; set; }
    public IScriptExecutor Executor { get; set; } = null!;
    public IStatusManager? Source { get; set; }
    public string SourceId { get; set; } = "";
    public string SourceDataId { get; set; } = "";
    public List<TargetHpFrame> Targets { get; set; } = new();

    public void Reset()
    {
        CallId = 0;
        Frame = 0;
        Executor = null!;
        Source = null;
        SourceId = "";
        SourceDataId = "";
        Targets = DamageCaptureSession.EmptyTargetFrames;
    }
}

internal sealed class TargetHpFrame
{
    public IStatusManager Target { get; set; } = null!;
    public int BeforeHp { get; set; }
    public bool Recorded { get; set; }

    public void Reset()
    {
        Target = null!;
        BeforeHp = 0;
        Recorded = false;
    }
}

internal sealed class BuffApplicationFrame : IDamageCaptureFrame
{
    public int Frame { get; set; }
    public IScriptExecutor Executor { get; set; } = null!;
    public long TrackerId { get; set; }

    public void Reset()
    {
        Frame = 0;
        Executor = null!;
        TrackerId = 0;
    }
}

internal sealed class StatusBuffFrame : IDamageCaptureFrame
{
    public int Frame { get; set; }
    public IStatusManager Target { get; set; } = null!;
    public string TargetId { get; set; } = "";
    public string BuffId { get; set; } = "";
    public int BeforeLevel { get; set; }

    public void Reset()
    {
        Frame = 0;
        Target = null!;
        TargetId = "";
        BuffId = "";
        BeforeLevel = 0;
    }
}

internal sealed class DamageTextInfo
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int Hit { get; set; }
    public string DamageType { get; set; } = "";
}

internal sealed class HpSetterFrame : IDamageCaptureFrame
{
    public int Frame { get; set; }
    public IStatusManager Target { get; set; } = null!;
    public int BeforeHp { get; set; }
    public long PureFrameId { get; set; }

    public void Reset()
    {
        Frame = 0;
        Target = null!;
        BeforeHp = 0;
        PureFrameId = 0;
    }
}
