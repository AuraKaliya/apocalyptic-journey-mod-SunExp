using System;
using UnityEngine;
using UnityEngine.UI;

namespace AuraTools.UnityUiPreview
{
    internal static class NativeSettingsPageBuilder
    {
        internal static GameObject BuildAudioVisual(Transform parent)
        {
            var root = CreatePageRoot("AudioVisualPage", parent);
            var scroll = PreviewUi.Scroll("AudioVisualScroll", root.transform, new Vector4(8f, 8f, 8f, 8f), 0f);
            AddSection(scroll.Content, "显示");
            AddSelectorRow(scroll.Content, "分辨率", new[] { "1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440" }, 2);
            AddSelectorRow(scroll.Content, "模式", new[] { "窗口", "无边框", "全屏" }, 1);
            AddSelectorRow(scroll.Content, "画面质量", new[] { "低", "普通", "高" }, 1);
            AddSelectorRow(scroll.Content, "帧率", new[] { "60", "120", "165", "无限制" }, 2);

            AddSection(scroll.Content, "音频");
            AddSliderRow(scroll.Content, "全局音量", 100f);
            AddSliderRow(scroll.Content, "音乐音量", 82f);
            AddSliderRow(scroll.Content, "效果音量", 94f);
            AddSliderRow(scroll.Content, "旁白音量", 76f);

            AddSection(scroll.Content, "语言与表现");
            AddSelectorRow(scroll.Content, "字体", new[] { "Harmony", "系统默认" }, 0);
            AddSwitchRow(scroll.Content, "角色配音", true);
            AddSwitchRow(scroll.Content, "低配模式", false);
            return root;
        }

        internal static GameObject BuildGame(Transform parent)
        {
            var root = CreatePageRoot("GamePage", parent);
            var scroll = PreviewUi.Scroll("GameScroll", root.transform, new Vector4(8f, 8f, 8f, 8f), 0f);
            AddSection(scroll.Content, "其他");
            AddSwitchRow(scroll.Content, "推演剧情", true);
            AddSwitchRow(scroll.Content, "加速模式", false);
            AddSelectorRow(scroll.Content, "语言", new[] { "简体中文", "繁体中文", "English", "Japanese", "한국어" }, 0);

            AddSection(scroll.Content, "返回");
            AddActionButtonsRow(scroll.Content);

            AddSection(scroll.Content, "战斗表现");
            AddSwitchRow(scroll.Content, "显示伤害数字", true);
            AddSwitchRow(scroll.Content, "指向卡牌自指", false);
            AddSwitchRow(scroll.Content, "角色配音", true);
            AddSelectorRow(scroll.Content, "动画速度", new[] { "普通", "较快", "最快" }, 0);
            AddSelectorRow(scroll.Content, "选牌确认", new[] { "手动确认", "自动确认" }, 0);

            AddSection(scroll.Content, "辅助");
            AddSwitchRow(scroll.Content, "鼠标悬停提示", true);
            AddSwitchRow(scroll.Content, "战斗前显示卡组", true);
            AddSwitchRow(scroll.Content, "自动保存冒险", true);
            return root;
        }

