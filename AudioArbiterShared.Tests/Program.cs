using AudioArbiter.Shared;
using AuraAudio.Shared;

var tests = new AudioArbiterContractTests();
tests.Run();

internal sealed partial class AudioArbiterContractTests
{
    private int assertions;

    public void Run()
    {
        VerifyManifestDefaults();
        VerifyConstants();
        VerifyFileLoadPolicy();
        VerifyFileFormatProbe();
        VerifyHookCatalog();
        VerifyRequestFactory();
        VerifySkillSlotResolver();
        VerifyLowHealthCoordinator();
        VerifyPropertyReader();
        VerifyRequestProjection();
        VerifyNetworkProjection();
        VerifyNetworkPolicy();
        VerifyNetworkSessionState();
        VerifyManifestLoader();
        VerifyManifestMatchPolicy();
        VerifyVariantSelectionPolicy();
        VerifyProviderIdentityAndOrdering();
        VerifyProviderResolution();
        VerifyPendingPresentationQueue();
        VerifyCooldownPolicy();
        VerifyPresentationPolicy();
        VerifySuppressionPolicy();
        VerifyReplacementCoordinator();

        Console.WriteLine($"AudioArbiterShared behavior tests passed: {assertions} assertions.");
    }
}
