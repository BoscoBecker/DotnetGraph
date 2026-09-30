using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DotnetGraph.Extension.Services;

internal static class UserGraphSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotnetGraph",
            "settings.json");

    public static GraphThemePreference ThemePreference { get; private set; } = GraphThemePreference.System;

    public static GraphUiLanguage UiLanguage { get; private set; } = GraphUiLanguage.BrazilianPortuguese;

    public static double DetailPanelWidth { get; private set; } = 360;

    private static readonly Dictionary<string, Dictionary<string, LayoutPoint>> Layouts = new(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<UserGraphSettingsDto>(json, JsonOptions);
            if (dto is null)
            {
                return;
            }

            if (Enum.IsDefined(typeof(GraphThemePreference), dto.ThemePreference))
            {
                ThemePreference = (GraphThemePreference)dto.ThemePreference;
            }

            if (dto.UiLanguage is >= 0 and <= 2)
            {
                UiLanguage = (GraphUiLanguage)dto.UiLanguage;
            }

            if (dto.DetailPanelWidth is >= 240 and <= 720)
            {
                DetailPanelWidth = dto.DetailPanelWidth.Value;
            }

            if (dto.Layouts is not null)
            {
                Layouts.Clear();
                foreach (var entry in dto.Layouts)
                {
                    if (entry.Value is null)
                    {
                        continue;
                    }

                    Layouts[entry.Key] = new Dictionary<string, LayoutPoint>(entry.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }

    public static IReadOnlyDictionary<string, LayoutPoint>? GetLayout(string layoutKey)
    {
        return Layouts.TryGetValue(layoutKey, out var layout) ? layout : null;
    }

    public static void SaveLayout(string layoutKey, IReadOnlyDictionary<string, LayoutPoint> positions)
    {
        Layouts[layoutKey] = positions.ToDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase);
        PersistSettings();
    }

    public static void SaveTheme(GraphThemePreference preference)
    {
        ThemePreference = preference;
        PersistSettings();
    }

    public static void SaveUiLanguage(GraphUiLanguage language)
    {
        UiLanguage = language;
        PersistSettings();
    }

    public static void SaveDetailPanelWidth(double width)
    {
        DetailPanelWidth = Math.Max(240, Math.Min(720, width));
        PersistSettings();
    }

    private static void PersistSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            var dto = new UserGraphSettingsDto
            {
                ThemePreference = (int)ThemePreference,
                UiLanguage = (int)UiLanguage,
                DetailPanelWidth = DetailPanelWidth,
                Layouts = Layouts
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (IOException)
        {
        }
    }

    internal sealed class LayoutPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    private sealed class UserGraphSettingsDto
    {
        public int ThemePreference { get; set; }
        public int UiLanguage { get; set; }
        public double? DetailPanelWidth { get; set; }
        public Dictionary<string, Dictionary<string, LayoutPoint>>? Layouts { get; set; }
    }
}
