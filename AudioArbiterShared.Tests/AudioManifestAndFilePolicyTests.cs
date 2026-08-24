using AudioArbiter.Shared;
using AuraAudio.Shared;

internal sealed partial class AudioArbiterContractTests
{
    private void VerifyManifestDefaults()
    {
        var manifest = new AudioRegistryManifest();
        Equal(1, manifest.schemaVersion, "registry schema default");
        Equal("", manifest.ownerModId, "registry owner default");
        Null(manifest.audioProtocol, "registry protocol default");
        Null(manifest.defaults, "registry defaults default");
        Null(manifest.providers, "registry providers default");
    
        var protocol = new AudioProtocolManifest();
        Equal(1, protocol.minVersion, "protocol minimum default");
        Equal(1, protocol.preferredVersion, "protocol preferred default");
    
        var defaults = new AudioRegistryDefaults();
        Equal("", defaults.bus, "bus default");
        Equal("", defaults.policy, "policy default");
        Null(defaults.hardClaim, "hard claim default");
        Null(defaults.sync, "sync default");
        Null(defaults.cooldownSeconds, "cooldown default");
        Null(defaults.gainDb, "gain default");
        Null(defaults.volumeMultiplier, "volume default");
    
        var provider = new AudioProviderManifest();
        Equal("", provider.providerId, "provider id default");
        Equal("", provider.ownerModId, "provider owner default");
        Equal("", provider.kind, "provider kind default");
        Equal("", provider.vocalState, "provider vocal default");
        Equal("", provider.path, "provider path default");
        Null(provider.variantPaths, "provider variant paths default");
        Equal(0, provider.priority, "provider priority default");
        Null(provider.match, "provider match default");
        Null(provider.suppressOriginal, "provider suppression default");
        Null(new AudioProviderMatch().skillSlot, "skill-slot match default");
    }
    
    private void VerifyConstants()
    {
        Equal(10000, SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds, "presentation max age");
        Equal("CardUse", SoundEventKinds.CardUse, "card-use kind");
        Equal("SkillVoice", SoundEventKinds.SkillVoice, "skill-voice kind");
        Equal("CareerSelected", SoundEventKinds.CareerSelected, "career kind");
        Equal("BuffApplied", SoundEventKinds.BuffApplied, "buff kind");
        Equal("LowHealth", SoundEventKinds.LowHealth, "low-health kind");
        Equal("BattleCompleted", SoundEventKinds.BattleCompleted, "battle-completed kind");
        Equal("VocalState", SoundEventKinds.VocalState, "vocal-state kind");
        Equal("Effect", SoundBuses.Effect, "effect bus");
        Equal("Vocal", SoundBuses.Vocal, "vocal bus");
        Equal("Ui", SoundBuses.Ui, "ui bus");
        Equal("Additive", SoundPolicies.Additive, "additive policy");
        Equal("Replace", SoundPolicies.Replace, "replace policy");
        Equal("ReplaceOriginal", SoundPolicies.ReplaceOriginal, "replace-original policy");
        Equal("SuppressOriginal", SoundPolicies.SuppressOriginal, "suppress-original policy");
    }
    
