using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using AuraGameData.Shared.GameApi;
using AuraGameData.Shared;
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
    internal static IReadOnlyList<CardStateOption> States()=>AuraGameDataHostApi.Table(DataType.Buff).Select(row=>State(row.Id)).Where(row=>row!=null).Cast<CardStateOption>().ToArray();
    internal static CardStateOption? State(string id)
    {
        var row=AuraGameDataHostApi.Resolve(DataType.Buff,id);
        if(row==null)return null;
        row.Fields.TryGetValue("Name",out var name);row.Fields.TryGetValue("Description",out var description);row.Fields.TryGetValue("Icon",out var icon);
        string source=row.OwnerModId??"",sourceName="";
        var manager=Singleton<GameConfigManager>.Instance;
        if(row.SourceKind==AuraGameDataSourceKinds.Native)
        {
            // The shared native capture's BaseGame owner is a default, not proof of
            // provenance. The host tracks CSV script owners, including foreign MODs.
            if(manager.TryGetModDataConfigOwner(row.Id,out var owner)&&owner!=null){source=owner.ModId??"";sourceName=owner.ModName??"";}
            else if(source=="BaseGame")source="";
        }
        if(sourceName.Length==0&&source.Length>0)sourceName=manager.modConfigs.FirstOrDefault(mod=>mod.ModId==source)?.ModName??"";
        return new(){Id=row.Id,Name=string.IsNullOrWhiteSpace(name)?row.Id:name!,Description=description??"",Source=source,SourceName=sourceName,Icon=icon??""};
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
        var data=CustomCardPresentationData.Create(document,compilation,id);
        foreach(var script in compilation.Scripts)data[script.Key]=script.Value;
        data["AuraToolsCustomCardVersion"]="2";
        data["AuraToolsCustomCardRuntime"]=CardBlueprintScriptCompatibility.RuntimeRevision;
        data[CardBlueprintDescriptionCompatibility.RevisionField]=CardBlueprintDescriptionCompatibility.Revision;
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
    internal static bool RepairPersistedData(DataConfig card)
    {
        if(card?.data==null||card.Type!=DataType.Card||!card.data.TryGetValue("Id",out var id)||!id.StartsWith("AuraToolsExp_custom_",StringComparison.Ordinal))return false;
        lock(card)
        {
            bool Repair(IDictionary<string,string> original,out Dictionary<string,string> updated,out List<string> changedScripts)
            {
                bool changed=CardBlueprintScriptCompatibility.TryRepair(original,out updated,out changedScripts);
                if(!changed){updated=new(original,StringComparer.Ordinal);changedScripts=new();}
                if(CardBlueprintDescriptionCompatibility.TryRepair(updated,out var presentation)){updated=presentation;changed=true;}
                return changed;
            }
            if(!Repair(card.data,out var repaired,out var keys))return false;
            string? replacementRaw=null;
            if(card.Vars.TryGetValue("RawData",out var raw))
            {
                var portable=JsonConvert.DeserializeObject<Dictionary<string,string>>(GZip.DecompressToString(Convert.FromBase64String(raw)))??throw new InvalidOperationException("旧卡牌原始数据为空。");
                Repair(portable,out var migrated,out _);
                migrated.TryGetValue("Description",out var portableDescription);repaired.TryGetValue("Description",out var liveDescription);
                if(migrated["Id"]!=repaired["Id"]||portableDescription!=liveDescription||keys.Any(key=>!migrated.TryGetValue(key,out var script)||script!=repaired[key]))throw new InvalidOperationException("旧卡牌原始数据与运行数据不一致，已保留原数据。");
                string hash;using(var sha=SHA256.Create())hash=string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)).Select(b=>b.ToString("x2")));
                var backup=Path.Combine(AuraSharedPaths.OwnerSystemDataDirectory(AuraToolsIds.ModId,"CustomCards"),"ScriptRepairs",hash+".rawdata.txt");
                if(!File.Exists(backup))AuraSharedFileStore.WriteAllText(AuraToolsIds.ModId,backup,raw);
                replacementRaw=Convert.ToBase64String(GZip.CompressString(JsonConvert.SerializeObject(migrated)));
            }
            card.data=repaired;if(replacementRaw!=null)card.Vars["RawData"]=replacementRaw;
            foreach(var key in keys)card.scriptExecutor?.ScriptDict.Remove(key);
            AuraToolsLog.Warn("[CustomCard] 已更新工坊卡牌："+repaired["Id"]+(keys.Count>0?"；修复脚本 "+string.Join(", ",keys):"；效果描述"));
            return true;
        }
    }
}
