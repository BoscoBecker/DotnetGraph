using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;

namespace DotnetGraph.Extension.Services;

internal static class GraphThemeService
{
    public const string SidebarLinkBrushKey = "SidebarLinkBrush";
    private static bool _vsDefaultStylesMerged;

    public static string ResolveWebTheme(GraphThemePreference preference) =>
        preference switch
        {
            GraphThemePreference.Light => "light",
            GraphThemePreference.Dark => "dark",
            GraphThemePreference.System => IsVisualStudioDark() ? "dark" : "light",
            _ => "light"
        };

    public static bool IsVisualStudioDark()
    {
        var vsTheme = TryGetVisualStudioThemeName();
        if (!string.IsNullOrWhiteSpace(vsTheme))
        {
            return vsTheme.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return IsWindowsAppsDarkTheme();
    }

    private static string? TryGetVisualStudioThemeName()
    {
        try
        {
            using var visualStudioKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\VisualStudio");
            if (visualStudioKey is null)
            {
                return null;
            }

            foreach (var instanceName in visualStudioKey.GetSubKeyNames())
            {
                if (!instanceName.StartsWith("17.", StringComparison.Ordinal)
                    && !instanceName.StartsWith("18.", StringComparison.Ordinal))
                {
                    continue;
                }

                using var generalKey = visualStudioKey.OpenSubKey(instanceName + @"\General");
                var theme =
                    generalKey?.GetValue("CurrentTheme") as string
                    ?? generalKey?.GetValue("Theme") as string
                    ?? generalKey?.GetValue("VSSetTheme") as string;

                if (!string.IsNullOrWhiteSpace(theme))
                {
                    return theme;
                }
            }
        }
        catch (System.Security.SecurityException)
        {
        }

        return null;
    }

    public static void EnsureVsWpfStyles(FrameworkElement host)
    {
        if (_vsDefaultStylesMerged)
        {
            return;
        }

        if (Application.Current?.TryFindResource(VsResourceKeys.ThemedDialogDefaultStylesKey) is ResourceDictionary dictionary)
        {
            host.Resources.MergedDictionaries.Add(dictionary);
            _vsDefaultStylesMerged = true;
        }
    }

    public static void ApplyVsFluentHeader(
        FrameworkElement headerPanel,
        ComboBox comboBox,
        TextBlock themeLabel,
        TextBlock languageLabel,
        FrameworkElement brushRoot,
        bool useVisualStudioCombo)
    {
        EnsureVsWpfStyles(headerPanel);
        if (headerPanel is Panel headerAsPanel)
        {
            headerAsPanel.Background = Brushes.Transparent;
        }

        if (headerPanel is ToolBar toolbar)
        {
            toolbar.BorderBrush = Brushes.Transparent;
        }

        if (useVisualStudioCombo)
        {
            ApplyVsComboBoxColors(comboBox);
        }
        else
        {
            ApplyFluentComboBoxColors(comboBox, brushRoot);
        }

        ApplyVsCaptionLabel(themeLabel);
        ApplyVsCaptionLabel(languageLabel);
    }

    public static void ApplyFluentComboBoxColors(ComboBox comboBox, FrameworkElement resourceRoot)
    {
        comboBox.Background = GetResourceBrush(resourceRoot, "GraphFluentSurfaceBrush");
        comboBox.BorderBrush = GetResourceBrush(resourceRoot, "GraphFluentBorderBrush");
        comboBox.Foreground = GetResourceBrush(resourceRoot, "GraphFluentTextBrush");
    }

    private static Brush GetResourceBrush(FrameworkElement root, string key) =>
        root.TryFindResource(key) as Brush ?? Brushes.Transparent;

    public static void ApplyVsComboBoxColors(ComboBox comboBox)
    {
        comboBox.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        comboBox.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        comboBox.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
    }

    public static void SetFluentResourceBrushes(Control root, bool dark)
    {
        root.Resources["GraphFluentSurfaceBrush"] = Brush(dark, 0x2D, 0x2D, 0x30, 0xFF, 0xFF, 0xFF);
        root.Resources["GraphFluentSurfaceHoverBrush"] = Brush(dark, 0x3E, 0x3E, 0x42, 0xF5, 0xF5, 0xF5);
        root.Resources["GraphFluentBorderBrush"] = Brush(dark, 0x3F, 0x3F, 0x46, 0xE1, 0xE1, 0xE1);
        root.Resources["GraphFluentTextBrush"] = Brush(dark, 0xF3, 0xF3, 0xF3, 0x1E, 0x1E, 0x1E);
        root.Resources["GraphFluentAccentBrush"] = Brush(dark, 0x0E, 0x63, 0x9C, 0x0E, 0x63, 0x9C);
        root.Resources["GraphFluentAccentHoverBrush"] = Brush(dark, 0x11, 0x76, 0xB3, 0x0E, 0x70, 0xC0);
        root.Resources["GraphFluentAccentTextBrush"] = Brushes.White;
    }

    public static void ApplyVsProgressBarTheme(ProgressBar progressBar)
    {
        progressBar.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ProgressBarStyleKey);
    }

