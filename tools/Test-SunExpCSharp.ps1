param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = "",
    [switch]$SkipBuild,
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Text
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function New-ProjectXml {
    param(
        [string]$RepoRoot,
        [string]$SourceDir
    )

    $dictionaryUtil = Join-Path $RepoRoot "SunExp-Dev\Infrastructure\DictionaryUtil.cs"
    $auraSharedDictionary = Join-Path $RepoRoot "AuraSharedCore\AuraSharedDictionary.cs"
    $sunExpIds = Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpIds.cs"
    $sunExpFrameDispatcher = Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpFrameDispatcher.cs"
    $sunExpPerformanceSettings = Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpPerformanceSettings.cs"
    $cardApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardApi.cs"
    $cardConfigApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"
    $cardVisualSkinApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardVisualSkinApi.cs"
    $cardVisualEffectApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardVisualEffectApi.cs"
    $cardVisualEffectTarget = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardVisualEffectTarget.cs"
    $cardVisualEffectSpec = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardVisualEffectSpec.cs"
    $cardVisualEffectRegistry = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardVisualEffectRegistry.cs"
    $cardVisualSkinSpec = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardVisualSkinSpec.cs"
    $cardVisualSkinRule = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardVisualSkinRule.cs"
    $cardVisualSkinRegistry = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardVisualSkinRegistry.cs"
    $cardMutationService = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardMutationService.cs"
    $runtimeCardAttachmentService = Join-Path $RepoRoot "SunExp-Dev\Mechanics\RuntimeCardAttachmentService.cs"
    $sunExpCardRefreshQueue = Join-Path $RepoRoot "SunExp-Dev\Mechanics\SunExpCardRefreshQueue.cs"
    $starBlessingCostOverrideStore = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarBlessingCostOverrideStore.cs"
    $loneerCombatState = Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerCombatState.cs"
    $starScoreNote = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreNote.cs"
    $starScoreDisplaySnapshot = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreDisplaySnapshot.cs"
    $starScoreCadenceCatalog = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs"
    $starScoreCombatState = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreCombatState.cs"
    $mapNodeCardArtFitMode = Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeCardArtFitMode.cs"
    $mapNodeCardArtFitResult = Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeCardArtFitResult.cs"
    $mapNodeTextureBounds = Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeTextureBounds.cs"
    $mapNodeTextureFitService = Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs"
    $modeChoiceDragRange = Join-Path $RepoRoot "SunExp-Dev\Mechanics\ModeChoiceDragRange.cs"

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$SourceDir\Stubs.cs" />
    <Compile Include="$auraSharedDictionary" />
    <Compile Include="$dictionaryUtil" />
    <Compile Include="$sunExpIds" />
    <Compile Include="$sunExpFrameDispatcher" />
    <Compile Include="$sunExpPerformanceSettings" />
    <Compile Include="$cardApi" />
    <Compile Include="$cardConfigApi" />
    <Compile Include="$cardVisualSkinApi" />
    <Compile Include="$cardVisualEffectApi" />
    <Compile Include="$cardVisualEffectTarget" />
    <Compile Include="$cardVisualEffectSpec" />
    <Compile Include="$cardVisualEffectRegistry" />
    <Compile Include="$cardVisualSkinSpec" />
    <Compile Include="$cardVisualSkinRule" />
    <Compile Include="$cardVisualSkinRegistry" />
    <Compile Include="$sunExpCardRefreshQueue" />
    <Compile Include="$cardMutationService" />
    <Compile Include="$runtimeCardAttachmentService" />
    <Compile Include="$starBlessingCostOverrideStore" />
    <Compile Include="$loneerCombatState" />
    <Compile Include="$starScoreNote" />
    <Compile Include="$starScoreDisplaySnapshot" />
    <Compile Include="$starScoreCadenceCatalog" />
    <Compile Include="$starScoreCombatState" />
    <Compile Include="$mapNodeCardArtFitMode" />
    <Compile Include="$mapNodeCardArtFitResult" />
    <Compile Include="$mapNodeTextureBounds" />
    <Compile Include="$mapNodeTextureFitService" />
    <Compile Include="$modeChoiceDragRange" />
    <Compile Include="$SourceDir\Tests.cs" />
  </ItemGroup>
</Project>
"@
}

function New-StubsSource {
@'
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Witch.Core
{
}

public sealed class FightPlayer
{
    public static FightPlayer Instance { get; } = new();

    public FakeStatus Status { get; } = new();
}

public interface IStatusManager
{
    string InstanceId { get; }
}

public sealed class FakeStatus : IStatusManager
{
    public FakeStatus(string instanceId = "local-player")
    {
        InstanceId = instanceId;
    }

    public string InstanceId { get; }

    public Dictionary<string, float> dynamicVariables { get; } = new();
}

public enum DataType
{
    Card,
    Buff
}

public sealed class Singleton<T>
    where T : new()
{
    public static T Instance { get; } = new();
}

public sealed class GameConfigManager
{
    public object? GetOne(DataType type, string id)
    {
        return string.IsNullOrWhiteSpace(id) ? null : new object();
    }
}

public sealed class ScriptExecutor
{
    public IStatusManager? Self { get; set; } = FightPlayer.Instance.Status;

    public bool ThrowOnDelivery { get; set; }

    public List<CardItem> HandCard { get; } = new();

    public List<CardItem> WaitCard { get; } = new();

    public void SetStatus(string status)
    {
    }

    public void AddCardByData(string id, string addTag = "")
    {
        var data = new Dictionary<string, string>
        {
            ["Id"] = id,
            ["Expend"] = "2",
            ["Tag"] = ""
        };
        var vars = new Dictionary<string, string>
        {
            ["Id"] = id,
            ["Tag"] = addTag
        };
        FightCardManager.Instance.cardList.Add(new DataConfig(data, vars));
    }

    public void GetCardFromDeck(IDataConfig data)
    {
        if (ThrowOnDelivery)
        {
            throw new InvalidOperationException("delivery failed");
        }
    }
}

public sealed class FightCardManager
{
    public static FightCardManager Instance { get; } = new();

    public List<DataConfig> cardList { get; } = new();

    public void RefreshTag(IDataConfig config)
    {
    }
}

public sealed class CardItem
{
    private readonly int instanceId = Guid.NewGuid().GetHashCode();

    public DataConfig? dataConfig { get; set; }

    public IDictionary<string, string> data { get; set; } = new Dictionary<string, string>();

    public IDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

    public List<string> Tags { get; } = new();

    public void RefreshTag()
    {
    }

    public void DataUpdate()
    {
    }

    public int GetInstanceID()
    {
        return instanceId;
    }
}

public sealed class DataConfig : IDataConfig
{
    private IDictionary<string, string> dataValue = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public DataConfig(IDictionary<string, string> data, IDictionary<string, string>? vars = null)
    {
        this.data = data;
        Vars = vars ?? new Dictionary<string, string>();
        InstanceID = Guid.NewGuid().ToString("N");
    }

    public IDictionary<string, string> data
    {
        get => dataValue;
        set => dataValue = new ReadOnlyDictionary<string, string>(value);
    }

    public IDictionary<string, string> Vars { get; }

    public string InstanceID { get; }

    public DataType Type => DataType.Card;

    public IScriptExecutor scriptExecutor => throw new NotSupportedException();

    public bool isCompiling => false;
}

public interface IScriptExecutor
{
}

public interface IDataConfig
{
    IDictionary<string, string> data { get; set; }

    IDictionary<string, string> Vars { get; }

    string InstanceID { get; }

    DataType Type { get; }

    IScriptExecutor scriptExecutor { get; }

    bool isCompiling { get; }
}

namespace SunExp.Dll.GameApi
{
    public static class ExecutorApi
    {
        private static readonly Dictionary<string, int> CombatVars = new();

        public static void ResetCombatVars()
        {
            CombatVars.Clear();
        }

        public static int CombatIntGet(string key, int fallback = 0)
        {
            return key != null && CombatVars.TryGetValue(key, out var value) ? value : fallback;
        }

        public static int CombatIntSet(string key, int value)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                CombatVars[key] = value;
            }

            return value;
        }

        public static int CombatIntAdd(string key, int amount)
        {
            return CombatIntSet(key, CombatIntGet(key) + amount);
        }
    }

    public static class CardSelectionApi
    {
        public static bool SelectCardsFromCards(
            ScriptExecutor self,
            IReadOnlyList<IDataConfig> source,
            int count,
            Func<IDataConfig, bool> predicate,
            Action<IReadOnlyList<IDataConfig>> onSelected,
            string caption,
            Action? onCancelled = null)
        {
            var cards = (source ?? Array.Empty<IDataConfig>())
                .Where(card => card != null && (predicate == null || predicate(card)))
                .Take(Math.Max(0, count))
                .ToList();
            if (self == null || cards.Count == 0 || onSelected == null)
            {
                return false;
            }

            onSelected(cards);
            return true;
        }
    }
}

namespace SunExp.Dll.Infrastructure
{
    public static class SunExpLog
    {
        public static void Warn(string message)
        {
        }

        public static void Info(string message)
        {
        }

        public static void Debug(string message)
        {
        }

        public static void Error(string message, Exception exception)
        {
        }
    }

    public static class SunExpPerformanceCounters
    {
        public static long Timestamp()
        {
            return 0L;
        }

        public static void Record(string name)
        {
        }

        public static void RecordDuration(string name, long startTimestamp)
        {
        }
    }
}

namespace Witch.UI.Window
{
    public static class FightUI
    {
        public static List<CardItem> cardItemList { get; } = new();

        public static List<CardItem> WaitCard { get; } = new();
    }
}
'@
}

