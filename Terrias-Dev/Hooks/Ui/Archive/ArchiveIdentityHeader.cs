using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchiveIdentityHeader : MonoBehaviour
{
    private Image? avatar;
    private Text? avatarFallback;
    private Text? title;
    private Text? characterName;

    public static ArchiveIdentityHeader Create(Transform parent)
    {
        var bounds = ArchiveLayoutMetrics.IdentityHeader;
        var root = ArchiveUiFactory.CreateFromRect("IdentityHeader", parent, bounds);
        var avatarRect = ArchiveUiFactory.CreateTopLeft("Avatar", root, 0f, 4f, 80f, 80f);
        var avatar = avatarRect.gameObject.AddComponent<Image>();
        avatar.color = Color.white;
        avatar.preserveAspect = true;
        avatar.raycastTarget = false;
        var fallback = ArchiveUiFactory.CreateText(
            "AvatarFallback",
            avatarRect,
            "?",
            34,
            TextAnchor.MiddleCenter,
            ArchiveUiTheme.TextPrimary,
            true);
        var separator = ArchiveUiFactory.CreateTopLeft("Separator", root, 96f, 12f, 2f, 64f);
        ArchiveUiFactory.ApplyPanel(separator.gameObject, ArchiveUiTheme.Divider, false);
        var titleRect = ArchiveUiFactory.CreateTopLeft("Title", root, 112f, 8f, 236f, 26f);
        var title = ArchiveUiFactory.CreateText(
            "Value",
            titleRect,
            "",
            16,
            TextAnchor.MiddleLeft,
            ArchiveUiTheme.TextSecondary,
            true);
        var nameRect = ArchiveUiFactory.CreateTopLeft("Name", root, 112f, 34f, 236f, 46f);
        var name = ArchiveUiFactory.CreateText(
            "Value",
            nameRect,
            "",
            30,
            TextAnchor.MiddleLeft,
            ArchiveUiTheme.TextPrimary,
            true);

        var view = root.gameObject.AddComponent<ArchiveIdentityHeader>();
        view.avatar = avatar;
        view.avatarFallback = fallback;
        view.title = title;
        view.characterName = name;
        return view;
    }

    public void Bind(WitchArchiveDisplayEntry entry)
    {
        if (title != null)
        {
            title.text = entry.Title;
        }

        if (characterName != null)
        {
            characterName.text = entry.Name;
        }

        var sprite = TerriasResourceCache.Load<Sprite>(
            entry.AvatarPath,
            true,
            TerriasIds.WitchArchiveResourceCategory);
        if (avatar != null)
        {
            avatar.sprite = sprite;
            avatar.enabled = sprite != null;
        }

        if (avatarFallback != null)
        {
            avatarFallback.text = string.IsNullOrWhiteSpace(entry.Name) ? "?" : entry.Name.Substring(0, 1);
            avatarFallback.gameObject.SetActive(sprite == null);
        }
    }
}
