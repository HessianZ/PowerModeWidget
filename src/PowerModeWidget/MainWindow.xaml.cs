using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PowerModeWidget;

public partial class MainWindow : Window
{
    private readonly PowerModeService _powerModes;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTime _switchErrorUntil;
    private static readonly Brush NormalStatus = new SolidColorBrush(Color.FromRgb(166, 175, 194));
    private static readonly Brush ErrorStatus = new SolidColorBrush(Color.FromRgb(255, 159, 166));
    private static readonly Brush ActiveStatus = new SolidColorBrush(Color.FromRgb(187, 166, 255));

    public MainWindow() : this(new PowerModeService()) { }

    internal MainWindow(PowerModeService powerModes)
    {
        _powerModes = powerModes;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - ActualWidth - 24);
            Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight - 24);
            RefreshMode();
            _refreshTimer.Start();
        };
        _refreshTimer.Tick += (_, _) => RefreshMode();
        Closed += (_, _) => _refreshTimer.Stop();
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
        StatusText.Text = mode switch
        {
            PowerMode.Efficiency => "当前：最佳能效",
            PowerMode.Balanced => "当前：平衡",
            PowerMode.Performance => "当前：最佳性能",
            _ => "未识别当前电源模式"
        };
        StatusText.Foreground = NormalStatus;
        StatusText.ToolTip = null;
        StatusDot.Fill = mode is null ? NormalStatus : ActiveStatus;
    }

    private void DisplayError(string message, Exception error)
    {
        SetActiveButton(EfficiencyButton, false);
        SetActiveButton(BalancedButton, false);
        SetActiveButton(PerformanceButton, false);
        StatusText.Text = message;
        StatusText.Foreground = ErrorStatus;
        StatusText.ToolTip = error.Message;
        StatusDot.Fill = ErrorStatus;
    }

    private static void SetActiveButton(Button button, bool active)
    {
        button.Tag = active ? "Active" : null;
        System.Windows.Automation.AutomationProperties.SetHelpText(button, active ? "当前电源模式" : "切换电源模式");
    }

    private void EfficiencyButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.Efficiency);
    private void BalancedButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.Balanced);
    private void PerformanceButton_Click(object sender, RoutedEventArgs e) => SelectMode(PowerMode.Performance);
    private void TopmostButton_Click(object sender, RoutedEventArgs e) => Topmost = TopmostButton.IsChecked == true;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBlock or Grid && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
