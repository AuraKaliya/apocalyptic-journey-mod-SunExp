using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Visual;

public sealed class FrameImageAnimator : MonoBehaviour
{
    private Sprite[] frames = System.Array.Empty<Sprite>();
    private Image? image;
    private float frameSeconds = 0.2f;
    private float elapsed;
    private int index;

    public void Configure(FrameSpriteAnimationSpec spec, string logPrefix)
    {
        image ??= GetComponent<Image>();
        frames = FrameSpriteCache.LoadFrames(spec, logPrefix);
        frameSeconds = spec.FrameSeconds;
        elapsed = 0f;
        index = 0;
        SetFrame(0);
        enabled = frames.Length > 1;
    }

    private void Awake()
    {
        image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        SetFrame(index);
    }

    private void Update()
    {
        if (frames.Length <= 1 || image == null)
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
        if (frames.Length == 0 || image == null)
        {
            return;
        }

        index = nextIndex % frames.Length;
        var frame = frames[index];
        if (frame != null)
        {
            image.sprite = frame;
        }
    }
}
