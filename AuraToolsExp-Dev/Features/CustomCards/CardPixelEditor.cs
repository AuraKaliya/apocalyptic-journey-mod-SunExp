using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static AuraToolsExp.Dll.Features.CustomCards.CustomCardWorkshopController;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal sealed class CardPixelEditorController : MonoBehaviour
{
    private CustomCardArtwork art=null!;
    private Action record=null!;
    private Texture2D texture=null!;
    private RawImage image=null!;
    private byte[] pixels=Array.Empty<byte>();
    private Action changed=null!;
    private bool notifyPending;
    private int mode;
    private byte color=2;
    private Vector2Int last;
    private bool drawing;
    private RectTransform canvas=null!;
    private CardPixelGrid grid=null!;
    private RectTransform viewport=null!;
    private readonly List<Button> swatches=new(),toolButtons=new();
    private TMP_Text zoomLabel=null!;
    private Button sizeButton=null!;
    private float zoom=1;
    private Vector2 lastView,panOrigin,panPointer;
    private bool panning;
    internal void Build(Transform parent,CustomCardArtwork artwork,Action beforeChange,Action afterChange)
    {
        art=artwork;record=beforeChange;changed=afterChange;pixels=Convert.FromBase64String(art.Pixels);
        parent.GetComponent<VerticalLayoutGroup>().spacing=12;
        var toolsRow=CommandRow(parent,"PaintTools");
        string[] names={"画笔","橡皮","填充","吸色"};
        for(int i=0;i<names.Length;i++){int selected=i;toolButtons.Add(Button(toolsRow,names[i],()=>{mode=selected;RefreshTools();},60,36));}
        Spacer(toolsRow);CustomCardControls.IconButton(toolsRow,CardIcon.Help,"画板帮助",()=>ReportHelp(toolsRow));
        var actions=CommandRow(parent,"PixelActions");
        sizeButton=Select(actions,new[]{"32 × 32","64 × 64","128 × 128"},Array.IndexOf(CardPixelCanvas.Sizes,art.Size),i=>
        {BeginChange();pixels=IndexedPixelCanvas.Resize(pixels,art.Size,CardPixelCanvas.Sizes[i]);art.Size=CardPixelCanvas.Sizes[i];Refresh();},116);
        Quiet(Button(actions,"导入底图",Import,92));
        Button templates=null!;
        templates=Quiet(Button(actions,"模板",()=>CustomCardPopover.Show(templates.transform,CardPixelCanvas.Templates,-1,i=>{BeginChange();pixels=CardPixelCanvas.Template(art.Size,i);Refresh();}),60));
        var body=CustomCardUi.CreateLayout("PixelWorkspace",parent);var bodySize=body.AddComponent<LayoutElement>();bodySize.flexibleHeight=1;bodySize.minHeight=160;
        var bodyLayout=body.AddComponent<HorizontalLayoutGroup>();bodyLayout.spacing=16;bodyLayout.childControlWidth=bodyLayout.childControlHeight=true;bodyLayout.childForceExpandWidth=false;bodyLayout.childForceExpandHeight=true;
        var palette=CustomCardUi.CreateLayout("PixelPalette",body.transform);var shelf=palette.AddComponent<LayoutElement>();shelf.minWidth=0;shelf.preferredWidth=92;
        var layout=palette.AddComponent<GridLayoutGroup>();layout.cellSize=new(20,20);layout.spacing=new(4,4);layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=4;
        var responsive=palette.AddComponent<CardPixelPaletteLayout>();responsive.Layout=layout;responsive.Shelf=shelf;responsive.Count=PixelEmojiCodec.PaletteRgba.Length;
        for(byte i=0;i<PixelEmojiCodec.PaletteRgba.Length;i++)
        {
            byte index=i;var button=Button(palette.transform,i==0?"×":"",()=>{color=index;RefreshTools();},20,20);swatches.Add(button);
            button.GetComponent<CustomCardControlFeedback>().enabled=false;button.transform.Find("ControlBorder").gameObject.SetActive(false);button.transition=Selectable.Transition.ColorTint;
            var p=PixelEmojiCodec.PaletteRgba[i];var value=i==0?new Color(.14f,.12f,.2f):(Color)new Color32((byte)(p>>24),(byte)(p>>16),(byte)(p>>8),255);
            var tint=button.colors;tint.normalColor=value;tint.highlightedColor=tint.selectedColor=Color.Lerp(value,Color.white,.2f);tint.pressedColor=Color.Lerp(value,Color.black,.15f);button.colors=tint;button.targetGraphic.color=Color.white;button.targetGraphic.CrossFadeColor(value,0,true,true);
            var border=button.gameObject.AddComponent<Outline>();border.effectColor=CustomCardVisuals.Gold;border.effectDistance=new(2,2);border.enabled=false;
        }
        var view=CustomCardUi.CreateLayout("PixelViewport",body.transform);var el=view.AddComponent<LayoutElement>();el.flexibleHeight=el.flexibleWidth=1;el.minWidth=140;viewport=(RectTransform)view.transform;
        CustomCardUi.AddImage(view,CustomCardVisuals.Well);view.AddComponent<RectMask2D>();
        var go=CustomCardUi.CreateRect("Canvas",view.transform,new(.5f,.5f),new(.5f,.5f),new(.5f,.5f),Vector2.one*320);
        canvas=(RectTransform)go.transform;go.AddComponent<CardPixelBackdrop>();
        var paint=CustomCardUi.CreateRect("Pixels",go.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);image=paint.AddComponent<RawImage>();image.raycastTarget=false;
        var input=go.AddComponent<CardPixelInput>();input.Owner=this;
        var overlay=CustomCardUi.CreateRect("Grid",go.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
        grid=overlay.AddComponent<CardPixelGrid>();grid.raycastTarget=false;CustomCardControls.Border(go,CustomCardVisuals.Border);
        var viewTools=CommandRow(parent,"PixelViewTools");Spacer(viewTools);
        Quiet(Button(viewTools,"适合画布",()=>{zoom=1;FitCanvas(true);},92));
        Quiet(Button(viewTools,"−",()=>Zoom(-1),28));zoomLabel=CustomCardUi.AddTmpText(viewTools,"1.0×",12,TextAnchor.MiddleCenter,CustomCardVisuals.Muted,32,0,40);Quiet(Button(viewTools,"＋",()=>Zoom(1),28));
        CustomCardControls.Check(viewTools,"网格",true,v=>{grid.Visible=v;grid.SetVerticesDirty();},84);
        Refresh(false);
    }
    private void RefreshTools()
    {
        for(int i=0;i<swatches.Count;i++)swatches[i].GetComponent<Outline>().enabled=i==color;
        for(int i=0;i<toolButtons.Count;i++)CustomCardVisuals.Button(toolButtons[i],i==mode);
        CustomCardUi.SetButtonLabel(sizeButton,art.Size+" × "+art.Size);
    }
    private void FitCanvas(bool center=false)
    {
        if(viewport==null||canvas==null||viewport.rect.width<=0)return;
        canvas.sizeDelta=Vector2.one*Math.Max(96,Math.Min(viewport.rect.width,viewport.rect.height)-24)*zoom;
        if(center)canvas.anchoredPosition=Vector2.zero;
        if(zoomLabel!=null)zoomLabel.text=zoom.ToString("0.0")+"×";grid.SetVerticesDirty();
    }
    internal void Zoom(float amount){zoom=Mathf.Clamp(zoom*Mathf.Pow(1.2f,amount),.5f,8);FitCanvas();}
    private void LateUpdate(){if(viewport!=null&&lastView!=viewport.rect.size){lastView=viewport.rect.size;FitCanvas(true);}if(notifyPending&&!drawing){notifyPending=false;changed();}}
    private void BeginChange()
    {
        record();
    }
    internal void Begin(PointerEventData e)
    {
        if(e.button==PointerEventData.InputButton.Middle){panning=true;panPointer=e.position;panOrigin=canvas.anchoredPosition;return;}
        if(!Position(e,out var point))return;
        if(mode==3){color=pixels[point.y*art.Size+point.x];RefreshTools();return;}
        BeginChange();last=point;drawing=true;
        byte ink=e.button==PointerEventData.InputButton.Right||mode==1?(byte)0:color;
        if(mode==2){IndexedPixelCanvas.Fill(pixels,art.Size,point.x,point.y,ink);drawing=false;}
        else IndexedPixelCanvas.DrawLine(pixels,art.Size,point.x,point.y,point.x,point.y,ink);
        Refresh();
    }
    internal void Drag(PointerEventData e)
    {
        if(panning){var root=GetComponentInParent<Canvas>();canvas.anchoredPosition=panOrigin+(e.position-panPointer)/(root!=null?root.scaleFactor:1);return;}
        if(!drawing||!Position(e,out var point))return;
        IndexedPixelCanvas.DrawLine(pixels,art.Size,last.x,last.y,point.x,point.y,e.button==PointerEventData.InputButton.Right||mode==1?(byte)0:color);last=point;Refresh();
    }
    internal void End(){drawing=false;panning=false;}
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
        RefreshTools();if(edited)notifyPending=true;
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
    private void ReportHelp(Transform parent)
    {
        var window=Overlay("CustomCards.PaintHelp",parent,"画板操作",maxWidth:520,preferredHeight:260);
        Hint(window.transform,"右键擦除 · 中键移动 · 滚轮缩放");Hint(window.transform,"底图会转换为当前色板。撤销可恢复导入前的卡面。");
    }
    private void OnDisable(){End();notifyPending=false;}
    private void OnDestroy(){if(texture!=null)Destroy(texture);}
}

internal sealed class CardPixelInput : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IDragHandler,IScrollHandler
{
    internal CardPixelEditorController Owner=null!;
    public void OnPointerDown(PointerEventData e)=>Owner.Begin(e);
    public void OnPointerUp(PointerEventData e)=>Owner.End();
    public void OnDrag(PointerEventData e)=>Owner.Drag(e);
    public void OnScroll(PointerEventData e)=>Owner.Zoom(e.scrollDelta.y);
}

internal sealed class CardPixelPaletteLayout : MonoBehaviour
{
    internal GridLayoutGroup Layout=null!;
    internal LayoutElement Shelf=null!;
    internal int Count;
    private void LateUpdate()
    {
        if(Layout==null)return;float height=((RectTransform)transform).rect.height;
        int rows=Mathf.Max(1,Mathf.FloorToInt((height+4)/24));int columns=Mathf.Clamp(Mathf.CeilToInt(Count/(float)rows),2,8);
        if(columns>2&&columns<4)columns=4;
        if(Layout.constraintCount==columns)return;Layout.constraintCount=columns;Shelf.preferredWidth=columns*24-4;
    }
}

internal sealed class CardPixelBackdrop : RawImage
{
    private Texture2D? owned;
    protected override void Awake()
    {
        base.Awake();owned=new Texture2D(16,16,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
        var values=new Color32[256];for(int y=0;y<16;y++)for(int x=0;x<16;x++)values[y*16+x]=(x/8+y/8)%2==0?new Color32(32,27,45,255):new Color32(45,38,58,255);
        owned.SetPixels32(values);owned.Apply();texture=owned;raycastTarget=true;
    }
    protected override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();uvRect=new(0,0,rectTransform.rect.width/16,rectTransform.rect.height/16);}
    protected override void OnDestroy(){if(owned!=null)Destroy(owned);base.OnDestroy();}
}

internal sealed class CardPixelGrid : RawImage
{
    internal int Size=64;
    internal bool Visible=true;
    private Texture2D? owned;
    private int textureSize;
    private void LateUpdate()=>EnsureTexture();
    private void EnsureTexture()
    {
        if(owned==null||textureSize!=Size)
        {
            if(owned!=null)Destroy(owned);textureSize=Size;int side=Size*8;
            owned=new Texture2D(side,side,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var colors=new Color32[side*side];var line=new Color32(180,170,200,42);
            for(int y=0;y<side;y++)for(int x=0;x<side;x++)if(x%8==0||y%8==0||x==side-1||y==side-1)colors[y*side+x]=line;
            owned.SetPixels32(colors);owned.Apply();texture=owned;
        }
    }
    protected override void OnPopulateMesh(VertexHelper vh){if(!Visible||rectTransform.rect.width/Size<5){vh.Clear();return;}base.OnPopulateMesh(vh);}
    protected override void OnDestroy(){if(owned!=null)Destroy(owned);base.OnDestroy();}
}
