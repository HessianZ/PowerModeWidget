using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PowerModeWidget;

internal static class DesktopHost
{
    private const int StyleIndex = -16;
    private const int ExtendedStyleIndex = -20;
    private const int ChildStyle = 0x40000000;
    private const int PopupStyle = unchecked((int)0x80000000);
    private const int AppWindowStyle = 0x00040000;
    private const int ToolWindowStyle = 0x00000080;
    private const int NoActivateStyle = 0x08000000;
    private const int TopmostStyle = 0x00000008;
    private const uint NoSize = 0x0001;
    private const uint NoActivate = 0x0010;
    private const uint FrameChanged = 0x0020;

    internal static void Attach(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Topmost = false;
        var handle = Handle(window);
        var desktopView = FindDesktopView();
        if (desktopView == IntPtr.Zero)
            throw new InvalidOperationException("未找到 Windows 桌面图标宿主。请确认 Explorer 桌面正在运行。");

        // Cross-process SetParent can reset DPI awareness when the modes differ.
        var windowContext = GetWindowDpiAwarenessContext(handle);
        var desktopContext = GetWindowDpiAwarenessContext(desktopView);
        if (!AreDpiAwarenessContextsEqual(windowContext, desktopContext))
            throw new InvalidOperationException("桌面与小组件的 DPI 模式不兼容，无法安全附着。");

        Check(GetWindowRect(handle, out var rectangle), "读取小组件位置失败");
        var originalStyle = GetWindowLong(handle, StyleIndex);
        var originalExtendedStyle = GetWindowLong(handle, ExtendedStyleIndex);
        SetStyle(handle, StyleIndex, (originalStyle | ChildStyle) & ~PopupStyle);
        SetStyle(handle, ExtendedStyleIndex,
            (originalExtendedStyle | ToolWindowStyle | NoActivateStyle) & ~AppWindowStyle & ~TopmostStyle);

        SetLastError(0);
        var previousParent = SetParent(handle, desktopView);
        var parentError = Marshal.GetLastWin32Error();
        if (previousParent == IntPtr.Zero && parentError != 0)
        {
            SetStyle(handle, StyleIndex, originalStyle);
            SetStyle(handle, ExtendedStyleIndex, originalExtendedStyle);
            throw new Win32Exception(parentError, "小组件附着到桌面失败");
        }

        // DefView contains the full-screen icon list. Our child belongs above that
        // list, while Explorer's desktop remains below normal application windows.
        Move(window, rectangle.Left, rectangle.Top);
    }

    // Coordinates are physical screen pixels, matching Window.PointToScreen.
    internal static void Move(Window window, double screenLeft, double screenTop)
    {
        var handle = Handle(window);
        var parent = GetParent(handle);
        if (parent == IntPtr.Zero)
            throw new InvalidOperationException("小组件尚未附着到桌面。");

        var point = new NativePoint
        {
            X = checked((int)Math.Round(screenLeft)),
            Y = checked((int)Math.Round(screenTop))
        };
        SetLastError(0);
        var mapped = MapWindowPoints(IntPtr.Zero, parent, ref point, 1);
        var mapError = Marshal.GetLastWin32Error();
        if (mapped == 0 && mapError != 0)
            throw new Win32Exception(mapError, "转换桌面坐标失败");

        Check(SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, 0, 0,
            NoSize | NoActivate | FrameChanged), "移动桌面小组件失败");
    }

    internal static void PlaceBottomRight(Window window)
    {
        var handle = Handle(window);
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Check(GetMonitorInfo(monitor, ref info), "读取屏幕工作区域失败");
        Check(GetWindowRect(handle, out var rectangle), "读取小组件尺寸失败");
        var margin = (int)Math.Round(24 * GetDpiForWindow(handle) / 96.0);
        Move(window,
            Math.Max(info.Work.Left, info.Work.Right - (rectangle.Right - rectangle.Left) - margin),
            Math.Max(info.Work.Top, info.Work.Bottom - (rectangle.Bottom - rectangle.Top) - margin));
    }

    private static IntPtr Handle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("小组件窗口尚未创建。请在 Loaded 后附着。");
        return handle;
    }

    private static IntPtr FindDesktopView()
    {
        var progman = FindWindow("Progman", null);
        var desktopView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (progman != IntPtr.Zero && desktopView != IntPtr.Zero) return desktopView;

        EnumWindows((handle, _) =>
        {
            desktopView = FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null);
            return desktopView == IntPtr.Zero;
        }, IntPtr.Zero);
        return desktopView;
    }

    private static void SetStyle(IntPtr handle, int index, int style)
    {
        SetLastError(0);
        var previous = SetWindowLong(handle, index, style);
        var error = Marshal.GetLastWin32Error();
        if (previous == 0 && error != 0)
            throw new Win32Exception(error, "设置小组件窗口样式失败");
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle Work;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativePoint point, uint count);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void SetLastError(uint error);
}
