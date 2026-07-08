using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace SunExp.Dll.Hooks;

public static class SunExpCardPresentationLifecycleBridge
{
    private static int observedSetCardStyle;
    private static bool loggedUnexpectedShape;
    private static bool loggedMissingArguments;
    private static bool loggedRecoveredShape;

    public static void Initialize()
    {
        SunExpCardLifecycleRouter.Register("CardPresentationBridge", new SunExpCardLifecycleSubscription
        {
            AfterSetCardStyle = ApplyFromSetCardStyle
        });
    }

    private static void ApplyFromSetCardStyle(ModHookContext context)
    {
        var args = context.Arguments;
        observedSetCardStyle++;
        SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleObserved");

        if (!TryExtractSetCardStyleArguments(args, out var transform, out var config))
        {
            if (!loggedMissingArguments)
            {
                loggedMissingArguments = true;
                SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentMiss");
                SunExpLog.Warn("Card presentation SetCardStyle hook observed but arguments were not Transform + IDataConfig: "
                    + ArgumentShape(args)
                    + ", observed="
                    + observedSetCardStyle);
            }

            return;
        }

        SunExpCardPresentationRouter.RequestApply(transform, config, SunExpHookTargets.ICardSetCardStyle, SunExpCardPresentationSurface.CardStyle);
    }

    private static bool TryExtractSetCardStyleArguments(object[]? args, out Transform? transform, out IDataConfig? config)
    {
        transform = null;
        config = null;
        if (args == null || args.Length == 0)
        {
            return false;
        }

        if (args.Length >= 2 && args[0] is Transform directTransform && args[1] is IDataConfig directConfig)
        {
            transform = directTransform;
            config = directConfig;
            return true;
        }

        foreach (var arg in args)
        {
            transform ??= arg as Transform;
            config ??= arg as IDataConfig;
        }

        if (transform != null && config != null)
        {
            if (!loggedRecoveredShape)
            {
                loggedRecoveredShape = true;
                SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentRecovered");
                SunExpLog.Warn("Card presentation SetCardStyle hook used recovered argument shape: " + ArgumentShape(args));
            }

            return true;
        }

        if (!loggedUnexpectedShape)
        {
            loggedUnexpectedShape = true;
            SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleUnexpectedShape");
            SunExpLog.Warn("Card presentation SetCardStyle hook argument shape unsupported: " + ArgumentShape(args));
        }

        return false;
    }

    private static string ArgumentShape(object[]? args)
    {
        if (args == null)
        {
            return "<null>";
        }

        if (args.Length == 0)
        {
            return "<empty>";
        }

        var parts = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            parts[i] = args[i]?.GetType().FullName ?? "<null>";
        }

        return string.Join(", ", parts);
    }
}
