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
        var window=AuraToolsUi.CreateOverlay("AuraTools.CustomCards",parent,"自建卡牌",maxWidth:1320f,canClose:()=>controller==null||controller.SaveBeforeClose());
        controller=window.AddComponent<CustomCardWorkshopController>();controller.Build(window.transform);
    }
}

internal sealed class CustomCardEditorScope
{
    internal bool Target;
    internal bool Current;
    internal readonly Dictionary<string,(string Name,bool Object)> Variables=new();
    internal CustomCardEditorScope Child() { var copy=new CustomCardEditorScope { Target=Target,Current=Current };foreach(var p in Variables)copy.Variables[p.Key]=p.Value;return copy; }
    internal Dictionary<string,string> Names => Variables.ToDictionary(p=>p.Key,p=>p.Value.Name);
}

internal sealed class CustomCardWorkshopController : MonoBehaviour
{
    private CustomCardDocument doc=new();
    private readonly List<string> undo=new(),redo=new();
    private readonly HashSet<string> folded=new();
    private Transform root=null!,content=null!;
    private TMP_Text status=null!,description=null!;
    private Button craft=null!;
    private Texture2D? previewTexture;
    private Texture2D? sideTexture;
    private GameObject? sideRoot;
    private RawImage? sideImage;
    private TMP_Text? sideTitle,sideDescription;
    private string sideArtKey="";
    private int tab;
    private bool dirty;
    private bool rebuilding;
    private readonly CardTrialInput trial=new();
    private string trialResult="设置模拟数据，然后点击试算。";
    private bool viewPending;
    private int renderedTab=-1;
    private readonly Dictionary<int,float> scrollPositions=new();

