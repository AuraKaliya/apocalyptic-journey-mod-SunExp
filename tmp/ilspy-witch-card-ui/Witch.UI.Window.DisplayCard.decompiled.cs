using System;
using System.Diagnostics;
using System.Reflection;
using DG.Tweening;
using Rougamo;
using Rougamo.Context;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace Witch.UI.Window;

public class DisplayCard : CardItem, IPointerClickHandler, IEventSystemHandler
{
	public float CurrentScale = 1.2f;

	public float NormalScale = 0.8f;

	public bool isSelect;

	public UnityEvent onClick = new UnityEvent();

	[DebuggerStepThrough]
	public override void OnBeginDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
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
	public override void OnEndDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
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
	public override void OnDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
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
	public override void OnPointerEnter(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnPointerEnter(eventData);
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
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
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
	public override void DataUpdate()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DataUpdate();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public override void OnPointerExit(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnPointerExit(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnHover()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnHover();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnExit()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnExit();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnPointerClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnPointerClick(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnSelect()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnSelect();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnUnSelect()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DisplayCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DisplayCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnUnSelect();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private void $Rougamo_OnBeginDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnPointerEnter(PointerEventData eventData)
	{
		OnHover();
	}

	private void $Rougamo_Init(DataConfig dataConfig)
	{
		if (FightPlayer.Instance != null)
		{
			status = FightPlayer.Instance.Status as StatusManager;
		}
		else
		{
			status = null;
		}
		base.dataConfig = dataConfig;
		data = dataConfig.data;
		Vars = dataConfig.Vars;
		if (FightPlayer.Instance != null)
		{
			base.scriptExecutor.Self = FightPlayer.Instance.Status;
			base.scriptExecutor.Object.Clear();
			if (dataConfig.data["InitScript"].Contains("Vars[\"BaseScript\"]=\"AttackCardItem\";"))
			{
				if (EnemyManager.Instance != null && EnemyManager.Instance.enemyList.Count > 0)
				{
					base.scriptExecutor.Target = EnemyManager.Instance.enemyList[0].Status;
				}
			}
			else
			{
				base.scriptExecutor.Object.Add(FightPlayer.Instance.Status);
			}
		}
		else
		{
			base.scriptExecutor.Self = null;
		}
		Singleton<EventCenter>.Instance.AddEventListener("LanguageChange", DataUpdate, this);
		base.scriptExecutor.dataConfig.Vars["Usable"] = "1";
		ICard.SetCardStyle(base.transform, dataConfig);
		DataUpdate();
		base.transform.localScale = new Vector3(NormalScale, NormalScale, 1f);
	}

	private void $Rougamo_DataUpdate()
	{
		if (this == null)
		{
			return;
		}
		ICard.SetCardMsg(base.transform, dataConfig, FightPlayer.Instance?.Status as StatusManager);
		if (base.transform.parent != null)
		{
			KeywordDisplay obj = base.transform.parent.GetComponent<KeywordDisplay>() ?? base.transform.parent.gameObject.AddComponent<KeywordDisplay>();
			Sprite sprite = ResourceLoader.Load<Sprite>(data.GetValueOrDefault("Icon", "Icon/Card/卡面占位"));
			if (sprite == null)
			{
				sprite = ResourceLoader.Load<Sprite>("Icon/Card/卡面占位");
			}
			obj.SetText(GetComponent<KeywordDisplay>().title, base.gameObject.GetComponent<KeywordDisplay>().text, base.gameObject.GetComponent<KeywordDisplay>().keyWords, null, sprite, "card".Localize("Glossary"));
		}
	}

	private void $Rougamo_OnPointerExit(PointerEventData eventData)
	{
		OnExit();
	}

	private void $Rougamo_OnHover()
	{
		base.transform.DOScale(CurrentScale, 0.25f);
		base.index = base.transform.GetSiblingIndex();
	}

	private void $Rougamo_OnExit()
	{
		if (!isSelect)
		{
			base.transform.DOScale(NormalScale, 0.25f);
			base.transform.SetSiblingIndex(base.index);
		}
	}

	private void $Rougamo_OnPointerClick(PointerEventData eventData)
	{
		onClick?.Invoke();
	}

	private void $Rougamo_OnSelect()
	{
		isSelect = true;
		OnHover();
	}

	private void $Rougamo_OnUnSelect()
	{
		isSelect = false;
		OnExit();
	}
}
