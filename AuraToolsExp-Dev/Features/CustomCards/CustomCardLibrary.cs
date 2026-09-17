using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Modules;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.CustomCards;

public sealed class CustomCardLibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<CustomCardDocument> Items { get; set; } = new();
}

internal static class CustomCardLibrary
{
    private const string FileName="CustomCardLibrary.json";
    private static readonly object Gate=new();
    private static AuraSharedConfigSnapshot<CustomCardLibraryDocument> Read()
    {
        var s=AuraSharedConfigStore.ReadOwner(AuraToolsIds.ModId,AuraToolsPaths.ConfigSystem,FileName,new CustomCardLibraryDocument());
        if(s.SchemaVersion>1 || s.Value?.SchemaVersion!=1 || s.Value.Items==null) throw new InvalidOperationException("设计稿库版本不兼容，原文件保持不变。");
        return s;
    }
    internal static IReadOnlyList<CustomCardDocument> List()
    {
        lock(Gate) return Read().Value.Items.Where(i=>i!=null).Select(i=>i.Copy()).ToArray();
    }
    internal static void Save(CustomCardDocument doc)
    {
        if(doc.Format!=CustomCardDocument.FormatId || doc.SchemaVersion!=1) throw new InvalidOperationException("不能覆盖不兼容版本的作品。");
        lock(Gate)
        {
            var snapshot=Read();
            var old=snapshot.Value.Items.FirstOrDefault(d=>d.Id==doc.Id);
            if(old!=null && old.Revision!=doc.Revision) throw new InvalidOperationException("作品已被其他编辑修改，请重新打开后保存。");
            if(old==null && snapshot.Value.Items.Count>=128) throw new InvalidOperationException("作品库已达到 128 张，请先导出并整理。");
            var copy=doc.Copy();copy.Revision=(old?.Revision??0)+1;
            snapshot.Value.Items.RemoveAll(d=>d.Id==doc.Id);snapshot.Value.Items.Add(copy);
            var result=AuraSharedConfigStore.WriteOwner(AuraToolsIds.ModId,AuraToolsPaths.ConfigSystem,FileName,snapshot.Value,snapshot.Revision,1);
            if(!result.Success) throw new IOException(result.Message);
            doc.Revision=copy.Revision;
            AuraToolConfigChangeBus.Publish(AuraToolModuleIds.CustomCards,result.Revision);
        }
    }
    internal static void Delete(CustomCardDocument doc)
    {
        lock(Gate)
        {
            var snapshot=Read();var old=snapshot.Value.Items.FirstOrDefault(i=>i.Id==doc.Id);
            if(old!=null && old.Revision!=doc.Revision)throw new InvalidOperationException("作品已被其他编辑修改，请重新打开。");
            snapshot.Value.Items.RemoveAll(i=>i.Id==doc.Id);
            var result=AuraSharedConfigStore.WriteOwner(AuraToolsIds.ModId,AuraToolsPaths.ConfigSystem,FileName,snapshot.Value,snapshot.Revision,1);
            if(!result.Success)throw new IOException(result.Message);
            AuraToolConfigChangeBus.Publish(AuraToolModuleIds.CustomCards,result.Revision);
        }
    }
    internal static string Export(CustomCardDocument doc)
    {
        var directory=Path.Combine(AuraToolsConfigService.DataRootDirectory,"Exports","CustomCards");
        var path=Path.Combine(directory,doc.Id+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".auracard.json");
        AuraSharedFileStore.WriteAllText(AuraToolsIds.ModId,path,JsonConvert.SerializeObject(doc,Formatting.Indented));
        return path;
    }
    internal static CustomCardDocument Import(string path)
    {
        var info=new FileInfo(path);
        if(!info.Exists || info.Length<2 || info.Length>2*1024*1024)throw new InvalidOperationException("作品文件无效或超过 2 MB。");
        var doc=JsonConvert.DeserializeObject<CustomCardDocument>(File.ReadAllText(path),new JsonSerializerSettings { MaxDepth=80,TypeNameHandling=TypeNameHandling.None }) ?? throw new InvalidOperationException("作品内容为空。");
        if(doc.Format!=CustomCardDocument.FormatId || doc.SchemaVersion!=1)throw new InvalidOperationException("作品格式或版本不兼容。");
        var validation=CustomCardCompiler.Compile(doc);
        if(!validation.Success)throw new InvalidOperationException("导入检查未通过："+string.Join("；",validation.Issues.Select(i=>i.Message)));
        doc.Id=Guid.NewGuid().ToString("N");doc.Revision=0;
        return doc;
    }
}
