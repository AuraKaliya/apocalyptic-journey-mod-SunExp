using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static AuraToolsExp.Dll.Features.CustomCards.CustomCardWorkshopController;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardStateSelector
{
    internal static Button Create(Transform parent,string id,Action open,float width=218)
    {
        var state=string.IsNullOrEmpty(id)?null:CustomCardNative.State(id);
        var button=Button(parent,state?.Name??(id.Length==0?"选择状态…":"状态未加载 · 重新选择"),open,width,36);
        button.gameObject.name="StateSelector";
        var text=button.GetComponentInChildren<TMP_Text>();text.alignment=TextAlignmentOptions.MidlineLeft;text.fontSize=14;text.textWrappingMode=TextWrappingModes.NoWrap;text.richText=false;
        text.rectTransform.offsetMin=new(38,2);text.rectTransform.offsetMax=new(-24,-2);
        Icon(button.transform,state,28);
        if(state==null)CustomCardControls.Border(button.gameObject,CustomCardVisuals.Error);
        var arrow=CustomCardUi.AddTmpText(button.transform,"›",18,TextAnchor.MiddleCenter,CustomCardVisuals.Muted,30);
        arrow.rectTransform.anchorMin=new(1,0);arrow.rectTransform.anchorMax=new(1,1);arrow.rectTransform.sizeDelta=new(20,0);arrow.rectTransform.anchoredPosition=new(-12,0);
        button.gameObject.AddComponent<CustomCardTooltip>().Text=state==null?(id.Length==0?"必选：指定这张卡要操作的状态":"未加载的状态："+id):state.Name+" · "+state.SourceLabel+"\n点击查看说明或更换";
        return button;
    }
    internal static void Icon(Transform parent,CardStateOption? state,float size=44,bool layout=false)
    {
        var go=CustomCardUi.CreateRect("StateIcon",parent,new(0,.5f),new(0,.5f),new(0,.5f),new(size,size));
        ((RectTransform)go.transform).anchoredPosition=new(6,0);
        if(layout)CustomCardUi.SetFixedSize(go,size,size);
        var sprite=string.IsNullOrEmpty(state?.Icon)?null:AuraToolsResourceCache.Load<Sprite>(state!.Icon,true);
        if(sprite!=null){var icon=CustomCardUi.AddImage(go,Color.white);icon.sprite=sprite;icon.preserveAspect=true;icon.raycastTarget=false;}
        else {var icon=go.AddComponent<CustomCardIcon>();icon.Icon=CardIcon.Layers;icon.color=CustomCardVisuals.Gold;icon.raycastTarget=false;}
    }
}

internal sealed class CustomCardStatePicker:MonoBehaviour
{
    private IReadOnlyList<CardStateOption> entries=Array.Empty<CardStateOption>();
    private string current="",focused="",query="";
    private string? source;
    private int page;
    private Transform list=null!,details=null!,pages=null!;
    private TMP_Text count=null!;
    private Action<string> choose=null!;

