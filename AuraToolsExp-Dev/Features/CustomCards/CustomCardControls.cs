using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal enum CardControlKind { Secondary, Primary, Quiet, Tab, ActiveTab, Selected, Segment }
internal enum CardIcon { Close, More, Undo, Redo, Library, Layers, Expand, Panel, Help, Pencil, Eraser, Fill, Pick }

/// <summary>The workshop owns its controls; shared input, sound and focus services remain shared.</summary>
internal static class CustomCardControls
{
    internal static Button IconButton(Transform parent,CardIcon icon,string label,Action action,float size=32)
    {
        var root=CustomCardUi.CreateLayout("CardAction."+label,parent);CustomCardUi.SetFixedSize(root,size,size);
        var fill=CustomCardUi.AddImage(root,Color.clear);var button=root.AddComponent<Button>();button.targetGraphic=fill;
        root.AddComponent<AuraUi.Shared.AuraUiButtonSoundRelay>().Configure(button,AuraUi.Shared.AuraUiButtonSoundStyle.Pure);
        button.onClick.AddListener(()=>CustomCardUi.RunConfigAction(action));
        var graphic=CustomCardUi.CreateRect("Icon",root.transform,new(.5f,.5f),new(.5f,.5f),new(.5f,.5f),new(16,16)).AddComponent<CustomCardIcon>();
        graphic.Icon=icon;graphic.color=CustomCardVisuals.Muted;graphic.raycastTarget=false;
        Style(button,CardControlKind.Quiet);root.AddComponent<CustomCardTooltip>().Text=label;return button;
    }
    internal static void Style(Button button, CardControlKind kind)
    {
        var feedback=button.GetComponent<CustomCardControlFeedback>()??button.gameObject.AddComponent<CustomCardControlFeedback>();
        feedback.Configure(button,kind);
    }

