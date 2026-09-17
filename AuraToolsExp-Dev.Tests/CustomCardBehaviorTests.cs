using System.Diagnostics;
using AuraToolsExp.Dll.Features.CustomCards;
using AuraToolsExp.Dll.Features.PixelEmoji;

internal static partial class AuraToolsTestSuite
{
    public static void TestCustomCards(bool executeLua=false)
    {
        var doc=new CustomCardDocument();
        Assert(CustomCardCompiler.Compile(doc).Success,"new card compiles");
        var invalid=doc.Copy();invalid.Rules[0].Trigger=CardRuleTrigger.Draw;
        Assert(!CustomCardCompiler.Compile(invalid).Success,"draw rule cannot read selected target");
        invalid=doc.Copy();invalid.Rules[0].Blocks[0].Value=new(){Kind=CardValueKind.Variable,VariableId="missing"};
        Assert(!CustomCardCompiler.Compile(invalid).Success,"undefined variable is rejected");
        invalid=doc.Copy();invalid.Rules[0].Blocks[0].Value=CardValue.Compare(CardValueKind.Divide,CardValue.Constant(1),CardValue.Constant(0));
        Assert(!CustomCardCompiler.Compile(invalid).Success,"literal zero division is rejected");
        invalid=doc.Copy();invalid.Rules[0].Blocks[0].Effect=CardEffectKind.Energy;
        Assert(!CustomCardCompiler.Compile(invalid).Success,"energy cannot target enemies");
        invalid=doc.Copy();invalid.Rules[0].Blocks[0].Effect=CardEffectKind.AddBuff;invalid.Rules[0].Blocks[0].ResourceId="missing";
        Assert(!CustomCardCompiler.Compile(invalid,_=>null).Success,"unloaded resource blocks crafting");
        invalid=doc.Copy();invalid.SchemaVersion=99;
        Assert(!CustomCardCompiler.Compile(invalid).Success,"future document cannot be compiled");
        var remember=new CardRuleBlock { Kind=CardBlockKind.RememberNumber,VariableName="基础伤害",Value=CardValue.Compare(CardValueKind.Multiply,CardValue.Reading(CardDataField.Shield),CardValue.Constant(2)) };
        var damage=new CardRuleBlock { Object=new(){Kind=CardObjectKind.Current},Value=new(){Kind=CardValueKind.Variable,VariableId=remember.Id} };
        var branch=new CardRuleBlock { Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Greater,CardValue.Reading(CardDataField.Health,CardObjectKind.Current),CardValue.Constant(10)),Then=new(){damage},Else=new(){new(){Object=new(){Kind=CardObjectKind.Current},Value=CardValue.Constant(1)}} };
        var each=new CardRuleBlock { Kind=CardBlockKind.ForEach,Object=new(){Kind=CardObjectKind.Enemies},Condition=new(){Kind=CardValueKind.Exists,Object=new(){Kind=CardObjectKind.Current}},Then=new(){branch} };
        doc.Rules[0].Blocks=new(){remember,each,new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(6)}};
        var compilation=CustomCardCompiler.Compile(doc);
        Assert(compilation.Success,"nested branches and variable references compile");
        var another=CustomCardCompiler.Compile(doc.Copy());
        Assert(compilation.Scripts.SequenceEqual(another.Scripts),"compilation is deterministic across serialization");
        var trial=new CardTrialInput();var result=CustomCardTrial.Run(doc,trial,CardRuleTrigger.Use);
        Assert(result.Last().Contains("护盾 14")&&result.Last().Contains("敌人生命 14"),"trial evaluates captured value before later shield change");
        Assert(trial.Self.Shield==8&&trial.Enemies[0].Health==30,"trial does not mutate supplied scenario");
        invalid=doc.Copy();invalid.Rules[0].Blocks.RemoveAt(0);
        Assert(!CustomCardCompiler.Compile(invalid).Success,"removed definition invalidates dependent nested block");
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
        RunCardLua(compilation.Scripts["UseScript"],"assert(enemy.CurHp==14 and own.Defend==14); assert(self.Object:get_Item(0)==enemy and self.status==nil); assert(self.Target==enemy)");
        var arithmetic=new CustomCardDocument();
        arithmetic.Rules[0].Blocks[0].Value=CardValue.Compare(CardValueKind.Subtract,CardValue.Constant(1),CardValue.Constant(-2));
        RunCardLua(CustomCardCompiler.Compile(arithmetic).Scripts["UseScript"],"assert(enemy.CurHp==27)");
        var repeat=new CustomCardDocument();repeat.Rules[0].Blocks=new(){new(){Kind=CardBlockKind.Repeat,Value=CardValue.Constant(3),Then=new(){new(){Value=CardValue.Constant(2)}}}};
        RunCardLua(CustomCardCompiler.Compile(repeat).Scripts["UseScript"],"assert(enemy.CurHp==24)");
        var shortCircuit=new CustomCardDocument();shortCircuit.Rules[0].Blocks=new(){new(){Kind=CardBlockKind.If,Condition=new(){Kind=CardValueKind.And,Inputs=new(){CardValue.Compare(CardValueKind.Equal,CardValue.Constant(0),CardValue.Constant(1)),CardValue.Compare(CardValueKind.Greater,CardValue.Compare(CardValueKind.Divide,CardValue.Constant(1),CardValue.Reading(CardDataField.Energy)),CardValue.Constant(0))}},Then=new(){new()}}};
        RunCardLua(CustomCardCompiler.Compile(shortCircuit).Scripts["UseScript"],"assert(enemy.CurHp==30)","CS.FightPlayer.Instance.CurPowerCount=0\n");
        var selection=new CustomCardDocument();selection.Rules[0].Blocks=new(){new(){Effect=CardEffectKind.SelectFromDeck,Object=new(),Value=CardValue.Constant(1)},new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}};
        RunCardLua(CustomCardCompiler.Compile(selection).Scripts["UseScript"],"assert(own.Defend==8); assert(pending~=nil); pending(List({card})); assert(own.Defend==11 and self.HandCard.Count==1); pending(List({card})); assert(own.Defend==11)");
        var listener=new CustomCardDocument();listener.Rules[0].Trigger=CardRuleTrigger.AfterUseHurt;listener.Rules[0].MaximumTriggers=1;listener.Rules[0].Blocks=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(4)}};
        RunCardLua(CustomCardCompiler.Compile(listener).Scripts["UseScript"],"assert(own.Defend==8); events.Hurt[1](); events.Hurt[1](); assert(own.Defend==12)");
        listener.Rules[0].MaximumTriggers=0;listener.Rules[0].MaximumTriggersPerRound=1;
        listener.Rules[0].Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.Health),CardValue.Constant(20));
        RunCardLua(CustomCardCompiler.Compile(listener).Scripts["UseScript"],"events.Hurt[1](); assert(own.Defend==8); own.CurHp=10;events.Hurt[1]();events.Hurt[1]();assert(own.Defend==12);events.StartRound[1]();events.Hurt[1]();assert(own.Defend==16)");
        var drawing=new CustomCardDocument();drawing.Rules[0].Blocks=new(){new(){Effect=CardEffectKind.Draw,Object=new(),Value=CardValue.Constant(1)},new(){Value=CardValue.Reading(CardDataField.HandCount)}};
        RunCardLua(CustomCardCompiler.Compile(drawing).Scripts["UseScript"],"assert(enemy.CurHp==30);self.HandCard:Add(card);fightui.createCardQueue:Clear();events.EndCreateCardItem[1]();assert(enemy.CurHp==29)","function self:DrawCount(n) fightui.createCardQueue:Add(card) end\n");
        // Hundreds of sibling functions must not exceed Lua's per-function local-variable limit.
        var wide=new CustomCardDocument();wide.Rules[0].Blocks=Enumerable.Range(0,100).Select(_=>new CardRuleBlock{Value=CardValue.Constant(0)}).ToList();
        RunCardLua(CustomCardCompiler.Compile(wide).Scripts["UseScript"],"assert(enemy.CurHp==30)");
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
