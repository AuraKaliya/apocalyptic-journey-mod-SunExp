using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static AuraToolsExp.Dll.Features.CustomCards.CustomCardWorkshopController;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>A read-only owned window; opening help must not replace or validate the user's editor.</summary>
internal sealed class CustomCardGuide:MonoBehaviour
{
    private static readonly string[] Chapters={"开始制作","连线与执行","数值与百分比","状态 / BUFF","组合与编辑","检查与制作","节点索引"};
    private readonly Dictionary<int,Button> navigation=new();
    private Transform body=null!;
    private IDisposable? editing;
    private int chapter,indexPage;
    private string query="";
    private string? nodeIdentity;
    internal static void Show(Transform parent,Transform editingScope,CardGraphNode? node=null)
    {
        var suspended=CustomCardInputFeedback.SuspendEditing(editingScope);
        try
        {
            var window=Overlay("CustomCards.Guide",parent,"节点指南",fullWindow:true);
            var guide=window.AddComponent<CustomCardGuide>();guide.editing=suspended;guide.nodeIdentity=node==null?null:CardNodeCatalog.Identity(node);guide.chapter=node==null?0:6;guide.Build();
        }
        catch{suspended.Dispose();throw;}
    }
    private void Close()=>CustomCardUiLifetime.Destroy(transform.parent.gameObject);
    private void OnDisable(){editing?.Dispose();editing=null;}
    private void Build()
    {
        var top=CommandRow(transform,"GuideToolbar");Quiet(Button(top,"返回编辑",Close,94));
        var search=CustomCardUi.AddTmpInput(top,"","搜索节点、用途或 BUFF",v=>{query=v;chapter=6;nodeIdentity=null;indexPage=0;Render();},400);CustomCardFormStyle.Input(search);
        var columns=TwoColumns(transform,"GuideColumns");columns.gameObject.AddComponent<LayoutElement>().flexibleHeight=1;
        columns.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight=true;columns.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth=false;
        var nav=CustomCardUi.CreateScroll(columns,"GuideNavigation");var size=nav.parent.parent.GetComponent<LayoutElement>();size.preferredWidth=150;size.flexibleWidth=0;
        for(int i=0;i<Chapters.Length;i++)
        {
            int page=i;navigation[i]=Button(nav,(i+1)+"  "+Chapters[i],()=>{chapter=page;nodeIdentity=null;Render();},150,44);
            navigation[i].GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
        }
        body=CustomCardUi.CreateScroll(columns,"GuideBody");body.GetComponent<VerticalLayoutGroup>().spacing=16;body.GetComponent<VerticalLayoutGroup>().padding=new(16,14,10,24);
        CustomCardUi.AddImage(body.parent.parent.gameObject,CustomCardVisuals.Node);Render();
    }
    private void Render()
    {
        CustomCardUiLifetime.Clear(body);
        foreach(var pair in navigation)CustomCardControls.Style(pair.Value,pair.Key==chapter?CardControlKind.Selected:CardControlKind.Quiet);
        if(chapter==6)
        {
            var node=nodeIdentity==null?null:CardNodeCatalog.Library().FirstOrDefault(n=>CardNodeCatalog.Identity(n)==nodeIdentity);
            if(node!=null)Node(node);else Index();
        }
        else Chapter();
        body.GetComponentInParent<ScrollRect>().verticalNormalizedPosition=1;
    }
    private void Heading(string title)=>CustomCardUi.AddTmpText(body,title,21,TextAnchor.MiddleLeft,CustomCardVisuals.Ink,36);
    private void Paragraph(string title,string text){Hint(body,title,CustomCardVisuals.Gold);Hint(body,text,CustomCardVisuals.Ink);}
    private void Chapter()
    {
        Heading(Chapters[chapter]);
        switch(chapter)
        {
            case 0:
                Paragraph("1 · 确定触发时机","在效果蓝图中从‘本牌使用时’、‘本牌抽到时’或‘本牌丢弃时’开始。每个时机只有一个入口；只在这些离散时点执行。");
                Paragraph("2 · 连接效果并配置参数","从节点库加入效果，用白色执行线连接。再填写目标、数量，以及状态节点必选的具体状态。数值和条件节点按需取值，不需要执行线。");
                Paragraph("3 · 完成卡牌","在卡牌属性设置名称、费用、类型，在卡面编辑图像。返回效果蓝图检查问题，确认预览后制作到仓库。保存用于保留可继续编辑的作品。");
                Paragraph("从示例开始","节点库的常用组合可以插入一组相连节点。它们是编辑起点，仍需填写具体状态等必选参数。");break;
            case 1:
                Paragraph("识别接口","白色箭头：执行；蓝色方框：数值；紫色菱形：条件；金色圆环：角色；绿色成组标记：角色集合。空心表示未连接，实心表示已连接。");
                Paragraph("执行线与数据线","执行出口只能接一个后继；数据输出可以复用。连接同类型接口，输入接线后用连线值覆盖原来的填写值。群体效果接角色集合，单体效果接单个角色。");
                Paragraph("作用域与分支","随机选择结果只在‘已选中’分支可用；当前对象只在对应遍历体内可用。分支末端返回控制节点，再执行‘完成后’。不要跨入口或跨分支借用临时结果。");
                Paragraph("使用目标","‘使用时选中目标’仅存在于需要选中目标的卡牌使用流程。抽到、丢弃或无目标技能牌的流程应选择使用者、合法集合或其他对象来源。");break;
            case 2:
                Paragraph("普通数值与比例","普通数值同时包含整数和小数，例如层数 3、数量 2.5。比例是另一种含义，可以显示为百分比或小数：50% = 比例小数 0.5。");
                Paragraph("比较时统一含义","读取生命百分比后连接到比较节点，另一项填写 50%，即可比较是否低于一半生命。连接会为尚未填写的数值采用适合的格式；已经填写的数值不会被静默改写。");
                Paragraph("显式转换","‘用作比例’把普通 0.5 变为 50%；‘转为小数’把 50% 变为普通 0.5；‘百分数值’把 50% 变为普通 50。按你的计算目的选择。");
                Paragraph("精度和错误","小数计算需要容差时用近似相等。除数不能为 0；效果数量非负，实际执行向下取整；重复次数必须是 0～64 的整数。输入错误会保留原文本并标红。");break;
            case 3:
                Paragraph("先选择具体状态","状态 / BUFF 是游戏及已加载 MOD 提供的效果。选择器展示名称、图标、说明、来源和标识。同名状态用来源和标识区分。");
                Paragraph("施加已有状态","必选：具体状态；配置：目标和层数。向目标添加所选状态，叠加和持续规则由该状态本身决定。群体版本对目标集合中的角色生效。");
                Paragraph("移除已有状态","必选：具体状态；配置：目标。移除整个指定状态，不是扣除一层。群体版本移除集合中各角色的该状态。");
                Paragraph("读取指定状态层数","必选：具体状态；配置：角色。输出当前层数，未持有为 0。它只读取数值，不会施加或移除状态；可以把结果连接到比较节点。");
                Paragraph("状态缺失","没有选择，或旧稿引用的状态未加载时，需要重新选择或加载其来源。草稿仍可保存，实际执行流程中的缺失状态会阻止制作。");
                foreach(var n in CardNodeCatalog.Library().Where(CardNodeCatalog.RequiresState))NodeLink(n);break;
            case 4:
                Paragraph("选择和调整","拖标题移动节点；滚轮缩放；空格或中键平移；拖动空白框选。右击接口断开连线，点击连线后按 Delete 删除。");
                Paragraph("组合与参数","组合用于整理多个节点。折叠后可以在右侧直接修改内部节点的状态、目标、数值和结果名称；也可以点‘定位此节点’展开并跳转。展开不改变执行关系。");
                Paragraph("复制与回退","Ctrl+C / Ctrl+V 复制粘贴节点和内部连接；撤销和重做恢复编辑。备注只用于说明，不能改变节点的真实用途。");
                Paragraph("查指南时继续保留现场","打开指南不会重建画布。返回时保留选中节点、缩放、位置和尚未完成的输入。作品库和生成脚本也提供返回编辑入口。");break;
            case 5:
                Paragraph("先处理问题","检查面板将阻止制作的问题与一般提示分开。每条给出节点及字段，点击定位；缺少状态可以直接打开选择器修正。未接入执行流程的草稿节点会给出提示。");
                Paragraph("检查卡牌描述","描述来自实际接入的效果。检查预览中的对象、状态名称和数量；读取节点和流程控制节点通常只影响计算，不作为多余的执行说明。");
                Paragraph("保存与切换","保存保留整个设计稿，包括还没完成的节点。切换作品时若有修改，可以保存后切换、丢弃并切换或取消。");
                Paragraph("制作","没有阻止制作的问题后，可将当前版本制作到仓库。继续修改设计稿不会替换已经制作出的卡牌。");break;
        }
        var actions=Row(body,"GuideChapterActions");
        if(chapter>0)Quiet(Button(actions,"上一节",()=>{chapter--;Render();},90));
        Button(actions,chapter==5?"查看节点索引":"下一节",()=>{chapter++;nodeIdentity=null;Render();},140);
    }
    private void Index()
    {
        Heading("节点索引");var nodes=CardNodeCatalog.Library().Where(n=>CardNodeCatalog.Matches(n,query)).ToArray();
        Hint(body,nodes.Length+" 个节点 · 点击一行查看参数、接口和示例");
        TableRow(new[]{"节点","用途","必填 / 配置","输入","输出"},null,true);
        const int pageSize=16;int total=Math.Max(1,(nodes.Length+pageSize-1)/pageSize);indexPage=Mathf.Clamp(indexPage,0,total-1);
        foreach(var n in nodes.Skip(indexPage*pageSize).Take(pageSize))
        {
            var item=n;TableRow(new[]{CardNodeCatalog.Title(n),CardNodeCatalog.Description(n),CardNodeCatalog.ParameterSummary(n),CardNodeCatalog.PortSummary(n,false),CardNodeCatalog.PortSummary(n,true)},()=>{nodeIdentity=CardNodeCatalog.Identity(item);Render();});
        }
        if(nodes.Length==0)Hint(body,"没有匹配的节点。可搜索状态、BUFF、伤害、百分比或节点名称。");
        var page=Row(body,"GuideIndexPages");Button(page,"上一页",()=>{indexPage--;Render();},90).interactable=indexPage>0;
        Label(page,(indexPage+1)+" / "+total,80);Button(page,"下一页",()=>{indexPage++;Render();},90).interactable=indexPage+1<total;
    }
    private void TableRow(string[] cells,Action? open,bool heading=false)
    {
        var row=TwoColumns(body,heading?"GuideIndexHeader":"GuideIndexRow");var layout=row.GetComponent<HorizontalLayoutGroup>();layout.spacing=12;layout.padding=new(8,8,8,8);layout.childForceExpandHeight=true;
        var image=CustomCardUi.AddImage(row.gameObject,heading?CustomCardVisuals.Raised:CustomCardVisuals.Well);
        if(open!=null){var button=row.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>open());}
        for(int i=0;i<cells.Length;i++)
        {
            var text=CustomCardUi.AddTmpText(row,string.IsNullOrEmpty(cells[i])?"—":cells[i],13,TextAnchor.UpperLeft,heading||i==0?CustomCardVisuals.Gold:CustomCardVisuals.Ink,24);
            var size=text.GetComponent<LayoutElement>();size.minWidth=0;size.preferredWidth=0;size.flexibleWidth=i==1?1.6f:i==2?1.3f:1;size.minHeight=24;size.preferredHeight=-1;
            text.richText=false;
        }
    }
    private void Node(CardGraphNode node)
    {
        Quiet(Button(body,"返回节点索引",()=>{nodeIdentity=null;Render();},140));Heading(CardNodeCatalog.Title(node));
        Paragraph("用途",CardNodeCatalog.Description(node));Paragraph("必填项与配置",CardNodeCatalog.ParameterSummary(node));
        Paragraph("接口",string.Join("\n",CardNodeCatalog.Ports(node).Select(p=>(p.Output?"输出":"输入")+" · "+p.Name+" · "+CardBlueprintCompiler.TypeName(p.Type)+(p.Required?" · 必须连线":""))));
        Paragraph("示例",CardNodeCatalog.Example(node));Hint(body,"相关节点",CustomCardVisuals.Gold);
        foreach(var item in CardNodeCatalog.Related(node))NodeLink(item);
    }
    private void NodeLink(CardGraphNode node)
    {
        var button=Quiet(Button(body,CardNodeCatalog.Title(node),()=>{chapter=6;nodeIdentity=CardNodeCatalog.Identity(node);Render();},300));button.GetComponent<LayoutElement>().flexibleWidth=1;
    }
}
