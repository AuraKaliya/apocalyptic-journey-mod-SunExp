using AuraToolsExp.Dll.Features.CustomCards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class AuraToolsTestSuite
{
    private static string LegacyNumericJson(CustomCardDocument document,int version=3)
    {
        var json=JObject.FromObject(document);json["SchemaVersion"]=version;
        foreach(var node in (JArray)json["Graph"]!["Nodes"]!)
        {
            node["Version"]=1;var values=(JObject)node["Numbers"]!;
            node["Number"]=(double?)values["a"]?["Value"]??6;
            node["Second"]=(double?)values["b"]?["Value"]??1;
            node["Third"]=(double?)values["c"]?["Value"]??100;
            ((JObject)node).Remove("Numbers");
        }
        return json.ToString(Formatting.None);
    }
    private static void TestCardNumbers(bool lua)
    {
        Assert(CardBlueprintNumbers.TryParse("50%",CardNumberFormat.Percent,out var value,out _)&&value==.5,"percent editor converts 50% to the canonical ratio .5");
        Assert(CardBlueprintNumbers.TryParse("0.5",CardNumberFormat.Percent,out value,out _)&&value==.005,"decimal percent is not confused with a decimal ratio");
        Assert(CardBlueprintNumbers.TryParse("150",CardNumberFormat.Percent,out value,out _)&&value==1.5,"percent is not clamped to 100");
        Assert(!CardBlueprintNumbers.TryParse("50%",CardNumberFormat.Number,out _,out _),"ordinary number input cannot silently reinterpret percent text");
        foreach(var text in new[]{"NaN","Infinity","-Infinity","1e309","","-","abc"})Assert(!CardBlueprintNumbers.TryParse(text,CardNumberFormat.Number,out _,out _),"invalid numeric literal rejected: "+text);
        foreach(double precise in new[]{.12345678901234567,3.000000000001,-.000000000023,999999.1234567})
            Assert(CardBlueprintNumbers.TryParse(CardBlueprintNumbers.Format(precise,CardNumberFormat.Number),CardNumberFormat.Number,out value,out _)&&value==precise,"editing retains the full numeric precision");
        CustomCardDocument Compare(CardValue a,CardValue b,CardValueKind op=CardValueKind.Less,CardValue? tolerance=null)
        {
            var condition=CardValue.Compare(op,a,b);if(tolerance!=null)condition.Inputs.Add(tolerance);
            var branch=new CardRuleBlock{Kind=CardBlockKind.If,Condition=condition,Then=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}};
            return new(){Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){branch}}})};
        }
        var ratio=Compare(CardValue.Reading(CardDataField.HealthPercent),CardValue.Ratio(.505));
        var compiled=CustomCardCompiler.Compile(ratio);Assert(compiled.Success&&compiled.Description.Contains("50.5%"),"percentage comparison and its description share the numeric contract");
        var mixed=Compare(CardValue.Reading(CardDataField.Health),CardValue.Ratio(.5));
        Assert(!CustomCardCompiler.Compile(mixed).Success,"health amount and percentage cannot be silently compared");
        var integerDecimal=Compare(CardValue.Reading(CardDataField.HandCount),CardValue.Constant(3.5));
        Assert(CustomCardCompiler.Compile(integerDecimal).Success,"integer sources permit decimal comparison thresholds");
        var decimalRatio=Compare(CardValue.Ratio(.5),CardValue.Ratio(.5,false),CardValueKind.Equal);
        Assert(CustomCardCompiler.Compile(decimalRatio).Success,"percent and decimal presentations of ratios are compatible");
        var near=Compare(CardValue.Compare(CardValueKind.Add,CardValue.Constant(.1),CardValue.Constant(.2)),CardValue.Constant(.3),CardValueKind.Approximately,CardValue.Constant(.000001));
        Assert(CustomCardCompiler.Compile(near).Success,"approximate comparison has an explicit tolerance");
        var negative=Compare(CardValue.Constant(1),CardValue.Constant(1),CardValueKind.Approximately,CardValue.Constant(-1));
        Assert(!CustomCardCompiler.Compile(negative).Success,"negative comparison tolerance is rejected");
        var product=new CustomCardDocument{Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Compare(CardValueKind.Multiply,CardValue.Constant(100),CardValue.Ratio(.5))}}}})};
        Assert(CustomCardCompiler.Compile(product).Success,"100 times 50% produces a quantity, not an incompatible ratio");
        var snapshot=JsonConvert.SerializeObject(ratio);CustomCardCompiler.Compile(ratio);Assert(JsonConvert.SerializeObject(ratio)==snapshot,"numeric inference never edits a document");
        var graph=new CardBlueprint();var read=graph.Add(CardNodeKind.Value);read.ValueKind=CardValueKind.Read;read.Field=CardDataField.HealthPercent;
        var comparison=graph.Add(CardNodeKind.Value);comparison.ValueKind=CardValueKind.Less;graph.Link(read,"result",comparison,"a");
        CardBlueprintNumbers.AdoptEmptyInputs(graph);
        Assert(!comparison.Operand("b").IsSet&&comparison.Operand("b").Kind==CardNumberKind.Ratio&&comparison.Operand("b").Format==CardNumberFormat.Percent,"only empty operands inherit their source's percentage format");
        comparison.Operand("b").Kind=CardNumberKind.Value;comparison.Operand("b").Format=CardNumberFormat.Number;comparison.Second=50;
        CardBlueprintNumbers.AdoptEmptyInputs(graph);Assert(comparison.Second==50&&comparison.Operand("b").Kind==CardNumberKind.Value,"connecting a percent source never changes an existing literal's meaning");
        var blank=Compare(CardValue.Constant(6),CardValue.Constant(1));var compareNode=blank.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Value&&CardBlueprintNumbers.Comparison(n.ValueKind));compareNode.Numbers.Clear();
        Assert(!CustomCardCompiler.Compile(blank).Success,"new comparison nodes need explicit values and have no inherited 6/1 defaults");
        // An old graph reads 50 for half health and then multiplies it by two.
        var old=new CustomCardDocument{Targeted=false,Graph=new()};var entry=old.Graph.Add(CardNodeKind.Entry);read=old.Graph.Add(CardNodeKind.Value);read.ValueKind=CardValueKind.Read;read.Field=CardDataField.HealthPercent;
        var multiply=old.Graph.Add(CardNodeKind.Value);multiply.ValueKind=CardValueKind.Multiply;multiply.Second=2;
        var effect=old.Graph.Add(CardNodeKind.Effect);effect.Effect=CardEffectKind.Shield;
        old.Graph.Link(entry,"next",effect,"in");old.Graph.Link(read,"result",multiply,"a");old.Graph.Link(multiply,"result",effect,"value");
        var legacy=LegacyNumericJson(old);var upgraded=CardBlueprintMigration.Read(legacy);
        Assert(upgraded.SchemaVersion==4&&upgraded.Graph.Nodes.Any(n=>n.ValueKind==CardValueKind.PercentPoints),"v3 percentage reads migrate through an explicit value conversion");
        var upgradedCompile=CustomCardCompiler.Compile(upgraded);Assert(upgradedCompile.Success,"migrated percentage arithmetic compiles");
        var second=CardBlueprintMigration.Read(JsonConvert.SerializeObject(upgraded));Assert(second.Graph.Nodes.Count==upgraded.Graph.Nodes.Count,"numeric migration is one-way and idempotent");
        var spaced=old.Copy();spaced.Graph.Nodes[0].X=320;spaced.Graph.Nodes[0].Y=160;
        var roomy=CardBlueprintMigration.Read(LegacyNumericJson(spaced));Assert(roomy.Graph.Nodes[0].X==400&&roomy.Graph.Nodes[0].Y==224,"old node spacing grows with the redesigned components");
        spaced.Graph.Nodes.First(n=>n.Field==CardDataField.HealthPercent).X=1000000;spaced.Graph.Nodes.First(n=>n.Field==CardDataField.HealthPercent).Y=1000000;
        Assert(CardBlueprintMigration.Read(LegacyNumericJson(spaced)).Graph.Nodes.All(n=>Math.Abs(n.X)<=1000000&&Math.Abs(n.Y)<=1000000),"migration conversion stays inside supported coordinate bounds");
        while(old.Graph.Nodes.Count<512)old.Graph.Add(CardNodeKind.Note);
        var full=CardBlueprintMigration.Read(LegacyNumericJson(old));Assert(full.Graph.Nodes.Count==513&&CustomCardCompiler.Compile(full).Success,"old drafts at capacity still accept the required percentage conversion");
        if(!lua)return;
        RunCardLua(compiled.Scripts["UseScript"],"assert(own.Defend==11)");
        RunCardLua(CustomCardCompiler.Compile(integerDecimal).Scripts["UseScript"],"assert(own.Defend==11)","for i=1,3 do self.HandCard:Add(card) end\n");
        RunCardLua(CustomCardCompiler.Compile(decimalRatio).Scripts["UseScript"],"assert(own.Defend==11)");
        RunCardLua(CustomCardCompiler.Compile(near).Scripts["UseScript"],"assert(own.Defend==11)");
        RunCardLua(CustomCardCompiler.Compile(product).Scripts["UseScript"],"assert(own.Defend==58)");
        RunCardLua(upgradedCompile.Scripts["UseScript"],"assert(own.Defend==108)");
        foreach(var hp in new[]{49.999,50,50.001})
        {
            var boundary=Compare(CardValue.Reading(CardDataField.HealthPercent),CardValue.Ratio(.5));
            RunCardLua(CustomCardCompiler.Compile(boundary).Scripts["UseScript"],"assert(own.Defend=="+(hp<50?11:8)+")","own.CurHp="+hp.ToString(System.Globalization.CultureInfo.InvariantCulture)+";own.MaxHp=100\n");
        }
    }
}
