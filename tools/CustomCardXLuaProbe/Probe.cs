using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AuraToolsExp.Dll.Features.CustomCards;
using Newtonsoft.Json;
using UnityEngine;

public sealed class CardProbeActor
{
    public double CurHp=30,MaxHp=50,Defend;
    public string state="Default",InstanceId="enemy";
}
public sealed class CardProbeCard { public int Throws,Burns; public void InternalThrow()=>Throws++; public void InternalBurning()=>Burns++; }
public static class CustomCardXLuaProbe
{
    public static void Run()
    {
        string Arg(string prefix)=>Environment.GetCommandLineArgs().First(a=>a.StartsWith(prefix)).Substring(prefix.Length);
        var managed=Arg("-cardManaged=");var output=Arg("-cardProbeOutput=");
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{var path=Path.Combine(managed,new AssemblyName(args.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
        var witch=Assembly.LoadFrom(Path.Combine(managed,"Witch.dll"));
        RuntimeHelpers.RunClassConstructor(witch.GetType("XLua.CSObjectWrap.XLua_Gen_Initer_Register__",true).TypeHandle);
        var cases=new List<string>();
        void RunCase(CustomCardDocument document,string check,string setup="",bool legacy=false,bool repair=false)
        {
            dynamic env=Activator.CreateInstance(witch.GetType("XLua.LuaEnv",true));
            using((IDisposable)env)
            {
                var self=new CardProbeActor{CurHp=25,Defend=8,InstanceId="self"};var enemy=new CardProbeActor();var card=new CardProbeCard();
                var objects=new List<CardProbeActor>{enemy};var deck=new List<CardProbeCard>{card};var hand=new List<CardProbeCard>();var discard=new List<CardProbeCard>();
                env.Global.Set("actor",self);env.Global.Set("enemy",enemy);env.Global.Set("objects",objects);env.Global.Set("deck",deck);env.Global.Set("hand",hand);env.Global.Set("discard",discard);
                env.Global.Set("prototype",new List<CardProbeCard>());env.Global.Set("selected",new List<CardProbeCard>{card});env.Global.Set("card",card);
                env.Global.Set("data",new Dictionary<string,string>{{"Expend",document.Cost.ToString()}});env.Global.Set("vars",new Dictionary<string,string>());
                env.DoString(Bootstrap,"CardProbe.Bootstrap");env.DoString(setup,"CardProbe.Setup");
                var compilation=CustomCardCompiler.Compile(document);
                if(!compilation.Success)throw new Exception(string.Join(";",compilation.Issues));
                env.DoString(compilation.Scripts["InitScript"],"CardProbe.Init");
                var source=compilation.Scripts["UseScript"];
                if(legacy)source=source.Replace("compiler 5","compiler 3").Replace("t[#t+1] = xs[i]","t[#t+1] = xs:get_Item(i)");
                if(repair)
                {
                    var payload=new Dictionary<string,string>{{"Id","AuraToolsExp_custom_native_probe"},{"AuraToolsCustomCardVersion","2"},{"UseScript",source}};
                    if(!CardBlueprintScriptCompatibility.TryRepair(payload,out var repaired,out _))throw new Exception("Persisted script repair was not applied.");source=repaired["UseScript"];
                }
                env.Global.Set("program",source);
                env.DoString("local ok,err=pcall(assert(load(program))); probeOk=ok;probeError=tostring(err)","CardProbe.Execute");
                env.DoString(check,"CardProbe.Assert");
                env.Global.Set<string,object>("pending",null);
                env.DoString("collectgarbage('collect')");env.Tick();
                cases.Add(document.Name+(repair?" migrated legacy":legacy?" legacy reproduction":""));
            }
        }
        var basic=new CustomCardDocument{Name="actual XLua list failure boundary"};
        RunCase(basic,"assert(not probeOk and string.find(probeError,'get_Item'))",legacy:true);
        RunCase(basic,"assert(probeOk,probeError);assert(enemy.CurHp==24 and actor.CurHp==25);assert(objects[0]==enemy and objects.get_Item==nil);assert(vars:get_Item('BaseScript')=='AttackCardItem')");
        RunCase(basic,"assert(probeOk,probeError);assert(enemy.CurHp==24 and objects[0]==enemy)",legacy:true,repair:true);
        RunCase(CardBlueprintTemplates.Create(10),"assert(probeOk,probeError);assert(enemy.CurHp==24 and rolls==1 and objects[0]==enemy)");
        RunCase(CardBlueprintTemplates.Create(10),"assert(probeOk,probeError);assert(actor.Defend==11 and rolls==0)","enemy.state='Dead'");
        RunCase(CardBlueprintTemplates.Create(8),"assert(probeOk,probeError);assert(actor.Defend==8);pending(selected);assert(actor.Defend==11 and hand.Count==1 and deck.Count==0);pending(selected);assert(hand.Count==1)");
        foreach(var effect in new[]{CardEffectKind.Discard,CardEffectKind.Burn})
        {
            var d=new CustomCardDocument{Name=effect.ToString(),Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){new(){Effect=effect,Object=new(),Value=CardValue.Constant(1)},new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}}})};
            RunCase(d,"assert(probeOk,probeError);pending(selected);assert(actor.Defend==11 and card."+(effect==CardEffectKind.Discard?"Throws":"Burns")+"==1);assert(objects[0]==enemy)","hand:Add(card)");
        }
        var cost=new CustomCardDocument{Name="native string dictionary binding",Cost=3,Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Reading(CardDataField.CardCost)}}}})};
        RunCase(cost,"assert(probeOk,probeError);assert(actor.Defend==11)");
        var condition=new CardRuleBlock{Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.HealthPercent),CardValue.Ratio(.5)),Then=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}};
        var percentage=new CustomCardDocument{Name="native percentage comparison",Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){condition}}})};
        foreach(var hp in new[]{49.999,50,50.001})RunCase(percentage,"assert(probeOk,probeError);assert(actor.Defend=="+(hp<50?11:8)+")","actor.CurHp="+hp.ToString(System.Globalization.CultureInfo.InvariantCulture)+";actor.MaxHp=100");
        var scaling=new CustomCardDocument{Name="native percentage multiplier",Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Compare(CardValueKind.Multiply,CardValue.Constant(100),CardValue.Ratio(.5))}}}})};
        RunCase(scaling,"assert(probeOk,probeError);assert(actor.Defend==58)");
        File.WriteAllText(output,JsonConvert.SerializeObject(new{success=true,cases,engine=Application.unityVersion,bindingAssembly=witch.Location,scope="actual game XLua and CLR collections; battle services are isolated"},Formatting.Indented));
        Debug.Log("Custom card native XLua probe passed: "+cases.Count);
    }
    private const string Bootstrap=@"
