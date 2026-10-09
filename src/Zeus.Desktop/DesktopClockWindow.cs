using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Zeus.Desktop;

internal sealed class DesktopClockWindow : Window
{
    private readonly TextBlock _time = new() { FontSize = 32, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _date = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0)), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _surface = new() { Background = new SolidColorBrush(Color.FromArgb(225, 20, 28, 39)), BorderBrush = new SolidColorBrush(Color.FromArgb(100, 130, 160, 185)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 10, 18, 11) };
    private readonly Action _positionChanged;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _showDate = true, _showSeconds;

    public DesktopClockWindow(Action positionChanged)
    {
        _positionChanged = positionChanged;
        Title = "Relógio ZEUS";
        Width = 220; Height = 88; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = false;
        Background = Brushes.Transparent; AllowsTransparency = true;
        _surface.Child = new StackPanel { Children = { _time, _date } };
        Content = _surface;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); _positionChanged(); } };
        _timer.Tick += (_, _) => UpdateTime();
        Loaded += (_, _) => { UpdateTime(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    public void Configure(bool showDate, bool showSeconds, bool alwaysOnTop, double opacity, Brush accentBrush, bool highContrast)
    {
        _showDate = showDate; _showSeconds = showSeconds; Topmost = alwaysOnTop; Opacity = highContrast ? 1 : Math.Clamp(opacity, 0.45, 1);
        _time.Foreground = highContrast ? SystemColors.WindowTextBrush : accentBrush;
        _date.Foreground = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0));
        _surface.Background = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(225, 20, 28, 39));
        _surface.BorderBrush = highContrast ? SystemColors.WindowFrameBrush : new SolidColorBrush(Color.FromArgb(100, 130, 160, 185));
        UpdateTime();
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString(_showSeconds ? "HH:mm:ss" : "HH:mm");
        _date.Text = now.ToString("dddd, d 'de' MMMM");
        _date.Visibility = _showDate ? Visibility.Visible : Visibility.Collapsed;
    }
}
