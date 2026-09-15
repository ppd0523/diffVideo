using System.IO;
using System.Windows;
using DiffVideo.Core;

namespace DiffVideo.App;

public partial class ExportConfirmationWindow : Window
{
    private readonly Composition _snapshot;
    public ExportConfirmationWindow(Composition snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;
        var output = snapshot.Output;
        var quality = output.Quality switch { OutputQuality.High => "높음", OutputQuality.Small => "작은 파일", _ => "균형" };
        SummaryText.Text = $"캔버스  {output.Width} × {output.Height} px\n출력  MP4 · H.264 / AAC · {output.FramesPerSecond} fps\n" +
            $"내보내기 구간  {output.ExportStart.TotalSeconds:0.###}초 → {output.EffectiveExportEnd.TotalSeconds:0.###}초\n" +
            $"저장 길이  {output.ExportDuration.TotalSeconds:0.###}초   ·   품질  {quality}\n\n" +
            $"위에 표시  {(snapshot.Video1.ZIndex > snapshot.Video2.ZIndex ? "A · 영상 1" : "B · 영상 2")}\n\n" +
            Describe("영상 1", snapshot.Video1) + "\n\n" + Describe("영상 2", snapshot.Video2) + "\n\n" +
            (snapshot.ExtraAudio is { } audio ? $"MP3  {Path.GetFileName(audio.Media.Path)}\n시작 {audio.Start.TotalSeconds:0.000}초 · 오디오 {Audio(audio.IncludeAudio, audio.Volume)}" : "MP3  없음") +
            "\n\n시작 전: 첫 프레임 · 종료 후: 마지막 프레임 유지\n오디오 혼합: 클리핑 방지 리미터 적용";
    }

    public Composition ConfirmedComposition => _snapshot with
    {
        Video1 = _snapshot.Video1 with { FileNameLabel = (_snapshot.Video1.FileNameLabel ?? FileNameLabel.Default(false)) with { Enabled = IncludeFileNamesCheckBox.IsChecked == true } },
        Video2 = _snapshot.Video2 with { FileNameLabel = (_snapshot.Video2.FileNameLabel ?? FileNameLabel.Default(false)) with { Enabled = IncludeFileNamesCheckBox.IsChecked == true } }
    };

    private static string Audio(bool enabled, double volume) => enabled ? $"포함 · 볼륨 {volume:P0}" : "제외";
    private static string Describe(string name, VideoTrack track) =>
        $"{name}  {Path.GetFileName(track.Media.Path)}\n시작 {track.Start.TotalSeconds:0.000}초 · 원본 길이 {track.Media.Duration.TotalSeconds:0.###}초\n" +
        $"ROI  ({track.Roi.X}, {track.Roi.Y})  {track.Roi.Width} × {track.Roi.Height}\n" +
        $"배치  ({track.Destination.X}, {track.Destination.Y})  {track.Destination.Width} × {track.Destination.Height} · {track.FitMode} · 레이어 {track.ZIndex}\n" +
        $"오디오  {Audio(track.IncludeAudio && track.Media.HasAudio, track.Volume)}";

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
