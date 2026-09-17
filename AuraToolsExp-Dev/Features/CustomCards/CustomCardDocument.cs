using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AuraToolsExp.Dll.Features.CustomCards;

[JsonConverter(typeof(StringEnumConverter))]
public enum CardRuleTrigger { Draw, Use, Discard, AfterUseRoundStart, AfterUseRoundEnd, AfterUseHurt }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardBlockKind { Effect, If, Repeat, ForEach, RememberNumber, RememberObject }
[JsonConverter(typeof(StringEnumConverter))]
public enum CardValueKind { Number, Read, Variable, Add, Subtract, Multiply, Divide, Minimum, Maximum, Floor, Ceiling, Clamp, Equal, NotEqual, Greater, AtLeast, Less, AtMost, And, Or, Not, Exists }
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
    public CardValueKind Kind { get; set; }
    public double Number { get; set; } = 1;
    public CardDataField Field { get; set; }
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CardObjectReference Object { get; set; } = new();
    public string ResourceId { get; set; } = "";
    public string VariableId { get; set; } = "";
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CardValue> Inputs { get; set; } = new();
    public static CardValue Constant(double n) => new() { Number = n };
    public static CardValue Reading(CardDataField field, CardObjectKind subject = CardObjectKind.Self) => new() { Kind = CardValueKind.Read, Field = field, Object = new() { Kind = subject } };
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
    public int Size { get; set; } = 64;
    public string Pixels { get; set; } = Convert.ToBase64String(new byte[64 * 64]);
    public bool UsePixels { get; set; }
    public string TemplateIcon { get; set; } = "Icon/Card/元素升华";
}

public sealed class CustomCardDocument
{
    public const string FormatId = "AuraTools.CustomCard";
    public const int CurrentVersion = 1;
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
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CustomCardRule> Rules { get; set; } = new() { new() { Blocks = new() { new() } } };

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
    public string NodeId { get; set; } = "";
    public string Message { get; set; } = "";
    public override string ToString() => Message;
}

public sealed class CustomCardCompilation
{
    public List<CardCompileIssue> Issues { get; } = new();
    public Dictionary<string, string> Scripts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> BuffReferences { get; } = new(StringComparer.Ordinal);
    public string Description { get; internal set; } = "";
    public bool Success => Issues.Count == 0;
}

internal static class CustomCardNames
{
    internal static readonly string[] Triggers = { "抽到时", "使用时", "丢弃时", "使用后 · 回合开始", "使用后 · 回合结束", "使用后 · 受到伤害" };
    internal static readonly string[] Blocks = { "产生效果", "如果 / 否则", "重复执行", "逐个对象执行", "记住数值", "记住对象" };
    internal static readonly string[] Values = { "固定数字", "读取战斗数据", "已记住的数值", "加", "减", "乘", "除", "较小值", "较大值", "向下取整", "向上取整", "限定范围", "等于", "不等于", "大于", "大于等于", "小于", "小于等于", "全部满足", "任一满足", "取反", "对象存在" };
    internal static readonly string[] Objects = { "自己", "选中目标", "当前遍历对象", "已记住的对象", "所有敌人", "所有友方", "所有角色", "随机一个敌人", "随机一个友方" };
    internal static readonly string[] Fields = { "当前生命", "生命上限", "已损失生命", "生命百分比", "护盾", "指定状态层数", "当前能量", "能量上限", "手牌数量", "抽牌堆数量", "弃牌堆数量", "本牌基础费用" };
    internal static readonly string[] Effects = { "造成普通伤害", "造成真实伤害", "获得护盾", "恢复生命", "增加生命上限", "获得能量", "抽牌", "添加状态", "移除状态", "弃牌", "焚毁手牌", "重新洗牌", "从抽牌堆选牌入手", "从弃牌堆选牌入手", "结束回合" };
    internal static string Name<T>(T value, string[] names) where T : Enum
    {
        var index = Convert.ToInt32(value);
        return index >= 0 && index < names.Length ? names[index] : "未知选项";
    }
    internal static bool Boolean(CardValueKind kind) => kind >= CardValueKind.Equal;
    internal static bool Single(CardObjectKind kind) => kind <= CardObjectKind.Saved || kind == CardObjectKind.RandomEnemy || kind == CardObjectKind.RandomFriend;
    internal static int Arity(CardValueKind kind) => kind switch
    {
        CardValueKind.Number or CardValueKind.Read or CardValueKind.Variable or CardValueKind.Exists => 0,
        CardValueKind.Floor or CardValueKind.Ceiling or CardValueKind.Not => 1,
        CardValueKind.Clamp => 3,
        _ => 2
    };
    internal static bool HasAmount(CardEffectKind effect) => effect != CardEffectKind.RemoveBuff && effect != CardEffectKind.Shuffle && effect != CardEffectKind.EndTurn;
    internal static bool PlayerEffect(CardEffectKind effect) => effect == CardEffectKind.Energy || effect == CardEffectKind.Draw || effect >= CardEffectKind.Discard;
    internal static bool NeedsBuff(CardEffectKind effect) => effect == CardEffectKind.AddBuff || effect == CardEffectKind.RemoveBuff;
}
