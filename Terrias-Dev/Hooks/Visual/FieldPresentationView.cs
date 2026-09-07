using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

/// <summary>Owns its meshes/materials; native renderers and post-process state are never mutated.</summary>
[DefaultExecutionOrder(1000)]
public sealed class FieldPresentationView : MonoBehaviour
{
    private sealed class Theme
    {
        public TerriasFieldId Field;
        public FieldVisualSpec Spec = null!;
        public Color Primary;
        public Color Accent;
        public bool BackgroundAttempted;
        public Texture2D? Background;
        public Material? BackgroundMaterial;
        public FieldVisualMesh? BackgroundMesh;
    }

    private readonly FieldPresentationState state = new();
    private readonly List<Theme> themes = new(3);
    private readonly Vector3[] corners = new Vector3[4];
    private Func<FieldPresentationScene?>? sceneProvider;
    private Func<Material?>? materialFactory;
    private Func<string, Texture2D?>? textureLoader;
    private FieldPresentationOptions options = new();
    private FieldPresentationScene? scene;
    private GameObject? worldRoot;
    private RectTransform? uiRoot;
    private Material? material;
    private FieldVisualMesh? farMesh;
    private FieldVisualMesh? groundMesh;
    private FieldVisualMesh? frontMesh;
    private FieldVisualMesh? uiMesh;
    private float animationTime;
    private float nextGeometryTime;
    private float nextResourceAttempt;

    public void Configure(Func<FieldPresentationScene?> getScene, FieldPresentationOptions settings,
        IReadOnlyList<FieldVisualSpec> specs, Func<Material?> createMaterial, Func<string, Texture2D?> loadTexture)
    {
        ReleaseVisuals();
        state.Reset();
        sceneProvider = getScene;
        materialFactory = createMaterial;
        textureLoader = loadTexture;
        options = settings;
        themes.Clear();
        for (var i = 1; i <= 3; i++)
        {
            var field = (TerriasFieldId)i;
            foreach (var spec in specs)
            {
                if (!spec.Enabled || spec.Id != FieldVisualSpec.Slug(field)) continue;
                ColorUtility.TryParseHtmlString(spec.PrimaryColor, out var primary);
                ColorUtility.TryParseHtmlString(spec.AccentColor, out var accent);
                themes.Add(new Theme { Field = field, Spec = spec, Primary = primary, Accent = accent });
                break;
            }
        }
    }

    public void SetField(TerriasFieldId field, int stacks, int maxStacks, bool animate = true)
    {
        var supported = false;
        foreach (var theme in themes) if (theme.Field == field) supported = true;
        state.Apply(supported ? field : TerriasFieldId.None, stacks, maxStacks, animate);
        nextGeometryTime = 0f;
    }

    public void Pulse() => state.Trigger();

    private void LateUpdate()
    {
        if (sceneProvider == null) return;
        state.Advance(Time.unscaledDeltaTime, options.ReducedMotion);
        if (!options.ReducedMotion) animationTime += Mathf.Min(0.1f, Time.unscaledDeltaTime);
        var current = sceneProvider();
        if (!options.Enabled || options.Intensity <= 0f || current == null || !current.IsAlive || state.Visibility <= 0.001f)
        {
            ReleaseVisuals();
            return;
        }
        if (scene != current || worldRoot == null || uiRoot == null)
        {
            ReleaseVisuals();
            scene = current;
            if (Time.unscaledTime < nextResourceAttempt) return;
            var created = false;
            try { created = CreateVisuals(); }
            catch (Exception ex)
            {
                TerriasLog.WarnOnce("FieldPresentation.Create", "[FieldPresentation] visual setup failed: " + ex.Message);
                ReleaseVisuals();
            }
            if (!created)
            {
                nextResourceAttempt = Time.unscaledTime + 2f;
                return;
            }
        }
        if (!current.TryWorldBounds(out var bounds)) return;
        // The backdrop must follow every camera frame, including low-quality mode.
        // Only the particle/motif geometry is throttled; a delayed quad would expose
        // the native backdrop at the viewport edge while the turn camera moves.
        foreach (var theme in themes) DrawBackground(theme, bounds, state.Weight(theme.Field));
        if (Time.unscaledTime < nextGeometryTime) return;
        nextGeometryTime = Time.unscaledTime + TerriasPerformanceSettings.FieldVisualGeometryInterval(options.LowQuality, options.ReducedMotion);
        Draw(bounds);
    }

