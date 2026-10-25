$ErrorActionPreference = 'Stop'

if (-not ('SideDeskRestoreNative' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SideDeskRestoreNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hWnd, IntPtr updateRect, IntPtr updateRegion, uint flags);

    public static IntPtr FindDesktopListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr result = IntPtr.Zero;
        if (progman != IntPtr.Zero)
        {
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                result = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
                if (result == IntPtr.Zero)
                {
                    result = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                }
            }
        }

        if (result != IntPtr.Zero)
        {
            return result;
        }

        IntPtr found = IntPtr.Zero;
        EnumWindows((topLevel, _) =>
        {
            IntPtr defView = FindWindowEx(topLevel, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                return true;
            }

            IntPtr listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            if (listView == IntPtr.Zero)
            {
                listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            }

            if (listView == IntPtr.Zero)
            {
                return true;
            }

            found = listView;
            return false;
        }, IntPtr.Zero);

        return found;
    }
}
'@
}

$listView = [SideDeskRestoreNative]::FindDesktopListView()
if ($listView -eq [IntPtr]::Zero) {
    Write-Host 'Desktop icon area was not found. Start Explorer and try again.' -ForegroundColor Yellow
    exit 1
}

[SideDeskRestoreNative]::ShowWindow($listView, 5) | Out-Null
[SideDeskRestoreNative]::RedrawWindow(
    $listView,
    [IntPtr]::Zero,
    [IntPtr]::Zero,
    [uint32](0x0001 -bor 0x0004 -bor 0x0080 -bor 0x0100)
) | Out-Null

Write-Host 'Desktop icons restored.' -ForegroundColor Green
Start-Sleep -Seconds 1
