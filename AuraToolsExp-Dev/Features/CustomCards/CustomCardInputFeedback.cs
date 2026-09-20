using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.CustomCards;

/// <summary>Editing feedback and one validated commit per edit; shared by every workshop input.</summary>
internal sealed class CustomCardInputFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
{
    private TMP_InputField input=null!;
    private Image surface=null!;
    private CustomCardControlBorder border=null!;
    private Func<string,string?>? validate;
    private Action<string>? commit;
    private string accepted="",error="";
    private bool initialized,hovered,committing;
    private static bool committingAll;
    private GameObject? popup;
    private int state=-1;
    private int suspended;
    internal bool HasError=>error.Length>0;
    internal static CustomCardInputFeedback Attach(TMP_InputField input)
    {
        var feedback=input.GetComponent<CustomCardInputFeedback>()??input.gameObject.AddComponent<CustomCardInputFeedback>();
        feedback.Configure(input);return feedback;
    }
    private void Configure(TMP_InputField field)
    {
        input=field;input.transition=Selectable.Transition.None;input.caretWidth=2;input.caretBlinkRate=1;
        input.customCaretColor=true;input.caretColor=CustomCardVisuals.Gold;input.selectionColor=new(.83f,.71f,.46f,.38f);
        input.restoreOriginalTextOnEscape=true;
        if(initialized)return;initialized=true;accepted=input.text;
        SetSurface(input.gameObject);
        input.onSelect.AddListener(_=>{state=-1;Apply();});
        input.onDeselect.AddListener(_=>{state=-1;Apply();});
        input.onValueChanged.AddListener(_=>{if(error.Length>0){error=validate?.Invoke(input.text)??"";ClosePopup();NotifyOwner();if(HasError)ShowPopup();}state=-1;Apply();});
        input.onEndEdit.AddListener(_=>{if(!TryCommit()&&isActiveAndEnabled)StartCoroutine(RefocusError());});
    }
    internal void SetSurface(GameObject owner)
    {
        surface=owner.GetComponent<Image>();CustomCardControls.Border(owner,CustomCardVisuals.Border);border=owner.transform.Find("ControlBorder").GetComponent<CustomCardControlBorder>();
        state=-1;Apply();
    }
    internal static void Bind(TMP_InputField input,Action<string> commit,Func<string,string?>? validate=null)
    {
        var feedback=Attach(input);feedback.accepted=input.text;feedback.commit=commit;feedback.validate=validate;
    }
    internal static void BindNumber(TMP_InputField input,CardNumber number,Action<double> changed,bool integer=false,double minimum=-1000000,double maximum=1000000)
    {
        input.onFocusSelectAll=true;
        Bind(input,text=>{CardBlueprintNumbers.TryParse(text,number.Format,out var value,out _);changed(value);},text=>
        {
            if(!CardBlueprintNumbers.TryParse(text,number.Format,out var value,out var problem))return problem;
            if(integer&&value!=Math.Truncate(value))return "此处需要整数。";
            if(value<minimum||value>maximum)return "数值需在 "+minimum+"～"+maximum+" 之间。";
            return null;
        });
    }
    internal bool TryCommit()
    {
        if(input==null||committing||suspended>0)return true;
        if(input.wasCanceled){input.SetTextWithoutNotify(accepted);error="";state=-1;Apply();NotifyOwner();return true;}
        string text=input.text;
        if(text==accepted){error="";state=-1;Apply();return true;}
        var problem=validate?.Invoke(text);
        if(!string.IsNullOrEmpty(problem)){error=problem!;state=-1;Apply();ShowPopup();NotifyOwner();return false;}
        accepted=text;error="";committing=true;
        try{commit?.Invoke(text);}
        finally{committing=false;if(this!=null){state=-1;Apply();NotifyOwner();}}
        return true;
    }
    internal static bool CommitAll(Transform root)
    {
        if(committingAll||root==null)return true;
        committingAll=true;
        try
        {
            foreach(var field in root.GetComponentsInChildren<CustomCardInputFeedback>().ToArray())
                if(field!=null&&!field.TryCommit())
                {
                    if(field.input!=null){field.input.Select();field.input.ActivateInputField();field.ShowPopup();}return false;
                }
            return true;
        }
        finally{committingAll=false;}
    }
    private void LateUpdate()=>Apply();
    private void NotifyOwner()=>GetComponentInParent<CustomCardWorkshopController>()?.InputStateChanged();
    private IEnumerator RefocusError()
    {
        yield return null;
        if(this!=null&&isActiveAndEnabled&&suspended==0&&HasError&&input!=null){input.Select();input.ActivateInputField();ShowPopup();}
    }
    private void Apply()
    {
        if(input==null||surface==null||border==null)return;
        int next=!input.interactable||input.readOnly?0:HasError?4:input.isFocused?3:hovered?2:1;
        if(next==state)return;state=next;
        surface.color=next==3?CustomCardVisuals.Raised:next==2?Color.Lerp(CustomCardVisuals.Well,CustomCardVisuals.Raised,.45f):CustomCardVisuals.Well;
        surface.CrossFadeColor(Color.white,0,true,true);
        border.color=next==4?CustomCardVisuals.Error:next==3?CustomCardVisuals.Gold:next==2?CustomCardVisuals.Muted:CustomCardVisuals.Border;
        border.Pixels=next==3||next==4?2:1.25f;border.SetVerticesDirty();
        if(!HasError||!input.isFocused&&!hovered)ClosePopup();
    }
    public void OnPointerEnter(PointerEventData e){hovered=true;state=-1;Apply();if(HasError)ShowPopup();}
    public void OnPointerExit(PointerEventData e){hovered=false;state=-1;Apply();}
    private void ShowPopup()
    {
        if(suspended>0||!HasError||popup!=null)return;
        var owner=GetComponentInParent<CustomCardFocusOwner>();var parent=owner!=null?owner.transform.Find("Window"):null;
        if(parent is not RectTransform bounds)return;
        popup=CustomCardUi.CreateRect("InputError",parent,new(.5f,.5f),new(.5f,.5f),new(0,1),new(290,48));popup.AddComponent<LayoutElement>().ignoreLayout=true;
        var corners=new Vector3[4];((RectTransform)transform).GetWorldCorners(corners);var point=bounds.InverseTransformPoint(corners[0]);
        ((RectTransform)popup.transform).anchoredPosition=new(Mathf.Clamp(point.x,bounds.rect.xMin+8,bounds.rect.xMax-298),Mathf.Clamp(point.y-4,bounds.rect.yMin+56,bounds.rect.yMax-8));
        CustomCardUi.AddImage(popup,CustomCardVisuals.Node).raycastTarget=false;CustomCardControls.Border(popup,CustomCardVisuals.Error);
        var text=CustomCardUi.AddTmpText(popup.transform,error,13,TextAnchor.MiddleLeft,CustomCardVisuals.Error,48);text.margin=new(10,4,10,4);popup.transform.SetAsLastSibling();
    }
    private void ClosePopup(){if(popup!=null){popup.SetActive(false);Destroy(popup);popup=null;}}
    private void OnDisable(){ClosePopup();hovered=false;state=-1;}

    // Read-only help may open with an invalid buffer. Keep that buffer and suppress the
    // delayed error refocus; never commit or rebuild the editor just to read documentation.
    internal static IDisposable SuspendEditing(Transform scope)=>new Suspension(scope);
    private sealed class Suspension:IDisposable
    {
        private readonly (CustomCardInputFeedback Field,string Text,bool ReadOnly)[] fields;
        private bool disposed;
        internal Suspension(Transform scope)
        {
            fields=scope.GetComponentsInChildren<CustomCardInputFeedback>().Select(f=>(f,f.input.text,f.input.readOnly)).ToArray();
            foreach(var entry in fields)entry.Field.suspended++;
            foreach(var entry in fields)
            {
                var f=entry.Field;f.ClosePopup();f.input.readOnly=true;f.input.DeactivateInputField();f.input.SetTextWithoutNotify(entry.Text);
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var entry in fields)
            {
                var f=entry.Field;if(f==null||f.input==null)continue;
                f.input.SetTextWithoutNotify(entry.Text);f.input.readOnly=entry.ReadOnly;f.suspended--;f.state=-1;f.Apply();
            }
        }
    }
}
