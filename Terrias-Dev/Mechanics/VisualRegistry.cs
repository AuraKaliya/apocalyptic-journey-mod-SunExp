using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class VisualRegistry
{
    private const string RegistryFileName = "visual.registry.json";
    private const string OwnerModId = "Terrias";
    private const string ModPathPrefix = "Mods/Terrias/";

    private static readonly object SyncRoot = new();
    private static VisualRegistryDocument document = VisualRegistryDefaults.Create();
    private static string modDirectory = "";

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var fallback = VisualRegistryDefaults.Create();
            var path = Path.Combine(modConfig.DirectoryName, RegistryFileName);
            modDirectory = modConfig.DirectoryName;
            if (!File.Exists(path))
            {
                document = Normalize(fallback, fallback);
                TerriasLog.Warn("[VisualRegistry] missing visual.registry.json; using built-in defaults.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<VisualRegistryDocument>(File.ReadAllText(path)) ?? new VisualRegistryDocument();
                document = Normalize(loaded, fallback);
                TerriasLog.Info("[VisualRegistry] loaded visual declarations from " + path);
            }
            catch (Exception ex)
            {
                document = Normalize(fallback, fallback);
                TerriasLog.Warn("[VisualRegistry] failed to load visual.registry.json; using built-in defaults: " + ex.Message);
            }
        }
    }

    public static string? TexturePath(string id, string? fallback = null)
    {
        var spec = ActiveDocument().Textures.FirstOrDefault(item => IsEnabled(item.Enabled, item.Id, id));
        var path = spec?.Path ?? "";
        return string.IsNullOrWhiteSpace(path) ? fallback : path.Trim();
    }

    public static ModeEntryVisualSpec? ModeEntry(string id)
    {
        return ActiveDocument().ModeEntries.FirstOrDefault(item => IsEnabled(item.Enabled, item.Id, id));
    }

    public static ShaderVisualSpec? Shader(string id)
    {
        return ActiveDocument().Shaders.FirstOrDefault(item => IsEnabled(item.Enabled, item.Id, id));
    }

    public static VisualEffectVisualSpec? Effect(string id)
    {
        return ActiveDocument().Effects.FirstOrDefault(item => IsEnabled(item.Enabled, item.Id, id));
    }

    public static string ResolveContentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var clean = path.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(clean))
        {
            return clean;
        }

        if (clean.StartsWith(ModPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(ModDirectory(), clean.Substring(ModPathPrefix.Length).Replace('/', Path.DirectorySeparatorChar));
        }

        return Path.Combine(ModDirectory(), clean.Replace('/', Path.DirectorySeparatorChar));
    }

    public static IReadOnlyList<MapNodeCardArtSpec> MapNodeArtSpecs()
    {
        return ActiveDocument().MapNodeArt
            .Where(spec => spec.Enabled && !string.IsNullOrWhiteSpace(spec.TexturePath))
            .Select(ToMapNodeCardArtSpec)
            .ToArray();
    }

    public static IReadOnlyList<string> BundlePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shader in ActiveDocument().Shaders)
        {
            AddBundlePath(paths, shader.Enabled, shader.BundlePath);
        }

        foreach (var effect in ActiveDocument().Effects)
        {
            AddBundlePath(paths, effect.Enabled, effect.BundlePath);
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static FrameAnimationVisualSpec? FrameAnimationByMatchId(string id, string targetKind)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return ActiveDocument().FrameAnimations
            .Where(spec => IsFrameTarget(spec, targetKind))
            .FirstOrDefault(spec => spec.MatchIds.Any(match => string.Equals(match, id, StringComparison.OrdinalIgnoreCase)));
    }

    public static FrameAnimationVisualSpec? FrameAnimationBySpriteName(string? spriteName, string targetKind)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return null;
        }

        var name = spriteName ?? "";
        return ActiveDocument().FrameAnimations
            .Where(spec => IsFrameTarget(spec, targetKind))
            .FirstOrDefault(spec => spec.MatchSpriteNames.Any(match => name.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static VisualRegistryDocument ActiveDocument()
    {
        lock (SyncRoot)
        {
            return document;
        }
    }

    private static bool IsEnabled(bool enabled, string actualId, string expectedId)
    {
        return enabled && string.Equals(actualId, expectedId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFrameTarget(FrameAnimationVisualSpec spec, string targetKind)
    {
        return spec.Enabled
            && spec.FramePaths.Count > 0
            && string.Equals(spec.TargetKind, targetKind, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddBundlePath(ISet<string> paths, bool enabled, string? bundlePath)
    {
        var path = bundlePath?.Trim() ?? "";
        if (enabled && path.Length > 0)
        {
            paths.Add(path);
        }
    }

    private static MapNodeCardArtSpec ToMapNodeCardArtSpec(MapNodeArtVisualSpec spec)
    {
        var fitMode = Enum.TryParse<MapNodeCardArtFitMode>(spec.FitMode, true, out var parsed)
            ? parsed
            : MapNodeCardArtFitMode.ContainTrimmed;

        return new MapNodeCardArtSpec(
            spec.TexturePath,
            fitMode,
            spec.MapIds,
            spec.LevelIds,
            spec.EnemyIds,
            spec.BoundsWidth,
            spec.BoundsHeight,
            spec.AlphaThreshold,
            spec.OffsetX,
            spec.OffsetY,
            spec.Priority);
    }

    private static VisualRegistryDocument Normalize(VisualRegistryDocument current, VisualRegistryDocument fallback)
    {
        current.SchemaVersion = current.SchemaVersion <= 0 ? fallback.SchemaVersion : current.SchemaVersion;
        current.OwnerModId = string.IsNullOrWhiteSpace(current.OwnerModId) ? OwnerModId : current.OwnerModId.Trim();
        current.Textures = Merge(current.Textures, fallback.Textures, spec => spec.Id, NormalizeTexture);
        current.ModeEntries = Merge(current.ModeEntries, fallback.ModeEntries, spec => spec.Id, NormalizeModeEntry);
        current.FrameAnimations = Merge(current.FrameAnimations, fallback.FrameAnimations, spec => spec.Id, NormalizeFrameAnimation);
        current.MapNodeArt = Merge(current.MapNodeArt, fallback.MapNodeArt, spec => spec.Id, NormalizeMapNodeArt);
        current.Shaders = Merge(current.Shaders, fallback.Shaders, spec => spec.Id, NormalizeShader);
        current.Effects = Merge(current.Effects, fallback.Effects, spec => spec.Id, NormalizeEffect);
        return current;
    }

    private static List<T> Merge<T>(
        IEnumerable<T>? current,
        IEnumerable<T>? fallback,
        Func<T, string> idSelector,
        Action<T> normalize)
    {
        var merged = new List<T>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void AddOrReplace(T item)
        {
            normalize(item);
            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            if (indexes.TryGetValue(id, out var index))
            {
                merged[index] = item;
                return;
            }

            indexes[id] = merged.Count;
            merged.Add(item);
        }

        foreach (var item in fallback ?? Array.Empty<T>())
        {
            AddOrReplace(item);
        }

        foreach (var item in current ?? Array.Empty<T>())
        {
            AddOrReplace(item);
        }

        return merged;
    }

    private static void NormalizeTexture(TextureVisualSpec spec)
    {
        spec.Id = Clean(spec.Id);
        spec.Path = Clean(spec.Path);
    }

    private static void NormalizeModeEntry(ModeEntryVisualSpec spec)
    {
        spec.Id = Clean(spec.Id);
        spec.NormalTitleSprite = Clean(spec.NormalTitleSprite);
        spec.HighlightedTitleSprite = Clean(spec.HighlightedTitleSprite);
        if (spec.TitleArtHeightRatio <= 0f || spec.TitleArtHeightRatio > 1f)
        {
            spec.TitleArtHeightRatio = 0.735f;
        }
    }

    private static void NormalizeFrameAnimation(FrameAnimationVisualSpec spec)
    {
        spec.Id = Clean(spec.Id);
        spec.TargetKind = Clean(spec.TargetKind);
        spec.FrameSeconds = Math.Max(0.05f, spec.FrameSeconds);
        spec.MatchIds = NormalizeList(spec.MatchIds);
        spec.MatchSpriteNames = NormalizeList(spec.MatchSpriteNames);
        spec.FramePaths = NormalizeList(spec.FramePaths);
    }

    private static void NormalizeMapNodeArt(MapNodeArtVisualSpec spec)
    {
        spec.Id = Clean(spec.Id);
        spec.TexturePath = Clean(spec.TexturePath);
        spec.FitMode = Clean(spec.FitMode);
        spec.MapIds = NormalizeList(spec.MapIds);
        spec.LevelIds = NormalizeList(spec.LevelIds);
        spec.EnemyIds = NormalizeList(spec.EnemyIds);
        spec.BoundsWidth = spec.BoundsWidth > 0f ? spec.BoundsWidth : MapNodeTextureFitService.DefaultFightBoundsWidth;
        spec.BoundsHeight = spec.BoundsHeight > 0f ? spec.BoundsHeight : MapNodeTextureFitService.DefaultFightBoundsHeight;
        spec.AlphaThreshold = spec.AlphaThreshold > 0f ? spec.AlphaThreshold : MapNodeTextureFitService.DefaultAlphaThreshold;
    }

    private static void NormalizeShader(ShaderVisualSpec spec)
    {
        spec.Id = Clean(spec.Id);
        spec.ShaderName = Clean(spec.ShaderName);
        spec.ShaderPath = Clean(spec.ShaderPath);
        spec.BundlePath = Clean(spec.BundlePath);
        spec.MaterialPath = Clean(spec.MaterialPath);
    }

    private static void NormalizeEffect(VisualEffectVisualSpec spec)
    {
        spec.Id = Clean(spec.Id);
        spec.Kind = Clean(spec.Kind);
        spec.ShaderId = Clean(spec.ShaderId);
        spec.BundlePath = Clean(spec.BundlePath);
        spec.MaterialPath = Clean(spec.MaterialPath);
        spec.Textures = NormalizeDictionary(spec.Textures);
        spec.Colors = NormalizeDictionary(spec.Colors);
        spec.Floats = spec.Floats ?? new Dictionary<string, float>();
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return values?
            .Select(Clean)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
    }

    private static string Clean(string? value)
    {
        var text = value ?? "";
        return string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
    }

    private static Dictionary<string, string> NormalizeDictionary(IDictionary<string, string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values == null)
        {
            return result;
        }

        foreach (var pair in values)
        {
            var key = Clean(pair.Key);
            var value = Clean(pair.Value);
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static string ModDirectory()
    {
        return string.IsNullOrWhiteSpace(modDirectory) ? Environment.CurrentDirectory : modDirectory;
    }
}

internal static class VisualRegistryDefaults
{
    public static VisualRegistryDocument Create()
    {
        return new VisualRegistryDocument
        {
            SchemaVersion = 1,
            OwnerModId = "Terrias",
            Textures = new List<TextureVisualSpec>
            {
                new()
                {
                    Id = "solar_memory.event_map_card",
                    Path = "Mods/Terrias/ModResource/Images/MapNode/\u65e5\u8000\u56de\u5fc6-\u4e8b\u4ef6.png"
                }
            },
            ModeEntries = new List<ModeEntryVisualSpec>
            {
                new()
                {
                    Id = "solar_memory",
                    NormalTitleSprite = "Mods/Terrias/ModResource/Images/UI/solar_memory_title_c.png",
                    HighlightedTitleSprite = "Mods/Terrias/ModResource/Images/UI/solar_memory_title_c_h.png",
                    TitleArtHeightRatio = 0.735f
                },
                new()
                {
                    Id = "endless_abyss",
                    NormalTitleSprite = "Mods/Terrias/ModResource/Images/UI/endless_sea_title_c.png",
                    HighlightedTitleSprite = "Mods/Terrias/ModResource/Images/UI/endless_sea_title_h.png",
                    TitleArtHeightRatio = 0.735f
                }
            },
            FrameAnimations = new List<FrameAnimationVisualSpec>
            {
                new()
                {
                    Id = "dusk.afterheat.icon",
                    TargetKind = "blessing-icon",
                    FrameSeconds = 0.2f,
                    MatchSpriteNames = new List<string> { "huanghun_1" },
                    FramePaths = DuskFrames()
                },
                new()
                {
                    Id = "star_clay_doll.icon",
                    TargetKind = "blessing-icon",
                    FrameSeconds = 0.2f,
                    MatchSpriteNames = new List<string> { "renkui_1" },
                    FramePaths = StarClayFrames()
                },
                new()
                {
                    Id = "dusk.afterheat.buff",
                    TargetKind = "buff-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { TerriasIds.DuskAfterheatRecoveryTrait },
                    FramePaths = DuskFrames()
                },
                new()
                {
                    Id = "star_clay_doll.buff",
                    TargetKind = "buff-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { TerriasIds.StarClayBody, TerriasIds.StarClayDollTrait },
                    FramePaths = StarClayFrames()
                },
                new()
                {
                    Id = "enemy.saint_wuna.dictionary",
                    TargetKind = "enemy-dictionary-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { TerriasIds.SolarBossSaintWunaEnemyId },
                    FramePaths = EnemyDictFrames("WuNa_e")
                },
                new()
                {
                    Id = "enemy.second_sun.dictionary",
                    TargetKind = "enemy-dictionary-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { TerriasIds.SolarBossSecondSunEnemyId },
                    FramePaths = EnemyDictFrames("SecondSunWeel_e")
                }
            },
            MapNodeArt = new List<MapNodeArtVisualSpec>
            {
                new()
                {
                    Id = "solar_memory.second_sun.map_card",
                    TexturePath = TerriasIds.SolarBossSecondSunMapTexturePath,
                    FitMode = nameof(MapNodeCardArtFitMode.ContainTrimmed),
                    MapIds = new List<string> { TerriasIds.SolarBossSecondSunMapId, TerriasIds.SolarBossSecondSunShortMapId },
                    LevelIds = new List<string> { TerriasIds.SolarBossSecondSunLevelId, "level_second_sun_last_day" },
                    EnemyIds = new List<string> { TerriasIds.SolarBossSecondSunEnemyId, "boss_second_sun_last_day" },
                    Priority = 100
                },
                new()
                {
                    Id = "solar_memory.saint_wuna.map_card",
                    TexturePath = TerriasIds.SolarBossSaintWunaMapTexturePath,
                    FitMode = nameof(MapNodeCardArtFitMode.ContainTrimmed),
                    MapIds = new List<string> { TerriasIds.SolarBossSaintWunaMapId, TerriasIds.SolarBossSaintWunaShortMapId },
                    LevelIds = new List<string> { TerriasIds.SolarBossSaintWunaLevelId, "level_saint_wuna" },
                    EnemyIds = new List<string> { TerriasIds.SolarBossSaintWunaEnemyId, "boss_saint_wuna" },
                    Priority = 100
                }
            },
            Shaders = new List<ShaderVisualSpec>
            {
                new()
                {
                    Id = "terrias.star_score_hud",
                    ShaderName = "Terrias/StarScoreHud",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/StarScoreHudLit",
                    ShaderPath = "Terrias/StarScoreHud"
                },
                new()
                {
                    Id = "terrias.wuna_orbit_fire",
                    ShaderName = "Terrias/WunaOrbitFire",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/WunaOrbitFireFront",
                    ShaderPath = "Terrias/WunaOrbitFire"
                },
                new()
                {
                    Id = "terrias.card_use_fx.stardust.shader",
                    ShaderName = "Terrias/CardUseStardust",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/CardUseStardust",
                    ShaderPath = "Terrias/CardUseStardust"
                }
            },
            Effects = new List<VisualEffectVisualSpec>
            {
                new()
                {
                    Id = "terrias.star_score_hud.lit_slot",
                    Kind = "ui-material",
                    ShaderId = "terrias.star_score_hud",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/StarScoreHudLit",
                    Floats = new Dictionary<string, float>
                    {
                        ["_TerriasFlowSpeed"] = 0.55f,
                        ["_TerriasFlowScale"] = 1.2f,
                        ["_TerriasEdgeGlow"] = 0.35f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_TerriasGlowColor"] = "#FFE08AFF",
                        ["_TerriasFlowColor"] = "#9DDCFFFF"
                    }
                },
                new()
                {
                    Id = TerriasIds.StellarOvertureCardUseVisualEffectId,
                    Kind = "star-score-card-use-material",
                    ShaderId = "terrias.card_use_fx.stardust.shader",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/CardUseStardust",
                    Textures = new Dictionary<string, string>
                    {
                        ["_NoiseTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_TerriasEffectMode"] = 1f,
                        ["_TerriasFlowSpeed"] = 0.72f,
                        ["_TerriasFlowScale"] = 1.5f,
                        ["_TerriasNoiseScale"] = 6.2f,
                        ["_TerriasDistortion"] = 0.004f,
                        ["_TerriasEffectIntensity"] = 0.95f,
                        ["_TerriasEdgeGlow"] = 0.12f,
                        ["_TerriasStardustGrain"] = 0.28f,
                        ["_TerriasStardustDensity"] = 0.46f,
                        ["_TerriasStardustTwinkle"] = 1.25f,
                        ["_TerriasStardustTwinkleSpeed"] = 2.15f,
                        ["_TerriasStardustOrbit"] = 0.18f,
                        ["_TerriasStardustGlowRadius"] = 0.18f,
                        ["_TerriasStardustGlowPower"] = 5.4f,
                        ["_TerriasStardustSweepSpeed"] = 1.85f,
                        ["_TerriasStardustSweepIntensity"] = 0.62f,
                        ["_TerriasStardustSweepWidth"] = 0.045f,
                        ["_TerriasEdgeSample"] = 2f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_TerriasStardustColorA"] = "#F3FBFFFF",
                        ["_TerriasStardustColorB"] = "#FFE6A8FF"
                    }
                },
                new()
                {
                    Id = "terrias.wuna.orbit_fire.core.back",
                    Kind = "character-orbit-core-material",
                    ShaderId = "terrias.wuna_orbit_fire",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/WunaOrbitFireBack",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_TerriasNoiseScale"] = 2.0f,
                        ["_TerriasDistortion"] = 0.08f,
                        ["_TerriasAlphaCutoff"] = 0.01f,
                        ["_TerriasAlphaSoftness"] = 0.12f,
                        ["_TerriasFlowSpeed"] = 0.34f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_TerriasCoreColor"] = "#FFF5B8D8",
                        ["_TerriasEdgeColor"] = "#FF7A26A8",
                        ["_TerriasSmokeColor"] = "#35100822"
                    }
                },
                new()
                {
                    Id = "terrias.wuna.orbit_fire.core.front",
                    Kind = "character-orbit-core-material",
                    ShaderId = "terrias.wuna_orbit_fire",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/WunaOrbitFireFront",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_TerriasNoiseScale"] = 2.35f,
                        ["_TerriasDistortion"] = 0.11f,
                        ["_TerriasAlphaCutoff"] = 0.01f,
                        ["_TerriasAlphaSoftness"] = 0.1f,
                        ["_TerriasFlowSpeed"] = 0.48f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_TerriasCoreColor"] = "#FFFFC8F0",
                        ["_TerriasEdgeColor"] = "#FF9C36D8",
                        ["_TerriasSmokeColor"] = "#42100828"
                    }
                },
                new()
                {
                    Id = "terrias.wuna.orbit_fire.back",
                    Kind = "character-orbit-material",
                    ShaderId = "terrias.wuna_orbit_fire",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/WunaOrbitFireBack",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_TerriasNoiseScale"] = 2.45f,
                        ["_TerriasDistortion"] = 0.18f,
                        ["_TerriasAlphaCutoff"] = 0.03f,
                        ["_TerriasAlphaSoftness"] = 0.08f,
                        ["_TerriasFlowSpeed"] = 0.48f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_TerriasCoreColor"] = "#FFE9A0C0",
                        ["_TerriasEdgeColor"] = "#E85A1A94",
                        ["_TerriasSmokeColor"] = "#38140C30"
                    }
                },
                new()
                {
                    Id = "terrias.wuna.orbit_fire.front",
                    Kind = "character-orbit-material",
                    ShaderId = "terrias.wuna_orbit_fire",
                    BundlePath = "Mods/Terrias/ModResource/VisualBundles/terrias_visuals",
                    MaterialPath = "Terrias/Materials/WunaOrbitFireFront",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/Terrias/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_TerriasNoiseScale"] = 3.1f,
                        ["_TerriasDistortion"] = 0.24f,
                        ["_TerriasAlphaCutoff"] = 0.035f,
                        ["_TerriasAlphaSoftness"] = 0.07f,
                        ["_TerriasFlowSpeed"] = 0.68f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_TerriasCoreColor"] = "#FFF8B8E8",
                        ["_TerriasEdgeColor"] = "#FF6C20C8",
                        ["_TerriasSmokeColor"] = "#45140C34"
                    }
                }
            }
        };
    }

    private static List<string> DuskFrames()
    {
        return new List<string>
        {
            "Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_1",
            "Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_2",
            "Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_3",
            "Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_4",
            "Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_3",
            "Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_2"
        };
    }

    private static List<string> StarClayFrames()
    {
        return new List<string>
        {
            "Mods/Terrias/ModResource/Images/Buff/Loneer/renkui_1",
            "Mods/Terrias/ModResource/Images/Buff/Loneer/renkui_2",
            "Mods/Terrias/ModResource/Images/Buff/Loneer/renkui_3",
            "Mods/Terrias/ModResource/Images/Buff/Loneer/renkui_4",
            "Mods/Terrias/ModResource/Images/Buff/Loneer/renkui_3",
            "Mods/Terrias/ModResource/Images/Buff/Loneer/renkui_2"
        };
    }

    private static List<string> EnemyDictFrames(string folder)
    {
        var frames = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            frames.Add("Mods/Terrias/ModResource/AnimationLib/" + folder + "/Dict/Dict_" + i.ToString("00"));
        }

        return frames;
    }
}
