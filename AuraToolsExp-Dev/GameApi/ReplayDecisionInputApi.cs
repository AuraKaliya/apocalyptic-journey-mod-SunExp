using System;
using System.Reflection;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.GameApi;

internal static class ReplayDecisionInputApi
{
    // Matched to repository Managed Witch.dll and its decompile: native Yes only
    // commits when this flag flips; a click on an invalid confirmation is not a decision.
    private static readonly FieldInfo SelectionConfirmedField = typeof(FightUI).GetField(
        "selectConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(FightUI).FullName, "selectConfirmed");

    internal static bool SelectionConfirmed(FightUI ui) => SelectionConfirmedField.GetValue(ui) is bool value
        ? value : throw new InvalidOperationException("Native selection confirmation is not a Boolean.");
}
