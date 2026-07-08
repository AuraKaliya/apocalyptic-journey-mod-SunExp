using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using AllScripts;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Data.Save;
using Fight.ActionCommand;
using Fight.ObjTarget;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Mirror;
using Network.Query;
using Rougamo;
using Rougamo.Context;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Witch.UI;
using Witch.UI.Window;
using XLua;
using ZLinq;
using ZLinq.Linq;

[IgnoreMo(MoTypes = new Type[] { typeof(Modifiable) })]
[ReflectionUse]
[LuaCallCSharp(GenFlag.No)]
public class ScriptExecutor : IScriptExecutor
{
	public class DiceWrapper
	{
		public Action<Dice.State> OnRoll = delegate
		{
		};

		internal Dice dice;

		public Dice.State result;

		public DiceWrapper(Dice dice)
		{
			this.dice = dice;
		}

		[DebuggerStepThrough]
		public Dice.State InternalRoll(int? Target = null)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(DiceWrapper);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DiceWrapper).TypeHandle);
			methodContext.Arguments = new object[1] { Target };
			try
			{
				modifiable.OnEntry(methodContext);
				Dice.State state = $Rougamo_InternalRoll(Target);
				modifiable.OnSuccess(methodContext);
				return state;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public Dice.State Roll(int? Target = null)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(DiceWrapper);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DiceWrapper).TypeHandle);
			methodContext.Arguments = new object[1] { Target };
			try
			{
				modifiable.OnEntry(methodContext);
				Dice.State state = $Rougamo_Roll(Target);
				modifiable.OnSuccess(methodContext);
				return state;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public Dice WithRange(int min, int max)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.Target = this;
			methodContext.TargetType = typeof(DiceWrapper);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(DiceWrapper).TypeHandle);
			methodContext.Arguments = new object[2] { min, max };
			try
			{
				modifiable.OnEntry(methodContext);
				Dice dice = $Rougamo_WithRange(min, max);
				modifiable.OnSuccess(methodContext);
				return dice;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		private Dice.State $Rougamo_InternalRoll(int? Target = null)
		{
			Dice.State state = dice.Roll();
			if (FightPlayer.Instance == null)
			{
				return state;
			}
			DiceIcon diceIcon = FightPlayer.Instance.diceIcon;
			diceIcon.rangeValue = (dice.Range.min, dice.Range.max);
			diceIcon.value = state.Value;
			diceIcon.bonus = state.Bonus;
			if (Target.HasValue)
			{
				diceIcon.Target = Target.ToString();
			}
			if (dice.Type == "Check")
			{
				diceIcon.Roll("检定骰");
			}
			return state;
		}

		private Dice.State $Rougamo_Roll(int? Target = null)
		{
			if (FightPlayer.Instance?.Status != null && dice.Type == "Check")
			{
				Singleton<EventCenter>.Instance.EventTrigger("OnDiceCheck" + FightPlayer.Instance.Status.InstanceId);
			}
			Action<Dice.State> obj = (Action<Dice.State>)Delegate.Combine(OnRoll, (Action<Dice.State>)delegate
			{
			});
			if (dice.Type == "Default")
			{
				result = dice.Roll();
			}
			else
			{
				result = InternalRoll(Target);
			}
			obj?.Invoke(result);
			OnRoll = delegate
			{
			};
			return result;
		}

		private Dice $Rougamo_WithRange(int min, int max)
		{
			return dice.WithRange(min, max);
		}
	}

	[LuaCallCSharp(GenFlag.No)]
	public static class PlayerInfo
	{
		public static int TrueCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_TrueCount();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_TrueCount(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int MaxHp
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_MaxHp();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_MaxHp(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Hp
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Hp();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Hp(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Power
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Power();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Power(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int MaxPower
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_MaxPower();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_MaxPower(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int RelicCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_RelicCount();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int CardTopCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
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
		}

		public static int enemylevel
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_enemylevel();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int enemyCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_enemyCount();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int CardTotalCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_CardTotalCount();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int BlessingCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_BlessingCount();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Money
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Money();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Money(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int MoneyMultiplier
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_MoneyMultiplier();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_MoneyMultiplier(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Level
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Level();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static IDataConfig LastCard
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					IDataConfig result = $Rougamo_get_LastCard();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static FightType Win
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					FightType result = $Rougamo_get_Win();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static FightType Loss
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					FightType result = $Rougamo_get_Loss();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static FightType Enemy
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					FightType result = $Rougamo_get_Enemy();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static FightType Pattern
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					FightType result = $Rougamo_get_Pattern();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static FightType Player
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					FightType result = $Rougamo_get_Player();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static FightType Escape
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					FightType result = $Rougamo_get_Escape();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static List<IDataConfig> CardList
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					List<IDataConfig> result = $Rougamo_get_CardList();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static List<IDataConfig> UnCardList
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					List<IDataConfig> result = $Rougamo_get_UnCardList();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static List<IDataConfig> BlessingList
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					List<IDataConfig> result = $Rougamo_get_BlessingList();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static List<IDataConfig> RelicList
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					List<IDataConfig> result = $Rougamo_get_RelicList();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int PlayerCount
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_PlayerCount();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Reward
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Reward();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Reward(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Strength
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Strength();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Strength(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int DefaultRoll
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_DefaultRoll();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Lucky
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Lucky();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Lucky(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Wisdom
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Wisdom();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Wisdom(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int Perceive
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_Perceive();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_Perceive(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int TempStrength
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_TempStrength();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_TempStrength(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int TempLucky
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_TempLucky();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_TempLucky(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int TempWisdom
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_TempWisdom();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_TempWisdom(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static Dictionary<string, int> SkillTime
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					Dictionary<string, int> result = $Rougamo_get_SkillTime();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_SkillTime(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static List<string> ChooseVars
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					List<string> result = $Rougamo_get_ChooseVars();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_ChooseVars(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int MainVarUpperBound
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_MainVarUpperBound();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_MainVarUpperBound(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int SecondaryVarUpperBound
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_SecondaryVarUpperBound();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_SecondaryVarUpperBound(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int OtherVarUpperBound
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_OtherVarUpperBound();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_OtherVarUpperBound(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static int TempPerceive
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					int result = $Rougamo_get_TempPerceive();
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
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[1] { value };
				try
				{
					modifiable.OnEntry(methodContext);
					$Rougamo_set_TempPerceive(value);
					modifiable.OnSuccess(methodContext);
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static string PlayerName
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					string result = $Rougamo_get_PlayerName();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		public static Dictionary<string, string> SpecialVars
		{
			[DebuggerStepThrough]
			get
			{
				Modifiable modifiable = new Modifiable();
				MethodContext methodContext = RougamoPool<MethodContext>.Get();
				methodContext.TargetType = typeof(PlayerInfo);
				methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
				methodContext.Arguments = new object[0];
				try
				{
					modifiable.OnEntry(methodContext);
					Dictionary<string, string> result = $Rougamo_get_SpecialVars();
					modifiable.OnSuccess(methodContext);
					return result;
				}
				finally
				{
					RougamoPool<MethodContext>.Return(methodContext);
				}
			}
		}

		[DebuggerStepThrough]
		public static string GetTagDiff()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				string result = $Rougamo_GetTagDiff();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ChangeEventSubtip(string text)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { text };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ChangeEventSubtip(text);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ChangeType(FightType type)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { type };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ChangeType(type);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void WinTheFight()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_WinTheFight();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void GiveWin()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_GiveWin();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void CopyCard(string instanceId)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { instanceId };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_CopyCard(instanceId);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void CopyBless(string instanceId)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { instanceId };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_CopyBless(instanceId);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void CopyRelic(string instanceId)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { instanceId };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_CopyRelic(instanceId);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AddCard(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AddCard(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RemoveCard(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RemoveCard(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AddRelic(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AddRelic(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RemoveRelic(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RemoveRelic(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AddBless(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AddBless(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RemoveBless(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RemoveBless(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void DelayAddCard(string id, int delayFrames = 2)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { id, delayFrames };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_DelayAddCard(id, delayFrames);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void DelayAddRelic(string id, int delayFrames = 2)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { id, delayFrames };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_DelayAddRelic(id, delayFrames);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void DelayAddBless(string id, int delayFrames = 2)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { id, delayFrames };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_DelayAddBless(id, delayFrames);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomAddBless(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomAddBless(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomAddRelic(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomAddRelic(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void Goodbless(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_Goodbless(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomAddCard(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomAddCard(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomrelicByRarity(string rarity)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { rarity };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomrelicByRarity(rarity);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomcardByRarity(string rarity)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { rarity };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomcardByRarity(rarity);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		private static void DelayGive(Action action, int delayFrames)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { action, delayFrames };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_DelayGive(action, delayFrames);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomAddCardByDeck(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomAddCardByDeck(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomRemoveCard(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomRemoveCard(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomRemoveBless(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomRemoveBless(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void RandomRemoveRelic(string count)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { count };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_RandomRemoveRelic(count);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void StartLevel(string type, string id2 = null)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { type, id2 };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_StartLevel(type, id2);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ShowReward()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ShowReward();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void SetGameVar(string key, string value)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { key, value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_SetGameVar(key, value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static string GetGameVar(string key)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { key };
			try
			{
				modifiable.OnEntry(methodContext);
				string result = $Rougamo_GetGameVar(key);
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ContinueEvent(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ContinueEvent(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void GameOver()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_GameOver();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ShowDialogue(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ShowDialogue(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AddEvent(string name, Action action, object obj)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[3] { name, action, obj };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AddEvent(name, action, obj);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void EndDialogue()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_EndDialogue();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void HideDialogue(bool flag = true)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { flag };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_HideDialogue(flag);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AddCardByData(DataConfig data)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { data };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AddCardByData(data);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ShowOptions(params (string text, Action action)[] options)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { options };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ShowOptions(options);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void EventTrigger(string name)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { name };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_EventTrigger(name);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static string RandomSelect(params string[] lists)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { lists };
			try
			{
				modifiable.OnEntry(methodContext);
				string result = $Rougamo_RandomSelect(lists);
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void EventTryChangeMap()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_EventTryChangeMap();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AnnounceEventDone()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AnnounceEventDone();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static GameRuntimeData Getsave()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				GameRuntimeData result = $Rougamo_Getsave();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void AddItem(string itemId, string type)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { itemId, type };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_AddItem(itemId, type);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void EndEvent()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_EndEvent();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void LockChoice(string index)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { index };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_LockChoice(index);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static IDictionary<string, string> GetCareerData()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				IDictionary<string, string> result = $Rougamo_GetCareerData();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static DataConfig GetCareer()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				DataConfig result = $Rougamo_GetCareer();
				modifiable.OnSuccess(methodContext);
				return result;
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void UpdateAch(string id, int progress)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[2] { id, progress };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_UpdateAch(id, progress);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ChangeSelected(string value)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ChangeSelected(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ChangeAllVars(string value)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { value };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ChangeAllVars(value);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void UnlockItem(string id)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { id };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_UnlockItem(id);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ShowCaption(string text)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[1] { text };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ShowCaption(text);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void QuitAndDeleteSave()
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[0];
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_QuitAndDeleteSave();
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		[DebuggerStepThrough]
		public static void ShowItemShowUI(string iconPath, string title, string description, string tips = null)
		{
			Modifiable modifiable = new Modifiable();
			MethodContext methodContext = RougamoPool<MethodContext>.Get();
			methodContext.TargetType = typeof(PlayerInfo);
			methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(PlayerInfo).TypeHandle);
			methodContext.Arguments = new object[4] { iconPath, title, description, tips };
			try
			{
				modifiable.OnEntry(methodContext);
				$Rougamo_ShowItemShowUI(iconPath, title, description, tips);
				modifiable.OnSuccess(methodContext);
			}
			finally
			{
				RougamoPool<MethodContext>.Return(methodContext);
			}
		}

		private static string $Rougamo_GetTagDiff()
		{
			return GameSaveManager.GetHardLevel().ToString();
		}

		[SpecialName]
		private static int $Rougamo_get_TrueCount()
		{
			return Singleton<GameRuntimeData>.Instance.Truth;
		}

		[SpecialName]
		private static void $Rougamo_set_TrueCount(int value)
		{
			Singleton<GameRuntimeData>.Instance.Truth = value;
			UIManager.Instance.GetUI<TopBarUI>("TopBarUI").ChangeTrue();
		}

		[SpecialName]
		private static int $Rougamo_get_MaxHp()
		{
			return RoleTable.Instance.MaxSan;
		}

		[SpecialName]
		private static void $Rougamo_set_MaxHp(int value)
		{
			RoleTable.Instance.MaxSan = value;
		}

		[SpecialName]
		private static int $Rougamo_get_Hp()
		{
			return RoleTable.Instance.San;
		}

		[SpecialName]
		private static void $Rougamo_set_Hp(int value)
		{
			RoleTable.Instance.San = value;
		}

		[SpecialName]
		private static int $Rougamo_get_Power()
		{
			return FightPlayer.Instance?.CurPowerCount ?? 0;
		}

		[SpecialName]
		private static void $Rougamo_set_Power(int value)
		{
			FightPlayer instance = FightPlayer.Instance;
			if (instance != null)
			{
				instance.CurPowerCount = value;
			}
		}

		[SpecialName]
		private static int $Rougamo_get_MaxPower()
		{
			return FightPlayer.Instance?.MaxPowerCount ?? 0;
		}

		[SpecialName]
		private static void $Rougamo_set_MaxPower(int value)
		{
			FightPlayer instance = FightPlayer.Instance;
			if (instance != null)
			{
				instance.MaxPowerCount = value;
			}
		}

		[SpecialName]
		private static int $Rougamo_get_RelicCount()
		{
			if (RoleTable.Instance == null)
			{
				return 0;
			}
			return RoleTable.Instance.relicList.Count;
		}

		[SpecialName]
		private static int $Rougamo_get_CardTopCount()
		{
			return UIManager.Instance.GetUI<FightUI>("FightUI")?.CardTopCount ?? 0;
		}

		[SpecialName]
		private static int $Rougamo_get_enemylevel()
		{
			return EnemyManager.SettlementMultiplier;
		}

		[SpecialName]
		private static int $Rougamo_get_enemyCount()
		{
			return EnemyManager.enemyCount;
		}

		[SpecialName]
		private static int $Rougamo_get_CardTotalCount()
		{
			return RoleTable.Instance.cardList.Count;
		}

		[SpecialName]
		private static int $Rougamo_get_BlessingCount()
		{
			return RoleTable.Instance.blessingConfigs.Count;
		}

		[SpecialName]
		private static int $Rougamo_get_Money()
		{
			return RoleTable.Instance.Money;
		}

		[SpecialName]
		private static void $Rougamo_set_Money(int value)
		{
			RoleTable.Instance.Money = value;
		}

		[SpecialName]
		private static int $Rougamo_get_MoneyMultiplier()
		{
			return RoleTable.Instance.MoneyMultiplier;
		}

		[SpecialName]
		private static void $Rougamo_set_MoneyMultiplier(int value)
		{
			RoleTable.Instance.MoneyMultiplier = value;
		}

		[SpecialName]
		private static int $Rougamo_get_Level()
		{
			return MapManager.Instance.Level;
		}

		[SpecialName]
		private static IDataConfig $Rougamo_get_LastCard()
		{
			return FightUI.LastCard;
		}

		[SpecialName]
		private static FightType $Rougamo_get_Win()
		{
			return FightType.Win;
		}

		[SpecialName]
		private static FightType $Rougamo_get_Loss()
		{
			return FightType.Loss;
		}

		[SpecialName]
		private static FightType $Rougamo_get_Enemy()
		{
			return FightType.Enemy;
		}

		[SpecialName]
		private static FightType $Rougamo_get_Pattern()
		{
			return FightType.Partner;
		}

		[SpecialName]
		private static FightType $Rougamo_get_Player()
		{
			return FightType.Player;
		}

		[SpecialName]
		private static FightType $Rougamo_get_Escape()
		{
			return FightType.Escape;
		}

		[SpecialName]
		private static List<IDataConfig> $Rougamo_get_CardList()
		{
			return RoleTable.Instance.cardList.ToList().Cast<IDataConfig>().ToList();
		}

		[SpecialName]
		private static List<IDataConfig> $Rougamo_get_UnCardList()
		{
			return RoleTable.Instance.UnCardList.ToList().Cast<IDataConfig>().ToList();
		}

		[SpecialName]
		private static List<IDataConfig> $Rougamo_get_BlessingList()
		{
			return RoleTable.Instance.blessingConfigs.ToList().Cast<IDataConfig>().ToList();
		}

		[SpecialName]
		private static List<IDataConfig> $Rougamo_get_RelicList()
		{
			return RoleTable.Instance.relicList.ToList().Cast<IDataConfig>().ToList();
		}

		private static void $Rougamo_ChangeEventSubtip(string text)
		{
			if (UIManager.Instance.GetUI<EventUI>("EventUI") != null)
			{
				UIManager.Instance.GetUI<EventUI>("EventUI").ChangeSubtip(text);
			}
		}

		[SpecialName]
		private static int $Rougamo_get_PlayerCount()
		{
			return GameEntryUI.playerCount;
		}

		[SpecialName]
		private static int $Rougamo_get_Reward()
		{
			return RoleTable.Instance.Reward;
		}

		[SpecialName]
		private static void $Rougamo_set_Reward(int value)
		{
			RoleTable.Instance.Reward = value;
		}

		[SpecialName]
		private static int $Rougamo_get_Strength()
		{
			return RoleTable.Instance.VarsMap["Strength"];
		}

		[SpecialName]
		private static void $Rougamo_set_Strength(int value)
		{
			RoleTable.Instance.VarsMap["Strength"] = value;
			RoleTable.Instance.VarsCheck("Strength");
		}

		[SpecialName]
		private static int $Rougamo_get_DefaultRoll()
		{
			return MapManager.Instance.NowDice.WithRange(0, 99).Roll().Value;
		}

		[SpecialName]
		private static int $Rougamo_get_Lucky()
		{
			return RoleTable.Instance.VarsMap["Lucky"];
		}

		[SpecialName]
		private static void $Rougamo_set_Lucky(int value)
		{
			RoleTable.Instance.VarsMap["Lucky"] = value;
			RoleTable.Instance.VarsCheck("Lucky");
		}

		[SpecialName]
		private static int $Rougamo_get_Wisdom()
		{
			return RoleTable.Instance.VarsMap["Wisdom"];
		}

		[SpecialName]
		private static void $Rougamo_set_Wisdom(int value)
		{
			RoleTable.Instance.VarsMap["Wisdom"] = value;
			RoleTable.Instance.VarsCheck("Wisdom");
		}

		[SpecialName]
		private static int $Rougamo_get_Perceive()
		{
			return RoleTable.Instance.VarsMap["Perceive"];
		}

		[SpecialName]
		private static void $Rougamo_set_Perceive(int value)
		{
			RoleTable.Instance.VarsMap["Perceive"] = value;
			RoleTable.Instance.VarsCheck("Perceive");
		}

		[SpecialName]
		private static int $Rougamo_get_TempStrength()
		{
			return FightManager.Instance?.TempVarsMap["Strength"] ?? 0;
		}

		[SpecialName]
		private static void $Rougamo_set_TempStrength(int value)
		{
			if (!(FightManager.Instance == null))
			{
				FightManager.Instance.TempVarsMap["Strength"] = value;
				UIManager.Instance.GetUI<TopBarUI>("TopBarUI")?.ChangeVar();
			}
		}

		[SpecialName]
		private static int $Rougamo_get_TempLucky()
		{
			return FightManager.Instance?.TempVarsMap["Lucky"] ?? 0;
		}

		[SpecialName]
		private static void $Rougamo_set_TempLucky(int value)
		{
			if (!(FightManager.Instance == null))
			{
				FightManager.Instance.TempVarsMap["Lucky"] = value;
				UIManager.Instance.GetUI<TopBarUI>("TopBarUI")?.ChangeVar();
			}
		}

		[SpecialName]
		private static int $Rougamo_get_TempWisdom()
		{
			return FightManager.Instance?.TempVarsMap["Wisdom"] ?? 0;
		}

		[SpecialName]
		private static void $Rougamo_set_TempWisdom(int value)
		{
			if (!(FightManager.Instance == null))
			{
				FightManager.Instance.TempVarsMap["Wisdom"] = value;
				UIManager.Instance.GetUI<TopBarUI>("TopBarUI")?.ChangeVar();
			}
		}

		[SpecialName]
		private static Dictionary<string, int> $Rougamo_get_SkillTime()
		{
			if (RoleTable.Instance != null)
			{
				return RoleTable.Instance.SkillTime;
			}
			return new Dictionary<string, int>();
		}

		[SpecialName]
		private static void $Rougamo_set_SkillTime(Dictionary<string, int> value)
		{
			RoleTable.Instance.SkillTime = value;
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").UpdateSkill();
			}
		}

		[SpecialName]
		private static List<string> $Rougamo_get_ChooseVars()
		{
			return RoleTable.Instance?.ChooseVars;
		}

		[SpecialName]
		private static void $Rougamo_set_ChooseVars(List<string> value)
		{
			if (RoleTable.Instance != null)
			{
				RoleTable.Instance.ChooseVars = value;
			}
		}

		[SpecialName]
		private static int $Rougamo_get_MainVarUpperBound()
		{
			if (RoleTable.Instance == null)
			{
				return 0;
			}
			return RoleTable.Instance.MainVarUpperBound;
		}

		[SpecialName]
		private static void $Rougamo_set_MainVarUpperBound(int value)
		{
			RoleTable.Instance.MainVarUpperBound = value;
		}

		[SpecialName]
		private static int $Rougamo_get_SecondaryVarUpperBound()
		{
			if (RoleTable.Instance == null)
			{
				return 0;
			}
			return RoleTable.Instance.SecondaryVarUpperBound;
		}

		[SpecialName]
		private static void $Rougamo_set_SecondaryVarUpperBound(int value)
		{
			RoleTable.Instance.SecondaryVarUpperBound = value;
		}

		[SpecialName]
		private static int $Rougamo_get_OtherVarUpperBound()
		{
			if (RoleTable.Instance == null)
			{
				return 0;
			}
			return RoleTable.Instance.OtherVarUpperBound;
		}

		[SpecialName]
		private static void $Rougamo_set_OtherVarUpperBound(int value)
		{
			RoleTable.Instance.OtherVarUpperBound = value;
		}

		[SpecialName]
		private static int $Rougamo_get_TempPerceive()
		{
			return FightManager.Instance?.TempVarsMap["Perceive"] ?? 0;
		}

		[SpecialName]
		private static void $Rougamo_set_TempPerceive(int value)
		{
			if (!(FightManager.Instance == null))
			{
				FightManager.Instance.TempVarsMap["Perceive"] = value;
				UIManager.Instance.GetUI<TopBarUI>("TopBarUI")?.ChangeVar();
			}
		}

		private static void $Rougamo_ChangeType(FightType type)
		{
			if (!(FightManager.Instance == null))
			{
				FightManager.Instance.CmdChangeType(type);
			}
		}

		private static void $Rougamo_WinTheFight()
		{
			ChangeType(Win);
		}

		[SpecialName]
		private static string $Rougamo_get_PlayerName()
		{
			return Singleton<GameConfigManager>.Instance.PlayerName;
		}

		[SpecialName]
		private static Dictionary<string, string> $Rougamo_get_SpecialVars()
		{
			return RoleTable.Instance?.SpecialVarMap ?? null;
		}

		private static void $Rougamo_GiveWin()
		{
			Commands.Log("", Commands.give("win", "1"));
		}

		private static void $Rougamo_CopyCard(string instanceId)
		{
			Commands.Log("", Commands.copy("card", instanceId));
		}

		private static void $Rougamo_CopyBless(string instanceId)
		{
			Commands.Log("", Commands.copy("bless", instanceId));
		}

		private static void $Rougamo_CopyRelic(string instanceId)
		{
			Commands.Log("", Commands.copy("relic", instanceId));
		}

		private static void $Rougamo_AddCard(string id)
		{
			Commands.Log("", Commands.give("card", id));
		}

		private static void $Rougamo_RemoveCard(string id)
		{
			Commands.Log("", Commands.remove("card", id));
		}

		private static void $Rougamo_AddRelic(string id)
		{
			Commands.Log("", Commands.give("relic", id));
		}

		private static void $Rougamo_RemoveRelic(string id)
		{
			Commands.Log("", Commands.remove("relic", id));
		}

		private static void $Rougamo_AddBless(string id)
		{
			Commands.Log("", Commands.give("bless", id));
		}

		private static void $Rougamo_RemoveBless(string id)
		{
			Commands.Log("", Commands.remove("bless", id));
		}

		private static void $Rougamo_DelayAddCard(string id, int delayFrames = 2)
		{
			DelayGive(delegate
			{
				AddCard(id);
			}, delayFrames);
		}

		private static void $Rougamo_DelayAddRelic(string id, int delayFrames = 2)
		{
			DelayGive(delegate
			{
				AddRelic(id);
			}, delayFrames);
		}

		private static void $Rougamo_DelayAddBless(string id, int delayFrames = 2)
		{
			DelayGive(delegate
			{
				AddBless(id);
			}, delayFrames);
		}

		private static void $Rougamo_RandomAddBless(string count)
		{
			Commands.Log("", Commands.give("randombless", count));
		}

		private static void $Rougamo_RandomAddRelic(string count)
		{
			Commands.Log("", Commands.give("randomrelic", count));
		}

		private static void $Rougamo_Goodbless(string count)
		{
			Commands.Log("", Commands.give("goodbless", count));
		}

		private static void $Rougamo_RandomAddCard(string count)
		{
			Commands.Log("", Commands.give("randomcard", count));
		}

		private static void $Rougamo_RandomrelicByRarity(string rarity)
		{
			Commands.Log("", Commands.give("randomrelicByRarity", rarity));
		}

		private static void $Rougamo_RandomcardByRarity(string rarity)
		{
			Commands.Log("", Commands.give("randomcardByRarity", rarity));
		}

		private static void $Rougamo_DelayGive(Action action, int delayFrames)
		{
			UniTask.DelayFrame(Math.Max(1, delayFrames)).ContinueWith(action).Forget();
		}

		private static void $Rougamo_RandomAddCardByDeck(string count)
		{
			Commands.Log("", Commands.give("randomcardbydeck", count));
		}

		private static void $Rougamo_RandomRemoveCard(string count)
		{
			Commands.Log("", Commands.remove("randomcard", count));
		}

		private static void $Rougamo_RandomRemoveBless(string count)
		{
			Commands.Log("", Commands.remove("randombless", count));
		}

		private static void $Rougamo_RandomRemoveRelic(string count)
		{
			Commands.Log("", Commands.remove("randomrelic", count));
		}

		private static void $Rougamo_StartLevel(string type, string id2 = null)
		{
			Commands.Log("", Commands.load(type, id2));
		}

		private static void $Rougamo_ShowReward()
		{
			Commands.ShowReward("1");
		}

		private static void $Rougamo_SetGameVar(string key, string value)
		{
			if (key == GameVar.Branch.ToString() && value != GameSaveManager.GetValue<string>(key))
			{
				AchievementRuntimeService.Instance.RecordBranchChosen();
			}
			if (PlayerManager.Instance != null && NetworkClient.isConnected)
			{
				PlayerManager.Instance.SetGameVar(key, value);
			}
			else
			{
				GameSaveManager.SetValue(key, value);
			}
		}

		private static string $Rougamo_GetGameVar(string key)
		{
			return GameSaveManager.GetValue<string>(key) ?? "0";
		}

		private static void $Rougamo_ContinueEvent(string id)
		{
			UIManager.Instance.GetUI<EventUI>("EventUI").ContinueEvent(id);
		}

		private static void $Rougamo_GameOver()
		{
			UIManager.Instance.ShowUIAsync<AcknowledgmentsUI>("AcknowledgmentsUI").Forget();
		}

		private static void $Rougamo_ShowDialogue(string id)
		{
			Singleton<DialogueManager>.Instance.ShowDialogue(id);
		}

		private static void $Rougamo_AddEvent(string name, Action action, object obj)
		{
			Singleton<EventCenter>.Instance.AddEventListener(name, action, obj);
		}

		private static void $Rougamo_EndDialogue()
		{
			Singleton<DialogueManager>.Instance.EndDialogue();
		}

		private static void $Rougamo_HideDialogue(bool flag = true)
		{
			Singleton<DialogueManager>.Instance.HideDialogue(flag);
		}

		private static void $Rougamo_AddCardByData(DataConfig data)
		{
			UnityEngine.Debug.Log("添加的物品名字是" + data.data.Localize("Name"));
			if (RoleTable.Instance != null)
			{
				RoleTable.Instance.cardList.Add(data);
			}
		}

		private static void $Rougamo_ShowOptions(params (string text, Action action)[] options)
		{
			Singleton<DialogueManager>.Instance.ShowOptions(options);
		}

		private static void $Rougamo_EventTrigger(string name)
		{
			Singleton<EventCenter>.Instance?.EventTrigger(name);
		}

		private static string $Rougamo_RandomSelect(params string[] lists)
		{
			return lists[UnityEngine.Random.Range(0, lists.Length)];
		}

		private static void $Rougamo_EventTryChangeMap()
		{
			if (!(UIManager.Instance.GetUI<EventUI>("EventUI") == null))
			{
				UIManager.Instance.GetUI<EventUI>("EventUI").TryChangeMap();
			}
		}

		private static void $Rougamo_AnnounceEventDone()
		{
			if (!(UIManager.Instance.GetUI<EventUI>("EventUI") == null))
			{
				UIManager.Instance.GetUI<EventUI>("EventUI").AnnounceEventDone();
			}
		}

		private static GameRuntimeData $Rougamo_Getsave()
		{
			return Singleton<GameRuntimeData>.Instance;
		}

		private static void $Rougamo_AddItem(string itemId, string type)
		{
			Singleton<GameRuntimeData>.Instance.AddItem(itemId, type);
		}

		private static void $Rougamo_EndEvent()
		{
			if (!(UIManager.Instance.GetUI<EventUI>("EventUI") == null))
			{
				UIManager.Instance.GetUI<EventUI>("EventUI").EndEvent();
			}
		}

		private static void $Rougamo_LockChoice(string index)
		{
			if (!(UIManager.Instance.GetUI<EventUI>("EventUI") == null))
			{
				UIManager.Instance.GetUI<EventUI>("EventUI").LockChoice(index);
			}
		}

		private static IDictionary<string, string> $Rougamo_GetCareerData()
		{
			return RoleTable.Instance.Career.data;
		}

		private static DataConfig $Rougamo_GetCareer()
		{
			if (RoleTable.Instance == null)
			{
				return null;
			}
			return RoleTable.Instance.Career;
		}

		private static void $Rougamo_UpdateAch(string id, int progress)
		{
			if (AchievementRuntimeService.Instance != null)
			{
				AchievementRuntimeService.Instance.UpdateProgress(id, progress);
			}
		}

		private static void $Rougamo_ChangeSelected(string value)
		{
			foreach (string item in new List<string>(RoleTable.Instance.ChooseVars))
			{
				RoleTable.Instance.VarsMap[item] += value.ToInt();
				RoleTable.Instance.VarsCheck(item);
			}
		}

		private static void $Rougamo_ChangeAllVars(string value)
		{
			Strength += value.ToInt();
			Lucky += value.ToInt();
			Wisdom += value.ToInt();
			Perceive += value.ToInt();
		}

		private static void $Rougamo_UnlockItem(string id)
		{
			Commands.unlock(id);
		}

		private static void $Rougamo_ShowCaption(string text)
		{
			UIManager.Instance.ShowTip(text);
		}

		private static void $Rougamo_QuitAndDeleteSave()
		{
			UIManager.Instance.CloseUI("DialogueUI");
			GameApp.Instance.CloseHouse();
			UIManager.Instance.ShowUI<MainMenuUI>("MainMenuUI");
		}

		private static void $Rougamo_ShowItemShowUI(string iconPath, string title, string description, string tips = null)
		{
			UIManager.Instance.ShowItemShowUI(iconPath, title, description, tips);
		}
	}

	private static bool deckUiSelectBusy;

	public Dictionary<IStatusManager, Dictionary<string, int>> GetStatus = new Dictionary<IStatusManager, Dictionary<string, int>>();

	public List<PropertyChangedEventHandler> handlers = new List<PropertyChangedEventHandler>();

	private static Dictionary<string, Action<ScriptExecutor>> AllScriptDict;

	public static LuaEnv luaEnv;

	public static LuaTable luaTable;

	private LuaTable scriptExecutorEnv;

	private static readonly List<Func<ScriptExecutor, Delegate>> DynamicMethodDelegates;

	private static ScriptOptions options;

	public IStatusManager status { get; set; }

	public IStatusManager Self { get; set; }

	public List<IStatusManager> Object { get; set; } = new List<IStatusManager>();

	public IDataConfig dataConfig { get; set; }

	public IStatusManager Target { get; set; }

	public Dictionary<string, Delegate> ScriptDict { get; set; } = new Dictionary<string, Delegate>();

	public string Id => Vars["Id"];

	public DiceWrapper CheckDice
	{
		get
		{
			if (!(FightManager.Instance != null))
			{
				return null;
			}
			return FightManager.Instance.CheckDice;
		}
	}

	public DiceWrapper DefaultDice
	{
		get
		{
			if (FightManager.Instance?.DefaultDice != null)
			{
				return FightManager.Instance.DefaultDice;
			}
			return new DiceWrapper((MapManager.Instance?.NowDice ?? Dice.Default).WithType("Default"));
		}
	}

	public IDictionary<string, string> Vars => dataConfig.Vars;

	public List<CardItem> HandCard => FightUI.cardItemList ?? new List<CardItem>();

	public List<CardItem> WaitCard => FightUI.WaitCard ?? new List<CardItem>();

	public List<DataConfig> DeckCard => FightCardManager.Instance?.cardList.ToList() ?? new List<DataConfig>();

	public List<DataConfig> UsedCard => FightCardManager.Instance?.usedCardList.ToList() ?? new List<DataConfig>();

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void SetHp(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_SetHp(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void SetMaxHp(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_SetMaxHp(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeHp(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeHp(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void PureChangeHp(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_PureChangeHp(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeSkill(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeSkill(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddCardById(string id)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { id };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					id = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_AddCardById(id);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddCardToDeckById(string Id, bool toUsed = true)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { Id, toUsed };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					Id = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					toUsed = ((arguments[1] != null) ? ((bool)arguments[1]) : default(bool));
				}
				$Rougamo_AddCardToDeckById(Id, toUsed);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddFakeCard(bool toUsed = true)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { toUsed };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					toUsed = ((arguments[0] != null) ? ((bool)arguments[0]) : default(bool));
				}
				$Rougamo_AddFakeCard(toUsed);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	public void AddCardToFightManager(DataConfig dataConfig, bool toUsed = true)
	{
		if (Self == null || Self.fatherObject == null || FightCardManager.Instance == null)
		{
			return;
		}
		if (toUsed)
		{
			FightCardManager.Instance.usedCardList.Add(dataConfig);
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
				{
					cardData = dataConfig,
					isBurning = false
				}, toThrow: true, needInit: true);
			}
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家向弃牌堆置入卡牌 <color=purple>" + dataConfig.data.Localize("Name") + "</color>");
		}
		else
		{
			FightCardManager.Instance.cardList.Add(dataConfig);
			int value = DefaultDice.WithRange(0, FightCardManager.Instance.cardList.Count - 1).Roll().Value;
			ObservableCollection<DataConfig> cardList;
			int index = (cardList = FightCardManager.Instance.cardList).Count - 1;
			ObservableCollection<DataConfig> cardList2 = FightCardManager.Instance.cardList;
			int index2 = value;
			DataConfig dataConfig2 = FightCardManager.Instance.cardList[value];
			ObservableCollection<DataConfig> cardList3 = FightCardManager.Instance.cardList;
			DataConfig dataConfig3 = cardList3[cardList3.Count - 1];
			DataConfig dataConfig4 = (cardList[index] = dataConfig2);
			dataConfig4 = (cardList2[index2] = dataConfig3);
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
				{
					cardData = dataConfig,
					isBurning = false
				}, toThrow: false, needInit: true);
			}
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家向抽牌堆置入卡牌 <color=purple>" + dataConfig.data.Localize("Name") + "</color>");
		}
		FightCardManager.Instance.CardTagCheck(dataConfig);
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeMaxHp(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeMaxHp(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddBuff(string buffId, string level)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { buffId, level };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					buffId = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					level = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_AddBuff(buffId, level);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void RemoveBuff(string buffId)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { buffId };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					buffId = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_RemoveBuff(buffId);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void RunImmediately(string buffId, string eventName)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { buffId, eventName };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					buffId = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					eventName = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_RunImmediately(buffId, eventName);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void Resurrection(string value)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { value };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					value = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_Resurrection(value);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeDefence(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeDefence(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void SetPower(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_SetPower(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void DrawCount(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_DrawCount(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangePower(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangePower(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeMaxPower(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeMaxPower(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeRound()
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				$Rougamo_ChangeRound();
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void DoAction(string index)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { index };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					index = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_DoAction(index);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void RemoveBadBuff(string val, string good = "false")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { val, good };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					good = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_RemoveBadBuff(val, good);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void RemoveAllBadBuff(string obj)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { obj };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					obj = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_RemoveAllBadBuff(obj);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void RemoveAllBuff()
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				$Rougamo_RemoveAllBuff();
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddCardByCardList(string count, string tag = "all")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { count, tag };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					count = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					tag = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_AddCardByCardList(count, tag);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddCardByUsedCardList(string count, string tag = "all")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { count, tag };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					count = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					tag = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_AddCardByUsedCardList(count, tag);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void RandomAddCard(string id)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { id };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					id = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_RandomAddCard(id);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeMoney(string val, string changeMax = "false")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { val, changeMax };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					changeMax = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_ChangeMoney(val, changeMax);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddAction(string count)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { count };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					count = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_AddAction(count);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ShuffleDeck()
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				$Rougamo_ShuffleDeck();
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ShuffleHand()
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[0];
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				$Rougamo_ShuffleHand();
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeCardTop(string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeCardTop(val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void GetCardByTag(string count, string tag = "all")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { count, tag };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					count = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					tag = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_GetCardByTag(count, tag);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddCard(string id)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { id };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					id = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_AddCard(id);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void AddCardByData(string Id, string AddTag = "")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { Id, AddTag };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					Id = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					AddTag = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_AddCardByData(Id, AddTag);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeCareer(string Id)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { Id };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					Id = ((arguments[0] != null) ? ((string)arguments[0]) : null);
				}
				$Rougamo_ChangeCareer(Id);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeSummon(bool Isshow)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[1] { Isshow };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					Isshow = ((arguments[0] != null) ? ((bool)arguments[0]) : default(bool));
				}
				$Rougamo_ChangeSummon(Isshow);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObjectNative))]
	[DebuggerStepThrough]
	public void AddEvent(string eventName, Action script)
	{
		ForEachObjectNative forEachObjectNative = default(ForEachObjectNative);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObjectNative };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { eventName, script };
		try
		{
			forEachObjectNative.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					eventName = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					script = ((arguments[1] != null) ? ((Action)arguments[1]) : null);
				}
				$Rougamo_AddEvent(eventName, script);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObjectNative))]
	[DebuggerStepThrough]
	public void AddTempEvent(string eventName, Action script)
	{
		ForEachObjectNative forEachObjectNative = default(ForEachObjectNative);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObjectNative };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { eventName, script };
		try
		{
			forEachObjectNative.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					eventName = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					script = ((arguments[1] != null) ? ((Action)arguments[1]) : null);
				}
				$Rougamo_AddTempEvent(eventName, script);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObjectNative))]
	[DebuggerStepThrough]
	public void AddEvent<T>(string eventName, Action<T> datafrom) where T : ISourceData
	{
		ForEachObjectNative forEachObjectNative = default(ForEachObjectNative);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObjectNative };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { eventName, datafrom };
		try
		{
			forEachObjectNative.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					eventName = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					datafrom = ((arguments[1] != null) ? ((Action<T>)arguments[1]) : null);
				}
				$Rougamo_AddEvent(eventName, datafrom);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObjectNative))]
	[DebuggerStepThrough]
	public void AddEventWithVar(string name, Action<object> script)
	{
		ForEachObjectNative forEachObjectNative = default(ForEachObjectNative);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObjectNative };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { name, script };
		try
		{
			forEachObjectNative.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					name = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					script = ((arguments[1] != null) ? ((Action<object>)arguments[1]) : null);
				}
				$Rougamo_AddEventWithVar(name, script);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObjectNative))]
	[DebuggerStepThrough]
	public void AddTempEvent<T>(string eventName, Action<T> datafrom) where T : ISourceData
	{
		ForEachObjectNative forEachObjectNative = default(ForEachObjectNative);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObjectNative };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { eventName, datafrom };
		try
		{
			forEachObjectNative.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					eventName = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					datafrom = ((arguments[1] != null) ? ((Action<T>)arguments[1]) : null);
				}
				$Rougamo_AddTempEvent(eventName, datafrom);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeDynamicVar(string varName, string value)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { varName, value };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					varName = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					value = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_ChangeDynamicVar(varName, value);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeDynamicVarPercent(string varName, string value)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { varName, value };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					varName = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					value = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_ChangeDynamicVarPercent(varName, value);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ChangeVars(string type, string val)
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { type, val };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					type = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					val = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_ChangeVars(type, val);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	public void SetDamageFilter(string key, float value)
	{
		if (Self != null && !string.IsNullOrEmpty(key))
		{
			if (Self is StatusManager statusManager)
			{
				statusManager.SetDamageFilter(key, value);
			}
			else
			{
				Self.DamageFilter[key] = value;
			}
		}
	}

	public void AddDamageFilter(string key, float delta)
	{
		if (Self != null && !string.IsNullOrEmpty(key))
		{
			if (Self is StatusManager statusManager)
			{
				statusManager.AddDamageFilter(key, delta);
			}
			else
			{
				Self.DamageFilter[key] = Self.DamageFilter.GetValueOrDefault(key) + delta;
			}
		}
	}

	public void RemoveDamageFilter(string key)
	{
		if (Self != null && !string.IsNullOrEmpty(key))
		{
			if (Self is StatusManager statusManager)
			{
				statusManager.RemoveDamageFilter(key);
			}
			else
			{
				Self.DamageFilter.Remove(key);
			}
		}
	}

	public void ClearDamageFilter()
	{
		if (Self != null)
		{
			if (Self is StatusManager statusManager)
			{
				statusManager.ClearDamageFilter();
			}
			else
			{
				Self.DamageFilter.Clear();
			}
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void ThrowCard(string val, string type = "1")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { val, type };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					type = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_ThrowCard(val, type);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	[Rougamo(typeof(ForEachObject))]
	[DebuggerStepThrough]
	public void BurnCard(string val, string type = "1")
	{
		ForEachObject forEachObject = default(ForEachObject);
		MethodContext methodContext = RougamoPool<MethodContext>.Get();
		methodContext.Mos = new IMo[1] { forEachObject };
		methodContext.Target = this;
		methodContext.TargetType = typeof(ScriptExecutor);
		methodContext.Method = MethodBase.GetMethodFromHandle((RuntimeMethodHandle)/*OpCode not supported: LdMemberToken*/, typeof(ScriptExecutor).TypeHandle);
		methodContext.Arguments = new object[2] { val, type };
		try
		{
			forEachObject.OnEntry(methodContext);
			if (!methodContext.ReturnValueReplaced)
			{
				if (methodContext.RewriteArguments)
				{
					object[] arguments = methodContext.Arguments;
					val = ((arguments[0] != null) ? ((string)arguments[0]) : null);
					type = ((arguments[1] != null) ? ((string)arguments[1]) : null);
				}
				$Rougamo_BurnCard(val, type);
			}
		}
		finally
		{
			RougamoPool<MethodContext>.Return(methodContext);
		}
	}

	public void Undone(params object[] args)
	{
	}

	public bool CardTopCheck()
	{
		FightUI uI = UIManager.Instance.GetUI<FightUI>("FightUI");
		if (uI == null)
		{
			return true;
		}
		if (FightUI.cardItemList.Count + uI.createCardQueue.Count < uI.CardTopCount)
		{
			return false;
		}
		return true;
	}

	public void GetCardFromDeck(IDataConfig idata)
	{
		FightCardManager.Instance.cardList.Remove(idata as DataConfig);
		FightCardManager.Instance.usedCardList.Remove(idata as DataConfig);
		UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(idata as DataConfig);
	}

	public void UpdateSkillTime()
	{
		if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").UpdateSkill();
		}
	}

	public void UseCard(IDataConfig idata)
	{
		idata.scriptExecutor.Self = Self;
		if (idata.scriptExecutor.Target == null)
		{
			idata.scriptExecutor.Target = Target;
		}
		idata.scriptExecutor.RunScript("UseScript");
		if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
			{
				cardData = (idata as DataConfig),
				isBurning = TagCheck(idata, "Burnout")
			}, toThrow: true, needInit: true);
			UIManager.Instance.GetUI<FightUI>("FightUI").CallActionAnimation(idata.scriptExecutor);
		}
	}

	public bool TagCheck(IDataConfig theData, string tag)
	{
		DataConfig key = theData as DataConfig;
		if (FightCardManager.Instance == null)
		{
			return false;
		}
		FightCardManager.Instance.RefreshTag(key);
		if (!FightCardManager.Instance.CardTags[key].Contains(tag))
		{
			return false;
		}
		return true;
	}

	public void RepeatByBuffLevel(string buffId, Action action)
	{
		IBuffItem buffItem = Self?.GetBuff(buffId);
		if (buffItem != null && action != null)
		{
			int num = Math.Max(1, buffItem.buffConfig.Level);
			for (int i = 0; i < num; i++)
			{
				action();
			}
		}
	}

	public void RepeatRitualEcho(string ritualBuffId, string eventName)
	{
		IBuffItem buffItem = Self?.GetBuff("buff_ritualechostaff");
		if (buffItem != null)
		{
			int num = Math.Max(1, buffItem.buffConfig.Level);
			for (int i = 0; i < num; i++)
			{
				RunImmediately(ritualBuffId, eventName);
			}
		}
	}

	public void UpdateAllDharmasSpellList()
	{
		string spellChain = (Self?.GetBuff("buff_AllDharmas"))?.buffConfig?.dataConfig?.Vars.GetValueOrDefault("SpellChain", "");
		Self?.UpdateAllDharmasSpellList(spellChain);
	}

	public void ClearAllDharmasSpellList()
	{
		Self?.ClearAllDharmasSpellList();
	}

	public void AddEnemyAction(DataConfig outData)
	{
		foreach (IStatusManager item in Object)
		{
			if (item != null && !(item.fatherObject == null) && item.fatherObject is OtherObj otherObj)
			{
				otherObj.AddAction(outData);
			}
		}
	}

	public void FightRelicCheck(Action<List<DataConfig>, string> action)
	{
		SetStatus("AllFriends");
		foreach (IStatusManager item in new List<IStatusManager>(Object))
		{
			PlayerManager.Instance.SendQuery(new QueryRelic(item.InstanceId), delegate(RelicData result)
			{
				action(result.relicList.ToList(), item.InstanceId);
			});
		}
	}

	public void Resentment(string count)
	{
		foreach (IStatusManager item in new List<IStatusManager>(Object))
		{
			if (item == null)
			{
				break;
			}
			IBuffItem[] buffs = item.GetBuffs();
			if (buffs == null)
			{
				break;
			}
			List<Dictionary<string, string>> source = (from x in Singleton<GameConfigManager>.Instance.GetTable(DataType.Buff).Getlines().AsValueEnumerable()
				where x["Type"] == "负面"
				select x).ToList();
			source = source.Where((Dictionary<string, string> x) => !buffs.Any((IBuffItem y) => y.buffConfig.dataConfig.data["Id"] == x["Id"])).ToList();
			source = source.Where((Dictionary<string, string> x) => x["Id"] != "buff_oblivion" && x["Id"] != "buff_chaos" && x["Id"] != "buff_cripple").ToList();
			SetStatusById(item.InstanceId);
			int num = count.ToInt();
			for (int num2 = 0; num2 < num; num2++)
			{
				if (source.Count == 0)
				{
					AddBuff("buff_resentment", (num - num2).ToString());
					break;
				}
				List<Dictionary<string, string>> list = new RandomPool(source, DefaultDice.dice).DrawByCount(1);
				if (list.Count > 0)
				{
					AddBuff(list[0]["Id"], "1");
				}
				source.Remove(list[0]);
			}
		}
	}

	public bool CheckFrom(string thisId)
	{
		if (Singleton<TempDataManager>.Instance == null || Singleton<TempDataManager>.Instance.RoleStatusMap == null)
		{
			return false;
		}
		if (!Singleton<TempDataManager>.Instance.RoleStatusMap.ContainsKey(RoleTable.Instance.Id))
		{
			return false;
		}
		if (!Singleton<TempDataManager>.Instance.RoleStatusMap[RoleTable.Instance.Id].Contains(thisId))
		{
			return false;
		}
		return true;
	}

	public void ChooseCardToAction(string count, Action<List<CardItem>> onCardSelected, string type = "1")
	{
		if (!(UIManager.Instance.GetUI<FightUI>("FightUI") == null))
		{
			SetStatus("Self");
			UIManager.Instance.GetUI<FightUI>("FightUI").SelectCardToAction(count, onCardSelected, type);
		}
	}

	public void CopyCardWare(string count, List<IDataConfig> source, Action<List<IDataConfig>> feedback, string AddTag = "Burnout,Retain")
	{
		CopyCard(count, source, feedback, AddTag);
	}

	public async UniTask CopyCard(string count, List<IDataConfig> source, Action<List<IDataConfig>> feedback = null, string AddTag = "Fragmented,Retain")
	{
		await SelectDeckCards(count, source, delegate(List<IDataConfig> cardList)
		{
			feedback?.Invoke(cardList);
			foreach (IDataConfig card in cardList)
			{
				DataConfig dataConfig = card as DataConfig;
				DataConfig dataConfig2 = dataConfig.Clone() as DataConfig;
				IDictionary<string, string> vars = dataConfig2.Vars;
				vars["Tag"] = vars["Tag"] + "," + AddTag;
				if (RoleTable.Instance.enchasedDict.ContainsKey(dataConfig.InstanceID))
				{
					RoleTable.Instance.enchasedDict.Add(dataConfig2.InstanceID, RoleTable.Instance.enchasedDict[dataConfig.InstanceID]);
				}
				UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(dataConfig2);
			}
		});
	}

	public void CreateCard(IDataConfig config)
	{
		DataConfig dataConfig = config as DataConfig;
		DataConfig dataConfig2 = dataConfig.Clone() as DataConfig;
		if (RoleTable.Instance.enchasedDict.ContainsKey(dataConfig.InstanceID))
		{
			RoleTable.Instance.enchasedDict.Add(dataConfig2.InstanceID, RoleTable.Instance.enchasedDict[dataConfig.InstanceID]);
		}
		UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(dataConfig2);
	}

	public void DesEnemyAllAction()
	{
		List<IStatusManager> list = Object.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			Enemy enemy = GetEnemy(list[i]);
			if (enemy == null)
			{
				continue;
			}
			List<ObjectCard> list2 = enemy.ActionCards.ToList();
			int num = 0;
			foreach (ObjectCard item in list2)
			{
				_ = item;
				enemy.AnnounceDesAction(num);
				num++;
			}
		}
	}

	public void DesEnemyAction()
	{
		List<IStatusManager> list = Object.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			Enemy enemy = GetEnemy(list[i]);
			if (enemy == null)
			{
				continue;
			}
			List<ObjectCard> list2 = enemy.ActionCards.ToList();
			int num = 0;
			foreach (ObjectCard item in list2)
			{
				if (item.dataConfig.data["Action"] == "Skill" || item.dataConfig.data["Action"] == "Attack")
				{
					enemy.AnnounceDesAction(num);
					break;
				}
				num++;
			}
		}
	}

	public FightType returnFightType()
	{
		return FightManager.Instance?.fightType ?? FightType.None;
	}

	public void BurnCardByData(IDataConfig fromdata)
	{
		foreach (IDataConfig item in new List<IDataConfig>(FightCardManager.Instance.cardList))
		{
			if (item.InstanceID == fromdata.InstanceID)
			{
				FightCardManager.Instance.cardList.Remove(item as DataConfig);
				if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
				{
					UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
					{
						cardData = (item as DataConfig),
						isBurning = true
					}, toThrow: false, needInit: true);
				}
			}
		}
		foreach (IDataConfig item2 in new List<IDataConfig>(FightCardManager.Instance.usedCardList))
		{
			if (item2.InstanceID == fromdata.InstanceID)
			{
				FightCardManager.Instance.usedCardList.Remove(item2 as DataConfig);
				if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
				{
					UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
					{
						cardData = (item2 as DataConfig),
						isBurning = true
					}, toThrow: false, needInit: true);
				}
			}
		}
		foreach (CardItem item3 in HandCard)
		{
			if (item3.dataConfig.InstanceID == fromdata.InstanceID)
			{
				item3.Burning();
				break;
			}
		}
		foreach (CardItem item4 in WaitCard)
		{
			if (item4.dataConfig.InstanceID == fromdata.InstanceID)
			{
				item4.Burning();
				break;
			}
		}
	}

	public void UpdateRelicShow()
	{
		if (UIManager.Instance.GetUI<TopBarUI>("TopBarUI") != null)
		{
			UIManager.Instance.GetUI<TopBarUI>("TopBarUI").UpdateRelicCountShow();
		}
	}

	public void ReplaceSelfRelicWithRandomRelic(string count = "1")
	{
		IDataConfig sourceRelic = dataConfig;
		string sourceId = sourceRelic?.data?.GetValueOrDefault("Id", string.Empty) ?? string.Empty;
		int addCount = Math.Max(0, count.ToInt());
		UniTask.DelayFrame(2).ContinueWith(delegate
		{
			RoleTable instance = RoleTable.Instance;
			if (instance != null)
			{
				RemoveRelicInstance(instance, sourceRelic as DataConfig);
				AddRandomRelicsExcluding(sourceId, addCount);
				UpdateRelicShow();
			}
		}).Forget();
	}

	private static void RemoveRelicInstance(RoleTable role, DataConfig relic)
	{
		if (role == null || relic == null)
		{
			return;
		}
		if (!role.relicList.Remove(relic))
		{
			DataConfig dataConfig = role.relicList.FirstOrDefault((DataConfig x) => x.InstanceID == relic.InstanceID);
			if (dataConfig != null)
			{
				role.relicList.Remove(dataConfig);
			}
		}
		if (!role.WithoutArmedRelicList.Remove(relic))
		{
			DataConfig dataConfig2 = role.WithoutArmedRelicList.FirstOrDefault((DataConfig x) => x.InstanceID == relic.InstanceID);
			if (dataConfig2 != null)
			{
				role.WithoutArmedRelicList.Remove(dataConfig2);
			}
		}
	}

	private void AddRandomRelicsExcluding(string excludedId, int count)
	{
		if (count <= 0 || RoleTable.Instance == null || Singleton<GameConfigManager>.Instance == null)
		{
			return;
		}
		List<Dictionary<string, string>> list = new List<Dictionary<string, string>>();
		foreach (DataConfig relic in RoleTable.Instance.relicList)
		{
			list.Add(new Dictionary<string, string>(relic.data));
		}
		if (list.Count == 0)
		{
			list = Singleton<GameConfigManager>.Instance.GetTable(DataType.Relic).Getlines().AsValueEnumerable()
				.Where(delegate(Dictionary<string, string> x)
				{
					if (x == null || x.GetValueOrDefault("Id", string.Empty) == excludedId)
					{
						return false;
					}
					return !Singleton<GameRuntimeData>.Instance.IsLocked(x["Id"]);
				})
				.ToList();
		}
		for (int num = 0; num < count; num++)
		{
			int index = UnityEngine.Random.Range(0, list.Count);
			Dictionary<string, string> dictionary = list[index];
			RoleTable.Instance.WithoutArmedRelicList.Add(new DataConfig(dictionary["Id"], DataType.Relic));
		}
	}

	public void ComboSc()
	{
		Self.AddBuff("buff_revelation", 1);
		int num = 0;
		bool flag = false;
		foreach (DataConfig card in FightCardManager.Instance.cardList)
		{
			if (FightCardManager.Instance.CardTags.ContainsKey(card))
			{
				if (FightCardManager.Instance.CardTags[card].Contains("Combo"))
				{
					flag = true;
					break;
				}
				num++;
			}
		}
		if (flag)
		{
			DataConfig value = FightCardManager.Instance.cardList[FightCardManager.Instance.cardList.Count - 1];
			FightCardManager.Instance.cardList[FightCardManager.Instance.cardList.Count - 1] = FightCardManager.Instance.cardList[num];
			FightCardManager.Instance.cardList[num] = value;
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(1);
			}
		}
	}

	public bool ComboCheck()
	{
		if (Self == null)
		{
			return false;
		}
		if (Self.GetBuff("buff_revelation") == null)
		{
			ComboSc();
		}
		IDataConfig lastCard = PlayerInfo.LastCard;
		if (lastCard == null || FightCardManager.Instance.CardTags[lastCard as DataConfig].Contains("Combo"))
		{
			return true;
		}
		return false;
	}

	public void EndTheGame()
	{
		if (UIManager.Instance.GetUI<EventUI>("EventUI") != null)
		{
			UIManager.Instance.GetUI<EventUI>("EventUI").Close();
		}
		UIManager.Instance.ShowUI<GameExitUI>("GameExitUI");
	}

	public void EscapeFight()
	{
		UnityEngine.Debug.Log("触发了逃跑");
		UniTask.WaitForSeconds(0.2f).ContinueWith(delegate
		{
			PlayerInfo.ChangeType(PlayerInfo.Escape);
		}).Forget();
	}

	public void LossFight()
	{
		UniTask.WaitForSeconds(0.3f).ContinueWith(delegate
		{
			PlayerInfo.ChangeType(PlayerInfo.Loss);
		}).Forget();
	}

	public void RandomAddBuff(string count)
	{
		List<Dictionary<string, string>> list = Singleton<GameConfigManager>.Instance.GetTable(DataType.Buff).Getlines().AsValueEnumerable()
			.Where(delegate(Dictionary<string, string> x)
			{
				string text = x["Type"];
				return text == "正面" || text == "负面";
			})
			.ToList();
		int count2 = count.ToInt();
		if (Object.Exists((IStatusManager item) => item.fatherObject is FightPlayer && PlayerInfo.TempLucky >= 20))
		{
			list = (from x in list.AsValueEnumerable()
				where x["Type"] != "负面"
				select x).ToList();
		}
		RandomAddBuffsByPool(list, count2);
	}

	public void RandomAddBuffAndAbility(string count)
	{
		List<Dictionary<string, string>> list = Singleton<GameConfigManager>.Instance.GetTable(DataType.Buff).Getlines().AsValueEnumerable()
			.Where(delegate(Dictionary<string, string> x)
			{
				string text = x["Type"];
				return text == "正面" || text == "负面" || text == "能力";
			})
			.ToList();
		int count2 = count.ToInt();
		if (Object.Exists((IStatusManager item) => item.fatherObject is FightPlayer && PlayerInfo.TempLucky >= 20))
		{
			list = (from x in list.AsValueEnumerable()
				where x["Type"] != "负面"
				select x).ToList();
		}
		RandomAddBuffsByPool(list, count2);
	}

	public void RandomAddGoodBuff(string count, string type = "1")
	{
		List<Dictionary<string, string>> buff = ((!(type == "1")) ? (from x in Singleton<GameConfigManager>.Instance.GetTable(DataType.Buff).Getlines().AsValueEnumerable()
			where x["Type"] == "负面"
			select x).ToList() : (from x in Singleton<GameConfigManager>.Instance.GetTable(DataType.Buff).Getlines().AsValueEnumerable()
			where x["Type"] == "正面"
			select x).ToList());
		int count2 = count.ToInt();
		RandomAddBuffsByPool(buff, count2);
	}

	private void RandomAddBuffsByPool(List<Dictionary<string, string>> buff, int count)
	{
		if (count <= 0 || buff == null || buff.Count == 0)
		{
			return;
		}
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		foreach (Dictionary<string, string> item in new RandomPool(buff, DefaultDice.dice).DrawByCountWithReplacement(count))
		{
			if (item.TryGetValue("Id", out var value) && !string.IsNullOrEmpty(value))
			{
				dictionary[value] = dictionary.GetValueOrDefault(value, 0) + 1;
			}
		}
		foreach (KeyValuePair<string, int> item2 in dictionary)
		{
			AddBuff(item2.Key, item2.Value.ToString());
		}
	}

	public void AddEnemy(string id)
	{
		string id2 = EnemyManager.Instance.AddEnemy(id);
		Singleton<EventCenter>.Instance.EventTrigger("AddEnemy" + Self.InstanceId, new NewEnemyData(id2));
	}

	public string atk()
	{
		string result = "0";
		if (Self == null)
		{
			return "0";
		}
		if (Self.fatherObject is Enemy)
		{
			Enemy enemy = Self.fatherObject as Enemy;
			if (enemy != null)
			{
				return enemy.Attack.ToString();
			}
			return "0";
		}
		if (Self.fatherObject is Partner)
		{
			Partner partner = Self.fatherObject as Partner;
			if (partner != null)
			{
				return partner.Attack.ToString();
			}
			return "0";
		}
		return result;
	}

	public void AddBaseEvent(string eventName, Action action)
	{
		Singleton<EventCenter>.Instance.AddEventListener(eventName, (Action)delegate
		{
			action();
		}, (object)this, EventDispose.OnFightEnd);
		if (Self != null && !Self.IsNull())
		{
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "<color=grey>" + Self.Name + "具体对象添加了事件监听" + eventName + "</color>");
		}
	}

	public Enemy GetEnemy(IStatusManager status)
	{
		return status.fatherObject as Enemy;
	}

	public string def()
	{
		string result = "0";
		if (Self.fatherObject is Enemy)
		{
			Enemy enemy = Self.fatherObject as Enemy;
			if (enemy != null)
			{
				return enemy.Defend.ToString();
			}
			return "0";
		}
		if (Self.fatherObject is Partner)
		{
			Partner partner = Self.fatherObject as Partner;
			if (partner != null)
			{
				return partner.Defend.ToString();
			}
			return "0";
		}
		return result;
	}

	public void CallEffect()
	{
		if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").CallActionAnimation(this);
		}
	}

	public void OnlineDamage(string val, string fromDataId, string fromId, string damagetype = "Normal")
	{
		IStatusManager statusManager = status ?? Self;
		if (statusManager != null)
		{
			statusManager.Hit(val.ToInt(), damagetype, fromDataId, fromId);
			if (!statusManager.IsNull())
			{
				Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", fromId + "通过" + fromDataId + "对" + statusManager.Name + "造成了 <color=green>" + val + "</color> 基础值点伤害");
			}
		}
	}

	public void Damage(string val, string damagetype = "Normal")
	{
		foreach (IStatusManager item in new List<IStatusManager>(Object))
		{
			int curHp = item.CurHp;
			Singleton<EventCenter>.Instance.EventTrigger("Attack" + Self.InstanceId);
			status = item;
			if (damagetype != "True" && damagetype != "Dot")
			{
				if (TrySendOnlineEvent("OnlineDamage", new string[4]
				{
					Self.DamageCalculate(val.ToInt()).ToString(),
					dataConfig.data["Id"],
					Self.InstanceId,
					damagetype
				}))
				{
					continue;
				}
				string s = Self.DamageCalculate(val.ToInt()).ToString();
				item.Hit(s.ToInt(), damagetype, dataConfig.data["Id"], Self.InstanceId);
			}
			else
			{
				if (TrySendOnlineEvent("OnlineDamage", new string[4]
				{
					val,
					dataConfig.data["Id"],
					Self.InstanceId,
					damagetype
				}))
				{
					continue;
				}
				item.Hit(val.ToInt(), damagetype, dataConfig.data["Id"], Self.InstanceId);
			}
			int num = curHp - item.CurHp;
			if (Self != null && !Self.IsNull())
			{
				Commands.Log("id为" + Self.InstanceId + "<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", string.Format("{0}对{1}造成了 <color=red>{2}</color> 点{3}", Self.Name, item.Name, num, (damagetype + "Damage").Localize("Glossary")));
			}
		}
		if (damagetype != "True" && damagetype != "Dot")
		{
			Singleton<EventCenter>.Instance.EventTrigger("AttackDone" + Self.InstanceId);
		}
		status = null;
	}

	public List<Dictionary<string, string>> GetcardsByRarity(string Minrarity, string Maxrairty)
	{
		return Singleton<GameConfigManager>.Instance.CardPackCheck((from x in Singleton<GameConfigManager>.Instance.GetTable(DataType.Card).Getlines().AsValueEnumerable()
			where x["Rarity"].ToInt() >= Minrarity.ToInt() && x["Rarity"].ToInt() <= Maxrairty.ToInt() && !Singleton<GameRuntimeData>.Instance.IsLocked(x["Id"])
			select x).ToList());
	}

	public List<Dictionary<string, string>> GetcardsOutLock()
	{
		return Singleton<GameConfigManager>.Instance.CardPackCheck(Singleton<GameConfigManager>.Instance.GetTable(DataType.Card).Getlines());
	}

	public DataConfig EnchGetCard()
	{
		foreach (KeyValuePair<string, DataConfig> item in RoleTable.Instance.enchasedDict)
		{
			if (item.Value == dataConfig && RoleTable.Instance.cardList.Any((DataConfig x) => x.InstanceID == item.Key))
			{
				return RoleTable.Instance.cardList.First((DataConfig x) => x.InstanceID == item.Key);
			}
		}
		return null;
	}

	public DataConfig CardGetEnch(IDataConfig card)
	{
		if (card == null)
		{
			return null;
		}
		if (RoleTable.Instance.enchasedDict.ContainsKey(card.InstanceID))
		{
			return RoleTable.Instance.enchasedDict[card.InstanceID];
		}
		return null;
	}

	public void PackToDeckAction(string count, List<IDataConfig> source, Action<List<IDataConfig>> action, float DelayTime = 0f)
	{
		GetDeckUIToAction(count, source, action, DelayTime);
	}

	public async UniTask GetDeckUIToAction(string count, List<IDataConfig> source, Action<List<IDataConfig>> action, float DelayTime = 0f)
	{
		await SelectDeckCards(count, source, action, () => UIManager.Instance.GetUI<TopBarUI>("TopBarUI") != null && Self != null && !Self.IsNull(), DelayTime);
	}

	public async UniTask AddCardByDeck(string count, List<IDataConfig> source, string tag = "all")
	{
		List<IDataConfig> list = new List<IDataConfig>();
		foreach (DataConfig item in source)
		{
			if (item.Vars["Tag"].Contains(tag))
			{
				list.Add(item);
			}
			if (RoleTable.Instance.enchasedDict.ContainsKey(item.InstanceID) && RoleTable.Instance.enchasedDict[item.InstanceID].Vars["Tag"].Contains(tag))
			{
				list.Add(item);
			}
		}
		if (tag == "all")
		{
			list = source;
		}
		await SelectDeckCards(count, list, delegate(List<IDataConfig> cardList)
		{
			foreach (IDataConfig card in cardList)
			{
				FightCardManager.Instance.cardList.Remove(card as DataConfig);
				FightCardManager.Instance.usedCardList.Remove(card as DataConfig);
			}
			foreach (IDataConfig card2 in cardList)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(card2 as DataConfig);
			}
		});
	}

	public async void OutFightSelectCardToAction(string count, List<IDataConfig> source, Action<List<IDataConfig>> cardevent)
	{
		await SelectDeckCards(count, source, cardevent);
	}

	private async UniTask SelectDeckCards(string count, List<IDataConfig> source, Action<List<IDataConfig>> onSelected, Func<bool> canShow = null, float delayTime = 0f)
	{
		CancellationToken cancellationToken = Singleton<GameConfigManager>.Instance.cts.Token;
		object selectCardEndOwner = new object();
		await UniTask.WaitUntil(() => !deckUiSelectBusy && UIManager.Instance.GetUI<DeckUI>("DeckUI") == null, PlayerLoopTiming.Update, cancellationToken);
		deckUiSelectBusy = true;
		try
		{
			if (delayTime > 0f)
			{
				await UniTask.WaitForSeconds(delayTime, ignoreTimeScale: false, PlayerLoopTiming.Update, cancellationToken);
			}
			count = Math.Min(count.ToInt(), source.Count()).ToString();
			if (count == "0" || (canShow != null && !canShow()))
			{
				return;
			}
			UniTaskCompletionSource selectCompletion = new UniTaskCompletionSource();
			List<IDataConfig> cardList = new List<IDataConfig>();
			Singleton<EventCenter>.Instance.AddEventListener("SelectCardEnd", delegate
			{
				try
				{
					onSelected?.Invoke(cardList);
				}
				finally
				{
					Singleton<EventCenter>.Instance.RemoveEventListener("SelectCardEnd", selectCardEndOwner);
					selectCompletion.TrySetResult();
				}
			}, selectCardEndOwner);
			UIManager.Instance.ShowUI<DeckUI>("DeckUI");
			UIManager.Instance.GetUI<DeckUI>("DeckUI").CreateDeckMenuForSelect(count.ToInt(), cardList, source);
			await selectCompletion.Task.AttachExternalCancellation(cancellationToken);
		}
		finally
		{
			Singleton<EventCenter>.Instance.RemoveEventListener("SelectCardEnd", selectCardEndOwner);
			deckUiSelectBusy = false;
		}
	}

	public void SetStatusById(string searchId)
	{
		if (FightManager.Instance.statuses.TryGetValue(searchId, out var value))
		{
			Object.Clear();
			Object.Add(value);
		}
	}

	public List<IStatusManager> SetStatus(string filter)
	{
		bool flag = Vars.GetValueOrDefault("IsAllFriend", "False") == "True";
		List<IStatusManager> list = new List<IStatusManager>();
		Object.Clear();
		bool flag2 = filter.Contains("ExSelf");
		filter = filter.Replace("ExSelf", "");
		bool flag3 = filter.StartsWith("AllRandom");
		if (flag3)
		{
			filter = filter.Replace("AllRandom", "");
		}
		int val = 1;
		string text = new string(filter.AsValueEnumerable().Where(char.IsDigit).ToArray());
		if (!string.IsNullOrEmpty(text))
		{
			val = text.ToInt();
			filter = filter.Replace(text, "");
		}
		string text2 = "";
		string text3 = "";
		if (filter.StartsWith("All"))
		{
			text2 = "All";
			text3 = filter.Substring(3);
		}
		else
		{
			text2 = filter;
			text3 = filter;
		}
		if (!flag3)
		{
			if (text2 == "Self")
			{
				list.Add(Self);
				Object.Clear();
				Object.AddRange(list);
				return Object;
			}
			if (text2 == "Target")
			{
				if (Self.fatherObject is Enemy)
				{
					Target = FightPlayer.Instance?.Status;
					list.AddRange(FightManager.Instance.roleQueue.Select((FightManager.RoleData r) => FightManager.Instance.statuses[r.InstanceId]).ToArray());
					Object.Clear();
					Object.AddRange(list);
					return Object;
				}
				if (!(Self.fatherObject is Enemy) && (EnemyManager.Instance == null || EnemyManager.Instance.enemyList.Count == 0))
				{
					return Object;
				}
				if ((Self.fatherObject is FightPlayer || Self.fatherObject is Partner) && Target == null)
				{
					Target = EnemyManager.Instance.enemyList[0].Status;
					if (flag)
					{
						int num = FightManager.Instance.roleQueue.Count() - 1;
						if (num <= 0)
						{
							Target = null;
						}
						else
						{
							Target = FightManager.Instance.statuses[FightManager.Instance.roleQueue[DefaultDice.WithRange(0, num).Roll().Value].InstanceId];
						}
					}
				}
				if (Target != null)
				{
					list.Add(Target);
					Object.Clear();
					Object.AddRange(list);
				}
				return Object;
			}
		}
		List<IStatusManager> list2 = new List<IStatusManager>();
		bool flag4 = text3.Contains("Friends");
		bool flag5 = text3.Contains("Target");
		if (flag)
		{
			flag4 = true;
			flag5 = false;
		}
		if (flag5)
		{
			if (Self.fatherObject is Enemy)
			{
				list2.AddRange((from r in FightManager.Instance.roleQueue
					select FightManager.Instance.statuses.GetValueOrDefault(r.InstanceId) into s
					where s != null
					select s).ToArray());
			}
			else
			{
				list2.AddRange((from e in EnemyManager.Instance.enemyList.AsValueEnumerable()
					where e != null && e.enabled && e.Status != null && e.Status.state != IStatusManager.State.Dead
					select e.Status).ToArray());
			}
		}
		else if (flag4)
		{
			if (Self.fatherObject is Enemy)
			{
				list2.AddRange((from e in EnemyManager.Instance.enemyList.AsValueEnumerable()
					where e != null && e.enabled && e.Status != null && e.Status.state != IStatusManager.State.Dead
					select e.Status).ToArray());
			}
			else
			{
				list2.AddRange(FightManager.Instance.roleQueue.Select((FightManager.RoleData r) => FightManager.Instance.statuses[r.InstanceId]).ToArray());
			}
		}
		else if (text2 == "All")
		{
			list2.AddRange((from e in EnemyManager.Instance.enemyList.AsValueEnumerable()
				where e != null && e.enabled && e.Status.state != IStatusManager.State.Dead
				select e.Status).ToArray());
			list2.AddRange(FightManager.Instance.roleQueue.Select((FightManager.RoleData r) => FightManager.Instance.statuses[r.InstanceId]).ToArray());
		}
		if (flag2)
		{
			list2 = (from c in list2.AsValueEnumerable()
				where c != Self
				select c).ToList();
		}
		if (flag3)
		{
			using ValueEnumerator<OrderBySkipTake<FromList<IStatusManager>, IStatusManager, int>, IStatusManager> valueEnumerator = (from _ in list2.AsValueEnumerable()
				orderby DefaultDice.Roll().Value
				select _).Take(Math.Min(val, list2.Count)).GetEnumerator<OrderBySkipTake<FromList<IStatusManager>, IStatusManager, int>, IStatusManager>();
			while (valueEnumerator.MoveNext())
			{
				IStatusManager current = valueEnumerator.Current;
				list.Add(current);
			}
		}
		else
		{
			foreach (IStatusManager item in list2)
			{
				list.Add(item);
			}
		}
		Object.Clear();
		Object.AddRange(list);
		return Object;
	}

	public List<IStatusManager> SetStatus(IEnumerable<IStatusManager> statuses)
	{
		List<IStatusManager> collection = new List<IStatusManager>(statuses);
		Object.Clear();
		Object.AddRange(collection);
		return Object;
	}

	public List<IStatusManager> SetStatus(ValueEnumerable<FromEnumerable<IStatusManager>, IStatusManager> statuses)
	{
		Object.Clear();
		Object.AddRange(statuses.ToList());
		return Object;
	}

	public void ProcessEffect(IStatusManager status, string effectName)
	{
		Singleton<EffectManager>.Instance.PlayEffect(status, effectName);
	}

	public void DiceCheck(int percent, Action<bool> action)
	{
		int value = CheckDice.Roll().Value;
		action(value >= percent);
	}

	public void ForAllStatus(Action<IStatusManager> action)
	{
		List<IStatusManager> list = Object.ToList();
		foreach (IStatusManager item in list)
		{
			Object.Clear();
			Object.Add(item);
			action(item);
		}
		Object.Clear();
		Object.AddRange(list);
	}

	public void Log(string content)
	{
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>日志", content);
	}

	public void WatchRoleTable(string propertyName, Action action)
	{
		handlers.Add(Singleton<PropertyWatcher>.Instance.AddListener(RoleTable.Instance, propertyName, action));
	}

	public void WatchRoleTable(string propertyName, Action<int> action)
	{
		handlers.Add(Singleton<PropertyWatcher>.Instance.AddListener(RoleTable.Instance, propertyName, action));
	}

	public void AddDescription(string index, string type, string value)
	{
		int.TryParse(index, out var result);
		if (result == 0 && index != "0")
		{
			Commands.Log("<color=red>错误</color>", "无法添加描述值，索引 " + index + " 不是有效的数字。");
			return;
		}
		string key = "DesVal" + index;
		switch (type)
		{
		case "Damage":
			value = ((Self != null) ? Self.DamageCalculate(value.ToInt()).ToString() : value.ToInt().ToString());
			if (!Target.IsNull())
			{
				int num5 = value.ToInt();
				if (Target.DamageFilter != null && Target.DamageFilter.ContainsKey(dataConfig.data["Id"]))
				{
					num5 = (int)((float)num5 * (100f - Target.DamageFilter[dataConfig.data["Id"]]) / 100f);
				}
				if (Target.DamageFilter != null && Target.DamageFilter.ContainsKey("Normal"))
				{
					num5 = (int)((float)num5 * (100f - Target.DamageFilter["Normal"]) / 100f);
				}
				int num6 = Target.UnDamageCalucate(num5);
				if (num6 < 0)
				{
					num6 = 0;
				}
				int num7 = num6 - value.ToInt();
				string text = ((num7 > 0) ? "green" : ((num7 >= 0) ? "white" : "red"));
				string color = text;
				value = WrapByColor(num6.ToString(), color);
			}
			break;
		case "MultiDamage":
			value = ((Self != null) ? (Self.DamageCalculate(value.Split("*")[0].ToInt()) + "*" + value.Split("*")[1]) : (value.Split("*")[0].ToInt() + "*" + value.Split("*")[1]));
			if (!Target.IsNull())
			{
				int num3 = value.Split("*")[0].ToInt();
				if (Target.DamageFilter != null && Target.DamageFilter.ContainsKey(dataConfig.data["Id"]))
				{
					num3 = (int)((float)num3 * (100f - Target.DamageFilter[dataConfig.data["Id"]]) / 100f);
				}
				if (Target.DamageFilter != null && Target.DamageFilter.ContainsKey("Normal"))
				{
					num3 = (int)((float)num3 * (100f - Target.DamageFilter["Normal"]) / 100f);
				}
				int num4 = Target.UnDamageCalucate(num3);
				if (num4 < 0)
				{
					num4 = 0;
				}
				value = num4 + "*" + value.Split("*")[1];
			}
			break;
		case "TrueDamage":
		{
			int num2 = value.ToInt();
			if (!Target.IsNull())
			{
				if (Target.DamageFilter.ContainsKey(dataConfig.data["Id"]))
				{
					num2 = (int)((float)num2 * (100f - Target.DamageFilter[dataConfig.data["Id"]]) / 100f);
				}
				if (Target.DamageFilter.ContainsKey("Normal"))
				{
					num2 = (int)((float)num2 * (100f - Target.DamageFilter["Normal"]) / 100f);
				}
			}
			value = num2.ToString();
			break;
		}
		case "Defence":
			value = ((Self != null) ? Self.DefenceCalculate(value.ToInt()).ToString() : value.ToInt().ToString());
			break;
		case "Hp":
		case "Heal":
		{
			int num = value.ToInt();
			if (Self != null)
			{
				num = (int)((float)num * Self.dynamicVariables.GetValueOrDefault("HealMultiplier", 1f));
			}
			value = WrapByColor(num.ToString(), "white");
			break;
		}
		case "Power":
			value = WrapByColor(value.ToInt().ToString(), "white");
			break;
		case "Draw":
			value = WrapByColor(value.ToInt().ToString(), "white");
			break;
		case "Money":
			value = WrapByColor(value.ToInt().ToString(), "white");
			break;
		case "Buff":
			value = WrapByColor(value.ToInt().ToString(), "white");
			break;
		case "Value":
			value = WrapByColor(value.ToInt().ToString(), "white");
			break;
		case "Percent":
			value = WrapByColor(float.Parse(value).ToString("P0"), "white");
			break;
		}
		Vars[key] = value;
		static string WrapByColor(string val, string text2)
		{
			return "<color=" + text2 + ">" + val + "</color>";
		}
	}

	public string GetDesValue(string index)
	{
		int.TryParse(index, out var result);
		if (result == 0 && index != "0")
		{
			Commands.Log("<color=red>错误</color>", "无法获取描述值，索引 " + index + " 不是有效的数字。");
			return "";
		}
		string key = "DesVal" + index;
		if (Vars.ContainsKey(key))
		{
			return Regex.Replace(Vars[key], "<.*?>", "");
		}
		return "0";
	}

	public void AddDescription(string index, string type, int value)
	{
		AddDescription(index, type, value.ToString());
	}

	public void AddDescription(string index, string type, float value)
	{
		AddDescription(index, type, value.ToString());
	}

	public void AddDescription(string index, string type, double value)
	{
		AddDescription(index, type, value.ToString());
	}

	public void CallScript(string scriptId, string scriptName)
	{
		DataConfig obj = new DataConfig(Singleton<GameConfigManager>.Instance.DataConfigCache[scriptId].data, dataConfig.Vars);
		obj.scriptExecutor.Self = Self;
		obj.scriptExecutor.Object.Clear();
		obj.scriptExecutor.Object.AddRange(Object);
		obj.scriptExecutor.Target = Target;
		obj.scriptExecutor.RunScript(scriptName);
	}

	static ScriptExecutor()
	{
		DynamicMethodDelegates = new List<Func<ScriptExecutor, Delegate>>();
		List<MetadataReference> list = new List<MetadataReference>();
		list.AddRange(LoadMetadataFromName("PE"));
		options = ScriptOptions.Default.AddReferences(list).AddImports("System", "System.Collections.Generic", "System.Linq", "UnityEngine").WithMetadataResolver(new LockedMetadataResolver(ScriptMetadataResolver.Default));
	}

	private static PortableExecutableReference[] LoadMetadataFromName(string assemblyName)
	{
		List<byte[]> list = (from b in Addressables.LoadAssetsAsync<TextAsset>(assemblyName ?? "").WaitForCompletion()
			select b.bytes).ToList();
		List<PortableExecutableReference> list2 = new List<PortableExecutableReference>();
		foreach (byte[] item in list)
		{
			list2.Add(MetadataReference.CreateFromImage(item));
		}
		return list2.ToArray();
	}

	private static void InitLuaEnv()
	{
		if (luaEnv != null || luaEnv != null)
		{
			return;
		}
		luaEnv = new LuaEnv();
		ScriptExecutor.luaTable = luaEnv.NewTable();
		LuaTable luaTable = luaEnv.NewTable();
		luaTable.Set("__index", luaEnv.Global);
		ScriptExecutor.luaTable.SetMetaTable(luaTable);
		luaTable.Dispose();
		luaEnv.Global.Set("self", ScriptExecutor.luaTable);
		luaEnv.DoString("PlayerInfo = CS.ScriptExecutor.PlayerInfo;");
		ScriptExecutor.luaTable.Set("ScriptExecutor", typeof(ScriptExecutor));
		luaEnv.AddLoader(delegate(ref string path)
		{
			if (!path.StartsWith("Mods"))
			{
				return (byte[])null;
			}
			path = ResourceLoader.ResolveModPath(path);
			return File.ReadAllBytes(path);
		});
	}

	internal static bool EnsureLuaEnvReady(string context)
	{
		if (luaEnv != null)
		{
			return true;
		}
		UnityEngine.Debug.LogWarning("[Lua] LuaEnv 尚未初始化，尝试在 " + context + " 阶段补初始化。");
		InitLuaEnv();
		if (luaEnv == null)
		{
			UnityEngine.Debug.LogError("[Lua] LuaEnv 初始化失败，阶段: " + context);
			return false;
		}
		return true;
	}

	private void InitLuaEnvInstance(string luaScript = null)
	{
		if (!EnsureLuaEnvReady("ScriptExecutor(" + (dataConfig?.data?["Id"] ?? "Unknown") + ").InitLuaEnvInstance"))
		{
			return;
		}
		scriptExecutorEnv = luaEnv.NewTable();
		LuaTable luaTable = luaEnv.NewTable();
		luaTable.Set("__index", ScriptExecutor.luaTable);
		scriptExecutorEnv.SetMetaTable(luaTable);
		luaTable.Dispose();
		scriptExecutorEnv.Set("self", this);
		string text = Application.streamingAssetsPath + "/LuaSource/" + dataConfig.data["Id"];
		if (luaScript == null && File.Exists(text + ".lua"))
		{
			luaEnv.DoString(File.ReadAllText(text + ".lua"), dataConfig.data["Id"], scriptExecutorEnv);
			{
				foreach (string key in dataConfig.data.Keys)
				{
					if (!key.Contains("Script"))
					{
						continue;
					}
					LuaFunction func = scriptExecutorEnv.Get<LuaFunction>(key);
					if (func != null)
					{
						ScriptDict[key] = (Action<ScriptExecutor>)delegate(ScriptExecutor executor)
						{
							executor.CallLuaFunc(func);
						};
					}
				}
				return;
			}
		}
		if (string.IsNullOrEmpty(luaScript))
		{
			return;
		}
		luaEnv.DoString(luaScript, dataConfig.data["Id"], scriptExecutorEnv);
		foreach (string key2 in dataConfig.data.Keys)
		{
			if (!key2.Contains("Script"))
			{
				continue;
			}
			LuaFunction func2 = scriptExecutorEnv.Get<LuaFunction>(key2);
			if (func2 != null)
			{
				ScriptDict[key2] = (Action<ScriptExecutor>)delegate(ScriptExecutor executor)
				{
					executor.CallLuaFunc(func2);
				};
			}
		}
	}

	internal object[] CallLuaFunc(LuaFunction func)
	{
		try
		{
			if (func == null)
			{
				UnityEngine.Debug.LogError(dataConfig.data["Id"] + " 的 Lua 脚本执行时发生错误！函数为空\n");
				return null;
			}
			if (scriptExecutorEnv == null)
			{
				InitLuaEnvInstance();
				if (scriptExecutorEnv == null)
				{
					UnityEngine.Debug.LogError(dataConfig.data["Id"] + " 的 Lua 脚本执行时发生错误！环境为空（luaEnv=" + ((luaEnv == null) ? "null" : "ok") + "）\n");
					return null;
				}
			}
			func.SetEnv(scriptExecutorEnv);
			return func.Call();
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogError(dataConfig.data["Id"] + " 的 Lua 脚本执行时发生错误！\n" + ex?.ToString() + "\n");
			return null;
		}
	}

	internal static void Init()
	{
		InitLuaEnv();
		AllScriptDict = global::AllScripts.AllScripts.totalScripts;
	}

	internal ScriptExecutor(DataConfig dataConfig)
	{
		this.dataConfig = dataConfig;
	}

	private Delegate CreateDelegate(string Id, string scriptType)
	{
		string text = MakeSafeIdentifier(Id);
		string text2 = MakeSafeIdentifier(scriptType);
		string key = text + "_" + text2;
		if (AllScriptDict == null || !AllScriptDict.ContainsKey(key))
		{
			return null;
		}
		return AllScriptDict[key];
		static string MakeSafeIdentifier(string input)
		{
			string text3 = Regex.Replace(input, "[^\\w_]", "_");
			if (char.IsDigit(text3[0]))
			{
				text3 = "_" + text3;
			}
			return text3;
		}
	}

	private Delegate CreateDelegateFromLua(string ScriptName)
	{
		if (!EnsureLuaEnvReady("ScriptExecutor(" + (dataConfig?.data?["Id"] ?? "Unknown") + ").CreateDelegateFromLua"))
		{
			return null;
		}
		if (scriptExecutorEnv != null)
		{
			LuaFunction cachedFunc = scriptExecutorEnv.Get<LuaFunction>(ScriptName);
			if (cachedFunc != null)
			{
				return (Action<ScriptExecutor>)delegate(ScriptExecutor executor)
				{
					executor.CallLuaFunc(cachedFunc);
				};
			}
		}
		string chunk = dataConfig.data[ScriptName];
		LuaFunction loadedFunc = luaEnv.LoadString(chunk, ScriptName);
		return (Action<ScriptExecutor>)delegate(ScriptExecutor executor)
		{
			executor.CallLuaFunc(loadedFunc);
		};
	}

	public void PreCompileScripts(string ScriptName, ScriptOptions options = null)
	{
		if (!dataConfig.data.ContainsKey(ScriptName))
		{
			foreach (KeyValuePair<string, string> datum in dataConfig.data)
			{
				UnityEngine.Debug.Log(datum.Key + ":" + datum.Value);
			}
			UnityEngine.Debug.LogError(dataConfig.data["Id"] + "找不到脚本" + ScriptName);
			return;
		}
		string value = dataConfig.data[ScriptName];
		Delegate obj = null;
		obj = CreateDelegate(dataConfig.data["Id"], ScriptName);
		if ((object)obj == null && !string.IsNullOrEmpty(value))
		{
			if (scriptExecutorEnv == null)
			{
				InitLuaEnvInstance();
			}
			obj = CreateDelegateFromLua(ScriptName);
		}
		if ((object)obj != null)
		{
			ScriptDict[ScriptName] = obj;
		}
	}

	public void RunScript(string ScriptsName)
	{
		if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None && ScriptsName != "InitScript" && Self == null)
		{
			UnityEngine.Debug.LogError(FightManager.Instance.fightType.ToString() + "Self is null");
			return;
		}
		try
		{
			if (!ScriptDict.ContainsKey(ScriptsName))
			{
				PreCompileScripts(ScriptsName);
			}
			if (ScriptDict.ContainsKey(ScriptsName))
			{
				if (ScriptDict[ScriptsName] is ScriptRunner<object> scriptRunner)
				{
					scriptRunner(this).Wait(Singleton<GameConfigManager>.Instance.cts.Token);
				}
				else if (ScriptDict[ScriptsName] is Action action)
				{
					action();
				}
				else if (ScriptDict[ScriptsName] is Action<ScriptExecutor> action2)
				{
					action2(this);
				}
			}
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogError(dataConfig.data["Id"] + " 的 " + ScriptsName + " 脚本执行时发生错误！\n" + ex?.ToString() + "\n对应为" + dataConfig.data[ScriptsName]);
			throw;
		}
	}

	public void Clear()
	{
		Singleton<EventCenter>.Instance.Clear(this);
		foreach (PropertyChangedEventHandler handler in handlers)
		{
			Singleton<PropertyWatcher>.Instance.RemoveListener(RoleTable.Instance, handler);
		}
		handlers.Clear();
	}

	~ScriptExecutor()
	{
		Clear();
	}

	public bool TrySendOnlineEvent(string eventName, string[] parameters)
	{
		if (status == null)
		{
			return false;
		}
		if (PlayerManager.Instance != null && (!Singleton<TempDataManager>.Instance.RoleStatusMap.ContainsKey(RoleTable.Instance.Id) || !Singleton<TempDataManager>.Instance.RoleStatusMap[RoleTable.Instance.Id].Contains(status.InstanceId)) && !Vars.ContainsKey("Online"))
		{
			byte[] theData = ObjTargetBase.SerializeConfigData(dataConfig.data);
			FightManager.Instance.CmdSendEvent(eventName, status.InstanceId, Self.InstanceId, dataConfig.data["Id"], theData, parameters);
			return true;
		}
		return false;
	}

	private void $Rougamo_SetHp(string val)
	{
		status.CurHp = val.ToInt();
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", Self.Name + "将" + status.Name + "的理智值变更为 <color=green>" + val + "</color>");
	}

	private void $Rougamo_SetMaxHp(string val)
	{
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", Self.Name + "将" + status.Name + "的最大理智值变更为 <color=green>" + val + "</color>");
		status.MaxHp = val.ToInt();
	}

	private void $Rougamo_ChangeHp(string val)
	{
		int curHp = status.CurHp;
		if (val.ToInt() < 0)
		{
			status.Hit(-val.ToInt(), "Dot", dataConfig.data["Id"], Self.InstanceId);
			int num = curHp - status.CurHp;
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", string.Format("{0}对{1}造成了 <color=red>{2}</color>点{3}", Self.Name, status.Name, num, "TrueDamage".Localize("Glossary")));
		}
		else
		{
			status.Heal(val.ToInt(), "Heal");
			int num2 = status.CurHp - curHp;
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", ZString.Format("{0}对{1}恢复了 <color=green>{2}</color> 点血量", (object)Self.Name, (object)status.Name, (object)num2));
		}
	}

	private void $Rougamo_PureChangeHp(string val)
	{
		int curHp = status.CurHp;
		status.CurHp += int.Parse(val);
		int num = curHp - status.CurHp;
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", string.Format("{0}对{1}改变了 <color=red>{2}</color>点{3}", Self.Name, status.Name, num, "TrueDamage".Localize("Glossary")));
	}

	private void $Rougamo_ChangeSkill(string val)
	{
		foreach (string item in PlayerInfo.SkillTime.Keys.ToList())
		{
			PlayerInfo.SkillTime[item] = Math.Max(0, PlayerInfo.SkillTime[item] + int.Parse(val));
		}
		if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").UpdateSkill();
		}
	}

	private void $Rougamo_AddCardById(string id)
	{
		if (!(status.fatherObject is FightPlayer))
		{
			UnityEngine.Debug.Log("父物体是" + status.fatherObject);
			return;
		}
		id = id?.Replace("*", "").Trim();
		if (string.IsNullOrEmpty(id) || Singleton<GameConfigManager>.Instance.GetOne(DataType.Card, id) == null)
		{
			UnityEngine.Debug.Log("没有id" + id);
			return;
		}
		DataConfig dataConfig = new DataConfig(id, DataType.Card);
		FightCardManager.Instance.cardList.Add(dataConfig);
		FightCardManager.Instance.CardTags.Add(dataConfig, new HashSet<string>());
		dataConfig.Vars["Tag"].Replace(" ", "");
		string[] array = dataConfig.Vars["Tag"].Split('|', ',', '，', ' ', ';', '；');
		foreach (string item in array)
		{
			FightCardManager.Instance.CardTags[dataConfig].Add(item);
		}
		UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(1);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家添加了一张卡牌" + this.dataConfig.data.Localize("Name"));
	}

	private void $Rougamo_AddCardToDeckById(string Id, bool toUsed = true)
	{
		DataConfig dataConfig = new DataConfig(Id, DataType.Card);
		AddCardToFightManager(dataConfig, toUsed);
	}

	private void $Rougamo_AddFakeCard(bool toUsed = true)
	{
		DataConfig dataConfig = new DataConfig("cursecard_15", DataType.Card);
		if (RoleTable.Instance == null)
		{
			return;
		}
		int count = RoleTable.Instance.cardList.Count;
		if (count == 0)
		{
			return;
		}
		count = DefaultDice.WithRange(0, count - 1).Roll().Value;
		DataConfig dataConfig2 = RoleTable.Instance.cardList[count];
		Dictionary<string, string> dictionary = new Dictionary<string, string>(dataConfig2.data);
		dictionary["Id"] = dataConfig.data["Id"];
		foreach (KeyValuePair<string, string> var in dataConfig2.Vars)
		{
			if (var.Key != "Id" && var.Key != "InstanceID")
			{
				dataConfig.Vars[var.Key] = dataConfig2.Vars[var.Key];
			}
		}
		dataConfig.Vars["IsFake"] = "True";
		dataConfig.data = dictionary;
		RoleTable.Instance.enchasedDict.Add(dataConfig.InstanceID, new DataConfig("enchtag_16", DataType.EnchTag));
		if (toUsed)
		{
			FightCardManager.Instance.usedCardList.Add(dataConfig);
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
				{
					cardData = dataConfig,
					isBurning = false
				}, toThrow: true, needInit: true);
			}
			return;
		}
		FightCardManager.Instance.cardList.Add(dataConfig);
		int value = DefaultDice.WithRange(0, FightCardManager.Instance.cardList.Count - 1).Roll().Value;
		ObservableCollection<DataConfig> cardList;
		int index = (cardList = FightCardManager.Instance.cardList).Count - 1;
		ObservableCollection<DataConfig> cardList2 = FightCardManager.Instance.cardList;
		int index2 = value;
		DataConfig dataConfig3 = FightCardManager.Instance.cardList[value];
		ObservableCollection<DataConfig> cardList3 = FightCardManager.Instance.cardList;
		DataConfig dataConfig4 = cardList3[cardList3.Count - 1];
		DataConfig dataConfig5 = (cardList[index] = dataConfig3);
		dataConfig5 = (cardList2[index2] = dataConfig4);
		if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
			{
				cardData = dataConfig,
				isBurning = false
			}, toThrow: false, needInit: true);
		}
	}

	private void $Rougamo_ChangeMaxHp(string val)
	{
		int num = val.ToInt();
		if (status?.fatherObject is FightPlayer)
		{
			num = Mathf.RoundToInt((float)num * Commands.DebugPlayerMaxHpGainMultiplier);
		}
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", ZString.Format("{0}将{1}的最大理智值增加了 <color=green>{2}</color>", (object)Self.Name, (object)status.Name, (object)num));
		status.MaxHp += num;
	}

	private void $Rougamo_AddBuff(string buffId, string level)
	{
		if (status != null && status.InstanceId != null)
		{
			Singleton<EventCenter>.Instance.EventTrigger("AddBuff" + Self.InstanceId, new AddBuffData(new DataConfig(buffId, DataType.Buff), Self.InstanceId, dataConfig.data["Id"], status.InstanceId));
			status.AddBuff(buffId, level.ToInt());
			if (Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, buffId) != null)
			{
				Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", Self.Name + "对" + status.Name + "施加了 <color=yellow>" + level + "</color> 层  无限  ) 回合的 <color=yellow>" + Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, buffId)["Name"] + "</color> 状态");
			}
		}
	}

	private void $Rougamo_RemoveBuff(string buffId)
	{
		status.RemoveBuff(buffId);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", Self.Name + "对" + status.Name + "移除了 <color=yellow>" + Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, buffId)["Name"] + "</color> 状态");
	}

	private void $Rougamo_RunImmediately(string buffId, string eventName)
	{
		IBuffItem buff = status.GetBuff(buffId);
		if (buff != null)
		{
			Singleton<EventCenter>.Instance.EventTrigger(eventName + status.InstanceId, (object)buff.scriptExecutor);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", Self.Name + "对" + status.Name + "立即触发了 <color=yellow>" + Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, buffId)["Name"] + "</color> 状态");
		}
	}

	private void $Rougamo_Resurrection(string value)
	{
		status.Resurrection(value.ToInt());
	}

	private void $Rougamo_ChangeDefence(string val)
	{
		if (status != null)
		{
			int defend = status.Defend;
			if (status.fatherObject is OtherObj)
			{
				status.Defend += (int)((float)val.ToInt() * status.dynamicVariables.GetValueOrDefault("DefendPercent", 1f));
			}
			else if (status.fatherObject is FightPlayer)
			{
				status.Defend += Self.DefenceCalculate(val.ToInt());
			}
			int num = status.Defend - defend;
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", ZString.Format("{0}对{1}增加了 <color=blue>{2}</color> 点护盾", (object)Self.Name, (object)status.Name, (object)num));
		}
	}

	private void $Rougamo_SetPower(string val)
	{
		if (status.fatherObject is FightPlayer)
		{
			FightPlayer.Instance.CurPowerCount = val.ToInt();
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的能量变更为 <color=blue>" + val + "</color>");
		}
	}

	private void $Rougamo_DrawCount(string val)
	{
		if (status.fatherObject == null || !(status.fatherObject is FightPlayer))
		{
			return;
		}
		int num = val.ToInt();
		if (num != 0 && num >= 0)
		{
			int count = FightCardManager.Instance.cardList.Count;
			int num2 = num;
			if (!FightCardManager.Instance.HasCard() || count < num2)
			{
				FightCardManager.Instance.RandomIndex();
			}
			UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(num2);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", ZString.Format("{0}玩家抽取了 <color=purple>{1}</color> 张卡牌", (object)status.InstanceId, (object)num2));
		}
	}

	private void $Rougamo_ChangePower(string val)
	{
		new List<IStatusManager>(Object);
		if (!(status.fatherObject is FightPlayer))
		{
			return;
		}
		FightPlayer.Instance.CurPowerCount += val.ToInt();
		if (val.ToInt() < 0)
		{
			Singleton<EventCenter>.Instance.EventTrigger("CostPower" + FightPlayer.Instance.Status?.InstanceId, new PowerData(status.InstanceId));
			if (FightPlayer.Instance.CurPowerCount <= 0)
			{
				FightPlayer.Instance.CurPowerCount = 0;
				Singleton<EventCenter>.Instance.EventTrigger("NoPower" + FightPlayer.Instance.Status?.InstanceId);
			}
		}
		else
		{
			AudioManager.Instance?.PlayEffect("NewSounds/战斗中/恢复魔能");
			Singleton<EventCenter>.Instance.EventTrigger("AddPower" + FightPlayer.Instance.Status?.InstanceId);
		}
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", status.InstanceId + "玩家的能量" + ((val.ToInt() > 0) ? "增加" : "减少") + "了 <color=blue>" + val + "</color>");
	}

	private void $Rougamo_ChangeMaxPower(string val)
	{
		if (status.fatherObject is FightPlayer)
		{
			FightPlayer.Instance.MaxPowerCount += val.ToInt();
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的最大能量" + ((val.ToInt() > 0) ? "增加" : "减少") + "了 <color=blue>" + val + "</color>");
		}
	}

	private void $Rougamo_ChangeRound()
	{
		if (status.fatherObject is FightPlayer)
		{
			FightManager.Instance.TurnEnd();
		}
		status.ChangeState(IStatusManager.State.NoAction);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", status.Name + "的回合被跳过");
	}

	private void $Rougamo_DoAction(string index)
	{
		if (status.fatherObject is Enemy)
		{
			Enemy obj = status.fatherObject as Enemy;
			obj.DoOneAction(index.ToInt(), isSingle: true);
			obj.ShowAction();
		}
		else if (status.fatherObject is Partner)
		{
			Partner obj2 = status.fatherObject as Partner;
			obj2.DoOneAction(index.ToInt(), isSingle: true);
			obj2.ShowAction();
		}
		else if (HandCard.Count > index.ToInt())
		{
			HandCard[index.ToInt()].dataConfig.scriptExecutor.RunScript("UseScript");
		}
	}

	private void $Rougamo_RemoveBadBuff(string val, string good = "false")
	{
		int num = val.ToInt();
		IBuffItem[] buffs = status.GetBuffs();
		List<IBuffItem> list = new List<IBuffItem>();
		for (int i = 0; i < buffs.Length; i++)
		{
			if (num <= 0)
			{
				break;
			}
			if ((buffs[i].buffConfig.Type == "负面" && good == "false") || (buffs[i].buffConfig.Type == "正面" && good == "true"))
			{
				list.Add(buffs[i]);
				num--;
			}
		}
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", status.Name + "移除了 <color=yellow>" + string.Join(",", list.Select((IBuffItem x) => x.buffConfig.BuffName)) + "</color> buff");
		foreach (IBuffItem item in list)
		{
			item.ClearBuff();
		}
	}

	private void $Rougamo_RemoveAllBadBuff(string obj)
	{
		obj = ((obj == "1") ? "正面" : "负面");
		if (status == null)
		{
			UnityEngine.Debug.Log("这里没有status");
		}
		IBuffItem[] buffs = status.GetBuffs();
		List<IBuffItem> list = new List<IBuffItem>();
		for (int i = 0; i < buffs.Length; i++)
		{
			if (buffs[i] != null && buffs[i].buffConfig.dataConfig.data["Type"] == obj)
			{
				list.Add(buffs[i]);
			}
		}
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", status.Name + "移除了 <color=yellow>" + string.Join(",", list.Select((IBuffItem x) => x.buffConfig.BuffName).ToArray()) + "</color> buff");
		foreach (IBuffItem item in list)
		{
			item.ClearBuff();
		}
	}

	private void $Rougamo_RemoveAllBuff()
	{
		status.ClearAllBuff();
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", Self.Name + "对" + status.Name + "移除了所有状态");
	}

	private void $Rougamo_AddCardByCardList(string count, string tag = "all")
	{
		if (status.fatherObject is FightPlayer)
		{
			AddCardByDeck(count, FightCardManager.Instance.cardList.Cast<IDataConfig>().ToList(), tag);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家从抽牌堆检索了 <color=purple>" + count + "</color> 张卡牌");
		}
	}

	private void $Rougamo_AddCardByUsedCardList(string count, string tag = "all")
	{
		if (status.fatherObject is FightPlayer)
		{
			AddCardByDeck(count, FightCardManager.Instance.usedCardList.Cast<IDataConfig>().ToList(), tag);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家从弃牌堆检索了 <color=purple>" + count + "</color> 张卡牌");
		}
	}

	private void $Rougamo_RandomAddCard(string id)
	{
		if (status.fatherObject is FightPlayer)
		{
			DataConfig dataConfig = new DataConfig(id, DataType.Card);
			FightCardManager.Instance.cardList.Add(dataConfig);
			FightCardManager.Instance.CardTagCheck(dataConfig);
			int value = DefaultDice.WithRange(0, FightCardManager.Instance.cardList.Count - 1).Roll().Value;
			ObservableCollection<DataConfig> cardList;
			int index = (cardList = FightCardManager.Instance.cardList).Count - 1;
			ObservableCollection<DataConfig> cardList2 = FightCardManager.Instance.cardList;
			int index2 = value;
			DataConfig dataConfig2 = FightCardManager.Instance.cardList[value];
			ObservableCollection<DataConfig> cardList3 = FightCardManager.Instance.cardList;
			DataConfig dataConfig3 = cardList3[cardList3.Count - 1];
			DataConfig dataConfig4 = (cardList[index] = dataConfig2);
			dataConfig4 = (cardList2[index2] = dataConfig3);
			if (UIManager.Instance.GetUI<FightUI>("FightUI") != null)
			{
				UIManager.Instance.GetUI<FightUI>("FightUI").DoCardUseAnimation(new UseCard.CardUseData
				{
					cardData = dataConfig,
					isBurning = false
				}, toThrow: false, needInit: true);
			}
		}
		Commands.Log("<color=grey>" + this.dataConfig.data.Localize("Name") + "</color>效果", "玩家随机添加了一张卡牌" + this.dataConfig.data.Localize("Name"));
	}

	private void $Rougamo_ChangeMoney(string val, string changeMax = "false")
	{
		int num = val.ToInt();
		if (status.fatherObject is FightPlayer)
		{
			long num2 = (long)RoleTable.Instance.Money + num;
			if (num2 >= 0)
			{
				RoleTable.Instance.Money = (int)((num2 >= int.MaxValue) ? int.MaxValue : num2);
			}
			else
			{
				RoleTable.Instance.Money = 0;
				if (changeMax == "true")
				{
					ChangeMaxHp(num2.ToString());
				}
			}
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的金钱" + ((val.ToInt() > 0) ? "增加" : "减少") + "了 <color=yellow>" + val + "</color>");
		}
		else
		{
			ChangeHp(val);
		}
	}

	private void $Rougamo_AddAction(string count)
	{
		int num = count.ToInt();
		if (status.fatherObject is OtherObj)
		{
			OtherObj otherObj = status.fatherObject as OtherObj;
			otherObj.MaxActionCount += num;
			if (otherObj.MaxActionCount > 4)
			{
				otherObj.MaxActionCount = 4;
			}
		}
	}

	private void $Rougamo_ShuffleDeck()
	{
		if (status.fatherObject is FightPlayer)
		{
			FightCardManager.Instance.RandomIndex();
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的卡组被重新洗牌了");
		}
	}

	private void $Rougamo_ShuffleHand()
	{
		if (!(status.fatherObject is FightPlayer))
		{
			return;
		}
		foreach (CardItem item in HandCard.ToList())
		{
			FightUI.cardItemList.Remove(item);
			item.EffectOfThrowCard("Canvas/FightUI/Left/Card");
			FightCardManager.Instance.cardList.Add(item.dataConfig);
		}
		FightCardManager.Instance.RandomIndex(NeedUsed: false);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的卡组被重新洗牌了");
	}

	private void $Rougamo_ChangeCardTop(string val)
	{
		if (status.fatherObject is FightPlayer)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").CardTopCount += val.ToInt();
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的手牌上限增加了 <color=purple>" + val + "</color> 张卡牌");
		}
	}

	private void $Rougamo_GetCardByTag(string count, string tag = "all")
	{
		if (!(status.fatherObject is FightPlayer))
		{
			return;
		}
		List<DataConfig> list = null;
		list = ((!(tag != "all")) ? FightCardManager.Instance.cardList.ToList() : (from x in FightCardManager.Instance.cardList.AsValueEnumerable()
			where FightCardManager.Instance.CardTags[x].Contains(tag)
			select x).ToList());
		if (list.Count == 0)
		{
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "没有找到符合条件的卡牌");
			return;
		}
		int num = Math.Min(count.ToInt(), list.Count);
		for (int num2 = 0; num2 < num; num2++)
		{
			DataConfig item = list[num2];
			FightCardManager.Instance.cardList.Remove(item);
			UIManager.Instance.GetUI<FightUI>("FightUI").CreateCardItem(item);
		}
	}

	private void $Rougamo_AddCard(string id)
	{
		if (status.fatherObject is FightPlayer)
		{
			DataConfig dataConfig = new DataConfig(id, DataType.Card);
			FightCardManager.Instance.cardList.Add(dataConfig);
			FightCardManager.Instance.CardTags.Add(dataConfig, new HashSet<string>());
			dataConfig.Vars["Tag"] = dataConfig.Vars["Tag"].Replace(" ", "");
			string[] array = dataConfig.Vars["Tag"].Split('|', ',', '，', ' ', ';', '；');
			foreach (string item in array)
			{
				FightCardManager.Instance.CardTags[dataConfig].Add(item);
			}
		}
	}

	private void $Rougamo_AddCardByData(string Id, string AddTag = "")
	{
		if (!(status.fatherObject is FightPlayer))
		{
			return;
		}
		DataConfig dataConfig = new DataConfig(Id, DataType.Card);
		FightCardManager.Instance.cardList.Add(dataConfig);
		FightCardManager.Instance.CardTags.Add(dataConfig, new HashSet<string>());
		if (!string.IsNullOrEmpty(AddTag))
		{
			IDictionary<string, string> vars = dataConfig.Vars;
			vars["Tag"] = vars["Tag"] + "," + AddTag;
		}
		dataConfig.Vars["Tag"] = dataConfig.Vars["Tag"].Replace(" ", "");
		string[] array = dataConfig.Vars["Tag"].Split('|', ',', '，', ' ', ';', '；');
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				FightCardManager.Instance.CardTags[dataConfig].Add(text);
			}
		}
	}

	private void $Rougamo_ChangeCareer(string Id)
	{
		if (status.fatherObject is FightPlayer)
		{
			DataType type = ((Singleton<GameConfigManager>.Instance.GetOne(DataType.Enemy, Id) != null) ? DataType.Enemy : DataType.Career);
			RoleTable.Instance.Career = new DataConfig(Id, type);
			FightPlayer.Instance.Status.ResetAnimator(replaceImmediate: false);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家的职业变更为 <color=yellow>" + RoleTable.Instance.Career.data["Id"] + "</color>");
		}
		else if (status.fatherObject is OtherObj)
		{
			OtherObj otherObj = status.fatherObject as OtherObj;
			otherObj.dataConfig = new DataConfig(Id, DataType.Enemy);
			status.ResetAnimator(replaceImmediate: false);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", otherObj.Name + "的职业变更为 <color=yellow>" + otherObj.dataConfig.data["Id"] + "</color>");
		}
		if (!Application.isEditor || Application.isPlaying)
		{
			FightManager.Instance.CmdChangeCareer(Id, status.InstanceId);
		}
	}

	private void $Rougamo_ChangeSummon(bool Isshow)
	{
		FightManager.Instance.CmdChangeSummon(Isshow, status.InstanceId);
	}

	private void $Rougamo_AddEvent(string eventName, Action script)
	{
		Singleton<EventCenter>.Instance.AddEventListener(eventName + status.InstanceId, (Action)delegate
		{
			script();
		}, (object)this, EventDispose.OnFightEnd);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "<color=grey>" + Self.Name + "具体对象" + Self.InstanceId + "对" + status.Name + "具体对象" + status.InstanceId + "添加了事件监听" + eventName + "</color>");
	}

	private void $Rougamo_AddTempEvent(string eventName, Action script)
	{
		Singleton<EventCenter>.Instance.AddEventListener(eventName + status.InstanceId, (Action)delegate
		{
			script();
		}, (object)this, EventDispose.OnTrigger);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "<color=grey>" + Self.Name + "具体对象" + Self.InstanceId + "对" + status.Name + "具体对象" + status.InstanceId + "添加了一次性事件监听" + eventName + "</color>");
	}

	private void $Rougamo_AddEvent<T>(string eventName, Action<T> datafrom) where T : ISourceData
	{
		Singleton<EventCenter>.Instance.AddEventListener(eventName + status.InstanceId, datafrom, this, EventDispose.OnFightEnd);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "<color=grey>" + Self.Name + "具体对象" + Self.InstanceId + "对" + status.Name + "具体对象" + status.InstanceId + "添加了事件监听" + eventName + "</color>");
	}

	private void $Rougamo_AddEventWithVar(string name, Action<object> script)
	{
		Singleton<EventCenter>.Instance.AddEventListener(name + status.InstanceId, script, this, EventDispose.OnFightEnd);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "<color=grey>" + Self.Name + "具体对象" + Self.InstanceId + "对" + status.Name + "具体对象" + status.InstanceId + "添加了事件监听" + name + "</color>");
	}

	private void $Rougamo_AddTempEvent<T>(string eventName, Action<T> datafrom) where T : ISourceData
	{
		Singleton<EventCenter>.Instance.AddEventListener(eventName + status.InstanceId, datafrom, this, EventDispose.OnTrigger);
	}

	private void $Rougamo_ChangeDynamicVar(string varName, string value)
	{
		if (status is StatusManager statusManager)
		{
			statusManager.AddDynamicVariable(varName, float.Parse(value));
		}
		else
		{
			status.dynamicVariables[varName] = status.dynamicVariables.GetValueOrDefault(varName) + float.Parse(value);
		}
		if (!status.dynamicVariablesLog.ContainsKey(dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]))
		{
			status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]] = new Dictionary<string, float>();
		}
		if (!status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]].ContainsKey(varName))
		{
			status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]][varName] = 0f;
		}
		status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]][varName] += float.Parse(value);
		Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", ZString.Format("{0}的{1}变更为 <color=green>{2}</color>", (object)status.Name, (object)varName, (object)status.dynamicVariables[varName]));
	}

	private void $Rougamo_ChangeDynamicVarPercent(string varName, string value)
	{
		if (status is StatusManager statusManager)
		{
			statusManager.AddDynamicVariable(varName, float.Parse(value) / 100f);
		}
		else
		{
			status.dynamicVariables[varName] = status.dynamicVariables.GetValueOrDefault(varName) + float.Parse(value) / 100f;
		}
		if (!status.dynamicVariablesLog.ContainsKey(dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]))
		{
			status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]] = new Dictionary<string, float>();
		}
		if (!status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]].ContainsKey(varName))
		{
			status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]][varName] = 0f;
		}
		status.dynamicVariablesLog[dataConfig.scriptExecutor.Self.InstanceId + dataConfig.data["Id"]][varName] += float.Parse(value) / 100f;
		if (status.fatherObject != null && !string.IsNullOrEmpty(status.Name))
		{
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", ZString.Format("{0}的{1}变更为 <color=green>{2}</color>", (object)status.Name, (object)varName, (object)status.dynamicVariables[varName]));
		}
	}

	private void $Rougamo_ChangeVars(string type, string val)
	{
		if (status.fatherObject is FightPlayer)
		{
			int num = val.ToInt();
			if (FightManager.Instance.TempVarsMap.ContainsKey(type))
			{
				FightManager.Instance.TempVarsMap[type] += num;
				UIManager.Instance.GetUI<TopBarUI>("TopBarUI").ChangeVar();
				Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", status.InstanceId + "玩家增加了 <color=purple>" + val + "</color> 点" + type + "属性");
			}
		}
	}

	private void $Rougamo_ThrowCard(string val, string type = "1")
	{
		if (status.fatherObject is FightPlayer)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").ThrowCardScript(val, type);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家丢弃了 <color=purple>" + val + "</color> 张卡牌");
		}
	}

	private void $Rougamo_BurnCard(string val, string type = "1")
	{
		if (status.fatherObject is FightPlayer)
		{
			UIManager.Instance.GetUI<FightUI>("FightUI").Burning(val, type);
			Commands.Log("<color=grey>" + dataConfig.data.Localize("Name") + "</color>效果", "玩家焚毁了 <color=purple>" + val + "</color> 张卡牌");
		}
	}
}
