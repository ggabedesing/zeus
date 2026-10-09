using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        var openedUris = new List<string>();
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
                        window = new MainWindow(fixture, uri =>
                        {
                            openedUris.Add(uri);
                            if (uri == "ms-settings:personalization-colors") throw new InvalidOperationException("Falha de abertura simulada.");
                        }) { Width = 1440, Height = 1024 };
                        window.Show();
                        await VerifyExperienceAsync(window, fixture, openedUris);
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

    private static async Task VerifyExperienceAsync(MainWindow window, string fixture, List<string> openedUris)
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
        var themeSelector = Assert.IsType<ComboBox>(window.FindName("ThemeSelector"));
        Assert.Equal("theme-selector", AutomationProperties.GetAutomationId(themeSelector));
        var themePopup = Assert.IsType<Popup>(themeSelector.Template.FindName("PART_Popup", themeSelector));
        Assert.Equal(PopupAnimation.None, themePopup.PopupAnimation);
        var themeSelectorPeer = Assert.IsAssignableFrom<ComboBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(themeSelector));
        Assert.Equal("Tema do aplicativo", themeSelectorPeer.GetName());
        var expandCollapse = Assert.IsAssignableFrom<IExpandCollapseProvider>(themeSelectorPeer.GetPattern(PatternInterface.ExpandCollapse));
        Assert.Equal(3, themeSelector.Items.Count);
        var technicalModeToggle = Assert.IsType<CheckBox>(window.FindName("TechnicalModeToggle"));
        Assert.Equal("technical-mode-toggle", AutomationProperties.GetAutomationId(technicalModeToggle));
        Assert.Equal("Modo técnico: mostrar detalhes adicionais", AutomationProperties.GetName(technicalModeToggle));
        Assert.False(window.IsTechnicalMode);
        expandCollapse.Expand();
        Assert.True(themeSelector.IsDropDownOpen);
        expandCollapse.Collapse();
        Assert.False(themeSelector.IsDropDownOpen);
        foreach (var theme in new[] { DesktopTheme.Complete, DesktopTheme.Minimal, DesktopTheme.MacInspired })
        {
            window.SelectedTheme = theme;
            Assert.Same(Application.Current.Resources["PanelBrush"], themeSelector.Background);
            Assert.Same(Application.Current.Resources["TextBrush"], themeSelector.Foreground);
        }
        Assert.NotEmpty(window.HardwareCards);
        Assert.NotNull(window.Snapshot);
        Assert.NotEmpty(window.DeviceRepairSummary);
        Assert.NotEmpty(window.EventDiagnosticSummary);
        if (window.Snapshot.WindowsInventory?.NetworkConfiguration.Count > 0)
        {
            Assert.Contains("IP:", window.NetworkResetPreparationSummary);
            Assert.Contains("Proxy observado", window.NetworkResetPreparationSummary);
        }
        else
        {
            Assert.True(
                window.NetworkResetPreparationSummary.Contains("Configuração de rede indisponível", StringComparison.Ordinal) ||
                window.NetworkResetPreparationSummary.Contains("Nenhuma configuração de interface", StringComparison.Ordinal),
                "Missing network data must be stated explicitly before any reset can be prepared.");
        }
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
        var organizePreviewButton = Assert.IsType<Button>(window.FindName("PreviewDesktopOrganizationButton"));
        Assert.Equal("preview-desktop-organization", AutomationProperties.GetAutomationId(organizePreviewButton));
        Assert.True(organizePreviewButton.IsEnabled);
        var organizeApplyButton = Assert.IsType<Button>(window.FindName("ApplyDesktopOrganizationButton"));
        Assert.Equal("apply-desktop-organization", AutomationProperties.GetAutomationId(organizeApplyButton));
        Assert.False(organizeApplyButton.IsEnabled, "A organização exige uma prévia explícita.");
        Assert.Null(window.DesktopOrganizationPreview);
        var windowsSettingsButtons = new[]
        {
            (Name: "OpenWindowsThemesButton", AutomationId: "open-windows-themes", Uri: "ms-settings:themes"),
            (Name: "OpenWindowsColorsButton", AutomationId: "open-windows-colors", Uri: "ms-settings:personalization-colors"),
            (Name: "OpenWindowsStartButton", AutomationId: "open-windows-start", Uri: "ms-settings:personalization-start"),
            (Name: "OpenWindowsTaskbarButton", AutomationId: "open-windows-taskbar", Uri: "ms-settings:taskbar"),
            (Name: "OpenWindowsSoundButton", AutomationId: "open-windows-sound", Uri: "ms-settings:sound"),
            (Name: "OpenWindowsLockScreenButton", AutomationId: "open-windows-lock-screen", Uri: "ms-settings:lockscreen")
        };
        foreach (var entry in windowsSettingsButtons)
        {
            var button = Assert.IsType<Button>(window.FindName(entry.Name));
            Assert.Equal(entry.AutomationId, AutomationProperties.GetAutomationId(button));
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            Assert.Equal(entry.Uri, Assert.Single(openedUris));
            if (entry.Uri == "ms-settings:personalization-colors")
            {
                Assert.Equal("Configurações do Windows não foram abertas", window.StatusTitle);
                Assert.Contains("Falha de abertura simulada", window.StatusDetail, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("Configurações oficiais do Windows abertas", window.StatusTitle);
                Assert.Contains("não alterou nem guardou estado para reverter", window.StatusDetail, StringComparison.OrdinalIgnoreCase);
            }
            openedUris.Clear();
        }
        Assert.NotNull(window.Snapshot.Cpu);
        Assert.NotNull(window.Snapshot.Memory);
        Assert.NotEmpty(window.Snapshot.Disks);
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Proxy do usuário (HKCU)");
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Reinicialização pendente");
        var imageHealthRow = Assert.Single(window.ExtendedHardwareRows, row => row.Title == "Integridade da imagem do Windows");
        if (window.Snapshot.WindowsInventory?.WindowsImageHealth is null)
            Assert.Contains("Não verificada nesta coleta", imageHealthRow.Detail, StringComparison.Ordinal);
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
            DriverProvider: "NVIDIA", DriverClass: "Display", DriverDate: new DateOnly(2025, 11, 4), UpdateServerSelection: 2));
        var unlicensed = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":2", "Acceptance fixture", "Fixture", "Fixture", null, false,
            UpdateServerSelection: 2));
        Assert.Equal("NVIDIA (heurística pelo nome declarado)", licensed.ProviderCategory);
        Assert.Equal("2025-11-04", licensed.DriverDate);
        Assert.Equal("Windows Update · serviço público", licensed.DriverSource);
        Assert.Contains("valida hashes e assinaturas", licensed.PackageIntegritySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não captura", licensed.PackageIntegritySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("indisponível", unlicensed.DriverDate);
        Assert.False(unlicensed.CanSelectForInstall);
        Assert.Contains("data ausente", unlicensed.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
        var unidentified = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":3", "Unidentified device", null, "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today), UpdateServerSelection: 2));
        Assert.False(unidentified.CanSelectForInstall);
        Assert.Contains("fabricante e modelo", unidentified.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
        var futureDated = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":4", "Future dated driver", "Fixture", "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today.AddDays(1)), UpdateServerSelection: 2));
        Assert.False(futureDated.CanSelectForInstall);
        Assert.Contains("futura", futureDated.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
        var managedSource = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":5", "Managed source fixture", "Fixture", "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today), UpdateServerSelection: 1));
        Assert.Equal("Windows Update Agent · servidor gerenciado", managedSource.DriverSource);
        Assert.True(managedSource.CanSelectForInstall);
        var invalidSource = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":6", "Additional service fixture", "Fixture", "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today), UpdateServerSelection: 3,
            UpdateServiceId: "12345678-1234-1234-1234-123456789abc"));
        Assert.False(invalidSource.CanSelectForInstall);
        Assert.Contains("origem", invalidSource.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("serviço adicional não permitido", invalidSource.DriverSource, StringComparison.OrdinalIgnoreCase);
        var microsoftUpdate = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":7", "Microsoft Update fixture", "Fixture", "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today), UpdateServerSelection: 3,
            UpdateServiceId: Zeus.Core.WindowsUpdateSourcePolicy.MicrosoftUpdateServiceId));
        Assert.True(microsoftUpdate.CanSelectForInstall);
        Assert.Contains("Microsoft Update", microsoftUpdate.DriverSource, StringComparison.Ordinal);
        window.DriverCandidates.Add(licensed); window.DriverCandidates.Add(unlicensed);
        licensed.IsSelected = true; unlicensed.IsSelected = true;
        Assert.False(window.CanInstallDriver);
        var workspaceTabs = Assert.IsType<TabControl>(window.FindName("WorkspaceTabs"));
        var previousTab = workspaceTabs.SelectedItem;
        var driverTab = workspaceTabs.Items.Cast<TabItem>()
            .Single(tab => AutomationProperties.GetAutomationId(tab) == "DriverTab");
        workspaceTabs.SelectedItem = driverTab;
        await Task.Delay(25);
        window.UpdateLayout();
        Assert.Contains(FindVisualDescendants<TextBlock>(window), block =>
            block.Text == licensed.PackageIntegritySummary);
        workspaceTabs.SelectedItem = previousTab;

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
        var verifyDriverButton = Assert.IsType<Button>(window.FindName("VerifyPendingDriverUpdatesButton"));
        Assert.Equal("verify-pending-driver-updates", AutomationProperties.GetAutomationId(verifyDriverButton));
        Assert.False(verifyDriverButton.IsEnabled, "A reconsulta de driver só fica disponível para histórico pendente com origem registrada.");
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
        window.Width = 900;
        window.Height = 650;
        Assert.True(window.WorkspaceTabs.ActualWidth > 0 && window.WorkspaceTabs.ActualHeight > 0,
            "The workspaces must remain available at the minimum supported viewport.");
        AssertControlFitsWindow(window, technicalModeToggle);
        AssertControlFitsWindow(window, FindVisualDescendants<Button>(window).Single(button => AutomationProperties.GetAutomationId(button) == "refresh-diagnostics"));
        AssertControlFitsWindow(window, FindVisualDescendants<Button>(window).Single(button => AutomationProperties.GetAutomationId(button) == "export-report"));
        var diagnosticPackageButton = FindVisualDescendants<Button>(window).Single(button => AutomationProperties.GetAutomationId(button) == "export-diagnostic-package");
        Assert.Equal("Salvar pacote de diagnóstico ZIP", AutomationProperties.GetName(diagnosticPackageButton));
        Assert.True(diagnosticPackageButton.IsEnabled);
        AssertControlFitsWindow(window, diagnosticPackageButton);
        var navigationScroll = Assert.IsType<ScrollViewer>(tabs.Template.FindName("WorkspaceTabNavigationScrollViewer", tabs));
        Assert.True(navigationScroll.ScrollableHeight > 0, "The vertical navigation must offer scrolling when all workspaces do not fit.");
        navigationScroll.ScrollToEnd();
        await Task.Delay(50);
        window.UpdateLayout();
        var navigationViewport = navigationScroll.TransformToAncestor(window).TransformBounds(new Rect(navigationScroll.RenderSize));
        var historyTab = actualTabs.Single(tab => AutomationProperties.GetAutomationId(tab) == "HistoryTab");
        var historyTabBounds = historyTab.TransformToAncestor(window).TransformBounds(new Rect(historyTab.RenderSize));
        Assert.True(navigationViewport.Contains(historyTabBounds), "The last workspace must fit inside the navigation viewport after scrolling.");
        await RenderAsync(window, "zeus-compact-viewport.png");
        window.Width = 1440;
        window.Height = 1024;
        window.SelectedTheme = DesktopTheme.Minimal;
        window.IsMinimal = true;
        Assert.Equal(Visibility.Collapsed, window.DetailedVisibility);
        technicalModeToggle.IsChecked = true;
        Assert.True(window.IsTechnicalMode);
        Assert.Equal(Visibility.Visible, window.DetailedVisibility);
        var technicalPreferencesDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        DesktopPreferences persistedPreferences;
        do
        {
            await Task.Delay(25);
            persistedPreferences = await new DesktopStorage(fixture).ReadPreferencesAsync();
        } while (!persistedPreferences.IsTechnicalMode && DateTimeOffset.UtcNow < technicalPreferencesDeadline);
        Assert.True(persistedPreferences.IsTechnicalMode, "Technical mode must persist in the SQLite-backed app settings.");
        window.SelectedTheme = DesktopTheme.MacInspired;
        Assert.True(window.IsTechnicalMode, "Changing the application theme must not change the technical-mode preference.");
        Assert.Equal(Visibility.Visible, window.DetailedVisibility);
        window.SelectedTheme = DesktopTheme.Minimal;
        technicalModeToggle.IsChecked = false;
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
        Assert.Same(SystemColors.WindowBrush, themeSelector.Background);
        Assert.Same(SystemColors.WindowTextBrush, themeSelector.Foreground);
        Assert.Same(SystemColors.HighlightBrush, app.Resources["PrimaryButtonBrush"]);
        window.SelectedTheme = DesktopTheme.Complete;
        app.RefreshSystemContrast(false);
        Assert.Equal(DesktopTheme.Complete, window.SelectedTheme);
        Assert.Same(app.Resources["PanelBrush"], themeSelector.Background);
        Assert.Same(app.Resources["TextBrush"], themeSelector.Foreground);
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
                Assert.Contains("Mais opções oficiais de personalização", FindVisualDescendants<TextBlock>(window).Select(block => block.Text));
                Assert.True(Assert.IsType<Button>(window.FindName("OpenWindowsTaskbarButton")).IsVisible);
                var profile = Assert.IsType<ScrollViewer>(content);
                var organizeScrollTarget = Assert.IsType<Button>(window.FindName("PreviewDesktopOrganizationButton"));
                organizeScrollTarget.BringIntoView();
                profile.UpdateLayout();
                profile.ScrollToVerticalOffset(Math.Min(profile.ScrollableHeight, profile.VerticalOffset + 120));
                profile.UpdateLayout();
                await RenderAsync(window, "zeus-desktop-organization-review.png");
                AssertControlFitsWindow(window, organizeScrollTarget);
                var organizeApplyScrollTarget = Assert.IsType<Button>(window.FindName("ApplyDesktopOrganizationButton"));
                AssertControlFitsWindow(window, organizeApplyScrollTarget);
                var applyBounds = organizeApplyScrollTarget.TransformToAncestor(profile).TransformBounds(new Rect(organizeApplyScrollTarget.RenderSize));
                Assert.InRange(applyBounds.Top, 0, profile.ViewportHeight);
                Assert.True(applyBounds.Bottom <= profile.ViewportHeight, "The apply button must fit inside the profile scroll viewport.");
                Assert.False(organizeApplyScrollTarget.IsEnabled,
                    "The apply action stays unavailable until the user creates a fresh preview.");
                profile.ScrollToEnd();
                await RenderAsync(window, "zeus-general-plan-review.png");
                profile.ScrollToTop();
            }
        }
        window.DesktopClockShowDate = false;
        window.DesktopClockShowSeconds = true;
        window.DesktopClockAlwaysOnTop = true;
        window.DesktopClockOpacity = 0.72;
        window.DesktopClockEnabled = true;
        var clockDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        DesktopPreferences clockPreferences;
        do
        {
            await Task.Delay(25);
            clockPreferences = await new DesktopStorage(fixture).ReadPreferencesAsync();
        } while (clockPreferences.Clock?.Enabled != true && DateTimeOffset.UtcNow < clockDeadline);
        Assert.Equal(new DesktopClockPreferences(true, false, true, true, 0.72, clockPreferences.Clock!.Left, clockPreferences.Clock.Top), clockPreferences.Clock);
        var desktopClock = Assert.Single(Application.Current!.Windows.OfType<DesktopClockWindow>());
        Assert.True(desktopClock.IsVisible);
        Assert.True(desktopClock.Topmost);
        Assert.Equal(0.72, desktopClock.Opacity);
        window.DesktopClockEnabled = false;
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

    private static void AssertControlFitsWindow(Window window, FrameworkElement control)
    {
        Assert.True(control.IsVisible, $"The {AutomationProperties.GetName(control)} control must remain visible in the compact viewport.");
        var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight,
            $"The {AutomationProperties.GetName(control)} control must fit inside the compact viewport.");
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
