using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Settings;

/// <summary>Root-canvas bounds, sorting and ownership for all tool workspaces and dialogs.</summary>
internal sealed class AuraToolsWindowHost : MonoBehaviour
{
    private AuraToolsWindowHost? owner;
    private Canvas? surface;
    private Transform? root;
    private RectTransform window=null!;
    private bool fullWindow;
    private float width,height;
    private Vector2 previous;
    private float previousScale;

    internal static void Attach(GameObject window,Transform source,bool fullWindow,float width,float height=0)
    {
        var overlay=window.transform.parent.gameObject;
        var host=overlay.AddComponent<AuraToolsWindowHost>();
        host.window=(RectTransform)window.transform;host.fullWindow=fullWindow;host.width=width;host.height=height;
        var sourceCanvas=source.GetComponentInParent<Canvas>();
        if(sourceCanvas!=null)
        {
            var rootCanvas=sourceCanvas.rootCanvas;
            host.owner=source.GetComponentInParent<AuraToolsWindowHost>();host.root=rootCanvas.transform;
            var effective=sourceCanvas;
            while(!effective.isRootCanvas&&!effective.overrideSorting)
            {
                var parent=effective.transform.parent?.GetComponentInParent<Canvas>();if(parent==null)break;effective=parent;
            }
            int layerId=effective.sortingLayerID,order=effective.sortingOrder;
            foreach(var other in rootCanvas.GetComponentsInChildren<AuraToolsWindowHost>())
            {
                if(other==host||other.surface==null)continue;
                int currentValue=SortingLayer.GetLayerValueFromID(layerId),otherValue=SortingLayer.GetLayerValueFromID(other.surface.sortingLayerID);
                if(otherValue>currentValue){layerId=other.surface.sortingLayerID;order=other.surface.sortingOrder;}
                else if(otherValue==currentValue)order=Math.Max(order,other.surface.sortingOrder);
            }
            if(order>=short.MaxValue)throw new InvalidOperationException("当前界面层级已满，请关闭上层窗口后重试。");
            host.surface=overlay.AddComponent<Canvas>();host.surface.overrideSorting=true;
            host.surface.sortingLayerID=layerId;host.surface.sortingOrder=order+1;host.surface.worldCamera=rootCanvas.worldCamera;
            overlay.AddComponent<GraphicRaycaster>();
        }
        host.Resize();
    }

    private void LateUpdate()=>Resize();
    private void Resize()
    {
        if(window==null||transform is not RectTransform bounds)return;
        float canvasScale=surface!=null?Mathf.Max(.1f,surface.rootCanvas.scaleFactor):1;
        float scale=Mathf.Max(1,1/canvasScale),margin=12/(canvasScale*scale);
        var available=new Vector2(Mathf.Max(0,bounds.rect.width/scale-2*margin),Mathf.Max(0,bounds.rect.height/scale-2*margin));
        var size=fullWindow?available:new Vector2(Mathf.Min(width,available.x),Mathf.Min(height>0?height:available.y,available.y));
        if((size-previous).sqrMagnitude<.1f&&Mathf.Abs(scale-previousScale)<.001f)return;
        previous=size;previousScale=scale;
        window.localScale=Vector3.one*scale;window.anchorMin=window.anchorMax=window.pivot=new(.5f,.5f);
        window.sizeDelta=size;window.anchoredPosition=Vector2.zero;
    }

    private void OnDisable()
    {
        var events=EventSystem.current;var selected=events!=null?events.currentSelectedGameObject:null;
        if(events!=null&&!events.alreadySelecting&&selected!=null&&selected.transform.IsChildOf(transform))events.SetSelectedGameObject(null);
        // Sibling overlays retain their logical owner even though they share root-canvas bounds.
        if(root==null)return;
        foreach(var child in root.GetComponentsInChildren<AuraToolsWindowHost>(true))
            if(child!=this&&child.owner==this){child.gameObject.SetActive(false);Destroy(child.gameObject);}
    }
}
