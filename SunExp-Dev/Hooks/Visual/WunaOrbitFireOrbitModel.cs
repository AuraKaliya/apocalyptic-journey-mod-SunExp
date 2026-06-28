using System;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

internal static class WunaOrbitFireOrbitModel
{
    private const float Tau = Mathf.PI * 2f;
    private static readonly Vector2 BodyFocus = new(0.52f, 0.43f);

    internal static readonly OrbitRail[] Rails =
    {
        new(
            "outer-crown",
            new Vector2(0.5f, 0.42f),
            new Vector2(0.46f, 0.11f),
            0.46f,
            0.2f,
            0.36f,
            0.16f,
            0.023f,
            0.072f,
            0.76f,
            0.98f,
            0.06f,
            0.045f,
            0.008f,
            0.018f,
            0.95f,
            -0.55f,
            1.7f,
            0.018f),
        new(
            "middle-orbit",
            new Vector2(0.52f, 0.41f),
            new Vector2(0.37f, 0.085f),
            0.39f,
            2.45f,
            -0.28f,
            0.13f,
            0.021f,
            0.064f,
            0.84f,
            0.95f,
            0.045f,
            0.035f,
            0.007f,
            0.016f,
            1.28f,
            0.46f,
            2.2f,
            0.02f),
        new(
            "inner-wick",
            new Vector2(0.53f, 0.43f),
            new Vector2(0.27f, 0.065f),
            0.31f,
            4.1f,
            0.44f,
            0.1f,
            0.018f,
            0.052f,
            0.7f,
            0.9f,
            0.035f,
            0.025f,
            0.006f,
            0.014f,
            1.65f,
            -0.38f,
            2.8f,
            0.016f)
    };

    internal static OrbitSample Sample(Bounds bounds, OrbitRail rail, float time, float actionPulse, float orbit01)
    {
        orbit01 = Mathf.Repeat(orbit01, 1f);
        var angle = orbit01 * Tau + rail.Phase + time * rail.Speed + actionPulse * 0.24f * rail.Direction;
        var center = Center(bounds, rail, time, actionPulse);
        var radiusPulse = 1f + actionPulse * rail.ActionSpreadScale;
        var radiusX = bounds.size.x * rail.Radius.x * radiusPulse;
        var radiusY = bounds.size.y * rail.Radius.y * Mathf.Lerp(1f, 1.08f, actionPulse);
        var radiusZ = bounds.size.x * rail.DepthRadiusScale * Mathf.Lerp(1f, 1.18f, actionPulse);
        var cos = Mathf.Cos(angle);
        var sin = Mathf.Sin(angle);

        var verticalWave =
            Mathf.Sin(angle * rail.BodyWaveCycles + time * rail.WaveSpeed + rail.Phase) * rail.WaveAmplitude
            + Mathf.Sin(angle * 3.5f - time * 1.12f + rail.Phase) * rail.FlickerAmplitude;

        var local3 = new Vector3(
            cos * radiusX,
            sin * radiusY + cos * radiusY * rail.PlaneTilt + bounds.size.y * verticalWave,
            sin * radiusZ);

        var depth = radiusZ <= 0.001f ? 0f : Mathf.Clamp(local3.z / radiusZ, -1f, 1f);
        var perspective = 1f + depth * (0.055f + actionPulse * 0.035f);
        var projected = new Vector2(
            center.x + local3.x * perspective + local3.z * rail.DepthSkew,
            center.y + local3.y * perspective + depth * bounds.size.y * rail.PerspectiveLift);

        var normalAngle = angle + Mathf.PI * 0.5f;
        var heatFlutter = Mathf.Sin(time * 2.1f + angle * 4.7f + rail.Phase) * bounds.size.x * rail.FlickerAmplitude * 0.55f;
        projected += new Vector2(Mathf.Cos(normalAngle), Mathf.Sin(normalAngle) * 0.28f) * heatFlutter;
        return new OrbitSample(projected, depth);
    }

