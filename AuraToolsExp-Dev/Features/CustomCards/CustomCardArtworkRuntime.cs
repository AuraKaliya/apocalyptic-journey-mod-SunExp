using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardArtworkRuntime
{
    private static readonly object Gate=new();
    private static ModConfig? modConfig;
    private static AuraHookRegistry? hooks;
    internal static void Initialize(ModConfig config)=>modConfig=config;
    internal static void Deactivate()
    {
        hooks?.Dispose();
        hooks=null;
    }
    internal static void Activate()
    {
        if(modConfig==null||hooks!=null)return;
        hooks=new AuraHookRegistry(modConfig,"AuraTools.CustomCards.Artwork");
        hooks.BeforeRouted("DataConfig.PreCompileScripts",context=>{if(context.Target is DataConfig card)RestorePersisted(card);},"RestoreBeforeCompile");
        hooks.AfterRouted("DataConfig.RestoreData",context=>{if(context.Target is DataConfig card)RestorePersisted(card);},"RestoreAfterLoad");
        hooks.AfterRouted("DataConfig.ReSetVars",context=>{if(context.Target is DataConfig card)RestorePersisted(card);},"RestoreAfterReset");
        // Account cards can have been deserialized before the MOD entry was initialized.
        bool updated=false;
        foreach(var card in Singleton<GameRuntimeData>.Instance.CardData.ToArray())
            try{updated|=CustomCardNative.RepairPersistedData(card);Restore(card);}catch(Exception ex){AuraToolsLog.Warn("[CustomCard] saved card could not be restored: "+ex.Message);}
        if(updated)Singleton<GameRuntimeData>.Instance.Save();
    }
    private static void RestorePersisted(DataConfig card){CustomCardNative.RepairPersistedData(card);Restore(card);}
    internal static void Restore(DataConfig card)
    {
        if(card?.data==null||!card.data.TryGetValue("AuraToolsCustomCardArt",out var json))return;
        if(!card.data.TryGetValue("AuraToolsCustomCardVersion",out var version)||!CustomCardArtwork.SupportsNativeCardVersion(version))return;
        if(json.Length>30000)throw new InvalidOperationException("自建卡牌的像素数据过大。");
        var art=JsonConvert.DeserializeObject<CustomCardArtwork>(json,new JsonSerializerSettings{MaxDepth=8,TypeNameHandling=TypeNameHandling.None});
        if(art==null||!art.UsePixels||!CardPixelCanvas.IsValid(art.Size,art.Pixels))throw new InvalidOperationException("自建卡牌的像素数据无效。");
        string key;
        using(var sha=SHA256.Create())key=string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes("1|"+art.Size+"|"+art.Pixels)).Select(b=>b.ToString("x2")));
        var path=Path.Combine(AuraSharedPaths.OwnerSystemDataDirectory(AuraToolsIds.ModId,"CustomCards"),"Artwork",key+".png");
        lock(Gate)
        {
            if(!File.Exists(path)||new FileInfo(path).Length==0)AuraSharedFileStore.WriteAllBytes(AuraToolsIds.ModId,path,CardPixelCanvas.Png(art));
        }
        var localIcon="Raw:"+path;
        if(card.data.TryGetValue("Icon",out var current)&&current==localIcon)return;
        var row=new Dictionary<string,string>(card.data,StringComparer.Ordinal){["Icon"]=localIcon};
        card.data=row;
        // RawData intentionally remains portable, with the template fallback and embedded pixels.
    }
    internal static Texture2D Texture(CustomCardArtwork art)
    {
        var texture=new Texture2D(art.Size,art.Size,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,name="CustomCardArtwork"};
        Refresh(texture,Convert.FromBase64String(art.Pixels));return texture;
    }
    internal static void Refresh(Texture2D texture,byte[] pixels)
    {
        var colors=new Color32[pixels.Length];
        for(int i=0;i<pixels.Length;i++){var p=PixelEmojiCodec.PaletteRgba[pixels[i]];colors[i]=new Color32((byte)(p>>24),(byte)(p>>16),(byte)(p>>8),(byte)p);}
        texture.SetPixels32(colors);texture.Apply(false,false);
    }
}
