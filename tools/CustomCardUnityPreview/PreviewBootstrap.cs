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
    private readonly List<object> interactions=new();
    private readonly List<object> performance=new();
    private readonly List<object> lifecycle=new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()=>new GameObject("CustomCardPreview").AddComponent<CustomCardPreviewBootstrap>();
    private void Awake()
    {
        Application.runInBackground=true;
        output=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-cardOutput=")).Substring(12);Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"failure.txt"),"");
        Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Exception||type==LogType.Error){failed=true;File.AppendAllText(Path.Combine(output,"failure.txt"),message+"\n"+trace+"\n");if(type==LogType.Exception)Application.Quit(1);}};
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
        if(Environment.GetCommandLineArgs().Contains("-cardClipProbe")){yield return NativeUiRenderProbe.Run(canvas,camera,output);Application.Quit(0);yield break;}
        yield return ModalLayerContract(go,canvas);
        yield return DraftLifecycleContract(go);
        foreach(var size in new[]{new Vector2Int(1600,1000),new Vector2Int(1280,900),new Vector2Int(960,720),new Vector2Int(760,640)})
        {
            Screen.SetResolution(size.x,size.y,false);yield return null;yield return null;
            CustomCardWorkshop.Show(go.transform);yield return null;
            var controller=FindObjectOfType<CustomCardWorkshopController>();
            var doc=Sample();Set(controller,"doc",doc);
            CustomCardLibrary.Save(doc);Vector2 windowSize=default;
            foreach(var tab in new[]{0,1,2,6})
            {
                Set(controller,"tab",tab);Invoke(controller,"Render");yield return null;yield return null;
                Capture("card-"+size.x+"-"+tab,go.transform,size);
                ValidateDesign(controller,tab);
                var currentSize=((RectTransform)controller.transform).rect.size;
                if(windowSize!=default&&currentSize!=windowSize)throw new Exception("Switching workspace page changed window size.");windowSize=currentSize;
                if(controller.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("制作不消耗货币")||t.text.Contains("制作免费")))throw new Exception("Redundant currency copy returned.");
                if(tab==1)
                {
                    var costRow=controller.GetComponentsInChildren<Transform>().First(t=>t.name=="CostAndRarity");var costInput=costRow.GetComponentInChildren<TMP_InputField>();
                    EventSystem.current.SetSelectedGameObject(costInput.gameObject);costInput.ActivateInputField();yield return null;costInput.text="3";
                    Click("制作到仓库");yield return null;
                    var status=(TMP_Text)controller.GetType().GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
                    if(doc.Cost!=3||CustomCardNative.LastCraftedCost!=3||!status.text.Contains("制作到仓库"))throw new Exception("Crafting dropped a focused cost edit or its completion message.");
                    Capture("craft-cost-"+size.x,go.transform,size);
                }
            }
            Set(controller,"tab",0);Invoke(controller,"Render");yield return null;yield return null;
            yield return NodeZoomContract(controller,go,size);
            yield return NumericInputContract(controller,go,size);
            yield return StateGuideContract(controller,go,size);
            Invoke(controller,"Preview");yield return null;yield return null;Capture("preview-"+size.x,go.transform,size);Click("关闭",last:true);yield return null;
            Click("更多");yield return null;Capture("menu-"+size.x,go.transform,size);Click("节点指南",last:true);yield return null;
            Click("返回编辑",last:true);yield return null;
            Set(controller,"tab",0);Invoke(controller,"Render");yield return null;
            yield return GraphInteractions(controller,go,size);
            doc=(CustomCardDocument)controller.GetType().GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            var graph=FindObjectOfType<CustomCardGraphEditor>();graph.FocusNode(doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect).Id);
            graph.ShowInspector();yield return null;yield return null;Capture("block-"+size.x,go.transform,size);CloseInspector(graph);yield return null;
            doc.Artwork=new(){UsePixels=true,Size=64,Pixels=Convert.ToBase64String(CardPixelCanvas.Template(64,2))};Set(controller,"tab",2);Invoke(controller,"Render");yield return null;
            yield return null;yield return null;Capture("pixels-"+size.x,go.transform,size);
            if(controller.GetComponentsInChildren<CardPixelEditorController>().Length!=1)throw new Exception("Pixel editor is not embedded in the workshop.");
            // Same production drawing controller is exercised through pointer events.
            var pixel=FindObjectOfType<CardPixelInput>();var rect=pixel.GetComponent<RectTransform>();var center=RectTransformUtility.WorldToScreenPoint(camera,rect.position);
            var view=rect.parent as RectTransform;var fit=Mathf.Min(view.rect.width,view.rect.height)-24;
            if(Mathf.Abs(rect.rect.width-fit)>3||rect.rect.width<140)throw new Exception("Pixel canvas did not fit the largest available square: "+rect.rect+" in "+view.rect);
            var white=FindObjectsOfType<Button>().FirstOrDefault(b=>b.transform.parent.name=="PixelPalette"&&b.colors.normalColor==Color.white);if(white==null)throw new Exception("Palette colors were multiplied by the button theme.");
            var e=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(rect.rect.width*.42f,rect.rect.height*.42f,0))),button=PointerEventData.InputButton.Left,pointerPressRaycast=new RaycastResult{module=go.GetComponent<GraphicRaycaster>()}};
            var before=doc.Artwork.Pixels;pixel.OnPointerDown(e);pixel.OnPointerUp(e);if(doc.Artwork.Pixels==before)throw new Exception("Pointer drawing did not change pixels.");
            yield return null;Click("撤销");yield return null;yield return null;
            doc=(CustomCardDocument)controller.GetType().GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            if(doc.Artwork.Pixels!=before)throw new Exception("Workshop undo did not restore artwork.");
            foreach(Transform child in go.transform){child.gameObject.SetActive(false);Destroy(child.gameObject);}yield return null;yield return null;
            if(CustomCardPreviewHost.LiveViews!=0)throw new Exception("Closed workspace retained native preview content.");
        }
        yield return NativeUiRenderProbe.Run(canvas,camera,output);
        File.WriteAllText(Path.Combine(output,"report.json"),JsonConvert.SerializeObject(new{success=!failed,cases,interactions,lifecycle,performance,productionFeatureSources=true,hostServicesAdapted=true,nativePngDecoded=true,nativeDictionaryRendering=false,previewLifecycle=true,stableWorkspace=true,embeddedPainting=true,designGeometry=true,scaledClipping=true,modalLayering=true,fullWindow=true,nodeZoomBounds=true,inputEditing=true,numericFormats=true,trialRemoved=true,stateWorkflow=true,guideContextPreserved=true,draftLifecycle=true,unityVersion=Application.unityVersion},Formatting.Indented));
        Application.Quit(failed?1:0);
    }
    private IEnumerator GraphInteractions(CustomCardWorkshopController controller,GameObject canvas,Vector2Int size)
    {
        var doc=new CustomCardDocument{Name="蓝图交互验收"};Set(controller,"doc",doc);Invoke(controller,"Render");yield return null;yield return null;yield return null;
        var graph=FindObjectOfType<CustomCardGraphEditor>();graph.Fit();yield return null;
        var effect=doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);
        var header=graph.GetComponentsInChildren<CustomCardGraphNodeInput>().First(n=>n.NodeId==effect.Id);
        var original=new Vector2(effect.X,effect.Y);var before=CustomCardCompiler.Compile(doc).Scripts["UseScript"];
        PointerEventData Event(Transform t)
        {
            var r=(RectTransform)t;return new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,r.TransformPoint(r.rect.center)),button=PointerEventData.InputButton.Left,pointerPressRaycast=new RaycastResult{module=canvas.GetComponent<GraphicRaycaster>()}};
        }
        var drag=Event(header.transform);header.OnBeginDrag(drag);drag.position+=new Vector2(38,-18);drag.delta=new Vector2(38,-18);header.OnDrag(drag);header.OnEndDrag(drag);
        if(effect.X==original.x||CustomCardCompiler.Compile(doc).Scripts["UseScript"]!=before)throw new Exception("Node dragging did not preserve execution semantics.");
        Click("撤销");yield return null;yield return null;
        doc=(CustomCardDocument)controller.GetType().GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);graph=FindObjectOfType<CustomCardGraphEditor>();effect=doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);
        if(effect.X!=original.x||effect.Y!=original.y)throw new Exception("Graph undo did not restore node position.");
        var number=doc.Graph.Add(CardNodeKind.Value,effect.X-380,effect.Y+230);number.ValueKind=CardValueKind.Number;number.Number=9;graph.Refresh();graph.Fit();yield return null;yield return null;
        CustomCardGraphPortInput Port(string node,string id,bool outgoing)
        {
            graph.Blueprint.Zoom=Mathf.Max(1f,graph.Blueprint.Zoom);graph.ApplyView();
            var port=graph.GetComponentsInChildren<CustomCardGraphPortInput>().First(p=>p.Node.Id==node&&p.Port.Id==id&&p.Port.Output==outgoing);
            var point=graph.Viewport.InverseTransformPoint(port.transform.position);
            if(point.y<graph.Viewport.rect.yMin+24){graph.Blueprint.PanY+=point.y-graph.Viewport.rect.yMin-24;graph.ApplyView();}
            if(point.y>graph.Viewport.rect.yMax-24){graph.Blueprint.PanY+=point.y-graph.Viewport.rect.yMax+24;graph.ApplyView();}
            point=graph.Viewport.InverseTransformPoint(port.transform.position);
            if(point.x<graph.Viewport.rect.xMin+24){graph.Blueprint.PanX+=graph.Viewport.rect.xMin+24-point.x;graph.ApplyView();}
            if(point.x>graph.Viewport.rect.xMax-24){graph.Blueprint.PanX+=graph.Viewport.rect.xMax-24-point.x;graph.ApplyView();}
            Canvas.ForceUpdateCanvases();
            return port;
        }
        var source=Port(number.Id,"result",true);var target=Port(effect.Id,"value",false);
        yield return null;
        var wire=Event(source.transform);source.OnPointerDown(wire);source.OnBeginDrag(wire);wire.dragging=true;wire.position=Event(target.transform).position;source.OnDrag(wire);source.OnEndDrag(wire);
        if(!doc.Graph.Edges.Any(e=>e.From==number.Id&&e.To==effect.Id&&e.Input=="value")){Capture("wire-failure-"+size.x,canvas.transform,size);var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(wire,hits);throw new Exception("Pointer wire connection did not commit at "+size.x+" / "+wire.position+"; hits="+string.Join(",",hits.Select(h=>h.gameObject.name)));}
        yield return null;yield return null;
        var edges=JsonConvert.SerializeObject(doc.Graph.Edges);source=Port(number.Id,"result",true);target=Port(effect.Id,"in",false);
        wire=Event(source.transform);source.OnPointerDown(wire);wire.dragging=true;wire.position=Event(target.transform).position;source.OnDrag(wire);source.OnEndDrag(wire);graph.CancelInteraction();
        if(JsonConvert.SerializeObject(doc.Graph.Edges)!=edges)throw new Exception("Invalid rewiring destroyed existing edges.");
        var entryNode=doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Entry);var a=Port(entryNode.Id,"next",true);var b=Port(effect.Id,"in",false);
        var edgeClick=Event(a.transform);edgeClick.position=(Event(a.transform).position+Event(b.transform).position)*.5f;graph.BackgroundClick(edgeClick);
        EditClick("在线路中插入节点");yield return null;yield return null;Click("给予护盾",last:true);yield return null;yield return null;
        if(!doc.Graph.Nodes.Any(n=>n.Kind==CardNodeKind.Effect&&n.Effect==CardEffectKind.Shield)||!CustomCardCompiler.Compile(doc).Success)throw new Exception("Insertion through the contextual palette failed.");
        graph.Fit();yield return null;
        int beforePaste=doc.Graph.Nodes.Count;
        var clipboard=GUIUtility.systemCopyBuffer;
        graph.SelectNode(number.Id);graph.SelectNode(effect.Id,true);EditClick("复制选中节点 · Ctrl+C");EditClick("粘贴节点 · Ctrl+V");yield return null;yield return null;
        if(doc.Graph.Nodes.Count!=beforePaste+2||doc.Graph.Nodes.Select(n=>n.Id).Distinct().Count()!=doc.Graph.Nodes.Count)throw new Exception("Clipboard did not remap node identities.");
        GUIUtility.systemCopyBuffer=clipboard;graph.Delete();graph.SelectNode(effect.Id);graph.SelectNode(number.Id,true);EditClick("组合 / 折叠");EditClick("组合 / 折叠");yield return null;yield return null;
        if(!doc.Graph.Groups.Any(g=>g.Collapsed)||!CustomCardCompiler.Compile(doc).Success)throw new Exception("Collapsed group changed graph validity.");
        Capture("graph-group-"+size.x,canvas.transform,size);
        graph.ShowInspector();yield return null;
        if(doc.Graph.Groups.Any(g=>g.Collapsed))throw new Exception("Group did not expand.");
        var encoded=JsonConvert.SerializeObject(doc);Set(controller,"doc",CardBlueprintMigration.Read(encoded));Invoke(controller,"Render");yield return null;yield return null;
        doc=(CustomCardDocument)controller.GetType().GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
        if(CustomCardCompiler.Compile(doc).Scripts["UseScript"]==before)throw new Exception("Serialized connected value was lost.");
        interactions.Add(new{width=size.x,dragUndo=true,pointerConnection=true,invalidRewirePreserved=true,edgeInsertion=true,clipboardRemapped=true,groupRoundTrip=true,documentRoundTrip=true});
        if(size.x==1280)
        {
            foreach(int count in new[]{50,128,256})
            {
                var stress=new CustomCardDocument{Name="节点容量验收"};
                for(int i=2;i<count;i++){var n=stress.Graph.Add(CardNodeKind.Value,(i%12)*280,(i/12)*180);n.Number=i;}
                Set(controller,"doc",stress);var watch=System.Diagnostics.Stopwatch.StartNew();Invoke(controller,"Render");yield return null;yield return null;Canvas.ForceUpdateCanvases();watch.Stop();
                performance.Add(new{nodes=count,openMilliseconds=watch.Elapsed.TotalMilliseconds});
            }
            Set(controller,"doc",doc);Invoke(controller,"Render");yield return null;yield return null;
        }
        Capture("graph-edited-"+size.x,canvas.transform,size);
        yield return AnnotationLifecycle(controller,canvas,size);
    }
    private IEnumerator AnnotationLifecycle(CustomCardWorkshopController controller,GameObject canvas,Vector2Int size)
    {
        var graph=FindObjectOfType<CustomCardGraphEditor>();
        var doc=(CustomCardDocument)controller.GetType().GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
        var original=CustomCardCompiler.Compile(doc).Scripts.ToArray();
        Click("添加节点");yield return null;
        var button=FindObjectsOfType<Button>().Last(b=>b.GetComponentInChildren<TMP_Text>()?.text=="注释");
        EventSystem.current.SetSelectedGameObject(button.gameObject);button.onClick.Invoke();yield return null;yield return null;
        var note=doc.Graph.Nodes.Last(n=>n.Kind==CardNodeKind.Note);
        if(!CustomCardCompiler.Compile(doc).Scripts.SequenceEqual(original)||CardNodeCatalog.Ports(note).Count!=0)throw new Exception("Annotation changed program semantics.");
        if(EventSystem.current.currentSelectedGameObject==null&&!ReferenceEquals(EventSystem.current.currentSelectedGameObject,null))throw new Exception("Destroyed palette button retained focus.");
        graph.ShowInspector();yield return null;
        TMP_InputField PopupInput()
        {
            var popup=(GameObject)typeof(CustomCardGraphEditor).GetField("popup",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(graph);
            var owner=popup!=null?popup.transform:(Transform)typeof(CustomCardGraphEditor).GetField("inspector",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(graph);
            return owner.GetComponentsInChildren<TMP_InputField>().First(i=>i.lineType==TMP_InputField.LineType.MultiLineNewline);
        }
        var input=PopupInput();
        EventSystem.current.SetSelectedGameObject(input.gameObject);input.ActivateInputField();yield return null;
        input.text="此处只执行一次随机选择。\n多个数据线复用同一个结果。";EventSystem.current.SetSelectedGameObject(null);yield return null;yield return null;
        if(!note.Label.Contains("\n"))throw new Exception("Annotation multiline edit was lost.");
        input=PopupInput();
        if(FindObjectsOfType<TMP_InputField>().Any(i=>i.lineType==TMP_InputField.LineType.MultiLineNewline&&i.text!=note.Label))throw new Exception("Inspector and sidebar disagree after annotation edit.");
        EventSystem.current.SetSelectedGameObject(input.gameObject);input.ActivateInputField();yield return null;
        CloseInspector(graph);yield return null;yield return null;
        if(CustomCardUiLifetime.TextFocused())throw new Exception("Closed inspector kept a focused input.");
        if(!note.Label.Contains("\n"))throw new Exception("Closing inspector erased note text at "+size.x);
        doc.Graph.Zoom=.8f;graph.FocusNode(note.Id);yield return null;
        var grip=graph.GetComponentsInChildren<CustomCardNoteResize>().First(g=>g.Node.Id==note.Id);var oldWidth=note.NoteWidth;
        var resize=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,grip.transform.position),button=PointerEventData.InputButton.Left,pointerPressRaycast=new RaycastResult{module=canvas.GetComponent<GraphicRaycaster>()}};
        grip.OnBeginDrag(resize);resize.position+=new Vector2(45,-30);grip.OnDrag(resize);grip.OnEndDrag(resize);yield return null;
        if(note.NoteWidth<=oldWidth)throw new Exception("Annotation resize grip did not persist geometry.");
        if(!note.Label.Contains("\n"))throw new Exception("Resizing erased note text at "+size.x);
        var foreign=new GameObject("ExternalFocus",typeof(RectTransform));foreign.transform.SetParent(canvas.transform,false);EventSystem.current.SetSelectedGameObject(foreign);
        graph.Refresh();if(EventSystem.current.currentSelectedGameObject!=foreign)throw new Exception("Graph stole another window's focus.");
        Destroy(foreign);yield return null;
        // Reproduce the precise Unity fake-null failure boundary from the game log.
        typeof(EventSystem).GetField("m_CurrentSelected",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(EventSystem.current,foreign);
        if(CustomCardUiLifetime.TextFocused())throw new Exception("Destroyed managed wrapper was treated as a focused live control.");
        if(!note.Label.Contains("\n"))throw new Exception("Focus refresh erased note text at "+size.x);
        graph.SelectNode(note.Id);graph.Delete();yield return null;Click("撤销");yield return null;yield return null;
        doc=(CustomCardDocument)controller.GetType().GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);graph=FindObjectOfType<CustomCardGraphEditor>();
        if(!doc.Graph.Nodes.Any(n=>n.Kind==CardNodeKind.Note&&n.Label.Contains("\n")))throw new Exception("Undo lost annotation text at "+size.x+"; notes="+string.Join(";",doc.Graph.Nodes.Where(n=>n.Kind==CardNodeKind.Note).Select(n=>n.Label))+"; history="+((List<string>)controller.GetType().GetField("undo",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller)).Count);
        // The right-click canvas menu uses the same authoritative note factory.
        var e=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Right,position=RectTransformUtility.WorldToScreenPoint(camera,graph.Viewport.TransformPoint(new Vector3(100,-100,0))),pointerPressRaycast=new RaycastResult{module=canvas.GetComponent<GraphicRaycaster>()}};
        graph.BackgroundClick(e);yield return null;Click("注释",last:true);yield return null;
        if(doc.Graph.Nodes.Count(n=>n.Kind==CardNodeKind.Note)!=2)throw new Exception("Context-menu annotation insertion failed.");
        graph.Fit();yield return null;yield return null;Capture("annotations-"+size.x,canvas.transform,size);
        // Same production visual tokens and independent state channels, with actual geometry.
        if(CustomCardVisuals.Canvas==CustomCardVisuals.Node||graph.Viewport.GetComponent<Image>().color!=CustomCardVisuals.Canvas)throw new Exception("Production node and canvas fills are identical or unused.");
        var p=doc.Graph.Add(CardNodeKind.PickEnemy,400,420);graph.Refresh();graph.SelectNode(p.Id);
        var chrome=graph.GetComponentsInChildren<CustomCardNodeChrome>().First(n=>n.gameObject.name=="Node."+p.Id);
        if(!chrome.Selected||chrome.Fill!=CustomCardVisuals.Node||p.Kind!=CardNodeKind.PickEnemy||CardNodeCatalog.Ports(p).Any(p=>p.Id=="object"))throw new Exception("Picker appearance/contract mismatch.");
        doc.Graph.Zoom=size.y<700?.8f:1;graph.FocusNode(p.Id);yield return null;yield return null;Capture("picker-design-"+size.x,canvas.transform,size);
        var reopened=CardBlueprintMigration.Read(JsonConvert.SerializeObject(doc));Set(controller,"doc",reopened);Invoke(controller,"Render");yield return null;
        if(reopened.Graph.Nodes.Count(n=>n.Kind==CardNodeKind.Note)!=2)throw new Exception("Reopen lost annotations.");
        var stateDoc=new CustomCardDocument{Name="接口错误与选中状态"};var begin=stateDoc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Entry);var effect=stateDoc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);
        var required=stateDoc.Graph.Add(CardNodeKind.CaptureObject,420,60);effect.X=820;stateDoc.Graph.Edges.Clear();stateDoc.Graph.Link(begin,"next",required,"in");stateDoc.Graph.Link(required,"next",effect,"in");
        stateDoc.Graph.HasView=true;stateDoc.Graph.Zoom=size.y<700?.8f:1;Set(controller,"doc",stateDoc);Invoke(controller,"Render");yield return null;yield return null;
        graph=FindObjectOfType<CustomCardGraphEditor>();graph.FocusNode(required.Id);yield return null;
        chrome=graph.GetComponentsInChildren<CustomCardNodeChrome>().First(c=>c.gameObject.name=="Node."+required.Id);
        var missing=chrome.GetComponentsInChildren<CustomCardPortGlyph>().First(g=>g.GetComponent<CustomCardGraphPortInput>().Port.Id=="object");
        if(!chrome.Selected||!chrome.Invalid||!missing.RequiredMissing)throw new Exception("Selection concealed a required-input error.");
        Capture("node-error-"+size.x,canvas.transform,size);
        Click("更多");Click("节点指南",last:true);yield return null;yield return null;
        var guide=FindObjectOfType<CustomCardGuide>();guide.GetComponentInChildren<TMP_InputField>().text="从集合随机";yield return null;yield return null;
        if(!guide.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="从集合随机选择一个角色"))throw new Exception("In-app guide does not use production catalog.");
        Click("返回编辑",last:true);yield return null;
        Set(controller,"tab",0);Set(controller,"doc",reopened);Invoke(controller,"Render");yield return null;
        lifecycle.Add(new{width=size.x,paletteAnnotation=true,contextAnnotation=true,multiline=true,resize=true,focusRelease=true,destroyedUnityWrapper=true,foreignFocusPreserved=true,undoAndReopen=true,selectionWithError=true,productionPalette=true,catalogGuide=true});
    }
    private static CustomCardDocument Sample()
    {
        var d=new CustomCardDocument{Name="星火"};d.Artwork=new(){UsePixels=true,Size=64,Pixels=Convert.ToBase64String(CardPixelCanvas.Template(64,0))};
        return d;
    }
    private static void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);
    private static void ValidateDesign(CustomCardWorkshopController owner,int page)
    {
        Transform Find(string name)=>owner.GetComponentsInChildren<Transform>().First(t=>t.name==name);
        var width=((RectTransform)owner.transform).rect.width;
        var window=(RectTransform)owner.transform;var bounds=(RectTransform)window.parent;
        if(Mathf.Abs(window.rect.width*window.localScale.x-bounds.rect.width+24)>2||Mathf.Abs(window.rect.height*window.localScale.y-bounds.rect.height+24)>2)throw new Exception("Detailed editor did not fill its game window with a 12px safe margin.");
        if((page==1||page==2)&&width>=680&&!Find("CustomCardSidePreview").gameObject.activeInHierarchy)throw new Exception("Design sidebar disappeared at a supported width.");
        if(page==1)
        {
            var cost=Find("Cost");var rarity=Find("Rarity");
            if(Mathf.Abs(cost.position.y-rarity.position.y)>1)throw new Exception("Cost and rarity no longer share the designed row.");
            var segments=Find("Segments");var buttons=segments.GetComponentsInChildren<Button>();
            if(buttons.Length!=2||Mathf.Abs(((RectTransform)buttons[0].transform).rect.width-((RectTransform)buttons[1].transform).rect.width)>1)throw new Exception("Card type is not an equal-width segmented control.");
        }
        if(page==0)
        {
            var graph=owner.GetComponentInChildren<CustomCardGraphEditor>();var viewport=graph.Viewport;
            foreach(var node in graph.GetComponentsInChildren<CustomCardNodeChrome>())
            {
                var points=new Vector3[4];node.rectTransform.GetWorldCorners(points);
                foreach(var p in points){var point=viewport.InverseTransformPoint(p);if(point.x<viewport.rect.xMin-2||point.x>viewport.rect.xMax+2||point.y<viewport.rect.yMin-2||point.y>viewport.rect.yMax+2)throw new Exception("Initial blueprint fitting clips a node: "+node.name);}
            }
        }
    }
    private IEnumerator NodeZoomContract(CustomCardWorkshopController owner,GameObject root,Vector2Int size)
    {
        var graph=owner.GetComponentInChildren<CustomCardGraphEditor>();var zoom=graph.Blueprint.Zoom;var pan=new Vector2(graph.Blueprint.PanX,graph.Blueprint.PanY);
        foreach(float factor in new[]{.4f,.54f,.55f,.79f,.8f,1f,1.6f})
        {
            graph.Blueprint.Zoom=factor;graph.Blueprint.PanX=24-graph.Blueprint.Nodes.Min(n=>n.X)*factor;graph.Blueprint.PanY=24-graph.Blueprint.Nodes.Min(n=>n.Y)*factor;graph.ApplyView();yield return null;Canvas.ForceUpdateCanvases();
            foreach(var node in graph.GetComponentsInChildren<CustomCardNodeChrome>())
            foreach(var label in node.GetComponentsInChildren<TMP_Text>())
            {
                if(label.text.Contains("放大查看接口"))throw new Exception("Repeated node zoom instruction is still visible.");
                label.ForceMeshUpdate();
                foreach(var character in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c=>c.isVisible))
                foreach(var corner in new[]{character.bottomLeft,character.topRight})
                {
                    var point=node.rectTransform.InverseTransformPoint(label.rectTransform.TransformPoint(corner));
                    if(!node.rectTransform.rect.Contains(new Vector2(point.x,point.y)))throw new Exception("Zoom "+factor+" draws text outside "+node.name+": "+label.text);
                }
            }
            if(factor==.4f)Capture("graph-overview-"+size.x,root.transform,size);
        }
        graph.Blueprint.Zoom=zoom;graph.Blueprint.PanX=pan.x;graph.Blueprint.PanY=pan.y;graph.ApplyView();yield return null;
    }
    private IEnumerator NumericInputContract(CustomCardWorkshopController owner,GameObject root,Vector2Int size)
    {
        var branch=new CardRuleBlock{Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.HealthPercent),CardValue.Ratio(.5)),Then=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}};
        var doc=new CustomCardDocument{Name="百分比条件",Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){branch}}})};
        Set(owner,"doc",doc);Set(owner,"tab",0);Invoke(owner,"Render");yield return null;yield return null;
        var graph=owner.GetComponentInChildren<CustomCardGraphEditor>();var node=doc.Graph.Nodes.First(n=>n.ValueKind==CardValueKind.Less&&n.Kind==CardNodeKind.Value);
        doc.Graph.Zoom=1;graph.FocusNode(node.Id);yield return null;yield return null;
        TMP_InputField Field()=>graph.GetComponentsInChildren<CustomCardNodeChrome>().First(c=>c.name=="Node."+node.Id).GetComponentInChildren<TMP_InputField>();
        var input=Field();if(input.text!="50")throw new Exception("Percent input did not display 50 for ratio .5.");
        EventSystem.current.SetSelectedGameObject(input.gameObject);input.ActivateInputField();yield return null;yield return null;
        var border=input.transform.Find("ControlBorder").GetComponent<CustomCardControlBorder>();
        if(!input.isFocused||border.color!=CustomCardVisuals.Gold||border.Pixels<2||input.targetGraphic.color!=CustomCardVisuals.Raised||input.caretWidth<2||input.selectionColor.a<.25f)throw new Exception("Input has no visible editing state.");
        Capture("input-edit-"+size.x,root.transform,size);
        input.text="12.3456789012345";input.onEndEdit.Invoke(input.text);yield return null;yield return null;
        if(Math.Abs(node.Second-.123456789012345)<1e-16==false||node.Operand("b").Kind!=CardNumberKind.Ratio)throw new Exception("Percent editing lost precision or meaning.");
        input=Field();var before=JsonConvert.SerializeObject(doc);input.Select();input.ActivateInputField();yield return null;input.DeactivateInputField();yield return null;
        if(JsonConvert.SerializeObject(doc)!=before)throw new Exception("An unchanged input edit modified the document.");
        input=Field();input.Select();input.ActivateInputField();yield return null;input.text="abc";input.onEndEdit.Invoke(input.text);yield return null;
        CustomCardNative.LastCraftedCost=-999;Invoke(owner,"Craft");yield return null;
        if(CustomCardNative.LastCraftedCost!=-999||!input.GetComponent<CustomCardInputFeedback>().HasError||owner.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.制作到仓库").interactable)throw new Exception("Invalid numeric input was silently accepted.");
        Capture("input-error-"+size.x,root.transform,size);
        var graphBefore=graph;var selection=graph.Selection.ToArray();var pan=new Vector2(doc.Graph.PanX,doc.Graph.PanY);var zoom=doc.Graph.Zoom;
        Click("节点指南",last:true);yield return null;yield return null;
        var help=FindObjectOfType<CustomCardGuide>();var helpSearch=help.GetComponentInChildren<TMP_InputField>();helpSearch.Select();helpSearch.ActivateInputField();yield return null;yield return null;
        if(!helpSearch.isFocused||input.isFocused||input.text!="abc"||!input.readOnly)throw new Exception("Invalid input stole focus from help or lost its buffer.");
        helpSearch.text="百分比";yield return null;yield return null;Capture("guide-index-"+size.x,root.transform,size);
        Click("返回编辑",last:true);yield return null;yield return null;
        if(FindObjectOfType<CustomCardGuide>()!=null||graph!=graphBefore||input.text!="abc"||input.readOnly||!graph.Selection.SequenceEqual(selection)||doc.Graph.Zoom!=zoom||pan!=new Vector2(doc.Graph.PanX,doc.Graph.PanY))throw new Exception("Returning from guide changed editor context or invalid input.");
        input.text="25.5";input.onEndEdit.Invoke(input.text);yield return null;yield return null;
        if(Math.Abs(node.Second-.255)>1e-15)throw new Exception("Corrected percent input did not commit.");
        input=Field();input.Select();input.ActivateInputField();yield return null;input.text="99";input.ProcessEvent(Event.KeyboardEvent("escape"));input.DeactivateInputField();yield return null;yield return null;
        if(Math.Abs(node.Second-.255)>1e-15)throw new Exception("Escape committed an abandoned numeric edit.");
        var scripts=CustomCardCompiler.Compile(doc).Scripts.ToArray();
        Button FormatChoice(string label)=>graph.GetComponentsInChildren<CustomCardNodeChrome>().First(c=>c.name=="Node."+node.Id).GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<TMP_Text>()?.text==label);
        FormatChoice("%").onClick.Invoke();yield return null;Click("比例",last:true);yield return null;yield return null;
        if(node.Operand("b").Format!=CardNumberFormat.Number||Field().text!="0.255"||!CustomCardCompiler.Compile(doc).Scripts.SequenceEqual(scripts))throw new Exception("Changing percentage display changed the numeric value or its script.");
        Capture("input-ratio-format-"+size.x,root.transform,size);
        FormatChoice("比例").onClick.Invoke();yield return null;Click("%",last:true);yield return null;yield return null;
        if(Field().text!="25.5"||Math.Abs(node.Second-.255)>1e-15)throw new Exception("Percentage display did not round-trip.");
        if(owner.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="试算")||typeof(CustomCardWorkshop).Assembly.GetType("AuraToolsExp.Dll.Features.CustomCards.CustomCardTrial")!=null)throw new Exception("Removed trial feature is still shipped.");
        Set(owner,"tab",1);Invoke(owner,"Render");yield return null;yield return null;
        var cost=owner.GetComponentsInChildren<Transform>().First(t=>t.name=="CostStepper");var costInput=cost.GetComponentInChildren<TMP_InputField>();costInput.Select();costInput.ActivateInputField();yield return null;yield return null;
        if(cost.GetComponent<Image>().color!=CustomCardVisuals.Raised||cost.Find("ControlBorder").GetComponent<CustomCardControlBorder>().color!=CustomCardVisuals.Gold)throw new Exception("Compound input did not show editing state on its shared border.");
        Capture("input-cost-"+size.x,root.transform,size);costInput.DeactivateInputField();yield return null;
        Set(owner,"doc",Sample());Set(owner,"tab",0);Invoke(owner,"Render");yield return null;yield return null;
    }
    private IEnumerator StateGuideContract(CustomCardWorkshopController owner,GameObject root,Vector2Int size)
    {
        var doc=new CustomCardDocument{Name="状态与指南验收"};var apply=doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);apply.Effect=CardEffectKind.AddBuff;
        var remove=doc.Graph.Add(CardNodeKind.Effect,480,320);remove.Effect=CardEffectKind.RemoveBuff;remove.Subject=CardObjectKind.Target;doc.Graph.Link(apply,"next",remove,"in");
        var read=doc.Graph.Add(CardNodeKind.Value,60,350);read.ValueKind=CardValueKind.Read;read.Field=CardDataField.BuffStacks;doc.Graph.Link(read,"result",apply,"value");
        foreach(var effect in new[]{CardEffectKind.AddBuff,CardEffectKind.RemoveBuff}){var n=doc.Graph.Add(CardNodeKind.Effect,880,100+200*(int)effect);n.Effect=effect;n.ManyTargets=true;n.Subject=CardObjectKind.Enemies;}
        doc.Graph.HasView=true;doc.Graph.Zoom=1;Set(owner,"doc",doc);Set(owner,"tab",0);Invoke(owner,"Render");yield return null;yield return null;
        var graph=owner.GetComponentInChildren<CustomCardGraphEditor>();graph.FocusNode(apply.Id);yield return null;yield return null;
        if(graph.GetComponentsInChildren<CustomCardNodeChrome>().Count(n=>n.transform.Find("StateSelector")!=null)!=5)throw new Exception("State parameter is missing from a state node family.");
        graph.GetComponentsInChildren<Transform>().First(t=>t.name=="Node."+apply.Id).Find("StateSelector").GetComponent<Button>().onClick.Invoke();yield return null;yield return null;
        var picker=FindObjectOfType<CustomCardStatePicker>();var search=picker.GetComponentInChildren<TMP_InputField>();search.text="中毒";yield return null;yield return null;
        if(picker.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("StateResult."))!=2)throw new Exception("State picker conflates same-name resources.");
        picker.GetComponentsInChildren<Button>().Single(b=>b.name=="StateResult.mod_poison").onClick.Invoke();yield return null;yield return null;
        Capture("state-picker-"+size.x,root.transform,size);
        picker.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmState").onClick.Invoke();yield return null;yield return null;
        if(apply.ResourceId!="mod_poison"||FindObjectOfType<CustomCardStatePicker>()!=null)throw new Exception("State selection did not update and return to the same graph.");
        foreach(var node in doc.Graph.Nodes.Where(CardNodeCatalog.RequiresState))node.ResourceId="poison";
        remove.ResourceId="unloaded";graph.Refresh();Invoke(graph,"ShowDiagnostics");yield return null;yield return null;Capture("state-diagnostics-"+size.x,root.transform,size);
        Click("选择状态",last:true);yield return null;yield return null;picker=FindObjectOfType<CustomCardStatePicker>();
        search=picker.GetComponentInChildren<TMP_InputField>();search.text="poison";yield return null;
        picker.GetComponentsInChildren<Button>().Single(b=>b.name=="StateResult.poison").onClick.Invoke();yield return null;
        picker.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmState").onClick.Invoke();yield return null;yield return null;
        if(remove.ResourceId!="poison"||!CustomCardCompiler.Compile(doc,CustomCardNative.BuffName).Success)throw new Exception("Missing state repair did not complete compilation.");
        graph.FocusNode(read.Id);yield return null;Click("节点说明",last:true);yield return null;yield return null;
        var guide=FindObjectOfType<CustomCardGuide>();if(!guide.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("具体状态（必选）")))throw new Exception("Node help did not deep-link to required state configuration.");
        Capture("state-guide-"+size.x,root.transform,size);Click("返回编辑",last:true);yield return null;yield return null;
        var reader=graph.GetComponentsInChildren<Transform>().Single(t=>t.name=="Node."+read.Id);
        reader.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<TMP_Text>()?.text=="指定状态层数").onClick.Invoke();yield return null;
        FindObjectOfType<CustomCardPopover>().GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.当前生命").onClick.Invoke();yield return null;yield return null;
        reader=graph.GetComponentsInChildren<Transform>().Single(t=>t.name=="Node."+read.Id);
        var inspector=(Transform)typeof(CustomCardGraphEditor).GetField("inspector",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(graph);
        if(read.Field!=CardDataField.Health||reader.Find("StateSelector")!=null||inspector.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("具体状态")||t.text.Contains("来源：")))throw new Exception("Reader retains an irrelevant state field after switching attribute: "+read.Field+" / "+string.Join("|",inspector.GetComponentsInChildren<TMP_Text>().Select(t=>t.text)));
        read.Field=CardDataField.BuffStacks;
        var group=new CardGraphGroup{Name="状态组合",Collapsed=true};doc.Graph.Groups.Add(group);apply.GroupId=remove.GroupId=read.GroupId=group.Id;graph.Refresh();graph.SelectNode(apply.Id);yield return null;yield return null;
        if(inspector.GetComponentsInChildren<Button>().Count(b=>b.name=="StateSelector")!=3||!inspector.GetComponentsInChildren<Button>().Any(b=>b.name=="CardAction.定位此节点"))throw new Exception("Collapsed group hides state or child navigation.");
        Capture("state-group-"+size.x,root.transform,size);
        inspector.GetComponentsInChildren<Button>().First(b=>b.name=="StateSelector").onClick.Invoke();yield return null;yield return null;
        picker=FindObjectOfType<CustomCardStatePicker>();
        if(!picker.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.StartsWith("✓  中毒")))throw new Exception("Current state is not marked in the list.");
        for(int i=0;i<4;i++){Click("下一页",last:true);yield return null;}
        if(!picker.GetComponentsInChildren<Button>().Any(b=>b.name=="StateResult.preview_95"))throw new Exception("States beyond 80 are unreachable.");
        Click("返回编辑",last:true);yield return null;
        inspector.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.定位此节点").onClick.Invoke();yield return null;yield return null;
        if(group.Collapsed)throw new Exception("Locating group child did not expand it.");
        var palette=graph.GetComponentsInChildren<ScrollRect>().First(s=>s.name=="Scroll-NodePalette");var paletteInput=palette.GetComponentInChildren<TMP_InputField>();paletteInput.text="BUFF";yield return null;yield return null;
        if(palette.GetComponentsInChildren<Button>().Count(b=>b.GetComponentInChildren<TMP_Text>()?.text.Contains("状态")==true)<5)throw new Exception("Palette BUFF search omits state nodes.");
        paletteInput.text="无匹配内容";yield return null;
        if(palette.GetComponentsInChildren<Button>().Length!=0||!palette.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.StartsWith("没有匹配")))throw new Exception("No-results search still lists unrelated common combinations.");
        var selected=graph.Selection.ToArray();var pan=new Vector2(doc.Graph.PanX,doc.Graph.PanY);
        Click("作品库");yield return null;yield return null;Click("返回编辑",last:true);yield return null;yield return null;
        graph=owner.GetComponentInChildren<CustomCardGraphEditor>();if(!selected.SequenceEqual(graph.Selection)||pan!=new Vector2(doc.Graph.PanX,doc.Graph.PanY))throw new Exception("Library return lost selection/viewport.");
        Set(owner,"dirty",true);Click("作品库");yield return null;yield return null;Click("新建卡牌");yield return null;Click("保存并切换",last:true);yield return null;yield return null;
        if(CustomCardLibrary.List().All(d=>d.Id!=doc.Id)||ReferenceEquals(doc,typeof(CustomCardWorkshopController).GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner)))throw new Exception("Save-and-switch lost the previous work or did not switch.");
        Set(owner,"doc",Sample());Set(owner,"tab",0);Invoke(owner,"Render");yield return null;yield return null;
    }
    private IEnumerator DraftLifecycleContract(GameObject root)
    {
        int initialCount=CustomCardLibrary.List().Count;
        CustomCardWorkshopController Open()=>FindObjectOfType<CustomCardWorkshopController>();
        bool Dirty(CustomCardWorkshopController editor)=>(bool)typeof(CustomCardWorkshopController).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
        CustomCardDocument Document(CustomCardWorkshopController editor)=>(CustomCardDocument)typeof(CustomCardWorkshopController).GetField("doc",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
        for(int i=0;i<3;i++)
        {
            CustomCardWorkshop.Show(root.transform);yield return null;yield return null;yield return null;
            var editor=Open();var graph=editor.GetComponentInChildren<CustomCardGraphEditor>();var doc=Document(editor);
            if(Dirty(editor))throw new Exception("Opening a pristine draft marks it modified through automatic viewport fitting.");
            graph.Fit();graph.FocusNode(doc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect).Id);graph.PanTo(new Vector2(180,240));
            var wheel=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,graph.Viewport.TransformPoint(graph.Viewport.rect.center)),scrollDelta=new Vector2(0,1)};
            graph.Scroll(wheel);yield return null;
            Click("卡牌属性");yield return null;Click("卡面");yield return null;Click("作品库");yield return null;Click("返回编辑",last:true);yield return null;Click("效果蓝图");yield return null;yield return null;
            if(Dirty(editor)||CustomCardLibrary.List().Count!=initialCount)throw new Exception("Browsing pages or moving the viewport creates a library work.");
            if(i==1){var window=editor.transform.parent.gameObject;window.SetActive(false);Destroy(window);}
            else editor.GetComponentsInChildren<Button>().Single(b=>b.name=="CardAction.关闭").onClick.Invoke();
            yield return null;yield return null;
            if(CustomCardLibrary.List().Count!=initialCount)throw new Exception("Closing/reopening an untouched draft changes the library.");
        }
        // A deliberate edit must still be protected by the normal close/save path.
        CustomCardWorkshop.Show(root.transform);yield return null;yield return null;
        var edited=Open();var current=Document(edited);var id=current.Id;
        edited.Change(()=>current.Name="主动编辑的作品");yield return null;yield return null;
        edited.GetComponentsInChildren<Button>().Single(b=>b.name=="CardAction.关闭").onClick.Invoke();yield return null;yield return null;
        var saved=CustomCardLibrary.List().Single(d=>d.Id==id);
        if(saved.Name!="主动编辑的作品"||saved.Revision!=1||CustomCardLibrary.List().Count!=initialCount+1)throw new Exception("Closing an edited draft must save exactly once.");
        CustomCardWorkshop.Show(root.transform);yield return null;yield return null;var reopened=Open();
        typeof(CustomCardWorkshopController).GetMethod("Switch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reopened,new object[]{saved});yield return null;yield return null;
        reopened.GetComponentInChildren<CustomCardGraphEditor>().Fit();reopened.GetComponentsInChildren<Button>().Single(b=>b.name=="CardAction.关闭").onClick.Invoke();yield return null;yield return null;
        if(CustomCardLibrary.List().Single(d=>d.Id==id).Revision!=1)throw new Exception("Viewing an existing work writes a new revision.");
        CustomCardLibrary.Delete(saved);
        // Explicit Save may persist the starting blueprint; no content/name heuristic may block it.
        CustomCardWorkshop.Show(root.transform);yield return null;yield return null;var explicitEditor=Open();var explicitDoc=Document(explicitEditor);
        Click("保存");yield return null;explicitEditor.GetComponentsInChildren<Button>().Single(b=>b.name=="CardAction.关闭").onClick.Invoke();yield return null;yield return null;
        var explicitSaved=CustomCardLibrary.List().Single(d=>d.Id==explicitDoc.Id);
        if(explicitSaved.Revision!=1)throw new Exception("An explicitly saved default blueprint was lost or saved twice.");CustomCardLibrary.Delete(explicitSaved);
        // Authored layout edits and interrupted closing must still save, even though they do not alter Lua.
        CustomCardWorkshop.Show(root.transform);yield return null;yield return null;var layoutEditor=Open();var layoutDoc=Document(layoutEditor);var graphEditor=layoutEditor.GetComponentInChildren<CustomCardGraphEditor>();
        var effect=layoutDoc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);graphEditor.SelectNode(effect.Id);
        var drag=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,graphEditor.Viewport.TransformPoint(graphEditor.Viewport.rect.center)),button=PointerEventData.InputButton.Left};
        graphEditor.BeginMove(effect.Id,drag);drag.position+=new Vector2(80,-40);graphEditor.Move(drag);graphEditor.EndMove();
        if(!Dirty(layoutEditor))throw new Exception("Authored node layout edit was mistaken for viewport browsing.");
        var owned=layoutEditor.transform.parent.gameObject;owned.SetActive(false);Destroy(owned);yield return null;yield return null;
        var layoutSaved=CustomCardLibrary.List().Single(d=>d.Id==layoutDoc.Id);
        if(layoutSaved.Revision!=1||layoutSaved.Graph.Nodes.First(n=>n.Id==effect.Id).X!=effect.X)throw new Exception("Interrupted close lost an authored node layout edit.");CustomCardLibrary.Delete(layoutSaved);
        if(CustomCardLibrary.List().Count!=initialCount)throw new Exception("Draft lifecycle acceptance left temporary works.");
    }
    private IEnumerator ModalLayerContract(GameObject root,Canvas canvas)
    {
        Screen.SetResolution(960,720,false);yield return null;yield return null;
        var source=CustomCardUi.CreateRect("IndependentToolbox",root.transform,new(.5f,.5f),new(.5f,.5f),new(.5f,.5f),new(620,480));
        var owner=source.AddComponent<Canvas>();owner.overrideSorting=true;owner.sortingOrder=37;owner.worldCamera=camera;source.AddComponent<GraphicRaycaster>();
        CustomCardUi.AddImage(source,new Color(.08f,.06f,.25f));
        var launch=CustomCardWorkshopController.Button(source.transform,"打开工坊",()=>CustomCardWorkshop.Show(source.transform),100);
        launch.onClick.Invoke();yield return null;yield return null;
        var controller=FindObjectOfType<CustomCardWorkshopController>();var modal=controller.transform.parent;
        void BlockedBy(Transform expected)
        {
            var point=RectTransformUtility.WorldToScreenPoint(camera,source.transform.position);var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            if(hits.Count==0||!(hits[0].gameObject.transform==expected||hits[0].gameObject.transform.IsChildOf(expected)))throw new Exception("Modal raycast is behind an independent toolbox canvas.");
        }
        if(modal.GetComponent<Canvas>().sortingOrder<=owner.sortingOrder)throw new Exception("Workshop ignored opener canvas order.");
        BlockedBy(modal);Invoke(controller,"Preview");yield return null;yield return null;
        var child=root.transform.Find("CustomCards.Preview");if(child==null||child.GetComponent<Canvas>().sortingOrder<=modal.GetComponent<Canvas>().sortingOrder)throw new Exception("Nested preview is not above its owner.");
        BlockedBy(child);child.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.关闭").onClick.Invoke();yield return null;yield return null;BlockedBy(modal);
        Invoke(controller,"Preview");yield return null;
        Set(controller,"dirty",false);modal.gameObject.SetActive(false);Destroy(modal.gameObject);yield return null;yield return null;
        if(root.transform.Find("CustomCards.Preview")!=null)throw new Exception("Owner left a modal preview alive.");
        BlockedBy(source.transform);Destroy(source);yield return null;yield return null;
    }
    private static void Invoke(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
    private static void EditClick(string label){Click("编辑");Click(label,last:true);}
    private static void CloseInspector(CustomCardGraphEditor graph)
    {
        var popup=(GameObject)typeof(CustomCardGraphEditor).GetField("popup",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(graph);
        if(popup!=null)Click("关闭",last:true);
        else CustomCardUiLifetime.ReleaseFocus(graph.transform);
    }
    private static void Click(string label,bool last=false)
    {
        int Layer(Button b){var t=b.transform;while(t.parent!=null&&t.parent.GetComponent<Canvas>()==null)t=t.parent;return t.GetSiblingIndex();}
        var buttons=FindObjectsOfType<Button>().Where(b=>b.gameObject.activeInHierarchy&&(b.name=="CardAction."+label||b.GetComponentInChildren<TMP_Text>()?.text==label)).OrderBy(b=>SortingLayer.GetLayerValueFromID(b.GetComponentInParent<Canvas>().sortingLayerID)).ThenBy(b=>b.GetComponentInParent<Canvas>().sortingOrder).ThenBy(Layer).ThenBy(b=>b.transform.GetSiblingIndex()).ToArray();
        if(buttons.Length==0)throw new Exception("Button missing: "+label);(last?buttons.Last():buttons.First()).onClick.Invoke();
    }
    private void Capture(string name,Transform root,Vector2Int size)
    {
        Canvas.ForceUpdateCanvases();
        foreach(var auxiliary in root.GetComponentsInChildren<Transform>().Where(t=>t.name=="CustomCards.States"||t.name=="CustomCards.Guide"))
        {
            var header=(RectTransform)auxiliary.Find("Window/Header");
            foreach(var child in header.GetComponentsInChildren<TMP_Text>())
            {
                child.ForceMeshUpdate();foreach(var c in child.textInfo.characterInfo.Take(child.textInfo.characterCount).Where(c=>c.isVisible))
                {
                    var p=header.InverseTransformPoint(child.rectTransform.TransformPoint(c.topRight));
                    if(!header.rect.Contains(new Vector2(p.x,p.y)))throw new Exception(name+": auxiliary heading escapes header: "+p+" in "+header.rect);
                }
            }
        }
        var controller=FindObjectOfType<CustomCardWorkshopController>();
        var content=(Transform)controller.GetType().GetField("content",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
        var contentViewport=content.GetComponentInParent<ScrollRect>().viewport.rect;
        if(contentViewport.width<300||contentViewport.height<120)throw new Exception(name+": editor workspace collapsed");
        var graph=FindObjectOfType<CustomCardGraphEditor>();
        if(graph!=null)
        {
            var info=(TMP_Text)typeof(CustomCardGraphEditor).GetField("information",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(graph);
            var viewport=content.GetComponentInParent<ScrollRect>().viewport;var position=viewport.InverseTransformPoint(info.rectTransform.TransformPoint(new Vector3(0,info.rectTransform.rect.yMin,0)));
            if(position.y<viewport.rect.yMin-3)throw new Exception(name+": graph diagnostics clipped below workspace");
        }
        var target=new RenderTexture(size.x,size.y,24);camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Destroy(image);Destroy(target);
        var corners=new Vector3[4];int buttons=0;
        foreach(var button in root.GetComponentsInChildren<Button>())
        {
            var rect=button.transform as RectTransform;rect.GetWorldCorners(corners);var left=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var right=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
            if(right.y<30||left.y>size.y-30)continue;
            var visible=Rect.MinMaxRect(left.x,left.y,right.x,right.y);
            foreach(var mask in button.GetComponentsInParent<RectMask2D>())
            {
                var points=new Vector3[4];mask.rectTransform.GetWorldCorners(points);var a=RectTransformUtility.WorldToScreenPoint(camera,points[0]);var b=RectTransformUtility.WorldToScreenPoint(camera,points[2]);
                visible=Rect.MinMaxRect(Mathf.Max(visible.xMin,a.x),Mathf.Max(visible.yMin,a.y),Mathf.Min(visible.xMax,b.x),Mathf.Min(visible.yMax,b.y));
            }
            if(visible.width<=0||visible.height<=0)continue;
            if(visible.xMin<-.1f||visible.xMax>size.x+1)throw new Exception(name+": button extends outside viewport: "+button.name);
            buttons++;
        }
        cases.Add(new{name,width=size.x,height=size.y,visibleButtons=buttons,editorWidth=contentViewport.width,editorHeight=contentViewport.height});
    }
}
