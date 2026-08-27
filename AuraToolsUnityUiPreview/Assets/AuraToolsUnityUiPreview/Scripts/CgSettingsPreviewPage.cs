using UnityEngine;
using UnityEngine.UI;

namespace AuraTools.UnityUiPreview
{
    internal static class CgSettingsPreviewPage
    {
        internal static GameObject BuildRole(Transform parent)
        {
            var root = PreviewUi.Stretch("RoleCgSettings", parent, Vector4.zero);
            Label(root.transform, "角色", 0f, 0f, 52f, 48f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            Button(root.transform, "Role", "乌娜", 60f, 4f, 300f, 40f, false);
            Checkbox(root.transform, "Sync", true, -126f, 8f);
            LabelRight(root.transform, "联机同步", 0f, 0f, 90f, 48f, PreviewTheme.Text);

            Button(root.transform, "SkillTab", "技能", 0f, 56f, 148f, 40f, true);
            Button(root.transform, "FeastTab", "美餐", 156f, 56f, 148f, 40f, false);
            Button(root.transform, "LowHealthTab", "低生命", 312f, 56f, 148f, 40f, false);

            Label(root.transform, "触发技能", 0f, 104f, 88f, 44f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            Button(root.transform, "Skill", "白曜圣祷", 96f, 106f, 360f, 40f, false, TextAnchor.MiddleLeft);
            Label(root.transform, "只显示当前角色、类型与技能对应的资源", 474f, 104f, 430f, 44f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);

            Candidate(root.transform, "CandidateSelected", 156f, "白曜圣祷", "Terrias · 默认资源", true, false);
            Candidate(root.transform, "CandidateAlternate", 248f, "星火祈愿", "扩展内容 · 默认资源", false, false);
            Candidate(root.transform, "CandidateManual", 340f, "我的乌娜 CG", "玩家资源", false, true);

            Label(root.transform, "3 个可用资源", 0f, 448f, 220f, 40f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            ButtonRight(root.transform, "Import", "导入图片", -216f, 448f, 104f, 40f, false);
            ButtonRight(root.transform, "Reset", "恢复默认", -104f, 448f, 104f, 40f, false);
            return root;
        }

        internal static GameObject BuildEvent(Transform parent, bool preview)
        {
            var root = PreviewUi.Stretch(preview ? "EventCgPreview" : "EventCgSettings", parent, Vector4.zero);
            Button(root.transform, "VictoryTab", "胜利", 0f, 0f, 150f, 40f, true);
            Button(root.transform, "OpeningTab", "战斗开场", 158f, 0f, 150f, 40f, false);
            Button(root.transform, "DefeatTab", "战斗失败", 316f, 0f, 150f, 40f, false);
            Button(root.transform, "SettlementTab", "冒险结算", 474f, 0f, 150f, 40f, false);

            Label(root.transform, "胜利类型", 0f, 52f, 88f, 44f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            Button(root.transform, "VictoryType", "点金手胜利", 96f, 54f, 300f, 40f, false, TextAnchor.MiddleLeft);
            Checkbox(root.transform, "SceneEnabled", true, -136f, 58f);
            LabelRight(root.transform, "启用此场景", 0f, 52f, 104f, 44f, PreviewTheme.Text);

            Button(root.transform, "ConfigTab", "配置", 0f, 104f, 120f, 40f, !preview);
            Button(root.transform, "PreviewTab", "预览", 128f, 104f, 120f, 40f, preview);
            Label(root.transform, preview ? "4 人预览" : "AuraToolsExp 默认方案", 266f, 104f, 360f, 40f,
                preview ? PreviewTheme.MutedText : PreviewTheme.Success, TextAnchor.MiddleLeft);
            Checkbox(root.transform, "Sync", true, -126f, 108f);
            LabelRight(root.transform, "联机同步", 0f, 104f, 90f, 40f, PreviewTheme.Text);

            if (preview)
            {
                BuildEventPreview(root.transform);
            }
            else
            {
                Summary(root.transform, 156f, "使用方案", "AuraToolsExp 默认方案", "", PreviewTheme.Success);
                Summary(root.transform, 212f, "背景", "程序主题（无需背景图）", "可选叠层", PreviewTheme.Text);
                Summary(root.transform, 268f, "冒险队伍", "跟随实际参与玩家 · 当前角色皮肤 · 1-8 人自动布局", "", PreviewTheme.Text);
                Summary(root.transform, 324f, "展示时长", "标准 · 3 秒", "高级调整", PreviewTheme.Text);
                var note = Panel(root.transform, "DefaultNote", 0f, 388f, 0f, 76f, false);
                PreviewUi.FillText(
                    "Text",
                    note.transform,
                    "默认方案由 AuraToolsExp 提供。替换背景或高级参数后，仅保存当前场景的本地覆盖。",
                    15,
                    TextAnchor.MiddleLeft,
                    PreviewTheme.MutedText,
                    new Vector4(14f, 8f, 14f, 8f),
                    true);
            }
            return root;
        }

        private static void Candidate(
            Transform parent,
            string name,
            float y,
            string title,
            string source,
            bool selected,
            bool manual)
        {
            var row = Panel(parent, name, 0f, y, 0f, 84f, selected);
            var thumbnail = PreviewUi.Rect("Thumbnail", row.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(64f, 64f), new Vector2(8f, -10f));
            PreviewUi.Image(thumbnail, new Color(0.08f, 0.07f, 0.14f, 1f));
            var icon = PreviewUi.Rect("Icon", thumbnail.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(32f, 32f), Vector2.zero);
            PreviewUi.Image(icon, selected ? PreviewTheme.Accent : PreviewTheme.AuraAccent, PreviewAssets.Icon("skill-cg"));

            var labelRoot = PreviewUi.Stretch("Resource", row.transform, new Vector4(82f, 8f, 336f, 8f));
            PreviewUi.Text(labelRoot, title + "\n<size=13>" + source + "</size>", 16,
                TextAnchor.MiddleLeft, selected ? PreviewTheme.Text : PreviewTheme.MutedText, true);
            if (selected)
            {
                LabelRight(row.transform, "已选择", -268f, 0f, 58f, 84f, PreviewTheme.Accent);
            }

            var right = manual ? 268f : 192f;
            ButtonRight(row.transform, "Preview", "预览", -right, 24f, 60f, 36f, false);
            ButtonRight(row.transform, "Select", selected ? "已选择" : "选择", -(right - 68f), 24f, 64f, 36f, selected);
            ButtonRight(row.transform, "Adjust", "调整", -(right - 140f), 24f, 64f, 36f, false);
            if (manual)
            {
                ButtonRight(row.transform, "Remove", "移除", -60f, 24f, 60f, 36f, false);
            }
        }

        private static void BuildEventPreview(Transform parent)
        {
            var stage = PreviewUi.Rect("Stage", parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(520f, 292f), new Vector2(0f, -152f));
            PreviewUi.Image(stage, new Color(0.12f, 0.075f, 0.025f, 1f));
            var wash = PreviewUi.Stretch("ThemeWash", stage.transform, Vector4.zero);
            PreviewUi.Image(wash, new Color(0.34f, 0.18f, 0.03f, 0.34f));
            var topBand = PreviewUi.Rect("TopBand", stage.transform,
                new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.945f), Vector2.zero, Vector2.zero, Vector2.zero);
            PreviewUi.Image(topBand, new Color(1f, 0.77f, 0.20f, 0.85f));
            var bottomBand = PreviewUi.Rect("BottomBand", stage.transform,
                new Vector2(0.04f, 0.055f), new Vector2(0.96f, 0.09f), Vector2.zero, Vector2.zero, Vector2.zero);
            PreviewUi.Image(bottomBand, new Color(1f, 0.77f, 0.20f, 0.65f));
            var stageGlow = PreviewUi.Rect("StageGlow", stage.transform,
                new Vector2(0.09f, 0.13f), new Vector2(0.91f, 0.31f), Vector2.zero, Vector2.zero, Vector2.zero);
            PreviewUi.Image(stageGlow, new Color(0.95f, 0.52f, 0.08f, 0.30f));
            for (var index = 0; index < 4; index++)
            {
                var x = 52f + index * 128f;
                var role = PreviewUi.Rect("Participant-" + index, stage.transform,
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(108f, 142f), new Vector2(x, 46f));
                PreviewUi.Image(role, new Color(0.055f, 0.03f, 0.015f, 0.82f), PreviewAssets.ToolboxControl);
                PreviewUi.FillText("Name", role.transform, new[] { "乌娜", "卡洛琳", "阿瓜", "萨娅" }[index],
                    14, TextAnchor.LowerCenter, PreviewTheme.Text, new Vector4(6f, 4f, 6f, 8f), true);
            }
            var caption = PreviewUi.Rect("Caption", stage.transform,
                Vector2.zero, new Vector2(1f, 0.18f), Vector2.zero, Vector2.zero, Vector2.zero);
            PreviewUi.Image(caption, new Color(0f, 0f, 0f, 0.62f));
            PreviewUi.FillText("Text", caption.transform, "点金手胜利 · 组件化队伍构图", 16,
                TextAnchor.MiddleCenter, PreviewTheme.Text, new Vector4(8f, 4f, 8f, 4f), true);

            Label(parent, "预览人数", 0f, 448f, 92f, 40f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            Button(parent, "Minus", "-", 100f, 450f, 44f, 36f, false);
            Label(parent, "4", 152f, 448f, 36f, 40f, PreviewTheme.Text, TextAnchor.MiddleCenter);
            Button(parent, "Plus", "+", 196f, 450f, 44f, 36f, false);
            Label(parent, "实际播放使用当前冒险的真实玩家与角色", 256f, 448f, 430f, 40f,
                PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            ButtonRight(parent, "Play", "播放预览", -112f, 448f, 112f, 40f, false);
        }

        private static void Summary(Transform parent, float y, string label, string value, string action, Color valueColor)
        {
            var row = Panel(parent, "Summary-" + label, 0f, y, 0f, 48f, false);
            Label(row.transform, label, 12f, 0f, 112f, 48f, PreviewTheme.MutedText, TextAnchor.MiddleLeft);
            var actionWidth = string.IsNullOrWhiteSpace(action) ? 0f : 112f;
            var valueRoot = PreviewUi.Stretch("Value", row.transform, new Vector4(124f, 0f, actionWidth + 12f, 0f));
            PreviewUi.Text(valueRoot, value, 16, TextAnchor.MiddleLeft, valueColor, true);
            if (actionWidth > 0f)
            {
                ButtonRight(row.transform, "Action", action, -8f, 4f, 104f, 40f, false);
            }
        }

        private static GameObject Panel(Transform parent, string name, float x, float y, float width, float height, bool selected)
        {
            var root = PreviewUi.Rect(name, parent,
                new Vector2(0f, 1f), new Vector2(width <= 0f ? 1f : 0f, 1f), new Vector2(0f, 1f),
                new Vector2(width <= 0f ? 0f : width, height), new Vector2(x, -y));
            if (width <= 0f)
            {
                var rect = root.GetComponent<RectTransform>();
                rect.offsetMin = new Vector2(x, -y - height);
                rect.offsetMax = new Vector2(0f, -y);
            }
            PreviewUi.Image(root, selected ? Color.white : PreviewTheme.Panel,
                selected ? PreviewAssets.ToolboxCategorySelected : PreviewAssets.ToolboxControl);
            return root;
        }

        private static Button Button(
            Transform parent,
            string name,
            string text,
            float x,
            float y,
            float width,
            float height,
            bool selected,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var root = PreviewUi.Rect(name, parent,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(width, height), new Vector2(x, -y));
            var image = PreviewUi.Image(root, Color.white,
                selected ? PreviewAssets.ToolboxCategorySelected : PreviewAssets.ToolboxControl);
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            PreviewUi.FillText("Label", root.transform, text, 15, anchor,
                selected ? PreviewTheme.Accent : PreviewTheme.Text,
                new Vector4(anchor == TextAnchor.MiddleLeft ? 12f : 6f, 4f, 8f, 4f), true);
            return button;
        }

        private static Button ButtonRight(
            Transform parent,
            string name,
            string text,
            float right,
            float y,
            float width,
            float height,
            bool selected)
        {
            var button = Button(parent, name, text, 0f, y, width, height, selected);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(right, -y);
            return button;
        }

        private static void Checkbox(Transform parent, string name, bool value, float right, float y)
        {
            var root = PreviewUi.Rect(name, parent,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, 32f), new Vector2(right, -y));
            PreviewUi.Image(root, Color.white, PreviewAssets.ToolboxCheckbox(value ? 1 : 0));
        }

        private static Text Label(
            Transform parent,
            string text,
            float x,
            float y,
            float width,
            float height,
            Color color,
            TextAnchor anchor)
        {
            var root = PreviewUi.Rect("Label", parent,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(width, height), new Vector2(x, -y));
            return PreviewUi.Text(root, text, 15, anchor, color, true);
        }

        private static Text LabelRight(
            Transform parent,
            string text,
            float right,
            float y,
            float width,
            float height,
            Color color)
        {
            var label = Label(parent, text, 0f, y, width, height, color, TextAnchor.MiddleLeft);
            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(right, -y);
            return label;
        }

    }
}
