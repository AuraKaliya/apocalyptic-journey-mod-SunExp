using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace AuraToolsExp.Dll.Features.CustomCards;

public static class CardBlueprintMigration
{
    public static CustomCardDocument Read(string json)
    {
        if(json==null||System.Text.Encoding.UTF8.GetByteCount(json)>2*1024*1024)throw new InvalidOperationException("作品文件超过 2 MB。");
        using var reader=new JsonTextReader(new StringReader(json)){MaxDepth=80};
        var obj=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
        if((string?)obj["Format"]!=CustomCardDocument.FormatId)throw new InvalidOperationException("不是自建卡牌作品。");
        int version=(int?)obj["SchemaVersion"]??0;
        if(version<1||version>CustomCardDocument.CurrentVersion)throw new InvalidOperationException("作品版本不兼容，原文件保持不变。");
        if(version<4&&obj["Graph"]?["Nodes"] is JArray legacyNodes)
            foreach(var node in legacyNodes.OfType<JObject>())
            {
                if(node["Numbers"]!=null)continue;
                node["Numbers"]=JObject.FromObject(new Dictionary<string,CardNumber>{{"a",CardNumber.Plain((double?)node["Number"]??6)},{"b",CardNumber.Plain((double?)node["Second"]??1)},{"c",CardNumber.Plain((double?)node["Third"]??100)}});
            }
        var doc=obj.ToObject<CustomCardDocument>(JsonSerializer.Create(new(){TypeNameHandling=TypeNameHandling.None,MaxDepth=80}))!;
        if(version<CustomCardDocument.CurrentVersion)return Upgrade(doc,json);
        if(obj["Graph"]==null||doc.Graph==null||doc.Rules!=null)throw new InvalidOperationException("作品必须只有蓝图逻辑。");
        CheckEnvelope(doc);return doc;
    }
    public static void CheckEnvelope(CustomCardDocument doc)
    {
        if(doc.Graph?.Nodes==null||doc.Graph.Edges==null||doc.Graph.Groups==null||doc.Graph.Nodes.Count>(doc.SchemaVersion<4?512:1024)||doc.Graph.Edges.Count>(doc.SchemaVersion<4?2048:4096)||doc.Graph.Groups.Count>128||doc.Graph.Nodes.Any(n=>n==null)||doc.Graph.Edges.Any(e=>e==null)||doc.Graph.Groups.Any(g=>g==null))throw new InvalidOperationException("蓝图结构无效或超过草稿容量。");
        if(doc.Artwork==null||!CardPixelCanvas.IsValid(doc.Artwork.Size,doc.Artwork.Pixels))throw new InvalidOperationException("卡面数据无效。");
        if(doc.MigrationReview==null||doc.LegacyBackup==null)throw new InvalidOperationException("迁移记录无效。");
        bool Identity(string? id)=>id!=null&&Regex.IsMatch(id,"^[a-zA-Z0-9_-]{1,100}$",RegexOptions.CultureInvariant);
        bool Coordinate(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value)<=1000000;
        if(!Identity(doc.Id)||doc.Graph.Nodes.Any(n=>!Identity(n.Id))||doc.Graph.Nodes.GroupBy(n=>n.Id).Any(g=>g.Count()>1))throw new InvalidOperationException("作品或节点身份无效。");
        if(doc.Graph.Edges.Any(e=>!Identity(e.Id)||e.From==null||e.To==null||e.Input==null||e.Output==null)||doc.Graph.Edges.GroupBy(e=>e.Id).Any(g=>g.Count()>1))throw new InvalidOperationException("连线身份无效。");
        if(doc.Graph.Groups.Any(g=>!Identity(g.Id)||g.Name==null||g.Name.Length>500)||doc.Graph.Groups.GroupBy(g=>g.Id).Any(g=>g.Count()>1))throw new InvalidOperationException("组合身份无效。");
        if(!Coordinate(doc.Graph.PanX)||!Coordinate(doc.Graph.PanY)||!Coordinate(doc.Graph.Zoom)||doc.Graph.Nodes.Any(n=>!Coordinate(n.X)||!Coordinate(n.Y)))throw new InvalidOperationException("画布坐标无效。");
        if(doc.Graph.Nodes.Any(n=>n.Label==null||n.Label.Length>500||n.ResourceId==null||n.ResourceId.Length>200||n.VariableId==null||n.GroupId==null||n.VariableName==null||n.VariableName.Length>32)||doc.Name==null||doc.Note==null)throw new InvalidOperationException("作品文本无效或过长。");
        if(doc.Graph.Nodes.Any(n=>!Coordinate(n.NoteWidth)||!Coordinate(n.NoteHeight)||n.NoteWidth<180||n.NoteWidth>1400||n.NoteHeight<96||n.NoteHeight>1400))throw new InvalidOperationException("注释尺寸无效。");
        if(doc.Graph.Nodes.Any(n=>n.Version!=(doc.SchemaVersion<4?1:2)&&!(doc.SchemaVersion<4&&n.Version==2)))throw new InvalidOperationException("作品包含较新版本的节点，原文件保持不变。");
        if(doc.Graph.Nodes.Any(n=>n.Numbers==null||n.Numbers.Count>3||n.Numbers.Any(p=>(p.Key!="a"&&p.Key!="b"&&p.Key!="c")||!CardBlueprintNumbers.Valid(p.Value))))throw new InvalidOperationException("数值格式或数值数据无效。");
    }
    public static CustomCardDocument Upgrade(CustomCardDocument doc,string? original=null)
    {
        if(doc.SchemaVersion==CustomCardDocument.CurrentVersion){CheckEnvelope(doc);return doc;}
        if(doc.SchemaVersion==3)
        {
            CheckEnvelope(doc);UpgradeNumbers(doc.Graph);doc.SchemaVersion=CustomCardDocument.CurrentVersion;CheckEnvelope(doc);return doc;
        }
        if(doc.SchemaVersion==2)
        {
            CheckEnvelope(doc);if(doc.Rules!=null)throw new InvalidOperationException("蓝图作品不能同时包含旧规则。");
            doc.BlueprintV2Backup=original??JsonConvert.SerializeObject(doc);
            UpgradeGraph(doc.Graph,doc.MigrationReview);
            // Retain relative placement while giving the wider, sectioned nodes room to render.
            if(doc.Graph.Nodes.All(n=>Math.Abs(n.X)<=500000&&Math.Abs(n.Y)<=500000))
                foreach(var node in doc.Graph.Nodes){node.X*=412f/292f;node.Y*=350f/222f;}
            doc.Graph.HasView=false;doc.Graph.PanX=doc.Graph.PanY=24;
            UpgradeNumbers(doc.Graph);doc.SchemaVersion=CustomCardDocument.CurrentVersion;CheckEnvelope(doc);return doc;
        }
        if(doc.SchemaVersion!=1||doc.Rules==null||doc.Rules.Count>12)throw new InvalidOperationException("旧稿结构无效，无法迁移。");
        var rules=doc.Rules;
        doc.LegacyBackup=original??JsonConvert.SerializeObject(doc);
        doc.Graph=FromProgram(rules,false);doc.Rules=null;doc.SchemaVersion=CustomCardDocument.CurrentVersion;
        foreach(var group in rules.Where(r=>r.Trigger<=CardRuleTrigger.Discard).GroupBy(r=>r.Trigger))
            if(group.Count()>1&&group.Take(group.Count()-1).Any(r=>Blocks(r.Blocks).Any(b=>b.Kind==CardBlockKind.Effect&&Async(b.Effect))))
                doc.MigrationReview.Add(CustomCardNames.Name(group.Key,CustomCardNames.Triggers)+"有多组规则与异步操作；新版依次完成后继续，请检查顺序。");
        if(rules.Any(r=>Blocks(r.Blocks).Any(b=>Random(b.Object)||Values(b.Value).Concat(Values(b.Condition)).Any(v=>Random(v.Object)))))
            doc.MigrationReview.Add("旧稿引用随机对象，请检查每次抽样的位置；使用选择并记住对象节点可复用同一目标。");
        UpgradeGraph(doc.Graph,doc.MigrationReview);CheckEnvelope(doc);return doc;
    }
    private static void UpgradeNumbers(CardBlueprint graph)
    {
        // Keep authored relative placement while making room for the wider inputs and separate outputs.
        if(graph.Nodes.Any(n=>n.Version<2)&&graph.Nodes.All(n=>Math.Abs(n.X)<=800000&&Math.Abs(n.Y)<=700000))
            foreach(var node in graph.Nodes){node.X*=1.25f;node.Y*=1.4f;}
        foreach(var node in graph.Nodes.ToArray())
        {
            if(node.Version>=2)continue;
            node.Version=2;
            if(node.Kind!=CardNodeKind.Value||node.ValueKind!=CardValueKind.Read||node.Field!=CardDataField.HealthPercent)continue;
            var consumers=graph.Edges.Where(e=>e.From==node.Id&&e.Output=="result").ToArray();if(consumers.Length==0)continue;
            float x=node.X+(node.X>999600?-360:360),y=node.Y+(node.Y>999800?-180:180);
            var scalar=graph.Add(CardNodeKind.Value,x,y);scalar.ValueKind=CardValueKind.PercentPoints;scalar.Label="保留原百分数值（50% → 50）";scalar.GroupId=node.GroupId;
            foreach(var edge in consumers)edge.From=scalar.Id;
            graph.Link(node,"result",scalar,"a");
        }
        graph.HasView=false;
    }
    private static void UpgradeGraph(CardBlueprint graph,ICollection<string>? review)
    {
        if(graph?.Nodes==null||graph.Edges==null)throw new InvalidOperationException("旧蓝图结构无效。");
        foreach(var n in graph.Nodes.ToArray())
        {
            if(n.Kind==CardNodeKind.CaptureNumber||n.Kind==CardNodeKind.CaptureObject)
                n.VariableName=n.Label.Length>0&&n.Label.Length<=32?n.Label:n.Kind==CardNodeKind.CaptureNumber?"捕获数值":"捕获对象";
            var incoming=graph.Edges.FirstOrDefault(e=>e.To==n.Id&&e.Input=="object");
            if(n.Kind==CardNodeKind.CaptureObject)
            {
                if(incoming==null&&(n.Subject==CardObjectKind.RandomEnemy||n.Subject==CardObjectKind.RandomFriend))
                {
                    n.Kind=n.Subject==CardObjectKind.RandomEnemy?CardNodeKind.PickEnemy:CardNodeKind.PickFriend;
                    n.Subject=n.Kind==CardNodeKind.PickEnemy?CardObjectKind.Enemies:CardObjectKind.Friends;
                    foreach(var edge in graph.Edges.Where(e=>e.From==n.Id&&e.Output=="next"))edge.Output="then";
                    review?.Add("随机节点「"+n.VariableName+"」改为已选中 / 无可选目标分支；请检查原先空目标时继续执行的步骤。");
                }
                else
                {
                    if(incoming==null)
                    {
                        if(n.Subject==CardObjectKind.Saved)graph.Edges.Add(new(){From=n.VariableId,Output="result",To=n.Id,Input="object"});
                        else{var source=graph.Add(CardNodeKind.Object,n.X-330,n.Y+180);source.Subject=n.Subject;graph.Link(source,"result",n,"object");}
                    }
                    n.Subject=CardObjectKind.Self;n.VariableId="";
                }
                if(n.Label=="随机选择一个敌人"||n.Label=="随机选择一个友方")n.Label="";
            }
            if(CardNodeCatalog.FixedOwner(n)&&incoming!=null)
            {
                var source=graph.Nodes.FirstOrDefault(x=>x.Id==incoming.From);
                if(source?.Kind==CardNodeKind.Object&&source.Subject==CardObjectKind.Self)graph.Edges.Remove(incoming);
                else review?.Add("「"+CardNodeCatalog.Title(n)+"」属于使用者，旧对象连接不合法，请删除或重新设计。");
            }
            // A previously wired Self overrides an unused default on player-only nodes.
            if(CardNodeCatalog.FixedOwner(n)&&incoming!=null&&!graph.Edges.Contains(incoming))n.Subject=CardObjectKind.Self;
        }
    }
    private static bool Random(CardObjectReference o)=>o!=null&&(o.Kind==CardObjectKind.RandomEnemy||o.Kind==CardObjectKind.RandomFriend);
    private static bool Async(CardEffectKind e)=>e==CardEffectKind.Draw||e==CardEffectKind.Discard||e==CardEffectKind.Burn||e==CardEffectKind.SelectFromDeck||e==CardEffectKind.SelectFromDiscard;
    private static IEnumerable<CardRuleBlock> Blocks(List<CardRuleBlock> bs,int depth=0)
    {
        if(bs==null||depth>12)yield break;
        foreach(var b in bs){if(b==null)continue;yield return b;foreach(var c in Blocks(b.Then,depth+1).Concat(Blocks(b.Else,depth+1)))yield return c;}
    }
    private static IEnumerable<CardValue> Values(CardValue v,int depth=0)
    {
        if(v==null||depth>12)yield break;yield return v;
        foreach(var c in v.Inputs??new())foreach(var x in Values(c,depth+1))yield return x;
    }
    // This builder is also used for bundled templates. The returned graph is the only persisted source.
    public static CardBlueprint FromProgram(IReadOnlyList<CustomCardRule> rules,bool upgrade=true)
    {
        var g=new CardBlueprint();int count=0;float row=40;
        CardGraphNode Node(CardNodeKind kind,float x,float y)
        {
            if(++count>1024)throw new InvalidOperationException("旧稿过大，原稿保持不变。");return g.Add(kind,x,y);
        }
        void Object(CardObjectReference value,CardGraphNode n)
        {
            n.Subject=value?.Kind??CardObjectKind.Self;n.VariableId=value?.VariableId??"";
            if(n.Subject==CardObjectKind.Saved)g.Edges.Add(new(){From=n.VariableId,Output="result",To=n.Id,Input="object"});
        }
        void Value(CardValue v,CardGraphNode parent,string port,int depth=0)
        {
            if(v==null||depth>12)throw new InvalidOperationException("旧稿表达式无效或过深。");
            if(v.Kind==CardValueKind.Number)
            {
                var literal=parent.Operand(port);literal.Value=v.Number;literal.Kind=v.NumberKind;literal.Format=v.NumberFormat;literal.IsSet=true;return;
            }
            if(v.Kind==CardValueKind.Variable){g.Edges.Add(new(){From=v.VariableId,Output="result",To=parent.Id,Input=port});return;}
            var n=Node(CardNodeKind.Value,parent.X-280,parent.Y+140+depth*25);n.ValueKind=v.Kind;n.Field=v.Field;n.ResourceId=v.ResourceId;Object(v.Object,n);
            if(v.Kind==CardValueKind.Read&&v.Field==CardDataField.HealthPercent&&v.NumberKind==CardNumberKind.Value)
            {
                var conversion=Node(CardNodeKind.Value,n.X+120,n.Y+150);conversion.ValueKind=CardValueKind.PercentPoints;g.Link(n,"result",conversion,"a");g.Link(conversion,"result",parent,port);
            }
            else g.Link(n,"result",parent,port);
            for(int i=0;i<(v.Inputs?.Count??0);i++)Value(v.Inputs![i],n,new[]{"a","b","c"}[i],depth+1);
        }
        CardGraphNode? Sequence(List<CardRuleBlock> blocks,CardGraphNode parent,string port,int depth=0)
        {
            if(blocks==null||depth>12)throw new InvalidOperationException("旧稿控制结构无效或过深。");
            CardGraphNode previous=parent;string output=port;CardGraphNode? first=null;
            foreach(var b in blocks)
            {
                if(b==null)throw new InvalidOperationException("旧稿含空步骤。");
                var kind=b.Kind switch {CardBlockKind.Effect=>CardNodeKind.Effect,CardBlockKind.If=>CardNodeKind.If,CardBlockKind.Repeat=>CardNodeKind.Repeat,CardBlockKind.ForEach=>CardNodeKind.ForEach,CardBlockKind.RememberNumber=>CardNodeKind.CaptureNumber,CardBlockKind.RememberObject=>CardNodeKind.CaptureObject,CardBlockKind.Stop=>CardNodeKind.Stop,CardBlockKind.PickEnemy=>CardNodeKind.PickEnemy,CardBlockKind.PickFriend=>CardNodeKind.PickFriend,CardBlockKind.PickFromSet=>CardNodeKind.PickFromSet,_=>CardNodeKind.Unsupported};
                var n=Node(kind,previous.X+300,previous.Y+(output=="next"?0:220));n.Id=b.Id;n.Effect=b.Effect;n.ResourceId=b.ResourceId;Object(b.Object,n);
                if(kind==CardNodeKind.PickEnemy)n.Subject=CardObjectKind.Enemies;
                if(kind==CardNodeKind.PickFriend)n.Subject=CardObjectKind.Friends;
                if(kind==CardNodeKind.PickFromSet&&!g.Edges.Any(e=>e.To==n.Id&&e.Input=="object"))
                {
                    var source=Node(CardNodeKind.Object,n.X-350,n.Y+200);source.Subject=n.Subject;g.Link(source,"result",n,"object");
                }
                if(kind==CardNodeKind.Effect)n.ManyTargets=!CustomCardNames.Single(n.Subject);
                if(kind==CardNodeKind.CaptureNumber||kind==CardNodeKind.CaptureObject)n.Label=b.VariableName;
                // Convert direct random effects into an explicit, once-per-step capture.
                if(kind==CardNodeKind.Effect&&Random(b.Object))
                {
                    var capture=Node(CardNodeKind.CaptureObject,n.X,n.Y);capture.Subject=b.Object.Kind;capture.Label="随机选择对象";n.X+=300;
                    g.Link(previous,output,capture,"in");g.Link(capture,"next",n,"in");g.Link(capture,"result",n,"object");n.Subject=CardObjectKind.Self;
                }
                else g.Link(previous,output,n,"in");
                first??=n;
                if(kind==CardNodeKind.If)Value(b.Condition,n,"condition");
                if(kind==CardNodeKind.Repeat||kind==CardNodeKind.CaptureNumber||(kind==CardNodeKind.Effect&&CustomCardNames.HasAmount(b.Effect)))Value(b.Value,n,"value");
                if(CardNodeCatalog.IsBranch(kind)){if(CardNodeCatalog.IsPicker(kind))n.VariableName=b.VariableName;Sequence(b.Then,n,"then",depth+1);Sequence(b.Else,n,"else",depth+1);}
                if(kind==CardNodeKind.Repeat)Sequence(b.Then,n,"body",depth+1);
                if(kind==CardNodeKind.ForEach)
                {
                    // v1 allowed a per-object predicate. Keep it inside the matching scope.
                    var predicate=Node(CardNodeKind.If,n.X+300,n.Y+220);predicate.Label="逐个对象的条件";
                    g.Link(n,"body",predicate,"in");Value(b.Condition,predicate,"condition");Sequence(b.Then,predicate,"then",depth+1);
                }
                previous=n;output="next";
            }
            return first;
        }
        foreach(var grouped in rules.GroupBy(r=>r.Trigger))
        {
            if(grouped.Key>CardRuleTrigger.Discard)
            {
                foreach(var r in grouped){var n=Node(CardNodeKind.Unsupported,40,row);n.Id=r.Id;n.Label="旧监听 · "+(r.Trigger==CardRuleTrigger.AfterUseHurt?"使用后受到伤害":r.Trigger==CardRuleTrigger.AfterUseRoundStart?"使用后回合开始":"使用后回合结束")+"（原稿已保留）";row+=220;}continue;
            }
            var entry=Node(CardNodeKind.Entry,40,row);entry.Trigger=grouped.Key;row+=560;CardGraphNode previous=entry;
            foreach(var r in grouped)
            {
                if(grouped.Count()==1&&r.Condition.Kind==CardValueKind.Exists&&r.Condition.Object.Kind==CardObjectKind.Self)
                {entry.Id=r.Id;Sequence(r.Blocks,entry,"next");continue;}
                var guard=Node(CardNodeKind.If,previous.X+300,previous.Y);guard.Id=r.Id;guard.Label="触发条件";
                g.Link(previous,"next",guard,"in");Value(r.Condition,guard,"condition");Sequence(r.Blocks,guard,"then");previous=guard;
            }
        }
        if(upgrade)UpgradeGraph(g,null);CardBlueprintLayout.Arrange(g);return g;
    }
}

