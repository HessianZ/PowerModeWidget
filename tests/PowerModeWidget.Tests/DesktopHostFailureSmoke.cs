using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PowerModeWidget;

internal static class DesktopHostFailureSmoke
{
    public static void Run(Action<string, Action> test)
    {
        test("Different per-monitor DPI contexts are rejected before reparenting", () =>
        {
            var desktop = IntPtr.Zero;
            EnumWindows((handle, _) =>
            {
                desktop = FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null);
                return desktop == IntPtr.Zero;
            }, IntPtr.Zero);
            Check(desktop != IntPtr.Zero, "Cannot locate Explorer desktop view.");
            var desktopContext = GetWindowDpiAwarenessContext(desktop);
            Check(GetAwarenessFromDpiAwarenessContext(desktopContext) == 2, "Expected per-monitor Explorer desktop.");
            var alternateContext = AreDpiAwarenessContextsEqual(desktopContext, new IntPtr(-4))
                ? new IntPtr(-3) : new IntPtr(-4);
            var window = new Window { ShowInTaskbar = false, ShowActivated = false, Topmost = false };
            try
            {
                var priorThreadContext = SetThreadDpiAwarenessContext(alternateContext);
                Check(priorThreadContext != IntPtr.Zero, "Cannot set temporary test thread DPI context.");
                IntPtr handle;
                try { handle = new WindowInteropHelper(window).EnsureHandle(); }
                finally { SetThreadDpiAwarenessContext(priorThreadContext); }

                var originalContext = GetWindowDpiAwarenessContext(handle);
                Check(GetAwarenessFromDpiAwarenessContext(originalContext) == 2,
                    "Test window must share desktop's broad per-monitor category.");
                Check(!AreDpiAwarenessContextsEqual(originalContext, desktopContext),
                    "Test window needs a distinct V1/V2 context.");
                var originalParent = GetParent(handle);
                var originalStyle = GetWindowLong(handle, -16);
                var originalExtendedStyle = GetWindowLong(handle, -20);
                var rejected = false;
                try { DesktopHost.Attach(window); }
                catch (InvalidOperationException error) when (error.Message.Contains("DPI")) { rejected = true; }

                Check(rejected, "Attach accepted incompatible per-monitor DPI contexts.");
                Check(GetParent(handle) == originalParent, "Rejected attachment changed the native parent.");
                Check(GetWindowLong(handle, -16) == originalStyle &&
                    GetWindowLong(handle, -20) == originalExtendedStyle, "Rejected attachment changed native styles.");
                Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle), originalContext),
                    "Rejected attachment reset the window DPI context.");
            }
            finally { window.Close(); }
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
