using System.Windows.Controls.Primitives;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerModeWidget;

internal static class UiSmoke
{
    public static void ErrorFeedback()
    {
        // A provider can report success while Windows refuses to apply a mode.
        var api = new FakePowerApi { Overlay = Guid.Empty, IgnoreSet = true };
        var window = new MainWindow(new PowerModeService(api));
        Check(PresentationSource.FromVisual(window) is null, "Constructing UI must not create a native desktop host.");
        try
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var performance = (Button)window.FindName("PerformanceButton");
            performance.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var error = window.ToolTip as string;
            Check(error is not null && error.Contains("切换失败"), "Missing switch failure tooltip.");
            Pump(TimeSpan.FromMilliseconds(2200));
            Check(Equals(error, window.ToolTip), "Polling erased switch error tooltip too soon.");
            foreach (var mode in Enum.GetValues(typeof(PowerMode)).Cast<PowerMode>())
                Check(((Button)window.FindName($"{mode}Button")).Tag is null, "Failed switch should not display selection.");
            var balanced = (Button)window.FindName("BalancedButton");
            balanced.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            AssertMode(window, PowerMode.Balanced);
            Check(window.ToolTip is null, "Successful manual retry should clear error immediately.");
        }
        finally { window.Close(); }
    }
    public static void Run(Action<string, Action> test)
    {
        var native = new NativePowerApi();
        Check(native.ReadOverlay(out var original) == 0, "Cannot capture original overlay.");
        var service = new PowerModeService();
        var window = new MainWindow();
        var handle = IntPtr.Zero;
        try
        {
            handle = window.Show().Handle;
            Pump(TimeSpan.FromMilliseconds(150));
            test("UI startup preserves actual power mode", () =>
            {
                Check(native.ReadOverlay(out var current) == 0 && current == original, "Startup changed power mode.");
                AssertMode(window, service.ReadCurrentMode());
            });
            DesktopHostFailureSmoke.Run(test);
            DesktopShellSmoke.Run(window, test);
            foreach (var mode in Enum.GetValues(typeof(PowerMode)).Cast<PowerMode>())
            {
                test($"UI click {mode} changes Windows and highlights one button", () =>
                {
                    var button = (Button)window.FindName($"{mode}Button");
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Check(service.ReadCurrentMode() == mode, $"Windows did not apply {mode}.");
                    AssertMode(window, mode);
                    Capture(window, mode.ToString());
                });
            }
            test("UI follows external power mode changes within polling interval", () =>
            {
                service.SetMode(PowerMode.Efficiency);
                Pump(TimeSpan.FromMilliseconds(2200));
                AssertMode(window, PowerMode.Efficiency);
                service.SetMode(PowerMode.Balanced);
                Pump(TimeSpan.FromMilliseconds(2200));
                AssertMode(window, PowerMode.Balanced);
            });
        }
        finally
        {
            try
            {
                if (handle != IntPtr.Zero)
                    test("Context-menu Exit disposes the native widget HWND", () =>
                    {
                        var exit = window.FindName("ExitMenuItem") as MenuItem
                            ?? window.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "ExitMenuItem");
                        exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        Check(!IsWindow(handle), "Context-menu Exit leaked the native widget HWND.");
                    });
                window.Close();
            }
            finally
            {
                test("UI tests restore original Windows power mode", () =>
                {
                    Check(native.SetOverlay(original) == 0, "Restore API failed.");
                    Check(native.ReadOverlay(out var restored) == 0 && restored == original, "Original mode not restored.");
                });
            }
        }
    }

    private static void AssertMode(MainWindow window, PowerMode? expected)
    {
        foreach (var mode in Enum.GetValues(typeof(PowerMode)).Cast<PowerMode>())
        {
            var button = (Button)window.FindName($"{mode}Button");
            Check((button.Tag as string == "Active") == (mode == expected), $"Wrong highlight for {mode}.");
        }

    }

    private static void Capture(FrameworkElement window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth * 2, (int)window.ActualHeight * 2,
            192, 192, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("artifacts/ui");
        using var output = File.Create($"artifacts/ui/{name}.png");
        encoder.Save(output);
    }

    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
