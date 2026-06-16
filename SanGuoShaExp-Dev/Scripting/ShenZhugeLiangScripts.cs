using System;
using System.Collections.Generic;
using System.Linq;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class ShenZhugeLiangScripts
{
    private const int QixingPileSize = 7;
    private const int GaleVulnerability = 10;
    private const int MistFatePerLostStar = 5;
    private const int MistCheckCount = 3;

    private static readonly string[] QixingSkillIds =
    {
        SanGuoShaExpIds.QixingCardId,
        "*qixing",
        "qixing"
    };

    private static readonly Dictionary<string, List<DataConfig>> QixingPiles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> QixingInitialCounts = new(StringComparer.Ordinal);

    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, SanGuoShaExpIds.CareerToken, "0")) + 1).ToString();
            self.SetStatus("Self");
            SetQixingSkillReady(self);

            var fightStartRegistered = ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, SanGuoShaExpIds.CareerToken, token))
                {
                    SetQixingSkillReady(self);
                    BuildQixingPile(self);
                }
            }), "shen_zhugeliang_career");

            var endRoundRegistered = ExecutorApi.TryAddEvent(self, "EndRound", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, SanGuoShaExpIds.CareerToken, token))
                {
                    ResolveEndRoundPassives(self);
                }
            }), "shen_zhugeliang_career");

            ExecutorApi.TryAddEvent(self, "Win", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, SanGuoShaExpIds.CareerToken, token))
                {
                    ClearCareerState(self);
                }
            }), "shen_zhugeliang_career");

            ExecutorApi.TryAddEvent(self, "Escape", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, SanGuoShaExpIds.CareerToken, token))
                {
                    ClearCareerState(self);
                }
            }), "shen_zhugeliang_career");

            if (fightStartRegistered && endRoundRegistered)
            {
                ExecutorApi.SetVar(self, SanGuoShaExpIds.CareerHook, "1");
                ExecutorApi.SetVar(self, SanGuoShaExpIds.CareerToken, token);
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Shen Zhuge Liang InitCareer failed", ex);
        }
    }

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
            self.AddDescription("1", "Value", QixingPileSize.ToString());
            SetQixingSkillReady(self);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Shen Zhuge Liang Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            SetQixingSkillReady(self);
            UseQixing(self);
            SetQixingSkillReady(self);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Shen Zhuge Liang Use failed: " + id, ex);
        }
    }

    private static void BuildQixingPile(ScriptExecutor self)
    {
        var key = CombatKey(self);
        var pile = QixingPiles[key] = new List<DataConfig>();
        QixingInitialCounts[key] = 0;
        var deck = FightCardManager.Instance?.cardList;
        if (deck == null || deck.Count == 0)
        {
            SyncQixingBuff(self);
            return;
        }

        var drawCount = Math.Min(QixingPileSize, deck.Count);
        QixingInitialCounts[key] = drawCount;
        var availableIndexes = Enumerable.Range(0, deck.Count).ToList();
        for (var i = 0; i < drawCount; i++)
        {
            var index = UnityEngine.Random.Range(0, availableIndexes.Count);
            var card = deck[availableIndexes[index]];
            availableIndexes.RemoveAt(index);
            pile.Add(CopyCardConfig(card));
        }

        SyncQixingBuff(self);
        SanGuoShaExpLog.Debug("Qixing pile built: count=" + pile.Count);
    }

    private static DataConfig CopyCardConfig(DataConfig card)
    {
        var cardId = card.data.GetValueOrDefault("Id", card.InstanceID);
        return new DataConfig(cardId, DataType.Card);
    }

    private static void UseQixing(ScriptExecutor self)
    {
        var pile = CurrentPile(self);
        if (pile.Count == 0)
        {
            ShowCaption("七星卡堆已空。");
            return;
        }

        if (self.HandCard == null || self.HandCard.Count == 0)
        {
            ShowCaption("没有可替换的手牌。");
            return;
        }

        self.ChooseCardToAction("1", selectedHands =>
        {
            var hand = selectedHands?.FirstOrDefault();
            if (hand == null)
            {
                return;
            }

            self.PackToDeckAction("1", pile.Cast<IDataConfig>().ToList(), selectedQixing =>
            {
                var selected = selectedQixing?.FirstOrDefault() as DataConfig;
                if (selected == null)
                {
                    return;
                }

                hand.Burning(0f);
                pile.RemoveAll(card => card.InstanceID == selected.InstanceID);
                self.CreateCard(selected);
                SyncQixingBuff(self);
                SanGuoShaExpLog.Debug("Qixing replaced hand card with " + selected.data.GetValueOrDefault("Id", selected.InstanceID));
            });
        });
    }

    private static void ResolveEndRoundPassives(ScriptExecutor self)
    {
        BurnRandomQixingCard(self);

        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            ExecutorApi.AddStatusBuff(self, target, SanGuoShaExpIds.Vulnerability, GaleVulnerability);
        }

        self.SetStatus("Self");
        var lostStars = LostQixingStars(self);
        var fateGain = lostStars * MistFatePerLostStar;
        if (fateGain > 0)
        {
            self.AddBuff(SanGuoShaExpIds.Fate, fateGain.ToString());
        }

        RollMistChecks(self);
    }

    private static void BurnRandomQixingCard(ScriptExecutor self)
    {
        var pile = CurrentPile(self);
        if (pile.Count == 0)
        {
            SyncQixingBuff(self);
            return;
        }

        var index = UnityEngine.Random.Range(0, pile.Count);
        var burned = pile[index];
        pile.RemoveAt(index);
        SyncQixingBuff(self);
        SanGuoShaExpLog.Debug("Qixing burned: " + burned.data.GetValueOrDefault("Id", burned.InstanceID));
    }

    private static List<DataConfig> CurrentPile(ScriptExecutor self)
    {
        var key = CombatKey(self);
        if (!QixingPiles.TryGetValue(key, out var pile))
        {
            pile = new List<DataConfig>();
            QixingPiles[key] = pile;
        }

        return pile;
    }

    private static int LostQixingStars(ScriptExecutor self)
    {
        var key = CombatKey(self);
        var current = CurrentPile(self).Count;
        if (!QixingInitialCounts.TryGetValue(key, out var initial))
        {
            initial = current;
        }

        return Math.Max(0, initial - current);
    }

    private static void SyncQixingBuff(ScriptExecutor self)
    {
        var remaining = Math.Max(0, CurrentPile(self).Count);
        self.SetStatus("Self");
        var buff = self.Self?.GetBuff(SanGuoShaExpIds.QixingStars);
        if (buff == null)
        {
            self.AddBuff(SanGuoShaExpIds.QixingStars, Math.Max(1, remaining).ToString());
            buff = self.Self?.GetBuff(SanGuoShaExpIds.QixingStars);
        }

        if (buff?.buffConfig != null)
        {
            buff.buffConfig.Level = remaining;
        }
    }

    private static void ClearCareerState(ScriptExecutor self)
    {
        var key = CombatKey(self);
        QixingPiles.Remove(key);
        QixingInitialCounts.Remove(key);
        self.SetStatus("Self");
        self.RemoveBuff(SanGuoShaExpIds.QixingStars);
        ExecutorApi.ClearHook(self, SanGuoShaExpIds.CareerHook, SanGuoShaExpIds.CareerToken);
    }

    private static void SetQixingSkillReady(ScriptExecutor self)
    {
        foreach (var skillId in QixingSkillIds)
        {
            PlayerApi.SetSkillTime(skillId, 0);
        }

        try
        {
            self.UpdateSkillTime();
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Debug("Qixing skill time UI refresh skipped: " + ex.Message);
        }
    }

    private static void RollMistChecks(ScriptExecutor self)
    {
        for (var i = 0; i < MistCheckCount; i++)
        {
            try
            {
                self.CheckDice.Roll();
            }
            catch (Exception ex)
            {
                SanGuoShaExpLog.Warn("Great Fog check roll failed: " + ex.Message);
                return;
            }
        }
    }

    private static string CombatKey(ScriptExecutor self)
    {
        return self?.Self?.InstanceId ?? "solo";
    }

    private static void ShowCaption(string text)
    {
        try
        {
            var playerInfo = typeof(ScriptExecutor).GetNestedType("PlayerInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            playerInfo?.GetMethod("ShowCaption", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { text });
        }
        catch
        {
            SanGuoShaExpLog.Warn(text);
        }
    }
}
