using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SideDesk.Services;

namespace SideDesk;

public partial class App : Application
{
    private const string MutexName = @"Local\SideDesk.SingleInstance";
    private const string ShowEventName = @"Local\SideDesk.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private MainWindow? _mainWindow;
    private int _cleanupStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Write("SideDesk 启动。");

        if (e.Args.Any(arg => arg.Equals("--restore-icons", StringComparison.OrdinalIgnoreCase)))
        {
            var restored = DesktopIcons.ForceShow();
            AppLog.Write(restored ? "手动恢复桌面图标成功。" : "手动恢复桌面图标失败：未找到桌面图标区域。");
            Shutdown(restored ? 0 : 1);
            return;
        }

        if (e.Args.Any(arg => arg.Equals("--diagnose", StringComparison.OrdinalIgnoreCase)))
        {
            RunDiagnostics(e.Args);
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAndRestore();
        SessionEnding += (_, _) =>
        {
            _mainWindow?.AllowSystemShutdown();
            DesktopIcons.RestoreCapturedState();
        };

        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName, out var isNewInstance);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            if (!isNewInstance)
            {
                _showEvent.Set();
                Shutdown(0);
                return;
            }

            _showWait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (_, _) => Dispatcher.BeginInvoke(() => _mainWindow?.RevealFromExternal()),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            var settings = SettingsStore.Load();
            var autoExitAfter = ParseSmokeTest(e.Args);
            _mainWindow = new MainWindow(settings, autoExitAfter);
            MainWindow = _mainWindow;
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write("SideDesk 启动失败。", ex);
            try
            {
                MessageBox.Show(
                    "SideDesk 启动失败。详情已写入日志：\n" + Path.Combine(SettingsStore.SettingsDirectory, "SideDesk.log"),
                    "SideDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Nothing else we can do here.
            }

            Shutdown(1);
        }
    }

    private static TimeSpan? ParseSmokeTest(string[] args)
    {
        var index = Array.FindIndex(args, arg => arg.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        var seconds = 12;
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed))
        {
            seconds = Math.Clamp(parsed, 3, 120);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static void RunDiagnostics(string[] args)
    {
        var index = Array.FindIndex(args, arg => arg.Equals("--diagnose", StringComparison.OrdinalIgnoreCase));
        var outputPath = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : Path.Combine(AppContext.BaseDirectory, "SideDesk-diagnostics.txt");

        var builder = new StringBuilder();
        builder.AppendLine("SideDesk diagnostics");
        builder.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Executable: " + StartupManager.GetExecutablePath());
        builder.AppendLine("OS: " + Environment.OSVersion);
        builder.AppendLine("Screen work area: " + SystemParameters.WorkArea);
        builder.AppendLine("Startup registration: " + StartupManager.IsEnabled());

        var desktopVisible = DesktopIcons.TryGetVisible(out var visible);
        builder.AppendLine("Desktop icon list found: " + desktopVisible);
        builder.AppendLine("Desktop icon list visible: " + (desktopVisible ? visible : "unknown"));

        foreach (var directory in DesktopCatalog.GetDesktopDirectories())
        {
            builder.AppendLine("Desktop folder: " + directory + " (exists=" + Directory.Exists(directory) + ")");
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var count = new DirectoryInfo(directory).EnumerateFileSystemInfos()
                    .Count(info =>
                    {
                        try
                        {
                            return (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
                        }
                        catch
                        {
                            return false;
                        }
                    });
                builder.AppendLine("  visible entries: " + count);
            }
            catch (Exception ex)
            {
                builder.AppendLine("  read failed: " + ex.Message);
            }
        }

        try
        {
            File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
            AppLog.Write("诊断信息已写入: " + outputPath);
        }
        catch (Exception ex)
        {
            AppLog.Write("写入诊断信息失败。", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("未处理的界面异常。", e.Exception);
        try
        {
            DesktopIcons.RestoreCapturedState();
            MessageBox.Show(
                "SideDesk 遇到错误，桌面图标已尝试恢复。\n详情见 SideDesk.log。",
                "SideDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore secondary failures.
        }

        e.Handled = true;
        Shutdown(1);
    }

    private void CleanupAndRestore()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        {
            return;
        }

        try
        {
            DesktopIcons.RestoreCapturedState();
        }
        catch (Exception ex)
        {
            AppLog.Write("恢复桌面图标时出错。", ex);
        }

        try
        {
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            _mutex?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write("释放单实例资源时出错。", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupAndRestore();
        AppLog.Write("SideDesk 退出。");
        base.OnExit(e);
    }
}
