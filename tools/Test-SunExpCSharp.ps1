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
    $cardConfigApi = Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"
    $loneerCombatState = Join-Path $RepoRoot "SunExp-Dev\Mechanics\LoneerCombatState.cs"
    $starScoreCombatState = Join-Path $RepoRoot "SunExp-Dev\Mechanics\StarScoreCombatState.cs"

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
    <Compile Include="$cardConfigApi" />
    <Compile Include="$loneerCombatState" />
    <Compile Include="$starScoreCombatState" />
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
using SunExp.Dll.Mechanics;

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
        TestSolarMemoryIsolationIds();
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
        True(SunExpIds.IsSolarMemoryExclusiveMapId("solar_event"), "Legacy solar event map ids are exclusive");
        False(SunExpIds.IsSolarMemoryExclusiveMapId("map_0"), "Base game map ids are not Solar Memory exclusive");
        True(SunExpIds.IsSolarMemoryExclusiveEventId("SunExp_sunexp_Sub_solar_memory_second_sun"), "Full Solar Memory story event ids are exclusive");
        True(SunExpIds.IsSolarMemoryExclusiveEventId("Sub_wuna_event_1"), "Legacy Wuna story event ids are exclusive");
        False(SunExpIds.IsSolarMemoryExclusiveEventId("event_2001"), "Base game event ids are not Solar Memory exclusive");
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
    $sunExpFieldId = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Infrastructure\SunExpFieldId.cs"))
    $playerApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\PlayerApi.cs"))
    $specialTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SpecialTagRuntime.cs"))
    $cardConfigApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"))
    $gameCompatibilityApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\GameCompatibilityApi.cs"))
    $cardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\CardScripts.cs"))
    $buffScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BuffScripts.cs"))
    $buffApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\BuffApi.cs"))
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
    $solarEventRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarEventRuntime.cs"))
    $solarMemoryModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"))
    $solarMemoryContentIsolationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryContentIsolationRuntime.cs"))
    $solarMemoryMapItemAnimationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryMapItemAnimationRuntime.cs"))
    $sunExpHardTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SunExpHardTagRuntime.cs"))
    $solarMemoryStarterDeckRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"))
    $solarMemorySetupFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"))
    $solarMemoryBlessingPickerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"))
    $solarMemoryPreparationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPreparationRuntime.cs"))
    $solarMemoryPlayerSetupState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarMemoryPlayerSetupState.cs"))
    $solarMemoryRoleCommitApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\SolarMemoryRoleCommitApi.cs"))
    $solarMemoryRoleCommit = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Network\RpcSolarMemoryRoleCommit.cs"))
    $audioArbiterRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AudioArbiterShared\AudioArbiterRuntime.cs"))
    $battleBgmArbiterRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "BattleBgmArbiterShared\BattleBgmArbiterRuntime.cs"))
    $modConfig = Get-Content -LiteralPath (Join-Path $RepoRoot "SunExp\ModConfig.json") -Raw | ConvertFrom-Json
    $solarMemoryMapNodePoolFactory = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolFactory.cs"))
    $solarMemoryMapNodePoolApplier = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolApplier.cs"))
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

    $addStatusBuff = [regex]::Match($executorApi, "public\s+static\s+bool\s+AddStatusBuff[\s\S]*?public\s+static\s+bool\s+RemoveStatusBuff")
    Assert-True $addStatusBuff.Success "Could not locate ExecutorApi.AddStatusBuff for source assertion."
    Assert-True (-not $addStatusBuff.Value.Contains("HandleBurnOverflow")) "AddStatusBuff must not call HandleBurnOverflow; burn overflow is handled by the StatusManager.AddBuff hook."
    $tryAddEvent = [regex]::Match($executorApi, "public\s+static\s+bool\s+TryAddEvent[\s\S]*?public\s+static\s+void\s+SetBaseScript")
    Assert-True $tryAddEvent.Success "Could not locate ExecutorApi.TryAddEvent for source assertion."
    Assert-True $tryAddEvent.Value.Contains("executor.Self == null") "TryAddEvent must skip preview/dictionary executors without Self before calling AddEvent."
    Assert-True $tryAddEvent.Value.Contains("catch (Exception ex)") "TryAddEvent must catch all event registration failures and degrade safely."
    Assert-True $executorApi.Contains("public static bool ClearFieldBuff") "ExecutorApi.ClearFieldBuff is missing."
    Assert-True $sunExpFieldId.Contains("public enum SunExpFieldId") "SunExpFieldId must define enum-like field ids."
    Assert-True $sunExpFieldId.Contains("ScorchingCanopy") "SunExpFieldId must include ScorchingCanopy."
    Assert-True $executorApi.Contains("private static int TotalFieldBuffStacks") "Field stacks must be recomputed from combat statuses."
    Assert-True $executorApi.Contains("foreach (var status in FightManager.Instance.statuses.Values)") "Field sync must scan FightManager statuses."
    Assert-True $executorApi.Contains('CombatIntAdd(FieldCombatKey(field, "Epoch"), 1);') "Field state changes must advance a shared epoch."
    Assert-True $executorApi.Contains("SyncFieldStacks(ScriptExecutor? executor, SunExpFieldId field)") "Field sync must expose the enum-based overload."
    Assert-True (-not [regex]::IsMatch($executorApi, 'ClearFieldBuff[\s\S]*?SetSharedFieldState\(fieldId,\s*0\)')) "ClearFieldBuff must resync field state instead of blindly clearing shared stacks."
    Assert-True $buffScripts.Contains("self.AddEvent(SunExpIds.ScorchingCanopy + ""OnLevelChange""") "Scorching Canopy must resync when its carrier buff level changes."
    Assert-True $buffScripts.Contains("ExecutorApi.SyncFieldStacks(self, SunExpFieldId.ScorchingCanopy);") "Scorching Canopy apply/clear must use enum-based field sync."
    Assert-True $executorApi.Contains("public static int BurnUpperBound(IStatusManager? target)") "ExecutorApi must expose a dynamic burn upper bound helper."
    Assert-True $executorApi.Contains("private const int BurnUpperBoundFallback = 1;") "Invalid burn upper bounds must fall back to the minimum valid stack count."
    Assert-True $executorApi.Contains("target.GetBuff(buffId)?.buffConfig?.UpperBound") "Burn upper bound must prefer the live BuffItemConfig.UpperBound."
    Assert-True $executorApi.Contains('GetOne(DataType.Buff, buffId)') "Burn upper bound must fall back to the current Buff data row."
    Assert-True $executorApi.Contains("var upperBound = BurnUpperBound(target);") "Burn overflow must use the dynamic burn upper bound."
    Assert-True $runtimeHooks.Contains('RegisterBefore(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffBefore);') "Burn overflow must hook StatusManager.AddBuff so the real target is known."
    Assert-True $runtimeHooks.Contains('RegisterAfter(modConfig, "StatusManager.AddBuff", OnStatusManagerAddBuffAfter);') "Solar Radiance cap repair must hook StatusManager.AddBuff after creation so first gains above 12 can be restored for Wuna."
    Assert-True (-not $runtimeHooks.Contains('RegisterBefore(modConfig, "ScriptExecutor.AddBuff", OnScriptExecutorAddBuffBefore);')) "Burn overflow must not hook ScriptExecutor.AddBuff because it can mutate the active target list."
    Assert-True $executorApi.Contains("target.AddBuff(SunExpIds.BodyBurn, overflow);") "Burn overflow must add body burn directly to the resolved status target."
    Assert-True $executorApi.Contains("private const int SolarRadianceDefaultUpperBound = 12;") "Solar Radiance default upper bound must be 12."
    Assert-True $executorApi.Contains("private const int WunaSolarRadianceUpperBound = 15;") "Wuna Solar Radiance upper bound must be 15."
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
    Assert-True ([regex]::IsMatch($cardScripts, 'case\s+"draw_flame":\s+ExecutorApi\.SetBaseScript\(self,\s+"AttackCardItem"\);\s+break;')) "draw_flame must allow self-targeting during initialization."
    Assert-True $cardScripts.Contains("var target = ExecutorApi.PrimaryTargetIncludingSelf(self);") "draw_flame must resolve targets without excluding self."
    Assert-True $cardScripts.Contains("ExecutorApi.TriggerBurnAllEnemies(self, times * 2);") "flamewheel_recurrence must trigger enemy burn 2*N times while keeping N as the cost."
    Assert-True $cardScripts.Contains("ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, level, ""Target"");") "eclipse_hex must add current Burn stacks instead of directly setting a capped level."
    Assert-True $buffScripts.Contains("return maxHp / 100 + 1;") "body_burn must deal 1% max HP + 1 true damage per stack."
    Assert-True (-not $specialTagRuntime.Contains("CardConfigApi.BaseCost")) "White radiance should use current actual play cost, not BaseCost."
    Assert-True $cardConfigApi.Contains("ReadPlayerCardCostMultiplier") "CardConfigApi must read the player CardCost multiplier."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.EnsureInCurrentLayer")) "RuntimeHooks must not inject SunExp events into normal adventure maps."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.RepairMapSelection")) "RuntimeHooks must not repair normal adventure map selections for SunExp events."
    Assert-True $solarEventRuntime.Contains("Retired: Solar Memory content must never be injected into other game modes.") "The legacy normal-mode solar event injector must remain retired."
    Assert-True $runtimeHooks.Contains("SolarMemoryContentIsolationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize the Solar Memory content isolation guard."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "NormalMapManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize World Simulation map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "SublimationManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize Sublimation map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "TeachMapManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize tutorial map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterAfter(modConfig, "SlotMachineManager.GeneratrMap", SanitizeGeneratedMap)') "Solar Memory isolation must sanitize slot-mode map generation."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", SanitizeMapBeforeSelect)') "Solar Memory isolation must clean old generated nodes before map selection UI is built."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("SanitizeSelectionArrays(maps, mapData, level)") "Solar Memory isolation must repair multiplayer map selection arrays."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("if (SolarMemoryModeRuntime.IsSolarMemoryRun())") "Solar Memory isolation must leave Solar Memory runs untouched."
    Assert-True $sunExpIds.Contains("public static bool IsSolarMemoryExclusiveMapId") "SunExpIds must centralize exclusive Solar Memory map identification."
    Assert-True $sunExpIds.Contains("public static bool IsSolarMemoryExclusiveEventId") "SunExpIds must centralize exclusive Solar Memory event identification."
    Assert-True $runtimeHooks.Contains("DuskPartnerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Dusk partner runtime."
    Assert-True $runtimeHooks.Contains("StarClayDollRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Star Clay Doll independently from Dusk."
    Assert-True $runtimeHooks.Contains("LoneerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Loneer's card-action runtime."
    Assert-True $runtimeHooks.Contains("SolarMemoryMapItemAnimationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize solar memory map-item animation fallback hooks."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);') "Solar memory map items must patch fixed boss animation paths before native MapItem.Init loads Texture2D frames."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);') "Solar memory map item animation fallback must restore enemy animation paths after native MapItem.Init."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("SunExpIds.SolarBossSecondSunLevelId") "Solar memory map item fallback must cover the second-sun boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("SunExpIds.SolarBossSaintWunaLevelId") "Solar memory map item fallback must cover the saint Wuna boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('row["Animation"] = fallbackAnimation') "Solar memory map item fallback must temporarily replace the enemy Animation row."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('restore.Row["Animation"] = restore.Animation') "Solar memory map item fallback must restore the original enemy Animation row."
    Assert-True $duskPartnerRuntime.Contains('"GameEntryUI.CheckCareer"') "Dusk runtime must clean its placeholder blessing after career checks."
    Assert-True $duskPartnerRuntime.Contains('"Fight_Start.Init"') "Dusk runtime must grant its trait at fight start."
    Assert-True $duskPartnerRuntime.Contains("status.AddBuff(SunExpIds.DuskAfterheatRecoveryTrait, 1)") "Dusk runtime must grant the afterheat recovery trait buff."
    Assert-True (-not $duskPartnerRuntime.Contains("StarClay")) "Dusk runtime must not own Star Clay Doll behavior."
    Assert-True $starClayDollRuntime.Contains("status.AddBuff(SunExpIds.StarClayDollTrait, 1)") "Star Clay Doll runtime must grant its own trait."
    Assert-True $starClayDollRuntime.Contains('"StatusManager.Hit"') "Star Clay Doll runtime must own lethal-hit protection."
    Assert-True (-not $starScoreRuntime.Contains("LoneerMiracleService")) "Generic star score runtime must not dispatch Loneer role behavior."
    Assert-True (-not $starScoreRuntime.Contains("StarClay")) "Generic star score runtime must not own partner behavior."
    Assert-True $loneerRuntime.Contains("LoneerMiracleService.OnCardActionAfter") "Loneer runtime must own non-derived card action dispatch."
    Assert-True $loneerState.Contains("Dictionary<string, LoneerCombatState>") "Loneer combat state must be keyed by owner status instead of ScriptExecutor.Vars."
    Assert-True $loneerService.Contains("LoneerCombatStateStore.GetOrCreate(self.Self)") "Loneer skill and action flows must resolve owner-scoped combat state."
    Assert-True $loneerService.Contains("SunExpIds.LoneerDerivedTag") "Copied guidance cards must be marked as derived."
    Assert-True (-not $loneerService.Contains("SunExpIds.LoneerGuidanceCardId")) "Loneer guidance must not be stored in per-executor Vars."
    Assert-True $starScoreService.Contains("StarScoreCombatStateStore.GetOrCreate(self.Self)") "Star score notes must be owner-scoped across card executors."
    Assert-True $starScoreState.Contains("while (notes.Count > Math.Max(1, windowSize))") "Star score must maintain a bounded sliding window."
    Assert-True $duskPartnerScripts.Contains("SunExpDuskAfterheatHook") "Dusk trait scripts must remain in the Dusk module."
    Assert-True $starClayDollScripts.Contains("SunExpStarClayDollHook") "Star Clay Doll trait scripts must remain in the Star Clay module."
    Assert-True $starClayDollScripts.Contains('AddEvent("ActionAfter"') "Star Clay Doll must grant starlight after an action resolves."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.DuskPartnerScripts") "XLua registration must expose the Dusk script entry point."
    Assert-True $entry.Contains("SunExp.Dll.Scripting.StarClayDollScripts") "XLua registration must expose the Star Clay Doll script entry point."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^dusk_afterheat_recovery,0,,,Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1,[^,]*,,5\r?$")) "Dusk afterheat recovery must remain a legal zero-weight technical Blessing for GameEntryUI.CheckCareer."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^dusk,10,0,0,0,2,,,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk_choice,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk,Mods/SunExp/ModResource/AnimationLib/Dusk,SunExp_sunexp_dusk_afterheat_recovery,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk\r?$")) "Dusk partner must keep a non-empty Bless column because GameEntryUI.CheckCareer creates a DataConfig from it."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^star_clay_doll_placeholder,0,,,Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1,[^,]*,,5\r?$")) "Star Clay Doll must use a non-conflicting technical Blessing id."
    Assert-True (-not [regex]::IsMatch($blessingData, "(?m)^star_clay_doll_trait,")) "Star Clay Doll Blessing id must not collide with its Buff id."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^star_clay_doll,10,0,0,0,2,,,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk_choice,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk,Mods/SunExp/ModResource/AnimationLib/Dusk,SunExp_sunexp_star_clay_doll_placeholder,Mods/SunExp/ModResource/Images/Partner/SunExp/dusk\r?$")) "Star Clay Doll partner must reference its non-conflicting placeholder Blessing."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("IsTechnicalBlessing(id)") "Solar memory blessing picker must skip technical partner blessings."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "GameConfigManager.CardPackCheck", FilterSolarMemoryCardPackCheck)') "Solar memory must filter event cards before CardPackCheck builds reward candidates."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState)') "Solar memory must capture event records before base map generation can draw ordinary events."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect)') "Solar memory must normalize SelectNode immediately before map candidate cards are created."
    Assert-True (-not $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapManager.TryChange", RouteSolarFinaleBeforeMapChange)')) "Solar finale must not open EventUI from the generic TryChange hook; that can recurse through event init failure."
    Assert-True (-not $solarMemoryModeRuntime.Contains('ShowEventUIWithTurn<MapSelectUI>("MapSelectUI", SunExpIds.SolarFinaleFullSaintGateEventId)')) "Solar finale must not open the saint gate event from map transition hooks."
    Assert-True (-not $solarMemoryModeRuntime.Contains("EnterSolarFinaleLayer")) "Solar memory must not route into a dedicated finale map layer."
    Assert-True (-not $solarMemoryModeRuntime.Contains("RepairSolarFinaleMapArrays")) "Solar memory must not force finale map candidates into a pre-boss dialogue or saint boss."
    Assert-True $solarMemoryModeRuntime.Contains('"NormalMapManager.MapItemInit", SettleLegacySolarFinaleBeforeMapItems') "Solar memory must settle legacy level-30 saves before native MapItemInit indexes map lists."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "Fight_Escape.ResetStates", PrepareSolarMemoryFightAbort)') "Solar memory fight escape must prepare map state and UI cleanup before native reset."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterAfter(modConfig, "Fight_Escape.ResetStates", SettleSolarMemoryFightAbort)') "Solar memory fight escape must settle map state after native reset."
    Assert-True $solarMemoryModeRuntime.Contains('RegisterAfter(modConfig, "Fight_Loss.Init", SettleSolarMemoryFightLoss)') "Solar memory fight loss must clear transient state before fake-loss escape transition."
    Assert-True $solarMemoryModeRuntime.Contains('EnsureSolarMemoryCurrentNodeForTransition("Fight_Escape.ResetStates:before")') "Solar memory escape must repair current node before MapManager.TryChange can consume it."
    Assert-True $solarMemoryModeRuntime.Contains('CloseSolarMemoryTransientUi("Fight_Escape.ResetStates:after")') "Solar memory escape must close transient setup UI after native fight reset."
    Assert-True $solarMemoryModeRuntime.Contains('ClearSolarFinalePendingBattle("Fight_Escape.ResetStates")') "Solar memory escape must clear pending finale battle state."
    Assert-True $solarMemoryModeRuntime.Contains('DisableRaycastsAndDestroy("SunExpSolarMemoryStarterDeck", source)') "Solar memory UI cleanup must disable raycasts before destroying starter-deck panels."
    Assert-True $solarMemoryModeRuntime.Contains("raycastTarget = false") "Solar memory UI cleanup must make destroyed Graphics non-raycastable."
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
    Assert-True (-not $solarMemoryModeRuntime.Contains("EnsureSolarMemoryMapBeforeMapItems")) "Solar memory must not rewrite MapTree immediately before native MapItemInit consumes default nodes."
    Assert-True (-not $solarMemoryModeRuntime.Contains("NormalMapManager.MapItemInit:before")) "Solar memory MapItemInit hooks must not call ApplyToCurrentLayer before native map item creation."
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
    $solarMemorySetupSources = $solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime + $solarMemoryPreparationRuntime + $solarMemoryModeRuntime
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
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("SolarMemoryPlayerSetupState.SetSelectedBlessings") "Solar memory blessing picker must persist selected ids per player for re-entry safety."
    Assert-True $solarMemoryPlayerSetupState.Contains("role.SpecialVarMap[key]") "Solar memory setup state must store preparation choices on the current role."
    Assert-True $solarMemoryPlayerSetupState.Contains("!PlayerApi.IsMultiplayerSession()") "Solar memory setup state must not migrate legacy global preparation values during multiplayer."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selected.Add(entries[index % entries.Count].Id)") "Solar memory blessing auto-fill must allow duplicate blessings when needed."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selected.RemoveAt(index)") "Solar memory blessing picker must remove selected rows by index for duplicate ids."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("private static bool IsSelected")) "Solar memory blessing picker must not globally deduplicate blessing ids."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("if (IsSelected(entry.Id))")) "Solar memory blessing picker must allow duplicate manual selections."
    Assert-True (-not $solarMemoryBlessingPickerRuntime.Contains("CreateBlessUI")) "Solar memory custom blessing picker must not call the native blessing choice UI."
    Assert-True $eventScripts.Contains("CanClaim(progress)") "Event rewards must be guarded by current progress."
    Assert-True $eventScripts.Contains("GetProgress() == progress - 1") "Event reward progress must match exactly before advancing."
    Assert-True $mapData.Contains("solar_event,Event,Breaks_solar_event,-1,7") "The legacy solar event map must stay hidden from every RandomPool draw."
    $exclusiveMapRows = @($mapData -split "`r?`n" | Where-Object { $_ -match '^solar_(event|memory_)' })
    Assert-True ($exclusiveMapRows.Count -eq 11) "Solar Memory isolation assertions must cover every shipped exclusive map row."
    Assert-True (@($exclusiveMapRows | Where-Object { $_ -notmatch ',7$' }).Count -eq 0) "Every Solar Memory map, event, and boss row must use Rarity 7."
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
