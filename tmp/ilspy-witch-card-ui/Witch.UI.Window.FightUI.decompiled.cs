using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using Fight.ActionCommand;
using Michsky.MUIP;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.VFX;
using Witch.Core;
using ZLinq;

namespace Witch.UI.Window;

public class FightUI : UIBase
{
	private class DamageTextInfo
	{
		public string text;

		public Vector2 position;

		public string popUpType;

		public StatusManager status;

		public StatusManager to;

		public string realDamage;
	}

	public struct AnimationData
	{
		public StatusManager[] status;

		public IStatusManager.AnimatedState[] animationState;

		public string effectName;

		public AnimationData(ActionAnimation.AnimationData data)
		{
			status = (from x in data.status.AsValueEnumerable()
				select FightManager.Instance?.statuses.GetValueOrDefault(x, null)).ToArray();
			animationState = data.animationState;
			effectName = data.effectName;
		}
	}

	public float Card_y_position;

	public static DataConfig LastCard;

	public bool started;

	private int _cardTopCount;

	public GameObject chest;

	private System.Random random = new System.Random();

	private Transform process;

	public static List<CardItem> cardItemList = new List<CardItem>();

	public static List<CardItem> SelectedCard = new List<CardItem>();

	public static List<CardItem> WaitCard = new List<CardItem>();

	private CardItem _keyboardSelectedCard;

	private int _keyboardSelectedIndex = -1;

	private bool _fightInputRegistered;

	public int ShouldCard;

	public CardContainer cardContainer;

	public CardContainer selectCardContainer;

	public List<StatusManager> StatusList = new List<StatusManager>();

	private float lastDisplayTime;

	private int maxDisplayPerSecond = 8;

	private const int DamageMergeThreshold = 8;

	private const int MaxDamagePopupsOnScreen = 20;

	private const float DamagePopupLifetime = 2.5f;

	private List<float> activeDamagePopupTimes = new List<float>();

	private Queue<DamageTextInfo> damageTextQueue = new Queue<DamageTextInfo>();

	public Dictionary<StatusManager, (PopUpTextUI text, float time)> totalDamageText = new Dictionary<StatusManager, (PopUpTextUI, float)>();

	private List<KeyValuePair<StatusManager, (PopUpTextUI text, float time)>> _totalDamageSnapshot = new List<KeyValuePair<StatusManager, (PopUpTextUI, float)>>();

	private Sequence processTween;

	private Tween turnCameraTween;

	public bool autoCard;

	public Transform ConfirmButton;

	public Transform endfight;

	public Transform FightAgain;

	public Transform UsedCardList;

	public ButtonManager turnButton;

	public static bool IsReset = false;

	private List<SkillItem> skillItems = new List<SkillItem>();

	private bool quickReset;

	public Queue<DataConfig> createCardQueue = new Queue<DataConfig>();

	public bool NeedUpdateCardMsg;

	private TMP_Text Title;

	public static int SpecialCount;

	private bool selectConfirmed;

	public GameObject instance;

	private GameObject prefabA;

	public static bool InIEn;

	public static string SelectType;

	public static bool CanBeforeEnd;

	private bool isWin;

	private bool ShowReward = true;

	public Queue<AnimationData> animationQueue = new Queue<AnimationData>();

	private float waitingTime;

	public bool blurReturn;

	public bool NowAnimation;

	private Dictionary<StatusManager, int> activeTweens = new Dictionary<StatusManager, int>();

	private readonly Dictionary<StatusManager, int> activeActionAnimationCounts = new Dictionary<StatusManager, int>();

	public Dictionary<StatusManager, int> ItemSum = new Dictionary<StatusManager, int>();

