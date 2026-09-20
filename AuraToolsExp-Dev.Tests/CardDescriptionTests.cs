using AuraToolsExp.Dll.Features.CustomCards;

internal static partial class AuraToolsTestSuite
{
    private static void TestCardDescriptions()
    {
        var rules=new[]
        {
            new CustomCardRule{Trigger=CardRuleTrigger.Draw,Blocks=new(){new(){Effect=CardEffectKind.MaxHealth,Object=new(){Kind=CardObjectKind.Self},Value=CardValue.Constant(1000)},new(){Kind=CardBlockKind.Stop}}},
            new CustomCardRule{Trigger=CardRuleTrigger.Use,Blocks=new(){new(){Effect=CardEffectKind.Damage,Object=new(){Kind=CardObjectKind.Target},Value=CardValue.Constant(100)},new(){Kind=CardBlockKind.Stop}}},
            new CustomCardRule{Trigger=CardRuleTrigger.Discard,Blocks=new(){new(){Effect=CardEffectKind.Draw,Object=new(){Kind=CardObjectKind.Self},Value=CardValue.Constant(6)},new(){Kind=CardBlockKind.Stop}}}
        };
        var expected="抽到时：自身生命上限增加 1000。\n使用时：对选中目标造成 100 点普通伤害。\n丢弃时：抽 6 张牌。";
        Assert(CustomCardDescription.DescribeRules(rules)==expected,"linear effects describe outcomes without terminal control instructions or duplicated subjects");
        var conditional=new CustomCardRule{Blocks=new(){new(){Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.Health),CardValue.Constant(10)),Then=new(){new(){Kind=CardBlockKind.Stop}}},new(){Effect=CardEffectKind.Draw,Object=new(){Kind=CardObjectKind.Self},Value=CardValue.Constant(2)}}};
        var text=CustomCardDescription.DescribeRules(new[]{conditional});
        Assert(text.Contains("不再执行此次触发的后续效果。")&&text.Contains("抽 2 张牌。"),"branch early termination remains visible before a successor effect");
        var legacy=new Dictionary<string,string>
        {
            ["Id"]="AuraToolsExp_custom_saved",["AuraToolsCustomCardVersion"]="2",
            ["UseScript"]=CustomCardCompiler.Compile(new CustomCardDocument()).Scripts["UseScript"],
            ["Description"]="抽到时：\n  使用者 · 增加生命上限 1000\n  结束本次流程。\n使用时：\n  使用时选中目标 · 造成普通伤害 100\n  结束本次流程。\n丢弃时：\n  使用者 · 使用者抽牌 6\n  结束本次流程。"
        };
        Assert(CardBlueprintDescriptionCompatibility.TryRepair(legacy,out var upgraded)&&upgraded["Description"]==expected,"existing crafted card text receives the same presentation as new cards");
        Assert(upgraded["UseScript"]==legacy["UseScript"]&&!legacy.ContainsKey("AuraToolsCustomCardDescription"),"description repair preserves gameplay and the input payload");
        Assert(!CardBlueprintDescriptionCompatibility.TryRepair(upgraded,out _),"description migration is idempotent");
        foreach(var version in new[]{"1","2","3"})
        {
            var historical=new Dictionary<string,string>(legacy){["UseScript"]="-- AuraTools.CustomCard compiler "+version+"\noriginal gameplay"};
            Assert(CardBlueprintDescriptionCompatibility.TryRepair(historical,out var converted)&&converted["Description"]==expected,"supported historical compiler description migrates: "+version);
        }
        legacy["Description"]="使用时：\n  如果 使用者生命小于 10：\n    结束本次流程。\n  使用者 · 使用者抽牌 2\n  结束本次流程。";
        Assert(CardBlueprintDescriptionCompatibility.TryRepair(legacy,out upgraded)&&upgraded["Description"].Contains("    不再执行此次触发的后续效果。")&&!upgraded["Description"].Contains("结束本次流程"),"legacy branch termination preserves its indentation and semantics");
        legacy["Id"]="Foreign_card";Assert(!CardBlueprintDescriptionCompatibility.TryRepair(legacy,out _),"description migration never rewrites another owner's card");
        legacy["Id"]="AuraToolsExp_custom_saved";legacy["UseScript"]="handwritten script";Assert(!CardBlueprintDescriptionCompatibility.TryRepair(legacy,out _),"unknown script provenance is not guessed during text migration");
        foreach(var effect in Enum.GetValues<CardEffectKind>())
        {
            var block=new CardRuleBlock{Effect=effect,Object=new(){Kind=CardObjectKind.Self},Value=CardValue.Constant(3),ResourceId="力量"};
            var description=CustomCardDescription.Block(block);
            Assert(description.EndsWith("。")&&!description.Contains(" · "),"all effects have player-facing sentences: "+effect);
        }
    }
}
