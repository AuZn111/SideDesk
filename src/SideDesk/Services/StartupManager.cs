using System.Diagnostics;
using Microsoft.Win32;

namespace SideDesk.Services;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SideDesk";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLog.Write("读取开机启动设置失败。", ex);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var exePath = GetExecutablePath();
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("写入开机启动设置失败。", ex);
            return false;
        }
    }

    public static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
        return string.IsNullOrWhiteSpace(mainModule)
            ? Path.Combine(AppContext.BaseDirectory, "SideDesk.exe")
            : mainModule;
    }
}
