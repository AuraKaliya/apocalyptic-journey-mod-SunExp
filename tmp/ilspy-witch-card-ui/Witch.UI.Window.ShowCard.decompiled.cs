using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Data.Save;
using Loxodon.Framework.Obfuscation;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Witch.UI.Window;

public class ShowCard : Item
{
	public OutDeckUI cardShowUI;

	public int DestroyCost = 20;

	private bool fromSelf = true;

	private List<string> keyWords = new List<string>();

	private bool hasEnch;

	private new bool ifEquipped
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(ShowCard);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				bool result = $Rougamo_get_ifEquipped();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[DebuggerStepThrough]
		set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(ShowCard);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_ifEquipped(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	[DebuggerStepThrough]
	public void Init(DataConfig dataConfig, bool ifequipped, bool fromSelf)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[3] { dataConfig, ifequipped, fromSelf };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Init(dataConfig, ifequipped, fromSelf);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void OnEnable()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnEnable();
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
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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
	public void MoveItem()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_MoveItem();
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
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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
	public override void OnBeginDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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
	public override void OnPointerClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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
	private void OnDisable()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnDisable();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void DecomposeItem()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DecomposeItem();
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
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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
	public void ItemCheck()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ItemCheck();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void SellItem()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SellItem();
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
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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
	public override void AddToList(SwapContentIdentity content)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
		methodContext.Arguments = new object[1] { content };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_AddToList(content);
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
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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

	private async UniTask OnHoverDelayAsync()
	{
		float totTime = 0.4f;
		while (isHover && totTime >= 0f)
		{
			if (!isHover)
			{
				return;
			}
			await UniTask.WaitForSeconds(0.05f, ignoreTimeScale: false, PlayerLoopTiming.Update, Singleton<GameConfigManager>.Instance.cts.Token);
			totTime -= 0.05f;
		}
		_ = isHover;
	}

	[DebuggerStepThrough]
	public override void OnPointerExit(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(ShowCard);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ShowCard).TypeHandle);
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

	[SpecialName]
	private bool $Rougamo_get_ifEquipped()
	{
		return base.ifEquipped;
	}

	[SpecialName]
	private void $Rougamo_set_ifEquipped(bool value)
	{
		if (base.ifEquipped == value)
		{
			return;
		}
		base.ifEquipped = value;
		if (!base.gameObject.activeSelf)
		{
			return;
		}
		if (!base.ifEquipped)
		{
			if (RoleTable.Instance.cardList.Contains(dataConfig))
			{
				AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/取出卡牌");
				RoleTable.Instance.cardList.Remove(dataConfig);
				RoleTable.Instance.UnCardList.Add(dataConfig);
			}
		}
		else if (RoleTable.Instance.UnCardList.Contains(dataConfig))
		{
			AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/置入卡牌");
			RoleTable.Instance.UnCardList.Remove(dataConfig);
			RoleTable.Instance.cardList.Add(dataConfig);
		}
	}

	private void $Rougamo_Init(DataConfig dataConfig, bool ifequipped, bool fromSelf)
	{
		ifEquipped = ifequipped;
		this.fromSelf = fromSelf;
		ItemType = "Card";
		base.Init(dataConfig);
		DestroyCost = 20 + GameSaveManager.GetValue<int>(GameVar.ExpensiveCard);
		AddToParent();
		if (!ifequipped)
		{
			ICard.SetCardStyle(base.transform, dataConfig);
			return;
		}
		int b = int.Parse(dataConfig.data["Expend"]) + int.Parse(dataConfig.Vars.GetValueOrDefault("TotalExCost", "0"));
		b = Mathf.Max(0, b);
		base.transform.Find("Cost").GetComponent<TMP_Text>().text = b.ToString();
		base.transform.Find("Name").GetComponent<TMP_Text>().text = dataConfig.data.Localize("Name");
		Sprite sprite = ResourceLoader.Load<Sprite>(dataConfig.data["Icon"]);
		if (sprite == null)
		{
			sprite = ResourceLoader.Load<Sprite>("Icon/Card/卡面占位");
		}
		itemIcon = sprite;
		if (RoleTable.Instance.enchasedDict.ContainsKey(dataConfig.InstanceID))
		{
			Sprite sprite2 = ResourceLoader.Load<Sprite>(RoleTable.Instance.enchasedDict[dataConfig.InstanceID].data["Icon"]);
			if (sprite2 != null)
			{
				base.transform.Find("Ench").gameObject.SetActive(value: true);
				base.transform.Find("Ench").GetComponent<Image>().sprite = sprite2;
			}
		}
		else
		{
			base.transform.Find("Ench").gameObject.SetActive(value: false);
		}
		base.transform.Find("Mask/CardIcon").GetComponent<Image>().sprite = sprite;
	}

