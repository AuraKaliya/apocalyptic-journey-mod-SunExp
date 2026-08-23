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
    [Flags]
    public enum AuraCardActionPhase
    {
        None = 0,
        NativeStarted = 1,
        Committed = 2
    }
    public sealed class AuraCardActionContext
    {
        public AuraCardActionPhase Phase { get; set; }
        public string OwnerStatusId { get; set; } = "";
    }
    public enum AuraSharedFramePhase { Presentation }
    public sealed class AuraSharedFrameSliceContext
    {
        public bool IsBudgetExhausted => false;
    }
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
        private static AuraSharedFrameWorkRequest? pendingRequest;

        public static bool RunCooperative(AuraSharedFrameWorkRequest request)
        {
            pendingRequest = request;
            return true;
        }

        public static AuraSharedFrameWorkRequest? TakePendingRequest()
        {
            var request = pendingRequest;
            pendingRequest = null;
            return request;
        }

        public static void Reset()
        {
            pendingRequest = null;
        }
    }
    public static class AuraCardPresentationDelta
    {
        public static bool CostResult { get; set; } = true;
        public static bool DescriptionResult { get; set; } = true;
        public static int CostUpdates { get; private set; }
        public static int DescriptionUpdates { get; private set; }

        public static bool TrySetCost(UnityEngine.Transform? transform, string costText)
        {
            CostUpdates++;
            return CostResult;
        }

        public static bool TrySetDescription(UnityEngine.Transform? transform, string description)
        {
            DescriptionUpdates++;
            return DescriptionResult;
        }

        public static void Reset()
        {
            CostResult = true;
            DescriptionResult = true;
            CostUpdates = 0;
            DescriptionUpdates = 0;
        }
    }

    public enum AuraCardPresentationSurface
    {
        CombatCard
    }

    public sealed class AuraCardPresentationContext
    {
        public UnityEngine.Transform? Root { get; set; }
        public object? Config { get; set; }
        public object? Card { get; set; }
        public string Source { get; set; } = "";
        public AuraCardPresentationSurface Surface { get; set; }
    }

    public static class AuraCardPresentationRuntime
    {
        public static int ApplyCount { get; private set; }

        public static void RequestApply(AuraCardPresentationContext context)
        {
            ApplyCount++;
        }

        public static void ResetDiagnostics()
        {
            ApplyCount = 0;
        }
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
    public sealed class GameObject
    {
    }

    public sealed class Transform
    {
    }
}

namespace Witch.Core
{
    public sealed class ModHookContext
    {
        public object? Target { get; set; }
    }
}

namespace Witch.Mod
{
    public sealed class ModConfig
    {
    }
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
    public static class PlayerInfo
    {
        private static readonly Dictionary<string, string> GameVars = new(StringComparer.Ordinal);

        public static string GetGameVar(string key)
        {
            return GameVars.TryGetValue(key ?? "", out var value) ? value : "";
        }

        public static void SetGameVar(string key, string value)
        {
            GameVars[key ?? ""] = value ?? "";
        }
    }

    public IStatusManager? Self { get; set; } = FightPlayer.Instance.Status;
    public Dictionary<string, Delegate> ScriptDict { get; } = new();

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

