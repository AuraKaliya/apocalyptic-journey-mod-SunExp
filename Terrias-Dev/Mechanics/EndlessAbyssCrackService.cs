using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssCrackService
{
    public const string CrackTag = "裂痕";
    private const string FragmentedTag = "Fragmented";
    private const string CounterKey = "TerriasAbyssCrackCount";
    private const string TemporaryFragmentedMarker = "TerriasAbyssCrackTemporaryFragmented";

    public static void OnCardPlayed(CardItem? card, string source)
    {
        try
        {
            if (card?.dataConfig == null || !HasCrack(card.dataConfig))
            {
                return;
            }

            var count = DictionaryUtil.GetInt(card.dataConfig.Vars, CounterKey) + 1;
            DictionaryUtil.Set(card.dataConfig.Vars, CounterKey, count.ToString());
            var threshold = EndlessAbyssConfigStore.Current.Shock.CrackThreshold;
            if (count < threshold)
            {
                return;
            }

            CardMutationService.RemoveNativeTags(card, CrackTag);
            CardMutationService.AddNativeTags(card, FragmentedTag);
            CardMutationService.SetRuntimeMarkers(card.dataConfig, TemporaryFragmentedMarker);
            TerriasLog.Info("[EndlessAbyssCrack] card became temporary Fragmented from "
                + source
                + ": "
                + CardConfigApi.Id(card.dataConfig));
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessAbyssCrack] play hook failed from " + source + ": " + ex.Message);
        }
    }

    public static void RestoreTemporaryCracks(string source)
    {
        try
        {
            var restored = 0;
            foreach (var card in CombatAndDeckCards())
            {
                if (card == null || !CardMutationService.HasRuntimeMarker(card, TemporaryFragmentedMarker))
                {
                    continue;
                }

                var changed = CardMutationService.RemoveNativeTags(card, FragmentedTag);
                changed = CardMutationService.AddNativeTags(card, CrackTag) || changed;
                DictionaryUtil.Set(card.Vars, CounterKey, "0");
                DictionaryUtil.Set(card.Vars, TerriasIds.RuntimeMarkersKey, RemoveToken(
                    DictionaryUtil.Get(card.Vars, TerriasIds.RuntimeMarkersKey),
                    TemporaryFragmentedMarker));
                if (changed)
                {
                    restored++;
                }
            }

            if (restored > 0)
            {
                EndlessSeaCardAffixService.TryPersistCurrentRole("EndlessAbyssCrack.Restore");
                TerriasLog.Info("[EndlessAbyssCrack] restored " + restored + " cards from " + source + ".");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessAbyssCrack] restore failed from " + source + ": " + ex.Message);
        }
    }

    public static bool HasCrack(IDataConfig? card)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.Vars, "Tag"), CrackTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.data, "Tag"), CrackTag);
    }

    private static IEnumerable<IDataConfig> CombatAndDeckCards()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in RoleTable.Instance?.cardList ?? Enumerable.Empty<IDataConfig>())
        {
            if (card != null && seen.Add(card.InstanceID ?? CardConfigApi.Id(card)))
            {
                yield return card;
            }
        }

        var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
        foreach (var card in executor?.DeckCard ?? new List<DataConfig>())
        {
            if (card != null && seen.Add(card.InstanceID ?? CardConfigApi.Id(card)))
            {
                yield return card;
            }
        }

        foreach (var card in executor?.UsedCard ?? new List<DataConfig>())
        {
            if (card != null && seen.Add(card.InstanceID ?? CardConfigApi.Id(card)))
            {
                yield return card;
            }
        }

        foreach (var item in executor?.HandCard ?? Enumerable.Empty<CardItem>())
        {
            var card = item?.dataConfig;
            if (card != null && seen.Add(card.InstanceID ?? CardConfigApi.Id(card)))
            {
                yield return card;
            }
        }
    }

    private static string RemoveToken(string text, string token)
    {
        return string.Join(",", (text ?? "")
            .Split(new[] { ',', '|', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(item => !string.Equals(item.Trim(), token, StringComparison.Ordinal)));
    }
}
