using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.CardVisual;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using UnityEngine;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Capture;

internal static class ReplayFactCaptureV11
{
    internal static ReplayLogicalStateV11 CaptureState(
        int turnIndex,
        ReplayContentCatalogBuilderV11 catalog)
    {
        var manager = FightManager.Instance;
        var result = new ReplayLogicalStateV11
        {
            LevelId = manager?.level ?? "",
            TurnIndex = Math.Max(1, turnIndex),
            PlayerPower = FightPlayer.Instance?.CurPowerCount ?? 0,
            PlayerMaxPower = FightPlayer.Instance?.MaxPowerCount ?? 0
        };
        if (manager == null) return result;

        var enemySlots = (EnemyManager.Instance?.enemyList ?? new List<Enemy>())
            .Where(item => item?.Status != null)
            .Select((item, index) => (item.Status.InstanceId ?? "", index))
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .ToDictionary(item => item.Item1, item => item.index, StringComparer.Ordinal);
        var actorOrder = 0;
        foreach (var pair in manager.statuses.Where(item => item.Value != null)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var status = pair.Value;
            var actor = CaptureActor(pair.Key ?? "", status, actorOrder++, enemySlots, catalog);
            if (!string.IsNullOrWhiteSpace(actor.InstanceId)) result.Actors.Add(actor);
        }

        result.ActiveActorId = result.Actors.FirstOrDefault(item =>
            string.Equals(item.EntityKind, ReplayEntityKindsV11.Player, StringComparison.Ordinal))?.InstanceId ?? "";
        CaptureCards(result, catalog);
        result.Intents = ReplayIntentCaptureV11.CapturePlans(catalog);
        return result;
    }

    internal static ReplayActionSourceV11 CaptureActionSource(object? target, ReplayContentCatalogBuilderV11 catalog)
    {
        var config = target switch
        {
            CardItem card => card.dataConfig,
            SkillItem skill => skill.dataConfig,
            _ => null
        };
        var kind = target is SkillItem ? "Skill" : "Card";
        var content = catalog.Register(kind, config, Read(config?.data, "Id"));
        return new ReplayActionSourceV11
        {
            ActorId = target switch
            {
                CardItem card => card.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
                SkillItem skill => skill.status?.InstanceId ?? FightPlayer.Instance?.Status?.InstanceId ?? "",
                _ => FightPlayer.Instance?.Status?.InstanceId ?? ""
            },
            SourceInstanceId = config?.InstanceID ?? Read(config?.Vars, "InstanceID"),
            Content = content,
            Label = First(Read(config?.Vars, "Name"), Read(config?.data, "Name"), Read(config?.data, "DisplayName"), content.StableContentId),
            PresentationKind = target is SkillItem ? ReplayPresentationKindsV11.Skill : ReplayPresentationKindsV11.Card,
            ActionState = First(Read(config?.Vars, "Action"), Read(config?.data, "Action"), "Idle"),
            EffectName = First(Read(config?.Vars, "Effects"), Read(config?.data, "Effects"))
        };
    }

