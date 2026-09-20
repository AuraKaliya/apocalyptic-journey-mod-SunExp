using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.CustomCards;

// Parameters and help use the same node identity/port contract as the editor.
public static partial class CardNodeCatalog
{
    public static bool HasResultName(CardGraphNode n)=>n.Kind==CardNodeKind.CaptureNumber||n.Kind==CardNodeKind.CaptureObject||IsPicker(n.Kind);
    public static bool Matches(CardGraphNode n,string query)
    {
        string text=Title(n)+" "+Category(n)+" "+Description(n)+(RequiresState(n)?" BUFF buff 状态 层数":"")+(n.ValueKind==CardValueKind.Read&&n.Kind==CardNodeKind.Value?" 读取数值":"");
        return SearchWords(text,query);
    }
    public static bool SearchWords(string text,string query)=>query.Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries).All(word=>text.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);
    public static bool TemplateMatches(int index,string query)=>SearchWords(CardBlueprintTemplates.Names[index]+" 常用组合 "+string.Join(" ",CardBlueprintTemplates.Create(index).Graph.Nodes.Select(n=>Title(n)+" "+(RequiresState(n)?"BUFF 状态":""))),query);
    public static string ParameterSummary(CardGraphNode n)
    {
        var fields=new List<string>();
        if(RequiresState(n))fields.Add("具体状态（必选）");
        if(EditableObjectDefault(n))fields.Add("目标或角色（可接线）");
        if(HasResultName(n))fields.Add("结果名称");
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Read)fields.Add("读取属性");
        if(n.Kind==CardNodeKind.Value&&CardBlueprintNumbers.Comparison(n.ValueKind))fields.Add("比较关系");
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Number)fields.Add("数值及格式");
        fields.AddRange(Ports(n).Where(p=>!p.Output&&p.Type==CardPortType.Number).Select(p=>p.Name+"（填写或接线）"));
        fields.AddRange(Ports(n).Where(p=>!p.Output&&p.Required).Select(p=>p.Name+"（必须连线）"));
        if(n.Kind==CardNodeKind.Note)fields.Add("注释内容、尺寸");
        return fields.Count==0?"无需额外配置":string.Join("；",fields);
    }
    public static string PortSummary(CardGraphNode n,bool output)=>string.Join("、",Ports(n).Where(p=>p.Output==output).Select(p=>p.Name+"·"+CardBlueprintCompiler.TypeName(p.Type)));
    public static string Example(CardGraphNode n)
    {
        if(RequiresState(n))return n.Kind==CardNodeKind.Value?"选择一个已加载的状态，读取使用者持有的层数 → 与 3 比较 → 连接‘如果 / 否则’。未持有该状态时返回 0。":n.Effect==CardEffectKind.RemoveBuff?"选择要移除的状态，设置目标 → 接入执行流程。目标身上的该状态会被整个移除，不是减少一层。":"选择一个已加载的状态，设置目标和 3 层 → 接入执行流程。叠加和持续行为由该状态本身决定。";
        if(n.Kind==CardNodeKind.Entry)return Title(n)+" → 给予护盾（使用者，3 点）。";
        if(n.Kind==CardNodeKind.Effect)return "本牌使用时 → "+Title(n)+(FixedOwner(n)?"（作用于使用者）":"（选择目标）")+(CustomCardNames.HasAmount(n.Effect)?"，填写"+AmountName(n.Effect)+" 3。":"。 ");
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Read)return Title(n)+" → 数值比较 → 如果 / 否则。";
        if(n.Kind==CardNodeKind.Value&&CardBlueprintNumbers.Comparison(n.ValueKind))return "使用者的生命百分比 → 数值 A；数值 B 设为 50% → 条件结果连接‘如果 / 否则’。普通数值与比例需要先明确转换。";
        if(n.Kind==CardNodeKind.Value&&CustomCardNames.Boolean(n.ValueKind))return n.ValueKind==CardValueKind.Exists?"角色 → 判断对象有效 → 如果 / 否则；成立分支中再读取属性。":"两个数值比较的结果 → 条件接口 → 如果 / 否则；取反只接一个条件。";
        if(n.Kind==CardNodeKind.Value)return "填写输入数值或连接取值节点 → 将输出连接到后续数值接口。比例 50% 与比例小数 0.5 等价；整数也是普通数值。";
        if(n.Kind==CardNodeKind.Object)return Title(n)+" → 对应类型的目标接口；角色集合可接群体效果或逐个对象执行。";
        return n.Kind switch
        {
            CardNodeKind.If=>"生命百分比小于 50% → 条件；成立 → 给予护盾，不成立 → 抽牌。分支末端返回后执行‘完成后’。",
            CardNodeKind.Repeat=>"本牌使用时 → 重复 3 次；执行体 → 使用者抽牌 1 张；完成后接下一个效果。",
            CardNodeKind.ForEach=>"所有敌人 → 对象集合；当前对象 → 伤害目标，执行体 → 造成普通伤害。",
            CardNodeKind.CaptureNumber=>"读取生命 → 捕获数值；执行到此处记住生命，再改变生命，捕获值仍保持原值。",
            CardNodeKind.CaptureObject=>"随机选中的对象 → 捕获对象；记住的对象可在之后的同一流程使用。",
            CardNodeKind.PickEnemy or CardNodeKind.PickFriend or CardNodeKind.PickFromSet=>"已选中 → 造成伤害，结果 → 伤害目标；未选中 → 给予使用者护盾。",
            CardNodeKind.Stop=>"如果 / 否则的某一分支 → 结束本次流程，不再执行该入口的剩余效果。",
            CardNodeKind.Note=>"为一组节点写下注释，说明‘生命较低时补充护盾’；注释不参与执行。",
            _=>"先修订旧稿中标记的问题，再接入当前支持的节点。"
        };
    }
    public static IEnumerable<CardGraphNode> Related(CardGraphNode n)=>Library().Where(other=>Identity(other)!=Identity(n)&&(RequiresState(n)?RequiresState(other)||other.Kind==CardNodeKind.If:Category(other)==Category(n))).Take(6);
}

/// <summary>Read-only presentation of a loaded state. Identity is always its ID, never its localized name.</summary>
public sealed class CardStateOption
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Description {get;set;}="";
    public string Source {get;set;}="";
    public string SourceName {get;set;}="";
    public string Icon {get;set;}="";
    public string SourceLabel=>Source=="BaseGame"?"原生游戏":Source.Length==0?"来源未标注":string.IsNullOrWhiteSpace(SourceName)||SourceName==Source?Source:SourceName+" · "+Source;
}

public static class CardStateQuery
{
    public const int PageSize=20;
    public static IReadOnlyList<CardStateOption> Filter(IEnumerable<CardStateOption> entries,string query,string? source=null)=>entries
        .Where(e=>(source==null||e.Source==source)&&CardNodeCatalog.SearchWords(e.Name+" "+e.Id+" "+e.SourceLabel,query))
        .OrderBy(e=>e.Name,StringComparer.Ordinal).ThenBy(e=>e.Source,StringComparer.Ordinal).ThenBy(e=>e.Id,StringComparer.Ordinal).ToArray();
    public static int PageCount(int count)=>Math.Max(1,(count+PageSize-1)/PageSize);
    public static IReadOnlyList<CardStateOption> Page(IReadOnlyList<CardStateOption> entries,int page)=>entries.Skip(Math.Max(0,Math.Min(page,PageCount(entries.Count)-1))*PageSize).Take(PageSize).ToArray();
}
