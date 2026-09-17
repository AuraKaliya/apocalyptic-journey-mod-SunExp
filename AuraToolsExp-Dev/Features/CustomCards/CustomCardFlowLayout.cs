using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>Wraps parameter/action rows when the editor is narrow or a block is deeply nested.</summary>
internal sealed class CustomCardFlowLayout : LayoutGroup
{
    private const float Gap=6;
    public override void CalculateLayoutInputHorizontal(){base.CalculateLayoutInputHorizontal();SetLayoutInputForAxis(96,400,1,0);}
    public override void CalculateLayoutInputVertical(){SetLayoutInputForAxis(Arrange(false,0),Arrange(false,0),-1,1);}
    public override void SetLayoutHorizontal()=>Arrange(true,0);
    public override void SetLayoutVertical()=>Arrange(true,1);
    private float Arrange(bool apply,int axis)
    {
        float width=Mathf.Max(96,rectTransform.rect.width-padding.horizontal),x=0,y=padding.top,rowHeight=0;
        foreach(var child in rectChildren)
        {
            float w=Mathf.Min(width,Mathf.Max(32,LayoutUtility.GetPreferredWidth(child)));
            float h=Mathf.Max(40,LayoutUtility.GetPreferredHeight(child));
            if(x>0&&x+w>width+0.1f){x=0;y+=rowHeight+Gap;rowHeight=0;}
            if(apply)SetChildAlongAxis(child,axis,axis==0?padding.left+x:y,axis==0?w:h);
            x+=w+Gap;rowHeight=Mathf.Max(rowHeight,h);
        }
        return y+rowHeight+padding.bottom;
    }
}