function New-TestsSource {
@'
using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

internal static class Program
{
    private static int assertions;
    private const string WhiteRadiance = "\u767d\u66dc";

    private static void Main()
    {
        TestDictionaryUtil();
        TestCardCostHelpers();
        TestStarBlessingCostOverrideStore();
        TestCardGrantRequest();
        TestCardMutationService();
        TestRuntimeCardAttachmentService();
        TestSolarTriggerCostOverride();
        TestWhiteRadianceTags();
        TestTemporaryWhiteRadianceClaim();
        TestSolarMemoryIsolationIds();
        TestCardVisualSkinRegistry();
        TestCardVisualEffectRegistry();
        TestMapNodeTextureFitService();
        TestModeChoiceDragRange();
        TestLoneerStateOwnership();
        TestStarScoreWindow();

        Console.WriteLine("SunExp C# tests passed: " + assertions + " assertions.");
    }

    private static void TestDictionaryUtil()
    {
        Equal(12, DictionaryUtil.ParseInt("12"), "ParseInt parses positive values");
        Equal(-4, DictionaryUtil.ParseInt("-4"), "ParseInt parses negative values");
        Equal(9, DictionaryUtil.ParseInt("not-a-number", 9), "ParseInt returns fallback on invalid text");
        Equal("fallback", DictionaryUtil.Get(null, "key", "fallback"), "DictionaryUtil.Get handles null dictionaries");

        var values = new Dictionary<string, string> { ["A"] = "1" };
        Equal("1", DictionaryUtil.Get(values, "A"), "DictionaryUtil.Get reads existing values");
        DictionaryUtil.Set(values, "B", "2");
        Equal("2", values["B"], "DictionaryUtil.Set writes values");

        True(DictionaryUtil.ContainsToken("Burnout, " + WhiteRadiance + " ,Froze", SunExpIds.WhiteRadianceTag), "ContainsToken trims comma-separated tokens");
        False(DictionaryUtil.ContainsToken(WhiteRadiance + "\u5316", SunExpIds.WhiteRadianceTag), "ContainsToken requires exact token matches");
    }

    private static void TestSolarMemoryIsolationIds()
    {
        True(SunExpIds.IsSolarMemoryExclusiveMapId("solar_memory_black_sun_after"), "Short Solar Memory story map ids are exclusive");
        True(SunExpIds.IsSolarMemoryExclusiveMapId("SunExp_sunexp_solar_memory_boss_saint_wuna"), "Full Solar Memory boss map ids are exclusive");
        False(SunExpIds.IsSolarMemoryExclusiveMapId("solar_event"), "Retired solar event map ids are no longer shipped exclusive maps");
        False(SunExpIds.IsSolarMemoryExclusiveMapId("map_0"), "Base game map ids are not Solar Memory exclusive");
        True(SunExpIds.IsSolarMemoryExclusiveEventId("SunExp_sunexp_Sub_solar_memory_second_sun"), "Full Solar Memory story event ids are exclusive");
        False(SunExpIds.IsSolarMemoryExclusiveEventId("Sub_wuna_event_1"), "Retired Wuna story event ids are no longer shipped exclusive events");
        False(SunExpIds.IsSolarMemoryExclusiveEventId("event_2001"), "Base game event ids are not Solar Memory exclusive");
    }

    private static void TestCardVisualSkinRegistry()
    {
        CardVisualSkinRegistry.ClearOwner("TestMod");
        CardVisualSkinApi.RegisterTheme(
            "TestMod",
            "test.skin.pack",
            "pack-frame",
            "",
            "Pack",
            10,
            null,
            new[] { "pack_a" },
            null);
        CardVisualSkinApi.RegisterTheme(
            "TestMod",
            "test.skin.icon",
            "icon-frame",
            "",
            "Icon",
            20,
            null,
            null,
            new[] { "Mods/Test/Icon/" });

        var packCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "card_pack",
            ["PackBelong"] = "pack_a",
            ["Icon"] = "Other/Icon"
        });
        Equal("test.skin.pack", CardVisualSkinRegistry.Resolve(packCard)?.Id, "Card visual skin resolves by pack id");

        var iconCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "card_icon",
            ["PackBelong"] = "pack_a",
            ["Icon"] = "Mods/Test/Icon/card"
        });
        Equal("test.skin.icon", CardVisualSkinRegistry.Resolve(iconCard)?.Id, "Higher-priority card visual skin resolves by icon prefix");

        CardVisualSkinRegistry.ClearOwner("TestMod");
        Equal(null, CardVisualSkinRegistry.Resolve(packCard)?.Id, "Clearing owner removes registered card visual skin rules");

        CardVisualSkinApi.RegisterSunExpDefaults();
        var radiantSparkCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "SunExp_sunexp_morning_light_bulwark",
            ["PackBelong"] = SunExpIds.RadiantSparkCardPackId,
            ["Icon"] = "Mods/SunExp/ModResource/Images/Card/SunExp/morning_light_bulwark"
        });
        Equal(SunExpIds.SunCardVisualSkinId, CardVisualSkinRegistry.Resolve(radiantSparkCard)?.Id, "SunExp defaults keep Sun packs on the Sun card visual skin");

        var morningStarPackCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = SunExpIds.PrewrittenMeasureCardId,
            ["PackBelong"] = SunExpIds.MorningStarOvertureCardPackId,
            ["Icon"] = "Mods/SunExp/ModResource/Images/Card/MorningStar/prewritten_measure"
        });
        Equal(SunExpIds.MorningStarCardVisualSkinId, CardVisualSkinRegistry.Resolve(morningStarPackCard)?.Id, "Morning Star Overture pack cards use the Morning Star card visual skin");
        CardVisualSkinRegistry.ClearOwner(SunExpIds.ModId);
    }

    private static void TestCardVisualEffectRegistry()
    {
        CardVisualEffectRegistry.ClearOwner("TestMod");
        CardVisualEffectRegistry.Register(new CardVisualEffectSpec(
            "TestMod",
            "test.effect.low",
            CardVisualEffectTarget.Face,
            "test.visual.low",
            "Low",
            1,
            new[] { "target_card" }));
        CardVisualEffectRegistry.Register(new CardVisualEffectSpec(
            "TestMod",
            "test.effect.high",
            CardVisualEffectTarget.Face,
            SunExpIds.CardFaceFoilHoloVisualEffectId,
            "High",
            20,
            new[] { "target_card" }));

        var target = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "target_card"
        });
        var other = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "other_sun_card"
        });
        Equal("test.effect.high", CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, target)?.Id, "Card visual effect resolves the highest-priority face effect by explicit card id");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, other)?.Id, "Card visual effect does not apply to other cards just because they share a skin");

        CardVisualEffectRegistry.Register(new CardVisualEffectSpec(
            "TestMod",
            "test.effect.full",
            CardVisualEffectTarget.Frame,
            SunExpIds.CardFaceFoilHoloVisualEffectId,
            "Full",
            30,
            new[] { SunExpIds.BlazingCrownCollapseCardId }));
        var blazingCrownCollapse = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = SunExpIds.BlazingCrownCollapseCardId
        });
        Equal("test.effect.full", CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, blazingCrownCollapse)?.Id, "Card visual effect supports full mod-qualified card ids on the frame target");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, blazingCrownCollapse)?.Id, "Frame card visual effects do not bleed into the face target");

        CardVisualEffectRegistry.ClearOwner("TestMod");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, target)?.Id, "Clearing owner removes registered card visual effects");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, blazingCrownCollapse)?.Id, "Clearing owner removes registered frame visual effects");

        CardVisualEffectApi.RegisterSunExpDefaults();
        Equal(SunExpIds.BlazingCrownCollapseHoloEffectBindingId, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, blazingCrownCollapse)?.Id, "Blazing Crown Collapse foil applies to the card frame");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, blazingCrownCollapse)?.Id, "Blazing Crown Collapse foil does not apply to the card face");
        foreach (var cardId in new[]
        {
            "*stellar_overture_start",
            "*stellar_overture_sustain",
            "*stellar_overture_turn",
            "*stellar_overture_close"
        })
        {
            var generatedOverture = new DataConfig(new Dictionary<string, string>
            {
                ["Id"] = cardId
            });
            Equal(SunExpIds.StellarOvertureStardustEffectBindingId, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, generatedOverture)?.Id, "Stardust applies to generated Stellar Overture frame id " + cardId);
            Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, generatedOverture)?.Id, "Stardust does not apply to generated Stellar Overture face id " + cardId);
        }

        foreach (var cardId in new[]
        {
            SunExpIds.StellarOvertureStartShortCardId,
            SunExpIds.StellarOvertureSustainShortCardId,
            SunExpIds.StellarOvertureTurnShortCardId,
            SunExpIds.StellarOvertureCloseShortCardId,
            SunExpIds.StellarOvertureStartCardId,
            SunExpIds.StellarOvertureSustainCardId,
            SunExpIds.StellarOvertureTurnCardId,
            SunExpIds.StellarOvertureCloseCardId
        })
        {
            var overture = new DataConfig(new Dictionary<string, string>
            {
                ["Id"] = cardId
            });
            Equal(SunExpIds.StellarOvertureStardustEffectBindingId, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, overture)?.Id, "Stardust applies to Stellar Overture frame id " + cardId);
            Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, overture)?.Id, "Stardust does not apply to Stellar Overture face id " + cardId);
        }

        var unrelatedGeneratedSuffix = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "OtherMod_sunexp_stellar_overture_start"
        });
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, unrelatedGeneratedSuffix)?.Id, "Leading star generated-card ids are matched literally, not as broad wildcards");

        var ordinaryMorningStarCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = SunExpIds.PrewrittenMeasureCardId
        });
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, ordinaryMorningStarCard)?.Id, "Stardust does not apply to ordinary Morning Star cards");
    }

    private static void TestMapNodeTextureFitService()
    {
        var secondSun = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 20, 20, 90, 91),
            MapNodeCardArtFitMode.ContainTrimmed);
        True(secondSun.ShouldApplyTransform, "Trimmed map-node art owns the icon transform");
        Approximately(182.86f, secondSun.ScaleX, 0.01f, "Wide second-sun art scales the full canvas from the visible-width fit");
        Approximately(272f, secondSun.ScaleY, 0.01f, "Wide second-sun art preserves canvas aspect while fitting visible width");
        Approximately(-0.29f, secondSun.OffsetY, 0.02f, "Asymmetric transparent trim recenters the visible subject");

        var saint = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 63, 64, 70, 130),
            MapNodeCardArtFitMode.ContainTrimmed);
        Approximately(265.28f, saint.ScaleX, 0.01f, "Tall saint art scales the full canvas from the visible-width fit");
        Approximately(394.61f, saint.ScaleY, 0.01f, "Tall saint art keeps the original canvas ratio");
        Approximately(-24.87f, saint.OffsetY, 0.01f, "Large bottom transparency is compensated by a vertical offset");

        var canvas = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 63, 64, 70, 130),
            MapNodeCardArtFitMode.ContainCanvas);
        Approximately(160f, canvas.ScaleX, 0.01f, "Canvas mode fits the full 320px canvas width");
        Approximately(238f, canvas.ScaleY, 0.01f, "Canvas mode fits the full 476px canvas height");
        Approximately(0f, canvas.OffsetY, 0.01f, "Canvas mode does not compensate transparent padding");

        var legacy = MapNodeTextureFitService.Fit(
            new MapNodeTextureBounds(320, 476, 0, 0, 0, 0),
            MapNodeCardArtFitMode.StretchLegacy);
        False(legacy.ShouldApplyTransform, "Legacy mode leaves native MapItem transform untouched");
    }

    private static void TestModeChoiceDragRange()
    {
        var fiveSlots = ModeChoiceDragRangeService.Calculate(
            -987.5f,
            987.5f,
            355f,
            5,
            50f,
            1920f,
            4,
            96f);
        Approximately(1570f, fiveSlots.ViewportWidth, 0.01f, "Four visible mode slots define the viewport width");
        Approximately(-202.5f, fiveSlots.MinOffset, 0.01f, "Left drag limit fully reveals the fifth mode");
        Approximately(202.5f, fiveSlots.MaxOffset, 0.01f, "Right drag limit fully reveals the first four modes");
        Approximately(202.5f, fiveSlots.DefaultOffset, 0.01f, "Initial position shows the native four modes");
        True(fiveSlots.DragEnabled, "Five mode slots enable horizontal dragging");

        var fourSlots = ModeChoiceDragRangeService.Calculate(
            -785f,
            785f,
            355f,
            4,
            50f,
            1920f,
            4,
            96f);
        Approximately(0f, fourSlots.MinOffset, 0.01f, "Four fitting slots need no negative offset");
        Approximately(0f, fourSlots.MaxOffset, 0.01f, "Four fitting slots need no positive offset");
        False(fourSlots.DragEnabled, "Four fitting slots keep dragging disabled");
    }

    private static void TestCardCostHelpers()
    {
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "test_card",
                ["Expend"] = "6"
            },
            new Dictionary<string, string>
            {
                ["ExCost"] = "2",
                ["OnceExCost"] = "1",
                ["TotalExCost"] = "4"
            });

        Equal("test_card", CardConfigApi.Id(config), "CardConfigApi.Id reads data Id");
        Equal(11, CardConfigApi.CurrentCost(config), "CurrentCost caps scaled base cost and includes extra costs");
        Equal(6, CardConfigApi.BaseCost(config), "BaseCost reads only Expend");

        FightPlayer.Instance.Status.dynamicVariables["CardCost"] = 0.5f;
        Equal(10, CardConfigApi.CurrentCost(config), "CurrentCost honors the player CardCost multiplier");
        FightPlayer.Instance.Status.dynamicVariables.Clear();

        var negative = NewConfig(
            new Dictionary<string, string> { ["Expend"] = "-3" },
            new Dictionary<string, string> { ["ExCost"] = "-9" });
        Equal(0, CardConfigApi.CurrentCost(negative), "CurrentCost is clamped to zero");
        Equal(0, CardConfigApi.BaseCost(negative), "BaseCost is clamped to zero");
    }

    private static void TestStarBlessingCostOverrideStore()
    {
        var store = new StarBlessingCostOverrideStore();
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "star_blessing_target",
                ["Expend"] = "3"
            },
            new Dictionary<string, string>
            {
                ["ExCost"] = "1",
                ["OnceExCost"] = "-1",
                ["TotalExCost"] = "0"
            });

        Equal(3, CardConfigApi.CurrentCost(config), "Star blessing test card starts at its normal modified cost");
        True(store.BeginPreview(config, 2), "Star blessing begins one preview transaction");
        Equal(2, CardConfigApi.CurrentCost(config), "Star blessing preview displays halved rounded-up cost");
        False(store.BeginPreview(config, 2), "Star blessing preview is idempotent for the same card instance");
        store.Cancel(config);
        Equal("-1", config.Vars["OnceExCost"], "Cancelling star blessing restores the original one-use modifier");
        Equal(3, CardConfigApi.CurrentCost(config), "Cancelling star blessing restores the normal displayed cost");

        True(store.BeginPreview(config, 2), "Star blessing preview can begin again after cancellation");
        store.MarkBlessingConsumed(config);
        store.MarkActionObserved(config);
        True(store.ActionObserved(config), "Confirmed card action marks the preview transaction committed");
        var committed = store.Commit(config);
        True(committed.BlessingConsumed, "Committed transaction reports that the blessing was consumed");
        Equal("0", config.Vars["OnceExCost"], "Successful play consumes all one-use cost modifiers");
        Equal(4, CardConfigApi.CurrentCost(config), "The card returns to its normal non-once cost after successful play");

        True(store.BeginPreview(config, 2), "A later blessing can preview the same card again");
        store.CancelAll();
        Equal("0", config.Vars["OnceExCost"], "Fight cleanup restores every active preview");
        False(store.Contains(config), "Fight cleanup removes active preview state");
    }

    private static void TestCardGrantRequest()
    {
        var request = CardGrantRequest.ToHand("spark")
            .WithRuntimeTags("Burnout", "Burnout", "Nihility");
        Equal("Burnout,Nihility", request.RuntimeTags, "CardGrantRequest deduplicates runtime tags");

        var executor = new ScriptExecutor();
        FightCardManager.Instance.cardList.Clear();
        var result = CardApi.GrantCardToHand(
            executor,
            CardGrantRequest.ToHand("spark")
                .Configure(CardMutationService.SetTemporaryCostMutation(1))
                .Configure(CardMutationService.AddSpecialTagsMutation("A", "A", "B")));
        True(result.Success, "CardApi grant succeeds through the unified hand-delivery pipeline");
        Equal("spark", result.CardId, "CardApi grant returns the resolved card id");
        Equal("-1", result.Config!.Vars["TotalExCost"], "CardApi grant applies request mutations before delivery");
        Equal("2", result.Config!.data["Expend"], "CardApi grant mutations do not write base data");
        Equal("A,B", result.Config!.Vars["SpecialTag"], "CardApi grant applies deduplicated SpecialTag mutations");

        FightCardManager.Instance.cardList.Clear();
        var runtimeVarsResult = CardApi.GrantCardToHand(
            executor,
            CardGrantRequest.ToHand("runtime_state_card")
                .Configure("runtime-vars", config =>
                {
                    config.Vars["Name"] = "Runtime role card";
                    config.Vars["RuntimeFlag"] = "1";
                }));
        True(runtimeVarsResult.Success, "CardApi grant keeps runtime Vars writable while base data remains read-only");
        True(runtimeVarsResult.Config!.data is System.Collections.ObjectModel.ReadOnlyDictionary<string, string>, "CardApi grant preserves the game's read-only base data contract");
        Equal("Runtime role card", runtimeVarsResult.Config!.Vars["Name"], "CardApi grant accepts runtime display state through Vars");
        Equal("1", runtimeVarsResult.Config!.Vars["RuntimeFlag"], "CardApi grant accepts runtime flags through Vars");

        FightCardManager.Instance.cardList.Clear();
        var failing = new ScriptExecutor { ThrowOnDelivery = true };
        var failed = CardApi.GrantCardToHand(failing, CardGrantRequest.ToHand("spark"));
        False(failed.Success, "CardApi grant returns structured failure on delivery errors");
        Equal("deliver", failed.FailureStep, "CardApi grant identifies the failing step");
        Equal(0, FightCardManager.Instance.cardList.Count, "CardApi grant cleans up created combat cards when delivery fails");
    }

    private static void TestCardMutationService()
    {
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "guided",
                ["Expend"] = "3",
                ["Tag"] = "Native"
            },
            new Dictionary<string, string>());

        CardMutationService.SetTemporaryCost(config, 1);
        Equal("-2", config.Vars["TotalExCost"], "Temporary cost is expressed through TotalExCost");
        Equal("3", config.data["Expend"], "Temporary cost leaves base Expend read-only");

        True(CardMutationService.AddSpecialTags(config, "Guidance", "Guidance", "Derived"), "Special tags are added once");
        Equal("Guidance,Derived", config.Vars["SpecialTag"], "Special tags are deduplicated");
        False(CardMutationService.AddSpecialTags(config, "Guidance"), "Existing SpecialTags are not rewritten");
        True(CardMutationService.AddNativeTags(config, "Burnout", "Burnout"), "Native tags are added once");
        Equal("Native,Burnout", config.Vars["Tag"], "Native tags are deduplicated in Vars.Tag");
        Equal("Native", config.data["Tag"], "Native tag mutations do not write base data.Tag");
        False(CardMutationService.AddNativeTags(config, "Burnout"), "Existing native tags are not rewritten");

        CardMutationService.MarkTemporaryWhiteRadiance(config);
        Equal("1", config.Vars[SunExpIds.TempWhiteRadiance], "Temporary white radiance marker is set");
        Equal("0", config.Vars[SunExpIds.TempWhiteRadianceResolved], "Temporary white radiance starts unresolved");
        True(CardMutationService.HasSpecialTag(config, SunExpIds.WhiteRadianceTag), "Temporary white radiance adds the white-radiance SpecialTag");
    }

    private static void TestRuntimeCardAttachmentService()
    {
        ExecutorApi.ResetCombatVars();
        FightCardManager.Instance.cardList.Clear();
        Witch.UI.Window.FightUI.cardItemList.Clear();
        Witch.UI.Window.FightUI.WaitCard.Clear();

        var config = new DataConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "temporary_hand_card",
                ["Tag"] = ""
            },
            new Dictionary<string, string>());
        var card = new CardItem
        {
            dataConfig = config,
            Vars = config.Vars,
            data = new Dictionary<string, string>
            {
                ["Id"] = "temporary_hand_card",
                ["Tag"] = ""
            }
        };
        var executor = new ScriptExecutor();
        executor.HandCard.Add(card);
        Witch.UI.Window.FightUI.cardItemList.Add(card);

        var result = RuntimeCardAttachmentService.AttachToCurrentHand(
            executor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment());

        Equal(1, result.TouchedCardItems, "Runtime attachment touches the current hand card once");
        Equal(1, result.TouchedConfigs, "Runtime attachment touches the hand card config once");
        True(result.Changed > 0, "Runtime attachment records marker/tag changes");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "Tag"), "Burnout"), "Runtime attachment writes native tags to card item Vars.Tag");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout"), "Runtime attachment writes native tags to config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), "Burnout"), "Runtime attachment does not write base config data.Tag");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment writes SpecialTag to card item Vars");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment writes SpecialTag to config Vars");
        Equal("1", config.Vars[SunExpIds.TempWhiteRadiance], "Runtime attachment marks temporary white radiance on config");
        Equal(card.Vars[SunExpIds.TempWhiteRadianceLockId], config.Vars[SunExpIds.TempWhiteRadianceLockId], "Card item and config share the temporary white radiance lock");
        True(CardConfigApi.HasTemporaryWhiteRadiance(config), "Runtime attachment is visible to the white-radiance trigger runtime");
        False(CardConfigApi.HasNativeWhiteRadiance(config), "Runtime hand attachment does not turn white radiance into a native run tag");

        var cleared = RuntimeCardAttachmentService.ClearTemporaryAttachments("test");
        True(cleared > 0, "Runtime attachment cleanup removes temporary card vars at the next fight boundary");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup removes temporary Burnout from config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from config Vars.SpecialTag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.TempWhiteRadiance), "Runtime attachment cleanup removes the temporary marker from card Vars");
        False(config.Vars.ContainsKey(SunExpIds.TempWhiteRadiance), "Runtime attachment cleanup removes temporary white radiance state");
        False(config.Vars.ContainsKey(SunExpIds.TempWhiteRadianceLockId), "Runtime attachment cleanup removes the temporary white radiance lock");
        False(card.Tags.Contains("Burnout"), "Runtime attachment cleanup removes temporary Burnout from visible card tags");
        False(card.Tags.Contains(WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from visible card tags");

        FightCardManager.Instance.cardList.Clear();
        Witch.UI.Window.FightUI.cardItemList.Clear();
        Witch.UI.Window.FightUI.WaitCard.Clear();
        ExecutorApi.ResetCombatVars();

        var waitConfig = new DataConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "temporary_wait_card",
                ["Tag"] = ""
            },
            new Dictionary<string, string>());
        var waitCard = new CardItem
        {
            dataConfig = waitConfig,
            Vars = waitConfig.Vars,
            data = new Dictionary<string, string>
            {
                ["Id"] = "temporary_wait_card",
                ["Tag"] = ""
            }
        };
        var waitExecutor = new ScriptExecutor();
        waitExecutor.WaitCard.Add(waitCard);
        Witch.UI.Window.FightUI.WaitCard.Add(waitCard);

        var waitResult = RuntimeCardAttachmentService.AttachToCurrentHand(
            waitExecutor,
            RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment());

        Equal(1, waitResult.TouchedCardItems, "Runtime attachment touches wait-list hand cards");
        Equal(1, waitResult.TouchedConfigs, "Runtime attachment touches wait-list configs once");
        True(waitResult.ExecutorWaitCards > 0, "Runtime attachment scans executor WaitCard");
        True(waitResult.UiWaitCards > 0, "Runtime attachment scans FightUI WaitCard");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitCard.Vars, "Tag"), "Burnout"), "Runtime attachment writes native tags to wait-list card item Vars.Tag");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment writes SpecialTag to wait-list config Vars");

        RuntimeCardAttachmentService.ClearTemporaryAttachments("test.wait");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitConfig.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup removes temporary Burnout from wait-list config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(waitConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from wait-list config Vars.SpecialTag");

        FightCardManager.Instance.cardList.Clear();
        Witch.UI.Window.FightUI.cardItemList.Clear();
        Witch.UI.Window.FightUI.WaitCard.Clear();

        var nativeBurnoutConfig = new DataConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "native_burnout_card",
                ["Tag"] = "Burnout"
            },
            new Dictionary<string, string>
            {
                ["Tag"] = "Burnout",
                ["SpecialTag"] = WhiteRadiance,
                [SunExpIds.RuntimeMarkersKey] = SunExpIds.TempWhiteRadiance,
                [SunExpIds.TempWhiteRadiance] = "1"
            });
        FightCardManager.Instance.cardList.Add(nativeBurnoutConfig);

        RuntimeCardAttachmentService.ClearTemporaryAttachments("test.legacy");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup preserves native Burnout when base data owns it");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes legacy temporary white radiance without a snapshot");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.TempWhiteRadiance), "Runtime attachment cleanup removes legacy temporary markers without a snapshot");
        False(nativeBurnoutConfig.Vars.ContainsKey(SunExpIds.TempWhiteRadiance), "Runtime attachment cleanup removes legacy temporary state without a snapshot");
    }

    private static void TestSolarTriggerCostOverride()
    {
        var config = NewConfig(
            new Dictionary<string, string> { ["Id"] = "flamewheel_recurrence" },
            new Dictionary<string, string> { [SunExpIds.SolarTriggerCost] = "5" });

        Equal(5, CardConfigApi.ResolveSolarTriggerCost(config, 1), "Solar trigger override wins over fallback");
        CardConfigApi.ClearSolarTriggerCost(config);
        Equal("", config.Vars[SunExpIds.SolarTriggerCost], "ClearSolarTriggerCost blanks the override var");
        Equal(1, CardConfigApi.ResolveSolarTriggerCost(config, 1), "ResolveSolarTriggerCost falls back after clear");
    }

    private static void TestWhiteRadianceTags()
    {
        var native = NewConfig(
            new Dictionary<string, string> { ["Tag"] = "Burnout," + WhiteRadiance },
            new Dictionary<string, string>());
        True(CardConfigApi.HasNativeWhiteRadiance(native), "Native white radiance is read from Vars.Tag");

        var temporary = NewConfig(
            new Dictionary<string, string> { ["Tag"] = "" },
            new Dictionary<string, string>
            {
                ["SpecialTag"] = WhiteRadiance,
                [SunExpIds.TempWhiteRadiance] = "1"
            });
        True(CardConfigApi.HasTemporaryWhiteRadiance(temporary), "Temporary white radiance requires marker and SpecialTag");
        True(CardConfigApi.HasSpecialWhiteRadiance(temporary), "Special white radiance is read from Vars.SpecialTag");
        False(CardConfigApi.HasNativeWhiteRadiance(temporary), "Temporary white radiance is not native");
    }

    private static void TestTemporaryWhiteRadianceClaim()
    {
        ExecutorApi.ResetCombatVars();
        var config = NewConfig();

        True(CardConfigApi.TryClaimTemporaryWhiteRadiance(config), "First temporary white radiance claim succeeds");
        Equal("1", config.Vars[SunExpIds.TempWhiteRadianceResolved], "Successful claim marks card resolved");
        False(CardConfigApi.TryClaimTemporaryWhiteRadiance(config), "Second claim on the same card is blocked");

        var stale = NewConfig(vars: new Dictionary<string, string>
        {
            [SunExpIds.TempWhiteRadianceLockId] = config.Vars[SunExpIds.TempWhiteRadianceLockId],
            [SunExpIds.TempWhiteRadianceResolved] = "0"
        });
        True(CardConfigApi.TryClaimTemporaryWhiteRadiance(stale), "A stale unresolved card lock is renewed");
        NotEqual(config.Vars[SunExpIds.TempWhiteRadianceLockId], stale.Vars[SunExpIds.TempWhiteRadianceLockId], "Renewed stale lock receives a new id");
    }

    private static void TestLoneerStateOwnership()
    {
        LoneerCombatStateStore.ClearAll();
        var owner = new FakeStatus("loneer-a");
        var other = new FakeStatus("loneer-b");
        var selectedFromCareer = LoneerCombatStateStore.ResetForFight(owner)!;
        selectedFromCareer.GuidanceCardId = "selected-guide";
        selectedFromCareer.ClockValue = 7;
        selectedFromCareer.SelectionVersion = 2;

        var readFromSkill = LoneerCombatStateStore.GetOrCreate(owner)!;
        True(ReferenceEquals(selectedFromCareer, readFromSkill), "Loneer state is shared across executors for the same owner");
        Equal("selected-guide", readFromSkill.GuidanceCardId, "Guidance survives executor changes");
        Equal(7, readFromSkill.ClockValue, "Miracle Clock state survives executor changes");
        Equal(2, readFromSkill.SelectionVersion, "Guidance selection version survives executor changes");

        var isolated = LoneerCombatStateStore.GetOrCreate(other)!;
        Equal("", isolated.GuidanceCardId, "Different owners receive isolated guidance state");
        Equal(0, isolated.ClockValue, "Different owners receive isolated Miracle Clock state");
        LoneerCombatStateStore.Remove(owner);
        Equal("", LoneerCombatStateStore.GetOrCreate(owner)!.GuidanceCardId, "Removed combat state does not leak into the next fight");
    }

    private static void TestStarScoreWindow()
    {
        StarScoreCombatStateStore.ClearAll();
        var owner = new FakeStatus("score-owner");
        var score = StarScoreCombatStateStore.GetOrCreate(owner)!;
        score.Record("S", 3);
        score.Record("U", 3);
        score.Record("T", 3);
        score.RecordCompletedCadence("SUT");
        var preview = score.Snapshot(owner.InstanceId, isCadencePreview: true, completedCadencePattern: "SUT");
        Equal(3, preview.Notes.Count, "Star score HUD preview exposes the full completed cadence");
        Equal(StarScoreNote.Turn, preview.Notes[2], "Star score HUD preview keeps typed note identity");
        True(preview.IsCadencePreview, "Star score HUD preview is flagged before cadence collapse");
        Equal("SUT", preview.CompletedCadencePattern, "Star score HUD preview records the completed cadence pattern");

        score.RetainLastNoteAsCadenceStart();

        Equal(1, score.Notes.Count, "Star score keeps the last note after a completed cadence");
        Equal(StarScoreNote.Turn, score.Notes[0], "Star score reuses the last overture as the next cadence start");
        Equal("T", StarScoreNoteCodes.PatternFromNotes(score.Notes), "Star score converts retained notes back to cadence pattern codes");
        score.Record("C", 3);
        score.Record("S", 3);
        Equal(3, score.Notes.Count, "Star score builds the next cadence from the retained note");
        Equal(StarScoreNote.Turn, score.Notes[0], "Star score retained note remains the first note of the next cadence");
        True(ReferenceEquals(score, StarScoreCombatStateStore.GetOrCreate(owner)), "Star score is shared across card executors for the same owner");

        var openingCadence = StarScoreCadenceCatalog.Resolve(new[] { StarScoreNote.Opening, StarScoreNote.Opening, StarScoreNote.Opening });
        Equal("\u542f\u542f\u542f\uff1a\u6025\u677f\u3002\u53cb\u65b9\u5168\u4f53\u4f59\u97f3+1\uff1b\u53cb\u65b9\u5168\u4f53\u62bd2\u5f20\u724c", openingCadence.DisplayText, "Opening cadence tooltip text matches the design copy");
        var defaultCadence = StarScoreCadenceCatalog.Resolve(new[] { StarScoreNote.Opening, StarScoreNote.Sustain, StarScoreNote.Opening });
        Equal("\u542f\u627f\u542f\uff1a\u4e09\u58f0\u548c\u5f26\u3002\u53cb\u65b9\u5168\u4f53\u62bd1\u5f20\u724c", defaultCadence.DisplayText, "Default cadence tooltip text matches the design copy");
        var candidates = StarScoreCadenceCatalog.CandidatesForPrefix(new[] { StarScoreNote.Opening, StarScoreNote.Sustain });
        Equal(4, candidates.Count, "Two-note star score prefixes enumerate four possible third notes");
        True(candidates.Any(row => row.DisplayText == "\u542f\u627f\u8f6c\uff1a\u8c03\u5f8b\u3002\u81ea\u8eab\u4f59\u97f3+1\uff1b\u53cb\u65b9\u5168\u4f53\u4f59\u97f3+1"), "Candidate list includes the named tuning cadence");
    }

    private static FakeDataConfig NewConfig(
        IDictionary<string, string>? data = null,
        IDictionary<string, string>? vars = null)
    {
        return new FakeDataConfig(data, vars);
    }

    private static void True(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + message);
        }
    }

    private static void False(bool condition, string message)
    {
        True(!condition, message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Assertion failed: " + message + ". Expected <" + expected + ">, got <" + actual + ">.");
        }
    }

    private static void Approximately(float expected, float actual, float tolerance, string message)
    {
        assertions++;
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException("Assertion failed: " + message + ". Expected <" + expected + ">, got <" + actual + ">.");
        }
    }

    private static void NotEqual<T>(T unexpected, T actual, string message)
    {
        assertions++;
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new InvalidOperationException("Assertion failed: " + message + ". Did not expect <" + actual + ">.");
        }
    }

    private sealed class FakeDataConfig : IDataConfig
    {
        public FakeDataConfig(IDictionary<string, string>? data, IDictionary<string, string>? vars)
        {
            this.data = data ?? new Dictionary<string, string>();
            Vars = vars ?? new Dictionary<string, string>();
            InstanceID = Guid.NewGuid().ToString("N");
        }

        public IDictionary<string, string> data { get; set; }

        public IDictionary<string, string> Vars { get; }

        public string InstanceID { get; }

        public DataType Type => DataType.Card;

        public IScriptExecutor scriptExecutor => throw new NotSupportedException();

        public bool isCompiling => false;
    }
}
'@
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-SourceAssertions {
    param([string]$RepoRoot)

    $bossMirrorName = [regex]::Unescape('\u767d\u66dc\u955c\u9635')
    $bossMirrorOldName = [regex]::Unescape('\u767d\u66dc\u955c\u9635\u00b7\u4e09\u5343\u73af\u65e5\u955c')
    $bossSecondSunName = [regex]::Unescape('\u65e0\u6148\u7b2c\u4e8c\u65e5\u8f6e')
    $bossSecondSunOldName = [regex]::Unescape('\u65e0\u6148\u7b2c\u4e8c\u65e5\u8f6e\u00b7\u7ec8\u65e5\u6001')
    $bossSaintWunaName = [regex]::Unescape('\u767d\u66dc\u5723\u5973\u00b7\u4e4c\u5a1c')
    $solarMemoryPrefix = [regex]::Unescape('\u65e5\u8000\u56de\u5fc6\u00b7')
    $solarMemoryTraditionalPrefix = [regex]::Unescape('\u65e5\u8000\u56de\u61b6\u00b7')

    $executorApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\ExecutorApi.cs"))
    $sunExpIds = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpIds.cs"))
    $sunExpFieldId = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpFieldId.cs"))
    $playerApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\PlayerApi.cs"))
    $cardApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardApi.cs"))
    $roleSkillApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\RoleSkillApi.cs"))
    $cardMutationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardMutationService.cs"))
    $polymorphActivationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\PolymorphActivationService.cs"))
    $polymorphBuffService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\PolymorphBuffService.cs"))
    $polymorphCooldownService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\PolymorphCooldownService.cs"))
    $polymorphRuntimeService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\PolymorphRuntimeService.cs"))
    $polymorphStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\PolymorphStateStore.cs"))
    $projectionActivationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\ProjectionActivationService.cs"))
    $projectionOtherObj = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\ProjectionOtherObj.cs"))
    $heartChangeControlService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\HeartChangeControlService.cs"))
    $heartChangeActionProxyObj = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\HeartChangeActionProxyObj.cs"))
    $heartChangeIntentService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\HeartChangeIntentService.cs"))
    $projectionStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\ProjectionStateStore.cs"))
    $projectionStrategyService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\ProjectionStrategyService.cs"))
    $projectionSummonService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\ProjectionSummonService.cs"))
    $companionBattleModels = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionBattleModels.cs"))
    $companionBattleStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionBattleStateStore.cs"))
    $companionIntentRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionIntentRegistry.cs"))
    $companionIntentSelector = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionIntentSelector.cs"))
    $companionSlotService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionSlotService.cs"))
    $companionStatsService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionStatsService.cs"))
    $companionThreatService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CompanionThreatService.cs"))
    $companionIntentRegistryJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\companion.intent.registry.json"))
    $runtimeCardAttachmentService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\RuntimeCardAttachmentService.cs"))
    $starBlessingCostOverrideStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarBlessingCostOverrideStore.cs"))
    $cardGrantRecipes = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardGrantRecipes.cs"))
    $specialTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SpecialTagRuntime.cs"))
    $companionThreatRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\CompanionThreatRuntime.cs"))
    $cardConfigApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"))
    $gameCompatibilityApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\GameCompatibilityApi.cs"))
    $cardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\CardScripts.cs"))
    $relicScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\RelicScripts.cs"))
    $morningStarCardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\MorningStarCardScripts.cs"))
    $buffScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BuffScripts.cs"))
    $buffApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BuffApi.cs"))
    $scriptEventApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\ScriptEventApi.cs"))
    $fieldApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\FieldApi.cs"))
    $buffOverflowApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BuffOverflowApi.cs"))
    $eventScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\EventScripts.cs"))
    $bossScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BossScripts.cs"))
    $entry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Entry.cs"))
    $wunaScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\WunaScripts.cs"))
    $emberAdventureStateService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EmberAdventureStateService.cs"))
    $emberAdventureStateRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EmberAdventureStateRuntime.cs"))
    $rpcEmberAdventureStateCommit = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Network\RpcEmberAdventureStateCommit.cs"))
    $enemyApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\EnemyApi.cs"))
    $endlessAbyssEnemyInjectionService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessAbyssEnemyInjectionService.cs"))
    $runtimeHooks = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\RuntimeHooks.cs"))
    $sunExpCombatActionRouter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpCombatActionRouter.cs"))
    $sunExpStatusLifecycleRouter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpStatusLifecycleRouter.cs"))
    $sunExpCardPresentationRouter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpCardPresentationRouter.cs"))
    $sunExpCardPresentationLifecycleBridge = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpCardPresentationLifecycleBridge.cs"))
    $polymorphRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\PolymorphRuntime.cs"))
    $projectionRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ProjectionRuntime.cs"))
    $heartChangeControlRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\HeartChangeControlRuntime.cs"))
    $duskPartnerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\DuskPartnerRuntime.cs"))
    $starClayDollRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\StarClayDollRuntime.cs"))
    $loneerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\LoneerRuntime.cs"))
    $starScoreRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\StarScoreRuntime.cs"))
    $starScoreHudRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\StarScoreHudRuntime.cs"))
    $loneerService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerMiracleService.cs"))
    $starStonePouchService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarStonePouchService.cs"))
    $loneerState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerCombatState.cs"))
    $cardSelectionApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardSelectionApi.cs"))
    $starScoreService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreService.cs"))
    $starScoreState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreCombatState.cs"))
    $starScoreNote = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreNote.cs"))
    $starScoreSnapshot = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreDisplaySnapshot.cs"))
    $starScoreCadenceCatalog = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs"))
    $duskPartnerScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\DuskPartnerScripts.cs"))
    $starClayDollScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\StarClayDollScripts.cs"))
    $projectionScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\ProjectionScripts.cs"))
    $heartChangeScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\HeartChangeScripts.cs"))
    $scriptingSource = [string]::Join("`n", (Get-ChildItem -LiteralPath (Join-Path $RepoRoot "SunExp-Dev\Scripting") -File -Filter "*.cs" | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))
    $solarEventRuntimePath = Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarEventRuntime.cs"
    $battleRewardApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BattleRewardApi.cs"))
    $battleRewardAdjustmentService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\BattleRewardAdjustmentService.cs"))
    $battleRewardAdjustmentRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs"))
    $solarMemoryRewardRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs"))
    $solarMemoryModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"))
    $solarMemoryModeEntryRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryModeEntryRuntime.cs"))
    $solarMemoryMapVisualRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryMapVisualRuntime.cs"))
    $solarMemoryCombatRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryCombatRuntime.cs"))
    $cardVisualSkinRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\CardVisualSkinRuntime.cs"))
    $polymorphCardFaceRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Visual\PolymorphCardFaceRuntime.cs"))
    $modeChoiceEntryDefinition = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ModeChoiceEntryDefinition.cs"))
    $modeChoiceEntryRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ModeChoiceEntryRegistry.cs"))
    $modeChoiceLayoutRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ModeChoiceLayoutRuntime.cs"))
    $solarMemoryRunLauncher = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs"))
    $solarMemoryContentIsolationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryContentIsolationRuntime.cs"))
    $solarMemoryMapItemAnimationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryMapItemAnimationRuntime.cs"))
    $mapNodeCardArtRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs"))
    $mapNodeCardArtRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs"))
    $visualRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\VisualRegistry.cs"))
    $visualRegistryJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\visual.registry.json"))
    $mapItemApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\MapItemApi.cs"))
    $mapNodeTextureFitService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs"))
    $sunExpHardTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpHardTagRuntime.cs"))
    $solarMemoryStarterDeckRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"))
    $endlessSeaIntroBoardRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EndlessSeaIntroBoardRuntime.cs"))
    $endlessSeaRunLauncher = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EndlessSeaRunLauncher.cs"))
    $endlessSeaSaveCacheRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EndlessSeaSaveCacheRuntime.cs"))
    $endlessSeaModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EndlessSeaModeRuntime.cs"))
    $endlessSeaMapViewPresenter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\EndlessSeaMapViewPresenter.cs"))
    $endlessSeaNetworkSync = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Network\EndlessSeaNetworkSync.cs"))
    $sunExpNetworkRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Network\SunExpNetworkRuntime.cs"))
    $endlessSeaFloorPlanner = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaFloorPlanner.cs"))
    $endlessSeaMapBuilder = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaMapBuilder.cs"))
    $endlessSeaMapProjectionService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaMapProjectionService.cs"))
    $endlessSeaSelectableNodeDeckPlanner = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaSelectableNodeDeckPlanner.cs"))
    $sunExpSkillCgRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Features\SkillCg\SunExpSkillCgRuntime.cs"))
    $auraCgRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AuraCgShared\AuraCgRuntime.cs"))
    $endlessSeaStarterDeckCatalog = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaStarterDeckCatalog.cs"))
    $endlessSeaRichTextSanitizer = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaRichTextSanitizer.cs"))
    $endlessSeaOriginService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaOriginService.cs"))
    $endlessSeaCardAffixRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EndlessSeaCardAffixRuntime.cs"))
    $endlessSeaCardAffixService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaCardAffixService.cs"))
    $endlessSeaCombatRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\EndlessSeaCombatRuntime.cs"))
    $endlessAbyssConfig = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessAbyssConfig.cs"))
    $endlessAbyssConfigJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\endless_abyss.config.json"))
    $endlessAbyssRewardPoolService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessAbyssRewardPoolService.cs"))
    $endlessAbyssMilestoneRewardService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessAbyssMilestoneRewardService.cs"))
    $endlessAbyssRunLedger = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessAbyssRunLedger.cs"))
    $endlessAbyssMilestoneRewardPanel = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\EndlessAbyssMilestoneRewardPanel.cs"))
    $endlessAbyssShockPanel = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\EndlessAbyssShockPanel.cs"))
    $endlessSeaRunStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\EndlessSeaRunStateStore.cs"))
    $modeChoiceSaveCacheApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\ModeChoiceSaveCacheApi.cs"))
    $solarMemorySetupFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"))
    $solarMemoryBlessingPickerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"))
    $solarMemoryPreparationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPreparationRuntime.cs"))
    $solarMemoryPlayerSetupState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPlayerSetupState.cs"))
    $dialogueFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\DialogueFlowRuntime.cs"))
    $dialogueFlowService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\DialogueFlowService.cs"))
    $solarMemoryStoryGateService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryStoryGateService.cs"))
    $solarMemoryFlowApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\SolarMemoryFlowApi.cs"))
    $solarMemoryRoleCommitApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\SolarMemoryRoleCommitApi.cs"))
    $solarMemoryRoleCommit = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Network\RpcSolarMemoryRoleCommit.cs"))
    $dirtyState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpDirtyState.cs"))
    $sunExpUiSafety = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiSafety.cs"))
    $sunExpUiBuilder = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiBuilder.cs"))
    $sunExpModalHost = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpModalHost.cs"))
    $sunExpUiLifetimeScope = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiLifetimeScope.cs"))
    $sunExpUiPool = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiPool.cs"))
    $sunExpUiSprites = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiSprites.cs"))
    $starScoreHudAssets = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\StarScoreHudAssets.cs"))
    $starScoreHudView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\StarScoreHudView.cs"))
    $starScoreHudHoverProbe = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\StarScoreHudHoverProbe.cs"))
    $starScoreHudTooltipView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\StarScoreHudTooltipView.cs"))
    $sunExpProject = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\SunExp.Dll.csproj"))
    $audioArbiterRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AudioArbiterShared\AudioArbiterRuntime.cs"))
    $battleBgmArbiterRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "BattleBgmArbiterShared\BattleBgmArbiterRuntime.cs"))
    $modConfig = Get-Content -LiteralPath (Join-Path $RepoRoot "SunExp\ModConfig.json") -Raw | ConvertFrom-Json
    $solarMemoryMapNodePoolFactory = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolFactory.cs"))
    $solarMemoryMapNodePoolApplier = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolApplier.cs"))
    $mapNodeSafetyService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeSafetyService.cs"))
    $mapData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Map\sunexp.csv"))
    $mapText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Map\sunexp.csv"))
    $levelData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Level\sunexp.csv"))
    $enemyData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Enemy\sunexp.csv"))
    $enemyText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Enemy\sunexp.csv"))
    $enemyCardData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\EnemyCard\sunexp.csv"))
    $enemyCardText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\EnemyCard\sunexp.csv"))
    $buffData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Buff\sunexp.csv"))
    $buffText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Buff\sunexp.csv"))
    $enchTagData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\EnchTag\sunexp.csv"))
    $keywordText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\KeyWordsDic\sunexp.csv"))
    $eventData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\EventList\sunexp.csv"))
    $eventText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\EventList\sunexp.csv"))
    $dialogueData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Dialogue\sunexp.csv"))
    $solarMemoryRoleData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\RoleData\solar_memory.csv"))
    $loneerRoleData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\RoleData\loneer.csv"))
    $blessingData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Blessing\sunexp.csv"))
    $partnerData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Partner\sunexp.csv"))
    $cardDataPath = Join-Path $RepoRoot "SunExp\Data\Card\sunexp.csv"
    $cardData = [System.IO.File]::ReadAllText($cardDataPath)
    $cardTextPath = Join-Path $RepoRoot "SunExp\Text\Card\sunexp.csv"
    $cardText = [System.IO.File]::ReadAllText($cardTextPath)
    $loneerCareerText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Career\loneer.csv"))

    $addStatusBuff = [regex]::Match($executorApi, "public\s+static\s+bool\s+AddStatusBuff[\s\S]*?public\s+static\s+bool\s+RemoveStatusBuff")
    Assert-True $addStatusBuff.Success "Could not locate ExecutorApi.AddStatusBuff for source assertion."
    Assert-True (-not $addStatusBuff.Value.Contains("HandleBurnOverflow")) "AddStatusBuff must not call HandleBurnOverflow; burn overflow is handled by the StatusManager.AddBuff hook."
    $tryAddEvent = [regex]::Match($scriptEventApi, "public\s+static\s+bool\s+TryAddEvent[\s\S]*?public\s+static\s+bool\s+TryAddTokenedEvent")
    Assert-True $tryAddEvent.Success "Could not locate ScriptEventApi.TryAddEvent for source assertion."
    Assert-True $tryAddEvent.Value.Contains("executor.Self == null") "TryAddEvent must skip preview/dictionary executors without Self before calling AddEvent."
    Assert-True $tryAddEvent.Value.Contains("catch (Exception ex)") "TryAddEvent must catch all event registration failures and degrade safely."
    Assert-True $executorApi.Contains("public static bool TryAddTokenedEvent") "ExecutorApi must expose a token-guarded event registration wrapper."
    Assert-True $executorApi.Contains("public static bool TryAddTempEvent") "ExecutorApi must expose a safe temporary event registration wrapper."
    Assert-True (-not [regex]::IsMatch($scriptingSource, '\.\s*Add(?:Temp)?Event\s*\(')) "Scripting modules must route event registration through ExecutorApi wrappers."
    Assert-True $executorApi.Contains("public static bool ClearFieldBuff") "ExecutorApi.ClearFieldBuff is missing."
    Assert-True $sunExpFieldId.Contains("public enum SunExpFieldId") "SunExpFieldId must define enum-like field ids."
    Assert-True $sunExpFieldId.Contains("ScorchingCanopy") "SunExpFieldId must include ScorchingCanopy."
    Assert-True $fieldApi.Contains("private static int TotalFieldBuffStacks") "Field stacks must be recomputed from combat statuses."
    Assert-True $fieldApi.Contains("foreach (var status in FightManager.Instance.statuses.Values)") "Field sync must scan FightManager statuses."
    Assert-True $fieldApi.Contains('CombatVarApi.AddInt(FieldCombatKey(field, "Epoch"), 1);') "Field state changes must advance a shared epoch."
    Assert-True $fieldApi.Contains("SyncFieldStacks(ScriptExecutor? executor, SunExpFieldId field)") "Field sync must expose the enum-based overload."
    Assert-True (-not [regex]::IsMatch($fieldApi, 'ClearFieldBuff[\s\S]*?SetSharedFieldState\(fieldId,\s*0\)')) "ClearFieldBuff must resync field state instead of blindly clearing shared stacks."
    Assert-True $buffScripts.Contains("ExecutorApi.TryAddTokenedEvent(self, SunExpIds.ScorchingCanopy + ""OnLevelChange""") "Scorching Canopy must resync when its carrier buff level changes through the shared event wrapper."
    Assert-True $buffScripts.Contains("ExecutorApi.SyncFieldStacks(self, SunExpFieldId.ScorchingCanopy);") "Scorching Canopy apply/clear must use enum-based field sync."
    Assert-True $executorApi.Contains("public static int BurnUpperBound(IStatusManager? target)") "ExecutorApi must expose a dynamic burn upper bound helper."
    Assert-True $buffOverflowApi.Contains("private const int BurnUpperBoundFallback = 1;") "Invalid burn upper bounds must fall back to the minimum valid stack count."
    Assert-True $buffOverflowApi.Contains("target.GetBuff(buffId)?.buffConfig?.UpperBound") "Burn upper bound must prefer the live BuffItemConfig.UpperBound."
    Assert-True $buffOverflowApi.Contains('GetOne(DataType.Buff, buffId)') "Burn upper bound must fall back to the current Buff data row."
    Assert-True $buffOverflowApi.Contains("var upperBound = BurnUpperBound(target);") "Burn overflow must use the dynamic burn upper bound."
    Assert-True $sunExpStatusLifecycleRouter.Contains("SunExpHookTargets.StatusManagerAddBuff") "Burn overflow must route StatusManager.AddBuff through the shared status lifecycle router."
    Assert-True $runtimeHooks.Contains('SunExpStatusLifecycleRouter.Register("RuntimeStatusBuff"') "RuntimeHooks must subscribe burn overflow to the shared StatusManager.AddBuff lifecycle."
    Assert-True $runtimeHooks.Contains("BeforeAddBuff = OnStatusManagerAddBuffBefore") "Burn overflow must prepare before real StatusManager.AddBuff execution."
    Assert-True $runtimeHooks.Contains("AfterAddBuff = OnStatusManagerAddBuffAfter") "Solar Radiance cap repair must run after StatusManager.AddBuff creation."
    Assert-True (-not $runtimeHooks.Contains('RegisterBefore(modConfig, "ScriptExecutor.AddBuff", OnScriptExecutorAddBuffBefore);')) "Burn overflow must not hook ScriptExecutor.AddBuff because it can mutate the active target list."
    Assert-True $buffOverflowApi.Contains("target.AddBuff(SunExpIds.BodyBurn, overflow);") "Burn overflow must add body burn directly to the resolved status target."
    Assert-True $buffOverflowApi.Contains("private const int SolarRadianceDefaultUpperBound = 12;") "Solar Radiance default upper bound must be 12."
    Assert-True $buffOverflowApi.Contains("private const int WunaSolarRadianceUpperBound = 15;") "Wuna Solar Radiance upper bound must be 15."
    Assert-True $executorApi.Contains("public static void PrepareSolarRadianceUpperBound") "ExecutorApi must prepare live Solar Radiance caps before AddBuff."
    Assert-True $executorApi.Contains("public static void FinalizeSolarRadianceUpperBound") "ExecutorApi must repair Wuna Solar Radiance caps after AddBuff."
    Assert-True $buffApi.Contains("public static bool IsWunaPlayerStatus") "BuffApi must expose a target-specific Wuna player status check."
    Assert-True $buffApi.Contains("PlayerApi.LocalPlayerStatusId()") "Wuna-only Solar Radiance expansion must be limited to the local player status, not enemies."
    Assert-True $buffData.Contains('","0","0","0","12","Mods/SunExp/ModResource/Images/Buff/SunExp/solar_radiance"') "Solar Radiance data default upper bound must be 12."
    Assert-True $executorApi.Contains('DictionaryUtil.Set(executor?.Vars, "CanSelf", canSelf ? "True" : "False");') "SetBaseScript must explicitly write CanSelf for self-targetable attack cards."
    Assert-True $executorApi.Contains("public static IStatusManager? PrimaryTargetIncludingSelf") "ExecutorApi.PrimaryTargetIncludingSelf is missing."
    Assert-True $playerApi.Contains("public static string ScopedGameVarKey") "PlayerApi.ScopedGameVarKey is missing."
    Assert-True $runtimeHooks.Contains("EmberAdventureStateRuntime.Initialize(modConfig)") "Persistent Ember restore must be registered as a generic runtime hook, not Wuna-only career setup."
    Assert-True $emberAdventureStateRuntime.Contains("EmberAdventureStateService.RestoreForLocalPlayer") "Persistent Ember must restore through the generic adventure-state service."
    Assert-True (-not $wunaScripts.Contains("RestorePersistentEmber")) "Wuna career setup must not own generic Persistent Ember restoration."
    Assert-True $auraCgRuntime.Contains("TryPrepareLocalPlaybackBatch") "Shared Skill CG must generate playback ids only after confirming the local owner."
    Assert-True $auraCgRuntime.Contains("RpcSkillCgPlaybackRequest") "Shared Skill CG clients must submit playback requests to the host instead of broadcasting directly."
    Assert-True $auraCgRuntime.Contains("RpcSkillCgPlayback") "Shared Skill CG host must relay authorized playback to all clients."
    Assert-True $auraCgRuntime.Contains("TryClaimPlayback") "Shared Skill CG must keep a global playback pool for duplicate suppression."
    Assert-True $sunExpSkillCgRuntime.Contains("owner instance id is empty in multiplayer") "SunExp Skill CG must diagnose and skip empty owner ids in multiplayer."
    Assert-True $sunExpSkillCgRuntime.Contains("BuildRegisteredCardUseRequests(") "SunExp Skill CG must still include registered card-use CG requests."
    Assert-True $sunExpSkillCgRuntime.Contains("syncRemote: true") "SunExp Skill CG must request synchronized playback through the shared Skill CG runtime."
    Assert-True (-not $sunExpSkillCgRuntime.Contains("RpcSkillCgPlaybackRequest")) "SunExp Skill CG must not own private playback RPCs."
    Assert-True $wunaScripts.Contains("SunExpCardTagService.RequestBurnoutAndWhiteRadianceForFriendlyHands(self") "White Sun Prayer must schedule friendly hand Burnout and White Radiance tagging."
    Assert-True $runtimeCardAttachmentService.Contains("WunaWhiteSunPrayerHandAttachment") "Runtime card attachment service must expose Wuna hand attachment recipe."
    Assert-True $runtimeCardAttachmentService.Contains("WunaCoronationTokenAttachment") "Runtime card attachment service must expose Wuna coronation token attachment recipe."
    Assert-True $runtimeCardAttachmentService.Contains("MarkTemporaryWhiteRadiance") "Runtime card attachment service must mark temporary white radiance with a combat lock."
    Assert-True $runtimeCardAttachmentService.Contains("ClearTemporaryAttachments") "Runtime card attachment service must expose fight-boundary cleanup for temporary attachments."
    Assert-True $runtimeCardAttachmentService.Contains("CaptureOriginalVars") "Runtime card attachment cleanup must snapshot original Vars before adding temporary tags."
    Assert-True $specialTagRuntime.Contains('RuntimeCardAttachmentService.ClearTemporaryAttachments("Fight_Start.Init")') "Fight start must clear temporary runtime card attachments before the next battle can reuse cards."
    Assert-True $cardGrantRecipes.Contains("RuntimeCardAttachmentService.WunaCoronationTokenAttachment()") "Wuna coronation token grants must use the reusable temporary attachment service."
    Assert-True $cardGrantRecipes.Contains('.WithRuntimeTags("Burnout", "Froze")') "Wuna coronation token grants must carry Burnout and Froze runtime tags."
    Assert-True $wunaScripts.Contains("EmberAdventureStateService.CommitLocal(self?.Self") "Wuna scripts must write Persistent Ember through the generic adventure-state service."
    Assert-True $buffApi.Contains("EmberAdventureStateService.CommitLocal(status") "BuffApi.SavePersistentEmber must submit through the generic adventure-state service."
    Assert-True $emberAdventureStateService.Contains("PlayerApi.GetScopedGameVar(") "Persistent Ember sync must keep scoped GameVar compatibility fallback."
    Assert-True $emberAdventureStateService.Contains("SunExpIds.WunaPersistentEmber") "Persistent Ember sync must read the old Wuna key as a legacy fallback."
    Assert-True $emberAdventureStateService.Contains("OwnerGameVarKey") "Persistent Ember sync must persist by stable player/status owner key."
    Assert-True $rpcEmberAdventureStateCommit.Contains("ISunExpServerBoundRpcCommand") "Persistent Ember RPC must bind server sender authority."
    Assert-True $rpcEmberAdventureStateCommit.Contains("owner mismatch") "Persistent Ember RPC must reject payload owner ids that do not match the bound sender."
    $savePersistentEmberBlock = [regex]::Match($buffApi, "public\s+static\s+int\s+SavePersistentEmber[\s\S]*?private\s+static\s+IEnumerable")
    Assert-True ($savePersistentEmberBlock.Success -and -not $savePersistentEmberBlock.Value.Contains("IsWunaActive()")) "BuffApi.SavePersistentEmber must not be gated by Wuna activation."
    Assert-True ([regex]::IsMatch($buffApi, "SavePersistentEmber\(executor,\s*status\);\s*if\s*\(!IsWunaActive\(\)\)")) "Ember consumption must persist generic state before applying Wuna-only passive rewards."
    Assert-True $buffApi.Contains("return string.IsNullOrWhiteSpace(careerId)") "Wuna active fallback must not override an explicit non-Wuna career."
    Assert-True (-not [regex]::IsMatch($buffApi + $wunaScripts, "SetGameVar\s*\(\s*SunExpIds\.WunaPersistentEmber")) "Persistent Ember must not write to the legacy unscoped GameVar."
    Assert-True $cardScripts.Contains('["draw_flame"] = InitDrawFlame') "draw_flame must be registered for initialization."
    Assert-True ([regex]::IsMatch($cardScripts, 'private\s+static\s+void\s+InitDrawFlame[\s\S]*?ExecutorApi\.SetBaseScript\(self,\s+"AttackCardItem"\);')) "draw_flame must allow self-targeting during initialization."
    Assert-True $cardScripts.Contains("var target = ExecutorApi.PrimaryTargetIncludingSelf(self);") "draw_flame must resolve targets without excluding self."
    Assert-True $cardScripts.Contains("ExecutorApi.TriggerBurnAllEnemies(self, times * 2);") "flamewheel_recurrence must trigger enemy burn 2*N times while keeping N as the cost."
    Assert-True $cardScripts.Contains("ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, Math.Max(8, level), ""Target"");") "eclipse_hex must add current Burn stacks with an 8-stack minimum."
    Assert-True $buffScripts.Contains("return StatusApi.MaxHp(target) / 100 + 1;") "body_burn must deal 1% max HP + 1 true damage per stack."
    Assert-True (-not $specialTagRuntime.Contains("CardConfigApi.BaseCost")) "White radiance should use current actual play cost, not BaseCost."
    Assert-True $cardConfigApi.Contains("ReadPlayerCardCostMultiplier") "CardConfigApi must read the player CardCost multiplier."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.EnsureInCurrentLayer")) "RuntimeHooks must not inject SunExp events into normal adventure maps."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.RepairMapSelection")) "RuntimeHooks must not repair normal adventure map selections for SunExp events."
    Assert-True (-not [System.IO.File]::Exists($solarEventRuntimePath)) "The retired normal-mode solar event injector file must be removed."
    Assert-True $runtimeHooks.Contains("SolarMemoryContentIsolationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize the Solar Memory content isolation guard."
    Assert-True $runtimeHooks.Contains("SolarMemoryCombatRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Solar Memory combat tuning."
    Assert-True $runtimeHooks.Contains("SolarMemoryRewardRuntime.Initialize()") "RuntimeHooks must register Solar Memory battle reward adjustment rules."
    Assert-True $runtimeHooks.Contains("BattleRewardAdjustmentRuntime.Initialize(modConfig)") "RuntimeHooks must initialize generic battle reward adjustment hooks."
    Assert-True $battleRewardAdjustmentRuntime.Contains('RegisterAfter(modConfig, "BattleRewardsUI.ModeSetReward", ApplyRewardAdjustments)') "Battle reward adjustments must run after native reward generation."
    Assert-True $battleRewardAdjustmentRuntime.Contains("BattleRewardAdjustmentService.ApplyAll(context.Target as BattleRewardsUI)") "Battle reward runtime must delegate to the shared adjustment service."
    Assert-True $battleRewardAdjustmentService.Contains("ConditionalWeakTable<BattleRewardsUI, AppliedRuleSet>") "Battle reward adjustment service must prevent duplicate rule application per UI."
    Assert-True $battleRewardAdjustmentService.Contains("Rules.RemoveAll") "Battle reward adjustment rule registration must replace duplicate rule ids."
    Assert-True $solarMemoryRewardRuntime.Contains("SolarMemoryModeRuntime.IsSolarMemoryRun()") "Solar Memory reward rule must only apply during Solar Memory runs."
    Assert-True $solarMemoryRewardRuntime.Contains("BattleRewardApi.IsCurrentBattleReward()") "Solar Memory reward rule must target battle rewards."
    Assert-True $solarMemoryRewardRuntime.Contains("BattleRewardApi.AppendRandomRelicReward") "Solar Memory battle rewards must append a random relic reward."
    Assert-True $battleRewardApi.Contains("rewardUi.RandomSetRelic(candidates)") "Random relic rewards must reuse the native BattleRewardsUI relic flow."
    Assert-True $battleRewardApi.Contains('DictionaryUtil.Get(row, "Rarity") != "4"') "Solar Memory extra random relics must not draw special rarity-4 relics by default."
    Assert-True $battleRewardApi.Contains("manager.CardPackCheck(candidates)") "Extra random relic candidates must respect active card-pack filtering."
    Assert-True $sunExpIds.Contains('public const string EmberCloakLiningRelicId = "*ember_cloak_lining";') "Retired Ember Cloak Lining relic id must use the pool-hidden star prefix."
    Assert-True $sunExpIds.Contains('public const string LegacyEmberCloakLiningRelicId = "ember_cloak_lining";') "Retired Ember Cloak Lining legacy id must remain recognized."
    Assert-True $sunExpIds.Contains("public static bool IsHiddenRelicId") "SunExpIds must expose hidden relic filtering."
    Assert-True $battleRewardApi.Contains('!SunExpIds.IsHiddenRelicId(DictionaryUtil.Get(row, "Id"))') "Random relic reward candidates must exclude hidden relics."
    Assert-True $endlessAbyssMilestoneRewardService.Contains("!SunExpIds.IsHiddenRelicId(id)") "Endless Abyss relic options must exclude hidden relics."
    $relicRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "SunExp\Data\Relic\sunexp.csv")
    $emberCloakLiningRelicRow = $relicRows | Where-Object { $_.Id -eq "*ember_cloak_lining" } | Select-Object -First 1
    $ashCharmRelicRow = $relicRows | Where-Object { $_.Id -eq "ash_charm" } | Select-Object -First 1
    Assert-True ($null -ne $emberCloakLiningRelicRow) "Ember Cloak Lining must remain as a hidden star-prefixed relic row."
    Assert-True ($emberCloakLiningRelicRow.Rarity -eq "1") "Hidden relic rows must keep a UI-valid rarity instead of using Rarity 7."
    Assert-True ($ashCharmRelicRow.Rarity -eq "3") "Ash Charm must be promoted to rarity tier 3."
    $displayRarityKinds = @("Card", "Relic", "Buff", "Blessing", "EnchTag")
    foreach ($kind in $displayRarityKinds) {
        $kindRoot = Join-Path $RepoRoot "SunExp\Data\$kind"
        if (-not (Test-Path -LiteralPath $kindRoot)) {
            continue
        }

        foreach ($file in (Get-ChildItem -LiteralPath $kindRoot -Filter *.csv)) {
            foreach ($row in ((Import-Csv -LiteralPath $file.FullName) | Select-Object -Skip 1)) {
                if (($row.PSObject.Properties.Name -contains "Rarity") -and $row.Rarity -eq "7") {
                    Assert-True $false "$kind '$($row.Id)' in $($file.Name) must not use Rarity 7 as a hidden flag; use a leading * id when the row must leave random pools."
                }
            }
        }
    }
    $relicTextRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "SunExp\Text\Relic\sunexp.csv")
    $emberCloakLiningTextRow = $relicTextRows | Where-Object { $_.Id -eq "*ember_cloak_lining" } | Select-Object -First 1
    $sunOrbitMirrorTextRow = $relicTextRows | Where-Object { $_.Id -eq "sun_orbit_mirror" } | Select-Object -First 1
    $miniatureSunwheelTextRow = $relicTextRows | Where-Object { $_.Id -eq "miniature_sunwheel" } | Select-Object -First 1
    $blazingCrownHeartTextRow = $relicTextRows | Where-Object { $_.Id -eq "blazing_crown_heart" } | Select-Object -First 1
    $ashCharmTextRow = $relicTextRows | Where-Object { $_.Id -eq "ash_charm" } | Select-Object -First 1
    Assert-True ($null -ne $emberCloakLiningTextRow) "Hidden Ember Cloak Lining relic text row must keep the same star-prefixed id."
    Assert-True $sunOrbitMirrorTextRow.Description_en.Contains("Every 3 actions, gain 1 stack") "Sun-Orbit Mirror text must describe Gathered Flame gain."
    Assert-True $miniatureSunwheelTextRow.Description_en.Contains("All enemies gain {buff_burn} equal to your {SunExp_sunexp_solar_radiance} stacks.") "Miniature Sunwheel text must describe party-wide Burn."
    Assert-True $blazingCrownHeartTextRow.Description_en.Contains("gain 8 stacks of {SunExp_sunexp_solar_radiance}") "Blazing Crown Heart text must describe 8 Solar Radiance at combat start."
    Assert-True $ashCharmTextRow.Description_en.Contains("At round end") "Ash Charm text must trigger at round end."
    $sunOrbitMirrorBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterSunOrbitMirror[\s\S]*?private\s+static\s+void\s+RegisterSolarPhaseDial")
    $miniatureSunwheelBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterMiniatureSunwheel[\s\S]*?private\s+static\s+void\s+RegisterSunOrbitMirror")
    $blazingCrownHeartBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterBlazingCrownHeart[\s\S]*?private\s+static\s+void\s+RegisterSolarPrism")
    $ashCharmBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterAshCharm[\s\S]*?private\s+static\s+void\s+RegisterBlazingSundial")
    Assert-True ($sunOrbitMirrorBlock.Success -and $sunOrbitMirrorBlock.Value.Contains('self.AddBuff(SunExpIds.GatheredFlame, "1");') -and $sunOrbitMirrorBlock.Value.Contains("ExecutorApi.AddBurnToRandomEnemy(self, 3);")) "Sun-Orbit Mirror must gain Gathered Flame and apply 3 Burn every third action."
    Assert-True ($miniatureSunwheelBlock.Success -and $miniatureSunwheelBlock.Value.Contains("BuffApi.NegativeTotal(self.Self)") -and $miniatureSunwheelBlock.Value.Contains("ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, burn);")) "Miniature Sunwheel must convert negative stacks into Gathered Flame and add Solar Radiance as Burn to all enemies."
    Assert-True ($miniatureSunwheelBlock.Success -and -not $miniatureSunwheelBlock.Value.Contains("ScorchingCanopy")) "Miniature Sunwheel must not require Scorching Canopy."
    Assert-True ([regex]::IsMatch($blazingCrownHeartBlock.Value, 'AddBuff\(SunExpIds\.SolarRadiance, "8"\);[\s\S]*ApplyFieldBuff\(self, "scorching_canopy", 2\);[\s\S]*AddBuff\(SunExpIds\.SolarCrown, "1"\);')) "Blazing Crown Heart must grant Radiance, Canopy, then Crown in order."
    Assert-True (-not $blazingCrownHeartBlock.Value.Contains('TryAddEvent(self, "StartRound"')) "Blazing Crown Heart must not keep the old round-start Burn aura."
    Assert-True ($ashCharmBlock.Success -and $ashCharmBlock.Value.Contains('TryAddEvent(self, "EndRound"') -and $ashCharmBlock.Value.Contains("self.AddBuff(SunExpIds.Ember, burn.ToString());") -and $ashCharmBlock.Value.Contains("self.ChangeDefence(burn.ToString());")) "Ash Charm must grant Ember and Block equal to self Burn at round end."
    Assert-True $solarMemoryCombatRuntime.Contains('SunExpStatusLifecycleRouter.Register("SolarMemoryCombat"') "Solar Memory combat tuning must subscribe through the shared status lifecycle router."
    Assert-True $solarMemoryCombatRuntime.Contains("AfterEnemyInit = ScaleEnemyHpAfterInit") "Solar Memory combat tuning must scale enemies after native Enemy.Init."
    Assert-True $solarMemoryCombatRuntime.Contains("EnemyHpMultiplier = 3") "Solar Memory enemies must use the configured 3x HP multiplier."
    Assert-True $solarMemoryCombatRuntime.Contains("SolarMemoryModeRuntime.IsSolarMemoryRun()") "Solar Memory enemy HP scaling must be gated to Solar Memory runs."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize World Simulation map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "SublimationManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize Sublimation map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "TeachMapManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize tutorial map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "SlotMachineManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize slot-mode map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", SanitizeMapBeforeSelect)') "Solar Memory isolation must clean old generated nodes before map selection UI is built."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterBefore(modConfig, "MapManager.RpcNextMap", RepairCurrentNodeBeforeNextMap)') "Normal-mode isolation must repair missing client current nodes before RpcNextMap consumes them."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("SanitizeSelectionArrays(maps, mapData, level)") "Solar Memory isolation must repair multiplayer map selection arrays."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RestoreCurrentNodeIfMissingOrExclusive(level, "MapSelectUI.ReadyToSelect", clientOnly: true)') "Normal-mode isolation must restore a missing client current node before map selection UI is consumed."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RestoreCurrentNodeIfMissingOrExclusive(level, "MapManager.MapSelectionSync", clientOnly: true)') "Normal-mode map sync isolation must repair client currentNode from synchronized arrays."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("MapNodeSafetyService.EnsureNodeDice") "Normal-mode isolation must ensure replacement nodes have NodeDice."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("if (SolarMemoryModeRuntime.IsSolarMemoryRun())") "Solar Memory isolation must leave Solar Memory runs untouched."
    Assert-True $mapNodeSafetyService.Contains("public static bool RestoreCurrentNodeIfMissingOrExclusive") "Map node safety service must expose client current-node restoration."
    Assert-True $mapNodeSafetyService.Contains("clientOnly && !IsClientOnlyPlayer()") "Client current-node restoration must be gated so it does not advance host authority."
    Assert-True $mapNodeSafetyService.Contains("TryBuildCurrentNodeFromSyncArrays") "Client current-node restoration must prefer synchronized map arrays."
    Assert-True $mapNodeSafetyService.Contains("GameSaveManager.UpdateNode(node)") "Current-node restoration must update the saved node after assigning MapTree.currentNode."
    Assert-True $mapNodeSafetyService.Contains("NodeDice = tree.treedice ?? Dice.Default") "Restored synchronized nodes must have deterministic NodeDice."
    Assert-True $sunExpIds.Contains("public static bool IsSolarMemoryExclusiveMapId") "SunExpIds must centralize exclusive Solar Memory map identification."
    Assert-True $sunExpIds.Contains("public static bool IsSolarMemoryExclusiveEventId") "SunExpIds must centralize exclusive Solar Memory event identification."
    Assert-True $runtimeHooks.Contains("DuskPartnerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Dusk partner runtime."
    Assert-True $runtimeHooks.Contains("StarClayDollRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Star Clay Doll independently from Dusk."
    Assert-True $runtimeHooks.Contains("StarScoreHudRuntime.Initialize(modConfig)") "RuntimeHooks must initialize the star score HUD independently from card logic."
    Assert-True $runtimeHooks.Contains("LoneerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Loneer's card-action runtime."
    Assert-True $runtimeHooks.Contains("SolarMemoryMapItemAnimationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize solar memory map-item animation fallback hooks."
    Assert-True $runtimeHooks.Contains("MapNodeCardArtRuntime.Initialize(modConfig)") "RuntimeHooks must initialize generic map-node card art hooks after animation fallback hooks."
    Assert-True $starScoreService.Contains("public static event Action<StarScoreDisplaySnapshot>? Changed") "Star score mechanics must publish typed display snapshots for UI runtimes."
    Assert-True $starScoreService.Contains("PublishChanged(self.Self, state, isCadencePreview: true") "Star score HUD must receive a full three-note cadence preview before state collapse."
    Assert-True $starScoreState.Contains("public StarScoreDisplaySnapshot Snapshot") "Star score combat state must expose a display snapshot instead of leaking mutable note lists."
    Assert-True $starScoreNote.Contains("public enum StarScoreNote") "Star score notes must be modeled as typed values."
    Assert-True $starScoreSnapshot.Contains("IReadOnlyList<StarScoreNote> Notes") "Star score display snapshots must expose typed notes."
    Assert-True $starScoreCadenceCatalog.Contains("public static class StarScoreCadenceCatalog") "Star score tooltip cadence copy must live in Mechanics."
    Assert-True $starScoreCadenceCatalog.Contains("CandidatesForPrefix") "Star score tooltip cadence candidates must be calculated from the current prefix."
    Assert-True $starScoreHudRuntime.Contains("StarScoreService.Changed += OnStarScoreChanged") "Star score HUD runtime must subscribe to mechanics snapshots."
    Assert-True $starScoreHudRuntime.Contains('SunExpBattleLifecycleRouter.Register("StarScoreHud"') "Star score HUD runtime must clear its UI through the shared battle lifecycle router."
    Assert-True $starScoreHudRuntime.Contains('FightStarted = OnFightBoundary') "Star score HUD runtime must clear its UI on fight start."
    Assert-True $starScoreHudRuntime.Contains('FightEnding = OnFightBoundary') "Star score HUD runtime must clear its UI on fight end."
    Assert-True $starScoreHudRuntime.Contains('RegisterAfter(modConfig, SunExpHookTargets.FightWinInit, OnFightBoundary);') "Star score HUD runtime must still cover fight-win init boundaries."
    Assert-True $starScoreHudRuntime.Contains('RegisterAfter(modConfig, SunExpHookTargets.FightEscapeInit, OnFightBoundary);') "Star score HUD runtime must still cover fight-escape init boundaries."
    Assert-True $starScoreHudRuntime.Contains("UIManager.Instance?.canvasTf") "Star score HUD must attach to the main canvas, not modal upper UI by default."
    Assert-True $starScoreHudRuntime.Contains("FightPlayer.Instance?.Status?.InstanceId") "Star score HUD must filter snapshots to the local player owner."
    Assert-True $starScoreHudRuntime.Contains('activeView.Close("StarScoreHudRuntime.Close")') "Star score HUD runtime must close roots through the view safety path."
    Assert-True (-not $starScoreHudRuntime.Contains("Object.Destroy(activeView.gameObject)")) "Star score HUD runtime must not directly destroy HUD roots."
    Assert-True (-not $starScoreHudView.Contains("ProgressPartThresholds")) "Star score HUD must keep the full frame visible instead of lighting progress parts."
    Assert-True $starScoreHudView.Contains("SunExpUiSafety.CloseTransient(gameObject") "Star score HUD view must close through shared UI safety."
    Assert-True $starScoreHudView.Contains("SlotTops = { 0f, 146f, 226f }") "Star score HUD lighting masks must merge head and space art into the three overture stages."
    Assert-True $starScoreHudView.Contains("SlotHeights = { 146f, 80f, 100f }") "Star score HUD lighting masks must cover head+slot1, space+slot2, and space+slot3."
    Assert-True $starScoreHudView.Contains("StarScoreHudTooltipView.Create") "Star score HUD must create a hover tooltip view."
    Assert-True (-not $starScoreHudView.Contains("Input.mousePosition")) "Star score HUD must not use legacy input polling for hover."
    Assert-True (-not $starScoreHudView.Contains("RectTransformUtility.RectangleContainsScreenPoint")) "Star score HUD hover detection must use UI pointer events."
    Assert-True $starScoreHudHoverProbe.Contains("IPointerEnterHandler") "Star score HUD hover probe must receive pointer enter events."
    Assert-True $starScoreHudHoverProbe.Contains("IPointerExitHandler") "Star score HUD hover probe must receive pointer exit events."
    Assert-True $starScoreHudView.Contains("image.raycastTarget = true") "Star score HUD must expose a hover hotspot for pointer events."
    Assert-True $starScoreHudView.Contains("image.raycastTarget = false") "Star score HUD images must not intercept pointer input."
    Assert-True $starScoreHudTooltipView.Contains("group.blocksRaycasts = false") "Star score tooltip must not block native battle controls."
    Assert-True $starScoreHudTooltipView.Contains("image.raycastTarget = false") "Star score tooltip rows must not intercept pointer input."
    Assert-True $starScoreHudTooltipView.Contains("SunExpUiPool.AcquireComponent") "Star score tooltip row rebuilds must reuse pooled rows."
    Assert-True $starScoreHudTooltipView.Contains("SunExpUiPool.ReleaseOrDestroyChildren") "Star score tooltip row rebuilds must use pooled teardown."
    Assert-True (-not $starScoreHudTooltipView.Contains("Destroy(child.gameObject)")) "Star score tooltip must not directly destroy rows."
    Assert-True $starScoreHudView.Contains("LayoutScale = 0.61f") "Star score HUD must use a single root scale for fixed placement."
    Assert-True $starScoreHudAssets.Contains('OpeningIconPath = Root + "\u542f.png"') "Star score HUD assets must map the Opening icon resource."
    Assert-True $starScoreHudAssets.Contains("StarScoreNote.Opening => Load(OpeningIconPath)") "Star score HUD assets must map typed notes to icon sprites."
    Assert-True (-not $sunExpProject.Contains("UnityEngine.InputLegacyModule")) "Star score HUD hover detection must not depend on the Unity input legacy module."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);') "Solar memory map items must patch fixed boss animation paths before native MapItem.Init loads Texture2D frames."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);') "Solar memory map item animation fallback must restore enemy animation paths after native MapItem.Init."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("SunExpIds.SolarBossSecondSunLevelId") "Solar memory map item fallback must cover the second-sun boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("SunExpIds.SolarBossSaintWunaLevelId") "Solar memory map item fallback must cover the saint Wuna boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('row["Animation"] = fallbackAnimation') "Solar memory map item fallback must temporarily replace the enemy Animation row."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('restore.Row["Animation"] = restore.Animation') "Solar memory map item fallback must restore the original enemy Animation row."
    Assert-True (-not $solarMemoryMapItemAnimationRuntime.Contains("ApplyFixedBossMapTexture")) "Solar memory animation fallback must not own map-node texture replacement."
    Assert-True $mapNodeCardArtRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", CaptureMapItemBaseline);') "Map-node art runtime must capture icon baseline before native MapItem.Init mutates transform."
    Assert-True $mapNodeCardArtRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", ApplyMapNodeCardArt);') "Map-node art runtime must apply configured art after native MapItem.Init."
    Assert-True $mapNodeCardArtRuntime.Contains("SunExpResourceCache.Load<Texture>(spec.TexturePath, true)") "Map-node art runtime must load textures through the shared mod-aware resource cache."
    Assert-True $mapNodeCardArtRegistry.Contains("VisualRegistry.MapNodeArtSpecs()") "Map-node art registry must be driven by the visual registry."
    Assert-True ($visualRegistry.Contains("SunExpIds.SolarBossSecondSunMapTexturePath") -and $visualRegistryJson.Contains("solar_memory.second_sun.map_card")) "Visual registry must cover the second-sun boss map texture."
    Assert-True ($visualRegistry.Contains("SunExpIds.SolarBossSaintWunaMapTexturePath") -and $visualRegistryJson.Contains("solar_memory.saint_wuna.map_card")) "Visual registry must cover the saint Wuna boss map texture."
    Assert-True ($visualRegistry.Contains("MapNodeCardArtFitMode.ContainTrimmed") -and $visualRegistryJson.Contains('"fitMode": "ContainTrimmed"')) "Fixed boss map-node art must use transparent-edge contain fitting."
    Assert-True $mapItemApi.Contains("TextureTransparencyAnalyzer.AnalyzeAllEdges") "MapItemApi must analyze transparent edges before applying fitted map-node textures."
    Assert-True $mapItemApi.Contains("MapNodeTextureFitService.Fit") "MapItemApi must delegate map-node texture geometry to the fit service."
    Assert-True $mapNodeTextureFitService.Contains("DefaultFightBoundsWidth = 160f") "Map-node texture fit service must preserve native fight-node width."
    Assert-True $mapNodeTextureFitService.Contains("DefaultFightBoundsHeight = 238f") "Map-node texture fit service must preserve native fight-node height."
    Assert-True $duskPartnerRuntime.Contains('"GameEntryUI.CheckCareer"') "Dusk runtime must clean its placeholder blessing after career checks."
    Assert-True $duskPartnerRuntime.Contains('SunExpBattleLifecycleRouter.Register("DuskPartner"') "Dusk runtime must grant its trait at fight start through the shared lifecycle router."
    Assert-True ($duskPartnerRuntime.Contains("BuffApi.TryAddBattleScopedBuffOnce") -and $duskPartnerRuntime.Contains("SunExpIds.DuskAfterheatRecoveryTrait")) "Dusk runtime must grant the afterheat recovery trait buff through battle-scoped duplicate suppression."
    Assert-True (-not $duskPartnerRuntime.Contains("StarClay")) "Dusk runtime must not own Star Clay Doll behavior."
    Assert-True $starClayDollRuntime.Contains('SunExpBattleLifecycleRouter.Register("StarClayDoll"') "Star Clay Doll runtime must grant its own trait at fight start through the shared lifecycle router."
    Assert-True ($starClayDollRuntime.Contains("BuffApi.TryAddBattleScopedBuffOnce") -and $starClayDollRuntime.Contains("SunExpIds.StarClayDollTrait")) "Star Clay Doll runtime must grant its own trait through battle-scoped duplicate suppression."
    Assert-True $starClayDollRuntime.Contains('SunExpStatusLifecycleRouter.Register("StarClayDoll"') "Star Clay Doll runtime must route lethal-hit protection through the status lifecycle router."
    Assert-True $starClayDollRuntime.Contains("AfterHit = ProtectAfterHit") "Star Clay Doll runtime must own lethal-hit protection."
    Assert-True (-not $starScoreRuntime.Contains("LoneerMiracleService")) "Generic star score runtime must not dispatch Loneer role behavior."
    Assert-True (-not $starScoreRuntime.Contains("StarClay")) "Generic star score runtime must not own partner behavior."
    Assert-True $starScoreRuntime.Contains('"CommonCardItem.OnBeginDrag"') "Star Blessing must preview zero cost when a common card begins dragging."
    Assert-True $starScoreRuntime.Contains('"AttackCardItem.OnPointerDown"') "Star Blessing must preview zero cost when an attack card enters target selection."
    Assert-True $starScoreRuntime.Contains('"AttackCardItem.CancelLineMode"') "Star Blessing must roll back when attack-card targeting is cancelled."
    Assert-True $starScoreRuntime.Contains('"CardItem.CancelUseDrag"') "Star Blessing must roll back when a card drag is cancelled."
    Assert-True $starScoreRuntime.Contains('AfterCommonCardUse = OnCardUseAfter') "Star Blessing must finalize common-card cost state after use."
    Assert-True $starScoreRuntime.Contains('AfterAttackCardUse = OnCardUseAfter') "Star Blessing must finalize attack-card cost state after use."
    Assert-True $starScoreRuntime.Contains("RefundBlessing();") "A rejected card use must refund the consumed Star Blessing."
    Assert-True $starBlessingCostOverrideStore.Contains('DictionaryUtil.Set(config.Vars, "OnceExCost", entry.OriginalOnceCost.ToString())') "Cancelling Star Blessing must restore the exact original one-use cost."
    Assert-True $starBlessingCostOverrideStore.Contains('DictionaryUtil.Set(config.Vars, "OnceExCost", "0")') "Successful Star Blessing use must clear one-use cost state."
    Assert-True (-not $loneerRuntime.Contains("LoneerMiracleService.OnCardActionAfter")) "Loneer runtime must not own Star Stone Pouch action dispatch."
    Assert-True $buffScripts.Contains('["star_stone_pouch"] = ApplyStarStonePouch') "BuffScripts must route Star Stone Pouch apply behavior."
    Assert-True $buffScripts.Contains('["star_stone_pouch"] = ClearStarStonePouch') "BuffScripts must route Star Stone Pouch clear behavior."
    Assert-True $buffScripts.Contains('["star_score"] = ApplyStarScore') "BuffScripts must route Star Score apply behavior."
    Assert-True $buffScripts.Contains('["star_score"] = ClearStarScore') "BuffScripts must route Star Score clear behavior."
    Assert-True $starStonePouchService.Contains('ExecutorApi.TryAddTokenedEvent(self, "ActionAfter"') "Star Stone Pouch must own its own after-action draw hook."
    Assert-True $loneerService.Contains("StarStonePouchService.Drawn += OnStarStonePouchDrawn") "Loneer must subscribe to Star Stone Pouch draw results instead of owning the pouch."
    Assert-True (-not $loneerService.Contains("private static void DrawStone")) "Loneer miracle logic must not keep a role-owned Star Stone draw flow."
    Assert-True $loneerState.Contains("Dictionary<string, LoneerCombatState>") "Loneer combat state must be keyed by owner status instead of ScriptExecutor.Vars."
    Assert-True $loneerService.Contains("LoneerCombatStateStore.GetOrCreate(self.Self)") "Loneer skill and action flows must resolve owner-scoped combat state."
    Assert-True $cardSelectionApi.Contains("Action? onCancelled = null") "Card selection API must expose cancellation separately from empty candidate pools."
    Assert-True $cardSelectionApi.Contains("onCancelled?.Invoke();") "Card selection API must notify callers when the selection UI closes without a card."
    Assert-True $loneerService.Contains("ResolveRandomGuidanceFallback") "Loneer must randomize Guidance when a non-empty selection UI is cancelled."
    Assert-True $loneerService.Contains("RandomGuidanceCard") "Loneer random fallback must choose from the current selectable Guidance pool."
    Assert-True ([regex]::IsMatch($loneerService, "RandomGuidanceCard[\s\S]*UnityEngine\.Random\.Range\(0,\s*pool\.Count\)")) "Cancelled Guidance selection must randomize within the current candidate pool."
    Assert-True (-not $loneerService.Contains("FirstGuidanceCardId")) "Cancelled Guidance selection must not use the old deterministic first-card fallback."
    Assert-True $cardGrantRecipes.Contains("SunExpIds.LoneerDerivedMarker") "Copied guidance cards must receive a hidden derived marker."
    Assert-True $cardGrantRecipes.Contains("SunExpIds.LoneerDerivedTag") "Copied guidance cards must receive a localized visible derived tag."
    Assert-True $cardMutationService.Contains("public static bool SetRuntimeMarkers") "CardMutationService must separate hidden runtime markers from visible SpecialTags."
    Assert-True $loneerService.Contains("CardMutationService.HasRuntimeMarker") "Loneer filtering must read hidden runtime markers."
    Assert-True (-not $cardGrantRecipes.Contains('AddSpecialTagsMutation(SunExpIds.LoneerDerivedMarker')) "Internal Loneer marker ids must never be written to SpecialTag."
    Assert-True $loneerService.Contains("LoneerCardGrantService.GrantGuidanceCopyToHand") "Loneer must use the shared card-grant recipe for guidance copies."
    Assert-True $wunaScripts.Contains("WunaCardGrantService.GrantCoronationTokenToHand") "Wuna must use the shared card-grant recipe for coronation tokens."
    Assert-True $cardApi.Contains("public static CardGrantResult GrantCardToHand") "Generated cards must go through the structured CardApi grant pipeline."
    Assert-True $cardApi.Contains('self.AddCardByData(resolved, request?.RuntimeTags ?? "");') "Generated cards must receive their runtime tags during DataConfig creation."
    Assert-True $cardApi.Contains("self.GetCardFromDeck(added);") "Generated cards must deliver the exact tagged DataConfig to the hand queue."
    Assert-True (-not $cardApi.Contains("LoneerDerivedTag")) "CardApi must not contain Loneer-specific business tags."
    Assert-True (-not $cardApi.Contains("WhiteRadianceTag")) "CardApi must not contain Wuna/SunExp-specific business tags."
    Assert-True (-not $wunaScripts.Contains("AddCardByData")) "Wuna must not hand-roll combat card creation."
    Assert-True (-not $wunaScripts.Contains("EnsureHandTags")) "Wuna must not hand-roll temporary tag propagation."
    Assert-True $cardMutationService.Contains("public static void SetTemporaryCost") "CardMutationService must own temporary card-cost mutation."
    Assert-True (-not $cardMutationService.Contains('config.data["Expend')) "Temporary card-cost mutation must not write base data."
    Assert-True (-not $polymorphActivationService.Contains("DictionaryUtil.Set(config.data")) "Polymorph role-card runtime state must be written to Vars, not read-only base data."
    Assert-True $polymorphActivationService.Contains("PolymorphBuffService.GrantForRole(self, role);") "Polymorph role cards must grant the trait buff instead of changing career directly."
    Assert-True (-not $polymorphActivationService.Contains("self.ChangeCareer(role.Id);")) "Polymorph card use must not be hard-bound to direct career changes."
    Assert-True $polymorphBuffService.Contains("self.ChangeCareer(role.Id);") "Polymorph trait buff apply must own the career change."
    Assert-True $polymorphBuffService.Contains("PolymorphRuntimeService.Enter(self, role, state);") "Polymorph trait buff apply must enter a runtime overlay after ChangeCareer."
    Assert-True $polymorphBuffService.Contains("PolymorphStateStore.ClearOwner(owner") "Polymorph trait buff clear must restore the owner career through state cleanup."
    Assert-True $polymorphBuffService.Contains('ExecutorApi.TryAddTokenedEvent(self, "StartRound"') "Polymorph trait buff must own shared cooldown round ticking."
    Assert-True $polymorphCooldownService.Contains("public const int SkillCooldownRounds = 1;") "Polymorph shared skill cooldown must stay fixed at 1 round."
    Assert-True $polymorphCooldownService.Contains("RoleSkillApi.SetCurrentCareerSkillTimes(cooldown);") "Polymorph shared cooldown must apply one value to every current role skill."
    Assert-True $wunaScripts.Contains("PolymorphCooldownService.MarkSkillUsed(self, ""Wuna.WhiteSunPrayer"")") "Wuna polymorph skill use must commit the shared cooldown."
    Assert-True $wunaScripts.Contains("PolymorphCooldownService.MarkSkillUsed(self, ""Wuna.GraveSong"")") "Wuna second polymorph skill must share the same cooldown record."
    Assert-True $loneerService.Contains("PolymorphCooldownService.MarkSkillUsed(self, ""Loneer.MorningStarPrayer"")") "Loneer polymorph skill use must commit the shared cooldown."
    Assert-True $buffScripts.Contains("[SunExpIds.PolymorphTraitBuffShortId] = ApplyPolymorphTrait") "BuffScripts must route polymorph trait apply behavior."
    Assert-True $buffScripts.Contains("[SunExpIds.PolymorphTraitBuffShortId] = ClearPolymorphTrait") "BuffScripts must route polymorph trait clear behavior."
    Assert-True (-not $polymorphRuntime.Contains('HideTraitBuffFromContext')) "Polymorph trait buff must remain visible in battle."
    Assert-True $polymorphRuntime.Contains('RegisterBefore(modConfig, SunExpHookTargets.SkillItemTrueUse, CaptureSkillUseBefore);') "Polymorph runtime must capture official skill use before TrueUse."
    Assert-True $polymorphRuntime.Contains('RegisterAfter(modConfig, SunExpHookTargets.SkillItemTrueUse, MarkSkillUseAfter);') "Polymorph runtime must commit shared cooldown after official skill use."
    Assert-True $buffData.Contains('"polymorph_trait"') "Polymorph trait buff data row is missing."
    Assert-True ([regex]::IsMatch($buffData, '"polymorph_trait"[\s\S]*?"Icon/Buff/')) "Polymorph trait buff must reuse the Heroic Blessing icon path family."
    Assert-True $polymorphActivationService.Contains("PolymorphRuntimeService.ClearAll(source);") "Polymorph cleanup must clear runtime overlays before restoring career state."
    Assert-True $polymorphRuntimeService.Contains("TryRunCurrentCareerScript") "Polymorph runtime must run the current target-role career script."
    Assert-True $polymorphRuntimeService.Contains("RoleSkillApi.RefreshFightSkills") "Polymorph runtime must rebuild combat skill buttons after changing career."
    Assert-True $polymorphRuntimeService.Contains("executor?.Clear();") "Polymorph runtime cleanup must clear the attached career executor."
    Assert-True $roleSkillApi.Contains("fightUi.InitSkill();") "RoleSkillApi must reuse the native FightUI skill creation path."
    Assert-True $roleSkillApi.Contains("EnsureCurrentCareerSkillTimes") "RoleSkillApi must ensure target skill cooldown keys exist before the rebuilt buttons are used."
    Assert-True $roleSkillApi.Contains('value.Replace("*", "")') "RoleSkillApi must normalize starred official skill ids before cooldown sync."
    Assert-True $roleSkillApi.Contains("SetCurrentCareerSkillTimes") "RoleSkillApi must expose unified current-role skill cooldown writes for polymorph."
    Assert-True $polymorphStateStore.Contains("public static bool IsLocalRoleSuppressed") "Polymorph state must expose role suppression for old passive guards."
    Assert-True $loneerService.Contains("PolymorphStateStore.IsLocalRoleSuppressed") "Loneer passive and skill entries must respect active polymorph suppression."
    Assert-True $wunaScripts.Contains("IsWunaRuntimeActive") "Wuna passive and skill entries must respect active polymorph suppression."
    Assert-True $cardScripts.Contains("[SunExpIds.ProjectionCardShortId] = UseProjection") "CardScripts must route the projection selection card."
    Assert-True $cardScripts.Contains("[SunExpIds.ProjectionRoleTemplateShortId] = UseProjectionRoleCard") "CardScripts must route generated projection role cards."
    Assert-True $runtimeHooks.Contains("ProjectionRuntime.Initialize(modConfig)") "RuntimeHooks must initialize projection combat hooks."
    Assert-True $runtimeHooks.Contains("CompanionIntentRegistry.Load(modConfig)") "RuntimeHooks must load companion intent registry before projection combat hooks."
    Assert-True $runtimeHooks.Contains("CompanionThreatRuntime.Initialize(modConfig)") "RuntimeHooks must initialize companion threat targeting."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.ProjectionScripts") "Entry must register ProjectionScripts for CSV action calls."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.HeartChangeScripts") "Entry must register HeartChangeScripts for temporary controlled intent action calls."
    Assert-True $projectionActivationService.Contains("CardGrantRequest") "Projection generated cards must use the shared card grant API."
    Assert-True $projectionActivationService.Contains("DictionaryUtil.Set(config.Vars") "Projection generated cards must write runtime overrides to Vars."
    Assert-True (-not $projectionActivationService.Contains("DictionaryUtil.Set(config.data")) "Projection generated cards must not mutate base config data."
    Assert-True $projectionSummonService.Contains("RealPlayerCount() + ProjectionStateStore.ActiveCount()") "Projection summon must respect the four-unit friendly cap."
    Assert-True $projectionSummonService.Contains('SunExpResourceCache.Load<GameObject>("Model/player", true, "projection")') "Projection summon must load the player model through the shared resource cache."
    Assert-True $projectionSummonService.Contains("SunExpIds.ProjectionActionStaffTapCardId") "Projection summon must attach the shared staff-tap action."
    Assert-True $projectionSummonService.Contains("SunExpIds.ProjectionActionShieldBlessingCardId") "Projection summon must attach the shared shield action."
    Assert-True $projectionSummonService.Contains("CompanionSlotService.FindOpenPlayerSlot") "Projection summon must occupy an open player-side slot."
    Assert-True $projectionSummonService.Contains("CompanionStatsService.ProjectionStats") "Projection summon must derive independent companion stats."
    Assert-True $projectionOtherObj.Contains("public sealed class ProjectionOtherObj : OtherObj") "Projection actors must stay friendly OtherObj objects, not real partners."
    Assert-True $projectionOtherObj.Contains("EnsureActionIcons") "Projection actors must create action icons because native OtherObj does not."
    Assert-True $projectionOtherObj.Contains("CompanionBattleStateStore.Create") "Projection actors must create companion runtime state."
    Assert-True $projectionOtherObj.Contains("CompanionIntentSelector.Select") "Projection actors must select intents through the companion selector."
    Assert-True $projectionOtherObj.Contains("CompanionThreatService.SetPreview") "Projection actors must publish selected-intent preview threat."
    Assert-True $projectionOtherObj.Contains('RefreshProjectionIntent("InitProjection")') "Projection actors must reveal intent immediately after summon."
    Assert-True $projectionOtherObj.Contains("FightAction.ActionExecute()") "Projection turns must execute queued actions without native head/Msg announcement UI."
    Assert-True (-not $projectionOtherObj.Contains("return base.DoAction();")) "Projection turns must not use native OtherObj.DoAction because the player model lacks head/Msg."
    Assert-True $projectionStateStore.Contains("ProjectionStatusIdPrefix") "Projection status ids must use the centralized friendly projection prefix."
    Assert-True $projectionStateStore.Contains("removeStatusRecords: false") "Projection retirement must leave status records long enough for native hit queues to settle."
    Assert-True $projectionRuntime.Contains('SunExpStatusLifecycleRouter.Register("Projection"') "Projection runtime must retire dead projections through the shared status lifecycle router."
    Assert-True $projectionRuntime.Contains("AfterHit = RetireProjectionAfterDamage") "Projection runtime must retire dead projections after full damage resolves."
    Assert-True $projectionRuntime.Contains("AfterCurHpChanged = RetireProjectionAfterHpChange") "Projection runtime must retire dead projections after direct HP changes."
    Assert-True $projectionRuntime.Contains("AfterMaxHpChanged = RetireProjectionAfterHpChange") "Projection runtime must retire projections whose max HP is reduced to zero."
    Assert-True (-not $projectionRuntime.Contains("SetDamageFilter")) "Projection runtime must not use temporary damage filters after protection redirects were removed."
    Assert-True (-not $projectionRuntime.Contains("RedirectThreatBeforeHit")) "Projection runtime must not redirect enemy attacks away from players."
    Assert-True (-not $projectionRuntime.Contains("ProjectionThreatService")) "Projection runtime must not depend on retired threat redirection."
    Assert-True $projectionStateStore.Contains("RetireIfDead") "Projection state store must expose a shared death retirement guard."
    Assert-True $projectionStateStore.Contains("SunExpFrameDispatcher.RunOnceNextFrame") "Projection retirement must delay status-record removal until native queues settle."
    Assert-True $projectionStateStore.Contains("CompanionBattleStateStore.Remove") "Projection retirement must clear companion runtime state."
    Assert-True (-not $projectionStateStore.Contains("ThreatBoost")) "Projection state must not keep retired threat-weight state."
    Assert-True (-not $projectionStrategyService.Contains("MarkShielded")) "Projection shield behavior must not modify retired threat weights."
    Assert-True $projectionStrategyService.Contains("CompanionIntentExecutor.UseAction") "Projection strategy must delegate shared action behavior to companion intents."
    Assert-True $projectionScripts.Contains("ProjectionStrategyService.UseAction") "ProjectionScripts must keep CSV actions routed through Mechanics."
    Assert-True $companionBattleModels.Contains("CompanionIntentTendency") "Companion models must define attack/defense tendencies."
    Assert-True $companionBattleModels.Contains("CompanionIntentType") "Companion models must define companion intent types."
    Assert-True $companionIntentSelector.Contains("Take(3)") "Companion intent selection must sample from top three priority candidates."
    Assert-True $companionIntentSelector.Contains("PickWeighted") "Companion intent selection must use weighted random selection."
    Assert-True $companionIntentSelector.Contains("CompanionThreatService.ThreatPercent") "Companion intent priority must react to current companion threat."
    Assert-True $companionBattleStateStore.Contains("CompanionThreatService.Register") "Companion battle state creation must register threat state."
    Assert-True $companionBattleStateStore.Contains("CompanionThreatService.Remove") "Companion battle state removal must clear threat state."
    Assert-True $companionIntentRegistry.Contains("companion.intent.registry.json") "Companion intent pools must be data-driven through the registry."
    Assert-True $companionIntentRegistryJson.Contains('"staff_tap"') "Companion intent registry must define the common staff-tap intent."
    Assert-True $companionIntentRegistryJson.Contains('"shield_blessing"') "Companion intent registry must define the common magic-shield intent."
    Assert-True $companionIntentRegistryJson.Contains('"threat"') "Companion intent registry must declare intent threat."
    Assert-True $companionSlotService.Contains("MaxFriendlySlots = 4") "Companion slots must use the four friendly player-side slots."
    Assert-True $companionThreatService.Contains("TryRedirectEnemySingleTarget") "Companion threat must expose weighted enemy single-target redirection."
    Assert-True $companionThreatService.Contains("AddActiveCompanionsToAllTargets") "Companion threat must expose all-target companion expansion."
    Assert-True (-not $companionThreatService.Contains("roleQueue.Add")) "Companion threat must not add projections to the native player role queue."
    Assert-True $companionThreatRuntime.Contains('RegisterAfter(modConfig, "ScriptExecutor.SetStatus", ExtendEnemyTargetsAfterSetStatus);') "Companion threat runtime must hook enemy SetStatus after native target construction."
    Assert-True $companionThreatRuntime.Contains("executor.Self?.fatherObject is not Enemy") "Companion threat runtime must only extend enemy target selection."
    Assert-True $companionStatsService.Contains('"Strength"') "Companion stats must derive magic from the Strength origin key."
    Assert-True $companionStatsService.Contains('"Lucky"') "Companion stats must derive spirit from the Lucky origin key."
    Assert-True $companionStatsService.Contains('"Wisdom"') "Companion stats must derive luck from the Wisdom origin key."
    Assert-True $companionStatsService.Contains('"Perceive"') "Companion stats must derive perception from the Perceive origin key."
    Assert-True $sunExpIds.Contains("HeartChangeActionStrikeCardId") "Heart Change must centralize its temporary EnemyCard id."
    Assert-True $heartChangeControlService.Contains('QueueProxyAction(state, "Apply")') "Heart Change must queue a proxy action as soon as control is applied."
    Assert-True $heartChangeControlService.Contains("manager.ActionQueue.Add(proxy)") "Heart Change must place the proxy actor in the action queue."
    Assert-True $heartChangeControlService.Contains("CompleteProxyAction") "Heart Change must expose a proxy completion path that ends control immediately after the proxy action."
    Assert-True $heartChangeControlService.Contains("consumeNativeAction") "Heart Change cleanup must distinguish proxy completion from plain control cancellation."
    Assert-True $heartChangeControlService.Contains("ApplyFriendlyFacing(state)") "Heart Change must mirror the controlled enemy while it occupies a friendly slot."
    Assert-True $heartChangeControlService.Contains("scale.x = -originalX") "Heart Change friendly-slot mirroring must reverse the original X-facing sign."
    Assert-True $heartChangeControlService.Contains("RestoreNativeQueueNow(state, source, consumeNativeAction)") "Heart Change cleanup must restore the native enemy queue immediately after proxy cleanup."
    Assert-True $heartChangeControlService.Contains("RestoreNativeVisibleState(state, source)") "Heart Change queue restoration must also repair accidental NoAction visible state."
    Assert-True $heartChangeControlService.Contains("state.Status.state == IStatusManager.State.NoAction") "Heart Change visible-state repair must detect stale native NoAction placeholders."
    Assert-True $heartChangeControlService.Contains("state.Status.ChangeState(IStatusManager.State.Default)") "Heart Change visible-state repair must return living enemies to Default before requeue."
    Assert-True (-not $heartChangeControlService.Contains("QueueNativeRestore")) "Heart Change must not defer native enemy queue restoration through a temporary queue."
    Assert-True (-not $heartChangeControlService.Contains("PendingNativeRestores")) "Heart Change must not depend on a delayed pending native restore pool."
    Assert-True (-not $heartChangeControlService.Contains("RestorePendingNativeActions")) "Heart Change must not expose delayed native queue restoration hooks."
    Assert-True (-not $heartChangeControlRuntime.Contains("FightManager.DOAllAction")) "Heart Change must not rely on DOAllAction coroutine hooks for queue restoration."
    Assert-True (-not $heartChangeControlService.Contains("MarkNativeActionConsumed")) "Heart Change must not consume proxy completion by leaving the native enemy in NoAction."
    Assert-True $heartChangeControlService.Contains("ControlledOpponentStatuses") "Heart Change temporary intents must be able to select uncontrolled enemy opponents."
    Assert-True $heartChangeControlService.Contains("ResolveProxyBeforeNativeFallback") "Heart Change must resolve the proxy action if a controlled native enemy action leaks through."
    Assert-True $heartChangeControlService.Contains("HeartChange.ProxyNativeFallbackResolved") "Heart Change must log native-fallback proxy resolution for battle log diagnosis."
    Assert-True (-not $heartChangeControlService.Contains("state.Enemy.FightAction =")) "Heart Change must not swap the native enemy FightAction after native ActionCards have already been shown."
    Assert-True (-not $heartChangeControlService.Contains("PrepareProjectedAction")) "Heart Change must not prepare native enemy actions through the retired projection path."
    Assert-True (-not $heartChangeControlService.Contains("OriginalFightAction")) "Heart Change must not depend on temporarily storing and restoring the enemy's native FightAction."
    Assert-True (-not $heartChangeControlService.Contains("IsLastAction")) "Heart Change must not wait for native multi-action lists before ending control."
    Assert-True (-not $heartChangeControlService.Contains("projected controlled intent")) "Heart Change must not execute the old native projected-intent path."
    Assert-True $heartChangeActionProxyObj.Contains("ResolveIntentCount(source)") "Heart Change proxy must preserve the controlled enemy's displayed intent count."
    Assert-True $heartChangeActionProxyObj.Contains("MaxActionCount = proxyIntentCount") "Heart Change proxy must advertise the preserved temporary intent count."
    Assert-True $heartChangeActionProxyObj.Contains("ActionCount = proxyIntentCount") "Heart Change proxy must execute the preserved temporary intent count."
    Assert-True $heartChangeActionProxyObj.Contains("FightAction.AddCard(CreateProxyActionCard") "Heart Change proxy must build one temporary EnemyCard per preserved intent."
    Assert-True $heartChangeActionProxyObj.Contains("SunExpIds.HeartChangeActionStrikeCardId") "Heart Change proxy must build its preview from the dedicated temporary EnemyCard."
    Assert-True $heartChangeActionProxyObj.Contains("RefreshIntent(""Configure"")") "Heart Change proxy must reveal its temporary intent immediately when queued."
    Assert-True $heartChangeActionProxyObj.Contains("card.UseCard(targetStatus)") "Heart Change proxy must execute each temporary intent against a selected enemy target."
    Assert-True $heartChangeActionProxyObj.Contains("CallActionAnimation(card)") "Heart Change proxy must explicitly play action animation after direct ObjectCard execution."
    Assert-True (-not $heartChangeActionProxyObj.Contains("ActionExecute()")) "Heart Change proxy must not depend on ObjectAction.ActionExecute's native actor assumptions."
    Assert-True $heartChangeActionProxyObj.Contains('CompleteProxyAction(Status, "ProxyAction.Complete")') "Heart Change proxy must end control immediately after executing its action."
    Assert-True $heartChangeIntentService.Contains("ControlledOpponentStatuses") "Heart Change temporary intent must choose from uncontrolled enemy opponents."
    Assert-True $heartChangeIntentService.Contains("OrderBy(target => target.CurHp)") "Heart Change temporary intent must use deterministic lowest-HP targeting."
    Assert-True $heartChangeIntentService.Contains("ExecutorApi.DealDamageToTarget") "Heart Change temporary intent must execute through the shared damage API."
    Assert-True $heartChangeIntentService.Contains("proxy strike: status=") "Heart Change temporary intent must log actual target and damage for battle-log diagnosis."
    Assert-True $heartChangeScripts.Contains("HeartChangeIntentService.InitAction") "HeartChangeScripts must route temporary EnemyCard init through Mechanics."
    Assert-True $heartChangeScripts.Contains("HeartChangeIntentService.Target") "HeartChangeScripts must route temporary EnemyCard targeting through Mechanics."
    Assert-True $heartChangeScripts.Contains("HeartChangeIntentService.UseAction") "HeartChangeScripts must route temporary EnemyCard use through Mechanics."
    Assert-True (-not $cardApi.Contains("previousCount")) "Generated-card success must not depend on draw-pile net count."
    Assert-True (-not $cardApi.Contains("could not verify added card")) "The inverted draw-pile count verifier must remain removed."
    Assert-True $loneerService.Contains("SetMorningPrayerCooldown(self, state, PrayerCooldownRounds);") "Morning Star Prayer must commit its cooldown after a successful copy."
    Assert-True $loneerService.Contains("self?.UpdateSkillTime();") "Morning Star Prayer cooldown changes must refresh the skill UI."
    $starStoneActionFlow = [regex]::Match($starStonePouchService, "private\s+static\s+void\s+DrawForAction[\s\S]*?private\s+static\s+void\s+PublishDrawn")
    Assert-True $starStoneActionFlow.Success "Could not locate Star Stone Pouch action flow for source assertion."
    Assert-True (-not $starStoneActionFlow.Value.Contains("IsExcludedActionCard")) "Every action taken with Star Stone Pouch should be eligible to draw a Star Stone."
    $naturalMorningStar = [regex]::Match($loneerService, "private\s+static\s+void\s+TriggerNaturalMorningStar[\s\S]*?private\s+static\s+void\s+TriggerBorrowedMiracle")
    Assert-True $naturalMorningStar.Success "Could not locate Natural Morning Star for source assertion."
    Assert-True (-not $naturalMorningStar.Value.Contains("AddStarlight")) "Natural Morning Star must not grant Starlight directly."
    Assert-True $naturalMorningStar.Value.Contains("StarStonePouchService.ResetPouch(self);") "Natural Morning Star must reset the shared Star Stone Pouch."
    Assert-True $naturalMorningStar.Value.Contains("RequestGuidanceSelection") "Natural Morning Star must reselect Guidance after copying it."
    $stoneDraw = [regex]::Match($starStonePouchService, "private\s+static\s+void\s+DrawForAction[\s\S]*?private\s+static\s+void\s+PublishDrawn")
    Assert-True $stoneDraw.Success "Could not locate Star Stone Pouch draw flow for source assertion."
    Assert-True $stoneDraw.Value.Contains("var blackStonesRemaining = state.BlackStoneCount();") "A white stone must count the black stones currently remaining in the pouch."
    Assert-True $stoneDraw.Value.Contains("var starlightGain = stone == WhiteStone ? blackStonesRemaining : 1;") "Star Stone Pouch must derive black and white stone Starlight gains inside the buff service."
    Assert-True $stoneDraw.Value.Contains("StarScoreService.AddStarlight(self, starlightGain);") "Star Stone Pouch must grant Starlight from the draw result."
    Assert-True $stoneDraw.Value.Contains("PublishDrawn(self, new StarStonePouchDrawResult") "Star Stone Pouch must publish draw results for role-specific reactions."
    $borrowedMiracle = [regex]::Match($loneerService, "private\s+static\s+void\s+TriggerBorrowedMiracle[\s\S]*?private\s+static\s+void\s+ReduceClock")
    Assert-True $borrowedMiracle.Success "Could not locate Borrowed Miracle for source assertion."
    Assert-True $borrowedMiracle.Value.Contains("ResetPouchAndClock(self, state, grantStarlight: true);") "Restoring the Miracle Clock must grant Starlight equal to its cap."
    Assert-True $borrowedMiracle.Value.Contains("RequestGuidanceSelection") "Borrowed Miracle must reselect Guidance after copying it."
    $resetPouchAndClock = [regex]::Match($loneerService, "private\s+static\s+void\s+ResetPouchAndClock[\s\S]*?private\s+static\s+void\s+EnsureInitialized")
    Assert-True $resetPouchAndClock.Success "Could not locate ResetPouchAndClock for source assertion."
    Assert-True $resetPouchAndClock.Value.Contains("StarStonePouchService.ResetPouch(self);") "Borrowed Miracle must reset the shared Star Stone Pouch through ResetPouchAndClock."
    Assert-True $loneerCareerText.Contains("When the Star Stone Pouch draws a white stone") "Loneer career text must describe only Loneer's reaction to Star Stone Pouch draws."
    Assert-True $buffText.Contains("When the Miracle Clock is restored to its cap, gain {SunExp_sunexp_starlight} equal to that cap.") "Miracle Clock text must describe its Starlight restoration reward."
    Assert-True $buffText.Contains("After each action, draw one Star Stone.") "Star Stone Pouch text must own the every-action draw rule."
    Assert-True $buffText.Contains("If it is black, gain 1 {SunExp_sunexp_starlight}") "Star Stone Pouch text must describe black-stone Starlight gain."
    Assert-True $buffText.Contains("equal to the current number of black stones.") "Star Stone Pouch text must describe white-stone Starlight gain."
    Assert-True ([regex]::IsMatch($buffData, '(?m)^"star_stone_pouch".*"TRUE"\r?$')) "Star Stone Pouch buff data must allow a zero-layer pouch while the white stone remains."
    Assert-True $buffData.Contains('BuffScripts.Apply(self, ""star_stone_pouch"")') "Star Stone Pouch buff data must call its apply script."
    Assert-True $buffData.Contains('BuffScripts.Clear(self, ""star_stone_pouch"")') "Star Stone Pouch buff data must call its clear script."
    $buffRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "SunExp\Data\Buff\sunexp.csv")
    $starStonePouchRow = $buffRows | Where-Object { $_.Id -eq "star_stone_pouch" } | Select-Object -First 1
    $miracleClockRow = $buffRows | Where-Object { $_.Id -eq "miracle_clock" } | Select-Object -First 1
    $solarRadianceRow = $buffRows | Where-Object { $_.Id -eq "solar_radiance" } | Select-Object -First 1
    $gatheredFlameRow = $buffRows | Where-Object { $_.Id -eq "gathered_flame" } | Select-Object -First 1
    $traitType = [string]([char]0x7279) + [string]([char]0x6027)
    $positiveType = [string]([char]0x6b63) + [string]([char]0x9762)
    Assert-True ($starStonePouchRow.Type -eq $traitType) "Star Stone Pouch must be a trait buff, not a positive ability buff."
    Assert-True ($miracleClockRow.Type -eq $traitType) "Miracle Clock must be a trait buff, not a positive ability buff."
    Assert-True ($solarRadianceRow.Type -eq $positiveType) "Solar Radiance must be a positive buff."
    Assert-True ($gatheredFlameRow.Type -eq $positiveType) "Gathered Flame must be a positive buff."
    Assert-True $buffText.Contains("gain 1/1/1 stacks of {SunExp_sunexp_star_blessing}") "Starlight text must grant one Star Blessing at each threshold."
    $positiveExcludeIdsBlock = [regex]::Match($buffApi, "PositiveExcludeIds[\s\S]*?\};")
    Assert-True $positiveExcludeIdsBlock.Success "Could not locate BuffApi.PositiveExcludeIds for source assertion."
    Assert-True (-not $positiveExcludeIdsBlock.Value.Contains("SunExpIds.SolarRadiance")) "Solar Radiance must enter global positive buff logic."
    Assert-True (-not $positiveExcludeIdsBlock.Value.Contains("SunExpIds.GatheredFlame")) "Gathered Flame must enter global positive buff logic."
    Assert-True $starScoreService.Contains("gain += 1;") "Starlight threshold rewards must grant one Star Blessing per threshold."
    Assert-True (-not $starScoreService.Contains("gain += 2;")) "Starlight reaching 30 must no longer grant two Star Blessing stacks."
    $positiveBuffsBlock = [regex]::Match($buffApi, "private\s+static\s+IEnumerable<IBuffItem>\s+PositiveBuffs[\s\S]*?private\s+static\s+bool\s+IsNegativeType")
    Assert-True $positiveBuffsBlock.Success "Could not locate BuffApi.PositiveBuffs for source assertion."
    Assert-True $positiveBuffsBlock.Value.Contains("if (IsPositiveExcluded(buff.buffConfig.BuffId))") "Positive buff enumeration must skip excluded trait-like technical buffs."
    Assert-True $buffData.Contains('BuffScripts.Apply(self, ""star_score"")') "Star Score buff data must bind its UI lifecycle to the buff apply script."
    Assert-True $buffData.Contains('BuffScripts.Clear(self, ""star_score"")') "Star Score buff data must bind its UI lifecycle to the buff clear script."
    Assert-True ([regex]::IsMatch($enchTagData, '(?m)^"morning_star_seal",.*,"3",')) "Morning Star Seal must be rarity tier 3."
    Assert-True $keywordText.Contains('"Natural Morning Star"') "Natural Morning Star keyword localization is missing."
    Assert-True $keywordText.Contains('"Borrowed Miracle"') "Borrowed Miracle keyword localization is missing."
    Assert-True $cardScripts.Contains('id = NormalizeId(id);') "Card script entry points must normalize generated-card ids."
    Assert-True (-not [regex]::IsMatch($cardScripts, 'case\s+"\*')) "Card script switches must use normalized, unstarred ids."
    Assert-True $cardScripts.Contains("IsStarScoreEntry(id)") "CardScripts must route Stellar Overture cards through the shared StarScore entry predicate."
    foreach ($stellarId in @("stellar_overture_start", "stellar_overture_sustain", "stellar_overture_turn", "stellar_overture_close")) {
        Assert-True $starScoreNote.Contains('case "' + $stellarId + '":') ("StarScoreNoteCodes must dispatch " + $stellarId + ".")
    }
    $stellarRows = Import-Csv -LiteralPath $cardDataPath | Where-Object { $_.Id -like "*stellar_overture_*" }
    Assert-True ($stellarRows.Count -eq 4) "SunExp must define all four Stellar Overture cards."
    foreach ($row in $stellarRows) {
        Assert-True ($row.InitScript -match 'CardScripts\.Init\(self, "([^"]+)"\)') ("Missing CardScripts.Init dispatch for " + $row.Id)
        $initId = $Matches[1].Replace("*", "").Trim()
        Assert-True $starScoreNote.Contains('case "' + $initId + '":') ("Normalized Init id is not dispatched: " + $row.Id)
        Assert-True ($row.UseScript -match 'CardScripts\.Use\(self, "([^"]+)"\)') ("Missing CardScripts.Use dispatch for " + $row.Id)
        $useId = $Matches[1].Replace("*", "").Trim()
        Assert-True $starScoreNote.Contains('case "' + $useId + '":') ("Normalized Use id is not dispatched: " + $row.Id)
    }
    $cardRows = Import-Csv -LiteralPath $cardDataPath
    $starMapRow = $cardRows | Where-Object { $_.Id -eq "star_map" } | Select-Object -First 1
    $starStageRow = $cardRows | Where-Object { $_.Id -eq "morning_star_stage" } | Select-Object -First 1
    Assert-True ($starMapRow.Rarity -eq "3") "Star Map must be promoted to rarity tier 3."
    Assert-True ([regex]::IsMatch($starMapRow.Tag, '(^|,)Burnout(,|$)')) "Star Map must have Burnout."
    Assert-True ([regex]::IsMatch($starStageRow.Tag, '(^|,)Burnout(,|$)')) "Morning Star: Star Stage must have Burnout."
    Assert-True $morningStarCardScripts.Contains("CardApi.SelectAndBurnHandCards(self, 3);") "Star Map must let the player choose three cards to burn."
    Assert-True $cardApi.Contains("public static bool SelectAndBurnHandCards") "CardApi must expose selected hand-card burning."
    Assert-True $cardSelectionApi.Contains("public static bool SelectCardsFromCards") "CardSelectionApi must support multi-card selection for Star Map."
    $cardTextRows = Import-Csv -LiteralPath $cardTextPath
    $starMapTextRow = $cardTextRows | Where-Object { $_.Id -eq "star_map" } | Select-Object -First 1
    $stellarCloseTextRow = $cardTextRows | Where-Object { $_.Id -eq "*stellar_overture_close" } | Select-Object -First 1
    $stellarSustainTextRow = $cardTextRows | Where-Object { $_.Id -eq "*stellar_overture_sustain" } | Select-Object -First 1
    Assert-True $starMapTextRow.Description_en.Contains("choose 3 cards to burn") "Star Map text must describe selected burning."
    Assert-True $stellarCloseTextRow.Description_en.Contains("Deal {0} damage") "Stellar Overture: Close must use the first dynamic damage placeholder."
    Assert-True $stellarSustainTextRow.Description_en.Contains("Gain {0} Block") "Stellar Overture: Sustain must use the first dynamic block placeholder."
    Assert-True (-not $loneerService.Contains("SunExpIds.LoneerGuidanceCardId")) "Loneer guidance must not be stored in per-executor Vars."
    Assert-True $starScoreService.Contains("StarScoreCombatStateStore.GetOrCreate(self.Self)") "Star score notes must be owner-scoped across card executors."
    Assert-True $starScoreState.Contains("while (notes.Count > Math.Max(1, windowSize))") "Star score must maintain a bounded sliding window."
    Assert-True $starScoreState.Contains("RetainLastNoteAsCadenceStart") "Star score must retain the last overture after a completed cadence."
    Assert-True $starScoreService.Contains("state.RetainLastNoteAsCadenceStart();") "Star score cadence resolution must seed the next cadence with the final overture."
    Assert-True $starScoreService.Contains("DrawCardsForFriendlyParty(self, 2);") "Start-Start-Start cadence must make the friendly party draw two cards."
    Assert-True ([regex]::IsMatch($starScoreService, 'case NoteStart \+ NoteSustain \+ NoteTurn:[\s\S]*self\.AddBuff\(SunExpIds\.Resonance, "1"\);[\s\S]*AddBuffToFriendlyParty\(self, SunExpIds\.Resonance, 1\);')) "Start-Sustain-Turn cadence must grant self resonance and friendly-party resonance."
    Assert-True $duskPartnerScripts.Contains("SunExpDuskAfterheatHook") "Dusk trait scripts must remain in the Dusk module."
    Assert-True $starClayDollScripts.Contains("SunExpStarClayDollHook") "Star Clay Doll trait scripts must remain in the Star Clay module."
    Assert-True $starClayDollScripts.Contains('ExecutorApi.TryAddTokenedEvent(self, "ActionAfter"') "Star Clay Doll must grant starlight after an action resolves through the shared tokened event wrapper."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.DuskPartnerScripts") "XLua registration must expose the Dusk script entry point."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.StarClayDollScripts") "XLua registration must expose the Star Clay Doll script entry point."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^dusk_afterheat_recovery,0,,,Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1,[^,]*,,5\r?$")) "Dusk afterheat recovery must remain a legal zero-weight technical Blessing for GameEntryUI.CheckCareer."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^dusk,10,0,0,0,2,,,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk_choice,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk,Mods/SunExp/ModResource/AnimationLib/Dusk,SunExp_sunexp_dusk_afterheat_recovery,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk\r?$")) "Dusk partner must keep a non-empty Bless column because GameEntryUI.CheckCareer creates a DataConfig from it."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^star_clay_doll_placeholder,0,,,[^,]+,[^,]*,,5\r?$")) "Star Clay Doll must use a non-conflicting technical Blessing id."
    Assert-True (-not [regex]::IsMatch($blessingData, "(?m)^star_clay_doll_trait,")) "Star Clay Doll Blessing id must not collide with its Buff id."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^star_clay_doll,10,0,0,0,2,,,Mods/SunExp/ModResource/Images/Partner/SunExp/RenKui_choice,Mods/SunExp/ModResource/Images/Partner/SunExp/RenKui,Mods/SunExp/ModResource/AnimationLib/Dusk,SunExp_sunexp_star_clay_doll_placeholder,Mods/SunExp/ModResource/Images/Partner/SunExp/RenKui\r?$")) "Star Clay Doll partner must reference its own images and non-conflicting placeholder Blessing."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("IsTechnicalBlessing(id)") "Solar memory blessing picker must skip technical partner blessings."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "GameConfigManager.CardPackCheck", FilterSolarMemoryCardPackCheck)') "Solar memory must filter event cards before CardPackCheck builds reward candidates."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState)') "Solar memory must capture event records before base map generation can draw ordinary events."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect)') "Solar memory must normalize SelectNode immediately before map candidate cards are created."
    Assert-True (-not $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapManager.TryChange", RouteSolarFinaleBeforeMapChange)')) "Solar finale must not open EventUI from the generic TryChange hook; that can recurse through event init failure."
    Assert-True (-not $solarMemoryModeRuntime.Contains('ShowEventUIWithTurn<MapSelectUI>("MapSelectUI", SunExpIds.SolarFinaleFullSaintGateEventId)')) "Solar finale must not open the saint gate event from map transition hooks."
    Assert-True (-not $solarMemoryModeRuntime.Contains("EnterSolarFinaleLayer")) "Solar memory must not route into a dedicated finale map layer."
    Assert-True (-not $solarMemoryModeRuntime.Contains("RepairSolarFinaleMapArrays")) "Solar memory must not force finale map candidates into a pre-boss dialogue or saint boss."
    Assert-True $solarMemoryModeRuntime.Contains('"NormalMapManager.MapItemInit", SettleLegacyTerminalLevelBeforeMapItems') "Solar memory must settle legacy level-30 saves before native MapItemInit indexes map lists."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "Fight_Escape.ResetStates", PrepareSolarMemoryFightAbort)') "Solar memory fight escape must prepare map state and UI cleanup before native reset."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterAfter(modConfig, "Fight_Escape.ResetStates", SettleSolarMemoryFightAbort)') "Solar memory fight escape must settle map state after native reset."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterAfter(modConfig, "Fight_Loss.Init", SettleSolarMemoryFightLoss)') "Solar memory fight loss must clear transient state before fake-loss escape transition."
    Assert-True $solarMemoryModeRuntime.Contains('EnsureSolarMemoryCurrentNodeForTransition("Fight_Escape.ResetStates:before")') "Solar memory escape must repair current node before MapManager.TryChange can consume it."
    Assert-True $solarMemoryModeRuntime.Contains('CloseSolarMemoryTransientUi("Fight_Escape.ResetStates:after")') "Solar memory escape must close transient setup UI after native fight reset."
    Assert-True (-not $solarMemoryModeRuntime.Contains("ClearSolarFinalePendingBattle")) "Solar memory must not retain pending finale-battle cleanup after retiring finale events."
    Assert-True $solarMemoryModeRuntime.Contains('SunExpUiSafety.DisableRaycastsAndDestroyByName("SunExpSolarMemoryStarterDeck", source, "[SolarMemoryFightAbort]")') "Solar memory UI cleanup must route starter-deck teardown through SunExpUiSafety."
    Assert-True $sunExpUiSafety.Contains("UiRaycastSafeDestroyRuntime.DisableAndHide") "Solar memory UI cleanup must disable and hide UI before destroying it."
    Assert-True $sunExpUiSafety.Contains("ScrubGraphicRegistryForFrames") "Solar memory UI cleanup must scrub stale graphics after transient UI teardown."
    Assert-True $sunExpUiSafety.Contains("Object.Destroy(root)") "Solar memory UI cleanup must destroy only after disabling raycasts."
    Assert-True $sunExpModalHost.Contains("SunExpUiSafety.CloseTransient") "SunExp modal host must centralize transient UI teardown."
    Assert-True $dirtyState.Contains("public sealed class SunExpDirtyState") "Repeated UI rebuild guards must use a shared dirty-state helper."
    Assert-True $dirtyState.Contains('SunExpPerformanceCounters.Record("DirtyState.Skipped")') "Dirty-state skips must be visible to performance counters."
    Assert-True $sunExpUiLifetimeScope.Contains("button.onClick.RemoveListener(action)") "Pooled UI button listeners must be detachable."
    Assert-True $sunExpUiPool.Contains("public static class SunExpUiPool") "SunExp local UI pooling must be centralized."
    Assert-True $sunExpUiPool.Contains("SunExpPerformanceSettings.UiPoolCapacityPerKey") "SunExp UI pooling must obey performance-tier capacity caps."
    Assert-True $sunExpUiPool.Contains("button.onClick.RemoveAllListeners()") "SunExp UI pooling must scrub stale button listeners before reuse."
    Assert-True $sunExpUiSprites.Contains("private static readonly Dictionary<string, Sprite?> Cache") "SunExp UI sprites must share a cache across modal windows."
    Assert-True $sunExpUiBuilder.Contains("public static Image ApplyPanelImage") "SunExp local UI builder must expose reusable panel image creation."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Solar memory starter deck UI must reuse SunExpUiBuilder panel creation."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Solar memory blessing picker UI must reuse SunExpUiBuilder panel creation."
    Assert-True $solarMemorySetupFlowRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Solar memory setup flow UI must reuse SunExpUiBuilder panel creation."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("SunExpModalHost.Close(ref activePanel") "Solar memory starter deck close must route through SunExpModalHost."
    Assert-True $solarMemorySetupFlowRuntime.Contains("SunExpModalHost.Close(ref activeOriginRoot") "Solar memory origin setup close must route through SunExpModalHost."
    Assert-True $solarMemorySetupFlowRuntime.Contains("SunExpModalHost.Close(ref activeBlessingChrome") "Solar memory blessing setup chrome close must route through SunExpModalHost."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SunExpModalHost.Close(ref activePanel") "Solar memory blessing picker close must route through SunExpModalHost."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Endless Sea intro board must reuse shared panel creation."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("SunExpModalHost.Close(ref activePanel") "Endless Sea intro board close must route through SunExpModalHost."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("ScrollRect") "Endless Sea intro board body must be scrollable."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("supportRichText = true") "Endless Sea intro board must enable controlled rich text."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("EndlessSeaRichTextSanitizer.Sanitize") "Endless Sea intro board must sanitize rich text before display."
    Assert-True (-not $endlessSeaIntroBoardRuntime.Contains("WebView")) "Endless Sea intro board must not embed web content."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("SunExpUiPool.AcquireComponent") "Solar memory starter deck list rows must reuse pooled UI."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("deckListDirty.ShouldRefresh") "Solar memory starter deck selected list must skip unchanged rebuilds."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SunExpUiPool.AcquireComponent") "Solar memory blessing picker list rows must reuse pooled UI."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("candidateListDirty.ShouldRefresh") "Solar memory blessing candidates must skip unchanged rebuilds."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("SunExpUiSprites.Button") "Solar memory starter deck must use cached shared button sprites."
    Assert-True $solarMemorySetupFlowRuntime.Contains("SunExpUiSprites.Button") "Solar memory setup flow must use cached shared button sprites."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SunExpUiSprites.Button") "Solar memory blessing picker must use cached shared button sprites."
    $solarMemorySetupUiSources = $solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime
    Assert-True (-not $solarMemorySetupUiSources.Contains("CreateNineSliceSprite")) "Solar memory setup windows must not duplicate nine-slice sprite construction."
    Assert-True (-not $solarMemorySetupUiSources.Contains("GetButtonSprite")) "Solar memory setup windows must not keep per-window button sprite caches."
    Assert-True (-not $solarMemorySetupUiSources.Contains("Object.Destroy(active")) "Solar memory setup windows must not directly destroy active modal roots."
    Assert-True $solarMemoryModeRuntime.Contains("CompleteSolarMemoryRun") "Solar memory must settle immediately after the third layer boss."
    Assert-True $solarMemoryModeRuntime.Contains("manager.Level = levelForNativeFlow") "Solar memory completion must route through the native settlement level."
    Assert-True (-not $eventScripts.Contains("InitSolarFinale")) "Retired solar finale EventList entries must not leave script entry points behind."
    Assert-True (-not $eventScripts.Contains("FinishSolarFinaleEnding")) "Retired solar finale ending must not be opened through EventScripts."
    Assert-True (-not $sunExpIds.Contains("SolarFinaleFullEndingEventId")) "Retired solar finale ending event id must not remain in SunExpIds."
    Assert-True $solarMemoryModeRuntime.Contains("RepairSolarMemoryMapSelection") "Solar memory must repair synced map arrays for its fixed first node."
    Assert-True $solarMemoryModeEntryRuntime.Contains("Mods/SunExp/ModResource/Images/UI/solar_memory_title_c.png") "Solar memory mode entry must load its cropped normal title sprite."
    Assert-True $solarMemoryModeEntryRuntime.Contains("Mods/SunExp/ModResource/Images/UI/solar_memory_title_c_h.png") "Solar memory mode entry must load its cropped highlighted title sprite."
    Assert-True $solarMemoryModeEntryRuntime.Contains('VisualRegistry.ModeEntry("solar_memory")') "Solar memory mode entry title art must resolve from the visual registry."
    Assert-True $solarMemoryModeEntryRuntime.Contains('var normalTitle = entry.Find("Normal/Title")') "Solar memory mode entry must locate the native normal title image."
    Assert-True $solarMemoryModeEntryRuntime.Contains('var highlightedTitle = entry.Find("HighLighted/Title")') "Solar memory mode entry must locate the native highlighted title image."
    Assert-True $solarMemoryModeEntryRuntime.Contains("SetImageSprite(normalTitle, normalSprite)") "Solar memory mode entry must replace the native normal title image."
    Assert-True $solarMemoryModeEntryRuntime.Contains("SetImageSprite(highlightedTitle, highlightedSprite)") "Solar memory mode entry must replace the native highlighted title image."
    Assert-True $solarMemoryModeEntryRuntime.Contains('title.gameObject.SetActive(false);') "Solar memory mode entry must hide the fallback text title when sprites load."
    Assert-True $solarMemoryModeEntryRuntime.Contains("ConfigureEntryUnlocked(entry.transform)") "Solar memory mode entry must clear lock state inherited from the cloned native mode."
    Assert-True $solarMemoryModeEntryRuntime.Contains('string.Equals(child.name, "Lock"') "Solar memory mode entry must hide cloned Lock objects."
    Assert-True $solarMemoryModeEntryRuntime.Contains("TrimTransparentPadding(sprite)") "Solar memory mode entry must trim transparent padding from configured title art."
    Assert-True $solarMemoryModeEntryRuntime.Contains("CropEntryTitleArt(trimmed)") "Solar memory mode entry must crop full-card art into the native Title slot."
    Assert-True $solarMemoryModeEntryRuntime.Contains("DefaultEntryTitleArtHeightRatio") "Solar memory mode entry must keep the title-slot crop ratio explicit."
    Assert-True $solarMemoryModeEntryRuntime.Contains("ClearEntryStateImages") "Solar memory mode entry must clear native mode art layers before applying custom art."
    Assert-True $solarMemoryModeEntryRuntime.Contains("stateRoot.GetComponentsInChildren<Image>(true)") "Solar memory mode entry must disable cloned Image layers."
    Assert-True $solarMemoryModeEntryRuntime.Contains("stateRoot.GetComponentsInChildren<RawImage>(true)") "Solar memory mode entry must disable cloned RawImage layers."
    Assert-True $solarMemoryModeEntryRuntime.Contains("ConfigureEntryHoverState(entry)") "Solar memory mode entry must isolate inherited hover animation state."
    Assert-True $solarMemoryModeEntryRuntime.Contains("switchButton.isAnimated = false") "Solar memory mode entry must use immediate SwitchButton state changes."
    Assert-True $solarMemoryModeEntryRuntime.Contains("component.StopAllCoroutines()") "Solar memory mode entry must stop inherited ButtonManager hover transitions."
    Assert-True $solarMemoryModeEntryRuntime.Contains("component.enabled = false") "Solar memory mode entry must disable duplicate ButtonManager hover controllers."
    Assert-True $solarMemoryModeEntryRuntime.Contains('entry.Find("Pressed/Title")') "Solar memory mode entry must provide custom art for the pressed state."
    Assert-True $solarMemoryModeEntryRuntime.Contains("switchButton.SetOffImmediate()") "Solar memory mode entry must reset cloned CanvasGroup state deterministically."
    Assert-True $solarMemoryModeEntryRuntime.Contains("RegisterModeChoiceEntry()") "Solar memory mode entry must register itself through the shared mode-choice entry registry."
    Assert-True $solarMemoryModeEntryRuntime.Contains("ModeChoiceLayoutRuntime.Initialize(modConfig)") "Solar memory mode entry must use the shared mode-choice layout runtime."
    Assert-True (-not $solarMemoryModeRuntime.Contains('"ModeChoiceUI.Init", InjectEntry')) "Solar memory mode entry must not directly inject and position itself from ModeChoiceUI.Init."
    Assert-True (-not $solarMemoryModeRuntime.Contains('"ModeChoiceUI.DataUpdate", InjectEntry')) "Solar memory mode entry must not directly inject and position itself from ModeChoiceUI.DataUpdate."
    Assert-True $modeChoiceEntryRegistry.Contains("public static class ModeChoiceEntryRegistry") "Mode choice custom entries must be registered through a shared registry."
    Assert-True $modeChoiceLayoutRuntime.Contains('"ModeChoiceUI.Init", ApplyRegisteredEntries') "Mode choice layout runtime must refresh entries after native Init."
    Assert-True $modeChoiceLayoutRuntime.Contains('"ModeChoiceUI.DataUpdate", ApplyRegisteredEntries') "Mode choice layout runtime must refresh entries after native DataUpdate."
    Assert-True $modeChoiceLayoutRuntime.Contains("AppendRegisteredEntries") "Mode choice layout runtime must append custom entries after native entries."
    Assert-True $modeChoiceLayoutRuntime.Contains("FindNativeEntries") "Mode choice layout runtime must discover actual native entries from ModeList."
    Assert-True $modeChoiceLayoutRuntime.Contains("PlaceAfterNativeEntries") "Mode choice layout runtime must place custom entries after the real last native entry."
    Assert-True $modeChoiceLayoutRuntime.Contains("KnownNativeEntryNames") "Mode choice layout runtime must use an explicit native entry list before heuristic scanning."
    Assert-True $modeChoiceLayoutRuntime.Contains('"StoryMode"') "Mode choice layout runtime must treat the native StoryMode card as a protected fourth entry."
    Assert-True $modeChoiceLayoutRuntime.Contains("rect.GetWorldCorners(corners)") "Mode choice layout runtime must measure actual RectTransform bounds instead of assuming anchored-position spacing."
    Assert-True $modeChoiceLayoutRuntime.Contains("Intersects(blocker.Bounds, placedBounds") "Mode choice layout runtime must reject placements that overlap native entries."
    Assert-True $modeChoiceLayoutRuntime.Contains("EnsureFallbackButton") "Mode choice layout runtime must expose a separate fallback entry when fifth-card placement is unsafe."
    Assert-True $modeChoiceLayoutRuntime.Contains("EnsureLayoutSlot") "Mode choice layout runtime must create transparent LayoutGroup slots for reserved native and custom positions."
    Assert-True $modeChoiceLayoutRuntime.Contains("PlaceRegisteredEntriesInLayoutSlots") "Mode choice layout runtime must append custom entries through LayoutGroup placeholder slots."
    Assert-True $modeChoiceLayoutRuntime.Contains("FindProtectedNativeEntries") "Mode choice layout runtime must include inactive known native slots when reserving native positions."
    Assert-True $modeChoiceLayoutRuntime.Contains("NativeReserveSlotPrefix") "Mode choice layout runtime must reserve inactive native mode slots explicitly."
    Assert-True $modeChoiceLayoutRuntime.Contains("NativeProxySlotPrefix") "Mode choice layout runtime must render inactive native mode slots through visible proxy entries."
    Assert-True $modeChoiceLayoutRuntime.Contains("EnsureNativeProxySlot") "Mode choice layout runtime must clone inactive native entries into visible LayoutGroup proxies."
    Assert-True $modeChoiceLayoutRuntime.Contains("CustomSlotPrefix") "Mode choice layout runtime must create a real fifth LayoutGroup slot for custom mode entries."
    Assert-True $modeChoiceLayoutRuntime.Contains("EnsureIgnoredByLayout(customEntry.Rect, ignored: true)") "Mode choice custom cards must stay active but ignored by the native LayoutGroup."
    Assert-True $modeChoiceLayoutRuntime.Contains("ModeChoiceHorizontalDrag") "Mode choice layout runtime must provide horizontal dragging for overflowed mode entries."
    Assert-True $modeChoiceLayoutRuntime.Contains("ModeChoiceDragRangeService.Calculate") "Mode choice layout runtime must calculate overflow through testable mechanics."
    Assert-True $modeChoiceLayoutRuntime.Contains("DisableLegacyDragSurface") "Mode choice layout runtime must disable stale raycast-blocking drag surfaces."
    Assert-True $modeChoiceLayoutRuntime.Contains("image.raycastTarget = false") "Mode choice layout runtime must clear stale drag-surface raycasts before hiding it."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("ConfigureDragSurface")) "Mode choice layout runtime must not create a full-screen raycast-blocking drag surface."
    Assert-True $modeChoiceLayoutRuntime.Contains("EnsureBackgroundDragSurface") "Mode choice layout runtime must provide a background-only drag raycast surface."
    Assert-True $modeChoiceLayoutRuntime.Contains("surface.SetAsFirstSibling()") "Mode choice background drag surface must stay behind clickable mode entries."
    Assert-True $modeChoiceLayoutRuntime.Contains("image.raycastTarget = dragEnabled") "Mode choice background drag surface must receive events only while dragging is available."
    Assert-True $modeChoiceLayoutRuntime.Contains("modeChoice.gameObject") "Mode choice dragging must be handled by the common UI root."
    Assert-True $modeChoiceLayoutRuntime.Contains("preferred.gameObject.activeSelf") "Mode choice custom entries must prefer an active native visual template."
    Assert-True $modeChoiceLayoutRuntime.Contains("var targetChanged = !configured || modeList != modeListRect") "Mode choice drag configuration must retain a stable baseline across repeated UI refreshes."
    Assert-True $modeChoiceLayoutRuntime.Contains("ModeChoiceSidePadding") "Mode choice layout runtime must reserve left and right breathing room."
    Assert-True $modeChoiceLayoutRuntime.Contains("defaultOffset") "Mode choice layout runtime must start at a padded default offset."
    Assert-True $modeChoiceLayoutRuntime.Contains("DragStartThreshold") "Mode choice layout runtime must distinguish clicks from horizontal drags."
    Assert-True $modeChoiceLayoutRuntime.Contains("strategy=layout-slot-placeholder") "Mode choice layout diagnostics must identify the placeholder LayoutGroup strategy."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("strategy=overlay-layout-group")) "Mode choice layout runtime must not use the failed overlay strategy."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("layout-group=sibling-order")) "Mode choice layout runtime must not rely on sibling order under a native LayoutGroup."
    Assert-True $modeChoiceLayoutRuntime.Contains("CopyRectShape(rightmostNative.Rect, target)") "Mode choice custom entries must copy native RectTransform shape instead of inventing anchors."
    Assert-True $modeChoiceLayoutRuntime.Contains("SetCenterInReference") "Mode choice custom entries must be appended in ModeList local coordinates."
    Assert-True $modeChoiceEntryDefinition.Contains("Action<ModeChoiceUI>? Activate") "Mode choice entries must carry a launch callback for fallback UI."
    Assert-True $solarMemoryModeEntryRuntime.Contains("SunExpIds.SolarMemoryTitle") "Solar memory mode entry must provide its display name to fallback UI."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("Screen.width")) "Mode choice layout runtime must not mix screen pixels with RectTransform local coordinates."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("rect.anchorMin = new Vector2(0.5f, 0.5f)")) "Mode choice layout runtime must not recenter every native entry."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("LayoutScale")) "Mode choice layout runtime must not globally scale mode entries."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolApplier, 'defaultStart\s*=\s*pool\.Layer\s*\*\s*pool\.DefaultSegmentSize')) "Solar memory default nodes must be rewritten for the current layer, not only layer 0."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolApplier, 'selectStart\s*=\s*pool\.Layer\s*\*\s*pool\.SelectSegmentSize')) "Solar memory candidate SelectNode entries must be rewritten for the current layer."
    Assert-True $solarMemoryMapNodePoolApplier.Contains("MapNodeSafetyService.EnsureNodeDice(tree, replacement") "Solar memory node pool application must validate replacement NodeDice before inserting nodes."
    Assert-True $solarMemoryMapNodePoolApplier.Contains("TrimSolarMemoryEventRecord") "Solar memory must roll back ordinary event records consumed during base map generation."
    Assert-True $sunExpIds.Contains("SolarMemoryEventIds") "Solar memory must define all fixed story event ids."
    Assert-True $sunExpIds.Contains("Sub_solar_memory_above_sacred_wheel") "Solar memory id list must include the sixth fixed event."
    Assert-True $sunExpIds.Contains("SolarMemoryLayerNames") "Solar memory must define custom layer names."
    Assert-True $solarMemoryMapVisualRuntime.Contains('"MapSelectUI.DataUpdate", SolarMemoryModeRuntime.ApplySolarMemoryLayerTitle') "Solar memory must override map layer titles in MapSelectUI."
    Assert-True $solarMemoryMapVisualRuntime.Contains('"NormalMapManager.MapItemInit", SolarMemoryModeRuntime.ApplySolarMemoryFixedSlotsAfterMapItems') "Solar memory map visuals must repair fixed slots after native map item creation."
    Assert-True $solarMemoryMapVisualRuntime.Contains('"MapSelectUI.ShowMap", SolarMemoryModeRuntime.ReapplySolarMemoryFixedSlotLocks') "Solar memory map visuals must reapply fixed-slot locks when the map is shown."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("MidLayerSlotIndex = 3") "Solar memory must reserve the fourth map slot for the second story event in each layer."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("CreateSolarMemoryEventNode(layer, OpeningSlotIndex)") "Solar memory default nodes must use the per-layer opening story event."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("CreateSolarMemoryEventNode(layer, MidLayerSlotIndex)")) "Solar memory SelectNode entries must not expose fixed story events as draggable candidates."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryFixedNodeSpec.Event(SolarMemoryMidLayerSlotIndex") "Solar memory runtime must lock the fourth map node as the second story event."
    Assert-True (-not $solarMemoryModeRuntime.Contains("EnsureSolarMemoryMapBeforeMapItems")) "Solar memory must not rewrite MapTree immediately before native MapItemInit consumes default nodes."
    Assert-True (-not $solarMemoryModeRuntime.Contains("NormalMapManager.MapItemInit:before")) "Solar memory MapItemInit hooks must not call ApplyToCurrentLayer before native map item creation."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("GenerateFinaleLayer")) "Solar memory must not generate a dedicated finale map layer while third-layer completion settles immediately."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("CreateSolarFinaleStoryEventNode")) "Solar memory must not create finale pre-boss dialogue nodes in map generation."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("TryCreateFixedEndingNode") "Solar memory must reserve fixed ending nodes for per-layer story and boss endpoints."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("TryCreateFixedBossNode") "Solar memory must reserve fixed story boss nodes for accepted Wuna bosses."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolFactory, 'if\s*\(\s*layer\s*==\s*0\s*\)\s*\{\s*return\s+false\s*;')) "Solar memory must not feed a layer-one ending event into native FightPrefab initialization."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("CreateExpandedBossPoolNode") "Solar memory must use an expanded all-layer boss pool for non-fixed boss nodes."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("SolarMemoryMapNodePoolFactory.TypeGenerateFallback") "Solar memory TypeGenerate fallback nodes must be normalized for NodeDice."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("CreateFightNodeDice(tree)") "Solar memory generated boss nodes must receive per-node fight dice."
    Assert-True $solarMemoryMapNodePoolFactory.Contains('"WithCursor"') "Solar memory boss node dice must route through the game's cursor forking API."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("dice.Roll().Value") "Solar memory boss nodes must consume a unique tree-dice cursor per node."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("node.NodeDice = tree.treedice ?? Dice.Default")) "Solar memory boss nodes must not reuse the shared tree dice directly."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("IsSolarMemoryFixedStoryBoss") "Solar memory expanded boss pool must exclude fixed Wuna story bosses."
    Assert-True $sunExpIds.Contains("SolarBossOrbitMirrorMapId") "Solar memory must define the fixed mirror-array boss map id."
    Assert-True $sunExpIds.Contains("SolarBossSecondSunMapId") "Solar memory must define the fixed second-sun boss map id."
    Assert-True $sunExpIds.Contains("SolarBossSaintWunaMapId") "Solar memory must define the hidden saint boss map id."
    Assert-True $solarMemoryModeRuntime.Contains('"NormalMapManager.ReadyToChangeMap", FinishSolarMemoryAfterFinalLayer') "Solar finale routing must hook ReadyToChangeMap."
    Assert-True (-not $solarMemoryModeRuntime.Contains("SolarFinalePhysicalStartLevel")) "Solar memory immediate settlement must not keep a separate finale physical level."
    Assert-True (-not $solarMemoryMapNodePoolApplier.Contains("IsFinaleLayer() ? 0")) "Solar memory node application must not carry finale segment remapping when completion settles immediately."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.BossScripts") "Entry must register BossScripts for CSV script calls."
    Assert-True $bossScripts.Contains("public static void InitCard") "BossScripts must expose enemy-card init for CSV rows."
    Assert-True $bossScripts.Contains("public static void UseCard") "BossScripts must expose enemy-card use behavior for CSV rows."
    Assert-True $sunExpIds.Contains("BossTraitMirrorArray") "SunExpIds must define the mirror-array boss trait buff id."
    Assert-True $sunExpIds.Contains("BossTraitMercilessDaylight") "SunExpIds must define the merciless-daylight boss trait buff id."
    Assert-True $sunExpIds.Contains("BossTraitWhiteRadianceSaint") "SunExpIds must define the white-radiance-saint boss trait buff id."
    Assert-True $buffScripts.Contains('"boss_trait_mirror_array"') "BuffScripts must route mirror-array boss trait apply/clear."
    Assert-True $buffScripts.Contains('"boss_trait_merciless_daylight"') "BuffScripts must route merciless-daylight boss trait apply/clear."
    Assert-True $buffScripts.Contains('"boss_trait_white_radiance_saint"') "BuffScripts must route white-radiance-saint boss trait apply/clear."
    Assert-True $buffScripts.Contains("BossScripts.ApplyTrait(self, id)") "BuffScripts must delegate boss trait apply to BossScripts."
    Assert-True $buffScripts.Contains("BossScripts.ClearTrait(self, id)") "BuffScripts must delegate boss trait clear to BossScripts."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, SunExpIds.BossTraitMirrorArray)") "Mirror-array boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, SunExpIds.BossTraitMercilessDaylight)") "Second-sun boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, SunExpIds.BossTraitWhiteRadianceSaint)") "Saint Wuna boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("TriggerMirrorArray") "BossScripts must implement the mirror-array trait trigger."
    Assert-True $bossScripts.Contains("TriggerMercilessDaylight") "BossScripts must implement the merciless-daylight trait trigger."
    Assert-True $bossScripts.Contains("TriggerWhiteRadianceSaint") "BossScripts must implement the white-radiance-saint trait trigger."
    Assert-True $bossScripts.Contains("MoveSavedNameToBurned") "Merciless daylight must be able to convert preserved names into burned names."
    Assert-True $bossScripts.Contains("MoveSavedNameToNameless") "White Radiance Saint must be able to convert preserved names into nameless people."
    Assert-True $sunExpIds.Contains('public const string Cripple = "buff_cripple";') "SunExpIds must expose the official Cripple buff id."
    Assert-True $sunExpIds.Contains("BossWhiteRadianceCrown") "SunExpIds must define the White Radiance Crown boss buff id."
    Assert-True $sunExpIds.Contains("EnemyCardSaintWhiteEdict") "SunExpIds must define the White Radiance extra-action card id."
    Assert-True $buffApi.Contains("public static bool RemovePositiveBuffs") "BuffApi must support clearing all positive statuses from a target."
    Assert-True $executorApi.Contains("public static bool DealDamageToTarget") "ExecutorApi must expose explicit target damage for multiplayer boss actions."
    Assert-True $executorApi.Contains("public static bool AddEnemyAction") "ExecutorApi must expose a safe enemy-action append wrapper."
    Assert-True $executorApi.Contains("DealTrueDamageAllEnemiesByMaxHp") "ExecutorApi must support max-HP true damage against all player targets."
    Assert-True $playerApi.Contains("public static string LocalPlayerStatusId") "PlayerApi must expose a local player status id for per-player local effects."
    Assert-True $bossScripts.Contains("LastDayNoonDamage = 28") "Second Sun noon action must deal 28 damage."
    Assert-True $bossScripts.Contains("ExecutorApi.DealDamageToTarget(self, noonTarget, LastDayNoonDamage)") "Second Sun noon action must deal damage to its explicit target."
    Assert-True $bossScripts.Contains("SunExpIds.Cripple") "Second Sun noon action must apply the official Cripple buff."
    Assert-True $bossScripts.Contains("ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, MirrorArrayBurn);") "Mirror Array must apply Burn before counting total Burn stacks."
    Assert-True $bossScripts.Contains("burnTotal += ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);") "Mirror Array shield must count post-application Burn stacks across all targets."
    Assert-True $bossScripts.Contains("maxHp * burnTotal / 100") "Mirror Array shield must scale by 1 percent max HP per Burn stack."
    Assert-True $bossScripts.Contains("MercilessDaylightBodyBurn = 10") "Second Sun failed name burn must apply 10 Body Burn."
    Assert-True (-not $bossScripts.Contains("MercilessDaylightFlame")) "Second Sun trait must no longer grant gathered flame after burning names."
    Assert-True $bossScripts.Contains("WhiteRadianceSaintRadiance = 6") "White Radiance Saint start-round prayer must grant 6 Solar Radiance."
    Assert-True $bossScripts.Contains("ExecutorApi.StatusMaxHp(self.Self) / 10") "White Radiance Saint start-round prayer must grant a 10 percent max HP shield."
    Assert-True $bossScripts.Contains("SaintCoronationRadianceThreshold = 12") "Saint Wuna must coronate at 12 Solar Radiance."
    Assert-True $bossScripts.Contains("SetWhiteRadianceTier(self, WhiteRadianceTier(self) + 1)") "White Radiance Crown must gain one tier each round after coronation."
    Assert-True $bossScripts.Contains("AnnihilateRandomPlayerCards(self, SaintCrownAnnihilateCount)") "White Radiance tier 4 must annihilate random player cards."
    Assert-True $bossScripts.Contains("LocalAnnihilationLockKey") "White Radiance tier 4 must lock annihilation per local player."
    Assert-True $bossScripts.Contains("PlayerApi.LocalPlayerStatusId") "White Radiance tier 4 local lock must use the local player status id."
    Assert-True $bossScripts.Contains("ResolveWhiteRadianceAfterAction") "White Radiance tier 5 must resolve after each Saint action."
    Assert-True $buffData.Contains("boss_trait_mirror_array") "Buff data must define the mirror-array boss trait."
    Assert-True $buffData.Contains("boss_trait_merciless_daylight") "Buff data must define the merciless-daylight boss trait."
    Assert-True $buffData.Contains("boss_trait_white_radiance_saint") "Buff data must define the white-radiance-saint boss trait."
    Assert-True $buffData.Contains("boss_white_radiance_crown") "Buff data must define White Radiance Crown."
    Assert-True $enemyCardData.Contains("enemycard_saint_white_edict") "EnemyCard data must define Wuna's extra White Radiance action."
    Assert-True $cardData.Contains("witch_projection") "Card data must define the projection selection card."
    Assert-True $cardData.Contains("*projection_role_template") "Card data must define the generated projection role template."
    Assert-True $enemyCardData.Contains("enemycard_heart_change_strike") "EnemyCard data must define Heart Change's temporary strike intent."
    Assert-True $enemyCardData.Contains("HeartChangeScripts.InitAction") "Heart Change EnemyCard row must route initialization through HeartChangeScripts."
    Assert-True $enemyCardData.Contains("HeartChangeScripts.UseAction") "Heart Change EnemyCard row must route execution through HeartChangeScripts."
    Assert-True $enemyCardData.Contains("enemycard_projection_staff_tap") "EnemyCard data must define the projection staff-tap action."
    Assert-True $enemyCardData.Contains("enemycard_projection_shield_blessing") "EnemyCard data must define the projection shield action."
    Assert-True $enemyCardData.Contains("ProjectionScripts.InitAction") "Projection enemy-card rows must route initialization through ProjectionScripts."
    Assert-True $enemyCardText.Contains("Turncoat Strike") "EnemyCard text must localize Heart Change's temporary strike intent."
    Assert-True $enemyCardText.Contains("Staff Bonk") "EnemyCard text must localize the projection staff action."
    Assert-True $enemyCardText.Contains("Magic Shield") "EnemyCard text must localize the projection magic-shield action."
    Assert-True (-not $enemyCardText.Contains("threat weight")) "Projection shield text must not promise retired threat-weight behavior."
    Assert-True (-not $enemyCardText.Contains("威胁权重")) "Projection shield Chinese text must not promise retired threat-weight behavior."
    Assert-True $buffText.Contains("Three Thousand Orbit Mirrors") "Buff text must localize the mirror-array boss trait."
    Assert-True $buffText.Contains("Merciless Daylight") "Buff text must localize the merciless-daylight boss trait."
    Assert-True $buffText.Contains("White Radiance Saint") "Buff text must localize the white-radiance-saint boss trait."
    Assert-True $buffText.Contains("Crown Manifestation: White Radiance") "Buff text must localize White Radiance Crown."
    Assert-True $enemyCardText.Contains("Noonday Spinebreaker") "EnemyCard text must describe the strengthened Second Sun noon action."
    Assert-True $enemyCardText.Contains("White Radiance Edict") "EnemyCard text must localize Wuna's extra action."
    Assert-True $enemyData.Contains("SunExp_sunexp_boss_trait_mirror_array") "Mirror-array enemy data must expose its trait in AttributeText."
    Assert-True $enemyData.Contains("SunExp_sunexp_boss_trait_merciless_daylight") "Second-sun enemy data must expose its trait in AttributeText."
    Assert-True $enemyData.Contains("SunExp_sunexp_boss_trait_white_radiance_saint") "Saint Wuna enemy data must expose its trait in AttributeText."
    Assert-True ([regex]::IsMatch($enemyData, "(?m)^boss_orbit_mirror_array,[^,]*,180,12,8,2,3,")) "Mirror-array boss must use enemy rarity 3 so it appears under the official Boss dictionary filter."
    Assert-True ([regex]::IsMatch($enemyData, "(?m)^boss_second_sun_last_day,[^,]*,360,16,12,2,3,")) "Second-sun boss must use enemy rarity 3 so it appears under the official Boss dictionary filter."
    Assert-True ([regex]::IsMatch($enemyData, "(?m)^boss_saint_wuna,[^,]*,320,14,14,2,3,")) "Saint Wuna boss must use enemy rarity 3 so it appears under the official Boss dictionary filter."
    Assert-True $enemyData.Contains("boss_orbit_mirror_array,$bossMirrorName,180") "Mirror-array boss must use the compact White Radiance Mirror Array name."
    Assert-True $enemyData.Contains("boss_second_sun_last_day,$bossSecondSunName,360") "Second-sun boss must use the compact Merciless Second Sun name."
    Assert-True $enemyData.Contains("boss_saint_wuna,$bossSaintWunaName,320") "Saint Wuna boss must keep the requested White Radiance Saint name."
    Assert-True (-not $enemyData.Contains($bossMirrorOldName)) "Enemy data must not keep the overlong mirror-array boss name."
    Assert-True (-not $enemyData.Contains($bossSecondSunOldName)) "Enemy data must not keep the overlong second-sun boss name."
    Assert-True $enemyText.Contains("<title>Mirror Array</title>") "Mirror-array bestiary text must use the renamed mirror-array entry."
    Assert-True $enemyText.Contains("<title>Last Day</title>") "Second-sun bestiary text must use the renamed last-day entry."
    Assert-True $enemyText.Contains("<title>Saint Prayer</title>") "Saint Wuna bestiary text must use the renamed saint-prayer entry."
    Assert-True (-not $enemyText.Contains("<title>Mirror Calibration</title>")) "Boss bestiary text must not keep the old mirror-calibration title."
    Assert-True (-not $enemyText.Contains("<title>Final Purification</title>")) "Boss bestiary text must not keep the old final-purification title."
    Assert-True $keywordText.Contains('"Mirror Array"') "Keyword dictionary must expose Mirror Array."
    Assert-True $keywordText.Contains('"Last Day"') "Keyword dictionary must expose Last Day."
    Assert-True $keywordText.Contains('"Book Burning"') "Keyword dictionary must expose Book Burning."
    Assert-True $keywordText.Contains('"Saint Prayer"') "Keyword dictionary must expose Saint Prayer."
    Assert-True $keywordText.Contains('"Time Engraving"') "Keyword dictionary must expose Time Engraving."
    Assert-True $keywordText.Contains('"Nameless Person"') "Keyword dictionary must expose Nameless Person."
    Assert-True ($enemyCardText.Contains("enemycard_saint_purification,,") -and $enemyCardText.Contains("Saintly Purification")) "Saint purification enemy-card text must use the updated purification name."
    Assert-True ($enemyCardText.Contains("enemycard_saint_return_to_court,,") -and $enemyCardText.Contains("Name Engraved Homeward")) "Saint return enemy-card text must use the updated court-return name."
    Assert-True $solarMemoryModeRuntime.Contains("foreach (var spec in FixedNodeSpecs(layer))") "Solar memory sync repair must force every fixed map node id."
    Assert-True $gameCompatibilityApi.Contains("public static List<Dictionary<string, string>> GetItemsByPack") "Game compatibility API must expose version-safe card-pack item lookup."
    Assert-True $gameCompatibilityApi.Contains("CurrentGetItemsByPack") "Card-pack compatibility lookup must support the current three-argument game API."
    Assert-True $gameCompatibilityApi.Contains("LegacyGetItemsByPack") "Card-pack compatibility lookup must support the legacy two-argument game API."
    Assert-True $gameCompatibilityApi.Contains("GetItemsByPackFallback") "Card-pack compatibility lookup must retain a table-scan fallback."
    Assert-True (-not $solarMemoryStarterDeckRuntime.Contains(".GetPackItems(")) "Solar memory starter deck must not bind directly to the unstable GetPackItems signature."
    Assert-True (-not $solarMemoryModeRuntime.Contains(".GetPackItems(")) "Solar memory setup UI must not bind directly to the unstable GetPackItems signature."
    $sunsetExpedition = [regex]::Match($sunExpHardTagRuntime, "private\s+static\s+void\s+ApplySunsetExpedition\(\)[\s\S]*?private\s+static\s+int\s+ApplyMorningStarDimmedToCombatCards")
    Assert-True $sunsetExpedition.Success "Could not locate ApplySunsetExpedition for source assertion."
    Assert-True (-not $sunsetExpedition.Value.Contains("MirrorSc")) "Sunset Expedition must not borrow the player's generic MirrorSc executor."
    Assert-True (-not $sunsetExpedition.Value.Contains("ChangeHp")) "Sunset Expedition must not call ChangeHp without a dataConfig Id."
    Assert-True $sunsetExpedition.Value.Contains("status.CurHp = nextHp") "Sunset Expedition must apply HP loss through the synchronized status property."
    Assert-True $sunsetExpedition.Value.Contains("if (IsServerAuthority())") "Only the host may advance the shared Sunset Expedition fight count."
    Assert-True (-not $sunExpHardTagRuntime.Contains("ApplyWhiteRadianceCourtCards")) "White Radiance Court must not attach White Radiance to player cards."
    Assert-True (-not $sunExpHardTagRuntime.Contains("ApplyWhiteRadianceToRunDeck")) "White Radiance Court must not mutate the run deck."
    Assert-True (-not $sunExpHardTagRuntime.Contains("ApplyWhiteRadianceToFightZones")) "White Radiance Court must not mutate combat card zones."
    Assert-True $sunExpHardTagRuntime.Contains("CombatVarApi.AddInt(AbyssalShockHpStacksKey, 1)") "Abyssal Shock HP option must add one stack every time it triggers."
    Assert-True $sunExpHardTagRuntime.Contains("while (applied < stacks)") "Abyssal Shock enemy HP scaling must catch enemies up to every triggered HP stack."
    Assert-True $sunExpHardTagRuntime.Contains("Math.Ceiling(Math.Max(1, value) * 1.3)") "Abyssal Shock HP scaling must multiply MaxHp/CurHp by 1.3 each stack."
    Assert-True $sunExpHardTagRuntime.Contains("MorningStarDimmedCostMarker") "Morning Star Dimmed must mark cards after applying the combat cost increase."
    Assert-True $sunExpHardTagRuntime.Contains('RegisterBefore(modConfig, SunExpHookTargets.SkillItemTrueUse, OnSkillUseBefore)') "Stagnant Water must hook skill use before native cooldown is set."
    Assert-True $sunExpHardTagRuntime.Contains('RegisterAfter(modConfig, SunExpHookTargets.SkillItemTrueUse, OnSkillUseAfter)') "Stagnant Water must hook skill use after native cooldown is set."
    Assert-True $sunExpHardTagRuntime.Contains('RunFightStartStep("BlackSunListener"') "A Sunset Expedition failure must not prevent Black Sun listener registration."
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryMapIds[eventIndex]") "Solar memory sync repair must use the fixed story map id array."
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryFullEventIds[eventIndex]") "Solar memory sync repair must use the fixed story event id array."
    Assert-True $eventScripts.Contains("public static void InitSolarMemoryNode") "Solar memory fixed story events must expose an init method."
    Assert-True $eventScripts.Contains("public static void ContinueSolarMemory") "Solar memory fixed story events must expose a continue method."
    Assert-True (-not $eventScripts.Contains("SunExp.Dll.Hooks")) "Solar memory event scripts must not import Hooks directly."
    Assert-True (-not [regex]::IsMatch($eventScripts, "SolarMemory(?:ModeRuntime|PreparationRuntime|PlayerSetupState)")) "Solar memory event scripts must call the GameApi flow facade instead of Hook runtimes."
    Assert-True $eventScripts.Contains("SolarMemoryFlowApi.ContinueAfterPreparation()") "Solar memory event scripts must delegate preparation and story gating through SolarMemoryFlowApi."
    Assert-True $solarMemoryFlowApi.Contains("if (!IsPreparationComplete())") "SolarMemoryFlowApi must gate continuation on preparation completion."
    Assert-True $solarMemoryFlowApi.Contains("StartOrResumePreparation();") "SolarMemoryFlowApi must start preparation when continuation is requested early."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryPostPreparationDialoguePendingKey") "SolarMemoryFlowApi must distinguish dialogue confirmation from first-time dialogue opening."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryStoryGateService.TryStartPostPreparationDialogue") "SolarMemoryFlowApi must route completed preparation through the managed story dialogue flow."
    Assert-True $sunExpIds.Contains("SolarMemorySaintWunaBossPendingKey") "Solar memory must persist a pending hidden-saint boss transition across UI timing gaps."
    Assert-True $solarMemoryFlowApi.Contains('SolarMemoryModeRuntime.ContinueSaintWunaBossFromPreludeDialogue("SolarMemoryDialogue:saint_wuna_prelude")') "Saint Wuna prelude completion must bridge back into the runtime boss transition."
    Assert-True $solarMemoryModeRuntime.Contains("public static void ContinueSaintWunaBossFromPreludeDialogue") "Solar memory runtime must expose a managed continuation for the Saint Wuna prelude."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemorySaintWunaBossPendingKey, true)") "Saint Wuna continuation must mark a retryable pending transition before advancing."
    Assert-True $solarMemoryModeRuntime.Contains("TryContinuePendingSaintWunaBoss(""MapSelectUI.ReadyToSelect"")") "Saint Wuna pending transition must retry when map selection is rebuilt."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryMapNodePoolFactory.CreateFixedBossNode(tree, SunExpIds.SolarBossSaintWunaMapId)") "Saint Wuna continuation must create the fixed boss node through the Solar Memory node factory."
    Assert-True $solarMemoryModeRuntime.Contains("node.SetChild(0, CreateSolarMemoryTerminalNode") "Saint Wuna boss node must include a deterministic child for native RpcNextMap."
    Assert-True $solarMemoryModeRuntime.Contains("GameSaveManager.UpdateNode(bossNode)") "Saint Wuna continuation must persist the restored current node before native map transition."
    Assert-True $solarMemoryModeRuntime.Contains("UIManager.Instance?.CloseUI(""BattleRewardsUI"")") "Saint Wuna continuation must clear stale reward UI before starting the hidden boss."
    Assert-True $solarMemoryModeRuntime.Contains("mapManager.CmdNextMap()") "Saint Wuna continuation must request the native next-map command instead of ending at a log line."
    Assert-True $solarMemoryStoryGateService.Contains("DialogueFlowService.Start") "Solar Memory story gates must start reusable managed dialogue flows."
    Assert-True $dialogueFlowRuntime.Contains("DialogueUI.ChooseOption") "DialogueFlowRuntime must hook native dialogue choice completion."
    Assert-True $dialogueFlowService.Contains("DialogueApi.EndDialogue") "DialogueFlowService must close native dialogue UI from C# after managed choice handling."
    Assert-True (-not $dialogueData.Contains("CS.SunExp.Dll.Scripting")) "Solar Memory Dialogue rows must not call C# from native Dialogue script columns."
    Assert-True $dialogueData.Contains("RoleImage1") "Solar Memory Dialogue rows must expose RoleImage1 overrides for dialogue art."
    Assert-True $dialogueData.Contains("solar_memory_opening_4,,,SunExp_solar_memory_solar_memory_wuna_dialogue,,1,,,Mods/SunExp/ModResource/Images/Dialogue/WuNa") "Solar Memory opening dialogue must complete through a managed final choice with a positioned dialogue role id."
    Assert-True $dialogueData.Contains("solar_memory_second_sun_end_2,,,SunExp_solar_memory_solar_memory_wuna_dialogue,,1,,,Mods/SunExp/ModResource/Images/Dialogue/WuNa") "Solar Memory second-sun ending dialogue must settle only after a managed final choice with a positioned dialogue role id."
    Assert-True $dialogueData.Contains("solar_memory_saint_wuna_prelude_6,,,SunExp_solar_memory_solar_memory_saint_wuna,,1,,,Mods/SunExp/ModResource/Images/Dialogue/WuNa_e") "Solar Memory saint-wuna prelude dialogue must resume map flow only after a managed final choice with a resolvable role id."
    Assert-True $dialogueData.Contains("solar_memory_saint_wuna_end_3,,,SunExp_loneer_loneer,,1,,,Mods/SunExp/ModResource/Images/Dialogue/Loneer") "Solar Memory saint-wuna ending dialogue must settle only after a managed final choice with a resolvable role id."
    Assert-True (-not $dialogueData.Contains(",,,wuna,,")) "Solar Memory Dialogue rows must use full runtime RoleData ids, not short role ids."
    Assert-True (-not $dialogueData.Contains(",,,loneer,,")) "Solar Memory Dialogue rows must use full runtime RoleData ids, not short role ids."
    Assert-True (-not $dialogueData.Contains(",,,solar_memory_saint_wuna,,")) "Solar Memory Dialogue rows must use full runtime RoleData ids, not short role ids."
    Assert-True $solarMemoryRoleData.Contains("DefaultY,DefaultScale") "Solar Memory dialogue roles must expose native dialogue positioning fields."
    Assert-True $solarMemoryRoleData.Contains("solar_memory_wuna_dialogue,Mods/SunExp/ModResource/Images/Avatar/WuNa,Mods/SunExp/ModResource/Images/Dialogue/WuNa,Mods/SunExp/ModResource/Images/Icon/WuNa3,300,1") "Solar Memory Wuna dialogue role must lift the dialogue image above the text box."
    Assert-True $solarMemoryRoleData.Contains("solar_memory_saint_wuna,Mods/SunExp/ModResource/Images/Avatar/WuNa,Mods/SunExp/ModResource/Images/Dialogue/WuNa_e,Mods/SunExp/ModResource/Images/Icon/WuNa3,300,1") "Solar Memory saint Wuna dialogue role must lift the dialogue image above the text box."
    Assert-True $loneerRoleData.Contains("DefaultY,DefaultScale") "Loneer dialogue role must expose native dialogue positioning fields."
    Assert-True $loneerRoleData.Contains("loneer,Mods/SunExp/ModResource/Images/Icon/Loneer2,Mods/SunExp/ModResource/Images/Character/Loneer,Mods/SunExp/ModResource/Images/Dialogue/Loneer,300,1") "Loneer dialogue role must lift the dialogue image above the text box."
    Assert-True $solarMemoryStoryGateService.Contains("CompleteDialogueId") "Solar Memory managed dialogue gates must register the final dialogue id for native option completion."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryPreparationRuntime.IsComplete()") "SolarMemoryFlowApi must bridge preparation completion to the Hook runtime."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryModeRuntime.OpenOriginWindow()") "SolarMemoryFlowApi must bridge origin setup UI to the Hook runtime."
    Assert-True (-not $eventScripts.Contains('PlayerApi.SetGameVar(SunExpIds.SolarMemoryOriginPointsKey, "50")')) "Solar memory event initialization must not reset origin points to the old value."
    Assert-True $mapData.Contains("Id,Type,NodeId,Level,Rarity") "Solar memory map data must expose the RandomPool rarity marker."
    Assert-True $mapData.Contains("solar_memory_black_sun_after,Event,Breaks_solar_memory_black_sun_after,-1,7") "Solar memory story maps must be hidden from every RandomPool draw."
    Assert-True $mapData.Contains("solar_memory_above_sacred_wheel,Event,Breaks_solar_memory_above_sacred_wheel,-1,7") "All fixed Solar Memory story maps must be hidden from every RandomPool draw."
    Assert-True $mapData.Contains("solar_memory_boss_orbit_mirror_array,Fight,SunExp_sunexp_level_orbit_mirror_array,99,7") "Solar memory mirror-array boss must be hidden and use an unreachable normal-adventure layer."
    Assert-True $mapData.Contains("solar_memory_boss_second_sun_last_day,Fight,SunExp_sunexp_level_second_sun_last_day,99,7") "Solar memory second-sun boss must be hidden and use an unreachable normal-adventure layer."
    Assert-True $mapData.Contains("solar_memory_boss_saint_wuna,Fight,SunExp_sunexp_level_saint_wuna,99,7") "Solar memory saint boss must be hidden and use an unreachable normal-adventure layer."
    Assert-True (-not $mapData.Contains("solar_memory_boss_orbit_mirror_array,Fight,SunExp_sunexp_level_orbit_mirror_array,-1")) "Solar memory bosses must not be wildcard candidates in normal adventure."
    Assert-True $levelData.Contains("level_saint_wuna,SunExp_sunexp_boss_saint_wuna,boss,-1") "Solar memory level data must define the hidden saint fight as a boss level."
    Assert-True $mapText.Contains("solar_memory_polluted_light") "Solar memory map text must include the polluted light node."
    Assert-True ($mapText.Contains("solar_memory_boss_saint_wuna") -and $mapText.Contains("Hidden Boss")) "Solar memory map text must mark the hidden saint fight as a boss node."
    Assert-True (-not $mapText.Contains($solarMemoryPrefix)) "Solar memory map event names must not repeat the mode prefix."
    Assert-True (-not $mapText.Contains("Solar Memory - ")) "Localized Solar Memory map event names must stay compact."
    Assert-True ($mapText.Contains("solar_memory_boss_orbit_mirror_array,") -and $mapText.Contains(",$bossMirrorName,")) "Solar memory map text must use the compact mirror-array boss name."
    Assert-True ($mapText.Contains("solar_memory_boss_second_sun_last_day,") -and $mapText.Contains(",$bossSecondSunName,")) "Solar memory map text must use the compact second-sun boss name."
    Assert-True $eventData.Contains("Sub_solar_memory_grief_struggle,CS.SunExp.Dll.Scripting.EventScripts.ContinueSolarMemory();") "Solar memory event data must route story choices through C# continue."
    Assert-True $eventText.Contains("Sub_solar_memory_above_sacred_wheel") "Solar memory event text must include the sixth fixed story row."
    Assert-True (-not $eventText.Contains($solarMemoryPrefix)) "Solar Memory event titles must not repeat the mode prefix."
    Assert-True (-not $eventText.Contains($solarMemoryTraditionalPrefix)) "Traditional Solar Memory event titles must not repeat the mode prefix."
    Assert-True (-not $eventText.Contains("Solar Memory - ")) "Localized Solar Memory event titles must stay compact."
    Assert-True (-not $eventText.Contains("Alderin")) "Solar finale ending text must not refer to Alderin as Wuna's world."
    Assert-True $solarMemoryModeRuntime.Contains("public static int SanitizeSolarMemoryRoleCards") "Solar memory must expose a role-card sanitizer."
    Assert-True $solarMemoryModeRuntime.Contains("RemoveEventConfigs(role.cardList") "Solar memory sanitizer must remove event cards from the actual deck."
    Assert-True $solarMemoryModeRuntime.Contains("RemoveEventConfigs(role.UnCardList") "Solar memory sanitizer must remove event cards from the reserve pool."
    Assert-True $solarMemoryModeRuntime.Contains('SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool")') "Clearing the solar memory reserve must also sanitize the active deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "NormalMapManager.InitRoleTable")') "Solar memory role initialization must sanitize the official starter deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "ApplyStarterDeck")') "Solar memory custom starter deck application must sanitize the final deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "KeepOfficialDeck")') "Solar memory official starter deck path must sanitize before continuing."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("!SolarMemoryModeRuntime.IsSolarMemoryEventCard(id)") "Solar memory starter deck candidates must exclude event cards."
    Assert-True ([regex]::IsMatch($solarMemoryModeRuntime, 'public\s+static\s+void\s+OpenDeckWindow\(\)[\s\S]*SolarMemoryStarterDeckAppliedKey[\s\S]*SolarMemoryPreparationRuntime\.StartOrResume\(\);[\s\S]*return;[\s\S]*SanitizeSolarMemoryRoleCards\(RoleTable\.Instance,\s*"OpenDeckWindow"\)')) "Solar memory deck option must resume starter-deck preparation before opening the native deck window."
    Assert-True (-not [regex]::IsMatch($solarMemoryModeRuntime, 'public\s+static\s+void\s+OpenDeckWindow\(\)[\s\S]*?if\s*\([^)]*SolarMemoryDeckConfiguredKey[\s\S]*?ClearSolarMemoryReservePool\(\);')) "Solar memory deck option must not mark the deck configured before starter-deck selection is applied."
    Assert-True $solarMemoryRunLauncher.Contains('saveInfo.GameVars[SunExpIds.SolarMemoryOriginPointsKey] = "50"') "Solar memory must initialize origin setup with 50 points."
    Assert-True $sunExpIds.Contains("SolarMemoryPrepStepKey") "Solar memory preparation must persist an explicit preparation step."
    Assert-True $solarMemoryRunLauncher.Contains("SolarMemoryPrepStep.DeckSelection") "Solar memory saves must initialize the preparation state machine."
    Assert-True $solarMemoryPreparationRuntime.Contains("public static void StartOrResume") "Solar memory preparation runtime must expose a stable start/resume entry point."
    Assert-True $solarMemoryPreparationRuntime.Contains("InferStepFromLegacyState") "Solar memory preparation runtime must infer state from old boolean keys."
    Assert-True ([regex]::IsMatch($solarMemoryPreparationRuntime, 'public\s+static\s+bool\s+IsComplete\(\)[\s\S]*SolarMemorySetupFinishedKey[\s\S]*ReadOrInferStep\(\)\s*==\s*SolarMemoryPrepStep\.Complete')) "Solar memory preparation must not report complete until final setup commit is marked finished."
    $solarMemorySetupSources = $solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime + $solarMemoryPreparationRuntime + $solarMemoryModeRuntime + $solarMemoryRunLauncher
    Assert-True (-not $solarMemorySetupSources.Contains("StarterDeckArbiterRuntime.SyncRoleTable")) "Solar memory preparation must not use the native RoleTable collector before final setup completion."
    Assert-True ([regex]::IsMatch($solarMemoryStarterDeckRuntime, 'ApplyDeck\([\s\S]*?sync:\s*false\)')) "Solar memory custom starter deck must suppress intermediate role synchronization."
    Assert-True ([regex]::IsMatch($solarMemoryStarterDeckRuntime, 'KeepOfficialDeck\(roleTable,\s*CreateClaim\(mode\),\s*sync:\s*false\)')) "Solar memory official starter deck path must suppress intermediate role synchronization."
    Assert-True ([regex]::IsMatch($endlessSeaIntroBoardRuntime, 'ApplyDeck\([\s\S]*?sync:\s*true\)')) "Endless Sea starter deck choices must persist through the shared role sync path."
    Assert-True $endlessSeaRunLauncher.Contains("EndlessSeaRunStateStore.FindLatestUnfinishedRun") "Endless Sea launcher must resume unfinished Endless Sea saves before creating a new run."
    Assert-True $endlessSeaRunLauncher.Contains("ShowContinuePrompt") "Endless Sea launcher must prompt before continuing or replacing an unfinished Endless Sea run."
    Assert-True $endlessSeaRunLauncher.Contains("buttonLayout.childControlWidth = true") "Endless Sea continue prompt buttons must be laid out horizontally with controlled widths."
    Assert-True $endlessSeaRunLauncher.Contains("element.minHeight = 50f") "Endless Sea continue prompt buttons must keep a readable minimum height."
    Assert-True $endlessSeaRunLauncher.Contains("EndlessSeaRunStateStore.DeleteUnfinishedRuns") "Endless Sea launcher must delete unfinished Endless Sea saves only after the player chooses a new run."
    Assert-True $endlessSeaRunLauncher.Contains("modeType = NativeMapModeType") "Endless Sea saves must use native Normal mode so the official map manager can start."
    Assert-True $endlessSeaRunLauncher.Contains("private const string NativeMapModeType = SunExpIds.NativeNormalModeType") "Endless Sea must keep native map startup on the official Normal mode manager."
    Assert-True $endlessSeaRunLauncher.Contains("SetLobbyModeType(NativeMapModeType)") "Endless Sea lobby launch must reuse the native Normal mode manager."
    Assert-True (-not $endlessSeaRunLauncher.Contains("SetLobbyModeType(SunExpIds.EndlessSeaModeType)")) "Endless Sea must not pass its custom save mode type into the native lobby map startup."
    Assert-True (-not $endlessSeaRunLauncher.Contains("modeType = SunExpIds.EndlessSeaModeType")) "Endless Sea saves must not store custom modeType values that break native map startup."
    Assert-True $endlessSeaRunLauncher.Contains("EndlessSeaRunStateStore.InitializeNewRun") "Endless Sea launcher must delegate save initialization to the run-state store."
    Assert-True $endlessSeaRunStateStore.Contains("saveInfo.modeType = SunExpIds.NativeNormalModeType") "Endless Sea run-state repair must migrate Endless Sea saves back to native Normal mode."
    Assert-True $endlessSeaModeRuntime.Contains("EndlessSeaSaveCacheRuntime.Initialize(modConfig)") "Endless Sea runtime must isolate Endless Sea saves from the official Normal continue cache."
    Assert-True $endlessSeaSaveCacheRuntime.Contains('"ModeChoiceUI.NormalMode"') "Endless Sea save cache isolation must run before native Normal mode uses its cached save."
    Assert-True $endlessSeaSaveCacheRuntime.Contains('"ModeChoiceUI.DeleteExistingSavesForMode"') "Endless Sea save cache isolation must protect Endless Sea saves from native Normal cleanup."
    Assert-True $endlessSeaSaveCacheRuntime.Contains("TemporarilyProtectedSaves") "Endless Sea save cache isolation must restore Endless Sea saves after native cleanup."
    Assert-True $endlessSeaSaveCacheRuntime.Contains("ModeChoiceSaveCacheApi.ClearCachedSaveIf") "Endless Sea save cache isolation must route official cache mutation through GameApi."
    Assert-True $modeChoiceSaveCacheApi.Contains("ModeChoiceUI.beforeSave") "Mode choice save cache GameApi must own official beforeSave access."
    Assert-True $endlessSeaRunStateStore.Contains("DeleteUnfinishedRuns") "Endless Sea run-state store must own unfinished-run deletion."
    Assert-True $endlessSeaRunStateStore.Contains('Set(saveInfo, SunExpIds.EndlessSeaIntroSeenKey, "0")') "Endless Sea saves must initialize the intro board as unseen."
    Assert-True $endlessSeaRunStateStore.Contains('Set(saveInfo, SunExpIds.EndlessSeaStarterDeckAppliedKey, "0")') "Endless Sea saves must initialize starter-deck selection as unapplied."
    Assert-True $endlessSeaRunStateStore.Contains('Set(saveInfo, SunExpIds.EndlessSeaFloorPlanKey, "")') "Endless Sea saves must initialize the persisted floor plan slot."
    Assert-True $endlessSeaRunStateStore.Contains("EndlessSeaRunIdKey") "Endless Sea saves must persist a run id."
    Assert-True $endlessSeaRunStateStore.Contains("EndlessSeaRunPhaseKey") "Endless Sea saves must persist a phase."
    Assert-True $endlessSeaRunLauncher.Contains('saveInfo.GameVars[GameVar.ExLockDes.ToString()] = "0"') "Endless Sea saves must not pre-lock editable map slots."
    Assert-True $endlessSeaFloorPlanner.Contains("EndlessSeaNodeKind.Monster") "Endless Sea floor planner must fix the native start slot as a monster."
    Assert-True $endlessSeaFloorPlanner.Contains("EndlessSeaNodeKind.Boss") "Endless Sea floor planner must fix the final boss slot."
    Assert-True $endlessSeaFloorPlanner.Contains("new List<EndlessSeaSlotPlan>(SunExpIds.EndlessSeaNativeDefaultNodeCount)") "Endless Sea floor planner must prefill only native fixed slots."
    Assert-True $endlessSeaMapBuilder.Contains("EndlessSeaFloorPlanStore.Save(plan)") "Endless Sea map builder must persist the visual floor plan."
    Assert-True $endlessSeaMapBuilder.Contains("EndlessSeaMapProjectionService.NativeDefaultOrder") "Endless Sea map builder must route native bootstrap ordering through projection."
    Assert-True $endlessSeaMapBuilder.Contains('SetSaveValue(GameVar.ExLockDes.ToString(), "0")') "Endless Sea map builder must leave editable native map slots unlocked."
    Assert-True $endlessSeaMapBuilder.Contains("EndlessSeaSelectableNodeDeckPlanner.CreateKinds") "Endless Sea map builder must route selectable node composition through a planner."
    Assert-True $endlessSeaSelectableNodeDeckPlanner.Contains("EndlessSeaNodeKind.Rest") "Endless Sea selectable node planner must include one rest node card."
    Assert-True $endlessSeaSelectableNodeDeckPlanner.Contains("EndlessSeaNodeKind.Building") "Endless Sea selectable node planner must include one building node card."
    Assert-True $endlessSeaMapProjectionService.Contains("EndlessSeaNativeDefaultNodeCount") "Endless Sea native bootstrap must keep only the native start placeholder and boss defaults."
    Assert-True $endlessSeaMapProjectionService.Contains("EndlessSeaNodeKind.Rest") "Endless Sea native bootstrap must feed the native Start slot a safe non-fight placeholder."
    Assert-True $endlessSeaMapProjectionService.Contains('NodeType(tree.DefaultNode[0]) != "Fight"') "Endless Sea native bootstrap must keep DefaultNode[0] safe for native Start initialization."
    Assert-True $endlessSeaMapProjectionService.Contains('NodeType(tree.DefaultNode[1]) == "Fight"') "Endless Sea native bootstrap must keep DefaultNode[1] as the boss fight."
    Assert-True $endlessSeaMapViewPresenter.Contains("ClearEditableSlots") "Endless Sea map presenter must clear middle slots for player node-card placement."
    Assert-True $endlessSeaMapViewPresenter.Contains("nodes[slot].data = null") "Endless Sea editable map slots must start empty."
    Assert-True $endlessSeaMapViewPresenter.Contains('return string.Equals(type, "Fight"') "Endless Sea map presenter must route non-fight nodes away from FightPrefab."
    Assert-True $endlessSeaMapViewPresenter.Contains('"EventPrefab"') "Endless Sea map presenter must render building slots with a native EventPrefab."
    Assert-True (-not $endlessSeaMapViewPresenter.Contains('"BuildPrefab"')) "Endless Sea map presenter must not request a non-native BuildPrefab."
    Assert-True $endlessSeaModeRuntime.Contains("EndlessSeaMapViewPresenter.ApplySlots") "Endless Sea runtime must delegate visible slot repair to the map presenter."
    Assert-True (-not $endlessSeaModeRuntime.Contains('"MapSelectUI.DataUpdate", ScheduleAbyssMapPanels')) "Endless Sea must not request abyss panels from repeated MapSelectUI.DataUpdate ticks."
    Assert-True $endlessSeaNetworkSync.Contains("applyAllSlots: false") "Endless Sea snapshot UI refresh must be fixed-slot only."
    Assert-True (-not $endlessSeaNetworkSync.Contains("applyAllSlots: true")) "Endless Sea snapshots must not clear editable map slots during interaction."
    Assert-True $endlessSeaNetworkSync.Contains("SnapshotRequestThrottleSeconds") "Endless Sea client snapshot requests must be throttled."
    Assert-True $endlessSeaNetworkSync.Contains("SunExpNetworkRuntime.HasRemotePlayers()") "Endless Sea snapshots must only run for real multiplayer sessions."
    Assert-True $sunExpNetworkRuntime.Contains("public static bool HasRemotePlayers()") "SunExp network runtime must expose an actual remote-player guard."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("AddTextFill(header.transform") "Endless Sea intro board must render a header subtitle."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("SetDeckButtonsInteractable(false)") "Endless Sea deck application must disable buttons while applying."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("SetDeckButtonsInteractable(true)") "Endless Sea deck application must restore buttons on retryable failure."
    Assert-True $endlessSeaStarterDeckCatalog.Contains("public const int FixedDeckSize = 11") "Endless Sea hardcoded starter decks must keep an 11-card fixed package."
    Assert-True $endlessSeaStarterDeckCatalog.Contains("public const int ThemeDeckSize = 4") "Endless Sea hardcoded starter decks must add a 4-card theme package."
    Assert-True $endlessSeaStarterDeckCatalog.Contains("public const int DeckSize = FixedDeckSize + ThemeDeckSize") "Endless Sea hardcoded starter decks must total fixed plus theme cards."
    Assert-True (([regex]::Matches($endlessSeaStarterDeckCatalog, 'new\(\s*\r?\n\s*"').Count) -ge 11) "Endless Sea must expose the default theme plus configured official pack themes."
    Assert-True $endlessSeaStarterDeckCatalog.Contains("AvailableProfiles()") "Endless Sea must filter starter deck themes by available official card packs."
    Assert-True $endlessSeaStarterDeckCatalog.Contains('"academy_required"') "Endless Sea must expose the Academy Required starter deck id."
    Assert-True $endlessSeaStarterDeckCatalog.Contains('"church_defense_tactics"') "Endless Sea must expose the Church Defense Tactics theme deck id."
    Assert-True $endlessSeaStarterDeckCatalog.Contains('"origin_of_elements"') "Endless Sea must expose the Origin of Elements theme deck id."
    Assert-True $endlessSeaStarterDeckCatalog.Contains('"card_3"') "Endless Sea starter decks must use official default cards."
    Assert-True $endlessSeaStarterDeckCatalog.Contains('"burningcard_1"') "Endless Sea starter decks must use official default cards."
    Assert-True (-not $endlessSeaStarterDeckCatalog.Contains('"spark"')) "Endless Sea starter decks must not use unresolved SunExp short card ids."
    Assert-True (-not $endlessSeaStarterDeckCatalog.Contains('"solar_prayer"')) "Endless Sea starter decks must not use unresolved SunExp short card ids."
    Assert-True $endlessSeaStarterDeckCatalog.Contains("new DataConfig(cardId, DataType.Card)") "Endless Sea starter deck catalog must validate card ids through DataConfig."
    Assert-True $endlessSeaRichTextSanitizer.Contains("AllowedSimpleTags") "Endless Sea rich text sanitizer must use an explicit simple-tag allowlist."
    Assert-True $endlessSeaRichTextSanitizer.Contains("AllowedScopedTags") "Endless Sea rich text sanitizer must use an explicit scoped-tag allowlist."
    Assert-True (-not $endlessSeaRichTextSanitizer.Contains("link")) "Endless Sea rich text sanitizer must not allow link tags."
    Assert-True $endlessSeaOriginService.Contains('role.enchasedDict[card.InstanceID] = new DataConfig("enchtag_2", DataType.EnchTag);') "Endless Sea Magic 50 unstable thoughts must attach the Extinction enchant tag."
    Assert-True $endlessSeaOriginService.Contains("FortuneExtraTriggerThreshold = 150") "Endless Sea Fortune 50 must define the 150-point extra trigger threshold."
    Assert-True $endlessSeaOriginService.Contains("bonus += FortuneExtraTriggers") "Endless Sea Fortune 50 must add two extra triggers after reaching 150."
    Assert-True $endlessSeaCardAffixRuntime.Contains("EndlessSeaCardAffixService.ApplyBurnout") "Endless Sea card affix runtime must delegate Burnout application to the service."
    Assert-True $endlessSeaCardAffixRuntime.Contains("EndlessSeaCardAffixService.NormalizeOwnedCards") "Endless Sea card affix runtime must normalize owned cards from non-reward gain paths."
    Assert-True $endlessSeaCardAffixService.Contains("CardAttachmentService.AttachToConfig") "Endless Sea card affix service must use the shared card attachment service."
    Assert-True $endlessSeaCardAffixService.Contains("EndlessSeaStarterDeckBaselineMarker") "Endless Sea card affix service must protect starter deck baseline cards."
    Assert-True $endlessSeaCardAffixService.Contains("RunWithStarterDeckSuppressed") "Endless Sea starter deck writes must suppress automatic Burnout attachment."
    Assert-True $endlessSeaCardAffixService.Contains("role.cardList") "Endless Sea card affix service must normalize equipped deck cards."
    Assert-True $endlessSeaCardAffixService.Contains("role.UnCardList") "Endless Sea card affix service must normalize reserve cards."
    Assert-True $endlessSeaCombatRuntime.Contains("EndlessAbyssEnemyInjectionService.TryInjectAfterFightInit") "Endless Sea combat runtime must delegate extra enemy injection to a SunExp-owned service."
    Assert-True (-not $endlessSeaCombatRuntime.Contains("CmdAddEnemy")) "Endless Sea combat runtime must not directly issue native enemy-add commands."
    Assert-True $endlessAbyssEnemyInjectionService.Contains("EnemyApi.IsClientOnlyDynamicEnemyObserver()") "Endless Abyss extra enemy planning must be skipped on client-only observers."
    Assert-True $endlessAbyssEnemyInjectionService.Contains("EnemyApi.AddDynamicEnemyAuthoritative") "Endless Abyss extra enemies must use the SunExp-owned EnemyApi wrapper."
    Assert-True $enemyApi.Contains("PlayerManager.Instance") "EnemyApi must own the multiplayer authority check for dynamic enemy adds."
    Assert-True $enemyApi.Contains("EnemyManager.Instance") "EnemyApi must resolve the native enemy manager before adding a dynamic enemy."
    Assert-True $enemyApi.Contains("manager.AddEnemy(enemyId)") "EnemyApi must follow the game's native dynamic enemy-add entry point."
    Assert-True (-not $enemyApi.Contains("CmdAddEnemy")) "EnemyApi must not call CmdAddEnemy directly."
    Assert-True $endlessAbyssConfig.Contains("RewardPools") "Endless Abyss config must expose independent reward pool definitions."
    Assert-True $endlessAbyssConfig.Contains("OtherDimensionCardPoolId") "Endless Abyss milestone rewards must address a configured reward pool instead of a hard-coded card pack."
    Assert-True $endlessAbyssConfigJson.Contains('"rewardPools"') "Endless Abyss shipped config must define reward pools."
    Assert-True $endlessAbyssConfigJson.Contains('"milestone.other_dimension.cards"') "Endless Abyss shipped config must bind the other-dimension milestone reward to its independent pool."
    Assert-True $endlessAbyssConfigJson.Contains('"SunExp_sunexp_cardpack_more_dimensions"') "Endless Abyss default other-dimension pool must be initialized from the More Dimensions card pack."
    Assert-True $endlessAbyssConfigJson.Contains('"heart_change"') "Endless Abyss legacy other-dimension fallback must include Heart Change."
    Assert-True $endlessAbyssRewardPoolService.Contains("CardPackMatches") "Endless Abyss reward pools must expand card pack sources generically."
    Assert-True $endlessAbyssRewardPoolService.Contains("IncludeCardIds") "Endless Abyss reward pools must support explicit card inclusions."
    Assert-True $endlessAbyssRewardPoolService.Contains("ExcludeCardIds") "Endless Abyss reward pools must support explicit card exclusions."
    Assert-True $endlessAbyssMilestoneRewardService.Contains("EndlessAbyssRewardPoolService.CardIds") "Endless Abyss milestone other-dimension rewards must draw from the reward pool service."
    Assert-True $endlessAbyssMilestoneRewardService.Contains("PlayerApi.TryAddCardToDeck") "Endless Abyss milestone card rewards must verify deck grants before claiming."
    Assert-True $endlessAbyssMilestoneRewardService.Contains("ResultKey") "Endless Abyss milestone rewards must record each player's selected result."
    Assert-True $endlessAbyssMilestoneRewardService.Contains("TryPersistCurrentRole") "Endless Abyss milestone rewards must treat role persistence as best-effort after local reward application."
    Assert-True (-not $endlessAbyssMilestoneRewardService.Contains("GameSaveManager.UpdateRoles(RoleTable.Instance)")) "Endless Abyss milestone reward settlement must not fail non-host clients through direct role persistence."
    Assert-True $endlessAbyssRunLedger.Contains("ContainsPrefix") "Endless Abyss ledger must be able to detect player-scoped milestone result records."
    Assert-True $playerApi.Contains("public static bool TryAddCardToDeck") "PlayerApi must expose a verified out-of-combat deck grant helper."
    Assert-True $playerApi.Contains("OwnedCardSnapshot") "Verified deck grants must compare owned card snapshots."
    Assert-True $endlessAbyssMilestoneRewardPanel.Contains("EndlessAbyssFramedTextCard.Create") "Endless Abyss milestone options must render title/body inside a framed content inset."
    Assert-True $endlessAbyssShockPanel.Contains("EndlessAbyssFramedTextCard.Create") "Endless Abyss shock options must share the framed content inset layout."
    Assert-True $solarMemoryPreparationRuntime.Contains('if (!SolarMemoryRoleCommitApi.CommitFinal(RoleTable.Instance, "SunExp.SolarMemory.SetupFinished"))') "Solar memory preparation completion must require a successful final role commit."
    Assert-True ([regex]::IsMatch($solarMemoryPreparationRuntime, 'CommitFinal\(RoleTable\.Instance,\s*"SunExp\.SolarMemory\.SetupFinished"\)[\s\S]*SolarMemoryPlayerSetupState\.SetFlag\(SunExpIds\.SolarMemorySetupFinishedKey,\s*false\)')) "Solar memory preparation must withdraw setup completion when final role commit fails."
    Assert-True $solarMemoryPreparationRuntime.Contains('SolarMemoryPlayerSetupState.SetValue(SunExpIds.SolarMemorySetupCommitTokenKey, "")') "Solar memory preparation must clear failed local commit tokens for retry."
    Assert-True $solarMemoryPreparationRuntime.Contains("setup completion is pending retry") "Solar memory preparation must log failed final role commits as retryable."
    Assert-True (-not $solarMemorySetupFlowRuntime.Contains("FinishSetup()")) "Solar memory setup flow must not retain an unreachable competing completion path."
    Assert-True $solarMemoryRoleCommitApi.Contains("SendRpcCommand(new RpcSolarMemoryRoleCommit") "Solar memory clients must submit the final role through a dedicated RPC command."
    Assert-True (-not $solarMemoryRoleCommit.Contains("CmdSyncRoleTable")) "Solar memory final role commit must not call the native role collector."
    Assert-True (-not $solarMemoryRoleCommit.Contains("ReceiveRoleTable")) "Solar memory final role commit must not increment GameServer.roleCount."
    Assert-True $solarMemoryRoleCommit.Contains("server.RoleTables[role.Id] = role") "Solar memory final role commit must update the authoritative role dictionary."
    Assert-True $solarMemoryRoleCommit.Contains("GameSaveManager.UpdateRoles(role)") "Solar memory final role commit must persist the authoritative role."
    Assert-True $solarMemoryRoleCommit.Contains("SolarMemorySetupFinishedKey") "Solar memory final role commit must reject unfinished preparation state."
    Assert-True $solarMemoryRoleCommitApi.Contains("SolarMemorySetupCommitTokenKey") "Solar memory final role submission must suppress local re-entry with a per-run token."
    Assert-True $solarMemoryRoleCommit.Contains("CommittedTokens.Add(commitToken)") "Solar memory final role command must suppress duplicate network delivery."
    Assert-True ($modConfig.ModVersion -eq "0.4.2") "SunExp network protocol change must ship as version 0.4.2."
    Assert-True ($modConfig.MustSame -eq $true) "SunExp must require an identical multiplayer mod version."
    Assert-True $audioArbiterRuntime.Contains('CurrentBuildId = "audio-arbiter-2026-07-08-v6"') "Audio arbiter must expose the owner-qualified provider runtime build id."
    Assert-True $audioArbiterRuntime.Contains('const string sharedPrefix = "Shared:"') "Audio arbiter must resolve AuraShared resource paths."
    Assert-True $audioArbiterRuntime.Contains("MatchesProviderRequest") "Audio arbiter must expose owner-aware provider matching."
    Assert-True ([regex]::IsMatch($audioArbiterRuntime, 'MatchesProviderRequest\(requestedProviderId,\s*"",\s*ownerStrict:\s*false\)')) "Audio bare provider matching must remain backward-compatible."
    Assert-True ([regex]::IsMatch($audioArbiterRuntime, 'MatchesProviderRequest\(\s*requestedProviderId,\s*requestedOwnerModId,\s*ownerStrict:\s*true\)')) "Audio explicit owner-scoped requests must use strict owner-aware matching."
    Assert-True ([regex]::IsMatch($audioArbiterRuntime, 'request\.IsRemote[\s\S]*WarnProviderMismatchOnce\(request,\s*"Remote sound provider mismatch"\)')) "Audio remote provider mismatch must fail closed and log a diagnostic."
    Assert-True $audioArbiterRuntime.Contains("request.ProviderId = provider.ProviderId") "Audio RPC payload must retain bare ProviderId for legacy receivers."
    Assert-True $audioArbiterRuntime.Contains("request.OwnerModId = provider.OwnerModId") "Audio RPC payload must preserve OwnerModId for deterministic remote matching."
    Assert-True $audioArbiterRuntime.Contains("OwnerModId to disambiguate") "Audio RPC compatibility comment must document OwnerModId-based matching."
    Assert-True $battleBgmArbiterRuntime.Contains('CurrentBuildId = "battle-bgm-arbiter-2026-06-23-v4"') "Battle BGM arbiter must expose its owner-qualified provider runtime build id."
    Assert-True $battleBgmArbiterRuntime.Contains("Fake loss detected; BGM settlement deferred until escape reset") "Battle BGM arbiter must defer fake-loss settlement."
    Assert-True $battleBgmArbiterRuntime.Contains("Duplicate fight end ignored") "Battle BGM arbiter must ignore duplicate end callbacks."
    Assert-True $battleBgmArbiterRuntime.Contains("leaving current BGM unchanged") "Battle BGM arbiter must preserve audio when no snapshot exists."
    Assert-True (-not $battleBgmArbiterRuntime.Contains("StopMainBgm")) "Battle BGM end handling must never stop the current BGM as a missing-snapshot fallback."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("public static bool OpenOrResume") "Solar memory starter deck runtime must expose a resumable preparation entry point."
    Assert-True (-not $eventScripts.Contains("OpenSolarMemoryPreparation")) "Retired solar memory start event must not leave a preparation EventScript entry point."
    Assert-True (-not $eventData.Contains("Sub_solar_memory_start")) "Retired solar memory start event must not remain in EventList data."
    Assert-True $solarMemorySetupFlowRuntime.Contains("private const int OriginSetupPointTotal = 50") "Solar memory origin setup must expose 50 assignable points."
    Assert-True (-not $solarMemorySetupFlowRuntime.Contains("private const int OriginStatCap = 40")) "Solar memory origin setup must not use a fixed 40 cap for every origin."
    Assert-True $solarMemorySetupFlowRuntime.Contains("private const int OriginLargeStep = 10") "Solar memory origin setup must support ten-point increments."
    Assert-True $solarMemorySetupFlowRuntime.Contains('CreateLayoutButton(controls, "++"') "Solar memory origin setup must render a ++ button."
    Assert-True $solarMemorySetupFlowRuntime.Contains("AllowedOriginAdd") "Solar memory origin setup must clamp additions by remaining points and stat cap."
    Assert-True $solarMemorySetupFlowRuntime.Contains("OriginCapFor") "Solar memory origin setup must resolve dynamic caps from the chosen origin roles."
    Assert-True $solarMemorySetupFlowRuntime.Contains("role.MainVarUpperBound") "Solar memory origin setup must use RoleTable.MainVarUpperBound for the main origin."
    Assert-True $solarMemorySetupFlowRuntime.Contains("role.SecondaryVarUpperBound") "Solar memory origin setup must use RoleTable.SecondaryVarUpperBound for the secondary origin."
    Assert-True $solarMemorySetupFlowRuntime.Contains("role.OtherVarUpperBound") "Solar memory origin setup must use RoleTable.OtherVarUpperBound for unchosen origins."
    Assert-True $solarMemorySetupFlowRuntime.Contains("OriginAssignablePointTotal") "Solar memory origin setup must reduce total points when current stat capacity is below 50."
    Assert-True $solarMemorySetupFlowRuntime.Contains("NormalizePendingOriginAdds") "Solar memory origin setup must re-clamp pending points before confirmation."
    Assert-True $solarMemorySetupFlowRuntime.Contains("OriginRoleLabel") "Solar memory origin setup must show whether an origin is main, secondary, or unchosen."
    Assert-True $solarMemorySetupFlowRuntime.Contains("SolarMemoryBlessingPickerRuntime.Open") "Solar memory blessing setup must use the custom quota picker."
    Assert-True (-not $solarMemorySetupFlowRuntime.Contains("BlessingChoiceGenerator().CreateBlessUI")) "Solar memory setup must not chain the native blessing picker."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("public const int Tier4Quota = 2") "Solar memory blessing picker must offer two tier-4 blessings."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("public const int Tier3Quota = 3") "Solar memory blessing picker must offer three tier-3 blessings."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("public const int Tier2Quota = 5") "Solar memory blessing picker must offer five tier-2 blessings."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("public const int Tier1Quota = 5") "Solar memory blessing picker must offer five tier-1 blessings."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("PlayerApi.AddBless(id)") "Solar memory blessing picker must grant selected blessings through PlayerApi.AddBless."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SolarMemoryPlayerSetupState.SetSelectedBlessings") "Solar memory blessing picker must persist selected ids per player for re-entry safety."
    Assert-True $solarMemoryPlayerSetupState.Contains("role.SpecialVarMap[key]") "Solar memory setup state must store preparation choices on the current role."
    Assert-True $solarMemoryPlayerSetupState.Contains("!PlayerApi.IsMultiplayerSession()") "Solar memory setup state must not migrate legacy global preparation values during multiplayer."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selected.Add(entries[index % entries.Count].Id)") "Solar memory blessing auto-fill must allow duplicate blessings when needed."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selected.RemoveAt(index)") "Solar memory blessing picker must remove selected rows by index for duplicate ids."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("private static bool IsSelected")) "Solar memory blessing picker must not globally deduplicate blessing ids."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("if (IsSelected(entry.Id))")) "Solar memory blessing picker must allow duplicate manual selections."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("CreateBlessUI")) "Solar memory custom blessing picker must not call the native blessing choice UI."
    Assert-True (-not $eventScripts.Contains("CanClaim(progress)")) "Retired Wuna event rewards must not leave progress-claim code behind."
    Assert-True (-not $mapData.Contains("solar_event,Event,Breaks_solar_event,-1,7")) "Retired legacy solar event map must be removed from Map data."
    Assert-True (-not $eventData.Contains("Sub_wuna_event_")) "Retired Wuna event rows must be removed from EventList data."
    Assert-True (-not $eventData.Contains("Sub_solar_finale_")) "Retired solar finale event rows must be removed from EventList data."
    $exclusiveMapRows = @($mapData -split "`r?`n" | Where-Object { $_ -match '^solar_memory_' })
    Assert-True ($exclusiveMapRows.Count -eq 9) "Solar Memory isolation assertions must cover every shipped exclusive map row."
    Assert-True (-not $mapText.Contains("solar_event,")) "Retired legacy solar event map text must be removed."

    Write-Host "C# source assertions passed."
}

