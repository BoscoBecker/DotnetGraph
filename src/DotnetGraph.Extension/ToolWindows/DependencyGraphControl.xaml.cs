using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Win32;
using DotnetGraph.Core.Analysis;
using DotnetGraph.Core.Models;
using DotnetGraph.Core.Serialization;
using DotnetGraph.Extension.Services;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DotnetGraph.Extension.ToolWindows;

public partial class DependencyGraphControl : UserControl
{
    private readonly SolutionGraphService _graphService = new();
    private readonly ProjectCompositionAnalyzer _compositionAnalyzer = new();
    private readonly CallGraphAnalyzer _callGraphAnalyzer = new();
    private readonly NamespaceMapAnalyzer _namespaceMapAnalyzer = new();
    private NamespaceMapResult? _lastNamespaceMap;
    private string? _lastNamespaceProjectName;
    private CircularDependencyReport _circularDependencyReport = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SolutionGraph? _currentGraph;
    private string? _selectedProjectId;
    private string? _callGraphRootType;
    private int _detailRequestVersion;
    private IReadOnlyList<ReferencedProjectInfo> _currentProjectReferences = Array.Empty<ReferencedProjectInfo>();
    private string _currentDetailProjectName = string.Empty;
    private bool _webViewReady;
    private WebView2? _graphWebView;
    private GraphThemePreference _themePreference;
    private bool _vsThemeHooked;
    private string? _currentSolutionPath;
    private CallGraphResult? _lastCallGraph;

    public DependencyGraphControl()
    {
        UserGraphSettings.Load();
        InitializeComponent();
        _graphWebView = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        GraphWebViewHost.Children.Add(_graphWebView);
        GraphToolWindowHost.Register(this);
        Unloaded += OnControlUnloaded;

        _themePreference = UserGraphSettings.ThemePreference;
        ThemeComboBox.SelectedIndex = (int)_themePreference;
        LoadHeaderIcon();
        EnsureFluentToolbarStyles();
        ApplyLocalizedUi();
        ApplyChromeTheme();

        Loaded += OnLoaded;
    }

    private void EnsureFluentToolbarStyles()
    {
        RefreshButton.Style = (Style)FindResource("GraphFluentAccentButton");
    }

    private void ApplyLocalizedUi()
    {
        TitleText.Text = GraphLocalizer.T("Title");
        ShowArchitectureButton.Content = GraphLocalizer.T("Architecture");
        ShowArchitectureButton.ToolTip = GraphLocalizer.T("TooltipArchitecture");
        ShowCallGraphButton.Content = GraphLocalizer.T("CallGraph");
        ShowCallGraphButton.ToolTip = GraphLocalizer.T("TooltipCallGraph");
        ShowNamespaceMapButton.Content = GraphLocalizer.T("Namespaces");
        ShowNamespaceMapButton.ToolTip = GraphLocalizer.T("TooltipNamespaces");
        ThemeLabel.Text = GraphLocalizer.T("Theme");
        LanguageLabel.Text = GraphLocalizer.T("Language");
        LangBrButton.Content = GraphLocalizer.T("LangBr");
        LangEnButton.Content = GraphLocalizer.T("LangEn");
        LangEsButton.Content = GraphLocalizer.T("LangEs");
        RefreshButton.Content = GraphLocalizer.T("Refresh");
        CloseDetailButton.ToolTip = GraphLocalizer.T("ClosePanel");
        DetailLoadingText.Text = GraphLocalizer.T("LoadingDetails");
        SectionProjectRefs.Text = GraphLocalizer.T("SectionProjectRefs");
        SectionPackages.Text = GraphLocalizer.T("SectionPackages");
        SectionTypes.Text = GraphLocalizer.T("SectionTypes");
        SectionCallMethods.Text = GraphLocalizer.T("SectionCallMethods");
        SectionSourceFiles.Text = GraphLocalizer.T("SectionSourceFiles");
        CopyProjectRefsButton.ToolTip = GraphLocalizer.T("CopyRefs");
        ExportProjectRefsButton.ToolTip = GraphLocalizer.T("ExportRefs");
        CircularDependencyTitle.Text = GraphLocalizer.T("CircularDetected");

        if (ThemeComboBox.Items.Count >= 3)
        {
            ((ComboBoxItem)ThemeComboBox.Items[0]).Content = GraphLocalizer.T("ThemeSystem");
            ((ComboBoxItem)ThemeComboBox.Items[1]).Content = GraphLocalizer.T("ThemeLight");
            ((ComboBoxItem)ThemeComboBox.Items[2]).Content = GraphLocalizer.T("ThemeDark");
        }

        UpdateLanguageButtonStyles();
    }

