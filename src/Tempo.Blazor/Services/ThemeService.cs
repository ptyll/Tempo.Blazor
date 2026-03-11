namespace Tempo.Blazor.Services;

/// <summary>
/// Manages the current theme (light/dark) for the application.
/// Registered as Scoped — each circuit (Server) or tab (WASM) gets its own instance.
/// </summary>
public class ThemeService
{
    private bool _isDark;

    /// <summary>Gets whether the dark theme is currently active.</summary>
    public bool IsDark => _isDark;

    /// <summary>Gets the current theme name ("dark" or "light").</summary>
    public string ThemeName => _isDark ? "dark" : "light";

    /// <summary>Raised whenever the theme changes.</summary>
    public event Action? OnChanged;

    /// <summary>Toggles between light and dark theme.</summary>
    public void Toggle()
    {
        _isDark = !_isDark;
        OnChanged?.Invoke();
    }
}
