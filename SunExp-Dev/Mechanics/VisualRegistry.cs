using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Mechanics;

public static class VisualRegistry
{
    private const string RegistryFileName = "visual.registry.json";
    private const string OwnerModId = "SunExp";
    private const string ModPathPrefix = "Mods/SunExp/";

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
                SunExpLog.Warn("[VisualRegistry] missing visual.registry.json; using built-in defaults.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<VisualRegistryDocument>(File.ReadAllText(path)) ?? new VisualRegistryDocument();
                document = Normalize(loaded, fallback);
                SunExpLog.Info("[VisualRegistry] loaded visual declarations from " + path);
            }
            catch (Exception ex)
            {
                document = Normalize(fallback, fallback);
                SunExpLog.Warn("[VisualRegistry] failed to load visual.registry.json; using built-in defaults: " + ex.Message);
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
            OwnerModId = "SunExp",
            Textures = new List<TextureVisualSpec>
            {
                new()
                {
                    Id = "solar_memory.event_map_card",
                    Path = "Mods/SunExp/ModResource/Images/MapNode/\u65e5\u8000\u56de\u5fc6-\u4e8b\u4ef6.png"
                }
            },
            ModeEntries = new List<ModeEntryVisualSpec>
            {
                new()
                {
                    Id = "solar_memory",
                    NormalTitleSprite = "Mods/SunExp/ModResource/Images/UI/solar_memory_title_c.png",
                    HighlightedTitleSprite = "Mods/SunExp/ModResource/Images/UI/solar_memory_title_c_h.png",
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
                    MatchIds = new List<string> { SunExpIds.DuskAfterheatRecoveryTrait },
                    FramePaths = DuskFrames()
                },
                new()
                {
                    Id = "star_clay_doll.buff",
                    TargetKind = "buff-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { SunExpIds.StarClayBody, SunExpIds.StarClayDollTrait },
                    FramePaths = StarClayFrames()
                },
                new()
                {
                    Id = "enemy.saint_wuna.dictionary",
                    TargetKind = "enemy-dictionary-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { SunExpIds.SolarBossSaintWunaEnemyId },
                    FramePaths = EnemyDictFrames("WuNa_e")
                },
                new()
                {
                    Id = "enemy.second_sun.dictionary",
                    TargetKind = "enemy-dictionary-icon",
                    FrameSeconds = 0.2f,
                    MatchIds = new List<string> { SunExpIds.SolarBossSecondSunEnemyId },
                    FramePaths = EnemyDictFrames("SecondSunWeel_e")
                }
            },
            MapNodeArt = new List<MapNodeArtVisualSpec>
            {
                new()
                {
                    Id = "solar_memory.second_sun.map_card",
                    TexturePath = SunExpIds.SolarBossSecondSunMapTexturePath,
                    FitMode = nameof(MapNodeCardArtFitMode.ContainTrimmed),
                    MapIds = new List<string> { SunExpIds.SolarBossSecondSunMapId, SunExpIds.SolarBossSecondSunShortMapId },
                    LevelIds = new List<string> { SunExpIds.SolarBossSecondSunLevelId, "level_second_sun_last_day" },
                    EnemyIds = new List<string> { SunExpIds.SolarBossSecondSunEnemyId, "boss_second_sun_last_day" },
                    Priority = 100
                },
                new()
                {
                    Id = "solar_memory.saint_wuna.map_card",
                    TexturePath = SunExpIds.SolarBossSaintWunaMapTexturePath,
                    FitMode = nameof(MapNodeCardArtFitMode.ContainTrimmed),
                    MapIds = new List<string> { SunExpIds.SolarBossSaintWunaMapId, SunExpIds.SolarBossSaintWunaShortMapId },
                    LevelIds = new List<string> { SunExpIds.SolarBossSaintWunaLevelId, "level_saint_wuna" },
                    EnemyIds = new List<string> { SunExpIds.SolarBossSaintWunaEnemyId, "boss_saint_wuna" },
                    Priority = 100
                }
            },
            Shaders = new List<ShaderVisualSpec>
            {
                new()
                {
                    Id = "sunexp.star_score_hud",
                    ShaderName = "SunExp/StarScoreHud",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/StarScoreHudLit",
                    ShaderPath = "SunExp/StarScoreHud"
                },
                new()
                {
                    Id = "sunexp.wuna_orbit_fire",
                    ShaderName = "SunExp/WunaOrbitFire",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/WunaOrbitFireFront",
                    ShaderPath = "SunExp/WunaOrbitFire"
                },
                new()
                {
                    Id = SunExpIds.CardFaceEffectShaderId,
                    ShaderName = "SunExp/CardFaceEffect",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/CardFaceEffect",
                    ShaderPath = "SunExp/CardFaceEffect"
                }
            },
            Effects = new List<VisualEffectVisualSpec>
            {
                new()
                {
                    Id = "sunexp.star_score_hud.lit_slot",
                    Kind = "ui-material",
                    ShaderId = "sunexp.star_score_hud",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/StarScoreHudLit",
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpFlowSpeed"] = 0.55f,
                        ["_SunExpFlowScale"] = 1.2f,
                        ["_SunExpEdgeGlow"] = 0.35f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpGlowColor"] = "#FFE08AFF",
                        ["_SunExpFlowColor"] = "#9DDCFFFF"
                    }
                },
                new()
                {
                    Id = SunExpIds.CardFaceFoilHoloVisualEffectId,
                    Kind = "card-visual-face-material",
                    ShaderId = SunExpIds.CardFaceEffectShaderId,
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/CardFaceEffect",
                    Textures = new Dictionary<string, string>
                    {
                        ["_NoiseTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpEffectMode"] = 0f,
                        ["_SunExpFlowSpeed"] = 0.36f,
                        ["_SunExpFlowScale"] = 1.65f,
                        ["_SunExpNoiseScale"] = 4.8f,
                        ["_SunExpDistortion"] = 0.014f,
                        ["_SunExpEffectIntensity"] = 0.68f,
                        ["_SunExpEdgeGlow"] = 0.16f,
                        ["_SunExpSweepFrequency"] = 5.6f,
                        ["_SunExpSweepWidth"] = 0.105f,
                        ["_SunExpSweepIntensity"] = 1.15f,
                        ["_SunExpPrismScale"] = 16f,
                        ["_SunExpPrismStrength"] = 0.58f,
                        ["_SunExpFoilGrain"] = 0.34f,
                        ["_SunExpMirrorSweep"] = 0.52f,
                        ["_SunExpSwirlStrength"] = 0.2f,
                        ["_SunExpEdgeSample"] = 2f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpHoloColorA"] = "#FFE36DFF",
                        ["_SunExpHoloColorB"] = "#89E8FFFF",
                        ["_SunExpHoloColorC"] = "#FF8BE0FF"
                    }
                },
                new()
                {
                    Id = SunExpIds.CardFaceStardustVisualEffectId,
                    Kind = "card-visual-face-material",
                    ShaderId = SunExpIds.CardFaceEffectShaderId,
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/CardFaceEffect",
                    Textures = new Dictionary<string, string>
                    {
                        ["_NoiseTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpEffectMode"] = 1f,
                        ["_SunExpFlowSpeed"] = 0.26f,
                        ["_SunExpFlowScale"] = 1.2f,
                        ["_SunExpNoiseScale"] = 3.6f,
                        ["_SunExpDistortion"] = 0.006f,
                        ["_SunExpEffectIntensity"] = 0.72f,
                        ["_SunExpEdgeGlow"] = 0.1f,
                        ["_SunExpFoilGrain"] = 0.22f,
                        ["_SunExpStardustDensity"] = 0.48f,
                        ["_SunExpStardustTwinkle"] = 1.18f,
                        ["_SunExpStardustOrbit"] = 0.36f,
                        ["_SunExpEdgeSample"] = 2f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpStardustColorA"] = "#DDF2FFFF",
                        ["_SunExpStardustColorB"] = "#FFE08AFF"
                    }
                },
                new()
                {
                    Id = "sunexp.wuna.orbit_fire.core.back",
                    Kind = "character-orbit-core-material",
                    ShaderId = "sunexp.wuna_orbit_fire",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/WunaOrbitFireBack",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpNoiseScale"] = 2.0f,
                        ["_SunExpDistortion"] = 0.08f,
                        ["_SunExpAlphaCutoff"] = 0.01f,
                        ["_SunExpAlphaSoftness"] = 0.12f,
                        ["_SunExpFlowSpeed"] = 0.34f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpCoreColor"] = "#FFF5B8D8",
                        ["_SunExpEdgeColor"] = "#FF7A26A8",
                        ["_SunExpSmokeColor"] = "#35100822"
                    }
                },
                new()
                {
                    Id = "sunexp.wuna.orbit_fire.core.front",
                    Kind = "character-orbit-core-material",
                    ShaderId = "sunexp.wuna_orbit_fire",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/WunaOrbitFireFront",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpNoiseScale"] = 2.35f,
                        ["_SunExpDistortion"] = 0.11f,
                        ["_SunExpAlphaCutoff"] = 0.01f,
                        ["_SunExpAlphaSoftness"] = 0.1f,
                        ["_SunExpFlowSpeed"] = 0.48f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpCoreColor"] = "#FFFFC8F0",
                        ["_SunExpEdgeColor"] = "#FF9C36D8",
                        ["_SunExpSmokeColor"] = "#42100828"
                    }
                },
                new()
                {
                    Id = "sunexp.wuna.orbit_fire.back",
                    Kind = "character-orbit-material",
                    ShaderId = "sunexp.wuna_orbit_fire",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/WunaOrbitFireBack",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpNoiseScale"] = 2.45f,
                        ["_SunExpDistortion"] = 0.18f,
                        ["_SunExpAlphaCutoff"] = 0.03f,
                        ["_SunExpAlphaSoftness"] = 0.08f,
                        ["_SunExpFlowSpeed"] = 0.48f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpCoreColor"] = "#FFE9A0C0",
                        ["_SunExpEdgeColor"] = "#E85A1A94",
                        ["_SunExpSmokeColor"] = "#38140C30"
                    }
                },
                new()
                {
                    Id = "sunexp.wuna.orbit_fire.front",
                    Kind = "character-orbit-material",
                    ShaderId = "sunexp.wuna_orbit_fire",
                    BundlePath = "Mods/SunExp/ModResource/VisualBundles/sunexp_visuals",
                    MaterialPath = "SunExp/Materials/WunaOrbitFireFront",
                    Textures = new Dictionary<string, string>
                    {
                        ["_MainTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailMask.png",
                        ["_NoiseTex"] = "Mods/SunExp/ModResource/Images/Effects/WunaOrbitFire/WunaOrbitTrailNoise.png"
                    },
                    Floats = new Dictionary<string, float>
                    {
                        ["_SunExpNoiseScale"] = 3.1f,
                        ["_SunExpDistortion"] = 0.24f,
                        ["_SunExpAlphaCutoff"] = 0.035f,
                        ["_SunExpAlphaSoftness"] = 0.07f,
                        ["_SunExpFlowSpeed"] = 0.68f
                    },
                    Colors = new Dictionary<string, string>
                    {
                        ["_SunExpCoreColor"] = "#FFF8B8E8",
                        ["_SunExpEdgeColor"] = "#FF6C20C8",
                        ["_SunExpSmokeColor"] = "#45140C34"
                    }
                }
            }
        };
    }

    private static List<string> DuskFrames()
    {
        return new List<string>
        {
            "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1",
            "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_2",
            "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_3",
            "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_4",
            "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_3",
            "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_2"
        };
    }

    private static List<string> StarClayFrames()
    {
        return new List<string>
        {
            "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_1",
            "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_2",
            "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_3",
            "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_4",
            "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_3",
            "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_2"
        };
    }

    private static List<string> EnemyDictFrames(string folder)
    {
        var frames = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            frames.Add("Mods/SunExp/ModResource/AnimationLib/" + folder + "/Dict/Dict_" + i.ToString("00"));
        }

        return frames;
    }
}
