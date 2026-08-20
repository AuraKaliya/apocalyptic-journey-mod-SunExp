using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Runtime;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Presentation;

internal static class ReplaySceneRuntime
{
    private static ReplaySceneInstance? active;

    internal static bool IsActive => active != null;

    internal static ReplaySceneInstance? Active => active;

    internal static bool TryStart(string recordId, long eventSequence, out string message)
    {
        Stop();
        try
        {
            var document = MatchRecordStorage.Database.LoadV10(recordId);
            if (document == null)
            {
                message = "这条记录没有经过验证的 Replay Document v10。";
                return false;
            }

            active = ReplaySceneInstance.Create(document, target: null, interactive: true, includeHud: true);
            if (eventSequence > 0) active.Controller.SeekSequence(eventSequence);
            active.RenderNow();
            message = "Replay Document v10 已开始播放。";
            return true;
        }
        catch (Exception ex)
        {
            Stop();
            message = "无法开始 v10 回放：" + ex.Message;
            AuraToolsLog.Warn("[MatchRecords] v10 replay launch failed: " + ex);
            return false;
        }
    }

    internal static ReplaySceneInstance CreateExportSession(
        ReplayDocumentV10 document,
        RenderTexture target,
        bool includeHud)
    {
        return ReplaySceneInstance.Create(document, target, interactive: false, includeHud: includeHud);
    }

    internal static void Stop()
    {
        active?.Dispose();
        active = null;
    }

    internal static void ClearIfOwned(ReplaySceneInstance value)
    {
        if (ReferenceEquals(active, value)) active = null;
    }
}

internal sealed class ReplaySceneInstance : IDisposable
{
    private const int ReplayLayer = 30;
    private readonly ReplayDocumentV10 document;
    private readonly Dictionary<string, ReplayContentDefinitionV10> content;
    private readonly List<ReplayActorView> actors = new();
    private readonly List<ReplayCardView> cards = new();
    private readonly List<GameObject> intents = new();
    private readonly bool interactive;
    private readonly bool includeHud;
    private Scene scene;
    private GameObject root = null!;
    private Camera camera = null!;
    private Canvas canvas = null!;
    private Transform friendlyRoot = null!;
    private Transform enemyRoot = null!;
    private Transform cardRoot = null!;
    private Transform intentRoot = null!;
    private TextMeshProUGUI statusText = null!;
    private TextMeshProUGUI cueText = null!;
    private Slider? progress;
    private Button? playButton;
    private TextMeshProUGUI? playLabel;
    private TextMeshProUGUI? speedLabel;
    private Sprite? backgroundSprite;
    private string renderedHash = "";
    private long renderedEvent;
    private bool disposed;
    private bool paused;
    private float speed = 1f;

    private ReplaySceneInstance(ReplayDocumentV10 document, bool interactive, bool includeHud)
    {
        this.document = document;
        this.interactive = interactive;
        this.includeHud = includeHud;
        content = document.Content.Definitions.ToDictionary(item => item.Content.Key, item => item, StringComparer.Ordinal);
        Controller = new ReplayTimelineController(document);
    }

    internal ReplayTimelineController Controller { get; }

    internal Camera Camera => camera;

    internal static ReplaySceneInstance Create(
        ReplayDocumentV10 document,
        RenderTexture? target,
        bool interactive,
        bool includeHud)
    {
        var result = new ReplaySceneInstance(document, interactive, includeHud);
        result.Build(target);
        return result;
    }

    internal void AdvanceFixed(long ticks)
    {
        Controller.Advance(ticks);
        RenderNow();
    }

    internal void SeekTime(long ticks)
    {
        Controller.SeekTime(ticks);
        RenderNow();
    }

