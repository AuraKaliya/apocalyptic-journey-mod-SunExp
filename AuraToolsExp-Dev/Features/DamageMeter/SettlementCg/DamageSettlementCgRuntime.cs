using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;

public static class DamageSettlementCgRuntime
{
    private const string RootName = "AuraToolsDamageSettlementCgOverlay";
    private const string DriverName = "AuraToolsDamageSettlementCgDriver";
    private const int OverlaySortingOrder = 32000;
    private const float OverlayReferenceWidth = 1600f;
    private const float OverlayReferenceHeight = 900f;
    private static GameObject? root;
    private static GameObject? driverRoot;
    private static CanvasGroup? group;
    private static DamageSettlementCgDriver? driver;
    private static Coroutine? currentRoutine;
    private static int generation;

    public static void BeginAdventure()
    {
        if (IsEnabled())
        {
            DamageSettlementCgAssetCache.BeginAdventure();
        }
    }

    public static void PrepareForTeam(IEnumerable<OutOfRunTeamMemberSnapshot> teamMembers)
    {
        if (IsEnabled())
        {
            DamageSettlementCgAssetCache.PrepareForTeam(teamMembers);
        }
    }

    public static void TryPlay(OutOfRunDamageHistoryRecord record)
    {
        if (!IsEnabled())
        {
            return;
        }

        var settings = CurrentSettings();
        var payload = DamageSettlementCgBuilder.Build(record, settings);
        if (payload.Entries.Count == 0)
        {
            AuraToolsLog.Info("[SettlementCG] skipped: no ranked team members.");
            return;
        }

        DamageSettlementCgAssetCache.AttachPreparedClipKeys(payload.Entries);
        PlayPayload(payload, broadcast: true);
    }

    public static void PlayRemote(DamageSettlementCgPayload payload)
    {
        if (!IsEnabled())
        {
            return;
        }

        PlayPayload(payload, broadcast: false);
    }

    private static void PlayPayload(DamageSettlementCgPayload payload, bool broadcast)
    {
        if (payload == null || payload.ProtocolVersion != DamageMeterProtocol.Version)
        {
            return;
        }

        var settings = CurrentSettings();
        if (string.IsNullOrWhiteSpace(payload.BackgroundResource))
        {
            payload.BackgroundResource = settings.BackgroundResource;
        }

        if (payload.Entries == null || payload.Entries.Count == 0)
        {
            return;
        }

        if (broadcast && settings.SyncRemote)
        {
            Broadcast(payload);
        }

        if (!EnsureDriver())
        {
            return;
        }

        if (currentRoutine != null && driver != null)
        {
            driver.StopCoroutine(currentRoutine);
            currentRoutine = null;
            generation++;
            HideOverlay();
        }

        if (!EnsureOverlay())
        {
            return;
        }

        generation++;
        currentRoutine = driver!.StartCoroutine(PlayRoutine(payload, settings, generation));
    }

    private static IEnumerator PlayRoutine(
        DamageSettlementCgPayload payload,
        DamageSettlementCgSettings settings,
        int routineGeneration)
    {
        if (root == null || group == null)
        {
            if (routineGeneration == generation)
            {
                currentRoutine = null;
            }

            HideOverlay();
            yield break;
        }

        var background = LoadBackground(payload.BackgroundResource);
        if (background == null)
        {
            AuraToolsLog.Warn("[SettlementCG] skipped: background missing. resource=" + payload.BackgroundResource);
            if (routineGeneration == generation)
            {
                currentRoutine = null;
            }

            HideOverlay();
            yield break;
        }

        var clips = ResolveClips(payload);
        if (clips.Count == 0)
        {
            AuraToolsLog.Info("[SettlementCG] skipped: no role idle frames resolved.");
            if (routineGeneration == generation)
            {
                currentRoutine = null;
            }

            HideOverlay();
            yield break;
        }

        ConfigureOverlay(background, clips, settings);
        root.SetActive(true);
        root.transform.SetAsLastSibling();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var animation = driver!.StartCoroutine(Animate(clips, routineGeneration));
        yield return Fade(0f, 1f, settings.FadeIn, routineGeneration);
        yield return Wait(settings.Hold, routineGeneration);
        yield return Fade(1f, 0f, settings.FadeOut, routineGeneration);
        if (driver != null)
        {
            driver.StopCoroutine(animation);
        }

        if (routineGeneration == generation)
        {
            currentRoutine = null;
            HideOverlay();
        }
    }

