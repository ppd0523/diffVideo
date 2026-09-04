using System.Globalization;
using System.Text.Json;
using DiffVideo.Core;

namespace DiffVideo.Infrastructure;

public sealed class MediaProbeService(FfmpegPaths paths)
{
    private readonly FfmpegPaths _paths = paths;

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("미디어 파일을 찾을 수 없습니다.", path);
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("MP4 영상과 MP3 음원만 지원합니다.");
        }

        var result = await ProcessRunner.RunTextAsync(
            _paths.Ffprobe,
            ["-v", "error", "-show_streams", "-show_format", "-of", "json", path],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"미디어 정보를 읽을 수 없습니다: {LastMeaningfulLine(result.StandardError)}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "video");
        var audio = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "audio");
        var format = root.GetProperty("format");
        var duration = ParseDouble(GetString(format, "duration"));
        var container = GetString(format, "format_name") ?? "";
        var containerNames = container.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
            !containerNames.Contains("mp4", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("확장자는 MP4이지만 실제 파일 컨테이너가 MP4가 아닙니다.");
        }

        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
            !containerNames.Contains("mp3", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("확장자는 MP3이지만 실제 파일 형식이 MP3가 아닙니다.");
        }

        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            if (audio.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("MP3 파일에 재생 가능한 오디오 스트림이 없습니다.");
            }

            return new(
                path,
                MediaKind.Audio,
                container,
                GetString(audio, "codec_name") ?? "unknown",
                TimeSpan.FromSeconds(duration),
                AudioCodec: GetString(audio, "codec_name") ?? "unknown",
                AudioSampleRate: ParseInt(GetString(audio, "sample_rate")),
                AudioChannels: GetInt(audio, "channels"));
        }

        if (video.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException("MP4 파일에 재생 가능한 영상 스트림이 없습니다.");
        }

        var rotation = ReadRotation(video);
        var colorTransfer = GetString(video, "color_transfer") ?? "";
        var frameRate = ParseFrameRate(GetString(video, "avg_frame_rate") ?? GetString(video, "r_frame_rate"));

        return new(
            path,
            MediaKind.Video,
            container,
            GetString(video, "codec_name") ?? "unknown",
            TimeSpan.FromSeconds(duration),
            GetInt(video, "width"),
            GetInt(video, "height"),
            frameRate,
            audio.ValueKind != JsonValueKind.Undefined,
            audio.ValueKind == JsonValueKind.Undefined ? "" : GetString(audio, "codec_name") ?? "unknown",
            audio.ValueKind == JsonValueKind.Undefined ? 0 : ParseInt(GetString(audio, "sample_rate")),
            audio.ValueKind == JsonValueKind.Undefined ? 0 : GetInt(audio, "channels"),
            rotation,
            colorTransfer is "smpte2084" or "arib-std-b67",
            TimeSpan.FromSeconds(ParseDouble(GetString(video, "duration")) is > 0 and var videoDuration ? videoDuration : duration));
    }

    private static int ReadRotation(JsonElement video)
    {
        if (video.TryGetProperty("tags", out var tags) &&
            tags.TryGetProperty("rotate", out var rotate) &&
            int.TryParse(rotate.GetString(), out var tagRotation))
        {
            return tagRotation;
        }

        if (video.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotation) && rotation.TryGetInt32(out var value))
                {
                    return value;
                }
            }
        }

        return 0;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;

    private static int ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static double ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var parts = value.Split('/');
        if (parts.Length == 2)
        {
            var numerator = ParseDouble(parts[0]);
            var denominator = ParseDouble(parts[1]);
            return denominator == 0 ? 0 : numerator / denominator;
        }

        return ParseDouble(value);
    }

    private static string LastMeaningfulLine(string error) =>
        error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "알 수 없는 오류";
}
