using System.Diagnostics;
using Newtonsoft.Json;
using AuraToolsExp.Dll.Features.CustomCards;
using AuraToolsExp.Dll.Features.PixelEmoji;

internal static partial class AuraToolsTestSuite
{
    public static void TestCustomCards(bool executeLua=false)
    {
        TestCardNodeContracts(executeLua);
        TestCardScriptRepair(executeLua);
        TestCardDescriptions();
        TestCardNumbers(executeLua);
        CustomCardDocument Program(params CardRuleBlock[] blocks)=>new(){Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=blocks.ToList()}})};
        var doc=new CustomCardDocument();
        var visualDocument=doc.Copy();visualDocument.Name="图鉴草稿";visualDocument.Cost=3;visualDocument.Rarity=2;visualDocument.Burnout=true;visualDocument.Retain=true;visualDocument.Note="风味";
        var visualCompilation=CustomCardCompiler.Compile(visualDocument);
        var originalVisual=JsonConvert.SerializeObject(visualDocument);
        var visualFields=CustomCardPresentationData.Create(visualDocument,visualCompilation,"preview");
        Assert(visualFields["Name"]=="图鉴草稿"&&visualFields["Expend"]=="3"&&visualFields["Rarity"]=="2"&&visualFields["Type"]=="攻击牌","native draft and crafting use the same display attributes");
        Assert(visualFields["Tag"]=="Burnout,Retain,"&&visualFields["Note"]=="风味"&&visualFields["Description"]==visualCompilation.Description,"native display retains traits, flavor and compiled description");
        Assert(!visualFields.Keys.Any(k=>k.EndsWith("Script")||k=="RawData"||k=="AuraToolsCustomCardArt"),"preview projection cannot execute or persist a crafted card");
        visualFields["Name"]="changed";
        Assert(JsonConvert.SerializeObject(visualDocument)==originalVisual,"native preview fields are independent of editable draft data");
        visualDocument.Graph.Nodes.Clear();
        Assert(CustomCardPresentationData.Create(visualDocument,CustomCardCompiler.Compile(visualDocument),"preview")["Description"]=="效果尚未完成","incomplete draft keeps a clear non-executable native preview");
        Assert(CustomCardArtwork.SupportsNativeCardVersion("1")&&CustomCardArtwork.SupportsNativeCardVersion("2")&&!CustomCardArtwork.SupportsNativeCardVersion("3"),"native artwork recognizes legacy and blueprint cards only");
        Assert(CustomCardCompiler.Compile(doc).Success,"new blueprint compiles");
        var invalid=doc.Copy();invalid.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Entry).Trigger=CardRuleTrigger.Draw;
        Assert(!CustomCardCompiler.Compile(invalid).Success,"draw cannot read selected target");
        invalid=Program(new CardRuleBlock{Value=new(){Kind=CardValueKind.Variable,VariableId="missing"}});
        Assert(!CustomCardCompiler.Compile(invalid).Success,"missing capture rejected");
        invalid=Program(new CardRuleBlock{Value=CardValue.Compare(CardValueKind.Divide,CardValue.Constant(1),CardValue.Constant(0))});
        Assert(!CustomCardCompiler.Compile(invalid).Success,"zero divisor rejected");
        invalid=Program(new CardRuleBlock{Effect=CardEffectKind.Energy});
        Assert(!CustomCardCompiler.Compile(invalid).Success,"energy cannot target enemies");
        invalid=Program(new CardRuleBlock{Effect=CardEffectKind.AddBuff,ResourceId="missing"});
        Assert(!CustomCardCompiler.Compile(invalid,_=>null).Success,"missing resource prevents crafting");
        for(int i=0;i<CardBlueprintTemplates.Names.Length;i++)
        {
            var template=CardBlueprintTemplates.Create(i);
            if(i==6)template.Graph.Nodes.First(n=>n.Field==CardDataField.BuffStacks).ResourceId="sample";
            var compiled=CustomCardCompiler.Compile(template);
            Assert(compiled.Success,"template compiles: "+i+" "+string.Join(";",compiled.Issues));
            Assert(!compiled.Scripts.Values.Any(s=>s.Contains("__listen")||s.Contains("AddEvent('Hurt'")),"new cards contain no persistent listener generation");
        }
        var half=CardBlueprintTemplates.Create(1);
        var remember=new CardRuleBlock{Kind=CardBlockKind.RememberNumber,VariableName="原护盾",Value=CardValue.Reading(CardDataField.Shield)};
        doc=Program(remember,new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(6)},new(){Value=new(){Kind=CardValueKind.Variable,VariableId=remember.Id}});
        var compiledRemember=CustomCardCompiler.Compile(doc);
        Assert(compiledRemember.Success,"capture graph compiles");
        var copied=CardBlueprintMigration.Read(JsonConvert.SerializeObject(doc));
        copied.Graph.Nodes.ForEach(n=>n.X+=123);
        Assert(compiledRemember.Scripts.SequenceEqual(CustomCardCompiler.Compile(copied).Scripts),"layout and serialization do not alter emitted program");
        invalid=doc.Copy();invalid.Graph.Delete(new[]{remember.Id});
        Assert(!CustomCardCompiler.Compile(invalid).Success,"removing a capture invalidates its consumers");
        // Connection checks are the same semantic validator used by production compilation.
        var branch=half.Graph.Nodes.First(n=>n.Kind==CardNodeKind.If&&n.Label!="触发条件");
        var action=half.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);
        var badEdge=new CardGraphEdge{From=action.Id,Output="next",To=branch.Id,Input="condition"};
        var before=JsonConvert.SerializeObject(half.Graph);
        Assert(!CardBlueprintCompiler.Connect(half.Graph,badEdge,true,out _)&&JsonConvert.SerializeObject(half.Graph)==before,"rejected reconnection preserves original graph");
        var selfCycle=new CardGraphEdge{From=action.Id,Output="next",To=action.Id,Input="in"};
        Assert(CardBlueprintCompiler.ConnectionError(half.Graph,selfCycle)!=null,"execution self-cycle rejected");
        var incomplete=new CustomCardDocument();var control=incomplete.Graph.Add(CardNodeKind.If,300,0);
        var entry=incomplete.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Entry);
        Assert(CardBlueprintCompiler.Connect(incomplete.Graph,new(){From=entry.Id,Output="next",To=control.Id,Input="in"},true,out _),"well-typed execution connection accepts an unfinished condition");
        Assert(!CustomCardCompiler.Compile(incomplete).Success,"unfinished conditions still prevent crafting");
        var insertDoc=new CustomCardDocument();var originalEdge=insertDoc.Graph.Edges[0];var shieldNode=new CardGraphNode{Kind=CardNodeKind.Effect,Effect=CardEffectKind.Shield,Number=3};
        Assert(CardBlueprintCompiler.Insert(insertDoc.Graph,originalEdge.Id,shieldNode,true,out _),"compatible node inserts atomically into execution edge");
        var saved=new CardRuleBlock{Kind=CardBlockKind.RememberNumber,VariableName="内部分支",Value=CardValue.Constant(3)};
        invalid=Program(new CardRuleBlock{Kind=CardBlockKind.If,Then=new(){saved}},new(){Value=new(){Kind=CardValueKind.Variable,VariableId=saved.Id}});
        Assert(!CustomCardCompiler.Compile(invalid).Success,"branch capture cannot leak to continuation");
        var randomCapture=new CardRuleBlock{Kind=CardBlockKind.RememberObject,Object=new(){Kind=CardObjectKind.RandomEnemy},VariableName="随机目标"};
        var randomDoc=Program(randomCapture,new(){Object=new(){Kind=CardObjectKind.Saved,VariableId=randomCapture.Id}},new(){Object=new(){Kind=CardObjectKind.Saved,VariableId=randomCapture.Id}});
        var select=CardBlueprintTemplates.Create(8);
        var energyRead=Program(new CardRuleBlock{Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Reading(CardDataField.MaxEnergy)});
        var dynamicDivide=Program(new CardRuleBlock{Value=CardValue.Compare(CardValueKind.Divide,CardValue.Constant(5),CardValue.Reading(CardDataField.Energy))});
        var legacy=new CustomCardDocument{SchemaVersion=1,Rules=new(){new(){Blocks=new(){new()}}}};
        var migrated=CardBlueprintMigration.Read(JsonConvert.SerializeObject(legacy));
        Assert(migrated.SchemaVersion==CustomCardDocument.CurrentVersion&&migrated.Rules==null&&migrated.LegacyBackup.Length>0&&CustomCardCompiler.Compile(migrated).Success,"v1 direct draft migrates one-way with original retained");
        legacy.Rules![0].Trigger=CardRuleTrigger.AfterUseHurt;
        migrated=CardBlueprintMigration.Read(JsonConvert.SerializeObject(legacy));
        Assert(migrated.Graph.Nodes.Any(n=>n.Kind==CardNodeKind.Unsupported)&&!CustomCardCompiler.Compile(migrated).Success,"legacy listeners are retained for repair, never silently converted");
        legacy.Rules=new(){new(){Blocks=new(){new(){Effect=CardEffectKind.SelectFromDeck,Object=new()}}},new(){Blocks=new(){new()}}};
        migrated=CardBlueprintMigration.Read(JsonConvert.SerializeObject(legacy));
        Assert(migrated.MigrationReview.Count>0&&!CustomCardCompiler.Compile(migrated).Success,"asynchronous multi-rule migration requires explicit review");
        var fragment=doc.Graph.Fragment(doc.Graph.Nodes.Select(n=>n.Id));var paste=new CardBlueprint();paste.Paste(fragment);paste.Paste(fragment);
        Assert(paste.Nodes.Select(n=>n.Id).Distinct().Count()==paste.Nodes.Count&&paste.Edges.All(e=>paste.Nodes.Any(n=>n.Id==e.From)&&paste.Nodes.Any(n=>n.Id==e.To)),"paste remaps identities and edges");
        invalid=new();invalid.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect).Number=-1;
        var draft=CardBlueprintMigration.Read(JsonConvert.SerializeObject(invalid));
        Assert(!CustomCardCompiler.Compile(draft).Success,"semantically invalid drafts remain importable");
        var filtered=Program(new CardRuleBlock{Kind=CardBlockKind.ForEach,Object=new(){Kind=CardObjectKind.Enemies},Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.Health,CardObjectKind.Current),CardValue.Constant(20)),Then=new(){new(){Object=new(){Kind=CardObjectKind.Current}}}});
        invalid=new();invalid.Graph.PanX=float.NaN;bool rejected=false;
        try{CardBlueprintMigration.Read(JsonConvert.SerializeObject(invalid));}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"unsafe imported canvas coordinates rejected before Unity rendering");
        var emptyLoops=Program(new CardRuleBlock{Kind=CardBlockKind.Repeat,Value=CardValue.Constant(64),Then=new(){new(){Kind=CardBlockKind.Repeat,Value=CardValue.Constant(64)}}},new(){Value=CardValue.Constant(2)});
        foreach(int size in CardPixelCanvas.Sizes)
        {
            foreach(int template in Enumerable.Range(0,CardPixelCanvas.Templates.Length))
            {
                var raster=CardPixelCanvas.Template(size,template);
                Assert(CardPixelCanvas.IsValid(size,Convert.ToBase64String(raster)),"pixel template preserves palette and dimensions");
                if(template>0)Assert(raster.Any(x=>x!=0),"pixel pattern is nonempty");
            }
            var art=new CustomCardArtwork { Size=size,Pixels=Convert.ToBase64String(CardPixelCanvas.Template(size,2)) };
            var png=CardPixelCanvas.Png(art);
            Assert(png.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})&&ReadBigEndian(png,16)==256&&ReadBigEndian(png,20)==256,"native pixel PNG uses a portable 256-square image");
            using var compressed=new MemoryStream();
            for(int offset=8;offset<png.Length;)
            {
                int length=ReadBigEndian(png,offset);var kind=System.Text.Encoding.ASCII.GetString(png,offset+4,4);
                if(kind=="IDAT")compressed.Write(png,offset+8,length);offset+=12+length;
            }
            compressed.Position=0;using var zlib=new System.IO.Compression.ZLibStream(compressed,System.IO.Compression.CompressionMode.Decompress);using var raw=new MemoryStream();zlib.CopyTo(raw);
            Assert(raw.Length==256*(256*4+1),"PNG compressed scanlines decode with standard zlib");
        }
        var pixels=PixelEmojiCodec.Blank();PixelEmojiCodec.DrawLine(pixels,-2,-2,3,3,2);
        Assert(pixels[0]==2&&pixels[3*24+3]==2,"shared raster clips lines at the existing emoji boundary");
        Assert(IndexedPixelCanvas.Fill(pixels,24,23,0,4),"shared raster fills connected region");
        Assert(IndexedPixelCanvas.Resize(pixels,24,64).Length==4096,"canvas resize preserves requested dimensions");
        if(!executeLua)return;
        RunCardLua(CustomCardCompiler.Compile(insertDoc).Scripts["UseScript"],"assert(own.Defend==11 and enemy.CurHp==24)");
        RunCardLua(CustomCardCompiler.Compile(randomDoc).Scripts["UseScript"],"assert(enemy.CurHp==18 and rolls==1)");
        RunCardLua(CustomCardCompiler.Compile(energyRead).Scripts["UseScript"],"assert(own.Defend==15)","CS.FightPlayer.Instance.MaxPowerCount=7\n");
        RunCardLua("local ok,err=pcall(assert(load("+JsonConvert.SerializeObject(CustomCardCompiler.Compile(dynamicDivide).Scripts["UseScript"])+")));assert(not ok and string.find(err,'除数为零'))","assert(enemy.CurHp==30)","CS.FightPlayer.Instance.CurPowerCount=0\n");
        RunCardLua(CustomCardCompiler.Compile(filtered).Scripts["UseScript"],"assert(enemy.CurHp==4)","enemy.CurHp=10\n");
        RunCardLua(CustomCardCompiler.Compile(filtered).Scripts["UseScript"],"assert(enemy.CurHp==30)");
        RunCardLua(compiledRemember.Scripts["UseScript"],"assert(enemy.CurHp==22 and own.Defend==14)");
        foreach(var hp in new[]{49,50,51})RunCardLua(CustomCardCompiler.Compile(half).Scripts["UseScript"],"assert(enemy.CurHp=="+(hp<50?18:24)+")","own.CurHp="+hp+";own.MaxHp=100\n");
        RunCardLua(CustomCardCompiler.Compile(CardBlueprintTemplates.Create(4)).Scripts["UseScript"],"assert(enemy.CurHp==24)");
        RunCardLua(CustomCardCompiler.Compile(select).Scripts["UseScript"],"assert(own.Defend==8);pending(List({card}));pending(List({card}));assert(own.Defend==11 and self.HandCard.Count==1)");
        RunCardLua(CustomCardCompiler.Compile(select).Scripts["UseScript"],"pending(List());assert(own.Defend==8)");
        var drawing=Program(new CardRuleBlock{Effect=CardEffectKind.Draw,Object=new(),Value=CardValue.Constant(1)},new(){Value=CardValue.Reading(CardDataField.HandCount)});
        RunCardLua(CustomCardCompiler.Compile(drawing).Scripts["UseScript"],"assert(enemy.CurHp==30);self.HandCard:Add(card);fightui.createCardQueue:Clear();local resume=events.EndCreateCardItem[1];resume();resume();assert(enemy.CurHp==29);assert(next(CS.EventCenter.Instance.listeners)==nil)","function self:DrawCount(n) fightui.createCardQueue:Add(card) end\n");
        var shortCircuit=Program(new CardRuleBlock{Kind=CardBlockKind.If,Condition=new(){Kind=CardValueKind.And,Inputs=new(){CardValue.Compare(CardValueKind.Equal,CardValue.Constant(0),CardValue.Constant(1)),CardValue.Compare(CardValueKind.Greater,CardValue.Compare(CardValueKind.Divide,CardValue.Constant(1),CardValue.Reading(CardDataField.Energy)),CardValue.Constant(0))}},Then=new(){new()}});
        RunCardLua(CustomCardCompiler.Compile(shortCircuit).Scripts["UseScript"],"assert(enemy.CurHp==30)","CS.FightPlayer.Instance.CurPowerCount=0\n");
        var wide=Program(Enumerable.Range(0,100).Select(_=>new CardRuleBlock{Value=CardValue.Constant(0)}).ToArray());
        RunCardLua(CustomCardCompiler.Compile(wide).Scripts["UseScript"],"assert(enemy.CurHp==30)");
        var recursive=CustomCardCompiler.Compile(new CustomCardDocument()).Scripts["UseScript"];
        RunCardLua("local program=assert(load("+JsonConvert.SerializeObject(CustomCardCompiler.Compile(emptyLoops).Scripts["UseScript"])+"))\nlocal ok,err=pcall(program)\nassert(not ok and string.find(err,'4096'))","assert(enemy.CurHp==30)");
        RunCardLua("local blueprint=assert(load("+JsonConvert.SerializeObject(recursive)+"))\nhits=0\nfunction self:Damage(a,b) hits=hits+1;blueprint() end\nlocal ok,err=pcall(blueprint)\nassert(not ok and hits<=64 and string.find(err,'连锁'))","assert(_G.__AuraCustomCardV2.current==nil)");
        var drawChain=new CustomCardDocument{Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Trigger=CardRuleTrigger.Draw,Blocks=new(){new(){Effect=CardEffectKind.Draw,Object=new(),Value=CardValue.Constant(1)}}}})};
        var drawSource=CustomCardCompiler.Compile(drawChain).Scripts["DrawScript"];
        RunCardLua("local blueprint=assert(load("+JsonConvert.SerializeObject(drawSource)+"))\nfunction self:DrawCount(n) fightui.createCardQueue:Add(card) end\nlocal ok,err=pcall(function() for i=1,129 do blueprint() end end)\nassert(not ok and string.find(err,'连锁'))\nfightui.createCardQueue:Clear()\nlocal callbacks={}\nfor _,cb in ipairs(events.EndCreateCardItem) do callbacks[#callbacks+1]=cb end\nfor _,cb in ipairs(callbacks) do cb() end","assert(next(CS.EventCenter.Instance.listeners)==nil and _G.__AuraCustomCardV2.pending==nil)");
        var discard=Program(new CardRuleBlock{Effect=CardEffectKind.Discard,Object=new(),Value=CardValue.Constant(1)},new(){Value=CardValue.Constant(2)});
        RunCardLua(CustomCardCompiler.Compile(discard).Scripts["UseScript"],"assert(self.Object[0]==enemy and self.status==nil);pending(List());assert(enemy.CurHp==30)","self.HandCard:Add(card)\nfunction self:ChooseCardToAction(n,callback,t) self:SetStatus('Self');self.status=own;pending=callback end\n");
    }
    private static int ReadBigEndian(byte[] bytes,int start)=>(bytes[start]<<24)|(bytes[start+1]<<16)|(bytes[start+2]<<8)|bytes[start+3];
    private static void RunCardLua(string source,string assertion,string prefix="")
    {
        var folder=new DirectoryInfo(AppContext.BaseDirectory);
        while(folder!=null&&!File.Exists(Path.Combine(folder.FullName,"AuraToolsExp-Dev.Tests","Fixtures","CustomCards","native-harness.lua")))folder=folder.Parent;
        if(folder==null)throw new InvalidOperationException("Custom card Lua harness missing.");
        var harness=File.ReadAllText(Path.Combine(folder.FullName,"AuraToolsExp-Dev.Tests","Fixtures","CustomCards","native-harness.lua"));
        var path=Path.Combine(Path.GetTempPath(),"aura-card-"+Guid.NewGuid().ToString("N")+".lua");
        try
        {
            File.WriteAllText(path,harness+"\n"+prefix+"\n"+source+"\n"+assertion);
            var start=new ProcessStartInfo("lua") { RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true };start.ArgumentList.Add(path);
            using var process=Process.Start(start)!;var error=process.StandardError.ReadToEnd();var output=process.StandardOutput.ReadToEnd();
            if(!process.WaitForExit(15000)){process.Kill(true);throw new Exception("Lua execution timed out.");}
            Assert(process.ExitCode==0,"generated Lua executes expected semantics: "+error+output);
        }
        finally{File.Delete(path);}
    }
}