    private static void ApplyVsCaptionLabel(TextBlock label)
    {
        label.SetResourceReference(TextBlock.StyleProperty, VsResourceKeys.TextBlockEnvironment90PercentFontSizeStyleKey);
        label.ClearValue(TextBlock.FontSizeProperty);
    }

    private static void ApplyToolbarChildStyle(ToolBar toolbar, ResourceKey toolbarStyleKey, object vsStyleKey)
    {
        if (Application.Current?.TryFindResource(vsStyleKey) is Style style)
        {
            toolbar.Resources[toolbarStyleKey] = style;
        }
    }

    public static void ApplyWpfChrome(
        GraphThemePreference preference,
        Control root,
        Border header,
        Border graphHost,
        Border detailPanel,
        Border detailHeader,
        TextBlock title,
        TextBlock subtitle,
        TextBlock detailTitle,
        TextBlock detailMeta,
        TextBlock themeLabel,
        TextBlock languageLabel,
        TextBlock detailLoadingText,
        TextBlock sectionProjectRefs,
        TextBlock sectionPackages,
        TextBlock sectionTypes,
        ItemsControl projectReferencesList,
        ItemsControl packageReferencesList,
        ItemsControl typesList,
        Button refreshButton,
        Button closeDetailButton,
        GridSplitter detailSplitter,
        Border loadingOverlay,
        TextBlock loadingText,
        Button showArchitectureButton,
        Button showCallGraphButton,
        Button showNamespaceMapButton,
        Button copyProjectRefsButton,
        Button exportProjectRefsButton,
        FrameworkElement headerActionsPanel,
        ComboBox themeComboBox,
        ProgressBar detailLoadingProgress,
        ProgressBar overlayProgress)
    {
        if (preference == GraphThemePreference.System)
        {
            ApplyVisualStudioChrome(
                root, header, graphHost, detailPanel, detailHeader, title, subtitle, detailTitle, detailMeta,
                themeLabel, languageLabel, detailLoadingText, sectionProjectRefs, sectionPackages, sectionTypes,
                projectReferencesList, packageReferencesList, typesList, refreshButton, closeDetailButton,
                detailSplitter, loadingOverlay, loadingText, showArchitectureButton, showCallGraphButton,
                showNamespaceMapButton, copyProjectRefsButton, exportProjectRefsButton,
                headerActionsPanel, themeComboBox, detailLoadingProgress, overlayProgress);
            return;
        }

        ApplyExplicitChrome(
            preference == GraphThemePreference.Dark,
            root, header, graphHost, detailPanel, detailHeader, title, subtitle, detailTitle, detailMeta,
            themeLabel, languageLabel, detailLoadingText, sectionProjectRefs, sectionPackages, sectionTypes,
            projectReferencesList, packageReferencesList, typesList, refreshButton, closeDetailButton,
            detailSplitter, loadingOverlay, loadingText, showArchitectureButton, showCallGraphButton,
            showNamespaceMapButton, copyProjectRefsButton, exportProjectRefsButton,
            headerActionsPanel, themeComboBox, detailLoadingProgress, overlayProgress);
    }

