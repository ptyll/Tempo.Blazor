using System.Globalization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Sets <see cref="CultureInfo.CurrentCulture"/> and <see cref="CultureInfo.CurrentUICulture"/> for
/// the scope and restores both on <see cref="Dispose"/>, so a test that selects a language never
/// leaks it into the tests that run afterwards on the same thread. Use with
/// <c>using var _ = CultureScope.Use("cs");</c>.
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

    public static CultureScope Use(string name) => Use(CultureInfo.GetCultureInfo(name));

    public static CultureScope Use(CultureInfo culture, CultureInfo? uiCulture = null) => new(culture, uiCulture ?? culture);

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
