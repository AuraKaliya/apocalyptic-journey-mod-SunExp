using System;
using AuraToolsExp.Dll.Features.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal interface ICustomCardPreviewContent : IDisposable
{
    void Bind(CustomCardDocument document,CustomCardCompilation compilation);
    void Fit(Vector2 size);
}

internal sealed class CustomCardPreview : MonoBehaviour
{
    private ICustomCardPreviewContent? native;
    private TMP_Text? failure;
    private CustomCardDocument? document;
    private CustomCardCompilation? compilation;
    private Action? clicked;
    private Vector2 size;
    internal static CustomCardPreview Create(Transform parent,float height,Action? clicked=null)
    {
        var root=CustomCardUi.CreateLayout("NativeCardPreview",parent);var layout=CustomCardUi.SetFixedHeight(root,height);
        layout.flexibleWidth=1;layout.minWidth=80;
        var view=root.AddComponent<CustomCardPreview>();view.clicked=clicked;return view;
    }
    internal void Bind(CustomCardDocument value,CustomCardCompilation result)
    {
        document=value;compilation=result;if(!isActiveAndEnabled)return;
        try
        {
            native??=CustomCardPreviewHost.Create((RectTransform)transform,clicked);
            native.Bind(value,result);native.Fit(((RectTransform)transform).rect.size);
            if(failure!=null)failure.gameObject.SetActive(false);
        }
        catch(Exception ex)
        {
            native?.Dispose();native=null;
            failure??=CustomCardUi.AddTmpText(transform,"",14,TextAnchor.MiddleCenter,CustomCardVisuals.Muted,80);
            failure.alignment=TextAlignmentOptions.Center;failure.text="卡牌预览暂不可用\n"+ex.Message;failure.gameObject.SetActive(true);
            var r=failure.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new(8,8);r.offsetMax=new(-8,-8);
        }
    }
    private void LateUpdate()
    {
        var next=((RectTransform)transform).rect.size;if(size==next)return;size=next;native?.Fit(size);
    }
    private void OnEnable(){if(document!=null&&compilation!=null)Bind(document,compilation);}
    private void OnDisable(){native?.Dispose();native=null;}
    private void OnDestroy(){native?.Dispose();native=null;}
}
