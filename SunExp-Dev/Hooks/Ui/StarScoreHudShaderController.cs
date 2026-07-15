using System;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class StarScoreHudShaderController : MonoBehaviour
{
    private const float LightFadeSeconds = 0.22f;
    private const float CadencePulseSeconds = 0.42f;
    private const float SlotPulseSeconds = 0.18f;
    private static readonly Color DimFrameColor = new(0.34f, 0.35f, 0.43f, 1f);
    private static readonly Color LitColor = Color.white;
    private static readonly Color PulseColor = new(1f, 0.86f, 0.44f, 1f);

    private readonly CanvasGroup[] litGroups = new CanvasGroup[3];
    private readonly Image[] litImages = new Image[3];
    private readonly Material?[] litMaterials = new Material?[3];
    private readonly float[] currentLit = new float[3];
    private readonly float[] targetLit = new float[3];
    private readonly float[] slotPulseUntil = new float[3];
    private readonly float[] slotPulseStrength = new float[3];

    private Image? dimFrame;
    private float pulseUntil;
    private float flowTime;

    public void Configure(Image frame, CanvasGroup[] groups, Image[] images)
    {
        dimFrame = frame;
        dimFrame.color = DimFrameColor;

        for (var i = 0; i < litGroups.Length; i++)
        {
            litGroups[i] = groups.Length > i ? groups[i] : null!;
            litImages[i] = images.Length > i ? images[i] : null!;
            litMaterials[i] = StarScoreHudShaderMaterials.CreateLitMaterial(i);
            if (litMaterials[i] != null && litImages[i] != null)
            {
                litImages[i].material = litMaterials[i];
            }
        }

        ApplyVisuals(0f);
    }

    public void ApplySnapshot(StarScoreDisplaySnapshot snapshot)
    {
        enabled = true;
        var noteCount = Math.Min(3, Math.Max(0, snapshot.Notes.Count));
        for (var i = 0; i < targetLit.Length; i++)
        {
            targetLit[i] = i < noteCount ? 1f : 0f;
        }

        if (snapshot.IsCadencePreview && noteCount >= 3)
        {
            pulseUntil = Time.unscaledTime + CadencePulseSeconds;
            for (var i = 0; i < targetLit.Length; i++)
            {
                targetLit[i] = 1f;
            }
        }

        if (!snapshot.HasNotes)
        {
            pulseUntil = 0f;
            for (var i = 0; i < targetLit.Length; i++)
            {
                targetLit[i] = 0f;
                currentLit[i] = 0f;
            }

            ApplyVisuals(0f);
            enabled = false;
        }
    }

    public void PulseSlot(int slotIndex, float strength)
    {
        if (slotIndex < 0 || slotIndex >= slotPulseUntil.Length)
        {
            return;
        }

        slotPulseUntil[slotIndex] = Time.unscaledTime + SlotPulseSeconds;
        slotPulseStrength[slotIndex] = Mathf.Clamp(strength, 1f, 2.5f);
        enabled = true;
    }

    private void Update()
    {
        var delta = Mathf.Max(Time.unscaledDeltaTime, 0f);
        if (ShouldAnimateFlow())
        {
            flowTime += delta;
        }

        var speed = LightFadeSeconds <= 0f ? 1f : delta / LightFadeSeconds;
        for (var i = 0; i < currentLit.Length; i++)
        {
            currentLit[i] = Mathf.MoveTowards(currentLit[i], targetLit[i], speed);
        }

        var pulse = PulseAmount();
        ApplyVisuals(pulse);
        if (IsStable(pulse))
        {
            enabled = false;
        }
    }

    private float PulseAmount()
    {
        if (pulseUntil <= Time.unscaledTime)
        {
            return 0f;
        }

        var remaining = Mathf.Clamp01((pulseUntil - Time.unscaledTime) / CadencePulseSeconds);
        return Mathf.Sin(remaining * Mathf.PI);
    }

    private void ApplyVisuals(float pulse)
    {
        var maxLit = 0f;
        for (var i = 0; i < litGroups.Length; i++)
        {
            var slotPulse = SlotPulseAmount(i);
            var combinedPulse = Mathf.Max(pulse, slotPulse);
            maxLit = Mathf.Max(maxLit, currentLit[i]);
            if (litGroups[i] != null)
            {
                litGroups[i].alpha = currentLit[i];
            }

            var tint = Color.Lerp(LitColor, PulseColor, Mathf.Clamp01(combinedPulse));
            if (litImages[i] != null)
            {
                litImages[i].color = tint;
            }

            var material = litMaterials[i];
            if (material == null)
            {
                continue;
            }

            material.SetFloat(StarScoreHudShaderIds.LitAmount, currentLit[i]);
            material.SetFloat(StarScoreHudShaderIds.Pulse, combinedPulse);
            material.SetFloat(StarScoreHudShaderIds.FlowTime, flowTime);
            material.SetFloat(StarScoreHudShaderIds.FlowStrength, Mathf.Max(currentLit[i], combinedPulse));
            material.SetColor(StarScoreHudShaderIds.Tint, tint);
        }

        if (dimFrame != null)
        {
            dimFrame.color = Color.Lerp(DimFrameColor, new Color(0.42f, 0.41f, 0.48f, 1f), maxLit * 0.18f + pulse * 0.12f);
        }
    }

    private float SlotPulseAmount(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotPulseUntil.Length || slotPulseUntil[slotIndex] <= Time.unscaledTime)
        {
            return 0f;
        }

        var remaining = Mathf.Clamp01((slotPulseUntil[slotIndex] - Time.unscaledTime) / SlotPulseSeconds);
        return Mathf.Sin(remaining * Mathf.PI) * slotPulseStrength[slotIndex];
    }

    private bool IsStable(float pulse)
    {
        if (pulse > 0.001f)
        {
            return false;
        }

        for (var i = 0; i < currentLit.Length; i++)
        {
            if (slotPulseUntil[i] > Time.unscaledTime)
            {
                return false;
            }

            if (Mathf.Abs(currentLit[i] - targetLit[i]) > 0.001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldAnimateFlow()
    {
        return true;
    }

    private void OnDestroy()
    {
        StarScoreHudShaderMaterials.DestroyAll(litMaterials);
    }
}
