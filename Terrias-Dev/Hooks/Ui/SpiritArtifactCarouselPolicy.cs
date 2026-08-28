using System;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Hooks.Ui;

internal readonly struct SpiritArtifactCarouselGeometry
{
    public SpiritArtifactCarouselGeometry(
        float slotSize,
        float radius,
        float portraitWidth,
        float portraitHeight)
    {
        SlotSize = slotSize;
        Radius = radius;
        PortraitWidth = portraitWidth;
        PortraitHeight = portraitHeight;
    }

    public float SlotSize { get; }
    public float Radius { get; }
    public float PortraitWidth { get; }
    public float PortraitHeight { get; }
}

internal readonly struct SpiritArtifactCarouselPoint
{
    public SpiritArtifactCarouselPoint(
        float circleX,
        float circleY,
        float x,
        float y,
        float depth,
        float scale,
        float alpha)
    {
        CircleX = circleX;
        CircleY = circleY;
        X = x;
        Y = y;
        Depth = depth;
        Scale = scale;
        Alpha = alpha;
    }

    public float CircleX { get; }
    public float CircleY { get; }
    public float X { get; }
    public float Y { get; }
    public float Depth { get; }
    public float Scale { get; }
    public float Alpha { get; }
}

internal readonly struct SpiritArtifactAutomaticMotionSample
{
    public SpiritArtifactAutomaticMotionSample(float phaseDegrees, bool moving, bool cycleComplete)
    {
        PhaseDegrees = phaseDegrees;
        Moving = moving;
        CycleComplete = cycleComplete;
    }

    public float PhaseDegrees { get; }
    public bool Moving { get; }
    public bool CycleComplete { get; }
}

internal static class SpiritArtifactCarouselPolicy
{
    public const float BoundaryMargin = 10f;
    public const float ForwardTiltDegrees = 15f;
    public const float AutomaticDwellSeconds = 3.2f;
    public const float AutomaticTransitionSeconds = 0.8f;
    public const float AutomaticStepDegrees = 72f;
    public const float FocusAngleDegrees = -90f;
    public const float BackScale = 0.96f;
    public const float FrontScale = 1.04f;
    public const float BackAlpha = 0.78f;
    public const float FrontAlpha = 1f;
    public const float MinimumFocusSeconds = 0.28f;
    public const float MaximumFocusSeconds = 0.48f;

    private const float MinimumWidth = 240f;
    private const float MinimumHeight = 180f;

    public static SpiritArtifactCarouselGeometry CalculateGeometry(float width, float height)
    {
        width = Math.Max(MinimumWidth, width);
        height = Math.Max(MinimumHeight, height);
        var slotSize = Clamp(
            Math.Min(width, height) * 0.22f,
            SpiritArtifactCardStylePolicy.EquipmentSlotSize,
            68f);
        var tiltCosine = (float)Math.Cos(ForwardTiltDegrees * Math.PI / 180d);
        var verticalRadius = Math.Max(
            1f,
            (height - slotSize - BoundaryMargin * 2f) / Math.Max(0.01f, tiltCosine * 2f));
        var horizontalRadius = Math.Max(
            1f,
            (width - slotSize - BoundaryMargin * 2f) * 0.5f);
        var radius = Math.Min(verticalRadius, horizontalRadius);
        return new SpiritArtifactCarouselGeometry(
            slotSize,
            radius,
            Math.Min(width * 0.46f, radius * 1.45f),
            Math.Min(height * 0.74f, radius * 1.82f));
    }

    public static SpiritArtifactCarouselPoint CalculatePoint(
        SpiritArtifactCarouselGeometry geometry,
        string slotId,
        float phaseDegrees)
    {
        var angle = (BaseAngleDegrees(slotId) + phaseDegrees) * Math.PI / 180d;
        var cosine = (float)Math.Cos(angle);
        var sine = (float)Math.Sin(angle);
        var circleX = cosine * geometry.Radius;
        var circleY = sine * geometry.Radius;
        var tiltRadians = ForwardTiltDegrees * Math.PI / 180d;
        var tiltCosine = (float)Math.Cos(tiltRadians);
        var tiltSine = (float)Math.Sin(tiltRadians);
        var depth = -circleY * tiltSine;
        var depth01 = Clamp((-sine + 1f) * 0.5f, 0f, 1f);
        return new SpiritArtifactCarouselPoint(
            circleX,
            circleY,
            circleX,
            circleY * tiltCosine,
            depth,
            Lerp(BackScale, FrontScale, depth01),
            Lerp(BackAlpha, FrontAlpha, depth01));
    }

    public static float AutomaticCycleSeconds
        => AutomaticDwellSeconds + AutomaticTransitionSeconds;

    public static SpiritArtifactAutomaticMotionSample SampleAutomaticMotion(
        float originPhaseDegrees,
        float elapsedSeconds)
    {
        elapsedSeconds = Math.Max(0f, elapsedSeconds);
        if (elapsedSeconds <= AutomaticDwellSeconds)
            return new SpiritArtifactAutomaticMotionSample(
                Normalize360(originPhaseDegrees),
                moving: false,
                cycleComplete: false);
        var progress = (elapsedSeconds - AutomaticDwellSeconds) / AutomaticTransitionSeconds;
        if (progress >= 0.99999f)
            return new SpiritArtifactAutomaticMotionSample(
                Normalize360(originPhaseDegrees - AutomaticStepDegrees),
                moving: false,
                cycleComplete: true);
        var eased = EaseInOutCubic(progress);
        return new SpiritArtifactAutomaticMotionSample(
            Normalize360(originPhaseDegrees - AutomaticStepDegrees * eased),
            moving: true,
            cycleComplete: false);
    }

    public static float FocusTargetPhase(string slotId)
        => Normalize360(FocusAngleDegrees - BaseAngleDegrees(slotId));

    public static float ShortestFocusDelta(float currentPhaseDegrees, string slotId)
        => DeltaAngle(currentPhaseDegrees, FocusTargetPhase(slotId));

    public static float FocusDuration(float angularDelta)
    {
        var progress = Clamp(Math.Abs(angularDelta) / 180f, 0f, 1f);
        return Lerp(MinimumFocusSeconds, MaximumFocusSeconds, progress);
    }

    public static float EaseOutCubic(float progress)
    {
        progress = Clamp(progress, 0f, 1f);
        var inverse = 1f - progress;
        return 1f - inverse * inverse * inverse;
    }

    public static float EaseInOutCubic(float progress)
    {
        progress = Clamp(progress, 0f, 1f);
        if (progress < 0.5f) return 4f * progress * progress * progress;
        var inverse = -2f * progress + 2f;
        return 1f - inverse * inverse * inverse * 0.5f;
    }

    public static float BaseAngleDegrees(string slotId)
    {
        return SpiritArtifactSlots.Normalize(slotId) switch
        {
            SpiritArtifactSlots.Flower => 90f,
            SpiritArtifactSlots.Plume => 162f,
            SpiritArtifactSlots.Sands => 18f,
            SpiritArtifactSlots.Goblet => 234f,
            SpiritArtifactSlots.Circlet => 306f,
            _ => 90f
        };
    }

    public static float Normalize360(float degrees)
    {
        var value = degrees % 360f;
        return value < 0f ? value + 360f : value;
    }

    private static float DeltaAngle(float current, float target)
    {
        var delta = Normalize360(target - current + 180f) - 180f;
        return delta;
    }

    private static float Lerp(float left, float right, float progress)
        => left + (right - left) * progress;

    private static float Clamp(float value, float minimum, float maximum)
        => Math.Max(minimum, Math.Min(maximum, value));
}
