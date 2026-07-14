using System;

namespace SunExp.Dll.Infrastructure;

public static class SunExpHardTagIds
{
    public const string ScorchedWorld = "sunexp_scorched_world";
    public const string SamsaraGarden = "sunexp_samsara_garden";
    public const string BlackSunCalamity = "sunexp_black_sun_calamity";
    public const string WhiteRadianceCourt = "sunexp_white_radiance_court";
    public const string SunsetExpedition = "sunexp_sunset_expedition";
    public const string Rebirth = "sunexp_rebirth";
    public const string AbyssalShock = "sunexp_abyssal_shock";
    public const string AbyssGaze = "sunexp_abyss_gaze";
    public const string MorningStarDimmed = "sunexp_morning_star_dimmed";
    public const string OtherDimensionStagnantWater = "sunexp_other_dimension_stagnant_water";

    public const string RebirthBuff = "buff_rebirth";

    private const string FullPrefix = "SunExp_sunexp_";

    public static string FullId(string id)
    {
        return string.IsNullOrWhiteSpace(id) || id.StartsWith(FullPrefix, StringComparison.Ordinal)
            ? id
            : FullPrefix + id;
    }

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        var value = (id ?? "").Trim().TrimStart('*');
        return value.StartsWith(FullPrefix, StringComparison.Ordinal)
            ? value.Substring(FullPrefix.Length)
            : value;
    }

    public static bool Same(string? left, string right)
    {
        var normalizedLeft = NormalizeAlias(Normalize(left));
        var normalizedRight = NormalizeAlias(Normalize(right));
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static string NormalizeAlias(string value)
    {
        return value switch
        {
            "solar_memory_scorched_world" => ScorchedWorld,
            "solar_memory_samsara_garden" => SamsaraGarden,
            "solar_memory_black_sun_calamity" => BlackSunCalamity,
            "solar_memory_white_radiance_court" => WhiteRadianceCourt,
            "solar_memory_sunset_expedition" => SunsetExpedition,
            "solar_memory_rebirth" => Rebirth,
            _ => value
        };
    }
}
