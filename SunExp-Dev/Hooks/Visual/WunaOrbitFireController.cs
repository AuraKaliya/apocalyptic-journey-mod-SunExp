using System;
using System.Collections.Generic;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public sealed class WunaOrbitFireController : MonoBehaviour
{
    private const int CoreSections = 144;
    private const int DetailTongues = 4;
    private const int DetailSparks = 5;
    private const int OrbitFlamesPerRail = 4;
    private const int FlameAtlasColumns = 16;
    private const int FlameAtlasRows = 4;
    private const int FlameAtlasFrames = FlameAtlasColumns * FlameAtlasRows;
    private const int MaxVertices = 1600;
    private const float Tau = Mathf.PI * 2f;
    private const float DefaultBoostSeconds = 0.95f;
    private const float MinBoundsSize = 0.01f;
    private const float AlphaBoundsThreshold = 0.08f;

    private static readonly WunaOrbitFireOrbitModel.OrbitRail[] Rails = WunaOrbitFireOrbitModel.Rails;
    private static readonly Vector2 OrbitBoundsFocus = new(0.5f, 0.46f);
    private static readonly Dictionary<int, Bounds?> AlphaBoundsCache = new();

    private readonly List<Vector3> vertices = new(MaxVertices);
    private readonly List<Vector2> uvs = new(MaxVertices);
    private readonly List<Vector2> localUvs = new(MaxVertices);
    private readonly List<Color> colors = new(MaxVertices);
    private readonly List<int> triangles = new(MaxVertices * 3);

    private SpriteRenderer? targetRenderer;
    private Mesh? backCoreMesh;
    private Mesh? backDetailMesh;
    private Mesh? backFlameMesh;
    private Mesh? frontCoreMesh;
    private Mesh? frontDetailMesh;
    private Mesh? frontFlameMesh;
    private MeshRenderer? backCoreRenderer;
    private MeshRenderer? backDetailRenderer;
    private MeshRenderer? backFlameRenderer;
    private MeshRenderer? frontCoreRenderer;
    private MeshRenderer? frontDetailRenderer;
    private MeshRenderer? frontFlameRenderer;
    private Material? backCoreMaterial;
    private Material? backDetailMaterial;
    private Material? backFlameMaterial;
    private Material? frontCoreMaterial;
    private Material? frontDetailMaterial;
    private Material? frontFlameMaterial;
    private float boostUntil;
    private float boostAmount;
    private float actionPulse;
    private int unreadableTextureId;

    public void Configure(SpriteRenderer renderer)
    {
        targetRenderer = renderer;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        EnsureLayer("BackCore", false, true, false, ref backCoreMesh, ref backCoreRenderer, ref backCoreMaterial);
        EnsureLayer("BackDetail", false, false, false, ref backDetailMesh, ref backDetailRenderer, ref backDetailMaterial);
        EnsureLayer("BackFlames", false, false, true, ref backFlameMesh, ref backFlameRenderer, ref backFlameMaterial);
        EnsureLayer("FrontCore", true, true, false, ref frontCoreMesh, ref frontCoreRenderer, ref frontCoreMaterial);
        EnsureLayer("FrontDetail", true, false, false, ref frontDetailMesh, ref frontDetailRenderer, ref frontDetailMaterial);
        EnsureLayer("FrontFlames", true, false, true, ref frontFlameMesh, ref frontFlameRenderer, ref frontFlameMaterial);
        SyncSorting();
    }

    public void BoostForAction(string action)
    {
        var normalized = (action ?? "").Trim();
        boostAmount = string.Equals(normalized, "Skill", StringComparison.OrdinalIgnoreCase)
            ? 1f
            : string.Equals(normalized, "Attack", StringComparison.OrdinalIgnoreCase)
                ? 0.68f
                : 0.42f;
        boostUntil = Time.unscaledTime + DefaultBoostSeconds;
    }

    private void LateUpdate()
    {
        if (targetRenderer == null || targetRenderer.sprite == null)
        {
            return;
        }

        SyncSorting();
        var bounds = GetOrbitBounds(targetRenderer.sprite);
        if (bounds.size.x < MinBoundsSize || bounds.size.y < MinBoundsSize)
        {
            return;
        }

        actionPulse = Mathf.MoveTowards(actionPulse, Time.unscaledTime < boostUntil ? boostAmount : 0f, Time.unscaledDeltaTime * 2.8f);
        var intensity = 0.55f + actionPulse * 0.42f;
        UpdateMaterial(backCoreMaterial, -1f, intensity * 0.52f);
        UpdateMaterial(backDetailMaterial, -1f, intensity * 0.58f);
        UpdateMaterial(backFlameMaterial, -1f, intensity * 0.64f);
        UpdateMaterial(frontCoreMaterial, 1f, intensity * 0.76f);
        UpdateMaterial(frontDetailMaterial, 1f, intensity * 0.86f);
        UpdateMaterial(frontFlameMaterial, 1f, intensity * 0.98f);
        BuildStreamLayer(backCoreMesh, bounds, false, intensity * 0.34f, false);
        BuildDetailLayer(backDetailMesh, bounds, false, intensity * 0.38f);
        BuildParticleLayer(backFlameMesh, bounds, false, intensity * 0.46f);
        BuildStreamLayer(frontCoreMesh, bounds, true, intensity * 0.58f, false);
        BuildDetailLayer(frontDetailMesh, bounds, true, intensity * 0.62f);
        BuildParticleLayer(frontFlameMesh, bounds, true, intensity * 0.8f);
    }

    private void OnDestroy()
    {
        WunaOrbitFireMaterials.DestroyAll(new[]
        {
            backCoreMaterial,
            backDetailMaterial,
            backFlameMaterial,
            frontCoreMaterial,
            frontDetailMaterial,
            frontFlameMaterial
        });
        DestroyMesh(backCoreMesh);
        DestroyMesh(backDetailMesh);
        DestroyMesh(backFlameMesh);
        DestroyMesh(frontCoreMesh);
        DestroyMesh(frontDetailMesh);
        DestroyMesh(frontFlameMesh);
    }

    private static void DestroyMesh(Mesh? mesh)
    {
        if (mesh != null)
        {
            Destroy(mesh);
        }
    }

    private void EnsureLayer(
        string name,
        bool frontLayer,
        bool coreLayer,
        bool flameAtlasLayer,
        ref Mesh? mesh,
        ref MeshRenderer? meshRenderer,
        ref Material? material)
    {
        var childName = "SunExp_WunaOrbitFire_" + name;
        var child = transform.Find(childName);
        if (child == null)
        {
            var go = new GameObject(childName, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            child = go.transform;
        }

        mesh = new Mesh
        {
            name = childName + "_Mesh"
        };
        mesh.MarkDynamic();
        child.GetComponent<MeshFilter>().sharedMesh = mesh;

        material = WunaOrbitFireMaterials.CreateLayerMaterial(frontLayer, coreLayer, flameAtlasLayer);
        meshRenderer = child.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
    }

    private void SyncSorting()
    {
        if (targetRenderer == null)
        {
            return;
        }

        ApplySorting(backCoreRenderer, -3);
        ApplySorting(backDetailRenderer, -2);
        ApplySorting(backFlameRenderer, -1);
        ApplySorting(frontCoreRenderer, 1);
        ApplySorting(frontDetailRenderer, 2);
        ApplySorting(frontFlameRenderer, 3);
    }

    private void ApplySorting(MeshRenderer? renderer, int offset)
    {
        if (renderer == null || targetRenderer == null)
        {
            return;
        }

        renderer.sortingLayerID = targetRenderer.sortingLayerID;
        renderer.sortingOrder = targetRenderer.sortingOrder + offset;
    }

    private static void UpdateMaterial(Material? material, float layer, float intensity)
    {
        if (material == null)
        {
            return;
        }

        SetFloatIfPresent(material, WunaOrbitFireShaderIds.FlowTime, Time.unscaledTime);
        SetFloatIfPresent(material, WunaOrbitFireShaderIds.Intensity, intensity);
        SetFloatIfPresent(material, WunaOrbitFireShaderIds.Layer, layer);
    }

    private static void SetFloatIfPresent(Material material, int propertyId, float value)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetFloat(propertyId, value);
        }
    }

    private void BuildCoreLayer(Mesh? mesh, Bounds bounds, bool frontLayer, float intensity)
    {
        BeginMesh();
        var time = Time.unscaledTime;
        foreach (var rail in Rails)
        {
            AddCoreRibbon(rail, bounds, time, frontLayer, intensity);
        }

        EndMesh(mesh);
    }

    private void BuildDetailLayer(Mesh? mesh, Bounds bounds, bool frontLayer, float intensity)
    {
        BeginMesh();
        var time = Time.unscaledTime;
        for (var railIndex = 0; railIndex < Rails.Length; railIndex++)
        {
            AddFlameTongues(Rails[railIndex], railIndex, bounds, time, frontLayer, intensity);
            AddSparks(Rails[railIndex], railIndex, bounds, time, frontLayer, intensity);
        }

        EndMesh(mesh);
    }

    private void BeginMesh()
    {
        vertices.Clear();
        uvs.Clear();
        localUvs.Clear();
        colors.Clear();
        triangles.Clear();
    }

    private void EndMesh(Mesh? mesh)
    {
        if (mesh == null)
        {
            return;
        }

        mesh.Clear();
        if (vertices.Count == 0)
        {
            return;
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, localUvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private static void ClearMesh(Mesh? mesh)
    {
        if (mesh != null)
        {
            mesh.Clear();
        }
    }

    private Bounds GetOrbitBounds(Sprite sprite)
    {
        var full = sprite.bounds;
        var alpha = TryGetAlphaBounds(sprite, full);
        var source = alpha.HasValue && alpha.Value.size.x > MinBoundsSize && alpha.Value.size.y > MinBoundsSize
            ? alpha.Value
            : full;

        var fullFocus = new Vector2(
            full.min.x + full.size.x * OrbitBoundsFocus.x,
            full.min.y + full.size.y * OrbitBoundsFocus.y);
        var center = Vector2.Lerp(source.center, fullFocus, source.size.x > source.size.y * 1.15f ? 0.72f : 0.38f);
        var height = Mathf.Min(source.size.y * 0.86f, full.size.y * 0.74f);
        var width = Mathf.Min(source.size.x * 0.74f, height * 0.82f);
        height = Mathf.Max(height, width * 1.18f);

        return new Bounds(
            new Vector3(center.x, center.y, full.center.z),
            new Vector3(Mathf.Max(width, MinBoundsSize), Mathf.Max(height, MinBoundsSize), full.size.z));
    }

    private static Bounds? TryGetAlphaBounds(Sprite sprite, Bounds full)
    {
        var texture = sprite.texture;
        if (texture == null)
        {
            return null;
        }

        var key = sprite.GetInstanceID();
        if (AlphaBoundsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var rect = sprite.textureRect;
            var minX = 1f;
            var minY = 1f;
            var maxX = 0f;
            var maxY = 0f;
            var found = false;
            var stepX = Mathf.Max(1, Mathf.RoundToInt(rect.width / 96f));
            var stepY = Mathf.Max(1, Mathf.RoundToInt(rect.height / 96f));

            for (var y = 0; y < rect.height; y += stepY)
            {
                for (var x = 0; x < rect.width; x += stepX)
                {
                    var u = (rect.x + x + 0.5f) / texture.width;
                    var v = (rect.y + y + 0.5f) / texture.height;
                    if (texture.GetPixelBilinear(u, v).a < AlphaBoundsThreshold)
                    {
                        continue;
                    }

                    var nx = x / rect.width;
                    var ny = y / rect.height;
                    minX = Mathf.Min(minX, nx);
                    minY = Mathf.Min(minY, ny);
                    maxX = Mathf.Max(maxX, nx);
                    maxY = Mathf.Max(maxY, ny);
                    found = true;
                }
            }

            if (!found)
            {
                AlphaBoundsCache[key] = null;
                return null;
            }

            var paddingX = Mathf.Clamp((maxX - minX) * 0.08f, 0.025f, 0.07f);
            var paddingY = Mathf.Clamp((maxY - minY) * 0.06f, 0.02f, 0.06f);
            minX = Mathf.Clamp01(minX - paddingX);
            maxX = Mathf.Clamp01(maxX + paddingX);
            minY = Mathf.Clamp01(minY - paddingY);
            maxY = Mathf.Clamp01(maxY + paddingY);

            var min = new Vector2(
                Mathf.Lerp(full.min.x, full.max.x, minX),
                Mathf.Lerp(full.min.y, full.max.y, minY));
            var max = new Vector2(
                Mathf.Lerp(full.min.x, full.max.x, maxX),
                Mathf.Lerp(full.min.y, full.max.y, maxY));
            var result = new Bounds(
                new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, full.center.z),
                new Vector3(Mathf.Max(max.x - min.x, MinBoundsSize), Mathf.Max(max.y - min.y, MinBoundsSize), full.size.z));
            AlphaBoundsCache[key] = result;
            return result;
        }
        catch (UnityException)
        {
            AlphaBoundsCache[key] = null;
            return null;
        }
    }

    private void BuildParticleLayer(Mesh? mesh, Bounds bounds, bool frontLayer, float intensity)
    {
        BeginMesh();
        var time = Time.unscaledTime;
        for (var railIndex = 0; railIndex < Rails.Length; railIndex++)
        {
            var rail = Rails[railIndex];
            for (var i = 0; i < OrbitFlamesPerRail; i++)
            {
                var orbitPhase = Mathf.Repeat(
                    (i + railIndex * 0.43f) / OrbitFlamesPerRail
                    + time * (0.07f + railIndex * 0.012f) * rail.Direction
                    + Mathf.Sin(time * 0.37f + i * 1.7f + rail.Phase) * 0.018f,
                    1f);
                var sampleT = Mathf.Repeat(orbitPhase + Mathf.Sin(time * 0.53f + i + rail.Phase) * 0.012f, 1f);
                var sample = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, sampleT);
                var layerFade = LayerFade(sample.Depth, frontLayer);
                if (!frontLayer)
                {
                    layerFade = OccludeBackLayer(layerFade, sample, bounds, rail);
                }

                if (layerFade <= 0.08f)
                {
                    continue;
                }

                var tangent = WunaOrbitFireOrbitModel.Tangent(bounds, rail, time, actionPulse, sampleT);
                var normal = new Vector2(-tangent.y, tangent.x);
                var flicker = Mathf.Sin(time * 7.4f + orbitPhase * Tau * 2.2f + i * 1.91f + rail.Phase) * 0.5f + 0.5f;
                var frontBoost = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.18f, 0.88f, sample.Depth));
                var flameLife = Mathf.Lerp(0.42f, 1f, flicker) * (frontLayer ? Mathf.Lerp(0.68f, 1.14f, frontBoost) : Mathf.Lerp(0.84f, 0.56f, frontBoost));
                var alpha = Mathf.Clamp01(layerFade * intensity * rail.AlphaScale * flameLife * Mathf.Lerp(0.42f, 0.86f, flicker));
                if (alpha <= 0.03f)
                {
                    continue;
                }

                var size = bounds.size.x
                    * rail.TongueWidthScale
                    * sample.Scale
                    * Mathf.Lerp(0.48f, 0.96f, flicker)
                    * Mathf.Lerp(0.9f, 1.18f, actionPulse);
                var stretch = size * Mathf.Lerp(1.42f, 2.35f, flicker) * (frontLayer ? 1.08f : 0.86f);
                var side = Mathf.Sin(time * 1.4f + i + rail.Phase) * size * 0.42f;
                var center = sample.Position + normal * side;
                var frame = (int)Mathf.Repeat(time * (20f + railIndex * 2f) + i * 11f + railIndex * 13f, FlameAtlasFrames);
                AddFlameQuad(center, tangent, normal, size, stretch, alpha, frame);
            }
        }

        EndMesh(mesh);
    }

    private void BuildStreamLayer(Mesh? mesh, Bounds bounds, bool frontLayer, float intensity, bool outerVeil)
    {
        BeginMesh();
        var time = Time.unscaledTime;
        foreach (var rail in Rails)
        {
            AddStreamRibbon(rail, bounds, time, frontLayer, intensity, outerVeil);
        }

        EndMesh(mesh);
    }

    private void AddCoreRibbon(WunaOrbitFireOrbitModel.OrbitRail rail, Bounds bounds, float time, bool frontLayer, float intensity)
    {
        var firstVertex = vertices.Count;
        for (var section = 0; section < CoreSections; section++)
        {
            var t = section / (float)(CoreSections - 1);
            var sample = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, t);
            var tangent = WunaOrbitFireOrbitModel.Tangent(bounds, rail, time, actionPulse, t);
            var normal = new Vector2(-tangent.y, tangent.x);
            var layerFade = LayerFade(sample.Depth, frontLayer);
            if (!frontLayer)
            {
                layerFade = OccludeBackLayer(layerFade, sample, bounds, rail);
            }

            var widthCurve = 0.38f + Mathf.Sin((1f - t) * Mathf.PI) * 0.42f;
            var headGlow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0f, t));
            var width = bounds.size.x * rail.CoreWidthScale * (widthCurve + headGlow * 0.5f) * Mathf.Lerp(0.96f, 1.22f, actionPulse);
            var alphaCurve = Mathf.Pow(1f - t, 1.15f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.78f, t));
            var alpha = Mathf.Clamp01(layerFade * intensity * rail.AlphaScale * alphaCurve * 1.15f);
            var drift = normal * (Mathf.Sin(time * 1.55f + rail.Phase + section * 0.23f) * bounds.size.x * 0.005f);
            AddRibbonSection(sample.Position + drift, normal, width, alpha, 1f - t);
        }

        AddStripTriangles(firstVertex, CoreSections);
    }

    private void AddStreamRibbon(WunaOrbitFireOrbitModel.OrbitRail rail, Bounds bounds, float time, bool frontLayer, float intensity, bool outerVeil)
    {
        var firstVertex = vertices.Count;
        for (var section = 0; section <= CoreSections; section++)
        {
            var t = section / (float)CoreSections;
            var sample = SmoothSample(bounds, rail, time, t);
            var tangent = SmoothTangent(bounds, rail, time, t);
            var normal = new Vector2(-tangent.y, tangent.x);
            var layerFade = LayerFade(sample.Depth, frontLayer);
            if (!frontLayer)
            {
                layerFade = OccludeBackLayer(layerFade, sample, bounds, rail);
            }

            var orbitWave = Mathf.Sin(t * Mathf.PI * 6f + time * (1.1f + Mathf.Abs(rail.Speed)) + rail.Phase) * 0.5f + 0.5f;
            var envelope = Mathf.Lerp(0.72f, 1.08f, orbitWave);
            var frontGlow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.88f, sample.Depth));
            var widthScale = outerVeil
                ? Mathf.Lerp(0.48f, 0.88f, envelope)
                : Mathf.Lerp(0.26f, 0.52f, envelope) + frontGlow * 0.06f;
            var width = bounds.size.x
                * (outerVeil ? rail.TongueWidthScale : rail.CoreWidthScale)
                * sample.Scale
                * widthScale
                * Mathf.Lerp(0.84f, 1.04f, actionPulse);
            var alphaCurve = Mathf.Lerp(0.72f, 1f, orbitWave) * Mathf.Lerp(0.82f, 1.12f, frontGlow);
            var alphaScale = outerVeil ? 0.34f : 0.72f;
            var alpha = Mathf.Clamp01(layerFade * intensity * rail.AlphaScale * alphaCurve * alphaScale);
            var floatOffset = Mathf.Sin(time * 1.35f + t * Mathf.PI * 10f + rail.Phase)
                * bounds.size.x
                * rail.FlickerAmplitude
                * (outerVeil ? 0.45f : 0.22f);
            var trailU = Mathf.Repeat(t + time * 0.06f * rail.Direction, 1f);
            AddRibbonSection(sample.Position + normal * floatOffset, normal, width, alpha, trailU, 0.5f);
        }

        AddStripTriangles(firstVertex, CoreSections + 1);
    }

    private WunaOrbitFireOrbitModel.OrbitSample SmoothSample(Bounds bounds, WunaOrbitFireOrbitModel.OrbitRail rail, float time, float t)
    {
        var before = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, t - 0.012f);
        var current = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, t);
        var after = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, t + 0.012f);
        var position = (before.Position + current.Position * 2f + after.Position) * 0.25f;
        var depth = (before.Depth + current.Depth * 2f + after.Depth) * 0.25f;
        var scale = (before.Scale + current.Scale * 2f + after.Scale) * 0.25f;
        return new WunaOrbitFireOrbitModel.OrbitSample(position, depth, scale);
    }

    private Vector2 SmoothTangent(Bounds bounds, WunaOrbitFireOrbitModel.OrbitRail rail, float time, float t)
    {
        var before = SmoothSample(bounds, rail, time, t - 0.016f);
        var after = SmoothSample(bounds, rail, time, t + 0.016f);
        var tangent = (after.Position - before.Position).normalized;
        return tangent.sqrMagnitude < 0.001f ? Vector2.right : tangent;
    }

    private void AddFlameTongues(WunaOrbitFireOrbitModel.OrbitRail rail, int railIndex, Bounds bounds, float time, bool frontLayer, float intensity)
    {
        for (var i = 0; i < DetailTongues; i++)
        {
            var raw = (i + 0.35f) / (DetailTongues + 0.85f);
            var t = Mathf.Pow(raw, 1.18f);
            t = Mathf.Clamp01(t + Mathf.Sin(time * 1.7f + i * 1.31f + rail.Phase) * 0.026f);
            var sample = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, t);
            var layerFade = LayerFade(sample.Depth, frontLayer);
            if (!frontLayer)
            {
                layerFade = OccludeBackLayer(layerFade, sample, bounds, rail);
            }

            if (layerFade <= 0.035f)
            {
                continue;
            }

            var tangent = WunaOrbitFireOrbitModel.Tangent(bounds, rail, time, actionPulse, t);
            var normal = new Vector2(-tangent.y, tangent.x);
            var side = (i % 2 == 0 ? 1f : -1f) * rail.Direction;
            var tonguePhase = Mathf.Sin(time * (2.35f + railIndex * 0.23f) + rail.Phase + i * 1.7f) * 0.5f + 0.5f;
            var headBias = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.02f, t));
            var width = bounds.size.x
                * rail.TongueWidthScale
                * sample.Scale
                * Mathf.Lerp(0.46f, 1.08f, tonguePhase)
                * Mathf.Lerp(0.82f, 1.16f, headBias);
            var length = bounds.size.y
                * rail.TongueLengthScale
                * sample.Scale
                * Mathf.Lerp(0.62f, 1.32f, tonguePhase)
                * Mathf.Lerp(0.94f, 1.22f, actionPulse);
            var alpha = Mathf.Clamp01(layerFade * intensity * rail.AlphaScale * Mathf.Pow(1f - t, 1.12f) * Mathf.Lerp(0.6f, 1f, headBias));
            var center = sample.Position + normal * side * width * Mathf.Lerp(0.16f, 0.38f, tonguePhase);
            AddTongueQuad(center, tangent, normal * side, width, length, alpha);
        }
    }

    private void AddSparks(WunaOrbitFireOrbitModel.OrbitRail rail, int railIndex, Bounds bounds, float time, bool frontLayer, float intensity)
    {
        for (var i = 0; i < DetailSparks; i++)
        {
            var t = Mathf.Pow((i + 0.7f) / (DetailSparks + 1.15f), 1.12f);
            var sample = WunaOrbitFireOrbitModel.Sample(bounds, rail, time, actionPulse, t);
            var layerFade = LayerFade(sample.Depth, frontLayer);
            if (!frontLayer)
            {
                layerFade = OccludeBackLayer(layerFade, sample, bounds, rail);
            }

            var sparkle = Mathf.Sin(time * 4.65f + railIndex * 2.1f + i * 1.37f) * 0.5f + 0.5f;
            if (sparkle < 0.42f || layerFade <= 0.05f)
            {
                continue;
            }

            var tangent = WunaOrbitFireOrbitModel.Tangent(bounds, rail, time, actionPulse, t);
            var normal = new Vector2(-tangent.y, tangent.x);
            var side = i % 2 == 0 ? 1f : -1f;
            var size = bounds.size.x * rail.TongueWidthScale * sample.Scale * Mathf.Lerp(0.18f, 0.32f, sparkle);
            var center = sample.Position + normal * side * bounds.size.x * rail.TongueWidthScale * (0.55f + sparkle * 0.35f);
            var alpha = Mathf.Clamp01(layerFade * intensity * 0.74f * sparkle * Mathf.Pow(1f - t, 0.76f));
            AddTongueQuad(center, tangent, normal * side, size, size * 1.8f, alpha);
        }
    }

    private float OccludeBackLayer(float layerFade, WunaOrbitFireOrbitModel.OrbitSample sample, Bounds bounds, WunaOrbitFireOrbitModel.OrbitRail rail)
    {
        var silhouette = BodyMaskAlpha(sample.Position, bounds);
        var behindBody = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, -0.78f, sample.Depth));
        var bodyCover = Mathf.Clamp01(silhouette * behindBody * rail.OcclusionStrength * 1.08f);
        return layerFade * (1f - bodyCover);
    }

    private void AddRibbonSection(Vector2 center, Vector2 normal, float width, float alpha, float u, float localU = -1f)
    {
        var half = width * 0.5f;
        var left = center - normal * half;
        var right = center + normal * half;
        var localX = localU >= 0f ? localU : u;

        vertices.Add(new Vector3(left.x, left.y, 0f));
        vertices.Add(new Vector3(right.x, right.y, 0f));
        uvs.Add(new Vector2(u, 0f));
        uvs.Add(new Vector2(u, 1f));
        localUvs.Add(new Vector2(localX, 0f));
        localUvs.Add(new Vector2(localX, 1f));

        var color = new Color(1f, 1f, 1f, alpha);
        colors.Add(color);
        colors.Add(color);
    }

    private void AddStripTriangles(int firstVertex, int sectionCount)
    {
        for (var i = 0; i < sectionCount - 1; i++)
        {
            var a = firstVertex + i * 2;
            var b = a + 2;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(a + 1);
            triangles.Add(a + 1);
            triangles.Add(b);
            triangles.Add(b + 1);
        }
    }

    private void AddTongueQuad(Vector2 center, Vector2 tangent, Vector2 outward, float width, float length, float alpha)
    {
        var index = vertices.Count;
        var half = width * 0.5f;
        var lean = tangent.sqrMagnitude < 0.001f ? Vector2.right : tangent.normalized;
        var side = outward.sqrMagnitude < 0.001f ? new Vector2(-lean.y, lean.x) : outward.normalized;
        var up = (Vector2.up * 0.72f + lean * 0.18f + side * 0.24f).normalized;
        var cross = new Vector2(up.y, -up.x);
        var baseCenter = center - up * length * 0.28f;
        var tipCenter = center + up * length * 0.72f + side * width * 0.34f;
        var baseLeft = baseCenter - cross * half;
        var baseRight = baseCenter + cross * half;
        var tipRight = tipCenter + cross * half * 0.22f;
        var tipLeft = tipCenter - cross * half * 0.22f;

        vertices.Add(new Vector3(baseLeft.x, baseLeft.y, 0f));
        vertices.Add(new Vector3(baseRight.x, baseRight.y, 0f));
        vertices.Add(new Vector3(tipRight.x, tipRight.y, 0f));
        vertices.Add(new Vector3(tipLeft.x, tipLeft.y, 0f));
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        localUvs.Add(new Vector2(0f, 0f));
        localUvs.Add(new Vector2(0f, 1f));
        localUvs.Add(new Vector2(1f, 1f));
        localUvs.Add(new Vector2(1f, 0f));

        var color = new Color(1f, 1f, 1f, alpha);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 1);
        triangles.Add(index);
        triangles.Add(index + 3);
        triangles.Add(index + 2);
    }

    private void AddFlameQuad(Vector2 center, Vector2 tangent, Vector2 normal, float width, float height, float alpha, int frame)
    {
        var index = vertices.Count;
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        var lean = tangent.sqrMagnitude < 0.001f ? Vector2.right : tangent.normalized;
        var outward = normal.sqrMagnitude < 0.001f ? Vector2.up : normal.normalized;
        var up = (Vector2.up * 0.78f + lean * 0.16f + outward * 0.24f).normalized;
        var right = new Vector2(up.y, -up.x);
        var baseLift = up * halfHeight * 0.22f;
        var bottom = center - baseLift;
        var top = center + up * halfHeight;
        var lowerLeft = bottom - right * halfWidth;
        var lowerRight = bottom + right * halfWidth;
        var upperRight = top + right * halfWidth * 0.58f;
        var upperLeft = top - right * halfWidth * 0.58f;

        vertices.Add(new Vector3(lowerLeft.x, lowerLeft.y, 0f));
        vertices.Add(new Vector3(lowerRight.x, lowerRight.y, 0f));
        vertices.Add(new Vector3(upperRight.x, upperRight.y, 0f));
        vertices.Add(new Vector3(upperLeft.x, upperLeft.y, 0f));

        var column = frame % FlameAtlasColumns;
        var row = frame / FlameAtlasColumns;
        var invColumns = 1f / FlameAtlasColumns;
        var invRows = 1f / FlameAtlasRows;
        var u0 = column * invColumns;
        var u1 = u0 + invColumns;
        var v1 = 1f - row * invRows;
        var v0 = v1 - invRows;
        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u0, v1));
        localUvs.Add(new Vector2(0f, 0f));
        localUvs.Add(new Vector2(1f, 0f));
        localUvs.Add(new Vector2(1f, 1f));
        localUvs.Add(new Vector2(0f, 1f));

        var color = new Color(1f, 1f, 1f, alpha);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);

        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 1);
        triangles.Add(index);
        triangles.Add(index + 3);
        triangles.Add(index + 2);
    }

    private static float LayerFade(float depth, bool frontLayer)
    {
        return frontLayer
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.58f, depth))
            : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.58f, 0.1f, depth));
    }

    private float BodyMaskAlpha(Vector2 localPoint, Bounds bounds)
    {
        return Mathf.Max(SampledSpriteAlpha(localPoint, bounds), FallbackBodyMask(localPoint, bounds));
    }

    private float SampledSpriteAlpha(Vector2 localPoint, Bounds bounds)
    {
        var renderer = targetRenderer;
        var sprite = renderer?.sprite;
        var texture = sprite?.texture;
        if (sprite == null || texture == null || unreadableTextureId == texture.GetInstanceID())
        {
            return 0f;
        }

        var normalized = new Vector2(
            Mathf.InverseLerp(bounds.min.x, bounds.max.x, localPoint.x),
            Mathf.InverseLerp(bounds.min.y, bounds.max.y, localPoint.y));
        if (normalized.x <= 0f || normalized.x >= 1f || normalized.y <= 0f || normalized.y >= 1f)
        {
            return 0f;
        }

        try
        {
            var rect = sprite.textureRect;
            var u = (rect.x + rect.width * normalized.x) / texture.width;
            var v = (rect.y + rect.height * normalized.y) / texture.height;
            return texture.GetPixelBilinear(u, v).a;
        }
        catch (UnityException)
        {
            unreadableTextureId = texture.GetInstanceID();
            return 0f;
        }
    }

    private static float FallbackBodyMask(Vector2 localPoint, Bounds bounds)
    {
        var uv = new Vector2(
            Mathf.InverseLerp(bounds.min.x, bounds.max.x, localPoint.x),
            Mathf.InverseLerp(bounds.min.y, bounds.max.y, localPoint.y));
        if (uv.x <= 0f || uv.x >= 1f || uv.y <= 0f || uv.y >= 1f)
        {
            return 0f;
        }

        var torso = EllipseMask(uv, new Vector2(0.52f, 0.48f), new Vector2(0.14f, 0.27f));
        var dress = EllipseMask(uv, new Vector2(0.52f, 0.25f), new Vector2(0.2f, 0.2f));
        var hair = EllipseMask(uv, new Vector2(0.44f, 0.54f), new Vector2(0.28f, 0.22f));
        var rightSleeve = EllipseMask(uv, new Vector2(0.67f, 0.38f), new Vector2(0.16f, 0.2f));
        return Mathf.Max(Mathf.Max(torso, dress), Mathf.Max(hair, rightSleeve));
    }

    private static float EllipseMask(Vector2 uv, Vector2 center, Vector2 radius)
    {
        var dx = (uv.x - center.x) / Mathf.Max(radius.x, 0.001f);
        var dy = (uv.y - center.y) / Mathf.Max(radius.y, 0.001f);
        var distance = dx * dx + dy * dy;
        return 1f - Mathf.SmoothStep(0.72f, 1f, distance);
    }

}
