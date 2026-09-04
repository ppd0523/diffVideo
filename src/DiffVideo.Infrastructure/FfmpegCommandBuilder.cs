using System.Globalization;
using System.Text;
using DiffVideo.Core;

namespace DiffVideo.Infrastructure;

public enum H264Encoder
{
    AmdAmf,
    MediaFoundation
}

public sealed record FfmpegInvocation(IReadOnlyList<string> Arguments, int PreviewWidth = 0, int PreviewHeight = 0);

public static class FfmpegCommandBuilder
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static FfmpegInvocation BuildExport(Composition composition, string outputPath, H264Encoder encoder,
        IReadOnlyList<LabelOverlayAsset>? labels = null)
    {
        var arguments = CreateInputArguments(composition);
        foreach (var label in labels ?? [])
        {
            arguments.AddRange(["-loop", "1", "-framerate", composition.Output.FramesPerSecond.ToString(Invariant), "-i", label.Path]);
        }
        var graph = BuildFilterGraph(composition, includeAudio: true, labels);
        arguments.AddRange(["-filter_complex", graph.FilterGraph, "-map", $"[{graph.VideoLabel}]", "-map", $"[{graph.AudioLabel}]"]);

        AddEncoderArguments(arguments, composition, encoder);
        arguments.AddRange([
            "-c:a", "aac",
            "-b:a", AudioBitrate(composition.Output.Quality),
            "-ar", "48000",
            "-ac", "2",
            "-movflags", "+faststart",
            "-t", Seconds(composition.Output.Duration.TotalSeconds),
            "-f", "mp4",
            "-progress", "pipe:2",
            "-nostats",
            outputPath
        ]);
        return new(arguments);
    }

    public static FfmpegInvocation BuildStillPreview(Composition composition, double atSeconds)
    {
        var arguments = CreateInputArguments(composition);
        var (width, height) = PreviewSize(composition.Output.Width, composition.Output.Height);
        var graph = BuildFilterGraph(composition, includeAudio: false);
        var filter = $"{graph.FilterGraph};[{graph.VideoLabel}]trim=start={Seconds(atSeconds)}:duration=0.1,setpts=PTS-STARTPTS,scale={width}:{height}:flags=fast_bilinear,format=rgba[preview]";
        arguments.AddRange([
            "-filter_complex", filter,
            "-map", "[preview]",
            "-frames:v", "1",
            "-an",
            "-c:v", "png",
            "-f", "image2pipe",
            "pipe:1"
        ]);
        return new(arguments, width, height);
    }

    public static FfmpegInvocation BuildPreviewStream(Composition composition, double fromSeconds)
    {
        var arguments = CreateInputArguments(composition);
        var (width, height) = PreviewSize(composition.Output.Width, composition.Output.Height);
        var graph = BuildFilterGraph(composition, includeAudio: false);
        var filter = $"{graph.FilterGraph};[{graph.VideoLabel}]trim=start={Seconds(fromSeconds)},setpts=PTS-STARTPTS,scale={width}:{height}:flags=fast_bilinear,format=bgra[preview]";
        arguments.AddRange([
            "-filter_complex", filter,
            "-map", "[preview]",
            "-an",
            "-pix_fmt", "bgra",
            "-f", "rawvideo",
            "pipe:1"
        ]);
        return new(arguments, width, height);
    }

    public static FfmpegInvocation BuildAudioPreview(Composition composition, double fromSeconds)
    {
        var arguments = CreateInputArguments(composition);
        var filters = BuildAudioFilters(composition);
        var remaining = Math.Max(0, composition.Output.Duration.TotalSeconds - fromSeconds);
        filters.Add($"[aout]atrim=start={Seconds(fromSeconds)}:duration={Seconds(remaining)},asetpts=PTS-STARTPTS[apreview]");
        arguments.AddRange([
            "-filter_complex", string.Join(';', filters),
            "-map", "[apreview]",
            "-vn",
            "-c:a", "pcm_s16le",
            "-ar", "48000",
            "-ac", "2",
            "-f", "s16le",
            "pipe:1"
        ]);
        return new(arguments);
    }

    private static List<string> CreateInputArguments(Composition composition)
    {
        var arguments = new List<string> { "-hide_banner", "-nostdin" };
        arguments.AddRange(["-i", composition.Video1.Media.Path]);
        arguments.AddRange(["-i", composition.Video2.Media.Path]);
        if (composition.ExtraAudio is { } audio)
        {
            arguments.AddRange(["-i", audio.Media.Path]);
        }

        return arguments;
    }

    private static (string FilterGraph, string VideoLabel, string AudioLabel) BuildFilterGraph(Composition composition, bool includeAudio,
        IReadOnlyList<LabelOverlayAsset>? labels = null)
    {
        var output = composition.Output;
        var filters = new List<string>
        {
            $"color=c=black:s={output.Width}x{output.Height}:r={output.FramesPerSecond}:d={Seconds(output.Duration.TotalSeconds)}[base]"
        };

        var tracks = new[] { (Track: composition.Video1, Input: 0), (Track: composition.Video2, Input: 1) };
        foreach (var item in tracks)
        {
            filters.Add(BuildVideoFilter(item.Track, item.Input, output));
        }

        var currentVideo = "base";
        var layer = 0;
        foreach (var item in tracks.OrderBy(item => item.Track.ZIndex))
        {
            var video = $"v{item.Input}";
            var labelIndex = labels?.ToList().FindIndex(label => label.VideoIndex == item.Input) ?? -1;
            if (labelIndex >= 0)
            {
                var label = labels![labelIndex];
                var input = (composition.ExtraAudio is null ? 2 : 3) + labelIndex;
                filters.Add($"[{video}][{input}:v]overlay=x={label.X}:y={label.Y}:shortest=1:eof_action=repeat[labeled{item.Input}]");
                video = $"labeled{item.Input}";
            }
            var next = $"layer{layer++}";
            filters.Add($"[{currentVideo}][{video}]overlay=x={item.Track.Destination.X}:y={item.Track.Destination.Y}:shortest=0:eof_action=pass[{next}]");
            currentVideo = next;
        }

        filters.Add($"[{currentVideo}]fps={output.FramesPerSecond},format=yuv420p,trim=duration={Seconds(output.Duration.TotalSeconds)},setpts=PTS-STARTPTS[vout]");

        if (!includeAudio)
        {
            return (string.Join(';', filters), "vout", "");
        }

        filters.AddRange(BuildAudioFilters(composition));
        return (string.Join(';', filters), "vout", "aout");
    }

    private static List<string> BuildAudioFilters(Composition composition)
    {
        var output = composition.Output;
        var filters = new List<string>();
        var audioLabels = new List<string>();
        AddAudioFilter(filters, audioLabels, composition.Video1, 0, output.Duration);
        AddAudioFilter(filters, audioLabels, composition.Video2, 1, output.Duration);

        if (composition.ExtraAudio is { IncludeAudio: true } extraAudio)
        {
            var index = 2;
            var available = Math.Max(0, Math.Min(extraAudio.Media.Duration.TotalSeconds, output.Duration.TotalSeconds - extraAudio.Start.TotalSeconds));
            if (available > 0)
            {
                var fadeOut = Math.Max(0, available - 0.01);
                filters.Add($"[{index}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={Number(extraAudio.Volume)},atrim=duration={Seconds(available)},asetpts=PTS-STARTPTS,afade=t=in:st=0:d=0.01,afade=t=out:st={Seconds(fadeOut)}:d=0.01,adelay={Milliseconds(extraAudio.Start)}:all=1[amp3]");
                audioLabels.Add("amp3");
            }
        }

        if (audioLabels.Count == 0)
        {
            filters.Add($"anullsrc=r=48000:cl=stereo:d={Seconds(output.Duration.TotalSeconds)}[aout]");
        }
        else
        {
            var inputs = string.Concat(audioLabels.Select(label => $"[{label}]"));
            filters.Add($"{inputs}amix=inputs={audioLabels.Count}:duration=longest:normalize=0,alimiter=limit=0.95:attack=5:release=50,apad=whole_dur={Seconds(output.Duration.TotalSeconds)},atrim=duration={Seconds(output.Duration.TotalSeconds)},asetpts=PTS-STARTPTS[aout]");
        }

        return filters;
    }

    private static string BuildVideoFilter(VideoTrack track, int inputIndex, OutputSettings output)
    {
        var roi = track.Roi;
        var destination = track.Destination;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"[{inputIndex}:v:0]crop={roi.Width}:{roi.Height}:{roi.X}:{roi.Y},");

        if (track.Media.IsHdr)
        {
            builder.Append("zscale=transfer=linear:npl=100,format=gbrpf32le,zscale=primaries=bt709,tonemap=tonemap=hable:desat=0,zscale=transfer=bt709:matrix=bt709:range=tv,format=yuv420p,");
        }

        builder.Append(track.FitMode switch
        {
            VideoFitMode.Fit => $"scale={destination.Width}:{destination.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={destination.Width}:{destination.Height}:(ow-iw)/2:(oh-ih)/2:black,",
            VideoFitMode.Fill => $"scale={destination.Width}:{destination.Height}:force_original_aspect_ratio=increase,crop={destination.Width}:{destination.Height},",
            _ => $"scale={destination.Width}:{destination.Height},"
        });

        var start = track.Start.TotalSeconds;
        var sourceDuration = track.Media.Duration.TotalSeconds;
        var stop = Math.Max(0, output.Duration.TotalSeconds - start - sourceDuration);
        builder.Append(CultureInfo.InvariantCulture, $"setpts=PTS-STARTPTS,tpad=start_mode=clone:start_duration={Seconds(start)}:stop_mode=clone:stop_duration={Seconds(stop)},trim=duration={Seconds(output.Duration.TotalSeconds)},setpts=PTS-STARTPTS[v{inputIndex}]");
        return builder.ToString();
    }

    private static void AddAudioFilter(List<string> filters, List<string> labels, VideoTrack track, int inputIndex, TimeSpan outputDuration)
    {
        if (!track.IncludeAudio || !track.Media.HasAudio)
        {
            return;
        }

        var available = Math.Max(0, Math.Min(track.Media.Duration.TotalSeconds, outputDuration.TotalSeconds - track.Start.TotalSeconds));
        if (available <= 0)
        {
            return;
        }

        var label = $"a{inputIndex}";
        var fadeOut = Math.Max(0, available - 0.01);
        filters.Add($"[{inputIndex}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={Number(track.Volume)},atrim=duration={Seconds(available)},asetpts=PTS-STARTPTS,afade=t=in:st=0:d=0.01,afade=t=out:st={Seconds(fadeOut)}:d=0.01,adelay={Milliseconds(track.Start)}:all=1[{label}]");
        labels.Add(label);
    }

    private static void AddEncoderArguments(List<string> arguments, Composition composition, H264Encoder encoder)
    {
        var bitrate = VideoBitrate(composition.Output);
        arguments.AddRange(["-pix_fmt", "yuv420p", "-r", composition.Output.FramesPerSecond.ToString(Invariant)]);
        if (encoder == H264Encoder.AmdAmf)
        {
            arguments.AddRange(["-c:v", "h264_amf", "-usage", "transcoding", "-quality", "balanced", "-rc", "vbr_peak"]);
        }
        else
        {
            arguments.AddRange(["-c:v", "h264_mf", "-rate_control", "pc_vbr"]);
        }

        arguments.AddRange(["-b:v", bitrate.ToString(Invariant), "-maxrate", ((long)(bitrate * 1.5)).ToString(Invariant), "-bufsize", (bitrate * 2).ToString(Invariant)]);
    }

    private static long VideoBitrate(OutputSettings output)
    {
        var bitsPerPixel = output.Quality switch
        {
            OutputQuality.High => 0.12,
            OutputQuality.Small => 0.05,
            _ => 0.08
        };
        var estimated = (long)(output.Width * (double)output.Height * output.FramesPerSecond * bitsPerPixel);
        return Math.Clamp(estimated, 1_000_000, 50_000_000);
    }

    private static string AudioBitrate(OutputQuality quality) => quality switch
    {
        OutputQuality.High => "256k",
        OutputQuality.Small => "128k",
        _ => "192k"
    };

    private static (int Width, int Height) PreviewSize(int width, int height)
    {
        var ratio = Math.Min(960d / width, 540d / height);
        ratio = Math.Min(1, ratio);
        var previewWidth = Math.Max(2, (int)Math.Round(width * ratio) / 2 * 2);
        var previewHeight = Math.Max(2, (int)Math.Round(height * ratio) / 2 * 2);
        return (previewWidth, previewHeight);
    }

    private static string Number(double value) => value.ToString("0.######", Invariant);

    private static string Seconds(double value) => Math.Max(0, value).ToString("0.######", Invariant);

    private static string Milliseconds(TimeSpan value) => Math.Max(0, (long)Math.Round(value.TotalMilliseconds)).ToString(Invariant);
}
