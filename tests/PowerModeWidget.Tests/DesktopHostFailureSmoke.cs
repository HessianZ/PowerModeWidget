using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PowerModeWidget;

internal static class DesktopHostFailureSmoke
{
    public static void Run(Action<string, Action> test)
    {
        test("Different per-monitor DPI contexts are rejected before creating a desktop HWND", () =>
        {
            var desktopView = IntPtr.Zero;
            EnumWindows((handle, _) =>
            {
                desktopView = FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null);
                return desktopView == IntPtr.Zero;
            }, IntPtr.Zero);
            Check(desktopView != IntPtr.Zero, "Cannot locate Explorer desktop view.");
            var desktopHost = GetParent(desktopView);
            Check(desktopHost != IntPtr.Zero, "Cannot locate desktop host.");
            var desktopContext = GetWindowDpiAwarenessContext(desktopHost);
            Check(GetAwarenessFromDpiAwarenessContext(desktopContext) == 2, "Expected per-monitor Explorer desktop.");
            var alternateContext = AreDpiAwarenessContextsEqual(desktopContext, new IntPtr(-4))
                ? new IntPtr(-3) : new IntPtr(-4);
            var widget = new MainWindow(new PowerModeService(new FakePowerApi()));
            HwndSource? source = null;
            var originalHostStyle = GetWindowLong(desktopHost, -16);
            var originalHostExtendedStyle = GetWindowLong(desktopHost, -20);
            var priorThreadContext = SetThreadDpiAwarenessContext(alternateContext);
            Check(priorThreadContext != IntPtr.Zero, "Cannot set temporary test thread DPI context.");
            try
            {
                var testContext = GetThreadDpiAwarenessContext();
                Check(GetAwarenessFromDpiAwarenessContext(testContext) == 2,
                    "Test thread must share desktop's broad per-monitor category.");
                Check(!AreDpiAwarenessContextsEqual(testContext, desktopContext),
                    "Test thread needs a distinct V1/V2 context.");
                var rejected = false;
                try { source = DesktopHost.Show(widget); }
                catch (InvalidOperationException error) when (error.Message.Contains("DPI")) { rejected = true; }

                Check(rejected, "Show accepted incompatible per-monitor DPI contexts.");
                Check(PresentationSource.FromVisual(widget) is null, "Rejected show created a desktop HWND.");
                Check(GetWindowLong(desktopHost, -16) == originalHostStyle &&
                    GetWindowLong(desktopHost, -20) == originalHostExtendedStyle,
                    "Rejected show changed Explorer's native styles.");
            }
            finally
            {
                SetThreadDpiAwarenessContext(priorThreadContext);
                source?.Dispose();
                widget.Close();
            }
        });
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);
}
