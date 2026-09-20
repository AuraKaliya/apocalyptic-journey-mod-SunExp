using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.CustomCards;

public static class CardBlueprintCompiler
{
    public static string? ConnectionError(CardBlueprint graph,CardGraphEdge edge,bool targeted=true)
    {
        var local=PortError(graph,edge);if(local!=null)return local;
        var copy=graph.Copy();
        copy.Edges.RemoveAll(e=>e.To==edge.To&&e.Input==edge.Input);
        var from=copy.Nodes.First(n=>n.Id==edge.From);
        if(CardNodeCatalog.Ports(from).First(p=>p.Output&&p.Id==edge.Output).Type==CardPortType.Execution)
            copy.Edges.RemoveAll(e=>e.From==edge.From&&e.Output==edge.Output);
        copy.Edges.Add(edge);
        CardBlueprintNumbers.AdoptEmptyInputs(copy);
        var reachable=new HashSet<string>();var pending=new Stack<string>();pending.Push(edge.To);
        while(pending.Count>0)
        {
            var node=pending.Pop();if(node==edge.From)return "连接形成循环，请使用有限重复节点。";
            if(reachable.Add(node))foreach(var link in copy.Edges.Where(e=>e.From==node))pending.Push(link.To);
        }
        var before=CustomCardCompiler.Compile(new(){Graph=graph,Targeted=targeted});
        var after=CustomCardCompiler.Compile(new(){Graph=copy,Targeted=targeted});
        var old=new HashSet<string>(before.Issues.Select(i=>i.NodeId+"|"+i.PortId+"|"+i.Message));
        return after.Issues.FirstOrDefault(i=>i.BlocksConnection&&!old.Contains(i.NodeId+"|"+i.PortId+"|"+i.Message))?.Message;
    }
    public static bool Connect(CardBlueprint graph,CardGraphEdge edge,bool targeted,out string? error)
    {
        error=ConnectionError(graph,edge,targeted);if(error!=null)return false;
        graph.Edges.RemoveAll(e=>e.To==edge.To&&e.Input==edge.Input);
        var port=CardNodeCatalog.Ports(graph.Nodes.First(n=>n.Id==edge.From)).First(p=>p.Output&&p.Id==edge.Output);
        if(port.Type==CardPortType.Execution)graph.Edges.RemoveAll(e=>e.From==edge.From&&e.Output==edge.Output);
        graph.Edges.Add(edge);CardBlueprintNumbers.AdoptEmptyInputs(graph);return true;
    }
    public static string? InsertionError(CardBlueprint graph,string edgeId,CardGraphNode node,bool targeted)
    {
        var edge=graph.Edges.FirstOrDefault(e=>e.Id==edgeId);if(edge==null)return "原连线已不存在。";
        var originalError=PortError(graph,edge);if(originalError!=null)return originalError;
        var source=graph.Nodes.FirstOrDefault(n=>n.Id==edge.From);if(source==null)return "原连线的节点已不存在。";
        var type=CardNodeCatalog.Ports(source).FirstOrDefault(p=>p.Output&&p.Id==edge.Output)?.Type;
        var ports=CardNodeCatalog.Ports(node);var input=ports.FirstOrDefault(p=>!p.Output&&p.Type==type);var output=ports.FirstOrDefault(p=>p.Output&&p.Type==type&&(type!=CardPortType.Execution||p.Id=="next"));
        if(input==null||output==null)return "此节点没有与连线匹配的输入和输出。";
        if(graph.Nodes.Any(n=>n.Id==node.Id))return "节点身份重复。";
        var copy=graph.Copy();copy.Nodes.Add(node);copy.Edges.RemoveAll(e=>e.Id==edgeId);
        copy.Edges.Add(new(){From=edge.From,Output=edge.Output,To=node.Id,Input=input.Id});
        copy.Edges.Add(new(){From=node.Id,Output=output.Id,To=edge.To,Input=edge.Input});
        var before=CustomCardCompiler.Compile(new(){Graph=graph,Targeted=targeted});var after=CustomCardCompiler.Compile(new(){Graph=copy,Targeted=targeted});
        return after.Issues.FirstOrDefault(i=>i.BlocksConnection&&!before.Issues.Any(b=>b.NodeId==i.NodeId&&b.Message==i.Message))?.Message;
    }
    public static bool Insert(CardBlueprint graph,string edgeId,CardGraphNode node,bool targeted,out string? error)
    {
        error=InsertionError(graph,edgeId,node,targeted);if(error!=null)return false;
        var edge=graph.Edges.First(e=>e.Id==edgeId);var source=graph.Nodes.First(n=>n.Id==edge.From);
        var type=CardNodeCatalog.Ports(source).First(p=>p.Output&&p.Id==edge.Output).Type;var ports=CardNodeCatalog.Ports(node);
        var input=ports.First(p=>!p.Output&&p.Type==type);var output=ports.First(p=>p.Output&&p.Type==type&&(type!=CardPortType.Execution||p.Id=="next"));
        graph.Nodes.Add(node);graph.Edges.Remove(edge);graph.Edges.Add(new(){From=edge.From,Output=edge.Output,To=node.Id,Input=input.Id});graph.Edges.Add(new(){From=node.Id,Output=output.Id,To=edge.To,Input=edge.Input});return true;
    }
    private static string? PortError(CardBlueprint g,CardGraphEdge e)
    {
        var from=g.Nodes.FirstOrDefault(n=>n.Id==e.From);var to=g.Nodes.FirstOrDefault(n=>n.Id==e.To);
        if(from==null||to==null)return "连接的节点已不存在。";
        var a=CardNodeCatalog.Ports(from).FirstOrDefault(p=>p.Output&&p.Id==e.Output);
        var b=CardNodeCatalog.Ports(to).FirstOrDefault(p=>!p.Output&&p.Id==e.Input);
        if(a==null||b==null)return "连接端口已不存在。";
        if(a.Type!=b.Type)return "端口类型不匹配："+TypeName(a.Type)+"不能接入"+TypeName(b.Type)+"。";
        if(e.From==e.To)return "不能连接回节点自身。";
        return null;
    }
    public static string TypeName(CardPortType type)=>type switch {CardPortType.Execution=>"执行",CardPortType.Number=>"数值",CardPortType.Boolean=>"条件",CardPortType.Actor=>"单个角色",_=>"角色集合"};

