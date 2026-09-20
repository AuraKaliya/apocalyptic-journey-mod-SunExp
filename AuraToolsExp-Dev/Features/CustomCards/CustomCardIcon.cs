using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

internal sealed class CustomCardIcon : MaskableGraphic
{
    internal CardIcon Icon;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var r=rectTransform.rect;
        void Line(float ax,float ay,float bx,float by)=>CustomCardGraphLines.Quad(mesh,new(r.xMin+ax*r.width,r.yMin+ay*r.height),new(r.xMin+bx*r.width,r.yMin+by*r.height),color,1.3f);
        void Box(float x,float y,float w,float h){Line(x,y,x+w,y);Line(x+w,y,x+w,y+h);Line(x+w,y+h,x,y+h);Line(x,y+h,x,y);}
        if(Icon==CardIcon.Close){Line(.2f,.2f,.8f,.8f);Line(.2f,.8f,.8f,.2f);}
        else if(Icon==CardIcon.More){for(int i=0;i<3;i++)Box(.15f+i*.3f,.46f,.05f,.05f);}
        else if(Icon==CardIcon.Undo||Icon==CardIcon.Redo)
        {
            float Flip(float x)=>Icon==CardIcon.Redo?1-x:x;
            void Stroke(float ax,float ay,float bx,float by)=>Line(Flip(ax),ay,Flip(bx),by);
            Stroke(.4f,.8f,.15f,.55f);Stroke(.15f,.55f,.4f,.3f);Stroke(.15f,.55f,.65f,.55f);Stroke(.65f,.55f,.82f,.4f);Stroke(.82f,.4f,.82f,.16f);
        }
        else if(Icon==CardIcon.Library){Line(.2f,.15f,.2f,.85f);Line(.42f,.15f,.42f,.85f);Line(.6f,.8f,.82f,.15f);}
        else if(Icon==CardIcon.Layers){for(int i=0;i<3;i++){float y=.75f-i*.23f;Line(.1f,y,.5f,y-.2f);Line(.5f,y-.2f,.9f,y);}Line(.1f,.75f,.5f,.95f);Line(.5f,.95f,.9f,.75f);}
        else if(Icon==CardIcon.Expand){Line(.12f,.6f,.12f,.9f);Line(.12f,.9f,.42f,.9f);Line(.58f,.1f,.88f,.1f);Line(.88f,.1f,.88f,.4f);Line(.15f,.85f,.4f,.6f);Line(.6f,.4f,.85f,.15f);}
        else if(Icon==CardIcon.Panel){Box(.1f,.1f,.8f,.8f);Line(.6f,.1f,.6f,.9f);}
        else if(Icon==CardIcon.Help){for(int i=0;i<20;i++){float a=i*Mathf.PI/10,b=(i+1)*Mathf.PI/10;Line(.5f+Mathf.Cos(a)*.42f,.5f+Mathf.Sin(a)*.42f,.5f+Mathf.Cos(b)*.42f,.5f+Mathf.Sin(b)*.42f);}Line(.5f,.4f,.5f,.7f);Box(.47f,.22f,.05f,.05f);}
        else{Line(.2f,.15f,.8f,.75f);Line(.2f,.15f,.4f,.2f);Line(.8f,.75f,.68f,.88f);Line(.68f,.88f,.15f,.3f);Line(.15f,.3f,.2f,.15f);}
    }
}

internal sealed class CustomCardTooltip : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
{
    internal string Text="";
    private GameObject? popup;
    public void OnPointerEnter(PointerEventData data)
    {
        Close();var owner=GetComponentInParent<CustomCardFocusOwner>();if(owner==null)return;
        popup=CustomCardUi.CreateRect("ControlTooltip",owner.transform,new(.5f,.5f),new(.5f,.5f),new(.5f,1),new(Mathf.Max(64,Text.Length*14+20),28));
        var rect=(RectTransform)popup.transform;var corners=new Vector3[4];((RectTransform)transform).GetWorldCorners(corners);var point=owner.transform.InverseTransformPoint((corners[0]+corners[3])*.5f);
        var bounds=((RectTransform)owner.transform).rect;rect.anchoredPosition=new(Mathf.Clamp(point.x,bounds.xMin+rect.rect.width/2,bounds.xMax-rect.rect.width/2),Mathf.Clamp(point.y-6,bounds.yMin+30,bounds.yMax-6));
        CustomCardUi.AddImage(popup,CustomCardVisuals.Raised).raycastTarget=false;
        CustomCardUi.AddTmpText(popup.transform,Text,12,TextAnchor.MiddleCenter,CustomCardVisuals.Ink,28);popup.transform.SetAsLastSibling();
    }
    public void OnPointerExit(PointerEventData data)=>Close();
    private void OnDisable()=>Close();
    private void Close(){if(popup!=null){popup.SetActive(false);Destroy(popup);popup=null;}}
}
