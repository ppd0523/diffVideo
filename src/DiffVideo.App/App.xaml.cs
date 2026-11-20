using System.Configuration;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DiffVideo.App.Services;
using Microsoft.Win32;

namespace DiffVideo.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                TryWriteCrashLog(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => TryWriteCrashLog(args.Exception);
        base.OnStartup(e);
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => UseDarkTitleBar((Window)sender, _dark == true)));
        if (e.Args.FirstOrDefault() == "--preview-diagnostics")
        {
            PreviewDiagnostics.Start(e.Args);
        }
        else
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }

    private static void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = TryWriteCrashLog(e.Exception);
        MessageBox.Show(
            $"DiffVideo를 실행하는 중 오류가 발생했습니다.\n\n{e.Exception.Message}\n\n진단 로그: {logPath}",
            "DiffVideo 실행 오류",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(-1);
    }

    private static string TryWriteCrashLog(Exception exception)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DiffVideo");
        var logPath = Path.Combine(directory, "crash.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return logPath;
    }

    private bool? _dark;

    protected override void OnExit(ExitEventArgs e)
    {
        // SystemEvents는 정적 이벤트라 풀지 않으면 앱이 해제되지 않는다
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) { Dispatcher.InvokeAsync(ApplyTheme); }
    }

    private void ApplyTheme()
    {
        var dark = IsDarkTheme();
        if (dark == _dark) { return; }
        _dark = dark;
        var dictionaries = Resources.MergedDictionaries;
        var index = dictionaries.IndexOf(dictionaries.First(d => d.Source?.OriginalString.Contains("Colors.", StringComparison.Ordinal) == true));
        dictionaries[index] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/DiffVideo;component/Themes/Colors.{(dark ? "Dark" : "Light")}.xaml")
        };
        foreach (Window window in Windows)
        {
            UseDarkTitleBar(window, dark);
            InvalidateDrawing(window);
        }
    }

    internal ThemePreference ThemePreference { get; private set; }

    internal void SetThemePreference(ThemePreference preference)
    {
        ThemePreference = Enum.IsDefined(preference) ? preference : ThemePreference.System;
        ApplyTheme();
    }

    /// <summary>앱 설정(밝게/어둡게)이 있으면 그것을, 아니면 Windows 설정의 앱 모드를 따른다. DIFFVIDEO_THEME=dark|light는 화면 검사용으로 둘 다 덮어쓴다.</summary>
    private bool IsDarkTheme()
    {
        var forced = Environment.GetEnvironmentVariable("DIFFVIDEO_THEME");
        if (forced is "dark" or "light") { return forced == "dark"; }
        if (ThemePreference != ThemePreference.System) { return ThemePreference == ThemePreference.Dark; }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static void UseDarkTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) { return; }
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));  // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    // 직접 그리는 컨트롤(AxisRuler 등)은 OnRender에서 색을 읽으므로 테마가 바뀌면 다시 그리게 한다
    private static void InvalidateDrawing(DependencyObject node)
    {
        if (node is UIElement element) { element.InvalidateVisual(); }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { InvalidateDrawing(VisualTreeHelper.GetChild(node, i)); }
    }
}

