using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Text.Json;
using Zeus.Core;
using Zeus.Desktop;
using Zeus.Windows;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zeus.Windows.Acceptance.Tests;

public sealed class WpfExperienceTests
{
    [Fact]
    public void EquivalentCpuCoreFormattingDoesNotUsePercentUnits()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            Assert.Equal("0,5 núcleos equivalentes", MainWindow.FormatCpuCores(0.5));
            Assert.Equal("1 núcleo equivalente", MainWindow.FormatCpuCores(1));
            Assert.Equal("1,25 núcleos equivalentes", MainWindow.FormatCpuCores(1.25));
            Assert.Equal("indisponível", MainWindow.FormatCpuCores(null));
            Assert.Equal("indisponível", MainWindow.FormatCpuCores(double.NaN));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void MemoryInterleaveFormattingDoesNotClaimActiveChannels()
    {
        Assert.Equal("Interleave SMBIOS: posição 1 · profundidade 2; canais ativos não confirmados.",
            MainWindow.FormatMemoryInterleave(new MemoryModuleInfo("ChannelA-DIMM0", 8UL * 1024 * 1024 * 1024, 2666, "Fabricante", 1, 2)));
        Assert.Equal("Interleave SMBIOS: não intercalado; canais ativos não confirmados.",
            MainWindow.FormatMemoryInterleave(new MemoryModuleInfo("DIMM 1", 8UL * 1024 * 1024 * 1024, null, "Fabricante", 0, 0)));
        Assert.Equal("Interleave SMBIOS indisponível; canais ativos não confirmados.",
            MainWindow.FormatMemoryInterleave(new MemoryModuleInfo("DIMM 1", 8UL * 1024 * 1024 * 1024, null, "Fabricante")));
    }

