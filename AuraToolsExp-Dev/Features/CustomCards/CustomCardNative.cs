using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using UnityEngine;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardNative
{
    internal static string? BuffName(string id)
    {
        var row=AuraGameDataHostApi.CopyRow(DataType.Buff,id);
        return row==null ? null : row.TryGetValue("Name",out var name) ? name : id;
    }
    internal static IReadOnlyList<KeyValuePair<string,string>> Buffs()
    {
        return Singleton<GameConfigManager>.Instance.GetTable(DataType.Buff).Getlines()
            .Where(r=>r.ContainsKey("Id")).Select(r=>new KeyValuePair<string,string>(r["Id"],r.TryGetValue("Name",out var name)?name:r["Id"]))
            .OrderBy(r=>r.Value,StringComparer.Ordinal).ToArray();
    }
    internal static void CheckLua(CustomCardCompilation compilation)
    {
        if(!compilation.Success)throw new InvalidOperationException(string.Join("；",compilation.Issues.Select(i=>i.Message)));
        if(ScriptExecutor.luaEnv==null)throw new InvalidOperationException("游戏脚本环境尚未就绪。");
        foreach(var script in compilation.Scripts)
        {
            // Loading compiles syntax, without executing a card or touching live battle state.
            using(var function=ScriptExecutor.luaEnv.LoadString(script.Value,"CustomCard."+script.Key)) { }
        }
    }
    internal static DataConfig Craft(CustomCardDocument document)
    {
        if(!AuraGameDataHostApi.IsNativeCatalogReady)throw new InvalidOperationException("游戏内容尚未加载完成。");
        var compilation=CustomCardCompiler.Compile(document,BuffName);
        CheckLua(compilation);
        if(!document.Artwork.UsePixels && AuraToolsResourceCache.Load<Sprite>(document.Artwork.TemplateIcon,true)==null)
            throw new InvalidOperationException("卡面模板资源不可用。");
        var id="AuraToolsExp_custom_"+Guid.NewGuid().ToString("N");
        var data=new Dictionary<string,string>(compilation.Scripts,StringComparer.Ordinal)
        {
            ["Id"]=id,["Name"]=document.Name,["Expend"]=document.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Rarity"]=document.Rarity.ToString(System.Globalization.CultureInfo.InvariantCulture),["Type"]=document.Targeted?"攻击牌":"技能牌",
            ["Tag"]=(document.Burnout?"Burnout,":"")+(document.Retain?"Retain,":""),["Description"]=compilation.Description,
            ["Note"]=document.Note,["Effects"]="",["Icon"]=document.Artwork.TemplateIcon,
            ["AuraToolsCustomCardVersion"]="1"
        };
        if(document.Artwork.UsePixels)data["AuraToolsCustomCardArt"]=JsonConvert.SerializeObject(document.Artwork);
        var vars=new Dictionary<string,string> { ["Id"]=id,["Tag"]=data["Tag"],["RawData"]=Convert.ToBase64String(GZip.CompressString(JsonConvert.SerializeObject(data))) };
        var card=new DataConfig(data,vars,true,DataType.Card);
        CustomCardArtworkRuntime.Restore(card);
        var runtime=Singleton<GameRuntimeData>.Instance ?? throw new InvalidOperationException("账号仓库尚未就绪。");
        runtime.CardData.Add(card);
        try { runtime.Save(); }
        catch { runtime.CardData.Remove(card);throw; }
        return card;
    }
}
