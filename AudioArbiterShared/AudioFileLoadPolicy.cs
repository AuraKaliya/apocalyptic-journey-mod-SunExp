using System.IO;

namespace AudioArbiter.Shared;

internal enum AudioFileEncoding
{
    Mpeg,
    Wav,
    OggVorbis,
    UnsupportedVideoContainer
}

internal static class AudioFileLoadPolicy
{
    public static AudioFileEncoding Classify(string? path)
    {
        switch (Path.GetExtension(path ?? "").ToLowerInvariant())
        {
            case ".wav":
                return AudioFileEncoding.Wav;
            case ".ogg":
                return AudioFileEncoding.OggVorbis;
            case ".mp4":
            case ".m4v":
            case ".mov":
                return AudioFileEncoding.UnsupportedVideoContainer;
            case ".m4a":
            case ".aac":
            case ".mp3":
            default:
                return AudioFileEncoding.Mpeg;
        }
    }
}