    [Theory]
    [InlineData(WallpaperPosition.Fill, Stretch.UniformToFill, TileMode.None)]
    [InlineData(WallpaperPosition.Fit, Stretch.Uniform, TileMode.None)]
    [InlineData(WallpaperPosition.Stretch, Stretch.Fill, TileMode.None)]
    [InlineData(WallpaperPosition.Center, Stretch.Uniform, TileMode.None)]
    [InlineData(WallpaperPosition.Tile, Stretch.Fill, TileMode.Tile)]
    [InlineData(WallpaperPosition.Span, Stretch.UniformToFill, TileMode.None)]
    public void WallpaperPreviewPresentationMatchesSelectedPlacement(WallpaperPosition mode, Stretch expectedStretch, TileMode expectedTileMode)
    {
        var target = new WallpaperMonitorBounds(-1920, 0, 0, 1080);
        var preview = WallpaperPreviewPresentation.Create(mode, null, target, 16d / 9d);

        Assert.Equal(expectedStretch, preview.Stretch);
        Assert.Equal(expectedTileMode, preview.TileMode);
        Assert.Equal(16d / 9d, preview.TargetAspectRatio, 3);
        Assert.Equal(preview.TargetAspectRatio, preview.Width / preview.Height, 3);
        Assert.InRange(preview.Width, 1, 640);
        Assert.InRange(preview.Height, 1, 260);
        if (mode == WallpaperPosition.Tile)
        {
            Assert.True(preview.Viewport.Width > 0 && preview.Viewport.Height > 0);
            Assert.Contains("ilustrativa", preview.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WallpaperPreviewKeepsUnknownCurrentModeExplicit()
    {
        var preview = WallpaperPreviewPresentation.Create(null, null, null, double.NaN);

        Assert.Equal(Stretch.Uniform, preview.Stretch);
        Assert.Equal(TileMode.None, preview.TileMode);
        Assert.Equal(16d / 9d, preview.TargetAspectRatio, 3);
        Assert.Contains("desconhecido", preview.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(519.9, 1)]
    [InlineData(520, 2)]
    [InlineData(839.9, 2)]
    [InlineData(840, 3)]
    [InlineData(double.NaN, 3)]
    [InlineData(double.PositiveInfinity, 3)]
    public void HardwareOverviewCardsChooseColumnsForAvailableWidth(double width, int expectedColumns)
    {
        Assert.Equal(expectedColumns, MainWindow.ResolveHardwareCardColumns(width));
    }

    [Fact]
    public void GraphicsOverviewDoesNotTreatTheFirstReturnedAdapterAsPrimary()
    {
        var card = MainWindow.CreateGraphicsOverviewCard(
        [
            new GpuInfo("Virtual display adapter", "1.2.3"),
            new GpuInfo("NVIDIA GeForce GTX 960", "572.16")
        ]);

        Assert.Equal("2 adaptadores identificados", card.Value);
        Assert.Contains("Virtual display adapter · Driver 1.2.3", card.Detail, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce GTX 960 · Driver 572.16", card.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphicsOverviewKeepsMissingAndAdditionalAdaptersExplicit()
    {
        var unavailable = MainWindow.CreateGraphicsOverviewCard([]);
        Assert.Equal("Indisponível", unavailable.Value);
        Assert.Contains("não retornou adaptadores", unavailable.Detail, StringComparison.OrdinalIgnoreCase);

        var many = MainWindow.CreateGraphicsOverviewCard(
        [
            new GpuInfo("Adapter A", "1"),
            new GpuInfo("Adapter B", "2"),
            new GpuInfo("Adapter C", "3"),
            new GpuInfo("Adapter D", "4")
        ]);
        Assert.Equal("4 adaptadores identificados", many.Value);
        Assert.Contains("+ 1 adaptador(es) na aba Hardware.", many.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Adapter D", many.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowMinimumSizeFitsSmallWorkAreasWithoutContradictingMaximumSize()
    {
        var minimum = MainWindow.ClampMinimumWindowSize(new Size(900, 600), new Size(800, 450));

        Assert.Equal(new Size(800, 450), minimum);
        Assert.True(800 >= minimum.Width);
        Assert.True(450 >= minimum.Height);
        Assert.Equal(new Size(900, 600), MainWindow.ClampMinimumWindowSize(new Size(900, 600), new Size(1600, 900)));
    }

    [Fact]
    public void VisualLayoutCatalogLoadsVersionedDataAndRejectsExecutableFields()
    {
        var presets = VisualLayoutCatalog.Load();

        Assert.Equal(9, presets.Count);
        Assert.Equal(9, presets.Select(preset => preset.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(presets, preset => Assert.Equal("zeus-ui", preset.Scope));
        Assert.Contains(presets, preset => preset.Name == "Windows Moderno");
        Assert.Contains(presets, preset => preset.Name == "Minimalista");
        Assert.Contains(presets, preset => preset.Name == "Produtividade");
        Assert.Contains(presets, preset => preset.Name == "Aurora");
        Assert.Contains(presets, preset => preset.Id == "terminal" && preset.Theme == DesktopTheme.Minimal && preset.Accent == AppAccentColor.Green);
        Assert.Contains(presets, preset => preset.Id == "retro-amber" && preset.Theme == DesktopTheme.RetroAmber);
        Assert.Contains(presets, preset => preset.Id == "monocromatico" && preset.Theme == DesktopTheme.Monochrome);

        const string unexpectedCommand = """{"schemaVersion":1,"presets":[{"id":"teste","name":"Teste","theme":"Complete","accent":"ThemeDefault","description":"Perfil de teste","scope":"zeus-ui","command":"powershell"}]}""";
        Assert.Throws<JsonException>(() => VisualLayoutCatalog.Parse(unexpectedCommand));
        const string customJson = """{"schemaVersion":1,"presets":[{"id":"custom-criacao","name":"Criação pessoal","theme":"Cyberpunk","accent":"Violet","description":"Perfil local do ZEUS.","scope":"zeus-ui"}]}""";
        var custom = Assert.Single(VisualLayoutCatalog.ParseCustom(customJson, presets));
        Assert.Equal("custom-criacao", custom.Id);
        Assert.Contains("custom-criacao", VisualLayoutCatalog.SerializeCustom([custom]), StringComparison.Ordinal);
        const string unsafeId = """{"schemaVersion":1,"presets":[{"id":"custom-../run","name":"Inseguro","theme":"Complete","accent":"ThemeDefault","description":"Teste","scope":"zeus-ui"}]}""";
        Assert.Throws<JsonException>(() => VisualLayoutCatalog.ParseCustom(unsafeId, presets));
        const string duplicateName = """{"schemaVersion":1,"presets":[{"id":"custom-aurora","name":"Aurora","theme":"Complete","accent":"ThemeDefault","description":"Teste","scope":"zeus-ui"}]}""";
        Assert.Throws<JsonException>(() => VisualLayoutCatalog.ParseCustom(duplicateName, presets));
        Assert.Throws<JsonException>(() => VisualLayoutCatalog.Parse(new string(' ', 65 * 1024)));
        Assert.DoesNotContain("command", VisualLayoutCatalog.CreateTemplate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopClockRestoresSecondaryMonitorPositionAndClampsOffscreenCoordinates()
    {
        var virtualScreen = new Rect(-1920, -200, 3840, 1280);

        var visiblePosition = MainWindow.ResolveInitialClockPosition(-1700, -100, 280, 112, virtualScreen);
        Assert.Equal(new Point(-1700, -100), visiblePosition);

        var offscreenPosition = MainWindow.ResolveInitialClockPosition(100000, 100000, 280, 112, virtualScreen);
        Assert.Equal(new Point(1640, 968), offscreenPosition);
    }

    [Fact]
    public void DesktopClockHidesOnlyWhenTopmostAndAnotherWindowCoversItsMonitor()
    {
        var monitor = new Rect(-1920, 0, 1920, 1080);

        Assert.True(FullscreenWindowDetector.CoversMonitor(monitor, monitor));
        Assert.False(FullscreenWindowDetector.CoversMonitor(new Rect(-1920, 0, 1920, 1040), monitor),
            "A normal maximized window that leaves the taskbar area visible is not fullscreen.");
        Assert.False(FullscreenWindowDetector.CoversMonitor(new Rect(-1700, 40, 1600, 900), monitor));
        Assert.True(FullscreenWindowDetector.CoversMonitorOnClockDisplay(true, monitor, monitor));
        Assert.False(FullscreenWindowDetector.CoversMonitorOnClockDisplay(false, monitor, monitor),
            "Fullscreen on a different monitor must not hide the desktop clock.");
        Assert.Equal(Visibility.Hidden, DesktopClockWindow.ResolveFullscreenVisibility(true, true, true));
        Assert.Equal(Visibility.Visible, DesktopClockWindow.ResolveFullscreenVisibility(false, true, true));
        Assert.Equal(Visibility.Visible, DesktopClockWindow.ResolveFullscreenVisibility(true, false, true));
        Assert.Equal(Visibility.Visible, DesktopClockWindow.ResolveFullscreenVisibility(true, true, false));
    }

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
                        var constrainedWindow = new MainWindow(fixture, _ => { }, new Size(800, 450));
                        Assert.Equal(800, constrainedWindow.MinWidth);
                        Assert.Equal(450, constrainedWindow.MinHeight);
                        Assert.Equal(800, constrainedWindow.MaxWidth);
                        Assert.Equal(450, constrainedWindow.MaxHeight);
                        constrainedWindow.Close();
                        Assert.Empty(bindingErrors.Errors);
                        var restoredWindow = new MainWindow(fixture, _ => { }, new Size(800, 450)) { Width = 800, Height = 450 };
                        restoredWindow.Show();
                        var restoreDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
                        while (restoredWindow.StatusTitle != "Diagnóstico concluído" && DateTimeOffset.UtcNow < restoreDeadline)
                            await Task.Delay(50);
                        Assert.Equal("Diagnóstico concluído", restoredWindow.StatusTitle);
                        Assert.Equal("aurora", restoredWindow.SelectedVisualLayoutPreset.Id);
                        Assert.Contains(restoredWindow.VisualLayoutPresets, preset => preset.Id == "custom-criacao");
                        Assert.Equal(800, restoredWindow.MinWidth);
                        Assert.Equal(450, restoredWindow.MinHeight);
                        Assert.InRange(restoredWindow.ActualWidth, 1, 800);
                        Assert.InRange(restoredWindow.ActualHeight, 1, 450);
                        Assert.True(restoredWindow.WorkspaceTabs.ActualWidth > 0 && restoredWindow.WorkspaceTabs.ActualHeight > 0);
                        AssertControlFitsWindow(restoredWindow, FindVisualDescendants<Button>(restoredWindow).Single(button => AutomationProperties.GetAutomationId(button) == "refresh-diagnostics"));
                        var compactNavigation = Assert.IsType<ScrollViewer>(restoredWindow.WorkspaceTabs.Template.FindName("WorkspaceTabNavigationScrollViewer", restoredWindow.WorkspaceTabs));
                        Assert.True(compactNavigation.ScrollableHeight > 0);
                        compactNavigation.ScrollToEnd();
                        restoredWindow.UpdateLayout();
                        var compactViewport = compactNavigation.TransformToAncestor(restoredWindow).TransformBounds(new Rect(compactNavigation.RenderSize));
                        var lastWorkspace = restoredWindow.WorkspaceTabs.Items.Cast<TabItem>().Single(tab => AutomationProperties.GetAutomationId(tab) == "HistoryTab");
                        Assert.True(compactViewport.Contains(lastWorkspace.TransformToAncestor(restoredWindow).TransformBounds(new Rect(lastWorkspace.RenderSize))));
                        restoredWindow.Close();
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
        Assert.True(firstRunButton.Focusable && firstRunButton.IsTabStop,
            "The first-run action must participate in keyboard navigation when its workspace is selected.");
        firstRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, firstRunButton));
        var firstRunDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!window.FirstRunSetupComplete && DateTimeOffset.UtcNow < firstRunDeadline) await Task.Delay(25);
        Assert.True(window.FirstRunSetupComplete, "The first-run preference must only be marked done after SQLite persistence succeeds.");
        Assert.Equal(Visibility.Collapsed, window.FirstRunSetupVisibility);
        Assert.True((await new DesktopStorage(fixture).ReadPreferencesAsync()).FirstRunSetupComplete);
        Assert.Same(window, window.DataContext);
        Assert.False(string.IsNullOrWhiteSpace(window.BuildVersion));
        var cpuUnitsExplanation = Assert.IsType<TextBlock>(window.FindName("ProcessCpuUnitsExplanation"));
        Assert.Equal("process-cpu-units-explanation", AutomationProperties.GetAutomationId(cpuUnitsExplanation));
        Assert.Contains("1,0 equivale ao tempo de um núcleo ocupado", cpuUnitsExplanation.Text, StringComparison.Ordinal);
        Assert.Contains("não confirmam um gargalo", cpuUnitsExplanation.Text, StringComparison.Ordinal);
        var performanceLabel = Assert.IsType<TextBox>(window.FindName("PerformanceActivityLabelTextBox"));
        Assert.Equal("performance-activity-label", AutomationProperties.GetAutomationId(performanceLabel));
        Assert.Equal(80, performanceLabel.MaxLength);
        performanceLabel.Text = "  Minecraft   + OBS\n  sessão ao vivo  ";
        Assert.Equal("  Minecraft   + OBS\n  sessão ao vivo  ", window.PerformanceActivityLabel);
        Assert.Equal("Medição manual · Minecraft + OBS sessão ao vivo",
            MainWindow.BuildPerformanceSessionLabel("Medição manual", window.PerformanceActivityLabel));
        Assert.Equal("Observador adaptativo",
            MainWindow.BuildPerformanceSessionLabel("Observador adaptativo", " \t "));
        Assert.Equal(80, MainWindow.BuildPerformanceSessionLabel("Referência", new string('x', 100))["Referência · ".Length..].Length);
        Assert.Same(Application.Current.Resources["BackgroundBrush"], window.Background);
        Assert.Same(Application.Current.Resources["TextBrush"], window.Foreground);
        var themeSelector = Assert.IsType<ComboBox>(window.FindName("ThemeSelector"));
        Assert.Equal("theme-selector", AutomationProperties.GetAutomationId(themeSelector));
        var themePopup = Assert.IsType<Popup>(themeSelector.Template.FindName("PART_Popup", themeSelector));
        Assert.Equal(PopupAnimation.None, themePopup.PopupAnimation);
        var themeSelectorPeer = Assert.IsAssignableFrom<ComboBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(themeSelector));
        Assert.Equal("Tema do aplicativo", themeSelectorPeer.GetName());
        var expandCollapse = Assert.IsAssignableFrom<IExpandCollapseProvider>(themeSelectorPeer.GetPattern(PatternInterface.ExpandCollapse));
        Assert.Equal(8, themeSelector.Items.Count);
        var themeScope = Assert.IsType<ItemsControl>(window.FindName("AppearanceCapabilitiesList"));
        Assert.Equal(5, themeScope.Items.Count);
        Assert.Equal("Matriz do que o tema altera", AutomationProperties.GetName(themeScope));
        Assert.Contains(window.AppearanceCapabilities, capability => capability.Name == "Papel de parede" && capability.Status == "Ação separada");
        Assert.Contains(window.AppearanceCapabilities, capability => capability.Name == "Iniciar, barra de tarefas, sons e tela de bloqueio" && capability.Status == "Configurações do Windows");
        var customizationResources = Assert.IsType<ItemsControl>(window.FindName("CustomizationResourcesList"));
        Assert.Equal(3, customizationResources.Items.Count);
        Assert.All(window.CustomizationResources, resource => Assert.Contains("https://", resource.OfficialUri, StringComparison.Ordinal));
        Assert.Contains(window.CustomizationResources, resource => resource.Id == "rainmeter" && resource.Caution.Contains("scripts", StringComparison.OrdinalIgnoreCase));
        var animationsBeforeThemeChange = window.ReduceAnimations;
        var transparencyBeforeThemeChange = window.ReduceTransparency;
        var wallpaperBeforeThemeChange = window.SelectedWallpaperPath;
        var visualLayoutSelector = Assert.IsType<ComboBox>(window.FindName("VisualLayoutPresetSelector"));
        Assert.Equal("visual-layout-preset-selector", AutomationProperties.GetAutomationId(visualLayoutSelector));
        Assert.Equal("Perfil visual do ZEUS", AutomationProperties.GetName(visualLayoutSelector));
        Assert.Equal(9, visualLayoutSelector.Items.Count);
        var visualLayoutPreview = Assert.IsType<Border>(window.FindName("VisualLayoutPreviewCard"));
        Assert.Equal("visual-layout-preview", AutomationProperties.GetAutomationId(visualLayoutPreview));
        var applyVisualLayoutButton = Assert.IsType<Button>(window.FindName("ApplyVisualLayoutButton"));
        Assert.Equal("apply-visual-layout", AutomationProperties.GetAutomationId(applyVisualLayoutButton));
        var cancelVisualLayoutPreviewButton = Assert.IsType<Button>(window.FindName("CancelVisualLayoutPreviewButton"));
        Assert.Equal("cancel-visual-layout-preview", AutomationProperties.GetAutomationId(cancelVisualLayoutPreviewButton));
        var originalTheme = window.SelectedTheme;
        var originalAccent = window.SelectedAccentColor;
        var originalBackground = Assert.IsType<SolidColorBrush>(Application.Current.Resources["BackgroundBrush"]).Color;
        var storedAppearanceBeforePreview = await new DesktopStorage(fixture).ReadPreferencesAsync();
        var cancelCandidate = window.VisualLayoutPresets.Last(preset => preset.Theme != originalTheme || preset.Accent != originalAccent);
        window.SelectedVisualLayoutPreset = cancelCandidate;
        Assert.True(window.IsVisualLayoutPreviewing);
        Assert.True(cancelVisualLayoutPreviewButton.IsEnabled);
        Assert.Equal(originalTheme, window.SelectedTheme);
        Assert.Equal(originalAccent, window.SelectedAccentColor);
        Assert.Equal(Assert.IsType<SolidColorBrush>(window.SelectedVisualLayoutPreview.BackgroundBrush).Color,
            Assert.IsType<SolidColorBrush>(Application.Current.Resources["BackgroundBrush"]).Color);
        var storedAppearanceDuringPreview = await new DesktopStorage(fixture).ReadPreferencesAsync();
        Assert.Equal(storedAppearanceBeforePreview.Theme, storedAppearanceDuringPreview.Theme);
        Assert.Equal(storedAppearanceBeforePreview.AccentColor, storedAppearanceDuringPreview.AccentColor);
        Assert.Equal(storedAppearanceBeforePreview.VisualLayoutPresetId, storedAppearanceDuringPreview.VisualLayoutPresetId);
        cancelVisualLayoutPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelVisualLayoutPreviewButton));
        Assert.False(window.IsVisualLayoutPreviewing);
        Assert.Equal(originalBackground, Assert.IsType<SolidColorBrush>(Application.Current.Resources["BackgroundBrush"]).Color);
        foreach (var visualPreset in window.VisualLayoutPresets)
        {
            var themeBeforePreview = window.SelectedTheme;
            window.SelectedVisualLayoutPreset = visualPreset;
            Assert.Equal(themeBeforePreview, window.SelectedTheme);
            Assert.Equal(visualPreset.Theme != window.SelectedTheme || visualPreset.Accent != window.SelectedAccentColor,
                window.IsVisualLayoutPreviewing);
            Assert.Equal(Assert.IsType<SolidColorBrush>(window.SelectedVisualLayoutPreview.BackgroundBrush).Color,
                Assert.IsType<SolidColorBrush>(Application.Current.Resources["BackgroundBrush"]).Color);
            Assert.Equal(visualPreset.Name, window.SelectedVisualLayoutPreview.Name);
            Assert.NotNull(window.SelectedVisualLayoutPreview.BackgroundBrush);
            Assert.NotNull(window.SelectedVisualLayoutPreview.PanelBrush);
            Assert.NotNull(window.SelectedVisualLayoutPreview.TextBrush);
            Assert.NotNull(window.SelectedVisualLayoutPreview.AccentBrush);
            applyVisualLayoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(visualPreset.Theme, window.SelectedTheme);
            Assert.Equal(visualPreset.Accent, window.SelectedAccentColor);
            Assert.Contains("somente à interface do ZEUS", window.StatusDetail, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(animationsBeforeThemeChange, window.ReduceAnimations);
            Assert.Equal(transparencyBeforeThemeChange, window.ReduceTransparency);
            Assert.Equal(wallpaperBeforeThemeChange, window.SelectedWallpaperPath);
        }
        const string importedVisualLayout = """{"schemaVersion":1,"presets":[{"id":"custom-criacao","name":"Criação pessoal","theme":"GamingNeon","accent":"Green","description":"Paleta criada localmente para o ZEUS.","scope":"zeus-ui"}]}""";
        Assert.Null(await window.TryImportVisualLayoutManifestAsync(importedVisualLayout));
        Assert.Equal(10, window.VisualLayoutPresets.Count);
        Assert.Equal("custom-criacao", window.SelectedVisualLayoutPreset.Id);
        Assert.True(window.IsVisualLayoutPreviewing);
        Assert.True(applyVisualLayoutButton.IsEnabled);
        var storedBeforeCustomConfirm = await new DesktopStorage(fixture).ReadPreferencesAsync();
        Assert.Contains("custom-criacao", storedBeforeCustomConfirm.CustomVisualLayoutsJson, StringComparison.Ordinal);
        Assert.Equal("monocromatico", storedBeforeCustomConfirm.VisualLayoutPresetId);
        applyVisualLayoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, applyVisualLayoutButton));
        var storedAfterCustomConfirm = await new DesktopStorage(fixture).ReadPreferencesAsync();
        Assert.Equal("custom-criacao", storedAfterCustomConfirm.VisualLayoutPresetId);
        Assert.Equal(DesktopTheme.GamingNeon, storedAfterCustomConfirm.Theme);
        window.SelectedVisualLayoutPreset = window.VisualLayoutPresets.Single(preset => preset.Id == "aurora");
        applyVisualLayoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var customAccentInput = Assert.IsType<TextBox>(window.FindName("CustomAccentHexTextBox"));
        var previewCustomAccent = Assert.IsType<Button>(window.FindName("PreviewCustomAccentButton"));
        var confirmCustomAccent = Assert.IsType<Button>(window.FindName("ConfirmCustomAccentButton"));
        var cancelCustomAccent = Assert.IsType<Button>(window.FindName("CancelCustomAccentPreviewButton"));
        var initialCustomAccent = (await new DesktopStorage(fixture).ReadPreferencesAsync()).CustomAccentHex;
        customAccentInput.Text = "#00FFFF";
        Assert.True(previewCustomAccent.IsEnabled, window.CustomAccentValidationSummary);
        previewCustomAccent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, previewCustomAccent));
        Assert.True(window.IsCustomAccentPreviewing);
        Assert.Equal(initialCustomAccent, (await new DesktopStorage(fixture).ReadPreferencesAsync()).CustomAccentHex);
        Assert.Equal(ColorConverter.ConvertFromString("#00FFFF"), Assert.IsType<SolidColorBrush>(Application.Current.Resources["AccentBrush"]).Color);
        cancelCustomAccent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelCustomAccent));
        Assert.False(window.IsCustomAccentPreviewing);
        Assert.Equal(initialCustomAccent, (await new DesktopStorage(fixture).ReadPreferencesAsync()).CustomAccentHex);
        Assert.True(previewCustomAccent.IsEnabled);
        previewCustomAccent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, previewCustomAccent));
        Assert.True(confirmCustomAccent.IsEnabled);
        confirmCustomAccent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirmCustomAccent));
        await Task.Delay(150);
        Assert.Equal("#00FFFF", (await new DesktopStorage(fixture).ReadPreferencesAsync()).CustomAccentHex);
        Assert.False(window.IsCustomAccentPreviewing);
        customAccentInput.Text = "#101010";
        Assert.False(previewCustomAccent.IsEnabled, "A color that fails the required 4.5:1 contrast must not be previewable.");
        Assert.Contains("4,5:1", window.CustomAccentValidationSummary, StringComparison.Ordinal);
        customAccentInput.Text = "#00FFFF";
        var accentSelector = Assert.IsType<ComboBox>(window.FindName("AccentColorSelector"));
        Assert.Equal("accent-color-selector", AutomationProperties.GetAutomationId(accentSelector));
        Assert.Equal("Cor de destaque do aplicativo", AutomationProperties.GetName(accentSelector));
        Assert.Equal(6, accentSelector.Items.Count);
        Assert.True(Keyboard.Focus(themeSelector) == themeSelector, "The theme selector must accept keyboard focus.");
        window.UpdateLayout();
        var themeSurface = Assert.IsType<Border>(themeSelector.Template.FindName("ComboSurface", themeSelector));
        Assert.Equal(new Thickness(2), themeSurface.BorderThickness);
        Assert.Same(Application.Current.Resources["AccentBrush"], themeSurface.BorderBrush);
        var technicalModeToggle = Assert.IsType<CheckBox>(window.FindName("TechnicalModeToggle"));
        Assert.Equal("technical-mode-toggle", AutomationProperties.GetAutomationId(technicalModeToggle));
        Assert.Equal("Modo técnico: mostrar detalhes adicionais", AutomationProperties.GetName(technicalModeToggle));
        Assert.False(window.IsTechnicalMode);
        expandCollapse.Expand();
        Assert.True(themeSelector.IsDropDownOpen);
        expandCollapse.Collapse();
        Assert.False(themeSelector.IsDropDownOpen);
        foreach (var theme in Enum.GetValues<DesktopTheme>())
        {
            window.SelectedAccentColor = AppAccentColor.ThemeDefault;
            window.SelectedTheme = theme;
            Assert.Equal(theme, window.SelectedThemeOption.Value);
            Assert.False(string.IsNullOrWhiteSpace(window.SelectedThemeOption.Description));
            Assert.Equal(animationsBeforeThemeChange, window.ReduceAnimations);
            Assert.Equal(transparencyBeforeThemeChange, window.ReduceTransparency);
            Assert.Equal(wallpaperBeforeThemeChange, window.SelectedWallpaperPath);
            Assert.Same(Application.Current.Resources["PanelBrush"], themeSelector.Background);
            Assert.Same(Application.Current.Resources["TextBrush"], themeSelector.Foreground);
            Assert.Equal("Aparência do ZEUS atualizada", window.StatusTitle);
            Assert.Contains("somente a interface do ZEUS", window.StatusDetail, StringComparison.OrdinalIgnoreCase);
            if (theme == DesktopTheme.RetroAmber)
                Assert.Equal(Color.FromRgb(0xFF, 0xC8, 0x57), Assert.IsType<SolidColorBrush>(Application.Current.Resources["AccentBrush"]).Color);
            if (theme == DesktopTheme.Monochrome)
                Assert.Equal(Color.FromRgb(0xD9, 0xD9, 0xD9), Assert.IsType<SolidColorBrush>(Application.Current.Resources["AccentBrush"]).Color);
            await RenderAsync(window, $"zeus-theme-{theme}.png");
        }
        foreach (var theme in Enum.GetValues<DesktopTheme>())
        foreach (var accent in Enum.GetValues<AppAccentColor>())
        {
            window.SelectedTheme = theme;
            window.SelectedAccentColor = accent;
            AssertThemeTextContrast("TextBrush", "PanelBrush", 4.5);
            AssertThemeTextContrast("MutedBrush", "PanelBrush", 4.5);
            AssertThemeTextContrast("ButtonTextBrush", "ButtonBrush", 4.5);
            AssertThemeTextContrast("SelectedTabTextBrush", "SelectedTabBrush", 4.5);
            AssertThemeTextContrast("PrimaryTextBrush", "PrimaryButtonBrush", 4.5);
        }
        foreach (var accent in Enum.GetValues<AppAccentColor>())
        {
            window.SelectedAccentColor = accent;
            Assert.IsType<SolidColorBrush>(Application.Current.Resources["AccentBrush"]);
        }
        Assert.NotEmpty(window.HardwareCards);
        Assert.NotNull(window.Snapshot);
        Assert.Equal(MainWindow.CreateGraphicsOverviewCard(window.Snapshot.Graphics),
            Assert.Single(window.HardwareCards, card => card.Title == "Placas de vídeo"));
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
        var wallpaperPositionSelector = Assert.IsType<ComboBox>(window.FindName("WallpaperPositionSelector"));
        Assert.Equal("wallpaper-position-selector", AutomationProperties.GetAutomationId(wallpaperPositionSelector));
        Assert.Equal(7, wallpaperPositionSelector.Items.Count);
        Assert.Equal("Manter ajuste atual", Assert.IsType<WallpaperPositionOption>(wallpaperPositionSelector.SelectedItem).Name);
        Assert.Null(window.SelectedWallpaperPosition);
        Assert.Contains(window.WallpaperPositionOptions, option => option.Value == WallpaperPosition.Span && option.Description.Contains("todos os monitores", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Ajuste atual no Windows:", window.CurrentWallpaperPositionSummary, StringComparison.Ordinal);
        var wallpaperPreviewFrame = Assert.IsType<Border>(window.FindName("WallpaperPreviewFrame"));
        Assert.Equal("Prévia do papel de parede conforme o modo selecionado", AutomationProperties.GetName(wallpaperPreviewFrame));
        Assert.InRange(window.WallpaperPreviewWidth, 1, 640);
        Assert.InRange(window.WallpaperPreviewHeight, 1, 260);
        Assert.Contains("formato", window.WallpaperPreviewSummary, StringComparison.OrdinalIgnoreCase);
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
                if (entry.Uri == "ms-settings:sound")
                {
                    Assert.Equal("Configurações de som abertas", window.StatusTitle);
                    Assert.Contains("guia Sons", window.StatusDetail, StringComparison.Ordinal);
                    Assert.Contains("não aplicou nem guardou estado para reverter", window.StatusDetail, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal("Configurações oficiais do Windows abertas", window.StatusTitle);
                    Assert.Contains("não alterou nem guardou estado para reverter", window.StatusDetail, StringComparison.OrdinalIgnoreCase);
                }
            }
            openedUris.Clear();
        }
        var resourceButton = new Button { DataContext = window.CustomizationResources[0] };
        typeof(MainWindow).GetMethod("OpenCustomizationResource_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, [resourceButton, new RoutedEventArgs(Button.ClickEvent, resourceButton)]);
        Assert.Equal(window.CustomizationResources[0].OfficialUri, Assert.Single(openedUris));
        Assert.Equal("Página oficial aberta", window.StatusTitle);
        Assert.Contains("Não instalou nem configurou", window.StatusDetail, StringComparison.Ordinal);
        openedUris.Clear();
        Assert.NotNull(window.Snapshot.Cpu);
        Assert.NotNull(window.Snapshot.Memory);
        Assert.NotEmpty(window.Snapshot.Disks);
        if (window.Snapshot.MemoryModules is { } memoryModules)
        {
            var memorySummary = Assert.Single(window.ExtendedHardwareRows, row => row.Title == "RAM · Slots e canais");
            Assert.Contains("canais: não certificados", memorySummary.Detail, StringComparison.Ordinal);
            var memoryRows = window.ExtendedHardwareRows.Where(row => row.Title.StartsWith("RAM · ", StringComparison.Ordinal) && row.Title != "RAM · Slots e canais").ToArray();
            Assert.Equal(memoryModules.Count, memoryRows.Length);
            for (var index = 0; index < memoryModules.Count; index++)
                Assert.Contains(MainWindow.FormatMemoryInterleave(memoryModules[index]), memoryRows[index].Detail, StringComparison.Ordinal);
        }
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Proxy do usuário (HKCU)");
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Proxy WinHTTP padrão" &&
            row.Detail.Contains(window.Snapshot.WindowsInventory?.WinHttpProxyConfiguration is { IsAvailable: true } ? "Fonte: configuração WinHTTP padrão" : "Estado indisponível", StringComparison.Ordinal));
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Modo de inicialização firmware" &&
            row.Detail.Contains(window.Snapshot.WindowsInventory?.FirmwareBoot is { IsAvailable: true } ? "reportada pelo Windows" : "Indisponível", StringComparison.Ordinal));
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Sistema operacional" &&
            row.Detail.Contains(window.Snapshot.WindowsVersion is { IsAvailable: true } ? "build" : "Detalhes da edição/build indisponíveis", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Programas instalados" && row.Detail.Contains("Win32/Appx-MSIX", StringComparison.Ordinal));
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Reinicialização pendente");
        var inventory = window.Snapshot.WindowsInventory!;
        var scheduledTasksRow = Assert.Single(window.ExtendedHardwareRows, row => row.Title == "Tarefas agendadas");
        Assert.Contains("até 30", scheduledTasksRow.Detail, StringComparison.Ordinal);
        var taskInventoryLimited = inventory.Warnings.Any(warning => warning.StartsWith("Tarefas agendadas: amostra limitada a 500", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(taskInventoryLimited, scheduledTasksRow.Detail.Contains("amostra está incompleta", StringComparison.Ordinal));
        Assert.Equal(Math.Min(inventory.ScheduledTasks.Count, 30), window.ExtendedHardwareRows.Count(row => row.Title.StartsWith("Tarefa · ", StringComparison.Ordinal)));
        var processInventoryRow = Assert.Single(window.ExtendedHardwareRows, row => row.Title == "Processos do Windows");
        Assert.Equal(inventory.Warnings.Any(warning => warning.StartsWith("Processos: amostra limitada aos 200", StringComparison.OrdinalIgnoreCase)),
            processInventoryRow.Detail.Contains("Amostra incompleta", StringComparison.Ordinal));
        Assert.Single(window.ExtendedHardwareRows, row => row.Title == "Rotas de rede");
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title == "Serviços" && row.Detail.Contains("nenhum foi iniciado ou parado", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Math.Min(inventory.Services.Count, 30), window.ExtendedHardwareRows.Count(row => row.Title.StartsWith("Serviço · ", StringComparison.Ordinal)));
        var imageHealthRow = Assert.Single(window.ExtendedHardwareRows, row => row.Title == "Integridade da imagem do Windows");
        if (window.Snapshot.WindowsInventory?.WindowsImageHealth is null)
            Assert.Contains("Não verificada nesta coleta", imageHealthRow.Detail, StringComparison.Ordinal);
        Assert.Contains(window.ExtendedHardwareRows, row => row.Title.StartsWith("Rede · ", StringComparison.Ordinal));
        if (window.Snapshot.WindowsInventory?.NetworkConfiguration.Count is null or 0)
        {
            var networkUnavailable = Assert.Single(window.ExtendedHardwareRows, row => row.Title == "Rede · configuração IP/DNS/rotas");
            Assert.Contains("IP, DNS, gateway e rotas", networkUnavailable.Detail, StringComparison.Ordinal);
            Assert.Contains("não confirma ausência de adaptadores", networkUnavailable.Detail, StringComparison.Ordinal);
        }
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
        var partialSearchChoice = new DriverChoice(new(Guid.NewGuid().ToString("D") + ":8", "Partial result fixture", "Fixture", "Fixture adapter", null, false,
            DriverDate: DateOnly.FromDateTime(DateTime.Today), UpdateServerSelection: 2), searchComplete: false);
        Assert.False(partialSearchChoice.CanSelectForInstall);
        Assert.Contains("busca incompleta", partialSearchChoice.InstallabilityReason, StringComparison.OrdinalIgnoreCase);
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
        var automaticZeusUpdateCheck = Assert.IsType<CheckBox>(window.FindName("AutomaticZeusUpdateCheckToggle"));
        Assert.Equal("automatic-zeus-update-check", AutomationProperties.GetAutomationId(automaticZeusUpdateCheck));
        Assert.False(automaticZeusUpdateCheck.IsChecked, "A verificação de rede no início deve permanecer desativada até a pessoa optar por ela.");
        automaticZeusUpdateCheck.IsChecked = true;
        Assert.True(window.CheckZeusUpdatesAutomatically);
        var autoCheckSaveDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        DesktopPreferences updatePreferences;
        do
        {
            await Task.Delay(25);
            updatePreferences = await new DesktopStorage(fixture).ReadPreferencesAsync();
        } while (!updatePreferences.CheckZeusUpdatesAutomatically && DateTimeOffset.UtcNow < autoCheckSaveDeadline);
        Assert.True(updatePreferences.CheckZeusUpdatesAutomatically);
        automaticZeusUpdateCheck.IsChecked = false;
        do
        {
            await Task.Delay(25);
            updatePreferences = await new DesktopStorage(fixture).ReadPreferencesAsync();
        } while (updatePreferences.CheckZeusUpdatesAutomatically && DateTimeOffset.UtcNow < autoCheckSaveDeadline);
        Assert.False(updatePreferences.CheckZeusUpdatesAutomatically);
        var updateHistoryButton = Assert.IsType<Button>(window.FindName("ReadWindowsUpdateHistoryButton"));
        Assert.Equal("read-windows-update-history", AutomationProperties.GetAutomationId(updateHistoryButton));
        Assert.True(updateHistoryButton.IsEnabled, "A consulta local somente leitura do histórico também deve exigir ação explícita.");
        Assert.Contains("consultado quando você solicitar", window.WindowsUpdateHistorySummary, StringComparison.OrdinalIgnoreCase);
        var initialWindowsUpdateHistorySummary = window.WindowsUpdateHistorySummary;
        updateHistoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, updateHistoryButton));
        var historyDeadline = DateTimeOffset.UtcNow.AddSeconds(55);
        while (window.WindowsUpdateHistorySummary == initialWindowsUpdateHistorySummary && DateTimeOffset.UtcNow < historyDeadline)
            await Task.Delay(50);
        Assert.NotEqual(initialWindowsUpdateHistorySummary, window.WindowsUpdateHistorySummary);
        Assert.True(window.WindowsUpdateHistory.Count <= Zeus.Windows.WindowsUpdateService.WindowsUpdateHistoryLimit);
        Assert.True(window.WindowsUpdateHistorySummary.StartsWith("Leitura concluída", StringComparison.Ordinal) ||
            window.WindowsUpdateHistorySummary.StartsWith("Histórico incompleto/desconhecido", StringComparison.Ordinal),
            window.WindowsUpdateHistorySummary);
        var idleDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!window.CanRefresh && DateTimeOffset.UtcNow < idleDeadline) await Task.Delay(25);
        Assert.True(window.CanRefresh,
            $"A consulta do histórico deveria terminar antes da revisão do backup. Estado: {window.StatusTitle} · {window.StatusDetail}");
        var verifyDriverButton = Assert.IsType<Button>(window.FindName("VerifyPendingDriverUpdatesButton"));
        Assert.Equal("verify-pending-driver-updates", AutomationProperties.GetAutomationId(verifyDriverButton));
        Assert.False(verifyDriverButton.IsEnabled, "A reconsulta de driver só fica disponível para histórico pendente com origem registrada.");
        var boardSupportButton = Assert.IsType<Button>(window.FindName("BoardSupportButton"));
        Assert.Equal("open-board-support", AutomationProperties.GetAutomationId(boardSupportButton));
        Assert.Equal(window.CanOpenBoardSupport, boardSupportButton.IsEnabled);
        if (window.CanOpenBoardSupport)
        {
            var boardSource = Assert.IsType<DriverSupportSource>(DriverSupportCatalog.Find(window.BoardSupportSourceName));
            Assert.Contains(boardSource.Name, Assert.IsType<string>(boardSupportButton.Content), StringComparison.Ordinal);
            boardSupportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, boardSupportButton));
            Assert.Contains(boardSource.Uri.AbsoluteUri, openedUris);
        }
        else
        {
            Assert.Contains("não", window.BoardSupportSummary, StringComparison.OrdinalIgnoreCase);
        }
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
        window.ReduceAnimations = true;
        tabs.SelectedIndex = 1;
        Assert.False(Assert.IsAssignableFrom<FrameworkElement>(tabs.SelectedContent).HasAnimatedProperties,
            "The reduced-motion preference must disable ZEUS workspace transitions.");
        window.ReduceAnimations = false;
        if (!SystemParameters.HighContrast && SystemParameters.ClientAreaAnimation)
        {
            tabs.SelectedIndex = 0;
            Assert.True(Assert.IsAssignableFrom<FrameworkElement>(tabs.SelectedContent).HasAnimatedProperties,
                "Workspace navigation should use the brief transition when motion is allowed.");
        }
        tabs.SelectedIndex = 0;
        await RenderAsync(window, "zeus-complete-overview.png");
        var hardwareCardsList = Assert.IsType<ItemsControl>(window.FindName("HardwareCardsList"));
        var wideHardwareCardColumns = window.HardwareCardColumns;
        Assert.Equal(MainWindow.ResolveHardwareCardColumns(hardwareCardsList.ActualWidth), wideHardwareCardColumns);
        window.Width = 900;
        window.Height = 650;
        await Task.Delay(50);
        window.UpdateLayout();
        var compactHardwareCardColumns = window.HardwareCardColumns;
        Assert.Equal(MainWindow.ResolveHardwareCardColumns(hardwareCardsList.ActualWidth), compactHardwareCardColumns);
        Assert.True(compactHardwareCardColumns <= wideHardwareCardColumns,
            "A narrower available area must not increase the number of overview columns.");
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
        await Task.Delay(50);
        window.UpdateLayout();
        var restoredHardwareCardColumns = window.HardwareCardColumns;
        Assert.Equal(MainWindow.ResolveHardwareCardColumns(hardwareCardsList.ActualWidth), restoredHardwareCardColumns);
        Assert.True(restoredHardwareCardColumns >= compactHardwareCardColumns,
            "A wider available area must not reduce the number of overview columns.");
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
        Assert.Equal(Color.FromRgb(0xFC, 0xD3, 0x4D), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);
        window.SelectedAccentColor = AppAccentColor.ThemeDefault;
        Assert.Equal(Color.FromRgb(0x65, 0xE3, 0xE0), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);
        Assert.Equal(Color.FromRgb(0xFF, 0xD1, 0x8B), Assert.IsType<SolidColorBrush>(app.Resources["WarningBrush"]).Color);
        window.SelectedTheme = DesktopTheme.MacInspired;
        await RenderAsync(window, "zeus-mac-inspired-overview.png");
        window.SelectedTheme = DesktopTheme.Light;
        Assert.Equal(Color.FromRgb(0x17, 0x6B, 0x87), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);
        Assert.Equal(Color.FromRgb(0x80, 0x54, 0x00), Assert.IsType<SolidColorBrush>(app.Resources["WarningBrush"]).Color);
        window.SelectedAccentColor = AppAccentColor.Green;
        Assert.Equal(Color.FromRgb(0x22, 0x6B, 0x45), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);
        await RenderAsync(window, "zeus-light-overview.png");
        window.SelectedAccentColor = AppAccentColor.ThemeDefault;
        window.SelectedTheme = DesktopTheme.GamingNeon;
        Assert.Equal(Color.FromRgb(0xD6, 0xFF, 0x5F), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);
        window.SelectedTheme = DesktopTheme.Cyberpunk;
        Assert.Equal(Color.FromRgb(0xFF, 0x63, 0xD8), Assert.IsType<SolidColorBrush>(app.Resources["AccentBrush"]).Color);

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
        var clockPreferencesBeforePreview = await new DesktopStorage(fixture).ReadPreferencesAsync();
        window.DesktopClockShowDate = false;
        window.DesktopClockShowSeconds = true;
        window.DesktopClockUse24HourFormat = false;
        window.DesktopClockAlwaysOnTop = true;
        window.DesktopClockOpacity = 0.72;
        window.SelectedDesktopClockSize = DesktopClockSize.Large;
        window.DesktopClockEnabled = true;
        Assert.True(window.IsDesktopClockSettingsPreviewing);
        Assert.False((await new DesktopStorage(fixture).ReadPreferencesAsync()).Clock!.Enabled,
            "Changing clock controls must not save before explicit confirmation.");
        var cancelClockPreviewButton = Assert.IsType<Button>(window.FindName("CancelDesktopClockSettingsPreviewButton"));
        Assert.Equal("cancel-desktop-clock-preview", AutomationProperties.GetAutomationId(cancelClockPreviewButton));
        cancelClockPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelClockPreviewButton));
        Assert.False(window.IsDesktopClockSettingsPreviewing);
        Assert.False(window.DesktopClockEnabled);
        Assert.Equal(clockPreferencesBeforePreview.Clock, (await new DesktopStorage(fixture).ReadPreferencesAsync()).Clock);

        window.DesktopClockShowDate = false;
        window.DesktopClockShowSeconds = true;
        window.DesktopClockUse24HourFormat = false;
        window.DesktopClockAlwaysOnTop = true;
        window.DesktopClockOpacity = 0.72;
        window.SelectedDesktopClockSize = DesktopClockSize.Large;
        window.DesktopClockEnabled = true;
        Assert.True(window.IsDesktopClockSettingsPreviewing);
        var confirmClockPreviewButton = Assert.IsType<Button>(window.FindName("ConfirmDesktopClockSettingsButton"));
        Assert.Equal("confirm-desktop-clock-settings", AutomationProperties.GetAutomationId(confirmClockPreviewButton));
        confirmClockPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirmClockPreviewButton));
        Assert.False(window.IsDesktopClockSettingsPreviewing);
        var clockDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        DesktopPreferences clockPreferences;
        do
        {
            await Task.Delay(25);
            clockPreferences = await new DesktopStorage(fixture).ReadPreferencesAsync();
        } while (clockPreferences.Clock?.Enabled != true && DateTimeOffset.UtcNow < clockDeadline);
        Assert.Equal(new DesktopClockPreferences(true, false, true, true, 0.72, clockPreferences.Clock!.Left, clockPreferences.Clock.Top, DesktopClockSize.Large) { Use24HourFormat = false }, clockPreferences.Clock);
        var desktopClock = Assert.Single(Application.Current!.Windows.OfType<DesktopClockWindow>());
        var hideFullscreenToggle = Assert.IsType<CheckBox>(window.FindName("DesktopClockHideDuringFullscreenToggle"));
        Assert.Equal("clock-hide-fullscreen", AutomationProperties.GetAutomationId(hideFullscreenToggle));
        Assert.True(hideFullscreenToggle.IsChecked);
        Assert.Equal(42, Assert.IsType<TextBlock>(desktopClock.Content is Border clockBorder ? (clockBorder.Child as StackPanel)?.Children[0] : null).FontSize);
        var clockSizeSelector = Assert.IsType<ComboBox>(window.FindName("DesktopClockSizeSelector"));
        Assert.Equal("Tamanho do relógio", AutomationProperties.GetName(clockSizeSelector));
        Assert.Contains(window.DesktopClockSizeOptions, option => option.Label == "Grande" && option.Value == DesktopClockSize.Large);
        Assert.True(desktopClock.IsVisible);
        Assert.True(desktopClock.Topmost);
        Assert.Equal(0.72, desktopClock.Opacity);
        var clockCustomAccentInput = Assert.IsType<TextBox>(window.FindName("CustomAccentHexTextBox"));
        var clockPreviewCustomAccentButton = Assert.IsType<Button>(window.FindName("PreviewCustomAccentButton"));
        var clockCancelCustomAccentPreviewButton = Assert.IsType<Button>(window.FindName("CancelCustomAccentPreviewButton"));
        clockCustomAccentInput.Text = "#00FFFF";
        Assert.True(clockPreviewCustomAccentButton.IsEnabled, window.CustomAccentValidationSummary);
        clockPreviewCustomAccentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clockPreviewCustomAccentButton));
        var clockTimeText = FindVisualDescendants<TextBlock>(desktopClock).First();
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(clockTimeText.Foreground).Color);
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(Application.Current.Resources["AccentBrush"]).Color);
        clockCancelCustomAccentPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clockCancelCustomAccentPreviewButton));
        Assert.Equal(Color.FromRgb(0xFF, 0x63, 0xD8), Assert.IsType<SolidColorBrush>(clockTimeText.Foreground).Color);
        Assert.Equal(Color.FromRgb(0xFF, 0x63, 0xD8), Assert.IsType<SolidColorBrush>(Application.Current.Resources["AccentBrush"]).Color);
        var simulatedFullscreen = true;
        var transitionClock = new DesktopClockWindow(() => { }, () => simulatedFullscreen) { Left = 360, Top = 260 };
        transitionClock.Configure(false, false, true, true, 1, DesktopClockSize.Compact,
            SystemColors.HighlightBrush, highContrast: false, hideDuringFullscreen: true);
        transitionClock.Show();
        transitionClock.RefreshFullscreenVisibility();
        Assert.Equal(Visibility.Hidden, transitionClock.Visibility);
        simulatedFullscreen = false;
        transitionClock.RefreshFullscreenVisibility();
        Assert.Equal(Visibility.Visible, transitionClock.Visibility);
        transitionClock.Close();
        var clockLabels = FindVisualDescendants<TextBlock>(desktopClock).ToArray();
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\s+\S+$", clockLabels[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(1), desktopClock.NextUpdateInterval);
        window.DesktopClockShowSeconds = false;
        Assert.InRange(desktopClock.NextUpdateInterval, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(60));
        window.DesktopClockShowSeconds = true;
        Assert.Equal(TimeSpan.FromSeconds(1), desktopClock.NextUpdateInterval);
        window.DesktopClockUse24HourFormat = true;
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", clockLabels[0].Text);
        var clockFormatToggle = Assert.IsType<CheckBox>(window.FindName("DesktopClockUse24HourFormatToggle"));
        Assert.Equal("clock-use-24-hour", AutomationProperties.GetAutomationId(clockFormatToggle));
        Assert.Equal("Relógio: formato 24 horas", AutomationProperties.GetName(clockFormatToggle));
        desktopClock.Left = 100000;
        desktopClock.Top = 100000;
        var resetClockPositionButton = Assert.IsType<Button>(window.FindName("ResetDesktopClockPositionButton"));
        Assert.Equal("reset-desktop-clock-position", AutomationProperties.GetAutomationId(resetClockPositionButton));
        Assert.True(resetClockPositionButton.IsEnabled);
        var committedClockLeft = clockPreferences.Clock!.Left;
        var committedClockTop = clockPreferences.Clock.Top;
        resetClockPositionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, resetClockPositionButton));
        Assert.True(window.IsDesktopClockSettingsPreviewing);
        cancelClockPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelClockPreviewButton));
        Assert.Equal(committedClockLeft, desktopClock.Left);
        Assert.Equal(committedClockTop, desktopClock.Top);
        Assert.False(window.IsDesktopClockSettingsPreviewing);
        desktopClock.Left = 100000;
        desktopClock.Top = 100000;
        resetClockPositionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, resetClockPositionButton));
        Assert.True(window.IsDesktopClockSettingsPreviewing);
        confirmClockPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirmClockPreviewButton));
        Assert.False(window.IsDesktopClockSettingsPreviewing);
        var clockWorkArea = SystemParameters.WorkArea;
        Assert.InRange(desktopClock.Left, clockWorkArea.Left, clockWorkArea.Right - desktopClock.Width);
        Assert.InRange(desktopClock.Top, clockWorkArea.Top, clockWorkArea.Bottom - desktopClock.Height);
        var positionDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        do
        {
            await Task.Delay(25);
            clockPreferences = await new DesktopStorage(fixture).ReadPreferencesAsync();
        } while (clockPreferences.Clock?.Left != desktopClock.Left && DateTimeOffset.UtcNow < positionDeadline);
        Assert.Equal(desktopClock.Left, clockPreferences.Clock!.Left);
        Assert.Equal(desktopClock.Top, clockPreferences.Clock.Top);
        window.SelectedAccentColor = AppAccentColor.Blue;
        Assert.Equal(Color.FromRgb(0x60, 0xA5, 0xFA), Assert.IsType<SolidColorBrush>(clockLabels[0].Foreground).Color);
        window.SelectedAccentColor = AppAccentColor.ThemeDefault;
        app.RefreshSystemContrast(true);
        Assert.Same(SystemColors.WindowTextBrush, clockLabels[0].Foreground);
        Assert.Same(SystemColors.WindowTextBrush, clockLabels[1].Foreground);
        var clockSurface = Assert.IsType<Border>(desktopClock.Content);
        Assert.Same(SystemColors.WindowBrush, clockSurface.Background);
        Assert.Same(SystemColors.WindowFrameBrush, clockSurface.BorderBrush);
        Assert.Equal(1, desktopClock.Opacity);
        app.RefreshSystemContrast(false);
        Assert.Equal(Color.FromRgb(0xFF, 0x63, 0xD8), Assert.IsType<SolidColorBrush>(clockLabels[0].Foreground).Color);
        Assert.Equal(0.72, desktopClock.Opacity);
        window.DesktopClockEnabled = false;
        window.SelectedDesktopClockSize = DesktopClockSize.Medium;
        Assert.True(window.IsDesktopClockSettingsPreviewing);
        confirmClockPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, confirmClockPreviewButton));
        Assert.False(window.IsDesktopClockSettingsPreviewing);
        performanceLabel.Text = "Jogo teste + OBS";
        var measureButton = Assert.IsType<Button>(window.FindName("MeasurePerformanceButton"));
        Assert.Equal("measure-performance", AutomationProperties.GetAutomationId(measureButton));
        measureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, measureButton));
        var measurementDeadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (window.StatusTitle == "Medindo carga real" && DateTimeOffset.UtcNow < measurementDeadline)
            await Task.Delay(100);
        Assert.True(window.StatusTitle == "Medição concluída",
            $"A medição pela interface não terminou corretamente: {window.StatusTitle} · {window.StatusDetail}");
        Assert.Contains("não confirma", window.StatusDetail, StringComparison.OrdinalIgnoreCase);
        var savedPerformanceSession = Assert.Single(await new DesktopStorage(fixture).ReadPerformanceSessionsAsync(),
            session => session.Label == "Medição manual · Jogo teste + OBS");
        Assert.Single(savedPerformanceSession.Samples);
        Assert.Contains("Medição manual · Jogo teste + OBS", window.PerformanceSessionHistorySummary, StringComparison.Ordinal);
        var exportedSession = Assert.Single(window.CreateExportDocument().PerformanceSessions!,
            session => session.Label == savedPerformanceSession.Label);
        Assert.Equal(savedPerformanceSession.StartedAt, exportedSession.StartedAt);
        Assert.Equal(savedPerformanceSession.FinishedAt, exportedSession.FinishedAt);
        Assert.Equal(savedPerformanceSession.IsReference, exportedSession.IsReference);
        Assert.Equal(savedPerformanceSession.Samples.Count, exportedSession.SampleCount);
        Assert.All(window.MaintenanceChoices, choice => Assert.False(choice.IsSelected));
        Assert.False(window.CanExecute);
        // No button that repairs, installs a driver, deletes files, schedules a
        // scan, changes startup or reboots the machine is invoked by this test.
        window.DesktopClockEnabled = true;
        Assert.Single(Application.Current!.Windows.OfType<DesktopClockWindow>());
        window.Close();
        var closeDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (window.IsVisible && DateTimeOffset.UtcNow < closeDeadline) await Task.Delay(25);
        Assert.False(window.IsVisible, "The main window must finish closing after its activity writes drain.");
        Assert.Empty(Application.Current.Windows.OfType<DesktopClockWindow>());
        Assert.Equal("aurora", (await new DesktopStorage(fixture).ReadPreferencesAsync()).VisualLayoutPresetId);
    }

    private static void AssertThemeTextContrast(string foregroundKey, string backgroundKey, double minimum)
    {
        var foreground = Assert.IsType<SolidColorBrush>(Application.Current.Resources[foregroundKey]).Color;
        var background = Assert.IsType<SolidColorBrush>(Application.Current.Resources[backgroundKey]).Color;
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        var ratio = (lighter + 0.05) / (darker + 0.05);
        Assert.True(ratio >= minimum,
            $"Theme text contrast {foregroundKey} on {backgroundKey} was {ratio:F2}:1; expected at least {minimum:F1}:1.");
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
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
