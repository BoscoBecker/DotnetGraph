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
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DotnetGraph.Extension.ToolWindows;

public partial class DependencyGraphControl : UserControl
{
    private readonly Lazy<SolutionGraphService> _graphService = new(() => new SolutionGraphService());
    private readonly Lazy<ProjectCompositionAnalyzer> _compositionAnalyzer = new(() => new ProjectCompositionAnalyzer());
    private readonly Lazy<CallGraphAnalyzer> _callGraphAnalyzer = new(() => new CallGraphAnalyzer());
    private readonly Lazy<NamespaceMapAnalyzer> _namespaceMapAnalyzer = new(() => new NamespaceMapAnalyzer());
    private readonly Lazy<TypeGraphAnalyzer> _typeGraphAnalyzer = new(() => new TypeGraphAnalyzer());
    private readonly Lazy<SymbolImpactAnalyzer> _symbolImpactAnalyzer = new(() => new SymbolImpactAnalyzer());
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
    private TypeGraphResult? _lastTypeGraph;
    private ImpactAnalysisResult? _lastImpactAnalysis;
    private string? _selectedTypeFullName;
    private string? _architectureScopeFilter;
    private bool _suppressScopeFilterChange;
    private string? _pendingGraphExportFormat;
    private ActiveGraphView _activeGraphView = ActiveGraphView.Architecture;

    private enum ActiveGraphView
    {
        Architecture,
        CallGraph,
        NamespaceMap,
        TypeGraph,
        ImpactGraph
    }

    public DependencyGraphControl()
    {
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

        _themePreference = GraphThemePreference.System;
        ThemeComboBox.SelectedIndex = 0;
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
        ShowTypeGraphButton.Content = GraphLocalizer.T("TypeGraph");
        ShowTypeGraphButton.ToolTip = GraphLocalizer.T("TooltipTypeGraph");
        ScopeLabel.Text = GraphLocalizer.T("Scope");
        SolutionScopeComboBox.ToolTip = GraphLocalizer.T("TooltipScope");
        WhoUsesButton.Content = GraphLocalizer.T("WhoUses");
        WhoUsesButton.ToolTip = GraphLocalizer.T("TooltipWhoUses");
        SectionImpactUsages.Text = GraphLocalizer.T("SectionImpactUsages");
        ThemeLabel.Text = GraphLocalizer.T("Theme");
        LanguageLabel.Text = GraphLocalizer.T("Language");
        LangBrButton.Content = GraphLocalizer.T("LangBr");
        LangEnButton.Content = GraphLocalizer.T("LangEn");
        LangEsButton.Content = GraphLocalizer.T("LangEs");
        RefreshButton.Content = GraphLocalizer.T("Refresh");
        ExportPngButton.Content = GraphLocalizer.T("ExportPng");
        ExportPngButton.ToolTip = GraphLocalizer.T("TooltipExportPng");
        ExportMermaidButton.Content = GraphLocalizer.T("ExportMermaid");
        ExportMermaidButton.ToolTip = GraphLocalizer.T("TooltipExportMermaid");
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
        CircularPathLabel.Text = GraphLocalizer.T("Path");

        if (ThemeComboBox.Items.Count >= 3)
        {
            ((ComboBoxItem)ThemeComboBox.Items[0]).Content = GraphLocalizer.T("ThemeSystem");
            ((ComboBoxItem)ThemeComboBox.Items[1]).Content = GraphLocalizer.T("ThemeLight");
            ((ComboBoxItem)ThemeComboBox.Items[2]).Content = GraphLocalizer.T("ThemeDark");
        }

        UpdateLanguageButtonStyles();
        UpdateViewButtonStyles();
    }

    private void SetActiveGraphView(ActiveGraphView view)
    {
        _activeGraphView = view;
        UpdateViewButtonStyles();
        UpdateSolutionScopeAvailability();
    }