    internal static void Border(GameObject target, Color color)
    {
        var border=target.transform.Find("ControlBorder")?.GetComponent<CustomCardControlBorder>();
        if(border==null)
        {
            var go=CustomCardUi.CreateRect("ControlBorder",target.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
            var element=go.AddComponent<LayoutElement>();element.ignoreLayout=true;
            border=go.AddComponent<CustomCardControlBorder>();border.raycastTarget=false;
        }
        border.color=color;
    }

    internal static Button Select(Transform parent,IReadOnlyList<string> labels,int selected,Action<int> changed,float width)
    {
        var index=Math.Max(0,Math.Min(labels.Count-1,selected));Button button=null!;
        button=CustomCardWorkshopController.Button(parent,labels[index],()=>
            CustomCardPopover.Show(button.transform,labels,index,i=>
            {
                changed(i);index=i;
                if(button!=null)CustomCardUi.SetButtonLabel(button,labels[i]);
            }),width,40);
        var text=button.GetComponentInChildren<TMP_Text>();text.alignment=TextAlignmentOptions.MidlineLeft;
        text.rectTransform.offsetMin=new(12,2);text.rectTransform.offsetMax=new(-30,-2);
        var arrow=CustomCardUi.CreateRect("Arrow",button.transform,new(1,.5f),new(1,.5f),new(1,.5f),new(12,8));
        arrow.GetComponent<RectTransform>().anchoredPosition=new(-12,0);arrow.AddComponent<CustomCardChevron>().raycastTarget=false;
        return button;
    }

    internal static Transform Segments(Transform parent,IReadOnlyList<string> labels,int selected,Action<int> changed,float width=140,bool stretch=true)
    {
        var row=CustomCardUi.CreateLayout("Segments",parent).transform;var element=CustomCardUi.SetFixedSize(row.gameObject,width*labels.Count,40);element.flexibleWidth=stretch?1:0;
        CustomCardUi.AddImage(row.gameObject,CustomCardVisuals.Well);Border(row.gameObject,CustomCardVisuals.Border);
        var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.padding=new(4,4,4,4);layout.spacing=4;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
        for(int i=0;i<labels.Count;i++)
        {
            int index=i;
            var button=CustomCardWorkshopController.Button(row,labels[i],()=>changed(index),width,32);
            button.GetComponent<LayoutElement>().flexibleWidth=1;
            Style(button,index==selected?CardControlKind.Segment:CardControlKind.Quiet);
        }
        return row;
    }

    internal static Toggle Check(Transform parent,string label,bool value,Action<bool> changed,float width=120)
    {
        var root=CustomCardUi.CreateLayout("Check."+label,parent);CustomCardUi.SetFixedSize(root,width,40);
        var hit=CustomCardUi.AddImage(root,Color.clear);
        var box=CustomCardUi.CreateRect("Box",root.transform,new(0,.5f),new(0,.5f),new(0,.5f),new(20,20));
        var background=CustomCardUi.AddImage(box,CustomCardVisuals.Well);Border(box,CustomCardVisuals.Border);
        var mark=CustomCardUi.CreateRect("Mark",box.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
        mark.GetComponent<RectTransform>().offsetMin=new(4,4);mark.GetComponent<RectTransform>().offsetMax=new(-4,-4);
        var graphic=CustomCardUi.AddImage(mark,CustomCardVisuals.Gold);graphic.raycastTarget=false;
        var text=CustomCardUi.AddTmpText(root.transform,label,16,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,40);
        text.rectTransform.offsetMin=new(30,0);text.rectTransform.offsetMax=Vector2.zero;
        var toggle=root.AddComponent<Toggle>();toggle.targetGraphic=background;toggle.graphic=graphic;toggle.isOn=value;
        toggle.onValueChanged.AddListener(v=>CustomCardUi.RunConfigAction(()=>changed(v)));
        return toggle;
    }

    internal static void Rule(Transform parent)
    {
        var go=CustomCardUi.CreateLayout("Divider",parent);CustomCardUi.SetFixedHeight(go,1);
        CustomCardUi.AddImage(go,CustomCardVisuals.Border).raycastTarget=false;
    }
}

internal sealed class CustomCardControlBorder : MonoBehaviour
{
    internal bool Underline;
    internal float Pixels=1.25f;
    private readonly Image[] edges=new Image[4];
    private Color tint;
    private Canvas? rootCanvas;
    internal Color color{get=>tint;set{tint=value;Refresh();}}
    internal bool raycastTarget{get=>false;set{}}
    private void Awake()
    {
        rootCanvas=GetComponentInParent<Canvas>()?.rootCanvas;
        var anchors=new[]{(new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,0),new Vector2(0,1)),(new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,1),new Vector2(0,1)),(new Vector2(0,0),new Vector2(0,1),new Vector2(0,.5f),new Vector2(1,0)),(new Vector2(1,0),new Vector2(1,1),new Vector2(1,.5f),new Vector2(1,0))};
        for(int i=0;i<4;i++)
        {var a=anchors[i];var go=CustomCardUi.CreateRect("Edge"+i,transform,a.Item1,a.Item2,a.Item3,a.Item4);edges[i]=CustomCardUi.AddImage(go,tint);edges[i].raycastTarget=false;}
        Refresh();
    }
    internal void SetVerticesDirty()=>Refresh();
    private void OnRectTransformDimensionsChange()=>Refresh();
    private void Refresh()
    {
        var rootScale=rootCanvas!=null?rootCanvas.transform.lossyScale:Vector3.one;var ownScale=transform.lossyScale;
        float pixelsX=rootCanvas!=null?rootCanvas.scaleFactor*Mathf.Abs(ownScale.x/Mathf.Max(.00001f,Mathf.Abs(rootScale.x))):1;
        float pixelsY=rootCanvas!=null?rootCanvas.scaleFactor*Mathf.Abs(ownScale.y/Mathf.Max(.00001f,Mathf.Abs(rootScale.y))):1;
        float vertical=Pixels/Mathf.Max(.1f,pixelsX),horizontal=(Underline?2:Pixels)/Mathf.Max(.1f,pixelsY);
        for(int i=0;i<4;i++)if(edges[i]!=null){edges[i].color=tint;edges[i].gameObject.SetActive(!Underline||i==0);}
        if(edges[0]!=null)edges[0].rectTransform.sizeDelta=new(0,horizontal);
        if(edges[1]!=null)edges[1].rectTransform.sizeDelta=new(0,horizontal);
        for(int i=2;i<4;i++)if(edges[i]!=null)edges[i].rectTransform.sizeDelta=new(vertical,0);
    }
}

internal sealed class CustomCardControlFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler
{
    private Button button=null!;
    private CardControlKind kind;
    private bool hovered,pressed,focused,enabledState;
    internal void Configure(Button value,CardControlKind style)
    {
        button=value;kind=style;button.transition=Selectable.Transition.None;
        if(button.targetGraphic is Image image)image.sprite=null;
        CustomCardControls.Border(gameObject,CustomCardVisuals.Border);Apply();
    }
    private void Apply()
    {
        if(button==null)return;enabledState=button.IsInteractable();
        bool primary=kind==CardControlKind.Primary,selected=kind==CardControlKind.Selected||kind==CardControlKind.ActiveTab||kind==CardControlKind.Segment;
        bool tab=kind==CardControlKind.Tab||kind==CardControlKind.ActiveTab;
        bool quiet=kind==CardControlKind.Quiet||tab;
        var fill=tab?Color.clear:primary?CustomCardVisuals.Gold:selected?CustomCardVisuals.Raised:quiet?Color.clear:CustomCardVisuals.Node;
        if(pressed)fill=Color.Lerp(fill,CustomCardVisuals.Gold,.2f);
        else if(hovered||focused)fill=Color.Lerp(quiet?CustomCardVisuals.Canvas:fill,CustomCardVisuals.Raised,.65f);
        if(tab)fill=Color.clear;
        if(!enabledState)fill=quiet?Color.clear:Color.Lerp(fill,CustomCardVisuals.Canvas,.55f);
        button.targetGraphic.color=fill;button.targetGraphic.CrossFadeColor(Color.white,0,true,true);
        foreach(var text in button.GetComponentsInChildren<TMP_Text>())
            text.color=!enabledState?CustomCardVisuals.Muted:primary?CustomCardVisuals.Canvas:selected?CustomCardVisuals.Gold:quiet&&!hovered?CustomCardVisuals.Muted:CustomCardVisuals.Ink;
        foreach(var icon in button.GetComponentsInChildren<CustomCardIcon>())icon.color=!enabledState?Color.Lerp(CustomCardVisuals.Muted,CustomCardVisuals.Canvas,.6f):hovered?CustomCardVisuals.Ink:CustomCardVisuals.Muted;
        var border=transform.Find("ControlBorder").GetComponent<CustomCardControlBorder>();
        border.Underline=tab;
        border.color=kind==CardControlKind.Segment?Color.clear:selected||primary?CustomCardVisuals.Gold:quiet?Color.clear:hovered||focused?CustomCardVisuals.Gold:CustomCardVisuals.Border;
        border.SetVerticesDirty();
    }
    private void LateUpdate(){if(button!=null&&enabledState!=button.IsInteractable())Apply();}
    public void OnPointerEnter(PointerEventData e){hovered=true;Apply();}
    public void OnPointerExit(PointerEventData e){hovered=pressed=false;Apply();}
    public void OnPointerDown(PointerEventData e){pressed=true;Apply();}
    public void OnPointerUp(PointerEventData e){pressed=false;Apply();}
    public void OnSelect(BaseEventData e){focused=true;Apply();}
    public void OnDeselect(BaseEventData e){focused=pressed=false;Apply();}
    private void OnDisable(){hovered=pressed=focused=false;}
}