	private void $Rougamo_OnEnable()
	{
		DataUpdate();
	}

	private void $Rougamo_DataUpdate()
	{
		if (dataConfig == null)
		{
			return;
		}
		if (FightManager.Instance != null && FightManager.Instance.fightType == FightType.None)
		{
			dataConfig.scriptExecutor.Self = null;
		}
		base.itemDescription = dataConfig.Description().Highlight(keyWords);
		if (!ifEquipped)
		{
			ICard.SetCardMsg(base.transform, dataConfig);
			return;
		}
		string text = "";
		text = text + "<color=" + Singleton<TempDataManager>.Instance.rareColorMap1[Singleton<TempDataManager>.Instance.RarityMap[dataConfig.data["Rarity"]]] + ">" + "rarity".Localize("Glossary") + ": " + Singleton<TempDataManager>.Instance.RarityMap[dataConfig.data["Rarity"]].Localize("Tips") + "</color>\n";
		text = text + "<color=white>" + "effect".Localize("Glossary") + ": " + base.itemDescription + "</color>\n";
		int b = int.Parse(dataConfig.data["Expend"]) + int.Parse(dataConfig.Vars.GetValueOrDefault("TotalExCost", "0"));
		b = Mathf.Max(0, b);
		if (dataConfig.data.ContainsKey("Expend"))
		{
			text += ZString.Format("<color=white>{0}: {1}</color>\n", (object)"power".Localize("Glossary"), (object)b);
		}
		if (RoleTable.Instance != null)
		{
			DataConfig valueOrDefault = RoleTable.Instance.enchasedDict.GetValueOrDefault(dataConfig.InstanceID, null);
			if (valueOrDefault != null)
			{
				keyWords.Add(valueOrDefault.data.Localize("Name"));
				text = text + "<color=yellow>" + valueOrDefault.data.Localize("Name") + "</color>";
			}
		}
		base.transform.GetComponent<KeywordDisplay>().SetText(dataConfig.data.Localize("Name"), text, keyWords, null, keywordDisplay.icon = ResourceLoader.Load<Sprite>("Icon/Item/Rarity" + dataConfig.data["Rarity"]), ItemType.Localize("Glossary"));
	}

	private void $Rougamo_MoveItem()
	{
		if (ifEquipped && RoleTable.Instance.UnCardList.Count < RoleTable.Instance.MaxAlCardCount && RoleTable.Instance.cardList.Count > RoleTable.Instance.CardBottomCount)
		{
			AddToList(cardShowUI.unequipCardTransform.parent.parent);
		}
		else if (!ifEquipped && RoleTable.Instance.cardList.Count < RoleTable.Instance.CardTopCount)
		{
			AddToList(cardShowUI.equipCardTransform.parent.parent);
		}
		else if (ifEquipped && RoleTable.Instance.cardList.Count <= RoleTable.Instance.CardBottomCount)
		{
			UIManager.Instance.ShowModalWindow("Tips", "The number of cards has reached the lower limit");
		}
		else
		{
			UIManager.Instance.ShowModalWindow("Tips", "The number of cards has reached the upper limit");
		}
	}

	private void $Rougamo_ShowFloatingWindow()
	{
		if (!fromSelf)
		{
			return;
		}
		floatingWindow.Clear();
		floatingWindow.AddButton("move", delegate
		{
			MoveItem();
			HideFloatingWindow();
		});
		if (UIManager.Instance.GetUI<ShopUI>("ShopUI") != null || (bool)UIManager.Instance.GetUI<DestinyTreeUI>("DestinyTreeUI") || (bool)UIManager.Instance.GetUI<CardEnchUI>("CardEnchUI"))
		{
			floatingWindow.AddButton("sell".Localize("Button") + (int)((float)(20 * int.Parse(dataConfig.data.GetValueOrDefault("Rarity", "1"))) * RoleTable.Instance.MoneyCal), delegate
			{
				SellItem();
				HideFloatingWindow();
			});
		}
		else
		{
			floatingWindow.AddButton("Destroy".Localize("Button") + ZString.Format("(-{0})", (object)DestroyCost), delegate
			{
				DecomposeItem();
				HideFloatingWindow();
			});
		}
		base.ShowFloatingWindow();
	}

