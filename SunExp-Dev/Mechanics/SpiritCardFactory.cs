using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class SpiritCardFactory
{
    private const int MaxExchangeCount = 999;

    public static CardGrantResult GrantCapturedToHand(ScriptExecutor self, CapturedEnemySnapshot snapshot)
    {
        return Grant(self, snapshot, 0, new SpiritCardBattleState(), persistToAdventureDeck: true, "spirit-capture");
    }

    public static CardGrantResult GrantReturnedToHand(
        ScriptExecutor self,
        CapturedEnemySnapshot snapshot,
        int exchangeCount,
        SpiritCardBattleState battleState,
        string source)
    {
        return Grant(self, snapshot, exchangeCount, battleState, persistToAdventureDeck: false, source);
    }

    private static CardGrantResult Grant(
        ScriptExecutor self,
        CapturedEnemySnapshot snapshot,
        int exchangeCount,
        SpiritCardBattleState battleState,
        bool persistToAdventureDeck,
        string source)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var success = false;
        var failureStep = "";
        try
        {
            var adventureDeck = persistToAdventureDeck ? RoleTable.Instance?.cardList : null;
            if (persistToAdventureDeck && adventureDeck == null)
            {
                failureStep = "adventure-deck";
                return CardGrantResult.Fail(SunExpIds.SpiritCardTemplateId, null, failureStep, "RoleTable cardList unavailable");
            }

            var normalizedExchangeCount = NormalizeExchangeCount(exchangeCount);
            var runtime = BuildRuntime(snapshot, normalizedExchangeCount, battleState);
            var request = CardGrantRequest
                .ToHand(SunExpIds.SpiritCardTemplateShortId)
                .WithSource((source ?? "spirit-card") + ":" + snapshot.EnemyId)
                .WithRuntimeTags("Retain", "Burnout")
                .WithRuntimePresentation(runtime)
                .RequireMutations()
                .Configure("spirit-card", config => Configure(config, runtime));
            var result = CardApi.GrantCardToHand(self, request);
            success = result.Success;
            failureStep = result.FailureStep;
            if (persistToAdventureDeck
                && result.Success
                && result.Config != null
                && adventureDeck != null
                && !adventureDeck.Contains(result.Config))
            {
                adventureDeck.Add(result.Config);
            }

            return result;
        }
        finally
        {
            SunExpPerformanceCounters.RecordHotspot(
                "Spirit.Card.GrantToHand",
                started,
                "enemy=" + (snapshot?.EnemyId ?? "<none>")
                + ", exchangeCount=" + NormalizeExchangeCount(exchangeCount)
                + ", persistent=" + persistToAdventureDeck
                + ", success=" + success
                + (failureStep.Length == 0 ? "" : ", failureStep=" + failureStep),
                logFirstSample: true);
        }
    }

    public static int ReadExchangeCount(IDataConfig? config)
    {
        return config == null
            ? 0
            : NormalizeExchangeCount(RuntimeInt(config, SunExpIds.SpiritExchangeCountKey));
    }

    public static SpiritCardBattleState ReadBattleState(IDataConfig? config)
    {
        if (config == null)
        {
            return new SpiritCardBattleState();
        }

        var result = new SpiritCardBattleState
        {
            TurnIndex = Math.Max(0, RuntimeInt(config, SunExpIds.SpiritIntentTurnIndexKey))
        };
        try
        {
            result.ReadyOnTurn = AuraSharedJson.Deserialize<Dictionary<string, int>>(
                    RuntimeValue(config, SunExpIds.SpiritIntentReadyOnTurnKey))
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
        var enemyId = RuntimeValue(runtimeConfig, SunExpIds.SpiritEnemyIdKey);
        if (enemyId.Length == 0)
        {
            return null;
        }

        return new CapturedEnemySnapshot
        {
            SpiritUid = RuntimeValue(runtimeConfig, SunExpIds.SpiritUidKey),
            SourceModId = RuntimeValue(runtimeConfig, SunExpIds.SpiritSourceModIdKey),
            EnemyId = enemyId,
            VariantId = RuntimeValue(runtimeConfig, SunExpIds.SpiritVariantIdKey, enemyId),
            DisplayName = RuntimeValue(runtimeConfig, SunExpIds.SpiritDisplayNameKey, RuntimeValue(runtimeConfig, "Name")),
            Description = RuntimeValue(runtimeConfig, SunExpIds.SpiritDescriptionKey),
            AnimationPath = RuntimeValue(runtimeConfig, SunExpIds.SpiritAnimationPathKey),
            DictPath = RuntimeValue(runtimeConfig, SunExpIds.SpiritDictPathKey),
            IdlePath = RuntimeValue(runtimeConfig, SunExpIds.SpiritIdlePathKey),
            CaptureOrigin = RuntimeValue(runtimeConfig, SunExpIds.SpiritCaptureOriginKey),
            CapturedAt = RuntimeValue(runtimeConfig, SunExpIds.SpiritCapturedAtKey),
            BaseHp = RuntimeInt(runtimeConfig, "SunExpSpiritBaseHp"),
            BaseAttack = RuntimeInt(runtimeConfig, "SunExpSpiritBaseAttack"),
            BaseArmor = RuntimeInt(runtimeConfig, "SunExpSpiritBaseArmor"),
            Rarity = RuntimeInt(runtimeConfig, "SunExpSpiritEnemyRarity"),
            SourceEnemyCardIds = Split(RuntimeValue(runtimeConfig, "SunExpSpiritSourceEnemyCardIds"))
        };
    }

    public static bool IsSpiritCard(IDataConfig? config)
    {
        return config != null
            && (string.Equals(DictionaryUtil.Get(config.data, "Id"), SunExpIds.SpiritCardTemplateId, StringComparison.Ordinal)
                || string.Equals(DictionaryUtil.Get(config.data, "Id"), SunExpIds.SpiritCardTemplateShortId, StringComparison.Ordinal)
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, SunExpIds.RuntimeMarkersKey), SunExpIds.SpiritCardMarker)
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey), SunExpIds.SpiritCardMarker));
    }

    public static bool IsSpiritBall(IDataConfig? config)
    {
        var id = DictionaryUtil.Get(config?.data, "Id").Replace("*", "");
        return string.Equals(id, SunExpIds.SpiritBallCardId, StringComparison.Ordinal)
            || string.Equals(id, SunExpIds.SpiritBallCardShortId, StringComparison.Ordinal);
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
        Set(runtime, "Icon", SunExpIds.SpiritBallIconPath);
        Set(runtime, "Name", name);
        Set(runtime, "Name_zh-Hant", traditionalName);
        Set(runtime, "Name_en", "Spirit: " + snapshot.DisplayName);
        Set(runtime, "Name_ja", "精霊・" + snapshot.DisplayName);
        Set(runtime, "Description", description);
        Set(runtime, "Description_zh-Hant", traditionalDescription);
        Set(runtime, "Description_en", "Summon one " + snapshot.DisplayName + ".");
        Set(runtime, "Description_ja", snapshot.DisplayName + "を一体召喚する。");
        Set(runtime, SunExpIds.RuntimeMarkersKey, SunExpIds.SpiritCardMarker);
        Set(runtime, SunExpIds.SpiritUidKey, snapshot.SpiritUid);
        Set(runtime, SunExpIds.SpiritSourceModIdKey, snapshot.SourceModId);
        Set(runtime, SunExpIds.SpiritEnemyIdKey, snapshot.EnemyId);
        Set(runtime, SunExpIds.SpiritVariantIdKey, snapshot.VariantId);
        Set(runtime, SunExpIds.SpiritDisplayNameKey, snapshot.DisplayName);
        Set(runtime, SunExpIds.SpiritDescriptionKey, snapshot.Description);
        Set(runtime, SunExpIds.SpiritAnimationPathKey, snapshot.AnimationPath);
        Set(runtime, SunExpIds.SpiritDictPathKey, snapshot.DictPath);
        Set(runtime, SunExpIds.SpiritIdlePathKey, snapshot.IdlePath);
        Set(runtime, SunExpIds.SpiritProfileVersionKey, SpiritIntentRegistry.RegistryHash);
        Set(runtime, SunExpIds.SpiritCaptureOriginKey, snapshot.CaptureOrigin);
        Set(runtime, SunExpIds.SpiritCapturedAtKey, snapshot.CapturedAt);
        Set(runtime, SunExpIds.SpiritExchangeCountKey, exchangeCount.ToString());
        Set(runtime, SunExpIds.SpiritIntentTurnIndexKey, Math.Max(0, battleState?.TurnIndex ?? 0).ToString());
        Set(runtime, SunExpIds.SpiritIntentReadyOnTurnKey, AuraSharedJson.Serialize(
            battleState?.ReadyOnTurn ?? new Dictionary<string, int>(StringComparer.Ordinal)));
        Set(runtime, "TotalExCost", exchangeCount.ToString());
        Set(runtime, "SunExpSpiritBaseHp", snapshot.BaseHp.ToString());
        Set(runtime, "SunExpSpiritBaseAttack", snapshot.BaseAttack.ToString());
        Set(runtime, "SunExpSpiritBaseArmor", snapshot.BaseArmor.ToString());
        Set(runtime, "SunExpSpiritEnemyRarity", snapshot.Rarity.ToString());
        Set(runtime, "SunExpSpiritSourceEnemyCardIds", string.Join(",", snapshot.SourceEnemyCardIds ?? new List<string>()));

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
