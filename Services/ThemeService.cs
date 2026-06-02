using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Effortless.Services;

/// <summary>
/// Owns the app's dark/light theme. Brushes are exposed as DynamicResource keys
/// (<c>WindowBackgroundBrush</c>, <c>TextPrimaryBrush</c>, …), so a theme swap
/// updates every window live without a restart.
/// </summary>
public static class ThemeService
{
    private static ResourceDictionary? _current;

    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after the active theme changes.</summary>
    public static event EventHandler? Changed;

    public static void ApplyFromSettings()
    {
        Apply(StorageService.LoadSettings().IsDarkMode);
    }

    public static void Apply(bool isDark)
    {
        IsDark = isDark;
        var next = isDark ? BuildDark() : BuildLight();
        var app = System.Windows.Application.Current;
        if (_current != null && app.Resources.MergedDictionaries.Contains(_current))
            app.Resources.MergedDictionaries.Remove(_current);
        _current = next;
        app.Resources.MergedDictionaries.Add(next);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Toggle()
    {
        var newValue = !IsDark;
        var settings = StorageService.LoadSettings();
        settings.IsDarkMode = newValue;
        StorageService.SaveSettings(settings);
        Apply(newValue);
    }

    private static ResourceDictionary BuildDark() => Build(new Dictionary<string, string>
    {
        ["WindowBackgroundBrush"]    = "#000000",
        ["ChromeBackgroundBrush"]    = "#0a0a0a",
        ["InputBackgroundBrush"]     = "#141414",
        ["PanelBackgroundBrush"]     = "#1a1a1a",
        ["SunkenBackgroundBrush"]    = "#1f1f1f",
        ["ElevatedBackgroundBrush"]  = "#2a2a2a",

        ["BorderBrush"]              = "#333333",
        ["BorderSubtleBrush"]        = "#1a1a1a",

        ["TextPrimaryBrush"]         = "#f0f0f0",
        ["TextSecondaryBrush"]       = "#d0d0d0",
        ["TextMutedBrush"]           = "#888888",
        ["TextDimBrush"]             = "#666666",
        ["TextSubtleBrush"]          = "#555555",

        ["CaretBrush"]               = "#ffffff",

        ["ScrollbarThumbBrush"]      = "#2a2a2a",
        ["ScrollbarThumbHoverBrush"] = "#4a4a4a",
        ["ScrollbarThumbDragBrush"]  = "#555555",

        ["AccentRedBrush"]           = "#EF5350",
        ["AccentGreenBrush"]         = "#4CAF50",
    });

    private static ResourceDictionary BuildLight() => Build(new Dictionary<string, string>
    {
        ["WindowBackgroundBrush"]    = "#ffffff",
        ["ChromeBackgroundBrush"]    = "#f4f4f5",
        ["InputBackgroundBrush"]     = "#fafafa",
        ["PanelBackgroundBrush"]     = "#f0f0f0",
        ["SunkenBackgroundBrush"]    = "#e9e9eb",
        ["ElevatedBackgroundBrush"]  = "#d4d4d8",

        ["BorderBrush"]              = "#d4d4d8",
        ["BorderSubtleBrush"]        = "#e5e5e5",

        ["TextPrimaryBrush"]         = "#18181b",
        ["TextSecondaryBrush"]       = "#3f3f46",
        ["TextMutedBrush"]           = "#71717a",
        ["TextDimBrush"]             = "#a1a1aa",
        ["TextSubtleBrush"]          = "#b4b4b8",

        ["CaretBrush"]               = "#18181b",

        ["ScrollbarThumbBrush"]      = "#c1c1c8",
        ["ScrollbarThumbHoverBrush"] = "#8a8a92",
        ["ScrollbarThumbDragBrush"]  = "#6b6b73",

        ["AccentRedBrush"]           = "#EF5350",
        ["AccentGreenBrush"]         = "#16a34a",
    });

    private static ResourceDictionary Build(Dictionary<string, string> map)
    {
        var dict = new ResourceDictionary();
        var converter = new BrushConverter();
        foreach (var (key, hex) in map)
        {
            if (converter.ConvertFromString(hex) is Brush brush)
            {
                brush.Freeze();
                dict[key] = brush;
            }
        }
        return dict;
    }
}
