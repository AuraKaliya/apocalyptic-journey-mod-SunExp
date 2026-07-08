using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Data.Save;
using Loxodon.Framework.Obfuscation;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Witch.UI.Window;

public class ShopItem : ItemNonDrag
{
	public ShopUI shop;

	public Dice dice;

	private bool canUpdate;

	private static bool firstBuy = true;

	[DebuggerStepThrough]
	public override void Init(DataConfig dataConfig = null)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public void Init()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Init();
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
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public override void OnPointerClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public override void OnPointerEnter(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public override void OnPointerExit(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public override void OnDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public override void OnBeginDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
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
	public void TryBuy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_TryBuy();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void EnchTryBuy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_EnchTryBuy();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private int PriceCalculate()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			int result = $Rougamo_PriceCalculate();
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UpdateItem()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShopItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShopItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdateItem();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private void $Rougamo_Init(DataConfig dataConfig = null)
	{
		if (dataConfig != null)
		{
			base.Init(dataConfig);
		}
		dice = MapManager.Instance.NowDice;
		canUpdate = true;
		if (ItemType == "Card")
		{
			ICard.SetCardStyle(base.transform.Find("CardItem"), dataConfig);
		}
		DataUpdate();
	}

	private void $Rougamo_Init()
	{
		keywordDisplay.title = "BLESS".Localize("ShopUI");
		keywordDisplay.text = "Buy random blessings".Localize("ShopUI");
		keywordDisplay.icon = ResourceLoader.Load<Sprite>("Icon/Item/Rarity3");
		keywordDisplay.enabled = true;
		canUpdate = true;
		DataUpdate();
	}

	private void $Rougamo_DataUpdate()
	{
		if (!canUpdate)
		{
			return;
		}
		if (ItemType != "RandomBlessing")
		{
			base.DataUpdate();
			if (ItemType == "EnchTag")
			{
				base.itemDescription = dataConfig.Description().Highlight(keywords);
			}
			keywordDisplay.text = CreateTooltipText();
			for (int i = 1; i <= Math.Min(int.Parse(dataConfig.data["Rarity"]), 3); i++)
			{
				base.transform.Find("Item/RarityList/" + i).gameObject.SetActive(value: true);
			}
			for (int j = int.Parse(dataConfig.data["Rarity"]) + 1; j <= 3; j++)
			{
				base.transform.Find("Item/RarityList/" + j).gameObject.SetActive(value: false);
			}
		}
		else
		{
			for (int k = 1; k <= 3; k++)
			{
				base.transform.Find("Item/RarityList/" + k).gameObject.SetActive(value: true);
			}
		}
		if (ItemType == "Card")
		{
			keywordDisplay.enabled = false;
			keywordDisplay.icon = null;
			ICard.SetCardMsg(base.transform.Find("CardItem"), dataConfig);
			base.transform.Find("Item").gameObject.SetActive(value: false);
			base.transform.Find("CardItem").gameObject.SetActive(value: true);
		}
		else
		{
			keywordDisplay.enabled = true;
			base.transform.Find("Item").gameObject.SetActive(value: true);
			base.transform.Find("CardItem").gameObject.SetActive(value: false);
			base.transform.Find("Item/Des").GetComponent<TMP_Text>().text = base.itemDescription;
		}
		if (ItemType == "Relic" || ItemType == "Bless" || ItemType == "EnchTag")
		{
			base.transform.Find("Item/Normal/Icon").GetComponent<Image>().sprite = itemIcon;
			base.transform.Find("Item/Highlight/Icon").GetComponent<Image>().sprite = itemIcon;
			base.transform.Find("Item/Normal/Icon").GetComponent<Image>().SetNativeSize();
			base.transform.Find("Item/Highlight/Icon").GetComponent<Image>().SetNativeSize();
		}
		if (ItemType == "RandomBlessing")
		{
			base.transform.Find("Item/name/Title").GetComponent<TMP_Text>().text = "RandomBlessing".Localize("ShopUI");
			base.transform.Find("Item/Normal/Icon").GetComponent<Image>().SetNativeSize();
			base.transform.Find("Item/Highlight/Icon").GetComponent<Image>().SetNativeSize();
		}
		else if (ItemType != "Card")
		{
			base.transform.Find("Item/name/Title").GetComponent<TMP_Text>().text = base.itemName;
		}
		itemPrice = PriceCalculate();
		if (RoleTable.Instance.InHighTide)
		{
			itemPrice = itemPrice * 6 / 5;
		}
		UpdateItem();
	}

	private void $Rougamo_OnPointerClick(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnPointerEnter(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnPointerExit(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnBeginDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
	}

	private void $Rougamo_TryBuy()
	{
		if (!(RoleTable.Instance.Money >= itemPrice))
		{
			return;
		}
		if (ItemType == "Card")
		{
			if (RoleTable.Instance.UnCardList.Count >= RoleTable.Instance.MaxAlCardCount)
			{
				UIManager.Instance.ShowModalWindow("Tips", "你的卡牌数量已达上限", delegate
				{
					if (UIManager.Instance.GetUI<OutDeckUI>("OutDeckUI") != null)
					{
						UIManager.Instance.GetUI<OutDeckUI>("OutDeckUI").SetRole(new OutDeckUIData(RoleTable.Instance));
					}
					UIManager.Instance.ShowUI<OutDeckUI>("OutDeckUI");
				});
				return;
			}
			GameSaveAnalyser.Instance.TryPushNative(dataConfig, OperObj.Cards, OperType.Buy);
			RoleTable.Instance.UnCardList.Add(dataConfig);
		}
		else if (ItemType == "Relic")
		{
			if (firstBuy && RoleTable.Instance.relicList.Count >= 6)
			{
				firstBuy = false;
				UIManager.Instance.ShowModalWindow("Tips", "You have reached the maximum number of relics. If you purchase more, they will be automatically stored in the warehouse. Do you want to buy it?".Localize("ShopUI"), delegate
				{
					TryBuy();
				});
				return;
			}
			GameSaveAnalyser.Instance.TryPushNative(dataConfig, OperObj.Relics, OperType.Buy);
			RoleTable.Instance.WithoutArmedRelicList.Add(dataConfig);
		}
		else if (ItemType == "Bless")
		{
			GameSaveAnalyser.Instance.TryPushNative(dataConfig, OperObj.Blessings, OperType.Buy);
			RoleTable.Instance.blessingConfigs.Add(dataConfig);
		}
		else if (ItemType == "RandomBlessing")
		{
			UIManager.Instance.GetUI<DestinyTreeUI>("DestinyTreeUI").Divination();
		}
		else if (ItemType == "EnchTag" && UIManager.Instance.GetUI<CardEnchUI>("CardEnchUI") != null)
		{
			UIManager.Instance.GetUI<CardEnchUI>("CardEnchUI").ShowCardToEnch(this);
		}
		RoleTable.Instance.Money -= (ObfuscatedInt)itemPrice;
		if (ItemType != "RandomBlessing" && ItemType != "EnchTag")
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
		else if (ItemType == "RandomBlessing")
		{
			itemPrice = UIManager.Instance.GetUI<DestinyTreeUI>("DestinyTreeUI").Cost;
			UpdateItem();
		}
	}

	private void $Rougamo_EnchTryBuy()
	{
		if (RoleTable.Instance.Money >= itemPrice && ItemType == "EnchTag" && UIManager.Instance.GetUI<CardEnchUI>("CardEnchUI") != null)
		{
			UIManager.Instance.GetUI<CardEnchUI>("CardEnchUI").ShowCardToEnch(this);
		}
	}

	private int $Rougamo_PriceCalculate()
	{
		int num = 0;
		if (ItemType == "Relic")
		{
			num = 150;
		}
		else if (ItemType == "Card")
		{
			num = 50;
		}
		else if (ItemType == "EnchTag")
		{
			num = 80;
		}
		else if (ItemType == "Bless")
		{
			num = 20 * int.Parse(dataConfig.data["Weight"]) - 15;
			if (dataConfig.data["Rarity"] == "3")
			{
				num = 100;
			}
		}
		else if (ItemType == "RandomBlessing")
		{
			return num;
		}
		List<float> list = new List<float> { 1f, 1.3f, 1.6f, 3.5f, 4.5f, 6f, 8f, 10f };
		string text = dataConfig.data["Rarity"];
		int num2 = ((text == null || text == "") ? 1 : int.Parse(dataConfig.data["Rarity"]));
		num = (int)((float)num * list[num2]);
		num = (int)((float)(num * GameSaveManager.GetValue<int>(GameVar.PriceMul)) / 100f);
		return num + dice.WithRange(-num2 * 5, num2 * 5).Roll().Value;
	}

	private void $Rougamo_UpdateItem()
	{
		if (itemPrice > RoleTable.Instance.Money)
		{
			base.transform.Find("val/Normal/Title").GetComponent<TextMeshProUGUI>().text = "<color=red>" + itemPrice + "</color>";
			base.transform.Find("val/Hlight/Title").GetComponent<TextMeshProUGUI>().text = "<color=red>" + itemPrice + "</color>";
		}
		else
		{
			base.transform.Find("val/Normal/Title").GetComponent<TextMeshProUGUI>().text = itemPrice.ToString();
			base.transform.Find("val/Hlight/Title").GetComponent<TextMeshProUGUI>().text = itemPrice.ToString();
		}
	}
}
