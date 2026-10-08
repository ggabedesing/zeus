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
                        await VerifyExperienceAsync(window, fixture);
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

    private static async Task VerifyExperienceAsync(MainWindow window, string fixture)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (window.StatusTitle != "Diagnóstico concluído" && DateTimeOffset.UtcNow < deadline)
        {
            Assert.NotEqual("Não foi possível concluir o diagnóstico", window.StatusTitle);
            await Task.Delay(100);
        }
        Assert.Equal("Diagnóstico concluído", window.StatusTitle);
        Assert.False(window.FirstRunSetupComplete);
        Assert.Equal(Visibility.Visible, window.FirstRunSetupVisibility);
        var firstRunButton = Assert.IsType<Button>(window.FindName("CompleteFirstRunSetupButton"));
        Assert.Equal("complete-first-run-setup", AutomationProperties.GetAutomationId(firstRunButton));
        Assert.True(firstRunButton.IsEnabled);
        firstRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, firstRunButton));
        var firstRunDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!window.FirstRunSetupComplete && DateTimeOffset.UtcNow < firstRunDeadline) await Task.Delay(25);
        Assert.True(window.FirstRunSetupComplete, "The first-run preference must only be marked done after SQLite persistence succeeds.");
        Assert.Equal(Visibility.Collapsed, window.FirstRunSetupVisibility);
        Assert.True((await new DesktopStorage(fixture).ReadPreferencesAsync()).FirstRunSetupComplete);
        Assert.Same(window, window.DataContext);
        Assert.False(string.IsNullOrWhiteSpace(window.BuildVersion));
        Assert.Same(Application.Current.Resources["BackgroundBrush"], window.Background);
        Assert.Same(Application.Current.Resources["TextBrush"], window.Foreground);
        Assert.NotEmpty(window.HardwareCards);
        Assert.NotNull(window.Snapshot);
        Assert.NotEmpty(window.DeviceRepairSummary);
        Assert.NotEmpty(window.EventDiagnosticSummary);
        Assert.Contains("IP:", window.NetworkResetPreparationSummary);
        Assert.Contains("Proxy observado", window.NetworkResetPreparationSummary);
        Assert.All(window.EventDiagnosticRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Detail)));
        Assert.All(window.DeviceRepairRows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Title));
            Assert.False(string.IsNullOrWhiteSpace(row.Detail));
        });
        var serviceDependencyButton = Assert.IsType<Button>(window.FindName("AnalyzeServiceDependenciesButton"));
        Assert.Equal("analyze-service-dependencies", AutomationProperties.GetAutomationId(serviceDependencyButton));
        Assert.True(serviceDependencyButton.IsEnabled);
        var networkResetButton = Assert.IsType<Button>(window.FindName("OpenNetworkResetSettingsButton"));
        Assert.Equal("open-network-reset-settings", AutomationProperties.GetAutomationId(networkResetButton));
        Assert.False(networkResetButton.IsEnabled, "A navegação exige revisar os dados e confirmar preparo para recuperação.");
        var saveNetworkReferenceButton = Assert.IsType<Button>(window.FindName("SaveNetworkResetReferenceButton"));
        Assert.Equal("save-network-reference", AutomationProperties.GetAutomationId(saveNetworkReferenceButton));
        Assert.True(saveNetworkReferenceButton.IsEnabled);
        window.NetworkResetReviewed = true;
        Assert.False(networkResetButton.IsEnabled);
        window.NetworkResetRecoveryReady = true;
        Assert.True(networkResetButton.IsEnabled);
        window.NetworkResetReviewed = false;
        window.NetworkResetRecoveryReady = false;
        Assert.False(networkResetButton.IsEnabled);
        serviceDependencyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, serviceDependencyButton));
        Assert.NotEmpty(window.ServiceDependencyRows);
        Assert.Contains("serviço(s)", window.ServiceDependencySummary);
        Assert.NotNull(window.FormalOptimizationPlan);
        Assert.NotEmpty(window.FormalPlanSummary);
        Assert.Contains(window.ProfileOptions, option => option.Value == Zeus.Windows.UsageProfile.GamingStreaming);
        Assert.Contains(window.ProfileOptions, option => option.Value == Zeus.Windows.UsageProfile.Development);
        var chooseWallpaperButton = Assert.IsType<Button>(window.FindName("ChooseWallpaperPreviewButton"));
        Assert.True(chooseWallpaperButton.IsEnabled);
        var applyWallpaperButton = Assert.IsType<Button>(window.FindName("ApplyWallpaperButton"));
        Assert.False(applyWallpaperButton.IsEnabled, "A aplicação exige primeiro uma imagem escolhida e pré-visualizada.");
        Assert.NotNull(window.Snapshot.Cpu);
        Assert.NotNull(window.Snapshot.Memory);
        Assert.NotEmpty(window.Snapshot.Disks);
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Proxy do usuário (HKCU)");
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title.StartsWith("Rede · ", StringComparison.Ordinal));
        Assert.Contains("Win32_PnPSignedDriver", window.DriverInventorySummary, StringComparison.Ordinal);
        Assert.Equal(Math.Min(window.Snapshot.WindowsInventory!.Drivers.Count, 100), window.InstalledDriverRows.Count);
        Assert.All(window.InstalledDriverRows, row => Assert.Contains("Assinatura reportada:", row.Detail, StringComparison.Ordinal));
        Assert.All(window.InstalledDriverRows, row => Assert.Contains("Fabricante do dispositivo:", row.Detail, StringComparison.Ordinal));
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
        // Driver installation is individual; multiple candidates and missing consent both block it.
        var licensed = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":1", "Acceptance fixture", "Fixture", "Fixture", null, true, "Fixture terms",
            DriverProvider: "NVIDIA", DriverClass: "Display", DriverDate: new DateOnly(2025, 11, 4)));
        var unlicensed = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":2", "Acceptance fixture", "Fixture", "Fixture", null, false));
        Assert.Equal("NVIDIA (heurística pelo nome declarado)", licensed.ProviderCategory);
        Assert.Equal("2025-11-04", licensed.DriverDate);
        Assert.Equal("Windows Update · origem configurada no sistema", licensed.DriverSource);
        Assert.Equal("indisponível", unlicensed.DriverDate);
        Assert.False(unlicensed.CanSelectForInstall);
        Assert.Contains("data do driver", unlicensed.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
        var unidentified = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":3", "Unidentified device", null, "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today)));
        Assert.False(unidentified.CanSelectForInstall);
        Assert.Contains("fabricante e modelo", unidentified.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
        var futureDated = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":4", "Future dated driver", "Fixture", "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today.AddDays(1))));
        Assert.False(futureDated.CanSelectForInstall);
        Assert.Contains("futura", futureDated.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
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
        Assert.False(window.CanInstallDriver, "More than one selected candidate must never enter an install transaction.");
        unlicensed.IsSelected = false;
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
        var restoreDatabaseButton = Assert.IsType<Button>(window.FindName("RestoreDatabaseButton"));
        Assert.Equal("restore-local-data", AutomationProperties.GetAutomationId(restoreDatabaseButton));
        Assert.True(restoreDatabaseButton.IsEnabled, "A restauração deve ficar visível no histórico e aguardar a confirmação explícita do usuário.");
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
        var app = Assert.IsType<App>(Application.Current);
        app.RefreshSystemContrast(true);
        Assert.Same(SystemColors.WindowBrush, app.Resources["BackgroundBrush"]);
        Assert.Same(SystemColors.WindowTextBrush, app.Resources["TextBrush"]);
        Assert.Same(SystemColors.HighlightBrush, app.Resources["PrimaryButtonBrush"]);
        window.SelectedTheme = DesktopTheme.Complete;
        app.RefreshSystemContrast(false);
        Assert.Equal(DesktopTheme.Complete, window.SelectedTheme);
        Assert.Equal(Color.FromRgb(0x65, 0xE3, 0xE0), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);
        Assert.Equal(Color.FromRgb(0xFF, 0xD1, 0x8B), Assert.IsType<SolidColorBrush>(app.Resources["WarningBrush"]).Color);
        window.SelectedTheme = DesktopTheme.MacInspired;
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
