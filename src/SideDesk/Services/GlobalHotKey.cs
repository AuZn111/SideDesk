using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SideDesk.Interop;

namespace SideDesk.Services;

public sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0x5344;

    private readonly HwndSource? _source;
    private readonly IntPtr _handle;
    private bool _registered;
    private bool _disposed;

    public GlobalHotKey(Window window, ModifierKeys modifiers, Key key, string displayName)
    {
        DisplayName = displayName;
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        var nativeModifiers = ToNative(modifiers) | NativeMethods.ModNoRepeat;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = NativeMethods.RegisterHotKey(_handle, HotKeyId, nativeModifiers, virtualKey);
        if (!_registered)
        {
            AppLog.Write($"注册全局热键失败: {displayName}");
        }
    }

    public string DisplayName { get; }

    public bool IsRegistered => _registered;

    public event EventHandler? Pressed;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static uint ToNative(ModifierKeys modifiers)
    {
        var result = 0u;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= 0x0004;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= 0x0008;
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_handle, HotKeyId);
            _registered = false;
        }

        _source?.RemoveHook(WndProc);
    }
}
