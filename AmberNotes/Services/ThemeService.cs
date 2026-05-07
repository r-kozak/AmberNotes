using System;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace AmberNotes.Services;

/// <summary>
/// Available visual themes.
/// </summary>
public enum AppTheme
{
    /// <summary>Warm dark theme — deep darks + amber accent (#FFBF00).</summary>
    AmberNoir,
    /// <summary>Warm light theme — parchment + saffron accent (#D97706).</summary>
    SaffronLinen
}

/// <summary>
/// Singleton service that switches global visual themes at runtime via
/// Avalonia's DynamicResource / ResourceDictionary.MergedDictionaries.
/// Works identically on Desktop and Android (avares:// URIs are cross-platform).
/// </summary>
public sealed class ThemeService
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static readonly ThemeService Instance = new();
    private ThemeService() { }

    // ── State ─────────────────────────────────────────────────────────────────
    private AppTheme _currentTheme = AppTheme.AmberNoir;

    public AppTheme CurrentTheme => _currentTheme;
    public bool IsDark => _currentTheme == AppTheme.AmberNoir;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<AppTheme>? ThemeChanged;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the default theme (AmberNoir) on first launch.
    /// Call once from App.OnFrameworkInitializationCompleted AFTER
    /// AvaloniaXamlLoader.Load(this) so Application.Resources is ready.
    /// </summary>
    public void Initialize() => ApplyTheme(_currentTheme);

    /// <summary>Switches to the given theme immediately.</summary>
    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        _currentTheme = theme;
        ApplyTheme(theme);
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>Toggles between AmberNoir ↔ SaffronLinen.</summary>
    public void ToggleTheme() =>
        SetTheme(_currentTheme == AppTheme.AmberNoir ? AppTheme.SaffronLinen : AppTheme.AmberNoir);

    // ── Core ──────────────────────────────────────────────────────────────────

    private static void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("Application.Current is null — call Initialize() after Avalonia starts.");

        var uri = theme == AppTheme.AmberNoir
            ? new Uri("avares://AmberNotes/Styles/Themes/AmberNoir.axaml")
            : new Uri("avares://AmberNotes/Styles/Themes/SaffronLinen.axaml");

        var merged = app.Resources.MergedDictionaries;

        // Remove every ResourceInclude that belongs to our theme folder
        var stale = merged
            .OfType<ResourceInclude>()
            .Where(r => r.Source?.ToString().Contains("/Styles/Themes/") == true)
            .ToList();

        foreach (var d in stale)
            merged.Remove(d);

        // Load and register the new theme dictionary
        var baseUri = new Uri("avares://AmberNotes/");
        merged.Add(new ResourceInclude(baseUri) { Source = uri });

        // Keep the Fluent base variant in sync so built-in controls look right
        app.RequestedThemeVariant = theme == AppTheme.AmberNoir
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }
}
