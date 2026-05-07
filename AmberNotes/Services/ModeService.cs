using System;

namespace AmberNotes.Services;

/// <summary>
/// Application working mode.
/// </summary>
public enum AppMode
{
    /// <summary>Public mode — shows un-encrypted (or public-typed) notes.</summary>
    Public,
    /// <summary>Private mode — requires authentication; shows private notes only.</summary>
    Private
}

/// <summary>
/// Singleton service that tracks the current Public / Private mode.
/// Raises ModeChanged when the mode is switched so that any subscriber
/// (ViewModels, views) can react without tight coupling.
///
/// Security Bridge (Step 17) will add password re-prompting here;
/// for now the switch is unconditional.
/// </summary>
public sealed class ModeService
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static readonly ModeService Instance = new();
    private ModeService() { }

    // ── State ─────────────────────────────────────────────────────────────────
    private AppMode _currentMode = AppMode.Public;

    public AppMode CurrentMode => _currentMode;
    public bool IsPrivate => _currentMode == AppMode.Private;
    public bool IsPublic  => _currentMode == AppMode.Public;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<AppMode>? ModeChanged;

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetMode(AppMode mode)
    {
        if (_currentMode == mode) return;
        _currentMode = mode;
        ModeChanged?.Invoke(mode);
    }

    public void ToggleMode() =>
        SetMode(_currentMode == AppMode.Public ? AppMode.Private : AppMode.Public);
}
