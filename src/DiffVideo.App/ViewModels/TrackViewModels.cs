using System.Windows;
using System.Windows.Media;
using DiffVideo.Core;

namespace DiffVideo.App.ViewModels;

/// <summary>What a timeline row needs, whatever kind of media it holds.</summary>
public interface ITimelineTrack
{
    string Name { get; }
    bool HasMedia { get; }
    double StartSeconds { get; }
    bool IsEditingStart { get; set; }
    string StartEditText { get; set; }
}

public sealed class VideoTrackViewModel : ObservableObject, ITimelineTrack
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
    private bool _isSelected;
    private bool _syncingSize;
    private string _name;
    private bool _isPlacementCustomized;
    private bool _isLagging;
    private int _zIndex;
    private bool _isEditingStart;
    private string _startEditText = "";

    public VideoTrackViewModel(string name)
    {
        _name = name;
    }

    /// <summary>Renumbered whenever tracks are added, removed or reordered.</summary>
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    /// <summary>
    /// Set once the user positions this track by hand. Automatic grid layout then skips it,
    /// so adding a track never destroys placement work.
    /// </summary>
    public bool IsPlacementCustomized { get => _isPlacementCustomized; set => SetProperty(ref _isPlacementCustomized, value); }

    /// <summary>True while this track trails the shared playback clock; drives its buffering badge.</summary>
    public bool IsLagging { get => _isLagging; set => SetProperty(ref _isLagging, value); }

    /// <summary>Derived from row order by the owner; never edited directly.</summary>
    public int ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }

    /// <summary>True while this track shows its inline start-time editor instead of its label.</summary>
    public bool IsEditingStart { get => _isEditingStart; set => SetProperty(ref _isEditingStart, value); }

    /// <summary>Text held by the inline editor; only committed values reach StartSeconds.</summary>
    public string StartEditText { get => _startEditText; set => SetProperty(ref _startEditText, value); }

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
    public int DestinationX { get => _destinationX; set { if (SetProperty(ref _destinationX, value)) { IsPlacementCustomized = true; } } }
    public int DestinationY { get => _destinationY; set { if (SetProperty(ref _destinationY, value)) { IsPlacementCustomized = true; } } }

    /// <summary>A user placement: marks the track so automatic layout leaves it alone.</summary>
    public void SetDestination(PixelRect rectangle)
    {
        ApplyLayoutDestination(rectangle);
        IsPlacementCustomized = true;
    }

    /// <summary>An automatic placement: leaves the customized flag exactly as it was.</summary>
    public void ApplyLayoutDestination(PixelRect rectangle)
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
            if (!SetProperty(ref _destinationWidth, normalized)) { return; }
            IsPlacementCustomized = true;
            if (!AspectRatioLocked || _syncingSize)
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
            if (!SetProperty(ref _destinationHeight, normalized)) { return; }
            IsPlacementCustomized = true;
            if (!AspectRatioLocked || _syncingSize)
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
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public void SetMedia(MediaInfo media, PixelRect destination)
    {
        Media = media;
        StartSeconds = 0;
        SetRoi(PixelRect.FullFrame(media));
        ApplyLayoutDestination(destination);
        FitMode = VideoFitMode.Fit;
        AspectRatioLocked = true;
        IncludeAudio = media.HasAudio;
        VolumePercent = 100;
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

    /// <summary>Snapshot using the layer order this track already carries.</summary>
    public VideoTrack ToModel() => ToModel(ZIndex);

    /// <summary>Layer order comes from the track list, so the caller may override it.</summary>
    public VideoTrack ToModel(int zIndex) => new(
        Media ?? throw new InvalidOperationException($"{Name}에 영상이 없습니다."),
        TimeSpan.FromSeconds(StartSeconds),
        new(RoiX, RoiY, RoiWidth, RoiHeight),
        new(DestinationX, DestinationY, DestinationWidth, DestinationHeight),
        FitMode,
        AspectRatioLocked,
        zIndex,
        IncludeAudio,
        VolumePercent / 100d);
}

public sealed class AudioTrackViewModel : ObservableObject, ITimelineTrack
{
    private MediaInfo? _media;
    private double _startSeconds;
    private bool _includeAudio = true;
    private double _volumePercent = 30;
    private bool _isEditingStart;
    private string _startEditText = "";
    private Brush? _waveformMask;

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

    public string Name => "음원";
    public bool IsEditingStart { get => _isEditingStart; set => SetProperty(ref _isEditingStart, value); }
    public string StartEditText { get => _startEditText; set => SetProperty(ref _startEditText, value); }
    public string DisplayName => Media?.DisplayName ?? "선택적 음원";
    public string Details => Media is null ? "MP3/WAV 파일을 놓으세요" : $"{Media.Codec.ToUpperInvariant()} · {Media.Duration:mm\\:ss\\.f}";
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
        WaveformMask = null;
    }

    /// <summary>블록 크기에 맞춰 늘어나는 파형 모양. 색은 XAML이 정하도록 불투명 마스크로 둔다.</summary>
    public Brush? WaveformMask { get => _waveformMask; private set => SetProperty(ref _waveformMask, value); }

    public void SetWaveform(IReadOnlyList<double> peaks)
    {
        var levels = Waveform.Scale(peaks);
        if (levels.Length == 0)
        {
            WaveformMask = null;
            return;
        }

        // 가운데(y=1)를 기준으로 위쪽 윤곽을 따라가고 아래쪽으로 되돌아오는 대칭 도형.
        var outline = new List<Point>(levels.Length * 2 + 1);
        for (var index = 0; index < levels.Length; index++) { outline.Add(new(index + 0.5, 1 - levels[index])); }
        outline.Add(new(levels.Length, 1));
        for (var index = levels.Length - 1; index >= 0; index--) { outline.Add(new(index + 0.5, 1 + levels[index])); }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 1), isFilled: true, isClosed: true);
            context.PolyLineTo(outline, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        // Viewbox를 고정해야 조용한 음원도 도형 경계로 늘어나지 않고 정해진 높이로 그려진다.
        var brush = new DrawingBrush(new GeometryDrawing(Brushes.Black, null, geometry))
        {
            Viewbox = new Rect(0, 0, levels.Length, 2),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        WaveformMask = brush;
    }

    public AudioTrack? ToModel() => Media is null
        ? null
        : new(Media, TimeSpan.FromSeconds(StartSeconds), IncludeAudio, VolumePercent / 100d);
}

public sealed record Choice<T>(string Label, T Value);
