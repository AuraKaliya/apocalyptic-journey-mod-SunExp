using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Network.Command;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCg.Shared;

public sealed class SkillCgArbiterOptions
{
    public int MaxQueueLength { get; set; } = 8;

    public float MaxRequestAgeSeconds { get; set; } = 6f;

    public float DuplicateWindowSeconds { get; set; } = 0.2f;

    public SkillCgArbiterOptions Normalized()
    {
        return new SkillCgArbiterOptions
        {
            MaxQueueLength = Mathf.Clamp(MaxQueueLength, 1, 30),
            MaxRequestAgeSeconds = Mathf.Clamp(MaxRequestAgeSeconds, 0.5f, 30f),
            DuplicateWindowSeconds = Mathf.Clamp(DuplicateWindowSeconds, 0.02f, 2f)
        };
    }
}
public sealed class SkillCgRegisteredEntryView
{
    public SkillCgRegisteredEntryView()
    {
    }

    public SkillCgRegisteredEntryView(AuraCgRegistryEntry entry, AuraCgActivationEntryState activation)
    {
        OwnerModId = entry.OwnerModId;
        CgId = entry.CgId;
        QualifiedCgId = entry.QualifiedCgId;
        DisplayName = entry.DisplayName;
        Kind = entry.Kind;
        TargetRoleIds = (entry.TargetRoleIds ?? new List<string>()).ToList();
        CardIds = (entry.CardIds ?? new List<string>()).ToList();
        SkillIds = (entry.SkillIds ?? new List<string>()).ToList();
        MediaType = entry.Media.Type;
        Resource = entry.Media.Resource;
        FallbackImage = entry.Media.FallbackImage;
        BundlePath = entry.Media.BundlePath;
        BundleAssetPrefix = entry.Media.BundleAssetPrefix;
        FlashMode = entry.Media.FlashMode;
        Priority = entry.Priority;
        Enabled = activation.Enabled;
        ConsumerMode = activation.ConsumerMode;
        ConsumerModId = activation.ConsumerModId;
        UserOverridden = activation.UserOverridden;
    }

    public string OwnerModId { get; set; } = "";

    public string CgId { get; set; } = "";

    public string QualifiedCgId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Kind { get; set; } = "";

    public List<string> TargetRoleIds { get; set; } = new();

    public List<string> CardIds { get; set; } = new();

    public List<string> SkillIds { get; set; } = new();

    public string MediaType { get; set; } = "";

    public string Resource { get; set; } = "";

    public string FallbackImage { get; set; } = "";

    public string BundlePath { get; set; } = "";

    public string BundleAssetPrefix { get; set; } = "";

    public string FlashMode { get; set; } = "";

    public int Priority { get; set; }

    public bool Enabled { get; set; }

    public string ConsumerMode { get; set; } = "";

    public string ConsumerModId { get; set; } = "";

    public bool UserOverridden { get; set; }
}
public sealed class SkillCgTriggerContext
{
    public string TriggerKind { get; set; } = "";

    public long ActionSequence { get; set; }

    public string EventToken { get; set; } = "";

    public string Action { get; set; } = "";

    public string CardId { get; set; } = "";

    public string SkillId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string OwnerRoleId { get; set; } = "";

    public float CreatedAt { get; set; }
}

