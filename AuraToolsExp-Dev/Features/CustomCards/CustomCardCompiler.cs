using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>Compiles typed, bounded editor documents, never user-supplied Lua fragments.</summary>
public static class CustomCardCompiler
{
    public const int MaximumNodes = 256;
    public const int MaximumDepth = 12;
    public const int MaximumRepeat = 64;
    public const int RuntimeSteps = 4096;
    private static readonly Regex Identifier = new("^[a-zA-Z0-9_-]{1,100}$", RegexOptions.CultureInvariant);
    private sealed class Scope
    {
        internal bool Current;
        internal readonly Dictionary<string, bool> Variables = new(StringComparer.Ordinal);
        internal Scope Copy() { var s = new Scope { Current = Current }; foreach (var p in Variables) s.Variables.Add(p.Key, p.Value); return s; }
    }

    public static CustomCardCompilation Compile(CustomCardDocument document, Func<string, string?>? buffName = null)
    {
        var c = new CustomCardCompilation();
        if (document == null) { Error(c, "", "设计稿为空。"); return c; }
        if (document.Format != CustomCardDocument.FormatId || document.SchemaVersion != CustomCardDocument.CurrentVersion)
            Error(c, "", "不支持这个版本的设计稿，请使用对应版本的妙妙工具。");
        if (document.Rules != null) Error(c, "", "旧规则必须先迁移为蓝图，不能作为第二份执行来源。");
        if (document.MigrationReview == null) Error(c, "", "迁移记录无效。");
        else foreach (var review in document.MigrationReview) Error(c, "", "迁移待确认：" + review);
        if (!ValidId(document.Id)) Error(c, "", "作品身份无效。");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 40 || document.Name.Any(char.IsControl)) Error(c, "", "名称需为 1～40 个可见字符。");
        if (document.Cost < 0 || document.Cost > 99) Error(c, "", "卡牌费用需为 0～99。");
        if (document.Rarity < 1 || document.Rarity > 3) Error(c, "", "请选择有效稀有度。");
        if (document.Note == null || document.Note.Length > 300) Error(c, "", "风味文字最多 300 字。");
        if (document.Artwork == null || !CardPixelCanvas.IsValid(document.Artwork.Size, document.Artwork.Pixels)) Error(c, "", "像素卡面数据无效。");
        if (document.Artwork != null && (string.IsNullOrWhiteSpace(document.Artwork.TemplateIcon) || document.Artwork.TemplateIcon.Length > 500)) Error(c, "", "请选择有效卡面模板。");
        if (!c.Success) return c;
        var rules = CardBlueprintCompiler.Lower(document, c);
        c.Program = rules;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int nodes = 0;
        foreach (var rule in rules)
        {
            if (rule == null) { Error(c, "", "触发规则为空。"); continue; }
            CheckId(rule.Id, c, ids);
            if (rule.Trigger < CardRuleTrigger.Draw || rule.Trigger > CardRuleTrigger.Discard) Error(c, rule.Id, "不支持持续监听入口。");
            if (rule.Blocks == null || rule.Blocks.Count == 0) { Error(c, rule.Id, "触发规则还没有效果。"); continue; }
            var targetAvailable = document.Targeted && rule.Trigger == CardRuleTrigger.Use;
            Value(rule.Condition, true, new Scope(), targetAvailable, 0, ref nodes, rule.Id, c, buffName);
            ValidateBlocks(rule.Blocks, new Scope(), targetAvailable, 0, ref nodes, ids, c, buffName);
        }
        bool HasEffect(IEnumerable<CardRuleBlock> blocks)=>blocks.Any(b=>b.Kind==CardBlockKind.Effect||HasEffect(b.Then)||HasEffect(b.Else));
        if(!rules.Any(r=>HasEffect(r.Blocks)))Error(c,"","请连接至少一个卡牌效果。");
        if (!c.Success) return c;
        c.Scripts["InitScript"] = "self.Vars:set_Item(\"BaseScript\", " + Quote(document.Targeted ? "AttackCardItem" : "CommonCardItem") + ")\n";
        foreach (var trigger in new[] { CardRuleTrigger.Draw, CardRuleTrigger.Use, CardRuleTrigger.Discard })
        {
            var emitter = new Emitter(c);
            foreach (var rule in rules.Where(r => r.Trigger == trigger))
                emitter.Rule(rule);
            var key = trigger == CardRuleTrigger.Draw ? "DrawScript" : trigger == CardRuleTrigger.Use ? "UseScript" : "DropScript";
            c.Scripts[key] = emitter.Text;
        }
        c.Description = CustomCardDescription.DescribeRules(rules);
        foreach (var reference in c.BuffReferences)
            c.Description = c.Description.Replace("「" + reference.Key + "」", "「" + reference.Value + "」");
        return c;
    }

    private static void ValidateBlocks(List<CardRuleBlock>? blocks, Scope scope, bool target, int depth, ref int nodes, HashSet<string> ids, CustomCardCompilation c, Func<string, string?>? buffName)
    {
        if (blocks == null) { Error(c, "", "效果分支为空。"); return; }
        if (depth > MaximumDepth) { Error(c, "", "嵌套层数过多，请拆分规则。"); return; }
        foreach (var b in blocks)
        {
            if (++nodes > MaximumNodes) { Error(c, b?.Id ?? "", "作品超过 256 个积木或表达式。"); return; }
            if (b == null) { Error(c, "", "效果积木为空。"); continue; }
            CheckId(b.Id, c, ids);
            if (!Enum.IsDefined(typeof(CardBlockKind), b.Kind)) { Error(c, b.Id, "未知积木。"); continue; }
            switch (b.Kind)
            {
                case CardBlockKind.Effect:
                    if (!Enum.IsDefined(typeof(CardEffectKind), b.Effect)) { Error(c, b.Id, "未知效果。"); break; }
                    Object(b.Object, scope, target, false, b.Id, c);
                    if (CustomCardNames.PlayerEffect(b.Effect) && b.Object?.Kind != CardObjectKind.Self) Error(c, b.Id, "能量、牌堆和回合效果只能作用于自己。",true);
                    if (CustomCardNames.HasAmount(b.Effect)) Value(b.Value, false, scope, target, depth + 1, ref nodes, b.Id, c, buffName);
                    if (CustomCardNames.HasAmount(b.Effect) && b.Value?.Kind == CardValueKind.Number)
                    {
                        if (b.Value.Number < 0) Error(c, b.Id, "效果数量不能为负数。");
                        if ((b.Effect == CardEffectKind.Draw || b.Effect == CardEffectKind.Discard || b.Effect == CardEffectKind.Burn || b.Effect == CardEffectKind.SelectFromDeck || b.Effect == CardEffectKind.SelectFromDiscard) && b.Value.Number > 64)
                            Error(c, b.Id, "单次牌堆操作最多 64 张。");
                    }
                    if (CustomCardNames.NeedsBuff(b.Effect)) Buff(b.ResourceId, b.Id, c, buffName);
                    break;
                case CardBlockKind.If:
                    Value(b.Condition, true, scope, target, depth + 1, ref nodes, b.Id, c, buffName);
                    ValidateBlocks(b.Then, scope.Copy(), target, depth + 1, ref nodes, ids, c, buffName);
                    ValidateBlocks(b.Else, scope.Copy(), target, depth + 1, ref nodes, ids, c, buffName);
                    break;
                case CardBlockKind.Repeat:
                    Value(b.Value, false, scope, target, depth + 1, ref nodes, b.Id, c, buffName);
                    if (b.Value?.Kind == CardValueKind.Number && (b.Value.Number < 0 || b.Value.Number > MaximumRepeat || b.Value.Number != Math.Floor(b.Value.Number))) Error(c, b.Id, "重复次数必须是 0～64 的整数。");
                    ValidateBlocks(b.Then, scope.Copy(), target, depth + 1, ref nodes, ids, c, buffName);
                    break;
                case CardBlockKind.ForEach:
                    Object(b.Object, scope, target, false, b.Id, c);
                    var child = scope.Copy(); child.Current = true;
                    Value(b.Condition, true, child, target, depth + 1, ref nodes, b.Id, c, buffName);
                    ValidateBlocks(b.Then, child, target, depth + 1, ref nodes, ids, c, buffName);
                    break;
                case CardBlockKind.RememberNumber:
                case CardBlockKind.RememberObject:
                    if (string.IsNullOrWhiteSpace(b.VariableName) || b.VariableName.Length > 32) Error(c, b.Id, "请填写最多 32 字的变量名称。");
                    if (b.Kind == CardBlockKind.RememberNumber) Value(b.Value, false, scope, target, depth + 1, ref nodes, b.Id, c, buffName);
                    else Object(b.Object, scope, target, true, b.Id, c);
                    scope.Variables[b.Id] = b.Kind == CardBlockKind.RememberObject;
                    break;
                case CardBlockKind.Stop:
                    break;
                case CardBlockKind.PickEnemy:case CardBlockKind.PickFriend:case CardBlockKind.PickFromSet:
                    Object(b.Object,scope,target,false,b.Id,c);
                    if(b.Object!=null&&CustomCardNames.Single(b.Object.Kind))Error(c,b.Id,"随机选择需要角色集合。",true);
                    var success=scope.Copy();success.Variables[b.Id]=true;
                    ValidateBlocks(b.Then,success,target,depth+1,ref nodes,ids,c,buffName);
                    ValidateBlocks(b.Else,scope.Copy(),target,depth+1,ref nodes,ids,c,buffName);break;
            }
        }
    }

    private static void Value(CardValue? v, bool boolean, Scope scope, bool target, int depth, ref int nodes, string id, CustomCardCompilation c, Func<string, string?>? buffName)
    {
        if (!string.IsNullOrEmpty(v?.NodeId)) id = v!.NodeId;
        if ((v==null||!CardBlueprintNumbers.Conversion(v.Kind))&&++nodes > MaximumNodes || depth > MaximumDepth) { Error(c, id, "表达式过于复杂。"); return; }
        if (v == null || !Enum.IsDefined(typeof(CardValueKind), v.Kind)) { Error(c, id, "缺少有效表达式。"); return; }
        if (CustomCardNames.Boolean(v.Kind) != boolean) Error(c, id, boolean ? "此处需要条件。" : "此处需要数值。");
        if (v.Kind == CardValueKind.Number && (double.IsNaN(v.Number) || double.IsInfinity(v.Number) || Math.Abs(v.Number) > 1000000)) Error(c, id, "数字必须有限且不超过 ±1,000,000。");
        if (v.Kind == CardValueKind.Variable && (!scope.Variables.TryGetValue(v.VariableId ?? "", out var isObject) || isObject)) Error(c, id, "引用的数值尚未定义，或不在这个分支内。",true);
        if (v.Kind == CardValueKind.Read || v.Kind == CardValueKind.Exists)
        {
            Object(v.Object, scope, target, true, id, c);
            if (v.Kind == CardValueKind.Read)
            {
                if (!Enum.IsDefined(typeof(CardDataField), v.Field)) Error(c, id, "未知战斗数据。");
                if (v.Field >= CardDataField.Energy && v.Object?.Kind != CardObjectKind.Self) Error(c, id, "玩家能量、牌堆和本牌费用仅从自己读取。",true);
                if (v.Field == CardDataField.BuffStacks) Buff(v.ResourceId, id, c, buffName);
            }
        }
        var arity = CustomCardNames.Arity(v.Kind);
        if (v.Inputs == null || v.Inputs.Count != arity) { Error(c, id, "表达式参数不完整。"); return; }
        foreach (var input in v.Inputs) Value(input, v.Kind == CardValueKind.And || v.Kind == CardValueKind.Or || v.Kind == CardValueKind.Not, scope, target, depth + (CardBlueprintNumbers.Conversion(v.Kind)?0:1), ref nodes, id, c, buffName);
        if (v.Kind == CardValueKind.Divide && v.Inputs[1]?.Kind == CardValueKind.Number && v.Inputs[1].Number == 0) Error(c, id, "除数不能为零。");
        if (v.Kind == CardValueKind.Clamp && v.Inputs[1]?.Kind == CardValueKind.Number && v.Inputs[2]?.Kind == CardValueKind.Number && v.Inputs[1].Number > v.Inputs[2].Number) Error(c, id, "范围下限不能大于上限。");
    }

    private static void Object(CardObjectReference? o, Scope scope, bool target, bool single, string id, CustomCardCompilation c)
    {
        if (o == null || !Enum.IsDefined(typeof(CardObjectKind), o.Kind)) { Error(c, id, "请选择有效对象。"); return; }
        if (single && !CustomCardNames.Single(o.Kind)) Error(c, id, "此处需要单个对象，请先逐个遍历或记住对象。",true);
        if (o.Kind == CardObjectKind.Current && !scope.Current) Error(c, id, "当前遍历对象只能在逐个执行内部使用。",true);
        if (o.Kind == CardObjectKind.Target && !target) Error(c, id, "只有需要选中目标的卡牌在使用时可引用选中目标。",true);
        if (o.Kind == CardObjectKind.Saved && (!scope.Variables.TryGetValue(o.VariableId ?? "", out var isObject) || !isObject)) Error(c, id, "引用的对象尚未定义，或不在这个分支内。",true);
    }

    private static void Buff(string id, string node, CustomCardCompilation c, Func<string, string?>? name)
    {
        void StateError(string message){if(c.Issues.Count<40)c.Issues.Add(new(){NodeId=node,FieldId=CardNodeCatalog.StateParameter,Message=message});}
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.Any(char.IsControl)) { StateError("请选择具体状态。"); return; }
        var display = name?.Invoke(id);
        if (name != null && display == null) StateError("状态不存在或未加载：" + id);
        c.BuffReferences[id] = display ?? id;
    }
    private static bool ValidId(string? id) => id != null && Identifier.IsMatch(id);
    private static void CheckId(string? id, CustomCardCompilation c, HashSet<string> ids) { if (!ValidId(id) || !ids.Add(id!)) Error(c, id ?? "", "积木身份无效或重复。"); }
    private static void Error(CustomCardCompilation c, string id, string message,bool connection=false) { if (c.Issues.Count < 40) c.Issues.Add(new() { NodeId = id, Message = message,BlocksConnection=connection }); }
    internal static string Quote(string? s)
    {
        var b = new StringBuilder("\"");
        foreach (var ch in s ?? "")
        {
            if (ch == '\\' || ch == '"') b.Append('\\').Append(ch);
            else if (ch < 32 || ch == 127) b.Append('\\').Append(((int)ch).ToString("D3", CultureInfo.InvariantCulture));
            else b.Append(ch);
        }
        return b.Append('"').ToString();
    }

    private sealed class Emitter
    {
        private readonly StringBuilder b = new(CustomCardLuaSupport.Source);
        private int serial;
        internal Emitter(CustomCardCompilation compilation) { }
        internal string Text => b.ToString();
        private string Next() => "__f.f" + (++serial).ToString(CultureInfo.InvariantCulture);
        internal void Rule(CustomCardRule r)
        {
            var root = Sequence(r.Blocks);
            b.AppendLine("do local c=__context(" + Quote(r.Trigger.ToString()) + "); __run(c,function() return " + root + "(c,function() __finish(c) end) end) end");
        }
        private string Sequence(List<CardRuleBlock> blocks)
        {
            var fs = blocks.Select(Block).ToArray();
            var name = Next();
            b.AppendLine("function " + name + "(c, done)");
            b.AppendLine("return __sequence(c,{" + string.Join(",", fs) + "},done)");
            b.AppendLine("end");
            return name;
        }
        private string Block(CardRuleBlock n)
        {
            bool pick=n.Kind==CardBlockKind.PickEnemy||n.Kind==CardBlockKind.PickFriend||n.Kind==CardBlockKind.PickFromSet;
            var then = n.Kind == CardBlockKind.If || n.Kind == CardBlockKind.Repeat || n.Kind == CardBlockKind.ForEach || pick ? Sequence(n.Then) : "";
            var otherwise = n.Kind == CardBlockKind.If || pick ? Sequence(n.Else) : "";
            var name = Next();
            b.AppendLine("-- node:" + n.Id);
            b.AppendLine("function " + name + "(c, done)");
            b.AppendLine("__tick(c," + Quote(n.Id) + ")");
            switch (n.Kind)
            {
                case CardBlockKind.Effect:
                    b.AppendLine("return __effect(c," + Quote(n.Effect.ToString()) + "," + Objects(n.Object) + "," + (CustomCardNames.HasAmount(n.Effect) ? Expr(n.Value) : "0") + "," + Quote(n.ResourceId) + ",done)"); break;
                case CardBlockKind.If:
                    b.AppendLine("if " + Expr(n.Condition) + " then return " + then + "(__child(c),done) else return " + otherwise + "(__child(c),done) end"); break;
                case CardBlockKind.Repeat:
                    b.AppendLine("return __repeat(c," + Expr(n.Value) + "," + then + ",done)"); break;
                case CardBlockKind.ForEach:
                    b.AppendLine("return __each(c," + Objects(n.Object) + ",function(c) return " + Expr(n.Condition) + " end," + then + ",done)"); break;
                case CardBlockKind.RememberNumber:
                    b.AppendLine("c.vars[" + Quote(n.Id) + "] = " + Expr(n.Value)); b.AppendLine("return done()"); break;
                case CardBlockKind.RememberObject:
                    b.AppendLine("c.vars[" + Quote(n.Id) + "] = " + Subject(n.Object)); b.AppendLine("return done()"); break;
                case CardBlockKind.Stop:
                    b.AppendLine("return __finish(c)"); break;
                case CardBlockKind.PickEnemy:case CardBlockKind.PickFriend:case CardBlockKind.PickFromSet:
                    b.AppendLine("return __pick(c,"+Quote(n.Id)+","+Objects(n.Object)+","+then+","+otherwise+",done)");break;
            }
            b.AppendLine("end");
            return name;
        }
        private static string Subject(CardObjectReference o) => o.Kind switch { CardObjectKind.Self => "__self.Self", CardObjectKind.Target => "c.target", CardObjectKind.Current => "c.current", CardObjectKind.Saved => "c.vars[" + Quote(o.VariableId) + "]", _ => "nil" };
        private static string Objects(CardObjectReference o) => CustomCardNames.Single(o.Kind) ? "__one(" + Subject(o) + ")" : "__objects(" + Quote(o.Kind.ToString()) + ")";
        private static string Expr(CardValue v) => CardBlueprintNumbers.Conversion(v.Kind)?ExprCore(v):"__value(c," + Quote(v.NodeId) + ",function() return " + ExprCore(v) + " end)";
        private static string ExprCore(CardValue v)
        {
            var a = v.Inputs.Count > 0 ? Expr(v.Inputs[0]) : "";
            var d = v.Inputs.Count > 1 ? Expr(v.Inputs[1]) : "";
            var e = v.Inputs.Count > 2 ? Expr(v.Inputs[2]) : "";
            switch (v.Kind)
            {
                case CardValueKind.Number: return "(" + v.Number.ToString("R", CultureInfo.InvariantCulture) + ")";
                case CardValueKind.Read: return "__read(" + Subject(v.Object) + "," + Quote(v.Field.ToString()) + "," + Quote(v.ResourceId) + ")";
                case CardValueKind.Variable: return "__number(c.vars[" + Quote(v.VariableId) + "])";
                case CardValueKind.Add: return "__number(" + a + "+" + d + ")";
                case CardValueKind.Subtract: return "__number(" + a + "-" + d + ")";
                case CardValueKind.Multiply: return "__number(" + a + "*" + d + ")";
                case CardValueKind.Divide: return "__divide(" + a + "," + d + ")";
                case CardValueKind.Minimum: return "math.min(" + a + "," + d + ")";
                case CardValueKind.Maximum: return "math.max(" + a + "," + d + ")";
                case CardValueKind.Floor: return "math.floor(" + a + ")";
                case CardValueKind.Ceiling: return "math.ceil(" + a + ")";
                case CardValueKind.AsRatio: case CardValueKind.AsNumber: return a;
                case CardValueKind.PercentPoints: return "__number(("+a+")*100)";
                case CardValueKind.Approximately: return "__near("+a+","+d+","+e+")";
                case CardValueKind.Clamp: return "__clamp(" + a + "," + d + "," + e + ")";
                case CardValueKind.Equal: return "(" + a + "==" + d + ")";
                case CardValueKind.NotEqual: return "(" + a + "~=" + d + ")";
                case CardValueKind.Greater: return "(" + a + ">" + d + ")";
                case CardValueKind.AtLeast: return "(" + a + ">=" + d + ")";
                case CardValueKind.Less: return "(" + a + "<" + d + ")";
                case CardValueKind.AtMost: return "(" + a + "<=" + d + ")";
                case CardValueKind.And: return "(" + a + " and " + d + ")";
                case CardValueKind.Or: return "(" + a + " or " + d + ")";
                case CardValueKind.Not: return "(not " + a + ")";
                case CardValueKind.Exists: return "__alive(" + Subject(v.Object) + ")";
                default: throw new InvalidOperationException("Unvalidated expression.");
            }
        }
    }
}
