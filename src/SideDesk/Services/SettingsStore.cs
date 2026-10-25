using System.IO;
using System.Text.Json;

namespace SideDesk.Services;

public sealed class AppSettings
{
    public bool Pinned { get; set; }
    public bool HideDesktopIconsOnStart { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public bool FirstRunTipShown { get; set; }
    public string SidebarColor { get; set; } = "#8B5CF6";
    public double SidebarOpacity { get; set; } = 0.90;
    public double ArrowOpacity { get; set; } = 0.50;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SideDesk");

    public static string SettingsPath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.SidebarColor = ColorUtil.NormalizeHex(settings.SidebarColor, "#8B5CF6");
            settings.SidebarOpacity = Math.Clamp(settings.SidebarOpacity, 0.35, 1.0);
            settings.ArrowOpacity = Math.Clamp(settings.ArrowOpacity, 0.15, 1.0);
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Write("读取设置失败，已使用默认设置。", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Write("保存设置失败。", ex);
        }
    }
}