    private static ReplayActorStateV11 CaptureActor(
        string instanceId,
        StatusManager status,
        int fallbackSlot,
        IReadOnlyDictionary<string, int> enemySlots,
        ReplayContentCatalogBuilderV11 catalog)
    {
        var isEnemy = status.fatherObject is Enemy;
        var isPlayer = status.fatherObject is FightPlayer;
        var remotePlayer = status.fatherObject as OtherPlayer;
        var partner = status.fatherObject as Partner;
        var config = (status.fatherObject as Enemy)?.dataConfig ?? partner?.dataConfig;
        var remoteRole = remotePlayer == null
            ? null
            : FightManager.Instance?.roleQueue?.FirstOrDefault(item =>
                string.Equals(item.InstanceId, remotePlayer.InstanceId, StringComparison.Ordinal))?.career;
        var stableId = isEnemy
            ? Read(config?.data, "Id").Replace("*", "")
            : partner != null
                ? Read(config?.data, "Id").Replace("*", "")
            : isPlayer
                ? First(Read(RoleTable.Instance?.Career?.data, "Id"), RoleTable.Instance?.Id ?? "player")
                : First(Read(remoteRole?.data, "Id"), remotePlayer?.Id ?? instanceId);
        var contentKind = isEnemy ? "Enemy" : partner != null ? "Partner" : "Role";
        var contentConfig = isPlayer ? RoleTable.Instance?.Career : remoteRole ?? config;
        var content = catalog.Register(contentKind, contentConfig, stableId);
        var actor = new ReplayActorStateV11
        {
            InstanceId = instanceId,
            Content = content,
            EntityKind = isEnemy
                ? ReplayEntityKindsV11.Enemy
                : partner != null
                    ? ReplayEntityKindsV11.Summon
                : isPlayer
                    ? ReplayEntityKindsV11.Player
                    : ReplayEntityKindsV11.RemotePlayer,
            Team = isEnemy ? ReplayTeamsV11.Enemy : ReplayTeamsV11.Friendly,
            OwnerPlayerId = isPlayer
                ? RoleTable.Instance?.Id ?? ""
                : remotePlayer?.InstanceId ?? "",
            SlotIndex = enemySlots.TryGetValue(instanceId, out var slot) ? slot : fallbackSlot,
            MaxHp = status.maxHp,
            CurrentHp = status.curHp,
            Defense = status.defend,
            State = status.state.ToString()
        };
        foreach (var value in status.dynamicVariables.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            actor.Variables.Add(new ReplayIntValueV11
            {
                Key = (value.Key ?? "") + ".float32bits",
                Value = BitConverter.ToInt32(BitConverter.GetBytes(value.Value), 0)
            });
        }

        foreach (var buff in (status.GetBuffs() ?? Array.Empty<IBuffItem>())
                     .Where(item => item?.buffConfig != null)
                     .OrderBy(item => item.buffConfig.BuffId, StringComparer.Ordinal))
        {
            var configValue = buff.buffConfig;
            var buffId = configValue.BuffId ?? "";
            var dataConfig = configValue.dataConfig;
            actor.Buffs.Add(new ReplayBuffStateV11
            {
                InstanceId = instanceId + "|" + buffId,
                Content = catalog.Register("Buff", dataConfig, buffId),
                Level = configValue.Level,
                UpperBound = configValue.UpperBound,
                ReducePerTurn = configValue.ReducePerTurn,
                ReducePerUse = configValue.ReducePerUse,
                ReducePerAttacked = configValue.ReducePerAttacked,
                Values = CaptureDisplayValues(dataConfig?.data, dataConfig?.Vars)
            });
        }

        return actor;
    }

    private static void CaptureCards(ReplayLogicalStateV11 target, ReplayContentCatalogBuilderV11 catalog)
    {
        AddCards(target.Cards, "Draw", FightCardManager.Instance?.cardList, catalog);
        AddCards(target.Cards, "Discard", FightCardManager.Instance?.usedCardList, catalog);
        AddCards(target.Cards, "Nascent", FightCardManager.Instance?.nascentList, catalog);
        var hand = FightUI.cardItemList ?? new List<CardItem>();
        var order = 0;
        foreach (var item in hand.Where(item => item?.dataConfig != null))
        {
            target.Cards.Add(CaptureCard("Hand", order++, item.dataConfig, catalog));
        }

        var fightUi = Witch.UI.UIManager.Instance?.GetUI<FightUI>("FightUI");
        target.CardTopCount = fightUi?.CardTopCount ?? 0;
    }

    private static void AddCards(
        ICollection<ReplayCardStateV11> target,
        string zone,
        IEnumerable<DataConfig>? source,
        ReplayContentCatalogBuilderV11 catalog)
    {
        var order = 0;
        foreach (var config in source ?? Enumerable.Empty<DataConfig>())
        {
            if (config != null) target.Add(CaptureCard(zone, order++, config, catalog));
        }
    }

