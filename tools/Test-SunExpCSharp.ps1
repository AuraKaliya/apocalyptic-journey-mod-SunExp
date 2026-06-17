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
    $sunExpIds = Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpIds.cs"
    $cardConfigApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"

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
    <Compile Include="$dictionaryUtil" />
    <Compile Include="$sunExpIds" />
    <Compile Include="$cardConfigApi" />
    <Compile Include="$SourceDir\Tests.cs" />
  </ItemGroup>
</Project>
"@
}

function New-StubsSource {
@'
using System.Collections.Generic;

public sealed class FightPlayer
{
    public static FightPlayer Instance { get; } = new();

    public FakeStatus Status { get; } = new();
}

public sealed class FakeStatus
{
    public Dictionary<string, float> dynamicVariables { get; } = new();
}

public enum DataType
{
    Card,
    Buff
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
'@
}

function New-TestsSource {
@'
using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

internal static class Program
{
    private static int assertions;
    private const string WhiteRadiance = "\u767d\u66dc";

    private static void Main()
    {
        TestDictionaryUtil();
        TestCardCostHelpers();
        TestSolarTriggerCostOverride();
        TestWhiteRadianceTags();
        TestTemporaryWhiteRadianceClaim();

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

    $executorApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\ExecutorApi.cs"))
    $sunExpIds = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpIds.cs"))
    $playerApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\PlayerApi.cs"))
    $specialTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SpecialTagRuntime.cs"))
    $cardConfigApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"))
    $cardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\CardScripts.cs"))
    $buffScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BuffScripts.cs"))
    $buffApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BuffApi.cs"))
    $eventScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\EventScripts.cs"))
    $bossScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BossScripts.cs"))
    $entry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Entry.cs"))
    $wunaScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\WunaScripts.cs"))
    $runtimeHooks = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\RuntimeHooks.cs"))
    $duskPartnerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\DuskPartnerRuntime.cs"))
    $solarEventRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarEventRuntime.cs"))
    $solarMemoryModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"))
    $solarMemoryStarterDeckRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"))
    $solarMemorySetupFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"))
    $solarMemoryBlessingPickerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"))
    $solarMemoryPreparationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPreparationRuntime.cs"))
    $solarMemoryMapNodePoolFactory = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolFactory.cs"))
    $solarMemoryMapNodePoolApplier = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolApplier.cs"))
    $mapData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Map\sunexp.csv"))
    $mapText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Map\sunexp.csv"))
    $levelData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Level\sunexp.csv"))
    $enemyData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Enemy\sunexp.csv"))
    $enemyText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Enemy\sunexp.csv"))
    $enemyCardText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\EnemyCard\sunexp.csv"))
    $buffData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Buff\sunexp.csv"))
    $buffText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Buff\sunexp.csv"))
    $keywordText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\KeyWordsDic\sunexp.csv"))
    $eventData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\EventList\sunexp.csv"))
    $eventText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\EventList\sunexp.csv"))
    $blessingData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Blessing\sunexp.csv"))
    $partnerData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Partner\sunexp.csv"))

    $addStatusBuff = [regex]::Match($executorApi, "public\s+static\s+bool\s+AddStatusBuff[\s\S]*?public\s+static\s+bool\s+RemoveStatusBuff")
    Assert-True $addStatusBuff.Success "Could not locate ExecutorApi.AddStatusBuff for source assertion."
    Assert-True (-not $addStatusBuff.Value.Contains("HandleBurnOverflow")) "AddStatusBuff must not call HandleBurnOverflow; burn overflow is handled by the ScriptExecutor.AddBuff hook."
    $tryAddEvent = [regex]::Match($executorApi, "public\s+static\s+bool\s+TryAddEvent[\s\S]*?public\s+static\s+void\s+SetBaseScript")
    Assert-True $tryAddEvent.Success "Could not locate ExecutorApi.TryAddEvent for source assertion."
    Assert-True $tryAddEvent.Value.Contains("executor.Self == null") "TryAddEvent must skip preview/dictionary executors without Self before calling AddEvent."
    Assert-True $tryAddEvent.Value.Contains("catch (Exception ex)") "TryAddEvent must catch all event registration failures and degrade safely."
    Assert-True $executorApi.Contains("public static bool ClearFieldBuff") "ExecutorApi.ClearFieldBuff is missing."
    Assert-True $executorApi.Contains("public static int BurnUpperBound(IStatusManager? target)") "ExecutorApi must expose a dynamic burn upper bound helper."
    Assert-True $executorApi.Contains("private const int BurnUpperBoundFallback = 1;") "Invalid burn upper bounds must fall back to the minimum valid stack count."
    Assert-True $executorApi.Contains("target.GetBuff(buffId)?.buffConfig?.UpperBound") "Burn upper bound must prefer the live BuffItemConfig.UpperBound."
    Assert-True $executorApi.Contains('GetOne(DataType.Buff, buffId)') "Burn upper bound must fall back to the current Buff data row."
    Assert-True $executorApi.Contains("var upperBound = BurnUpperBound(target);") "Burn overflow must use the dynamic burn upper bound."
    Assert-True $executorApi.Contains('DictionaryUtil.Set(executor?.Vars, "CanSelf", canSelf ? "True" : "False");') "SetBaseScript must explicitly write CanSelf for self-targetable attack cards."
    Assert-True $executorApi.Contains("public static IStatusManager? PrimaryTargetIncludingSelf") "ExecutorApi.PrimaryTargetIncludingSelf is missing."
    Assert-True $playerApi.Contains("public static string ScopedGameVarKey") "PlayerApi.ScopedGameVarKey is missing."
    Assert-True $wunaScripts.Contains("PlayerApi.GetScopedGameVar(SunExpIds.WunaPersistentEmber") "Wuna persistent ember must read from a player-scoped GameVar."
    Assert-True $wunaScripts.Contains("PlayerApi.SetScopedGameVar(SunExpIds.WunaPersistentEmber") "Wuna persistent ember must write to a player-scoped GameVar."
    Assert-True $buffApi.Contains("PlayerApi.SetScopedGameVar(SunExpIds.WunaPersistentEmber, status") "BuffApi.SavePersistentEmber must write to a player-scoped GameVar."
    Assert-True $buffApi.Contains("return string.IsNullOrWhiteSpace(careerId)") "Wuna active fallback must not override an explicit non-Wuna career."
    Assert-True (-not [regex]::IsMatch($buffApi + $wunaScripts, "SetGameVar\s*\(\s*SunExpIds\.WunaPersistentEmber")) "Persistent Ember must not write to the legacy unscoped GameVar."
    Assert-True ([regex]::IsMatch($cardScripts, 'case\s+"draw_flame":\s+ExecutorApi\.SetBaseScript\(self,\s+"AttackCardItem"\);\s+break;')) "draw_flame must allow self-targeting during initialization."
    Assert-True $cardScripts.Contains("var target = ExecutorApi.PrimaryTargetIncludingSelf(self);") "draw_flame must resolve targets without excluding self."
    Assert-True $cardScripts.Contains("ExecutorApi.TriggerBurnAllEnemies(self, times * 2);") "flamewheel_recurrence must trigger enemy burn 2*N times while keeping N as the cost."
    Assert-True $cardScripts.Contains("ExecutorApi.BurnUpperBound(target)") "eclipse_hex must use the current burn upper bound instead of a hard-coded cap."
    Assert-True $buffScripts.Contains("return maxHp / 100 + 1;") "body_burn must deal 1% max HP + 1 true damage per stack."
    Assert-True (-not $specialTagRuntime.Contains("CardConfigApi.BaseCost")) "White radiance should use current actual play cost, not BaseCost."
    Assert-True $cardConfigApi.Contains("ReadPlayerCardCostMultiplier") "CardConfigApi must read the player CardCost multiplier."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.EnsureInCurrentLayer")) "RuntimeHooks must not inject SunExp events into normal adventure maps."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.RepairMapSelection")) "RuntimeHooks must not repair normal adventure map selections for SunExp events."
    Assert-True $runtimeHooks.Contains("DuskPartnerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Dusk partner runtime."
    Assert-True $duskPartnerRuntime.Contains('"GameEntryUI.CheckCareer"') "Dusk runtime must clean stale partner blessing placeholders after career checks."
    Assert-True $duskPartnerRuntime.Contains('"Fight_Start.Init"') "Dusk runtime must grant the trait at fight start."
    Assert-True $duskPartnerRuntime.Contains("status.AddBuff(SunExpIds.DuskAfterheatRecoveryTrait, 1)") "Dusk runtime must grant the afterheat recovery trait buff."
    Assert-True $duskPartnerRuntime.Contains("RemoveDuskPlaceholderBlessing") "Dusk runtime must remove stale technical blessing placeholders from role blessings."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^dusk_afterheat_recovery,0,,,Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1,[^,]*,,5\r?$")) "Dusk afterheat recovery must remain a legal zero-weight technical Blessing for GameEntryUI.CheckCareer."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^dusk,10,0,0,0,2,,,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk_choice,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk,Mods/SunExp/ModResource/AnimationLib/Dusk,SunExp_sunexp_dusk_afterheat_recovery,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk\r?$")) "Dusk partner must keep a non-empty Bless column because GameEntryUI.CheckCareer creates a DataConfig from it."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("IsTechnicalBlessing(id)") "Solar memory blessing picker must skip technical partner blessings."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "GameConfigManager.CardPackCheck", FilterSolarMemoryCardPackCheck)') "Solar memory must filter event cards before CardPackCheck builds reward candidates."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState)') "Solar memory must capture event records before base map generation can draw ordinary events."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect)') "Solar memory must normalize SelectNode immediately before map candidate cards are created."
    Assert-True (-not $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapManager.TryChange", RouteSolarFinaleBeforeMapChange)')) "Solar finale must not open EventUI from the generic TryChange hook; that can recurse through event init failure."
    Assert-True (-not $solarMemoryModeRuntime.Contains('ShowEventUIWithTurn<MapSelectUI>("MapSelectUI", SunExpIds.SolarFinaleFullSaintGateEventId)')) "Solar finale must not open the saint gate event from map transition hooks."
    Assert-True (-not $solarMemoryModeRuntime.Contains("EnterSolarFinaleLayer")) "Solar memory must not route into a dedicated finale map layer."
    Assert-True (-not $solarMemoryModeRuntime.Contains("RepairSolarFinaleMapArrays")) "Solar memory must not force finale map candidates into a pre-boss dialogue or saint boss."
    Assert-True $solarMemoryModeRuntime.Contains('"NormalMapManager.MapItemInit", SettleLegacySolarFinaleBeforeMapItems') "Solar memory must settle legacy level-30 saves before native MapItemInit indexes map lists."
    Assert-True $solarMemoryModeRuntime.Contains("CompleteSolarMemoryRun") "Solar memory must settle immediately after the third layer boss."
    Assert-True $solarMemoryModeRuntime.Contains("manager.Level = levelForNativeFlow") "Solar memory completion must route through the native settlement level."
    Assert-True $eventScripts.Contains("PlayerApi.SetGameVar(SunExpIds.SolarFinaleSaintGateOpenedKey, ""shown"")") "Solar finale saint gate init must mark that the event actually displayed."
    Assert-True $eventScripts.Contains("PlayerApi.SetGameVar(SunExpIds.SolarFinaleCompletedKey, ""1"")") "Solar finale ending must mark completion before showing settlement."
    Assert-True $sunExpIds.Contains("SolarFinaleFinalLayerEnteredKey") "Solar finale must define a persisted final-layer state key."
    Assert-True $sunExpIds.Contains("SolarFinaleSaintGateOpenedKey") "Solar finale must define a persisted gate-opened state key."
    Assert-True $sunExpIds.Contains("SolarFinaleCompletedKey") "Solar finale must define a persisted completion state key."
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
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolApplier, 'defaultStart\s*=\s*pool\.Layer\s*\*\s*pool\.DefaultSegmentSize')) "Solar memory default nodes must be rewritten for the current layer, not only layer 0."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolApplier, 'selectStart\s*=\s*pool\.Layer\s*\*\s*pool\.SelectSegmentSize')) "Solar memory candidate SelectNode entries must be rewritten for the current layer."
    Assert-True $solarMemoryMapNodePoolApplier.Contains("TrimSolarMemoryEventRecord") "Solar memory must roll back ordinary event records consumed during base map generation."
    Assert-True $sunExpIds.Contains("SolarMemoryEventIds") "Solar memory must define all fixed story event ids."
    Assert-True $sunExpIds.Contains("Sub_solar_memory_above_sacred_wheel") "Solar memory id list must include the sixth fixed event."
    Assert-True $sunExpIds.Contains("SolarMemoryLayerNames") "Solar memory must define custom layer names."
    Assert-True $solarMemoryModeRuntime.Contains('"MapSelectUI.DataUpdate", ApplySolarMemoryLayerTitle') "Solar memory must override map layer titles in MapSelectUI."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("MidLayerSlotIndex = 3") "Solar memory must reserve the fourth map slot for the second story event in each layer."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("CreateSolarMemoryEventNode(layer, OpeningSlotIndex)") "Solar memory default nodes must use the per-layer opening story event."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("CreateSolarMemoryEventNode(layer, MidLayerSlotIndex)")) "Solar memory SelectNode entries must not expose fixed story events as draggable candidates."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryFixedNodeSpec.Event(SolarMemoryMidLayerSlotIndex") "Solar memory runtime must lock the fourth map node as the second story event."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("GenerateFinaleLayer")) "Solar memory must not generate a dedicated finale map layer while third-layer completion settles immediately."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("CreateSolarFinaleStoryEventNode")) "Solar memory must not create finale pre-boss dialogue nodes in map generation."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("TryCreateFixedEndingNode") "Solar memory must reserve fixed ending nodes for per-layer story and boss endpoints."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("TryCreateFixedBossNode") "Solar memory must reserve fixed story boss nodes for accepted Wuna bosses."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolFactory, 'if\s*\(\s*layer\s*==\s*0\s*\)\s*\{\s*return\s+false\s*;')) "Solar memory must not feed a layer-one ending event into native FightPrefab initialization."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("CreateExpandedBossPoolNode") "Solar memory must use an expanded all-layer boss pool for non-fixed boss nodes."
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
    Assert-True $buffScripts.Contains('case "boss_trait_mirror_array":') "BuffScripts must route mirror-array boss trait apply/clear."
    Assert-True $buffScripts.Contains('case "boss_trait_merciless_daylight":') "BuffScripts must route merciless-daylight boss trait apply/clear."
    Assert-True $buffScripts.Contains('case "boss_trait_white_radiance_saint":') "BuffScripts must route white-radiance-saint boss trait apply/clear."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, SunExpIds.BossTraitMirrorArray)") "Mirror-array boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, SunExpIds.BossTraitMercilessDaylight)") "Second-sun boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, SunExpIds.BossTraitWhiteRadianceSaint)") "Saint Wuna boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("TriggerMirrorArray") "BossScripts must implement the mirror-array trait trigger."
    Assert-True $bossScripts.Contains("TriggerMercilessDaylight") "BossScripts must implement the merciless-daylight trait trigger."
    Assert-True $bossScripts.Contains("TriggerWhiteRadianceSaint") "BossScripts must implement the white-radiance-saint trait trigger."
    Assert-True $bossScripts.Contains("MoveSavedNameToBurned") "Merciless daylight must be able to convert preserved names into burned names."
    Assert-True $bossScripts.Contains("MoveSavedNameToNameless") "White Radiance Saint must be able to convert preserved names into nameless people."
    Assert-True $buffData.Contains("boss_trait_mirror_array") "Buff data must define the mirror-array boss trait."
    Assert-True $buffData.Contains("boss_trait_merciless_daylight") "Buff data must define the merciless-daylight boss trait."
    Assert-True $buffData.Contains("boss_trait_white_radiance_saint") "Buff data must define the white-radiance-saint boss trait."
    Assert-True $buffText.Contains("三千环日镜") "Buff text must localize the mirror-array boss trait."
    Assert-True $buffText.Contains("无慈白昼") "Buff text must localize the merciless-daylight boss trait."
    Assert-True $buffText.Contains("白耀圣女") "Buff text must localize the white-radiance-saint boss trait."
    Assert-True $enemyData.Contains("SunExp_sunexp_boss_trait_mirror_array") "Mirror-array enemy data must expose its trait in AttributeText."
    Assert-True $enemyData.Contains("SunExp_sunexp_boss_trait_merciless_daylight") "Second-sun enemy data must expose its trait in AttributeText."
    Assert-True $enemyData.Contains("SunExp_sunexp_boss_trait_white_radiance_saint") "Saint Wuna enemy data must expose its trait in AttributeText."
    Assert-True $enemyText.Contains("<title>镜阵</title>") "Mirror-array bestiary text must use the renamed 镜阵 entry."
    Assert-True $enemyText.Contains("<title>终日</title>") "Second-sun bestiary text must use the renamed 终日 entry."
    Assert-True $enemyText.Contains("<title>圣祷</title>") "Saint Wuna bestiary text must use the renamed 圣祷 entry."
    Assert-True (-not $enemyText.Contains("镜阵校准")) "Boss bestiary text must not keep the old 镜阵校准 title."
    Assert-True (-not $enemyText.Contains("最后净化")) "Boss bestiary text must not keep the old 最后净化 title."
    Assert-True $keywordText.Contains('"镜阵"') "Keyword dictionary must expose 镜阵."
    Assert-True $keywordText.Contains('"终日"') "Keyword dictionary must expose 终日."
    Assert-True $keywordText.Contains('"焚书"') "Keyword dictionary must expose 焚书."
    Assert-True $keywordText.Contains('"圣祷"') "Keyword dictionary must expose 圣祷."
    Assert-True $keywordText.Contains('"时光铭刻"') "Keyword dictionary must expose 时光铭刻."
    Assert-True $keywordText.Contains('"无名之人"') "Keyword dictionary must expose 无名之人."
    Assert-True $enemyCardText.Contains("enemycard_saint_purification,,圣祷") "Saint purification enemy-card text must use 圣祷."
    Assert-True $enemyCardText.Contains("enemycard_saint_return_to_court,,时光铭刻") "Saint return enemy-card text must use 时光铭刻."
    Assert-True $solarMemoryModeRuntime.Contains("foreach (var spec in FixedNodeSpecs(layer))") "Solar memory sync repair must force every fixed map node id."
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryMapIds[eventIndex]") "Solar memory sync repair must use the fixed story map id array."
    Assert-True $solarMemoryModeRuntime.Contains("SunExpIds.SolarMemoryFullEventIds[eventIndex]") "Solar memory sync repair must use the fixed story event id array."
    Assert-True $eventScripts.Contains("public static void InitSolarMemoryNode") "Solar memory fixed story events must expose an init method."
    Assert-True $eventScripts.Contains("public static void ContinueSolarMemory") "Solar memory fixed story events must expose a continue method."
    Assert-True $eventScripts.Contains('PlayerApi.SetGameVar(SunExpIds.SolarMemoryOriginPointsKey, "50")') "Solar memory event initialization must not reset origin points to the old value."
    Assert-True $mapData.Contains("solar_memory_black_sun_after,Event,Breaks_solar_memory_black_sun_after,-1") "Solar memory map data must use a Breaks placeholder so normal adventure generation excludes it."
    Assert-True $mapData.Contains("solar_memory_above_sacred_wheel,Event,Breaks_solar_memory_above_sacred_wheel,-1") "Solar memory map data must use Breaks placeholders for all fixed story events."
    Assert-True $mapData.Contains("solar_memory_boss_orbit_mirror_array,Fight,SunExp_sunexp_level_orbit_mirror_array,-1") "Solar memory map data must include the fixed mirror-array boss."
    Assert-True $mapData.Contains("solar_memory_boss_second_sun_last_day,Fight,SunExp_sunexp_level_second_sun_last_day,-1") "Solar memory map data must include the fixed second-sun boss."
    Assert-True $mapData.Contains("solar_memory_boss_saint_wuna,Fight,SunExp_sunexp_level_saint_wuna,-1") "Solar memory map data must include the hidden saint boss."
    Assert-True $levelData.Contains("level_saint_wuna,SunExp_sunexp_boss_saint_wuna,boss,-1") "Solar memory level data must define the hidden saint fight as a boss level."
    Assert-True $mapText.Contains("solar_memory_polluted_light") "Solar memory map text must include the polluted light node."
    Assert-True ($mapText.Contains("solar_memory_boss_saint_wuna") -and $mapText.Contains("Hidden Boss")) "Solar memory map text must mark the hidden saint fight as a boss node."
    Assert-True $eventData.Contains("Sub_solar_memory_grief_struggle,CS.SunExp.Dll.Scripting.EventScripts.ContinueSolarMemory();") "Solar memory event data must route story choices through C# continue."
    Assert-True $eventText.Contains("Sub_solar_memory_above_sacred_wheel") "Solar memory event text must include the sixth fixed story row."
    Assert-True (-not $eventText.Contains("Alderin")) "Solar finale ending text must not refer to Alderin as Wuna's world."
    Assert-True $solarMemoryModeRuntime.Contains("public static int SanitizeSolarMemoryRoleCards") "Solar memory must expose a role-card sanitizer."
    Assert-True $solarMemoryModeRuntime.Contains("RemoveEventConfigs(role.cardList") "Solar memory sanitizer must remove event cards from the actual deck."
    Assert-True $solarMemoryModeRuntime.Contains("RemoveEventConfigs(role.UnCardList") "Solar memory sanitizer must remove event cards from the reserve pool."
    Assert-True $solarMemoryModeRuntime.Contains('SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool")') "Clearing the solar memory reserve must also sanitize the active deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "NormalMapManager.InitRoleTable")') "Solar memory role initialization must sanitize the official starter deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "ApplyStarterDeck")') "Solar memory custom starter deck application must sanitize the final deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "KeepOfficialDeck")') "Solar memory official starter deck path must sanitize before continuing."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("!SolarMemoryModeRuntime.IsSolarMemoryEventCard(id)") "Solar memory starter deck candidates must exclude event cards."
    Assert-True $solarMemoryModeRuntime.Contains('saveInfo.GameVars[SunExpIds.SolarMemoryOriginPointsKey] = "50"') "Solar memory must initialize origin setup with 50 points."
    Assert-True $sunExpIds.Contains("SolarMemoryPrepStepKey") "Solar memory preparation must persist an explicit preparation step."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryPrepStep.DeckSelection") "Solar memory saves must initialize the preparation state machine."
    Assert-True $solarMemoryPreparationRuntime.Contains("public static void StartOrResume") "Solar memory preparation runtime must expose a stable start/resume entry point."
    Assert-True $solarMemoryPreparationRuntime.Contains("InferStepFromLegacyState") "Solar memory preparation runtime must infer state from old boolean keys."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("public static bool OpenOrResume") "Solar memory starter deck runtime must expose a resumable preparation entry point."
    Assert-True $eventScripts.Contains("OpenSolarMemoryPreparation") "Solar memory start event must expose a preparation entry point."
    Assert-True $eventScripts.Contains("SolarMemoryPreparationRuntime.IsComplete()") "Solar memory boss rush must be gated by preparation completion."
    Assert-True $eventData.Contains("Sub_solar_memory_start,,,CS.SunExp.Dll.Scripting.EventScripts.OpenSolarMemoryPreparation();") "Solar memory start event must route its preparation option through C# preparation."
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
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SunExpIds.SolarMemoryBlessSelectedIdsKey") "Solar memory blessing picker must persist selected ids for re-entry safety."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selected.Add(entries[index % entries.Count].Id)") "Solar memory blessing auto-fill must allow duplicate blessings when needed."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selected.RemoveAt(index)") "Solar memory blessing picker must remove selected rows by index for duplicate ids."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("private static bool IsSelected")) "Solar memory blessing picker must not globally deduplicate blessing ids."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("if (IsSelected(entry.Id))")) "Solar memory blessing picker must allow duplicate manual selections."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("CreateBlessUI")) "Solar memory custom blessing picker must not call the native blessing choice UI."
    Assert-True $eventScripts.Contains("CanClaim(progress)") "Event rewards must be guarded by current progress."
    Assert-True $eventScripts.Contains("GetProgress() == progress - 1") "Event reward progress must match exactly before advancing."
    Assert-True $mapData.Contains("solar_event,Event,Breaks_solar_event,-1") "solar_event must use a Breaks-like static NodeId so base random map generation filters it out."
    $normalEventNote = -join ([char]0x666E, [char]0x901A, [char]0x4E8B, [char]0x4EF6)
    $solarNote = -join ([char]0x65E5, [char]0x8000, [char]0x4E8B, [char]0x4EF6)
    Assert-True $mapText.Contains("solar_event,$normalEventNote,") "solar_event map text Note must use a base-game map note to avoid NormalMapManager weight lookup crashes."
    Assert-True (-not $mapText.Contains("solar_event,$solarNote,")) "solar_event must not use a custom solar Note; the base game does not know that map weight key."

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
