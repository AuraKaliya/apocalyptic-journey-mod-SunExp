using System;
using System.Diagnostics;
using System.Reflection;
using DG.Tweening;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Witch.UI.Window;

public class SafeBoxItem : Item
{
	public SafeBoxUI safeBoxUI;

	public bool InBackPack = true;

	public bool hasInBack;

	public Tooltip tooltip;

	public CanvasGroup normalCanvasGroup;

	public CanvasGroup highlightCanvasGroup;

	[DebuggerStepThrough]
	public override void Init(DataConfig dataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
	public override void DataUpdate()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
	public override string CreateTooltipText()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			string result = $Rougamo_CreateTooltipText();
			modifiable.OnSuccess(methodContext);
			return result;
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
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
	public override void OnPointerClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
	public override void OnDestroy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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

	[DebuggerStepThrough]
	public override void AddToList(SwapContentIdentity item)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
		methodContext.Arguments = new object[1] { item };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_AddToList(item);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public override void OnTransformParentChanged()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnTransformParentChanged();
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
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
	private void OnDisable()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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
	public override void OnPointerExit(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(SafeBoxItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(SafeBoxItem).TypeHandle);
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

	private void $Rougamo_Init(DataConfig dataConfig)
	{
		base.Init(dataConfig);
		AddToParent();
		if (ItemType == "Relic")
		{
			normalCanvasGroup.transform.Find("Icon Parent/Icon").GetComponent<Image>().sprite = itemIcon;
			highlightCanvasGroup.transform.Find("Icon Parent/Icon").GetComponent<Image>().sprite = itemIcon;
			return;
		}
		if (InBackPack)
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

	private void $Rougamo_OnPointerEnter(PointerEventData eventData)
	{
		base.OnPointerEnter(eventData);
		if (ItemType == "Relic")
		{
			normalCanvasGroup.DOFade(0f, 0.2f);
			highlightCanvasGroup.DOFade(1f, 0.2f);
		}
	}

	private void $Rougamo_DataUpdate()
	{
		if (dataConfig != null)
		{
			if (ItemType == "Relic" || !InBackPack)
			{
				base.DataUpdate();
				return;
			}
			dataConfig.scriptExecutor.Self = null;
			ICard.SetCardMsg(base.transform, dataConfig);
		}
	}

	private string $Rougamo_CreateTooltipText()
	{
		string text = "";
		if (ItemType == "Card")
		{
			text = text + "<color=yellow><b>" + dataConfig.data.Localize("Name") + "</b></color>\n";
			text = text + "<color=white>" + "type".Localize("Glossary") + ": " + "card".Localize("Glossary") + "</color>\n";
			text = text + "<color=white>" + "power".Localize("Glossary") + ": " + dataConfig.data["Expend"] + "</color>\n";
			return text + "<color=white>" + "effect".Localize("Glossary") + ": \n" + dataConfig.Description().Highlight(keywords) + "</color>\n";
		}
		if (ItemType == "Relic")
		{
			Rarity = Singleton<TempDataManager>.Instance.RarityMap[dataConfig.data["Rarity"]].Localize("Tips");
			color = Singleton<TempDataManager>.Instance.rareColorMap1[Singleton<TempDataManager>.Instance.RarityMap[dataConfig.data["Rarity"]]];
			text = text + "<color=yellow><b>" + dataConfig.data.Localize("Name") + "</b></color>\n";
			text = text + "<color=white>" + "type".Localize("Glossary") + ": " + "relic".Localize("Glossary") + "</color>\n";
			text = text + "<color=" + color + ">" + "rarity".Localize("Glossary") + ": " + Rarity + "</color>\n";
			return text + "<color=white>" + "effect".Localize("Glossary") + ": \n" + dataConfig.Description().Highlight(keywords) + "</color>\n";
		}
		return text;
	}

	private void $Rougamo_OnDrag(PointerEventData eventData)
	{
		if (eventData.button == PointerEventData.InputButton.Left)
		{
			if (base.gameObject.GetComponent<LayoutElement>() != null)
			{
				base.gameObject.GetComponent<LayoutElement>().ignoreLayout = true;
			}
			isDrag = true;
			base.transform.GetComponent<RectTransform>().localPosition = GetMousePos(eventData);
		}
	}

	private void $Rougamo_OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button == PointerEventData.InputButton.Right)
		{
			safeBoxUI.ShowFloatingWindow(this);
		}
	}

	private void $Rougamo_OnDestroy()
	{
		ClearEvent();
	}

	private void $Rougamo_AddToList(SwapContentIdentity item)
	{
		if (item == null || item.Content == base.transform.parent)
		{
			base.transform.GetComponent<RectTransform>().localPosition = lastPos;
			base.gameObject.GetComponent<CanvasGroup>().blocksRaycasts = true;
			return;
		}
		if ((bool)base.transform.GetComponent<CanvasGroup>())
		{
			base.transform.GetComponent<CanvasGroup>().blocksRaycasts = true;
		}
		Transform parent = base.transform.parent;
		if (ItemType == "Relic" && ((item.name == "Scroll Area" && parent.parent.name == "ButtonScroll Area") || (parent.parent.name == "Scroll Area" && item.name == "ButtonScroll Area") || item.transform == parent.parent))
		{
			base.AddToList(item);
		}
		else if (InBackPack)
		{
			safeBoxUI.PutIntoStore(base.gameObject);
		}
		else
		{
			safeBoxUI.PutItBack(base.gameObject);
		}
		if (parent == base.transform.parent)
		{
			base.transform.GetComponent<RectTransform>().localPosition = lastPos;
		}
	}

	private void $Rougamo_OnTransformParentChanged()
	{
		if (!base.gameObject.activeSelf || ItemType != "Relic")
		{
			return;
		}
		RemoveFromParent();
		AddToParent();
		RoleTable.Instance.relicList.Remove(dataConfig);
		RoleTable.Instance.WithoutArmedRelicList.Remove(dataConfig);
		if (base.transform.parent.parent.name == "Scroll Area")
		{
			ifEquipped = true;
			RoleTable.Instance.relicList.Add(dataConfig);
		}
		else
		{
			ifEquipped = false;
			if (!base.transform.parent.parent.name.Contains("OutScroll Area"))
			{
				RoleTable.Instance.WithoutArmedRelicList.Add(dataConfig);
			}
		}
		if (UIManager.Instance.GetUI<TopBarUI>("TopBarUI") != null)
		{
			UIManager.Instance.GetUI<TopBarUI>("TopBarUI").UpdateRelics();
		}
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
		base.OnEndDrag(eventData);
		if (base.gameObject.GetComponent<LayoutElement>() != null)
		{
			base.gameObject.GetComponent<LayoutElement>().ignoreLayout = false;
		}
	}

	private void $Rougamo_OnDisable()
	{
		floatingWindow.Hide();
	}

	private void $Rougamo_OnPointerExit(PointerEventData eventData)
	{
		safeBoxUI.HideTooltip();
		if (ItemType == "Relic")
		{
			normalCanvasGroup.DOFade(1f, 0.2f);
			highlightCanvasGroup.DOFade(0f, 0.2f);
		}
	}
}
