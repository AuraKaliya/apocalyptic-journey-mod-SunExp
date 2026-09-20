using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>One contract catalog for the palette, ports, inspector, validation and user guide.</summary>
public static partial class CardNodeCatalog
{
    public const string StateParameter="ResourceId";
    public static bool RequiresState(CardGraphNode n)=>(n.Kind==CardNodeKind.Effect&&CustomCardNames.NeedsBuff(n.Effect))||(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Read&&n.Field==CardDataField.BuffStacks);
    public static bool IsPicker(CardNodeKind kind)=>kind==CardNodeKind.PickEnemy||kind==CardNodeKind.PickFriend||kind==CardNodeKind.PickFromSet;
    public static bool IsBranch(CardNodeKind kind)=>kind==CardNodeKind.If||IsPicker(kind);
    public static bool PlayerField(CardDataField field)=>field>=CardDataField.Energy;
    public static bool FixedOwner(CardGraphNode n)=>(n.Kind==CardNodeKind.Effect&&CustomCardNames.PlayerEffect(n.Effect))||(n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Read&&PlayerField(n.Field));
    public static string Identity(CardGraphNode n)=>n.Kind switch
    {
        CardNodeKind.Entry=>"entry."+n.Trigger,CardNodeKind.Effect=>"effect."+n.Effect+(n.ManyTargets?".group":""),
        CardNodeKind.Value=>"value."+n.ValueKind+(n.ValueKind==CardValueKind.Read?"."+n.Field:""),CardNodeKind.Object=>"object."+n.Subject,_=>"node."+n.Kind
    };
    public static string Title(CardGraphNode n)=>n.Kind switch
    {
        CardNodeKind.Entry=>"本牌"+CustomCardNames.Name(n.Trigger,CustomCardNames.Triggers),
        CardNodeKind.Effect=>(n.ManyTargets?"群体 · ":"")+CustomCardNames.Name(n.Effect,CustomCardNames.Effects),
        CardNodeKind.If=>"如果 / 否则",CardNodeKind.Repeat=>"重复若干次",CardNodeKind.ForEach=>"逐个对象执行",
        CardNodeKind.CaptureNumber=>"记住数值",CardNodeKind.CaptureObject=>"记住对象",CardNodeKind.PickEnemy=>"随机选择一个敌人",
        CardNodeKind.PickFriend=>"随机选择一个友方",CardNodeKind.PickFromSet=>"从集合随机选择一个角色",
        CardNodeKind.Value=>n.ValueKind==CardValueKind.Read?CustomCardNames.Name(n.Field,CustomCardNames.Fields):CustomCardNames.Name(n.ValueKind,CustomCardNames.Values),
        CardNodeKind.Object=>CustomCardNames.Name(n.Subject,CustomCardNames.Objects),CardNodeKind.Stop=>"结束本次流程",
        CardNodeKind.Note=>"注释",_=>"旧稿待修订"
    };
    public static string Category(CardGraphNode n)=>n.Kind switch
    {
        CardNodeKind.Entry=>"触发入口",CardNodeKind.Object or CardNodeKind.PickEnemy or CardNodeKind.PickFriend or CardNodeKind.PickFromSet=>"对象与选择",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Read=>"读取数据",CardNodeKind.Value=>"数值与条件",
        CardNodeKind.Effect=>"卡牌效果",CardNodeKind.CaptureNumber or CardNodeKind.CaptureObject=>"本次变量",CardNodeKind.Note=>"画布注释",_=>"流程控制"
    };
    public static string Description(CardGraphNode n)=>n.Kind switch
    {
        CardNodeKind.Entry when n.Trigger==CardRuleTrigger.Use=>"本牌使用时开始一次流程；目标由本次使用提供。",
        CardNodeKind.Entry when n.Trigger==CardRuleTrigger.Draw=>"本牌进入原生抽到时点时执行；没有使用目标。",
        CardNodeKind.Entry=>"本牌原生丢弃时执行；用牌后的弃牌也可能触发。",
        CardNodeKind.PickEnemy=>"从原生合法敌方集合选择 1 个；每次执行抽样一次。",
        CardNodeKind.PickFriend=>"从友方集合选择 1 个，包含使用者；每次执行抽样一次。",
        CardNodeKind.PickFromSet=>"从输入集合中的有效角色随机选择 1 个。",
        CardNodeKind.CaptureObject=>"保存对象引用供后续使用；不会冻结其生命等属性。",
        CardNodeKind.CaptureNumber=>"保存执行到此处时的数值；后续读取不重新计算。",
        CardNodeKind.ForEach=>"进入时确定集合，逐个执行；当前对象只在执行体有效。",
        CardNodeKind.Repeat=>"按整数次数执行子流程；零次直接完成，上限 64 次。",
        CardNodeKind.If=>"根据条件执行一个分支，然后进入完成出口。",
        CardNodeKind.Stop=>"立即结束本次入口，包括尚未完成的重复。",
        CardNodeKind.Note=>"画布说明，不参与连线、执行或语义预算。",
        CardNodeKind.Object when n.Subject==CardObjectKind.Target=>"本次使用选中的角色；仅限有目标卡牌的使用流程。",
        CardNodeKind.Object when n.Subject==CardObjectKind.Self=>"本牌的使用者；固定输出单个角色。",
        CardNodeKind.Object=>"需要该数据时读取有效角色集合；不持续监控。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Read&&n.Field==CardDataField.CardCost=>"读取本牌基础费用，不代表本次实际支付费用。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Read&&PlayerField(n.Field)=>"读取使用者的"+CustomCardNames.Name(n.Field,CustomCardNames.Fields)+"。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Read&&n.Field==CardDataField.HealthPercent=>"读取指定角色的生命比例，以百分比显示；例如 50% 与比例小数 0.5 等价。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Read&&n.Field==CardDataField.BuffStacks=>"读取已有状态层数；未持有为 0，资源缺失是错误。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Read=>"需要该数值时读取指定角色的"+CustomCardNames.Name(n.Field,CustomCardNames.Fields)+"。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Divide=>"被除数除以除数；除数为零会停止流程并定位此节点。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.And=>"两个条件都成立；第一个不成立时不读取第二个。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Or=>"任一条件成立；第一个成立时不读取第二个。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Clamp=>"把输入值限制在下限与上限之间。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.AsRatio=>"将普通数值明确用作比例：0.5 变为 50%，实际数值保持。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.AsNumber=>"将比例转为普通小数：50% 变为 0.5，用于明确的数量计算。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.PercentPoints=>"取百分比的百分数值：50% 变为普通数值 50。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Approximately=>"判断两数之差是否在指定容差内；容差与输入单位一致，且不能为负数。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Exists=>"判断角色是否仍有效，可用于保护后续属性读取。",
        CardNodeKind.Value when n.ValueKind==CardValueKind.Number=>"固定数值或比例；支持小数，百分比 50% 与比例小数 0.5 等价。",
        CardNodeKind.Value=>"按所示运算输出"+(CustomCardNames.Boolean(n.ValueKind)?"真假结果。":"数值结果。"),
        CardNodeKind.Effect when n.Effect==CardEffectKind.EndTurn=>"结束使用者回合，并终止本次流程。",
        CardNodeKind.Effect when n.Effect==CardEffectKind.RemoveBuff=>"移除目标的整个指定状态。",
        CardNodeKind.Effect when n.Effect==CardEffectKind.AddBuff=>"向目标施加游戏已有状态，不创建新的 BUFF。",
        CardNodeKind.Effect when IsSelection(n.Effect)=>"使用者选择卡牌；完成后继续，取消会结束本次流程。",
        CardNodeKind.Effect when n.Effect==CardEffectKind.Shuffle=>"重洗使用者牌堆，沿用原生洗牌规则。",
        CardNodeKind.Effect when n.Effect==CardEffectKind.Draw=>"使用者抽取指定张数，入手完成后继续。",
        CardNodeKind.Effect=>"对"+(FixedOwner(n)?"使用者":n.ManyTargets?"指定集合":"指定角色")+"执行"+CustomCardNames.Name(n.Effect,CustomCardNames.Effects)+"；数量非负并向下取整。",
        _=>"保留旧稿内容，修订后才能制作。"
    };
    public static bool IsSelection(CardEffectKind e)=>e==CardEffectKind.Discard||e==CardEffectKind.Burn||e==CardEffectKind.SelectFromDeck||e==CardEffectKind.SelectFromDiscard;
    public static bool Executable(CardNodeKind kind)=>kind!=CardNodeKind.Value&&kind!=CardNodeKind.Object&&kind!=CardNodeKind.Note&&kind!=CardNodeKind.Unsupported;
    public static bool EditableObjectDefault(CardGraphNode n)=>Ports(n).Any(p=>!p.Output&&p.Id=="object"&&!p.Required);
    public static IReadOnlyList<CardObjectKind> ObjectDefaults(CardGraphNode n)
    {
        var p=Ports(n).FirstOrDefault(p=>!p.Output&&p.Id=="object");
        return p==null||p.Required?Array.Empty<CardObjectKind>():p.Type==CardPortType.Actors?new[]{CardObjectKind.Enemies,CardObjectKind.Friends,CardObjectKind.All}:new[]{CardObjectKind.Self,CardObjectKind.Target,CardObjectKind.Current};
    }
    public static string NumberInputName(CardGraphNode n,string port)=>port=="value"?n.Kind==CardNodeKind.Repeat?"次数":n.Kind==CardNodeKind.Effect?AmountName(n.Effect):"数值":n.ValueKind switch
    {
        CardValueKind.Divide=>port=="a"?"被除数":"除数",CardValueKind.Subtract=>port=="a"?"被减数":"减数",
        CardValueKind.Clamp=>port=="a"?"值":port=="b"?"下限":"上限",CardValueKind.Floor or CardValueKind.Ceiling=>"数值",
        CardValueKind.Approximately when port=="c"=>"容差",
        CardValueKind.AsRatio or CardValueKind.AsNumber or CardValueKind.PercentPoints=>"输入数值",
        _=>port=="a"?"数值 A":"数值 B"
    };
    public static string AmountName(CardEffectKind e)=>e switch{CardEffectKind.Damage or CardEffectKind.TrueDamage=>"伤害值",CardEffectKind.AddBuff=>"层数",CardEffectKind.Draw or CardEffectKind.Discard or CardEffectKind.Burn or CardEffectKind.SelectFromDeck or CardEffectKind.SelectFromDiscard=>"张数",_=>"数量"};
    public static string Unit(CardGraphNode n,string port)=>n.Kind==CardNodeKind.Repeat?"次":n.Kind==CardNodeKind.Effect?n.Effect==CardEffectKind.AddBuff?"层":n.Effect==CardEffectKind.Draw||IsSelection(n.Effect)?"张":"点":"";
    public static IReadOnlyList<CardNodePort> Ports(CardGraphNode n)
    {
        var p=new List<CardNodePort>();
        void In(string id,string name,CardPortType type,bool required=false)=>p.Add(new(id,name,type,false,required));
        void Out(string id,string name,CardPortType type)=>p.Add(new(id,name,type,true));
        if(Executable(n.Kind))
        {
            if(n.Kind!=CardNodeKind.Entry)In("in","执行",CardPortType.Execution);
            if(!IsPicker(n.Kind)&&n.Kind!=CardNodeKind.Stop&&!(n.Kind==CardNodeKind.Effect&&n.Effect==CardEffectKind.EndTurn))Out("next",n.Kind==CardNodeKind.Entry?"开始":n.Kind==CardNodeKind.If||n.Kind==CardNodeKind.Repeat||n.Kind==CardNodeKind.ForEach?"完成后":"完成",CardPortType.Execution);
        }
        switch(n.Kind)
        {
            case CardNodeKind.Effect:
                if(!FixedOwner(n))In("object",n.ManyTargets?"目标集合":"目标",n.ManyTargets?CardPortType.Actors:CardPortType.Actor);
                if(CustomCardNames.HasAmount(n.Effect))In("value",AmountName(n.Effect),CardPortType.Number);break;
            case CardNodeKind.If:In("condition","条件",CardPortType.Boolean,true);Out("then","成立",CardPortType.Execution);Out("else","不成立",CardPortType.Execution);break;
            case CardNodeKind.Repeat:In("value","次数",CardPortType.Number);Out("body","执行体",CardPortType.Execution);break;
            case CardNodeKind.ForEach:In("object","对象集合",CardPortType.Actors);Out("body","执行体",CardPortType.Execution);Out("current","当前对象",CardPortType.Actor);break;
            case CardNodeKind.CaptureNumber:In("value","捕获数值",CardPortType.Number);Out("result","捕获值",CardPortType.Number);break;
            case CardNodeKind.CaptureObject:In("object","捕获对象",CardPortType.Actor,true);Out("result","记住的对象",CardPortType.Actor);break;
            case CardNodeKind.PickEnemy:case CardNodeKind.PickFriend:case CardNodeKind.PickFromSet:
                if(n.Kind==CardNodeKind.PickFromSet)In("object","候选集合",CardPortType.Actors,true);
                Out("then","已选中",CardPortType.Execution);Out("else","无可选目标",CardPortType.Execution);Out("result",n.Kind==CardNodeKind.PickEnemy?"选中的敌人":n.Kind==CardNodeKind.PickFriend?"选中的友方":"选中的角色",CardPortType.Actor);break;
            case CardNodeKind.Object:Out("result",CustomCardNames.Single(n.Subject)?"角色":"角色集合",CustomCardNames.Single(n.Subject)?CardPortType.Actor:CardPortType.Actors);break;
            case CardNodeKind.Value:
                Out("result",CustomCardNames.Boolean(n.ValueKind)?n.ValueKind==CardValueKind.Not?"取反结果":"判断结果":"数值",CustomCardNames.Boolean(n.ValueKind)?CardPortType.Boolean:CardPortType.Number);
                if((n.ValueKind==CardValueKind.Read&&!FixedOwner(n))||n.ValueKind==CardValueKind.Exists)In("object","角色",CardPortType.Actor);
                bool boolean=n.ValueKind==CardValueKind.And||n.ValueKind==CardValueKind.Or||n.ValueKind==CardValueKind.Not;
                for(int i=0;i<CustomCardNames.Arity(n.ValueKind);i++){var id=new[]{"a","b","c"}[i];In(id,boolean?(n.ValueKind==CardValueKind.Not?"原条件":i==0?"条件 1":"条件 2"):NumberInputName(n,id),boolean?CardPortType.Boolean:CardPortType.Number,boolean);}break;
        }
        return p;
    }
    public static IReadOnlyList<CardGraphNode> Library()
    {
        var nodes=new List<CardGraphNode>();
        foreach(var t in new[]{CardRuleTrigger.Use,CardRuleTrigger.Draw,CardRuleTrigger.Discard})nodes.Add(new(){Kind=CardNodeKind.Entry,Trigger=t});
        foreach(CardEffectKind e in Enum.GetValues(typeof(CardEffectKind)))nodes.Add(new(){Kind=CardNodeKind.Effect,Effect=e,Number=6,Subject=e==CardEffectKind.Damage||e==CardEffectKind.TrueDamage||e==CardEffectKind.AddBuff||e==CardEffectKind.RemoveBuff?CardObjectKind.Target:CardObjectKind.Self});
        foreach(CardEffectKind e in Enum.GetValues(typeof(CardEffectKind)))if(!CustomCardNames.PlayerEffect(e))nodes.Add(new(){Kind=CardNodeKind.Effect,Effect=e,Number=6,ManyTargets=true,Subject=e==CardEffectKind.Damage||e==CardEffectKind.TrueDamage||e==CardEffectKind.AddBuff||e==CardEffectKind.RemoveBuff?CardObjectKind.Enemies:CardObjectKind.Friends});
        foreach(CardValueKind v in Enum.GetValues(typeof(CardValueKind)))if(v!=CardValueKind.Variable&&v!=CardValueKind.Read)nodes.Add(new(){Kind=CardNodeKind.Value,ValueKind=v});
        foreach(CardDataField f in Enum.GetValues(typeof(CardDataField)))nodes.Add(new(){Kind=CardNodeKind.Value,ValueKind=CardValueKind.Read,Field=f});
        foreach(var o in new[]{CardObjectKind.Self,CardObjectKind.Target,CardObjectKind.Enemies,CardObjectKind.Friends,CardObjectKind.All})nodes.Add(new(){Kind=CardNodeKind.Object,Subject=o});
        foreach(var k in new[]{CardNodeKind.If,CardNodeKind.Repeat,CardNodeKind.ForEach,CardNodeKind.CaptureNumber,CardNodeKind.CaptureObject,CardNodeKind.Stop,CardNodeKind.Note,CardNodeKind.PickEnemy,CardNodeKind.PickFriend,CardNodeKind.PickFromSet})
            nodes.Add(new(){Kind=k,Subject=k==CardNodeKind.ForEach||k==CardNodeKind.PickEnemy||k==CardNodeKind.PickFromSet?CardObjectKind.Enemies:k==CardNodeKind.PickFriend?CardObjectKind.Friends:CardObjectKind.Self,Number=k==CardNodeKind.Repeat?2:6});
        return nodes;
    }
    public static string GuideMarkdown()
    {
        var text=new StringBuilder("节点目录由生产定义生成。每个节点的系统名称不可被备注覆盖。\n\n");
        foreach(var n in Library())
        {
            text.AppendLine("**"+Title(n)+"**（"+Category(n)+"）\n\n"+Description(n)+"\n");
            text.AppendLine("类型 `"+Identity(n)+"`；节点版本 "+n.Version+"。"+(n.Kind==CardNodeKind.Entry?"本牌这一时点的唯一入口。":n.Kind==CardNodeKind.Note?"画布元素，不属于执行流程。":n.Kind==CardNodeKind.Object&&n.Subject==CardObjectKind.Target?"仅限需要选中目标的卡牌的使用入口。":"可用于三类入口；读取使用目标或临时变量时还需满足其作用域。")+"\n");
            text.AppendLine("| 方向 | 接口 | 类型 | 规则 |\n| --- | --- | --- | --- |");
            foreach(var p in Ports(n))
            {
                string rule=p.Type==CardPortType.Execution?p.Output?"子流程或完成出口":"单一执行来源":p.Output?IsPicker(n.Kind)?"只在已选中子流程可用":n.Kind==CardNodeKind.ForEach?"仅限对应遍历体":n.Kind==CardNodeKind.CaptureNumber||n.Kind==CardNodeKind.CaptureObject?"捕获后的同一执行作用域":"同一入口内按需读取":p.Required?"必须连接":p.Type==CardPortType.Number?"默认 "+(p.Id=="b"?n.Second:p.Id=="c"?n.Third:n.Number).ToString(System.Globalization.CultureInfo.InvariantCulture)+Unit(n,p.Id)+"；接线覆盖默认值":"默认 "+CustomCardNames.Name(n.Subject,CustomCardNames.Objects)+"；接线覆盖默认值";
                text.AppendLine("| "+(p.Output?"输出":"输入")+" | "+p.Name+" | "+CardBlueprintCompiler.TypeName(p.Type)+" | "+rule+" |");
            }
            text.AppendLine("参数："+ParameterSummary(n)+"\n\n示例："+Example(n)+"\n");
            if(n.Kind==CardNodeKind.Note)text.AppendLine("画布元素，没有接口；不计入运行节点数。");
            text.AppendLine();
        }
        return text.ToString();
    }
}
