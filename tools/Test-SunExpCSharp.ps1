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
    $cardApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardApi.cs"
    $cardConfigApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"
    $cardMutationService = Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardMutationService.cs"
    $starBlessingCostOverrideStore = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarBlessingCostOverrideStore.cs"
    $loneerCombatState = Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerCombatState.cs"
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
    <Compile Include="$cardApi" />
    <Compile Include="$cardConfigApi" />
    <Compile Include="$cardMutationService" />
    <Compile Include="$starBlessingCostOverrideStore" />
    <Compile Include="$loneerCombatState" />
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
}

public sealed class DataConfig : IDataConfig
{
    public DataConfig(IDictionary<string, string> data, IDictionary<string, string>? vars = null)
    {
        this.data = data;
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
}

namespace SunExp.Dll.Infrastructure
{
    public static class SunExpLog
    {
        public static void Warn(string message)
        {
        }

        public static void Debug(string message)
        {
        }
    }
}

namespace Witch.UI.Window
{
    public static class FightUI
    {
        public static List<CardItem> cardItemList { get; } = new();
    }
}
'@
}

function New-TestsSource {
@'
using System;
using System.Collections.Generic;
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
        TestSolarTriggerCostOverride();
        TestWhiteRadianceTags();
        TestTemporaryWhiteRadianceClaim();
        TestSolarMemoryIsolationIds();
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
        True(store.BeginPreview(config), "Star blessing begins one preview transaction");
        Equal(0, CardConfigApi.CurrentCost(config), "Star blessing preview displays zero cost");
        False(store.BeginPreview(config), "Star blessing preview is idempotent for the same card instance");
        store.Cancel(config);
        Equal("-1", config.Vars["OnceExCost"], "Cancelling star blessing restores the original one-use modifier");
        Equal(3, CardConfigApi.CurrentCost(config), "Cancelling star blessing restores the normal displayed cost");

        True(store.BeginPreview(config), "Star blessing preview can begin again after cancellation");
        store.MarkBlessingConsumed(config);
        store.MarkActionObserved(config);
        True(store.ActionObserved(config), "Confirmed card action marks the preview transaction committed");
        var committed = store.Commit(config);
        True(committed.BlessingConsumed, "Committed transaction reports that the blessing was consumed");
        Equal("0", config.Vars["OnceExCost"], "Successful play consumes all one-use cost modifiers");
        Equal(4, CardConfigApi.CurrentCost(config), "The card returns to its normal non-once cost after successful play");

        True(store.BeginPreview(config), "A later blessing can preview the same card again");
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

        var failing = new ScriptExecutor { ThrowOnDelivery = true };
        var failed = CardApi.GrantCardToHand(failing, CardGrantRequest.ToHand("spark"));
        False(failed.Success, "CardApi grant returns structured failure on delivery errors");
        Equal("deliver", failed.FailureStep, "CardApi grant identifies the failing step");
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

        CardMutationService.MarkTemporaryWhiteRadiance(config);
        Equal("1", config.Vars[SunExpIds.TempWhiteRadiance], "Temporary white radiance marker is set");
        Equal("0", config.Vars[SunExpIds.TempWhiteRadianceResolved], "Temporary white radiance starts unresolved");
        True(CardMutationService.HasSpecialTag(config, SunExpIds.WhiteRadianceTag), "Temporary white radiance adds the white-radiance SpecialTag");
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
        True(CardConfigApi.HasNativeWhiteRadiance(native), "Native white radiance is read from data.Tag");

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
        selectedFromCareer.ReplaceStones(new[] { "B", "W", "B" });

        var readFromSkill = LoneerCombatStateStore.GetOrCreate(owner)!;
        True(ReferenceEquals(selectedFromCareer, readFromSkill), "Loneer state is shared across executors for the same owner");
        Equal("selected-guide", readFromSkill.GuidanceCardId, "Guidance survives executor changes");
        Equal("B", readFromSkill.DrawStone(), "Stone draws use the shared owner bag");
        Equal(1, readFromSkill.BlackStoneCount("B"), "Shared stone bag advances exactly once");

        var isolated = LoneerCombatStateStore.GetOrCreate(other)!;
        Equal("", isolated.GuidanceCardId, "Different owners receive isolated guidance state");
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
        score.Record("C", 3);

        Equal(3, score.Notes.Count, "Star score keeps a three-card sliding window");
        Equal("U", score.Notes[0], "Star score drops the oldest note");
        True(ReferenceEquals(score, StarScoreCombatStateStore.GetOrCreate(owner)), "Star score is shared across card executors for the same owner");
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
    $cardMutationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardMutationService.cs"))
    $starBlessingCostOverrideStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarBlessingCostOverrideStore.cs"))
    $cardGrantRecipes = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\CardGrantRecipes.cs"))
    $specialTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SpecialTagRuntime.cs"))
    $cardConfigApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"))
    $gameCompatibilityApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\GameCompatibilityApi.cs"))
    $cardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\CardScripts.cs"))
    $buffScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BuffScripts.cs"))
    $buffApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BuffApi.cs"))
    $scriptEventApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\ScriptEventApi.cs"))
    $fieldApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\FieldApi.cs"))
    $buffOverflowApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BuffOverflowApi.cs"))
    $eventScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\EventScripts.cs"))
    $bossScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BossScripts.cs"))
    $entry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Entry.cs"))
    $wunaScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\WunaScripts.cs"))
    $runtimeHooks = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\RuntimeHooks.cs"))
    $duskPartnerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\DuskPartnerRuntime.cs"))
    $starClayDollRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\StarClayDollRuntime.cs"))
    $loneerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\LoneerRuntime.cs"))
    $starScoreRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\StarScoreRuntime.cs"))
    $loneerService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerMiracleService.cs"))
    $loneerState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerCombatState.cs"))
    $starScoreService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreService.cs"))
    $starScoreState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreCombatState.cs"))
    $duskPartnerScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\DuskPartnerScripts.cs"))
    $starClayDollScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\StarClayDollScripts.cs"))
    $scriptingSource = [string]::Join("`n", (Get-ChildItem -LiteralPath (Join-Path $RepoRoot "SunExp-Dev\Scripting") -File -Filter "*.cs" | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))
    $solarEventRuntimePath = Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarEventRuntime.cs"
    $solarMemoryModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"))
    $modeChoiceEntryDefinition = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ModeChoiceEntryDefinition.cs"))
    $modeChoiceEntryRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ModeChoiceEntryRegistry.cs"))
    $modeChoiceLayoutRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\ModeChoiceLayoutRuntime.cs"))
    $solarMemoryRunLauncher = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs"))
    $solarMemoryContentIsolationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryContentIsolationRuntime.cs"))
    $solarMemoryMapItemAnimationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryMapItemAnimationRuntime.cs"))
    $mapNodeCardArtRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs"))
    $mapNodeCardArtRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs"))
    $mapItemApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\MapItemApi.cs"))
    $mapNodeTextureFitService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs"))
    $sunExpHardTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpHardTagRuntime.cs"))
    $solarMemoryStarterDeckRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"))
    $solarMemorySetupFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"))
    $solarMemoryBlessingPickerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"))
    $solarMemoryPreparationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPreparationRuntime.cs"))
    $solarMemoryPlayerSetupState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPlayerSetupState.cs"))
    $solarMemoryFlowApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\SolarMemoryFlowApi.cs"))
    $solarMemoryRoleCommitApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\SolarMemoryRoleCommitApi.cs"))
    $solarMemoryRoleCommit = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Network\RpcSolarMemoryRoleCommit.cs"))
    $sunExpUiSafety = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiSafety.cs"))
    $sunExpUiBuilder = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\Ui\SunExpUiBuilder.cs"))
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
    $keywordText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\KeyWordsDic\sunexp.csv"))
    $eventData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\EventList\sunexp.csv"))
    $eventText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\EventList\sunexp.csv"))
    $blessingData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Blessing\sunexp.csv"))
    $partnerData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Partner\sunexp.csv"))
    $cardDataPath = Join-Path $RepoRoot "SunExp\Data\Card\sunexp.csv"
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
    Assert-True $runtimeHooks.Contains('RegisterBefore(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffBefore);') "Burn overflow must hook StatusManager.AddBuff so the real target is known."
    Assert-True $runtimeHooks.Contains('RegisterAfter(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffAfter);') "Solar Radiance cap repair must hook StatusManager.AddBuff after creation so first gains above 12 can be restored for Wuna."
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
    Assert-True $wunaScripts.Contains("PlayerApi.GetScopedGameVar(SunExpIds.WunaPersistentEmber") "Wuna persistent ember must read from a player-scoped GameVar."
    Assert-True $wunaScripts.Contains("PlayerApi.SetScopedGameVar(SunExpIds.WunaPersistentEmber") "Wuna persistent ember must write to a player-scoped GameVar."
    Assert-True $buffApi.Contains("PlayerApi.SetScopedGameVar(SunExpIds.WunaPersistentEmber, status") "BuffApi.SavePersistentEmber must write to a player-scoped GameVar."
    Assert-True $buffApi.Contains("return string.IsNullOrWhiteSpace(careerId)") "Wuna active fallback must not override an explicit non-Wuna career."
    Assert-True (-not [regex]::IsMatch($buffApi + $wunaScripts, "SetGameVar\s*\(\s*SunExpIds\.WunaPersistentEmber")) "Persistent Ember must not write to the legacy unscoped GameVar."
    Assert-True $cardScripts.Contains('["draw_flame"] = InitDrawFlame') "draw_flame must be registered for initialization."
    Assert-True ([regex]::IsMatch($cardScripts, 'private\s+static\s+void\s+InitDrawFlame[\s\S]*?ExecutorApi\.SetBaseScript\(self,\s+"AttackCardItem"\);')) "draw_flame must allow self-targeting during initialization."
    Assert-True $cardScripts.Contains("var target = ExecutorApi.PrimaryTargetIncludingSelf(self);") "draw_flame must resolve targets without excluding self."
    Assert-True $cardScripts.Contains("ExecutorApi.TriggerBurnAllEnemies(self, times * 2);") "flamewheel_recurrence must trigger enemy burn 2*N times while keeping N as the cost."
    Assert-True $cardScripts.Contains("ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, level, ""Target"");") "eclipse_hex must add current Burn stacks instead of directly setting a capped level."
    Assert-True $buffScripts.Contains("return StatusApi.MaxHp(target) / 100 + 1;") "body_burn must deal 1% max HP + 1 true damage per stack."
    Assert-True (-not $specialTagRuntime.Contains("CardConfigApi.BaseCost")) "White radiance should use current actual play cost, not BaseCost."
    Assert-True $cardConfigApi.Contains("ReadPlayerCardCostMultiplier") "CardConfigApi must read the player CardCost multiplier."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.EnsureInCurrentLayer")) "RuntimeHooks must not inject SunExp events into normal adventure maps."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.RepairMapSelection")) "RuntimeHooks must not repair normal adventure map selections for SunExp events."
    Assert-True (-not [System.IO.File]::Exists($solarEventRuntimePath)) "The retired normal-mode solar event injector file must be removed."
    Assert-True $runtimeHooks.Contains("SolarMemoryContentIsolationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize the Solar Memory content isolation guard."
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
    Assert-True $runtimeHooks.Contains("LoneerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Loneer's card-action runtime."
    Assert-True $runtimeHooks.Contains("SolarMemoryMapItemAnimationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize solar memory map-item animation fallback hooks."
    Assert-True $runtimeHooks.Contains("MapNodeCardArtRuntime.Initialize(modConfig)") "RuntimeHooks must initialize generic map-node card art hooks after animation fallback hooks."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);') "Solar memory map items must patch fixed boss animation paths before native MapItem.Init loads Texture2D frames."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);') "Solar memory map item animation fallback must restore enemy animation paths after native MapItem.Init."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("SunExpIds.SolarBossSecondSunLevelId") "Solar memory map item fallback must cover the second-sun boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("SunExpIds.SolarBossSaintWunaLevelId") "Solar memory map item fallback must cover the saint Wuna boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('row["Animation"] = fallbackAnimation') "Solar memory map item fallback must temporarily replace the enemy Animation row."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('restore.Row["Animation"] = restore.Animation') "Solar memory map item fallback must restore the original enemy Animation row."
    Assert-True (-not $solarMemoryMapItemAnimationRuntime.Contains("ApplyFixedBossMapTexture")) "Solar memory animation fallback must not own map-node texture replacement."
    Assert-True $mapNodeCardArtRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", CaptureMapItemBaseline);') "Map-node art runtime must capture icon baseline before native MapItem.Init mutates transform."
    Assert-True $mapNodeCardArtRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", ApplyMapNodeCardArt);') "Map-node art runtime must apply configured art after native MapItem.Init."
    Assert-True $mapNodeCardArtRuntime.Contains("ResourceLoader.Load<Texture>(spec.TexturePath, true)") "Map-node art runtime must load textures through the mod-aware ResourceLoader path."
    Assert-True $mapNodeCardArtRegistry.Contains("SunExpIds.SolarBossSecondSunMapTexturePath") "Map-node art registry must cover the second-sun boss map texture."
    Assert-True $mapNodeCardArtRegistry.Contains("SunExpIds.SolarBossSaintWunaMapTexturePath") "Map-node art registry must cover the saint Wuna boss map texture."
    Assert-True $mapNodeCardArtRegistry.Contains("MapNodeCardArtFitMode.ContainTrimmed") "Fixed boss map-node art must use transparent-edge contain fitting."
    Assert-True $mapItemApi.Contains("TextureTransparencyAnalyzer.AnalyzeAllEdges") "MapItemApi must analyze transparent edges before applying fitted map-node textures."
    Assert-True $mapItemApi.Contains("MapNodeTextureFitService.Fit") "MapItemApi must delegate map-node texture geometry to the fit service."
    Assert-True $mapNodeTextureFitService.Contains("DefaultFightBoundsWidth = 160f") "Map-node texture fit service must preserve native fight-node width."
    Assert-True $mapNodeTextureFitService.Contains("DefaultFightBoundsHeight = 238f") "Map-node texture fit service must preserve native fight-node height."
    Assert-True $duskPartnerRuntime.Contains('"GameEntryUI.CheckCareer"') "Dusk runtime must clean its placeholder blessing after career checks."
    Assert-True $duskPartnerRuntime.Contains('"Fight_Start.Init"') "Dusk runtime must grant its trait at fight start."
    Assert-True $duskPartnerRuntime.Contains("status.AddBuff(SunExpIds.DuskAfterheatRecoveryTrait, 1)") "Dusk runtime must grant the afterheat recovery trait buff."
    Assert-True (-not $duskPartnerRuntime.Contains("StarClay")) "Dusk runtime must not own Star Clay Doll behavior."
    Assert-True $starClayDollRuntime.Contains("status.AddBuff(SunExpIds.StarClayDollTrait, 1)") "Star Clay Doll runtime must grant its own trait."
    Assert-True $starClayDollRuntime.Contains('"StatusManager.Hit"') "Star Clay Doll runtime must own lethal-hit protection."
    Assert-True (-not $starScoreRuntime.Contains("LoneerMiracleService")) "Generic star score runtime must not dispatch Loneer role behavior."
    Assert-True (-not $starScoreRuntime.Contains("StarClay")) "Generic star score runtime must not own partner behavior."
    Assert-True $starScoreRuntime.Contains('"CommonCardItem.OnBeginDrag"') "Star Blessing must preview zero cost when a common card begins dragging."
    Assert-True $starScoreRuntime.Contains('"AttackCardItem.OnPointerDown"') "Star Blessing must preview zero cost when an attack card enters target selection."
    Assert-True $starScoreRuntime.Contains('"AttackCardItem.CancelLineMode"') "Star Blessing must roll back when attack-card targeting is cancelled."
    Assert-True $starScoreRuntime.Contains('"CardItem.CancelUseDrag"') "Star Blessing must roll back when a card drag is cancelled."
    Assert-True $starScoreRuntime.Contains('RegisterAfter(modConfig, "CommonCardItem.TrueUse", OnCardUseAfter);') "Star Blessing must finalize common-card cost state after use."
    Assert-True $starScoreRuntime.Contains('RegisterAfter(modConfig, "AttackCardItem.TrueUse", OnCardUseAfter);') "Star Blessing must finalize attack-card cost state after use."
    Assert-True $starScoreRuntime.Contains("RefundBlessing();") "A rejected card use must refund the consumed Star Blessing."
    Assert-True $starBlessingCostOverrideStore.Contains('DictionaryUtil.Set(config.Vars, "OnceExCost", entry.OriginalOnceCost.ToString())') "Cancelling Star Blessing must restore the exact original one-use cost."
    Assert-True $starBlessingCostOverrideStore.Contains('DictionaryUtil.Set(config.Vars, "OnceExCost", "0")') "Successful Star Blessing use must clear one-use cost state."
    Assert-True $loneerRuntime.Contains("LoneerMiracleService.OnCardActionAfter") "Loneer runtime must own non-derived card action dispatch."
    Assert-True $loneerState.Contains("Dictionary<string, LoneerCombatState>") "Loneer combat state must be keyed by owner status instead of ScriptExecutor.Vars."
    Assert-True $loneerService.Contains("LoneerCombatStateStore.GetOrCreate(self.Self)") "Loneer skill and action flows must resolve owner-scoped combat state."
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
    Assert-True (-not $cardApi.Contains("previousCount")) "Generated-card success must not depend on draw-pile net count."
    Assert-True (-not $cardApi.Contains("could not verify added card")) "The inverted draw-pile count verifier must remain removed."
    Assert-True $loneerService.Contains("SetMorningPrayerCooldown(self, state, PrayerCooldownRounds);") "Morning Star Prayer must commit its cooldown after a successful copy."
    Assert-True $loneerService.Contains("self?.UpdateSkillTime();") "Morning Star Prayer cooldown changes must refresh the skill UI."
    $loneerActionFlow = [regex]::Match($loneerService, "public\s+static\s+void\s+OnCardActionAfter[\s\S]*?public\s+static\s+void\s+UseMorningStarPrayer")
    Assert-True $loneerActionFlow.Success "Could not locate Loneer action flow for source assertion."
    Assert-True (-not $loneerActionFlow.Value.Contains("IsExcludedActionCard(config)")) "Every player card action, including generated and Stellar Overture cards, must draw a Star Stone."
    $naturalMorningStar = [regex]::Match($loneerService, "private\s+static\s+void\s+TriggerNaturalMorningStar[\s\S]*?private\s+static\s+void\s+TriggerBorrowedMiracle")
    Assert-True $naturalMorningStar.Success "Could not locate Natural Morning Star for source assertion."
    Assert-True (-not $naturalMorningStar.Value.Contains("AddStarlight")) "Natural Morning Star must not grant Starlight directly."
    Assert-True $naturalMorningStar.Value.Contains("RequestGuidanceSelection") "Natural Morning Star must reselect Guidance after copying it."
    $stoneDraw = [regex]::Match($loneerService, "private\s+static\s+void\s+DrawStone[\s\S]*?private\s+static\s+void\s+ReduceBlackStoneMax")
    Assert-True $stoneDraw.Success "Could not locate Loneer stone draw flow for source assertion."
    Assert-True $stoneDraw.Value.Contains("var whiteStarlight = state.BlackStoneCount(BlackStone);") "A white stone must count the black stones currently remaining in the pouch."
    Assert-True $stoneDraw.Value.Contains("StarScoreService.AddStarlight(self, whiteStarlight);") "A white stone must grant Starlight equal to the current black-stone count."
    Assert-True $stoneDraw.Value.Contains("StarScoreService.AddStarlight(self, 1);") "A black stone must grant exactly 1 Starlight."
    $borrowedMiracle = [regex]::Match($loneerService, "private\s+static\s+void\s+TriggerBorrowedMiracle[\s\S]*?private\s+static\s+void\s+ReduceClock")
    Assert-True $borrowedMiracle.Success "Could not locate Borrowed Miracle for source assertion."
    Assert-True $borrowedMiracle.Value.Contains("ResetPouchAndClock(self, state, grantStarlight: true);") "Restoring the Miracle Clock must grant Starlight equal to its cap."
    Assert-True $borrowedMiracle.Value.Contains("RequestGuidanceSelection") "Borrowed Miracle must reselect Guidance after copying it."
    Assert-True $loneerCareerText.Contains("After each action, draw a Star Stone from the Star Stone Pouch.") "Loneer career text must describe the every-action Star Stone draw."
    Assert-True $buffText.Contains("When the Miracle Clock is restored to its cap, gain {SunExp_sunexp_starlight} equal to that cap.") "Miracle Clock text must describe its Starlight restoration reward."
    Assert-True $buffText.Contains("When you draw a black stone, gain 1 {SunExp_sunexp_starlight}.") "Star Stone Pouch text must describe black-stone Starlight gain."
    Assert-True $buffText.Contains("equal to the current number of black stones.") "Star Stone Pouch text must describe white-stone Starlight gain."
    Assert-True $keywordText.Contains('"Natural Morning Star"') "Natural Morning Star keyword localization is missing."
    Assert-True $keywordText.Contains('"Borrowed Miracle"') "Borrowed Miracle keyword localization is missing."
    Assert-True $cardScripts.Contains('id = NormalizeId(id);') "Card script entry points must normalize generated-card ids."
    Assert-True (-not [regex]::IsMatch($cardScripts, 'case\s+"\*')) "Card script switches must use normalized, unstarred ids."
    Assert-True $cardScripts.Contains("IsStarScoreEntry(id)") "CardScripts must route Stellar Overture cards through the shared StarScore entry predicate."
    foreach ($stellarId in @("stellar_overture_start", "stellar_overture_sustain", "stellar_overture_turn", "stellar_overture_close")) {
        Assert-True $starScoreService.Contains('value == "' + $stellarId + '"') ("StarScoreService must dispatch " + $stellarId + ".")
    }
    $stellarRows = Import-Csv -LiteralPath $cardDataPath | Where-Object { $_.Id -like "*stellar_overture_*" }
    Assert-True ($stellarRows.Count -eq 4) "SunExp must define all four Stellar Overture cards."
    foreach ($row in $stellarRows) {
        Assert-True ($row.InitScript -match 'CardScripts\.Init\(self, "([^"]+)"\)') ("Missing CardScripts.Init dispatch for " + $row.Id)
        $initId = $Matches[1].Replace("*", "").Trim()
        Assert-True $starScoreService.Contains('value == "' + $initId + '"') ("Normalized Init id is not dispatched: " + $row.Id)
        Assert-True ($row.UseScript -match 'CardScripts\.Use\(self, "([^"]+)"\)') ("Missing CardScripts.Use dispatch for " + $row.Id)
        $useId = $Matches[1].Replace("*", "").Trim()
        Assert-True $starScoreService.Contains('value == "' + $useId + '"') ("Normalized Use id is not dispatched: " + $row.Id)
    }
    Assert-True (-not $loneerService.Contains("SunExpIds.LoneerGuidanceCardId")) "Loneer guidance must not be stored in per-executor Vars."
    Assert-True $starScoreService.Contains("StarScoreCombatStateStore.GetOrCreate(self.Self)") "Star score notes must be owner-scoped across card executors."
    Assert-True $starScoreState.Contains("while (notes.Count > Math.Max(1, windowSize))") "Star score must maintain a bounded sliding window."
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
    Assert-True $sunExpUiSafety.Contains("UiRaycastSafeDestroyRuntime.DisableRaycasts") "Solar memory UI cleanup must reuse the shared raycast-safe destroy runtime."
    Assert-True $sunExpUiSafety.Contains("Object.Destroy(root)") "Solar memory UI cleanup must destroy only after disabling raycasts."
    Assert-True $sunExpUiBuilder.Contains("public static Image ApplyPanelImage") "SunExp local UI builder must expose reusable panel image creation."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Solar memory starter deck UI must reuse SunExpUiBuilder panel creation."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Solar memory blessing picker UI must reuse SunExpUiBuilder panel creation."
    Assert-True $solarMemorySetupFlowRuntime.Contains("SunExpUiBuilder.ApplyPanelImage") "Solar memory setup flow UI must reuse SunExpUiBuilder panel creation."
    Assert-True $solarMemoryModeRuntime.Contains("CompleteSolarMemoryRun") "Solar memory must settle immediately after the third layer boss."
    Assert-True $solarMemoryModeRuntime.Contains("manager.Level = levelForNativeFlow") "Solar memory completion must route through the native settlement level."
    Assert-True (-not $eventScripts.Contains("InitSolarFinale")) "Retired solar finale EventList entries must not leave script entry points behind."
    Assert-True (-not $eventScripts.Contains("FinishSolarFinaleEnding")) "Retired solar finale ending must not be opened through EventScripts."
    Assert-True (-not $sunExpIds.Contains("SolarFinaleFullEndingEventId")) "Retired solar finale ending event id must not remain in SunExpIds."
    Assert-True $solarMemoryModeRuntime.Contains("RepairSolarMemoryMapSelection") "Solar memory must repair synced map arrays for its fixed first node."
    Assert-True $solarMemoryModeRuntime.Contains("Mods/SunExp/ModResource/Images/UI/solar_memory_title_c.png") "Solar memory mode entry must load its cropped normal title sprite."
    Assert-True $solarMemoryModeRuntime.Contains("Mods/SunExp/ModResource/Images/UI/solar_memory_title_c_h.png") "Solar memory mode entry must load its cropped highlighted title sprite."
    Assert-True $solarMemoryModeRuntime.Contains('var normalTitle = entry.Find("Normal/Title")') "Solar memory mode entry must locate the native normal title image."
    Assert-True $solarMemoryModeRuntime.Contains('var highlightedTitle = entry.Find("HighLighted/Title")') "Solar memory mode entry must locate the native highlighted title image."
    Assert-True $solarMemoryModeRuntime.Contains("SetImageSprite(normalTitle, normalSprite)") "Solar memory mode entry must replace the native normal title image."
    Assert-True $solarMemoryModeRuntime.Contains("SetImageSprite(highlightedTitle, highlightedSprite)") "Solar memory mode entry must replace the native highlighted title image."
    Assert-True $solarMemoryModeRuntime.Contains('title.gameObject.SetActive(false);') "Solar memory mode entry must hide the fallback text title when sprites load."
    Assert-True $solarMemoryModeRuntime.Contains("ConfigureEntryUnlocked(entry.transform)") "Solar memory mode entry must clear lock state inherited from the cloned native mode."
    Assert-True $solarMemoryModeRuntime.Contains('string.Equals(child.name, "Lock"') "Solar memory mode entry must hide cloned Lock objects."
    Assert-True $solarMemoryModeRuntime.Contains("TrimTransparentPadding(sprite)") "Solar memory mode entry must trim transparent padding from configured title art."
    Assert-True $solarMemoryModeRuntime.Contains("CropEntryTitleArt(trimmed)") "Solar memory mode entry must crop full-card art into the native Title slot."
    Assert-True $solarMemoryModeRuntime.Contains("EntryTitleArtHeightRatio") "Solar memory mode entry must keep the title-slot crop ratio explicit."
    Assert-True $solarMemoryModeRuntime.Contains("ClearEntryStateImages") "Solar memory mode entry must clear native mode art layers before applying custom art."
    Assert-True $solarMemoryModeRuntime.Contains("stateRoot.GetComponentsInChildren<Image>(true)") "Solar memory mode entry must disable cloned Image layers."
    Assert-True $solarMemoryModeRuntime.Contains("stateRoot.GetComponentsInChildren<RawImage>(true)") "Solar memory mode entry must disable cloned RawImage layers."
    Assert-True $solarMemoryModeRuntime.Contains("ConfigureEntryHoverState(entry)") "Solar memory mode entry must isolate inherited hover animation state."
    Assert-True $solarMemoryModeRuntime.Contains("switchButton.isAnimated = false") "Solar memory mode entry must use immediate SwitchButton state changes."
    Assert-True $solarMemoryModeRuntime.Contains("component.StopAllCoroutines()") "Solar memory mode entry must stop inherited ButtonManager hover transitions."
    Assert-True $solarMemoryModeRuntime.Contains("component.enabled = false") "Solar memory mode entry must disable duplicate ButtonManager hover controllers."
    Assert-True $solarMemoryModeRuntime.Contains('entry.Find("Pressed/Title")') "Solar memory mode entry must provide custom art for the pressed state."
    Assert-True $solarMemoryModeRuntime.Contains("switchButton.SetOffImmediate()") "Solar memory mode entry must reset cloned CanvasGroup state deterministically."
    Assert-True $solarMemoryModeRuntime.Contains("RegisterModeChoiceEntry()") "Solar memory mode entry must register itself through the shared mode-choice entry registry."
    Assert-True $solarMemoryModeRuntime.Contains("ModeChoiceLayoutRuntime.Initialize(modConfig)") "Solar memory mode entry must use the shared mode-choice layout runtime."
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
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryTitle") "Solar memory mode entry must provide its display name to fallback UI."
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
    Assert-True $solarMemoryModeRuntime.Contains('"MapSelectUI.DataUpdate", ApplySolarMemoryLayerTitle') "Solar memory must override map layer titles in MapSelectUI."
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
    Assert-True $solarMemoryMapNodePoolFactory.Contains("NodeDice = tree.treedice ?? Dice.Default") "Solar memory generated boss nodes must have deterministic NodeDice fallback."
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
    Assert-True $bossScripts.Contains("MercilessDaylightBodyBurn = 5") "Second Sun failed name burn must apply 5 Body Burn."
    Assert-True (-not $bossScripts.Contains("MercilessDaylightFlame")) "Second Sun trait must no longer grant gathered flame after burning names."
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
    $sunsetExpedition = [regex]::Match($sunExpHardTagRuntime, "private\s+static\s+void\s+ApplySunsetExpedition\(\)[\s\S]*?private\s+static\s+void\s+ApplyWhiteRadianceCourtCards")
    Assert-True $sunsetExpedition.Success "Could not locate ApplySunsetExpedition for source assertion."
    Assert-True (-not $sunsetExpedition.Value.Contains("MirrorSc")) "Sunset Expedition must not borrow the player's generic MirrorSc executor."
    Assert-True (-not $sunsetExpedition.Value.Contains("ChangeHp")) "Sunset Expedition must not call ChangeHp without a dataConfig Id."
    Assert-True $sunsetExpedition.Value.Contains("status.CurHp = nextHp") "Sunset Expedition must apply HP loss through the synchronized status property."
    Assert-True $sunsetExpedition.Value.Contains("if (IsServerAuthority())") "Only the host may advance the shared Sunset Expedition fight count."
    Assert-True $sunExpHardTagRuntime.Contains('RunFightStartStep("BlackSunListener"') "A Sunset Expedition failure must not prevent Black Sun listener registration."
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryMapIds[eventIndex]") "Solar memory sync repair must use the fixed story map id array."
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryFullEventIds[eventIndex]") "Solar memory sync repair must use the fixed story event id array."
    Assert-True $eventScripts.Contains("public static void InitSolarMemoryNode") "Solar memory fixed story events must expose an init method."
    Assert-True $eventScripts.Contains("public static void ContinueSolarMemory") "Solar memory fixed story events must expose a continue method."
    Assert-True (-not $eventScripts.Contains("SunExp.Dll.Hooks")) "Solar memory event scripts must not import Hooks directly."
    Assert-True (-not [regex]::IsMatch($eventScripts, "SolarMemory(?:ModeRuntime|PreparationRuntime|PlayerSetupState)")) "Solar memory event scripts must call the GameApi flow facade instead of Hook runtimes."
    Assert-True $eventScripts.Contains("SolarMemoryFlowApi.IsPreparationComplete()") "Solar memory event scripts must gate preparation through SolarMemoryFlowApi."
    Assert-True $eventScripts.Contains("SolarMemoryFlowApi.StartOrResumePreparation()") "Solar memory event scripts must start preparation through SolarMemoryFlowApi."
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
    Assert-True $solarMemoryRunLauncher.Contains('saveInfo.GameVars[SunExpIds.SolarMemoryOriginPointsKey] = "50"') "Solar memory must initialize origin setup with 50 points."
    Assert-True $sunExpIds.Contains("SolarMemoryPrepStepKey") "Solar memory preparation must persist an explicit preparation step."
    Assert-True $solarMemoryRunLauncher.Contains("SolarMemoryPrepStep.DeckSelection") "Solar memory saves must initialize the preparation state machine."
    Assert-True $solarMemoryPreparationRuntime.Contains("public static void StartOrResume") "Solar memory preparation runtime must expose a stable start/resume entry point."
    Assert-True $solarMemoryPreparationRuntime.Contains("InferStepFromLegacyState") "Solar memory preparation runtime must infer state from old boolean keys."
    $solarMemorySetupSources = $solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime + $solarMemoryPreparationRuntime + $solarMemoryModeRuntime + $solarMemoryRunLauncher
    Assert-True (-not $solarMemorySetupSources.Contains("StarterDeckArbiterRuntime.SyncRoleTable")) "Solar memory preparation must not use the native RoleTable collector before final setup completion."
    Assert-True ([regex]::IsMatch($solarMemoryStarterDeckRuntime, 'ApplyDeck\([\s\S]*?sync:\s*false\)')) "Solar memory custom starter deck must suppress intermediate role synchronization."
    Assert-True ([regex]::IsMatch($solarMemoryStarterDeckRuntime, 'KeepOfficialDeck\(roleTable,\s*CreateClaim\(mode\),\s*sync:\s*false\)')) "Solar memory official starter deck path must suppress intermediate role synchronization."
    Assert-True $solarMemoryPreparationRuntime.Contains('SolarMemoryRoleCommitApi.CommitFinal(RoleTable.Instance, "SunExp.SolarMemory.SetupFinished")') "Solar memory preparation completion must submit the final role commit."
    Assert-True (-not $solarMemorySetupFlowRuntime.Contains("FinishSetup()")) "Solar memory setup flow must not retain an unreachable competing completion path."
    Assert-True $solarMemoryRoleCommitApi.Contains("SendRpcCommand(new RpcSolarMemoryRoleCommit") "Solar memory clients must submit the final role through a dedicated RPC command."
    Assert-True (-not $solarMemoryRoleCommit.Contains("CmdSyncRoleTable")) "Solar memory final role commit must not call the native role collector."
    Assert-True (-not $solarMemoryRoleCommit.Contains("ReceiveRoleTable")) "Solar memory final role commit must not increment GameServer.roleCount."
    Assert-True $solarMemoryRoleCommit.Contains("server.RoleTables[role.Id] = role") "Solar memory final role commit must update the authoritative role dictionary."
    Assert-True $solarMemoryRoleCommit.Contains("GameSaveManager.UpdateRoles(role)") "Solar memory final role commit must persist the authoritative role."
    Assert-True $solarMemoryRoleCommit.Contains("SolarMemorySetupFinishedKey") "Solar memory final role commit must reject unfinished preparation state."
    Assert-True $solarMemoryRoleCommitApi.Contains("SolarMemorySetupCommitTokenKey") "Solar memory final role submission must suppress local re-entry with a per-run token."
    Assert-True $solarMemoryRoleCommit.Contains("CommittedTokens.Add(commitToken)") "Solar memory final role command must suppress duplicate network delivery."
    Assert-True ($modConfig.ModVersion -eq "0.4.1") "SunExp network protocol change must ship as version 0.4.1."
    Assert-True ($modConfig.MustSame -eq $true) "SunExp must require an identical multiplayer mod version."
    Assert-True $audioArbiterRuntime.Contains('CurrentBuildId = "audio-arbiter-2026-06-23-v5"') "Audio arbiter must expose the owner-qualified provider runtime build id."
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
    Assert-True (@($exclusiveMapRows | Where-Object { $_ -notmatch ',7$' }).Count -eq 0) "Every Solar Memory map, event, and boss row must use Rarity 7."
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
