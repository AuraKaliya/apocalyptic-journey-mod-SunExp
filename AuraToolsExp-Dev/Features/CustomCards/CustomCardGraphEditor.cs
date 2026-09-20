using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AuraToolsExp.Dll.Features.Settings;
using static AuraToolsExp.Dll.Features.CustomCards.CustomCardWorkshopController;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal enum CustomCardGraphChange { Content, Layout, View }

/// <summary>Runtime UGUI editor. Graph coordinates and gesture state never determine program order.</summary>
internal sealed class CustomCardGraphEditor : MonoBehaviour
{
    internal const float NodeWidth=340;
    private CustomCardDocument document=null!;
    private Action record=null!;
    private Action<CustomCardGraphChange> changed=null!;
    private Action<Transform,string,Action<string>> pickBuff=null!;
    private Action<CardGraphNode>? guide;
    private Action<string> report=null!;
    private RectTransform viewport=null!,surface=null!;
    private Transform palette=null!,inspector=null!;
    private GameObject paletteRoot=null!,inspectorRoot=null!;
    private TMP_Text information=null!;
    private TMP_Text zoomText=null!;
    private Button diagnostics=null!;
    private Button inspectorButton=null!;
    private CustomCardGraphLines lines=null!;
    private readonly Dictionary<string,RectTransform> views=new();
    private readonly Dictionary<string,string> signatures=new();
    private readonly Dictionary<string,Vector2> ports=new();
    private readonly HashSet<string> selected=new();
    private readonly Dictionary<string,Vector2> dragStart=new();
    private CardGraphEdge? selectedEdge;
    private string? insertingEdge;
    private CardGraphNode? pendingNode;
    private CardNodePort? pendingPort;
    private Vector2 pointer;
    private Vector2 gestureStart,panStart;
    private bool dragging,moving,boxing;
    private RectTransform? selectionBox;
    private GameObject? popup;
    private Transform? popupInspector;
    private string search="";
    private string category="全部";
    private readonly HashSet<string> collapsedCategories=new(){"对象与选择","读取数据","数值与条件","流程控制","本次变量","画布注释","常用组合"};
    private bool building;
    private float layoutWidth;
    private bool resizePending=true;
    private LayoutElement workspaceSize=null!;
    private Transform toolbar=null!;
    private CustomCardGraphMiniMap miniMap=null!;
    private bool initialFit;
    private bool paletteCollapsed,inspectorCollapsed;
    private readonly Dictionary<string,string> foldedRepresentative=new();
    private readonly Dictionary<string,(string Group,Vector2 Offset)> foldedPorts=new();
    private CustomCardCompilation? lastValidation;
    private bool inputError;
    private CardBlueprint Graph=>document.Graph;
    internal CardBlueprint Blueprint=>Graph;
    internal IReadOnlyCollection<string> Selection=>selected;
    internal RectTransform Viewport=>viewport;

