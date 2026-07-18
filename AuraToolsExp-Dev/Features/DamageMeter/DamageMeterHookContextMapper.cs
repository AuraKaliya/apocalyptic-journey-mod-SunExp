using System;
using System.Globalization;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterHookContextMapper
{
    internal static HitHookObservation? MapHit(ModHookContext context)
    {
        return context.Target is IStatusManager target
            ? new HitHookObservation(
                target,
                ArgumentString(context.Arguments, 1),
                ArgumentString(context.Arguments, 2),
                ArgumentString(context.Arguments, 3))
            : null;
    }

    internal static DamageTextHookObservation? MapDamageText(ModHookContext context)
    {
        return context.Arguments != null && context.Arguments.Length > 0
            ? new DamageTextHookObservation(context.Arguments[0])
            : null;
    }

    internal static PureHpHookObservation? MapPureHp(ModHookContext context)
    {
        return context.Target is IScriptExecutor executor
            ? new PureHpHookObservation(executor, ParseInt(ArgumentString(context.Arguments, 0)))
            : null;
    }

    internal static StatusHookObservation? MapStatus(ModHookContext context)
    {
        return context.Target is IStatusManager target ? new StatusHookObservation(target) : null;
    }

    internal static ScriptBuffHookObservation? MapScriptBuff(ModHookContext context)
    {
        return context.Target is IScriptExecutor executor
            ? new ScriptBuffHookObservation(executor, ArgumentString(context.Arguments, 0))
            : null;
    }

    internal static StatusBuffHookObservation? MapStatusBuff(ModHookContext context)
    {
        return context.Target is IStatusManager target
            ? new StatusBuffHookObservation(target, StatusAddBuffId(context.Arguments))
            : null;
    }

    internal static BuffLevelHookObservation? MapBuffLevel(ModHookContext context)
    {
        return context.Target is IBuffItemConfig config
            ? new BuffLevelHookObservation(config, ParseInt(ArgumentString(context.Arguments, 0)))
            : null;
    }

    internal static object? MapRoundUnit(ModHookContext context) => context.Target;

    internal static string ArgumentString(object[]? arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Length
            ? arguments[index]?.ToString() ?? ""
            : "";
    }

    internal static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string StatusAddBuffId(object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0 || arguments[0] == null)
        {
            return "";
        }

        if (arguments[0] is string text)
        {
            return text.Trim();
        }

        if (arguments[0] is IBuffItemConfig config)
        {
            return config.BuffId?.Trim() ?? "";
        }

        return arguments[0] is IDataConfig dataConfig
            ? DamageCaptureHostReader.SafeDataId(dataConfig)
            : "";
    }
}

internal sealed class HitHookObservation
{
    internal HitHookObservation(IStatusManager target, string damageType, string sourceDataId, string sourceInstanceId)
    {
        Target = target;
        DamageType = damageType;
        SourceDataId = sourceDataId;
        SourceInstanceId = sourceInstanceId;
    }

    internal IStatusManager Target { get; }
    internal string DamageType { get; }
    internal string SourceDataId { get; }
    internal string SourceInstanceId { get; }
}

internal sealed class DamageTextHookObservation
{
    internal DamageTextHookObservation(object? value) => Value = value;
    internal object? Value { get; }
}

internal sealed class PureHpHookObservation
{
    internal PureHpHookObservation(IScriptExecutor executor, int delta)
    {
        Executor = executor;
        Delta = delta;
    }

    internal IScriptExecutor Executor { get; }
    internal int Delta { get; }
}

internal sealed class StatusHookObservation
{
    internal StatusHookObservation(IStatusManager target) => Target = target;
    internal IStatusManager Target { get; }
}

internal sealed class ScriptBuffHookObservation
{
    internal ScriptBuffHookObservation(IScriptExecutor executor, string buffId)
    {
        Executor = executor;
        BuffId = buffId;
    }

    internal IScriptExecutor Executor { get; }
    internal string BuffId { get; }
}

internal sealed class StatusBuffHookObservation
{
    internal StatusBuffHookObservation(IStatusManager target, string buffId)
    {
        Target = target;
        BuffId = buffId;
    }

    internal IStatusManager Target { get; }
    internal string BuffId { get; }
}

internal sealed class BuffLevelHookObservation
{
    internal BuffLevelHookObservation(IBuffItemConfig config, int level)
    {
        Config = config;
        Level = level;
    }

    internal IBuffItemConfig Config { get; }
    internal int Level { get; }
}
