using System;
using System.Collections;
using System.Collections.Generic;
using AuraGameData.Shared.GameApi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace SunExp.Dll.Mechanics;

public sealed class ProjectionTurnAnchorObj : OtherObj
{
    public override string Type => "ProjectionTurnAnchor";

    public bool InitializeAnchor(int battleEpoch, IDictionary<string, string> templateData)
    {
        if (templateData == null
            || !templateData.TryGetValue("Animation", out var animation)
            || string.IsNullOrWhiteSpace(animation))
        {
            return false;
        }

        if (!templateData.TryGetValue("Id", out var templateId)
            || AuraGameDataHostApi.ResolveHandle(DataType.Career, templateId) is not { } handle)
        {
            return false;
        }

        var result = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = handle,
            PreCompile = false,
            DataOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Name"] = "",
                ["Name_zh-Hant"] = "",
                ["Name_en"] = "",
                ["Name_ja"] = "",
                ["Attack"] = "0",
                ["Defend"] = "0",
                ["Hp"] = "1",
                ["ActionCount"] = "1",
                ["CardList"] = "",
                ["Animation"] = animation
            }
        });
        dataConfig = result.Instance as DataConfig;
        if (dataConfig == null)
        {
            return false;
        }

        data = dataConfig.data;
        FightAction = new ObjectAction(this);
        Attack = 0;
        Defend = 0;
        MaxHp = 1;
        CurHp = 1;
        MaxActionCount = 0;
        ActionCount = 0;
        InstanceId = "SunExpProjectionTurnAnchor:" + battleEpoch;
        gameObject.name = InstanceId;

        var status = gameObject.AddComponent<StatusManager>().Init(this) as StatusManager;
        if (status == null)
        {
            return false;
        }

        Status = status;
        if (!EnsureActionIcons(status))
        {
            return false;
        }

        status.animatedState = IStatusManager.AnimatedState.Idle;
        if (status.statusBarUI != null)
        {
            status.statusBarUI.gameObject.SetActive(false);
        }

        if (status.actionContent != null)
        {
            status.actionContent.SetActive(false);
        }

        if (status.effectListObj != null)
        {
            status.effectListObj.SetActive(false);
        }

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }

        transform.position = new Vector3(0f, -10000f, 0f);
        return true;
    }

    private static bool EnsureActionIcons(StatusManager status)
    {
        if (status.actionContent == null
            || status.actionObj == null
            || status.actionText == null
            || status.actionObj.Length < 4
            || status.actionText.Length < 4)
        {
            return false;
        }

        var content = status.actionContent.transform.Find("content");
        var uiManager = UIManager.Instance;
        if (content == null || uiManager == null)
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            var icon = status.actionObj[i];
            if (icon == null)
            {
                icon = uiManager.CreateActionIcon();
                if (icon == null)
                {
                    return false;
                }

                icon.transform.SetParent(content);
                icon.transform.localScale = Vector3.one;
                icon.transform.localPosition = Vector3.zero;
                var iconImage = icon.transform.Find("Icon")?.GetComponent<Image>();
                if (iconImage != null)
                {
                    iconImage.color = Color.white;
                }

                status.actionObj[i] = icon;
            }

            var keyword = status.actionText[i]
                ?? icon.GetComponent<KeywordDisplay>()
                ?? icon.AddComponent<KeywordDisplay>();
            keyword.text = "";
            keyword.type = "Action";
            status.actionText[i] = keyword;

            var valueText = icon.transform.Find("Icon/val")?.GetComponent<TMP_Text>();
            if (valueText != null)
            {
                valueText.text = "";
            }

            icon.SetActive(false);
        }

        return true;
    }

    public override void AddCardList()
    {
    }

    public override IEnumerator DoAction()
    {
        var routine = ProjectionTurnCoordinator.ExecuteCurrentRound();
        while (routine.MoveNext())
        {
            yield return routine.Current;
        }
    }
}
