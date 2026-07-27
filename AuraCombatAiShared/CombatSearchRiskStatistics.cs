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
            (int)Math.Ceiling(orderedSamples.Length * normalized));
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

    private void EnsureOrderedSamples()
    {
        if (!orderedSamplesDirty)
        {
            return;
        }
        orderedSamples = returnSamples.ToArray();
        Array.Sort(orderedSamples);
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