    internal void RenderNow()
    {
        if (disposed) return;
        var state = Controller.State;
        var stateHash = ReplayProjectionStateV10.Hash(state);
        if (!string.Equals(stateHash, renderedHash, StringComparison.Ordinal))
        {
            renderedHash = stateHash;
            RebuildState(state);
        }

        var current = Controller.CurrentEvent;
        if (current != null && current.Sequence != renderedEvent)
        {
            renderedEvent = current.Sequence;
            cueText.text = CueText(current);
        }

        statusText.text = "回合 " + Math.Max(1, state.TurnIndex)
                          + "    动作 " + CountCompletedActions(Controller.EventIndex) + "/" + CountCompletedActions(document.Events.Count)
                          + "    " + FormatTime(Controller.CurrentTicks) + " / " + FormatTime(Controller.DurationTicks);
        if (progress != null) progress.SetValueWithoutNotify(Controller.Progress);
        if (playLabel != null) playLabel.text = paused ? "播放" : "暂停";
        if (speedLabel != null) speedLabel.text = speed.ToString("0.#") + "x";
        Canvas.ForceUpdateCanvases();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReplaySceneRuntime.ClearIfOwned(this);
        foreach (var actor in actors) actor.Dispose();
        foreach (var card in cards) card.Dispose();
        actors.Clear();
        cards.Clear();
        if (backgroundSprite != null)
        {
            var texture = backgroundSprite.texture;
            Object.Destroy(backgroundSprite);
            if (texture != null) Object.Destroy(texture);
            backgroundSprite = null;
        }
        if (root != null) Object.Destroy(root);
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
    }

