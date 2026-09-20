using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AuraToolsExp.Dll.Features.CustomCards;

[JsonConverter(typeof(StringEnumConverter))]
public enum CardRuleTrigger { Draw, Use, Discard, AfterUseRoundStart, AfterUseRoundEnd, AfterUseHurt }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardBlockKind { Effect, If, Repeat, ForEach, RememberNumber, RememberObject, Stop, PickEnemy, PickFriend, PickFromSet }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardValueKind { Number, Read, Variable, Add, Subtract, Multiply, Divide, Minimum, Maximum, Floor, Ceiling, Clamp, Equal, NotEqual, Greater, AtLeast, Less, AtMost, And, Or, Not, Exists, AsRatio, AsNumber, PercentPoints, Approximately }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardObjectKind { Self, Target, Current, Saved, Enemies, Friends, All, RandomEnemy, RandomFriend }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardDataField { Health, MaxHealth, MissingHealth, HealthPercent, Shield, BuffStacks, Energy, MaxEnergy, HandCount, DeckCount, DiscardCount, CardCost }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardEffectKind { Damage, TrueDamage, Shield, Heal, MaxHealth, Energy, Draw, AddBuff, RemoveBuff, Discard, Burn, Shuffle, SelectFromDeck, SelectFromDiscard, EndTurn }

public sealed class CardObjectReference
{
    public CardObjectKind Kind { get; set; } = CardObjectKind.Self;
    public string VariableId { get; set; } = "";
}

public sealed class CardValue
{
    public string NodeId { get; set; } = "";
    public CardValueKind Kind { get; set; }
    public double Number { get; set; } = 1;
    public CardNumberKind NumberKind { get; set; }
    public CardNumberFormat NumberFormat { get; set; }
    public CardDataField Field { get; set; }
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CardObjectReference Object { get; set; } = new();
    public string ResourceId { get; set; } = "";
    public string VariableId { get; set; } = "";
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CardValue> Inputs { get; set; } = new();
    public static CardValue Constant(double n) => new() { Number = n };
    public static CardValue Ratio(double n,bool percent=true) => new() { Number=n,NumberKind=CardNumberKind.Ratio,NumberFormat=percent?CardNumberFormat.Percent:CardNumberFormat.Number };
    public static CardValue Reading(CardDataField field, CardObjectKind subject = CardObjectKind.Self) => new() { Kind = CardValueKind.Read, Field = field, NumberKind=field==CardDataField.HealthPercent?CardNumberKind.Ratio:CardNumberKind.Value,NumberFormat=field==CardDataField.HealthPercent?CardNumberFormat.Percent:CardNumberFormat.Number,Object = new() { Kind = subject } };
    public static CardValue Compare(CardValueKind op, CardValue a, CardValue b) => new() { Kind = op, Inputs = new() { a, b } };
}

public sealed class CardRuleBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CardBlockKind Kind { get; set; }
    public CardEffectKind Effect { get; set; }
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CardObjectReference Object { get; set; } = new() { Kind = CardObjectKind.Target };
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CardValue Value { get; set; } = CardValue.Constant(6);
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CardValue Condition { get; set; } = CardValue.Compare(CardValueKind.Greater, CardValue.Reading(CardDataField.Shield), CardValue.Constant(0));
    public string ResourceId { get; set; } = "";
    public string VariableName { get; set; } = "记住的数值";
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CardRuleBlock> Then { get; set; } = new();
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CardRuleBlock> Else { get; set; } = new();
}

public sealed class CustomCardRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CardRuleTrigger Trigger { get; set; } = CardRuleTrigger.Use;
    // Zero means no per-activation limit; listener lifetime is one battle.
    public int MaximumTriggers { get; set; } = 1;
    public int MaximumTriggersPerRound { get; set; }
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public CardValue Condition { get; set; } = new() { Kind = CardValueKind.Exists };
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CardRuleBlock> Blocks { get; set; } = new();
}

public sealed class CustomCardArtwork
{
    public static bool SupportsNativeCardVersion(string version)=>version=="1"||version=="2";
    public int Size { get; set; } = 64;
    public string Pixels { get; set; } = Convert.ToBase64String(new byte[64 * 64]);
    public bool UsePixels { get; set; }
    public string TemplateIcon { get; set; } = "Icon/Card/元素升华";
}

