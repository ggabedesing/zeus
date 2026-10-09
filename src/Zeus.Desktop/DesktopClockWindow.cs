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
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _showDate = true, _showSeconds, _use24HourFormat = true;
    private bool _alwaysOnTop, _hideDuringFullscreen = true;

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
        _timer.Tick += (_, _) => { _timer.Stop(); UpdateTime(); };
        _fullscreenTimer.Tick += (_, _) => RefreshFullscreenVisibility();
        Loaded += (_, _) => UpdateTime();
        Loaded += (_, _) => { RefreshFullscreenVisibility(); _fullscreenTimer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _fullscreenTimer.Stop(); };
    }

    internal TimeSpan NextUpdateInterval => _timer.Interval;

    public void Configure(bool showDate, bool showSeconds, bool use24HourFormat, bool alwaysOnTop, double opacity, DesktopClockSize size, Brush accentBrush, bool highContrast, bool hideDuringFullscreen = true)
    {
        _showDate = showDate; _showSeconds = showSeconds; _use24HourFormat = use24HourFormat;
        _alwaysOnTop = alwaysOnTop; _hideDuringFullscreen = hideDuringFullscreen; Topmost = alwaysOnTop;
        Opacity = highContrast ? 1 : Math.Clamp(opacity, 0.45, 1);
        var (timeSize, dateSize, width, height, padding) = size switch
        {
            DesktopClockSize.Compact => (24d, 10d, 180d, 70d, new Thickness(12, 7, 12, 8)),
            DesktopClockSize.Large => (42d, 14d, 280d, 112d, new Thickness(22, 14, 22, 15)),
            _ => (32d, 12d, 220d, 88d, new Thickness(18, 10, 18, 11))
        };
        _time.FontSize = timeSize;
        _date.FontSize = dateSize;
        _surface.Padding = padding;
        Width = width;
        Height = height;
        _time.Foreground = highContrast ? SystemColors.WindowTextBrush : accentBrush;
        _date.Foreground = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(0xC8, 0xD4, 0xE0));
        _surface.Background = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(225, 20, 28, 39));
        _surface.BorderBrush = highContrast ? SystemColors.WindowFrameBrush : new SolidColorBrush(Color.FromArgb(100, 130, 160, 185));
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var position = ClampPosition(Left, Top, width, height, virtualScreen);
        Left = position.X;
        Top = position.Y;
        UpdateTime();
        RefreshFullscreenVisibility();
    }

    internal void RefreshFullscreenVisibility()
    {
        if (IsLoaded) Visibility = ResolveFullscreenVisibility(_alwaysOnTop, _hideDuringFullscreen, FullscreenWindowDetector.IsForegroundFullscreen());
    }

    internal static Visibility ResolveFullscreenVisibility(bool alwaysOnTop, bool hideDuringFullscreen, bool foregroundIsFullscreen) =>
        alwaysOnTop && hideDuringFullscreen && foregroundIsFullscreen ? Visibility.Hidden : Visibility.Visible;

    internal static Point ClampPosition(double left, double top, double width, double height, Rect bounds) => new(
        Math.Clamp(left, bounds.Left, Math.Max(bounds.Left, bounds.Right - width)),
        Math.Clamp(top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height)));

    private void UpdateTime()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString(_use24HourFormat
            ? _showSeconds ? "HH:mm:ss" : "HH:mm"
            : _showSeconds ? "hh:mm:ss" : "hh:mm");
        if (!_use24HourFormat) _time.Text += now.Hour < 12 ? " AM" : " PM";
        _date.Text = now.ToString("dddd, d 'de' MMMM");
        _date.Visibility = _showDate ? Visibility.Visible : Visibility.Collapsed;

        if (!IsLoaded) return;
        _timer.Interval = _showSeconds
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
        if (_timer.Interval <= TimeSpan.Zero) _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Stop();
        _timer.Start();
    }
}
