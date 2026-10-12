using System.Threading;
using System.Windows;

namespace PowerModeWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\HessianZ.PowerModeWidget", out var firstInstance);
        if (!firstInstance)
            return;

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Run(new MainWindow());
    }
}