    internal void Build(CustomCardDocument doc,Action beforeChange,Action<CustomCardGraphChange> afterChange,Action<Transform,string,Action<string>> resourcePicker,Action<string> message,Action<CardGraphNode>? nodeGuide=null)
    {
        document=doc;record=beforeChange;changed=afterChange;pickBuff=resourcePicker;report=message;guide=nodeGuide;
        var layout=gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=0;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
        var bar=CommandRow(transform,"BlueprintToolbar");toolbar=bar;CustomCardUi.SetFixedHeight(bar.gameObject,52);bar.GetComponent<HorizontalLayoutGroup>().padding=new(16,16,6,6);
        CustomCardControls.IconButton(bar,CardIcon.Panel,"节点库",()=>{paletteCollapsed=!paletteCollapsed;Resize();});
        Button(bar,"添加节点",()=>ShowPalette(null),108);Quiet(Button(bar,"整理",Arrange,64));Quiet(Button(bar,"适合画布",Fit,100));
        CustomCardControls.IconButton(bar,CardIcon.More,"编辑",ShowEditMenu);
        inspectorButton=CustomCardControls.IconButton(bar,CardIcon.Panel,"参数",()=>{if(layoutWidth>=680){inspectorCollapsed=!inspectorCollapsed;Resize();}else ShowInspector();});Spacer(bar);
        diagnostics=Quiet(Button(bar,"检查通过",ShowDiagnostics,108));CustomCardControls.Rule(transform);
        var body=CustomCardUi.CreateLayout("GraphWorkspace",transform);
        var bodyLayout=body.AddComponent<HorizontalLayoutGroup>();bodyLayout.spacing=0;bodyLayout.childControlHeight=true;bodyLayout.childControlWidth=true;bodyLayout.childForceExpandWidth=false;bodyLayout.childForceExpandHeight=true;
        workspaceSize=CustomCardUi.SetFixedHeight(body,Math.Max(240,Math.Min(570,Screen.height-430)));
        palette=CustomCardUi.CreateScroll(body.transform,"NodePalette");paletteRoot=palette.parent.parent.gameObject;
        var pl=paletteRoot.GetComponent<LayoutElement>();pl.minWidth=0;pl.preferredWidth=172;pl.flexibleWidth=0;
        CustomCardUi.AddImage(paletteRoot,CustomCardVisuals.Node);palette.GetComponent<VerticalLayoutGroup>().padding=new(12,2,16,12);
        var canvas=CustomCardUi.CreateRect("GraphViewport",body.transform,Vector2.zero,Vector2.one,new(0,1),Vector2.zero);
        var cl=canvas.AddComponent<LayoutElement>();cl.minWidth=220;cl.flexibleWidth=1;
        CustomCardUi.AddImage(canvas,CustomCardVisuals.Canvas);canvas.AddComponent<RectMask2D>();viewport=canvas.GetComponent<RectTransform>();
        var input=canvas.AddComponent<CustomCardGraphInput>();input.Editor=this;
        var grid=CustomCardUi.CreateRect("Grid",canvas.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero).AddComponent<CustomCardGraphGrid>();grid.raycastTarget=false;
        var content=CustomCardUi.CreateRect("GraphSurface",canvas.transform,new(0,1),new(0,1),new(0,1),Vector2.zero);surface=content.GetComponent<RectTransform>();
        var edgeObject=CustomCardUi.CreateRect("Connections",surface,new(0,1),new(0,1),new(0,1),Vector2.zero);
        edgeObject.GetComponent<RectTransform>().pivot=new(.5f,.5f);edgeObject.GetComponent<RectTransform>().sizeDelta=new(2000000,2000000);
        lines=edgeObject.AddComponent<CustomCardGraphLines>();lines.Editor=this;lines.raycastTarget=false;
        var map=CustomCardUi.CreateRect("GraphMiniMap",canvas.transform,new(1,0),new(1,0),new(1,0),new(136,88));
        map.GetComponent<RectTransform>().anchoredPosition=new(-8,8);miniMap=map.AddComponent<CustomCardGraphMiniMap>();miniMap.Editor=this;
        inspector=CustomCardUi.CreateScroll(body.transform,"NodeInspector");inspectorRoot=inspector.parent.parent.gameObject;
        var il=inspectorRoot.GetComponent<LayoutElement>();il.minWidth=0;il.preferredWidth=232;il.flexibleWidth=0;
        CustomCardUi.AddImage(inspectorRoot,CustomCardVisuals.Node);inspector.GetComponent<VerticalLayoutGroup>().padding=new(16,6,20,16);inspector.GetComponent<VerticalLayoutGroup>().spacing=12;
        CustomCardControls.Rule(transform);var footer=CommandRow(transform,"GraphStatus");footer.GetComponent<HorizontalLayoutGroup>().padding=new(16,16,0,0);CustomCardUi.SetFixedHeight(footer.gameObject,32);
        information=CustomCardUi.AddTmpText(footer,"",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,28,1);zoomText=CustomCardUi.AddTmpText(footer,"100%",12,TextAnchor.MiddleRight,CustomCardVisuals.Muted,28,0,48);
        var first=Graph.Nodes.FirstOrDefault(n=>n.Kind==CardNodeKind.Effect);if(first!=null)selected.Add(first.Id);
        FillPalette(palette,false,null);Refresh();resizePending=true;initialFit=!Graph.HasView;
    }
    private void Edit(Action action,bool semantic=true)
    {
        if(!CustomCardInputFeedback.CommitAll(transform))return;
        record();action();CardBlueprintNumbers.AdoptEmptyInputs(Graph);changed(semantic?CustomCardGraphChange.Content:CustomCardGraphChange.Layout);Refresh();
    }
    internal void RestoreSelection(IEnumerable<string> ids)
    {
        selected.Clear();foreach(var id in ids)if(Graph.Nodes.Any(n=>n.Id==id))selected.Add(id);
        UpdateHighlights();FillInspector(inspector);
    }
    private void ChooseState(Transform parent,CardGraphNode node)
    {
        if(!CustomCardInputFeedback.CommitAll(transform))return;
        pickBuff(parent,node.ResourceId,id=>Edit(()=>node.ResourceId=id));
    }
    internal void Refresh()
    {
        if(building||surface==null)return;building=true;
        ports.Clear();foldedRepresentative.Clear();foldedPorts.Clear();var kept=new HashSet<string>();
        selected.RemoveWhere(id=>!Graph.Nodes.Any(n=>n.Id==id));
        var validation=CustomCardCompiler.Compile(document,CustomCardNative.BuffName);
        foreach(var n in Graph.Nodes)
        {
            var group=Graph.Groups.FirstOrDefault(g=>g.Id==n.GroupId&&g.Collapsed);
            if(group!=null)
            {
                if(!foldedRepresentative.ContainsKey(group.Id)){var rep=GroupAnchor(group.Id);ReleaseView(rep.Id);DrawGroup(group,validation);kept.Add(rep.Id);}
                continue;
            }
            var signature=JsonConvert.SerializeObject(new{n.Kind,n.Version,n.Trigger,n.Effect,n.ManyTargets,n.ValueKind,n.Subject,n.Field,n.ResourceId,n.VariableId,n.VariableName,n.Label,n.NoteWidth,n.NoteHeight,n.Numbers,
                Sources=Graph.Edges.Where(e=>e.To==n.Id).Select(e=>Graph.Nodes.FirstOrDefault(s=>s.Id==e.From)).Where(s=>s!=null).Select(s=>new{s!.Id,s.Field,s.ValueKind,s.Numbers}),
                Connections=Graph.Edges.Where(e=>e.To==n.Id||e.From==n.Id).Select(e=>e.From+e.Output+e.To+e.Input).OrderBy(p=>p).ToArray(),Error=validation.Issues.FirstOrDefault(i=>i.NodeId==n.Id)?.Message,Unused=validation.Warnings.Any(i=>i.NodeId==n.Id)});
            if(!views.ContainsKey(n.Id)||!signatures.TryGetValue(n.Id,out var previous)||signature!=previous){ReleaseView(n.Id);DrawNode(n,validation,null);signatures[n.Id]=signature;}
            else views[n.Id].anchoredPosition=new(n.X,-n.Y);
            kept.Add(n.Id);
        }
        foreach(var id in views.Keys.Where(id=>!kept.Contains(id)).ToArray())ReleaseView(id);
        RepositionPorts();UpdateHighlights();ApplyView();FillInspector(inspector);if(popupInspector!=null)FillInspector(popupInspector);UpdateInfo(validation);building=false;resizePending=true;
    }
    private void ReleaseView(string id)
    {
        if(views.TryGetValue(id,out var view)){CustomCardUiLifetime.Destroy(view.gameObject,viewport.gameObject);views.Remove(id);}signatures.Remove(id);
    }
    private void UpdateHighlights()
    {
        foreach(var pair in views)
        {
            var node=Graph.Nodes.FirstOrDefault(n=>n.Id==pair.Key);if(node==null)continue;
            bool chosen=selected.Contains(node.Id);
            if(foldedRepresentative.ContainsKey(node.GroupId))
            {
                var ids=new HashSet<string>(Graph.Nodes.Where(n=>n.GroupId==node.GroupId).Select(n=>n.Id));chosen=ids.Any(selected.Contains);
            }
            var chrome=pair.Value.GetComponent<CustomCardNodeChrome>();
            if(chrome!=null)
            {
                chrome.Selected=chosen;chrome.SetVerticesDirty();
                if(chrome.StatusLabel!=null){chrome.StatusLabel.text=chrome.IdleState;chrome.StatusLabel.color=chrome.Invalid?CustomCardVisuals.Error:CustomCardVisuals.Muted;}
            }
        }
    }
    private void DrawGroup(CardGraphGroup group,CustomCardCompilation validation)
    {
        var members=Graph.Nodes.Where(n=>n.GroupId==group.Id).ToArray();if(members.Length==0)return;
        var ids=new HashSet<string>(members.Select(n=>n.Id));var rep=GroupAnchor(group.Id);foldedRepresentative[group.Id]=rep.Id;
        var exposed=new List<(CardGraphNode Node,CardNodePort Port)>();
        foreach(var node in members)foreach(var port in CardNodeCatalog.Ports(node))
        {
            var edges=Graph.Edges.Where(e=>port.Output?e.From==node.Id&&e.Output==port.Id:e.To==node.Id&&e.Input==port.Id).ToArray();
            bool boundary=edges.Any(e=>!ids.Contains(port.Output?e.To:e.From));
            bool free=edges.Length==0&&(port.Type==CardPortType.Execution||port.Required||port.Output);
            if(boundary||free)exposed.Add((node,port));
        }
        var ins=exposed.Where(p=>!p.Port.Output).ToArray();var outs=exposed.Where(p=>p.Port.Output).ToArray();
        var stateMembers=members.Where(CardNodeCatalog.RequiresState).ToArray();float extra=stateMembers.Length>0?32:0;
        float x=rep.X,y=rep.Y,height=94+extra+32*Math.Max(ins.Length,outs.Length);
        var go=CustomCardUi.CreateRect("Group."+group.Id,surface,new(0,1),new(0,1),new(0,1),new(NodeWidth,height));
        views[rep.Id]=Place(go,x,y,NodeWidth,height);var chrome=go.AddComponent<CustomCardNodeChrome>();chrome.Invalid=validation.Issues.Any(i=>ids.Contains(i.NodeId))||stateMembers.Any(n=>string.IsNullOrEmpty(n.ResourceId)||CustomCardNative.State(n.ResourceId)==null);
        Text(go.transform,"组合 · "+group.Name,14,7,NodeWidth-28,30,17);Text(go.transform,members.Length+" 个节点 · 双击展开",14,48,NodeWidth-28,27,13,CustomCardVisuals.Muted);
        if(extra>0)Text(go.transform,"状态："+string.Join("、",stateMembers.Select(n=>CustomCardNative.BuffName(n.ResourceId)??"待选择")),14,77,NodeWidth-28,27,13,chrome.Invalid?CustomCardVisuals.Error:CustomCardVisuals.Gold);
        var header=CustomCardUi.CreateRect("GroupDrag",go.transform,new(0,1),new(0,1),new(0,1),new(NodeWidth,35));CustomCardUi.AddImage(header,new Color(1,1,1,.001f));var input=header.AddComponent<CustomCardGraphNodeInput>();input.Editor=this;input.NodeId=rep.Id;
        foreach(var side in new[]{ins,outs})for(int i=0;i<side.Length;i++)
        {
            var item=side[i];var pin=item.Port;
            var name=(side.Length>1?CardNodeCatalog.Title(item.Node)+" · ":"")+pin.Name;
            Pin(go.transform,item.Node,new CardNodePort(pin.Id,name,pin.Type,pin.Output,pin.Required),84+extra+i*32,false);
            var key=Key(item.Node.Id,pin.Id,pin.Output);var relative=new Vector2(pin.Output?NodeWidth-14:14,-(98+extra+i*32));
            foldedPorts[key]=(group.Id,relative);ports[key]=new Vector2(x,-y)+relative;
        }
    }
    private CardGraphNode GroupAnchor(string group)
    {
        var members=Graph.Nodes.Where(n=>n.GroupId==group).ToArray();var ids=new HashSet<string>(members.Select(n=>n.Id));
        return members.FirstOrDefault(n=>CardNodeCatalog.Executable(n.Kind)&&!Graph.Edges.Any(e=>e.To==n.Id&&e.Input=="in"&&ids.Contains(e.From)))??members.FirstOrDefault(n=>CardNodeCatalog.Executable(n.Kind))??members[0];
    }
    private static RectTransform Place(GameObject go,float x,float y,float width,float height)
    {
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new(0,1);r.anchoredPosition=new(x,-y);r.sizeDelta=new(width,height);return r;
    }
    private static TMP_Text Text(Transform parent,string text,float x,float y,float w,float h,int font=14,Color? color=null)
    {
        var t=CustomCardUi.AddTmpText(parent,text,font,TextAnchor.MiddleLeft,color??CustomCardVisuals.Ink,h);Place(t.gameObject,x,y,w,h);t.richText=false;t.raycastTarget=false;t.overflowMode=TextOverflowModes.Ellipsis;return t;
    }
    private void DrawNode(CardGraphNode n,CustomCardCompilation validation,string? groupName)
    {
        float height=NodeHeight(n),width=n.Kind==CardNodeKind.Note?n.NoteWidth:NodeWidth;
        var go=CustomCardUi.CreateRect("Node."+n.Id,surface,new(0,1),new(0,1),new(0,1),new(width,height));
        views[n.Id]=Place(go,n.X,n.Y,width,height);
        var chrome=go.AddComponent<CustomCardNodeChrome>();chrome.Header=CustomCardVisuals.Category(n.Kind);chrome.Fill=n.Kind==CardNodeKind.Note?CustomCardVisuals.Note:CustomCardVisuals.Node;
        var title=Text(go.transform,CardNodeCatalog.Title(n),12,3,width-24,36,16);title.textWrappingMode=TextWrappingModes.NoWrap;
        var header=CustomCardUi.CreateRect("TitleDrag",go.transform,new(0,1),new(0,1),new(0,1),new(width,44));
        CustomCardUi.AddImage(header,new Color(1,1,1,.001f));var drag=header.AddComponent<CustomCardGraphNodeInput>();drag.Editor=this;drag.NodeId=n.Id;
        if(n.Kind==CardNodeKind.Note)
        {
            var note=Text(go.transform,string.IsNullOrWhiteSpace(n.Label)?"双击标题编辑说明\n拖动右下角调整尺寸":n.Label,14,52,width-28,height-68,15,CustomCardVisuals.Gold);note.alignment=TextAlignmentOptions.TopLeft;
            var grip=CustomCardUi.CreateRect("ResizeNote",go.transform,new(0,1),new(0,1),new(0,1),new(24,24));Place(grip,width-26,height-26,24,24);CustomCardUi.AddImage(grip,new Color(1,1,1,.03f));
            Text(grip.transform,"◢",3,0,20,24,17,CustomCardVisuals.Gold);var resize=grip.AddComponent<CustomCardNoteResize>();resize.Editor=this;resize.Node=n;
            var noteLod=go.AddComponent<CustomCardNodeLod>();noteLod.Title=title;noteLod.Details.Add(note.gameObject);noteLod.Details.Add(grip);
            noteLod.Overview=Text(go.transform,n.Label,14,48,width-28,Math.Min(32,height-60),18,CustomCardVisuals.Gold);noteLod.Apply(Graph.Zoom);return;
        }
        if(n.Label.Length>0)Text(go.transform,n.Label,14,48,NodeWidth-28,22,12,CustomCardVisuals.Gold);
        string fullTitle=CardNodeCatalog.Title(n);
        if(n.Kind==CardNodeKind.Value&&CardBlueprintNumbers.Comparison(n.ValueKind))
        {
            fullTitle="数值比较";Place(title.gameObject,12,3,126,36);
            var relation=Select(go.transform,CardBlueprintNumbers.Comparisons.Select(CardBlueprintNumbers.Operator).ToArray(),Array.IndexOf(CardBlueprintNumbers.Comparisons,n.ValueKind),i=>Edit(()=>n.ValueKind=CardBlueprintNumbers.Comparisons[i]),180);
            Place(relation.gameObject,148,6,180,30);
        }
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Read)
        {
            fullTitle="读取数值";Place(title.gameObject,12,3,126,36);
            var field=Select(go.transform,CustomCardNames.Fields,(int)n.Field,i=>Edit(()=>{n.Field=(CardDataField)i;if(CardNodeCatalog.FixedOwner(n)){n.Subject=CardObjectKind.Self;Graph.Edges.RemoveAll(e=>e.To==n.Id&&e.Input=="object");}}),180);
            Place(field.gameObject,148,6,180,30);
        }
        if(CardNodeCatalog.RequiresState(n))
        {
            float y=n.Label.Length>0?80:52;
            Text(go.transform,"状态",30,y,76,36,13);
            var stateChoice=CustomCardStateSelector.Create(go.transform,n.ResourceId,()=>ChooseState(transform,n),NodeWidth-124);
            Place(stateChoice.gameObject,110,y,NodeWidth-124,36);
        }
        foreach(var item in PortLayout(n))Pin(go.transform,n,item.Port,item.Y,true);
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Number)NumericEditor(go.transform,n,"a",14,n.Label.Length>0?76:48,NodeWidth-28);
        var issue=validation.Issues.FirstOrDefault(i=>i.NodeId==n.Id);
        if(issue!=null){title.text="! "+title.text;title.color=CustomCardVisuals.Error;}
        chrome.Invalid=issue!=null;
        string state=issue!=null?"!  "+issue.Message:"";
        chrome.IdleState=state;chrome.StatusLabel=Text(go.transform,state,14,height-28,NodeWidth-28,24,11,issue!=null?CustomCardVisuals.Error:CustomCardVisuals.Muted);
        var lod=go.AddComponent<CustomCardNodeLod>();lod.Title=title;lod.FullTitle=(issue!=null?"! ":"")+fullTitle;lod.CompactTitle=title.text;
        foreach(Transform child in go.transform)if(child.gameObject!=title.gameObject&&child.gameObject!=header)lod.Details.Add(child.gameObject);
        string summary=issue!=null?"需要修正":CardNodeCatalog.RequiresState(n)?CustomCardNative.BuffName(n.ResourceId)??"待选择状态":n.Kind==CardNodeKind.Effect&&CustomCardNames.HasAmount(n.Effect)
            ?(Graph.Edges.Any(e=>e.To==n.Id&&e.Input=="value")?"数值由连线提供":n.Number.ToString("0.###",CultureInfo.InvariantCulture)+" "+CardNodeCatalog.Unit(n,"value"))
            :n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Number?n.PeekOperand("a").EditText+n.PeekOperand("a").Suffix:n.Label;
        lod.Overview=Text(go.transform,summary,14,48,NodeWidth-28,Math.Min(32,height-60),18,issue!=null?CustomCardVisuals.Error:CustomCardVisuals.Muted);
        lod.Overview.textWrappingMode=TextWrappingModes.NoWrap;lod.Apply(Graph.Zoom);
    }
    private static float PortStart(CardGraphNode n)=>(n.Label.Length>0?80:52)+(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Number?44:0)+(CardNodeCatalog.RequiresState(n)?44:0);
    private static IEnumerable<(CardNodePort Port,float Y)> PortLayout(CardGraphNode n)
    {
        float y=PortStart(n);var pins=CardNodeCatalog.Ports(n);
        var ins=pins.Where(p=>!p.Output&&p.Type==CardPortType.Execution).ToArray();var outs=pins.Where(p=>p.Output&&p.Type==CardPortType.Execution).ToArray();
        int count=Math.Max(ins.Length,outs.Length);
        for(int i=0;i<ins.Length;i++)yield return(ins[i],y+i*32);
        for(int i=0;i<outs.Length;i++)yield return(outs[i],y+i*32);
        if(count>0)y+=count*32+8;
        foreach(var p in pins.Where(p=>!p.Output&&p.Type!=CardPortType.Execution)){yield return(p,y);y+=40;}
        foreach(var p in pins.Where(p=>p.Output&&p.Type!=CardPortType.Execution)){yield return(p,y);y+=36;}
    }
    private static float PortY(CardGraphNode n,CardNodePort port)
    {
        return PortLayout(n).FirstOrDefault(p=>p.Port.Id==port.Id&&p.Port.Output==port.Output).Y;
    }
    internal static float NodeHeight(CardGraphNode n)
    {
        if(n.Kind==CardNodeKind.Note)return n.NoteHeight;return PortLayout(n).Select(p=>p.Y+58).DefaultIfEmpty(PortStart(n)+24).Max();
    }
    private void Pin(Transform parent,CardGraphNode n,CardNodePort p,float y,bool defaults)
    {
        float x=p.Output?NodeWidth-28:0;
        var pin=CustomCardUi.CreateRect("Port."+p.Id,parent,new(0,1),new(0,1),new(0,1),new(28,28));Place(pin,x,y,28,28);
        var glyph=pin.AddComponent<CustomCardPortGlyph>();glyph.Type=p.Type;
        glyph.Connected=Graph.Edges.Any(e=>p.Output?e.From==n.Id&&e.Output==p.Id:e.To==n.Id&&e.Input==p.Id);glyph.RequiredMissing=!p.Output&&p.Required&&!glyph.Connected;
        var handler=pin.AddComponent<CustomCardGraphPortInput>();handler.Editor=this;handler.Node=n;handler.Port=p;
        bool inline=defaults&&!p.Output&&p.Type==CardPortType.Number&&!glyph.Connected;
        bool actor=defaults&&!p.Output&&(p.Type==CardPortType.Actor||p.Type==CardPortType.Actors)&&!p.Required&&!glyph.Connected;
        var label=Text(parent,p.Name,p.Output?NodeWidth-202:30,y,p.Output?174:76,28,13);
        if(p.Output)label.alignment=TextAlignmentOptions.MidlineRight;
        if(p.Output&&p.Type==CardPortType.Number)
        {
            var format=CardBlueprintNumbers.Output(Graph,n);label.text=format.Kind==CardNumberKind.Ratio?format.Format==CardNumberFormat.Percent?"比例（%）":"比例（小数）":p.Name;
        }
        if(inline)
        {
            NumericEditor(parent,n,p.Id,110,y,NodeWidth-144);
        }
        if(actor)
        {
            var options=CardNodeCatalog.ObjectDefaults(n);var selected=Math.Max(0,options.ToList().IndexOf(n.Subject));
            var actorChoice=Select(parent,options.Select(o=>CustomCardNames.Name(o,CustomCardNames.Objects)).ToArray(),selected,i=>Edit(()=>n.Subject=options[i]),NodeWidth-144);Place(actorChoice.gameObject,110,y,NodeWidth-144,30);
        }
        if(defaults&&!p.Output&&p.Type!=CardPortType.Execution&&glyph.Connected)
        {
            var edge=Graph.Edges.First(e=>e.To==n.Id&&e.Input==p.Id);var source=Graph.Nodes.FirstOrDefault(s=>s.Id==edge.From);
            var text=source==null?"来源不存在":CardNodeCatalog.Title(source);
            if(source!=null&&p.Type==CardPortType.Number&&CardBlueprintNumbers.Output(Graph,source).Kind==CardNumberKind.Ratio)text+="（比例）";
            var link=Quiet(Button(parent,text,()=>FocusNode(edge.From),NodeWidth-144,30));Place(link.gameObject,110,y,NodeWidth-144,30);link.GetComponentInChildren<TMP_Text>().fontSize=12;
            link.gameObject.AddComponent<CustomCardTooltip>().Text="来自："+text;
        }
        else if(defaults&&!p.Output&&p.Required)
        {
            Text(parent,p.Type==CardPortType.Boolean?"连接条件":"连接来源",110,y,NodeWidth-144,28,12,CustomCardVisuals.Muted);
        }
        ports[Key(n.Id,p.Id,p.Output)]=new(n.X+(p.Output?NodeWidth-14:14),-(n.Y+y+14));
    }
    private void NumericEditor(Transform parent,CardGraphNode node,string port,float x,float y,float width,bool flow=false)
    {
        var number=CardBlueprintNumbers.Input(Graph,node,port);var literal=node.PeekOperand(port);number.Value=literal.Value;number.IsSet=literal.IsSet;
        var input=CustomCardUi.AddTmpInput(parent,number.EditText,"输入数值",_=>{},width-64,30);
        if(flow){var size=input.GetComponent<LayoutElement>();size.preferredWidth=0;size.flexibleWidth=1;}else Place(input.gameObject,x,y,width-64,30);
        bool scalarOnly=node.Kind==CardNodeKind.Effect||node.Kind==CardNodeKind.Repeat;
        CustomCardInputFeedback.BindNumber(input,number,value=>Edit(()=>{var item=node.Operand(port);item.Value=value;item.Kind=number.Kind;item.Format=number.Format;item.IsSet=true;}),node.Kind==CardNodeKind.Repeat,scalarOnly||node.ValueKind==CardValueKind.Approximately&&port=="c"?0:-1000000,node.Kind==CardNodeKind.Repeat?64:1000000);
        if(scalarOnly)
        {
            var unit=CustomCardUi.AddTmpText(parent,CardNodeCatalog.Unit(node,port),12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,30,0,60);
            if(!flow)Place(unit.gameObject,x+width-60,y,60,30);return;
        }
        int mode=number.Kind==CardNumberKind.Value?0:number.Format==CardNumberFormat.Percent?1:2;
        var format=Select(parent,new[]{"数值","%","比例"},mode,index=>Edit(()=>{var item=node.Operand(port);item.Kind=index==0?CardNumberKind.Value:CardNumberKind.Ratio;item.Format=index==1?CardNumberFormat.Percent:CardNumberFormat.Number;}),60);
        if(!flow)Place(format.gameObject,x+width-60,y,60,30);format.gameObject.AddComponent<CustomCardTooltip>().Text="输入格式；比例 0.5 = 50%";
    }
    private static string Key(string node,string port,bool output)=>node+"/"+port+(output?"/o":"/i");
    internal static Color PortColor(CardPortType type)=>CustomCardVisuals.Port(type);
    private void UpdateInfo(CustomCardCompilation validation)
    {
        lastValidation=validation;
        information.text=Graph.Nodes.Count+" 个节点 · "+validation.Issues.Count+" 个问题"+(validation.Warnings.Count>0?" · "+validation.Warnings.Count+" 个提示":"");
        if(zoomText!=null)zoomText.text=Mathf.RoundToInt(Graph.Zoom*100)+"%";
        if(diagnostics!=null){CustomCardUi.SetButtonLabel(diagnostics,inputError?"输入待修正":validation.Success?"检查通过":validation.Issues.Count+" 个问题");diagnostics.GetComponentInChildren<TMP_Text>().color=validation.Success&&!inputError?CustomCardVisuals.Visited:CustomCardVisuals.Error;}
        information.color=validation.Success?CustomCardVisuals.Muted:CustomCardVisuals.Error;
    }
    internal void SetInputError(bool invalid){inputError=invalid;if(lastValidation!=null)UpdateInfo(lastValidation);}
    internal void ApplyView()
    {
        Graph.Zoom=Mathf.Clamp(Graph.Zoom,.4f,1.6f);surface.anchoredPosition=new(Graph.PanX,-Graph.PanY);surface.localScale=Vector3.one*Graph.Zoom;
        foreach(var border in surface.GetComponentsInChildren<CustomCardControlBorder>())border.SetVerticesDirty();
        lines.SetVerticesDirty();
        foreach(var lod in surface.GetComponentsInChildren<CustomCardNodeLod>())lod.Apply(Graph.Zoom);
        if(miniMap!=null)miniMap.SetVerticesDirty();
        if(lastValidation!=null)UpdateInfo(lastValidation);
    }
    internal Vector2 Point(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,e.position,e.pressEventCamera,out var local);
        return new((local.x-Graph.PanX)/Graph.Zoom,(-local.y-Graph.PanY)/Graph.Zoom);
    }
    internal void SelectNode(string id,bool additive=false)
    {
        if(!CustomCardInputFeedback.CommitAll(transform))return;
        if(!additive&&!selected.Contains(id))selected.Clear();
        var node=Graph.Nodes.First(n=>n.Id==id);var group=Graph.Groups.FirstOrDefault(g=>g.Id==node.GroupId&&g.Collapsed);
        if(group!=null){foreach(var member in Graph.Nodes.Where(n=>n.GroupId==group.Id))selected.Add(member.Id);}
        else if(additive&&selected.Contains(id))selected.Remove(id);else selected.Add(id);
        selectedEdge=null;UpdateHighlights();FillInspector(inspector);lines.SetVerticesDirty();miniMap.SetVerticesDirty();resizePending=true;
    }
    internal void BeginMove(string id,PointerEventData e)
    {
        if(!CustomCardInputFeedback.CommitAll(transform))return;
        if(e.button!=PointerEventData.InputButton.Left)return;
        if(!selected.Contains(id)){selected.Clear();selected.Add(id);}
        var clicked=Graph.Nodes.First(n=>n.Id==id);var group=Graph.Groups.FirstOrDefault(g=>g.Id==clicked.GroupId&&g.Collapsed);
        if(group!=null)foreach(var node in Graph.Nodes.Where(n=>n.GroupId==group.Id))selected.Add(node.Id);
        record();moving=true;gestureStart=Point(e);dragStart.Clear();
        foreach(var n in Graph.Nodes.Where(n=>selected.Contains(n.Id)))dragStart[n.Id]=new(n.X,n.Y);
    }
    internal void Move(PointerEventData e)
    {
        if(!moving)return;var delta=Point(e)-gestureStart;
        foreach(var n in Graph.Nodes.Where(n=>dragStart.ContainsKey(n.Id)))
        {
            var point=dragStart[n.Id]+delta;n.X=point.x;n.Y=point.y;
            if(views.TryGetValue(n.Id,out var view)&&!foldedRepresentative.ContainsKey(n.GroupId))view.anchoredPosition=new(n.X,-n.Y);
        }
        foreach(var pair in foldedRepresentative)
        {
            var anchor=GroupAnchor(pair.Key);views[pair.Value].anchoredPosition=new(anchor.X,-anchor.Y);
        }
        RepositionPorts();lines.SetVerticesDirty();miniMap.SetVerticesDirty();
    }
    private void RepositionPorts()
    {
        foreach(var n in Graph.Nodes.Where(n=>views.ContainsKey(n.Id)))
        {
            if(foldedRepresentative.ContainsKey(n.GroupId))continue;
            var pins=CardNodeCatalog.Ports(n);
            foreach(var p in pins)ports[Key(n.Id,p.Id,p.Output)]=new(n.X+(p.Output?NodeWidth-14:14),-(n.Y+PortY(n,p)+14));
        }
        foreach(var pair in foldedPorts)
        {
            var anchor=GroupAnchor(pair.Value.Group);ports[pair.Key]=new Vector2(anchor.X,-anchor.Y)+pair.Value.Offset;
        }
    }
    internal void EndMove(){if(!moving)return;moving=false;changed(CustomCardGraphChange.Layout);FillInspector(inspector);}
    internal void BeginNoteResize()=>record();
    internal void ResizeNote(CardGraphNode node,Vector2 size)
    {
        node.NoteWidth=Mathf.Clamp(size.x,180,1400);node.NoteHeight=Mathf.Clamp(size.y,96,1400);Refresh();
    }
    internal void EndNoteResize()=>changed(CustomCardGraphChange.Layout);
    internal void StartBackground(PointerEventData e)
    {
        CancelConnection();gestureStart=Point(e);panStart=new(Graph.PanX,Graph.PanY);
        dragging=e.button==PointerEventData.InputButton.Middle||CustomCardInputState.Held(KeyCode.Space);
        boxing=!dragging&&e.button==PointerEventData.InputButton.Left;
        if(boxing)
        {
            var box=CustomCardUi.CreateRect("SelectionBox",surface,new(0,1),new(0,1),new(0,1),Vector2.zero);selectionBox=box.GetComponent<RectTransform>();CustomCardUi.AddImage(box,new Color(.4f,.65f,1f,.14f)).raycastTarget=false;
        }
    }
    internal void DragBackground(PointerEventData e)
    {
        if(dragging){Graph.PanX+=e.delta.x;Graph.PanY-=e.delta.y;ApplyView();}
        if(boxing&&selectionBox!=null)
        {
            var p=Point(e);Place(selectionBox.gameObject,Math.Min(p.x,gestureStart.x),Math.Min(p.y,gestureStart.y),Math.Abs(p.x-gestureStart.x),Math.Abs(p.y-gestureStart.y));
        }
    }
    internal void EndBackground(PointerEventData e)
    {
        if(dragging)changed(CustomCardGraphChange.View);
        if(boxing)
        {
            var p=Point(e);var bounds=Rect.MinMaxRect(Math.Min(p.x,gestureStart.x),Math.Min(p.y,gestureStart.y),Math.Max(p.x,gestureStart.x),Math.Max(p.y,gestureStart.y));
            if(!CustomCardInputState.Held(KeyCode.LeftControl)&&!CustomCardInputState.Held(KeyCode.RightControl))selected.Clear();
            foreach(var n in Graph.Nodes.Where(n=>views.ContainsKey(n.Id)))if(bounds.Overlaps(new Rect(n.X,n.Y,NodeWidth,views[n.Id].rect.height)))selected.Add(n.Id);
        }
        dragging=boxing=false;if(selectionBox!=null)Destroy(selectionBox.gameObject);selectionBox=null;Refresh();
    }
    internal void BackgroundClick(PointerEventData e)
    {
        if(e.button==PointerEventData.InputButton.Right){ShowPalette(Point(e));return;}
        if(e.button!=PointerEventData.InputButton.Left)return;
        var p=Point(e);selectedEdge=HitEdge(new(p.x,-p.y));if(selectedEdge!=null){selected.Clear();information.text="已选中连线；Delete 删除，右键空白处添加节点。";lines.SetVerticesDirty();}
    }
    internal void Scroll(PointerEventData e)
    {
        if(!CustomCardInputFeedback.CommitAll(transform))return;CustomCardUiLifetime.ReleaseFocus(transform);
        initialFit=false;Graph.HasView=true;
        var anchor=Point(e);var old=Graph.Zoom;Graph.Zoom=Mathf.Clamp(old*Mathf.Pow(1.12f,e.scrollDelta.y),.4f,1.6f);
        Graph.PanX+=anchor.x*(old-Graph.Zoom);Graph.PanY+=anchor.y*(old-Graph.Zoom);ApplyView();changed(CustomCardGraphChange.View);
    }
    internal void PortDown(CardGraphNode node,CardNodePort port,PointerEventData e)
    {
        if(e.button==PointerEventData.InputButton.Right){Edit(()=>Graph.Edges.RemoveAll(x=>port.Output?x.From==node.Id&&x.Output==port.Id:x.To==node.Id&&x.Input==port.Id));return;}
        if(pendingNode!=null&&pendingPort!=null&&pendingPort.Output!=port.Output){FinishConnection(node,port);return;}
        pendingNode=node;pendingPort=port;pointer=Point(e);lines.SetVerticesDirty();
        foreach(var candidate in surface.GetComponentsInChildren<CustomCardGraphPortInput>())
        {
            var glyph=candidate.GetComponent<CustomCardPortGlyph>();
            glyph.Compatible=candidate.Node.Id!=node.Id&&candidate.Port.Output!=port.Output&&candidate.Port.Type==port.Type;glyph.SetVerticesDirty();
        }
        information.text="青色为同类型候选；悬停检查作用域，拖到空白处选择节点。Esc 取消。";
    }
    internal void HoverNode(string id,bool hovered)
    {
        if(views.TryGetValue(id,out var view)){var chrome=view.GetComponent<CustomCardNodeChrome>();if(chrome!=null){chrome.Hovered=hovered;chrome.SetVerticesDirty();}}
        if(hovered){var node=Graph.Nodes.FirstOrDefault(n=>n.Id==id);if(node!=null)information.text=CardNodeCatalog.Title(node);}
        else if(lastValidation!=null)UpdateInfo(lastValidation);
    }
    internal void DragPort(PointerEventData e){pointer=Point(e);lines.SetVerticesDirty();}
    internal void ReleasePort(PointerEventData e)
    {
        if(pendingNode==null)return;
        if(EventSystem.current==null)return;var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
        var top=hits.FirstOrDefault().gameObject;var port=top!=null?top.GetComponentInParent<CustomCardGraphPortInput>():null;
        if(port!=null&&port.Node.Id!=pendingNode.Id)FinishConnection(port.Node,port.Port);
        else if(e.dragging&&top!=null&&top.transform.IsChildOf(viewport))ShowPalette(Point(e));
    }
    private CardGraphEdge Edge(CardGraphNode other,CardNodePort port)=>pendingPort!.Output?new(){From=pendingNode!.Id,Output=pendingPort.Id,To=other.Id,Input=port.Id}:new(){From=other.Id,Output=port.Id,To=pendingNode!.Id,Input=pendingPort.Id};
    internal void HoverPort(CardGraphNode other,CardNodePort port)
    {
        if(pendingNode==null||pendingPort==null)
        {
            var links=Graph.Edges.Where(e=>port.Output?e.From==other.Id&&e.Output==port.Id:e.To==other.Id&&e.Input==port.Id).ToArray();
            string state=links.Length>0?"已连接 "+links.Length+" 条":port.Output?"尚未连接":port.Required?"必须连接":port.Type==CardPortType.Number?"使用输入框数值":"默认："+CustomCardNames.Name(other.Subject,CustomCardNames.Objects);
            information.text=CardNodeCatalog.Title(other)+" · "+port.Name+" / "+CardBlueprintCompiler.TypeName(port.Type)+" · "+state;information.color=CustomCardVisuals.Muted;return;
        }
        if(port.Output==pendingPort.Output){information.text="请连接一个输出和一个输入。";return;}
        var error=CardBlueprintCompiler.ConnectionError(Graph,Edge(other,port),document.Targeted);
        information.text=error??"可以连接："+port.Name;information.color=error==null?CustomCardUi.SuccessText:CustomCardUi.ErrorText;
        foreach(var glyph in surface.GetComponentsInChildren<CustomCardPortGlyph>()){var handler=glyph.GetComponent<CustomCardGraphPortInput>();glyph.Rejected=handler.Node.Id==other.Id&&handler.Port.Id==port.Id&&handler.Port.Output==port.Output&&error!=null;glyph.SetVerticesDirty();}
    }
    private void FinishConnection(CardGraphNode node,CardNodePort port)
    {
        if(pendingPort==null||pendingNode==null||pendingPort.Output==port.Output)return;
        var edge=Edge(node,port);var error=CardBlueprintCompiler.ConnectionError(Graph,edge,document.Targeted);
        if(error!=null){information.text=error;information.color=CustomCardUi.ErrorText;return;}
        Edit(()=>CardBlueprintCompiler.Connect(Graph,edge,document.Targeted,out _));CancelConnection();
    }
    private void CancelConnection(){pendingNode=null;pendingPort=null;if(lines!=null)lines.SetVerticesDirty();if(surface!=null)foreach(var pin in surface.GetComponentsInChildren<CustomCardPortGlyph>()){pin.Compatible=pin.Rejected=false;pin.SetVerticesDirty();}}
    internal bool CancelInteraction()
    {
        bool active=pendingNode!=null||moving||dragging||boxing;
        if(moving)foreach(var n in Graph.Nodes.Where(n=>dragStart.ContainsKey(n.Id))){n.X=dragStart[n.Id].x;n.Y=dragStart[n.Id].y;}
        CancelConnection();moving=dragging=boxing=false;if(selectionBox!=null)Destroy(selectionBox.gameObject);selectionBox=null;
        if(active)Refresh();return active;
    }
    private void FillPalette(Transform target,bool compact,Vector2? position)
    {
        CustomCardUiLifetime.Clear(target,viewport.gameObject);
        if(!compact)CustomCardUi.AddTmpText(target,"节点库",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,20);
        var input=CustomCardUi.AddTmpInput(target,search,"搜索节点",_=>{},compact?420:170,36);CustomCardFormStyle.Input(input);
        var categories=new[]{"全部","触发入口","卡牌效果","流程控制","读取数据","数值与条件","对象与选择","本次变量","画布注释","常用组合"};
        if(compact)Select(target,categories,Array.IndexOf(categories,category),i=>{category=categories[i];FillPalette(target,compact,position);},420);
        var list=Column(target,"PaletteItems");
        void Fill(string query)
        {
            search=query;CustomCardUiLifetime.Clear(list,viewport.gameObject);int matches=0;
            bool Header(string name)
            {
                if(compact||query.Length>0)return true;
                var header=Button(list,name+(collapsedCategories.Contains(name)?"  ＋":"  −"),()=>{if(!collapsedCategories.Remove(name))collapsedCategories.Add(name);Fill(query);},170,36);
                Quiet(header);var title=header.GetComponentInChildren<TMP_Text>();title.color=CustomCardVisuals.Muted;title.fontSize=12;title.alignment=TextAlignmentOptions.MidlineLeft;
                return !collapsedCategories.Contains(name);
            }
            if(compact&&pendingNode==null&&insertingEdge==null&&(category=="全部"||category=="常用组合")&&Header("常用组合"))
                foreach(int index in new[]{1,3,4,5,6})
                {int template=index;var name=CardBlueprintTemplates.Names[index];if(!CardNodeCatalog.TemplateMatches(index,query))continue;matches++;Quiet(Button(list,name,()=>InsertTemplateFragment(template,position),compact?420:170,42));}
            var headings=new HashSet<string>();
            foreach(var candidate in CardNodeCatalog.Library().Where(n=>(!compact||category=="全部"||CardNodeCatalog.Category(n)==category)&&CardNodeCatalog.Matches(n,query)).OrderBy(n=>Array.IndexOf(categories,CardNodeCatalog.Category(n))))
            {
                var existing=candidate.Kind==CardNodeKind.Entry?Graph.Nodes.FirstOrDefault(n=>n.Kind==CardNodeKind.Entry&&n.Trigger==candidate.Trigger):null;
                if(existing!=null&&compact)continue;
                var compatible=pendingPort==null?null:CardNodeCatalog.Ports(candidate).FirstOrDefault(p=>p.Output!=pendingPort.Output&&p.Type==pendingPort.Type);
                if(pendingPort!=null&&compatible==null)continue;
                if(insertingEdge!=null)
                {
                    var edge=Graph.Edges.FirstOrDefault(e=>e.Id==insertingEdge);var original=edge==null?null:Graph.Nodes.FirstOrDefault(n=>n.Id==edge.From);
                    var type=original==null?null:CardNodeCatalog.Ports(original).FirstOrDefault(p=>p.Output&&p.Id==edge!.Output)?.Type;
                    var pins=CardNodeCatalog.Ports(candidate);
                    if(!pins.Any(p=>!p.Output&&p.Type==type)||!pins.Any(p=>p.Output&&p.Type==type&&(type!=CardPortType.Execution||p.Id=="next")))continue;
                }
                var categoryName=CardNodeCatalog.Category(candidate);
                if(headings.Add(categoryName))Header(categoryName);
                if(!compact&&query.Length==0&&collapsedCategories.Contains(categoryName))continue;
                var item=candidate;var pin=compatible;
                matches++;
                var button=Button(list,CardNodeCatalog.Title(item),()=>{if(existing!=null)FocusNode(existing.Id);else AddNode(item,position,pin);},compact?420:170,36);
                CustomCardControls.Style(button,CardControlKind.Quiet);button.GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
            }
            if(!compact&&Header("常用组合"))foreach(int index in new[]{1,3,4,5,6}){int template=index;if(!CardNodeCatalog.TemplateMatches(index,query))continue;matches++;Quiet(Button(list,CardBlueprintTemplates.Names[index],()=>InsertTemplateFragment(template,position),170,40));}
            if(matches==0&&(query.Length>0||compact))Hint(list,"没有匹配的节点或组合。试试名称、用途或 BUFF。");
        }
        input.onValueChanged.RemoveAllListeners();input.onValueChanged.AddListener(Fill);Fill(search);
    }
    private void ShowPalette(Vector2? position)
    {
        ClosePopup();insertingEdge=null;popup=Overlay("CustomCards.NodePalette",transform,"添加节点",()=>{popup=null;insertingEdge=null;CancelConnection();},maxWidth:640);
        var list=CustomCardUi.CreateScroll(popup.transform,"CompatibleNodes");FillPalette(list,true,position);
    }
    private void AddNode(CardGraphNode prototype,Vector2? position,CardNodePort? compatible)
    {
        var n=JsonConvert.DeserializeObject<CardGraphNode>(JsonConvert.SerializeObject(prototype))!;n.Id=Guid.NewGuid().ToString("N");
        var point=position??new Vector2((80-Graph.PanX)/Graph.Zoom,(100-Graph.PanY)/Graph.Zoom);n.X=point.x;n.Y=point.y;
        var origin=pendingNode;var originPort=pendingPort;
        if(insertingEdge!=null)
        {
            var error=CardBlueprintCompiler.InsertionError(Graph,insertingEdge,n,document.Targeted);if(error!=null){report(error);return;}
            var old=insertingEdge;var edge=Graph.Edges.First(e=>e.Id==old);var from=Graph.Nodes.First(x=>x.Id==edge.From);var target=Graph.Nodes.First(x=>x.Id==edge.To);
            Edit(()=>
            {
                var origin=foldedRepresentative.ContainsKey(from.GroupId)?GroupAnchor(from.GroupId):from;
                n.X=origin.X+NodeWidth+64;n.Y=origin.Y;
                var movingIds=new HashSet<string>();var queue=new Queue<string>();queue.Enqueue(target.Id);
                while(queue.Count>0)
                {
                    var id=queue.Dequeue();if(!movingIds.Add(id))continue;
                    foreach(var next in Graph.Edges.Where(e=>e.From==id))queue.Enqueue(next.To);
                    var member=Graph.Nodes.FirstOrDefault(x=>x.Id==id);if(member!=null&&foldedRepresentative.ContainsKey(member.GroupId))foreach(var sibling in Graph.Nodes.Where(x=>x.GroupId==member.GroupId))queue.Enqueue(sibling.Id);
                }
                float shift=Math.Max(0,n.X+NodeWidth+64-target.X);foreach(var moved in Graph.Nodes.Where(x=>movingIds.Contains(x.Id)))moved.X+=shift;
                CardBlueprintCompiler.Insert(Graph,old,n,document.Targeted,out _);
            });selectedEdge=null;insertingEdge=null;ClosePopup();return;
        }
        record();Graph.Nodes.Add(n);
        if(origin!=null&&originPort!=null&&compatible!=null)
        {
            var edge=Edge(n,compatible);
            if(!CardBlueprintCompiler.Connect(Graph,edge,document.Targeted,out var error)){Graph.Nodes.Remove(n);report(error??"无法连接。");return;}
        }
        selected.Clear();selected.Add(n.Id);CancelConnection();ClosePopup();changed(CustomCardGraphChange.Content);Refresh();
    }
    private void InsertTemplateFragment(int index,Vector2? position)
    {
        var fragment=CardBlueprintTemplates.Create(index).Graph;
        fragment.Delete(fragment.Nodes.Where(n=>n.Kind==CardNodeKind.Entry).Select(n=>n.Id).ToArray());
        var group=new CardGraphGroup{Name=CardBlueprintTemplates.Names[index]};fragment.Groups.Add(group);foreach(var n in fragment.Nodes)n.GroupId=group.Id;
        Edit(()=>{var at=position??new(100,100);selected.Clear();foreach(var id in Graph.Paste(fragment,at.x-fragment.Nodes.Min(n=>n.X),at.y-fragment.Nodes.Min(n=>n.Y)))selected.Add(id);});ClosePopup();
    }
    private void ClosePopup(){popupInspector=null;if(popup!=null){var old=popup;popup=null;CustomCardUiLifetime.Destroy(old.transform.parent.gameObject,viewport!=null?viewport.gameObject:null);}insertingEdge=null;}
    private void ShowEditMenu()
    {
        ClosePopup();popup=Overlay("CustomCards.GraphEdit",transform,"蓝图编辑",()=>popup=null,maxWidth:480,preferredHeight:430);
        var list=CustomCardUi.CreateScroll(popup.transform,"GraphEditActions");
        Button(list,"复制选中节点 · Ctrl+C",()=>{Copy();ClosePopup();},300);
        Button(list,"粘贴节点 · Ctrl+V",()=>{Paste();ClosePopup();},300);
        Button(list,"删除选中内容 · Delete",()=>{Delete();ClosePopup();},300);
        Button(list,"组合 / 折叠",()=>{Group();ClosePopup();},300);
        Button(list,"在线路中插入节点",()=>{if(selectedEdge==null){report("先点击一条连线，再插入节点。");ClosePopup();return;}var id=selectedEdge.Id;ShowPalette(null);insertingEdge=id;var list=popup!.GetComponentsInChildren<ScrollRect>().First().content;FillPalette(list,true,null);},300);
        Hint(list,"拖动标题移动节点；空格或中键平移；滚轮缩放；右击接口断线。",CustomCardVisuals.Muted);
    }
    internal void ShowInspector()
    {
        var selectedNode=Graph.Nodes.FirstOrDefault(n=>selected.Contains(n.Id));var collapsed=Graph.Groups.FirstOrDefault(g=>g.Id==selectedNode?.GroupId&&g.Collapsed);
        if(collapsed!=null){Edit(()=>collapsed.Collapsed=false,false);return;}
        if(layoutWidth>=680&&selectedNode!=null){ClosePopup();inspectorCollapsed=false;Resize();FillInspector(inspector);return;}
        ClosePopup();popup=Overlay("CustomCards.NodeInspector",transform,"节点参数",()=>{popup=null;popupInspector=null;},maxWidth:560,preferredHeight:720);
        var list=CustomCardUi.CreateScroll(popup.transform,"NodeParameters");popupInspector=list;FillInspector(list);
    }
    private void FillInspector(Transform target)
    {
        if(target==null)return;CustomCardUiLifetime.Clear(target,viewport.gameObject);var n=Graph.Nodes.FirstOrDefault(x=>selected.Contains(x.Id));
        CustomCardUi.AddTmpText(target,"节点参数",12,TextAnchor.MiddleLeft,CustomCardVisuals.Muted,24);
        if(n==null){Hint(target,selectedEdge!=null?"选中连线，按 Delete 删除。":"选中节点后修改参数。");return;}
        var heading=CommandRow(target,"InspectorHeading");CustomCardUi.AddTmpText(heading,CardNodeCatalog.Title(n),16,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,36,1);
        CustomCardControls.IconButton(heading,CardIcon.Help,"节点说明",()=>
        {
            if(guide!=null)guide(n);else CustomCardGuide.Show(transform,transform,n);
        });
        void Update(Action action)=>Edit(action);
        var selectedGroup=Graph.Groups.FirstOrDefault(g=>g.Id==n.GroupId&&g.Collapsed);
        if(selectedGroup!=null)
        {
            Hint(target,"组合："+selectedGroup.Name);
            var nameInput=CustomCardUi.AddTmpInput(target,selectedGroup.Name,"组合名称",_=>{},218,38);CustomCardFormStyle.Input(nameInput);CustomCardInputFeedback.Bind(nameInput,v=>Update(()=>selectedGroup.Name=v));
            foreach(var member in Graph.Nodes.Where(m=>m.GroupId==selectedGroup.Id))
            {
                var item=member;CustomCardControls.Rule(target);Hint(target,CardNodeCatalog.Title(item),CustomCardVisuals.Gold);
                Quiet(Button(target,"定位此节点",()=>{if(CustomCardInputFeedback.CommitAll(transform))FocusNode(item.Id);},120));
                FillNodeParameters(target,item);
            }
            Button(target,"展开组合",()=>Update(()=>selectedGroup.Collapsed=false),218);return;
        }
        FillNodeParameters(target,n);
        var group=Graph.Groups.FirstOrDefault(g=>g.Id==n.GroupId);
        if(group!=null)
        {
            Button(target,group.Collapsed?"展开组合":"折叠组合",()=>Update(()=>group.Collapsed=!group.Collapsed),218);
            Button(target,"取消组合",()=>Update(()=>{foreach(var member in Graph.Nodes.Where(m=>m.GroupId==group.Id))member.GroupId="";Graph.Groups.Remove(group);}),218);
        }
        if(n.Label.Length==0)Quiet(Button(target,"添加备注",()=>Update(()=>n.Label="备注"),110));
    }
    private void FillNodeParameters(Transform target,CardGraphNode n)
    {
        void Update(Action action)=>Edit(action);
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Read)
        {
            Hint(target,"读取属性");Select(target,CustomCardNames.Fields,(int)n.Field,i=>Update(()=>{n.Field=(CardDataField)i;if(CardNodeCatalog.FixedOwner(n)){n.Subject=CardObjectKind.Self;Graph.Edges.RemoveAll(e=>e.To==n.Id&&e.Input=="object");}}),218);
        }
        if(n.Kind==CardNodeKind.Value&&CardBlueprintNumbers.Comparison(n.ValueKind))
        {
            Hint(target,"比较关系");Select(target,CardBlueprintNumbers.Comparisons.Select(CardBlueprintNumbers.Operator).ToArray(),Array.IndexOf(CardBlueprintNumbers.Comparisons,n.ValueKind),i=>Update(()=>n.ValueKind=CardBlueprintNumbers.Comparisons[i]),218);
        }
        if(n.Kind==CardNodeKind.Note||n.Label.Length>0)
        {
            Hint(target,n.Kind==CardNodeKind.Note?"注释内容":"备注");
            var label=CustomCardUi.AddTmpInput(target,n.Label,n.Kind==CardNodeKind.Note?"在这里填写说明":"可选备注",_=>{},218,n.Kind==CardNodeKind.Note?150:38);
            CustomCardFormStyle.Input(label);label.characterLimit=500;
            if(n.Kind==CardNodeKind.Note){label.lineType=TMP_InputField.LineType.MultiLineNewline;label.textComponent.textWrappingMode=TextWrappingModes.Normal;label.textComponent.alignment=TextAlignmentOptions.TopLeft;}
            CustomCardInputFeedback.Bind(label,v=>Update(()=>n.Label=v));
        }
        if(CardNodeCatalog.HasResultName(n))
        {
            Hint(target,"结果名称");var name=CustomCardUi.AddTmpInput(target,n.VariableName,"结果名称",_=>{},218,38);CustomCardFormStyle.Input(name);name.characterLimit=32;CustomCardInputFeedback.Bind(name,v=>Update(()=>n.VariableName=v),v=>string.IsNullOrWhiteSpace(v)?"请填写结果名称。":null);
        }
        if(n.Kind==CardNodeKind.Unsupported){Hint(target,"此旧稿监听不再执行。原始规则已保留在导出稿件中。请明确删除，或重新设计离散入口。",CustomCardUi.ErrorText);return;}
        if(n.Kind==CardNodeKind.Entry){Hint(target,"本牌的离散时点。每次触发执行一次；不持续监听战斗。使用和丢弃遵循原生卡牌规则，使用后的入弃牌堆也可能触发丢弃。");return;}
        if(n.Kind==CardNodeKind.Note)
        {
            void Size(double value,bool width){if(double.IsNaN(value)||double.IsInfinity(value)){report("请输入有效的注释尺寸。");return;}Update(()=>{if(width)n.NoteWidth=(float)Math.Max(180,Math.Min(1400,value));else n.NoteHeight=(float)Math.Max(96,Math.Min(1400,value));});}
            Hint(target,"宽度 / 高度（也可拖动右下角）");NumberInput(target,n.NoteWidth,v=>Size(v,true),218);NumberInput(target,n.NoteHeight,v=>Size(v,false),218);return;
        }
        if(CardNodeCatalog.EditableObjectDefault(n))
        {
            var objects=CardNodeCatalog.ObjectDefaults(n).ToArray();
            if(Graph.Edges.Any(e=>e.To==n.Id&&e.Input=="object"))Hint(target,"对象由连线提供；断开后恢复默认："+CustomCardNames.Name(n.Subject,CustomCardNames.Objects));
            else {Hint(target,"目标");Select(target,objects.Select(o=>CustomCardNames.Name(o,CustomCardNames.Objects)).ToArray(),Array.IndexOf(objects,n.Subject),i=>Update(()=>n.Subject=objects[i]),218);}
        }
        if(CardNodeCatalog.RequiresState(n))
        {
            Hint(target,"具体状态 · 必选");CustomCardStateSelector.Create(target,n.ResourceId,()=>ChooseState(transform,n));
            var state=CustomCardNative.State(n.ResourceId);
            if(state!=null)Hint(target,"来源："+state.SourceLabel);
            else Hint(target,n.ResourceId.Length==0?"尚未选择具体状态。":"原状态未加载，请重新选择。",CustomCardVisuals.Error);
            Hint(target,n.Kind==CardNodeKind.Value?"读取当前层数；未持有时为 0。":n.Effect==CardEffectKind.RemoveBuff?"移除整个指定状态。":"添加指定层数；叠加规则由状态决定。");
        }
        if(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Number){Hint(target,"固定数值");NumericEditor(CommandRow(target,"ConstantNumber"),n,"a",0,0,218,true);}
        foreach(var p in CardNodeCatalog.Ports(n).Where(p=>!p.Output&&p.Type==CardPortType.Number))
        {
            var pin=p;Hint(target,p.Name+(Graph.Edges.Any(e=>e.To==n.Id&&e.Input==p.Id)?"（由连线提供）":""));
            var edge=Graph.Edges.FirstOrDefault(e=>e.To==n.Id&&e.Input==p.Id);
            if(edge!=null){var source=Graph.Nodes.FirstOrDefault(s=>s.Id==edge.From);Quiet(Button(target,"来自："+(source==null?"来源不存在":CardNodeCatalog.Title(source)),()=>FocusNode(edge.From),218));continue;}
            NumericEditor(CommandRow(target,"NumberParameter."+p.Id),n,p.Id,0,0,218,true);
        }
        if(n.Kind==CardNodeKind.If||n.Kind==CardNodeKind.Repeat||n.Kind==CardNodeKind.ForEach)Hint(target,"子流程末端返回本节点，再执行“完成后”。结束本次流程会直接结束整个入口。");
        if(CardNodeCatalog.IsPicker(n.Kind))Hint(target,"只执行一个出口。选择结果仅能用于“已选中”分支及其内部流程；多个数据连线不会重复抽样。");
    }
    internal void FocusNode(string id)
    {
        var n=Graph.Nodes.FirstOrDefault(x=>x.Id==id);if(n==null){ShowDiagnostics();return;}
        var group=Graph.Groups.FirstOrDefault(g=>g.Id==n.GroupId);if(group!=null)group.Collapsed=false;
        selected.Clear();selected.Add(id);Graph.PanX=Math.Max(16,(viewport.rect.width-NodeWidth*Graph.Zoom)*.5f)-n.X*Graph.Zoom;Graph.PanY=Math.Max(16,(viewport.rect.height-NodeHeight(n)*Graph.Zoom)*.5f)-n.Y*Graph.Zoom;Graph.HasView=true;initialFit=false;Refresh();changed(CustomCardGraphChange.View);
    }
    private void ShowDiagnostics()
    {
        if(!CustomCardInputFeedback.CommitAll(transform))return;
        ClosePopup();popup=Overlay("CustomCards.GraphIssues",transform,"蓝图检查",()=>popup=null,maxWidth:840);
        var list=CustomCardUi.CreateScroll(popup.transform,"GraphIssues");var validation=CustomCardCompiler.Compile(document,CustomCardNative.BuffName);
        Hint(list,validation.Success?"可以制作。" : "请修正以下问题后制作。",validation.Success?CustomCardUi.SuccessText:CustomCardUi.ErrorText);
        foreach(var section in new[]{(Title:"需要修正",Items:validation.Issues),(Title:"提示",Items:validation.Warnings)})
        {
            if(section.Items.Count==0)continue;
            Hint(list,section.Title+" · "+section.Items.Count,section.Title=="提示"?CustomCardVisuals.Muted:CustomCardVisuals.Error);
            foreach(var issue in section.Items)
            {
                var item=issue;var node=Graph.Nodes.FirstOrDefault(n=>n.Id==item.NodeId);
                string field=item.FieldId==CardNodeCatalog.StateParameter?"具体状态":node==null?"":CardNodeCatalog.Ports(node).FirstOrDefault(p=>p.Id==item.PortId)?.Name??"";
                var entry=Column(list,"Diagnostic");CustomCardUi.AddImage(entry.gameObject,CustomCardVisuals.Node);entry.GetComponent<VerticalLayoutGroup>().padding=new(12,12,10,10);
                Hint(entry,(node==null?"作品":CardNodeCatalog.Title(node))+(field.Length>0?" · "+field:""),CustomCardVisuals.Ink);Hint(entry,item.Message);
                var actions=Row(entry,"DiagnosticActions");
                if(node!=null)Button(actions,"定位节点",()=>{ClosePopup();FocusNode(node.Id);},100);
                if(node!=null&&item.FieldId==CardNodeCatalog.StateParameter)Button(actions,"选择状态",()=>{ClosePopup();FocusNode(node.Id);ChooseState(transform,node);},110);
                if(item.EdgeId.Length>0)Button(actions,"删除这条无效连接",()=>{Edit(()=>Graph.Edges.RemoveAll(e=>e.Id==item.EdgeId));ShowDiagnostics();},190);
            }
        }
    }
    private void Copy()
    {
        GUIUtility.systemCopyBuffer=JsonConvert.SerializeObject(new {Format="AuraTools.CardFragment",Graph=Graph.Fragment(selected)});information.text="已复制选中的节点及内部连线。";
    }
    private void Paste()
    {
        try
        {
            string json=GUIUtility.systemCopyBuffer;if(json.Length>1024*1024)throw new InvalidOperationException("片段过大。");
            using var reader=new JsonTextReader(new System.IO.StringReader(json)){MaxDepth=80};var obj=Newtonsoft.Json.Linq.JObject.Load(reader);
            if((string?)obj["Format"]!="AuraTools.CardFragment")throw new InvalidOperationException("剪贴板不是蓝图片段。");
            var fragment=obj["Graph"]!.ToObject<CardBlueprint>()!;CardBlueprintMigration.CheckEnvelope(new(){Graph=fragment});
            if(fragment.Nodes.Any(n=>n.Kind==CardNodeKind.Entry&&Graph.Nodes.Any(x=>x.Kind==CardNodeKind.Entry&&x.Trigger==n.Trigger)))throw new InvalidOperationException("粘贴包含重复入口，请仅复制入口后的片段。");
            if(Graph.Nodes.Count+fragment.Nodes.Count>1024)throw new InvalidOperationException("草稿节点数量超限。");
            Edit(()=>{selected.Clear();foreach(var id in Graph.Paste(fragment))selected.Add(id);});
        }
        catch(Exception ex){report("无法粘贴："+ex.Message);}
    }
    internal void Delete()
    {
        if(selectedEdge!=null){var id=selectedEdge.Id;Edit(()=>Graph.Edges.RemoveAll(e=>e.Id==id));selectedEdge=null;return;}
        if(selected.Count>0)Edit(()=>Graph.Delete(selected.ToArray()));
    }
    private void Group()
    {
        if(selected.Count==0)return;
        var existing=Graph.Groups.FirstOrDefault(g=>Graph.Nodes.Where(n=>selected.Contains(n.Id)).All(n=>n.GroupId==g.Id));
        Edit(()=>
        {
            if(existing!=null){existing.Collapsed=!existing.Collapsed;return;}
            var group=new CardGraphGroup{Name="组合片段"};Graph.Groups.Add(group);foreach(var n in Graph.Nodes.Where(n=>selected.Contains(n.Id)))n.GroupId=group.Id;
        },false);
    }
    internal void Fit()
    {
        if(views.Count==0)return;
        float left=views.Values.Min(v=>v.anchoredPosition.x),top=views.Values.Min(v=>-v.anchoredPosition.y),right=views.Values.Max(v=>v.anchoredPosition.x+v.rect.width),bottom=views.Values.Max(v=>-v.anchoredPosition.y+v.rect.height);
        Graph.Zoom=Mathf.Clamp(Math.Min((viewport.rect.width-48)/Math.Max(1,right-left),(viewport.rect.height-48)/Math.Max(1,bottom-top)),.4f,1f);
        Graph.PanX=24-left*Graph.Zoom;Graph.PanY=24-top*Graph.Zoom;Graph.HasView=true;ApplyView();changed(CustomCardGraphChange.View);
    }
    private void Arrange()
    {
        Edit(()=>CardBlueprintLayout.Arrange(Graph),false);Fit();
    }
    private void Resize()
    {
        if(viewport==null)return;float width=(transform as RectTransform)!.rect.width;
        layoutWidth=width;
        paletteRoot.SetActive(width>=680&&!paletteCollapsed);
        inspectorRoot.SetActive(width>=680&&!inspectorCollapsed);
        paletteRoot.GetComponent<LayoutElement>().preferredWidth=width>=1100?212:172;
        inspectorRoot.GetComponent<LayoutElement>().preferredWidth=width>=1100?304:232;
        if(miniMap!=null)miniMap.gameObject.SetActive(viewport.rect.width>=420);
        var ownerScroll=transform.parent.GetComponentInParent<ScrollRect>();
        if(ownerScroll!=null)
        {
            float available=ownerScroll.viewport.rect.height-(toolbar as RectTransform)!.rect.height-34;
            if(Math.Abs(workspaceSize.preferredHeight-available)>2)workspaceSize.minHeight=workspaceSize.preferredHeight=Math.Max(140,available);
        }
    }
    private void OnRectTransformDimensionsChange(){resizePending=true;}
    private void LateUpdate(){if(resizePending&&viewport!=null){resizePending=false;Resize();}if(initialFit&&viewport!=null&&viewport.rect.width>0){Canvas.ForceUpdateCanvases();Resize();Canvas.ForceUpdateCanvases();initialFit=false;Fit();}}
    private bool TextFocused()=>CustomCardUiLifetime.TextFocused();
    private void Update()
    {
        if(viewport==null||EventSystem.current==null||popup!=null||TextFocused()||!CustomCardInputState.AnyDown)return;
        // Only receive shortcuts while the pointer belongs to this editor; other tool overlays retain their input.
        var e=new PointerEventData(EventSystem.current){position=CustomCardInputState.Pointer};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
        if(hits.Count==0||!hits[0].gameObject.transform.IsChildOf(transform))return;
        if(CustomCardInputState.Down(KeyCode.Escape))CancelInteraction();
        if(CustomCardInputState.Down(KeyCode.Delete)||CustomCardInputState.Down(KeyCode.Backspace))Delete();
        bool ctrl=CustomCardInputState.Held(KeyCode.LeftControl)||CustomCardInputState.Held(KeyCode.RightControl);
        if(ctrl&&CustomCardInputState.Down(KeyCode.C))Copy();if(ctrl&&CustomCardInputState.Down(KeyCode.V))Paste();
        if(CustomCardInputState.Down(KeyCode.F))Fit();
    }
    private void OnDisable(){CustomCardUiLifetime.ReleaseFocus(transform);CancelConnection();moving=dragging=boxing=false;ClosePopup();}
    private void OnApplicationFocus(bool focused){if(!focused)CancelInteraction();}
    internal void PanTo(Vector2 point)
    {
        initialFit=false;Graph.HasView=true;
        Graph.PanX=viewport.rect.width*.5f-point.x*Graph.Zoom;Graph.PanY=viewport.rect.height*.5f-point.y*Graph.Zoom;ApplyView();changed(CustomCardGraphChange.View);
    }
    internal IEnumerable<(Vector2 A,Vector2 B,Color Color,float Width)> Segments()
    {
        foreach(var e in Graph.Edges)
        {
            if(!ports.TryGetValue(Key(e.From,e.Output,true),out var a)||!ports.TryGetValue(Key(e.To,e.Input,false),out var b))continue;
            var from=Graph.Nodes.FirstOrDefault(n=>n.Id==e.From);if(from==null)continue;
            var port=CardNodeCatalog.Ports(from).FirstOrDefault(p=>p.Output&&p.Id==e.Output);if(port==null)continue;
            var color=e.Id==selectedEdge?.Id?CustomCardUi.SuccessText:PortColor(port.Type);

            foreach(var pair in Curve(a,b))yield return(pair.A,pair.B,color,(e.Id==selectedEdge?.Id?3f:1.8f)/Graph.Zoom);
        }
        if(pendingNode!=null&&pendingPort!=null&&ports.TryGetValue(Key(pendingNode.Id,pendingPort.Id,pendingPort.Output),out var start))
        {
            var end=new Vector2(pointer.x,-pointer.y);foreach(var pair in Curve(pendingPort.Output?start:end,pendingPort.Output?end:start))yield return(pair.A,pair.B,PortColor(pendingPort.Type),2f/Graph.Zoom);
        }
    }
    private IEnumerable<(Vector2 A,Vector2 B)> Curve(Vector2 a,Vector2 b)
    {
        float bend=Math.Max(65,Math.Abs(b.x-a.x)*.45f);var c=a+Vector2.right*bend;var d=b-Vector2.right*bend;var last=a;
        int samples=Math.Max(4,Math.Min(24,14000/Math.Max(1,Graph.Edges.Count)));
        for(int i=1;i<=samples;i++){float t=(float)i/samples,u=1-t;var p=u*u*u*a+3*u*u*t*c+3*u*t*t*d+t*t*t*b;yield return(last,p);last=p;}
    }
    private CardGraphEdge? HitEdge(Vector2 point)
    {
        foreach(var e in Graph.Edges)
        {
            if(!ports.TryGetValue(Key(e.From,e.Output,true),out var a)||!ports.TryGetValue(Key(e.To,e.Input,false),out var b))continue;
            foreach(var segment in Curve(a,b))
            {
                var delta=segment.B-segment.A;var t=delta.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(point-segment.A,delta)/delta.sqrMagnitude):0;
                if(Vector2.Distance(point,segment.A+t*delta)<=8/Graph.Zoom)return e;
            }
        }
        return null;
    }
}

