using System;

namespace AuraCg.Shared;

internal static class AuraCgMediaCacheKeys
{
    public static string Preload(SkillCgRequest request)
    {
        return string.Equals(request.MediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase)
            ? "sequence:" + Sequence(request)
            : "image:" + Sprite(request.ImagePath, SkillCgAlphaModes.None, 0.03f, 0.08f);
    }

    public static string Sequence(SkillCgRequest request)
    {
        return (request.OwnerModId ?? "")
            + "\u001f" + (request.BundlePath ?? "")
            + "\u001f" + (request.BundleAssetPrefix ?? "")
            + "\u001f" + (request.ImagePath ?? "")
            + "\u001f" + SkillCgAlphaModes.Normalize(request.AlphaMode)
            + "\u001f" + request.KeyThreshold.ToString("0.####")
            + "\u001f" + request.KeySoftness.ToString("0.####");
    }

    public static string Sprite(string path, string alphaMode, float keyThreshold, float keySoftness)
    {
        return path
            + "\u001f" + SkillCgAlphaModes.Normalize(alphaMode)
            + "\u001f" + keyThreshold.ToString("0.####")
            + "\u001f" + keySoftness.ToString("0.####");
    }

    public static string Bundle(string ownerModId, string bundleId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = AuraCgMediaPathResolver.NormalizeBundleId(bundleId);
        return owner.Length == 0 ? id : owner + "\u001f" + id;
    }
}
