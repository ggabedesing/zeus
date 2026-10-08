using System.Windows;

namespace Zeus.Desktop;

public partial class App : Application
{
    private readonly bool _startMainWindow;
    public App() : this(true) { }
    public App(bool startMainWindow) => _startMainWindow = startMainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (SystemParameters.HighContrast)
        {
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
        }
        base.OnStartup(e);
        if (_startMainWindow)
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }
}
