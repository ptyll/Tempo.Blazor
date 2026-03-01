using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Components.Icons;

/// <summary>
/// Static registry for custom SVG icons. Consuming applications call
/// <see cref="Register"/> or <see cref="RegisterProvider"/> in their
/// <c>Program.cs</c> before the app starts.
///
/// <example>
/// <code>
/// // Program.cs
/// IconRegistry.Register("my-logo", "&lt;svg viewBox='0 0 24 24'&gt;...&lt;/svg&gt;");
/// // or
/// IconRegistry.RegisterProvider(new MyFontIconProvider());
/// </code>
/// </example>
/// </summary>
public static class IconRegistry
{
    private static readonly Dictionary<string, string> _icons
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<IIconProvider> _providers = [];

    // Lock for thread-safe registration (typically called once at startup)
    private static readonly object _lock = new();

    /// <summary>
    /// Registers an inline SVG string for the given icon name.
    /// The <paramref name="svgContent"/> should be the inner content of the &lt;svg&gt; tag
    /// (i.e. path, circle, etc. elements — NOT the &lt;svg&gt; wrapper).
    /// Case-insensitive: "MyIcon" and "myicon" resolve to the same entry.
    /// </summary>
    public static void Register(string name, string svgContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(svgContent);

        lock (_lock)
            _icons[name] = svgContent;
    }

    /// <summary>
    /// Registers a custom <see cref="IIconProvider"/>.
    /// Providers are consulted in registration order after the direct registry.
    /// </summary>
    public static void RegisterProvider(IIconProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_lock)
            _providers.Add(provider);
    }

    /// <summary>
    /// Resolves SVG content for the given icon name.
    /// Checks direct registry first, then registered providers in order.
    /// Returns <see langword="null"/> when not found anywhere.
    /// </summary>
    public static string? Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        if (_icons.TryGetValue(name, out var svg))
            return svg;

        foreach (var provider in _providers)
        {
            if (provider.HasIcon(name))
                return provider.GetSvg(name);
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the icon name exists in the direct registry
    /// or in any registered provider.
    /// </summary>
    public static bool Contains(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (_icons.ContainsKey(name))
            return true;

        return _providers.Any(p => p.HasIcon(name));
    }

    /// <summary>
    /// Clears all registered icons and providers.
    /// Intended for use in unit tests only — do not call in production code.
    /// </summary>
    internal static void Reset()
    {
        lock (_lock)
        {
            _icons.Clear();
            _providers.Clear();
        }
    }
}