internal sealed class CustomCardGraphNodeInput : MonoBehaviour,IPointerClickHandler,IBeginDragHandler,IDragHandler,IEndDragHandler,IPointerEnterHandler,IPointerExitHandler
{
    internal CustomCardGraphEditor Editor=null!;internal string NodeId="";
    public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){Editor.SelectNode(NodeId,CustomCardInputState.Held(KeyCode.LeftControl)||CustomCardInputState.Held(KeyCode.RightControl));if(e.clickCount>1)Editor.ShowInspector();}}
    public void OnBeginDrag(PointerEventData e)=>Editor.BeginMove(NodeId,e);
    public void OnDrag(PointerEventData e)=>Editor.Move(e);
    public void OnEndDrag(PointerEventData e)=>Editor.EndMove();
    public void OnPointerEnter(PointerEventData e)=>Editor.HoverNode(NodeId,true);
    public void OnPointerExit(PointerEventData e)=>Editor.HoverNode(NodeId,false);
}
internal sealed class CustomCardNoteResize : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    internal CustomCardGraphEditor Editor=null!;internal CardGraphNode Node=null!;private Vector2 start,size;
    public void OnBeginDrag(PointerEventData e){start=Editor.Point(e);size=new(Node.NoteWidth,Node.NoteHeight);Editor.BeginNoteResize();}
    public void OnDrag(PointerEventData e)
    {
        // Keep this drag handler alive; commit the redraw once the pointer is released.
        var delta=Editor.Point(e)-start;Node.NoteWidth=Mathf.Clamp(size.x+delta.x,180,1400);Node.NoteHeight=Mathf.Clamp(size.y+delta.y,96,1400);
        (transform.parent as RectTransform)!.sizeDelta=new(Node.NoteWidth,Node.NoteHeight);
        (transform as RectTransform)!.anchoredPosition=new(Node.NoteWidth-26,-Node.NoteHeight+26);
    }
    public void OnEndDrag(PointerEventData e){Editor.EndNoteResize();Editor.Refresh();}
}
internal sealed class CustomCardGraphPortInput : MonoBehaviour,IPointerDownHandler,IDragHandler,IEndDragHandler,IBeginDragHandler,IPointerUpHandler,IPointerEnterHandler
{
    internal CustomCardGraphEditor Editor=null!;internal CardGraphNode Node=null!;internal CardNodePort Port=null!;
    public void OnPointerDown(PointerEventData e)=>Editor.PortDown(Node,Port,e);
    public void OnBeginDrag(PointerEventData e){}
    public void OnDrag(PointerEventData e)=>Editor.DragPort(e);
    public void OnEndDrag(PointerEventData e)=>Editor.ReleasePort(e);
    public void OnPointerUp(PointerEventData e){if(!e.dragging)Editor.ReleasePort(e);}
    public void OnPointerEnter(PointerEventData e)=>Editor.HoverPort(Node,Port);
}
internal sealed class CustomCardGraphInput : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler,IScrollHandler,IPointerClickHandler
{
    internal CustomCardGraphEditor Editor=null!;
    public void OnBeginDrag(PointerEventData e)=>Editor.StartBackground(e);
    public void OnDrag(PointerEventData e)=>Editor.DragBackground(e);
    public void OnEndDrag(PointerEventData e)=>Editor.EndBackground(e);
    public void OnScroll(PointerEventData e)=>Editor.Scroll(e);
    public void OnPointerClick(PointerEventData e)=>Editor.BackgroundClick(e);
}
internal sealed class CustomCardGraphLines : MaskableGraphic
{
    internal CustomCardGraphEditor Editor=null!;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(Editor==null)return;
        foreach(var segment in Editor.Segments())Quad(mesh,segment.A,segment.B,segment.Color,segment.Width);
    }
    internal static void Quad(VertexHelper mesh,Vector2 a,Vector2 b,Color color,float width)
    {
        var delta=b-a;if(delta.sqrMagnitude<.0001f)return;var normal=new Vector2(-delta.y,delta.x).normalized*width*.5f;int start=mesh.currentVertCount;
        foreach(var p in new[]{a-normal,a+normal,b+normal,b-normal})mesh.AddVert(p,color,Vector2.zero);
        mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
    }
}
internal sealed class CustomCardGraphGrid : RawImage
{
    private Texture2D? owned;
    protected override void Awake()
    {
        base.Awake();owned=new Texture2D(24,24,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
        var pixels=new Color32[24*24];pixels[12*24+12]=new Color32(115,99,144,110);owned.SetPixels32(pixels);owned.Apply();texture=owned;
    }
    protected override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();uvRect=new(0,0,rectTransform.rect.width/24,rectTransform.rect.height/24);}
    protected override void OnDestroy(){if(owned!=null)Destroy(owned);base.OnDestroy();}
}

