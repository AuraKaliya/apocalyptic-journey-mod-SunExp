using System;
using System.Diagnostics;
using System.Reflection;
using Network.Command;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Witch.UI.Window;

public class WarehouseItem : Item
{
	public bool Inwarehouse;

	public WarehouseUI warehouseUI;

	[DebuggerStepThrough]
	public void Init(bool isware, bool equipped, DataConfig dataConfig = null)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
		methodContext.Arguments = new object[3] { isware, equipped, dataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Init(isware, equipped, dataConfig);
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
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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
	public void TryMove()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_TryMove();
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
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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
	public override void OnPointerClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(WarehouseItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(WarehouseItem).TypeHandle);
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

	private void $Rougamo_Init(bool isware, bool equipped, DataConfig dataConfig = null)
	{
		Inwarehouse = isware;
		ifEquipped = equipped;
		if (dataConfig != null)
		{
			base.Init(dataConfig);
		}
		if (ItemType == "Card")
		{
			ICard.SetCardStyle(base.transform.Find("CardItem"), dataConfig);
			base.transform.Find("Item").gameObject.SetActive(value: false);
			base.transform.Find("CardItem").gameObject.SetActive(value: true);
		}
		else
		{
			base.transform.Find("Item").gameObject.SetActive(value: true);
			base.transform.Find("CardItem").gameObject.SetActive(value: false);
		}
		DataUpdate();
	}

	private void $Rougamo_DataUpdate()
	{
		base.DataUpdate();
		if (ItemType == "Card")
		{
			ICard.SetCardMsg(base.transform.Find("CardItem"), dataConfig);
			base.transform.Find("cost/Background/Title").GetComponent<TMP_Text>().text = "Expend:".Localize("ShopUI") + dataConfig.data["Expend"];
			base.transform.Find("Des").GetComponent<TMP_Text>().text = base.itemDescription;
		}
		if (ItemType == "Relic")
		{
			base.transform.Find("cost/Background/Title").GetComponent<TMP_Text>().text = "RELIC".Localize("ShopUI");
			base.transform.Find("Des").GetComponent<TMP_Text>().text = base.itemDescription.Replace("\n", "");
			base.transform.Find("Item/Normal/Icon").GetComponent<Image>().sprite = itemIcon;
			base.transform.Find("Item/Highlight/Icon").GetComponent<Image>().sprite = itemIcon;
		}
		base.transform.Find("rarity/Background/Title").GetComponent<TMP_Text>().text = "Rarity:".Localize("ShopUI") + Rarity;
		base.transform.Find("name/Background/Title").GetComponent<TMP_Text>().text = "【" + base.itemName + "】";
	}

	private void $Rougamo_TryMove()
	{
		if (PlayerManager.Instance == null)
		{
			return;
		}
		if (ItemType == "Card" && dataConfig.Vars["Tag"].Contains("Eternal"))
		{
			UIManager.Instance.ShowModalWindow("Shop", "This card cannot be removed".Localize("ShopUI"), delegate
			{
			});
			return;
		}
		if (Inwarehouse)
		{
			if (ItemType == "Card" && RoleTable.Instance.UnCardList.Count < RoleTable.Instance.MaxAlCardCount)
			{
				if (!warehouseUI.MoveCheck("Card", isGet: true))
				{
					UIManager.Instance.ShowModalWindow("Shop", "This resting place cannot obtain more cards.");
					return;
				}
				PlayerManager.Instance.SendRpcCommand(new RpcGetItem("Card", dataConfig, PlayerManager.Instance.PlayerId));
			}
			else
			{
				if (!(ItemType == "Relic"))
				{
					UIManager.Instance.ShowModalWindow("Shop", "The number of cards has reached the upper limit");
					return;
				}
				if (!warehouseUI.MoveCheck("Relic", isGet: true))
				{
					UIManager.Instance.ShowModalWindow("Shop", "This resting place cannot obtain more relics.");
					return;
				}
				PlayerManager.Instance.SendRpcCommand(new RpcGetItem("Relic", dataConfig, PlayerManager.Instance.PlayerId));
			}
		}
		else if (ItemType == "Card" && ((ifEquipped && RoleTable.Instance.cardList.Count > RoleTable.Instance.CardBottomCount) || !ifEquipped))
		{
			if (!warehouseUI.MoveCheck("Card", isGet: false))
			{
				UIManager.Instance.ShowModalWindow("Shop", "This resting place cannot send out more cards.");
				return;
			}
			RoleTable.Instance.cardList.Remove(dataConfig);
			RoleTable.Instance.UnCardList.Remove(dataConfig);
			PlayerManager.Instance.SendRpcCommand(new RpcSendItem("Card", dataConfig));
		}
		else
		{
			if (!(ItemType == "Relic"))
			{
				UIManager.Instance.ShowModalWindow("Shop", "The number of cards has reached the lower limit");
				return;
			}
			if (!warehouseUI.MoveCheck("Relic", isGet: false))
			{
				UIManager.Instance.ShowModalWindow("Shop", "This resting place cannot send out more relics.");
				return;
			}
			RoleTable.Instance.relicList.Remove(dataConfig);
			RoleTable.Instance.WithoutArmedRelicList.Remove(dataConfig);
			PlayerManager.Instance.SendRpcCommand(new RpcSendItem("Relic", dataConfig));
		}
		Inwarehouse = !Inwarehouse;
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

	private void $Rougamo_OnPointerClick(PointerEventData eventData)
	{
	}
}
