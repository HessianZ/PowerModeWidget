using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PowerModeWidget;

internal static class DesktopHost
{
    private const uint NoSize = 0x0001;
    private const uint NoActivate = 0x0010;

    internal static HwndSource Show(MainWindow widget)
    {
        var desktop = FindDesktopHost();
        if (desktop == IntPtr.Zero)
            throw new InvalidOperationException("未找到 Windows 桌面宿主。请确认 Explorer 桌面正在运行。");
        if (!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), GetWindowDpiAwarenessContext(desktop)))
            throw new InvalidOperationException("桌面与小组件的 DPI 模式不兼容，无法安全附着。");

        var scale = GetDpiForWindow(desktop) / 96.0;
        var parameters = new HwndSourceParameters("Power Mode Widget")
        {
            ParentWindow = desktop,
            WindowStyle = unchecked((int)0x80000000) | 0x10000000, // Visible popup owned by the desktop.
            ExtendedWindowStyle = 0x08000000 | 0x00000080, // No activate, tool window.
            Width = (int)Math.Round(widget.Width * scale),
            Height = (int)Math.Round(widget.Height * scale),
            UsesPerPixelTransparency = true
        };
        var source = new HwndSource(parameters);
        try
        {
            // Give the widget its own composition surface, owned by the desktop.
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            source.RootVisual = widget;
            PlaceBottomRight(widget);
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    // Popup coordinates are physical screen pixels, matching PointToScreen.
    internal static void Move(FrameworkElement widget, double screenLeft, double screenTop)
    {
        var handle = Handle(widget);
        var parent = GetParent(handle);
        if (parent == IntPtr.Zero)
            throw new InvalidOperationException("小组件尚未附着到桌面。");
        var point = new NativePoint
        {
            X = checked((int)Math.Round(screenLeft)),
            Y = checked((int)Math.Round(screenTop))
        };
        Check(SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, 0, 0, NoSize | NoActivate), "移动桌面小组件失败");
    }

    private static void PlaceBottomRight(FrameworkElement widget)
    {
        var handle = Handle(widget);
        var monitor = MonitorFromWindow(GetParent(handle), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Check(GetMonitorInfo(monitor, ref info), "读取屏幕工作区域失败");
        Check(GetWindowRect(handle, out var rectangle), "读取小组件尺寸失败");
        var margin = (int)Math.Round(24 * GetDpiForWindow(handle) / 96.0);
        Move(widget,
            Math.Max(info.Work.Left, info.Work.Right - (rectangle.Right - rectangle.Left) - margin),
            Math.Max(info.Work.Top, info.Work.Bottom - (rectangle.Bottom - rectangle.Top) - margin));
    }

    internal static IntPtr Handle(FrameworkElement widget) =>
        (PresentationSource.FromVisual(widget) as HwndSource)?.Handle
        ?? throw new InvalidOperationException("小组件窗口尚未创建。");

    private static IntPtr FindDesktopHost()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return progman;
        var desktop = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            desktop = handle;
            return false;
        }, IntPtr.Zero);
        return desktop;
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle Work;
        public uint Flags;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr handle, out NativeRectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
}
