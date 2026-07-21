using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

public sealed class FrameSpriteRendererAnimator : MonoBehaviour
{
    private Sprite[] frames = System.Array.Empty<Sprite>();
    private SpriteRenderer? spriteRenderer;
    private float frameSeconds = 0.2f;
    private float elapsed;
    private int index;

    public void Configure(FrameSpriteAnimationSpec spec, string logPrefix)
    {
        spriteRenderer ??= GetComponent<SpriteRenderer>();
        frames = FrameSpriteCache.LoadFrames(spec, logPrefix);
        frameSeconds = spec.FrameSeconds;
        elapsed = 0f;
        index = 0;
        SetFrame(0);
        enabled = frames.Length > 1;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        SetFrame(index);
    }

    private void Update()
    {
        if (frames.Length <= 1 || spriteRenderer == null)
        {
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < frameSeconds)
        {
            return;
        }

        elapsed -= frameSeconds;
        SetFrame(index + 1);
    }

    private void SetFrame(int nextIndex)
    {
        if (frames.Length == 0 || spriteRenderer == null)
        {
            return;
        }

        index = nextIndex % frames.Length;
        var frame = frames[index];
        if (frame != null)
        {
            spriteRenderer.sprite = frame;
        }
    }
}
