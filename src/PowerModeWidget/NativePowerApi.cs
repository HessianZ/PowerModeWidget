using System.Runtime.InteropServices;

namespace PowerModeWidget;

internal interface IPowerApi
{
    uint ReadOverlay(out Guid overlay);
    uint SetOverlay(Guid overlay);
}

internal sealed class NativePowerApi : IPowerApi
{
    public uint ReadOverlay(out Guid overlay) => PowerGetActualOverlayScheme(out overlay);
    public uint SetOverlay(Guid overlay) => PowerSetActiveOverlayScheme(overlay);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerGetActualOverlayScheme(out Guid overlay);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlay);
}
