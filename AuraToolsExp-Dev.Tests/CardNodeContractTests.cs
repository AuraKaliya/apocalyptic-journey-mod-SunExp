using AuraToolsExp.Dll.Features.CustomCards;
using Newtonsoft.Json;

internal static partial class AuraToolsTestSuite
{
    private static void TestCardNodeContracts(bool executeLua)
    {
        TestStateAndGuideContracts();
        TestCardExpressionContracts(executeLua);
        var library=CardNodeCatalog.Library();
        Assert(library.Select(CardNodeCatalog.Identity).Distinct().Count()==library.Count,"catalog has no ambiguous node identities");
        foreach(var node in library)
        {
            var title=CardNodeCatalog.Title(node);node.Label="随机选择一个敌人";
            Assert(CardNodeCatalog.Title(node)==title,"remark cannot masquerade as another capability: "+title);
            var ports=CardNodeCatalog.Ports(node);
            Assert(ports.Select(p=>p.Id+"/"+p.Output).Distinct().Count()==ports.Count,"ports have stable unique identities: "+title);
            Assert(CardNodeCatalog.Description(node).Length>=10,"every catalog entry explains behavior: "+title);
            if(node.Kind==CardNodeKind.Object)Assert(ports.Count==1&&ports[0].Output&&!CardNodeCatalog.EditableObjectDefault(node),"fixed object sources cannot be relabeled into another type: "+title);
        }
        foreach(var effect in new[]{CardEffectKind.Energy,CardEffectKind.Draw,CardEffectKind.Discard,CardEffectKind.Burn,CardEffectKind.Shuffle,CardEffectKind.SelectFromDeck,CardEffectKind.SelectFromDiscard,CardEffectKind.EndTurn})
            Assert(!CardNodeCatalog.Ports(new(){Kind=CardNodeKind.Effect,Effect=effect}).Any(p=>p.Id=="object"),"owner-only effect has no actor input: "+effect);
        foreach(var field in Enum.GetValues<CardDataField>().Where(f=>f>=CardDataField.Energy))
            Assert(!CardNodeCatalog.Ports(new(){Kind=CardNodeKind.Value,ValueKind=CardValueKind.Read,Field=field}).Any(p=>p.Id=="object"),"owner/card read has no actor input: "+field);
        foreach(var kind in new[]{CardNodeKind.PickEnemy,CardNodeKind.PickFriend})
        {
            var ports=CardNodeCatalog.Ports(new(){Kind=kind});
            Assert(!ports.Any(p=>p.Id=="object"||p.Id=="next")&&ports.Any(p=>p.Id=="then")&&ports.Any(p=>p.Id=="else"),"fixed picker has immutable pool and explicit empty branch: "+kind);
        }
        var setPorts=CardNodeCatalog.Ports(new(){Kind=CardNodeKind.PickFromSet});
        Assert(setPorts.Any(p=>p.Id=="object"&&p.Required&&p.Type==CardPortType.Actors),"set picker requires collection, never a single actor");
        Assert(CardNodeCatalog.Ports(new(){Kind=CardNodeKind.CaptureObject}).Any(p=>p.Id=="object"&&p.Required&&p.Type==CardPortType.Actor),"capture requires explicit single actor");

        (CustomCardDocument Doc,CardGraphNode Pick,CardGraphNode Hit,CardGraphNode Empty) Picker(CardNodeKind kind)
        {
            var d=new CustomCardDocument{Targeted=false,Graph=new()};var g=d.Graph;var entry=g.Add(CardNodeKind.Entry);entry.Trigger=CardRuleTrigger.Use;
            var p=g.Add(kind);var hit=g.Add(CardNodeKind.Effect);hit.Effect=CardEffectKind.Damage;hit.Number=4;
            var empty=g.Add(CardNodeKind.Effect);empty.Effect=CardEffectKind.Shield;empty.Number=3;empty.Subject=CardObjectKind.Self;
            g.Link(entry,"next",p,"in");g.Link(p,"then",hit,"in");g.Link(p,"result",hit,"object");g.Link(p,"else",empty,"in");
            if(kind==CardNodeKind.PickFromSet){var source=g.Add(CardNodeKind.Object);source.Subject=CardObjectKind.Enemies;g.Link(source,"result",p,"object");}
            return(d,p,hit,empty);
        }
        foreach(var kind in new[]{CardNodeKind.PickEnemy,CardNodeKind.PickFriend,CardNodeKind.PickFromSet})
        {
            var sample=Picker(kind);var c=CustomCardCompiler.Compile(sample.Doc);
            Assert(c.Success,"canonical picker compiles: "+kind+" "+string.Join(";",c.Issues));
            if(kind!=CardNodeKind.PickFriend)
            {
                if(executeLua)
                {
                    RunCardLua(c.Scripts["UseScript"],"assert(enemy.CurHp==26 and own.CurHp==25 and own.Defend==8 and rolls==1)");
                    RunCardLua(c.Scripts["UseScript"],"assert(own.Defend==11 and rolls==0)","enemy.CurHp=0;enemy.state='Dead'\n");
                }
            }
            else if(executeLua)RunCardLua(c.Scripts["UseScript"],"assert(own.CurHp==21 and enemy.CurHp==30 and rolls==1)");
            // A data wire into the failure branch must not leak an uninitialized result.
            var before=JsonConvert.SerializeObject(sample.Doc.Graph);
            Assert(!CardBlueprintCompiler.Connect(sample.Doc.Graph,new(){From=sample.Pick.Id,Output="result",To=sample.Empty.Id,Input="object"},false,out _)&&before==JsonConvert.SerializeObject(sample.Doc.Graph),"failure-branch result wiring rejected atomically");
            sample.Doc.Graph.Delete(new[]{sample.Pick.Id});
            Assert(!CustomCardCompiler.Compile(sample.Doc).Success,"deleting picker preserves visible broken data references");
        }
        var multiple=Picker(CardNodeKind.PickEnemy);var second=multiple.Doc.Graph.Add(CardNodeKind.Effect);second.Effect=CardEffectKind.Damage;second.Number=5;
        multiple.Doc.Graph.Link(multiple.Hit,"next",second,"in");multiple.Doc.Graph.Link(multiple.Pick,"result",second,"object");
        if(executeLua)RunCardLua(CustomCardCompiler.Compile(multiple.Doc).Scripts["UseScript"],"assert(enemy.CurHp==21 and rolls==1)");
        var repeated=Picker(CardNodeKind.PickEnemy);var repeat=repeated.Doc.Graph.Add(CardNodeKind.Repeat);repeat.Number=3;var entryRepeat=repeated.Doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Entry);
        repeated.Doc.Graph.Edges.RemoveAll(e=>e.From==entryRepeat.Id);repeated.Doc.Graph.Link(entryRepeat,"next",repeat,"in");repeated.Doc.Graph.Link(repeat,"body",repeated.Pick,"in");
        if(executeLua)RunCardLua(CustomCardCompiler.Compile(repeated.Doc).Scripts["UseScript"],"assert(enemy.CurHp==18 and rolls==3)");
        var selfNode=repeated.Doc.Graph.Add(CardNodeKind.Object);selfNode.Subject=CardObjectKind.Self;
        Assert(CardBlueprintCompiler.ConnectionError(repeated.Doc.Graph,new(){From=selfNode.Id,Output="result",To=repeated.Pick.Id,Input="object"},false)!=null,"self cannot be connected to fixed random enemy");
        var setPick=Picker(CardNodeKind.PickFromSet);var one=setPick.Doc.Graph.Add(CardNodeKind.Object);one.Subject=CardObjectKind.Self;
        Assert(CardBlueprintCompiler.ConnectionError(setPick.Doc.Graph,new(){From=one.Id,Output="result",To=setPick.Pick.Id,Input="object"},false)!=null,"single actor cannot replace a collection");
        setPick.Doc.Graph.Edges.RemoveAll(e=>e.To==setPick.Pick.Id&&e.Input=="object");Assert(!CustomCardCompiler.Compile(setPick.Doc).Success,"collection input is actually required");
        var friendPick=Picker(CardNodeKind.PickFriend);friendPick.Hit.Effect=CardEffectKind.Shield;
        if(executeLua)RunCardLua(CustomCardCompiler.Compile(friendPick.Doc).Scripts["UseScript"],"assert(friend.Defend==4 and own.Defend==8 and enemy.Defend==0)","friend={CurHp=30,MaxHp=30,Defend=0,state='Default'};function self:SetStatus(filter) assert(filter=='AllFriends');self.Object:Clear();self.Object:Add(own);self.Object:Add(friend);return self.Object end;function self.DefaultDice:WithRange(low,high) assert(low==0 and high==1);return {Roll=function() return {Value=1} end} end\n");

