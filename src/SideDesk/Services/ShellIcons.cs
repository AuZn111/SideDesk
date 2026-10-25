using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SideDesk.Interop;

namespace SideDesk.Services;

internal static class ShellIcons
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSysIconIndex = 0x000004000;
    private const int ShilJumbo = 0x4;
    private const int IldTransparent = 0x00000001;

    public static ImageSource? Load(string path, bool isFolder)
    {
        try
        {
            var index = GetIconIndex(path);
            if (index >= 0)
            {
                var jumbo = GetImageListIcon(index);
                if (jumbo is not null)
                {
                    return jumbo;
                }
            }

            var handle = GetIconHandle(path);
            if (handle != IntPtr.Zero)
            {
                return FromHIcon(handle, 256);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"读取图标失败: {path}", ex);
        }

        var name = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        return IconFactory.CreateFallback(name, isFolder);
    }

    private static int GetIconIndex(string path)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiSysIconIndex | ShgfiLargeIcon);
        return result == IntPtr.Zero ? -1 : info.iIcon;
    }

    private static IntPtr GetIconHandle(string path)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);
        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }

    private static ImageSource? GetImageListIcon(int index)
    {
        var iid = typeof(IImageList).GUID;
        IImageList? imageList = null;
        try
        {
            var hr = SHGetImageList(ShilJumbo, ref iid, out imageList);
            if (hr != 0 || imageList is null)
            {
                return null;
            }

            var getIconResult = imageList.GetIcon(index, IldTransparent, out var hIcon);
            if (getIconResult != 0 || hIcon == IntPtr.Zero)
            {
                return null;
            }

            return FromHIcon(hIcon, 256);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (imageList is not null)
            {
                Marshal.ReleaseComObject(imageList);
            }
        }
    }

    private static ImageSource? FromHIcon(IntPtr hIcon, int targetSize)
    {
        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(targetSize, targetSize));
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            AppLog.Write("转换图标失败。", ex);
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig]
        int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);

        [PreserveSig]
        int ReplaceIcon(int i, IntPtr hicon, out int pi);

        [PreserveSig]
        int SetOverlayImage(int iImage, int iOverlay);

        [PreserveSig]
        int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);

        [PreserveSig]
        int AddMasked(IntPtr hbmImage, uint crMask, out int pi);

        [PreserveSig]
        int Draw(IntPtr pimldp);

        [PreserveSig]
        int Remove(int i);

        [PreserveSig]
        int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShellFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(
        int iImageList,
        ref Guid riid,
        out IImageList? ppv);
}
