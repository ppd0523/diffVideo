using System.Diagnostics;
using DiffVideo.Core;
using DiffVideo.Infrastructure;
using NAudio.Wave;

namespace DiffVideo.App.Services;

/// <summary>Audio-only mix with bounded prefetch. The device's played samples are the master clock.</summary>
public sealed class BufferedTimelineAudio(FfmpegPaths paths) : IDisposable
{
    private AudioSession? _session;
    public double PositionSeconds => (_session?.Wave?.GetPosition() ?? 0) / 192000d;
    public bool NeedsBuffer => _session is { Eof: false } session && session.Buffer.BufferedDuration.TotalSeconds < 0.06;

    public async Task PrepareAsync(Composition composition, double from, CancellationToken token)
    {
        Stop();
        var session = new AudioSession(paths, composition, from, token);
        _session = session;
        await session.InitializeAsync();
        token.ThrowIfCancellationRequested();
    }

    public Task WaitForBufferAsync(CancellationToken token) => _session?.WaitForBufferAsync(token) ?? Task.CompletedTask;
    public void Play() => _session?.Wave?.Play();
    public void Pause() => _session?.Wave?.Pause();
    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
    }
    public void Dispose() => Stop();

    private sealed class AudioSession : IDisposable
    {
        private readonly Process _process;
        private readonly CancellationTokenSource _cancellation;
        private readonly Task _pump;
        private volatile Exception? _error;
        private volatile bool _eof;
        private bool _disposed;
        public bool Eof => _eof;
        public BufferedWaveProvider Buffer { get; } = new(new WaveFormat(48000, 16, 2), TimeSpan.FromSeconds(2))
        {
            ReadFully = true
        };
        public WaveOut? Wave { get; private set; }

        public AudioSession(FfmpegPaths paths, Composition composition, double from, CancellationToken token)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var invocation = FfmpegCommandBuilder.BuildAudioPreview(composition, from);
            var info = new ProcessStartInfo(paths.Ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var argument in invocation.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
            _process = new() { StartInfo = info };
            _process.Start();
            _pump = Task.Run(PumpAsync);
        }

        public async Task InitializeAsync()
        {
            await WaitForBufferAsync(_cancellation.Token);
            _cancellation.Token.ThrowIfCancellationRequested();
            var wave = new WaveOut { BufferMilliseconds = 15, NumberOfBuffers = 3 };
            try { wave.Init(Buffer); Wave = wave; }
            catch { wave.Dispose(); throw; }
        }

        public async Task WaitForBufferAsync(CancellationToken token)
        {
            var wait = Stopwatch.StartNew();
            while (!_eof && Buffer.BufferedDuration.TotalSeconds < 0.25)
            {
                if (_error is not null) { throw new InvalidOperationException("오디오 준비 실패", _error); }
                if (wait.Elapsed > TimeSpan.FromSeconds(20)) { throw new TimeoutException("오디오 준비 시간이 초과되었습니다."); }
                await Task.Delay(10, token);
            }
            if (_error is not null) { throw new InvalidOperationException("오디오 재생 실패", _error); }
        }

        private async Task PumpAsync()
        {
            var token = _cancellation.Token;
            var errors = _process.StandardError.ReadToEndAsync();
            try
            {
                var bytes = new byte[8192];
                while (true)
                {
                    while (Buffer.BufferedDuration.TotalSeconds > 0.75)
                    {
                        await Task.Delay(10, token).ConfigureAwait(false);
                    }
                    var count = await _process.StandardOutput.BaseStream.ReadAsync(bytes, token).ConfigureAwait(false);
                    if (count == 0) { break; }
                    Buffer.AddSamples(bytes, 0, count);
                }
                await _process.WaitForExitAsync(token).ConfigureAwait(false);
                if (_process.ExitCode != 0) { throw new InvalidOperationException(await errors.ConfigureAwait(false)); }
                _eof = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { _error = exception; }
            finally
            {
                Kill();
                try { await _process.WaitForExitAsync().ConfigureAwait(false); await errors.ConfigureAwait(false); }
                catch (InvalidOperationException) { }
            }
        }

        private void Kill()
        {
            try { if (!_process.HasExited) { _process.Kill(); } }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _cancellation.Cancel();
            Kill();
            Wave?.Stop();
            Wave?.Dispose();
            Wave = null;
            _ = _pump.ContinueWith(_ => { _process.Dispose(); _cancellation.Dispose(); }, TaskScheduler.Default);
        }
    }
}