    private bool CreateVisuals()
    {
        if (scene == null) return false;
        material = materialFactory?.Invoke();
        if (material == null) return false;
        material.mainTexture = Texture2D.whiteTexture;
        worldRoot = new GameObject("Terrias_FieldEnvironment");
        worldRoot.transform.SetParent(scene.Background.transform, false);
        var go = new GameObject("Terrias_FieldUiLight", typeof(RectTransform), typeof(CanvasGroup));
        uiRoot = go.GetComponent<RectTransform>();
        uiRoot.SetParent(transform, false);
        uiRoot.anchorMin = Vector2.zero;
        uiRoot.anchorMax = Vector2.one;
        uiRoot.offsetMin = uiRoot.offsetMax = Vector2.zero;
        var group = go.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        farMesh = new FieldVisualMesh("FieldAtmosphere", worldRoot.transform, material, "background", 110, true);
        groundMesh = new FieldVisualMesh("FieldGround", worldRoot.transform, material, "middleground", 100, true);
        frontMesh = new FieldVisualMesh("FieldParticles", worldRoot.transform, material, "foreground", 100, true);
        uiMesh = new FieldVisualMesh("FieldHandBacklight", uiRoot, material, "Default", 9);
        nextGeometryTime = 0f;
        return true;
    }

    private void Draw(Rect bounds)
    {
        if (scene == null || farMesh == null || groundMesh == null || frontMesh == null || uiMesh == null) return;
        farMesh.Clear();
        groundMesh.Clear();
        frontMesh.Clear();
        uiMesh.Clear();
        foreach (var theme in themes)
        {
            var weight = state.Weight(theme.Field);
            if (weight <= 0.001f) continue;
            var strength = weight * options.Intensity * state.Strength(theme.Field);
            var pulse = theme.Field == state.Field ? state.Pulse : 0f;
            var ground = new Vector2(bounds.center.x, scene.GroundY - 0.03f);
            farMesh.Glow(new Vector2(bounds.center.x, bounds.yMax), bounds.width * 0.8f, bounds.height * 0.8f,
                Alpha(theme.Primary, strength * (0.1f + pulse * 0.06f)));
            groundMesh.Glow(ground, bounds.width * 0.48f, 0.35f,
                Alpha(theme.Primary, strength * (0.2f + pulse * 0.16f)));
            DrawGround(theme, ground, strength, pulse, bounds.width);
            DrawParticles(theme, bounds, strength);
            DrawUi(theme, strength, pulse);
        }
        farMesh.Commit();
        groundMesh.Commit();
        frontMesh.Commit();
        uiMesh.Commit();
        TerriasPerformanceCounters.Record("FieldVisual.GeometryFrame");
    }

    private void DrawBackground(Theme theme, Rect bounds, float weight)
    {
        if (!options.BackgroundsEnabled)
        {
            theme.BackgroundMesh?.Clear();
            theme.BackgroundMesh?.Commit();
            return;
        }
        if (material == null || worldRoot == null) return;
        if (weight > 0.001f && !theme.BackgroundAttempted)
        {
            theme.BackgroundAttempted = true;
            if (!string.IsNullOrWhiteSpace(theme.Spec.BackgroundPath))
                theme.Background = textureLoader?.Invoke(theme.Spec.BackgroundPath);
        }
        if (theme.Background != null && theme.BackgroundMesh == null)
        {
            theme.BackgroundMaterial = new Material(material) { mainTexture = theme.Background };
            theme.BackgroundMesh = new FieldVisualMesh("FieldBackdrop_" + theme.Spec.Id, worldRoot.transform,
                theme.BackgroundMaterial, "background", 100 + (int)theme.Field, true);
        }
        if (theme.BackgroundMesh == null || theme.Background == null) return;
        theme.BackgroundMesh.Clear();
        if (weight > 0.001f)
        {
            var textureAspect = (float)theme.Background.width / theme.Background.height;
            var viewAspect = bounds.width / bounds.height;
            var uv = textureAspect > viewAspect
                ? new Rect((1f - viewAspect / textureAspect) * 0.5f, 0f, viewAspect / textureAspect, 1f)
                : new Rect(0f, (1f - textureAspect / viewAspect) * 0.5f, 1f, textureAspect / viewAspect);
            var color = Alpha(Color.white, weight * theme.Spec.BackgroundOpacity);
            theme.BackgroundMesh.Quad(bounds, color, color, uv);
        }
        theme.BackgroundMesh.Commit();
    }

    private void DrawGround(Theme theme, Vector2 ground, float strength, float pulse, float width)
    {
        if (groundMesh == null) return;
        var segments = options.LowQuality ? 24 : 48;
        var time = options.ReducedMotion ? 0f : animationTime;
        if (theme.Field == TerriasFieldId.ScorchingCanopy)
        {
            for (var i = 0; i < 9; i++)
            {
                var x = ground.x - width * 0.4f + width * 0.1f * i;
                var breathing = 0.8f + 0.2f * Mathf.Sin(time * 1.1f + i);
                groundMesh.Glow(new Vector2(x, ground.y), 0.7f, 0.1f,
                    Alpha(theme.Accent, strength * (0.17f + pulse * 0.22f) * breathing), 12);
            }
            return;
        }
        for (var i = 0; i < 3; i++)
        {
            var phase = options.ReducedMotion ? i / 3f : Mathf.Repeat(time * 0.07f + i / 3f, 1f);
            var radius = width * (0.13f + 0.24f * phase + pulse * 0.02f);
            groundMesh.Ring(ground, radius, 0.1f + phase * 0.2f, 0.018f,
                Alpha(theme.Primary, strength * (1f - phase) * (0.2f + pulse * 0.22f)), segments);
        }
        if (theme.Field != TerriasFieldId.SamsaraGarden) return;
        for (var i = 0; i < 12; i++)
        {
            var x = ground.x - width * 0.4f + width * 0.8f * i / 11f;
            groundMesh.Petal(new Vector2(x, ground.y - 0.03f), 0.11f + pulse * 0.03f,
                i * 2.4f, Alpha(theme.Accent, strength * (0.3f + pulse * 0.15f)));
        }
    }

