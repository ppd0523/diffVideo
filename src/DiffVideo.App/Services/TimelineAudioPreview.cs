using System.Diagnostics;
using DiffVideo.Core;
using DiffVideo.Infrastructure;
using NAudio.Wave;

namespace DiffVideo.App.Services;

public sealed class TimelineAudioPreview(FfmpegPaths paths) : IDisposable
{
    private readonly object _gate = new();
    private readonly FfmpegPaths _paths = paths;
    private Process? _process;
    private RawSourceWaveStream? _waveStream;
    private WaveOut? _waveOut;
    private CancellationTokenRegistration _cancellationRegistration;

    public void Start(Composition composition, double fromSeconds, CancellationToken cancellationToken)
    {
        Stop();
        cancellationToken.ThrowIfCancellationRequested();
        if (fromSeconds >= composition.Output.Duration.TotalSeconds)
        {
            return;
        }

        var invocation = FfmpegCommandBuilder.BuildAudioPreview(composition, fromSeconds);
        var startInfo = new ProcessStartInfo(_paths.Ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();
        _ = process.StandardError.ReadToEndAsync().ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        var waveStream = new RawSourceWaveStream(process.StandardOutput.BaseStream, new WaveFormat(48000, 16, 2));
        // NAudio applies BufferMilliseconds to each buffer. Three 15 ms buffers keep
        // the queued WinMM audio below the 50 ms preview A/V-sync target.
        var waveOut = new WaveOut { BufferMilliseconds = 15, NumberOfBuffers = 3 };
        try
        {
            waveOut.Init(waveStream);
            lock (_gate)
            {
                _process = process;
                _waveStream = waveStream;
                _waveOut = waveOut;
                waveOut.Play();
            }

            var registration = cancellationToken.Register(Stop);
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _cancellationRegistration = registration;
                }
                else
                {
                    registration.Dispose();
                }
            }
        }
        catch
        {
            StopResources(process, waveStream, waveOut);
            throw;
        }
    }

    public void Stop()
    {
        Process? process;
        RawSourceWaveStream? waveStream;
        WaveOut? waveOut;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            process = _process;
            waveStream = _waveStream;
            waveOut = _waveOut;
            registration = _cancellationRegistration;
            _process = null;
            _waveStream = null;
            _waveOut = null;
            _cancellationRegistration = default;
        }

        registration.Dispose();
        StopResources(process, waveStream, waveOut);
    }

    private static void StopResources(Process? process, RawSourceWaveStream? waveStream, WaveOut? waveOut)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        waveOut?.Stop();
        waveOut?.Dispose();
        waveStream?.Dispose();
        process?.Dispose();
    }

    public void Dispose() => Stop();
}
