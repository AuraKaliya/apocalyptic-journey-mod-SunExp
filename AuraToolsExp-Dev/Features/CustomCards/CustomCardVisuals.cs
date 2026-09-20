using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>Owned palette for both the shipped editor and its Unity acceptance scenes.</summary>
internal static class CustomCardVisuals
{
    internal static readonly Color Canvas=Hex(0x100e20),Well=Hex(0x110f20),Node=Hex(0x191629),Raised=Hex(0x30253d),Ink=Hex(0xece7dc),Muted=Hex(0xaaa1b9),Border=Hex(0x393247),Selected=Hex(0xd4b475),Gold=Hex(0xd4b475),Error=Hex(0xef9a97),Visited=Hex(0x9acaad),Note=Hex(0x302938);
    private static Color Hex(uint n)=>new((n>>16)/255f,((n>>8)&255)/255f,(n&255)/255f,1);
    internal static Color Category(CardNodeKind k)=>k switch
    {
        CardNodeKind.Note=>Note,_=>Raised
    };
    internal static Color Port(CardPortType t)=>t switch{CardPortType.Execution=>Hex(0xd9e6f3),CardPortType.Actor=>Hex(0xeab775),CardPortType.Actors=>Hex(0x8dd5b2),CardPortType.Boolean=>Hex(0xd6a0df),_=>Hex(0x83c3f5)};
    internal static void Button(Button b,bool primary=false)
    {
        CustomCardControls.Style(b,primary?CardControlKind.Selected:CardControlKind.Secondary);
    }
}

/// <summary>Unity destroyed objects are not managed nulls; focus must be detached before UI teardown.</summary>
internal static class CustomCardUiLifetime
{
    internal static bool TextFocused()
    {
        var system=EventSystem.current;if(system==null)return false;
        var selected=system.currentSelectedGameObject;if(selected==null)return false;
        var input=selected.GetComponentInParent<TMP_InputField>();return input!=null&&input.isFocused;
    }
    internal static void ReleaseFocus(Transform owner,GameObject? fallback=null)
    {
        var system=EventSystem.current;if(system==null||system.alreadySelecting)return;
        var selected=system.currentSelectedGameObject;
        if(selected==null){if(!ReferenceEquals(selected,null))system.SetSelectedGameObject(null);return;}
        if(owner!=null&&(selected.transform==owner||selected.transform.IsChildOf(owner)))system.SetSelectedGameObject(fallback!=null&&fallback.activeInHierarchy?fallback:null);
    }
    internal static void Destroy(GameObject value,GameObject? fallback=null)
    {
        if(value==null)return;ReleaseFocus(value.transform,fallback);if(value==null)return;
        value.SetActive(false);UnityEngine.Object.Destroy(value);
    }
    internal static void Clear(Transform parent,GameObject? fallback=null)
    {
        if(parent==null)return;ReleaseFocus(parent,fallback);
        // Deselect can commit an edit and replace children. Take the snapshot after that callback.
        var children=new List<GameObject>();foreach(Transform child in parent)children.Add(child.gameObject);
        foreach(var child in children)Destroy(child,fallback);
    }
}
internal sealed class CustomCardFocusOwner : MonoBehaviour
{
    private void OnDisable()=>CustomCardUiLifetime.ReleaseFocus(transform);
}

