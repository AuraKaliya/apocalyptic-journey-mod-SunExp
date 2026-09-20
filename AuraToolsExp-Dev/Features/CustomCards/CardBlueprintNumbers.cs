using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AuraToolsExp.Dll.Features.CustomCards;

[JsonConverter(typeof(StringEnumConverter))] public enum CardNumberKind { Value, Ratio }
[JsonConverter(typeof(StringEnumConverter))] public enum CardNumberFormat { Number, Percent }

/// <summary>Canonical numeric value. Percent is a display scale, never a second arithmetic value.</summary>
public sealed class CardNumber
{
    public double Value { get; set; }
    public CardNumberKind Kind { get; set; }
    public CardNumberFormat Format { get; set; }
    public bool IsSet { get; set; }
    public CardNumber Copy()=>new(){Value=Value,Kind=Kind,Format=Format,IsSet=IsSet};
    [JsonIgnore] public string EditText=>IsSet?CardBlueprintNumbers.Format(Value,Format):"";
    [JsonIgnore] public string Suffix=>Format==CardNumberFormat.Percent?"%":Kind==CardNumberKind.Ratio?"比例":"";
    public static CardNumber Plain(double value)=>new(){Value=value,IsSet=true};
}

/// <summary>Shared by compilation, graph inputs, descriptions and migration. Never executes a card.</summary>
public static class CardBlueprintNumbers
{
    public static readonly CardValueKind[] Comparisons={CardValueKind.Less,CardValueKind.AtMost,CardValueKind.Equal,CardValueKind.NotEqual,CardValueKind.AtLeast,CardValueKind.Greater,CardValueKind.Approximately};
    public static bool Comparison(CardValueKind kind)=>Comparisons.Contains(kind);
    public static bool Conversion(CardValueKind kind)=>kind==CardValueKind.AsRatio||kind==CardValueKind.AsNumber||kind==CardValueKind.PercentPoints;
    public static string Operator(CardValueKind kind)=>kind switch{CardValueKind.Less=>"< 小于",CardValueKind.AtMost=>"≤ 小于等于",CardValueKind.Equal=>"= 等于",CardValueKind.NotEqual=>"≠ 不等于",CardValueKind.AtLeast=>"≥ 大于等于",CardValueKind.Greater=>"> 大于",CardValueKind.Approximately=>"≈ 约等于",_=>""};
    public static string Format(double value,CardNumberFormat format)=> (format==CardNumberFormat.Percent?value*100:value).ToString("R",CultureInfo.InvariantCulture);
    public static bool TryParse(string text,CardNumberFormat format,out double value,out string error)
    {
        text=(text??"").Trim();bool suffix=text.EndsWith("%",StringComparison.Ordinal)||text.EndsWith("％",StringComparison.Ordinal);
        if(suffix&&format!=CardNumberFormat.Percent){value=0;error="请先选择百分比格式。";return false;}
        if(suffix)text=text.Substring(0,text.Length-1).Trim();
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value)) {error="请输入有效数字。";return false;}
        if(format==CardNumberFormat.Percent)value/=100;
        if(Math.Abs(value)>1000000){error=format==CardNumberFormat.Percent?"百分比不能超过 ±100,000,000%。":"数值不能超过 ±1,000,000。";return false;}
        error="";return true;
    }
    public static bool Valid(CardNumber? n)=>n!=null&&Enum.IsDefined(typeof(CardNumberKind),n.Kind)&&Enum.IsDefined(typeof(CardNumberFormat),n.Format)
        &&(n.Format!=CardNumberFormat.Percent||n.Kind==CardNumberKind.Ratio)&&!double.IsNaN(n.Value)&&!double.IsInfinity(n.Value)&&Math.Abs(n.Value)<=1000000;
    public static CardNumber Read(CardGraphNode node)=>new(){Kind=node.Field==CardDataField.HealthPercent?CardNumberKind.Ratio:CardNumberKind.Value,Format=node.Field==CardDataField.HealthPercent?CardNumberFormat.Percent:CardNumberFormat.Number};
    public static CardNumber Input(CardBlueprint graph,CardGraphNode node,string port)=>new Analysis(graph).Input(node,port);
    public static CardNumber Output(CardBlueprint graph,CardGraphNode node)=>new Analysis(graph).Output(node);
    public static void AdoptEmptyInputs(CardBlueprint graph)
    {
        var analysis=new Analysis(graph);
        foreach(var node in graph.Nodes)
        foreach(var port in CardNodeCatalog.Ports(node).Where(p=>!p.Output&&p.Type==CardPortType.Number))
        {
            var literal=node.Operand(port.Id);if(literal.IsSet||graph.Edges.Any(e=>e.To==node.Id&&e.Input==port.Id))continue;
            var format=analysis.Input(node,port.Id);literal.Kind=format.Kind;literal.Format=format.Format;
        }
    }
    public static IReadOnlyList<CardCompileIssue> Validate(CardBlueprint graph)
    {
        var issues=new List<CardCompileIssue>();var analysis=new Analysis(graph);
        void Error(CardGraphNode n,string port,string message)=>issues.Add(new(){NodeId=n.Id,PortId=port,Message=message,BlocksConnection=true});
        foreach(var n in graph.Nodes)
        {
            if(n.Kind==CardNodeKind.Effect||n.Kind==CardNodeKind.Repeat)
            {
                if(CardNodeCatalog.Ports(n).Any(p=>p.Id=="value")&&analysis.Input(n,"value").Kind==CardNumberKind.Ratio)
                    Error(n,"value","此处需要实际数量，请将比例乘以基础数值，或使用明确的转换节点。");
                continue;
            }
            if(n.Kind!=CardNodeKind.Value||CustomCardNames.Boolean(n.ValueKind)&&!Comparison(n.ValueKind))continue;
            var a=analysis.Input(n,"a");var b=analysis.Input(n,"b");var c=analysis.Input(n,"c");
            bool Has(string port)=>graph.Edges.Any(e=>e.To==n.Id&&e.Input==port)||n.PeekOperand(port).IsSet;
            bool same=Comparison(n.ValueKind)||n.ValueKind==CardValueKind.Add||n.ValueKind==CardValueKind.Subtract||n.ValueKind==CardValueKind.Minimum||n.ValueKind==CardValueKind.Maximum||n.ValueKind==CardValueKind.Clamp;
            if(same&&Has("a")&&Has("b")&&a.Kind!=b.Kind)Error(n,"b","普通数值与比例不能直接比较或相加，请选择对应格式或转换节点。");
            if((n.ValueKind==CardValueKind.Clamp||n.ValueKind==CardValueKind.Approximately)&&Has("a")&&Has("c")&&a.Kind!=c.Kind)Error(n,"c","范围或容差必须与输入数值使用相同的含义。");
            if(n.ValueKind==CardValueKind.AsRatio&&Has("a")&&a.Kind!=CardNumberKind.Value)Error(n,"a","数值转比例需要普通数值输入。");
            if((n.ValueKind==CardValueKind.AsNumber||n.ValueKind==CardValueKind.PercentPoints)&&Has("a")&&a.Kind!=CardNumberKind.Ratio)Error(n,"a","此转换需要比例输入。");
            if(n.ValueKind==CardValueKind.Approximately&&!graph.Edges.Any(e=>e.To==n.Id&&e.Input=="c")&&n.PeekOperand("c").IsSet&&n.Third<0)Error(n,"c","容差不能为负数。");
        }
        return issues;
    }
    public sealed class Analysis
    {
        private readonly CardBlueprint graph;
        private readonly Dictionary<string,CardGraphNode> nodes;
        private readonly Dictionary<string,CardNumber> cache=new();
        private readonly HashSet<string> visiting=new();
        public Analysis(CardBlueprint graph){this.graph=graph;nodes=graph.Nodes.GroupBy(n=>n.Id).ToDictionary(g=>g.Key,g=>g.First());}
        public CardNumber Input(CardGraphNode node,string port)
        {
            var edge=graph.Edges.FirstOrDefault(e=>e.To==node.Id&&e.Input==port);
            if(edge!=null&&nodes.TryGetValue(edge.From,out var source))return Output(source);
            var literal=node.PeekOperand(port).Copy();if(literal.IsSet)return literal;
            bool same=node.Kind==CardNodeKind.Value&&(Comparison(node.ValueKind)||node.ValueKind==CardValueKind.Add||node.ValueKind==CardValueKind.Subtract||node.ValueKind==CardValueKind.Minimum||node.ValueKind==CardValueKind.Maximum||node.ValueKind==CardValueKind.Clamp);
            if(same)
            {
                foreach(var peer in new[]{"a","b","c"}.Where(p=>p!=port))
                {
                    var link=graph.Edges.FirstOrDefault(e=>e.To==node.Id&&e.Input==peer);
                    CardNumber? value=link!=null&&nodes.TryGetValue(link.From,out var from)?Output(from):node.PeekOperand(peer).IsSet?node.PeekOperand(peer):null;
                    if(value!=null){literal.Kind=value.Kind;literal.Format=value.Format;break;}
                }
            }
            return literal;
        }
        public CardNumber Output(CardGraphNode node)
        {
            if(cache.TryGetValue(node.Id,out var result))return result;
            if(!visiting.Add(node.Id))return new();
            if(node.Kind==CardNodeKind.CaptureNumber)result=Input(node,"value");
            else if(node.ValueKind==CardValueKind.Read)result=Read(node);
            else if(node.ValueKind==CardValueKind.Number)result=node.PeekOperand("a").Copy();
            else
            {
                var a=Input(node,"a");var b=Input(node,"b");result=a.Copy();result.IsSet=false;
                if(node.ValueKind==CardValueKind.AsRatio){result.Kind=CardNumberKind.Ratio;result.Format=CardNumberFormat.Percent;}
                else if(node.ValueKind==CardValueKind.AsNumber||node.ValueKind==CardValueKind.PercentPoints||(node.ValueKind==CardValueKind.Divide&&a.Kind==b.Kind)||(node.ValueKind==CardValueKind.Multiply&&(a.Kind==CardNumberKind.Value||b.Kind==CardNumberKind.Value)))
                {result.Kind=CardNumberKind.Value;result.Format=CardNumberFormat.Number;}
            }
            visiting.Remove(node.Id);cache[node.Id]=result;return result;
        }
    }
}