    private void UpdateLanguageButtonStyles()
    {
        var active = (Style)FindResource("GraphLangToggleButtonActive");
        var normal = (Style)FindResource("GraphLangToggleButton");
        LangBrButton.Style = UserGraphSettings.UiLanguage == GraphUiLanguage.BrazilianPortuguese ? active : normal;
        LangEnButton.Style = UserGraphSettings.UiLanguage == GraphUiLanguage.English ? active : normal;
        LangEsButton.Style = UserGraphSettings.UiLanguage == GraphUiLanguage.Spanish ? active : normal;
    }

    private void SetUiLanguage(GraphUiLanguage language)
    {
        if (UserGraphSettings.UiLanguage == language)
        {
            return;
        }

        UserGraphSettings.SaveUiLanguage(language);
        ApplyLocalizedUi();
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await PushLocaleToWebViewAsync();
        });
    }

    private void LangBrButton_OnClick(object sender, RoutedEventArgs e) => SetUiLanguage(GraphUiLanguage.BrazilianPortuguese);

    private void LangEnButton_OnClick(object sender, RoutedEventArgs e) => SetUiLanguage(GraphUiLanguage.English);

    private void LangEsButton_OnClick(object sender, RoutedEventArgs e) => SetUiLanguage(GraphUiLanguage.Spanish);

    private static string ArchitectureLayoutKey(string? solutionPath) =>
        "architecture:" + (string.IsNullOrWhiteSpace(solutionPath) ? "default" : solutionPath);

    private static string CallGraphLayoutKey(string projectId) => "callGraph:" + projectId;

    private static string NamespaceMapLayoutKey(string projectId) => "namespaceMap:" + projectId;

    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        GraphToolWindowHost.Unregister(this);
        if (_vsThemeHooked)
        {
            VSColorTheme.ThemeChanged -= OnVsThemeChanged;
            _vsThemeHooked = false;
        }
    }

    private void OnVsThemeChanged(ThemeChangedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ApplyChromeTheme();
            await PushThemeToWebViewAsync();
        });
    }

    private void LoadHeaderIcon()
    {
        try
        {
            var packUri = new Uri(
                "pack://application:,,,/DotnetGraph.Extension;component/Resources/Icons/PackageIcon.png",
                UriKind.Absolute);
            HeaderIcon.Source = new BitmapImage(packUri);
            return;
        }
        catch (IOException)
        {
        }
        catch (UriFormatException)
        {
        }

        var iconPath = Path.Combine(GetExtensionDirectory(), "Resources", "Icons", "PackageIcon.png");
        if (!File.Exists(iconPath))
        {
            return;
        }

        try
        {
            HeaderIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        }
        catch (IOException)
        {
        }
    }

    private void SetDetailSidebarVisible(bool visible)
    {
        DetailPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DetailSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DetailSplitterColumn.Width = visible ? new GridLength(4) : new GridLength(0);

        if (visible)
        {
            Grid.SetColumnSpan(GraphHostBorder, 1);
            DetailColumn.MinWidth = 240;
            DetailColumn.MaxWidth = 720;
            DetailColumn.Width = new GridLength(UserGraphSettings.DetailPanelWidth);
        }
        else
        {
            Grid.SetColumnSpan(GraphHostBorder, 3);
            DetailColumn.MinWidth = 0;
            DetailColumn.MaxWidth = double.PositiveInfinity;
            DetailColumn.Width = new GridLength(0);
            PushClearSelectionToWebView();
        }
    }

    private void CloseDetailSidebar()
    {
        SetDetailSidebarVisible(false);
    }

    private void DetailSplitter_OnDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DetailColumn.ActualWidth >= 240)
        {
            UserGraphSettings.SaveDetailPanelWidth(DetailColumn.ActualWidth);
        }
    }

    private void CopyProjectRefsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentProjectReferences.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(FormatProjectReferencesForExport());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ShowStatusSafeAsync("Não foi possível copiar para a área de transferência.");
            });
        }
    }

    private void ExportProjectRefsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentProjectReferences.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar referências de projeto",
            Filter = "Texto (*.txt)|*.txt|Todos (*.*)|*.*",
            FileName = $"{_currentDetailProjectName}-project-references.txt",
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, FormatProjectReferencesForExport(), Encoding.UTF8);
        }
        catch (IOException ex)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ShowStatusSafeAsync("Exportação falhou: " + ex.Message);
            });
        }
    }

    private string FormatProjectReferencesForExport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Projeto: " + _currentDetailProjectName);
        builder.AppendLine("Referências de projeto:");
        foreach (var reference in _currentProjectReferences)
        {
            if (string.IsNullOrWhiteSpace(reference.ProjectPath))
            {
                builder.AppendLine("- " + reference.Name);
            }
            else
            {
                builder.AppendLine("- " + reference.Name + " (" + reference.ProjectPath + ")");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void TypeLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DetailLinkItem item || !item.IsNavigable)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await OpenSourceFileAsync(item.FilePath!, item.Line);
        });
    }

    private void CloseDetailButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseDetailSidebar();
    }

    private void ShowArchitectureButton_OnClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ShowArchitectureGraphAsync();
        });
    }

    private void ShowCallGraphButton_OnClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ShowCallGraphAsync();
        });
    }

    private void ShowNamespaceMapButton_OnClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ShowNamespaceMapAsync();
        });
    }

    private async Task ShowArchitectureGraphAsync()
    {
        if (_currentGraph is null || _graphWebView?.CoreWebView2 is null)
        {
            await ShowStatusSafeAsync("Atualize o grafo da solução primeiro.");
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var payload = GraphWebPayload.WithLayout(
            GraphJsonSerializer.SerializeSolutionGraph(_currentGraph, _circularDependencyReport),
            ArchitectureLayoutKey(_currentSolutionPath));
        _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
        SolutionLabel.Text = _circularDependencyReport.HasCycles
            ? string.Format(GraphLocalizer.T("ViewArchitectureCycles"), _circularDependencyReport.Cycles.Count)
            : GraphLocalizer.T("ViewArchitecture");
    }

    private async Task ShowNamespaceMapAsync()
    {
        if (_currentGraph is null || string.IsNullOrWhiteSpace(_selectedProjectId))
        {
            await ShowStatusSafeAsync("Selecione um projeto no grafo.");
            return;
        }

        var project = _currentGraph.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, _selectedProjectId, StringComparison.Ordinal));
        if (project is null)
        {
            await ShowStatusSafeAsync("Projeto selecionado não encontrado.");
            return;
        }

        if (project.Language != ProjectLanguage.CSharp)
        {
            await ShowStatusSafeAsync("Namespace map disponível apenas para projetos C#.");
            return;
        }

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingNamespace"));
            var result = await Task.Run(() => _namespaceMapAnalyzer.Analyze(project));
            _lastNamespaceMap = result;
            _lastNamespaceProjectName = project.Name;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_graphWebView?.CoreWebView2 is null)
            {
                return;
            }

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeNamespaceMap(result),
                NamespaceMapLayoutKey(project.Id));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewNamespaces"), project.Name);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync("Namespace map: " + ex.Message);
        }
        finally
        {
            SetLoading(false, null);
        }
    }

    private async Task ShowCallGraphAsync()
    {
        if (_currentGraph is null || string.IsNullOrWhiteSpace(_selectedProjectId))
        {
            await ShowStatusSafeAsync("Selecione um projeto no grafo.");
            return;
        }

        var project = _currentGraph.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, _selectedProjectId, StringComparison.Ordinal));
        if (project is null)
        {
            await ShowStatusSafeAsync("Projeto selecionado não encontrado.");
            return;
        }

        if (project.Language != ProjectLanguage.CSharp)
        {
            await ShowStatusSafeAsync("Call graph disponível apenas para projetos C#.");
            return;
        }

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingCallGraph"));
            var result = await Task.Run(() =>
                _callGraphAnalyzer.Analyze(project, _callGraphRootType, null));
            _lastCallGraph = result;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_graphWebView?.CoreWebView2 is null)
            {
                return;
            }

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeCallGraph(result),
                CallGraphLayoutKey(project.Id));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewCallGraph"), project.Name);
            ShowCallGraphInSidebar(result, project.Name);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync("Call graph: " + ex.Message);
        }
        finally
        {
            SetLoading(false, null);
        }
    }

    public void RequestRefresh()
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await RefreshGraphAsync();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_vsThemeHooked)
        {
            VSColorTheme.ThemeChanged += OnVsThemeChanged;
            _vsThemeHooked = true;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await LoadInitialAsync();
        });
    }

    private async Task LoadInitialAsync()
    {
        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingInit"));
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync("Falha ao iniciar: " + FormatUserError(ex));
        }
        finally
        {
            SetLoading(false, null);
        }

        await RefreshGraphAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var graphFolder = Path.Combine(GetExtensionDirectory(), "Resources", "Graph");
        var indexPath = Path.Combine(graphFolder, "index.html");
        if (!File.Exists(indexPath))
        {
            SolutionLabel.Text = "Recursos do grafo não encontrados.";
            return;
        }

        if (_graphWebView is null)
        {
            return;
        }

        if (_webViewReady && _graphWebView.CoreWebView2 is not null)
        {
            return;
        }

        var environment = await GraphWebViewEnvironment.GetOrCreateAsync();
        await _graphWebView.EnsureCoreWebView2Async(environment);
        _graphWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _graphWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        _graphWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _graphWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _graphWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "dotnetgraph.local",
            graphFolder,
            CoreWebView2HostResourceAccessKind.Allow);
        _graphWebView.Source = new Uri("https://dotnetgraph.local/index.html");
        _webViewReady = true;
        await PushThemeToWebViewAsync();
        await PushLocaleToWebViewAsync();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await RefreshGraphAsync();
        });
    }

    private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var tag = item.Tag?.ToString();
        _themePreference = tag switch
        {
            "Light" => GraphThemePreference.Light,
            "Dark" => GraphThemePreference.Dark,
            _ => GraphThemePreference.System
        };

        UserGraphSettings.SaveTheme(_themePreference);
        ApplyChromeTheme();
        ThreadHelper.JoinableTaskFactory.RunAsync(PushThemeToWebViewAsync);
    }

    private async Task RefreshGraphAsync()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            await ShowStatusSafeAsync("Análise em andamento. Aguarde...");
            return;
        }

        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetLoading(true, GraphLocalizer.T("LoadingAnalyze"));
            RefreshButton.IsEnabled = false;

            if (!_webViewReady)
            {
                try
                {
                    await InitializeWebViewAsync();
                }
                catch (Exception webEx)
                {
                    await ShowStatusSafeAsync("WebView indisponível: " + FormatUserError(webEx));
                    return;
                }
            }

            var dte = await GetDteAsync();
            if (dte?.Solution is null || !dte.Solution.IsOpen)
            {
                CloseDetailSidebar();
                await ShowStatusSafeAsync("Nenhuma solução aberta.");
                return;
            }

            var solutionPath = dte.Solution.FullName;
            _currentSolutionPath = solutionPath;
            var solutionName = Path.GetFileName(solutionPath);
            var projects = SolutionProjectCollector.CollectFromDte(dte);

            await Task.Run(() =>
            {
                _currentGraph = _graphService.BuildFromProjects(solutionPath, projects);
                _circularDependencyReport = _currentGraph is not null
                    ? CircularDependencyDetector.Analyze(_currentGraph)
                    : new CircularDependencyReport();
            });

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await ShowStatusSafeAsync(solutionName);

            if (_currentGraph is null)
            {
                await ShowStatusSafeAsync("Não foi possível montar o grafo.");
                return;
            }

            if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
            {
                await ShowStatusSafeAsync(
                    $"{solutionName} — grafo calculado, mas a visualização não está pronta. Clique em Atualizar.");
                return;
            }

            _selectedProjectId = null;
            ShowCallGraphButton.IsEnabled = false;
            ShowNamespaceMapButton.IsEnabled = false;

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeSolutionGraph(_currentGraph, _circularDependencyReport),
                ArchitectureLayoutKey(_currentSolutionPath));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            await PushThemeToWebViewAsync();
            await PushLocaleToWebViewAsync();

            if (_circularDependencyReport.HasCycles)
            {
                ShowCircularDependenciesInSidebar(_circularDependencyReport);
                SolutionLabel.Text = string.Format(
                    GraphLocalizer.T("ViewArchitectureCycles"),
                    _circularDependencyReport.Cycles.Count);
            }
            else
            {
                CloseDetailSidebar();
                SolutionLabel.Text = GraphLocalizer.T("ViewArchitecture");
            }
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync("Erro ao gerar grafo: " + FormatUserError(ex));
        }
        finally
        {
            _refreshGate.Release();

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetLoading(false, null);
                RefreshButton.IsEnabled = true;
            }
            catch
            {
                // Semáforo já liberado — evita travar Atualizar após HRESULT na UI thread.
            }
        }
    }

    private async Task PushThemeToWebViewAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        var theme = GraphThemeService.ResolveWebTheme(_themePreference);
        var message = JsonSerializer.Serialize(new { type = "setTheme", theme });
        _graphWebView.CoreWebView2.PostWebMessageAsString(message);
    }

    private async Task PushLocaleToWebViewAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        var message = JsonSerializer.Serialize(new
        {
            type = "setLocale",
            strings = GraphLocalizer.WebStrings()
        });
        _graphWebView.CoreWebView2.PostWebMessageAsString(message);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = GetWebMessageString(e);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await HandleWebMessageAsync(message!);
        });
    }

    private async Task HandleWebMessageAsync(string messageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var messageType = typeElement.GetString();
            if (string.Equals(messageType, "backgroundClick", StringComparison.Ordinal))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                CloseDetailSidebar();
                return;
            }

            if (string.Equals(messageType, "namespaceClusterClick", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("clusterId", out var clusterIdElement))
                {
                    return;
                }

                var clusterId = clusterIdElement.GetString();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowNamespaceClusterInSidebar(clusterId);
                return;
            }

            if (string.Equals(messageType, "callNodeClick", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("nodeId", out var nodeIdElement))
                {
                    return;
                }

                var nodeId = nodeIdElement.GetString();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                FocusCallGraphMethodInSidebar(nodeId);
                return;
            }

            if (string.Equals(messageType, "saveLayout", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("layoutKey", out var layoutKeyElement))
                {
                    return;
                }

                var layoutKey = layoutKeyElement.GetString();
                if (string.IsNullOrWhiteSpace(layoutKey)
                    || !root.TryGetProperty("layoutPositions", out var positionsElement)
                    || positionsElement.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                var positions = ParseLayoutPositions(positionsElement);
                if (positions.Count > 0)
                {
                    UserGraphSettings.SaveLayout(layoutKey, positions);
                }

                return;
            }

            if (!string.Equals(messageType, "nodeClick", StringComparison.Ordinal))
            {
                return;
            }

            if (_currentGraph is null)
            {
                return;
            }

            if (!root.TryGetProperty("projectId", out var idElement))
            {
                return;
            }

            var projectId = idElement.GetString();
            var project = _currentGraph.Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.Ordinal));
            if (project is null)
            {
                return;
            }

            var requestVersion = Interlocked.Increment(ref _detailRequestVersion);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _selectedProjectId = projectId;
            var isCSharp = project.Language == ProjectLanguage.CSharp;
            ShowCallGraphButton.IsEnabled = isCSharp;
            ShowNamespaceMapButton.IsEnabled = isCSharp;
            SetDetailSidebarVisible(true);
            DetailContentPanel.Visibility = Visibility.Collapsed;
            DetailLoadingPanel.Visibility = Visibility.Visible;
            PushLanguageHighlightToWebView(project.Language);

            var detail = await Task.Run(() => _compositionAnalyzer.Analyze(project));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (requestVersion != _detailRequestVersion)
            {
                return;
            }

            DetailLoadingPanel.Visibility = Visibility.Collapsed;
            DetailContentPanel.Visibility = Visibility.Visible;
            ShowDetail(detail);
        }
        catch (Exception ex)
        {
            await ShowStatusAsync("Erro ao carregar detalhes: " + ex.Message);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DetailLoadingPanel.Visibility = Visibility.Collapsed;
            DetailContentPanel.Visibility = Visibility.Visible;
        }
    }

    private void ShowNamespaceClusterInSidebar(string? clusterId)
    {
        if (_lastNamespaceMap is null || string.IsNullOrWhiteSpace(clusterId))
        {
            return;
        }

        var cluster = _lastNamespaceMap.Clusters.FirstOrDefault(c =>
            string.Equals(c.Id, clusterId, StringComparison.Ordinal));
        if (cluster is null)
        {
            return;
        }

        SetDetailSidebarVisible(true);
        SetDetailSectionsMode(DetailSidebarContent.NamespaceFiles);
        DetailLanguageBadge.Visibility = Visibility.Visible;
        DetailLoadingPanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;

        DetailTitle.Text = cluster.Label;
        DetailMeta.Text = $"{_lastNamespaceProjectName ?? _lastNamespaceMap.ProjectName} · {cluster.TypeCount} tipos";
        ApplyLanguageBadge(ProjectLanguage.CSharp);

        CopyProjectRefsButton.IsEnabled = false;
        ExportProjectRefsButton.IsEnabled = false;

        SourceFilesList.ItemsSource = cluster.SourceFiles.Count > 0
            ? cluster.SourceFiles.Cast<object>().ToList()
            : new[] { "(nenhum arquivo .cs neste cluster)" };
    }

    private enum DetailSidebarContent
    {
        Project,
        NamespaceFiles,
        CircularDependencies,
        CallGraphMethods
    }

    private void SetDetailSectionsMode(DetailSidebarContent content)
    {
        var projectMode = content == DetailSidebarContent.Project ? Visibility.Visible : Visibility.Collapsed;
        var namespaceMode = content == DetailSidebarContent.NamespaceFiles ? Visibility.Visible : Visibility.Collapsed;
        var cycleMode = content == DetailSidebarContent.CircularDependencies ? Visibility.Visible : Visibility.Collapsed;
        var callGraphMode = content == DetailSidebarContent.CallGraphMethods ? Visibility.Visible : Visibility.Collapsed;

        CircularDependencyPanel.Visibility = cycleMode;
        SectionProjectRefs.Visibility = projectMode;
        ProjectReferencesList.Visibility = projectMode;
        CopyProjectRefsButton.Visibility = projectMode;
        ExportProjectRefsButton.Visibility = projectMode;
        SectionPackages.Visibility = projectMode;
        PackageReferencesList.Visibility = projectMode;
        SectionTypes.Visibility = projectMode;
        SectionCallMethods.Visibility = callGraphMode;
        TypesList.Visibility = content == DetailSidebarContent.Project
            || content == DetailSidebarContent.CallGraphMethods
            ? Visibility.Visible
            : Visibility.Collapsed;
        SectionSourceFiles.Visibility = namespaceMode;
        SourceFilesList.Visibility = namespaceMode;
    }

    private void ShowCallGraphInSidebar(CallGraphResult result, string projectName)
    {
        SetDetailSidebarVisible(true);
        SetDetailSectionsMode(DetailSidebarContent.CallGraphMethods);
        DetailLanguageBadge.Visibility = Visibility.Visible;
        DetailLoadingPanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;

        DetailTitle.Text = projectName;
        DetailMeta.Text = result.EntryMethod ?? GraphLocalizer.T("CallGraph");
        ApplyLanguageBadge(ProjectLanguage.CSharp);

        CopyProjectRefsButton.IsEnabled = false;
        ExportProjectRefsButton.IsEnabled = false;

        TypesList.ItemsSource = BuildCallGraphMethodLinks(result);
    }

    private void FocusCallGraphMethodInSidebar(string? nodeId)
    {
        if (_lastCallGraph is null || string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        var node = _lastCallGraph.Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            return;
        }

        SetDetailSidebarVisible(true);
        SetDetailSectionsMode(DetailSidebarContent.CallGraphMethods);
        DetailTitle.Text = node.Label;
        DetailMeta.Text = node.Subtitle;
        TypesList.ItemsSource = BuildCallGraphMethodLinks(_lastCallGraph);
    }

    private static List<DetailLinkItem> BuildCallGraphMethodLinks(CallGraphResult result)
    {
        return result.Nodes
            .Where(n => !string.Equals(n.Id, "info", StringComparison.Ordinal))
            .Select(n => new DetailLinkItem
            {
                DisplayText = string.IsNullOrWhiteSpace(n.Subtitle)
                    ? n.Label
                    : $"{n.Label} — {n.Subtitle}",
                FilePath = n.SourceFilePath,
                Line = n.SourceLine > 0 ? n.SourceLine : 1
            })
            .ToList();
    }

    private static Dictionary<string, UserGraphSettings.LayoutPoint> ParseLayoutPositions(JsonElement positionsElement)
    {
        var positions = new Dictionary<string, UserGraphSettings.LayoutPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in positionsElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!property.Value.TryGetProperty("x", out var xElement)
                || !property.Value.TryGetProperty("y", out var yElement))
            {
                continue;
            }

            positions[property.Name] = new UserGraphSettings.LayoutPoint
            {
                X = xElement.GetDouble(),
                Y = yElement.GetDouble()
            };
        }

        return positions;
    }

    private void ShowCircularDependenciesInSidebar(CircularDependencyReport report)
    {
        SetDetailSidebarVisible(true);
        SetDetailSectionsMode(DetailSidebarContent.CircularDependencies);
        DetailLoadingPanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;

        DetailTitle.Text = "Dependências circulares";
        DetailMeta.Text = $"{report.Cycles.Count} ciclo(s) entre projetos da solução";
        DetailLanguageBadge.Visibility = Visibility.Collapsed;

        CircularDependencyTitle.Text = report.Cycles.Count == 1
            ? "⚠ Circular dependency detected"
            : $"⚠ {report.Cycles.Count} circular dependencies detected";

        CircularDependencyCyclesList.ItemsSource = report.Cycles
            .Select((cycle, index) => new DetailCycleItem
            {
                Index = index,
                ProjectNames = cycle.ProjectNames
            })
            .ToList();
    }

    private void HighlightCycleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int cycleIndex)
        {
            return;
        }

        PushHighlightCycleToWebView(cycleIndex);
    }

    private void PushHighlightCycleToWebView(int cycleIndex)
    {
        if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        var message = JsonSerializer.Serialize(new { type = "highlightCycle", cycleIndex });
        _graphWebView.CoreWebView2.PostWebMessageAsString(message);
    }

    private void ShowDetail(ProjectDetail detail)
    {
        SetDetailSidebarVisible(true);
        SetDetailSectionsMode(DetailSidebarContent.Project);
        DetailLanguageBadge.Visibility = Visibility.Visible;
        _callGraphRootType = detail.Types
            .Select(t => t.FullName.Contains('.')
                ? t.FullName.Substring(t.FullName.LastIndexOf('.') + 1)
                : t.FullName)
            .FirstOrDefault(n => n.EndsWith("Controller", StringComparison.Ordinal))
            ?? detail.Types.FirstOrDefault()?.FullName;
        DetailTitle.Text = detail.Project.Name;
        _currentDetailProjectName = detail.Project.Name;
        _currentProjectReferences = detail.ProjectReferences;
        DetailMeta.Text = $"{detail.Project.TargetFramework} · {detail.Project.OutputType}";
        ApplyLanguageBadge(detail.Project.Language);

        var canExportRefs = detail.ProjectReferences.Count > 0;
        CopyProjectRefsButton.IsEnabled = canExportRefs;
        ExportProjectRefsButton.IsEnabled = canExportRefs;

        ProjectReferencesList.ItemsSource = detail.ProjectReferences.Count > 0
            ? detail.ProjectReferences.Select(r => r.Name).Cast<object>().ToList()
            : new[] { "(nenhuma referência de projeto)" };

        PackageReferencesList.ItemsSource = detail.PackageReferences.Count > 0
            ? detail.PackageReferences.Select(p => $"{p.Name} ({p.Version})").Cast<object>().ToList()
            : new[] { "(nenhum pacote NuGet)" };

        TypesList.ItemsSource = detail.Types.Count > 0
            ? detail.Types.Select(CreateTypeLinkItem).ToList()
            : new[] { new DetailLinkItem { DisplayText = "(nenhum tipo encontrado no código-fonte)" } };
    }

    private static DetailLinkItem CreateTypeLinkItem(TypeMemberInfo type)
    {
        var members = string.Join(", ", type.Members);
        var display = string.IsNullOrWhiteSpace(members)
            ? $"{type.FullName} [{type.Kind}]"
            : $"{type.FullName} [{type.Kind}] — {members}";

        return new DetailLinkItem
        {
            DisplayText = display,
            FilePath = type.SourceFilePath,
            Line = type.SourceLine > 0 ? type.SourceLine : 1
        };
    }

    private void ApplyLanguageBadge(ProjectLanguage language)
    {
        var (label, bg, fg) = language switch
        {
            ProjectLanguage.CSharp => ("C#", Color.FromRgb(0x51, 0x2B, 0xD4), Colors.White),
            ProjectLanguage.FSharp => ("F#", Color.FromRgb(0x37, 0x8B, 0xBA), Colors.White),
            ProjectLanguage.VisualBasic => ("VB", Color.FromRgb(0x00, 0x5A, 0x9E), Colors.White),
            _ => ("?", Color.FromRgb(0x6B, 0x72, 0x80), Colors.White)
        };

        DetailLanguageLabel.Text = label;
        DetailLanguageBadge.Background = new SolidColorBrush(bg);
        DetailLanguageLabel.Foreground = new SolidColorBrush(fg);
    }

    private void PushLanguageHighlightToWebView(ProjectLanguage language)
    {
        if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        var message = JsonSerializer.Serialize(new { type = "highlightLanguage", language = (int)language });
        _graphWebView.CoreWebView2.PostWebMessageAsString(message);
    }

    private void PushClearSelectionToWebView()
    {
        if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        _graphWebView.CoreWebView2.PostWebMessageAsString("{\"type\":\"clearSelection\"}");
    }

    private void ApplyChromeTheme()
    {
        GraphThemeService.ApplyWpfChrome(
            _themePreference,
            this,
            HeaderBorder,
            GraphHostBorder,
            DetailPanel,
            DetailHeaderBorder,
            TitleText,
            SolutionLabel,
            DetailTitle,
            DetailMeta,
            ThemeLabel,
            LanguageLabel,
            DetailLoadingText,
            SectionProjectRefs,
            SectionPackages,
            SectionTypes,
            ProjectReferencesList,
            PackageReferencesList,
            TypesList,
            RefreshButton,
            CloseDetailButton,
            DetailSplitter,
            LoadingOverlay,
            LoadingText,
            ShowArchitectureButton,
            ShowCallGraphButton,
            ShowNamespaceMapButton,
            CopyProjectRefsButton,
            ExportProjectRefsButton,
            HeaderActionsPanel,
            ThemeComboBox,
            DetailLoadingProgress,
            OverlayProgress);

        var cycleWarning = _themePreference == GraphThemePreference.Dark
            || (_themePreference == GraphThemePreference.System && GraphThemeService.IsVisualStudioDark())
            ? Color.FromRgb(0xFF, 0x6B, 0x6B)
            : Color.FromRgb(0xC4, 0x2B, 0x1C);
        CircularDependencyTitle.Foreground = new SolidColorBrush(cycleWarning);
    }

    private static async Task OpenSourceFileAsync(string filePath, int line)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
        if (dte is null || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var window = dte.ItemOperations.OpenFile(filePath, Constants.vsViewKindTextView);
            if (window?.Document is null)
            {
                return;
            }

            if (window.Document.Selection is TextSelection selection)
            {
                selection.GotoLine(Math.Max(1, line), false);
            }
        }
        catch (COMException)
        {
        }
    }

    private void SetLoading(bool visible, string? message)
    {
        LoadingOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(message))
        {
            LoadingText.Text = message;
        }
    }

    private async Task ShowStatusAsync(string message) => await ShowStatusSafeAsync(message);

    private async Task ShowStatusSafeAsync(string message)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SolutionLabel.Text = message;
        }
        catch
        {
            // Ignora falhas COM/UI ao fechar o VS ou após erro fatal na thread principal.
        }
    }

    private static string FormatUserError(Exception ex)
    {
        var builder = new StringBuilder();
        builder.Append(ex.Message);

        if (ex is COMException com && com.HResult != 0)
        {
            builder.Append(" (HRESULT 0x");
            builder.Append(com.HResult.ToString("X8"));
            builder.Append(')');
        }

        if (ex.InnerException is not null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
        {
            builder.Append(" — ");
            builder.Append(ex.InnerException.Message);
        }

        return builder.ToString();
    }

    private static async Task<DTE?> GetDteAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
    }

    private static string? GetWebMessageString(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var asString = e.TryGetWebMessageAsString();
            if (!string.IsNullOrWhiteSpace(asString))
            {
                return asString;
            }
        }
        catch (ArgumentException)
        {
        }

        var raw = e.WebMessageAsJson;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Length >= 2 && raw[0] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(raw);
            }
            catch (JsonException)
            {
                return raw.Trim('"');
            }
        }

        return raw;
    }

    private static string GetExtensionDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        return Path.GetDirectoryName(assemblyLocation)
            ?? AppDomain.CurrentDomain.BaseDirectory;
    }
}
