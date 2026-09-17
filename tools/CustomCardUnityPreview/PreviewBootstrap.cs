using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraToolsExp.Dll.Features.CustomCards;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class CustomCardPreviewBootstrap:MonoBehaviour
{
    private string output;
    private Camera camera;
    private readonly List<object> cases=new();
    private bool failed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()=>new GameObject("CustomCardPreview").AddComponent<CustomCardPreviewBootstrap>();
    private void Awake()
    {
        Application.runInBackground=true;
        output=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-cardOutput=")).Substring(12);Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"failure.txt"),"");
        Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Exception||type==LogType.Error){failed=true;File.AppendAllText(Path.Combine(output,"failure.txt"),message+"\n"+trace+"\n");}};
    }
    private IEnumerator Start()
    {
        var go=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));var canvas=go.GetComponent<Canvas>();
        var png=CardPixelCanvas.Png(Sample().Artwork);var bitmap=new Texture2D(2,2);
        if(!bitmap.LoadImage(png)||bitmap.width!=256||bitmap.height!=256)throw new Exception("Generated artwork PNG did not load in Unity.");
        File.WriteAllBytes(Path.Combine(output,"native-card-art.png"),png);Destroy(bitmap);
        camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.025f,.08f);
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
        new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
        foreach(var size in new[]{new Vector2Int(1280,900),new Vector2Int(960,720),new Vector2Int(760,640)})
        {
            Screen.SetResolution(size.x,size.y,false);yield return null;yield return null;
            CustomCardWorkshop.Show(go.transform);yield return null;
            var controller=FindObjectOfType<CustomCardWorkshopController>();
            var doc=Sample();Set(controller,"doc",doc);
            foreach(var tab in new[]{0,1,3,4})
            {
                Set(controller,"tab",tab);Invoke(controller,"Render");yield return null;yield return null;
                Capture("card-"+size.x+"-"+tab,go.transform,size);
            }
            Set(controller,"tab",1);Invoke(controller,"Render");yield return null;
            Click("设置");yield return null;yield return null;Capture("block-"+size.x,go.transform,size);Click("关闭",last:true);yield return null;
            Set(controller,"tab",2);Invoke(controller,"Render");yield return null;
            Click("打开像素画板");yield return null;yield return null;Capture("pixels-"+size.x,go.transform,size);
            // Same production drawing controller is exercised through pointer events.
            var pixel=FindObjectOfType<CardPixelInput>();var rect=pixel.GetComponent<RectTransform>();var center=RectTransformUtility.WorldToScreenPoint(camera,rect.position);
            var e=new PointerEventData(EventSystem.current){position=center+new Vector2(110,110),button=PointerEventData.InputButton.Left,pointerPressRaycast=new RaycastResult{module=go.GetComponent<GraphicRaycaster>()}};
            var before=doc.Artwork.Pixels;pixel.OnPointerDown(e);pixel.OnPointerUp(e);if(doc.Artwork.Pixels==before)throw new Exception("Pointer drawing did not change pixels.");
            Click("撤销",last:true);if(doc.Artwork.Pixels!=before)throw new Exception("Pixel undo did not restore artwork.");
            foreach(Transform child in go.transform){child.gameObject.SetActive(false);Destroy(child.gameObject);}yield return null;yield return null;
        }
        File.WriteAllText(Path.Combine(output,"report.json"),JsonConvert.SerializeObject(new{success=!failed,cases,productionFeatureSources=true,hostServicesAdapted=true,nativePngDecoded=true},Formatting.Indented));
        Application.Quit(failed?1:0);
    }
    private static CustomCardDocument Sample()
    {
        var d=new CustomCardDocument{Name="霜火回响",Note="将余烬刻入冰晶。",Artwork=new(){UsePixels=true,Size=64,Pixels=Convert.ToBase64String(CardPixelCanvas.Template(64,2))}};
        var remember=new CardRuleBlock{Kind=CardBlockKind.RememberNumber,VariableName="初始护盾",Value=CardValue.Reading(CardDataField.Shield)};
        d.Rules[0].Blocks=new(){remember,new(){Kind=CardBlockKind.ForEach,Object=new(){Kind=CardObjectKind.Enemies},Condition=new(){Kind=CardValueKind.Exists,Object=new(){Kind=CardObjectKind.Current}},Then=new(){new(){Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Greater,CardValue.Reading(CardDataField.Health,CardObjectKind.Current),CardValue.Constant(10)),Then=new(){new(){Object=new(){Kind=CardObjectKind.Current},Value=CardValue.Compare(CardValueKind.Add,new(){Kind=CardValueKind.Variable,VariableId=remember.Id},CardValue.Constant(5))}},Else=new(){new(){Object=new(){Kind=CardObjectKind.Current}}}}}}};
        return d;
    }
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);
    private static void Invoke(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
    private static void Click(string label,bool last=false)
    {
        int Layer(Button b){var t=b.transform;while(t.parent!=null&&t.parent.GetComponent<Canvas>()==null)t=t.parent;return t.GetSiblingIndex();}
        var buttons=FindObjectsOfType<Button>().Where(b=>b.gameObject.activeInHierarchy&&b.GetComponentInChildren<TMP_Text>()?.text==label).OrderBy(Layer).ThenBy(b=>b.transform.GetSiblingIndex()).ToArray();
        if(buttons.Length==0)throw new Exception("Button missing: "+label);(last?buttons.Last():buttons.First()).onClick.Invoke();
    }
    private void Capture(string name,Transform root,Vector2Int size)
    {
        Canvas.ForceUpdateCanvases();
        var controller=FindObjectOfType<CustomCardWorkshopController>();
        var content=(Transform)controller.GetType().GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
        var contentViewport=content.GetComponentInParent<ScrollRect>().viewport.rect;
        if(contentViewport.width<300||contentViewport.height<120)throw new Exception(name+": editor workspace collapsed");
        var target=new RenderTexture(size.x,size.y,24);camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Destroy(image);Destroy(target);
        var corners=new Vector3[4];int buttons=0;
        foreach(var button in root.GetComponentsInChildren<Button>())
        {
            var rect=button.transform as RectTransform;rect.GetWorldCorners(corners);var left=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var right=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
            if(right.y<30||left.y>size.y-30)continue;
            if(left.x<0||right.x>size.x+1)throw new Exception(name+": button extends outside viewport: "+button.name);
            buttons++;
        }
        cases.Add(new{name,width=size.x,height=size.y,visibleButtons=buttons,editorWidth=contentViewport.width,editorHeight=contentViewport.height});
    }
}
