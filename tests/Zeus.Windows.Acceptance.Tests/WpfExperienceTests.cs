using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zeus.Desktop;
using Zeus.Windows;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zeus.Windows.Acceptance.Tests;

public sealed class WpfExperienceTests
{
    [Fact]
    public async Task ActualApplicationLoadsRealInventoryAndRendersEveryWorkspaceAndTheme()
    {
        Assert.True(OperatingSystem.IsWindows(), "WPF acceptance requires an actual Windows desktop.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var bindingErrors = new BindingErrorListener();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            App? app = null;
            MainWindow? window = null;
            var fixture = Path.Combine(Path.GetTempPath(), "Zeus.Acceptance." + Guid.NewGuid().ToString("N"));
            try
            {
                app = new App(startMainWindow: false);
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += (_, args) =>
                {
                    args.Handled = true;
                    completion.TrySetException(args.Exception);
                    app.Shutdown();
                };
                app.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        window = new MainWindow(fixture) { Width = 1440, Height = 1024 };
                        window.Show();
                        await VerifyExperienceAsync(window);
                        Assert.Empty(bindingErrors.Errors);
                        completion.TrySetResult();
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally
                    {
                        window?.Close();
                        if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
                        app.Shutdown();
                    }
                }));
                app.Run();
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors); }
        }) { IsBackground = true, Name = "ZEUS real WPF acceptance" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(180));
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF application must exit after acceptance.");
    }

    private static async Task VerifyExperienceAsync(MainWindow window)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (window.StatusTitle != "Diagnóstico concluído" && DateTimeOffset.UtcNow < deadline)
        {
            Assert.NotEqual("Não foi possível concluir o diagnóstico", window.StatusTitle);
            await Task.Delay(100);
        }
        Assert.Equal("Diagnóstico concluído", window.StatusTitle);
        Assert.Same(window, window.DataContext);
        Assert.Same(Application.Current.Resources["BackgroundBrush"], window.Background);
        Assert.Same(Application.Current.Resources["TextBrush"], window.Foreground);
        Assert.NotEmpty(window.HardwareCards);
        Assert.NotNull(window.Snapshot);
        Assert.NotNull(window.FormalOptimizationPlan);
        Assert.NotEmpty(window.FormalPlanSummary);
        Assert.Contains(window.ProfileOptions, option => option.Value == Zeus.Windows.UsageProfile.GamingStreaming);
        Assert.Contains(window.ProfileOptions, option => option.Value == Zeus.Windows.UsageProfile.Development);
        Assert.NotNull(window.Snapshot.Cpu);
        Assert.NotNull(window.Snapshot.Memory);
        Assert.NotEmpty(window.Snapshot.Disks);
        var scanChoice = window.MaintenanceChoices.Single(choice => choice.Id == Zeus.Core.MaintenanceActionId.ScanWindowsImage);
        var repairChoice = window.MaintenanceChoices.Single(choice => choice.Id == Zeus.Core.MaintenanceActionId.RepairWindowsImage);
        scanChoice.IsSelected = true;
        repairChoice.IsSelected = true;
        Assert.False(window.CanExecute);
        Assert.Contains("Separe verificação e reparo", window.SelectedActionsText, StringComparison.OrdinalIgnoreCase);
        scanChoice.IsSelected = false;
        repairChoice.IsSelected = false;
        Assert.All(window.MaintenanceChoices, choice => Assert.False(choice.IsSelected));
        Assert.False(window.CanExecute, "Opening ZEUS must not preselect privileged maintenance.");
        Assert.False(window.CanGeneralOptimize);
        window.GeneralApplyVisual = true;
        Assert.True(window.CanGeneralOptimize);
        Assert.Contains("incluídas", window.GeneralPlanSummary);
        window.GeneralApplyVisual = false;
        var selectedMaintenance = window.MaintenanceChoices.First();
        selectedMaintenance.IsSelected = true;
        Assert.True(window.CanGeneralOptimize);
        selectedMaintenance.IsSelected = false;
        Assert.False(window.CanGeneralOptimize);
        // A missing license on any selected driver must block the entire batch.
        var licensed = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":1", "Acceptance fixture", "Fixture", "Fixture", null, true, "Fixture terms"));
        var unlicensed = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":2", "Acceptance fixture", "Fixture", "Fixture", null, false));
        window.DriverCandidates.Add(licensed); window.DriverCandidates.Add(unlicensed);
        licensed.IsSelected = true; unlicensed.IsSelected = true;
        Assert.False(window.CanInstallDriver);

        var wingetCandidate = new WingetUpdateCandidate("Fixture App", "Vendor.Fixture", "1.0", "2.0", "winget");
        var availableUpdate = new WingetUpdateRow(wingetCandidate);
        Assert.True(availableUpdate.CanInstall);
        var pendingUpdate = new WingetUpdateRow(wingetCandidate, PendingReview: true);
        Assert.False(pendingUpdate.CanInstall);
        Assert.Contains("resultado confirmado", pendingUpdate.InstallabilityReason, StringComparison.OrdinalIgnoreCase);

        licensed.EulaAccepted = true;
        Assert.True(window.CanInstallDriver);
        window.DriverCandidates.Clear();
        Assert.False(window.CanInstallDriver);

        var tabs = Assert.IsType<TabControl>(window.FindName("WorkspaceTabs"));
        Assert.Equal("workspace-tabs", AutomationProperties.GetAutomationId(tabs));
        var updateSearchButton = Assert.IsType<Button>(window.FindName("SearchPendingWindowsUpdatesButton"));
        Assert.Equal("search-pending-windows-updates", AutomationProperties.GetAutomationId(updateSearchButton));
        Assert.True(updateSearchButton.IsEnabled, "A busca online somente leitura deve exigir ação explícita do usuário.");
        var backupDatabaseButton = Assert.IsType<Button>(window.FindName("BackupDatabaseButton"));
        Assert.Equal("backup-local-data", AutomationProperties.GetAutomationId(backupDatabaseButton));
        Assert.True(backupDatabaseButton.IsEnabled, "O usuário deve conseguir criar um backup local a partir do histórico.");
        var expectedIds = new[]
        {
            "OverviewTab", "MaintenanceTab", "CleanupTab", "StartupTab",
            "ProfileTab", "DriverTab", "HardwareTab", "HistoryTab"
        };
        var actualTabs = tabs.Items.Cast<TabItem>().ToArray();
        Assert.Equal(expectedIds.Order(), actualTabs.Select(AutomationProperties.GetAutomationId).Order());
        foreach (var tab in actualTabs)
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(tab);
            Assert.NotNull(peer);
            Assert.False(string.IsNullOrWhiteSpace(peer.GetName()), "Every workspace must have a readable accessibility name.");
        }

        window.SelectedTheme = DesktopTheme.Complete;
        window.IsMinimal = false;
        tabs.SelectedIndex = 0;
        await RenderAsync(window, "zeus-complete-overview.png");
        window.SelectedTheme = DesktopTheme.Minimal;
        window.IsMinimal = true;
        Assert.Equal(Visibility.Collapsed, window.DetailedVisibility);
        await RenderAsync(window, "zeus-minimal-overview.png");
        window.IsMinimal = false;
        window.SelectedTheme = DesktopTheme.MacInspired;
        Assert.Equal(DesktopTheme.MacInspired, window.SelectedTheme);
        Assert.Equal(Visibility.Visible, window.DetailedVisibility);
        await RenderAsync(window, "zeus-mac-inspired-overview.png");

        foreach (var tab in actualTabs)
        {
            tabs.SelectedItem = tab;
            await RenderAsync(window, $"zeus-workspace-{AutomationProperties.GetAutomationId(tab)}.png");
            Assert.True(tab.IsSelected);
            var content = Assert.IsAssignableFrom<FrameworkElement>(tab.Content);
            Assert.True(content.ActualHeight > 0 && content.ActualWidth > 0,
                $"Workspace {AutomationProperties.GetAutomationId(tab)} must render its real content.");
            if (AutomationProperties.GetAutomationId(tab) == "ProfileTab")
            {
                var profile = Assert.IsType<ScrollViewer>(content);
                profile.ScrollToEnd();
                await RenderAsync(window, "zeus-general-plan-review.png");
                profile.ScrollToTop();
            }
        }
        Assert.All(window.MaintenanceChoices, choice => Assert.False(choice.IsSelected));
        Assert.False(window.CanExecute);
        // No button that repairs, installs a driver, deletes files, schedules a
        // scan, changes startup or reboots the machine is invoked by this test.
    }

    private static async Task RenderAsync(MainWindow window, string fileName)
    {
        await Task.Delay(200);
        window.UpdateLayout();
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        Assert.InRange(width, 900, 3000);
        Assert.InRange(height, 650, 2000);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var directory = Environment.GetEnvironmentVariable("ZEUS_VALIDATION_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "AcceptanceArtifacts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            encoder.Save(file);
        Assert.True(new FileInfo(path).Length > 10_000, "Acceptance must capture the rendered window, not an empty image.");
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
