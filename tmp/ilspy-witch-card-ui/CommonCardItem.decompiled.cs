using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Data.Save;
using Fight.ActionCommand;
using Rougamo;
using Rougamo.Context;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI;
using Witch.UI.Window;

public class CommonCardItem : CardItem, IPointerDownHandler, IEventSystemHandler
{
	public static List<Func<CommonCardItem, bool>> UseChecker = new List<Func<CommonCardItem, bool>>
	{
		delegate(CommonCardItem card)
		{
			if ((card.scriptExecutor.dataConfig.Vars.TryGetValue("Usable", out var value) && value == "0") || card.Tags.Contains("Unusable"))
			{
				UIManager.Instance.ShowTip("无法被打出");
				return false;
			}
			return true;
		},
		delegate(CommonCardItem card)
		{
			int num = int.Parse(card.dataConfig.Vars.GetValueOrDefault("TotalExCost", "0")) + int.Parse(card.dataConfig.Vars.GetValueOrDefault("ExCost", "0")) + int.Parse(card.dataConfig.Vars.GetValueOrDefault("OnceExCost", "0"));
			int num2 = (int)((float)int.Parse(card.data["Expend"]) * FightPlayer.Instance.Status.dynamicVariables.GetValueOrDefault("CardCost", 1f));
			num2 += num;
			num2 = Math.Max(0, num2);
			if (num2 > FightPlayer.Instance.CurPowerCount)
			{
				AudioManager.Instance?.PlayEffect("Effect/lose");
				UIManager.Instance.ShowTip("费用不足");
				Singleton<NarrationManager>.Instance.Play(22);
				Singleton<EventCenter>.Instance.EventTrigger("NoPowerWhenTry" + card.status.InstanceId);
				return false;
			}
			return true;
		}
	};

	public static int ExUseCount = 0;

	public static List<Func<CommonCardItem, (bool blockThrow, bool blockUse, Action onUse)>> UseCallback = new List<Func<CommonCardItem, (bool, bool, Action)>>
	{
		delegate(CommonCardItem card)
		{
			if (card.Tags.Contains("Burnout"))
			{
				FightUI.cardItemList.Remove(card);
				return ((bool blockThrow, bool blockUse, Action onUse))(blockThrow: true, blockUse: false, onUse: delegate
				{
					card.InternalBurning();
				});
			}
			return ((bool blockThrow, bool blockUse, Action onUse))(blockThrow: false, blockUse: false, onUse: null);
		},
		(CommonCardItem card) => ((bool blockThrow, bool blockUse, Action onUse))((card.Tags.Contains("Fragmented") || (card.Vars.ContainsKey("NeedRemove") && card.Vars["NeedRemove"] == "True")) ? (blockThrow: true, blockUse: false, onUse: delegate
		{
			card.InternalBurning();
			RoleTable.Instance.cardList.Remove(card.dataConfig);
			RoleTable.Instance.UnCardList.Remove(card.dataConfig);
		}) : (blockThrow: false, blockUse: false, onUse: null)),
		delegate(CommonCardItem card)
		{
			if (card.Tags.Contains("Instant"))
			{
				return ((bool blockThrow, bool blockUse, Action onUse))(blockThrow: false, blockUse: true, onUse: delegate
				{
					CardItem.UseCount = (int)card.status.dynamicVariables.GetValueOrDefault("UseCount", 1f) + int.Parse(card.Vars.GetValueOrDefault("ExUseCount", "0")) + ExUseCount;
					card.status.SetDynamicVariable("UseCount", 1f);
					card.dataConfig.Vars["OnceExCost"] = "0";
					for (int i = 0; i < CardItem.UseCount; i++)
					{
						card.status.AddBuff("buff_timelock", 1);
						card.status.GetBuff("buff_timelock").effectList.Add((card.dataConfig, delegate
						{
							card.scriptExecutor.RunScript("UseScript");
							card.enchScriptExecutor?.RunScript("UseScript");
						}));
					}
					Singleton<EventCenter>.Instance.EventTrigger("ActionAfter" + card.status.InstanceId, new ActionData(card.dataConfig, RoleTable.Instance?.Id));
				});
			}
			return ((bool blockThrow, bool blockUse, Action onUse))(card.Tags.Contains("Combo") ? (blockThrow: false, blockUse: false, onUse: delegate
			{
				if (card.scriptExecutor is ScriptExecutor scriptExecutor)
				{
					scriptExecutor.ComboSc();
				}
			}) : (blockThrow: false, blockUse: false, onUse: null));
		},
		(CommonCardItem card) => ((bool blockThrow, bool blockUse, Action onUse))(card.Tags.Contains("Recycle") ? (blockThrow: true, blockUse: false, onUse: null) : (blockThrow: false, blockUse: false, onUse: null)),
		(CommonCardItem card) => ((bool blockThrow, bool blockUse, Action onUse))(card.Tags.Contains("Ouroboros") ? ((card.Tags.Contains("Burnout") || card.Tags.Contains("Fragmented") || (card.Vars.ContainsKey("NeedRemove") && card.Vars["NeedRemove"] == "True")) ? (blockThrow: false, blockUse: false, onUse: null) : (blockThrow: true, blockUse: false, onUse: delegate
		{
			FightUI.cardItemList.Remove(card);
			FightUI.WaitCard.Remove(card);
			FightCardManager.Instance.cardList.Add(card.dataConfig);
			card.DataUpdate();
			card.EffectOfThrowCard("Canvas/FightUI/Left/Card");
		})) : (blockThrow: false, blockUse: false, onUse: null))
	};