    internal static Vector2 Tangent(Bounds bounds, OrbitRail rail, float time, float actionPulse, float orbit01)
    {
        var before = Sample(bounds, rail, time, actionPulse, orbit01 - 0.006f);
        var after = Sample(bounds, rail, time, actionPulse, orbit01 + 0.006f);
        var tangent = (after.Position - before.Position).normalized;
        return tangent.sqrMagnitude < 0.001f ? Vector2.right : tangent;
    }

    private static Vector2 Center(Bounds bounds, OrbitRail rail, float time, float actionPulse)
    {
        var bodyCenter = new Vector2(
            bounds.min.x + bounds.size.x * BodyFocus.x,
            bounds.min.y + bounds.size.y * BodyFocus.y);
        var anchorCenter = new Vector2(
            bounds.min.x + bounds.size.x * rail.Anchor.x,
            bounds.min.y + bounds.size.y * rail.Anchor.y);
        var drift = new Vector2(
            Mathf.Cos(time * 0.38f + rail.Phase) * bounds.size.x * rail.CenterDriftScale,
            Mathf.Sin(time * 0.31f + rail.Phase * 0.7f) * bounds.size.y * rail.CenterDriftScale * 0.65f);
        return Vector2.Lerp(bodyCenter, anchorCenter + drift, 0.54f + actionPulse * 0.08f);
    }

    internal readonly struct OrbitRail
    {
        public OrbitRail(
            string name,
            Vector2 anchor,
            Vector2 radius,
            float depthRadiusScale,
            float phase,
            float speed,
            float perspectiveLift,
            float coreWidthScale,
            float tongueWidthScale,
            float alphaScale,
            float occlusionStrength,
            float actionSpreadScale,
            float centerDriftScale,
            float waveAmplitude,
            float flickerAmplitude,
            float bodyWaveCycles,
            float planeTilt,
            float waveSpeed,
            float depthSkew)
        {
            Name = name;
            Anchor = anchor;
            Radius = radius;
            DepthRadiusScale = depthRadiusScale;
            Phase = phase;
            Speed = speed;
            PerspectiveLift = perspectiveLift;
            CoreWidthScale = coreWidthScale;
            TongueWidthScale = tongueWidthScale;
            TongueLengthScale = tongueWidthScale * 2.35f;
            AlphaScale = alphaScale;
            OcclusionStrength = occlusionStrength;
            ActionSpreadScale = actionSpreadScale;
            CenterDriftScale = centerDriftScale;
            WaveAmplitude = waveAmplitude;
            FlickerAmplitude = flickerAmplitude;
            BodyWaveCycles = bodyWaveCycles;
            PlaneTilt = planeTilt;
            WaveSpeed = waveSpeed;
            DepthSkew = depthSkew;
            Direction = Math.Sign(speed == 0f ? 1f : speed);
            NoiseSeed = 17.13f + phase * 3.71f;
        }

        public string Name { get; }

        public Vector2 Anchor { get; }

        public Vector2 Radius { get; }

        public float DepthRadiusScale { get; }

        public float Phase { get; }

        public float Speed { get; }

        public float PerspectiveLift { get; }

        public float CoreWidthScale { get; }

        public float TongueWidthScale { get; }

        public float TongueLengthScale { get; }

        public float AlphaScale { get; }

        public float OcclusionStrength { get; }

        public float ActionSpreadScale { get; }

        public float CenterDriftScale { get; }

        public float WaveAmplitude { get; }

        public float FlickerAmplitude { get; }

        public float BodyWaveCycles { get; }

        public float PlaneTilt { get; }

        public float WaveSpeed { get; }

        public float DepthSkew { get; }

        public float Direction { get; }

        public float NoiseSeed { get; }
    }

    internal readonly struct OrbitSample
    {
        public OrbitSample(Vector2 position, float depth)
        {
            Position = position;
            Depth = depth;
        }

        public Vector2 Position { get; }

        public float Depth { get; }
    }
}
