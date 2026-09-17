using System;
using System.Collections.Generic;
using System.IO;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static AuraToolsExp.Dll.Features.CustomCards.CustomCardWorkshopController;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CardPixelEditor
{
    internal static void Show(Transform parent,CustomCardArtwork artwork,Action record,Action changed)
    {
        var window=AuraToolsUi.CreateOverlay("CustomCards.PixelEditor",parent,"像素卡面",changed,maxWidth:1000);
        window.AddComponent<CardPixelEditorController>().Build(window.transform,artwork,record);
    }
}

internal sealed class CardPixelEditorController : MonoBehaviour
{
    private CustomCardArtwork art=null!;
    private Action record=null!;
    private Texture2D texture=null!;
    private RawImage image=null!;
    private byte[] pixels=Array.Empty<byte>();
    private readonly List<(int Size,string Pixels,bool UsePixels)> undo=new(),redo=new();
    private int mode;
    private byte color=2;
    private Vector2Int last;
    private bool drawing;
    private RectTransform canvas=null!;
    private CardPixelGrid grid=null!;
    internal void Build(Transform parent,CustomCardArtwork artwork,Action beforeChange)
    {
        art=artwork;record=beforeChange;pixels=Convert.FromBase64String(art.Pixels);
        var actions=Row(parent,"PixelActions");
        Select(actions,new[]{"32 × 32","64 × 64","128 × 128"},Array.IndexOf(CardPixelCanvas.Sizes,art.Size),i=>
        {BeginChange();pixels=IndexedPixelCanvas.Resize(pixels,art.Size,CardPixelCanvas.Sizes[i]);art.Size=CardPixelCanvas.Sizes[i];Refresh();},150);
        Select(actions,CardPixelCanvas.Templates,0,i=>{BeginChange();pixels=CardPixelCanvas.Template(art.Size,i);Refresh();},140);
        Button(actions,"撤销",()=>History(undo,redo),66);Button(actions,"重做",()=>History(redo,undo),66);
        Button(actions,"导入底图",Import,100);
        var tools=Row(parent,"PixelTools");
        Select(tools,new[]{"画笔","橡皮","填充","吸色"},0,i=>mode=i,130);
        Select(tools,new[]{"100%","200%","300%"},0,i=>{canvas.sizeDelta=Vector2.one*320*(i+1);},130);
        Button(tools,"网格",()=>{grid.Visible=!grid.Visible;grid.SetVerticesDirty();},70);
        Hint(parent,"32 色像素绘制。导入底图会转换为当前色板；模板、尺寸变化和绘画均可撤销。右键可擦除。");
        var palette=AuraToolsUi.CreateLayout("PixelPalette",parent);AuraToolsUi.SetFixedHeight(palette,74);
        var layout=palette.AddComponent<GridLayoutGroup>();layout.cellSize=new Vector2(32,32);layout.spacing=new Vector2(4,4);layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=16;
        for(byte i=0;i<PixelEmojiCodec.PaletteRgba.Length;i++)
        {
            byte index=i;var button=Button(palette.transform,i==0?"×":"",()=>color=index,32,32);
            var p=PixelEmojiCodec.PaletteRgba[i];button.targetGraphic.color=i==0?new Color(0.25f,0.25f,0.25f):new Color32((byte)(p>>24),(byte)(p>>16),(byte)(p>>8),255);
        }
        var view=AuraToolsUi.CreateLayout("PixelViewport",parent);var el=view.AddComponent<LayoutElement>();el.flexibleHeight=1;el.minHeight=200;
        AuraToolsUi.AddImage(view,new Color(0.13f,0.13f,0.17f));view.AddComponent<RectMask2D>();
        var go=AuraToolsUi.CreateRect("Canvas",view.transform,new Vector2(0.5f,0.5f),new Vector2(0.5f,0.5f),new Vector2(0.5f,0.5f),Vector2.one*320);
        canvas=go.GetComponent<RectTransform>();image=go.AddComponent<RawImage>();
        var input=go.AddComponent<CardPixelInput>();input.Owner=this;
        var overlay=AuraToolsUi.CreateRect("Grid",go.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
        grid=overlay.AddComponent<CardPixelGrid>();grid.raycastTarget=false;
        var scroll=view.AddComponent<ScrollRect>();scroll.viewport=view.GetComponent<RectTransform>();scroll.content=canvas;scroll.horizontal=true;scroll.vertical=true;scroll.scrollSensitivity=28;
        Refresh(false);
    }
    private void BeginChange()
    {
        record();undo.Add((art.Size,Convert.ToBase64String(pixels),art.UsePixels));if(undo.Count>40)undo.RemoveAt(0);redo.Clear();
    }
    private void History(List<(int Size,string Pixels,bool UsePixels)> from,List<(int Size,string Pixels,bool UsePixels)> to)
    {
        if(from.Count==0)return;record();to.Add((art.Size,Convert.ToBase64String(pixels),art.UsePixels));var value=from[from.Count-1];from.RemoveAt(from.Count-1);
        art.Size=value.Size;art.UsePixels=value.UsePixels;pixels=Convert.FromBase64String(value.Pixels);Refresh(false);
    }
    internal void Begin(PointerEventData e)
    {
        if(!Position(e,out var point))return;
        if(mode==3){color=pixels[point.y*art.Size+point.x];return;}
        BeginChange();last=point;drawing=true;
        byte ink=e.button==PointerEventData.InputButton.Right||mode==1?(byte)0:color;
        if(mode==2){IndexedPixelCanvas.Fill(pixels,art.Size,point.x,point.y,ink);drawing=false;}
        else IndexedPixelCanvas.DrawLine(pixels,art.Size,point.x,point.y,point.x,point.y,ink);
        Refresh();
    }
    internal void Drag(PointerEventData e)
    {
        if(!drawing||!Position(e,out var point))return;
        IndexedPixelCanvas.DrawLine(pixels,art.Size,last.x,last.y,point.x,point.y,e.button==PointerEventData.InputButton.Right||mode==1?(byte)0:color);last=point;Refresh();
    }
    internal void End()=>drawing=false;
    private bool Position(PointerEventData e,out Vector2Int point)
    {
        point=default;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,e.position,e.pressEventCamera,out var local))return false;
        var rect=canvas.rect;if(!rect.Contains(local))return false;
        point=new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((local.x-rect.xMin)/rect.width*art.Size),0,art.Size-1),Mathf.Clamp(Mathf.FloorToInt((local.y-rect.yMin)/rect.height*art.Size),0,art.Size-1));return true;
    }
    private void Refresh(bool edited=true)
    {
        art.Pixels=Convert.ToBase64String(pixels);if(edited)art.UsePixels=true;
        if(texture==null||texture.width!=art.Size){if(texture!=null)Destroy(texture);texture=CustomCardArtworkRuntime.Texture(art);image.texture=texture;}
        else CustomCardArtworkRuntime.Refresh(texture,pixels);
        grid.Size=art.Size;grid.SetVerticesDirty();
    }
    private void Import()
    {
        OptionalFileDialog.PickImageFileAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),result=>
        {
            if(this==null||!result.Selected)return;
            Texture2D? source=null;
            try
            {
                var info=new FileInfo(result.Path);if(info.Length>8*1024*1024)throw new InvalidOperationException("底图不能超过 8 MB。");
                source=ResourceLoader.Load<Texture>("Raw:"+info.FullName) as Texture2D;
                if(source==null||source.width>4096||source.height>4096)throw new InvalidOperationException("图片无效或超过 4096 × 4096。");
                BeginChange();
                for(int y=0;y<art.Size;y++)for(int x=0;x<art.Size;x++)
                {
                    var pixel=source.GetPixelBilinear((x+0.5f)/art.Size,(y+0.5f)/art.Size);byte best=0;float distance=float.MaxValue;
                    if(pixel.a>0.3f)for(byte c=1;c<PixelEmojiCodec.PaletteRgba.Length;c++)
                    {
                        var packed=PixelEmojiCodec.PaletteRgba[c];var sample=(Color)new Color32((byte)(packed>>24),(byte)(packed>>16),(byte)(packed>>8),255);
                        float d=(sample.r-pixel.r)*(sample.r-pixel.r)+(sample.g-pixel.g)*(sample.g-pixel.g)+(sample.b-pixel.b)*(sample.b-pixel.b);if(d<distance){distance=d;best=c;}
                    }
                    pixels[y*art.Size+x]=best;
                }
                Refresh();
            }
            catch(Exception ex){AuraToolsLog.Warn("[CustomCard] import artwork: "+ex.Message);Witch.UI.UIManager.Instance?.ShowModalWindow("卡面导入",ex.Message,null,1f);}
            finally{if(source!=null)Destroy(source);}
        });
    }
    private void OnDestroy(){if(texture!=null)Destroy(texture);}
}