    private void DrawParticles(Theme theme, Rect bounds, float strength)
    {
        if (frontMesh == null) return;
        var count = Math.Min(theme.Spec.ParticleCount,
            options.ReducedMotion ? 6 : TerriasPerformanceSettings.FieldVisualParticleBudget(options.LowQuality));
        for (var i = 0; i < count; i++)
        {
            var seed = Mathf.Repeat(i * 0.618033989f + (int)theme.Field * 0.127f, 1f);
            var speed = theme.Field == TerriasFieldId.ScorchingCanopy ? 0.07f : 0.026f;
            var phase = Mathf.Repeat(i * 0.381966f + (options.ReducedMotion ? 0f : animationTime * speed), 1f);
            var y = theme.Field == TerriasFieldId.SamsaraGarden ? 1f - phase : phase;
            // Most motion stays at the edges; sparse center particles remain behind HUDs.
            var edgeX = i % 3 == 0 ? seed : (i % 2 == 0 ? seed * 0.19f : 0.81f + seed * 0.19f);
            var x = Mathf.Lerp(bounds.xMin, bounds.xMax, edgeX) + 0.13f * Mathf.Sin(animationTime * 0.35f + i);
            var position = new Vector2(x, Mathf.Lerp(bounds.yMin, bounds.yMax, 0.13f + y * 0.79f));
            var fade = Mathf.Sin(phase * Mathf.PI) * strength;
            var color = Alpha(theme.Accent, fade * 0.35f);
            var size = 0.025f + seed * 0.035f;
            if (theme.Field == TerriasFieldId.SamsaraGarden)
                frontMesh.Petal(position, size * 1.5f, i + animationTime * 0.22f, color);
            else
            {
                frontMesh.Quad(new Rect(position.x, position.y, size, size), color, color);
                if (theme.Field == TerriasFieldId.MoonDomain && i % 4 == 0)
                    frontMesh.Glow(position, size * 3f, size * 3f, Alpha(color, fade * 0.1f), 8);
            }
        }
    }

    private void DrawUi(Theme theme, float strength, float pulse)
    {
        if (scene == null || uiRoot == null || uiMesh == null) return;
        var hand = LocalRect(scene.Hand);
        var center = new Vector2(hand.center.x, hand.yMin + Mathf.Min(40f, hand.height * 0.2f));
        uiMesh.Glow(center, Mathf.Max(180f, hand.width * 0.55f), 105f,
            Alpha(theme.Primary, strength * (0.16f + pulse * 0.12f)), 32);
        DrawUiAccent(scene.Left, theme.Primary, strength);
        DrawUiAccent(scene.Clock, theme.Primary, strength);
    }

    private void DrawUiAccent(RectTransform? rect, Color color, float strength)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy || uiMesh == null) return;
        var area = LocalRect(rect);
        uiMesh.Glow(area.center, Mathf.Clamp(area.width * 0.6f, 60f, 170f),
            Mathf.Clamp(area.height * 0.5f, 45f, 120f), Alpha(color, strength * 0.11f), 24);
    }

    private Rect LocalRect(RectTransform? target)
    {
        if (target == null || uiRoot == null) return new Rect(-500f, -480f, 1000f, 160f);
        target.GetWorldCorners(corners);
        var min = uiRoot.InverseTransformPoint(corners[0]);
        var max = uiRoot.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Color Alpha(Color color, float alpha) => new(color.r, color.g, color.b, Mathf.Clamp01(alpha));

    private void OnDisable() => ReleaseVisuals();
    private void OnDestroy() => ReleaseVisuals();

    private void ReleaseVisuals()
    {
        farMesh?.Dispose();
        groundMesh?.Dispose();
        frontMesh?.Dispose();
        uiMesh?.Dispose();
        farMesh = groundMesh = frontMesh = uiMesh = null;
        foreach (var theme in themes)
        {
            theme.BackgroundMesh?.Dispose();
            theme.BackgroundMesh = null;
            if (theme.BackgroundMaterial != null) Destroy(theme.BackgroundMaterial);
            theme.BackgroundMaterial = null;
        }
        if (worldRoot != null) { worldRoot.SetActive(false); Destroy(worldRoot); }
        if (uiRoot != null) { uiRoot.gameObject.SetActive(false); Destroy(uiRoot.gameObject); }
        worldRoot = null;
        uiRoot = null;
        if (material != null) Destroy(material);
        material = null;
        scene = null;
    }
}
