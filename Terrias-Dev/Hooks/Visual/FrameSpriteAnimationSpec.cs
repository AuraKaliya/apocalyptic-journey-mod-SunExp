using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public readonly struct FrameSpriteAnimationSpec
{
    public FrameSpriteAnimationSpec(string id, float frameSeconds, IReadOnlyList<string> framePaths)
    {
        Id = string.IsNullOrWhiteSpace(id) ? "anonymous" : id.Trim();
        FrameSeconds = Mathf.Max(0.05f, frameSeconds);
        FramePaths = framePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .ToArray();
    }

    public string Id { get; }

    public float FrameSeconds { get; }

    public IReadOnlyList<string> FramePaths { get; }

    public bool IsValid => FramePaths.Count > 0;

    public static FrameSpriteAnimationSpec From(FrameAnimationVisualSpec spec)
    {
        if (spec == null)
        {
            return new FrameSpriteAnimationSpec("missing", 0.2f, Array.Empty<string>());
        }

        return new FrameSpriteAnimationSpec(spec.Id, spec.FrameSeconds, spec.FramePaths);
    }
}