internal sealed class CustomCardChevron : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;
        CustomCardGraphLines.Quad(mesh,new(r.xMin,r.yMax),new(r.center.x,r.yMin),CustomCardVisuals.Muted,1.5f);
        CustomCardGraphLines.Quad(mesh,new(r.center.x,r.yMin),new(r.xMax,r.yMax),CustomCardVisuals.Muted,1.5f);
    }
}

/// <summary>An owned, bounded menu. Closing the workshop also destroys all of its menus.</summary>
internal sealed class CustomCardPopover : MonoBehaviour
{
    private static CustomCardPopover? active;
    private Transform anchor=null!;
    internal static void Show(Transform source,IReadOnlyList<string> labels,int selected,Action<int> choose)
    {
        if(active!=null){var same=active.anchor==source;active.Close();if(same)return;}
        var owner=source.GetComponentInParent<CustomCardFocusOwner>();
        var parent=owner!=null?owner.transform:source.GetComponentInParent<Canvas>().transform;
        var layer=CustomCardUi.CreateRect("WorkshopMenu",parent,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
        layer.AddComponent<LayoutElement>().ignoreLayout=true;
        var background=CustomCardUi.AddImage(layer,new(0,0,0,.04f));var dismiss=layer.AddComponent<Button>();dismiss.targetGraphic=background;
        var menu=layer.AddComponent<CustomCardPopover>();menu.anchor=source;active=menu;dismiss.onClick.AddListener(menu.Close);
        Canvas.ForceUpdateCanvases();var rect=(RectTransform)layer.transform;var corners=new Vector3[4];((RectTransform)source).GetWorldCorners(corners);
        var low=rect.InverseTransformPoint(corners[0]);var high=rect.InverseTransformPoint(corners[2]);
        float width=Mathf.Min(Mathf.Max(220,high.x-low.x),rect.rect.width-24),height=Mathf.Min(labels.Count*42+16,Mathf.Min(380,rect.rect.height-24));
        var panel=CustomCardUi.CreateRect("MenuPanel",rect,new(.5f,.5f),new(.5f,.5f),new(0,1),new(width,height));
        var panelRect=(RectTransform)panel.transform;
        panelRect.anchoredPosition=new(Mathf.Clamp(low.x,rect.rect.xMin+12,rect.rect.xMax-width-12),Mathf.Clamp(low.y-4,rect.rect.yMin+height+12,rect.rect.yMax-12));
        var image=CustomCardUi.AddImage(panel,CustomCardVisuals.Node);panel.AddComponent<Button>().targetGraphic=image;CustomCardControls.Border(panel,CustomCardVisuals.Border);
        var layout=panel.AddComponent<VerticalLayoutGroup>();layout.padding=new(8,8,8,8);layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;
        var list=CustomCardUi.CreateScroll(panel.transform,"MenuItems");
        for(int i=0;i<labels.Count;i++)
        {
            int index=i;var button=CustomCardWorkshopController.Button(list,labels[i],()=>{menu.Close();choose(index);},width-16,36);
            CustomCardControls.Style(button,index==selected?CardControlKind.Selected:CardControlKind.Quiet);
            button.GetComponent<LayoutElement>().flexibleWidth=1;
            button.GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
        }
    }
    private void Update(){if(anchor==null||!anchor.gameObject.activeInHierarchy||CustomCardInputState.Down(KeyCode.Escape))Close();}
    private void Close(){if(active==this)active=null;CustomCardUiLifetime.Destroy(gameObject,anchor!=null?anchor.gameObject:null);}
    private void OnDestroy(){if(active==this)active=null;}
}