    private static List<DamageSettlementCgCharacterView> ResolveClips(DamageSettlementCgPayload payload)
    {
        var views = new List<DamageSettlementCgCharacterView>();
        foreach (var entry in (payload.Entries ?? new List<DamageSettlementCgEntry>())
                     .Where(entry => entry != null)
                     .OrderBy(entry => entry.Rank)
                     .Take(DamageSettlementCgLayout.MaxSlots))
        {
            var clip = DamageSettlementCgAssetCache.ResolvePreparedClip(entry);
            if (clip == null || clip.Frames.Count == 0)
            {
                AuraToolsLog.Warn("[SettlementCG] idle skipped: rank="
                                  + entry.Rank + ", role=" + entry.RoleId + ", reason=not preloaded.");
                continue;
            }

            views.Add(new DamageSettlementCgCharacterView
            {
                Entry = entry,
                Clip = clip
            });
        }

        return views;
    }

    private static void ConfigureOverlay(
        Sprite background,
        List<DamageSettlementCgCharacterView> views,
        DamageSettlementCgSettings settings)
    {
        if (root == null)
        {
            return;
        }

        ClearChildren(root.transform);
        var viewport = ViewportSize();
        var layout = DamageSettlementCgLayout.Calculate(viewport.x, viewport.y, settings);

        var backgroundObject = CreateTopLeftRect(
            "Background",
            root.transform,
            layout.Background.X,
            layout.Background.Y,
            layout.Background.Width,
            layout.Background.Height);
        var backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.sprite = background;
        backgroundImage.color = Color.white;
        backgroundImage.raycastTarget = false;
        backgroundImage.preserveAspect = false;

        foreach (var view in views)
        {
            var slot = layout.SlotForRank(view.Entry.Rank);
            if (slot == null)
            {
                continue;
            }

            var slotObject = CreateTopLeftRect(
                "Rank" + view.Entry.Rank,
                root.transform,
                slot.Rect.X,
                slot.Rect.Y,
                slot.Rect.Width,
                slot.Rect.Height);
            slotObject.AddComponent<RectMask2D>();
            var imageObject = new GameObject("Idle", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(slotObject.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;
            var image = imageObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.color = Color.white;
            view.SlotRect = slotObject.GetComponent<RectTransform>();
            view.Image = image;
            view.FrameIndex = 0;
            view.Elapsed = 0f;
            ApplyFrame(view);
        }
    }

    private static IEnumerator Animate(List<DamageSettlementCgCharacterView> views, int routineGeneration)
    {
        while (routineGeneration == generation)
        {
            var delta = Time.unscaledDeltaTime;
            foreach (var view in views)
            {
                if (view.Image == null || view.Clip.Frames.Count <= 1)
                {
                    continue;
                }

                view.Elapsed += delta;
                if (view.Elapsed < view.Clip.FrameSeconds)
                {
                    continue;
                }

                view.Elapsed = 0f;
                view.FrameIndex++;
                if (view.FrameIndex >= view.Clip.Frames.Count)
                {
                    view.FrameIndex = view.Clip.Loop ? 0 : view.Clip.Frames.Count - 1;
                }

                ApplyFrame(view);
            }

            yield return null;
        }
    }

    private static void ApplyFrame(DamageSettlementCgCharacterView view)
    {
        if (view.Image == null || view.SlotRect == null || view.Clip.Frames.Count == 0)
        {
            return;
        }

        var sprite = view.Clip.Frames[Mathf.Clamp(view.FrameIndex, 0, view.Clip.Frames.Count - 1)];
        view.Image.sprite = sprite;
        var imageRect = view.Image.rectTransform;
        var slot = view.SlotRect.rect;
        var spriteRect = sprite.rect;
        var width = Mathf.Max(1f, spriteRect.width);
        var height = Mathf.Max(1f, spriteRect.height);
        var scale = Mathf.Min(slot.width / width, slot.height / height);
        imageRect.sizeDelta = new Vector2(width * scale, height * scale);
        imageRect.anchoredPosition = Vector2.zero;
    }

    private static IEnumerator Fade(float from, float to, float seconds, int routineGeneration)
    {
        if (group == null)
        {
            yield break;
        }

        if (seconds <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        var elapsed = 0f;
        while (routineGeneration == generation && elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }

        if (routineGeneration == generation)
        {
            group.alpha = to;
        }
    }

    private static IEnumerator Wait(float seconds, int routineGeneration)
    {
        var elapsed = 0f;
        while (routineGeneration == generation && elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static Sprite? LoadBackground(string resource)
    {
        try
        {
            return ResourceLoader.Load<Sprite>(resource, true);
        }
        catch
        {
            return null;
        }
    }

    private static bool EnsureOverlay()
    {
        if (root != null)
        {
            return true;
        }

        root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        Object.DontDestroyOnLoad(root);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(OverlayReferenceWidth, OverlayReferenceHeight);
        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        root.SetActive(false);
        return true;
    }

    private static bool EnsureDriver()
    {
        if (driverRoot != null && driver != null)
        {
            if (!driverRoot.activeSelf)
            {
                driverRoot.SetActive(true);
            }

            return true;
        }

        if (currentRoutine != null && driver != null)
        {
            driver.StopCoroutine(currentRoutine);
            currentRoutine = null;
        }

        if (driverRoot != null)
        {
            Object.Destroy(driverRoot);
        }

        driverRoot = new GameObject(DriverName);
        Object.DontDestroyOnLoad(driverRoot);
        driverRoot.SetActive(true);
        driver = driverRoot.AddComponent<DamageSettlementCgDriver>();
        return true;
    }

    private static void HideOverlay()
    {
        if (root == null || group == null)
        {
            return;
        }

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        UiRaycastSafeDestroyRuntime.DisableAndHide(root, "AuraToolsExp:SettlementCG.Hide");
        ClearChildren(root.transform);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(4, "AuraToolsExp:SettlementCG.Hide");
        Object.Destroy(root);
        root = null;
        group = null;
    }

    private static GameObject CreateTopLeftRect(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return go;
    }

    private static Vector2 ViewportSize()
    {
        if (root != null)
        {
            var rect = root.GetComponent<RectTransform>().rect;
            if (rect.width > 1f && rect.height > 1f)
            {
                return new Vector2(rect.width, rect.height);
            }
        }

        return new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
    }

    private static void ClearChildren(Transform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(parent.GetChild(i).gameObject);
        }
    }

    private static void Broadcast(DamageSettlementCgPayload payload)
    {
        var manager = PlayerManager.Instance;
        if (manager == null || !manager.isServer)
        {
            return;
        }

        try
        {
            var command = new DamageSettlementCgCommand(payload);
            AuraToolsRpcTransport.Send(manager, command, "SettlementCG.Payload", excludeOwner: true);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SettlementCG] remote sync failed: " + ex.Message);
        }
    }

    private static bool IsEnabled()
    {
        return AuraToolsConfigService.Root.MatchExperience.Enabled
               && AuraToolsConfigService.MatchExperience.DamageMeter.Enabled
               && AuraToolsConfigService.MatchExperience.DamageMeter.SettlementCg.Enabled;
    }

    private static DamageSettlementCgSettings CurrentSettings()
    {
        var settings = AuraToolsConfigService.MatchExperience.DamageMeter.SettlementCg ?? new DamageSettlementCgSettings();
        settings.Normalize();
        return settings;
    }

    private sealed class DamageSettlementCgCharacterView
    {
        public DamageSettlementCgEntry Entry { get; set; } = new();

        public DamageSettlementCgIdleClip Clip { get; set; } = new();

        public RectTransform? SlotRect { get; set; }

        public Image? Image { get; set; }

        public int FrameIndex { get; set; }

        public float Elapsed { get; set; }
    }
}

public sealed class DamageSettlementCgDriver : MonoBehaviour
{
}

[Serializable]
public sealed class DamageSettlementCgCommand : RpcCommandBase
{
    public DamageSettlementCgCommand()
    {
        Payload = new DamageSettlementCgPayload();
    }

    public DamageSettlementCgCommand(DamageSettlementCgPayload payload)
    {
        Payload = payload ?? new DamageSettlementCgPayload();
    }

    public DamageSettlementCgPayload Payload { get; set; }

    public override void RpcExecute()
    {
        DamageSettlementCgRuntime.PlayRemote(Payload);
    }
}