public sealed class CustomCardDocument
{
    public const string FormatId = "AuraTools.CustomCard";
    public const int CurrentVersion = 4;
    public string Format { get; set; } = FormatId;
    public int SchemaVersion { get; set; } = CurrentVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名魔法";
    public long Revision { get; set; }
    public string Note { get; set; } = "";
    public int Cost { get; set; } = 1;
    public int Rarity { get; set; } = 1;
    public bool Targeted { get; set; } = true;
    public bool Burnout { get; set; }
    public bool Retain { get; set; }
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CustomCardArtwork Artwork { get; set; } = new();
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public CardBlueprint Graph { get; set; } = CardBlueprint.Basic();
    // Only the explicitly versioned v1 reader consumes Rules. Never an alternate v2 source.
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<CustomCardRule>? Rules { get; set; }
    public string LegacyBackup { get; set; } = "";
    public string BlueprintV2Backup { get; set; } = "";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> MigrationReview { get; set; } = new();

    public CustomCardDocument Copy() => JsonConvert.DeserializeObject<CustomCardDocument>(JsonConvert.SerializeObject(this))!;

    public CustomCardDocument Duplicate()
    {
        var copy = Copy();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Revision = 0;
        copy.Name += " 副本";
        return copy;
    }
}

public sealed class CardCompileIssue
{
    public string FieldId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string PortId { get; set; } = "";
    public string EdgeId { get; set; } = "";
    public bool BlocksConnection { get; set; }
    public string Message { get; set; } = "";
    public override string ToString() => Message;
}

public sealed class CustomCardCompilation
{
    public List<CardCompileIssue> Issues { get; } = new();
    public List<CardCompileIssue> Warnings { get; } = new();
    public List<CustomCardRule> Program { get; internal set; } = new();
    public Dictionary<string, string> Scripts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> BuffReferences { get; } = new(StringComparer.Ordinal);
    public string Description { get; internal set; } = "";
    public bool Success => Issues.Count == 0;
}

internal static class CustomCardNames
{
    internal static readonly string[] Triggers = { "抽到时", "使用时", "丢弃时" };
    internal static readonly string[] Blocks = { "产生效果", "如果 / 否则", "重复执行", "逐个对象执行", "记住数值", "记住对象", "结束本次流程" };
    internal static readonly string[] Values = { "固定数值", "读取战斗数据", "已记住的数值", "加", "减", "乘", "除", "较小值", "较大值", "向下取整", "向上取整", "限定范围", "等于", "不等于", "大于", "大于等于", "小于", "小于等于", "全部满足", "任一满足", "条件取反", "对象存在", "数值转比例", "比例转小数", "百分比转数值", "约等于" };
    internal static readonly string[] Objects = { "使用者", "使用时选中目标", "当前遍历对象", "已记住的对象", "所有敌人", "所有友方（含使用者）", "所有角色", "随机一个敌人", "随机一个友方" };
    internal static readonly string[] Fields = { "当前生命", "生命上限", "已损失生命", "生命百分比", "护盾", "指定状态层数", "当前能量", "能量上限", "手牌数量", "抽牌堆数量", "弃牌堆数量", "本牌基础费用" };
    internal static readonly string[] Effects = { "造成普通伤害", "造成真实伤害", "给予护盾", "恢复生命", "增加生命上限", "使用者获得能量", "使用者抽牌", "施加已有状态", "移除已有状态", "使用者弃牌", "焚毁使用者手牌", "重洗使用者牌堆", "从抽牌堆选牌入手", "从弃牌堆选牌入手", "结束使用者回合" };
    internal static string Name<T>(T value, string[] names) where T : Enum
    {
        var index = Convert.ToInt32(value);
        return index >= 0 && index < names.Length ? names[index] : "未知选项";
    }
    internal static bool Boolean(CardValueKind kind) => CardBlueprintNumbers.Comparison(kind)||kind==CardValueKind.And||kind==CardValueKind.Or||kind==CardValueKind.Not||kind==CardValueKind.Exists;
    internal static bool Single(CardObjectKind kind) => kind <= CardObjectKind.Saved || kind == CardObjectKind.RandomEnemy || kind == CardObjectKind.RandomFriend;
    internal static int Arity(CardValueKind kind) => kind switch
    {
        CardValueKind.Number or CardValueKind.Read or CardValueKind.Variable or CardValueKind.Exists => 0,
        CardValueKind.Floor or CardValueKind.Ceiling or CardValueKind.Not or CardValueKind.AsRatio or CardValueKind.AsNumber or CardValueKind.PercentPoints => 1,
        CardValueKind.Clamp or CardValueKind.Approximately => 3,
        _ => 2
    };
    internal static bool HasAmount(CardEffectKind effect) => effect != CardEffectKind.RemoveBuff && effect != CardEffectKind.Shuffle && effect != CardEffectKind.EndTurn;
    internal static bool PlayerEffect(CardEffectKind effect) => effect == CardEffectKind.Energy || effect == CardEffectKind.Draw || effect >= CardEffectKind.Discard;
    internal static bool NeedsBuff(CardEffectKind effect) => effect == CardEffectKind.AddBuff || effect == CardEffectKind.RemoveBuff;
}
