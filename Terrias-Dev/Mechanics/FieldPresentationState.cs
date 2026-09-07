using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

/// <summary>Local presentation interpolation; never writes combat or network state.</summary>
public sealed class FieldPresentationState
{
    private readonly float[] weights = new float[4];
    private readonly float[] strengths = new float[4];

    public TerriasFieldId Field { get; private set; }
    public int Stacks { get; private set; }
    public int MaxStacks { get; private set; }
    public float Pulse { get; private set; }
    public float Visibility { get; private set; }

    public float Weight(TerriasFieldId field) => Valid(field) ? weights[(int)field] : 0f;
    public float Strength(TerriasFieldId field) => Valid(field) ? strengths[(int)field] : 0f;

    public bool Apply(TerriasFieldId field, int stacks, int maxStacks, bool animate = true)
    {
        if (!Valid(field) || stacks <= 0 || maxStacks <= 0)
        {
            field = TerriasFieldId.None;
            stacks = maxStacks = 0;
        }
        else
        {
            stacks = Math.Min(stacks, maxStacks);
        }

        if (Field == field && Stacks == stacks && MaxStacks == maxStacks) return false;
        var changedField = Field != field;
        Field = field;
        Stacks = stacks;
        MaxStacks = maxStacks;
        if (field != TerriasFieldId.None)
        {
            strengths[(int)field] = 0.45f + 0.55f * (float)Math.Sqrt((double)stacks / maxStacks);
            Pulse = Math.Max(Pulse, changedField ? 1f : 0.45f);
        }
        if (!animate)
        {
            for (var i = 1; i < weights.Length; i++) weights[i] = i == (int)field ? 1f : 0f;
            Visibility = field == TerriasFieldId.None ? 0f : 1f;
            Pulse = 0f;
        }
        return true;
    }

    public void Trigger(float strength = 0.7f)
    {
        if (Field != TerriasFieldId.None && !float.IsNaN(strength))
            Pulse = Math.Max(Pulse, Math.Min(1f, Math.Max(0f, strength)));
    }

    public void Advance(float seconds, bool reducedMotion)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;
        var retain = (float)Math.Exp(-Math.Min(seconds, 1f) / (reducedMotion ? 0.12f : 0.2f));
        Visibility = 0f;
        for (var i = 1; i < weights.Length; i++)
        {
            var target = i == (int)Field ? 1f : 0f;
            weights[i] = target + (weights[i] - target) * retain;
            if (Math.Abs(weights[i] - target) < 0.001f) weights[i] = target;
            Visibility += weights[i];
        }
        Pulse = reducedMotion ? 0f : Math.Max(0f, Pulse - seconds / 0.9f);
    }

    public void Reset()
    {
        Field = TerriasFieldId.None;
        Stacks = MaxStacks = 0;
        Visibility = Pulse = 0f;
        Array.Clear(weights, 0, weights.Length);
        Array.Clear(strengths, 0, strengths.Length);
    }

    private static bool Valid(TerriasFieldId field) =>
        field is TerriasFieldId.ScorchingCanopy or TerriasFieldId.SamsaraGarden or TerriasFieldId.MoonDomain;
}
