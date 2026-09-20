using AuraToolsExp.Dll.Features.CustomCards;
using Newtonsoft.Json;

internal static partial class AuraToolsTestSuite
{
    private static void TestCardScriptRepair(bool lua)
    {
        var current=CustomCardCompiler.Compile(CardBlueprintTemplates.Create(8));
        string Legacy(string script)=>script.Replace("compiler 5","compiler 3").Replace("xs[i]","xs:get_Item(i)").Replace("original[i]","original:get_Item(i)").Replace("cards[i]","cards:get_Item(i)");
        var row=new Dictionary<string,string>(current.Scripts){["Id"]="AuraToolsExp_custom_existing",["AuraToolsCustomCardVersion"]="2",["Name"]="未命名魔222",["Expend"]="1",["Icon"]="Icon/Card/禁果",["AuraToolsCustomCardArt"]="portable-pixels"};
        row["UseScript"]=Legacy(row["UseScript"]);var before=JsonConvert.SerializeObject(row);
        Assert(CardBlueprintScriptCompatibility.TryRepair(row,out var repaired,out var keys)&&keys.SequenceEqual(new[]{"UseScript"}),"owned legacy script repaired only at changed entry points");
        Assert(JsonConvert.SerializeObject(row)==before,"script repair stages data without mutating original");
        foreach(var name in new[]{"Name","Expend","Icon","AuraToolsCustomCardArt","InitScript"})Assert(repaired[name]==row[name],"repair preserves card property: "+name);
        Assert(repaired["AuraToolsCustomCardRuntime"]=="4"&&!CardBlueprintScriptCompatibility.TryRepair(repaired,out _,out _),"collection repair is idempotent");
        Assert(repaired["UseScript"].Contains("data:get_Item('Expend')")&&repaired["InitScript"].Contains("Vars:set_Item"),"valid native string dictionary bindings are retained");
        var foreign=new Dictionary<string,string>(row){["Id"]="Terrias_custom_example"};Assert(!CardBlueprintScriptCompatibility.TryRepair(foreign,out _,out _),"foreign cards remain unchanged");
        foreign=new(row){["AuraToolsCustomCardVersion"]="99"};Assert(!CardBlueprintScriptCompatibility.TryRepair(foreign,out _,out _),"future card formats remain unchanged");
        foreign=new(row){["UseScript"]="-- custom user code\nxs:get_Item(0)"};Assert(!CardBlueprintScriptCompatibility.TryRepair(foreign,out _,out _),"arbitrary authored scripts remain unchanged");
        var windows=new Dictionary<string,string>(row){["UseScript"]=row["UseScript"].Replace("\n","\r\n")};Assert(CardBlueprintScriptCompatibility.TryRepair(windows,out var fixedWindows,out _)&&!fixedWindows["UseScript"].Contains("xs:get_Item(i)"),"Windows newline payloads are repaired");
        if(lua)
        {
            RunCardLua("local ok,err=pcall(assert(load("+JsonConvert.SerializeObject(row["UseScript"])+")));assert(not ok and string.find(err,'get_Item'))","assert(own.Defend==8)");
            RunCardLua(repaired["UseScript"],"pending(List({card}));assert(own.Defend==11 and self.HandCard.Count==1)");
        }
    }
}