    internal static void Show(Transform parent,string current,Action<string> choose)
    {
        var entries=CustomCardNative.States();
        var window=Overlay("CustomCards.States",parent,"选择状态",fullWindow:true);
        var picker=window.AddComponent<CustomCardStatePicker>();picker.entries=entries;picker.current=current;picker.focused=current;picker.choose=choose;picker.Build();
    }
    private void Close()=>CustomCardUiLifetime.Destroy(transform.parent.gameObject);
    private void Build()
    {
        var toolbar=CommandRow(transform,"StateSearch");
        Quiet(Button(toolbar,"返回编辑",Close,90));
        var search=CustomCardUi.AddTmpInput(toolbar,"","搜索名称、标识或来源",v=>{query=v;page=0;Fill();},320);CustomCardFormStyle.Input(search);
        var sources=entries.Select(e=>e.Source).Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        var filters=new[]{"全部来源"}.Concat(sources.Select(s=>entries.First(e=>e.Source==s).SourceLabel)).ToArray();
        Select(toolbar,filters,0,i=>{source=i==0?null:sources[i-1];page=0;Fill();},190);
        count=CustomCardUi.AddTmpText(transform,"",13,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,26);
        var body=TwoColumns(transform,"StateBrowser");body.gameObject.AddComponent<LayoutElement>().flexibleHeight=1;body.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight=true;body.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth=false;
        list=CustomCardUi.CreateScroll(body,"StateResults");details=CustomCardUi.CreateScroll(body,"StateDetails");
        details.parent.parent.GetComponent<LayoutElement>().flexibleWidth=.9f;
        details.GetComponent<VerticalLayoutGroup>().padding=new(20,12,8,12);CustomCardUi.AddImage(details.parent.parent.gameObject,CustomCardVisuals.Node);
        pages=CommandRow(transform,"StatePages");Fill();
    }
    private void Fill()
    {
        var matches=CardStateQuery.Filter(entries,query,source);page=Math.Max(0,Math.Min(page,CardStateQuery.PageCount(matches.Count)-1));
        count.text=matches.Count+" 个结果 / 共 "+entries.Count+" 个状态";
        CustomCardUiLifetime.Clear(list);CustomCardUiLifetime.Clear(pages);
        if(matches.Count==0)Hint(list,entries.Count==0?"没有已加载的状态。请在游戏内容加载完成后重新打开。":"没有匹配的状态，试试其他名称或来源。");
        foreach(var entry in CardStateQuery.Page(matches,page))
        {
            var item=entry;
            var button=Button(list,(item.Id==current?"✓  ":"")+item.Name+"\n"+item.SourceLabel+" · "+item.Id,()=>{focused=item.Id;Fill();},400,68);
            button.gameObject.name="StateResult."+item.Id;button.GetComponent<LayoutElement>().flexibleWidth=1;
            CustomCardControls.Style(button,item.Id==focused?CardControlKind.Selected:CardControlKind.Quiet);
            var text=button.GetComponentInChildren<TMP_Text>();text.alignment=TextAlignmentOptions.MidlineLeft;text.fontSize=14;text.richText=false;text.rectTransform.offsetMin=new(50,4);
            CustomCardStateSelector.Icon(button.transform,item,36);
        }
        var previous=Button(pages,"上一页",()=>{page--;Fill();},86);previous.interactable=page>0;
        CustomCardUi.AddTmpText(pages,(page+1)+" / "+CardStateQuery.PageCount(matches.Count),14,TextAnchor.MiddleCenter,CustomCardVisuals.Muted,36,0,100);
        Button(pages,"下一页",()=>{page++;Fill();},86).interactable=page+1<CardStateQuery.PageCount(matches.Count);
        var scroll=list.GetComponentInParent<ScrollRect>();scroll.verticalNormalizedPosition=1;
        FillDetails();
    }
    private void FillDetails()
    {
        CustomCardUiLifetime.Clear(details);
        var item=entries.FirstOrDefault(e=>e.Id==focused);
        if(item==null){Hint(details,focused.Length==0?"选择左侧状态，查看作用和来源。":"原状态未加载，请重新选择。",focused.Length==0?CustomCardVisuals.Muted:CustomCardVisuals.Error);return;}
        var heading=CommandRow(details,"StateHeading");CustomCardUi.SetFixedHeight(heading.gameObject,56);
        CustomCardStateSelector.Icon(heading,item,48,true);
        var title=CustomCardUi.AddTmpText(heading,item.Name,20,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,56,1);title.richText=false;
        Hint(details,"来源："+item.SourceLabel);Hint(details,"标识："+item.Id);
        CustomCardControls.Rule(details);
        var description=string.IsNullOrWhiteSpace(item.Description)?"这个状态未提供文字说明。":item.Description;
        Hint(details,description,CustomCardVisuals.Ink);
        Hint(details,"层数、叠加和持续规则由状态本身决定。施加会添加层数；移除会移除整个状态；未持有时读取层数为 0。");
        var button=Button(details,item.Id==current?"已选择 · 返回编辑":"使用此状态",()=>{var selected=item.Id;Close();if(selected!=current)choose(selected);},240);
        button.gameObject.name="ConfirmState";CustomCardControls.Style(button,CardControlKind.Primary);
    }
}
