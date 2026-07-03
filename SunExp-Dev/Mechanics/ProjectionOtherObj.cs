using System;
using System.Collections;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace SunExp.Dll.Mechanics;

public sealed class ProjectionOtherObj : OtherObj
{
    public string RoleId { get; private set; } = "";

    public string OwnerStatusId { get; private set; } = "";

    public override string Type => "Projection";

    public bool InitProjection(PolymorphRoleSpec role, string ownerStatusId, int index)
    {
        if (role == null)
        {
            return false;
        }

        RoleId = role.Id;
        OwnerStatusId = ownerStatusId ?? "";
        dataConfig = ProjectionSummonService.CreateProjectionDataConfig(role);
        data = dataConfig.data;
        FightAction = new ObjectAction(this);
        ApplyEnemyMaterial();

        Attack = 0;
        Defend = 0;
        MaxHp = ProjectionStrategyService.ProjectionMaxHp(role);
        CurHp = MaxHp;
        MaxActionCount = 1;
        ActionCount = MaxActionCount;
        InstanceId = ProjectionStateStore.NextStatusId();
        gameObject.name = data.Localize("Name") + InstanceId;
        var status = transform.gameObject.AddComponent<StatusManager>().Init(this) as StatusManager;
        if (status == null)
        {
            return false;
        }

        Status = status;
        EnsureActionIcons();
        ProjectionSummonService.RegisterFightState(this);
        dataConfig.scriptExecutor.Self = Status;
        dataConfig.scriptExecutor.SetStatus("Self");
        AddCardList();
        status.UpdateStatus(true);
        status.animatedState = IStatusManager.AnimatedState.Idle;
        if (GameApp.Instance.NowBackground != null && GameApp.Instance.NowBackground.name == "BalancedHolySee")
        {
            CanRef();
        }

        InitBound(null, true);
        ProjectionSummonService.PositionProjection(this, index);
        return true;
    }

    public override IEnumerator DoAction()
    {
        FightManager.Instance?.ChangeUnit(FightType.Partner);
        return base.DoAction();
    }

    public override void AddCardList()
    {
        AddProjectionAction(SunExpIds.ProjectionActionStaffTapCardId);
        AddProjectionAction(SunExpIds.ProjectionActionShieldBlessingCardId);
    }

    private void AddProjectionAction(string cardId)
    {
        var objectCard = new ObjectCard
        {
            status = Status as StatusManager
        };
        objectCard.Init(new DataConfig(cardId, DataType.EnemyCard));
        FightAction.AddCard(objectCard);
    }

    private void ApplyEnemyMaterial()
    {
        try
        {
            var body = transform.Find("body");
            var renderer = body?.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                var material = SunExpResourceCache.Load<Material>("Material/EnemyMaterial", true);
                if (material != null)
                {
                    renderer.material = UnityEngine.Object.Instantiate(material);
                }

                renderer.color = new Color(0.82f, 0.9f, 1f, 0.88f);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Projection] material fallback used: " + ex.Message);
        }
    }

    private void EnsureActionIcons()
    {
        var status = Status as StatusManager;
        if (status?.actionContent == null)
        {
            return;
        }

        var content = status.actionContent.transform.Find("content");
        if (content == null)
        {
            return;
        }

        try
        {
            var layout = content.GetComponent<AnimatedHorizontalLayout>();
            if (layout != null)
            {
                layout.spacing = 24f;
            }
        }
        catch
        {
            // Layout tuning is cosmetic.
        }

        for (var i = 0; i < 4; i++)
        {
            if (status.actionObj[i] != null && status.actionText[i] != null)
            {
                continue;
            }

            var icon = UIManager.Instance.CreateActionIcon();
            icon.transform.SetParent(content);
            icon.transform.localScale = Vector3.one;
            icon.transform.localPosition = Vector3.zero;
            icon.transform.Find("Icon").GetComponent<Image>().color = Color.white;
            var keyword = icon.AddComponent<KeywordDisplay>();
            keyword.type = "Action";
            icon.SetActive(false);
            status.actionObj[i] = icon;
            status.actionText[i] = keyword;

            var valueText = icon.transform.Find("Icon/val")?.GetComponent<TMP_Text>();
            if (valueText != null)
            {
                valueText.text = "";
            }
        }
    }
}