    private static ReplayCardStateV11 CaptureCard(
        string zone,
        int order,
        DataConfig config,
        ReplayContentCatalogBuilderV11 catalog)
    {
        var stableId = Read(config.data, "Id");
        var values = CaptureDisplayValues(config.data, config.Vars);
        foreach (var pair in AuraToolsCardVisualRuntime.CaptureReplaySnapshot(config))
        {
            values.RemoveAll(value => string.Equals(value.Key, pair.Key, StringComparison.Ordinal));
            values.Add(new ReplayStringValueV11 { Key = pair.Key, Value = pair.Value });
        }
        return new ReplayCardStateV11
        {
            InstanceId = config.InstanceID ?? Read(config.Vars, "InstanceID"),
            Content = catalog.Register("Card", config, stableId),
            Zone = zone,
            Order = order,
            DisplayedCost = ParseInt(First(Read(config.Vars, "Expend"), Read(config.data, "Expend"))),
            Values = values
        };
    }

    internal static List<ReplayStringValueV11> CaptureDisplayValues(
        IDictionary<string, string>? data,
        IDictionary<string, string>? vars)
    {
        var keys = new[]
        {
            "Name", "DisplayName", "Description", "Description1", "Description2", "Tag", "Type",
            "Expend", "DesVal1", "DesVal2", "DesVal3", "Rarity", "Action", "Effects",
            "Icon", "BackIcon", "Color", "PackBelong"
        };
        var result = new List<ReplayStringValueV11>();
        foreach (var key in keys)
        {
            var value = First(Read(vars, key), Read(data, key));
            if (!string.IsNullOrWhiteSpace(value)) result.Add(new ReplayStringValueV11 { Key = key, Value = value });
        }

        return result;
    }