internal sealed class CustomCardGraphMiniMap : MaskableGraphic,IPointerDownHandler,IDragHandler
{
    internal CustomCardGraphEditor Editor=null!;
    private Rect bounds;
    private float scale;
    private Vector2 offset;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(Editor==null)return;var nodes=Editor.Blueprint.Nodes;if(nodes.Count==0)return;
        var r=rectTransform.rect;Fill(mesh,r,CustomCardVisuals.Canvas);
        bounds=Rect.MinMaxRect(nodes.Min(n=>n.X)-40,nodes.Min(n=>n.Y)-40,nodes.Max(n=>n.X+CustomCardGraphEditor.NodeWidth)+40,nodes.Max(n=>n.Y+180)+40);
        scale=Math.Min((r.width-12)/Math.Max(1,bounds.width),(r.height-12)/Math.Max(1,bounds.height));offset=new(r.xMin+6,r.yMax-6);
        foreach(var n in nodes)
        {
            var p=Map(new(n.X,n.Y));Fill(mesh,new Rect(p.x,p.y-100*scale,CustomCardGraphEditor.NodeWidth*scale,100*scale),Editor.Selection.Contains(n.Id)?CustomCardVisuals.Selected:CustomCardVisuals.Muted);
        }
        var graph=Editor.Blueprint;var a=Map(new(-graph.PanX/graph.Zoom,-graph.PanY/graph.Zoom));var b=Map(new((Editor.Viewport.rect.width-graph.PanX)/graph.Zoom,(Editor.Viewport.rect.height-graph.PanY)/graph.Zoom));
        a=new(Mathf.Clamp(a.x,r.xMin,r.xMax),Mathf.Clamp(a.y,r.yMin,r.yMax));b=new(Mathf.Clamp(b.x,r.xMin,r.xMax),Mathf.Clamp(b.y,r.yMin,r.yMax));
        CustomCardGraphLines.Quad(mesh,a,new(b.x,a.y),CustomCardUi.Text,1);CustomCardGraphLines.Quad(mesh,new(b.x,a.y),b,CustomCardUi.Text,1);CustomCardGraphLines.Quad(mesh,b,new(a.x,b.y),CustomCardUi.Text,1);CustomCardGraphLines.Quad(mesh,new(a.x,b.y),a,CustomCardUi.Text,1);
    }
    private Vector2 Map(Vector2 p)=>offset+new Vector2((p.x-bounds.xMin)*scale,-(p.y-bounds.yMin)*scale);
    private static void Fill(VertexHelper mesh,Rect r,Color color)
    {
        int i=mesh.currentVertCount;mesh.AddVert(new Vector2(r.xMin,r.yMin),color,Vector2.zero);mesh.AddVert(new Vector2(r.xMin,r.yMax),color,Vector2.zero);mesh.AddVert(new Vector2(r.xMax,r.yMax),color,Vector2.zero);mesh.AddVert(new Vector2(r.xMax,r.yMin),color,Vector2.zero);mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
    }
    private void Navigate(PointerEventData e){if(scale<=0)return;RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out var p);Editor.PanTo(new((p.x-offset.x)/scale+bounds.xMin,-(p.y-offset.y)/scale+bounds.yMin));}
    public void OnPointerDown(PointerEventData e)=>Navigate(e);
    public void OnDrag(PointerEventData e)=>Navigate(e);
}

