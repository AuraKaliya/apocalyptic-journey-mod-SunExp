using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using AuraToolsExp.Dll.Features.CustomCards;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

public static class NativeUiRenderProbe
{
    public static IEnumerator Run(Canvas canvas,Camera camera,string output)
    {
        Screen.SetResolution(960,720,false);yield return null;yield return null;
        var scaler=canvas.gameObject.GetComponent<CanvasScaler>()??canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,900);scaler.matchWidthOrHeight=1;
        yield return null;
        var parent=new GameObject("ScaledHost",typeof(RectTransform));parent.transform.SetParent(canvas.transform,false);
        var host=parent.GetComponent<RectTransform>();host.sizeDelta=new Vector2(920,780);host.localScale=Vector3.one*.82f;host.anchoredPosition=new Vector2(26,-12);
        var backdrop=parent.AddComponent<Image>();backdrop.color=Color.black;
        var areas=new List<RectTransform>();
        for(int i=0;i<2;i++)
        {
            var area=new GameObject("ClipArea"+i,typeof(RectTransform),typeof(Image),typeof(RectMask2D));area.transform.SetParent(host,false);
            var r=area.GetComponent<RectTransform>();r.sizeDelta=new Vector2(380,620);r.anchoredPosition=new Vector2(i==0?-210:210,0);area.GetComponent<Image>().color=new Color(.03f,.02f,.06f);
            if(i==1){var stencil=new GameObject("NativeStencilViewport",typeof(RectTransform),typeof(Image),typeof(Mask));stencil.transform.SetParent(r,false);var maskRect=(RectTransform)stencil.transform;maskRect.anchorMin=Vector2.zero;maskRect.anchorMax=Vector2.one;maskRect.sizeDelta=Vector2.zero;stencil.GetComponent<Image>().color=new Color(0,0,0,.01f);stencil.GetComponent<Mask>().showMaskGraphic=false;r=maskRect;}
            var shape=new GameObject("Grid",typeof(RectTransform));shape.transform.SetParent(r,false);var s=shape.GetComponent<RectTransform>();s.sizeDelta=new Vector2(320,320);s.anchoredPosition=new Vector2(0,-90);
            shape.AddComponent<CardPixelGrid>().Size=32;areas.Add(s);
            var border=new GameObject("Border",typeof(RectTransform));border.transform.SetParent(r,false);var b=border.GetComponent<RectTransform>();b.sizeDelta=new Vector2(280,80);b.anchoredPosition=new Vector2(0,210);
            border.AddComponent<CustomCardControlBorder>().color=Color.white;
            if(i==1)r.localScale=Vector3.one*.8f;
        }
        yield return null;yield return null;Canvas.ForceUpdateCanvases();
        var image=Capture(camera,960,720);File.WriteAllBytes(Path.Combine(output,"clip-probe.png"),image.EncodeToPNG());
        var counts=new List<int[]>();
        foreach(var grid in areas)
        {
            var values=new int[4];var corners=new Vector3[4];grid.GetWorldCorners(corners);
            var lo=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var hi=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
            for(int y=(int)lo.y+2;y<(int)hi.y-2;y++)for(int x=(int)lo.x+2;x<(int)hi.x-2;x++)
            {
                var c=image.GetPixel(x,y);if(c.r>.12f)values[(x<(lo.x+hi.x)/2?0:1)+(y<(lo.y+hi.y)/2?0:2)]++;
            }
            counts.Add(values);
        }
        bool passed=counts.All(values=>values.Min()>0&&values.Max()/(float)values.Min()<1.4f);
        File.WriteAllText(Path.Combine(output,"clip-probe.json"),JsonConvert.SerializeObject(new{unity=Application.unityVersion,quadrantPixels=counts,passed},Formatting.Indented));
        Object.Destroy(image);Object.Destroy(parent);yield return null;
        Object.Destroy(scaler);yield return null;
        if(!passed)throw new System.InvalidOperationException("Scaled nested-mask grid lost a quadrant.");
    }
    internal static Texture2D Capture(Camera camera,int width,int height)
    {
        var target=new RenderTexture(width,height,24);camera.targetTexture=target;camera.Render();var old=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
        RenderTexture.active=old;camera.targetTexture=null;Object.Destroy(target);return image;
    }
}
