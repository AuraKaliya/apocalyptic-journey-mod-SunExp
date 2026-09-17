using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraToolsExp.Dll.Features.CustomCards;

// Only host services/toolkit primitives are adapted; the complete editor and compiler are production sources.
namespace AuraToolsExp.Dll.Infrastructure
{
    public static class AuraToolsLog { public static void Warn(string s)=>Debug.LogWarning(s); }
    public enum OptionalFileDialogStatus { Selected,Cancelled,Unavailable,Error }
    public class OptionalFileDialogResult { public bool Selected;public string Path="",Message="";public OptionalFileDialogStatus Status; }
    public class OptionalFileDialogFilter { public OptionalFileDialogFilter(string a,string b){} }
    public static class OptionalFileDialog
    {
        public static void PickFileAsync(string a,OptionalFileDialogFilter[] b,string c,string d,Action<OptionalFileDialogResult> done)=>done(new OptionalFileDialogResult{Status=OptionalFileDialogStatus.Cancelled});
        public static void PickImageFileAsync(string d,Action<OptionalFileDialogResult> done)=>done(new OptionalFileDialogResult{Status=OptionalFileDialogStatus.Cancelled});
    }
    public static class AuraToolsResourceCache { public static T Load<T>(string p,bool b) where T:UnityEngine.Object => null; }
}
public static class ResourceLoader { public static T Load<T>(string path) where T:UnityEngine.Object=>null; }
namespace Witch.UI
{
    public class UIManager { public static UIManager Instance;public void ShowModalWindow(string a,string b,object c,float d){} }
}
namespace AuraUi.Shared
{
    public class AuraUiStableId:MonoBehaviour {public string Value;public static void Assign(GameObject o,string id){var c=o.GetComponent<AuraUiStableId>()??o.AddComponent<AuraUiStableId>();c.Value=id;}}
}
namespace AuraToolsExp.Dll.Features.CustomCards
{
    internal static class CustomCardLibrary
    {
        private static readonly List<CustomCardDocument> Items=new();
        public static IReadOnlyList<CustomCardDocument> List()=>Items.Select(i=>i.Copy()).ToArray();
        public static void Save(CustomCardDocument d){Items.RemoveAll(i=>i.Id==d.Id);d.Revision++;Items.Add(d.Copy());}
        public static void Delete(CustomCardDocument d)=>Items.RemoveAll(i=>i.Id==d.Id);
        public static string Export(CustomCardDocument d)=>"preview.auracard.json";
        public static CustomCardDocument Import(string p)=>new();
    }
    internal static class CustomCardNative
    {
        public static IReadOnlyList<KeyValuePair<string,string>> Buffs()=>new[]{new KeyValuePair<string,string>("poison","中毒"),new KeyValuePair<string,string>("strength","力量")};
        public static void CheckLua(CustomCardCompilation c){}
        public static object Craft(CustomCardDocument d)=>new object();
    }
    internal static class CustomCardArtworkRuntime
    {
        public static Texture2D Texture(CustomCardArtwork art){var t=new Texture2D(art.Size,art.Size,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};Refresh(t,Convert.FromBase64String(art.Pixels));return t;}
        public static void Refresh(Texture2D t,byte[] pixels)
        {
            var colors=new Color32[pixels.Length];for(int i=0;i<pixels.Length;i++){var p=AuraToolsExp.Dll.Features.PixelEmoji.PixelEmojiCodec.PaletteRgba[pixels[i]];colors[i]=new Color32((byte)(p>>24),(byte)(p>>16),(byte)(p>>8),(byte)p);}t.SetPixels32(colors);t.Apply();
        }
    }
}
namespace AuraToolsExp.Dll.Features.Settings
{
    internal static class AuraToolsUi
    {
        public static readonly Color Text=new(.93f,.91f,.88f),MutedText=new(.65f,.67f,.77f),SuccessText=new(.5f,.85f,.6f),ErrorText=new(1f,.45f,.4f),Row=new(.1f,.1f,.2f),Panel=new(.065f,.065f,.14f);
        private static TMP_FontAsset font;
        private static TMP_FontAsset Font { get { if(font==null)font=TMP_FontAsset.CreateFontAsset(Resources.Load<UnityEngine.Font>("PreviewFont"));return font;} }
        public static GameObject CreateRect(string n,Transform p,Vector2 amin,Vector2 amax,Vector2 pivot,Vector2 size)
        {var o=new GameObject(n,typeof(RectTransform));o.transform.SetParent(p,false);var r=o.GetComponent<RectTransform>();r.anchorMin=amin;r.anchorMax=amax;r.pivot=pivot;r.sizeDelta=size;return o;}
        public static GameObject CreateLayout(string n,Transform p)=>CreateRect(n,p,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);
        public static LayoutElement SetFixedHeight(GameObject o,float h){var e=o.GetComponent<LayoutElement>()??o.AddComponent<LayoutElement>();e.minHeight=h;e.preferredHeight=h;e.flexibleHeight=0;return e;}
        public static LayoutElement SetFixedSize(GameObject o,float w,float h){var e=SetFixedHeight(o,h);e.minWidth=w;e.preferredWidth=w;e.flexibleWidth=0;return e;}
        public static Image AddImage(GameObject o,Color c){var i=o.AddComponent<Image>();i.color=c;return i;}
        public static TextMeshProUGUI AddTmpText(Transform p,string s,int size,TextAnchor alignment,Color color,float height=40,float flexibleWidth=0,float width=0,bool autoSize=false)
        {
            var o=CreateLayout("Text",p);var t=o.AddComponent<TextMeshProUGUI>();t.font=Font;t.text=s;t.fontSize=size;t.color=color;t.raycastTarget=false;t.alignment=alignment==TextAnchor.UpperLeft?TextAlignmentOptions.TopLeft:TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping=true;var e=SetFixedHeight(o,height);e.preferredWidth=width>0?width:180;e.flexibleWidth=flexibleWidth;return t;
        }
        public static TMP_InputField AddTmpInput(Transform p,string value,string placeholder,Action<string> changed,float width=190,float height=42)
        {
            var o=CreateLayout("Input",p);SetFixedSize(o,width,height);var background=AddImage(o,new(.14f,.15f,.25f));
            var text=AddTmpText(o.transform,value,16,TextAnchor.MiddleLeft,Text);UnityEngine.Object.Destroy(text.GetComponent<LayoutElement>());var tr=text.rectTransform;tr.offsetMin=new(9,3);tr.offsetMax=new(-9,-3);
            var input=o.AddComponent<TMP_InputField>();input.targetGraphic=background;input.textViewport=tr;input.textComponent=text;input.text=value;input.onValueChanged.AddListener(v=>changed(v));return input;
        }
        public static Button AddButton(Transform p,string label,Action click,float width=108,float height=40)
        {
            var o=CreateLayout("Button."+label,p);SetFixedSize(o,width,height);var image=AddImage(o,new(.19f,.18f,.32f));var b=o.AddComponent<Button>();b.targetGraphic=image;b.onClick.AddListener(()=>click());
            var t=AddTmpText(o.transform,label,15,TextAnchor.MiddleLeft,Text);UnityEngine.Object.Destroy(t.GetComponent<LayoutElement>());t.alignment=TextAlignmentOptions.Center;t.rectTransform.offsetMin=new(4,2);t.rectTransform.offsetMax=new(-4,-2);return b;
        }
        public static Toggle AddToggle(Transform p,bool v,Action<bool> change,float size=28)
        {var o=CreateLayout("Toggle",p);SetFixedSize(o,size,size);var i=AddImage(o,v?SuccessText:MutedText);var t=o.AddComponent<Toggle>();t.targetGraphic=i;t.graphic=i;t.isOn=v;t.onValueChanged.AddListener(x=>change(x));return t;}
        public static Button AddSelectButton(Transform p,IReadOnlyList<string> labels,int selected,Action<int> change,float width=220,float height=40)
        {
            return AddButton(p,labels[Math.Max(0,selected)]+" ▾",()=>
            {
                var w=CreateOverlay("Selector",p,"选择");var s=CreateScroll(w.transform,"Options");for(int i=0;i<labels.Count;i++){int n=i;AddButton(s,labels[i],()=>{change(n);UnityEngine.Object.Destroy(w.transform.parent.gameObject);},Math.Min(width,600));}
            },width,height);
        }
        public static Transform CreateScroll(Transform p,string n)
        {
            var o=CreateLayout(n,p);var element=o.AddComponent<LayoutElement>();element.flexibleHeight=1;element.flexibleWidth=1;var view=CreateRect("Viewport",o.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);AddImage(view,new Color(0,0,0,.01f));view.AddComponent<RectMask2D>();
            var c=CreateRect("Content",view.transform,new(0,1),new(1,1),new(0,1),Vector2.zero);var l=c.AddComponent<VerticalLayoutGroup>();l.spacing=8;l.childControlHeight=true;l.childControlWidth=true;l.childForceExpandHeight=false;var f=c.AddComponent<ContentSizeFitter>();f.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=o.AddComponent<ScrollRect>();scroll.viewport=view.GetComponent<RectTransform>();scroll.content=c.GetComponent<RectTransform>();scroll.horizontal=false;return c.transform;
        }
        public static GameObject CreateOverlay(string n,Transform p,string title,Action onClose=null,bool singleInstance=true,float maxWidth=1180,Func<bool> canClose=null)
        {
            var owner=p.GetComponentInParent<Canvas>().transform;var backdrop=CreateRect(n,owner,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);AddImage(backdrop,new Color(0,0,0,.8f));
            var window=CreateRect("Window",backdrop.transform,Vector2.zero,Vector2.one,new(.5f,.5f),Vector2.zero);var r=window.GetComponent<RectTransform>();r.offsetMin=new(24,20);r.offsetMax=new(-24,-20);AddImage(window,Panel);
            var l=window.AddComponent<VerticalLayoutGroup>();l.padding=new RectOffset(16,16,12,12);l.spacing=8;l.childControlHeight=true;l.childControlWidth=true;l.childForceExpandHeight=false;
            var head=CreateLayout("Header",window.transform);SetFixedHeight(head,50);var h=head.AddComponent<HorizontalLayoutGroup>();h.childForceExpandWidth=false;AddTmpText(head.transform,title,24,TextAnchor.MiddleLeft,Text,45,1);AddButton(head.transform,"关闭",()=>{if(canClose!=null&&!canClose())return;onClose?.Invoke();UnityEngine.Object.Destroy(backdrop);},70);
            return window;
        }
        public static void ShowConfirmation(Transform p,string n,string title,string text,string button,Action action){var w=CreateOverlay(n,p,title);AddTmpText(w.transform,text,16,TextAnchor.MiddleLeft,Text,80);AddButton(w.transform,button,()=>{action();UnityEngine.Object.Destroy(w.transform.parent.gameObject);},180);}
        public static void ClearChildren(Transform t){foreach(Transform c in t){c.gameObject.SetActive(false);UnityEngine.Object.Destroy(c.gameObject);}}
    }
}