        internal static GameObject BuildFeedback(Transform parent)
        {
            var root = CreatePageRoot("FeedbackPage", parent);
            var panel = PreviewUi.Stretch("FeedbackPanel", root.transform, new Vector4(30f, 24f, 30f, 24f));
            PreviewUi.Image(panel, Color.white, PreviewAssets.NativePanelLarge);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(60, 60, 42, 30);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleRoot = PreviewUi.Rect("Title", panel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(titleRoot, 0f, 36f);
            PreviewUi.Text(titleRoot, "问题反馈", 28, TextAnchor.MiddleLeft, PreviewTheme.Text);
            var hintRoot = PreviewUi.Rect("Hint", panel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(hintRoot, 0f, 48f);
            PreviewUi.Text(hintRoot, "反馈会附带本次运行的错误与崩溃日志。独立预览不会连接网络或发送数据。", 17, TextAnchor.MiddleLeft, PreviewTheme.MutedText);

            var input = PreviewUi.Input("FeedbackInput", panel.transform, "", "描述遇到的问题…", null, true);
            PreviewUi.Fixed(input.gameObject, 0f, 180f);

            var actionRow = PreviewUi.Rect("Actions", panel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(actionRow, 0f, 46f);
            var actionLayout = actionRow.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 10f;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = false;
            actionLayout.childForceExpandHeight = true;
            var statusRoot = PreviewUi.Rect("Status", actionRow.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(statusRoot, 1f, 0f);
            var status = PreviewUi.Text(statusRoot, "", 13, TextAnchor.MiddleLeft, PreviewTheme.Success);
            var send = PreviewUi.NativeButton("Send", actionRow.transform, "发送反馈", null, 18);
            PreviewUi.Fixed(send.gameObject, 150f, 46f);
            send.onClick.AddListener(() => status.text = string.IsNullOrWhiteSpace(input.text) ? "请先填写反馈内容。" : "预览模式：反馈未发送。界面状态正常。 ");
            return root;
        }

        internal static GameObject BuildKeyBindings(Transform parent)
        {
            var root = CreatePageRoot("KeyBindingsPage", parent);
            var scroll = PreviewUi.Scroll("KeyBindingsScroll", root.transform, new Vector4(30f, 20f, 30f, 20f), 0f);
            AddSection(scroll.Content, "战斗键位");
            AddKeyRow(scroll.Content, "重开战斗", "R");
            AddKeyRow(scroll.Content, "结束回合", "Space");
            AddKeyRow(scroll.Content, "结束选牌", "E");
            AddSection(scroll.Content, "界面键位");
            AddKeyRow(scroll.Content, "打开设置", "Esc");
            AddKeyRow(scroll.Content, "查看卡组", "Tab");
            AddKeyRow(scroll.Content, "切换目标", "Q / E");

            var resetRow = PreviewUi.Rect("ResetRow", scroll.Content, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(resetRow, 0f, 58f);
            var layout = resetRow.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 7, 7);
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var spacer = PreviewUi.Rect("Spacer", resetRow.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(spacer, 1f, 0f);
            var reset = PreviewUi.NativeButton("Reset", resetRow.transform, "重置按键", null, 18);
            PreviewUi.Fixed(reset.gameObject, 154f, 46f);
            return root;
        }

        private static GameObject CreatePageRoot(string name, Transform parent)
        {
            var root = PreviewUi.Stretch(name, parent, Vector4.zero);
            PreviewUi.Image(root, PreviewTheme.Background).raycastTarget = true;
            return root;
        }

        private static void AddSection(Transform parent, string label)
        {
            var root = PreviewUi.Rect("Section-" + label, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, 0f, 58f);
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 12, 10, 4);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            var textRoot = PreviewUi.Rect("Label", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(textRoot, 1f, 0f);
            PreviewUi.Text(textRoot, label, 28, TextAnchor.MiddleLeft, PreviewTheme.Text);
        }

        private static GameObject AddRow(Transform parent, string label, Action<Transform> buildControl)
        {
            var root = PreviewUi.Rect("Setting-" + label, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, 0f, 64f);
            PreviewUi.Image(root, PreviewTheme.Panel);
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(26, 22, 9, 9);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var labelRoot = PreviewUi.Rect("Label", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(labelRoot, 1f, 0f);
            PreviewUi.Text(labelRoot, label, 22, TextAnchor.MiddleLeft, PreviewTheme.Text);
            var controlRoot = PreviewUi.Rect("Control", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(controlRoot, 500f, 46f);
            buildControl(controlRoot.transform);
            return root;
        }

        private static void AddSelectorRow(Transform parent, string label, string[] values, int selected)
        {
            AddRow(parent, label, controlParent => PreviewSelectorControl.Create(controlParent, values, selected));
        }

        private static void AddSwitchRow(Transform parent, string label, bool value)
        {
            AddRow(parent, label, controlParent => PreviewBooleanControl.Create(controlParent, value));
        }

        private static void AddSliderRow(Transform parent, string label, float value)
        {
            AddRow(parent, label, controlParent =>
            {
                var valueRoot = PreviewUi.Rect("Value", controlParent, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(56f, 0f), Vector2.zero);
                var valueText = PreviewUi.Text(valueRoot, Mathf.RoundToInt(value) + "%", 13, TextAnchor.MiddleRight, PreviewTheme.MutedText);
                var sliderRoot = PreviewUi.Stretch("SliderHost", controlParent, new Vector4(0f, 5f, 66f, 5f));
                PreviewUi.Slider("Slider", sliderRoot.transform, value, current => valueText.text = Mathf.RoundToInt(current) + "%");
            });
        }

        private static void AddKeyRow(Transform parent, string label, string binding)
        {
            AddRow(parent, label, controlParent =>
            {
                var button = PreviewUi.NativeButton("Binding", controlParent, binding, null, 18);
                var rect = button.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            });
        }

        private static void AddActionButtonsRow(Transform parent)
        {
            var root = PreviewUi.Rect("ReturnActions", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Fixed(root, 0f, 72f);
            PreviewUi.Image(root, PreviewTheme.Panel);
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 11, 11);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var spacer = PreviewUi.Rect("Spacer", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(spacer, 1f, 0f);
            var returnButton = PreviewUi.NativeButton("ReturnToMenu", root.transform, "返回主菜单", null, 18);
            PreviewUi.Fixed(returnButton.gameObject, 174f, 50f);
            var exitButton = PreviewUi.NativeButton("CloseGame", root.transform, "关闭游戏", null, 18);
            PreviewUi.Fixed(exitButton.gameObject, 154f, 50f);
        }
    }
}
