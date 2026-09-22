namespace DiffVideo.Core;

public sealed record ValidationIssue(string Code, string Message);

public static class CompositionValidator
{
    public const int MinimumCanvasDimension = 16;
    public const int MaximumCanvasWidth = 3840;
    public const int MaximumCanvasHeight = 2160;
    public const int MaximumFramesPerSecond = 60;

    public static IReadOnlyList<ValidationIssue> Validate(Composition composition)
    {
        var issues = new List<ValidationIssue>();
        ValidateOutput(composition.Output, issues);
        if (composition.Videos.Count == 0 && composition.Audios.Count == 0)
        {
            issues.Add(new("COMPOSITION_EMPTY", "합성하려면 미디어가 최소 한 개 필요합니다."));
        }

        for (var index = 0; index < composition.Videos.Count; index++)
        {
            ValidateVideo(composition.Videos[index], $"VIDEO_{index + 1}", issues);
        }

        for (var index = 0; index < composition.Audios.Count; index++)
        {
            var audio = composition.Audios[index];
            var prefix = $"AUDIO_{index + 1}";
            if (audio.Media.Kind != MediaKind.Audio)
            {
                issues.Add(new($"{prefix}_KIND", "음원 트랙에는 MP3 오디오 파일만 사용할 수 있습니다."));
            }

            if (audio.Start < TimeSpan.Zero)
            {
                issues.Add(new($"{prefix}_START", "음원 시작 시각은 0초 이상이어야 합니다."));
            }

            ValidateVolume(audio.Volume, $"{prefix}_VOLUME", issues);
        }

        return issues;
    }

    public static int NormalizeEvenDimension(int value, int minimum, int maximum)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped % 2 == 0)
        {
            return clamped;
        }

        return clamped == maximum ? clamped - 1 : clamped + 1;
    }

    private static void ValidateOutput(OutputSettings output, ICollection<ValidationIssue> issues)
    {
        if (output.Width is < MinimumCanvasDimension or > MaximumCanvasWidth || output.Width % 2 != 0)
        {
            issues.Add(new("OUTPUT_WIDTH", $"캔버스 너비는 {MinimumCanvasDimension}~{MaximumCanvasWidth} 범위의 짝수여야 합니다."));
        }

        if (output.Height is < MinimumCanvasDimension or > MaximumCanvasHeight || output.Height % 2 != 0)
        {
            issues.Add(new("OUTPUT_HEIGHT", $"캔버스 높이는 {MinimumCanvasDimension}~{MaximumCanvasHeight} 범위의 짝수여야 합니다."));
        }

        if (output.FramesPerSecond is < 1 or > MaximumFramesPerSecond)
        {
            issues.Add(new("OUTPUT_FPS", $"출력 FPS는 1~{MaximumFramesPerSecond} 범위여야 합니다."));
        }

        if (output.Duration <= TimeSpan.Zero)
        {
            issues.Add(new("OUTPUT_DURATION", "출력 종료 시각은 0초보다 커야 합니다."));
        }
        if (output.ExportStart < TimeSpan.Zero || output.EffectiveExportEnd > output.Duration ||
            output.EffectiveExportEnd <= output.ExportStart)
        {
            issues.Add(new("EXPORT_RANGE", "내보내기 구간은 전체 타임라인 안에서 시작보다 종료가 뒤에 있어야 합니다."));
        }
    }

    private static void ValidateVideo(VideoTrack track, string prefix, ICollection<ValidationIssue> issues)
    {
        if (track.Media.Kind != MediaKind.Video)
        {
            issues.Add(new($"{prefix}_KIND", "영상 트랙에는 MP4 영상만 사용할 수 있습니다."));
        }

        if (track.Start < TimeSpan.Zero)
        {
            issues.Add(new($"{prefix}_START", "영상 시작 시각은 0초 이상이어야 합니다."));
        }

        var roi = track.Roi;
        if (roi.Width < 2 || roi.Height < 2 || roi.X < 0 || roi.Y < 0 ||
            (long)roi.X + roi.Width > track.Media.DisplayWidth || (long)roi.Y + roi.Height > track.Media.DisplayHeight)
        {
            issues.Add(new($"{prefix}_ROI", "ROI는 영상 프레임 안에 있어야 합니다."));
        }

        var destination = track.Destination;
        if (destination.Width <= 0 || destination.Height <= 0 ||
            (long)destination.X + destination.Width > int.MaxValue || (long)destination.Y + destination.Height > int.MaxValue)
        {
            issues.Add(new($"{prefix}_DESTINATION", "영상 배치 크기는 양수이며 좌표 계산 범위 안에 있어야 합니다."));
        }

        ValidateVolume(track.Volume, $"{prefix}_VOLUME", issues);
    }

    private static void ValidateVolume(double volume, string code, ICollection<ValidationIssue> issues)
    {
        if (volume is < 0 or > 2)
        {
            issues.Add(new(code, "오디오 볼륨은 0~200% 범위여야 합니다."));
        }
    }
}
