using System;
using System.Globalization;
using System.IO;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoEncodingPolicy
{
    internal static string BuildFfmpegArguments(
        int framesPerSecond,
        string? wavePath,
        string outputPath)
    {
        var fps = Math.Max(1, Math.Min(120, framesPerSecond));
        var audio = !string.IsNullOrWhiteSpace(wavePath) && File.Exists(wavePath);
        return "-hide_banner -loglevel error -y -f image2pipe -vcodec mjpeg -framerate "
               + fps.ToString(CultureInfo.InvariantCulture) + " -i pipe:0 "
               + (audio ? "-i \"" + wavePath + "\" -shortest -c:a aac -b:a 160k " : "-an ")
               + "-vf \"scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p\" "
               + "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p "
               + "-color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 "
               + "-x264-params \"colorprim=bt709:transfer=bt709:colormatrix=bt709:fullrange=off\" "
               + "-movflags +faststart \"" + outputPath + "\"";
    }
}
