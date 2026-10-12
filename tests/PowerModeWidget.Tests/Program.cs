using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using PowerModeWidget;

internal static class Program
{
    private static readonly List<TestResult> Results = [];
    private static readonly Dictionary<string, object?> Details = [];

    [STAThread]
    private static int Main(string[] args)
    {
        var live = args.Contains("--live");
        var ui = args.Contains("--ui");
        var reportPath = Path.Combine("artifacts", "test-results", ui ? "ui.json" : live ? "live.json" : "offline.json");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--live" or "--ui") continue;
            if (args[i] == "--report" && i + 1 < args.Length)
            {
                reportPath = args[++i];
                continue;
            }
            Console.Error.WriteLine("Usage: dotnet run --project tests/PowerModeWidget.Tests [-- --live | --ui] [--report <path>]");
            return 2;
        }

        if (live && ui) { Console.Error.WriteLine("Select --live or --ui, not both."); return 2; }
        if (ui) Run("UI integration suite", () => UiSmoke.Run(Run));
        else if (live) Run("Live mode switching and restoration", Live);
        else Offline();

        var failed = Results.Count(result => !result.Passed);
        var report = new
        {
            Suite = ui ? "ui" : live ? "live" : "offline",
            TimestampUtc = DateTimeOffset.UtcNow,
            WindowsVersion = Environment.OSVersion.VersionString,
            Passed = Results.Count - failed,
            Failed = failed,
            Details,
            Tests = Results
        };
        var absolutePath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Report: {absolutePath}");
        Console.WriteLine($"{Results.Count - failed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Offline()
    {
        Run("UI switch error survives polling and manual retry recovers", UiSmoke.ErrorFeedback);
        var modes = new (PowerMode Mode, Guid Overlay)[]
        {
            (PowerMode.Efficiency, new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")),
            (PowerMode.Balanced, Guid.Empty),
            (PowerMode.Performance, new Guid("ded574b5-45a0-4f42-8737-46345c09c238"))
        };
        foreach (var (mode, overlay) in modes)
        {
            Run($"Read {mode} from Windows overlay", () =>
            {
                var api = new FakePowerApi { Overlay = overlay };
                Equal<PowerMode?>(mode, new PowerModeService(api).ReadCurrentMode());
                Equal(0, api.SetCalls);
            });
            Run($"Set and verify {mode} overlay", () =>
            {
                var api = new FakePowerApi { Overlay = Guid.NewGuid() };
                new PowerModeService(api).SetMode(mode);
                Equal(overlay, api.LastRequested);
                Equal(1, api.ReadCalls);
                Equal(1, api.SetCalls);
            });
        }

        Run("Unknown overlay remains unknown", () =>
        {
            var api = new FakePowerApi { Overlay = new Guid("11111111-1111-1111-1111-111111111111") };
            Equal<PowerMode?>(null, new PowerModeService(api).ReadCurrentMode());
        });
        Run("Read failure preserves native error", () =>
        {
            var error = Throws<Win32Exception>(() => new PowerModeService(new FakePowerApi { ReadError = 5 }).ReadCurrentMode());
            Equal(5, error.NativeErrorCode);
            Check(error.Message.Contains("读取电源模式失败"), error.Message);
        });
        Run("Set failure preserves native error and skips readback", () =>
        {
            var api = new FakePowerApi { SetError = 5 };
            var error = Throws<Win32Exception>(() => new PowerModeService(api).SetMode(PowerMode.Performance));
            Equal(5, error.NativeErrorCode);
            Equal(0, api.ReadCalls);
        });
        Run("Failed readback is not reported as success", () =>
        {
            var api = new FakePowerApi { ReadError = 50 };
            var error = Throws<Win32Exception>(() => new PowerModeService(api).SetMode(PowerMode.Performance));
            Equal(50, error.NativeErrorCode);
            Equal(1, api.SetCalls);
        });
        Run("False native success with unchanged mode is rejected", () =>
        {
            var api = new FakePowerApi { IgnoreSet = true, Overlay = Guid.Empty };
            Throws<InvalidOperationException>(() => new PowerModeService(api).SetMode(PowerMode.Performance));
        });
        Run("False native success with unknown mode is rejected", () =>
        {
            var api = new FakePowerApi { IgnoreSet = true, Overlay = Guid.NewGuid() };
            Throws<InvalidOperationException>(() => new PowerModeService(api).SetMode(PowerMode.Balanced));
        });
        Run("Invalid mode does not touch Windows", () =>
        {
            var api = new FakePowerApi();
            Throws<ArgumentOutOfRangeException>(() => new PowerModeService(api).SetMode((PowerMode)999));
            Equal(0, api.SetCalls);
            Equal(0, api.ReadCalls);
        });
        Run("Unavailable Windows API gives an actionable exception", () =>
        {
            var api = new FakePowerApi { MissingApi = true };
            var error = Throws<PlatformNotSupportedException>(() => new PowerModeService(api).ReadCurrentMode());
            Check(error.InnerException is EntryPointNotFoundException, "Original native exception should be retained.");
        });
    }

    private static void Live()
    {
        var native = new NativePowerApi();
        ThrowNative(native.ReadOverlay(out var originalOverlay));
        var originalPlan = ReadBasePlan();
        Details["OriginalOverlay"] = originalOverlay;
        Details["OriginalBasePlan"] = originalPlan;
        Details["RestorationSuccessful"] = false;
        var service = new PowerModeService();
        try
        {
            foreach (var mode in Enum.GetValues<PowerMode>())
            {
                Run($"Live set/read {mode}; base plan preserved", () =>
                {
                    service.SetMode(mode);
                    Equal<PowerMode?>(mode, service.ReadCurrentMode());
                    Equal(originalPlan, ReadBasePlan());
                });
            }
        }
        finally
        {
            Run("Restore original overlay and verify base plan", () =>
            {
                ThrowNative(native.SetOverlay(originalOverlay));
                ThrowNative(native.ReadOverlay(out var restoredOverlay));
                Details["RestoredOverlay"] = restoredOverlay;
                var restoredPlan = ReadBasePlan();
                Details["RestoredBasePlan"] = restoredPlan;
                Equal(originalOverlay, restoredOverlay);
                Equal(originalPlan, restoredPlan);
                Details["RestorationSuccessful"] = true;
            });
        }
    }

    private static Guid ReadBasePlan()
    {
        ThrowNative(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    private static void ThrowNative(uint result)
    {
        if (result != 0) throw new Win32Exception(unchecked((int)result));
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Results.Add(new TestResult(name, true, null));
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            Results.Add(new TestResult(name, false, error.ToString()));
            Console.Error.WriteLine($"FAIL {name}: {error.Message}");
        }
    }

    private static void Equal<T>(T expected, T actual) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    private sealed record TestResult(string Name, bool Passed, string? Error);
}

internal sealed class FakePowerApi : IPowerApi
{
    public Guid Overlay { get; set; }
    public Guid LastRequested { get; private set; }
    public uint ReadError { get; init; }
    public uint SetError { get; init; }
    public bool IgnoreSet { get; init; }
    public bool MissingApi { get; init; }
    public int ReadCalls { get; private set; }
    public int SetCalls { get; private set; }

    public uint ReadOverlay(out Guid overlay)
    {
        ReadCalls++;
        if (MissingApi) throw new EntryPointNotFoundException();
        overlay = Overlay;
        return ReadError;
    }

    public uint SetOverlay(Guid overlay)
    {
        SetCalls++;
        LastRequested = overlay;
        if (SetError == 0 && !IgnoreSet) Overlay = overlay;
        return SetError;
    }
}