    private void VerifyFileLoadPolicy()
    {
        Equal(AudioFileEncoding.Wav, AudioFileLoadPolicy.Classify("voice.WAV"), "wav file classification");
        Equal(AudioFileEncoding.OggVorbis, AudioFileLoadPolicy.Classify("voice.ogg"), "ogg file classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.mp3"), "mp3 file classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.m4a"), "m4a compatibility classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.aac"), "aac compatibility classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.bin"), "unknown extension preserves MPEG fallback");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify(null), "missing extension preserves MPEG fallback");
        Equal(AudioFileEncoding.UnsupportedVideoContainer, AudioFileLoadPolicy.Classify("voice.mp4"), "mp4 video container rejected");
        Equal(AudioFileEncoding.UnsupportedVideoContainer, AudioFileLoadPolicy.Classify("voice.m4v"), "m4v video container rejected");
        Equal(AudioFileEncoding.UnsupportedVideoContainer, AudioFileLoadPolicy.Classify("voice.mov"), "mov video container rejected");
    }
    
    private void VerifyFileFormatProbe()
    {
        var mp3 = AudioFileFormatProbe.Probe(new byte[] { 0xff, 0xfb, 0x90, 0x64 });
        Equal(true, mp3.Success, "mp3 frame detected");
        Equal(AudioFileFormat.Mp3, mp3.Format, "mp3 format");
        Equal(".mp3", mp3.CanonicalExtension, "mp3 canonical extension");
    
        var id3Mp3 = AudioFileFormatProbe.Probe(new byte[]
        {
            (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0,
            0xff, 0xfb, 0x90, 0x64
        });
        Equal(true, id3Mp3.Success, "mp3 after id3 detected");
    
        const int largeTagSize = 140 * 1024;
        var largeId3Path = Path.Combine(Path.GetTempPath(), "audio-probe-large-id3-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            var largeId3Mp3 = new byte[10 + largeTagSize + 4];
            largeId3Mp3[0] = (byte)'I';
            largeId3Mp3[1] = (byte)'D';
            largeId3Mp3[2] = (byte)'3';
            largeId3Mp3[3] = 4;
            largeId3Mp3[6] = (byte)((largeTagSize >> 21) & 0x7f);
            largeId3Mp3[7] = (byte)((largeTagSize >> 14) & 0x7f);
            largeId3Mp3[8] = (byte)((largeTagSize >> 7) & 0x7f);
            largeId3Mp3[9] = (byte)(largeTagSize & 0x7f);
            largeId3Mp3[10 + largeTagSize] = 0xff;
            largeId3Mp3[11 + largeTagSize] = 0xfb;
            largeId3Mp3[12 + largeTagSize] = 0x90;
            largeId3Mp3[13 + largeTagSize] = 0x64;
            File.WriteAllBytes(largeId3Path, largeId3Mp3);
    
            var largeId3Result = AudioFileFormatProbe.Probe(largeId3Path);
            Equal(true, largeId3Result.Success, "mp3 after id3 larger than probe window detected");
            Equal(AudioFileFormat.Mp3, largeId3Result.Format, "large-id3 file resolves mp3 format");
        }
        finally
        {
            if (File.Exists(largeId3Path))
            {
                File.Delete(largeId3Path);
            }
        }
    
        var wav = new byte[44];
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, wav, 0, 4);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, wav, 8, 4);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, wav, 12, 4);
        wav[16] = 16;
        wav[20] = 1;
        var wavResult = AudioFileFormatProbe.Probe(wav);
        Equal(true, wavResult.Success, "pcm wav detected");
        Equal(AudioFileFormat.WavPcm, wavResult.Format, "pcm wav format");
    
        var floatWav = (byte[])wav.Clone();
        floatWav[20] = 3;
        var floatWavResult = AudioFileFormatProbe.Probe(floatWav);
        Equal(true, floatWavResult.Success, "float wav detected");
        Equal(AudioFileFormat.WavIeeeFloat, floatWavResult.Format, "float wav format");
    
        var compressedWav = (byte[])wav.Clone();
        compressedWav[20] = 2;
        var compressedWavResult = AudioFileFormatProbe.Probe(compressedWav);
        Equal(false, compressedWavResult.Success, "compressed wav rejected");
        Equal("unsupported-wav-encoding", compressedWavResult.FailureCode, "compressed wav failure code");
    
        var oggVorbis = System.Text.Encoding.ASCII.GetBytes("OggSxxxxxxxx\u0001vorbis");
        var oggResult = AudioFileFormatProbe.Probe(oggVorbis);
        Equal(true, oggResult.Success, "ogg vorbis detected");
        Equal(AudioFileFormat.OggVorbis, oggResult.Format, "ogg vorbis format");
        Equal(".ogg", oggResult.CanonicalExtension, "ogg canonical extension");
    
        var mislabeledPath = Path.Combine(Path.GetTempPath(), "audio-probe-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(mislabeledPath, oggVorbis);
            var mislabeled = AudioFileFormatProbe.Probe(mislabeledPath);
            Equal(AudioFileFormat.OggVorbis, mislabeled.Format, "content wins over file extension");
            Equal(".ogg", mislabeled.CanonicalExtension, "mislabeled file receives real extension");
        }
        finally
        {
            if (File.Exists(mislabeledPath))
            {
                File.Delete(mislabeledPath);
            }
        }
    
        var oggOpus = AudioFileFormatProbe.Probe(System.Text.Encoding.ASCII.GetBytes("OggSxxxxxxxxOpusHead"));
        Equal(false, oggOpus.Success, "ogg opus rejected");
        Equal(AudioFileFormat.OggOpus, oggOpus.Format, "ogg opus recognized");
        Equal("unsupported-ogg-opus", oggOpus.FailureCode, "ogg opus failure code");
    
        var iso = new byte[12];
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("ftyp"), 0, iso, 4, 4);
        var isoResult = AudioFileFormatProbe.Probe(iso);
        Equal(false, isoResult.Success, "iso base media rejected");
        Equal(AudioFileFormat.IsoBaseMedia, isoResult.Format, "iso base media recognized");
    
        var id3Only = AudioFileFormatProbe.Probe(new byte[]
        {
            (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0
        });
        Equal(false, id3Only.Success, "id3 without mp3 frame rejected");
        Equal(false, AudioFileFormatProbe.Probe(new byte[] { 1, 2, 3, 4 }).Success, "unknown bytes rejected");
    }
    
    private void VerifyHookCatalog()
    {
        var hooks = AudioHookCatalog.All;
        Equal(19, hooks.Count, "audio hook catalog count");
        Equal(19, hooks.Select(item => item.HandlerId).Distinct(StringComparer.Ordinal).Count(), "audio hook handler ids are unique");
        Equal(2, hooks.Count(item => item.RegistrationKind == AudioHookRegistrationKind.Before), "audio before hook count");
        Equal(16, hooks.Count(item => item.RegistrationKind == AudioHookRegistrationKind.After), "audio after hook count");
        Equal(1, hooks.Count(item => item.RegistrationKind == AudioHookRegistrationKind.CombatActionBefore), "audio combat router count");
        Equal(2, hooks.Count(item => item.Target == "Fight_Start.Init"), "fight start keeps before and after hooks");
        Equal(6, hooks.Count(item => item.Target.StartsWith("ScriptExecutor.", StringComparison.Ordinal)), "script HP hook count");
        Equal(2, hooks.Count(item => item.Target.StartsWith("StatusManager.set_", StringComparison.Ordinal)), "status HP setter hook count");
        Equal(6, hooks.Count(item => item.CallbackKind == AudioHookCallbackKind.PotentialHpChanged), "script HP hooks share one callback kind");
        Equal(2, hooks.Count(item => item.CallbackKind == AudioHookCallbackKind.StatusHpChanged), "status HP hooks share one callback kind");
        Equal(13, hooks.Select(item => item.CallbackKind).Distinct().Count(), "audio callback kind count");
    
        var combat = hooks.Single(item => item.HandlerId == "combat-action");
        Equal("FightUI.CallActionAnimation", combat.Target, "combat action hook target");
        Equal(AudioHookRegistrationKind.CombatActionBefore, combat.RegistrationKind, "combat action uses routed before hook");
        Equal(AudioHookCallbackKind.CombatActionBefore, combat.CallbackKind, "combat action callback kind");
        var effect = hooks.Single(item => item.HandlerId == "native-effect");
        Equal("EffectSound.Start", effect.Target, "native effect hook target");
        Equal(AudioHookRegistrationKind.Before, effect.RegistrationKind, "native effect runs before original playback");
        Equal(AudioHookCallbackKind.NativeEffectBefore, effect.CallbackKind, "native effect callback kind");
        var vocal = hooks.Single(item => item.HandlerId == "vocal-state");
        Equal("StatusManager.PlayVocal", vocal.Target, "vocal state hook target");
        Equal(AudioHookRegistrationKind.After, vocal.RegistrationKind, "vocal state is observed after native call");
        Equal(AudioHookCallbackKind.VocalState, vocal.CallbackKind, "vocal state callback kind");
        Equal(true, hooks.Any(item => item.HandlerId == "fight-win" && item.Target == "Fight_Win.ResetStates"), "fight win hook retained");
        Equal(true, hooks.Any(item => item.HandlerId == "fight-escape" && item.Target == "Fight_Escape.ResetStates"), "fight escape hook retained");
        Equal(AudioHookCallbackKind.FightWin, hooks.Single(item => item.HandlerId == "fight-win").CallbackKind, "fight win callback kind");
        Equal(AudioHookCallbackKind.FightEscape, hooks.Single(item => item.HandlerId == "fight-escape").CallbackKind, "fight escape callback kind");
    }
}