        // v2's misleading random title with an explicit Self wire was really a Self capture.
        CustomCardDocument Legacy(bool wired)
        {
            var d=new CustomCardDocument{SchemaVersion=2,Targeted=false,Graph=new()};var g=d.Graph;var entry=g.Add(CardNodeKind.Entry);entry.Trigger=CardRuleTrigger.Use;
            var old=g.Add(CardNodeKind.CaptureObject);old.Subject=CardObjectKind.RandomEnemy;old.Label="随机选择一个敌人";
            var shield=g.Add(CardNodeKind.Effect);shield.Effect=CardEffectKind.Shield;shield.Number=6;
            g.Link(entry,"next",old,"in");g.Link(old,"next",shield,"in");g.Link(old,"result",shield,"object");
            if(wired){var self=g.Add(CardNodeKind.Object);self.Subject=CardObjectKind.Self;g.Link(self,"result",old,"object");}
            return d;
        }
        var original=LegacyNumericJson(Legacy(true),2);var migrated=CardBlueprintMigration.Read(original);
        Assert(migrated.BlueprintV2Backup==original&&migrated.SchemaVersion==CustomCardDocument.CurrentVersion,"v2 keeps exact original backup before migration");
        Assert(migrated.Graph.Nodes.Any(n=>n.Kind==CardNodeKind.CaptureObject)&&!migrated.Graph.Nodes.Any(n=>n.Kind==CardNodeKind.PickEnemy),"wired random v2 node migrates to truthful capture preserving actual behavior");
        if(executeLua)RunCardLua(CustomCardCompiler.Compile(migrated).Scripts["UseScript"],"assert(own.Defend==14 and enemy.Defend==0)");
        original=LegacyNumericJson(Legacy(false),2);migrated=CardBlueprintMigration.Read(original);
        Assert(migrated.MigrationReview.Count>0&&!CustomCardCompiler.Compile(migrated).Success,"empty-target behavior change requires explicit migration review");
        migrated.MigrationReview.Clear();Assert(CustomCardCompiler.Compile(migrated).Success,"reviewed legacy random blueprint is usable");
        var fresh=CardBlueprintMigration.Read(JsonConvert.SerializeObject(migrated));Assert(fresh.BlueprintV2Backup==original&&fresh.MigrationReview.Count==0,"v3 roundtrip does not remigrate or re-add notices");
        var named=new CustomCardDocument();var scripts=CustomCardCompiler.Compile(named).Scripts.ToArray();var note=named.Graph.Add(CardNodeKind.Note);note.Label="说明\n第二行";note.NoteWidth=540;
        Assert(CustomCardCompiler.Compile(named).Scripts.SequenceEqual(scripts),"annotation has no runtime script or execution semantics");
        note.NoteWidth=float.PositiveInfinity;bool rejected=false;try{CardBlueprintMigration.Read(JsonConvert.SerializeObject(named));}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"malformed note size rejected before Unity");
        var guide=CardNodeCatalog.GuideMarkdown();Assert(library.All(n=>guide.Contains("**"+CardNodeCatalog.Title(n)+"**")),"generated guide covers every shipped catalog entry");
        var folder=new DirectoryInfo(AppContext.BaseDirectory);while(folder!=null&&!Directory.Exists(Path.Combine(folder.FullName,"AuraToolsExp-Dev")))folder=folder.Parent;
        if(folder!=null){var path=Path.Combine(folder.FullName,"output","custom-card-review");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"node-catalog.generated.md"),guide);}
    }
    private static void TestCardExpressionContracts(bool lua)
    {
        CustomCardDocument Numeric(CardValue v,double expected)=>new(){Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){new(){Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Equal,v,(v.NumberKind==CardNumberKind.Ratio?CardValue.Ratio(expected):CardValue.Constant(expected))),Then=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}}}}})};
        var arithmetic=new[]{(CardValueKind.Add,9d,4d,13d),(CardValueKind.Subtract,9d,4d,5d),(CardValueKind.Multiply,9d,4d,36d),(CardValueKind.Divide,9d,4d,2.25d),(CardValueKind.Minimum,9d,4d,4d),(CardValueKind.Maximum,9d,4d,9d)};
        var values=arithmetic.Select(c=>(Value:CardValue.Compare(c.Item1,CardValue.Constant(c.Item2),CardValue.Constant(c.Item3)),Expected:c.Item4)).ToList();
        values.Add((new(){Kind=CardValueKind.Floor,Inputs=new(){CardValue.Constant(3.8)}},3));
        values.Add((new(){Kind=CardValueKind.Ceiling,Inputs=new(){CardValue.Constant(3.2)}},4));
        values.Add((new(){Kind=CardValueKind.Clamp,Inputs=new(){CardValue.Constant(20),CardValue.Constant(4),CardValue.Constant(9)}},9));
        foreach(var value in values)
        {
            var doc=Numeric(value.Value,value.Expected);var c=CustomCardCompiler.Compile(doc);
            Assert(c.Success,"numeric node evaluates exact result: "+value.Value.Kind);
            if(lua)RunCardLua(c.Scripts["UseScript"],"assert(own.Defend==11)");
        }
        var expected=new Dictionary<CardDataField,double>{{CardDataField.Health,25},{CardDataField.MaxHealth,50},{CardDataField.MissingHealth,25},{CardDataField.HealthPercent,.5},{CardDataField.Shield,8},{CardDataField.BuffStacks,3},{CardDataField.Energy,3},{CardDataField.MaxEnergy,3},{CardDataField.HandCount,4},{CardDataField.DeckCount,10},{CardDataField.DiscardCount,2},{CardDataField.CardCost,1}};
        foreach(var pair in expected)
        {
            var read=CardValue.Reading(pair.Key);read.ResourceId="poison";var doc=Numeric(read,pair.Value);doc.Cost=1;
            var c=CustomCardCompiler.Compile(doc);
            Assert(c.Success,"read field has declared owner and unit: "+pair.Key);
            if(lua)RunCardLua(c.Scripts["UseScript"],"assert(own.Defend==11)","for i=1,4 do self.HandCard:Add(card) end;self.DeckCard:Clear();for i=1,10 do self.DeckCard:Add(card) end;for i=1,2 do self.UsedCard:Add(card) end;function own:GetBuff(id) return {buffConfig={Level=3}} end\n");
        }
        foreach(var pair in new[]{(CardValueKind.Equal,false),(CardValueKind.NotEqual,true),(CardValueKind.Greater,true),(CardValueKind.AtLeast,true),(CardValueKind.Less,false),(CardValueKind.AtMost,false)})
        {
            var d=new CustomCardDocument{Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){new(){Kind=CardBlockKind.If,Condition=CardValue.Compare(pair.Item1,CardValue.Constant(9),CardValue.Constant(4)),Then=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}}}}})};
            Assert(CustomCardCompiler.Compile(d).Success,"comparison truth table: "+pair.Item1);
            if(lua)RunCardLua(CustomCardCompiler.Compile(d).Scripts["UseScript"],"assert(own.Defend=="+(pair.Item2?11:8)+")");
        }
        foreach(var effect in Enum.GetValues<CardEffectKind>())
        {
            var d=new CustomCardDocument{Targeted=false,Graph=new()};var entry=d.Graph.Add(CardNodeKind.Entry);var n=d.Graph.Add(CardNodeKind.Effect);n.Effect=effect;n.Subject=CardObjectKind.Self;n.ResourceId="poison";n.Number=3.8;d.Graph.Link(entry,"next",n,"in");
            var c=CustomCardCompiler.Compile(d,_=>"中毒");Assert(c.Success,"every effect family admits its declared defaults: "+effect);
            if(CustomCardNames.HasAmount(effect)){n.Number=-1;Assert(!CustomCardCompiler.Compile(d).Success,"negative amount rejected: "+effect);}
            if(lua)TestNativeCardEffect(c.Scripts["UseScript"],effect);
        }
    }
    private static void TestStateAndGuideContracts()
    {
        var catalog=CardNodeCatalog.Library();var states=catalog.Where(CardNodeCatalog.RequiresState).ToArray();
        Assert(states.Length==5,"all single/group state effects and stack reader share the state parameter contract");
        Assert(catalog.Where(n=>CardNodeCatalog.Matches(n,"BUFF")).Select(CardNodeCatalog.Identity).OrderBy(i=>i).SequenceEqual(states.Select(CardNodeCatalog.Identity).OrderBy(i=>i)),"BUFF alias finds every state node and no unrelated results");
        foreach(var node in catalog)Assert(!string.IsNullOrWhiteSpace(CardNodeCatalog.Example(node))&&!string.IsNullOrWhiteSpace(CardNodeCatalog.ParameterSummary(node)),"guide has example and parameter guidance: "+CardNodeCatalog.Title(node));
        foreach(var prototype in states)
        {
            var doc=new CustomCardDocument();var effect=doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);CardGraphNode node;
            if(prototype.Kind==CardNodeKind.Value)
            {
                node=doc.Graph.Add(CardNodeKind.Value);node.ValueKind=CardValueKind.Read;node.Field=CardDataField.BuffStacks;doc.Graph.Link(node,"result",effect,"value");
            }
            else{node=effect;node.Effect=prototype.Effect;node.ManyTargets=prototype.ManyTargets;node.Subject=node.ManyTargets?CardObjectKind.Enemies:CardObjectKind.Target;}
            var missing=CustomCardCompiler.Compile(doc,_=>null);
            Assert(missing.Issues.Any(i=>i.NodeId==node.Id&&i.FieldId==CardNodeCatalog.StateParameter),"state issue locates the correct node and editable field: "+CardNodeCatalog.Title(node));
            node.ResourceId="poison";var compiled=CustomCardCompiler.Compile(doc,id=>id=="poison"?"中毒":null);Assert(compiled.Success&&compiled.BuffReferences.ContainsKey("poison"),"selected state flows into compiler: "+CardNodeCatalog.Title(node));
            node.ResourceId="unloaded";Assert(CustomCardCompiler.Compile(doc,_=>null).Issues.Any(i=>i.FieldId==CardNodeCatalog.StateParameter),"unloaded state offers the same repair field");
            if(node.Kind==CardNodeKind.Value){node.Field=CardDataField.Health;Assert(!CardNodeCatalog.RequiresState(node)&&CustomCardCompiler.Compile(doc,_=>null).Success,"switching reader hides and ignores retained irrelevant state without deleting user data");}
        }
        var entries=Enumerable.Range(0,103).Select(i=>new CardStateOption{Id="state_"+i,Name="同名状态",Source=i%2==0?"BaseGame":"MOD"}).ToArray();
        var results=CardStateQuery.Filter(entries,"");var pages=Enumerable.Range(0,CardStateQuery.PageCount(results.Count)).SelectMany(p=>CardStateQuery.Page(results,p)).ToArray();
        Assert(pages.Length==103&&pages.Select(s=>s.Id).Distinct().Count()==103,"pagination makes all states reachable past 80 without duplicates");
        Assert(CardStateQuery.Filter(entries,"同名状态","MOD").Count==51,"source filtering distinguishes same-name states");
        Assert(CardStateQuery.Filter(entries,"state_102").Single().Id=="state_102","stable state ID can be searched directly");
        Assert(CardStateQuery.Page(Array.Empty<CardStateOption>(),99).Count==0&&CardStateQuery.PageCount(0)==1,"empty query supports a stable empty page");
        Assert(!CardNodeCatalog.TemplateMatches(1,"不可能匹配的内容"),"combination search obeys query instead of always displaying entries");
        Assert(new CardStateOption().SourceLabel=="来源未标注","missing provenance is explicit instead of guessed as base-game content");
        Assert(CardStateQuery.Filter(new[]{new CardStateOption{Id="foreign",Source="mod-id",SourceName="示例模组"}},"示例模组").Count==1,"state search includes the player-facing source name");
    }
    private static void TestNativeCardEffect(string script,CardEffectKind effect)
    {
        var prefix=@"
calls={}
function record(name,...) calls[name]={...} end
function self:Damage(v,k) assert(self.Object[0]==own);record('Damage',v,k) end
function self:ChangeDefence(v) assert(self.Object[0]==own);record('Shield',v) end
function self:ChangeHp(v) record('Heal',v) end
function self:ChangeMaxHp(v) record('MaxHealth',v) end
function self:ChangePower(v) record('Energy',v) end
function self:DrawCount(v) record('Draw',v) end
function self:AddBuff(id,v) record('AddBuff',id,v) end
function self:RemoveBuff(id) record('RemoveBuff',id) end
function self:ShuffleDeck() record('Shuffle') end
function self:ChangeRound() record('EndTurn') end
function self:ChooseCardToAction(n,callback,kind) record('Choose',n);callback(List({card})) end
function card:InternalThrow() record('Discard') end
function card:InternalBurning() record('Burn') end
function self:GetDeckUIToAction(n,source,callback) record('Select',n);callback(List({card})) end
function self:CreateCard(c) record('CreateCard') end
for i=1,4 do self.HandCard:Add(card);self.DeckCard:Add(card);self.UsedCard:Add(card) end
";
        var assertion=effect switch
        {
            CardEffectKind.Damage=>"assert(calls.Damage[1]=='3' and calls.Damage[2]=='Normal')",
            CardEffectKind.TrueDamage=>"assert(calls.Damage[1]=='3' and calls.Damage[2]=='True')",
            CardEffectKind.AddBuff=>"assert(calls.AddBuff[1]=='poison' and calls.AddBuff[2]=='3')",
            CardEffectKind.RemoveBuff=>"assert(calls.RemoveBuff[1]=='poison')",
            CardEffectKind.Discard or CardEffectKind.Burn=>"assert(calls.Choose[1]=='3' and calls."+effect+"~=nil)",
            CardEffectKind.SelectFromDeck or CardEffectKind.SelectFromDiscard=>"assert(calls.Select[1]=='3' and calls.CreateCard~=nil)",
            CardEffectKind.Shuffle or CardEffectKind.EndTurn=>"assert(calls."+effect+"~=nil)",
            _=>"assert(calls."+effect+"[1]=='3')"
        };
        RunCardLua(script,assertion,prefix);
    }
}
