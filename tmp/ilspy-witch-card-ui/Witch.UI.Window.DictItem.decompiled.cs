using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Witch.UI.Window;

public class DictItem : DictionaryItem, IPointerEnterHandler, IEventSystemHandler
{
	private bool needSc = true;

	[DebuggerStepThrough]
	public override void Init(DataConfig dataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DictItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DictItem).TypeHandle);
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
	public override void OnPointerEnter(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DictItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DictItem).TypeHandle);
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
	public void SetCardMsg(Transform newTransform)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(DictItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DictItem).TypeHandle);
		methodContext.Arguments = new object[1] { newTransform };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SetCardMsg(newTransform);
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
		methodContext.TargetType = typeof(DictItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DictItem).TypeHandle);
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

	private void $Rougamo_Init(DataConfig dataConfig)
	{
		needSc = true;
		base.Init(dataConfig);
		ICard.SetCardStyle(base.transform, dataConfig);
		if (itemIcon == null)
		{
			itemIcon = ResourceLoader.Load<Sprite>("Icon/Card/卡面占位");
		}
		base.transform.Find("Front/字体").gameObject.SetActive(value: true);
		base.transform.Find("Front/background").GetComponent<Image>().color = new Color(1f, 1f, 1f);
		base.transform.Find("Front/icon").gameObject.SetActive(value: true);
		base.transform.GetComponent<Button>().onClick.RemoveAllListeners();
		base.transform.GetComponent<Button>().onClick.AddListener(delegate
		{
			dictionaryUI.ShowInfo(this);
			HideFloatingWindow();
		});
	}

	private void $Rougamo_OnPointerEnter(PointerEventData eventData)
	{
		base.OnPointerEnter(eventData);
	}

	private void $Rougamo_SetCardMsg(Transform newTransform)
	{
		IScriptExecutor scriptExecutor = dataConfig.scriptExecutor;
		IDictionary<string, string> data = dataConfig.data;
		if (newTransform.Find("Front/cost/cost").GetComponent<MeshRenderer>() != null)
		{
			newTransform.Find("Front/cost/cost").GetComponent<MeshRenderer>().material.mainTexture = ResourceLoader.Load<Texture>("Icon/CardTemplate/Template/费用数字/" + data["Expend"]);
		}
		else if (newTransform.Find("Front/cost/cost").GetComponent<Image>() != null)
		{
			newTransform.Find("Front/cost/cost").GetComponent<Image>().sprite = ResourceLoader.Load<Sprite>("Icon/CardTemplate/Template/费用数字/" + data["Expend"]);
		}
		if (newTransform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>() != null)
		{
			newTransform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().SetLocalizedText(() => data.Localize("Name"));
		}
		if (needSc)
		{
			if (FightManager.Instance != null && FightPlayer.Instance != null && FightPlayer.Instance.Status != null)
			{
				scriptExecutor.Self = FightPlayer.Instance.Status;
			}
			else
			{
				scriptExecutor.Self = null;
			}
			needSc = false;
			scriptExecutor.RunScript("InitScript");
		}
		if (newTransform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>() != null)
		{
			newTransform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().SetLocalizedText(base.GetLocalizedDescription);
		}
	}

	private void $Rougamo_DataUpdate()
	{
		if (dataConfig != null && base.gameObject.activeSelf)
		{
			RefreshLocalizedCache();
			BindLocalizedKeywordDisplay();
			if (!dataConfig.data.ContainsKey("Rarity"))
			{
				UnityEngine.Debug.Log("物品" + base.itemName + "缺少Rarity字段，已设置为默认值0");
			}
			SetCardMsg(base.transform);
		}
	}
}
