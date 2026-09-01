using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class LegacySpiritCardMigrationRecord
{
    public string SpiritUid { get; set; } = "";
    public CapturedEnemySnapshot Source { get; set; } = new();
}

public static class LegacySpiritCardMigrationReader
{
    private const string UidKey = "TerriasSpiritUid";
    private const string EnemyIdKey = "TerriasSpiritEnemyId";
    private const string VariantIdKey = "TerriasSpiritVariantId";
    private const string SourceModIdKey = "TerriasSpiritSourceModId";
    private const string DisplayNameKey = "TerriasSpiritDisplayName";
    private const string DescriptionKey = "TerriasSpiritDescription";
    private const string AnimationPathKey = "TerriasSpiritAnimationPath";
    private const string DictPathKey = "TerriasSpiritDictPath";
    private const string IdlePathKey = "TerriasSpiritIdlePath";
    private const string CaptureOriginKey = "TerriasSpiritCaptureOrigin";
    private const string CapturedAtKey = "TerriasSpiritCapturedAt";

    public static LegacySpiritCardMigrationRecord? Read(IDataConfig? config)
    {
        if (!SpiritCardFactory.IsSpiritCard(config)) return null;
        var enemyId = Value(config!, EnemyIdKey);
        if (enemyId.Length == 0) return null;
        return new LegacySpiritCardMigrationRecord
        {
            SpiritUid = Value(config!, UidKey),
            Source = new CapturedEnemySnapshot
            {
                SourceModId = Value(config!, SourceModIdKey),
                EnemyId = enemyId,
                VariantId = Value(config!, VariantIdKey, enemyId),
                DisplayName = Value(config!, DisplayNameKey, Value(config!, "Name")),
                Description = Value(config!, DescriptionKey),
                AnimationPath = Value(config!, AnimationPathKey),
                DictPath = Value(config!, DictPathKey),
                IdlePath = Value(config!, IdlePathKey),
                CaptureOrigin = Value(config!, CaptureOriginKey),
                CapturedAt = Value(config!, CapturedAtKey),
                BaseHp = Integer(config!, "TerriasSpiritBaseHp"),
                BaseAttack = Integer(config!, "TerriasSpiritBaseAttack"),
                BaseArmor = Integer(config!, "TerriasSpiritBaseArmor"),
                Rarity = Integer(config!, "TerriasSpiritEnemyRarity"),
                SourceEnemyCardIds = Split(Value(config!, "TerriasSpiritSourceEnemyCardIds"))
            }
        };
    }

    private static string Value(IDataConfig config, string key, string fallback = "")
    {
        if (config.Vars != null && config.Vars.TryGetValue(key, out var runtimeValue))
            return runtimeValue ?? "";
        return DictionaryUtil.Get(config.data, key, fallback);
    }

    private static int Integer(IDataConfig config, string key)
        => DictionaryUtil.ParseInt(Value(config, key));

    private static List<string> Split(string value)
        => (value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
}
