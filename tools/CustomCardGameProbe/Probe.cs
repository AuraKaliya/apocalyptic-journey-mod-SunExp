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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;
using AuraUi.Shared;
using ButtonManager=Michsky.MUIP.ButtonManager;

namespace AuraWorkshopUiProbe;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig config)
    {
        var arg=Environment.GetCommandLineArgs().FirstOrDefault(s=>s.StartsWith("-aura-workshop-probe=",StringComparison.Ordinal));
        if(arg==null)return;
        var probe=new GameObject("AuraWorkshopUiAcceptance").AddComponent<Probe>();probe.Output=Path.GetFullPath(arg.Substring("-aura-workshop-probe=".Length));
        UnityEngine.Object.DontDestroyOnLoad(probe.gameObject);
    }
}

public sealed class Probe:MonoBehaviour
{
    internal string Output="";
    private UnityEngine.Component? controller;
    private readonly List<object> cases=new();
    private readonly List<string> errors=new();
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private float started;
    private bool complete;
    private Button? toolboxEntry;
    private Transform? toolboxPanel;
    private bool nestedLayers,toolboxRestored,reopened;
    private bool legacyDescription,scaledNodeText;
    private bool inputEditing,percentInputs,trialRemoved;
    private bool stateEditing,guideNavigation;
    private int loadedStates;
    private bool pristineDraftLifecycle;
    private readonly List<object> detailWorkspaces=new();
    private void Awake(){Application.runInBackground=true;started=Time.realtimeSinceStartup;}
    private void Start(){Directory.CreateDirectory(Output);StartCoroutine(RunGuarded());}
    private void Update(){if(!complete&&Time.realtimeSinceStartup-started>100)Finish("Game UI readiness timed out.");}
    private IEnumerator RunGuarded()
    {
        var run=Run();
        while(!complete)
        {
            bool next;object? value;
            try{next=run.MoveNext();value=next?run.Current:null;}
            catch(Exception ex){Finish(ex.ToString());yield break;}
            if(!next)yield break;
            yield return value;
        }
    }
    private IEnumerator Run()
    {
        while(UIManager.Instance==null||UIManager.Instance.canvasTf==null||UIManager.Instance.GetUI<MainMenuUI>("MainMenuUI")==null||!AuraGameData.Shared.GameApi.AuraGameDataHostApi.IsNativeCatalogReady)yield return null;
        yield return new WaitForSecondsRealtime(3);
        var before=Singleton<GameRuntimeData>.Instance.CardData.Count;
        var settings=UIManager.Instance.ShowUI<SettingUI>("SettingUI");
        for(int i=0;i<12;i++)yield return null;
        var tab=settings.GetComponentsInChildren<Transform>(true).First(t=>t.name=="AuraToolsSettingsTabButton");
        ExecuteEvents.Execute(tab.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
        for(int i=0;i<300;i++)
        {
            var entry=settings.GetComponentsInChildren<AuraUiStableId>().FirstOrDefault(id=>id.Value=="toolbox.module.gameplay.custom-cards.settings");
            if(entry!=null){toolboxEntry=entry.GetComponent<Button>();break;}yield return null;
        }
        if(toolboxEntry==null)throw new Exception("Toolbox entry did not appear after clicking the native AuraTools tab.");
        toolboxPanel=settings.GetComponentsInChildren<Transform>(true).First(t=>t.name=="AuraToolsSettingsPanel");
        var toolboxSize=((RectTransform)toolboxPanel).rect.size;
        var toolboxScroll=toolboxEntry.GetComponentInParent<ScrollRect>();float toolboxPosition=toolboxScroll!=null?toolboxScroll.verticalNormalizedPosition:1;
        var library=typeof(CustomCardWorkshop).Assembly.GetType("AuraToolsExp.Dll.Features.CustomCards.CustomCardLibrary")!;
        string LibrarySnapshot()=>string.Join("|",((IReadOnlyList<CustomCardDocument>)library.GetMethod("List",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!).OrderBy(d=>d.Id).Select(d=>d.Id+"@"+d.Revision));
        string libraryBefore=LibrarySnapshot();
        for(int attempt=0;attempt<3;attempt++)
        {
            toolboxEntry.onClick.Invoke();for(int frame=0;frame<8;frame++)yield return null;
            controller=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(c=>c.GetType().Name=="CustomCardWorkshopController");
            if((bool)controller.GetType().GetField("dirty",Private)!.GetValue(controller)!)throw new Exception("Opening the real workshop marks its default blueprint as edited.");
            foreach(var page in new[]{1,2,6,0}){controller.GetType().GetMethod("Navigate",Private)!.Invoke(controller,new object[]{page});yield return null;yield return null;}
            var browsingGraph=controller.GetComponentsInChildren<MonoBehaviour>().First(c=>c.GetType().Name=="CustomCardGraphEditor");
            browsingGraph.GetType().GetMethod("Fit",Private)!.Invoke(browsingGraph,null);
            browsingGraph.GetType().GetMethod("PanTo",Private)!.Invoke(browsingGraph,new object[]{new Vector2(100,200)});
            if((bool)controller.GetType().GetField("dirty",Private)!.GetValue(controller)!||LibrarySnapshot()!=libraryBefore)throw new Exception("Browsing a default blueprint creates a library work.");
            if(attempt==1){var owner=controller.transform.parent.gameObject;owner.SetActive(false);Destroy(owner);}
            else controller.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.关闭").onClick.Invoke();
            controller=null;yield return null;yield return null;
            if(LibrarySnapshot()!=libraryBefore)throw new Exception("Closing an untouched workshop mutates the library.");
        }
        pristineDraftLifecycle=true;
        EventSystem.current.SetSelectedGameObject(toolboxEntry.gameObject);toolboxEntry.onClick.Invoke();
        yield return null;yield return null;
        controller=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(c=>c.GetType().Name=="CustomCardWorkshopController");
        var document=new CustomCardDocument{Name="星火"};Set("doc",document);
        foreach(int page in new[]{0,1,2})
        {
            if(page==2){document.Artwork.UsePixels=true;document.Artwork.Pixels=Convert.ToBase64String(new byte[document.Artwork.Size*document.Artwork.Size]);}
            Set("tab",page);Invoke("Render");
            for(int i=0;i<8;i++)yield return null;
            Canvas.ForceUpdateCanvases();
            var canvas=controller.GetComponentInParent<Canvas>().rootCanvas;
            var rect=(RectTransform)controller.transform;
            AssertFullWindow(rect);
            AssertHitWithin(toolboxEntry.transform,controller.transform.parent,"Toolbox entry is not blocked by the workshop.");
            if(page==0)
            {
                var viewport=controller.GetComponentsInChildren<RectTransform>().First(r=>r.name=="GraphViewport");
                foreach(var header in controller.GetComponentsInChildren<MonoBehaviour>().Where(c=>c.GetType().Name=="CustomCardNodeChrome"))
                {
                    var points=new Vector3[4];((RectTransform)header.transform).GetWorldCorners(points);
                    if(points.Select(p=>viewport.InverseTransformPoint(p)).Any(p=>p.x<viewport.rect.xMin-2||p.x>viewport.rect.xMax+2||p.y<viewport.rect.yMin-2||p.y>viewport.rect.yMax+2))throw new Exception("Native initial graph clips a node.");
                }
            }
            if(page==1)
            {
                var native=controller.GetComponentInChildren<DictionaryShowItem>();
                if(native==null||native.dataConfig.data["Name"]!="星火")throw new Exception("Native dictionary preview did not bind the draft.");
                if(native.dataConfig.data["Description"].Contains("结束本次流程")||native.dataConfig.data["Description"].Contains(" · "))throw new Exception("Native preview still exposes compiler-style descriptions.");
                var row=controller.GetComponentsInChildren<Transform>().First(t=>t.name=="CostAndRarity");
                var input=row.GetComponentInChildren<TMP_InputField>();var surface=input.transform.parent.GetComponent<Image>();var idle=surface.color;
                input.Select();input.ActivateInputField();yield return null;yield return null;
                if(!input.isFocused||surface.color==idle||input.caretWidth<2||input.selectionColor.a<.25f)throw new Exception("Native compound input has no visible editing state.");
                Capture(canvas,rect,Path.Combine(Output,"native-input-cost.png"));inputEditing=true;
                input.text="3";input.onEndEdit.Invoke("3");yield return null;input.DeactivateInputField();yield return null;
                native=controller.GetComponentInChildren<DictionaryShowItem>();
                if(native.dataConfig.data["Expend"]!="3")throw new Exception("Native preview cost did not refresh.");
            }
            var action=controller.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.制作到仓库");
            var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            float minimumBorder=100;
            foreach(var edge in controller.GetComponentsInChildren<Image>().Where(i=>i.name.StartsWith("Edge",StringComparison.Ordinal)&&i.transform.parent.name=="ControlBorder"&&i.color.a>.2f))
            {
                var corners=new Vector3[4];edge.rectTransform.GetWorldCorners(corners);
                var a=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var b=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
                minimumBorder=Mathf.Min(minimumBorder,Mathf.Min(Mathf.Abs(b.x-a.x),Mathf.Abs(b.y-a.y)));
            }
            if(minimumBorder<1)throw new Exception("A native control border became thinner than one physical pixel: "+minimumBorder);
            var point=RectTransformUtility.WorldToScreenPoint(camera,((RectTransform)action.transform).TransformPoint(((RectTransform)action.transform).rect.center));
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            if(hits.Count==0||!(hits[0].gameObject.transform==action.transform||hits[0].gameObject.transform.IsChildOf(action.transform)))throw new Exception("Native craft button is covered.");
            var file=Path.Combine(Output,"native-page-"+page+".png");Capture(canvas,rect,file);
            cases.Add(new{page,width=Screen.width,height=Screen.height,canvasScale=canvas.scaleFactor,windowWidth=rect.rect.width,windowHeight=rect.rect.height,minimumBorderPixels=minimumBorder,craftRaycast=true,nativeCards=controller.GetComponentsInChildren<DictionaryShowItem>().Length,toolboxOrder=toolboxPanel.GetComponent<Canvas>().sortingOrder,workshopOrder=controller.GetComponentInParent<Canvas>().sortingOrder,toolboxBlocked=true});
        }
        Set("tab",0);Invoke("Render");yield return null;yield return null;
        var graph=controller.GetComponentsInChildren<MonoBehaviour>().First(c=>c.GetType().Name=="CustomCardGraphEditor");
        foreach(float zoom in new[]{.4f,.55f,.79f,.8f,1f,1.6f})
        {
            document.Graph.Zoom=zoom;document.Graph.PanX=24-document.Graph.Nodes.Min(n=>n.X)*zoom;document.Graph.PanY=24-document.Graph.Nodes.Min(n=>n.Y)*zoom;graph.GetType().GetMethod("ApplyView",Private)!.Invoke(graph,null);yield return null;
            foreach(var node in graph.GetComponentsInChildren<MonoBehaviour>().Where(c=>c.GetType().Name=="CustomCardNodeChrome"))
            foreach(var text in node.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();var bounds=((RectTransform)node.transform).rect;
                foreach(var character in text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c=>c.isVisible))
                foreach(var point in new[]{character.bottomLeft,character.topRight})
                {
                    var local=node.transform.InverseTransformPoint(text.transform.TransformPoint(point));
                    if(!bounds.Contains(new Vector2(local.x,local.y)))throw new Exception("Native zoom text exceeds its node: "+zoom+" / "+text.text);
                }
            }
            if(zoom==.4f)Capture(controller.GetComponentInParent<Canvas>().rootCanvas,(RectTransform)controller.transform,Path.Combine(Output,"native-graph-overview.png"));
        }
        scaledNodeText=true;
        var condition=new CardRuleBlock{Kind=CardBlockKind.If,Condition=CardValue.Compare(CardValueKind.Less,CardValue.Reading(CardDataField.HealthPercent),CardValue.Ratio(.5)),Then=new(){new(){Effect=CardEffectKind.Shield,Object=new(),Value=CardValue.Constant(3)}}};
        var numericDoc=new CustomCardDocument{Name="百分比条件",Targeted=false,Graph=CardBlueprintMigration.FromProgram(new[]{new CustomCardRule{Blocks=new(){condition}}})};
        Set("doc",numericDoc);Invoke("Render");yield return null;yield return null;
        graph=controller.GetComponentsInChildren<MonoBehaviour>().First(c=>c.GetType().Name=="CustomCardGraphEditor");
        var comparison=numericDoc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Value&&n.ValueKind==CardValueKind.Less);numericDoc.Graph.Zoom=1;
        graph.GetType().GetMethod("FocusNode",Private)!.Invoke(graph,new object[]{comparison.Id});yield return null;yield return null;
        TMP_InputField NumericField()=>graph.GetComponentsInChildren<Transform>().First(t=>t.name=="Node."+comparison.Id).GetComponentInChildren<TMP_InputField>();
        var numericInput=NumericField();if(numericInput.text!="50")throw new Exception("Native percent input did not show 50%.");
        var beforeFocus=numericInput.targetGraphic.color;numericInput.Select();numericInput.ActivateInputField();yield return null;yield return null;
        if(!numericInput.isFocused||numericInput.targetGraphic.color==beforeFocus)throw new Exception("Node input focus is not visible in the game.");
        Capture(controller.GetComponentInParent<Canvas>().rootCanvas,(RectTransform)controller.transform,Path.Combine(Output,"native-input-node.png"));
        numericInput.text="12.3456789012345";numericInput.onEndEdit.Invoke(numericInput.text);yield return null;yield return null;
        if(Math.Abs(comparison.Second-.123456789012345)>1e-16)throw new Exception("Native percent edit lost its precision.");
        numericInput=NumericField();numericInput.Select();numericInput.ActivateInputField();yield return null;numericInput.text="abc";numericInput.onEndEdit.Invoke(numericInput.text);yield return null;
        var feedback=typeof(CustomCardWorkshop).Assembly.GetType("AuraToolsExp.Dll.Features.CustomCards.CustomCardInputFeedback")!;
        if((bool)feedback.GetMethod("CommitAll",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{controller.transform})!)throw new Exception("Invalid native input was accepted for submission.");
        Capture(controller.GetComponentInParent<Canvas>().rootCanvas,(RectTransform)controller.transform,Path.Combine(Output,"native-input-error.png"));
        var retainedGraph=graph;var retainedPan=new Vector2(numericDoc.Graph.PanX,numericDoc.Graph.PanY);
        controller.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.节点指南").onClick.Invoke();yield return null;yield return null;
        var guideRoot=controller.GetComponentInParent<Canvas>().rootCanvas.transform.Find("CustomCards.Guide");
        if(guideRoot==null)throw new Exception("Guide is blocked by invalid input.");
        var guideSearch=guideRoot.GetComponentInChildren<TMP_InputField>();guideSearch.Select();guideSearch.ActivateInputField();yield return null;yield return null;
        if(!guideSearch.isFocused||numericInput.isFocused||numericInput.text!="abc")throw new Exception("Guide steals or loses the numeric edit buffer.");
        guideSearch.text="BUFF";yield return null;yield return null;
        if(guideRoot.GetComponentsInChildren<Transform>().Count(t=>t.name=="GuideIndexRow")!=5)throw new Exception("Native guide index cannot find all state nodes.");
        Capture(guideRoot.GetComponent<Canvas>().rootCanvas,(RectTransform)guideRoot.Find("Window"),Path.Combine(Output,"native-guide-index.png"));
        guideRoot.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.返回编辑").onClick.Invoke();yield return null;yield return null;
        if(graph!=retainedGraph||numericInput.text!="abc"||numericInput.readOnly||retainedPan!=new Vector2(numericDoc.Graph.PanX,numericDoc.Graph.PanY))throw new Exception("Native guide return changed editing context.");
        guideNavigation=true;
        numericInput.text="50";numericInput.onEndEdit.Invoke(numericInput.text);yield return null;yield return null;percentInputs=true;
        trialRemoved=typeof(CustomCardWorkshop).Assembly.GetType("AuraToolsExp.Dll.Features.CustomCards.CustomCardTrial")==null&&!controller.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="试算");
        if(!trialRemoved)throw new Exception("Trial feature remains in the shipped editor.");
        var nativeApi=typeof(CustomCardWorkshop).Assembly.GetType("AuraToolsExp.Dll.Features.CustomCards.CustomCardNative")!;
        var states=(IReadOnlyList<CardStateOption>)nativeApi.GetMethod("States",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;loadedStates=states.Count;
        if(states.Count==0)throw new Exception("Native state catalog is unexpectedly empty.");
        int verifiedModOwners=0;
        foreach(var state in states)
        {
            if(Singleton<GameConfigManager>.Instance.TryGetModDataConfigOwner(state.Id,out var owner)&&owner!=null)
            {
                verifiedModOwners++;if(state.Source!=owner.ModId)throw new Exception("Picker misidentifies a host-owned MOD state: "+state.Id+" / "+state.Source+" != "+owner.ModId);
            }
        }
        if(verifiedModOwners==0)throw new Exception("Native provenance acceptance needs at least one loaded MOD-owned state.");
        File.WriteAllText(Path.Combine(Output,"state-provenance.json"),JsonConvert.SerializeObject(new{verifiedModOwners,sources=states.GroupBy(s=>s.Source).Select(g=>new{source=g.Key,label=g.First().SourceLabel,count=g.Count()})},Formatting.Indented));
        var selectedState=states.First(s=>s.Name.Length>0&&s.Description.Length>0);
        var stateDoc=new CustomCardDocument{Name="状态选择验收"};var stateNode=stateDoc.Graph.Nodes.First(n=>n.Kind==CardNodeKind.Effect);stateNode.Effect=CardEffectKind.AddBuff;
        Set("doc",stateDoc);Invoke("Render");yield return null;yield return null;
        graph=controller.GetComponentsInChildren<MonoBehaviour>().First(c=>c.GetType().Name=="CustomCardGraphEditor");
        stateDoc.Graph.Zoom=1;graph.GetType().GetMethod("FocusNode",Private)!.Invoke(graph,new object[]{stateNode.Id});yield return null;yield return null;
        graph.GetComponentsInChildren<Transform>().First(t=>t.name=="Node."+stateNode.Id).Find("StateSelector").GetComponent<Button>().onClick.Invoke();yield return null;yield return null;
        var stateRoot=controller.GetComponentInParent<Canvas>().rootCanvas.transform.Find("CustomCards.States");
        AssertFullWindow((RectTransform)stateRoot.Find("Window"));AssertHitWithin(toolboxEntry.transform,stateRoot,"Native state picker lost modal ownership.");
        stateRoot.GetComponentInChildren<TMP_InputField>().text=selectedState.Id;yield return null;yield return null;
        stateRoot.GetComponentsInChildren<Button>().Single(b=>b.name=="StateResult."+selectedState.Id).onClick.Invoke();yield return null;yield return null;
        if(!stateRoot.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains(selectedState.SourceLabel)))throw new Exception("Native picker omits resource provenance.");
        Capture(stateRoot.GetComponent<Canvas>().rootCanvas,(RectTransform)stateRoot.Find("Window"),Path.Combine(Output,"native-state-picker.png"));
        stateRoot.GetComponentsInChildren<Button>().First(b=>b.name=="ConfirmState").onClick.Invoke();yield return null;yield return null;
        var compiledState=CustomCardCompiler.Compile(stateDoc,id=>states.FirstOrDefault(s=>s.Id==id)?.Name);
        if(stateNode.ResourceId!=selectedState.Id||!compiledState.Success||!compiledState.BuffReferences.ContainsKey(selectedState.Id)||controller.GetComponentInParent<Canvas>().rootCanvas.transform.Find("CustomCards.States")!=null)throw new Exception("Native state selection failed to round-trip into card compilation.");
        nativeApi.GetMethod("CheckLua",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{compiledState});
        Capture(controller.GetComponentInParent<Canvas>().rootCanvas,(RectTransform)controller.transform,Path.Combine(Output,"native-state-node.png"));stateEditing=true;
        Invoke("Preview");for(int i=0;i<6;i++)yield return null;
        var preview=controller.GetComponentInParent<Canvas>().rootCanvas.transform.Find("CustomCards.Preview");
        if(preview==null)throw new Exception("Magnified preview did not open.");
        AssertHitWithin(toolboxEntry.transform,preview,"Nested preview is behind the toolbox/workshop.");
        if(preview.GetComponent<Canvas>().sortingOrder<=controller.GetComponentInParent<Canvas>().sortingOrder)throw new Exception("Nested modal did not acquire a higher layer.");
        Capture(preview.GetComponent<Canvas>().rootCanvas,(RectTransform)preview.Find("Window"),Path.Combine(Output,"native-layer-preview.png"));
        preview.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.关闭").onClick.Invoke();yield return null;yield return null;
        AssertHitWithin(toolboxEntry.transform,controller.transform.parent,"Preview close changed workshop ordering.");nestedLayers=true;
        Set("dirty",false);
        controller.GetComponentsInChildren<Button>().First(b=>b.name=="CardAction.关闭").onClick.Invoke();controller=null;
        yield return null;yield return null;
        AssertHitWithin(toolboxEntry.transform,toolboxEntry.transform,"Closing the workshop did not restore toolbox input.");toolboxRestored=true;
        if(((RectTransform)toolboxPanel).rect.size!=toolboxSize||toolboxScroll!=null&&Mathf.Abs(toolboxScroll.verticalNormalizedPosition-toolboxPosition)>.01f)throw new Exception("Returning from the editor changed toolbox size or scroll position.");
        EventSystem.current.SetSelectedGameObject(toolboxEntry.gameObject);toolboxEntry.onClick.Invoke();yield return null;yield return null;
        controller=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(c=>c.GetType().Name=="CustomCardWorkshopController");
        AssertHitWithin(toolboxEntry.transform,controller.transform.parent,"Reopened workshop lost its layer.");reopened=true;
        Invoke("Preview");yield return null;yield return null;
        Set("dirty",false);var root=controller.transform.parent.gameObject;root.SetActive(false);Destroy(root);controller=null;
        yield return null;yield return null;
        if(UIManager.Instance.canvasTf.Find("CustomCards.Preview")!=null)throw new Exception("Closing owner left a child modal behind.");
        AssertHitWithin(toolboxEntry.transform,toolboxEntry.transform,"Owner teardown left an invisible blocker.");
        foreach(var detail in new[]{("audio","AuraToolsExp.Dll.Features.Audio.AuraToolsAudioSettingsPage","ShowBattleBgm"),("logging","AuraToolsExp.Dll.Features.Logging.AuraToolsLoggingSettingsPage","Show"),("starter-deck","AuraToolsExp.Dll.Features.StarterDeck.AuraToolsStarterDeckSettingsPage","Show")})
        {
            typeof(CustomCardWorkshop).Assembly.GetType(detail.Item2)!.GetMethod(detail.Item3,BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{toolboxEntry.transform});
            for(int i=0;i<8;i++)yield return null;
            var host=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(c=>c.GetType().Name=="AuraToolsWindowHost");
            var window=(RectTransform)host.transform.Find("Window");AssertFullWindow(window);AssertHitWithin(toolboxEntry.transform,host.transform,"Tool detail lost its sorting layer.");
            Capture(host.GetComponent<Canvas>().rootCanvas,window,Path.Combine(Output,"native-details-"+detail.Item1+".png"));
            detailWorkspaces.Add(new{tool=detail.Item1,width=window.rect.width,height=window.rect.height});
            window.Find("Header").GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<TMP_Text>()?.text=="返回"||b.GetComponentInChildren<Text>()?.text=="返回").onClick.Invoke();
            yield return null;yield return null;AssertHitWithin(toolboxEntry.transform,toolboxEntry.transform,"Tool detail return left a blocker.");
        }
        var oldData=new Dictionary<string,string>(CustomCardCompiler.Compile(new CustomCardDocument()).Scripts)
        {
            ["Id"]="AuraToolsExp_custom_probe_"+Guid.NewGuid().ToString("N"),["Name"]="描述验收",["Description"]="使用时：\n  使用时选中目标 · 造成普通伤害 6\n  结束本次流程。",["AuraToolsCustomCardVersion"]="2",["Type"]="攻击牌",["Expend"]="1",["Tag"]="",["Rarity"]="1",["Icon"]=""
        };
        string raw=Convert.ToBase64String(GZip.CompressString(JsonConvert.SerializeObject(oldData)));
        var saved=new DataConfig(new Dictionary<string,string>(oldData),new Dictionary<string,string>{{"Id",oldData["Id"]},{"RawData",raw}},true,DataType.Card);
        typeof(CustomCardWorkshop).Assembly.GetType("AuraToolsExp.Dll.Features.CustomCards.CustomCardNative")!.GetMethod("RepairPersistedData",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{saved});
        var portable=JsonConvert.DeserializeObject<Dictionary<string,string>>(GZip.DecompressToString(Convert.FromBase64String(saved.Vars["RawData"])));
        if(saved.data["Description"]!="使用时：对选中目标造成 6 点普通伤害。"||portable!["Description"]!=saved.data["Description"]||saved.data["UseScript"]!=oldData["UseScript"])throw new Exception("Existing crafted card migration did not preserve its script and portable description.");
        legacyDescription=true;
        if(Singleton<GameRuntimeData>.Instance.CardData.Count!=before)throw new Exception("UI acceptance changed warehouse card count.");
        if(LibrarySnapshot()!=libraryBefore)throw new Exception("Native UI acceptance changed the user's library.");
        if(UnityEngine.Object.FindObjectsByType<DictionaryShowItem>(FindObjectsSortMode.None).Any(c=>c.name=="DictionaryCard"))throw new Exception("Native preview survived close.");
        Finish(null);
    }
    private static void AssertFullWindow(RectTransform window)
    {
        var canvas=window.GetComponentInParent<Canvas>().rootCanvas;var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        var corners=new Vector3[4];window.GetWorldCorners(corners);var a=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var b=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
        if(Mathf.Abs(a.x-12)>2||Mathf.Abs(a.y-12)>2||Mathf.Abs(Screen.width-b.x-12)>2||Mathf.Abs(Screen.height-b.y-12)>2)throw new Exception("Detailed page does not fill the game window: "+a+" / "+b);
    }
    private void Set(string name,object value)=>controller!.GetType().GetField(name,Private)!.SetValue(controller,value);
    private void Invoke(string name)=>controller!.GetType().GetMethod(name,Private)!.Invoke(controller,null);
    private static void AssertHitWithin(Transform target,Transform owner,string error)
    {
        var canvas=target.GetComponentInParent<Canvas>().rootCanvas;var rect=(RectTransform)target;
        var point=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center));
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        if(hits.Count==0||!(hits[0].gameObject.transform==owner||hits[0].gameObject.transform.IsChildOf(owner)))throw new Exception(error+" Top hit: "+(hits.Count>0?hits[0].gameObject.name:"none"));
    }
    private void Capture(Canvas canvas,RectTransform window,string file)
    {
        var mode=canvas.renderMode;var worldCamera=canvas.worldCamera;var plane=canvas.planeDistance;
        var source=worldCamera!=null?worldCamera:Camera.main;
        var go=new GameObject("WorkshopCaptureCamera");var camera=go.AddComponent<Camera>();
        if(source!=null){camera.CopyFrom(source);camera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);}
        else{camera.orthographic=true;camera.orthographicSize=Screen.height*.5f;camera.transform.position=new Vector3(0,0,-10);}
        camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.rect=new Rect(0,0,1,1);
        foreach(var item in canvas.GetComponentsInChildren<Transform>(true))camera.cullingMask|=1<<item.gameObject.layer;
        var target=new RenderTexture(Screen.width,Screen.height,24);target.Create();var previous=RenderTexture.active;
        Texture2D? image=null;
        try
        {
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=Mathf.Max(camera.nearClipPlane+1,Mathf.Min(plane,camera.farClipPlane-1));
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;
            var corners=new Vector3[4];window.GetWorldCorners(corners);
            var low=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);var high=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
            int x=Mathf.Clamp(Mathf.FloorToInt(low.x),0,Screen.width-1),y=Mathf.Clamp(Mathf.FloorToInt(low.y),0,Screen.height-1);
            int width=Mathf.Clamp(Mathf.CeilToInt(high.x)-x,1,Screen.width-x),height=Mathf.Clamp(Mathf.CeilToInt(high.y)-y,1,Screen.height-y);
            image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(x,y,width,height),0,0);image.Apply();
            var pixels=image.GetPixels32();int bright=0;for(int i=0;i<pixels.Length;i+=4)if(pixels[i].r>90||pixels[i].g>90||pixels[i].b>90)bright++;
            if(bright<200)throw new Exception("Native capture did not contain rendered UI pixels.");
            File.WriteAllBytes(file,image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode=mode;canvas.worldCamera=worldCamera;canvas.planeDistance=plane;Canvas.ForceUpdateCanvases();
            RenderTexture.active=previous;if(image!=null)Destroy(image);target.Release();Destroy(target);Destroy(go);
        }
    }
    private void Finish(string? error)
    {
        if(complete)return;complete=true;if(error!=null)errors.Add(error);
        if(controller!=null){try{Set("dirty",false);Destroy(controller.transform.parent.gameObject);}catch(Exception ex){errors.Add(ex.Message);}}
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"report.json"),JsonConvert.SerializeObject(new{success=errors.Count==0,cases,errors,detailWorkspaces,legacyDescription,scaledNodeText,inputEditing,percentInputs,trialRemoved,stateEditing,guideNavigation,loadedStates,pristineDraftLifecycle,unity=Application.unityVersion,entryAssembly=typeof(CustomCardWorkshop).Assembly.Location,sharedAssembly=typeof(AuraShared.Core.AuraSharedPaths).Assembly.Location,warehouseUnchanged=errors.Count==0,renderedPixelCapture=errors.Count==0,openedFromToolbox=true,nestedLayers,toolboxRestored,reopened},Formatting.Indented));
        Application.Quit(errors.Count==0?0:1);
    }
}
