using System.Runtime.InteropServices;
using SideDesk.Interop;

namespace SideDesk.Services;

/// <summary>
/// Hides the actual Explorer desktop icon list. The desktop itself (wallpaper,
/// right-click menu, selection) keeps working; only the icons are tucked away.
/// </summary>
public static class DesktopIcons
{
    private static readonly object Gate = new();
    private static IntPtr _listView;
    private static bool _initialCaptured;
    private static bool _initialVisible = true;

    public static bool InitialStateCaptured
    {
        get
        {
            lock (Gate)
            {
                return _initialCaptured;
            }
        }
    }

    public static bool TryGetVisible(out bool visible)
    {
        lock (Gate)
        {
            var handle = FindListView();
            if (handle == IntPtr.Zero)
            {
                visible = false;
                return false;
            }

            visible = NativeMethods.IsWindowVisible(handle);
            return true;
        }
    }

    public static void CaptureInitialState()
    {
        lock (Gate)
        {
            if (_initialCaptured)
            {
                return;
            }

            var handle = FindListView();
            if (handle != IntPtr.Zero)
            {
                _initialVisible = NativeMethods.IsWindowVisible(handle);
                _initialCaptured = true;
                AppLog.Write($"已记录桌面图标初始状态: {(_initialVisible ? "显示" : "隐藏")}。");
            }
        }
    }

    public static bool SetVisible(bool visible)
    {
        lock (Gate)
        {
            var handle = FindListView();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.ShowWindow(handle, visible ? NativeMethods.SwShow : NativeMethods.SwHide);
            if (visible)
            {
                NativeMethods.RedrawWindow(
                    handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.RdwInvalidate | NativeMethods.RdwErase
                    | NativeMethods.RdwAllChildren | NativeMethods.RdwUpdateNow);
            }

            return NativeMethods.IsWindow(handle);
        }
    }

    public static bool EnsureHidden()
    {
        lock (Gate)
        {
            var handle = FindListView();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            if (NativeMethods.IsWindowVisible(handle))
            {
                NativeMethods.ShowWindow(handle, NativeMethods.SwHide);
            }

            return !NativeMethods.IsWindowVisible(handle);
        }
    }

    /// <summary>
    /// Puts the desktop icons back into the state they had before SideDesk touched
    /// them. If the initial state could not be read, this is a no-op.
    /// </summary>
    public static void RestoreCapturedState()
    {
        lock (Gate)
        {
            if (!_initialCaptured)
            {
                return;
            }

            var handle = FindListView();
            if (handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ShowWindow(handle, _initialVisible ? NativeMethods.SwShow : NativeMethods.SwHide);
            if (_initialVisible)
            {
                NativeMethods.RedrawWindow(
                    handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.RdwInvalidate | NativeMethods.RdwErase
                    | NativeMethods.RdwAllChildren | NativeMethods.RdwUpdateNow);
            }

            AppLog.Write($"退出前已恢复桌面图标: {(_initialVisible ? "显示" : "隐藏")}。");
        }
    }

    /// <summary>Unconditional repair path used by the --restore-icons command.</summary>
    public static bool ForceShow()
    {
        lock (Gate)
        {
            var handle = FindListView();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.ShowWindow(handle, NativeMethods.SwShow);
            NativeMethods.RedrawWindow(
                handle,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RdwInvalidate | NativeMethods.RdwErase
                | NativeMethods.RdwAllChildren | NativeMethods.RdwUpdateNow);
            return NativeMethods.IsWindow(handle);
        }
    }

    private static IntPtr FindListView()
    {
        if (_listView != IntPtr.Zero && NativeMethods.IsWindow(_listView))
        {
            return _listView;
        }

        _listView = IntPtr.Zero;

        // Classic layout: Progman -> SHELLDLL_DefView -> SysListView32.
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                _listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
                if (_listView == IntPtr.Zero)
                {
                    _listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                }
            }
        }

        if (_listView != IntPtr.Zero)
        {
            return _listView;
        }

        // Wallpaper/slideshow layouts host SHELLDLL_DefView under a WorkerW window.
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((topLevel, _) =>
        {
            var defView = NativeMethods.FindWindowEx(topLevel, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                return true;
            }

            var listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            if (listView == IntPtr.Zero)
            {
                listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            }

            if (listView == IntPtr.Zero)
            {
                return true;
            }

            found = listView;
            return false;
        }, IntPtr.Zero);

        _listView = found;
        return _listView;
    }
}
