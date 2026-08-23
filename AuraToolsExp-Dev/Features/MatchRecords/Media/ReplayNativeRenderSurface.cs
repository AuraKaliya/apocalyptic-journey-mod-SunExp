using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.Settings;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayNativeRenderSurface : IDisposable
{
    private readonly Camera camera;
    private readonly RenderTexture? originalTarget;
    private readonly List<CanvasState> canvases = new();
    private readonly ReplayNativeCaptureVisibility visibility;

    internal ReplayNativeRenderSurface(RenderTexture target, bool includeBattleHud)
    {
        camera = Camera.main ?? Object.FindAnyObjectByType<Camera>()
            ?? throw new InvalidOperationException("原生战斗相机不可用。");
        originalTarget = camera.targetTexture;
        camera.targetTexture = target;
        visibility = new ReplayNativeCaptureVisibility(includeBattleHud);
        var plane = 1f;
        foreach (var canvas in Object.FindObjectsByType<Canvas>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None)
                 .Where(value => value != null && value.isRootCanvas && value.gameObject.activeInHierarchy))
        {
            canvases.Add(new CanvasState(canvas));
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = plane;
                plane += 0.01f;
            }
            else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
            {
                canvas.worldCamera = camera;
            }
        }
    }

    internal void Render()
    {
        visibility.HideForCapture();
        try
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
        }
        finally
        {
            visibility.RestoreAfterCapture();
        }
    }

    public void Dispose()
    {
        visibility.Dispose();
        foreach (var state in canvases) state.Restore();
        canvases.Clear();
        if (camera != null) camera.targetTexture = originalTarget;
    }

    private sealed class CanvasState
    {
        private readonly Canvas canvas;
        private readonly RenderMode renderMode;
        private readonly Camera? worldCamera;
        private readonly float planeDistance;

        internal CanvasState(Canvas canvas)
        {
            this.canvas = canvas;
            renderMode = canvas.renderMode;
            worldCamera = canvas.worldCamera;
            planeDistance = canvas.planeDistance;
        }

        internal void Restore()
        {
            if (canvas == null) return;
            canvas.renderMode = renderMode;
            canvas.worldCamera = worldCamera;
            canvas.planeDistance = planeDistance;
        }
    }
}

internal sealed class ReplayNativeCaptureVisibility : IDisposable
{
    private readonly List<CanvasGroupState> groups = new();
    private bool hidden;

    internal ReplayNativeCaptureVisibility(bool includeBattleHud)
    {
        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        var nativeRoots = (UIManager.Instance?.GetAllUI() ?? Array.Empty<UIBase>())
            .Where(item => item != null && item.gameObject != null && item.gameObject.activeInHierarchy)
            .Select(item => item.gameObject);
        var toolRoots = Object.FindObjectsByType<AuraToolsOwnedOverlay>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(item => item != null && item.gameObject != null && item.gameObject.activeInHierarchy)
            .Select(item => item.gameObject);
        var replayControls = new[]
            {
                GameObject.Find("AuraToolsReplayExportControls"),
                GameObject.Find("AuraToolsMatchReplayControls")
            }
            .Where(value => value != null)
            .Select(value => value!);
        foreach (var root in nativeRoots.Concat(toolRoots).Concat(replayControls)
                     .Where(root => root != null)
                     .Distinct()
                     .Where(root => includeBattleHud && fightUi != null && root == fightUi.gameObject
                         ? false
                         : true))
        {
            var group = root.GetComponent<CanvasGroup>();
            var added = group == null;
            group ??= root.AddComponent<CanvasGroup>();
            groups.Add(new CanvasGroupState(group, added));
        }
    }

    internal void HideForCapture()
    {
        if (hidden) return;
        hidden = true;
        foreach (var state in groups) state.Hide();
    }

    internal void RestoreAfterCapture()
    {
        if (!hidden) return;
        foreach (var state in groups) state.Restore();
        hidden = false;
    }

    public void Dispose()
    {
        RestoreAfterCapture();
        foreach (var state in groups) state.Dispose();
        groups.Clear();
    }

    private sealed class CanvasGroupState : IDisposable
    {
        private readonly CanvasGroup group;
        private readonly bool added;
        private readonly float alpha;
        private readonly bool interactable;
        private readonly bool blocksRaycasts;

        internal CanvasGroupState(CanvasGroup group, bool added)
        {
            this.group = group;
            this.added = added;
            alpha = group.alpha;
            interactable = group.interactable;
            blocksRaycasts = group.blocksRaycasts;
        }

        internal void Hide()
        {
            if (group == null) return;
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        internal void Restore()
        {
            if (group == null) return;
            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = blocksRaycasts;
        }

        public void Dispose()
        {
            Restore();
            if (added && group != null) Object.Destroy(group);
        }
    }
}
