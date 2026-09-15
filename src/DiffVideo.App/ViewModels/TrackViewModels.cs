using DiffVideo.Core;

namespace DiffVideo.App.ViewModels;

public sealed class VideoTrackViewModel : ObservableObject
{
    private MediaInfo? _media;
    private double _startSeconds;
    private int _roiX;
    private int _roiY;
    private int _roiWidth = 16;
    private int _roiHeight = 16;
    private int _destinationX;
    private int _destinationY;
    private int _destinationWidth = 640;
    private int _destinationHeight = 720;
    private VideoFitMode _fitMode = VideoFitMode.Fit;
    private bool _aspectRatioLocked = true;
    private bool _includeAudio = true;
    private double _volumePercent = 100;
    private int _zIndex;
    private bool _isSelected;
    private bool _syncingSize;

    public VideoTrackViewModel(string name, int zIndex)
    {
        Name = name;
        _zIndex = zIndex;
    }

    public string Name { get; }

    public MediaInfo? Media
    {
        get => _media;
        private set
        {
            if (SetProperty(ref _media, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Details));
                OnPropertyChanged(nameof(HasMedia));
                OnPropertyChanged(nameof(HasAudio));
                OnPropertyChanged(nameof(DurationSeconds));
            }
        }
    }

    public string DisplayName => Media?.DisplayName ?? "비어 있음";

    public string Details => Media is null
        ? "MP4 파일을 놓으세요"
        : $"{Media.Codec.ToUpperInvariant()} · {Media.DisplayWidth}×{Media.DisplayHeight} · {Media.Duration:mm\\:ss\\.f}";

    public bool HasMedia => Media is not null;

    public bool HasAudio => Media?.HasAudio == true;

    public double DurationSeconds => Media?.Duration.TotalSeconds ?? 0;

    public double StartSeconds { get => _startSeconds; set => SetProperty(ref _startSeconds, Math.Max(0, value)); }
    public int RoiX { get => _roiX; set => SetRoi(new(value, RoiY, RoiWidth, RoiHeight)); }
    public int RoiY { get => _roiY; set => SetRoi(new(RoiX, value, RoiWidth, RoiHeight)); }
    public int RoiWidth { get => _roiWidth; set => SetRoi(new(RoiX, RoiY, value, RoiHeight)); }
    public int RoiHeight { get => _roiHeight; set => SetRoi(new(RoiX, RoiY, RoiWidth, value)); }
    public int DestinationX { get => _destinationX; set => SetProperty(ref _destinationX, value); }
    public int DestinationY { get => _destinationY; set => SetProperty(ref _destinationY, value); }

    public void SetDestination(PixelRect rectangle)
    {
        var previous = new PixelRect(_destinationX, _destinationY, _destinationWidth, _destinationHeight);
        (_destinationX, _destinationY, _destinationWidth, _destinationHeight) =
            (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        if (previous.X != rectangle.X) { OnPropertyChanged(nameof(DestinationX)); }
        if (previous.Y != rectangle.Y) { OnPropertyChanged(nameof(DestinationY)); }
        if (previous.Width != rectangle.Width) { OnPropertyChanged(nameof(DestinationWidth)); }
        if (previous.Height != rectangle.Height) { OnPropertyChanged(nameof(DestinationHeight)); }
    }

    public int DestinationWidth
    {
        get => _destinationWidth;
        set
        {
            var normalized = Math.Max(2, value / 2 * 2);
            if (!SetProperty(ref _destinationWidth, normalized) || !AspectRatioLocked || _syncingSize)
            {
                return;
            }

            _syncingSize = true;
            var aspect = RoiHeight == 0 ? 1 : RoiWidth / (double)RoiHeight;
            DestinationHeight = Math.Max(2, (int)Math.Round(normalized / aspect) / 2 * 2);
            _syncingSize = false;
        }
    }

    public int DestinationHeight
    {
        get => _destinationHeight;
        set
        {
            var normalized = Math.Max(2, value / 2 * 2);
            if (!SetProperty(ref _destinationHeight, normalized) || !AspectRatioLocked || _syncingSize)
            {
                return;
            }

            _syncingSize = true;
            var aspect = RoiHeight == 0 ? 1 : RoiWidth / (double)RoiHeight;
            DestinationWidth = Math.Max(2, (int)Math.Round(normalized * aspect) / 2 * 2);
            _syncingSize = false;
        }
    }

    public VideoFitMode FitMode { get => _fitMode; set => SetProperty(ref _fitMode, value); }
    public bool AspectRatioLocked { get => _aspectRatioLocked; set => SetProperty(ref _aspectRatioLocked, value); }
    public bool IncludeAudio { get => _includeAudio; set => SetProperty(ref _includeAudio, value); }
    public double VolumePercent { get => _volumePercent; set => SetProperty(ref _volumePercent, Math.Clamp(value, 0, 200)); }
    public int ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public void SetMedia(MediaInfo media, PixelRect destination)
    {
        Media = media;
        SetRoi(PixelRect.FullFrame(media));
        DestinationX = destination.X;
        DestinationY = destination.Y;
        _syncingSize = true;
        DestinationWidth = destination.Width;
        DestinationHeight = destination.Height;
        _syncingSize = false;
        IncludeAudio = media.HasAudio;
    }

    public void SetRoi(PixelRect roi)
    {
        var normalized = Media is { } media
            ? RoiBounds.Clamp(roi, media.DisplayWidth, media.DisplayHeight)
            : new PixelRect(Math.Max(0, roi.X), Math.Max(0, roi.Y), Math.Max(2, roi.Width), Math.Max(2, roi.Height));
        var previous = new PixelRect(_roiX, _roiY, _roiWidth, _roiHeight);
        // Publish the complete valid rectangle before notifying any binding/preview observer.
        (_roiX, _roiY, _roiWidth, _roiHeight) = (normalized.X, normalized.Y, normalized.Width, normalized.Height);
        if (previous.X != normalized.X || roi.X != normalized.X) { OnPropertyChanged(nameof(RoiX)); }
        if (previous.Y != normalized.Y || roi.Y != normalized.Y) { OnPropertyChanged(nameof(RoiY)); }
        if (previous.Width != normalized.Width || roi.Width != normalized.Width) { OnPropertyChanged(nameof(RoiWidth)); }
        if (previous.Height != normalized.Height || roi.Height != normalized.Height) { OnPropertyChanged(nameof(RoiHeight)); }
    }

    public VideoTrack ToModel() => new(
        Media ?? throw new InvalidOperationException($"{Name}에 영상이 없습니다."),
        TimeSpan.FromSeconds(StartSeconds),
        new(RoiX, RoiY, RoiWidth, RoiHeight),
        new(DestinationX, DestinationY, DestinationWidth, DestinationHeight),
        FitMode,
        AspectRatioLocked,
        ZIndex,
        IncludeAudio,
        VolumePercent / 100d);
}

