using System;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public sealed class PolymorphRoleSpec
{
    public PolymorphRoleSpec(
        string id,
        TerriasLocalizedText name,
        string cardFacePath,
        string avatarPath,
        string skill1,
        string skill2,
        bool isLocked,
        int cropOffsetX,
        int cropOffsetY,
        int cropSize)
    {
        Id = id ?? "";
        Name = name ?? new TerriasLocalizedText { LegacyFallback = Id };
        CardFacePath = cardFacePath ?? "";
        AvatarPath = avatarPath ?? "";
        Skill1 = skill1 ?? "";
        Skill2 = skill2 ?? "";
        IsLocked = isLocked;
        CropOffsetX = cropOffsetX;
        CropOffsetY = cropOffsetY;
        CropSize = Math.Max(1, cropSize);
    }

    public string Id { get; }

    public TerriasLocalizedText Name { get; }

    public string DisplayName => Name.Resolve(TerriasLanguageApi.CurrentLocale, Id);

    public string DisplayNameFor(string locale) => Name.Resolve(locale, Id);

    public string CardFacePath { get; }

    public string AvatarPath { get; }

    public string Skill1 { get; }

    public string Skill2 { get; }

    public bool IsLocked { get; }

    public int CropOffsetX { get; }

    public int CropOffsetY { get; }

    public int CropSize { get; }
}
