namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Provides SVG markup for custom icons by name.
/// Implement this interface to extend TmIcon with your own icon set.
///
/// Register via: <c>IconRegistry.RegisterProvider(new MyIconProvider());</c>
/// </summary>
public interface IIconProvider
{
    /// <summary>
    /// Returns inline SVG path content (contents of the &lt;svg&gt; tag) for the given icon name,
    /// or <see langword="null"/> if this provider does not handle the name.
    /// </summary>
    string? GetSvg(string iconName);

    /// <summary>
    /// Returns <see langword="true"/> if this provider knows the given icon name.
    /// Called before <see cref="GetSvg"/> to avoid unnecessary allocations.
    /// </summary>
    bool HasIcon(string iconName);
}
