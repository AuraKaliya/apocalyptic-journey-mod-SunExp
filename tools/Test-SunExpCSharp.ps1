param(
    [string]$Configuration = "Release",
    [string]$GamePath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey",
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
    $specialTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SpecialTagRuntime.cs"))
    $cardConfigApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\GameApi\CardConfigApi.cs"))
    $cardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\CardScripts.cs"))
    $buffScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\BuffScripts.cs"))
    $eventScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Scripting\EventScripts.cs"))
    $runtimeHooks = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\RuntimeHooks.cs"))
    $solarEventRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp-Dev\Hooks\SolarEventRuntime.cs"))
    $mapData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Data\Map\sunexp.csv"))
    $mapText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "SunExp\Text\Map\sunexp.csv"))

    $addStatusBuff = [regex]::Match($executorApi, "public\s+static\s+bool\s+AddStatusBuff[\s\S]*?public\s+static\s+bool\s+RemoveStatusBuff")
    Assert-True $addStatusBuff.Success "Could not locate ExecutorApi.AddStatusBuff for source assertion."
    Assert-True (-not $addStatusBuff.Value.Contains("HandleBurnOverflow")) "AddStatusBuff must not call HandleBurnOverflow; burn overflow is handled by the ScriptExecutor.AddBuff hook."
    $tryAddEvent = [regex]::Match($executorApi, "public\s+static\s+bool\s+TryAddEvent[\s\S]*?public\s+static\s+void\s+SetBaseScript")
    Assert-True $tryAddEvent.Success "Could not locate ExecutorApi.TryAddEvent for source assertion."
    Assert-True $tryAddEvent.Value.Contains("executor.Self == null") "TryAddEvent must skip preview/dictionary executors without Self before calling AddEvent."
    Assert-True $tryAddEvent.Value.Contains("catch (Exception ex)") "TryAddEvent must catch all event registration failures and degrade safely."
    Assert-True $executorApi.Contains("public static bool ClearFieldBuff") "ExecutorApi.ClearFieldBuff is missing."
    Assert-True $executorApi.Contains('DictionaryUtil.Set(executor?.Vars, "CanSelf", canSelf ? "True" : "False");') "SetBaseScript must explicitly write CanSelf for self-targetable attack cards."
    Assert-True $executorApi.Contains("public static IStatusManager? PrimaryTargetIncludingSelf") "ExecutorApi.PrimaryTargetIncludingSelf is missing."
    Assert-True ([regex]::IsMatch($cardScripts, 'case\s+"draw_flame":\s+ExecutorApi\.SetBaseScript\(self,\s+"AttackCardItem"\);\s+break;')) "draw_flame must allow self-targeting during initialization."
    Assert-True $cardScripts.Contains("var target = ExecutorApi.PrimaryTargetIncludingSelf(self);") "draw_flame must resolve targets without excluding self."
    Assert-True $cardScripts.Contains("ExecutorApi.TriggerBurnAllEnemies(self, times * 2);") "flamewheel_recurrence must trigger enemy burn 2*N times while keeping N as the cost."
    Assert-True $buffScripts.Contains("return maxHp / 100 + 1;") "body_burn must deal 1% max HP + 1 true damage per stack."
    Assert-True (-not $specialTagRuntime.Contains("CardConfigApi.BaseCost")) "White radiance should use current actual play cost, not BaseCost."
    Assert-True $cardConfigApi.Contains("ReadPlayerCardCostMultiplier") "CardConfigApi must read the player CardCost multiplier."
    Assert-True $runtimeHooks.Contains("SolarEventRuntime.EnsureInCurrentLayer") "RuntimeHooks must route solar map injection through SolarEventRuntime."
    Assert-True $runtimeHooks.Contains("SolarEventRuntime.RepairMapSelection") "RuntimeHooks must route solar map sync repair through SolarEventRuntime."
    Assert-True (-not $solarEventRuntime.Contains("TypeGenerate")) "SolarEventRuntime must not generate map nodes by Note; the base game does not know the solar event Note."
    Assert-True $solarEventRuntime.Contains("SunExpIds.SolarEventMapId") "SolarEventRuntime must target the dedicated solar map id."
    Assert-True $solarEventRuntime.Contains("SunExpIds.WunaEventFullPrefix") "SolarEventRuntime must route mapdata to the current Wuna Sub event id."
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

if (-not $SkipBuild) {
    & (Join-Path $repoRoot "tools\Build-SunExpDll.ps1") -Configuration $Configuration -GamePath $GamePath | Out-Host
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