	private void $Rougamo_OnBeginDrag(PointerEventData eventData)
	{
		if (fromSelf)
		{
			base.OnBeginDrag(eventData);
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

	private void $Rougamo_OnDisable()
	{
		HideFloatingWindow();
	}

	private void $Rougamo_DecomposeItem()
	{
		if (dataConfig.Vars["Tag"].Contains("Eternal"))
		{
			UIManager.Instance.ShowModalWindow("Shop", "This card cannot be removed".Localize("ShopUI"), delegate
			{
			});
		}
		else if (RoleTable.Instance.Money < DestroyCost)
		{
			UIManager.Instance.ShowModalWindow("Shop", "Currently, there are too few coins to break down cards");
		}
		else if ((ifEquipped && RoleTable.Instance.cardList.Count > RoleTable.Instance.CardBottomCount) || !ifEquipped)
		{
			if (dataConfig.data["Type"] == "诅咒" && RoleTable.Instance.VarsMap["Wisdom"] < DestroyCost)
			{
				UIManager.Instance.GetUI<CaptionUI>("CaptionUI").ShowCaption("你目前还没有消除诅咒的能力", CaptionStyle.Top, 1f, 1.5f, 3);
				return;
			}
			RoleTable.Instance.Money -= (ObfuscatedInt)DestroyCost;
			ItemCheck();
		}
		else
		{
			UIManager.Instance.ShowModalWindow("Tips", "The number of cards has reached the lower limit");
		}
	}

	private void $Rougamo_OnDrag(PointerEventData eventData)
	{
		if (fromSelf && eventData.button == PointerEventData.InputButton.Left)
		{
			base.gameObject.GetComponent<LayoutElement>().ignoreLayout = true;
			isDrag = true;
			base.transform.GetComponent<RectTransform>().localPosition = GetMousePos(eventData);
		}
	}

	private void $Rougamo_ItemCheck()
	{
		if (ifEquipped)
		{
			RoleTable.Instance.cardList.Remove(dataConfig);
		}
		else
		{
			RoleTable.Instance.UnCardList.Remove(dataConfig);
		}
		GameSaveAnalyser.Instance.TryPushNative(dataConfig, OperObj.Cards, OperType.Delete);
		Singleton<ObjectPool>.Instance.Release(base.gameObject);
		foreach (Transform item in base.transform.parent)
		{
			if (item.name == "Null" && !item.gameObject.activeSelf && ((ifEquipped && RoleTable.Instance.cardList.Count < RoleTable.Instance.CardTopCount) || (!ifEquipped && RoleTable.Instance.UnCardList.Count < RoleTable.Instance.MaxAlCardCount)))
			{
				item.gameObject.SetActive(value: true);
				break;
			}
		}
		cardShowUI.ChangeCardShow();
	}

	private void $Rougamo_SellItem()
	{
		if (dataConfig.Vars["Tag"].Contains("Eternal"))
		{
			UIManager.Instance.ShowModalWindow("Shop", "This card cannot be removed".Localize("ShopUI"), delegate
			{
			});
		}
		else if ((ifEquipped && RoleTable.Instance.cardList.Count > RoleTable.Instance.CardBottomCount) || !ifEquipped)
		{
			if (dataConfig.data["Type"] == "诅咒" && RoleTable.Instance.VarsMap["Wisdom"] < 20)
			{
				UIManager.Instance.GetUI<CaptionUI>("CaptionUI").ShowCaption("你目前还没有消除诅咒的能力", CaptionStyle.Top, 1f, 1.5f, 3);
				return;
			}
			RoleTable.Instance.Money += (ObfuscatedInt)(20 * int.Parse(dataConfig.data.GetValueOrDefault("Rarity", "1")));
			ItemCheck();
		}
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
		if (!fromSelf)
		{
			return;
		}
		isDrag = false;
		PointerEventData eventData2 = new PointerEventData(EventSystem.current)
		{
			position = KeyManager.playerAction.Main.Point.ReadValue<Vector2>()
		};
		List<RaycastResult> list = new List<RaycastResult>();
		EventSystem.current.RaycastAll(eventData2, list);
		SwapContentIdentity swapContentIdentity = null;
		foreach (RaycastResult item in list)
		{
			swapContentIdentity = item.gameObject.GetComponent<SwapContentIdentity>();
			if (swapContentIdentity != null && (!swapContentIdentity.CheckType || swapContentIdentity.ItemName == ItemType))
			{
				break;
			}
		}
		base.gameObject.GetComponent<LayoutElement>().ignoreLayout = false;
		if ((bool)base.transform.GetComponent<Canvas>())
		{
			base.transform.GetComponent<Canvas>().overrideSorting = false;
			UnityEngine.Object.Destroy(base.transform.GetComponent<Canvas>());
		}
		base.gameObject.GetComponent<CanvasGroup>().blocksRaycasts = true;
		ScrollRect componentInParent = lastParent.GetComponentInParent<ScrollRect>();
		if (componentInParent != null)
		{
			componentInParent.enabled = true;
		}
		else if ((bool)lastParent.GetComponentInParent<ScrollRectNonDrag>())
		{
			lastParent.GetComponentInParent<ScrollRectNonDrag>().enabled = true;
		}
		AddToList(swapContentIdentity);
	}

	private void $Rougamo_AddToList(SwapContentIdentity content)
	{
		if (content == null || content.transform == base.transform.parent.parent.parent)
		{
			return;
		}
		if ((!ifEquipped || RoleTable.Instance.UnCardList.Count >= RoleTable.Instance.MaxAlCardCount || RoleTable.Instance.cardList.Count <= RoleTable.Instance.CardBottomCount) && (ifEquipped || RoleTable.Instance.cardList.Count >= RoleTable.Instance.CardTopCount))
		{
			base.transform.localPosition = lastPos;
			GetComponent<CanvasGroup>().blocksRaycasts = true;
			if ((bool)GetComponent<Canvas>())
			{
				GetComponent<Canvas>().overrideSorting = false;
				UnityEngine.Object.Destroy(GetComponent<Canvas>());
			}
			if (ifEquipped && RoleTable.Instance.cardList.Count <= RoleTable.Instance.CardBottomCount)
			{
				UIManager.Instance.ShowModalWindow("Tips", "The number of cards has reached the lower limit");
			}
			else
			{
				UIManager.Instance.ShowModalWindow("Tips", "The number of cards has reached the upper limit");
			}
			return;
		}
		lastParent = base.transform.parent;
		isDrag = false;
		Transform nullItem = lastParent.GetChild(lastParent.childCount - 1);
		UniTask.WaitForEndOfFrame().ContinueWith(delegate
		{
			if (!(nullItem == null) && !(nullItem.gameObject == null) && !(nullItem.name != "Null"))
			{
				if ((ifEquipped && RoleTable.Instance.cardList.Count < RoleTable.Instance.CardTopCount) || (!ifEquipped && RoleTable.Instance.UnCardList.Count < RoleTable.Instance.MaxAlCardCount))
				{
					nullItem.gameObject.SetActive(value: true);
					int num = nullItem.GetSiblingIndex();
					while (num > 0 && !nullItem.parent.GetChild(num - 1).gameObject.activeSelf)
					{
						num--;
					}
					nullItem.SetSiblingIndex(num);
				}
				if (this != null && base.transform.parent != null)
				{
					lastParent = base.transform.parent;
				}
			}
		}).Forget();
		if (ifEquipped)
		{
			if (!RoleTable.Instance.cardList.Contains(dataConfig))
			{
				return;
			}
			AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/取出卡牌");
			RoleTable.Instance.cardList.Remove(dataConfig);
			RoleTable.Instance.UnCardList.Add(dataConfig);
		}
		else
		{
			if (!RoleTable.Instance.UnCardList.Contains(dataConfig))
			{
				return;
			}
			AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/置入卡牌");
			RoleTable.Instance.UnCardList.Remove(dataConfig);
			RoleTable.Instance.cardList.Add(dataConfig);
		}
		cardShowUI.CreateItem(dataConfig, !ifEquipped);
		Singleton<ObjectPool>.Instance.Release(base.gameObject);
		cardShowUI.ChangeCardShow();
	}

	private void $Rougamo_OnPointerEnter(PointerEventData eventData)
	{
		isHover = true;
		UniTask.ToCoroutine(OnHoverDelayAsync);
	}

	private void $Rougamo_OnPointerExit(PointerEventData eventData)
	{
		if (!isDrag)
		{
			base.OnPointerExit(eventData);
		}
	}
}
