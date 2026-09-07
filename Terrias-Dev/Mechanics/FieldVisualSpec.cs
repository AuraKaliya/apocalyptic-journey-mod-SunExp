using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class FieldPresentationOptions
{
    [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
    [JsonProperty("quality")] public string Quality { get; set; } = "standard";
    [JsonProperty("intensity")] public float Intensity { get; set; } = 0.8f;
    [JsonProperty("reducedMotion")] public bool ReducedMotion { get; set; }
    [JsonProperty("backgroundsEnabled")] public bool BackgroundsEnabled { get; set; } = true;

    public bool LowQuality => string.Equals(Quality, "low", StringComparison.OrdinalIgnoreCase);

    public void Normalize()
    {
        Quality = string.Equals(Quality?.Trim(), "low", StringComparison.OrdinalIgnoreCase) ? "low" : "standard";
        Intensity = FieldVisualSpec.FiniteRange(Intensity, 0f, 1f, 0.8f);
    }
}

public sealed class FieldVisualSpec
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
    [JsonProperty("backgroundPath")] public string BackgroundPath { get; set; } = "";
    [JsonProperty("primaryColor")] public string PrimaryColor { get; set; } = "#A7BFF4";
    [JsonProperty("accentColor")] public string AccentColor { get; set; } = "#E0DCFF";
    [JsonProperty("particleCount")] public int ParticleCount { get; set; } = 28;
    [JsonProperty("backgroundOpacity")] public float BackgroundOpacity { get; set; } = 1f;

    public void Normalize()
    {
        Id = (Id ?? "").Trim().ToLowerInvariant();
        BackgroundPath = (BackgroundPath ?? "").Trim().Replace('\\', '/');
        PrimaryColor = NormalizeColor(PrimaryColor, "#A7BFF4");
        AccentColor = NormalizeColor(AccentColor, "#E0DCFF");
        ParticleCount = Math.Min(48, Math.Max(0, ParticleCount));
        BackgroundOpacity = FiniteRange(BackgroundOpacity, 0f, 1f, 1f);
    }

    public static string Slug(TerriasFieldId field) => field switch
    {
        TerriasFieldId.ScorchingCanopy => "scorching_canopy",
        TerriasFieldId.SamsaraGarden => "samsara_garden",
        TerriasFieldId.MoonDomain => "moon_domain",
        _ => ""
    };

    public static List<FieldVisualSpec> Defaults() => new()
    {
        new() { Id = "scorching_canopy", PrimaryColor = "#EF7946", AccentColor = "#FFC676",
            BackgroundPath = "Mods/Terrias/ModResource/Images/Field/scorching_canopy.png", ParticleCount = 32 },
        new() { Id = "samsara_garden", PrimaryColor = "#73C9AE", AccentColor = "#F1ACCD",
            BackgroundPath = "Mods/Terrias/ModResource/Images/Field/samsara_garden.png", ParticleCount = 24 },
        new() { Id = "moon_domain", PrimaryColor = "#9BAEEF", AccentColor = "#E5D8FF",
            BackgroundPath = "Mods/Terrias/ModResource/Images/Field/moon_domain.png", ParticleCount = 28 }
    };

    internal static float FiniteRange(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Min(max, Math.Max(min, value));

    private static string NormalizeColor(string? value, string fallback)
    {
        var color = (value ?? "").Trim();
        if (color.Length != 7 || color[0] != '#') return fallback;
        for (var i = 1; i < color.Length; i++)
            if (!Uri.IsHexDigit(color[i])) return fallback;
        return color;
    }
}