    public void AddCardToDeckById(string id, bool toUsed = true)
    {
        var config = new DataConfig(new Dictionary<string, string>
        {
            ["Id"] = id,
            ["Expend"] = "0",
            ["Tag"] = ""
        });
        if (toUsed)
        {
            FightCardManager.Instance.usedCardList.Add(config);
        }
        else
        {
            DeckCard.Add(config);
        }
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

    public Dictionary<DataConfig, HashSet<string>> CardTags { get; } = new();
    public int RefreshTagCount { get; private set; }

    public void RefreshTag(IDataConfig config)
    {
        RefreshTagCount++;
        if (config is DataConfig dataConfig)
        {
            CardTags[dataConfig] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public void ResetDiagnostics()
    {
        RefreshTagCount = 0;
    }
}

public sealed class RoleTable
{
    public static RoleTable Instance { get; } = new();
    public DataConfig Career { get; set; } = new(new Dictionary<string, string> { ["Id"] = "career" });
    public List<DataConfig> cardList { get; } = new();
    public List<DataConfig> relicList { get; } = new();
    public List<DataConfig> blessingConfigs { get; } = new();
}

public sealed class CardItem
{
    private readonly int instanceId = Guid.NewGuid().GetHashCode();

    public DataConfig? dataConfig { get; set; }

    public IDictionary<string, string> data { get; set; } = new Dictionary<string, string>();

    public IDictionary<string, string> Vars { get; set; } = new Dictionary<string, string>();

    public List<string> Tags { get; } = new();

    public UnityEngine.Transform transform { get; } = new();
    public UnityEngine.GameObject gameObject { get; } = new();

    public Action? DataUpdateAction { get; set; }

    public int DataUpdateCount { get; private set; }
    public int RefreshTagCount { get; private set; }
    public int TransformCount { get; private set; }

    public void RefreshTag()
    {
        RefreshTagCount++;
        FightCardManager.Instance.RefreshTag(dataConfig!);
        DataUpdate();
    }

    public void DataUpdate()
    {
        DataUpdateCount++;
        DataUpdateAction?.Invoke();
    }

    public CardItem TransformToConfiguredType(DataConfig nextDataConfig)
    {
        TransformCount++;
        dataConfig = nextDataConfig;
        DataUpdate();
        return this;
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
        CombatCard,
        PostCommit
    }

    public sealed class TerriasCardPresentationContext
    {
        public UnityEngine.Transform? Root { get; set; }
        public IDataConfig? Config { get; set; }
        public CardItem? Card { get; set; }
        public string Source { get; set; } = "";
        public TerriasCardPresentationSurface Surface { get; set; }
    }

    public sealed class TerriasCardLifecycleSubscription
    {
        public Action<Witch.Core.ModHookContext>? AfterCardItemInit { get; set; }
        public Action<Witch.Core.ModHookContext>? AfterAttackCardItemInit { get; set; }
        public Action<Witch.Core.ModHookContext>? AfterCardItemDataUpdate { get; set; }
        public Action<Witch.Core.ModHookContext>? AfterAttackCardItemDataUpdate { get; set; }
    }

    public static class TerriasCardLifecycleRouter
    {
        public static IDisposable Register(string id, TerriasCardLifecycleSubscription subscription) => EmptyDisposable.Instance;
    }

    public sealed class TerriasBattleLifecycleSubscription
    {
        public Action<Witch.Core.ModHookContext>? BattleInitializing { get; set; }
        public Action<Witch.Core.ModHookContext>? BattleSettling { get; set; }
        public Action<Witch.Core.ModHookContext>? BattleRestarting { get; set; }
    }

    public static class TerriasBattleLifecycleRouter
    {
        public static IDisposable Register(string id, TerriasBattleLifecycleSubscription subscription) => EmptyDisposable.Instance;
    }

    internal sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
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
        public static int ApplyCount { get; private set; }

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
            ApplyCount++;
        }

        public static void RequestApply(TerriasCardPresentationContext context)
        {
            ApplyCount++;
        }

        public static void RequestActiveCombatCardsReapply(string source, int delayFrames)
        {
        }

        public static void ResetDiagnostics()
        {
            ApplyCount = 0;
        }
    }
}

namespace Terrias.Dll.GameApi
{
    public static class FightUiCardLayoutApi
    {
        public static bool RequestCurrentHandLayout(string source)
        {
            return true;
        }
    }

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

        public static double ElapsedMilliseconds(long startTimestamp)
        {
            return 0d;
        }
    }
}

namespace Terrias.Dll.Mechanics
{
    public static class CardVisualThemeCatalog
    {
        public static bool IsTerriasCard(IDataConfig? config)
        {
            return config != null
                   && Terrias.Dll.GameApi.CardConfigApi.Id(config).StartsWith("Terrias_", StringComparison.Ordinal);
        }
    }

    public static class TerriasCardDescriptionProjector
    {
        public static int RecomputeCount { get; private set; }
        public static int ApplyDescriptionCount { get; private set; }

        public static bool TryRecompute(CardItem? card)
        {
            RecomputeCount++;
            return true;
        }

        public static bool TryApplyDescription(CardItem? card)
        {
            ApplyDescriptionCount++;
            return AuraShared.Core.AuraCardPresentationDelta.TrySetDescription(card?.transform, "description");
        }

        public static void Reset()
        {
            RecomputeCount = 0;
            ApplyDescriptionCount = 0;
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