	public int CardTopCount
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(FightUI);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				int result = $Rougamo_get_CardTopCount();
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
			methodContext.TargetType = typeof(FightUI);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_CardTopCount(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	[DebuggerStepThrough]
	public override void OnDestroy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
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
	public override void Close()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Close();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void EnqueueDamageText(string text, Vector3 position, string popUpType1, StatusManager status, StatusManager to, string realDamage)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[6] { text, position, popUpType1, status, to, realDamage };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_EnqueueDamageText(text, position, popUpType1, status, to, realDamage);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public static int NormalizeDisplayDamage(string damageText)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { damageText };
		try
		{
			modifiable.OnEntry(methodContext);
			int result = $Rougamo_NormalizeDisplayDamage(damageText);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public static int AddDisplayDamage(int current, string addedDamage)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { current, addedDamage };
		try
		{
			modifiable.OnEntry(methodContext);
			int result = $Rougamo_AddDisplayDamage(current, addedDamage);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public static int GetNextDisplayDamageValue(int current, int target, float animationTime, float deltaTime)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[4] { current, target, animationTime, deltaTime };
		try
		{
			modifiable.OnEntry(methodContext);
			int result = $Rougamo_GetNextDisplayDamageValue(current, target, animationTime, deltaTime);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void SetClockName(string name)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { name };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SetClockName(name);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void DoTurnAnimation(float movement, float duration = 0.5f)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { movement, duration };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DoTurnAnimation(movement, duration);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void ResetTurnCamera()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ResetTurnCamera();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void SetTurn(FightObject obj, int index, int count)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[3] { obj, index, count };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SetTurn(obj, index, count);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void ShowChest()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ShowChest();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void Update()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Update();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void AutoUseCard()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_AutoUseCard();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void UpdateCardKeyboardShortcut()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdateCardKeyboardShortcut();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void ShowDamageText()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ShowDamageText();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void Awake()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
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

	private async UniTaskVoid ProcessCreateCardQueueAsync()
	{
		await UniTask.SwitchToMainThread();
		await UniTask.WaitUntil(() => started, PlayerLoopTiming.Update, base.destroyCancellationToken);
		while (!this.IsNull() && !base.destroyCancellationToken.IsCancellationRequested)
		{
			if (createCardQueue.Count > 0)
			{
				DataConfig dataConfig = createCardQueue.Dequeue();
				CreateCardItemInternal(dataConfig);
			}
			await UniTask.WaitForSeconds(0.15f + 0.4f / (float)(createCardQueue.Count + 1), ignoreTimeScale: false, PlayerLoopTiming.Update, base.destroyCancellationToken);
		}
	}

	[DebuggerStepThrough]
	private void Start()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Start();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void ShowTitle()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ShowTitle();
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
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
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
	private void RegisterFightInput()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RegisterFightInput();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void UnregisterFightInput()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UnregisterFightInput();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void OnEndTurnInput(InputAction.CallbackContext context)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { context };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnEndTurnInput(context);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void OnEndSelect(InputAction.CallbackContext context)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { context };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnEndSelect(context);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void OnRestartFightInput(InputAction.CallbackContext context)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { context };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnRestartFightInput(context);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void TryEndTurnFromInput()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_TryEndTurnFromInput();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void TryRestartFightFromInput()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_TryRestartFightFromInput();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void InitSkill()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_InitSkill();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CreateSkillItem(Transform tempItem, int index)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { tempItem, index };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CreateSkillItem(tempItem, index);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UpdateSkill()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdateSkill();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CreateDeckMenu()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CreateDeckMenu();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CreateUsedCardList()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CreateUsedCardList();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UpdatePower()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdatePower();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void FightAgainCheck()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_FightAgainCheck();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void FightAgainBtn()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_FightAgainBtn();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void ResetButtonCheck()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ResetButtonCheck();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void Reset()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Reset();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void onChangeTurnBtn()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_onChangeTurnBtn();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private IEnumerator TurnBtn()
	{
		CardItem.canUse = false;
		yield return new WaitForSeconds(0.5f);
		while (InIEn || createCardQueue.Count > 0)
		{
			yield return null;
		}
		if (FightPlayer.Instance.Status.state != IStatusManager.State.Dead)
		{
			IStatusManager status = FightPlayer.Instance.Status;
			Singleton<EventCenter>.Instance.EventTrigger("EndRound" + status.InstanceId);
			RemoveAllCards();
			status.ChangeState(IStatusManager.State.Default);
			foreach (CardItem cardItem in cardItemList)
			{
				cardItem.Vars["OnceExCost"] = "0";
			}
			status.CheckAllBuff("ReducePerTurn");
		}
		else
		{
			FightManager.Instance.CmdAnnounceDone(FightPlayer.Instance.InstanceId, FightPlayer.Instance.Status.state == IStatusManager.State.Dead);
		}
	}

	[DebuggerStepThrough]
	public void CreateCardItem(int Count)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { Count };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CreateCardItem(Count);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CreateCardItem(DataConfig dataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { dataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CreateCardItem(dataConfig);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void CreateCardItemInternal(DataConfig dataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { dataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CreateCardItemInternal(dataConfig);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UpdateCardItemPos(TweenCallback OnComplete = null, CardContainer from = null)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { OnComplete, from };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdateCardItemPos(OnComplete, from);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void ShuffleCardItems()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ShuffleCardItems();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UpdateCardMsg()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdateCardMsg();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void UpdateCardsShow()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_UpdateCardsShow();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void RemoveAllCards()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RemoveAllCards();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private IEnumerator RemoveAllCardsAfterBurn(int burnCount)
	{
		if (burnCount > 0)
		{
			yield return StartCoroutine(Burn(burnCount.ToString(), "2"));
		}
		ShouldCard = (int)FightPlayer.Instance.Status.dynamicVariables.GetValueOrDefault("RetainCard", 0f);
		if (ShouldCard != 0)
		{
			yield return StartCoroutine(Wait());
			yield break;
		}
		CardItem.canUse = false;
		for (int i = 0; i < cardItemList.Count; i++)
		{
			if (!cardItemList[i].Tags.Contains("Retain"))
			{
				cardItemList[i].ignore = true;
			}
		}
		for (int num = cardItemList.Count - 1; num >= 0; num--)
		{
			bool num2 = !cardItemList[num].Tags.Contains("Retain");
			CardItem cardItem = cardItemList[num];
			if (num2)
			{
				new Stopwatch().Start();
				cardItem.InternalThrow(needUp: false);
			}
		}
		FightManager.Instance.CmdAnnounceDone(FightPlayer.Instance.InstanceId, FightPlayer.Instance.Status.state == IStatusManager.State.Dead);
	}

	private IEnumerator Wait()
	{
		if (isWin)
		{
			yield break;
		}
		while (true)
		{
			if (InIEn || createCardQueue.Count > 0)
			{
				if (!isWin)
				{
					yield return null;
					continue;
				}
				break;
			}
			lock (this)
			{
				SelectInit("2");
				if (ShouldCard > cardItemList.Count)
				{
					ShouldCard = cardItemList.Count;
				}
				SelectType = "Retain";
				SpecialCount += ShouldCard;
				Title = instance.transform.Find("Title").GetComponent<TMP_Text>();
				Title.text = "choose".Localize("FightUI") + ShouldCard + "cards".Localize("FightUI") + "retain".Localize("FightUI");
				while (!selectConfirmed || SelectedCard.Any((CardItem x) => !x.enabled))
				{
					RefreshSelectConfirmButton();
					if (cardItemList.Count == 0)
					{
						break;
					}
					yield return null;
				}
				List<CardItem> list = new List<CardItem>(cardItemList);
				for (int num = list.Count - 1; num >= 0; num--)
				{
					if (!SelectedCard.Contains(list[num]))
					{
						if (!list[num].Tags.Contains("Retain"))
						{
							list[num].InternalThrow(needUp: false);
						}
					}
					else
					{
						SelectedCard.Remove(list[num]);
						list[num].transform.SetParent(cardContainer.transform);
					}
				}
				UpdateCardItemPos();
				SelectedCard.Clear();
				ResetSelectState();
				base.transform.Find("ClockBoard/结束回合").gameObject.SetActive(value: true);
				CardItem.canUse = true;
				UnityEngine.Object.Destroy(instance);
				ConfirmButton.gameObject.SetActive(value: false);
				InIEn = false;
				UpdateCardsShow();
				FightManager.Instance.CmdAnnounceDone(FightPlayer.Instance.InstanceId, FightPlayer.Instance.Status.state == IStatusManager.State.Dead);
			}
			break;
		}
	}

	[DebuggerStepThrough]
	public void ThrowCardScript(string val, string Type)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { val, Type };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ThrowCardScript(val, Type);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void Burning(string val, string Type)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { val, Type };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Burning(val, Type);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void SelectCardToAction(string val, Action<List<CardItem>> onCardSelected, string Type = "1")
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[3] { val, onCardSelected, Type };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SelectCardToAction(val, onCardSelected, Type);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private bool PreparePendingSelectRequest()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			bool result = $Rougamo_PreparePendingSelectRequest();
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private IEnumerator CardToAction(string val, Action<List<CardItem>> onCardSelected, string Type)
	{
		if (isWin)
		{
			yield break;
		}
		while (true)
		{
			if (InIEn || createCardQueue.Count > 0)
			{
				if (!isWin)
				{
					yield return null;
					continue;
				}
				break;
			}
			lock (this)
			{
				if (onCardSelected == null)
				{
					break;
				}
				SelectInit(Type);
				SelectType = "CardToAction";
				SpecialCount += int.Parse(val);
				if (SpecialCount > cardItemList.Count)
				{
					SpecialCount = cardItemList.Count;
				}
				Title = instance.transform.Find("Title").GetComponent<TMP_Text>();
				Title.text = "choose".Localize("FightUI") + SpecialCount + "cards".Localize("FightUI");
				while (!selectConfirmed || SelectedCard.Any((CardItem x) => !x.enabled))
				{
					RefreshSelectConfirmButton();
					if (cardItemList.Count == 0)
					{
						break;
					}
					yield return null;
				}
				List<CardItem> list = new List<CardItem>(cardItemList);
				for (int num = list.Count - 1; num >= 0; num--)
				{
					list[num].transform.SetParent(cardContainer.transform);
				}
				onCardSelected(SelectedCard);
				SelectedCard.Clear();
				ResetSelectState();
				UpdateCardItemPos();
				if (Application.isEditor && !Application.isPlaying)
				{
					UnityEngine.Object.DestroyImmediate(instance);
				}
				else
				{
					UnityEngine.Object.Destroy(instance);
					UpdateCardMsg();
				}
				base.transform.Find("ClockBoard/结束回合").gameObject.SetActive(value: true);
				ConfirmButton.gameObject.SetActive(value: false);
				CardItem.canUse = true;
				InIEn = false;
				break;
			}
		}
	}

	private IEnumerator Burn(string val, string Type)
	{
		if (isWin)
		{
			yield break;
		}
		while (true)
		{
			if (InIEn || createCardQueue.Count > 0)
			{
				if (!isWin)
				{
					yield return null;
					continue;
				}
				break;
			}
			lock (this)
			{
				SelectInit(Type);
				SelectType = "Burn";
				SpecialCount += int.Parse(val);
				if (SpecialCount >= cardItemList.Count && !CanBeforeEnd)
				{
					foreach (CardItem item in new List<CardItem>(cardItemList))
					{
						if (!item.Tags.Contains("Froze"))
						{
							item.InternalBurning();
						}
					}
					SpecialCount = 0;
				}
				else
				{
					SpecialCount = Math.Min(BurnAndThrowCheck(), SpecialCount);
					Title = instance.transform.Find("Title").GetComponent<TMP_Text>();
					Title.text = "choose".Localize("FightUI") + SpecialCount + "cards".Localize("FightUI") + "burnout".Localize("FightUI");
					while (!selectConfirmed || SelectedCard.Any((CardItem x) => !x.enabled))
					{
						RefreshSelectConfirmButton();
						if (cardItemList.Count == 0 || AllCannotUse())
						{
							break;
						}
						yield return null;
					}
					List<CardItem> list = new List<CardItem>(cardItemList);
					for (int num = list.Count - 1; num >= 0; num--)
					{
						if (SelectedCard.Contains(list[num]))
						{
							BurnCard(list[num]);
						}
						else
						{
							list[num].transform.SetParent(cardContainer.transform);
						}
					}
				}
				SelectedCard.Clear();
				ResetSelectState();
				UpdateCardItemPos();
				UnityEngine.Object.Destroy(instance);
				base.transform.Find("ClockBoard/结束回合").gameObject.SetActive(value: true);
				ConfirmButton.gameObject.SetActive(value: false);
				CardItem.canUse = true;
				InIEn = false;
			}
			break;
		}
	}

	[DebuggerStepThrough]
	public int BurnAndThrowCheck()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			int result = $Rougamo_BurnAndThrowCheck();
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void BurnCard(CardItem cardItem)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { cardItem };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_BurnCard(cardItem);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void SelectInit(string uitype)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { uitype };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SelectInit(uitype);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void RefreshSelectConfirmButton()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RefreshSelectConfirmButton();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void ResetSelectState()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ResetSelectState();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void ShowBattleReward()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ShowBattleReward();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void CanWin()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CanWin();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void EndInstance()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_EndInstance();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void Yes()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Yes();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public bool AllCannotUse()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			bool result = $Rougamo_AllCannotUse();
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private IEnumerator Throw(string val, string Type)
	{
		if (isWin)
		{
			yield break;
		}
		while (true)
		{
			if (InIEn || createCardQueue.Count > 0)
			{
				if (!isWin)
				{
					yield return null;
					continue;
				}
				break;
			}
			lock (this)
			{
				SelectInit(Type);
				SelectType = "Throw";
				SpecialCount += int.Parse(val);
				if (SpecialCount >= cardItemList.Count && !CanBeforeEnd)
				{
					foreach (CardItem item in new List<CardItem>(cardItemList))
					{
						item.ThrowCard();
					}
					SpecialCount = 0;
				}
				else
				{
					SpecialCount = Math.Min(BurnAndThrowCheck(), SpecialCount);
					Title = instance.transform.Find("Title").GetComponent<TMP_Text>();
					Title.text = "choose".Localize("FightUI") + SpecialCount + "cards".Localize("FightUI") + "throw".Localize("FightUI");
					while (!selectConfirmed || SelectedCard.Any((CardItem x) => !x.enabled))
					{
						RefreshSelectConfirmButton();
						if (cardItemList.Count == 0 || AllCannotUse())
						{
							break;
						}
						yield return null;
					}
					for (int num = cardItemList.Count - 1; num >= 0; num--)
					{
						if (SelectedCard.Contains(cardItemList[num]))
						{
							cardItemList[num].ThrowCard();
						}
						else
						{
							SelectedCard.Remove(cardItemList[num]);
							cardItemList[num].transform.SetParent(cardContainer.transform);
						}
					}
				}
				SelectedCard.Clear();
				ResetSelectState();
				CardItem.canUse = true;
				ConfirmButton.gameObject.SetActive(value: false);
				base.transform.Find("ClockBoard/结束回合").gameObject.SetActive(value: true);
				UnityEngine.Object.Destroy(instance);
				UpdateCardItemPos();
				InIEn = false;
				UpdateCardsShow();
			}
			break;
		}
	}

	[DebuggerStepThrough]
	public void CallActionAnimation(IScriptExecutor scriptExecutor)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { scriptExecutor };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CallActionAnimation(scriptExecutor);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void BeginStatusActionAnimation(StatusManager status)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { status };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_BeginStatusActionAnimation(status);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private bool FinishStatusActionAnimation(StatusManager status)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { status };
		try
		{
			modifiable.OnEntry(methodContext);
			bool result = $Rougamo_FinishStatusActionAnimation(status);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static bool IsHitReactionAnimation(IStatusManager.AnimatedState state)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { state };
		try
		{
			modifiable.OnEntry(methodContext);
			bool result = $Rougamo_IsHitReactionAnimation(state);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static IStatusManager.AnimatedState ResolvePlayableAnimationState(StatusManager status, IStatusManager.AnimatedState state)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[2] { status, state };
		try
		{
			modifiable.OnEntry(methodContext);
			IStatusManager.AnimatedState result = $Rougamo_ResolvePlayableAnimationState(status, state);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static void ResetStatusToIdleAnimation(StatusManager status)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { status };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ResetStatusToIdleAnimation(status);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static void RestoreStatusKeywordDisplay(StatusManager status)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { status };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RestoreStatusKeywordDisplay(status);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static void RestoreStatusSortingLayer(StatusManager status)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { status };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RestoreStatusSortingLayer(status);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void DOActionAnimation()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DOActionAnimation();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static Dictionary<Transform, Vector3> CaptureSummonBodyScales(StatusManager status)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[1] { status };
		try
		{
			modifiable.OnEntry(methodContext);
			Dictionary<Transform, Vector3> result = $Rougamo_CaptureSummonBodyScales(status);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static void SyncSummonBodyScales(Dictionary<Transform, Vector3> summonScales, Vector3 ownerOriginalScale, Vector3 ownerCurrentScale)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[3] { summonScales, ownerOriginalScale, ownerCurrentScale };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SyncSummonBodyScales(summonScales, ownerOriginalScale, ownerCurrentScale);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static float ApplyScaleRatio(float originalValue, float ownerOriginalValue, float ownerCurrentValue)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[3] { originalValue, ownerOriginalValue, ownerCurrentValue };
		try
		{
			modifiable.OnEntry(methodContext);
			float result = $Rougamo_ApplyScaleRatio(originalValue, ownerOriginalValue, ownerCurrentValue);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	private async UniTaskVoid RestoreBlurAsync(float speedMultiplier)
	{
		blurReturn = true;
		while (waitingTime < 1f / speedMultiplier)
		{
			waitingTime += Time.deltaTime;
			await UniTask.Yield();
		}
		if (!(GameApp.Instance == null) && !(GameApp.Instance.NowBackground == null) && !(this == null))
		{
			Resources.Load<Material>("Material/PostProcess/Blur")?.DisableKeyword("_BLUR_ON");
			waitingTime = 0f;
			blurReturn = false;
		}
	}

	[DebuggerStepThrough]
	public void DoCardUseAnimation(UseCard.CardUseData cardUseData, bool toThrow = true, bool needInit = false)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(FightUI);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(FightUI).TypeHandle);
		methodContext.Arguments = new object[3] { cardUseData, toThrow, needInit };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DoCardUseAnimation(cardUseData, toThrow, needInit);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[SpecialName]
	private int $Rougamo_get_CardTopCount()
	{
		return _cardTopCount;
	}

	[SpecialName]
	private void $Rougamo_set_CardTopCount(int value)
	{
		if (value > 20)
		{
			_cardTopCount = 20;
		}
		else if (value < 5)
		{
			_cardTopCount = 5;
		}
		else
		{
			_cardTopCount = value;
		}
	}

	private void $Rougamo_OnDestroy()
	{
		UnregisterFightInput();
		InIEn = false;
		ResetSelectState();
		Singleton<EventCenter>.Instance.Clear(this);
		if (cardItemList != null)
		{
			foreach (CardItem item in new List<CardItem>(cardItemList))
			{
				if (!(item == null) && !(item.gameObject == null))
				{
					UnityEngine.Object.Destroy(item.gameObject);
				}
			}
		}
		if (WaitCard != null)
		{
			foreach (CardItem item2 in new List<CardItem>(WaitCard))
			{
				if (!(item2 == null) && !(item2.gameObject == null))
				{
					UnityEngine.Object.Destroy(item2.gameObject);
				}
			}
		}
		cardItemList?.Clear();
		WaitCard?.Clear();
		skillItems?.Clear();
		SelectedCard?.Clear();
		foreach (KeyValuePair<StatusManager, (PopUpTextUI, float)> item3 in totalDamageText)
		{
			if (!(item3.Value.Item1 == null) && !(item3.Value.Item1.gameObject == null))
			{
				UnityEngine.Object.Destroy(item3.Value.Item1.gameObject);
			}
		}
		foreach (StatusManager status in StatusList)
		{
			if (!(status == null) && status.gameObject != null)
			{
				UnityEngine.Object.Destroy(status.gameObject);
			}
		}
		totalDamageText.Clear();
		activeDamagePopupTimes.Clear();
		UIManager.Instance.CloseUI("DeckUI");
		if (chest != null)
		{
			UnityEngine.Object.Destroy(chest);
		}
		UIManager.Instance.HideUI("LineUI");
		Resources.Load<Material>("Material/PostProcess/Blur")?.DisableKeyword("_BLUR_ON");
		ResetTurnCamera();
	}

	private void $Rougamo_Close()
	{
		if (UIManager.Instance != null && this != null && base.gameObject != null)
		{
			UIManager.Instance.RemoveUI(base.gameObject.name);
		}
		UnityEngine.Object.Destroy(base.gameObject);
	}

	private void $Rougamo_EnqueueDamageText(string text, Vector3 position, string popUpType1, StatusManager status, StatusManager to, string realDamage)
	{
		DamageTextInfo item = new DamageTextInfo
		{
			text = text,
			position = position + new Vector3(random.Next(-200, 100), random.Next(-100, 150), 0f),
			popUpType = popUpType1,
			status = status,
			realDamage = realDamage,
			to = to
		};
		damageTextQueue.Enqueue(item);
	}

	private static int $Rougamo_NormalizeDisplayDamage(string damageText)
	{
		return Math.Max(0, damageText.ToInt());
	}

	private static int $Rougamo_AddDisplayDamage(int current, string addedDamage)
	{
		long num = Math.Max(0L, current) + NormalizeDisplayDamage(addedDamage);
		if (num <= int.MaxValue)
		{
			return (int)num;
		}
		return int.MaxValue;
	}

	private static int $Rougamo_GetNextDisplayDamageValue(int current, int target, float animationTime, float deltaTime)
	{
		if (current == target)
		{
			return current;
		}
		double num = Math.Max(0.0001f, animationTime);
		long num2 = (long)target - (long)current;
		long num3 = Math.Max(1L, (long)Math.Floor((double)Math.Abs(num2) / num * (double)deltaTime));
		long val = current + Math.Sign(num2) * num3;
		if (num2 <= 0)
		{
			return (int)Math.Max(val, target);
		}
		return (int)Math.Min(val, target);
	}

	private void $Rougamo_SetClockName(string name)
	{
		if (processTween != null && processTween.IsActive() && processTween.IsPlaying())
		{
			processTween.Kill();
		}
		processTween = DOTween.Sequence();
		process.GetComponent<CanvasGroup>().alpha = 0f;
		processTween.Append(process.GetComponent<CanvasGroup>().DOFade(1f, 0.5f));
		processTween.Append(process.Find("Tip").DORotate(new Vector3(360f, 0f, 0f), 1f, RotateMode.LocalAxisAdd).OnStart(delegate
		{
			process.Find("Tip/Text").GetComponent<TMP_Text>().DOFade(0f, 0.5f)
				.OnComplete(delegate
				{
					process.Find("Tip/Text").GetComponent<TMP_Text>().text = "";
					process.Find("Tip/Text").GetComponent<TMP_Text>().DOFade(1f, 0.5f);
					process.Find("Tip/Text").GetComponent<TMP_Text>().text = "<color=red>" + name + "</color> " + "的回合".Localize("FightUI");
				});
		})
			.OnComplete(delegate
			{
				process.Find("Tip").eulerAngles = new Vector3(0f, 0f, 0f);
			}));
		processTween.Append(process.GetComponent<CanvasGroup>().DOFade(0f, 0.5f).SetDelay(2f));
	}

	private void $Rougamo_DoTurnAnimation(float movement, float duration = 0.5f)
	{
		Transform transform = Camera.main?.transform;
		if (!(transform == null))
		{
			turnCameraTween?.Kill();
			turnCameraTween = transform.DOMoveX(movement, duration).SetEase(Ease.OutSine);
		}
	}

	private void $Rougamo_ResetTurnCamera()
	{
		turnCameraTween?.Kill();
		turnCameraTween = null;
		Transform transform = Camera.main?.transform;
		if (!(transform == null))
		{
			transform.position = new Vector3(0f, 0f, -5f);
		}
	}

	private void $Rougamo_SetTurn(FightObject obj, int index, int count)
	{
		SetClockName(StatusBarUI.GetDisplayName(obj?.Status as StatusManager));
		if (count <= 1 || index < 0)
		{
			DoTurnAnimation(0f);
			return;
		}
		index = Mathf.Clamp(index, 0, count - 1);
		float movement = Mathf.Lerp(-1f, 1f, (float)index / (float)(count - 1));
		DoTurnAnimation(movement, 1.5f / (float)count);
	}

	private void $Rougamo_ShowChest()
	{
		chest.transform.SetAsLastSibling();
		chest.SetActive(value: true);
		EndInstance();
		TweenerCore<float, float, FloatOptions> tweenerCore = base.transform.Find("Left").GetComponent<CanvasGroup>().DOFade(0f, 0.5f);
		tweenerCore.onComplete = (TweenCallback)Delegate.Combine(tweenerCore.onComplete, (TweenCallback)delegate
		{
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").transform.Find("Left").gameObject.SetActive(value: false);
			}
		});
		UniTask.WaitForSeconds(10f).ContinueWith(delegate
		{
			if (this != null && base.gameObject != null)
			{
				ShowBattleReward();
			}
		}).Forget();
	}

	private void $Rougamo_Update()
	{
		UpdateCardKeyboardShortcut();
		ShowDamageText();
		AutoUseCard();
		if (NeedUpdateCardMsg)
		{
			NeedUpdateCardMsg = false;
			UpdateCardMsg();
		}
	}

	private void $Rougamo_AutoUseCard()
	{
		if (!autoCard)
		{
			return;
		}
		foreach (CardItem cardItem in cardItemList)
		{
			if (cardItem != null && !cardItem.hasUse && cardItem is CommonCardItem commonCardItem)
			{
				commonCardItem.UseCardDirectly();
				break;
			}
		}
		if (cardItemList.Count() == 0)
		{
			CreateCardItem(10);
		}
	}

	private void $Rougamo_UpdateCardKeyboardShortcut()
	{
		if (FightManager.Instance == null || FightManager.Instance.fightType != FightType.Player || cardItemList == null || (UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI") != null && UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI").gameObject.activeSelf))
		{
			return;
		}
		Keyboard current = Keyboard.current;
		if (current == null)
		{
			return;
		}
		bool flag = false;
		for (int i = 0; i < cardItemList.Count; i++)
		{
			CardItem cardItem = cardItemList[i];
			if (!(cardItem == null) && !cardItem.hasUse)
			{
				if (cardItem.draging)
				{
					flag = true;
					break;
				}
				if (cardItem is AttackCardItem { isLine: not false })
				{
					flag = true;
					break;
				}
			}
		}
		if (_keyboardSelectedCard != null && _keyboardSelectedCard.hasUse)
		{
			_keyboardSelectedCard = null;
			_keyboardSelectedIndex = -1;
		}
		if (!flag && _keyboardSelectedCard == null)
		{
			int num = -1;
			if (current.digit1Key.wasPressedThisFrame)
			{
				num = 0;
			}
			else if (current.digit2Key.wasPressedThisFrame)
			{
				num = 1;
			}
			else if (current.digit3Key.wasPressedThisFrame)
			{
				num = 2;
			}
			else if (current.digit4Key.wasPressedThisFrame)
			{
				num = 3;
			}
			else if (current.digit5Key.wasPressedThisFrame)
			{
				num = 4;
			}
			else if (current.digit6Key.wasPressedThisFrame)
			{
				num = 5;
			}
			else if (current.digit7Key.wasPressedThisFrame)
			{
				num = 6;
			}
			else if (current.digit8Key.wasPressedThisFrame)
			{
				num = 7;
			}
			else if (current.digit9Key.wasPressedThisFrame)
			{
				num = 8;
			}
			else if (current.digit0Key.wasPressedThisFrame)
			{
				num = 9;
			}
			if (num >= 0 && num < cardItemList.Count)
			{
				CardItem cardItem2 = cardItemList[num];
				if (!InIEn && CardItem.canUse)
				{
					if (cardItem2 != null && !cardItem2.hasUse)
					{
						if (cardItem2 is AttackCardItem attackCardItem2)
						{
							if (attackCardItem2.BeginLineMode(requireClickable: false))
							{
								_keyboardSelectedCard = attackCardItem2;
								_keyboardSelectedIndex = num;
							}
						}
						else if (cardItem2 is CommonCardItem commonCardItem)
						{
							commonCardItem.UseCardDirectly();
							UpdateCardItemPos();
						}
					}
				}
				else if (InIEn)
				{
					PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
					pointerEventData.button = PointerEventData.InputButton.Left;
					cardItem2.OnRightClick(pointerEventData);
				}
			}
		}
		if (_keyboardSelectedCard != null && ((current.digit1Key.wasReleasedThisFrame && _keyboardSelectedIndex == 0) || (current.digit2Key.wasReleasedThisFrame && _keyboardSelectedIndex == 1) || (current.digit3Key.wasReleasedThisFrame && _keyboardSelectedIndex == 2) || (current.digit4Key.wasReleasedThisFrame && _keyboardSelectedIndex == 3) || (current.digit5Key.wasReleasedThisFrame && _keyboardSelectedIndex == 4) || (current.digit6Key.wasReleasedThisFrame && _keyboardSelectedIndex == 5) || (current.digit7Key.wasReleasedThisFrame && _keyboardSelectedIndex == 6) || (current.digit8Key.wasReleasedThisFrame && _keyboardSelectedIndex == 7) || (current.digit9Key.wasReleasedThisFrame && _keyboardSelectedIndex == 8) || (current.digit0Key.wasReleasedThisFrame && _keyboardSelectedIndex == 9)))
		{
			if (_keyboardSelectedCard is AttackCardItem attackCardItem3)
			{
				attackCardItem3.CommitOrCancelFromKeyboard();
			}
			_keyboardSelectedCard = null;
			_keyboardSelectedIndex = -1;
		}
	}

	private async void $Rougamo_ShowDamageText()
	{
		if (Singleton<GameRuntimeData>.Instance.settingTable.GetValue("显示伤害数字") == "关闭")
		{
			return;
		}
		if (damageTextQueue.Count > 0 && Time.time - lastDisplayTime >= 1f / (float)maxDisplayPerSecond)
		{
			if (damageTextQueue.Count > 20)
			{
				maxDisplayPerSecond = 10;
				if (damageTextQueue.Count > 40)
				{
					maxDisplayPerSecond = 15;
				}
			}
			bool flag = damageTextQueue.Count >= 8;
			List<DamageTextInfo> list = new List<DamageTextInfo>();
			DamageTextInfo first = damageTextQueue.Dequeue();
			if (first.status == null || IsNullExtension.IsNull(first.status))
			{
				lastDisplayTime = Time.time;
			}
			else
			{
				list.Add(first);
				if (flag)
				{
					List<DamageTextInfo> list2 = new List<DamageTextInfo>();
					while (damageTextQueue.Count > 0)
					{
						DamageTextInfo damageTextInfo = damageTextQueue.Dequeue();
						if (damageTextInfo.to == first.to && damageTextInfo.status == first.status && !IsNullExtension.IsNull(damageTextInfo.status))
						{
							list.Add(damageTextInfo);
						}
						else
						{
							list2.Add(damageTextInfo);
						}
					}
					foreach (DamageTextInfo item in list2)
					{
						damageTextQueue.Enqueue(item);
					}
				}
				int sumReal = 0;
				Vector2 sumPos = first.position;
				for (int i = 0; i < list.Count; i++)
				{
					sumReal = AddDisplayDamage(sumReal, list[i].realDamage);
					if (i > 0)
					{
						sumPos += list[i].position;
					}
				}
				if (list.Count > 1)
				{
					sumPos /= (float)list.Count;
				}
				string sumRealStr = sumReal.ToString();
				string sumText = sumRealStr;
				if (first.to == null)
				{
					lastDisplayTime = Time.time;
				}
				else
				{
					if (!totalDamageText.ContainsKey(first.to))
					{
						PopUpTextUI text = await UIManager.Instance.ShowPopUpText("DamageTotalText", sumText, first.to.selfUI.transform.localPosition + new Vector3(0f, 100f, 0f));
						totalDamageText.TryAdd(first.to, (text, Time.time));
						text.target = "0";
						text.SetDisplayInt(0);
						UniTask.WaitForSeconds(0.3f).ContinueWith(delegate
						{
							text.pause = true;
						}).Forget();
					}
					int num = AddDisplayDamage(NormalizeDisplayDamage(totalDamageText[first.to].text.target), sumReal.ToString());
					totalDamageText[first.to].text.target = num.ToString();
					totalDamageText[first.to] = (totalDamageText[first.to].text, Time.time);
					for (int num2 = activeDamagePopupTimes.Count - 1; num2 >= 0; num2--)
					{
						if (Time.time - activeDamagePopupTimes[num2] > 2.5f)
						{
							activeDamagePopupTimes.RemoveAt(num2);
						}
					}
					if (activeDamagePopupTimes.Count < 20)
					{
						activeDamagePopupTimes.Add(Time.time);
						UIManager.Instance.ShowPopUpDamage(first.popUpType + "DamageText", sumText, first.status, sumPos, sumRealStr).Forget();
					}
					lastDisplayTime = Time.time;
				}
			}
		}
		_totalDamageSnapshot.Clear();
		foreach (KeyValuePair<StatusManager, (PopUpTextUI, float)> item2 in totalDamageText)
		{
			_totalDamageSnapshot.Add(item2);
		}
		for (int num3 = 0; num3 < _totalDamageSnapshot.Count; num3++)
		{
			KeyValuePair<StatusManager, (PopUpTextUI, float)> keyValuePair = _totalDamageSnapshot[num3];
			(PopUpTextUI, float) value = keyValuePair.Value;
			if (keyValuePair.Key == null)
			{
				keyValuePair.Value.Item1.pause = false;
				if (!(keyValuePair.Value.Item1 == null) && !(keyValuePair.Value.Item1.gameObject == null))
				{
					totalDamageText.Remove(keyValuePair.Key);
				}
			}
			else if (keyValuePair.Value.Item1.pause && keyValuePair.Value.Item1.time >= 0.25f && Time.time - keyValuePair.Value.Item2 < 1f)
			{
				int num4 = NormalizeDisplayDamage(keyValuePair.Value.Item1.target);
				int displayInt = value.Item1.GetDisplayInt();
				if (displayInt != num4)
				{
					int nextDisplayDamageValue = GetNextDisplayDamageValue(displayInt, num4, 1f, Time.deltaTime);
					value.Item1.SetDisplayInt(nextDisplayDamageValue);
					value.Item1.maxFontSize = 60 + ((nextDisplayDamageValue >= 100) ? 1 : ((int)((float)nextDisplayDamageValue / 10f)));
				}
			}
			else if (Time.time - keyValuePair.Value.Item2 >= 1f)
			{
				keyValuePair.Value.Item1.pause = false;
				totalDamageText.Remove(keyValuePair.Key);
			}
		}
	}

	private void $Rougamo_Awake()
	{
		GameApp.Instance.NowBackground.gameObject.SetActive(value: true);
		chest = UnityEngine.Object.Instantiate(ResourceLoader.Load<GameObject>("Model/Chest"));
		Sprite sprite = chest.transform.Find("body/Close").GetComponent<SpriteRenderer>().sprite;
		chest.transform.position = new Vector3(4.8f, GameApp.Instance.NowBackground.transform.Find("com").GetComponent<SceneInfo>().ground_y + sprite.bounds.size.y / 2f, 0f);
		chest.SetActive(value: false);
		chest.GetComponent<Button>().onClick.RemoveAllListeners();
		chest.GetComponent<Button>().onClick.AddListener(ShowBattleReward);
		ProcessCreateCardQueueAsync().Forget();
	}

	private void $Rougamo_Start()
	{
		if (UIManager.Instance.GetUI<CurtainTurnUI>("CurtainTurnUI") != null)
		{
			Singleton<EventCenter>.Instance.AddEventListener("UIClose-CurtainTurnUI", delegate
			{
				ShowTitle();
			}, this);
		}
		else
		{
			ShowTitle();
		}
	}

	private void $Rougamo_ShowTitle()
	{
		UIManager.Instance.ShowUI<TitleUI>("TitleUI").ShowTitle(GameApp.Instance.NowBackground.name.Localize("MapSelectUI"), "Encounter Enemy".Localize("FightUI"), EnemyManager.SettlementMultiplier.ToString());
		AudioManager.Instance?.PlayEffect("Effect/行动单位发生变化");
		started = true;
	}

	private void $Rougamo_Init()
	{
		createCardQueue.Clear();
		process = base.transform.Find("Process");
		LastCard = null;
		SelectedCard.Clear();
		CardTopCount = RoleTable.Instance.CardCount;
		base.transform.SetAsFirstSibling();
		cardItemList = new List<CardItem>();
		turnButton = base.transform.Find("ClockBoard/结束回合").GetComponent<ButtonManager>();
		turnButton.onClick.RemoveAllListeners();
		turnButton.onClick.AddListener(FightManager.Instance.TurnEnd);
		RegisterFightInput();
		cardContainer = base.transform.Find("container").GetComponent<CardContainer>();
		selectCardContainer = base.transform.Find("Selectcontainer").GetComponent<CardContainer>();
		ConfirmButton = base.transform.Find("ClockBoard/确定");
		endfight = base.transform.Find("ClockBoard/结束战斗");
		FightAgain = base.transform.Find("ClockBoard/重开战斗");
		UsedCardList = base.transform.Find("ClockBoard/弃牌堆");
		Card_y_position = cardContainer.transform.position.y;
		CardItem.canUse = true;
		IsReset = true;
		ResetButtonCheck();
	}

	private void $Rougamo_RegisterFightInput()
	{
		if (!_fightInputRegistered && KeyManager.playerAction != null)
		{
			KeyManager.playerAction.Main.EndTurn.performed += OnEndTurnInput;
			KeyManager.playerAction.Main.RestartFight.performed += OnRestartFightInput;
			KeyManager.playerAction.Main.EndSelect.performed += OnEndSelect;
			_fightInputRegistered = true;
		}
	}

	private void $Rougamo_UnregisterFightInput()
	{
		if (_fightInputRegistered)
		{
			if (KeyManager.playerAction != null)
			{
				KeyManager.playerAction.Main.EndTurn.performed -= OnEndTurnInput;
				KeyManager.playerAction.Main.RestartFight.performed -= OnRestartFightInput;
				KeyManager.playerAction.Main.EndSelect.performed -= OnEndSelect;
			}
			_fightInputRegistered = false;
		}
	}

	private void $Rougamo_OnEndTurnInput(InputAction.CallbackContext context)
	{
		if (!(UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI") != null) || !UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI").gameObject.activeSelf)
		{
			TryEndTurnFromInput();
		}
	}

	private void $Rougamo_OnEndSelect(InputAction.CallbackContext context)
	{
		if (!(UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI") != null) || !UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI").gameObject.activeSelf)
		{
			Yes();
		}
	}

	private void $Rougamo_OnRestartFightInput(InputAction.CallbackContext context)
	{
		if (!(UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI") != null) || !UIManager.Instance.GetUI<ConsoleUI>("ConsoleUI").gameObject.activeSelf)
		{
			TryRestartFightFromInput();
		}
	}

	private void $Rougamo_TryEndTurnFromInput()
	{
		if (!(FightManager.Instance == null) && FightManager.Instance.fightType == FightType.Player && !(turnButton == null) && turnButton.gameObject.activeInHierarchy && turnButton.isInteractable)
		{
			FightManager.Instance.TurnEnd();
		}
	}

	private void $Rougamo_TryRestartFightFromInput()
	{
		if (!(FightAgain == null) && FightAgain.gameObject.activeInHierarchy)
		{
			ButtonManager component = FightAgain.GetComponent<ButtonManager>();
			if (!(component == null) && component.isInteractable)
			{
				FightAgainCheck();
			}
		}
	}

	private void $Rougamo_InitSkill()
	{
		if (RoleTable.Instance.Career.data["ActionImage2"] != "")
		{
			base.transform.Find("Left/Skill2").gameObject.SetActive(value: true);
			base.transform.Find("Left/Skill1").gameObject.SetActive(value: false);
			CreateSkillItem(base.transform.Find("Left/Skill2/Skill1"), 1);
			CreateSkillItem(base.transform.Find("Left/Skill2/Skill2"), 2);
		}
		else if (RoleTable.Instance.Career.data["ActionImage1"] != "")
		{
			Transform tempItem = base.transform.Find("Left/Skill1");
			CreateSkillItem(tempItem, 1);
		}
	}

	private void $Rougamo_CreateSkillItem(Transform tempItem, int index)
	{
		if (RoleTable.Instance.Career.data.TryGetValue("Skill" + index, out var value))
		{
			tempItem.Find("Icon").gameObject.SetActive(value: true);
			tempItem.GetComponent<SkillItem>().enabled = true;
			tempItem.GetComponent<SkillItem>().Init(new DataConfig(value, DataType.Card));
			tempItem.Find("Icon").GetComponent<Image>().sprite = ResourceLoader.Load<Sprite>(RoleTable.Instance.Career.data["ActionImage" + index]);
			tempItem.Find("Icon").GetComponent<Image>().enabled = true;
			skillItems.Add(tempItem.GetComponent<SkillItem>());
		}
	}

	private void $Rougamo_UpdateSkill()
	{
		foreach (SkillItem skillItem in skillItems)
		{
			skillItem.UpdateSkillTime();
		}
	}

	private void $Rougamo_CreateDeckMenu()
	{
		UIManager.Instance.ShowUI<DeckUI>("DeckUI").CreateDeckMenu();
	}

	private void $Rougamo_CreateUsedCardList()
	{
		UIManager.Instance.ShowUI<DeckUI>("DeckUI").CreateUsedDeckMenu();
	}

	private void $Rougamo_UpdatePower()
	{
		base.transform.Find("Left/Time/total/val").GetComponent<TMP_Text>().text = FightPlayer.Instance.CurPowerCount + "/" + FightPlayer.Instance.MaxPowerCount;
	}

	private void $Rougamo_FightAgainCheck()
	{
		UIManager.Instance.ShowModalWindow("Tips", "是否要重启战斗", delegate
		{
			FightAgainBtn();
		});
	}

	private void $Rougamo_FightAgainBtn()
	{
		if (!IsReset && !quickReset)
		{
			IsReset = true;
			quickReset = true;
			ResetButtonCheck();
			UniTask.WaitForSeconds(1).ContinueWith(delegate
			{
				quickReset = false;
				ResetButtonCheck();
			}).Forget();
			StopAllCoroutines();
			chest.SetActive(value: false);
			base.transform.Find("Left").GetComponent<CanvasGroup>().alpha = 1f;
			FightManager.Instance.ReSetFight();
			turnButton.gameObject.SetActive(value: true);
		}
	}

	private void $Rougamo_ResetButtonCheck()
	{
		bool flag = false;
		flag = ((!IsReset && !quickReset) ? true : false);
		if (FightAgain.GetComponent<ButtonManager>().isInteractable != flag)
		{
			FightAgain.GetComponent<ButtonManager>().Interactable(flag);
		}
	}

	private void $Rougamo_Reset()
	{
		StopAllCoroutines();
		createCardQueue.Clear();
		UIManager.Instance.CloseUI("DeckUI");
		ConfirmButton.gameObject.SetActive(value: false);
		turnButton.gameObject.SetActive(value: true);
		Singleton<EventCenter>.Instance.Clear(EventDispose.OnFightEnd, EventDispose.OnTrigger);
		foreach (KeyValuePair<string, StatusManager> status in FightManager.Instance.statuses)
		{
			if (status.Value != null && status.Value.gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(status.Value.gameObject);
			}
		}
		foreach (CardItem item in new List<CardItem>(cardItemList))
		{
			if (item != null && item.gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(item.gameObject);
			}
		}
		cardItemList.Clear();
		foreach (CardItem item2 in new List<CardItem>(WaitCard))
		{
			if (item2 != null && item2.gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(item2.gameObject);
			}
		}
		WaitCard.Clear();
		skillItems.Clear();
		foreach (CardItem item3 in SelectedCard)
		{
			if (item3 != null && item3.gameObject != null)
			{
				UnityEngine.Object.DestroyImmediate(item3.gameObject);
			}
		}
		if (InIEn)
		{
			InIEn = false;
			StopAllCoroutines();
			if (instance != null)
			{
				UnityEngine.Object.DestroyImmediate(instance);
			}
		}
		FightManager.Instance.statuses.Clear();
		RoleTable.Instance.isDead = false;
		FightManager.Instance.ActionQueue.Clear();
		ResetSelectState();
		Init();
	}

	private void $Rougamo_onChangeTurnBtn()
	{
		if (FightManager.Instance == null)
		{
			return;
		}
		AudioManager.Instance?.PlayEffect("NewSounds/战斗中/切换回合/切换回合");
		if (UIManager.Instance != null && (bool)UIManager.Instance.GetUI<LineUI>("LineUI"))
		{
			UIManager.Instance.GetUI<LineUI>("LineUI").Hide();
		}
		foreach (CardItem item in new List<CardItem>(cardItemList))
		{
			if (item.Tags.Contains("Nihility"))
			{
				item.InternalBurning();
			}
		}
		if (FightManager.Instance.fightType == FightType.Player)
		{
			if (turnButton != null)
			{
				turnButton.Interactable(value: false);
			}
			StartCoroutine(TurnBtn());
		}
	}

	private void $Rougamo_CreateCardItem(int Count)
	{
		if (FightPlayer.Instance == null)
		{
			return;
		}
		for (int i = 0; i < Count; i++)
		{
			Singleton<EventCenter>.Instance.EventTrigger("ICreateCardItem" + FightPlayer.Instance.InstanceId);
		}
		Singleton<EventCenter>.Instance.EventTrigger("CreateCardItem" + FightPlayer.Instance.InstanceId);
		for (int j = 0; j < Count; j++)
		{
			if (cardItemList.Count + createCardQueue.Count < CardTopCount)
			{
				if (!FightCardManager.Instance.HasCard() || FightCardManager.Instance.cardList.Count < ShouldCard)
				{
					FightCardManager.Instance.RandomIndex();
				}
				if (!FightCardManager.Instance.HasCard())
				{
					break;
				}
				DataConfig dataConfig = FightCardManager.Instance.DrawCard();
				CreateCardItem(dataConfig);
			}
			else
			{
				UIManager.Instance.ShowTip("手牌满了");
				Singleton<NarrationManager>.Instance.Play(21);
			}
		}
		Singleton<EventCenter>.Instance.EventTrigger("EndCreateCardItem" + FightPlayer.Instance.Status.InstanceId);
		base.transform.SetAsFirstSibling();
	}

	private void $Rougamo_CreateCardItem(DataConfig dataConfig)
	{
		if (dataConfig != null && !cardItemList.Any((CardItem item) => item.dataConfig == dataConfig) && !createCardQueue.Any((DataConfig config) => config == dataConfig))
		{
			if (cardItemList.Count + createCardQueue.Count >= CardTopCount)
			{
				FightCardManager.Instance.cardList.Add(dataConfig);
				UIManager.Instance.ShowTip("手牌满了");
			}
			else
			{
				createCardQueue.Enqueue(dataConfig);
			}
		}
	}

	private void $Rougamo_CreateCardItemInternal(DataConfig dataConfig)
	{
		AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/抽牌");
		dataConfig.scriptExecutor.Self = FightPlayer.Instance.Status;
		dataConfig.scriptExecutor.RunScript("InitScript");
		if (!(cardContainer == null))
		{
			Singleton<EventCenter>.Instance.EventTrigger("CreateInt" + FightPlayer.Instance.InstanceId, new CreateData(dataConfig, FightPlayer.Instance.InstanceId));
			GameObject obj = UnityEngine.Object.Instantiate(ResourceLoader.Load("UI/CardItem"), cardContainer.transform) as GameObject;
			obj.GetComponent<RectTransform>().anchoredPosition = new Vector2(-1500f, Card_y_position);
			CardItem cardItem = obj.AddComponent(Type.GetType(dataConfig.Vars["BaseScript"])) as CardItem;
			cardItem.selectContainer = selectCardContainer;
			cardItem.cardcontainer = cardContainer;
			cardItem.Init(dataConfig);
			cardItemList.Add(cardItem);
			base.transform.SetAsFirstSibling();
			UpdateCardItemPos();
			Singleton<EventCenter>.Instance.EventTrigger("EndCreateCardItem" + FightPlayer.Instance.Status.InstanceId);
		}
	}

	private void $Rougamo_UpdateCardItemPos(TweenCallback OnComplete = null, CardContainer from = null)
	{
		List<CardItem> list = new List<CardItem>();
		if (cardContainer == null)
		{
			return;
		}
		if (from == null)
		{
			list = cardItemList.Where((CardItem x) => !SelectedCard.Contains(x)).ToList();
			cardContainer.UpdateCardItemPos(list, OnComplete);
		}
		else
		{
			list = SelectedCard;
			selectCardContainer.UpdateCardItemPos(list, OnComplete);
		}
	}

	private void $Rougamo_ShuffleCardItems()
	{
		if (this.IsNull())
		{
			return;
		}
		GameObject tempItem = UnityEngine.Object.Instantiate(base.transform.Find("ClockBoard/弃牌堆").gameObject, base.transform.Find("ClockBoard"));
		tempItem.SetActive(value: false);
		RectTransform uiElement = tempItem.GetComponent<RectTransform>();
		GameObject trail = UnityEngine.Object.Instantiate(ResourceLoader.Load("UI/Trail"), base.transform) as GameObject;
		Transform vfx = trail.transform.Find("geometryBursts");
		foreach (Transform item in vfx.transform)
		{
			item.GetComponent<VisualEffect>().SetInt("count", 0);
		}
		tempItem.transform.DOMove(base.transform.Find("Left/Card").transform.position, 1f).OnComplete(delegate
		{
			if (!(tempItem == null))
			{
				UnityEngine.Object.Destroy(tempItem);
				foreach (Transform item2 in vfx.transform)
				{
					item2.GetComponent<VisualEffect>().SetInt("count", 0);
				}
				if (this.IsNull())
				{
					UnityEngine.Object.Destroy(trail, 4f);
				}
				else
				{
					foreach (Transform child in base.transform.Find("Left/Card").transform)
					{
						child.DOKill();
						child.DOPunchScale(Vector3.one * 0.2f, 0.3f, 2).OnKill(delegate
						{
							child.localScale = Vector3.one;
						});
					}
					UnityEngine.Object.Destroy(trail, 5f);
				}
			}
		}).OnStart(delegate
		{
			foreach (Transform item3 in vfx.transform)
			{
				item3.GetComponent<VisualEffect>().SetInt("count", 1);
			}
		})
			.OnUpdate(delegate
			{
				foreach (Transform item4 in vfx.transform)
				{
					VisualEffect component = item4.GetComponent<VisualEffect>();
					Vector3 v = PositionUtility.CameraSpaceToZeroPlane(uiElement);
					component.SetVector3("startPos", v);
					component.SetFloat("direction", 160f);
				}
			})
			.OnKill(delegate
			{
				if (!(tempItem == null))
				{
					UnityEngine.Object.Destroy(tempItem);
					foreach (Transform item5 in vfx.transform)
					{
						item5.GetComponent<VisualEffect>().SetInt("count", 0);
					}
					if (this.IsNull())
					{
						UnityEngine.Object.Destroy(trail, 4f);
					}
					else
					{
						foreach (Transform child in base.transform.Find("Left/Card").transform)
						{
							child.DOKill();
							child.DOPunchScale(Vector3.one * 0.2f, 0.3f, 2).OnKill(delegate
							{
								child.localScale = Vector3.one;
							});
						}
						UnityEngine.Object.Destroy(trail, 4f);
					}
				}
			});
	}

	private void $Rougamo_UpdateCardMsg()
	{
		for (int i = 0; i < cardItemList.Count; i++)
		{
			cardItemList[i].DataUpdate();
		}
		foreach (SkillItem skillItem in skillItems)
		{
			skillItem.DataUpdate();
		}
		UpdateCardsShow();
		foreach (Enemy enemy in EnemyManager.Instance.enemyList)
		{
			enemy.UpdataActionShow();
		}
	}

	private void $Rougamo_UpdateCardsShow()
	{
		base.transform.Find("Left/Card/val").GetComponent<TMP_Text>().text = FightCardManager.Instance.cardList.Count().ToString();
		base.transform.Find("ClockBoard/弃牌堆/val").GetComponent<TMP_Text>().text = FightCardManager.Instance.usedCardList.Count().ToString();
	}

	private void $Rougamo_RemoveAllCards()
	{
		int burnCount = (int)FightPlayer.Instance.Status.dynamicVariables.GetValueOrDefault("BurnCount", 0f);
		StartCoroutine(RemoveAllCardsAfterBurn(burnCount));
	}

	private void $Rougamo_ThrowCardScript(string val, string Type)
	{
		if (PreparePendingSelectRequest())
		{
			StartCoroutine(Throw(val, Type));
		}
	}

	private void $Rougamo_Burning(string val, string Type)
	{
		if (PreparePendingSelectRequest())
		{
			StartCoroutine(Burn(val, Type));
		}
	}

	private void $Rougamo_SelectCardToAction(string val, Action<List<CardItem>> onCardSelected, string Type = "1")
	{
		if (onCardSelected != null && PreparePendingSelectRequest())
		{
			StartCoroutine(CardToAction(val, onCardSelected, Type));
		}
	}

	private bool $Rougamo_PreparePendingSelectRequest()
	{
		if (isWin)
		{
			return false;
		}
		CardItem.canUse = false;
		return true;
	}

	private int $Rougamo_BurnAndThrowCheck()
	{
		int num = 0;
		foreach (CardItem cardItem in cardItemList)
		{
			if (!cardItem.Tags.Contains("Froze"))
			{
				num++;
			}
		}
		return num;
	}

	private void $Rougamo_BurnCard(CardItem cardItem)
	{
		cardItem.Burning();
	}

	private void $Rougamo_SelectInit(string uitype)
	{
		base.transform.Find("ClockBoard/结束回合").gameObject.SetActive(value: false);
		CardItem.canUse = false;
		string path = "UI/FightCardInstance";
		selectConfirmed = false;
		if (!InIEn)
		{
			InIEn = true;
			if (prefabA == null)
			{
				prefabA = ResourceLoader.Load(path) as GameObject;
				prefabA.name = "prefabA";
			}
		}
		if (!(prefabA != null))
		{
			return;
		}
		instance = UnityEngine.Object.Instantiate(prefabA, base.transform);
		instance.name = "SelectCard";
		instance.transform.localPosition = new Vector3(0f, 0f, 0f);
		instance.transform.SetAsFirstSibling();
		if (ConfirmButton != null)
		{
			if (uitype != "2")
			{
				ConfirmButton.gameObject.SetActive(value: false);
				CanBeforeEnd = false;
			}
			else
			{
				ConfirmButton.gameObject.SetActive(value: true);
				CanBeforeEnd = true;
			}
			ConfirmButton.GetComponent<ButtonManager>().onClick.RemoveAllListeners();
			ConfirmButton.GetComponent<ButtonManager>().onClick.AddListener(Yes);
		}
		instance.transform.SetAsLastSibling();
		Title = instance.transform.Find("Title").GetComponent<TMP_Text>();
	}

	private void $Rougamo_RefreshSelectConfirmButton()
	{
		if (!(ConfirmButton == null))
		{
			ConfirmButton.gameObject.SetActive(InIEn && !selectConfirmed && (SpecialCount <= 0 || CanBeforeEnd));
		}
	}

	private void $Rougamo_ResetSelectState()
	{
		SpecialCount = 0;
		CanBeforeEnd = false;
		selectConfirmed = false;
		SelectType = null;
	}

	private void $Rougamo_ShowBattleReward()
	{
		if (ShowReward && !(FightManager.Instance == null) && FightManager.Instance.fightType != FightType.Loss)
		{
			ShowReward = false;
			chest.transform.Find("body/Close").gameObject.SetActive(value: false);
			chest.transform.Find("body/Open").gameObject.SetActive(value: true);
			MapManager.Instance.ModeMapManager.SetRewardType("normal");
			UIManager.Instance.ShowUI<BattleRewardsUI>("BattleRewardsUI");
			Close();
		}
	}

	private void $Rougamo_CanWin()
	{
		base.transform.Find("ClockBoard/结束回合").gameObject.SetActive(value: false);
		EndInstance();
		UIManager.Instance.CloseUI("DeckUI");
		if (EnemyManager.Instance.enemyList.Count != 0)
		{
			foreach (Enemy item in new List<Enemy>(EnemyManager.Instance.enemyList))
			{
				item.Status.EnemyDead();
			}
		}
		FightAgain.gameObject.SetActive(value: false);
		endfight.gameObject.SetActive(value: true);
		if (EventSystem.current != null)
		{
			EventSystem.current.enabled = true;
		}
		if (!isWin)
		{
			isWin = true;
			if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.Loss || FightManager.Instance.fightType == FightType.Win)
			{
				return;
			}
			FightManager.Instance.CmdChangeType(FightType.Win);
			FightPlayer.Instance?.Status?.PlayVocal(IStatusManager.VocalState.Win);
			UIManager.Instance.CloseUI("DeckUI");
		}
		ConfirmButton.gameObject.SetActive(value: false);
		ResetSelectState();
		DoTurnAnimation(0f);
	}

	private void $Rougamo_EndInstance()
	{
		if (InIEn)
		{
			InIEn = false;
			StopAllCoroutines();
			if (instance != null)
			{
				UnityEngine.Object.Destroy(instance);
			}
		}
		ResetSelectState();
	}

	private void $Rougamo_Yes()
	{
		if (InIEn && (SpecialCount <= 0 || CanBeforeEnd) && !(ConfirmButton == null) && ConfirmButton.gameObject.activeSelf)
		{
			selectConfirmed = true;
			if (ConfirmButton != null)
			{
				ConfirmButton.gameObject.SetActive(value: false);
			}
		}
	}

	private bool $Rougamo_AllCannotUse()
	{
		foreach (CardItem cardItem in cardItemList)
		{
			if (!cardItem.Tags.Contains("Froze"))
			{
				return false;
			}
		}
		return true;
	}

	private void $Rougamo_CallActionAnimation(IScriptExecutor scriptExecutor)
	{
		AnimationData animationData = new AnimationData
		{
			effectName = scriptExecutor.dataConfig.data.GetValueOrDefault("Effects", null),
			status = new StatusManager[scriptExecutor.Object.Count + 1]
		};
		animationData.status[0] = scriptExecutor.Self as StatusManager;
		for (int i = 0; i < scriptExecutor.Object.Count; i++)
		{
			animationData.status[i + 1] = scriptExecutor.Object[i] as StatusManager;
		}
		animationData.status = animationData.status.Distinct().ToArray();
		animationData.animationState = new IStatusManager.AnimatedState[animationData.status.Length];
		if (scriptExecutor.dataConfig.data.ContainsKey("Action") && Enum.TryParse<IStatusManager.AnimatedState>(scriptExecutor.dataConfig.data["Action"], out var result))
		{
			animationData.animationState[0] = result;
		}
		for (int j = 1; j < animationData.status.Length; j++)
		{
			if (animationData.status[j] != scriptExecutor.Self && (!(scriptExecutor.Self.fatherObject is FightPlayer) || !(animationData.status[j].fatherObject is OtherPlayer)))
			{
				animationData.animationState[j] = animationData.status[j].ConsumeHitReactionAnimationState();
			}
		}
		if (!string.IsNullOrEmpty(animationData.effectName))
		{
			ISingleton<IEffectManager>.Instance.PlayActionEffect(scriptExecutor, animationData.effectName, 0.05f);
		}
		else
		{
			IStatusManager.AnimatedState animatedState = animationData.animationState[0];
			if (animatedState == IStatusManager.AnimatedState.Attack || animatedState == IStatusManager.AnimatedState.Skill)
			{
				ISingleton<IEffectManager>.Instance.PlayActionEffect(scriptExecutor, scriptExecutor.Self.fatherObject.GetRoleEffectName(animationData.animationState[0]), 0.05f);
				ISingleton<IEffectManager>.Instance.PlayActionEffect(scriptExecutor, scriptExecutor.Self.fatherObject.GetRoleEffectName(IStatusManager.AnimatedState.Hit), 0.05f);
			}
		}
		animationQueue.Enqueue(animationData);
		DOActionAnimation();
		FightManager.Instance.EnqueueEvent(new ActionAnimation().Create(animationData));
	}

	private void $Rougamo_BeginStatusActionAnimation(StatusManager status)
	{
		if (!IsNullExtension.IsNull(status))
		{
			activeActionAnimationCounts[status] = activeActionAnimationCounts.GetValueOrDefault(status, 0) + 1;
		}
	}

	private bool $Rougamo_FinishStatusActionAnimation(StatusManager status)
	{
		if (IsNullExtension.IsNull(status))
		{
			return false;
		}
		int valueOrDefault = activeActionAnimationCounts.GetValueOrDefault(status, 0);
		if (valueOrDefault <= 1)
		{
			activeActionAnimationCounts.Remove(status);
			return true;
		}
		activeActionAnimationCounts[status] = valueOrDefault - 1;
		return false;
	}

	private static bool $Rougamo_IsHitReactionAnimation(IStatusManager.AnimatedState state)
	{
		if (state != IStatusManager.AnimatedState.Hit)
		{
			return state == IStatusManager.AnimatedState.Defend;
		}
		return true;
	}

	private static IStatusManager.AnimatedState $Rougamo_ResolvePlayableAnimationState(StatusManager status, IStatusManager.AnimatedState state)
	{
		if (IsNullExtension.IsNull(status) || state == IStatusManager.AnimatedState.Idle)
		{
			return IStatusManager.AnimatedState.Idle;
		}
		FightObject fatherObject = status.fatherObject;
		if ((object)fatherObject != null && fatherObject.AnimatedStateSprites != null && fatherObject.AnimatedStateSprites.TryGetValue(state, out var value) && value.Length != 0)
		{
			return state;
		}
		return IStatusManager.AnimatedState.Idle;
	}

	private static void $Rougamo_ResetStatusToIdleAnimation(StatusManager status)
	{
		if (IsNullExtension.IsNull(status))
		{
			return;
		}
		RestoreStatusKeywordDisplay(status);
		if (status.fatherObject is OtherObj otherObj)
		{
			otherObj.ShowAction(isShow: true);
		}
		status.animatedState = IStatusManager.AnimatedState.Idle;
		FightObject fatherObject = status.fatherObject;
		if ((object)fatherObject != null)
		{
			fatherObject.AnimationData = (state: IStatusManager.AnimatedState.Idle, index: 0);
			if (fatherObject.AnimatedStateSprites != null && fatherObject.AnimatedStateSprites.TryGetValue(IStatusManager.AnimatedState.Idle, out var value) && value.Length != 0)
			{
				status.SetSprite(value[0]);
			}
		}
	}

	private static void $Rougamo_RestoreStatusKeywordDisplay(StatusManager status)
	{
		if (!IsNullExtension.IsNull(status))
		{
			KeywordDisplay component = status.GetComponent<KeywordDisplay>();
			if (component != null)
			{
				component.enabled = true;
			}
		}
	}

	private static void $Rougamo_RestoreStatusSortingLayer(StatusManager status)
	{
		if (IsNullExtension.IsNull(status))
		{
			return;
		}
		Transform transform = status.transform.Find("body");
		SortingGroup sortingGroup = ((transform == null) ? null : transform.GetComponent<SortingGroup>());
		if (sortingGroup != null)
		{
			sortingGroup.sortingLayerName = "role";
		}
		foreach (SummonObject item in status.Summon)
		{
			SortingGroup sortingGroup2 = ((item == null) ? null : item.transform.Find("body")?.GetComponent<SortingGroup>());
			if (sortingGroup2 != null)
			{
				sortingGroup2.sortingLayerName = "role";
			}
		}
	}

	private void $Rougamo_DOActionAnimation()
	{
		if (animationQueue.Count <= 0)
		{
			return;
		}
		NowAnimation = true;
		AnimationData animationData = animationQueue.Dequeue();
		if (animationData.status == null || animationData.status.Length == 0)
		{
			NowAnimation = false;
			return;
		}
		float num = GameSpeed.MultiplierWith((animationQueue.Count > 0) ? 1.5f : 1f);
		var array = (from x in animationData.status.Select((StatusManager status2, int index3) => new
			{
				status = status2,
				index = index3
			})
			where !IsNullExtension.IsNull(x.status)
			select x).ToArray();
		int num2 = array.Count(x => x.status.initPos.x < 0f);
		int num3 = array.Length - num2;
		Vector3[] array2 = animationData.status.Select((StatusManager x) => (!IsNullExtension.IsNull(x)) ? x.initPos : Vector3.zero).ToArray();
		if (array.Length == 0)
		{
			NowAnimation = false;
			return;
		}
		Vector3[] array3 = new Vector3[array2.Length];
		array2.CopyTo(array3, 0);
		Vector3 position = Camera.main.transform.position;
		var array4 = (from x in array
			where x.status.initPos.x < 0f
			orderby x.status.initPos.x
			select x).ToArray();
		var array5 = (from x in array
			where x.status.initPos.x >= 0f
			orderby x.status.initPos.x
			select x).ToArray();
		for (int num4 = 0; num4 < array4.Length; num4++)
		{
			int index = array4[num4].index;
			array3[index].x = Mathf.Lerp(-5f, -1f, (float)(num4 + 1) / (float)(num2 + 1));
			array3[index] += position;
		}
		for (int num5 = 0; num5 < array5.Length; num5++)
		{
			int index2 = array5[num5].index;
			array3[index2].x = Mathf.Lerp(1f, 5f, (float)(num5 + 1) / (float)(num3 + 1));
			array3[index2] += position;
		}
		array3.CopyTo(array2, 0);
		waitingTime = 0f;
		bool flag = (animationData.animationState[0] == IStatusManager.AnimatedState.Attack || animationData.animationState[0] == IStatusManager.AnimatedState.Special || animationData.animationState[0] == IStatusManager.AnimatedState.Special1 || animationData.animationState[0] == IStatusManager.AnimatedState.Special2 || animationData.animationState[0] == IStatusManager.AnimatedState.Skill) && Singleton<GameRuntimeData>.Instance.settingTable.GetValue("低配模式") == "关闭";
		Dictionary<StatusManager, Vector3> originalScales = new Dictionary<StatusManager, Vector3>();
		Dictionary<StatusManager, Dictionary<Transform, Vector3>> dictionary = new Dictionary<StatusManager, Dictionary<Transform, Vector3>>();
		for (int num6 = 0; num6 < animationData.status.Length; num6++)
		{
			StatusManager status = animationData.status[num6];
			if (IsNullExtension.IsNull(status))
			{
				continue;
			}
			Transform body = status.transform.Find("body");
			if (body == null)
			{
				continue;
			}
			SortingGroup bodySortingGroup = body.GetComponent<SortingGroup>();
			if (bodySortingGroup == null)
			{
				continue;
			}
			bool num7 = animationData.status.Length > 1 && (flag || IsHitReactionAnimation(animationData.animationState[num6]));
			if (status.fatherObject is OtherObj otherObj)
			{
				otherObj.ShowAction(isShow: false);
			}
			if (num7)
			{
				BeginStatusActionAnimation(status);
			}
			if (flag)
			{
				status.transform.DOKill();
				body.DOKill();
			}
			bodySortingGroup.sortingLayerName = "Default";
			foreach (SummonObject item in status.Summon)
			{
				item.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "Default";
			}
			int tempCount = activeTweens.GetValueOrDefault(status, 0) + 1;
			activeTweens[status] = tempCount;
			if (animationData.status.Length == 1)
			{
				if (animationData.animationState[num6] != IStatusManager.AnimatedState.Attack && animationData.animationState[num6] != IStatusManager.AnimatedState.Skill)
				{
					status.animatedState = ResolvePlayableAnimationState(status, animationData.animationState[num6]);
				}
				NowAnimation = false;
				RestoreStatusSortingLayer(status);
				RestoreStatusKeywordDisplay(status);
				continue;
			}
			if (flag)
			{
				status.animatedState = ResolvePlayableAnimationState(status, animationData.animationState[num6]);
				originalScales[status] = body.localScale;
				dictionary[status] = CaptureSummonBodyScales(status);
				Dictionary<Transform, Vector3> summonScales = dictionary[status];
				IRole.AnimationConfig animationConfig = ((IRole)status.fatherObject).TryGetAnimationConfig(status.animatedState);
				if (animationConfig != null)
				{
					float num8 = ((status.fatherObject.Type == "Enemy") ? 1f : (-1f));
					float num9 = Mathf.Abs(originalScales[status].x);
					if (animationConfig.Direction == "Left")
					{
						body.localScale = new Vector3(num9 * num8, originalScales[status].y, originalScales[status].z);
					}
					else if (animationConfig.Direction == "Right")
					{
						body.localScale = new Vector3((0f - num9) * num8, originalScales[status].y, originalScales[status].z);
					}
				}
				SyncSummonBodyScales(summonScales, originalScales[status], body.localScale);
				int num10 = animationData.status.Length;
				float num11 = Mathf.Lerp(0.5f, -0.5f, (float)num6 / (float)(num10 - 1)) * (float)((!(array2[0].x > 0f)) ? 1 : (-1)) * 0.3f;
				if (num6 == 0)
				{
					status.transform.position = new Vector3(7f * (float)((array2[0].x > 0f) ? 1 : (-1)), 0f, 0f);
				}
				ItemSum[status] = 13 - bodySortingGroup.sortingOrder;
				foreach (SummonObject item2 in status.Summon)
				{
					item2.transform.Find("body").GetComponent<SortingGroup>().sortingOrder += ItemSum[status];
				}
				bodySortingGroup.sortingOrder = 13;
				status.GetComponent<KeywordDisplay>().enabled = false;
				status.transform.DOMoveX(array2[num6].x + 1f * (float)((array2[0].x > 0f) ? 1 : (-1)), 0.05f / num).OnUpdate(delegate
				{
					if (!IsNullExtension.IsNull(status))
					{
						status.UpdateObjPos();
					}
				});
				status.transform.DOMoveX(array2[num6].x, 0.3f).SetEase(Ease.OutSine).SetDelay(0.05f / num)
					.OnUpdate(delegate
					{
						if (!IsNullExtension.IsNull(status))
						{
							status.UpdateObjPos();
						}
					});
				status.transform.DOMoveX(array2[num6].x + 1f * (float)((!(array2[0].x > 0f)) ? 1 : (-1)), 0.45f / num).SetEase(Ease.OutSine).SetDelay(0.35f / num)
					.OnUpdate(delegate
					{
						if (!IsNullExtension.IsNull(status))
						{
							status.UpdateObjPos();
						}
					});
				status.transform.DOMoveY(num11, 0.05f / num).OnUpdate(delegate
				{
					if (!IsNullExtension.IsNull(status))
					{
						status.UpdateObjPos();
					}
				});
				status.transform.DOMoveY(-0.1f * (float)((!(array2[0].x > 0f)) ? 1 : (-1)) + num11, 0.35f / num).SetEase(Ease.OutSine).SetDelay(0.05f / num)
					.OnUpdate(delegate
					{
						if (!IsNullExtension.IsNull(status))
						{
							status.UpdateObjPos();
						}
					});
				status.transform.DOMoveY(-0.2f * (float)((!(array2[0].x > 0f)) ? 1 : (-1)) + num11, 0.45f / num).SetEase(Ease.OutSine).SetDelay(0.35f / num)
					.OnUpdate(delegate
					{
						if (!IsNullExtension.IsNull(status))
						{
							status.UpdateObjPos();
						}
					});
				status.transform.DOMove(status.initPos, 0.24f / num).OnKill(delegate
				{
					if (!IsNullExtension.IsNull(status) && tempCount == activeTweens.GetValueOrDefault(status, 0) && activeActionAnimationCounts.GetValueOrDefault(status, 0) <= 1)
					{
						status.SetPosition(status.initPos);
					}
				}).SetDelay(0.8f / num)
					.OnUpdate(delegate
					{
						if (!IsNullExtension.IsNull(status))
						{
							status.UpdateObjPos();
						}
					});
				float num12 = ((originalScales[status].x * body.localScale.x >= 0f) ? 1f : (-1f));
				bool finishHandled = false;
				body.DOScale(new Vector3(originalScales[status].x * num12, originalScales[status].y, originalScales[status].z), 0.24f / num).OnComplete(delegate
				{
					if (!IsNullExtension.IsNull(status) && !finishHandled)
					{
						finishHandled = true;
						bool flag2 = FinishStatusActionAnimation(status);
						body.localScale = originalScales[status];
						SyncSummonBodyScales(summonScales, originalScales[status], body.localScale);
						if (flag2)
						{
							RestoreStatusKeywordDisplay(status);
						}
						if (tempCount == activeTweens.GetValueOrDefault(status, 0))
						{
							NowAnimation = false;
							status.SetPosition(status.initPos);
							bodySortingGroup.sortingOrder = ((IRole)status.fatherObject).GetAnimationLayer(status.animatedState);
							ItemSum[status] = 13 - bodySortingGroup.sortingOrder;
							foreach (SummonObject item3 in status.Summon)
							{
								item3.transform.Find("body").GetComponent<SortingGroup>().sortingOrder -= ItemSum[status];
							}
							bodySortingGroup.sortingLayerName = "role";
							foreach (SummonObject item4 in status.Summon)
							{
								item4.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "role";
							}
							if (flag2)
							{
								ResetStatusToIdleAnimation(status);
							}
						}
					}
				}).OnUpdate(delegate
				{
					if (!IsNullExtension.IsNull(status))
					{
						SyncSummonBodyScales(summonScales, originalScales[status], body.localScale);
						status.UpdateObjPos();
					}
				})
					.OnKill(delegate
					{
						if (!IsNullExtension.IsNull(status) && !finishHandled)
						{
							finishHandled = true;
							bool flag2 = FinishStatusActionAnimation(status);
							body.localScale = originalScales[status];
							SyncSummonBodyScales(summonScales, originalScales[status], body.localScale);
							if (flag2)
							{
								RestoreStatusKeywordDisplay(status);
							}
							if (tempCount == activeTweens.GetValueOrDefault(status, 0))
							{
								NowAnimation = false;
								bodySortingGroup.sortingOrder = ((IRole)status.fatherObject).GetAnimationLayer(status.animatedState);
								bodySortingGroup.sortingLayerName = "role";
								foreach (SummonObject item5 in status.Summon)
								{
									item5.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "Default";
								}
								if (flag2)
								{
									ResetStatusToIdleAnimation(status);
								}
							}
						}
					})
					.SetDelay(0.8f / num);
				body.DOScale(body.localScale * 2f, 0.05f / num).OnUpdate(delegate
				{
					if (!IsNullExtension.IsNull(status))
					{
						SyncSummonBodyScales(summonScales, originalScales[status], body.localScale);
						status.UpdateObjPos();
					}
				});
				continue;
			}
			if (IsHitReactionAnimation(animationData.animationState[num6]))
			{
				status.animatedState = ResolvePlayableAnimationState(status, animationData.animationState[num6]);
				ItemSum[status] = 13 - bodySortingGroup.sortingOrder;
				foreach (SummonObject item6 in status.Summon)
				{
					item6.transform.Find("body").GetComponent<SortingGroup>().sortingOrder += ItemSum[status];
				}
				bodySortingGroup.sortingOrder = 13;
				bool finishHandled2 = false;
				status.transform.DOShakePosition(0.3f / num, new Vector3(0.8f, 0.1f, 0f)).OnUpdate(delegate
				{
					if (!IsNullExtension.IsNull(status))
					{
						status.UpdateObjPos();
					}
				}).OnKill(delegate
				{
					if (!IsNullExtension.IsNull(status) && !finishHandled2)
					{
						finishHandled2 = true;
						bool flag2 = FinishStatusActionAnimation(status);
						if (flag2)
						{
							RestoreStatusKeywordDisplay(status);
						}
						if (tempCount == activeTweens.GetValueOrDefault(status, 0))
						{
							bodySortingGroup.sortingLayerName = "role";
							foreach (SummonObject item7 in status.Summon)
							{
								item7.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "role";
							}
							NowAnimation = false;
							if (flag2)
							{
								status.SetPosition(status.initPos);
							}
							bodySortingGroup.sortingOrder = ((IRole)status.fatherObject).GetAnimationLayer(status.animatedState);
							ItemSum[status] = 13 - bodySortingGroup.sortingOrder;
							foreach (SummonObject item8 in status.Summon)
							{
								item8.transform.Find("body").GetComponent<SortingGroup>().sortingOrder -= ItemSum[status];
							}
							if (flag2)
							{
								ResetStatusToIdleAnimation(status);
							}
						}
					}
				})
					.OnComplete(delegate
					{
						if (!IsNullExtension.IsNull(status) && !finishHandled2)
						{
							finishHandled2 = true;
							bool flag2 = FinishStatusActionAnimation(status);
							if (flag2)
							{
								RestoreStatusKeywordDisplay(status);
							}
							if (tempCount == activeTweens.GetValueOrDefault(status, 0))
							{
								bodySortingGroup.sortingLayerName = "role";
								foreach (SummonObject item9 in status.Summon)
								{
									item9.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "role";
								}
								NowAnimation = false;
								if (flag2)
								{
									status.SetPosition(status.initPos);
								}
								bodySortingGroup.sortingOrder = ((IRole)status.fatherObject).GetAnimationLayer(status.animatedState);
								ItemSum[status] = 13 - bodySortingGroup.sortingOrder;
								foreach (SummonObject item10 in status.Summon)
								{
									item10.transform.Find("body").GetComponent<SortingGroup>().sortingOrder -= ItemSum[status];
								}
								if (flag2)
								{
									ResetStatusToIdleAnimation(status);
								}
							}
						}
					});
				continue;
			}
			if (animationData.animationState[num6] != IStatusManager.AnimatedState.Attack && animationData.animationState[num6] != IStatusManager.AnimatedState.Skill)
			{
				NowAnimation = false;
				status.animatedState = ResolvePlayableAnimationState(status, animationData.animationState[num6]);
				bodySortingGroup.sortingLayerName = "role";
				foreach (SummonObject item11 in status.Summon)
				{
					item11.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "role";
				}
				RestoreStatusKeywordDisplay(status);
				continue;
			}
			NowAnimation = false;
			bodySortingGroup.sortingLayerName = "role";
			foreach (SummonObject item12 in status.Summon)
			{
				item12.transform.Find("body").GetComponent<SortingGroup>().sortingLayerName = "role";
			}
			RestoreStatusKeywordDisplay(status);
		}
		if (flag)
		{
			Resources.Load<Material>("Material/PostProcess/Blur")?.EnableKeyword("_BLUR_ON");
			if (waitingTime > 0f)
			{
				waitingTime = 0f;
			}
			else if (!blurReturn)
			{
				RestoreBlurAsync(num).Forget();
			}
		}
	}

	private static Dictionary<Transform, Vector3> $Rougamo_CaptureSummonBodyScales(StatusManager status)
	{
		Dictionary<Transform, Vector3> dictionary = new Dictionary<Transform, Vector3>();
		if (status == null || status.Summon == null)
		{
			return dictionary;
		}
		foreach (SummonObject item in status.Summon)
		{
			if (!(item == null) && !(item.gameObject == null))
			{
				Transform transform = item.transform.Find("body");
				if (!(transform == null))
				{
					dictionary[transform] = transform.localScale;
				}
			}
		}
		return dictionary;
	}

	private static void $Rougamo_SyncSummonBodyScales(Dictionary<Transform, Vector3> summonScales, Vector3 ownerOriginalScale, Vector3 ownerCurrentScale)
	{
		if (summonScales == null || summonScales.Count == 0)
		{
			return;
		}
		foreach (KeyValuePair<Transform, Vector3> summonScale in summonScales)
		{
			Transform key = summonScale.Key;
			if (!(key == null))
			{
				key.localScale = new Vector3(ApplyScaleRatio(summonScale.Value.x, ownerOriginalScale.x, ownerCurrentScale.x), ApplyScaleRatio(summonScale.Value.y, ownerOriginalScale.y, ownerCurrentScale.y), ApplyScaleRatio(summonScale.Value.z, ownerOriginalScale.z, ownerCurrentScale.z));
			}
		}
	}

	private static float $Rougamo_ApplyScaleRatio(float originalValue, float ownerOriginalValue, float ownerCurrentValue)
	{
		if (!(Mathf.Abs(ownerOriginalValue) < 0.0001f))
		{
			return originalValue * ownerCurrentValue / ownerOriginalValue;
		}
		return originalValue;
	}

	private void $Rougamo_DoCardUseAnimation(UseCard.CardUseData cardUseData, bool toThrow = true, bool needInit = false)
	{
		if (!Application.isEditor || Application.isPlaying)
		{
			CardItem cardItem = UnityEngine.Object.Instantiate(ResourceLoader.Load<GameObject>("UI/CardItem"), base.transform.Find("CenterCardContainer")).AddComponent<CardItem>();
			cardItem.enabled = false;
			cardItem.GetComponent<ObjectGroup>().blocksRaycasts = false;
			cardItem.transform.Find("Trigger").gameObject.SetActive(value: false);
			cardItem.GetComponent<SortingGroup>().sortingOrder = -13;
			cardItem.cardcontainer = cardContainer;
			if (needInit)
			{
				cardItem.dataConfig = cardUseData.cardData;
				cardItem.RunScript("InitScript");
			}
			ICard.SetCardStyle(cardItem.transform, cardUseData.cardData);
			ICard.SetPureMsg(cardItem.transform, cardUseData.cardData);
			if (cardUseData.isBurning)
			{
				cardItem.EffectOfBurnCard();
			}
			else if (toThrow)
			{
				cardItem.EffectOfThrowCard("Canvas/FightUI/ClockBoard/弃牌堆");
			}
			else
			{
				cardItem.EffectOfThrowCard("Canvas/FightUI/Left/Card");
			}
		}
	}
}
