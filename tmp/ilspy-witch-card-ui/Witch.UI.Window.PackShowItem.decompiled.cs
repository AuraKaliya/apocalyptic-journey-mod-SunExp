using System;
using System.Diagnostics;
using System.Reflection;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Witch.UI.Window;

public class PackShowItem : ItemNonDrag
{
	[DebuggerStepThrough]
	public override void Init(DataConfig data)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(PackShowItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PackShowItem).TypeHandle);
		methodContext.Arguments = new object[1] { data };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Init(data);
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
		methodContext.TargetType = typeof(PackShowItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PackShowItem).TypeHandle);
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

	private void $Rougamo_Init(DataConfig data)
	{
		base.Init(data);
		if (dataConfig.Type == DataType.Card)
		{
			base.transform.Find("CardItem").gameObject.SetActive(value: true);
			base.transform.Find("RelicCard").gameObject.SetActive(value: false);
			ICard.SetCardStyle(base.transform.Find("CardItem"), dataConfig);
			ICard.SetCardMsg(base.transform.Find("CardItem"), dataConfig);
		}
		else
		{
			base.transform.Find("CardItem").gameObject.SetActive(value: false);
			base.transform.Find("RelicCard").gameObject.SetActive(value: true);
			base.transform.Find("RelicCard/Front/icon").GetComponent<Image>().sprite = ResourceLoader.Load<Sprite>(dataConfig.data["Icon"]);
			base.transform.Find("RelicCard/Front/字体/nameTxt").GetComponent<TMP_Text>().text = dataConfig.data.Localize("Name");
			base.transform.Find("RelicCard/Front/字体/msgTxt").GetComponent<TMP_Text>().text = base.itemDescription;
		}
	}

	private void $Rougamo_DataUpdate()
	{
		base.itemName = dataConfig.data.Localize("Name");
		base.itemDescription = dataConfig.Description()?.Highlight(keywords) ?? "";
		base.itemTip = dataConfig.data.Localize("Tips");
		keywordDisplay.text = CreateTooltipText();
		keywordDisplay.title = base.itemName;
		keywordDisplay.keyWords = keywords;
		keywordDisplay.msg = base.itemTip;
		keywordDisplay.type = ItemType.Localize("Glossary");
		Rarity = dataConfig.data.GetValueOrDefault("Rarity", null);
		if (!string.IsNullOrEmpty(Rarity))
		{
			keywordDisplay.icon = ResourceLoader.Load<Sprite>("Icon/Item/Rarity" + dataConfig.data["Rarity"]);
			if (Singleton<TempDataManager>.Instance?.RarityMap != null && Singleton<TempDataManager>.Instance.RarityMap.TryGetValue(dataConfig.data["Rarity"], out var value))
			{
				rareLevel = value.Localize("Tips");
			}
		}
		else
		{
			keywordDisplay.icon = itemIcon;
		}
	}
}
