using AuraAudio.Shared;
using UnityEngine;

namespace AudioArbiter.Shared;

internal static class AudioUnityFileLoadPolicy
{
    public static bool TryResolve(AudioFileFormatDescriptor descriptor, out AudioType audioType)
    {
        switch (descriptor.Format)
        {
            case AudioFileFormat.Mp3:
                audioType = AudioType.MPEG;
                return true;
            case AudioFileFormat.WavPcm:
            case AudioFileFormat.WavIeeeFloat:
                audioType = AudioType.WAV;
                return true;
            case AudioFileFormat.OggVorbis:
                audioType = AudioType.OGGVORBIS;
                return true;
            default:
                audioType = AudioType.UNKNOWN;
                return false;
        }
    }
}
