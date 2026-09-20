using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AuraToolsExp.Dll.Features.CustomCards;

[JsonConverter(typeof(StringEnumConverter))]
public enum CardNodeKind { Entry, Effect, If, Repeat, ForEach, CaptureNumber, CaptureObject, Value, Object, Stop, Note, Unsupported, PickEnemy, PickFriend, PickFromSet }
public enum CardPortType { Execution, Number, Boolean, Actor, Actors }

public sealed class CardGraphNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Version { get; set; } = 2;
    public CardNodeKind Kind { get; set; }
    public CardRuleTrigger Trigger { get; set; } = CardRuleTrigger.Use;
    public CardEffectKind Effect { get; set; }
    public bool ManyTargets { get; set; }
    public CardValueKind ValueKind { get; set; }
    public CardObjectKind Subject { get; set; } = CardObjectKind.Self;
    public CardDataField Field { get; set; }
    public string ResourceId { get; set; } = "";
    public string VariableId { get; set; } = "";
    public string Label { get; set; } = "";
    public string VariableName { get; set; } = "";
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)] public Dictionary<string,CardNumber> Numbers { get; set; } = new();
    // Convenience accessors share the single persisted operand store; legacy JSON is read by migration only.
    [JsonIgnore] public double Number { get=>PeekOperand("a").Value;set=>SetNumber("a",value); }
    [JsonIgnore] public double Second { get=>PeekOperand("b").Value;set=>SetNumber("b",value); }
    [JsonIgnore] public double Third { get=>PeekOperand("c").Value;set=>SetNumber("c",value); }
    public CardNumber PeekOperand(string port)=>Numbers.TryGetValue(port=="b"||port=="c"?port:"a",out var value)?value:new();
    public CardNumber Operand(string port)
    {
        port=port=="b"||port=="c"?port:"a";
        if(!Numbers.TryGetValue(port,out var value))Numbers[port]=value=new();return value;
    }
    private void SetNumber(string port,double value){var number=Operand(port);number.Value=value;number.IsSet=true;}
    public float X { get; set; }
    public float Y { get; set; }
    public string GroupId { get; set; } = "";
    public float NoteWidth { get; set; } = 320;
    public float NoteHeight { get; set; } = 160;
}

public sealed class CardGraphEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string Output { get; set; } = "next";
    public string To { get; set; } = "";
    public string Input { get; set; } = "in";
}

public sealed class CardGraphGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "组合片段";
    public bool Collapsed { get; set; }
}

public sealed class CardBlueprint
{
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<CardGraphNode> Nodes { get; set; } = new();
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<CardGraphEdge> Edges { get; set; } = new();
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<CardGraphGroup> Groups { get; set; } = new();
    public float PanX { get; set; } = 24;
    public float PanY { get; set; } = 24;
    public float Zoom { get; set; } = 1;
    public bool HasView { get; set; }
    public static CardBlueprint Basic()
    {
        var g=new CardBlueprint(); var entry=g.Add(CardNodeKind.Entry,60,40);
        var effect=g.Add(CardNodeKind.Effect,60,188); effect.Subject=CardObjectKind.Target;
        g.Link(entry,"next",effect,"in");return g;
    }
    public CardGraphNode Add(CardNodeKind kind,float x=0,float y=0)
    {
        var n=new CardGraphNode { Kind=kind,X=x,Y=y };
        if(kind==CardNodeKind.Effect)n.Number=6;
        if(kind==CardNodeKind.ForEach||kind==CardNodeKind.PickFromSet||kind==CardNodeKind.PickEnemy)n.Subject=CardObjectKind.Enemies;
        if(kind==CardNodeKind.PickFriend)n.Subject=CardObjectKind.Friends;
        if(kind==CardNodeKind.Repeat)n.Number=2;
        Nodes.Add(n);return n;
    }
    public void Link(CardGraphNode from,string output,CardGraphNode to,string input) => Edges.Add(new(){From=from.Id,Output=output,To=to.Id,Input=input});
    public CardBlueprint Copy()=>JsonConvert.DeserializeObject<CardBlueprint>(JsonConvert.SerializeObject(this))!;
    public void Delete(IEnumerable<string> ids)
    {
        var remove=new HashSet<string>(ids,StringComparer.Ordinal);
        var captures=new HashSet<string>(Nodes.Where(n=>remove.Contains(n.Id)&&(n.Kind==CardNodeKind.CaptureNumber||n.Kind==CardNodeKind.CaptureObject||CardNodeCatalog.IsPicker(n.Kind))).Select(n=>n.Id));
        Nodes.RemoveAll(n=>remove.Contains(n.Id));Edges.RemoveAll(e=>remove.Contains(e.To)||(remove.Contains(e.From)&&!(captures.Contains(e.From)&&e.Output=="result")));
        Groups.RemoveAll(g=>!Nodes.Any(n=>n.GroupId==g.Id));
    }
    public IReadOnlyList<string> Paste(CardBlueprint fragment,float offsetX=40,float offsetY=40)
    {
        var copy=fragment.Copy();var ids=copy.Nodes.ToDictionary(n=>n.Id,_=>Guid.NewGuid().ToString("N"));
        var groups=copy.Groups.ToDictionary(g=>g.Id,_=>Guid.NewGuid().ToString("N"));
        foreach(var n in copy.Nodes){n.Id=ids[n.Id];n.X+=offsetX;n.Y+=offsetY;n.GroupId=groups.TryGetValue(n.GroupId,out var group)?group:"";if(ids.TryGetValue(n.VariableId,out var captured))n.VariableId=captured;else if(n.VariableId.Length>0)n.VariableId=Guid.NewGuid().ToString("N");}
        foreach(var e in copy.Edges){e.Id=Guid.NewGuid().ToString("N");e.From=ids[e.From];e.To=ids[e.To];}
        foreach(var g in copy.Groups)g.Id=groups[g.Id];
        Nodes.AddRange(copy.Nodes);Edges.AddRange(copy.Edges);Groups.AddRange(copy.Groups);return copy.Nodes.Select(n=>n.Id).ToArray();
    }
    public CardBlueprint Fragment(IEnumerable<string> selection)
    {
        var ids=new HashSet<string>(selection,StringComparer.Ordinal);var copy=Copy();
        copy.Nodes.RemoveAll(n=>!ids.Contains(n.Id));copy.Edges.RemoveAll(e=>!ids.Contains(e.From)||!ids.Contains(e.To));
        copy.Groups.RemoveAll(g=>!copy.Nodes.Any(n=>n.GroupId==g.Id));return copy;
    }
}

public sealed class CardNodePort
{
    public string Id { get; }
    public string Name { get; }
    public CardPortType Type { get; }
    public bool Output { get; }
    public bool Required { get; }
    public CardNodePort(string id,string name,CardPortType type,bool output,bool required=false){Id=id;Name=name;Type=type;Output=output;Required=required;}
}