public sealed class AudioTrackViewModel : ObservableObject
{
    private MediaInfo? _media;
    private double _startSeconds;
    private bool _includeAudio = true;
    private double _volumePercent = 30;

    public MediaInfo? Media
    {
        get => _media;
        private set
        {
            if (SetProperty(ref _media, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Details));
                OnPropertyChanged(nameof(HasMedia));
                OnPropertyChanged(nameof(DurationSeconds));
            }
        }
    }

    public string DisplayName => Media?.DisplayName ?? "선택적 MP3";
    public string Details => Media is null ? "MP3 파일을 놓으세요" : $"{Media.Codec.ToUpperInvariant()} · {Media.Duration:mm\\:ss\\.f}";
    public bool HasMedia => Media is not null;
    public double DurationSeconds => Media?.Duration.TotalSeconds ?? 0;
    public double StartSeconds { get => _startSeconds; set => SetProperty(ref _startSeconds, Math.Max(0, value)); }
    public bool IncludeAudio { get => _includeAudio; set => SetProperty(ref _includeAudio, value); }
    public double VolumePercent { get => _volumePercent; set => SetProperty(ref _volumePercent, Math.Clamp(value, 0, 200)); }

    public void SetMedia(MediaInfo media)
    {
        Media = media;
        StartSeconds = 0;
        IncludeAudio = true;
        VolumePercent = 30;
    }

    public ExtraAudioTrack? ToModel() => Media is null
        ? null
        : new(Media, TimeSpan.FromSeconds(StartSeconds), IncludeAudio, VolumePercent / 100d);
}

public sealed record Choice<T>(string Label, T Value);