// The game uses Input System. The isolated Unity 2022 preview also supports its legacy host input.
internal static class CustomCardInputState
{
#if UNITY_5_3_OR_NEWER && !ENABLE_INPUT_SYSTEM
    internal static bool AnyDown=>UnityEngine.Input.anyKeyDown;
    internal static Vector2 Pointer=>UnityEngine.Input.mousePosition;
    internal static bool Held(KeyCode key)=>UnityEngine.Input.GetKey(key);
    internal static bool Down(KeyCode key)=>UnityEngine.Input.GetKeyDown(key);
#else
    internal static bool AnyDown=>UnityEngine.InputSystem.Keyboard.current?.anyKey.wasPressedThisFrame==true;
    internal static Vector2 Pointer=>UnityEngine.InputSystem.Pointer.current?.position.ReadValue()??Vector2.zero;
    internal static bool Held(KeyCode key)=>Control(key)?.isPressed==true;
    internal static bool Down(KeyCode key)=>Control(key)?.wasPressedThisFrame==true;
    private static UnityEngine.InputSystem.Controls.KeyControl? Control(KeyCode key)
    {
        var keyboard=UnityEngine.InputSystem.Keyboard.current;if(keyboard==null)return null;
        return key switch
        {
            KeyCode.Space=>keyboard.spaceKey,KeyCode.LeftControl=>keyboard.leftCtrlKey,KeyCode.RightControl=>keyboard.rightCtrlKey,
            KeyCode.Escape=>keyboard.escapeKey,KeyCode.Delete=>keyboard.deleteKey,KeyCode.Backspace=>keyboard.backspaceKey,
            KeyCode.C=>keyboard.cKey,KeyCode.V=>keyboard.vKey,KeyCode.F=>keyboard.fKey,_=>null
        };
    }
#endif
}
