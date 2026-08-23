using AuraToolsExp.Dll.Features.Settings;

internal static partial class AuraToolsTestSuite
{
    public static void TestPreparationDockLayoutPolicy()
    {
        var centered = AuraToolsPreparationDockLayoutPolicy.AboveReadyButton(
            500f, -300f, 240f, 70f, 0.5f, 0.5f, 240f, -800f, 800f);
        Assert(Math.Abs(centered.X - 500f) < 0.001f
               && Math.Abs(centered.Y - (-257f)) < 0.001f,
            "preparation dock is centered eight pixels above the native ready button");

        var leftClamped = AuraToolsPreparationDockLayoutPolicy.AboveReadyButton(
            -780f, -300f, 240f, 70f, 0.5f, 0.5f, 240f, -800f, 800f);
        Assert(Math.Abs(leftClamped.X - (-672f)) < 0.001f
               && leftClamped.Y > -265f,
            "preparation dock remains above the ready button while clamping to the left safe margin");

        var rightClamped = AuraToolsPreparationDockLayoutPolicy.AboveReadyButton(
            780f, -300f, 240f, 70f, 0.5f, 0.5f, 240f, -800f, 800f);
        Assert(Math.Abs(rightClamped.X - 672f) < 0.001f
               && rightClamped.Y > -265f,
            "preparation dock remains above the ready button while clamping to the right safe margin");

        var multiAction = AuraToolsPreparationDockLayoutPolicy.AboveReadyButton(
            0f, -360f, 300f, 80f, 0.5f, 0.5f, 466f, -640f, 640f);
        Assert(Math.Abs(multiAction.X) < 0.001f
               && Math.Abs(multiAction.Y - (-312f)) < 0.001f,
            "a multi-action dock stays horizontally centered and vertically above the embark control");
    }
}
