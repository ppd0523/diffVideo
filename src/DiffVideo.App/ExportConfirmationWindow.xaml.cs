using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
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
        AddSummarySection("출력",
            $"캔버스  {output.Width} × {output.Height} px\n출력  MP4 · H.264 / AAC · {output.FramesPerSecond} fps\n" +
            $"내보내기 구간  {output.ExportStart.TotalSeconds:0.###}초 → {output.EffectiveExportEnd.TotalSeconds:0.###}초\n" +
            $"저장 길이  {output.ExportDuration.TotalSeconds:0.###}초 · 품질  {quality}");
        AddSummarySection("레이어", string.Join("\n", snapshot.Videos
            .OrderByDescending(track => track.ZIndex)
            .Select((track, order) => $"{order + 1}번째  {Path.GetFileName(track.Media.Path)}")));
        for (var index = 0; index < snapshot.Videos.Count; index++)
        {
            AddSummarySection($"영상 {index + 1}", Describe(snapshot.Videos[index]));
        }

        AddSummarySection("음원", snapshot.Audios.Count == 0
            ? "없음"
            : string.Join("\n", snapshot.Audios.Select(track =>
                $"{Path.GetFileName(track.Media.Path)}\n시작 {track.Start.TotalSeconds:0.000}초 · 오디오 {Audio(track.IncludeAudio, track.Volume)}")));
        AddSummarySection("오디오 믹스", snapshot.AudioMix().Any()
            ? string.Join("\n", snapshot.AudioMix().Select(item =>
                $"입력 {item.Input + 1}  비중 {item.Gain:P0}"))
            : "무음");
        AddSummarySection("처리",
            "시작 전: 첫 프레임 · 종료 후: 마지막 프레임 유지\n오디오 혼합: 클리핑 방지 리미터 적용");
    }

    public Composition ConfirmedComposition => _snapshot with
    {
        Videos = [.. _snapshot.Videos.Select(track => track with
        {
            FileNameLabel = (track.FileNameLabel ?? FileNameLabel.Default(false))
                with { Enabled = IncludeFileNamesCheckBox.IsChecked == true }
        })]
    };

    private static string Audio(bool enabled, double volume) => enabled ? $"포함 · 볼륨 {volume:P0}" : "제외";
    private void AddSummarySection(string title, string content)
    {
        if (SummaryText.Inlines.Count > 0)
        {
            SummaryText.Inlines.Add(new LineBreak());
            SummaryText.Inlines.Add(new LineBreak());
        }

        SummaryText.Inlines.Add(new Run(title)
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("PrimaryBrush")
        });
        SummaryText.Inlines.Add(new LineBreak());
        SummaryText.Inlines.Add(new Run(content));
    }

    private static string Describe(VideoTrack track) =>
        $"{Path.GetFileName(track.Media.Path)}\n시작 {track.Start.TotalSeconds:0.000}초 · 원본 길이 {track.Media.Duration.TotalSeconds:0.###}초\n" +
        $"ROI  ({track.Roi.X}, {track.Roi.Y})  {track.Roi.Width} × {track.Roi.Height}\n" +
        $"배치  ({track.Destination.X}, {track.Destination.Y})  {track.Destination.Width} × {track.Destination.Height} · {track.FitMode} · 레이어 {track.ZIndex}\n" +
        $"오디오  {Audio(track.IncludeAudio && track.Media.HasAudio, track.Volume)}";

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
