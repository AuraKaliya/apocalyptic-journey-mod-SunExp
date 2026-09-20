using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>One-way presentation migration for owned, generated cards without changing their scripts.</summary>
public static class CardBlueprintDescriptionCompatibility
{
    public const string Revision="2";
    internal const string RevisionField="AuraToolsCustomCardDescription";
    public static bool TryRepair(IDictionary<string,string> original,out Dictionary<string,string> repaired)
    {
        repaired=null!;
        if(!original.TryGetValue("Id",out var id)||!id.StartsWith("AuraToolsExp_custom_",StringComparison.Ordinal)
            ||!original.TryGetValue("AuraToolsCustomCardVersion",out var version)||(version!="1"&&version!="2")
            ||original.ContainsKey(RevisionField)||!original.TryGetValue("Description",out var description))return false;
        // Do not rewrite imported, handwritten effects just because an ID happens to use our prefix.
        if(!new[]{"DrawScript","UseScript","DropScript"}.Any(key=>original.TryGetValue(key,out var script)
            &&new[]{"1","2","3","4","5"}.Any(v=>script.StartsWith("-- AuraTools.CustomCard compiler "+v+"\n",StringComparison.Ordinal)
                ||script.StartsWith("-- AuraTools.CustomCard compiler "+v+"\r\n",StringComparison.Ordinal))))return false;
        var lines=description.Replace("\r\n","\n").Split('\n');var output=new List<string>();
        for(int i=0;i<lines.Length;i++)
        {
            var line=lines[i];var body=line.TrimStart(' ');int indent=line.Length-body.Length;
            bool triggerEnd=i==lines.Length-1||lines[i+1].Length>0&&lines[i+1][0]!=' ';
            if(indent==2&&body=="结束本次流程。"&&triggerEnd)continue;
            if(body=="结束本次流程。")body="不再执行此次触发的后续效果。";
            int separator=body.IndexOf(" · ",StringComparison.Ordinal);
            if(separator>0)
            {
                var subject=body.Substring(0,separator);var effect=body.Substring(separator+3);
                for(int kind=0;kind<CustomCardNames.Effects.Length;kind++)
                {
                    var name=CustomCardNames.Effects[kind];
                    if(effect!=name&&!effect.StartsWith(name+" ",StringComparison.Ordinal)&&!effect.StartsWith(name+"「",StringComparison.Ordinal))continue;
                    var tail=effect.Substring(name.Length).Trim();var value=tail;var resource="";var e=(CardEffectKind)kind;
                    if(CustomCardNames.NeedsBuff(e))
                    {
                        int open=tail.LastIndexOf('「');if(open<0||!tail.EndsWith("」",StringComparison.Ordinal))break;
                        value=tail.Substring(0,open).Trim();resource=tail.Substring(open+1,tail.Length-open-2);
                    }
                    body=CustomCardDescription.Effect(e,subject,value,resource);break;
                }
            }
            output.Add(new string(' ',indent)+body);
        }
        // Flatten only entirely linear trigger bodies; indentation of branches remains meaningful.
        var compact=new List<string>();
        for(int i=0;i<output.Count;)
        {
            var title=output[i++];int start=i;while(i<output.Count&&output[i].StartsWith(" ",StringComparison.Ordinal))i++;
            var body=output.Skip(start).Take(i-start).ToArray();
            if(title.EndsWith("：",StringComparison.Ordinal)&&body.Length>0&&body.All(line=>line.StartsWith("  ",StringComparison.Ordinal)&&!line.StartsWith("    ",StringComparison.Ordinal)&&line.EndsWith("。",StringComparison.Ordinal)))
                compact.Add(title+string.Join("",body.Select(line=>line.Trim())));
            else{compact.Add(title);compact.AddRange(body);}
        }
        repaired=new Dictionary<string,string>(original,StringComparer.Ordinal){["Description"]=string.Join("\n",compact),[RevisionField]=Revision};
        return true;
    }
}
