using System.Globalization;
using System.Xml.Linq;

namespace DiffVideo.Core.Tests;

/// <summary>PRODUCT.md의 WCAG AA 요구를 라이트·다크 색 사전 모두에서 지킨다.</summary>
public sealed class ThemePaletteTests
{
    public static TheoryData<string> Themes => new() { "Light", "Dark" };

    [Fact]
    public void LightAndDarkDefineTheSameKeys()
    {
        Assert.Equal(Load("Light").Keys.Order(), Load("Dark").Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TextMeetsAa(string theme)
    {
        var c = Load(theme);
        (string Text, string Background)[] pairs =
        [
            ("InkBrush", "CanvasBrush"), ("InkBrush", "SurfaceBrush"), ("InkBrush", "RaisedSurfaceBrush"),
            ("SecondaryTextBrush", "CanvasBrush"), ("SecondaryTextBrush", "SurfaceBrush"),
            // 기본 버튼 글자: 평소, hover, pressed
            ("PrimaryBrush", "CanvasBrush"), ("PrimaryBrush", "SurfaceBrush"), ("PrimaryBrush", "RaisedSurfaceBrush"),
            // 주 버튼·선택된 클립·체크박스 위 글자
            ("CanvasBrush", "PrimaryBrush"), ("CanvasBrush", "PrimaryHoverBrush"), ("CanvasBrush", "PrimaryActiveBrush"),
            ("CanvasBrush", "WarningBrush"), ("CanvasBrush", "InkBrush"), ("ErrorBrush", "CanvasBrush"),
        ];
        foreach (var (text, background) in pairs)
        {
            Assert.True(Contrast(c[text], c[background]) >= 4.5, $"{theme}: {text} on {background} = {Contrast(c[text], c[background]):0.00}");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ControlsAndMarksMeetAa(string theme)
    {
        var c = Load(theme);
        (string Mark, string Background)[] pairs =
        [
            ("MediumBorderBrush", "CanvasBrush"), ("StrongBorderBrush", "CanvasBrush"),
            ("WarningBrush", "CanvasBrush"), ("WarningBrush", "SurfaceBrush"),
            ("CanvasBrush", "ExportBrush"), ("CanvasBrush", "ExportHoverBrush"), ("CanvasBrush", "ExportActiveBrush"),
        ];
        foreach (var (mark, background) in pairs)
        {
            Assert.True(Contrast(c[mark], c[background]) >= 3, $"{theme}: {mark} on {background} = {Contrast(c[mark], c[background]):0.00}");
        }
    }

    [Fact]
    public void ContrastMatchesWcagReferenceValues()
    {
        Assert.Equal(21, Contrast("#000000", "#FFFFFF"), 2);
        Assert.Equal(4.59, Contrast("#78716C", "#FAFAF9"), 2);
    }

    private static Dictionary<string, string> Load(string theme)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DiffVideo.slnx"))) { directory = directory.Parent; }
        var path = Path.Combine(directory!.FullName, "src", "DiffVideo.App", "Themes", $"Colors.{theme}.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
    }

    private static double Contrast(string a, string b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    // #RRGGBB 또는 #AARRGGBB(알파는 무시: 대비 검사 대상은 모두 불투명 색)
    private static double Luminance(string hex)
    {
        var rgb = hex[^6..];
        double Channel(int i)
        {
            var v = int.Parse(rgb.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }
}
