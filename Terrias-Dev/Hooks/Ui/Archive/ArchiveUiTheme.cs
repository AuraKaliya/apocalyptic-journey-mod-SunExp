using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui.Archive;

public static class ArchiveUiTheme
{
    public static readonly Color Backdrop = new(0.003f, 0.010f, 0.016f, 0.86f);
    public static readonly Color Frame = new(0.012f, 0.031f, 0.043f, 1f);
    public static readonly Color TopBar = new(0.010f, 0.030f, 0.042f, 0.98f);
    public static readonly Color Panel = new(0.018f, 0.052f, 0.066f, 0.76f);
    public static readonly Color PanelStrong = new(0.022f, 0.060f, 0.074f, 0.84f);
    public static readonly Color Control = new(0.055f, 0.120f, 0.138f, 0.08f);
    public static readonly Color ControlSelected = new(0.035f, 0.125f, 0.146f, 0.86f);
    public static readonly Color TextPrimary = new(0.965f, 0.980f, 0.990f, 1f);
    public static readonly Color TextSecondary = new(0.760f, 0.825f, 0.860f, 1f);
    public static readonly Color TextTertiary = new(0.600f, 0.700f, 0.745f, 1f);
    public static readonly Color Accent = new(0.74f, 0.91f, 0.94f, 1f);
    public static readonly Color AccentMuted = new(0.30f, 0.62f, 0.67f, 0.78f);
    public static readonly Color Divider = new(0.65f, 0.84f, 0.87f, 0.38f);
}

public static class ArchiveLayoutMetrics
{
    public const float ReferenceWidth = 1600f;
    public const float ReferenceHeight = 900f;
    public const float EdgeMargin = 20f;
    public const float TopBarHeight = 120f;
    public const float FooterHeight = 64f;
    public const float FooterTop = ReferenceHeight - FooterHeight;
    public const float ContentTop = 144f;
    public const float ContentBottomGap = 24f;
    public const float ContentHeight = FooterTop - ContentTop - ContentBottomGap;
    public const float PortraitAreaHeight = FooterTop - TopBarHeight;

    public static readonly Rect IdentityHeader = new(32f, 16f, 360f, 88f);
    public static readonly Rect CharacterRail = new(432f, 12f, 736f, 96f);
    public static readonly Rect SectionTabs = new(32f, ContentTop, 240f, ContentHeight);
    public static readonly Rect PortraitViewport = new(0f, TopBarHeight, ReferenceWidth, PortraitAreaHeight);
    public static readonly Rect InfoPanel = new(1160f, ContentTop, 408f, ContentHeight);
    public static readonly Rect Footer = new(0f, FooterTop, ReferenceWidth, FooterHeight);
    public static readonly Rect CloseButton = new(1528f, 28f, 48f, 48f);
}

public enum WitchArchiveSection
{
    Basic,
    Background
}

public static class WitchArchiveStrings
{
    public static string EntryLabel => Resolve("魔女档案", "魔女檔案", "Witch Archive", "魔女アーカイブ");

    public static string Basic => Resolve("基础资料", "基礎資料", "Profile", "基本資料");

    public static string Background => Resolve("背景资料", "背景資料", "Background", "背景資料");

    public static string Name => Resolve("姓名", "姓名", "Name", "名前");

    public static string Title => Resolve("称号", "稱號", "Title", "称号");

    public static string Summary => Resolve("角色简介", "角色簡介", "Summary", "人物概要");

    public static string Close => Resolve("关闭", "關閉", "Close", "閉じる");

    public static string SwitchCharacter => Resolve("切换角色", "切換角色", "Switch Character", "キャラクター切替");

    public static string SwitchSection => Resolve("切换栏目", "切換欄目", "Switch Section", "項目切替");

    public static string Empty => Resolve("暂无可用的魔女档案。", "暫無可用的魔女檔案。", "No witch archive entries are available.", "利用可能な魔女記録がありません。");

    private static string Resolve(string zhHans, string zhHant, string english, string japanese)
    {
        return new WitchArchiveLocalizedText
        {
            ZhHans = zhHans,
            ZhHant = zhHant,
            English = english,
            Japanese = japanese
        }.Resolve(zhHans);
    }
}
