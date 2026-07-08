using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DG.Tweening;
using Data.Save;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;

namespace Witch.UI.Window;

public class CardChoiceItem : MonoBehaviour
{
	private Transform cardBack;

	private Transform background;

	private Transform icon;

	private Transform Icons;

	private Transform Texts;

	private DataConfig dataConfig;

	[UnityInject(false)]
	public ObjectGroup objectGroup;

	private Transform light;

	private bool canClick;

	private Material backgroundMat;

	private readonly List<Color> rarityColors = new List<Color>
	{
		new Color(1f, 0.49f, 0.19f, 0.57f),
		new Color(1f, 0.78f, 0.22f, 0.6f),
		new Color(0.95f, 0.42f, 1f, 0.65f)
	};

	private CardChoiceUI cardChoiceUI;

	[DebuggerStepThrough]
	private void Awake()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardChoiceItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardChoiceItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Awake();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void Initialize(CardChoiceUI UI, string fromId)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardChoiceItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardChoiceItem).TypeHandle);
		methodContext.Arguments = new object[2] { UI, fromId };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Initialize(UI, fromId);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void FadeIn(float delay = 0f)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardChoiceItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardChoiceItem).TypeHandle);
		methodContext.Arguments = new object[1] { delay };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_FadeIn(delay);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void DataUpdate()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardChoiceItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardChoiceItem).TypeHandle);
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
	public void FadeOut(float delay = 0f)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardChoiceItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardChoiceItem).TypeHandle);
		methodContext.Arguments = new object[1] { delay };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_FadeOut(delay);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void MoveToDeck()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardChoiceItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardChoiceItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_MoveToDeck();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private void $Rougamo_Awake()
	{
		cardBack = base.transform.Find("Back/background");
		background = base.transform.Find("Front/background");
		light = base.transform.Find("Front/Light");
		icon = base.transform.Find("Front/icon");
		Icons = base.transform.Find("Front/Icons");
		Texts = base.transform.Find("Front/字体");
		objectGroup.alpha = 1f;
		base.transform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().DOFade(0f, 0f);
		base.transform.Find("Front/cost/cost").GetComponent<TMP_Text>().DOFade(0f, 0f);
		base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().DOFade(0f, 0f);
		Texture mainTexture = cardBack.GetComponent<MeshRenderer>().material.mainTexture;
		cardBack.GetComponent<MeshRenderer>().material = UnityEngine.Object.Instantiate(Resources.Load<Material>("Material/CardChoiceItem"));
		cardBack.GetComponent<MeshRenderer>().material.mainTexture = mainTexture;
		backgroundMat = cardBack.GetComponent<MeshRenderer>().material;
		backgroundMat.SetFloat("_Dissolve", 1f);
	}

	private void $Rougamo_Initialize(CardChoiceUI UI, string fromId)
	{
		cardChoiceUI = UI;
		dataConfig = new DataConfig(fromId, DataType.Card);
		GameSaveAnalyser.Instance.TryPushNative(dataConfig, OperObj.Cards, OperType.RewardShow);
		ICard.SetCardStyle(base.transform, dataConfig);
		ICard.SetCardMsg(base.transform, dataConfig);
		DataUpdate();
		base.gameObject.GetComponent<Button>().onClick.AddListener(delegate
		{
			if (canClick)
			{
				if (RoleTable.Instance.UnCardList.Count < RoleTable.Instance.MaxAlCardCount)
				{
					GameSaveAnalyser.Instance.TryPushNative(dataConfig, OperObj.Cards, OperType.Select);
					cardChoiceUI.Select(base.gameObject, dataConfig);
				}
				else
				{
					UIManager.Instance.ShowModalWindow("Tips", "你的卡牌数量已达上限", delegate
					{
						if (UIManager.Instance.GetUI<OutDeckUI>("OutDeckUI") != null)
						{
							UIManager.Instance.GetUI<OutDeckUI>("OutDeckUI").SetRole(new OutDeckUIData(RoleTable.Instance));
						}
						UIManager.Instance.ShowUI<OutDeckUI>("OutDeckUI");
					});
					base.gameObject.GetComponent<Button>().interactable = true;
				}
			}
		});
	}

	private void $Rougamo_FadeIn(float delay = 0f)
	{
		objectGroup.blocksRaycasts = false;
		base.gameObject.SetActive(value: true);
		base.transform.DOScale(Vector3.one * 0.8f, 0.1f).SetDelay(delay);
		AudioManager.Instance?.PlayEffect("Cards/draw");
		backgroundMat.DOFloat(0f, "_Dissolve", 0.8f).SetDelay(delay + 0.2f).OnComplete(delegate
		{
			canClick = true;
			Texts.GetComponent<ObjectGroup>().DOFade(1f, 1f);
			base.transform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().DOFade(1f, 1f);
			base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().DOFade(1f, 1f);
			base.transform.Find("Front/cost/cost").GetComponent<TMP_Text>().DOFade(1f, 1f);
			light.gameObject.SetActive(value: true);
			light.GetComponent<SpriteRenderer>().DOColor(rarityColors[dataConfig.data["Rarity"].ToInt() - 1], 0.2f);
			if (dataConfig.data["Rarity"] == "3")
			{
				light.Find("粒子").gameObject.SetActive(value: true);
			}
			Icons.gameObject.SetActive(value: true);
			objectGroup.blocksRaycasts = true;
		});
		base.transform.DORotate(Vector3.zero, 0.5f).SetDelay(delay + 0.8f).OnComplete(delegate
		{
			AudioManager.Instance?.PlayEffect("Cards/smash");
			base.transform.DOScale(Vector3.one * 0.75f, 0.1f);
		});
	}

	private void $Rougamo_DataUpdate()
	{
		if (!(this == null))
		{
			base.transform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().text = dataConfig.data.Localize("Name");
			dataConfig.scriptExecutor.RunScript("InitScript");
			List<string> list = new List<string>();
			base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().text = dataConfig.Description().Highlight(list);
			if (base.transform.GetComponent<KeywordDisplay>() == null)
			{
				base.transform.gameObject.AddComponent<KeywordDisplay>();
			}
			base.transform.GetComponent<KeywordDisplay>().keyWords = list;
		}
	}

	private void $Rougamo_FadeOut(float delay = 0f)
	{
		canClick = false;
		objectGroup.blocksRaycasts = false;
		light.GetComponent<SpriteRenderer>().DOFade(0f, 0f);
		Texts.GetComponent<ObjectGroup>().DOKill();
		base.transform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().DOKill();
		base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().DOKill();
		base.transform.Find("Front/cost/cost").GetComponent<TMP_Text>().DOKill();
		base.transform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().DOFade(0f, 0.1f);
		base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().DOFade(0f, 0.1f);
		base.transform.Find("Front/cost/cost").GetComponent<TMP_Text>().DOFade(0f, 0.1f);
		base.transform.DORotate(new Vector3(0f, 180f, 0f), 1f).SetDelay(delay + 0.2f);
		backgroundMat.DOFloat(1f, "_Dissolve", 1f).SetDelay(delay + 1f);
	}

	private void $Rougamo_MoveToDeck()
	{
		canClick = false;
		base.transform.DOLocalMove(Vector3.zero, 0.5f);
		GameObject target = UIManager.Instance.GetUI<TopBarUI>("TopBarUI").transform.Find("Content/Buttons/CardBack").gameObject;
		Vector3 vector = target.transform.position - base.transform.position;
		float z = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
		base.transform.DORotateQuaternion(Quaternion.Euler(new Vector3(0f, 0f, z)), 0.5f).SetDelay(0.5f);
		base.transform.DOScale(0f, 0.5f).SetDelay(0.5f);
		GameObject trail = UnityEngine.Object.Instantiate(ResourceLoader.Load("UI/Trail"), base.transform.parent) as GameObject;
		Transform vfx = trail.transform.Find("geometryBursts");
		foreach (Transform item in vfx.transform)
		{
			item.GetComponent<VisualEffect>().SetInt("count", 0);
		}
		base.transform.DOMove(target.GetComponent<RectTransform>().position, 1f).OnStart(delegate
		{
			foreach (Transform item2 in vfx.transform)
			{
				item2.GetComponent<VisualEffect>().SetInt("count", 1);
			}
		}).OnUpdate(delegate
		{
			Vector3 v = PositionUtility.CameraSpaceToZeroPlane(base.transform.GetComponent<RectTransform>());
			foreach (Transform item3 in vfx.transform)
			{
				VisualEffect component = item3.GetComponent<VisualEffect>();
				component.SetVector3("startPos", v);
				component.SetFloat("direction", base.transform.rotation.eulerAngles.z * (MathF.PI / 180f));
			}
		})
			.OnComplete(delegate
			{
				foreach (Transform item4 in vfx.transform)
				{
					item4.GetComponent<VisualEffect>().SetInt("count", 0);
				}
				UnityEngine.Object.Destroy(trail, 5f);
				if (target != null)
				{
					target.transform.DOKill();
					target.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 2).OnKill(delegate
					{
						target.transform.localScale = Vector3.one;
					});
				}
			})
			.SetDelay(0.5f);
	}
}
