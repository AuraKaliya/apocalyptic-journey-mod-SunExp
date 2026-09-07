using System;
using UnityEngine;

namespace Terrias.Dll.GameApi;

/// <summary>The native objects used by field visuals, independent of combat rules.</summary>
public sealed class FieldPresentationScene
{
    private readonly Func<float>? groundHeight;
    public FieldPresentationScene(RectTransform fightUi, GameObject background, Camera camera,
        Transform ground, RectTransform? hand, RectTransform? left, RectTransform? clock, Func<float>? groundHeight = null)
    {
        this.groundHeight = groundHeight;
        FightUi = fightUi;
        Background = background;
        Camera = camera;
        Ground = ground;
        Hand = hand;
        Left = left;
        Clock = clock;
    }

    public RectTransform FightUi { get; }
    public GameObject Background { get; }
    public Camera Camera { get; }
    public Transform Ground { get; }
    public RectTransform? Hand { get; }
    public RectTransform? Left { get; }
    public RectTransform? Clock { get; }
    public float GroundY => groundHeight != null ? groundHeight() : Ground != null ? Ground.position.y : 0f;

    public bool IsAlive => FightUi != null && FightUi.gameObject.activeInHierarchy
        && Background != null && Background.activeInHierarchy && Camera != null && Ground != null;

    public bool TryWorldBounds(out Rect bounds)
    {
        bounds = default;
        if (!IsAlive || !TryProject(0f, 0f, out var min) || !TryProject(1f, 1f, out var max)) return false;
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > 0.01f && bounds.height > 0.01f && bounds.width < 10000f;
    }

    private bool TryProject(float x, float y, out Vector3 point)
    {
        point = default;
        var ray = Camera.ViewportPointToRay(new Vector3(x, y, 0f));
        if (Mathf.Abs(ray.direction.z) < 0.0001f) return false;
        var distance = -ray.origin.z / ray.direction.z;
        if (distance < 0f) return false;
        point = ray.GetPoint(distance);
        return !float.IsNaN(point.x) && !float.IsInfinity(point.x);
    }
}
