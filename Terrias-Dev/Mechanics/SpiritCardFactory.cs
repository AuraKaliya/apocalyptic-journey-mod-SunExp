using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SpiritCardFactory
{
    private const int MaxExchangeCount = 999;

    public static CardGrantResult GrantDeploymentToHand(ScriptExecutor self, CapturedEnemySnapshot snapshot)
    {
        return Grant(self, snapshot, 0, new SpiritCardBattleState(), "spirit-deployment");
    }

    public static CardGrantResult GrantReturnedToHand(
        ScriptExecutor self,
        CapturedEnemySnapshot snapshot,
        int exchangeCount,
        SpiritCardBattleState battleState,
        string source)
    {
        return Grant(self, snapshot, exchangeCount, battleState, source);
    }

    private static CardGrantResult Grant(
        ScriptExecutor self,
        CapturedEnemySnapshot snapshot,
        int exchangeCount,
        SpiritCardBattleState battleState,
        string source)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        var success = false;
        var failureStep = "";
        try
        {
            var normalizedExchangeCount = NormalizeExchangeCount(exchangeCount);
            var runtime = BuildRuntime(snapshot, normalizedExchangeCount, battleState);
            var request = CardGrantRequest
                .ToHand(TerriasIds.SpiritCardTemplateShortId)
                .WithSource((source ?? "spirit-card") + ":" + snapshot.EnemyId)
                .WithRuntimeTags("Retain", "Burnout")
                .WithRuntimePresentation(runtime)
                .RequireMutations()
                .Configure("spirit-card", config => Configure(config, runtime));
            var result = CardApi.GrantCardToHand(self, request);
            success = result.Success;
            failureStep = result.FailureStep;
            return result;
        }
        finally
        {
            TerriasPerformanceCounters.RecordHotspot(
                "Spirit.Card.GrantToHand",
                started,
                "enemy=" + (snapshot?.EnemyId ?? "<none>")
                + ", exchangeCount=" + NormalizeExchangeCount(exchangeCount)
                + ", persistent=False"
                + ", success=" + success
                + (failureStep.Length == 0 ? "" : ", failureStep=" + failureStep),
                logFirstSample: true);
        }
    }

    public static int ReadExchangeCount(IDataConfig? config)
    {
        return config == null
            ? 0
            : NormalizeExchangeCount(RuntimeInt(config, TerriasIds.SpiritExchangeCountKey));
    }

    public static SpiritCardBattleState ReadBattleState(IDataConfig? config)
    {
        if (config == null)
        {
            return new SpiritCardBattleState();
        }

        var result = new SpiritCardBattleState
        {
            TurnIndex = Math.Max(0, RuntimeInt(config, TerriasIds.SpiritIntentTurnIndexKey))
        };
        try
        {
            result.ReadyOnTurn = AuraSharedJson.Deserialize<Dictionary<string, int>>(
                    RuntimeValue(config, TerriasIds.SpiritIntentReadyOnTurnKey))
                ?? new Dictionary<string, int>(StringComparer.Ordinal);
        }
        catch
        {
            result.ReadyOnTurn = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        result.ReadyOnTurn = result.ReadyOnTurn
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Take(128)
            .ToDictionary(
                entry => entry.Key.Trim(),
                entry => Math.Max(0, Math.Min(10000, entry.Value)),
                StringComparer.Ordinal);
        result.TurnIndex = Math.Min(10000, result.TurnIndex);
        return result;
    }

    public static CapturedEnemySnapshot? Read(IDataConfig? config)
    {
        if (!IsSpiritCard(config))
        {
            return null;
        }

        var runtimeConfig = config!;
        var enemyId = RuntimeValue(runtimeConfig, TerriasIds.SpiritEnemyIdKey);
        if (enemyId.Length == 0)
        {
            return null;
        }

        return new CapturedEnemySnapshot
        {
            SpiritUid = RuntimeValue(runtimeConfig, TerriasIds.SpiritUidKey),
            SourceModId = RuntimeValue(runtimeConfig, TerriasIds.SpiritSourceModIdKey),
            SpeciesId = RuntimeValue(runtimeConfig, TerriasIds.SpiritSpeciesIdKey),
            ProfileId = RuntimeValue(runtimeConfig, TerriasIds.SpiritGrowthProfileIdKey),
            EnemyId = enemyId,
            VariantId = RuntimeValue(runtimeConfig, TerriasIds.SpiritVariantIdKey, enemyId),
            DisplayName = RuntimeValue(runtimeConfig, TerriasIds.SpiritDisplayNameKey, RuntimeValue(runtimeConfig, "Name")),
            Description = RuntimeValue(runtimeConfig, TerriasIds.SpiritDescriptionKey),
            AnimationPath = RuntimeValue(runtimeConfig, TerriasIds.SpiritAnimationPathKey),
            DictPath = RuntimeValue(runtimeConfig, TerriasIds.SpiritDictPathKey),
            IdlePath = RuntimeValue(runtimeConfig, TerriasIds.SpiritIdlePathKey),
            CaptureOrigin = RuntimeValue(runtimeConfig, TerriasIds.SpiritCaptureOriginKey),
            CapturedAt = RuntimeValue(runtimeConfig, TerriasIds.SpiritCapturedAtKey),
            BaseHp = RuntimeInt(runtimeConfig, "TerriasSpiritBaseHp"),
            BaseAttack = RuntimeInt(runtimeConfig, "TerriasSpiritBaseAttack"),
            BaseArmor = RuntimeInt(runtimeConfig, "TerriasSpiritBaseArmor"),
            Rarity = RuntimeInt(runtimeConfig, "TerriasSpiritEnemyRarity"),
            SourceEnemyCardIds = Split(RuntimeValue(runtimeConfig, "TerriasSpiritSourceEnemyCardIds")),
            SpiritLevel = RuntimeInt(runtimeConfig, "TerriasSpiritLevel"),
            SpiritAptitude = RuntimeInt(runtimeConfig, "TerriasSpiritAptitude"),
            OriginMagic = RuntimeInt(runtimeConfig, "TerriasSpiritOriginMagic"),
            OriginSpirit = RuntimeInt(runtimeConfig, "TerriasSpiritOriginSpirit"),
            OriginLuck = RuntimeInt(runtimeConfig, "TerriasSpiritOriginLuck"),
            OriginPerception = RuntimeInt(runtimeConfig, "TerriasSpiritOriginPerception"),
            DeploymentToken = RuntimeValue(runtimeConfig, "TerriasSpiritDeploymentToken")
        };
    }

    public static bool IsSpiritCard(IDataConfig? config)
    {
        return config != null
            && (string.Equals(DictionaryUtil.Get(config.data, "Id"), TerriasIds.SpiritCardTemplateId, StringComparison.Ordinal)
                || string.Equals(DictionaryUtil.Get(config.data, "Id"), TerriasIds.SpiritCardTemplateShortId, StringComparison.Ordinal)
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, TerriasIds.RuntimeMarkersKey), TerriasIds.SpiritCardMarker)
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey), TerriasIds.SpiritCardMarker));
    }

    public static bool IsSpiritBall(IDataConfig? config)
    {
        var id = DictionaryUtil.Get(config?.data, "Id").Replace("*", "");
        return string.Equals(id, TerriasIds.SpiritBallCardId, StringComparison.Ordinal)
            || string.Equals(id, TerriasIds.SpiritBallCardShortId, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> BuildRuntime(
        CapturedEnemySnapshot snapshot,
        int exchangeCount,
        SpiritCardBattleState battleState)
    {
        var runtime = new Dictionary<string, string>();
        var name = "精灵·" + snapshot.DisplayName;
        var traditionalName = "精靈·" + snapshot.DisplayName;
        var description = "召唤一只" + snapshot.DisplayName;
        var traditionalDescription = "召喚一隻" + snapshot.DisplayName;

        Set(runtime, "Tag", "Retain,Burnout");
        Set(runtime, "Icon", TerriasIds.SpiritBallIconPath);
        Set(runtime, "Name", name);
        Set(runtime, "Name_zh-Hant", traditionalName);
        Set(runtime, "Name_en", "Spirit: " + snapshot.DisplayName);
        Set(runtime, "Name_ja", "精霊・" + snapshot.DisplayName);
        Set(runtime, "Description", description);
        Set(runtime, "Description_zh-Hant", traditionalDescription);
        Set(runtime, "Description_en", "Summon one " + snapshot.DisplayName + ".");
        Set(runtime, "Description_ja", snapshot.DisplayName + "を一体召喚する。");
        Set(runtime, TerriasIds.RuntimeMarkersKey, TerriasIds.SpiritCardMarker);
        Set(runtime, TerriasIds.SpiritUidKey, snapshot.SpiritUid);
        Set(runtime, TerriasIds.SpiritSourceModIdKey, snapshot.SourceModId);
        Set(runtime, TerriasIds.SpiritSpeciesIdKey, snapshot.SpeciesId);
        Set(runtime, TerriasIds.SpiritGrowthProfileIdKey, snapshot.ProfileId);
        Set(runtime, TerriasIds.SpiritEnemyIdKey, snapshot.EnemyId);
        Set(runtime, TerriasIds.SpiritVariantIdKey, snapshot.VariantId);
        Set(runtime, TerriasIds.SpiritDisplayNameKey, snapshot.DisplayName);
        Set(runtime, TerriasIds.SpiritDescriptionKey, snapshot.Description);
        Set(runtime, TerriasIds.SpiritAnimationPathKey, snapshot.AnimationPath);
        Set(runtime, TerriasIds.SpiritDictPathKey, snapshot.DictPath);
        Set(runtime, TerriasIds.SpiritIdlePathKey, snapshot.IdlePath);
        Set(runtime, TerriasIds.SpiritProfileVersionKey, SpiritIntentRegistry.RegistryHash);
        Set(runtime, TerriasIds.SpiritCaptureOriginKey, snapshot.CaptureOrigin);
        Set(runtime, TerriasIds.SpiritCapturedAtKey, snapshot.CapturedAt);
        Set(runtime, TerriasIds.SpiritExchangeCountKey, exchangeCount.ToString());
        Set(runtime, TerriasIds.SpiritIntentTurnIndexKey, Math.Max(0, battleState?.TurnIndex ?? 0).ToString());
        Set(runtime, TerriasIds.SpiritIntentReadyOnTurnKey, AuraSharedJson.Serialize(
            battleState?.ReadyOnTurn ?? new Dictionary<string, int>(StringComparer.Ordinal)));
        Set(runtime, "TotalExCost", exchangeCount.ToString());
        Set(runtime, "TerriasSpiritBaseHp", snapshot.BaseHp.ToString());
        Set(runtime, "TerriasSpiritBaseAttack", snapshot.BaseAttack.ToString());
        Set(runtime, "TerriasSpiritBaseArmor", snapshot.BaseArmor.ToString());
        Set(runtime, "TerriasSpiritEnemyRarity", snapshot.Rarity.ToString());
        Set(runtime, "TerriasSpiritSourceEnemyCardIds", string.Join(",", snapshot.SourceEnemyCardIds ?? new List<string>()));
        Set(runtime, "TerriasSpiritLevel", snapshot.SpiritLevel.ToString());
        Set(runtime, "TerriasSpiritAptitude", snapshot.SpiritAptitude.ToString());
        Set(runtime, "TerriasSpiritOriginMagic", snapshot.OriginMagic.ToString());
        Set(runtime, "TerriasSpiritOriginSpirit", snapshot.OriginSpirit.ToString());
        Set(runtime, "TerriasSpiritOriginLuck", snapshot.OriginLuck.ToString());
        Set(runtime, "TerriasSpiritOriginPerception", snapshot.OriginPerception.ToString());
        Set(runtime, "TerriasSpiritDeploymentToken", snapshot.DeploymentToken);

        return runtime;
    }

    private static void Configure(DataConfig config, IReadOnlyDictionary<string, string> runtime)
    {
        var persistedData = config.data == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(config.data);
        foreach (var entry in runtime)
        {
            DictionaryUtil.Set(config.Vars, entry.Key, entry.Value);
            persistedData[entry.Key] = entry.Value;
        }

        DictionaryUtil.Set(
            config.Vars,
            "RawData",
            Convert.ToBase64String(GZip.CompressString(AuraSharedJson.Serialize(persistedData))));
    }

    private static void Set(IDictionary<string, string> data, string key, string value) => data[key] = value ?? "";

    private static string RuntimeValue(IDataConfig config, string key, string fallback = "")
    {
        if (config.Vars != null && config.Vars.TryGetValue(key, out var runtimeValue))
        {
            return runtimeValue ?? "";
        }

        return DictionaryUtil.Get(config.data, key, fallback);
    }

    private static int RuntimeInt(IDataConfig config, string key, int fallback = 0)
    {
        return DictionaryUtil.ParseInt(RuntimeValue(config, key), fallback);
    }

    private static int NormalizeExchangeCount(int value)
    {
        return Math.Max(0, Math.Min(MaxExchangeCount, value));
    }

    private static List<string> Split(string value)
    {
        return (value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }
}
