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
        var output=new StringBuilder();
        var variables=new Dictionary<string,string>();
        foreach(var rule in doc.Rules ?? new())
        {
            if(rule==null) continue;
            output.Append(CustomCardNames.Name(rule.Trigger,CustomCardNames.Triggers));
            if(rule.Trigger>=CardRuleTrigger.AfterUseRoundStart) output.Append(rule.MaximumTriggers==0 ? "（每次使用独立生效，本场持续）" : "（每次使用独立生效，本场最多 "+rule.MaximumTriggers+" 次）");
            output.AppendLine("：");
            if(rule.MaximumTriggersPerRound>0 && rule.Trigger>=CardRuleTrigger.AfterUseRoundStart)output.AppendLine("  每回合最多 "+rule.MaximumTriggersPerRound+" 次。");
            if(rule.Condition.Kind!=CardValueKind.Exists || rule.Condition.Object.Kind!=CardObjectKind.Self)output.AppendLine("  满足 "+Value(rule.Condition)+" 时：");
            Blocks(rule.Blocks,output,variables,1);
        }
        return output.ToString().TrimEnd();
    }
    private static void Blocks(List<CardRuleBlock>? blocks,StringBuilder output,Dictionary<string,string> vars,int depth)
    {
        if(depth>CustomCardCompiler.MaximumDepth || blocks==null) return;
        foreach(var b in blocks)
        {
            if(b==null) continue;
            output.Append(' ',depth*2).AppendLine(Block(b,vars));
            if(b.Kind==CardBlockKind.RememberNumber || b.Kind==CardBlockKind.RememberObject) vars[b.Id]=b.VariableName;
            if(b.Kind==CardBlockKind.If || b.Kind==CardBlockKind.Repeat || b.Kind==CardBlockKind.ForEach) Blocks(b.Then,output,new(vars),depth+1);
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
            _ => Subject(b.Object,vars)+" · "+CustomCardNames.Name(b.Effect,CustomCardNames.Effects)
                +(CustomCardNames.HasAmount(b.Effect) ? " "+EffectAmount(b.Value,vars) : "")
                +(CustomCardNames.NeedsBuff(b.Effect) ? "「"+b.ResourceId+"」" : "")
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
        string Arg(int i) => v.Inputs!=null && i<v.Inputs.Count ? Value(v.Inputs[i],vars,depth+1) : "[填写]";
        if(v.Kind==CardValueKind.Number) return v.Number.ToString("0.###",CultureInfo.InvariantCulture);
        if(v.Kind==CardValueKind.Read) return Subject(v.Object,vars)+"的"+CustomCardNames.Name(v.Field,CustomCardNames.Fields)+(v.Field==CardDataField.BuffStacks ? "「"+v.ResourceId+"」" : "");
        if(v.Kind==CardValueKind.Variable) return Variable(v.VariableId,vars);
        if(v.Kind==CardValueKind.Exists) return Subject(v.Object,vars)+"存在";
        var op=v.Kind switch { CardValueKind.Add=>"＋",CardValueKind.Subtract=>"－",CardValueKind.Multiply=>"×",CardValueKind.Divide=>"÷",CardValueKind.Equal=>"＝",CardValueKind.NotEqual=>"≠",CardValueKind.Greater=>"＞",CardValueKind.AtLeast=>"≥",CardValueKind.Less=>"＜",CardValueKind.AtMost=>"≤",CardValueKind.And=>"且",CardValueKind.Or=>"或",_=>"" };
        return op.Length>0 ? "("+Arg(0)+" "+op+" "+Arg(1)+")" : CustomCardNames.Name(v.Kind,CustomCardNames.Values)+"("+string.Join("，",Enumerable.Range(0,CustomCardNames.Arity(v.Kind)).Select(Arg))+")";
    }
}
