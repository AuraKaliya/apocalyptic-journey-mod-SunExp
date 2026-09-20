using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.CustomCards;

public static class CustomCardWorkshop
{
    public static void Show(Transform parent)
    {
        CustomCardWorkshopController? controller=null;
        var window=CustomCardWorkshopController.Overlay("AuraTools.CustomCards",parent,"卡牌工坊",fullWindow:true,canClose:()=>controller==null||controller.SaveBeforeClose());
        controller=window.AddComponent<CustomCardWorkshopController>();controller.Build(window.transform);
    }
}

internal sealed class CustomCardWorkshopController : MonoBehaviour
{
    private CustomCardDocument doc=new();
    private readonly List<string> undo=new(),redo=new();
    private CustomCardGraphEditor? graphEditor;
    private Transform root=null!,content=null!;
    private TMP_Text status=null!;
    private TMP_Text? documentTitle;
    private TMP_Text? saveState;
    private readonly Dictionary<int,Button> workspaceTabs=new();
    private Button craft=null!;
    private Button undoButton=null!,redoButton=null!,previewButton=null!;
    private Button moreButton=null!;
    private GameObject? sideRoot;
    private CustomCardPreview sidePreview=null!;
    private TMP_Text previewIssue=null!;
    private CustomCardCompilation? compilation;
    private string librarySearch="";
    private bool libraryTemplates;
    private Transform workspace=null!;
    private int tab;
    private bool dirty;
    private bool rebuilding;
    private bool viewPending;
    private int renderedTab=-1;
    private readonly Dictionary<int,float> scrollPositions=new();
    private readonly Dictionary<string,float> innerScrollPositions=new();
    private bool previewCollapsed;
    private int editorTab;
    private string graphDocumentId="";
    private string[] graphSelection=Array.Empty<string>();

