using System.Diagnostics;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.Core.Tests;

public sealed class FfmpegIntegrationTests
{
    [Fact]
    public async Task Export_SelectedRangePreservesSourceTimeAudioOffsetAndExactFrameCount()
    {
        var ffmpegBin = Environment.GetEnvironmentVariable("DIFFVIDEO_FFMPEG_BIN");
        if (string.IsNullOrWhiteSpace(ffmpegBin)) { return; }
        var paths = new FfmpegPaths(Path.Combine(ffmpegBin, "ffmpeg.exe"), Path.Combine(ffmpegBin, "ffprobe.exe"));
        var temp = Path.Combine(Path.GetTempPath(), "DiffVideo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var sourcePath = Path.Combine(temp, "red-then-blue.mp4");
            var audioPath = Path.Combine(temp, "tone.mp3");
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=red:s=160x90:r=30:d=1", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=30:d=2", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]", "-c:v", "mpeg4", sourcePath]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-c:a", "libmp3lame", audioPath]);
            var probe = new MediaProbeService(paths);
            var media = await probe.ProbeAsync(sourcePath);
            var audio = await probe.ProbeAsync(audioPath);
            var composition = new Composition(
                [
                    new(media, TimeSpan.Zero, PixelRect.FullFrame(media), new(0, 0, 160, 90), VideoFitMode.Stretch, false, 0, false, 1),
                    new(media, TimeSpan.FromSeconds(0.5), PixelRect.FullFrame(media), new(-160, 0, 160, 90), VideoFitMode.Stretch, false, 1, false, 1)
                ],
                [new(audio, TimeSpan.FromSeconds(1.5), true, 1)],
                new(160, 90, 30, TimeSpan.FromSeconds(3), OutputQuality.Balanced)
                { ExportStart = TimeSpan.FromSeconds(1), ExportEnd = TimeSpan.FromSeconds(2) });
            var outputPath = Path.Combine(temp, "range.mp4");
            await new FfmpegExportService(paths).ExportAsync(composition, outputPath);
            var output = await probe.ProbeAsync(outputPath);
            Assert.InRange(output.Duration.TotalSeconds, 0.99, 1.02);
            var rgbPath = Path.Combine(temp, "range.rgb");
            await RunAsync(paths.Ffmpeg, ["-y", "-i", outputPath, "-an", "-pix_fmt", "rgb24", "-f", "rawvideo", rgbPath]);
            var rgb = await File.ReadAllBytesAsync(rgbPath);
            Assert.Equal(160 * 90 * 3 * 30, rgb.Length);
            Assert.True(rgb[0] < 30 && rgb[2] > 200, "First saved frame comes from source second 1 (blue), not source second 0 (red)");
            var pcmPath = Path.Combine(temp, "range.pcm");
            await RunAsync(paths.Ffmpeg, ["-y", "-i", outputPath, "-vn", "-ar", "48000", "-ac", "1", "-f", "s16le", pcmPath]);
            var pcm = await File.ReadAllBytesAsync(pcmPath);
            Assert.InRange(FirstSampleAbove(pcm, 300) / 48000d, 0.48, 0.53);

            var oneFrame = composition with { Output = composition.Output with { ExportStart = TimeSpan.FromSeconds(1d / 30), ExportEnd = TimeSpan.FromSeconds(2d / 30) } };
            var oneFramePath = Path.Combine(temp, "one-frame.mp4");
            await new FfmpegExportService(paths).ExportAsync(oneFrame, oneFramePath);
            await RunAsync(paths.Ffmpeg, ["-y", "-i", oneFramePath, "-an", "-pix_fmt", "rgb24", "-f", "rawvideo", rgbPath]);
            Assert.Equal(160 * 90 * 3, new FileInfo(rgbPath).Length);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

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
                [
                    new(media1, TimeSpan.Zero, PixelRect.FullFrame(media1), new(0, 0, 320, 360), VideoFitMode.Fit, true, 0, true, 1),
                    new(media2, TimeSpan.FromSeconds(1), PixelRect.FullFrame(media2), new(320, 0, 320, 360), VideoFitMode.Fill, true, 1, true, 1)
                ],
                [new(music, TimeSpan.FromSeconds(0.5), true, 0.3)],
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

            // Red extends beyond the upper-left; green extends beyond the lower-right.
            // Export must clip the rectangles instead of rejecting or moving them.
            var clippedComposition = composition
                .WithVideo(0, composition.Videos[0] with { Destination = new(-160, -180, 320, 360), FitMode = VideoFitMode.Stretch })
                .WithVideo(1, composition.Videos[1] with { Destination = new(480, 180, 320, 360), FitMode = VideoFitMode.Stretch })
                with { Output = composition.Output with { Duration = TimeSpan.FromSeconds(1) } };
            var clippedPath = Path.Combine(temp, "clipped.mp4");
            await new FfmpegExportService(paths).ExportAsync(clippedComposition, clippedPath);
            var pixelsPath = Path.Combine(temp, "clipped.rgb");
            await RunAsync(paths.Ffmpeg, ["-y", "-i", clippedPath, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", pixelsPath]);
            var pixels = await File.ReadAllBytesAsync(pixelsPath);
            Assert.Equal(640 * 360 * 3, pixels.Length);
            var red = (40 * 640 + 40) * 3;
            var black = (100 * 640 + 300) * 3;
            var green = (240 * 640 + 540) * 3;
            Assert.True(pixels[red] > 200 && pixels[red + 1] < 30, "Negative placement shows the remaining red area");
            Assert.True(pixels[black] < 20 && pixels[black + 1] < 20 && pixels[black + 2] < 20, "Uncovered output stays black");
            Assert.True(pixels[green] < 30 && pixels[green + 1] > 80, "Right/bottom overflow shows the remaining green area");
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
                [
                    new(one, TimeSpan.FromSeconds(1), PixelRect.FullFrame(one), new(0, 0, 320, 180), VideoFitMode.Fit, true, 0, true, 1),
                    new(two, TimeSpan.Zero, PixelRect.FullFrame(two), new(320, 0, 320, 180), VideoFitMode.Fit, true, 1, false, 1)
                ],
                [],
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
                [
                    new(one, TimeSpan.Zero, PixelRect.FullFrame(one), new(0, 0, 960, 1080), VideoFitMode.Fill, true, 0, false, 1),
                    new(two, TimeSpan.Zero, PixelRect.FullFrame(two), new(960, 0, 960, 1080), VideoFitMode.Fill, true, 1, false, 1)
                ],
                [],
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
                [
                    new(one, TimeSpan.Zero, PixelRect.FullFrame(one), new(0, 0, 960, 1080), VideoFitMode.Fill, true, 0, true, 1),
                    new(two, TimeSpan.FromSeconds(1), PixelRect.FullFrame(two), new(960, 0, 960, 1080), VideoFitMode.Fill, true, 1, true, 1)
                ],
                [],
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

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Export_PlacesEveryVideoOfAnArbitraryCount(int videoCount)
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
            // One quadrant per video. With three videos the fourth quadrant must stay black,
            // which also proves the canvas base survives an odd track count.
            string[] colours = ["red", "green", "blue", "white"];
            (int X, int Y)[] cells = [(0, 0), (320, 0), (0, 180), (320, 180)];
            var probe = new MediaProbeService(paths);
            var tracks = new List<VideoTrack>();
            for (var index = 0; index < videoCount; index++)
            {
                var sourcePath = Path.Combine(temp, $"source-{index}.mp4");
                await RunAsync(paths.Ffmpeg, [
                    "-y", "-f", "lavfi", "-i", $"color=c={colours[index]}:size=320x180:rate=30:d=2",
                    "-f", "lavfi", "-i", $"sine=frequency={220 * (index + 1)}:duration=2",
                    "-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac", "-shortest", sourcePath]);
                var media = await probe.ProbeAsync(sourcePath);
                var cell = cells[index];
                tracks.Add(new(media, TimeSpan.Zero, PixelRect.FullFrame(media),
                    new(cell.X, cell.Y, 320, 180), VideoFitMode.Stretch, false, index, true, 1));
            }

            var composition = new Composition(tracks, [],
                new(640, 360, 30, TimeSpan.FromSeconds(2), OutputQuality.Balanced));
            var outputPath = Path.Combine(temp, "merged.mp4");
            await new FfmpegExportService(paths).ExportAsync(composition, outputPath);

            var output = await probe.ProbeAsync(outputPath);
            Assert.Equal("h264", output.Codec);
            Assert.Equal("aac", output.AudioCodec);
            Assert.InRange(output.Duration.TotalSeconds, 1.95, 2.05);

            var pixelsPath = Path.Combine(temp, "merged.rgb");
            await RunAsync(paths.Ffmpeg, ["-y", "-i", outputPath, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", pixelsPath]);
            var pixels = await File.ReadAllBytesAsync(pixelsPath);
            Assert.Equal(640 * 360 * 3, pixels.Length);

            for (var index = 0; index < cells.Length; index++)
            {
                // Sample each quadrant's centre, far from the chroma-subsampled seams.
                var offset = ((cells[index].Y + 90) * 640 + cells[index].X + 160) * 3;
                int r = pixels[offset], g = pixels[offset + 1], b = pixels[offset + 2];
                if (index >= videoCount)
                {
                    Assert.True(r < 40 && g < 40 && b < 40, $"Quadrant {index} holds no video, saw {r},{g},{b}");
                    continue;
                }

                var matches = colours[index] switch
                {
                    "red" => r > 150 && g < 90 && b < 90,
                    "green" => g > 100 && r < 90 && b < 90,
                    "blue" => b > 150 && r < 90 && g < 90,
                    _ => r > 180 && g > 180 && b > 180
                };
                Assert.True(matches, $"Quadrant {index} must show {colours[index]}, saw {r},{g},{b}");
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

    [Fact]
    public async Task Export_NormalisesTheMixSoExtraSourcesDoNotPileUp()
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
            var videoPath = Path.Combine(temp, "silent.mp4");
            var tonePath = Path.Combine(temp, "tone.mp3");
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "color=c=gray:size=320x180:rate=30:d=2", "-c:v", "mpeg4", "-q:v", "3", videoPath]);
            await RunAsync(paths.Ffmpeg, ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2", "-c:a", "libmp3lame", "-q:a", "2", tonePath]);

            var probe = new MediaProbeService(paths);
            var video = await probe.ProbeAsync(videoPath);
            var tone = await probe.ProbeAsync(tonePath);
            VideoTrack picture = new(video, TimeSpan.Zero, PixelRect.FullFrame(video), new(0, 0, 320, 180),
                VideoFitMode.Stretch, false, 0, false, 1);
            OutputSettings output = new(320, 180, 30, TimeSpan.FromSeconds(2), OutputQuality.High);

            async Task<int> PeakAsync(int toneCount, string name)
            {
                var audios = Enumerable.Range(0, toneCount)
                    .Select(_ => new AudioTrack(tone, TimeSpan.Zero, true, 1)).ToArray();
                var outputPath = Path.Combine(temp, name + ".mp4");
                await new FfmpegExportService(paths).ExportAsync(new Composition([picture], audios, output), outputPath);
                var pcmPath = Path.Combine(temp, name + ".pcm");
                await RunAsync(paths.Ffmpeg, ["-y", "-i", outputPath, "-vn", "-ar", "48000", "-ac", "1", "-f", "s16le", pcmPath]);
                var pcm = await File.ReadAllBytesAsync(pcmPath);
                var peak = 0;
                for (var index = 0; index + 1 < pcm.Length; index += 2)
                {
                    peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, index)));
                }

                return peak;
            }

            var onePeak = await PeakAsync(1, "one-tone");
            var threePeak = await PeakAsync(3, "three-tones");
            Assert.True(onePeak > 3000, $"The single-source export must actually carry the tone, saw {onePeak}");

            // Three copies of the same tone each take a third of the mix, so the sum matches one
            // source at full scale instead of tripling into the limiter.
            Assert.InRange(threePeak, onePeak * 0.8, onePeak * 1.2);
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
