using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardWindowStyle
{
    internal static void Skin(GameObject window)
    {
        var background=window.GetComponent<Image>();if(background!=null){background.sprite=null;background.color=CustomCardVisuals.Canvas;}
        CustomCardControls.Border(window,CustomCardVisuals.Border);
        var header=window.transform.Find("Header");
        if(header!=null)
        {
            CustomCardUi.SetFixedHeight(header.gameObject,52);
            var fill=header.GetComponent<Image>();if(fill!=null){fill.sprite=null;fill.color=CustomCardVisuals.Canvas;}
            foreach(var text in header.GetComponentsInChildren<TMP_Text>()){text.color=CustomCardVisuals.Ink;text.fontSize=20;text.fontStyle=FontStyles.Normal;}
            var title=header.GetComponentInChildren<TMP_Text>();if(title!=null){var element=title.GetComponent<LayoutElement>();if(element!=null){element.minWidth=100;element.preferredWidth=180;element.flexibleWidth=1;}}
        }
    }
}

internal static class CustomCardFormStyle
{
    internal static void Input(TMP_InputField input,bool stretch=true)
    {
        if(input.targetGraphic is Image background){background.sprite=null;background.color=CustomCardVisuals.Well;}
        CustomCardControls.Border(input.gameObject,CustomCardVisuals.Border);
        input.textComponent.color=CustomCardVisuals.Ink;input.textComponent.fontSize=16;input.customCaretColor=true;input.caretColor=CustomCardVisuals.Selected;
        if(input.placeholder is TMP_Text placeholder){placeholder.color=CustomCardVisuals.Muted;placeholder.fontSize=14;}
        CustomCardInputFeedback.Attach(input);
        var layout=input.GetComponent<LayoutElement>();if(stretch&&layout!=null){layout.minWidth=0;layout.preferredWidth=-1;layout.flexibleWidth=1;}
    }
}

internal sealed class CustomCardFillAvailable : MonoBehaviour
{
    private RectTransform viewport=null!;
    private LayoutElement element=null!;
    private float reserved,minimum;
    internal void Configure(RectTransform view,float reserve,float min)
    {viewport=view;reserved=reserve;minimum=min;element=gameObject.GetComponent<LayoutElement>()??gameObject.AddComponent<LayoutElement>();Resize();}
    private void LateUpdate()=>Resize();
    private void Resize(){if(viewport==null||element==null)return;float height=Mathf.Max(minimum,viewport.rect.height-reserved);if(Mathf.Abs(element.preferredHeight-height)<1)return;element.minHeight=element.preferredHeight=height;element.flexibleHeight=0;}
}