    internal void Build(Transform parent)
    {
        root=parent;
        var windowLayout=parent.GetComponent<VerticalLayoutGroup>();windowLayout.padding=new(0,0,0,0);windowLayout.spacing=0;
        var header=parent.Find("Header");var close=header.GetComponentsInChildren<Button>().FirstOrDefault();
        var headerLayout=header.GetComponent<HorizontalLayoutGroup>();headerLayout.padding=new(20,20,10,10);headerLayout.spacing=8;
        var title=header.GetComponentInChildren<TMP_Text>();title.GetComponent<LayoutElement>().flexibleWidth=0;title.GetComponent<LayoutElement>().preferredWidth=100;
        var mark=CustomCardUi.CreateRect("WorkshopMark",header,new(.5f,.5f),new(.5f,.5f),new(.5f,.5f),new(18,18));CustomCardUi.SetFixedSize(mark,20,20);var glyph=mark.AddComponent<CustomCardIcon>();glyph.Icon=CardIcon.Layers;glyph.color=CustomCardVisuals.Gold;glyph.raycastTarget=false;mark.transform.SetAsFirstSibling();
        title.transform.SetSiblingIndex(1);
        if(close!=null)Quiet(Button(header,"返回",()=>close.onClick.Invoke(),56)).transform.SetAsFirstSibling();
        documentTitle=CustomCardUi.AddTmpText(header,"",16,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,36,0,160);
        documentTitle.textWrappingMode=TextWrappingModes.NoWrap;
        saveState=CustomCardUi.AddTmpText(header,"",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,36,0,52);
        Spacer(header);
        Quiet(Button(header,"作品库",Library,84));Button(header,"保存",Save,56);
        craft=Button(header,"制作到仓库",Craft,124);CustomCardControls.Style(craft,CardControlKind.Primary);
        moreButton=CustomCardControls.IconButton(header,CardIcon.More,"更多",DocumentMenu);if(close!=null)close.transform.SetAsLastSibling();
        CustomCardUi.SetFixedHeight(header.gameObject,60);CustomCardControls.Rule(parent);
        var tabs=CommandRow(parent,"Tabs");tabs.GetComponent<HorizontalLayoutGroup>().padding=new(20,20,0,0);tabs.GetComponent<HorizontalLayoutGroup>().spacing=8;
        CustomCardUi.SetFixedHeight(tabs.gameObject,48);
        foreach(var item in new[]{(0,"效果蓝图"),(1,"卡牌属性"),(2,"卡面")})
        {int index=item.Item1;workspaceTabs[index]=Button(tabs,item.Item2,()=>Navigate(index),index<2?78:48,48);}
        Spacer(tabs);
        Quiet(Button(tabs,"节点指南",()=>Guide(),88));
        undoButton=CustomCardControls.IconButton(tabs,CardIcon.Undo,"撤销",()=>History(undo,redo));redoButton=CustomCardControls.IconButton(tabs,CardIcon.Redo,"重做",()=>History(redo,undo));
        CustomCardControls.IconButton(tabs,CardIcon.Panel,"预览侧栏",()=>{previewCollapsed=!previewCollapsed;UpdateSidebarVisibility();});
        previewButton=CustomCardControls.IconButton(tabs,CardIcon.Expand,"预览",Preview);CustomCardControls.Rule(parent);
        var body=CustomCardUi.CreateLayout("CustomCardWorkspace",parent);workspace=body.transform;body.AddComponent<LayoutElement>().flexibleHeight=1;
        var workspaceLayout=body.AddComponent<HorizontalLayoutGroup>();workspaceLayout.spacing=0;workspaceLayout.childControlWidth=true;workspaceLayout.childControlHeight=true;workspaceLayout.childForceExpandWidth=false;workspaceLayout.childForceExpandHeight=true;
        content=CustomCardUi.CreateScroll(workspace,"CustomCards");
        var side=Column(workspace,"CustomCardSidePreview");sideRoot=side.gameObject;
        var sideLayout=sideRoot.AddComponent<LayoutElement>();sideLayout.minWidth=0;sideLayout.preferredWidth=248;sideLayout.flexibleWidth=0;
        side.GetComponent<VerticalLayoutGroup>().padding=new(16,16,20,20);CustomCardUi.AddImage(sideRoot,CustomCardVisuals.Node);
        var heading=CommandRow(side,"PreviewHeading");CustomCardUi.AddTmpText(heading,"实时预览",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,36,1);CustomCardControls.IconButton(heading,CardIcon.Expand,"放大",Preview);
        sidePreview=CustomCardPreview.Create(side,320,Preview);previewIssue=CustomCardUi.AddTmpText(side,"",13,TextAnchor.UpperLeft,CustomCardVisuals.Error,60);
        CustomCardControls.Rule(parent);status=CustomCardUi.AddTmpText(parent,"",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,24);status.margin=new(20,0,20,0);
        Render();
    }
    private void Navigate(int page){if(!CustomCardInputFeedback.CommitAll(root))return;CustomCardUiLifetime.ReleaseFocus(root);if(tab<=2)editorTab=tab;tab=page;Render();}
    private void Guide(CardGraphNode? node=null)=>CustomCardGuide.Show(root,root,node);
    private void Render()
    {
        if(rebuilding || this==null)return;
        rebuilding=true;
        var scroll=content.GetComponentInParent<ScrollRect>();
        foreach(var inner in content.GetComponentsInChildren<ScrollRect>())innerScrollPositions[renderedTab+"/"+inner.name]=inner.verticalNormalizedPosition;
        if(renderedTab>=0&&scroll!=null)scrollPositions[renderedTab]=scroll.content.rect.height>scroll.viewport.rect.height?scroll.verticalNormalizedPosition:1;
        var position=scrollPositions.TryGetValue(tab,out var savedPosition)?savedPosition:1;renderedTab=tab;
        if(graphEditor!=null&&graphDocumentId==doc.Id)graphSelection=graphEditor.Selection.ToArray();
        CustomCardUiLifetime.Clear(content);
        var contentLayout=content.GetComponent<VerticalLayoutGroup>();contentLayout.childForceExpandWidth=true;contentLayout.childAlignment=TextAnchor.UpperLeft;contentLayout.spacing=16;contentLayout.padding=tab==0?new RectOffset():new(24,14,20,20);
        graphEditor=null;
        foreach(var item in workspaceTabs)CustomCardControls.Style(item.Value,item.Key==tab?CardControlKind.ActiveTab:CardControlKind.Tab);
        UpdateSidebarVisibility();
        switch(tab){case 0:Rules();break;case 1:Basics();break;case 2:Artwork();break;case 6:LibraryPage();break;default:Scripts();break;}
        UpdateSidebarVisibility();RefreshState();
        rebuilding=false;
        Canvas.ForceUpdateCanvases();if(scroll!=null)scroll.verticalNormalizedPosition=position;
        foreach(var inner in content.GetComponentsInChildren<ScrollRect>())if(innerScrollPositions.TryGetValue(tab+"/"+inner.name,out var innerPosition))inner.verticalNormalizedPosition=innerPosition;
    }
    private void QueueRender()
    {
        if(viewPending)return;viewPending=true;StartCoroutine(NextRender());
    }
    private IEnumerator NextRender(){yield return null;viewPending=false;Render();}
    internal void Change(Action action,bool render=true)
    {
        if(!CustomCardInputFeedback.CommitAll(root))return;
        Record();action();dirty=true;
        if(render)QueueRender();else RefreshState();
    }
    internal void Record()
    {
        undo.Add(JsonConvert.SerializeObject(doc));if(undo.Count>40)undo.RemoveAt(0);redo.Clear();dirty=true;
    }
    private void History(List<string> from,List<string> to)
    {
        if(from.Count==0)return;
        to.Add(JsonConvert.SerializeObject(doc));var revision=doc.Revision;
        doc=JsonConvert.DeserializeObject<CustomCardDocument>(from[from.Count-1])!;doc.Revision=revision;from.RemoveAt(from.Count-1);dirty=true;Render();
    }
    private void RefreshState()
    {
        if(documentTitle!=null){documentTitle.text=doc.Name;documentTitle.GetComponent<LayoutElement>().preferredWidth=Mathf.Min(documentTitle.GetPreferredValues(doc.Name).x+4,Mathf.Max(60,Mathf.Min(320,((RectTransform)root).rect.width-760)));}
        if(saveState!=null)saveState.text=dirty?"未保存":doc.Revision>0?"已保存":"新作品";
        var validation=compilation=CustomCardCompiler.Compile(doc,CustomCardNative.BuffName);
        craft.interactable=validation.Success;
        status.text=validation.Success?"":validation.Issues.FirstOrDefault()?.Message??"无法制作";
        status.color=CustomCardVisuals.Error;
        undoButton.interactable=undo.Count>0;redoButton.interactable=redo.Count>0;
        previewIssue.text=validation.Success?"":validation.Issues.FirstOrDefault()?.Message??"";
        previewIssue.gameObject.SetActive(!validation.Success);
        sidePreview.Bind(doc,validation);
    }
    private void Report(string message,bool error=false){status.text=message;status.color=error?CustomCardUi.ErrorText:CustomCardUi.SuccessText;}
    internal void InputStateChanged()
    {
        if(rebuilding||root==null)return;
        bool invalid=root.GetComponentsInChildren<CustomCardInputFeedback>().Any(f=>f.HasError);
        if(craft!=null)craft.interactable=compilation?.Success==true&&!invalid;
        if(graphEditor!=null)graphEditor.SetInputError(invalid);
    }
    private void Do(Action action){try{action();}catch(Exception ex){Report(ex.Message,true);}}
    private void Save()=>SaveCurrent();
    private bool SaveCurrent()
    {
        if(!CustomCardInputFeedback.CommitAll(root))return false;CustomCardUiLifetime.ReleaseFocus(root);
        try{CustomCardLibrary.Save(doc);dirty=false;RefreshState();return true;}
        catch(Exception ex){Report("保存失败："+ex.Message,true);return false;}
    }
    internal bool SaveBeforeClose()
    {
        if(!CustomCardInputFeedback.CommitAll(root))return false;
        CustomCardUiLifetime.ReleaseFocus(root);
        if(graphEditor!=null&&graphEditor.CancelInteraction())return false;
        if(!dirty)return true;
        try{CustomCardLibrary.Save(doc);dirty=false;return true;}
        catch(Exception ex){Report("草稿保存失败，窗口保持打开："+ex.Message,true);return false;}
    }
    private void Craft()
    {
        if(!CustomCardInputFeedback.CommitAll(root))return;
        CustomCardUiLifetime.ReleaseFocus(root);
        Do(()=>{CustomCardNative.Craft(doc);Report("已将「"+doc.Name+"」制作到仓库。");});
    }
    private void Export(){Do(()=>{var path=CustomCardLibrary.Export(doc);GUIUtility.systemCopyBuffer=path;Report("已导出含像素卡面的作品，路径已复制："+path);});}
    private void Import()
    {
        OptionalFileDialog.PickFileAsync("导入自建卡牌",new[]{new OptionalFileDialogFilter("自建卡牌作品","*.auracard.json;*.json")},"json",Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),result=>
        {if(this==null)return;if(result.Selected)Do(()=>Switch(CustomCardLibrary.Import(result.Path)));else if(result.Status!=OptionalFileDialogStatus.Cancelled)Report(result.Message,true);});
    }
    private void Switch(CustomCardDocument next)
    {
        void Apply(){doc=next;undo.Clear();redo.Clear();graphSelection=Array.Empty<string>();tab=editorTab=0;dirty=next.Revision==0;Render();}
        if(!dirty){Apply();return;}
        var window=Overlay("CustomCards.Switch",root,"切换作品",maxWidth:620,preferredHeight:250);
        Hint(window.transform,"「"+doc.Name+"」有未保存的修改。");var row=Row(window.transform,"SwitchActions");
        void Close()=>CustomCardUiLifetime.Destroy(window.transform.parent.gameObject);
        Button(row,"保存并切换",()=>{if(SaveCurrent()){Close();Apply();}},140);
        Quiet(Button(row,"丢弃并切换",()=>{Close();Apply();},130));Quiet(Button(row,"取消",Close,70));
    }
    private void Library()
    {
        Navigate(6);
    }
    private void LibraryPage()
    {
        var toolbar=Row(content,"LibraryToolbar");
        Quiet(Button(toolbar,"返回编辑",()=>Navigate(editorTab),94));
        CustomCardControls.Segments(toolbar,new[]{"我的作品","内置模板"},libraryTemplates?1:0,i=>{libraryTemplates=i==1;QueueRender();},110,false);
        Button(toolbar,"新建卡牌",()=>Switch(new()),110);Quiet(Button(toolbar,"导入作品",Import,96));
        var input=CustomCardUi.AddTmpInput(content,librarySearch,"搜索作品",_=>{},320);CustomCardFormStyle.Input(input);
        var list=Column(content,"LibraryItems");
        void Fill()
        {
            CustomCardUiLifetime.Clear(list);
            var documents=libraryTemplates?Enumerable.Range(0,CardBlueprintTemplates.Names.Length).Select(CardBlueprintTemplates.Create):CustomCardLibrary.List();
            var matches=documents.Where(i=>i.Name.IndexOf(librarySearch,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            if(matches.Length==0){Hint(list,librarySearch.Length>0?"没有匹配的作品。":"还没有作品，从新建卡牌或内置模板开始。");return;}
            foreach(var item in matches)
            {
                var entry=CustomCardUi.CreateLayout("LibraryItem",list);var layout=entry.AddComponent<HorizontalLayoutGroup>();layout.spacing=20;layout.padding=new(16,16,12,12);layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
                CustomCardUi.AddImage(entry,CustomCardVisuals.Node);
                var preview=CustomCardPreview.Create(entry.transform,190,()=>PreviewDocument(item));var previewSize=preview.GetComponent<LayoutElement>();previewSize.minWidth=previewSize.preferredWidth=140;previewSize.flexibleWidth=0;
                var compiled=CustomCardCompiler.Compile(item,CustomCardNative.BuffName);preview.Bind(item,compiled);
                var details=Column(entry.transform,"Details");details.gameObject.AddComponent<LayoutElement>().flexibleWidth=1;
                CustomCardUi.AddTmpText(details,item.Name,18,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,32);
                Hint(details,item.Cost+" 能量 · "+(item.Targeted?"攻击牌":"技能牌"));
                var summary=CustomCardUi.AddTmpText(details,compiled.Success?compiled.Description:compiled.Issues.FirstOrDefault()?.Message??"效果尚未完成",14,TextAnchor.UpperLeft,CustomCardVisuals.Muted,64);summary.overflowMode=TextOverflowModes.Ellipsis;
                var actions=Row(details,"LibraryActions");
                Button(actions,libraryTemplates?"从模板新建":"编辑",()=>Switch(item),libraryTemplates?132:72);
                if(!libraryTemplates)
                {Quiet(Button(actions,"复制",()=>Switch(item.Duplicate()),64));Quiet(Button(actions,"删除",()=>Confirm(root,"CustomCards.Delete","删除设计稿","已制作的卡牌会保留。","删除",()=>Do(()=>{CustomCardLibrary.Delete(item);if(doc.Id==item.Id){doc=new();dirty=false;undo.Clear();redo.Clear();}QueueRender();})),64));}
            }
        }
        input.onValueChanged.RemoveAllListeners();input.onValueChanged.AddListener(v=>{librarySearch=v;Fill();});Do(Fill);
    }
    private void Basics()
    {
        var layout=content.GetComponent<VerticalLayoutGroup>();layout.childForceExpandWidth=false;layout.childAlignment=TextAnchor.UpperCenter;
        var basic=Section(content,"基础属性");
        basic.gameObject.AddComponent<LayoutElement>().preferredWidth=780;
        Field(basic,"卡牌名称",doc.Name,v=>Change(()=>doc.Name=v,false));
        var row=TwoColumns(basic,"CostAndRarity");
        var cost=FlexibleColumn(row,"Cost");CustomCardUi.AddTmpText(cost,"能量费用",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,18);
        var stepper=CommandRow(cost,"CostStepper");CustomCardUi.AddImage(stepper.gameObject,CustomCardVisuals.Well);CustomCardControls.Border(stepper.gameObject,CustomCardVisuals.Border);
        Quiet(Button(stepper,"−",()=>Change(()=>doc.Cost=Math.Max(0,doc.Cost-1)),32));
        var costInput=NumberInput(stepper,doc.Cost,v=>Change(()=>doc.Cost=double.IsNaN(v)?-1:(int)v,false),70);
        CustomCardFormStyle.Input(costInput);costInput.textComponent.alignment=TextAlignmentOptions.Center;costInput.targetGraphic.color=Color.clear;costInput.transform.Find("ControlBorder").gameObject.SetActive(false);
        CustomCardInputFeedback.Attach(costInput).SetSurface(stepper.gameObject);
        CustomCardInputFeedback.BindNumber(costInput,CardNumber.Plain(doc.Cost),v=>Change(()=>doc.Cost=(int)v,false),true,0,99);
        Quiet(Button(stepper,"＋",()=>Change(()=>doc.Cost++),32));
        var rarity=FlexibleColumn(row,"Rarity");CustomCardUi.AddTmpText(rarity,"稀有度",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,18);
        Select(rarity,new[]{"普通","稀有","传奇"},doc.Rarity-1,i=>Change(()=>doc.Rarity=i+1),160).GetComponent<LayoutElement>().flexibleWidth=1;
        CustomCardUi.AddTmpText(basic,"卡牌类型",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,18);CustomCardControls.Segments(basic,new[]{"攻击牌","技能牌"},doc.Targeted?0:1,i=>Change(()=>doc.Targeted=i==0),140);
        CustomCardControls.Rule(content);
        var traits=Section(content,"特性与风味");
        traits.gameObject.AddComponent<LayoutElement>().preferredWidth=780;
        row=Row(traits,"Tags");CustomCardControls.Check(row,"焚毁",doc.Burnout,v=>Change(()=>doc.Burnout=v,false));CustomCardControls.Check(row,"保留",doc.Retain,v=>Change(()=>doc.Retain=v,false));
        Field(traits,"风味文字 · 可选",doc.Note,v=>Change(()=>doc.Note=v,false),80);
    }
    private void Rules()
    {
        if(doc.MigrationReview.Count>0)
        {
            Hint(content,"迁移待修订："+string.Join("；",doc.MigrationReview),CustomCardUi.ErrorText);
            Button(content,"已检查，采用蓝图中的明确顺序",()=>Change(()=>doc.MigrationReview.Clear()),330);
        }
        var host=CustomCardUi.CreateLayout("BlueprintEditor",content);
        graphEditor=host.AddComponent<CustomCardGraphEditor>();
        graphEditor.Build(doc,Record,change=>
        {
            // Fitting, zooming and locating only change the viewport. They must not
            // enroll the untouched starting document in close/interruption autosave.
            if(change==CustomCardGraphChange.View)return;
            if(change==CustomCardGraphChange.Content){dirty=true;RefreshState();}
            else {dirty=true;if(saveState!=null)saveState.text="未保存";undoButton.interactable=undo.Count>0;redoButton.interactable=redo.Count>0;}
        },(parent,current,picked)=>Do(()=>CustomCardStatePicker.Show(parent,current,picked)),message=>Report(message),Guide);
        if(graphDocumentId==doc.Id&&graphSelection.Length>0)graphEditor.RestoreSelection(graphSelection);
        graphDocumentId=doc.Id;
    }
    private void OpenIssue(string id)
    {
        if(tab!=0){tab=0;Render();}
        graphEditor?.FocusNode(id);
    }
    private void DocumentMenu()
    {
        var anchor=moreButton.transform;
        Action[] actions={ ()=>Switch(new()),()=>Switch(doc.Duplicate()),Import,Export,()=>Navigate(5),()=>Guide() };
        CustomCardPopover.Show(anchor,new[]{"新建卡牌","另存副本","导入作品","导出作品","查看生成脚本","节点指南"},-1,i=>actions[i]());
    }
    private void Artwork()
    {
        CustomCardControls.Segments(content,new[]{"自绘卡面","游戏卡面"},doc.Artwork.UsePixels?0:1,i=>Change(()=>doc.Artwork.UsePixels=i==0),140);
        if(!doc.Artwork.UsePixels)
        {
            var source=Section(content,"选择游戏卡面");
            Select(source,new[]{"元素升华","双刃剑盾","禁果"},Array.IndexOf(new[]{"Icon/Card/元素升华","Icon/Card/双刃剑盾","Icon/Card/禁果"},doc.Artwork.TemplateIcon),i=>Change(()=>doc.Artwork.TemplateIcon=new[]{"Icon/Card/元素升华","Icon/Card/双刃剑盾","Icon/Card/禁果"}[i]),270);
            return;
        }
        var host=Column(content,"EmbeddedPixelEditor");
        host.gameObject.AddComponent<CustomCardFillAvailable>().Configure(content.GetComponentInParent<ScrollRect>().viewport,96,320);
        host.gameObject.AddComponent<CardPixelEditorController>().Build(host,doc.Artwork,Record,()=>{dirty=true;RefreshState();});
    }
    private void Preview()
    {
        CustomCardUiLifetime.ReleaseFocus(root);PreviewDocument(doc);
    }
    private void PreviewDocument(CustomCardDocument document)
    {
        var window=Overlay("CustomCards.Preview",root,document.Name,maxWidth:520,preferredHeight:720);
        var preview=CustomCardPreview.Create(window.transform,560);preview.GetComponent<LayoutElement>().flexibleHeight=1;preview.GetComponent<LayoutElement>().minHeight=240;
        var result=CustomCardCompiler.Compile(document,CustomCardNative.BuffName);preview.Bind(document,result);
        if(!result.Success)Hint(window.transform,result.Issues.FirstOrDefault()?.Message??"效果尚未完成",CustomCardVisuals.Error);
    }
    private void Scripts()
    {
        Quiet(Button(content,"返回编辑",()=>Navigate(editorTab),94));
        var compilation=CustomCardCompiler.Compile(doc);
        if(!compilation.Success){foreach(var issue in compilation.Issues)Hint(content,issue.Message,CustomCardUi.ErrorText);return;}
        var row=Row(content,"ScriptActions");Button(row,"检查游戏 Lua 语法",()=>Do(()=>{CustomCardNative.CheckLua(compilation);Report("所有生成脚本的 Lua 语法检查通过；未执行战斗效果。");}),230);
        Button(row,"复制全部脚本",()=>{GUIUtility.systemCopyBuffer=string.Join("\n\n",compilation.Scripts.Select(p=>"-- "+p.Key+"\n"+p.Value));Report("生成脚本已复制。");},190);
        foreach(var pair in compilation.Scripts)
        {
            Hint(content,pair.Key);
            var text=CustomCardUi.AddTmpText(content,pair.Value,13,TextAnchor.UpperLeft,CustomCardUi.Text,Math.Max(70,pair.Value.Split('\n').Length*18));text.richText=false;
        }
    }
    private void OnDestroy()
    {
        if(dirty)try{CustomCardLibrary.Save(doc);}catch(Exception ex){AuraToolsLog.Warn("[CustomCard] interrupted draft save failed: "+ex.Message);}
    }
    private void OnRectTransformDimensionsChange()=>UpdateSidebarVisibility();
    private void UpdateSidebarVisibility()
    {
        if(sideRoot==null||root==null)return;float width=(root as RectTransform)?.rect.width??0;
        bool visible=(tab==1||tab==2)&&width>=680&&!previewCollapsed;
        var layout=sideRoot.GetComponent<LayoutElement>();layout.preferredWidth=width>=1200?300:width>=920?248:216;
        if(sideRoot.activeSelf!=visible)sideRoot.SetActive(visible);
        if(previewButton!=null)previewButton.gameObject.SetActive(!visible);
    }
    internal static Transform Row(Transform parent,string name)
    {
        var row=CustomCardUi.CreateLayout(name,parent);row.AddComponent<CustomCardFlowLayout>();return row.transform;
    }
    internal static Transform Column(Transform parent,string name)
    {
        var col=CustomCardUi.CreateLayout(name,parent);var layout=col.AddComponent<VerticalLayoutGroup>();layout.spacing=5;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;return col.transform;
    }
    internal static Button Button(Transform parent,string name,Action action,float width=120,float height=36)
    {
        // The workshop owns this skin and its feedback; the shared toolbox button has a separate theme state machine.
        var root=CustomCardUi.CreateLayout("CardAction."+name,parent);CustomCardUi.SetFixedSize(root,Math.Min(width,Math.Max(180,Screen.width-112)),height);
        var background=CustomCardUi.AddImage(root,Color.white);var button=root.AddComponent<Button>();button.targetGraphic=background;
        button.onClick.AddListener(()=>CustomCardUi.RunConfigAction(action));root.AddComponent<AuraUiButtonSoundRelay>().Configure(button,AuraUiButtonSoundStyle.Pure);
        var label=CustomCardUi.AddTmpText(root.transform,name,15,TextAnchor.MiddleCenter,CustomCardVisuals.Ink,height);label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
        label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=new(6,2);label.rectTransform.offsetMax=new(-6,-2);
        CustomCardVisuals.Button(button);return button;
    }
    internal static Button Quiet(Button button){CustomCardControls.Style(button,CardControlKind.Quiet);return button;}
    internal static GameObject Overlay(string name,Transform parent,string title,Action? close=null,float maxWidth=1180,Func<bool>? canClose=null,float preferredHeight=0,bool fullWindow=false)
    {
        var window=CustomCardUi.CreateOverlay(name,parent,title,close,maxWidth:maxWidth,canClose:canClose,fullWindow:fullWindow,preferredHeight:preferredHeight);window.transform.parent.gameObject.AddComponent<CustomCardFocusOwner>();CustomCardWindowStyle.Skin(window);return window;
    }
    internal static Transform Section(Transform parent,string title,string subtitle="")
    {
        var panel=Column(parent,"Section."+title);var layout=panel.GetComponent<VerticalLayoutGroup>();layout.padding=new RectOffset();layout.spacing=8;layout.childForceExpandWidth=true;
        var heading=CustomCardUi.AddTmpText(panel,title,16,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,24);
        if(subtitle.Length>0)CustomCardUi.AddTmpText(panel,subtitle,12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,22);
        return panel;
    }
    private static void Field(Transform parent,string title,string value,Action<string> change,float height=40)
    {
        CustomCardUi.AddTmpText(parent,title,12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,18);
        var input=CustomCardUi.AddTmpInput(parent,value,height>40?"为这张卡留下一句话…":title,_=>{},300,height);CustomCardFormStyle.Input(input);if(height>40){input.lineType=TMP_InputField.LineType.MultiLineNewline;input.textComponent.textWrappingMode=TextWrappingModes.Normal;input.textComponent.alignment=TextAlignmentOptions.TopLeft;}
        input.characterLimit=height>40?300:40;
        CustomCardInputFeedback.Bind(input,change,height>40?null:v=>string.IsNullOrWhiteSpace(v)?"请填写卡牌名称。":null);
    }
    internal static Transform CommandRow(Transform parent,string name)
    {
        var row=CustomCardUi.CreateLayout(name,parent);CustomCardUi.SetFixedHeight(row,40);var layout=row.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandWidth=false;layout.childForceExpandHeight=true;return row.transform;
    }
    internal static void Spacer(Transform parent){var go=CustomCardUi.CreateLayout("Spacer",parent);var e=go.AddComponent<LayoutElement>();e.minWidth=0;e.flexibleWidth=1;}
    internal static Transform TwoColumns(Transform parent,string name)
    {
        var row=CustomCardUi.CreateLayout(name,parent);var layout=row.AddComponent<HorizontalLayoutGroup>();layout.spacing=16;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;return row.transform;
    }
    internal static Transform FlexibleColumn(Transform parent,string name)
    {var result=Column(parent,name);var e=result.gameObject.AddComponent<LayoutElement>();e.minWidth=0;e.preferredWidth=0;e.flexibleWidth=1;return result;}
    private static void Confirm(Transform parent,string name,string title,string explanation,string confirm,Action action)
    {
        var window=Overlay(name,parent,title,maxWidth:560,preferredHeight:250);Hint(window.transform,explanation);var row=Row(window.transform,"ConfirmationActions");Button(row,"取消",()=>CustomCardUiLifetime.Destroy(window.transform.parent.gameObject),80);Button(row,confirm,()=>{action();CustomCardUiLifetime.Destroy(window.transform.parent.gameObject);},160);
    }
    internal static void Hint(Transform parent,string text,Color? color=null)
    {
        var label=CustomCardUi.AddTmpText(parent,text,15,TextAnchor.MiddleLeft,color??CustomCardVisuals.Muted,36);
        var layout=label.GetComponent<LayoutElement>();layout.minHeight=24;layout.preferredHeight=-1;layout.flexibleHeight=0;label.textWrappingMode=TextWrappingModes.Normal;
    }
    internal static void Label(Transform parent,string text,float width=130)=>CustomCardUi.AddTmpText(parent,text,16,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,40,0,width);
    internal static Button Select(Transform parent,IReadOnlyList<string> labels,int selected,Action<int> change,float width=230)=>CustomCardControls.Select(parent,labels,selected,change,width);
    internal static TMP_InputField NumberInput(Transform parent,double value,Action<double> change,float width=92)
    {
        var input=CustomCardUi.AddTmpInput(parent,value.ToString("R",CultureInfo.InvariantCulture),"数字",_=>{},width,40);
        CustomCardFormStyle.Input(input,false);
        CustomCardInputFeedback.BindNumber(input,CardNumber.Plain(value),change);
        return input;
    }
    private static void TextInput(Transform parent,string name,string value,Action<string> change,float width)
    {
        var row=Row(parent,name);Label(row,name,110);var input=CustomCardUi.AddTmpInput(row,value,name,_=>{},width,40);
        CustomCardInputFeedback.Bind(input,change);
    }
}