    private static void ApplyVisualStudioChrome(
        Control root,
        Border header,
        Border graphHost,
        Border detailPanel,
        Border detailHeader,
        TextBlock title,
        TextBlock subtitle,
        TextBlock detailTitle,
        TextBlock detailMeta,
        TextBlock themeLabel,
        TextBlock languageLabel,
        TextBlock detailLoadingText,
        TextBlock sectionProjectRefs,
        TextBlock sectionPackages,
        TextBlock sectionTypes,
        ItemsControl projectReferencesList,
        ItemsControl packageReferencesList,
        ItemsControl typesList,
        Button refreshButton,
        Button closeDetailButton,
        GridSplitter detailSplitter,
        Border loadingOverlay,
        TextBlock loadingText,
        Button showArchitectureButton,
        Button showCallGraphButton,
        Button showNamespaceMapButton,
        Button copyProjectRefsButton,
        Button exportProjectRefsButton,
        FrameworkElement headerActionsPanel,
        ComboBox themeComboBox,
        ProgressBar detailLoadingProgress,
        ProgressBar overlayProgress)
    {
        root.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        header.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        header.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        graphHost.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        detailPanel.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        detailPanel.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        detailHeader.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        detailHeader.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        detailSplitter.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);

        BindTextTheme(title, subtitle, detailTitle, detailMeta, themeLabel, languageLabel, detailLoadingText,
            sectionProjectRefs, sectionPackages, sectionTypes, projectReferencesList, packageReferencesList, typesList, loadingText);

        loadingOverlay.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

