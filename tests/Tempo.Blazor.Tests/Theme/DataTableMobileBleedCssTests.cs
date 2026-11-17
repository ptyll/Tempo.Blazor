using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Fáze 7 bug: under 768 px the wrapper padding shrinks to <c>--tm-space-2</c> while the scroll box
/// keeps its full-width negative margin of <c>-1 * --tm-space-4</c>, so the box stuck out 8 px on
/// each side of the wrapper. One shared variable — <c>--tm-data-table-bleed</c> — now drives BOTH
/// the wrapper padding and the scroll box bleed, so the two can never drift apart again.
/// </summary>
public sealed class DataTableMobileBleedCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Css()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_data-table.css"));
    }

    [Fact]
    public void Wrapper_DeclaresTheBleedVariable_AndPadsWithIt()
    {
        var css = Css();

        Regex.IsMatch(css, @"\.tm-data-table-wrapper\s*\{[^}]*--tm-data-table-bleed:\s*var\(--tm-space-4\)[^}]*padding:\s*var\(--tm-data-table-bleed\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the wrapper padding must be driven by --tm-data-table-bleed so the scroll box bleed can match it");
    }

    [Fact]
    public void ScrollBox_BleedsByTheSharedVariable()
    {
        var css = Css();

        Regex.IsMatch(css, @"\.tm-data-table-scroll\s*\{[^}]*margin:\s*0\s+calc\(-1 \* var\(--tm-data-table-bleed,\s*var\(--tm-space-4\)\)\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the scroll box negative margin must use the same variable as the wrapper padding");
        Regex.IsMatch(css, @"\.tm-data-table-scroll\s*\{[^}]*width:\s*calc\(100% \+ 2 \* var\(--tm-data-table-bleed,\s*var\(--tm-space-4\)\)\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the scroll box width compensation must use the same variable");
    }

    [Fact]
    public void NarrowViewport_ReducesTheBleedVariable_ToMatchTheWrapperPadding()
    {
        var css = Css();

        Regex.IsMatch(css, @"@media \(width < 768px\)\s*\{[^@]*\.tm-data-table-wrapper\s*\{[^}]*--tm-data-table-bleed:\s*var\(--tm-space-2\)[^}]*padding:\s*var\(--tm-data-table-bleed\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the narrow wrapper must shrink the bleed variable, not just its padding, so the scroll box bleeds exactly the wrapper padding");
    }
}
