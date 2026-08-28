using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritArtifactPreviewView
{
    private static readonly Color Pale = new(0.92f, 0.95f, 0.98f, 1f);
    private static readonly Color Muted = new(0.66f, 0.73f, 0.80f, 1f);
    private static readonly Color Gold = new(0.96f, 0.78f, 0.34f, 1f);
    private readonly Transform parent;
    private readonly ScrollRect scroll;
    private readonly SpiritArtifactPreviewTransition transition;
    private readonly List<Text> rows = new();

    private SpiritArtifactPreviewView(
        Transform value,
        ScrollRect scrollRect,
        SpiritArtifactPreviewTransition animator)
    {
        parent = value;
        scroll = scrollRect;
        transition = animator;
    }

    public static SpiritArtifactPreviewView Create(TerriasUiComponents.ScrollArea area)
    {
        var group = area.Root.GetComponent<CanvasGroup>() ?? area.Root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        group.interactable = true;
        var animator = area.Root.AddComponent<SpiritArtifactPreviewTransition>();
        animator.Configure(group);
        return new SpiritArtifactPreviewView(area.Content, area.Scroll, animator);
    }

    public void BindSummary(
        SpiritCollectionDocument collection,
        SpiritInstance? spirit,
        bool animate)
    {
        var specs = new List<RowSpec>
        {
            new("总词条预览", 17, Gold, 28f, FontStyle.Bold)
        };
        var view = SpiritArtifactLoadoutResolver.Resolve(collection, spirit);
        var battle = view.Battle;
        AddStat(specs, "生命", battle.FlatLife);
        AddStat(specs, "魔力", battle.OriginMagic);
        AddStat(specs, "精神", battle.OriginSpirit);
        AddStat(specs, "幸运", battle.OriginLuck);
        AddStat(specs, "感知", battle.OriginPerception);
        AddStat(specs, "速度", battle.Speed);
        AddStat(specs, "护甲", battle.FlatArmor);
        AddStat(specs, "魔能上限", battle.MaxMagic);
        AddStat(specs, "开局超凡", battle.StartExtraordinary);
        foreach (var pair in view.SetCounts.Where(value => value.Value >= 2).OrderByDescending(value => value.Value))
        {
            var set = SpiritArtifactRegistry.Set(pair.Key);
            var active = set?.Bonuses.Where(value => value.RequiredPieces <= pair.Value)
                .OrderBy(value => value.RequiredPieces).ToArray() ?? Array.Empty<SpiritArtifactSetBonusDefinition>();
            foreach (var bonus in active)
            {
                var description = SpiritArtifactRegistry.Name(set) + " " + bonus.RequiredPieces + "件："
                                  + SpiritArtifactRegistry.Description(bonus);
                specs.Add(new RowSpec(description, 11, Muted, description.Length > 28 ? 42f : 30f));
            }
        }
        if (specs.Count == 1)
            specs.Add(new RowSpec("当前没有已装备圣遗物词条", 12, Muted, 24f));
        Apply(specs, animate);
    }

    public void BindArtifact(
        SpiritArtifactInstance artifact,
        SpiritCollectionDocument collection,
        bool animate)
    {
        var piece = SpiritArtifactRegistry.Piece(artifact.PieceId);
        var set = SpiritArtifactRegistry.Set(artifact.SetId);
        var specs = new List<RowSpec>
        {
            new("圣遗物详情", 11, Muted, 18f),
            new(SpiritArtifactRegistry.Name(piece), 18, Gold, 26f, FontStyle.Bold),
            new(new string('★', artifact.Rarity) + "   Lv." + artifact.Level + "/5", 13, Pale, 18f),
            new("主词条", 11, Muted, 16f),
            new(
                SpiritArtifactStats.DisplayName(artifact.MainStat.StatId) + "  +" + artifact.MainStat.Value,
                16,
                Pale,
                26f,
                FontStyle.Bold),
            new("副词条", 11, Muted, 16f)
        };
        var grouped = (artifact.SubStatRolls ?? new List<SpiritArtifactStatRoll>())
            .GroupBy(value => value.StatId, StringComparer.Ordinal)
            .OrderBy(value => SpiritArtifactStats.SubStats.IndexOf(value.Key));
        foreach (var statGroup in grouped)
        {
            var values = statGroup.Select(value => value.Value).ToArray();
            var detail = values.Length > 1 ? "（" + string.Join("+", values) + "）" : "";
            specs.Add(new RowSpec(
                SpiritArtifactStats.DisplayName(statGroup.Key) + "  +" + values.Sum() + detail,
                13,
                Pale,
                20f));
        }
        if ((artifact.SubStatRolls?.Count ?? 0) == 0)
            specs.Add(new RowSpec("升级后获得副词条", 12, Muted, 20f));
        specs.Add(new RowSpec("套装 · " + SpiritArtifactRegistry.Name(set), 13, Gold, 20f, FontStyle.Bold));
        foreach (var bonus in set?.Bonuses ?? new List<SpiritArtifactSetBonusDefinition>())
        {
            var description = bonus.RequiredPieces + "件套：" + SpiritArtifactRegistry.Description(bonus);
            specs.Add(new RowSpec(description, 11, Muted, description.Length > 28 ? 42f : 28f));
        }
        var ownerUid = SpiritArtifactInventoryService.EquippedSpiritUid(collection, artifact.ArtifactUid);
        if (ownerUid.Length > 0)
        {
            var owner = collection.Instances.FirstOrDefault(value => value.SpiritUid == ownerUid);
            specs.Add(new RowSpec(
                "装备者 · " + (owner == null ? ownerUid : SpiritPresentationResolver.Name(owner)),
                12,
                Gold,
                20f));
        }
        var presetNames = (collection.ArtifactInventory?.Presets ?? new List<SpiritArtifactPreset>())
            .Where(value => value.ArtifactUids().Contains(artifact.ArtifactUid, StringComparer.Ordinal))
            .Select(value => value.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (presetNames.Length > 0)
        {
            var value = "预设保护 · " + string.Join("、", presetNames);
            specs.Add(new RowSpec(value, 12, Gold, value.Length > 28 ? 36f : 20f));
        }
        Apply(specs, animate);
    }

    public void Clear()
    {
        foreach (var row in rows) row.gameObject.SetActive(false);
        transition.ShowImmediate();
    }

    private void Apply(IReadOnlyList<RowSpec> specs, bool animate)
    {
        EnsureRows(specs.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var active = index < specs.Count;
            rows[index].gameObject.SetActive(active);
            if (active) Bind(rows[index], specs[index]);
        }
        scroll.verticalNormalizedPosition = 1f;
        if (animate) transition.Reveal(); else transition.ShowImmediate();
    }

    private void EnsureRows(int count)
    {
        while (rows.Count < count)
        {
            var text = TerriasUiComponents.AddTextBlock(
                parent,
                "",
                12,
                TextAnchor.UpperLeft,
                Pale,
                20f,
                1f);
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            rows.Add(text);
        }
    }

    private static void AddStat(ICollection<RowSpec> rows, string label, int value)
    {
        if (value > 0) rows.Add(new RowSpec(label + "  +" + value, 13, Pale, 22f));
    }

    private static void Bind(Text text, RowSpec spec)
    {
        text.text = spec.Value;
        text.fontSize = spec.FontSize;
        text.fontStyle = spec.Style;
        text.color = spec.Color;
        text.alignment = TextAnchor.UpperLeft;
        var element = text.GetComponent<LayoutElement>();
        if (element == null) return;
        element.minHeight = spec.Height;
        element.preferredHeight = spec.Height;
    }

    private readonly struct RowSpec
    {
        public RowSpec(
            string value,
            int fontSize,
            Color color,
            float height,
            FontStyle style = FontStyle.Normal)
        {
            Value = value ?? "";
            FontSize = fontSize;
            Color = color;
            Height = height;
            Style = style;
        }

        public string Value { get; }
        public int FontSize { get; }
        public Color Color { get; }
        public float Height { get; }
        public FontStyle Style { get; }
    }
}

internal sealed class SpiritArtifactPreviewTransition : MonoBehaviour
{
    private const float DurationSeconds = 0.12f;
    private CanvasGroup? group;
    private float started;

    public void Configure(CanvasGroup value)
    {
        group = value;
        enabled = false;
    }

    public void Reveal()
    {
        if (group == null) return;
        group.alpha = 0.68f;
        started = Time.unscaledTime;
        enabled = true;
    }

    public void ShowImmediate()
    {
        if (group != null) group.alpha = 1f;
        enabled = false;
    }

    private void Update()
    {
        if (group == null)
        {
            enabled = false;
            return;
        }
        var progress = Mathf.Clamp01((Time.unscaledTime - started) / DurationSeconds);
        var eased = progress * progress * (3f - 2f * progress);
        group.alpha = Mathf.Lerp(0.68f, 1f, eased);
        if (progress >= 1f) enabled = false;
    }
}
