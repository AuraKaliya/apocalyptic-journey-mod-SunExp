using System;
using System.Collections.Generic;
using AuraShared.Core;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AudioArbiter.Shared;

internal sealed class AudioHookContextMapper
{
    private const string CombatActionSource = "FightUI.CallActionAnimation";
    private readonly AudioGameStateReader gameStateReader;

    public AudioHookContextMapper(AudioGameStateReader gameStateReader)
    {
        this.gameStateReader = gameStateReader ?? throw new ArgumentNullException(nameof(gameStateReader));
    }

    public AudioCareerObservation? MapCareerDetail(ModHookContext context)
    {
        if (context.Arguments != null
            && context.Arguments.Length >= 3
            && context.Arguments[2] is bool applySelection
            && !applySelection)
        {
            return null;
        }

        var showCareer = ReadArgument<ShowCareer>(context, 0);
        var careerId = gameStateReader.ReadCareerId(showCareer);
        return string.IsNullOrWhiteSpace(careerId)
            ? null
            : new AudioCareerObservation
            {
                CareerId = careerId,
                SourceName = "GameEntryUI.ShowDetail.ApplySelection"
            };
    }

    public AudioCombatActionObservation? MapCombatAction(AuraCardActionContext context)
    {
        if (context == null
            || string.IsNullOrWhiteSpace(context.CardDataId)
            || !gameStateReader.IsLocalOwnerStatus(context.OwnerStatus, context.OwnerStatusId))
        {
            return null;
        }

        return new AudioCombatActionObservation
        {
            CardId = context.CardDataId,
            CareerId = gameStateReader.ReadCurrentCareerId(),
            RoleId = string.IsNullOrWhiteSpace(context.OwnerRoleId)
                ? gameStateReader.ReadCurrentCareerId()
                : context.OwnerRoleId,
            StatusInstanceId = context.OwnerStatusId,
            EffectName = context.Effects,
            ActionName = context.Action,
            SourceName = CombatActionSource
        };
    }

    public AudioBuffObservation? MapBuffApplied(ModHookContext context)
    {
        var config = ReadArgument<BuffItemConfig>(context, 0);
        if (config == null)
        {
            return null;
        }

        var status = ReadArgument<StatusManager>(context, 1);
        return new AudioBuffObservation
        {
            BuffId = gameStateReader.ReadBuffId(config),
            CareerId = gameStateReader.ReadCurrentCareerId(),
            StatusInstanceId = gameStateReader.ReadStatusInstanceId(status, fallbackToRole: false),
            SourceName = "BuffItem.Init"
        };
    }

    public AudioVocalObservation? MapVocalState(ModHookContext context)
    {
        var status = context.Target as StatusManager;
        var state = ReadArgument<object>(context, 0)?.ToString() ?? "";
        if (status == null || string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return new AudioVocalObservation
        {
            VocalState = state,
            CareerId = gameStateReader.ReadCurrentCareerId(),
            RoleId = gameStateReader.ReadStatusRoleId(status),
            StatusInstanceId = gameStateReader.ReadStatusInstanceId(status),
            SourceName = "StatusManager.PlayVocal.After"
        };
    }

    public AudioNarrationObservation MapNarration(ModHookContext context)
    {
        return new AudioNarrationObservation
        {
            NarrationIds = ReadArgument<int[]>(context, 0) ?? Array.Empty<int>()
        };
    }

    public IReadOnlyList<AudioStatusSnapshot> MapExecutorHpChanges(ModHookContext context)
    {
        return gameStateReader.ReadExecutorStatusSnapshots(
            context.Target as IScriptExecutor,
            "ScriptExecutor.HpChanged.Self",
            "ScriptExecutor.HpChanged.Target");
    }

    public AudioStatusSnapshot? MapStatusHpChange(ModHookContext context)
    {
        return gameStateReader.ReadStatusSnapshot(
            context.Target as StatusManager,
            "StatusManager.HpChanged");
    }

    public IReadOnlyList<AudioStatusSnapshot> MapFightStatusSnapshots()
    {
        return gameStateReader.ReadFightStatusSnapshots("Fight_Start.Init");
    }

    public AudioBattleObservation MapBattleCompleted(string result, string sourceName)
    {
        return new AudioBattleObservation
        {
            Result = result ?? "",
            CareerId = gameStateReader.ReadCurrentCareerId(),
            SourceName = sourceName ?? ""
        };
    }

    private static T? ReadArgument<T>(ModHookContext context, int index) where T : class
    {
        var arguments = context.Arguments;
        return arguments != null && index >= 0 && index < arguments.Length
            ? arguments[index] as T
            : null;
    }
}
