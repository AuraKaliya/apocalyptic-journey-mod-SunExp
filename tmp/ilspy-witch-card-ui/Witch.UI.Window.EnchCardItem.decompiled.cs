using System;
using System.Diagnostics;
using System.Reflection;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine.EventSystems;

namespace Witch.UI.Window;

public class EnchCardItem : ItemNonDrag
{
	public CardEnchUI CardEnchUI;

	[DebuggerStepThrough]
	public override void OnPointerClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(EnchCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(EnchCardItem).TypeHandle);
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
	public override void ShowFloatingWindow()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(EnchCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(EnchCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ShowFloatingWindow();
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
		methodContext.TargetType = typeof(EnchCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(EnchCardItem).TypeHandle);
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
	public void Unload()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(EnchCardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(EnchCardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Unload();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private void $Rougamo_OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button == PointerEventData.InputButton.Right)
		{
			ShowFloatingWindow();
		}
		if (eventData.button == PointerEventData.InputButton.Left)
		{
			HideFloatingWindow();
		}
	}

	private void $Rougamo_ShowFloatingWindow()
	{
		floatingWindow.Clear();
		floatingWindow.AddButton("unmount", delegate
		{
			Unload();
			HideFloatingWindow();
		});
		base.ShowFloatingWindow();
	}

	private void $Rougamo_Init(DataConfig dataConfig)
	{
		base.Init(dataConfig);
		ICard.SetCardStyle(base.transform, dataConfig);
		ItemType = "Card";
		ICard.SetCardMsg(base.transform, dataConfig);
	}

	private void $Rougamo_Unload()
	{
		if (!RoleTable.Instance.enchasedDict.ContainsKey(base.dataConfig.InstanceID))
		{
			UIManager.Instance.GetUI<CaptionUI>("CaptionUI").ShowCaption("该卡牌尚未被镶嵌", CaptionStyle.Top, 1f, 1.5f, 3);
			return;
		}
		DataConfig dataConfig = RoleTable.Instance.enchasedDict[base.dataConfig.InstanceID];
		base.dataConfig.data["Description"].Remove(base.dataConfig.data["Description"].LastIndexOf(dataConfig.data["Description"]), dataConfig.data["Description"].Length);
		base.dataConfig.scriptExecutor.RunScript("UnloadScript");
		base.dataConfig.PreCompileScripts();
		base.dataConfig.scriptExecutor.RunScript("InitScript");
		base.itemDescription = base.dataConfig.Description().Highlight(keywords);
		base.gameObject.GetComponent<KeywordDisplay>().SetText(base.itemName, CreateTooltipText(), keywords);
		base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().text = base.itemDescription;
		Singleton<GameRuntimeData>.Instance.Save();
		RoleTable.Instance.enchasedDict.Remove(base.dataConfig.InstanceID);
		base.transform.Find("Front/icon/Ench").gameObject.SetActive(value: false);
	}
}
