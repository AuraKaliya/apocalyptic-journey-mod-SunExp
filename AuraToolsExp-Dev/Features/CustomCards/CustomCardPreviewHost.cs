using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;
using Witch.UI.Window;
using Object=UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardPreviewHost
{
    internal static ICustomCardPreviewContent Create(RectTransform parent,Action? clicked)
    {
        var prefab=AuraToolsResourceCache.Load<GameObject>("UI/DictionaryUI",false);
        var dictionary=prefab!=null?prefab.GetComponent<DictionaryUI>():null;
        var templates=dictionary?.CardList?.parent?.parent?.Find("TempList");
        var template=templates!=null&&templates.childCount>0?templates.GetChild(0).GetComponent<DictionaryShowItem>():null;
        if(template==null)throw new InvalidOperationException("游戏图鉴尚未加载。");
        var view=Object.Instantiate(template,parent,false);
        try{return new NativeContent(view,clicked);}
        catch{view.gameObject.SetActive(false);Object.Destroy(view.gameObject);throw;}
    }

    private sealed class NativeContent : ICustomCardPreviewContent
    {
        private readonly DictionaryShowItem view;
        private readonly Vector2 bounds;
        private readonly List<Material> ownedMaterials=new();
        private DataConfig? snapshot;
        private Texture2D? texture;
        private Sprite? sprite;
        private string artKey="";
        private bool disposed;
        internal NativeContent(DictionaryShowItem item,Action? clicked)
        {
            view=item;view.gameObject.name="DictionaryCard";view.ItemType="Card";view.dictionaryUI=null;
            var rect=(RectTransform)view.transform;bounds=rect.rect.size;
            if(bounds.x<=0||bounds.y<=0)throw new InvalidOperationException("图鉴卡牌尺寸无效。");
            rect.anchorMin=rect.anchorMax=rect.pivot=new(.5f,.5f);rect.anchoredPosition=Vector2.zero;
            foreach(var graphic in view.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            // DictionaryShowItem has its own pointer handlers. A button on the same root
            // owns magnification while the native KeywordDisplay continues to own hover.
            var hit=view.GetComponent<Image>()??view.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            var button=view.GetComponent<Button>()??view.gameObject.AddComponent<Button>();button.targetGraphic=hit;button.transition=Selectable.Transition.None;
            button.onClick.RemoveAllListeners();if(clicked!=null)button.onClick.AddListener(()=>clicked());
            // Own the material instances on the exact native image surfaces before
            // ICard's .material writes. Other renderers and shared theme assets are not owned here.
            foreach(var path in new[]{"Front/icon","Front/FrontBack","Front/Icons/Ench/Item"})
            {
                var target=view.transform.Find(path);var renderer=target!=null?target.GetComponent<MeshRenderer>():null;
                if(renderer==null||renderer.sharedMaterial==null)continue;
                var material=new Material(renderer.sharedMaterial);renderer.material=material;ownedMaterials.Add(material);
            }
        }
        public void Bind(CustomCardDocument document,CustomCardCompilation compilation)
        {
            if(disposed)return;ResetSnapshot();
            var fields=CustomCardPresentationData.Create(document,compilation,"AuraToolsExp_preview_"+document.Id);
            fields["Expend"]=Math.Max(0,document.Cost).ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields["Rarity"]=Math.Max(1,Math.Min(3,document.Rarity)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            snapshot=new DataConfig(fields,new Dictionary<string,string>{{"Tag",fields["Tag"]}},false,DataType.Card);
            // SetCardMsg invokes InitScript even on dictionary surfaces. Preview has no
            // compiled gameplay scripts, account card, RawData writer or battle context.
            snapshot.scriptExecutor.ScriptDict["InitScript"]=(Action)(()=>{});
            view.Init(snapshot);view.gameObject.SetActive(true);
            var next=document.Artwork.UsePixels?document.Artwork.Size+"|"+document.Artwork.Pixels:"";
            if(next!=artKey)
            {
                ReleaseArtwork();artKey=next;
                if(document.Artwork.UsePixels)
                {
                    texture=CustomCardArtworkRuntime.Texture(document.Artwork);
                    sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new(.5f,.5f));
                }
            }
            if(sprite!=null)
            {
                var icon=view.transform.Find("Front/icon");
                if(icon.TryGetComponent<Image>(out var image))image.sprite=sprite;
                else if(icon.TryGetComponent<MeshRenderer>(out var renderer))renderer.material.mainTexture=texture;
                else throw new InvalidOperationException("图鉴卡面组件不可用。");
                var keyword=view.GetComponent<KeywordDisplay>();if(keyword!=null)keyword.icon=sprite;
            }
        }
        public void Fit(Vector2 size)
        {
            if(!disposed&&view!=null)view.transform.localScale=Vector3.one*Mathf.Max(.01f,Mathf.Min((size.x-12)/bounds.x,(size.y-12)/bounds.y));
        }
        private void ResetSnapshot()
        {
            if(snapshot==null)return;
            try
            {
                if(view!=null)
                {
                    var keyword=view.GetComponent<KeywordDisplay>();if(keyword!=null&&keyword.isHover)keyword.OnPointerExit(null);
                    AuraCardPresentationRuntime.RequestReset(new AuraCardPresentationContext{Root=view.transform,Config=snapshot,Surface=AuraCardPresentationSurface.Dictionary,ResetKind=AuraCardPresentationResetKind.Rebind,Source="CustomCard.Preview.Release"});
                }
            }
            finally{snapshot.scriptExecutor?.Clear();snapshot=null;}
        }
        private void ReleaseArtwork(){if(sprite!=null)Object.Destroy(sprite);if(texture!=null)Object.Destroy(texture);sprite=null;texture=null;}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{ResetSnapshot();}
            finally
            {
                if(view!=null){view.dataConfig=null;view.gameObject.SetActive(false);Object.Destroy(view.gameObject);}
                ReleaseArtwork();foreach(var material in ownedMaterials)if(material!=null)Object.Destroy(material);ownedMaterials.Clear();
            }
        }
    }
}