public static class CardBlueprintLayout
{
    public static void Arrange(CardBlueprint graph)
    {
        var placed=new Dictionary<string,(int Column,int Row)>();var occupied=new HashSet<string>();
        var remaining=graph.Nodes.ToList();int limit=remaining.Count+1;
        while(remaining.Count>0&&limit-->0)
        {
            bool progress=false;
            foreach(var node in remaining.ToArray())
            {
                var parents=graph.Edges.Where(e=>e.To==node.Id&&graph.Nodes.Any(n=>n.Id==e.From)).Select(e=>e.From).Distinct().ToArray();
                if(parents.Any(p=>!placed.ContainsKey(p)))continue;
                int column=parents.Length==0?0:parents.Max(p=>placed[p].Column)+1;
                int row=parents.Length==0?0:(int)Math.Round(parents.Average(p=>placed[p].Row));
                while(occupied.Contains(column+"/"+row))row++;
                occupied.Add(column+"/"+row);placed[node.Id]=(column,row);node.X=30+column*412;node.Y=30+row*350;
                remaining.Remove(node);progress=true;
            }
            if(!progress)break;
        }
        int extra=placed.Count==0?0:placed.Values.Max(p=>p.Row)+1;
        foreach(var node in remaining){node.X=30;node.Y=30+350*extra++;}
    }
}

