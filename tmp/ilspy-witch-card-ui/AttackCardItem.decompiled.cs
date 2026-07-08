using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using Data.Save;
using Rougamo;
using Rougamo.Context;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Witch.UI;
using Witch.UI.Window;

public class AttackCardItem : CommonCardItem, IPointerDownHandler, IEventSystemHandler
{
	[CompilerGenerated]
	private bool <isLine>k__BackingField;

	private StatusManager hitEnemy;

	private EventSystem currentSystem;

	public bool isLine
	{
		[CompilerGenerated]
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(AttackCardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				bool result = $Rougamo_get_isLine();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[CompilerGenerated]
		[DebuggerStepThrough]
		private set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(AttackCardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_isLine(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	[DebuggerStepThrough]
	public override void OnBeginDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
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
	public override void OnDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnDrag(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public override void Init(DataConfig dataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { dataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Init(dataConfig);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public override void OnEndDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
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
	public new void OnPointerDown(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
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
	public bool BeginLineMode(bool requireClickable = true)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { requireClickable };
		try
		{
			modifiable.OnEntry(methodContext);
			bool result = $Rougamo_BeginLineMode(requireClickable);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CancelLineMode()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CancelLineMode();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CommitOrCancelFromKeyboard()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CommitOrCancelFromKeyboard();
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
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
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

	private IEnumerator OnMouseDownRight()
	{
		currentSystem = EventSystem.current;
		currentSystem.enabled = false;
		while (!KeyManager.playerAction.Main.RightClick.WasPressedThisFrame() && !hasUse && !(FightManager.Instance == null) && FightManager.Instance.fightType != FightType.Loss && FightManager.Instance.fightType != FightType.None && FightManager.Instance.fightType != FightType.Win)
		{
			if (UIManager.Instance.GetUI<LineUI>("LineUI") != null)
			{
				UIManager.Instance.GetUI<LineUI>("LineUI").SetEndPos();
			}
			CheckRayToEnemy();
			yield return null;
		}
		currentSystem.enabled = true;
		if (hitEnemy != null)
		{
			hitEnemy.OnUnSelect();
			hitEnemy = null;
		}
		if (UIManager.Instance.GetUI<LineUI>("LineUI") != null)
		{
			UIManager.Instance.GetUI<LineUI>("LineUI").Hide();
		}
		hitEnemy = null;
		if (data["InitScript"].Contains("Damage"))
		{
			base.scriptExecutor.Target = null;
			DataUpdate();
		}
		isLine = false;
		Cursor.visible = true;
	}

	[DebuggerStepThrough]
	public override void TrueUse()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
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
	private void CheckRayToEnemy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CheckRayToEnemy();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static bool IsSelectableTarget(StatusManager target)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[1] { target };
		try
		{
			modifiable.OnEntry(methodContext);
			bool result = $Rougamo_IsSelectableTarget(target);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void OnDestroy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(AttackCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(AttackCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnDestroy();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[SpecialName]
	private bool $Rougamo_get_isLine()
	{
		return isLine;
	}

	[SpecialName]
	private void $Rougamo_set_isLine(bool value)
	{
		isLine = value;
	}

	private void $Rougamo_OnBeginDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_Init(DataConfig dataConfig)
	{
		if (dataConfig == null || FightPlayer.Instance == null)
		{
			return;
		}
		status = FightPlayer.Instance.Status as StatusManager;
		if (!FightPlayer.Instance.Status.IsNull())
		{
			base.dataConfig = dataConfig;
			data = dataConfig.data;
			Vars = dataConfig.Vars;
			Vars["HasBurn"] = "False";
			base.scriptExecutor.Self = status;
			if (RoleTable.Instance.enchasedDict.ContainsKey(dataConfig.InstanceID))
			{
				enchScriptExecutor = RoleTable.Instance.enchasedDict[dataConfig.InstanceID].scriptExecutor;
				enchScriptExecutor.Self = status;
				enchScriptExecutor.Object.Clear();
			}
			base.scriptExecutor.Object.Clear();
			base.scriptExecutor.dataConfig.Vars["Usable"] = "1";
			ICard.SetCardStyle(base.transform, dataConfig);
			DrawEffect();
			DataUpdate();
			FightCardManager.Instance.CardTagCheck(dataConfig);
		}
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnPointerDown(PointerEventData eventData)
	{
		if (!isLine && !(FightManager.Instance == null) && FightManager.Instance.fightType == FightType.Player && CardItem.canUse)
		{
			BeginLineMode();
		}
	}

	private bool $Rougamo_BeginLineMode(bool requireClickable = true)
	{
		if (isLine)
		{
			return false;
		}
		if (requireClickable && !UIUtil.CheckClickable(base.transform))
		{
			return false;
		}
		isLine = true;
		currentSystem = EventSystem.current;
		UIManager.Instance.ShowUI<LineUI>("LineUI").SetStartPos(base.transform.position);
		Cursor.visible = false;
		StopAllCoroutines();
		StartCoroutine(OnMouseDownRight());
		return true;
	}

	private void $Rougamo_CancelLineMode()
	{
		if (isLine)
		{
			StopAllCoroutines();
			isLine = false;
			Cursor.visible = true;
			if (hitEnemy != null)
			{
				hitEnemy.OnUnSelect();
				hitEnemy = null;
			}
			base.scriptExecutor.Target = null;
			if (data != null && data.TryGetValue("InitScript", out var value) && value != null && value.Contains("Damage"))
			{
				DataUpdate();
			}
			if (UIManager.Instance.GetUI<LineUI>("LineUI") != null)
			{
				UIManager.Instance.GetUI<LineUI>("LineUI").Hide();
			}
			if (currentSystem != null)
			{
				currentSystem.enabled = true;
			}
		}
	}

	private void $Rougamo_CommitOrCancelFromKeyboard()
	{
		if (!isLine)
		{
			return;
		}
		StopAllCoroutines();
		isLine = false;
		Cursor.visible = true;
		if (UIManager.Instance.GetUI<LineUI>("LineUI") != null)
		{
			UIManager.Instance.GetUI<LineUI>("LineUI").Hide();
		}
		if (currentSystem != null)
		{
			currentSystem.enabled = true;
		}
		if (IsSelectableTarget(hitEnemy))
		{
			TrueUse();
			hitEnemy.OnUnSelect();
			hitEnemy = null;
			base.scriptExecutor.Target = null;
			return;
		}
		if (hitEnemy != null)
		{
			hitEnemy.OnUnSelect();
			hitEnemy = null;
		}
		base.scriptExecutor.Target = null;
		if (data != null && data.TryGetValue("InitScript", out var value) && value != null && value.Contains("Damage"))
		{
			DataUpdate();
		}
	}

	private async void $Rougamo_DrawEffect()
	{
		base.enabled = false;
		await UniTask.WaitForSeconds(0.1f, ignoreTimeScale: false, PlayerLoopTiming.Update, base.destroyCancellationToken);
		if (!(this == null) && (bool)this)
		{
			base.enabled = true;
			RunScript("DrawScript");
		}
	}

	private void $Rougamo_TrueUse()
	{
		if (hasUse)
		{
			return;
		}
		var (flag, action) = TryUse();
		if (!GameSaveManager.GetValue<bool>(GameVar.LateThrow))
		{
			action?.Invoke();
		}
		if (flag)
		{
			if (base.scriptExecutor.Target.IsNull() && EnemyManager.Instance != null && EnemyManager.Instance.enemyList.Count > 0)
			{
				base.scriptExecutor.Target = EnemyManager.Instance.enemyList[0].Status;
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
			if (hitEnemy != null)
			{
				base.scriptExecutor.Target = hitEnemy;
			}
			else if (EnemyManager.Instance != null && EnemyManager.Instance.enemyList.Count > 0)
			{
				base.scriptExecutor.Target = EnemyManager.Instance.enemyList[0].Status;
			}
			RunScript("PreUseScript");
			CardItem.UseCount = (int)status.dynamicVariables.GetValueOrDefault("UseCount", 1f) + CommonCardItem.ExUseCount + int.Parse(Vars.GetValueOrDefault("ExUseCount", "0"));
			CommonCardItem.ExUseCount = 0;
			status.SetDynamicVariable("UseCount", 1f);
			for (int i = 0; i < CardItem.UseCount; i++)
			{
				RunScript("UseScript");
			}
			Singleton<EventCenter>.Instance.EventTrigger("ActionAfter" + status.InstanceId, new ActionData(dataConfig, RoleTable.Instance?.Id));
			UIManager.Instance.GetUI<FightUI>("FightUI").CallActionAnimation(base.scriptExecutor);
			FightUI.LastCard = dataConfig;
		}
		else
		{
			CommonCardItem.ExUseCount = 0;
			hasUse = false;
		}
		if (GameSaveManager.GetValue<bool>(GameVar.LateThrow))
		{
			action?.Invoke();
		}
		if (base.Tags.Contains("Recycle"))
		{
			hasUse = false;
		}
	}

	private void $Rougamo_CheckRayToEnemy()
	{
		Ray ray = Camera.main.ScreenPointToRay(KeyManager.playerAction.Main.Point.ReadValue<Vector2>());
		if (FightPlayer.Instance == null)
		{
			return;
		}
		if (Physics.Raycast(ray, out var hitInfo, 10000f, LayerMask.GetMask("Enemy", "Player")))
		{
			StatusManager statusManager = null;
			statusManager = hitInfo.transform.GetComponent<StatusManager>();
			if (statusManager == null || status == null || (statusManager.GetComponent<StatusManager>().InstanceId == status.InstanceId && dataConfig.Vars.GetValueOrDefault("CanSelf", "False") == "False"))
			{
				return;
			}
			if (Vars.GetValueOrDefault("CanEnemy", "True") == "False" && statusManager.fatherObject is Enemy)
			{
				statusManager = null;
			}
			if (statusManager != null && IsSelectableTarget(statusManager) && statusManager != hitEnemy)
			{
				if (hitEnemy != null)
				{
					hitEnemy.OnUnSelect();
				}
				hitEnemy = statusManager;
				base.scriptExecutor.Target = hitEnemy;
				base.scriptExecutor.Object.Clear();
				base.scriptExecutor.Object.Add(base.scriptExecutor.Target);
				if (data["InitScript"].Contains("Damage"))
				{
					DataUpdate();
				}
			}
			else if (statusManager == null && hitEnemy != null)
			{
				hitEnemy.OnUnSelect();
				hitEnemy = null;
				base.scriptExecutor.Target = null;
				if (data["InitScript"].Contains("Damage"))
				{
					DataUpdate();
				}
			}
			if (IsSelectableTarget(hitEnemy))
			{
				hitEnemy.OnSelect();
			}
			else if (hitEnemy != null)
			{
				hitEnemy.OnUnSelect();
				hitEnemy = null;
				base.scriptExecutor.Target = null;
				if (data["InitScript"].Contains("Damage"))
				{
					DataUpdate();
				}
			}
			if ((KeyManager.playerAction.Main.Click.WasPressedThisFrame() || KeyManager.playerAction.Main.Click.WasReleasedThisFrame()) && hitEnemy != null && IsSelectableTarget(hitEnemy))
			{
				currentSystem.enabled = true;
				StopAllCoroutines();
				Cursor.visible = true;
				isLine = false;
				UIManager.Instance.HideUI("LineUI");
				TrueUse();
				hitEnemy.OnUnSelect();
				hitEnemy = null;
				base.scriptExecutor.Target = null;
			}
			return;
		}
		if (KeyManager.playerAction.Main.Click.WasReleasedThisFrame())
		{
			if (Time.time - lasttime < 0.25f)
			{
				if (EnemyManager.Instance != null && EnemyManager.Instance.enemyList.Count != 0 && EnemyManager.enemyCount != 0)
				{
					base.scriptExecutor.Target = EnemyManager.Instance.enemyList[0].Status;
					TrueUse();
					currentSystem.enabled = true;
					StopAllCoroutines();
					Cursor.visible = true;
					isLine = false;
					UIManager.Instance.HideUI("LineUI");
					if (hitEnemy != null)
					{
						hitEnemy.OnUnSelect();
						hitEnemy = null;
					}
				}
				return;
			}
			if (Touchscreen.current != null)
			{
				currentSystem.enabled = true;
				StopAllCoroutines();
				Cursor.visible = true;
				isLine = false;
				UIManager.Instance.HideUI("LineUI");
				return;
			}
			lasttime = Time.time;
		}
		if (hitEnemy != null)
		{
			hitEnemy.OnUnSelect();
		}
	}

	private static bool $Rougamo_IsSelectableTarget(StatusManager target)
	{
		if (target == null || !target.enabled)
		{
			return false;
		}
		if (target.fatherObject is FightPlayer || target.fatherObject is OtherPlayer)
		{
			return true;
		}
		if (target.fatherObject is OtherObj)
		{
			return target.CurHp > 0;
		}
		return false;
	}

	private void $Rougamo_OnDestroy()
	{
		ClearEvent();
		StopAllCoroutines();
		if (isLine)
		{
			isLine = false;
			Cursor.visible = true;
			currentSystem.enabled = true;
		}
	}
}