internal sealed class CardPixelInput : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IDragHandler
{
    internal CardPixelEditorController Owner=null!;
    public void OnPointerDown(PointerEventData e)=>Owner.Begin(e);
    public void OnPointerUp(PointerEventData e)=>Owner.End();
    public void OnDrag(PointerEventData e)=>Owner.Drag(e);
}

internal sealed class CardPixelGrid : MaskableGraphic
{
    internal int Size=64;
    internal bool Visible=true;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();if(!Visible)return;var rect=rectTransform.rect;
        for(int i=0;i<=Size;i++)
        {
            float x=rect.xMin+rect.width*i/Size,y=rect.yMin+rect.height*i/Size;
            Add(vh,new Rect(x-0.3f,rect.yMin,0.6f,rect.height));Add(vh,new Rect(rect.xMin,y-0.3f,rect.width,0.6f));
        }
    }
    private static void Add(VertexHelper vh,Rect r)
    {
        int i=vh.currentVertCount;var c=new Color32(180,180,200,45);
        vh.AddVert(new Vector3(r.xMin,r.yMin),c,Vector2.zero);vh.AddVert(new Vector3(r.xMax,r.yMin),c,Vector2.zero);vh.AddVert(new Vector3(r.xMax,r.yMax),c,Vector2.zero);vh.AddVert(new Vector3(r.xMin,r.yMax),c,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
    }
}
