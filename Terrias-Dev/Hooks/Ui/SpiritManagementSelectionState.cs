using System;
using System.Collections.Generic;

namespace Terrias.Dll.Hooks.Ui;

internal enum SpiritTrainingTargetKind
{
    None,
    IntentSlot,
    PassiveSlot
}

internal sealed class SpiritTrainingSelectionState
{
    public SpiritTrainingTargetKind TargetKind { get; private set; }

    public int IntentSlotIndex { get; private set; } = -1;

    public string FocusedAbilityId { get; private set; } = "";

    public bool TargetsIntentSlot(int slot)
    {
        return TargetKind == SpiritTrainingTargetKind.IntentSlot && IntentSlotIndex == slot;
    }

    public bool TargetsPassiveSlot => TargetKind == SpiritTrainingTargetKind.PassiveSlot;

    public void Reset()
    {
        TargetKind = SpiritTrainingTargetKind.None;
        IntentSlotIndex = -1;
        FocusedAbilityId = "";
    }

    public void EnsureInitialized(IReadOnlyList<string> equippedIntentIds)
    {
        if (TargetKind != SpiritTrainingTargetKind.None || FocusedAbilityId.Length > 0) return;
        SelectIntentSlot(0, equippedIntentIds.Count > 0 ? equippedIntentIds[0] : "");
    }

    public void SelectIntentSlot(int slot, string? equippedAbilityId)
    {
        TargetKind = SpiritTrainingTargetKind.IntentSlot;
        IntentSlotIndex = Math.Max(0, slot);
        FocusedAbilityId = Normalize(equippedAbilityId);
    }

    public void SelectPassiveSlot(string? equippedAbilityId)
    {
        TargetKind = SpiritTrainingTargetKind.PassiveSlot;
        IntentSlotIndex = -1;
        FocusedAbilityId = Normalize(equippedAbilityId);
    }

    public void PreviewAbility(string? abilityId)
    {
        FocusedAbilityId = Normalize(abilityId);
    }

    private static string Normalize(string? value) => (value ?? "").Trim();
}

internal static class SpiritPartySlotInteraction
{
    public static bool TrySelectOccupant(string? currentUid, out string selectedUid)
    {
        selectedUid = (currentUid ?? "").Trim();
        return selectedUid.Length > 0;
    }
}