    private void Build(RenderTexture? target)
    {
        scene = SceneManager.CreateScene("AuraToolsReplay-" + document.Header.RecordId.Substring(0, Math.Min(8, document.Header.RecordId.Length)));
        root = new GameObject("AuraToolsReplaySceneRuntime");
        root.layer = ReplayLayer;
        SceneManager.MoveGameObjectToScene(root, scene);
        if (interactive) Object.DontDestroyOnLoad(root);
        root.AddComponent<ReplaySceneDriver>().Owner = this;

        var cameraObject = new GameObject("ReplayCamera", typeof(Camera));
        cameraObject.layer = ReplayLayer;
        cameraObject.transform.SetParent(root.transform, false);
        camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.045f, 0.055f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.cullingMask = 1 << ReplayLayer;
        camera.depth = interactive ? 100f : -100f;
        camera.targetTexture = target;

        var canvasObject = new GameObject("ReplayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = ReplayLayer;
        canvasObject.transform.SetParent(root.transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = short.MaxValue;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        if (interactive && EventSystem.current == null)
        {
            var events = new GameObject("ReplayEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.layer = ReplayLayer;
            events.transform.SetParent(root.transform, false);
        }

        BuildLayout(canvasObject.transform);
        SetLayerRecursively(root, ReplayLayer);
        RenderNow();
    }

    private void BuildLayout(Transform parent)
    {
        var level = content.Values.FirstOrDefault(item =>
            string.Equals(item.Content.ContentKind, "Level", StringComparison.Ordinal));
        backgroundSprite = LoadSprite(level?.Display.BackgroundAssetSha256 ?? "");
        if (backgroundSprite != null)
        {
            var backdrop = AuraToolsUi.CreateRect("Backdrop", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var backdropImage = AuraToolsUi.AddImage(backdrop, Color.white);
            backdropImage.sprite = backgroundSprite;
            backdropImage.preserveAspect = false;
            backdropImage.raycastTarget = false;
        }
        var background = AuraToolsUi.CreateRect("Background", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AuraToolsUi.AddImage(background, new Color(0.035f, 0.045f, 0.055f, backgroundSprite == null ? 1f : 0.78f)).raycastTarget = true;

        var top = AuraToolsUi.CreateRect("Top", background.transform, new Vector2(0f, 0.82f), Vector2.one, Vector2.zero, Vector2.zero);
        var topImage = AuraToolsUi.AddImage(top, new Color(0.075f, 0.085f, 0.1f, 0.98f));
        topImage.raycastTarget = false;
        var title = AuraToolsUi.AddTmpFillText(top.transform,
            "AURA REPLAY  |  " + document.Header.LevelId,
            25f,
            TextAnchor.UpperLeft,
            Color.white);
        title.margin = new Vector4(24f, 14f, 220f, 72f);
        statusText = AuraToolsUi.AddTmpFillText(top.transform, "", 17f, TextAnchor.LowerLeft, new Color(0.72f, 0.78f, 0.82f));
        statusText.margin = new Vector4(24f, 60f, 220f, 12f);
        if (interactive)
        {
            var exit = AuraToolsUi.AddButton(top.transform, "退出回放", ReplaySceneRuntime.Stop, 118f, 42f);
            Anchor(exit.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -16f));
            ((RectTransform)exit.transform).sizeDelta = new Vector2(118f, 42f);
        }

        enemyRoot = BuildBand(background.transform, "Enemies", new Vector2(0.04f, 0.58f), new Vector2(0.96f, 0.79f));
        cueText = AuraToolsUi.AddTmpFillText(background.transform, "准备回放", 26f, TextAnchor.MiddleCenter, new Color(0.95f, 0.82f, 0.4f));
        var cueRect = (RectTransform)cueText.transform;
        cueRect.anchorMin = new Vector2(0.12f, 0.49f);
        cueRect.anchorMax = new Vector2(0.88f, 0.58f);
        cueRect.offsetMin = Vector2.zero;
        cueRect.offsetMax = Vector2.zero;
        friendlyRoot = BuildBand(background.transform, "Friendly", new Vector2(0.04f, 0.31f), new Vector2(0.96f, 0.49f));
        intentRoot = BuildBand(background.transform, "Intents", new Vector2(0.04f, 0.23f), new Vector2(0.96f, 0.30f));
        cardRoot = BuildBand(background.transform, "Cards", new Vector2(0.04f, interactive ? 0.09f : 0.03f), new Vector2(0.96f, 0.22f));

        if (interactive) BuildControls(background.transform);
        if (!includeHud)
        {
            top.SetActive(false);
            intentRoot.gameObject.SetActive(false);
        }
    }

    private void BuildControls(Transform parent)
    {
        var controls = AuraToolsUi.CreateRect("Controls", parent, Vector2.zero, new Vector2(1f, 0.085f), Vector2.zero, Vector2.zero);
        AuraToolsUi.AddImage(controls, new Color(0.055f, 0.065f, 0.08f, 0.99f));
        var layout = controls.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 10, 10);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        AuraToolsUi.AddButton(controls.transform, "上一回合", () => { Controller.SeekTurn(-1); RenderNow(); }, 104f, 40f);
        AuraToolsUi.AddButton(controls.transform, "上个动作", () => { Controller.SeekAction(-1); RenderNow(); }, 96f, 40f);
        playButton = AuraToolsUi.AddButton(controls.transform, "暂停", () => { paused = !paused; RenderNow(); }, 82f, 40f);
        playLabel = playButton.GetComponentInChildren<TextMeshProUGUI>();
        AuraToolsUi.AddButton(controls.transform, "下个动作", () => { Controller.SeekAction(1); RenderNow(); }, 96f, 40f);
        AuraToolsUi.AddButton(controls.transform, "下一回合", () => { Controller.SeekTurn(1); RenderNow(); }, 104f, 40f);
        var speedButton = AuraToolsUi.AddButton(controls.transform, "1x", CycleSpeed, 64f, 40f);
        speedLabel = speedButton.GetComponentInChildren<TextMeshProUGUI>();
        var sliderRoot = AuraToolsUi.CreateLayout("Progress", controls.transform);
        AuraToolsUi.SetFixedSize(sliderRoot, 420f, 40f);
        progress = sliderRoot.AddComponent<Slider>();
        progress.minValue = 0f;
        progress.maxValue = 1f;
        progress.onValueChanged.AddListener(value =>
        {
            Controller.SeekTime((long)(Controller.DurationTicks * Math.Max(0f, Math.Min(1f, value))));
            RenderNow();
        });
        var fill = AuraToolsUi.CreateRect("Fill", sliderRoot.transform, new Vector2(0f, 0.36f), new Vector2(1f, 0.64f), Vector2.zero, Vector2.zero);
        AuraToolsUi.AddImage(fill, new Color(0.25f, 0.56f, 0.62f, 1f));
        progress.fillRect = (RectTransform)fill.transform;
    }

    private Transform BuildBand(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var band = AuraToolsUi.CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
        var layout = band.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        return band.transform;
    }

    private void RebuildState(ReplayLogicalStateV10 state)
    {
        foreach (var actor in actors) actor.Dispose();
        foreach (var card in cards) card.Dispose();
        foreach (var intent in intents) if (intent != null) Object.Destroy(intent);
        actors.Clear();
        cards.Clear();
        intents.Clear();
        foreach (var actor in state.Actors.OrderBy(item => item.SlotIndex))
        {
            var parent = actor.Team == ReplayTeamsV10.Enemy ? enemyRoot : friendlyRoot;
            actors.Add(ReplayActorView.Create(parent, actor, Definition(actor.Content), LoadSprite));
        }

        foreach (var card in state.Cards.Where(item => item.Zone == "Hand").OrderBy(item => item.Order).Take(12))
        {
            cards.Add(ReplayCardView.Create(cardRoot, card, Definition(card.Content), LoadSprite));
        }

        foreach (var intent in state.Intents.OrderBy(item => item.ActorId).ThenBy(item => item.SlotIndex))
        {
            var definition = Definition(intent.Content);
            var root = AuraToolsUi.CreateLayout("Intent-" + intent.InstanceId, intentRoot);
            AuraToolsUi.SetFixedSize(root, 170f, 42f);
            AuraToolsUi.AddImage(root, new Color(0.13f, 0.105f, 0.12f, 0.98f));
            AuraToolsUi.AddTmpFillText(root.transform,
                (definition?.Display.Name ?? intent.Content.StableContentId) + "  " + intent.DisplayValue,
                15f,
                TextAnchor.MiddleCenter,
                new Color(0.95f, 0.76f, 0.58f));
            intents.Add(root);
        }
    }

    private ReplayContentDefinitionV10? Definition(ReplayContentRefV10 reference)
    {
        return reference == null ? null : content.TryGetValue(reference.Key, out var value) ? value : null;
    }

    private Sprite? LoadSprite(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        var path = MatchRecordStorage.Database.ResolveReplayAsset(hash);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!LoadTextureBytes(texture, File.ReadAllBytes(path)))
            {
                Object.Destroy(texture);
                return null;
            }
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch
        {
            return null;
        }
    }

    private static bool LoadTextureBytes(Texture2D texture, byte[] payload)
    {
        var method = typeof(ImageConversion).GetMethod(
            "LoadImage",
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        return method?.Invoke(null, new object[] { texture, payload, false }) is true;
    }

    private string CueText(ReplayTimelineEventV10 value)
    {
        var cue = value.Presentation.FirstOrDefault();
        return cue == null
            ? value.EventType
            : string.IsNullOrWhiteSpace(cue.Label) ? cue.Kind : cue.Label;
    }

    private int CountCompletedActions(int eventCount)
    {
        return document.Events.Take(Math.Max(0, Math.Min(eventCount, document.Events.Count)))
            .Count(item => item.EventType == ReplayEventTypesV10.ActionCompleted);
    }

    private void CycleSpeed()
    {
        speed = speed < 1f ? 1f : speed < 2f ? 2f : 0.5f;
        RenderNow();
    }

    private static string FormatTime(long ticks)
    {
        var seconds = ticks / (double)ReplayProtocolV10.TimebaseTicksPerSecond;
        return TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(seconds >= 3600d ? @"h\:mm\:ss" : @"m\:ss");
    }

    private static void Anchor(GameObject value, Vector2 min, Vector2 max, Vector2 pivot, Vector2 anchored)
    {
        var rect = (RectTransform)value.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = anchored;
    }

    private static void SetLayerRecursively(GameObject value, int layer)
    {
        value.layer = layer;
        foreach (Transform child in value.transform) SetLayerRecursively(child.gameObject, layer);
    }

    internal void TickInteractive()
    {
        if (disposed || !interactive || paused) return;
        Controller.Advance((long)(Time.unscaledDeltaTime * speed * ReplayProtocolV10.TimebaseTicksPerSecond));
        RenderNow();
    }
}

internal sealed class ReplaySceneDriver : MonoBehaviour
{
    internal ReplaySceneInstance? Owner { get; set; }

    private void Update()
    {
        Owner?.TickInteractive();
    }
}

internal sealed class ReplayActorView : IDisposable
{
    private readonly GameObject root;
    private readonly Sprite? sprite;

    private ReplayActorView(GameObject root, Sprite? sprite)
    {
        this.root = root;
        this.sprite = sprite;
    }

    internal static ReplayActorView Create(
        Transform parent,
        ReplayActorStateV10 state,
        ReplayContentDefinitionV10? definition,
        Func<string, Sprite?> load)
    {
        var root = AuraToolsUi.CreateLayout("Actor-" + state.InstanceId, parent);
        AuraToolsUi.SetFixedSize(root, 250f, 116f);
        AuraToolsUi.AddPanelImage(root, new Color(0.085f, 0.095f, 0.11f, 0.98f));
        var display = definition?.Display ?? new ReplayDisplaySnapshotV10();
        var sprite = load(ReplayFact(display));
        if (sprite != null)
        {
            var portrait = AuraToolsUi.CreateRect("Portrait", root.transform, new Vector2(0f, 0f), new Vector2(0.34f, 1f), Vector2.zero, Vector2.zero);
            var image = AuraToolsUi.AddImage(portrait, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
        }
        var name = AuraToolsUi.AddTmpFillText(root.transform,
            string.IsNullOrWhiteSpace(display.Name) ? state.Content.StableContentId : display.Name,
            18f,
            TextAnchor.UpperLeft,
            Color.white);
        name.margin = new Vector4(92f, 12f, 10f, 55f);
        var details = AuraToolsUi.AddTmpFillText(root.transform,
            "HP " + state.CurrentHp + "/" + state.MaxHp + "   DEF " + state.Defense
            + (state.Buffs.Count == 0 ? "" : "\n" + string.Join("  ", state.Buffs.Take(3).Select(item => item.Content.StableContentId + " " + item.Level))),
            15f,
            TextAnchor.LowerLeft,
            new Color(0.72f, 0.82f, 0.86f));
        details.margin = new Vector4(92f, 46f, 10f, 10f);
        return new ReplayActorView(root, sprite);
    }

    public void Dispose()
    {
        if (sprite != null)
        {
            var texture = sprite.texture;
            Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
        }
        if (root != null) Object.Destroy(root);
    }

    private static string ReplayFact(ReplayDisplaySnapshotV10 display)
    {
        return !string.IsNullOrWhiteSpace(display.PortraitAssetSha256)
            ? display.PortraitAssetSha256
            : !string.IsNullOrWhiteSpace(display.ArtworkAssetSha256)
                ? display.ArtworkAssetSha256
                : display.IconAssetSha256;
    }
}

internal sealed class ReplayCardView : IDisposable
{
    private readonly GameObject root;
    private readonly Sprite? sprite;

    private ReplayCardView(GameObject root, Sprite? sprite)
    {
        this.root = root;
        this.sprite = sprite;
    }

    internal static ReplayCardView Create(
        Transform parent,
        ReplayCardStateV10 state,
        ReplayContentDefinitionV10? definition,
        Func<string, Sprite?> load)
    {
        var root = AuraToolsUi.CreateLayout("Card-" + state.InstanceId, parent);
        AuraToolsUi.SetFixedSize(root, 146f, 88f);
        AuraToolsUi.AddPanelImage(root, new Color(0.1f, 0.095f, 0.12f, 0.98f));
        var display = definition?.Display ?? new ReplayDisplaySnapshotV10();
        var hash = string.IsNullOrWhiteSpace(display.ArtworkAssetSha256) ? display.IconAssetSha256 : display.ArtworkAssetSha256;
        var sprite = load(hash);
        if (sprite != null)
        {
            var art = AuraToolsUi.CreateRect("Art", root.transform, Vector2.zero, new Vector2(0.36f, 1f), Vector2.zero, Vector2.zero);
            var image = AuraToolsUi.AddImage(art, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
        }
        var label = AuraToolsUi.AddTmpFillText(root.transform,
            (state.DisplayedCost > 0 ? state.DisplayedCost + "  " : "")
            + (string.IsNullOrWhiteSpace(display.Name) ? state.Content.StableContentId : display.Name),
            15f,
            TextAnchor.MiddleLeft,
            Color.white,
            autoSize: true);
        label.margin = new Vector4(sprite == null ? 10f : 58f, 8f, 8f, 8f);
        return new ReplayCardView(root, sprite);
    }

    public void Dispose()
    {
        if (sprite != null)
        {
            var texture = sprite.texture;
            Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
        }
        if (root != null) Object.Destroy(root);
    }
}
