using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.CustomCards;

public sealed class CardTrialActor
{
    public string Name { get; set; } = "敌人";
    public double Health { get; set; } = 30;
    public double MaximumHealth { get; set; } = 30;
    public double Shield { get; set; }
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public Dictionary<string,double> Buffs { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CardTrialInput
{
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public CardTrialActor Self { get; set; } = new() { Name="自己",Health=25,MaximumHealth=50,Shield=8 };
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public List<CardTrialActor> Enemies { get; set; } = new() { new() };
    public int Energy { get; set; } = 3;
    public int HandCount { get; set; } = 4;
    public int DeckCount { get; set; } = 10;
    public int DiscardCount { get; set; } = 2;
    public int Seed { get; set; } = 1;
}

/// <summary>Isolated base-value trial. It never queries or modifies a live game.</summary>
public static class CustomCardTrial
{
    private sealed class Scope
    {
        internal CardTrialActor? Current;
        internal readonly Dictionary<string,object?> Values=new();
        internal Scope Child() { var s=new Scope { Current=Current }; foreach(var p in Values)s.Values[p.Key]=p.Value; return s; }
    }
    public static IReadOnlyList<string> Run(CustomCardDocument doc,CardTrialInput input,CardRuleTrigger trigger)
    {
        var output=new List<string>();
        var compile=CustomCardCompiler.Compile(doc);
        if(!compile.Success) return compile.Issues.Select(i=>i.Message).ToArray();
        // The caller's scenario stays unchanged after the trial.
        var state=Newtonsoft.Json.JsonConvert.DeserializeObject<CardTrialInput>(Newtonsoft.Json.JsonConvert.SerializeObject(input))!;
        var random=new Random(state.Seed);
        int steps=0;
        IEnumerable<CardTrialActor> Objects(CardObjectReference o,Scope s)
        {
            IEnumerable<CardTrialActor?> result=o.Kind switch
            {
                CardObjectKind.Self=>new[]{state.Self},CardObjectKind.Target=>state.Enemies.Take(1),CardObjectKind.Current=>(IEnumerable<CardTrialActor?>)new[]{s.Current},
                CardObjectKind.Saved=>(IEnumerable<CardTrialActor?>)new[]{s.Values.TryGetValue(o.VariableId,out var x) ? x as CardTrialActor : null},
                CardObjectKind.Enemies=>state.Enemies,CardObjectKind.Friends=>new[]{state.Self},CardObjectKind.All=>new[]{state.Self}.Concat(state.Enemies),
                CardObjectKind.RandomEnemy=>state.Enemies.Where(x=>x.Health>0).OrderBy(_=>random.Next()).Take(1),CardObjectKind.RandomFriend=>new[]{state.Self},_=>Array.Empty<CardTrialActor>()
            };
            return result.Where(x=>x!=null && x.Health>0).Cast<CardTrialActor>().ToArray();
        }
        double Number(CardValue v,Scope s)
        {
            double N(int i)=>Number(v.Inputs[i],s);
            double result;
            if(v.Kind==CardValueKind.Number) result=v.Number;
            else if(v.Kind==CardValueKind.Variable) result=Convert.ToDouble(s.Values[v.VariableId]);
            else if(v.Kind==CardValueKind.Read)
            {
                var actor=Objects(v.Object,s).FirstOrDefault() ?? throw new InvalidOperationException("读取的对象已不存在。");
                result=v.Field switch { CardDataField.Health=>actor.Health,CardDataField.MaxHealth=>actor.MaximumHealth,CardDataField.MissingHealth=>Math.Max(0,actor.MaximumHealth-actor.Health),CardDataField.HealthPercent=>actor.MaximumHealth<=0 ? 0 : 100*actor.Health/actor.MaximumHealth,CardDataField.Shield=>actor.Shield,CardDataField.BuffStacks=>actor.Buffs.TryGetValue(v.ResourceId,out var b) ? b : 0,CardDataField.Energy=>state.Energy,CardDataField.MaxEnergy=>3,CardDataField.HandCount=>state.HandCount,CardDataField.DeckCount=>state.DeckCount,CardDataField.DiscardCount=>state.DiscardCount,CardDataField.CardCost=>doc.Cost,_=>0 };
            }
            else result=v.Kind switch { CardValueKind.Add=>N(0)+N(1),CardValueKind.Subtract=>N(0)-N(1),CardValueKind.Multiply=>N(0)*N(1),CardValueKind.Divide=>N(1)==0 ? throw new InvalidOperationException("除数为零。") : N(0)/N(1),CardValueKind.Minimum=>Math.Min(N(0),N(1)),CardValueKind.Maximum=>Math.Max(N(0),N(1)),CardValueKind.Floor=>Math.Floor(N(0)),CardValueKind.Ceiling=>Math.Ceiling(N(0)),CardValueKind.Clamp=>N(1)>N(2) ? throw new InvalidOperationException("范围上下限颠倒。") : Math.Max(N(1),Math.Min(N(0),N(2))),_=>throw new InvalidOperationException("需要数值。") };
            if(double.IsNaN(result)||double.IsInfinity(result)||Math.Abs(result)>1000000) throw new InvalidOperationException("数值无效或超出范围。");
            return result;
        }
        bool Condition(CardValue v,Scope s)
        {
            double N(int i)=>Number(v.Inputs[i],s);
            return v.Kind switch { CardValueKind.Exists=>Objects(v.Object,s).Any(),CardValueKind.And=>Condition(v.Inputs[0],s)&&Condition(v.Inputs[1],s),CardValueKind.Or=>Condition(v.Inputs[0],s)||Condition(v.Inputs[1],s),CardValueKind.Not=>!Condition(v.Inputs[0],s),CardValueKind.Equal=>N(0)==N(1),CardValueKind.NotEqual=>N(0)!=N(1),CardValueKind.Greater=>N(0)>N(1),CardValueKind.AtLeast=>N(0)>=N(1),CardValueKind.Less=>N(0)<N(1),CardValueKind.AtMost=>N(0)<=N(1),_=>throw new InvalidOperationException("需要条件。") };
        }
        void RunBlocks(List<CardRuleBlock> blocks,Scope s)
        {
            foreach(var b in blocks)
            {
                if(++steps>CustomCardCompiler.RuntimeSteps) throw new InvalidOperationException("超过执行步骤上限。");
                switch(b.Kind)
                {
                    case CardBlockKind.If:
                        bool yes=Condition(b.Condition,s); output.Add("条件"+(yes ? "成立" : "不成立")+"："+CustomCardDescription.Value(b.Condition)); RunBlocks(yes ? b.Then : b.Else,s.Child()); break;
                    case CardBlockKind.Repeat:
                        var count=Number(b.Value,s); if(count!=Math.Floor(count)||count<0||count>64)throw new InvalidOperationException("重复次数需为 0～64 整数。");
                        for(int i=0;i<count;i++)RunBlocks(b.Then,s.Child()); break;
                    case CardBlockKind.ForEach:
                        foreach(var actor in Objects(b.Object,s)) { var child=s.Child();child.Current=actor;if(Condition(b.Condition,child))RunBlocks(b.Then,child); } break;
                    case CardBlockKind.RememberNumber:
                        s.Values[b.Id]=Number(b.Value,s);output.Add("记住 "+b.VariableName+"＝"+s.Values[b.Id]);break;
                    case CardBlockKind.RememberObject:
                        s.Values[b.Id]=Objects(b.Object,s).FirstOrDefault();break;
                    case CardBlockKind.Effect:
                        var objects=Objects(b.Object,s).ToArray(); if(objects.Length==0){output.Add("对象不存在，跳过效果。");break;}
                        var amount=CustomCardNames.HasAmount(b.Effect) ? Number(b.Value,s) : 0;
                        if(amount<0)throw new InvalidOperationException("效果数量不能为负数。");amount=Math.Floor(amount);
                        foreach(var actor in objects)
                        {
                            output.Add(actor.Name+"："+CustomCardNames.Name(b.Effect,CustomCardNames.Effects)+" "+amount);
                            switch(b.Effect)
                            {
                                case CardEffectKind.Damage: var shield=Math.Min(actor.Shield,amount);actor.Shield-=shield;actor.Health=Math.Max(0,actor.Health-amount+shield);break;
                                case CardEffectKind.TrueDamage: actor.Health=Math.Max(0,actor.Health-amount);break;
                                case CardEffectKind.Shield: actor.Shield+=amount;break;
                                case CardEffectKind.Heal: actor.Health=Math.Min(actor.MaximumHealth,actor.Health+amount);break;
                                case CardEffectKind.MaxHealth: actor.MaximumHealth+=amount;break;
                                case CardEffectKind.Energy: state.Energy+=(int)amount;break;
                                case CardEffectKind.AddBuff: actor.Buffs[b.ResourceId]=(actor.Buffs.TryGetValue(b.ResourceId,out var old) ? old : 0)+amount;break;
                                case CardEffectKind.RemoveBuff: actor.Buffs.Remove(b.ResourceId);break;
                                case CardEffectKind.Draw: var drawn=Math.Min(state.DeckCount,(int)amount);state.DeckCount-=drawn;state.HandCount+=drawn;break;
                                case CardEffectKind.Discard: var discarded=Math.Min(state.HandCount,(int)amount);state.HandCount-=discarded;state.DiscardCount+=discarded;break;
                                case CardEffectKind.Burn: state.HandCount=Math.Max(0,state.HandCount-(int)amount);break;
                                case CardEffectKind.Shuffle: state.DeckCount+=state.DiscardCount;state.DiscardCount=0;break;
                                case CardEffectKind.SelectFromDeck: case CardEffectKind.SelectFromDiscard: output.Add("需要选牌：试算在此停止，请在游戏中验证选择结果。");throw new TrialSelectionStop();
                                case CardEffectKind.EndTurn: throw new TrialSelectionStop();
                            }
                        }
                        break;
                }
            }
        }
        output.Add("基础数值试算：不模拟状态修正、原生抽牌动画队列或联机结算。");
        try { foreach(var r in doc.Rules.Where(r=>r.Trigger==trigger)) { var scope=new Scope();if(Condition(r.Condition,scope))RunBlocks(r.Blocks,scope);else output.Add("本组触发条件不成立，跳过。"); } }
        catch(TrialSelectionStop) { }
        catch(Exception ex) { output.Add("停止："+ex.Message); }
        output.Add("结束：自己生命 "+state.Self.Health+"，护盾 "+state.Self.Shield+"；"+string.Join("，",state.Enemies.Select(e=>e.Name+"生命 "+e.Health)));
        return output;
    }
    private sealed class TrialSelectionStop : Exception { }
}