public static class CardBlueprintTemplates
{
    public static readonly string[] Names={"直接伤害","生命低于一半强化","护盾后攻击","群体攻击","三段攻击","抽牌后获得护盾","已有状态条件","丢弃时获得护盾","选牌后继续","抽到时获得能量","随机敌人和空目标分支","从友方集合随机给予护盾"};
    public static CustomCardDocument Create(int index)
    {
        var doc=new CustomCardDocument {Name=Names[Math.Max(0,Math.Min(Names.Length-1,index))]};
        CardRuleBlock Effect(CardEffectKind effect,double value,CardObjectKind target=CardObjectKind.Self)=>new(){Effect=effect,Value=CardValue.Constant(value),Object=new(){Kind=target}};
        var rule=new CustomCardRule();var hit=Effect(CardEffectKind.Damage,6,CardObjectKind.Target);
        switch(index)
        {
            case 1:rule.Blocks.Add(new(){Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.HealthPercent),CardValue.Ratio(.5)),Then=new(){Effect(CardEffectKind.Damage,12,CardObjectKind.Target)},Else=new(){hit}});break;
            case 2:rule.Blocks.Add(Effect(CardEffectKind.Shield,5));rule.Blocks.Add(hit);break;
            case 3:doc.Targeted=false;rule.Blocks.Add(new(){Kind=CardBlockKind.ForEach,Object=new(){Kind=CardObjectKind.Enemies},Condition=new(){Kind=CardValueKind.Exists,Object=new(){Kind=CardObjectKind.Current}},Then=new(){Effect(CardEffectKind.Damage,4,CardObjectKind.Current)}});break;
            case 4:rule.Blocks.Add(new(){Kind=CardBlockKind.Repeat,Value=CardValue.Constant(3),Then=new(){Effect(CardEffectKind.Damage,2,CardObjectKind.Target)}});break;
            case 5:doc.Targeted=false;rule.Blocks.Add(Effect(CardEffectKind.Draw,2));var shield=Effect(CardEffectKind.Shield,0);shield.Value=CardValue.Reading(CardDataField.HandCount);rule.Blocks.Add(shield);break;
            case 6:var value=CardValue.Reading(CardDataField.BuffStacks,CardObjectKind.Target);rule.Blocks.Add(new(){Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Greater,value,CardValue.Constant(0)),Then=new(){Effect(CardEffectKind.Damage,12,CardObjectKind.Target)},Else=new(){hit}});break;
            case 7:doc.Targeted=false;rule.Trigger=CardRuleTrigger.Discard;rule.Blocks.Add(Effect(CardEffectKind.Shield,3));break;
            case 8:doc.Targeted=false;rule.Blocks.Add(Effect(CardEffectKind.SelectFromDeck,1));rule.Blocks.Add(Effect(CardEffectKind.Shield,3));break;
            case 9:doc.Targeted=false;rule.Trigger=CardRuleTrigger.Draw;rule.Blocks.Add(Effect(CardEffectKind.Energy,1));break;
            case 10:
                doc.Targeted=false;var random=new CardRuleBlock{Kind=CardBlockKind.PickEnemy,VariableName="本次选中的敌人",Object=new(){Kind=CardObjectKind.Enemies}};
                var damage=Effect(CardEffectKind.Damage,6);damage.Object=new(){Kind=CardObjectKind.Saved,VariableId=random.Id};random.Then.Add(damage);random.Else.Add(Effect(CardEffectKind.Shield,3));rule.Blocks.Add(random);break;
            case 11:
                doc.Targeted=false;var fromSet=new CardRuleBlock{Kind=CardBlockKind.PickFromSet,VariableName="本次选中的友方",Object=new(){Kind=CardObjectKind.Friends}};
                var protection=Effect(CardEffectKind.Shield,6);protection.Object=new(){Kind=CardObjectKind.Saved,VariableId=fromSet.Id};fromSet.Then.Add(protection);rule.Blocks.Add(fromSet);break;
            default:rule.Blocks.Add(hit);break;
        }
        doc.Graph=CardBlueprintMigration.FromProgram(new[]{rule});return doc;
    }
}
