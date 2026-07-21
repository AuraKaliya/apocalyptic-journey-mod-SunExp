using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Visual;

public static class FrameAnimationAttacher
{
    public static bool Attach(Image? image, FrameAnimationVisualSpec? visualSpec, string logPrefix)
    {
        if (image == null || visualSpec == null)
        {
            return false;
        }

        var spec = FrameSpriteAnimationSpec.From(visualSpec);
        if (!spec.IsValid)
        {
            return false;
        }

        var animator = image.GetComponent<FrameImageAnimator>() ?? image.gameObject.AddComponent<FrameImageAnimator>();
        animator.Configure(spec, logPrefix);
        return true;
    }

    public static bool Attach(SpriteRenderer? spriteRenderer, FrameAnimationVisualSpec? visualSpec, string logPrefix)
    {
        if (spriteRenderer == null || visualSpec == null)
        {
            return false;
        }

        var spec = FrameSpriteAnimationSpec.From(visualSpec);
        if (!spec.IsValid)
        {
            return false;
        }

        var animator = spriteRenderer.GetComponent<FrameSpriteRendererAnimator>() ?? spriteRenderer.gameObject.AddComponent<FrameSpriteRendererAnimator>();
        animator.Configure(spec, logPrefix);
        return true;
    }
}
