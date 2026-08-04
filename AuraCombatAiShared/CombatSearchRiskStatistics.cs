using System;
using System.Collections.Generic;

namespace AuraCombatAi.Shared;

internal readonly struct CombatSearchRiskEstimate
{
    public CombatSearchRiskEstimate(
        int sampleCount,
        int tailSampleCount,
        double mean,
        double rawLowerTailMean,
        double effectiveLowerTailMean,
        double tailConfidence,
        double standardError)
    {
        SampleCount = sampleCount;
        TailSampleCount = tailSampleCount;
        Mean = mean;
        RawLowerTailMean = rawLowerTailMean;
        EffectiveLowerTailMean = effectiveLowerTailMean;
        TailConfidence = tailConfidence;
        StandardError = standardError;
    }

    public int SampleCount { get; }

    public int TailSampleCount { get; }

    public double Mean { get; }

    public double RawLowerTailMean { get; }

    public double EffectiveLowerTailMean { get; }

    public double TailConfidence { get; }

    public double StandardError { get; }
}

internal sealed class CombatSearchRiskStatistics
{
    private const int MaximumReturnSamples = 2048;
    private const int FullTailEvidence = 8;
    private readonly List<double> returnSamples = new();
    private double[] orderedSamples = Array.Empty<double>();
    private int orderedSampleCount;
    private bool orderedSamplesDirty = true;
    private int count;
    private double mean;
    private double sumSquaredDeviation;
    private double riskSum;

    public int Count => count;

    public double Mean => count == 0 ? 0d : mean;

    public double MeanRisk => count == 0 ? 1d : riskSum / count;

    public void Record(double value, double risk)
    {
        count++;
        var delta = value - mean;
        mean += delta / count;
        sumSquaredDeviation += delta * (value - mean);
        riskSum += risk;

        if (returnSamples.Count < MaximumReturnSamples)
        {
            returnSamples.Add(value);
        }
        else
        {
            returnSamples[(count - 1) % MaximumReturnSamples] = value;
        }
        orderedSamplesDirty = true;
    }

    public void Reset()
    {
        returnSamples.Clear();
        orderedSampleCount = 0;
        orderedSamplesDirty = true;
        count = 0;
        mean = 0d;
        sumSquaredDeviation = 0d;
        riskSum = 0d;
    }

    public CombatSearchRiskEstimate Estimate(double quantile)
    {
        if (returnSamples.Count == 0)
        {
            return new CombatSearchRiskEstimate(
                0,
                0,
                Mean,
                Mean,
                Mean,
                0d,
                0d);
        }

        EnsureOrderedSamples();
        var normalized = Math.Max(0.01d, Math.Min(1d, quantile));
        var tailCount = Math.Max(
            1,
            (int)Math.Ceiling(orderedSampleCount * normalized));
        var tailSum = 0d;
        for (var i = 0; i < tailCount; i++)
        {
            tailSum += orderedSamples[i];
        }

        var rawTail = tailSum / tailCount;
        var confidence = Math.Max(
            0d,
            Math.Min(
                1d,
                (tailCount - 1d) / (FullTailEvidence - 1d)));
        var effectiveTail = Mean + (rawTail - Mean) * confidence;
        var standardError = count <= 1
            ? Math.Max(1d, Math.Abs(Mean) * 0.25d)
            : Math.Sqrt(
                Math.Max(0d, sumSquaredDeviation / (count - 1d))
                / count);
        return new CombatSearchRiskEstimate(
            returnSamples.Count,
            tailCount,
            Mean,
            rawTail,
            effectiveTail,
            confidence,
            standardError);
    }

    public double[] Quantiles(int count)
    {
        var size = Math.Max(1, Math.Min(64, count));
        var result = new double[size];
        if (returnSamples.Count == 0)
        {
            return result;
        }
        EnsureOrderedSamples();
        for (var index = 0; index < size; index++)
        {
            var tau = (index + 0.5d) / size;
            var position = tau * (orderedSampleCount - 1);
            var lower = (int)Math.Floor(position);
            var upper = Math.Min(orderedSampleCount - 1, lower + 1);
            var fraction = position - lower;
            result[index] = orderedSamples[lower]
                            + (orderedSamples[upper] - orderedSamples[lower])
                            * fraction;
        }
        return result;
    }

    private void EnsureOrderedSamples()
    {
        if (!orderedSamplesDirty)
        {
            return;
        }
        if (orderedSamples.Length < returnSamples.Count)
        {
            var capacity = Math.Max(16, orderedSamples.Length);
            while (capacity < returnSamples.Count)
            {
                capacity = Math.Min(MaximumReturnSamples, capacity * 2);
            }
            orderedSamples = new double[capacity];
        }
        returnSamples.CopyTo(orderedSamples, 0);
        orderedSampleCount = returnSamples.Count;
        Array.Sort(orderedSamples, 0, orderedSampleCount);
        orderedSamplesDirty = false;
    }
}

internal static class CombatRiskAdjustedSearchValue
{
    public static double Calculate(
        CombatSearchRiskEstimate estimate,
        double meanRisk,
        CombatDecisionProfile profile)
    {
        return estimate.Mean * 0.65d
               + estimate.EffectiveLowerTailMean * 0.35d
               - profile.TailRiskPenalty * meanRisk
               - profile.UncertaintyPenalty * estimate.StandardError;
    }
}
