using System.Diagnostics;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.Core.Tests;

public sealed class FfmpegIntegrationTests
{
    [Fact]
    public async Task Export_CreatesExpectedDurationH264AacMp4()
    {
        var ffmpegBin = Environment.GetEnvironmentVariable("DIFFVIDEO_FFMPEG_BIN");
        if (string.IsNullOrWhiteSpace(ffmpegBin))
        {
            return;
        }

        var paths = new FfmpegPaths(Path.Combine(ffmpegBin, "ffmpeg.exe"), Path.Combine(ffmpegBin, "ffprobe.exe"));
        Assert.True(File.Exists(paths.Ffmpeg));
        Assert.True(File.Exists(paths.Ffprobe));

        var temp = Path.Combine(Path.GetTempPath(), "DiffVideo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var video1Path = Path.Combine(temp, "one.mp4");
            var video2Path = Path.Combine(temp, "two.mp4");
            var mp3Path = Path.Combine(temp, "music.mp3");
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=red:size=640x360:rate=30:d=3", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac", "-shortest", video1Path]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=green:size=640x360:rate=30:d=2", "-f", "lavfi", "-i", "sine=frequency=660:duration=2", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac", "-shortest", video2Path]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "sine=frequency=220:duration=4", "-c:a", "libmp3lame", mp3Path]);

            var probe = new MediaProbeService(paths);
            var media1 = await probe.ProbeAsync(video1Path);
            var media2 = await probe.ProbeAsync(video2Path);
            var music = await probe.ProbeAsync(mp3Path);
            var composition = new Composition(
                new(media1, TimeSpan.Zero, PixelRect.FullFrame(media1), new(0, 0, 320, 360), VideoFitMode.Fit, true, 0, true, 1),
                new(media2, TimeSpan.FromSeconds(1), PixelRect.FullFrame(media2), new(320, 0, 320, 360), VideoFitMode.Fill, true, 1, true, 1),
                new(music, TimeSpan.FromSeconds(0.5), true, 0.3),
                new(640, 360, 30, TimeSpan.FromSeconds(5), OutputQuality.Balanced));

            var outputPath = Path.Combine(temp, "merged.mp4");
            var result = await new FfmpegExportService(paths).ExportAsync(composition, outputPath);
            var output = await probe.ProbeAsync(outputPath);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("h264", output.Codec);
            Assert.Equal("aac", output.AudioCodec);
            Assert.Equal(640, output.DisplayWidth);
            Assert.Equal(360, output.DisplayHeight);
            Assert.InRange(output.Duration.TotalSeconds, 4.95, 5.05);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Export_AudioStartOffsetIsWithinTenMilliseconds()
    {
        var ffmpegBin = Environment.GetEnvironmentVariable("DIFFVIDEO_FFMPEG_BIN");
        if (string.IsNullOrWhiteSpace(ffmpegBin))
        {
            return;
        }

        var paths = new FfmpegPaths(Path.Combine(ffmpegBin, "ffmpeg.exe"), Path.Combine(ffmpegBin, "ffprobe.exe"));
        var temp = Path.Combine(Path.GetTempPath(), "DiffVideo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var onePath = Path.Combine(temp, "timed-audio.mp4");
            var twoPath = Path.Combine(temp, "silent-video.mp4");
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=red:size=320x180:rate=30:d=2", "-f", "lavfi", "-i", "aevalsrc=if(lt(t\\,0.5)\\,0\\,0.8*sin(2*PI*1000*t)):s=48000:d=2", "-c:v", "mpeg4", "-q:v", "5", "-c:a", "aac", "-shortest", onePath]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=blue:size=320x180:rate=30:d=2", "-c:v", "mpeg4", "-q:v", "5", twoPath]);

            var probe = new MediaProbeService(paths);
            var one = await probe.ProbeAsync(onePath);
            var two = await probe.ProbeAsync(twoPath);
            var composition = new Composition(
                new(one, TimeSpan.FromSeconds(1), PixelRect.FullFrame(one), new(0, 0, 320, 180), VideoFitMode.Fit, true, 0, true, 1),
                new(two, TimeSpan.Zero, PixelRect.FullFrame(two), new(320, 0, 320, 180), VideoFitMode.Fit, true, 1, false, 1),
                null,
                new(640, 180, 30, TimeSpan.FromSeconds(3), OutputQuality.Small));

            var outputPath = Path.Combine(temp, "timed-output.mp4");
            await new FfmpegExportService(paths).ExportAsync(composition, outputPath);
            var pcmPath = Path.Combine(temp, "audio.pcm");
            await RunAsync(paths.Ffmpeg, ["-y", "-i", outputPath, "-map", "0:a:0", "-ac", "1", "-ar", "48000", "-f", "s16le", pcmPath]);

            var pcm = await File.ReadAllBytesAsync(pcmPath);
            var firstAudibleSample = FirstSampleAbove(pcm, 2000);
            var expected = (int)(1.5 * 48000);
            Assert.InRange(firstAudibleSample, expected - 480, expected + 480);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Export_CancellationRemovesPartialFile()
    {
        var ffmpegBin = Environment.GetEnvironmentVariable("DIFFVIDEO_FFMPEG_BIN");
        if (string.IsNullOrWhiteSpace(ffmpegBin))
        {
            return;
        }

        var paths = new FfmpegPaths(Path.Combine(ffmpegBin, "ffmpeg.exe"), Path.Combine(ffmpegBin, "ffprobe.exe"));
        var temp = Path.Combine(Path.GetTempPath(), "DiffVideo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var onePath = Path.Combine(temp, "cancel-one.mp4");
            var twoPath = Path.Combine(temp, "cancel-two.mp4");
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=red:size=320x180:rate=30:d=1", "-c:v", "mpeg4", "-q:v", "5", onePath]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=blue:size=320x180:rate=30:d=1", "-c:v", "mpeg4", "-q:v", "5", twoPath]);

            var probe = new MediaProbeService(paths);
            var one = await probe.ProbeAsync(onePath);
            var two = await probe.ProbeAsync(twoPath);
            var composition = new Composition(
                new(one, TimeSpan.Zero, PixelRect.FullFrame(one), new(0, 0, 960, 1080), VideoFitMode.Fill, true, 0, false, 1),
                new(two, TimeSpan.Zero, PixelRect.FullFrame(two), new(960, 0, 960, 1080), VideoFitMode.Fill, true, 1, false, 1),
                null,
                new(1920, 1080, 60, TimeSpan.FromSeconds(60), OutputQuality.High));

            var outputPath = Path.Combine(temp, "cancelled.mp4");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => new FfmpegExportService(paths).ExportAsync(composition, outputPath, cancellationToken: cancellation.Token));

            Assert.False(File.Exists(outputPath));
            Assert.False(File.Exists(outputPath + ".part"));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Stress_TenMinute1080p30ExportSucceedsTwice()
    {
        var ffmpegBin = Environment.GetEnvironmentVariable("DIFFVIDEO_FFMPEG_BIN");
        var runStressTest = Environment.GetEnvironmentVariable("DIFFVIDEO_RUN_STRESS_TEST");
        if (string.IsNullOrWhiteSpace(ffmpegBin) || runStressTest != "1")
        {
            return;
        }

        var paths = new FfmpegPaths(Path.Combine(ffmpegBin, "ffmpeg.exe"), Path.Combine(ffmpegBin, "ffprobe.exe"));
        var temp = Path.Combine(Path.GetTempPath(), "DiffVideo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var onePath = Path.Combine(temp, "stress-one.mp4");
            var twoPath = Path.Combine(temp, "stress-two.mp4");
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "testsrc2=size=960x1080:rate=30:d=2", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-c:v", "mpeg4", "-q:v", "5", "-c:a", "aac", "-shortest", onePath]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "testsrc2=size=960x1080:rate=30:d=2", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000:duration=2", "-vf", "hue=h=90", "-c:v", "mpeg4", "-q:v", "5", "-c:a", "aac", "-shortest", twoPath]);

            var probe = new MediaProbeService(paths);
            var one = await probe.ProbeAsync(onePath);
            var two = await probe.ProbeAsync(twoPath);
            var composition = new Composition(
                new(one, TimeSpan.Zero, PixelRect.FullFrame(one), new(0, 0, 960, 1080), VideoFitMode.Fill, true, 0, true, 1),
                new(two, TimeSpan.FromSeconds(1), PixelRect.FullFrame(two), new(960, 0, 960, 1080), VideoFitMode.Fill, true, 1, true, 1),
                null,
                new(1920, 1080, 30, TimeSpan.FromMinutes(10), OutputQuality.Small));

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var outputPath = Path.Combine(temp, $"stress-output-{attempt}.mp4");
                await new FfmpegExportService(paths).ExportAsync(composition, outputPath);
                var output = await probe.ProbeAsync(outputPath);
                Assert.Equal(1920, output.DisplayWidth);
                Assert.Equal(1080, output.DisplayHeight);
                Assert.InRange(output.Duration.TotalSeconds, 599.95, 600.05);
                File.Delete(outputPath);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }

    private static int FirstSampleAbove(byte[] pcm, short threshold)
    {
        for (var index = 0; index + 1 < pcm.Length; index += 2)
        {
            var sample = BitConverter.ToInt16(pcm, index);
            if (Math.Abs((int)sample) >= threshold)
            {
                return index / 2;
            }
        }

        return -1;
    }
}