internal sealed class CustomCardNodeChrome : MaskableGraphic
{
    internal Color Fill=CustomCardVisuals.Node,Header=CustomCardVisuals.Raised;
    internal float HeaderHeight=42;
    internal bool Selected,Invalid,Hovered;
    internal TMP_Text? StatusLabel;
    internal string IdleState="";
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;
        Rounded(mesh,r,9,Selected?CustomCardVisuals.Selected:Invalid?CustomCardVisuals.Error:Hovered?CustomCardVisuals.Muted:CustomCardVisuals.Border);
        var inset=Selected?2.5f:1.2f;var inner=new Rect(r.xMin+inset,r.yMin+inset,r.width-2*inset,r.height-2*inset);
        Rounded(mesh,inner,7,Fill);
        var title=new Rect(inner.xMin,inner.yMax-HeaderHeight,inner.width,HeaderHeight);Rounded(mesh,title,7,Header);
        CustomCardGraphLines.Quad(mesh,new(inner.xMin,inner.yMax-HeaderHeight),new(inner.xMax,inner.yMax-HeaderHeight),CustomCardVisuals.Border,1);
    }
    private static void Rounded(VertexHelper mesh,Rect r,float radius,Color c)
    {
        int start=mesh.currentVertCount;mesh.AddVert(r.center,c,Vector2.zero);int count=0;
        for(int corner=0;corner<4;corner++)
        {
            var center=new Vector2(corner==0||corner==3?r.xMax-radius:r.xMin+radius,corner<2?r.yMax-radius:r.yMin+radius);
            for(int i=0;i<=5;i++){float angle=(corner*90+i*18)*Mathf.Deg2Rad;mesh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,c,Vector2.zero);count++;}
        }
        for(int i=0;i<count;i++)mesh.AddTriangle(start,start+1+i,start+1+(i+1)%count);
    }
}
internal sealed class CustomCardNodeLod : MonoBehaviour
{
    internal TMP_Text Title=null!,Overview=null!;
    internal string? FullTitle,CompactTitle;
    internal readonly List<GameObject> Details=new();
    private bool compact;
    internal void Apply(float zoom)
    {
        bool next=zoom<.8f;
        if(FullTitle!=null)Title.text=next?CompactTitle:FullTitle;
        if(next!=compact){foreach(var detail in Details)if(detail!=null)detail.SetActive(!next);compact=next;}
        Overview.gameObject.SetActive(next&&zoom>=.55f&&!string.IsNullOrWhiteSpace(Overview.text));
        Title.fontSize=next?Mathf.Min(30,12/zoom):16;
        Title.enableAutoSizing=true;Title.fontSizeMin=14;Title.fontSizeMax=Title.fontSize;
        Overview.fontSize=20;Overview.enableAutoSizing=true;Overview.fontSizeMin=14;Overview.fontSizeMax=20;
    }
}

/// <summary>Geometry conveys type even when hue cannot be distinguished. Empty sockets remain hollow.</summary>
internal sealed class CustomCardPortGlyph : MaskableGraphic
{
    internal CardPortType Type;
    internal bool Connected,RequiredMissing,Compatible,Rejected;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var center=rectTransform.rect.center;var tint=Rejected?CustomCardVisuals.Error:Compatible?CustomCardVisuals.Selected:RequiredMissing?CustomCardVisuals.Error:CustomCardVisuals.Port(Type);
        Vector2[] points;
        if(Type==CardPortType.Execution)points=new[]{new Vector2(-5,-7),new Vector2(2,-7),new Vector2(8,0),new Vector2(2,7),new Vector2(-5,7)};
        else if(Type==CardPortType.Boolean)points=new[]{new Vector2(-7,0),new Vector2(0,-7),new Vector2(7,0),new Vector2(0,7)};
        else if(Type==CardPortType.Number)points=new[]{new Vector2(-6,-6),new Vector2(6,-6),new Vector2(6,6),new Vector2(-6,6)};
        else {points=new Vector2[16];for(int i=0;i<points.Length;i++)points[i]=new Vector2(Mathf.Cos(i*Mathf.PI/8),Mathf.Sin(i*Mathf.PI/8))*6;}
        for(int i=0;i<points.Length;i++)CustomCardGraphLines.Quad(mesh,center+points[i],center+points[(i+1)%points.Length],tint,Compatible?2.6f:1.8f);
        if(Connected)
        {
            int start=mesh.currentVertCount;mesh.AddVert(center,tint,Vector2.zero);
            foreach(var p in points)mesh.AddVert(center+p*.58f,tint,Vector2.zero);
            for(int i=0;i<points.Length;i++)mesh.AddTriangle(start,start+1+i,start+1+(i+1)%points.Length);
        }
        if(Type==CardPortType.Actors)CustomCardGraphLines.Quad(mesh,center+new Vector2(-4,-10),center+new Vector2(7,-10),tint,1.8f);
    }
}
