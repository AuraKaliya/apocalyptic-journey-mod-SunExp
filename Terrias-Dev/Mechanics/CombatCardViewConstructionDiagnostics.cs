using System;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Mechanics;

public static class CombatCardViewConstructionDiagnostics
{
    private const int MaxUsefulAgeFrames = 900;
    private static ConstructionSample lastSample;

    public static void Record(
        string bucket,
        double rootMilliseconds,
        double prefabLoadMilliseconds,
        double instantiateMilliseconds,
        double addComponentMilliseconds,
        double markerMilliseconds,
        double totalMilliseconds)
    {
        lastSample = new ConstructionSample(
            Time.frameCount,
            bucket,
            rootMilliseconds,
            prefabLoadMilliseconds,
            instantiateMilliseconds,
            addComponentMilliseconds,
            markerMilliseconds,
            totalMilliseconds);
        SunExpPerformanceCounters.Record("CombatCardViewConstruction.Sampled");
    }

    public static string FormatRecent()
    {
        var sample = lastSample;
        var age = Math.Max(0, Time.frameCount - sample.Frame);
        if (sample.Frame <= 0 || age > MaxUsefulAgeFrames)
        {
            return " poolConstructionProbe=<unavailable>";
        }

        return " poolConstructionProbe="
            + sample.Bucket
            + "[ageFrames="
            + age
            + ",root="
            + sample.RootMilliseconds.ToString("0.###")
            + ",prefabLoad="
            + sample.PrefabLoadMilliseconds.ToString("0.###")
            + ",instantiate="
            + sample.InstantiateMilliseconds.ToString("0.###")
            + ",addComponent="
            + sample.AddComponentMilliseconds.ToString("0.###")
            + ",marker="
            + sample.MarkerMilliseconds.ToString("0.###")
            + ",total="
            + sample.TotalMilliseconds.ToString("0.###")
            + "]";
    }

    private readonly struct ConstructionSample
    {
        public ConstructionSample(
            int frame,
            string bucket,
            double rootMilliseconds,
            double prefabLoadMilliseconds,
            double instantiateMilliseconds,
            double addComponentMilliseconds,
            double markerMilliseconds,
            double totalMilliseconds)
        {
            Frame = frame;
            Bucket = bucket ?? "unknown";
            RootMilliseconds = Math.Max(0d, rootMilliseconds);
            PrefabLoadMilliseconds = Math.Max(0d, prefabLoadMilliseconds);
            InstantiateMilliseconds = Math.Max(0d, instantiateMilliseconds);
            AddComponentMilliseconds = Math.Max(0d, addComponentMilliseconds);
            MarkerMilliseconds = Math.Max(0d, markerMilliseconds);
            TotalMilliseconds = Math.Max(0d, totalMilliseconds);
        }

        public int Frame { get; }
        public string Bucket { get; }
        public double RootMilliseconds { get; }
        public double PrefabLoadMilliseconds { get; }
        public double InstantiateMilliseconds { get; }
        public double AddComponentMilliseconds { get; }
        public double MarkerMilliseconds { get; }
        public double TotalMilliseconds { get; }
    }
}