    internal void Build(Transform parent)
    {
        root=parent;
        var actions=Row(parent,"DocumentActions");
        Button(actions,"作品库",Library,90);Button(actions,"新建",()=>Switch(new()),74);
        Button(actions,"保存稿件",Save,96);Button(actions,"另存副本",()=>Switch(doc.Duplicate()),96);
        Button(actions,"导入",Import,74);Button(actions,"导出",Export,74);
        var tabs=Row(parent,"Tabs");
        var labels=new[]{"基本属性","效果积木","像素卡面","卡牌预览","试算","生成脚本"};
        for(int i=0;i<labels.Length;i++){int index=i;Button(tabs,labels[i],()=>{tab=index;Render();},96);}
        var workspace=AuraToolsUi.CreateLayout("CustomCardWorkspace",parent);workspace.AddComponent<LayoutElement>().flexibleHeight=1;
        var workspaceLayout=workspace.AddComponent<HorizontalLayoutGroup>();workspaceLayout.spacing=14;workspaceLayout.childControlWidth=true;workspaceLayout.childControlHeight=true;workspaceLayout.childForceExpandWidth=false;workspaceLayout.childForceExpandHeight=false;
        content=AuraToolsUi.CreateScroll(workspace.transform,"CustomCards");
        var side=AuraToolsUi.CreateScroll(workspace.transform,"CustomCardSidePreview");sideRoot=side.parent.parent.gameObject;
        var sideLayout=sideRoot.GetComponent<LayoutElement>();sideLayout.minWidth=280;sideLayout.preferredWidth=280;sideLayout.flexibleWidth=0;
        sideTitle=AuraToolsUi.AddTmpText(side,"",19,TextAnchor.MiddleLeft,AuraToolsUi.Text,66);
        var art=AuraToolsUi.CreateLayout("PreviewArt",side);AuraToolsUi.SetFixedHeight(art,256);
        var sideArt=AuraToolsUi.CreateRect("Image",art.transform,Vector2.zero,Vector2.one,new Vector2(0.5f,0.5f),Vector2.zero);
        var sideAspect=sideArt.AddComponent<AspectRatioFitter>();sideAspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;sideAspect.aspectRatio=1;
        sideImage=sideArt.AddComponent<RawImage>();sideImage.raycastTarget=false;
        sideDescription=AuraToolsUi.AddTmpText(side,"",15,TextAnchor.UpperLeft,AuraToolsUi.Text,280);
        UpdateSidebarVisibility();
        var footer=Row(parent,"Footer");
        Button(footer,"撤销",()=>History(undo,redo),74);Button(footer,"重做",()=>History(redo,undo),74);
        craft=Button(footer,"免费制作到仓库",Craft,164);
        status=AuraToolsUi.AddTmpText(parent,"",14,TextAnchor.MiddleLeft,AuraToolsUi.MutedText,54);
        Render();
    }
    private void Render()
    {
        if(rebuilding || this==null)return;
        rebuilding=true;
        var scroll=content.GetComponentInParent<ScrollRect>();
        if(renderedTab>=0&&scroll!=null)scrollPositions[renderedTab]=scroll.content.rect.height>scroll.viewport.rect.height?scroll.verticalNormalizedPosition:1;
        var position=scrollPositions.TryGetValue(tab,out var savedPosition)?savedPosition:1;renderedTab=tab;
        AuraToolsUi.ClearChildren(content);
        description=null!;
        switch(tab){case 0:Basics();break;case 1:Rules();break;case 2:Artwork();break;case 3:Preview();break;case 4:Trial();break;default:Scripts();break;}
        RefreshState();
        rebuilding=false;
        Canvas.ForceUpdateCanvases();if(scroll!=null)scroll.verticalNormalizedPosition=position;
    }
    private void QueueRender()
    {
        if(viewPending)return;viewPending=true;StartCoroutine(NextRender());
    }
    private IEnumerator NextRender(){yield return null;viewPending=false;Render();}
    internal void Change(Action action,bool render=true)
    {
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
        var validation=CustomCardCompiler.Compile(doc);
        craft.interactable=validation.Success;
        status.text=validation.Success ? (dirty?"有修改，关闭时自动保存草稿。":doc.Revision>0?"设计稿已保存。":"新作品，开始编辑后可保存草稿。")+" 制作不消耗货币。" : string.Join("\n",validation.Issues.Take(2).Select(i=>i.Message));
        status.color=validation.Success ? AuraToolsUi.MutedText : AuraToolsUi.ErrorText;
        if(sideTitle!=null)sideTitle.text=doc.Name+"\n费用 "+doc.Cost+" · "+(doc.Targeted?"攻击牌":"技能牌");
        if(sideDescription!=null)
        {
            sideDescription.text=CustomCardDescription.Describe(doc);
            AuraToolsUi.SetFixedHeight(sideDescription.gameObject,Math.Max(220,sideDescription.text.Length/16*22+sideDescription.text.Count(c=>c=='\n')*22));
        }
        var artKey=JsonConvert.SerializeObject(doc.Artwork);
        if(sideImage!=null&&sideArtKey!=artKey)
        {
            sideArtKey=artKey;if(sideTexture!=null)Destroy(sideTexture);sideTexture=null;
            try
            {
                if(doc.Artwork.UsePixels){sideTexture=CustomCardArtworkRuntime.Texture(doc.Artwork);sideImage.texture=sideTexture;}
                else sideImage.texture=AuraToolsResourceCache.Load<Texture>(doc.Artwork.TemplateIcon,true);
            }
            catch(Exception){sideImage.texture=null;}
        }
    }
    private void Report(string message,bool error=false){status.text=message;status.color=error?AuraToolsUi.ErrorText:AuraToolsUi.SuccessText;}
    private void Do(Action action){try{action();}catch(Exception ex){Report(ex.Message,true);}}
    private void Save(){Do(()=>{CustomCardLibrary.Save(doc);dirty=false;RefreshState();});}
    internal bool SaveBeforeClose()
    {
        if(!dirty)return true;
        try{CustomCardLibrary.Save(doc);dirty=false;return true;}
        catch(Exception ex){Report("草稿保存失败，窗口保持打开："+ex.Message,true);return false;}
    }
    private void Craft()
    {
        Do(()=>{var card=CustomCardNative.Craft(doc);Report("已制作「"+doc.Name+"」并存入账号仓库。费用为 0。" );});
    }
    private void Export(){Do(()=>{var path=CustomCardLibrary.Export(doc);GUIUtility.systemCopyBuffer=path;Report("已导出含像素卡面的作品，路径已复制："+path);});}
    private void Import()
    {
        OptionalFileDialog.PickFileAsync("导入自建卡牌",new[]{new OptionalFileDialogFilter("自建卡牌作品","*.auracard.json;*.json")},"json",Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),result=>
        {if(this==null)return;if(result.Selected)Do(()=>Switch(CustomCardLibrary.Import(result.Path)));else if(result.Status!=OptionalFileDialogStatus.Cancelled)Report(result.Message,true);});
    }
    private void Switch(CustomCardDocument next)
    {
        void Apply(){doc=next;undo.Clear();redo.Clear();folded.Clear();dirty=next.Revision==0;Render();}
        if(dirty)AuraToolsUi.ShowConfirmation(root,"CustomCards.Switch","切换作品","当前修改尚未保存。切换后会丢弃这些修改。","丢弃并切换",Apply);
        else Apply();
    }
    private void Library()
    {
        Do(()=>
        {
            var window=AuraToolsUi.CreateOverlay("CustomCards.Library",root,"自建卡牌 · 设计稿库");
            var search="";
            var input=AuraToolsUi.AddTmpInput(window.transform,"","搜索作品名称",_=>{},420);
            var list=AuraToolsUi.CreateScroll(window.transform,"CustomCardLibrary");
            void Fill()
            {
                AuraToolsUi.ClearChildren(list);
                foreach(var item in CustomCardLibrary.List().Where(i=>i.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))
                {
                    var row=Row(list,"LibraryItem");Label(row,item.Name,240);
                    Button(row,"编辑",()=>{Switch(item);Destroy(window.transform.parent.gameObject);},80);
                    Button(row,"复制",()=>{Switch(item.Duplicate());Destroy(window.transform.parent.gameObject);},80);
                    Button(row,"删除",()=>AuraToolsUi.ShowConfirmation(window.transform,"CustomCards.Delete","删除设计稿","只删除设计稿，已制作卡牌不受影响。","删除设计稿",()=>Do(()=>{CustomCardLibrary.Delete(item);if(doc.Id==item.Id){doc=new();dirty=false;undo.Clear();redo.Clear();QueueRender();}Fill();})),80);
                }
            }
            input.onValueChanged.RemoveAllListeners();input.onValueChanged.AddListener(v=>{search=v;Fill();});Fill();
        });
    }
    private void Basics()
    {
        TextInput(content,"卡牌名称",doc.Name,v=>Change(()=>doc.Name=v,false),420);
        var row=Row(content,"Properties");Label(row,"使用费用",90);NumberInput(row,doc.Cost,v=>Change(()=>doc.Cost=double.IsNaN(v)?-1:(int)v,false));
        Select(row,new[]{"普通","稀有","传奇"},doc.Rarity-1,i=>Change(()=>doc.Rarity=i+1),130);
        Select(row,new[]{"技能牌 · 无需选中","攻击牌 · 需要选中"},doc.Targeted?1:0,i=>Change(()=>doc.Targeted=i==1),230);
        row=Row(content,"Tags");Label(row,"焚毁",70);AuraToolsUi.AddToggle(row,doc.Burnout,v=>Change(()=>doc.Burnout=v,false));Label(row,"保留",70);AuraToolsUi.AddToggle(row,doc.Retain,v=>Change(()=>doc.Retain=v,false));
        TextInput(content,"风味文字",doc.Note,v=>Change(()=>doc.Note=v,false),500);
        Hint(content,"制作免费。属性不决定效果点数；规则只校验对象、数据、执行范围和游戏可用性。");
    }
    private void Rules()
    {
        Hint(content,"点击积木设置参数；拖动同组的 ≡ 手柄调整顺序。数值按钮可切换为战斗数据或公式。");
        foreach(var rule in doc.Rules.ToArray())
        {
            var header=Row(content,"Rule."+rule.Id);AuraUiStableId.Assign(header.gameObject,rule.Id);
            Select(header,CustomCardNames.Triggers,(int)rule.Trigger,i=>Change(()=>rule.Trigger=(CardRuleTrigger)i),250);
            if(rule.Trigger>=CardRuleTrigger.AfterUseRoundStart){Label(header,"本场最多次数",112);NumberInput(header,rule.MaximumTriggers,v=>Change(()=>rule.MaximumTriggers=double.IsNaN(v)?-1:(int)v,false));}
            if(rule.Trigger>=CardRuleTrigger.AfterUseRoundStart){Label(header,"每回合最多",100);NumberInput(header,rule.MaximumTriggersPerRound,v=>Change(()=>rule.MaximumTriggersPerRound=double.IsNaN(v)?-1:(int)v,false));}
            Button(header,"删除规则",()=>Change(()=>doc.Rules.Remove(rule)),100);
            if(rule.Trigger>=CardRuleTrigger.AfterUseRoundStart)Hint(content,"每次使用分别建立一组监听；0 次表示本场不限次数。战斗结束自动清理。");
            if(rule.Trigger==CardRuleTrigger.AfterUseHurt)Hint(content,"扣除生命后检查条件；护盾完全挡住的伤害不会触发。");
            ExpressionButton(content,"触发条件",rule.Condition,true,new CustomCardEditorScope { Target=doc.Targeted&&rule.Trigger==CardRuleTrigger.Use },v=>rule.Condition=v,QueueRender);
            RenderBlocks(content,rule.Blocks,new CustomCardEditorScope { Target=doc.Targeted&&rule.Trigger==CardRuleTrigger.Use },0);
        }
        Button(content,"＋ 添加触发规则",()=>Change(()=>doc.Rules.Add(new())),200);
        var validation=CustomCardCompiler.Compile(doc);
        foreach(var issue in validation.Issues)
        {
            Button(content,"⚠ "+issue.Message,()=>OpenIssue(issue.NodeId),Math.Max(300,(content as RectTransform)?.rect.width??600),46);
        }
    }
    private void OpenIssue(string id)
    {
        bool Find(List<CardRuleBlock> list,CustomCardEditorScope scope)
        {
            foreach(var b in list)
            {
                if(b.Id==id){BlockEditor(b,scope);return true;}
                var child=scope.Child();if(b.Kind==CardBlockKind.ForEach)child.Current=true;
                if(Find(b.Then,child)||Find(b.Else,scope.Child()))return true;
                if(b.Kind==CardBlockKind.RememberNumber||b.Kind==CardBlockKind.RememberObject)scope.Variables[b.Id]=(b.VariableName,b.Kind==CardBlockKind.RememberObject);
            }
            return false;
        }
        foreach(var r in doc.Rules)if(Find(r.Blocks,new(){Target=doc.Targeted&&r.Trigger==CardRuleTrigger.Use}))return;
        Report("请检查本组触发条件或卡牌基本属性。",true);
    }
    private void RenderBlocks(Transform parent,List<CardRuleBlock> blocks,CustomCardEditorScope scope,int depth)
    {
        if(depth>CustomCardCompiler.MaximumDepth){Hint(parent,"嵌套过深",AuraToolsUi.ErrorText);return;}
        foreach(var block in blocks.ToArray())
        {
            var section=Column(parent,"Block."+block.Id);AuraUiStableId.Assign(section.gameObject,block.Id);
            section.GetComponent<VerticalLayoutGroup>().padding=new RectOffset(10+depth*3,6,5,5);
            AuraToolsUi.AddImage(section.gameObject,depth%2==0?AuraToolsUi.Row:AuraToolsUi.Panel).raycastTarget=false;
            var row=Row(section,"BlockActions");
            var captured=scope.Child();
            var handle=Button(row,"≡",()=>{},36);var drag=handle.gameObject.AddComponent<CustomCardBlockDrag>();drag.Owner=this;drag.Blocks=blocks;drag.Block=block;
            var drop=row.gameObject.AddComponent<CustomCardBlockDrag>();drop.Owner=this;drop.Blocks=blocks;drop.Block=block;
            Button(row,folded.Contains(block.Id)?"展开":"折叠",()=>{if(!folded.Add(block.Id))folded.Remove(block.Id);Render();},66);
            Button(row,"设置",()=>BlockEditor(block,captured),66);
            Button(row,"↑",()=>Move(blocks,block,-1),36);Button(row,"↓",()=>Move(blocks,block,1),36);
            Button(row,"复制",()=>Change(()=>{var copy=CopyBlock(block);blocks.Insert(blocks.IndexOf(block)+1,copy);}),66);
            Button(row,"删除",()=>Change(()=>blocks.Remove(block)),66);
            var summary=CustomCardDescription.Block(block,scope.Names);
            Hint(section,summary);
            if(!folded.Contains(block.Id))
            {
                if(block.Kind==CardBlockKind.If||block.Kind==CardBlockKind.ForEach||block.Kind==CardBlockKind.Repeat)
                {
                    var child=scope.Child();if(block.Kind==CardBlockKind.ForEach)child.Current=true;
                    RenderBlocks(section,block.Then,child,depth+1);
                    if(block.Kind==CardBlockKind.If){Hint(section,"否则");RenderBlocks(section,block.Else,scope.Child(),depth+1);}
                }
            }
            if(block.Kind==CardBlockKind.RememberNumber||block.Kind==CardBlockKind.RememberObject)scope.Variables[block.Id]=(block.VariableName,block.Kind==CardBlockKind.RememberObject);
        }
        Select(parent,new[]{"＋ 添加积木"}.Concat(CustomCardNames.Blocks).ToArray(),0,index=>
        {
            if(index==0)return;
            Change(()=>
            {
                var block=new CardRuleBlock { Kind=(CardBlockKind)(index-1),Object=new(){Kind=scope.Target?CardObjectKind.Target:CardObjectKind.Self} };
                if(block.Kind==CardBlockKind.ForEach){block.Object.Kind=CardObjectKind.Enemies;block.Condition=new(){Kind=CardValueKind.Exists,Object=new(){Kind=CardObjectKind.Current}};}
                if(block.Kind==CardBlockKind.Repeat)block.Value=CardValue.Constant(2);
                blocks.Add(block);
            });
        },230);
    }
    private void Move(List<CardRuleBlock> blocks,CardRuleBlock block,int delta)
    {
        int i=blocks.IndexOf(block),j=i+delta;if(i<0||j<0||j>=blocks.Count)return;
        Change(()=>{blocks.RemoveAt(i);blocks.Insert(j,block);});
    }
    internal void DropBlock(List<CardRuleBlock> blocks,CardRuleBlock source,CardRuleBlock target)
    {
        if(source==target||!blocks.Contains(source)||!blocks.Contains(target))return;
        Change(()=>{int i=blocks.IndexOf(target);blocks.Remove(source);blocks.Insert(Math.Min(i,blocks.Count),source);});
    }
    private static CardRuleBlock CopyBlock(CardRuleBlock source)
    {
        var copy=JsonConvert.DeserializeObject<CardRuleBlock>(JsonConvert.SerializeObject(source))!;
        var ids=new Dictionary<string,string>();
        void Ids(CardRuleBlock b){var id=Guid.NewGuid().ToString("N");ids[b.Id]=id;b.Id=id;foreach(var x in b.Then.Concat(b.Else))Ids(x);}
        void Ref(CardObjectReference o){if(ids.TryGetValue(o.VariableId,out var id))o.VariableId=id;}
        void Value(CardValue v){if(ids.TryGetValue(v.VariableId,out var id))v.VariableId=id;Ref(v.Object);foreach(var x in v.Inputs)Value(x);}
        void Fix(CardRuleBlock b){Ref(b.Object);Value(b.Value);Value(b.Condition);foreach(var x in b.Then.Concat(b.Else))Fix(x);}
        Ids(copy);Fix(copy);return copy;
    }
    private void BlockEditor(CardRuleBlock block,CustomCardEditorScope scope)
    {
        var window=AuraToolsUi.CreateOverlay("CustomCards.BlockEditor",root,"积木参数 · "+CustomCardNames.Name(block.Kind,CustomCardNames.Blocks),()=>QueueRender(),maxWidth:1000);
        var panel=AuraToolsUi.CreateScroll(window.transform,"CardBlockParameters");
        void Fill()
        {
            AuraToolsUi.ClearChildren(panel);
            if(block.Kind==CardBlockKind.Effect)
            {
                Select(panel,CustomCardNames.Effects,(int)block.Effect,i=>{Change(()=>{block.Effect=(CardEffectKind)i;if(CustomCardNames.PlayerEffect(block.Effect))block.Object=new(){Kind=CardObjectKind.Self};},false);Fill();},350);
                ObjectChoice(panel,block.Object,scope,false,()=>Fill(),o=>block.Object=o);
                if(CustomCardNames.HasAmount(block.Effect))ExpressionButton(panel,"数量",block.Value,false,scope,v=>block.Value=v,Fill);
                if(CustomCardNames.NeedsBuff(block.Effect))Button(panel,"状态："+(string.IsNullOrEmpty(block.ResourceId)?"点击选择":block.ResourceId),()=>BuffPicker(window.transform,id=>{Change(()=>block.ResourceId=id,false);Fill();}),450);
            }
            else if(block.Kind==CardBlockKind.If)ExpressionButton(panel,"条件",block.Condition,true,scope,v=>block.Condition=v,Fill);
            else if(block.Kind==CardBlockKind.Repeat)ExpressionButton(panel,"重复次数",block.Value,false,scope,v=>block.Value=v,Fill);
            else if(block.Kind==CardBlockKind.ForEach)
            {
                ObjectChoice(panel,block.Object,scope,false,Fill,o=>block.Object=o);var child=scope.Child();child.Current=true;
                ExpressionButton(panel,"对象筛选条件",block.Condition,true,child,v=>block.Condition=v,Fill);
            }
            else
            {
                TextInput(panel,"变量名称",block.VariableName,v=>Change(()=>block.VariableName=v,false),380);
                if(block.Kind==CardBlockKind.RememberNumber)ExpressionButton(panel,"记住数值",block.Value,false,scope,v=>block.Value=v,Fill);
                else ObjectChoice(panel,block.Object,scope,true,Fill,o=>block.Object=o);
                Hint(panel,"变量可用于后续步骤及其子分支；本次触发结束后清除。");
            }
            Hint(panel,CustomCardDescription.Block(block,scope.Names));
        }
        Fill();
    }
    private void ExpressionButton(Transform parent,string label,CardValue value,bool boolean,CustomCardEditorScope scope,Action<CardValue> set,Action refresh)
    {
        Hint(parent,label+"：");
        Button(parent,CustomCardDescription.Value(value,scope.Names),()=>ExpressionEditor(parent,value,boolean,scope,set,refresh),Math.Min(620,Math.Max(300,(parent as RectTransform)?.rect.width??500)),48);
    }
    private void ExpressionEditor(Transform parent,CardValue value,bool boolean,CustomCardEditorScope scope,Action<CardValue> set,Action refresh)
    {
        var window=AuraToolsUi.CreateOverlay("CustomCards.Expression."+Guid.NewGuid().ToString("N"),parent,boolean?"条件表达式":"数值表达式",()=>{refresh();QueueRender();},singleInstance:false,maxWidth:970);
        var panel=AuraToolsUi.CreateScroll(window.transform,"CardExpression");
        void Fill()
        {
            AuraToolsUi.ClearChildren(panel);
            var kinds=Enum.GetValues(typeof(CardValueKind)).Cast<CardValueKind>().Where(k=>CustomCardNames.Boolean(k)==boolean).ToArray();
            Select(panel,kinds.Select(k=>CustomCardNames.Name(k,CustomCardNames.Values)).ToArray(),Array.IndexOf(kinds,value.Kind),i=>
            {
                Change(()=>
                {
                    value=new(){Kind=kinds[i],Object=new(){Kind=CardObjectKind.Self}};
                    bool conditions=value.Kind==CardValueKind.And||value.Kind==CardValueKind.Or||value.Kind==CardValueKind.Not;
                    for(int n=0;n<CustomCardNames.Arity(value.Kind);n++)value.Inputs.Add(conditions?new(){Kind=CardValueKind.Exists}:CardValue.Constant(n==1?1:0));
                    set(value);
                },false);Fill();
            },280);
            if(value.Kind==CardValueKind.Number)NumberInput(panel,value.Number,v=>Change(()=>value.Number=v,false),240);
            if(value.Kind==CardValueKind.Read)
            {
                Select(panel,CustomCardNames.Fields,(int)value.Field,i=>{Change(()=>{value.Field=(CardDataField)i;if(value.Field>=CardDataField.Energy)value.Object=new();},false);Fill();},280);
                ObjectChoice(panel,value.Object,scope,true,Fill,o=>value.Object=o);
                if(value.Field==CardDataField.BuffStacks)Button(panel,"选择状态："+value.ResourceId,()=>BuffPicker(window.transform,id=>{Change(()=>value.ResourceId=id,false);Fill();}),450);
            }
            if(value.Kind==CardValueKind.Exists)ObjectChoice(panel,value.Object,scope,true,Fill,o=>value.Object=o);
            if(value.Kind==CardValueKind.Variable)VariableChoice(panel,scope,false,value.VariableId,id=>{Change(()=>value.VariableId=id,false);Fill();});
            for(int i=0;i<value.Inputs.Count;i++)
            {
                int index=i;bool childBoolean=value.Kind==CardValueKind.And||value.Kind==CardValueKind.Or||value.Kind==CardValueKind.Not;
                ExpressionButton(panel,"参数 "+(i+1),value.Inputs[i],childBoolean,scope,v=>value.Inputs[index]=v,Fill);
            }
            Hint(panel,CustomCardDescription.Value(value,scope.Names));
        }
        Fill();
    }
    private void ObjectChoice(Transform parent,CardObjectReference value,CustomCardEditorScope scope,bool single,Action refresh,Action<CardObjectReference> set)
    {
        var options=Enum.GetValues(typeof(CardObjectKind)).Cast<CardObjectKind>()
            .Where(k=>(!single||CustomCardNames.Single(k))&&(k!=CardObjectKind.Target||scope.Target)&&(k!=CardObjectKind.Current||scope.Current)).ToArray();
        Select(parent,options.Select(k=>CustomCardNames.Name(k,CustomCardNames.Objects)).ToArray(),Array.IndexOf(options,value.Kind),i=>{Change(()=>set(new(){Kind=options[i]}),false);refresh();},310);
        if(value.Kind==CardObjectKind.Saved)VariableChoice(parent,scope,true,value.VariableId,id=>{Change(()=>value.VariableId=id,false);refresh();});
    }
    private static void VariableChoice(Transform parent,CustomCardEditorScope scope,bool objects,string selected,Action<string> set)
    {
        var items=scope.Variables.Where(p=>p.Value.Object==objects).ToArray();
        if(items.Length==0){Hint(parent,"当前没有可引用的变量，请先添加“记住”积木。");return;}
        Select(parent,new[]{"请选择变量"}.Concat(items.Select(p=>p.Value.Name)).ToArray(),Array.FindIndex(items,p=>p.Key==selected)+1,i=>{if(i>0)set(items[i-1].Key);},310);
    }
    private void BuffPicker(Transform parent,Action<string> picked)
    {
        Do(()=>
        {
            var entries=CustomCardNative.Buffs();var window=AuraToolsUi.CreateOverlay("CustomCards.Buffs",parent,"选择状态");
            var input=AuraToolsUi.AddTmpInput(window.transform,"","搜索名称或标识",_=>{},420);var list=AuraToolsUi.CreateScroll(window.transform,"CardBuffs");
            void Fill(string query)
            {
                AuraToolsUi.ClearChildren(list);
                foreach(var p in entries.Where(p=>p.Key.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||p.Value.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).Take(80))
                    Button(list,p.Value+" · "+p.Key,()=>{picked(p.Key);Destroy(window.transform.parent.gameObject);},560);
                Hint(list,"最多显示 80 个结果，输入名称缩小范围。");
            }
            input.onValueChanged.RemoveAllListeners();input.onValueChanged.AddListener(Fill);Fill("");
        });
    }
    private void Artwork()
    {
        Hint(content,"像素卡面随作品和成品保存。模板可以直接应用，修改模板不会改变已有成品。");
        Button(content,"打开像素画板",()=>CardPixelEditor.Show(root,doc.Artwork,Record,()=>{dirty=true;QueueRender();}),230);
        var row=Row(content,"ArtMode");Select(row,new[]{"固定游戏卡面","自绘像素卡面"},doc.Artwork.UsePixels?1:0,i=>Change(()=>doc.Artwork.UsePixels=i==1),230);
        Select(content,new[]{"元素升华","双刃剑盾","禁果"},Array.IndexOf(new[]{"Icon/Card/元素升华","Icon/Card/双刃剑盾","Icon/Card/禁果"},doc.Artwork.TemplateIcon),i=>Change(()=>doc.Artwork.TemplateIcon=new[]{"Icon/Card/元素升华","Icon/Card/双刃剑盾","Icon/Card/禁果"}[i]),230);
        ArtPreview(content,280);
    }
    private void ArtPreview(Transform parent,int size)
    {
        if(previewTexture!=null){Destroy(previewTexture);previewTexture=null;}
        var panel=AuraToolsUi.CreateLayout("CardArtPreview",parent);AuraToolsUi.SetFixedHeight(panel,size);
        var picture=AuraToolsUi.CreateRect("Image",panel.transform,Vector2.zero,Vector2.one,new Vector2(0.5f,0.5f),Vector2.zero);
        var aspect=picture.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;aspect.aspectRatio=1;
        var image=picture.AddComponent<RawImage>();image.raycastTarget=false;
        try
        {
            if(doc.Artwork.UsePixels){previewTexture=CustomCardArtworkRuntime.Texture(doc.Artwork);image.texture=previewTexture;}
            else image.texture=AuraToolsResourceCache.Load<Texture>(doc.Artwork.TemplateIcon,true);
        }
        catch(Exception ex){Hint(parent,"卡面预览暂不可用："+ex.Message);}
    }
    private void Preview()
    {
        var row=Row(content,"CardTitle");Label(row,doc.Name,300);Label(row,"费用 "+doc.Cost+" · "+(doc.Targeted?"攻击牌":"技能牌"),250);
        ArtPreview(content,256);
        description=AuraToolsUi.AddTmpText(content,CustomCardDescription.Describe(doc),17,TextAnchor.UpperLeft,AuraToolsUi.Text,Math.Max(140,CustomCardDescription.Describe(doc).Split('\n').Length*25));
        Hint(content,(doc.Burnout?"焚毁  ":"")+(doc.Retain?"保留":""));Hint(content,doc.Note);
    }
    private void Trial()
    {
        Hint(content,"基础数值试算不触碰当前对局；不计算原生状态修正、伤害乘区和联机结算。");
        var self=Row(content,"TrialSelf");Label(self,"自己生命 / 上限 / 护盾",205);
        NumberInput(self,trial.Self.Health,v=>trial.Self.Health=v);NumberInput(self,trial.Self.MaximumHealth,v=>trial.Self.MaximumHealth=v);NumberInput(self,trial.Self.Shield,v=>trial.Self.Shield=v);
        var enemy=Row(content,"TrialEnemy");Label(enemy,"敌人生命 / 上限 / 护盾",205);
        NumberInput(enemy,trial.Enemies[0].Health,v=>trial.Enemies[0].Health=v);NumberInput(enemy,trial.Enemies[0].MaximumHealth,v=>trial.Enemies[0].MaximumHealth=v);NumberInput(enemy,trial.Enemies[0].Shield,v=>trial.Enemies[0].Shield=v);
        var cardRow=Row(content,"TrialCards");Label(cardRow,"能量 / 手牌 / 抽牌 / 弃牌",205);
        NumberInput(cardRow,trial.Energy,v=>trial.Energy=(int)v);NumberInput(cardRow,trial.HandCount,v=>trial.HandCount=(int)v);NumberInput(cardRow,trial.DeckCount,v=>trial.DeckCount=(int)v);NumberInput(cardRow,trial.DiscardCount,v=>trial.DiscardCount=(int)v);
        foreach(var buff in CustomCardCompiler.Compile(doc).BuffReferences)
        {
            var row=Row(content,"TrialBuff");Label(row,buff.Value+"（自己 / 敌人）",300);
            NumberInput(row,trial.Self.Buffs.TryGetValue(buff.Key,out var own)?own:0,v=>trial.Self.Buffs[buff.Key]=v);
            NumberInput(row,trial.Enemies[0].Buffs.TryGetValue(buff.Key,out var other)?other:0,v=>trial.Enemies[0].Buffs[buff.Key]=v);
        }
        Select(content,new[]{"选择要试算的触发时机"}.Concat(CustomCardNames.Triggers).ToArray(),0,i=>{if(i>0){trialResult=string.Join("\n",CustomCardTrial.Run(doc,trial,(CardRuleTrigger)(i-1)));Render();}},330);
        AuraToolsUi.AddTmpText(content,trialResult,16,TextAnchor.UpperLeft,AuraToolsUi.Text,Math.Max(180,trialResult.Split('\n').Length*25));
    }
    private void Scripts()
    {
        var compilation=CustomCardCompiler.Compile(doc);
        if(!compilation.Success){foreach(var issue in compilation.Issues)Hint(content,issue.Message,AuraToolsUi.ErrorText);return;}
        var row=Row(content,"ScriptActions");Button(row,"检查游戏 Lua 语法",()=>Do(()=>{CustomCardNative.CheckLua(compilation);Report("所有生成脚本的 Lua 语法检查通过；未执行战斗效果。");}),230);
        Button(row,"复制全部脚本",()=>{GUIUtility.systemCopyBuffer=string.Join("\n\n",compilation.Scripts.Select(p=>"-- "+p.Key+"\n"+p.Value));Report("生成脚本已复制。");},190);
        foreach(var pair in compilation.Scripts)
        {
            Hint(content,pair.Key);
            var text=AuraToolsUi.AddTmpText(content,pair.Value,13,TextAnchor.UpperLeft,AuraToolsUi.Text,Math.Max(70,pair.Value.Split('\n').Length*18));text.richText=false;
        }
    }
    private void OnDestroy()
    {
        if(previewTexture!=null)Destroy(previewTexture);
        if(sideTexture!=null)Destroy(sideTexture);
        if(dirty)try{CustomCardLibrary.Save(doc);}catch(Exception ex){AuraToolsLog.Warn("[CustomCard] interrupted draft save failed: "+ex.Message);}
    }
    private void OnRectTransformDimensionsChange()=>UpdateSidebarVisibility();
    private void UpdateSidebarVisibility()
    {
        if(sideRoot==null||root==null)return;bool visible=(root as RectTransform)?.rect.width>=1100;
        if(sideRoot.activeSelf!=visible)sideRoot.SetActive(visible);
    }
    internal static Transform Row(Transform parent,string name)
    {
        var row=AuraToolsUi.CreateLayout(name,parent);row.AddComponent<CustomCardFlowLayout>();return row.transform;
    }
    internal static Transform Column(Transform parent,string name)
    {
        var col=AuraToolsUi.CreateLayout(name,parent);var layout=col.AddComponent<VerticalLayoutGroup>();layout.spacing=5;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;return col.transform;
    }
    internal static Button Button(Transform parent,string name,Action action,float width=120,float height=40)=>AuraToolsUi.AddButton(parent,name,action,width,height);
    internal static void Hint(Transform parent,string text,Color? color=null)=>AuraToolsUi.AddTmpText(parent,text,15,TextAnchor.MiddleLeft,color??AuraToolsUi.MutedText,Math.Max(36,(text.Length/65+1)*24));
    internal static void Label(Transform parent,string text,float width=130)=>AuraToolsUi.AddTmpText(parent,text,16,TextAnchor.MiddleLeft,AuraToolsUi.Text,40,0,width);
    internal static void Select(Transform parent,IReadOnlyList<string> labels,int selected,Action<int> change,float width=230)=>AuraToolsUi.AddSelectButton(parent,labels,Math.Max(0,selected),change,width,40);
    internal static void NumberInput(Transform parent,double value,Action<double> change,float width=92)
    {
        var input=AuraToolsUi.AddTmpInput(parent,value.ToString("0.###",CultureInfo.InvariantCulture),"数字",_=>{},width,40);
        input.onValueChanged.RemoveAllListeners();input.onEndEdit.AddListener(v=>change(double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)?number:double.NaN));
    }
    private static void TextInput(Transform parent,string name,string value,Action<string> change,float width)
    {
        var row=Row(parent,name);Label(row,name,110);var input=AuraToolsUi.AddTmpInput(row,value,name,_=>{},width,40);
        input.onValueChanged.RemoveAllListeners();input.onEndEdit.AddListener(v=>change(v));
    }
}

internal sealed class CustomCardBlockDrag : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler,IDropHandler
{
    private static CustomCardBlockDrag? dragging;
    internal CustomCardWorkshopController Owner=null!;
    internal List<CardRuleBlock> Blocks=null!;
    internal CardRuleBlock Block=null!;
    public void OnBeginDrag(PointerEventData eventData){dragging=this;}
    public void OnDrag(PointerEventData eventData){ }
    public void OnEndDrag(PointerEventData eventData){dragging=null;}
    public void OnDrop(PointerEventData eventData){if(dragging!=null&&dragging.Owner==Owner&&ReferenceEquals(dragging.Blocks,Blocks))Owner.DropBlock(Blocks,dragging.Block,Block);}
    private void OnDestroy(){if(dragging==this)dragging=null;}
}