    public static List<CustomCardRule> Lower(CustomCardDocument doc,CustomCardCompilation result)
    {
        var g=doc.Graph;var rules=new List<CustomCardRule>();
        void Error(string id,string message,string port="",string edge="",bool connection=false)=>result.Issues.Add(new(){NodeId=id,Message=message,PortId=port,EdgeId=edge,BlocksConnection=connection});
        if(g?.Nodes==null||g.Edges==null||g.Groups==null){Error("","蓝图结构不完整。");return rules;}
        if(g.Nodes.Count>1024||g.Edges.Count>4096||g.Groups.Count>128){Error("","蓝图草稿超过容量限制。");return rules;}
        if(g.Nodes.Any(n=>n==null)||g.Edges.Any(e=>e==null)||g.Groups.Any(n=>n==null)){Error("","蓝图包含空元素。");return rules;}
        if(g.Nodes.Any(n=>string.IsNullOrEmpty(n.Id)||n.Id.Length>100)||g.Nodes.GroupBy(n=>n.Id).Any(x=>x.Count()!=1)) {Error("","节点身份无效或重复。");return rules;}
        if(g.Edges.Any(e=>string.IsNullOrEmpty(e.Id)||e.Id.Length>100)||g.Edges.GroupBy(e=>e.Id).Any(x=>x.Count()!=1)){Error("","连线身份无效或重复。");return rules;}
        var nodes=g.Nodes.ToDictionary(n=>n.Id,StringComparer.Ordinal);
        foreach(var n in g.Nodes)
        {
            if(n.Version!=2||!Enum.IsDefined(typeof(CardNodeKind),n.Kind))Error(n.Id,"不支持这个节点版本或类型。");
            if(n.Numbers==null||n.Numbers.Count>3||n.Numbers.Any(p=>(p.Key!="a"&&p.Key!="b"&&p.Key!="c")||!CardBlueprintNumbers.Valid(p.Value)))Error(n.Id,"数值格式或数值数据无效。");
            if(n.Kind==CardNodeKind.Unsupported)Error(n.Id,"旧监听不再支持，请重写或明确删除此节点；原稿已保留。");
            if(n.Label==null||n.Label.Length>500||n.ResourceId==null||n.VariableId==null||n.GroupId==null)Error(n.Id,"节点参数无效。");
            if(n.VariableName==null||n.VariableName.Length>32)Error(n.Id,"捕获名称最多 32 字。");
            if(CardNodeCatalog.FixedOwner(n)&&(n.Subject!=CardObjectKind.Self||n.ManyTargets))Error(n.Id,"此节点固定作用于使用者，不能修改对象范围。",connection:true);
            if(n.Kind==CardNodeKind.Object&&!Enum.IsDefined(typeof(CardObjectKind),n.Subject))Error(n.Id,"对象来源无效。");
            if(float.IsNaN(n.X)||float.IsInfinity(n.X)||float.IsNaN(n.Y)||float.IsInfinity(n.Y)||Math.Abs(n.X)>1000000||Math.Abs(n.Y)>1000000)Error(n.Id,"节点位置无效。");
            if(!Enum.IsDefined(typeof(CardValueKind),n.ValueKind)||!Enum.IsDefined(typeof(CardObjectKind),n.Subject)||!Enum.IsDefined(typeof(CardEffectKind),n.Effect))Error(n.Id,"未知节点参数。");
            if(n.Subject==CardObjectKind.RandomEnemy||n.Subject==CardObjectKind.RandomFriend)Error(n.Id,"请使用独立随机选择节点；通用对象参数不能隐含抽样。");
        }
        if(result.Issues.Count>0)return rules;
        foreach(var e in g.Edges){var error=PortError(g,e);if(error!=null)Error(e.To,error,e.Input,e.Id);}
        if(result.Issues.Count>0)return rules;
        foreach(var input in g.Edges.GroupBy(e=>e.To+"/"+e.Input))if(input.Count()>1)Error(input.First().To,"一个输入只能接入一个来源。",input.First().Input);
        foreach(var output in g.Edges.GroupBy(e=>e.From+"/"+e.Output))
            if(output.Count()>1&&CardNodeCatalog.Ports(nodes[output.First().From]).Any(p=>p.Output&&p.Id==output.First().Output&&p.Type==CardPortType.Execution))Error(output.First().From,"一个执行出口只能连接一个后续步骤。",output.First().Output);
        var colors=new Dictionary<string,int>();
        bool Visit(string id)
        {
            if(colors.TryGetValue(id,out var state))return state==1;
            colors[id]=1;
            foreach(var e in g.Edges.Where(e=>e.From==id))if(Visit(e.To))return true;
            colors[id]=2;return false;
        }
        foreach(var n in g.Nodes)if(Visit(n.Id)){Error(n.Id,"连接形成循环，请使用有限重复节点。");break;}
        var entries=g.Nodes.Where(n=>n.Kind==CardNodeKind.Entry).ToArray();
        if(entries.GroupBy(n=>n.Trigger).Any(x=>x.Count()>1))Error("","同一种触发时点只能有一个入口。");
        if(entries.Any(n=>n.Trigger<CardRuleTrigger.Draw||n.Trigger>CardRuleTrigger.Discard))Error("","卡牌只支持抽到、使用和丢弃三个入口。");
        if(result.Issues.Count>0)return rules;
        var inputs=g.Edges.ToDictionary(e=>e.To+"/"+e.Input);
        result.Issues.AddRange(CardBlueprintNumbers.Validate(g));if(result.Issues.Count>0)return rules;
        var numeric=new CardBlueprintNumbers.Analysis(g);
        var exec=g.Edges.Where(e=>CardNodeCatalog.Ports(nodes[e.From]).Any(p=>p.Output&&p.Id==e.Output&&p.Type==CardPortType.Execution)).ToDictionary(e=>e.From+"/"+e.Output);
        var used=new HashSet<string>();var owners=new Dictionary<string,string>();var visiting=new HashSet<string>();
        string entryId="";int expressionCount=0;
        void Use(CardGraphNode n)
        {
            used.Add(n.Id);
            if(owners.TryGetValue(n.Id,out var owner)&&owner!=entryId)Error(n.Id,"不同入口不能直接共享节点，请复制或插入片段。",connection:true);
            owners[n.Id]=entryId;
        }
        CardObjectReference ObjectInput(CardGraphNode n,string current)
        {
            if(CardNodeCatalog.FixedOwner(n))return new(){Kind=CardObjectKind.Self};
            if(n.Kind==CardNodeKind.PickEnemy)return new(){Kind=CardObjectKind.Enemies};
            if(n.Kind==CardNodeKind.PickFriend)return new(){Kind=CardObjectKind.Friends};
            if(!inputs.TryGetValue(n.Id+"/object",out var e))
            {
                var port=CardNodeCatalog.Ports(n).FirstOrDefault(p=>!p.Output&&p.Id=="object");
                if(port?.Required==true)Error(n.Id,"请连接必需的"+port.Name+"。","object");
                if(port!=null && (port.Type==CardPortType.Actor)!=CustomCardNames.Single(n.Subject))Error(n.Id,"默认对象与端口类型不匹配，请选择单个角色或对应集合。","object",connection:true);
                return new(){Kind=n.Subject,VariableId=n.VariableId};
            }
            var from=nodes[e.From];Use(from);
            if(from.Kind==CardNodeKind.CaptureObject||CardNodeCatalog.IsPicker(from.Kind))return new(){Kind=CardObjectKind.Saved,VariableId=from.Id};
            if(from.Kind==CardNodeKind.ForEach)
            {
                if(current!=from.Id)Error(n.Id,"当前对象只能在对应的遍历体内使用；跨遍历请先记住对象。","object",e.Id,connection:true);
                return new(){Kind=CardObjectKind.Current};
            }
            return new(){Kind=from.Subject,VariableId=from.VariableId};
        }
        CardValue ValueInput(CardGraphNode n,string port,string current,int depth,int hops=0)
        {
            bool conversion=inputs.TryGetValue(n.Id+"/"+port,out var sourceEdge)&&CardBlueprintNumbers.Conversion(nodes[sourceEdge.From].ValueKind);
            if(hops>64||depth>CustomCardCompiler.MaximumDepth||(!conversion&&++expressionCount>4096)){Error(n.Id,"数据表达式过于复杂。",port);return CardValue.Constant(0);}
            if(!inputs.TryGetValue(n.Id+"/"+port,out var e))
            {
                var p=CardNodeCatalog.Ports(n).FirstOrDefault(p=>!p.Output&&p.Id==port);
                if(p?.Required==true){Error(n.Id,"请连接必需的"+p.Name+"。",port);return new(){Kind=CardValueKind.Exists};}
                var literal=n.PeekOperand(port);
                if(!literal.IsSet)Error(n.Id,"请填写"+(p?.Name??"数值")+"。",port);
                return new(){Kind=CardValueKind.Number,Number=literal.Value,NumberKind=literal.Kind,NumberFormat=literal.Format};
            }
            var source=nodes[e.From];Use(source);
            var shape=numeric.Output(source);
            if(source.Kind==CardNodeKind.CaptureNumber)return new(){Kind=CardValueKind.Variable,VariableId=source.Id,NodeId=source.Id,NumberKind=shape.Kind,NumberFormat=shape.Format};
            if(source.ValueKind==CardValueKind.Number&&!source.PeekOperand("a").IsSet)Error(source.Id,"请填写固定数值。","a");
            var value=new CardValue {NodeId=source.Id,Kind=source.ValueKind,Number=source.Number,NumberKind=shape.Kind,NumberFormat=shape.Format,Field=source.Field,ResourceId=source.ResourceId,VariableId=source.VariableId,Object=ObjectInput(source,current)};
            for(int i=0;i<CustomCardNames.Arity(value.Kind);i++)value.Inputs.Add(ValueInput(source,new[]{"a","b","c"}[i],current,depth+(CardBlueprintNumbers.Conversion(value.Kind)?0:1),hops+1));
            return value;
        }
        string Next(string id,string port)=>exec.TryGetValue(id+"/"+port,out var edge)?edge.To:"";
        List<CardRuleBlock> Sequence(string first,string current,int depth)
        {
            var blocks=new List<CardRuleBlock>();
            if(depth>CustomCardCompiler.MaximumDepth){Error(first,"控制结构嵌套过深。");return blocks;}
            string id=first;
            while(id.Length>0)
            {
                var n=nodes[id];Use(n);
                if(!visiting.Add(id)){Error(id,"执行节点有多个路径，请通过控制节点的完成出口连接。");break;}
                var b=new CardRuleBlock {Id=n.Id,Effect=n.Effect,Object=ObjectInput(n,current),ResourceId=n.ResourceId,VariableName=string.IsNullOrWhiteSpace(n.VariableName)?CardNodeCatalog.Title(n):n.VariableName};
                switch(n.Kind)
                {
                    case CardNodeKind.Effect:b.Kind=CardBlockKind.Effect;if(CustomCardNames.HasAmount(n.Effect))b.Value=ValueInput(n,"value",current,0);break;
                    case CardNodeKind.If:b.Kind=CardBlockKind.If;b.Condition=ValueInput(n,"condition",current,0);b.Then=Sequence(Next(id,"then"),current,depth+1);b.Else=Sequence(Next(id,"else"),current,depth+1);break;
                    case CardNodeKind.Repeat:b.Kind=CardBlockKind.Repeat;b.Value=ValueInput(n,"value",current,0);b.Then=Sequence(Next(id,"body"),current,depth+1);break;
                    case CardNodeKind.ForEach:b.Kind=CardBlockKind.ForEach;b.Condition=new(){Kind=CardValueKind.Exists,Object=new(){Kind=CardObjectKind.Current}};b.Then=Sequence(Next(id,"body"),id,depth+1);break;
                    case CardNodeKind.CaptureNumber:b.Kind=CardBlockKind.RememberNumber;b.Value=ValueInput(n,"value",current,0);break;
                    case CardNodeKind.CaptureObject:b.Kind=CardBlockKind.RememberObject;break;
                    case CardNodeKind.PickEnemy:case CardNodeKind.PickFriend:case CardNodeKind.PickFromSet:
                        b.Kind=n.Kind==CardNodeKind.PickEnemy?CardBlockKind.PickEnemy:n.Kind==CardNodeKind.PickFriend?CardBlockKind.PickFriend:CardBlockKind.PickFromSet;
                        b.Then=Sequence(Next(id,"then"),current,depth+1);b.Else=Sequence(Next(id,"else"),current,depth+1);break;
                    case CardNodeKind.Stop:b.Kind=CardBlockKind.Stop;break;
                    default:Error(id,"此节点不能作为执行步骤。");return blocks;
                }
                if((b.Object.Kind==CardObjectKind.RandomEnemy||b.Object.Kind==CardObjectKind.RandomFriend)&&n.Kind!=CardNodeKind.CaptureObject)Error(id,"随机对象需要先通过选择并记住对象节点抽样。");
                blocks.Add(b);id=Next(id,"next");
            }
            return blocks;
        }
        foreach(var entry in entries.OrderBy(n=>n.Trigger))
        {
            entryId=entry.Id;Use(entry);var blocks=Sequence(Next(entry.Id,"next"),"",0);
            if(blocks.Count>0)rules.Add(new(){Id=entry.Id,Trigger=entry.Trigger,Blocks=blocks});
            else result.Warnings.Add(new(){NodeId=entry.Id,Message="此入口尚未连接效果。"});
        }
        if(rules.Count==0)Error("","请连接至少一条有实际行为的入口流程。");
        foreach(var n in g.Nodes.Where(n=>!used.Contains(n.Id)&&n.Kind!=CardNodeKind.Note))result.Warnings.Add(new(){NodeId=n.Id,Message="未参与执行："+CardNodeCatalog.Title(n)});
        return rules;
    }
}
