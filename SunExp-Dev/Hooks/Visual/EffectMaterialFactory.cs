using System;
using System.Globalization;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class EffectMaterialFactory
{
    public static Material? CreateMaterial(string effectId, string fallbackShaderId, string fallbackShaderName, string logPrefix)
    {
        var effect = VisualRegistry.Effect(effectId);
        var shaderSpec = VisualRegistry.Shader(effect?.ShaderId ?? fallbackShaderId) ?? VisualRegistry.Shader(fallbackShaderId);
        var material = CreateFromDeclaredMaterial(effect, shaderSpec, logPrefix);
        if (material == null)
        {
            var shader = ShaderAssetLoader.ResolveShader(shaderSpec, fallbackShaderName, logPrefix);
            if (shader == null)
            {
                return null;
            }

            material = new Material(shader);
        }

        ApplyDefaults(material, effect, logPrefix);
        return material;
    }

    private static Material? CreateFromDeclaredMaterial(VisualEffectVisualSpec? effect, ShaderVisualSpec? shaderSpec, string logPrefix)
    {
        var materialPath = FirstNonEmpty(effect?.MaterialPath, shaderSpec?.MaterialPath);
        if (string.IsNullOrWhiteSpace(materialPath))
        {
            return null;
        }

        var bundlePath = FirstNonEmpty(effect?.BundlePath, shaderSpec?.BundlePath);
        var source = AssetBundleCache.LoadAsset<Material>(bundlePath, materialPath, logPrefix);
        if (source == null)
        {
            return null;
        }

        return new Material(source);
    }

    private static void ApplyDefaults(Material material, VisualEffectVisualSpec? effect, string logPrefix)
    {
        if (effect == null)
        {
            return;
        }

        foreach (var pair in effect.Floats)
        {
            material.SetFloat(pair.Key, pair.Value);
        }

        foreach (var pair in effect.Colors)
        {
            if (TryParseColor(pair.Value, out var color))
            {
                material.SetColor(pair.Key, color);
            }
            else
            {
                SunExpLog.Warn(logPrefix + " effect color parse failed: " + pair.Key + "=" + pair.Value);
            }
        }

        foreach (var pair in effect.Textures)
        {
            var texture = EffectTextureCache.Load(pair.Value, logPrefix);
            if (texture != null)
            {
                material.SetTexture(pair.Key, texture);
            }
        }
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = Color.white;
        var text = (value ?? "").Trim();
        if (!text.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var hex = text.Substring(1);
        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        var a = (byte)255;
        if (hex.Length == 8
            && !byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
        {
            return false;
        }

        color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }

    private static string FirstNonEmpty(string? first, string? second)
    {
        var primary = first ?? "";
        return !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : (second ?? "").Trim();
    }
}
