namespace DiffVideo.Infrastructure;

public sealed record FfmpegPaths(string Ffmpeg, string Ffprobe)
{
    public static FfmpegPaths Discover(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var candidates = new[]
        {
            root,
            Path.Combine(root, "ffmpeg"),
            Path.Combine(root, "tools", "ffmpeg", "bin"),
            Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "tools", "ffmpeg", "bin"))
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ffmpeg = Path.Combine(candidate, "ffmpeg.exe");
            var ffprobe = Path.Combine(candidate, "ffprobe.exe");
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                return new(ffmpeg, ffprobe);
            }
        }

        throw new FileNotFoundException(
            "ffmpeg.exe와 ffprobe.exe를 찾을 수 없습니다. portable 폴더에 두 파일을 넣어 주세요.");
    }
}