    private void UpdateViewButtonStyles()
    {
        var active = (Style)FindResource("GraphViewToggleButtonActive");
        var normal = (Style)FindResource("GraphViewToggleButton");
        ShowArchitectureButton.Style = _activeGraphView is ActiveGraphView.Architecture or ActiveGraphView.ImpactGraph
            ? active
            : normal;
        ShowCallGraphButton.Style = _activeGraphView == ActiveGraphView.CallGraph ? active : normal;
        ShowNamespaceMapButton.Style = _activeGraphView == ActiveGraphView.NamespaceMap ? active : normal;
        ShowTypeGraphButton.Style = _activeGraphView == ActiveGraphView.TypeGraph ? active : normal;
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
            await RefreshLocalizedGraphViewAsync();
        });
    }

    private void LangBrButton_OnClick(object sender, RoutedEventArgs e) => SetUiLanguage(GraphUiLanguage.BrazilianPortuguese);

    private void LangEnButton_OnClick(object sender, RoutedEventArgs e) => SetUiLanguage(GraphUiLanguage.English);

    private void LangEsButton_OnClick(object sender, RoutedEventArgs e) => SetUiLanguage(GraphUiLanguage.Spanish);

    private static string ArchitectureLayoutKey(string? solutionPath) =>
        "architecture:" + (string.IsNullOrWhiteSpace(solutionPath) ? "default" : solutionPath);

    private static string CallGraphLayoutKey(string projectId) => "callGraph:" + projectId;

    private static string NamespaceMapLayoutKey(string projectId) => "namespaceMap:" + projectId;

    private static string TypeGraphLayoutKey(string projectId) => "typeGraph:" + projectId;

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
        try
        {
            Clipboard.SetText(FormatProjectReferencesForExport());
            ShowProjectRefsActionStatus(GraphLocalizer.T("RefsCopied"));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowProjectRefsActionStatus(GraphLocalizer.T("RefsCopyFailed"));
        }
    }

    private void ExportPngButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestGraphExport("png");
    }

    private void ExportMermaidButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestGraphExport("mermaid");
    }

    private void RequestGraphExport(string format)
    {
        if (_graphWebView?.CoreWebView2 is null || _currentGraph is null)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ShowStatusSafeAsync(GraphLocalizer.T("GraphExportEmpty"));
            });
            return;
        }

        _pendingGraphExportFormat = format;
        var message = JsonSerializer.Serialize(new { type = "exportRequest", format });
        _graphWebView.CoreWebView2.PostWebMessageAsString(message);
    }

    private void UpdateGraphExportButtonsEnabled(bool enabled)
    {
        var ok = enabled && _webViewReady && _currentGraph is not null;
        ExportPngButton.IsEnabled = ok;
        ExportMermaidButton.IsEnabled = ok;
    }

    private void ExportProjectRefsButton_OnClick(object sender, RoutedEventArgs e)
    {
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
            ShowProjectRefsActionStatus(GraphLocalizer.T("RefsExported"));
        }
        catch (IOException ex)
        {
            ShowProjectRefsActionStatus(string.Format(GraphLocalizer.T("RefsExportFailed"), ex.Message));
        }
    }

    private void ShowProjectRefsActionStatus(string message)
    {
        ProjectRefsActionStatus.Text = message;
        ProjectRefsActionStatus.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ClearProjectRefsActionStatus()
    {
        ShowProjectRefsActionStatus(string.Empty);
    }

    private string FormatProjectReferencesForExport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Projeto: " + _currentDetailProjectName);
        builder.AppendLine(GraphLocalizer.T("SectionProjectRefs") + ":");
        if (_currentProjectReferences.Count == 0)
        {
            builder.AppendLine(GraphLocalizer.T("RefsExportEmpty"));
        }
        else
        {
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
        }

        return builder.ToString().TrimEnd();
    }

    private void TypeLink_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DetailLinkItem item || !item.IsNavigable)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.TypeFullName))
        {
            _selectedTypeFullName = item.TypeFullName;
            WhoUsesButton.IsEnabled = true;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await OpenSourceFileAsync(item.FilePath!, item.Line);
        });
    }

    private void WhoUsesButton_OnClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await RunImpactAnalysisAsync();
        });
    }

    private void ShowTypeGraphButton_OnClick(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ShowTypeGraphAsync();
        });
    }

    private void SolutionScopeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScopeFilterChange || !IsLoaded)
        {
            return;
        }

        if (SolutionScopeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _architectureScopeFilter = item.Tag?.ToString();
        if (_activeGraphView != ActiveGraphView.Architecture)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ShowStatusSafeAsync(GraphLocalizer.T("StatusScopeArchitectureOnly"));
            });
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await PushArchitectureGraphAsync();
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
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusRefreshGraphFirst"));
            return;
        }

        SetActiveGraphView(ActiveGraphView.Architecture);
        await PushArchitectureGraphAsync();
    }

    private async Task PushArchitectureGraphAsync()
    {
        if (_currentGraph is null || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var filtered = FilterGraphByScope(_currentGraph, _architectureScopeFilter);
        var scopedCycles = CircularDependencyDetector.Analyze(filtered);
        var payload = GraphWebPayload.WithLayout(
            GraphJsonSerializer.SerializeSolutionGraph(filtered, scopedCycles),
            ArchitectureLayoutKey(_currentSolutionPath));
        _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
        UpdateArchitectureScopeLabel(filtered, scopedCycles);

        if (filtered.Projects.Count == 0 && !string.IsNullOrWhiteSpace(_architectureScopeFilter))
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusScopeEmpty"));
        }

        if (_activeGraphView == ActiveGraphView.Architecture && scopedCycles.HasCycles)
        {
            ShowCircularDependenciesInSidebar(scopedCycles);
        }
    }

    private void UpdateArchitectureScopeLabel(SolutionGraph filtered, CircularDependencyReport scopedCycles)
    {
        var scopeName = GetScopeDisplayName(_architectureScopeFilter);
        SolutionLabel.Text = scopedCycles.HasCycles
            ? string.Format(
                GraphLocalizer.T("ViewArchitectureScopeCycles"),
                scopeName,
                filtered.Projects.Count,
                scopedCycles.Cycles.Count)
            : string.Format(GraphLocalizer.T("ViewArchitectureScope"), scopeName, filtered.Projects.Count);
    }

    private static string GetScopeDisplayName(string? scopeFilter)
    {
        if (string.IsNullOrWhiteSpace(scopeFilter))
        {
            return GraphLocalizer.T("ScopeAll");
        }

        if (string.Equals(scopeFilter, "__root__", StringComparison.Ordinal))
        {
            return GraphLocalizer.T("ScopeRoot");
        }

        return scopeFilter;
    }

    private bool SolutionHasScopeFolders()
    {
        return _currentGraph?.Projects.Any(p => !string.IsNullOrWhiteSpace(p.SolutionFolderPath)) == true;
    }

    private void UpdateSolutionScopeAvailability()
    {
        var hasFolders = SolutionHasScopeFolders();
        var architectureView = _activeGraphView == ActiveGraphView.Architecture;
        SolutionScopeComboBox.IsEnabled = hasFolders;
        ScopeLabel.Opacity = hasFolders ? 1.0 : 0.55;
        if (!hasFolders)
        {
            SolutionScopeComboBox.ToolTip = GraphLocalizer.T("TooltipScope");
            return;
        }

        SolutionScopeComboBox.ToolTip = architectureView
            ? GraphLocalizer.T("TooltipScope")
            : GraphLocalizer.T("StatusScopeArchitectureOnly");
    }

    private static IEnumerable<string> CollectSolutionFolderScopes(IEnumerable<ProjectNode> projects)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in projects
                     .Select(p => p.SolutionFolderPath)
                     .Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var parts = path.Split('\\');
            var current = string.Empty;
            for (var i = 0; i < parts.Length; i++)
            {
                current = i == 0 ? parts[i] : current + "\\" + parts[i];
                set.Add(current);
            }
        }

        return set.OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }

    private static SolutionGraph FilterGraphByScope(SolutionGraph graph, string? scopeFilter)
    {
        if (string.IsNullOrWhiteSpace(scopeFilter))
        {
            return graph;
        }

        var visibleProjects = graph.Projects.Where(p => ProjectMatchesScope(p, scopeFilter)).ToList();
        var visibleIds = visibleProjects.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = graph.References
            .Where(r => visibleIds.Contains(r.SourceProjectId) && visibleIds.Contains(r.TargetProjectId))
            .ToList();

        return new SolutionGraph
        {
            SolutionPath = graph.SolutionPath,
            Projects = visibleProjects,
            References = references
        };
    }

    private static bool ProjectMatchesScope(ProjectNode project, string scopeFilter)
    {
        if (string.Equals(scopeFilter, "__root__", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(project.SolutionFolderPath);
        }

        if (string.IsNullOrWhiteSpace(project.SolutionFolderPath))
        {
            return false;
        }

        return project.SolutionFolderPath.StartsWith(scopeFilter + "\\", StringComparison.OrdinalIgnoreCase)
               || string.Equals(project.SolutionFolderPath, scopeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void PopulateSolutionScopeCombo()
    {
        if (_currentGraph is null)
        {
            return;
        }

        _suppressScopeFilterChange = true;
        try
        {
            var previous = _architectureScopeFilter;
            SolutionScopeComboBox.Items.Clear();
            SolutionScopeComboBox.Items.Add(new ComboBoxItem
            {
                Content = GraphLocalizer.T("ScopeAll"),
                Tag = string.Empty
            });
            SolutionScopeComboBox.Items.Add(new ComboBoxItem
            {
                Content = GraphLocalizer.T("ScopeRoot"),
                Tag = "__root__"
            });

            foreach (var folder in CollectSolutionFolderScopes(_currentGraph.Projects))
            {
                SolutionScopeComboBox.Items.Add(new ComboBoxItem { Content = folder, Tag = folder });
            }

            var selectedIndex = 0;
            for (var i = 0; i < SolutionScopeComboBox.Items.Count; i++)
            {
                if (SolutionScopeComboBox.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag?.ToString(), previous ?? string.Empty, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }

            SolutionScopeComboBox.SelectedIndex = selectedIndex;
            _architectureScopeFilter = (SolutionScopeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        }
        finally
        {
            _suppressScopeFilterChange = false;
        }

        UpdateSolutionScopeAvailability();
    }

    private async Task ShowTypeGraphAsync()
    {
        if (_currentGraph is null || string.IsNullOrWhiteSpace(_selectedProjectId))
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusSelectProject"));
            return;
        }

        var project = _currentGraph.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, _selectedProjectId, StringComparison.Ordinal));
        if (project is null)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusProjectNotFound"));
            return;
        }

        if (project.Language != ProjectLanguage.CSharp)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusTypeGraphCSharpOnly"));
            return;
        }

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingTypeGraph"));
            var result = await Task.Run(() => _typeGraphAnalyzer.Value.Analyze(project));
            _lastTypeGraph = result;
            var localized = GraphAnalysisLocalizer.Localize(result);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_graphWebView?.CoreWebView2 is null)
            {
                return;
            }

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeTypeGraph(localized),
                TypeGraphLayoutKey(project.Id));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            SetActiveGraphView(ActiveGraphView.TypeGraph);
            SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewTypeGraph"), project.Name);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("StatusTypeGraphError", ex.Message));
        }
        finally
        {
            SetLoading(false, null);
        }
    }

    private async Task RunImpactAnalysisAsync()
    {
        if (_currentGraph is null || string.IsNullOrWhiteSpace(_selectedProjectId))
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusSelectProject"));
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedTypeFullName))
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("SelectTypeForImpact"));
            return;
        }

        var project = _currentGraph.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, _selectedProjectId, StringComparison.Ordinal));
        if (project is null || project.Language != ProjectLanguage.CSharp)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusImpactCSharpOnly"));
            return;
        }

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingImpact"));
            var typeName = _selectedTypeFullName;
            var result = await Task.Run(() =>
                _symbolImpactAnalyzer.Value.Analyze(_currentGraph, project, typeName!));
            _lastImpactAnalysis = result;
            var localized = GraphAnalysisLocalizer.Localize(result);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_graphWebView?.CoreWebView2 is null)
            {
                return;
            }

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeImpactAnalysis(localized),
                ArchitectureLayoutKey(_currentSolutionPath));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            SetActiveGraphView(ActiveGraphView.ImpactGraph);
            SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewImpact"), localized.SymbolLabel);
            ShowImpactInSidebar(localized);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("StatusImpactError", ex.Message));
        }
        finally
        {
            SetLoading(false, null);
        }
    }

    private async Task ShowNamespaceMapAsync()
    {
        if (_currentGraph is null || string.IsNullOrWhiteSpace(_selectedProjectId))
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusSelectProject"));
            return;
        }

        var project = _currentGraph.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, _selectedProjectId, StringComparison.Ordinal));
        if (project is null)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusProjectNotFound"));
            return;
        }

        if (project.Language != ProjectLanguage.CSharp)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusNamespaceCSharpOnly"));
            return;
        }

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingNamespace"));
            var result = await Task.Run(() => _namespaceMapAnalyzer.Value.Analyze(project));
            _lastNamespaceMap = result;
            var localized = GraphAnalysisLocalizer.Localize(result);
            _lastNamespaceProjectName = project.Name;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_graphWebView?.CoreWebView2 is null)
            {
                return;
            }

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeNamespaceMap(localized),
                NamespaceMapLayoutKey(project.Id));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            SetActiveGraphView(ActiveGraphView.NamespaceMap);
            SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewNamespaces"), project.Name);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("StatusNamespaceError", ex.Message));
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
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusSelectProject"));
            return;
        }

        var project = _currentGraph.Projects.FirstOrDefault(p =>
            string.Equals(p.Id, _selectedProjectId, StringComparison.Ordinal));
        if (project is null)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusProjectNotFound"));
            return;
        }

        if (project.Language != ProjectLanguage.CSharp)
        {
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusCallGraphCSharpOnly"));
            return;
        }

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingCallGraph"));
            var result = await Task.Run(() =>
                _callGraphAnalyzer.Value.Analyze(project, _callGraphRootType, null));
            _lastCallGraph = result;
            var localized = GraphAnalysisLocalizer.Localize(result);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_graphWebView?.CoreWebView2 is null)
            {
                return;
            }

            var payload = GraphWebPayload.WithLayout(
                GraphJsonSerializer.SerializeCallGraph(localized),
                CallGraphLayoutKey(project.Id));
            _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
            SetActiveGraphView(ActiveGraphView.CallGraph);
            SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewCallGraph"), project.Name);
            ShowCallGraphInSidebar(localized, project.Name);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("StatusCallGraphError", ex.Message));
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
        await UserGraphSettings.EnsureLoadedAsync().ConfigureAwait(true);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _themePreference = UserGraphSettings.ThemePreference;
        ThemeComboBox.SelectedIndex = (int)_themePreference;
        ApplyChromeTheme();

        await Task.Yield();

        try
        {
            SetLoading(true, GraphLocalizer.T("LoadingInit"));
            await InitializeWebViewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("StatusStartupFailed", FormatUserError(ex)));
        }
        finally
        {
            SetLoading(false, null);
        }

        await RefreshGraphAsync().ConfigureAwait(true);
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

        var environment = await GraphWebViewEnvironment.GetOrCreateAsync().ConfigureAwait(true);
        await _graphWebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
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
            await ShowStatusSafeAsync(GraphLocalizer.T("StatusAnalysisInProgress"));
            return;
        }

        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetLoading(true, GraphLocalizer.T("LoadingAnalyze"));
            RefreshButton.IsEnabled = false;
            UpdateGraphExportButtonsEnabled(false);

            if (!_webViewReady)
            {
                try
                {
                    await InitializeWebViewAsync();
                }
                catch (Exception webEx)
                {
                    await ShowStatusSafeAsync(GraphLocalizer.Format("StatusWebViewUnavailable", FormatUserError(webEx)));
                    return;
                }
            }

            var dte = await GetDteAsync();
            if (dte?.Solution is null || !dte.Solution.IsOpen)
            {
                CloseDetailSidebar();
                await ShowStatusSafeAsync(GraphLocalizer.T("StatusNoSolution"));
                return;
            }

            var solutionPath = dte.Solution.FullName;
            _currentSolutionPath = solutionPath;
            var solutionName = Path.GetFileName(solutionPath);
            var projects = SolutionProjectCollector.CollectFromDte(dte);

            await Task.Run(() =>
            {
                _currentGraph = _graphService.Value.BuildFromProjects(solutionPath, projects);
                _circularDependencyReport = _currentGraph is not null
                    ? CircularDependencyDetector.Analyze(_currentGraph)
                    : new CircularDependencyReport();
            }).ConfigureAwait(true);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await ShowStatusSafeAsync(solutionName);

            if (_currentGraph is null)
            {
                await ShowStatusSafeAsync(GraphLocalizer.T("StatusGraphBuildFailed"));
                return;
            }

            if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
            {
                await ShowStatusSafeAsync(GraphLocalizer.Format("StatusWebViewNotReady", solutionName));
                return;
            }

            _selectedProjectId = null;
            _selectedTypeFullName = null;
            WhoUsesButton.IsEnabled = false;
            ShowCallGraphButton.IsEnabled = false;
            ShowNamespaceMapButton.IsEnabled = false;
            ShowTypeGraphButton.IsEnabled = false;

            PopulateSolutionScopeCombo();
            SetActiveGraphView(ActiveGraphView.Architecture);
            await PushArchitectureGraphAsync();
            await PushThemeToWebViewAsync();
            await PushLocaleToWebViewAsync();

            if (!_circularDependencyReport.HasCycles)
            {
                CloseDetailSidebar();
            }
        }
        catch (Exception ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("StatusGraphError", FormatUserError(ex)));
        }
        finally
        {
            _refreshGate.Release();

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetLoading(false, null);
                RefreshButton.IsEnabled = true;
                UpdateGraphExportButtonsEnabled(_currentGraph is not null);
            }
            catch
            {
                // Semaphore already released — avoids blocking Refresh after an HRESULT on the UI thread.
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

    private async Task RefreshLocalizedGraphViewAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (!_webViewReady || _graphWebView?.CoreWebView2 is null)
        {
            return;
        }

        if (_circularDependencyReport.HasCycles && DetailPanel.Visibility == Visibility.Visible)
        {
            ShowCircularDependenciesInSidebar(_circularDependencyReport);
        }

        switch (_activeGraphView)
        {
            case ActiveGraphView.CallGraph when _lastCallGraph is not null:
            {
                var localized = GraphAnalysisLocalizer.Localize(_lastCallGraph);
                var projectId = _selectedProjectId;
                if (!string.IsNullOrWhiteSpace(projectId))
                {
                    var payload = GraphWebPayload.WithLayout(
                        GraphJsonSerializer.SerializeCallGraph(localized),
                        CallGraphLayoutKey(projectId));
                    _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
                    ShowCallGraphInSidebar(localized, _currentDetailProjectName);
                }

                break;
            }
            case ActiveGraphView.NamespaceMap when _lastNamespaceMap is not null && !string.IsNullOrWhiteSpace(_selectedProjectId):
            {
                var localized = GraphAnalysisLocalizer.Localize(_lastNamespaceMap);
                var payload = GraphWebPayload.WithLayout(
                    GraphJsonSerializer.SerializeNamespaceMap(localized),
                    NamespaceMapLayoutKey(_selectedProjectId));
                _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
                break;
            }
            case ActiveGraphView.TypeGraph when _lastTypeGraph is not null && !string.IsNullOrWhiteSpace(_selectedProjectId):
            {
                var localized = GraphAnalysisLocalizer.Localize(_lastTypeGraph);
                var payload = GraphWebPayload.WithLayout(
                    GraphJsonSerializer.SerializeTypeGraph(localized),
                    TypeGraphLayoutKey(_selectedProjectId));
                _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
                break;
            }
            case ActiveGraphView.ImpactGraph when _lastImpactAnalysis is not null:
            {
                var localized = GraphAnalysisLocalizer.Localize(_lastImpactAnalysis);
                var payload = GraphWebPayload.WithLayout(
                    GraphJsonSerializer.SerializeImpactAnalysis(localized),
                    ArchitectureLayoutKey(_currentSolutionPath));
                _graphWebView.CoreWebView2.PostWebMessageAsString(payload);
                SolutionLabel.Text = string.Format(GraphLocalizer.T("ViewImpact"), localized.SymbolLabel);
                ShowImpactInSidebar(localized);
                break;
            }
        }
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
        }).Task.Forget();
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

            if (string.Equals(messageType, "typeNodeClick", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("nodeId", out var typeNodeIdElement))
                {
                    return;
                }

                var typeNodeId = typeNodeIdElement.GetString();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                FocusTypeGraphNodeInSidebar(typeNodeId);
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

            if (string.Equals(messageType, "exportResult", StringComparison.Ordinal))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await HandleGraphExportResultAsync(root);
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
            ShowTypeGraphButton.IsEnabled = isCSharp;
            WhoUsesButton.IsEnabled = isCSharp && !string.IsNullOrWhiteSpace(_selectedTypeFullName);
            SetDetailSidebarVisible(true);
            DetailContentPanel.Visibility = Visibility.Collapsed;
            DetailLoadingPanel.Visibility = Visibility.Visible;
            PushLanguageHighlightToWebView(project.Language);

            var detail = await Task.Run(() => _compositionAnalyzer.Value.Analyze(project));

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
            await ShowStatusAsync(GraphLocalizer.Format("StatusDetailError", ex.Message));
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

        var fileLinks = cluster.SourceFileLinks
            .Where(f => !string.IsNullOrWhiteSpace(f.FilePath))
            .Select(f => new DetailLinkItem
            {
                DisplayText = f.FileName,
                FilePath = f.FilePath,
                Line = f.Line > 0 ? f.Line : 1
            })
            .ToList();

        SourceFilesList.ItemsSource = fileLinks.Count > 0
            ? fileLinks.Cast<object>().ToList()
            : new[] { new DetailLinkItem { DisplayText = GraphLocalizer.T("NoClusterFiles") } };
    }

    private enum DetailSidebarContent
    {
        Project,
        NamespaceFiles,
        CircularDependencies,
        CallGraphMethods,
        ImpactAnalysis
    }

    private void SetDetailSectionsMode(DetailSidebarContent content)
    {
        var projectMode = content == DetailSidebarContent.Project ? Visibility.Visible : Visibility.Collapsed;
        var namespaceMode = content == DetailSidebarContent.NamespaceFiles ? Visibility.Visible : Visibility.Collapsed;
        var cycleMode = content == DetailSidebarContent.CircularDependencies ? Visibility.Visible : Visibility.Collapsed;
        var callGraphMode = content == DetailSidebarContent.CallGraphMethods ? Visibility.Visible : Visibility.Collapsed;
        var impactMode = content == DetailSidebarContent.ImpactAnalysis ? Visibility.Visible : Visibility.Collapsed;

        CircularDependencyPanel.Visibility = cycleMode;
        SectionProjectRefs.Visibility = projectMode;
        ProjectReferencesList.Visibility = projectMode;
        CopyProjectRefsButton.Visibility = projectMode;
        ExportProjectRefsButton.Visibility = projectMode;
        ProjectRefsActionStatus.Visibility = projectMode == Visibility.Visible
            && !string.IsNullOrWhiteSpace(ProjectRefsActionStatus.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (projectMode != Visibility.Visible)
        {
            ClearProjectRefsActionStatus();
        }
        SectionPackages.Visibility = projectMode;
        PackageReferencesList.Visibility = projectMode;
        TypesSectionPanel.Visibility = projectMode;
        SectionCallMethods.Visibility = callGraphMode;
        SectionImpactUsages.Visibility = impactMode;
        TypesList.Visibility = content == DetailSidebarContent.Project
            || content == DetailSidebarContent.CallGraphMethods
            || content == DetailSidebarContent.ImpactAnalysis
            ? Visibility.Visible
            : Visibility.Collapsed;
        WhoUsesButton.Visibility = projectMode;
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

    private void FocusTypeGraphNodeInSidebar(string? nodeId)
    {
        if (_lastTypeGraph is null || string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        var node = _lastTypeGraph.Nodes.FirstOrDefault(n =>
            string.Equals(n.Id, nodeId, StringComparison.Ordinal));
        if (node is null || string.IsNullOrWhiteSpace(node.SourceFilePath))
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await OpenSourceFileAsync(node.SourceFilePath!, node.SourceLine > 0 ? node.SourceLine : 1);
        });
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

        DetailTitle.Text = GraphLocalizer.T("MsgCircularTitle");
        DetailMeta.Text = GraphLocalizer.Format("MsgCircularMeta", report.Cycles.Count);
        DetailLanguageBadge.Visibility = Visibility.Collapsed;

        CircularDependencyTitle.Text = report.Cycles.Count == 1
            ? GraphLocalizer.T("MsgCircularSingle")
            : GraphLocalizer.Format("MsgCircularMultiple", report.Cycles.Count);

        CircularDependencyCyclesList.ItemsSource = report.Cycles
            .Select((cycle, index) => new DetailCycleItem
            {
                Index = index,
                ProjectNames = cycle.ProjectNames,
                HighlightCycleLabel = GraphLocalizer.T("HighlightCycle")
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

        CopyProjectRefsButton.IsEnabled = true;
        ExportProjectRefsButton.IsEnabled = true;
        ClearProjectRefsActionStatus();

        ProjectReferencesList.ItemsSource = detail.ProjectReferences.Count > 0
            ? detail.ProjectReferences.Select(r => r.Name).Cast<object>().ToList()
            : new[] { GraphLocalizer.T("RefsExportEmpty") };

        PackageReferencesList.ItemsSource = detail.PackageReferences.Count > 0
            ? detail.PackageReferences.Select(p => $"{p.Name} ({p.Version})").Cast<object>().ToList()
            : new[] { GraphLocalizer.T("MsgNoNuGetPackages") };

        _selectedTypeFullName = null;
        WhoUsesButton.IsEnabled = false;
        TypesList.ItemsSource = detail.Types.Count > 0
            ? detail.Types.Select(CreateTypeLinkItem).ToList()
            : new[] { new DetailLinkItem { DisplayText = GraphLocalizer.T("MsgNoTypesInSource") } };
    }

    private void ShowImpactInSidebar(ImpactAnalysisResult result)
    {
        SetDetailSidebarVisible(true);
        SetDetailSectionsMode(DetailSidebarContent.ImpactAnalysis);
        DetailLanguageBadge.Visibility = Visibility.Visible;
        DetailLoadingPanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;

        DetailTitle.Text = result.SymbolLabel;
        DetailMeta.Text = result.DefinitionProjectName;
        ApplyLanguageBadge(ProjectLanguage.CSharp);

        CopyProjectRefsButton.IsEnabled = false;
        ExportProjectRefsButton.IsEnabled = false;

        var links = result.Usages
            .Where(u => !string.IsNullOrWhiteSpace(u.FilePath))
            .Select(u => new DetailLinkItem
            {
                DisplayText = $"{u.ProjectName} · {Path.GetFileName(u.FilePath)}:{u.Line}",
                FilePath = u.FilePath,
                Line = u.Line > 0 ? u.Line : 1
            })
            .ToList();

        TypesList.ItemsSource = links.Count > 0
            ? links.Cast<object>().ToList()
            : new[] { new DetailLinkItem { DisplayText = result.Message ?? GraphLocalizer.T("MsgNoUsages") } };
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
            Line = type.SourceLine > 0 ? type.SourceLine : 1,
            TypeFullName = type.FullName
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
        GraphThemeService.EnsureVsWpfStyles(this);
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
            ScopeLabel,
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
            // Ignore COM/UI failures when closing VS or after a fatal error on the main thread.
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

    private async Task HandleGraphExportResultAsync(JsonElement root)
    {
        if (_pendingGraphExportFormat is null)
        {
            return;
        }

        var expectedFormat = _pendingGraphExportFormat;
        _pendingGraphExportFormat = null;

        if (!root.TryGetProperty("success", out var successElement) || !successElement.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            if (string.Equals(error, "empty", StringComparison.Ordinal))
            {
                await ShowStatusSafeAsync(GraphLocalizer.T("GraphExportEmpty"));
            }
            else
            {
                await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportFailed", error ?? "?"));
            }

            return;
        }

        var format = root.TryGetProperty("format", out var formatElement)
            ? formatElement.GetString()
            : expectedFormat;
        if (!string.Equals(format, expectedFormat, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var defaultFileName = root.TryGetProperty("fileName", out var fileNameElement)
            ? fileNameElement.GetString()
            : "dotnet-graph";

        try
        {
            if (string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
            {
                if (!root.TryGetProperty("pngBase64", out var pngElement))
                {
                    await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportFailed", "PNG"));
                    return;
                }

                var dataUrl = pngElement.GetString() ?? string.Empty;
                const string prefix = "data:image/png;base64,";
                if (dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    dataUrl = dataUrl.Substring(prefix.Length);
                }

                var bytes = Convert.FromBase64String(dataUrl);
                var dialog = new SaveFileDialog
                {
                    Title = GraphLocalizer.T("GraphExportDialogPng"),
                    Filter = "PNG (*.png)|*.png|Todos (*.*)|*.*",
                    FileName = string.IsNullOrWhiteSpace(defaultFileName) ? "dotnet-graph.png" : defaultFileName,
                    DefaultExt = ".png"
                };

                if (dialog.ShowDialog() != true)
                {
                    await ShowStatusSafeAsync(GraphLocalizer.T("GraphExportCancelled"));
                    return;
                }

                File.WriteAllBytes(dialog.FileName, bytes);
                await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportSuccess", Path.GetFileName(dialog.FileName)));
                return;
            }

            if (string.Equals(format, "mermaid", StringComparison.OrdinalIgnoreCase))
            {
                if (!root.TryGetProperty("mermaid", out var mermaidElement))
                {
                    await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportFailed", "Mermaid"));
                    return;
                }

                var diagram = mermaidElement.GetString() ?? string.Empty;
                var dialog = new SaveFileDialog
                {
                    Title = GraphLocalizer.T("GraphExportDialogMermaid"),
                    Filter = "Mermaid (*.mmd)|*.mmd|Markdown (*.md)|*.md|Todos (*.*)|*.*",
                    FileName = string.IsNullOrWhiteSpace(defaultFileName) ? "dotnet-graph.mmd" : defaultFileName,
                    DefaultExt = ".mmd"
                };

                if (dialog.ShowDialog() != true)
                {
                    await ShowStatusSafeAsync(GraphLocalizer.T("GraphExportCancelled"));
                    return;
                }

                File.WriteAllBytes(dialog.FileName, Encoding.UTF8.GetBytes(diagram));
                await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportSuccess", Path.GetFileName(dialog.FileName)));
            }
        }
        catch (FormatException ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportFailed", ex.Message));
        }
        catch (IOException ex)
        {
            await ShowStatusSafeAsync(GraphLocalizer.Format("GraphExportFailed", ex.Message));
        }
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