        SetSidebarLinkBrush(root, EnvironmentColors.PanelHyperlinkBrushKey);
        SetFluentResourceBrushes(root, IsVisualStudioDark());
        ApplyVsFluentChrome(root, headerActionsPanel, themeComboBox, themeLabel, languageLabel, detailLoadingProgress, overlayProgress, useVisualStudioCombo: false);
    }

    private static void ApplyExplicitChrome(
        bool dark,
        Control root,
        Border header,
        Border graphHost,
        Border detailPanel,
        Border detailHeader,
        TextBlock title,
        TextBlock subtitle,
        TextBlock detailTitle,
        TextBlock detailMeta,
        TextBlock themeLabel,
        TextBlock languageLabel,
        TextBlock detailLoadingText,
        TextBlock sectionProjectRefs,
        TextBlock sectionPackages,
        TextBlock sectionTypes,
        ItemsControl projectReferencesList,
        ItemsControl packageReferencesList,
        ItemsControl typesList,
        Button refreshButton,
        Button closeDetailButton,
        GridSplitter detailSplitter,
        Border loadingOverlay,
        TextBlock loadingText,
        Button showArchitectureButton,
        Button showCallGraphButton,
        Button showNamespaceMapButton,
        Button copyProjectRefsButton,
        Button exportProjectRefsButton,
        FrameworkElement headerActionsPanel,
        ComboBox themeComboBox,
        ProgressBar detailLoadingProgress,
        ProgressBar overlayProgress)
    {
        var background = Brush(dark, 0x1E, 0x1E, 0x1E, 0xF6, 0xF6, 0xF6);
        var surface = Brush(dark, 0x25, 0x25, 0x26, 0xFF, 0xFF, 0xFF);
        var border = Brush(dark, 0x3F, 0x3F, 0x46, 0xE1, 0xE1, 0xE1);
        var text = Brush(dark, 0xF3, 0xF3, 0xF3, 0x1E, 0x1E, 0x1E);
        var link = Brush(dark, 0x37, 0x94, 0xFF, 0x0E, 0x70, 0xC0);
        var overlay = new SolidColorBrush(dark ? Color.FromArgb(240, 0x1E, 0x1E, 0x1E) : Color.FromArgb(240, 0xF6, 0xF6, 0xF6));

        SetSolid(root, Control.BackgroundProperty, background);
        SetSolid(header, Border.BackgroundProperty, surface);
        SetSolid(header, Border.BorderBrushProperty, border);
        SetSolid(graphHost, Border.BackgroundProperty, background);
        SetSolid(detailPanel, Border.BackgroundProperty, surface);
        SetSolid(detailPanel, Border.BorderBrushProperty, border);
        SetSolid(detailHeader, Border.BackgroundProperty, surface);
        SetSolid(detailHeader, Border.BorderBrushProperty, border);
        SetSolid(detailSplitter, Control.BackgroundProperty, border);
        SetSolid(loadingOverlay, Border.BackgroundProperty, overlay);

        SetSolid(title, TextBlock.ForegroundProperty, text);
        SetSolid(subtitle, TextBlock.ForegroundProperty, text);
        SetSolid(detailTitle, TextBlock.ForegroundProperty, text);
        SetSolid(detailMeta, TextBlock.ForegroundProperty, text);
        SetSolid(themeLabel, TextBlock.ForegroundProperty, text);
        SetSolid(languageLabel, TextBlock.ForegroundProperty, text);
        SetSolid(detailLoadingText, TextBlock.ForegroundProperty, text);
        SetSolid(sectionProjectRefs, TextBlock.ForegroundProperty, text);
        SetSolid(sectionPackages, TextBlock.ForegroundProperty, text);
        SetSolid(sectionTypes, TextBlock.ForegroundProperty, text);
        SetSolid(projectReferencesList, Control.ForegroundProperty, text);
        SetSolid(packageReferencesList, Control.ForegroundProperty, text);
        SetSolid(typesList, Control.ForegroundProperty, text);
        SetSolid(loadingText, TextBlock.ForegroundProperty, text);

        root.Resources[SidebarLinkBrushKey] = link;
        SetFluentResourceBrushes(root, dark);
        SetSolid(themeLabel, TextBlock.ForegroundProperty, text);
        SetSolid(languageLabel, TextBlock.ForegroundProperty, text);
        ApplyVsFluentChrome(root, headerActionsPanel, themeComboBox, themeLabel, languageLabel, detailLoadingProgress, overlayProgress, useVisualStudioCombo: false);
    }

    private static void ApplyVsFluentChrome(
        FrameworkElement brushRoot,
        FrameworkElement headerActionsPanel,
        ComboBox themeComboBox,
        TextBlock themeLabel,
        TextBlock languageLabel,
        ProgressBar detailLoadingProgress,
        ProgressBar overlayProgress,
        bool useVisualStudioCombo)
    {
        if (headerActionsPanel is Panel headerAsPanel)
        {
            headerAsPanel.Background = Brushes.Transparent;
        }

        ApplyVsFluentHeader(headerActionsPanel, themeComboBox, themeLabel, languageLabel, brushRoot, useVisualStudioCombo);
        ApplyVsProgressBarTheme(detailLoadingProgress);
        ApplyVsProgressBarTheme(overlayProgress);
    }

    private static void BindTextTheme(
        TextBlock title,
        TextBlock subtitle,
        TextBlock detailTitle,
        TextBlock detailMeta,
        TextBlock themeLabel,
        TextBlock languageLabel,
        TextBlock detailLoadingText,
        TextBlock sectionProjectRefs,
        TextBlock sectionPackages,
        TextBlock sectionTypes,
        ItemsControl projectReferencesList,
        ItemsControl packageReferencesList,
        ItemsControl typesList,
        TextBlock loadingText)
    {
        title.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        detailTitle.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        detailMeta.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        themeLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        languageLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        detailLoadingText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        sectionProjectRefs.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        sectionPackages.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        sectionTypes.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        projectReferencesList.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        packageReferencesList.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        typesList.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        loadingText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
    }

    private static void SetSidebarLinkBrush(Control root, ThemeResourceKey vsLinkKey)
    {
        try
        {
            var themed = VSColorTheme.GetThemedColor(vsLinkKey);
            var color = Color.FromArgb(themed.A, themed.R, themed.G, themed.B);
            root.Resources[SidebarLinkBrushKey] = new SolidColorBrush(color);
            return;
        }
        catch (InvalidOperationException)
        {
        }

        root.Resources[SidebarLinkBrushKey] = new SolidColorBrush(Color.FromRgb(0x0E, 0x70, 0xC0));
    }

    private static SolidColorBrush Brush(bool dark, byte dr, byte dg, byte db, byte lr, byte lg, byte lb) =>
        new(dark ? Color.FromRgb(dr, dg, db) : Color.FromRgb(lr, lg, lb));

    private static void SetSolid(DependencyObject target, DependencyProperty property, Brush brush)
    {
        if (target is FrameworkElement element)
        {
            element.ClearValue(property);
        }

        target.SetValue(property, brush);
    }

    private static bool IsWindowsAppsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
