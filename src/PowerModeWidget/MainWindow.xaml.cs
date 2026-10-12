using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PowerModeWidget;

public partial class MainWindow : UserControl
{
    private readonly PowerModeService _powerModes;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTime _switchErrorUntil;
    private bool _dragging;
    private Vector _dragOffset;

    public MainWindow() : this(new PowerModeService()) { }

    internal MainWindow(PowerModeService powerModes)
    {
        _powerModes = powerModes;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshMode();
            _refreshTimer.Start();
        };
        _refreshTimer.Tick += (_, _) => RefreshMode();
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    internal HwndSource Show() => DesktopHost.Show(this);

    internal void Close()
    {
        _refreshTimer.Stop();
        (PresentationSource.FromVisual(this) as HwndSource)?.Dispose();
    }

    private void RefreshMode()
    {
        if (DateTime.UtcNow < _switchErrorUntil) return;

        try
        {
            DisplayMode(_powerModes.ReadCurrentMode());
        }
        catch (Exception error)
        {
            DisplayError("读取失败，请稍后重试", error);
        }
    }

    private void SelectMode(PowerMode mode)
    {
        _switchErrorUntil = default;
        try
        {
            _powerModes.SetMode(mode);
            DisplayMode(_powerModes.ReadCurrentMode());
        }
        catch (Exception error)
        {
            _switchErrorUntil = DateTime.UtcNow.AddSeconds(5);
            DisplayError("切换失败，请重试", error);
        }
    }

    private void DisplayMode(PowerMode? mode)
    {
        SetActiveButton(EfficiencyButton, mode == PowerMode.Efficiency);
        SetActiveButton(BalancedButton, mode == PowerMode.Balanced);
        SetActiveButton(PerformanceButton, mode == PowerMode.Performance);
        ToolTip = mode is null ? "未识别当前电源模式" : null;
        System.Windows.Automation.AutomationProperties.SetHelpText(this,
            mode is null ? "未识别当前电源模式" : "切换电源模式");
    }

    private void DisplayError(string message, Exception error)
    {
        SetActiveButton(EfficiencyButton, false);
        SetActiveButton(BalancedButton, false);
        SetActiveButton(PerformanceButton, false);
        var detail = message + Environment.NewLine + error.Message;
        ToolTip = detail;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, detail);
        foreach (var button in new[] { EfficiencyButton, BalancedButton, PerformanceButton })
        {
            button.ToolTip = detail;
            System.Windows.Automation.AutomationProperties.SetHelpText(button, detail);
        }
    }

    private static void SetActiveButton(Button button, bool active)
    {
        button.Tag = active ? "Active" : null;
        var name = System.Windows.Automation.AutomationProperties.GetName(button);
        button.ToolTip = active ? name + "（当前）" : name;
        System.Windows.Automation.AutomationProperties.SetHelpText(button,
            active ? name + "，当前电源模式" : "切换到" + name);
    }

    private void EfficiencyButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.Efficiency);
    private void BalancedButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.Balanced);
    private void PerformanceButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.Performance);
    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not (Border or Grid) || e.ButtonState != MouseButtonState.Pressed)
            return;

        for (var element = e.OriginalSource as DependencyObject; element != null && element != this;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is Button) return;
        }

        _dragOffset = PointToScreen(e.GetPosition(this)) - PointToScreen(new Point(0, 0));
        _dragging = CaptureMouse();
        e.Handled = _dragging;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        var screenPosition = PointToScreen(e.GetPosition(this)) - _dragOffset;
        try
        {
            DesktopHost.Move(this, screenPosition.X, screenPosition.Y);
        }
        catch (Exception error)
        {
            EndDrag();
            DisplayError("移动失败，请重试", error);
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();
    private void Window_LostMouseCapture(object sender, MouseEventArgs e) => _dragging = false;

    private void EndDrag()
    {
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