[Serializable]
public sealed class SkillCgRequest
{
    internal string PreloadProducerId { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string TriggerKind { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public string ImageResource { get; set; } = "";

    public string BundlePath { get; set; } = "";

    public string BundleAssetPrefix { get; set; } = "";

    public string MediaType { get; set; } = SkillCgMediaTypes.Image;

    public float FrameSeconds { get; set; } = 0.08f;

    public string AlphaMode { get; set; } = SkillCgAlphaModes.None;

    public float KeyThreshold { get; set; } = 0.03f;

    public float KeySoftness { get; set; } = 0.08f;

    public float FlashAtSeconds { get; set; } = -1f;

    public float FlashDuration { get; set; } = 0.18f;

    public string FlashMode { get; set; } = SkillCgFlashModes.Screen;

    public int FlashStartFrame { get; set; }

    public int FlashEndFrame { get; set; }

    public int FlashPulseEveryFrames { get; set; } = 1;

    public float FlashStrength { get; set; } = 0.82f;

    public int Priority { get; set; }

    public float FadeIn { get; set; } = 0.35f;

    public float Hold { get; set; } = 1f;

    public float FadeOut { get; set; } = 0.45f;

    public string PresentationMode { get; set; } = SkillCgPresentationModes.Slide;

    public string FitMode { get; set; } = SkillCgFitModes.Contain;

    public float FocusX { get; set; } = 0.5f;

    public float FocusY { get; set; } = 0.5f;

    public float SafeScale { get; set; } = 1f;

    public float CreatedAt { get; set; }

    public long ActionSequence { get; set; }

    public string EventToken { get; set; } = "";

    public string IssuerPlayerId { get; set; } = "";

    public string SkillCgPlayId { get; set; } = "";

    public bool IsRemote { get; set; }

    public bool DisableSync { get; set; }

    public string DuplicateKey => OwnerInstanceId
                                  + "|" + TriggerKind
                                  + "|" + CardId
                                  + "|" + CanonicalMediaKey()
                                  + "|" + MediaType
                                  + "|" + FrameSeconds.ToString("0.###")
                                  + "|" + AlphaMode
                                  + "|" + FlashMode
                                  + "|" + FlashAtSeconds.ToString("0.###")
                                  + "|" + FlashStartFrame
                                  + "|" + FlashEndFrame
                                  + "|" + FlashPulseEveryFrames
                                  + "|" + PresentationMode
                                  + "|" + FitMode
                                  + "|" + FocusX.ToString("0.###")
                                  + "|" + FocusY.ToString("0.###")
                                  + "|" + SafeScale.ToString("0.###");

    public string QualifiedProviderId => QualifyProviderId(OwnerModId, ProviderId);

    public void Normalize()
    {
        ProviderId = string.IsNullOrWhiteSpace(ProviderId) ? "unknown" : ProviderId.Trim();
        OwnerModId = OwnerModId?.Trim() ?? "";
        CardId = CardId?.Trim() ?? "";
        TriggerKind = TriggerKind?.Trim().ToLowerInvariant() ?? "";
        OwnerInstanceId = OwnerInstanceId?.Trim() ?? "";
        ImagePath = ImagePath?.Trim() ?? "";
        ImageResource = ImageResource?.Trim() ?? "";
        BundlePath = NormalizeBundlePath(BundlePath);
        BundleAssetPrefix = NormalizeRequestRelativePath(BundleAssetPrefix);
        if (string.IsNullOrWhiteSpace(ImageResource) && !string.IsNullOrWhiteSpace(ImagePath))
        {
            ImageResource = Path.GetFileName(ImagePath);
        }

        MediaType = SkillCgMediaTypes.Normalize(MediaType);
        FrameSeconds = Mathf.Max(0.01f, FrameSeconds);
        AlphaMode = SkillCgAlphaModes.Normalize(AlphaMode);
        KeyThreshold = Mathf.Clamp01(KeyThreshold);
        KeySoftness = Mathf.Clamp(KeySoftness, 0.001f, 1f);
        FlashAtSeconds = FlashAtSeconds < 0f ? -1f : FlashAtSeconds;
        FlashDuration = Mathf.Clamp(FlashDuration <= 0f ? 0.18f : FlashDuration, 0.03f, 1f);
        FlashMode = SkillCgFlashModes.Normalize(FlashMode);
        FlashStartFrame = Math.Max(0, FlashStartFrame);
        FlashEndFrame = Math.Max(0, FlashEndFrame);
        if (FlashStartFrame > 0 && FlashEndFrame > 0 && FlashEndFrame < FlashStartFrame)
        {
            FlashEndFrame = FlashStartFrame;
        }

        FlashPulseEveryFrames = Math.Max(1, FlashPulseEveryFrames);
        FlashStrength = Mathf.Clamp01(FlashStrength <= 0f ? 0.82f : FlashStrength);
        FadeIn = Mathf.Max(0f, FadeIn);
        Hold = Mathf.Max(0f, Hold);
        FadeOut = Mathf.Max(0f, FadeOut);
        PresentationMode = SkillCgPresentationModes.Normalize(PresentationMode);
        FitMode = SkillCgFitModes.Normalize(FitMode);
        FocusX = Mathf.Clamp01(FocusX);
        FocusY = Mathf.Clamp01(FocusY);
        SafeScale = Mathf.Clamp(SafeScale <= 0f ? 1f : SafeScale, 1f, 3f);
        EventToken = string.IsNullOrWhiteSpace(EventToken)
            ? OwnerInstanceId + ":" + CardId + ":" + ActionSequence.ToString()
            : EventToken.Trim();
        IssuerPlayerId = IssuerPlayerId?.Trim() ?? "";
        SkillCgPlayId = SkillCgPlayId?.Trim() ?? "";

        if (CreatedAt <= 0f)
        {
            CreatedAt = Time.unscaledTime;
        }
    }

    public static SkillCgRequest? FromObject(object? source, string providerId, string ownerModId, int priority, SkillCgTriggerContext context)
    {
        if (source == null)
        {
            return null;
        }

        if (source is SkillCgRequest request)
        {
            return request;
        }

        var type = source.GetType();
        return new SkillCgRequest
        {
            ProviderId = ReadString(type, source, "ProviderId", providerId),
            OwnerModId = ReadString(type, source, "OwnerModId", ownerModId),
            CardId = ReadString(type, source, "CardId", context.CardId),
            TriggerKind = ReadString(type, source, "TriggerKind", context.TriggerKind),
            OwnerInstanceId = ReadString(type, source, "OwnerInstanceId", context.OwnerInstanceId),
            ImagePath = ReadString(type, source, "ImagePath", ""),
            ImageResource = ReadString(type, source, "ImageResource", ""),
            BundlePath = ReadString(type, source, "BundlePath", ""),
            BundleAssetPrefix = ReadString(type, source, "BundleAssetPrefix", ""),
            MediaType = ReadString(type, source, "MediaType", SkillCgMediaTypes.Image),
            FrameSeconds = ReadFloat(type, source, "FrameSeconds", 0.08f),
            AlphaMode = ReadString(type, source, "AlphaMode", SkillCgAlphaModes.None),
            KeyThreshold = ReadFloat(type, source, "KeyThreshold", 0.03f),
            KeySoftness = ReadFloat(type, source, "KeySoftness", 0.08f),
            FlashAtSeconds = ReadFloat(type, source, "FlashAtSeconds", -1f),
            FlashDuration = ReadFloat(type, source, "FlashDuration", 0.18f),
            FlashMode = ReadString(type, source, "FlashMode", SkillCgFlashModes.Screen),
            FlashStartFrame = ReadInt(type, source, "FlashStartFrame", 0),
            FlashEndFrame = ReadInt(type, source, "FlashEndFrame", 0),
            FlashPulseEveryFrames = ReadInt(type, source, "FlashPulseEveryFrames", 1),
            FlashStrength = ReadFloat(type, source, "FlashStrength", 0.82f),
            Priority = ReadInt(type, source, "Priority", priority),
            FadeIn = ReadFloat(type, source, "FadeIn", 0.35f),
            Hold = ReadFloat(type, source, "Hold", 1f),
            FadeOut = ReadFloat(type, source, "FadeOut", 0.45f),
            PresentationMode = ReadString(type, source, "PresentationMode", SkillCgPresentationModes.Slide),
            FitMode = ReadString(type, source, "FitMode", SkillCgFitModes.Contain),
            FocusX = ReadFloat(type, source, "FocusX", 0.5f),
            FocusY = ReadFloat(type, source, "FocusY", 0.5f),
            SafeScale = ReadFloat(type, source, "SafeScale", 1f),
            CreatedAt = ReadFloat(type, source, "CreatedAt", Time.unscaledTime),
            ActionSequence = ReadLong(type, source, "ActionSequence", context.ActionSequence),
            EventToken = ReadString(type, source, "EventToken", context.EventToken),
            IssuerPlayerId = ReadString(type, source, "IssuerPlayerId", ""),
            SkillCgPlayId = ReadString(type, source, "SkillCgPlayId", ""),
            IsRemote = ReadBool(type, source, "IsRemote", false),
            DisableSync = ReadBool(type, source, "DisableSync", false)
        };
    }

    private static string ReadString(Type type, object source, string name, string fallback)
    {
        try
        {
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) as string ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string QualifyProviderId(string ownerModId, string providerId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = (providerId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "unknown";
        }

        if (id.Contains(":") || string.IsNullOrWhiteSpace(owner))
        {
            return id;
        }

        return owner + ":" + id;
    }

    private static string NormalizeBundlePath(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static string NormalizeRequestRelativePath(string value)
    {
        return (value ?? "")
            .Trim()
            .Trim('"')
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private string CanonicalMediaKey()
    {
        var image = string.IsNullOrWhiteSpace(ImageResource) ? ImagePath : ImageResource;
        return NormalizeRequestRelativePath(image).ToLowerInvariant()
               + "|" + NormalizeBundlePath(BundlePath).ToLowerInvariant()
               + "|" + NormalizeRequestRelativePath(BundleAssetPrefix).ToLowerInvariant();
    }

    private static int ReadInt(Type type, object source, string name, int fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is int typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static float ReadFloat(Type type, object source, string name, float fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is float typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static long ReadLong(Type type, object source, string name, long fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is long typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(Type type, object source, string name, bool fallback)
    {
        try
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source);
            return value is bool typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
