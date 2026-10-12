using System.ComponentModel;

namespace PowerModeWidget;

public sealed class PowerModeService
{
    internal static readonly Guid EfficiencyOverlay = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    internal static readonly Guid PerformanceOverlay = new("ded574b5-45a0-4f42-8737-46345c09c238");
    private readonly IPowerApi api;

    public PowerModeService() : this(new NativePowerApi()) { }

    internal PowerModeService(IPowerApi api) => this.api = api;

    public PowerMode? ReadCurrentMode()
    {
        var overlay = ReadOverlay();
        if (overlay == EfficiencyOverlay) return PowerMode.Efficiency;
        if (overlay == Guid.Empty) return PowerMode.Balanced;
        if (overlay == PerformanceOverlay) return PowerMode.Performance;
        return null;
    }

    public void SetMode(PowerMode mode)
    {
        var requested = mode switch
        {
            PowerMode.Efficiency => EfficiencyOverlay,
            PowerMode.Balanced => Guid.Empty,
            PowerMode.Performance => PerformanceOverlay,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        var result = InvokeNative(() => api.SetOverlay(requested));
        ThrowOnError(result, "切换电源模式失败");
        var actual = ReadOverlay();
        if (actual != requested)
        {
            throw new InvalidOperationException(
                $"Windows 未应用所选电源模式（请求：{requested}，实际：{actual}）。请检查系统电源设置或设备策略。");
        }
    }

    private Guid ReadOverlay()
    {
        Guid overlay = Guid.Empty;
        var result = InvokeNative(() => api.ReadOverlay(out overlay));
        ThrowOnError(result, "读取电源模式失败");
        return overlay;
    }

    private static uint InvokeNative(Func<uint> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new PlatformNotSupportedException("此 Windows 版本不支持电源模式切换接口。", exception);
        }
    }

    private static void ThrowOnError(uint result, string message)
    {
        if (result != 0)
        {
            throw new Win32Exception(unchecked((int)result),
                $"{message}（Windows 错误 {result}）：{new Win32Exception(unchecked((int)result)).Message}");
        }
    }
}
