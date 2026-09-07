using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UiRaycastSafetyShared;

namespace AuraCg.Shared;


internal sealed class AuraCgOverlayPresenter
{
    private const float SlideDurationSeconds = 2.0f;
    private const int OverlaySortingOrder = 32760;
    private const string MaskedInvertShaderName = "AuraCg/MaskedInvertFlash";
    private const string LumaKeyShaderName = "AuraCg/LumaKeyUI";
    private const string ScreenBwFlashShaderName = "AuraCg/ScreenBwFlash";
    private readonly MonoBehaviour coroutineOwner;
    private readonly Func<string, Material?> registeredMaterialResolver;
    private GameObject? overlayRoot;
    private Canvas? overlayCanvas;
    private CanvasGroup? overlayGroup;
    private Image? overlayImage;
    private GameObject? sceneRoot;
    private AuraCgSceneCompositionRenderer? sceneRenderer;
    private IReadOnlyList<AuraCgSceneLayerPresentation> activeSceneLayers = Array.Empty<AuraCgSceneLayerPresentation>();
    private Image? overlayFlash;
    private Image? overlayScreenFlash;
    private Sprite? screenFlashSprite;
    private Material? lumaKeyMaterial;
    private bool lumaKeyMaterialResolved;
    private Material? maskedInvertMaterial;
    private bool maskedInvertMaterialResolved;
    private Material? screenBwFlashMaterial;
    private bool screenBwFlashMaterialResolved;

    public AuraCgOverlayPresenter(
        MonoBehaviour coroutineOwner,
        Func<string, Material?> registeredMaterialResolver)
    {
        this.coroutineOwner = coroutineOwner ?? throw new ArgumentNullException(nameof(coroutineOwner));
        this.registeredMaterialResolver = registeredMaterialResolver ?? throw new ArgumentNullException(nameof(registeredMaterialResolver));
    }

    public bool ShowImage(Sprite sprite, SkillCgRequest request)
    {
        if (!EnsureOverlay())
        {
            return false;
        }

        ActivateRoot();
        HideSceneLayers();
        overlayImage!.sprite = sprite;
        overlayImage.material = ResolveLumaKeyMaterial(request);
        overlayImage.raycastTarget = false;
        overlayImage.enabled = true;
        ResetGroup();
        return true;
    }

    public bool ShowSequence(IReadOnlyList<Sprite> sprites, SkillCgRequest request)
    {
        if (sprites == null || sprites.Count == 0 || !EnsureOverlay())
        {
            return false;
        }

        ActivateRoot();
        HideSceneLayers();
        overlayImage!.sprite = sprites[0];
        overlayImage.material = ResolveLumaKeyMaterial(request);
        overlayImage.raycastTarget = false;
        overlayImage.enabled = true;
        ResetGroup();
        DisableMaskedFlash();
        DisableScreenFlash();
        ConfigureFullscreenImage(sprites[0], request);
        return true;
    }

    public bool ShowScene(
        AuraCgScenePresentation presentation,
        SkillCgRequest request)
    {
        var layers = presentation.Participants;
        if (layers == null
            || layers.Count == 0
            || request.ScenePlan == null
            || !EnsureOverlay())
        {
            return false;
        }

        ActivateRoot();
        overlayImage!.sprite = null;
        overlayImage.material = null;
        overlayImage.raycastTarget = false;
        overlayImage.enabled = false;
        activeSceneLayers = layers
            .Where(layer => layer != null && layer.Frames != null && layer.Frames.Count > 0)
            .OrderBy(layer => layer.Plan.ZIndex)
            .ThenBy(layer => layer.Plan.SeatIndex)
            .ToList();
        if (activeSceneLayers.Count == 0)
        {
            return false;
        }

        if (sceneRenderer == null
            || !sceneRenderer.Bind(presentation, request.ScenePlan, request.FadeIn + request.Hold + request.FadeOut))
        {
            return false;
        }

        ResetGroup();
        DisableMaskedFlash();
        DisableScreenFlash();
        return true;
    }

