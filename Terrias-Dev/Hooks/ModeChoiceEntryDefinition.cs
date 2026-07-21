using System;
using UnityEngine;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public sealed class ModeChoiceEntryDefinition
{
    public ModeChoiceEntryDefinition(
        string objectName,
        string templateName,
        int sortOrder,
        Action<GameObject, ModeChoiceUI> configure,
        Action<ModeChoiceUI>? activate = null,
        string displayName = "",
        string modeId = "")
    {
        ObjectName = objectName;
        TemplateName = templateName;
        SortOrder = sortOrder;
        Configure = configure;
        Activate = activate;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? objectName : displayName;
        ModeId = modeId?.Trim() ?? "";
    }

    public string ObjectName { get; }

    public string TemplateName { get; }

    public int SortOrder { get; }

    public Action<GameObject, ModeChoiceUI> Configure { get; }

    public Action<ModeChoiceUI>? Activate { get; }

    public string DisplayName { get; }

    public string ModeId { get; }
}
