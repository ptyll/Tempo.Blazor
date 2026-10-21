using System.Globalization;

namespace Tempo.Blazor.EmailTemplates.Tests.Localization;

/// <summary>
/// Runs a test under a pinned ambient culture and restores the previous ones on <see cref="Dispose"/>.
/// <para>
/// The email editor resolves its label tables through <see cref="CultureInfo.CurrentUICulture"/>, so a test
/// that wants Czech or French has to pin the culture. Assigning the ambient culture directly leaks the
/// language into every later test on the same thread (the repository-wide
/// <c>CultureAssignmentGuardTests</c> fails the build for it), hence this scope: the same shape as the core
/// suite's helper, one per test assembly.
/// </para>
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    private CultureScope(CultureInfo culture, CultureInfo uiCulture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
    }

    /// <summary>Pins <paramref name="name"/> as both the current and the current UI culture.</summary>
    /// <param name="name">A culture name, e.g. <c>cs</c> or <c>fr</c>.</param>
    public static CultureScope Use(string name) => Use(CultureInfo.GetCultureInfo(name));

    /// <summary>Pins the given culture(s) until the returned scope is disposed.</summary>
    /// <param name="culture">The current culture.</param>
    /// <param name="uiCulture">The current UI culture; defaults to <paramref name="culture"/>.</param>
    public static CultureScope Use(CultureInfo culture, CultureInfo? uiCulture = null)
        => new(culture, uiCulture ?? culture);

    /// <inheritdoc />
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
