using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using SideDesk.Models;

namespace SideDesk.Services;

/// <summary>
/// Reads the user and public desktop folders, extracts shell icons and watches
/// both folders so the sidebar follows desktop changes immediately.
/// </summary>
public sealed class DesktopCatalog : IDisposable
{
    private static readonly string[] ShortcutSuffixes = { " - 快捷方式", " - Shortcut" };

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _debounceTimer;
    private readonly object _watcherGate = new();
    private bool _disposed;

    public DesktopCatalog()
    {
        _debounceTimer = new System.Threading.Timer(
            _ => RaiseChanged(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        foreach (var directory in GetDesktopDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName
                                   | NotifyFilters.DirectoryName
                                   | NotifyFilters.LastWrite
                                   | NotifyFilters.Size
                                   | NotifyFilters.CreationTime,
                    EnableRaisingEvents = false
                };

                watcher.Created += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemChanged;
                watcher.Changed += OnFileSystemChanged;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                AppLog.Write($"监视桌面文件夹失败: {directory}", ex);
            }
        }
    }

    public event EventHandler? Changed;

    public static IReadOnlyList<string> GetDesktopDirectories()
    {
        var result = new List<string>(2);
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        if (!string.IsNullOrWhiteSpace(userDesktop))
        {
            result.Add(userDesktop);
        }

        if (!string.IsNullOrWhiteSpace(publicDesktop)
            && !result.Contains(publicDesktop, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(publicDesktop);
        }

        return result;
    }

    public async Task<IReadOnlyList<DesktopItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var entries = await Task.Run(() => EnumerateEntries(cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<DesktopItem>(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheKey = $"{entry.Path}|{entry.LastWriteTicks}|{entry.IsFolder}";
            if (!_iconCache.TryGetValue(cacheKey, out var icon))
            {
                icon = ShellIcons.Load(entry.Path, entry.IsFolder);
                _iconCache[cacheKey] = icon;
            }

            var kindLabel = entry.IsFolder
                ? "文件夹"
                : entry.KindRank == 1 ? "快捷方式" : "文件";

            items.Add(new DesktopItem
            {
                Path = entry.Path,
                DisplayName = entry.DisplayName,
                ShortName = DesktopItem.MakeShortName(entry.DisplayName),
                KindLabel = kindLabel,
                KindRank = entry.KindRank,
                IsFolder = entry.IsFolder,
                ToolTipText = $"{entry.DisplayName}\n{kindLabel} · {entry.Path}\n双击打开，右键更多",
                Icon = icon
            });
        }

        items.Sort(CompareItems);
        return items;
    }

    private List<Entry> EnumerateEntries(CancellationToken cancellationToken)
    {
        var entries = new List<Entry>(64);
        foreach (var directory in GetDesktopDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var attributes = info.Attributes;
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }

                    if (info.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var isFolder = (attributes & FileAttributes.Directory) != 0;
                    var fileName = info.Name;
                    var extension = info.Extension;
                    var displayName = fileName;

                    if (!isFolder)
                    {
                        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                        {
                            displayName = Path.GetFileNameWithoutExtension(fileName);
                            foreach (var suffix in ShortcutSuffixes)
                            {
                                if (displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                {
                                    displayName = displayName[..^suffix.Length].TrimEnd();
                                    break;
                                }
                            }
                        }
                    }

                    var kindRank = isFolder
                        ? 0
                        : extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                          || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                            ? 1
                            : 2;

                    entries.Add(new Entry(
                        info.FullName,
                        fileName,
                        string.IsNullOrWhiteSpace(displayName) ? fileName : displayName,
                        isFolder,
                        kindRank,
                        info.LastWriteTimeUtc.Ticks));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"读取桌面文件夹失败: {directory}", ex);
            }
        }

        return entries;
    }

    private static int CompareItems(DesktopItem left, DesktopItem right)
    {
        var rank = left.KindRank.CompareTo(right.KindRank);
        if (rank != 0)
        {
            return rank;
        }

        var natural = StrCmpLogicalW(left.DisplayName, right.DisplayName);
        return natural != 0
            ? natural
            : string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        lock (_watcherGate)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer.Change(550, Timeout.Infinite);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        AppLog.Write("桌面文件夹监视器出错，将自动刷新。", e.GetException());
        lock (_watcherGate)
        {
            if (!_disposed)
            {
                _debounceTimer.Change(900, Timeout.Infinite);
            }
        }
    }

    private void RaiseChanged()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Write("刷新桌面图标时出错。", ex);
        }
    }

    public void Dispose()
    {
        lock (_watcherGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _debounceTimer.Dispose();
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // Ignore disposal failures from Explorer file system events.
            }
        }

        _watchers.Clear();
        _iconCache.Clear();
    }

    private sealed record Entry(
        string Path,
        string FileName,
        string DisplayName,
        bool IsFolder,
        int KindRank,
        long LastWriteTicks);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}
