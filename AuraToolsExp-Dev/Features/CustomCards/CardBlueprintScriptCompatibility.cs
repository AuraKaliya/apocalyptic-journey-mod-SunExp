using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>One-way repair of the exact generated list accessors in existing owned card payloads.</summary>
public static class CardBlueprintScriptCompatibility
{
    public const string RuntimeRevision="5";
    public static bool TryRepair(IDictionary<string,string> original,out Dictionary<string,string> repaired,out List<string> changedScripts)
    {
        repaired=null!;changedScripts=null!;
        if(!original.TryGetValue("Id",out var id)||!id.StartsWith("AuraToolsExp_custom_",StringComparison.Ordinal)||!original.TryGetValue("AuraToolsCustomCardVersion",out var version)||(version!="1"&&version!="2"))return false;
        repaired=new Dictionary<string,string>(original,StringComparer.Ordinal);changedScripts=new();
        foreach(var key in new[]{"UseScript","DrawScript","DropScript"})
        {
            if(!original.TryGetValue(key,out var script)||string.IsNullOrEmpty(script))continue;
            var newline=script.IndexOf('\n');if(newline<0)continue;var header=script.Substring(0,newline).TrimEnd('\r');
            if(header!="-- AuraTools.CustomCard compiler 1"&&header!="-- AuraTools.CustomCard compiler 2"&&header!="-- AuraTools.CustomCard compiler 3")continue;
            // String-key dictionary accessors ARE exported by this XLua version; only integer lists change.
            var next=script.Replace("t[#t+1] = xs:get_Item(i)","t[#t+1] = xs[i]")
                .Replace("source:Add(original:get_Item(i))","source:Add(original[i])")
                .Replace("local card=cards:get_Item(i)","local card=cards[i]");
            if(next==script)continue;
            repaired[key]=next;changedScripts.Add(key);
        }
        if(changedScripts.Count==0)return false;
        repaired["AuraToolsCustomCardRuntime"]="4";return true;
    }
}