    public IEnumerator PlayImage(Sprite sprite, SkillCgRequest request, Func<bool> isCurrent)
    {
        if (string.Equals(request.PresentationMode, SkillCgPresentationModes.FullscreenFade, StringComparison.OrdinalIgnoreCase))
        {
            yield return FullscreenFade(sprite, request, isCurrent);
            yield break;
        }

        if (string.Equals(request.PresentationMode, SkillCgPresentationModes.CenterFade, StringComparison.OrdinalIgnoreCase))
        {
            yield return CenterFade(sprite, request, isCurrent);
            yield break;
        }

        yield return SlideRightToLeft(sprite, isCurrent);
    }

    public IEnumerator PlaySequence(
        IReadOnlyList<Sprite> sprites,
        SkillCgRequest request,
        Func<bool> isCurrent,
        Func<Sprite, Sprite> createInvertedSprite)
    {
        yield return Fade(0f, 1f, request.FadeIn, isCurrent);
        yield return PlaySequenceFrames(sprites, request, isCurrent, createInvertedSprite);
        DisableMaskedFlash();
        DisableScreenFlash();
        yield return Wait(request.Hold, isCurrent);
        yield return Fade(1f, 0f, request.FadeOut, isCurrent);
    }

    public IEnumerator PlayScene(SkillCgRequest request, Func<bool> isCurrent)
    {
        if (overlayGroup == null || activeSceneLayers.Count == 0)
        {
            yield break;
        }

        var fadeIn = Mathf.Max(0f, request.FadeIn);
        var hold = Mathf.Max(0f, request.Hold);
        var fadeOut = Mathf.Max(0f, request.FadeOut);
        var total = Mathf.Max(0.01f, fadeIn + hold + fadeOut);
        var elapsed = 0f;
        while (isCurrent() && elapsed < total)
        {
            sceneRenderer?.UpdateFrames(elapsed);
            overlayGroup.alpha = SceneAlpha(elapsed, fadeIn, hold, fadeOut);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (isCurrent())
        {
            sceneRenderer?.UpdateFrames(total);
            overlayGroup.alpha = 0f;
        }
    }

    public bool ShouldApplyCpuAlphaMode(string alphaMode)
    {
        if (!string.Equals(SkillCgAlphaModes.Normalize(alphaMode), SkillCgAlphaModes.BlackKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ResolveLumaKeyMaterial(new SkillCgRequest { AlphaMode = alphaMode }) == null;
    }

    public void Hide()
    {
        if (overlayImage != null)
        {
            overlayImage.raycastTarget = false;
            overlayImage.enabled = false;
            overlayImage.sprite = null;
            overlayImage.material = null;
        }

        DisableMaskedFlash();
        DisableScreenFlash();
        HideSceneLayers();

        if (overlayGroup != null)
        {
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;
        }

        if (overlayRoot != null)
        {
            UiRaycastSafeDestroyRuntime.DisableAndHide(overlayRoot, "Aura CG hide overlay", AuraCgLog.DebugLog);
        }
    }

    public void Destroy()
    {
        if (overlayRoot != null)
        {
            UiRaycastSafeDestroyRuntime.DisableAndDestroyAfterFrame(
                coroutineOwner,
                overlayRoot,
                "Aura CG destroy overlay",
                AuraCgLog.DebugLog);
        }

        overlayRoot = null;
        overlayCanvas = null;
        overlayGroup = null;
        overlayImage = null;
        sceneRenderer?.Dispose();
        sceneRenderer = null;
        sceneRoot = null;
        activeSceneLayers = Array.Empty<AuraCgSceneLayerPresentation>();
        overlayFlash = null;
        overlayScreenFlash = null;
        DestroyRuntimeResources();
    }

    private bool EnsureOverlay()
    {
        if (overlayRoot != null
            && overlayCanvas != null
            && overlayGroup != null
            && overlayImage != null
            && sceneRoot != null
            && sceneRenderer != null
            && overlayFlash != null
            && overlayScreenFlash != null)
        {
            return true;
        }

        if (overlayRoot != null || overlayCanvas != null || overlayGroup != null || overlayImage != null || sceneRoot != null || sceneRenderer != null || overlayFlash != null || overlayScreenFlash != null)
        {
            Destroy();
        }

        overlayRoot = new GameObject("AuraCg.OverlayRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        UnityEngine.Object.DontDestroyOnLoad(overlayRoot);
        var rect = overlayRoot.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        overlayCanvas = overlayRoot.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = OverlaySortingOrder;

        overlayGroup = overlayRoot.GetComponent<CanvasGroup>();
        ResetGroup();

        overlayImage = CreateImage("AuraCg.Image", Color.white, preserveAspect: true);
        sceneRoot = new GameObject("AuraCg.SceneLayers", typeof(RectTransform));
        sceneRoot.transform.SetParent(overlayRoot.transform, false);
        var sceneRect = sceneRoot.GetComponent<RectTransform>();
        sceneRect.anchorMin = Vector2.zero;
        sceneRect.anchorMax = Vector2.one;
        sceneRect.offsetMin = Vector2.zero;
        sceneRect.offsetMax = Vector2.zero;
        sceneRenderer = new AuraCgSceneCompositionRenderer(sceneRoot.transform, "AuraCg.SceneComposition");
        overlayFlash = CreateImage("AuraCg.Flash", Color.clear, preserveAspect: false);
        overlayScreenFlash = CreateImage("AuraCg.ScreenFlash", Color.clear, preserveAspect: false);
        overlayRoot.SetActive(false);
        AuraCgLog.InfoOnce("overlay-created", "CG overlay created on an independent non-interactive canvas.");
        return true;
    }

    private void HideSceneLayers()
    {
        activeSceneLayers = Array.Empty<AuraCgSceneLayerPresentation>();
        sceneRenderer?.Hide();
    }

    private static float SceneAlpha(float elapsed, float fadeIn, float hold, float fadeOut)
    {
        if (fadeIn > 0f && elapsed < fadeIn)
        {
            return Mathf.Clamp01(elapsed / fadeIn);
        }

        var fadeOutStart = fadeIn + hold;
        if (fadeOut > 0f && elapsed > fadeOutStart)
        {
            return Mathf.Clamp01(1f - (elapsed - fadeOutStart) / fadeOut);
        }

        return 1f;
    }

    private Image CreateImage(string name, Color color, bool preserveAspect)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(overlayRoot!.transform, false);
        var imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        var image = imageObject.GetComponent<Image>();
        image.color = color;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    private void ActivateRoot()
    {
        overlayRoot!.SetActive(true);
        if (overlayRoot.transform.parent != null)
        {
            overlayRoot.transform.SetAsLastSibling();
        }
    }

    private void ResetGroup()
    {
        if (overlayGroup == null)
        {
            return;
        }

        overlayGroup.alpha = 0f;
        overlayGroup.blocksRaycasts = false;
        overlayGroup.interactable = false;
    }

    private IEnumerator SlideRightToLeft(Sprite sprite, Func<bool> isCurrent)
    {
        if (overlayRoot == null || overlayGroup == null || overlayImage == null)
        {
            yield break;
        }

        var imageRect = overlayImage.rectTransform;
        overlayImage.preserveAspect = true;
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);

        var elapsed = 0f;
        while (isCurrent() && elapsed < SlideDurationSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / SlideDurationSeconds);
            var viewport = GetOverlayViewportSize();
            var xRatio = AuraCgPresentationMath.EvaluateSlideXRatio(progress);

            imageRect.sizeDelta = CalculateImageSize(sprite, viewport);
            imageRect.anchoredPosition = new Vector2((xRatio - 0.5f) * viewport.x, 0f);
            overlayGroup.alpha = AuraCgPresentationMath.EvaluateSlideAlpha(xRatio);
            yield return null;
        }

        if (isCurrent())
        {
            var viewport = GetOverlayViewportSize();
            imageRect.sizeDelta = CalculateImageSize(sprite, viewport);
            imageRect.anchoredPosition = new Vector2((AuraCgPresentationMath.SlideEndXRatio - 0.5f) * viewport.x, 0f);
            overlayGroup.alpha = 0f;
        }
    }

    private IEnumerator FullscreenFade(Sprite sprite, SkillCgRequest request, Func<bool> isCurrent)
    {
        if (overlayGroup == null || overlayImage == null)
        {
            yield break;
        }

        ConfigureFullscreenImage(sprite, request);
        yield return Fade(0f, 1f, request.FadeIn, isCurrent);
        yield return Wait(request.Hold, isCurrent);
        yield return Fade(1f, 0f, request.FadeOut, isCurrent);
    }

    private IEnumerator CenterFade(Sprite sprite, SkillCgRequest request, Func<bool> isCurrent)
    {
        if (overlayGroup == null || overlayImage == null)
        {
            yield break;
        }

        ConfigureCenteredImage(sprite);
        yield return Fade(0f, 1f, request.FadeIn, isCurrent);
        yield return Wait(request.Hold, isCurrent);
        yield return Fade(1f, 0f, request.FadeOut, isCurrent);
    }

    private void ConfigureFullscreenImage(Sprite sprite, SkillCgRequest request)
    {
        if (overlayImage != null)
        {
            ConfigureFullscreenGraphic(overlayImage, sprite, request);
        }
    }

    private void ConfigureFullscreenGraphic(Image image, Sprite sprite, SkillCgRequest request)
    {
        var imageRect = image.rectTransform;
        imageRect.pivot = new Vector2(0.5f, 0.5f);

        if (string.Equals(request.FitMode, SkillCgFitModes.Stretch, StringComparison.OrdinalIgnoreCase))
        {
            image.preserveAspect = false;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            imageRect.anchoredPosition = Vector2.zero;
            imageRect.sizeDelta = Vector2.zero;
            return;
        }

        image.preserveAspect = true;
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        var viewport = GetOverlayViewportSize();
        if (string.Equals(request.FitMode, SkillCgFitModes.Cover, StringComparison.OrdinalIgnoreCase))
        {
            var imageSize = CalculateCoverImageSize(sprite, viewport, request.SafeScale);
            imageRect.sizeDelta = imageSize;
            imageRect.anchoredPosition = CalculateCoverImageOffset(imageSize, viewport, request.FocusX, request.FocusY);
            return;
        }

        imageRect.anchoredPosition = Vector2.zero;
        imageRect.sizeDelta = viewport;
    }

    private void ConfigureCenteredImage(Sprite sprite)
    {
        if (overlayImage == null)
        {
            return;
        }

        var imageRect = overlayImage.rectTransform;
        overlayImage.preserveAspect = true;
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = Vector2.zero;
        imageRect.sizeDelta = CalculateImageSize(sprite, GetOverlayViewportSize());
    }

    private Vector2 GetOverlayViewportSize()
    {
        if (overlayRoot != null)
        {
            var rect = overlayRoot.GetComponent<RectTransform>().rect;
            if (rect.width > 1f && rect.height > 1f)
            {
                return new Vector2(rect.width, rect.height);
            }
        }

        return new Vector2(Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
    }

    private static Vector2 CalculateImageSize(Sprite sprite, Vector2 viewport)
    {
        var spriteRect = sprite.rect;
        var size = AuraCgPresentationMath.CalculateSlideImageSize(
            spriteRect.width,
            spriteRect.height,
            viewport.x,
            viewport.y);
        return new Vector2(size.X, size.Y);
    }

    private static Vector2 CalculateCoverImageSize(Sprite sprite, Vector2 viewport, float safeScale)
    {
        var spriteRect = sprite.rect;
        var size = AuraCgPresentationMath.CalculateCoverImageSize(
            spriteRect.width,
            spriteRect.height,
            viewport.x,
            viewport.y,
            safeScale);
        return new Vector2(size.X, size.Y);
    }

    private static Vector2 CalculateCoverImageOffset(Vector2 imageSize, Vector2 viewport, float focusX, float focusY)
    {
        var offset = AuraCgPresentationMath.CalculateCoverImageOffset(
            imageSize.x,
            imageSize.y,
            viewport.x,
            viewport.y,
            focusX,
            focusY);
        return new Vector2(offset.X, offset.Y);
    }

    private IEnumerator PlaySequenceFrames(
        IReadOnlyList<Sprite> sprites,
        SkillCgRequest request,
        Func<bool> isCurrent,
        Func<Sprite, Sprite> createInvertedSprite)
    {
        if (overlayImage == null)
        {
            yield break;
        }

        var frameSeconds = Mathf.Max(0.01f, request.FrameSeconds);
        var totalSeconds = Mathf.Max(frameSeconds, sprites.Count * frameSeconds);
        var elapsed = 0f;
        var lastIndex = -1;
        while (isCurrent() && elapsed < totalSeconds)
        {
            var index = Mathf.Clamp((int)(elapsed / frameSeconds), 0, sprites.Count - 1);
            if (index != lastIndex)
            {
                overlayImage.sprite = sprites[index];
                ConfigureFullscreenImage(sprites[index], request);
                lastIndex = index;
            }

            UpdateSequenceFlash(request, elapsed, index + 1, sprites[index], createInvertedSprite);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (isCurrent() && sprites.Count > 0)
        {
            overlayImage.sprite = sprites[sprites.Count - 1];
            ConfigureFullscreenImage(sprites[sprites.Count - 1], request);
            UpdateSequenceFlash(request, totalSeconds, sprites.Count, sprites[sprites.Count - 1], createInvertedSprite);
        }
    }

    private void UpdateSequenceFlash(
        SkillCgRequest request,
        float elapsed,
        int frameNumber,
        Sprite sprite,
        Func<Sprite, Sprite> createInvertedSprite)
    {
        UpdateScreenBwFlash(request, frameNumber);

        if (AuraCgPresentationPolicy.UsesMaskedFlash(request))
        {
            UpdateMaskedFlash(request, frameNumber, sprite, createInvertedSprite);
            return;
        }

        UpdateFlash(request, elapsed);
    }

    private void UpdateScreenBwFlash(SkillCgRequest request, int frameNumber)
    {
        if (overlayScreenFlash == null || !AuraCgPresentationPolicy.UsesScreenBwFlash(request))
        {
            return;
        }

        var startFrame = Mathf.Max(1, request.FlashStartFrame);
        var endFrame = Mathf.Max(startFrame, request.FlashEndFrame <= 0 ? startFrame : request.FlashEndFrame);
        if (frameNumber < startFrame || frameNumber > endFrame)
        {
            DisableScreenFlash();
            return;
        }

        var localFrame = frameNumber - startFrame;
        var baseStrength = Mathf.Clamp01(request.FlashStrength <= 0f ? 1f : request.FlashStrength);
        var pulse = AuraCgPresentationMath.ScreenBwPulse(localFrame) * baseStrength;
        if (pulse <= 0.001f)
        {
            DisableScreenFlash();
            return;
        }

        overlayScreenFlash.sprite = ScreenFlashSprite();
        overlayScreenFlash.raycastTarget = false;
        overlayScreenFlash.enabled = true;
        overlayScreenFlash.rectTransform.anchorMin = Vector2.zero;
        overlayScreenFlash.rectTransform.anchorMax = Vector2.one;
        overlayScreenFlash.rectTransform.offsetMin = Vector2.zero;
        overlayScreenFlash.rectTransform.offsetMax = Vector2.zero;

        var material = ResolveScreenBwFlashMaterial();
        if (material != null && localFrame <= 6 && localFrame % 2 == 0)
        {
            overlayScreenFlash.material = material;
            SetMaterialFloat(material, "_AuraCgFlashStrength", pulse);
            overlayScreenFlash.color = Color.white;
            return;
        }

        overlayScreenFlash.material = null;
        overlayScreenFlash.color = localFrame % 2 == 0
            ? new Color(1f, 1f, 1f, pulse * 0.86f)
            : new Color(0f, 0f, 0f, pulse * 0.72f);
    }

    private void UpdateFlash(SkillCgRequest request, float elapsed)
    {
        if (overlayFlash == null || request.FlashAtSeconds < 0f)
        {
            return;
        }

        var since = elapsed - request.FlashAtSeconds;
        if (since < 0f || since > request.FlashDuration)
        {
            overlayFlash.color = Color.clear;
            overlayFlash.enabled = false;
            return;
        }

        var alpha = Mathf.Clamp01(1f - since / Mathf.Max(0.03f, request.FlashDuration));
        overlayFlash.enabled = alpha > 0.001f;
        overlayFlash.color = new Color(1f, 0.94f, 0.72f, alpha * 0.82f);
    }

    private void UpdateMaskedFlash(
        SkillCgRequest request,
        int frameNumber,
        Sprite sprite,
        Func<Sprite, Sprite> createInvertedSprite)
    {
        if (overlayFlash == null)
        {
            return;
        }

        var startFrame = Mathf.Max(1, request.FlashStartFrame);
        var endFrame = Mathf.Max(startFrame, request.FlashEndFrame <= 0 ? startFrame : request.FlashEndFrame);
        if (frameNumber < startFrame || frameNumber > endFrame)
        {
            DisableMaskedFlash();
            return;
        }

        var pulseEvery = Mathf.Max(1, request.FlashPulseEveryFrames);
        if (pulseEvery > 1 && (frameNumber - startFrame) % pulseEvery != 0)
        {
            DisableMaskedFlash();
            return;
        }

        var strength = Mathf.Clamp01(request.FlashStrength <= 0f ? 1f : request.FlashStrength);
        var material = ResolveMaskedInvertMaterial();
        if (material != null)
        {
            overlayFlash.sprite = sprite;
            overlayFlash.material = material;
            SetMaterialFloat(material, "_AuraCgFlashStrength", strength);
            SetMaterialFloat(material, "_AuraCgKeyThreshold", request.KeyThreshold);
            SetMaterialFloat(material, "_AuraCgKeySoftness", request.KeySoftness);
            overlayFlash.color = Color.white;
        }
        else
        {
            overlayFlash.sprite = createInvertedSprite(sprite);
            overlayFlash.material = null;
            overlayFlash.color = new Color(1f, 1f, 1f, strength);
        }

        overlayFlash.raycastTarget = false;
        overlayFlash.enabled = overlayFlash.sprite != null;
        ConfigureFullscreenGraphic(overlayFlash, sprite, request);
    }

    private void DisableMaskedFlash()
    {
        if (overlayFlash == null)
        {
            return;
        }

        overlayFlash.enabled = false;
        overlayFlash.color = Color.clear;
        overlayFlash.sprite = null;
        overlayFlash.material = null;
    }

    private void DisableScreenFlash()
    {
        if (overlayScreenFlash == null)
        {
            return;
        }

        overlayScreenFlash.enabled = false;
        overlayScreenFlash.color = Color.clear;
        overlayScreenFlash.sprite = null;
        overlayScreenFlash.material = null;
    }

    private Material? ResolveMaskedInvertMaterial()
    {
        if (maskedInvertMaterialResolved)
        {
            return maskedInvertMaterial;
        }

        maskedInvertMaterialResolved = true;
        try
        {
            maskedInvertMaterial = CloneRegisteredMaterial(MaskedInvertShaderName, "AuraCg.MaskedInvertFlash.Runtime");
            if (maskedInvertMaterial != null)
            {
                return maskedInvertMaterial;
            }

            var shader = Shader.Find(MaskedInvertShaderName);
            if (shader == null)
            {
                AuraCgLog.WarnOnce(
                    "masked-invert-shader-missing",
                    "Masked invert shader is not loaded; using CPU inverted-sprite fallback. shader=" + MaskedInvertShaderName);
                return null;
            }

            maskedInvertMaterial = new Material(shader)
            {
                name = "AuraCg.MaskedInvertFlash.Runtime"
            };
            return maskedInvertMaterial;
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("masked-invert-shader-failed", "Masked invert shader setup failed: " + ex.Message);
            return null;
        }
    }

    private Material? ResolveScreenBwFlashMaterial()
    {
        if (screenBwFlashMaterialResolved)
        {
            return screenBwFlashMaterial;
        }

        screenBwFlashMaterialResolved = true;
        try
        {
            screenBwFlashMaterial = CloneRegisteredMaterial(ScreenBwFlashShaderName, "AuraCg.ScreenBwFlash.Runtime");
            if (screenBwFlashMaterial != null)
            {
                return screenBwFlashMaterial;
            }

            var shader = Shader.Find(ScreenBwFlashShaderName);
            if (shader != null)
            {
                screenBwFlashMaterial = new Material(shader)
                {
                    name = "AuraCg.ScreenBwFlash.Runtime"
                };
            }
            else
            {
                AuraCgLog.WarnOnce(
                    "screen-bw-flash-shader-missing",
                    "Screen black-white flash shader is not loaded; using color overlay fallback. shader=" + ScreenBwFlashShaderName);
            }
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("screen-bw-flash-material-failed", "Screen black-white flash material failed: " + ex.Message);
            screenBwFlashMaterial = null;
        }

        return screenBwFlashMaterial;
    }

    private Material? ResolveLumaKeyMaterial(SkillCgRequest request)
    {
        if (!string.Equals(SkillCgAlphaModes.Normalize(request.AlphaMode), SkillCgAlphaModes.BlackKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!lumaKeyMaterialResolved)
        {
            lumaKeyMaterialResolved = true;
            try
            {
                lumaKeyMaterial = CloneRegisteredMaterial(LumaKeyShaderName, "AuraCg.LumaKeyUI.Runtime");
                if (lumaKeyMaterial == null)
                {
                    var shader = Shader.Find(LumaKeyShaderName);
                    if (shader != null)
                    {
                        lumaKeyMaterial = new Material(shader)
                        {
                            name = "AuraCg.LumaKeyUI.Runtime"
                        };
                    }
                    else
                    {
                        AuraCgLog.WarnOnce(
                            "luma-key-shader-missing",
                            "Luma-key shader is not loaded; using CPU black-key fallback. shader=" + LumaKeyShaderName);
                    }
                }
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce("luma-key-shader-failed", "Luma-key shader setup failed: " + ex.Message);
            }
        }

        if (lumaKeyMaterial != null)
        {
            SetMaterialFloat(lumaKeyMaterial, "_AuraCgKeyThreshold", request.KeyThreshold);
            SetMaterialFloat(lumaKeyMaterial, "_AuraCgKeySoftness", request.KeySoftness);
        }

        return lumaKeyMaterial;
    }

    private Material? CloneRegisteredMaterial(string materialId, string runtimeName)
    {
        try
        {
            var source = registeredMaterialResolver(materialId);
            return source == null
                ? null
                : new Material(source) { name = runtimeName };
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("registered-material-clone-failed:" + materialId, "Registered CG material clone failed: " + materialId + ", error=" + ex.Message);
            return null;
        }
    }

    private static void SetMaterialFloat(Material material, string propertyName, float value)
    {
        try
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
        catch
        {
        }
    }

    private Sprite ScreenFlashSprite()
    {
        if (screenFlashSprite != null)
        {
            return screenFlashSprite;
        }

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "AuraCg.ScreenFlash.WhitePixel"
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, false);
        screenFlashSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        screenFlashSprite.name = "AuraCg.ScreenFlash.Sprite";
        return screenFlashSprite;
    }

    private void DestroyRuntimeResources()
    {
        DestroyMaterial(lumaKeyMaterial);
        DestroyMaterial(maskedInvertMaterial);
        DestroyMaterial(screenBwFlashMaterial);
        lumaKeyMaterial = null;
        lumaKeyMaterialResolved = false;
        maskedInvertMaterial = null;
        maskedInvertMaterialResolved = false;
        screenBwFlashMaterial = null;
        screenBwFlashMaterialResolved = false;

        if (screenFlashSprite != null)
        {
            Texture2D? texture = null;
            try
            {
                texture = screenFlashSprite.texture;
            }
            catch
            {
            }

            UnityEngine.Object.Destroy(screenFlashSprite);
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }

            screenFlashSprite = null;
        }
    }

    private static void DestroyMaterial(Material? material)
    {
        if (material != null)
        {
            UnityEngine.Object.Destroy(material);
        }
    }

    private IEnumerator Fade(float from, float to, float seconds, Func<bool> isCurrent)
    {
        if (overlayGroup == null)
        {
            yield break;
        }

        if (seconds <= 0f)
        {
            overlayGroup.alpha = to;
            yield break;
        }

        var elapsed = 0f;
        while (isCurrent() && elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            overlayGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }

        if (isCurrent())
        {
            overlayGroup.alpha = to;
        }
    }

    private static IEnumerator Wait(float seconds, Func<bool> isCurrent)
    {
        var elapsed = 0f;
        while (isCurrent() && elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