    internal static string Read(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    internal static string First(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}

internal sealed class ReplayActionSourceV11
{
    internal string ActorId { get; set; } = "";

    internal string SourceInstanceId { get; set; } = "";

    internal ReplayContentRefV11 Content { get; set; } = new();

    internal string Label { get; set; } = "";

    internal string PresentationKind { get; set; } = ReplayPresentationKindsV11.Notice;

    internal string ActionState { get; set; } = "Idle";

    internal string EffectName { get; set; } = "";
}

internal sealed class ReplayContentCatalogBuilderV11
{
    private readonly Dictionary<string, ReplayContentDefinitionV11> definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayAttachmentV11> attachments = new(StringComparer.OrdinalIgnoreCase);

    internal ReplayContentManifestV11 Manifest
    {
        get
        {
            var ordered = definitions.Values.OrderBy(item => item.Content.Key, StringComparer.Ordinal).ToList();
            return new ReplayContentManifestV11
            {
                Definitions = ordered,
                Dependencies = ordered
                    .GroupBy(item => item.Content.OwnerModId, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new ReplayContentDependencyV11
                    {
                        OwnerModId = group.Key,
                        Version = ResolveOwnerVersion(group.Key),
                        ManifestSha256 = ReplayCanonicalJsonV11.Sha256(group.ToList()),
                        Files = group.SelectMany(item => new[]
                            {
                                item.Display.IconAssetSha256,
                                item.Display.PortraitAssetSha256,
                                item.Display.ArtworkAssetSha256,
                                item.Display.BackgroundAssetSha256
                            })
                            .Where(hash => !string.IsNullOrWhiteSpace(hash) && attachments.ContainsKey(hash))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(hash => hash, StringComparer.OrdinalIgnoreCase)
                            .Select(hash => new ReplayContentFileHashV11
                            {
                                LogicalPath = "attachment:" + hash,
                                Sha256 = hash,
                                ByteLength = attachments[hash].ByteLength
                            })
                            .ToList()
                    })
                    .ToList()
            };
        }
    }

    internal List<ReplayAttachmentV11> Attachments => attachments.Values
        .OrderBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
        .ToList();

    internal string RegisterAttachment(ReplayAttachmentV11? attachment)
    {
        if (attachment == null || string.IsNullOrWhiteSpace(attachment.Sha256)) return "";
        attachments[attachment.Sha256] = attachment;
        return attachment.Sha256;
    }

    internal ReplayContentRefV11 Register(string contentKind, IDataConfig? config, string stableId)
    {
        var id = string.IsNullOrWhiteSpace(stableId)
            ? ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(config?.data, "Id"),
                ReplayFactCaptureV11.Read(config?.Vars, "Id"),
                "unknown")
            : stableId.Trim();
        var content = new ReplayContentRefV11
        {
            OwnerModId = Owner(id),
            ContentKind = contentKind ?? "",
            StableContentId = id
        };
        if (!definitions.ContainsKey(content.Key))
        {
            var display = CaptureDisplay(config, contentKind ?? "");
            if (string.IsNullOrWhiteSpace(display.IconAssetSha256)
                && string.IsNullOrWhiteSpace(display.PortraitAssetSha256)
                && string.IsNullOrWhiteSpace(display.ArtworkAssetSha256))
            {
                display.IconAssetSha256 = CaptureFallbackTexture(id, (contentKind ?? "Content") + ".Fallback", 64, 64);
            }
            definitions[content.Key] = new ReplayContentDefinitionV11
            {
                Content = content,
                Display = display
            };
        }

        return new ReplayContentRefV11
        {
            OwnerModId = content.OwnerModId,
            ContentKind = content.ContentKind,
            StableContentId = content.StableContentId
        };
    }

    internal string CaptureBackground(GameObject? background)
    {
        if (background == null) return "";
        try
        {
            var sprite = background.GetComponentsInChildren<SpriteRenderer>(includeInactive: true)
                .Select(item => item?.sprite)
                .OfType<Sprite>()
                .OrderByDescending(item => item.rect.width * item.rect.height)
                .FirstOrDefault();
            return sprite == null ? "" : CaptureTexture(sprite.texture, "Background", required: true);
        }
        catch
        {
            return "";
        }
    }

    internal ReplayContentRefV11 RegisterBackground(string levelId, string sceneName, GameObject? background)
    {
        var content = Register("Level", null, string.IsNullOrWhiteSpace(levelId) ? "unknown-level" : levelId);
        var definition = definitions[content.Key];
        definition.Display.Name = string.IsNullOrWhiteSpace(sceneName) ? levelId ?? "" : sceneName;
        definition.Display.BackgroundAssetSha256 = CaptureBackground(background);
        if (string.IsNullOrWhiteSpace(definition.Display.BackgroundAssetSha256))
        {
            definition.Display.BackgroundAssetSha256 = CaptureFallbackTexture(
                levelId ?? "level",
                "Level.BackgroundFallback",
                64,
                36);
        }
        return content;
    }

    private ReplayDisplaySnapshotV11 CaptureDisplay(IDataConfig? config, string contentKind)
    {
        var data = config?.data;
        var vars = config?.Vars;
        var display = new ReplayDisplaySnapshotV11
        {
            Name = ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "Name"),
                ReplayFactCaptureV11.Read(data, "Name"),
                ReplayFactCaptureV11.Read(data, "DisplayName"),
                ReplayFactCaptureV11.Read(data, "Id")),
            Subtitle = ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "Tag"),
                ReplayFactCaptureV11.Read(data, "Tag"),
                contentKind),
            Description = ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "Description"),
                ReplayFactCaptureV11.Read(data, "Description"),
                string.Join("\n", new[]
                {
                    ReplayFactCaptureV11.Read(data, "Description1"),
                    ReplayFactCaptureV11.Read(data, "Description2")
                }.Where(value => !string.IsNullOrWhiteSpace(value)))),
            RulesText = ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "RulesText"),
                ReplayFactCaptureV11.Read(data, "RulesText")),
            AccentColor = ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "Color"),
                ReplayFactCaptureV11.Read(data, "Color")),
            Values = ReplayFactCaptureV11.CaptureDisplayValues(data, vars)
        };
        display.IconAssetSha256 = CaptureResource(
            ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "Icon"),
                ReplayFactCaptureV11.Read(data, "Icon"),
                ReplayFactCaptureV11.Read(data, "BackIcon")),
            contentKind + ".Icon");
        display.PortraitAssetSha256 = CaptureResource(
            ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "Portrait"),
                ReplayFactCaptureV11.Read(data, "Portrait"),
                ReplayFactCaptureV11.Read(data, "Image")),
            contentKind + ".Portrait");
        display.ArtworkAssetSha256 = CaptureResource(
            ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(vars, "CardImage"),
                ReplayFactCaptureV11.Read(data, "CardImage"),
                ReplayFactCaptureV11.Read(vars, "Picture"),
                ReplayFactCaptureV11.Read(data, "Picture")),
            contentKind + ".Artwork");
        return display;
    }

    private string CaptureResource(string resourcePath, string usage)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return "";
        try
        {
            var texture = ResourceLoader.Load<Texture2D>(resourcePath, true);
            if (texture != null) return CaptureTexture(texture, usage, required: true);
            var sprite = ResourceLoader.Load<Sprite>(resourcePath, true);
            if (sprite != null) return CaptureTexture(sprite.texture, usage, required: true);
            var sprites = ResourceLoader.LoadAll<Sprite>(resourcePath);
            var first = sprites?.FirstOrDefault(item => item != null);
            return first == null ? "" : CaptureTexture(first.texture, usage, required: true);
        }
        catch
        {
            return "";
        }
    }

    private string CaptureTexture(Texture texture, string usage, bool required)
    {
        if (texture == null) return "";
        Texture2D? readable = null;
        RenderTexture? temporary = null;
        var previous = RenderTexture.active;
        try
        {
            // Readable compressed Texture2D instances still cannot be passed
            // to EncodeToPNG. Normalize every source through an RGBA32 render
            // target so compressed, atlas, render, and ordinary textures share
            // one deterministic capture path.
            temporary = RenderTexture.GetTemporary(
                Math.Max(1, texture.width),
                Math.Max(1, texture.height),
                0,
                RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(
                Math.Max(1, texture.width),
                Math.Max(1, texture.height),
                TextureFormat.RGBA32,
                mipChain: false);
            readable.ReadPixels(
                new Rect(0f, 0f, readable.width, readable.height),
                0,
                0,
                recalculateMipMaps: false);
            readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            var payload = readable.EncodeToPNG();
            var hash = ReplayCanonicalJsonV11.Sha256(payload);
            if (!attachments.ContainsKey(hash))
            {
                attachments[hash] = new ReplayAttachmentV11
                {
                    Sha256 = hash,
                    MediaType = "image/png",
                    Extension = ".png",
                    Usage = usage ?? "Image",
                    ByteLength = payload.LongLength,
                    Width = readable.width,
                    Height = readable.height,
                    Required = required,
                    Payload = payload
                };
            }

            return hash;
        }
        finally
        {
            RenderTexture.active = previous;
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            if (readable != null) Object.Destroy(readable);
        }
    }

    private string CaptureFallbackTexture(string identity, string usage, int width, int height)
    {
        var texture = new Texture2D(Math.Max(2, width), Math.Max(2, height), TextureFormat.RGBA32, mipChain: false);
        try
        {
            var seed = ReplayCanonicalJsonV11.Sha256Text(identity ?? "fallback");
            var red = byte.Parse(seed.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var green = byte.Parse(seed.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var blue = byte.Parse(seed.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var primary = new Color32((byte)(48 + red / 2), (byte)(48 + green / 2), (byte)(48 + blue / 2), 255);
            var secondary = new Color32((byte)(24 + red / 3), (byte)(24 + green / 3), (byte)(24 + blue / 3), 255);
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    texture.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? primary : secondary);
                }
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return CaptureTexture(texture, usage, required: true);
        }
        finally
        {
            Object.Destroy(texture);
        }
    }

    private static string Owner(string stableId)
    {
        var value = (stableId ?? "").Trim();
        if (value.StartsWith("card_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("enemy_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("buff_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("career_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("role_", StringComparison.OrdinalIgnoreCase))
        {
            return "Witch";
        }

        var separator = value.IndexOfAny(new[] { '.', ':', '_' });
        return separator > 0 ? value.Substring(0, separator) : "Witch";
    }

    private static string ResolveOwnerVersion(string owner)
    {
        if (string.Equals(owner, "Witch", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item =>
            string.Equals(item.GetName().Name, owner, StringComparison.OrdinalIgnoreCase)
            || (item.GetName().Name ?? "").StartsWith(owner + ".", StringComparison.OrdinalIgnoreCase));
        return assembly?.GetName().Version?.ToString() ?? "unknown";
    }
}

internal static class ReplayIntentCaptureV11
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SelectedCardsField = typeof(ObjectAction).GetField("CardList", InstanceFields);

    internal static ReplayIntentStateV11? CaptureExecuting(
        object? target,
        object[]? arguments,
        ReplayContentCatalogBuilderV11 catalog)
    {
        if (target is not Enemy enemy || enemy.Status == null) return null;
        var slot = arguments != null && arguments.Length > 0 && arguments[0] is int value ? Math.Max(0, value) : 0;
        var card = enemy.FightAction?.TryGetCard();
        if (card == null && enemy.ActionCards != null && slot < enemy.ActionCards.Count) card = enemy.ActionCards[slot];
        return Capture(enemy, card, slot, catalog);
    }

    internal static List<ReplayIntentStateV11> CapturePlans(ReplayContentCatalogBuilderV11 catalog)
    {
        var result = new List<ReplayIntentStateV11>();
        foreach (var enemy in (EnemyManager.Instance?.enemyList ?? new List<Enemy>())
                     .Where(item => item?.Status != null)
                     .OrderBy(item => item.Status.InstanceId, StringComparer.Ordinal))
        {
            var cards = SelectedCards(enemy);
            for (var index = 0; index < cards.Count; index++)
            {
                var intent = Capture(enemy, cards[index], index, catalog);
                if (intent != null) result.Add(intent);
            }
        }

        return result;
    }

    private static List<ObjectCard> SelectedCards(Enemy enemy)
    {
        try
        {
            if (enemy.FightAction != null && SelectedCardsField?.GetValue(enemy.FightAction) is IEnumerable selected)
            {
                return selected.Cast<object>().OfType<ObjectCard>().Where(item => item != null).ToList();
            }
        }
        catch
        {
        }

        return (enemy.ActionCards ?? new List<ObjectCard>()).Where(item => item != null).ToList();
    }

    private static ReplayIntentStateV11? Capture(
        Enemy enemy,
        ObjectCard? card,
        int slot,
        ReplayContentCatalogBuilderV11 catalog)
    {
        var config = card?.dataConfig;
        if (config == null || enemy.Status == null) return null;
        var stableId = ReplayFactCaptureV11.First(
            ReplayFactCaptureV11.Read(config.data, "Id"),
            ReplayFactCaptureV11.Read(config.Vars, "Id"));
        return new ReplayIntentStateV11
        {
            InstanceId = config.InstanceID ?? enemy.Status.InstanceId + "|intent|" + slot,
            ActorId = enemy.Status.InstanceId ?? enemy.InstanceId ?? "",
            Content = catalog.Register("EnemyIntent", config, stableId),
            SlotIndex = Math.Max(0, slot),
            DisplayValue = ReplayFactCaptureV11.First(
                ReplayFactCaptureV11.Read(config.Vars, "DesVal1"),
                ReplayFactCaptureV11.Read(config.data, "DesVal1")),
            TargetIds = (config.scriptExecutor?.Object ?? new List<IStatusManager>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InstanceId))
                .Select(item => item.InstanceId)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }
}
