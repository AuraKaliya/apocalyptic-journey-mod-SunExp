using System;
using System.Globalization;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoEncodingPolicy
{
    internal const string CodecProfileId = "mp4-mpeg4-aac-bt709.v1";
    internal const string ImportedCodecProfileId = "mp4-mpeg4-aac-bt709.import.v1";

    internal static string BuildFfmpegArguments(
        int width,
        int height,
        int framesPerSecond,
        string? wavePath,
        string outputPath)
    {
        var safeWidth = Math.Max(16, width);
        var safeHeight = Math.Max(16, height);
        var fps = Math.Max(1, Math.Min(60, framesPerSecond));
        var audio = !string.IsNullOrWhiteSpace(wavePath);
        return "-hide_banner -loglevel error -nostdin -y "
               + "-f rawvideo -pixel_format rgb24 -video_size "
               + safeWidth.ToString(CultureInfo.InvariantCulture) + "x"
               + safeHeight.ToString(CultureInfo.InvariantCulture)
               + " -framerate " + fps.ToString(CultureInfo.InvariantCulture) + " -i pipe:0 "
               + (audio ? "-i " + Quote(wavePath!) + " -shortest -c:a aac -b:a 160k " : "-an ")
               + "-vf vflip,scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p,"
               + "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709 "
               + "-c:v mpeg4 -q:v 3 -pix_fmt yuv420p "
               + "-color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 "
               + "-movflags +faststart+write_colr -f mp4 " + Quote(outputPath);
    }

    internal static string BuildFfprobeArguments(string path)
    {
        return "-v error -count_frames -show_entries "
               + "format=format_name,duration:stream=index,codec_type,codec_name,width,height,r_frame_rate,nb_read_frames,sample_rate,channels,duration,pix_fmt,color_range,color_space,color_transfer,color_primaries "
               + "-of json " + Quote(path);
    }

    internal static string BuildDecodeArguments(string path)
    {
        return "-hide_banner -v error -i " + Quote(path) + " -f null NUL";
    }

    internal static string BuildNormalizeArguments(string inputPath, string outputPath)
    {
        return "-hide_banner -loglevel error -nostdin -y -i " + Quote(inputPath)
               + " -map 0:v:0 -map 0:a:0? -sn -dn -fps_mode cfr "
               + "-vf fps=30,scale=trunc(iw/2)*2:trunc(ih/2)*2:in_range=auto:out_range=tv:out_color_matrix=bt709,format=yuv420p,"
               + "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709 "
               + "-c:v mpeg4 -q:v 3 -pix_fmt yuv420p -c:a aac -b:a 160k -ar 48000 -ac 2 "
               + "-color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 "
               + "-movflags +faststart+write_colr -f mp4 " + Quote(outputPath);
    }

    internal static string Quote(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }
}
