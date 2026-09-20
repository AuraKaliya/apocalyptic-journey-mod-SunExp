using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AuraToolsExp.Dll.Features.CustomCards;

public static class CustomCardDescription
{
    public static string Describe(CustomCardDocument doc)
    {
        var result=CustomCardCompiler.Compile(doc);
        return result.Success?result.Description:string.Join("\n",result.Issues.Select(i=>i.Message));
    }
    internal static string DescribeRules(IReadOnlyList<CustomCardRule> rules)
    {
        var output=new StringBuilder();
        foreach(var rule in rules)
        {
            if(rule==null) continue;
            var body=new StringBuilder();var variables=new Dictionary<string,string>();
            if(rule.Condition.Kind!=CardValueKind.Exists || rule.Condition.Object.Kind!=CardObjectKind.Self)body.AppendLine("  满足 "+Value(rule.Condition)+" 时：");
            Blocks(rule.Blocks,body,variables,1);
            output.Append(CustomCardNames.Name(rule.Trigger,CustomCardNames.Triggers)).Append('：');
            var lines=body.ToString().TrimEnd().Split('\n');
            if(lines.All(line=>line.StartsWith("  ",StringComparison.Ordinal)&&!line.StartsWith("    ",StringComparison.Ordinal)&&line.TrimEnd().EndsWith("。",StringComparison.Ordinal)))
                output.AppendLine(string.Join("",lines.Select(line=>line.Trim())));
            else {output.AppendLine();output.Append(body);}
        }
        return output.ToString().TrimEnd().Replace("\r\n","\n");
    }
    private static void Blocks(List<CardRuleBlock>? blocks,StringBuilder output,Dictionary<string,string> vars,int depth)
    {
        if(depth>CustomCardCompiler.MaximumDepth || blocks==null) return;
        for(int index=0;index<blocks.Count;index++)
        {
            var b=blocks[index];
            if(b==null) continue;
            // Only a top-level final stop is equivalent to naturally completing this trigger.
            if(depth==1&&index==blocks.Count-1&&b.Kind==CardBlockKind.Stop)continue;
            output.Append(' ',depth*2).AppendLine(Block(b,vars));
            if(b.Kind==CardBlockKind.RememberNumber || b.Kind==CardBlockKind.RememberObject) vars[b.Id]=b.VariableName;
            bool pick=b.Kind==CardBlockKind.PickEnemy||b.Kind==CardBlockKind.PickFriend||b.Kind==CardBlockKind.PickFromSet;
            if(pick){var captured=new Dictionary<string,string>(vars){[b.Id]=b.VariableName};Blocks(b.Then,output,captured,depth+1);if(b.Else?.Count>0){output.Append(' ',depth*2).AppendLine("无可选目标：");Blocks(b.Else,output,new(vars),depth+1);}}
            else if(b.Kind==CardBlockKind.If || b.Kind==CardBlockKind.Repeat || b.Kind==CardBlockKind.ForEach) Blocks(b.Then,output,new(vars),depth+1);
            if(b.Kind==CardBlockKind.If && b.Else?.Count>0) { output.Append(' ',depth*2).AppendLine("否则："); Blocks(b.Else,output,new(vars),depth+1); }
        }
    }
    internal static string Block(CardRuleBlock b,IReadOnlyDictionary<string,string>? vars=null)
    {
        return b.Kind switch
        {
            CardBlockKind.If => "如果 "+Value(b.Condition,vars)+"：",
            CardBlockKind.Repeat => "重复 "+Value(b.Value,vars)+" 次：",
            CardBlockKind.ForEach => "逐个处理"+Subject(b.Object,vars)+"，满足 "+Value(b.Condition,vars)+"：",
            CardBlockKind.RememberNumber => "记住「"+b.VariableName+"」＝"+Value(b.Value,vars),
            CardBlockKind.RememberObject => "记住「"+b.VariableName+"」＝"+Subject(b.Object,vars),
            CardBlockKind.Stop => "不再执行此次触发的后续效果。",
            CardBlockKind.PickEnemy=>"随机选择一个敌人，选中后：",CardBlockKind.PickFriend=>"随机选择一个友方（含使用者），选中后：",CardBlockKind.PickFromSet=>"从"+Subject(b.Object,vars)+"中随机选择一个角色，选中后：",
            _ => Effect(b.Effect,Subject(b.Object,vars),CustomCardNames.HasAmount(b.Effect)?EffectAmount(b.Value,vars):"",b.ResourceId)
        };
    }
    internal static string Effect(CardEffectKind kind,string subject,string amount,string resource)
    {
        subject=subject=="使用者"?"自身":subject=="使用时选中目标"?"选中目标":subject;
        return kind switch
        {
            CardEffectKind.Damage=>"对"+subject+"造成 "+amount+" 点普通伤害。",
            CardEffectKind.TrueDamage=>"对"+subject+"造成 "+amount+" 点真实伤害。",
            CardEffectKind.Shield=>"给予"+subject+" "+amount+" 点护盾。",
            CardEffectKind.Heal=>"为"+subject+"恢复 "+amount+" 点生命。",
            CardEffectKind.MaxHealth=>subject+"生命上限增加 "+amount+"。",
            CardEffectKind.Energy=>"获得 "+amount+" 点能量。",
            CardEffectKind.Draw=>"抽 "+amount+" 张牌。",
            CardEffectKind.AddBuff=>"对"+subject+"施加 "+amount+" 层「"+resource+"」。",
            CardEffectKind.RemoveBuff=>"移除"+subject+"的「"+resource+"」。",
            CardEffectKind.Discard=>"弃掉 "+amount+" 张手牌。",
            CardEffectKind.Burn=>"焚毁 "+amount+" 张手牌。",
            CardEffectKind.Shuffle=>"重洗牌堆。",
            CardEffectKind.SelectFromDeck=>"从抽牌堆选择 "+amount+" 张牌加入手牌。",
            CardEffectKind.SelectFromDiscard=>"从弃牌堆选择 "+amount+" 张牌加入手牌。",
            CardEffectKind.EndTurn=>"结束回合。",
            _=>throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
    private static string EffectAmount(CardValue value,IReadOnlyDictionary<string,string>? vars)
    {
        if(value.Kind==CardValueKind.Number)return Math.Floor(value.Number).ToString("0.###",CultureInfo.InvariantCulture);
        var text=Value(value,vars);
        if(value.Kind==CardValueKind.Floor||value.Kind==CardValueKind.Ceiling||(value.Kind==CardValueKind.Read&&value.Field!=CardDataField.HealthPercent))return text;
        return "向下取整("+text+")";
    }
    internal static string Subject(CardObjectReference? o,IReadOnlyDictionary<string,string>? vars=null) => o==null ? "[选择对象]" : o.Kind==CardObjectKind.Saved ? Variable(o.VariableId,vars) : CustomCardNames.Name(o.Kind,CustomCardNames.Objects);
    private static string Variable(string id,IReadOnlyDictionary<string,string>? vars) => vars!=null && vars.TryGetValue(id,out var name) ? "「"+name+"」" : "[记住的值]";
    internal static string Value(CardValue? v,IReadOnlyDictionary<string,string>? vars=null,int depth=0)
    {
        if(v==null || depth>CustomCardCompiler.MaximumDepth) return "[填写表达式]";
        string Arg(int i) => v.Inputs!=null && i<v.Inputs.Count ? Value(v.Inputs[i],vars,depth+(CardBlueprintNumbers.Conversion(v.Kind)?0:1)) : "[填写]";
        if(v.Kind==CardValueKind.Number) return CardBlueprintNumbers.Format(v.Number,v.NumberFormat)+(v.NumberFormat==CardNumberFormat.Percent?"%":v.NumberKind==CardNumberKind.Ratio?"（比例）":"");
        if(v.Kind==CardValueKind.AsRatio)return "比例("+Arg(0)+")";
        if(v.Kind==CardValueKind.AsNumber)return "比例的小数值("+Arg(0)+")";
        if(v.Kind==CardValueKind.PercentPoints)return "百分数值("+Arg(0)+")";
        if(v.Kind==CardValueKind.Approximately)return Arg(0)+" 约等于 "+Arg(1)+"（容差 "+Arg(2)+"）";
        if(v.Kind==CardValueKind.Read) return Subject(v.Object,vars)+"的"+CustomCardNames.Name(v.Field,CustomCardNames.Fields)+(v.Field==CardDataField.BuffStacks ? "「"+v.ResourceId+"」" : "");
        if(v.Kind==CardValueKind.Variable) return Variable(v.VariableId,vars);
        if(v.Kind==CardValueKind.Exists) return Subject(v.Object,vars)+"存在";
        var op=v.Kind switch { CardValueKind.Add=>"＋",CardValueKind.Subtract=>"－",CardValueKind.Multiply=>"×",CardValueKind.Divide=>"÷",CardValueKind.Equal=>"＝",CardValueKind.NotEqual=>"≠",CardValueKind.Greater=>"＞",CardValueKind.AtLeast=>"≥",CardValueKind.Less=>"＜",CardValueKind.AtMost=>"≤",CardValueKind.And=>"且",CardValueKind.Or=>"或",_=>"" };
        return op.Length>0 ? "("+Arg(0)+" "+op+" "+Arg(1)+")" : CustomCardNames.Name(v.Kind,CustomCardNames.Values)+"("+string.Join("，",Enumerable.Range(0,CustomCardNames.Arity(v.Kind)).Select(Arg))+")";
    }
}
