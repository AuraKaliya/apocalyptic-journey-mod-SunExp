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

function Read-SourceTreeText {
    param(
        [string]$RepoRoot,
        [string]$RelativeDirectory
    )

    $directory = Join-Path $RepoRoot $RelativeDirectory
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Required source directory is missing: $RelativeDirectory"
    }

    $files = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter "*.cs" -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Required source directory has no C# files: $RelativeDirectory"
    }

    return (($files | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join [Environment]::NewLine)
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

    $dictionaryUtil = Join-Path $RepoRoot "Terrias-Dev\Infrastructure\DictionaryUtil.cs"
    $auraSharedDictionary = Join-Path $RepoRoot "AuraSharedCore\AuraSharedDictionary.cs"
    $auraCombatCardZoneSnapshot = Join-Path $RepoRoot "AuraSharedCore\AuraCombatCardZoneSnapshot.cs"
    $terriasIds = Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasIds.cs"
    $terriasContentIdCompatibility = Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasContentIdCompatibility.cs"
    $terriasFrameDispatcher = Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasFrameDispatcher.cs"
    $terriasPerformanceSettings = Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasPerformanceSettings.cs"
    $cardApi = Join-Path $RepoRoot "Terrias-Dev\GameApi\CardApi.cs"
    $combatCardViewPoolApi = Join-Path $RepoRoot "Terrias-Dev\GameApi\CombatCardViewPoolApi.cs"
    $combatCardViewPoolCatalog = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CombatCardViewPoolCatalog.cs"
    $pooledCardViewExit = Join-Path $RepoRoot "Terrias-Dev\Mechanics\PooledCardViewExit.cs"
    $cardConfigApi = Join-Path $RepoRoot "Terrias-Dev\GameApi\CardConfigApi.cs"
    $cardVisualSkinApi = Join-Path $RepoRoot "Terrias-Dev\GameApi\CardVisualSkinApi.cs"
    $cardVisualEffectApi = Join-Path $RepoRoot "Terrias-Dev\GameApi\CardVisualEffectApi.cs"
    $cardVisualEffectTarget = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualEffectTarget.cs"
    $cardVisualEffectSpec = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualEffectSpec.cs"
    $cardVisualEffectRegistry = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualEffectRegistry.cs"
    $cardVisualInterestIndex = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualInterestIndex.cs"
    $cardVisualSkinSpec = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualSkinSpec.cs"
    $cardVisualSkinRule = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualSkinRule.cs"
    $cardVisualSkinRegistry = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardVisualSkinRegistry.cs"
    $cardMutationService = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardMutationService.cs"
    $runtimeCardAttachmentService = Join-Path $RepoRoot "Terrias-Dev\Mechanics\RuntimeCardAttachmentService.cs"
    $terriasCardRefreshQueue = Join-Path $RepoRoot "Terrias-Dev\Mechanics\TerriasCardRefreshQueue.cs"
    $cardGrantPostCommitQueue = Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardGrantPostCommitQueue.cs"
    $starBlessingCostOverrideStore = Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarBlessingCostOverrideStore.cs"
    $resonanceCostTransactionStore = Join-Path $RepoRoot "Terrias-Dev\Mechanics\ResonanceCostTransactionStore.cs"
    $loneerCombatState = Join-Path $RepoRoot "Terrias-Dev\Mechanics\LoneerCombatState.cs"
    $starScoreNote = Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreNote.cs"
    $starScoreDisplaySnapshot = Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreDisplaySnapshot.cs"
    $starScoreCadenceCatalog = Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreCadenceCatalog.cs"
    $starScoreCombatState = Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreCombatState.cs"
    $starScoreArrivalCueService = Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreArrivalCueService.cs"
    $mapNodeCardArtFitMode = Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeCardArtFitMode.cs"
    $mapNodeCardArtFitResult = Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeCardArtFitResult.cs"
    $mapNodeTextureBounds = Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeTextureBounds.cs"
    $mapNodeTextureFitService = Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeTextureFitService.cs"
    $modeChoiceDragRange = Join-Path $RepoRoot "Terrias-Dev\Mechanics\ModeChoiceDragRange.cs"
    $spiritProfileIdentityResolver = Join-Path $RepoRoot "Terrias-Dev\Mechanics\SpiritProfileIdentityResolver.cs"
    $dimensionShopRandom = Join-Path $RepoRoot "Terrias-Dev\Mechanics\DimensionShopRandom.cs"
    $endlessSeaNodeKind = Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaNodeKind.cs"
    $endlessAbyssEnemyScaling = Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssEnemyScalingService.cs"
    $endlessAbyssEvacuationDepth = Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssEvacuationDepth.cs"
    $solarMemoryFixedNodeSpec = Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryFixedNodeSpec.cs"
    $solarMemoryMapSyncRepairService = Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryMapSyncRepairService.cs"
    $solarMemoryContentIsolationService = Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryContentIsolationService.cs"

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
    <Compile Include="$auraCombatCardZoneSnapshot" />
    <Compile Include="$dictionaryUtil" />
    <Compile Include="$terriasContentIdCompatibility" />
    <Compile Include="$terriasIds" />
    <Compile Include="$terriasFrameDispatcher" />
    <Compile Include="$terriasPerformanceSettings" />
    <Compile Include="$cardApi" />
    <Compile Include="$combatCardViewPoolApi" />
    <Compile Include="$combatCardViewPoolCatalog" />
    <Compile Include="$pooledCardViewExit" />
    <Compile Include="$cardConfigApi" />
    <Compile Include="$cardVisualSkinApi" />
    <Compile Include="$cardVisualEffectApi" />
    <Compile Include="$cardVisualEffectTarget" />
    <Compile Include="$cardVisualEffectSpec" />
    <Compile Include="$cardVisualEffectRegistry" />
    <Compile Include="$cardVisualInterestIndex" />
    <Compile Include="$cardVisualSkinSpec" />
    <Compile Include="$cardVisualSkinRule" />
    <Compile Include="$cardVisualSkinRegistry" />
    <Compile Include="$terriasCardRefreshQueue" />
    <Compile Include="$cardGrantPostCommitQueue" />
    <Compile Include="$cardMutationService" />
    <Compile Include="$runtimeCardAttachmentService" />
    <Compile Include="$starBlessingCostOverrideStore" />
    <Compile Include="$resonanceCostTransactionStore" />
    <Compile Include="$loneerCombatState" />
    <Compile Include="$starScoreNote" />
    <Compile Include="$starScoreDisplaySnapshot" />
    <Compile Include="$starScoreCadenceCatalog" />
    <Compile Include="$starScoreCombatState" />
    <Compile Include="$starScoreArrivalCueService" />
    <Compile Include="$mapNodeCardArtFitMode" />
    <Compile Include="$mapNodeCardArtFitResult" />
    <Compile Include="$mapNodeTextureBounds" />
    <Compile Include="$mapNodeTextureFitService" />
    <Compile Include="$modeChoiceDragRange" />
    <Compile Include="$spiritProfileIdentityResolver" />
    <Compile Include="$dimensionShopRandom" />
    <Compile Include="$endlessSeaNodeKind" />
    <Compile Include="$endlessAbyssEnemyScaling" />
    <Compile Include="$endlessAbyssEvacuationDepth" />
    <Compile Include="$solarMemoryFixedNodeSpec" />
    <Compile Include="$solarMemoryMapSyncRepairService" />
    <Compile Include="$solarMemoryContentIsolationService" />
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

namespace AuraCombatAi.Shared
{
    public enum CombatPromptKind
    {
        BurnCards
    }

    public enum CombatPromptZone
    {
        Hand
    }

    public sealed class CombatInteractionHint
    {
        public string OwnerModId { get; set; } = "";
        public string Purpose { get; set; } = "";
        public CombatPromptKind Kind { get; set; }
        public CombatPromptZone Zone { get; set; }
        public bool Forced { get; set; }
        public bool PreferLowestValue { get; set; }
    }
}

namespace AuraShared.Core
{
    public enum AuraSharedFramePhase { Presentation }
    public sealed class AuraSharedFrameSliceContext { }
    public sealed class AuraSharedFrameSliceReport
    {
        public double ElapsedMilliseconds { get; set; }
    }
    public sealed class AuraSharedFrameWorkRequest
    {
        public string OwnerId { get; set; } = "";
        public string Key { get; set; } = "";
        public string Source { get; set; } = "";
        public int DelayFrames { get; set; }
        public AuraSharedFramePhase Phase { get; set; }
        public int Priority { get; set; }
        public int EstimatedCost { get; set; }
        public double SliceBudgetMilliseconds { get; set; }
        public Func<AuraSharedFrameSliceContext, bool>? ExecuteSlice { get; set; }
        public Action<AuraSharedFrameSliceReport>? OnSliceExecuted { get; set; }
    }
    public static class AuraSharedFrameScheduler
    {
        public static bool RunCooperative(AuraSharedFrameWorkRequest request) => true;
    }
    public static class AuraCardPresentationDelta
    {
        public static bool TrySetCost(UnityEngine.Transform? transform, string costText) => true;
    }
}

namespace AuraGameData.Shared
{
    public sealed class AuraGameDataDefinitionHandle
    {
        public DataType DataType { get; set; }
        public string Id { get; set; } = "";
    }
}

namespace AuraGameData.Shared.GameApi
{
    using AuraGameData.Shared;

    public enum AuraGameDataFieldAccess { Base, Runtime, Effective }

    public sealed class AuraGameDataMaterializeRequest
    {
        public AuraGameDataDefinitionHandle? Definition { get; set; }
        public Dictionary<string, string> Vars { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> DataOverrides { get; set; } = new(StringComparer.Ordinal);
        public bool PreCompile { get; set; } = true;
    }

    public sealed class AuraGameDataHostMutationResult
    {
        public IDataConfig? Instance { get; set; }
    }

    public static class AuraGameDataHostApi
    {
        public static string ResolveId(DataType dataType, IEnumerable<string> candidates, string fallback = "")
        {
            foreach (var candidate in candidates ?? Array.Empty<string>())
            {
                if (Singleton<GameConfigManager>.Instance.GetOne(dataType, candidate) != null)
                {
                    return candidate;
                }
            }
            return fallback;
        }

        public static AuraGameDataDefinitionHandle? ResolveHandle(DataType dataType, params string[] candidates)
        {
            var id = ResolveId(dataType, candidates, "");
            return string.IsNullOrWhiteSpace(id) ? null : new AuraGameDataDefinitionHandle { DataType = dataType, Id = id };
        }

        public static AuraGameDataHostMutationResult Materialize(AuraGameDataMaterializeRequest request)
        {
            return new AuraGameDataHostMutationResult
            {
                Instance = request.Definition == null
                    ? null
                    : new DataConfig(
                        new Dictionary<string, string>
                        {
                            ["Id"] = request.Definition.Id,
                            ["Expend"] = "2",
                            ["Tag"] = ""
                        },
                        new Dictionary<string, string> { ["Id"] = request.Definition.Id })
            };
        }

        public static AuraGameDataHostMutationResult Materialize(DataType dataType, params string[] candidates)
        {
            return Materialize(new AuraGameDataMaterializeRequest
            {
                Definition = ResolveHandle(dataType, candidates)
            });
        }

        public static DataConfig CloneWritable(
            IDataConfig source,
            IReadOnlyDictionary<string, string>? dataOverrides = null,
            IReadOnlyDictionary<string, string>? varsOverrides = null,
            bool preCompile = true)
        {
            var data = new Dictionary<string, string>(source.data);
            var vars = new Dictionary<string, string>(source.Vars);
            foreach (var pair in dataOverrides ?? new Dictionary<string, string>()) data[pair.Key] = pair.Value;
            foreach (var pair in varsOverrides ?? new Dictionary<string, string>()) vars[pair.Key] = pair.Value;
            return new DataConfig(data, vars);
        }

        public static string ReadField(IDataConfig? source, string field, AuraGameDataFieldAccess access, string fallback = "")
        {
            if (source == null) return fallback;
            if (access != AuraGameDataFieldAccess.Base && source.Vars.TryGetValue(field, out var runtime)) return runtime;
            return source.data.TryGetValue(field, out var value) ? value : fallback;
        }
    }
}

namespace UnityEngine
{
    public sealed class Transform
    {
    }
}

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

    public List<DataConfig> DeckCard { get; } = new();

    public List<DataConfig> UsedCard { get; } = new();

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

    public List<DataConfig> usedCardList { get; } = new();

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

    public UnityEngine.Transform transform { get; } = new();

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

    public DataConfig(
        IDictionary<string, string> data,
        IDictionary<string, string>? vars,
        bool preCompile,
        DataType type)
        : this(data, vars)
    {
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

namespace Terrias.Dll.Hooks
{
    public enum TerriasCardPresentationSurface
    {
        PostCommit
    }

    public sealed class TestCardRoot
    {
        private readonly CardItem card;

        public TestCardRoot(CardItem card)
        {
            this.card = card;
        }

        public T? GetComponent<T>()
            where T : class
        {
            return card as T;
        }
    }

    public static class TerriasCardPresentationRouter
    {
        public static TestCardRoot? FindCombatCardRoot(IDataConfig config)
        {
            foreach (var item in Witch.UI.Window.FightUI.cardItemList)
            {
                if (ReferenceEquals(item.dataConfig, config))
                {
                    return new TestCardRoot(item);
                }
            }

            return null;
        }

        public static void RequestApply(TestCardRoot root, IDataConfig config, string source, TerriasCardPresentationSurface surface)
        {
        }

        public static void RequestActiveCombatCardsReapply(string source, int delayFrames)
        {
        }
    }
}

namespace Terrias.Dll.GameApi
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
            Action? onCancelled = null,
            AuraCombatAi.Shared.CombatInteractionHint? interactionHint = null)
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

namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog
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

    public static class TerriasPerformanceCounters
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
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

internal static class Program
{
    private static int assertions;
    private const string WhiteRadiance = "\u767d\u66dc";

    private static void Main()
    {
        TestDictionaryUtil();
        TestCardCostHelpers();
        TestStarBlessingCostOverrideStore();
        TestResonanceCostTransactionStore();
        TestCardGrantRequest();
        TestCombatCardViewPoolCatalog();
        TestCardMutationService();
        TestRuntimeCardAttachmentService();
        TestSolarTriggerCostOverride();
        TestWhiteRadianceTags();
        TestTemporaryWhiteRadianceClaim();
        TestSolarMemoryIsolationIds();
        TestSolarMemoryFixedNodeCatalog();
        TestSolarMemoryMapSyncRepair();
        TestSolarMemoryContentIsolation();
        TestCardVisualSkinRegistry();
        TestCardVisualEffectRegistry();
        TestCardVisualInterestIndex();
        TestMapNodeTextureFitService();
        TestModeChoiceDragRange();
        TestSpiritProfileIdentityResolver();
        TestLoneerStateOwnership();
        TestStarScoreWindow();
        TestStarScoreArrivalCueService();
        TestDimensionShopRandom();
        TestEndlessAbyssEnemyScaling();
        TestEndlessAbyssEvacuationDepth();

        Console.WriteLine("Terrias C# tests passed: " + assertions + " assertions.");
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

        True(DictionaryUtil.ContainsToken("Burnout, " + WhiteRadiance + " ,Froze", TerriasIds.WhiteRadianceTag), "ContainsToken trims comma-separated tokens");
        False(DictionaryUtil.ContainsToken(WhiteRadiance + "\u5316", TerriasIds.WhiteRadianceTag), "ContainsToken requires exact token matches");
    }

    private static void TestEndlessAbyssEnemyScaling()
    {
        var config = new EndlessAbyssEnemyScalingConfig();
        var floorOne = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.Monster, config);
        var floorSix = EndlessAbyssEnemyScalingService.Calculate(6, 1, EndlessSeaNodeKind.Monster, config);
        var floorSeven = EndlessAbyssEnemyScalingService.Calculate(7, 1, EndlessSeaNodeKind.Monster, config);
        var floorThirteen = EndlessAbyssEnemyScalingService.Calculate(13, 1, EndlessSeaNodeKind.Monster, config);

        Approximately(1.0f, (float)floorOne.HpMultiplier, 0.0001f, "Endless Abyss HP scaling starts from the first floor baseline");
        Approximately(1.0f, (float)floorOne.AttackMultiplier, 0.0001f, "Endless Abyss attack scaling starts from the first floor baseline");
        Approximately(1.875f, (float)floorSix.HpMultiplier, 0.0001f, "Endless Abyss HP grows on every pre-endless floor");
        Approximately(1.1425f, (float)floorSix.AttackMultiplier, 0.0001f, "Endless Abyss attack grows on every pre-endless floor");
        Approximately(2.7196f, (float)floorSeven.HpMultiplier, 0.0001f, "Endless Abyss floor seven applies the configured HP phase jump");
        Approximately(1.316224f, (float)floorSeven.AttackMultiplier, 0.0001f, "Endless Abyss floor seven applies the configured attack phase jump");
        Approximately(5.03412f, (float)floorThirteen.HpMultiplier, 0.0001f, "Endless Abyss applies its first six-floor HP cycle after floor seven");
        Approximately(1.6002739f, (float)floorThirteen.AttackMultiplier, 0.0001f, "Endless Abyss applies its first six-floor attack cycle after floor seven");

        var floorEighty = EndlessAbyssEnemyScalingService.Calculate(80, 1, EndlessSeaNodeKind.Monster, config);
        Approximately(88.98844f, (float)floorEighty.HpMultiplier, 0.0001f, "Endless Abyss HP overflow is compressed after its soft cap");
        Approximately(8.549733f, (float)floorEighty.AttackMultiplier, 0.0001f, "Endless Abyss attack overflow is compressed after its soft cap");

        var cappedGaze = EndlessAbyssEnemyScalingService.Calculate(1, 100, EndlessSeaNodeKind.Monster, config);
        Approximately(1.5f, (float)cappedGaze.HpMultiplier, 0.0001f, "Endless Abyss gaze HP growth is capped independently");
        Approximately(1.15f, (float)cappedGaze.AttackMultiplier, 0.0001f, "Endless Abyss gaze attack growth is capped independently");

        var elite = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.Elite, config);
        var boss = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.Boss, config);
        var endlessBoss = EndlessAbyssEnemyScalingService.Calculate(1, 1, EndlessSeaNodeKind.EndlessBoss, config);
        Approximately(1.12f, (float)elite.HpMultiplier, 0.0001f, "Endless Abyss elite nodes apply their HP factor");
        Approximately(1.05f, (float)elite.AttackMultiplier, 0.0001f, "Endless Abyss elite nodes apply their attack factor");
        Approximately(1.2f, (float)boss.HpMultiplier, 0.0001f, "Endless Abyss boss nodes apply their HP factor");
        Approximately(1.08f, (float)boss.AttackMultiplier, 0.0001f, "Endless Abyss boss nodes apply their attack factor");
        Approximately(1.3f, (float)endlessBoss.HpMultiplier, 0.0001f, "Endless Abyss endless boss nodes apply their HP factor");
        Approximately(1.12f, (float)endlessBoss.AttackMultiplier, 0.0001f, "Endless Abyss endless boss nodes apply their attack factor");
    }

    private static void TestEndlessAbyssEvacuationDepth()
    {
        Equal(0, EndlessAbyssEvacuationDepth.Calculate(1, 0), "Endless Abyss evacuation is available before the first node");
        Equal(5, EndlessAbyssEvacuationDepth.Calculate(1, 5), "Endless Abyss evacuation preserves first-floor node progress");
        Equal(6, EndlessAbyssEvacuationDepth.Calculate(2, 0), "Endless Abyss evacuation includes completed prior floors");
        Equal(39, EndlessAbyssEvacuationDepth.Calculate(7, 3), "Endless Abyss evacuation projects floor and node progress into native depth");
        Equal(0, EndlessAbyssEvacuationDepth.Calculate(0, -3), "Endless Abyss evacuation normalizes invalid floor and level values");
        Equal(int.MaxValue, EndlessAbyssEvacuationDepth.Calculate(int.MaxValue, int.MaxValue), "Endless Abyss evacuation depth saturates instead of overflowing");
    }

    private static void TestSolarMemoryIsolationIds()
    {
        True(TerriasIds.IsSolarMemoryExclusiveMapId("solar_memory_black_sun_after"), "Short Solar Memory story map ids are exclusive");
        True(TerriasIds.IsSolarMemoryExclusiveMapId("Terrias_terrias_solar_memory_boss_saint_wuna"), "Full Solar Memory boss map ids are exclusive");
        False(TerriasIds.IsSolarMemoryExclusiveMapId("solar_event"), "Retired solar event map ids are no longer shipped exclusive maps");
        False(TerriasIds.IsSolarMemoryExclusiveMapId("map_0"), "Base game map ids are not Solar Memory exclusive");
        True(TerriasIds.IsSolarMemoryExclusiveEventId("Terrias_terrias_Sub_solar_memory_second_sun"), "Full Solar Memory story event ids are exclusive");
        False(TerriasIds.IsSolarMemoryExclusiveEventId("Sub_wuna_event_1"), "Retired Wuna story event ids are no longer shipped exclusive events");
        False(TerriasIds.IsSolarMemoryExclusiveEventId("event_2001"), "Base game event ids are not Solar Memory exclusive");
    }

    private static void TestSolarMemoryFixedNodeCatalog()
    {
        var firstLayer = SolarMemoryFixedNodeCatalog.ForLayer(-1);
        Equal(2, firstLayer.Count, "Solar Memory first layer keeps opening and ending story locks");
        Equal(SolarMemoryFixedNodeCatalog.OpeningSlotIndex, firstLayer[0].SlotIndex, "Solar Memory opening story stays in slot zero");
        Equal(TerriasIds.SolarMemoryMapIds[0], firstLayer[0].MapId, "Solar Memory first opening story resolves from the fixed id catalog");
        Equal(TerriasIds.SolarMemoryFullEventIds[1], firstLayer[1].NodeId, "Solar Memory first ending story resolves the second layer event id");

        var secondLayer = SolarMemoryFixedNodeCatalog.ForLayer(1);
        Equal(3, secondLayer.Count, "Solar Memory second layer keeps two stories and the mirror boss");
        Equal(SolarMemoryFixedNodeCatalog.MidLayerSlotIndex, secondLayer[1].SlotIndex, "Solar Memory second story stays in the fourth slot");
        Equal(TerriasIds.SolarBossOrbitMirrorMapId, secondLayer[2].MapId, "Solar Memory second layer ends at the mirror boss");

        var finalLayer = SolarMemoryFixedNodeCatalog.ForLayer(99);
        Equal(4, finalLayer.Count, "Solar Memory final layer keeps two stories and two fixed bosses");
        Equal(TerriasIds.SolarMemoryMapIds[4], finalLayer[0].MapId, "Solar Memory final layer opening resolves the fifth story map");
        Equal(TerriasIds.SolarMemoryFullEventIds[5], finalLayer[1].NodeId, "Solar Memory final mid slot resolves the sixth story event");
        Equal(TerriasIds.SolarBossSecondSunMapId, finalLayer[2].MapId, "Solar Memory final penultimate slot is the second-sun boss");
        Equal(TerriasIds.SolarBossSaintWunaMapId, finalLayer[3].MapId, "Solar Memory final ending slot is Saint Wuna");
    }

    private static void TestSolarMemoryMapSyncRepair()
    {
        var maps = new[]
        {
            "map_0",
            TerriasIds.SolarBossOrbitMirrorMapId,
            "map_2",
            "map_3",
            "map_4",
            "map_5"
        };
        var mapData = new[] { "node_0", "node_1", "node_2", "node_3", "node_4", "node_5" };
        var repairs = new List<SolarMemoryMapSyncRepair>();

        Equal(5,
            SolarMemoryMapSyncRepairService.Repair(maps, mapData, 2, repairs.Add),
            "Solar Memory sync repair fixes every final-layer lock and misplaced exclusive node");
        Equal(5, repairs.Count, "Solar Memory sync repair reports each changed index once");
        Equal(TerriasIds.SolarMemoryMapIds[4], maps[0], "Solar Memory sync repair restores the final-layer opening story");
        Equal(TerriasIds.SolarMemoryMapIds[4], maps[1], "Solar Memory sync repair replaces misplaced exclusive nodes deterministically");
        Equal("map_2", maps[2], "Solar Memory sync repair preserves ordinary unlocked slots");
        Equal(TerriasIds.SolarMemoryFullEventIds[5], mapData[3], "Solar Memory sync repair restores the final-layer mid story");
        Equal(TerriasIds.SolarBossSecondSunLevelId, mapData[4], "Solar Memory sync repair restores the second-sun level id");
        Equal(TerriasIds.SolarBossSaintWunaLevelId, mapData[5], "Solar Memory sync repair restores the Saint Wuna level id");
        Equal(0,
            SolarMemoryMapSyncRepairService.Repair(maps, mapData, 2),
            "Solar Memory sync repair is idempotent after arrays are normalized");

        var shortMaps = new[] { "map_0", "map_1", "map_2" };
        var shortData = new[] { "node_0" };
        Equal(1,
            SolarMemoryMapSyncRepairService.Repair(shortMaps, shortData, 0),
            "Solar Memory sync repair respects mismatched synchronized array lengths");
    }

    private static void TestSolarMemoryContentIsolation()
    {
        var maps = new[]
        {
            "map_0",
            TerriasIds.SolarMemoryMapIds[0],
            "map_2",
            TerriasIds.SolarBossSaintWunaMapId
        };
        var mapData = new[]
        {
            "node_0",
            TerriasIds.SolarMemoryFullEventIds[0],
            TerriasIds.SolarMemoryFullEventIds[1],
            TerriasIds.SolarBossSaintWunaLevelId
        };
        var resolverCalls = 0;
        var replaced = SolarMemoryContentIsolationService.SanitizeSelectionArrays(
            maps,
            mapData,
            (_, _, index) =>
            {
                resolverCalls++;
                return index switch
                {
                    1 => new SolarMemoryMapSelectionReplacement("safe_event_map", "event_2001"),
                    2 => new SolarMemoryMapSelectionReplacement("safe_fight_map", "level_2001"),
                    _ => new SolarMemoryMapSelectionReplacement(
                        TerriasIds.SolarBossSaintWunaMapId,
                        TerriasIds.SolarBossSaintWunaLevelId)
                };
            });

        Equal(3, resolverCalls, "Solar Memory isolation resolves only exclusive synchronized choices");
        Equal(2, replaced, "Solar Memory isolation applies only safe non-exclusive replacements");
        Equal("map_0", maps[0], "Solar Memory isolation preserves ordinary synchronized choices");
        Equal("safe_event_map", maps[1], "Solar Memory isolation replaces an exclusive map and event pair");
        Equal("safe_fight_map", maps[2], "Solar Memory isolation replaces a normal map carrying an exclusive event id");
        Equal(TerriasIds.SolarBossSaintWunaMapId, maps[3], "Solar Memory isolation rejects an exclusive replacement result");
        False(SolarMemoryContentIsolationService.RequiresReplacement("map_0", "event_2001"), "Solar Memory isolation accepts ordinary map selections");
        True(SolarMemoryContentIsolationService.RequiresReplacement("map_0", TerriasIds.SolarMemoryFullEventIds[0]), "Solar Memory isolation detects exclusive event ids independently");
    }

    private static void TestCombatCardViewPoolCatalog()
    {
        Equal(PooledCardExitKind.MoveToDiscard,
            PooledCardViewExit.ClassifyThrowTarget(PooledCardViewExit.DiscardTargetPath),
            "Native discard visuals retain their discard destination adapter");
        Equal(PooledCardExitKind.MoveToDrawPile,
            PooledCardViewExit.ClassifyThrowTarget(PooledCardViewExit.DrawPileTargetPath),
            "Ouroboros-style visuals retain their draw-pile destination adapter");
        Equal(PooledCardExitKind.Unsupported,
            PooledCardViewExit.ClassifyThrowTarget("Canvas/FightUI/FutureSpecialZone"),
            "Unknown future card exits fail closed instead of being treated as discard");

        var close = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.StellarOvertureCloseCardId
        });
        True(CombatCardViewPoolCatalog.TryResolveBucket(close, out var closeBucket), "Stellar Overture Close is eligible for combat card pooling");
        Equal(CombatCardViewPoolCatalog.AttackBucket, closeBucket, "Stellar Overture Close always uses an attack-card view");

        var turn = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.StellarOvertureTurnCardId
        });
        True(CombatCardViewPoolCatalog.TryResolveBucket(turn, out var turnBucket), "Stellar Overture Turn is eligible for combat card pooling");
        Equal(CombatCardViewPoolCatalog.AttackBucket, turnBucket, "Stellar Overture Turn always uses an attack-card view");

        var heartChange = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "Terrias_terrias_heart_change"
        });
        True(CombatCardViewPoolCatalog.TryResolveBucket(heartChange, out var heartChangeBucket), "Heart Change is eligible for combat card pooling");
        Equal(CombatCardViewPoolCatalog.AttackBucket, heartChangeBucket, "Heart Change always uses an attack-card view");

        var projectionRole = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.ProjectionRoleTemplateCardId
        });
        True(CombatCardViewPoolCatalog.TryResolveBucket(projectionRole, out var projectionBucket), "Projection role cards are eligible for combat card pooling");
        Equal(CombatCardViewPoolCatalog.CommonBucket, projectionBucket, "Projection role cards use common-card views");

        close.Vars["BaseScript"] = "AttackCardItem";
        True(CombatCardViewPoolCatalog.MatchesInitializedBucket(close, closeBucket, out _), "Initialized attack cards match their selected pool bucket");
        close.Vars["BaseScript"] = "CommonCardItem";
        False(CombatCardViewPoolCatalog.MatchesInitializedBucket(close, closeBucket, out _), "Pool validation rejects an initialized component mismatch");
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

        CardVisualSkinApi.RegisterTerriasDefaults();
        var radiantSparkCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "Terrias_terrias_morning_light_bulwark",
            ["PackBelong"] = TerriasIds.RadiantSparkCardPackId,
            ["Icon"] = "Mods/Terrias/ModResource/Images/Card/Terrias/morning_light_bulwark"
        });
        Equal(TerriasIds.SunCardVisualSkinId, CardVisualSkinRegistry.Resolve(radiantSparkCard)?.Id, "Terrias defaults keep Sun packs on the Sun card visual skin");

        var morningStarPackCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.PrewrittenMeasureCardId,
            ["PackBelong"] = TerriasIds.MorningStarOvertureCardPackId,
            ["Icon"] = "Mods/Terrias/ModResource/Images/Card/MorningStar/prewritten_measure"
        });
        Equal(TerriasIds.MorningStarCardVisualSkinId, CardVisualSkinRegistry.Resolve(morningStarPackCard)?.Id, "Morning Star Overture pack cards use the Morning Star card visual skin");

        var generatedOvertureCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "*" + TerriasIds.StellarOvertureStartShortCardId,
            ["PackBelong"] = "",
            ["Icon"] = ""
        });
        Equal(TerriasIds.MorningStarCardVisualSkinId, CardVisualSkinRegistry.Resolve(generatedOvertureCard)?.Id, "Generated Stellar Overture cards use the Morning Star card visual skin by runtime id");
        CardVisualSkinRegistry.ClearOwner(TerriasIds.ModId);
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
            TerriasIds.CardFaceFoilHoloVisualEffectId,
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
            TerriasIds.CardFaceFoilHoloVisualEffectId,
            "Full",
            30,
            new[] { TerriasIds.BlazingCrownCollapseCardId }));
        var blazingCrownCollapse = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.BlazingCrownCollapseCardId
        });
        Equal("test.effect.full", CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, blazingCrownCollapse)?.Id, "Card visual effect supports full mod-qualified card ids on the frame target");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, blazingCrownCollapse)?.Id, "Frame card visual effects do not bleed into the face target");

        CardVisualEffectRegistry.ClearOwner("TestMod");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, target)?.Id, "Clearing owner removes registered card visual effects");
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, blazingCrownCollapse)?.Id, "Clearing owner removes registered frame visual effects");

        CardVisualEffectApi.RegisterTerriasDefaults();
        Equal(TerriasIds.BlazingCrownCollapseHoloEffectBindingId, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, blazingCrownCollapse)?.Id, "Blazing Crown Collapse foil applies to the card frame");
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
            Equal(TerriasIds.StellarOvertureStardustEffectBindingId, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, generatedOverture)?.Id, "Stardust applies to generated Stellar Overture frame id " + cardId);
            Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, generatedOverture)?.Id, "Stardust does not apply to generated Stellar Overture face id " + cardId);
        }

        foreach (var cardId in new[]
        {
            TerriasIds.StellarOvertureStartShortCardId,
            TerriasIds.StellarOvertureSustainShortCardId,
            TerriasIds.StellarOvertureTurnShortCardId,
            TerriasIds.StellarOvertureCloseShortCardId,
            TerriasIds.StellarOvertureStartCardId,
            TerriasIds.StellarOvertureSustainCardId,
            TerriasIds.StellarOvertureTurnCardId,
            TerriasIds.StellarOvertureCloseCardId
        })
        {
            var overture = new DataConfig(new Dictionary<string, string>
            {
                ["Id"] = cardId
            });
            Equal(TerriasIds.StellarOvertureStardustEffectBindingId, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, overture)?.Id, "Stardust applies to Stellar Overture frame id " + cardId);
            Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, overture)?.Id, "Stardust does not apply to Stellar Overture face id " + cardId);
        }

        var unrelatedGeneratedSuffix = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "OtherMod_terrias_stellar_overture_start"
        });
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, unrelatedGeneratedSuffix)?.Id, "Leading star generated-card ids are matched literally, not as broad wildcards");

        var ordinaryMorningStarCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.PrewrittenMeasureCardId
        });
        Equal(null, CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, ordinaryMorningStarCard)?.Id, "Stardust does not apply to ordinary Morning Star cards");
    }

    private static void TestCardVisualInterestIndex()
    {
        CardVisualSkinRegistry.ClearOwner("InterestTest");
        CardVisualEffectRegistry.ClearOwner("InterestTest");

        var officialCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "official_card",
            ["PackBelong"] = "official_pack",
            ["Icon"] = "Icon/Card/official"
        });
        False(CardVisualInterestIndex.MayAffect(officialCard), "Card visual interest index misses ordinary official cards");

        CardVisualSkinApi.RegisterTheme(
            "InterestTest",
            "interest.skin",
            "frame",
            "",
            "Interest",
            10,
            null,
            new[] { "interest_pack" },
            null);
        var skinCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "skin_card",
            ["PackBelong"] = "interest_pack",
            ["Icon"] = "Icon/Card/skin"
        });
        True(CardVisualInterestIndex.MayAffect(skinCard), "Card visual interest index hits skin pack rules");

        CardVisualSkinRegistry.ClearOwner("InterestTest");
        False(CardVisualInterestIndex.MayAffect(skinCard), "Card visual interest index invalidates after skin rules are cleared");

        CardVisualEffectRegistry.Register(new CardVisualEffectSpec(
            "InterestTest",
            "interest.effect",
            CardVisualEffectTarget.Frame,
            TerriasIds.CardFaceFoilHoloVisualEffectId,
            "Interest Effect",
            10,
            new[] { "effect_card" }));
        var effectCard = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = "effect_card",
            ["PackBelong"] = "official_pack",
            ["Icon"] = "Icon/Card/effect"
        });
        True(CardVisualInterestIndex.MayAffect(effectCard), "Card visual interest index hits frame effect rules without a skin rule");

        CardVisualEffectRegistry.ClearOwner("InterestTest");
        False(CardVisualInterestIndex.MayAffect(effectCard), "Card visual interest index invalidates after effect rules are cleared");
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

    private static void TestDimensionShopRandom()
    {
        Equal(-1, DimensionShopRandom.Index("run", "card", 0, 0), "Dimension shop random handles empty pools");
        Equal(
            DimensionShopRandom.Index("run", "card", 0, 4),
            DimensionShopRandom.Index("run", "card", 0, 4),
            "Dimension shop initial shelves are deterministic for the same run seed");
        Equal(
            DimensionShopRandom.Index("run", "card", 0, 4),
            DimensionShopRandom.Index("run", "card", -5, 4),
            "Dimension shop random clamps invalid counters to the initial draw");

        var cardSequence = Enumerable.Range(0, 64)
            .Select(counter => DimensionShopRandom.Index("run|player", "refresh.card", counter, 4))
            .ToArray();
        var relicSequence = Enumerable.Range(0, 64)
            .Select(counter => DimensionShopRandom.Index("run|player", "refresh.relic", counter, 4))
            .ToArray();
        True(cardSequence.All(index => index >= 0 && index < 4), "Dimension shop random indices stay inside the configured pool");
        False(cardSequence.SequenceEqual(relicSequence), "Dimension shop card and relic refreshes use independent deterministic streams");
        True(cardSequence.Distinct().Count() < cardSequence.Length, "Dimension shop draws permit repeated products instead of tracking a no-repeat bag");

        var shelf = DimensionShopRandom.Sample(new[] { "a", "b", "c", "d" }, "run|player", "cards", 2, 3);
        Equal(3, shelf.Count, "Dimension shop fills three offer slots when the pool is large enough");
        Equal(3, shelf.Distinct().Count(), "Dimension shop samples one shelf without duplicate products");
        True(
            shelf.SequenceEqual(DimensionShopRandom.Sample(new[] { "a", "b", "c", "d" }, "run|player", "cards", 2, 3)),
            "Dimension shop multi-offer shelves are deterministic");
        Equal(
            2,
            DimensionShopRandom.Sample(new[] { "a", "b" }, "run|player", "cards", 2, 3).Count,
            "Dimension shop does not duplicate products to fill a short pool");
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

    private static void TestSpiritProfileIdentityResolver()
    {
        var profiles = new List<TestSpiritProfile>
        {
            new("10026", "*"),
            new("boss_orbit_mirror_array", "*"),
            new("enemy_exact", "v1"),
            new("enemy_10026", "enemy_10026"),
            new("*", "*")
        };

        SpiritProfileResolution<TestSpiritProfile> Resolve(string enemyId, string variantId) =>
            SpiritProfileIdentityResolver.Resolve(profiles, profile => profile.EnemyId, profile => profile.VariantId, enemyId, variantId);

        var runtimeBaseGame = Resolve("enemy_10026", "enemy_10026");
        Equal("enemy_10026", runtimeBaseGame.MatchedEnemyId, "Raw exact profiles take precedence over canonical aliases");
        Equal("exact", runtimeBaseGame.MatchKind, "Raw exact profile resolution reports its match kind");

        profiles.RemoveAt(3);
        var oldCapturedCard = Resolve("enemy_10026", "enemy_10026");
        Equal("10026", oldCapturedCard.MatchedEnemyId, "Old captured cards resolve the base-game runtime prefix to the stable registry id");
        Equal("*", oldCapturedCard.MatchedVariantId, "Canonical base-game ids retain enemy wildcard fallback");
        Equal("alias-enemy-wildcard", oldCapturedCard.MatchKind, "Base-game prefix normalization is visible in diagnostics");
        True(oldCapturedCard.UsedAlias, "Base-game prefix normalization is marked as an alias match");
        True(oldCapturedCard.UsedVariantWildcard, "Enemy wildcard use is marked in the resolution result");
        False(oldCapturedCard.UsedGlobalFallback, "Known base-game enemies do not reach the global profile");

        var canonical = Resolve("10026", "10026");
        Equal("10026", canonical.MatchedEnemyId, "Canonical registry ids continue to resolve directly");
        Equal("enemy-wildcard", canonical.MatchKind, "Canonical ids use the explicit enemy wildcard profile");

        var terriasRuntime = Resolve("Terrias_terrias_boss_orbit_mirror_array", "Terrias_terrias_boss_orbit_mirror_array");
        Equal("boss_orbit_mirror_array", terriasRuntime.MatchedEnemyId, "Terrias runtime ids resolve to short stable profile ids");
        Equal("alias-enemy-wildcard", terriasRuntime.MatchKind, "Terrias prefix normalization is visible in diagnostics");

        var exactVariant = Resolve("enemy_exact", "v1");
        Equal("exact", exactVariant.MatchKind, "Explicit variant profiles resolve before enemy wildcards");
        Equal("v1", exactVariant.MatchedVariantId, "Explicit variant identity is retained");

        var unknownModEnemy = Resolve("OtherMod_enemy_dragon", "OtherMod_enemy_dragon");
        Equal("*", unknownModEnemy.MatchedEnemyId, "Unknown mod enemies use the global compatibility profile");
        Equal("global-fallback", unknownModEnemy.MatchKind, "Unknown mod fallback is explicit in diagnostics");
        True(unknownModEnemy.UsedGlobalFallback, "Unknown mod fallback is marked in the resolution result");

        SpiritProfileIdentityResolver.ParseProfileKey("spirit:enemy_10026#enemy_10026", out var parsedEnemy, out var parsedVariant);
        Equal("enemy_10026", parsedEnemy, "Persisted spirit profile keys retain their raw enemy id");
        Equal("enemy_10026", parsedVariant, "Persisted spirit profile keys retain their raw variant id");
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

    private static void TestResonanceCostTransactionStore()
    {
        var store = new ResonanceCostTransactionStore();
        var owner = new FakeStatus("resonance-owner");
        var config = NewConfig(
            new Dictionary<string, string>
            {
                ["Id"] = "resonance_target",
                ["Expend"] = "4"
            },
            new Dictionary<string, string>
            {
                ["OnceExCost"] = "1"
            });

        var begun = store.Begin(owner, config, 3);
        True(begun.Found, "Resonance begins a cost-payment transaction");
        Equal(3, begun.ResonancePaid, "Resonance records the exact number of substituted Magic points");
        True(ReferenceEquals(owner, begun.Owner), "Resonance records the player who funded the payment");
        Equal("-2", config.Vars["OnceExCost"], "Resonance applies its own one-use cost delta");
        False(store.Begin(owner, config, 1).Found, "Resonance cannot charge the same card transaction twice");

        store.MarkPaymentApplied(config);
        DictionaryUtil.Set(config.Vars, "OnceExCost", "0");
        var cancelled = store.Cancel(config);
        True(cancelled.PaymentApplied, "Cancelled Resonance transaction reports that its Buff payment was applied");
        Equal("3", config.Vars["OnceExCost"], "Cancelling Resonance removes only its own delta and preserves later modifiers");
        False(store.Contains(config), "Cancelling Resonance closes the transaction exactly once");

        DictionaryUtil.Set(config.Vars, "OnceExCost", "1");
        True(store.Begin(owner, config, 2).Found, "Resonance can begin a later transaction for the same card");
        store.MarkPaymentApplied(config);
        store.MarkActionObserved(config);
        True(store.ActionObserved(config), "Card Action marks the Resonance transaction as confirmed");
        var committed = store.Commit(config);
        True(committed.ActionObserved, "Committed Resonance transaction retains Action evidence");
        Equal("0", config.Vars["OnceExCost"], "Successful Resonance payment consumes all one-use cost modifiers");

        DictionaryUtil.Set(config.Vars, "OnceExCost", "2");
        store.Begin(owner, config, 1);
        var cleared = store.CancelAll();
        Equal(1, cleared.Count, "Fight cleanup returns every pending Resonance transaction");
        Equal("2", config.Vars["OnceExCost"], "Fight cleanup removes the pending Resonance delta");
        False(store.Contains(config), "Fight cleanup clears pending Resonance state");
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
        var presentationResult = CardApi.GrantCardToHand(
            executor,
            CardGrantRequest.ToHand("runtime_presentation_card")
                .WithRuntimePresentation(new Dictionary<string, string>
                {
                    ["Name"] = "Spirit: Cat",
                    ["Description"] = "Summon one Cat",
                    ["RuntimeFlag"] = "must-stay-in-vars"
                })
                .Configure("runtime-state", config => config.Vars["RuntimeFlag"] = "1"));
        True(presentationResult.Success, "CardApi grant composes runtime presentation before native materialization");
        True(presentationResult.Config!.data is System.Collections.ObjectModel.ReadOnlyDictionary<string, string>, "Runtime presentation remains immutable after DataConfig construction");
        Equal("Spirit: Cat", presentationResult.Config!.data["Name"], "Native card readers receive the dynamic runtime name");
        Equal("Summon one Cat", presentationResult.Config!.data["Description"], "Native card readers receive the dynamic runtime description");
        Equal("Spirit: Cat", presentationResult.Config!.Vars["Name"], "Runtime presentation also remains available through Vars");
        False(presentationResult.Config!.data.ContainsKey("RuntimeFlag"), "Non-presentation runtime state is not copied into the immutable data snapshot");
        Equal("1", presentationResult.Config!.Vars["RuntimeFlag"], "Non-presentation runtime state remains writable in Vars");
        True(CardApi.MarkForAdventureRemoval(presentationResult.Config), "CardApi marks a valid card for adventure removal");
        Equal("True", presentationResult.Config!.Vars["NeedRemove"], "Adventure removal uses the host NeedRemove runtime contract");

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
        Equal("1", config.Vars[TerriasIds.TempWhiteRadiance], "Temporary white radiance marker is set");
        Equal("0", config.Vars[TerriasIds.TempWhiteRadianceResolved], "Temporary white radiance starts unresolved");
        True(CardMutationService.HasSpecialTag(config, TerriasIds.WhiteRadianceTag), "Temporary white radiance adds the white-radiance SpecialTag");
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
        Equal("1", config.Vars[TerriasIds.TempWhiteRadiance], "Runtime attachment marks temporary white radiance on config");
        Equal(card.Vars[TerriasIds.TempWhiteRadianceLockId], config.Vars[TerriasIds.TempWhiteRadianceLockId], "Card item and config share the temporary white radiance lock");
        True(CardConfigApi.HasTemporaryWhiteRadiance(config), "Runtime attachment is visible to the white-radiance trigger runtime");
        False(CardConfigApi.HasNativeWhiteRadiance(config), "Runtime hand attachment does not turn white radiance into a native run tag");

        var cleared = RuntimeCardAttachmentService.ClearTemporaryAttachments("test");
        True(cleared > 0, "Runtime attachment cleanup removes temporary card vars at the next fight boundary");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup removes temporary Burnout from config Vars.Tag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes temporary white radiance from config Vars.SpecialTag");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes the temporary marker from card Vars");
        False(config.Vars.ContainsKey(TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes temporary white radiance state");
        False(config.Vars.ContainsKey(TerriasIds.TempWhiteRadianceLockId), "Runtime attachment cleanup removes the temporary white radiance lock");
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
                [TerriasIds.RuntimeMarkersKey] = TerriasIds.TempWhiteRadiance,
                [TerriasIds.TempWhiteRadiance] = "1"
            });
        FightCardManager.Instance.cardList.Add(nativeBurnoutConfig);

        RuntimeCardAttachmentService.ClearTemporaryAttachments("test.legacy");
        True(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, "Tag"), "Burnout"), "Runtime attachment cleanup preserves native Burnout when base data owns it");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, "SpecialTag"), WhiteRadiance), "Runtime attachment cleanup removes legacy temporary white radiance without a snapshot");
        False(DictionaryUtil.ContainsToken(DictionaryUtil.Get(nativeBurnoutConfig.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes legacy temporary markers without a snapshot");
        False(nativeBurnoutConfig.Vars.ContainsKey(TerriasIds.TempWhiteRadiance), "Runtime attachment cleanup removes legacy temporary state without a snapshot");
    }

    private static void TestSolarTriggerCostOverride()
    {
        var config = NewConfig(
            new Dictionary<string, string> { ["Id"] = "flamewheel_recurrence" },
            new Dictionary<string, string> { [TerriasIds.SolarTriggerCost] = "5" });

        Equal(5, CardConfigApi.ResolveSolarTriggerCost(config, 1), "Solar trigger override wins over fallback");
        CardConfigApi.ClearSolarTriggerCost(config);
        Equal("", config.Vars[TerriasIds.SolarTriggerCost], "ClearSolarTriggerCost blanks the override var");
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
                [TerriasIds.TempWhiteRadiance] = "1"
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
        Equal("1", config.Vars[TerriasIds.TempWhiteRadianceResolved], "Successful claim marks card resolved");
        False(CardConfigApi.TryClaimTemporaryWhiteRadiance(config), "Second claim on the same card is blocked");

        var stale = NewConfig(vars: new Dictionary<string, string>
        {
            [TerriasIds.TempWhiteRadianceLockId] = config.Vars[TerriasIds.TempWhiteRadianceLockId],
            [TerriasIds.TempWhiteRadianceResolved] = "0"
        });
        True(CardConfigApi.TryClaimTemporaryWhiteRadiance(stale), "A stale unresolved card lock is renewed");
        NotEqual(config.Vars[TerriasIds.TempWhiteRadianceLockId], stale.Vars[TerriasIds.TempWhiteRadianceLockId], "Renewed stale lock receives a new id");
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

    private static void TestStarScoreArrivalCueService()
    {
        StarScoreArrivalCueService.Clear();
        var card = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = TerriasIds.StellarOvertureStartCardId
        });
        StarScoreArrivalCueService.Record(card, StarScoreNote.Opening, 0, false, "score-owner");
        StarScoreArrivalCueService.Record(card, StarScoreNote.Sustain, 1, false, "score-owner");
        StarScoreArrivalCueService.Record(card, StarScoreNote.Turn, 2, true, "score-owner");
        StarScoreArrivalCueService.Record(card, StarScoreNote.Close, 1, false, "score-owner");

        var cues = StarScoreArrivalCueService.Consume(card);
        Equal(4, cues.Count, "Card-use FX cue ledger retains every actual extra execution note");
        Equal(0, cues[0].SlotIndex, "First note cue targets slot one");
        Equal(2, cues[2].SlotIndex, "Cadence-completing cue targets slot three");
        True(cues[2].CompletesCadence, "Third note cue marks the cadence preview extension point");
        True(cues[0].Sequence < cues[3].Sequence, "Card-use FX cues preserve execution order");
        Equal(0, StarScoreArrivalCueService.Consume(card).Count, "Card-use FX cue ledger is consumed exactly once");
        Equal(3, StarScoreArrivalCueService.MaxVisibleRibbonCount, "Card-use FX limits one use to three visible ribbons");
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

    private sealed class TestSpiritProfile
    {
        public TestSpiritProfile(string enemyId, string variantId)
        {
            EnemyId = enemyId;
            VariantId = variantId;
        }

        public string EnemyId { get; }

        public string VariantId { get; }
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

    $executorApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\ExecutorApi.cs"))
    $terriasIds = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasIds.cs"))
    $terriasContentIdCompatibility = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasContentIdCompatibility.cs"))
    $terriasConfigIndex = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\TerriasConfigIndex.cs"))
    $terriasFieldId = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasFieldId.cs"))
    $playerApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\PlayerApi.cs"))
    $cardApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\CardApi.cs"))
    $combatCardApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\CombatCardApi.cs"))
    $fightUiCardLayoutApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\FightUiCardLayoutApi.cs"))
    $fightActionPresentationApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\FightActionPresentationApi.cs"))
    $combatCardViewPoolCatalogText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CombatCardViewPoolCatalog.cs"))
    $combatCardViewPoolText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasCombatCardViewPool.cs"))
    $pooledCombatCardViewMarkerText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\PooledCombatCardViewMarker.cs"))
    $combatCardUiDiagnostics = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasCombatCardUiDiagnostics.cs"))
    $cardGrantPostCommitQueue = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardGrantPostCommitQueue.cs"))
    $roleSkillApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\RoleSkillApi.cs"))
    $cardMutationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardMutationService.cs"))
    $polymorphActivationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\PolymorphActivationService.cs"))
    $polymorphBuffService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\PolymorphBuffService.cs"))
    $polymorphCooldownService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\PolymorphCooldownService.cs"))
    $polymorphRoleRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\PolymorphRoleRegistry.cs"))
    $polymorphRuntimeService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\PolymorphRuntimeService.cs"))
    $polymorphStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\PolymorphStateStore.cs"))
    $projectionActivationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionActivationService.cs"))
    $projectionOtherObj = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionOtherObj.cs"))
    $companionIntentPlanner = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionIntentPlanner.cs"))
    $heartChangeControlService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\HeartChangeControlService.cs"))
    $heartChangeActionProxyObj = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\HeartChangeActionProxyObj.cs"))
    $heartChangeIntentService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\HeartChangeIntentService.cs"))
    $projectionStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionStateStore.cs"))
    $projectionStrategyService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionStrategyService.cs"))
    $projectionSummonService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionSummonService.cs"))
    $spiritSummonService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SpiritSummonService.cs"))
    $projectionActionExecutor = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionActionExecutor.cs"))
    $projectionEffectContext = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionEffectContext.cs"))
    $projectionAttachmentPresenter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\ProjectionAttachmentPresenter.cs"))
    $spiritAttachmentPresenter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\SpiritAttachmentPresenter.cs"))
    $companionSceneApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\CompanionSceneApi.cs"))
    $companionSceneLifecycleRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\CompanionSceneLifecycleRuntime.cs"))
    $companionPresentationCleanup = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\CompanionPresentationCleanup.cs"))
    $projectionIntentPresenter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\ProjectionIntentPresenter.cs"))
    $pooledCardExitAnimator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\PooledCardExitAnimator.cs"))
    $projectionTurnCoordinator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionTurnCoordinator.cs"))
    $projectionTurnAnchorObj = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ProjectionTurnAnchorObj.cs"))
    $companionBattleModels = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionBattleModels.cs"))
    $companionBattleStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionBattleStateStore.cs"))
    $companionIntentRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionIntentRegistry.cs"))
    $companionIntentHandlers = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionIntentHandlers.cs"))
    $companionFriendlyRosterService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionFriendlyRosterService.cs"))
    $companionIntentSelector = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionIntentSelector.cs"))
    $companionSlotService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionSlotService.cs"))
    $companionStatsService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionStatsService.cs"))
    $companionThreatService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CompanionThreatService.cs"))
    $companionIntentRegistryJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\companion.intent.registry.json"))
    $runtimeCardAttachmentService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\RuntimeCardAttachmentService.cs"))
    $starBlessingCostOverrideStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarBlessingCostOverrideStore.cs"))
    $resonanceCostTransactionStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\ResonanceCostTransactionStore.cs"))
    $cardGrantRecipes = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\CardGrantRecipes.cs"))
    $specialTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SpecialTagRuntime.cs"))
    $companionThreatRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\CompanionThreatRuntime.cs"))
    $cardConfigApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\CardConfigApi.cs"))
    $gameCompatibilityApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\GameCompatibilityApi.cs"))
    $cardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\CardScripts.cs"))
    $familiarBlessingEffectRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\FamiliarBlessingEffectRuntime.cs"))
    $relicScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\RelicScripts.cs"))
    $morningStarCardScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\MorningStarCardScripts.cs"))
    $buffScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\BuffScripts.cs"))
    $buffApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\BuffApi.cs"))
    $statusApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\StatusApi.cs"))
    $solarRadianceService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarRadianceService.cs"))
    $burnTriggerApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\BurnTriggerApi.cs"))
    $scriptEventApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\ScriptEventApi.cs"))
    $fieldApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\FieldApi.cs"))
    $fieldRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\FieldRuntime.cs"))
    $fieldBuffHudRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\FieldBuffHudRuntime.cs"))
    $fieldBuffHudView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\FieldBuffHudView.cs"))
    $fieldBuffHudHoverProbe = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\FieldBuffHudHoverProbe.cs"))
    $fieldBuffHudTooltipView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\FieldBuffHudTooltipView.cs"))
    $fieldEffectHandlers = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\FieldEffectHandlers.cs"))
    $fieldEffectRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\FieldEffectRegistry.cs"))
    $morningStarOvertureService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\MorningStarOvertureService.cs"))
    $fieldStartCoordinator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\FieldStartCoordinator.cs"))
    $difficultyFieldPoolService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\DifficultyFieldPoolService.cs"))
    $relicFieldStartSourceService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\RelicFieldStartSourceService.cs"))
    $relicOpeningEffectService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\RelicOpeningEffectService.cs"))
    $relicApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\RelicApi.cs"))
    $fieldNetworkSync = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\FieldNetworkSync.cs"))
    $auraAuthoritativeSyncRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AuraSharedCore\AuraAuthoritativeSyncRuntime.cs"))
    $endlessAbyssEvolutionTraitRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssEvolutionTraitRegistry.cs"))
    $endlessAbyssEvolutionTraitRegistryJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\endless_abyss.evolution_traits.registry.json"))
    $buffOverflowApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\BuffOverflowApi.cs"))
    $eventScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\EventScripts.cs"))
    $bossScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\BossScripts.cs"))
    $entry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Entry.cs"))
    $wunaScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\WunaScripts.cs"))
    $wunaPassiveService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\WunaPassiveService.cs"))
    $emberAdventureStateService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EmberAdventureStateService.cs"))
    $emberAdventureStateRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EmberAdventureStateRuntime.cs"))
    $rpcEmberAdventureStateCommit = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\RpcEmberAdventureStateCommit.cs"))
    $enemyApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\EnemyApi.cs"))
    $endlessAbyssEnemyInjectionService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssEnemyInjectionService.cs"))
    $runtimeHooks = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\RuntimeHooks.cs"))
    $terriasPerformanceSettingsSource = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasPerformanceSettings.cs"))
    $terriasCombatActionRouter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasCombatActionRouter.cs"))
    $terriasStatusLifecycleRouter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasStatusLifecycleRouter.cs"))
    $terriasCardPresentationRouter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasCardPresentationRouter.cs"))
    $terriasCardPresentationLifecycleBridge = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasCardPresentationLifecycleBridge.cs"))
    $cardPresentationRootResolver = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\CardPresentationRootResolver.cs"))
    $terriasResourcePreloader = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasResourcePreloader.cs"))
    $terriasCombatCardUiWorkloadRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasCombatCardUiWorkloadRuntime.cs"))
    $solarMemoryJourneyApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\SolarMemoryJourneyApi.cs"))
    $polymorphRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\PolymorphRuntime.cs"))
    $projectionRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\ProjectionRuntime.cs"))
    $spiritRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SpiritRuntime.cs"))
    $heartChangeControlRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\HeartChangeControlRuntime.cs"))
    $duskPartnerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\DuskPartnerRuntime.cs"))
    $duskAfterheatRecoveryService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\DuskAfterheatRecoveryService.cs"))
    $starClayDollRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\StarClayDollRuntime.cs"))
    $loneerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\LoneerRuntime.cs"))
    $starScoreRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\StarScoreRuntime.cs"))
    $starScoreHudRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\StarScoreHudRuntime.cs"))
    $loneerService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\LoneerMiracleService.cs"))
    $starStonePouchService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarStonePouchService.cs"))
    $loneerState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\LoneerCombatState.cs"))
    $cardSelectionApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\CardSelectionApi.cs"))
    $starScoreService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreService.cs"))
    $starScoreState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreCombatState.cs"))
    $starScoreNote = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreNote.cs"))
    $starScoreSnapshot = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreDisplaySnapshot.cs"))
    $starScoreCadenceCatalog = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\StarScoreCadenceCatalog.cs"))
    $duskPartnerScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\DuskPartnerScripts.cs"))
    $starClayDollScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\StarClayDollScripts.cs"))
    $projectionScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\ProjectionScripts.cs"))
    $heartChangeScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\HeartChangeScripts.cs"))
    $scriptingSource = [string]::Join("`n", (Get-ChildItem -LiteralPath (Join-Path $RepoRoot "Terrias-Dev\Scripting") -File -Filter "*.cs" | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))
    $solarEventRuntimePath = Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarEventRuntime.cs"
    $battleRewardApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\BattleRewardApi.cs"))
    $battleRewardAdjustmentService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\BattleRewardAdjustmentService.cs"))
    $battleRewardAdjustmentRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\BattleRewardAdjustmentRuntime.cs"))
    $solarMemoryRewardRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryRewardRuntime.cs"))
    $solarMemoryModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryModeRuntime.cs"))
    $solarMemoryMapLifecycleCoordinator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryMapLifecycleCoordinator.cs"))
    $solarMemoryModeEntryRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryModeEntryRuntime.cs"))
    $solarMemoryMapVisualRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryMapVisualRuntime.cs"))
    $solarMemoryMapProjectionRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryMapProjectionRuntime.cs"))
    $solarMemoryBattleExitCoordinator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryBattleExitCoordinator.cs"))
    $solarMemoryBossTransitionCoordinator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryBossTransitionCoordinator.cs"))
    $solarMemorySettlementCoordinator = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemorySettlementCoordinator.cs"))
    $solarMemoryDeckIsolationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryDeckIsolationRuntime.cs"))
    $solarMemoryCombatRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryCombatRuntime.cs"))
    $cardVisualSkinRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\CardVisualSkinRuntime.cs"))
    $polymorphCardFaceRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Visual\PolymorphCardFaceRuntime.cs"))
    $modeChoiceEntryDefinition = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\ModeChoiceEntryDefinition.cs"))
    $modeChoiceEntryRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\ModeChoiceEntryRegistry.cs"))
    $modeChoiceLayoutRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\ModeChoiceLayoutRuntime.cs"))
    $solarMemoryRunLauncher = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryRunLauncher.cs"))
    $solarMemoryContentIsolationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryContentIsolationRuntime.cs"))
    $solarMemoryMapItemAnimationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryMapItemAnimationRuntime.cs"))
    $mapNodeCardArtRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\MapNodeCardArtRuntime.cs"))
    $dimensionShopRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\DimensionShopRuntime.cs"))
    $dimensionShopGameApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\DimensionShopGameApi.cs"))
    $dimensionShopPanel = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\DimensionShopPanel.cs"))
    $dimensionShopNativeSkin = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\DimensionShopNativeSkin.cs"))
    $sharedUiNativeInteraction = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AuraUiShared\AuraUiNativeInteraction.cs"))
    $sharedUiNativeGameItems = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AuraUiShared\AuraUiNativeGameItemAdapter.cs"))
    $sharedUiNativeOverlayVisibility = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AuraUiShared\AuraUiNativeOverlayVisibility.cs"))
    $sharedUiModalHost = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AuraUiShared\AuraUiModalHost.cs"))
    $dimensionShopService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\DimensionShopService.cs"))
    $dimensionShopConfigSource = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\DimensionShopConfig.cs"))
    $mapNodeCardArtRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeCardArtRegistry.cs"))
    $visualRegistry = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\VisualRegistry.cs"))
    $visualRegistryJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\visual.registry.json"))
    $mapItemApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\MapItemApi.cs"))
    $mapNodeTextureFitService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeTextureFitService.cs"))
    $terriasHardTagRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\TerriasHardTagRuntime.cs"))
    $solarMemoryStarterDeckRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"))
    $endlessSeaIntroBoardRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessSeaIntroBoardRuntime.cs"))
    $endlessSeaRunLauncher = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessSeaRunLauncher.cs"))
    $endlessSeaSaveCacheRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessSeaSaveCacheRuntime.cs"))
    $endlessSeaModeRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessSeaModeRuntime.cs"))
    $endlessSeaMapViewPresenter = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\EndlessSeaMapViewPresenter.cs"))
    $endlessSeaNetworkSync = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\EndlessSeaNetworkSync.cs"))
    $endlessAbyssEvacuationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessAbyssEvacuationRuntime.cs"))
    $endlessAbyssEvacuationButtonRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\EndlessAbyssEvacuationButtonRuntime.cs"))
    $endlessAbyssEvacuationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssEvacuationService.cs"))
    $endlessAbyssEvacuationRpc = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\RpcEndlessAbyssEvacuation.cs"))
    $endlessAbyssSettlementBarrierRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessAbyssSettlementBarrierRuntime.cs"))
    $endlessAbyssSettlementBarrierView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\EndlessAbyssSettlementBarrierView.cs"))
    $endlessAbyssSettlementBarrierRpc = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\RpcEndlessAbyssSettlementBarrier.cs"))
    $terriasNetworkRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\TerriasNetworkRuntime.cs"))
    $endlessSeaFloorPlanner = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaFloorPlanner.cs"))
    $endlessSeaMapBuilder = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaMapBuilder.cs"))
    $endlessSeaMapProjectionService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaMapProjectionService.cs"))
    $endlessSeaSelectableNodeDeckPlanner = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaSelectableNodeDeckPlanner.cs"))
    $terriasSkillCgRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Features\SkillCg\TerriasSkillCgRuntime.cs"))
    $auraCgRuntime = Read-SourceTreeText $RepoRoot "AuraCgShared"
    $endlessSeaStarterDeckCatalog = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaStarterDeckCatalog.cs"))
    $endlessSeaRichTextSanitizer = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaRichTextSanitizer.cs"))
    $endlessSeaOriginService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaOriginService.cs"))
    $originCapService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\OriginCapService.cs"))
    $originMilestoneService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\OriginMilestoneService.cs"))
    $originMilestoneRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\OriginMilestoneRuntime.cs"))
    $blessingScripts = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Scripting\BlessingScripts.cs"))
    $endlessSeaCardAffixRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessSeaCardAffixRuntime.cs"))
    $endlessSeaCardAffixService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaCardAffixService.cs"))
    $endlessSeaCombatRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\EndlessSeaCombatRuntime.cs"))
    $endlessAbyssConfig = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssConfig.cs"))
    $endlessAbyssEnemyScaling = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssEnemyScalingService.cs"))
    $endlessAbyssConfigJson = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\endless_abyss.config.json"))
    $endlessAbyssCurseService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssCurseService.cs"))
    $endlessAbyssGazePressureService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssGazePressureService.cs"))
    $endlessAbyssRewardService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssRewardService.cs"))
    $endlessAbyssRewardPoolService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssRewardPoolService.cs"))
    $endlessAbyssMilestoneRewardService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssMilestoneRewardService.cs"))
    $endlessAbyssRunLedger = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessAbyssRunLedger.cs"))
    $morningStarDimmedService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\MorningStarDimmedService.cs"))
    $playerPowerApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\PlayerPowerApi.cs"))
    $endlessAbyssMilestoneRewardPanel = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\EndlessAbyssMilestoneRewardPanel.cs"))
    $endlessAbyssShockPanel = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\EndlessAbyssShockPanel.cs"))
    $endlessSeaRunStateStore = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\EndlessSeaRunStateStore.cs"))
    $modeChoiceSaveCacheApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\ModeChoiceSaveCacheApi.cs"))
    $solarMemorySetupFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"))
    $solarMemoryBlessingPickerRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"))
    $solarMemoryPreparationRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryPreparationRuntime.cs"))
    $solarMemoryPlayerSetupState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\SolarMemoryPlayerSetupState.cs"))
    $dialogueFlowRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\DialogueFlowRuntime.cs"))
    $dialogueFlowService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\DialogueFlowService.cs"))
    $solarMemoryStoryGateService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryStoryGateService.cs"))
    $solarMemoryFlowApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\SolarMemoryFlowApi.cs"))
    $solarMemoryRoleCommitApi = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\GameApi\SolarMemoryRoleCommitApi.cs"))
    $solarMemoryRoleCommit = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Network\RpcSolarMemoryRoleCommit.cs"))
    $dirtyState = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Infrastructure\TerriasDirtyState.cs"))
    $terriasUiSafety = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasUiSafety.cs"))
    $terriasUiBuilder = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasUiBuilder.cs"))
    $terriasModalHost = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasModalHost.cs"))
    $terriasUiLifetimeScope = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasUiLifetimeScope.cs"))
    $terriasUiPool = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasUiPool.cs"))
    $terriasUiSprites = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\TerriasUiSprites.cs"))
    $starScoreHudAssets = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\StarScoreHudAssets.cs"))
    $starScoreHudView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\StarScoreHudView.cs"))
    $starScoreHudHoverProbe = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\StarScoreHudHoverProbe.cs"))
    $starScoreHudTooltipView = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Hooks\Ui\StarScoreHudTooltipView.cs"))
    $terriasProject = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Terrias.Dll.csproj"))
    $audioArbiterRuntime = Read-SourceTreeText $RepoRoot "AudioArbiterShared"
    $audioProviderResolver = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AudioArbiterShared\AudioProviderResolver.cs"))
    $audioNetworkRuntime = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "AudioArbiterShared\AudioNetworkRuntime.cs"))
    $battleBgmArbiterRuntime = Read-SourceTreeText $RepoRoot "BattleBgmArbiterShared"
    $modConfig = Get-Content -LiteralPath (Join-Path $RepoRoot "Terrias\ModConfig.json") -Raw | ConvertFrom-Json
    $solarMemoryMapNodePoolFactory = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryMapNodePoolFactory.cs"))
    $solarMemoryMapNodePoolApplier = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryMapNodePoolApplier.cs"))
    $solarMemoryFixedNodeSpec = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryFixedNodeSpec.cs"))
    $solarMemoryMapSyncRepairService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryMapSyncRepairService.cs"))
    $solarMemoryContentIsolationService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\SolarMemoryContentIsolationService.cs"))
    $mapNodeSafetyService = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias-Dev\Mechanics\MapNodeSafetyService.cs"))
    $mapData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\Map\terrias.csv"))
    $mapText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\Map\terrias.csv"))
    $levelData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\Level\terrias.csv"))
    $enemyData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\Enemy\terrias.csv"))
    $enemyText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\Enemy\terrias.csv"))
    $enemyCardData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\EnemyCard\terrias.csv"))
    $enemyCardText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\EnemyCard\terrias.csv"))
    $buffDataPath = Join-Path $RepoRoot "Terrias\Data\Buff\terrias.csv"
    $buffData = [System.IO.File]::ReadAllText($buffDataPath)
    $buffRows = Import-Csv -LiteralPath $buffDataPath
    $scorchingCanopyBuffRow = $buffRows | Where-Object { $_.Id -eq "scorching_canopy" } | Select-Object -First 1
    $samsaraGardenBuffRow = $buffRows | Where-Object { $_.Id -eq "samsara_garden" } | Select-Object -First 1
    $buffText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\Buff\terrias.csv"))
    $enchTagData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\EnchTag\terrias.csv"))
    $keywordText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\KeyWordsDic\terrias.csv"))
    $eventData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\EventList\terrias.csv"))
    $eventText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\EventList\terrias.csv"))
    $dialogueData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\Dialogue\terrias.csv"))
    $solarMemoryRoleData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\RoleData\solar_memory.csv"))
    $loneerRoleData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\RoleData\loneer.csv"))
    $blessingData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\Blessing\terrias.csv"))
    $originMilestoneBlessingRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Data\Blessing\terrias.csv") |
        Where-Object { $_.Id -like "origin_*_50" }
    $partnerData = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Data\Partner\terrias.csv"))
    $cardDataPath = Join-Path $RepoRoot "Terrias\Data\Card\terrias.csv"
    $cardData = [System.IO.File]::ReadAllText($cardDataPath)
    $cardTextPath = Join-Path $RepoRoot "Terrias\Text\Card\terrias.csv"
    $cardText = [System.IO.File]::ReadAllText($cardTextPath)
    $loneerCareerText = [System.IO.File]::ReadAllText((Join-Path $RepoRoot "Terrias\Text\Career\loneer.csv"))
    $duskTraitTextRow = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Text\Buff\terrias.csv") |
        Where-Object { $_.Id -eq "dusk_afterheat_recovery_trait" } |
        Select-Object -First 1
    $duskPartnerTextRow = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Text\Partner\terrias.csv") |
        Where-Object { $_.Id -eq "dusk" } |
        Select-Object -First 1
    $duskBlessingTextRow = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Text\Blessing\terrias.csv") |
        Where-Object { $_.Id -eq "dusk_afterheat_recovery" } |
        Select-Object -First 1
    $cardRows = Import-Csv -LiteralPath $cardDataPath
    $cardTextRows = Import-Csv -LiteralPath $cardTextPath
    $sparkRow = $cardRows | Where-Object { $_.Id -eq "spark" } | Select-Object -First 1
    $courtPurificationRow = $cardRows | Where-Object { $_.Id -eq "afterglow_omen_card" } | Select-Object -First 1
    $scorchingCanopyTextRow = $cardTextRows | Where-Object { $_.Id -eq "scorching_canopy_card" } | Select-Object -First 1
    $drawFlameTextRow = $cardTextRows | Where-Object { $_.Id -eq "draw_flame" } | Select-Object -First 1
    $hardTextRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Text\Hard\terrias.csv")
    $scorchedWorldTextRow = $hardTextRows | Where-Object { $_.Id -eq "terrias_scorched_world" } | Select-Object -First 1
    $samsaraGardenTextRow = $hardTextRows | Where-Object { $_.Id -eq "terrias_samsara_garden" } | Select-Object -First 1
    $hardRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Data\Hard\terrias.csv")
    $samsaraGardenHardRow = $hardRows | Where-Object { $_.Id -eq "terrias_samsara_garden" } | Select-Object -First 1
    $utf8 = [System.Text.Encoding]::UTF8
    Assert-True ($sparkRow.Tag -eq $utf8.GetString([Convert]::FromBase64String("55m95puc"))) "Spark must carry the White Radiance tag."
    Assert-True ($courtPurificationRow.Tag -eq $utf8.GetString([Convert]::FromBase64String("UmV0YWluLOeZveabnCxBbm5paGlsYXRpb24="))) "Court Purification must use Retain, White Radiance, and Annihilation without Burnout."
    Assert-True ($scorchingCanopyTextRow.Description -eq $utf8.GetString([Convert]::FromBase64String("6ZO65LiKMeWxgntUZXJyaWFzX3RlcnJpYXNfc2NvcmNoaW5nX2Nhbm9weX3lnLrlnLDvvIzlhajkvZPojrflvpcy5bGCe2J1ZmZfYnVybn3jgII="))) "Scorching Canopy must use the field-placement description."
    Assert-True ($drawFlameTextRow.Description -eq $utf8.GetString([Convert]::FromBase64String("5ZC45pS25Lu75oSP55uu5qCH55qE5omA5pyJe2J1ZmZfYnVybn3vvIzovazljJbkuLrnrYnph4/nmoR7VGVycmlhc190ZXJyaWFzX2dhdGhlcmVkX2ZsYW1lfeOAgg=="))) "Draw Flame must use the conversion description."
    $scorchedWorldDescription = $utf8.GetString([Convert]::FromBase64String("5oiY5paX5byA5aeL5pe277yM5Li65Zy65LiK6ZO65LiK6YCJ5oup5bGC5pWw55qEe1RlcnJpYXNfdGVycmlhc19zY29yY2hpbmdfY2Fub3B5feOAgg=="))
    $samsaraGardenDescription = $utf8.GetString([Convert]::FromBase64String("5oiY5paX5byA5aeL5pe277yM5Li65Zy65LiK6ZO65LiK6YCJ5oup5bGC5pWw55qEe1RlcnJpYXNfdGVycmlhc19zYW1zYXJhX2dhcmRlbn3jgII="))
    $eternalGardenName = $utf8.GetString([Convert]::FromBase64String("5rC45oGS6Iqx5Zut"))
    Assert-True ($scorchedWorldTextRow.Description -eq $scorchedWorldDescription) "Scorched World must use the concise combat-start field description."
    Assert-True ($samsaraGardenTextRow.Name -eq $eternalGardenName) "The Samsara Garden difficulty tag must be displayed as Eternal Garden."
    Assert-True ($samsaraGardenTextRow.Description -eq $samsaraGardenDescription) "Eternal Garden must use the concise combat-start field description."
    Assert-True ($samsaraGardenHardRow.Belong -eq $eternalGardenName) "The Samsara Garden difficulty row must belong to Eternal Garden."
    foreach ($duskTextRow in @($duskTraitTextRow, $duskPartnerTextRow, $duskBlessingTextRow)) {
        Assert-True ($null -ne $duskTextRow) "Every Dusk passive text surface must keep its localized row."
        $duskDescriptions = @($duskTextRow.Description, $duskTextRow.Passive1)
        Assert-True (($duskDescriptions -join " ").Contains("1/3")) "Every Dusk passive text surface must describe the one-third conversion."
        Assert-True (($duskDescriptions -join " ").Contains("{Terrias_terrias_gathered_flame}")) "Every Dusk passive text surface must mention Gathered Flame."
    }

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
    Assert-True $executorApi.Contains("public static void ActivateField") "ExecutorApi must expose field activation without a player-carrier buff."
    Assert-True $executorApi.Contains("public static bool TryClearActiveField") "ExecutorApi must expose an explicit field clear interface."
    Assert-True $terriasFieldId.Contains("public enum TerriasFieldId") "TerriasFieldId must define enum-like field ids."
    Assert-True $terriasFieldId.Contains("ScorchingCanopy") "TerriasFieldId must include ScorchingCanopy."
    Assert-True $terriasFieldId.Contains("SamsaraGarden") "TerriasFieldId must include SamsaraGarden."
    Assert-True $fieldApi.Contains("ActiveFieldIdKey") "FieldApi must keep one battle-wide active field id."
    Assert-True $fieldApi.Contains("ActiveFieldStacksKey") "FieldApi must keep battle-wide field stacks outside player status buffs."
    Assert-True $fieldApi.Contains("MaxStacksFor") "FieldApi must clamp field stacks through the configured buff upper bound."
    Assert-True $fieldEffectRegistry.Contains("WarmupConfigCache") "Field effect registry must preload Buff runtime data once during initialization."
    Assert-True $fieldEffectRegistry.Contains("snapshot.TryGet(DataType.Buff.ToString(), definition.BuffId") "Field stack caps must resolve from one immutable shared snapshot during epoch-aware registry warmup."
    Assert-True $fieldEffectRegistry.Contains("FieldEffectRuntimeSpec") "Field effect registry must expose precomputed runtime specs for field hot paths and HUD."
    Assert-True $fieldEffectRegistry.Contains("description = description.Description();") "Field descriptions must resolve localized Buff placeholders during registry warmup."
    Assert-True $fieldEffectRegistry.Contains("public string HudIconPath") "Field effect definitions must own dedicated HUD icon paths."
    Assert-True $fieldEffectRegistry.Contains('hudIconPath: "Mods/Terrias/ModResource/Images/Buff/Area/\u707c\u70ed\u5929\u5e55"') "Scorching Canopy must register the renamed 64x64 field HUD icon."
    Assert-True $fieldEffectRegistry.Contains('hudIconPath: "Mods/Terrias/ModResource/Images/Buff/Area/\u8f6e\u56de\u82b1\u5ead"') "Garden of Samsara must register its 64x64 field HUD icon."
    Assert-True $fieldEffectRegistry.Contains("maxVisualTier: 4") "Garden of Samsara stack 5 must reuse visual tier 4."
    Assert-True (-not $fieldEffectRegistry.Contains('DictionaryUtil.Get(data, "Icon")')) "Field HUD icons must not depend on generic Buff.Icon data."
    Assert-True $fieldApi.Contains("CombatVarApi.AddInt(ActiveFieldEpochKey, 1);") "Field state changes must advance a shared epoch."
    Assert-True $fieldApi.Contains("SyncFieldStacks(ScriptExecutor? executor, TerriasFieldId field)") "Field sync must expose the enum-based overload."
    Assert-True $fieldApi.Contains("TryClearActiveField") "FieldApi must clear fields only through an explicit interface."
    Assert-True $fieldApi.Contains("IsFieldBuffId") "FieldApi must classify buff rows that are actually field buffs."
    Assert-True $fieldApi.Contains("RemoveFieldBuffCarrier") "FieldApi must remove field buff carriers from player/enemy status lists."
    Assert-True $fieldApi.Contains("IsAuthoritativeFieldWriter") "FieldApi must gate field writes to the host in multiplayer."
    Assert-True $fieldApi.Contains("ApplyNetworkSnapshot") "FieldApi must apply host-authored field snapshots on non-host clients."
    Assert-True $fieldApi.Contains("TryGetActiveField") "FieldApi must expose allocation-free active field reads for AddBuff hot paths."
    Assert-True $fieldApi.Contains("HasActiveBuffAddedPolicy") "FieldApi must cache active field AddBuff policy flags."
    Assert-True $fieldApi.Contains("FieldNetworkSync.RequestActivate") "Non-host field activation must request host authority instead of mutating local TempVarsMap."
    Assert-True $fieldEffectRegistry.Contains("FieldEffectDefinition") "Field effects must be backed by a registry definition."
    Assert-True $fieldEffectRegistry.Contains("HasRoundStartHandler") "Field definitions must declare whether round-start handling is implemented."
    Assert-True $fieldEffectRegistry.Contains("HasBuffAddedPolicy") "Field definitions must declare whether AddBuff lifecycle policy is implemented."
    Assert-True $fieldNetworkSync.Contains("FieldStateSnapshot") "Field multiplayer sync must use a lightweight indexed snapshot."
    Assert-True $fieldNetworkSync.Contains("RpcFieldStateRequest") "Field multiplayer sync must support non-host request messages."
    Assert-True $fieldNetworkSync.Contains("ITerriasServerBoundRpcCommand") "Field request RPC must bind sender through authority runtime."
    Assert-True $fieldNetworkSync.Contains("AuraAuthoritativeSyncRuntime.RegisterDomain") "Field sync must use the shared authoritative sync domain service."
    Assert-True $fieldNetworkSync.Contains("SyncDomain.TryBeginSnapshotRequest") "Field snapshot requests must be coalesced through the shared sync domain."
    Assert-True $fieldNetworkSync.Contains("SyncDomain.TryClaimToken") "Field request idempotency must be owned by the shared sync domain."
    Assert-True $fieldNetworkSync.Contains("AcceptRemoteSnapshotSession") "Field snapshots must use shared host-session freshness checks."
    Assert-True (-not $fieldNetworkSync.Contains("requestBattleSerial != CurrentBattleSerial")) "Field requests must not reject valid clients by comparing local-only battle serials."
    Assert-True $auraAuthoritativeSyncRuntime.Contains("public static class AuraAuthoritativeSyncRuntime") "Shared core must provide semantic-free authoritative sync foundations."
    Assert-True $auraAuthoritativeSyncRuntime.Contains("TryBeginSnapshotRequest") "Shared authoritative sync must coalesce snapshot requests."
    Assert-True $auraAuthoritativeSyncRuntime.Contains("TryClaimToken") "Shared authoritative sync must own bounded command token de-duplication."
    Assert-True $fieldRuntime.Contains("TerriasHookTargets.FightPlayerTurnInit") "Field runtime must resolve field effects from the round-start hook."
    Assert-True $fieldRuntime.Contains("FieldApi.ResolveRoundStart") "Field runtime must delegate round-start field settlement to FieldApi."
    Assert-True $fieldRuntime.Contains("FightOpening = OnFightOpening") "Field runtime must resolve all opening sources after native FightStart initialization."
    Assert-True $fieldRuntime.Contains("FieldStartCoordinator.ResolveAndCommit") "Field runtime must delegate opening field resolution to the coordinator."
    Assert-True $fieldRuntime.Contains("FieldNetworkSync.RequestSnapshot") "Non-host field runtime must request host-authored field snapshots."
    Assert-True $fieldRuntime.Contains("FieldApi.CanResolveFieldEffects") "Non-host field runtime must skip local field settlement."
    Assert-True $runtimeHooks.Contains("FieldEffectRegistry.WarmupConfigCache") "RuntimeHooks must preload field Buff config before field runtime and HUD use."
    Assert-True ($fieldEffectRegistry.Contains("AuraGameDataCatalogRuntime.SnapshotChanged += OnCatalogSnapshotChanged") -and $fieldEffectRegistry.Contains("snapshot.Version.NativeReady") -and $fieldEffectRegistry.Contains("runtimeSpecsEpoch")) "Field config cache must wait for a native-ready catalog and rebuild by catalog epoch."
    Assert-True ($fieldEffectRegistry.Contains("public static event Action? Changed") -and $fieldRuntime.Contains("FieldEffectRegistry.Changed += OnFieldEffectConfigChanged") -and $fieldRuntime.Contains('FieldBuffHudRuntime.RequestRefresh("FieldEffectRegistry.Changed")')) "A published field config epoch must refresh an already active field HUD."
    Assert-True $fieldBuffHudRuntime.Contains('TerriasFrameScheduler.RunOnceNextFrame("FieldBuffHud.Refresh"') "Field HUD refresh must be deferred through the frame scheduler."
    Assert-True $fieldBuffHudRuntime.Contains("FieldNetworkSync.RequestSnapshot") "Field HUD must request a repair snapshot when a non-host client has no local field state."
    Assert-True $fieldBuffHudView.Contains("FieldBuffHudTooltipView.Create") "Field HUD must create a hover tooltip."
    Assert-True $fieldBuffHudView.Contains("FieldBuffHudHoverProbe") "Field HUD must use pointer events for hover."
    Assert-True $fieldBuffHudView.Contains("private const float RootWidth = 164f") "Field HUD must restore the approved field status panel width."
    Assert-True $fieldBuffHudView.Contains("private const float RootHeight = 128f") "Field HUD must use the compact vertical panel height."
    Assert-True $fieldBuffHudView.Contains("private const float IconSize = 64f") "Field HUD must preserve a fixed 64x64 field icon."
    Assert-True $fieldBuffHudView.Contains("new Vector2(RootWidth, RootHeight)") "Field HUD root must use the approved panel dimensions."
    Assert-True $fieldBuffHudView.Contains("ConfigureTmpText") "Field HUD labels must use the shared game-font TMP component."
    Assert-True $fieldBuffHudView.Contains('stackText.text = currentSnapshot.Stacks + "/" + currentSnapshot.MaxStacks;') "Field HUD must show current and maximum stacks below the icon."
    Assert-True $fieldBuffHudView.Contains("stackText.outlineWidth") "Field HUD stack text must remain legible against the panel."
    Assert-True $fieldBuffHudView.Contains('ApplyPanelImage(gameObject, TerriasUiSprites.Panel("[FieldBuffHud]")') "Field HUD must restore its outer status-panel background."
    Assert-True (-not $fieldBuffHudView.Contains("ApplyLabelImage")) "Field HUD must not draw stack or name label backgrounds."
    Assert-True $fieldBuffHudView.Contains('"NameSection"') "Field HUD must use a darker integrated name region."
    Assert-True $fieldBuffHudView.Contains("private const float DividerInset = 12f") "Field HUD name divider must remain inset from the outer border."
    Assert-True ($fieldBuffHudView.Contains('"Divider"') -and $fieldBuffHudView.Contains("dividerImage.raycastTarget = false")) "Field HUD must separate the name region with a non-blocking bright line."
    Assert-True $fieldBuffHudTooltipView.Contains("DescriptionHeight = Height * 0.5f") "Field HUD tooltip body must reserve half of the floating panel height."
    Assert-True $fieldBuffHudTooltipView.Contains("21f, FontStyles.Normal") "Field HUD tooltip body must use the enlarged approved font size."
    Assert-True (-not $fieldBuffHudView.Contains('"NameBar"')) "Field HUD must use an integrated name region instead of a raised label bar."
    Assert-True $fieldBuffHudView.Contains("group.blocksRaycasts = true") "Field HUD must allow its local hotspot to receive hover raycasts."
    Assert-True $fieldBuffHudView.Contains("FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).HudIconPathForStacks(snapshot.Stacks)") "Field HUD must render its icon through the visual-tier fallback."
    Assert-True $fieldBuffHudView.Contains("FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).DisplayName") "Field HUD must restore the localized field name in its lower section."
    Assert-True $fieldBuffHudTooltipView.Contains("FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).DisplayName") "Field HUD tooltip must keep the localized field name on hover."
    Assert-True $fieldBuffHudView.Contains("MultiplayerAvoidanceAt1080 = 150f") "Field HUD must use the final approved 150/1080 top offset."
    Assert-True $fieldBuffHudView.Contains("MultiplayerAvoidanceAt1080 / 1080f") "Field HUD must apply its top offset responsively."
    Assert-True $fieldBuffHudView.Contains("new Vector2(0f, -avoidance)") "Field HUD must use only the approved responsive avoidance offset."
    Assert-True (-not $fieldBuffHudView.Contains("BaselineTopOffset")) "Field HUD must not retain the obsolete fixed baseline offset."
    Assert-True $fieldBuffHudHoverProbe.Contains("IPointerEnterHandler") "Field HUD hover probe must use Unity pointer enter events."
    Assert-True $fieldBuffHudHoverProbe.Contains("IPointerExitHandler") "Field HUD hover probe must use Unity pointer exit events."
    Assert-True $fieldBuffHudTooltipView.Contains("FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).Description") "Field HUD tooltip must use the prewarmed Buff description cache."
    Assert-True (-not $fieldBuffHudTooltipView.Contains('Localize("Description")')) "Field HUD tooltip must not query Buff CSV on hover."
    Assert-True (-not $fieldBuffHudTooltipView.Contains("DataType.Buff")) "Field HUD tooltip must not read field display data directly from the Buff row."
    Assert-True ($projectionEffectContext.Contains("autoConsumableCacheEpoch") -and $projectionEffectContext.Contains("snapshot.Version.NativeReady")) "Projection auto-consumable classification must not cache pre-ready misses and must follow catalog epochs."
    Assert-True ($morningStarOvertureService.Contains("compositionPoolEpoch") -and $morningStarOvertureService.Contains("snapshot.Version.NativeReady")) "Morning Star composition pools must rebuild after game-data catalog publication."
    Assert-True ($endlessAbyssCurseService.Contains("randomCursePoolEpoch") -and $endlessAbyssRewardPoolService.Contains("cardPoolCacheEpoch")) "Endless Abyss derived card pools must follow game-data catalog epochs."
    Assert-True $dimensionShopService.Contains("AuraGameDataHostApi.IsNativeCatalogReady") "Dimension shop must not persist a run-wide empty product pool before the native catalog is ready."
    Assert-True $fieldBuffHudTooltipView.Contains("group.blocksRaycasts = false") "Field HUD tooltip must not block battle controls."
    Assert-True $fieldStartCoordinator.Contains("DifficultyPool = 100") "Field coordinator must resolve the difficulty pool first."
    Assert-True $fieldStartCoordinator.Contains("Blessing = 200") "Field coordinator must resolve blessings second."
    Assert-True $fieldStartCoordinator.Contains("Relic = 300") "Field coordinator must resolve relics third."
    Assert-True $fieldStartCoordinator.Contains("Other = 400") "Field coordinator must resolve other opening sources last."
    Assert-True $fieldStartCoordinator.Contains("field == grant.Field ? stacks + grant.Stacks : grant.Stacks") "Field coordinator must add same-type grants and replace different fields."
    Assert-True $fieldStartCoordinator.Contains("FieldApi.CommitOpeningField") "Field coordinator must commit only the final opening field."
    Assert-True $fieldStartCoordinator.Contains("TryClaimBattleOperation") "Field coordination must be idempotent within a battle session."
    Assert-True $difficultyFieldPoolService.Contains("TerriasHardTagIds.ScorchedWorld") "Difficulty field pool must include Scorched World."
    Assert-True $difficultyFieldPoolService.Contains("TerriasHardTagIds.SamsaraGarden") "Difficulty field pool must include Garden of Samsara."
    Assert-True $difficultyFieldPoolService.Contains("UnityEngine.Random.Range(0, candidates.Count)") "Difficulty field pool must draw each distinct field type with equal probability."
    Assert-True $relicFieldStartSourceService.Contains('"blazing_crown_heart"') "Blazing Crown Heart must register its field grant through the relic provider."
    Assert-True $relicApi.Contains("public static bool HasRelic") "Relic ownership lookup must be isolated in GameApi."
    Assert-True $familiarBlessingEffectRuntime.Contains('SelectedEffects("CombatStartField")') "Blessing field grants must register through the opening coordinator."
    Assert-True $fieldRuntime.Contains('RunOpeningStep(') "Independent field-opening actions must be isolated so one failure cannot block final field submission."
    Assert-True $relicOpeningEffectService.Contains("RelicApi.HasRelic") "Blazing Crown Heart's non-field opening effects must replay against rebuilt combat status."
    Assert-True $relicOpeningEffectService.Contains("TryClaimBattleOperation") "Blazing Crown Heart's non-field opening effects must be battle-idempotent."
    Assert-True $fieldEffectHandlers.Contains("TriggerScorchingCanopyRoundStart") "Scorching Canopy field effect must live outside carrier buff scripts."
    Assert-True $fieldEffectHandlers.Contains("FieldRoundStartContext") "All round-start fields must share one field processing context."
    Assert-True $fieldEffectHandlers.Contains("ApplyToAllCombatants") "All round-start fields must use the shared all-combatant processor."
    Assert-True $fieldEffectHandlers.Contains("ExecutorApi.AllCombatTargets(executor, includeSelf: true)") "Field settlement must collect all combatants in one target pass."
    Assert-True $fieldEffectHandlers.Contains("target.AddBuff(TerriasIds.Burn, count);") "Scorching Canopy field effect must apply burn directly to combat statuses."
    Assert-True $fieldEffectHandlers.Contains("HandleBuffAdded") "Active field definitions must own StatusManager.AddBuff lifecycle policies."
    Assert-True $fieldEffectHandlers.Contains("BuffOverflowApi.HandleBurnOverflow") "Scorching Canopy must provide Burn overflow conversion as a field-owned policy."
    Assert-True $fieldEffectHandlers.Contains("TriggerSamsaraGardenRoundStart") "Garden of Samsara must have a registered round-start handler."
    Assert-True $fieldEffectHandlers.Contains("StatusApi.TryHeal(target, heal)") "Garden of Samsara must heal each combatant through the native status-targeted wrapper."
    Assert-True $statusApi.Contains("public static bool TryHeal(IStatusManager? target, int amount)") "StatusApi.TryHeal must not require a borrowed ScriptExecutor."
    Assert-True $statusApi.Contains("target!.Heal(amount, NativeHealDamageType);") "StatusApi.TryHeal must call the native status Heal API."
    Assert-True (-not $statusApi.Contains("TargetApi.SetStatusForTarget")) "StatusApi.TryHeal must not mutate ScriptExecutor.Object target state."
    Assert-True (-not $statusApi.Contains("executor.ChangeHp(amount.ToString())")) "StatusApi.TryHeal must not use the ForEachObject ChangeHp path."
    Assert-True $fieldEffectHandlers.Contains("target.AddBuff(TerriasIds.Rebirth, 30)") "Garden of Samsara must grant 30 Rebirth every round while capped."
    Assert-True $fieldEffectHandlers.Contains("StatusApi.IsAlive(target)") "Garden of Samsara must skip dead combatants."
    Assert-True (-not $fieldEffectHandlers.Contains("executor.AddBuff(TerriasIds.Burn")) "Scorching Canopy field effect must not use ScriptExecutor.AddBuff because round-start hook executors may lack dataConfig Id."
    Assert-True (-not $buffScripts.Contains('TryAddEvent(self, "StartRound"')) "Scorching Canopy carrier buff must not own round-start settlement."
    Assert-True $buffScripts.Contains("ExecutorApi.ActivateField(self, TerriasFieldId.ScorchingCanopy") "Scorching Canopy carrier apply must convert legacy carrier adds into field state."
    Assert-True (-not $buffScripts.Contains("TryConsumePendingFieldBuffCarrier")) "Scorching Canopy carrier apply must not depend on global AddBuff redirection state."
    Assert-True $buffScripts.Contains("self.RemoveBuff(TerriasIds.ScorchingCanopy);") "Scorching Canopy legacy carrier apply must immediately remove the player-mounted carrier buff."
    Assert-True $buffData.Contains('"scorching_canopy","","CS.Terrias.Dll.Scripting.BuffScripts.Apply(self, ""scorching_canopy"");') "Scorching Canopy buff data row is missing."
    $fieldBuffTypeText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("5Zy65Zyw"))
    Assert-True ($null -ne $scorchingCanopyBuffRow) "Scorching Canopy buff data row must remain importable."
    Assert-True ([string]::IsNullOrWhiteSpace($scorchingCanopyBuffRow.Icon)) "Scorching Canopy must not expose its field-only icon through generic Buff.Icon."
    Assert-True ($scorchingCanopyBuffRow.Type -eq $fieldBuffTypeText) "Scorching Canopy must remain typed as a field buff so native random positive/negative/ability pools do not select it."
    Assert-True ($null -ne $samsaraGardenBuffRow) "Garden of Samsara buff data row must remain importable."
    Assert-True ([string]::IsNullOrWhiteSpace($samsaraGardenBuffRow.Icon)) "Garden of Samsara must not expose its field-only icon through generic Buff.Icon."
    Assert-True ($samsaraGardenBuffRow.Type -eq $fieldBuffTypeText) "Garden of Samsara must remain typed as a field buff."
    Assert-True ($samsaraGardenBuffRow.UpperBound -eq "5") "Garden of Samsara field gameplay cap must remain 5."
    Assert-True ($null -ne $samsaraGardenHardRow -and $samsaraGardenHardRow.MaxCount -eq "4") "Garden of Samsara difficulty selection must remain capped at 4 stacks."
    $fieldHudIconName = $utf8.GetString([Convert]::FromBase64String("54G854Ot5aSp5bmVLnBuZw=="))
    $oldFieldHudIconName = $utf8.GetString([Convert]::FromBase64String("54K954G85aSp5bmVLnBuZw=="))
    $fieldHudIconDirectory = Join-Path $RepoRoot "Terrias\ModResource\Images\Buff\Area"
    $fieldHudIconPath = Join-Path $fieldHudIconDirectory $fieldHudIconName
    Assert-True (Test-Path -LiteralPath $fieldHudIconPath -PathType Leaf) "Scorching Canopy's renamed field HUD icon asset is missing."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fieldHudIconDirectory $oldFieldHudIconName) -PathType Leaf)) "The old Scorching Canopy field HUD icon filename must not remain."
    $fieldHudIconBytes = [System.IO.File]::ReadAllBytes($fieldHudIconPath)
    Assert-True ($fieldHudIconBytes.Length -ge 24) "Scorching Canopy's field HUD icon must be a valid PNG header."
    $fieldHudIconWidth = ([int]$fieldHudIconBytes[16] -shl 24) -bor ([int]$fieldHudIconBytes[17] -shl 16) -bor ([int]$fieldHudIconBytes[18] -shl 8) -bor [int]$fieldHudIconBytes[19]
    $fieldHudIconHeight = ([int]$fieldHudIconBytes[20] -shl 24) -bor ([int]$fieldHudIconBytes[21] -shl 16) -bor ([int]$fieldHudIconBytes[22] -shl 8) -bor [int]$fieldHudIconBytes[23]
    Assert-True ($fieldHudIconWidth -eq 64 -and $fieldHudIconHeight -eq 64) "Scorching Canopy's dedicated field HUD icon must remain 64x64."
    $samsaraGardenIconPath = Join-Path $fieldHudIconDirectory ($utf8.GetString([Convert]::FromBase64String("6L2u5Zue6Iqx5bqtLnBuZw==")))
    Assert-True (Test-Path -LiteralPath $samsaraGardenIconPath -PathType Leaf) "Garden of Samsara's field HUD icon asset is missing."
    $samsaraGardenIconBytes = [System.IO.File]::ReadAllBytes($samsaraGardenIconPath)
    $samsaraGardenIconWidth = ([int]$samsaraGardenIconBytes[16] -shl 24) -bor ([int]$samsaraGardenIconBytes[17] -shl 16) -bor ([int]$samsaraGardenIconBytes[18] -shl 8) -bor [int]$samsaraGardenIconBytes[19]
    $samsaraGardenIconHeight = ([int]$samsaraGardenIconBytes[20] -shl 24) -bor ([int]$samsaraGardenIconBytes[21] -shl 16) -bor ([int]$samsaraGardenIconBytes[22] -shl 8) -bor [int]$samsaraGardenIconBytes[23]
    Assert-True ($samsaraGardenIconWidth -eq 64 -and $samsaraGardenIconHeight -eq 64) "Garden of Samsara's dedicated field HUD icon must remain 64x64."
    Assert-True $endlessAbyssRewardService.Contains("EndlessAbyssEvolutionTraitRegistry.EvolutionTraitBuffIds()") "Endless Abyss evolution rewards must read the advanced trait pool from the registry."
    Assert-True (-not $endlessAbyssRewardService.Contains("EvolutionTraitPool")) "Endless Abyss evolution traits must not use the old hardcoded pool."
    Assert-True $entry.Contains("EndlessAbyssEvolutionTraitRegistry.Load(modConfig)") "Terrias entry must load the evolution trait registry during initialization."
    Assert-True $endlessAbyssEvolutionTraitRegistry.Contains("TerriasIds.EndlessAbyssEvolutionTraitPoolId") "Evolution trait registry must resolve the named advanced trait pool."
    Assert-True $endlessAbyssEvolutionTraitRegistryJson.Contains('"SpecialBuff_Law:Supreme"') "Evolution trait registry must include Liquid Body."
    Assert-True $endlessAbyssEvolutionTraitRegistryJson.Contains('"SpecialBuff_Transcendent"') "Evolution trait registry must include Colossus."
    Assert-True $endlessAbyssEvolutionTraitRegistryJson.Contains('"Terrias_terrias_boss_trait_mirror_array"') "Evolution trait registry must include Three-Thousand Ring Sun Mirror."
    Assert-True (-not $endlessAbyssEvolutionTraitRegistryJson.Contains("Terrias_terrias_boss_trait_merciless_daylight")) "Evolution trait registry must exclude the removed Merciless Daylight boss trait."
    Assert-True (-not $endlessAbyssEvolutionTraitRegistryJson.Contains("Terrias_terrias_boss_trait_white_radiance_saint")) "Evolution trait registry must exclude the removed White Radiance Saint boss trait."
    Assert-True $executorApi.Contains("public static int BurnUpperBound(IStatusManager? target)") "ExecutorApi must expose a dynamic burn upper bound helper."
    Assert-True $buffOverflowApi.Contains("private const int BurnUpperBoundFallback = 1;") "Invalid burn upper bounds must fall back to the minimum valid stack count."
    Assert-True $buffOverflowApi.Contains("target.GetBuff(buffId)?.buffConfig?.UpperBound") "Burn upper bound must prefer the live BuffItemConfig.UpperBound."
    Assert-True ($buffOverflowApi.Contains('AuraGameDataHostApi.CopyRow(') -and $buffOverflowApi.Contains('TerriasContentIdCompatibility.LookupCandidates(buffId, "terrias", "wuna", "columbina")')) "Burn upper bound must fall back to the current or legacy Buff data row through the explicit shared copy API."
    Assert-True $buffOverflowApi.Contains("var upperBound = BurnUpperBound(target);") "Burn overflow must use the dynamic burn upper bound."
    Assert-True $terriasStatusLifecycleRouter.Contains("TerriasHookTargets.StatusManagerAddBuff") "Burn overflow must route StatusManager.AddBuff through the shared status lifecycle router."
    Assert-True $runtimeHooks.Contains('TerriasStatusLifecycleRouter.Register("RuntimeStatusBuff"') "RuntimeHooks must subscribe burn overflow to the shared StatusManager.AddBuff lifecycle."
    Assert-True $runtimeHooks.Contains("BeforeAddBuff = OnStatusManagerAddBuffBefore") "Burn overflow must prepare before real StatusManager.AddBuff execution."
    Assert-True $runtimeHooks.Contains("AfterAddBuff = OnStatusManagerAddBuffAfter") "Solar Radiance cap repair must run after StatusManager.AddBuff creation."
    Assert-True (-not $runtimeHooks.Contains("FieldApi.TryRedirectStatusFieldBuffAdd")) "Status AddBuff hooks must not redirect every field-buff add through the global hot path."
    Assert-True (-not $runtimeHooks.Contains("FieldApi.RemoveFieldBuffCarrier")) "Status AddBuff hooks must not run field carrier cleanup on every native AddBuff."
    Assert-True $runtimeHooks.Contains("FieldEffectHandlers.HandleBuffAdded") "Status AddBuff hooks must delegate active field policies to FieldEffectHandlers."
    Assert-True $buffApi.Contains("FieldApi.IsFieldBuffId") "BuffApi positive/negative buff scans must exclude all field buffs through FieldApi."
    Assert-True (-not $runtimeHooks.Contains('RegisterBefore(modConfig, "ScriptExecutor.AddBuff", OnScriptExecutorAddBuffBefore);')) "Burn overflow must not hook ScriptExecutor.AddBuff because it can mutate the active target list."
    Assert-True $buffOverflowApi.Contains("target.AddBuff(TerriasIds.BodyBurn, overflow);") "Burn overflow must add body burn directly to the resolved status target."
    Assert-True $buffOverflowApi.Contains("private const int SolarRadianceDefaultUpperBound = 12;") "Solar Radiance default upper bound must be 12."
    Assert-True $buffOverflowApi.Contains("private const int WunaSolarRadianceUpperBound = 15;") "Wuna Solar Radiance upper bound must be 15."
    Assert-True $executorApi.Contains("public static void PrepareSolarRadianceUpperBound") "ExecutorApi must prepare live Solar Radiance caps before AddBuff."
    Assert-True $executorApi.Contains("public static void FinalizeSolarRadianceUpperBound") "ExecutorApi must repair Wuna Solar Radiance caps after AddBuff."
    Assert-True $buffApi.Contains("public static bool IsWunaPlayerStatus") "BuffApi must expose a target-specific Wuna player status check."
    Assert-True $buffApi.Contains("PlayerApi.LocalPlayerStatusId()") "Wuna-only Solar Radiance expansion must be limited to the local player status, not enemies."
    Assert-True $buffData.Contains('","0","0","0","12","Mods/Terrias/ModResource/Images/Buff/Terrias/solar_radiance"') "Solar Radiance data default upper bound must be 12."
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
    Assert-True $terriasSkillCgRuntime.Contains("owner instance id is empty in multiplayer") "Terrias Skill CG must diagnose and skip empty owner ids in multiplayer."
    Assert-True $terriasSkillCgRuntime.Contains("BuildRegisteredCardUseRequests(") "Terrias Skill CG must still include registered card-use CG requests."
    Assert-True $terriasSkillCgRuntime.Contains("syncRemote: true") "Terrias Skill CG must request synchronized playback through the shared Skill CG runtime."
    Assert-True (-not $terriasSkillCgRuntime.Contains("RpcSkillCgPlaybackRequest")) "Terrias Skill CG must not own private playback RPCs."
    Assert-True $wunaScripts.Contains("TerriasCardTagService.RequestBurnoutAndWhiteRadianceForFriendlyHands(self") "White Sun Prayer must schedule friendly hand Burnout and White Radiance tagging."
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
    Assert-True $emberAdventureStateService.Contains("TerriasIds.WunaPersistentEmber") "Persistent Ember sync must read the old Wuna key as a legacy fallback."
    Assert-True $emberAdventureStateService.Contains("OwnerGameVarKey") "Persistent Ember sync must persist by stable player/status owner key."
    Assert-True $rpcEmberAdventureStateCommit.Contains("ITerriasServerBoundRpcCommand") "Persistent Ember RPC must bind server sender authority."
    Assert-True $rpcEmberAdventureStateCommit.Contains("owner mismatch") "Persistent Ember RPC must reject payload owner ids that do not match the bound sender."
    $savePersistentEmberBlock = [regex]::Match($buffApi, "public\s+static\s+int\s+SavePersistentEmber[\s\S]*?private\s+static\s+IEnumerable")
    Assert-True ($savePersistentEmberBlock.Success -and -not $savePersistentEmberBlock.Value.Contains("IsWunaActive()")) "BuffApi.SavePersistentEmber must not be gated by Wuna activation."
    $emberConsumedBlock = [regex]::Match($buffApi, "public\s+static\s+int\s+OnEmberConsumed[\s\S]*?public\s+static\s+int\s+SavePersistentEmber")
    Assert-True ($emberConsumedBlock.Success -and $emberConsumedBlock.Value.Contains("SavePersistentEmber(executor, status);") -and -not $emberConsumedBlock.Value.Contains("ChangeMaxHp")) "BuffApi Ember consumption must persist and publish the generic Buff event without applying Wuna career rewards."
    Assert-True ($wunaPassiveService.Contains('PolymorphStateStore.IsEffectiveCombatRoleFor(status, "wuna")') -and $wunaPassiveService.Contains("executor.ChangeMaxHp(consumed.ToString());")) "Wuna-only Ember rewards must live in Mechanics and follow the effective combat form."
    Assert-True ($buffScripts.Contains("WunaPassiveService.ResolveEmberConsumed") -and $wunaScripts.Contains("WunaPassiveService.ResolveEmberConsumed")) "Both Buff-driven and skill-driven Ember consumption must route Wuna career rewards through the separated passive service."
    Assert-True $buffApi.Contains("return string.IsNullOrWhiteSpace(careerId)") "Wuna active fallback must not override an explicit non-Wuna career."
    Assert-True (-not [regex]::IsMatch($buffApi + $wunaScripts, "SetGameVar\s*\(\s*TerriasIds\.WunaPersistentEmber")) "Persistent Ember must not write to the legacy unscoped GameVar."
    Assert-True $cardScripts.Contains('["draw_flame"] = InitDrawFlame') "draw_flame must be registered for initialization."
    Assert-True ([regex]::IsMatch($cardScripts, 'private\s+static\s+void\s+InitDrawFlame[\s\S]*?ExecutorApi\.SetBaseScript\(self,\s+"AttackCardItem"\);')) "draw_flame must allow self-targeting during initialization."
    Assert-True $cardScripts.Contains("var target = ExecutorApi.PrimaryTargetIncludingSelf(self);") "draw_flame must resolve targets without excluding self."
    Assert-True $cardScripts.Contains("ExecutorApi.TriggerBurnAllEnemies(self, times * 2);") "flamewheel_recurrence must trigger enemy burn 2*N times while keeping N as the cost."
    Assert-True $cardScripts.Contains("ExecutorApi.AddStatusBuff(self, target, TerriasIds.Burn, Math.Max(8, level), ""Target"");") "eclipse_hex must add current Burn stacks with an 8-stack minimum."
    Assert-True $buffScripts.Contains("return StatusApi.MaxHp(target) / 100 + 1;") "body_burn must deal 1% max HP + 1 true damage per stack."
    Assert-True (-not $specialTagRuntime.Contains("CardConfigApi.BaseCost")) "White radiance should use current actual play cost, not BaseCost."
    Assert-True $cardConfigApi.Contains("ReadPlayerCardCostMultiplier") "CardConfigApi must read the player CardCost multiplier."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.EnsureInCurrentLayer")) "RuntimeHooks must not inject Terrias events into normal adventure maps."
    Assert-True (-not $runtimeHooks.Contains("SolarEventRuntime.RepairMapSelection")) "RuntimeHooks must not repair normal adventure map selections for Terrias events."
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
    Assert-True $terriasIds.Contains('public const string EmberCloakLiningRelicId = "*ember_cloak_lining";') "Retired Ember Cloak Lining relic id must use the pool-hidden star prefix."
    Assert-True $terriasIds.Contains('public const string LegacyEmberCloakLiningRelicId = "ember_cloak_lining";') "Retired Ember Cloak Lining legacy id must remain recognized."
    Assert-True $terriasIds.Contains("public static bool IsHiddenRelicId") "TerriasIds must expose hidden relic filtering."
    Assert-True $battleRewardApi.Contains('!TerriasIds.IsHiddenRelicId(DictionaryUtil.Get(row, "Id"))') "Random relic reward candidates must exclude hidden relics."
    Assert-True $endlessAbyssMilestoneRewardService.Contains("!TerriasIds.IsHiddenRelicId(id)") "Endless Abyss relic options must exclude hidden relics."
    $relicRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Data\Relic\terrias.csv")
    $emberCloakLiningRelicRow = $relicRows | Where-Object { $_.Id -eq "*ember_cloak_lining" } | Select-Object -First 1
    $ashCharmRelicRow = $relicRows | Where-Object { $_.Id -eq "ash_charm" } | Select-Object -First 1
    $sunOrbitMirrorRelicRow = $relicRows | Where-Object { $_.Id -eq "sun_orbit_mirror" } | Select-Object -First 1
    Assert-True ($null -ne $emberCloakLiningRelicRow) "Ember Cloak Lining must remain as a hidden star-prefixed relic row."
    Assert-True ($emberCloakLiningRelicRow.Rarity -eq "1") "Hidden relic rows must keep a UI-valid rarity instead of using Rarity 7."
    Assert-True ($ashCharmRelicRow.Rarity -eq "3") "Ash Charm must be promoted to rarity tier 3."
    Assert-True ($sunOrbitMirrorRelicRow.PackBelong -eq "Terrias_terrias_cardpack_ember_crown") "Sun-Orbit Mirror must belong to the Ember Crown card pack."
    $displayRarityKinds = @("Card", "Relic", "Buff", "Blessing", "EnchTag")
    foreach ($kind in $displayRarityKinds) {
        $kindRoot = Join-Path $RepoRoot "Terrias\Data\$kind"
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
    $relicTextRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Text\Relic\terrias.csv")
    $emberCloakLiningTextRow = $relicTextRows | Where-Object { $_.Id -eq "*ember_cloak_lining" } | Select-Object -First 1
    $sunOrbitMirrorTextRow = $relicTextRows | Where-Object { $_.Id -eq "sun_orbit_mirror" } | Select-Object -First 1
    $miniatureSunwheelTextRow = $relicTextRows | Where-Object { $_.Id -eq "miniature_sunwheel" } | Select-Object -First 1
    $blazingCrownHeartTextRow = $relicTextRows | Where-Object { $_.Id -eq "blazing_crown_heart" } | Select-Object -First 1
    $ashCharmTextRow = $relicTextRows | Where-Object { $_.Id -eq "ash_charm" } | Select-Object -First 1
    Assert-True ($null -ne $emberCloakLiningTextRow) "Hidden Ember Cloak Lining relic text row must keep the same star-prefixed id."
    Assert-True $sunOrbitMirrorTextRow.Description_en.Contains("Every 3 actions, gain 1 stack") "Sun-Orbit Mirror text must describe Gathered Flame gain."
    Assert-True $miniatureSunwheelTextRow.Description_en.Contains("All enemies gain {buff_burn} equal to your {Terrias_terrias_solar_radiance} stacks.") "Miniature Sunwheel text must describe party-wide Burn."
    Assert-True $blazingCrownHeartTextRow.Description_en.Contains("gain 8 stacks of {Terrias_terrias_solar_radiance}") "Blazing Crown Heart text must describe 8 Solar Radiance at combat start."
    $blazingCrownHeartChineseDescription = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("5Li65Zy65Zyw6ZO65LiKMuWxgntUZXJyaWFzX3RlcnJpYXNfc2NvcmNoaW5nX2Nhbm9weX0="))
    Assert-True $blazingCrownHeartTextRow.Description.Contains($blazingCrownHeartChineseDescription) "Blazing Crown Heart Chinese text must describe laying 2 Scorching Canopy stacks over the battlefield."
    Assert-True $ashCharmTextRow.Description_en.Contains("At round end") "Ash Charm text must trigger at round end."
    $sunOrbitMirrorBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterSunOrbitMirror[\s\S]*?private\s+static\s+void\s+RegisterSolarPhaseDial")
    $miniatureSunwheelBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterMiniatureSunwheel[\s\S]*?private\s+static\s+void\s+RegisterSunOrbitMirror")
    $blazingCrownHeartBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterBlazingCrownHeart[\s\S]*?private\s+static\s+void\s+RegisterSolarPrism")
    $ashCharmBlock = [regex]::Match($relicScripts, "private\s+static\s+void\s+RegisterAshCharm[\s\S]*?private\s+static\s+void\s+RegisterBlazingSundial")
    Assert-True ($sunOrbitMirrorBlock.Success -and $sunOrbitMirrorBlock.Value.Contains('self.AddBuff(TerriasIds.GatheredFlame, "1");') -and $sunOrbitMirrorBlock.Value.Contains("ExecutorApi.AddBurnToRandomEnemy(self, 3);")) "Sun-Orbit Mirror must gain Gathered Flame and apply 3 Burn every third action."
    Assert-True ($miniatureSunwheelBlock.Success -and $miniatureSunwheelBlock.Value.Contains("BuffApi.NegativeTotal(self.Self)") -and $miniatureSunwheelBlock.Value.Contains("ExecutorApi.AddStatusBuff(self, target, TerriasIds.Burn, burn);")) "Miniature Sunwheel must convert negative stacks into Gathered Flame and add Solar Radiance as Burn to all enemies."
    Assert-True ($miniatureSunwheelBlock.Success -and -not $miniatureSunwheelBlock.Value.Contains("ScorchingCanopy")) "Miniature Sunwheel must not require Scorching Canopy."
    Assert-True (-not $blazingCrownHeartBlock.Value.Contains('TryAddEvent(self, "FightStart"')) "Blazing Crown Heart must not restore the legacy FightStart listener that can be lost during combat-status rebuild."
    Assert-True ($relicOpeningEffectService.Contains("executor.Self.AddBuff(TerriasIds.SolarRadiance, 8);") -and $relicOpeningEffectService.Contains("executor.Self.AddBuff(TerriasIds.SolarCrown, 1);")) "Blazing Crown Heart must replay Radiance and Crown through the focused opening-effect service."
    Assert-True ($relicFieldStartSourceService.Contains('TerriasFieldId.ScorchingCanopy') -and $relicFieldStartSourceService.Contains('"blazing_crown_heart"')) "Blazing Crown Heart's field grant must be registered separately with the opening coordinator."
    Assert-True (-not $blazingCrownHeartBlock.Value.Contains('TryAddEvent(self, "StartRound"')) "Blazing Crown Heart must not keep the old round-start Burn aura."
    Assert-True ($ashCharmBlock.Success -and $ashCharmBlock.Value.Contains('TryAddEvent(self, "EndRound"') -and $ashCharmBlock.Value.Contains("self.AddBuff(TerriasIds.Ember, burn.ToString());") -and $ashCharmBlock.Value.Contains("self.ChangeDefence(burn.ToString());")) "Ash Charm must grant Ember and Block equal to self Burn at round end."
    Assert-True $solarMemoryCombatRuntime.Contains('TerriasStatusLifecycleRouter.Register("SolarMemoryCombat"') "Solar Memory combat tuning must subscribe through the shared status lifecycle router."
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
    Assert-True $solarMemoryContentIsolationRuntime.Contains("SolarMemoryContentIsolationService.SanitizeSelectionArrays") "Solar Memory isolation runtime must delegate synchronized array mutation to the pure Mechanics service."
    Assert-True $solarMemoryContentIsolationService.Contains("TerriasIds.IsSolarMemoryExclusiveMapId") "Solar Memory isolation policy must use centralized exclusive map ids."
    Assert-True $solarMemoryContentIsolationService.Contains("!RequiresReplacement(replacement.MapId, replacement.NodeId)") "Solar Memory isolation policy must reject exclusive fallback results."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RestoreCurrentNodeIfMissingOrExclusive(level, "MapSelectUI.ReadyToSelect", clientOnly: true)') "Normal-mode isolation must restore a missing client current node before map selection UI is consumed."
    Assert-True $solarMemoryContentIsolationRuntime.Contains('RestoreCurrentNodeIfMissingOrExclusive(level, "MapManager.MapSelectionSync", clientOnly: true)') "Normal-mode map sync isolation must repair client currentNode from synchronized arrays."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("MapNodeSafetyService.EnsureNodeDice") "Normal-mode isolation must ensure replacement nodes have NodeDice."
    Assert-True $solarMemoryContentIsolationRuntime.Contains("if (SolarMemoryModeRuntime.IsSolarMemoryRun())") "Solar Memory isolation must leave Solar Memory runs untouched."
    Assert-True $mapNodeSafetyService.Contains("public static bool RestoreCurrentNodeIfMissingOrExclusive") "Map node safety service must expose client current-node restoration."
    Assert-True $mapNodeSafetyService.Contains("clientOnly && !IsClientOnlyPlayer()") "Client current-node restoration must be gated so it does not advance host authority."
    Assert-True $mapNodeSafetyService.Contains("TryBuildCurrentNodeFromSyncArrays") "Client current-node restoration must prefer synchronized map arrays."
    Assert-True $mapNodeSafetyService.Contains("GameSaveManager.UpdateNode(node)") "Current-node restoration must update the saved node after assigning MapTree.currentNode."
    Assert-True $mapNodeSafetyService.Contains("NodeDice = tree.treedice ?? Dice.Default") "Restored synchronized nodes must have deterministic NodeDice."
    Assert-True $terriasIds.Contains("public static bool IsSolarMemoryExclusiveMapId") "TerriasIds must centralize exclusive Solar Memory map identification."
    Assert-True $terriasIds.Contains("public static bool IsSolarMemoryExclusiveEventId") "TerriasIds must centralize exclusive Solar Memory event identification."
    Assert-True $runtimeHooks.Contains("DuskPartnerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Dusk partner runtime."
    Assert-True $runtimeHooks.Contains("StarClayDollRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Star Clay Doll independently from Dusk."
    Assert-True $runtimeHooks.Contains("StarScoreHudRuntime.Initialize(modConfig)") "RuntimeHooks must initialize the star score HUD independently from card logic."
    Assert-True $runtimeHooks.Contains("LoneerRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Loneer's card-action runtime."
    Assert-True $runtimeHooks.Contains("SolarMemoryMapItemAnimationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize solar memory map-item animation fallback hooks."
    Assert-True $runtimeHooks.Contains("MapNodeCardArtRuntime.Initialize(modConfig)") "RuntimeHooks must initialize generic map-node card art hooks after animation fallback hooks."
    Assert-True ($runtimeHooks.IndexOf("DimensionShopRuntime.Initialize(modConfig)", [System.StringComparison]::Ordinal) -lt $runtimeHooks.IndexOf("MapNodeCardArtRuntime.Initialize(modConfig)", [System.StringComparison]::Ordinal)) "Dimension shop MapItem compatibility must register before generic map-node card art hooks."
    Assert-True $dimensionShopRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", PrepareDimensionShopMapItem);') "Dimension shop must prepare its custom map node for native MapItem initialization."
    Assert-True $dimensionShopRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", RestoreDimensionShopMapItem);') "Dimension shop must restore its custom map node after native MapItem initialization."
    Assert-True ($dimensionShopRuntime.Contains('node.data["NodeId"] = NativeMapItemNodeId;') -and $dimensionShopRuntime.Contains('node.data["NodeId"] = originalNodeId;')) "Dimension shop MapItem compatibility must be a reversible NodeId mapping."
    Assert-True ($dimensionShopRuntime.Contains("NodeDice = previous?.NodeDice") -and $dimensionShopRuntime.Contains("MapNodeSafetyService.EnsureNodeDice")) "Dimension shop injection must preserve the replaced RNG cursor and repair a missing authoritative NodeDice before insertion."
    Assert-True ($dimensionShopRuntime.Contains('RegisterBefore(modConfig, "MapSelectUI.SetNodes", RestoreBeforeMapSelectionBoundary);') -and $dimensionShopRuntime.Contains('RegisterBefore(modConfig, "MapItem.OnPointerDown", RestoreBeforeMapItemBoundary);')) "Dimension shop must restore residual native NodeIds before persistence or user interaction."
    Assert-True ($dimensionShopRuntime.Contains('RegisterBefore(modConfig, "Commands.load", PrepareDimensionShopRoute);') -and $dimensionShopRuntime.Contains("DimensionShopGameApi.CloseNativeBreakFallback()")) "Dimension shop must recover a residual native NodeId that reaches command routing."
    Assert-True ($dimensionShopGameApi.Contains('GameObject.Find("Breaks")') -and $dimensionShopGameApi.Contains("background.SetActive(true)")) "Dimension shop route recovery must remove the native break screen and restore the adventure background."
    Assert-True ($dimensionShopGameApi.Contains("NetworkClient.active") -and $dimensionShopGameApi.Contains("playerManager.CmdSyncRoleTable(role)") -and $dimensionShopGameApi.Contains("save?.roleTable == null")) "Dimension shop role persistence must route multiplayer snapshots through the native server command and reserve direct save writes for a complete offline save."
    Assert-True ($dimensionShopGameApi.Contains("HasPendingRolePersist") -and $dimensionShopPanel.Contains("FlushPendingRolePersist") -and $dimensionShopPanel.Contains("RolePersistRetryLimit")) "Dimension shop must retain and boundedly retry a role snapshot when native submission is temporarily unavailable."
    Assert-True (-not $dimensionShopGameApi.Contains('PersistRole("DimensionShop.Card")') -and -not $dimensionShopGameApi.Contains('PersistRole("DimensionShop.Relic")') -and -not $dimensionShopService.Contains("DimensionShop.BuyCard.Rollback") -and -not $dimensionShopService.Contains("DimensionShop.BuyRelic.Rollback")) "Dimension shop purchases must submit exactly at their transaction boundary instead of from grant or rollback intermediates."
    Assert-True $dimensionShopPanel.Contains("DimensionShopNativeSkin.TryCreate") "Dimension shop must prefer the official ShopUI visual shell while retaining fallback orchestration."
    Assert-True ($dimensionShopPanel.Contains("TerriasModalHost.NativeUiParent()") -and $dimensionShopPanel.Contains("TerriasModalHost.CreateNativeFullscreenRoot") -and -not $dimensionShopPanel.Contains("TerriasModalHost.ModalParent()") -and $sharedUiModalHost.Contains("return UIManager.Instance?.canvasTf;")) "Dimension shop must share the official main Canvas with Tooltip and Floating Window instead of rendering above them."
    Assert-True ($dimensionShopNativeSkin.Contains('NativeShopResourcePath = "UI/ShopUI"') -and $dimensionShopNativeSkin.Contains("source.ItemPrefab") -and $dimensionShopNativeSkin.Contains("source.SellCardPrefab") -and $dimensionShopNativeSkin.Contains("source.TopRelicPrefab")) "Dimension shop native skin must source official ShopUI visual templates."
    Assert-True ($dimensionShopNativeSkin.Contains("AuraUiNativeGameItemAdapter.AdoptShopItem(holder)") -and $dimensionShopNativeSkin.Contains("AuraGameDataHostApi.Materialize(type, item.Id)") -and $dimensionShopNativeSkin.Contains("nativeItem.Init(nativeConfig)") -and -not $dimensionShopNativeSkin.Contains("ShowUI<ShopUI>")) "Dimension shop offers must initialize through a real ShopItem and the shared definition materializer without activating the ShopUI controller."
    Assert-True ($dimensionShopNativeSkin.Contains("AuraUiNativeButtonBinding.NeutralizeTree") -and $sharedUiNativeInteraction.Contains("target.onClick = new UnityEvent()") -and $sharedUiNativeInteraction.Contains("unityButton.onClick = new Button.ButtonClickedEvent()")) "Dimension shop native visual clones must sever persistent native button listeners through AuraUiShared."
    Assert-True ($dimensionShopNativeSkin.Contains("MakeReadOnly") -and -not $dimensionShopNativeSkin.Contains(".TryBuy(")) "Dimension shop held-item visuals must remain read-only and must not invoke native purchases."
    Assert-True (-not $dimensionShopNativeSkin.Contains("belongsToGameAssembly") -and -not $dimensionShopNativeSkin.Contains("GetComponentsInChildren<MonoBehaviour>(true)") -and -not $dimensionShopNativeSkin.Contains("GetComponentsInChildren<EventTrigger>") -and -not $dimensionShopNativeSkin.Contains("trigger.triggers?.Clear()")) "Dimension shop must retain native UI components and CardItem EventTrigger hover actions."
    Assert-True ($dimensionShopNativeSkin.Contains("GetComponentsInChildren<TutorialSpotlightUI>(true)") -and $dimensionShopNativeSkin.Contains("UnityEngine.Object.DestroyImmediate(tutorialRoot)")) "Dimension shop must remove the native ShopUI tutorial overlay and its raycast blocker as a complete subtree."
    Assert-True (-not $dimensionShopNativeSkin.Contains("if (!item.Equipped")) "Dimension shop held-relic rendering must include owned unequipped relics."
    Assert-True $dimensionShopPanel.Contains("native ShopUI render failed; switching to fallback panel") "Dimension shop must recover from native render incompatibility inside the active modal."
    Assert-True ($dimensionShopNativeSkin.Contains("grid.constraintCount = 3") -and $dimensionShopNativeSkin.Contains("Instantiate(offerTemplate, shopRoot") -and $dimensionShopNativeSkin.Contains("Instantiate(heldCardTemplate, heldCardRoot")) "Dimension shop must use a native three-column offer grid and complete native offer/backpack visual prefabs."
    Assert-True ($sharedUiNativeGameItems.Contains("AuraUiSafeSellItem : SellItem") -and $sharedUiNativeGameItems.Contains("AuraUiSafeRelicItem : RelicItemConfig") -and $sharedUiNativeGameItems.Contains("EnsureTooltip")) "AuraUiShared must retain native item initialization and tooltip ownership while replacing only unsafe action meaning."
    Assert-True ($dimensionShopNativeSkin.Contains("nativeItem.Init(item.Equipped, NativeConfig(item, DataType.Card))") -and $dimensionShopNativeSkin.Contains("nativeItem.Init(NativeConfig(item, DataType.Relic))") -and $dimensionShopService.Contains("NativeConfig = config")) "Dimension shop backpack entries must initialize from their exact native DataConfig instances."
    Assert-True ($dimensionShopNativeSkin.Contains("LogNativeComponentTopology") -and $dimensionShopNativeSkin.Contains("DimensionShopGameApi.VerifyTooltipVisible") -and $dimensionShopGameApi.Contains("native overlay verified visible") -and $dimensionShopGameApi.Contains('"floating-card"') -and $dimensionShopGameApi.Contains('"floating-relic"') -and -not $dimensionShopNativeSkin.Contains("native KeywordDisplay shown") -and -not $dimensionShopNativeSkin.Contains("right-click reached native")) "Dimension shop diagnostics must verify rendered overlay visibility rather than report pointer-event arrival as success."
    Assert-True ($dimensionShopNativeSkin.Contains("BeginNativeOverlayGeneration") -and $dimensionShopGameApi.Contains("TooltipVerificationCancellationReason") -and $dimensionShopGameApi.Contains('return "render-generation-changed"') -and $dimensionShopGameApi.Contains("native overlay verification cancelled")) "Dimension shop tooltip verification must classify redraw, destroyed anchors, and ended hover as cancellation instead of visibility failure."
    Assert-True ($sharedUiNativeOverlayVisibility.Contains("SharesRootCanvas") -and $sharedUiNativeOverlayVisibility.Contains("IsVisibleAbove") -and $sharedUiNativeOverlayVisibility.Contains("sameRootCanvas") -and $sharedUiNativeOverlayVisibility.Contains("aboveAnchor") -and $dimensionShopGameApi.Contains("AuraUiNativeOverlayVisibility.SharesRootCanvas") -and $dimensionShopGameApi.Contains("AuraUiNativeOverlayVisibility.IsVisibleAbove")) "AuraUiShared must verify that native overlays share the anchor Canvas and render above its UI branch."
    Assert-True ($sharedUiNativeInteraction.Contains("class AuraUiNativeItemAnchor") -and $sharedUiNativeInteraction.Contains("onRightClick: surface.InvokeRight") -and $sharedUiNativeInteraction.Contains("lastRightFrame == frame") -and $sharedUiNativeInteraction.Contains("tooltip.enabled = true")) "AuraUiShared anchored item binding must restore exact tooltip and right-click response without double dispatch."
    Assert-True ($sharedUiNativeGameItems.Contains("manager.enableIcon = sprite != null") -and $sharedUiNativeGameItems.Contains("manager.SetIcon(sprite)") -and $dimensionShopNativeSkin.Contains('"val/Disabled/Title"')) "Dimension shop icon and price state must explicitly cover null and disabled native states."
    Assert-True ($dimensionShopNativeSkin.Contains("item.State == DimensionShopItemState.Empty") -and $dimensionShopNativeSkin.Contains("image.raycastTarget = false") -and -not $dimensionShopNativeSkin.Contains("ConfigureRelicOffer")) "Dimension shop must omit empty native products and keep status overlays from blocking native hover."
    Assert-True ($dimensionShopNativeSkin.Contains('Require(holder.transform, "val")') -and $dimensionShopNativeSkin.Contains("AuraUiNativeButtonBinding.TryBind")) "Dimension shop product prices must bind the exact native price ButtonManager through AuraUiShared."
    Assert-True ($dimensionShopNativeSkin.Contains("!busy && !hasTerminalOverlay") -and $dimensionShopNativeSkin.Contains("if (item.CanBuy && !busy)")) "Dimension shop must keep an insufficient-balance price in the native visible state while independently guarding purchase semantics."
    Assert-True ($dimensionShopNativeSkin.Contains("ClearPrice(holder.transform)") -and $dimensionShopNativeSkin.Contains("currencyIcon.sprite = null") -and $dimensionShopNativeSkin.Contains("priceRoot.gameObject.SetActive(false)")) "Dimension shop terminal offers must explicitly clear their price label and Truth Crystal icon."
    Assert-True ($dimensionShopNativeSkin.Contains("CreateStatusOverlay(OfferVisual(holder.transform, type), item.Status)") -and $dimensionShopNativeSkin.Contains('type == DataType.Card ? "CardItem" : "Item"')) "Dimension shop terminal shading must stay inside the active native product visual instead of stretching across the complete offer holder."
    Assert-True ($dimensionShopNativeSkin.Contains('refreshButton.SetLabel("\u5237\u65b0 " + state.RefreshPrice)') -and -not $dimensionShopNativeSkin.Contains("refreshInteraction.transform.parent")) "Dimension shop refresh labels must render the effective configured price on the bound native button."
    Assert-True ($dimensionShopNativeSkin.Contains("label: null") -and -not $dimensionShopNativeSkin.Contains('"\u79bb\u5f00"')) "Dimension shop exit control must preserve the official native icon instead of replacing it with a text label."
    Assert-True ($sharedUiNativeInteraction.Contains("string? label") -and $sharedUiNativeInteraction.Contains("if (label != null)") -and -not $sharedUiNativeInteraction.Contains("HasCompleteVisualState")) "AuraUiShared native button binding must support icon-only and partial-state controls without rejecting the native shell."
    Assert-True (-not $dimensionShopNativeSkin.Contains("class DimensionShopNativeInteraction") -and -not $dimensionShopNativeSkin.Contains("class DimensionShopHeldCardInteraction")) "Dimension shop must not retain private duplicates of shared native pointer interaction components."
    Assert-True ($dimensionShopNativeSkin.Contains("goldBalanceText.text") -and $dimensionShopNativeSkin.Contains("truthBalanceText.text") -and $dimensionShopNativeSkin.Contains('OfferCurrencyIconPath = "val/Icon"')) "Dimension shop must show both currencies and bind product prices to the exact native currency icon node."
    Assert-True (-not $dimensionShopNativeSkin.Contains("ReplaceCurrencyIcon") -and -not $dimensionShopNativeSkin.Contains("FindCurrencyIcon")) "Dimension shop currency rendering must not guess image nodes by name or shape."
    Assert-True ($dimensionShopService.Contains("public static bool SellCard(string instanceId") -and $dimensionShopService.Contains("role.cardList.Remove(card)") -and $dimensionShopService.Contains("role.UnCardList.Remove(card)") -and $dimensionShopService.Contains("role.Money += baseGold")) "Dimension shop card sales must settle by instance and reward gold only."
    Assert-True ($dimensionShopService.Contains("public static bool SellRelic(string instanceId") -and $dimensionShopService.Contains("public static bool UnequipRelic(string instanceId") -and $dimensionShopGameApi.Contains("ShowRelicMenu")) "Dimension shop relics must expose safe sale and take-off actions through the host floating menu."
    Assert-True ($dimensionShopService.Contains('tags.IndexOf("Eternal"') -and $dimensionShopService.Contains("role.cardList.Count <= role.CardBottomCount")) "Dimension shop card sales must preserve native Eternal and minimum-deck restrictions."
    Assert-True ($dimensionShopGameApi.Contains("TruthCurrencySprite") -and $dimensionShopGameApi.Contains('TruthCurrencyResourcePath = "Icon/UI_Icons/Native/Icon/\u771f\u7406\u4e4b\u6676"') -and -not $dimensionShopGameApi.Contains("CurrencySpriteNear") -and $dimensionShopGameApi.Contains("GetFloatingWindow")) "Dimension shop currency and context-menu integrations must use explicit host APIs and stay behind the GameApi facade."
    Assert-True ($terriasContentIdCompatibility.Contains("LegacyMainTableId") -and $terriasContentIdCompatibility.Contains('LegacyPrefix("wuna")') -and $terriasContentIdCompatibility.Contains('LegacyPrefix("loneer")') -and $terriasContentIdCompatibility.Contains('LegacyPrefix("columbina")') -and $terriasContentIdCompatibility.Contains('LegacyPrefix("cursecard")') -and $terriasContentIdCompatibility.Contains('LegacyPrefix("solar_memory")') -and $terriasContentIdCompatibility.Contains("Canonicalize") -and $terriasContentIdCompatibility.Contains("LookupCandidates") -and $terriasConfigIndex.Contains("TerriasContentIdCompatibility.LookupCandidates")) "Terrias content lookup must centralize all supported legacy-to-current prefix aliases."
    Assert-True ($dimensionShopService.Contains("public enum DimensionShopItemState") -and $dimensionShopService.Contains("HeldCards = BuildHeldCards()") -and $dimensionShopService.Contains("HeldRelics = BuildHeldRelics()")) "Dimension shop view state must expose typed offer states and held-item snapshots."
    Assert-True ($dimensionShopService.Contains('rarity == 3 || rarity == 4') -and $dimensionShopService.Contains('.Where(id => !DimensionShopGameApi.HasRelic(id))') -and -not $dimensionShopService.Contains("DimensionShopConfigStore.Current.RelicIds")) "Dimension shop relic shelves must discover all runtime tier-three/four relics while excluding carried relics and ignoring pack allowlists."
    Assert-True ($dimensionShopService.Contains("private const int OfferCount = 3") -and $dimensionShopService.Contains("Cards = Enumerable.Range(0, OfferCount)") -and $dimensionShopService.Contains("Relics = Enumerable.Range(0, OfferCount)") -and $dimensionShopNativeSkin.Contains("state.Cards.Count") -and $dimensionShopNativeSkin.Contains("state.Relics.Count")) "Dimension shop must render three card offers and three relic offers."
    Assert-True ($dimensionShopService.Contains("DimensionShopRelicPurchaseUsedKey") -and $dimensionShopService.Contains("if (purchaseUsed)") -and $dimensionShopService.Contains("DimensionShopItemState.SoldOut")) "Dimension shop must sell at most one relic per player per run and mark every later relic offer sold out."
    Assert-True ($dimensionShopService.IndexOf("TryGrantRelicToWarehouse", [System.StringComparison]::Ordinal) -lt $dimensionShopService.IndexOf('SetPlayerValue(TerriasIds.DimensionShopRelicPurchaseUsedKey, "1")', [System.StringComparison]::Ordinal)) "Dimension shop must not consume a player's relic allowance before the grant succeeds."
    Assert-True ($dimensionShopService.Contains("BoughtRelics().Count > 0") -and $dimensionShopService.Contains("DimensionShopRunVersionKey") -and $dimensionShopService.Contains("DimensionShopPlayerVersionKey")) "Dimension shop must migrate old one-item shelves and purchased-relic history."
    Assert-True ($dimensionShopService.Contains("SetCardBoughtSlots") -and $dimensionShopService.Contains("boughtSlots[slot]") -and $dimensionShopService.Contains("BuyCard(int slot")) "Dimension shop card purchases must be tracked independently by shelf slot."
    Assert-True ($dimensionShopService.Contains("Description = SafeItemDescription(config)") -and $dimensionShopService.Contains('Tips = SafeLocalizedField(config, "Tips")')) "Dimension shop held cards and relics must expose localized hover-tooltip content."
    Assert-True ($dimensionShopConfigSource.Contains("ShopkeeperPortraitResourcePath") -and $dimensionShopConfigSource.Contains("ShopkeeperPortraitNodePath")) "Dimension shop config must expose replaceable native shopkeeper portrait settings."
    Assert-True $terriasProject.Contains('<Reference Include="Plugins">') "Terrias must reference the host UI plugin assembly used by native ShopUI controls."
    Assert-True $starScoreService.Contains("public static event Action<StarScoreDisplaySnapshot>? Changed") "Star score mechanics must publish typed display snapshots for UI runtimes."
    Assert-True $starScoreService.Contains("PublishChanged(self.Self, state, isCadencePreview: true") "Star score HUD must receive a full three-note cadence preview before state collapse."
    Assert-True $starScoreState.Contains("public StarScoreDisplaySnapshot Snapshot") "Star score combat state must expose a display snapshot instead of leaking mutable note lists."
    Assert-True $starScoreNote.Contains("public enum StarScoreNote") "Star score notes must be modeled as typed values."
    Assert-True $starScoreSnapshot.Contains("IReadOnlyList<StarScoreNote> Notes") "Star score display snapshots must expose typed notes."
    Assert-True $starScoreCadenceCatalog.Contains("public static class StarScoreCadenceCatalog") "Star score tooltip cadence copy must live in Mechanics."
    Assert-True $starScoreCadenceCatalog.Contains("CandidatesForPrefix") "Star score tooltip cadence candidates must be calculated from the current prefix."
    Assert-True $starScoreHudRuntime.Contains("StarScoreService.Changed += OnStarScoreChanged") "Star score HUD runtime must subscribe to mechanics snapshots."
    Assert-True $starScoreHudRuntime.Contains('TerriasBattleLifecycleRouter.Register("StarScoreHud"') "Star score HUD runtime must clear its UI through the shared battle lifecycle router."
    Assert-True $starScoreHudRuntime.Contains('FightStarted = OnFightBoundary') "Star score HUD runtime must clear its UI on fight start."
    Assert-True $starScoreHudRuntime.Contains('FightEnding = OnFightBoundary') "Star score HUD runtime must clear its UI on fight end."
    Assert-True $starScoreHudRuntime.Contains('RegisterAfter(modConfig, TerriasHookTargets.FightWinInit, OnFightBoundary);') "Star score HUD runtime must still cover fight-win init boundaries."
    Assert-True $starScoreHudRuntime.Contains('RegisterAfter(modConfig, TerriasHookTargets.FightEscapeInit, OnFightBoundary);') "Star score HUD runtime must still cover fight-escape init boundaries."
    Assert-True $starScoreHudRuntime.Contains("BattleHudHost.TryGet") "Star score HUD must attach beneath FightUI and avoid modal selection layers."
    Assert-True $starScoreHudRuntime.Contains("FightPlayer.Instance?.Status?.InstanceId") "Star score HUD must filter snapshots to the local player owner."
    Assert-True $starScoreHudRuntime.Contains('activeView.Close("StarScoreHudRuntime.Close")') "Star score HUD runtime must close roots through the view safety path."
    Assert-True (-not $starScoreHudRuntime.Contains("Object.Destroy(activeView.gameObject)")) "Star score HUD runtime must not directly destroy HUD roots."
    Assert-True (-not $starScoreHudView.Contains("ProgressPartThresholds")) "Star score HUD must keep the full frame visible instead of lighting progress parts."
    Assert-True $starScoreHudView.Contains("TerriasUiSafety.CloseTransient(gameObject") "Star score HUD view must close through shared UI safety."
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
    Assert-True $starScoreHudTooltipView.Contains("TerriasUiPool.AcquireComponent") "Star score tooltip row rebuilds must reuse pooled rows."
    Assert-True $starScoreHudTooltipView.Contains("TerriasUiPool.ReleaseOrDestroyChildren") "Star score tooltip row rebuilds must use pooled teardown."
    Assert-True (-not $starScoreHudTooltipView.Contains("Destroy(child.gameObject)")) "Star score tooltip must not directly destroy rows."
    Assert-True $starScoreHudView.Contains("LayoutScale = 0.61f") "Star score HUD must use a single root scale for fixed placement."
    Assert-True $starScoreHudAssets.Contains('OpeningIconPath = Root + "\u542f.png"') "Star score HUD assets must map the Opening icon resource."
    Assert-True $starScoreHudAssets.Contains("StructuralPaths()") "Star score HUD assets must separate structural warmup resources."
    Assert-True $starScoreHudAssets.Contains("NoteIconPaths()") "Star score HUD assets must expose heavy note icons separately."
    Assert-True $starScoreHudAssets.Contains("StarScoreNote.Opening => Load(OpeningIconPath)") "Star score HUD assets must map typed notes to icon sprites."
    Assert-True (-not $terriasProject.Contains("UnityEngine.InputLegacyModule")) "Star score HUD hover detection must not depend on the Unity input legacy module."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", PrepareMapItemAnimation);') "Solar memory map items must patch fixed boss animation paths before native MapItem.Init loads Texture2D frames."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", RestoreMapItemAnimation);') "Solar memory map item animation fallback must restore enemy animation paths after native MapItem.Init."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("TerriasIds.SolarBossSecondSunLevelId") "Solar memory map item fallback must cover the second-sun boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains("TerriasIds.SolarBossSaintWunaLevelId") "Solar memory map item fallback must cover the saint Wuna boss map node."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('row["Animation"] = fallbackAnimation') "Solar memory map item fallback must temporarily replace the enemy Animation row."
    Assert-True $solarMemoryMapItemAnimationRuntime.Contains('restore.Row["Animation"] = restore.Animation') "Solar memory map item fallback must restore the original enemy Animation row."
    Assert-True (-not $solarMemoryMapItemAnimationRuntime.Contains("ApplyFixedBossMapTexture")) "Solar memory animation fallback must not own map-node texture replacement."
    Assert-True $mapNodeCardArtRuntime.Contains('RegisterBefore(modConfig, "MapItem.Init", CaptureMapItemBaseline);') "Map-node art runtime must capture icon baseline before native MapItem.Init mutates transform."
    Assert-True $mapNodeCardArtRuntime.Contains('RegisterAfter(modConfig, "MapItem.Init", ApplyMapNodeCardArt);') "Map-node art runtime must apply configured art after native MapItem.Init."
    Assert-True $mapNodeCardArtRuntime.Contains("TerriasResourceCache.Load<Texture>(spec.TexturePath, true)") "Map-node art runtime must load textures through the shared mod-aware resource cache."
    Assert-True ($mapNodeCardArtRuntime.Contains("NodeRuntimeScope") -and $mapNodeCardArtRuntime.Contains('string.Equals(scope, "historicalProjection"') -and $mapNodeCardArtRuntime.Contains("TerriasLog.DebugOnce")) "Map-node diagnostics must distinguish authority nodes from display-only historical projections and suppress repeated projection noise."
    Assert-True $mapNodeCardArtRegistry.Contains("VisualRegistry.MapNodeArtSpecs()") "Map-node art registry must be driven by the visual registry."
    Assert-True ($visualRegistry.Contains("TerriasIds.SolarBossSecondSunMapTexturePath") -and $visualRegistryJson.Contains("solar_memory.second_sun.map_card")) "Visual registry must cover the second-sun boss map texture."
    Assert-True ($visualRegistry.Contains("TerriasIds.SolarBossSaintWunaMapTexturePath") -and $visualRegistryJson.Contains("solar_memory.saint_wuna.map_card")) "Visual registry must cover the saint Wuna boss map texture."
    Assert-True ($visualRegistry.Contains("MapNodeCardArtFitMode.ContainTrimmed") -and $visualRegistryJson.Contains('"fitMode": "ContainTrimmed"')) "Fixed boss map-node art must use transparent-edge contain fitting."
    Assert-True $mapItemApi.Contains("TextureTransparencyAnalyzer.AnalyzeAllEdges") "MapItemApi must analyze transparent edges before applying fitted map-node textures."
    Assert-True $mapItemApi.Contains("MapNodeTextureFitService.Fit") "MapItemApi must delegate map-node texture geometry to the fit service."
    Assert-True $mapNodeTextureFitService.Contains("DefaultFightBoundsWidth = 160f") "Map-node texture fit service must preserve native fight-node width."
    Assert-True $mapNodeTextureFitService.Contains("DefaultFightBoundsHeight = 238f") "Map-node texture fit service must preserve native fight-node height."
    Assert-True $duskPartnerRuntime.Contains('"GameEntryUI.CheckCareer"') "Dusk runtime must clean its placeholder blessing after career checks."
    Assert-True $duskPartnerRuntime.Contains('TerriasBattleLifecycleRouter.Register("DuskPartner"') "Dusk runtime must grant its trait at fight start through the shared lifecycle router."
    Assert-True ($duskPartnerRuntime.Contains("status.GetBuff(TerriasIds.DuskAfterheatRecoveryTrait) == null") -and $duskPartnerRuntime.Contains("status.AddBuff(TerriasIds.DuskAfterheatRecoveryTrait")) "Dusk runtime must restore the trait from actual rebuilt status state."
    Assert-True ($duskPartnerRuntime.Contains('TerriasStatusLifecycleRouter.Register("DuskPartner"') -and $duskPartnerRuntime.Contains("AfterAddBuff = ObserveBurnAfterAdd") -and $duskPartnerRuntime.Contains("AfterEnemyInit = ObserveEnemyAfterInit")) "Dusk runtime must attach burn observers through existing status lifecycle hooks."
    Assert-True ($duskAfterheatRecoveryService.Contains("burn?.scriptExecutor") -and $duskAfterheatRecoveryService.Contains("ScriptEventApi.TryAddOwnedEventListener")) "Dusk afterheat recovery must register on the native burn executor owner used by RunImmediately."
    Assert-True $duskAfterheatRecoveryService.Contains("HashSet<IBuffItem>") "Dusk afterheat recovery must deduplicate listeners by burn buff instance."
    Assert-True $duskAfterheatRecoveryService.Contains("snapshot.StacksAtTrigger / 3") "Dusk's native passive must convert one third of triggered Burn stacks."
    Assert-True $duskAfterheatRecoveryService.Contains("owner.AddBuff(TerriasIds.GatheredFlame, traitGain.ToString())") "Dusk's native passive must grant Gathered Flame alongside Embers."
    Assert-True (-not $duskAfterheatRecoveryService.Contains("activeTraitBuff == null ? 0 : snapshot.StacksAtTrigger / 2")) "Dusk's native passive must not retain the old one-half Ember-only formula."
    Assert-True $burnTriggerApi.Contains("NotifyActual") "Burn execution must publish one unified actual-trigger semantic event."
    Assert-True $duskAfterheatRecoveryService.Contains("BurnTriggerApi.Triggered") "Dusk must observe immediate and native Burn through the unified trigger entry."
    Assert-True $duskPartnerRuntime.Contains("EnsureActive") "Dusk must rebind to the rebuilt player status after fight reset."
    Assert-True (-not $duskPartnerRuntime.Contains("TryAddBattleScopedBuffOnce")) "Dusk reset rehydration must not be blocked by a stale battle-operation ledger."
    Assert-True (-not $starClayDollRuntime.Contains("TryAddBattleScopedBuffOnce")) "Familiar start traits must be restored from actual status state after quick reset."
    Assert-True (-not $duskPartnerScripts.Contains("EventCenter")) "Dusk CSV entry points must not register raw native events."
    Assert-True (-not $duskPartnerScripts.Contains('TryAddTokenedEvent(self, "Action"')) "Dusk must not scan every enemy after each player action."
    Assert-True (-not $duskPartnerRuntime.Contains("StarClay")) "Dusk runtime must not own Star Clay Doll behavior."
    Assert-True $starClayDollRuntime.Contains('TerriasBattleLifecycleRouter.Register("StarClayDoll"') "Star Clay Doll runtime must grant its own trait at fight start through the shared lifecycle router."
    Assert-True ($starClayDollRuntime.Contains("status.GetBuff(TerriasIds.StarClayDollTrait) == null") -and $starClayDollRuntime.Contains("status.AddBuff(TerriasIds.StarClayDollTrait")) "Star Clay Doll runtime must restore its trait from actual rebuilt status state."
    Assert-True $starClayDollRuntime.Contains('TerriasStatusLifecycleRouter.Register("StarClayDoll"') "Star Clay Doll runtime must route lethal-hit protection through the status lifecycle router."
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
    Assert-True $starScoreRuntime.Contains("ResonanceCostTransactions.MarkActionObserved(config)") "Resonance payment must commit only after the matching card Action is observed."
    Assert-True $starScoreRuntime.Contains('RefundResonance(ResonanceCostTransactions.Cancel(config), "CardUseAfterWithoutAction")') "A rejected card use must roll back and refund its Resonance payment."
    Assert-True $starScoreRuntime.Contains('CancelResonancePayment(card, "CardDestroyed")') "Destroying a pending card must roll back its Resonance payment."
    Assert-True $resonanceCostTransactionStore.Contains("ApplyOnceCostDelta(config, -entry.AppliedOnceCostDelta)") "Resonance cancellation must remove only the transaction-owned cost delta."
    Assert-True $resonanceCostTransactionStore.Contains('DictionaryUtil.Set(config.Vars, "OnceExCost", "0")') "Successful Resonance payment must consume one-use cost state."
    Assert-True (-not $loneerRuntime.Contains("LoneerMiracleService.OnCardActionAfter")) "Loneer runtime must not own Star Stone Pouch action dispatch."
    Assert-True $buffScripts.Contains('["star_stone_pouch"] = ApplyStarStonePouch') "BuffScripts must route Star Stone Pouch apply behavior."
    Assert-True $buffScripts.Contains('["star_stone_pouch"] = ClearStarStonePouch') "BuffScripts must route Star Stone Pouch clear behavior."
    Assert-True $buffScripts.Contains('["star_score"] = ApplyStarScore') "BuffScripts must route Star Score apply behavior."
    Assert-True $buffScripts.Contains('["star_score"] = ClearStarScore') "BuffScripts must route Star Score clear behavior."
    Assert-True $starStonePouchService.Contains('ExecutorApi.TryAddTokenedEvent(self, "ActionAfter"') "Star Stone Pouch must own its own after-action draw hook."
    Assert-True $loneerService.Contains("StarStonePouchService.Drawn += OnStarStonePouchDrawn") "Loneer must subscribe to Star Stone Pouch draw results instead of owning the pouch."
    Assert-True (-not $loneerService.Contains("private static void DrawStone")) "Loneer miracle logic must not keep a role-owned Star Stone draw flow."
    $loneerDrawSubscriber = [regex]::Match($loneerService, "private\s+static\s+void\s+OnStarStonePouchDrawn[\s\S]*?private\s+static\s+void\s+QueueStarStonePouchDraw")
    Assert-True ($loneerDrawSubscriber.Success -and $loneerDrawSubscriber.Value.Contains("QueueStarStonePouchDraw(self, result);")) "Loneer Star Stone draw subscriber must enqueue derived work instead of resolving it inside ActionAfter."
    Assert-True (-not $loneerDrawSubscriber.Value.Contains("TriggerNaturalMorningStar")) "Loneer Star Stone draw subscriber must not trigger card grants synchronously inside ActionAfter."
    Assert-True $loneerService.Contains("PendingStarStoneDrawBatch") "Loneer Star Stone draw results must be batchable per owner."
    Assert-True $loneerService.Contains('"Loneer.StarStonePouchDraw."') "Loneer Star Stone draw batches must use keyed frame scheduling."
    Assert-True $loneerService.Contains("RequestGuidanceSelectionDeferred") "Loneer miracle guidance UI must be deferred away from card-use hot paths."
    Assert-True $loneerState.Contains("SelectionScheduled") "Loneer combat state must suppress duplicate deferred guidance selection requests."
    Assert-True $loneerState.Contains("Dictionary<string, LoneerCombatState>") "Loneer combat state must be keyed by owner status instead of ScriptExecutor.Vars."
    Assert-True $loneerService.Contains("LoneerCombatStateStore.GetOrCreate(self.Self)") "Loneer skill and action flows must resolve owner-scoped combat state."
    Assert-True $cardSelectionApi.Contains("Action? onCancelled = null") "Card selection API must expose cancellation separately from empty candidate pools."
    Assert-True $cardSelectionApi.Contains("onCancelled?.Invoke();") "Card selection API must notify callers when the selection UI closes without a card."
    Assert-True $loneerService.Contains("ResolveRandomGuidanceFallback") "Loneer must randomize Guidance when a non-empty selection UI is cancelled."
    Assert-True $loneerService.Contains("RandomGuidanceCard") "Loneer random fallback must choose from the current selectable Guidance pool."
    Assert-True ([regex]::IsMatch($loneerService, "RandomGuidanceCard[\s\S]*UnityEngine\.Random\.Range\(0,\s*pool\.Count\)")) "Cancelled Guidance selection must randomize within the current candidate pool."
    Assert-True (-not $loneerService.Contains("FirstGuidanceCardId")) "Cancelled Guidance selection must not use the old deterministic first-card fallback."
    Assert-True $cardGrantRecipes.Contains("TerriasIds.LoneerDerivedMarker") "Copied guidance cards must receive a hidden derived marker."
    Assert-True $cardGrantRecipes.Contains("TerriasIds.LoneerDerivedTag") "Copied guidance cards must receive a localized visible derived tag."
    Assert-True $cardMutationService.Contains("public static bool SetRuntimeMarkers") "CardMutationService must separate hidden runtime markers from visible SpecialTags."
    Assert-True $loneerService.Contains("CardMutationService.HasRuntimeMarker") "Loneer filtering must read hidden runtime markers."
    Assert-True (-not $cardGrantRecipes.Contains('AddSpecialTagsMutation(TerriasIds.LoneerDerivedMarker')) "Internal Loneer marker ids must never be written to SpecialTag."
    Assert-True $loneerService.Contains("LoneerCardGrantService.GrantGuidanceCopyToHand") "Loneer must use the shared card-grant recipe for guidance copies."
    Assert-True $wunaScripts.Contains("WunaCardGrantService.GrantCoronationTokenToHand") "Wuna must use the shared card-grant recipe for coronation tokens."
    Assert-True $wunaScripts.Contains('"WunaRadiance.BurnChanged."') "Wuna enemy burn OnLevelChange work must be merged by owner and hook token."
    Assert-True $wunaScripts.Contains("TerriasFrameDispatcher.RunOnceNextFrame") "Wuna enemy burn OnLevelChange work must defer aggregate burn scans through the shared frame dispatcher."
    Assert-True $wunaScripts.Contains("WunaRadiance.BurnChanged.Deduped") "Wuna burn-change batching must expose duplicate suppression counters."
    Assert-True $cardApi.Contains("public static CardGrantResult GrantCardToHand") "Generated cards must go through the structured CardApi grant pipeline."
    Assert-True $combatCardApi.Contains("public static bool TryDrawPlayerCards") "Non-script combat draws must use a focused GameApi facade."
    Assert-True $combatCardApi.Contains("FightCardManager.Instance") "Combat draw facade must resolve the native draw manager."
    Assert-True $combatCardApi.Contains('GetUI<FightUI>("FightUI")') "Combat draw facade must resolve the native fight UI."
    Assert-True $combatCardApi.Contains("manager.RandomIndex()") "Combat draw facade must preserve native draw-pile refill behavior."
    Assert-True (-not $combatCardApi.Contains(".DrawCount(")) "Combat draw facade must not invoke ScriptExecutor APIs that require dataConfig.Id."
    Assert-True $familiarBlessingEffectRuntime.Contains("CombatCardApi.TryDrawPlayerCards") "Familiar combat-start draw must use the safe combat draw facade."
    Assert-True (-not $familiarBlessingEffectRuntime.Contains("executor.DrawCount")) "Familiar combat-start draw must not borrow a synthetic ScriptExecutor."
    Assert-True $familiarBlessingEffectRuntime.Contains('LogCombatStartEffect(status, entry, "failed", ex.Message)') "Familiar combat-start effects must isolate and diagnose individual failures."
    Assert-True $cardApi.Contains('self.AddCardByData(resolved, request?.RuntimeTags ?? "");') "Generated cards must receive their runtime tags during DataConfig creation."
    Assert-True $cardApi.Contains("public CardGrantRequest WithRuntimePresentation") "Dynamic cards must compose native-readable presentation before materialization."
    Assert-True $cardApi.Contains("public static bool MarkForAdventureRemoval") "Permanent-use cards must use the centralized host lifecycle facade."
    Assert-True $cardApi.Contains("self.GetCardFromDeck(added);") "Generated cards must deliver the exact tagged DataConfig to the hand queue."
    Assert-True $cardApi.Contains("CardGrantPostCommitQueue.Request") "Generated cards must submit Terrias post-commit refresh work after native delivery succeeds."
    Assert-True $cardGrantPostCommitQueue.Contains("TerriasCardRefreshQueue.RequestConfigTagRefresh") "Post-commit card grant refreshes must reuse the card refresh queue."
    Assert-True $cardGrantPostCommitQueue.Contains("VisualRefreshSuppressed") "Post-commit card grant visuals must be explicitly suppressed in stable lifecycle mode."
    Assert-True (-not $cardGrantPostCommitQueue.Contains("CombatVisualReapplyPasses")) "Stable lifecycle mode must not run combat visual reapply passes."
    Assert-True (-not $cardGrantPostCommitQueue.Contains("RequestActiveCombatCardsReapply")) "Stable lifecycle mode must not coalesce post-commit visual misses into active combat-card reapply work."
    Assert-True (-not $cardGrantPostCommitQueue.Contains("MaterializeRetryBudget")) "Post-commit card grant visuals must not restore many per-card materialization retries."
    Assert-True (-not $cardGrantPostCommitQueue.Contains("SameFrameRetry")) "Post-commit card grant visuals must not retry within the same scheduler frame."
    Assert-True (-not $cardGrantPostCommitQueue.Contains("AddCardByData")) "Post-commit card grant refreshes must not own native card creation."
    Assert-True (-not $cardGrantPostCommitQueue.Contains("GetCardFromDeck")) "Post-commit card grant refreshes must not move cards through the native battle flow."
    Assert-True (-not $cardApi.Contains("LoneerDerivedTag")) "CardApi must not contain Loneer-specific business tags."
    Assert-True (-not $cardApi.Contains("WhiteRadianceTag")) "CardApi must not contain Wuna/Terrias-specific business tags."
    Assert-True (-not $wunaScripts.Contains("AddCardByData")) "Wuna must not hand-roll combat card creation."
    Assert-True (-not $wunaScripts.Contains("EnsureHandTags")) "Wuna must not hand-roll temporary tag propagation."
    Assert-True $cardMutationService.Contains("public static void SetTemporaryCost") "CardMutationService must own temporary card-cost mutation."
    Assert-True (-not $cardMutationService.Contains('config.data["Expend')) "Temporary card-cost mutation must not write base data."
    Assert-True (-not $polymorphActivationService.Contains("DictionaryUtil.Set(config.data")) "Polymorph role-card runtime state must be written to Vars, not read-only base data."
    Assert-True ($polymorphActivationService.Contains("BuildRoleCardPresentation(role)") -and $polymorphActivationService.Contains(".WithRuntimePresentation(presentation)")) "Polymorph role cards must materialize dynamic names, descriptions, and icons through the native-readable presentation path."
    Assert-True $polymorphActivationService.Contains("PolymorphBuffService.GrantForRole(self, role);") "Polymorph role cards must grant the trait buff instead of changing career directly."
    Assert-True (-not $polymorphActivationService.Contains("self.ChangeCareer(role.Id);")) "Polymorph card use must not be hard-bound to direct career changes."
    Assert-True $polymorphBuffService.Contains("self.ChangeCareer(role.Id);") "Polymorph trait buff apply must own the career change."
    Assert-True $polymorphBuffService.Contains("PolymorphRuntimeService.Enter(self, role, state);") "Polymorph trait buff apply must enter a runtime overlay after ChangeCareer."
    Assert-True ($polymorphBuffService.Contains("BuildTraitPresentation(role)") -and $polymorphBuffService.Contains("BuffApi.ApplyRuntimePresentation(buff, presentation)")) "Polymorph trait buffs must publish role-specific descriptions through the native-readable buff presentation path."
    Assert-True ($buffApi.Contains("RuntimePresentationDiffers") -and $buffApi.Contains("CopyRuntimeExecutorContext(config, replacement)")) "Live Buff presentation refreshes must migrate their initialized script executor before replacing native UI data."
    Assert-True ($buffApi.Contains("replacementExecutor.Self = sourceExecutor.Self") -and $buffApi.Contains("replacementExecutor.status = sourceExecutor.status") -and $buffApi.Contains("replacementExecutor.Target = sourceExecutor.Target") -and $buffApi.Contains("replacementExecutor.Object.AddRange(sourceExecutor.Object)")) "Live Buff presentation clones must preserve Self, status, Target, and Object for later ClearScript execution."
    Assert-True $polymorphBuffService.Contains("PolymorphStateStore.ClearOwner(owner") "Polymorph trait buff clear must restore the owner career through state cleanup."
    Assert-True $polymorphBuffService.Contains('ExecutorApi.TryAddTokenedEvent(self, "StartRound"') "Polymorph trait buff must own shared cooldown round ticking."
    Assert-True $polymorphCooldownService.Contains("CrossFormSkillUse") "Polymorph must track a separate cross-form skill-use ledger."
    Assert-True $polymorphCooldownService.Contains("HasDifferentFormSkillUseThisRound") "Polymorph must detect same-round use by another form only while initializing the next form."
    Assert-True $polymorphCooldownService.Contains("RoleCooldowns") "Polymorph must retain actual cooldown snapshots per form."
    Assert-True ($polymorphCooldownService.Contains("PrepareCurrentRoleEntry") -and $polymorphCooldownService.Contains("RoleSkillApi.SetCurrentCareerSkillTimes(0)") -and $polymorphCooldownService.Contains(": 0;")) "Polymorph must clear career-seeded cooldowns on first entry and restore only revisited-form cooldown snapshots."
    Assert-True ($polymorphCooldownService.Contains("CrossFormEntryCooldown = 1") -and $polymorphCooldownService.Contains("ApplyEntryCooldownFloor") -and $polymorphCooldownService.Contains("EntryCooldownOverlays")) "Polymorph must apply a one-time native entry cooldown without persisting it as a form cooldown."
    Assert-True (-not $polymorphCooldownService.Contains("StateVersion")) "Cross-form entry cooldown tracking must survive form-state version changes within the same Polymorph session."
    Assert-True $polymorphBuffService.Contains("CaptureCurrentRole(self.Self") "Polymorph must capture the outgoing form cooldown before ChangeCareer."
    Assert-True (-not $polymorphCooldownService.Contains("RoleSkillApi.SetCurrentCareerSkillTimes(cooldown);")) "Polymorph must not overwrite native role skill cooldowns."
    Assert-True $wunaScripts.Contains('"Wuna.WhiteSunPrayer",') "Wuna polymorph skill use must commit the shared cooldown."
    Assert-True $wunaScripts.Contains('"Wuna.GraveSong", GraveSongCardId') "Wuna second polymorph skill must share the same cooldown record."
    Assert-True $loneerService.Contains('"Loneer.MorningStarPrayer",') "Loneer polymorph skill use must commit the shared cooldown."
    Assert-True ($wunaScripts.Contains("PlayerApi.SetSkillTime(TerriasIds.WunaWhiteSunPrayerCardId, 5);") -and $wunaScripts.IndexOf("PlayerApi.SetSkillTime(TerriasIds.WunaWhiteSunPrayerCardId, 5);") -lt $wunaScripts.IndexOf('"Wuna.WhiteSunPrayer",')) "Wuna White Sun Prayer must write its native cooldown before the polymorph ledger captures it."
    Assert-True ($wunaScripts.Contains("PlayerApi.SetSkillTime(GraveSongCardId, 4);") -and $wunaScripts.IndexOf("PlayerApi.SetSkillTime(GraveSongCardId, 4);") -lt $wunaScripts.IndexOf('"Wuna.GraveSong", GraveSongCardId')) "Wuna Grave Song must write its native cooldown before the polymorph ledger captures it."
    Assert-True ($loneerService.Contains("SetMorningPrayerCooldown(self, state, PrayerCooldownRounds);") -and $loneerService.IndexOf("SetMorningPrayerCooldown(self, state, PrayerCooldownRounds);") -lt $loneerService.IndexOf('"Loneer.MorningStarPrayer",')) "Loneer Morning Star Prayer must write its cooldown before the polymorph ledger captures it."
    Assert-True ($polymorphRoleRegistry.Contains("AuraRoleRegistryRuntime.GetEffectiveSnapshot()") -and -not $polymorphRoleRegistry.Contains("TerriasConfigIndex.Rows(DataType.Career)")) "Polymorph role discovery must consume the shared effective-role catalog instead of every registered Career row."
    Assert-True $buffScripts.Contains("[TerriasIds.PolymorphTraitBuffShortId] = ApplyPolymorphTrait") "BuffScripts must route polymorph trait apply behavior."
    Assert-True $buffScripts.Contains("[TerriasIds.PolymorphTraitBuffShortId] = ClearPolymorphTrait") "BuffScripts must route polymorph trait clear behavior."
    Assert-True (-not $polymorphRuntime.Contains('HideTraitBuffFromContext')) "Polymorph trait buff must remain visible in battle."
    Assert-True $polymorphRuntime.Contains('RegisterBefore(modConfig, TerriasHookTargets.SkillItemTrueUse, CaptureSkillUseBefore);') "Polymorph runtime must capture official skill use before TrueUse."
    Assert-True $polymorphRuntime.Contains('RegisterAfter(modConfig, TerriasHookTargets.SkillItemTrueUse, MarkSkillUseAfter);') "Polymorph runtime must commit shared cooldown after official skill use."
    Assert-True (-not $polymorphRuntime.Contains("TerriasHookTargets.SkillItemTryUse") -and -not $polymorphRuntime.Contains("TerriasHookTargets.ScriptExecutorUpdateSkillTime") -and -not $polymorphCooldownService.Contains("TryUseSharedSkill")) "Polymorph entry cooldown must remain reducible and must not be reapplied by skill-use or cooldown-UI hooks."
    Assert-True $buffData.Contains('"polymorph_trait"') "Polymorph trait buff data row is missing."
    Assert-True ([regex]::IsMatch($buffData, '"polymorph_trait"[\s\S]*?"Icon/Buff/')) "Polymorph trait buff must reuse the Heroic Blessing icon path family."
    Assert-True $polymorphActivationService.Contains("PolymorphRuntimeService.ClearAll(source);") "Polymorph cleanup must clear runtime overlays before restoring career state."
    Assert-True $polymorphRuntimeService.Contains("TryRunCurrentCareerScript") "Polymorph runtime must run the current target-role career script."
    Assert-True $polymorphRuntimeService.Contains("RoleSkillApi.RefreshFightSkills") "Polymorph runtime must rebuild combat skill buttons after changing career."
    Assert-True $polymorphRuntimeService.Contains("executor?.Clear();") "Polymorph runtime cleanup must clear the attached career executor."
    Assert-True ($polymorphRuntimeService.Contains("ClearAttachment(attachment, source, endCombat: false)") -and $polymorphRuntimeService.Contains("if (endCombat && executor != null)") -and $polymorphRuntimeService.Contains("LoneerMiracleService.DetachCareerRuntime(executor)")) "Leaving a Loneer form must detach only career runtime; full Loneer combat cleanup is reserved for battle end."
    Assert-True ($polymorphStateStore.Contains("PolymorphRuntimeService.RestoreOriginalCareerRuntime") -and $polymorphRuntimeService.Contains("LoneerMiracleService.ResumeAfterPolymorph(executor)")) "Restoring an original Loneer career must restore its career runtime without deleting autonomous buffs."
    Assert-True ($loneerService.Contains("StarStonePouchService.EnsurePresent(self)") -and $starStonePouchService.Contains("public static bool EnsurePresent") -and $starStonePouchService.Contains("if (!BuffApi.Has(self.Self, TerriasIds.StarStonePouch))")) "Loneer polymorph entry and restoration must preserve an existing Star Stone Pouch and recover it only when missing."
    $loneerPolymorphEntry = [regex]::Match($loneerService, "public\s+static\s+void\s+PreparePolymorphEntry[\s\S]*?public\s+static\s+void\s+ResumeAfterPolymorph")
    Assert-True ($loneerPolymorphEntry.Success -and -not $loneerPolymorphEntry.Value.Contains("ClearCombatBuffs") -and -not $loneerPolymorphEntry.Value.Contains("EndCombatCleanup")) "Entering a Loneer form must not clear or pause autonomous buff behavior."
    Assert-True $roleSkillApi.Contains("fightUi.InitSkill();") "RoleSkillApi must reuse the native FightUI skill creation path."
    Assert-True $roleSkillApi.Contains("EnsureCurrentCareerSkillTimes") "RoleSkillApi must ensure target skill cooldown keys exist before the rebuilt buttons are used."
    Assert-True $roleSkillApi.Contains('value.Replace("*", "")') "RoleSkillApi must normalize starred official skill ids before cooldown sync."
    Assert-True $roleSkillApi.Contains("SetCurrentCareerSkillTimes") "RoleSkillApi must expose unified current-role skill cooldown writes for polymorph."
    Assert-True $polymorphStateStore.Contains("public static bool IsLocalRoleSuppressed") "Polymorph state must expose role suppression for old passive guards."
    Assert-True ($polymorphStateStore.Contains("public static string EffectiveCombatRoleIdFor") -and $polymorphStateStore.Contains("active.RoleId") -and $polymorphStateStore.Contains("PlayerApi.GetCurrentCareerId()")) "Polymorph state must expose one effective combat-role identity that prefers the temporary form over the immutable adventure role."
    Assert-True ($polymorphRuntimeService.Contains("public static bool IsRestoringOriginalCareerRuntime") -and $polymorphRuntimeService.Contains("restoreRuntimeOnly: true") -and $wunaScripts.Contains("if (!PolymorphRuntimeService.IsRestoringOriginalCareerRuntime)")) "Restoring original Wuna runtime must reattach hooks without rerunning first-entry skill cooldown initialization."
    Assert-True ($loneerService.Contains("PolymorphStateStore.IsLocalEffectiveCombatRole") -and $loneerService.Contains("PolymorphStateStore.IsEffectiveCombatRoleFor")) "Loneer passive and skill entries must follow the unified effective combat-role identity."
    Assert-True ($wunaScripts.Contains("IsWunaRuntimeActive") -and $wunaScripts.Contains('PolymorphStateStore.IsEffectiveCombatRoleFor(self?.Self, "wuna")')) "Wuna passive and skill entries must follow the same effective combat-role identity."
    Assert-True $cardScripts.Contains("[TerriasIds.ProjectionCardShortId] = UseProjection") "CardScripts must route the projection selection card."
    Assert-True $cardScripts.Contains("[TerriasIds.ProjectionRoleTemplateShortId] = UseProjectionRoleCard") "CardScripts must route generated projection role cards."
    Assert-True $runtimeHooks.Contains("ProjectionRuntime.Initialize(modConfig)") "RuntimeHooks must initialize projection combat hooks."
    Assert-True $runtimeHooks.Contains("CompanionSceneLifecycleRuntime.Initialize(modConfig)") "RuntimeHooks must initialize direct scene-replacement cleanup."
    Assert-True ($companionSceneLifecycleRuntime.Contains("SceneManager.sceneUnloaded += OnSceneUnloaded") -and $companionSceneLifecycleRuntime.Contains("SceneManager.activeSceneChanged += OnActiveSceneChanged")) "Companion lifecycle cleanup must observe both scene unload and the guarded active-scene fallback."
    Assert-True $companionSceneLifecycleRuntime.Contains("!CompanionSceneApi.IsSceneLoaded(previousHandle)") "Additive scene activation must not clear companions while the tracked battle scene is loaded."
    Assert-True ($companionSceneLifecycleRuntime.Contains("ProjectionRuntime.ClearBattle(source, sweepVisualOrphans: false)") -and $companionSceneLifecycleRuntime.Contains("SpiritRuntime.ClearBattle(source, sweepVisualOrphans: false)")) "Direct scene replacement must clear tracked companion state without duplicate presenter sweeps."
    Assert-True $companionSceneLifecycleRuntime.Contains("CompanionAuthorityService.InvalidateBattleEpoch") "Direct scene replacement must invalidate late network state."
    Assert-True ($companionSceneApi.Contains("SceneManager.MoveGameObjectToScene") -and $companionSceneApi.Contains("SceneManager.sceneCount") -and -not $companionSceneApi.Contains("GetSceneByHandle")) "Companion scene ownership must use APIs present in the current Managed contract."
    Assert-True ($projectionSummonService.Contains("CompanionSceneApi.MoveToOwnerScene") -and $spiritSummonService.Contains("CompanionSceneApi.MoveToOwnerScene") -and $projectionTurnCoordinator.Contains("CompanionSceneApi.MoveToOwnerScene")) "Companion actors and their turn anchor must inherit the owner's scene lifetime."
    Assert-True ($projectionAttachmentPresenter.Contains("public static void ClearAll") -and $spiritAttachmentPresenter.Contains("public static void ClearAll")) "Both companion presenters must expose an orphan-safe proxy sweep."
    Assert-True ($projectionRuntime.Contains('RunCleanupStep("NetworkDedupe"') -and $spiritRuntime.Contains('RunCleanupStep("CaptureDedupe"')) "Companion cleanup must reset all battle-scoped duplicate sets."
    Assert-True ($companionSceneLifecycleRuntime.Contains('TerriasHookRegistry.Before(') -and $companionSceneLifecycleRuntime.Contains('"GameEntryUI.Init"')) "Returning directly to the main menu must run companion cleanup through the safe hook registry."
    Assert-True ($companionSceneLifecycleRuntime.Contains('"TopBarUI.ReturnToMenu"') -and $companionSceneLifecycleRuntime.Contains('"GameApp.ReturnToMenu"')) "Companion cleanup must run before confirmed end-of-frame return and retain a direct-return fallback."
    Assert-True ($companionSceneLifecycleRuntime.Contains('"SuppressPresentation"') -and $companionSceneLifecycleRuntime.Contains("SchedulePostCleanupAudit(source,")) "Menu-exit cleanup must suppress the last rendered frame and conditionally audit residual objects on the next frame."
    Assert-True ($companionPresentationCleanup.Contains("GetComponentsInChildren<Renderer>(true)") -and $companionPresentationCleanup.Contains("status.actionContent") -and $companionPresentationCleanup.Contains("status.statusBarObj")) "Companion presentation suppression must immediately hide renderers and separately parented fight UI."
    Assert-True ($companionSceneLifecycleRuntime.Contains("!cleanupPending && !hasTrackedScenes") -and $companionSceneLifecycleRuntime.Contains("needsOrphanSweep")) "Scene-transition cleanup must deduplicate repeated boundaries and keep orphan scans on a conditional slow path."
    Assert-True ($companionSceneLifecycleRuntime.Contains("suppression.Total > 0 || !cleanupSucceeded") -and $companionSceneLifecycleRuntime.Contains("TerriasLog.InfoAlways(message)")) "Main-menu residual auditing must remain observable but run only after artifacts or failures."
    Assert-True ($companionSceneApi.Contains("HasTrackedScenes()") -and $companionPresentationCleanup.Contains("ProjectionRoots")) "Companion cleanup must reuse its synchronous suppression pass as the pre-destroy artifact inventory."
    Assert-True $companionSceneLifecycleRuntime.Contains('FightInitializing = _ => CleanupAfterSceneBoundary("FightInitializing")') "Fight initialization must sweep stale companion state from abnormal scene replacement."
    Assert-True $companionSceneLifecycleRuntime.Contains('FightEnded = _ => CleanupAfterSceneBoundary("FightEnded")') "Normal fight settlement must run the complete companion cleanup pipeline."
    Assert-True (-not $companionSceneLifecycleRuntime.Contains('FightEnding = _ => CompanionSceneApi.ClearTrackedScenes')) "Fight ending must not erase scene tracking before cleanup runs."
    Assert-True $terriasPerformanceSettingsSource.Contains("ReadFlag(CountersKey, false)") "Terrias performance counters must be opt-in."
    Assert-True ($runtimeHooks.Contains("if (TerriasPerformanceSettings.CountersEnabled)") -and $terriasCombatCardUiWorkloadRuntime.Contains("if (!TerriasPerformanceSettings.CountersEnabled)")) "Pure card-UI measurement hooks must not register outside performance diagnostics mode."
    Assert-True $spiritRuntime.Contains("CommonCardItem.UseChecker.Contains(SpiritCardUseChecker)") "Spirit-card use gating must register idempotently in the native pre-consumption checker."
    Assert-True $spiritRuntime.Contains('ProjectionStateStore.HasForOwner("", owner.InstanceId)') "Spirit cards must reject projection occupancy before native card consumption."
    Assert-True (-not $spiritRuntime.Contains("CardItem.canUse = false")) "Spirit-card eligibility must not toggle the global card-use state."
    Assert-True $runtimeHooks.Contains("CompanionIntentRegistry.Load(modConfig)") "RuntimeHooks must load companion intent registry before projection combat hooks."
    Assert-True $runtimeHooks.Contains("CompanionThreatRuntime.Initialize(modConfig)") "RuntimeHooks must initialize companion threat targeting."
    Assert-True $entry.Contains("Terrias.Dll.Scripting.ProjectionScripts") "Entry must register ProjectionScripts for CSV action calls."
    Assert-True $entry.Contains("Terrias.Dll.Scripting.HeartChangeScripts") "Entry must register HeartChangeScripts for temporary controlled intent action calls."
    Assert-True $projectionActivationService.Contains("CardGrantRequest") "Projection generated cards must use the shared card grant API."
    Assert-True ($projectionActivationService.Contains("BuildRoleCardPresentation(role, fixedAnotherMe)") -and $projectionActivationService.Contains(".WithRuntimePresentation(presentation)")) "Projection generated cards must materialize dynamic names, descriptions, and icons through the native-readable presentation path."
    Assert-True $projectionActivationService.Contains("DictionaryUtil.Set(config.Vars") "Projection generated cards must write runtime overrides to Vars."
    Assert-True (-not $projectionActivationService.Contains("DictionaryUtil.Set(config.data")) "Projection generated cards must not mutate base config data."
    Assert-True $projectionSummonService.Contains("CompanionPositionOwnershipService.HasForOwner") "Projection summon must enforce the shared projection/spirit position per player owner."
    Assert-True $projectionSummonService.Contains("ShowRejectionCaption(snapshot.RejectionReason)") "Projection rejection snapshots must pass through the localized presentation boundary."
    Assert-True $projectionSummonService.Contains($utf8.GetString([Convert]::FromBase64String("UmVqZWN0T3duZXJBbHJlYWR5SGFzUHJvamVjdGlvbiA9PiAi5oqV5b2x5L2N572u5bey6KKr5Y2g55So44CCIg=="))) "Projection rejection reasons must map stable protocol codes to Chinese player text."
    Assert-True (-not $projectionSummonService.Contains("+ snapshot.RejectionReason")) "Projection rejection protocol text must never be appended directly to player captions."
    Assert-True $projectionSummonService.Contains("ShowLocalRejectionIfNeeded") "Projection rejection fallback must avoid duplicate local and network captions."
    Assert-True $projectionSummonService.Contains('TerriasResourceCache.Load<GameObject>("Model/player", true, "projection")') "Projection summon must load the player model through the shared resource cache."
    Assert-True $projectionSummonService.Contains("TerriasIds.ProjectionActionStaffTapCardId") "Projection summon must attach the shared staff-tap action."
    Assert-True $projectionSummonService.Contains("TerriasIds.ProjectionActionShieldBlessingCardId") "Projection summon must attach the shared shield action."
    Assert-True $projectionSummonService.Contains("SpawnProjection(role, ownerStatusId, -1") "Projection summon must stay outside formal friendly slots."
    Assert-True $projectionSummonService.Contains("ProjectionTurnCoordinator.RegisterProjection") "Projection summon must register through the stable projection-turn coordinator."
    Assert-True (-not $projectionSummonService.Contains("manager.ActionQueue.Add(projection)")) "Projection summon must not depend on late insertion into the native immutable action snapshot."
    Assert-True $projectionTurnCoordinator.Contains("ProjectionStateStore.Active()") "Projection turn anchor must resolve projections at execution time so same-round summons are included."
    Assert-True $projectionTurnCoordinator.Contains("CompanionAuthorityService.IsAuthoritative()") "Projection turn execution must remain host/server authoritative."
    Assert-True $projectionTurnCoordinator.Contains("ExecutedThisRound") "Projection turn execution must suppress same-round duplicates."
    Assert-True $projectionTurnCoordinator.Contains("OrderBy(state => state.OwnerPlayerId") "Projection turn execution order must be stable by player owner."
    Assert-True $projectionTurnCoordinator.Contains("pendingRoot.SetActive(false)") "Projection turn anchor must be hidden before native status initialization."
    Assert-True $projectionTurnCoordinator.Contains("finally") "Projection turn anchor creation must clean up failed prefab instances."
    Assert-True $projectionTurnCoordinator.Contains("ResolveAnchorTemplateData") "Projection turn anchor must initialize from complete career data."
    Assert-True $projectionTurnAnchorObj.Contains('TryGetValue("Animation"') "Projection turn anchor must reject incomplete native animation data."
    Assert-True $projectionTurnAnchorObj.Contains("EnsureActionIcons(status)") "Projection turn anchor must satisfy native OtherObj action UI prerequisites before registration."
    Assert-True $projectionTurnAnchorObj.Contains("status.actionObj.Length < 4") "Projection turn anchor must validate all four native action object slots."
    Assert-True $projectionTurnAnchorObj.Contains('keyword.text = ""') "Projection turn anchor action placeholders must remain visually empty."
    Assert-True $projectionTurnAnchorObj.Contains("icon.SetActive(false)") "Projection turn anchor action placeholders must remain hidden."
    Assert-True $projectionTurnAnchorObj.Contains("MaxActionCount = 0") "Projection turn anchor must not pop a card from its intentionally empty native action queue."
    Assert-True $projectionTurnAnchorObj.Contains("ActionCount = 0") "Projection turn anchor must expose zero native card actions."
    Assert-True (-not $projectionTurnCoordinator.Contains("Math.Max(1, anchor.MaxActionCount)")) "Projection turn requeue must not restore a fake native card action."
    Assert-True $projectionTurnAnchorObj.Contains("public sealed class ProjectionTurnAnchorObj : OtherObj") "Projection turns must reserve a native action snapshot slot before player summons occur."
    Assert-True $projectionRuntime.Contains("ProjectionTurnCoordinator.BeginPlayerRound") "Projection runtime must advance the coordinator round token from the native player-turn lifecycle."
    Assert-True (-not $companionSlotService.Contains("NearestSlot")) "Friendly logical slots must not be inferred from stale world coordinates."
    Assert-True $companionSlotService.Contains("ReflowFriendlyLineup") "Friendly companions must share one dynamic lineup reflow path."
    Assert-True $companionSlotService.Contains("friendlyCount") "Friendly lineup coordinates must use the current unit count."
    Assert-True (-not $companionSlotService.Contains("ProjectionStateStore.Active")) "Owner-bound projections must not participate in formal lineup reflow."
    Assert-True $heartChangeControlService.Contains('ReflowFriendlyLineup(source + ".Cleared")') "Heart Change cleanup must compact the remaining friendly lineup."
    Assert-True $fightUiCardLayoutApi.Contains("parameters.Length == 2") "FightUI card layout compatibility must support the current two-parameter optional signature."
    Assert-True $fightUiCardLayoutApi.Contains("new object?[parameterCount]") "FightUI card layout compatibility must invoke optional parameters explicitly through reflection."
    Assert-True $projectionSummonService.Contains("CompanionStatsService.ProjectionStats") "Projection summon must derive independent companion stats."
    Assert-True $projectionOtherObj.Contains("public sealed class ProjectionOtherObj : OtherObj") "Projection actors must stay friendly OtherObj objects, not real partners."
    Assert-True $projectionOtherObj.Contains("EnsureActionIcons") "Projection actors must create action icons because native OtherObj does not."
    Assert-True $projectionOtherObj.Contains("CompanionBattleStateStore.Create") "Projection actors must create companion runtime state."
    Assert-True $projectionOtherObj.Contains("CompanionIntentPlanner.Create") "Projection actors must create an authoritative immutable turn plan."
    Assert-True $projectionOtherObj.Contains("NormalizeProjectionActionConfig(actionConfig") "Projection action cards must be normalized before ObjectCard.Init."
    Assert-True $projectionOtherObj.Contains('DictionaryUtil.Set(config.Vars, "CD", "0")') "Projection action cards must always expose native CD."
    Assert-True $companionIntentPlanner.Contains('"[ProjectionPlan] committed"') "Projection authoritative plans must emit one commit diagnostic."
    Assert-True $companionIntentPlanner.Contains("CompanionAuthorityService.IsAuthoritative()") "Projection plan diagnostics must be authority-gated."
    Assert-True $starScoreRuntime.Contains("StarScore.RefreshSignatureSkip") "Star score preview refreshes must skip unchanged signatures."
    Assert-True $starScoreRuntime.Contains("LastRefreshSignatures.Clear") "Star score preview signatures must reset per fight."
    Assert-True $morningStarDimmedService.Contains("TerriasCardRefreshQueue.RequestCostUpdate") "Morning Star Dimmed cost-only changes must use incremental refreshes."
    Assert-True $cardScripts.Contains('RequestCostUpdate(card, "FlamewheelHand")') "Flamewheel recurrence cost-only changes must use incremental refreshes."
    Assert-True $terriasResourcePreloader.Contains("WarmupTier.Essential") "Adventure preload must separate essential and opportunity work."
    Assert-True $terriasResourcePreloader.Contains("ResourcePreloader.EssentialCompleted") "Adventure preload must report essential completion."
    Assert-True $terriasResourcePreloader.Contains("StarScoreHudAssets.StructuralPaths()") "Adventure preload must keep oversized note icons out of structural warmup."
    Assert-True (-not $terriasResourcePreloader.Contains("PolymorphRoleRegistry.CardFacePaths(12)")) "Adventure preload must defer optional polymorph card-face sources."
    Assert-True $terriasResourcePreloader.Contains("ResourcePreloader.HeavyOptionalDeferred") "Deferred heavy preload work must remain observable."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.ICardSetCardStyle") "Card UI diagnostics must measure card-style application separately."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.CardItemDataUpdate") "Card UI diagnostics must measure data updates separately."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.FightCardManagerCardTagCheck") "Card UI diagnostics must measure tag checks separately."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.FightUiUpdateCardMsg") "Card UI diagnostics must measure whole-hand refresh batches."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.ICardSetCardMsg") "Card UI diagnostics must measure native card-message binding."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.ScriptExecutorRunScript") "Card UI diagnostics must measure InitScript bridge work."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.LocalizeExDescription") "Card UI diagnostics must measure description expansion."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("TerriasHookTargets.TextTranslatorTranslate") "Card UI diagnostics must measure keyword translation."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("RegisterRefreshCauses") "Card UI diagnostics must associate native refresh causes with each batch."
    Assert-True $terriasCombatCardUiWorkloadRuntime.Contains("[ThreadStatic] private static Stack<StartEntry>") "Card UI diagnostics must avoid global hot-path locking."
    Assert-True $combatCardUiDiagnostics.Contains('var prefix = "Nested." + target') "Card UI diagnostics must attribute nested work to its parent stage."
    Assert-True $combatCardUiDiagnostics.Contains('RecordSegment(prefix + "/" + pair.Key') "Card UI diagnostics must retain hierarchical child timings."
    Assert-True $combatCardUiDiagnostics.Contains("private struct Scope") "Card UI diagnostics must avoid per-call scope object allocation."
    Assert-True $combatCardUiDiagnostics.Contains("BeginRefreshBatch") "Card UI diagnostics must model one UpdateCardMsg call as a batch."
    Assert-True $combatCardUiDiagnostics.Contains('"; topCards="') "Card UI diagnostics must report the three slowest cards in a refresh batch."
    Assert-True $combatCardUiDiagnostics.Contains("FightUiDiagnosticsApi.SkillCount") "Card UI diagnostics must include current skill count through GameApi."
    Assert-True $combatCardUiDiagnostics.Contains('"ms[setMsg="') "Slow-card summaries must expose the native SetCardMsg boundary."
    Assert-True $combatCardUiDiagnostics.Contains('",runScript="') "Slow-card summaries must expose nested script time."
    Assert-True $combatCardUiDiagnostics.Contains('",description="') "Slow-card summaries must expose nested description time."
    Assert-True $combatCardUiDiagnostics.Contains('",translate="') "Slow-card summaries must expose nested translation time."
    Assert-True $combatCardUiDiagnostics.Contains('",remainder="') "Slow-card summaries must expose unhookable native presentation work."
    Assert-True $combatCardUiDiagnostics.Contains("WithSetCardMsgFallback") "Card UI diagnostics must fall back to DataUpdate timing when SetCardMsg is not hookable."
    Assert-True $combatCardUiDiagnostics.Contains("context.Target is CardItem") "Card UI diagnostics must resolve DataUpdate card ids from the hook receiver."
    Assert-True $projectionTurnAnchorObj.Contains("AuraGameDataHostApi.ResolveHandle(DataType.Career, templateId)") "Projection turn anchors must derive from a registered career definition."
    Assert-True ($projectionTurnAnchorObj.Contains("DataOverrides = new Dictionary<string, string>(StringComparer.Ordinal)") -and $projectionTurnAnchorObj.Contains("PreCompile = false")) "Projection turn anchors must use minimal overrides and disable script precompilation through the shared materializer."
    Assert-True (-not $projectionTurnAnchorObj.Contains("new Dictionary<string, string>(templateData)")) "Projection turn anchors must not inherit role script fields."
    Assert-True $solarMemoryJourneyApi.Contains('JourneyId = "Terrias:Terrias.SolarMemory"') "Solar Memory journey identity must be owner-qualified without changing its stable id."
    Assert-True $terriasCardPresentationLifecycleBridge.Contains("Card = card") "Card presentation lifecycle must retain the exact initialized CardItem."
    Assert-True $cardPresentationRootResolver.Contains('root.Find("Mask/CardIcon")') "Compact ShowCard surfaces must have an explicit structural adapter."
    Assert-True $cardVisualSkinRuntime.Contains("CardVisualSkin.CompactDisplayHandled") "Compact display fallback must be measurable instead of warning as a root miss."
    Assert-True $projectionOtherObj.Contains("ActivateAfterHydration") "Projection actors must reveal intent after authoritative buff hydration."
    Assert-True $projectionOtherObj.Contains("ProjectionActionExecutor.Execute") "Projection turns must use the dedicated friendly-AI executor."
    Assert-True $projectionOtherObj.Contains("ProjectionStateStore.NotifyActionPresented") "Projection mechanics must request visual feedback through a presentation event."
    Assert-True $projectionStateStore.Contains("ActionPresented") "Projection mechanics must expose hook-safe action presentation."
    Assert-True $projectionAttachmentPresenter.Contains("ProjectionVisualProxy") "Projection visual runtime must isolate attachment presentation in a proxy."
    Assert-True $projectionAttachmentPresenter.Contains("pulseMultiplier") "Projection action feedback must be applied inside proxy layout."
    Assert-True $projectionAttachmentPresenter.Contains("PlayActionFocus") "Projection action feedback must distinguish committed intent focus."
    Assert-True $projectionAttachmentPresenter.Contains("AttackFocusTravelAt1080 = 70f") "Projection attacks must use stronger combat focus travel."
    Assert-True $projectionAttachmentPresenter.Contains("focusProgress") "Projection focus travel must remain integrated into proxy layout."
    Assert-True $projectionAttachmentPresenter.Contains("ResolveFocusDirection") "Projection attacks must focus toward committed targets."
    Assert-True (-not $projectionOtherObj.Contains("FightAction.ActionExecute()")) "Projection turns must not use native enemy target setup."
    Assert-True $projectionActionExecutor.Contains("ProjectionEffectContextService.RefreshLockedPlan") "Projection execution must refresh owner-derived numbers without rerolling intent."
    Assert-True $projectionActionExecutor.Contains("FightActionPresentationApi.PresentCommittedAction") "Projection execution must present committed actions through the native animation facade."
    Assert-True $fightActionPresentationApi.Contains("CallActionAnimation(executor)") "Projection presentation must reuse native combat animation."
    Assert-True $fightActionPresentationApi.Contains("executor.Object.AddRange(previousObjects)") "Projection presentation must restore executor targets."
    Assert-True $fightActionPresentationApi.Contains('RecordDuration("ProjectionAction.NativeAnimation"') "Projection native animation must expose measurable duration."
    Assert-True (-not $fightActionPresentationApi.Contains("ActionExecute")) "Projection presentation must not re-enter native enemy action execution."
    Assert-True $projectionEffectContext.Contains("ModifierOwner") "Projection effect context must separate actor and modifier owner."
    Assert-True $projectionEffectContext.Contains("AttributionOwner") "Projection effect context must retain player attribution."
    Assert-True $projectionEffectContext.Contains("HasActiveConsumableModifier") "Projection effect inheritance must reject consumable modifiers by policy."
    Assert-True (-not $projectionSummonService.Contains("ProjectionBuffCopyService.Capture")) "Projection summon must not copy the owner's buffs."
    Assert-True $projectionAttachmentPresenter.Contains("ProjectionHeightAt1080 = 120f") "Projection attachment must use the approved fixed reference height."
    Assert-True $projectionAttachmentPresenter.Contains("HorizontalOverlapRatio = 1f / 3f") "Projection attachment must overlap one third of its width from the owner right edge."
    Assert-True $projectionAttachmentPresenter.Contains("targetScreenHeight = Screen.height * ProjectionHeightAt1080 / 1080f") "Projection attachment height must be resolved in screen space."
    Assert-True $projectionAttachmentPresenter.Contains("ownerScreen.xMax - targetScreenWidth * HorizontalOverlapRatio") "Projection proxy center must move left by one third of its width from the owner right edge."
    Assert-True $projectionAttachmentPresenter.Contains("ownerScreen.yMax + targetScreenHeight * 0.5f") "Projection proxy center must remain half its height above the owner top edge."
    Assert-True $projectionAttachmentPresenter.Contains("GetComponent<BoxCollider>()") "Projection attachment must resolve native alpha-trimmed AABB colliders."
    Assert-True $projectionAttachmentPresenter.Contains("localAabbCenter = center") "Projection proxy must cache native alpha-trimmed AABB geometry."
    Assert-True $projectionAttachmentPresenter.Contains("localAabbSize = size") "Projection proxy must retain its last valid local AABB size."
    Assert-True $projectionAttachmentPresenter.Contains("targetWorldHeight / localAabbSize.y") "Projection height must derive from cached local collider geometry."
    Assert-True $projectionAttachmentPresenter.Contains("var depth = bounds.center.z") "Projection screen AABB must use a common camera depth."
    Assert-True $projectionAttachmentPresenter.Contains("ProjectionAttachment.ProxyLayoutSkipped") "Projection proxy must reject and measure invalid layouts."
    Assert-True $projectionAttachmentPresenter.Contains("lastOwnerBounds") "Projection proxy must retain a last-known-good owner AABB."
    Assert-True $projectionAttachmentPresenter.Contains("hasLayoutSnapshot") "Projection proxy must reuse stable layout snapshots between dirty frames."
    Assert-True $projectionAttachmentPresenter.Contains("if (!layoutChanged)") "Projection proxy must skip full screen/world conversion when layout inputs are unchanged."
    Assert-True $projectionAttachmentPresenter.Contains("lastWorldToCameraMatrix") "Projection layout invalidation must observe camera movement."
    Assert-True $projectionAttachmentPresenter.Contains("ProjectionAttachment.ProxyLayoutApplied") "Projection dirty-layout applications must remain measurable."
    Assert-True $projectionAttachmentPresenter.Contains('new GameObject("Terrias_ProjectionVisualProxy:') "Projection presentation must use a standalone visual proxy."
    Assert-True (-not $projectionAttachmentPresenter.Contains("projection.transform.SetParent")) "Projection gameplay roots must remain under native hierarchy ownership."
    Assert-True (-not $projectionAttachmentPresenter.Contains("projection.transform.localScale")) "Projection layout must not scale gameplay roots."
    Assert-True (-not $projectionAttachmentPresenter.Contains("projection.transform.position")) "Projection layout must not move gameplay roots."
    Assert-True $projectionAttachmentPresenter.Contains("sourceRenderer.enabled = false") "Projection proxy must hide the native body only."
    Assert-True $projectionAttachmentPresenter.Contains("proxyRenderer.sharedMaterial = synchronizedMaterial") "Projection proxy must share source materials."
    Assert-True $projectionAttachmentPresenter.Contains("proxyRenderer.sprite = synchronizedSprite") "Projection proxy must follow source sprites by reference."
    Assert-True $projectionAttachmentPresenter.Contains("sortingOrder - 1") "Projection attachment must render below its owner body."
    Assert-True $projectionAttachmentPresenter.Contains("IntentIconScale = 0.60f") "Projection intent icons must remain compact while retaining native hover components."
    Assert-True $projectionAttachmentPresenter.Contains("IntentGapAt1080 = 14f") "Projection intent must remain above the visible projection boundary."
    Assert-True $projectionAttachmentPresenter.Contains("private void LateUpdate()") "Projection attachment must follow native focus movement."
    Assert-True $projectionStateStore.Contains("IntentPresented") "Projection mechanics must expose a hook-safe intent presentation event."
    Assert-True $projectionOtherObj.Contains("NotifyIntentPresented") "Projection intent rebuilds must notify the dedicated presenter."
    Assert-True $projectionIntentPresenter.Contains("ResetAllLines(status)") "Projection intent presentation must clear stale native lines before rebinding."
    Assert-True $projectionIntentPresenter.Contains('string.Equals(effectIntent.Target.Mode, "All"') "All-target companion intents must hide misleading single-target lines."
    Assert-True $projectionIntentPresenter.Contains("SpiritStateStore.IntentPresented += BindCommittedPlan") "Spirit intent lines must share the committed-plan presenter with projections."
    Assert-True $projectionIntentPresenter.Contains("IsValidCommittedTarget") "Projection intent presentation must enforce committed target scope."
    Assert-True $projectionIntentPresenter.Contains("IPointerEnterHandler, IPointerExitHandler") "Projection intent lines must be hover-driven."
    Assert-True $projectionIntentPresenter.Contains("hoverLine.Configure(line, targetUi.transform)") "Projection plan binding must not immediately reveal target lines."
    Assert-True $projectionIntentPresenter.Contains("private void OnDisable()") "Projection intent lines must clear when their icon is disabled."
    Assert-True $pooledCardExitAnimator.Contains("List<TextBinding> burnTextBindings") "Pooled burn exits must cache text lifecycle bindings."
    Assert-True $pooledCardExitAnimator.Contains("GetComponentsInChildren<TMP_Text>(true)") "Pooled burn exits must include all card TMP nodes."
    Assert-True $pooledCardExitAnimator.Contains("RefreshTextBindings") "Pooled burn exits must refresh dynamic text after native card initialization."
    Assert-True $pooledCardExitAnimator.Contains("text.enabled = false") "Pooled burn exits must hide text before the burn shader starts."
    Assert-True $pooledCardExitAnimator.Contains("originalEnabled") "Pooled burn exits must restore each text node's enabled state."
    Assert-True $pooledCardExitAnimator.Contains('RecordDuration("PooledCardExit.BurnTextPrepare"') "Pooled burn text hiding must expose measurable duration."
    Assert-True $pooledCardExitAnimator.Contains('RecordDuration("PooledCardExit.BurnFrameCpu"') "Pooled burn profiling must measure per-frame CPU work separately."
    Assert-True $pooledCardExitAnimator.Contains('RecordDuration("PooledCardExit.BurnWallDuration"') "Pooled burn profiling must label coroutine wall duration explicitly."
    Assert-True $pooledCardExitAnimator.Contains("burnBindingCount == 0") "Pooled burn exits must detect when no native mesh renderer can receive CardBurn."
    Assert-True $pooledCardExitAnimator.Contains("PooledCardExit.BurnFallbackStarted") "Pooled burn exits must run a visible fallback instead of disappearing when CardBurn cannot bind."
    Assert-True $pooledCardExitAnimator.Contains("GameSpeed.Duration(1.5f)") "Pooled burn timing must follow the native game-speed duration contract."
    Assert-True $combatCardViewPoolCatalogText.Contains("PresentationSignature") "Pooled dynamic cards must use deterministic presentation signatures."
    Assert-True $combatCardViewPoolText.Contains("TryLightweightRebind") "Pooled dynamic cards must offer a guarded lightweight rebind path."
    Assert-True $combatCardViewPoolText.Contains("ICard.SetCardMsg(card.transform, config, null)") "Lightweight rebinding must use native card presentation binding."
    Assert-True $combatCardViewPoolText.Contains("marker.PresentationSignature") "Lightweight rebinding must require an exact retained signature."
    Assert-True $combatCardViewPoolText.Contains("ReapplyPresentationAfterBind") "Every pooled bind must restore registered skins after idle cleanup."
    Assert-True $cardVisualSkinRuntime.Contains("PrepareForBurnVisualHandoff") "Card visual effects must hand the real skin texture to CardBurn before burn rendering starts."
    Assert-True $pooledCombatCardViewMarkerText.Contains("HasInitializedPresentation") "Pooled views must require one complete Init before lightweight rebinding."
    Assert-True $projectionActionExecutor.Contains("IsValidCommittedTarget") "Projection execution must reject targets outside the committed intent scope."
    Assert-True $companionFriendlyRosterService.Contains("manager.roleQueue") "The canonical friendly roster must use the fight role queue."
    Assert-True $companionFriendlyRosterService.Contains("ActiveSlotStatuses()") "The canonical friendly roster must include actively controlled units."
    Assert-True (-not $companionFriendlyRosterService.Contains("Singleton<TempDataManager>")) "The canonical friendly roster must not read the ownership routing map."
    Assert-True $companionSlotService.Contains("CompanionFriendlyRosterService.Snapshot") "Friendly slot layout must use the canonical friendly roster."
    Assert-True $companionIntentHandlers.Contains("CompanionFriendlyRosterService.Snapshot") "Friendly intent resolution must use the canonical friendly roster."
    Assert-True $companionIntentHandlers.Contains("CompanionFriendlyRosterService.Contains") "Committed target validation must use the canonical friendly roster."
    Assert-True (-not $companionIntentHandlers.Contains("Singleton<TempDataManager>")) "Companion target policies must not read the ownership routing map."
    Assert-True (-not $projectionOtherObj.Contains("return base.DoAction();")) "Projection turns must not use native OtherObj.DoAction because the player model lacks head/Msg."
    Assert-True $projectionStateStore.Contains("ProjectionStatusIdPrefix") "Projection status ids must use the centralized friendly projection prefix."
    Assert-True $projectionStateStore.Contains("removeStatusRecords: false") "Projection retirement must leave status records long enough for native hit queues to settle."
    Assert-True $projectionRuntime.Contains('TerriasStatusLifecycleRouter.Register("Projection"') "Projection runtime must retire dead projections through the shared status lifecycle router."
    Assert-True $projectionRuntime.Contains("CardItem.canUse = false") "A duplicate projection summon card must be gated before use."
    Assert-True $projectionRuntime.Contains("RestoreProjectionUseGate") "Projection use gating must restore the native global card state."
    Assert-True $projectionRuntime.Contains("AfterHit = RetireProjectionAfterDamage") "Projection runtime must retire dead projections after full damage resolves."
    Assert-True $projectionRuntime.Contains("AfterCurHpChanged = RetireProjectionAfterHpChange") "Projection runtime must retire dead projections after direct HP changes."
    Assert-True $projectionRuntime.Contains("AfterMaxHpChanged = RetireProjectionAfterHpChange") "Projection runtime must retire projections whose max HP is reduced to zero."
    Assert-True (-not $projectionRuntime.Contains("SetDamageFilter")) "Projection runtime must not use temporary damage filters after protection redirects were removed."
    Assert-True (-not $projectionRuntime.Contains("RedirectThreatBeforeHit")) "Projection runtime must not redirect enemy attacks away from players."
    Assert-True (-not $projectionRuntime.Contains("ProjectionThreatService")) "Projection runtime must not depend on retired threat redirection."
    Assert-True $projectionStateStore.Contains("RetireIfDead") "Projection state store must expose a shared death retirement guard."
    Assert-True $projectionStateStore.Contains("TerriasFrameDispatcher.RunOnceNextFrame") "Projection retirement must delay status-record removal until native queues settle."
    Assert-True $projectionStateStore.Contains("CompanionBattleStateStore.Remove") "Projection retirement must clear companion runtime state."
    Assert-True (-not $projectionStateStore.Contains("ThreatBoost")) "Projection state must not keep retired threat-weight state."
    Assert-True (-not $projectionStrategyService.Contains("MarkShielded")) "Projection shield behavior must not modify retired threat weights."
    Assert-True $projectionStrategyService.Contains("CompanionIntentExecutor.UseAction") "Projection strategy must delegate shared action behavior to companion intents."
    Assert-True $projectionScripts.Contains("ProjectionStrategyService.UseAction") "ProjectionScripts must keep CSV actions routed through Mechanics."
    Assert-True $companionBattleModels.Contains("CompanionIntentTendency") "Companion models must define attack/defense tendencies."
    Assert-True $companionBattleModels.Contains("CompanionIntentType") "Companion models must define companion intent types."
    Assert-True (-not $companionIntentSelector.Contains("PickType")) "Companion intent selection must not multiply priority through a second type lottery."
    Assert-True $companionIntentSelector.Contains("PickWeighted") "Companion intent selection must use weighted random selection."
    Assert-True $companionIntentSelector.Contains("CompanionIntentResolver.TendencyWeightsFor") "Companion intent tendency must resolve explicit projection or spirit profile weights independent of pool size."
    Assert-True $companionIntentSelector.Contains("CompanionThreatService.ThreatPressurePercent") "Companion intent priority must react to normalized 80-200 companion threat."
    Assert-True $companionBattleStateStore.Contains("CompanionThreatService.Register") "Companion battle state creation must register threat state."
    Assert-True $companionBattleStateStore.Contains("CompanionThreatService.Remove") "Companion battle state removal must clear threat state."
    Assert-True $companionIntentRegistry.Contains("companion.intent.registry.json") "Companion intent pools must be data-driven through the registry."
    Assert-True $companionIntentRegistryJson.Contains('"staff_tap"') "Companion intent registry must define the common staff-tap intent."
    Assert-True $companionIntentRegistryJson.Contains('"shield_blessing"') "Companion intent registry must define the common magic-shield intent."
    Assert-True $companionIntentRegistryJson.Contains('"staff_combo"') "Companion intent registry must define Staff Bonk Barrage."
    Assert-True $companionIntentRegistryJson.Contains('"magic_interference"') "Companion intent registry must define Mana Disruption."
    Assert-True $companionIntentRegistryJson.Contains('"you_are_enhanced"') "Companion intent registry must define the group Extraordinary intent."
    Assert-True $companionIntentRegistryJson.Contains('"buffStacks": 50') "Companion support intents must preserve the approved 50 Extraordinary stacks."
    Assert-True $companionIntentHandlers.Contains("ICompanionIntentHandler") "Companion effects must execute through a handler registry."
    Assert-True $companionIntentHandlers.Contains("DamageMulti") "Companion handlers must support multi-hit damage."
    Assert-True $companionIntentHandlers.Contains("HealSingle") "Companion handlers must support single-target healing."
    Assert-True $companionIntentRegistryJson.Contains('"threat"') "Companion intent registry must declare intent threat."
    Assert-True $companionSlotService.Contains("MaxFriendlySlots = 4") "Companion slots must use the four friendly player-side slots."
    Assert-True $companionSlotService.Contains("ReservedPlayerSeatCount") "Formal slots must reserve player seats across death and resurrection."
    Assert-True $companionThreatService.Contains("TryRedirectEnemySingleTarget") "Companion threat must expose weighted enemy single-target redirection."
    Assert-True $companionThreatService.Contains("AddActiveCompanionsToAllTargets") "Companion threat must expose all-target companion expansion."
    Assert-True $companionThreatService.Contains("OwnerThreat") "Projection threat must be accumulated onto its player owner."
    Assert-True $companionThreatService.Contains("BaseThreat = 0") "Projection threat must not derive from internal HP or armor."
    Assert-True (-not $companionThreatService.Contains("roleQueue.Add")) "Companion threat must not add projections to the native player role queue."
    Assert-True $companionThreatRuntime.Contains('RegisterAfter(modConfig, "ScriptExecutor.SetStatus", ExtendEnemyTargetsAfterSetStatus);') "Companion threat runtime must hook enemy SetStatus after native target construction."
    Assert-True $companionThreatRuntime.Contains("executor.Self?.fatherObject is not Enemy") "Companion threat runtime must only extend enemy target selection."
    Assert-True $companionStatsService.Contains('"Strength"') "Companion stats must derive magic from the Strength origin key."
    Assert-True $companionStatsService.Contains('"Lucky"') "Companion stats must derive spirit from the Lucky origin key."
    Assert-True $companionStatsService.Contains('"Wisdom"') "Companion stats must derive luck from the Wisdom origin key."
    Assert-True $companionStatsService.Contains('"Perceive"') "Companion stats must derive perception from the Perceive origin key."
    Assert-True $terriasIds.Contains("HeartChangeActionStrikeCardId") "Heart Change must centralize its temporary EnemyCard id."
    Assert-True $heartChangeControlService.Contains('QueueProxyAction(state, "Apply")') "Heart Change must queue a proxy action as soon as control is applied."
    Assert-True $heartChangeControlService.Contains("manager.ActionQueue.Add(proxy)") "Heart Change must place the proxy actor in the action queue."
    Assert-True $heartChangeControlService.Contains("CompleteProxyAction") "Heart Change must expose a proxy completion path that ends control immediately after the proxy action."
    Assert-True $heartChangeControlService.Contains("consumeNativeAction") "Heart Change cleanup must distinguish proxy completion from plain control cancellation."
    Assert-True $heartChangeControlService.Contains("ApplyFriendlyFacing(state)") "Heart Change must mirror the controlled enemy while it occupies a friendly slot."
    Assert-True $heartChangeControlService.Contains("HeartChange.NetworkDuplicateAcceptedAsNoOp") "Heart Change repeated network state must be idempotent."
    Assert-True $heartChangeControlService.Contains("BodyRenderer.flipX") "Heart Change must mirror only the visual body."
    Assert-True $heartChangeControlService.Contains("Math.Abs(restoredScale.x)") "Heart Change restore must keep root scale positive for colliders."
    Assert-True (-not $heartChangeControlService.Contains("scale.x = -originalX")) "Heart Change must not negatively scale the collider-owning root."
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
    Assert-True $heartChangeActionProxyObj.Contains("TerriasIds.HeartChangeActionStrikeCardId") "Heart Change proxy must build its preview from the dedicated temporary EnemyCard."
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
    Assert-True $naturalMorningStar.Value.Contains("MiracleClockService.ResetToMaxAndGrantStarlight") "Natural Morning Star must reset Miracle Clock through its buff service."
    Assert-True (-not $naturalMorningStar.Value.Contains("StarStonePouchService.ResetPouch")) "Natural Morning Star must not reset the independent Star Stone Pouch."
    Assert-True $naturalMorningStar.Value.Contains("RequestGuidanceSelection") "Natural Morning Star must reselect Guidance after copying it."
    $stoneDraw = [regex]::Match($starStonePouchService, "private\s+static\s+void\s+DrawForAction[\s\S]*?private\s+static\s+void\s+PublishDrawn")
    Assert-True $stoneDraw.Success "Could not locate Star Stone Pouch draw flow for source assertion."
    Assert-True $stoneDraw.Value.Contains("var blackStonesRemaining = state.BlackStoneCount();") "A white stone must count the black stones currently remaining in the pouch."
    Assert-True $stoneDraw.Value.Contains("var starlightGain = stone == WhiteStone ? blackStonesRemaining : 1;") "Star Stone Pouch must derive black and white stone Starlight gains inside the buff service."
    Assert-True $stoneDraw.Value.Contains("StarScoreService.AddStarlight(self, starlightGain);") "Star Stone Pouch must grant Starlight from the draw result."
    Assert-True $stoneDraw.Value.Contains("PublishDrawn(self, new StarStonePouchDrawResult") "Star Stone Pouch must publish draw results for role-specific reactions."
    $borrowedMiracle = [regex]::Match($loneerService, "private\s+static\s+void\s+TriggerBorrowedMiracle[\s\S]*?private\s+static\s+void\s+EnsureInitialized")
    Assert-True $borrowedMiracle.Success "Could not locate Borrowed Miracle for source assertion."
    Assert-True $borrowedMiracle.Value.Contains("MiracleClockService.ReduceMax") "Borrowed Miracle must reduce the Miracle Clock combat cap through its buff service."
    Assert-True $borrowedMiracle.Value.Contains("MiracleClockService.ResetToMaxAndGrantStarlight") "Restoring the Miracle Clock must grant Starlight equal to its cap."
    Assert-True (-not $borrowedMiracle.Value.Contains("StarStonePouchService.ResetPouch")) "Borrowed Miracle must not reset the independent Star Stone Pouch."
    Assert-True $borrowedMiracle.Value.Contains("RequestGuidanceSelection") "Borrowed Miracle must reselect Guidance after copying it."
    Assert-True (-not $loneerService.Contains("ResetPouchAndClock")) "Loneer must not keep a combined pouch-and-clock reset helper."
    Assert-True $loneerCareerText.Contains("When the Star Stone Pouch draws a white stone") "Loneer career text must describe only Loneer's reaction to Star Stone Pouch draws."
    Assert-True $buffText.Contains("When the Miracle Clock is restored to its cap, gain {Terrias_terrias_starlight} equal to that cap.") "Miracle Clock text must describe its Starlight restoration reward."
    Assert-True $buffText.Contains("After each action, draw one Star Stone.") "Star Stone Pouch text must own the every-action draw rule."
    Assert-True $buffText.Contains("If it is black, gain 1 {Terrias_terrias_starlight}") "Star Stone Pouch text must describe black-stone Starlight gain."
    Assert-True $buffText.Contains("equal to the current number of black stones.") "Star Stone Pouch text must describe white-stone Starlight gain."
    Assert-True ([regex]::IsMatch($buffData, '(?m)^"star_stone_pouch".*"TRUE"\r?$')) "Star Stone Pouch buff data must allow a zero-layer pouch while the white stone remains."
    Assert-True ([regex]::IsMatch($buffData, '(?m)^"miracle_clock".*"TRUE"\r?$')) "Miracle Clock buff data must allow a zero-layer clock so depletion can be observed before reset."
    Assert-True $buffData.Contains('BuffScripts.Apply(self, ""star_stone_pouch"")') "Star Stone Pouch buff data must call its apply script."
    Assert-True $buffData.Contains('BuffScripts.Clear(self, ""star_stone_pouch"")') "Star Stone Pouch buff data must call its clear script."
    $buffRows = Import-Csv -LiteralPath (Join-Path $RepoRoot "Terrias\Data\Buff\terrias.csv")
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
    Assert-True $buffText.Contains("gain 1/1/1 stacks of {Terrias_terrias_star_blessing}") "Starlight text must grant one Star Blessing at each threshold."
    $positiveExcludeIdsBlock = [regex]::Match($buffApi, "PositiveExcludeIds[\s\S]*?\};")
    Assert-True $positiveExcludeIdsBlock.Success "Could not locate BuffApi.PositiveExcludeIds for source assertion."
    Assert-True (-not $positiveExcludeIdsBlock.Value.Contains("TerriasIds.SolarRadiance")) "Solar Radiance must enter global positive buff logic."
    Assert-True (-not $positiveExcludeIdsBlock.Value.Contains("TerriasIds.GatheredFlame")) "Gathered Flame must enter global positive buff logic."
    $solarCrownTriggerBlock = [regex]::Match($solarRadianceService, "private\s+static\s+bool\s+TriggerSolarCrown[\s\S]*?private\s+static\s+string\s+SolarCrownEffectSummary")
    Assert-True ($solarCrownTriggerBlock.Success -and $solarCrownTriggerBlock.Value.Contains("BuffApi.RemoveNegativeBuffsAndTotalExcept") -and $solarCrownTriggerBlock.Value.Contains("TerriasIds.GatheredFlame") -and $solarCrownTriggerBlock.Value.Contains("TerriasIds.Burn") -and $solarCrownTriggerBlock.Value.Contains("TerriasIds.BodyBurn")) "Solar Crown tier 1 must exclude Gathered Flame, Burn, and Body Burn before converting negative buffs to Burn."
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
    Assert-True ($stellarRows.Count -eq 4) "Terrias must define all four Stellar Overture cards."
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
    Assert-True (-not $loneerService.Contains("TerriasIds.LoneerGuidanceCardId")) "Loneer guidance must not be stored in per-executor Vars."
    Assert-True $starScoreService.Contains("StarScoreCombatStateStore.GetOrCreate(self.Self)") "Star score notes must be owner-scoped across card executors."
    Assert-True $starScoreState.Contains("while (notes.Count > Math.Max(1, windowSize))") "Star score must maintain a bounded sliding window."
    Assert-True $starScoreState.Contains("RetainLastNoteAsCadenceStart") "Star score must retain the last overture after a completed cadence."
    Assert-True $starScoreService.Contains("state.RetainLastNoteAsCadenceStart();") "Star score cadence resolution must seed the next cadence with the final overture."
    Assert-True $starScoreService.Contains("DrawCardsForFriendlyParty(self, 2);") "Start-Start-Start cadence must make the friendly party draw two cards."
    Assert-True ([regex]::IsMatch($starScoreService, 'case NoteStart \+ NoteSustain \+ NoteTurn:[\s\S]*self\.AddBuff\(TerriasIds\.Resonance, "1"\);[\s\S]*AddBuffToFriendlyParty\(self, TerriasIds\.Resonance, 1\);')) "Start-Sustain-Turn cadence must grant self resonance and friendly-party resonance."
    Assert-True $duskPartnerScripts.Contains("TerriasDuskAfterheatHook") "Dusk trait scripts must remain in the Dusk module."
    Assert-True $scriptEventApi.Contains("TryAddOwnedEventListener") "ScriptEventApi must contain native-owner listener registration behind the GameApi boundary."
    Assert-True $starClayDollScripts.Contains("TerriasStarClayDollHook") "Star Clay Doll trait scripts must remain in the Star Clay module."
    Assert-True $starClayDollScripts.Contains('ExecutorApi.TryAddTokenedEvent(self, "ActionAfter"') "Star Clay Doll must grant starlight after an action resolves through the shared tokened event wrapper."
    Assert-True $entry.Contains("Terrias.Dll.Scripting.DuskPartnerScripts") "XLua registration must expose the Dusk script entry point."
    Assert-True $entry.Contains("Terrias.Dll.Scripting.StarClayDollScripts") "XLua registration must expose the Star Clay Doll script entry point."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^dusk_afterheat_recovery,0,,,Mods/Terrias/ModResource/Images/Buff/Terrias/huanghun_1,[^,]*,,5\r?$")) "Dusk afterheat recovery must remain a legal zero-weight technical Blessing for GameEntryUI.CheckCareer."
    Assert-True ($originMilestoneBlessingRows.Count -eq 4) "The universal 50-point origin milestone must provide one Blessing for each origin."
    Assert-True (($originMilestoneBlessingRows | Where-Object { $_.FightScript -notlike 'CS.Terrias.Dll.Scripting.BlessingScripts.Fight*' }).Count -eq 0) "Origin milestone Blessings must delegate through the stable C# Blessing entry point."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^dusk,10,0,0,0,2,,,Mods/Terrias/ModResource/Images/Partner/Terrias/dusk_choice,Mods/Terrias/ModResource/Images/Partner/Terrias/dusk,Mods/Terrias/ModResource/AnimationLib/Dusk,Terrias_terrias_dusk_afterheat_recovery,Mods/Terrias/ModResource/Images/Partner/Terrias/dusk\r?$")) "Dusk partner must keep a non-empty Bless column because GameEntryUI.CheckCareer creates a DataConfig from it."
    Assert-True ([regex]::IsMatch($blessingData, "(?m)^star_clay_doll_placeholder,0,,,[^,]+,[^,]*,,5\r?$")) "Star Clay Doll must use a non-conflicting technical Blessing id."
    Assert-True (-not [regex]::IsMatch($blessingData, "(?m)^star_clay_doll_trait,")) "Star Clay Doll Blessing id must not collide with its Buff id."
    Assert-True ([regex]::IsMatch($partnerData, "(?m)^star_clay_doll,10,0,0,0,2,,,Mods/Terrias/ModResource/Images/Partner/Terrias/RenKui_choice,Mods/Terrias/ModResource/Images/Partner/Terrias/RenKui,Mods/Terrias/ModResource/AnimationLib/Dusk,Terrias_terrias_star_clay_doll_placeholder,Mods/Terrias/ModResource/Images/Partner/Terrias/RenKui\r?$")) "Star Clay Doll partner must reference its own images and non-conflicting placeholder Blessing."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("IsTechnicalBlessing(id)") "Solar memory blessing picker must skip technical partner blessings."
    Assert-True ($solarMemoryBlessingPickerRuntime.Contains("AuraGameDataCatalogRuntime.SnapshotChanged += OnCatalogSnapshotChanged") -and $solarMemoryBlessingPickerRuntime.Contains("BuildBlessingPools();") -and $solarMemoryBlessingPickerRuntime.Contains("RefreshAll();")) "An open Solar Memory blessing picker must rebuild when the native game-data catalog becomes ready."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryDeckIsolationRuntime.Initialize(modConfig)") "Solar memory mode runtime must delegate deck isolation hook registration."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains('"GameConfigManager.CardPackCheck"') "Solar memory must filter event cards before CardPackCheck builds reward candidates."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryMapLifecycleCoordinator.Initialize(modConfig)") "Solar memory mode runtime must delegate map generation and synchronization hook registration."
    Assert-True $solarMemoryMapLifecycleCoordinator.Contains('RegisterBefore(modConfig, "NormalMapManager.RandomGenerate", CaptureSolarMemoryGenerationState)') "Solar memory must capture event records before base map generation can draw ordinary events."
    Assert-True $solarMemoryMapLifecycleCoordinator.Contains('RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", EnsureSolarMemoryMapBeforeSelect)') "Solar memory must normalize SelectNode immediately before map candidate cards are created."
    Assert-True (-not $solarMemoryModeRuntime.Contains('RegisterBefore(modConfig, "MapManager.TryChange", RouteSolarFinaleBeforeMapChange)')) "Solar finale must not open EventUI from the generic TryChange hook; that can recurse through event init failure."
    Assert-True (-not $solarMemoryModeRuntime.Contains('ShowEventUIWithTurn<MapSelectUI>("MapSelectUI", TerriasIds.SolarFinaleFullSaintGateEventId)')) "Solar finale must not open the saint gate event from map transition hooks."
    Assert-True (-not $solarMemoryModeRuntime.Contains("EnterSolarFinaleLayer")) "Solar memory must not route into a dedicated finale map layer."
    Assert-True (-not $solarMemoryModeRuntime.Contains("RepairSolarFinaleMapArrays")) "Solar memory must not force finale map candidates into a pre-boss dialogue or saint boss."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemorySettlementCoordinator.Initialize(modConfig)") "Solar memory mode runtime must delegate final-layer and legacy-save settlement."
    Assert-True $solarMemorySettlementCoordinator.Contains('"NormalMapManager.MapItemInit"') "Solar memory must settle legacy level-30 saves before native MapItemInit indexes map lists."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryBossTransitionCoordinator.Initialize(modConfig)") "Solar memory mode runtime must delegate boss-win routing to the boss transition coordinator."
    Assert-True $solarMemoryModeRuntime.Contains("SolarMemoryBattleExitCoordinator.Initialize(modConfig)") "Solar memory mode runtime must delegate fight-abort hook registration to the exit coordinator."
    Assert-True $solarMemoryBattleExitCoordinator.Contains("TerriasHookTargets.FightEscapeResetStates") "Solar memory fight escape coordinator must own the native reset boundary."
    Assert-True $solarMemoryBattleExitCoordinator.Contains("TerriasHookTargets.FightLossInit") "Solar memory fight loss coordinator must own the native loss boundary."
    Assert-True $solarMemoryBattleExitCoordinator.Contains('EnsureCurrentNodeForTransition("Fight_Escape.ResetStates:before")') "Solar memory escape must repair current node before MapManager.TryChange can consume it."
    Assert-True $solarMemoryBattleExitCoordinator.Contains('CloseTransientUi("Fight_Escape.ResetStates:after")') "Solar memory escape must close transient setup UI after native fight reset."
    Assert-True (-not $solarMemoryModeRuntime.Contains("ClearSolarFinalePendingBattle")) "Solar memory must not retain pending finale-battle cleanup after retiring finale events."
    Assert-True $solarMemoryBattleExitCoordinator.Contains('TerriasUiSafety.DisableRaycastsAndDestroyByName("TerriasSolarMemoryStarterDeck", source, LogPrefix)') "Solar memory UI cleanup must route starter-deck teardown through TerriasUiSafety."
    Assert-True (-not $solarMemoryModeRuntime.Contains("handlingSolarMemoryFightAbort")) "Solar memory mode runtime must not retain fight-abort coordination state."
    Assert-True (-not $solarMemoryModeRuntime.Contains("PrepareSolarMemoryFightAbort")) "Solar memory mode runtime must not retain fight-abort hook handlers."
    Assert-True (-not $solarMemoryModeRuntime.Contains("TerriasUiSafety.DisableRaycastsAndDestroyByName")) "Solar memory mode runtime must not retain fight-abort UI teardown."
    Assert-True (-not $solarMemoryModeRuntime.Contains("SettleSolarMemoryBossAfterWin")) "Solar memory mode runtime must not retain boss-win hook handlers."
    Assert-True (-not $solarMemoryModeRuntime.Contains("solarMemoryStorySettlementPending")) "Solar memory mode runtime must not retain boss-dialogue pending state."
    Assert-True (-not $solarMemoryModeRuntime.Contains("FinishSolarMemoryAfterFinalLayer")) "Solar memory mode runtime must not retain final-layer settlement handlers."
    Assert-True (-not $solarMemoryModeRuntime.Contains("SettleLegacyTerminalLevelBeforeMapItems")) "Solar memory mode runtime must not retain legacy-save settlement handlers."
    Assert-True $solarMemorySettlementCoordinator.Contains("SolarMemoryBossTransitionCoordinator.IsSettlementPending") "Solar memory settlement gates must observe coordinator-owned dialogue state."
    Assert-True $solarMemorySettlementCoordinator.Contains("SolarMemorySettlementPresenter.Show()") "Solar memory settlement coordinator must delegate native GameExitUI presentation."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("SolarMemorySettlementCoordinator.CompleteSolarMemoryRunForSettlement") "Solar memory boss completion must delegate to the settlement coordinator."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("TerriasHookTargets.FightWinResetStates") "Solar memory boss transition coordinator must own the native victory boundary."
    Assert-True $terriasUiSafety.Contains("UiRaycastSafeDestroyRuntime.DisableAndHide") "Solar memory UI cleanup must disable and hide UI before destroying it."
    Assert-True $terriasUiSafety.Contains("ScrubGraphicRegistryForFrames") "Solar memory UI cleanup must scrub stale graphics after transient UI teardown."
    Assert-True $terriasUiSafety.Contains("Object.Destroy(root)") "Solar memory UI cleanup must destroy only after disabling raycasts."
    Assert-True $terriasModalHost.Contains("TerriasUiSafety.CloseTransient") "Terrias modal host must centralize transient UI teardown."
    Assert-True $dirtyState.Contains("public sealed class TerriasDirtyState") "Repeated UI rebuild guards must use a shared dirty-state helper."
    Assert-True $dirtyState.Contains('TerriasPerformanceCounters.Record("DirtyState.Skipped")') "Dirty-state skips must be visible to performance counters."
    Assert-True $terriasUiLifetimeScope.Contains("button.onClick.RemoveListener(action)") "Pooled UI button listeners must be detachable."
    Assert-True $terriasUiPool.Contains("public static class TerriasUiPool") "Terrias local UI pooling must be centralized."
    Assert-True $terriasUiPool.Contains("TerriasPerformanceSettings.UiPoolCapacityPerKey") "Terrias UI pooling must obey performance-tier capacity caps."
    Assert-True $terriasUiPool.Contains("button.onClick.RemoveAllListeners()") "Terrias UI pooling must scrub stale button listeners before reuse."
    Assert-True $terriasUiSprites.Contains("private static readonly Dictionary<string, Sprite?> Cache") "Terrias UI sprites must share a cache across modal windows."
    Assert-True $terriasUiBuilder.Contains("public static Image ApplyPanelImage") "Terrias local UI builder must expose reusable panel image creation."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("TerriasUiBuilder.ApplyPanelImage") "Solar memory starter deck UI must reuse TerriasUiBuilder panel creation."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("TerriasUiBuilder.ApplyPanelImage") "Solar memory blessing picker UI must reuse TerriasUiBuilder panel creation."
    Assert-True $solarMemorySetupFlowRuntime.Contains("TerriasUiBuilder.ApplyPanelImage") "Solar memory setup flow UI must reuse TerriasUiBuilder panel creation."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("TerriasModalHost.Close(ref activePanel") "Solar memory starter deck close must route through TerriasModalHost."
    Assert-True $solarMemorySetupFlowRuntime.Contains("TerriasModalHost.Close(ref activeOriginRoot") "Solar memory origin setup close must route through TerriasModalHost."
    Assert-True $solarMemorySetupFlowRuntime.Contains("TerriasModalHost.Close(ref activeBlessingChrome") "Solar memory blessing setup chrome close must route through TerriasModalHost."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("TerriasModalHost.Close(ref activePanel") "Solar memory blessing picker close must route through TerriasModalHost."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("TerriasUiBuilder.ApplyPanelImage") "Endless Sea intro board must reuse shared panel creation."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("TerriasModalHost.Close(ref activePanel") "Endless Sea intro board close must route through TerriasModalHost."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("ScrollRect") "Endless Sea intro board body must be scrollable."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("supportRichText = true") "Endless Sea intro board must enable controlled rich text."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("EndlessSeaRichTextSanitizer.Sanitize") "Endless Sea intro board must sanitize rich text before display."
    Assert-True (-not $endlessSeaIntroBoardRuntime.Contains("WebView")) "Endless Sea intro board must not embed web content."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("TerriasUiPool.AcquireComponent") "Solar memory starter deck list rows must reuse pooled UI."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("deckListDirty.ShouldRefresh") "Solar memory starter deck selected list must skip unchanged rebuilds."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("TerriasUiPool.AcquireConfiguredComponent") "Solar memory blessing picker list rows must bind pooled UI before activation."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("selectedRows") "Solar memory blessing picker selected rows must reconcile incrementally."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("candidateListDirty.ShouldRefresh") "Solar memory blessing candidates must skip unchanged rebuilds."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("TerriasUiSprites.Button") "Solar memory starter deck must use cached shared button sprites."
    Assert-True $solarMemorySetupFlowRuntime.Contains("TerriasUiSprites.Button") "Solar memory setup flow must use cached shared button sprites."
    Assert-True $solarMemoryBlessingPickerRuntime.Contains("TerriasUiSprites.Button") "Solar memory blessing picker must use cached shared button sprites."
    $solarMemorySetupUiSources = $solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime
    Assert-True (-not $solarMemorySetupUiSources.Contains("CreateNineSliceSprite")) "Solar memory setup windows must not duplicate nine-slice sprite construction."
    Assert-True (-not $solarMemorySetupUiSources.Contains("GetButtonSprite")) "Solar memory setup windows must not keep per-window button sprite caches."
    Assert-True (-not $solarMemorySetupUiSources.Contains("Object.Destroy(active")) "Solar memory setup windows must not directly destroy active modal roots."
    Assert-True $solarMemorySettlementCoordinator.Contains("RouteToNativeSettlement") "Solar memory must settle immediately after the third layer boss."
    Assert-True $solarMemorySettlementCoordinator.Contains("manager.Level = levelForNativeFlow") "Solar memory completion must route through the native settlement level."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemorySettlementCoordinator.ShowSolarMemorySettlement()") "SolarMemoryFlowApi must delegate explicit settlement display to the settlement coordinator."
    Assert-True (-not $eventScripts.Contains("InitSolarFinale")) "Retired solar finale EventList entries must not leave script entry points behind."
    Assert-True (-not $eventScripts.Contains("FinishSolarFinaleEnding")) "Retired solar finale ending must not be opened through EventScripts."
    Assert-True (-not $terriasIds.Contains("SolarFinaleFullEndingEventId")) "Retired solar finale ending event id must not remain in TerriasIds."
    Assert-True $solarMemoryMapLifecycleCoordinator.Contains("RepairSolarMemoryMapSelection") "Solar memory map lifecycle must repair synchronized arrays for fixed nodes."
    Assert-True $solarMemoryModeEntryRuntime.Contains("Mods/Terrias/ModResource/Images/UI/solar_memory_title_c.png") "Solar memory mode entry must load its cropped normal title sprite."
    Assert-True $solarMemoryModeEntryRuntime.Contains("Mods/Terrias/ModResource/Images/UI/solar_memory_title_c_h.png") "Solar memory mode entry must load its cropped highlighted title sprite."
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
    Assert-True $solarMemoryModeEntryRuntime.Contains("TerriasIds.SolarMemoryTitle") "Solar memory mode entry must provide its display name to fallback UI."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("Screen.width")) "Mode choice layout runtime must not mix screen pixels with RectTransform local coordinates."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("rect.anchorMin = new Vector2(0.5f, 0.5f)")) "Mode choice layout runtime must not recenter every native entry."
    Assert-True (-not $modeChoiceLayoutRuntime.Contains("LayoutScale")) "Mode choice layout runtime must not globally scale mode entries."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolApplier, 'defaultStart\s*=\s*pool\.Layer\s*\*\s*pool\.DefaultSegmentSize')) "Solar memory default nodes must be rewritten for the current layer, not only layer 0."
    Assert-True ([regex]::IsMatch($solarMemoryMapNodePoolApplier, 'selectStart\s*=\s*pool\.Layer\s*\*\s*pool\.SelectSegmentSize')) "Solar memory candidate SelectNode entries must be rewritten for the current layer."
    Assert-True $solarMemoryMapNodePoolApplier.Contains("MapNodeSafetyService.EnsureNodeDice(tree, replacement") "Solar memory node pool application must validate replacement NodeDice before inserting nodes."
    Assert-True $solarMemoryMapNodePoolApplier.Contains("TrimSolarMemoryEventRecord") "Solar memory must roll back ordinary event records consumed during base map generation."
    Assert-True $terriasIds.Contains("SolarMemoryEventIds") "Solar memory must define all fixed story event ids."
    Assert-True $terriasIds.Contains("Sub_solar_memory_above_sacred_wheel") "Solar memory id list must include the sixth fixed event."
    Assert-True $terriasIds.Contains("SolarMemoryLayerNames") "Solar memory must define custom layer names."
    Assert-True $solarMemoryMapVisualRuntime.Contains('"MapSelectUI.DataUpdate", SolarMemoryMapProjectionRuntime.ApplySolarMemoryLayerTitle') "Solar memory must override map layer titles through the projection runtime."
    Assert-True $solarMemoryMapVisualRuntime.Contains('"NormalMapManager.MapItemInit", SolarMemoryMapProjectionRuntime.ApplySolarMemoryFixedSlotsAfterMapItems') "Solar memory map visuals must project fixed slots after native map item creation."
    Assert-True $solarMemoryMapVisualRuntime.Contains('"MapSelectUI.ShowMap", SolarMemoryMapLifecycleCoordinator.ReapplySolarMemoryFixedSlotLocks') "Solar memory map visuals must reapply fixed-slot locks through the map lifecycle coordinator."
    Assert-True $solarMemoryMapLifecycleCoordinator.Contains("SolarMemoryMapProjectionRuntime.ApplySolarMemoryFixedSlots") "Solar memory map lifecycle must delegate fixed-slot Unity mutation to the projection runtime."
    Assert-True $solarMemoryMapProjectionRuntime.Contains('VisualRegistry.TexturePath("solar_memory.event_map_card")') "Solar memory projection must resolve event map-card art through the visual registry."
    Assert-True $solarMemoryMapProjectionRuntime.Contains("TerriasResourceCache.Load<Texture>") "Solar memory projection must load map-card textures through the shared resource cache."
    Assert-True $solarMemoryMapProjectionRuntime.Contains("MapItemApi.ApplyCardBackgroundTexture") "Solar memory projection must route MapItem texture compatibility through MapItemApi."
    Assert-True $solarMemoryMapProjectionRuntime.Contains("objectGroup.blocksRaycasts = false") "Solar memory fixed-slot visuals must remain non-blocking for raycasts."
    Assert-True (-not $solarMemoryModeRuntime.Contains("using UnityEngine")) "SolarMemoryModeRuntime must not retain direct Unity visual dependencies."
    Assert-True (-not $solarMemoryModeRuntime.Contains("FixedSlotVisualState")) "SolarMemoryModeRuntime must not retain fixed-slot Unity state components."
    Assert-True (-not $solarMemoryModeRuntime.Contains("RegisterBefore(")) "SolarMemoryModeRuntime must remain a composition root instead of owning hook registration."
    Assert-True (-not $solarMemoryModeRuntime.Contains("MapManager")) "SolarMemoryModeRuntime must not retain map lifecycle implementation."
    Assert-True $solarMemoryFixedNodeSpec.Contains("MidLayerSlotIndex = 3") "Solar memory must reserve the fourth map slot for the second story event in each layer."
    Assert-True $solarMemoryMapNodePoolFactory.Contains("CreateSolarMemoryEventNode(layer, OpeningSlotIndex)") "Solar memory default nodes must use the per-layer opening story event."
    Assert-True (-not $solarMemoryMapNodePoolFactory.Contains("CreateSolarMemoryEventNode(layer, MidLayerSlotIndex)")) "Solar memory SelectNode entries must not expose fixed story events as draggable candidates."
    Assert-True $solarMemoryFixedNodeSpec.Contains("SolarMemoryFixedNodeSpec.Event(MidLayerSlotIndex") "Solar memory fixed-node catalog must lock the fourth map node as the second story event."
    Assert-True $solarMemoryMapProjectionRuntime.Contains("SolarMemoryFixedNodeCatalog.ForLayer(layer)") "Solar memory projection must consume fixed slots through the Mechanics catalog."
    Assert-True (-not $solarMemoryModeRuntime.Contains("private sealed class SolarMemoryFixedNodeSpec")) "Solar memory runtime must not own fixed-node specifications."
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
    Assert-True $terriasIds.Contains("SolarBossOrbitMirrorMapId") "Solar memory must define the fixed mirror-array boss map id."
    Assert-True $terriasIds.Contains("SolarBossSecondSunMapId") "Solar memory must define the fixed second-sun boss map id."
    Assert-True $terriasIds.Contains("SolarBossSaintWunaMapId") "Solar memory must define the hidden saint boss map id."
    Assert-True $solarMemorySettlementCoordinator.Contains('"NormalMapManager.ReadyToChangeMap"') "Solar finale routing must hook ReadyToChangeMap through the settlement coordinator."
    Assert-True (-not $solarMemoryModeRuntime.Contains("SolarFinalePhysicalStartLevel")) "Solar memory immediate settlement must not keep a separate finale physical level."
    Assert-True (-not $solarMemoryMapNodePoolApplier.Contains("IsFinaleLayer() ? 0")) "Solar memory node application must not carry finale segment remapping when completion settles immediately."
    Assert-True $entry.Contains("Terrias.Dll.Scripting.BossScripts") "Entry must register BossScripts for CSV script calls."
    Assert-True $bossScripts.Contains("public static void InitCard") "BossScripts must expose enemy-card init for CSV rows."
    Assert-True $bossScripts.Contains("public static void UseCard") "BossScripts must expose enemy-card use behavior for CSV rows."
    Assert-True $terriasIds.Contains("BossTraitMirrorArray") "TerriasIds must define the mirror-array boss trait buff id."
    Assert-True $terriasIds.Contains("BossTraitMercilessDaylight") "TerriasIds must define the merciless-daylight boss trait buff id."
    Assert-True $terriasIds.Contains("BossTraitWhiteRadianceSaint") "TerriasIds must define the white-radiance-saint boss trait buff id."
    Assert-True $buffScripts.Contains('"boss_trait_mirror_array"') "BuffScripts must route mirror-array boss trait apply/clear."
    Assert-True $buffScripts.Contains('"boss_trait_merciless_daylight"') "BuffScripts must route merciless-daylight boss trait apply/clear."
    Assert-True $buffScripts.Contains('"boss_trait_white_radiance_saint"') "BuffScripts must route white-radiance-saint boss trait apply/clear."
    Assert-True $buffScripts.Contains("BossScripts.ApplyTrait(self, id)") "BuffScripts must delegate boss trait apply to BossScripts."
    Assert-True $buffScripts.Contains("BossScripts.ClearTrait(self, id)") "BuffScripts must delegate boss trait clear to BossScripts."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, TerriasIds.BossTraitMirrorArray)") "Mirror-array boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, TerriasIds.BossTraitMercilessDaylight)") "Second-sun boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("ApplyBossTraitBuff(self, TerriasIds.BossTraitWhiteRadianceSaint)") "Saint Wuna boss init must grant its trait buff."
    Assert-True $bossScripts.Contains("TriggerMirrorArray") "BossScripts must implement the mirror-array trait trigger."
    Assert-True $bossScripts.Contains("TriggerMercilessDaylight") "BossScripts must implement the merciless-daylight trait trigger."
    Assert-True $bossScripts.Contains("TriggerWhiteRadianceSaint") "BossScripts must implement the white-radiance-saint trait trigger."
    Assert-True $bossScripts.Contains("MoveSavedNameToBurned") "Merciless daylight must be able to convert preserved names into burned names."
    Assert-True $bossScripts.Contains("MoveSavedNameToNameless") "White Radiance Saint must be able to convert preserved names into nameless people."
    Assert-True $terriasIds.Contains('public const string Cripple = "buff_cripple";') "TerriasIds must expose the official Cripple buff id."
    Assert-True $terriasIds.Contains("BossWhiteRadianceCrown") "TerriasIds must define the White Radiance Crown boss buff id."
    Assert-True $terriasIds.Contains("EnemyCardSaintWhiteEdict") "TerriasIds must define the White Radiance extra-action card id."
    Assert-True $buffApi.Contains("public static bool RemovePositiveBuffs") "BuffApi must support clearing all positive statuses from a target."
    Assert-True $executorApi.Contains("public static bool DealDamageToTarget") "ExecutorApi must expose explicit target damage for multiplayer boss actions."
    Assert-True $executorApi.Contains("public static bool AddEnemyAction") "ExecutorApi must expose a safe enemy-action append wrapper."
    Assert-True $executorApi.Contains("DealTrueDamageAllEnemiesByMaxHp") "ExecutorApi must support max-HP true damage against all player targets."
    Assert-True $playerApi.Contains("public static string LocalPlayerStatusId") "PlayerApi must expose a local player status id for per-player local effects."
    Assert-True $bossScripts.Contains("LastDayNoonDamage = 28") "Second Sun noon action must deal 28 damage."
    Assert-True $bossScripts.Contains("ExecutorApi.DealDamageToTarget(self, noonTarget, LastDayNoonDamage)") "Second Sun noon action must deal damage to its explicit target."
    Assert-True $bossScripts.Contains("TerriasIds.Cripple") "Second Sun noon action must apply the official Cripple buff."
    Assert-True $bossScripts.Contains("ExecutorApi.AddStatusBuff(self, target, TerriasIds.Burn, MirrorArrayBurn);") "Mirror Array must apply Burn before counting total Burn stacks."
    Assert-True $bossScripts.Contains("burnTotal += ExecutorApi.StatusBuffLevel(target, TerriasIds.Burn);") "Mirror Array shield must count post-application Burn stacks across all targets."
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
    Assert-True $enemyCardData.Contains("enemycard_projection_staff_combo") "EnemyCard data must define Staff Bonk Barrage."
    Assert-True $enemyCardData.Contains("enemycard_projection_holy_heal") "EnemyCard data must define Holy Heal."
    Assert-True ($enemyCardData.Contains("enemycard_spirit_intent_adapter") -and $enemyCardData.Contains('ProjectionScripts.InitAction(self, ""spirit-adapted"")')) "EnemyCard data must register a dedicated spirit adapter identity instead of reusing native precompiled ids."
    Assert-True $enemyCardData.Contains("ProjectionScripts.InitAction") "Projection enemy-card rows must route initialization through ProjectionScripts."
    Assert-True $enemyCardText.Contains("Turncoat Strike") "EnemyCard text must localize Heart Change's temporary strike intent."
    Assert-True $enemyCardText.Contains("Staff Bonk") "EnemyCard text must localize the projection staff action."
    Assert-True $enemyCardText.Contains("Magic Shield") "EnemyCard text must localize the projection magic-shield action."
    Assert-True $enemyCardText.Contains("Staff Bonk Barrage") "EnemyCard text must localize the projection multi-hit action."
    Assert-True $enemyCardText.Contains("Mana Disruption") "EnemyCard text must localize the projection debuff action."
    Assert-True $enemyCardText.Contains("You Are Empowered") "EnemyCard text must localize the projection group buff action."
    Assert-True $enemyCardText.Contains("Holy Heal") "EnemyCard text must localize the projection heal action."
    Assert-True ($enemyCardText.Contains("enemycard_spirit_intent_adapter") -and $enemyCardText.Contains("Spirit Intent")) "EnemyCard text must localize the spirit adapter fallback row."
    Assert-True (-not $enemyCardText.Contains("threat weight")) "Projection shield text must not promise retired threat-weight behavior."
    Assert-True (-not $enemyCardText.Contains("威胁权重")) "Projection shield Chinese text must not promise retired threat-weight behavior."
    Assert-True $buffText.Contains("Three Thousand Orbit Mirrors") "Buff text must localize the mirror-array boss trait."
    Assert-True $buffText.Contains("Merciless Daylight") "Buff text must localize the merciless-daylight boss trait."
    Assert-True $buffText.Contains("White Radiance Saint") "Buff text must localize the white-radiance-saint boss trait."
    Assert-True $buffText.Contains("Crown Manifestation: White Radiance") "Buff text must localize White Radiance Crown."
    Assert-True $enemyCardText.Contains("Noonday Spinebreaker") "EnemyCard text must describe the strengthened Second Sun noon action."
    Assert-True $enemyCardText.Contains("White Radiance Edict") "EnemyCard text must localize Wuna's extra action."
    Assert-True $enemyData.Contains("Terrias_terrias_boss_trait_mirror_array") "Mirror-array enemy data must expose its trait in AttributeText."
    Assert-True $enemyData.Contains("Terrias_terrias_boss_trait_merciless_daylight") "Second-sun enemy data must expose its trait in AttributeText."
    Assert-True $enemyData.Contains("Terrias_terrias_boss_trait_white_radiance_saint") "Saint Wuna enemy data must expose its trait in AttributeText."
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
    Assert-True $solarMemoryMapSyncRepairService.Contains("SolarMemoryFixedNodeCatalog.ForLayer") "Solar memory sync repair must force every fixed map node id through the shared catalog."
    Assert-True $solarMemoryMapLifecycleCoordinator.Contains("SolarMemoryMapSyncRepairService.Repair") "Solar memory map lifecycle must delegate synchronized array repair to Mechanics."
    Assert-True (-not $solarMemoryMapLifecycleCoordinator.Contains("RepairSolarMemorySyncIndex")) "Solar memory map lifecycle must not retain synchronized array mutation details."
    Assert-True (-not $solarMemoryMapLifecycleCoordinator.Contains("RewriteSolarMemoryDefaultLayer")) "Solar memory map lifecycle must not retain the retired duplicate map rewrite path."
    Assert-True (-not $solarMemoryMapLifecycleCoordinator.Contains("CreateBossChainNode")) "Solar memory map lifecycle must leave map-pool generation to the dedicated factory."
    Assert-True $gameCompatibilityApi.Contains("public static List<Dictionary<string, string>> GetItemsByPack") "Game compatibility API must expose version-safe card-pack item lookup."
    Assert-True $gameCompatibilityApi.Contains("CurrentGetItemsByPack") "Card-pack compatibility lookup must support the current three-argument game API."
    Assert-True $gameCompatibilityApi.Contains("LegacyGetItemsByPack") "Card-pack compatibility lookup must support the legacy two-argument game API."
    Assert-True $gameCompatibilityApi.Contains("GetItemsByPackFallback") "Card-pack compatibility lookup must retain a table-scan fallback."
    Assert-True (-not $solarMemoryStarterDeckRuntime.Contains(".GetPackItems(")) "Solar memory starter deck must not bind directly to the unstable GetPackItems signature."
    Assert-True (-not $solarMemoryModeRuntime.Contains(".GetPackItems(")) "Solar memory setup UI must not bind directly to the unstable GetPackItems signature."
    $sunsetExpedition = [regex]::Match($terriasHardTagRuntime, "private\s+static\s+void\s+ApplySunsetExpedition\(\)[\s\S]*?(?=private\s+static\s+)")
    Assert-True $sunsetExpedition.Success "Could not locate ApplySunsetExpedition for source assertion."
    Assert-True (-not $sunsetExpedition.Value.Contains("MirrorSc")) "Sunset Expedition must not borrow the player's generic MirrorSc executor."
    Assert-True (-not $sunsetExpedition.Value.Contains("ChangeHp")) "Sunset Expedition must not call ChangeHp without a dataConfig Id."
    Assert-True $sunsetExpedition.Value.Contains("status.CurHp = nextHp") "Sunset Expedition must apply HP loss through the synchronized status property."
    Assert-True $sunsetExpedition.Value.Contains("if (IsServerAuthority())") "Only the host may advance the shared Sunset Expedition fight count."
    Assert-True (-not $terriasHardTagRuntime.Contains("ApplyWhiteRadianceCourtCards")) "White Radiance Court must not attach White Radiance to player cards."
    Assert-True (-not $terriasHardTagRuntime.Contains("ApplyWhiteRadianceToRunDeck")) "White Radiance Court must not mutate the run deck."
    Assert-True (-not $terriasHardTagRuntime.Contains("ApplyWhiteRadianceToFightZones")) "White Radiance Court must not mutate combat card zones."
    Assert-True $terriasHardTagRuntime.Contains("CombatVarApi.AddInt(AbyssalShockHpStacksKey, 1)") "Abyssal Shock HP option must add one stack every time it triggers."
    Assert-True $terriasHardTagRuntime.Contains("while (applied < stacks)") "Abyssal Shock enemy HP scaling must catch enemies up to every triggered HP stack."
    Assert-True $terriasHardTagRuntime.Contains("Math.Ceiling(Math.Max(1, value) * 1.3)") "Abyssal Shock HP scaling must multiply MaxHp/CurHp by 1.3 each stack."
    Assert-True $terriasHardTagRuntime.Contains("TerriasLifecycleStepRunner.RunBattleOnce") "Hard-tag fight-start work must route through the lifecycle frame-step service."
    Assert-True $terriasHardTagRuntime.Contains('"FightInitialized"') "Hard-tag fight-start work must target the FightInitialized lifecycle."
    Assert-True $terriasHardTagRuntime.Contains('new TerriasFrameStep("MorningStarDimmed", () => MorningStarDimmedService.OnFightStarted') "Morning Star Dimmed fight-start work must be split as a lifecycle step."
    Assert-True $morningStarDimmedService.Contains("public const string CostMarker") "Morning Star Dimmed service must mark cards after applying the combat cost increase."
    Assert-True $morningStarDimmedService.Contains("TerriasLifecycleStepRunner.RunBattleOnce") "Morning Star Dimmed must split fight-start work through the Terrias lifecycle runner."
    Assert-True $morningStarDimmedService.Contains("TryClaimBattleOperation") "Morning Star Dimmed max power must be idempotent per battle."
    Assert-True $morningStarDimmedService.Contains("PlayerPowerApi.TryChangeMaxPower(1)") "Morning Star Dimmed must add only one max power through the player power API."
    Assert-True (-not $morningStarDimmedService.Contains("executor.ChangeMaxPower(")) "Morning Star Dimmed must not call the ForEachObject ScriptExecutor max-power method from a mirror executor."
    Assert-True $playerPowerApi.Contains("player.MaxPowerCount = expected") "Player power API must use the native max-power property so the fight UI refreshes."
    Assert-True $endlessAbyssGazePressureService.Contains("AddRandomCurseToCombatDeck") "Abyssal Gaze pressure must add temporary curses to the combat deck."
    Assert-True (-not $endlessAbyssGazePressureService.Contains("AddRandomCurseToLocalDeck(executor")) "Abyssal Gaze pressure must not add curses to the adventure deck."
    Assert-True $endlessAbyssCurseService.Contains("TemporaryCombatCurseMarker") "Abyssal Gaze temporary curses must carry a cleanup marker."
    Assert-True $endlessAbyssCurseService.Contains("FightCardManager.Instance?.cardList") "Abyssal Gaze temporary curses must enter the combat card list."
    Assert-True $terriasHardTagRuntime.Contains('CleanupTemporaryCombatCurses("FightEnding")') "Abyssal Gaze temporary curses must be cleaned at fight ending."
    Assert-True $terriasHardTagRuntime.Contains('RegisterBefore(modConfig, TerriasHookTargets.SkillItemTrueUse, OnSkillUseBefore)') "Stagnant Water must hook skill use before native cooldown is set."
    Assert-True $terriasHardTagRuntime.Contains('RegisterAfter(modConfig, TerriasHookTargets.SkillItemTrueUse, OnSkillUseAfter)') "Stagnant Water must hook skill use after native cooldown is set."
    Assert-True $terriasHardTagRuntime.Contains('new TerriasFrameStep("BlackSunListener"') "A Sunset Expedition failure must not prevent Black Sun listener registration."
    Assert-True $solarMemoryFixedNodeSpec.Contains("TerriasIds.SolarMemoryMapIds[eventIndex]") "Solar memory fixed-node catalog must use the fixed story map id array."
    Assert-True $solarMemoryFixedNodeSpec.Contains("TerriasIds.SolarMemoryFullEventIds[eventIndex]") "Solar memory fixed-node catalog must use the fixed story event id array."
    Assert-True $eventScripts.Contains("public static void InitSolarMemoryNode") "Solar memory fixed story events must expose an init method."
    Assert-True $eventScripts.Contains("public static void ContinueSolarMemory") "Solar memory fixed story events must expose a continue method."
    Assert-True (-not $eventScripts.Contains("Terrias.Dll.Hooks")) "Solar memory event scripts must not import Hooks directly."
    Assert-True (-not [regex]::IsMatch($eventScripts, "SolarMemory(?:ModeRuntime|PreparationRuntime|PlayerSetupState)")) "Solar memory event scripts must call the GameApi flow facade instead of Hook runtimes."
    Assert-True $eventScripts.Contains("SolarMemoryFlowApi.ContinueAfterPreparation()") "Solar memory event scripts must delegate preparation and story gating through SolarMemoryFlowApi."
    Assert-True $solarMemoryFlowApi.Contains("if (!IsPreparationComplete())") "SolarMemoryFlowApi must gate continuation on preparation completion."
    Assert-True $solarMemoryFlowApi.Contains("StartOrResumePreparation();") "SolarMemoryFlowApi must start preparation when continuation is requested early."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryPostPreparationDialoguePendingKey") "SolarMemoryFlowApi must distinguish dialogue confirmation from first-time dialogue opening."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryStoryGateService.TryStartPostPreparationDialogue") "SolarMemoryFlowApi must route completed preparation through the managed story dialogue flow."
    Assert-True $terriasIds.Contains("SolarMemorySaintWunaBossPendingKey") "Solar memory must persist a pending hidden-saint boss transition across UI timing gaps."
    Assert-True $solarMemoryFlowApi.Contains('SolarMemoryBossTransitionCoordinator.ContinueSaintWunaBossFromPreludeDialogue("SolarMemoryDialogue:saint_wuna_prelude")') "Saint Wuna prelude completion must bridge back into the boss transition coordinator."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("public static void ContinueSaintWunaBossFromPreludeDialogue") "Solar memory boss coordinator must expose a managed continuation for the Saint Wuna prelude."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemorySaintWunaBossPendingKey, true)") "Saint Wuna continuation must mark a retryable pending transition before advancing."
    Assert-True $solarMemoryMapLifecycleCoordinator.Contains("SolarMemoryBossTransitionCoordinator.TryContinuePendingSaintWunaBoss(""MapSelectUI.ReadyToSelect"")") "Saint Wuna pending transition must retry when map selection is rebuilt."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("SolarMemoryMapNodePoolFactory.CreateFixedBossNode(tree, TerriasIds.SolarBossSaintWunaMapId)") "Saint Wuna continuation must create the fixed boss node through the Solar Memory node factory."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("node.SetChild(0, CreateSolarMemoryTerminalNode") "Saint Wuna boss node must include a deterministic child for native RpcNextMap."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("GameSaveManager.UpdateNode(bossNode)") "Saint Wuna continuation must persist the restored current node before native map transition."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("UIManager.Instance?.CloseUI(""BattleRewardsUI"")") "Saint Wuna continuation must clear stale reward UI before starting the hidden boss."
    Assert-True $solarMemoryBossTransitionCoordinator.Contains("mapManager.CmdNextMap()") "Saint Wuna continuation must request the native next-map command instead of ending at a log line."
    Assert-True $solarMemoryStoryGateService.Contains("DialogueFlowService.Start") "Solar Memory story gates must start reusable managed dialogue flows."
    Assert-True $dialogueFlowRuntime.Contains("DialogueUI.ChooseOption") "DialogueFlowRuntime must hook native dialogue choice completion."
    Assert-True $dialogueFlowService.Contains("DialogueApi.EndDialogue") "DialogueFlowService must close native dialogue UI from C# after managed choice handling."
    Assert-True (-not $dialogueData.Contains("CS.Terrias.Dll.Scripting")) "Solar Memory Dialogue rows must not call C# from native Dialogue script columns."
    Assert-True $dialogueData.Contains("RoleImage1") "Solar Memory Dialogue rows must expose RoleImage1 overrides for dialogue art."
    Assert-True $dialogueData.Contains("solar_memory_opening_4,,,Terrias_solar_memory_solar_memory_wuna_dialogue,,1,,,Mods/Terrias/ModResource/Images/Dialogue/WuNa") "Solar Memory opening dialogue must complete through a managed final choice with a positioned dialogue role id."
    Assert-True $dialogueData.Contains("solar_memory_second_sun_end_2,,,Terrias_solar_memory_solar_memory_wuna_dialogue,,1,,,Mods/Terrias/ModResource/Images/Dialogue/WuNa") "Solar Memory second-sun ending dialogue must settle only after a managed final choice with a positioned dialogue role id."
    Assert-True $dialogueData.Contains("solar_memory_saint_wuna_prelude_6,,,Terrias_solar_memory_solar_memory_saint_wuna,,1,,,Mods/Terrias/ModResource/Images/Dialogue/WuNa_e") "Solar Memory saint-wuna prelude dialogue must resume map flow only after a managed final choice with a resolvable role id."
    Assert-True $dialogueData.Contains("solar_memory_saint_wuna_end_3,,,Terrias_loneer_loneer,,1,,,Mods/Terrias/ModResource/Images/Dialogue/Loneer") "Solar Memory saint-wuna ending dialogue must settle only after a managed final choice with a resolvable role id."
    Assert-True (-not $dialogueData.Contains(",,,wuna,,")) "Solar Memory Dialogue rows must use full runtime RoleData ids, not short role ids."
    Assert-True (-not $dialogueData.Contains(",,,loneer,,")) "Solar Memory Dialogue rows must use full runtime RoleData ids, not short role ids."
    Assert-True (-not $dialogueData.Contains(",,,solar_memory_saint_wuna,,")) "Solar Memory Dialogue rows must use full runtime RoleData ids, not short role ids."
    Assert-True $solarMemoryRoleData.Contains("DefaultY,DefaultScale") "Solar Memory dialogue roles must expose native dialogue positioning fields."
    Assert-True $solarMemoryRoleData.Contains("solar_memory_wuna_dialogue,Mods/Terrias/ModResource/Images/Avatar/WuNa,Mods/Terrias/ModResource/Images/Dialogue/WuNa,Mods/Terrias/ModResource/Images/Icon/WuNa3,300,1") "Solar Memory Wuna dialogue role must lift the dialogue image above the text box."
    Assert-True $solarMemoryRoleData.Contains("solar_memory_saint_wuna,Mods/Terrias/ModResource/Images/Avatar/WuNa,Mods/Terrias/ModResource/Images/Dialogue/WuNa_e,Mods/Terrias/ModResource/Images/Icon/WuNa3,300,1") "Solar Memory saint Wuna dialogue role must lift the dialogue image above the text box."
    Assert-True $loneerRoleData.Contains("DefaultY,DefaultScale") "Loneer dialogue role must expose native dialogue positioning fields."
    Assert-True $loneerRoleData.Contains("loneer,Mods/Terrias/ModResource/Images/Icon/Loneer2,Mods/Terrias/ModResource/Images/Character/Loneer,Mods/Terrias/ModResource/Images/Dialogue/Loneer,300,1") "Loneer dialogue role must lift the dialogue image above the text box."
    Assert-True $solarMemoryStoryGateService.Contains("CompleteDialogueId") "Solar Memory managed dialogue gates must register the final dialogue id for native option completion."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryPreparationRuntime.IsComplete()") "SolarMemoryFlowApi must bridge preparation completion to the Hook runtime."
    Assert-True $solarMemoryFlowApi.Contains("SolarMemoryModeRuntime.OpenOriginWindow()") "SolarMemoryFlowApi must bridge origin setup UI to the Hook runtime."
    Assert-True (-not $eventScripts.Contains('PlayerApi.SetGameVar(TerriasIds.SolarMemoryOriginPointsKey, "50")')) "Solar memory event initialization must not reset origin points to the old value."
    Assert-True $mapData.Contains("Id,Type,NodeId,Level,Rarity") "Solar memory map data must expose the RandomPool rarity marker."
    Assert-True $mapData.Contains("solar_memory_black_sun_after,Event,Breaks_solar_memory_black_sun_after,-1,7") "Solar memory story maps must be hidden from every RandomPool draw."
    Assert-True $mapData.Contains("solar_memory_above_sacred_wheel,Event,Breaks_solar_memory_above_sacred_wheel,-1,7") "All fixed Solar Memory story maps must be hidden from every RandomPool draw."
    Assert-True $mapData.Contains("solar_memory_boss_orbit_mirror_array,Fight,Terrias_terrias_level_orbit_mirror_array,99,7") "Solar memory mirror-array boss must be hidden and use an unreachable normal-adventure layer."
    Assert-True $mapData.Contains("solar_memory_boss_second_sun_last_day,Fight,Terrias_terrias_level_second_sun_last_day,99,7") "Solar memory second-sun boss must be hidden and use an unreachable normal-adventure layer."
    Assert-True $mapData.Contains("solar_memory_boss_saint_wuna,Fight,Terrias_terrias_level_saint_wuna,99,7") "Solar memory saint boss must be hidden and use an unreachable normal-adventure layer."
    Assert-True (-not $mapData.Contains("solar_memory_boss_orbit_mirror_array,Fight,Terrias_terrias_level_orbit_mirror_array,-1")) "Solar memory bosses must not be wildcard candidates in normal adventure."
    Assert-True $levelData.Contains("level_saint_wuna,Terrias_terrias_boss_saint_wuna,boss,-1") "Solar memory level data must define the hidden saint fight as a boss level."
    Assert-True $mapText.Contains("solar_memory_polluted_light") "Solar memory map text must include the polluted light node."
    Assert-True ($mapText.Contains("solar_memory_boss_saint_wuna") -and $mapText.Contains("Hidden Boss")) "Solar memory map text must mark the hidden saint fight as a boss node."
    Assert-True (-not $mapText.Contains($solarMemoryPrefix)) "Solar memory map event names must not repeat the mode prefix."
    Assert-True (-not $mapText.Contains("Solar Memory - ")) "Localized Solar Memory map event names must stay compact."
    Assert-True ($mapText.Contains("solar_memory_boss_orbit_mirror_array,") -and $mapText.Contains(",$bossMirrorName,")) "Solar memory map text must use the compact mirror-array boss name."
    Assert-True ($mapText.Contains("solar_memory_boss_second_sun_last_day,") -and $mapText.Contains(",$bossSecondSunName,")) "Solar memory map text must use the compact second-sun boss name."
    Assert-True $eventData.Contains("Sub_solar_memory_grief_struggle,CS.Terrias.Dll.Scripting.EventScripts.ContinueSolarMemory();") "Solar memory event data must route story choices through C# continue."
    Assert-True $eventText.Contains("Sub_solar_memory_above_sacred_wheel") "Solar memory event text must include the sixth fixed story row."
    Assert-True (-not $eventText.Contains($solarMemoryPrefix)) "Solar Memory event titles must not repeat the mode prefix."
    Assert-True (-not $eventText.Contains($solarMemoryTraditionalPrefix)) "Traditional Solar Memory event titles must not repeat the mode prefix."
    Assert-True (-not $eventText.Contains("Solar Memory - ")) "Localized Solar Memory event titles must stay compact."
    Assert-True (-not $eventText.Contains("Alderin")) "Solar finale ending text must not refer to Alderin as Wuna's world."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains("public static int SanitizeSolarMemoryRoleCards") "Solar memory deck isolation must expose a role-card sanitizer."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains("RemoveEventConfigs(role.cardList") "Solar memory sanitizer must remove event cards from the actual deck."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains("RemoveEventConfigs(role.UnCardList") "Solar memory sanitizer must remove event cards from the reserve pool."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains('SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool")') "Clearing the solar memory reserve must also sanitize the active deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "NormalMapManager.InitRoleTable")') "Solar memory role initialization must sanitize the official starter deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "ApplyStarterDeck")') "Solar memory custom starter deck application must sanitize the final deck."
    Assert-True $solarMemoryStarterDeckRuntime.Contains('SanitizeSolarMemoryRoleCards(roleTable, "KeepOfficialDeck")') "Solar memory official starter deck path must sanitize before continuing."
    Assert-True $solarMemoryStarterDeckRuntime.Contains("!SolarMemoryDeckIsolationRuntime.IsSolarMemoryEventCard(id)") "Solar memory starter deck candidates must exclude event cards."
    Assert-True ([regex]::IsMatch($solarMemoryDeckIsolationRuntime, 'public\s+static\s+void\s+OpenDeckWindow\(\)[\s\S]*SolarMemoryStarterDeckAppliedKey[\s\S]*SolarMemoryPreparationRuntime\.StartOrResume\(\);[\s\S]*return;[\s\S]*SanitizeSolarMemoryRoleCards\(RoleTable\.Instance,\s*"OpenDeckWindow"\)')) "Solar memory deck option must resume starter-deck preparation before opening the native deck window."
    Assert-True (-not [regex]::IsMatch($solarMemoryDeckIsolationRuntime, 'public\s+static\s+void\s+OpenDeckWindow\(\)[\s\S]*?if\s*\([^)]*SolarMemoryDeckConfiguredKey[\s\S]*?ClearSolarMemoryReservePool\(\);')) "Solar memory deck option must not mark the deck configured before starter-deck selection is applied."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains("SolarMemoryPlayerSetupState.SelectedPacks()") "Solar memory pack selection must prefer player-scoped preparation state."
    Assert-True $solarMemoryDeckIsolationRuntime.Contains("if (!PlayerApi.IsMultiplayerSession())") "Solar memory must not migrate saved global pack selection during multiplayer."
    Assert-True (-not $solarMemoryModeRuntime.Contains("SanitizeSolarMemoryRoleCards")) "Solar memory mode runtime must not retain role deck isolation."
    Assert-True $solarMemoryRunLauncher.Contains('saveInfo.GameVars[TerriasIds.SolarMemoryOriginPointsKey] = "50"') "Solar memory must initialize origin setup with 50 points."
    Assert-True $terriasIds.Contains("SolarMemoryPrepStepKey") "Solar memory preparation must persist an explicit preparation step."
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
    Assert-True $endlessSeaRunLauncher.Contains("private const string NativeMapModeType = TerriasIds.NativeNormalModeType") "Endless Sea must keep native map startup on the official Normal mode manager."
    Assert-True $endlessSeaRunLauncher.Contains("SetLobbyModeType(NativeMapModeType)") "Endless Sea lobby launch must reuse the native Normal mode manager."
    Assert-True (-not $endlessSeaRunLauncher.Contains("SetLobbyModeType(TerriasIds.EndlessSeaModeType)")) "Endless Sea must not pass its custom save mode type into the native lobby map startup."
    Assert-True (-not $endlessSeaRunLauncher.Contains("modeType = TerriasIds.EndlessSeaModeType")) "Endless Sea saves must not store custom modeType values that break native map startup."
    Assert-True $endlessSeaRunLauncher.Contains("EndlessSeaRunStateStore.InitializeNewRun") "Endless Sea launcher must delegate save initialization to the run-state store."
    Assert-True $endlessSeaRunStateStore.Contains("saveInfo.modeType = TerriasIds.NativeNormalModeType") "Endless Sea run-state repair must migrate Endless Sea saves back to native Normal mode."
    Assert-True $endlessSeaModeRuntime.Contains("EndlessSeaSaveCacheRuntime.Initialize(modConfig)") "Endless Sea runtime must isolate Endless Sea saves from the official Normal continue cache."
    Assert-True $endlessSeaSaveCacheRuntime.Contains('"ModeChoiceUI.NormalMode"') "Endless Sea save cache isolation must run before native Normal mode uses its cached save."
    Assert-True $endlessSeaSaveCacheRuntime.Contains('"ModeChoiceUI.DeleteExistingSavesForMode"') "Endless Sea save cache isolation must protect Endless Sea saves from native Normal cleanup."
    Assert-True $endlessSeaSaveCacheRuntime.Contains("TemporarilyProtectedSaves") "Endless Sea save cache isolation must restore Endless Sea saves after native cleanup."
    Assert-True $endlessSeaSaveCacheRuntime.Contains("ModeChoiceSaveCacheApi.ClearCachedSaveIf") "Endless Sea save cache isolation must route official cache mutation through GameApi."
    Assert-True $modeChoiceSaveCacheApi.Contains("ModeChoiceUI.beforeSave") "Mode choice save cache GameApi must own official beforeSave access."
    Assert-True $endlessSeaRunStateStore.Contains("DeleteUnfinishedRuns") "Endless Sea run-state store must own unfinished-run deletion."
    Assert-True $endlessSeaRunStateStore.Contains('Set(saveInfo, TerriasIds.EndlessSeaIntroSeenKey, "0")') "Endless Sea saves must initialize the intro board as unseen."
    Assert-True $endlessSeaRunStateStore.Contains('Set(saveInfo, TerriasIds.EndlessSeaStarterDeckAppliedKey, "0")') "Endless Sea saves must initialize starter-deck selection as unapplied."
    Assert-True $endlessSeaRunStateStore.Contains('Set(saveInfo, TerriasIds.EndlessSeaFloorPlanKey, "")') "Endless Sea saves must initialize the persisted floor plan slot."
    Assert-True $endlessSeaRunStateStore.Contains("EndlessSeaRunIdKey") "Endless Sea saves must persist a run id."
    Assert-True $endlessSeaRunStateStore.Contains("EndlessSeaRunPhaseKey") "Endless Sea saves must persist a phase."
    Assert-True $endlessSeaRunStateStore.Contains("EndlessSeaRunPhase.Evacuating") "Endless Sea saves must preserve the pending evacuation settlement phase."
    Assert-True $runtimeHooks.Contains("EndlessAbyssEvacuationRuntime.Initialize(modConfig)") "RuntimeHooks must initialize Endless Abyss evacuation."
    Assert-True $endlessAbyssEvacuationButtonRuntime.Contains('buttons?.Find("CardBack")') "Endless Abyss evacuation must clone the native TopBar card button template."
    Assert-True $endlessAbyssEvacuationButtonRuntime.Contains("EndlessAbyssEvacuationButtonRelay") "Endless Abyss evacuation must replace cloned native button listeners with a dedicated relay."
    Assert-True $endlessAbyssEvacuationButtonRuntime.Contains('Mods/Terrias/ModResource/Images/UI/\u65e0\u5c3d\u4e4b\u6e0a-\u9000\u51fa.png') "Endless Abyss evacuation must use the shipped evacuation icon."
    Assert-True $endlessAbyssEvacuationButtonRuntime.Contains('AuraUiNativeButtonIconOwner.Apply(manager, icon)') "Endless Abyss evacuation must own all three native button-state images."
    Assert-True $endlessAbyssEvacuationButtonRuntime.Contains('AuraUiNativeHoverHint.Attach(buttonObject, HoverHint)') "Endless Abyss evacuation must register a native-style settlement hover hint."
    Assert-True $endlessAbyssEvacuationRuntime.Contains("EndlessSeaRunPhase.MapPlanning") "Endless Abyss evacuation must only start from stable map planning."
    Assert-True $endlessAbyssEvacuationRuntime.Contains('GetBlockReason(allowConfirmationWindow: true)') "Endless Abyss evacuation confirmation must not reject its own closing modal window."
    Assert-True $endlessAbyssEvacuationRuntime.Contains("EndlessAbyssShockService.PendingRequest()") "Endless Abyss evacuation must not bypass pending shock resolution."
    Assert-True $endlessAbyssEvacuationRuntime.Contains("EndlessAbyssMilestoneRewardService.CanClaimCurrentFloor()") "Endless Abyss evacuation must not bypass pending milestone rewards."
    Assert-True $endlessAbyssEvacuationRuntime.Contains("GameExitUI.loss = false") "Endless Abyss evacuation must settle as a successful mode clear."
    Assert-True $endlessAbyssEvacuationRuntime.Contains("AuraModeOutcomeRuntime.Publish") "Endless Abyss evacuation must publish a generic run-scoped completed outcome for shared settlement consumers."
    Assert-True $endlessAbyssEvacuationRuntime.Contains('confirmation accepted; scheduling authoritative commit') "Endless Abyss evacuation must expose the modal-to-commit diagnostic boundary."
    Assert-True $endlessAbyssEvacuationRuntime.Contains('"GameExitUI.ReturnAsync"') "Endless Abyss evacuation must arm finalization only when the settlement is accepted."
    Assert-True $endlessAbyssEvacuationRuntime.Contains('"GameApp.ReturnToMenu"') "Endless Abyss evacuation must persist Ended immediately before the native menu return."
    Assert-True $endlessAbyssEvacuationService.Contains("EndlessAbyssEvacuationDepth.Calculate") "Endless Abyss evacuation must delegate total-depth projection to its pure calculator."
    Assert-True $endlessAbyssEvacuationRpc.Contains("serverSender.IsLobbyHost") "Endless Abyss evacuation RPC must require bound lobby-host authority."
    Assert-True $endlessAbyssEvacuationRpc.Contains("TryCaptureStored(RequestedToken") "Endless Abyss evacuation RPC must publish server-stored state instead of trusting its payload."
    Assert-True $endlessAbyssEvacuationRuntime.Contains('"GameExitUI.OnDestroy"') "Endless Abyss evacuation must wait for native settlement save completion before sending its ACK."
    Assert-True $endlessAbyssSettlementBarrierRuntime.Contains("HostWaitSeconds = 15") "Endless Abyss settlement barrier must bound the host wait."
    Assert-True $endlessAbyssSettlementBarrierRuntime.Contains("ForcedCommitGraceSeconds = 2") "Endless Abyss settlement barrier must preserve a forced local-save grace period."
    Assert-True $endlessAbyssSettlementBarrierView.Contains("settlementUi.NextShow()") "Endless Abyss settlement barrier must preserve native settlement details."
    Assert-True $endlessAbyssSettlementBarrierRpc.Contains("sender.IsLobbyHost") "Endless Abyss host barrier events must require bound host authority."
    Assert-True $endlessAbyssSettlementBarrierRpc.Contains("TryCaptureStored(command.SettlementToken") "Endless Abyss settlement barrier must validate the stored authoritative token."
    Assert-True $endlessSeaNetworkSync.Contains("CurrentProtocolVersion = 3") "Endless Sea snapshots must version the evacuation state extension."
    Assert-True $endlessSeaNetworkSync.Contains("EvacuationDepth") "Endless Sea snapshots must carry authoritative evacuation settlement depth."
    Assert-True $endlessSeaRunLauncher.Contains('saveInfo.GameVars[GameVar.ExLockDes.ToString()] = "0"') "Endless Sea saves must not pre-lock editable map slots."
    Assert-True $endlessSeaFloorPlanner.Contains("EndlessSeaNodeKind.Monster") "Endless Sea floor planner must fix the native start slot as a monster."
    Assert-True $endlessSeaFloorPlanner.Contains("EndlessSeaNodeKind.Boss") "Endless Sea floor planner must fix the final boss slot."
    Assert-True $endlessSeaFloorPlanner.Contains("new List<EndlessSeaSlotPlan>(TerriasIds.EndlessSeaNativeDefaultNodeCount)") "Endless Sea floor planner must prefill only native fixed slots."
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
    Assert-True $endlessSeaNetworkSync.Contains("TerriasNetworkRuntime.HasRemotePlayers()") "Endless Sea snapshots must only run for real multiplayer sessions."
    Assert-True $terriasNetworkRuntime.Contains("public static bool HasRemotePlayers()") "Terrias network runtime must expose an actual remote-player guard."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("AddTextFill(header.transform") "Endless Sea intro board must render a header subtitle."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("SetDeckButtonsInteractable(false)") "Endless Sea deck application must disable buttons while applying."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("SetDeckButtonsInteractable(true)") "Endless Sea deck application must restore buttons on retryable failure."
    Assert-True $endlessSeaIntroBoardRuntime.Contains("EndlessAbyssEvacuationButtonRuntime.Refresh()") "Endless Sea starter-deck completion must reveal the all-floor evacuation button immediately."
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
    Assert-True (-not $endlessSeaStarterDeckCatalog.Contains('"spark"')) "Endless Sea starter decks must not use unresolved Terrias short card ids."
    Assert-True (-not $endlessSeaStarterDeckCatalog.Contains('"solar_prayer"')) "Endless Sea starter decks must not use unresolved Terrias short card ids."
    Assert-True $endlessSeaStarterDeckCatalog.Contains("TerriasConfigIndex.Row(DataType.Card, cardId)") "Endless Sea starter deck catalog must validate card ids through the shared query index."
    Assert-True $endlessSeaRichTextSanitizer.Contains("AllowedSimpleTags") "Endless Sea rich text sanitizer must use an explicit simple-tag allowlist."
    Assert-True $endlessSeaRichTextSanitizer.Contains("AllowedScopedTags") "Endless Sea rich text sanitizer must use an explicit scoped-tag allowlist."
    Assert-True (-not $endlessSeaRichTextSanitizer.Contains("link")) "Endless Sea rich text sanitizer must not allow link tags."
    Assert-True ($originMilestoneService.Contains('AuraGameDataHostApi.Materialize(DataType.EnchTag, "enchtag_2")') -and $originMilestoneService.Contains('role.enchasedDict[card.InstanceID] = enchant;')) "Magic 50 must attach the registered Extinction enchant tag through the shared materializer."
    Assert-True $originMilestoneService.Contains("FortuneExtraTriggerThreshold = 150") "Fortune 50 must define the 150-point extra trigger threshold."
    Assert-True $originMilestoneService.Contains("bonus += FortuneExtraTriggers") "Fortune 50 must add two extra triggers after reaching 150."
    Assert-True ($originMilestoneService.Contains("IReadOnlyList<OriginMilestoneDefinition>") -and $originMilestoneService.Contains("OriginStrength50Blessing")) "Origin milestones must be catalog-driven rather than Endless Sea-only threshold branches."
    Assert-True ($originMilestoneRuntime.Contains('"RoleTable.VarsCheck"') -and $originMilestoneRuntime.Contains("OriginMilestoneService.Reconcile")) "Origin milestone reconciliation must extend the native origin change path in every mode."
    Assert-True $blessingScripts.Contains("OriginMilestoneService.ApplyFightScript") "Origin milestone blessing rows must delegate combat behavior to the C# service."
    Assert-True ($originCapService.Contains("role.MainVarUpperBound") -and $originCapService.Contains("role.SecondaryVarUpperBound") -and $originCapService.Contains("role.OtherVarUpperBound")) "Fate Star overflow must raise all three native cap classes."
    Assert-True ($endlessSeaOriginService.Contains("EnsureOriginCaps") -and -not $endlessSeaOriginService.Contains("ApplyBattleStartEffects")) "Endless Sea origin service must own only its mode-specific cap floor, not global milestone rewards."
    Assert-True $endlessSeaCardAffixRuntime.Contains("EndlessSeaCardAffixService.ApplyBurnout") "Endless Sea card affix runtime must delegate Burnout application to the service."
    Assert-True $endlessSeaCardAffixRuntime.Contains("EndlessSeaCardAffixService.NormalizeOwnedCards") "Endless Sea card affix runtime must normalize owned cards from non-reward gain paths."
    Assert-True $endlessSeaCardAffixService.Contains("CardAttachmentService.AttachToConfig") "Endless Sea card affix service must use the shared card attachment service."
    Assert-True $endlessSeaCardAffixService.Contains("EndlessSeaStarterDeckBaselineMarker") "Endless Sea card affix service must protect starter deck baseline cards."
    Assert-True $endlessSeaCardAffixService.Contains("RunWithStarterDeckSuppressed") "Endless Sea starter deck writes must suppress automatic Burnout attachment."
    Assert-True $endlessSeaCardAffixService.Contains("role.cardList") "Endless Sea card affix service must normalize equipped deck cards."
    Assert-True $endlessSeaCardAffixService.Contains("role.UnCardList") "Endless Sea card affix service must normalize reserve cards."
    Assert-True $endlessSeaCombatRuntime.Contains("EndlessAbyssEnemyInjectionService.TryInjectAfterFightInit") "Endless Sea combat runtime must delegate extra enemy injection to a Terrias-owned service."
    Assert-True (-not $endlessSeaCombatRuntime.Contains("CmdAddEnemy")) "Endless Sea combat runtime must not directly issue native enemy-add commands."
    Assert-True $endlessSeaCombatRuntime.Contains("EndlessAbyssEnemyScalingService.Calculate") "Endless Sea combat runtime must delegate enemy growth to the configured scaling service."
    Assert-True $endlessSeaCombatRuntime.Contains("enemy.Attack = nextAttack") "Endless Sea enemy growth must scale attack together with HP."
    Assert-True $endlessAbyssEnemyScaling.Contains("normalizedFloor >= endlessStartFloor") "Endless Abyss enemy scaling must apply a distinct endless-phase jump."
    Assert-True $endlessAbyssEnemyScaling.Contains("CycleFloorCount") "Endless Abyss enemy scaling must continue growing in configured floor cycles."
    Assert-True $endlessAbyssEnemyInjectionService.Contains("EnemyApi.IsClientOnlyDynamicEnemyObserver()") "Endless Abyss extra enemy planning must be skipped on client-only observers."
    Assert-True $endlessAbyssEnemyInjectionService.Contains("EnemyApi.AddDynamicEnemyAuthoritative") "Endless Abyss extra enemies must use the Terrias-owned EnemyApi wrapper."
    Assert-True $enemyApi.Contains("PlayerManager.Instance") "EnemyApi must own the multiplayer authority check for dynamic enemy adds."
    Assert-True $enemyApi.Contains("EnemyManager.Instance") "EnemyApi must resolve the native enemy manager before adding a dynamic enemy."
    Assert-True $enemyApi.Contains("manager.AddEnemy(enemyId)") "EnemyApi must follow the game's native dynamic enemy-add entry point."
    Assert-True (-not $enemyApi.Contains("CmdAddEnemy")) "EnemyApi must not call CmdAddEnemy directly."
    Assert-True $endlessAbyssConfig.Contains("RewardPools") "Endless Abyss config must expose independent reward pool definitions."
    Assert-True $endlessAbyssConfig.Contains("EnemyScaling") "Endless Abyss config must expose enemy growth settings independently."
    Assert-True $endlessAbyssConfig.Contains("OtherDimensionCardPoolId") "Endless Abyss milestone rewards must address a configured reward pool instead of a hard-coded card pack."
    Assert-True $endlessAbyssConfigJson.Contains('"rewardPools"') "Endless Abyss shipped config must define reward pools."
    Assert-True $endlessAbyssConfigJson.Contains('"enemyScaling"') "Endless Abyss shipped config must define enemy growth settings."
    Assert-True $endlessAbyssConfigJson.Contains('"endlessStartFloor": 7') "Endless Abyss shipped config must begin its phase jump at floor seven."
    Assert-True $endlessAbyssConfigJson.Contains('"milestone.other_dimension.cards"') "Endless Abyss shipped config must bind the other-dimension milestone reward to its independent pool."
    Assert-True $endlessAbyssConfigJson.Contains('"Terrias_terrias_cardpack_more_dimensions"') "Endless Abyss default other-dimension pool must be initialized from the More Dimensions card pack."
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
    Assert-True $solarMemoryPreparationRuntime.Contains('if (!SolarMemoryRoleCommitApi.CommitFinal(RoleTable.Instance, "Terrias.SolarMemory.SetupFinished"))') "Solar memory preparation completion must require a successful final role commit."
    Assert-True ([regex]::IsMatch($solarMemoryPreparationRuntime, 'CommitFinal\(RoleTable\.Instance,\s*"Terrias\.SolarMemory\.SetupFinished"\)[\s\S]*SolarMemoryPlayerSetupState\.SetFlag\(TerriasIds\.SolarMemorySetupFinishedKey,\s*false\)')) "Solar memory preparation must withdraw setup completion when final role commit fails."
    Assert-True $solarMemoryPreparationRuntime.Contains('SolarMemoryPlayerSetupState.SetValue(TerriasIds.SolarMemorySetupCommitTokenKey, "")') "Solar memory preparation must clear failed local commit tokens for retry."
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
    Assert-True ($modConfig.ModVersion -eq "0.5.0") "Terrias pre-release identity and network contract must ship as version 0.5.0."
    Assert-True ($modConfig.MustSame -eq $true) "Terrias must require an identical multiplayer mod version."
    Assert-True $audioArbiterRuntime.Contains('CurrentBuildId = "audio-arbiter-2026-07-20-v10"') "Audio arbiter must expose the content-probed presentation runtime build id."
    Assert-True $audioArbiterRuntime.Contains('const string sharedPrefix = "Shared:"') "Audio arbiter must resolve AuraShared resource paths."
    Assert-True $audioProviderResolver.Contains("MatchesProviderRequest") "Audio provider resolver must own owner-aware provider matching."
    Assert-True ([regex]::IsMatch($audioProviderResolver, 'requestedId,\s*"",\s*ownerStrict:\s*false')) "Audio bare provider matching must remain backward-compatible."
    Assert-True ([regex]::IsMatch($audioProviderResolver, 'requestedId,\s*requestedOwner,\s*ownerStrict:\s*true')) "Audio explicit owner-scoped requests must use strict owner-aware matching."
    Assert-True ($audioProviderResolver.Contains("ShouldWarnRemoteMismatch = isRemote") -and $audioArbiterRuntime.Contains('WarnProviderMismatchOnce(request, "Remote sound provider mismatch")')) "Audio remote provider mismatch must fail closed and log a diagnostic."
    Assert-True $audioNetworkRuntime.Contains("request.ProviderId = providerId") "Audio network adapter must retain bare ProviderId for legacy receivers."
    Assert-True $audioNetworkRuntime.Contains("request.OwnerModId = ownerModId") "Audio network adapter must preserve OwnerModId for deterministic remote matching."
    Assert-True $audioNetworkRuntime.Contains("OwnerModId to disambiguate") "Audio RPC compatibility comment must document OwnerModId-based matching."
    Assert-True $battleBgmArbiterRuntime.Contains('CurrentBuildId = "battle-bgm-arbiter-2026-07-20-v6"') "Battle BGM arbiter must expose its owner-qualified provider runtime build id."
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
    & (Join-Path $repoRoot "tools\Build-TerriasDll.ps1") -Configuration $Configuration -ManagedPath $ManagedPath | Out-Host
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("terrias-csharp-test-" + [System.Guid]::NewGuid().ToString("N"))
$sourceDir = Join-Path $tmpRoot "src"
New-Item -ItemType Directory -Path $sourceDir | Out-Null

try {
    Write-Utf8NoBom -Path (Join-Path $tmpRoot "Terrias.CSharpTests.csproj") -Text (New-ProjectXml -RepoRoot $repoRoot -SourceDir $sourceDir)
    Write-Utf8NoBom -Path (Join-Path $sourceDir "Stubs.cs") -Text (New-StubsSource)
    Write-Utf8NoBom -Path (Join-Path $sourceDir "Tests.cs") -Text (New-TestsSource)

    dotnet run --project (Join-Path $tmpRoot "Terrias.CSharpTests.csproj") -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Terrias C# tests failed."
    }

    Invoke-SourceAssertions -RepoRoot $repoRoot
    & (Join-Path $repoRoot "tools\Test-TerriasElemental.ps1") -Configuration $Configuration
    & (Join-Path $repoRoot "tools\Test-TerriasColumbina.ps1")
}
finally {
    if ($KeepTemp) {
        Write-Host "Kept temp directory: $tmpRoot"
    }
    else {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
