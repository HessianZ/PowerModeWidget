using System.Threading;
using System.Windows;

namespace PowerModeWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\HessianZ.PowerModeWidget", out var firstInstance);
        if (!firstInstance) return;

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            using var host = DesktopHost.Show(new MainWindow());
            host.Disposed += (_, _) => application.Shutdown();
            application.Run();
        }
        catch (Exception error)
        {
            MessageBox.Show($"无法启动桌面小组件。\n{error.Message}", "Power Mode Widget",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
