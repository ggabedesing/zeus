using System.Windows;
using System.ComponentModel;

namespace Zeus.Desktop;

public partial class App : Application
{
    private readonly bool _startMainWindow;
    private Dictionary<string, object>? _preContrastPalette;
    private static readonly string[] ContrastResourceKeys =
    [
        "BackgroundBrush", "PanelBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush",
        "WarningBrush", "ButtonBrush", "ButtonTextBrush", "SelectedTabBrush", "SelectedTabTextBrush",
        "PrimaryButtonBrush", "PrimaryTextBrush", "WarningPanelBrush", "WarningBorderBrush",
        "LogBackgroundBrush", "LogTextBrush"
    ];

    public App() : this(true) { }
    public App(bool startMainWindow) => _startMainWindow = startMainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += SystemParametersOnStaticPropertyChanged;
        RefreshSystemContrast(SystemParameters.HighContrast);
        base.OnStartup(e);
        if (_startMainWindow)
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParametersOnStaticPropertyChanged;
        base.OnExit(e);
    }

    internal void RefreshSystemContrast(bool enabled)
    {
        if (enabled)
        {
            _preContrastPalette ??= ContrastResourceKeys
                .Where(key => Resources.Contains(key))
                .ToDictionary(key => key, key => Resources[key], StringComparer.Ordinal);
            Resources["BackgroundBrush"] = SystemColors.WindowBrush;
            Resources["PanelBrush"] = SystemColors.WindowBrush;
            Resources["BorderBrush"] = SystemColors.WindowFrameBrush;
            Resources["TextBrush"] = SystemColors.WindowTextBrush;
            Resources["MutedBrush"] = SystemColors.WindowTextBrush;
            Resources["AccentBrush"] = SystemColors.WindowTextBrush;
            Resources["WarningBrush"] = SystemColors.WindowTextBrush;
            Resources["ButtonBrush"] = SystemColors.ControlBrush;
            Resources["ButtonTextBrush"] = SystemColors.ControlTextBrush;
            Resources["SelectedTabBrush"] = SystemColors.ControlBrush;
            Resources["SelectedTabTextBrush"] = SystemColors.ControlTextBrush;
            Resources["PrimaryButtonBrush"] = SystemColors.HighlightBrush;
            Resources["PrimaryTextBrush"] = SystemColors.HighlightTextBrush;
            Resources["WarningPanelBrush"] = SystemColors.WindowBrush;
            Resources["WarningBorderBrush"] = SystemColors.WindowFrameBrush;
            Resources["LogBackgroundBrush"] = SystemColors.WindowBrush;
            Resources["LogTextBrush"] = SystemColors.WindowTextBrush;
            if (MainWindow is Zeus.Desktop.MainWindow highContrastWindow) highContrastWindow.RefreshDesktopClockAppearance(true);
            return;
        }

        if (_preContrastPalette is null) return;
        foreach (var (key, value) in _preContrastPalette) Resources[key] = value;
        _preContrastPalette = null;
        if (MainWindow is Zeus.Desktop.MainWindow window) window.RefreshSelectedThemeAfterContrastChange();
    }

    private void SystemParametersOnStaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        Dispatcher.BeginInvoke(new Action(() => RefreshSystemContrast(SystemParameters.HighContrast)));
    }
}