local nativeCS=CS
CS={System=nativeCS.System,ScriptExecutor={PlayerInfo={CardList=prototype}},FightManager={Instance={fightType='Player'}},FightPlayer={Instance={Status=actor,CurPowerCount=3,MaxPowerCount=3}},FightCardManager={Instance={cardList=deck,usedCardList=discard}}}
self={Self=actor,Target=enemy,Object=objects,HandCard=hand,DeckCard=deck,UsedCard=discard,dataConfig={data=data},Vars=vars,status=nil}
rolls=0
self.DefaultDice={WithRange=function(_,a,b) return {Roll=function() rolls=rolls+1;return {Value=0} end} end}
function self:SetStatus(filter) self.Object:Clear();if filter=='AllFriends' or filter=='Self' then self.Object:Add(actor) else self.Object:Add(enemy) end;return self.Object end
function self:Damage(v,kind) for i=0,self.Object.Count-1 do local o=self.Object[i];o.CurHp=o.CurHp-tonumber(v) end end
function self:ChangeDefence(v) for i=0,self.Object.Count-1 do local o=self.Object[i];o.Defend=o.Defend+tonumber(v) end end
function self:GetDeckUIToAction(n,source,callback) assert(source:GetType()==prototype:GetType());pending=callback end
function self:ChooseCardToAction(n,callback,t) self:SetStatus('Self');self.status=actor;pending=callback end
function self:CreateCard(c) self.HandCard:Add(c) end
local ui={createCardQueue={Count=0},GetType=function() return {FullName='Witch.UI.Window.FightUI'} end}
CS.Witch={UI={UIManager={Instance={GetAllUI=function() return {GetEnumerator=function() return {Current=ui,MoveNext=function() return true end,Dispose=function() end} end} end}}}}
";
}
