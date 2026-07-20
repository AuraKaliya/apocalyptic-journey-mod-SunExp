using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.Application;
using AuraGameData.Shared.GameApi;
using Fight.ActionCommand;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssCurseService
{
    public const string TemporaryCombatCurseMarker = "SunExpTemporaryAbyssCurse";
    private const string TemporaryCombatCardMarker = "SunExpTemporaryCombatCard";
    private const string TemporaryCombatSourceKey = "SunExpTemporarySource";
    private static int suppressGazeCardGain;
    private static IReadOnlyList<string>? randomCursePoolCache;

    private static readonly HashSet<string> CurseCardIds = new(StringComparer.Ordinal)
    {
        "abyss_life_theft",
        "abyss_deficit"
    };

    public static bool SuppressGazeCardGain => suppressGazeCardGain > 0;

    public static bool IsCurseCard(string id)
    {
        return CurseCardIds.Contains(Normalize(id));
    }

    public static void Init(ScriptExecutor self, string id)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        var normalized = Normalize(id);
        if (normalized == "abyss_life_theft")
        {
            ExecutorApi.AddValueDescription(self, "1", 3);
            ExecutorApi.AddValueDescription(self, "2", 5);
            ExecutorApi.AddValueDescription(self, "3", 2);
        }
        else if (normalized == "abyss_deficit")
        {
            ExecutorApi.AddValueDescription(self, "1", 1);
        }
    }

    public static void Draw(ScriptExecutor self, string id)
    {
        switch (Normalize(id))
        {
            case "abyss_life_theft":
                LosePlayerCurrentHpByMaxPercent(self, 3, "draw");
                IncreaseAllEnemyMaxHpPercent(self, 5);
                break;
            case "abyss_deficit":
                self.SetStatus("Self");
                self.ChangePower("-1");
                break;
        }
    }

    public static void Drop(ScriptExecutor self, string id)
    {
        if (Normalize(id) == "abyss_life_theft")
        {
            LosePlayerCurrentHpByMaxPercent(self, 2, "drop");
        }
    }

    public static int AddCurseCardsToDeck(ScriptExecutor self, string cardId, int count)
    {
        if (self == null || count <= 0)
        {
            return 0;
        }

        var resolved = CardApi.ResolveCardId(cardId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return 0;
        }

        var added = 0;
        for (var i = 0; i < count; i++)
        {
            if (TryAddCurseToLocalDeck(resolved, "AddCurseCardsToDeck"))
            {
                added++;
            }
        }

        return added;
    }

    public static bool AddRandomCurseToHand(ScriptExecutor self, string source)
    {
        if (self == null)
        {
            return false;
        }

        var pool = RandomCursePool();
        if (pool.Count == 0)
        {
            return false;
        }

        var id = pool[PickIndex(pool.Count, source)];
        return CardApi.AddCardToHand(self, id);
    }

    public static bool AddRandomCurseToDeck(ScriptExecutor self, string source)
    {
        if (self == null)
        {
            return false;
        }

        var pool = RandomCursePool();
        if (pool.Count == 0)
        {
            return false;
        }

        var id = pool[PickIndex(pool.Count, source)];
        return TryAddCurseToLocalDeck(id, source);
    }

    public static bool AddRandomCurseToCombatDeck(ScriptExecutor? self, string source)
    {
        var pool = RandomCursePool();
        if (pool.Count == 0)
        {
            return false;
        }

        var id = pool[PickIndex(pool.Count, source)];
        return TryAddCurseToCombatDeck(self, id, source);
    }

    public static bool AddRandomCurseToLocalDeck(string source)
    {
        var pool = RandomCursePool();
        if (pool.Count == 0)
        {
            return false;
        }

        var id = pool[PickIndex(pool.Count, source)];
        return TryAddCurseToLocalDeck(id, source);
    }

    public static int CleanupTemporaryCombatCurses(string source)
    {
        var removed = 0;
        try
        {
            removed += RemoveTemporaryCurses(FightCardManager.Instance?.cardList);
            removed += RemoveTemporaryCurses(FightCardManager.Instance?.usedCardList);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssCurse] temporary curse cleanup failed from " + source + ": " + ex.Message);
        }

        if (removed > 0)
        {
            SunExpLog.Info("[EndlessAbyssCurse] cleaned temporary combat curses: " + removed + " from " + source + ".");
        }

        return removed;
    }

    private static bool TryAddCurseToLocalDeck(string cardId, string source)
    {
        try
        {
            suppressGazeCardGain++;
            if (PlayerApi.TryAddCardToDeck(cardId, out var granted, out var message))
            {
                SunExpLog.Info("[EndlessAbyssCurse] added curse to local deck: "
                    + granted
                    + " from "
                    + source
                    + ".");
                return true;
            }

            SunExpLog.Warn("[EndlessAbyssCurse] add curse to local deck failed from "
                + source
                + ": "
                + message);
            return false;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssCurse] add curse to local deck failed from " + source + ": " + ex.Message);
            return false;
        }
        finally
        {
            suppressGazeCardGain = Math.Max(0, suppressGazeCardGain - 1);
        }
    }

    private static bool TryAddCurseToCombatDeck(ScriptExecutor? self, string cardId, string source)
    {
        try
        {
            suppressGazeCardGain++;
            var resolved = CardApi.ResolveCardId(cardId);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return false;
            }

            var drawPile = FightCardManager.Instance?.cardList;
            if (drawPile == null)
            {
                SunExpLog.Warn("[EndlessAbyssCurse] combat draw pile missing from " + source + ".");
                return false;
            }

            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, resolved);
            if (handle == null)
            {
                SunExpLog.Warn("[EndlessAbyssCurse] registered card definition missing from " + source + ".");
                return false;
            }

            var result = new AuraCardInstanceService(new WitchCardInstancePort())
                .GrantToManagerDraw(new AuraCardGrantCommand
                {
                    Definition = handle,
                    Vars = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [SunExpIds.RuntimeMarkersKey] = TemporaryCombatCardMarker + "," + TemporaryCombatCurseMarker,
                        [TemporaryCombatSourceKey] = "AbyssGaze",
                        ["SunExpRuntimeCreatedAt"] = source ?? ""
                    },
                    Context = new AuraGameMutationContext
                    {
                        RequesterModId = SunExpIds.ModId,
                        Source = source ?? "",
                        Authoritative = true
                    }
                });
            if (!result.Success || result.Instance == null)
            {
                SunExpLog.Warn("[EndlessAbyssCurse] shared card grant failed from " + source + ": " + result.FailureStep + ": " + result.Message);
                return false;
            }

            var config = drawPile.FirstOrDefault(card => string.Equals(card.InstanceID, result.Instance.InstanceId, StringComparison.Ordinal));
            if (config == null)
            {
                SunExpLog.Warn("[EndlessAbyssCurse] granted card could not be located from " + source + ".");
                return false;
            }

            RandomizeLastCardIntoDrawPile(drawPile, source ?? "");
            TryPlayCombatDeckAddAnimation(config);
            SunExpLog.Info("[EndlessAbyssCurse] added temporary curse to combat deck: "
                + resolved
                + " from "
                + source
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssCurse] add temporary curse to combat deck failed from " + source + ": " + ex.Message);
            return false;
        }
        finally
        {
            suppressGazeCardGain = Math.Max(0, suppressGazeCardGain - 1);
        }
    }

    private static void RandomizeLastCardIntoDrawPile(IList<DataConfig> drawPile, string source)
    {
        if (drawPile.Count <= 1)
        {
            return;
        }

        var lastIndex = drawPile.Count - 1;
        var targetIndex = PickIndex(drawPile.Count, source);
        if (targetIndex == lastIndex)
        {
            return;
        }

        (drawPile[lastIndex], drawPile[targetIndex]) = (drawPile[targetIndex], drawPile[lastIndex]);
    }

    private static void TryPlayCombatDeckAddAnimation(DataConfig config)
    {
        try
        {
            var ui = UIManager.Instance?.GetUI<FightUI>("FightUI");
            if (ui == null)
            {
                return;
            }

            ui.DoCardUseAnimation(new UseCard.CardUseData
            {
                cardData = config,
                isBurning = false
            }, false, true);
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[EndlessAbyssCurse] temporary curse animation skipped: " + ex.Message);
        }
    }

    private static int RemoveTemporaryCurses(IList<DataConfig>? cards)
    {
        if (cards == null)
        {
            return 0;
        }

        var removed = 0;
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var config = cards[i];
            if (!CardMutationService.HasRuntimeMarker(config, TemporaryCombatCurseMarker))
            {
                continue;
            }

            cards.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    private static IReadOnlyList<string> RandomCursePool()
    {
        if (randomCursePoolCache != null)
        {
            return randomCursePoolCache;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            randomCursePoolCache = SunExpConfigIndex.Rows(DataType.Card)
                .Select(row => new
                {
                    Id = DictionaryUtil.Get(row, "Id"),
                    Type = DictionaryUtil.Get(row, "Type"),
                    Tag = DictionaryUtil.Get(row, "Tag")
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.Id)
                    && !row.Id.StartsWith("*", StringComparison.Ordinal)
                    && (DictionaryUtil.ContainsToken(row.Tag, "Curse")
                        || row.Type.Contains("诅咒")
                        || row.Type.Contains("詛咒")
                        || row.Type.IndexOf("curse", StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(row => CardApi.ResolveCardId(row.Id))
                .Where(id => !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                .ToList();
            return randomCursePoolCache;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssCurse] curse pool failed: " + ex.Message);
            randomCursePoolCache = new List<string>
            {
                SunExpIds.AbyssLifeTheftCardId,
                SunExpIds.AbyssDeficitCardId
            };
            return randomCursePoolCache;
        }
    }

    private static void LosePlayerCurrentHpByMaxPercent(ScriptExecutor self, int percent, string source)
    {
        var status = self?.Self ?? FightPlayer.Instance?.Status;
        if (self == null || status == null || percent <= 0)
        {
            return;
        }

        var damage = Math.Max(1, Math.Max(1, status.MaxHp) * percent / 100);
        var current = Math.Max(1, status.CurHp);
        var loss = Math.Min(current - 1, damage);
        if (loss <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.ChangeHp((-loss).ToString());
        SunExpLog.Debug("[EndlessAbyssCurse] life theft " + source + " hp -" + loss + ".");
    }

    private static void IncreaseAllEnemyMaxHpPercent(ScriptExecutor self, int percent)
    {
        foreach (var status in ExecutorApi.EnemyTargets(self))
        {
            if (status == null || status.CurHp <= 0)
            {
                continue;
            }

            var oldMax = Math.Max(1, status.MaxHp);
            var add = Math.Max(1, (int)Math.Ceiling(oldMax * percent / 100.0));
            status.MaxHp = Math.Max(1, oldMax + add);
            status.CurHp = Math.Max(1, status.CurHp + add);
            status.UpdateStatus(true);
        }
    }

    private static int PickIndex(int count, string seed)
    {
        if (count <= 1)
        {
            return 0;
        }

        return StableHash(seed + ":" + Environment.TickCount) % count;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value ?? "")
            {
                hash = hash * 31 + ch;
            }

            return Math.Abs(hash == int.MinValue ? int.MaxValue : hash);
        }
    }

    private static string Normalize(string id)
    {
        var value = (id ?? "").Replace("*", "").Trim();
        foreach (var prefix in new[] { "SunExp_sunexp_", "SunExp_cursecard_" })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value.Substring(prefix.Length);
            }
        }

        return value;
    }
}