	public float lasttime;

	[DebuggerStepThrough]
	public override void OnEndDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnEndDrag(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public override void OnBeginDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnBeginDrag(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public virtual void TrueUse()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_TrueUse();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UseCardDirectly()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UseCardDirectly();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnPointerDown(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnPointerDown(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public override void DrawEffect()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DrawEffect();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	protected (bool, Action) TryUse()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CommonCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CommonCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			(bool, Action) result = $Rougamo_TryUse();
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
		if (!base.draging)
		{
			return;
		}
		if (base.transform.localPosition.y > 50f)
		{
			TrueUse();
		}
		else if (!base.Tags.Contains("Instant") || !hasDone)
		{
			if (dataConfig?.scriptExecutor != null)
			{
				dataConfig.scriptExecutor.Target = null;
			}
			if (data != null && data.TryGetValue("InitScript", out var value) && value != null && value.Contains("Damage"))
			{
				DataUpdate();
			}
			base.OnEndDrag(eventData);
		}
	}

	private void $Rougamo_OnBeginDrag(PointerEventData eventData)
	{
		base.OnBeginDrag(eventData);
		if (dataConfig?.scriptExecutor == null || EnemyManager.Instance == null || EnemyManager.Instance.enemyList.Count == 0)
		{
			return;
		}
		Enemy enemy = EnemyManager.Instance.enemyList[0];
		if (enemy?.Status != null)
		{
			dataConfig.scriptExecutor.Target = enemy.Status;
			if (data != null && data.TryGetValue("InitScript", out var value) && value != null && value.Contains("Damage"))
			{
				DataUpdate();
			}
		}
	}

	private void $Rougamo_TrueUse()
	{
		if (hasUse)
		{
			return;
		}
		hasUse = true;
		if (base.Tags.Contains("Recycle"))
		{
			hasUse = false;
		}
		else
		{
			base.transform.GetComponent<ObjectGroup>().blocksRaycasts = false;
		}
		(bool, Action) tuple = TryUse();
		if (tuple.Item2 != null && !GameSaveManager.GetValue<bool>(GameVar.LateThrow))
		{
			tuple.Item2();
		}
		if (tuple.Item1)
		{
			dataConfig.Vars["OnceExCost"] = "0";
			RunScript("PreUseScript");
			CardItem.UseCount = (int)status.dynamicVariables.GetValueOrDefault("UseCount", 1f) + int.Parse(Vars.GetValueOrDefault("ExUseCount", "0")) + ExUseCount;
			status.SetDynamicVariable("UseCount", 1f);
			for (int i = 0; i < CardItem.UseCount; i++)
			{
				RunScript("UseScript");
			}
			Singleton<EventCenter>.Instance.EventTrigger("ActionAfter" + status.InstanceId, new ActionData(dataConfig, RoleTable.Instance?.Id));
			UIManager.Instance.GetUI<FightUI>("FightUI").CallActionAnimation(base.scriptExecutor);
		}
		else
		{
			hasUse = false;
		}
		if (base.Tags.Contains("Recycle"))
		{
			hasUse = false;
		}
		ExUseCount = 0;
		if (tuple.Item2 != null && GameSaveManager.GetValue<bool>(GameVar.LateThrow))
		{
			tuple.Item2();
		}
		base.draging = false;
		FightUI uI = UIManager.Instance.GetUI<FightUI>("FightUI");
		if (uI != null)
		{
			uI.UpdateCardItemPos();
		}
		FightUI.LastCard = dataConfig;
	}

	private void $Rougamo_UseCardDirectly()
	{
		if (CardItem.canUse)
		{
			if (dataConfig?.scriptExecutor != null && EnemyManager.Instance != null && EnemyManager.Instance.enemyList.Count > 0)
			{
				dataConfig.scriptExecutor.Target = EnemyManager.Instance.enemyList[0].Status;
			}
			if (data != null && data.TryGetValue("InitScript", out var value) && value != null && value.Contains("Damage"))
			{
				DataUpdate();
			}
			TrueUse();
			if (dataConfig?.scriptExecutor != null)
			{
				dataConfig.scriptExecutor.Target = null;
			}
		}
	}

	private void $Rougamo_OnPointerDown(PointerEventData eventData)
	{
		if (base.enabled && CardItem.canUse && eventData.button == PointerEventData.InputButton.Left)
		{
			if (Time.time - lasttime < 0.25f)
			{
				TrueUse();
			}
			else
			{
				lasttime = Time.time;
			}
		}
	}

	private async void $Rougamo_DrawEffect()
	{
		base.enabled = false;
		base.transform.DOLocalMoveY(base.initPosition.y + 100f, 0.1f);
		await UniTask.WaitForSeconds(0.2f, ignoreTimeScale: false, PlayerLoopTiming.Update, base.destroyCancellationToken);
		if (!(this == null) && (bool)this)
		{
			RunScript("DrawScript");
			animationController.PlayExitAnimation(base.initPosition, base.initScale).OnComplete(delegate
			{
				base.enabled = true;
			});
		}
	}

	private (bool, Action) $Rougamo_TryUse()
	{
		if (IsNullExtension.IsNull(status))
		{
			return (false, null);
		}
		if (status.state == IStatusManager.State.Dead || status.state == IStatusManager.State.NoAction)
		{
			return (false, null);
		}
		if (FightManager.Instance == null || FightManager.Instance.fightType != FightType.Player)
		{
			return (false, null);
		}
		if (data == null || dataConfig == null)
		{
			return (false, null);
		}
		if (CardItem.canUse)
		{
			bool flag = true;
			foreach (Func<CommonCardItem, bool> item in UseChecker)
			{
				flag &= item(this);
			}
			if (!flag)
			{
				return (false, null);
			}
			if (FightPlayer.Instance?.Status == null)
			{
				return (false, null);
			}
			if (!data.TryGetValue("Expend", out var value) || !int.TryParse(value, out var result))
			{
				return (false, null);
			}
			int num = Math.Min((int)((float)result * FightPlayer.Instance.Status.dynamicVariables.GetValueOrDefault("CardCost", 1f)), 4);
			int num2 = int.Parse(dataConfig.Vars.GetValueOrDefault("TotalExCost", "0")) + int.Parse(dataConfig.Vars.GetValueOrDefault("ExCost", "0")) + int.Parse(dataConfig.Vars.GetValueOrDefault("OnceExCost", "0"));
			num = Math.Max(0, num + num2);
			if (!base.Tags.Contains("Recycle"))
			{
				FightUI.cardItemList.Remove(this);
				FightUI.WaitCard.Add(this);
			}
			Singleton<EventCenter>.Instance.EventTrigger("Action" + status.InstanceId, new ActionData(dataConfig, RoleTable.Instance?.Id));
			Commands.Log("<color=grey>" + data.Localize("Name") + "</color>使用了", "能量从" + FightPlayer.Instance.CurPowerCount + "减少到" + (FightPlayer.Instance.CurPowerCount - num));
			FightPlayer.Instance.CurPowerCount -= num;
			if (num > 0)
			{
				Singleton<EventCenter>.Instance.EventTrigger("CostPower" + status.InstanceId, new PowerData(status.InstanceId));
			}
			if (FightPlayer.Instance.CurPowerCount <= 0)
			{
				Singleton<EventCenter>.Instance.EventTrigger("NoPower" + status.InstanceId);
			}
			status.CheckAllBuff("ReducePerUse");
			bool flag2 = false;
			bool flag3 = false;
			Action action = null;
			foreach (Func<CommonCardItem, (bool, bool, Action)> item2 in UseCallback)
			{
				(bool, bool, Action) tuple = item2(this);
				flag2 |= tuple.Item2;
				flag3 |= tuple.Item1;
				action = (Action)Delegate.Combine(action, tuple.Item3);
			}
			if (!flag3)
			{
				action = (Action)Delegate.Combine(action, (Action)delegate
				{
					InternalThrow();
				});
			}
			if (!flag2)
			{
				FightManager.Instance?.EnqueueEvent(new UseCard().Create(new UseCard.CardUseData
				{
					cardData = dataConfig,
					isBurning = base.Tags.Contains("Burnout")
				}));
			}
			return (!flag2, action);
		}
		return (false, null);
	}
}