$repoRoot = Get-RepoRoot

if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    if ([string]::IsNullOrWhiteSpace($GamePath)) {
        $ManagedPath = Join-Path $repoRoot "Managed"
    }
    else {
        $ManagedPath = Join-Path $GamePath "Witch's Apocalyptic Journey_Data\Managed"
    }
}

if (-not $SkipBuild) {
    & (Join-Path $repoRoot "tools\Build-SunExpDll.ps1") -Configuration $Configuration -ManagedPath $ManagedPath | Out-Host
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("sunexp-csharp-test-" + [System.Guid]::NewGuid().ToString("N"))
$sourceDir = Join-Path $tmpRoot "src"
New-Item -ItemType Directory -Path $sourceDir | Out-Null

try {
    Write-Utf8NoBom -Path (Join-Path $tmpRoot "SunExp.CSharpTests.csproj") -Text (New-ProjectXml -RepoRoot $repoRoot -SourceDir $sourceDir)
    Write-Utf8NoBom -Path (Join-Path $sourceDir "Stubs.cs") -Text (New-StubsSource)
    Write-Utf8NoBom -Path (Join-Path $sourceDir "Tests.cs") -Text (New-TestsSource)

    dotnet run --project (Join-Path $tmpRoot "SunExp.CSharpTests.csproj") -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "SunExp C# tests failed."
    }

    Invoke-SourceAssertions -RepoRoot $repoRoot
}
finally {
    if ($KeepTemp) {
        Write-Host "Kept temp directory: $tmpRoot"
    }
    else {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
