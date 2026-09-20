using System;
using AuraUi.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using HostUi=AuraToolsExp.Dll.Features.Settings.AuraToolsUi;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>Production geometry and typography for the workshop. Preview adapts fonts and host services only.</summary>
internal static class CustomCardUi
{
    internal static Color Text=>CustomCardVisuals.Ink;
    internal static Color Panel=>CustomCardVisuals.Node;
    internal static Color ErrorText=>CustomCardVisuals.Error;
    internal static Color SuccessText=>CustomCardVisuals.Visited;
    internal static bool RunConfigAction(Action action)=>HostUi.RunConfigAction(action);
    internal static GameObject CreateRect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 pivot,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform;r.anchorMin=min;r.anchorMax=max;r.pivot=pivot;r.sizeDelta=size;r.anchoredPosition=Vector2.zero;return go;
    }
    internal static GameObject CreateLayout(string name,Transform parent)=>CreateRect(name,parent,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
    internal static LayoutElement SetFixedHeight(GameObject go,float height)
    {
        var e=go.GetComponent<LayoutElement>()??go.AddComponent<LayoutElement>();e.minHeight=e.preferredHeight=height;e.flexibleHeight=0;return e;
    }
    internal static LayoutElement SetFixedSize(GameObject go,float width,float height)
    {
        var e=SetFixedHeight(go,height);e.minWidth=0;e.preferredWidth=width;e.flexibleWidth=0;return e;
    }
    internal static Image AddImage(GameObject go,Color color){var image=go.AddComponent<Image>();image.color=color;return image;}
    internal static TextMeshProUGUI AddTmpText(Transform parent,string value,int size,TextAnchor anchor,Color color,float height=40,float flexibleWidth=0,float width=0,bool autoSize=false)
    {
        var go=CreateLayout("Text",parent);var text=go.AddComponent<TextMeshProUGUI>();AuraUiNativeBridge.Apply(text);
        text.text=value;text.fontSize=size;text.fontStyle=FontStyles.Normal;text.color=color;text.raycastTarget=false;
        text.enableAutoSizing=false;text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Ellipsis;
        text.alignment=anchor switch
        {
            TextAnchor.MiddleLeft=>TextAlignmentOptions.MidlineLeft,TextAnchor.MiddleRight=>TextAlignmentOptions.MidlineRight,
            TextAnchor.MiddleCenter=>TextAlignmentOptions.Center,TextAnchor.UpperCenter=>TextAlignmentOptions.Top,
            TextAnchor.UpperRight=>TextAlignmentOptions.TopRight,_=>TextAlignmentOptions.TopLeft
        };
        var element=SetFixedHeight(go,height);element.minWidth=0;element.preferredWidth=width>0?width:-1;element.flexibleWidth=flexibleWidth;
        return text;
    }
    internal static TMP_InputField AddTmpInput(Transform parent,string value,string placeholder,Action<string> changed,float width=190,float height=40)
    {
        var go=CreateLayout("Input",parent);SetFixedSize(go,width,height);var background=AddImage(go,CustomCardVisuals.Well);
        var viewport=CreateRect("Viewport",go.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
        var rect=(RectTransform)viewport.transform;rect.offsetMin=new(12,4);rect.offsetMax=new(-12,-4);viewport.AddComponent<RectMask2D>();
        var text=AddTmpText(viewport.transform,value,16,TextAnchor.MiddleLeft,Text,height);text.GetComponent<LayoutElement>().ignoreLayout=true;
        text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Masking;
        var hint=AddTmpText(viewport.transform,placeholder,14,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,height);hint.GetComponent<LayoutElement>().ignoreLayout=true;
        hint.textWrappingMode=TextWrappingModes.NoWrap;
        var input=go.AddComponent<TMP_InputField>();input.targetGraphic=background;input.textViewport=rect;input.textComponent=text;input.placeholder=hint;
        input.text=value;input.onValueChanged.AddListener(v=>RunConfigAction(()=>changed(v)));CustomCardFormStyle.Input(input,false);return input;
    }
    internal static void SetButtonLabel(Button button,string value){var text=button.GetComponentInChildren<TMP_Text>();if(text!=null)text.text=value;}
    internal static Transform CreateScroll(Transform parent,string name)
    {
        var go=CreateLayout("Scroll-"+name,parent);var element=go.AddComponent<LayoutElement>();element.minWidth=0;element.minHeight=0;element.flexibleWidth=element.flexibleHeight=1;
        var viewport=CreateRect("Viewport",go.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
        ((RectTransform)viewport.transform).offsetMax=new(-10,0);AddImage(viewport,Color.clear);viewport.AddComponent<RectMask2D>();
        var content=CreateRect("Content",viewport.transform,new(0,1),new(1,1),new(0,1),Vector2.zero);
        var layout=content.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=false;
        content.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var scroll=go.AddComponent<ScrollRect>();scroll.viewport=(RectTransform)viewport.transform;scroll.content=(RectTransform)content.transform;scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=32;scroll.movementType=ScrollRect.MovementType.Clamped;
        var track=CreateRect("Scrollbar",go.transform,new(1,0),new(1,1),new(1,.5f),new(8,0));AddImage(track,Color.clear);
        var handle=CreateRect("Handle",track.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);var fill=AddImage(handle,CustomCardVisuals.Border);
        var bar=track.AddComponent<Scrollbar>();bar.handleRect=(RectTransform)handle.transform;bar.targetGraphic=fill;bar.direction=Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        return content.transform;
    }
    internal static GameObject CreateOverlay(string name,Transform parent,string title,Action? closed=null,bool singleInstance=true,float maxWidth=1180,Func<bool>? canClose=null,bool fullWindow=false,float preferredHeight=0)
    {
        var window=HostUi.CreateOverlay(name,parent,title,closed,singleInstance,maxWidth,canClose,fullWindow,preferredHeight);
        var header=window.transform.Find("Header");var old=header.GetComponentInChildren<Button>();var close=old.onClick;
        CustomCardUiLifetime.Clear(header);
        var layout=header.GetComponent<HorizontalLayoutGroup>();layout.padding=new(4,4,0,0);layout.spacing=8;layout.childAlignment=TextAnchor.MiddleLeft;
        layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=layout.childForceExpandWidth=false;
        AddTmpText(header,title,20,TextAnchor.MiddleLeft,Text,40,1);
        CustomCardControls.IconButton(header,CardIcon.Close,"关闭",()=>close.Invoke());
        return window;
    }
}
