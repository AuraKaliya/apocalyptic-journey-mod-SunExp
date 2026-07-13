using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class SpiritCardFactory
{
    public static CardGrantResult GrantToHand(ScriptExecutor self, CapturedEnemySnapshot snapshot)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        var success = false;
        var failureStep = "";
        try
        {
            var adventureDeck = RoleTable.Instance?.cardList;
            if (adventureDeck == null)
            {
                failureStep = "adventure-deck";
                return CardGrantResult.Fail(SunExpIds.SpiritCardTemplateId, null, failureStep, "RoleTable cardList unavailable");
            }

            var request = CardGrantRequest
                .ToHand(SunExpIds.SpiritCardTemplateShortId)
                .WithSource("spirit-capture:" + snapshot.EnemyId)
                .WithRuntimeTags("Retain", "Burnout")
                .RequireMutations()
                .Configure("spirit-card", config => Configure(config, snapshot));
            var result = CardApi.GrantCardToHand(self, request);
            success = result.Success;
            failureStep = result.FailureStep;
            if (result.Success && result.Config != null && !adventureDeck.Contains(result.Config))
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
                + ", success=" + success
                + (failureStep.Length == 0 ? "" : ", failureStep=" + failureStep),
                logFirstSample: true);
        }
    }

    public static CapturedEnemySnapshot? Read(IDataConfig? config)
    {
        if (!IsSpiritCard(config))
        {
            return null;
        }

        var data = config!.data;
        var enemyId = DictionaryUtil.Get(data, SunExpIds.SpiritEnemyIdKey);
        if (enemyId.Length == 0)
        {
            return null;
        }

        return new CapturedEnemySnapshot
        {
            SpiritUid = DictionaryUtil.Get(data, SunExpIds.SpiritUidKey),
            SourceModId = DictionaryUtil.Get(data, SunExpIds.SpiritSourceModIdKey),
            EnemyId = enemyId,
            VariantId = DictionaryUtil.Get(data, SunExpIds.SpiritVariantIdKey, enemyId),
            DisplayName = DictionaryUtil.Get(data, SunExpIds.SpiritDisplayNameKey, DictionaryUtil.Get(data, "Name")),
            Description = DictionaryUtil.Get(data, SunExpIds.SpiritDescriptionKey),
            AnimationPath = DictionaryUtil.Get(data, SunExpIds.SpiritAnimationPathKey),
            DictPath = DictionaryUtil.Get(data, SunExpIds.SpiritDictPathKey),
            IdlePath = DictionaryUtil.Get(data, SunExpIds.SpiritIdlePathKey),
            CaptureOrigin = DictionaryUtil.Get(data, SunExpIds.SpiritCaptureOriginKey),
            CapturedAt = DictionaryUtil.Get(data, SunExpIds.SpiritCapturedAtKey),
            BaseHp = DictionaryUtil.GetInt(data, "SunExpSpiritBaseHp"),
            BaseAttack = DictionaryUtil.GetInt(data, "SunExpSpiritBaseAttack"),
            BaseArmor = DictionaryUtil.GetInt(data, "SunExpSpiritBaseArmor"),
            Rarity = DictionaryUtil.GetInt(data, "SunExpSpiritEnemyRarity"),
            SourceEnemyCardIds = Split(DictionaryUtil.Get(data, "SunExpSpiritSourceEnemyCardIds"))
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

    private static void Configure(DataConfig config, CapturedEnemySnapshot snapshot)
    {
        var data = config.data;
        var name = "精灵·" + snapshot.DisplayName;
        var description = "召唤【" + snapshot.DisplayName + "】到投影位置。";
        if (!string.IsNullOrWhiteSpace(snapshot.Description))
        {
            description += "\n" + snapshot.Description;
        }

        Set(data, "Tag", "Retain,Burnout");
        Set(data, "Icon", SunExpIds.SpiritBallIconPath);
        Set(data, "Name", name);
        Set(data, "Name_zh-Hant", name);
        Set(data, "Name_en", "Spirit: " + snapshot.DisplayName);
        Set(data, "Name_ja", "精霊・" + snapshot.DisplayName);
        Set(data, "Description", description);
        Set(data, "Description_zh-Hant", description);
        Set(data, "Description_en", "Summon " + snapshot.DisplayName + " to the projection position.");
        Set(data, "Description_ja", snapshot.DisplayName + "を投影位置に召喚する。");
        Set(data, SunExpIds.RuntimeMarkersKey, SunExpIds.SpiritCardMarker);
        Set(data, SunExpIds.SpiritUidKey, snapshot.SpiritUid);
        Set(data, SunExpIds.SpiritSourceModIdKey, snapshot.SourceModId);
        Set(data, SunExpIds.SpiritEnemyIdKey, snapshot.EnemyId);
        Set(data, SunExpIds.SpiritVariantIdKey, snapshot.VariantId);
        Set(data, SunExpIds.SpiritDisplayNameKey, snapshot.DisplayName);
        Set(data, SunExpIds.SpiritDescriptionKey, snapshot.Description);
        Set(data, SunExpIds.SpiritAnimationPathKey, snapshot.AnimationPath);
        Set(data, SunExpIds.SpiritDictPathKey, snapshot.DictPath);
        Set(data, SunExpIds.SpiritIdlePathKey, snapshot.IdlePath);
        Set(data, SunExpIds.SpiritProfileVersionKey, SpiritIntentRegistry.RegistryHash);
        Set(data, SunExpIds.SpiritCaptureOriginKey, snapshot.CaptureOrigin);
        Set(data, SunExpIds.SpiritCapturedAtKey, snapshot.CapturedAt);
        Set(data, "SunExpSpiritBaseHp", snapshot.BaseHp.ToString());
        Set(data, "SunExpSpiritBaseAttack", snapshot.BaseAttack.ToString());
        Set(data, "SunExpSpiritBaseArmor", snapshot.BaseArmor.ToString());
        Set(data, "SunExpSpiritEnemyRarity", snapshot.Rarity.ToString());
        Set(data, "SunExpSpiritSourceEnemyCardIds", string.Join(",", snapshot.SourceEnemyCardIds ?? new List<string>()));

        foreach (var entry in data)
        {
            if (entry.Key == "RawData")
            {
                continue;
            }

            if (entry.Key == "Tag" || entry.Key == SunExpIds.RuntimeMarkersKey || entry.Key.StartsWith("SunExpSpirit", StringComparison.Ordinal))
            {
                DictionaryUtil.Set(config.Vars, entry.Key, entry.Value);
            }
        }

        config.Vars["RawData"] = Convert.ToBase64String(GZip.CompressString(AuraSharedJson.Serialize(data)));
    }

    private static void Set(IDictionary<string, string> data, string key, string value) => data[key] = value ?? "";

    private static List<string> Split(string value)
    {
        return (value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }
}
