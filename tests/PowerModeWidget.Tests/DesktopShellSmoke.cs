using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using PowerModeWidget;

internal static class DesktopShellSmoke
{
    public static void Run(Window widget, Action<string, Action> test)
    {
        var handle = new WindowInteropHelper(widget).Handle;
        test("Widget is an Explorer desktop child with no taskbar/AltTab eligibility", () =>
        {
            var parent = GetParent(handle);
            Check(parent != IntPtr.Zero, "Widget is still a standalone window.");
            var className = new StringBuilder(256);
            GetClassName(parent, className, className.Capacity);
            Check(className.ToString() == "SHELLDLL_DefView", $"Wrong desktop host: {className}");
            GetWindowThreadProcessId(parent, out var processId);
            Check(Process.GetProcessById((int)processId).ProcessName == "explorer", "Desktop host is not Explorer.");
            var style = GetWindowLongPtr(handle, -16).ToInt64();
            var extended = GetWindowLongPtr(handle, -20).ToInt64();
            Check((style & 0x40000000) != 0 && (style & 0x80000000) == 0, "Widget must be WS_CHILD, not WS_POPUP.");
            Check((extended & 0x40000) == 0 && (extended & 0x80) != 0, "Widget must not advertise a taskbar/AltTab window.");
            Check(!widget.ShowInTaskbar && !widget.Topmost && !widget.ShowActivated, "Incorrect WPF shell policy.");
        });
        test("Desktop widget movement uses parent-relative screen coordinates", () =>
        {
            Check(GetWindowRect(handle, out var before), "Cannot read widget bounds.");
            try
            {
                DesktopHost.Move(widget, before.Left - 20, before.Top - 20);
                Check(GetWindowRect(handle, out var moved), "Cannot read moved bounds.");
                Check(moved.Left == before.Left - 20 && moved.Top == before.Top - 20, "Child movement used incorrect coordinates.");
            }
            finally { DesktopHost.Move(widget, before.Left, before.Top); }
        });
        test("Normal apps cover widget; Show Desktop reveals clickable widget", () =>
        {
            Check(GetWindowRect(handle, out var bounds), "Cannot read widget bounds.");
            var point = new PointNative { X = (bounds.Left + bounds.Right) / 2, Y = (bounds.Top + bounds.Bottom) / 2 };
            var cover = new Window { Width = widget.Width, Height = widget.Height, ShowInTaskbar = false, ShowActivated = false };
            object? shell = null;
            var desktopToggled = false;
            try
            {
                cover.Show();
                var coverHandle = new WindowInteropHelper(cover).Handle;
                Check(SetWindowPos(coverHandle, IntPtr.Zero, bounds.Left, bounds.Top,
                    bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x10), "Cannot place cover window.");
                UiSmoke.Pump(TimeSpan.FromMilliseconds(250));
                Check(GetAncestor(WindowFromPoint(point), 2) == coverHandle, "Widget remained above a normal app.");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                ToggleDesktop(shell!);
                desktopToggled = true;
                UiSmoke.Pump(TimeSpan.FromMilliseconds(750));
                Check(IsWindowVisible(handle) && !IsIconic(handle), "Show Desktop hid/minimized the widget.");
                var hit = WindowFromPoint(point);
                Check(hit == handle || IsChild(handle, hit), "Widget is not clickable above desktop icons after Show Desktop.");
                foreach (var mode in Enum.GetValues<PowerMode>())
                {
                    var button = (System.Windows.Controls.Button)widget.FindName($"{mode}Button");
                    var center = button.PointToScreen(new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2));
                    var buttonHit = WindowFromPoint(new PointNative { X = (int)center.X, Y = (int)center.Y });
                    Check(buttonHit == handle || IsChild(handle, buttonHit), $"Desktop icon list intercepts {mode} button.");
                }
            }
            finally
            {
                if (desktopToggled) ToggleDesktop(shell!);
                if (shell is not null) Marshal.FinalReleaseComObject(shell);
                cover.Close();
            }
        });
    }

    private static void ToggleDesktop(object shell) => shell.GetType().InvokeMember("ToggleDesktop",
        System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PointNative point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
}
