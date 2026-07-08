using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Rougamo;
using Rougamo.Context;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.VFX;
using Witch.UI;
using Witch.UI.Window;

public class CardItem : MonoBehaviour, IPointerEnterHandler, IEventSystemHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ICard, ILocalize
{
	[CompilerGenerated]
	private Vector3 <initAngle>k__BackingField;

	[CompilerGenerated]
	private Vector2 <initPosition>k__BackingField;

	[CompilerGenerated]
	private bool <draging>k__BackingField;

	[CompilerGenerated]
	private bool <ignore>k__BackingField;

	public CardContainer cardcontainer;

	public bool hasUse;

	public CardContainer selectContainer;

	private bool _reverse;

	public DataConfig dataConfig;

	public IDictionary<string, string> data;

	public IDictionary<string, string> Vars;

	public StatusManager status;

	public bool hasDone;

	public SynchronizationContext _mainThreadContext;

	public IScriptExecutor enchScriptExecutor;

	[CompilerGenerated]
	private int <index>k__BackingField;

	public CardAnimationController animationController;

	public RectTransform uiElement;

	private Vector2 TargetPos;

	public static bool canUse = true;

	public static int UseCount = 0;

	public Vector3 initAngle
	{
		[CompilerGenerated]
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				Vector3 result = $Rougamo_get_initAngle();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[CompilerGenerated]
		[DebuggerStepThrough]
		set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_initAngle(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public Vector2 initPosition
	{
		[CompilerGenerated]
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				Vector2 result = $Rougamo_get_initPosition();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[CompilerGenerated]
		[DebuggerStepThrough]
		set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_initPosition(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public bool draging
	{
		[CompilerGenerated]
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				bool result = $Rougamo_get_draging();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[CompilerGenerated]
		[DebuggerStepThrough]
		set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_draging(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public bool ignore
	{
		[CompilerGenerated]
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				bool result = $Rougamo_get_ignore();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[CompilerGenerated]
		[DebuggerStepThrough]
		set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_ignore(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public float initScale
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				float result = $Rougamo_get_initScale();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public float selectScale
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				float result = $Rougamo_get_selectScale();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public bool isReverse
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				bool result = $Rougamo_get_isReverse();
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
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_isReverse(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public IScriptExecutor scriptExecutor
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				IScriptExecutor result = $Rougamo_get_scriptExecutor();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public HashSet<string> Tags
	{
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				HashSet<string> result = $Rougamo_get_Tags();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	public int index
	{
		[CompilerGenerated]
		[DebuggerStepThrough]
		get
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				int result = $Rougamo_get_index();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
		[CompilerGenerated]
		[DebuggerStepThrough]
		set
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(CardItem);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_set_index(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}
	}

	[DebuggerStepThrough]
	public void RunScript(string ScriptName)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { ScriptName };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RunScript(ScriptName);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void RunScriptWithDefaultSelf(string scriptName)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { scriptName };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RunScriptWithDefaultSelf(scriptName);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void RunScriptCore(string scriptName)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { scriptName };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RunScriptCore(scriptName);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static void ResetScriptObjectToSelf(IScriptExecutor executor)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { executor };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ResetScriptObjectToSelf(executor);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private static void RestoreScriptTarget(IScriptExecutor executor, List<IStatusManager> originalObject, IStatusManager originalTarget)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[3] { executor, originalObject, originalTarget };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RestoreScriptTarget(executor, originalObject, originalTarget);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public virtual void Init(DataConfig dataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public void SetIndex(int Index)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { Index };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_SetIndex(Index);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public virtual void DrawEffect()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_DrawEffect();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public virtual void OnPointerEnter(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public virtual void OnPointerExit(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public virtual void Awake()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public virtual void Start()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public void ClearEvent()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ClearEvent();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void RegisterEvent()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RegisterEvent();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public CardItem TransformToConfiguredType(DataConfig nextDataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { nextDataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			CardItem result = $Rougamo_TransformToConfiguredType(nextDataConfig);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void PrepareTransformedData(DataConfig nextDataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { nextDataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_PrepareTransformedData(nextDataConfig);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private Type ResolveConfiguredCardType(DataConfig nextDataConfig)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { nextDataConfig };
		try
		{
			modifiable.OnEntry(methodContext);
			Type result = $Rougamo_ResolveConfiguredCardType(nextDataConfig);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void CopyRuntimeStateTo(CardItem newCard)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { newCard };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CopyRuntimeStateTo(newCard);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void OnRightClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_OnRightClick(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void CancelUseDrag()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_CancelUseDrag();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void RefreshTag()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_RefreshTag();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void HandleSelectModeClick(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_HandleSelectModeClick(eventData);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public virtual void DataUpdate()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public virtual void OnBeginDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public Vector2 GetMousePos(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { eventData };
		try
		{
			modifiable.OnEntry(methodContext);
			Vector2 result = $Rougamo_GetMousePos(eventData);
			modifiable.OnSuccess(methodContext);
			return result;
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public virtual void OnDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	private void Update()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public virtual void OnEndDrag(PointerEventData eventData)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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
	public void Burning(float animationDelay = 0f)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { animationDelay };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Burning(animationDelay);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void InternalBurning(float animationDelay = 0f)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { animationDelay };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_InternalBurning(animationDelay);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void Reverse()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_Reverse();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void EffectOfBurnCard()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_EffectOfBurnCard();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void ThrowCard()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_ThrowCard();
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void InternalThrow(bool needUp = true)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[1] { needUp };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_InternalThrow(needUp);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	public void EffectOfThrowCard(string targetPath, bool needUp = true)
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
		methodContext.Arguments = new object[2] { targetPath, needUp };
		try
		{
			modifiable.OnEntry(methodContext);
			$Rougamo_EffectOfThrowCard(targetPath, needUp);
			modifiable.OnSuccess(methodContext);
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[DebuggerStepThrough]
	private void OnDestroy()
	{
		Modifiable modifiable = new Modifiable();
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Target = this;
		methodContext.TargetType = typeof(CardItem);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(CardItem).TypeHandle);
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

	[SpecialName]
	private Vector3 $Rougamo_get_initAngle()
	{
		return initAngle;
	}

	[SpecialName]
	private void $Rougamo_set_initAngle(Vector3 value)
	{
		initAngle = value;
	}

	[SpecialName]
	private Vector2 $Rougamo_get_initPosition()
	{
		return initPosition;
	}

	[SpecialName]
	private void $Rougamo_set_initPosition(Vector2 value)
	{
		initPosition = value;
	}

	[SpecialName]
	private bool $Rougamo_get_draging()
	{
		return draging;
	}

	[SpecialName]
	private void $Rougamo_set_draging(bool value)
	{
		draging = value;
	}

	[SpecialName]
	private bool $Rougamo_get_ignore()
	{
		return ignore;
	}

	[SpecialName]
	private void $Rougamo_set_ignore(bool value)
	{
		ignore = value;
	}

	[SpecialName]
	private float $Rougamo_get_initScale()
	{
		return 0.6f;
	}

	[SpecialName]
	private float $Rougamo_get_selectScale()
	{
		return 0.85f;
	}

	[SpecialName]
	private bool $Rougamo_get_isReverse()
	{
		return _reverse;
	}

	[SpecialName]
	private void $Rougamo_set_isReverse(bool value)
	{
		if (value != _reverse)
		{
			_reverse = value;
			if (value)
			{
				initAngle = new Vector3(initAngle.x, 180f, initAngle.z);
			}
			else
			{
				initAngle = new Vector3(initAngle.x, 0f, initAngle.z);
			}
			base.transform.Find("Front/字体").gameObject.SetActive(!value);
			base.transform.Find("Front/cost").gameObject.SetActive(!value);
			base.gameObject.GetComponent<KeywordDisplay>().enabled = !value;
			base.transform.DORotate(initAngle, 0.4f);
		}
	}

	[SpecialName]
	private IScriptExecutor $Rougamo_get_scriptExecutor()
	{
		return dataConfig.scriptExecutor;
	}

	[SpecialName]
	private HashSet<string> $Rougamo_get_Tags()
	{
		if (!FightCardManager.Instance.CardTags.ContainsKey(dataConfig))
		{
			return new HashSet<string>();
		}
		return FightCardManager.Instance.CardTags[dataConfig];
	}

	private void $Rougamo_RunScript(string ScriptName)
	{
		if (ScriptName == "DrawScript" || ScriptName == "DropScript")
		{
			RunScriptWithDefaultSelf(ScriptName);
		}
		else
		{
			RunScriptCore(ScriptName);
		}
	}

	private void $Rougamo_RunScriptWithDefaultSelf(string scriptName)
	{
		List<IStatusManager> originalObject = new List<IStatusManager>(scriptExecutor.Object);
		IStatusManager target = scriptExecutor.Target;
		List<IStatusManager> originalObject2 = null;
		IStatusManager originalTarget = null;
		if (enchScriptExecutor != null)
		{
			originalObject2 = new List<IStatusManager>(enchScriptExecutor.Object);
			originalTarget = enchScriptExecutor.Target;
		}
		try
		{
			ResetScriptObjectToSelf(scriptExecutor);
			RunScriptCore(scriptName);
		}
		finally
		{
			RestoreScriptTarget(scriptExecutor, originalObject, target);
			if (enchScriptExecutor != null)
			{
				RestoreScriptTarget(enchScriptExecutor, originalObject2, originalTarget);
			}
		}
	}

	private void $Rougamo_RunScriptCore(string scriptName)
	{
		if (enchScriptExecutor != null)
		{
			enchScriptExecutor.Object = new List<IStatusManager>(scriptExecutor.Object);
			enchScriptExecutor.Target = scriptExecutor.Target;
			enchScriptExecutor.RunScript(scriptName);
		}
		if (scriptName != "PreUseScript")
		{
			scriptExecutor.RunScript(scriptName);
		}
	}

	private static void $Rougamo_ResetScriptObjectToSelf(IScriptExecutor executor)
	{
		if (executor?.Self != null)
		{
			executor.Object.Clear();
			executor.Object.Add(executor.Self);
		}
	}

	private static void $Rougamo_RestoreScriptTarget(IScriptExecutor executor, List<IStatusManager> originalObject, IStatusManager originalTarget)
	{
		executor.Object.Clear();
		if (originalObject != null)
		{
			executor.Object.AddRange(originalObject);
		}
		executor.Target = originalTarget;
	}

	private void $Rougamo_Init(DataConfig dataConfig)
	{
		if (FightPlayer.Instance == null)
		{
			return;
		}
		status = FightPlayer.Instance.Status as StatusManager;
		if (!FightPlayer.Instance.Status.IsNull())
		{
			if (RoleTable.Instance.enchasedDict.ContainsKey(dataConfig.InstanceID))
			{
				enchScriptExecutor = RoleTable.Instance.enchasedDict[dataConfig.InstanceID].scriptExecutor;
				enchScriptExecutor.Self = status;
			}
			this.dataConfig = dataConfig;
			data = dataConfig.data;
			Vars = dataConfig.Vars;
			scriptExecutor.Self = status;
			scriptExecutor.Object.Clear();
			FightCardManager.Instance.CardTagCheck(dataConfig);
			scriptExecutor.Object.Add(FightPlayer.Instance.Status);
			scriptExecutor.dataConfig.Vars["Usable"] = "1";
			ICard.SetCardStyle(base.transform, dataConfig);
			DrawEffect();
			DataUpdate();
			Vars["HasBurn"] = "False";
			if (EnemyManager.Instance != null && EnemyManager.Instance.enemyList.Count > 0)
			{
				scriptExecutor.Target = EnemyManager.Instance.enemyList[0].Status;
			}
		}
	}

	[SpecialName]
	private int $Rougamo_get_index()
	{
		return index;
	}

	[SpecialName]
	private void $Rougamo_set_index(int value)
	{
		index = value;
	}

	private void $Rougamo_SetIndex(int Index)
	{
		index = Index;
		base.transform.SetSiblingIndex(index);
		base.transform.GetComponent<SortingGroup>().sortingOrder = index - 13;
	}

	private void $Rougamo_DrawEffect()
	{
	}

	private void $Rougamo_OnPointerEnter(PointerEventData eventData)
	{
		AudioManager.Instance.PlayEffect("NewSounds/新音效/金属卡牌滑动");
		if (!draging && base.enabled && canUse && !hasUse && !FightUI.SelectedCard.Contains(this))
		{
			index = base.transform.GetSiblingIndex();
			animationController.PlayEnterAnimation(new Vector2(initPosition.x, -30f), selectScale);
			animationController.RotateWithMouse().Forget();
		}
	}

	private void $Rougamo_OnPointerExit(PointerEventData eventData)
	{
		if (!draging && !FightUI.SelectedCard.Contains(this))
		{
			base.gameObject.GetComponent<EventTrigger>().enabled = false;
			animationController.PlayExitAnimation(initPosition, initScale).OnComplete(delegate
			{
				base.gameObject.GetComponent<EventTrigger>().enabled = true;
			});
		}
	}

	private void $Rougamo_Awake()
	{
		animationController = new CardAnimationController();
		animationController.Initialize(base.transform, this);
		_mainThreadContext = SynchronizationContext.Current;
		if (base.gameObject.GetComponent<KeywordDisplay>() == null)
		{
			base.gameObject.AddComponent<KeywordDisplay>();
		}
		base.transform.localScale = initScale * Vector3.one;
		uiElement = GetComponent<RectTransform>();
		RegisterEvent();
		EventTrigger obj = base.gameObject.GetComponent<EventTrigger>() ?? base.gameObject.AddComponent<EventTrigger>();
		obj.triggers.RemoveAll((EventTrigger.Entry entry2) => entry2.eventID == EventTriggerType.PointerDown);
		EventTrigger.Entry entry = new EventTrigger.Entry();
		entry.eventID = EventTriggerType.PointerDown;
		entry.callback.AddListener(delegate(BaseEventData data)
		{
			OnRightClick((PointerEventData)data);
		});
		obj.triggers.Add(entry);
	}

	private void $Rougamo_Start()
	{
	}

	private void $Rougamo_ClearEvent()
	{
		Singleton<EventCenter>.Instance.Clear(this);
	}

	private void $Rougamo_RegisterEvent()
	{
		Singleton<EventCenter>.Instance.AddEventListener(LanguageEvent.LanguageChange.ToString(), DataUpdate, this);
	}

	private CardItem $Rougamo_TransformToConfiguredType(DataConfig nextDataConfig)
	{
		if (nextDataConfig == null)
		{
			return this;
		}
		PrepareTransformedData(nextDataConfig);
		Type type = ResolveConfiguredCardType(nextDataConfig);
		if (type == null || type == GetType())
		{
			Init(nextDataConfig);
			return this;
		}
		bool flag = isReverse;
		bool flag2 = base.enabled;
		int num = FightUI.cardItemList.IndexOf(this);
		CardItem cardItem = base.gameObject.AddComponent(type) as CardItem;
		if (cardItem == null)
		{
			Init(nextDataConfig);
			return this;
		}
		CopyRuntimeStateTo(cardItem);
		cardItem.Init(nextDataConfig);
		cardItem.initAngle = initAngle;
		cardItem.initPosition = initPosition;
		cardItem.enabled = flag2;
		cardItem.isReverse = flag;
		if (num >= 0)
		{
			FightUI.cardItemList[num] = cardItem;
		}
		for (int i = 0; i < FightUI.SelectedCard.Count; i++)
		{
			if (FightUI.SelectedCard[i] == this)
			{
				FightUI.SelectedCard[i] = cardItem;
			}
		}
		if (cardcontainer != null && cardcontainer.cardTweenDict.TryGetValue(this, out var value))
		{
			cardcontainer.cardTweenDict.Remove(this);
			cardcontainer.cardTweenDict[cardItem] = value;
		}
		ClearEvent();
		base.enabled = false;
		UnityEngine.Object.Destroy(this);
		return cardItem;
	}

	private void $Rougamo_PrepareTransformedData(DataConfig nextDataConfig)
	{
		if (!(FightPlayer.Instance == null) && FightPlayer.Instance.Status != null)
		{
			nextDataConfig.scriptExecutor.Self = FightPlayer.Instance.Status;
			nextDataConfig.scriptExecutor.RunScript("InitScript");
		}
	}

	private Type $Rougamo_ResolveConfiguredCardType(DataConfig nextDataConfig)
	{
		string valueOrDefault = nextDataConfig.Vars.GetValueOrDefault("BaseScript", "CommonCardItem");
		Type type = Type.GetType(valueOrDefault);
		if (type == null || !typeof(CardItem).IsAssignableFrom(type))
		{
			UnityEngine.Debug.LogError("Invalid card BaseScript: " + valueOrDefault);
			return null;
		}
		return type;
	}

	private void $Rougamo_CopyRuntimeStateTo(CardItem newCard)
	{
		newCard.draging = draging;
		newCard.ignore = ignore;
		newCard.cardcontainer = cardcontainer;
		newCard.hasUse = hasUse;
		newCard.selectContainer = selectContainer;
		newCard.status = status;
		newCard.hasDone = hasDone;
		newCard.index = index;
		newCard.uiElement = uiElement;
	}

	private void $Rougamo_OnRightClick(PointerEventData eventData)
	{
		if (data == null)
		{
			return;
		}
		if (FightUI.InIEn)
		{
			HandleSelectModeClick(eventData);
		}
		else if (canUse)
		{
			if (eventData.button == PointerEventData.InputButton.Right)
			{
				CancelUseDrag();
			}
		}
		else
		{
			HandleSelectModeClick(eventData);
		}
	}

	private void $Rougamo_CancelUseDrag()
	{
		draging = false;
		base.enabled = false;
		animationController.enddrag();
		animationController.PlayExitAnimation(initPosition, initScale).OnComplete(delegate
		{
			base.enabled = true;
		});
		base.transform.SetSiblingIndex(index);
		if (data != null && data.TryGetValue("InitScript", out var value) && value.Contains("Damage"))
		{
			scriptExecutor.Target = null;
			DataUpdate();
		}
	}

	private void $Rougamo_RefreshTag()
	{
		FightCardManager.Instance.RefreshTag(dataConfig);
		DataUpdate();
	}

	private void $Rougamo_HandleSelectModeClick(PointerEventData eventData)
	{
		if (FightManager.Instance == null || FightManager.Instance.fightType != FightType.Player || hasUse || !base.enabled || eventData.button != PointerEventData.InputButton.Left || !FightUI.InIEn || (Tags.Contains("Froze") && (FightUI.SelectType == "Burn" || FightUI.SelectType == "Throw")))
		{
			return;
		}
		if (FightUI.SelectedCard.Contains(this))
		{
			OnPointerExit(eventData);
			FightUI.SelectedCard.Remove(this);
			FightUI.SpecialCount++;
			base.gameObject.transform.SetParent(cardcontainer.transform);
			base.enabled = false;
			cardcontainer.cardTweenDict.TryGetValue(this, out var value);
			if (value != null && value.IsPlaying())
			{
				value.Kill(complete: true);
			}
			UIManager.Instance.GetUI<FightUI>("FightUI").UpdateCardItemPos(delegate
			{
				if (!(UIManager.Instance.GetUI<FightUI>("FightUI") == null))
				{
					base.enabled = true;
				}
			});
		}
		else
		{
			if (selectContainer == null)
			{
				return;
			}
			if (FightUI.SpecialCount <= 0 && FightUI.SelectedCard.Count > 0)
			{
				CardItem cardItem = FightUI.SelectedCard[FightUI.SelectedCard.Count - 1];
				FightUI.SelectedCard.Remove(cardItem);
				FightUI.SpecialCount++;
				cardItem.gameObject.transform.SetParent(cardcontainer.transform);
				cardItem.enabled = true;
			}
			FightUI.SpecialCount--;
			FightUI.SelectedCard.Add(this);
			base.gameObject.transform.SetParent(selectContainer.transform);
			base.enabled = false;
			cardcontainer.cardTweenDict.TryGetValue(this, out var value2);
			if (value2 != null && value2.IsPlaying())
			{
				value2.Kill(complete: true);
			}
			UIManager.Instance.GetUI<FightUI>("FightUI").UpdateCardItemPos(delegate
			{
				if (!(UIManager.Instance.GetUI<FightUI>("FightUI") == null))
				{
					base.enabled = true;
				}
			}, selectContainer);
			UIManager.Instance.GetUI<FightUI>("FightUI").UpdateCardItemPos(delegate
			{
				if (!(UIManager.Instance.GetUI<FightUI>("FightUI") == null))
				{
					base.enabled = true;
				}
			});
		}
	}

	private void $Rougamo_DataUpdate()
	{
		if (!this.IsNull())
		{
			ICard.SetCardMsg(base.transform, dataConfig, status);
		}
	}

	private void $Rougamo_OnBeginDrag(PointerEventData eventData)
	{
		if (!FightUI.SelectedCard.Contains(this))
		{
			draging = true;
			animationController?.StopMove();
			base.transform.DOKill();
		}
	}

	private Vector2 $Rougamo_GetMousePos(PointerEventData eventData)
	{
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(base.transform.parent.GetComponent<RectTransform>(), eventData.position, eventData.pressEventCamera, out var localPoint))
		{
			return localPoint;
		}
		return default(Vector2);
	}

	private void $Rougamo_OnDrag(PointerEventData eventData)
	{
		if (!FightUI.SelectedCard.Contains(this))
		{
			TargetPos = GetMousePos(eventData);
		}
	}

	private void $Rougamo_Update()
	{
		if (!draging)
		{
			return;
		}
		if (!FightUI.InIEn && canUse && KeyManager.playerAction != null && KeyManager.playerAction.Main.RightClick.WasPressedThisFrame())
		{
			CancelUseDrag();
			return;
		}
		Vector2 anchoredPosition = (TargetPos - base.transform.GetComponent<RectTransform>().anchoredPosition) * 40f * Time.deltaTime + base.transform.GetComponent<RectTransform>().anchoredPosition;
		if ((double)anchoredPosition.magnitude > 0.1)
		{
			base.transform.GetComponent<RectTransform>().anchoredPosition = anchoredPosition;
		}
		else
		{
			base.transform.GetComponent<RectTransform>().anchoredPosition = TargetPos;
		}
	}

	private void $Rougamo_OnEndDrag(PointerEventData eventData)
	{
		draging = false;
		base.enabled = false;
		animationController.enddrag();
		base.enabled = true;
		OnPointerExit(eventData);
		base.transform.SetSiblingIndex(index);
	}

	private void $Rougamo_Burning(float animationDelay = 0f)
	{
		if (!Tags.Contains("Froze"))
		{
			InternalBurning(animationDelay);
		}
	}

	private void $Rougamo_InternalBurning(float animationDelay = 0f)
	{
		if (!hasDone && !(this == null) && !(base.transform == null) && !(base.gameObject == null) && !(FightPlayer.Instance == null))
		{
			Singleton<EventCenter>.Instance.EventTrigger("BurnCard" + status.InstanceId, new BurnData(dataConfig, FightPlayer.Instance.InstanceId));
			base.transform.Find("Front/字体").gameObject.SetActive(value: true);
			ignore = true;
			hasDone = true;
			base.enabled = false;
			FightCardManager.Instance.FightcardList.Remove(dataConfig);
			FightUI.cardItemList.Remove(this);
			FightUI.WaitCard.Remove(this);
			if (FightUI.cardItemList.Count == 0 && FightPlayer.Instance != null)
			{
				Singleton<EventCenter>.Instance.EventTrigger("NoCard" + FightPlayer.Instance.InstanceId);
			}
			UIManager.Instance.GetUI<FightUI>("FightUI").UpdateCardItemPos();
			AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/卡牌焚毁");
			DataUpdate();
			UniTask.WaitForSeconds(GameSpeed.Duration(animationDelay), ignoreTimeScale: false, PlayerLoopTiming.Update, base.destroyCancellationToken).ContinueWith(delegate
			{
				EffectOfBurnCard();
			}).Forget();
		}
	}

	private void $Rougamo_Reverse()
	{
		isReverse = !isReverse;
	}

	private void $Rougamo_EffectOfBurnCard()
	{
		if (cardcontainer == null)
		{
			return;
		}
		base.gameObject.GetComponent<ObjectGroup>().blocksRaycasts = false;
		base.transform.Find("Trigger").gameObject.SetActive(value: false);
		cardcontainer.cardTweenDict.TryGetValue(this, out var value);
		if (value != null && value.IsPlaying())
		{
			value.Kill();
		}
		animationController.StopMove();
		AudioManager.Instance?.PlayEffect("Effect/burn");
		Material material = UnityEngine.Object.Instantiate(ResourceLoader.Load<Material>("Material/CardBurn"));
		material.SetFloat("_Fade", 50f);
		material.SetFloat("_canvasScale", GameObject.Find("Canvas").GetComponent<RectTransform>().localScale.x);
		if (!(base.transform == null) && !(base.transform.gameObject == null))
		{
			material.SetFloat("_startY", base.transform.position.y);
			Sequence sequence = DOTween.Sequence();
			material.mainTexture = base.transform.Find("Front/icon").GetComponent<MeshRenderer>().material.mainTexture;
			Material material2 = new Material(material);
			material2.mainTexture = base.transform.Find("Back/background").GetComponent<MeshRenderer>().material.mainTexture;
			Material material3 = UnityEngine.Object.Instantiate(material);
			material3.mainTexture = base.transform.Find("Front/background").GetComponent<MeshRenderer>().material.mainTexture;
			Material material4 = UnityEngine.Object.Instantiate(material);
			material4.mainTexture = base.transform.Find("Front/FrontBack").GetComponent<MeshRenderer>().material.mainTexture;
			Material material5 = UnityEngine.Object.Instantiate(material);
			material5.mainTexture = base.transform.Find("Front/Icons/Ench/Item").GetComponent<MeshRenderer>().material.mainTexture;
			base.transform.Find("Front/icon").GetComponent<MeshRenderer>().material = material;
			base.transform.Find("Back/background").GetComponent<MeshRenderer>().material = material2;
			base.transform.Find("Front/background").GetComponent<MeshRenderer>().material = material3;
			base.transform.Find("Front/FrontBack").GetComponent<MeshRenderer>().material = material4;
			base.transform.Find("Front/cost/cost").GetComponent<TMP_Text>().DOFade(0f, GameSpeed.Duration(0.6f));
			base.transform.Find("Front/Icons/Ench/Item").GetComponent<MeshRenderer>().material = material5;
			sequence.Insert(0f, base.transform.Find("Front/background").GetComponent<Image>().DOFade(0f, GameSpeed.Duration(0.1f)));
			if (selectContainer != null && base.transform.parent != selectContainer.transform)
			{
				sequence.Insert(0f, uiElement.DOAnchorPos(new Vector3(0f, 600f, 0f), GameSpeed.Duration(0.3f)));
			}
			sequence.Insert(0f, base.transform.DORotate(new Vector3(0f, 0f, 0f), GameSpeed.Duration(0.1f)));
			sequence.Insert(0f, base.transform.Find("Front/字体/msgTxt").GetComponent<TMP_Text>().DOFade(0f, GameSpeed.Duration(0.6f)));
			sequence.Insert(GameSpeed.Duration(0.3f), material.DOFloat(-90f, "_Fade", GameSpeed.Duration(1.5f)));
			sequence.Insert(GameSpeed.Duration(0.3f), material2.DOFloat(-90f, "_Fade", GameSpeed.Duration(1.5f)));
			sequence.Insert(GameSpeed.Duration(0.3f), material3.DOFloat(-90f, "_Fade", GameSpeed.Duration(1.5f)));
			sequence.Insert(GameSpeed.Duration(0.3f), material4.DOFloat(-90f, "_Fade", GameSpeed.Duration(1.5f)));
			sequence.Insert(GameSpeed.Duration(0.3f), base.transform.Find("Front/字体/nameTxt").GetComponent<TMP_Text>().DOFade(0f, GameSpeed.Duration(0.3f)));
			sequence.OnComplete(delegate
			{
				UnityEngine.Object.Destroy(base.gameObject);
			});
		}
	}

	private void $Rougamo_ThrowCard()
	{
		if (!Tags.Contains("Froze"))
		{
			InternalThrow();
		}
	}

	private void $Rougamo_InternalThrow(bool needUp = true)
	{
		FightUI.cardItemList.Remove(this);
		FightUI.WaitCard.Remove(this);
		if (FightUI.cardItemList.Count == 0 && FightPlayer.Instance != null)
		{
			Singleton<EventCenter>.Instance.EventTrigger("NoCard" + FightPlayer.Instance.InstanceId);
		}
		if (!(Vars.GetValueOrDefault("HasBurn", "False") == "True"))
		{
			FightCardManager.Instance.usedCardList.Add(dataConfig);
			if (FightManager.Instance.fightType != FightType.None)
			{
				RunScript("DropScript");
			}
			DataUpdate();
			EffectOfThrowCard("Canvas/FightUI/ClockBoard/弃牌堆", needUp);
		}
	}

	private void $Rougamo_EffectOfThrowCard(string targetPath, bool needUp = true)
	{
		if (hasDone || this == null || base.transform == null || base.gameObject == null || cardcontainer == null)
		{
			return;
		}
		base.transform.Find("Front/字体").gameObject.SetActive(value: true);
		Transform target = GameObject.Find(targetPath).transform;
		cardcontainer.cardTweenDict.TryGetValue(this, out var value);
		if (value != null && value.IsPlaying())
		{
			value.Kill();
		}
		animationController.StopMove();
		base.transform.GetComponent<SortingGroup>().sortingOrder = -25;
		ignore = true;
		hasDone = true;
		base.enabled = false;
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		AudioManager.Instance?.PlayEffect("Cards/cardShove");
		base.transform.Find("Trigger").gameObject.SetActive(value: false);
		if (!(UIManager.Instance.GetUI<FightUI>("FightUI") != null))
		{
			return;
		}
		GameObject trail = UnityEngine.Object.Instantiate(ResourceLoader.Load("UI/Trail"), GameObject.Find("Canvas/FightUI").transform) as GameObject;
		Transform vfx = trail.transform.Find("geometryBursts");
		foreach (Transform item in vfx.transform)
		{
			item.GetComponent<VisualEffect>().SetInt("count", 0);
		}
		UIManager.Instance.GetUI<FightUI>("FightUI").UpdateCardItemPos();
		if (needUp && selectContainer != null && base.transform.parent != selectContainer.transform)
		{
			uiElement.DOAnchorPos(new Vector3(0f, 600f, 0f), GameSpeed.Duration(0.3f));
		}
		uiElement.DORotate(new Vector3(0f, 0f, 0f), GameSpeed.Duration(0.3f));
		base.transform.DOMove(target.position, GameSpeed.Duration(1f)).OnComplete(delegate
		{
			foreach (Transform item2 in vfx.transform)
			{
				item2.GetComponent<VisualEffect>().SetInt("count", 0);
			}
			foreach (Transform child in target)
			{
				child.DOKill();
				child.localScale = Vector3.one;
				child.DOPunchScale(Vector3.one * 0.2f, GameSpeed.Duration(0.3f), 2).OnKill(delegate
				{
					child.localScale = Vector3.one;
				});
			}
			UnityEngine.Object.Destroy(trail, 5f);
			UnityEngine.Object.Destroy(base.gameObject);
		}).OnStart(delegate
		{
			foreach (Transform item3 in vfx.transform)
			{
				item3.GetComponent<VisualEffect>().SetInt("count", 1);
			}
			Vector3 vector = GameObject.Find(targetPath).transform.position - base.transform.position;
			float z = Mathf.Atan2(vector.y, vector.x) * 57.29578f;
			base.transform.DORotateQuaternion(Quaternion.Euler(new Vector3(0f, 0f, z)), GameSpeed.Duration(0.5f));
			base.transform.DOScale(0f, GameSpeed.Duration(0.7f));
		})
			.OnUpdate(delegate
			{
				foreach (Transform item4 in vfx.transform)
				{
					VisualEffect component = item4.GetComponent<VisualEffect>();
					Vector3 v = PositionUtility.CameraSpaceToZeroPlane(uiElement);
					component.SetVector3("startPos", v);
					component.SetFloat("direction", base.transform.rotation.eulerAngles.z * (MathF.PI / 180f));
				}
			})
			.OnKill(delegate
			{
				foreach (Transform item5 in vfx.transform)
				{
					item5.GetComponent<VisualEffect>().SetInt("count", 0);
				}
				foreach (Transform child in target)
				{
					child.DOKill();
					child.localScale = Vector3.one;
					child.DOPunchScale(Vector3.one * 0.2f, GameSpeed.Duration(0.3f), 2).OnKill(delegate
					{
						child.localScale = Vector3.one;
					});
				}
				UnityEngine.Object.Destroy(trail, 5f);
				stopwatch.Stop();
				UnityEngine.Object.Destroy(base.gameObject);
			})
			.SetDelay(GameSpeed.Duration(0.8f));
	}

	private void $Rougamo_OnDestroy()
	{
		FightUI.SelectedCard.Remove(this);
		ClearEvent();
	}
}
