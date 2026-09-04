using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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
}

