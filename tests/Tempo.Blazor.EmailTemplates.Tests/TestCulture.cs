using System.Globalization;
using System.Runtime.CompilerServices;

namespace Tempo.Blazor.EmailTemplates.Tests;

/// <summary>
/// Pins the UI culture of the whole test assembly to English. Many assertions here read the SHIPPED
/// English wording (real <c>DefaultTmEmailLocalizer</c> over the embedded JSON), and that localizer follows
/// <see cref="CultureInfo.CurrentUICulture"/> — so on a machine whose OS UI language is e.g. Czech
/// eight tests failed for no product reason. Tests that exercise another language set
/// <c>CurrentUICulture</c> explicitly (and restore it), which still wins over this default.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void PinEnglishUiCulture()
    {
        var en = CultureInfo.GetCultureInfo("en");
        // Default for every thread the runner starts (and the thread-pool threads xUnit runs tests on); no bare assignment of the ambient
        // CurrentUICulture here - CultureAssignmentGuardTests forbids that, and the default covers every test thread.
        CultureInfo.DefaultThreadCurrentUICulture = en;
    }
}
